using System.Collections.Generic;
using UnityEngine;

namespace Meniscus.Gameplay
{
    /// <summary>
    /// Which per-frame animation rule a liquid-body vertex follows, so
    /// <see cref="GlassVisualController"/> can recompute Y without re-deriving topology.
    /// </summary>
    public enum LiquidVertexKind
    {
        CapCenter,
        Cap,
        Wall,
        BottomCenter
    }

    /// <summary>The closed liquid mesh plus the per-vertex metadata its driver animates against.</summary>
    public struct LiquidBodyMeshData
    {
        public Mesh Mesh;
        public Vector3[] BaseVertices;
        public LiquidVertexKind[] Kind;
        public float[] RadiusNorm; // cap verts: 0..1 of the rim radius
        public float[] Nx;         // cap verts: cos(angle) * radiusNorm
        public float[] Nz;         // cap verts: sin(angle) * radiusNorm
        public float[] WallT;      // wall verts: 0 at the floor .. (<1) just below the rim
        public int Segments;
        public int VertexCount => BaseVertices == null ? 0 : BaseVertices.Length;
    }

    /// <summary>
    /// Builds a closed liquid body — an animated top cap disc, a (gently tapered) cylindrical side
    /// wall, and a bottom disc — as one mesh. Pure geometry: no Unity component and no per-frame
    /// state, so it is headless-testable and reused on every glass re-fit. The rim ring (the cap's
    /// outermost ring) is shared as the top of the wall, so there is no seam. The driver owns the Y
    /// animation; this lays out X/Z, seeds Y at 0, and tags each vertex with a
    /// <see cref="LiquidVertexKind"/>.
    /// </summary>
    public static class LiquidBodyMesh
    {
        /// <param name="rimRadius">Liquid radius at the surface/rim.</param>
        /// <param name="floorRadius">Liquid radius at the glass floor (taper).</param>
        /// <param name="radialRings">Cap rings from centre to rim (the surface-detail disc).</param>
        /// <param name="angularSegments">Vertices around each ring.</param>
        /// <param name="wallLevels">Horizontal wall rings from the floor up to (not incl.) the rim.</param>
        public static LiquidBodyMeshData Build(
            float rimRadius,
            float floorRadius,
            int radialRings,
            int angularSegments,
            int wallLevels)
        {
            var rings = Mathf.Max(2, radialRings);
            var segments = Mathf.Max(3, angularSegments);
            var walls = Mathf.Max(1, wallLevels);

            var capVertexCount = 1 + rings * segments;              // centre + cap rings (last = rim)
            var wallVertexCount = walls * segments;                 // floor .. just below rim
            var vertexCount = capVertexCount + wallVertexCount + 1; // + bottom centre

            var vertices = new Vector3[vertexCount];
            var kind = new LiquidVertexKind[vertexCount];
            var radiusNorm = new float[vertexCount];
            var nx = new float[vertexCount];
            var nz = new float[vertexCount];
            var wallT = new float[vertexCount];
            var uvs = new Vector2[vertexCount];

            // --- Cap: centre + rings; ring `rings` is the rim. ---
            vertices[0] = Vector3.zero;
            kind[0] = LiquidVertexKind.CapCenter;
            uvs[0] = new Vector2(0.5f, 0.5f);

            var vi = 1;
            var rimRingStart = 1 + (rings - 1) * segments;

            for (var ring = 1; ring <= rings; ring++)
            {
                var rNorm = (float)ring / rings;
                var radius = rNorm * rimRadius;

                for (var s = 0; s < segments; s++)
                {
                    var angle = (float)s / segments * Mathf.PI * 2f;
                    var cos = Mathf.Cos(angle);
                    var sin = Mathf.Sin(angle);

                    vertices[vi] = new Vector3(cos * radius, 0f, sin * radius);
                    kind[vi] = LiquidVertexKind.Cap;
                    radiusNorm[vi] = rNorm;
                    nx[vi] = cos * rNorm;
                    nz[vi] = sin * rNorm;
                    uvs[vi] = new Vector2(0.5f + cos * rNorm * 0.5f, 0.5f + sin * rNorm * 0.5f);
                    vi++;
                }
            }

            // --- Wall: floor (wallT 0) up to just below the rim. ---
            var wallStart = vi;

            for (var level = 0; level < walls; level++)
            {
                var t = (float)level / walls; // 0 floor .. (<1) below rim
                var radius = Mathf.Lerp(floorRadius, rimRadius, t);

                for (var s = 0; s < segments; s++)
                {
                    var angle = (float)s / segments * Mathf.PI * 2f;
                    var cos = Mathf.Cos(angle);
                    var sin = Mathf.Sin(angle);

                    vertices[vi] = new Vector3(cos * radius, 0f, sin * radius);
                    kind[vi] = LiquidVertexKind.Wall;
                    wallT[vi] = t;
                    uvs[vi] = new Vector2((float)s / segments, t);
                    vi++;
                }
            }

            // --- Bottom centre. ---
            var bottomCentre = vi;
            vertices[vi] = Vector3.zero;
            kind[vi] = LiquidVertexKind.BottomCenter;
            uvs[vi] = new Vector2(0.5f, 0.5f);

            var triangles = new List<int>(
                segments * 3                  // cap centre fan
                + (rings - 1) * segments * 6  // cap inter-ring quads
                + (walls - 1) * segments * 6  // wall inter-ring quads
                + segments * 6                // top wall ring -> rim ring
                + segments * 3);              // bottom fan

            // Cap centre fan -> first ring.
            for (var s = 0; s < segments; s++)
            {
                triangles.Add(0);
                triangles.Add(1 + (s + 1) % segments);
                triangles.Add(1 + s);
            }

            // Cap inter-ring quads (flat, face up).
            for (var ring = 1; ring < rings; ring++)
                AddRingQuads(triangles, 1 + (ring - 1) * segments, 1 + ring * segments, segments);

            // Wall inter-ring quads (face outward).
            for (var level = 0; level < walls - 1; level++)
                AddWallQuads(triangles, wallStart + level * segments, wallStart + (level + 1) * segments, segments);

            // Top wall ring -> rim ring (closes the seam).
            AddWallQuads(triangles, wallStart + (walls - 1) * segments, rimRingStart, segments);

            // Bottom fan (floor ring = wall level 0), faces down.
            for (var s = 0; s < segments; s++)
            {
                triangles.Add(bottomCentre);
                triangles.Add(wallStart + s);
                triangles.Add(wallStart + (s + 1) % segments);
            }

            var mesh = new Mesh { name = "Runtime Liquid Body Mesh" };
            mesh.MarkDynamic();
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            // Keep the cap facing up: if the centre normal points down the mesh is wound inward —
            // reverse every triangle so normals point outward.
            if (mesh.normals[0].y < 0f)
            {
                var tris = mesh.triangles;
                System.Array.Reverse(tris);
                mesh.triangles = tris;
                mesh.RecalculateNormals();
            }

            return new LiquidBodyMeshData
            {
                Mesh = mesh,
                BaseVertices = vertices,
                Kind = kind,
                RadiusNorm = radiusNorm,
                Nx = nx,
                Nz = nz,
                WallT = wallT,
                Segments = segments
            };
        }

        // Quads between an inner (smaller-radius) and outer (larger-radius) ring of a flat disc,
        // wound to face up.
        static void AddRingQuads(List<int> triangles, int innerStart, int outerStart, int segments)
        {
            for (var s = 0; s < segments; s++)
            {
                var i0 = innerStart + s;
                var i1 = innerStart + (s + 1) % segments;
                var o0 = outerStart + s;
                var o1 = outerStart + (s + 1) % segments;

                triangles.Add(i0);
                triangles.Add(o1);
                triangles.Add(o0);

                triangles.Add(i0);
                triangles.Add(i1);
                triangles.Add(o1);
            }
        }

        // Quads between a lower wall ring and the ring above it, wound to face outward.
        static void AddWallQuads(List<int> triangles, int lowerStart, int upperStart, int segments)
        {
            for (var s = 0; s < segments; s++)
            {
                var l0 = lowerStart + s;
                var l1 = lowerStart + (s + 1) % segments;
                var u0 = upperStart + s;
                var u1 = upperStart + (s + 1) % segments;

                triangles.Add(l0);
                triangles.Add(u0);
                triangles.Add(u1);

                triangles.Add(l0);
                triangles.Add(u1);
                triangles.Add(l1);
            }
        }
    }
}
