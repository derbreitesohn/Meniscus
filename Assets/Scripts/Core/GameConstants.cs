using UnityEngine;

namespace Meniscus.Core
{
    // Balance / feel values for the whole game, kept as plain `static` fields so they read as one
    // tunable table. (They are not `const`: the Vector3/Color entries below cannot be, and keeping the
    // rest `static` keeps the table uniform.) Consuming code reads them live. MaxOverflowProbability is
    // `const` because it is used as a [Range] attribute argument in GlassManager.
    public static class GameConstants
    {
        public static int TotalRounds = 3;
        public static int MinCoinsPerActor = 8;
        public static int MaxCoinsPerActor = 12;
        public static int MaxEnemyCoinsPerTurn = 3;
        public static int MaxPlayerCoinsPerTurn = 3;
        public static int DeskCapacity = 8;

        // ── Shared coin pile / hands ──────────────────────────────────────────────────────────────────
        // Both actors draw from ONE shared reserve (SharedPileSize). Each turn the active actor's hand is
        // topped back up to HandSize from that reserve *before* they pour, so a player can never be left
        // empty-handed while the other still has coins — that was the old "I'm out, now I just watch the
        // Dealer" dead time. When the shared pile is drained it simply refills itself (the round only ever
        // ends on an overflow), so play never stalls. HandSize is the full hand each actor holds: it is
        // dealt at round open and flung back to a fresh full hand from the pile only once it is FULLY spent
        // (see GameManager.RefillHand) — coins deplete visibly (10 → … → 0 → a fresh 10 flies in) instead of
        // trickling in. SharedPileSize is the reserve depth before it silently refills itself; it never
        // blocks a refill, so no one is ever left empty-handed.
        public static int HandSize = 10;
        public static int SharedPileSize = 60;

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

        // Gentler than before (was 5/10/15) so each pour climbs the dome less and a round reaches more
        // hands before anyone is forced over the brim — more press-your-luck, fewer instant busts. The
        // glass still net-climbs because the smallest coin's risk stays above SurfaceSettlePerTurn.
        public static float SmallCoinRisk = 3f;
        public static float MediumCoinRisk = 6f;
        public static float LargeCoinRisk = 9f;

        // Payouts skew hard by size so the coin you choose to pour is the core risk/reward call: Copper is
        // barely worth anything, Silver pays a fair bit, and Gold is the high-roller jackpot (~15× a
        // Copper). This is the coin's raw value; the boldness factor below multiplies it by how dangerous
        // the pour was. Big coins are also the riskiest (see *CoinRisk), so the jackpot is paid for in dome
        // fill — daring a Gold climbs the meniscus far faster than dribbling Coppers.
        public static int SmallCoinBasePayout = 5;     // Copper — barely any money
        public static int MediumCoinBasePayout = 25;   // Silver — some
        public static int LargeCoinBasePayout = 75;    // Gold — a lot

        // Pay for boldness, not for pouring (see EconomyManager.CalculateSafeDropPayout). A safe pour pays
        // base × (floor + scale · boldness^exponent), where boldness is the spill chance the pour actually
        // braved (0..1 of a certain spill). A timid pour into a calm glass pays pennies; a big coin dared
        // into a near-overflowing one is the jackpot. The exponent makes the scariest pours pay
        // disproportionately. Lower floor → safe pours pay even less; raise scale/exponent → bigger jackpots.
        public static float BoldnessPayoutFloor = 0.1f;
        public static float BoldnessPayoutScale = 6f;
        public static float BoldnessExponent = 1.5f;
        // Combining coins pays a small bonus over pouring the same coins one at a time — batching is
        // rewarded, but only gently (the old flat ×3 made it pay far more than the coins were worth). The
        // bonus grows a little per EXTRA coin and caps low, so a big combo is worth a touch more than a
        // pair, never a jackpot. Combos also already claw back a little fill (ComboRiskReliefPerExtraCoin);
        // this is just the modest payout nudge on top. See GetComboPayoutMultiplier / EconomyManager.
        public static float ComboPayoutBonusPerExtraCoin = 0.15f;
        public static float MaxComboPayoutMultiplier = 1.6f;

