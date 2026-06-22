# Desk Item Boxes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the on-screen `DeskItemBar` button UI with physical placeholder boxes on the desk that the player clicks to select, then confirms (Use/Cancel tile) to use — one box per inventory stack.

**Architecture:** Presentation-only change. A new `DeskItemTray` (replacing `DeskItemBar`) subscribes to the same `PlayerInventory.Changed` / `GameManager.StateChanged` events and spawns one `DeskItemBox` cube per stack. Box/tile clicks relay (via `OnMouseDown`, like the book's `BookClickTarget`) to the tray, which owns selection and calls the **unchanged** `GameManager.TryUseItem`. Boxes are always visible; Use is gated to `PlayerTurn` (already enforced inside `TryUseItem`).

**Tech Stack:** Unity 6000.3.6f1, C# (Meniscus.UI / Meniscus.Core / Meniscus.Items asmdefs), NUnit EditMode tests, procedural primitives (`GameObject.CreatePrimitive`) + `TextMesh` labels.

## Global Constraints

- Editor version: **6000.3.6f1** (`ProjectSettings/ProjectVersion.txt`).
- New runtime code lives in namespace **`Meniscus.UI`** (`Assets/Scripts/UI/`); `GameManager` already has `using Meniscus.UI;`.
- The purchase flow, `ItemDefinition`, `ItemEffectApplier`, `ItemEffectKind`, `PlayerInventory`, and `GameManager.TryUseItem` are **not** modified beyond swapping the `deskItemBar` field for `deskItemTray`.
- `GameConstants.DeskCapacity == 8` — layout must fit up to 8 boxes without overlap.
- `ItemDefinition.Create(string id, string displayName, string description, int cost, ItemEffectKind effect, float magnitude)` — exact factory signature for tests.
- **Verification model (project convention):** the fast inner loop is the **offline compile-check** (Unity's bundled Roslyn over the Bee `.rsp` files) — a clean exit with no `error`/`warning CS` lines is the accepted "before done" bar. Genuine red/green for EditMode tests is confirmed by running the Unity EditMode test batch once per task (heavier; launches the editor headless). Both commands are spelled out in each task.

### Compile-check command (run from project root)

```bash
find Library -name "Meniscus.*.rsp"   # find the current <dag> folder id (changes between builds)
CSC="/Applications/Unity/Hub/Editor/6000.3.6f1/Unity.app/Contents/Resources/Scripting/DotNetSdkRoslyn/csc.dll"
dotnet "$CSC" "@Library/Bee/artifacts/<dag>/Meniscus.Runtime.rsp"
dotnet "$CSC" "@Library/Bee/artifacts/<dag>/Meniscus.Tests.EditMode.rsp"
```

### EditMode test-run command (run from project root)

```bash
"/Applications/Unity/Hub/Editor/6000.3.6f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -projectPath . -runTests -testPlatform EditMode \
  -testResults Logs/test-results.xml -quit
# Inspect Logs/test-results.xml; a passing run reports result="Passed" with 0 failures.
```

---

## File Structure

- `Assets/Scripts/UI/DeskItemBox.cs` — **new.** Per-stack box component: holds `ItemDefinition`/count, drives raised/idle visual + Use/Cancel tile visibility, relays clicks to the tray.
- `Assets/Scripts/UI/DeskItemTileRelay.cs` — **new.** Tiny click relay on the body/Use/Cancel colliders → forwards `OnMouseDown` to its `DeskItemBox`.
- `Assets/Scripts/UI/DeskItemTrayBuilder.cs` — **new.** Procedural geometry: builds one box (cube body + label + hidden Use/Cancel tiles) and returns its `DeskItemBox`. Mirrors `DeskItemBarBuilder`.
- `Assets/Scripts/UI/DeskItemTray.cs` — **new.** Replaces `DeskItemBar`. Subscribes to inventory/state, reconciles boxes to `Contents()`, lays them out along the desk's near edge, owns single-selection, routes Use → `TryUseItem`.
- `Assets/Scripts/UI/DeskItemBar.cs` — **delete.**
- `Assets/Scripts/UI/DeskItemBarBuilder.cs` — **delete.**
- `Assets/Scripts/Core/GameManager.cs` — **modify.** Swap serialized `deskItemBar` field + bootstrap for `deskItemTray`.
- `Assets/Scripts/Editor/RuntimeObjectAuthoring.cs` — **modify.** Replace `AuthorDeskItemBar` with `AuthorDeskItemTray`; update `Run()`.
- `Assets/Tests/EditMode/DeskItemTrayBuilderTests.cs` — **new** (replaces `DeskItemBarBuilderTests.cs`, which is deleted).
- `Assets/Tests/EditMode/DeskItemTrayTests.cs` — **new.** Reconciliation, single-selection, off-turn no-op.
- `Assets/Tests/EditMode/GameFlowTests.cs` — **modify.** Bootstrap test now asserts a `DeskItemTray`.

---

### Task 1: DeskItemBox, DeskItemTileRelay, and DeskItemTrayBuilder

**Files:**
- Create: `Assets/Scripts/UI/DeskItemBox.cs`
- Create: `Assets/Scripts/UI/DeskItemTileRelay.cs`
- Create: `Assets/Scripts/UI/DeskItemTrayBuilder.cs`
- Test: `Assets/Tests/EditMode/DeskItemTrayBuilderTests.cs`

**Interfaces:**
- Consumes: `Meniscus.Items.ItemDefinition` (`.Id`, `.DisplayName`); `DeskItemTray` (referenced only as a type — its methods `OnBoxClicked(DeskItemBox)`, `OnUseClicked(DeskItemBox)`, `OnCancelClicked(DeskItemBox)` are defined in Task 2; Task 1 compiles because it only stores a `DeskItemTray` reference and calls those methods, which must exist by the time both tasks are merged — so **Task 2 lands before any compile-check that includes the tray**; within Task 1, `DeskItemTray` does not yet exist, so Task 1's box stores its owner as the not-yet-defined type — see Step ordering note below).
- Produces:
  - `DeskItemBox` with `ItemDefinition Item {get;}`, `int Count {get;}`, `bool IsSelected {get;}`, `void Initialize(DeskItemTray, ItemDefinition, TextMesh, GameObject use, GameObject cancel, Renderer body)`, `void SetCount(int)`, `void SetRestPosition(Vector3 localPos)`, `void SetSelected(bool)`, `void NotifySelectClicked()`, `void NotifyUseClicked()`, `void NotifyCancelClicked()`, `void FlashUnavailable()`.
  - `DeskItemTileRelay` with `enum Kind { Body, Use, Cancel }` and `void Initialize(DeskItemBox, Kind)`.
  - `DeskItemTrayBuilder.BuildBox(Transform parent, DeskItemTray tray, ItemDefinition item) -> DeskItemBox` and `const float BoxSize`.

> **Step ordering note:** `DeskItemBox` and `DeskItemTrayBuilder` reference the type `DeskItemTray`, which is created in Task 2. To keep the runtime assembly compiling, implement Task 1 and Task 2 back-to-back and run the first compile-check only **after Task 2's `DeskItemTray.cs` exists**. The builder test in this task is written now but is first executed in Task 2's verification (it needs `DeskItemTray` to compile). This is the one place tasks are coupled; everything else is independent.

- [ ] **Step 1: Write `DeskItemTileRelay.cs`**

```csharp
using UnityEngine;

namespace Meniscus.UI
{
    /// <summary>
    /// Sits on a box's body / Use / Cancel collider and relays its click to the owning
    /// <see cref="DeskItemBox"/>. Keeps the box component free of per-collider click wiring and
    /// mirrors the book's <c>BookClickTarget</c> relay approach.
    /// </summary>
    [DisallowMultipleComponent]
    public class DeskItemTileRelay : MonoBehaviour
    {
        public enum Kind { Body, Use, Cancel }

        DeskItemBox box;
        Kind kind;

        public void Initialize(DeskItemBox owner, Kind tileKind)
        {
            box = owner;
            kind = tileKind;
        }

        void OnMouseDown()
        {
            if (box == null)
                return;

            switch (kind)
            {
                case Kind.Body: box.NotifySelectClicked(); break;
                case Kind.Use: box.NotifyUseClicked(); break;
                case Kind.Cancel: box.NotifyCancelClicked(); break;
            }
        }
    }
}
```

- [ ] **Step 2: Write `DeskItemBox.cs`**

```csharp
using System.Collections;
using Meniscus.Items;
using UnityEngine;

namespace Meniscus.UI
{
    /// <summary>
    /// A placeholder cube on the desk standing in for one held item stack. Visuals only: it shows the
    /// item name + count, raises when selected and reveals Use/Cancel tiles, and relays all clicks to
    /// the owning <see cref="DeskItemTray"/>, which makes every decision (select, use, turn-gating).
    /// </summary>
    [DisallowMultipleComponent]
    public class DeskItemBox : MonoBehaviour
    {
        const float RaiseHeight = 0.02f;
        const float FlashSeconds = 0.3f;

        DeskItemTray tray;
        TextMesh label;
        GameObject useTile;
        GameObject cancelTile;
        Renderer bodyRenderer;
        Vector3 restLocalPos;
        Color restColor;
        Coroutine flash;

        public ItemDefinition Item { get; private set; }
        public int Count { get; private set; }
        public bool IsSelected { get; private set; }

        public void Initialize(DeskItemTray owner, ItemDefinition item, TextMesh labelText,
            GameObject use, GameObject cancel, Renderer body)
        {
            tray = owner;
            Item = item;
            label = labelText;
            useTile = use;
            cancelTile = cancel;
            bodyRenderer = body;

            if (bodyRenderer != null)
                restColor = bodyRenderer.material.color;

            SetSelected(false);
        }

        public void SetCount(int count)
        {
            Count = count;

            if (label != null && Item != null)
                label.text = $"{Item.DisplayName.ToUpperInvariant()}\nx{count}";
        }

        /// <summary>Sets the box's resting local position (its row slot); preserves the raised offset.</summary>
        public void SetRestPosition(Vector3 localPos)
        {
            restLocalPos = localPos;
            transform.localPosition = IsSelected ? localPos + Vector3.up * RaiseHeight : localPos;
        }

        public void SetSelected(bool selected)
        {
            IsSelected = selected;

            if (useTile != null) useTile.SetActive(selected);
            if (cancelTile != null) cancelTile.SetActive(selected);

            transform.localPosition = selected ? restLocalPos + Vector3.up * RaiseHeight : restLocalPos;
        }

        /// <summary>Brief red blink when Use is pressed off-turn (placeholder "not your turn" cue).</summary>
        public void FlashUnavailable()
        {
            if (bodyRenderer == null || !gameObject.activeInHierarchy)
                return;

            if (flash != null)
                StopCoroutine(flash);

            flash = StartCoroutine(FlashRoutine());
        }

        IEnumerator FlashRoutine()
        {
            bodyRenderer.material.color = new Color(0.7f, 0.2f, 0.2f);
            var elapsed = 0f;

            while (elapsed < FlashSeconds)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            bodyRenderer.material.color = restColor;
            flash = null;
        }

        public void NotifySelectClicked() => tray?.OnBoxClicked(this);
        public void NotifyUseClicked() => tray?.OnUseClicked(this);
        public void NotifyCancelClicked() => tray?.OnCancelClicked(this);
    }
}
```

- [ ] **Step 3: Write `DeskItemTrayBuilder.cs`**

```csharp
using Meniscus.Items;
using UnityEngine;

namespace Meniscus.UI
{
    /// <summary>
    /// Builds one desk item box: a placeholder cube body (with the collider that receives clicks), a
    /// camera-facing name/count label, and two hidden Use/Cancel tiles. Pure geometry — shared by the
    /// runtime tray and (indirectly) the authoring tool. Mirrors <c>DeskItemBarBuilder</c>'s split of
    /// geometry from logic.
    /// </summary>
    public static class DeskItemTrayBuilder
    {
        public const float BoxSize = 0.05f;
        const float TileSize = 0.025f;

        public static DeskItemBox BuildBox(Transform parent, DeskItemTray tray, ItemDefinition item)
        {
            // Container (unscaled) carries the DeskItemBox; children carry geometry/colliders.
            var container = new GameObject($"Desk Item Box ({item.Id})");
            container.transform.SetParent(parent, false);

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(container.transform, false);
            body.transform.localScale = Vector3.one * BoxSize;
            var bodyRenderer = body.GetComponent<Renderer>();
            bodyRenderer.material.color = new Color(0.55f, 0.4f, 0.25f);
            body.AddComponent<DeskItemTileRelay>(); // wired below

            var label = CreateLabel(container.transform, new Vector3(0f, BoxSize * 0.9f, 0f));
            var use = CreateTile(container.transform, "USE",
                new Vector3(-BoxSize, BoxSize * 0.6f, 0f), new Color(0.2f, 0.55f, 0.25f));
            var cancel = CreateTile(container.transform, "CANCEL",
                new Vector3(BoxSize, BoxSize * 0.6f, 0f), new Color(0.6f, 0.25f, 0.2f));

            var box = container.AddComponent<DeskItemBox>();

            body.GetComponent<DeskItemTileRelay>().Initialize(box, DeskItemTileRelay.Kind.Body);
            use.GetComponent<DeskItemTileRelay>().Initialize(box, DeskItemTileRelay.Kind.Use);
            cancel.GetComponent<DeskItemTileRelay>().Initialize(box, DeskItemTileRelay.Kind.Cancel);

            box.Initialize(tray, item, label, use, cancel, bodyRenderer);
            return box;
        }

        static TextMesh CreateLabel(Transform parent, Vector3 localPos)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one * 0.01f;

            var text = go.AddComponent<TextMesh>();
            text.anchor = TextAnchor.LowerCenter;
            text.alignment = TextAlignment.Center;
            text.fontSize = 48;
            text.characterSize = 0.1f;
            text.color = Color.white;
            return text;
        }

        static GameObject CreateTile(Transform parent, string text, Vector3 localPos, Color color)
        {
            var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tile.name = text;
            tile.transform.SetParent(parent, false);
            tile.transform.localPosition = localPos;
            tile.transform.localScale = new Vector3(TileSize * 1.6f, TileSize, TileSize * 0.4f);
            tile.GetComponent<Renderer>().material.color = color;
            tile.AddComponent<DeskItemTileRelay>();

            var labelGo = new GameObject("Caption");
            labelGo.transform.SetParent(tile.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 0f, -0.6f);
            labelGo.transform.localScale = Vector3.one * 0.6f;
            var caption = labelGo.AddComponent<TextMesh>();
            caption.text = text;
            caption.anchor = TextAnchor.MiddleCenter;
            caption.alignment = TextAlignment.Center;
            caption.fontSize = 48;
            caption.characterSize = 0.02f;
            caption.color = Color.white;

            tile.SetActive(false); // hidden until the box is selected
            return tile;
        }
    }
}
```

- [ ] **Step 4: Write the builder test `Assets/Tests/EditMode/DeskItemTrayBuilderTests.cs`**

```csharp
using Meniscus.Items;
using Meniscus.UI;
using NUnit.Framework;
using UnityEngine;

namespace Meniscus.Tests.EditMode
{
    public class DeskItemTrayBuilderTests
    {
        GameObject host;

        [SetUp] public void SetUp() => host = new GameObject("DeskItemTrayHost");
        [TearDown] public void TearDown() { if (host != null) Object.DestroyImmediate(host); }

        [Test]
        public void BuildBox_CreatesBodyColliderLabelAndHiddenTiles()
        {
            var tray = host.AddComponent<DeskItemTray>();
            var item = ItemDefinition.Create("test", "Test Item", "", 10, ItemEffectKind.PayoutMultiplier, 2f);

            var box = DeskItemTrayBuilder.BuildBox(host.transform, tray, item);

            Assert.IsNotNull(box, "BuildBox must return a DeskItemBox.");
            Assert.AreSame(item, box.Item);
            Assert.IsNotNull(box.GetComponentInChildren<Collider>(), "Box body must have a collider for clicks.");
            Assert.IsFalse(box.IsSelected, "A freshly built box starts unselected (tiles hidden).");
        }

        [Test]
        public void SetCount_WritesNameAndQuantityIntoLabel()
        {
            var tray = host.AddComponent<DeskItemTray>();
            var item = ItemDefinition.Create("test", "Test Item", "", 10, ItemEffectKind.PayoutMultiplier, 2f);
            var box = DeskItemTrayBuilder.BuildBox(host.transform, tray, item);

            box.SetCount(3);

            var label = box.GetComponentInChildren<TextMesh>();
            StringAssert.Contains("TEST ITEM", label.text);
            StringAssert.Contains("x3", label.text);
        }
    }
}
```

- [ ] **Step 5: Verify (deferred to Task 2)**

This task's files reference `DeskItemTray`, created in Task 2. Do **not** compile-check yet — proceed straight into Task 2, then run Task 2's compile-check, which covers all Task 1 + Task 2 files together.

- [ ] **Step 6: Commit (after Task 2 compiles clean)**

Commit Task 1 + Task 2 together (they are mutually dependent). See Task 2, Step 8.

---

### Task 2: DeskItemTray (reconciliation, selection, use routing)

**Files:**
- Create: `Assets/Scripts/UI/DeskItemTray.cs`
- Test: `Assets/Tests/EditMode/DeskItemTrayTests.cs`

**Interfaces:**
- Consumes: `PlayerInventory` (`event Action Changed`, `IReadOnlyList<ItemStack> Contents()`, `int TotalCount`); `GameManager` (`event Action<GameState> StateChanged`, `GameState CurrentState`, `bool TryUseItem(ItemDefinition)`); `DeskItemTrayBuilder.BuildBox`; `DeskItemBox` (Task 1).
- Produces:
  - `DeskItemTray` with `void Configure(GameManager, PlayerInventory)`, `void Rebuild()`, `void OnBoxClicked(DeskItemBox)`, `void OnUseClicked(DeskItemBox)`, `void OnCancelClicked(DeskItemBox)`, `IReadOnlyList<DeskItemBox> Boxes {get;}`, `DeskItemBox Selected {get;}`. Serialized fields named `gameManager`, `inventory`, `deskItemsAnchor` (consumed by Task 3's authoring tool).

- [ ] **Step 1: Write `DeskItemTray.cs`**

```csharp
using System.Collections.Generic;
using Meniscus.Core;
using Meniscus.Items;
using UnityEngine;

namespace Meniscus.UI
{
    /// <summary>
    /// The diegetic replacement for the on-screen item bar: a row of placeholder boxes on the desk,
    /// one per held stack. Subscribes to the same inventory/state events the bar used. Boxes are always
    /// visible; clicking one selects it (raising it + showing Use/Cancel), and Use routes through the
    /// unchanged <see cref="GameManager.TryUseItem"/> (which itself only succeeds on the player's turn).
    /// </summary>
    [DisallowMultipleComponent]
    public class DeskItemTray : MonoBehaviour
    {
        [SerializeField] GameManager gameManager;
        [SerializeField] PlayerInventory inventory;
        [SerializeField] Transform deskItemsAnchor;
        [SerializeField] string deskObjectName = "Saloon Table";

        const float BoxSpacing = 0.07f;
        const float DeskClearance = 0.01f;

        readonly List<DeskItemBox> boxes = new();
        DeskItemBox selected;

        bool subscribedManager;
        bool subscribedInventory;

        Bounds deskBounds;
        bool deskResolved;
        bool hasDeskBounds;
        bool positioned;

        public IReadOnlyList<DeskItemBox> Boxes => boxes;
        public DeskItemBox Selected => selected;

        void Awake() => ResolveReferences();

        void OnEnable()
        {
            Subscribe();
            Rebuild();
        }

        void OnDisable() => Unsubscribe();

        public void Configure(GameManager manager, PlayerInventory playerInventory)
        {
            Unsubscribe();
            gameManager = manager;
            inventory = playerInventory;
            Subscribe();
            Rebuild();
        }

        void Subscribe()
        {
            ResolveReferences();

            if (!subscribedManager && gameManager != null)
            {
                gameManager.StateChanged += OnStateChanged;
                subscribedManager = true;
            }

            if (!subscribedInventory && inventory != null)
            {
                inventory.Changed += Rebuild;
                subscribedInventory = true;
            }
        }

        void Unsubscribe()
        {
            if (subscribedManager && gameManager != null)
                gameManager.StateChanged -= OnStateChanged;

            if (subscribedInventory && inventory != null)
                inventory.Changed -= Rebuild;

            subscribedManager = false;
            subscribedInventory = false;
        }

        void OnStateChanged(GameState state)
        {
            // Leaving the player's turn cancels any pending selection (mirrors PlayerController).
            if (state != GameState.PlayerTurn)
                Deselect();
        }

        public void Rebuild()
        {
            ResolveReferences();

            if (inventory == null)
                return;

            PositionTrayOnDesk();

            var contents = inventory.Contents();

            // Remove boxes for stacks that no longer exist.
            for (var i = boxes.Count - 1; i >= 0; i--)
            {
                if (StackIndexOf(contents, boxes[i].Item) < 0)
                {
                    if (selected == boxes[i])
                        selected = null;

                    DestroyBox(boxes[i].gameObject);
                    boxes.RemoveAt(i);
                }
            }

            // Add new boxes and refresh counts.
            for (var i = 0; i < contents.Count; i++)
            {
                var stack = contents[i];
                var box = FindBox(stack.Item);

                if (box == null)
                {
                    box = DeskItemTrayBuilder.BuildBox(transform, this, stack.Item);
                    boxes.Add(box);
                }

                box.SetCount(stack.Count);
            }

            Layout();
        }

        void Layout()
        {
            var start = -(boxes.Count - 1) * 0.5f * BoxSpacing;

            for (var i = 0; i < boxes.Count; i++)
                boxes[i].SetRestPosition(new Vector3(start + i * BoxSpacing, 0f, 0f));
        }

        public void OnBoxClicked(DeskItemBox box)
        {
            if (box == null || selected == box)
                return;

            Deselect();
            selected = box;
            box.SetSelected(true);
        }

        public void OnUseClicked(DeskItemBox box)
        {
            if (box == null || box != selected)
                return;

            var used = gameManager != null && gameManager.TryUseItem(box.Item);

            if (used)
                Deselect();         // box may already be gone (stack emptied); Deselect is null-safe
            else
                box.FlashUnavailable();   // not the player's turn — keep selected, show the cue
        }

        public void OnCancelClicked(DeskItemBox box)
        {
            if (box == selected)
                Deselect();
        }

        void Deselect()
        {
            if (selected != null)
                selected.SetSelected(false);

            selected = null;
        }

        DeskItemBox FindBox(ItemDefinition item)
        {
            for (var i = 0; i < boxes.Count; i++)
            {
                if (boxes[i].Item == item)
                    return boxes[i];
            }

            return null;
        }

        static int StackIndexOf(IReadOnlyList<ItemStack> contents, ItemDefinition item)
        {
            for (var i = 0; i < contents.Count; i++)
            {
                if (contents[i].Item == item)
                    return i;
            }

            return -1;
        }

        void DestroyBox(GameObject go)
        {
            if (Application.isPlaying)
                Destroy(go);
            else
                DestroyImmediate(go);
        }

        // --- Placement (mirrors BookShopView's anchor -> own-transform -> desk-bounds fallback) ---

        void PositionTrayOnDesk()
        {
            if (positioned)
                return;

            if (deskItemsAnchor != null)
            {
                transform.SetPositionAndRotation(deskItemsAnchor.position, deskItemsAnchor.rotation);
                positioned = true;
                return;
            }

            if (transform.position.sqrMagnitude > 0.0001f)
            {
                positioned = true; // already hand-placed on the desk
                return;
            }

            ResolveDesk();

            var cam = Camera.main;

            if (!hasDeskBounds || cam == null)
                return; // try again next Rebuild once a desk/camera exists

            var toCamera = Vector3.ProjectOnPlane(cam.transform.position - deskBounds.center, Vector3.up);
            toCamera = toCamera.sqrMagnitude > 1e-4f ? toCamera.normalized : -Vector3.forward;

            var pos = new Vector3(deskBounds.center.x, deskBounds.max.y + DeskClearance, deskBounds.center.z)
                      + toCamera * (deskBounds.extents.magnitude * 0.35f);

            // Local +X runs along the desk's near edge; boxes lay out along it in Layout().
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(toCamera, Vector3.up));
            positioned = true;
        }

        void ResolveDesk()
        {
            if (deskResolved)
                return;

            deskResolved = true;

            var deskObject = GameObject.Find(deskObjectName)
                             ?? GameObject.Find("Saloon Table")
                             ?? GameObject.Find("Table");

            if (deskObject == null)
                return;

            var renderers = deskObject.GetComponentsInChildren<Renderer>();

            if (renderers.Length == 0)
                return;

            deskBounds = renderers[0].bounds;

            for (var i = 1; i < renderers.Length; i++)
                deskBounds.Encapsulate(renderers[i].bounds);

            hasDeskBounds = true;
        }

        void ResolveReferences()
        {
            if (gameManager == null)
                gameManager = FindAnyObjectByType<GameManager>();

            if (inventory == null && gameManager != null)
                inventory = gameManager.Inventory;

            if (inventory == null)
                inventory = FindAnyObjectByType<PlayerInventory>();
        }
    }
}
```

- [ ] **Step 2: Write `Assets/Tests/EditMode/DeskItemTrayTests.cs`**

```csharp
using Meniscus.Core;
using Meniscus.Items;
using Meniscus.UI;
using NUnit.Framework;
using UnityEngine;

namespace Meniscus.Tests.EditMode
{
    public class DeskItemTrayTests
    {
        GameObject host;
        DeskItemTray tray;
        PlayerInventory inventory;
        GameManager gameManager;

        ItemDefinition itemA;
        ItemDefinition itemB;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("Tray Fixture");
            // GameManager defaults to GameState.StartRound (NOT PlayerTurn), so TryUseItem refuses —
            // exactly the off-turn condition we exercise below.
            gameManager = host.AddComponent<GameManager>();
            inventory = host.AddComponent<PlayerInventory>();
            tray = host.AddComponent<DeskItemTray>();
            tray.Configure(gameManager, inventory);

            itemA = ItemDefinition.Create("a", "Item A", "", 10, ItemEffectKind.PayoutMultiplier, 2f);
            itemB = ItemDefinition.Create("b", "Item B", "", 10, ItemEffectKind.SafeZoneBonus, 1f);
        }

        [TearDown]
        public void TearDown()
        {
            if (host != null)
                Object.DestroyImmediate(host);
        }

        [Test]
        public void Rebuild_MatchesOneBoxPerStackWithCount()
        {
            inventory.Grant(itemA);
            inventory.Grant(itemA);
            inventory.Grant(itemB);

            // inventory.Changed drove Rebuild; assert the box set.
            Assert.AreEqual(2, tray.Boxes.Count, "One box per stack.");

            var boxA = FindBox(itemA);
            var boxB = FindBox(itemB);
            Assert.IsNotNull(boxA);
            Assert.IsNotNull(boxB);
            Assert.AreEqual(2, boxA.Count);
            Assert.AreEqual(1, boxB.Count);
        }

        [Test]
        public void Rebuild_RemovesBoxWhenStackEmptied()
        {
            inventory.Grant(itemA);
            Assert.AreEqual(1, tray.Boxes.Count);

            inventory.TryConsume(itemA);

            Assert.AreEqual(0, tray.Boxes.Count, "Emptied stack removes its box.");
        }

        [Test]
        public void OnBoxClicked_SelectingSecondBoxDeselectsFirst()
        {
            inventory.Grant(itemA);
            inventory.Grant(itemB);

            var boxA = FindBox(itemA);
            var boxB = FindBox(itemB);

            tray.OnBoxClicked(boxA);
            Assert.AreSame(boxA, tray.Selected);
            Assert.IsTrue(boxA.IsSelected);

            tray.OnBoxClicked(boxB);
            Assert.AreSame(boxB, tray.Selected);
            Assert.IsTrue(boxB.IsSelected);
            Assert.IsFalse(boxA.IsSelected, "Selecting a second box deselects the first.");
        }

        [Test]
        public void OnUseClicked_OffTurn_DoesNotConsumeAndStaysSelected()
        {
            inventory.Grant(itemA);
            var boxA = FindBox(itemA);
            tray.OnBoxClicked(boxA);

            // GameManager is in StartRound (not PlayerTurn) → TryUseItem returns false.
            tray.OnUseClicked(boxA);

            Assert.AreEqual(1, inventory.TotalCount, "Off-turn use must not consume the item.");
            Assert.AreSame(boxA, tray.Selected, "Box stays selected after a refused use.");
        }

        [Test]
        public void OnCancelClicked_Deselects()
        {
            inventory.Grant(itemA);
            var boxA = FindBox(itemA);
            tray.OnBoxClicked(boxA);

            tray.OnCancelClicked(boxA);

            Assert.IsNull(tray.Selected);
            Assert.IsFalse(boxA.IsSelected);
        }

        DeskItemBox FindBox(ItemDefinition item)
        {
            foreach (var box in tray.Boxes)
            {
                if (box.Item == item)
                    return box;
            }

            return null;
        }
    }
}
```

- [ ] **Step 3: Compile-check the runtime + test assemblies**

```bash
find Library -name "Meniscus.*.rsp"
CSC="/Applications/Unity/Hub/Editor/6000.3.6f1/Unity.app/Contents/Resources/Scripting/DotNetSdkRoslyn/csc.dll"
dotnet "$CSC" "@Library/Bee/artifacts/<dag>/Meniscus.Runtime.rsp"
dotnet "$CSC" "@Library/Bee/artifacts/<dag>/Meniscus.Tests.EditMode.rsp"
```
Expected: both exit 0 with no `error CS` / `warning CS` lines. (This is the first compile-check; it covers Task 1 + Task 2.)

- [ ] **Step 4: Run the EditMode tests (red→green confirmation)**

```bash
"/Applications/Unity/Hub/Editor/6000.3.6f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -projectPath . -runTests -testPlatform EditMode \
  -testResults Logs/test-results.xml -quit
```
Expected: `DeskItemTrayBuilderTests` (2) and `DeskItemTrayTests` (5) all pass; 0 failures in `Logs/test-results.xml`.

- [ ] **Step 5: Commit Task 1 + Task 2**

```bash
git add Assets/Scripts/UI/DeskItemBox.cs Assets/Scripts/UI/DeskItemBox.cs.meta \
        Assets/Scripts/UI/DeskItemTileRelay.cs Assets/Scripts/UI/DeskItemTileRelay.cs.meta \
        Assets/Scripts/UI/DeskItemTrayBuilder.cs Assets/Scripts/UI/DeskItemTrayBuilder.cs.meta \
        Assets/Scripts/UI/DeskItemTray.cs Assets/Scripts/UI/DeskItemTray.cs.meta \
        Assets/Tests/EditMode/DeskItemTrayBuilderTests.cs Assets/Tests/EditMode/DeskItemTrayBuilderTests.cs.meta \
        Assets/Tests/EditMode/DeskItemTrayTests.cs Assets/Tests/EditMode/DeskItemTrayTests.cs.meta
git commit -m "feat: desk item boxes (DeskItemTray/Box/Builder) replacing the item bar's presentation"
```

> `.meta` files are generated by Unity on first import. If implementing headless, the `.meta` files appear after the next editor open / the EditMode test run in Step 4; add them in this commit if present, otherwise in the Task 3 commit.

---

### Task 3: Swap GameManager + authoring to the tray; delete DeskItemBar

This task lands as one atomic change so the runtime **and** editor assemblies keep compiling (both currently reference the soon-deleted `DeskItemBar`).

**Files:**
- Modify: `Assets/Scripts/Core/GameManager.cs` (field at `:33`, bootstrap at `:702-709`)
- Modify: `Assets/Scripts/Editor/RuntimeObjectAuthoring.cs` (`AuthorDeskItemBar` → `AuthorDeskItemTray`, `Run()`)
- Modify: `Assets/Tests/EditMode/GameFlowTests.cs` (bootstrap test at `:259-271`)
- Delete: `Assets/Scripts/UI/DeskItemBar.cs` (+ `.meta`)
- Delete: `Assets/Scripts/UI/DeskItemBarBuilder.cs` (+ `.meta`)
- Delete: `Assets/Tests/EditMode/DeskItemBarBuilderTests.cs` (+ `.meta`)

**Interfaces:**
- Consumes: `DeskItemTray.Configure(GameManager, PlayerInventory)` (Task 2).
- Produces: `GameManager` serialized field `deskItemTray` (type `DeskItemTray`); `RuntimeObjectAuthoring.AuthorDeskItemTray()`.

- [ ] **Step 1: Update the GameManager bootstrap test in `GameFlowTests.cs`**

Replace the test at lines 259-271:

```csharp
        [Test]
        public void StartMatch_BootstrapsDeskItemTrayWiredToInventory()
        {
            var fixture = CreateGameFixture();
            fixture.GameManager.StartMatch();

            var tray = Object.FindAnyObjectByType<DeskItemTray>();

            Assert.IsNotNull(tray);
            Assert.IsNotNull(fixture.GameManager.Inventory);

            fixture.Destroy();
        }
```

(If `GameFlowTests.cs` lacks `using Meniscus.UI;`, add it — `DeskItemBar` was previously referenced via that namespace, so it is almost certainly already present.)

- [ ] **Step 2: Swap the field and bootstrap in `GameManager.cs`**

Change the field at line 33:

```csharp
        [SerializeField] DeskItemTray deskItemTray;
```

Replace the bootstrap block at lines 702-709:

```csharp
            if (deskItemTray == null)
                deskItemTray = FindAnyObjectByType<DeskItemTray>();

            if (deskItemTray == null)
            {
                deskItemTray = gameObject.AddComponent<DeskItemTray>();
                deskItemTray.Configure(this, playerInventory);
            }
```

- [ ] **Step 3: Replace `AuthorDeskItemBar` with `AuthorDeskItemTray` in `RuntimeObjectAuthoring.cs`**

Replace the menu method + `AuthorDeskItemBar` (lines 114-164) with:

```csharp
        [MenuItem(MenuRoot + "Author Desk Item Tray")]
        static void AuthorDeskItemTrayMenu()
        {
            if (!TryOpenScene(out var scene))
                return;

            if (AuthorDeskItemTray())
                SaveScene(scene);
        }

        public static bool AuthorDeskItemTray()
        {
            var manager = Object.FindAnyObjectByType<GameManager>();

            if (manager == null)
            {
                Debug.LogError("[RuntimeObjectAuthoring] No GameManager in the scene. Aborting.");
                return false;
            }

            // The retired DeskItemBar serializes onto the Managers object as a missing script once its
            // class is deleted; purge it so re-authoring leaves a clean component set.
            var removed = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(manager.gameObject);

            var tray = Object.FindAnyObjectByType<DeskItemTray>();

            if (tray == null)
            {
                Undo.RegisterCompleteObjectUndo(manager.gameObject, "Author Desk Item Tray");
                tray = manager.gameObject.AddComponent<DeskItemTray>();
            }

            var inventory = Object.FindAnyObjectByType<PlayerInventory>();

            var traySerialized = new SerializedObject(tray);
            traySerialized.FindProperty("gameManager").objectReferenceValue = manager;
            traySerialized.FindProperty("inventory").objectReferenceValue = inventory;
            traySerialized.ApplyModifiedProperties();

            var managerSerialized = new SerializedObject(manager);
            managerSerialized.FindProperty("deskItemTray").objectReferenceValue = tray;
            managerSerialized.ApplyModifiedProperties();

            Debug.Log($"[RuntimeObjectAuthoring] DeskItemTray authored and wired " +
                      $"(removed {removed} missing script(s); position via deskItemsAnchor or the desk auto-fit).");
            return true;
        }
```

Then update `Run()` (line 295) — replace `changed |= AuthorDeskItemBar();` with:

```csharp
            changed |= AuthorDeskItemTray();
```

(`GameObjectUtility` lives in `UnityEditor`, already imported.)

- [ ] **Step 4: Delete the retired files**

```bash
git rm Assets/Scripts/UI/DeskItemBar.cs Assets/Scripts/UI/DeskItemBar.cs.meta \
       Assets/Scripts/UI/DeskItemBarBuilder.cs Assets/Scripts/UI/DeskItemBarBuilder.cs.meta \
       Assets/Tests/EditMode/DeskItemBarBuilderTests.cs Assets/Tests/EditMode/DeskItemBarBuilderTests.cs.meta
```

- [ ] **Step 5: Confirm no dangling references**

```bash
grep -rn "DeskItemBar" Assets/Scripts Assets/Tests
```
Expected: **no matches.** (The only remaining `DeskItemBar` text will be inside `Assets/Scenes/Saloon.unity` as a now-missing script reference, purged in Step 8.)

- [ ] **Step 6: Compile-check runtime + editor + test assemblies**

```bash
find Library -name "Meniscus.*.rsp"
CSC="/Applications/Unity/Hub/Editor/6000.3.6f1/Unity.app/Contents/Resources/Scripting/DotNetSdkRoslyn/csc.dll"
dotnet "$CSC" "@Library/Bee/artifacts/<dag>/Meniscus.Runtime.rsp"
dotnet "$CSC" "@Library/Bee/artifacts/<dag>/Meniscus.Editor.rsp"
dotnet "$CSC" "@Library/Bee/artifacts/<dag>/Meniscus.Tests.EditMode.rsp"
```
Expected: all exit 0, no `error CS` / `warning CS`. (If `Meniscus.Editor.rsp` has a different basename, locate it via the `find` output.)

- [ ] **Step 7: Run EditMode tests**

```bash
"/Applications/Unity/Hub/Editor/6000.3.6f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -projectPath . -runTests -testPlatform EditMode \
  -testResults Logs/test-results.xml -quit
```
Expected: full EditMode suite passes (including the renamed `StartMatch_BootstrapsDeskItemTrayWiredToInventory`); 0 failures.

- [ ] **Step 8: Re-author the scene (replaces the bar component with the tray, purges the missing script)**

```bash
"/Applications/Unity/Hub/Editor/6000.3.6f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -projectPath . -executeMethod Meniscus.Editor.RuntimeObjectAuthoring.Run -quit
```
Then confirm the scene no longer references the bar:
```bash
grep -c "DeskItemBar" Assets/Scenes/Saloon.unity   # expected: 0
```

- [ ] **Step 9: Commit**

```bash
git add Assets/Scripts/Core/GameManager.cs Assets/Scripts/Editor/RuntimeObjectAuthoring.cs \
        Assets/Tests/EditMode/GameFlowTests.cs Assets/Scenes/Saloon.unity
git add -A Assets/Scripts/UI Assets/Tests/EditMode   # stage deletions + any new .meta files
git commit -m "feat: wire DeskItemTray into GameManager + authoring; remove DeskItemBar"
```

- [ ] **Step 10: Manual in-engine check (placeholder behavior)**

Open `Saloon.unity`, enter Play mode, buy an item from the book shop, and confirm: a labeled box appears in a row on the player's edge of the desk; clicking it raises it and shows Use/Cancel; Use on your turn consumes it and updates the count/removes the box; Use off-turn flashes the box and keeps it selected; Cancel/clicking another box deselects.

---

## Self-Review

**1. Spec coverage:**
- Replace the bar → Task 3 deletes `DeskItemBar`/`DeskItemBarBuilder`, swaps `GameManager` + authoring. ✓
- Click-to-select, Use/Cancel confirm → `DeskItemBox` tiles + `DeskItemTray.OnBoxClicked/OnUseClicked/OnCancelClicked`. ✓
- One box per stack + count → `Rebuild` reconciliation + `SetCount`. ✓
- Always visible, usable only on `PlayerTurn` → boxes built regardless of state; Use delegates to `TryUseItem` (state-guarded) + off-turn `FlashUnavailable`. ✓
- Row along player's edge, anchor/own-transform/desk-bounds fallback → `PositionTrayOnDesk` + `Layout`. ✓
- Authoring `Author Desk Item Tray` in `Author All` → Task 3 Step 3. ✓
- Tests: reconciliation, single-selection, turn-gating, builder geometry → Task 1 + Task 2 tests. ✓

**2. Placeholder scan:** No TBD/TODO/"handle edge cases"; every code step shows complete code. ✓

**3. Type consistency:** `DeskItemTray` field names (`gameManager`, `inventory`, `deskItemsAnchor`) match the `FindProperty` strings in Task 3's authoring. `DeskItemBox.Initialize(...)` 6-arg signature matches `DeskItemTrayBuilder.BuildBox`'s call. `OnBoxClicked/OnUseClicked/OnCancelClicked` names match between `DeskItemBox.Notify*` relays and `DeskItemTray`. `Configure(GameManager, PlayerInventory)` matches the `GameManager` bootstrap call. `ItemDefinition.Create` 6-arg signature matches usage in tests. ✓
