// ─── Socket.IO Connection ──────────────────────────────────────────────────────
const socket = io();
let leftManager = null;
let rightManager = null;
let videoReceiving = false;
let gameStarted = false; // true en cuanto llega el primer vídeo (HUD visible): activa los joysticks

// Consulta el escenario por HTTP en cuanto el DOM está listo, antes incluso
// de que el socket se conecte, para aplicar la configuración correcta desde
// el principio aunque la webapp se abra con el shooter ya activo.
document.addEventListener('DOMContentLoaded', () => {
    fetch('/scenario').then(r => r.json()).then(data => {
        if (data.name) applyScenarioConfig(data.name);
    }).catch(() => {});
});

// ─── Pantalla completa "de mejor esfuerzo" al entrar ───────────────────────────
// No es la Fullscreen API (esa engancha el icono nativo de "salir de pantalla
// completa" del navegador, que no se puede mover ni ocultar por seguridad).
// En su lugar, se aprovecha que el body es 1px más alto que la ventana (ver
// CSS) para hacer un scroll(0,1) nada más cargar: en varios navegadores móviles
// eso repliega la barra de direcciones sin activar ningún modo fullscreen real.
// No es garantizado en todos los navegadores/versiones — la única forma 100%
// segura de no ver nada de interfaz del navegador sigue siendo "Añadir a
// pantalla de inicio".
window.addEventListener('load', () => {
    setTimeout(() => window.scrollTo(0, 1), 50);
});

// ─── Socket.IO Event Handlers ─────────────────────────────────────────────────
socket.on('connect', () => {
    console.log('[WS] Conectado al servidor');
});

// Se pone a true en cuanto el usuario elige un color a mano (ver botones más
// abajo). ScenarioReporter.cs (Unity) reenvía el escenario activo cada 3s, lo
// que dispara applyScenarioConfig una y otra vez mientras se juega: sin este
// flag, forzaba verde de nuevo a los pocos segundos de elegir cian y el
// cambio manual parecía no aplicarse nunca.
let userPickedJoyColor = false;

function applyScenarioConfig(scenarioName) {
    const isShooter = scenarioName === 'shooter';
    const fireBtnEl = document.getElementById('btn-fire');
    if (fireBtnEl) fireBtnEl.classList.toggle('hidden', !isShooter);
    const shooterCtrl = document.getElementById('shooter-controls');
    if (shooterCtrl) shooterCtrl.classList.toggle('hidden', !isShooter);
    // Verde por defecto en todos los escenarios (antes solo en shooter; el
    // resto caía en cyan), pero solo mientras el usuario no lo haya cambiado
    // a mano desde el panel lateral.
    if (!userPickedJoyColor) {
        document.body.classList.add('joy-green');
        document.getElementById('joy-color-green').classList.add('active');
        document.getElementById('joy-color-cyan').classList.remove('active');
    }
}

socket.on('scenario', (data) => {
    applyScenarioConfig(data.name);
});

socket.on('weapon_mode', (data) => {
    const isMissile = data.mode === 1;
    const tgt = document.getElementById('btn-cycle-target');
    if (tgt) tgt.classList.toggle('hidden', !isMissile);
});

// ─── Envío de instrucciones ────────────────────────────────────────────────────
// Camino principal: canal de datos WebRTC "control" (ver controlChannel más
// abajo), directo a Unity por la misma conexión que el vídeo. Respaldo,
// cuando el canal no está abierto (antes de que llegue el vídeo, reconectando):
// la ruta HTTP de app.py, que publica en el bróker MQTT (dron/<id>/cmd/*).
// Por los dos caminos la instrucción la ejecuta Unity de forma asíncrona:
// "ok" solo significa que ha salido, y un fallo llega después como error.
const COMMAND_ROUTES = {
    connect:       '/mav/connect',
    set_mode:      '/set_mode',
    arm:           '/arm',
    takeoff:       '/takeoff',
    land:          '/land',
    rtl:           '/rtl',
    throttle_lock: '/throttle_lock',
    fire:          '/fire',
    cycle_weapon:  '/cycle_weapon',
    cycle_target:  '/cycle_target',
};

