using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using GLTFast;

namespace ARDashboard
{
    /// Menú de configuración previo al shooter.
    /// Mismo estilo visual que Menú.
    public class ShooterConfigMenu : MonoBehaviour
    {
        /// Escena a cargar al confirmar (se rellena desde Configuración).
        public static string TargetScene = "";

        /// Acción a ejecutar al pulsar Volver (restaura el menú de origen).
        public static System.Action ReturnAction = null;

        // ── Colores idénticos a Menú ────────────────────────────
        static readonly Color C_ACCENT = new Color(0.15f, 1.00f, 0.70f, 1f);
        static readonly Color C_CYAN   = new Color(0.00f, 0.95f, 1.00f, 1f);
        static readonly Color C_BORDER = new Color(0.16f, 0.22f, 0.32f, 1f);
        static readonly Color C_TEXT   = new Color(0.90f, 0.95f, 1.00f, 1f);
        static readonly Color C_SUB    = new Color(0.42f, 0.58f, 0.72f, 1f);
        static readonly Color C_DISAB  = new Color(0.18f, 0.22f, 0.30f, 1f);
        static readonly Color C_SEL    = new Color(0.06f, 0.15f, 0.32f, 1f);

        // ── Modelos ───────────────────────────────────────────────────────────
        static readonly string[] ModelPaths =
        {
            Path.Combine(Application.streamingAssetsPath, "Drone", "scene.gltf"),
            Path.Combine(Application.streamingAssetsPath, "TieInterceptor", "scene.gltf"),
        };
        static readonly string[] EnemyNames    = { "DRONE", "TIE INTERCEPTOR" };
        static readonly float[]  EnemyScales   = { 0.5f,   0.04f  };  // escala en juego
        static readonly float[]  PreviewScales = { 3.5f,   0.25f  };  // escala en preview
        static readonly Vector3[] PreviewOffsets = { Vector3.zero, Vector3.zero };

        // ── HUD lateral ───────────────────────────────────────────────────────
        RectTransform   scaleLeftTicks, scaleRightTicks;
        TextMeshProUGUI altText, velText;
        float           hudTime, scaleLeftVal, scaleRightVal;
        CanvasGroup     hudGroup, blurGroup;
        float           hudFadeTime, blurFadeTime;
        const float     HUD_FADE = 0.4f;

        // ── Preview 3D ────────────────────────────────────────────────────────
        Camera          previewCam;
        RenderTexture   previewRT;
        GameObject      previewModel;
        float           previewYaw      = 0f;
        float           previewPitch    = 12f;
        bool            previewDragging = false;
        TextMeshProUGUI previewNameTmp;

        // ── Refs de controles ─────────────────────────────────────────────────
        // dropdown de tipo de enemigo (estado gestionado localmente en Build)
        Button[]          diffButtons;
        TextMeshProUGUI[] diffTmps;
        Button            barriersYes, barriersNo;
        TextMeshProUGUI   barriersYesTmp, barriersNoTmp;
        TMP_InputField    heightInput;
        Button            heightMinus, heightPlus;

        const float HEIGHT_MIN  = 0.1f;
        const float HEIGHT_MAX  = 10.0f;
        const float HEIGHT_STEP = 0.1f;

        // ─────────────────────────────────────────────────────────────────────
        void Start() => StartCoroutine(Build());

