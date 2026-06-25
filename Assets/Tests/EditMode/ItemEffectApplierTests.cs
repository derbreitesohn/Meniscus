using Meniscus.Core;
using Meniscus.Gameplay;
using Meniscus.Items;
using NUnit.Framework;
using UnityEngine;

namespace Meniscus.Tests.EditMode
{
    public class ItemEffectApplierTests
    {
        GameObject glassObject;
        GlassManager glassManager;
        Coin fillCoin;

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
            if (fillCoin != null) Object.DestroyImmediate(fillCoin.gameObject);
        }

        [Test]
        public void Apply_EnemySafeZonePenalty_AddsNegativeEnemyReliefImmediately()
        {
            var item = ItemDefinition.Create(
                "dog_whistle", "Pfeifi", "",
                50, ItemEffectKind.EnemySafeZonePenalty, 12f);

            ItemEffectApplier.Apply(item, null, glassManager, null);

            Assert.AreEqual(-12f, glassManager.GetSafeZoneRelief(TurnActor.Enemy), 0.001f);
        }

        [Test]
        public void Apply_RevealTrueOdds_RevealsImmediately()
        {
            var item = ItemDefinition.Create(
                "bartenders_spectacles", "Bartender's Spectacles", "",
                30, ItemEffectKind.RevealTrueOdds, 0f);

            ItemEffectApplier.Apply(item, null, glassManager, null);

            Assert.IsTrue(glassManager.TrueOddsRevealed);
        }

        [Test]
        public void Apply_ReduceCurrentRisk_LowersCurrentFill()
        {
            fillCoin = MakeCoin(40f);
            glassManager.DropCoins(new[] { fillCoin }, TurnActor.Player);
            Assert.AreEqual(40f, glassManager.CurrentOverflowProbability, 0.001f);

            var item = ItemDefinition.Create(
                "buy_the_house_a_round", "Buy the House a Round", "",
                45, ItemEffectKind.ReduceCurrentRisk, 15f);

            ItemEffectApplier.Apply(item, null, glassManager, null);

            Assert.AreEqual(25f, glassManager.CurrentOverflowProbability, 0.001f);
        }

        static Coin MakeCoin(float risk)
        {
            var coinObject = new GameObject("Coin");
            var coin = coinObject.AddComponent<Coin>();
            coin.Configure(CoinSize.Medium, risk, 10, true);
            return coin;
        }
    }
}
