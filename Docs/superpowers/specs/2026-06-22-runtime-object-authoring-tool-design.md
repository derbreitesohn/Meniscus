# Runtime Object Authoring Tool — Lift Code-Built Objects Into the Scene

**Date:** 2026-06-22
**Status:** Approved (design) — pending implementation plan
**Topic:** An Editor tool that authors the objects currently built procedurally at runtime as persistent, repositionable scene objects / prefabs, wired to their controllers.

## Context

An audit of the procedural-creation call sites (`new GameObject`, `AddComponent`, `CreatePrimitive`, `new Material`, `Instantiate`) across `Assets/Scripts/` found that the project already follows an **author-preferred, runtime-fallback** pattern: most controllers expose `[SerializeField]` references and a `Configure(...)` path, with a code-builder that only runs when those references are null.

Cross-checking script GUIDs against `Assets/Scenes/Saloon.unity` confirmed that the HUD (`SaloonHudController`), end screen (`EndScreenManager`), shop (`ShopManager`), coin-drop controller (`CoinDropPresentationController`), the gameplay managers, and a proper `EventSystem` (with the new `InputSystemUIInputModule`, zero legacy modules) are **already authored** — so their code-build blocks are dead fallbacks.

Three things are built from scratch on **every** play with no authored object at all (0 instances in the scene): `BookShopView`, `DeskItemBar`, `PlayerInventory`. Two more author objects only via a runtime fallback because their serialized references ship unwired: the glass water surface (`GlassVisualController`) and the overspill effect (`GlassSpillEffect`).

This tool closes that gap. There is no final modelled art yet, so the tool reproduces the **current procedural look** — the same primitives and materials the code makes today — but as persistent, repositionable scene objects / prefabs that the designer can move, re-skin, and later swap for models. The runtime keeps building when an authored object is absent, so the game behaves identically whether or not the tool has been run.

The five targets, and their current creation sites:

| # | Target | Current runtime creation | Authored path today |
|---|--------|--------------------------|---------------------|
| 1 | Glass water surface | `GlassVisualController.cs:374` (`"Runtime Water Surface"`); material fallback `:321-323` | `waterTransform` (:19), `liquidMaterial` (:37) — both **unwired** |
| 2 | PlayerInventory | `GameManager.cs:700` (`AddComponent`) | `playerInventory` field exists — component **not in scene** |
| 3 | DeskItemBar | `GameManager.cs:707-708`; canvas/rows `DeskItemBar.cs:142-157, 119-127` | `barCanvas` (:19) only; `rowRoot` not serialized — component **not in scene** |
| 4 | Book Shop | `ShopManager.cs:232`; prop+canvas `BookShopView.cs:250-396, 540-570` | `ShopManager.bookShop` (:22) exists — component **not in scene**; no field for the pre-built prop |
| 5 | Spill VFX | `GlassSpillEffect.cs:35-39, 71, 98`; spawned from `GlassVisualController.cs:346-354` | none — built fresh per spill |

## Decisions locked

1. **Approach A — shared builder, fallback kept.** Each target's procedural build is extracted into a static `Build(...)` method in the runtime assembly. Both the runtime fallback *and* the Editor tool call it, so the authored result is identical to the fallback by construction and the two cannot drift.
2. **Reproduce the current look.** Builders produce the same geometry and materials the code makes today (primitive covers/pages, cube/cylinder spill, `RuntimeUiFactory` UI). No new art is invented; the output is a repositionable starting point.
3. **Purely additive — keep every fallback.** Runtime gains/keeps a serialized reference and uses it when set, else builds as before. Deleting the dead fallbacks identified by the audit is explicitly **out of scope** for this work.
4. **Scene objects for one-offs; prefabs for instanced things.** Book prop, water child, and the two manager components are authored into `Saloon.unity`. The spill effect and the shop/desk list *rows* — created repeatedly at runtime — become `.prefab` assets under a new `Assets/Prefabs/`.
5. **BookShopView escape hatch.** If extracting `BookShopBuilder` cleanly from the ~150-line `BookShopView.Build()` proves too invasive (it is entangled with animation/runtime state), the tool reimplements that one target's authoring itself (Approach B for #4 only). All other targets extract cleanly.
6. **Mirror the existing Editor-tool conventions** from `CoinSceneSetup` / `CoinModelWiring`: `Meniscus.Editor` static class, `Tools/Meniscus/…` menu items, `SerializedObject` wiring, `Undo` registration, load-asset-dependencies-up-front guards, refuse-to-save on missing preconditions, headless `Run()` entry point.

