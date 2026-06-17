using System.Collections.Generic;
using Meniscus.Gameplay;
using UnityEngine;

namespace Meniscus.Core
{
    [DisallowMultipleComponent]
    public class EconomyManager : MonoBehaviour
    {
        [SerializeField] int playerTotalBankedCash;
        [SerializeField] int currentRoundEarnings;

        public int PlayerTotalBankedCash => playerTotalBankedCash;
        public int CurrentRoundEarnings => currentRoundEarnings;

        public void ResetRoundEarnings()
        {
            currentRoundEarnings = 0;
            Debug.Log("[EconomyManager] Current round earnings reset to 0.");
        }

        public int CalculateSafeDropPayout(IReadOnlyList<Coin> coins, float currentRisk)
        {
            if (coins == null || coins.Count == 0)
            {
                Debug.Log("[EconomyManager] Payout calculation received no coins. Payout=0.");
                return 0;
            }

            var greedMultiplier = 1f + Mathf.Max(0f, currentRisk) / GameConstants.GreedRiskDivisor;
            var total = 0f;

            for (var i = 0; i < coins.Count; i++)
            {
                if (coins[i] == null)
                    continue;

                var coinPayout = coins[i].basePayout * greedMultiplier;
                total += coinPayout;

                Debug.Log(
                    $"[EconomyManager] Coin payout: coin={coins[i].name}, base={coins[i].basePayout}, " +
                    $"riskBefore={currentRisk:0.##}, greedMultiplier={greedMultiplier:0.###}, " +
                    $"coinPayout={coinPayout:0.##}.");
            }

            if (coins.Count > 1)
            {
                total *= GameConstants.ComboMultiplier;
                Debug.Log(
                    $"[EconomyManager] Combo multiplier applied: coinCount={coins.Count}, " +
                    $"multiplier={GameConstants.ComboMultiplier:0.##}, total={total:0.##}.");
            }

            var rounded = Mathf.RoundToInt(total);
            Debug.Log($"[EconomyManager] Calculated safe drop payout={rounded}.");
            return rounded;
        }

        public int AwardSafeDrop(IReadOnlyList<Coin> coins, float currentRisk)
        {
            var payout = CalculateSafeDropPayout(coins, currentRisk);
            currentRoundEarnings += payout;

            Debug.Log(
                $"[EconomyManager] Awarded safe drop payout={payout}. " +
                $"Current round earnings={currentRoundEarnings}.");

            return payout;
        }

        public void BankCurrentRoundEarnings()
        {
            playerTotalBankedCash += currentRoundEarnings;

            Debug.Log(
                $"[EconomyManager] Banked current round earnings. Banked total={playerTotalBankedCash}, " +
                $"bankedThisRound={currentRoundEarnings}.");

            currentRoundEarnings = 0;
        }

        public void WipeCurrentRoundEarnings()
        {
            Debug.Log(
                $"[EconomyManager] Player overflow. Wiping current round earnings={currentRoundEarnings}. " +
                $"Banked total remains={playerTotalBankedCash}.");

            currentRoundEarnings = 0;
        }

        public bool CanAfford(int cost) =>
            cost >= 0 && playerTotalBankedCash >= cost;

        public bool TrySpendBankedCash(int cost)
        {
            if (cost < 0)
            {
                Debug.LogWarning($"[EconomyManager] Refused negative shop cost: {cost}.");
                return false;
            }

            if (!CanAfford(cost))
            {
                Debug.LogWarning(
                    $"[EconomyManager] Insufficient banked cash. Cost={cost}, banked={playerTotalBankedCash}.");
                return false;
            }

            playerTotalBankedCash -= cost;
            Debug.Log($"[EconomyManager] Spent {cost}. Banked total now={playerTotalBankedCash}.");
            return true;
        }
    }
}
