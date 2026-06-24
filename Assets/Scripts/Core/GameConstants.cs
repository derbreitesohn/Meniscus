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
        public static int MaxPlayerCoinsPerTurn = 3;
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

        // Payouts skew hard by size so big coins are the high-roller play: Gold pays 5× a Copper. This is
        // the coin's raw value; the boldness factor below multiplies it by how dangerous the pour was.
        public static int SmallCoinBasePayout = 10;
        public static int MediumCoinBasePayout = 25;
        public static int LargeCoinBasePayout = 50;

        // Pay for boldness, not for pouring (see EconomyManager.CalculateSafeDropPayout). A safe pour pays
        // base × (floor + scale · boldness^exponent), where boldness is the spill chance the pour actually
        // braved (0..1 of a certain spill). A timid pour into a calm glass pays pennies; a big coin dared
        // into a near-overflowing one is the jackpot. The exponent makes the scariest pours pay
        // disproportionately. Lower floor → safe pours pay even less; raise scale/exponent → bigger jackpots.
        public static float BoldnessPayoutFloor = 0.1f;
        public static float BoldnessPayoutScale = 6f;
        public static float BoldnessExponent = 1.5f;
        public static float ComboMultiplier = 2f;
        public const float MaxOverflowProbability = 100f;

        // Baseline relief (0 by default). Relief is a subtractive discount on the dome fill: shop items
        // (Steady Hand / Iron Grip) raise the player's relief to shrug off some fill, and a drunk-enemy
        // penalty applies it negatively. See GlassManager.CalculateTrueSpillChance.
        public static float SpillSafeZoneThreshold = 0f;

        // ── The meniscus dome (the playfield) ───────────────────────────────────────────────────────
        // The glass opens full to the brim; all play happens in the thin meniscus dome above the rim.
        // CurrentOverflowProbability is "how far coins have pushed into the dome" (0..100). The spill
        // resolution (GlassManager.CalculateTrueSpillChance) reads these knobs:
        //   • below DomeSafeZone   → surface tension holds for sure (0% spill, readable-safe),
        //   • across the dome      → the break chance ramps up CONVEXLY (gentle early, steep near the brim),
        //   • at/above DomeCapacity → a CERTAIN spill (the brim — pour here and it WILL go over).
        // GlassStartFill is where the glass opens each round (the "already at the edge" lever; 0 = calm
        // open). SurfaceSettlePerTurn is the tug-of-war recovery: the surface eases back this much between
        // drops, so the glass hovers at the brim over many turns instead of racing over the top in a couple.
        // DomeRampExponent shapes the climb: 1 = linear, higher = early pours stay safe and the danger
        // escalates into a late climax (so a round builds instead of busting on an early coin-flip).
        // Tuning: longer / less spiky rounds → raise DomeCapacity, SurfaceSettlePerTurn or DomeRampExponent,
        // or lower GlassStartFill; more knife-edge from turn one → raise GlassStartFill or shrink the dome.
        public static float DomeSafeZone = 10f;
        public static float DomeCapacity = 80f;
        public static float GlassStartFill = 15f;
        // Kept below the smallest coin's risk (Copper = 5) so even all-Copper play still creeps the glass
        // up — otherwise two players could turtle on Copper forever and the round never resolves.
        public static float SurfaceSettlePerTurn = 4f;
        public static float DomeRampExponent = 2.5f;

        // Kept only as the normaliser the danger visuals/HUD scale against (dome bulge, vignette, tension
        // RTPC). No longer governs the spill resolution — the dome knobs above do.
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

        // Round-to-round difficulty ramp: later rounds scale every coin's risk contribution up, so the
        // shared glass climbs toward the brim faster and the danger zone arrives sooner. Round 1 is
        // unscaled (a gentle on-ramp); 2 and 3 ramp up so shop items become the survival edge rather than
        // a luxury — the player has to earn (greedy paydays) to afford the relief that later rounds demand.
        // Neutral for now: with the meniscus-dome model a per-coin risk multiplier double-counts and makes
        // late rounds spill almost instantly. Per-round difficulty will instead come from a thinner dome
        // (a higher GlassStartFill / lower DomeCapacity in later rounds). Left as knobs at 1.0 (no scaling).
        public static float Round2RiskMultiplier = 1f;
        public static float Round3RiskMultiplier = 1f;

        public static float GetRoundRiskMultiplier(int round) =>
            round switch
            {
                <= 1 => 1f,
                2 => Round2RiskMultiplier,
                _ => Round3RiskMultiplier
            };

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
