using System;
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
        [SerializeField, Min(1f)] float nextSafeDropPayoutMultiplier = 1f;

        public event Action<int, int> MoneyAwarded;   // (payout, newRoundTotal)
        public event Action RoundEarningsWiped;
        public event Action<int, int> EarningsBanked; // (earned, newBankTotal)

        public int PlayerTotalBankedCash => playerTotalBankedCash;
        public int CurrentRoundEarnings => currentRoundEarnings;
        public float NextSafeDropPayoutMultiplier => nextSafeDropPayoutMultiplier;

        public void ResetRoundEarnings()
        {
            currentRoundEarnings = 0;
        }

        public int CalculateSafeDropPayout(IReadOnlyList<Coin> coins, float currentRisk)
        {
            if (coins == null || coins.Count == 0)
                return 0;

            var greedMultiplier = 1f + Mathf.Max(0f, currentRisk) / GameConstants.GreedRiskDivisor;
            var total = 0f;

            for (var i = 0; i < coins.Count; i++)
            {
                if (coins[i] == null)
                    continue;

                total += coins[i].basePayout * greedMultiplier;
            }

            if (coins.Count > 1)
                total *= GameConstants.ComboMultiplier;

            return Mathf.RoundToInt(total);
        }

        public int AwardSafeDrop(IReadOnlyList<Coin> coins, float currentRisk)
        {
            var payout = CalculateSafeDropPayout(coins, currentRisk);

            if (nextSafeDropPayoutMultiplier > 1f)
            {
                payout = Mathf.RoundToInt(payout * nextSafeDropPayoutMultiplier);
                nextSafeDropPayoutMultiplier = 1f;
            }

            currentRoundEarnings += payout;
            MoneyAwarded?.Invoke(payout, currentRoundEarnings);
            return payout;
        }

        public void QueueNextSafeDropPayoutMultiplier(float multiplier)
        {
            if (multiplier <= 1f)
            {
                Debug.LogWarning(
                    $"[EconomyManager] Ignored non-boosting safe-drop multiplier={multiplier:0.##}.");
                return;
            }

            nextSafeDropPayoutMultiplier = Mathf.Max(nextSafeDropPayoutMultiplier, multiplier);
        }

        public void ClearQueuedShopBonuses()
        {
            nextSafeDropPayoutMultiplier = 1f;
        }

        public void BankCurrentRoundEarnings()
        {
            var earned = currentRoundEarnings;
            playerTotalBankedCash += earned;
            currentRoundEarnings = 0;
            EarningsBanked?.Invoke(earned, playerTotalBankedCash);
        }

        public void WipeCurrentRoundEarnings()
        {
            currentRoundEarnings = 0;
            RoundEarningsWiped?.Invoke();
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
            return true;
        }
    }
}
