// ShaderTimekeeper.cs
// ─────────────────────────────────────────────────────────────────────────────
// Fix definitivo: Update() se congela con timeScale=0. En su lugar usamos
// una corrutina con yield return null en un MonoBehaviour marcado con
// hideFlags = HideFlags.DontSave, que:
//   1. No se guarda en la escena ni sobrevive a una build (solo existe en runtime).
//   2. Sobrevive cambios de escena (DontDestroyOnLoad).
//   3. Su corrutina sigue ejecutando aunque timeScale = 0 porque
//      yield return null avanza por FRAMES, no por tiempo de juego.
//   4. Escribe Time.unscaledTime al shader, así el movimiento
//      nunca se detiene independientemente de timeScale.
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections;
using UnityEngine;

namespace ARDashboard
{
    // ── Runner interno ────────────────────────────────────────────────────────
    internal class ShaderTimekeeperRunner : MonoBehaviour
    {
        void Start()
        {
            StartCoroutine(TickLoop());
        }

        IEnumerator TickLoop()
        {
            while (true)
            {
                ShaderTimekeeper.Tick();
                yield return null;   // avanza frame a frame, inmune a timeScale=0
            }
        }
    }

    // ── Clase pública ─────────────────────────────────────────────────────────
    public static class ShaderTimekeeper
    {
        static readonly int ID_Deep      = Shader.PropertyToID("_ColorDeep");
        static readonly int ID_Mid       = Shader.PropertyToID("_ColorMid");
        static readonly int ID_Accent    = Shader.PropertyToID("_ColorAccent");
        static readonly int ID_GridSize  = Shader.PropertyToID("_GridSize");
        static readonly int ID_GridAlpha = Shader.PropertyToID("_GridAlpha");
        static readonly int ID_GridSpeed = Shader.PropertyToID("_GridSpeed");
        static readonly int ID_ScanAlpha = Shader.PropertyToID("_ScanAlpha");
        static readonly int ID_PulseSpd  = Shader.PropertyToID("_PulseSpeed");
        static readonly int ID_PulseCnt  = Shader.PropertyToID("_PulseCount");
        static readonly int ID_Vig       = Shader.PropertyToID("_VigStrength");
        static readonly int ID_TimeOff   = Shader.PropertyToID("_TimeOffset");

        static readonly Color ColorDeep   = new Color(0.040f, 0.055f, 0.080f);
        static readonly Color ColorMid    = new Color(0.000f, 0.120f, 0.220f);
        static readonly Color ColorAccent = new Color(0.000f, 0.560f, 0.780f);
        const float GridSize   = 40f;
        const float GridAlpha  = 0.06f;
        const float GridSpeed  = 0.4f;
        const float ScanAlpha  = 0.04f;
        const float PulseSpeed = 0.6f;
        const float PulseCount = 3f;
        const float VigStr     = 1.1f;

        static Material _material;
        static Material _fpvMaterial;
        static bool     _runnerCreated;

        // Escáner activo solo en Menú y Configuración
        static bool  _scannerEnabled = false;
        static float _scanStartTime  = 0f;

        public static bool ScannerEnabled
        {
            get => _scannerEnabled;
            set
            {
                if (value && !_scannerEnabled)
                    _scanStartTime = Time.unscaledTime; // arranca desde abajo
                _scannerEnabled = value;
            }
        }

        static readonly int ID_BarAlpha    = Shader.PropertyToID("_BarAlpha");
        static readonly int ID_ScanTimeOff = Shader.PropertyToID("_ScanTimeOffset");
        static readonly int ID_ScanColor   = Shader.PropertyToID("_ScanColor");
        static readonly Color ColorScan    = new Color(0.15f, 1.00f, 0.70f); // verde HUD

        // ── Se llama automáticamente al arrancar el juego, antes de cargar escenas
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void AutoInit()
        {
            CreateRunner();
        }

        static void CreateRunner()
        {
            if (_runnerCreated) return;
            _runnerCreated = true;

            var go = new GameObject("[ShaderTimekeeperRunner]")
            {
                hideFlags = HideFlags.DontSave
            };
            // DontDestroyOnLoad is only valid in Play mode — calling it from
            // an Editor-time build (e.g. generating the canvas without
            // pressing Play) throws and aborts BuildUI() halfway through.
            // DontSave above already keeps it out of the saved scene,
            // which is all that matters outside Play mode anyway.
            if (Application.isPlaying)
                Object.DontDestroyOnLoad(go);
            go.AddComponent<ShaderTimekeeperRunner>();
        }

        public static Material GetMaterial()
        {
            CreateRunner();

            if (_material != null) return _material;

            var source = Resources.Load<Material>("Materials/Background");
            if (source != null)
            {
                _material = new Material(source);
                TickOnceIfNotPlaying();
                return _material;
            }

            var shader = Shader.Find("ARDashboard/Background");
            if (shader != null)
            {
                _material = new Material(shader);
                Debug.LogWarning("[ShaderTimekeeper] Material creado desde shader (fallback).");
                TickOnceIfNotPlaying();
                return _material;
            }

            Debug.LogError("[ShaderTimekeeper] No se encontró Background en Resources ni el shader.");
            return null;
        }

        // Outside Play mode the runner's coroutine never starts (Unity only
        // runs MonoBehaviour Start/coroutines in Play mode), so the shader
        // would otherwise keep its uninitialized property values and render
        // blank. Set them once manually right after creating the material —
        // it won't animate in the Editor, but it'll at least look right.
        static void TickOnceIfNotPlaying()
        {
            if (!Application.isPlaying) Tick();
        }

        public static Material GetFPVMaterial()
        {
            CreateRunner();

            if (_fpvMaterial != null) return _fpvMaterial;

            var shader = Shader.Find("ARDashboard/FPVBlur");
            if (shader != null)
            {
                _fpvMaterial = new Material(shader);
                return _fpvMaterial;
            }

            Debug.LogError("[ShaderTimekeeper] No se encontró el shader ARDashboard/FPVBlur.");
            return null;
        }

        // ── El runner llama esto cada frame ───────────────────────────────────
        public static void Tick()
        {
            var mat = GetMaterial();
            if (mat != null)
            {
                mat.SetColor(ID_Deep,      ColorDeep);
                mat.SetColor(ID_Mid,       ColorMid);
                mat.SetColor(ID_Accent,    ColorAccent);
                mat.SetFloat(ID_GridSize,  GridSize);
                mat.SetFloat(ID_GridAlpha, GridAlpha);
                mat.SetFloat(ID_GridSpeed, GridSpeed);
                mat.SetFloat(ID_ScanAlpha,   ScannerEnabled ? ScanAlpha : 0f);
                mat.SetFloat(ID_BarAlpha,    ScannerEnabled ? 0.12f     : 0f);
                mat.SetFloat(ID_ScanTimeOff, ScannerEnabled ? Time.unscaledTime - _scanStartTime : 0f);
                mat.SetColor(ID_ScanColor,   ColorScan);
                mat.SetFloat(ID_PulseSpd,  PulseSpeed);
                mat.SetFloat(ID_PulseCnt,  PulseCount);
                mat.SetFloat(ID_Vig,       VigStr);
                mat.SetFloat(ID_TimeOff,   Time.unscaledTime);
            }

            var fpv = GetFPVMaterial();
            if (fpv != null)
                fpv.SetFloat(ID_TimeOff, Time.unscaledTime);
        }
    }
}