## Architecture

For each target the migration is three moves:

1. **Extract a builder.** A static `Build(...)` in the runtime assembly (Gameplay/UI), following the headless-testable precedent of `LiquidBodyMesh` and `CoinPoolBuilder`. Returns the created root / tuple of references.
2. **Add a serialized field** on the controller for the authored object (where one is missing). Runtime: `if (field != null) use it; else result = Build(...)`.
3. **Author + wire in the tool.** The Editor tool calls the builder in edit mode, parents the result into the scene (or `PrefabUtility.SaveAsPrefabAsset` for the instanced ones), and assigns the serialized field via `SerializedObject`.

**New files**

- `Assets/Scripts/Editor/RuntimeObjectAuthoring.cs` — the tool (`Meniscus.Editor`), menu items under `Tools/Meniscus/Author Runtime Objects/…`.
- Runtime builders: `WaterSurfaceBuilder` (Gameplay), `DeskItemBarBuilder` (UI), `BookShopBuilder` (UI), `SpillEffectBuilder` (Gameplay). Static classes, no MonoBehaviour lifecycle.
- `Assets/Prefabs/` — `SpillEffect.prefab`, `DeskItemRow.prefab`, `BookOrderRow.prefab`.

Dependency direction stays clean: Editor → Runtime (builders); the runtime never references Editor.

## Components

### New — runtime builders

Each extracts the existing creation code verbatim (same dimensions, same `RuntimeUiFactory` calls, same materials) into a static method:

- `WaterSurfaceBuilder.Build(Transform glassParent) → Transform` — the `"Liquid"` child with `MeshFilter` + `MeshRenderer` (mesh is filled per-frame at runtime, so the builder only creates the renderer host). Extracted from `GlassVisualController.CreateRuntimeWaterSurface` (`:374`).
- `DeskItemBarBuilder.Build(Transform parent) → (Canvas barCanvas, Transform rowRoot)` — the overlay canvas + row container. Extracted from `DeskItemBar.EnsureCanvas` (`:142-157`).
- `BookShopBuilder.Build(Transform deskAnchor) → Transform` — the book prop (back/front covers, hinge, page sheets, click collider) + world-space menu canvas shell. Extracted from `BookShopView.Build` (`:250-396`, `540-570`). *(Escape hatch per Decision 5.)*
- `SpillEffectBuilder.Build() → GameObject` — the overspill host with rivulet/puddle primitives + material. Extracted from `GlassSpillEffect.Spawn` (`:35-39, 71, 98`).

### Changed — serialized fields (additive)

- **`DeskItemBar`** — serialize the existing `rowRoot`; add `[SerializeField] GameObject rowPrefab` (the per-item "use" button, currently built by `RuntimeUiFactory.CreateButton` in the loop at `:119-127`). When `rowPrefab` is set, the rebuild `Instantiate`s it per item instead of building a button; else it falls back to the current loop.
- **`BookShopView`** — add `[SerializeField] Transform authoredBook` (skip `Build()` when set) and `[SerializeField] GameObject orderRowPrefab` (the per-item order row at `:358-376`).
- **`GlassVisualController`** — add `[SerializeField] GlassSpillEffect spillPrefab`. `SpawnSpillEffect` (`:346`) `Instantiate`s it when set, else calls the existing `GlassSpillEffect.Spawn` builder path.
- **`PlayerInventory`, water surface** — no new fields; `GameManager.playerInventory`, `GlassVisualController.waterTransform` / `liquidMaterial` already exist.

### New — `RuntimeObjectAuthoring` (the Editor tool)

Menu items under `Tools/Meniscus/Author Runtime Objects/`:

- `Author All` — runs all five in dependency order (PlayerInventory before the DeskItemBar wiring that references it).
- `Author Glass Water Surface`, `Author Player Inventory`, `Author Desk Item Bar`, `Author Book Shop`, `Author Spill Effect Prefab` — each runnable in isolation.
- `public static void Run()` — headless entry for `-batchmode -executeMethod Meniscus.Editor.RuntimeObjectAuthoring.Run`.

Per target the tool: `EnsureSaloonSceneOpen()` → `FindAnyObjectByType<T>()` for the controller → resolve/abort on missing asset dependencies → call the builder (or `SaveAsPrefabAsset`) → wire the serialized field via `SerializedObject` + `ApplyModifiedProperties` → `Undo.Register…` → `MarkSceneDirty` + `SaveScene`.

**Per-target authoring actions**

