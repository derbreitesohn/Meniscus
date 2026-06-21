# Whiskey Liquid Body & Run-Down Overspill Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the glass's flat blue top-disc "water" with a closed amber whiskey body visible through the transparent glass, and replace the flat spill puddle with rivulets that run down the glass exterior.

**Architecture:** A new pure builder (`LiquidBodyMesh`) lays out a closed mesh — animated top cap + cylindrical side wall + bottom — once per glass re-fit. `GlassVisualController` keeps owning the per-frame animation but now bakes fill height into the cap/wall vertices instead of moving the surface transform, so the whole column grows with overflow risk. A new `GlassSpillEffect` MonoBehaviour plays the run-down on overflow and self-destructs. Color becomes a tunable bourbon-amber inspector field; glass floor depth/taper become tunable fields on `GlassTypeDefinition`.

**Tech Stack:** Unity 6000.3.6f1, URP/Lit transparent material, C#, NUnit EditMode tests (`Meniscus.Tests.EditMode`), Wwise audio (`AK.Wwise.Event`).

## Global Constraints

- Runtime gameplay code lives in `Assets/Scripts/Gameplay/` (assembly `Meniscus.Runtime`, namespace `Meniscus.Gameplay`).
- EditMode tests live in `Assets/Tests/EditMode/` (assembly `Meniscus.Tests.EditMode`, namespace `Meniscus.Tests.EditMode`), reference `Meniscus.Runtime`, `Unity.InputSystem`, `UnityEngine.UI`.
- Keep the existing static helpers `GlassVisualController.CalculateWaterSurfaceLocalY` and `GlassVisualController.CalculateWaterPulseScale` unchanged — `PresentationPolishTests` depends on them.
- Do NOT hand-edit FBX/GameObject scene references (project memory `fbx-root-fileid-changed`). Editing the ScriptableObject `.asset` YAML for plain serialized values is fine (project memory `wire-assets-offline`).
- Material setup must keep two-sided transparent (`_Cull` Off, `_ZWrite` 0, transparent render queue) so the back wall shows through the front.
- Spill visuals are runtime-built primitives (no authored prefabs), matching the existing `CreateSpillPuddle` style.
- Default bourbon amber color: `Color(0.55f, 0.27f, 0.05f, 0.82f)`. Default glass floor: `fillBottomLocalY = 0.06`, `fillBottomRadiusScale = 0.82`.

---

## Verification Toolkit

The Unity editor is usually open (holds `Temp/UnityLockfile`), so `-batchmode -runTests` can't run while it's open. Use these gates:

**A. Offline runtime compile check (always available — the primary fast gate).** Reuses Unity's Bee rsp + Roslyn. New `.cs` files are not yet in the rsp, so append them (the rsp's last line has no trailing newline — the leading `\n` in `printf` is required):

```bash
cd "C:/Users/Huti/Desktop/Git/Meniscus"
CSC="C:/Program Files/Unity/Hub/Editor/6000.3.6f1/Editor/Data/DotNetSdkRoslyn/csc.dll"
DAG="Library/Bee/artifacts/1900b0aE.dag"
SCRATCH="C:/Users/Huti/AppData/Local/Temp/claude/C--Users-Huti-Desktop-Git-Meniscus/e4810b98-9214-4e49-86ed-2e3daa1ec337/scratchpad"
RSP="$SCRATCH/runtime.rsp"
sed -E "s#^-out:.*#-out:\"$SCRATCH/Meniscus.Runtime.dll\"#; s#^-refout:.*#-refout:\"$SCRATCH/Meniscus.Runtime.ref.dll\"#" "$DAG/Meniscus.Runtime.rsp" > "$RSP"
# Append every NEW gameplay file created by this plan that is not yet in the rsp.
# Task >= 1: LiquidBodyMesh.cs   |   Task >= 5: also GlassSpillEffect.cs
printf '\n"Assets/Scripts/Gameplay/LiquidBodyMesh.cs"\n' >> "$RSP"
# printf '"Assets/Scripts/Gameplay/GlassSpillEffect.cs"\n' >> "$RSP"   # uncomment from Task 5 on
dotnet "$CSC" "@$RSP" && echo "RUNTIME COMPILE OK"
```

Expected on success: `RUNTIME COMPILE OK` and no `error CS` lines. This builds `Meniscus.Runtime.ref.dll` in the scratchpad, used by gate B.

**B. Offline test compile check (confirms test files compile against the new public surface).** Run gate A first (produces the fresh runtime ref). Then:

```bash
TRSP="$SCRATCH/tests.rsp"
sed -E "s#^-out:.*#-out:\"$SCRATCH/Meniscus.Tests.EditMode.dll\"#; s#^-refout:.*#-refout:\"$SCRATCH/Meniscus.Tests.EditMode.ref.dll\"#; s#$DAG/Meniscus.Runtime.ref.dll#$SCRATCH/Meniscus.Runtime.ref.dll#" "$DAG/Meniscus.Tests.EditMode.rsp" > "$TRSP"
printf '\n"Assets/Tests/EditMode/LiquidBodyMeshTests.cs"\n' >> "$TRSP"   # append new test files as created
dotnet "$CSC" "@$TRSP" && echo "TESTS COMPILE OK"
```

**C. Run the EditMode tests (authoritative test gate).** Either:
- **Editor open:** Unity → `Window > General > Test Runner` → EditMode → Run All (this compiles and runs). Confirm the named test(s) pass.
- **Editor closed:** `"C:/Program Files/Unity/Hub/Editor/6000.3.6f1/Editor/Unity.exe" -batchmode -projectPath "C:/Users/Huti/Desktop/Git/Meniscus" -runTests -testPlatform EditMode -testResults "$SCRATCH/results.xml" -logFile "$SCRATCH/tests.log"` then read `results.xml`.

**D. Play-mode visual check (the real gate for visuals).** Enter Play mode in the `Saloon` scene and observe (see per-task and Task 6 checklists).

