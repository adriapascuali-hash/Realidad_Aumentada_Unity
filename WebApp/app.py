from flask import Flask, render_template, request, jsonify
from flask_socketio import SocketIO, emit
import paho.mqtt.client as mqtt
import json
import webrtc_server

# ─── app2.py ─────────────────────────────────────────────────────────────────
# Copia de app.py SIN HTTPS — HTTP plano, tal como estaba antes de meter TLS.
# Sirve para descartar si un problema es del propio HTTPS (certificado,
# mixed content, etc.) comparando el mismo comportamiento sin él. Puertos
# idénticos a app.py (5000 y 8108), así que no se pueden lanzar los dos a la
# vez — es para probar uno u otro, no en paralelo.

app = Flask(__name__)
app.config['SECRET_KEY'] = 'droneapp-tfg-2026'
socketio = SocketIO(app, cors_allowed_origins="*", async_mode='threading')

# ─── MQTT Configuration ──────────────────────────────────────────────────────
# Unity es quien posee la única conexión MAVLink real (SITL o puerto COM,
# ver Assets/code/DroneMqttBridge.cs). Esta webapp ya no habla con pymavlink
# directamente: publica instrucciones y escucha telemetría por MQTT, usando
# el mismo bróker público que ya usa DroneMqttBridge.cs en Unity. Así
# ambos pueden "hablar" del dron sin competir por el mismo puerto serie.
MQTT_BROKER = 'broker.hivemq.com'
MQTT_PORT = 1883
DRONE_ID = '1'
DEFAULT_BAUD = 57600
WEBRTC_PORT = 8108   # relay de señalización (webrtc_server.py), ver abajo

TOPIC_TELEMETRY     = f'dron/{DRONE_ID}/telemetry'
TOPIC_STATUS        = f'dron/{DRONE_ID}/status'
TOPIC_PORTS         = f'dron/{DRONE_ID}/ports'
TOPIC_ERROR         = f'dron/{DRONE_ID}/error'
TOPIC_AVAILABLE     = f'dron/{DRONE_ID}/available'
TOPIC_CMD           = f'dron/{DRONE_ID}/cmd'
TOPIC_WEAPON_MODE   = f'dron/{DRONE_ID}/weapon_mode'
TOPIC_SCENARIO      = f'dron/{DRONE_ID}/scenario'

mav_connected = False
mav_mode = None
known_ports = []
# Presencia del puente MQTT de Unity (ver DroneMqttBridge.cs: Last Will +
# "birth message" en este mismo topic). Si está a False, "known_ports" es
# necesariamente el último valor retenido de una sesión de Unity que ya no
# está corriendo -> no es de fiar, se descarta en vez de mostrarlo como si
# fuera en tiempo real.
bridge_online = False

telemetry_data = {
    'lat': 0.0, 'lon': 0.0, 'alt': 0.0,
    'velocidad': 0.0, 'direccion': 0, 'estado': 'disconnected'
}

# ─── Scenario / Shooter State ───────────────────────────────────────────────────
current_scenario = ''
current_weapon_mode  = 0   # 0 = laser, 1 = misil


def _on_mqtt_connect(client, userdata, flags, rc):
    print(f"[MQTT] Conectado a {MQTT_BROKER}:{MQTT_PORT}")
    for topic in (TOPIC_TELEMETRY, TOPIC_STATUS, TOPIC_PORTS, TOPIC_ERROR, TOPIC_AVAILABLE,
                  TOPIC_WEAPON_MODE, TOPIC_SCENARIO):
        client.subscribe(topic)