        void Update()
        {
            // Usar unscaledDeltaTime para que funcione aunque timeScale = 0
            float dt = Time.unscaledDeltaTime;
            hudTime += dt;

            float alt = 28.7f + Mathf.Sin(hudTime * 0.4f) * 0.6f;
            float vel = Mathf.Abs(Mathf.Sin(hudTime * 1.2f) * 2.3f);
            if (altText) altText.text = $"ALT\n{alt:F1}";
            if (velText) velText.text = $"VEL\n{vel:F1}";

            const float yStep = 56f, uStep = 5f;
            float altSnap = Mathf.Round(alt / uStep) * uStep;
            float velSnap = Mathf.Round(vel / uStep) * uStep;
            float pxU = yStep / uStep;
            scaleLeftVal  = Mathf.Lerp(scaleLeftVal,  -(alt - altSnap) * pxU, dt * 6f);
            scaleRightVal = Mathf.Lerp(scaleRightVal, -(vel - velSnap) * pxU, dt * 6f);

            if (scaleLeftTicks)  { var p = scaleLeftTicks.anchoredPosition;  p.y = scaleLeftVal;  scaleLeftTicks.anchoredPosition  = p; UpdateScaleLabels(scaleLeftTicks,  "L", altSnap, 5); }
            if (scaleRightTicks) { var p = scaleRightTicks.anchoredPosition; p.y = scaleRightVal; scaleRightTicks.anchoredPosition = p; UpdateScaleLabels(scaleRightTicks, "R", velSnap, 5); }

            if (hudGroup  != null && hudGroup.alpha  < 1f) { hudFadeTime  += dt; hudGroup.alpha  = Mathf.Clamp01(hudFadeTime  / HUD_FADE); }
            if (blurGroup != null && blurGroup.alpha < 1f) { blurFadeTime += dt; blurGroup.alpha = Mathf.Clamp01(blurFadeTime / HUD_FADE); }

            if (previewModel != null)
            {
                // Auto-rotación cuando no hay drag activo
                if (!previewDragging) previewYaw += dt * 40f;
                previewModel.transform.rotation = Quaternion.Euler(previewPitch, previewYaw, 0f);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        IEnumerator Build()
        {
            // ── Canvas ───────────────────────────────────────────────────────
            var cGO = new GameObject("ShooterConfigCanvas");
            cGO.transform.SetParent(transform);
            var canvas = cGO.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = string.IsNullOrEmpty(TargetScene) ? 500 : 1000;
            var scaler = cGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            cGO.AddComponent<GraphicRaycaster>();

            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var esGO = new GameObject("EventSystem");
                esGO.AddComponent<EventSystem>();
                esGO.AddComponent<StandaloneInputModule>();
                SceneManager.MoveGameObjectToScene(esGO, gameObject.scene);
            }

            // ── Fondo: gradiente + shader animado ────────────────────────────
            var bgGO = new GameObject("BG");
            bgGO.transform.SetParent(cGO.transform, false);
            Stretch(bgGO.AddComponent<RectTransform>());
            bgGO.AddComponent<Image>().color = Color.black;

            var gradStart = new Color(0f, 0f, 0f, 1f);
            var gradEnd   = new Color(0.08f, 0.22f, 0.48f, 1f);
            for (int i = 0; i < 50; i++)
            {
                var b = new GameObject("Gb"); b.transform.SetParent(bgGO.transform, false);
                var bI = b.AddComponent<Image>(); bI.color = Color.Lerp(gradStart, gradEnd, i / 49f); bI.raycastTarget = false;
                var bR = b.GetComponent<RectTransform>();
                bR.anchorMin = new Vector2(0, i / 50f); bR.anchorMax = new Vector2(1, (i + 1) / 50f);
                bR.offsetMin = bR.offsetMax = Vector2.zero;
            }
            var bgAnimI = new GameObject("BGAnim"); bgAnimI.transform.SetParent(bgGO.transform, false);
            var bgA = bgAnimI.AddComponent<Image>(); bgA.color = Color.white; bgA.raycastTarget = false;
            Stretch(bgA.GetComponent<RectTransform>());
            var mat = ShaderTimekeeper.GetMaterial();
            if (mat != null) bgA.material = mat;

            var blurGO = new GameObject("FPVBlur"); blurGO.transform.SetParent(bgGO.transform, false);
            blurGroup = blurGO.AddComponent<CanvasGroup>(); blurGroup.alpha = 0f;
            Stretch(blurGO.AddComponent<RectTransform>());
            var blurI = blurGO.AddComponent<Image>(); blurI.color = Color.white; blurI.raycastTarget = false;
            Stretch(blurI.GetComponent<RectTransform>());
            var fpvMat = ShaderTimekeeper.GetFPVMaterial();
            if (fpvMat != null) blurI.material = fpvMat;

            // ── HUD lateral con fade-in ───────────────────────────────────────
            var hudGO = new GameObject("HUD"); hudGO.transform.SetParent(cGO.transform, false);
            hudGroup = hudGO.AddComponent<CanvasGroup>(); hudGroup.alpha = 0f;
            Stretch(hudGO.AddComponent<RectTransform>());
            BuildHUDScale(hudGO.transform, -760f, true);
            BuildHUDScale(hudGO.transform,  760f, false);

            // ── Tarjeta central 920 × 700 ────────────────────────────────────
            const float PW = 920f, PH = 730f;
            var card = new GameObject("Window");
            card.transform.SetParent(cGO.transform, false);
            var cardImg = card.AddComponent<Image>(); cardImg.color = new Color(0.02f, 0.04f, 0.09f, 0.80f);
            AddOutline(cardImg, C_BORDER);
            var cardRt = card.GetComponent<RectTransform>();
            cardRt.anchorMin = cardRt.anchorMax = cardRt.pivot = new Vector2(0.5f, 0.5f);
            cardRt.sizeDelta = new Vector2(PW, PH);
            cardRt.anchoredPosition = Vector2.zero;

            AddDarkGradient(card.transform);
            BuildGlowBorder(cGO.transform, cardRt);

            // Barra decorativa superior
            {
                var tb = new GameObject("TopAccentBar"); tb.transform.SetParent(card.transform, false);
                var tI = tb.AddComponent<Image>(); tI.color = new Color(C_CYAN.r, C_CYAN.g, C_CYAN.b, 0.28f); tI.raycastTarget = false;
                var tR = tb.GetComponent<RectTransform>();
                tR.anchorMin = new Vector2(0f, 1f); tR.anchorMax = new Vector2(0.1f, 1f);
                tR.pivot = new Vector2(0f, 1f);
                tR.offsetMin = tR.offsetMax = Vector2.zero; tR.sizeDelta = new Vector2(0f, 4f);
            }

            // ── CABECERA ─────────────────────────────────────────────────────
            float hy = -24f;
            HdrLabel(card.transform, "SISTEMA AR·V1.0", hy, 9f, C_CYAN, 22f);
            hy -= 26f;
            var titleLbl = HdrLabel(card.transform, "CONFIGURACIÓN DEL JUEGO", hy, 17f, C_TEXT, 0f);
            var orbitronTitle = MapCreatorUI.GetOrbitronFontAsset();
            if (orbitronTitle != null) titleLbl.font = orbitronTitle;
            hy -= 36f;

            // ── COLUMNAS DE CONTENIDO ─────────────────────────────────────────
            const float LX = -195f, LW = 380f;
            const float RX =  225f, RW = 360f;


            // ── S1: TIPO DE ENEMIGO — dropdown desplegable ───────────────────
            SectionHdr(card.transform, "TIPO DE ENEMIGO", new Vector2(LX, 237f), LW);

            // Botón principal del dropdown
            var ddBtnGO = new GameObject("DD_Btn"); ddBtnGO.transform.SetParent(card.transform, false);
            var ddBtnImg = ddBtnGO.AddComponent<Image>(); ddBtnImg.color = C_SEL;
            var ddBtnRt = ddBtnGO.GetComponent<RectTransform>();
            ddBtnRt.anchorMin = ddBtnRt.anchorMax = ddBtnRt.pivot = new Vector2(0.5f, 0.5f);
            ddBtnRt.anchoredPosition = new Vector2(LX, 200f);
            ddBtnRt.sizeDelta = new Vector2(LW, 38f);

            // Texto de selección actual
            var ddCurGO = new GameObject("DD_Cur"); ddCurGO.transform.SetParent(ddBtnGO.transform, false);
            var ddCurTmp = ddCurGO.AddComponent<TextMeshProUGUI>();
            ddCurTmp.text = EnemyNames[0]; ddCurTmp.fontSize = 11f; ddCurTmp.fontStyle = FontStyles.Bold;
            ddCurTmp.color = C_CYAN; ddCurTmp.alignment = TextAlignmentOptions.Center;
            ddCurTmp.characterSpacing = 10f; ddCurTmp.raycastTarget = false;
            { var r = ddCurGO.GetComponent<RectTransform>();
              r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
              r.offsetMin = new Vector2(14f,0f); r.offsetMax = new Vector2(-30f,0f); }

            // Flecha ▼/▲
            var ddArwGO = new GameObject("DD_Arw"); ddArwGO.transform.SetParent(ddBtnGO.transform, false);
            var ddArwTmp = ddArwGO.AddComponent<TextMeshProUGUI>();
            ddArwTmp.text = "▼"; ddArwTmp.fontSize = 9f; ddArwTmp.fontStyle = FontStyles.Bold;
            ddArwTmp.color = C_SUB; ddArwTmp.alignment = TextAlignmentOptions.Right;
            ddArwTmp.raycastTarget = false;
            { var r = ddArwGO.GetComponent<RectTransform>();
              r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
              r.offsetMin = new Vector2(0f,0f); r.offsetMax = new Vector2(-10f,0f); }

            // Panel desplegable (tamaño se recalcula al abrir, inicialmente oculto)
            const float OPT_H = 40f;
            var ddPanGO = new GameObject("DD_Panel"); ddPanGO.transform.SetParent(card.transform, false);
            var ddPanImg2 = ddPanGO.AddComponent<Image>();
            ddPanImg2.color = new Color(0.04f, 0.08f, 0.18f, 0.97f);
            AddOutline(ddPanImg2, C_BORDER);
            var ddPanRt = ddPanGO.GetComponent<RectTransform>();
            ddPanRt.anchorMin = ddPanRt.anchorMax = ddPanRt.pivot = new Vector2(0.5f, 0.5f);
            ddPanGO.SetActive(false);

            // Construir todas las filas de opciones
            var ddOptGOs = new GameObject[EnemyNames.Length];
            for (int oi = 0; oi < EnemyNames.Length; oi++)
            {
                int captIdx = oi;
                var optGO = new GameObject("DD_Opt_" + oi); optGO.transform.SetParent(ddPanGO.transform, false);
                ddOptGOs[oi] = optGO;
                optGO.AddComponent<Image>().color = new Color(0f,0f,0f,0f);
                var optRt = optGO.GetComponent<RectTransform>();
                optRt.anchorMin = optRt.anchorMax = optRt.pivot = new Vector2(0.5f,0.5f);
                optRt.sizeDelta = new Vector2(LW, OPT_H);

                var optTxtGO = new GameObject("T"); optTxtGO.transform.SetParent(optGO.transform, false);
                var optTmp = optTxtGO.AddComponent<TextMeshProUGUI>();
                optTmp.text = EnemyNames[oi]; optTmp.fontSize = 11f; optTmp.fontStyle = FontStyles.Bold;
                optTmp.color = C_TEXT; optTmp.alignment = TextAlignmentOptions.Center;
                optTmp.characterSpacing = 10f; optTmp.raycastTarget = false;
                { var r = optTxtGO.GetComponent<RectTransform>();
                  r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
                  r.offsetMin = new Vector2(16f,0f); r.offsetMax = new Vector2(-16f,0f); }

                var optBtn = optGO.AddComponent<Button>();
                var optCb = optBtn.colors;
                optCb.normalColor      = new Color(0f,0f,0f,0f);
                optCb.highlightedColor = new Color(C_CYAN.r,C_CYAN.g,C_CYAN.b,0.10f);
                optCb.pressedColor     = new Color(C_CYAN.r,C_CYAN.g,C_CYAN.b,0.20f);
                optBtn.colors = optCb;
                optBtn.onClick.AddListener(() =>
                {
                    SelectEnemyType(captIdx);
                    ddCurTmp.text = EnemyNames[captIdx];
                    ddArwTmp.text = "▼";
                    ddPanGO.SetActive(false);
                });
            }

            // Refresca el panel antes de mostrarlo:
            // oculta el ítem ya seleccionado, redimensiona y reposiciona los visibles.
            System.Action refreshDD = () =>
            {
                int sel = ShooterSettings.EnemyTypeIndex;
                // Ocultar seleccionado, mostrar el resto
                for (int k = 0; k < ddOptGOs.Length; k++)
                    ddOptGOs[k].SetActive(k != sel);
                // Calcular alto según ítems visibles
                int visCount = ddOptGOs.Length - 1;  // siempre n-1
                float newH   = Mathf.Max(1, visCount) * OPT_H;
                ddPanRt.sizeDelta        = new Vector2(LW, newH);
                ddPanRt.anchoredPosition = new Vector2(LX, 200f - 19f - newH * 0.5f);
                // Reposicionar visibles de arriba a abajo
                int vi = 0;
                for (int k = 0; k < ddOptGOs.Length; k++)
                {
                    if (k == sel) continue;
                    float y = newH * 0.5f - OPT_H * 0.5f - vi * OPT_H;
                    ddOptGOs[k].GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, y);
                    vi++;
                }
            };

            // Botón principal: muestra/oculta el panel
            var ddBtn = ddBtnGO.AddComponent<Button>();
            { var cb = ddBtn.colors;
              cb.normalColor      = C_SEL;
              cb.highlightedColor = Color.Lerp(C_SEL, Color.white, 0.12f);
              cb.pressedColor     = Color.Lerp(C_SEL, Color.black, 0.15f);
              ddBtn.colors = cb; }
            ddBtn.onClick.AddListener(() =>
            {
                bool show = !ddPanGO.activeSelf;
                if (show) { refreshDD(); ddPanGO.SetActive(true); ddPanGO.transform.SetAsLastSibling(); }
                else      { ddPanGO.SetActive(false); }
                ddArwTmp.text = show ? "▲" : "▼";
            });

            // ── S2: ALTITUD DE VUELO ──────────────────────────────────────────
            SectionHdr(card.transform, "ALTITUD DE VUELO", new Vector2(LX, 145f), LW);
            (heightMinus, _) = OptBtn(card.transform, "◄", new Vector2(LX - 95f, 108f), new Vector2(44f, 38f));
            (heightPlus,  _) = OptBtn(card.transform, "►", new Vector2(LX + 95f, 108f), new Vector2(44f, 38f));
            heightMinus.onClick.AddListener(() => ChangeHeight(-1));
            heightPlus .onClick.AddListener(() => ChangeHeight(+1));

            // ── Campo de entrada manual de altitud ───────────────────────────
            var hvGO = new GameObject("HVal"); hvGO.transform.SetParent(card.transform, false);
            hvGO.AddComponent<Image>().color = C_DISAB;
            var hvR = hvGO.GetComponent<RectTransform>();
            hvR.anchorMin = hvR.anchorMax = hvR.pivot = new Vector2(0.5f, 0.5f);
            hvR.anchoredPosition = new Vector2(LX, 108f); hvR.sizeDelta = new Vector2(130f, 38f);

            // Viewport con máscara de recorte
            var taGO = new GameObject("TextArea"); taGO.transform.SetParent(hvGO.transform, false);
            taGO.AddComponent<RectMask2D>();
            var taRt = taGO.GetComponent<RectTransform>();
            taRt.anchorMin = Vector2.zero; taRt.anchorMax = Vector2.one;
            taRt.offsetMin = new Vector2(8f, 2f); taRt.offsetMax = new Vector2(-8f, -2f);

            // Placeholder
            var phGO = new GameObject("Placeholder"); phGO.transform.SetParent(taGO.transform, false);
            var phTmp = phGO.AddComponent<TextMeshProUGUI>();
            phTmp.text = "0.0"; phTmp.fontSize = 16f; phTmp.fontStyle = FontStyles.Italic;
            phTmp.color = new Color(C_SUB.r, C_SUB.g, C_SUB.b, 0.50f);
            phTmp.alignment = TextAlignmentOptions.Center; phTmp.raycastTarget = false;
            { var r = phGO.GetComponent<RectTransform>();
              r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
              r.offsetMin = r.offsetMax = Vector2.zero; }

            // Texto editable
            var itGO = new GameObject("Text"); itGO.transform.SetParent(taGO.transform, false);
            var itTmp = itGO.AddComponent<TextMeshProUGUI>();
            itTmp.fontSize = 16f; itTmp.fontStyle = FontStyles.Bold;
            itTmp.color = C_SUB; itTmp.alignment = TextAlignmentOptions.Center;
            { var r = itGO.GetComponent<RectTransform>();
              r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
              r.offsetMin = r.offsetMax = Vector2.zero; }

            // TMP_InputField
            heightInput = hvGO.AddComponent<TMP_InputField>();
            heightInput.textViewport   = taRt;
            heightInput.textComponent  = itTmp;
            heightInput.placeholder    = phTmp;
            heightInput.contentType    = TMP_InputField.ContentType.DecimalNumber;
            heightInput.characterLimit = 5;
            heightInput.customCaretColor = true;
            heightInput.caretColor     = C_ACCENT;
            heightInput.selectionColor = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.30f);
            // Transición al enfocar / desenfocar — igual que OptBtn
            { var cb = heightInput.colors;
              cb.normalColor      = C_DISAB;
              cb.highlightedColor = Color.Lerp(C_DISAB, Color.white, 0.12f);
              cb.selectedColor    = C_SEL;
              cb.pressedColor     = Color.Lerp(C_SEL, Color.white, 0.15f);
              heightInput.colors  = cb; }
            heightInput.onSelect  .AddListener(_ => itTmp.color = C_ACCENT);
            heightInput.onDeselect.AddListener(_ => itTmp.color = C_SUB);
            heightInput.onEndEdit.AddListener(val =>
            {
                // Acepta tanto coma como punto como separador decimal
                string norm = val.Replace(',', '.');
                if (float.TryParse(norm,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float parsed))
                {
                    float step    = Mathf.Round(parsed / HEIGHT_STEP) * HEIGHT_STEP;
                    float clamped = Mathf.Clamp(step, HEIGHT_MIN, HEIGHT_MAX);
                    ShooterSettings.EnemyHeight = clamped;
                    heightInput.text = $"{clamped:F1}";
                }
                else
                {
                    heightInput.text = $"{ShooterSettings.EnemyHeight:F1}";
                }
            });
            UpdateHeightLabel();

