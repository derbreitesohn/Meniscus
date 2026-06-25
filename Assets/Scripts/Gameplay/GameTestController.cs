using System.Collections.Generic;
using Meniscus.Core;
using Meniscus.Items;
using Meniscus.UI;
using UnityEngine;

namespace Meniscus.Gameplay
{
    public class GameTestController : MonoBehaviour
    {
        [Tooltip("DEBUG: top-left panel listing every catalog item; click one to use it free this turn " +
                 "(applies the effect and plays its use performance, no cash spent, no inventory needed).")]
        [SerializeField] bool showItemTestMenu = false;
        [SerializeField] bool showDebugOverlay;
        [SerializeField] GameManager gameManager;
        [SerializeField] GlassManager glassManager;
        [SerializeField] EconomyManager economyManager;
        [SerializeField] ShopManager shopManager;

        // Cached so OnGUI (called several times a frame) does not rebuild the default catalog each pass.
        IReadOnlyList<ItemDefinition> cachedCatalog;

        void Awake()
        {
            ResolveReferences();
        }

        void OnGUI()
        {
            ResolveReferences();

            if (showItemTestMenu)
                DrawItemTestMenu();

            if (showDebugOverlay)
                DrawDebugOverlay();
        }

        // Top-left: one button per catalog item that uses it for free on the player's turn. Faithful to a
        // real use (effect + use performance) via GameManager.DebugUseItemFree, just without ownership/cost.
        void DrawItemTestMenu()
        {
            if (gameManager == null)
                return;

            var catalog = ResolveCatalog();

            if (catalog == null || catalog.Count == 0)
                return;

            const int width = 260;
            const int rowH = 24;
            var x = 16;
            var y = 16;

            var boxHeight = (catalog.Count + 3) * rowH + 16;
            GUI.Box(new Rect(x - 8, y - 8, width + 16, boxHeight), "Item Test — free use");
            y += 4;

            GUI.Label(new Rect(x, y, width, rowH), "Click to use this turn (free):");
            y += rowH;

            var busy = gameManager.ItemPresentationActive;
            var canUse = gameManager.CurrentState == GameState.PlayerTurn && !busy;
            GUI.enabled = canUse;

            for (var i = 0; i < catalog.Count; i++)
            {
                var item = catalog[i];

                if (item == null)
                    continue;

                if (GUI.Button(new Rect(x, y, width, rowH - 2), item.DisplayName))
                    gameManager.DebugUseItemFree(item);

                y += rowH;
            }

            GUI.enabled = true;

            if (!canUse)
                GUI.Label(new Rect(x, y, width, rowH),
                    busy ? "(busy — performance playing)" : "(only on your turn)");
        }

        // The catalog the shop is actually using (so authored lists are honoured), falling back to the
        // code-built default when no shop is present. ShopManager.Catalog caches its own resolution.
        IReadOnlyList<ItemDefinition> ResolveCatalog()
        {
            if (cachedCatalog != null && cachedCatalog.Count > 0)
                return cachedCatalog;

            cachedCatalog = shopManager != null ? shopManager.Catalog : ShopCatalog.CreateDefaultCatalog();
            return cachedCatalog;
        }

        void DrawDebugOverlay()
        {
            const int width = 340;
            const int height = 26;
            var x = 16;
            var y = Screen.height - 300;

            GUI.Box(new Rect(x - 8, y - 8, width + 16, 286), "Meniscus Debug");

            if (gameManager == null)
            {
                GUI.Label(new Rect(x, y, width, height), "No GameManager found.");
                return;
            }

            GUI.Label(new Rect(x, y, width, height), $"State: {gameManager.CurrentState}");
            y += height;
            GUI.Label(new Rect(x, y, width, height), $"Round: {gameManager.CurrentRound}/{GameConstants.TotalRounds}");
            y += height;
            GUI.Label(new Rect(x, y, width, height), $"Overflow Risk: {GetRisk():0.##}%");
            y += height;
            GUI.Label(new Rect(x, y, width, height), $"Round Earnings: {GetRoundEarnings()}");
            y += height;
            GUI.Label(new Rect(x, y, width, height), $"Banked Cash: {GetBankedCash()}");
            y += height;
            GUI.Label(new Rect(x, y, width, height), $"Player Coins: {gameManager.PlayerCoins.Count}");
            y += height;
            GUI.Label(new Rect(x, y, width, height), $"Enemy Coins: {gameManager.EnemyCoins.Count}");
            y += height + 6;

            if (gameManager.CurrentState == GameState.ShopPhase)
            {
                if (GUI.Button(new Rect(x, y, width, height), "Finish Ordering"))
                    gameManager.FinishShopPhase();

                y += height + 4;
            }

            if (gameManager.CurrentState == GameState.GameOver)
            {
                if (GUI.Button(new Rect(x, y, width, height), "Start New Match"))
                    gameManager.StartMatch();
            }
        }

        void ResolveReferences()
        {
            if (gameManager == null)
                gameManager = FindAnyObjectByType<GameManager>();

            if (glassManager == null)
                glassManager = FindAnyObjectByType<GlassManager>();

            if (economyManager == null)
                economyManager = FindAnyObjectByType<EconomyManager>();

            if (shopManager == null)
                shopManager = FindAnyObjectByType<ShopManager>();
        }

        float GetRisk() =>
            glassManager == null ? 0f : glassManager.CurrentOverflowProbability;

        int GetRoundEarnings() =>
            economyManager == null ? 0 : economyManager.CurrentRoundEarnings;

        int GetBankedCash() =>
            economyManager == null ? 0 : economyManager.PlayerTotalBankedCash;
    }
}
