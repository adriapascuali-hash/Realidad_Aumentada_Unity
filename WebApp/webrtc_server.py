"""
webrtc_server.py — Pure WebSocket signaling relay for WebRTC, following exactly
the pattern from https://github.com/dronsEETAC/Tutorial_VideoStreaming
(webRTC/webapp/servidorWebAppWebRTC.py).

The server does NOT touch the video itself (no aiortc here) — it only
relays SDP offers/answers and ICE candidates between one "emisor" (Unity,
the video producer) and any number of "receptores" (browser viewers). The
actual WebRTC media flows peer-to-peer between the emisor and each
receptor once signaling is done.

Runs as its own asyncio event loop in a background thread so it doesn't
interfere with the existing Flask-SocketIO app (telemetry + joystick RC).
"""
import asyncio
import json
import ssl
import threading

import websockets


def _make_handler():
    """Builds a connection handler with its own isolated receptores/wsEmisor
    state, so multiple relay instances (one per port) don't share state."""
    receptores = []
    state = {"wsEmisor": None}

    async def safe_send(target, payload):
        """Best-effort send: a peer can vanish (page reload, Unity stopped,
        etc.) at any moment without us hearing about it first. Letting that
        raise out of the handler is exactly what was causing the unhandled
        ConnectionClosedError -> websockets closing the connection with
        code 1011 ("internal error")."""
        if target is None:
            return
        try:
            await target.send(payload)
        except websockets.exceptions.ConnectionClosed:
            print("Aviso: intenté enviar a un peer que ya estaba desconectado.")

    async def handler(ws):
        try:
            await _handle_messages(ws)
        finally:
            # The receptor can vanish without ever sending "client-disconnect"
            # (network drop, browser tab killed, or just our own retry logic
            # reconnecting without notifying the old socket first). Without
            # this, the emisor never finds out and keeps that old
            # RTCPeerConnection (and its encoder) alive forever — they pile
            # up across reconnects and were what was making the stream
            # increasingly choppy over a long session.
            if ws in receptores:
                indice = receptores.index(ws)
                receptores[indice] = None
                print(f"Conexión cerrada: limpio receptor {indice} y aviso al emisor")
                await safe_send(state["wsEmisor"], json.dumps({"type": "client-disconnect", "id": indice}))
            if state["wsEmisor"] is ws:
                state["wsEmisor"] = None
                print("El emisor se ha desconectado")

    async def _handle_messages(ws):
        async for raw in ws:
            try:
                data = json.loads(raw)

                if data.get("type") == "registro" and data.get("role") == "emisor":
                    print("Se registra el emisor")
                    state["wsEmisor"] = ws
                    if len(receptores) > 0:
                        print("Traslado al emisor los indices de los receptores que estan esperando")
                        for indice, receptor in enumerate(receptores):
                            await safe_send(state["wsEmisor"], json.dumps({
                                "type": "receptor",
                                "id": indice
                            }))

                if data.get("type") == "peticion":
                    print("Recibo petición de receptor")
                    receptores.append(ws)
                    if state["wsEmisor"]:
                        indice = len(receptores) - 1
                        print("Envio al emisor en indice de este nuevo receptor")
                        await safe_send(state["wsEmisor"], json.dumps({
                            "type": "peticion",
                            "id": indice
                        }))
                    else:
                        print("El emisor aun no se ha conectado")

                if data.get("type") == "sdp" and data.get("role") == "emisor":
                    id = data.get("id")
                    print("Recibo y traslado la oferta para el receptor: ", id)
                    if 0 <= id < len(receptores):
                        await safe_send(receptores[id], raw)
                    else:
                        print(f"Aviso: oferta para receptor {id}, que ya no existe.")
                elif data.get("type") == "sdp" and data.get("role") == "receptor":
                    if ws not in receptores:
                        print("Aviso: aceptación de un receptor que ya no está en la lista.")
                        continue
                    id = receptores.index(ws)
                    print("Recibo y traslado al emisor la aceptación del receptor: ", id)
                    data["id"] = id
                    await safe_send(state["wsEmisor"], json.dumps(data))

                elif data.get("type") == "ice" and data.get("role") == "receptor":
                    if ws not in receptores:
                        print("Aviso: candidato de un receptor que ya no está en la lista.")
                        continue
                    id = receptores.index(ws)
                    data["id"] = id
                    print("Recibo y traslado al emisor un candidato para el receptor: ", id)
                    await safe_send(state["wsEmisor"], json.dumps(data))

                elif data.get("type") == "ice" and data.get("role") == "emisor":
                    print("Recibo y traslado a todos los receptores un candidato para el emisor")
                    for receptor in list(receptores):
                        await safe_send(receptor, raw)

                elif data.get("type") == "client-disconnect":
                    if ws not in receptores:
                        continue
                    id = receptores.index(ws)
                    print("Se desconecta el cliente: ", id)
                    receptores[id] = None
                    data["id"] = id
                    await safe_send(state["wsEmisor"], json.dumps(data))

            except Exception as e:
                # Never let one bad/stale message kill the whole connection
                # (that's what was producing "sent 1011 (unexpected error)").
                print(f"Error procesando mensaje: {e}")

    return handler


