using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Client.Options;
using Newtonsoft.Json.Linq;

namespace ARDashboard
{
    /// <summary>
    /// Cliente MQTT persistente, activo desde que arranca el juego (no
    /// depende de que haya cargada una escena de vuelo concreta) y que
    /// sobrevive a los cambios de escena — mismo patrón que
    /// ScenarioReporter.cs/UnityVideoStreamer.cs (DontDestroyOnLoad).
    ///
    /// Es el ÚNICO dueño de la conexión MAVLink real (csDronLink.Dron), tanto
    /// en modo SITL como en modo Antena (puerto COM): la abre, la cierra, y es
    /// quien recibe la telemetría directamente del callback de csDronLink.dll.
    /// DronControl (la representación visual del dron en cada escena) ya no
    /// conecta nada por sí mismo — cada frame LEE la telemetría de aquí
    /// (GetTelemetrySnapshot) para mover su transform. Al ser este puente
    /// persistente entre escenas, la conexión ahora sí sobrevive a los
    /// cambios de escena de verdad (antes se cerraba y reabría cada vez,
    /// porque vivía en el DronControl de la escena, que se destruye al
    /// cambiar de escena).
    ///
    /// Sustituye a lo que antes hacía app.py hablando directamente con
    /// pymavlink: publica telemetría/estado/puertos y ejecuta las
    /// instrucciones (conectar, armar, modo, despegar, aterrizar, RTL, RC,
    /// bloqueo de throttle) que la webapp publica en el mismo bróker MQTT
    /// público.
    /// </summary>
    public class DroneMqttBridge : MonoBehaviour
    {
        [Header("MQTT")]
        public string brokerHost = "broker.hivemq.com";
        public int    brokerPort = 8000; // WebSocket
        public string droneId    = "1";

        [Header("Telemetría")]
        [Tooltip("Frecuencia de publicación de telemetría por MQTT (Hz)")]
        public float telemetryPublishHz = 5f;
        [Tooltip("Frecuencia de recepción de datos del dron (Hz)")]
        [Range(1, 100)]
        public int telemetryFreq = 20;

        [Header("Debug")]
        public bool showDebugLogs = false;

        static DroneMqttBridge _instance;
        public static DroneMqttBridge Instance => _instance;

        // Arranca una única vez al iniciar el juego (independiente de qué
        // escena cargue primero) y persiste entre cambios de escena.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            var go = new GameObject("[DroneMqttBridge]");
            go.AddComponent<DroneMqttBridge>();
        }

        string _topicTelemetry, _topicStatus, _topicPorts, _topicError, _topicAvailable, _topicWeaponMode, _topicScenario, _cmdPrefix, _cmdFilter;
        IMqttClient _client;
        bool        _isQuitting;

        // ── Conexión MAVLink (antes vivía en DronControl) ────────────────────────
        csDronLink.Dron miDron;
        FieldInfo       _messageHandlerField;

        public bool   IsConnected       { get; private set; }
        public string CurrentMode       { get; private set; } // "sitl" | "real"
        public string CurrentConnString { get; private set; }

        readonly object _lock = new object();
        float _lat, _lon, _alt, _heading;
        float _roll, _pitch;
        bool  _hasData;

        public struct TelemetrySnapshot
        {
            public bool  hasData;
            public float lat, lon, alt, heading, roll, pitch;
        }

        // ── Telemetría que el callback normal de csDronLink.dll no expone ───────
        // (groundspeed/armado/modo solo viajan en VFR_HUD/HEARTBEAT, no en la
        // lista de campos que entrega EnviarDatosTelemetria). Se obtienen
        // enganchándose al MessageHandler interno — ver HookExtraTelemetry().
        float _groundspeed;
        bool  _armed;
        uint  _customMode;

        // ── Estado RC (equivalente a rc_values + rc_override_loop de app.py) ──
        // volatile: se escriben desde el hilo de red MQTT (ApplyRcCommand) y
        // se leen desde la corrutina RcOverrideLoop en el hilo principal.
        volatile float _rcYaw, _rcThrottle, _rcRoll, _rcPitch;
        volatile bool  _throttleLocked;

