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
        public void CalculateSafeDropPayout_UsesRiskBeforeDropForGreedMultiplier()
        {
            var coin = CreateCoin("Base Coin", 10f, 20, true);

            var payout = economyManager.CalculateSafeDropPayout(new[] { coin }, 50f);

            Assert.AreEqual(40, payout);
            Object.DestroyImmediate(coin.gameObject);
        }

        [Test]
        public void AwardSafeDrop_WithMultipleCoins_AppliesComboMultiplier()
        {
            var coinA = CreateCoin("Coin A", 5f, 10, true);
            var coinB = CreateCoin("Coin B", 5f, 10, true);

            var payout = economyManager.AwardSafeDrop(new[] { coinA, coinB }, 50f);

            Assert.AreEqual(60, payout);
            Assert.AreEqual(60, economyManager.CurrentRoundEarnings);

            Object.DestroyImmediate(coinA.gameObject);
            Object.DestroyImmediate(coinB.gameObject);
        }

        [Test]
        public void BankCurrentRoundEarnings_AddsRoundEarningsToTotalAndClearsRound()
        {
            var coin = CreateCoin("Bank Coin", 5f, 25, true);

            economyManager.AwardSafeDrop(new[] { coin }, 0f);
            economyManager.BankCurrentRoundEarnings();

            Assert.AreEqual(25, economyManager.PlayerTotalBankedCash);
            Assert.AreEqual(0, economyManager.CurrentRoundEarnings);

            Object.DestroyImmediate(coin.gameObject);
        }

        [Test]
        public void QueueNextSafeDropPayoutMultiplier_AppliesOnceToAwardedPlayerDrop()
        {
            var coin = CreateCoin("Marked Coin", 5f, 10, true);

            economyManager.QueueNextSafeDropPayoutMultiplier(2f);

            var boostedPayout = economyManager.AwardSafeDrop(new[] { coin }, 0f);
            var normalPayout = economyManager.AwardSafeDrop(new[] { coin }, 0f);

            Assert.AreEqual(20, boostedPayout);
            Assert.AreEqual(10, normalPayout);
            Assert.AreEqual(30, economyManager.CurrentRoundEarnings);

            Object.DestroyImmediate(coin.gameObject);
        }

        [Test]
        public void WipeCurrentRoundEarnings_DoesNotClearBankedCash()
        {
            var bankedCoin = CreateCoin("Banked Coin", 5f, 30, true);
            var riskCoin = CreateCoin("Risk Coin", 5f, 40, true);

            economyManager.AwardSafeDrop(new[] { bankedCoin }, 0f);
            economyManager.BankCurrentRoundEarnings();
            economyManager.AwardSafeDrop(new[] { riskCoin }, 0f);
            economyManager.WipeCurrentRoundEarnings();

            Assert.AreEqual(30, economyManager.PlayerTotalBankedCash);
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