> A "run the test and see it fail/pass" step below means gate **C**. The offline gates **A/B** are the quick compile sanity checks an agent can always run between edits.

---

## File Structure

- **Create** `Assets/Scripts/Gameplay/LiquidBodyMesh.cs` — pure geometry builder + `LiquidVertexKind` enum + `LiquidBodyMeshData` struct. (Task 1)
- **Create** `Assets/Tests/EditMode/LiquidBodyMeshTests.cs` — geometry invariants. (Task 1)
- **Modify** `Assets/Scripts/Gameplay/GlassVisualController.cs` — add `CalculateWallRingY` (Task 2); integrate the body mesh, bake fill into vertices, amber material, new `ConfigureSurface` signature (Task 4); wire the spill effect (Task 5).
- **Modify** `Assets/Tests/EditMode/PresentationPolishTests.cs` — add `CalculateWallRingY` + `GlassTypeDefinition.Create` floor tests. (Tasks 2, 3)
- **Modify** `Assets/Scripts/Gameplay/GlassTypeDefinition.cs` — floor depth/taper fields. (Task 3)
- **Modify** `Assets/GlassTypes/Standard Glass.asset` — floor field values. (Task 3)
- **Modify** `Assets/Scripts/Gameplay/GlassPresentationController.cs` — pass floor fields to `ConfigureSurface`. (Task 4)
- **Create** `Assets/Scripts/Gameplay/GlassSpillEffect.cs` — run-down + puddle MonoBehaviour. (Task 5)

---

## Task 1: `LiquidBodyMesh` geometry builder

**Files:**
- Create: `Assets/Scripts/Gameplay/LiquidBodyMesh.cs`
- Test: `Assets/Tests/EditMode/LiquidBodyMeshTests.cs`

**Interfaces:**
- Produces: `enum LiquidVertexKind { CapCenter, Cap, Wall, BottomCenter }`; `struct LiquidBodyMeshData { Mesh Mesh; Vector3[] BaseVertices; LiquidVertexKind[] Kind; float[] RadiusNorm; float[] Nx; float[] Nz; float[] WallT; int Segments; int VertexCount; }`; `static LiquidBodyMeshData LiquidBodyMesh.Build(float rimRadius, float floorRadius, int radialRings, int angularSegments, int wallLevels)`.

- [ ] **Step 1: Write the failing test**

Create `Assets/Tests/EditMode/LiquidBodyMeshTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the test to verify it fails**

Gate C (Test Runner, EditMode). Expected: FAIL/compile error — `LiquidBodyMesh`/`LiquidVertexKind` do not exist yet.

- [ ] **Step 3: Write the implementation**

Create `Assets/Scripts/Gameplay/LiquidBodyMesh.cs`:

```csharp
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
```

- [ ] **Step 4: Verify it compiles, then run the test to verify it passes**

Run gate A (append `LiquidBodyMesh.cs`), then gate B (append `LiquidBodyMeshTests.cs`), then gate C. Expected: `RUNTIME COMPILE OK`, `TESTS COMPILE OK`, and all four `LiquidBodyMeshTests` pass.

- [ ] **Step 5: Commit**

```bash
git add "Assets/Scripts/Gameplay/LiquidBodyMesh.cs" "Assets/Tests/EditMode/LiquidBodyMeshTests.cs"
git commit -m "feat(glass): closed liquid-body mesh builder"
```

---

## Task 2: `CalculateWallRingY` fill-column helper

**Files:**
- Modify: `Assets/Scripts/Gameplay/GlassVisualController.cs` (add one static method, near `CalculateWaterSurfaceLocalY` at the bottom of the class)
- Test: `Assets/Tests/EditMode/PresentationPolishTests.cs` (add one test method)

**Interfaces:**
- Produces: `static float GlassVisualController.CalculateWallRingY(float floorY, float rimY, float wallT)` — returns `Mathf.Lerp(floorY, rimY, Clamp01(wallT))`. Consumed by Task 4's per-frame loop.

- [ ] **Step 1: Write the failing test**

Add to `PresentationPolishTests` (e.g. after `CalculateWaterSurfaceLocalY_StaysNearTopWithTinyRiskRise`):

```csharp
        [Test]
        public void CalculateWallRingY_LerpsFloorToRimAndClamps()
        {
            Assert.AreEqual(0.06f, GlassVisualController.CalculateWallRingY(0.06f, 0.62f, 0f), 1e-4f);
            Assert.AreEqual(0.62f, GlassVisualController.CalculateWallRingY(0.06f, 0.62f, 1f), 1e-4f);
            Assert.AreEqual(0.34f, GlassVisualController.CalculateWallRingY(0.06f, 0.62f, 0.5f), 1e-4f);
            Assert.AreEqual(0.62f, GlassVisualController.CalculateWallRingY(0.06f, 0.62f, 2f), 1e-4f); // clamps
            Assert.AreEqual(0.06f, GlassVisualController.CalculateWallRingY(0.06f, 0.62f, -1f), 1e-4f); // clamps
        }
```

- [ ] **Step 2: Run the test to verify it fails**

Gate C. Expected: compile error — `CalculateWallRingY` does not exist.

- [ ] **Step 3: Write the implementation**

In `GlassVisualController.cs`, add this static method directly below the existing `CalculateWaterSurfaceLocalY` method (keep both existing static helpers as-is):

```csharp
        public static float CalculateWallRingY(float floorY, float rimY, float wallT)
        {
            return Mathf.Lerp(floorY, rimY, Mathf.Clamp01(wallT));
        }
