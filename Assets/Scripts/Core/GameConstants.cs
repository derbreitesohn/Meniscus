using UnityEngine;

namespace Meniscus.Core
{
    // NOTE: The balance/feel values below were authored as `const`, but are intentionally plain
    // `static` so the dev-only settings overlay (Meniscus.Dev.DevSettingsPanel) can mutate them at
    // runtime to trial different values. The consuming code reads them live, so edits take effect
    // immediately. MaxOverflowProbability stays `const` because it is used as a [Range] attribute
    // argument (GlassManager), which requires a compile-time constant. Revert these to `const` when
    // the dev overlay is removed.
    public static class GameConstants
    {
        public static int TotalRounds = 3;
        public static int MinCoinsPerActor = 8;
        public static int MaxCoinsPerActor = 12;
        public static int CoinsPerActor = 10;
        public static int MaxEnemyCoinsPerTurn = 2;
        public static int DeskCapacity = 8;

        public static int MinGlassCapacity = 8;
        public static int MaxGlassCapacity = 15;
        public static int MinDeposit = 1;
        public static int MaxDeposit = 2;
        public static int RoundsToWin = 2;
        public static int RoundsPerMatch => TotalRounds;

        public static bool IsValidBet(int amount) =>
            amount >= MinCoinsPerActor && amount <= MaxCoinsPerActor;

        public static bool IsValidDeposit(int amount) =>
            amount >= MinDeposit && amount <= MaxDeposit;

        public static float SmallCoinRisk = 5f;
        public static float MediumCoinRisk = 10f;
        public static float LargeCoinRisk = 15f;

        public static int SmallCoinBasePayout = 10;
        public static int MediumCoinBasePayout = 20;
        public static int LargeCoinBasePayout = 30;

        public static float GreedRiskDivisor = 50f;
        public static float ComboMultiplier = 1.5f;
        public const float MaxOverflowProbability = 100f;

        // No default grace period: with zero relief the spill curve rises straight from an empty glass,
        // so even the first coins of a round carry a small chance. Shop items (Steady Hand / Iron Grip)
        // raise the relief to temporarily shrug off some accumulated risk.
        public static float SpillSafeZoneThreshold = 0f;

        // The spill chance is a continuous curve that climbs with fill and asymptotically approaches
        // MaxSpillChance — getting ever closer but never reaching it, so a spill is never guaranteed.
        // The ceiling sits below 100% on purpose: even a brimming glass keeps a real chance to walk away
        // (a maxed meter lands around 78%, never quite 80), so pushing your luck stays a gamble rather
        // than a certainty. This is the single tuning knob; the climb rate is derived from the meter
        // scale (see GlassManager.CalculateTrueSpillChance).
        public static float MaxSpillChance = 80f;

        public static float EnemyTurnDelaySeconds = 2f;
        public static float EnemyTellDelaySeconds = 0.35f;
        public static float EnemyConservativeSpillChanceThreshold = 50f;

        public static Vector3 CopperCoinVisualScale = new(0.16f, 0.027f, 0.16f);
        public static Vector3 SilverCoinVisualScale = new(0.20f, 0.031f, 0.20f);
        public static Vector3 GoldCoinVisualScale = new(0.23f, 0.035f, 0.23f);

        public static Color CopperCoinColor = new(0.72f, 0.32f, 0.13f, 1f);
        public static Color SilverCoinColor = new(0.74f, 0.76f, 0.74f, 1f);
        public static Color GoldCoinColor = new(1f, 0.68f, 0.18f, 1f);

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

        // Each actor gets exactly the same number of coins every round. The per-round parameter is kept so
        // callers (and a future scaling rule) need not change.
        public static int GetCoinCountForRound(int round) => CoinsPerActor;

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