            // ── S3: DIFICULTAD ────────────────────────────────────────────────
            SectionHdr(card.transform, "DIFICULTAD", new Vector2(LX, 53f), LW);
            diffButtons = new Button[3];
            diffTmps    = new TextMeshProUGUI[3];
            string[] diffLabels = { "PACÍFICO", "FÁCIL", "DIFÍCIL" };
            const float diffBW = 110f, diffGap = 8f;
            float diffTotalW = 3 * diffBW + 2 * diffGap;
            float diffStart  = LX - diffTotalW * 0.5f + diffBW * 0.5f;
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                float bx = diffStart + i * (diffBW + diffGap);
                (diffButtons[i], diffTmps[i]) = OptBtn(card.transform,
                    diffLabels[i], new Vector2(bx, 16f), new Vector2(diffBW, 38f));
                diffButtons[i].onClick.AddListener(() => SelectDifficulty(idx));
            }

            // ── S4: BARRERAS ────────────────────────────────────────────────
            SectionHdr(card.transform, "BARRERAS", new Vector2(LX, -39f), LW);
            float rHalfSpan = 65f + 8f;
            (barriersYes, barriersYesTmp) = OptBtn(card.transform, "SÍ",
                new Vector2(LX - rHalfSpan, -76f), new Vector2(130f, 38f));
            (barriersNo, barriersNoTmp) = OptBtn(card.transform, "NO",
                new Vector2(LX + rHalfSpan, -76f), new Vector2(130f, 38f));
            barriersYes.onClick.AddListener(() => SelectBarriers(true));
            barriersNo .onClick.AddListener(() => SelectBarriers(false));

