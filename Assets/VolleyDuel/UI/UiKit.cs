using System;
using UnityEngine;
using UnityEngine.UI;

namespace VolleyDuel.UI
{
    /// <summary>
    /// Builds uGUI from code (no prefabs or hand-edited YAML): canvases, panels, labels, buttons and fill
    /// bars. Graybox styling stays in one place so the HUD pass can restyle it. Labels use uGUI Text with
    /// the engine's built-in font, so a fresh project shows text on device with no import step
    /// (TextMeshPro needs its essentials imported first; see DECISIONS U5).
    /// </summary>
    public static class UiKit
    {
        public static readonly Color Ink = new Color(0.06f, 0.07f, 0.09f, 0.88f);
        public static readonly Color Paper = new Color(0.96f, 0.95f, 0.92f, 1f);
        public static readonly Color Accent = new Color(1f, 0.78f, 0.2f, 1f);
        public static readonly Color Dim = new Color(1f, 1f, 1f, 0.35f);

        public static Canvas Canvas(string name, int sortingOrder)
        {
            var go = new GameObject(name);
            Canvas c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = sortingOrder;
            CanvasScaler scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            go.AddComponent<GraphicRaycaster>();
            return c;
        }

        /// <summary>A full-screen child that follows Screen.safeArea (notches, rounded corners, home bar).</summary>
        public static RectTransform SafeArea(Transform canvas)
        {
            RectTransform r = Rect("SafeArea", canvas, Vector2.zero, Vector2.one);
            r.gameObject.AddComponent<SafeAreaFitter>();
            return r;
        }

        public static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchorMin = anchorMin;
            r.anchorMax = anchorMax;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
            return r;
        }

        /// <summary>A rect at an anchor point with a fixed size in reference pixels.</summary>
        public static RectTransform At(string name, Transform parent, Vector2 anchor, Vector2 size, Vector2 offset)
        {
            RectTransform r = Rect(name, parent, anchor, anchor);
            r.pivot = anchor;
            r.sizeDelta = size;
            r.anchoredPosition = offset;
            return r;
        }

        public static Image Panel(Transform parent, string name, Color color, Vector2 anchorMin, Vector2 anchorMax)
        {
            RectTransform r = Rect(name, parent, anchorMin, anchorMax);
            Image img = r.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        static Font font;

        /// <summary>The engine's built-in UI font, with an OS font as a fallback.</summary>
        public static Font Font
        {
            get
            {
                if (font != null) return font;
                try
                {
                    font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }
                catch (ArgumentException)
                {
                    font = null;
                }
                if (font == null) font = Font.CreateDynamicFontFromOSFont(new[] { "Helvetica Neue", "Roboto", "Arial" }, 32);
                return font;
            }
        }

        public static Text Label(Transform parent, string text, float size, TextAnchor align, Color color)
        {
            RectTransform r = Rect("Label", parent, Vector2.zero, Vector2.one);
            Text t = r.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = Mathf.RoundToInt(size);
            t.alignment = align;
            t.color = color;
            t.raycastTarget = false;
            t.supportRichText = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        public static void SetAlpha(Graphic g, float alpha)
        {
            Color c = g.color;
            c.a = alpha;
            g.color = c;
        }

        public static Button Button(Transform parent, string label, Action onClick, float height = 110f)
        {
            RectTransform r = Rect(label, parent, Vector2.zero, Vector2.one);
            LayoutElement le = r.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            Image img = r.gameObject.AddComponent<Image>();
            img.color = Ink;
            Button b = r.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            ColorBlock colors = b.colors;
            colors.highlightedColor = new Color(0.2f, 0.22f, 0.28f, 1f);
            colors.pressedColor = Accent;
            b.colors = colors;
            if (onClick != null) b.onClick.AddListener(() => onClick());
            Label(r, label, 46f, TextAnchor.MiddleCenter, Paper);
            return b;
        }

        /// <summary>A vertical list of buttons centered on screen.</summary>
        public static RectTransform Column(Transform parent, string name, float width, Vector2 anchor)
        {
            RectTransform r = At(name, parent, anchor, new Vector2(width, 0f), Vector2.zero);
            VerticalLayoutGroup v = r.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 18f;
            v.childControlHeight = true;
            v.childControlWidth = true;
            v.childForceExpandHeight = false;
            v.childForceExpandWidth = true;
            ContentSizeFitter fit = r.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return r;
        }

        /// <summary>A horizontal bar whose fill grows from one side (score bars, Stamina).</summary>
        public static RectTransform Bar(Transform parent, string name, Color back, Color fill, bool fromRight, out RectTransform fillRect)
        {
            Image bg = Panel(parent, name, back, Vector2.zero, Vector2.one);
            Image f = Panel(bg.transform, "Fill", fill, fromRight ? new Vector2(1f, 0f) : Vector2.zero, fromRight ? Vector2.one : new Vector2(0f, 1f));
            fillRect = f.rectTransform;
            return bg.rectTransform;
        }

        /// <summary>Sets a bar's fill fraction (0-1).</summary>
        public static void SetFill(RectTransform fill, float fraction, bool fromRight)
        {
            fraction = Mathf.Clamp01(fraction);
            if (fromRight)
            {
                fill.anchorMin = new Vector2(1f - fraction, 0f);
                fill.anchorMax = Vector2.one;
            }
            else
            {
                fill.anchorMin = Vector2.zero;
                fill.anchorMax = new Vector2(fraction, 1f);
            }
        }
    }

    /// <summary>Keeps a RectTransform inside Screen.safeArea; landscape phones put notches on the sides.</summary>
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        Rect applied;

        void Update()
        {
            Rect safe = Screen.safeArea;
            if (safe == applied || Screen.width <= 0 || Screen.height <= 0) return;
            applied = safe;
            var r = (RectTransform)transform;
            r.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
            r.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
        }
    }
}
