using System;
using System.Collections.Generic;
using Meniscus.Gameplay;
using UnityEngine;

namespace Meniscus.Core
{
    [DisallowMultipleComponent]
    public class GlassManager : MonoBehaviour
    {
        [SerializeField, Range(0f, GameConstants.MaxOverflowProbability)]
        float currentOverflowProbability;
        [SerializeField, Min(0f)] float activePlayerPourRelief;
        [SerializeField, Min(0f)] float activeEnemyPourPenalty;
        [SerializeField] bool activeRevealTrueOdds;

        public event Action<float> ProbabilityChanged;
        public event Action<GlassDropResult> DropResolved;

        // Source of the 0..MaxOverflowProbability roll compared against the spill chance. Defaults to
        // UnityEngine.Random; tests inject a deterministic roll so outcomes don't depend on the RNG.
        public Func<float> SpillRollProvider { get; set; }

        public float CurrentOverflowProbability => currentOverflowProbability;
        public float CurrentSafeZoneThreshold => Mathf.Clamp(
            GameConstants.SpillSafeZoneThreshold + activePlayerPourRelief,
            0f,
            GameConstants.MaxOverflowProbability);
        public float CurrentTrueSpillChance => CalculateCurrentTrueSpillChance(currentOverflowProbability);
        public bool TrueOddsRevealed => activeRevealTrueOdds;

        // Relief is a subtractive discount on the fill. The player's safe-zone bonus raises it; the
        // enemy's "drunk" penalty pushes it negative (more effective fill, higher spill chance). This is
        // the only per-actor difference in the spill resolution.
        public float GetSafeZoneRelief(TurnActor actor) =>
            actor == TurnActor.Enemy
                ? GameConstants.SpillSafeZoneThreshold - activeEnemyPourPenalty
                : CurrentSafeZoneThreshold;

        public void ResetGlass()
        {
            activePlayerPourRelief = 0f;
            activeEnemyPourPenalty = 0f;
            activeRevealTrueOdds = false;
            // The glass opens full to the brim: the round starts partway up the meniscus dome, so the very
            // first pour already carries weight (no free, weightless filling phase).
            currentOverflowProbability = Mathf.Clamp(
                GameConstants.GlassStartFill, 0f, GameConstants.MaxOverflowProbability);
            ProbabilityChanged?.Invoke(currentOverflowProbability);
        }

        /// <summary>
        /// Eases the surface back down between drops — the tug-of-war recovery. A pour spikes the dome; by
        /// the time the next actor pours, surface tension has relaxed a little, so the glass hovers at the
        /// brim over many tense turns instead of racing over the top in a couple. No-op on a calm glass.
        /// </summary>
        public void SettleSurface()
        {
            if (currentOverflowProbability <= 0f)
                return;

            currentOverflowProbability = Mathf.Max(
                0f, currentOverflowProbability - Mathf.Max(0f, GameConstants.SurfaceSettlePerTurn));
            ProbabilityChanged?.Invoke(currentOverflowProbability);
        }

        public void AddPlayerPourRelief(float relief)
        {
            if (relief <= 0f)
                return;

            activePlayerPourRelief = Mathf.Clamp(
                activePlayerPourRelief + relief,
                0f,
                GameConstants.MaxOverflowProbability - GameConstants.SpillSafeZoneThreshold);
        }

        public void AddEnemyPourPenalty(float penalty)
        {
            if (penalty <= 0f)
                return;

            activeEnemyPourPenalty = Mathf.Clamp(
                activeEnemyPourPenalty + penalty,
                0f,
                GameConstants.MaxOverflowProbability);
        }

        public void RevealTrueOddsForRound() => activeRevealTrueOdds = true;

        public void ReduceCurrentRisk(float amount)
        {
            if (amount <= 0f)
                return;

            currentOverflowProbability = Mathf.Max(0f, currentOverflowProbability - amount);
            ProbabilityChanged?.Invoke(currentOverflowProbability);
        }

        public float CalculateCurrentTrueSpillChance(float totalRiskWeight) =>
            CalculateTrueSpillChance(totalRiskWeight, CurrentSafeZoneThreshold);

        /// <summary>
        /// The fill the glass would sit at after pouring <paramref name="coinCount"/> coins carrying
        /// <paramref name="addedRisk"/> total risk: the current fill plus that risk (clamped to the brim),
        /// minus the combo claw-back (see <see cref="ComboRelief"/>). Single source of truth so the live
        /// pour (<see cref="DropCoins"/>) and the selection preview agree on the post-drop fill.
        /// </summary>
        public float ProjectFillAfterDrop(float addedRisk, int coinCount)
        {
            var filled = Mathf.Clamp(
                currentOverflowProbability + Mathf.Max(0f, addedRisk),
                0f,
                GameConstants.MaxOverflowProbability);

            return Mathf.Max(0f, filled - ComboRelief(coinCount));
        }