            // ── S5: TAMAÑO DE ENEMIGOS ────────────────────────────────────────
            SectionHdr(card.transform, "TAMAÑO DE ENEMIGOS", new Vector2(LX, -131f), LW);
            {
                const float SL_W      = 320f;
                const float HANDLE_SZ = 11f;
                float slCx   = LX - 12f;
                float slInset = HANDLE_SZ * 0.5f;

                // Raíz del slider
                var slRootGO = new GameObject("SizeSlider"); slRootGO.transform.SetParent(card.transform, false);
                var slRootRt = slRootGO.AddComponent<RectTransform>();
                slRootRt.anchorMin = slRootRt.anchorMax = slRootRt.pivot = new Vector2(0.5f, 0.5f);
                slRootRt.anchoredPosition = new Vector2(slCx, -168f);
                slRootRt.sizeDelta = new Vector2(SL_W, HANDLE_SZ + 6f);

                // Pista (fondo oscuro)
                var trackGO = new GameObject("Track"); trackGO.transform.SetParent(slRootGO.transform, false);
                var trackImg = trackGO.AddComponent<Image>();
                trackImg.color = new Color(0.08f, 0.12f, 0.24f, 1f); trackImg.raycastTarget = false;
                var trackRt = trackGO.GetComponent<RectTransform>();
                trackRt.anchorMin = new Vector2(0f, 0.35f); trackRt.anchorMax = new Vector2(1f, 0.65f);
                trackRt.offsetMin = trackRt.offsetMax = Vector2.zero;

                // Fill Area (inset = radio del handle)
                var faGO = new GameObject("FillArea"); faGO.transform.SetParent(slRootGO.transform, false);
                var faRt = faGO.AddComponent<RectTransform>();
                faRt.anchorMin = new Vector2(0f, 0.35f); faRt.anchorMax = new Vector2(1f, 0.65f);
                faRt.offsetMin = new Vector2(slInset, 0f); faRt.offsetMax = new Vector2(-slInset, 0f);

                // Fill (el Slider ajusta anchorMax.x con el valor normalizado)
                var fillGO = new GameObject("Fill"); fillGO.transform.SetParent(faGO.transform, false);
                var fillImg = fillGO.AddComponent<Image>(); fillImg.color = C_CYAN; fillImg.raycastTarget = false;
                var fillRt = fillGO.GetComponent<RectTransform>();
                fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = new Vector2(1f, 1f);
                fillRt.offsetMin = fillRt.offsetMax = Vector2.zero;

                // Handle Slide Area (mismo inset que Fill Area)
                var hsaGO = new GameObject("HandleSlideArea"); hsaGO.transform.SetParent(slRootGO.transform, false);
                var hsaRt = hsaGO.AddComponent<RectTransform>();
                hsaRt.anchorMin = Vector2.zero; hsaRt.anchorMax = Vector2.one;
                hsaRt.offsetMin = new Vector2(slInset, 0f); hsaRt.offsetMax = new Vector2(-slInset, 0f);

                // Handle (círculo pequeño)
                var handleGO = new GameObject("Handle"); handleGO.transform.SetParent(hsaGO.transform, false);
                var handleImg = handleGO.AddComponent<Image>(); handleImg.color = C_CYAN;
                var handleRt = handleGO.GetComponent<RectTransform>();
                handleRt.anchorMin = handleRt.anchorMax = new Vector2(0f, 0.5f);
                handleRt.pivot = new Vector2(0.5f, 0.5f);
                handleRt.sizeDelta = new Vector2(HANDLE_SZ, HANDLE_SZ);

                // Componente Slider (Image transparente = área de clic)
                var hitImg = slRootGO.AddComponent<Image>(); hitImg.color = Color.clear;
                var sizeSlider = slRootGO.AddComponent<Slider>();
                sizeSlider.fillRect      = fillRt;
                sizeSlider.handleRect    = handleRt;
                sizeSlider.direction     = Slider.Direction.LeftToRight;
                sizeSlider.minValue      = 1f;
                sizeSlider.maxValue      = 10f;
                sizeSlider.wholeNumbers  = true;
                sizeSlider.targetGraphic = hitImg;
                sizeSlider.value         = ShooterSettings.EnemySizeLevel;

                // Etiqueta de nivel actual (a la derecha del slider)
                var lblGO = new GameObject("SizeLbl"); lblGO.transform.SetParent(card.transform, false);
                var lblTmp = lblGO.AddComponent<TextMeshProUGUI>();
                lblTmp.text = ShooterSettings.EnemySizeLevel.ToString();
                lblTmp.fontSize = 13f; lblTmp.fontStyle = FontStyles.Bold;
                lblTmp.color = C_CYAN; lblTmp.alignment = TextAlignmentOptions.Center;
                lblTmp.raycastTarget = false;
                var lblRt = lblGO.GetComponent<RectTransform>();
                lblRt.anchorMin = lblRt.anchorMax = lblRt.pivot = new Vector2(0.5f, 0.5f);
                lblRt.anchoredPosition = new Vector2(slCx + SL_W * 0.5f + 24f, -168f);
                lblRt.sizeDelta = new Vector2(36f, 20f);

                sizeSlider.onValueChanged.AddListener(val =>
                {
                    ShooterSettings.EnemySizeLevel = Mathf.RoundToInt(val);
                    lblTmp.text = ShooterSettings.EnemySizeLevel.ToString();
                });

                // Marcas de los 10 niveles bajo la pista
                float tkStart = slCx - SL_W * 0.5f + slInset;
                float tkEnd   = slCx + SL_W * 0.5f - slInset;
                for (int tk = 0; tk < 10; tk++)
                {
                    float tkX = Mathf.Lerp(tkStart, tkEnd, tk / 9f);
                    var tkGO = new GameObject("Tk"); tkGO.transform.SetParent(card.transform, false);
                    var tkImg = tkGO.AddComponent<Image>();
                    tkImg.color = new Color(C_SUB.r, C_SUB.g, C_SUB.b, 0.5f); tkImg.raycastTarget = false;
                    var tkRt = tkGO.GetComponent<RectTransform>();
                    tkRt.anchorMin = tkRt.anchorMax = tkRt.pivot = new Vector2(0.5f, 0.5f);
                    tkRt.anchoredPosition = new Vector2(tkX, -186f);
                    tkRt.sizeDelta = new Vector2(1.5f, 6f);
                }
            }