```

- [ ] **Step 4: Verify compile + run the test to verify it passes**

Gate A (append `LiquidBodyMesh.cs`), then gate C. Expected: `RUNTIME COMPILE OK` and `CalculateWallRingY_LerpsFloorToRimAndClamps` passes (plus the existing `Calculate*` tests still pass).

- [ ] **Step 5: Commit**

```bash
git add "Assets/Scripts/Gameplay/GlassVisualController.cs" "Assets/Tests/EditMode/PresentationPolishTests.cs"
git commit -m "feat(glass): CalculateWallRingY fill-column helper"
```

---

## Task 3: Glass-type floor depth & taper

**Files:**
- Modify: `Assets/Scripts/Gameplay/GlassTypeDefinition.cs`
- Modify: `Assets/GlassTypes/Standard Glass.asset`
- Test: `Assets/Tests/EditMode/PresentationPolishTests.cs` (add one test method)

**Interfaces:**
- Produces: `float GlassTypeDefinition.FillBottomLocalY`, `float GlassTypeDefinition.FillBottomRadiusScale`; `Create(...)` gains two trailing optional params `float fillBottomLocalY = 0.06f, float fillBottomRadiusScale = 0.82f`. Consumed by Task 4 (`GlassPresentationController`).

- [ ] **Step 1: Write the failing test**

Add to `PresentationPolishTests`:

```csharp
        [Test]
        public void GlassTypeDefinition_Create_DefaultsLiquidFloorFit()
        {
            var glass = GlassTypeDefinition.Create("test", "Test Glass", null);

            Assert.AreEqual(0.06f, glass.FillBottomLocalY, 1e-4f);
            Assert.AreEqual(0.82f, glass.FillBottomRadiusScale, 1e-4f);

            Object.DestroyImmediate(glass);
        }
```

- [ ] **Step 2: Run the test to verify it fails**

Gate C. Expected: compile error — `FillBottomLocalY` / `FillBottomRadiusScale` do not exist.

- [ ] **Step 3: Write the implementation**

In `GlassTypeDefinition.cs`, add the two fields inside the `[Header("Water Fit")]` block (after `waterSurfaceRadius`):

```csharp
        [Tooltip("Local Y of the glass interior floor; the liquid body fills from here up to the surface.")]
        [SerializeField] float fillBottomLocalY = 0.06f;
        [Tooltip("Liquid radius at the floor as a fraction of the surface radius (gentle tumbler taper).")]
        [SerializeField, Range(0.1f, 1f)] float fillBottomRadiusScale = 0.82f;
```

Add the accessors (after `WaterSurfaceRadius`):

```csharp
        public float FillBottomLocalY => fillBottomLocalY;
        public float FillBottomRadiusScale => Mathf.Clamp(fillBottomRadiusScale, 0.1f, 1f);
```

Replace the `Create` method signature and body so the two new fields are set. The full method becomes:

```csharp
        public static GlassTypeDefinition Create(
            string id,
            string displayName,
            GameObject modelPrefab,
            string modelChildName = "",
            float waterSurfaceLocalY = 0.62f,
            float waterSurfaceRadius = 0.39f,
            float fillBottomLocalY = 0.06f,
            float fillBottomRadiusScale = 0.82f)
        {
            var glass = CreateInstance<GlassTypeDefinition>();
            glass.id = id;
            glass.displayName = displayName;
            glass.modelPrefab = modelPrefab;
            glass.modelChildName = modelChildName;
            glass.modelLocalScale = Vector3.one;
            glass.waterSurfaceLocalY = waterSurfaceLocalY;
            glass.waterSurfaceRadius = waterSurfaceRadius;
            glass.fillBottomLocalY = fillBottomLocalY;
            glass.fillBottomRadiusScale = fillBottomRadiusScale;
            glass.name = string.IsNullOrEmpty(id) ? displayName : id;
            return glass;
        }
```

In `Assets/GlassTypes/Standard Glass.asset`, append two lines after `waterSurfaceRadius: 0.39` (keep two-space indentation, no tabs):

```yaml
  fillBottomLocalY: 0.06
  fillBottomRadiusScale: 0.82
```

- [ ] **Step 4: Verify compile + run the test to verify it passes**

Gate A (append `LiquidBodyMesh.cs`), then gate C. Expected: `RUNTIME COMPILE OK` and `GlassTypeDefinition_Create_DefaultsLiquidFloorFit` passes.

- [ ] **Step 5: Commit**

```bash
git add "Assets/Scripts/Gameplay/GlassTypeDefinition.cs" "Assets/GlassTypes/Standard Glass.asset" "Assets/Tests/EditMode/PresentationPolishTests.cs"
git commit -m "feat(glass): glass-type floor depth & taper fit"
```

---

## Task 4: Integrate the liquid body into `GlassVisualController`

**Files:**
- Modify: `Assets/Scripts/Gameplay/GlassVisualController.cs` (full rewrite below)
- Modify: `Assets/Scripts/Gameplay/GlassPresentationController.cs` (the `ConfigureSurface` call)

**Interfaces:**
- Consumes: `LiquidBodyMesh.Build` + `LiquidBodyMeshData` (Task 1); `CalculateWallRingY` (Task 2); `GlassTypeDefinition.FillBottomLocalY` / `FillBottomRadiusScale` (Task 3).
- Produces: `void GlassVisualController.ConfigureSurface(float surfaceLocalY, float discRadius, float floorLocalY, float floorRadiusScale)` (4-arg signature, replaces the 2-arg one); `public static Material GlassVisualController.CreateTransparentLiquidMaterial(string name, Color color)` (consumed by Task 5).

> This task is mostly visual; its automated gate is "runtime compiles + existing `PresentationPolishTests` still green", and its real gate is the play-mode check in Step 4.

- [ ] **Step 1: Rewrite `GlassVisualController.cs`**

Replace the **entire** file `Assets/Scripts/Gameplay/GlassVisualController.cs` with (note: this keeps `CalculateWallRingY` from Task 2 and both legacy static helpers; it removes the old `BuildSurfaceMesh`/`CreateSpillPuddle` and stops moving the water transform):

```csharp
using Meniscus.Core;
using UnityEngine;

