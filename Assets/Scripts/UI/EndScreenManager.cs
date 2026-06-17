using Meniscus.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Meniscus.UI
{
    [DisallowMultipleComponent]
    public class EndScreenManager : MonoBehaviour
    {
        [SerializeField] Canvas endCanvas;
        [SerializeField] Text titleText;
        [SerializeField] Text detailText;

        void Awake()
        {
            Hide();
        }

        public void Configure(Canvas canvas, Text title, Text detail)
        {
            endCanvas = canvas;
            titleText = title;
            detailText = detail;
            Hide();
        }

        public static EndScreenManager CreateRuntimeFallback()
        {
            var root = new GameObject("Runtime End Screen Manager");
            var manager = root.AddComponent<EndScreenManager>();

            var canvasObject = new GameObject(
                "Runtime End Screen Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(root.transform, false);

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.enabled = false;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var scrim = CreateUiImage(
                canvasObject.transform,
                "Runtime End Screen Scrim",
                new Vector2(1920f, 1080f),
                Vector2.zero,
                new Color(0.02f, 0.012f, 0.008f, 0.9f));

            var title = CreateUiText(
                scrim.transform,
                "Runtime Outcome Title",
                string.Empty,
                new Vector2(0f, 72f),
                new Vector2(900f, 120f),
                74,
                new Color(0.95f, 0.82f, 0.52f));
            title.fontStyle = FontStyle.Bold;

            var detail = CreateUiText(
                scrim.transform,
                "Runtime Outcome Detail",
                string.Empty,
                new Vector2(0f, -36f),
                new Vector2(900f, 100f),
                26,
                new Color(0.82f, 0.72f, 0.58f));

            manager.Configure(canvas, title, detail);
            Debug.Log("[EndScreenManager] Created runtime fallback end screen.");
            return manager;
        }

        public void ShowOutcome(MatchOutcome outcome, string reason, int bankedCash)
        {
            if (outcome == MatchOutcome.None)
            {
                Hide();
                return;
            }

            if (endCanvas != null)
                endCanvas.enabled = true;

            if (titleText != null)
                titleText.text = outcome == MatchOutcome.PlayerWon ? "YOU WON" : "YOU LOST";

            if (detailText != null)
                detailText.text = $"Reason: {reason}\nBanked cash: ${bankedCash}";

            Debug.Log(
                $"[EndScreenManager] Showing outcome={outcome}, reason={reason}, bankedCash={bankedCash}.");
        }

        public void Hide()
        {
            if (endCanvas != null)
                endCanvas.enabled = false;

            Debug.Log("[EndScreenManager] End screen hidden.");
        }

        static GameObject CreateUiImage(Transform parent, string name, Vector2 size, Vector2 position, Color color)
        {
            var imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(parent, false);

            var rectTransform = imageObject.GetComponent<RectTransform>();
            rectTransform.sizeDelta = size;
            rectTransform.anchoredPosition = position;

            imageObject.GetComponent<Image>().color = color;
            return imageObject;
        }

        static Text CreateUiText(
            Transform parent,
            string name,
            string text,
            Vector2 position,
            Vector2 size,
            int fontSize,
            Color color)
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
            uiText.alignment = TextAnchor.MiddleCenter;
            uiText.color = color;
            return uiText;
        }
    }
}
