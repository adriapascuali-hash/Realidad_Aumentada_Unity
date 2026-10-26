using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using TMPro;


namespace ARDashboard
{
    // ══════════════════════════════════════════════════════════════
    // DATOS
    // ══════════════════════════════════════════════════════════════
    public enum ScenarioType { Urbano, Natural, Indoor, Hostil, Training, Custom }
    public enum ScenarioDifficulty { Facil = 1, Media = 2, Dificil = 3 }

    [Serializable]
    public class ScenarioData
    {
        public string             id;
        public string             name;
        public string             description;
        public ScenarioType       type;
        public ScenarioDifficulty difficulty;
        public string             iconKey;
        public bool               isBuiltin;
        public string             createdAt;
        public string             sceneName;
        public string             mapObjectsJson;
        public float              refLat;
        public float              refLon;
        public float              mapCenterLat;
        public float              mapCenterLon;
        // false (valor por defecto de bool, incluido para escenarios ya
        // guardados sin este campo) = geofence activado, mismo comportamiento
        // que tenían todas las misiones antes de que existiera este ajuste.
        public bool                geofenceDisabled;

        public ScenarioData(string name, string description,
                            ScenarioType type, ScenarioDifficulty difficulty, string iconKey)
        {
            this.id          = Guid.NewGuid().ToString();
            this.name        = name;
            this.description = description;
            this.type        = type;
            this.difficulty  = difficulty;
            this.iconKey     = iconKey;
            this.isBuiltin   = false;
            this.createdAt   = DateTime.Now.ToString("yyyy-MM-dd");
            this.sceneName   = "";
        }

        public ScenarioData(string id, string name, string description,
                            ScenarioType type, ScenarioDifficulty difficulty,
                            string iconKey, bool isBuiltin, string sceneName = "")
        {
            this.id          = id;
            this.name        = name;
            this.description = description;
            this.type        = type;
            this.difficulty  = difficulty;
            this.iconKey     = iconKey;
            this.isBuiltin   = isBuiltin;
            this.createdAt   = "2024-01-10";
            this.sceneName   = sceneName;
        }
    }

    [Serializable]
    public class ScenarioList
    {
        public List<ScenarioData> scenarios = new List<ScenarioData>();
    }

    [Serializable]
    public class MapPlacedObject
    {
        public float  lat, lon;
        public string type;
        public float  height;           // centro del aro sobre el suelo (0 → usa 3 m por defecto)
        public float  diameter;         // diámetro del aro en metros (0 → usa 3 m por defecto)
        public float  rotX, rotY, rotZ; // rotación Euler del aro en grados
        public string aroVariant = "Rojo"; // qué objeto de aro (entre Assets/Resources/Prefabs/Aro_*) se coloca
        public string banderaVariant = "Rojo"; // qué objeto de bandera (entre Assets/Resources/Prefabs/Bandera_*) se coloca
        public string obstaculoVariant = "Rojo"; // qué objeto de obstáculo (entre Assets/Resources/Prefabs/Obstaculo_*) se coloca
        public string monedaVariant = "Rojo"; // qué objeto de moneda (entre Assets/Resources/Prefabs/Moneda_*) se coloca
        public string arcoVariant = "Rojo"; // qué objeto de arco (entre Assets/Resources/Prefabs/Arco_*) se coloca
        public string arco2Variant = "Rojo"; // qué objeto de arco 2 (entre Assets/Resources/Prefabs/Arco2_*) se coloca
        public string flechaVariant = "Rojo"; // qué objeto de flecha (entre Assets/Resources/Prefabs/Flecha_*) se coloca
        public string obstaculoSupportVariant = "Negro"; // color de los soportes del obstáculo (independiente del cuerpo)
        public float  arcoLongHoriz; // longitud horizontal del arco en metros (0 → usa 3 m por defecto)
        public float  arcoLongVert;  // longitud vertical del arco en metros (0 → usa 3 m por defecto)
        public float  arco2LongHoriz; // longitud horizontal del arco 2 en metros (0 → usa 3 m por defecto)
        public float  arco2LongVert;  // longitud vertical del arco 2 en metros (0 → usa 3 m por defecto)
        public float  obstaculoLongHoriz; // longitud horizontal del obstáculo en metros (0 → usa 3 m por defecto)
        public float  obstaculoLongVert;  // longitud vertical del obstáculo en metros (0 → usa 3 m por defecto)
        public float  aroGrosor    = 1f; // factor de escala del grosor del tubo del aro (1 = grosor original del modelo)
        public float  flechaGrosor = 1f; // factor de escala del grosor de la flecha (1 = grosor original del modelo)
        public float  flechaLongHoriz; // longitud horizontal de la flecha en metros (0 → usa 3 m por defecto)
        public float  aroHueco;    // agranda/encoge el hueco central del aro (positivo = banda más ancha/hueco menor)

        // Todos los campos son value types (float) o strings inmutables, así
        // que una copia superficial ya es una copia completa e independiente.
        public MapPlacedObject Clone() => (MapPlacedObject)MemberwiseClone();
    }

    // Una línea de alineación ya "asentada" en el editor de misión (ver
    // MapCreatorUI.settledGroups) — se guarda junto a los objetos para que,
    // al reabrir la misión para editarla, las líneas (incluidas las giradas)
    // reaparezcan tal cual se dejaron, en vez de perderse al cerrar el editor.
    [Serializable]
    public class LineGroup
    {
        public List<int> indices = new List<int>(); // posiciones dentro de MapObjectList.objects
        public Vector2    dir;                       // dirección unitaria de la línea
    }

    [Serializable]
    public class MapObjectList
    {
        public List<MapPlacedObject>  objects    = new List<MapPlacedObject>();
        public List<LineGroup>        lineGroups = new List<LineGroup>();
    }

    // ══════════════════════════════════════════════════════════════
    // REPOSITORIO
    // ══════════════════════════════════════════════════════════════
    public static class ScenarioRepository
    {
        static string SavePath
        {
            get
            {
#if UNITY_EDITOR
                // En el editor guarda en la raíz del proyecto (sobrevive al Stop)
                string dir = Path.Combine(Application.dataPath, "..", "SavedScenarios");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return Path.Combine(dir, "scenarios.json");
#else
                return Path.Combine(Application.persistentDataPath, "scenarios.json");
#endif
            }
        }

        static readonly HashSet<string> HiddenScenes = new HashSet<string>(
            System.StringComparer.OrdinalIgnoreCase)
        {
            "Inicio", "Menú", "Configuración", "SampleScene", "Circuito1", "circuito2", "juego2"
        };

        // Nombre mostrado en la tarjeta del selector cuando difiere del nombre
        // real de la escena (que no se toca, para no romper las comparaciones
        // por nombre de escena repartidas por el código).
        static readonly Dictionary<string, string> DisplayNameOverrides = new Dictionary<string, string>
        {
            ["shooter"]      = "Disparar al blanco",
            ["laberinto"]    = "Laberinto 1",
            ["laberinto 2"]  = "Laberinto 2",
            ["laberinto 3"]  = "Laberinto 3",
        };

        static List<ScenarioData> GetBuiltins()
        {
            var list = new List<ScenarioData>();
            int count = SceneManager.sceneCountInBuildSettings;

            for (int i = 0; i < count; i++)
            {
                string path = UnityEngine.SceneManagement.SceneUtility.GetScenePathByBuildIndex(i);
                string sceneName = System.IO.Path.GetFileNameWithoutExtension(path);
                if (string.IsNullOrEmpty(sceneName)) continue;
                if (HiddenScenes.Contains(sceneName)) continue;

                string displayName = DisplayNameOverrides.TryGetValue(sceneName, out var overrideName)
                    ? overrideName : sceneName;

                list.Add(new ScenarioData(
                    id:          "b_" + sceneName,
                    name:        displayName,
                    description: "No se puede eliminar.",
                    type:        ScenarioType.Custom,
                    difficulty:  ScenarioDifficulty.Media,
                    iconKey:     "icon_custom",
                    isBuiltin:   true,
                    sceneName:   sceneName
                ));
            }

            return list;
        }

        public static List<ScenarioData> LoadAll()
        {
            var builtins = GetBuiltins();

            if (!File.Exists(SavePath))
            {
                // Primer arranque en este dispositivo/instalación (SavePath cae en
                // Application.persistentDataPath fuera del Editor, que siempre
                // empieza vacío en una build nueva — ver comentario de SavePath).
                // Se siembra además con las misiones personalizadas incluidas de
                // fábrica en StreamingAssets, para que estén disponibles en
                // cualquier build sin depender de SavedScenarios/ (que solo existe
                // dentro del proyecto del Editor y nunca se empaqueta).
                var seeded = new List<ScenarioData>(builtins);
                seeded.AddRange(LoadBundledDefaults());
                var init = new ScenarioList { scenarios = seeded };
                Write(init);
                return seeded;
            }
            try
            {
                var list = JsonUtility.FromJson<ScenarioList>(File.ReadAllText(SavePath));
                if (list?.scenarios == null) return builtins;

                // Eliminar entradas ocultas que pudieran estar ya en el JSON
                list.scenarios.RemoveAll(sc => HiddenScenes.Contains(sc.sceneName));


                // Custom scenarios (MapCreator) always have GUID ids; builtins use "b_" prefix.
                // Use the id prefix as the reliable discriminator — the isBuiltin flag in the
                // JSON may be stale if a previous run incorrectly wrote it.
                var customSceneNames = new HashSet<string>();
                foreach (var sc in list.scenarios)
                {
                    if (!sc.id.StartsWith("b_") && !string.IsNullOrEmpty(sc.sceneName))
                    {
                        sc.isBuiltin = false;           // repair any incorrectly persisted flag
                        customSceneNames.Add(sc.sceneName);
                    }
                }

                // Purge stale builtin entries already written to the JSON whose scene is
                // owned by a custom scenario (left over from previous runs), OR whose
                // scene doesn't exist anymore at all (removed from Build Settings —
                // p. ej. una escena de laberinto borrada); sin esto seguían apareciendo
                // para siempre en el selector aunque ya no se pudieran cargar.
                var currentBuiltinIds = new HashSet<string>();
                foreach (var b in builtins) currentBuiltinIds.Add(b.id);
                list.scenarios.RemoveAll(sc =>
                    sc.id.StartsWith("b_") &&
                    (customSceneNames.Contains(sc.sceneName) || !currentBuiltinIds.Contains(sc.id)));

                foreach (var builtin in builtins)
                {
                    if (customSceneNames.Contains(builtin.sceneName)) continue;

                    var existing = list.scenarios.Find(x => x.id == builtin.id);
                    if (existing == null)
                        list.scenarios.Add(builtin);
                    else
                    {
                        existing.sceneName   = builtin.sceneName;
                        existing.name        = builtin.name;
                        existing.description = builtin.description;
                        existing.isBuiltin   = true;
                    }
                }
                Write(list);
                return list.scenarios;
            }
            catch { return builtins; }
        }

