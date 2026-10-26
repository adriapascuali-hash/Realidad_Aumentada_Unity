using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Unity.WebRTC;
using UnityEngine;
using ARDashboard;

/// <summary>
/// Streams the game view to the webapp as the WebRTC "emisor", following
/// exactly the signaling protocol from
/// https://github.com/dronsEETAC/Tutorial_VideoStreaming
/// (webRTC/webapp/senderWebAppRTC.py): register over a plain WebSocket,
/// then create one RTCPeerConnection + offer per "peticion"/"receptor"
/// (viewer request), with manual/trickle ICE.
///
/// Requires "com.unity.webrtc" and "com.unity.nuget.newtonsoft-json"
/// (added to Packages/manifest.json).
/// </summary>
public class UnityVideoStreamer : MonoBehaviour
{
    // Host y puerto del relay de señalización (webrtc_server.py) se leen de
    // webrtc_server.txt junto al .exe, no de campos del Inspector — ver
    // ReadHostConfig() más abajo.

    [Header("Stream")]
    // Now that the connection goes direct (host-to-host, confirmed via
    // candidate-pair stats after the firewall fix) instead of through the
    // TURN relay, there's a lot more bandwidth/latency headroom than when we
    // had to ration everything for fluidity over a relayed link.
    [Range(320, 3840)] public int captureWidth  = 1920;
    [Range(180, 2160)] public int captureHeight = 1080;
    [Tooltip("How many times per second we grab + encode a frame. Capturing faster than this just burns CPU/GPU on the main thread for frames that get thrown away anyway, which is what was causing the stutter.")]
    public int captureFps = 45;
    public bool autoStart = true;

    [Header("Quality")]
    [Tooltip("Encoder target bitrate. Higher = sharper but needs more bandwidth.")]
    public ulong maxBitrateBps = 15_000_000;
    [Tooltip("Floor the encoder is not allowed to drop below, even if the network looks congested. Keeps it from quietly going soft when bandwidth estimation gets conservative.")]
    public ulong minBitrateBps = 4_000_000;

    class PeerEntry
    {
        public RTCPeerConnection Pc;
        public VideoStreamTrack  Track;
        public RTCDataChannel    Control;
    }

    RenderTexture          _rt;
    RenderTexture          _captureRt;
    int                     _lastScreenW;
    int                     _lastScreenH;
    ClientWebSocket         _ws;
    readonly SemaphoreSlim  _sendLock = new(1, 1);
    CancellationTokenSource _cts;
    Coroutine               _webrtcUpdateRoutine;
    Coroutine               _captureRoutine;
    readonly Dictionary<int, PeerEntry> _peers = new();

    static readonly RTCIceServer[] IceServers =
    {
        new RTCIceServer { urls = new[] { "stun:stun.relay.metered.ca:80" } },
        new RTCIceServer
        {
            urls = new[] { "turn:dronseetac.upc.edu:3478" },
            username = "dronseetac",
            credential = "Mimara00.",
        },
    };

    static UnityVideoStreamer _instance;

