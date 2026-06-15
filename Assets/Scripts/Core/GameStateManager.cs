using System;
using UnityEngine;

namespace Meniscus.Core
{
    public class GameStateManager : MonoBehaviour
    {
        [SerializeField] int minGlassCapacity = GameConstants.MinGlassCapacity;
        [SerializeField] int maxGlassCapacity = GameConstants.MaxGlassCapacity;

        readonly MatchState state = new();

        public event Action<MatchState> StateChanged;

        public MatchState State => state;

        void Awake() => StartSession();

        public void StartSession()
        {
            state.Reset();
            SetPhase(GamePhase.BetSelection);
        }

        public bool TryConfirmBet(int amount)
        {
            if (state.Phase != GamePhase.BetSelection || !GameConstants.IsValidBet(amount))
                return false;

            state.SelectedBet = amount;
            state.CurrentRound = 1;
            state.PlayerRoundsWon = 0;
            state.EnemyRoundsWon = 0;
            state.MatchResult = MatchResult.None;
            StartRound();

            return true;
        }

        public bool TryStartNextRound()
        {
            if (state.Phase != GamePhase.RoundEnd)
                return false;

            state.CurrentRound++;
            StartRound();
            return true;
        }

        public bool TryPlayerDeposit(int amount)
        {
            if (state.Phase != GamePhase.PlayerTurn || !GameConstants.IsValidDeposit(amount))
                return false;

            if (state.PlayerCoinsRemaining < amount)
                return false;

            ApplyDeposit(ParticipantId.Player, amount);
            NotifyStateChanged();

            if (state.Glass.HasOverflowed)
            {
                FinishRound();
                return true;
            }

            ExecuteEnemyTurn();
            return true;
        }

        void StartRound()
        {
            state.PlayerCoinsRemaining = state.SelectedBet;
            state.EnemyCoinsRemaining = state.SelectedBet;
            state.PlayerDepositedThisRound = 0;
            state.EnemyDepositedThisRound = 0;
            state.LastPlayerDeposit = 0;
            state.LastEnemyDeposit = 0;
            state.RoundResult = RoundResult.None;
            state.Glass.Reset(RollGlassCapacity());
            BeginPlayerTurn();
        }

        int RollGlassCapacity() =>
            UnityEngine.Random.Range(
                Mathf.Min(minGlassCapacity, maxGlassCapacity),
                Mathf.Max(minGlassCapacity, maxGlassCapacity) + 1);

        void BeginPlayerTurn()
        {
            if (BothOutOfCoins())
            {
                FinishRound();
                return;
            }

            if (!CanDeposit(state.PlayerCoinsRemaining))
            {
                ExecuteEnemyTurn();
                return;
            }

            SetPhase(GamePhase.PlayerTurn);
        }

        void ExecuteEnemyTurn()
        {
            if (!CanDeposit(state.EnemyCoinsRemaining))
            {
                BeginPlayerTurn();
                return;
            }

            var maxAffordable = Mathf.Min(GameConstants.MaxDeposit, state.EnemyCoinsRemaining);
            var amount = UnityEngine.Random.Range(GameConstants.MinDeposit, maxAffordable + 1);
            ApplyDeposit(ParticipantId.Enemy, amount);
            NotifyStateChanged();

            if (state.Glass.HasOverflowed || BothOutOfCoins())
            {
                FinishRound();
                return;
            }

            BeginPlayerTurn();
        }

        static bool CanDeposit(int coinsRemaining) =>
            coinsRemaining >= GameConstants.MinDeposit;

        bool BothOutOfCoins() =>
            !CanDeposit(state.PlayerCoinsRemaining) && !CanDeposit(state.EnemyCoinsRemaining);

        void ApplyDeposit(ParticipantId participant, int amount)
        {
            if (participant == ParticipantId.Player)
            {
                state.PlayerCoinsRemaining -= amount;
                state.PlayerDepositedThisRound += amount;
                state.LastPlayerDeposit = amount;
            }
            else
            {
                state.EnemyCoinsRemaining -= amount;
                state.EnemyDepositedThisRound += amount;
                state.LastEnemyDeposit = amount;
            }

            state.Glass.AddCoins(amount, participant);
        }

        void FinishRound()
        {
            state.RoundResult = RoundResolver.Resolve(state);
            ApplyRoundWin();

            if (TryGetMatchResult(out var matchResult))
            {
                state.MatchResult = matchResult;
                SetPhase(GamePhase.MatchEnd);
                return;
            }

            SetPhase(GamePhase.RoundEnd);
        }

        void ApplyRoundWin()
        {
            if (state.RoundResult == RoundResult.PlayerWin)
                state.PlayerRoundsWon++;
            else if (state.RoundResult == RoundResult.EnemyWin)
                state.EnemyRoundsWon++;
        }

        bool TryGetMatchResult(out MatchResult matchResult)
        {
            if (state.PlayerRoundsWon >= GameConstants.RoundsToWin)
            {
                matchResult = MatchResult.PlayerWin;
                return true;
            }

            if (state.EnemyRoundsWon >= GameConstants.RoundsToWin)
            {
                matchResult = MatchResult.EnemyWin;
                return true;
            }

            if (state.CurrentRound >= GameConstants.RoundsPerMatch)
            {
                matchResult = state.PlayerRoundsWon > state.EnemyRoundsWon
                    ? MatchResult.PlayerWin
                    : state.EnemyRoundsWon > state.PlayerRoundsWon
                        ? MatchResult.EnemyWin
                        : MatchResult.Draw;
                return true;
            }

            matchResult = MatchResult.None;
            return false;
        }

        void SetPhase(GamePhase phase)
        {
            state.Phase = phase;
            NotifyStateChanged();
        }

        void NotifyStateChanged() => StateChanged?.Invoke(state);
    }
}
