using Meniscus.Core;
using Meniscus.Items;
using Meniscus.UI;
using NUnit.Framework;
using UnityEngine;

namespace Meniscus.Tests.EditMode
{
    public class DeskItemTrayTests
    {
        GameObject host;
        DeskItemTray tray;
        PlayerInventory inventory;
        GameManager gameManager;

        ItemDefinition itemA;
        ItemDefinition itemB;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("Tray Fixture");
            // GameManager defaults to GameState.StartRound (NOT PlayerTurn), so TryUseItem refuses —
            // exactly the off-turn condition we exercise below.
            gameManager = host.AddComponent<GameManager>();
            inventory = host.AddComponent<PlayerInventory>();
            tray = host.AddComponent<DeskItemTray>();
            tray.Configure(gameManager, inventory);

            itemA = ItemDefinition.Create("a", "Item A", "", 10, ItemEffectKind.PayoutMultiplier, 2f);
            itemB = ItemDefinition.Create("b", "Item B", "", 10, ItemEffectKind.SafeZoneBonus, 1f);
        }

        [TearDown]
        public void TearDown()
        {
            if (host != null)
                Object.DestroyImmediate(host);
        }

        [Test]
        public void Rebuild_MatchesOneBoxPerStackWithCount()
        {
            inventory.Grant(itemA);
            inventory.Grant(itemA);
            inventory.Grant(itemB);

            // inventory.Changed drove Rebuild; assert the box set.
            Assert.AreEqual(2, tray.Boxes.Count, "One box per stack.");

            var boxA = FindBox(itemA);
            var boxB = FindBox(itemB);
            Assert.IsNotNull(boxA);
            Assert.IsNotNull(boxB);
            Assert.AreEqual(2, boxA.Count);
            Assert.AreEqual(1, boxB.Count);
        }

        [Test]
        public void Rebuild_RemovesBoxWhenStackEmptied()
        {
            inventory.Grant(itemA);
            Assert.AreEqual(1, tray.Boxes.Count);

            inventory.TryConsume(itemA);

            Assert.AreEqual(0, tray.Boxes.Count, "Emptied stack removes its box.");
        }

        [Test]
        public void OnBoxClicked_SelectingSecondBoxDeselectsFirst()
        {
            inventory.Grant(itemA);
            inventory.Grant(itemB);

            var boxA = FindBox(itemA);
            var boxB = FindBox(itemB);

            tray.OnBoxClicked(boxA);
            Assert.AreSame(boxA, tray.Selected);
            Assert.IsTrue(boxA.IsSelected);

            tray.OnBoxClicked(boxB);
            Assert.AreSame(boxB, tray.Selected);
            Assert.IsTrue(boxB.IsSelected);
            Assert.IsFalse(boxA.IsSelected, "Selecting a second box deselects the first.");
        }

        [Test]
        public void OnUseClicked_OffTurn_DoesNotConsumeAndStaysSelected()
        {
            inventory.Grant(itemA);
            var boxA = FindBox(itemA);
            tray.OnBoxClicked(boxA);

            // GameManager is in StartRound (not PlayerTurn) → TryUseItem returns false.
            tray.OnUseClicked(boxA);

            Assert.AreEqual(1, inventory.TotalCount, "Off-turn use must not consume the item.");
            Assert.AreSame(boxA, tray.Selected, "Box stays selected after a refused use.");
        }

        [Test]
        public void OnCancelClicked_Deselects()
        {
            inventory.Grant(itemA);
            var boxA = FindBox(itemA);
            tray.OnBoxClicked(boxA);

            tray.OnCancelClicked(boxA);

            Assert.IsNull(tray.Selected);
            Assert.IsFalse(boxA.IsSelected);
        }

        DeskItemBox FindBox(ItemDefinition item)
        {
            foreach (var box in tray.Boxes)
            {
                if (box.Item == item)
                    return box;
            }

            return null;
        }
    }
}