namespace Meniscus.Gameplay
{
    /// <summary>
    /// Drives the whiskey inside the glass as a closed liquid body: an animated top surface
    /// (surface-tension meniscus when calm, a convex danger dome as overflow nears, decaying
    /// ripples/slosh after each coin, plus a constant ambient micro-wobble), a cylindrical side
    /// wall visible through the transparent glass, and a bottom. Fill height is baked into the mesh
    /// vertices — not a moving transform — so the whole amber column visibly grows with overflow
    /// risk. The dome is driven by the same spill chance the camera and HUD use, so the liquid
    /// telegraphs whether the next pour overflows. On overflow it spawns a
    /// <see cref="GlassSpillEffect"/> that runs down the glass exterior.
    /// </summary>
    public class GlassVisualController : MonoBehaviour
    {
        [SerializeField] GlassManager glassManager;
        [SerializeField] Transform waterTransform;
        [SerializeField] float stableSurfaceLocalY = 0.62f;
        [SerializeField] float maxRiskSurfaceRise = 0.015f;
        [SerializeField] float riseSpeed = 1.1f;
        [SerializeField] float dangerWobbleAmplitude = 0.008f;
        [SerializeField] float spillPuddleLifetime = 1.4f;

        [Header("Surface Mesh")]
        [SerializeField, Min(0.05f)] float surfaceRadius = 0.39f;
        [SerializeField, Range(2, 64)] int radialRings = 6;
        [SerializeField, Range(3, 96)] int angularSegments = 28;

        [Header("Liquid Body")]
        [SerializeField] float fillBottomLocalY = 0.06f;
        [SerializeField, Range(0.1f, 1f)] float fillBottomRadiusScale = 0.82f;
        [SerializeField, Range(1, 16)] int wallLevels = 5;
        [SerializeField] Color liquidColor = new Color(0.55f, 0.27f, 0.05f, 0.82f);

        [Header("Meniscus")]
        [SerializeField] float meniscusRimClimb = 0.012f;
        [SerializeField, Range(0f, 0.99f)] float meniscusRimStart = 0.62f;
        [SerializeField] float dangerDomeHeight = 0.05f;
        [SerializeField] float dangerTrembleAmplitude = 0.006f;

        [Header("Surface Motion")]
        [SerializeField] float ambientAmplitude = 0.0035f;
        [SerializeField] float ambientSpatialScale = 2.4f;
        [SerializeField] float ambientSpeed = 0.35f;
        [SerializeField] float rippleAmplitude = 0.02f;
        [SerializeField] float rippleWavelength = 26f;
        [SerializeField] float rippleSpeed = 7f;
        [SerializeField] float rippleDecay = 2.6f;
        [SerializeField] float sloshAmplitude = 0.02f;
        [SerializeField] float sloshFrequency = 7.5f;
        [SerializeField] float sloshDecay = 1.8f;

        [Header("Overspill")]
        [SerializeField, Range(1, 12)] int spillRivuletCount = 4;
        [SerializeField] float spillRunDownDuration = 0.4f;
        [SerializeField] float spillSurfaceDip = 0.02f;
        [SerializeField] float spillTableDrop = 0.34f;

        [Header("Audio")]
        [SerializeField] AK.Wwise.Event waterSpill;

        Mesh waterMesh;
        Vector3[] baseVertices;
        Vector3[] workingVertices;
        float[] vertexRadius;
        float[] vertexNx;
        float[] vertexNz;
        LiquidVertexKind[] vertexKind;
        float[] vertexWallT;

        Vector3 waterLocalPosition;
        float currentSurfaceLocalY;
        float targetSurfaceLocalY;
        float currentRisk;
        float spillFlashTimer;

        float rippleKick;
        float rippleStartTime;
        float sloshKick;
        float sloshStartTime;
        Vector2 sloshDirection = Vector2.right;

        void OnEnable()
        {
            ResolveReferences();

            if (waterTransform == null)
                waterTransform = CreateRuntimeWaterSurface();

            SetupSurfaceRendererAndMesh();

            PinWaterTransform();
            currentSurfaceLocalY = stableSurfaceLocalY;
            targetSurfaceLocalY = stableSurfaceLocalY;

            if (glassManager != null)
            {
                glassManager.ProbabilityChanged += OnProbabilityChanged;
                glassManager.DropResolved += OnDropResolved;
                OnProbabilityChanged(glassManager.CurrentOverflowProbability);
            }
        }

        void OnDisable()
        {
            if (glassManager != null)
            {
                glassManager.ProbabilityChanged -= OnProbabilityChanged;
                glassManager.DropResolved -= OnDropResolved;
            }

            if (waterMesh != null)
            {
                Destroy(waterMesh);
                waterMesh = null;
            }
        }

        void Update()
        {
            if (waterTransform == null || waterMesh == null)
                return;

            if (!Mathf.Approximately(currentSurfaceLocalY, targetSurfaceLocalY))
            {
                currentSurfaceLocalY = Mathf.MoveTowards(
                    currentSurfaceLocalY,
                    targetSurfaceLocalY,
                    riseSpeed * Time.deltaTime);
            }

            if (spillFlashTimer > 0f)
                spillFlashTimer = Mathf.Max(0f, spillFlashTimer - Time.deltaTime);

            UpdateSurfaceVertices();
        }

        /// <summary>
        /// Re-fits the liquid to a glass shape: sets the calm surface height, disc radius, and the
        /// interior floor, then rebuilds the body mesh in place. Safe before or after OnEnable, so
        /// <see cref="GlassPresentationController"/> can call it when switching glass types.
        /// </summary>
        public void ConfigureSurface(float surfaceLocalY, float discRadius, float floorLocalY, float floorRadiusScale)
        {
            stableSurfaceLocalY = surfaceLocalY;
            surfaceRadius = Mathf.Max(0.05f, discRadius);
            fillBottomLocalY = floorLocalY;
            fillBottomRadiusScale = Mathf.Clamp(floorRadiusScale, 0.1f, 1f);

            // Not enabled yet (or no surface): OnEnable will build with the new values.
            if (!isActiveAndEnabled || waterTransform == null)
                return;

            if (waterMesh != null)
            {
                Destroy(waterMesh);
                waterMesh = null;
            }

            SetupSurfaceRendererAndMesh();
            PinWaterTransform();

            currentSurfaceLocalY = stableSurfaceLocalY;
            targetSurfaceLocalY = CalculateWaterSurfaceLocalY(currentRisk, stableSurfaceLocalY, maxRiskSurfaceRise);
        }

