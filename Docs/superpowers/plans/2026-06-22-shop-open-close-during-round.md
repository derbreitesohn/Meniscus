# Open/Close the Shop During the Round — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the player open and close the diegetic book shop during a round by clicking the book prop, showing the catalog read-only (no buying).

**Architecture:** The open/close/browse state machine already exists in `BookShopView.OnBookClicked()` but is unreachable — it is fed by `BookClickTarget`'s legacy `OnMouse*` messages, which never fire under this project's new-Input-System backend. We (1) extract the toggle decision and a browse gate into pure, unit-testable functions, (2) reduce `BookClickTarget` to a plain marker, then (3) detect book clicks with the same `Mouse.current` + `Physics.Raycast` pattern the rest of the game uses, gated so browsing is blocked only in `ShopPhase`/`GameOver`.

**Tech Stack:** Unity 6000.3.6f1, C#, Unity Input System 1.18.0 (new backend only), NUnit EditMode tests.

## Global Constraints

- **Line numbers in this plan are indicative only.** A concurrent session rewrote
  `BookShopView.cs` (+458 lines: select-to-buy ticket, paginated list, animated page-flip)
  after this plan was written, so cited line ranges have shifted. Every quoted "before"
  code block was re-verified against the current file and still matches verbatim — make
  each edit by matching the quoted code, not the line number. Place newly added members
  sensibly among their peers; exact position does not affect compilation.

- Input: **Input System Package (New) only** (`activeInputHandler: 1`). Use `UnityEngine.InputSystem` (`Mouse.current`); never legacy `Input` / `OnMouse*` messages.
- Offline compile-check is the documented "before done" bar (full EditMode test runs + visual checks happen in-engine, run by the user). Compile-check commands:
  ```bash
  CSC="/Applications/Unity/Hub/Editor/6000.3.6f1/Unity.app/Contents/Resources/Scripting/DotNetSdkRoslyn/csc.dll"
  dotnet "$CSC" "@Library/Bee/artifacts/200b0aE.dag/Meniscus.Runtime.rsp"
  dotnet "$CSC" "@Library/Bee/artifacts/200b0aE.dag/Meniscus.Tests.EditMode.rsp"
  ```
  Run from the project root. The `200b0aE.dag` id can change between editor sessions — if a path is missing, find the current one with `find Library -name "Meniscus.*.rsp"`. Clean exit with no `error CS` lines = compiles.
- Browse is read-only: opening mid-round must NOT enable purchasing. Buying stays in `ShopPhase` only. Do not change purchasing rules, catalog, or `ShopPhase` flow.
- Keep `BookClickTarget` (the authored scene book at `Assets/Scenes/Saloon.unity:9403` carries a serialized instance; deleting the class would orphan it into a missing script).

---

## File Structure

- `Assets/Scripts/UI/BookShopView.cs` — owns the book's interaction. Gains a pure `ComputeBrowseToggle`, a browse-gate delegate + setter, and a `Mouse`/`Physics.Raycast` click poll in `Update()`. `OnBookClicked` refactored to consume `ComputeBrowseToggle`. `BookClickTarget` (same file) reduced to a marker.
- `Assets/Scripts/UI/ShopManager.cs` — gains a pure `BrowsingAllowed(GameState)` and wires the gate onto the book when it prepares/opens it.
- `Assets/Tests/EditMode/ShopBrowseGateTests.cs` — **new**; unit tests for the two pure functions.
- `Assets/Tests/EditMode/BookShopBuilderTests.cs` — unchanged assertion (marker still present); no edit expected.

---

## Task 1: Pure browse logic + gate, with unit tests

Extract the two pure decisions so they can be unit-tested without building Unity objects or running play mode, refactor `OnBookClicked` to use the toggle function, and reduce `BookClickTarget` to a marker. After this task, clicking still does nothing at runtime (the only caller was the dead `OnMouse*` path) — that wiring is Task 2. No behavior regression, since the click never worked.