            // ── PREVISUALIZACIÓN (columna derecha) ────────────────────────────
            SectionHdr(card.transform, "PREVISUALIZACIÓN ENEMIGO", new Vector2(RX, 237f), RW);

            const float PREV_W = 340f, PREV_H = 270f, PREV_CY = 85f;
            previewRT = new RenderTexture(512, 512, 16); previewRT.Create();

            var rawGO = new GameObject("PreviewRaw");
            rawGO.transform.SetParent(card.transform, false);
            var rawImg = rawGO.AddComponent<RawImage>();
            rawImg.texture = previewRT; rawImg.color = Color.white;
            var rawR = rawGO.GetComponent<RectTransform>();
            rawR.anchorMin = rawR.anchorMax = rawR.pivot = new Vector2(0.5f, 0.5f);
            rawR.anchoredPosition = new Vector2(RX, PREV_CY);
            rawR.sizeDelta = new Vector2(PREV_W, PREV_H);

            // Interacción ratón: arrastrar = rotar, scroll = zoom
            var dragger = rawGO.AddComponent<PreviewDragger>();
            dragger.onDrag = delta =>
            {
                previewDragging = true;
                previewYaw   += delta.x * 0.5f;
                previewPitch -= delta.y * 0.5f;
                previewPitch  = Mathf.Clamp(previewPitch, -80f, 80f);
            };
            dragger.onEndDrag = () => previewDragging = false;
            dragger.onScroll  = s =>
            {
                if (previewCam != null)
                    previewCam.fieldOfView = Mathf.Clamp(previewCam.fieldOfView - s * 6f, 8f, 70f);
            };

            BuildPreviewBorder(card.transform, new Vector2(RX, PREV_CY), new Vector2(PREV_W, PREV_H));

