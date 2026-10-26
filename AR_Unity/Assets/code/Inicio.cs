using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
 
namespace ARDashboard
{
    public class Inicio : MonoBehaviour
    {
        [Header("Escena destino")]
        public string targetScene = "Menú";
 
        static readonly Color C_CARD   = new Color(0.07f, 0.09f, 0.14f, 1f);
        static readonly Color C_ACCENT = new Color(0.15f, 1.00f, 0.70f, 1f);
        static readonly Color C_CYAN   = new Color(0.00f, 0.95f, 1.00f, 1f);
        static readonly Color C_BORDER = new Color(0.16f, 0.22f, 0.32f, 1f);
        static readonly Color C_TEXT   = new Color(0.90f, 0.95f, 1.00f, 1f);
        static readonly Color C_SUB    = new Color(0.42f, 0.58f, 0.72f, 1f);
        static readonly Color C_DISAB  = new Color(0.18f, 0.22f, 0.30f, 1f);

        void Start()
        {
           
            RemoveExistingCanvas();
            BuildUI();
        }

        void RemoveExistingCanvas()
        {
            var existing = transform.Find("StartMenuCanvas");
            if (existing == null) return;
            if (Application.isPlaying) Destroy(existing.gameObject);
            else DestroyImmediate(existing.gameObject);
        }

#if UNITY_EDITOR
        // Right-click the Inicio component in the Inspector (without
        // pressing Play) to materialize the canvas into the Hierarchy —
        // the exact same BuildUI() the game uses at runtime.
        [ContextMenu("Generar Canvas en el Editor (sin Play)")]
        void GenerateCanvasInEditor()
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning("[Inicio] Esto es solo para usarlo en el Editor sin pulsar Play.");
                return;
            }
            RemoveExistingCanvas();
            BuildUI();
            UnityEditor.EditorUtility.SetDirty(gameObject);
        }
