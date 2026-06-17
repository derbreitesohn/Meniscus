using Meniscus.Core;
using Meniscus.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace Meniscus.Tests.EditMode
{
    public class GlassManagerTests
    {
        GameObject glassObject;
        GlassManager glassManager;

        [SetUp]
        public void SetUp()
        {
            glassObject = new GameObject("GlassManager Test Host");
            glassManager = glassObject.AddComponent<GlassManager>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(glassObject);
        }

        [Test]
        public void ResetGlass_SetsOverflowProbabilityToZero()
        {
            var coin = CreateCoin("Risk Coin", 45f, 10, true);

            glassManager.DropCoins(new[] { coin }, TurnActor.Player);
            glassManager.ResetGlass();

            Assert.AreEqual(0f, glassManager.CurrentOverflowProbability);
            Object.DestroyImmediate(coin.gameObject);
        }

        [Test]
        public void DropCoins_WithNoCoins_DoesNotOverflowOrIncreaseRisk()
        {
            var result = glassManager.DropCoins(System.Array.Empty<Coin>(), TurnActor.Player);

            Assert.AreEqual(0f, glassManager.CurrentOverflowProbability);
            Assert.AreEqual(0f, result.RiskBeforeDrop);
            Assert.AreEqual(0f, result.AddedRisk);
            Assert.AreEqual(0f, result.RiskAfterDrop);
            Assert.IsFalse(result.Overflowed);
        }

        [Test]
        public void DropCoins_AddsCombinedRiskToProbability()
        {
            var small = CreateCoin("Small Coin", 5f, 10, true);
            var large = CreateCoin("Large Coin", 15f, 30, true);

            var result = glassManager.DropCoins(new[] { small, large }, TurnActor.Player);

            Assert.AreEqual(20f, glassManager.CurrentOverflowProbability);
            Assert.AreEqual(0f, result.RiskBeforeDrop);
            Assert.AreEqual(20f, result.AddedRisk);
            Assert.AreEqual(20f, result.RiskAfterDrop);
            Assert.AreEqual(2, result.CoinCount);

            Object.DestroyImmediate(small.gameObject);
            Object.DestroyImmediate(large.gameObject);
        }

        [Test]
        public void CalculateTrueSpillChance_UsesExtendedSafeZoneThenSofterCurve()
        {
            Assert.AreEqual(0f, GlassManager.CalculateTrueSpillChance(0f));
            Assert.AreEqual(0f, GlassManager.CalculateTrueSpillChance(44.99f));
            Assert.AreEqual(0f, GlassManager.CalculateTrueSpillChance(45f));
            Assert.AreEqual(1.3f, GlassManager.CalculateTrueSpillChance(50f), 0.45f);
            Assert.AreEqual(16.2f, GlassManager.CalculateTrueSpillChance(65f), 1.25f);
            Assert.AreEqual(44.3f, GlassManager.CalculateTrueSpillChance(80f), 1.75f);
            Assert.AreEqual(100f, GlassManager.CalculateTrueSpillChance(100f));
        }

        [Test]
        public void DropCoins_UnderSafeZoneCannotOverflow()
        {
            var coin = CreateCoin("Safe Zone Coin", 39f, 10, true);

            var result = glassManager.DropCoins(new[] { coin }, TurnActor.Player);

            Assert.AreEqual(39f, result.RiskAfterDrop);
            Assert.AreEqual(0f, result.TrueSpillChance);
            Assert.IsFalse(result.Overflowed);

            Object.DestroyImmediate(coin.gameObject);
        }

        [Test]
        public void QueueNextRoundSafeZoneBonus_AppliesForOneResetThenExpires()
        {
            glassManager.QueueNextRoundSafeZoneBonus(10f);

            glassManager.ResetGlass();

            Assert.AreEqual(55f, glassManager.CurrentSafeZoneThreshold);
            Assert.AreEqual(0f, glassManager.CalculateCurrentTrueSpillChance(50f));

            glassManager.ResetGlass();

            Assert.AreEqual(GameConstants.SpillSafeZoneThreshold, glassManager.CurrentSafeZoneThreshold);
        }

        [Test]
        public void DropCoins_WhenProbabilityReachesOneHundred_AlwaysOverflows()
        {
            var heavy = CreateCoin("Heavy Coin", 120f, 100, false);

            var result = glassManager.DropCoins(new[] { heavy }, TurnActor.Enemy);

            Assert.AreEqual(100f, glassManager.CurrentOverflowProbability);
            Assert.AreEqual(100f, result.TrueSpillChance);
            Assert.AreEqual(TurnActor.Enemy, result.Actor);
            Assert.IsTrue(result.Overflowed);

            Object.DestroyImmediate(heavy.gameObject);
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