        // Hombre muerto: mientras un joystick está cogido la webapp reenvía
        // su posición ~15 veces por segundo, aunque el dedo esté quieto. Si
        // deja de llegar RC durante RC_TIMEOUT_MS con algún stick fuera del
        // centro (pestaña cerrada, red caída, "soltar" perdido...), se centran
        // en vez de dejar al dron haciendo lo último que se recibió.
        const long RC_TIMEOUT_MS = 1000;
        static readonly System.Diagnostics.Stopwatch _rcClock = System.Diagnostics.Stopwatch.StartNew();
        long _lastRcMs;

        // ── Instrucciones del shooter (dron/<id>/cmd/fire|cycle_weapon|cycle_target) ──
        // Llegan por el hilo de red MQTT; se guardan aquí y las consume
        // ShooterController.Update() en el hilo principal (mismo patrón que
        // el resto de estado compartido con _lock).
        readonly object _shooterLock = new object();
        bool _firePending, _cycleWeaponPending, _cycleTargetPending;

        public bool ConsumeFire()
        {
            lock (_shooterLock) { bool v = _firePending; _firePending = false; return v; }
        }

        public bool ConsumeCycleWeapon()
        {
            lock (_shooterLock) { bool v = _cycleWeaponPending; _cycleWeaponPending = false; return v; }
        }

        public bool ConsumeCycleTarget()
        {
            lock (_shooterLock) { bool v = _cycleTargetPending; _cycleTargetPending = false; return v; }
        }

        /// Avisa a la webapp de qué arma tiene seleccionada el shooter ahora
        /// mismo (dron/<id>/weapon_mode), en vez del antiguo POST HTTP directo.
        public void PublishWeaponMode(int mode) =>
            _ = PublishAsync(_topicWeaponMode, new JObject { ["mode"] = mode }.ToString(Newtonsoft.Json.Formatting.None));

        /// Avisa a la webapp de qué escena está activa ahora mismo
        /// (dron/<id>/scenario), en vez del antiguo POST HTTP directo que
        /// usaba ScenarioReporter.cs.
        public void PublishScenario(string sceneName) =>
            _ = PublishAsync(_topicScenario, new JObject { ["name"] = sceneName }.ToString(Newtonsoft.Json.Formatting.None));

        // Mientras está a true, RcOverrideLoop manda throttle al mínimo pase
        // lo que pase (joystick o throttle_lock) — ArduPilot rechaza el
        // armado con "PreArm: Throttle too high" si no ve el throttle abajo
        // del todo justo antes. Ver RunArmSequence().
        bool _armingThrottleOverride;

