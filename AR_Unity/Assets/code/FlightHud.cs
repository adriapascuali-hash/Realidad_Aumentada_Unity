using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

namespace ARDashboard
{
    /// HUD de instrumentos de vuelo (altitud, velocidad, rumbo, escalera de
    /// cabeceo y retícula) — misma estética que el HUD del shooter
    /// (ShooterController.BuildHUD) pero sin nada de combate: sin contador de
    /// bajas, salud, arma ni indicadores de enemigos, que no tienen sentido
    /// fuera del juego de disparos.
    ///
    /// Se inyecta automáticamente en todas las escenas de vuelo (todas menos
    /// los menús puros y la propia escena "shooter", que ya trae su HUD
    /// completo). Visibilidad controlada en directo por FlightHud.Enabled,
    /// que se activa/desactiva desde el menú de pausa.
    public class FlightHud : MonoBehaviour
    {
        // ── Interruptor compartido (antes en FlightHudSettings.cs) ────────────
        // Lo consultan también PauseMenuUI (lo escribe) y ShooterController
        // (lo lee para su propio HUD, aunque no use esta clase para nada más).
        const string PREF_KEY = "FlightHud_Enabled";
        static bool _enabled = PlayerPrefs.GetInt(PREF_KEY, 1) != 0;

        public static bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                PlayerPrefs.SetInt(PREF_KEY, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        // ── Color del HUD: verde (por defecto), negro o blanco — alternable
        // desde el menú de pausa junto con Enabled. A diferencia de Enabled
        // (que solo activa/desactiva el Canvas), cambiar el color obliga a
        // reconstruir el HUD entero: la mayoría de sus elementos "hornean" el
        // color en el momento de crearse (ver CYAN_ en BuildHud) y no lo
        // vuelven a leer después, así que un simple cambio de esta propiedad
        // no bastaría.
        public enum HudColor { Green, Black, White }

        const string COLOR_PREF_KEY = "FlightHud_Color";
        static HudColor _color = (HudColor)PlayerPrefs.GetInt(COLOR_PREF_KEY, (int)HudColor.Green);

        public static HudColor ColorMode
        {
            get => _color;
            set
            {
                if (_color == value) return;
                _color = value;
                PlayerPrefs.SetInt(COLOR_PREF_KEY, (int)value);
                PlayerPrefs.Save();
                foreach (var hud in FindObjectsByType<FlightHud>(FindObjectsSortMode.None))
                    hud.RebuildHud();
            }
        }

        // ── Auto-inyección ────────────────────────────────────────────────────
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Subscribe() => SceneManager.sceneLoaded += OnSceneLoaded;

        static readonly HashSet<string> ExcludedScenes = new HashSet<string>(
            System.StringComparer.OrdinalIgnoreCase)
        {
            "Inicio", "Menú", "Configuración", "shooter",
        };

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (ExcludedScenes.Contains(scene.name)) return;
            var go = new GameObject("[FlightHud]");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<FlightHud>();
        }

        static readonly Color CYAN_GREEN = new Color(0.05f, 1.00f, 0.08f, 1.00f);
        static readonly Color CYAN_BLACK = new Color(0.00f, 0.00f, 0.00f, 1.00f);
        static readonly Color CYAN_WHITE = new Color(1.00f, 1.00f, 1.00f, 1.00f);
        static Color CYAN => _color switch
        {
            HudColor.Black => CYAN_BLACK,
            HudColor.White => CYAN_WHITE,
            _              => CYAN_GREEN,
        };

        /// Color actual del HUD, para HUDs que no son este componente pero
        /// siguen el mismo ajuste del menú de pausa (ShooterController).
        public static Color CurrentColor => CYAN;

        Camera           cam;
        Canvas           hudCanvas;
        RectTransform    crosshairContainer;
        Image[]          crosshairBars;
        Image            centerDot;
        RectTransform    fpvScaleL, fpvScaleR;
        TextMeshProUGUI  fpvAltText, fpvVelText;
        TextMeshProUGUI  headingTmp;
        const int        COMPASS_TICKS = 13;
        RectTransform[]  compassTickRts  = new RectTransform[COMPASS_TICKS];
        TextMeshProUGUI[] compassTickTmps = new TextMeshProUGUI[COMPASS_TICKS];
        RectTransform    pitchLadderRoot;
        const float      PITCH_PX_DEG = 14f;
        Vector3          lastCamPos;
        float            camSpeed, camSpeedSmoothed;
        float            fpvTime;
        Sprite           _tagShapeSpriteLeft, _tagShapeSpriteRight;

        void Start()
        {
            cam = Camera.main;
            if (cam != null) lastCamPos = cam.transform.position;
            BuildHud();
        }

