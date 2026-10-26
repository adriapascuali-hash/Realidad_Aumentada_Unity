# TFG: Desarrollo de una plataforma de juegos y escenarios en realidad aumentada para drones FPV

Este programa permite realizar un vuelo FPV con elementos en realidad aumentada. La aplicación de escritorio, hecha en Unity, mueve un dron virtual igual que el real a partir de su telemetría (GPS, altitud y actitud) y pone de fondo el vídeo de la cámara del dron, de modo que los objetos virtuales parecen estar en el espacio real. Está pensada para los visitantes del DronLab del Campus UPC Baix Llobregat y forma parte del Drone Engineering Ecosystem (DEE).

La plataforma incluye varios modos de juego:

- **Misiones del editor**: un editor sobre mapa por satélite permite diseñar escenarios en cualquier lugar del mundo con aros, arcos, monedas, banderas, flechas y obstáculos, con su altura, tamaño, orientación y color. La aplicación trae varias de ejemplo ("Prueba 1" a "Prueba 5") situadas en el DronLab.
- **Laberintos** (fácil, medio y difícil): hay que encontrar la salida siguiendo las flechas del suelo. Las paredes también se envían al autopiloto como *geofences*, así que el dron se frena antes de atravesarlas.
- **Disparar al blanco**: combate contra drones enemigos que se mueven por el DronLab y devuelven los disparos, con láser y misiles autoguiados. Empiezan 3 enemigos y aparece uno más cada 5 derribos, cada vez más agresivos.

Se puede jugar con el SITL de Mission Planner o con el dron real conectado por radio de telemetría. Además, hay dos aplicaciones web accesibles desde cualquier dispositivo:

- **WebApp de Control**: muestra la imagen del juego y permite pilotar el dron con dos joysticks táctiles, enviarle órdenes (armar, despegar, aterrizar, cambiar de modo, volver a casa) y usar los controles del juego de disparos.
- **WebApp de Espectador**: solo muestra la imagen del juego, sin ningún control.

<!-- Añadir aquí una captura o GIF de la aplicación, p. ej.: ![Demo](docs/demo.png) -->

### Contenido del repositorio

