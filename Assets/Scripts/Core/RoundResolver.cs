namespace Meniscus.Core
{
    public static class RoundResolver
    {
        public static RoundResult Resolve(MatchState state)
        {
            if (state.Glass.HasOverflowed && state.Glass.OverflowCausedBy.HasValue)
            {
                return state.Glass.OverflowCausedBy.Value == ParticipantId.Player
                    ? RoundResult.EnemyWin
                    : RoundResult.PlayerWin;
            }

            if (state.PlayerDepositedThisRound > state.EnemyDepositedThisRound)
                return RoundResult.PlayerWin;

            if (state.EnemyDepositedThisRound > state.PlayerDepositedThisRound)
                return RoundResult.EnemyWin;

            return RoundResult.Draw;
        }
    }
}
