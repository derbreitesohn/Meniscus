using Meniscus.UI;
using NUnit.Framework;
using UnityEngine;

namespace Meniscus.Tests.EditMode
{
    public class BookShopBuilderTests
    {
        GameObject host;

        [SetUp] public void SetUp() => host = new GameObject("BookHost");
        [TearDown] public void TearDown() { if (host != null) Object.DestroyImmediate(host); }

        [Test]
        public void BuildProp_CreatesNamedHierarchyWithHingeColliderAndClickTarget()
        {
            var p = new BookShopBuilder.BookPropParams
            {
                pageWidth = 0.30f, pageDepth = 0.38f, coverThickness = 0.02f,
                coverColor = new Color(0.34f, 0.16f, 0.08f), pageColor = new Color(0.86f, 0.78f, 0.6f),
            };

            var root = BookShopBuilder.BuildProp(host.transform, p);

            Assert.AreEqual(host.transform, root.parent);
            Assert.IsNotNull(root.Find("Book Hinge"), "Runtime resolves the hinge by this exact name.");
            Assert.IsNotNull(root.Find("Book Back Cover"));
            var collider = root.GetComponent<BoxCollider>();
            Assert.IsNotNull(collider);
            Assert.IsTrue(collider.isTrigger);
            Assert.IsNotNull(root.GetComponent<BookClickTarget>());
        }
    }
}