        void OnProbabilityChanged(float probability)
        {
            currentRisk = probability;
            targetSurfaceLocalY = CalculateWaterSurfaceLocalY(probability, stableSurfaceLocalY, maxRiskSurfaceRise);

            if (Mathf.Approximately(probability, 0f))
                currentSurfaceLocalY = targetSurfaceLocalY;
        }

        void OnDropResolved(GlassDropResult result)
        {
            KickRipple(result.Overflowed ? 1.6f : 1f);
            KickSlosh(result.Overflowed ? 1.5f : 1f);

            if (result.Overflowed)
            {
                spillFlashTimer = 1f;
                currentSurfaceLocalY = Mathf.Max(fillBottomLocalY, currentSurfaceLocalY - spillSurfaceDip);
                SpawnSpillEffect();
                waterSpill?.Post(gameObject);
                return;
            }

            var trueSpillChance = glassManager == null
                ? result.TrueSpillChance
                : glassManager.CurrentTrueSpillChance;

            if (trueSpillChance > 0f)
                spillFlashTimer = 0.25f;
        }

        void UpdateSurfaceVertices()
        {
            var t = Time.time;
            var danger = glassManager == null
                ? Mathf.Clamp01(currentRisk / GameConstants.MaxOverflowProbability)
                : Mathf.Clamp01(glassManager.CurrentTrueSpillChance / GameConstants.MaxOverflowProbability);

            var rippleElapsed = t - rippleStartTime;
            var rippleEnvelope = rippleKick > 0f ? rippleKick * Mathf.Exp(-rippleElapsed * rippleDecay) : 0f;
            var sloshElapsed = t - sloshStartTime;
            var sloshEnvelope = sloshKick > 0f ? sloshKick * Mathf.Exp(-sloshElapsed * sloshDecay) : 0f;
            var sloshPhase = Mathf.Sin(sloshElapsed * sloshFrequency);

            var surfaceBaseY = currentSurfaceLocalY;
            var floorY = Mathf.Min(fillBottomLocalY, surfaceBaseY);

            for (var i = 0; i < workingVertices.Length; i++)
            {
                float y;

                switch (vertexKind[i])
                {
                    case LiquidVertexKind.Wall:
                        y = CalculateWallRingY(floorY, surfaceBaseY, vertexWallT[i]);
                        break;

                    case LiquidVertexKind.BottomCenter:
                        y = floorY;
                        break;

                    default: // CapCenter, Cap
                        var r = vertexRadius[i];
                        var nx = vertexNx[i];
                        var nz = vertexNz[i];

                        // Concave meniscus: liquid clings up the wall while calm, giving way to the dome
                        // as danger rises.
                        var rimT = meniscusRimStart >= 1f
                            ? 0f
                            : Mathf.Clamp01((r - meniscusRimStart) / (1f - meniscusRimStart));
                        var concave = meniscusRimClimb * Mathf.SmoothStep(0f, 1f, rimT) * (1f - danger);

                        // Convex dome: surface tension straining a bulge above the rim as overflow nears.
                        var dome = dangerDomeHeight * danger * Mathf.Clamp01(1f - r * r);

                        var tremble = danger > 0f
                            ? Mathf.Sin(t * Mathf.Lerp(7f, 20f, danger) + (nx + nz) * 5f)
                                * (dangerTrembleAmplitude + dangerWobbleAmplitude) * danger
                            : 0f;

                        var ambient = (Mathf.PerlinNoise(
                            nx * ambientSpatialScale + t * ambientSpeed,
                            nz * ambientSpatialScale - t * ambientSpeed) - 0.5f) * ambientAmplitude;

                        var ripple = rippleEnvelope != 0f
                            ? Mathf.Sin(r * rippleWavelength - rippleElapsed * rippleSpeed)
                                * rippleEnvelope * Mathf.Clamp01(1f - r * 0.15f)
                            : 0f;

                        var slosh = sloshEnvelope != 0f
                            ? (nx * sloshDirection.x + nz * sloshDirection.y) * sloshPhase * sloshEnvelope
                            : 0f;

                        var spill = spillFlashTimer > 0f
                            ? Mathf.Sin(spillFlashTimer * Mathf.PI * 8f) * 0.01f * (1f - r)
                            : 0f;

                        y = surfaceBaseY + concave + dome + tremble + ambient + ripple + slosh + spill;
                        break;
                }

                workingVertices[i] = new Vector3(baseVertices[i].x, y, baseVertices[i].z);
            }

            waterMesh.vertices = workingVertices;
            waterMesh.RecalculateNormals();
            waterMesh.RecalculateBounds();
        }

        void KickRipple(float strength)
        {
            rippleKick = rippleAmplitude * Mathf.Max(0f, strength);
            rippleStartTime = Time.time;
        }

