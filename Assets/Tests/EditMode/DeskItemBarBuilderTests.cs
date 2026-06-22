using Meniscus.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Meniscus.Tests.EditMode
{
    public class DeskItemBarBuilderTests
    {
        GameObject host;

        [SetUp] public void SetUp() => host = new GameObject("DeskItemBarHost");
        [TearDown] public void TearDown() { if (host != null) Object.DestroyImmediate(host); }

        [Test]
        public void Build_CreatesOverlayCanvasWithRowContainer()
        {
            var (canvas, rowRoot) = DeskItemBarBuilder.Build(host.transform);

            Assert.IsNotNull(canvas);
            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode);
            Assert.IsFalse(canvas.enabled, "Bar canvas must start disabled (shown only on the player's turn).");
            Assert.IsNotNull(rowRoot);
            Assert.AreEqual(canvas.transform, rowRoot.parent);
        }
    }
}