            // Nombre del modelo
            var pnGO = new GameObject("PrevName"); pnGO.transform.SetParent(card.transform, false);
            previewNameTmp = pnGO.AddComponent<TextMeshProUGUI>();
            previewNameTmp.fontSize = 11f; previewNameTmp.fontStyle = FontStyles.Bold;
            previewNameTmp.color = C_SUB; previewNameTmp.alignment = TextAlignmentOptions.Center;
            previewNameTmp.characterSpacing = 14f; previewNameTmp.raycastTarget = false;
            var pnR = pnGO.GetComponent<RectTransform>();
            pnR.anchorMin = pnR.anchorMax = pnR.pivot = new Vector2(0.5f, 0.5f);
            pnR.anchoredPosition = new Vector2(RX, PREV_CY - PREV_H * 0.5f - 14f);
            pnR.sizeDelta = new Vector2(RW, 16f);

            // Cámara de preview (capa 31, lejos del escenario)
            var camGO = new GameObject("PreviewCamera");
            SceneManager.MoveGameObjectToScene(camGO, gameObject.scene);
            previewCam = camGO.AddComponent<Camera>();
            previewCam.clearFlags      = CameraClearFlags.SolidColor;
            previewCam.backgroundColor = new Color(0.02f, 0.04f, 0.09f, 1f);
            previewCam.targetTexture   = previewRT;
            previewCam.cullingMask     = 1 << 31;
            previewCam.fieldOfView     = 55f;           // campo más amplio = menos zoom inicial
            camGO.transform.position   = new Vector3(9000f, 0f, -5f);
            camGO.transform.LookAt(new Vector3(9000f, 0f, 0f));

            // ── BOTONES INFERIORES: VOLVER  +  INICIAR MISIÓN ─────────────────
            // layout centrado: [─140─] [20] [────240────]  = 400px total
            var btnBack = MakeFillButton(card.transform, "← Volver",
                new Vector2(-130f, -278f), new Vector2(140f, 48f),
                C_BORDER, C_TEXT);
            btnBack.onClick.AddListener(GoBack);

            var btnStart = MakeFillButton(card.transform, "►  Iniciar Misión",
                new Vector2(80f, -278f), new Vector2(240f, 48f),
                C_ACCENT, new Color(0.03f, 0.05f, 0.09f, 1f));
            btnStart.onClick.AddListener(StartGame);

            // ── FPV Overlay ───────────────────────────────────────────────────
            var fpvGO = new GameObject("FPVTargetingOverlay");
            fpvGO.transform.SetParent(cGO.transform, false);
            fpvGO.AddComponent<FPVTargetingOverlay>().Init(cardRt);

