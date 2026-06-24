// Assets/Tests/EditMode/MenuLayoutTests.cs
using Meniscus.UI;
using NUnit.Framework;

namespace Meniscus.Tests.EditMode
{
    public class MenuLayoutTests
    {
        [Test]
        public void ComputeScale_PicksTheTighterOfWidthAndDepth()
        {
            // width-fit = 0.56/1040 ≈ 0.000538; depth-fit = 0.34/900 ≈ 0.000378 -> depth wins
            var scale = MenuLayout.ComputeScale(0.56f, 0.34f, 1040f, 900f);
            Assert.AreEqual(0.34f / 900f, scale, 1e-7f);
        }

        [Test]
        public void ComputeScale_KeepsCanvasWithinBothBounds()
        {
            const float w = 0.56f, d = 0.34f, pw = 1040f, ph = 1500f;
            var scale = MenuLayout.ComputeScale(w, d, pw, ph);
            Assert.LessOrEqual(scale * pw, w + 1e-6f);
            Assert.LessOrEqual(scale * ph, d + 1e-6f);
        }

        [Test]
        public void ComputeScale_ZeroPixelDimension_ReturnsZero()
        {
            Assert.AreEqual(0f, MenuLayout.ComputeScale(0.56f, 0.34f, 0f, 900f));
        }

        [Test]
        public void ItemsPerPage_FloorsAndIsAtLeastOne()
        {
            Assert.AreEqual(5, MenuLayout.ItemsPerPage(520f, 96f)); // floor(5.41)
            Assert.AreEqual(1, MenuLayout.ItemsPerPage(10f, 96f));  // floor(0.10) clamped up
        }

        [Test]
        public void PageCount_CeilsAndIsAtLeastOne()
        {
            Assert.AreEqual(1, MenuLayout.PageCount(0, 5));
            Assert.AreEqual(1, MenuLayout.PageCount(5, 5));
            Assert.AreEqual(2, MenuLayout.PageCount(6, 5));
            Assert.AreEqual(2, MenuLayout.PageCount(9, 5));
        }

        [Test]
        public void ClampPage_StaysInRange()
        {
            Assert.AreEqual(0, MenuLayout.ClampPage(-3, 2));
            Assert.AreEqual(1, MenuLayout.ClampPage(9, 2));
        }

        [Test]
        public void CrossedMidpoint_TrueOnlyWhenPassingHalf()
        {
            Assert.IsFalse(MenuLayout.CrossedMidpoint(0.1f, 0.4f));
            Assert.IsTrue(MenuLayout.CrossedMidpoint(0.4f, 0.5f));
            Assert.IsTrue(MenuLayout.CrossedMidpoint(0.49f, 0.8f));
            Assert.IsFalse(MenuLayout.CrossedMidpoint(0.6f, 0.9f));
        }
    }
}
