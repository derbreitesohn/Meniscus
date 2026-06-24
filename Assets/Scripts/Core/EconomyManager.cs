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
        // Lasts the whole round (every safe pour), reset at each round start — Happy Hour's lever.
        [SerializeField, Min(1f)] float roundPayoutMultiplier = 1f;

        public event Action<int, int> MoneyAwarded;   // (payout, newRoundTotal)
        public event Action RoundEarningsWiped;
        public event Action<int, int> EarningsBanked; // (earned, newBankTotal)

        public int PlayerTotalBankedCash => playerTotalBankedCash;
        public int CurrentRoundEarnings => currentRoundEarnings;
        public float NextSafeDropPayoutMultiplier => nextSafeDropPayoutMultiplier;
        public float RoundPayoutMultiplier => roundPayoutMultiplier;

        // The effective payout multiplier (final ÷ raw coin value) the last safe drop earned, plus
        // whether a multi-coin combo was part of it. Set just before MoneyAwarded fires, so the HUD
        // can show "×2.4" / "COMBO" on the money pop-up. 1 = no bonus.
        public float LastSafeDropMultiplier { get; private set; } = 1f;
        public bool LastSafeDropComboApplied { get; private set; }

        public void ResetRoundEarnings()
        {
            currentRoundEarnings = 0;
            roundPayoutMultiplier = 1f;   // a round-long boost (Happy Hour) lasts only its own round
        }

        public int CalculateSafeDropPayout(IReadOnlyList<Coin> coins, float spillChanceBraved)
        {
            if (coins == null || coins.Count == 0)
                return 0;

            var baseTotal = 0f;
            for (var i = 0; i < coins.Count; i++)
            {
                if (coins[i] != null)
                    baseTotal += coins[i].basePayout;
            }

            // Pay for boldness, not for pouring: the reward scales with how close to spilling this pour was
            // (the danger braved), not with merely having dropped a coin. A timid pour into a calm glass
            // pays pennies; a big coin dared into a near-overflowing glass is the jackpot. The exponent
            // makes the scariest pours pay disproportionately.
            var boldness = Mathf.Clamp01(spillChanceBraved / GameConstants.MaxOverflowProbability);
            var payoutFactor = GameConstants.BoldnessPayoutFloor
                + GameConstants.BoldnessPayoutScale * Mathf.Pow(boldness, GameConstants.BoldnessExponent);
            var total = baseTotal * payoutFactor;

            if (coins.Count > 1)
                total *= GameConstants.ComboMultiplier;

            return Mathf.RoundToInt(total);
        }

        public int AwardSafeDrop(IReadOnlyList<Coin> coins, float spillChanceBraved)
        {
            var payout = CalculateSafeDropPayout(coins, spillChanceBraved);

            // Round-long boost (Happy Hour) applies to every safe pour and is NOT consumed.
            if (roundPayoutMultiplier > 1f)
                payout = Mathf.RoundToInt(payout * roundPayoutMultiplier);

            // One-shot boost (Marked Coin / Loaded Dice) stacks on top and is spent by this pour.
            if (nextSafeDropPayoutMultiplier > 1f)
            {
                payout = Mathf.RoundToInt(payout * nextSafeDropPayoutMultiplier);
                nextSafeDropPayoutMultiplier = 1f;
            }

            RecordLastSafeDropMultiplier(coins, payout);
            currentRoundEarnings += payout;
            MoneyAwarded?.Invoke(payout, currentRoundEarnings);
            return payout;
        }

        // Folds greed (risk), combo, and any shop multiplier into one figure — how many times the raw
        // coin value the player actually banked — for the money pop-up to celebrate.
        void RecordLastSafeDropMultiplier(IReadOnlyList<Coin> coins, int payout)
        {
            var baseTotal = 0;

            if (coins != null)
            {
                for (var i = 0; i < coins.Count; i++)
                {
                    if (coins[i] != null)
                        baseTotal += coins[i].basePayout;
                }
            }

            LastSafeDropMultiplier = baseTotal > 0 ? (float)payout / baseTotal : 1f;
            LastSafeDropComboApplied = (coins?.Count ?? 0) > 1;
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
            roundPayoutMultiplier = 1f;
        }

        public void SetRoundPayoutMultiplier(float multiplier)
        {
            if (multiplier <= 1f)
            {
                Debug.LogWarning(
                    $"[EconomyManager] Ignored non-boosting round multiplier={multiplier:0.##}.");
                return;
            }

            roundPayoutMultiplier = Mathf.Max(roundPayoutMultiplier, multiplier);
        }

        /// <summary>
        /// Adds cash straight to the bank (a stipend, a future "found money" item, or test setup), bypassing
        /// the boldness payout path. Non-positive amounts are ignored.
        /// </summary>
        public void GrantBankedCash(int amount)
        {
            if (amount <= 0)
                return;

            playerTotalBankedCash += amount;
            EarningsBanked?.Invoke(amount, playerTotalBankedCash);
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
