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
                glassManager == null ? 0f : glassManager.CurrentSafeZoneThreshold,
                economyManager == null ? 0 : economyManager.CurrentRoundEarnings,
                economyManager == null ? 0 : economyManager.PlayerTotalBankedCash,
                gameManager.PlayerCoins.Count,
                gameManager.EnemyCoins.Count);
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

            var canvasObject = new GameObject(
                "Runtime Saloon HUD Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);

            hudCanvas = canvasObject.GetComponent<Canvas>();
            hudCanvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var panelObject = new GameObject("HUD Table Card", typeof(RectTransform), typeof(Image));
            panelObject.transform.SetParent(canvasObject.transform, false);

            var panelRect = panelObject.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(24f, -24f);
            panelRect.sizeDelta = new Vector2(720f, 70f);

            panelObject.GetComponent<Image>().color = new Color(0.075f, 0.042f, 0.024f, 0.78f);

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

            Debug.Log("[SaloonHudController] Created runtime fallback HUD canvas.");
        }

        public static string BuildStatusLine(
            GameState state,
            int currentRound,
            int totalRounds,
            float totalRiskWeight,
            float safeZoneThreshold,
            int roundEarnings,
            int bankedCash,
            int playerCoinCount,
            int enemyCoinCount)
        {
            var riskLabel = totalRiskWeight <= safeZoneThreshold
                ? "Risk Building"
                : totalRiskWeight < 75f
                    ? "Danger"
                    : "Critical";

            return
                $"Round {currentRound}/{totalRounds}   {state}   {riskLabel} {totalRiskWeight:0}%   " +
                $"Round ${roundEarnings}   Bank ${bankedCash}   You {playerCoinCount} / Dealer {enemyCoinCount}";
        }
    }
}
