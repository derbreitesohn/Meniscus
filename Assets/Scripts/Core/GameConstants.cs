namespace Meniscus.Core
{
    public static class GameConstants
    {
        public const int MinDeposit = 1;
        public const int MaxDeposit = 3;
        public const int MinBet = 5;
        public const int MaxBet = 15;
        public const int DefaultBet = 8;
        public const int MinGlassCapacity = 5;
        public const int MaxGlassCapacity = 12;
        public const int RoundsPerMatch = 3;
        public const int RoundsToWin = 2;

        public static bool IsValidBet(int amount) =>
            amount >= MinBet && amount <= MaxBet;

        public static bool IsValidDeposit(int amount) =>
            amount >= MinDeposit && amount <= MaxDeposit;
    }
}
