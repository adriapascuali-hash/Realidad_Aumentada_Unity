using System;
using System.Collections;
using System.Collections.Generic;
using System.IO.Ports;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using ARDashboard;

public class Configuración : MonoBehaviour
{
    [Header("Referencia (opcional — se busca automáticamente)")]
    public VideoBackground videoController;

    // ── Colores ───────────────────────────────────────────────────────────────
    static readonly Color C_BG     = new Color(0.04f, 0.05f, 0.09f, 0.97f);
    static readonly Color C_CARD   = new Color(0.07f, 0.09f, 0.14f, 1f);
    static readonly Color C_ACCENT = new Color(0.20f, 0.85f, 0.65f, 1f);
    static readonly Color C_BORDER = new Color(0.16f, 0.22f, 0.32f, 1f);
    static readonly Color C_TEXT   = new Color(0.88f, 0.93f, 1.00f, 1f);
    static readonly Color C_SUB    = new Color(0.42f, 0.52f, 0.66f, 1f);
    static readonly Color C_MQTT   = new Color(0.14f, 0.58f, 0.96f, 1f);
    static readonly Color C_CAM    = new Color(0.08f, 0.28f, 0.20f, 1f);
    static readonly Color C_SITL   = new Color(0.14f, 0.58f, 0.96f, 1f);
    static readonly Color C_ANT    = new Color(0.85f, 0.55f, 0.15f, 1f);
    static readonly Color C_DISAB  = new Color(0.18f, 0.22f, 0.30f, 1f);
    static readonly Color C_GRAD_A = new Color(0.00f, 0.08f, 0.18f, 1f);
    static readonly Color C_GRAD_B = new Color(0.05f, 0.00f, 0.14f, 1f);

    // ── Colores FPV ───────────────────────────────────────────────────────────
    static readonly Color C_CYAN   = new Color(0.00f, 0.95f, 1.00f, 1f);
    static readonly Color C_WARN   = new Color(1.00f, 0.82f, 0.00f, 1f);

    // ── Estado UI ─────────────────────────────────────────────────────────────
    VideoSource selectedSource = VideoSource.LocalWebcam;
    GameConfig.DroneConnectionType selectedDrone = GameConfig.DroneConnectionType.SITL;

    GameObject camSelectorRoot, camButtonsContainer;
    int selectedCamIndex = 0;
    List<Button> camButtons = new List<Button>();
    string[] camNames = new string[0];

    GameObject comSelectorRoot, comButtonsContainer;
    int selectedComIndex = 0;
    List<Button> comButtons = new List<Button>();
    string[] comPortNames = new string[0];

    Button btnSitl, btnAntenna;
    GameObject sitlSection, antennaSection;
    TMP_InputField inputSitlHost, inputSitlPort;

    // ── Dimensiones del card ──────────────────────────────────────────────────
    float pw    = 400f;
    float cardW = 440f;
    float cardH = 690f;

    // ── Parámetros botones fuente ─────────────────────────────────────────────
    float srcCardW   = 160f;
    float srcCardGap = 80f;

    CanvasGroup overlayGroup;
    bool fadingOut = false;
    float fadeTimer = 0f;
    const float FADE_DURATION = 0.45f;

    // ── Fondo animado ─────────────────────────────────────────────────────────
    Image gradTopImg, gradBotImg;
    float bgTime = 0f;


    // ── Fade-in FPV y HUD ────────────────────────────────────────────────────
    CanvasGroup fpvGroup;
    CanvasGroup hudGroup;
    CanvasGroup blurGroup;
    float fpvFadeTime  = 0f;
    float hudFadeTime  = 0f;
    float blurFadeTime = 0f;
    const float FPV_FADE_DURATION = 0.4f;
    const float HUD_FADE_DURATION = 1.0f;

    // ── Estado HUD ────────────────────────────────────────────────────────────
    float hudTime = 0f;
    float lockTimer = 0f;
    bool  locked = false;
    const float LOCK_DURATION = 2.8f;

    // Referencias FPV animadas
    RectTransform cornerTL, cornerTR, cornerBL, cornerBR;
    TextMeshProUGUI lockText, distText, statusText;
    CanvasGroup lockIconGroup;
    RectTransform targetContainer;

    // Referencias escalas
    RectTransform scaleLeftTicks, scaleRightTicks;
    TextMeshProUGUI altText, velText;
    float scaleLeftOff = 0f, scaleRightOff = 0f;

    // ─────────────────────────────────────────────────────────────────────────
    void Awake()
    {
        if (videoController == null)
            videoController = FindAnyObjectByType<VideoBackground>();
        Time.timeScale = 0f;
    }

    void Start()
    {
        ShaderTimekeeper.ScannerEnabled = true;
        // If the canvas was generated in the Editor (via the context menu
        // below) to preview it without pressing Play, remove it first so
        // Play always rebuilds a fresh one — runtime behavior never depends
        // on whatever was left over from editing.
        RemoveExistingCanvas();
        BuildUI();
    }

    void RemoveExistingCanvas()
    {
        var existing = transform.Find("Configuración");
        if (existing == null) return;
        if (Application.isPlaying) Destroy(existing.gameObject);
        else DestroyImmediate(existing.gameObject);
    }

#if UNITY_EDITOR
    // Right-click the Configuración component in the Inspector (without
    // pressing Play) to materialize the canvas into the Hierarchy, so you
    // can see/tweak it directly. It's the exact same BuildUI() the game
    // uses at runtime — no separate/duplicated layout code to keep in sync.
    [ContextMenu("Generar Canvas en el Editor (sin Play)")]
    void GenerateCanvasInEditor()
    {
        if (Application.isPlaying)
        {
            Debug.LogWarning("[Configuración] Esto es solo para usarlo en el Editor sin pulsar Play.");
            return;
        }
        RemoveExistingCanvas();
        BuildUI();
        UnityEditor.EditorUtility.SetDirty(gameObject);
    }
#endif

