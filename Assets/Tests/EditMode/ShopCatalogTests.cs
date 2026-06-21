using System.Collections.Generic;
using Meniscus.Items;
using NUnit.Framework;

namespace Meniscus.Tests.EditMode
{
    public class ShopCatalogTests
    {
        static ItemDefinition Find(List<ItemDefinition> catalog, string id) =>
            catalog.Find(item => item != null && item.Id == id);

        [Test]
        public void DefaultCatalog_ContainsRoundForTheDealer()
        {
            var beer = Find(ShopCatalog.CreateDefaultCatalog(), "round_for_the_dealer");

            Assert.IsNotNull(beer);
            Assert.AreEqual(ItemEffectKind.EnemySafeZonePenalty, beer.Effect);
            Assert.AreEqual(55, beer.Cost);
            Assert.AreEqual(12f, beer.Magnitude, 0.001f);
        }

        [Test]
        public void DefaultCatalog_ContainsBartendersSpectacles()
        {
            var specs = Find(ShopCatalog.CreateDefaultCatalog(), "bartenders_spectacles");

            Assert.IsNotNull(specs);
            Assert.AreEqual(ItemEffectKind.RevealTrueOdds, specs.Effect);
            Assert.AreEqual(30, specs.Cost);
            Assert.AreEqual(0f, specs.Magnitude, 0.001f);
        }

        [Test]
        public void DefaultCatalog_ContainsBuyTheHouseARound()
        {
            var item = Find(ShopCatalog.CreateDefaultCatalog(), "buy_the_house_a_round");

            Assert.IsNotNull(item);
            Assert.AreEqual(ItemEffectKind.ReduceCurrentRisk, item.Effect);
            Assert.AreEqual(45, item.Cost);
            Assert.AreEqual(15f, item.Magnitude, 0.001f);
        }

        [Test]
        public void DefaultCatalog_ContainsStepOutside()
        {
            var item = Find(ShopCatalog.CreateDefaultCatalog(), "step_outside");

            Assert.IsNotNull(item);
            Assert.AreEqual(ItemEffectKind.SkipTurn, item.Effect);
            Assert.AreEqual(40, item.Cost);
        }
    }
}