def _on_mqtt_message(client, userdata, msg):
    global mav_connected, mav_mode, known_ports, bridge_online, current_weapon_mode, current_scenario

    try:
        payload = json.loads(msg.payload.decode('utf-8')) if msg.payload else {}
    except Exception as e:
        print(f"[MQTT] Payload inválido en {msg.topic}: {e}")
        return

    if msg.topic == TOPIC_TELEMETRY:
        telemetry_data.update(payload)
        socketio.emit('telemetry', telemetry_data)
    elif msg.topic == TOPIC_STATUS:
        mav_connected = bool(payload.get('connected', False))
        mav_mode = payload.get('mode')
        socketio.emit('drone_status', {'connected': mav_connected, 'client': mav_mode})
    elif msg.topic == TOPIC_PORTS:
        known_ports = payload if isinstance(payload, list) else []
    elif msg.topic == TOPIC_ERROR:
        socketio.emit('drone_error', payload)
    elif msg.topic == TOPIC_AVAILABLE:
        bridge_online = bool(payload.get('online', False))
        if not bridge_online:
            # Unity ya no está ahí (cierre limpio o Last Will tras una caída):
            # la lista de puertos retenida es de una sesión anterior, y el
            # dron no puede seguir conectado sin el puente que lo gestiona.
            known_ports = []
            mav_connected = False
            mav_mode = None
            socketio.emit('drone_status', {'connected': False, 'client': None})
    elif msg.topic == TOPIC_WEAPON_MODE:
        current_weapon_mode = payload.get('mode', current_weapon_mode)
        socketio.emit('weapon_mode', {'mode': current_weapon_mode})
    elif msg.topic == TOPIC_SCENARIO:
        current_scenario = payload.get('name', current_scenario)
        socketio.emit('scenario', {'name': current_scenario})


mqtt_client = mqtt.Client()
mqtt_client.on_connect = _on_mqtt_connect
mqtt_client.on_message = _on_mqtt_message
mqtt_client.connect_async(MQTT_BROKER, MQTT_PORT)
mqtt_client.loop_start()


def publish_cmd(action, payload=None, qos=1):
    """Publica una instrucción para que Unity (DroneMqttBridge.cs) la ejecute
    contra el SITL/COM. El resultado de ejecutarla llega después por
    TOPIC_STATUS (éxito) o TOPIC_ERROR (fallo) -> evento drone_error.

    QoS 1 por defecto: arm/land/rtl... no se pueden perder en silencio por
    un corte del bróker público. El RC usa QoS 0 (llega un valor nuevo cada
    pocos ms, reenviar uno viejo no tiene sentido).

    Devuelve None si se ha entregado al cliente MQTT, o un mensaje de error
    si ni siquiera ha salido (bróker caído), para no responder 'ok' a la
    webapp cuando la orden se ha perdido."""
    if not mqtt_client.is_connected():
        return 'Sin conexión con el bróker MQTT'
    info = mqtt_client.publish(f'{TOPIC_CMD}/{action}', json.dumps(payload or {}), qos=qos)
    if info.rc != mqtt.MQTT_ERR_SUCCESS:
        return f'Error publicando por MQTT: {mqtt.error_string(info.rc)}'
    return None


def cmd_response(action, payload=None, **ok_fields):
    """Publica la instrucción y construye la respuesta HTTP para la webapp."""
    err = publish_cmd(action, payload)
    if err:
        return jsonify({'status': 'error', 'message': err}), 503
    return jsonify({'status': 'ok', **ok_fields})


# ─── HTTP Routes ───────────────────────────────────────────────────────────────
@app.route('/')
def index():
    print(f"[INFO] Cliente conectado desde IP: {request.remote_addr}")
    return render_template('index.html')


@app.route('/mav/serial_ports')
def mav_serial_ports():
    # El puerto serie vive en el equipo de Unity, no en este; se devuelve la
    # última lista que Unity publicó (retained) en TOPIC_PORTS.
    return jsonify(known_ports)


@app.route('/mav/connect', methods=['POST'])
def mav_connect():
    data = request.get_json() or {}
    mode = data.get('mode')
    if mode not in ('sitl', 'real'):
        return jsonify({'status': 'error', 'message': 'Modo inválido'}), 400

    return cmd_response('connect', {
        'mode': mode,
        'port': data.get('port'),
        'baud': data.get('baud', DEFAULT_BAUD),
    }, client=mode)


@app.route('/set_mode', methods=['POST'])
def set_mode_route():
    data = request.get_json() or {}
    mode = data.get('mode', 'GUIDED').upper()
    return cmd_response('set_mode', {'mode': mode}, mode=mode)


@app.route('/arm', methods=['POST'])
def arm_route():
    data = request.get_json() or {}
    return cmd_response('arm', {'force': bool(data.get('force', False))})


