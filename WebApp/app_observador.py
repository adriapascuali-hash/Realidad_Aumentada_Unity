from flask import Flask, render_template, request

# ─── app3.py ─────────────────────────────────────────────────────────────────
# Vista de "solo espectador": únicamente el vídeo del dron a pantalla completa,
# sin nada de conexión al dron ni panel de configuración — pensada para correr
# EN PARALELO junto a app2.py (p. ej. como pantalla de espectador en otro
# dispositivo, mientras otra persona controla el dron desde app2.py).
#
# Por eso usa un puerto Flask distinto (5001, en vez de 5000) y, sobre todo,
# NO levanta su propio relay de señalización WebRTC: se conecta directamente
# al que YA arranca app2.py en el puerto 8108 (ver webrtc_server.py, que
# soporta cualquier número de receptores simultáneos sin más). Como no manda
# telemetría ni RC, tampoco necesita Flask-SocketIO ni el bróker MQTT — solo
# sirve la página estática de vista (templates/index2.html + static/js/main2.js).
#
# Requisito: app2.py debe estar en marcha para que esta vista reciba vídeo
# (es su relay el que usa). Lanzar app3.py solo, sin app2.py, deja la página
# esperando "SIN SEÑAL DE VIDEO" indefinidamente.

app = Flask(__name__)


@app.route('/')
def view():
    print(f"[INFO] Espectador conectado desde IP: {request.remote_addr}")
    return render_template('index2.html')


if __name__ == '__main__':
    print("[INFO] Iniciando vista de espectador (solo vídeo)")
    print("[INFO] Abre http://localhost:5001 en tu navegador")
    print("[INFO] Requiere que app2.py esté corriendo (usa su relay WebRTC en el puerto 8108).")

    app.run(host='0.0.0.0', port=5001, debug=True, use_reloader=False)
