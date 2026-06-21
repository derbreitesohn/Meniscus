using Meniscus.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace Meniscus.Tests.EditMode
{
    public class LiquidBodyMeshTests
    {
        [Test]
        public void Build_ProducesCapWallAndBottomVertexCounts()
        {
            var data = LiquidBodyMesh.Build(0.39f, 0.32f, 6, 28, 5);

            // cap centre + cap rings + wall rings + bottom centre
            var expected = 1 + 6 * 28 + 5 * 28 + 1;
            Assert.AreEqual(expected, data.VertexCount);
            Assert.AreEqual(expected, data.Mesh.vertexCount);
            Assert.AreEqual(28, data.Segments);
        }

        [Test]
        public void Build_RimRingSitsAtRimRadius_AndFloorRingAtTaperedRadius()
        {
            const float rim = 0.39f;
            const float floor = 0.39f * 0.82f;
            var data = LiquidBodyMesh.Build(rim, floor, 6, 28, 5);

            var maxCapRadius = 0f;
            var minFloorRadius = float.MaxValue;

            for (var i = 0; i < data.VertexCount; i++)
            {
                var v = data.BaseVertices[i];
                var radius = Mathf.Sqrt(v.x * v.x + v.z * v.z);

                if (data.Kind[i] == LiquidVertexKind.Cap)
                    maxCapRadius = Mathf.Max(maxCapRadius, radius);

                if (data.Kind[i] == LiquidVertexKind.Wall && data.WallT[i] == 0f)
                    minFloorRadius = Mathf.Min(minFloorRadius, radius);
            }

            Assert.AreEqual(rim, maxCapRadius, 1e-4f);
            Assert.AreEqual(floor, minFloorRadius, 1e-4f);
        }

        [Test]
        public void Build_WallTSpansFloorToBelowRim_AndCapFacesUp()
        {
            var data = LiquidBodyMesh.Build(0.39f, 0.32f, 6, 28, 5);

            var minWallT = float.MaxValue;
            var maxWallT = float.MinValue;

            for (var i = 0; i < data.VertexCount; i++)
            {
                if (data.Kind[i] != LiquidVertexKind.Wall)
                    continue;

                minWallT = Mathf.Min(minWallT, data.WallT[i]);
                maxWallT = Mathf.Max(maxWallT, data.WallT[i]);
            }

            Assert.AreEqual(0f, minWallT, 1e-4f);
            Assert.Less(maxWallT, 1f);
            Assert.Greater(data.Mesh.normals[0].y, 0f); // cap centre normal points up
        }

        [Test]
        public void Build_HasNoNaNVertices()
        {
            var data = LiquidBodyMesh.Build(0.39f, 0.32f, 6, 28, 5);

            foreach (var v in data.BaseVertices)
                Assert.IsFalse(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z));
        }
    }
}
