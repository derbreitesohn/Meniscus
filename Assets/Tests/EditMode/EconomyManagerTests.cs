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
        public void CalculateSafeDropPayout_GreedCountsGlassRiskPlusThisPoursCoins()
        {
            var coin = CreateCoin("Base Coin", 10f, 20, true);

            // greed = 1 + (riskBefore 50 + this coin's risk 10) / 50 = 2.2 → 20 × 2.2 = 44
            var payout = economyManager.CalculateSafeDropPayout(new[] { coin }, 50f);

            Assert.AreEqual(44, payout);
            Object.DestroyImmediate(coin.gameObject);
        }

        [Test]
        public void AwardSafeDrop_WithMultipleCoins_AppliesComboMultiplier()
        {
            var coinA = CreateCoin("Coin A", 5f, 10, true);
            var coinB = CreateCoin("Coin B", 5f, 10, true);

            // greed = 1 + (50 + 5 + 5) / 50 = 2.2; (10 + 10) × 2.2 = 44; combo ×1.5 = 66
            var payout = economyManager.AwardSafeDrop(new[] { coinA, coinB }, 50f);

            Assert.AreEqual(66, payout);
            Assert.AreEqual(66, economyManager.CurrentRoundEarnings);

            Object.DestroyImmediate(coinA.gameObject);
            Object.DestroyImmediate(coinB.gameObject);
        }

        [Test]
        public void BankCurrentRoundEarnings_AddsRoundEarningsToTotalAndClearsRound()
        {
            var coin = CreateCoin("Bank Coin", 5f, 25, true);

            // greed = 1 + (0 + 5)/50 = 1.1 → 25 × 1.1 = 27.5 → 28
            economyManager.AwardSafeDrop(new[] { coin }, 0f);
            economyManager.BankCurrentRoundEarnings();

            Assert.AreEqual(28, economyManager.PlayerTotalBankedCash);
            Assert.AreEqual(0, economyManager.CurrentRoundEarnings);

            Object.DestroyImmediate(coin.gameObject);
        }

        [Test]
        public void QueueNextSafeDropPayoutMultiplier_AppliesOnceToAwardedPlayerDrop()
        {
            var coin = CreateCoin("Marked Coin", 5f, 10, true);

            economyManager.QueueNextSafeDropPayoutMultiplier(2f);

            // base greed = 1 + (0 + 5)/50 = 1.1 → 10 × 1.1 = 11; boosted ×2 = 22
            var boostedPayout = economyManager.AwardSafeDrop(new[] { coin }, 0f);
            var normalPayout = economyManager.AwardSafeDrop(new[] { coin }, 0f);

            Assert.AreEqual(22, boostedPayout);
            Assert.AreEqual(11, normalPayout);
            Assert.AreEqual(33, economyManager.CurrentRoundEarnings);

            Object.DestroyImmediate(coin.gameObject);
        }

        [Test]
        public void WipeCurrentRoundEarnings_DoesNotClearBankedCash()
        {
            var bankedCoin = CreateCoin("Banked Coin", 5f, 30, true);
            var riskCoin = CreateCoin("Risk Coin", 5f, 40, true);

            // greed = 1 + (0 + 5)/50 = 1.1 → 30 × 1.1 = 33 banked; the wiped risk drop doesn't count
            economyManager.AwardSafeDrop(new[] { bankedCoin }, 0f);
            economyManager.BankCurrentRoundEarnings();
            economyManager.AwardSafeDrop(new[] { riskCoin }, 0f);
            economyManager.WipeCurrentRoundEarnings();

            Assert.AreEqual(33, economyManager.PlayerTotalBankedCash);
            Assert.AreEqual(0, economyManager.CurrentRoundEarnings);

            Object.DestroyImmediate(bankedCoin.gameObject);
            Object.DestroyImmediate(riskCoin.gameObject);
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
