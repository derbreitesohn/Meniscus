# Whiskey Liquid Body & Run-Down Overspill

**Date:** 2026-06-21
**Status:** Approved (design) — pending implementation plan
**Topic:** Make the glass liquid a visible amber whiskey body (not just a top disc) and improve the overspill to run down the glass exterior.

## Context

`Assets/Scripts/Gameplay/GlassVisualController.cs` drives the liquid inside the saloon glass.
Today it renders a **single flat radial-disc mesh** (the "water surface"): a center vertex
plus `radialRings` rings out to `surfaceRadius`, generated at runtime on the `WaterSurface`
child object. Per frame, the disc's vertices are displaced in Y to fake a meniscus rim-climb
when calm, a convex danger dome as overflow nears, decaying ripples/slosh after each coin, and
an ambient wobble. Fill height is faked by **moving the whole `WaterSurface` transform up/down**
(`currentSurfaceLocalY`, driven by `CalculateWaterSurfaceLocalY` + `riseSpeed`).

Three problems this design fixes:

1. **No liquid body.** The mesh is only a top disc. The glass model is transparent, so through
   its walls you see *empty glass* below the disc — there is no liquid column.
2. **Wrong color.** The material is water-blue (`Color(0.14, 0.42, 0.62, 0.5)`), hardcoded in
   `CreateTransparentWaterMaterial`. The spill puddle uses the same blue.
3. **Weak overspill.** On overflow, `CreateSpillPuddle` spawns one flat grey-blue cylinder
   primitive below the glass for `spillPuddleLifetime` (1.4s) plus a brief sine-wobble flash on
   the surface. It doesn't read as liquid breaching the rim.

Wiring facts confirmed before this design:

- `WaterSurface` is wired in `Saloon.unity` (`waterTransform` → fileID 1776584097);
  `stableSurfaceLocalY: 0.62`, `surfaceRadius: 0.39`.
- The active glass is fitted by `GlassPresentationController.SetGlassType` →
  `waterController.ConfigureSurface(type.WaterSurfaceLocalY, type.WaterSurfaceRadius)`.
  `GlassTypeDefinition` carries `WaterSurfaceLocalY` (0.62) and `WaterSurfaceRadius` (0.39).
- `CalculateWaterPulseScale` is **dead at runtime** (only its own definition + a
  `PresentationPolishTests` test reference it). `CoinDropPresentationController` does not touch
  the water transform/scale. So moving fill height from the transform into the mesh vertices
  breaks nothing — but both static helpers are kept so `PresentationPolishTests` stays green.

## Decisions locked (from brainstorming)

1. **Full liquid body.** Build a closed liquid mesh — animated top cap + cylindrical side wall
   + bottom cap — so the amber column is visible through the transparent glass and the fill
   level visibly grows with overflow risk. (Chosen over a plain translucent column and a
   minimal shell.)
2. **Classic bourbon amber.** Start color ≈ `RGB(0.55, 0.27, 0.05)`, translucent (alpha ≈ 0.82),
   glossy. Exposed as an inspector field for live tuning. (Chosen over deep mahogany and light
   golden scotch.)
3. **Overspill runs down the glass exterior.** Amber rivulets breach the rim and run down the
   outside to the table, then a puddle spreads and fades; the surface dips briefly. (Chosen over
   a splash-burst and a polished-puddle-only option.)
4. **Defaults to tune in-engine.** Glass interior floor starts at local Y `0.06` with a gentle
   tumbler taper (`fillBottomRadiusScale` `0.82`); these are exposed and refined live in Unity.
5. **Two new small files** (`LiquidBodyMesh`, `GlassSpillEffect`) so `GlassVisualController`
   stays a focused orchestrator.

## Components

### New — `LiquidBodyMesh` (pure geometry builder)

A static builder (no `MonoBehaviour`) that constructs the closed liquid mesh and returns it plus
per-vertex metadata the controller animates against. Headless-testable in EditMode.

**Inputs:** `rimRadius`, `floorRadius`, `radialRings`, `angularSegments`, `wallRings`.
(All Y is applied later by the controller; the builder works in a fill-normalized frame.)

**Topology (one `Mesh`):**

- **Top cap** — center vertex + `radialRings` rings (radius `0 → rimRadius`), center-fan +
  inter-ring quads, wound to face up. This is the existing surface disc; its **outer ring is the
  rim**.