        void KickSlosh(float strength)
        {
            sloshKick = sloshAmplitude * Mathf.Max(0f, strength);
            sloshStartTime = Time.time;

            var angle = Random.Range(0f, Mathf.PI * 2f);
            sloshDirection = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        void SetupSurfaceRendererAndMesh()
        {
            BuildLiquidMesh();

            var collider = waterTransform.GetComponent<Collider>();

            if (collider != null)
                Destroy(collider);

            var meshFilter = waterTransform.GetComponent<MeshFilter>();

            if (meshFilter == null)
                meshFilter = waterTransform.gameObject.AddComponent<MeshFilter>();

            meshFilter.sharedMesh = waterMesh;

            var meshRenderer = waterTransform.GetComponent<MeshRenderer>();

            if (meshRenderer == null)
                meshRenderer = waterTransform.gameObject.AddComponent<MeshRenderer>();

            meshRenderer.sharedMaterial = CreateTransparentLiquidMaterial(
                "Runtime Whiskey Liquid Material", liquidColor);
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Vertex heights carry the surface detail, so the authored y-squash must not flatten them.
            waterTransform.localScale = Vector3.one;
        }

        void BuildLiquidMesh()
        {
            var floorRadius = surfaceRadius * Mathf.Clamp(fillBottomRadiusScale, 0.1f, 1f);
            var data = LiquidBodyMesh.Build(surfaceRadius, floorRadius, radialRings, angularSegments, wallLevels);

            waterMesh = data.Mesh;
            baseVertices = data.BaseVertices;
            workingVertices = new Vector3[baseVertices.Length];
            System.Array.Copy(baseVertices, workingVertices, baseVertices.Length);
            vertexRadius = data.RadiusNorm;
            vertexNx = data.Nx;
            vertexNz = data.Nz;
            vertexKind = data.Kind;
            vertexWallT = data.WallT;
        }

        void SpawnSpillEffect()
        {
            var rimWorldY = transform.TransformPoint(new Vector3(0f, currentSurfaceLocalY, 0f)).y;

            GlassSpillEffect.Spawn(
                transform,
                transform.position,
                surfaceRadius,
                rimWorldY,
                transform.position.y - spillTableDrop,
                liquidColor,
                spillRivuletCount,
                spillRunDownDuration,
                spillPuddleLifetime);
        }

        void PinWaterTransform()
        {
            var lp = waterTransform.localPosition;
            waterTransform.localPosition = new Vector3(lp.x, 0f, lp.z);
            waterLocalPosition = waterTransform.localPosition;
        }

        Transform CreateRuntimeWaterSurface()
        {
            var waterObject = new GameObject("Runtime Water Surface", typeof(MeshFilter), typeof(MeshRenderer));
            waterObject.transform.SetParent(transform, false);
            waterObject.transform.localPosition = Vector3.zero;
            waterObject.transform.localScale = Vector3.one;
            return waterObject.transform;
        }

        public static Material CreateTransparentLiquidMaterial(string materialName, Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader)
            {
                name = materialName,
                color = color
            };

            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Smoothness", 0.9f);
            material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return material;
        }

        void ResolveReferences()
        {
            if (glassManager == null)
                glassManager = FindAnyObjectByType<GlassManager>();
        }

        public static Vector3 CalculateWaterPulseScale(Vector3 waterScale, float pulseAmount)
        {
            var pulse = Mathf.Max(0f, pulseAmount);
            return new Vector3(
                waterScale.x + pulse,
                waterScale.y,
                waterScale.z + pulse);
        }

        public static float CalculateWaterSurfaceLocalY(
            float probability,
            float stableSurfaceLocalY,
            float maxRise)
        {
            var riskNormalized = Mathf.Clamp01(probability / GameConstants.MaxOverflowProbability);
            return stableSurfaceLocalY + Mathf.Max(0f, maxRise) * riskNormalized;
        }

        public static float CalculateWallRingY(float floorY, float rimY, float wallT)
        {
            return Mathf.Lerp(floorY, rimY, Mathf.Clamp01(wallT));
        }
    }
}
```

> Note: this references `GlassSpillEffect.Spawn`, which does not exist until Task 5. To keep this task independently compilable, do Step 2 first.

- [ ] **Step 2: Add a temporary `GlassSpillEffect` stub so this task compiles alone**

Create `Assets/Scripts/Gameplay/GlassSpillEffect.cs` as a minimal stub (Task 5 replaces it fully):

```csharp
using UnityEngine;

namespace Meniscus.Gameplay
{
    // Temporary stub — replaced with the full run-down effect in Task 5.
    public class GlassSpillEffect : MonoBehaviour
    {
        public static GlassSpillEffect Spawn(
            Transform parent,
            Vector3 glassWorldPosition,
            float rimRadius,
            float rimWorldY,
            float tableWorldY,
            Color color,
            int rivuletCount,
            float runDownDuration,
            float puddleLifetime)
        {
            return null;
        }
    }
}
```

- [ ] **Step 3: Update the `GlassPresentationController` call site**

In `Assets/Scripts/Gameplay/GlassPresentationController.cs`, replace the `ConfigureSurface` call near the end of `SetGlassType`:

```csharp
            if (waterController != null)
                waterController.ConfigureSurface(type.WaterSurfaceLocalY, type.WaterSurfaceRadius);
```

with:

```csharp
            if (waterController != null)
                waterController.ConfigureSurface(
                    type.WaterSurfaceLocalY,
                    type.WaterSurfaceRadius,
                    type.FillBottomLocalY,
                    type.FillBottomRadiusScale);
