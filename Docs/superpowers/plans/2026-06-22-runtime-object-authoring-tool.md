# Runtime Object Authoring Tool Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An Editor tool that authors the objects currently built procedurally at runtime (glass water surface, PlayerInventory, DeskItemBar, Book Shop prop, spill VFX) as persistent, repositionable scene objects/prefabs wired to their controllers, with every runtime fallback kept intact.

**Architecture:** Approach A — for each target, extract its build into a static `Build(...)` in the runtime assembly that both the runtime fallback and the tool call (one source of truth). Add a serialized reference on the controller; runtime uses it when set, else builds as before. The Editor tool (`Meniscus.Editor`) calls the builders in edit mode, parents the result into `Saloon.unity` (or saves a `.prefab`), and wires the field via `SerializedObject`. Purely additive — nothing is deleted, so the game behaves identically whether or not the tool has run.

**Tech Stack:** Unity `6000.3.6f1`, C#, NUnit EditMode tests, assemblies `Meniscus.Runtime` / `Meniscus.Editor` / `Meniscus.Tests.EditMode`.

## Global Constants

- **Editor version:** `6000.3.6f1` (path `/Applications/Unity/Hub/Editor/6000.3.6f1/Unity.app`).
- **Assemblies & namespaces:** builders + runtime changes live in `Meniscus.Runtime` (`Assets/Scripts/...`, namespaces `Meniscus.Gameplay` / `Meniscus.UI`). The tool lives in `Meniscus.Editor` (`Assets/Scripts/Editor/`, already references `Meniscus.Runtime`). Tests live in `Meniscus.Tests.EditMode` (`Assets/Tests/EditMode/`, namespace `Meniscus.Tests.EditMode`).
- **Keep every fallback.** No runtime build code is deleted in this plan. Each change adds an authored path and leaves the existing `null → build` branch as the fallback.
- **Edit-mode-safe destroy.** Any builder that destroys a component must use `if (Application.isPlaying) Object.Destroy(x); else Object.DestroyImmediate(x);` — `Object.Destroy` is deferred and throws/silently fails in edit mode.
- **Scene path:** `Assets/Scenes/Saloon.unity`. **Material path:** `Assets/Art/Materials/Water_Surface.mat` (exists). **Prefabs go in:** `Assets/Prefabs/` (created in Task 9).
- **Stable names** (idempotency): water `"Liquid"`, book `"Book Shop"`, spill prefab `Assets/Prefabs/SpillEffect.prefab`. The tool resolves an existing object by serialized ref then by name before creating, so re-runs never duplicate.
- **Offline compile-check** (the project's "before done" bar — no `error CS`/`warning CS`, clean exit):
  ```bash
  CSC="/Applications/Unity/Hub/Editor/6000.3.6f1/Unity.app/Contents/Resources/Scripting/DotNetSdkRoslyn/csc.dll"
  DAG=$(dirname "$(find Library/Bee/artifacts -name 'Meniscus.Runtime.rsp' | head -1)")
  dotnet "$CSC" "@$DAG/Meniscus.Runtime.rsp"
  dotnet "$CSC" "@$DAG/Meniscus.Editor.rsp"
  dotnet "$CSC" "@$DAG/Meniscus.Tests.EditMode.rsp"
  ```
  (Run from the project root. If `Meniscus.Editor.rsp` is absent, the Editor assembly hasn't been compiled yet — open Unity once to generate the Bee artifacts.)
- **Running EditMode tests** (behavioral RED/GREEN happens in-engine): Unity ▸ `Window ▸ General ▸ Test Runner ▸ EditMode ▸ Run All`, or headless:
  ```bash
  "/Applications/Unity/Hub/Editor/6000.3.6f1/Unity.app/Contents/MacOS/Unity" \
    -batchmode -projectPath . -runTests -testPlatform EditMode \
    -testResults Logs/editmode-results.xml -quit
  ```
- **Commits:** stage only the files named in each task (`git add <exact paths>`), never `git add .` — the working tree has unrelated coin-feature WIP that must stay uncommitted. Unity generates `.meta` files for new files/folders on next editor focus; stage them alongside their files.
- **`.meta` note:** after creating a new `.cs` file or folder, let Unity import it (focus the editor) so a `.meta` is generated, then commit the `.cs`/folder *and* its `.meta` together.

---

### Task 1: WaterSurfaceBuilder (extract the liquid host)

**Files:**
- Create: `Assets/Scripts/Gameplay/WaterSurfaceBuilder.cs`
- Modify: `Assets/Scripts/Gameplay/GlassVisualController.cs` (OnEnable `:95-96`; delete `CreateRuntimeWaterSurface` `:429-436`)
- Test: `Assets/Tests/EditMode/WaterSurfaceBuilderTests.cs`

**Interfaces:**
- Produces: `Meniscus.Gameplay.WaterSurfaceBuilder.Build(Transform glass, string name) → Transform`; constants `DefaultName = "Runtime Water Surface"`, `AuthoredName = "Liquid"`.

- [ ] **Step 1: Write the failing test**

```csharp
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
```

- [ ] **Step 2: Run offline compile-check — expect RED**

Run the compile-check (Global Constants). Expected: `error CS0103`/`CS0117` — `WaterSurfaceBuilder` does not exist.

- [ ] **Step 3: Create the builder**

```csharp
using UnityEngine;

namespace Meniscus.Gameplay
{
    /// <summary>
    /// Builds the liquid surface host (a MeshFilter+MeshRenderer child of the glass) for
    /// <see cref="GlassVisualController"/>. Shared by the runtime fallback and the editor authoring
    /// tool so the authored object is identical to the runtime-built one.
    /// </summary>
    public static class WaterSurfaceBuilder
    {
        public const string DefaultName = "Runtime Water Surface";
        public const string AuthoredName = "Liquid";

        public static Transform Build(Transform glass, string name)
        {
            var waterObject = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            waterObject.transform.SetParent(glass, false);
            waterObject.transform.localPosition = Vector3.zero;
            waterObject.transform.localScale = Vector3.one;
            return waterObject.transform;
        }
    }
}
```

- [ ] **Step 4: Point the runtime fallback at the builder**

In `GlassVisualController.cs`, replace the OnEnable lines `:95-96`:

```csharp
            if (waterTransform == null)
                waterTransform = WaterSurfaceBuilder.Build(transform, WaterSurfaceBuilder.DefaultName);
```

Then delete the now-unused `CreateRuntimeWaterSurface()` method (`:429-436`).

- [ ] **Step 5: Run offline compile-check — expect clean**

Expected: clean exit, no `error CS` / `warning CS`.

- [ ] **Step 6: Run EditMode tests — expect GREEN**

Run the EditMode suite (Global Constants). Expected: `WaterSurfaceBuilderTests` passes; existing suite still green.

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Gameplay/WaterSurfaceBuilder.cs Assets/Scripts/Gameplay/WaterSurfaceBuilder.cs.meta \
        Assets/Scripts/Gameplay/GlassVisualController.cs \
        Assets/Tests/EditMode/WaterSurfaceBuilderTests.cs Assets/Tests/EditMode/WaterSurfaceBuilderTests.cs.meta
git commit -m "refactor: extract WaterSurfaceBuilder shared by runtime + authoring"
```

---

### Task 2: Editor tool skeleton + Author Glass Water Surface

**Files:**
- Create: `Assets/Scripts/Editor/RuntimeObjectAuthoring.cs`

**Interfaces:**
- Consumes: `WaterSurfaceBuilder.Build` / `.AuthoredName` (Task 1).
- Produces: `static bool RuntimeObjectAuthoring.AuthorWaterSurface()` (returns true if the scene changed); helpers `TryOpenScene(out Scene)`, `SaveScene(Scene)` reused by later tasks.

- [ ] **Step 1: Create the tool with the water command + shared helpers**

```csharp
using Meniscus.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Meniscus.Editor
{
    /// <summary>
    /// Authors the objects that GlassVisualController/GameManager/ShopManager otherwise build at
    /// runtime as persistent scene objects/prefabs, wiring each controller's serialized reference.
    /// Idempotent: resolves an existing object (by serialized ref, then by name) before creating.
    /// Mirrors CoinSceneSetup's conventions (open scene, SerializedObject wiring, Undo, save).
    /// </summary>
    static class RuntimeObjectAuthoring
    {
        const string ScenePath = "Assets/Scenes/Saloon.unity";
        const string WaterMaterialPath = "Assets/Art/Materials/Water_Surface.mat";
        const string MenuRoot = "Tools/Meniscus/Author Runtime Objects/";

        [MenuItem(MenuRoot + "Author Glass Water Surface")]
        static void AuthorWaterSurfaceMenu()
        {
            if (!TryOpenScene(out var scene))
                return;

            if (AuthorWaterSurface())
                SaveScene(scene);
        }

        public static bool AuthorWaterSurface()
        {
            var glass = Object.FindAnyObjectByType<GlassVisualController>();

            if (glass == null)
            {
                Debug.LogError("[RuntimeObjectAuthoring] No GlassVisualController in the scene. Aborting.");
                return false;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(WaterMaterialPath);

            if (material == null)
            {
                Debug.LogError($"[RuntimeObjectAuthoring] Missing '{WaterMaterialPath}'. Aborting without changes.");
                return false;
            }

            var serialized = new SerializedObject(glass);
            var waterProp = serialized.FindProperty("waterTransform");
            var materialProp = serialized.FindProperty("liquidMaterial");

            var water = waterProp.objectReferenceValue as Transform;

            if (water == null)
                water = glass.transform.Find(WaterSurfaceBuilder.AuthoredName);

            if (water == null)
            {
                water = WaterSurfaceBuilder.Build(glass.transform, WaterSurfaceBuilder.AuthoredName);
                Undo.RegisterCreatedObjectUndo(water.gameObject, "Author Water Surface");
            }

            waterProp.objectReferenceValue = water;
            materialProp.objectReferenceValue = material;
            serialized.ApplyModifiedProperties();

            Debug.Log("[RuntimeObjectAuthoring] Water surface authored ('Liquid') and wired (waterTransform + Water_Surface.mat).");
            return true;
        }

        static bool TryOpenScene(out Scene scene)
        {
            var active = EditorSceneManager.GetActiveScene();
            scene = active.IsValid() && active.path == ScenePath
                ? active
                : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            if (!scene.IsValid())
            {
                Debug.LogError($"[RuntimeObjectAuthoring] Could not open '{ScenePath}'. Aborting.");
                return false;
            }

            return true;
        }

        static void SaveScene(Scene scene)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
    }
}
```

- [ ] **Step 2: Run offline compile-check — expect clean**

Compile `Meniscus.Editor` (Global Constants). Expected: clean exit.

- [ ] **Step 3: Manual verification in Unity**

Open Unity. Run `Tools ▸ Meniscus ▸ Author Runtime Objects ▸ Author Glass Water Surface`. Confirm: a `"Liquid"` child appears under the glass with a MeshFilter+MeshRenderer; the `GlassVisualController`'s `Water Transform` = `Liquid` and `Liquid Material` = `Water_Surface.mat` in the Inspector; re-running does **not** create `"Liquid (1)"`. Enter Play — the whiskey looks identical.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Editor/RuntimeObjectAuthoring.cs Assets/Scripts/Editor/RuntimeObjectAuthoring.cs.meta
git commit -m "feat: editor tool authors glass water surface"
```

---

### Task 3: Author Player Inventory

**Files:**
- Modify: `Assets/Scripts/Editor/RuntimeObjectAuthoring.cs`

**Interfaces:**
- Consumes: `TryOpenScene`, `SaveScene` (Task 2); `Meniscus.Core.GameManager`, `Meniscus.Items.PlayerInventory`.
- Produces: `static bool AuthorPlayerInventory()`.

- [ ] **Step 1: Add the command**

Add these usings to `RuntimeObjectAuthoring.cs`: `using Meniscus.Core;` and `using Meniscus.Items;`. Add inside the class:

```csharp
        [MenuItem(MenuRoot + "Author Player Inventory")]
        static void AuthorPlayerInventoryMenu()
        {
            if (!TryOpenScene(out var scene))
                return;

            if (AuthorPlayerInventory())
                SaveScene(scene);
        }

        public static bool AuthorPlayerInventory()
        {
            var manager = Object.FindAnyObjectByType<GameManager>();

            if (manager == null)
            {
                Debug.LogError("[RuntimeObjectAuthoring] No GameManager in the scene. Aborting.");
                return false;
            }

            // The managers all live on the GameManager's GameObject (same object GameManager.AddComponent uses).
            var inventory = Object.FindAnyObjectByType<PlayerInventory>();

            if (inventory == null)
            {
                Undo.RegisterCompleteObjectUndo(manager.gameObject, "Author Player Inventory");
                inventory = manager.gameObject.AddComponent<PlayerInventory>();
            }

            var serialized = new SerializedObject(manager);
            serialized.FindProperty("playerInventory").objectReferenceValue = inventory;
            serialized.ApplyModifiedProperties();

            Debug.Log("[RuntimeObjectAuthoring] PlayerInventory authored on the Managers object and wired.");
            return true;
        }
```

- [ ] **Step 2: Run offline compile-check — expect clean**

- [ ] **Step 3: Manual verification in Unity**

Run `Tools ▸ Meniscus ▸ Author Runtime Objects ▸ Author Player Inventory`. Confirm a `PlayerInventory` component now sits on the Managers GameObject and `GameManager`'s `Player Inventory` field points to it. Re-run: no duplicate component (`[DisallowMultipleComponent]` also guards this).

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Editor/RuntimeObjectAuthoring.cs
git commit -m "feat: editor tool authors PlayerInventory onto the managers object"
```

---

### Task 4: DeskItemBarBuilder + serialize rowRoot

**Files:**
- Create: `Assets/Scripts/UI/DeskItemBarBuilder.cs`
- Modify: `Assets/Scripts/UI/DeskItemBar.cs` (`rowRoot` field `:21`; `EnsureCanvas` `:137-158`)
- Test: `Assets/Tests/EditMode/DeskItemBarBuilderTests.cs`

**Interfaces:**
- Produces: `Meniscus.UI.DeskItemBarBuilder.Build(Transform parent) → (Canvas barCanvas, Transform rowRoot)`.

- [ ] **Step 1: Write the failing test**

```csharp
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
```

- [ ] **Step 2: Run offline compile-check — expect RED** (`DeskItemBarBuilder` missing).

- [ ] **Step 3: Create the builder (move the EnsureCanvas body)**

```csharp
using UnityEngine;
using UnityEngine.UI;

namespace Meniscus.UI
{
    /// <summary>
    /// Builds the desk item bar's overlay canvas + transparent row container. Shared by the runtime
    /// fallback (<see cref="DeskItemBar.EnsureCanvas"/>) and the editor authoring tool.
    /// </summary>
    public static class DeskItemBarBuilder
    {
        public static (Canvas barCanvas, Transform rowRoot) Build(Transform parent)
        {
            var barCanvas = RuntimeUiFactory.CreateOverlayCanvas(parent, "Desk Item Bar Canvas", enabled: false);

            var row = RuntimeUiFactory.CreateImage(
                barCanvas.transform,
                "Desk Item Row",
                new Vector2(1500f, 84f),
                new Vector2(0f, 130f),
                new Color(0f, 0f, 0f, 0f));   // transparent container; buttons carry the look

            var rect = row.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 130f);

            return (barCanvas, row.transform);
        }
    }
}
```

- [ ] **Step 4: Serialize `rowRoot` and route `EnsureCanvas` through the builder**

In `DeskItemBar.cs`, change the field at `:21` from `Transform rowRoot;` to:

```csharp
        [SerializeField] Transform rowRoot;
```

Replace the body of `EnsureCanvas()` (`:137-158`) with:

```csharp
        void EnsureCanvas()
        {
            if (barCanvas != null && rowRoot != null)
                return;

            (barCanvas, rowRoot) = DeskItemBarBuilder.Build(transform);
        }
```

(With `rowRoot` now serialized, an authored `barCanvas` + `rowRoot` make the guard early-return and the runtime build is skipped.)

- [ ] **Step 5: Run offline compile-check — expect clean**

- [ ] **Step 6: Run EditMode tests — expect GREEN** (`DeskItemBarBuilderTests` passes; existing suite green).

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/UI/DeskItemBarBuilder.cs Assets/Scripts/UI/DeskItemBarBuilder.cs.meta \
        Assets/Scripts/UI/DeskItemBar.cs \
        Assets/Tests/EditMode/DeskItemBarBuilderTests.cs Assets/Tests/EditMode/DeskItemBarBuilderTests.cs.meta
git commit -m "refactor: extract DeskItemBarBuilder; serialize rowRoot"
```

---

### Task 5: Author Desk Item Bar

**Files:**
- Modify: `Assets/Scripts/Editor/RuntimeObjectAuthoring.cs`

**Interfaces:**
- Consumes: `DeskItemBarBuilder.Build` (Task 4); `AuthorPlayerInventory` (Task 3); `Meniscus.UI.DeskItemBar`.
- Produces: `static bool AuthorDeskItemBar()`.

- [ ] **Step 1: Add the command**

Add `using Meniscus.UI;` to `RuntimeObjectAuthoring.cs`. Add inside the class:

```csharp
        [MenuItem(MenuRoot + "Author Desk Item Bar")]
        static void AuthorDeskItemBarMenu()
        {
            if (!TryOpenScene(out var scene))
                return;

            if (AuthorDeskItemBar())
                SaveScene(scene);
        }

        public static bool AuthorDeskItemBar()
        {
            var manager = Object.FindAnyObjectByType<GameManager>();

            if (manager == null)
            {
                Debug.LogError("[RuntimeObjectAuthoring] No GameManager in the scene. Aborting.");
                return false;
            }

            var bar = Object.FindAnyObjectByType<DeskItemBar>();

            if (bar == null)
            {
                Undo.RegisterCompleteObjectUndo(manager.gameObject, "Author Desk Item Bar");
                bar = manager.gameObject.AddComponent<DeskItemBar>();
            }

            // Author the canvas + row container if not already wired.
            var barSerialized = new SerializedObject(bar);
            var canvasProp = barSerialized.FindProperty("barCanvas");
            var rowRootProp = barSerialized.FindProperty("rowRoot");

            if (canvasProp.objectReferenceValue == null || rowRootProp.objectReferenceValue == null)
            {
                var (canvas, rowRoot) = DeskItemBarBuilder.Build(bar.transform);
                Undo.RegisterCreatedObjectUndo(canvas.gameObject, "Author Desk Item Bar Canvas");
                canvasProp.objectReferenceValue = canvas;
                rowRootProp.objectReferenceValue = rowRoot;
            }

            barSerialized.FindProperty("gameManager").objectReferenceValue = manager;
            barSerialized.ApplyModifiedProperties();

            var managerSerialized = new SerializedObject(manager);
            managerSerialized.FindProperty("deskItemBar").objectReferenceValue = bar;
            managerSerialized.ApplyModifiedProperties();

            Debug.Log("[RuntimeObjectAuthoring] DeskItemBar authored (component + canvas + rowRoot) and wired.");
            return true;
        }
```

- [ ] **Step 2: Run offline compile-check — expect clean**

- [ ] **Step 3: Manual verification in Unity**

Run the command. Confirm: a `DeskItemBar` component on the Managers object; a `Desk Item Bar Canvas` child (disabled) with a `Desk Item Row` container; `barCanvas`/`rowRoot`/`gameManager` wired on the bar and `GameManager.deskItemBar` wired. Enter Play, reach the player's turn with items — the bar shows identically. Re-run: no duplicate canvas/component.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Editor/RuntimeObjectAuthoring.cs
git commit -m "feat: editor tool authors the desk item bar"
```

---

### Task 6: Book prop split + BookShopBuilder + authoredBook

**Files:**
- Create: `Assets/Scripts/UI/BookShopBuilder.cs`
- Modify: `Assets/Scripts/UI/BookShopView.cs` (add `authoredBook` field near `:44`; refactor `Build` `:248-294`; add `ResolveAuthoredProp`)
- Test: `Assets/Tests/EditMode/BookShopBuilderTests.cs`

**Interfaces:**
- Produces: `Meniscus.UI.BookShopBuilder.BuildProp(Transform parent, BookPropParams p) → Transform root`; `struct BookPropParams { float pageWidth, pageDepth, coverThickness; Color coverColor, pageColor; }`. The built root contains named children `"Book Hinge"` (+ `"Book Back Cover"`, `"Book Left Page"`, `"Book Right Page"`, `"Book Front Cover"`), a trigger `BoxCollider`, and a `BookClickTarget`.

- [ ] **Step 1: Write the failing test**

```csharp
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
```

- [ ] **Step 2: Run offline compile-check — expect RED** (`BookShopBuilder` missing).

- [ ] **Step 3: Create `BookShopBuilder` (move the prop construction, edit-mode-safe)**

```csharp
using UnityEngine;

namespace Meniscus.UI
{
    /// <summary>
    /// Builds the physical book prop (covers, hinged front cover, pages, click collider) for
    /// <see cref="BookShopView"/>. The data-driven menu canvas is NOT built here — it stays runtime
    /// (its buttons' onClick handlers are wired per-catalog at runtime and are not serializable).
    /// Shared by the runtime fallback and the editor authoring tool.
    /// </summary>
    public static class BookShopBuilder
    {
        public struct BookPropParams
        {
            public float pageWidth;
            public float pageDepth;
            public float coverThickness;
            public Color coverColor;
            public Color pageColor;
        }

        public static Transform BuildProp(Transform parent, BookPropParams p)
        {
            var root = new GameObject("Diegetic Book Shop").transform;
            root.SetParent(parent, false);

            CreateCoverCube(
                root, "Book Back Cover", p.coverColor,
                new Vector3(p.pageWidth * 2f + p.coverThickness * 2f, p.coverThickness, p.pageDepth + p.coverThickness * 2f),
                Vector3.zero);

            var pageSize = new Vector3(p.pageWidth * 0.94f, p.coverThickness * 0.5f, p.pageDepth * 0.92f);
            CreateCoverCube(root, "Book Left Page", p.pageColor, pageSize, new Vector3(-p.pageWidth * 0.5f, p.coverThickness * 0.75f, 0f));
            CreateCoverCube(root, "Book Right Page", p.pageColor, pageSize, new Vector3(p.pageWidth * 0.5f, p.coverThickness * 0.75f, 0f));

            var hingePivot = new GameObject("Book Hinge").transform;
            hingePivot.SetParent(root, false);
            hingePivot.localPosition = Vector3.zero;

            CreateCoverCube(
                hingePivot, "Book Front Cover", p.coverColor,
                new Vector3(p.pageWidth, p.coverThickness, p.pageDepth),
                new Vector3(p.pageWidth * 0.5f, p.coverThickness, 0f));

            var clickCollider = root.gameObject.AddComponent<BoxCollider>();
            clickCollider.center = new Vector3(0f, p.coverThickness, 0f);
            clickCollider.size = new Vector3(p.pageWidth * 2.1f, p.coverThickness * 3f, p.pageDepth * 1.05f);
            clickCollider.isTrigger = true;

            root.gameObject.AddComponent<BookClickTarget>();
            return root;
        }

        static Transform CreateCoverCube(Transform parent, string name, Color color, Vector3 size, Vector3 localPosition)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = localPosition;
            cube.transform.localScale = size;

            var collider = cube.GetComponent<Collider>();

            if (collider != null)
            {
                if (Application.isPlaying)
                    Object.Destroy(collider);
                else
                    Object.DestroyImmediate(collider);
            }

            var renderer = cube.GetComponent<Renderer>();

            if (renderer != null)
                renderer.sharedMaterial = CreateOpaqueMaterial($"{name} Material", color);

            return cube.transform;
        }

        static Material CreateOpaqueMaterial(string materialName, Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            return new Material(shader) { name = materialName, color = color };
        }
    }
}
```

- [ ] **Step 4: Add `authoredBook`, route `Build` through the builder, add `ResolveAuthoredProp`**

In `BookShopView.cs`, add after the `deskAnchor` field (`:44`):

```csharp
        [Tooltip("Pre-authored physical book prop (built by Tools ▸ Meniscus ▸ Author Book Shop). When " +
                 "set, the runtime uses it and only builds the menu canvas on top; left empty, the prop is " +
                 "built at runtime.")]
        [SerializeField] Transform authoredBook;
```

Replace the prop-building portion of `Build` (`:248-294`, the part before `BuildMenuCanvas`) so it uses the authored prop when present. The new `Build`:

```csharp
        void Build(IReadOnlyList<ItemDefinition> catalog)
        {
            if (authoredBook != null)
                ResolveAuthoredProp();
            else
                BuildProp();

            BuildMenuCanvas(catalog);

            built = true;
            root.gameObject.SetActive(false);
        }

        void BuildProp()
        {
            root = BookShopBuilder.BuildProp(transform, new BookShopBuilder.BookPropParams
            {
                pageWidth = pageWidth,
                pageDepth = pageDepth,
                coverThickness = coverThickness,
                coverColor = CoverColor,
                pageColor = PageColor,
            });

            hingePivot = root.Find("Book Hinge");

            var clickTarget = root.GetComponent<BookClickTarget>();
            clickTarget.Clicked = OnBookClicked;
            clickTarget.LogDiagnostics = logDiagnostics;
        }

        void ResolveAuthoredProp()
        {
            root = authoredBook;
            hingePivot = root.Find("Book Hinge");

            // onClick/Clicked delegates are not serialized, so re-wire the authored prop's click target.
            var clickTarget = root.GetComponent<BookClickTarget>();

            if (clickTarget == null)
                clickTarget = root.gameObject.AddComponent<BookClickTarget>();

            clickTarget.Clicked = OnBookClicked;
            clickTarget.LogDiagnostics = logDiagnostics;
        }
```

(The `BoxCollider` + `BookClickTarget` are created inside `BookShopBuilder.BuildProp`, so they are no longer added in `Build`. `BuildMenuCanvas` is unchanged — it still parents the world-space canvas under `root`.)

- [ ] **Step 5: Run offline compile-check — expect clean**

- [ ] **Step 6: Run EditMode tests — expect GREEN** (`BookShopBuilderTests` passes; existing suite green).

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/UI/BookShopBuilder.cs Assets/Scripts/UI/BookShopBuilder.cs.meta \
        Assets/Scripts/UI/BookShopView.cs \
        Assets/Tests/EditMode/BookShopBuilderTests.cs Assets/Tests/EditMode/BookShopBuilderTests.cs.meta
git commit -m "refactor: split book prop into BookShopBuilder; add authoredBook path"
```

---

### Task 7: Author Book Shop

**Files:**
- Modify: `Assets/Scripts/Editor/RuntimeObjectAuthoring.cs`

**Interfaces:**
- Consumes: `BookShopBuilder.BuildProp` (Task 6); `Meniscus.UI.BookShopView`, `Meniscus.UI.ShopManager`.
- Produces: `static bool AuthorBookShop()`.

- [ ] **Step 1: Add the command**

Add inside `RuntimeObjectAuthoring`. The book prop params must match `BookShopView`'s defaults (`pageWidth 0.30`, `pageDepth 0.38`, `coverThickness 0.02`, cover `(0.34,0.16,0.08)`, page `(0.86,0.78,0.6)`):

```csharp
        [MenuItem(MenuRoot + "Author Book Shop")]
        static void AuthorBookShopMenu()
        {
            if (!TryOpenScene(out var scene))
                return;

            if (AuthorBookShop())
                SaveScene(scene);
        }

        public static bool AuthorBookShop()
        {
            var shopManager = Object.FindAnyObjectByType<ShopManager>();

            if (shopManager == null)
            {
                Debug.LogError("[RuntimeObjectAuthoring] No ShopManager in the scene. Aborting.");
                return false;
            }

            var view = Object.FindAnyObjectByType<BookShopView>();

            if (view == null)
            {
                var go = new GameObject("Book Shop");
                Undo.RegisterCreatedObjectUndo(go, "Author Book Shop");
                view = go.AddComponent<BookShopView>();
            }

            var viewSerialized = new SerializedObject(view);
            var authoredProp = viewSerialized.FindProperty("authoredBook");

            var prop = authoredProp.objectReferenceValue as Transform;

            if (prop == null)
                prop = view.transform.Find("Diegetic Book Shop");

            if (prop == null)
            {
                prop = BookShopBuilder.BuildProp(view.transform, new BookShopBuilder.BookPropParams
                {
                    pageWidth = 0.30f,
                    pageDepth = 0.38f,
                    coverThickness = 0.02f,
                    coverColor = new Color(0.34f, 0.16f, 0.08f),
                    pageColor = new Color(0.86f, 0.78f, 0.6f),
                });
                Undo.RegisterCreatedObjectUndo(prop.gameObject, "Author Book Prop");
            }

            authoredProp.objectReferenceValue = prop;
            viewSerialized.ApplyModifiedProperties();

            var shopSerialized = new SerializedObject(shopManager);
            shopSerialized.FindProperty("bookShop").objectReferenceValue = view;
            shopSerialized.ApplyModifiedProperties();

            Debug.Log("[RuntimeObjectAuthoring] Book Shop authored (prop) and wired (BookShopView.authoredBook + ShopManager.bookShop). " +
                      "Position the 'Book Shop' object / set its deskAnchor in the Inspector.");
            return true;
        }
```

- [ ] **Step 2: Run offline compile-check — expect clean**

- [ ] **Step 3: Manual verification in Unity**

Run the command. Confirm: a `Book Shop` GameObject with `BookShopView`; a `Diegetic Book Shop` child holding `Book Back Cover` / `Book Left Page` / `Book Right Page` / `Book Hinge` (→ `Book Front Cover`), a trigger BoxCollider, and a BookClickTarget; `BookShopView.authoredBook` → the prop; `ShopManager.bookShop` → the view. Position the `Book Shop` object on the desk (or set its `deskAnchor`). Enter Play, win a round — the book lifts and the menu renders identically. Re-run: no duplicate prop.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Editor/RuntimeObjectAuthoring.cs
git commit -m "feat: editor tool authors the book shop prop"
```

---

### Task 8: Spill prefab-ify (tuning + material → serialized; Instantiate path)

**Files:**
- Modify: `Assets/Scripts/Gameplay/GlassSpillEffect.cs` (consts `:14-16` → fields; material `:39`; add `Begin`); `Assets/Scripts/Gameplay/GlassVisualController.cs` (add `spillPrefab` field; `SpawnSpillEffect` `:403-420`)
- Test: `Assets/Tests/EditMode/GlassSpillEffectTests.cs`

**Interfaces:**
- Produces: instance `GlassSpillEffect.Begin(Vector3 glassWorldPosition, float rimRadius, float rimWorldY, float tableWorldY, Color color, int rivuletCount, float runDownDuration, float puddleLifetime)`; serialized `[SerializeField] GlassSpillEffect spillPrefab` on `GlassVisualController`.

- [ ] **Step 1: Write the failing test**

```csharp
using Meniscus.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace Meniscus.Tests.EditMode
{
    public class GlassSpillEffectTests
    {
        [Test]
        public void RunDownProgress_IsClampedZeroToOne_AndMonotonic()
        {
            Assert.AreEqual(0f, GlassSpillEffect.RunDownProgress(0f, 1f), 1e-4f);
            Assert.AreEqual(1f, GlassSpillEffect.RunDownProgress(1f, 1f), 1e-4f);
            Assert.AreEqual(1f, GlassSpillEffect.RunDownProgress(5f, 1f), 1e-4f, "Past duration clamps to 1.");
            Assert.That(GlassSpillEffect.RunDownProgress(0.25f, 1f), Is.LessThan(GlassSpillEffect.RunDownProgress(0.75f, 1f)));
        }

        [Test]
        public void Begin_DoesNotThrow_AndKeepsHostAlive()
        {
            // Component must accept Begin() without a coroutine host blowing up at construction time.
            var go = new GameObject("Spill");
            var effect = go.AddComponent<GlassSpillEffect>();

            Assert.DoesNotThrow(() => effect.Begin(Vector3.zero, 0.4f, 1f, 0f, Color.white, 4, 0.4f, 1.4f));

            Object.DestroyImmediate(go);
        }
    }
}
```

- [ ] **Step 2: Run offline compile-check — expect RED** (`Begin` missing).

- [ ] **Step 3: Convert consts to serialized fields and add `Begin`**

In `GlassSpillEffect.cs`, replace the three consts (`:14-16`) with serialized fields, and add a serialized material override:

```csharp
        [Header("Rivulet Tuning")]
        [SerializeField] float rivuletWidth = 0.05f;
        [SerializeField] float rivuletDepth = 0.015f;
        [SerializeField] float rivuletRadiusOffset = 0.015f;
        [Tooltip("Authored material for the spill. Left empty, a transparent one is built from the spill color.")]
        [SerializeField] Material overspillMaterial;
```

Update the three `Run` references accordingly: `RivuletWidth` → `rivuletWidth`, `RivuletDepth` → `rivuletDepth`, `RivuletRadiusOffset` → `rivuletRadiusOffset`.

Extract the field-set + coroutine-start out of the static `Spawn` into an instance `Begin`, and have `Spawn` (the fallback) call it. Replace `Spawn` (`:22-50`) with:

```csharp
        public static GlassSpillEffect Spawn(
            Vector3 glassWorldPosition,
            float rimRadius,
            float rimWorldY,
            float tableWorldY,
            Color color,
            int rivuletCount,
            float runDownDuration,
            float puddleLifetime)
        {
            // Fallback path: no authored prefab. World-rooted at identity scale (the glass is non-uniformly
            // scaled), self-destructing when finished.
            var host = new GameObject("Runtime Overspill Effect");
            var effect = host.AddComponent<GlassSpillEffect>();
            effect.Begin(glassWorldPosition, rimRadius, rimWorldY, tableWorldY, color, rivuletCount, runDownDuration, puddleLifetime);
            return effect;
        }

        public void Begin(
            Vector3 glassWorldPosition,
            float rimRadius,
            float rimWorldY,
            float tableWorldY,
            Color color,
            int rivuletCount,
            float runDownDuration,
            float puddleLifetime)
        {
            baseColor = color;
            material = overspillMaterial != null
                ? overspillMaterial
                : GlassVisualController.CreateTransparentLiquidMaterial("Runtime Overspill Material", color);

            StartCoroutine(Run(
                glassWorldPosition,
                rimRadius,
                rimWorldY,
                tableWorldY,
                Mathf.Max(1, rivuletCount),
                Mathf.Max(0.05f, runDownDuration),
                Mathf.Max(0.1f, puddleLifetime)));
        }
```

(Leave `OnDestroy` as-is — it only destroys `material` when it built one; an authored `overspillMaterial` asset assigned in the inspector is shared and Unity does not destroy referenced assets on instance teardown. To be safe, guard `OnDestroy` so it only destroys a runtime-built material: track a `bool ownsMaterial` set true only in the `overspillMaterial == null` branch, and `if (ownsMaterial && material != null) Destroy(material);`.)

Apply that guard:

```csharp
        bool ownsMaterial;
```

In `Begin`, set `ownsMaterial = overspillMaterial == null;` right after assigning `material`. In `OnDestroy`:

```csharp
        void OnDestroy()
        {
            if (ownsMaterial && material != null)
                Destroy(material);
        }
```

- [ ] **Step 4: Add `spillPrefab` to GlassVisualController and Instantiate it**

In `GlassVisualController.cs`, add under the `[Header("Overspill")]` block (after `:64`):

```csharp
        [Tooltip("Authored spill prefab (built by Tools ▸ Meniscus ▸ Author Spill Effect Prefab). When set " +
                 "it is instantiated per spill; left empty, a runtime overspill object is built instead.")]
        [SerializeField] GlassSpillEffect spillPrefab;
```

Replace the `GlassSpillEffect.Spawn(...)` call in `SpawnSpillEffect` (`:411-419`) with a prefab-or-fallback path:

```csharp
            if (spillPrefab != null)
            {
                var effect = Instantiate(spillPrefab);
                effect.Begin(
                    transform.position,
                    worldRimRadius,
                    rimWorldY,
                    transform.position.y - spillTableDrop,
                    liquidColor,
                    spillRivuletCount,
                    spillRunDownDuration,
                    spillPuddleLifetime);
            }
            else
            {
                GlassSpillEffect.Spawn(
                    transform.position,
                    worldRimRadius,
                    rimWorldY,
                    transform.position.y - spillTableDrop,
                    liquidColor,
                    spillRivuletCount,
                    spillRunDownDuration,
                    spillPuddleLifetime);
            }
```

- [ ] **Step 5: Run offline compile-check — expect clean**

- [ ] **Step 6: Run EditMode tests — expect GREEN** (`GlassSpillEffectTests` passes; existing suite green).

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Gameplay/GlassSpillEffect.cs Assets/Scripts/Gameplay/GlassVisualController.cs \
        Assets/Tests/EditMode/GlassSpillEffectTests.cs Assets/Tests/EditMode/GlassSpillEffectTests.cs.meta
git commit -m "refactor: spill tuning/material to serialized fields; prefab Instantiate path"
```

---

### Task 9: Author Spill Effect Prefab (+ Assets/Prefabs/)

**Files:**
- Create: `Assets/Prefabs/` (folder), `Assets/Prefabs/SpillEffect.prefab` (created by the tool)
- Modify: `Assets/Scripts/Editor/RuntimeObjectAuthoring.cs`

**Interfaces:**
- Consumes: `GlassSpillEffect`, `GlassVisualController` `spillPrefab` (Task 8).
- Produces: `static bool AuthorSpillPrefab()`.

- [ ] **Step 1: Add the command (creates the folder + prefab, wires the field)**

Add inside `RuntimeObjectAuthoring`. Add `const string PrefabsDir = "Assets/Prefabs";` and `const string SpillPrefabPath = "Assets/Prefabs/SpillEffect.prefab";` near the other path constants:

```csharp
        [MenuItem(MenuRoot + "Author Spill Effect Prefab")]
        static void AuthorSpillPrefabMenu()
        {
            if (!TryOpenScene(out var scene))
                return;

            if (AuthorSpillPrefab())
                SaveScene(scene);
        }

        public static bool AuthorSpillPrefab()
        {
            var glass = Object.FindAnyObjectByType<GlassVisualController>();

            if (glass == null)
            {
                Debug.LogError("[RuntimeObjectAuthoring] No GlassVisualController in the scene. Aborting.");
                return false;
            }

            if (!AssetDatabase.IsValidFolder(PrefabsDir))
                AssetDatabase.CreateFolder("Assets", "Prefabs");

            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(SpillPrefabPath);

            if (existing == null)
            {
                // Build a transient host carrying the component + authored material, save it as a prefab, discard the host.
                var temp = new GameObject("SpillEffect");
                temp.AddComponent<GlassSpillEffect>();

                var material = AssetDatabase.LoadAssetAtPath<Material>(WaterMaterialPath);

                if (material != null)
                {
                    var tempSerialized = new SerializedObject(temp.GetComponent<GlassSpillEffect>());
                    tempSerialized.FindProperty("overspillMaterial").objectReferenceValue = material;
                    tempSerialized.ApplyModifiedProperties();
                }

                existing = PrefabUtility.SaveAsPrefabAsset(temp, SpillPrefabPath);
                Object.DestroyImmediate(temp);
            }

            var glassSerialized = new SerializedObject(glass);
            glassSerialized.FindProperty("spillPrefab").objectReferenceValue = existing.GetComponent<GlassSpillEffect>();
            glassSerialized.ApplyModifiedProperties();

            Debug.Log($"[RuntimeObjectAuthoring] Spill prefab authored at '{SpillPrefabPath}' and wired to GlassVisualController.spillPrefab.");
            return true;
        }
```

(`Water_Surface.mat` is reused as the spill's authored material; swap to a dedicated spill material later if desired.)

- [ ] **Step 2: Run offline compile-check — expect clean**

- [ ] **Step 3: Manual verification in Unity**

Run the command. Confirm: `Assets/Prefabs/SpillEffect.prefab` exists with a `GlassSpillEffect` (overspillMaterial assigned); `GlassVisualController.spillPrefab` points at it. Enter Play and overflow the glass — the run-down spill plays identically. Re-run: the prefab is reused, not duplicated.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Editor/RuntimeObjectAuthoring.cs \
        Assets/Prefabs Assets/Prefabs.meta \
        Assets/Prefabs/SpillEffect.prefab Assets/Prefabs/SpillEffect.prefab.meta
git commit -m "feat: editor tool authors the spill effect prefab"
```

---

### Task 10: Author All + headless Run()

**Files:**
- Modify: `Assets/Scripts/Editor/RuntimeObjectAuthoring.cs`

**Interfaces:**
- Consumes: all `Author*()` methods (Tasks 2–9).
- Produces: menu `Author All`; `public static void Run()` (headless `-executeMethod Meniscus.Editor.RuntimeObjectAuthoring.Run`).

- [ ] **Step 1: Add the orchestrator + headless entry**

```csharp
        [MenuItem(MenuRoot + "Author All")]
        static void AuthorAllMenu() => Run();

        /// <summary>
        /// Headless: Unity -batchmode -executeMethod Meniscus.Editor.RuntimeObjectAuthoring.Run -quit
        /// Authors all targets in dependency order (PlayerInventory before the DeskItemBar that references it).
        /// </summary>
        public static void Run()
        {
            if (!TryOpenScene(out var scene))
                return;

            var changed = false;
            changed |= AuthorWaterSurface();
            changed |= AuthorPlayerInventory();
            changed |= AuthorDeskItemBar();
            changed |= AuthorBookShop();
            changed |= AuthorSpillPrefab();

            if (changed)
                SaveScene(scene);

            Debug.Log("[RuntimeObjectAuthoring] Author All complete.");
        }
```

- [ ] **Step 2: Run offline compile-check — expect clean**

- [ ] **Step 3: Manual verification in Unity**

On a scene where none of the targets are authored yet (or after Undo-ing prior runs), run `Author All`. Confirm all five are authored and wired in one pass and the scene saves. Run `Author All` a second time — confirm it is idempotent (no duplicates, scene unchanged or harmlessly re-wired). Enter Play and play a full round (pour → win → shop book → use a desk item → overflow) — everything looks and behaves identically to before any authoring.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Editor/RuntimeObjectAuthoring.cs
git commit -m "feat: Author All command + headless Run() entry"
```

---

## Optional follow-up (deferred — flagged in handoff)

The spec listed `DeskItemRow.prefab` / `BookOrderRow.prefab` (Decision 4: list rows → prefabs). On inspection these serve code-tidiness, not the "reposition in the editor" goal — the rows are generated dynamically and positioned by layout code, and their `onClick` must be wired at runtime regardless. They are **deferred**: add them only if you later want the row *visual* authored. Each would be: author a styled button prefab (the `RuntimeUiFactory.CreateButton` output), add a `rowPrefab`/`orderRowPrefab` serialized field, and change the rebuild loop to `Instantiate` + set label/price + `onClick.AddListener` (fallback to the current `CreateButton` when the field is null).

## Notes for the executor

- **Branch first.** The working tree has unrelated coin-feature WIP. Create a branch (e.g. `git switch -c feature/runtime-object-authoring`) before Task 1, and keep stages file-scoped (Global Constants) so the WIP is never swept into a commit.
- **`[ExecuteAlways]` on GlassVisualController** means the water surface is also created in edit mode. After Task 1, opening the scene may produce a `"Runtime Water Surface"` until you run `Author Glass Water Surface`, which replaces it with the wired `"Liquid"`. Delete any stray `"Runtime Water Surface"` after authoring.
- **Book named-children contract:** the runtime resolves `"Book Hinge"` by name (`ResolveAuthoredProp`). If you rename prop children in the editor, keep `"Book Hinge"` intact, or the open/close animation loses its pivot. This is the fragility the design's escape hatch anticipated — if it bites, the fallback is to leave `authoredBook` empty (prop builds at runtime) and only keep the positioned `BookShopView` + `ShopManager.bookShop` wiring from Task 7.
- **Spill is a modest win by design:** the prefab carries the component + material + tuning; the run-down primitives stay procedurally animated (they can't be a static authored object).
```
