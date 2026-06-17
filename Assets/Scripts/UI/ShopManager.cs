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

        void Awake()
        {
            ResolveReferences();
            HideShop();
        }

        public void Configure(Canvas canvas, EconomyManager economy, GameManager manager)
        {
            shopCanvas = canvas;
            economyManager = economy;
            gameManager = manager;
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

        public bool BuyItemPlaceholder(int cost)
        {
            ResolveReferences();

            if (economyManager == null)
            {
                Debug.LogWarning("[ShopManager] Cannot buy item: EconomyManager reference is missing.");
                return false;
            }

            if (!economyManager.TrySpendBankedCash(cost))
            {
                Debug.LogWarning(
                    $"[ShopManager] Purchase failed. Cost={cost}, banked={economyManager.PlayerTotalBankedCash}.");
                return false;
            }

            Debug.Log(
                $"[ShopManager] Purchased placeholder item for {cost}. " +
                $"Banked cash remaining={economyManager.PlayerTotalBankedCash}.");
            return true;
        }

        public void BuyCheapItemPlaceholder()
        {
            BuyItemPlaceholder(25);
        }

        public void BuyPremiumItemPlaceholder()
        {
            BuyItemPlaceholder(75);
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

            CreateButton(card.transform, "Cheap Item Button", "BUY $25", new Vector2(-105f, -24f), BuyCheapItemPlaceholder);
            CreateButton(card.transform, "Premium Item Button", "BUY $75", new Vector2(105f, -24f), BuyPremiumItemPlaceholder);
            CreateButton(card.transform, "Finish Drink Button", "FINISH DRINK", new Vector2(0f, -124f), FinishOrdering);

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