@app.route('/takeoff', methods=['POST'])
def takeoff():
    data = request.get_json() or {}
    meters = data.get('meters', 5)
    return cmd_response('takeoff', {'meters': meters})


@app.route('/throttle_lock', methods=['POST'])
def throttle_lock():
    data = request.get_json() or {}
    locked = bool(data.get('locked', False))
    return cmd_response('throttle_lock', {'locked': locked}, locked=locked)


@app.route('/land', methods=['POST'])
def land():
    return cmd_response('land')


@app.route('/rtl', methods=['POST'])
def rtl():
    return cmd_response('rtl')


@app.route('/telemetry')
def get_telemetry():
    return jsonify(telemetry_data)


@app.route('/scenario', methods=['GET'])
def get_scenario():
    """El estado inicial lo sigue pidiendo la webapp por HTTP al cargar;
    Unity ahora reporta cambios por MQTT (ver TOPIC_SCENARIO / DroneMqttBridge.cs),
    no con un POST HTTP directo como antes."""
    return jsonify({'name': current_scenario})


@app.route('/fire', methods=['POST'])
def fire():
    """Webapp requests a shot; se publica por MQTT y lo consume
    DroneMqttBridge.cs en Unity (topic dron/<id>/cmd/fire)."""
    return cmd_response('fire')

@app.route('/cycle_weapon', methods=['POST'])
def cycle_weapon():
    err = publish_cmd('cycle_weapon')
    return (jsonify({'status': 'error', 'message': err}), 503) if err else ('', 204)

@app.route('/cycle_target', methods=['POST'])
def cycle_target():
    err = publish_cmd('cycle_target')
    return (jsonify({'status': 'error', 'message': err}), 503) if err else ('', 204)


# ─── SocketIO Events ───────────────────────────────────────────────────────────
@socketio.on('connect')
def handle_connect():
    emit('drone_status', {'connected': mav_connected, 'client': mav_mode})
    emit('telemetry', telemetry_data)
    emit('scenario', {'name': current_scenario})
    print(f"[WS] Cliente conectado: {request.sid}")


@socketio.on('disconnect')
def handle_disconnect():
    print(f"[WS] Cliente desconectado: {request.sid}")


@socketio.on('sendRC')
def handle_rc(data):
    """Receive joystick position: {id: 'left'|'right', x: float, y: float}.
    Se publica tal cual por MQTT; Unity (DroneMqttBridge.cs) es quien
    mantiene el bucle de RC override a 10Hz y aplica el bloqueo de throttle."""
    d = json.loads(data) if isinstance(data, str) else data
    # El "soltar" (0,0) va con QoS 1: si se pierde, Unity se queda con el
    # último valor del stick y el dron sigue moviéndose sin nadie tocándolo.
    released = not d.get('x') and not d.get('y')
    publish_cmd('rc', d, qos=1 if released else 0)


if __name__ == '__main__':
    print(f"[INFO] Iniciando Drone Control Station (HTTP, sin TLS)")
    print(f"[INFO] Abre http://localhost:5000 en tu navegador")
    print(f"[INFO] Instrucciones/telemetría vía MQTT ({MQTT_BROKER}:{MQTT_PORT}); "
          f"el puerto SITL/COM lo abre Unity, no esta webapp.")

    # Unity's video feed travels over WebRTC instead of being relayed as
    # base64 frames through Socket.IO, using the plain WebSocket signaling
    # relay from dronsEETAC/Tutorial_VideoStreaming, running in its own
    # event loop so it doesn't interfere with eventlet. Sin TLS: un único
    # listener plano en 0.0.0.0, igual para Unity que para los navegadores
    # (no hace falta el segundo puerto wss porque la página aquí es http,
    # sin mixed-content que evitar).
    webrtc_server.run_in_background(host='0.0.0.0', port=WEBRTC_PORT)

    # use_reloader=False: Werkzeug's auto-reloader restarts the whole process
    # on .py file changes, which silently kills the webrtc_server background
    # threads and every WebRTC connection mid-negotiation. Keep debug=True
    # for the error pages, just not the process-restarting watcher.
    socketio.run(app, host='0.0.0.0', port=5000, debug=True,
                 use_reloader=False, allow_unsafe_werkzeug=True)