        // How much dome fill a combo claws back per EXTRA coin beyond the first (a 3-coin pour relieves
        // 2× this). Pouring a cluster at once settles the surface a touch better than dripping the same
        // coins in one at a time. Kept BELOW the smallest coin's risk (Copper = 3) so a combo still
        // net-climbs the glass — it just climbs gentler per coin while paying the combo bonus, rewarding
        // bold batching without letting anyone turtle the meniscus down forever. See GlassManager.DropCoins.
        public static float ComboRiskReliefPerExtraCoin = 2.5f;
        public const float MaxOverflowProbability = 100f;

        // Baseline relief (0 by default). Relief is a subtractive discount on the dome fill: shop items
        // (Steady Hand / Iron Grip) raise the player's relief to shrug off some fill, and a drunk-enemy
        // penalty applies it negatively. See GlassManager.CalculateTrueSpillChance.
        public static float SpillSafeZoneThreshold = 0f;

        // ── The meniscus dome (the playfield) ───────────────────────────────────────────────────────
        // The glass opens full to the brim; all play happens in the thin meniscus dome above the rim.
        // CurrentOverflowProbability is "how far coins have pushed into the dome" (0..100). The spill
        // resolution (GlassManager.CalculateTrueSpillChance) reads these knobs to produce a spill CHANCE,
        // and each pour rolls against it (GlassManager.DropCoins) — overflow is a press-your-luck gamble,
        // not a fixed kill:
        //   • below DomeSafeZone   → surface tension holds for sure (0% chance, readable-safe),
        //   • across the dome      → the break chance ramps up CONVEXLY (gentle early, steep near the brim)
        //                            and the pour ROLLS against it (small early, climbing as it fills),
        //   • at/above DomeCapacity → the chance saturates at 100% — a CERTAIN spill regardless of the roll
        //                            (the physical brim — a visibly-overfull glass never survives, so a
        //                            round always resolves).
        // GlassStartFill is where the glass opens each round (the "already at the edge" lever; 0 = calm
        // open). SurfaceSettlePerTurn is the tug-of-war recovery: the surface eases back this much between
        // drops, so the glass hovers at the brim over many turns instead of racing over the top in a couple.
        // DomeRampExponent shapes the climb: 1 = linear, higher = early pours stay safe and the danger
        // escalates into a late climax (so a round builds instead of busting on an early coin-flip).
        // Tuning: longer / less spiky rounds → raise DomeCapacity, SurfaceSettlePerTurn or DomeRampExponent,
        // or lower GlassStartFill; more knife-edge from turn one → raise GlassStartFill or shrink the dome.
        public static float DomeSafeZone = 10f;
        public static float DomeCapacity = 80f;
        // Opens right at the safe-zone floor so the first pours barely enter the dome: early overflow stays
        // POSSIBLE (a pour pushes just above the floor) but vanishingly unlikely, and pays little (boldness
        // floor). 15→10 alongside the steeper ramp to lengthen the safe opening. Raise toward DomeSafeZone+N
        // for a tenser open.
        public static float GlassStartFill = 10f;
        // Kept below the smallest coin's risk (Copper = 3) so even all-Copper play still creeps the glass
        // up — otherwise two players could turtle on Copper forever and the round never resolves.
        public static float SurfaceSettlePerTurn = 2f;
        public static float DomeRampExponent = 3.5f;   // 2.5→3.5: cuts the early-bust tail — mid-fill pours are far safer, so danger concentrates near the brim instead of a round "randomly" ending at a moderate fill

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

        // The payout multiplier a pour of <paramref name="coinCount"/> coins earns just for combining: 1
        // for a single coin, then +ComboPayoutBonusPerExtraCoin per extra coin, capped at
        // MaxComboPayoutMultiplier so a big batch is only modestly better than a pair.
        public static float GetComboPayoutMultiplier(int coinCount) =>
            coinCount <= 1
                ? 1f
                : Mathf.Min(
                    MaxComboPayoutMultiplier,
                    1f + Mathf.Max(0f, ComboPayoutBonusPerExtraCoin) * (coinCount - 1));

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