#endif

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

            // Fondo con shader animado
            var bgAnim = MakeImage(canvasGO.transform, "BGAnim", Color.white);
            Stretch(bgAnim);
            var mat = ShaderTimekeeper.GetMaterial();
            if (mat != null) bgAnim.material = mat;
            bgAnim.raycastTarget = false;
 
            // Panel central
            var card = MakeImage(canvasGO.transform, "Window", C_CARD);
            var cardRt = card.GetComponent<RectTransform>();
            cardRt.anchorMin = cardRt.anchorMax = new Vector2(0.5f, 0.5f);
            cardRt.pivot = new Vector2(0.5f, 0.5f);
            cardRt.sizeDelta = new Vector2(480f, 330f);
            cardRt.anchoredPosition = Vector2.zero;
            AddOutline(card, C_BORDER);

            // Degradado negro escalonado: más oscuro abajo, más claro arriba (10 bandas)
            const int darkBands = 10;
            for (int i = 0; i < darkBands; i++)
            {
                float yMin  = i / (float)darkBands;
                float yMax  = (i + 1) / (float)darkBands;
                float alpha = Mathf.Lerp(0.88f, 0.00f, i / (float)(darkBands - 1));
                var bandImg = MakeImage(card.transform, "CardGradDark" + i, new Color(0f, 0f, 0f, alpha));
                bandImg.transform.SetAsFirstSibling();
                bandImg.raycastTarget = false;
                var bRt = bandImg.GetComponent<RectTransform>();
                bRt.anchorMin = new Vector2(0f, yMin);
                bRt.anchorMax = new Vector2(1f, yMax);
                bRt.offsetMin = bRt.offsetMax = Vector2.zero;
            }

            BuildCardGlowBorder(canvasGO.transform, cardRt);

            // Barra de acento superior izquierda
            var bar = MakeImage(card.transform, "Bar", new Color(C_CYAN.r, C_CYAN.g, C_CYAN.b, 0.28f));
            var barRt = bar.GetComponent<RectTransform>();
            barRt.anchorMin = new Vector2(0, 1); barRt.anchorMax = new Vector2(0.1f, 1);
            barRt.pivot = new Vector2(0, 1);
            barRt.offsetMin = barRt.offsetMax = Vector2.zero;
            barRt.sizeDelta = new Vector2(0, 4f);
 
            MakeLabel(card.transform, "SISTEMA AR·V1.0", new Vector2(0, -40f), 9,
                C_CYAN, FontStyles.Bold, TextAlignmentOptions.Center, new Vector2(400f, 14f), 22);

            var missionTitleLbl = MakeLabel(card.transform, "AR DRONE MISSION", new Vector2(0, -75f), 28,
                C_TEXT, FontStyles.Bold, TextAlignmentOptions.Center, new Vector2(420f, 40f));
            var orbitron = MapCreatorUI.GetOrbitronFontAsset();
            if (orbitron != null) missionTitleLbl.font = orbitron;

            var btnIniciar = MakeFillButton(card.transform, "INICIAR",
                new Vector2(0f, -154f), new Vector2(260f, 52f),
                C_ACCENT, new Color(0.03f, 0.05f, 0.09f, 1f));
            btnIniciar.onClick.AddListener(GoToScenario);

            var btnSalir = MakeFillButton(card.transform, "SALIR",
                new Vector2(0f, -218f), new Vector2(260f, 40f),
                C_DISAB, C_SUB);
            btnSalir.onClick.AddListener(() => Application.Quit());

            MakeLabel(card.transform, "●  SISTEMA LISTO", new Vector2(0f, -276f), 9,
                C_ACCENT, FontStyles.Bold, TextAlignmentOptions.Center, new Vector2(300f, 14f), 14);
        }
 
        void GoToScenario()
        {
            string scene = string.IsNullOrEmpty(targetScene) ? "Menú" : targetScene;
            SceneManager.LoadScene(scene);
        }
 
        void BuildCardGlowBorder(Transform canvasT, RectTransform cardRef)
        {
            float w = cardRef.sizeDelta.x, h = cardRef.sizeDelta.y, t = 2.5f;
            Color gc = new Color(C_CYAN.r, C_CYAN.g, C_CYAN.b, 0.28f);
            string[]  names = { "GlowTop", "GlowBottom", "GlowLeft", "GlowRight" };
            Vector2[] sizes = { new Vector2(w, t), new Vector2(w, t), new Vector2(t, h), new Vector2(t, h) };
            Vector2[] pos   = { new Vector2(0, h * 0.5f), new Vector2(0, -h * 0.5f),
                                new Vector2(-w * 0.5f, 0), new Vector2(w * 0.5f, 0) };
            for (int i = 0; i < 4; i++)
            {
                var go = new GameObject(names[i]);
                go.transform.SetParent(canvasT, false);
                var img = go.AddComponent<Image>();
                img.color = gc; img.raycastTarget = false;
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = pos[i]; rt.sizeDelta = sizes[i];
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


        Button MakeFillButton(Transform parent, string label, Vector2 pos, Vector2 size,
            Color fillColor, Color textColor)
        {
            var go = new GameObject("Btn_" + label);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>(); img.color = fillColor;
            var txtGO = new GameObject("Txt");
            txtGO.transform.SetParent(go.transform, false);
            var t = txtGO.AddComponent<TextMeshProUGUI>();
            t.text = label; t.fontSize = 14; t.fontStyle = FontStyles.Bold;
            t.color = textColor; t.alignment = TextAlignmentOptions.Center;
            t.characterSpacing = 8f;
            var tRt = txtGO.GetComponent<RectTransform>();
            tRt.anchorMin = Vector2.zero; tRt.anchorMax = Vector2.one;
            tRt.offsetMin = tRt.offsetMax = Vector2.zero;
            var btn = go.AddComponent<Button>();
            var cb = btn.colors;
            cb.normalColor      = fillColor;
            cb.highlightedColor = Color.Lerp(fillColor, Color.white, 0.22f);
            cb.pressedColor     = Color.Lerp(fillColor, Color.black, 0.22f);
            btn.colors = cb;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            return btn;
        }
    }
}