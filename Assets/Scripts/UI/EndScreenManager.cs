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
        [SerializeField] Button restartButton;
        [SerializeField] GameManager gameManager;

        void Awake()
        {
            ResolveReferences();
            WireRestartButton();
            Hide();
        }

        public void Configure(Canvas canvas, Text title, Text detail)
        {
            Configure(canvas, title, detail, null, null);
        }

        public void Configure(
            Canvas canvas,
            Text title,
            Text detail,
            Button restart,
            GameManager manager)
        {
            endCanvas = canvas;
            titleText = title;
            detailText = detail;
            restartButton = restart;
            gameManager = manager;
            ResolveReferences();
            WireRestartButton();
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
                new Vector2(0f, -28f),
                new Vector2(900f, 100f),
                26,
                new Color(0.82f, 0.72f, 0.58f));

            var restartButton = CreateUiButton(
                scrim.transform,
                "Runtime Restart Button",
                "RESTART",
                new Vector2(0f, -148f),
                new Vector2(220f, 58f));

            manager.Configure(canvas, title, detail, restartButton, null);
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

        public void RestartMatch()
        {
            ResolveReferences();
            Hide();

            if (gameManager == null)
            {
                Debug.LogWarning("[EndScreenManager] Restart ignored: GameManager reference is missing.");
                return;
            }

            Debug.Log("[EndScreenManager] Restart button pressed. Starting new match.");
            gameManager.StartMatch();
        }

        void ResolveReferences()
        {
            if (gameManager == null)
                gameManager = FindAnyObjectByType<GameManager>();
        }

        void WireRestartButton()
        {
            if (restartButton == null)
                return;

            restartButton.onClick.RemoveListener(RestartMatch);
            restartButton.onClick.AddListener(RestartMatch);
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

        static Button CreateUiButton(
            Transform parent,
            string name,
            string label,
            Vector2 position,
            Vector2 size)
        {
            var buttonObject = CreateUiImage(
                parent,
                name,
                size,
                position,
                new Color(0.18f, 0.08f, 0.04f, 1f));
            var button = buttonObject.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = new Color(0.18f, 0.08f, 0.04f, 1f);
            colors.highlightedColor = new Color(0.34f, 0.15f, 0.07f, 1f);
            colors.pressedColor = new Color(0.08f, 0.03f, 0.02f, 1f);
            button.colors = colors;

            var labelText = CreateUiText(
                buttonObject.transform,
                "Label",
                label,
                Vector2.zero,
                size,
                22,
                new Color(0.98f, 0.86f, 0.58f));
            labelText.fontStyle = FontStyle.Bold;

            return button;
        }
    }
}
