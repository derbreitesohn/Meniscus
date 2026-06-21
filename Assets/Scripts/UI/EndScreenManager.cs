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

            var canvas = RuntimeUiFactory.CreateOverlayCanvas(root.transform, "Runtime End Screen Canvas", enabled: false);

            var scrim = RuntimeUiFactory.CreateImage(
                canvas.transform,
                "Runtime End Screen Scrim",
                new Vector2(1920f, 1080f),
                Vector2.zero,
                new Color(0.02f, 0.012f, 0.008f, 0.9f));

            var title = RuntimeUiFactory.CreateText(
                scrim.transform,
                "Runtime Outcome Title",
                string.Empty,
                new Vector2(0f, 72f),
                new Vector2(900f, 120f),
                74,
                new Color(0.95f, 0.82f, 0.52f),
                TextAnchor.MiddleCenter,
                bold: true);

            var detail = RuntimeUiFactory.CreateText(
                scrim.transform,
                "Runtime Outcome Detail",
                string.Empty,
                new Vector2(0f, -28f),
                new Vector2(900f, 100f),
                26,
                new Color(0.82f, 0.72f, 0.58f));

            var restartButton = RuntimeUiFactory.CreateButton(
                scrim.transform,
                "Runtime Restart Button",
                "RESTART",
                new Vector2(220f, 58f),
                new Vector2(0f, -148f),
                22,
                onClick: null,
                boldLabel: true);

            manager.Configure(canvas, title, detail, restartButton, null);
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
        }

        public void Hide()
        {
            if (endCanvas != null)
                endCanvas.enabled = false;
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
    }
}
