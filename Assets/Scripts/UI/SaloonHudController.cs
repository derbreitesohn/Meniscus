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

            // No backdrop: the status line sits straight on the scene, pinned to the top-left. A warm
            // dark outline + drop shadow stand in for the old dark card so it stays legible.
            var textObject = new GameObject("Status", typeof(RectTransform), typeof(Text), typeof(Shadow), typeof(Outline));
            textObject.transform.SetParent(hudCanvas.transform, false);

            var textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0f, 1f);
            textRect.anchorMax = new Vector2(0f, 1f);
            textRect.pivot = new Vector2(0f, 1f);
            textRect.sizeDelta = new Vector2(840f, 54f);
            textRect.anchoredPosition = new Vector2(28f, -28f);

            statusText = textObject.GetComponent<Text>();
            statusText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            statusText.fontSize = 22;
            statusText.fontStyle = FontStyle.Bold;
            statusText.alignment = TextAnchor.MiddleLeft;
            statusText.color = new Color(0.96f, 0.86f, 0.62f);

            var shadow = textObject.GetComponent<Shadow>();
            shadow.effectColor    = new Color(0f, 0f, 0f, 0.8f);
            shadow.effectDistance = new Vector2(2f, -2f);

            var outline = textObject.GetComponent<Outline>();
            outline.effectColor    = new Color(0.10f, 0.05f, 0.02f, 0.95f);
            outline.effectDistance = new Vector2(1.4f, 1.4f);
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
