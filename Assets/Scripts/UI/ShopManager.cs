using System.Collections.Generic;
using Meniscus.Core;
using Meniscus.Items;
using UnityEngine;

namespace Meniscus.UI
{
    [DisallowMultipleComponent]
    public class ShopManager : MonoBehaviour
    {
        [SerializeField] Canvas shopCanvas;
        [SerializeField] EconomyManager economyManager;
        [SerializeField] GameManager gameManager;
        [SerializeField] PlayerInventory playerInventory;

        [Tooltip("Authored items. Leave empty to use the code-built default catalog.")]
        [SerializeField] List<ItemDefinition> itemCatalog = new();

        [Header("Presentation")]
        [Tooltip("When on, the shop opens as the diegetic book on the desk instead of a screen overlay.")]
        [SerializeField] bool useDiegeticBookShop = true;
        [SerializeField] BookShopView bookShop;

        List<ItemDefinition> resolvedCatalog;

        public IReadOnlyList<ItemDefinition> Catalog => ResolveCatalog();

        /// <summary>
        /// Whether the player may open the book to browse right now: any time a round is in
        /// progress, but not during the post-round shop phase (closed via "Finish Drink") or on
        /// the game-over screen.
        /// </summary>
        public static bool BrowsingAllowed(GameState state)
            => state != GameState.ShopPhase && state != GameState.GameOver;

        /// <summary>Banked cash the player can spend right now (0 if economy is unwired).</summary>
        public int BankedCash => economyManager != null ? economyManager.PlayerTotalBankedCash : 0;

        /// <summary>True when the desk cannot hold any more items.</summary>
        public bool IsDeskFull => playerInventory != null && playerInventory.IsFull;

        /// <summary>How many of <paramref name="item"/> the player already owns.</summary>
        public int OwnedCount(ItemDefinition item)
        {
            if (item == null || playerInventory == null)
                return 0;

            foreach (var stack in playerInventory.Contents())
                if (stack.Item == item)
                    return stack.Count;

            return 0;
        }

        void Awake()
        {
            ResolveReferences();
            HideShop();
        }

        void Start()
        {
            // Seat the diegetic book on the desk from the start of the match so it is a visible prop
            // during the rounds and is lifted from there when the shop opens, rather than spawning.
            if (!useDiegeticBookShop)
                return;

            EnsureBookShop();
            WireBrowseGate();

            if (bookShop != null)
                bookShop.PrepareOnDesk(Catalog, this);
        }

        public void Configure(
            Canvas canvas,
            EconomyManager economy,
            GameManager manager)
        {
            shopCanvas = canvas;
            economyManager = economy;
            gameManager = manager;
            HideShop();
        }

        public void ShowShop()
        {
            ResolveReferences();

            if (useDiegeticBookShop)
            {
                EnsureBookShop();
                WireBrowseGate();

                if (bookShop != null)
                {
                    HideShopCanvas();
                    bookShop.Open(Catalog, this);
                    return;
                }
            }

            EnsureFallbackShopCanvas();

            if (shopCanvas != null)
                shopCanvas.enabled = true;
        }

        public void HideShop()
        {
            HideShopCanvas();

            if (bookShop != null)
                bookShop.Close();
        }

        void HideShopCanvas()
        {
            if (shopCanvas != null)
                shopCanvas.enabled = false;
        }

        /// <summary>
        /// Attempts to buy an item: charges banked cash and grants it to the player's desk inventory.
        /// Returns false (and changes nothing) when the item is null, the desk is full, or it is
        /// unaffordable.
        /// </summary>
        public bool TryBuyItem(ItemDefinition item)
        {
            ResolveReferences();

            if (item == null)
            {
                Debug.LogWarning("[ShopManager] Ignored purchase of a null item.");
                return false;
            }

            if (playerInventory == null)
            {
                Debug.LogWarning($"[ShopManager] Cannot buy {item.DisplayName}: PlayerInventory is missing.");
                return false;
            }

            if (playerInventory.IsFull)
            {
                Debug.LogWarning(
                    $"[ShopManager] Desk is full ({playerInventory.TotalCount}/{GameConstants.DeskCapacity}); " +
                    $"cannot buy {item.DisplayName}.");
                return false;
            }

            if (!TrySpend(item.DisplayName, item.Cost))
                return false;

            playerInventory.Grant(item);
            return true;
        }

        public bool TryBuyItemById(string id)
        {
            var item = FindItem(id);

            if (item == null)
            {
                Debug.LogWarning($"[ShopManager] No catalog item with id '{id}'.");
                return false;
            }

            return TryBuyItem(item);
        }