        void Awake()
        {
            // Por si alguna escena antigua todavía trae un DroneMqttBridge
            // propio, o Bootstrap() se disparase más de una vez: solo debe
            // existir un puente MQTT en toda la partida.
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void Start()
        {
            if (_instance != this) return;

            _topicTelemetry  = $"dron/{droneId}/telemetry";
            _topicStatus     = $"dron/{droneId}/status";
            _topicPorts      = $"dron/{droneId}/ports";
            _topicError      = $"dron/{droneId}/error";
            _topicAvailable  = $"dron/{droneId}/available";
            _topicWeaponMode = $"dron/{droneId}/weapon_mode";
            _topicScenario   = $"dron/{droneId}/scenario";
            _cmdPrefix       = $"dron/{droneId}/cmd/";
            _cmdFilter       = _cmdPrefix + "+";

            _messageHandlerField = typeof(csDronLink.Dron).GetField(
                "messageHandler", BindingFlags.NonPublic | BindingFlags.Instance);

            Task.Run(ConnectToBrokerAsync);
            StartCoroutine(TelemetryPublishLoop());
            StartCoroutine(RcOverrideLoop());
            StartCoroutine(StatusAndPortsLoop());
            StartCoroutine(ConfigWatchLoop());
        }

        // Dispara un "connect" con lo que diga GameConfig ahora mismo. SITL no es
        // un recurso físico exclusivo: conectarse directamente es inofensivo y
        // deja la app usable sin depender de la webapp. El puerto COM real solo
        // se autoconecta si ya hay uno guardado (menú local o una sesión
        // anterior); si no, espera al primer "connect" explícito de la webapp.
        // Es una llamada local a Dispatch(), no un mensaje MQTT de verdad — no
        // hace falta esperar al bróker para nada de esto.
        void TriggerAutoConnect()
        {
            if (GameConfig.DroneConnection == GameConfig.DroneConnectionType.SITL)
            {
                Task.Run(() => SafeDispatch("connect", new JObject { ["mode"] = "sitl" }));
            }
            else if (!string.IsNullOrEmpty(GameConfig.SerialPort))
            {
                Task.Run(() => SafeDispatch("connect", new JObject
                {
                    ["mode"] = "real",
                    ["port"] = GameConfig.SerialPort,
                    ["baud"] = GameConfig.Baudrate,
                }));
            }
        }

        // Vigila GameConfig directamente (no la escena) y vuelve a conectar en
        // cuanto el modo o el puerto cambian — p. ej. el usuario pasa de Antena a
        // SITL, o elige otro puerto COM, desde el menú local. Antes este
        // re-chequeo pasaba "gratis" porque DronControl.Start() se ejecutaba de
        // nuevo en cada cambio de escena (se recreaba junto a la escena); ahora
        // que la conexión es persistente y ya no depende de la escena, hace
        // falta vigilar el propio GameConfig explícitamente. Connect() ya
        // ignora una orden que repita el mismo modo+puerto ya conectado, así
        // que no hay riesgo de reconectar de más si no ha cambiado nada.
        IEnumerator ConfigWatchLoop()
        {
            TriggerAutoConnect();
            var lastMode = GameConfig.DroneConnection;
            var lastPort = GameConfig.SerialPort;

            var wait = new WaitForSeconds(1f);
            while (!_isQuitting)
            {
                if (GameConfig.DroneConnection != lastMode || GameConfig.SerialPort != lastPort)
                {
                    lastMode = GameConfig.DroneConnection;
                    lastPort = GameConfig.SerialPort;
                    TriggerAutoConnect();
                }
                yield return wait;
            }
        }

        void OnApplicationQuit() => _isQuitting = true;
        void OnDestroy()
        {
            if (_instance != this) return; // el duplicado que se autodestruyó en Awake no llegó a arrancar nada
            _isQuitting = true;
            Task.Run(DisconnectAsync);
            if (miDron != null) { try { miDron.Desconectar(); } catch { } }
        }

        // ── Conexión MQTT ─────────────────────────────────────────────────────────
        async Task ConnectToBrokerAsync()
        {
            try
            {
                var factory = new MqttFactory();
                _client = factory.CreateMqttClient();

                // Last Will: si esta conexión se cae de forma NO limpia (Unity se
                // cierra a lo bruto, se cae la red, se mata el proceso...) el propio
                // bróker publica esto en nuestro lugar, para que la webapp sepa que
                // el puente dejó de estar vivo y no siga confiando en el último
                // "dron/<id>/ports" retenido (que quedaría desactualizado para
                // siempre si no fuera por esto).
                var willMessage = new MqttApplicationMessageBuilder()
                    .WithTopic(_topicAvailable)
                    .WithPayload(new JObject { ["online"] = false }.ToString(Newtonsoft.Json.Formatting.None))
                    .WithRetainFlag(true)
                    .Build();

                var options = new MqttClientOptionsBuilder()
                    .WithClientId("UnityDroneBridge_" + Guid.NewGuid().ToString("N").Substring(0, 6))
                    .WithWebSocketServer($"ws://{brokerHost}:{brokerPort}/mqtt")
                    .WithCleanSession()
                    .WithWillMessage(willMessage)
                    .Build();

                _client.UseApplicationMessageReceivedHandler(e =>
                    OnMessageReceived(e.ApplicationMessage.Topic,
                        System.Text.Encoding.UTF8.GetString(e.ApplicationMessage.Payload ?? Array.Empty<byte>())));

                _client.UseConnectedHandler(async e =>
                {
                    Debug.Log($"[DroneMqttBridge] Conectado a ws://{brokerHost}:{brokerPort}");
                    // QoS 1: el bróker entrega con min(QoS publicación, QoS
                    // suscripción), así que sin esto las órdenes que la webapp
                    // manda con QoS 1 (arm, land, rtl...) llegarían igual a QoS 0.
                    await _client.SubscribeAsync(
                        new MQTTnet.Client.Subscribing.MqttClientSubscribeOptionsBuilder()
                            .WithTopicFilter(_cmdFilter, MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                            .Build());
                    // "Birth message": avisa de que el puente está vivo AHORA, con el
                    // mismo topic/formato que el Last Will de arriba (pero online=true).
                    _ = PublishAsync(_topicAvailable,
                        new JObject { ["online"] = true }.ToString(Newtonsoft.Json.Formatting.None), retain: true);
                });

                _client.UseDisconnectedHandler(async e =>
                {
                    if (_isQuitting) return;
                    Debug.LogWarning("[DroneMqttBridge] Desconectado del bróker. Reintentando en 5s...");
                    await Task.Delay(TimeSpan.FromSeconds(5));
                    if (_isQuitting) return;
                    try { await _client.ConnectAsync(options); }
                    catch (Exception ex) { Debug.LogError($"[DroneMqttBridge] Reintento fallido: {ex.Message}"); }
                });

                await _client.ConnectAsync(options);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DroneMqttBridge] Error de conexión MQTT: {ex.Message}");
            }
        }

        async Task DisconnectAsync()
        {
            if (_client == null || !_client.IsConnected) return;
            // Un DisconnectAsync() limpio NO dispara el Last Will (eso solo pasa en
            // desconexiones no controladas) — así que aquí se publica el mismo
            // "offline" a mano antes de cerrar, para que también quede marcado al
            // cerrar Unity normalmente, no solo si se cae de golpe.
            try
            {
                await PublishAsync(_topicAvailable,
                    new JObject { ["online"] = false }.ToString(Newtonsoft.Json.Formatting.None), retain: true);
            }
            catch { }
            await _client.DisconnectAsync();
        }

        async Task PublishAsync(string topic, string json, bool retain = false)
        {
            if (_client == null || !_client.IsConnected) return;
            var msg = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(json)
                .WithAtMostOnceQoS()
                .WithRetainFlag(retain)
                .Build();
            try { await _client.PublishAsync(msg); }
            catch (Exception ex) { Debug.LogWarning($"[DroneMqttBridge] Publish {topic}: {ex.Message}"); }
        }

        void PublishError(string message) =>
            _ = PublishAsync(_topicError, new JObject { ["message"] = message }.ToString(Newtonsoft.Json.Formatting.None));

        // ── Instrucciones entrantes (dron/<id>/cmd/<accion>) ─────────────────────
        void OnMessageReceived(string topic, string payload)
        {
            if (!topic.StartsWith(_cmdPrefix)) return;
            string action = topic.Substring(_cmdPrefix.Length);

            JObject data;
            try { data = string.IsNullOrEmpty(payload) ? new JObject() : JObject.Parse(payload); }
            catch { data = new JObject(); }

            // RC y throttle_lock solo actualizan campos que lee RcOverrideLoop:
            // se aplican aquí mismo, en el orden en que llegan. Con Task.Run el
            // thread pool podía ejecutar el "soltar" (0,0) antes que un "move"
            // anterior y dejar el stick clavado en un valor viejo.
            if (action == "rc" || action == "throttle_lock")
            {
                try { ApplyRcCommand(action, data); }
                catch (Exception ex) { Debug.LogWarning($"[DroneMqttBridge] {action} inválido: {ex.Message}"); }
                return;
            }

            Task.Run(() => SafeDispatch(action, data));
        }

        // Sin dron conectado no es un error: RcOverrideLoop simplemente no
        // envía nada hasta que haya conexión. (Antes cada mensaje RC sin dron
        // publicaba un error en dron/<id>/error, decenas por segundo.)
        void ApplyRcCommand(string action, JObject data)
        {
            if (action == "throttle_lock")
            {
                _throttleLocked = data["locked"]?.ToObject<bool>() ?? false;
                return;
            }

            string id = (string)data["id"];
            float  x  = data["x"]?.ToObject<float>() ?? 0f;
            float  y  = data["y"]?.ToObject<float>() ?? 0f;
            if (id == "left") { _rcYaw = x; _rcThrottle = y; }
            else              { _rcRoll = x; _rcPitch = -y; }
            Interlocked.Exchange(ref _lastRcMs, _rcClock.ElapsedMilliseconds);
        }

        /// Mensajes del canal de datos WebRTC "control" (UnityVideoStreamer):
        /// joystick e instrucciones llegan directo del navegador, sin pasar
        /// por Flask ni por el bróker público (que queda como respaldo). Mismo
        /// contenido que en MQTT pero con la acción dentro del JSON en vez de
        /// en el topic: {"action":"rc","id":"left","x":0.5,"y":0},
        /// {"action":"takeoff","meters":5}... Si una instrucción falla, el
        /// error vuelve por el mismo canal con "reply" en vez de por MQTT.
        public void HandleControlMessage(string json, Action<string> reply = null)
        {
            JObject data;
            try { data = JObject.Parse(json); }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DroneMqttBridge] Mensaje WebRTC inválido: {ex.Message}");
                return;
            }