async function sendCommand(action, payload = {}) {
    const ch = controlChannel;
    if (ch && ch.readyState === 'open') {
        try {
            ch.send(JSON.stringify({ action, ...payload }));
            return { status: 'ok', ...payload };
        } catch (e) {
            console.warn(`[WebRTC] No se pudo enviar '${action}' por el canal, uso el bróker:`, e);
        }
    }
    try {
        const res = await fetch(COMMAND_ROUTES[action], {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });
        if (res.status === 204) return { status: 'ok' };
        return await res.json();
    } catch (e) {
        return { status: 'error', message: 'Sin conexión con el servidor' };
    }
}

// Fallos de instrucciones que fueron por el bróker (Unity los publica en
// dron/<id>/error). Los de las que fueron por WebRTC vuelven por el propio
// canal (ver ondatachannel).
socket.on('drone_error', (data) => {
    showToast(data.message || 'Error del dron', 'error');
});

document.getElementById('btn-cycle-weapon').onclick = () => sendCommand('cycle_weapon');
document.getElementById('btn-cycle-target').onclick = () => sendCommand('cycle_target');

// ─── WebRTC Video (replaces the old base64-over-Socket.IO frame relay) ────────
// Follows exactly the signaling protocol from
// https://github.com/dronsEETAC/Tutorial_VideoStreaming (webRTC/webapp):
// the Flask app's Socket.IO connection is untouched (telemetry/RC); video
// is a separate plain-WebSocket signaling relay (webrtc_server.py) that
// only brokers SDP/ICE between the "emisor" (producer) and this "receptor".
// Puerto y esquema según cómo se sirva esta misma página (mismo main.js para
// app.py [https, wss] y app2.py [http, ws] — ver webrtc_server.py):
// - https (app.py): el navegador bloquea como "mixed content" cualquier
//   ws:// sin cifrar abierto desde una página https, así que hace falta el
//   listener TLS aparte (wss://) en 8109.
// - http (app2.py): sin TLS de por medio, un único listener plano en 8108
//   (el mismo que usa Unity) sirve también para el navegador.
const WEBRTC_IS_SECURE = location.protocol === 'https:';
const WEBRTC_SIGNALING_PORT = WEBRTC_IS_SECURE ? 8109 : 8108;
const WEBRTC_NO_VIDEO_TIMEOUT_MS = 8000;  // give up and retry if no frame arrives in time
let webrtcWs = null;
let webrtcPc = null;
let webrtcWatchdog = null;
let webrtcRetryTimeout = null;
let webrtcStopped = true;  // becomes false once the user picks a flight mode
// Canal de datos "control" que crea Unity en la misma conexión del vídeo:
// por aquí va el joystick directo a Unity (ver sendRC más abajo).
let controlChannel = null;

function onVideoStateChange(live) {
    const video = document.getElementById('video-feed');
    if (live === videoReceiving) return;
    videoReceiving = live;
    document.getElementById('no-signal').style.display = live ? 'none' : 'flex';
    video.style.display = live ? 'block' : 'none';

    // Los joysticks se activan la primera vez que llega vídeo (el HUD ya es
    // visible). Una vez activados se quedan así: un corte de vídeo pasajero
    // durante el vuelo no debe volver a bloquear los controles.
    if (live && !gameStarted) {
        gameStarted = true;
        document.body.classList.remove('game-not-started');
    }
}

function clearWebRtcWatchdog() {
    if (webrtcWatchdog) { clearTimeout(webrtcWatchdog); webrtcWatchdog = null; }
}