**Files:**
- Modify: `Assets/Scripts/UI/BookShopView.cs` (add `ComputeBrowseToggle`; refactor `OnBookClicked`; reduce `BookClickTarget`; drop dead wiring in `BuildProp`/`ResolveAuthoredProp`)
- Modify: `Assets/Scripts/UI/ShopManager.cs` (add `BrowsingAllowed`)
- Test: `Assets/Tests/EditMode/ShopBrowseGateTests.cs` (new)

**Interfaces:**
- Produces:
  - `public static (bool isOpen, bool previewMode) BookShopView.ComputeBrowseToggle(bool isOpen, bool previewMode)`
  - `public static bool ShopManager.BrowsingAllowed(Meniscus.Core.GameState state)`

- [ ] **Step 1: Write the failing tests**

Create `Assets/Tests/EditMode/ShopBrowseGateTests.cs`:

```csharp
using Meniscus.Core;
using Meniscus.UI;
using NUnit.Framework;

namespace Meniscus.Tests.EditMode
{
    public class ShopBrowseGateTests
    {
        [Test]
        public void ComputeBrowseToggle_FromClosed_OpensReadOnlyPreview()
        {
            var (isOpen, previewMode) = BookShopView.ComputeBrowseToggle(false, false);

            Assert.IsTrue(isOpen, "Clicking the closed book opens it.");
            Assert.IsTrue(previewMode, "Mid-round it opens as a read-only preview.");
        }

        [Test]
        public void ComputeBrowseToggle_FromPreview_Closes()
        {
            var (isOpen, previewMode) = BookShopView.ComputeBrowseToggle(true, true);

            Assert.IsFalse(isOpen, "Clicking while previewing closes the book.");
            Assert.IsFalse(previewMode);
        }

        [Test]
        public void ComputeBrowseToggle_FromShopPhaseOpen_IsUnchanged()
        {
            // Shop-phase open is (isOpen=true, previewMode=false): a click is ignored
            // (the player closes it via "Finish Drink").
            var (isOpen, previewMode) = BookShopView.ComputeBrowseToggle(true, false);

            Assert.IsTrue(isOpen);
            Assert.IsFalse(previewMode);
        }

        [Test]
        public void BrowsingAllowed_DuringRoundStates_IsTrue()
        {
            Assert.IsTrue(ShopManager.BrowsingAllowed(GameState.StartRound));
            Assert.IsTrue(ShopManager.BrowsingAllowed(GameState.PlayerTurn));
            Assert.IsTrue(ShopManager.BrowsingAllowed(GameState.EnemyTurn));
            Assert.IsTrue(ShopManager.BrowsingAllowed(GameState.Resolution));
            Assert.IsTrue(ShopManager.BrowsingAllowed(GameState.RestockPhase));
        }

        [Test]
        public void BrowsingAllowed_InShopPhaseOrGameOver_IsFalse()
        {
            Assert.IsFalse(ShopManager.BrowsingAllowed(GameState.ShopPhase));
            Assert.IsFalse(ShopManager.BrowsingAllowed(GameState.GameOver));
        }
    }
}
```

- [ ] **Step 2: Compile-check to verify it fails (red)**

Run:
```bash
CSC="/Applications/Unity/Hub/Editor/6000.3.6f1/Unity.app/Contents/Resources/Scripting/DotNetSdkRoslyn/csc.dll"
dotnet "$CSC" "@Library/Bee/artifacts/200b0aE.dag/Meniscus.Tests.EditMode.rsp"
```
Expected: FAILS with `error CS0117`/`CS1061` — `ComputeBrowseToggle` and `BrowsingAllowed` do not exist yet. (This is the offline "red": the test file cannot compile until the methods exist. Assertion-level pass is confirmed in-engine.)

- [ ] **Step 3: Add `ComputeBrowseToggle` and refactor `OnBookClicked` in `BookShopView.cs`**

Replace the whole `OnBookClicked()` method (currently `Assets/Scripts/UI/BookShopView.cs:173-196`) with:

```csharp
        /// <summary>
        /// Pure transition for a book-prop click. Closed → opens as a read-only preview;
        /// previewing → closes; shop-phase-open (open, not preview) → unchanged (the player
        /// closes that via "Finish Drink"). No side effects, so it is unit-testable.
        /// </summary>
        public static (bool isOpen, bool previewMode) ComputeBrowseToggle(bool isOpen, bool previewMode)
        {
            if (!isOpen)
                return (true, true);

            if (previewMode)
                return (false, false);

            return (isOpen, previewMode);
        }

        /// <summary>
        /// Applies a book-prop click. Closed on the desk, a click lifts it open as a read-only
        /// preview; while previewing, a click closes it again. Ignored once the shop phase has
        /// opened it for real, where closing is done via "Finish Drink".
        /// </summary>
        void OnBookClicked()
        {
            Log($"OnBookClicked received. built={built}, isOpen={isOpen}, previewMode={previewMode}");

            if (!built)
                return;

            (isOpen, previewMode) = ComputeBrowseToggle(isOpen, previewMode);
            Log($"-> isOpen={isOpen}, previewMode={previewMode}");
        }
```

- [ ] **Step 4: Reduce `BookClickTarget` to a marker in `BookShopView.cs`**

Replace the whole `BookClickTarget` class (currently `Assets/Scripts/UI/BookShopView.cs:542-566`) with:

```csharp
    /// <summary>
    /// Marker placed on the book's click collider so <see cref="BookShopView"/>'s raycast can
    /// identify the prop it hit. Clicks are detected by <see cref="BookShopView"/> via the Input
    /// System (legacy OnMouse* messages do not fire under the new input backend), so this type
    /// carries no behaviour — it is purely a tag.
    /// </summary>
    [DisallowMultipleComponent]
    public class BookClickTarget : MonoBehaviour
    {
    }
```

- [ ] **Step 5: Drop the dead click-target wiring in `BuildProp` and `ResolveAuthoredProp`**

In `BuildProp()` (`Assets/Scripts/UI/BookShopView.cs:266-282`), remove the three lines that fetch the click target and assign `Clicked`/`LogDiagnostics`. The method body becomes:

```csharp
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
        }
```

In `ResolveAuthoredProp()` (`Assets/Scripts/UI/BookShopView.cs:284-297`), keep ensuring the marker exists (the raycast needs it) but drop the delegate/flag assignment. The method body becomes:

```csharp
        void ResolveAuthoredProp()
        {
            root = authoredBook;
            hingePivot = root.Find("Book Hinge");

            // The raycast identifies the book by this marker, so guarantee an authored prop has one.
            if (root.GetComponent<BookClickTarget>() == null)
                root.gameObject.AddComponent<BookClickTarget>();
        }
```

- [ ] **Step 6: Add `BrowsingAllowed` to `ShopManager.cs`**

`ShopManager` already has `using Meniscus.Core;`. Add this static method inside the `ShopManager` class (e.g. just after the `Catalog` property at `Assets/Scripts/UI/ShopManager.cs:26`):

```csharp
        /// <summary>
        /// Whether the player may open the book to browse right now: any time a round is in
        /// progress, but not during the post-round shop phase (closed via "Finish Drink") or on
        /// the game-over screen.
        /// </summary>
        public static bool BrowsingAllowed(GameState state)
            => state != GameState.ShopPhase && state != GameState.GameOver;
```

- [ ] **Step 7: Compile-check Runtime + EditMode (green)**

Run:
```bash
CSC="/Applications/Unity/Hub/Editor/6000.3.6f1/Unity.app/Contents/Resources/Scripting/DotNetSdkRoslyn/csc.dll"
dotnet "$CSC" "@Library/Bee/artifacts/200b0aE.dag/Meniscus.Runtime.rsp"
dotnet "$CSC" "@Library/Bee/artifacts/200b0aE.dag/Meniscus.Tests.EditMode.rsp"
```
Expected: both exit cleanly with no `error CS` lines. (Run the new tests in-engine via the Unity Test Runner to confirm assertions pass.)

- [ ] **Step 8: Commit**