        // Thin wrappers so authored scene buttons (which call these by name) keep working until the
        // diegetic book shop replaces the presentation.
        public void BuyMarkedCoin() => TryBuyItemById("marked_coin");
        public void BuySteadyHand() => TryBuyItemById("steady_hand");
        public void BuyDealersDebt() => TryBuyItemById("dealers_debt");

        public void FinishOrdering()
        {
            ResolveReferences();
            HideShop();

            if (gameManager != null)
                gameManager.FinishShopPhase();
            else
                Debug.LogWarning("[ShopManager] Cannot finish ordering: GameManager reference is missing.");
        }

        List<ItemDefinition> ResolveCatalog()
        {
            if (resolvedCatalog != null)
                return resolvedCatalog;

            resolvedCatalog = new List<ItemDefinition>();

            for (var i = 0; i < itemCatalog.Count; i++)
            {
                if (itemCatalog[i] != null)
                    resolvedCatalog.Add(itemCatalog[i]);
            }

            if (resolvedCatalog.Count == 0)
                resolvedCatalog = ShopCatalog.CreateDefaultCatalog();

            return resolvedCatalog;
        }

        ItemDefinition FindItem(string id)
        {
            var catalog = ResolveCatalog();

            for (var i = 0; i < catalog.Count; i++)
            {
                if (catalog[i] != null && catalog[i].Id == id)
                    return catalog[i];
            }

            return null;
        }

        void ResolveReferences()
        {
            if (economyManager == null)
                economyManager = FindAnyObjectByType<EconomyManager>();

            if (gameManager == null)
                gameManager = FindAnyObjectByType<GameManager>();

            if (playerInventory == null && gameManager != null)
                playerInventory = gameManager.Inventory;

            if (playerInventory == null)
                playerInventory = FindAnyObjectByType<PlayerInventory>();
        }

        bool TrySpend(string itemName, int cost)
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

            return true;
        }

        void EnsureBookShop()
        {
            if (bookShop == null)
                bookShop = FindAnyObjectByType<BookShopView>();

            if (bookShop == null)
                bookShop = new GameObject("Runtime Book Shop").AddComponent<BookShopView>();
        }

        void WireBrowseGate()
        {
            if (bookShop != null)
                bookShop.SetBrowseGate(() => gameManager == null || BrowsingAllowed(gameManager.CurrentState));
        }

        void EnsureFallbackShopCanvas()
        {
            if (shopCanvas != null)
                return;

            shopCanvas = CreateFallbackShopCanvas();
        }

        Canvas CreateFallbackShopCanvas()
        {
            var canvas = RuntimeUiFactory.CreateOverlayCanvas(transform, "Runtime Saloon Menu Canvas", enabled: false);

            var scrim = RuntimeUiFactory.CreateImage(
                canvas.transform,
                "Shop Table Dimming Scrim",
                new Vector2(1920f, 1080f),
                Vector2.zero,
                new Color(0.02f, 0.012f, 0.008f, 0.45f));

            var catalog = ResolveCatalog();

            const float headerHeight = 150f;
            const float rowHeight = 58f;
            const float footerHeight = 96f;
            var cardWidth = 470f;
            var cardHeight = headerHeight + catalog.Count * rowHeight + footerHeight;

            var card = RuntimeUiFactory.CreateImage(
                scrim.transform,
                "Greasy Saloon Menu Card",
                new Vector2(cardWidth, cardHeight),
                Vector2.zero,
                new Color(0.56f, 0.43f, 0.25f, 0.97f));

            var cardTextColor = new Color(0.08f, 0.045f, 0.025f);
            var top = cardHeight * 0.5f;

            RuntimeUiFactory.CreateText(
                card.transform, "Title", "SALOON MENU",
                new Vector2(0f, top - 48f), new Vector2(cardWidth - 60f, 44f), 32, cardTextColor);
            RuntimeUiFactory.CreateText(
                card.transform, "Description", "Spend banked cash between rounds.",
                new Vector2(0f, top - 92f), new Vector2(cardWidth - 60f, 32f), 18, cardTextColor);

            var rowY = top - headerHeight;
            var buttonSize = new Vector2(cardWidth - 70f, rowHeight - 12f);

            for (var i = 0; i < catalog.Count; i++)
            {
                var item = catalog[i];
                var label = $"{item.DisplayName.ToUpperInvariant()}   ${item.Cost}";

                RuntimeUiFactory.CreateButton(
                    card.transform,
                    $"{item.Id} Button",
                    label,
                    buttonSize,
                    new Vector2(0f, rowY),
                    17,
                    () => TryBuyItem(item));

                rowY -= rowHeight;
            }

            RuntimeUiFactory.CreateButton(
                card.transform, "Finish Drink Button", "FINISH DRINK",
                new Vector2(200f, 50f), new Vector2(0f, -top + 48f), 18, FinishOrdering, boldLabel: true);

            return canvas;
        }
    }
}