- **Side wall** — `wallRings` ring-pairs from the floor (`wallT = 0`, radius `floorRadius`) up to
  the rim (`wallT = 1`, radius `rimRadius`), radius lerped by `wallT`; quads wound to face
  outward. The **top wall ring is a separate (coincident-at-rim) ring** from the cap rim so the
  wall keeps a near-vertical normal and the cap a near-horizontal one (crisp rim, not a smoothed
  blob); the controller keeps the two rings' Y in sync each frame.
- **Bottom cap** — center vertex at the floor + the lowest wall ring, fan wound to face down.

**Returned metadata (parallel arrays, one entry per vertex):**

- `kind` — `CapCenter | Cap | Wall | BottomCenter | BottomRing` (drives the per-frame Y formula).
- `radiusNorm`, `nx`, `nz` — for cap/rim verts (meniscus, dome, ambient, tremble, slosh), as today.
- `wallT` — for wall verts (`0` floor … `1` rim), the fill-height parameter.

Material/cull stays two-sided transparent so the back wall shows through the front (liquid
depth). `RecalculateNormals` after each rebuild.

### New — `GlassSpillEffect` (self-managing run-down + puddle)

A `MonoBehaviour` spawned on overflow via a static `Spawn(...)` factory that creates a temporary
child GameObject, runs a coroutine, and destroys itself when finished. All visuals are runtime
primitives (no authored prefabs/scene wiring needed).

**Spawn params:** glass world position, `rimRadius`, rim world Y, table Y, `liquidColor`,
`rivuletCount`, `runDownDuration`, `puddleLifetime`.

**Animation (coroutine):**

- **Rivulets** — `rivuletCount` (3–5) thin amber streaks at random angles just outside the rim
  (radius `≈ rimRadius + ε`), pivoted at the rim. Their downward extent reveals from rim → table
  over `runDownDuration` (~0.4s) so they appear to flow down, then fade.
- **Puddle** — a flat amber disc on the table that spreads outward from zero as the rivulets
  land, then fades over `puddleLifetime`.
- Self-destructs after the longest child finishes.

### Changed — `GlassVisualController`

- **Fill height moves into the mesh.** `WaterSurface` transform Y becomes **fixed** (local
  `(x, 0, z)`); `currentSurfaceLocalY` (still driven by `CalculateWaterSurfaceLocalY` +
  `riseSpeed` MoveTowards, unchanged) becomes the **base Y of the cap + top wall ring**. The
  transform no longer moves.
- **`Update` rebuilds all vertices** via the body formula:
  - *Cap/rim verts:* `Y = currentSurfaceLocalY + (concave + dome + tremble + ambient + ripple +
    slosh + spill)` — the existing formula, now offset by `currentSurfaceLocalY` instead of the
    transform.
  - *Wall verts:* `Y = CalculateWallRingY(floorY, rimY, wallT)` where `rimY = currentSurfaceLocalY`
    (its meniscus/slosh handled at the rim ring), with a `wallT`-scaled fraction of the slosh tilt
    blended into the upper wall so the column sways near the top. The top wall ring's Y is set
    equal to the cap rim Y each frame (seam stays closed).
  - *Bottom verts:* fixed at `floorY`.
  - `RecalculateNormals` / `RecalculateBounds` as today.
- **`OnDropResolved` overflow path:** keep ripple + slosh kicks and the `waterSpill` Wwise event;
  replace `CreateSpillPuddle` with `GlassSpillEffect.Spawn(...)`; apply a brief **surface dip**
  (lower `currentSurfaceLocalY` by `spillSurfaceDip`, settling back via the next
  `OnProbabilityChanged`). Removes `CreateSpillPuddle`.
- **Material:** `CreateTransparentWaterMaterial` → produces bourbon-amber translucent material
  from the new `liquidColor` field (default `Color(0.55, 0.27, 0.05, 0.82)`); reused by the
  spill. Smoothness stays high. (Renamed conceptually to "liquid", method name kept or renamed —
  no external callers.)
- **New serialized fields** (all tunable in Inspector): `fillBottomLocalY` (0.06),
  `fillBottomRadiusScale` (0.82), `wallRings` (e.g. 5), `liquidColor` (bourbon amber),
  `spillRivuletCount` (4), `spillRunDownDuration` (0.4), `spillSurfaceDip` (~0.02). Existing
  meniscus/dome/ripple/slosh/ambient fields are unchanged and keep driving the cap.