        void Update()
        {
            bool enable = Enabled;
            if (hudCanvas != null && hudCanvas.enabled != enable)
                hudCanvas.enabled = enable;
            if (!enable) return;
            if (PauseMenuUI.IsPaused) return;

            if (cam == null) cam = Camera.main;
            if (cam == null) return;

            UpdateHud();
        }

        // ── Actualización en vivo ──────────────────────────────────────────────
        void UpdateHud()
        {
            fpvTime += Time.deltaTime;

            // ── Pitch ladder: se desplaza con el pitch y gira con el roll ──────
            if (pitchLadderRoot != null)
            {
                float rawPitch = cam.transform.eulerAngles.x;
                float pitchDeg = rawPitch > 180f ? 360f - rawPitch : -rawPitch;
                pitchLadderRoot.anchoredPosition = new Vector2(0f, -pitchDeg * PITCH_PX_DEG);

                float rawRoll = cam.transform.eulerAngles.z;
                float rollDeg = rawRoll > 180f ? rawRoll - 360f : rawRoll;
                pitchLadderRoot.localEulerAngles = new Vector3(0f, 0f, -rollDeg);
            }

            // ── Escalas de altitud/velocidad ────────────────────────────────────
            camSpeed         = Vector3.Distance(cam.transform.position, lastCamPos) / Mathf.Max(Time.deltaTime, 0.0001f);
            lastCamPos       = cam.transform.position;
            camSpeedSmoothed = Mathf.Lerp(camSpeedSmoothed, camSpeed, Time.deltaTime * 1.5f);

            const float SCALE_YSTEP = 56f;
            float altVal = cam.transform.position.y;
            float velVal = camSpeedSmoothed;
            if (fpvScaleL   != null) fpvScaleL.anchoredPosition = new Vector2(0f, -altVal * SCALE_YSTEP);
            if (fpvScaleR   != null) fpvScaleR.anchoredPosition = new Vector2(0f, -velVal * SCALE_YSTEP);
            if (fpvAltText  != null) fpvAltText.text = $"{altVal:F1}";
            if (fpvVelText  != null) fpvVelText.text = $"{velVal:F1}";

            // ── Brújula ─────────────────────────────────────────────────────────
            float hdg = cam.transform.eulerAngles.y;
            if (headingTmp != null)
            {
                int hdgRounded = Mathf.RoundToInt(hdg) % 360;
                headingTmp.text = $"{hdgRounded}°";
            }
            if (compassTickRts[0] != null)
            {
                const float PX_DEG = 360f / 120f, HALF_RNG = 60f;
                float minDeg = hdg - HALF_RNG;
                int first = Mathf.CeilToInt(minDeg / 10f) * 10;
                for (int ti = 0; ti < COMPASS_TICKS; ti++)
                {
                    float divDeg = first + ti * 10f;
                    float x = (divDeg - hdg) * PX_DEG;
                    if (Mathf.Abs(x) > HALF_RNG * PX_DEG + 20f)
                    { compassTickRts[ti].gameObject.SetActive(false); continue; }
                    compassTickRts[ti].gameObject.SetActive(true);
                    compassTickRts[ti].anchoredPosition = new Vector2(x, 0f);
                    int norm = (int)(divDeg % 360 + 360) % 360;
                    if (compassTickTmps[ti] != null)
                        compassTickTmps[ti].text = norm % 30 == 0 ? CompassLabel(norm) : "";
                }
            }

            // ── Retícula: parpadeo suave, sin lógica de blancos ────────────────
            if (crosshairBars != null)
            {
                float flicker = 0.90f + 0.10f * Mathf.PerlinNoise(fpvTime * 13f, 5f);
                Color glowCol = new Color(CYAN.r, CYAN.g, CYAN.b, flicker);
                foreach (var bar in crosshairBars) if (bar) bar.color = glowCol;
                if (centerDot) centerDot.color = glowCol;
            }
        }

        static string CompassLabel(int deg)
        {
            return deg == 0   ? "N" : deg == 90  ? "E"
                 : deg == 180 ? "S" : deg == 270 ? "W"
                 : $"{deg:000}";
        }

        // Destruye el HUD ya construido y lo vuelve a montar desde cero — único
        // modo de aplicar un cambio de color, ver comentario en ColorMode.
        void RebuildHud()
        {
            if (hudCanvas != null) Destroy(hudCanvas.gameObject);
            BuildHud();
        }

