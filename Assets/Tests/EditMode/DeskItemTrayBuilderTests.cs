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

            var label = box.GetComponentInChildren<TextMesh>();
            StringAssert.Contains("TEST ITEM", label.text);
            StringAssert.Contains("x3", label.text);
        }
    }
}
