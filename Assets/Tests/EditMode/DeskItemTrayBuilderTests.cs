using Meniscus.Items;
using Meniscus.UI;
using NUnit.Framework;
using UnityEngine;

namespace Meniscus.Tests.EditMode
{
    public class DeskItemTrayBuilderTests
    {
        GameObject host;

        [SetUp] public void SetUp() => host = new GameObject("DeskItemTrayHost");
        [TearDown] public void TearDown() { if (host != null) Object.DestroyImmediate(host); }

        [Test]
        public void BuildBox_CreatesBodyColliderLabelAndHiddenTiles()
        {
            var tray = host.AddComponent<DeskItemTray>();
            var item = ItemDefinition.Create("test", "Test Item", "", 10, ItemEffectKind.PayoutMultiplier, 2f);

            var box = DeskItemTrayBuilder.BuildBox(host.transform, tray, item);

            Assert.IsNotNull(box, "BuildBox must return a DeskItemBox.");
            Assert.AreSame(item, box.Item);
            Assert.IsNotNull(box.GetComponentInChildren<Collider>(), "Box body must have a collider for clicks.");
            Assert.IsFalse(box.IsSelected, "A freshly built box starts unselected (tiles hidden).");
        }

        [Test]
        public void SetCount_WritesNameAndQuantityIntoLabel()
        {
            var tray = host.AddComponent<DeskItemTray>();
            var item = ItemDefinition.Create("test", "Test Item", "", 10, ItemEffectKind.PayoutMultiplier, 2f);
            var box = DeskItemTrayBuilder.BuildBox(host.transform, tray, item);

            box.SetCount(3);

            // includeInactive: the card is hidden until the box is selected, so its text lives on an
            // inactive object at rest.
            var label = box.GetComponentInChildren<TextMesh>(true);
            StringAssert.Contains("TEST ITEM", label.text);
            StringAssert.Contains("x3", label.text);
        }

        [Test]
        public void Card_HiddenAtRest_RevealedOnSelect_AndExplainsTheItem()
        {
            var tray = host.AddComponent<DeskItemTray>();
            var item = ItemDefinition.Create(
                "test", "Test Item", "Does a test thing.", 10, ItemEffectKind.PayoutMultiplier, 2f);
            var box = DeskItemTrayBuilder.BuildBox(host.transform, tray, item);
            box.SetCount(1);

            var label = box.GetComponentInChildren<TextMesh>(true);
            Assert.IsFalse(label.gameObject.activeInHierarchy, "Card is hidden until the box is picked up.");
            StringAssert.Contains("Does a test thing.", label.text, "Card explains what the item does.");

            box.SetSelected(true);
            Assert.IsTrue(label.gameObject.activeInHierarchy, "Selecting the box reveals its card.");

            box.SetSelected(false);
            Assert.IsFalse(label.gameObject.activeInHierarchy, "Deselecting hides the card again.");
        }
    }
}