    // Se autocrea una única vez al arrancar el juego (mismo patrón que
    // DroneMqttBridge.Bootstrap) — así no depende de que ninguna escena
    // concreta traiga su propio GameObject, y no hay ya ningún duplicado
    // que pueda spawnearse al recargar "inicio" desde el menú de pausa.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("[UnityVideoStreamer]");
        go.AddComponent<UnityVideoStreamer>();
    }

    void Awake()
    {
        // Guarda de seguridad por si alguna escena antigua todavía trae un
        // UnityVideoStreamer propio colocado a mano: solo debe existir un
        // emisor en toda la partida — dos emisores compitiendo por el mismo
        // slot de señalización es lo que congelaba el vídeo.
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
    }

    void Start()
    {
        if (_instance != this) return;

        // Persiste entre escenas para que el streaming no se interrumpa al cambiar de escenario
        DontDestroyOnLoad(gameObject);

        // Without this, Unity throttles/pauses the game loop (and therefore
        // the screen capture + WebRTC tracks) as soon as the game window
        // loses focus or gets minimized — so the webapp would freeze the
        // moment you click away from the game.
        Application.runInBackground = true;

        _webrtcUpdateRoutine = StartCoroutine(WebRTC.Update());

        CreateRenderTextures();

        // Don't touch Camera.main.targetTexture: that would redirect its
        // output away from the screen (causes "Display 1, No cameras
        // rendering"). Instead grab a full screenshot each frame (same as
        // the original JPEG-over-HTTP version did) and blit it into _rt,
        // which is what feeds the WebRTC track.
        _captureRoutine = StartCoroutine(CaptureLoop());

        if (autoStart) StartStreaming();
    }

    void OnDestroy()
    {
        if (_instance == this) _instance = null;
        if (_instance != null && _instance != this) return; // the duplicate that self-destructed in Awake never started anything

        StopStreaming();
        if (_webrtcUpdateRoutine != null) StopCoroutine(_webrtcUpdateRoutine);
        if (_captureRoutine != null) StopCoroutine(_captureRoutine);
        ReleaseRenderTextures();
    }

    void ReleaseRenderTextures()
    {
        if (_rt != null) { _rt.Release(); Destroy(_rt); _rt = null; }
        if (_captureRt != null) { _captureRt.Release(); Destroy(_captureRt); _captureRt = null; }
    }

    // _captureRt must match the screen 1:1 — CaptureScreenshotIntoRenderTexture
    // doesn't reliably rescale into a differently-sized target (that
    // mismatch is what produces a cropped/zoomed-in view). _rt (what the
    // WebRTC track actually reads) is the capped/aspect-preserving size,
    // reached via the Blit in CaptureLoop, which DOES rescale properly.
    //
    // A separate camera with a fixed-size targetTexture was tried to avoid
    // this native-res capture cost, but this project's AR drone background
    // (VideoBackground.cs) is a quad parented specifically to Camera.main
    // — duplicating the camera doesn't reliably reproduce that, so the
    // capture came out blank. ScreenCapture grabs the real composited
    // backbuffer instead, so it always shows exactly what's on screen.
    void CreateRenderTextures()
    {
        ReleaseRenderTextures();

        int screenW = Mathf.Max(Screen.width, 2);
        int screenH = Mathf.Max(Screen.height, 2);
        _captureRt = new RenderTexture(screenW, screenH, 0, RenderTextureFormat.BGRA32);
        _captureRt.Create();

        float aspect = (float)screenW / screenH;
        int w = captureWidth;
        int h = Mathf.RoundToInt(w / aspect);
        if (h > captureHeight)
        {
            h = captureHeight;
            w = Mathf.RoundToInt(h * aspect);
        }
        w = Mathf.Max(w, 2);
        h = Mathf.Max(h, 2);

        _rt = new RenderTexture(w, h, 0, RenderTextureFormat.BGRA32);
        _rt.Create();

        _lastScreenW = Screen.width;
        _lastScreenH = Screen.height;
        Debug.Log($"[VideoStreamer] Captura nativa {screenW}x{screenH} → track {w}x{h}.");
    }

    IEnumerator CaptureLoop()
    {
        var eof = new WaitForEndOfFrame();
        float captureInterval = 1f / Mathf.Max(captureFps, 1);
        float nextCaptureTime = 0f;
        while (true)
        {
            yield return eof;

            // The game can render at 60+ fps while we only need ~24 frames
            // a second for the stream. Capturing+blitting more often than
            // that just burns GPU/CPU on frames the encoder was always
            // going to discard anyway.
            if (Time.unscaledTime < nextCaptureTime) continue;
            nextCaptureTime = Time.unscaledTime + captureInterval;

            // Fullscreen toggles, monitor changes, window resizes, etc. can
            // change Screen.width/height at any moment; recreate to match
            // and force every viewer to reconnect with a track bound to the
            // new texture.
            if (Screen.width != _lastScreenW || Screen.height != _lastScreenH)
            {
                CreateRenderTextures();
                _ws?.Dispose(); // triggers the existing reconnect/backfill logic in RunSignalingLoop
                continue;
            }

            // CaptureScreenshotIntoRenderTexture blits straight into a
            // RenderTexture, with no per-frame allocation at all (unlike
            // CaptureScreenshotAsTexture, which allocates a new Texture2D
            // every frame and was enough GC pressure to stall the main
            // thread at high resolutions).
            ScreenCapture.CaptureScreenshotIntoRenderTexture(_captureRt);

            // Flip vertically while copying into the texture the WebRTC
            // track actually reads from (Y convention mismatch between the
            // screen capture and VideoStreamTrack otherwise shows upside down).
            Graphics.Blit(_captureRt, _rt, new Vector2(1, -1), new Vector2(0, 1));
        }
    }

    public void StartStreaming()
    {
        if (_cts != null) return;
        _cts = new CancellationTokenSource();
        _ = RunSignalingLoop(_cts.Token);
    }

    public void StopStreaming()
    {
        _cts?.Cancel();
        _cts = null;

        foreach (var entry in _peers.Values) ClosePeer(entry);
        _peers.Clear();

        _ws?.Dispose();
        _ws = null;
    }

    async Task RunSignalingLoop(CancellationToken token)
    {
        // La dirección y el puerto del relay se leen de un archivo de texto
        // editable junto al .exe (no valores hardcodeados): así, si la
        // webapp/relay algún día no vive en la misma máquina o cambia de
        // puerto, el usuario solo tiene que editar ese archivo y reiniciar,
        // sin recompilar nada. Ver ReadHostConfig().
        var (host, port) = ReadHostConfig();

        var url = $"ws://{host}:{port}";

        // Keep retrying: the relay (webrtc_server.py) might not be up yet
        // when this starts, or the connection might drop later on.
        while (!token.IsCancellationRequested)
        {
            foreach (var entry in _peers.Values) ClosePeer(entry);
            _peers.Clear();

            _ws = new ClientWebSocket();
            try
            {
                Debug.Log($"[VideoStreamer] Conectando a {url}...");
                await _ws.ConnectAsync(new Uri(url), token);
                await SendJson(new JObject { ["type"] = "registro", ["role"] = "emisor" });
                Debug.Log("[VideoStreamer] Registrado como emisor.");

                var buffer = new byte[16 * 1024];
                while (!token.IsCancellationRequested && _ws.State == WebSocketState.Open)
                {
                    var json = await ReceiveJson(buffer, token);
                    if (json != null) HandleMessage(json);
                }
            }
            catch (OperationCanceledException) { break; /* expected on stop */ }
            catch (Exception e)
            {
                Debug.LogWarning($"[VideoStreamer] Señalización: {e.Message}");
            }

            if (token.IsCancellationRequested) break;
            Debug.Log("[VideoStreamer] Reintentando señalización en 3s...");
            try { await Task.Delay(3000, token); } catch (OperationCanceledException) { break; }
        }
    }

    // ── Dirección y puerto del relay, editables sin recompilar ─────────────
    // Archivo de texto plano junto al .exe (Application.dataPath/.. es la
    // carpeta "<Producto>_Data" en una build de Windows, así que ".." es la
    // carpeta donde vive el propio .exe), formato "host:puerto". Si no existe,
    // se crea con el valor por defecto — el usuario solo tiene que abrirlo y
    // cambiarlo si algún día el relay no vive en la misma máquina que Unity o
    // usa otro puerto. Reemplaza al antiguo descubrimiento del puerto por MQTT
    // (dron/<id>/webrtc_request): un solo sitio editable en vez de dos.
    const string WEBRTC_HOST_FILE    = "webrtc_server.txt";
    const string WEBRTC_HOST_DEFAULT = "127.0.0.1"; // no "localhost": en Windows resuelve a ::1 primero y el relay solo escucha en IPv4
    const int    WEBRTC_PORT_DEFAULT = 8108;

    static (string host, int port) ReadHostConfig()
    {
        try
        {
            string path = Path.Combine(Application.dataPath, "..", WEBRTC_HOST_FILE);
            if (!File.Exists(path))
            {
                File.WriteAllText(path, $"{WEBRTC_HOST_DEFAULT}:{WEBRTC_PORT_DEFAULT}");
                return (WEBRTC_HOST_DEFAULT, WEBRTC_PORT_DEFAULT);
            }

            string line = File.ReadAllText(path).Trim();
            if (string.IsNullOrEmpty(line)) return (WEBRTC_HOST_DEFAULT, WEBRTC_PORT_DEFAULT);

            int sep = line.LastIndexOf(':');
            if (sep < 0 || !int.TryParse(line.Substring(sep + 1), out int port))
                return (line, WEBRTC_PORT_DEFAULT); // solo host en el fichero (formato antiguo): puerto por defecto

            string host = line.Substring(0, sep);
            return (string.IsNullOrEmpty(host) ? WEBRTC_HOST_DEFAULT : host, port);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[VideoStreamer] No se pudo leer/crear '{WEBRTC_HOST_FILE}': {e.Message}. Uso '{WEBRTC_HOST_DEFAULT}:{WEBRTC_PORT_DEFAULT}'.");
            return (WEBRTC_HOST_DEFAULT, WEBRTC_PORT_DEFAULT);
        }
    }

    void HandleMessage(JObject data)
    {
        string type = (string)data["type"];

        if (type == "peticion" || type == "receptor")
        {
            // "peticion" = a viewer just requested the stream now.
            // "receptor" = sent right after our own "registro", backfilling
            // viewers that were already waiting before we connected — so
            // this works no matter which side (game or webapp) starts first.
            int id = (int)data["id"];
            Debug.Log($"[VideoStreamer] Petición del receptor: {id}");
            StartCoroutine(CreateOfferForReceiver(id));
        }
        else if (type == "sdp")
        {
            int id = (int)data["id"];
            if (_peers.TryGetValue(id, out var entry))
            {
                var desc = new RTCSessionDescription
                {
                    type = RTCSdpType.Answer,
                    sdp  = (string)data["sdp"],
                };
                StartCoroutine(ApplyRemoteAnswer(entry.Pc, desc, id));
            }
        }
        else if (type == "ice" && (string)data["role"] == "receptor")
        {
            int id = (int)data["id"];
            if (_peers.TryGetValue(id, out var entry))
            {
                var cand = data["candidate"];
                if (cand != null && cand.Type != JTokenType.Null)
                {
                    var init = new RTCIceCandidateInit
                    {
                        candidate     = (string)cand["candidate"],
                        sdpMid        = (string)cand["sdpMid"],
                        sdpMLineIndex = cand["sdpMLineIndex"] != null ? (int?)(int)cand["sdpMLineIndex"] : null,
                    };
                    entry.Pc.AddIceCandidate(new RTCIceCandidate(init));
                }
            }
        }
        else if (type == "client-disconnect")
        {
            int id = (int)data["id"];
            Debug.Log($"[VideoStreamer] Receptor desconectado: {id}");
            if (_peers.TryGetValue(id, out var entry))
            {
                ClosePeer(entry);
                _peers.Remove(id);
            }
        }
    }

    IEnumerator CreateOfferForReceiver(int id)
    {
        var config = new RTCConfiguration { iceServers = IceServers };
        var pc = new RTCPeerConnection(ref config);

        pc.OnIceConnectionChange = state =>
        {
            Debug.Log($"[VideoStreamer] iceConnectionState (receptor {id}): {state}");
            if (state == RTCIceConnectionState.Connected || state == RTCIceConnectionState.Completed)
                StartCoroutine(LogSelectedCandidatePair(pc, id));
        };

        pc.OnIceCandidate = candidate =>
        {
            Debug.Log(candidate == null
                ? $"[VideoStreamer] Fin de candidatos para receptor {id}"
                : $"[VideoStreamer] Candidato ICE para receptor {id}: {candidate.Candidate}");
            var msg = new JObject
            {
                ["type"] = "ice",
                ["role"] = "emisor",
                ["id"]   = id,
            };
            msg["candidate"] = candidate == null ? null : new JObject
            {
                ["candidate"]     = candidate.Candidate,
                ["sdpMid"]        = candidate.SdpMid,
                ["sdpMLineIndex"] = candidate.SdpMLineIndex,
            };
            _ = SendJson(msg);
        };

        // New track per receiver: sharing one track across clients meant
        // closing one connection killed the track for everybody else.
        var track = new VideoStreamTrack(_rt);
        var sender = pc.AddTrack(track);

        // Canal de datos "control": joystick e instrucciones de la webapp
        // llegan por aquí, directo navegador -> Unity por la misma conexión
        // que el vídeo, en vez de dar la vuelta por Flask + bróker MQTT
        // público (que queda solo como respaldo en la webapp). Tiene que
        // crearse ANTES de CreateOffer para que vaya en la oferta: el
        // navegador es quien responde, y añadirlo después obligaría a
        // renegociar. Fiable y ordenado (por defecto): en una conexión
        // directa no añade retraso apreciable y el "soltar" nunca se pierde.
        var control = pc.CreateDataChannel("control");
        control.OnOpen    = () => Debug.Log($"[VideoStreamer] Canal de control abierto (receptor {id}).");
        control.OnMessage = bytes => DroneMqttBridge.Instance?.HandleControlMessage(
            Encoding.UTF8.GetString(bytes), reply => SendControl(control, reply));
        control.OnClose   = () =>
        {
            Debug.Log($"[VideoStreamer] Canal de control cerrado (receptor {id}): sticks al centro.");
            DroneMqttBridge.Instance?.ReleaseSticks();
        };

        _peers[id] = new PeerEntry { Pc = pc, Track = track, Control = control };

        // Prefer H264/VP9 over the default VP8: at the same bitrate they
        // keep noticeably more detail. Falls back to whatever's available
        // if neither is supported on this platform.
        try
        {
            var transceiver = pc.GetTransceivers().FirstOrDefault(t => t.Sender == sender);
            if (transceiver != null)
            {
                var codecs = RTCRtpSender.GetCapabilities(TrackKind.Video).codecs;
                var ordered = codecs
                    .OrderBy(c => CodecRank(c.mimeType))
                    .ToArray();
                var err = transceiver.SetCodecPreferences(ordered);
                if (err == RTCErrorType.None)
                    Debug.Log("[VideoStreamer] Preferencia de códec: " +
                              string.Join(", ", ordered.Select(c => c.mimeType)));
                else
                    Debug.LogWarning($"[VideoStreamer] SetCodecPreferences: {err}");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[VideoStreamer] No se pudo fijar preferencia de códec: {e.Message}");
        }

        // Default WebRTC bitrate is way too conservative for a sharp game
        // feed — bump it up and stop it from auto-downscaling. Wrapped in
        // try/catch: this is a nice-to-have, it must never break the offer.
        try
        {
            var sendParams = sender.GetParameters();

            if (sendParams.encodings != null)
            {
                foreach (var encoding in sendParams.encodings)
                {
                    encoding.maxBitrate = maxBitrateBps;
                    encoding.minBitrate = minBitrateBps;
                    encoding.scaleResolutionDownBy = 1.0;
                    encoding.maxFramerate = (uint)captureFps;
                }
                sender.SetParameters(sendParams);
                Debug.Log($"[VideoStreamer] Bitrate configurado: {minBitrateBps}-{maxBitrateBps} bps ({sendParams.encodings.Length} encoding(s)).");
            }
            else
            {
                Debug.LogWarning("[VideoStreamer] sender.GetParameters().encodings es null; se omite el ajuste de bitrate.");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[VideoStreamer] No se pudo ajustar el bitrate: {e.Message}");
        }

        var offerOp = pc.CreateOffer();
        yield return offerOp;
        if (offerOp.IsError) { Debug.LogError($"[VideoStreamer] CreateOffer: {offerOp.Error.message}"); yield break; }

        var offerDesc = offerOp.Desc;
        var setLocalOp = pc.SetLocalDescription(ref offerDesc);
        yield return setLocalOp;
        if (setLocalOp.IsError) { Debug.LogError($"[VideoStreamer] SetLocalDescription: {setLocalOp.Error.message}"); yield break; }

        _ = SendJson(new JObject
        {
            ["type"]     = "sdp",
            ["role"]     = "emisor",
            ["id"]       = id,
            ["sdp"]      = pc.LocalDescription.sdp,
            ["sdp_type"] = "offer",
        });
        Debug.Log($"[VideoStreamer] Oferta enviada al receptor {id}.");
    }

    // Lower number = more preferred. Anything not listed keeps its original
    // relative order (OrderBy is stable) after the ones we do care about.
    static int CodecRank(string mimeType)
    {
        if (string.IsNullOrEmpty(mimeType)) return 99;
        var m = mimeType.ToLowerInvariant();
        if (m.Contains("h264")) return 0;
        if (m.Contains("vp9"))  return 1;
        if (m.Contains("vp8"))  return 2;
        return 3;
    }

    // Diagnostic only: tells us whether the connection ended up direct
    // (host/srflx — near-zero extra latency) or relayed through the TURN
    // server (relay — adds a real network hop, which is the most likely
    // explanation for a constant, non-growing delay).
    IEnumerator LogSelectedCandidatePair(RTCPeerConnection pc, int id)
    {
        var op = pc.GetStats();
        yield return op;
        if (op.IsError || op.Value == null) yield break;

        using (var report = op.Value)
        {
            foreach (var stat in report.Stats.Values)
            {
                if (stat is RTCIceCandidatePairStats pairStats && pairStats.nominated)
                {
                    string localType = "?", remoteType = "?";
                    if (report.TryGetValue(pairStats.localCandidateId, out var local) && local is RTCIceCandidateStats l)
                        localType = l.candidateType;
                    if (report.TryGetValue(pairStats.remoteCandidateId, out var remote) && remote is RTCIceCandidateStats r)
                        remoteType = r.candidateType;

                    Debug.Log($"[VideoStreamer] Par de candidatos seleccionado (receptor {id}): local={localType}, remoto={remoteType}" +
                              (localType == "relay" || remoteType == "relay" ? " -> VA POR TURN RELAY" : " -> CONEXIÓN DIRECTA"));
                }
            }
        }
    }

    IEnumerator ApplyRemoteAnswer(RTCPeerConnection pc, RTCSessionDescription desc, int id)
    {
        var op = pc.SetRemoteDescription(ref desc);
        yield return op;
        if (op.IsError)
            Debug.LogError($"[VideoStreamer] SetRemoteDescription receptor {id}: {op.Error.message}");
        else
            Debug.Log($"[VideoStreamer] Aceptación del receptor {id} aplicada.");
    }

    // Respuestas (errores de instrucciones) de vuelta al navegador. Se llama
    // desde el hilo de fondo de DroneMqttBridge.SafeDispatch; el canal puede
    // haberse cerrado o liberado entretanto (ReadyState lanza tras Dispose).
    static void SendControl(RTCDataChannel channel, string message)
    {
        try
        {
            if (channel.ReadyState == RTCDataChannelState.Open) channel.Send(message);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[VideoStreamer] No se pudo responder por el canal de control: {e.Message}");
        }
    }

    void ClosePeer(PeerEntry entry)
    {
        if (entry.Control != null)
        {
            // Sin callbacks: OnClose dispararía después del Dispose. Los sticks
            // se centran aquí mismo.
            entry.Control.OnMessage = null;
            entry.Control.OnClose   = null;
            entry.Control.Dispose();
            DroneMqttBridge.Instance?.ReleaseSticks();
        }
        entry.Track?.Dispose();
        entry.Pc?.Close();
        entry.Pc?.Dispose();
    }

    async Task SendJson(JObject obj)
    {
        if (_ws == null || _ws.State != WebSocketState.Open) return;
        var bytes = Encoding.UTF8.GetBytes(obj.ToString(Newtonsoft.Json.Formatting.None));

        // ClientWebSocket.SendAsync doesn't support concurrent calls on the
        // same instance. With several receivers gathering ICE candidates at
        // once, multiple OnIceCandidate callbacks were firing SendJson in
        // parallel, which threw and silently corrupted the socket — that
        // was the real cause of the connection dying every few seconds.
        await _sendLock.WaitAsync();
        try
        {
            if (_ws.State == WebSocketState.Open)
                await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    async Task<JObject> ReceiveJson(byte[] buffer, CancellationToken token)
    {
        using var ms = new System.IO.MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await _ws.ReceiveAsync(buffer, token);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        var text = Encoding.UTF8.GetString(ms.ToArray());
        return JObject.Parse(text);
    }
}
