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

        public event Action<float> ProbabilityChanged;

        public float CurrentOverflowProbability => currentOverflowProbability;
        public float CurrentTrueSpillChance => CalculateTrueSpillChance(currentOverflowProbability);

        public void ResetGlass()
        {
            currentOverflowProbability = 0f;
            Debug.Log("[GlassManager] Glass reset. Overflow probability is now 0%.");
            ProbabilityChanged?.Invoke(currentOverflowProbability);
        }

        public GlassDropResult DropCoins(IReadOnlyList<Coin> coins, TurnActor actor)
        {
            var coinCount = coins?.Count ?? 0;
            var riskBeforeDrop = currentOverflowProbability;
            var addedRisk = SumRisk(coins);
            currentOverflowProbability = Mathf.Clamp(
                currentOverflowProbability + addedRisk,
                0f,
                GameConstants.MaxOverflowProbability);

            var trueSpillChance = CalculateTrueSpillChance(currentOverflowProbability);
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
                $"True Spill Chance={trueSpillChance:0.##}%, roll={roll:0.##}, overflow={overflowed}.");

            ProbabilityChanged?.Invoke(currentOverflowProbability);
            return result;
        }

        public static float CalculateTrueSpillChance(float totalRiskWeight)
        {
            var clampedRisk = Mathf.Clamp(totalRiskWeight, 0f, GameConstants.MaxOverflowProbability);

            if (clampedRisk <= GameConstants.SpillSafeZoneThreshold)
                return 0f;

            var riskyRange = GameConstants.MaxOverflowProbability - GameConstants.SpillSafeZoneThreshold;
            var normalizedRisk = (clampedRisk - GameConstants.SpillSafeZoneThreshold) / riskyRange;
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
