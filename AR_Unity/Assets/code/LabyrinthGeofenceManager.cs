using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ARDashboard
{
    /// Crea las paredes de los escenarios de laberinto en la raíz de la escena
    /// (independientes del dronelab) y las envía al autopiloto como polígonos
    /// de exclusión MAVLink.
    ///
    /// Soporta tres escenas: "laberinto" (fácil), "laberinto 2" (medio), "laberinto 3" (difícil).
    /// Cada una tiene su propio layout definido en la tabla Layouts.
    ///
    /// ── Laberinto 1 (17 paredes, rejilla 6×6 ramificada): complejidad media,
    /// entre el laberinto 2 (56 paredes) y un simple pasillo ────────────────
    ///
    /// Laberinto de verdad (no un único pasillo en zigzag) sobre una rejilla
    /// de 6×6 celdas de 1.5×1.5 m (columnas/filas centradas en
    /// -3.75,-2.25,-0.75,+0.75,+2.25,+3.75). ENT entra en la celda
    /// (col2,fila0); EXIT sale desde (col5,fila5). Tiene 14 puntos de
    /// ramificación y ningún callejón sin salida en 36 celdas. Verificado
    /// por BFS: resoluble y sin atajos ocultos hacia la salida (las celdas
    /// col4/5 de la fila superior, ambas dentro del hueco de salida, quedan
    /// a 12 pasos de la entrada).
    /// Nota: con 6 columnas (número par) ninguna celda queda centrada
    /// exactamente en X=0, que es el centro del hueco de entrada del
    /// perímetro; por eso la fila de entrada (fila0) se dejó sin pared entre
    /// (col2,fila0) y (col3,fila0) — si esa pared existiera, partiría
    /// visualmente la única entrada en dos aberturas separadas.
    /// Vista de arriba (Z↑, de fila5 arriba a fila0 abajo; "o" = celda; "|"
    /// y "-" = pared entre celdas contiguas; hueco = paso abierto):
    ///
    /// o o o o o o                          [EXIT: col4-5, fila5]
    ///     - -
    /// o o|o o o o
    ///   -     -
    /// o o o|o o o
    ///     -   - -
    /// o|o o o o o
    ///     - - -
    /// o o|o o o|o
    ///       -
    /// o o|o o o o                          [ENT: col2-3, fila0]
    ///
    /// ── Laberinto 2 (21 paredes, rejilla 7×7 ramificada): muchos caminos y
    /// compartimentos ────────────────────────────────────────────────────
    ///
    /// Laberinto de verdad (no un pasillo único) sobre una rejilla de 7×7
    /// celdas de 9/7≈1.286×1.286 m. Generado con backtracker recursivo + 15
    /// lazos extra para dar varias rutas viables. ENT entra en la celda
    /// (col3,fila0); EXIT sale desde (col6,fila6). Tiene 30 puntos de
    /// ramificación y 3 callejones sin salida en 49 celdas; la ruta más
    /// corta recorre 13 celdas. Verificado por BFS: resoluble y sin atajos
    /// ocultos hacia la salida (las celdas col4/5/6 de la fila superior,
    /// dentro del hueco de salida, quedan a 11-13 pasos de la entrada).
    /// Nota: antes era una rejilla 9×9 de 56 paredes, pero el fence de
    /// Mission Planner/ArduPilot solo admite ~70 puntos en total (4 por
    /// polígono) y ni fusionando tramos colineales cabía (~160 puntos); se
    /// redujo a 7×7/21 paredes, que fusionadas quedan en 14 polígonos
    /// (60 puntos).
    /// Vista de arriba (Z↑, de fila6 arriba a fila0 abajo; "o" = celda; "|"
    /// y "-" = pared entre celdas contiguas; hueco = paso abierto):
    ///
    /// o o o o o o o                          [EXIT: col4-6, fila6]
    ///   -
    /// o|o|o o|o o o
    ///       - - - -
    /// o|o o o o o o
    ///     -
    /// o|o o o o|o o
    ///       -
    /// o|o o|o o o o
    ///           -
    /// o o|o o|o o o
    /// -           -
    /// o o|o o o o o                          [ENT: col3, fila0]
    ///
    /// ── Laberinto 3 (20 paredes, rejilla 5×5): pasillo único muy estrecho, sin
    /// ninguna bifurcación real — a diferencia de laberinto 1 y 2, aquí se pone
    /// una flecha en CADA celda del camino (arrowsAlongFullPath = true), no
    /// solo en los cruces, porque un pasillo sin ramas no tiene ningún "cruce
    /// real" que el sistema de flechas normal pudiera detectar ──────────────
    ///
    /// Rejilla de 5×5 celdas de 1.8×1.8 m (columnas/filas centradas en
    /// -3.6,-1.8,0,+1.8,+3.6). ENT entra en (col2,fila0); EXIT sale desde
    /// (col4,fila4). Serpentina de 21 celdas que recorre casi toda la rejilla
    /// en zigzag; las 4 celdas de la esquina inferior izquierda quedan
    /// completamente selladas (fuera del camino, sin ninguna conexión
    /// abierta). Verificado a mano: resoluble y sin ninguna arista abierta
    /// fuera de la propia secuencia del camino (cero atajos posibles).
    /// Vista de arriba (Z↑, de fila4 arriba a fila0 abajo; "o" = celda; "|" y
    /// "-" = pared entre celdas contiguas; hueco = paso abierto):
    ///
    /// o o o o o                              [EXIT: col4, fila4]
    ///   - - - -
    /// o o o o o
    /// - - - -
    /// o|o o o o
    ///   - - - -
    /// o|o o o o
    /// - - - -
    /// o|o|o o o                              [ENT: col2, fila0]
    public class LabyrinthGeofenceManager : MonoBehaviour
    {
        // ── Auto-inyección ────────────────────────────────────────────────────
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Subscribe() => SceneManager.sceneLoaded += OnSceneLoaded;

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!Layouts.ContainsKey(scene.name)) return;

            var listeners = Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
            if (listeners.Length > 1)
                for (int i = 1; i < listeners.Length; i++)
                    listeners[i].enabled = false;

            var go = new GameObject("[LabyrinthGeofenceManager]");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<LabyrinthGeofenceManager>()._sceneName = scene.name;
        }

        // ── Constantes GPS ────────────────────────────────────────────────────
        const float METERS_PER_DEG   = 111320f;
        const float OFFSET_X         = 166339.2f;
        const float OFFSET_Z         = 4594877f;
        const int   FENCE_TIMEOUT_MS = 8000;
        const float FENCE_INNER      = 4.5f;

        // ── Parámetros de pared ───────────────────────────────────────────────
        const float WALL_H = 10f;
        const float T      = 0.3f;

        // ── Textura metálica de las paredes (chapa rugosa) ────────────────────
        // Generada por código la primera vez que se pide (no hay ningún asset
        // de este tipo en el proyecto) y se comparte entre todas las paredes.
        const int   WALL_TEX_SIZE = 512;
        const float WALL_TEX_TILE_M = 1.2f; // metros de pared por cada repetición de textura
        static Texture2D _wallAlbedo;
        static Texture2D _wallNormal;

        static void EnsureWallTextures()
        {
            if (_wallAlbedo != null) return;

            int size = WALL_TEX_SIZE;
            _wallAlbedo = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat };
            _wallNormal = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat };

            var height = new float[size, size];
            var albedo = new Color[size * size];

            // 1) Metal base con veta de "cepillado" + grano fino de rugosidad
            var baseMetal = new Color(0.40f, 0.41f, 0.43f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float brushed = (Mathf.PerlinNoise(x * 0.015f, y * 0.4f) - 0.5f) * 0.14f;
                float fine    = (Mathf.PerlinNoise(x * 0.22f, y * 0.22f) - 0.5f) * 0.06f;
                float grain   = (Mathf.PerlinNoise(x * 0.9f,  y * 0.9f)  - 0.5f) * 0.05f;
                float v = brushed + fine + grain;
                albedo[y * size + x] = new Color(baseMetal.r + v, baseMetal.g + v, baseMetal.b + v);
                // La rugosidad también se nota en el relieve, no solo en el color
                height[x, y] = grain * 0.5f + fine * 0.3f;
            }

            _wallAlbedo.SetPixels(albedo);
            _wallAlbedo.Apply(true);

            // 2) Normal map derivado del height field (diferencias centrales)
            var normals = new Color[size * size];
            const float normalStrength = 2.4f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float hL = height[(x - 1 + size) % size, y];
                float hR = height[(x + 1) % size, y];
                float hD = height[x, (y - 1 + size) % size];
                float hU = height[x, (y + 1) % size];
                var n = new Vector3(-(hR - hL) * normalStrength, -(hU - hD) * normalStrength, 1f).normalized;
                normals[y * size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
            }
            _wallNormal.SetPixels(normals);
            _wallNormal.Apply(true);
        }

        // Shader cacheado explícitamente por nombre en vez de fiarse del material
        // por defecto que GameObject.CreatePrimitive() le asigna al renderer: ese
        // material implícito (Material.GetDefaultMaterial()) es lo único de esta
        // clase que Unity no ve referenciado por ningún asset del proyecto en
        // tiempo de build, así que su shader (y, sobre todo, la variante con
        // _NORMALMAP activado que se habilita más abajo) puede quedar fuera del
        // strippeo de shaders — se ve bien en el Editor (que compila todas las
        // variantes) pero sale en magenta/rosa en la build. Al pedir el shader
        // por nombre y construir el Material desde cero, además de dejarlo en
        // "Always Included Shaders" (ver GraphicsSettings), se elimina esa
        // dependencia del material implícito.
        static Shader _wallShader;
        static Shader WallShader()
        {
            if (_wallShader == null) _wallShader = Shader.Find("Universal Render Pipeline/Lit");
            return _wallShader;
        }

        static void ApplyWallMaterial(MeshRenderer mr, float wx, float wz)
        {
            EnsureWallTextures();
            var shader = WallShader();
            var mat = shader != null ? new Material(shader) : mr.material;
            mr.material = mat;

            if (mat.HasProperty("_Color"))     mat.color = Color.white;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_MainTex"))   mat.SetTexture("_MainTex", _wallAlbedo);
            if (mat.HasProperty("_BaseMap"))   mat.SetTexture("_BaseMap", _wallAlbedo);
            if (mat.HasProperty("_BumpMap"))
            {
                mat.SetTexture("_BumpMap", _wallNormal);
                mat.EnableKeyword("_NORMALMAP");
            }
            if (mat.HasProperty("_Metallic"))    mat.SetFloat("_Metallic", 0.7f);
            if (mat.HasProperty("_Glossiness"))  mat.SetFloat("_Glossiness", 0.35f);
            if (mat.HasProperty("_Smoothness"))  mat.SetFloat("_Smoothness", 0.35f);

            // Tiling en metros de mundo: la cara larga de la pared usa su
            // longitud real para que el grano no varíe de una pared a otra.
            float longSide = Mathf.Max(wx, wz);
            var tiling = new Vector2(Mathf.Max(1f, longSide / WALL_TEX_TILE_M), Mathf.Max(1f, WALL_H / WALL_TEX_TILE_M));
            mat.mainTextureScale = tiling;
            if (mat.HasProperty("_BumpMap")) mat.SetTextureScale("_BumpMap", tiling);
        }

        // ── Perímetro común a los tres laberintos ─────────────────────────────
        // Entrada (ENT): X -0.75 a +0.75, Z=-4.5 (abajo centro, única entrada)
        // Salida  (EXIT): X +1.5 a +4.5,  Z=+4.5 (arriba derecha)
        static readonly (float cx, float cz, float wx, float wz)[] PERIMETER_STD =
        {
            (-2.625f, -4.5f, 3.75f, T   ),   // inferior izquierda del hueco de entrada
            (+2.625f, -4.5f, 3.75f, T   ),   // inferior derecha  del hueco de entrada
            (-1.5f,   +4.5f, 6.0f,  T   ),   // superior izquierda del hueco de salida
            (-4.5f,    0f,   T,     9.0f),    // lateral izquierda (completa)
            (+4.5f,    0f,   T,     9.0f),    // lateral derecha   (completa)
        };

        // ── Tabla de layouts por nombre de escena ─────────────────────────────
        class LabyrinthLayout
        {
            public (float cx, float cz, float wx, float wz)[] perimeter;
            public (float cx, float cz, float wx, float wz)[] interior;

            // Metadatos de rejilla, usados para calcular las flechas de
            // orientación hacia la salida (gridN == 0 desactiva las flechas
            // por completo, para layouts que no estén en cuadrícula).
            public int gridN;
            public float cellSize;
            public (int c, int r) entCell;
            public (int c, int r) exitCell;

            // Si true, se pone una flecha en TODAS las celdas del camino
            // (no solo en los cruces reales) — pensado para laberintos que
            // son básicamente un pasillo único, donde marcar solo cruces
            // dejaría casi todo el recorrido sin ninguna flecha.
            public bool arrowsAlongFullPath;
        }

        static readonly Dictionary<string, LabyrinthLayout> Layouts =
            new Dictionary<string, LabyrinthLayout>
        {
            // ── Laberinto 1: 17 paredes, rejilla 6×6 ramificada (no un único
            // pasillo): generado con backtracker recursivo + 7 lazos extra para
            // dar varias rutas viables, menos la pared (col2,f0)-(col3,f0) para
            // que el hueco de entrada del perímetro (centrado en X=0) no quede
            // partido en dos por una pared justo detrás. Complejidad media,
            // entre un pasillo simple y el laberinto 2 (56 paredes). Ver
            // diagrama de rejilla más arriba. Celdas de 1.5×1.5 centradas en
            // X/Z = -3.75,-2.25,-0.75,+0.75,+2.25,+3.75.
            ["laberinto"] = new LabyrinthLayout
            {
                perimeter = PERIMETER_STD,
                gridN = 6,
                cellSize = 1.5f,
                entCell = (2, 0),
                exitCell = (5, 5),
                interior = new (float, float, float, float)[]
                {
                    (-3.00f, -0.75f, T,    1.50f),  // (col0,f2)-(col1,f2)
                    (-1.50f, -3.75f, T,    1.50f),  // (col1,f0)-(col2,f0)
                    (-1.50f, -2.25f, T,    1.50f),  // (col1,f1)-(col2,f1)
                    (-2.25f, 1.50f, 1.50f, T   ),  // (col1,f3)-(col1,f4)
                    (-1.50f, 2.25f, T,    1.50f),  // (col1,f4)-(col2,f4)
                    (-0.75f, -1.50f, 1.50f, T   ),  // (col2,f1)-(col2,f2)
                    (-0.75f, 0.00f, 1.50f, T   ),  // (col2,f2)-(col2,f3)
                    (0.00f, 0.75f, T,    1.50f),  // (col2,f3)-(col3,f3)
                    (-0.75f, 3.00f, 1.50f, T   ),  // (col2,f4)-(col2,f5)
                    (0.75f, -3.00f, 1.50f, T   ),  // (col3,f0)-(col3,f1)
                    (0.75f, -1.50f, 1.50f, T   ),  // (col3,f1)-(col3,f2)
                    (0.75f, 3.00f, 1.50f, T   ),  // (col3,f4)-(col3,f5)
                    (3.00f, -2.25f, T,    1.50f),  // (col4,f1)-(col5,f1)
                    (2.25f, -1.50f, 1.50f, T   ),  // (col4,f1)-(col4,f2)
                    (2.25f, 0.00f, 1.50f, T   ),  // (col4,f2)-(col4,f3)
                    (2.25f, 1.50f, 1.50f, T   ),  // (col4,f3)-(col4,f4)
                    (3.75f, 0.00f, 1.50f, T   ),  // (col5,f2)-(col5,f3)
                },
            },

            // ── Laberinto 2: 21 paredes, rejilla 7×7 ramificada (celdas de
            // 1.2857×1.2857 m = 9/7). Generado con backtracker recursivo + 15
            // lazos extra para dar varias rutas viables. Verificado por BFS:
            // resoluble, 30 ramificaciones, 3 callejones sin salida en 49
            // celdas. Bajado de 9×9/56 paredes a 7×7/21 paredes porque el
            // fence de Mission Planner/ArduPilot solo admite ~70 puntos en
            // total y, incluso fusionando tramos colineales, 56 paredes no
            // cabían (quedaban ~160 puntos); con 21 paredes se fusionan en 14
            // polígonos (60 puntos), dentro del límite.
            ["laberinto 2"] = new LabyrinthLayout
            {
                perimeter = PERIMETER_STD,
                gridN = 7,
                cellSize = 9f / 7f,
                entCell = (3, 0),
                exitCell = (6, 6),
                interior = new (float, float, float, float)[]
                {
                    (-3.8571f, -3.2143f, 1.2857f, T   ),  // (col0,f0)-(col0,f1)
                    (-3.2143f, -1.2857f, T,    1.2857f),  // (col0,f2)-(col1,f2)
                    (-3.2143f, 0.0000f, T,    1.2857f),  // (col0,f3)-(col1,f3)
                    (-3.2143f, 1.2857f, T,    1.2857f),  // (col0,f4)-(col1,f4)
                    (-3.2143f, 2.5714f, T,    1.2857f),  // (col0,f5)-(col1,f5)
                    (-1.9286f, -3.8571f, T,    1.2857f),  // (col1,f0)-(col2,f0)
                    (-1.9286f, -2.5714f, T,    1.2857f),  // (col1,f1)-(col2,f1)
                    (-1.9286f, 2.5714f, T,    1.2857f),  // (col1,f5)-(col2,f5)
                    (-2.5714f, 3.2143f, 1.2857f, T   ),  // (col1,f5)-(col1,f6)
                    (-0.6429f, -1.2857f, T,    1.2857f),  // (col2,f2)-(col3,f2)
                    (-1.2857f, 0.6429f, 1.2857f, T   ),  // (col2,f3)-(col2,f4)
                    (0.6429f, -2.5714f, T,    1.2857f),  // (col3,f1)-(col4,f1)
                    (0.0000f, -0.6429f, 1.2857f, T   ),  // (col3,f2)-(col3,f3)
                    (0.0000f, 1.9286f, 1.2857f, T   ),  // (col3,f4)-(col3,f5)
                    (0.6429f, 2.5714f, T,    1.2857f),  // (col3,f5)-(col4,f5)
                    (1.9286f, 0.0000f, T,    1.2857f),  // (col4,f3)-(col5,f3)
                    (1.2857f, 1.9286f, 1.2857f, T   ),  // (col4,f4)-(col4,f5)
                    (2.5714f, -1.9286f, 1.2857f, T   ),  // (col5,f1)-(col5,f2)
                    (2.5714f, 1.9286f, 1.2857f, T   ),  // (col5,f4)-(col5,f5)
                    (3.8571f, -3.2143f, 1.2857f, T   ),  // (col6,f0)-(col6,f1)
                    (3.8571f, 1.9286f, 1.2857f, T   ),  // (col6,f4)-(col6,f5)
                },
            },

            // ── Laberinto 3: 20 paredes, rejilla 5×5 (celdas de 1.8×1.8 m).
            // Pasillo único sin ninguna bifurcación real. Ver diagrama de
            // rejilla más arriba.
            ["laberinto 3"] = new LabyrinthLayout
            {
                perimeter = PERIMETER_STD,
                gridN = 5,
                cellSize = 1.8f,
                entCell = (2, 0),
                exitCell = (4, 4),
                arrowsAlongFullPath = true,
                interior = new (float, float, float, float)[]
                {
                    (-3.60f, -2.70f, 1.80f, T   ),  // (col0,f0)-(col0,f1)
                    (-3.60f, -0.90f, 1.80f, T   ),  // (col0,f1)-(col0,f2)
                    (-3.60f,  0.90f, 1.80f, T   ),  // (col0,f2)-(col0,f3)
                    (-1.80f, -2.70f, 1.80f, T   ),  // (col1,f0)-(col1,f1)
                    (-1.80f,  0.90f, 1.80f, T   ),  // (col1,f2)-(col1,f3)
                    (-1.80f,  2.70f, 1.80f, T   ),  // (col1,f3)-(col1,f4)
                    ( 0.00f, -2.70f, 1.80f, T   ),  // (col2,f0)-(col2,f1)
                    ( 0.00f, -0.90f, 1.80f, T   ),  // (col2,f1)-(col2,f2)
                    ( 0.00f,  0.90f, 1.80f, T   ),  // (col2,f2)-(col2,f3)
                    ( 0.00f,  2.70f, 1.80f, T   ),  // (col2,f3)-(col2,f4)
                    ( 1.80f, -2.70f, 1.80f, T   ),  // (col3,f0)-(col3,f1)
                    ( 1.80f, -0.90f, 1.80f, T   ),  // (col3,f1)-(col3,f2)
                    ( 1.80f,  0.90f, 1.80f, T   ),  // (col3,f2)-(col3,f3)
                    ( 1.80f,  2.70f, 1.80f, T   ),  // (col3,f3)-(col3,f4)
                    ( 3.60f, -0.90f, 1.80f, T   ),  // (col4,f1)-(col4,f2)
                    ( 3.60f,  2.70f, 1.80f, T   ),  // (col4,f3)-(col4,f4)
                    (-2.70f, -3.60f, T,    1.80f),  // (col0,f0)-(col1,f0)
                    (-0.90f, -3.60f, T,    1.80f),  // (col1,f0)-(col2,f0)
                    (-2.70f, -1.80f, T,    1.80f),  // (col0,f1)-(col1,f1)
                    (-2.70f,  0.00f, T,    1.80f),  // (col0,f2)-(col1,f2)
                },
            },
        };

        // ── Estado ────────────────────────────────────────────────────────────
        string _sceneName;
        csDronLink.Dron dron;
        bool geofenceActivo = false;

        // ── Ciclo de vida ─────────────────────────────────────────────────────
        async void Start()
        {
            try
            {
                await System.Threading.Tasks.Task.Yield();  // esperar un frame

                if (!Layouts.TryGetValue(_sceneName, out var layout))
                { Debug.LogWarning($"[Laberinto] Sin layout para '{_sceneName}'."); return; }

                var lab = FindDronelab();
                if (lab == null)
                { Debug.LogWarning("[Laberinto] 'dronelab' no encontrado."); return; }

                // 1. Crear GameObjects de todas las paredes
                var wallsParent = CreateWalls(lab, layout);

                // 1b. Flechas de orientación en los cruces, señalando hacia la salida
                SpawnExitArrows(wallsParent, layout);

                // 2. Construir escenario GPS (inclusión + exclusiones interiores).
                // Mission Planner/ArduPilot limitan el fence a ~70 puntos en total, así
                // que las paredes se fusionan primero (tramos colineales y contiguos se
                // unen en un único rectángulo) para no exceder ese límite.
                var mergedWalls = MergeCollinearWalls(layout.interior);
                var scenario = BuildScenario(lab.transform, mergedWalls);

                // 3. Reutilizar conexión de DroneMqttBridge
                dron = DroneMqttBridge.Instance?.GetDron();
                if (dron == null)
                { Debug.LogWarning("[Laberinto] GetDron() = null."); return; }

                // 4. Subir geofence al autopiloto en segundo plano
                int nInterior = layout.interior.Length;
                _ = System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        // brench (aviso de brecha) no puede ser null: EstableceEscenario lo
                        // invoca internamente sin comprobar null, y al omitirlo (dejando el
                        // valor por defecto) provoca un NullReferenceException aquí dentro.
                        dron.EstableceEscenario(
                            scenario,
                            brench: () => Debug.Log("[Laberinto] Aviso de brecha del geofence."),
                            timeoutMs: FENCE_TIMEOUT_MS);
                        dron.EscribirParametros(new List<(string, float)>
                        {
                            ("FENCE_TYPE",   7f),
                            ("FENCE_ACTION", 4f),   // Brake al cruzar el límite
                            ("FENCE_ENABLE", 1f),
                        });
                        geofenceActivo = true;
                        Debug.Log($"[Laberinto] Geofence activo: 1 inclusion + {nInterior} paredes.");
                    }
                    catch (System.TimeoutException)
                    { Debug.LogWarning("[Laberinto] Timeout — el autopiloto no respondió."); }
                    catch (System.Exception e)
                    { Debug.LogWarning($"[Laberinto] Error: {e}"); }
                });
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Laberinto] Excepción en Start: {e.Message}");
            }
        }

        void OnApplicationQuit() => ClearGeofence(waitMs: 2000);
        void OnDestroy()         => ClearGeofence(waitMs: 0);

        void ClearGeofence(int waitMs)
        {
            if (!geofenceActivo) return;
            geofenceActivo = false;
            var d = dron; dron = null;
            var task = System.Threading.Tasks.Task.Run(() =>
            {
                try   { d.EscribirParametros(new List<(string, float)> { ("FENCE_ENABLE", 0f) }); } catch { }
                try   { d.BorraEscenario(); Debug.Log("[Laberinto] Geofences borrados."); }
                catch (System.Exception e) { Debug.LogWarning($"[Laberinto] Error al borrar: {e.Message}"); }
            });
            if (waitMs > 0) task.Wait(waitMs);
        }

        // ── Creación de paredes ───────────────────────────────────────────────
        /// Crea todas las paredes en la raíz de la escena (no como hijos del dronelab).
        /// Las posiciones se calculan a partir del transform mundial del dronelab,
        /// pero el nodo padre no queda bajo él: permanecen visibles aunque sea invisible.
        Transform CreateWalls(GameObject lab, LabyrinthLayout layout)
        {
            var parentGO = new GameObject("LaberintoParedes");
            parentGO.transform.position   = lab.transform.position;
            parentGO.transform.rotation   = lab.transform.rotation;
            parentGO.transform.localScale = lab.transform.lossyScale;

            foreach (var (cx, cz, wx, wz) in layout.perimeter)
                SpawnWall(parentGO.transform, "perim", cx, cz, wx, wz);

            for (int i = 0; i < layout.interior.Length; i++)
            {
                var (cx, cz, wx, wz) = layout.interior[i];
                SpawnWall(parentGO.transform, $"IW{i + 1}", cx, cz, wx, wz);
            }

            return parentGO.transform;
        }

        Transform SpawnWall(Transform parent, string wallName, float cx, float cz, float wx, float wz)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = $"maze_{wallName}";
            wall.transform.SetParent(parent, worldPositionStays: false);

            // Extiende el lado LARGO de la pared medio grosor (T/2) por cada
            // extremo (el centro no cambia). Sin esto, dos paredes perpendiculares
            // que se tocan justo en el borde dejan un hueco en una de las cuatro
            // esquinas del cruce (ninguna de las dos cubre ese cuadrante); con el
            // solape, la esquina siempre queda completamente cubierta por ambas
            // paredes a la vez, así que se ve como una única esquina limpia.
            if (wx >= wz) wx += T; else wz += T;

            // WALL_H = 10m → localScale.y = 10 (dronelab.scale.y = 1)
            // Centrar en Y: localPosition.y = WALL_H / 2
            wall.transform.localPosition = new Vector3(cx, WALL_H * 0.5f, cz);
            wall.transform.localScale    = new Vector3(wx, WALL_H, wz);

            var mr = wall.GetComponent<MeshRenderer>();
            if (mr != null)
                ApplyWallMaterial(mr, wx, wz);

            return wall.transform;
        }

        // ── Flechas de orientación hacia la salida ────────────────────────────
        /// Instancia una flecha (Prefabs/Flecha_Amarillo) orientada hacia la
        /// siguiente celda del camino más corto de la entrada a la salida.
        /// Por defecto solo en los cruces reales (celda con 3 o más conexiones
        /// abiertas) de ese camino, para no saturar el laberinto de flechas —
        /// no se marcan los cruces que quedan fuera del camino (ramales/bucles
        /// alternativos). Si layout.arrowsAlongFullPath es true (laberinto 3,
        /// un pasillo sin bifurcaciones donde nunca habría ningún cruce real
        /// que marcar), se pone flecha en TODAS las celdas del camino.
        void SpawnExitArrows(Transform parent, LabyrinthLayout layout)
        {
            var arrows = ComputeExitArrows(layout);
            if (arrows.Count == 0) return;

            var prefab = Resources.Load<GameObject>("Prefabs/Flecha_Rojo");
            if (prefab == null)
            { Debug.LogWarning("[Laberinto] Prefabs/Flecha_Rojo no encontrado; sin flechas de salida."); return; }

            const float ARROW_SCALE  = 6f;   // el modelo mide ~0.13 m de largo a escala 1 → ~0.8 m
            const float ARROW_HEIGHT = 1.6f; // altura de vuelo cómoda, bien visible desde dentro de la celda

            foreach (var (cx, cz, rotY) in arrows)
            {
                var inst = (GameObject)Instantiate(prefab, parent);
                inst.name = "maze_ExitArrow";
                inst.transform.localPosition = new Vector3(cx, ARROW_HEIGHT, cz);
                // rotX=90: misma convención "vista frontal" que MapCreatorUI.flechaRotX.
                inst.transform.localRotation = Quaternion.Euler(90f, rotY, 0f);
                inst.transform.localScale    = Vector3.one * ARROW_SCALE;
            }

            Debug.Log($"[Laberinto] {arrows.Count} flecha(s) de orientación hacia la salida.");
        }

        /// Calcula, para cada cruce del camino más corto de entCell a exitCell,
        /// la posición del centro de su celda y el ángulo Y (grados) al que hay
        /// que orientar una flecha (con rotX=90, convención de MapCreatorUI: en
        /// rotY=0 apunta a -X, y rotY gira en sentido de las agujas del reloj
        /// visto desde arriba) para que señale la siguiente celda de ese camino.
        /// Vacío si el layout no tiene rejilla real (gridN == 0).
        static List<(float cx, float cz, float rotYdeg)> ComputeExitArrows(LabyrinthLayout layout)
        {
            var result = new List<(float, float, float)>();
            if (layout.gridN <= 0) return result;

            int N = layout.gridN;
            float cell = layout.cellSize;
            float CellX(int c) => -4.5f + cell * c + cell * 0.5f;
            float CellZ(int r) => -4.5f + cell * r + cell * 0.5f;

            // Reconstruye, a partir de cada rectángulo de pared interior, qué
            // arista (celda,celda) bloquea.
            var closed = new HashSet<(int c, int r, int c2, int r2)>();
            foreach (var (cx, cz, wx, wz) in layout.interior)
            {
                if (wx >= wz)
                {
                    // Pared "horizontal" (larga en X): separa fila r de fila r+1 en la columna c.
                    int r1 = Mathf.RoundToInt((cz + 4.5f) / cell) - 1;
                    int c1 = Mathf.RoundToInt((cx + 4.5f - cell * 0.5f) / cell);
                    closed.Add((c1, r1, c1, r1 + 1));
                }
                else
                {
                    // Pared "vertical" (larga en Z): separa columna c de columna c+1 en la fila r.
                    int c1 = Mathf.RoundToInt((cx + 4.5f) / cell) - 1;
                    int r1 = Mathf.RoundToInt((cz + 4.5f - cell * 0.5f) / cell);
                    closed.Add((c1, r1, c1 + 1, r1));
                }
            }

            List<(int c, int r)> OpenNeighbors(int c, int r)
            {
                var list = new List<(int, int)>();
                void TryAdd(int nc, int nr)
                {
                    if (nc < 0 || nc >= N || nr < 0 || nr >= N) return;
                    if (closed.Contains((c, r, nc, nr)) || closed.Contains((nc, nr, c, r))) return;
                    list.Add((nc, nr));
                }
                TryAdd(c + 1, r); TryAdd(c - 1, r); TryAdd(c, r + 1); TryAdd(c, r - 1);
                return list;
            }

            // BFS desde la salida: parent[celda] = vecina abierta más cercana a la salida.
            var parent  = new Dictionary<(int, int), (int, int)>();
            var visited = new HashSet<(int, int)> { layout.exitCell };
            var queue   = new Queue<(int, int)>();
            queue.Enqueue(layout.exitCell);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                foreach (var n in OpenNeighbors(cur.Item1, cur.Item2))
                {
                    if (visited.Contains(n)) continue;
                    visited.Add(n);
                    parent[n] = cur;
                    queue.Enqueue(n);
                }
            }

            // Solo se marcan los cruces que están sobre el camino más corto de la
            // entrada a la salida (no todos los cruces del laberinto: la mayoría
            // pertenecen a bucles/ramales alternativos que el piloto no necesita
            // que se le señalen). Se recorre ese camino siguiendo los "parent"
            // desde la entrada hasta la salida.
            if (!parent.ContainsKey(layout.entCell) && layout.entCell != layout.exitCell) return result;
            var pathCells = new List<(int c, int r)> { layout.entCell };
            var cursor = layout.entCell;
            while (cursor != layout.exitCell && parent.TryGetValue(cursor, out var stepNext))
            {
                cursor = stepNext;
                pathCells.Add(cursor);
            }

            foreach (var (c, r) in pathCells)
            {
                if ((c, r) == layout.exitCell) continue;
                if (!layout.arrowsAlongFullPath && OpenNeighbors(c, r).Count < 3) continue; // no es un cruce real
                if (!parent.TryGetValue((c, r), out var next)) continue; // inalcanzable (no debería pasar)

                float dx = CellX(next.Item1) - CellX(c);
                float dz = CellZ(next.Item2) - CellZ(r);
                float len = Mathf.Sqrt(dx * dx + dz * dz);
                if (len < 0.001f) continue;
                dx /= len; dz /= len;

                // rotY=0 apunta a -X, rotY=90 apunta a +Z (verificado por render):
                // dir = (-cos(rotY), sin(rotY)) → rotY = atan2(dz, -dx).
                float rotY = Mathf.Atan2(dz, -dx) * Mathf.Rad2Deg;
                result.Add((CellX(c), CellZ(r), rotY));
            }

            return result;
        }

        // ── Fusión de paredes colineales y contiguas ──────────────────────────
        /// Dos paredes con la misma orientación, mismo grosor y mismo eje
        /// transversal (misma cz para paredes "horizontales", misma cx para
        /// "verticales") que se tocan o solapan en su eje largo se funden en
        /// un único rectángulo equivalente (misma área total, sin huecos ni
        /// solapes). Reduce drásticamente el número de polígonos de exclusión
        /// enviados al fence sin cambiar la geometría real de las paredes.
        static (float cx, float cz, float wx, float wz)[] MergeCollinearWalls(
            (float cx, float cz, float wx, float wz)[] walls)
        {
            const float eps = 0.01f;
            var hGroups = new Dictionary<float, List<(float s, float e)>>(); // cz -> [xStart,xEnd]
            var vGroups = new Dictionary<float, List<(float s, float e)>>(); // cx -> [zStart,zEnd]

            foreach (var (cx, cz, wx, wz) in walls)
            {
                if (wx >= wz)
                {
                    if (!hGroups.TryGetValue(cz, out var list)) hGroups[cz] = list = new List<(float, float)>();
                    list.Add((cx - wx * 0.5f, cx + wx * 0.5f));
                }
                else
                {
                    if (!vGroups.TryGetValue(cx, out var list)) vGroups[cx] = list = new List<(float, float)>();
                    list.Add((cz - wz * 0.5f, cz + wz * 0.5f));
                }
            }

            List<(float s, float e)> MergeIntervals(List<(float s, float e)> intervals)
            {
                intervals.Sort((a, b) => a.s.CompareTo(b.s));
                var merged = new List<(float s, float e)>();
                foreach (var (s, e) in intervals)
                {
                    int last = merged.Count - 1;
                    if (last >= 0 && s <= merged[last].e + eps)
                        merged[last] = (merged[last].s, Mathf.Max(merged[last].e, e));
                    else
                        merged.Add((s, e));
                }
                return merged;
            }

            var result = new List<(float, float, float, float)>();
            foreach (var (cz, intervals) in hGroups)
                foreach (var (s, e) in MergeIntervals(intervals))
                    result.Add(((s + e) * 0.5f, cz, e - s, T));
            foreach (var (cx, intervals) in vGroups)
                foreach (var (s, e) in MergeIntervals(intervals))
                    result.Add((cx, (s + e) * 0.5f, T, e - s));

            return result.ToArray();
        }

        // ── Construcción del escenario GPS ────────────────────────────────────
        static List<List<(float lat, float lon)>> BuildScenario(
            Transform dronelab, (float cx, float cz, float wx, float wz)[] interiorWalls)
        {
            var scenario = new List<List<(float lat, float lon)>>();

            // Polígono de inclusión: límites del dronelab
            scenario.Add(RingFromPlane(dronelab, FENCE_INNER));

            // Polígonos de exclusión: paredes interiores del laberinto (ya fusionadas)
            foreach (var (cx, cz, wx, wz) in interiorWalls)
                scenario.Add(RingFromRect(dronelab, cx, cz, wx, wz));

            int totalPoints = (1 + interiorWalls.Length) * 4;
            Debug.Log($"[Laberinto] Escenario GPS: 1 inclusión + {interiorWalls.Length} exclusiones "
                + $"({totalPoints} puntos totales).");
            return scenario;
        }

        static List<(float lat, float lon)> RingFromPlane(Transform t, float inner)
        {
            return CornersToRing(t, new[]
            {
                new Vector3(-inner, 0f, -inner),
                new Vector3( inner, 0f, -inner),
                new Vector3( inner, 0f,  inner),
                new Vector3(-inner, 0f,  inner),
            });
        }

        static List<(float lat, float lon)> RingFromRect(Transform dronelab, float cx, float cz, float wx, float wz)
        {
            float hx = wx * 0.5f, hz = wz * 0.5f;
            return CornersToRing(dronelab, new[]
            {
                new Vector3(cx - hx, 0f, cz - hz),
                new Vector3(cx + hx, 0f, cz - hz),
                new Vector3(cx + hx, 0f, cz + hz),
                new Vector3(cx - hx, 0f, cz + hz),
            });
        }

        static List<(float lat, float lon)> CornersToRing(Transform t, Vector3[] localCorners)
        {
            var ring = new List<(float lat, float lon)>();
            foreach (var c in localCorners)
            {
                Vector3 w = t.TransformPoint(c);
                ring.Add(WorldToGPS(w.x, w.z));
            }
            return ring;
        }

        // ── Utilidades ────────────────────────────────────────────────────────
        static GameObject FindDronelab()
        {
            foreach (var tr in Resources.FindObjectsOfTypeAll<Transform>())
                if (tr.gameObject.name == "dronelab" && tr.gameObject.scene.IsValid())
                    return tr.gameObject;
            return null;
        }

        static (float lat, float lon) WorldToGPS(float wx, float wz)
        {
            float lat    = (wz + OFFSET_Z) / METERS_PER_DEG;
            float cosLat = Mathf.Cos(lat * Mathf.Deg2Rad);
            float lon    = (wx + OFFSET_X) / (cosLat * METERS_PER_DEG);
            return (lat, lon);
        }
    }
}
