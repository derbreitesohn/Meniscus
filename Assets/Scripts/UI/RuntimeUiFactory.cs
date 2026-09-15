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
        /// A slider in the same warm gold-on-dark language as the buttons: a rounded recessed
        /// track, a rounded filled portion and a round handle sized for a fingertip on touch.
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

            // Thick enough that the rounded caps read as a pill rather than a hairline.
            var trackHeight = Mathf.Max(8f, size.y * 0.32f);
            var handleSize = Mathf.Max(size.y, trackHeight * 2.4f);
            // Half a handle at each end, so the knob's travel stops flush with the track.
            var inset = handleSize * 0.5f;
            var pill = PillSprite(trackHeight);

            var track = CreateImage(root.transform, "Track", Vector2.zero,
                Vector2.zero, new Color(0.18f, 0.13f, 0.09f, 0.95f));
            StretchAcross(track, inset, trackHeight);
            ApplyPill(track, pill);

            // Slider drives the fill's and handle's anchors outright — it overwrites both to
            // stretch on the axis it is not sliding along. So their height has to come from
            // the container they sit in, and their own sizeDelta on that axis must be zero,
            // or they end up a container taller than the groove they are meant to sit in.
            var fillArea = CreateImage(root.transform, "Fill Area", Vector2.zero,
                Vector2.zero, new Color(0f, 0f, 0f, 0f));
            StretchAcross(fillArea, inset, trackHeight);

            var fill = CreateImage(fillArea.transform, "Fill", Vector2.zero,
                Vector2.zero, new Color(0.75f, 0.55f, 0.24f, 0.95f));
            ApplyPill(fill, pill);

            var handleArea = CreateImage(root.transform, "Handle Area", Vector2.zero,
                Vector2.zero, new Color(0f, 0f, 0f, 0f));
            StretchAcross(handleArea, inset, handleSize);

            // A gold disc inside a dark rim, so the knob still reads where it sits on the fill.
            var handle = CreateImage(handleArea.transform, "Handle", new Vector2(handleSize, 0f),
                Vector2.zero, new Color(0.22f, 0.13f, 0.06f, 1f));
            handle.GetComponent<Image>().sprite = CircleSprite();

            var knob = CreateImage(handle.transform, "Knob",
                new Vector2(handleSize * 0.74f, handleSize * 0.74f),
                Vector2.zero, new Color(0.95f, 0.82f, 0.52f, 1f));
            var knobImage = knob.GetComponent<Image>();
            knobImage.sprite = CircleSprite();
            knobImage.raycastTarget = false;

            slider.fillRect = fill.GetComponent<RectTransform>();
            slider.handleRect = handle.GetComponent<RectTransform>();
            slider.targetGraphic = knobImage;
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

        /// <summary>
        /// Lays a child across its parent's width at a fixed height, pulled in at both ends.
        /// </summary>
        static void StretchAcross(GameObject target, float inset, float height)
        {
            var rect = target.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.offsetMin = new Vector2(inset, -height * 0.5f);
            rect.offsetMax = new Vector2(-inset, height * 0.5f);
        }

        // --- Rounded chrome -------------------------------------------------------------
        //
        // Nothing here is authored, so the rounded shapes are drawn once into a small texture
        // and reused. Both come out of the same signed-distance pass: a pill is a rounded
        // rectangle with a near-half corner radius, a circle is one with exactly half.

        const int RoundedTextureSize = 64;
        const float PillCornerRadius = 30f;

        static Texture2D pillTexture;
        static Sprite circleSprite;

        static void ApplyPill(GameObject target, Sprite pill)
        {
            var image = target.GetComponent<Image>();
            image.sprite = pill;
            image.type = Image.Type.Sliced;   // the caps hold their shape, the middle stretches
        }

        /// <summary>
        /// White rounded-rectangle sprite for a bar of the given height. Pixels-per-unit comes
        /// from that height so the nine-slice caps land at roughly half of it: a pill at any
        /// width. Unity shrinks the borders itself once the bar is narrower than they are, so a
        /// fill near zero collapses cleanly instead of overlapping.
        /// </summary>
        static Sprite PillSprite(float barHeight)
        {
            if (pillTexture == null)
                pillTexture = BuildRoundedTexture("Runtime UI Pill", PillCornerRadius);

            var pixelsPerUnit = RoundedTextureSize / Mathf.Max(1f, barHeight);
            var border = new Vector4(PillCornerRadius, PillCornerRadius, PillCornerRadius, PillCornerRadius);

            var sprite = Sprite.Create(
                pillTexture,
                new Rect(0f, 0f, RoundedTextureSize, RoundedTextureSize),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit,
                0,
                SpriteMeshType.FullRect,
                border);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        /// <summary>White disc sprite, drawn unsliced at whatever size the rect asks for.</summary>
        static Sprite CircleSprite()
        {
            if (circleSprite == null)
            {
                var texture = BuildRoundedTexture("Runtime UI Circle", RoundedTextureSize * 0.5f - 0.5f);
                circleSprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, RoundedTextureSize, RoundedTextureSize),
                    new Vector2(0.5f, 0.5f),
                    100f,
                    0,
                    SpriteMeshType.FullRect);
                circleSprite.hideFlags = HideFlags.HideAndDontSave;
            }

            return circleSprite;
        }

        static Texture2D BuildRoundedTexture(string name, float cornerRadius)
        {
            var texture = new Texture2D(RoundedTextureSize, RoundedTextureSize, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                // Survives the Resources.UnloadUnusedAssets that runs on every scene load.
                hideFlags = HideFlags.HideAndDontSave,
            };

            var pixels = new Color32[RoundedTextureSize * RoundedTextureSize];
            var half = RoundedTextureSize * 0.5f;
            var straight = half - cornerRadius;   // half-extent of the flat part, before the corners

            for (var y = 0; y < RoundedTextureSize; y++)
            {
                for (var x = 0; x < RoundedTextureSize; x++)
                {
                    // Signed distance to a rounded rectangle centred in the texture.
                    var dx = Mathf.Abs(x + 0.5f - half) - straight;
                    var dy = Mathf.Abs(y + 0.5f - half) - straight;
                    var outside = new Vector2(Mathf.Max(dx, 0f), Mathf.Max(dy, 0f)).magnitude;
                    var distance = outside + Mathf.Min(Mathf.Max(dx, dy), 0f) - cornerRadius;

                    // One pixel of coverage across the edge keeps it smooth at any drawn size.
                    var alpha = Mathf.Clamp01(0.5f - distance);
                    pixels[y * RoundedTextureSize + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
