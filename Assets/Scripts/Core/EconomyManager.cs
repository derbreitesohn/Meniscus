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
        public event Action<int, int> CashSpent;      // (amountSpent, newSpendableTotal)

        public int PlayerTotalBankedCash => playerTotalBankedCash;
        public int CurrentRoundEarnings => currentRoundEarnings;

        /// <summary>
        /// Everything the player can spend right now: banked savings plus what this round has earned so
        /// far. The wallet HUD shows this same single total, so the shop charges against it (rather than
        /// banked-only) — otherwise money the player can plainly see reads as unspendable mid-round.
        /// </summary>
        public int SpendableCash => playerTotalBankedCash + currentRoundEarnings;

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

            // Combining coins adds a small bonus (gentle, capped) — never the old flat ×3.
            total *= GameConstants.GetComboPayoutMultiplier(coins.Count);

            return Mathf.RoundToInt(total);
        }

        /// <summary>
        /// The payout-per-coin-value this selection would bank if poured now and came up safe — boldness ×
        /// combo × any active shop boosts (Happy Hour, one-shot). This is the "×2.4" the selection preview
        /// shows before a pour; it mirrors the factors in <see cref="AwardSafeDrop"/> without mutating state
        /// or consuming the one-shot boost (and without the per-step integer rounding, so it reads as a clean
        /// forecast). Returns 1 for an empty selection.
        /// </summary>
        public float PreviewSafeDropMultiplier(IReadOnlyList<Coin> coins, float spillChanceBraved)
        {
            if (coins == null || coins.Count == 0)
                return 1f;

            var boldness = Mathf.Clamp01(spillChanceBraved / GameConstants.MaxOverflowProbability);
            var multiplier = GameConstants.BoldnessPayoutFloor
                + GameConstants.BoldnessPayoutScale * Mathf.Pow(boldness, GameConstants.BoldnessExponent);

            multiplier *= GameConstants.GetComboPayoutMultiplier(coins.Count);

            return multiplier * roundPayoutMultiplier * nextSafeDropPayoutMultiplier;
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
            cost >= 0 && SpendableCash >= cost;

        /// <summary>
        /// Spends <paramref name="cost"/> from the player's money, drawing banked savings first and then
        /// dipping into this round's at-risk earnings. Returns false (changing nothing) on a negative cost
        /// or when it cannot be afforded. This is what the shop charges: between rounds the round earnings
        /// are already 0 (banked on the win), so it behaves exactly like spending the bank; mid-round it
        /// lets a purchase be paid for with money earned this round — matching the single wallet total the
        /// HUD shows. Those at-risk earnings are still wiped by a bust, so buying mid-round converts some
        /// of them into a kept item before that can happen.
        /// </summary>
        public bool TrySpend(int cost)
        {
            if (cost < 0)
            {
                Debug.LogWarning($"[EconomyManager] Refused negative shop cost: {cost}.");
                return false;
            }

            if (!CanAfford(cost))
            {
                Debug.LogWarning(
                    $"[EconomyManager] Insufficient cash. Cost={cost}, spendable={SpendableCash} " +
                    $"(banked={playerTotalBankedCash}, round={currentRoundEarnings}).");
                return false;
            }

            // Drain banked savings first, then take the remainder from this round's at-risk earnings.
            var fromBank = Mathf.Min(playerTotalBankedCash, cost);
            playerTotalBankedCash -= fromBank;
            currentRoundEarnings -= cost - fromBank;

            CashSpent?.Invoke(cost, SpendableCash);
            return true;
        }
    }
}