```

- [ ] **Step 4: Verify compile + existing tests green + play-mode visual**

1. Gate A (append `LiquidBodyMesh.cs` **and** `GlassSpillEffect.cs`). Expected: `RUNTIME COMPILE OK`.
2. Gate C: all of `PresentationPolishTests` and `LiquidBodyMeshTests` still pass (the legacy `CalculateWaterPulseScale` / `CalculateWaterSurfaceLocalY` tests must remain green).
3. Gate D (play mode, `Saloon` scene): confirm
   - an **amber** liquid column is visible **through the glass walls from the side**, not just a top disc;
   - the calm surface sits where the old blue disc did (no vertical jump);
   - pouring coins makes the **whole column grow**, and the meniscus/dome still read on top;
   - (overflow still does nothing visible beyond the surface flash — the run-down arrives in Task 5).

   If the side wall renders unexpectedly dark, swap the two `triangles.Add(...)` blocks inside `LiquidBodyMesh.AddWallQuads` (reverse wall winding) and re-check — this only affects wall normal direction, not topology.

- [ ] **Step 5: Commit**

```bash
git add "Assets/Scripts/Gameplay/GlassVisualController.cs" "Assets/Scripts/Gameplay/GlassSpillEffect.cs" "Assets/Scripts/Gameplay/GlassPresentationController.cs"
git commit -m "feat(glass): amber liquid body baked into the mesh, fill grows with risk"
```

---

## Task 5: Overspill that runs down the glass exterior

**Files:**
- Modify: `Assets/Scripts/Gameplay/GlassSpillEffect.cs` (replace the Task 4 stub with the full effect)
- Test: `Assets/Tests/EditMode/PresentationPolishTests.cs` (add one test method)

**Interfaces:**
- Consumes: `GlassVisualController.CreateTransparentLiquidMaterial` (Task 4); called by `GlassVisualController.SpawnSpillEffect` (Task 4) via the `Spawn(...)` signature already used there.
- Produces: `static float GlassSpillEffect.RunDownProgress(float elapsed, float duration)`.

- [ ] **Step 1: Write the failing test**

Add to `PresentationPolishTests`:

```csharp
        [Test]
        public void RunDownProgress_RisesFromZeroToOneAndClamps()
        {
            Assert.AreEqual(0f, GlassSpillEffect.RunDownProgress(0f, 0.4f), 1e-4f);
            Assert.AreEqual(1f, GlassSpillEffect.RunDownProgress(0.4f, 0.4f), 1e-4f);
            Assert.AreEqual(1f, GlassSpillEffect.RunDownProgress(1f, 0.4f), 1e-4f); // clamps past the end

            var mid = GlassSpillEffect.RunDownProgress(0.2f, 0.4f);
            Assert.Greater(mid, 0f);
            Assert.Less(mid, 1f);
        }
```

- [ ] **Step 2: Run the test to verify it fails**

Gate C. Expected: compile error — `RunDownProgress` does not exist on the stub.

- [ ] **Step 3: Replace the stub with the full effect**

Replace the **entire** file `Assets/Scripts/Gameplay/GlassSpillEffect.cs` with:

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Meniscus.Gameplay
{
    /// <summary>
    /// One-shot overspill visual: amber rivulets run down the outside of the glass to the table,
    /// then a puddle spreads and fades. Spawned by <see cref="GlassVisualController"/> on overflow
    /// and self-destructs when finished. All primitives are built at runtime — no authored prefabs.
    /// </summary>
    public class GlassSpillEffect : MonoBehaviour
    {
        const float RivuletWidth = 0.05f;
        const float RivuletDepth = 0.015f;
        const float RivuletRadiusOffset = 0.015f;

        Material material;
        Color baseColor;
        readonly List<Renderer> renderers = new();

        public static GlassSpillEffect Spawn(
            Transform parent,
            Vector3 glassWorldPosition,
            float rimRadius,
            float rimWorldY,
            float tableWorldY,
            Color color,
            int rivuletCount,
            float runDownDuration,
            float puddleLifetime)
        {
            var host = new GameObject("Runtime Overspill Effect");
            host.transform.SetParent(parent, worldPositionStays: true);

            var effect = host.AddComponent<GlassSpillEffect>();
            effect.baseColor = color;
            effect.material = GlassVisualController.CreateTransparentLiquidMaterial("Runtime Overspill Material", color);
            effect.StartCoroutine(effect.Run(
                glassWorldPosition,
                rimRadius,
                rimWorldY,
                tableWorldY,
                Mathf.Max(1, rivuletCount),
                Mathf.Max(0.05f, runDownDuration),
                Mathf.Max(0.1f, puddleLifetime)));

            return effect;
        }

        IEnumerator Run(
            Vector3 glassWorldPosition,
            float rimRadius,
            float rimWorldY,
            float tableWorldY,
            int rivuletCount,
            float runDownDuration,
            float puddleLifetime)
        {
            var runHeight = Mathf.Max(0.01f, rimWorldY - tableWorldY);
            var streaks = new Transform[rivuletCount];
            var directions = new Vector3[rivuletCount];

            for (var i = 0; i < rivuletCount; i++)
            {
                var angle = (i + Random.value) / rivuletCount * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                directions[i] = dir;

                var streak = CreatePrimitive(PrimitiveType.Cube, "Overspill Rivulet").transform;
                streak.rotation = Quaternion.LookRotation(dir, Vector3.up);
                streaks[i] = streak;
            }

            // Phase 1 — rivulets grow downward from the rim to the table.
            var elapsed = 0f;
            while (elapsed < runDownDuration)
            {
                elapsed += Time.deltaTime;
                var length = runHeight * RunDownProgress(elapsed, runDownDuration);
                var centreY = rimWorldY - length * 0.5f;

                for (var i = 0; i < rivuletCount; i++)
                {
                    var dir = directions[i];
                    streaks[i].position = new Vector3(
                        glassWorldPosition.x + dir.x * (rimRadius + RivuletRadiusOffset),
                        centreY,
                        glassWorldPosition.z + dir.z * (rimRadius + RivuletRadiusOffset));
                    streaks[i].localScale = new Vector3(RivuletWidth, Mathf.Max(0.001f, length), RivuletDepth);
                }

                yield return null;
            }

            // Phase 2 — puddle spreads on the table while everything fades out.
            var puddle = CreatePrimitive(PrimitiveType.Cylinder, "Overspill Puddle").transform;
            puddle.position = new Vector3(glassWorldPosition.x, tableWorldY, glassWorldPosition.z);

            var fade = 0f;
            while (fade < puddleLifetime)
            {
                fade += Time.deltaTime;
                var k = Mathf.Clamp01(fade / puddleLifetime);
                var spread = Mathf.Lerp(0.3f, 1f, Mathf.Sqrt(k)) * rimRadius * 2.6f;
                puddle.localScale = new Vector3(spread, 0.006f, spread);
                SetAlpha(Mathf.Lerp(baseColor.a, 0f, k));
                yield return null;
            }

            Destroy(gameObject);
        }

        /// <summary>Eased 0..1 reveal of how far the rivulets have run from the rim toward the table.</summary>
        public static float RunDownProgress(float elapsed, float duration)
        {
            if (duration <= 0f)
                return 1f;

            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
        }

        GameObject CreatePrimitive(PrimitiveType type, string primitiveName)
        {
            var primitive = GameObject.CreatePrimitive(type);
            primitive.name = primitiveName;
            primitive.transform.SetParent(transform, worldPositionStays: true);

            var collider = primitive.GetComponent<Collider>();

            if (collider != null)
                Destroy(collider);

            var renderer = primitive.GetComponent<Renderer>();

            if (renderer != null)
            {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderers.Add(renderer);
            }

            return primitive;
        }

        void SetAlpha(float alpha)
        {
            var c = baseColor;
            c.a = alpha;

            if (material != null)
                material.color = c;
        }

        void OnDestroy()
        {
            if (material != null)
                Destroy(material);
        }
    }
}
```