            string action = (string)data["action"];
            if (string.IsNullOrEmpty(action)) return;

            // Igual que en OnMessageReceived: RC en orden y en el momento, el
            // resto en segundo plano porque toca csDronLink.dll y puede bloquear.
            if (action == "rc" || action == "throttle_lock")
            {
                try { ApplyRcCommand(action, data); }
                catch (Exception ex) { Debug.LogWarning($"[DroneMqttBridge] {action} inválido: {ex.Message}"); }
                return;
            }

            Task.Run(() => SafeDispatch(action, data, reply));
        }

        /// Sticks al centro. Lo usa UnityVideoStreamer al cerrarse el canal
        /// WebRTC, y RcOverrideLoop si deja de llegar RC (ver RC_TIMEOUT_MS).
        public void ReleaseSticks()
        {
            _rcYaw = 0f; _rcThrottle = 0f; _rcRoll = 0f; _rcPitch = 0f;
        }

        // Todo lo que toca csDronLink.dll corre en un hilo de fondo, igual que
        // hacía app.py con pymavlink: no bloquea ni el hilo de red MQTT ni el
        // hilo principal de Unity. Usado tanto por los comandos que llegan por
        // MQTT como por la autoconexión local de Start() y el canal WebRTC.
        // "reply" (solo si la orden vino por WebRTC) devuelve el error por ese
        // mismo canal; si no, se publica en dron/<id>/error como siempre.
        void SafeDispatch(string action, JObject data, Action<string> reply = null)
        {
            try { Dispatch(action, data); }
            catch (Exception ex)
            {
                Debug.LogError($"[DroneMqttBridge] Error ejecutando '{action}': {ex.Message}");
                string message = $"{action}: {ex.Message}";
                if (reply != null)
                    reply(new JObject { ["type"] = "error", ["message"] = message }.ToString(Newtonsoft.Json.Formatting.None));
                else
                    PublishError(message);
            }
        }

        void Dispatch(string action, JObject data)
        {
            // El shooter no necesita ninguna conexión MAVLink activa.
            bool needsDrone = action != "connect" && action != "fire"
                            && action != "cycle_weapon" && action != "cycle_target";
            if (needsDrone && miDron == null)
                throw new InvalidOperationException("No hay ninguna conexión activa con el dron.");

            switch (action)
            {
                case "fire":
                    lock (_shooterLock) { _firePending = true; }
                    break;

                case "cycle_weapon":
                    lock (_shooterLock) { _cycleWeaponPending = true; }
                    break;

                case "cycle_target":
                    lock (_shooterLock) { _cycleTargetPending = true; }
                    break;

                case "connect":
                    string mode = (string)data["mode"];
                    string port = (string)data["port"];
                    int    baud = data["baud"]?.ToObject<int>() ?? 57600;
                    string conn = mode == "sitl" ? GameConfig.GetConnectionString() : port;
                    Connect(mode, conn, baud);
                    break;

                case "arm":
                    bool force = data["force"]?.ToObject<bool>() ?? false;
                    RunArmSequence(() => Arm(force));
                    break;

                case "set_mode":
                    SetModeGeneric((string)data["mode"]);
                    break;

                case "takeoff":
                    // Despegar() arma internamente antes de subir, así que
                    // necesita la misma protección de throttle que "arm".
                    int meters = data["meters"]?.ToObject<int>() ?? 5;
                    RunArmSequence(() => Takeoff(meters));
                    break;

                case "land":
                    Aterrizar();
                    break;

                case "rtl":
                    Rtl();
                    break;

                // "rc" y "throttle_lock" no pasan por aquí: ver ApplyRcCommand().

                default:
                    Debug.LogWarning($"[DroneMqttBridge] Instrucción desconocida: {action}");
                    break;
            }
        }

        // ── Conexión MAVLink (antes DronControl.Connect) ─────────────────────────
        /// Conecta (o reconecta) al SITL o al puerto COM real. Único punto de
        /// entrada tanto para la autoconexión de Start() como para la
        /// instrucción "cmd/connect" que llega por MQTT desde la webapp.
        void Connect(string mode, string portOrConnString, int baud)
        {
            // Ya conectado a este mismo modo+puerto: ignorar, para no forzar un
            // ciclo de desconectar/reconectar innecesario (y el hueco de
            // "Acceso denegado" que eso implica) cuando la orden reenvía una
            // conexión que ya está viva y sin cambios.
            if (IsConnected && CurrentMode == mode && CurrentConnString == portOrConnString)
            {
                Debug.Log($"[DroneMqttBridge] Ya conectado ({mode}: {portOrConnString}). Se ignora la orden de conectar.");
                return;
            }

            if (miDron != null)
            {
                var old = miDron;
                miDron = null;
                try { old.Desconectar(); } catch { }
            }

            miDron = new csDronLink.Dron(1);
            try
            {
                if (mode == "sitl")
                    miDron.Conectar(modo: "simulacion", conector: portOrConnString, freq: telemetryFreq);
                else
                    miDron.Conectar(modo: "produccion", conector: portOrConnString, freq: telemetryFreq);

                miDron.EnviarDatosTelemetria(OnTelemetria);
                HookExtraTelemetry(miDron);
                IsConnected = true;
                CurrentMode = mode;
                CurrentConnString = portOrConnString;
                Debug.Log($"[DroneMqttBridge] Conectado ({mode}: {portOrConnString}). Suscrito a telemetría.");
            }
            catch (Exception e)
            {
                IsConnected = false;
                CurrentConnString = null;
                Debug.LogError($"[DroneMqttBridge] Error al conectar ({mode}: {portOrConnString}): {e.Message}");
                throw;
            }
        }

        void OnTelemetria(byte source, List<(string nombre, float valor)> campos)
        {
            if (campos == null || campos.Count == 0) return;

            float lat     = campos.Find(t => t.nombre == "Lat").valor     / 1e7f;
            float lon     = campos.Find(t => t.nombre == "Lon").valor     / 1e7f;
            float alt     = campos.Find(t => t.nombre == "Alt").valor;
            float heading = campos.Find(t => t.nombre == "Heading").valor / 1e2f;
            float roll    = campos.Find(t => t.nombre == "RollDeg").valor;
            float pitch   = campos.Find(t => t.nombre == "PitchDeg").valor;

            if (showDebugLogs)
                Debug.Log($"[DroneMqttBridge] Lat:{lat:F6} Lon:{lon:F6} Alt:{alt:F2} Hdg:{heading:F1}°");

            lock (_lock)
            {
                _lat = lat; _lon = lon; _alt = alt; _heading = heading;
                _roll = roll; _pitch = pitch;
                _hasData = true;
            }
        }

        void HookExtraTelemetry(csDronLink.Dron dron)
        {
            try
            {
                if (_messageHandlerField.GetValue(dron) is csDronLink.MessageHandler handler)
                {
                    handler.RegisterHandler("VFR_HUD", msg =>
                    {
                        var d = (MAVLink.mavlink_vfr_hud_t)msg.data;
                        _groundspeed = d.groundspeed;
                    });
                    handler.RegisterHandler("HEARTBEAT", msg =>
                    {
                        var d = (MAVLink.mavlink_heartbeat_t)msg.data;
                        _armed      = (d.base_mode & (byte)MAVLink.MAV_MODE_FLAG.SAFETY_ARMED) != 0;
                        _customMode = d.custom_mode;
                    });
                    Debug.Log("[DroneMqttBridge] Enganchado a VFR_HUD/HEARTBEAT (velocidad/armado/modo).");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DroneMqttBridge] No se pudo enganchar telemetría extra: {ex.Message}");
            }
        }

        public TelemetrySnapshot GetTelemetrySnapshot()
        {
            lock (_lock)
            {
                return new TelemetrySnapshot
                {
                    hasData = _hasData, lat = _lat, lon = _lon, alt = _alt,
                    heading = _heading, roll = _roll, pitch = _pitch,
                };
            }
        }

        public (float lat, float lon, float alt) GetPosition()
        {
            lock (_lock) { return (_lat, _lon, _alt); }
        }

        /// Devuelve la instancia interna del dron para que otros componentes
        /// (geofence) puedan reutilizar la conexión ya establecida.
        public csDronLink.Dron GetDron() => miDron;

        // ── Comandos de vuelo (antes en DronControl) ─────────────────────────────
        public void ModoGuiado()             => miDron?.PonModoGuiado();
        public void ModoLoiter()             => miDron?.PonModoLoiter();
        public void Reboot()                 => miDron?.Reboot();
        public void CambiarVelocidad(int ms) => miDron?.CambiaVelocidad(ms);

        public void IrA(float lat, float lon, float altMetros,
                        bool relativo = false,
                        Action<byte, object> onDone = null, object ctx = null)
            => miDron?.IrAlPunto(lat, lon, altMetros, relativo, onDone, ctx);

        public void Aterrizar(Action<byte, object> onDone = null, object ctx = null)
            => miDron?._Aterrizar(onDone, ctx);

        public void EnviarRC(ushort roll, ushort pitch, ushort throttle, ushort yaw)
            => miDron?.SendRC(roll, pitch, throttle, yaw);

        // ── Instrucciones que csDronLink.dll no expone (ver ARDashboard.RawMavlinkCommander) ─
        /// Arma o desarma. Con force=true salta ARMING_CHECK temporalmente
        /// (bloquea el hilo unos ~450ms, llamar desde un hilo de fondo).
        public void Arm(bool force = false)
        {
            if (miDron == null) return;
            if (force) RawMavlinkCommander.ForceArm(miDron);
            else       RawMavlinkCommander.Arm(miDron, true);
        }

        public void Disarm()
        {
            if (miDron != null) RawMavlinkCommander.Arm(miDron, false);
        }

        /// Cambia de modo por nombre (ALTHOLD, LOITER, GUIDED, STABILIZE, AUTO, RTL, LAND).
        public void SetModeGeneric(string modeName)
        {
            if (miDron == null) return;
            var m = modeName?.ToUpperInvariant();
            if (m == "GUIDED")      miDron.PonModoGuiado();
            else if (m == "LOITER") miDron.PonModoLoiter();
            else                    RawMavlinkCommander.SetMode(miDron, modeName);
        }

        public void Takeoff(int meters, Action<byte, object> onDone = null, object ctx = null)
            => miDron?.Despegar(meters, bloquear: false, onDone, ctx);

        public void Rtl(Action<byte, object> onDone = null, object ctx = null)
            => miDron?.RTL(bloquear: false, onDone, ctx);

        // ── Geofence ──────────────────────────────────────────────────────────────
        public void EstableceEscenario(List<List<(float lat, float lon)>> scenario, int timeoutMs = -1)
            => miDron?.EstableceEscenario(scenario, timeoutMs: timeoutMs);

        public void BorraEscenario()
            => miDron?.BorraEscenario();

        // ── RC override a 10Hz (equivalente a rc_override_loop de app.py) ────────
        static ushort Pwm(float v) => (ushort)Mathf.Clamp(1500f + v * 500f, 1000f, 2000f);

        IEnumerator RcOverrideLoop()
        {
            var wait = new WaitForSeconds(0.1f);
            while (!_isQuitting)
            {
                bool sticksOut = _rcYaw != 0f || _rcThrottle != 0f || _rcRoll != 0f || _rcPitch != 0f;
                if (sticksOut && _rcClock.ElapsedMilliseconds - Interlocked.Read(ref _lastRcMs) > RC_TIMEOUT_MS)
                {
                    Debug.LogWarning("[DroneMqttBridge] Sin RC desde hace más de 1s: sticks al centro.");
                    ReleaseSticks();
                }

                if (IsConnected)
                {
                    float throttle = _armingThrottleOverride ? -1f : (_throttleLocked ? 0f : _rcThrottle);
                    EnviarRC(Pwm(_rcRoll), Pwm(_rcPitch), Pwm(throttle), Pwm(_rcYaw));
                }
                yield return wait;
            }
        }

        // ── Secuencia segura de armado (equivalente a arm() en app.py) ───────────
        // ArduPilot comprueba el throttle del último RC recibido justo antes de
        // armar; con el valor "de reposo" (centro de joystick / throttle_lock a
        // 1500) siempre rechaza con "PreArm: Throttle too high". Se fuerza el
        // throttle al mínimo unos ciclos de RcOverrideLoop (10Hz) antes de
        // ejecutar el armado/despegue, y se espera a ver ARMED en el HEARTBEAT
        // (o un timeout) antes de devolver el RC al joystick real — Despegar()
        // arma en un hilo interno de csDronLink.dll, así que el momento exacto
        // del armado no es determinista desde aquí.
        void RunArmSequence(Action action, int maxWaitMs = 5000)
        {
            _armingThrottleOverride = true;
            try
            {
                System.Threading.Thread.Sleep(250);
                action();

                int waited = 0;
                while (!_armed && waited < maxWaitMs)
                {
                    System.Threading.Thread.Sleep(100);
                    waited += 100;
                }
                System.Threading.Thread.Sleep(300);
            }
            finally
            {
                _armingThrottleOverride = false;
            }
        }

        // ── Telemetría saliente ───────────────────────────────────────────────────
        IEnumerator TelemetryPublishLoop()
        {
            var wait = new WaitForSeconds(1f / Mathf.Max(telemetryPublishHz, 0.1f));
            while (!_isQuitting)
            {
                var snap = GetTelemetrySnapshot();
                if (snap.hasData)
                {
                    var json = new JObject
                    {
                        ["lat"]       = snap.lat,
                        ["lon"]       = snap.lon,
                        ["alt"]       = snap.alt,
                        ["velocidad"] = _groundspeed,
                        ["direccion"] = snap.heading,
                        ["estado"]    = ModeName(_customMode) + (_armed ? " (ARMED)" : ""),
                        ["armed"]     = _armed,
                    }.ToString(Newtonsoft.Json.Formatting.None);
                    _ = PublishAsync(_topicTelemetry, json);
                }
                yield return wait;
            }
        }

        static string ModeName(uint customMode)
        {
            foreach (var kv in RawMavlinkCommander.CopterModes)
                if (kv.Value == customMode && kv.Key != "ALTHOLD") return kv.Key;
            return "UNKNOWN";
        }

        IEnumerator StatusAndPortsLoop()
        {
            var wait = new WaitForSeconds(5f);
            while (!_isQuitting)
            {
                var statusJson = new JObject
                {
                    ["connected"] = IsConnected,
                    ["mode"]      = CurrentMode,
                }.ToString(Newtonsoft.Json.Formatting.None);
                _ = PublishAsync(_topicStatus, statusJson, retain: true);

                try
                {
                    var names = System.IO.Ports.SerialPort.GetPortNames();
                    var arr = new JArray();
                    foreach (var n in names) arr.Add(new JObject { ["device"] = n });
                    _ = PublishAsync(_topicPorts, arr.ToString(Newtonsoft.Json.Formatting.None), retain: true);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[DroneMqttBridge] Error listando puertos serie: {ex.Message}");
                }

                yield return wait;
            }
        }
    }
}
