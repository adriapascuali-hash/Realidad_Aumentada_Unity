// ─── main2.js ───────────────────────────────────────────────────────────────
// JS de app3.py (solo visualización): sin Socket.IO (no hace falta telemetría
// ni mandar RC) y sin nipplejs (no hay joysticks). Solo recibe el vídeo por
// WebRTC y deja alternar pantalla completa — nada más.
//
// Importante: app3.py NO levanta su propio relay de señalización WebRTC.
// Se conecta al que YA está corriendo app2.py (puerto 8108, ver
// webrtc_server.py — soporta cualquier número de receptores a la vez), así
// que para que esta vista reciba vídeo, app2.py tiene que estar en marcha.

// ─── Pantalla completa "de mejor esfuerzo" al entrar ───────────────────────────
window.addEventListener('load', () => {
    setTimeout(() => window.scrollTo(0, 1), 50);
});

// ─── WebRTC Video ───────────────────────────────────────────────────────────
// app3 siempre sirve por HTTP plano (igual que app2), así que el relay a usar
// es siempre el listener plano de app2 en 8108 — sin variante https/wss.
const WEBRTC_SIGNALING_PORT = 8108;
const WEBRTC_NO_VIDEO_TIMEOUT_MS = 8000;
let videoReceiving = false;
let webrtcWs = null;
let webrtcPc = null;
let webrtcWatchdog = null;
let webrtcRetryTimeout = null;
let webrtcStopped = false;

function onVideoStateChange(live) {
    const video = document.getElementById('video-feed');
    if (live === videoReceiving) return;
    videoReceiving = live;
    document.getElementById('no-signal').style.display = live ? 'none' : 'flex';
    video.style.display = live ? 'block' : 'none';
}

function clearWebRtcWatchdog() {
    if (webrtcWatchdog) { clearTimeout(webrtcWatchdog); webrtcWatchdog = null; }
}

function scheduleWebRtcRetry(reason) {
    if (webrtcStopped) return;
    console.warn(`[WebRTC] ${reason} Reintentando en 3s...`);
    onVideoStateChange(false);
    clearWebRtcWatchdog();
    if (webrtcWs && webrtcWs.readyState === WebSocket.OPEN) {
        try { webrtcWs.send(JSON.stringify({ type: 'client-disconnect' })); } catch (e) { /* ignore */ }
    }
    if (webrtcPc) { webrtcPc.close(); webrtcPc = null; }
    if (webrtcWs) { webrtcWs.onclose = null; webrtcWs.close(); webrtcWs = null; }
    clearTimeout(webrtcRetryTimeout);
    webrtcRetryTimeout = setTimeout(connectWebRtcViewer, 3000);
}

function createWebRtcPeerConnection() {
    const config = {
        iceServers: [
            { urls: 'stun:stun.relay.metered.ca:80' },
            {
                urls: 'turn:dronseetac.upc.edu:3478',
                username: 'dronseetac',
                credential: 'Mimara00.',
            },
        ],
    };

    const pc = new RTCPeerConnection(config);

    pc.onicecandidate = (event) => {
        if (event.candidate) {
            webrtcWs.send(JSON.stringify({
                type: 'ice',
                role: 'receptor',
                candidate: {
                    candidate: event.candidate.candidate,
                    sdpMid: event.candidate.sdpMid ?? '0',
                    sdpMLineIndex: event.candidate.sdpMLineIndex ?? 0,
                },
            }));
        } else {
            webrtcWs.send(JSON.stringify({ type: 'ice', role: 'receptor', candidate: null }));
        }
    };

    pc.ontrack = (event) => {
        clearWebRtcWatchdog();
        const video = document.getElementById('video-feed');
        video.srcObject = event.streams && event.streams[0]
            ? event.streams[0]
            : new MediaStream([event.track]);
        onVideoStateChange(true);

        if ('playoutDelayHint' in event.receiver) {
            event.receiver.playoutDelayHint = 0;
        }
    };

    pc.onconnectionstatechange = () => {
        if (['disconnected', 'failed', 'closed'].includes(pc.connectionState)) {
            scheduleWebRtcRetry(`connectionState=${pc.connectionState}.`);
        }
    };

    return pc;
}