- [ ] **Step 4: Verify compile + run the test to verify it passes + play-mode visual**

1. Gate A (append `LiquidBodyMesh.cs` and `GlassSpillEffect.cs`). Expected: `RUNTIME COMPILE OK`.
2. Gate C: `RunDownProgress_RisesFromZeroToOneAndClamps` passes; all prior tests still pass.
3. Gate D (play mode): force an overflow (pour into a near-full glass). Confirm amber rivulets run down the **outside** of the glass to the table, a puddle spreads and fades, the surface dips then settles, and **no blue** appears anywhere. The effect object removes itself afterward (no `Runtime Overspill Effect` left in the hierarchy).

- [ ] **Step 5: Commit**

```bash
git add "Assets/Scripts/Gameplay/GlassSpillEffect.cs" "Assets/Tests/EditMode/PresentationPolishTests.cs"
git commit -m "feat(glass): overspill rivulets run down the glass exterior"
```

---

## Task 6: Full verification & tuning pass

**Files:** none required (commit only if tuning changes serialized defaults).

- [ ] **Step 1: Full offline compile of both assemblies**

Run gate A (append both new files) then gate B (append `LiquidBodyMeshTests.cs`). Expected: `RUNTIME COMPILE OK` and `TESTS COMPILE OK`, zero `error CS`.

- [ ] **Step 2: Run the entire EditMode suite**

Gate C, Run All. Expected: green, including the pre-existing tests. (Note from project memory: ~5 `GameFlowTests` may already fail pre-existing on this branch, unrelated to this work — confirm the failures are the same set as before these changes, not new ones.)

- [ ] **Step 3: Play-mode acceptance checklist** (gate D, `Saloon` scene)

- [ ] Amber whiskey column visible through the glass sides at rest (not just a top disc).
- [ ] Calm surface height unchanged from before (no vertical pop on load).
- [ ] Pouring coins grows the whole column; meniscus (calm) and danger dome (near overflow) still read.
- [ ] Press `G` to cycle glass types (if more than one): the liquid re-fits without errors.
- [ ] Overflow → rivulets run down the exterior, puddle spreads and fades, surface dips then recovers.
- [ ] No blue anywhere; no leftover `Runtime Overspill Effect` objects after a spill.
- [ ] No errors/exceptions in the Console.

- [ ] **Step 4: Tune defaults if needed**

Adjust serialized fields live on the `WaterSurface`'s `GlassVisualController` and on `Standard Glass.asset` (`fillBottomLocalY`, `fillBottomRadiusScale`, `liquidColor`, `wallLevels`, `spill*`) until it reads right. If you change values you want to keep, copy them into `Standard Glass.asset` / the scene component and commit:

```bash
git add -A
git commit -m "chore(glass): tune whiskey liquid + overspill defaults"
```

---

## Self-Review

**Spec coverage:**
- "Full liquid body (cap + wall + bottom)" → Tasks 1 (builder) + 4 (integration). ✓
- "Fill height baked into vertices, column grows with risk" → Task 4 (`UpdateSurfaceVertices`, `PinWaterTransform`, transform no longer moved). ✓
- "Bourbon amber, exposed & reused by spill" → Task 4 (`liquidColor`, `CreateTransparentLiquidMaterial`), Task 5 (spill reuses it). ✓
- "Configurable floor depth/taper, on GlassTypeDefinition + asset" → Task 3 + Task 4 passthrough. ✓
- "Overspill runs down the exterior (rivulets + puddle + surface dip)" → Task 5 + the `OnDropResolved` dip in Task 4. ✓
- "Keep CalculateWaterSurfaceLocalY / CalculateWaterPulseScale" → preserved verbatim in Task 4. ✓
- "Two new small files" → `LiquidBodyMesh`, `GlassSpillEffect`. ✓
- "Degenerate floor >= surface clamps" → Task 4 `Mathf.Min(fillBottomLocalY, surfaceBaseY)`; builder tolerates `floorRadius==rimRadius`. ✓
- "EditMode tests: mesh invariants + column math" → Task 1 (4 tests), Task 2 (`CalculateWallRingY`), Task 5 (`RunDownProgress`), Task 3 (`Create`). ✓
- "Offline compile check per memory" → Verification Toolkit gates A/B + every task Step 4. ✓

**Placeholder scan:** No TBD/TODO. The Task 4 `GlassSpillEffect` stub is an explicit, complete, compilable bridge (replaced in Task 5), not a placeholder.

**Type consistency:** `LiquidBodyMesh.Build` signature and `LiquidBodyMeshData` field names match their use in Task 4 `BuildLiquidMesh`. `GlassSpillEffect.Spawn` parameter list is identical in the Task 4 stub, the Task 4 caller (`SpawnSpillEffect`), and the Task 5 full file. `ConfigureSurface(float,float,float,float)` matches the Task 4 `GlassPresentationController` call. `FillBottomLocalY` / `FillBottomRadiusScale` names match across Tasks 3 and 4. `CalculateWallRingY(floorY, rimY, wallT)` matches between Tasks 2 and 4.