- **[AR_Unity](#proyecto-de-unity-ar_unity)**: proyecto de Unity con todo el código fuente y los recursos.
- **[AR_ejecutable](#ejecutable-ar_ejecutable)**: versión compilada para Windows, para jugar sin instalar Unity.
- **[WebApp](#webapp)**: servidores en Python y páginas de las dos aplicaciones web.
- **[csDronLink](#csdronlink)**: librería en C# con la que la aplicación se comunica con el dron.
- **[Cómo ponerlo en marcha](#cómo-ponerlo-en-marcha)**: requisitos y pasos para ejecutar el proyecto.
- **[Links](#links)**: memoria del TFG y vídeos de demostración.

## Arquitectura

Unity es el único que habla con el dron (MAVLink, mediante csDronLink). La WebApp le envía las órdenes y recibe la telemetría a través del bróker **MQTT** `broker.hivemq.com`, y el vídeo del juego llega a los navegadores por **WebRTC**.

## Proyecto de Unity (AR_Unity)

La carpeta `AR_Unity` se abre directamente con Unity Hub. Las carpetas `ProjectSettings` y `Packages` guardan la configuración del proyecto y la lista de paquetes que usa (Universal Render Pipeline, WebRTC, glTFast, Newtonsoft Json…). En `SavedScenarios/scenarios.json` se guardan las misiones creadas con el editor.

El trabajo propio está en `Assets`, organizado así:

- **Scenes**: 8 escenas. `inicio`, `Menú` y `Configuración` forman la interfaz; `laberinto`, `laberinto 2`, `laberinto 3` y `shooter` son los escenarios de juego; y `Circuito1` es la escena base sobre la que se montan las misiones personalizadas.
- **code**: los scripts en C# de la aplicación:

  | Script | Función |
  |---|---|
  | `DroneMqttBridge.cs` | Mantiene la conexión MAVLink con el dron (SITL o radio) mediante csDronLink y hace de puente MQTT con la WebApp. |
  | `RawMavlinkCommander.cs` | Envía comandos MAVLink que csDronLink no ofrece directamente (armar/desarmar, cambio de modo). |
  | `DronControl.cs` | Representación visual del dron en la escena; se mueve según la telemetría recibida. |
  | `GameConfig.cs` | Configuración global de la aplicación (vídeo, tipo de conexión, puertos…). |
  | `VideoBackground.cs` | Muestra el vídeo FPV de la cámara del dron como fondo y corrige el efecto ojo de pez. |
  | `UnityVideoStreamer.cs` | Emite el vídeo del juego a las WebApps mediante WebRTC. |
  | `ScenarioReporter.cs` | Informa a la WebApp de qué escenario está activo. |
  | `Inicio.cs`, `Menú.cs`, `Configuración.cs` | Pantalla de inicio, selector de escenarios y menú de configuración. |
  | `Pausemenuui.cs` | Menú de pausa. |
  | `FlightHud.cs` | HUD de vuelo (altitud, velocidad, rumbo, escalera de cabeceo y retícula). |
  | `Shadertimekeeper.cs` | Mantiene animados los shaders de la interfaz cuando el juego está en pausa. |
  | `MapCreatorUI.cs` | Editor de escenarios personalizados sobre un mapa por satélite, con vista previa 3D. |
  | `AroHuecoUtil.cs` | Ajusta el tamaño del hueco de los aros en el editor. |
  | `MapSceneController.cs` | Coloca en la escena los objetos de un escenario personalizado y aplica la geofence del DronLab. |
  | `RuntimeSceneLoader.cs` | Monta la partida de un escenario personalizado. |
  | `coin.cs` | Recogida de monedas al pasar el dron cerca. |
  | `LabyrinthGeofenceManager.cs` | Crea las paredes de los laberintos y las envía al autopiloto como geofences de exclusión. |
  | `ShooterConfigMenu.cs`, `ShooterSettings.cs` | Menú y opciones de partida del escenario de disparos. |
  | `ShooterController.cs` | Lógica de disparo y HUD de combate. |
  | `TargetSpawner.cs`, `AITarget.cs` | Aparición y movimiento de los drones enemigos. |
  | `Projectile.cs`, `Missile.cs`, `EnemyProjectile.cs` | Láser, misiles y disparos enemigos. |
  | `ShooterFenceCleaner.cs` | Limpia las geofences del autopiloto al entrar y salir del escenario de disparos. |

- **Resources**: prefabs de los objetos virtuales (aros, arcos, banderas, flechas, monedas y obstáculos, en varios colores), el modelo del DronLab, materiales, fuentes e iconos de la interfaz.
- **Shaders**: `FisheyeUndistort` corrige el efecto ojo de pez de la cámara del dron; el resto son efectos de la interfaz.
- **StreamingAssets**: modelos 3D que se cargan en tiempo de ejecución (el dron, los misiles y los drones enemigos) y los escenarios por defecto (`DefaultScenarios/scenarios.json`).
- **Plugins**: librerías externas: `csDronLink.dll`, MQTTnet (cliente MQTT) y SharpDX, que el proyecto no usa directamente pero csDronLink necesita para compilar.
- **Settings**: configuración de la Universal Render Pipeline.

El funcionamiento de cada parte se explica con detalle en la memoria (ver [Links](#links)).

## Ejecutable (AR_ejecutable)

Versión compilada para Windows: basta con ejecutar `AR.exe`. En `webrtc_server.txt` se indica la IP y el puerto del servidor de señalización de la WebApp (por defecto `127.0.0.1:8108`).

## WebApp

Código de las dos aplicaciones web, escritas en Python con Flask:

- **app.py**: servidor de la WebApp de Control (puerto `5000`). Envía las órdenes a Unity y recibe la telemetría por MQTT, y además pone en marcha el servidor de señalización WebRTC.
- **app_observador.py**: servidor de la WebApp de Espectador (puerto `5001`). Reutiliza la señalización de `app.py`, así que solo funciona con este en marcha.
- **webrtc_server.py**: servidor de señalización WebRTC (WebSocket, puerto `8108`) que pone en contacto a Unity, que emite el vídeo, con los navegadores que lo reciben.
- **templates/index.html** y **static/js/main.js**: interfaz de la WebApp de Control (vídeo, telemetría, joysticks y botones).
- **templates/index2.html** y **static/js/main2.js**: interfaz de la WebApp de Espectador.
- **requirements.txt**: dependencias de Python.

## csDronLink

Librería en C# (.NET Framework 4.7.2) para comunicarse con el autopiloto mediante MAVLink: conexión, telemetría, despegue, aterrizaje, navegación, geofences, etc. No es obra de este TFG sino que fue creada por alumnos y docentes del DEE. Aquí se incluye su código fuente en la versión exacta con la que se ha compilado el `csDronLink.dll` del proyecto, para que se pueda recompilar aunque la librería original cambie más adelante.

## Cómo ponerlo en marcha

1. **Abrir el proyecto**: instalar Unity **6000.3.8f1** y añadir la carpeta `AR_Unity` desde Unity Hub (*Add project from disk*). La primera apertura tarda unos minutos porque Unity regenera la carpeta `Library`. Si falta el paquete **WebRTC 3.0.0-pre.8** (`com.unity.webrtc`), instalarlo desde el Package Manager.
2. **Ejecutar**: abrir la escena `inicio` y pulsar Play. Arrancar desde cualquier otra escena puede dejar la aplicación sin la conexión ni los menús inicializados.
3. **Configurar la partida** en el menú de **Configuración**:
   - **SITL**: conexión TCP con Mission Planner (por defecto `127.0.0.1:5762`).
   - **Antena**: puerto COM de la radio de telemetría (por defecto a `57600` baudios).
   - **Cámara**: el dispositivo de vídeo del ordenador que recibe la imagen del dron (o la webcam, para probar en simulación).
4. **Lanzar las WebApps**: abrir la carpeta `WebApp` en un entorno de desarrollo como Visual Studio, Visual Studio Code o PyCharm, instalar las librerías de `requirements.txt` y ejecutar el script `app.py`. La WebApp de Control queda en `http://localhost:5000`. Para la de Espectador, ejecutar también `app_observador.py` y abrir `http://localhost:5001`. Desde otro dispositivo de la misma red, usar la IP del ordenador en lugar de `localhost`.

Unity y la WebApp se comunican a través de un bróker MQTT público, por lo que ambos necesitan conexión a Internet. Antes de volar con el dron real, conviene seguir las pruebas de instalación, simulación y producción del Capítulo 6 de la memoria.

| Puerto | Servicio |
|---|---|
| `5000` | WebApp de Control (`app.py`) |
| `5001` | WebApp de Espectador (`app_observador.py`) |
| `8108` | Servidor de señalización WebRTC (`webrtc_server.py`) |
| `1883` | Bróker MQTT (`broker.hivemq.com`) |
| `5762` | SITL de ArduPilot (por defecto) |

## Links

- **Memoria del TFG**: _añadir enlace_
- **Vídeo 1**: _añadir descripción_ - _añadir enlace_
- **Vídeo 2**: _añadir descripción_ - _añadir enlace_
- **Vídeo 3**: _añadir descripción_ - _añadir enlace_

## Autor

**Adrià Pascual** - [@adriapascuali-hash](https://github.com/adriapascuali-hash)

Trabajo Final de Grado - Universitat Politècnica de Catalunya (UPC)
