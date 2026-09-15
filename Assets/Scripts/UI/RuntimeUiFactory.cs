using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Meniscus.UI
{
    /// <summary>
    /// Shared builder for the procedurally created fallback UI used by the saloon HUD, shop,
    /// and end screen. Centralizing it keeps the look consistent and makes a later migration to
    /// authored prefabs a single-point swap instead of three near-identical code paths.
    /// </summary>
    public static class RuntimeUiFactory
    {
        public static readonly Color ButtonNormalColor = new(0.18f, 0.08f, 0.04f, 1f);
        public static readonly Color ButtonHighlightedColor = new(0.34f, 0.15f, 0.07f, 1f);
        public static readonly Color ButtonPressedColor = new(0.08f, 0.03f, 0.02f, 1f);
        public static readonly Color ButtonLabelColor = new(0.98f, 0.86f, 0.58f);

        public static Canvas CreateOverlayCanvas(Transform parent, string name, bool enabled = true)
        {
            var canvasObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));

            if (parent != null)
                canvasObject.transform.SetParent(parent, false);

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.enabled = enabled;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            // Balance width against height. The default matches width alone, which on a
            // portrait phone scales the whole 1920-wide layout down to the handset's width
            // and leaves every control too small to read or hit.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            return canvas;
        }

        public static Canvas CreateWorldCanvas(
            Transform parent,
            string name,
            Vector2 pixelSize,
            Camera worldCamera)
        {
            var canvasObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));

            if (parent != null)
                canvasObject.transform.SetParent(parent, false);

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = worldCamera != null ? worldCamera : Camera.main;

            var rectTransform = canvasObject.GetComponent<RectTransform>();
            rectTransform.sizeDelta = pixelSize;

            return canvas;
        }

        public static GameObject CreateImage(Transform parent, string name, Vector2 size, Vector2 position, Color color)
        {
            var imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(parent, false);

            var rectTransform = imageObject.GetComponent<RectTransform>();
            rectTransform.sizeDelta = size;
            rectTransform.anchoredPosition = position;

            imageObject.GetComponent<Image>().color = color;
            return imageObject;
        }

        public static Text CreateText(
            Transform parent,
            string name,
            string text,
            Vector2 position,
            Vector2 size,
            int fontSize,
            Color color,
            TextAnchor alignment = TextAnchor.MiddleCenter,
            bool bold = false)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);

            var rectTransform = textObject.GetComponent<RectTransform>();
            rectTransform.sizeDelta = size;
            rectTransform.anchoredPosition = position;

            var uiText = textObject.GetComponent<Text>();
            uiText.text = text;
            uiText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            uiText.fontSize = fontSize;
            uiText.alignment = alignment;
            uiText.color = color;

            if (bold)
                uiText.fontStyle = FontStyle.Bold;

            return uiText;
        }

        public static Button CreateButton(
            Transform parent,
            string name,
            string label,
            Vector2 size,
            Vector2 position,
            int labelFontSize = 18,
            UnityAction onClick = null,
            bool boldLabel = false,
            Color? normalColor = null,
            Color? highlightedColor = null,
            Color? pressedColor = null,
            Color? labelColor = null,
            TextAnchor labelAlignment = TextAnchor.MiddleCenter,
            Vector2 labelPadding = default)
        {
            var normal = normalColor ?? ButtonNormalColor;
            var buttonObject = CreateImage(parent, name, size, position, normal);

            var button = buttonObject.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = normal;
            colors.highlightedColor = highlightedColor ?? ButtonHighlightedColor;
            colors.pressedColor = pressedColor ?? ButtonPressedColor;
            button.colors = colors;

            if (onClick != null)
                button.onClick.AddListener(onClick);

            CreateText(
                buttonObject.transform,
                "Label",
                label,
                Vector2.zero,
                new Vector2(size.x - labelPadding.x * 2f, size.y - labelPadding.y * 2f),
                labelFontSize,
                labelColor ?? ButtonLabelColor,
                labelAlignment,
                boldLabel);

            // Springy "sink on press, pop on release" feel on every fallback button (play mode only).
            buttonObject.AddComponent<UiPressPunch>();

            return button;
        }

        /// <summary>
        /// A slider in the same warm gold-on-dark language as the buttons: a thin recessed
        /// track, a filled portion and a chunky handle sized for a fingertip on touch.
        /// </summary>
        public static Slider CreateSlider(
            Transform parent,
            string name,
            Vector2 size,
            Vector2 position,
            float value,
            UnityAction<float> onChanged = null)
        {
            var root = CreateImage(parent, name, size, position, new Color(0f, 0f, 0f, 0f));
            var slider = root.AddComponent<Slider>();

            var trackHeight = Mathf.Max(6f, size.y * 0.22f);
            CreateImage(root.transform, "Track", new Vector2(size.x, trackHeight),
                Vector2.zero, new Color(0.18f, 0.13f, 0.09f, 0.95f));

            var fillArea = CreateImage(root.transform, "Fill Area", new Vector2(size.x, trackHeight),
                Vector2.zero, new Color(0f, 0f, 0f, 0f));
            var fill = CreateImage(fillArea.transform, "Fill", new Vector2(size.x, trackHeight),
                Vector2.zero, new Color(0.75f, 0.55f, 0.24f, 0.95f));

            var handleSize = Mathf.Max(size.y, trackHeight * 2.6f);
            var handleArea = CreateImage(root.transform, "Handle Area", new Vector2(size.x, size.y),
                Vector2.zero, new Color(0f, 0f, 0f, 0f));
            var handle = CreateImage(handleArea.transform, "Handle", new Vector2(handleSize, handleSize),
                Vector2.zero, new Color(0.95f, 0.82f, 0.52f, 1f));

            // Stretch the moving parts so the Slider drives them rather than fixed offsets.
            foreach (var stretch in new[] { fillArea.GetComponent<RectTransform>(), handleArea.GetComponent<RectTransform>() })
            {
                stretch.anchorMin = new Vector2(0f, 0.5f);
                stretch.anchorMax = new Vector2(1f, 0.5f);
                stretch.offsetMin = new Vector2(handleSize * 0.5f, -size.y * 0.5f);
                stretch.offsetMax = new Vector2(-handleSize * 0.5f, size.y * 0.5f);
            }

            var fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0f, 0.5f);
            fillRect.anchorMax = new Vector2(1f, 0.5f);
            fillRect.offsetMin = new Vector2(0f, -trackHeight * 0.5f);
            fillRect.offsetMax = new Vector2(0f, trackHeight * 0.5f);

            slider.fillRect = fillRect;
            slider.handleRect = handle.GetComponent<RectTransform>();
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.SetValueWithoutNotify(Mathf.Clamp01(value));

            var colors = slider.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.95f, 0.82f);
            colors.pressedColor = new Color(0.85f, 0.7f, 0.42f);
            slider.colors = colors;

            if (onChanged != null)
                slider.onValueChanged.AddListener(onChanged);

            return slider;
        }
    }
}