        // ── Construcción del HUD ────────────────────────────────────────────────
        void BuildHud()
        {
            var canvasGO = new GameObject("FlightHudCanvas");
            canvasGO.transform.SetParent(transform);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 190; // por debajo del HUD de combate del shooter (200)
            hudCanvas = canvas;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGO.AddComponent<GraphicRaycaster>();
            canvas.enabled = Enabled;

            var CYAN_ = CYAN;
            float panelH = 680f;
            float cyO = panelH * .5f + 38f; // 378, mismo offset que ShooterController

            System.Action<Transform, Vector2, Vector2, float> HBar =
                (parent, pos, size, alpha) => {
                    var g = new GameObject("HBar");
                    g.transform.SetParent(parent, false);
                    var img = g.AddComponent<Image>(); img.raycastTarget = false;
                    img.color = new Color(CYAN_.r, CYAN_.g, CYAN_.b, alpha);
                    var rt = g.GetComponent<RectTransform>();
                    rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = pos; rt.sizeDelta = size;
                };

            // ── Escalas laterales de altitud/velocidad ─────────────────────────
            BuildFPVScale(canvasGO.transform, -760f, true,  CYAN_, out fpvScaleL, out fpvAltText);
            BuildFPVScale(canvasGO.transform,  760f, false, CYAN_, out fpvScaleR, out fpvVelText);

            // ── Retícula central ─────────────────────────────────────────────────
            var chGO = new GameObject("Crosshair");
            chGO.transform.SetParent(canvasGO.transform, false);
            crosshairContainer = chGO.AddComponent<RectTransform>();
            crosshairContainer.anchorMin = crosshairContainer.anchorMax = new Vector2(.5f, .5f);
            crosshairContainer.pivot     = new Vector2(.5f, .5f);
            crosshairContainer.sizeDelta = Vector2.zero;
            crosshairContainer.anchoredPosition = Vector2.zero;
            const float BL = 65f, BG = 10f, BT = 1.8f, VH = 18f, TC = 10f;
            crosshairBars = new Image[13]; int bi = 0;
            crosshairBars[bi++] = MakeBar(chGO.transform, new Vector2(-(BG + BL * .5f), 0f), new Vector2(BL, BT));
            crosshairBars[bi++] = MakeBar(chGO.transform, new Vector2( (BG + BL * .5f), 0f), new Vector2(BL, BT));
            crosshairBars[bi++] = MakeBar(chGO.transform, new Vector2(-(BG + BL), -TC * 0.5f), new Vector2(BT, TC));
            crosshairBars[bi++] = MakeBar(chGO.transform, new Vector2( (BG + BL), -TC * 0.5f), new Vector2(BT, TC));
            crosshairBars[bi++] = MakeBar(chGO.transform, new Vector2(-(BG + BL * .5f - 18f), 0f), new Vector2(BT, TC * .55f));
            crosshairBars[bi++] = MakeBar(chGO.transform, new Vector2( (BG + BL * .5f - 18f), 0f), new Vector2(BT, TC * .55f));
            crosshairBars[bi++] = MakeBar(chGO.transform, new Vector2(0f,  VH * .5f), new Vector2(BT, VH));
            crosshairBars[bi++] = MakeBar(chGO.transform, new Vector2(0f, -VH * .5f), new Vector2(BT, VH));
            crosshairBars[bi++] = MakeBar(chGO.transform, new Vector2(0f,  VH), new Vector2(TC, BT));
            crosshairBars[bi++] = MakeBar(chGO.transform, new Vector2(0f, -VH), new Vector2(TC, BT));
            crosshairBars[bi++] = MakeBar(chGO.transform, new Vector2(0f, 0f), new Vector2(BG * 1.2f, BT));
            centerDot           = MakeBar(chGO.transform, Vector2.zero, new Vector2(3f, 3f));
            crosshairBars[bi++] = centerDot;
            crosshairBars[bi++] = MakeBar(chGO.transform, new Vector2(0f, BG * .6f), new Vector2(BT * 1.5f, BT * 1.5f));
            foreach (var bar in crosshairBars) if (bar) bar.color = CYAN_;

            // ── Brújula deslizante ──────────────────────────────────────────────
            var cmpGO = new GameObject("Compass");
            cmpGO.transform.SetParent(canvasGO.transform, false);
            var cmpRt = cmpGO.AddComponent<RectTransform>();
            cmpRt.anchorMin = cmpRt.anchorMax = new Vector2(.5f, .5f); cmpRt.pivot = new Vector2(.5f, .5f);
            cmpRt.anchoredPosition = new Vector2(0f, cyO + 50f); cmpRt.sizeDelta = new Vector2(300f, 30f);
            // Escala la tira (marcas, letras e indicador "▼", todos hijos de cmpGO)
            // en bloque, sin tener que tocar cada tamaño/espaciado a mano. El
            // número de rumbo de abajo (HdgVal) no es hijo de cmpGO, se escala
            // aparte con el mismo factor justo donde se crea, más abajo.
            const float COMPASS_SCALE = 1.20f;
            cmpRt.localScale = Vector3.one * COMPASS_SCALE;
            {
                var idxGO = new GameObject("Idx");
                idxGO.transform.SetParent(cmpGO.transform, false);
                var idxTmp = idxGO.AddComponent<TextMeshProUGUI>();
                idxTmp.text = "▼"; idxTmp.fontSize = 7f; idxTmp.raycastTarget = false;
                idxTmp.color = new Color(CYAN_.r, CYAN_.g, CYAN_.b, 0.94f); idxTmp.alignment = TextAlignmentOptions.Center;
                var idxRt = idxGO.GetComponent<RectTransform>();
                idxRt.anchorMin = idxRt.anchorMax = new Vector2(.5f, 0f); idxRt.pivot = new Vector2(.5f, 0f);
                idxRt.anchoredPosition = new Vector2(0f, -14f); idxRt.sizeDelta = new Vector2(14f, 12f);
            }
            {
                var hv = new GameObject("HdgVal"); hv.transform.SetParent(canvasGO.transform, false);
                headingTmp = hv.AddComponent<TextMeshProUGUI>();
                headingTmp.text = "000"; headingTmp.fontSize = 15f; headingTmp.fontStyle = FontStyles.Bold;
                headingTmp.color = new Color(CYAN_.r, CYAN_.g, CYAN_.b, 0.90f); headingTmp.alignment = TextAlignmentOptions.Center;
                headingTmp.raycastTarget = false;
                var hvRt = hv.GetComponent<RectTransform>();
                hvRt.anchorMin = hvRt.anchorMax = new Vector2(.5f, .5f); hvRt.pivot = new Vector2(.5f, .5f);
                hvRt.anchoredPosition = new Vector2(0f, cyO + 8f); hvRt.sizeDelta = new Vector2(80f, 24f);
                hvRt.localScale = Vector3.one * COMPASS_SCALE;
            }
            for (int ti = 0; ti < COMPASS_TICKS; ti++)
            {
                var tGO = new GameObject($"CT{ti}"); tGO.transform.SetParent(cmpGO.transform, false);
                var tRt = tGO.AddComponent<RectTransform>();
                tRt.anchorMin = tRt.anchorMax = new Vector2(.5f, 1f); tRt.pivot = new Vector2(.5f, 1f);
                tRt.sizeDelta = Vector2.zero; compassTickRts[ti] = tRt;
                HBar(tGO.transform, new Vector2(0f, -9f), new Vector2(2.5f, 6f), 1.00f);
                var tLGO = new GameObject("L"); tLGO.transform.SetParent(tGO.transform, false);
                var tTmp = tLGO.AddComponent<TextMeshProUGUI>();
                tTmp.fontSize = 13f; tTmp.color = new Color(CYAN_.r, CYAN_.g, CYAN_.b, 1.00f);
                tTmp.alignment = TextAlignmentOptions.Center; tTmp.raycastTarget = false;
                var tLRt = tLGO.GetComponent<RectTransform>();
                tLRt.anchorMin = tLRt.anchorMax = new Vector2(.5f, 1f); tLRt.pivot = new Vector2(.5f, 1f);
                tLRt.anchoredPosition = new Vector2(0f, -17f); tLRt.sizeDelta = new Vector2(28f, 11f);
                compassTickTmps[ti] = tTmp;
            }

            // ── Escalera de cabeceo (pitch ladder) ──────────────────────────────
            {
                var maskGO = new GameObject("PitchMask");
                maskGO.transform.SetParent(canvasGO.transform, false);
                maskGO.AddComponent<RectMask2D>();
                var maskRt = maskGO.GetComponent<RectTransform>();
                maskRt.anchorMin = maskRt.anchorMax = new Vector2(0.5f, 0.5f);
                maskRt.pivot     = new Vector2(0.5f, 0.5f);
                maskRt.sizeDelta = new Vector2(900f, 420f);
                maskRt.anchoredPosition = Vector2.zero;

                var rootGO = new GameObject("PitchRoot");
                rootGO.transform.SetParent(maskGO.transform, false);
                pitchLadderRoot = rootGO.AddComponent<RectTransform>();
                pitchLadderRoot.anchorMin = pitchLadderRoot.anchorMax = new Vector2(0.5f, 0.5f);
                pitchLadderRoot.pivot     = new Vector2(0.5f, 0.5f);
                pitchLadderRoot.sizeDelta = Vector2.zero;
                pitchLadderRoot.anchoredPosition = Vector2.zero;

                const float GAP = 80f, MAJ_W = 96f, LH = BT, CAP_H = 10f;

                System.Action<Transform, Vector2, Vector2, Color> Seg = (par, pos, sz, col) => {
                    var g = new GameObject("S"); g.transform.SetParent(par, false);
                    var im = g.AddComponent<Image>(); im.color = col; im.raycastTarget = false;
                    var rt = g.GetComponent<RectTransform>();
                    rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = pos; rt.sizeDelta = sz;
                };

                for (int deg = -90; deg <= 90; deg += 10)
                {
                    if (deg == 0) continue;
                    float y      = deg * PITCH_PX_DEG;
                    Color lc     = new Color(CYAN_.r, CYAN_.g, CYAN_.b, 0.82f);
                    float capDir = deg > 0 ? -1f : 1f;
                    float capY   = y + capDir * CAP_H * 0.5f;

                    Seg(rootGO.transform, new Vector2(-(GAP + MAJ_W * 0.5f), y),   new Vector2(MAJ_W, LH),   lc);
                    Seg(rootGO.transform, new Vector2(-(GAP + MAJ_W),         capY), new Vector2(LH,    CAP_H), lc);
                    Seg(rootGO.transform, new Vector2( (GAP + MAJ_W * 0.5f), y),   new Vector2(MAJ_W, LH),   lc);
                    Seg(rootGO.transform, new Vector2( (GAP + MAJ_W),         capY), new Vector2(LH,    CAP_H), lc);

                    foreach (float sx in new float[] { -(GAP + MAJ_W + 18f), (GAP + MAJ_W + 18f) })
                    {
                        var lblGO = new GameObject($"PL_N{deg}"); lblGO.transform.SetParent(rootGO.transform, false);
                        var lbl   = lblGO.AddComponent<TextMeshProUGUI>();
                        lbl.text  = $"{Mathf.Abs(deg)}°"; lbl.fontSize = 14f;
                        lbl.fontStyle = FontStyles.Bold; lbl.raycastTarget = false;
                        lbl.color = new Color(CYAN_.r, CYAN_.g, CYAN_.b, 0.75f);
                        lbl.alignment = sx < 0 ? TextAlignmentOptions.Right : TextAlignmentOptions.Left;
                        var lblRt = lblGO.GetComponent<RectTransform>();
                        lblRt.anchorMin = lblRt.anchorMax = lblRt.pivot = new Vector2(0.5f, 0.5f);
                        lblRt.anchoredPosition = new Vector2(sx, y);
                        lblRt.sizeDelta = new Vector2(28f, 14f);
                    }
                }
            }

            AddHudGlow(canvasGO.transform);
        }