- **`ConfigureSurface`** gains floor params: `ConfigureSurface(surfaceLocalY, rimRadius,
  floorLocalY, floorRadiusScale)`; rebuilds the body mesh in place (disposes the old mesh first,
  as today). Degenerate `floorY >= surfaceLocalY` (empty/short glass) clamps the wall to zero
  height — cap only, still a valid mesh.
- **New pure helper** `static float CalculateWallRingY(float floorY, float rimY, float wallT)` =
  `Mathf.Lerp(floorY, rimY, Mathf.Clamp01(wallT))` — testable headless.
- **Kept unchanged:** `CalculateWaterSurfaceLocalY` and `CalculateWaterPulseScale` (so
  `PresentationPolishTests` stays green).

### Changed — `GlassTypeDefinition` + `Standard Glass.asset`

- Add serialized `fillBottomLocalY` (default 0.06) and `fillBottomRadiusScale` (default 0.82)
  with accessors, and add them to the `Create(...)` factory params.
- Add the two fields to `Standard Glass.asset` YAML (Unity defaults them on recompile; set
  explicitly for clarity). Editing a ScriptableObject `.asset` by hand is fine per project
  memory; no FBX/GameObject refs change.

### Changed — `GlassPresentationController`

- `SetGlassType` passes the two new fields through:
  `waterController.ConfigureSurface(type.WaterSurfaceLocalY, type.WaterSurfaceRadius,
  type.FillBottomLocalY, type.FillBottomRadiusScale)`.

## Data flow

1. `GlassPresentationController.SetGlassType` → `ConfigureSurface(surfaceY, rimRadius, floorY,
   floorScale)` → `LiquidBodyMesh.Build(...)` produces the cap+wall+bottom mesh + metadata;
   `WaterSurface` transform pinned at fixed local position.
2. Each frame `Update` recomputes vertex Y (cap by the displacement formula at base
   `currentSurfaceLocalY`; wall by `CalculateWallRingY`; bottom fixed) → the amber column is
   visible through the glass and grows/shrinks with risk.
3. `OnProbabilityChanged` sets `targetSurfaceLocalY` (unchanged math); the column rises toward it.
4. `OnDropResolved`:
   - safe pour → ripple/slosh kick (as today).
   - overflow → ripple/slosh kick + surface dip + `GlassSpillEffect.Spawn` (rivulets run down the
     exterior, puddle spreads & fades) + `waterSpill` event.

## Edge cases / error handling

- `floorY >= surfaceLocalY` → wall clamped to zero height; cap-only mesh, no NaN.
- Probability 0 (calm) → column sits at the base fill height; snap as today.
- `ConfigureSurface` before `OnEnable` → fields stored; `OnEnable` builds with them (existing
  pattern preserved).
- Glass switch → old mesh disposed before rebuild (existing `Destroy(waterMesh)` pattern).
- `GlassSpillEffect` always self-destructs (coroutine end), so overflow never leaks objects.

## Testing

EditMode (NUnit), `Meniscus.Tests.EditMode`, GameObject-host `[SetUp]` /
`Object.DestroyImmediate` `[TearDown]` conventions.

- `LiquidBodyMesh.Build`: vertex & triangle counts match `radialRings`/`angularSegments`/
  `wallRings`; bottom-ring radius == `rimRadius * floorRadiusScale`; rim ring radius ==
  `rimRadius`; metadata `kind`/`wallT` ranges correct; mesh has no NaN vertices; is closed
  (every edge shared) for a non-degenerate fill.
- `CalculateWallRingY`: `wallT 0 → floorY`, `1 → rimY`, monotonic, clamps outside `[0,1]`.
- Degenerate `floorY >= rimY` → wall height clamps to 0, build still succeeds.
- Existing `CalculateWaterSurfaceLocalY` and `CalculateWaterPulseScale` tests remain unchanged
  and green.
- Offline compile check (Bee rsp + Roslyn) before claiming done, per project memory.
- **PlayMode / manual:**
  - Confirm an amber liquid column is visible through the glass walls from the side, not just a
    top disc.
  - Raise overflow risk (pour coins) → the whole column visibly grows; meniscus/dome still read.
  - Force an overflow → amber rivulets run down the outside to the table, puddle spreads & fades,
    surface dips then settles. No blue anywhere.

## Future / out of scope

- Refraction/caustics or a custom liquid shader (staying on URP/Lit transparent for now).
- Per-glass-shape interior profiles beyond a single floor Y + taper scale (e.g. curved tumbler
  walls).
- Particle-system splash (the run-down is code-driven primitives to match the existing style).
- Multiple liquid colors per glass type / drink type.