```bash
git add Assets/Scripts/UI/BookShopView.cs Assets/Scripts/UI/ShopManager.cs Assets/Tests/EditMode/ShopBrowseGateTests.cs
git commit -m "refactor: extract pure browse-toggle + gate; reduce BookClickTarget to a marker

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: Detect book clicks via the Input System and wire the gate

Make the book actually open/close at runtime: poll the mouse and raycast against the book's collider in `Update()`, gated by `BrowsingAllowed`. This is play-mode wiring (raycast against a live collider), so it is verified by running the app rather than by a unit test.

**Files:**
- Modify: `Assets/Scripts/UI/BookShopView.cs` (add `using UnityEngine.InputSystem;`, a `clickRaycastDistance` field, a `canBrowse` gate + `SetBrowseGate`, and a click poll in `Update()`)
- Modify: `Assets/Scripts/UI/ShopManager.cs` (wire the gate when preparing/opening the book)

**Interfaces:**
- Consumes (from Task 1): `BookShopView.ComputeBrowseToggle`, `ShopManager.BrowsingAllowed(GameState)`.
- Produces: `public void BookShopView.SetBrowseGate(System.Func<bool> canBrowse)`.

- [ ] **Step 1: Add the Input System using-directive in `BookShopView.cs`**

At the top of `Assets/Scripts/UI/BookShopView.cs` (currently lines 1-5), add the import so the file reads:

```csharp
using System;
using System.Collections.Generic;
using Meniscus.Core;
using Meniscus.Items;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
```

(`System` is for `Func<bool>`.)

- [ ] **Step 2: Add the gate field, setter, and raycast distance**

In the fields block near the other serialized fields (e.g. just after `[SerializeField] float menuWorldWidth = 0.56f;` at `Assets/Scripts/UI/BookShopView.cs:76`), add:

```csharp
        [Tooltip("Max distance for the click raycast that opens/closes the book during a round.")]
        [SerializeField] float clickRaycastDistance = 100f;
```

In the non-serialized field block near `bool previewMode;` (around `Assets/Scripts/UI/BookShopView.cs:110`), add:

```csharp
        // Supplied by ShopManager: returns true while the player may open the book to browse
        // (during a round) and false otherwise (shop phase / game over). Null means "allowed".
        Func<bool> canBrowse;
```

Add the setter as a public method (e.g. just after `Close()` at `Assets/Scripts/UI/BookShopView.cs:166`):

```csharp
        /// <summary>
        /// Sets the predicate deciding whether a book-prop click may open a browse preview.
        /// </summary>
        public void SetBrowseGate(Func<bool> gate) => canBrowse = gate;
```

- [ ] **Step 3: Poll for the click in `Update()`**

Add the call at the top of `Update()`. In the current file the guard is immediately
followed by `UpdatePageTurn();` — insert the poll between them. Match this exact block:

```csharp
            if (!built)
                return;

            UpdatePageTurn();
```

and replace it with:

```csharp
            if (!built)
                return;

            PollBrowseClick();
            UpdatePageTurn();
```

Then add the `PollBrowseClick` method (e.g. just below `Update()`):

```csharp
        /// <summary>
        /// Detects a click on the book prop using the Input System (the project's input backend
        /// does not deliver legacy OnMouse* messages) and toggles the read-only browse preview,
        /// provided the gate currently allows browsing.
        /// </summary>
        void PollBrowseClick()
        {
            var mouse = Mouse.current;

            if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
                return;

            if (canBrowse != null && !canBrowse())
                return;

            var cam = ActiveCamera();

            if (cam == null)
                return;

            var ray = cam.ScreenPointToRay(mouse.position.ReadValue());

            if (!Physics.Raycast(ray, out var hit, clickRaycastDistance))
                return;

            // Only our own book counts: the marker sits on the click collider's root.
            var marker = hit.collider.GetComponentInParent<BookClickTarget>();

            if (marker == null || marker.transform != root)
                return;

            OnBookClicked();
        }
```

- [ ] **Step 4: Wire the gate from `ShopManager`**

In `ShopManager.cs`, add a helper and call it wherever the book is ensured. Add the helper (e.g. just after `EnsureBookShop()` at `Assets/Scripts/UI/ShopManager.cs:233`):

```csharp
        void WireBrowseGate()
        {
            if (bookShop != null)
                bookShop.SetBrowseGate(() => gameManager == null || BrowsingAllowed(gameManager.CurrentState));
        }
