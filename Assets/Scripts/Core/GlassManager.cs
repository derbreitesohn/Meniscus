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
        [SerializeField, Min(0f)] float pendingNextRoundSafeZoneBonus;
        [SerializeField, Min(0f)] float activeSafeZoneBonus;

        public event Action<float> ProbabilityChanged;
        public event Action<GlassDropResult> DropResolved;

        public float CurrentOverflowProbability => currentOverflowProbability;
        public float CurrentSafeZoneThreshold => Mathf.Clamp(
            GameConstants.SpillSafeZoneThreshold + activeSafeZoneBonus,
            0f,
            GameConstants.MaxOverflowProbability);
        public float CurrentTrueSpillChance => CalculateCurrentTrueSpillChance(currentOverflowProbability);

        public void ResetGlass()
        {
            activeSafeZoneBonus = pendingNextRoundSafeZoneBonus;
            pendingNextRoundSafeZoneBonus = 0f;
            currentOverflowProbability = 0f;
            Debug.Log(
                $"[GlassManager] Glass reset. Overflow probability is now 0%. " +
                $"Safe zone threshold={CurrentSafeZoneThreshold:0.##}%.");
            ProbabilityChanged?.Invoke(currentOverflowProbability);
        }

        public void QueueNextRoundSafeZoneBonus(float bonus)
        {
            var clampedBonus = Mathf.Clamp(
                bonus,
                0f,
                GameConstants.MaxOverflowProbability - GameConstants.SpillSafeZoneThreshold);
            pendingNextRoundSafeZoneBonus = Mathf.Max(pendingNextRoundSafeZoneBonus, clampedBonus);

            Debug.Log(
                $"[GlassManager] Queued next-round safe-zone bonus={clampedBonus:0.##}%. " +
                $"Pending bonus={pendingNextRoundSafeZoneBonus:0.##}%.");
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

            var trueSpillChance = CalculateCurrentTrueSpillChance(currentOverflowProbability);
            var roll = UnityEngine.Random.Range(0f, GameConstants.MaxOverflowProbability);
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

            Debug.Log(
                $"[GlassManager] {actor} dropped {coinCount} coin(s). Risk before={riskBeforeDrop:0.##}, " +
                $"added={addedRisk:0.##}, Total Risk Weight={currentOverflowProbability:0.##}, " +
                $"Safe Zone Threshold={CurrentSafeZoneThreshold:0.##}, " +
                $"True Spill Chance={trueSpillChance:0.##}%, roll={roll:0.##}, overflow={overflowed}.");

            DropResolved?.Invoke(result);
            ProbabilityChanged?.Invoke(currentOverflowProbability);
            return result;
        }

        public static float CalculateTrueSpillChance(float totalRiskWeight) =>
            CalculateTrueSpillChance(totalRiskWeight, GameConstants.SpillSafeZoneThreshold);

        public static float CalculateTrueSpillChance(float totalRiskWeight, float safeZoneThreshold)
        {
            var clampedRisk = Mathf.Clamp(totalRiskWeight, 0f, GameConstants.MaxOverflowProbability);
            var clampedThreshold = Mathf.Clamp(safeZoneThreshold, 0f, GameConstants.MaxOverflowProbability);

            if (clampedRisk <= clampedThreshold)
                return 0f;

            var riskyRange = Mathf.Max(0.001f, GameConstants.MaxOverflowProbability - clampedThreshold);
            var normalizedRisk = (clampedRisk - clampedThreshold) / riskyRange;
            return GameConstants.MaxOverflowProbability * Mathf.Pow(normalizedRisk, GameConstants.SpillCurveExponent);
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
