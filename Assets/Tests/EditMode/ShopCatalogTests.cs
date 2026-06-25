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
        public void DefaultCatalog_ContainsBartendersSpectacles()
        {
            var specs = Find(ShopCatalog.CreateDefaultCatalog(), "bartenders_spectacles");

            Assert.IsNotNull(specs);
            Assert.AreEqual(ItemEffectKind.RevealTrueOdds, specs.Effect);
            Assert.AreEqual(30, specs.Cost);
            Assert.AreEqual(0f, specs.Magnitude, 0.001f);
        }

        [Test]
        public void DefaultCatalog_ContainsStepOutside()
        {
            var item = Find(ShopCatalog.CreateDefaultCatalog(), "step_outside");

            Assert.IsNotNull(item);
            Assert.AreEqual(ItemEffectKind.SkipTurn, item.Effect);
            Assert.AreEqual(40, item.Cost);
        }

        [Test]
        public void DefaultCatalog_ContainsDogWhistle()
        {
            var item = Find(ShopCatalog.CreateDefaultCatalog(), "dog_whistle");

            Assert.IsNotNull(item);
            Assert.AreEqual(ItemEffectKind.EnemySafeZonePenalty, item.Effect);
            Assert.AreEqual(50, item.Cost);
            Assert.AreEqual(15f, item.Magnitude, 0.001f);
        }

        [Test]
        public void DefaultCatalog_OmitsRemovedItems()
        {
            var catalog = ShopCatalog.CreateDefaultCatalog();

            Assert.IsNull(Find(catalog, "recast_coin"), "Recast was removed from the catalog.");
            Assert.IsNull(Find(catalog, "iron_grip"), "Iron Grip was removed from the catalog.");
            Assert.IsNull(Find(catalog, "buy_the_house_a_round"), "Buy a Round was removed from the catalog.");
            Assert.IsNull(Find(catalog, "happy_hour"), "Happy Hour was removed from the catalog.");
            Assert.IsNull(Find(catalog, "round_for_the_dealer"), "Round for the Dealer was removed from the catalog.");
        }

        [Test]
        public void DefaultCatalog_ContainsTaro()
        {
            var item = Find(ShopCatalog.CreateDefaultCatalog(), "taro_laps");

            Assert.IsNotNull(item);
            Assert.AreEqual(ItemEffectKind.ReduceCurrentRisk, item.Effect);
            Assert.AreEqual(70, item.Cost);
            Assert.AreEqual(30f, item.Magnitude, 0.001f);
        }
    }
}
