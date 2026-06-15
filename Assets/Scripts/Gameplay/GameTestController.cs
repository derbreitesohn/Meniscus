using Meniscus.Core;
using UnityEngine;

namespace Meniscus.Gameplay
{
    public class GameTestController : MonoBehaviour
    {
        [SerializeField] GameStateManager stateManager;

        int selectedBet = GameConstants.DefaultBet;

        void Awake()
        {
            stateManager = GetComponent<GameStateManager>();
            if (stateManager == null)
                stateManager = FindAnyObjectByType<GameStateManager>();
        }

        void OnGUI()
        {
            if (stateManager == null)
                return;

            var state = stateManager.State;
            const int width = 280;
            const int height = 32;
            var x = 16;
            var y = Screen.height - 320;

            GUI.Box(new Rect(x - 8, y - 8, width + 16, 340), "Meniscus");

            if (state.Phase == GamePhase.BetSelection)
            {
                GUI.Label(new Rect(x, y, width, height), "Choose your total bet:");
                y += height;

                if (GUI.Button(new Rect(x, y, 40, height), "-"))
                    selectedBet = Mathf.Max(GameConstants.MinBet, selectedBet - 1);

                GUI.Label(new Rect(x + 50, y, width - 100, height), $"{selectedBet} coins", CenteredStyle());
                if (GUI.Button(new Rect(x + width - 40, y, 40, height), "+"))
                    selectedBet = Mathf.Min(GameConstants.MaxBet, selectedBet + 1);

                y += height + 4;
                GUI.Label(new Rect(x, y, width, height), $"Best of {GameConstants.RoundsPerMatch} rounds.");
                y += height + 4;

                if (GUI.Button(new Rect(x, y, width, height), "Start match"))
                    stateManager.TryConfirmBet(selectedBet);
            }
            else if (state.Phase == GamePhase.PlayerTurn)
            {
                GUI.Label(new Rect(x, y, width, height), $"Round {state.CurrentRound} — You {state.PlayerRoundsWon} : {state.EnemyRoundsWon} Enemy");
                y += height;
                GUI.Label(new Rect(x, y, width, height), $"Glass holds: {state.Glass.OverflowThreshold} coins");
                y += height;
                GUI.Label(new Rect(x, y, width, height), $"Your coins: {state.PlayerCoinsRemaining}");
                y += height;
                GUI.Label(new Rect(x, y, width, height), $"Enemy coins: {state.EnemyCoinsRemaining}");
                y += height;
                GUI.Label(new Rect(x, y, width, height), $"In glass: {state.Glass.CoinsInGlass} / {state.Glass.OverflowThreshold}");
                y += height;

                if (state.LastEnemyDeposit > 0)
                {
                    GUI.Label(new Rect(x, y, width, height), $"Enemy put in +{state.LastEnemyDeposit}");
                    y += height;
                }

                y += 4;
                DrawDepositButton(x, y, 70, height, 1, state.PlayerCoinsRemaining);
                DrawDepositButton(x + 80, y, 70, height, 2, state.PlayerCoinsRemaining);
                DrawDepositButton(x + 160, y, 70, height, 3, state.PlayerCoinsRemaining);
            }
            else if (state.Phase == GamePhase.RoundEnd)
            {
                GUI.Label(new Rect(x, y, width, height * 2), BuildRoundResultMessage(state));
                y += height * 2;
                GUI.Label(new Rect(x, y, width, height), $"Score — You {state.PlayerRoundsWon} : {state.EnemyRoundsWon} Enemy");
                y += height + 4;

                if (GUI.Button(new Rect(x, y, width, height), "Next round"))
                    stateManager.TryStartNextRound();
            }
            else if (state.Phase == GamePhase.MatchEnd)
            {
                GUI.Label(new Rect(x, y, width, height * 2), BuildMatchResultMessage(state));
                y += height * 2;

                if (GUI.Button(new Rect(x, y, width, height), "New match"))
                {
                    selectedBet = GameConstants.DefaultBet;
                    stateManager.StartSession();
                }
            }
        }

        static string BuildRoundResultMessage(MatchState state)
        {
            return state.RoundResult switch
            {
                RoundResult.PlayerWin when state.Glass.HasOverflowed =>
                    $"Round won! Enemy overfilled the glass.\n(Glass held {state.Glass.OverflowThreshold} coins.)",
                RoundResult.EnemyWin when state.Glass.HasOverflowed =>
                    $"Round lost. You overfilled the glass.\n(Glass held {state.Glass.OverflowThreshold} coins.)",
                RoundResult.PlayerWin =>
                    $"Round won! You {state.PlayerDepositedThisRound} — Enemy {state.EnemyDepositedThisRound}.",
                RoundResult.EnemyWin =>
                    $"Round lost. You {state.PlayerDepositedThisRound} — Enemy {state.EnemyDepositedThisRound}.",
                _ => $"Round tied ({state.PlayerDepositedThisRound} vs {state.EnemyDepositedThisRound})."
            };
        }

        static string BuildMatchResultMessage(MatchState state)
        {
            var score = $"Final score — You {state.PlayerRoundsWon} : {state.EnemyRoundsWon} Enemy";

            return state.MatchResult switch
            {
                MatchResult.PlayerWin => $"You win the match!\n{score}",
                MatchResult.EnemyWin => $"You lose the match.\n{score}",
                _ => $"Match tied.\n{score}"
            };
        }

        void DrawDepositButton(int x, int y, int width, int height, int amount, int coinsRemaining)
        {
            GUI.enabled = coinsRemaining >= amount;
            if (GUI.Button(new Rect(x, y, width, height), $"+{amount}"))
                stateManager.TryPlayerDeposit(amount);
            GUI.enabled = true;
        }

        static GUIStyle CenteredStyle() =>
            new(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
    }
}