function scheduleWebRtcRetry(reason) {
    if (webrtcStopped) return;
    console.warn(`[WebRTC] ${reason} Reintentando en 3s...`);
    onVideoStateChange(false);
    clearWebRtcWatchdog();
    // Tell the relay (and therefore Unity) we're leaving *before* closing,
    // so the old RTCPeerConnection/encoder on the Unity side gets torn down
    // instead of piling up as a zombie every time we reconnect. The relay
    // also now does this automatically when the socket just dies (network
    // drop), but sending it explicitly here covers the common case faster.
    if (webrtcWs && webrtcWs.readyState === WebSocket.OPEN) {
        try { webrtcWs.send(JSON.stringify({ type: 'client-disconnect' })); } catch (e) { /* ignore */ }
    }
    controlChannel = null;
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
            console.log('[WebRTC] Obtengo y envío al servidor un candidato', event.candidate);
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
            console.log('[WebRTC] Fin de candidatos locales');
            webrtcWs.send(JSON.stringify({ type: 'ice', role: 'receptor', candidate: null }));
        }
    };

    pc.oniceconnectionstatechange = () => {
        console.log('[WebRTC] iceConnectionState ->', pc.iceConnectionState);
    };

    pc.ontrack = (event) => {
        console.log('[WebRTC] ontrack recibido', event);
        clearWebRtcWatchdog();
        const video = document.getElementById('video-feed');
        video.srcObject = event.streams && event.streams[0]
            ? event.streams[0]
            : new MediaStream([event.track]);
        onVideoStateChange(true);

        // Chrome/Edge add an extra playout buffer by default to smooth over
        // network jitter, at the cost of latency. For a live game feed we'd
        // rather risk an occasional stutter than carry that buffer as
        // constant added delay, so ask for none.
        if ('playoutDelayHint' in event.receiver) {
            event.receiver.playoutDelayHint = 0;
        }
    };

    pc.ondatachannel = (event) => {
        if (event.channel.label !== 'control') return;
        const ch = event.channel;
        ch.onopen  = () => { console.log('[WebRTC] Canal de control abierto: joystick e instrucciones directos a Unity'); controlChannel = ch; };
        ch.onclose = () => {
            console.warn('[WebRTC] Canal de control cerrado: joystick e instrucciones vuelven por el bróker');
            if (controlChannel === ch) controlChannel = null;
        };
        // Unity responde por aquí solo cuando una instrucción enviada por el
        // canal falla (equivalente al drone_error del camino MQTT).
        ch.onmessage = (e) => {
            try {
                const msg = JSON.parse(e.data);
                if (msg.type === 'error') showToast(msg.message || 'Error del dron', 'error');
            } catch (err) {
                console.warn('[WebRTC] Mensaje inválido en el canal de control:', e.data);
            }
        };
        if (ch.readyState === 'open') controlChannel = ch;
    };

    pc.onconnectionstatechange = () => {
        console.log('[WebRTC] connectionState ->', pc.connectionState);
        if (['disconnected', 'failed', 'closed'].includes(pc.connectionState)) {
            // The signaling WebSocket can stay open even though the actual
            // media connection died (e.g. the other side closed the app,
            // network blip, ICE failure). Without this, onVideoStateChange
            // just hid the video and nothing ever retried.
            scheduleWebRtcRetry(`connectionState=${pc.connectionState}.`);
        }
    };

    return pc;
}