        // Misiones personalizadas incluidas de fábrica (Assets/StreamingAssets/
        // DefaultScenarios/scenarios.json, sí se empaqueta en la build a
        // diferencia de SavedScenarios/) — se usan solo para sembrar SavePath la
        // primera vez que la app arranca en un dispositivo, nunca se leen después.
        // Filtra los "b_*" (builtins) del propio volcado: esos ya los aporta
        // GetBuiltins() por separado, e incluir ambos duplicaría las filas.
        static List<ScenarioData> LoadBundledDefaults()
        {
            try
            {
                string path = Path.Combine(Application.streamingAssetsPath, "DefaultScenarios", "scenarios.json");
                if (!File.Exists(path)) return new List<ScenarioData>();

                var list = JsonUtility.FromJson<ScenarioList>(File.ReadAllText(path));
                if (list?.scenarios == null) return new List<ScenarioData>();

                list.scenarios.RemoveAll(sc => sc.id.StartsWith("b_"));
                foreach (var sc in list.scenarios) sc.isBuiltin = false;
                return list.scenarios;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ScenarioRepository] Error cargando misiones de fábrica: {e.Message}");
                return new List<ScenarioData>();
            }
        }

        public static void Add(ScenarioData s, List<ScenarioData> list)
        {
            list.Insert(0, s);
            Save(list);
        }

        public static bool Remove(string id, List<ScenarioData> list)
        {
            var t = list.Find(x => x.id == id);
            if (t == null || t.isBuiltin) return false;
            list.Remove(t);
            Save(list);
            return true;
        }

        public static void Save(List<ScenarioData> list) =>
            Write(new ScenarioList { scenarios = list });

