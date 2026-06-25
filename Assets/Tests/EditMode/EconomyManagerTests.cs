using Meniscus.Core;
using Meniscus.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace Meniscus.Tests.EditMode
{
    public class EconomyManagerTests
    {
        GameObject economyObject;
        EconomyManager economyManager;

        [SetUp]
        public void SetUp()
        {
            economyObject = new GameObject("EconomyManager Test Host");
            economyManager = economyObject.AddComponent<EconomyManager>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(economyObject);
        }

        [Test]
        public void CalculateSafeDropPayout_PaysForTheDangerBraved()
        {
            var coin = CreateCoin("Base Coin", 10f, 20, true);

            // The second arg is the spill chance the pour braved (not the fill before it).
            // braved 50% → factor = 0.1 + 6·(0.5)^1.5 ≈ 2.2213 → 20 × 2.2213 ≈ 44.4 → 44
            var payout = economyManager.CalculateSafeDropPayout(new[] { coin }, 50f);

            Assert.AreEqual(44, payout);
            Object.DestroyImmediate(coin.gameObject);
        }

        [Test]
        public void AwardSafeDrop_WithMultipleCoins_AppliesComboMultiplier()
        {
            var coinA = CreateCoin("Coin A", 5f, 10, true);
            var coinB = CreateCoin("Coin B", 5f, 10, true);

            // braved 50% → factor ≈ 2.2213; (10 + 10) × 2.2213 ≈ 44.4; 2-coin combo ×1.15 ≈ 51.1 → 51
            var payout = economyManager.AwardSafeDrop(new[] { coinA, coinB }, 50f);

            Assert.AreEqual(51, payout);
            Assert.AreEqual(51, economyManager.CurrentRoundEarnings);

            Object.DestroyImmediate(coinA.gameObject);
            Object.DestroyImmediate(coinB.gameObject);
        }

        [Test]
        public void BankCurrentRoundEarnings_AddsRoundEarningsToTotalAndClearsRound()
        {
            var coin = CreateCoin("Bank Coin", 5f, 25, true);

            // braved 50% → factor ≈ 2.2213 → 25 × 2.2213 ≈ 55.5 → 56 banked
            economyManager.AwardSafeDrop(new[] { coin }, 50f);
            economyManager.BankCurrentRoundEarnings();

            Assert.AreEqual(56, economyManager.PlayerTotalBankedCash);
            Assert.AreEqual(0, economyManager.CurrentRoundEarnings);

            Object.DestroyImmediate(coin.gameObject);
        }

        [Test]
        public void QueueNextSafeDropPayoutMultiplier_AppliesOnceToAwardedPlayerDrop()
        {
            var coin = CreateCoin("Marked Coin", 5f, 10, true);

            economyManager.QueueNextSafeDropPayoutMultiplier(2f);

            // braved 50% → factor ≈ 2.2213 → 10 × 2.2213 ≈ 22.2 → 22; boosted ×2 = 44
            var boostedPayout = economyManager.AwardSafeDrop(new[] { coin }, 50f);
            var normalPayout = economyManager.AwardSafeDrop(new[] { coin }, 50f);

            Assert.AreEqual(44, boostedPayout);
            Assert.AreEqual(22, normalPayout);
            Assert.AreEqual(66, economyManager.CurrentRoundEarnings);

            Object.DestroyImmediate(coin.gameObject);
        }

        [Test]
        public void SetRoundPayoutMultiplier_AppliesToEverySafeDrop_UntilRoundReset()
        {
            var coin = CreateCoin("Happy Hour Coin", 5f, 10, true);

            economyManager.SetRoundPayoutMultiplier(2f);

            // braved 50% → 22 base; round ×2 = 44. Unlike the one-shot bonus it is NOT consumed, so the
            // next pour this round is boosted too.
            var first = economyManager.AwardSafeDrop(new[] { coin }, 50f);
            var second = economyManager.AwardSafeDrop(new[] { coin }, 50f);

            Assert.AreEqual(44, first);
            Assert.AreEqual(44, second);

            // A new round (ResetRoundEarnings) clears the boost, so the next pour pays the base again.
            economyManager.ResetRoundEarnings();
            var afterReset = economyManager.AwardSafeDrop(new[] { coin }, 50f);

            Assert.AreEqual(22, afterReset);

            Object.DestroyImmediate(coin.gameObject);
        }

        [Test]
        public void WipeCurrentRoundEarnings_DoesNotClearBankedCash()
        {
            var bankedCoin = CreateCoin("Banked Coin", 5f, 30, true);
            var riskCoin = CreateCoin("Risk Coin", 5f, 40, true);

            // braved 50% → factor ≈ 2.2213 → 30 × 2.2213 ≈ 66.6 → 67 banked; the wiped drop doesn't count
            economyManager.AwardSafeDrop(new[] { bankedCoin }, 50f);
            economyManager.BankCurrentRoundEarnings();
            economyManager.AwardSafeDrop(new[] { riskCoin }, 50f);
            economyManager.WipeCurrentRoundEarnings();

            Assert.AreEqual(67, economyManager.PlayerTotalBankedCash);
            Assert.AreEqual(0, economyManager.CurrentRoundEarnings);

            Object.DestroyImmediate(bankedCoin.gameObject);
            Object.DestroyImmediate(riskCoin.gameObject);
        }

        [Test]
        public void GrantBankedCash_AddsStraightToBank_IgnoresNonPositive()
        {
            economyManager.GrantBankedCash(100);
            Assert.AreEqual(100, economyManager.PlayerTotalBankedCash);

            economyManager.GrantBankedCash(0);
            economyManager.GrantBankedCash(-50);
            Assert.AreEqual(100, economyManager.PlayerTotalBankedCash);
        }

        [Test]
        public void SpendableCash_IsBankedPlusRoundEarnings()
        {
            economyManager.GrantBankedCash(40);
            var coin = CreateCoin("Round Coin", 5f, 100, true);
            var payout = economyManager.AwardSafeDrop(new[] { coin }, 50f);

            // Banked savings plus this round's at-risk earnings — the single total the wallet HUD shows.
            Assert.AreEqual(40 + payout, economyManager.SpendableCash);

            Object.DestroyImmediate(coin.gameObject);
        }

        [Test]
        public void TrySpend_DrawsFromBankFirstThenRoundEarnings()
        {
            economyManager.GrantBankedCash(30);
            var coin = CreateCoin("Round Coin", 5f, 100, true);
            economyManager.AwardSafeDrop(new[] { coin }, 50f);   // fills round earnings well above the cost
            var roundBefore = economyManager.CurrentRoundEarnings;

            Assert.IsTrue(economyManager.TrySpend(50));

            Assert.AreEqual(0, economyManager.PlayerTotalBankedCash);                 // the $30 bank drained first…
            Assert.AreEqual(roundBefore - 20, economyManager.CurrentRoundEarnings);   // …then $20 from round earnings

            Object.DestroyImmediate(coin.gameObject);
        }

        [Test]
        public void TrySpend_BeyondSpendable_FailsAndChangesNothing()
        {
            economyManager.GrantBankedCash(10);

            Assert.IsFalse(economyManager.TrySpend(11));
            Assert.AreEqual(10, economyManager.PlayerTotalBankedCash);
            Assert.AreEqual(0, economyManager.CurrentRoundEarnings);
        }

        [Test]
        public void TrySpend_RaisesCashSpentWithNewSpendableTotal()
        {
            economyManager.GrantBankedCash(100);
            var raisedAmount = 0;
            var reportedTotal = -1;
            economyManager.CashSpent += (amount, newTotal) => { raisedAmount = amount; reportedTotal = newTotal; };

            Assert.IsTrue(economyManager.TrySpend(35));
            Assert.AreEqual(35, raisedAmount);
            Assert.AreEqual(65, reportedTotal);
        }

        [Test]
        public void PreviewSafeDropMultiplier_FoldsBoldnessAndCombo()
        {
            var coin = CreateCoin("Solo Coin", 5f, 20, true);
            var coinB = CreateCoin("Combo Coin", 5f, 20, true);

            // braved 50% → factor = 0.1 + 6·(0.5)^1.5 ≈ 2.2213. It is a per-coin-value multiplier, so the
            // coins' payout is irrelevant to the figure; a second coin folds in the gentle ×1.15 combo
            // (2.2213 × 1.15 ≈ 2.5545).
            Assert.AreEqual(2.2213f, economyManager.PreviewSafeDropMultiplier(new[] { coin }, 50f), 0.001f);
            Assert.AreEqual(2.5545f, economyManager.PreviewSafeDropMultiplier(new[] { coin, coinB }, 50f), 0.001f);

            Object.DestroyImmediate(coin.gameObject);
            Object.DestroyImmediate(coinB.gameObject);
        }

        [Test]
        public void PreviewSafeDropMultiplier_IncludesShopBoosts_WithoutConsumingOneShot()
        {
            var coin = CreateCoin("Boosted Coin", 5f, 10, true);
            economyManager.QueueNextSafeDropPayoutMultiplier(2f);

            // The preview reflects the queued one-shot (≈2.2213 × 2 = 4.4426)…
            var preview = economyManager.PreviewSafeDropMultiplier(new[] { coin }, 50f);
            // …but must NOT spend it: the real award still gets the ×2 (RoundToInt(10×2.2213)=22, ×2 = 44).
            var awarded = economyManager.AwardSafeDrop(new[] { coin }, 50f);

            Assert.AreEqual(4.4426f, preview, 0.001f);
            Assert.AreEqual(44, awarded);

            Object.DestroyImmediate(coin.gameObject);
        }

        [Test]
        public void PreviewSafeDropMultiplier_EmptySelection_ReturnsOne()
        {
            Assert.AreEqual(1f, economyManager.PreviewSafeDropMultiplier(new Coin[0], 50f), 0.0001f);
            Assert.AreEqual(1f, economyManager.PreviewSafeDropMultiplier(null, 50f), 0.0001f);
        }

        static Coin CreateCoin(string name, float risk, int payout, bool isPlayerCoin)
        {
            var coinObject = new GameObject(name);
            var coin = coinObject.AddComponent<Coin>();
            coin.Configure(CoinSize.Medium, risk, payout, isPlayerCoin);
            return coin;
        }
    }
}
