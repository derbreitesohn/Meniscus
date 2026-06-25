using Meniscus.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Meniscus.UI
{
    [DisallowMultipleComponent]
    public class SaloonHudController : MonoBehaviour
    {
        [SerializeField] GameManager gameManager;
        [SerializeField] Canvas hudCanvas;
        [SerializeField] Text statusText;
        [SerializeField, Min(0.05f)] float refreshSeconds = 0.1f;

        float nextRefreshTime;

        void Awake()
        {
            ResolveReferences();
            EnsureFallbackHud();
            Refresh();
        }

        void Update()
        {
            if (Time.unscaledTime < nextRefreshTime)
                return;

            nextRefreshTime = Time.unscaledTime + refreshSeconds;
            Refresh();
        }

        public void Configure(Canvas canvas, Text text, GameManager manager)
        {
            if (canvas != null)
                hudCanvas = canvas;

            if (text != null)
                statusText = text;

            gameManager = manager;
            EnsureFallbackHud();
            Refresh();
        }

        void Refresh()
        {
            ResolveReferences();

            if (statusText == null || gameManager == null)
                return;

            // Friendly turn label only during the two pour turns; other phases (shop, resolution, round
            // change) show just the round header rather than narrating internal state.
            var turnLabel = gameManager.CurrentState switch
            {
                GameState.PlayerTurn => "Your turn",
                GameState.EnemyTurn  => "Dealer's turn",
                _ => string.Empty,
            };

            // Before the first round is entered CurrentRound is 0; show "1" so the header never reads 0.
            statusText.text = BuildStatusLine(
                Mathf.Max(1, gameManager.CurrentRound),
                GameConstants.TotalRounds,
                turnLabel);
        }

        void ResolveReferences()
        {
            if (gameManager == null)
                gameManager = FindAnyObjectByType<GameManager>();
        }

        void EnsureFallbackHud()
        {
            if (hudCanvas != null && statusText != null)
                return;

            hudCanvas = RuntimeUiFactory.CreateOverlayCanvas(transform, "Runtime Saloon HUD Canvas");

            // No backdrop: the readout sits straight on the scene, pinned to the top-centre. A warm dark
            // outline + drop shadow stand in for a card so it stays legible. Rich text sizes the two lines.
            var textObject = new GameObject("Status", typeof(RectTransform), typeof(Text), typeof(Shadow), typeof(Outline));
            textObject.transform.SetParent(hudCanvas.transform, false);

            var textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0.5f, 1f);
            textRect.anchorMax = new Vector2(0.5f, 1f);
            textRect.pivot = new Vector2(0.5f, 1f);
            textRect.sizeDelta = new Vector2(420f, 78f);
            textRect.anchoredPosition = new Vector2(0f, -24f);

            statusText = textObject.GetComponent<Text>();
            statusText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            statusText.fontSize = 22;
            statusText.fontStyle = FontStyle.Bold;
            statusText.alignment = TextAnchor.MiddleCenter;
            statusText.horizontalOverflow = HorizontalWrapMode.Overflow;
            statusText.verticalOverflow = VerticalWrapMode.Overflow;
            statusText.color = new Color(0.96f, 0.86f, 0.62f);

            var shadow = textObject.GetComponent<Shadow>();
            shadow.effectColor    = new Color(0f, 0f, 0f, 0.8f);
            shadow.effectDistance = new Vector2(2f, -2f);

            var outline = textObject.GetComponent<Outline>();
            outline.effectColor    = new Color(0.10f, 0.05f, 0.02f, 0.95f);
            outline.effectDistance = new Vector2(1.4f, 1.4f);
        }

        /// <summary>
        /// The at-a-glance match readout: which round we're on (the headline) and, while it's a pour turn,
        /// whose turn it is. Deliberately no spill odds — the danger is read off the glass itself, not a
        /// percentage — and no money (the wallet HUD owns that). An empty turnLabel shows only the round
        /// (e.g. during the shop or a resolution beat). Rich text sizes the lines.
        /// </summary>
        public static string BuildStatusLine(int currentRound, int totalRounds, string turnLabel)
        {
            var header = $"<size=30><b>ROUND {currentRound} / {totalRounds}</b></size>";

            return string.IsNullOrEmpty(turnLabel)
                ? header
                : header + $"\n<size=18><color=#C9A45Eff>{turnLabel}</color></size>";
        }
    }
}
