using UnityEngine;

namespace Meniscus.Core
{
    public static class GameConstants
    {
        public const int TotalRounds = 3;
        public const int MinCoinsPerActor = 8;
        public const int MaxCoinsPerActor = 12;
        public const int MaxEnemyCoinsPerTurn = 2;

        public const int MinGlassCapacity = 8;
        public const int MaxGlassCapacity = 15;
        public const int MinDeposit = 1;
        public const int MaxDeposit = 2;
        public const int RoundsToWin = 2;
        public const int RoundsPerMatch = TotalRounds;

        public static bool IsValidBet(int amount) =>
            amount >= MinCoinsPerActor && amount <= MaxCoinsPerActor;

        public static bool IsValidDeposit(int amount) =>
            amount >= MinDeposit && amount <= MaxDeposit;

        public const float SmallCoinRisk = 5f;
        public const float MediumCoinRisk = 10f;
        public const float LargeCoinRisk = 15f;

        public const int SmallCoinBasePayout = 10;
        public const int MediumCoinBasePayout = 20;
        public const int LargeCoinBasePayout = 30;

        public const float GreedRiskDivisor = 50f;
        public const float ComboMultiplier = 1.5f;
        public const float MaxOverflowProbability = 100f;
        public const float SpillSafeZoneThreshold = 45f;
        public const float SpillCurveExponent = 1.8f;
        public const float EnemyTurnDelaySeconds = 2f;
        public const float EnemyTellDelaySeconds = 0.35f;
        public const float EnemyConservativeSpillChanceThreshold = 50f;

        public static readonly Vector3 CopperCoinVisualScale = new(0.27f, 0.045f, 0.27f);
        public static readonly Vector3 SilverCoinVisualScale = new(0.34f, 0.052f, 0.34f);
        public static readonly Vector3 GoldCoinVisualScale = new(0.43f, 0.064f, 0.43f);

        public static readonly Color CopperCoinColor = new(0.72f, 0.32f, 0.13f, 1f);
        public static readonly Color SilverCoinColor = new(0.74f, 0.76f, 0.74f, 1f);
        public static readonly Color GoldCoinColor = new(1f, 0.68f, 0.18f, 1f);

        public static float GetRiskForSize(CoinSize size) =>
            size switch
            {
                CoinSize.Small => SmallCoinRisk,
                CoinSize.Medium => MediumCoinRisk,
                CoinSize.Large => LargeCoinRisk,
                _ => MediumCoinRisk
            };

        public static int GetBasePayoutForSize(CoinSize size) =>
            size switch
            {
                CoinSize.Small => SmallCoinBasePayout,
                CoinSize.Medium => MediumCoinBasePayout,
                CoinSize.Large => LargeCoinBasePayout,
                _ => MediumCoinBasePayout
            };

        public static int GetCoinCountForRound(int round)
        {
            var clampedRound = Mathf.Clamp(round, 1, TotalRounds);
            return Mathf.Clamp(
                MinCoinsPerActor + (clampedRound - 1) * 2,
                MinCoinsPerActor,
                MaxCoinsPerActor);
        }

        public static Vector3 GetVisualScaleForSize(CoinSize size) =>
            size switch
            {
                CoinSize.Small => CopperCoinVisualScale,
                CoinSize.Medium => SilverCoinVisualScale,
                CoinSize.Large => GoldCoinVisualScale,
                _ => SilverCoinVisualScale
            };

        public static Color GetMaterialColorForSize(CoinSize size) =>
            size switch
            {
                CoinSize.Small => CopperCoinColor,
                CoinSize.Medium => SilverCoinColor,
                CoinSize.Large => GoldCoinColor,
                _ => SilverCoinColor
            };

        public static string GetDisplayNameForSize(CoinSize size) =>
            size switch
            {
                CoinSize.Small => "Copper",
                CoinSize.Medium => "Silver",
                CoinSize.Large => "Gold",
                _ => "Silver"
            };
    }
}