        static void Write(ScenarioList w)
        {
            try { File.WriteAllText(SavePath, JsonUtility.ToJson(w, prettyPrint: true)); }
            catch (Exception e) { Debug.LogError("[ScenarioRepository] " + e.Message); }
        }
    }

    // ══════════════════════════════════════════════════════════════
    // ESCENARIO SELECCIONADO
    // ══════════════════════════════════════════════════════════════
    public static class SelectedScenario
    {
        public static ScenarioData Current      { get; set; }
        public static bool         HasSelection => Current != null;
        public static void         Clear()      => Current = null;
    }

    // ══════════════════════════════════════════════════════════════
    // FPV TARGETING OVERLAY
    // ══════════════════════════════════════════════════════════════
    public class FPVTargetingOverlay : MonoBehaviour
    {
        static readonly Color C_CYAN   = new Color(0.00f, 0.95f, 1.00f, 1f);
        static readonly Color C_ACCENT = new Color(0.15f, 1.00f, 0.70f, 1f);
        static readonly Color C_WARN   = new Color(1.00f, 0.82f, 0.00f, 1f);
        static readonly Color C_SUB    = new Color(0.42f, 0.58f, 0.72f, 0.9f);

        float time      = 0f;
        float lockTimer = 0f;
        bool  locked    = false;
        const float LOCK_DURATION = 2.8f;

        float panelW = 680f;
        float panelH = 680f;

        RectTransform cornerTL, cornerTR, cornerBL, cornerBR;
        TextMeshProUGUI lockText, distText, statusText;
        RectTransform targetContainer;
        CanvasGroup lockIconGroup;

        // ── Fade-in FPV ───────────────────────────────────────────
        CanvasGroup fpvGroup;
        float fadeTime  = 0f;
        const float FADE_IN_DURATION = 1.0f;
        const float FADE_IN_DELAY    = 0.0f;

        public void Init(RectTransform cardRT)
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) { Debug.LogError("[FPVOverlay] No Canvas found"); return; }
            var ct = canvas.transform;

            // Adaptar el encuadre al tamaño real de la tarjeta pasada
            if (cardRT != null)
            {
                panelW = cardRT.sizeDelta.x;
                panelH = cardRT.sizeDelta.y;
            }

            var cGO = new GameObject("FPV_Container");
            cGO.transform.SetParent(ct, false);
            targetContainer = cGO.AddComponent<RectTransform>();
            targetContainer.anchorMin = targetContainer.anchorMax = new Vector2(0.5f, 0.5f);
            targetContainer.pivot     = new Vector2(0.5f, 0.5f);
            targetContainer.sizeDelta = Vector2.zero;
            targetContainer.anchoredPosition = Vector2.zero;
            fpvGroup       = cGO.AddComponent<CanvasGroup>();
            fpvGroup.alpha = 0f;

            BuildCornerBrackets(targetContainer);
            BuildGuideCrosses(targetContainer);
            BuildStatusLabels(ct);
        }

        void BuildCornerBrackets(RectTransform parent)
        {
            float cxOuter = panelW * 0.5f + 38f;
            float cyOuter = panelH * 0.5f + 38f;
            float lenOuter = 70f;
            float thkOuter = 5f;

            cornerTL = MakeCorner(parent, "TL", new Vector2(-cxOuter,  cyOuter), lenOuter, thkOuter,  1f, -1f, 0.06f);
            cornerTR = MakeCorner(parent, "TR", new Vector2( cxOuter,  cyOuter), lenOuter, thkOuter, -1f, -1f, 0.06f);
            cornerBL = MakeCorner(parent, "BL", new Vector2(-cxOuter, -cyOuter), lenOuter, thkOuter,  1f,  1f, 0.06f);
            cornerBR = MakeCorner(parent, "BR", new Vector2( cxOuter, -cyOuter), lenOuter, thkOuter, -1f,  1f, 0.06f);

            float tx = 24f;
            MakeBar(parent, new Vector2(-cxOuter + tx,               cyOuter - thkOuter * 0.5f), new Vector2(1.5f, 16f), C_CYAN, 0.06f);
            MakeBar(parent, new Vector2(-cxOuter + thkOuter * 0.5f,  cyOuter - tx             ), new Vector2(16f, 1.5f), C_CYAN, 0.06f);
            MakeBar(parent, new Vector2( cxOuter - tx,               cyOuter - thkOuter * 0.5f), new Vector2(1.5f, 16f), C_CYAN, 0.06f);
            MakeBar(parent, new Vector2( cxOuter - thkOuter * 0.5f,  cyOuter - tx             ), new Vector2(16f, 1.5f), C_CYAN, 0.06f);
            MakeBar(parent, new Vector2(-cxOuter + tx,              -cyOuter + thkOuter * 0.5f), new Vector2(1.5f, 16f), C_CYAN, 0.06f);
            MakeBar(parent, new Vector2(-cxOuter + thkOuter * 0.5f, -cyOuter + tx             ), new Vector2(16f, 1.5f), C_CYAN, 0.06f);
            MakeBar(parent, new Vector2( cxOuter - tx,              -cyOuter + thkOuter * 0.5f), new Vector2(1.5f, 16f), C_CYAN, 0.06f);
            MakeBar(parent, new Vector2( cxOuter - thkOuter * 0.5f, -cyOuter + tx             ), new Vector2(16f, 1.5f), C_CYAN, 0.06f);

            float midBarLen = 22f, midBarThk = 3.5f;
            MakeBar(parent, new Vector2(0f,  cyOuter),                         new Vector2(midBarLen*2f, midBarThk), C_CYAN, 0.06f);
            MakeBar(parent, new Vector2(0f,  cyOuter - midBarLen*0.5f - 2f),   new Vector2(midBarThk, midBarLen),   C_CYAN, 0.06f);
            MakeBar(parent, new Vector2(0f, -cyOuter),                         new Vector2(midBarLen*2f, midBarThk), C_CYAN, 0.06f);
            MakeBar(parent, new Vector2(0f, -cyOuter + midBarLen*0.5f + 2f),   new Vector2(midBarThk, midBarLen),   C_CYAN, 0.06f);
            MakeBar(parent, new Vector2(-cxOuter, 0f),                         new Vector2(midBarThk, midBarLen*2f), C_CYAN, 0.06f);
            MakeBar(parent, new Vector2(-cxOuter + midBarLen*0.5f + 2f, 0f),   new Vector2(midBarLen, midBarThk),   C_CYAN, 0.06f);
            MakeBar(parent, new Vector2( cxOuter, 0f),                         new Vector2(midBarThk, midBarLen*2f), C_CYAN, 0.06f);
            MakeBar(parent, new Vector2( cxOuter - midBarLen*0.5f - 2f, 0f),   new Vector2(midBarLen, midBarThk),   C_CYAN, 0.06f);
        }

        void MakeScreenCorner(RectTransform parent, string name,
            Vector2 pos, float len, float thk, float hDir, float vDir, float alpha)
        {
            var go = new GameObject("FPV_SC_" + name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = Vector2.zero;
            MakeBar(go.transform, new Vector2(hDir * len * 0.5f, 0), new Vector2(len, thk), C_CYAN, alpha);
            MakeBar(go.transform, new Vector2(0, vDir * len * 0.5f), new Vector2(thk, len), C_CYAN, alpha);
        }

        RectTransform MakeCorner(RectTransform parent, string name,
            Vector2 pos, float len, float thk, float hDir, float vDir, float alpha)
        {
            var go = new GameObject("FPV_Corner_" + name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = Vector2.zero;
            MakeBar(go.transform, new Vector2(hDir * len * 0.5f, 0), new Vector2(len, thk), C_CYAN, alpha);
            MakeBar(go.transform, new Vector2(0, vDir * len * 0.5f), new Vector2(thk, len), C_CYAN, alpha);
            return rt;
        }

        void BuildGuideCrosses(RectTransform parent)
        {
            float cx = panelW * 0.5f + 38f + 20f;
            float cy = panelH * 0.5f + 38f + 20f;
            MakeBar(parent, new Vector2(0,  cy), new Vector2(60f, 1.5f), C_CYAN, 0.06f);
            MakeBar(parent, new Vector2(0, -cy), new Vector2(60f, 1.5f), C_CYAN, 0.06f);
            MakeBar(parent, new Vector2(-cx, 0), new Vector2(1.5f, 60f), C_CYAN, 0.06f);
            MakeBar(parent, new Vector2( cx, 0), new Vector2(1.5f, 60f), C_CYAN, 0.06f);
        }

        void BuildStatusLabels(Transform canvasT)
        {
            float cx = panelW * 0.5f + 38f;
            float cy = panelH * 0.5f + 38f;

            var lgGO = new GameObject("FPV_LockText");
            lgGO.transform.SetParent(canvasT, false);
            lockText = lgGO.AddComponent<TextMeshProUGUI>();
            lockText.text = "ACQUIRING TARGET"; lockText.fontSize = 12f;
            lockText.fontStyle = FontStyles.Bold; lockText.color = C_WARN;
            lockText.alignment = TextAlignmentOptions.Center; lockText.characterSpacing = 24f;
            lockText.raycastTarget = false;
            var lgRt = lgGO.GetComponent<RectTransform>();
            lgRt.anchorMin = lgRt.anchorMax = new Vector2(0.5f, 0.5f);
            lgRt.pivot = new Vector2(0.5f, 0.5f);
            lgRt.anchoredPosition = new Vector2(10f, cy + 42f);
            lgRt.sizeDelta = new Vector2(360f, 22f);

            // Icono de mira (círculo + cruz + punto), igual que el del shooter,
            // en vez del carácter "◈" que la fuente no soporta (salía como □).
            lockIconGroup = BuildLockIcon(canvasT, new Vector2(-88f, cy + 42f), C_ACCENT);
            lockIconGroup.gameObject.SetActive(false);

            distText = MakeTelLabel(canvasT, new Vector2(0f, cy + 68f), TextAlignmentOptions.Center, "DIST: --.- m", new Vector2(280f, 26f), 16f);

            var stGO = new GameObject("FPV_Status");
            stGO.transform.SetParent(canvasT, false);
            statusText = stGO.AddComponent<TextMeshProUGUI>();
            statusText.fontStyle = FontStyles.Bold; statusText.color = C_ACCENT;
            statusText.alignment = TextAlignmentOptions.Right; statusText.characterSpacing = 14f;
            statusText.raycastTarget = false;
            var stRt = stGO.GetComponent<RectTransform>();
            stRt.anchorMin = stRt.anchorMax = new Vector2(0.5f, 0.5f);
            stRt.pivot = new Vector2(0.5f, 0.5f);
            stRt.anchoredPosition = new Vector2(cx - 8f, cy + 60f);
            stRt.sizeDelta = new Vector2(220f, 18f);

            lgGO.transform.SetParent(targetContainer, false);
            var distGO = distText.transform.gameObject;
            distGO.transform.SetParent(targetContainer, false);
            stGO.transform.SetParent(targetContainer, false);
        }

        TextMeshProUGUI MakeTelLabel(Transform parent, Vector2 pos,
            TextAlignmentOptions align, string text, Vector2 size, float fontSize = 10f)
        {
            var go  = new GameObject("FPV_Tel");
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = fontSize;
            tmp.color = new Color(C_CYAN.r, C_CYAN.g, C_CYAN.b, 0.11f);
            tmp.fontStyle = FontStyles.Bold; tmp.alignment = align;
            tmp.characterSpacing = 12f; tmp.raycastTarget = false;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            return tmp;
        }

        void MakeSmallLabel(RectTransform parent, Vector2 pos,
            TextAlignmentOptions align, string text, Color col)
        {
            var go  = new GameObject("FPV_CornerLbl");
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = 9f;
            tmp.color = new Color(col.r, col.g, col.b, 0.40f);
            tmp.fontStyle = FontStyles.Bold; tmp.alignment = align;
            tmp.characterSpacing = 14f; tmp.raycastTarget = false;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(200f, 16f);
        }

        void MakeBar(Transform parent, Vector2 pos, Vector2 size, Color col, float alpha)
        {
            var go  = new GameObject("Bar");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(col.r, col.g, col.b, alpha);
            img.raycastTarget = false;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
        }

        void Update()
        {
            time      += Time.deltaTime;
            lockTimer += Time.deltaTime;

            // ── Fade-in FPV ───────────────────────────────────────
            if (fpvGroup != null && fpvGroup.alpha < 1f)
            {
                fadeTime += Time.deltaTime;
                fpvGroup.alpha = Mathf.Clamp01(fadeTime / FADE_IN_DURATION);
            }

            float lockP = Mathf.Clamp01(lockTimer / LOCK_DURATION);

            if (!locked && lockP >= 1f)
            {
                locked = true;
                if (lockText) { lockText.text = "TARGET LOCKED"; lockText.color = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.10f); }
                if (lockIconGroup) { lockIconGroup.gameObject.SetActive(true); lockIconGroup.alpha = 0.10f; }
            }
            else if (!locked && lockText)
            {
                float blink = Mathf.Sin(time * 9f) > 0f ? 0.10f : 0.04f;
                lockText.color = new Color(C_WARN.r, C_WARN.g, C_WARN.b, blink);
                int dots = (int)(time * 2.5f) % 4;
                lockText.text = "ACQUIRING TARGET" + new string('.', dots);
                if (lockIconGroup) lockIconGroup.gameObject.SetActive(false);
            }
            else if (locked && lockText)
            {
                float p = (Mathf.Sin(time * 2f) + 1f) * 0.5f;
                float a = Mathf.Lerp(0.07f, 0.12f, p);
                lockText.color = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, a);
                if (lockIconGroup) lockIconGroup.alpha = a;
            }

            float breath = Mathf.Sin(time * 1.4f) * 4f;
            float cx = panelW * 0.5f + 38f + breath;
            float cy = panelH * 0.5f + 38f + breath;
            if (cornerTL) cornerTL.anchoredPosition = new Vector2(-cx,  cy);
            if (cornerTR) cornerTR.anchoredPosition = new Vector2( cx,  cy);
            if (cornerBL) cornerBL.anchoredPosition = new Vector2(-cx, -cy);
            if (cornerBR) cornerBR.anchoredPosition = new Vector2( cx, -cy);

            float dist = 12.4f + Mathf.Sin(time * 0.7f) * 1.8f;
            if (distText) distText.text = $"DIST: {dist:F1} m";

            if (statusText)
            {
                float sb = (Mathf.Sin(time * 3f) > 0.4f) ? 1f : 0.45f;
                statusText.color = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, sb);
            }

            if (targetContainer)
            {
                if (!locked)
                {
                    float driftMag = (1f - lockP) * 10f;
                    float dx = Mathf.Sin(time * 0.9f) * driftMag;
                    float dy = Mathf.Cos(time * 1.1f) * driftMag;
                    targetContainer.anchoredPosition = Vector2.Lerp(
                        targetContainer.anchoredPosition,
                        new Vector2(dx, dy), Time.deltaTime * 4f);
                }
                else
                {
                    targetContainer.anchoredPosition = Vector2.Lerp(
                        targetContainer.anchoredPosition,
                        Vector2.zero, Time.deltaTime * 6f);
                }
            }
        }

        Image MakeImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            return img;
        }

        // Mismo icono de mira (círculo segmentado + 4 marcas + punto central) que
        // dibuja ShooterController.cs junto a "TARGET LOCKED" en el juego real.
        CanvasGroup BuildLockIcon(Transform parent, Vector2 pos, Color color)
        {
            const float R = 5.5f, THK = 1.3f, LINE = 4f;
            const int SEG = 16;
            // Solid shapes read as noticeably brighter than thin text glyphs at
            // the same alpha, so bake in some extra dimming here to actually
            // match how faint "TARGET LOCKED" looks at the same CanvasGroup alpha.
            color = new Color(color.r, color.g, color.b, color.a * 0.4f);

            var rootGO = new GameObject("LockIcon");
            rootGO.transform.SetParent(parent, false);
            var group = rootGO.AddComponent<CanvasGroup>();
            var rootRt = rootGO.AddComponent<RectTransform>();
            rootRt.anchorMin = rootRt.anchorMax = rootRt.pivot = new Vector2(0.5f, 0.5f);
            rootRt.anchoredPosition = pos;
            rootRt.sizeDelta = Vector2.zero;

            for (int s = 0; s < SEG; s++)
            {
                float deg = s * 360f / SEG;
                float rad = deg * Mathf.Deg2Rad;
                float arcLen = 2f * Mathf.PI * R / SEG * 1.2f;
                var seg = MakeImage(rootGO.transform, "S", color);
                seg.raycastTarget = false;
                var segRt = seg.GetComponent<RectTransform>();
                segRt.anchorMin = segRt.anchorMax = segRt.pivot = new Vector2(0.5f, 0.5f);
                segRt.anchoredPosition = new Vector2(Mathf.Cos(rad) * R, Mathf.Sin(rad) * R);
                segRt.sizeDelta = new Vector2(THK, arcLen);
                segRt.localRotation = Quaternion.Euler(0f, 0f, deg);
            }

            Vector2[] tickPos =
            {
                new Vector2(0f, R + LINE * 0.5f), new Vector2(0f, -(R + LINE * 0.5f)),
                new Vector2(-(R + LINE * 0.5f), 0f), new Vector2(R + LINE * 0.5f, 0f),
            };
            for (int i = 0; i < 4; i++)
            {
                var tick = MakeImage(rootGO.transform, "T", color);
                tick.raycastTarget = false;
                var tickRt = tick.GetComponent<RectTransform>();
                tickRt.anchorMin = tickRt.anchorMax = tickRt.pivot = new Vector2(0.5f, 0.5f);
                tickRt.anchoredPosition = tickPos[i];
                tickRt.sizeDelta = i < 2 ? new Vector2(THK, LINE) : new Vector2(LINE, THK);
            }

            var dot = MakeImage(rootGO.transform, "D", color);
            dot.raycastTarget = false;
            var dotRt = dot.GetComponent<RectTransform>();
            dotRt.anchorMin = dotRt.anchorMax = dotRt.pivot = new Vector2(0.5f, 0.5f);
            dotRt.sizeDelta = new Vector2(2.5f, 2.5f);

            return group;
        }
    }

    // ══════════════════════════════════════════════════════════════
    // UI PRINCIPAL
    // ══════════════════════════════════════════════════════════════
    public class Menú : MonoBehaviour
    {
#if UNITY_EDITOR
        // Registered by ScenarioSceneCreator (Editor assembly) via [InitializeOnLoad]
        public static Func<ScenarioData, string>  EditorBuildScene;
        public static System.Action<ScenarioData> EditorDeleteScene;
#endif

        static readonly Color C_ACCENT  = new Color(0.15f, 1.00f, 0.70f, 1f);
        static readonly Color C_CYAN    = new Color(0.00f, 0.95f, 1.00f, 1f);
        static readonly Color C_BORDER  = new Color(0.16f, 0.22f, 0.32f, 1f);
        static readonly Color C_TEXT    = new Color(0.90f, 0.95f, 1.00f, 1f);
        static readonly Color C_SUB     = new Color(0.42f, 0.58f, 0.72f, 1f);
        static readonly Color C_DISAB   = new Color(0.18f, 0.22f, 0.30f, 1f);

        static readonly Color C_ROW_SEL = new Color(0.05f, 0.12f, 0.19f, 1f);
        static readonly Color C_GRAD_A  = new Color(0.06f, 0.14f, 0.28f, 1f);
        static readonly Color C_GRAD_B  = new Color(0.00f, 0.00f, 0.00f, 1f);

        string menuSceneName = "Inicio";
        float  pw            = 560f;

        List<ScenarioData> scenarios  = new List<ScenarioData>();
        List<GameObject>   rowObjects = new List<GameObject>();
        ScenarioData       selected;

        Transform       listContent;
        ScrollRect      listScrollRect;
        Button          btnPlay;
        Canvas          _canvas;
        GameObject      rowContextMenu;

        RectTransform   scaleLeftTicks, scaleRightTicks;
        float           scaleLeftVal  = 0f;
        float           scaleRightVal = 0f;
        TextMeshProUGUI altText, velText;
        float           hudTime = 0f;

        Image[] gradBands = new Image[50];

        // ── Fade-in HUD barras laterales ──────────────────────────
        CanvasGroup hudGroup;
        float       hudFadeTime     = 0f;
        const float HUD_FADE_DURATION = 0.4f;

        // ── Fade-in blur overlay ───────────────────────────────
        CanvasGroup blurGroup;
        float       blurFadeTime = 0f;


        void Start() => BuildUI();

        void Update()
        {
            hudTime += Time.deltaTime;


            float alt = 28.7f + Mathf.Sin(hudTime * 0.4f)  * 0.6f;
            float vel = Mathf.Abs(Mathf.Sin(hudTime * 1.2f) * 2.3f);

            if (altText) altText.text = $"ALT\n{alt:F1}";
            if (velText) velText.text = $"VEL\n{vel:F1}";

            float unitsStep = 5f, yStep = 56f, pxPerUnit = yStep / unitsStep;
            float altSnap = Mathf.Round(alt / unitsStep) * unitsStep;
            float velSnap = Mathf.Round(vel / unitsStep) * unitsStep;
            float altFrac = -(alt - altSnap) * pxPerUnit;
            float velFrac = -(vel - velSnap) * pxPerUnit;

            scaleLeftVal  = Mathf.Lerp(scaleLeftVal,  altFrac, Time.deltaTime * 6f);
            scaleRightVal = Mathf.Lerp(scaleRightVal, velFrac, Time.deltaTime * 6f);

            if (scaleLeftTicks)
            {
                var p = scaleLeftTicks.anchoredPosition;
                p.y = scaleLeftVal;
                scaleLeftTicks.anchoredPosition = p;
                UpdateScaleLabels(scaleLeftTicks, "L", altSnap, (int)unitsStep);
            }
            if (scaleRightTicks)
            {
                var p = scaleRightTicks.anchoredPosition;
                p.y = scaleRightVal;
                scaleRightTicks.anchoredPosition = p;
                UpdateScaleLabels(scaleRightTicks, "R", velSnap, (int)unitsStep);
            }

            // ── Fade-in HUD barras laterales ──────────────────────
            if (hudGroup != null && hudGroup.alpha < 1f)
            {
                hudFadeTime += Time.deltaTime;
                hudGroup.alpha = Mathf.Clamp01(hudFadeTime / HUD_FADE_DURATION);
            }

            // ── Fade-in blur overlay ───────────────────────────
            if (blurGroup != null && blurGroup.alpha < 1f)
            {
                blurFadeTime += Time.deltaTime;
                blurGroup.alpha = Mathf.Clamp01(blurFadeTime / HUD_FADE_DURATION);
            }

        }

        void BuildUI()
        {
            ShaderTimekeeper.ScannerEnabled = true;

            var canvasGO = new GameObject("ScenarioCanvas");
            canvasGO.transform.SetParent(transform);
            var canvas = canvasGO.AddComponent<Canvas>();
            _canvas = canvas;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 999;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGO.AddComponent<GraphicRaycaster>();

            var bgContainerGO = new GameObject("BG_Container");
            bgContainerGO.transform.SetParent(canvasGO.transform, false);
            var bgContainerRt = bgContainerGO.AddComponent<RectTransform>();
            bgContainerRt.anchorMin = Vector2.zero;
            bgContainerRt.anchorMax = Vector2.one;
            bgContainerRt.offsetMin = bgContainerRt.offsetMax = Vector2.zero;

            var bg = MakeImage(bgContainerGO.transform, "BG", new Color(0.00f, 0.00f, 0.00f, 1f));
            Stretch(bg); bg.raycastTarget = false;

            const int gradCount  = 50;
            var gradStart = new Color(0.00f, 0.00f, 0.00f, 1f);   // negro abajo
            var gradEnd   = new Color(0.08f, 0.22f, 0.48f, 1f);   // azul más vivo arriba
            for (int i = 0; i < gradCount; i++)
            {
                float t    = i / (float)(gradCount - 1);
                float yMin = i / (float)gradCount;
                float yMax = (i + 1) / (float)gradCount;
                var bandGO = new GameObject("GradBand" + i);
                bandGO.transform.SetParent(bgContainerGO.transform, false);
                gradBands[i] = bandGO.AddComponent<Image>();
                gradBands[i].color = Color.Lerp(gradStart, gradEnd, t);
                gradBands[i].raycastTarget = false;
                var bRt = bandGO.GetComponent<RectTransform>();
                bRt.anchorMin = new Vector2(0f, yMin);
                bRt.anchorMax = new Vector2(1f, yMax);
                bRt.offsetMin = bRt.offsetMax = Vector2.zero;
            }

            var bgAnim = MakeImage(bgContainerGO.transform, "BGAnim", Color.white);
            Stretch(bgAnim);
            var mat = ShaderTimekeeper.GetMaterial();
            if (mat != null) bgAnim.material = mat;
            bgAnim.raycastTarget = false;

            var blurContainerGO = new GameObject("FPVBlur_Container");
            blurContainerGO.transform.SetParent(bgContainerGO.transform, false);
            blurGroup = blurContainerGO.AddComponent<CanvasGroup>();
            blurGroup.alpha = 0f;
            var blurContainerRt = blurContainerGO.AddComponent<RectTransform>();
            blurContainerRt.anchorMin = Vector2.zero;
            blurContainerRt.anchorMax = Vector2.one;
            blurContainerRt.offsetMin = blurContainerRt.offsetMax = Vector2.zero;
            var staticOverlay = MakeImage(blurContainerGO.transform, "FPVBlurOverlay", Color.white);
            Stretch(staticOverlay);
            var fpvMat = ShaderTimekeeper.GetFPVMaterial();
            if (fpvMat != null) staticOverlay.material = fpvMat;
            staticOverlay.raycastTarget = false;

            var hudContainerGO = new GameObject("HUD_Container");
            hudContainerGO.transform.SetParent(canvasGO.transform, false);
            hudGroup = hudContainerGO.AddComponent<CanvasGroup>();
            hudGroup.alpha = 0f;
            var hudContainerRt = hudContainerGO.AddComponent<RectTransform>();
            hudContainerRt.anchorMin = Vector2.zero;
            hudContainerRt.anchorMax = Vector2.one;
            hudContainerRt.offsetMin = hudContainerRt.offsetMax = Vector2.zero;

            BuildFPVBackgroundHUD(hudContainerGO.transform);

            // Capa invisible a pantalla completa para detectar clics fuera de
            // cualquier fila/botón y deseleccionar el escenario elegido. Va
            // detrás de "Window" y de las filas en la jerarquía (creada antes
            // que ellas), así que el raycaster solo la "atrapa" a ella cuando
            // el clic no cae encima de nada interactivo en ese punto.
            var deselectCatcherGO = new GameObject("DeselectCatcher");
            deselectCatcherGO.transform.SetParent(canvasGO.transform, false);
            var deselectImg = deselectCatcherGO.AddComponent<Image>();
            deselectImg.color = new Color(0f, 0f, 0f, 0f);
            Stretch(deselectImg);
            var deselectBtn = deselectCatcherGO.AddComponent<Button>();
            deselectBtn.transition = Selectable.Transition.None;
            deselectBtn.onClick.AddListener(DeselectAll);

            var card = MakeImage(canvasGO.transform, "Window", new Color(0.02f, 0.04f, 0.09f, 0.80f));
            var cardRt = card.GetComponent<RectTransform>();
            cardRt.anchorMin = cardRt.anchorMax = new Vector2(0.5f, 0.5f);
            cardRt.pivot = new Vector2(0.5f, 0.5f);
            cardRt.sizeDelta = new Vector2(pw + 60f, 680f);
            cardRt.anchoredPosition = Vector2.zero;
            AddOutline(card, C_BORDER);

            AddDarkGradient(card.transform);

            BuildCardGlowBorder(canvasGO.transform, cardRt);

            var bar = MakeImage(card.transform, "Bar", new Color(C_CYAN.r, C_CYAN.g, C_CYAN.b, 0.28f));
            var barRt = bar.GetComponent<RectTransform>();
            barRt.anchorMin = new Vector2(0, 1); barRt.anchorMax = new Vector2(0.1f, 1);
            barRt.pivot = new Vector2(0, 1);
            barRt.offsetMin = barRt.offsetMax = Vector2.zero;
            barRt.sizeDelta = new Vector2(0, 4f);

            float y = -24f;
            MakeLabel(card.transform, "SISTEMA AR·V1.0", new Vector2(0, y), 9,
                C_CYAN, FontStyles.Bold, TextAlignmentOptions.Center, new Vector2(pw, 14), 22);
            y -= 26f;
            var missionTitleLbl = MakeLabel(card.transform, "SELECCIÓN DE MISIÓN", new Vector2(0, y), 19,
                C_TEXT, FontStyles.Bold, TextAlignmentOptions.Center, new Vector2(pw, 32));
            var orbitron1 = MapCreatorUI.GetOrbitronFontAsset();
            if (orbitron1 != null) missionTitleLbl.font = orbitron1;
            y -= 60f;

            var actionRow = MakeContainer(card.transform, new Vector2(0, y), new Vector2(pw, 52));
            y -= 70f;

            var btnBack = MakeFillButton(actionRow.transform, "<- Volver",
                new Vector2(-208f, 0), new Vector2(124f, 48f), C_BORDER, C_TEXT);
            btnBack.onClick.AddListener(GoBack);

            var btnNew = MakeFillButton(actionRow.transform, "+ Nuevo",
                new Vector2(-58f, 0), new Vector2(150f, 48f),
                new Color(0.06f, 0.15f, 0.32f, 1f), C_CYAN);
            btnNew.onClick.AddListener(OpenMapCreator);

            btnPlay = MakeFillButton(actionRow.transform, "Iniciar",
                new Vector2(156f, 0), new Vector2(244f, 48f),
                C_ACCENT, new Color(0.03f, 0.05f, 0.09f, 1f));
            btnPlay.onClick.AddListener(GoPlay);

            BuildScrollView(card.transform, y, pw, 490f);

            LoadAndRender();
            RefreshButtons();

            var fpvGO = new GameObject("FPVTargetingOverlay");
            fpvGO.transform.SetParent(canvasGO.transform, false);
            var fpv = fpvGO.AddComponent<FPVTargetingOverlay>();
            fpv.Init(cardRt);
        }

        void BuildFPVBackgroundHUD(Transform canvasT)
        {
            BuildHUDScale(canvasT, -760f, 0f, true);
            BuildHUDScale(canvasT,  760f, 0f, false);
        }

        void MakeHUDBar(Transform parent, Vector2 pos, Vector2 size, Color col, float alpha)
        {
            var go  = new GameObject("HUD_Bar");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(col.r, col.g, col.b, alpha); img.raycastTarget = false;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
        }

        void BuildHUDScale(Transform parent, float xPos, float yCenter, bool isLeft)
        {
            float yStep = 56f, unitsStep = 5f;
            float railOffset = isLeft ? 52f : -52f;
            float railX = xPos + railOffset;
            float majorTickW = 30f, minorTickW = 16f;
            float tickDir = isLeft ? 1f : -1f;
            float railThk = 2.5f;
            int numMarks = 11;
            float clipH = yStep * (numMarks + 1);
            TextAlignmentOptions taNum = isLeft ? TextAlignmentOptions.Right : TextAlignmentOptions.Left;
            float labelNumX = isLeft ? xPos - 4f : xPos + 4f;

            // Rail: 0.18f → 0.09f
            MakeHUDBar(parent, new Vector2(railX, yCenter), new Vector2(railThk, clipH), C_CYAN, 0.06f);

            var tickGO = new GameObject("HUD_TickRoot_" + (isLeft ? "L" : "R"));
            tickGO.transform.SetParent(parent, false);
            var tickRT = tickGO.AddComponent<RectTransform>();
            tickRT.anchorMin = tickRT.anchorMax = new Vector2(0.5f, 0.5f);
            tickRT.pivot = new Vector2(0.5f, 0.5f);
            tickRT.anchoredPosition = new Vector2(0f, yCenter);
            tickRT.sizeDelta = Vector2.zero;
            if (isLeft) scaleLeftTicks = tickRT;
            else        scaleRightTicks = tickRT;

            int half = numMarks / 2;
            for (int i = -half - 2; i <= half + 2; i++)
            {
                int val = i * (int)unitsStep;
                float yy = i * yStep;
                bool isMaj = val % 20 == 0;
                float tW = isMaj ? majorTickW : minorTickW;
                // Major: 0.32f → 0.16f / Minor: 0.15f → 0.08f
                float tAlpha = 0.06f;
                float tThk = isMaj ? 2.5f : 2f;
                MakeHUDBar(tickRT, new Vector2(railX + tickDir * tW * 0.5f, yy), new Vector2(tW, tThk), C_CYAN, tAlpha);
                if (isMaj)
                {
                    // Labels: 0.35f/0.22f → 0.18f/0.11f
                    var lblTick = MakeHUDScaleLabelRef(tickRT, val.ToString(), new Vector2(labelNumX, yy), taNum,
                        val == 0 ? 16f : 13f, val == 0 ? 0.10f : 0.07f);
                    lblTick.gameObject.name = "ScaleTick_" + (isLeft ? "L" : "R") + "_" + val;
                }
            }

            // Puntero central: 0.35f → 0.18f
            MakeHUDBar(parent, new Vector2(railX + tickDir * (majorTickW + 4f), yCenter), new Vector2(majorTickW + 8f, 3f), C_CYAN, 0.06f);
            float lblX = isLeft ? railX + 120f : railX - 120f;
            string initTxt = isLeft ? "ALT\n--.-" : "VEL\n0.0";
            var lbl = MakeTelLabel(parent, new Vector2(lblX, yCenter), TextAlignmentOptions.Center, initTxt, new Vector2(140f, 48f), 15f);
            if (isLeft) altText = lbl;
            else        velText = lbl;
        }

        TextMeshProUGUI MakeHUDScaleLabelRef(Transform parent, string text, Vector2 pos,
            TextAlignmentOptions align, float size, float alpha)
        {
            var go  = new GameObject("HUD_ScaleLbl");
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = size;
            tmp.color = new Color(C_CYAN.r, C_CYAN.g, C_CYAN.b, alpha);
            tmp.fontStyle = FontStyles.Bold; tmp.alignment = align; tmp.raycastTarget = false;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(90f, 26f);
            return tmp;
        }

        TextMeshProUGUI MakeTelLabel(Transform parent, Vector2 pos,
            TextAlignmentOptions align, string text, Vector2 size, float fontSize = 10f)
        {
            var go  = new GameObject("HUD_Tel");
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = fontSize;
            tmp.color = new Color(C_CYAN.r, C_CYAN.g, C_CYAN.b, 0.11f);
            tmp.fontStyle = FontStyles.Bold; tmp.alignment = align;
            tmp.characterSpacing = 12f; tmp.raycastTarget = false;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            return tmp;
        }

        void UpdateScaleLabels(RectTransform tickRoot, string side, float centerVal, int step)
        {
            int half = 7;
            for (int i = -half; i <= half; i++)
            {
                int tickVal = i * step;
                int realVal = Mathf.RoundToInt(centerVal) + tickVal;
                string name = "ScaleTick_" + side + "_" + tickVal;
                var child = tickRoot.Find(name);
                if (child != null)
                {
                    var tmp = child.GetComponent<TextMeshProUGUI>();
                    if (tmp != null) tmp.text = realVal.ToString();
                }
            }
        }

        void BuildCardGlowBorder(Transform canvasT, RectTransform cardRef)
        {
            float w = cardRef.sizeDelta.x;
            float h = cardRef.sizeDelta.y;
            float t = 2.5f;
            Color gc = new Color(C_CYAN.r, C_CYAN.g, C_CYAN.b, 0.28f);
            string[]  names     = { "GlowTop", "GlowBottom", "GlowLeft", "GlowRight" };
            Vector2[] sizes     = { new Vector2(w, t), new Vector2(w, t), new Vector2(t, h), new Vector2(t, h) };
            Vector2[] positions = { new Vector2(0, h * 0.5f), new Vector2(0, -h * 0.5f),
                                    new Vector2(-w * 0.5f, 0), new Vector2(w * 0.5f, 0) };
            for (int i = 0; i < 4; i++)
            {
                var go  = new GameObject(names[i]);
                go.transform.SetParent(canvasT, false);
                var img = go.AddComponent<Image>();
                img.color = gc; img.raycastTarget = false;
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = positions[i]; rt.sizeDelta = sizes[i];
            }
        }

        void OpenMapCreator()
        {
            if (_canvas == null) return;
            MapCreatorUI.Show(_canvas.sortingOrder + 100,
                (objects, name, cLat, cLon, geofenceEnabled, lineGroups) => OnMapCreated(objects, name, cLat, cLon, geofenceEnabled, lineGroups), null);
        }

        void OnMapCreated(List<MapPlacedObject> objects, string name, double centerLat, double centerLon,
            bool geofenceEnabled, List<LineGroup> lineGroups)
        {
            var data = new ScenarioData(name,
                $"{objects.Count} objeto(s) en mapa",
                ScenarioType.Custom, ScenarioDifficulty.Media, "icon_custom");
            data.sceneName        = "MapScene";
            data.mapObjectsJson   = JsonUtility.ToJson(new MapObjectList { objects = objects, lineGroups = lineGroups });
            data.mapCenterLat     = (float)centerLat;
            data.mapCenterLon     = (float)centerLon;
            data.geofenceDisabled = !geofenceEnabled;
            if (objects.Count > 0) { data.refLat = objects[0].lat; data.refLon = objects[0].lon; }

#if UNITY_EDITOR
            // EditorSceneManager APIs are not allowed during Play Mode.
            // ScenarioSceneCreator.OnPlayModeChanged will process pending scenarios on exit.
            if (!Application.isPlaying && EditorBuildScene != null)
            {
                string builtScene = EditorBuildScene(data);
                if (builtScene != null)
                {
                    data.sceneName = builtScene;
                    UnityEditor.AssetDatabase.Refresh();
                }
            }
#endif

            ScenarioRepository.Add(data, scenarios);
            RebuildRows();
        }

        void OpenMapCreatorForEdit(ScenarioData s)
        {
            if (_canvas == null || s.isBuiltin) return;

            var mapList = string.IsNullOrEmpty(s.mapObjectsJson)
                ? null : JsonUtility.FromJson<MapObjectList>(s.mapObjectsJson);
            var objects    = mapList?.objects    ?? new List<MapPlacedObject>();
            var lineGroups = mapList?.lineGroups ?? new List<LineGroup>();

            ScenarioData sCapture = s;
            MapCreatorUI.Show(_canvas.sortingOrder + 100,
                (newObjects, name, cLat, cLon, geofenceEnabled, newLineGroups) => OnMapEdited(sCapture, newObjects, name, cLat, cLon, geofenceEnabled, newLineGroups),
                null,
                objects, s.name, (double)s.mapCenterLat, (double)s.mapCenterLon, !s.geofenceDisabled, lineGroups);
        }

        void OnMapEdited(ScenarioData original, List<MapPlacedObject> objects, string name,
            double centerLat, double centerLon, bool geofenceEnabled, List<LineGroup> lineGroups)
        {
            original.name           = name;
            original.description    = $"{objects.Count} objeto(s) en mapa";
            original.mapObjectsJson = JsonUtility.ToJson(new MapObjectList { objects = objects, lineGroups = lineGroups });
            original.mapCenterLat   = (float)centerLat;
            original.mapCenterLon   = (float)centerLon;
            original.geofenceDisabled = !geofenceEnabled;
            if (objects.Count > 0) { original.refLat = objects[0].lat; original.refLon = objects[0].lon; }

#if UNITY_EDITOR
            // Igual que al crear (OnMapCreated): hornear la escena con AssetDatabase/
            // EditorBuildSettings solo es seguro fuera de Play Mode. Si se edita
            // mientras se está jugando, se marca sceneName = "MapScene" para que
            // ScenarioSceneCreator.ProcessPendingScenarios la regenere sola al salir
            // de Play Mode (mismo mecanismo que usan los escenarios recién creados
            // antes de hornearse por primera vez).
            if (!Application.isPlaying && EditorBuildScene != null)
            {
                EditorDeleteScene?.Invoke(original);
                string builtScene = EditorBuildScene(original);
                if (builtScene != null)
                {
                    original.sceneName = builtScene;
                    UnityEditor.AssetDatabase.Refresh();
                }
            }
            else if (Application.isPlaying)
            {
                original.sceneName = "MapScene";
            }
#endif

            ScenarioRepository.Save(scenarios);
            if (selected != null && selected.id == original.id) selected = original;
            RebuildRows();
        }

        void GoBack() { ShaderTimekeeper.ScannerEnabled = false; SceneManager.LoadScene(menuSceneName); }

        void GoPlay()
        {
            if (selected == null) return;
            SelectedScenario.Current = selected;
            SceneManager.LoadScene("Configuración");
        }

        void BuildScrollView(Transform parent, float yPos, float width, float height)
        {
            var svGO = new GameObject("ScrollView");
            svGO.transform.SetParent(parent, false);
            svGO.AddComponent<Image>().color = new Color(0, 0, 0, 0);
            var svRt = svGO.GetComponent<RectTransform>();
            svRt.anchorMin = svRt.anchorMax = new Vector2(0.5f, 1f);
            svRt.pivot = new Vector2(0.5f, 1f);
            svRt.anchoredPosition = new Vector2(0, yPos);
            svRt.sizeDelta = new Vector2(width, height);

            var vpGO = new GameObject("Viewport");
            vpGO.transform.SetParent(svGO.transform, false);
            vpGO.AddComponent<Image>().color = new Color(0, 0, 0, 0);
            vpGO.AddComponent<RectMask2D>();
            var vpRt = vpGO.GetComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero; vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = Vector2.zero; vpRt.offsetMax = new Vector2(-6f, 0f);

            var contentGO = new GameObject("Content");
            contentGO.transform.SetParent(vpGO.transform, false);
            var vlg = contentGO.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 4; vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            var csf = contentGO.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            listContent = contentGO.transform;
            var contentRt = contentGO.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0, 1); contentRt.anchorMax = new Vector2(1, 1);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = Vector2.zero; contentRt.sizeDelta = Vector2.zero;

            var sbGO = new GameObject("Scrollbar");
            sbGO.transform.SetParent(svGO.transform, false);
            var sbImg = sbGO.AddComponent<Image>();
            sbImg.color = new Color(0.08f, 0.12f, 0.20f, 0.05f);
            var sbRt = sbGO.GetComponent<RectTransform>();
            sbRt.anchorMin = new Vector2(1f, 0f); sbRt.anchorMax = new Vector2(1f, 1f);
            sbRt.pivot = new Vector2(1f, 0.5f);
            sbRt.anchoredPosition = Vector2.zero;
            sbRt.sizeDelta = new Vector2(4f, 0f);

            var slideGO = new GameObject("SlidingArea");
            slideGO.transform.SetParent(sbGO.transform, false);
            var slideRt = slideGO.AddComponent<RectTransform>();
            slideRt.anchorMin = Vector2.zero; slideRt.anchorMax = Vector2.one;
            slideRt.offsetMin = new Vector2(1f, 2f); slideRt.offsetMax = new Vector2(-1f, -2f);

            var handleGO = new GameObject("Handle");
            handleGO.transform.SetParent(slideGO.transform, false);
            var handleImg = handleGO.AddComponent<Image>();
            handleImg.color = new Color(C_CYAN.r, C_CYAN.g, C_CYAN.b, 0.035f);
            var handleRt = handleGO.GetComponent<RectTransform>();
            handleRt.anchorMin = Vector2.zero; handleRt.anchorMax = Vector2.one;
            handleRt.offsetMin = handleRt.offsetMax = Vector2.zero;

            var sb = sbGO.AddComponent<Scrollbar>();
            sb.handleRect = handleRt;
            sb.direction = Scrollbar.Direction.BottomToTop;

            var sr = svGO.AddComponent<ScrollRect>();
            sr.viewport = vpRt; sr.content = contentRt;
            sr.horizontal = false; sr.vertical = true;
            sr.scrollSensitivity = 20f;
            sr.verticalScrollbar = sb;
            sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            sr.verticalScrollbarSpacing = 2f;
            listScrollRect = sr;
        }

        void LoadAndRender()
        {
            scenarios = ScenarioRepository.LoadAll();
            RebuildRows();
        }

        void RebuildRows()
        {
            foreach (var go in rowObjects) if (go) Destroy(go);
            rowObjects.Clear();
            for (int i = 0; i < scenarios.Count; i++)
            {
                var row = BuildRow(scenarios[i]);
                rowObjects.Add(row);
                if (selected != null && scenarios[i].id == selected.id)
                    ApplyRowSelected(row, true);
            }
        }

        GameObject BuildRow(ScenarioData s)
        {
            var go = new GameObject("Row_" + s.name);
            go.transform.SetParent(listContent, false);
            var img = go.AddComponent<Image>();
            img.color = C_DISAB;
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 58f; le.flexibleWidth = 1f;

            var selBar = MakeImage(go.transform, "SelBar", C_CYAN);
            var sbRt = selBar.GetComponent<RectTransform>();
            sbRt.anchorMin = new Vector2(0, 0); sbRt.anchorMax = new Vector2(0, 1);
            sbRt.pivot = new Vector2(0, 0.5f);
            sbRt.offsetMin = sbRt.offsetMax = Vector2.zero;
            sbRt.sizeDelta = new Vector2(4, 0);
            selBar.gameObject.SetActive(false);

            var nameGO = new GameObject("Name");
            nameGO.transform.SetParent(go.transform, false);
            var nameTmp = nameGO.AddComponent<TextMeshProUGUI>();
            nameTmp.text = s.name; nameTmp.fontSize = 16;
            nameTmp.fontStyle = FontStyles.Bold; nameTmp.color = C_TEXT;
            var nameRt = nameGO.GetComponent<RectTransform>();
            nameRt.anchorMin = new Vector2(0, 0.5f); nameRt.anchorMax = new Vector2(0.75f, 1f);
            nameRt.offsetMin = new Vector2(16, 0); nameRt.offsetMax = Vector2.zero;

            // Estado de borrado: siempre "Eliminable" / "No eliminable" según
            // si es una escena del proyecto (isBuiltin) o un escenario creado
            // por el usuario, en vez de mostrar la descripción guardada.
            var descGO = new GameObject("Desc");
            descGO.transform.SetParent(go.transform, false);
            var descTmp = descGO.AddComponent<TextMeshProUGUI>();
            descTmp.text = s.isBuiltin ? "No eliminable" : "Eliminable";
            descTmp.fontSize = 10f; descTmp.color = C_SUB;
            var descRt = descGO.GetComponent<RectTransform>();
            descRt.anchorMin = new Vector2(0, 0); descRt.anchorMax = new Vector2(0.75f, 0.5f);
            descRt.offsetMin = new Vector2(16, 2); descRt.offsetMax = Vector2.zero;

            if (!s.isBuiltin)
            {
                ScenarioData sCapture = s;

                var delGO = new GameObject("BtnDelete");
                delGO.transform.SetParent(go.transform, false);
                var delImg = delGO.AddComponent<Image>();
                delImg.color = new Color(0.45f, 0.07f, 0.07f, 0.9f);
                var delRt = delGO.GetComponent<RectTransform>();
                delRt.anchorMin = new Vector2(1f, 0.18f);
                delRt.anchorMax = new Vector2(1f, 0.82f);
                delRt.pivot = new Vector2(1f, 0.5f);
                delRt.offsetMin = new Vector2(-86f, 0f);
                delRt.offsetMax = new Vector2(-6f, 0f);

                var delTxtGO = new GameObject("DelTxt");
                delTxtGO.transform.SetParent(delGO.transform, false);
                var delTmp = delTxtGO.AddComponent<TextMeshProUGUI>();
                delTmp.text = "Eliminar";
                delTmp.fontSize = 10f;
                delTmp.fontStyle = FontStyles.Bold;
                delTmp.color = new Color(1f, 0.72f, 0.72f, 1f);
                delTmp.alignment = TextAlignmentOptions.Center;
                var delTxtRt = delTxtGO.GetComponent<RectTransform>();
                delTxtRt.anchorMin = Vector2.zero;
                delTxtRt.anchorMax = Vector2.one;
                // Deja hueco a la izquierda para el icono de papelera (no se centra
                // el texto solo, sino junto al icono).
                delTxtRt.offsetMin = new Vector2(11f, 0f);
                delTxtRt.offsetMax = Vector2.zero;

                // Icono de papelera dibujado con formas simples (no un emoji: la
                // fuente TMP de este proyecto no soporta glifos de emoji/dingbats,
                // salen como □; mismo problema ya resuelto así en
                // BuildLockIcon/BuildLocationPinIcon).
                BuildTrashIcon(delGO.transform, new Vector2(-30f, 0f), new Color(1f, 0.72f, 0.72f, 1f));

                var delBtn = delGO.AddComponent<Button>();
                var dcb = delBtn.colors;
                dcb.normalColor      = new Color(0.45f, 0.07f, 0.07f, 0.9f);
                dcb.highlightedColor = new Color(0.65f, 0.12f, 0.12f, 1f);
                dcb.pressedColor     = new Color(0.28f, 0.04f, 0.04f, 1f);
                delBtn.colors = dcb;
                delBtn.onClick.AddListener(() => { CloseContextMenu(); ShowConfirmDelete(sCapture); });
                delGO.SetActive(false);

                // Botón Editar: mismo tamaño que Eliminar, pegado a su izquierda.
                var editGO = new GameObject("BtnEdit");
                editGO.transform.SetParent(go.transform, false);
                var editImg = editGO.AddComponent<Image>();
                editImg.color = new Color(0.06f, 0.18f, 0.35f, 0.9f);
                var editRt = editGO.GetComponent<RectTransform>();
                editRt.anchorMin = new Vector2(1f, 0.18f);
                editRt.anchorMax = new Vector2(1f, 0.82f);
                editRt.pivot = new Vector2(1f, 0.5f);
                editRt.offsetMin = new Vector2(-172f, 0f);
                editRt.offsetMax = new Vector2(-92f, 0f);

                var editTextColor = new Color(0.75f, 0.88f, 1f, 1f);

                // Contenedor icono+texto: se centra como grupo dentro del botón (en
                // vez de posiciones fijas a mano), así el pincel y la palabra
                // "Editar" quedan centrados juntos sea cual sea el ancho real del
                // texto.
                var editContentGO = new GameObject("EditContent");
                editContentGO.transform.SetParent(editGO.transform, false);
                var editContentRt = editContentGO.AddComponent<RectTransform>();
                editContentRt.anchorMin = editContentRt.anchorMax = editContentRt.pivot = new Vector2(0.5f, 0.5f);
                editContentRt.anchoredPosition = new Vector2(-4f, 0f); // un poco a la izquierda del centro
                var editHlg = editContentGO.AddComponent<HorizontalLayoutGroup>();
                editHlg.childAlignment = TextAnchor.MiddleCenter;
                editHlg.spacing = 4f;
                editHlg.childControlWidth = true; editHlg.childControlHeight = true;
                editHlg.childForceExpandWidth = false; editHlg.childForceExpandHeight = false;
                editContentGO.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

                // Icono de pincel: emoji real (🖌️, Twemoji) descargado en tiempo de
                // ejecución, igual que las teselas del mapa (MapCreatorUI.FetchTile) —
                // este proyecto no importa imágenes como asset, todo se genera o
                // descarga en código. Invisible hasta que termine de cargar. Se
                // desatura (ver Desaturate) y se tiñe del mismo color que la palabra
                // "Editar".
                var editIconGO = new GameObject("EditIcon");
                editIconGO.transform.SetParent(editContentGO.transform, false);
                var editIconImg = editIconGO.AddComponent<Image>();
                editIconImg.color = new Color(editTextColor.r, editTextColor.g, editTextColor.b, 0f);
                editIconImg.raycastTarget = false;
                var editIconLe = editIconGO.AddComponent<LayoutElement>();
                editIconLe.preferredWidth = 14f; editIconLe.preferredHeight = 14f;
                StartCoroutine(LoadPaintbrushIcon(editIconImg, editTextColor));

                var editTxtGO = new GameObject("EditTxt");
                editTxtGO.transform.SetParent(editContentGO.transform, false);
                var editTmp = editTxtGO.AddComponent<TextMeshProUGUI>();
                editTmp.text = "Editar";
                editTmp.fontSize = 10f;
                editTmp.fontStyle = FontStyles.Bold;
                editTmp.color = editTextColor;
                editTmp.alignment = TextAlignmentOptions.Center;
                editTmp.enableAutoSizing = false;
                var editTxtLe = editTxtGO.AddComponent<LayoutElement>();
                editTxtLe.preferredHeight = 22f;

                var editBtn = editGO.AddComponent<Button>();
                var ecb = editBtn.colors;
                ecb.normalColor      = new Color(0.06f, 0.18f, 0.35f, 0.9f);
                ecb.highlightedColor = new Color(0.10f, 0.26f, 0.48f, 1f);
                ecb.pressedColor     = new Color(0.03f, 0.10f, 0.20f, 1f);
                editBtn.colors = ecb;
                editBtn.onClick.AddListener(() => { CloseContextMenu(); OpenMapCreatorForEdit(sCapture); });
                editGO.SetActive(false);
            }

            var btn = go.AddComponent<Button>();
            var cb = btn.colors;
            cb.normalColor      = C_DISAB;
            cb.highlightedColor = new Color(0.12f, 0.18f, 0.28f, 1f);
            cb.pressedColor     = C_ROW_SEL;
            cb.fadeDuration     = 0.25f;
            btn.colors = cb;

            ScenarioData captured = s;
            btn.onClick.AddListener(() => { CloseContextMenu(); OnRowClicked(captured, go); });

            var et = go.AddComponent<EventTrigger>();

            var clickEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            clickEntry.callback.AddListener(data =>
            {
                var pd = (PointerEventData)data;
                if (pd.button == PointerEventData.InputButton.Right)
                    ShowDeleteMenu(captured, pd.position);
            });
            et.triggers.Add(clickEntry);

            var scrollEntry = new EventTrigger.Entry { eventID = EventTriggerType.Scroll };
            scrollEntry.callback.AddListener(data =>
            {
                if (listScrollRect != null)
                    listScrollRect.OnScroll((PointerEventData)data);
            });
            et.triggers.Add(scrollEntry);

            return go;
        }

        void ApplyRowSelected(GameObject row, bool on)
        {
            var img   = row.GetComponent<Image>();
            var nameT = row.transform.Find("Name");
            var tmp   = nameT ? nameT.GetComponent<TextMeshProUGUI>() : null;

            StartCoroutine(LerpRowColor(img, tmp, on ? C_ROW_SEL : C_DISAB, on ? C_CYAN : C_TEXT));

            var selBar = row.transform.Find("SelBar");
            if (selBar) selBar.gameObject.SetActive(on);

            var delBtn = row.transform.Find("BtnDelete");
            if (delBtn) delBtn.gameObject.SetActive(on);

            var editBtn = row.transform.Find("BtnEdit");
            if (editBtn) editBtn.gameObject.SetActive(on);
        }

        IEnumerator LerpRowColor(Image img, TextMeshProUGUI tmp, Color targetImg, Color targetTmp)
        {
            const float duration = 0.28f;
            Color startImg = img  ? img.color  : targetImg;
            Color startTmp = tmp  ? tmp.color  : targetTmp;
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                float a = t / duration;
                if (img) img.color = Color.Lerp(startImg, targetImg, a);
                if (tmp) tmp.color = Color.Lerp(startTmp, targetTmp, a);
                yield return null;
            }
            if (img) img.color = targetImg;
            if (tmp) tmp.color = targetTmp;
        }

        void OnRowClicked(ScenarioData s, GameObject row)
        {
            selected = s;
            for (int i = 0; i < rowObjects.Count; i++)
                ApplyRowSelected(rowObjects[i], scenarios[i].id == s.id);
            RefreshButtons();
        }

        void RefreshButtons()
        {
            if (btnPlay) btnPlay.interactable = selected != null;
        }

        void DeselectAll()
        {
            if (selected == null) return;
            selected = null;
            for (int i = 0; i < rowObjects.Count; i++)
                ApplyRowSelected(rowObjects[i], false);
            RefreshButtons();
        }

        void AddDarkGradient(Transform parent, float maxAlpha = 0.88f)
        {
            const int bands = 10;
            for (int i = 0; i < bands; i++)
            {
                float yMin  = i / (float)bands;
                float yMax  = (i + 1) / (float)bands;
                float alpha = Mathf.Lerp(maxAlpha, 0.00f, i / (float)(bands - 1));
                var bandGO  = new GameObject("GradDark" + i);
                bandGO.transform.SetParent(parent, false);
                bandGO.transform.SetAsFirstSibling();
                var bandImg = bandGO.AddComponent<Image>();
                bandImg.color = new Color(0f, 0f, 0f, alpha);
                bandImg.raycastTarget = false;
                var bRt = bandGO.GetComponent<RectTransform>();
                bRt.anchorMin = new Vector2(0f, yMin);
                bRt.anchorMax = new Vector2(1f, yMax);
                bRt.offsetMin = bRt.offsetMax = Vector2.zero;
            }
        }

        Image MakeImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            return img;
        }

        // Rectángulo simple centrado en "pos" dentro de "parent", opcionalmente
        // rotado: pieza básica para montar iconos vectoriales (papelera, pincel)
        // sin depender de glifos de emoji que la fuente TMP de este proyecto no
        // soporta (salen como □; mismo enfoque que BuildLockIcon/BuildLocationPinIcon).
        Image MakeIconBar(Transform parent, Vector2 pos, Vector2 size, Color color, float rotationZ = 0f)
        {
            var img = MakeImage(parent, "IconBar", color);
            img.raycastTarget = false;
            var rt = img.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            if (rotationZ != 0f) rt.localRotation = Quaternion.Euler(0f, 0f, rotationZ);
            return img;
        }

        // Icono de papelera: asa + tapa + cuerpo, silueta reconocible sin depender
        // de ningún glifo.
        RectTransform BuildTrashIcon(Transform parent, Vector2 pos, Color color)
        {
            var rootGO = new GameObject("TrashIcon");
            rootGO.transform.SetParent(parent, false);
            var rootRt = rootGO.AddComponent<RectTransform>();
            rootRt.anchorMin = rootRt.anchorMax = rootRt.pivot = new Vector2(0.5f, 0.5f);
            rootRt.anchoredPosition = pos;
            rootRt.sizeDelta = Vector2.zero;

            MakeIconBar(rootRt, new Vector2(0f, 4.6f), new Vector2(3.2f, 1.6f), color);  // asa
            MakeIconBar(rootRt, new Vector2(0f, 3.2f), new Vector2(9f, 1.4f), color);    // tapa
            MakeIconBar(rootRt, new Vector2(0f, -1.4f), new Vector2(7f, 8f), color);     // cuerpo
            return rootRt;
        }

        static Sprite _paintbrushEmojiSprite;
        static bool   _paintbrushEmojiLoadFailed;

        // URL fija (versión anclada, no "@latest") del emoji 🖌️ PAINTBRUSH de
        // Twemoji (github.com/twitter/twemoji, licencia CC-BY 4.0), servido por
        // jsDelivr. Se descarga en tiempo de ejecución igual que las teselas del
        // mapa (MapCreatorUI.FetchTile): este proyecto no importa imágenes como
        // asset, todo lo visual se genera o descarga en código.
        const string PAINTBRUSH_EMOJI_URL =
            "https://cdn.jsdelivr.net/gh/twitter/twemoji@14.0.2/assets/72x72/1f58c.png";

        IEnumerator LoadPaintbrushIcon(Image target, Color tint)
        {
            if (_paintbrushEmojiSprite != null)
            {
                if (target) { target.sprite = _paintbrushEmojiSprite; target.color = tint; }
                yield break;
            }
            if (_paintbrushEmojiLoadFailed) yield break;

            var req = UnityWebRequestTexture.GetTexture(PAINTBRUSH_EMOJI_URL);
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success)
            {
                var tex = DownloadHandlerTexture.GetContent(req);
                // El emoji es a color; se pasa a blancos/grises/negros para poder
                // teñirlo del mismo color que el texto del botón (target.color).
                Desaturate(tex);
                _paintbrushEmojiSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f), 100f);
                if (target) { target.sprite = _paintbrushEmojiSprite; target.color = tint; }
            }
            else
            {
                _paintbrushEmojiLoadFailed = true;
                Debug.LogWarning("[Menú] No se pudo descargar el icono del pincel: " + req.error);
            }
            req.Dispose();
        }

        // Convierte una textura a escala de grises in-place (luminancia perceptual,
        // Rec. 601), conservando el alpha para que se mantenga el contorno/sombreado
        // del emoji, solo sin color.
        static void Desaturate(Texture2D tex)
        {
            var pixels = tex.GetPixels32();
            for (int i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                byte gray = (byte)(0.299f * p.r + 0.587f * p.g + 0.114f * p.b);
                pixels[i] = new Color32(gray, gray, gray, p.a);
            }
            tex.SetPixels32(pixels);
            tex.Apply();
        }

        void Stretch(Component c)
        {
            var rt = c.GetComponent<RectTransform>() ?? c.gameObject.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        void AddOutline(Component c, Color color)
        {
            var o = c.gameObject.AddComponent<Outline>();
            o.effectColor = color; o.effectDistance = new Vector2(1, -1);
        }

        TextMeshProUGUI MakeLabel(Transform parent, string text, Vector2 pos, float size,
            Color color, FontStyles style, TextAlignmentOptions align,
            Vector2 sizeDelta, float letterSpacing = 0)
        {
            var go = new GameObject("Lbl");
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = size; tmp.color = color;
            tmp.fontStyle = style; tmp.alignment = align;
            tmp.characterSpacing = letterSpacing;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = pos; rt.sizeDelta = sizeDelta;
            return tmp;
        }


        GameObject MakeContainer(Transform parent, Vector2 pos, Vector2 size)
        {
            var go = new GameObject("Container");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            return go;
        }

        void ShowDeleteMenu(ScenarioData s, Vector2 screenPos)
        {
            CloseContextMenu();
            if (_canvas == null) return;

            var root = new GameObject("CtxRoot");
            root.transform.SetParent(_canvas.transform, false);

            // Fondo transparente que cierra el menú al hacer click fuera
            var bdGO = new GameObject("Backdrop");
            bdGO.transform.SetParent(root.transform, false);
            var bdImg = bdGO.AddComponent<Image>(); bdImg.color = Color.clear;
            var bdRt = bdGO.GetComponent<RectTransform>();
            bdRt.anchorMin = Vector2.zero; bdRt.anchorMax = Vector2.one;
            bdRt.offsetMin = bdRt.offsetMax = Vector2.zero;
            bdGO.AddComponent<Button>().onClick.AddListener(CloseContextMenu);

            // Caja del menú contextual — con "Editar" (si la misión no es
            // predefinida) arriba de "Eliminar", igual que ya pasa con los
            // botones en línea de la fila (BtnEdit/BtnDelete más arriba en
            // este mismo fichero).
            bool showEdit = !s.isBuiltin;
            float menuHeight = showEdit ? 80f : 42f;

            var menuGO = new GameObject("CtxMenu");
            menuGO.transform.SetParent(root.transform, false);
            menuGO.AddComponent<Image>().color = new Color(0.06f, 0.10f, 0.18f, 0.97f);
            var menuRt = menuGO.GetComponent<RectTransform>();
            menuRt.anchorMin = menuRt.anchorMax = new Vector2(0f, 1f);
            menuRt.pivot = new Vector2(0f, 1f);
            menuRt.sizeDelta = new Vector2(140f, menuHeight);

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvas.GetComponent<RectTransform>(), screenPos, _canvas.worldCamera, out Vector2 local);
            menuRt.anchoredPosition = local;

            // Botón Editar (arriba) — solo para misiones no predefinidas,
            // igual que su equivalente en línea de la fila.
            if (showEdit)
            {
                ScenarioData sCapture = s;

                var editGO = new GameObject("BtnEditar");
                editGO.transform.SetParent(menuGO.transform, false);
                editGO.AddComponent<Image>().color = new Color(0.06f, 0.18f, 0.35f, 1f);
                var editRt = editGO.GetComponent<RectTransform>();
                editRt.anchorMin = new Vector2(0f, 0.5f); editRt.anchorMax = new Vector2(1f, 1f);
                editRt.offsetMin = new Vector2(4f, 2f); editRt.offsetMax = new Vector2(-4f, -4f);

                var editTxtGO = new GameObject("Txt");
                editTxtGO.transform.SetParent(editGO.transform, false);
                var editTmp = editTxtGO.AddComponent<TextMeshProUGUI>();
                editTmp.text = "Editar";
                editTmp.fontSize = 12; editTmp.fontStyle = FontStyles.Bold;
                editTmp.color = Color.white;
                editTmp.alignment = TextAlignmentOptions.Center;
                var editTxtRt = editTxtGO.GetComponent<RectTransform>();
                editTxtRt.anchorMin = Vector2.zero; editTxtRt.anchorMax = Vector2.one;
                editTxtRt.offsetMin = editTxtRt.offsetMax = Vector2.zero;

                var editBtn = editGO.AddComponent<Button>();
                var ecb = editBtn.colors;
                ecb.normalColor      = new Color(0.06f, 0.18f, 0.35f, 1f);
                ecb.highlightedColor = new Color(0.10f, 0.26f, 0.48f, 1f);
                ecb.pressedColor     = new Color(0.03f, 0.10f, 0.20f, 1f);
                editBtn.colors = ecb;
                editBtn.onClick.AddListener(() => { CloseContextMenu(); OpenMapCreatorForEdit(sCapture); });
            }

            // Botón Eliminar (abajo)
            var btnGO = new GameObject("BtnEliminar");
            btnGO.transform.SetParent(menuGO.transform, false);
            btnGO.AddComponent<Image>().color = new Color(0.48f, 0.07f, 0.07f, 1f);
            var btnRt = btnGO.GetComponent<RectTransform>();
            if (showEdit)
            {
                btnRt.anchorMin = new Vector2(0f, 0f); btnRt.anchorMax = new Vector2(1f, 0.5f);
                btnRt.offsetMin = new Vector2(4f, 4f); btnRt.offsetMax = new Vector2(-4f, -2f);
            }
            else
            {
                btnRt.anchorMin = Vector2.zero; btnRt.anchorMax = Vector2.one;
                btnRt.offsetMin = new Vector2(4f, 4f); btnRt.offsetMax = new Vector2(-4f, -4f);
            }

            var txtGO = new GameObject("Txt");
            txtGO.transform.SetParent(btnGO.transform, false);
            var tmp = txtGO.AddComponent<TextMeshProUGUI>();
            tmp.text = s.isBuiltin ? "No eliminable" : "Eliminar";
            tmp.fontSize = 12; tmp.fontStyle = FontStyles.Bold;
            tmp.color = s.isBuiltin ? C_SUB : Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            var txtRt = txtGO.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero; txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = txtRt.offsetMax = Vector2.zero;

            var delBtn = btnGO.AddComponent<Button>();
            var cb = delBtn.colors;
            cb.normalColor      = new Color(0.48f, 0.07f, 0.07f, 1f);
            cb.highlightedColor = new Color(0.65f, 0.12f, 0.12f, 1f);
            cb.pressedColor     = new Color(0.30f, 0.04f, 0.04f, 1f);
            cb.disabledColor    = C_DISAB;
            delBtn.colors = cb;
            delBtn.interactable = !s.isBuiltin;
            delBtn.onClick.AddListener(() => { CloseContextMenu(); ShowConfirmDelete(s); });

            rowContextMenu = root;
        }

        void CloseContextMenu()
        {
            if (rowContextMenu) { Destroy(rowContextMenu); rowContextMenu = null; }
        }

        void ShowConfirmDelete(ScenarioData s)
        {
            if (_canvas == null) return;

            var root = new GameObject("ConfirmRoot");
            root.transform.SetParent(_canvas.transform, false);

            // Fondo semitransparente que bloquea la interacción
            var bdGO = new GameObject("Backdrop");
            bdGO.transform.SetParent(root.transform, false);
            var bdImg = bdGO.AddComponent<Image>();
            bdImg.color = new Color(0f, 0f, 0f, 0.55f);
            var bdRt = bdGO.GetComponent<RectTransform>();
            bdRt.anchorMin = Vector2.zero; bdRt.anchorMax = Vector2.one;
            bdRt.offsetMin = bdRt.offsetMax = Vector2.zero;
            bdGO.AddComponent<Button>(); // absorbe clics fuera del panel

            // Panel central
            var panelGO = new GameObject("ConfirmPanel");
            panelGO.transform.SetParent(root.transform, false);
            var panelImg = panelGO.AddComponent<Image>();
            panelImg.color = new Color(0.04f, 0.10f, 0.20f, 0.97f);
            AddOutline(panelImg, C_BORDER);
            var panelRt = panelGO.GetComponent<RectTransform>();
            panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.pivot = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(360f, 160f);
            panelRt.anchoredPosition = Vector2.zero;

            AddDarkGradient(panelGO.transform, maxAlpha: 0.62f);

            // Borde cyan superior
            var topBarGO = new GameObject("TopBar");
            topBarGO.transform.SetParent(panelGO.transform, false);
            var topBarImg = topBarGO.AddComponent<Image>();
            topBarImg.color = new Color(C_CYAN.r, C_CYAN.g, C_CYAN.b, 0.5f);
            var topBarRt = topBarGO.GetComponent<RectTransform>();
            topBarRt.anchorMin = new Vector2(0f, 1f); topBarRt.anchorMax = new Vector2(1f, 1f);
            topBarRt.pivot = new Vector2(0.5f, 1f);
            topBarRt.offsetMin = topBarRt.offsetMax = Vector2.zero;
            topBarRt.sizeDelta = new Vector2(0f, 3f);

            // Título
            var titleGO = new GameObject("Title");
            titleGO.transform.SetParent(panelGO.transform, false);
            var titleTmp = titleGO.AddComponent<TextMeshProUGUI>();
            titleTmp.text = "¿Eliminar Misión?";
            titleTmp.fontSize = 14f; titleTmp.fontStyle = FontStyles.Bold;
            titleTmp.color = C_TEXT; titleTmp.alignment = TextAlignmentOptions.Center;
            var titleRt = titleGO.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0f, 1f); titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.anchoredPosition = new Vector2(0f, -18f);
            titleRt.sizeDelta = new Vector2(-24f, 22f);

            // Nombre del escenario
            var nameGO = new GameObject("ScenName");
            nameGO.transform.SetParent(panelGO.transform, false);
            var nameTmp = nameGO.AddComponent<TextMeshProUGUI>();
            nameTmp.text = $"«{s.name}»";
            nameTmp.fontSize = 11f; nameTmp.color = C_CYAN;
            nameTmp.alignment = TextAlignmentOptions.Center;
            var nameRt = nameGO.GetComponent<RectTransform>();
            nameRt.anchorMin = new Vector2(0f, 1f); nameRt.anchorMax = new Vector2(1f, 1f);
            nameRt.pivot = new Vector2(0.5f, 1f);
            nameRt.anchoredPosition = new Vector2(0f, -46f);
            nameRt.sizeDelta = new Vector2(-24f, 18f);

            // Subtítulo
            var subGO = new GameObject("Sub");
            subGO.transform.SetParent(panelGO.transform, false);
            var subTmp = subGO.AddComponent<TextMeshProUGUI>();
            subTmp.text = "Esta acción no se puede deshacer.";
            subTmp.fontSize = 9f; subTmp.color = C_SUB;
            subTmp.alignment = TextAlignmentOptions.Center;
            var subRt = subGO.GetComponent<RectTransform>();
            subRt.anchorMin = new Vector2(0f, 1f); subRt.anchorMax = new Vector2(1f, 1f);
            subRt.pivot = new Vector2(0.5f, 1f);
            subRt.anchoredPosition = new Vector2(0f, -68f);
            subRt.sizeDelta = new Vector2(-24f, 16f);

            // Botón Cancelar
            var btnCancel = MakeFillButton(panelGO.transform, "Cancelar",
                new Vector2(-70f, 24f), new Vector2(130f, 38f), C_BORDER, C_TEXT);
            btnCancel.onClick.AddListener(() => Destroy(root));

            // Botón Confirmar
            var btnConfirm = MakeFillButton(panelGO.transform, "Sí, eliminar",
                new Vector2(80f, 24f), new Vector2(140f, 38f),
                new Color(0.55f, 0.07f, 0.07f, 1f), new Color(1f, 0.80f, 0.80f, 1f));
            btnConfirm.onClick.AddListener(() =>
            {
                Destroy(root);
                DeleteScenario(s);
            });

            // Anclar botones desde abajo
            foreach (var btn in new[] { btnCancel.GetComponent<RectTransform>(),
                                        btnConfirm.GetComponent<RectTransform>() })
            {
                btn.anchorMin = new Vector2(0.5f, 0f);
                btn.anchorMax = new Vector2(0.5f, 0f);
                btn.pivot     = new Vector2(0.5f, 0f);
            }
        }

        void DeleteScenario(ScenarioData s)
        {
            if (s.isBuiltin) return;
#if UNITY_EDITOR
            EditorDeleteScene?.Invoke(s);
#endif
            ScenarioRepository.Remove(s.id, scenarios);
            if (selected != null && selected.id == s.id) { selected = null; RefreshButtons(); }
            RebuildRows();
        }

        Button MakeFillButton(Transform parent, string label, Vector2 pos, Vector2 size,
            Color fillColor, Color textColor)
        {
            var go = new GameObject("Btn_" + label);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>(); img.color = fillColor;
            var txtGO = new GameObject("Txt");
            txtGO.transform.SetParent(go.transform, false);
            var t = txtGO.AddComponent<TextMeshProUGUI>();
            t.text = label; t.fontSize = 12; t.fontStyle = FontStyles.Bold;
            t.color = textColor; t.alignment = TextAlignmentOptions.Center;
            var tRt = txtGO.GetComponent<RectTransform>();
            tRt.anchorMin = Vector2.zero; tRt.anchorMax = Vector2.one;
            tRt.offsetMin = tRt.offsetMax = Vector2.zero;
            var btn = go.AddComponent<Button>();
            var cb = btn.colors;
            cb.normalColor      = fillColor;
            cb.highlightedColor = Color.Lerp(fillColor, Color.white, 0.2f);
            cb.pressedColor     = Color.Lerp(fillColor, Color.black, 0.2f);
            cb.disabledColor    = C_DISAB;
            btn.colors = cb;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            return btn;
        }
    }
}