| # | Tool action | Field(s) wired |
|---|-------------|----------------|
| 1 | Resolve glass via `GlassVisualController`; build `"Liquid"` child; load `Assets/Art/Materials/Water_Surface.mat` | `waterTransform`, `liquidMaterial` |
| 2 | `AddComponent<PlayerInventory>` on the Managers object | `GameManager.playerInventory` |
| 3 | `AddComponent<DeskItemBar>` on Managers; build canvas+rowRoot; save `DeskItemRow.prefab` | `DeskItemBar.barCanvas`/`rowRoot`/`rowPrefab`, `GameManager.deskItemBar` |
| 4 | Author a "Book Shop" object with `BookShopView`; build prop at `deskAnchor`; save `BookOrderRow.prefab` | `BookShopView.authoredBook`/`orderRowPrefab`, `ShopManager.bookShop` |
| 5 | Build via `SpillEffectBuilder`; `SaveAsPrefabAsset` → `SpillEffect.prefab` | `GlassVisualController.spillPrefab` |

### Idempotency

Re-running must never produce `"Liquid (1)"`, a second book, or a duplicate component. Each authored object has a **stable name**; before creating, the tool resolves an existing instance — first via the serialized reference, then by name under the expected parent — and updates in place or skips. Prefabs re-save to the same asset path.

## Data flow

1. Developer runs `Tools ▸ Meniscus ▸ Author Runtime Objects ▸ Author All` (or one target).
2. Tool opens Saloon, builds each authored object, saves prefabs, wires every serialized field, saves the scene.
3. At runtime each controller checks its serialized reference: **set** → use the authored object and only *update* it (text, fill, visibility, animation); **null** → build at runtime exactly as today.
4. Designer repositions / re-skins the authored objects in the editor; runtime continues to drive them.

## Edge cases / error handling

- **Missing asset dependency** (e.g. `Water_Surface.mat` not at its path) → abort before touching the scene, `Debug.LogError` naming the missing asset (mirrors `CoinModelWiring`).
- **Target controller absent** from the scene → skip that target with a clear error; never save a half-wired scene.
- **Re-run** → idempotent (see above); no duplicates.
- **Tool never run** → runtime fallbacks fire; game is visually and behaviourally identical. This is the safety net that makes the change low-risk.
- **`deskAnchor` unresolved** for the book → abort target #4 with guidance to set the anchor first.

## Testing

EditMode (NUnit) against `Meniscus.Tests.EditMode`, using the existing GameObject-host `[SetUp]` / `Object.DestroyImmediate` `[TearDown]` conventions.

- **Builders are the unit under test** (pure construction, no live scene), following `LiquidBodyMeshTests` / `CoinPoolBuilderTests`:
  - `WaterSurfaceBuilder` → child has `MeshFilter` + `MeshRenderer`, parented correctly, identity local transform.
  - `DeskItemBarBuilder` → canvas + non-null `rowRoot` under it.
  - `SpillEffectBuilder` → host with the rivulet/puddle children + material.
  - `BookShopBuilder` → covers + hinge + canvas present (if extracted; skipped under the escape hatch).
- **Fallback parity** — for the controllers that gained a serialized field, assert that with the field null the runtime path still builds (existing behavior unbroken), and with the field set the build site is skipped.
- The existing EditMode suite (e.g. `PresentationPolishTests`) must stay green — all script changes are additive and fallback-preserving.
- **Editor-tool wiring** (`SerializedObject`, scene/prefab save) is integration-level and not cleanly unit-testable; it is covered by the runtime guards plus **manual verification**: run `Author All`, enter Play, confirm the look is identical and each object is now an editable scene/prefab instance.

## Future / out of scope

- **Deleting the dead runtime fallbacks** flagged by the audit (`SaloonHudController.EnsureFallbackHud`, `EndScreenManager.CreateRuntimeFallback`, `ShopManager.CreateFallbackShopCanvas`, the runtime `EventSystem`/`StandaloneInputModule` code, and the manager `AddComponent` fallbacks). A separate cleanup once authored objects are trusted.
- **Legacy `UnityEngine.UI.Text` → TextMeshPro** migration in `RuntimeUiFactory` and consumers.
- **Wiring `coinPrefab` + per-size `CoinModelLibrary`** so the placeholder cylinder coin paths never run (covered by the existing `Set Up Coin References` / `Wire Coin Models` tools).
- **Replacing the reproduced primitive placeholders with modelled art** — the whole point of making these authored is to enable exactly this, later.
