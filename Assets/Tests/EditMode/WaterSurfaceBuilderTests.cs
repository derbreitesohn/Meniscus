using Meniscus.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace Meniscus.Tests.EditMode
{
    public class WaterSurfaceBuilderTests
    {
        GameObject glass;

        [SetUp] public void SetUp() => glass = new GameObject("Glass");
        [TearDown] public void TearDown() { if (glass != null) Object.DestroyImmediate(glass); }

        [Test]
        public void Build_CreatesNamedChildWithMeshFilterAndRenderer()
        {
            var water = WaterSurfaceBuilder.Build(glass.transform, WaterSurfaceBuilder.AuthoredName);

            Assert.AreEqual(glass.transform, water.parent);
            Assert.AreEqual("Liquid", water.name);
            Assert.IsNotNull(water.GetComponent<MeshFilter>());
            Assert.IsNotNull(water.GetComponent<MeshRenderer>());
            Assert.AreEqual(Vector3.zero, water.localPosition);
            Assert.AreEqual(Vector3.one, water.localScale);
        }
    }
}
