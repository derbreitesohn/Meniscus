using System.Collections.Generic;
using Meniscus.Core;
using Meniscus.Items;
using Meniscus.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Meniscus.Gameplay
{
    public class GameTestController : MonoBehaviour
    {
        [SerializeField] bool showDebugOverlay;
        [SerializeField] GameManager gameManager;
        [SerializeField] GlassManager glassManager;
        [SerializeField] EconomyManager economyManager;
        [SerializeField] ShopManager shopManager;

        // Cached so OnGUI (called several times a frame) does not rebuild the default catalog each pass.
        IReadOnlyList<ItemDefinition> cachedCatalog;

        bool menuOpen = false;

        const string MenuSceneName = "MainMenu";
        const int PanelWidth = 276;
        const int RowH = 26;
        const int HeaderH = 28;
        const int PanelX = 8;
        const int PanelY = 8;

        void Awake()
        {
            ResolveReferences();
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1))
                menuOpen = !menuOpen;
        }

        void OnGUI()
        {
            ResolveReferences();
            DrawBugMenu();

            if (showDebugOverlay)
                DrawDebugOverlay();
        }

        void DrawBugMenu()
        {
            var catalog = ResolveCatalog();
            var itemCount = catalog?.Count ?? 0;

            // Always-visible header toggle button.
            var headerLabel = menuOpen ? "▼  Debug Menu" : "▶  Debug Menu";
            if (GUI.Button(new Rect(PanelX, PanelY, PanelWidth, HeaderH), headerLabel))
                menuOpen = !menuOpen;

            if (!menuOpen)
                return;

            var y = PanelY + HeaderH + 2;

            // Background box sized to content.
            var contentRows = itemCount + 3; // items + status line + separator + main menu btn
            var panelH = contentRows * RowH + 12;
            GUI.Box(new Rect(PanelX, y, PanelWidth, panelH), "");

            y += 6;
            var x = PanelX + 8;
            var innerW = PanelWidth - 16;

            // ── Item test section ───────────────────────────────────────────
            GUI.Label(new Rect(x, y, innerW, RowH - 4), "Use item (free, no cost/inventory):");
            y += RowH;

            var busy = gameManager != null && gameManager.ItemPresentationActive;
            var canUse = gameManager != null && gameManager.CurrentState == GameState.PlayerTurn && !busy;
            GUI.enabled = canUse;

            if (catalog != null)
            {
                for (var i = 0; i < catalog.Count; i++)
                {
                    var item = catalog[i];
                    if (item == null) continue;

                    if (GUI.Button(new Rect(x, y, innerW, RowH - 2), item.DisplayName))
                        gameManager.DebugUseItemFree(item);

                    y += RowH;
                }
            }

            GUI.enabled = true;

            GUI.Label(new Rect(x, y, innerW, RowH - 4),
                !canUse ? (busy ? "(busy — wait for animation)" : "(only usable on your turn)") : " ");
            y += RowH;

            // ── Separator ───────────────────────────────────────────────────
            GUI.Box(new Rect(x, y, innerW, 1), "");
            y += 8;

            // ── Main menu button ─────────────────────────────────────────────
            if (GUI.Button(new Rect(x, y, innerW, RowH), "→  Return to Main Menu"))
                SceneManager.LoadScene(MenuSceneName);
        }

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