async function handleWebRtcMessage(evt) {
    const data = JSON.parse(evt.data);

    if (data.type === 'ice' && data.role === 'emisor') {
        if (data.candidate) {
            console.log('[WebRTC] Recibo candidato del emisor', data.candidate);
            try {
                await webrtcPc.addIceCandidate(new RTCIceCandidate(data.candidate));
            } catch (e) {
                console.error('[WebRTC] Error añadiendo candidato del emisor:', e);
            }
        } else {
            console.log('[WebRTC] Fin de candidatos del emisor');
        }
        return;
    }

    if (data.type === 'sdp' && data.role === 'emisor') {
        console.log('[WebRTC] Recibo oferta del emisor', data);
        try {
            await webrtcPc.setRemoteDescription(
                new RTCSessionDescription({ type: data.sdp_type, sdp: data.sdp })
            );
            const answer = await webrtcPc.createAnswer();
            await webrtcPc.setLocalDescription(answer);
            console.log('[WebRTC] Envío aceptación de la oferta');
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
    const url = `${WEBRTC_IS_SECURE ? 'wss' : 'ws'}://${location.hostname}:${WEBRTC_SIGNALING_PORT}`;
    console.log('[WebRTC] Conectando señalización a', url);
    webrtcWs = new WebSocket(url);
    webrtcWs.onmessage = handleWebRtcMessage;
    webrtcWs.onopen = () => {
        console.log('[WebRTC] WebSocket de señalización abierto. Envío petición.');
        webrtcPc = createWebRtcPeerConnection();
        webrtcWs.send(JSON.stringify({ type: 'peticion' }));

        // Watchdog: if no video frame shows up within the timeout (signaling
        // got stuck, ICE never completed, emisor never connected, etc.),
        // tear everything down and try again — indefinitely, until it works.
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

// ─── Fullscreen ────────────────────────────────────────────────────────────────
// Debe dispararse dentro de un gesto de usuario (click), así que se llama justo
// al pulsar los botones de selección de modo. El navegador añade su propio
// icono/aviso de "salir de pantalla completa" (no se puede quitar ni mover,
// es a propósito por seguridad) pero es la única forma de conseguir pantalla
// completa real sin depender de que el usuario añada la web a su pantalla de
// inicio.
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

// El icono también debe actualizarse si el usuario sale con Esc o con el
// propio control nativo del navegador, no solo al pulsar este botón.
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

// ─── Mode Selection ────────────────────────────────────────────────────────────
document.getElementById('btn-mode-sitl').onclick = () => { requestFullscreen(); connectMode('sitl'); };

document.getElementById('btn-mode-real').onclick = async () => {
    document.getElementById('real-drone-options').classList.remove('hidden');
    const sel = document.getElementById('serial-port-select');
    sel.innerHTML = '<option value="">Seleccionar puerto...</option>';
    try {
        const res = await fetch('/mav/serial_ports');
        const ports = await res.json();
        ports.forEach(p => {
            const opt = document.createElement('option');
            opt.value = p.device;
            opt.textContent = p.device + (p.description ? ' - ' + p.description : '');
            sel.appendChild(opt);
        });
    } catch (e) {
        setModeStatus('Error listando puertos serie', true);
    }
};

document.getElementById('btn-connect-real').onclick = () => {
    const port = document.getElementById('serial-port-select').value;
    if (!port) {
        setModeStatus('Selecciona un puerto serie', true);
        return;
    }
    requestFullscreen();
    connectMode('real', port);
};

function setModeStatus(message, isError = false) {
    const el = document.getElementById('mode-status');
    el.textContent = message;
    el.classList.toggle('error', isError);
}

async function connectMode(mode, port) {
    setModeStatus('Conectando...');
    try {
        // Aquí todavía no hay vídeo ni canal WebRTC (se abre justo después),
        // así que en la práctica esta va siempre por el bróker.
        const data = await sendCommand('connect', { mode, port });
        if (data.status === 'ok') {
            document.getElementById('mode-modal').classList.add('hidden');
            document.getElementById('main-ui').classList.remove('hidden');
            initJoysticks();
            // Start the WebRTC connection only after a real user gesture
            // (choosing SITL/real drone), same as the reference template's
            // "CONECTAR" button gate — some browsers throttle ICE candidate
            // gathering on connections made before any user interaction.
            connectWebRtcViewer();
        } else {
            setModeStatus('Error: ' + data.message, true);
        }
    } catch (e) {
        setModeStatus('Error de conexión', true);
    }
}

// ─── Flight Controls ───────────────────────────────────────────────────────────
async function setMode(mode) {
    const data = await sendCommand('set_mode', { mode });
    if (data.status === 'ok') {
        showToast(`Modo ${mode} activado`, 'success');
    } else {
        showToast(data.message, 'error');
    }
}

document.getElementById('btn-guided').onclick = () => setMode('GUIDED');
document.getElementById('btn-loiter').onclick = () => setMode('LOITER');

async function doArm(force = false) {
    const data = await sendCommand('arm', { force });
    if (data.status === 'ok') {
        showToast(force ? 'Armado forzado' : 'Dron armado', 'success');
    } else {
        showToast(data.message, 'error');
    }
}
document.getElementById('btn-arm').onclick       = () => doArm(false);
document.getElementById('btn-arm-force').onclick = () => doArm(true);

document.getElementById('btn-takeoff').onclick = async () => {
    const meters = parseFloat(document.getElementById('altitude').value) || 5;
    const data = await sendCommand('takeoff', { meters });
    if (data.status === 'ok') {
        showToast(`Despegando a ${meters}m`, 'success');
    } else {
        showToast(data.message, 'error');
    }
};

document.getElementById('btn-land').onclick = async () => {
    const data = await sendCommand('land');
    if (data.status === 'ok') {
        showToast('Aterrizando...', 'info');
    } else {
        showToast(data.message, 'error');
    }
};

document.getElementById('btn-rtl').onclick = async () => {
    const data = await sendCommand('rtl');
    if (data.status === 'ok') {
        showToast('Volviendo a casa (RTL)...', 'info');
    } else {
        showToast(data.message, 'error');
    }
};

document.getElementById('btn-fire').onclick = () => sendCommand('fire');

let throttleLocked = false;
document.getElementById('btn-throttle-lock').onclick = async () => {
    const data = await sendCommand('throttle_lock', { locked: !throttleLocked });
    if (data.status !== 'ok') {
        showToast(data.message, 'error');
        return;
    }
    throttleLocked = data.locked;
    const btn = document.getElementById('btn-throttle-lock');
    btn.classList.toggle('active', throttleLocked);
    showToast(throttleLocked ? 'Empuje bloqueado' : 'Empuje desbloqueado', 'info');
};

// ─── Panel lateral de controles ────────────────────────────────────────────────
const drawer        = document.getElementById('side-drawer');
const drawerOverlay = document.getElementById('drawer-overlay');

function openDrawer() {
    drawer.classList.add('open');
    drawerOverlay.classList.remove('hidden');
}
function closeDrawer() {
    drawer.classList.remove('open');
    drawerOverlay.classList.add('hidden');
}

document.getElementById('btn-drawer-toggle').onclick = openDrawer;
document.getElementById('btn-drawer-close').onclick  = closeDrawer;
drawerOverlay.onclick = closeDrawer;

document.getElementById('toggle-gameplay-controls').onchange = (e) => {
    document.body.classList.toggle('gameplay-controls-hidden', !e.target.checked);
};

// ─── Sliders de sensibilidad ─────────────────────────────────────────────────
const sensInputs = {
    'left-x':  document.getElementById('sens-left-x'),
    'left-y':  document.getElementById('sens-left-y'),
    'right-x': document.getElementById('sens-right-x'),
    'right-y': document.getElementById('sens-right-y'),
};
function getSens(joyId, axis) {
    return (sensInputs[`${joyId}-${axis}`]?.value ?? 100) / 100;
}
Object.entries(sensInputs).forEach(([key, el]) => {
    el.addEventListener('input', () => {
        document.getElementById(`sens-${key}-val`).textContent = el.value + '%';
    });
});

document.getElementById('hand-right').onclick = () => {
    document.body.classList.remove('lefty');
    document.getElementById('hand-right').classList.add('active');
    document.getElementById('hand-left').classList.remove('active');
};
document.getElementById('hand-left').onclick = () => {
    document.body.classList.add('lefty');
    document.getElementById('hand-left').classList.add('active');
    document.getElementById('hand-right').classList.remove('active');
};

document.getElementById('joy-color-cyan').onclick = () => {
    userPickedJoyColor = true;
    document.body.classList.remove('joy-green');
    document.getElementById('joy-color-cyan').classList.add('active');
    document.getElementById('joy-color-green').classList.remove('active');
};
document.getElementById('joy-color-green').onclick = () => {
    userPickedJoyColor = true;
    document.body.classList.add('joy-green');
    document.getElementById('joy-color-green').classList.add('active');
    document.getElementById('joy-color-cyan').classList.remove('active');
};

// ─── Joystick ─────────────────────────────────────────────────────────────────
function initJoysticks() {
    const size = Math.min(window.innerHeight, window.innerWidth) * 0.35;

    leftManager = nipplejs.create({
        zone: document.getElementById('joy-left'),
        mode: 'static',
        position: { left: '50%', top: '50%' },
        color: '#00d4ff',
        size: size,
        restJoystick: true
    });

    rightManager = nipplejs.create({
        zone: document.getElementById('joy-right'),
        mode: 'static',
        position: { left: '50%', top: '50%' },
        color: '#00d4ff',
        size: size,
        restJoystick: true
    });

    // nipplejs dispara 'move' con cada pointermove (60-120Hz por joystick) y
    // cada uno acabaría siendo un mensaje MQTT al bróker público, que se
    // satura y mete segundos de retraso. En vez de eso, como una emisora RC
    // de verdad: mientras un joystick está cogido se reenvía su posición
    // actual a ritmo fijo, AUNQUE el dedo esté quieto. El RC va con QoS 0 y
    // puede perderse algún mensaje; si solo se enviara al moverse, perder el
    // último dejaría a Unity con un valor intermedio mientras se mantiene el
    // stick (el dron "deja de girar" con el stick a un lado). Unity aplica
    // el RC a 10Hz (RcOverrideLoop), así que ~15Hz basta.
    const RC_SEND_MS = 66;
    const activeRC = {};

    // Camino preferido: canal de datos WebRTC "control", directo navegador ->
    // Unity (misma conexión que el vídeo, normalmente dentro de la red local).
    // Si no está abierto (aún no hay vídeo, reconectando...), se usa el camino
    // antiguo Socket.IO -> Flask -> bróker MQTT -> Unity.
    function sendRC(id, x, y, { release = false } = {}) {
        const ch = controlChannel;
        if (ch && ch.readyState === 'open') {
            // Si el canal se atasca, no acumular movimientos viejos: el
            // siguiente tick ya lleva la posición actual.
            if (release || ch.bufferedAmount < 1024) {
                ch.send(JSON.stringify({ action: 'rc', id, x, y }));
            }
            // El "soltar" va también por MQTT: si antes de abrirse el canal
            // quedaban movimientos en camino por el bróker, este lo pisa.
            if (!release) return;
        }
        // Sin socket no se envía: socket.io acumularía los mensajes y los
        // soltaría todos de golpe (ya viejos) al reconectar.
        if (socket.connected) socket.emit('sendRC', JSON.stringify({ id, x, y }));
    }

    setInterval(() => {
        for (const id of Object.keys(activeRC)) {
            const { x, y } = activeRC[id];
            sendRC(id, x, y);
        }
    }, RC_SEND_MS);

    function onMove(joyId, _, data) {
        const angle = data.angle.radian;
        const force = Math.min(data.force, 1);
        const rawX = force * Math.cos(angle);
        const rawY = force * Math.sin(angle);
        const x = parseFloat((rawX * getSens(joyId, 'x')).toFixed(3));
        const y = parseFloat((rawY * getSens(joyId, 'y')).toFixed(3));
        activeRC[joyId] = { x, y };
    }

    function onEnd(joyId) {
        // El "soltar" sale al momento y deja de reenviarse la posición, para
        // que no llegue un valor viejo detrás.
        delete activeRC[joyId];
        sendRC(joyId, 0, 0, { release: true });
    }

    const joyId = (side) => document.body.classList.contains('lefty')
        ? (side === 'left' ? 'right' : 'left')
        : side;

    leftManager.on('move', (e, data) => onMove(joyId('left'), e, data));
    leftManager.on('end', () => onEnd(joyId('left')));
    rightManager.on('move', (e, data) => onMove(joyId('right'), e, data));
    rightManager.on('end', () => onEnd(joyId('right')));

    // Visibilidad sutil: solo se ven con claridad mientras se están usando
    const joyLeft  = document.getElementById('joy-left');
    const joyRight = document.getElementById('joy-right');
    leftManager.on('start',  () => joyLeft.classList.add('joy-active'));
    leftManager.on('end',    () => joyLeft.classList.remove('joy-active'));
    rightManager.on('start', () => joyRight.classList.add('joy-active'));
    rightManager.on('end',   () => joyRight.classList.remove('joy-active'));
}

// ─── UI Helpers ───────────────────────────────────────────────────────────────
let toastTimeout = null;
function showToast(message, type = 'info') {
    const toast = document.getElementById('toast');
    toast.textContent = message;
    toast.className = 'toast show toast-' + type;
    clearTimeout(toastTimeout);
    toastTimeout = setTimeout(() => toast.className = 'toast', 3000);
}