            // ── Estado inicial ────────────────────────────────────────────────
            SelectEnemyType(0);    // carga el modelo del drone (incluye LoadPreviewModel)
            SelectDifficulty(1);
            SelectBarriers(true);
            yield return null;     // ceder un frame para que la UI se inicialice
        }

        // ─── Lógica de selección ──────────────────────────────────────────────
        void SelectEnemyType(int idx)
        {
            ShooterSettings.EnemyTypeIndex = idx;
            if (previewNameTmp) previewNameTmp.text = EnemyNames[idx];
            // Resetear cámara al estado inicial para que todos los modelos partan del mismo punto
            previewYaw   = 0f;
            previewPitch = 12f;
            if (previewCam != null) previewCam.fieldOfView = 55f;
            StartCoroutine(LoadPreviewModel(idx));
        }

        void SelectDifficulty(int idx)
        {
            ShooterSettings.Difficulty = idx;
            for (int i = 0; i < diffButtons.Length; i++)
                SetActive(diffButtons[i], diffTmps[i], i == idx);
        }

        void SelectBarriers(bool yes)
        {
            ShooterSettings.BarriersEnabled = yes;
            SetActive(barriersYes, barriersYesTmp,  yes);
            SetActive(barriersNo,  barriersNoTmp,  !yes);
        }

        void ChangeHeight(int delta)
        {
            float next = Mathf.Round((ShooterSettings.EnemyHeight + delta * HEIGHT_STEP) / HEIGHT_STEP) * HEIGHT_STEP;
            ShooterSettings.EnemyHeight = Mathf.Clamp(next, HEIGHT_MIN, HEIGHT_MAX);
            UpdateHeightLabel();
        }

        void UpdateHeightLabel()
        {
            if (heightInput != null)
                heightInput.text = $"{ShooterSettings.EnemyHeight:F1}";
        }

        // ─── Preview model ────────────────────────────────────────────────────
        IEnumerator LoadPreviewModel(int idx)
        {
            if (previewModel != null) { Destroy(previewModel); previewModel = null; }
            string path = ModelPaths[idx];
            if (!File.Exists(path)) yield break;

            var gltf = new GltfImport();
            var t1 = gltf.Load(path);
            yield return new WaitUntil(() => t1.IsCompleted);
            if (!t1.Result) yield break;

            previewModel = new GameObject("PreviewModel");
            SceneManager.MoveGameObjectToScene(previewModel, gameObject.scene);
            var t2 = gltf.InstantiateMainSceneAsync(previewModel.transform);
            yield return new WaitUntil(() => t2.IsCompleted);

            previewModel.transform.localScale = Vector3.one * PreviewScales[idx];
            previewModel.transform.position   = new Vector3(9000f, 0f, 0f) + PreviewOffsets[idx];
            SetLayerRecursive(previewModel, 31);

            // Centrar el pivot en el centro geométrico del modelo
            var renderers = previewModel.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                Vector3 centerOffset = bounds.center - previewModel.transform.position;
                foreach (Transform child in previewModel.transform)
                    child.position -= centerOffset;
            }
        }

        static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform ch in go.transform) SetLayerRecursive(ch.gameObject, layer);
        }

        // ─── Iniciar juego ────────────────────────────────────────────────────
        void StartGame()
        {
            ShooterSettings.GameStarted = true;
            CleanupPreview();

            if (!string.IsNullOrEmpty(TargetScene))
            {
                string scene = TargetScene;
                TargetScene   = "";
                ReturnAction  = null;
                SceneManager.LoadScene(scene);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        // ─── Volver ───────────────────────────────────────────────────────────
        void GoBack()
        {
            CleanupPreview();
            TargetScene = "";

            if (ReturnAction != null)
            {
                var action = ReturnAction;
                ReturnAction = null;
                Destroy(gameObject);
                action();                     // restaura el menú de origen
            }
            else
            {
                // Fallback: acceso directo desde la escena shooter → volver al menú
                SceneManager.LoadScene("menu");
            }
        }

        void CleanupPreview()
        {
            if (previewCam   != null) { Destroy(previewCam.gameObject); previewCam = null; }
            if (previewModel != null) { Destroy(previewModel);          previewModel = null; }
            if (previewRT    != null) { previewRT.Release();             previewRT = null; }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // HELPER: DRAGGER DEL PREVIEW (componente interno)
        // ═══════════════════════════════════════════════════════════════════════
        class PreviewDragger : MonoBehaviour,
            IPointerDownHandler, IDragHandler, IPointerUpHandler, IScrollHandler
        {
            public System.Action<Vector2> onDrag;
            public System.Action          onEndDrag;
            public System.Action<float>   onScroll;

            public void OnPointerDown(PointerEventData e) { /* nada, captura el click */ }
            public void OnDrag      (PointerEventData e)  { onDrag?.Invoke(e.delta); }
            public void OnPointerUp (PointerEventData e)  { onEndDrag?.Invoke(); }
            public void OnScroll    (PointerEventData e)  { onScroll?.Invoke(e.scrollDelta.y); }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // HELPERS DE HUD (idénticos a Menú)
        // ═══════════════════════════════════════════════════════════════════════
        void BuildHUDScale(Transform parent, float xPos, bool isLeft)
        {
            const float yStep = 56f, uStep = 5f, clipH = yStep * 12f;
            float railOff = isLeft ? 52f : -52f, railX = xPos + railOff;
            float majW = 30f, minW = 16f, tickDir = isLeft ? 1f : -1f;
            TextAlignmentOptions ta = isLeft ? TextAlignmentOptions.Right : TextAlignmentOptions.Left;
            float labelX = isLeft ? xPos - 4f : xPos + 4f;

            HUDBar(parent, new Vector2(railX, 0f), new Vector2(2.5f, clipH), 0.06f);

            var tGO = new GameObject("TR_" + (isLeft ? "L" : "R"));
            tGO.transform.SetParent(parent, false);
            var tRt = tGO.AddComponent<RectTransform>();
            tRt.anchorMin = tRt.anchorMax = tRt.pivot = new Vector2(0.5f, 0.5f);
            tRt.anchoredPosition = Vector2.zero; tRt.sizeDelta = Vector2.zero;
            if (isLeft) scaleLeftTicks = tRt; else scaleRightTicks = tRt;

            for (int i = -7; i <= 7; i++)
            {
                int val = i * (int)uStep; float yy = i * yStep;
                bool maj = val % 20 == 0; float tW = maj ? majW : minW;
                HUDBar(tRt, new Vector2(railX + tickDir * tW * 0.5f, yy), new Vector2(tW, maj ? 2.5f : 2f), 0.06f);
                if (maj)
                {
                    var l = HUDLbl(tRt, val.ToString(), new Vector2(labelX, yy), ta,
                        val == 0 ? 16f : 13f, val == 0 ? 0.10f : 0.07f);
                    l.gameObject.name = "ScaleTick_" + (isLeft ? "L" : "R") + "_" + val;
                }
            }

            HUDBar(parent, new Vector2(railX + tickDir * (majW + 4f), 0f), new Vector2(majW + 8f, 3f), 0.06f);
            float vx = isLeft ? railX + 120f : railX - 120f;
            var vl = HUDTelLbl(parent, new Vector2(vx, 0f),
                isLeft ? "ALT\n--.-" : "VEL\n0.0", new Vector2(140f, 48f));
            if (isLeft) altText = vl; else velText = vl;
        }

        void UpdateScaleLabels(RectTransform root, string side, float center, int step)
        {
            for (int i = -7; i <= 7; i++)
            {
                var ch = root.Find("ScaleTick_" + side + "_" + (i * step));
                if (ch == null) continue;
                var t = ch.GetComponent<TextMeshProUGUI>();
                if (t) t.text = (Mathf.RoundToInt(center) + i * step).ToString();
            }
        }

        void HUDBar(Transform p, Vector2 pos, Vector2 sz, float alpha)
        {
            var go = new GameObject("HB"); go.transform.SetParent(p, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(C_CYAN.r, C_CYAN.g, C_CYAN.b, alpha); img.raycastTarget = false;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = sz;
        }

        TextMeshProUGUI HUDLbl(Transform p, string text, Vector2 pos,
            TextAlignmentOptions align, float size, float alpha)
        {
            var go = new GameObject("HL"); go.transform.SetParent(p, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text; t.fontSize = size;
            t.color = new Color(C_CYAN.r, C_CYAN.g, C_CYAN.b, alpha);
            t.fontStyle = FontStyles.Bold; t.alignment = align; t.raycastTarget = false;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(90f, 26f);
            return t;
        }

        TextMeshProUGUI HUDTelLbl(Transform p, Vector2 pos, string text, Vector2 sz)
        {
            var go = new GameObject("HT"); go.transform.SetParent(p, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text; t.fontSize = 15f;
            t.color = new Color(C_CYAN.r, C_CYAN.g, C_CYAN.b, 0.11f);
            t.fontStyle = FontStyles.Bold; t.alignment = TextAlignmentOptions.Center;
            t.characterSpacing = 12f; t.raycastTarget = false;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = sz;
            return t;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // HELPERS DE TARJETA
        // ═══════════════════════════════════════════════════════════════════════

        TextMeshProUGUI HdrLabel(Transform p, string text, float y, float fontSize, Color color, float spacing)
        {
            var go = new GameObject("Lbl"); go.transform.SetParent(p, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text; t.fontSize = fontSize; t.color = color;
            t.fontStyle = FontStyles.Bold; t.alignment = TextAlignmentOptions.Center;
            t.characterSpacing = spacing; t.raycastTarget = false;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, y);
            rt.sizeDelta = new Vector2(880f, 30f);
            return t;
        }

        void HdrDivider(Transform p, float y, float width)
        {
            var go = new GameObject("Div"); go.transform.SetParent(p, false);
            var img = go.AddComponent<Image>(); img.color = C_BORDER; img.raycastTarget = false;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, y);
            rt.sizeDelta = new Vector2(width, 1f);
        }

        /// Cabecera de sección: solo texto centrado, sin fondo ni bordes.
        void SectionHdr(Transform p, string text, Vector2 pos, float width)
        {
            const float H = 22f;
            var lGO = new GameObject("SH_Txt"); lGO.transform.SetParent(p, false);
            var lt = lGO.AddComponent<TextMeshProUGUI>();
            lt.text = text; lt.fontSize = 11f;
            lt.color = new Color(C_SUB.r, C_SUB.g, C_SUB.b, 0.70f);
            lt.fontStyle = FontStyles.Bold; lt.alignment = TextAlignmentOptions.Center;
            lt.characterSpacing = 20f; lt.raycastTarget = false;
            var lR = lGO.GetComponent<RectTransform>();
            lR.anchorMin = lR.anchorMax = lR.pivot = new Vector2(0.5f, 0.5f);
            lR.anchoredPosition = pos; lR.sizeDelta = new Vector2(width, H);
        }

        (Button btn, TextMeshProUGUI tmp) OptBtn(Transform p, string label, Vector2 pos, Vector2 sz)
        {
            var go = new GameObject("OB"); go.transform.SetParent(p, false);
            var img = go.AddComponent<Image>(); img.color = C_DISAB;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = sz;

            var btn = go.AddComponent<Button>();
            var cb = btn.colors;
            cb.normalColor      = C_DISAB;
            cb.highlightedColor = Color.Lerp(C_DISAB, Color.white, 0.15f);
            cb.pressedColor     = Color.Lerp(C_DISAB, Color.black, 0.15f);
            btn.colors = cb;

            var tGO = new GameObject("L"); tGO.transform.SetParent(go.transform, false);
            var tmp = tGO.AddComponent<TextMeshProUGUI>();
            tmp.text = label; tmp.fontSize = 11f; tmp.fontStyle = FontStyles.Bold;
            tmp.color = C_SUB; tmp.alignment = TextAlignmentOptions.Center;
            tmp.characterSpacing = 10f; tmp.raycastTarget = false;
            var tRt = tGO.GetComponent<RectTransform>();
            tRt.anchorMin = Vector2.zero; tRt.anchorMax = Vector2.one;
            tRt.offsetMin = tRt.offsetMax = Vector2.zero;

            return (btn, tmp);
        }

        void SetActive(Button btn, TextMeshProUGUI tmp, bool on)
        {
            if (btn == null) return;
            Color fill = on ? C_SEL  : C_DISAB;
            Color text = on ? C_CYAN : C_SUB;
            var img = btn.GetComponent<Image>();
            if (img) img.color = fill;
            if (tmp) tmp.color = text;
            var cb = btn.colors;
            cb.normalColor      = fill;
            cb.highlightedColor = Color.Lerp(fill, Color.white, 0.20f);
            cb.pressedColor     = Color.Lerp(fill, Color.black, 0.20f);
            btn.colors = cb;
        }

        Button MakeFillButton(Transform p, string label, Vector2 pos, Vector2 sz, Color fill, Color textCol)
        {
            var go = new GameObject("Btn_" + label); go.transform.SetParent(p, false);
            var img = go.AddComponent<Image>(); img.color = fill;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = sz;

            var tGO = new GameObject("T"); tGO.transform.SetParent(go.transform, false);
            var t = tGO.AddComponent<TextMeshProUGUI>();
            t.text = label; t.fontSize = 14f; t.fontStyle = FontStyles.Bold;
            t.color = textCol; t.alignment = TextAlignmentOptions.Center; t.raycastTarget = false;
            var tRt = tGO.GetComponent<RectTransform>();
            tRt.anchorMin = Vector2.zero; tRt.anchorMax = Vector2.one;
            tRt.offsetMin = tRt.offsetMax = Vector2.zero;

            var btn = go.AddComponent<Button>();
            var cb = btn.colors;
            cb.normalColor      = fill;
            cb.highlightedColor = Color.Lerp(fill, Color.white, 0.20f);
            cb.pressedColor     = Color.Lerp(fill, Color.black, 0.20f);
            btn.colors = cb;
            return btn;
        }

        void Rect2D(Transform p, Vector2 pos, Vector2 sz, Color color)
        {
            var go = new GameObject("R"); go.transform.SetParent(p, false);
            var img = go.AddComponent<Image>(); img.color = color; img.raycastTarget = false;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = sz;
        }

        void AddDarkGradient(Transform p)
        {
            for (int i = 0; i < 10; i++)
            {
                float yMin = i / 10f, yMax = (i + 1) / 10f;
                float alpha = Mathf.Lerp(0.88f, 0f, i / 9f);
                var b = new GameObject("GD" + i); b.transform.SetParent(p, false);
                b.transform.SetAsFirstSibling();
                var bI = b.AddComponent<Image>(); bI.color = new Color(0f, 0f, 0f, alpha); bI.raycastTarget = false;
                var bR = b.GetComponent<RectTransform>();
                bR.anchorMin = new Vector2(0f, yMin); bR.anchorMax = new Vector2(1f, yMax);
                bR.offsetMin = bR.offsetMax = Vector2.zero;
            }
        }

        void AddOutline(Component c, Color color)
        {
            var o = c.gameObject.AddComponent<Outline>();
            o.effectColor = color; o.effectDistance = new Vector2(1f, -1f);
        }

        void BuildGlowBorder(Transform canvasT, RectTransform card)
        {
            float w = card.sizeDelta.x, h = card.sizeDelta.y, t = 2.5f;
            Color gc = new Color(C_CYAN.r, C_CYAN.g, C_CYAN.b, 0.28f);
            (Vector2 pos, Vector2 sz)[] sides = {
                (new Vector2(0f,  h/2f), new Vector2(w, t)),
                (new Vector2(0f, -h/2f), new Vector2(w, t)),
                (new Vector2(-w/2f, 0f), new Vector2(t, h)),
                (new Vector2( w/2f, 0f), new Vector2(t, h)),
            };
            foreach (var s in sides)
            {
                var go = new GameObject("GB"); go.transform.SetParent(canvasT, false);
                var img = go.AddComponent<Image>(); img.color = gc; img.raycastTarget = false;
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = s.pos; rt.sizeDelta = s.sz;
            }
        }

        void BuildPreviewBorder(Transform p, Vector2 center, Vector2 sz)
        {
            Color c = new Color(C_BORDER.r, C_BORDER.g, C_BORDER.b, 1f);
            float hw = sz.x / 2f, hh = sz.y / 2f;
            Rect2D(p, new Vector2(center.x,        center.y + hh), new Vector2(sz.x + 2f, 1.5f), c);
            Rect2D(p, new Vector2(center.x,        center.y - hh), new Vector2(sz.x + 2f, 1.5f), c);
            Rect2D(p, new Vector2(center.x - hw,   center.y),      new Vector2(1.5f, sz.y + 2f), c);
            Rect2D(p, new Vector2(center.x + hw,   center.y),      new Vector2(1.5f, sz.y + 2f), c);
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
