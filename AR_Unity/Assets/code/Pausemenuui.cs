using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using ARDashboard;

/// <summary>
/// Añade este script a un GameObject vacío en SampleScene.
/// Pulsar Escape abre/cierra el menú de pausa.
/// </summary>
public class PauseMenuUI : MonoBehaviour
{
    // ── Colores (misma paleta que el resto de menús) ──────────────────────────
    static readonly Color C_CARD   = new Color(0.07f, 0.09f, 0.14f, 1f);
    static readonly Color C_ACCENT = new Color(0.15f, 1.00f, 0.70f, 1f);
    static readonly Color C_CYAN   = new Color(0.00f, 0.95f, 1.00f, 1f);
    static readonly Color C_BORDER = new Color(0.16f, 0.22f, 0.32f, 1f);
    static readonly Color C_TEXT   = new Color(0.90f, 0.95f, 1.00f, 1f);
    static readonly Color C_SUB    = new Color(0.42f, 0.58f, 0.72f, 1f);
    static readonly Color C_DISAB  = new Color(0.18f, 0.22f, 0.30f, 1f);

    public static bool IsPaused { get; private set; } = false;

    bool isPaused = false;
    GameObject pauseCanvas;
    CanvasGroup pauseCanvasGroup;
    float pauseFadeTime = 0f;
    bool  isFadingIn    = false;
    const float PAUSE_FADE_DURATION = 0.3f;
    Button hudToggleBtn;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (isPaused) Resume();
            else          Pause();
        }

        // Fade-in del menú de pausa
        if (isFadingIn && pauseCanvasGroup != null)
        {
            pauseFadeTime += Time.unscaledDeltaTime;
            pauseCanvasGroup.alpha = Mathf.Clamp01(pauseFadeTime / PAUSE_FADE_DURATION);
            if (pauseCanvasGroup.alpha >= 1f) isFadingIn = false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    void Pause()
    {
        isPaused = true;
        IsPaused = true;
        Time.timeScale = 0f;

        if (pauseCanvas == null)
            BuildPauseMenu();

        pauseCanvas.SetActive(true);

        // Arrancar fade-in
        pauseFadeTime = 0f;
        isFadingIn    = true;
        if (pauseCanvasGroup != null) pauseCanvasGroup.alpha = 0f;
    }

    void Resume()
    {
        isPaused = false;
        IsPaused = false;
        Time.timeScale = 1f;

        if (pauseCanvas != null)
            pauseCanvas.SetActive(false);
    }

    void GoToScenarioSelector()
    {
        isPaused = false;
        IsPaused = false;
        Time.timeScale = 1f;
        // Borra el geofence de la misión antes de salir -- si no, se quedaría
        // activo en el autopiloto hasta cerrar la aplicación entera (ver
        // MapSceneController.RegisterQuitCleanup).
        MapSceneController.ClearGeofence(waitMs: 0);
        SceneManager.LoadScene("inicio");
    }

    // Cicla OFF -> ON (verde) -> ON (negro) -> ON (blanco) -> OFF, un clic cada vez.
    void ToggleFlightHud()
    {
        if (!FlightHud.Enabled)
        {
            FlightHud.Enabled   = true;
            FlightHud.ColorMode = FlightHud.HudColor.Green;
        }
        else if (FlightHud.ColorMode == FlightHud.HudColor.Green)
        {
            FlightHud.ColorMode = FlightHud.HudColor.Black;
        }
        else if (FlightHud.ColorMode == FlightHud.HudColor.Black)
        {
            FlightHud.ColorMode = FlightHud.HudColor.White;
        }
        else
        {
            FlightHud.Enabled = false;
        }
        RefreshHudToggleLabel();
    }

    void RefreshHudToggleLabel()
    {
        if (hudToggleBtn == null) return;
        bool on = FlightHud.Enabled;
        // Relleno siempre discreto (igual que un botón secundario): solo el
        // color del texto refleja el estado, sin bloques de color grandes que
        // compitan con CONTINUAR / VOLVER AL INICIO.
        var txt = hudToggleBtn.GetComponentInChildren<TextMeshProUGUI>();
        if (txt != null)
        {
            string colorLabel = FlightHud.ColorMode switch
            {
                FlightHud.HudColor.Black => "NEGRO",
                FlightHud.HudColor.White => "BLANCO",
                _                        => "VERDE",
            };
            txt.text     = !on ? "HUD DE VUELO: OFF" : $"HUD DE VUELO: {colorLabel}";
            txt.color    = on ? C_ACCENT : C_SUB;
            txt.fontSize = 9f;
        }
    }

    void Awake()
    {
        IsPaused = false;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BUILD UI
    // ─────────────────────────────────────────────────────────────────────────
    void BuildPauseMenu()
    {
        // Canvas
        pauseCanvas = new GameObject("PauseCanvas");
        pauseCanvas.transform.SetParent(transform);
        var canvas = pauseCanvas.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        var scaler = pauseCanvas.AddComponent<CanvasScaler>();
        scaler.uiScaleMode        = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        pauseCanvas.AddComponent<GraphicRaycaster>();

        // CanvasGroup para fade-in
        pauseCanvasGroup = pauseCanvas.AddComponent<CanvasGroup>();
        pauseCanvasGroup.alpha = 0f;

        // Fondo grisáceo con tinte azulado que da sensación de pausa
        var overlay = MakeImage(pauseCanvas.transform, "Overlay", new Color(0.04f, 0.06f, 0.10f, 0.82f));
        Stretch(overlay);

        // Capa de viñeta adicional para oscurecer los bordes
        var vignette = MakeImage(pauseCanvas.transform, "Vignette", new Color(0f, 0f, 0f, 0.45f));
        Stretch(vignette);



        // Card central
        var card = MakeImage(pauseCanvas.transform, "PauseCard", C_CARD);
        var cardRt = card.GetComponent<RectTransform>();
        cardRt.anchorMin = cardRt.anchorMax = new Vector2(0.5f, 0.5f);
        cardRt.pivot     = new Vector2(0.5f, 0.5f);
        cardRt.sizeDelta = new Vector2(360f, 268f);
        cardRt.anchoredPosition = Vector2.zero;
        AddOutline(card, C_BORDER);

        // Degradado negro escalonado: más oscuro abajo, más claro arriba
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

        // Barra decorativa superior cyan
        var bar = MakeImage(card.transform, "TopBar", C_CYAN);
        var barRt = bar.GetComponent<RectTransform>();
        barRt.anchorMin = new Vector2(0f, 1f);
        barRt.anchorMax = new Vector2(0.12f, 1f);
        barRt.pivot     = new Vector2(0f, 1f);
        barRt.offsetMin = barRt.offsetMax = Vector2.zero;
        barRt.sizeDelta = new Vector2(0f, 4f);

        // Versión del sistema
        MakeLabel(card.transform, "SISTEMA AR·V1.0", new Vector2(0f, -18f),
            7.5f, C_CYAN, FontStyles.Bold, TextAlignmentOptions.Center,
            new Vector2(320f, 13f), 22f);

        // Título grande
        var pauseTitleLbl = MakeLabel(card.transform, "Pausa", new Vector2(0f, -36f),
            26f, C_TEXT, FontStyles.Bold, TextAlignmentOptions.Center,
            new Vector2(320f, 36f));
        var orbitronPause = MapCreatorUI.GetOrbitronFontAsset();
        if (orbitronPause != null) pauseTitleLbl.font = orbitronPause;

        // Toggle HUD de vuelo (altitud/velocidad/rumbo/mira) — persistido, ver
        // ARDashboard.FlightHud.Enabled. Deliberadamente pequeño y discreto
        // (no compite visualmente con CONTINUAR / VOLVER AL INICIO), colocado
        // encima de CONTINUAR con margen de sobra para que no se toquen.
        hudToggleBtn = MakeButton(card.transform, "", new Vector2(0f, -84f), new Vector2(160f, 22f),
            Color.clear, C_SUB);
        hudToggleBtn.onClick.AddListener(ToggleFlightHud);
        RefreshHudToggleLabel();

        // Botón CONTINUAR
        var btnResume = MakeButton(card.transform, "CONTINUAR",
            new Vector2(0f, -128f), new Vector2(300f, 44f),
            C_ACCENT, new Color(0.03f, 0.05f, 0.09f, 1f));
        btnResume.onClick.AddListener(Resume);

        // Botón VOLVER AL MENÚ
        var btnBack = MakeButton(card.transform, "VOLVER AL INICIO",
            new Vector2(0f, -186f), new Vector2(300f, 44f),
            C_DISAB, C_TEXT);
        btnBack.onClick.AddListener(GoToScenarioSelector);

        // Borde luminoso
        BuildGlowBorder(pauseCanvas.transform, cardRt);
    }

    // ── Borde luminoso alrededor del card ────────────────────────────────────
    void BuildGlowBorder(Transform canvasT, RectTransform cardRef)
    {
        float w  = cardRef.sizeDelta.x;
        float h  = cardRef.sizeDelta.y;
        float t  = 2f;
        Color gc = new Color(C_CYAN.r, C_CYAN.g, C_CYAN.b, 0.5f);

        string[]  names = { "GlowTop", "GlowBottom", "GlowLeft", "GlowRight" };
        Vector2[] sizes = { new Vector2(w, t), new Vector2(w, t),
                            new Vector2(t, h), new Vector2(t, h) };
        Vector2[] positions = { new Vector2(0f,  h * 0.5f), new Vector2(0f, -h * 0.5f),
                                new Vector2(-w * 0.5f, 0f), new Vector2( w * 0.5f, 0f) };

        for (int i = 0; i < 4; i++)
        {
            var go  = new GameObject(names[i]);
            go.transform.SetParent(canvasT, false);
            var img = go.AddComponent<Image>();
            img.color = gc; img.raycastTarget = false;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = positions[i];
            rt.sizeDelta = sizes[i];
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────────────────────────────────
    Image MakeImage(Transform parent, string name, Color color)
    {
        var go  = new GameObject(name);
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
        o.effectColor    = color;
        o.effectDistance = new Vector2(1f, -1f);
    }

    TextMeshProUGUI MakeLabel(Transform parent, string text, Vector2 pos, float size,
        Color color, FontStyles style, TextAlignmentOptions align,
        Vector2 sizeDelta, float letterSpacing = 0f)
    {
        var go  = new GameObject("Lbl");
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text             = text;
        tmp.fontSize         = size;
        tmp.color            = color;
        tmp.fontStyle        = style;
        tmp.alignment        = align;
        tmp.characterSpacing = letterSpacing;
        tmp.raycastTarget    = false;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot     = new Vector2(0.5f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = sizeDelta;
        return tmp;
    }

    void MakeDivider(Transform parent, float y, float width)
    {
        var go  = new GameObject("Divider");
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>(); img.color = C_BORDER;
        var rt  = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot     = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, y);
        rt.sizeDelta = new Vector2(width, 1f);
    }

    Button MakeButton(Transform parent, string label, Vector2 pos, Vector2 size,
        Color fillColor, Color textColor)
    {
        var go  = new GameObject("Btn_" + label);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>(); img.color = fillColor;

        var txtGO = new GameObject("Txt");
        txtGO.transform.SetParent(go.transform, false);
        var t = txtGO.AddComponent<TextMeshProUGUI>();
        t.text      = label;
        t.fontSize  = 12f;
        t.fontStyle = FontStyles.Bold;
        t.color     = textColor;
        t.alignment = TextAlignmentOptions.Center;
        t.characterSpacing = 14f;
        var tRt = txtGO.GetComponent<RectTransform>();
        tRt.anchorMin = Vector2.zero; tRt.anchorMax = Vector2.one;
        tRt.offsetMin = tRt.offsetMax = Vector2.zero;

        var btn = go.AddComponent<Button>();
        var cb  = btn.colors;
        cb.normalColor      = fillColor;
        cb.highlightedColor = Color.Lerp(fillColor, Color.white, 0.2f);
        cb.pressedColor     = Color.Lerp(fillColor, Color.black, 0.2f);
        cb.disabledColor    = new Color(0.18f, 0.22f, 0.30f, 1f);
        btn.colors = cb;

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot     = new Vector2(0.5f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return btn;
    }
}