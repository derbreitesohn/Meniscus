using Meniscus.Core;
using Meniscus.Items;
using NUnit.Framework;
using UnityEngine;

namespace Meniscus.Tests.EditMode
{
    public class ItemEffectApplierTests
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
        public void Apply_EnemySafeZonePenalty_AddsNegativeEnemyReliefImmediately()
        {
            var item = ItemDefinition.Create(
                "round_for_the_dealer", "Round for the Dealer", "",
                55, ItemEffectKind.EnemySafeZonePenalty, 12f);

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
    }
}