async function handleWebRtcMessage(evt) {
    const data = JSON.parse(evt.data);

    if (data.type === 'ice' && data.role === 'emisor') {
        if (data.candidate) {
            try {
                await webrtcPc.addIceCandidate(new RTCIceCandidate(data.candidate));
            } catch (e) {
                console.error('[WebRTC] Error añadiendo candidato del emisor:', e);
            }
        }
        return;
    }

    if (data.type === 'sdp' && data.role === 'emisor') {
        try {
            await webrtcPc.setRemoteDescription(
                new RTCSessionDescription({ type: data.sdp_type, sdp: data.sdp })
            );
            const answer = await webrtcPc.createAnswer();
            await webrtcPc.setLocalDescription(answer);
            webrtcWs.send(JSON.stringify({
                type: 'sdp',
                role: 'receptor',
                sdp: webrtcPc.localDescription.sdp,
                sdp_type: webrtcPc.localDescription.type,
            }));
        } catch (e) {
            console.error('[WebRTC] Error negociando la oferta del emisor:', e);
        }
    }
}

function connectWebRtcViewer() {
    webrtcStopped = false;
    const url = `ws://${location.hostname}:${WEBRTC_SIGNALING_PORT}`;
    console.log('[WebRTC] Conectando señalización a', url);
    webrtcWs = new WebSocket(url);
    webrtcWs.onmessage = handleWebRtcMessage;
    webrtcWs.onopen = () => {
        webrtcPc = createWebRtcPeerConnection();
        webrtcWs.send(JSON.stringify({ type: 'peticion' }));

        clearWebRtcWatchdog();
        webrtcWatchdog = setTimeout(
            () => scheduleWebRtcRetry('No llegó vídeo a tiempo.'),
            WEBRTC_NO_VIDEO_TIMEOUT_MS
        );
    };
    webrtcWs.onerror = (e) => {
        console.error('[WebRTC] Error en el WebSocket de señalización:', e);
    };
    webrtcWs.onclose = (e) => {
        scheduleWebRtcRetry(`WebSocket de señalización cerrado (code=${e.code}).`);
    };
}

window.addEventListener('beforeunload', () => {
    if (webrtcWs && webrtcWs.readyState === WebSocket.OPEN) {
        webrtcWs.send(JSON.stringify({ type: 'client-disconnect' }));
    }
});

// A diferencia de main.js (que espera a elegir modo SITL/real), aquí no hay
// ningún gesto de conexión que esperar: es una vista pasiva, así que se
// conecta directamente al cargar la página.
connectWebRtcViewer();

// ─── Fullscreen ────────────────────────────────────────────────────────────────
function requestFullscreen() {
    const el = document.documentElement;
    const request = el.requestFullscreen || el.webkitRequestFullscreen || el.msRequestFullscreen;
    if (request) {
        request.call(el).catch(() => {});
    }
}

function exitFullscreen() {
    const exit = document.exitFullscreen || document.webkitExitFullscreen || document.msExitFullscreen;
    if (exit) {
        exit.call(document).catch(() => {});
    }
}

function isFullscreen() {
    return !!(document.fullscreenElement || document.webkitFullscreenElement || document.msFullscreenElement);
}

function updateFullscreenIcon() {
    document.getElementById('icon-fullscreen-enter').classList.toggle('hidden', isFullscreen());
    document.getElementById('icon-fullscreen-exit').classList.toggle('hidden', !isFullscreen());
}

document.getElementById('btn-fullscreen-toggle').onclick = () => {
    if (isFullscreen()) exitFullscreen();
    else requestFullscreen();
};

['fullscreenchange', 'webkitfullscreenchange', 'msfullscreenchange'].forEach(evt =>
    document.addEventListener(evt, updateFullscreenIcon)
);
