namespace Meniscus.Core
{
    public enum CoinSize
    {
        Small,
        Medium,
        Large
    }

    public enum GameState
    {
        StartRound,
        PlayerTurn,
        EnemyTurn,
        Resolution,
        RestockPhase,
        ShopPhase,
        GameOver
    }

    public enum CameraState
    {
        TableOverview,
        PlayerFocus,
        DealerFocus,
        GlassZoom
    }

    public enum TurnActor
    {
        Player,
        Enemy
    }

    public enum MatchOutcome
    {
        None,
        PlayerWon,
        PlayerLost
    }
}
