using Meniscus.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Meniscus.UI
{
    [DisallowMultipleComponent]
    public class ShopManager : MonoBehaviour
    {
        [SerializeField] Canvas shopCanvas;
        [SerializeField] EconomyManager economyManager;
        [SerializeField] GameManager gameManager;
        [SerializeField] GlassManager glassManager;

        void Awake()
        {
            ResolveReferences();
            HideShop();
        }

        public void Configure(
            Canvas canvas,
            EconomyManager economy,
            GameManager manager,
            GlassManager glass = null)
        {
            shopCanvas = canvas;
            economyManager = economy;
            gameManager = manager;
            glassManager = glass;
            HideShop();
        }

        public void ShowShop()
        {
            ResolveReferences();
            EnsureFallbackShopCanvas();

            if (shopCanvas != null)
                shopCanvas.enabled = true;

            Debug.Log(
                $"[ShopManager] Saloon Menu Card opened. Banked cash={GetBankedCashForLog()}.");
        }

        public void HideShop()
        {
            if (shopCanvas != null)
                shopCanvas.enabled = false;

            Debug.Log("[ShopManager] Saloon Menu Card closed.");
        }

        public void BuyMarkedCoin()
        {
            ResolveReferences();

            if (!TryBuy("Marked Coin", 35))
                return;

            economyManager.QueueNextSafeDropPayoutMultiplier(2f);
            Debug.Log("[ShopManager] Marked Coin armed. Next safe player drop pays double.");
        }

        public void BuySteadyHand()
        {
            ResolveReferences();

            if (!TryBuy("Steady Hand", 50))
                return;

            if (glassManager == null)
            {
                Debug.LogWarning("[ShopManager] Bought Steady Hand, but GlassManager is missing.");
                return;
            }

            glassManager.QueueNextRoundSafeZoneBonus(10f);
            Debug.Log("[ShopManager] Steady Hand bought. Next round safe zone extends by 10%.");
        }

        public void BuyDealersDebt()
        {
            ResolveReferences();

            if (!TryBuy("Dealer's Debt", 60))
                return;

            if (gameManager == null)
            {
                Debug.LogWarning("[ShopManager] Bought Dealer's Debt, but GameManager is missing.");
                return;
            }

            gameManager.QueueEnemyForcedCoinCount(2);
            Debug.Log("[ShopManager] Dealer's Debt bought. Dealer must drop 2 coins on next enemy turn.");
        }

        public void FinishOrdering()
        {
            ResolveReferences();
            HideShop();
            Debug.Log("[ShopManager] Finish Drink pressed. Signaling GameManager.");

            if (gameManager != null)
                gameManager.FinishShopPhase();
            else
                Debug.LogWarning("[ShopManager] Cannot finish ordering: GameManager reference is missing.");
        }

        void ResolveReferences()
        {
            if (economyManager == null)
                economyManager = FindAnyObjectByType<EconomyManager>();

            if (gameManager == null)
                gameManager = FindAnyObjectByType<GameManager>();

            if (glassManager == null)
                glassManager = FindAnyObjectByType<GlassManager>();
        }

        bool TryBuy(string itemName, int cost)
        {
            if (economyManager == null)
            {
                Debug.LogWarning($"[ShopManager] Cannot buy {itemName}: EconomyManager reference is missing.");
                return false;
            }

            if (!economyManager.TrySpendBankedCash(cost))
            {
                Debug.LogWarning(
                    $"[ShopManager] Purchase failed for {itemName}. " +
                    $"Cost={cost}, banked={economyManager.PlayerTotalBankedCash}.");
                return false;
            }

            Debug.Log(
                $"[ShopManager] Purchased {itemName} for {cost}. " +
                $"Banked cash remaining={economyManager.PlayerTotalBankedCash}.");
            return true;
        }

        void EnsureFallbackShopCanvas()
        {
            if (shopCanvas != null)
                return;

            shopCanvas = CreateFallbackShopCanvas();
            Debug.Log("[ShopManager] Created runtime fallback Saloon Menu canvas.");
        }

        Canvas CreateFallbackShopCanvas()
        {
            var canvasObject = new GameObject(
                "Runtime Saloon Menu Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.enabled = false;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var scrim = CreateUiImage(
                canvasObject.transform,
                "Shop Table Dimming Scrim",
                new Vector2(1920f, 1080f),
                Vector2.zero,
                new Color(0.02f, 0.012f, 0.008f, 0.45f));

            var card = CreateUiImage(
                scrim.transform,
                "Greasy Saloon Menu Card",
                new Vector2(470f, 360f),
                new Vector2(0f, -48f),
                new Color(0.56f, 0.43f, 0.25f, 0.97f));

            CreateUiImage(
                card.transform,
                "Coffee Ring Stain",
                new Vector2(118f, 72f),
                new Vector2(-150f, 96f),
                new Color(0.18f, 0.09f, 0.03f, 0.22f));
            CreateUiImage(
                card.transform,
                "Water Stain",
                new Vector2(96f, 54f),
                new Vector2(152f, -92f),
                new Color(0.11f, 0.07f, 0.035f, 0.18f));

            CreateUiText(card.transform, "Title", "SALOON MENU", new Vector2(0f, 124f), 32, TextAnchor.MiddleCenter);
            CreateUiText(
                card.transform,
                "Description",
                "Spend banked cash between rounds.",
                new Vector2(0f, 76f),
                18,
                TextAnchor.MiddleCenter);

            CreateButton(card.transform, "Marked Coin Button", "MARKED $35", new Vector2(-112f, -12f), BuyMarkedCoin);
            CreateButton(card.transform, "Steady Hand Button", "STEADY $50", new Vector2(112f, -12f), BuySteadyHand);
            CreateButton(card.transform, "Dealer Debt Button", "DEBT $60", new Vector2(0f, -72f), BuyDealersDebt);
            CreateButton(card.transform, "Finish Drink Button", "FINISH DRINK", new Vector2(0f, -132f), FinishOrdering);

            return canvas;
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

        static GameObject CreateUiText(
            Transform parent,
            string name,
            string text,
            Vector2 position,
            int fontSize,
            TextAnchor alignment)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);

            var rectTransform = textObject.GetComponent<RectTransform>();
            rectTransform.sizeDelta = new Vector2(390f, 48f);
            rectTransform.anchoredPosition = position;

            var uiText = textObject.GetComponent<Text>();
            uiText.text = text;
            uiText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            uiText.fontSize = fontSize;
            uiText.alignment = alignment;
            uiText.color = new Color(0.08f, 0.045f, 0.025f);

            return textObject;
        }

        static void CreateButton(Transform parent, string name, string label, Vector2 position, UnityEngine.Events.UnityAction action)
        {
            var buttonObject = CreateUiImage(
                parent,
                name,
                new Vector2(170f, 48f),
                position,
                new Color(0.18f, 0.08f, 0.04f, 1f));

            var button = buttonObject.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = new Color(0.18f, 0.08f, 0.04f, 1f);
            colors.highlightedColor = new Color(0.34f, 0.15f, 0.07f, 1f);
            colors.pressedColor = new Color(0.08f, 0.03f, 0.02f, 1f);
            button.colors = colors;
            button.onClick.AddListener(action);

            CreateUiText(buttonObject.transform, "Label", label, Vector2.zero, 17, TextAnchor.MiddleCenter);
            var labelText = buttonObject.transform.Find("Label").GetComponent<Text>();
            labelText.color = new Color(0.98f, 0.86f, 0.58f);
        }

        string GetBankedCashForLog() =>
            economyManager == null ? "unknown" : economyManager.PlayerTotalBankedCash.ToString();
    }
}
