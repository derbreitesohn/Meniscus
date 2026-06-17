using Meniscus.Core;
using UnityEngine;

namespace Meniscus.Gameplay
{
    public class GameTestController : MonoBehaviour
    {
        [SerializeField] bool showDebugOverlay;
        [SerializeField] GameManager gameManager;
        [SerializeField] GlassManager glassManager;
        [SerializeField] EconomyManager economyManager;

        void Awake()
        {
            ResolveReferences();
        }

        void OnGUI()
        {
            if (!showDebugOverlay)
                return;

            ResolveReferences();

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
        }

        float GetRisk() =>
            glassManager == null ? 0f : glassManager.CurrentOverflowProbability;

        int GetRoundEarnings() =>
            economyManager == null ? 0 : economyManager.CurrentRoundEarnings;

        int GetBankedCash() =>
            economyManager == null ? 0 : economyManager.PlayerTotalBankedCash;
    }
}