        /// <summary>
        /// How much dome fill a combo of <paramref name="coinCount"/> coins claws back (0 for a single
        /// coin): <see cref="GameConstants.ComboRiskReliefPerExtraCoin"/> per extra coin beyond the first.
        /// </summary>
        public static float ComboRelief(int coinCount) =>
            coinCount > 1
                ? Mathf.Max(0f, GameConstants.ComboRiskReliefPerExtraCoin) * (coinCount - 1)
                : 0f;

        public GlassDropResult DropCoins(IReadOnlyList<Coin> coins, TurnActor actor)
        {
            var coinCount = coins?.Count ?? 0;
            var riskBeforeDrop = currentOverflowProbability;
            var addedRisk = SumRisk(coins);
            // Add the coins' risk, then let a combo claw a little of it back (see ProjectFillAfterDrop /
            // ComboRelief): pouring a cluster at once settles the surface a touch better than dripping the
            // same coins in one at a time. The relief stays below a single coin's risk, so a combo still
            // net-climbs the glass — it just climbs gentler per coin while it pays the combo bonus.
            currentOverflowProbability = ProjectFillAfterDrop(addedRisk, coinCount);

            var trueSpillChance = CalculateTrueSpillChance(
                currentOverflowProbability, GetSafeZoneRelief(actor));
            // Probabilistic overflow: the surface breaks on a dice roll against the spill chance, so a pour
            // into the dome is a genuine press-your-luck gamble (a small chance early, climbing as the glass
            // fills) rather than a fixed, readable kill. The one hard edge kept is the physical brim — once
            // the effective fill reaches the dome capacity the chance saturates at MaxOverflowProbability and
            // the glass WILL go over regardless of the roll, so a visibly-overfull glass never survives and a
            // round always resolves. Everything below the brim is the roll's to decide. The roll is in
            // [0, MaxOverflowProbability): a LOW (unlucky) roll under the chance breaks the surface; a HIGH
            // (lucky) roll at or above it holds.
            var roll = SpillRollProvider?.Invoke()
                ?? UnityEngine.Random.Range(0f, GameConstants.MaxOverflowProbability);
            var overflowed = trueSpillChance >= GameConstants.MaxOverflowProbability
                || roll < trueSpillChance;

            var result = new GlassDropResult(
                actor,
                riskBeforeDrop,
                addedRisk,
                currentOverflowProbability,
                roll,
                overflowed,
                coinCount,
                trueSpillChance);

            // One-shot pour modifiers are spent by the pour they applied to.
            if (actor == TurnActor.Player)
                activePlayerPourRelief = 0f;
            else
                activeEnemyPourPenalty = 0f;

            DropResolved?.Invoke(result);
            ProbabilityChanged?.Invoke(currentOverflowProbability);
            return result;
        }

        public static float CalculateTrueSpillChance(float totalRiskWeight) =>
            CalculateTrueSpillChance(totalRiskWeight, GameConstants.SpillSafeZoneThreshold);

        /// <summary>
        /// Spill chance as a reading of the meniscus dome — the percentage a pour at this fill ROLLS
        /// against in <see cref="DropCoins"/>. The glass is full to the brim; play happens in the thin dome
        /// above the rim. <paramref name="totalRiskWeight"/> is how far coins have pushed into that dome.
        /// Below <see cref="GameConstants.DomeSafeZone"/> surface tension holds for sure (0%). Across the
        /// dome the break chance ramps up convexly (gentle early, steepening hard near the top — see
        /// <see cref="GameConstants.DomeRampExponent"/>), saturating at 100% once the fill hits
        /// <see cref="GameConstants.DomeCapacity"/> — a CERTAIN spill at the physical brim regardless of the
        /// roll, so a round always resolves. The convex shape keeps early pours a long-odds gamble so a
        /// round builds over many turns instead of ending on an early coin-flip. <paramref name="safeZoneRelief"/>
        /// shifts the effective fill (a player bonus eases it down; a drunk-enemy penalty pushes it up).
        /// </summary>
        public static float CalculateTrueSpillChance(float totalRiskWeight, float safeZoneRelief)
        {
            var effectiveFill = totalRiskWeight - safeZoneRelief;
            var safeZone = Mathf.Max(0f, GameConstants.DomeSafeZone);
            var capacity = Mathf.Max(safeZone + 0.01f, GameConstants.DomeCapacity);

            if (effectiveFill <= safeZone)
                return 0f;
            if (effectiveFill >= capacity)
                return GameConstants.MaxOverflowProbability;   // certain spill once the dome is at the brim

            // Convex ramp: gentle early (lots of safe room), steepening sharply toward the brim.
            var t = (effectiveFill - safeZone) / (capacity - safeZone);   // 0..1 across the dome
            return GameConstants.MaxOverflowProbability * Mathf.Pow(t, Mathf.Max(1f, GameConstants.DomeRampExponent));
        }

        static float SumRisk(IReadOnlyList<Coin> coins)
        {
            if (coins == null || coins.Count == 0)
                return 0f;

            var totalRisk = 0f;

            for (var i = 0; i < coins.Count; i++)
            {
                if (coins[i] == null)
                    continue;

                totalRisk += Mathf.Max(0f, coins[i].riskContribution);
            }

            return totalRisk;
        }
    }
}
