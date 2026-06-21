using Meniscus.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Meniscus.UI
{
    [DisallowMultipleComponent]
    public class SaloonHudController : MonoBehaviour
    {
        [SerializeField] GameManager gameManager;
        [SerializeField] GlassManager glassManager;
        [SerializeField] EconomyManager economyManager;
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

        public void Configure(
            Canvas canvas,
            Text text,
            GameManager manager,
            GlassManager glass,
            EconomyManager economy)
        {
            if (canvas != null)
                hudCanvas = canvas;

            if (text != null)
                statusText = text;

            gameManager = manager;
            glassManager = glass;
            economyManager = economy;
            EnsureFallbackHud();
            Refresh();
        }

        void Refresh()
        {
            ResolveReferences();

            if (statusText == null || gameManager == null)
                return;

            statusText.text = BuildStatusLine(
                gameManager.CurrentState,
                gameManager.CurrentRound,
                GameConstants.TotalRounds,
                glassManager == null ? 0f : glassManager.CurrentOverflowProbability,
                economyManager == null ? 0 : economyManager.CurrentRoundEarnings,
                economyManager == null ? 0 : economyManager.PlayerTotalBankedCash,
                gameManager.PlayerCoins.Count,
                gameManager.EnemyCoins.Count,
                glassManager != null && glassManager.TrueOddsRevealed,
                glassManager == null ? 0f : glassManager.CurrentTrueSpillChance);
        }

        void ResolveReferences()
        {
            if (gameManager == null)
                gameManager = FindAnyObjectByType<GameManager>();

            if (glassManager == null)
                glassManager = FindAnyObjectByType<GlassManager>();

            if (economyManager == null)
                economyManager = FindAnyObjectByType<EconomyManager>();
        }

        void EnsureFallbackHud()
        {
            if (hudCanvas != null && statusText != null)
                return;

            hudCanvas = RuntimeUiFactory.CreateOverlayCanvas(transform, "Runtime Saloon HUD Canvas");

            var panelObject = RuntimeUiFactory.CreateImage(
                hudCanvas.transform,
                "HUD Table Card",
                new Vector2(720f, 70f),
                new Vector2(24f, -24f),
                new Color(0.075f, 0.042f, 0.024f, 0.78f));

            var panelRect = panelObject.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);

            var textObject = new GameObject("Status", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(panelObject.transform, false);

            var textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(18f, 8f);
            textRect.offsetMax = new Vector2(-18f, -8f);

            statusText = textObject.GetComponent<Text>();
            statusText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            statusText.fontSize = 20;
            statusText.alignment = TextAnchor.MiddleLeft;
            statusText.color = new Color(0.95f, 0.84f, 0.62f);
        }

        public static string BuildStatusLine(
            GameState state,
            int currentRound,
            int totalRounds,
            float totalRiskWeight,
            int roundEarnings,
            int bankedCash,
            int playerCoinCount,
            int enemyCoinCount,
            bool revealTrueOdds = false,
            float trueSpillChance = 0f)
        {
            var riskLabel = totalRiskWeight < 30f
                ? "Steady"
                : totalRiskWeight < 60f
                    ? "Risk Building"
                    : totalRiskWeight < 85f
                        ? "Danger"
                        : "Critical";

            var spillSuffix = revealTrueOdds ? $"   Spill {trueSpillChance:0}%" : string.Empty;

            return
                $"Round {currentRound}/{totalRounds}   {state}   {riskLabel} {totalRiskWeight:0}%   " +
                $"Round ${roundEarnings}   Bank ${bankedCash}   You {playerCoinCount} / Dealer {enemyCoinCount}" +
                spillSuffix;
        }
    }
}
