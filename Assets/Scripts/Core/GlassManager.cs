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
            currentOverflowProbability = 0f;
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

        public GlassDropResult DropCoins(IReadOnlyList<Coin> coins, TurnActor actor)
        {
            var coinCount = coins?.Count ?? 0;
            var riskBeforeDrop = currentOverflowProbability;
            var addedRisk = SumRisk(coins);
            currentOverflowProbability = Mathf.Clamp(
                currentOverflowProbability + addedRisk,
                0f,
                GameConstants.MaxOverflowProbability);

            var trueSpillChance = CalculateTrueSpillChance(
                currentOverflowProbability, GetSafeZoneRelief(actor));
            var roll = SpillRollProvider?.Invoke()
                ?? UnityEngine.Random.Range(0f, GameConstants.MaxOverflowProbability);
            var overflowed = trueSpillChance > 0f && roll <= trueSpillChance;

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
        /// Spill chance as a continuous curve of the accumulated risk already in the glass. It rises
        /// from 0 on an empty glass, climbs convexly (small early, steeper as it fills, so more and
        /// bigger coins bite harder) and asymptotically approaches <see cref="GameConstants.MaxSpillChance"/>
        /// — getting closer and closer but never reaching it, so a spill is never a certainty.
        /// <paramref name="safeZoneRelief"/> is a temporary discount (from shop items) that subtracts
        /// from the effective fill.
        /// </summary>
        public static float CalculateTrueSpillChance(float totalRiskWeight, float safeZoneRelief)
        {
            var clampedRisk = Mathf.Clamp(totalRiskWeight, 0f, GameConstants.MaxOverflowProbability);
            var clampedRelief = Mathf.Clamp(
                safeZoneRelief,
                -GameConstants.MaxOverflowProbability,
                GameConstants.MaxOverflowProbability);

            var effectiveRisk = clampedRisk - clampedRelief;
            if (effectiveRisk <= 0f)
                return 0f;

            // Half the meter sets the climb rate, so the curve is nearly at the ceiling by a maxed glass
            // yet never touches it. Squaring keeps the early game gentle and the rise convex.
            var load = effectiveRisk / (GameConstants.MaxOverflowProbability * 0.5f);
            return GameConstants.MaxSpillChance * (1f - Mathf.Exp(-load * load));
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