        // ── Glow ─────────────────────────────────────────────────────────────
        // Sobre fondos claros (cielo, nubes) un trazo fino se lava y pierde
        // contraste. En vez de tocar su ancho o su verde, se añade detrás un
        // halo fino y difuminado del mismo color (aclarado hacia blanco) que
        // se ciñe al contorno de cada elemento — no un bloque rectangular
        // suelto — y se lee como luz emitida por la línea.
        const float GLOW_PAD = 1.6f, GLOW_ALPHA_MUL = 0.95f, GLOW_WHITE_MIX = 0.25f;
        static Sprite _glowSprite;

        // Mancha difuminada (sin meseta plana: el degradado ocupa todo el
        // sprite) para el halo de las líneas/barras rectangulares simples.
        static Sprite BuildGlowSprite()
        {
            if (_glowSprite != null) return _glowSprite;
            const int size = 16;
            const float feather = size * 0.5f;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Min(Mathf.Min(x, size - 1 - x), Mathf.Min(y, size - 1 - y));
                    float t = Mathf.Clamp01(d / feather); // 0 en el borde, 1 en el centro

                    // Caída tipo gaussiana en vez de un smoothstep: plateau
                    // suave junto al trazo y una cola larga y progresiva hacia
                    // fuera, en lugar de un corte más brusco — es lo que da el
                    // aspecto de neón (luz que se apaga poco a poco).
                    float a    = Mathf.Exp(-4f * (1f - t) * (1f - t));
                    byte  core = (byte)Mathf.Lerp(210f, 255f, t); // núcleo algo más caliente/blanco

                    px[y * size + x] = new Color32(core, core, core, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(true);
            tex.filterMode = FilterMode.Bilinear;
            _glowSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return _glowSprite;
        }

        static void AddHudGlow(Transform root)
        {
            var blobSprite = BuildGlowSprite();
            foreach (var core in root.GetComponentsInChildren<Image>(true))
            {
                var glowGO = new GameObject("Glow");
                glowGO.transform.SetParent(core.transform.parent, false);
                var glow = glowGO.AddComponent<Image>();
                glow.raycastTarget = false;

                var coreRt = core.rectTransform;
                var glowRt = glowGO.GetComponent<RectTransform>();
                glowRt.anchorMin = coreRt.anchorMin; glowRt.anchorMax = coreRt.anchorMax; glowRt.pivot = coreRt.pivot;
                glowRt.anchoredPosition = coreRt.anchoredPosition;

                if (core.sprite != null)
                {
                    // Iconos con forma propia (p. ej. las cajas ALT/VEL): el halo
                    // reutiliza el mismo sprite, sólo un poco más grande, para
                    // que siga exactamente su silueta en vez de asomar como un
                    // bloque rectangular por detrás.
                    glow.sprite = core.sprite;
                    glow.type   = core.type;
                    glowRt.sizeDelta = coreRt.sizeDelta + new Vector2(GLOW_PAD, GLOW_PAD) * 2f;
                }
                else
                {
                    glow.sprite = blobSprite;
                    glow.type   = Image.Type.Simple;
                    glowRt.sizeDelta = coreRt.sizeDelta + new Vector2(GLOW_PAD * 2f, GLOW_PAD * 2f);
                }

                glowGO.transform.SetSiblingIndex(core.transform.GetSiblingIndex()); // detrás del trazo

                var link = glowGO.AddComponent<HudGlowLink>();
                link.core = core; link.glow = glow;
                link.Sync();
            }

            foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
            {
                // Outline de TMP: el shader lo soporta pero lo mantiene apagado
                // por keyword salvo que se active explícitamente (el material
                // por defecto del proyecto no la trae activada de fábrica).
                t.outlineWidth = 0.20f;
                t.outlineColor = Color.Lerp(t.color, Color.white, 0.35f);
                t.fontMaterial.EnableKeyword("OUTLINE_ON");
            }
        }

        // Mantiene el halo sincronizado cuando el trazo cambia de color en vivo
        // (p. ej. el parpadeo de la retícula).
        class HudGlowLink : MonoBehaviour
        {
            public Image core, glow;
            Color lastCoreColor;

            void LateUpdate()
            {
                if (core == null || glow == null) { enabled = false; return; }
                if (core.color == lastCoreColor) return;
                Sync();
            }

            public void Sync()
            {
                lastCoreColor = core.color;
                var tinted = Color.Lerp(lastCoreColor, Color.white, GLOW_WHITE_MIX);
                glow.color  = new Color(tinted.r, tinted.g, tinted.b, lastCoreColor.a * GLOW_ALPHA_MUL);
            }
        }

        void BuildFPVScale(Transform parent, float xPos, bool isLeft, Color cyan,
            out RectTransform tickRoot, out TextMeshProUGUI valLabel)
        {
            const float YSTEP = 56f;
            const int   HALF  = 20;
            const float CLIPH = YSTEP * 12f;
            float rOff = isLeft ?  36f : -36f;
            float tDir = isLeft ?   1f :  -1f;
            float lbX  = isLeft ? -20f :  20f;
            float mW   = 20f, mnW = 10f;
            string side = isLeft ? "L" : "R";
            TextAlignmentOptions ta = isLeft ? TextAlignmentOptions.Right : TextAlignmentOptions.Left;

            var sgGO = new GameObject($"ScaleGroup_{side}");
            sgGO.transform.SetParent(parent, false);
            sgGO.AddComponent<RectMask2D>();
            var sgRt = sgGO.GetComponent<RectTransform>();
            sgRt.anchorMin = sgRt.anchorMax = sgRt.pivot = new Vector2(0.5f, 0.5f);
            sgRt.anchoredPosition = new Vector2(xPos, 0f);
            sgRt.sizeDelta = new Vector2(100f, CLIPH);

            {
                var g = new GameObject("Rail"); g.transform.SetParent(sgGO.transform, false);
                var img = g.AddComponent<Image>(); img.raycastTarget = false;
                img.color = new Color(cyan.r, cyan.g, cyan.b, 1.00f);
                var rt = g.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(rOff, 0f);
                rt.sizeDelta = new Vector2(2.5f, CLIPH);
            }

            var tGO = new GameObject($"TickRoot_{side}"); tGO.transform.SetParent(sgGO.transform, false);
            var tRt = tGO.AddComponent<RectTransform>();
            tRt.anchorMin = tRt.anchorMax = tRt.pivot = new Vector2(0.5f, 0.5f);
            tRt.anchoredPosition = Vector2.zero; tRt.sizeDelta = Vector2.zero;
            tickRoot = tRt;

            for (int i = -(HALF + 2); i <= (HALF + 2); i++)
            {
                float yy  = i * YSTEP;
                int   val = i;
                bool  maj = val % 5 == 0;
                float tw  = maj ? mW : mnW;

                var bGO = new GameObject("Bar"); bGO.transform.SetParent(tRt, false);
                var bImg = bGO.AddComponent<Image>(); bImg.raycastTarget = false;
                bImg.color = new Color(cyan.r, cyan.g, cyan.b, maj ? 1.00f : 0.70f);
                var bRt = bGO.GetComponent<RectTransform>();
                bRt.anchorMin = bRt.anchorMax = bRt.pivot = new Vector2(0.5f, 0.5f);
                bRt.anchoredPosition = new Vector2(rOff + tDir * tw * .5f, yy);
                bRt.sizeDelta = new Vector2(tw, maj ? 2.5f : 2f);

                if (maj)
                {
                    var lGO = new GameObject($"ScaleTick_{side}_{i}");
                    lGO.transform.SetParent(tRt, false);
                    var lTmp = lGO.AddComponent<TextMeshProUGUI>(); lTmp.raycastTarget = false;
                    lTmp.text = val.ToString();
                    lTmp.fontSize = 14f;
                    lTmp.color = new Color(cyan.r, cyan.g, cyan.b, val == 0 ? 1.00f : 0.90f);
                    lTmp.fontStyle = FontStyles.Bold; lTmp.alignment = ta;
                    var lRt = lGO.GetComponent<RectTransform>();
                    lRt.anchorMin = lRt.anchorMax = lRt.pivot = new Vector2(0.5f, 0.5f);
                    lRt.anchoredPosition = new Vector2(lbX, yy);
                    lRt.sizeDelta = new Vector2(50f, 18f);
                }
            }

            {
                var g = new GameObject("Pointer"); g.transform.SetParent(parent, false);
                var img = g.AddComponent<Image>(); img.raycastTarget = false;
                img.color = new Color(cyan.r, cyan.g, cyan.b, 1.00f);
                var rt = g.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(xPos + rOff + tDir * (mW + 4f), 0f);
                rt.sizeDelta = new Vector2(mW + 8f, 3f);
            }

            float vx = xPos + rOff + (isLeft ? 90f : -90f);

            if (isLeft)
            {
                var alGO = new GameObject("AltLabel"); alGO.transform.SetParent(parent, false);
                var alTmp = alGO.AddComponent<TextMeshProUGUI>(); alTmp.raycastTarget = false;
                alTmp.text = "ALT"; alTmp.fontSize = 12f; alTmp.fontStyle = FontStyles.Bold;
                alTmp.color = new Color(cyan.r, cyan.g, cyan.b, 1.00f);
                alTmp.alignment = TextAlignmentOptions.Center; alTmp.characterSpacing = 0f;
                var alRt = alGO.GetComponent<RectTransform>();
                alRt.anchorMin = alRt.anchorMax = alRt.pivot = new Vector2(0.5f, 0.5f);
                alRt.anchoredPosition = new Vector2(vx + 8f, 28f); alRt.sizeDelta = new Vector2(140f, 18f);

                const float BOX_W = 66f, BOX_H = 20f;
                var boxGO = new GameObject("AltValueBox"); boxGO.transform.SetParent(parent, false);
                var boxRt = boxGO.AddComponent<RectTransform>();
                boxRt.anchorMin = boxRt.anchorMax = boxRt.pivot = new Vector2(0.5f, 0.5f);
                boxRt.anchoredPosition = new Vector2(vx, 0f); boxRt.sizeDelta = new Vector2(BOX_W, BOX_H);
                var boxImg = boxGO.AddComponent<Image>();
                boxImg.sprite = BuildTagShapeSprite(pointRight: false); boxImg.type = Image.Type.Simple;
                boxImg.color = new Color(cyan.r, cyan.g, cyan.b, 0.90f); boxImg.raycastTarget = false;

                var valGO = new GameObject("AltValue"); valGO.transform.SetParent(boxGO.transform, false);
                var valTmp = valGO.AddComponent<TextMeshProUGUI>(); valTmp.raycastTarget = false;
                valTmp.text = "--.-"; valTmp.fontSize = 14f; valTmp.fontStyle = FontStyles.Bold;
                valTmp.color = new Color(cyan.r, cyan.g, cyan.b, 1.00f);
                valTmp.alignment = TextAlignmentOptions.Center;
                var valRt = valGO.GetComponent<RectTransform>();
                valRt.anchorMin = Vector2.zero; valRt.anchorMax = Vector2.one;
                valRt.offsetMin = new Vector2(BOX_W * 0.22f, 0f); valRt.offsetMax = Vector2.zero;
                valLabel = valTmp;
            }
            else
            {
                var vlGO = new GameObject("VelLabel"); vlGO.transform.SetParent(parent, false);
                var vlTmp = vlGO.AddComponent<TextMeshProUGUI>(); vlTmp.raycastTarget = false;
                vlTmp.text = "VEL"; vlTmp.fontSize = 12f; vlTmp.fontStyle = FontStyles.Bold;
                vlTmp.color = new Color(cyan.r, cyan.g, cyan.b, 1.00f);
                vlTmp.alignment = TextAlignmentOptions.Center; vlTmp.characterSpacing = 0f;
                var vlRt = vlGO.GetComponent<RectTransform>();
                vlRt.anchorMin = vlRt.anchorMax = vlRt.pivot = new Vector2(0.5f, 0.5f);
                vlRt.anchoredPosition = new Vector2(vx - 8f, 28f); vlRt.sizeDelta = new Vector2(140f, 18f);

                const float BOX_W = 66f, BOX_H = 20f;
                var boxGO = new GameObject("VelValueBox"); boxGO.transform.SetParent(parent, false);
                var boxRt = boxGO.AddComponent<RectTransform>();
                boxRt.anchorMin = boxRt.anchorMax = boxRt.pivot = new Vector2(0.5f, 0.5f);
                boxRt.anchoredPosition = new Vector2(vx, 0f); boxRt.sizeDelta = new Vector2(BOX_W, BOX_H);
                var boxImg = boxGO.AddComponent<Image>();
                boxImg.sprite = BuildTagShapeSprite(pointRight: true); boxImg.type = Image.Type.Simple;
                boxImg.color = new Color(cyan.r, cyan.g, cyan.b, 0.90f); boxImg.raycastTarget = false;

                var vGO = new GameObject("VelValue"); vGO.transform.SetParent(boxGO.transform, false);
                var vTmp = vGO.AddComponent<TextMeshProUGUI>(); vTmp.raycastTarget = false;
                vTmp.text = "0.0"; vTmp.fontSize = 14f; vTmp.fontStyle = FontStyles.Bold;
                vTmp.color = new Color(cyan.r, cyan.g, cyan.b, 1.00f);
                vTmp.alignment = TextAlignmentOptions.Center;
                var vRt = vGO.GetComponent<RectTransform>();
                vRt.anchorMin = Vector2.zero; vRt.anchorMax = Vector2.one;
                vRt.offsetMin = Vector2.zero; vRt.offsetMax = new Vector2(-BOX_W * 0.22f, 0f);
                valLabel = vTmp;
            }
        }

        Sprite BuildTagShapeSprite(bool pointRight)
        {
            if (pointRight && _tagShapeSpriteRight != null) return _tagShapeSpriteRight;
            if (!pointRight && _tagShapeSpriteLeft  != null) return _tagShapeSpriteLeft;

            const int texW = 256, texH = 96;
            const float pointFrac = 0.22f;
            const float aa = 2.5f;
            const float thickness = 10f;
            float pointW = texW * pointFrac;
            float halfH  = texH * 0.5f;

            var tex = new Texture2D(texW, texH, TextureFormat.RGBA32, true);
            tex.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color32[texW * texH];
            for (int y = 0; y < texH; y++)
            {
                for (int x = 0; x < texW; x++)
                {
                    float xx = pointRight ? (texW - 1 - x) : x;

                    float dTop, dBot;
                    if (xx >= pointW)
                    {
                        dTop = texH - y; dBot = y;
                    }
                    else
                    {
                        float t = xx / pointW;
                        dTop = Mathf.Lerp(halfH, texH, t) - y;
                        dBot = y - Mathf.Lerp(halfH, 0f, t);
                    }
                    float dOpen = pointRight ? x : (texW - x);
                    float d = Mathf.Min(dTop, Mathf.Min(dBot, dOpen));

                    float outerFade = Mathf.Clamp01((d + aa * 0.5f) / aa);
                    float innerFade = 1f - Mathf.Clamp01((d - thickness + aa * 0.5f) / aa);
                    float alpha = Mathf.Clamp01(Mathf.Min(outerFade, innerFade));

                    pixels[y * texW + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(true);
            tex.filterMode = FilterMode.Trilinear;

            var sprite = Sprite.Create(tex, new Rect(0, 0, texW, texH),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            if (pointRight) _tagShapeSpriteRight = sprite; else _tagShapeSpriteLeft = sprite;
            return sprite;
        }

        Image MakeBar(Transform parent, Vector2 pos, Vector2 size)
        {
            var go  = new GameObject("Bar");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            var rt  = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta        = size;
            return img;
        }
    }
}
