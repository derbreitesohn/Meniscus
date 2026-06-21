using Meniscus.Core;
using Meniscus.Items;
using NUnit.Framework;
using UnityEngine;

namespace Meniscus.Tests.EditMode
{
    public class PlayerInventoryTests
    {
        GameObject host;
        PlayerInventory inventory;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("Inventory Test Host");
            inventory = host.AddComponent<PlayerInventory>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(host);
        }

        static ItemDefinition Item(string id) =>
            ItemDefinition.Create(id, id, "", 10, ItemEffectKind.PayoutMultiplier, 2f);

        [Test]
        public void Grant_ThenHas_AndStacksCounts()
        {
            var a = Item("a");

            Assert.IsTrue(inventory.Grant(a));
            Assert.IsTrue(inventory.Has(a));
            Assert.AreEqual(1, inventory.TotalCount);

            inventory.Grant(a);
            Assert.AreEqual(2, inventory.TotalCount);
            Assert.AreEqual(1, inventory.Contents().Count);
            Assert.AreEqual(2, inventory.Contents()[0].Count);
        }

        [Test]
        public void TryConsume_DecrementsAndRemovesAtZero()
        {
            var a = Item("a");
            inventory.Grant(a);

            Assert.IsTrue(inventory.TryConsume(a));
            Assert.IsFalse(inventory.Has(a));
            Assert.AreEqual(0, inventory.TotalCount);
            Assert.IsFalse(inventory.TryConsume(a));   // already empty
        }

        [Test]
        public void Grant_RejectedWhenFull()
        {
            for (var i = 0; i < GameConstants.DeskCapacity; i++)
                Assert.IsTrue(inventory.Grant(Item($"item{i}")));

            Assert.IsTrue(inventory.IsFull);
            Assert.IsFalse(inventory.Grant(Item("overflow")));
            Assert.AreEqual(GameConstants.DeskCapacity, inventory.TotalCount);
        }

        [Test]
        public void Clear_EmptiesInventory()
        {
            inventory.Grant(Item("a"));
            inventory.Clear();
            Assert.AreEqual(0, inventory.TotalCount);
        }

        [Test]
        public void Changed_FiresOnGrantAndConsume()
        {
            var fired = 0;
            inventory.Changed += () => fired++;

            var a = Item("a");
            inventory.Grant(a);
            inventory.TryConsume(a);

            Assert.AreEqual(2, fired);
        }
    }
}