```

In `Start()` (`Assets/Scripts/UI/ShopManager.cs:34-45`), set the gate after the book is prepared. The method becomes:

```csharp
        void Start()
        {
            // Seat the diegetic book on the desk from the start of the match so it is a visible prop
            // during the rounds and is lifted from there when the shop opens, rather than spawning.
            if (!useDiegeticBookShop)
                return;

            EnsureBookShop();
            WireBrowseGate();

            if (bookShop != null)
                bookShop.PrepareOnDesk(Catalog, this);
        }
```

In `ShowShop()`, also wire the gate after `EnsureBookShop()` so a runtime-created book (the `FindAnyObjectByType`/`new GameObject` fallback path) is gated too. Update the block at `Assets/Scripts/UI/ShopManager.cs:62-72` to:

```csharp
            if (useDiegeticBookShop)
            {
                EnsureBookShop();
                WireBrowseGate();

                if (bookShop != null)
                {
                    HideShopCanvas();
                    bookShop.Open(Catalog, this);
                    return;
                }
            }
```

- [ ] **Step 5: Compile-check Runtime + EditMode**

Run:
```bash
CSC="/Applications/Unity/Hub/Editor/6000.3.6f1/Unity.app/Contents/Resources/Scripting/DotNetSdkRoslyn/csc.dll"
dotnet "$CSC" "@Library/Bee/artifacts/200b0aE.dag/Meniscus.Runtime.rsp"
dotnet "$CSC" "@Library/Bee/artifacts/200b0aE.dag/Meniscus.Tests.EditMode.rsp"
```
Expected: both exit cleanly with no `error CS` lines.

- [ ] **Step 6: Verify in-engine (play mode)**

Open the Saloon scene and enter play mode. Confirm:
1. During a round (e.g. `PlayerTurn`), clicking the book on the desk lifts it open showing the catalog; the "Finish Drink" button is hidden and a browse hint is shown; clicking the buy rows does nothing (read-only). Clicking the book again closes it.
2. Browsing works across round phases (it is gated only by `ShopPhase`/`GameOver`).
3. Reaching the post-round shop phase still opens the book buyable with a working "Finish Drink"; clicking the book itself does not toggle it closed there.
4. On the game-over screen, clicking where the book is does nothing.
5. Coin/glass clicks during `PlayerTurn` still work as before (no regression from the second raycast).

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/UI/BookShopView.cs Assets/Scripts/UI/ShopManager.cs
git commit -m "feat: open/close the shop book mid-round via Input System raycast

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Self-Review

**Spec coverage:**
- "Migrate book click to new-input raycast pattern" → Task 2, Step 3 (`PollBrowseClick`).
- "Self-contained in `BookShopView` (approach A)" → all click handling lives in `BookShopView`; `ShopManager` only supplies the gate predicate.
- "`BookClickTarget` becomes a marker; don't orphan the scene component" → Task 1, Step 4 (class kept, reduced to empty marker).
- "Remove dead `OnMouse`/`Clicked` wiring" → Task 1, Steps 4-5.
- "Browse gate: block only `ShopPhase`/`GameOver`" → Task 1, Step 6 (`BrowsingAllowed`) + Task 2, Step 4 (wiring).
- "Read-only mid-round (no buying)" → preserved; `OnBookClicked` sets `previewMode`, and `Update()`'s existing `canPurchase = reveal > 0.95f && !previewMode` keeps the menu non-interactive. No purchasing code touched.
- "Keep `OnBookClicked` transitions unit-testable; add gate coverage" → Task 1 tests (`ComputeBrowseToggle`, `BrowsingAllowed`).
- "`BookShopBuilderTests.cs:31` stays valid" → marker retained; builder still adds it (unchanged).

**Placeholder scan:** none — every code step shows complete code; every run step shows the command and expected result.

**Type consistency:** `ComputeBrowseToggle(bool, bool) -> (bool isOpen, bool previewMode)`, `BrowsingAllowed(GameState) -> bool`, and `SetBrowseGate(Func<bool>)` are used with identical signatures in their definitions (Task 1 Steps 3/6, Task 2 Steps 2/4) and their consumers (Task 1 tests; Task 2 `OnBookClicked`, `PollBrowseClick`, `WireBrowseGate`). `root`, `built`, `ActiveCamera()`, `Log()` all pre-exist in `BookShopView`.
