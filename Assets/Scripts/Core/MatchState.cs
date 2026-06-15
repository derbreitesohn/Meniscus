using System;

namespace Meniscus.Core
{
    [Serializable]
    public class MatchState
    {
        public GamePhase Phase = GamePhase.BetSelection;
        public int SelectedBet;
        public int CurrentRound;
        public int PlayerRoundsWon;
        public int EnemyRoundsWon;
        public int PlayerCoinsRemaining;
        public int EnemyCoinsRemaining;
        public int PlayerDepositedThisRound;
        public int EnemyDepositedThisRound;
        public int LastPlayerDeposit;
        public int LastEnemyDeposit;
        public RoundResult RoundResult = RoundResult.None;
        public MatchResult MatchResult = MatchResult.None;
        public GlassState Glass = new();

        public void Reset()
        {
            Phase = GamePhase.BetSelection;
            SelectedBet = 0;
            CurrentRound = 0;
            PlayerRoundsWon = 0;
            EnemyRoundsWon = 0;
            PlayerCoinsRemaining = 0;
            EnemyCoinsRemaining = 0;
            PlayerDepositedThisRound = 0;
            EnemyDepositedThisRound = 0;
            LastPlayerDeposit = 0;
            LastEnemyDeposit = 0;
            RoundResult = RoundResult.None;
            MatchResult = MatchResult.None;
            Glass = new GlassState();
        }
    }
}