async def _start_websocket_server(host, port, tls_host=None, tls_port=None,
                                   certfile=None, keyfile=None):
    # Un único handler compartido por los dos listeners (plano y TLS): el
    # emisor (Unity) y los receptores (navegadores) tienen que poder verse
    # entre sí sin importar por cuál de los dos entraron — _make_handler()
    # crea su propio estado (receptores/wsEmisor) aislado por llamada, así
    # que si creáramos un handler por listener, cada uno viviría en su burbuja
    # y nunca se enterarían el uno del otro.
    handler = _make_handler()

    # ping_interval=None: disable the library's ping/pong keepalive. Unity's
    # ClientWebSocket doesn't answer those pings promptly enough (the main
    # thread is busy with per-frame screen capture), so the server was
    # concluding the emisor was dead and closing the connection every ~20s,
    # killing every ICE negotiation halfway through.
    await websockets.serve(handler, host, port, ping_interval=None)
    print(f"Servidor WebSocket WebRTC (Unity) iniciado en ws://{host}:{port}")

    # Listener TLS aparte, en otro puerto, solo para navegadores: si la
    # webapp se sirve por https, el navegador bloquea como "mixed content"
    # cualquier ws:// sin cifrar abierto desde una página https — necesita
    # wss:// sí o sí. Unity no es un navegador y sigue hablando ws:// plano
    # con el primer listener, evitando así tener que hacerle confiar en un
    # certificado autofirmado.
    if tls_host and tls_port and certfile and keyfile:
        ssl_context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
        ssl_context.load_cert_chain(certfile, keyfile)
        await websockets.serve(handler, tls_host, tls_port, ping_interval=None, ssl=ssl_context)
        print(f"Servidor WebSocket WebRTC (navegadores, TLS) iniciado en wss://{tls_host}:{tls_port}")

    await asyncio.Future()  # keep running forever


def run_in_background(host: str = "0.0.0.0", port: int = 8108,
                       tls_host: str = None, tls_port: int = None,
                       certfile: str = None, keyfile: str = None) -> threading.Thread:
    """Starts the signaling relay in its own thread with its own asyncio loop.

    Si se pasan tls_host/tls_port/certfile/keyfile, además del listener plano
    (host:port) se levanta un segundo listener WSS en tls_host:tls_port,
    compartiendo el mismo estado — ver _start_websocket_server().
    """

    def _runner():
        loop = asyncio.new_event_loop()
        asyncio.set_event_loop(loop)
        try:
            loop.run_until_complete(
                _start_websocket_server(host, port, tls_host, tls_port, certfile, keyfile))
        finally:
            loop.close()

    thread = threading.Thread(target=_runner, daemon=True)
    thread.start()
    return thread


if __name__ == "__main__":
    # Standalone mode, for testing this server without the rest of the Flask app.
    asyncio.run(_start_websocket_server("0.0.0.0", 8108))