    void Update()
    {
        if (fadingOut)
        {
            fadeTimer += Time.unscaledDeltaTime;
            overlayGroup.alpha = Mathf.Clamp01(1f - fadeTimer / FADE_DURATION);
            if (fadeTimer >= FADE_DURATION) Destroy(gameObject);
            return;
        }

        // ── Fondo animado ─────────────────────────────────────────────────────
        bgTime += Time.unscaledDeltaTime;
        if (gradTopImg != null && gradBotImg != null)
        {
            float t = (Mathf.Sin(bgTime * 0.4f) + 1f) * 0.5f;
            Color topColor = Color.Lerp(C_GRAD_A, C_GRAD_B, t);
            Color botColor = Color.Lerp(C_GRAD_B, C_GRAD_A, t);
            gradTopImg.color = new Color(topColor.r, topColor.g, topColor.b, 0.92f);
            gradBotImg.color = new Color(botColor.r, botColor.g, botColor.b, 0.92f);
        }


        // ── Fade-in FPV ───────────────────────────────────────────────────────
        if (fpvGroup != null && fpvGroup.alpha < 1f)
        {
            fpvFadeTime += Time.unscaledDeltaTime;
            fpvGroup.alpha = Mathf.Clamp01(fpvFadeTime / FPV_FADE_DURATION);
        }

        // ── Fade-in blur overlay ──────────────────────────────────────────────
        if (blurGroup != null && blurGroup.alpha < 1f)
        {
            blurFadeTime += Time.unscaledDeltaTime;
            blurGroup.alpha = Mathf.Clamp01(blurFadeTime / FPV_FADE_DURATION);
        }

        // ── Fade-in HUD barras ────────────────────────────────────────────────
        if (hudGroup != null && hudGroup.alpha < 1f)
        {
            hudFadeTime += Time.unscaledDeltaTime;
            hudGroup.alpha = Mathf.Clamp01(hudFadeTime / HUD_FADE_DURATION);
        }

        hudTime   += Time.unscaledDeltaTime;
        lockTimer += Time.unscaledDeltaTime;
        float lockP = Mathf.Clamp01(lockTimer / LOCK_DURATION);

        if (!locked && lockP >= 1f)
        {
            locked = true;
            if (lockText) { lockText.text = "TARGET LOCKED"; lockText.color = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.10f); }
            if (lockIconGroup) { lockIconGroup.gameObject.SetActive(true); lockIconGroup.alpha = 0.10f; }
        }
        else if (!locked && lockText)
        {
            float blink = Mathf.Sin(hudTime * 9f) > 0f ? 0.10f : 0.04f;
            lockText.color = new Color(C_WARN.r, C_WARN.g, C_WARN.b, blink);
            int dots = (int)(hudTime * 2.5f) % 4;
            lockText.text = "ACQUIRING TARGET" + new string('.', dots);
            if (lockIconGroup) lockIconGroup.gameObject.SetActive(false);
        }
        else if (locked && lockText)
        {
            float p = (Mathf.Sin(hudTime * 2f) + 1f) * 0.5f;
            float a = Mathf.Lerp(0.07f, 0.12f, p);
            lockText.color = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, a);
            if (lockIconGroup) lockIconGroup.alpha = a;
        }

        float alt  = 28.7f + Mathf.Sin(hudTime * 0.4f)  * 0.6f;
        float vel  = Mathf.Abs(Mathf.Sin(hudTime * 1.2f) * 2.3f);
        float dist = 12.4f + Mathf.Sin(hudTime * 0.7f)  * 1.8f;

        if (distText) distText.text = $"DIST: {dist:F1} m";
        if (altText)  altText.text  = $"ALT\n{alt:F1}";
        if (velText)  velText.text  = $"VEL\n{vel:F1}";

        if (statusText)
        {
            float sb = Mathf.Sin(hudTime * 3f) > 0.4f ? 1f : 0.45f;
            statusText.color = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, sb);
        }

        float unitsStep = 5f;
        float yStep     = 56f;
        float pxPerUnit = yStep / unitsStep;

        float altSnap = Mathf.Round(alt / unitsStep) * unitsStep;
        float velSnap = Mathf.Round(vel / unitsStep) * unitsStep;
        float altFrac = -(alt - altSnap) * pxPerUnit;
        float velFrac = -(vel - velSnap) * pxPerUnit;

        scaleLeftOff  = Mathf.Lerp(scaleLeftOff,  altFrac, Time.unscaledDeltaTime * 6f);
        scaleRightOff = Mathf.Lerp(scaleRightOff, velFrac, Time.unscaledDeltaTime * 6f);

        if (scaleLeftTicks)
        {
            var p = scaleLeftTicks.anchoredPosition;
            p.y = scaleLeftOff;
            scaleLeftTicks.anchoredPosition = p;
            UpdateScaleLabels(scaleLeftTicks, "L", altSnap, (int)unitsStep);
        }
        if (scaleRightTicks)
        {
            var p = scaleRightTicks.anchoredPosition;
            p.y = scaleRightOff;
            scaleRightTicks.anchoredPosition = p;
            UpdateScaleLabels(scaleRightTicks, "R", velSnap, (int)unitsStep);
        }

        float breath = Mathf.Sin(hudTime * 1.4f) * 4f;
        float cx = cardW * 0.5f + 38f + breath;
        float cy = cardH * 0.5f + 38f + breath;

        if (cornerTL) cornerTL.anchoredPosition = new Vector2(-cx,  cy);
        if (cornerTR) cornerTR.anchoredPosition = new Vector2( cx,  cy);
        if (cornerBL) cornerBL.anchoredPosition = new Vector2(-cx, -cy);
        if (cornerBR) cornerBR.anchoredPosition = new Vector2( cx, -cy);

        if (targetContainer)
        {
            if (!locked)
            {
                float driftMag = (1f - lockP) * 10f;
                float dx = Mathf.Sin(hudTime * 0.9f) * driftMag;
                float dy = Mathf.Cos(hudTime * 1.1f) * driftMag;
                targetContainer.anchoredPosition = Vector2.Lerp(
                    targetContainer.anchoredPosition,
                    new Vector2(dx, dy), Time.unscaledDeltaTime * 4f);
            }
            else
            {
                targetContainer.anchoredPosition = Vector2.Lerp(
                    targetContainer.anchoredPosition,
                    Vector2.zero, Time.unscaledDeltaTime * 6f);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BUILD UI
    // ─────────────────────────────────────────────────────────────────────────
    void BuildUI()
    {
        var canvasGO = new GameObject("StartMenuCanvas");
        canvasGO.transform.SetParent(transform);
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 999;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGO.AddComponent<GraphicRaycaster>();

        overlayGroup = canvasGO.AddComponent<CanvasGroup>();

        // ── Fondo idéntico al ScenarioSelector ───────────────────────────────
        var bgContainerGO = new GameObject("BG_Container");
        bgContainerGO.transform.SetParent(canvasGO.transform, false);
        var bgContainerRt = bgContainerGO.AddComponent<RectTransform>();
        bgContainerRt.anchorMin = Vector2.zero;
        bgContainerRt.anchorMax = Vector2.one;
        bgContainerRt.offsetMin = bgContainerRt.offsetMax = Vector2.zero;

        var bgBase = MakeImage(bgContainerGO.transform, "BG", new Color(0.02f, 0.03f, 0.06f, 1f));
        Stretch(bgBase); bgBase.raycastTarget = false;

        var gradTopGO = new GameObject("GradTop");
        gradTopGO.transform.SetParent(bgContainerGO.transform, false);
        gradTopImg = gradTopGO.AddComponent<Image>();
        gradTopImg.color = C_GRAD_A; gradTopImg.raycastTarget = false;
        var gtRt = gradTopGO.GetComponent<RectTransform>();
        gtRt.anchorMin = new Vector2(0f, 0.5f); gtRt.anchorMax = new Vector2(1f, 1f);
        gtRt.offsetMin = gtRt.offsetMax = Vector2.zero;

        var gradBotGO = new GameObject("GradBot");
        gradBotGO.transform.SetParent(bgContainerGO.transform, false);
        gradBotImg = gradBotGO.AddComponent<Image>();
        gradBotImg.color = C_GRAD_B; gradBotImg.raycastTarget = false;
        var gbRt = gradBotGO.GetComponent<RectTransform>();
        gbRt.anchorMin = new Vector2(0f, 0f); gbRt.anchorMax = new Vector2(1f, 0.5f);
        gbRt.offsetMin = gbRt.offsetMax = Vector2.zero;

        var bgAnim = MakeImage(bgContainerGO.transform, "BGAnim", Color.white);
        Stretch(bgAnim);
        var mat = ShaderTimekeeper.GetMaterial();
        if (mat != null) bgAnim.material = mat;
        bgAnim.raycastTarget = false;

        // ── Estática TV encima del fondo animado, debajo de los elementos UI ──
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
        var fpvOverlayMat = ShaderTimekeeper.GetFPVMaterial();
        if (fpvOverlayMat != null) staticOverlay.material = fpvOverlayMat;
        staticOverlay.raycastTarget = false;

        // ── Contenedor HUD barras laterales con fade ──────────────────────────
        var hudContainerGO = new GameObject("HUD_Container");
        hudContainerGO.transform.SetParent(canvasGO.transform, false);
        hudGroup = hudContainerGO.AddComponent<CanvasGroup>();
        hudGroup.alpha = 0f;
        var hudRt = hudContainerGO.AddComponent<RectTransform>();
        hudRt.anchorMin = Vector2.zero; hudRt.anchorMax = Vector2.one;
        hudRt.offsetMin = hudRt.offsetMax = Vector2.zero;

        BuildFPVBackgroundHUD(hudContainerGO.transform);

        // ── Card principal (semitransparente como ScenarioSelector) ───────────
        var card = MakeImage(canvasGO.transform, "Card", new Color(0.02f, 0.04f, 0.09f, 0.80f));
        var cardRt = card.GetComponent<RectTransform>();
        cardRt.anchorMin = cardRt.anchorMax = new Vector2(0.5f, 0.5f);
        cardRt.pivot = new Vector2(0.5f, 0.5f);
        cardRt.sizeDelta = new Vector2(cardW, cardH);
        cardRt.anchoredPosition = Vector2.zero;
        AddOutline(card, C_BORDER);

        // ── Degradado negro escalonado: más oscuro abajo, más claro arriba (10 bandas)
        const int darkBands = 10;
        for (int i = 0; i < darkBands; i++)
        {
            float yMin  = i / (float)darkBands;
            float yMax  = (i + 1) / (float)darkBands;
            float alpha = Mathf.Lerp(0.88f, 0.00f, i / (float)(darkBands - 1));
            var bandGO  = new GameObject("CardGradDark" + i);
            bandGO.transform.SetParent(card.transform, false);
            bandGO.transform.SetAsFirstSibling();
            var bandImg = bandGO.AddComponent<Image>();
            bandImg.color = new Color(0f, 0f, 0f, alpha);
            bandImg.raycastTarget = false;
            var bRt = bandGO.GetComponent<RectTransform>();
            bRt.anchorMin = new Vector2(0f, yMin);
            bRt.anchorMax = new Vector2(1f, yMax);
            bRt.offsetMin = bRt.offsetMax = Vector2.zero;
        }

        BuildCardGlowBorder(canvasGO.transform, cardRt);
        BuildFPVTargetingOverlay(canvasGO.transform, cardRt);

        // Barra decorativa superior
        var bar = MakeImage(card.transform, "Bar", new Color(C_CYAN.r, C_CYAN.g, C_CYAN.b, 0.28f));
        var barRt = bar.GetComponent<RectTransform>();
        barRt.anchorMin = new Vector2(0, 1); barRt.anchorMax = new Vector2(0.1f, 1);
        barRt.pivot = new Vector2(0, 1);
        barRt.offsetMin = barRt.offsetMax = Vector2.zero;
        barRt.sizeDelta = new Vector2(0, 4f);

        // ── Contenido ─────────────────────────────────────────────────────────
        float y = -30f;

        MakeLabel(card.transform, "SISTEMA AR·V1.0", new Vector2(0, y), 8,
            C_CYAN, FontStyles.Bold, TextAlignmentOptions.Center, new Vector2(pw, 13), 22); y -= 18f;
        var sessionTitleLbl = MakeLabel(card.transform, "CONFIGURACIÓN DE MISIÓN", new Vector2(0, y), 19,
            C_TEXT, FontStyles.Bold, TextAlignmentOptions.Center, new Vector2(pw, 30)); y -= 60f;
        var orbitronSession = MapCreatorUI.GetOrbitronFontAsset();
        if (orbitronSession != null) sessionTitleLbl.font = orbitronSession;

        var droneConnLbl = MakeLabel(card.transform, "CONEXIÓN AL DRON", new Vector2(0, y), 12,
            C_SUB, FontStyles.Bold, TextAlignmentOptions.Center, new Vector2(pw, 18), 20); y -= 44f;
        if (orbitronSession != null) droneConnLbl.font = orbitronSession;

        var droneRow = MakeContainer(card.transform, new Vector2(0, y), new Vector2(pw, 58)); y -= 80f;
        btnSitl    = MakeSourceCard(droneRow.transform, "SITL",   "Simulador Mission Planner", C_SITL, new Vector2(-srcCardGap, 0), srcCardW);
        btnAntenna = MakeSourceCard(droneRow.transform, "Antena", "Telemetría USB / serie",    C_ANT,  new Vector2( srcCardGap, 0), srcCardW);
        btnSitl.onClick.AddListener(() => SelectDroneSource(GameConfig.DroneConnectionType.SITL));
        btnAntenna.onClick.AddListener(() => SelectDroneSource(GameConfig.DroneConnectionType.Antenna));

        sitlSection = MakeContainer(card.transform, new Vector2(0, y), new Vector2(pw, 36));
        inputSitlHost = MakeInputField(sitlSection.transform, "Host TCP", new Vector2(-srcCardGap, 0), srcCardW, "127.0.0.1");
        inputSitlPort = MakeInputField(sitlSection.transform, "Puerto",   new Vector2( srcCardGap, 0), srcCardW, "5763");
        // No editables: csDronLink.Dron.Conectar() ignora ambos valores en modo
        // simulación — el host queda hardcodeado a "127.0.0.1" y el puerto se
        // calcula con su propia fórmula interna (ver GameConfig.SitlHost/SitlPort)
        // — así que dejar que el usuario los cambie solo genera una expectativa falsa.
        inputSitlHost.interactable = false;
        inputSitlPort.interactable = false;

        antennaSection = MakeContainer(card.transform, new Vector2(0, y), new Vector2(pw, 100));
        var comHeaderRow = MakeContainer(antennaSection.transform, new Vector2(0, -14), new Vector2(pw, 18));
        MakeLabel(comHeaderRow.transform, "PUERTO SERIE", new Vector2(0, 0), 8,
            C_ANT, FontStyles.Bold, TextAlignmentOptions.Center, new Vector2(pw, 12), 20);
        comButtonsContainer = MakeContainer(antennaSection.transform, new Vector2(-9, -54), new Vector2(pw - 28, 42));
        var btnRefCom = MakeSmallButton(antennaSection.transform, "↻", new Vector2((pw / 2) - 16, -25), 18);
        SetupRefreshIcon(btnRefCom);
        btnRefCom.onClick.AddListener(RefreshComButtons);
        y -= 100f;

        y -= 16f;

        camSelectorRoot = MakeContainer(card.transform, new Vector2(0, y), new Vector2(pw, 96));
        var camHeaderRow = MakeContainer(camSelectorRoot.transform, new Vector2(0, 0), new Vector2(pw, 18));
        var camHeaderLbl = MakeLabel(camHeaderRow.transform, "DISPOSITIVOS LOCALES DE CÁMARA", new Vector2(0, 0), 12,
            C_SUB, FontStyles.Bold, TextAlignmentOptions.Center, new Vector2(pw, 18), 20);
        if (orbitronSession != null) camHeaderLbl.font = orbitronSession;
        camButtonsContainer = MakeContainer(camSelectorRoot.transform, new Vector2(-9, -58), new Vector2(pw - 28, 42));
        var btnRefCam = MakeSmallButton(camSelectorRoot.transform, "↻", new Vector2((pw / 2) - 16, -31), 18);
        SetupRefreshIcon(btnRefCam);
        btnRefCam.onClick.AddListener(() => StartCoroutine(RefreshWebcamDelayed()));
        y -= 150f;

        y -= 16f;

        if (SelectedScenario.HasSelection)
        {
            BuildScenarioBadge(card.transform, SelectedScenario.Current, y);
            y -= 50f;
            y -= 20f;
        }

        var bottomRow = MakeContainer(card.transform, new Vector2(0, y), new Vector2(pw, 44));

        var btnBack = MakeBackButton(bottomRow.transform, new Vector2(-pw / 2f + 105f, 0), new Vector2(112f, 44f));
        btnBack.onClick.AddListener(() =>
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene("Menú");
        });

        var btnPlay = MakePlayButton(bottomRow.transform, new Vector2(pw / 2f - 96f, 0), new Vector2(178f, 44f));
        btnPlay.onClick.AddListener(OnPlay);

        SelectVideoSource(VideoSource.LocalWebcam);
        SelectDroneSource(GameConfig.DroneConnectionType.SITL);

        StartCoroutine(RefreshWebcamDelayed());
        RefreshComButtons();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BADGE ESCENARIO SELECCIONADO
    // ─────────────────────────────────────────────────────────────────────────
    void BuildScenarioBadge(Transform parent, ScenarioData scenario, float yPos)
    {
        MakeLabel(parent, "MISIÓN SELECCIONADA", new Vector2(0f, yPos + 14f),
            9f, C_SUB, FontStyles.Bold, TextAlignmentOptions.Center,
            new Vector2(pw, 15f), 18f);
        MakeLabel(parent, scenario.name, new Vector2(0f, yPos - 3f),
            13.5f, C_CYAN, FontStyles.Bold, TextAlignmentOptions.Center,
            new Vector2(pw, 24f));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HUD DE FONDO
    // ─────────────────────────────────────────────────────────────────────────
    void BuildFPVBackgroundHUD(Transform canvasT)
    {
        BuildHUDScale(canvasT, -760f, 0f, true);
        BuildHUDScale(canvasT,  760f, 0f, false);
    }

    void BuildHUDScale(Transform parent, float xPos, float yCenter, bool isLeft)
    {
        float yStep      = 56f;
        float unitsStep  = 5f;
        float railOffset = isLeft ? 52f : -52f;
        float railX      = xPos + railOffset;
        float majorTickW = 30f;
        float minorTickW = 16f;
        float tickDir    = isLeft ? 1f : -1f;
        float railThk    = 2.5f;
        int   numMarks   = 11;
        float clipH      = yStep * (numMarks + 1);

        TextAlignmentOptions taNum = isLeft ? TextAlignmentOptions.Right : TextAlignmentOptions.Left;
        float labelNumX = isLeft ? xPos - 4f : xPos + 4f;

        MakeHUDBar(parent, new Vector2(railX, yCenter),
            new Vector2(railThk, clipH), C_CYAN, 0.06f);

        var tickGO = new GameObject("HUD_TickRoot_" + (isLeft ? "L" : "R"));
        tickGO.transform.SetParent(parent, false);
        var tickRT = tickGO.AddComponent<RectTransform>();
        tickRT.anchorMin = tickRT.anchorMax = new Vector2(0.5f, 0.5f);
        tickRT.pivot = new Vector2(0.5f, 0.5f);
        tickRT.anchoredPosition = new Vector2(0f, yCenter);
        tickRT.sizeDelta = Vector2.zero;

        if (isLeft) scaleLeftTicks  = tickRT;
        else        scaleRightTicks = tickRT;

        int half = numMarks / 2;
        for (int i = -half - 2; i <= half + 2; i++)
        {
            int   val    = i * (int)unitsStep;
            float y      = i * yStep;
            bool  isMaj  = val % 20 == 0;
            float tW     = isMaj ? majorTickW : minorTickW;
            float tAlpha = 0.06f;
            float tThk   = isMaj ? 2.5f : 2f;

            MakeHUDBar(tickRT, new Vector2(railX + tickDir * tW * 0.5f, y),
                new Vector2(tW, tThk), C_CYAN, tAlpha);

            if (isMaj)
            {
                var lbl = MakeHUDScaleLabelRef(tickRT, val.ToString(),
                    new Vector2(labelNumX, y), taNum,
                    val == 0 ? 16f : 13f, val == 0 ? 0.10f : 0.07f);
                lbl.gameObject.name = "ScaleTick_" + (isLeft ? "L" : "R") + "_" + val;
            }
        }

        MakeHUDBar(parent, new Vector2(railX + tickDir * (majorTickW + 4f), yCenter),
            new Vector2(majorTickW + 8f, 3f), C_CYAN, 0.06f);

        float lblX = isLeft ? railX + 120f : railX - 120f;
        string initTxt = isLeft ? "ALT\n--.-" : "VEL\n0.0";
        var valLbl = MakeTelLabel(parent,
            new Vector2(lblX, yCenter),
            TextAlignmentOptions.Center, initTxt, new Vector2(140f, 48f), 15f);

        if (isLeft) altText = valLbl;
        else        velText = valLbl;
    }

    void UpdateScaleLabels(RectTransform tickRoot, string side, float centerVal, int step)
    {
        int half = 7;
        for (int i = -half; i <= half; i++)
        {
            int    tickVal = i * step;
            int    realVal = Mathf.RoundToInt(centerVal) + tickVal;
            string name    = "ScaleTick_" + side + "_" + tickVal;
            var child = tickRoot.Find(name);
            if (child != null)
            {
                var tmp = child.GetComponent<TextMeshProUGUI>();
                if (tmp != null) tmp.text = realVal.ToString();
            }
        }
    }

    void BuildFPVTargetingOverlay(Transform canvasT, RectTransform cardRef)
    {
        var fpvContainerGO = new GameObject("FPV_Container");
        fpvContainerGO.transform.SetParent(canvasT, false);
        fpvGroup = fpvContainerGO.AddComponent<CanvasGroup>();
        fpvGroup.alpha = 0f;
        var fpvRt = fpvContainerGO.AddComponent<RectTransform>();
        fpvRt.anchorMin = Vector2.zero; fpvRt.anchorMax = Vector2.one;
        fpvRt.offsetMin = fpvRt.offsetMax = Vector2.zero;

        var cGO = new GameObject("FPV_Target");
        cGO.transform.SetParent(fpvContainerGO.transform, false);
        targetContainer = cGO.AddComponent<RectTransform>();
        targetContainer.anchorMin = targetContainer.anchorMax = new Vector2(0.5f, 0.5f);
        targetContainer.pivot     = new Vector2(0.5f, 0.5f);
        targetContainer.sizeDelta = Vector2.zero;
        targetContainer.anchoredPosition = Vector2.zero;

        float cxOuter = cardW * 0.5f + 38f;
        float cyOuter = cardH * 0.5f + 38f;
        float lenOuter = 70f;
        float thkOuter = 5f;

        cornerTL = MakeFPVCorner(targetContainer, "TL", new Vector2(-cxOuter,  cyOuter), lenOuter, thkOuter,  1f, -1f);
        cornerTR = MakeFPVCorner(targetContainer, "TR", new Vector2( cxOuter,  cyOuter), lenOuter, thkOuter, -1f, -1f);
        cornerBL = MakeFPVCorner(targetContainer, "BL", new Vector2(-cxOuter, -cyOuter), lenOuter, thkOuter,  1f,  1f);
        cornerBR = MakeFPVCorner(targetContainer, "BR", new Vector2( cxOuter, -cyOuter), lenOuter, thkOuter, -1f,  1f);

        float tx = 24f;
        MakeHUDBar(targetContainer, new Vector2(-cxOuter + tx,               cyOuter - thkOuter * 0.5f), new Vector2(1.5f, 16f), C_CYAN, 0.06f);
        MakeHUDBar(targetContainer, new Vector2(-cxOuter + thkOuter * 0.5f,  cyOuter - tx             ), new Vector2(16f, 1.5f), C_CYAN, 0.06f);
        MakeHUDBar(targetContainer, new Vector2( cxOuter - tx,               cyOuter - thkOuter * 0.5f), new Vector2(1.5f, 16f), C_CYAN, 0.06f);
        MakeHUDBar(targetContainer, new Vector2( cxOuter - thkOuter * 0.5f,  cyOuter - tx             ), new Vector2(16f, 1.5f), C_CYAN, 0.06f);
        MakeHUDBar(targetContainer, new Vector2(-cxOuter + tx,              -cyOuter + thkOuter * 0.5f), new Vector2(1.5f, 16f), C_CYAN, 0.06f);
        MakeHUDBar(targetContainer, new Vector2(-cxOuter + thkOuter * 0.5f, -cyOuter + tx             ), new Vector2(16f, 1.5f), C_CYAN, 0.06f);
        MakeHUDBar(targetContainer, new Vector2( cxOuter - tx,              -cyOuter + thkOuter * 0.5f), new Vector2(1.5f, 16f), C_CYAN, 0.06f);
        MakeHUDBar(targetContainer, new Vector2( cxOuter - thkOuter * 0.5f, -cyOuter + tx             ), new Vector2(16f, 1.5f), C_CYAN, 0.06f);

        float midBarLen = 22f;
        float midBarThk = 3.5f;
        MakeHUDBar(targetContainer, new Vector2(0f,  cyOuter),                         new Vector2(midBarLen * 2f, midBarThk), C_CYAN, 0.06f);
        MakeHUDBar(targetContainer, new Vector2(0f,  cyOuter - midBarLen * 0.5f - 2f), new Vector2(midBarThk, midBarLen),      C_CYAN, 0.06f);
        MakeHUDBar(targetContainer, new Vector2(0f, -cyOuter),                         new Vector2(midBarLen * 2f, midBarThk), C_CYAN, 0.06f);
        MakeHUDBar(targetContainer, new Vector2(0f, -cyOuter + midBarLen * 0.5f + 2f), new Vector2(midBarThk, midBarLen),      C_CYAN, 0.06f);
        MakeHUDBar(targetContainer, new Vector2(-cxOuter, 0f),                         new Vector2(midBarThk, midBarLen * 2f), C_CYAN, 0.06f);
        MakeHUDBar(targetContainer, new Vector2(-cxOuter + midBarLen * 0.5f + 2f, 0f), new Vector2(midBarLen, midBarThk),      C_CYAN, 0.06f);
        MakeHUDBar(targetContainer, new Vector2( cxOuter, 0f),                         new Vector2(midBarThk, midBarLen * 2f), C_CYAN, 0.06f);
        MakeHUDBar(targetContainer, new Vector2( cxOuter - midBarLen * 0.5f - 2f, 0f), new Vector2(midBarLen, midBarThk),      C_CYAN, 0.06f);

        float gx = cxOuter + 20f;
        float gy = cyOuter + 20f;
        MakeHUDBar(targetContainer, new Vector2(0f,  gy), new Vector2(60f, 1.5f), C_CYAN, 0.06f);
        MakeHUDBar(targetContainer, new Vector2(0f, -gy), new Vector2(60f, 1.5f), C_CYAN, 0.06f);
        MakeHUDBar(targetContainer, new Vector2(-gx, 0f), new Vector2(1.5f, 60f), C_CYAN, 0.06f);
        MakeHUDBar(targetContainer, new Vector2( gx, 0f), new Vector2(1.5f, 60f), C_CYAN, 0.06f);

        distText = MakeTelLabel(fpvContainerGO.transform,
            new Vector2(0f, cardH * 0.5f + 38f + 68f),
            TextAlignmentOptions.Center, "DIST: --.- m", new Vector2(280f, 26f), 16f);

        var lgGO = new GameObject("FPV_LockText");
        lgGO.transform.SetParent(fpvContainerGO.transform, false);
        lockText = lgGO.AddComponent<TextMeshProUGUI>();
        lockText.text             = "ACQUIRING TARGET";
        lockText.fontSize         = 12f;
        lockText.fontStyle        = FontStyles.Bold;
        lockText.color            = C_WARN;
        lockText.alignment        = TextAlignmentOptions.Center;
        lockText.characterSpacing = 24f;
        lockText.raycastTarget    = false;
        var lgRt = lgGO.GetComponent<RectTransform>();
        lgRt.anchorMin = lgRt.anchorMax = new Vector2(0.5f, 0.5f);
        lgRt.pivot = new Vector2(0.5f, 0.5f);
        lgRt.anchoredPosition = new Vector2(10f, cardH * 0.5f + 38f + 42f);
        lgRt.sizeDelta = new Vector2(360f, 22f);

        // Icono de mira (círculo + cruz + punto), igual que el del shooter,
        // en vez del carácter "◈" que la fuente no soporta (salía como □).
        lockIconGroup = BuildLockIcon(fpvContainerGO.transform,
            new Vector2(-88f, cardH * 0.5f + 38f + 42f), C_ACCENT);
        lockIconGroup.gameObject.SetActive(false);

        var stGO = new GameObject("FPV_Status");
        stGO.transform.SetParent(fpvContainerGO.transform, false);
        statusText = stGO.AddComponent<TextMeshProUGUI>();
        statusText.fontSize         = 10f;
        statusText.fontStyle        = FontStyles.Bold;
        statusText.color            = C_ACCENT;
        statusText.alignment        = TextAlignmentOptions.Right;
        statusText.characterSpacing = 14f;
        statusText.raycastTarget    = false;
        var stRt = stGO.GetComponent<RectTransform>();
        stRt.anchorMin = stRt.anchorMax = new Vector2(0.5f, 0.5f);
        stRt.pivot = new Vector2(0.5f, 0.5f);
        stRt.anchoredPosition = new Vector2(cardW * 0.5f + 38f - 8f, cardH * 0.5f + 38f + 60f);
        stRt.sizeDelta = new Vector2(220f, 18f);
    }

    // Mismo icono de mira (círculo segmentado + 4 marcas + punto central) que
    // dibuja ShooterController.cs junto a "TARGET LOCKED" en el juego real.
    // Envuelto en un CanvasGroup para poder desvanecerlo igual que el texto.
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

    void BuildCardGlowBorder(Transform canvasT, RectTransform cardRef)
    {
        float w  = cardRef.sizeDelta.x;
        float h  = cardRef.sizeDelta.y;
        float t  = 2.5f;
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
            img.color = gc;
            img.raycastTarget = false;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = positions[i];
            rt.sizeDelta = sizes[i];
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CÁMARAS
    // ─────────────────────────────────────────────────────────────────────────
    IEnumerator RefreshWebcamDelayed()
    {
        yield return null;
        yield return null;
        RefreshWebcamButtons();
    }

    void RefreshWebcamButtons()
    {
        foreach (Transform child in camButtonsContainer.transform) Destroy(child.gameObject);
        camButtons.Clear();
        camNames = LocalWebcamCapture.GetDeviceNames();
        if (camNames.Length == 0)
        {
            MakeLabel(camButtonsContainer.transform, "— Sin dispositivos —",
                Vector2.zero, 9, C_SUB, FontStyles.Normal, TextAlignmentOptions.Center, new Vector2(pw, 30));
            return;
        }
        BuildButtonRow(camButtonsContainer.transform, camNames, camButtons, C_CAM, idx => SelectCamera(idx));
        selectedCamIndex = 0;
        UpdateButtonRowColors(camButtons, selectedCamIndex, C_CAM);
    }

    void SelectCamera(int index)
    {
        selectedCamIndex = index;
        UpdateButtonRowColors(camButtons, selectedCamIndex, C_CAM);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUERTOS COM
    // ─────────────────────────────────────────────────────────────────────────
    void RefreshComButtons()
    {
        foreach (Transform child in comButtonsContainer.transform) Destroy(child.gameObject);
        comButtons.Clear();
        try { comPortNames = SerialPort.GetPortNames(); }
        catch { comPortNames = new string[0]; }
        if (comPortNames.Length == 0)
        {
            MakeLabel(comButtonsContainer.transform, "— Sin puertos COM —",
                Vector2.zero, 9, C_SUB, FontStyles.Normal, TextAlignmentOptions.Center, new Vector2(pw, 30));
            return;
        }
        BuildButtonRow(comButtonsContainer.transform, comPortNames, comButtons, C_ANT, idx => SelectComPort(idx));
        selectedComIndex = 0;
        UpdateButtonRowColors(comButtons, selectedComIndex, C_ANT);
    }

    void SelectComPort(int index)
    {
        selectedComIndex = index;
        UpdateButtonRowColors(comButtons, selectedComIndex, C_ANT);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HELPERS: FILA DE BOTONES
    // ─────────────────────────────────────────────────────────────────────────
    void BuildButtonRow(Transform parent, string[] names, List<Button> list,
        Color activeColor, System.Action<int> onSelect)
    {
        float btnW   = Mathf.Min(160f, (pw - 8f * (names.Length - 1)) / names.Length);
        float totalW = btnW * names.Length + 8f * (names.Length - 1);
        float startX = -totalW / 2f + btnW / 2f;
        for (int i = 0; i < names.Length; i++)
        {
            int idx = i;
            string shortName = names[i].Length > 18 ? names[i].Substring(0, 16) + "…" : names[i];
            var btn = MakeRowButton(parent, shortName,
                new Vector2(startX + i * (btnW + 8f), 0), btnW, activeColor);
            btn.onClick.AddListener(() => onSelect(idx));
            list.Add(btn);
        }
    }

    Button MakeRowButton(Transform parent, string label, Vector2 pos, float width, Color activeColor)
    {
        var go  = new GameObject("RowBtn");
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>(); img.color = C_DISAB;
        var rt  = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(width, 42);
        AddStaircaseGradient(go.transform);

        var txtGO = new GameObject("Lbl"); txtGO.transform.SetParent(go.transform, false);
        var t = txtGO.AddComponent<TextMeshProUGUI>();
        t.text = label; t.fontSize = 8.5f; t.color = C_SUB;
        t.alignment = TextAlignmentOptions.Center;
        t.fontStyle = FontStyles.Bold;
        t.textWrappingMode = TMPro.TextWrappingModes.Normal;
        var tRt = txtGO.GetComponent<RectTransform>();
        tRt.anchorMin = Vector2.zero; tRt.anchorMax = Vector2.one;
        tRt.offsetMin = new Vector2(4, 2); tRt.offsetMax = new Vector2(-4, -2);

        var btn = go.AddComponent<Button>();
        var cb = btn.colors;
        cb.normalColor      = C_DISAB;
        cb.highlightedColor = Color.Lerp(activeColor, C_DISAB, 0.5f);
        cb.pressedColor     = activeColor;
        btn.colors = cb;
        return btn;
    }

    void UpdateButtonRowColors(List<Button> list, int selectedIdx, Color activeColor)
    {
        for (int i = 0; i < list.Count; i++)
        {
            var img = list[i].GetComponent<Image>();
            if (img != null) img.color = (i == selectedIdx) ? activeColor : C_DISAB;
            foreach (var lbl in list[i].GetComponentsInChildren<TextMeshProUGUI>())
                lbl.color = (i == selectedIdx) ? C_TEXT : C_SUB;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SELECCIÓN DE FUENTES
    // ─────────────────────────────────────────────────────────────────────────
    void SelectVideoSource(VideoSource src)
    {
        selectedSource = src;
        if (camSelectorRoot != null) camSelectorRoot.SetActive(src == VideoSource.LocalWebcam);
    }

    void SelectDroneSource(GameConfig.DroneConnectionType src)
    {
        selectedDrone = src;
        SetCardSelected(btnSitl,    src == GameConfig.DroneConnectionType.SITL,    C_SITL);
        SetCardSelected(btnAntenna, src == GameConfig.DroneConnectionType.Antenna, C_ANT);
        if (sitlSection    != null) sitlSection.SetActive(src == GameConfig.DroneConnectionType.SITL);
        if (antennaSection != null) antennaSection.SetActive(src == GameConfig.DroneConnectionType.Antenna);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ON PLAY
    // ─────────────────────────────────────────────────────────────────────────
    void OnPlay()
    {
        GameConfig.SelectedSource    = selectedSource;
        GameConfig.WebcamDeviceIndex = selectedCamIndex;
        GameConfig.DroneConnection   = selectedDrone;

        if (selectedDrone == GameConfig.DroneConnectionType.SITL)
        {
            GameConfig.SitlHost = inputSitlHost != null ? inputSitlHost.text.Trim() : "127.0.0.1";
            if (int.TryParse(inputSitlPort != null ? inputSitlPort.text : "5762", out int port))
                GameConfig.SitlPort = port;
        }
        else
        {
            GameConfig.SerialPort = (selectedComIndex < comPortNames.Length)
                ? comPortNames[selectedComIndex] : "";
        }

        Time.timeScale = 1f;

        ShaderTimekeeper.ScannerEnabled = false;

        if (SelectedScenario.HasSelection &&
            SelectedScenario.Current.sceneName == "MapScene")
        {
            RuntimeSceneLoader.Launch(SelectedScenario.Current);
            return;
        }

        string sceneToLoad = "SampleScene";
        if (SelectedScenario.HasSelection &&
            !string.IsNullOrEmpty(SelectedScenario.Current.sceneName))
        {
            sceneToLoad = SelectedScenario.Current.sceneName;
        }

        // Si el destino es el shooter, mostrar primero el menú de configuración
        // en esta misma escena; el config menu cargará "shooter" al confirmar.
        if (sceneToLoad == "shooter")
        {
            ShooterSettings.Reset();
            ARDashboard.ShooterConfigMenu.TargetScene = "shooter";

            // Al pulsar Volver en el config menu, restaurar este menú
            ARDashboard.ShooterConfigMenu.ReturnAction = () =>
            {
                overlayGroup.alpha          = 1f;
                overlayGroup.blocksRaycasts = true;
                overlayGroup.interactable   = true;
            };

            // Ocultar este menú sin destruirlo (la carga de escena lo eliminará si confirma)
            overlayGroup.alpha          = 0f;
            overlayGroup.blocksRaycasts = false;
            overlayGroup.interactable   = false;

            var cmGO = new GameObject("[ShooterConfigMenu]");
            cmGO.AddComponent<ARDashboard.ShooterConfigMenu>();
            return;
        }

        SceneManager.LoadScene(sceneToLoad);
    }

    void SetCardSelected(Button btn, bool selected, Color activeColor)
    {
        if (btn == null) return;
        var img = btn.GetComponent<Image>();
        if (img != null) img.color = selected ? activeColor : C_DISAB;
        foreach (var lbl in btn.GetComponentsInChildren<TextMeshProUGUI>())
            lbl.color = selected ? C_TEXT : C_SUB;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HELPERS HUD
    // ─────────────────────────────────────────────────────────────────────────
    RectTransform MakeFPVCorner(RectTransform parent, string name,
        Vector2 pos, float len, float thk, float hDir, float vDir)
    {
        var go = new GameObject("FPV_Corner_" + name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = Vector2.zero;
        MakeHUDBar(go.transform, new Vector2(hDir * len * 0.5f, 0), new Vector2(len, thk), C_CYAN, 0.06f);
        MakeHUDBar(go.transform, new Vector2(0, vDir * len * 0.5f), new Vector2(thk, len), C_CYAN, 0.06f);
        return rt;
    }

    void MakeHUDBar(Transform parent, Vector2 pos, Vector2 size, Color col, float alpha)
    {
        var go  = new GameObject("HUD_Bar");
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = new Color(col.r, col.g, col.b, alpha);
        img.raycastTarget = false;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    void MakeHUDLabel(Transform parent, string text, Vector2 pos,
        TextAlignmentOptions align, float size, Color col)
    {
        var go  = new GameObject("HUD_Lbl");
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text; tmp.fontSize = size; tmp.color = col;
        tmp.fontStyle = FontStyles.Bold; tmp.alignment = align;
        tmp.raycastTarget = false;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(240f, 26f);
    }

    void MakeHUDScaleLabel(Transform parent, string text, Vector2 pos,
        TextAlignmentOptions align, float size, float alpha)
    {
        var go  = new GameObject("HUD_ScaleLbl");
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text; tmp.fontSize = size;
        tmp.color = new Color(C_CYAN.r, C_CYAN.g, C_CYAN.b, alpha);
        tmp.fontStyle = FontStyles.Bold; tmp.alignment = align;
        tmp.raycastTarget = false;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(90f, 26f);
    }

    TextMeshProUGUI MakeHUDScaleLabelRef(Transform parent, string text, Vector2 pos,
        TextAlignmentOptions align, float size, float alpha)
    {
        var go  = new GameObject("HUD_ScaleLbl");
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text; tmp.fontSize = size;
        tmp.color = new Color(C_CYAN.r, C_CYAN.g, C_CYAN.b, alpha);
        tmp.fontStyle = FontStyles.Bold; tmp.alignment = align;
        tmp.raycastTarget = false;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(90f, 26f);
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
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return tmp;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HELPERS UI
    // ─────────────────────────────────────────────────────────────────────────
    Image MakeImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        return img;
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
        Color color, FontStyles style, TextAlignmentOptions align, Vector2 sizeDelta, float letterSpacing = 0)
    {
        var go = new GameObject("Lbl");
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text; tmp.fontSize = size; tmp.color = color;
        tmp.fontStyle = style; tmp.alignment = align; tmp.characterSpacing = letterSpacing;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = pos; rt.sizeDelta = sizeDelta;
        return tmp;
    }

    void AddStaircaseGradient(Transform parent)
    {
        const int bands = 10;
        for (int i = 0; i < bands; i++)
        {
            float yMin  = i / (float)bands;
            float yMax  = (i + 1) / (float)bands;
            float alpha = Mathf.Lerp(0.88f, 0.00f, i / (float)(bands - 1));
            var bandGO  = new GameObject("Grad" + i);
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

    void MakeDivider(Transform parent, float y, float width)
    {
        var go = new GameObject("Divider");
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>(); img.color = C_BORDER;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0, y); rt.sizeDelta = new Vector2(width, 1);
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

    Button MakeSourceCard(Transform parent, string title, string subtitle, Color color, Vector2 pos, float width = 200f)
    {
        var go = new GameObject("Card_" + title);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>(); img.color = C_DISAB;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(width, 58);

        var titleGO = new GameObject("Title"); titleGO.transform.SetParent(go.transform, false);
        var titleTmp = titleGO.AddComponent<TextMeshProUGUI>();
        titleTmp.text = title; titleTmp.fontSize = 13; titleTmp.fontStyle = FontStyles.Bold;
        titleTmp.color = C_SUB; titleTmp.alignment = TextAlignmentOptions.Center;
        var titleRt = titleGO.GetComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0, 0.5f); titleRt.anchorMax = new Vector2(1, 1f);
        titleRt.offsetMin = titleRt.offsetMax = Vector2.zero;

        var subGO = new GameObject("Sub"); subGO.transform.SetParent(go.transform, false);
        var subTmp = subGO.AddComponent<TextMeshProUGUI>();
        subTmp.text = subtitle; subTmp.fontSize = 7f;
        subTmp.color = C_SUB; subTmp.alignment = TextAlignmentOptions.Center;
        subTmp.textWrappingMode = TMPro.TextWrappingModes.Normal;
        var subRt = subGO.GetComponent<RectTransform>();
        subRt.anchorMin = new Vector2(0, 0); subRt.anchorMax = new Vector2(1, 0.5f);
        subRt.offsetMin = new Vector2(4, 2); subRt.offsetMax = new Vector2(-4, -2);

        var btn = go.AddComponent<Button>();
        var cb = btn.colors;
        cb.normalColor      = C_DISAB;
        cb.highlightedColor = Color.Lerp(color, C_DISAB, 0.5f);
        cb.pressedColor     = color;
        btn.colors = cb;
        return btn;
    }

    TMP_InputField MakeInputField(Transform parent, string placeholder, Vector2 pos, float width, string defaultVal = "")
    {
        var go = new GameObject("Input_" + placeholder);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>(); img.color = Color.clear;

        var textArea = new GameObject("TextArea"); textArea.transform.SetParent(go.transform, false);
        var taRt = textArea.AddComponent<RectTransform>();
        taRt.anchorMin = Vector2.zero; taRt.anchorMax = Vector2.one;
        taRt.offsetMin = new Vector2(6, 2); taRt.offsetMax = new Vector2(-6, -2);
        textArea.AddComponent<RectMask2D>();

        var inputText = new GameObject("Text"); inputText.transform.SetParent(textArea.transform, false);
        var itTmp = inputText.AddComponent<TextMeshProUGUI>();
        itTmp.fontSize = 10; itTmp.color = C_TEXT; itTmp.alignment = TextAlignmentOptions.Center;
        var itRt = inputText.GetComponent<RectTransform>();
        itRt.anchorMin = Vector2.zero; itRt.anchorMax = Vector2.one;
        itRt.offsetMin = itRt.offsetMax = Vector2.zero;

        var ph = new GameObject("PH"); ph.transform.SetParent(textArea.transform, false);
        var phTmp = ph.AddComponent<TextMeshProUGUI>();
        phTmp.text = placeholder; phTmp.fontSize = 10;
        phTmp.color = C_SUB; phTmp.fontStyle = FontStyles.Italic; phTmp.alignment = TextAlignmentOptions.Center;
        var phRt = ph.GetComponent<RectTransform>();
        phRt.anchorMin = Vector2.zero; phRt.anchorMax = Vector2.one;
        phRt.offsetMin = phRt.offsetMax = Vector2.zero;

        var field = go.AddComponent<TMP_InputField>();
        field.textViewport = taRt; field.textComponent = itTmp;
        field.placeholder = phTmp; field.text = defaultVal;

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(width, 30);
        return field;
    }

    static Sprite s_refreshIcon;

    void SetupRefreshIcon(Button btn)
    {
        if (s_refreshIcon == null)
        {
            var tex = Resources.Load<Texture2D>("Emoji/refresh");
            if (tex != null)
                s_refreshIcon = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }
        if (s_refreshIcon == null) return;

        var txt = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (txt != null) Destroy(txt.gameObject);

        var iconGO = new GameObject("RefreshIcon");
        iconGO.transform.SetParent(btn.transform, false);
        var img = iconGO.AddComponent<Image>();
        img.sprite = s_refreshIcon;
        img.color  = C_TEXT;
        var rt = iconGO.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta        = new Vector2(22f, 22f);
        rt.anchoredPosition = Vector2.zero;
    }


    Button MakeSmallButton(Transform parent, string label, Vector2 pos, float size)
    {
        var go = new GameObject("BtnSmall"); go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>(); img.color = Color.clear;
        var tmp = new GameObject("Text"); tmp.transform.SetParent(go.transform, false);
        var t = tmp.AddComponent<TextMeshProUGUI>();
        t.text = label; t.fontSize = 14; t.color = C_TEXT; t.alignment = TextAlignmentOptions.Center;
        var tRt = tmp.GetComponent<RectTransform>();
        tRt.anchorMin = Vector2.zero; tRt.anchorMax = Vector2.one;
        tRt.offsetMin = tRt.offsetMax = Vector2.zero;
        var btn = go.AddComponent<Button>();
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(size, 30);
        return btn;
    }

    Button MakeBackButton(Transform parent, Vector2 pos, Vector2 size)
    {
        var go = new GameObject("BtnBack"); go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>(); img.color = C_BORDER;
        var tmp = new GameObject("Text"); tmp.transform.SetParent(go.transform, false);
        var t = tmp.AddComponent<TextMeshProUGUI>();
        t.text = "<- Volver"; t.fontSize = 11; t.fontStyle = FontStyles.Bold;
        t.color = C_TEXT; t.alignment = TextAlignmentOptions.Center;
        var tRt = tmp.GetComponent<RectTransform>();
        tRt.anchorMin = Vector2.zero; tRt.anchorMax = Vector2.one;
        tRt.offsetMin = tRt.offsetMax = Vector2.zero;
        var btn = go.AddComponent<Button>();
        var cb = btn.colors;
        cb.normalColor      = C_BORDER;
        cb.highlightedColor = Color.Lerp(C_BORDER, Color.white, 0.15f);
        cb.pressedColor     = Color.Lerp(C_BORDER, Color.black, 0.2f);
        btn.colors = cb;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return btn;
    }

    Button MakePlayButton(Transform parent, Vector2 pos, Vector2 size)
    {
        var go = new GameObject("BtnPlay"); go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>(); img.color = C_ACCENT;
        var tmp = new GameObject("Text"); tmp.transform.SetParent(go.transform, false);
        var t = tmp.AddComponent<TextMeshProUGUI>();
        t.text = "INICIAR"; t.fontSize = 13; t.fontStyle = FontStyles.Bold;
        t.color = new Color(0.03f, 0.05f, 0.09f, 1f);
        t.alignment = TextAlignmentOptions.Center; t.characterSpacing = 18;
        var tRt = tmp.GetComponent<RectTransform>();
        tRt.anchorMin = Vector2.zero; tRt.anchorMax = Vector2.one;
        tRt.offsetMin = tRt.offsetMax = Vector2.zero;
        var btn = go.AddComponent<Button>();
        var cb = btn.colors;
        cb.normalColor      = C_ACCENT;
        cb.highlightedColor = Color.Lerp(C_ACCENT, Color.white, 0.2f);
        cb.pressedColor     = Color.Lerp(C_ACCENT, Color.black, 0.2f);
        btn.colors = cb;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return btn;
    }

    void MakeDot(Transform parent, Vector2 pos, float r, Color col, float alpha)
    {
        var go  = new GameObject("HUD_Dot");
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = new Color(col.r, col.g, col.b, alpha);
        img.raycastTarget = false;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(r * 2f, r * 2f);
    }
}










