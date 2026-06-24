# Book Shop Menu — Fit-to-Page, Select-to-Buy, Page Turning — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the diegetic book shop fit within the page, show a persistent select-to-buy flow with owned-item marking, and paginate the catalog with an animated page flip.

**Architecture:** Pull the menu's pure math (fit-scale, pagination) and view-model (selection, current page, buy-state) out of `BookShopView` into two small testable classes (`MenuLayout`, `MenuSelectionModel`). `BookShopView` becomes presentation only: it renders the model onto a left "order ticket" page and a paginated right "list" page, and drives the page-flip animation. `ShopManager` gains read-only queries (banked cash, desk-full, owned count) so the book can show prices and badges without reaching past it.

**Tech Stack:** Unity 6000.3.6f1, C#, uGUI (world-space `Canvas`), NUnit EditMode tests, `RuntimeUiFactory` for procedural UI, `BookShopBuilder` for procedural prop geometry.

## Global Constraints

- Unity editor version: `6000.3.6f1` (from `ProjectSettings/ProjectVersion.txt`).
- New UI classes go in namespace `Meniscus.UI`; `ItemDefinition`/`PlayerInventory` are in `Meniscus.Items`; `GameConstants` is in `Meniscus.Core` (`DeskCapacity = 8`).
- Verbatim UI copy (do not paraphrase):
  - Left page title: `Saloon Menu`
  - Empty-selection hint: `Pick an order from the menu →`
  - Buy button enabled label: `Buy — $<cost>` (em dash U+2014, spaces around it)
  - Buy disabled reasons: `Need $<cost>` (unaffordable), `Desk full` (inventory full)
  - Owned badge (shown only when owned count ≥ 1): `✓×<count>`
  - Finish button: `Finish Drink`
  - Page indicator (hidden when total pages == 1): `Page <n> / <m>` (n is 1-based)
  - Prev/next page arrows: `‹` (prev) / `›` (next)
- Auto-fit scale: `menuScaleBase = min(menuWorldWidth / pixelWidth, menuWorldDepth / pixelHeight)`.
- New serialized defaults on `BookShopView`: `menuWorldDepth = 0.34f`, `pageTurnSeconds = 0.35f`; list targets ≈ 5 rows per page.
- Do NOT change `ShopManager.TryBuyItem` economy/inventory logic — only add read-only queries.
- **Before marking any task done:** offline compile-check both assemblies per the `offline-compile-check` memory (Roslyn + Bee rsp files); a clean exit with no `error CS` lines is the pre-flight bar. Find current rsp files with `find Library -name "Meniscus.*.rsp"`.
- Run EditMode tests in-engine via the Unity Test Runner, or headless:
  `"/Applications/Unity/Hub/Editor/6000.3.6f1/Unity.app/Contents/MacOS/Unity" -runTests -batchmode -projectPath . -testPlatform EditMode -testResults /private/tmp/claude-501/-Users-huti-Meniscus/f249406b-15ea-47e8-a0b8-1af524fd1717/scratchpad/results.xml -logFile -`

---

### Task 1: `MenuLayout` — pure fit-scale and pagination math

**Files:**
- Create: `Assets/Scripts/UI/MenuLayout.cs`
- Test: `Assets/Tests/EditMode/MenuLayoutTests.cs`
- Reference: `Assets/Scripts/UI/BookShopView.cs:299-319` (current scale formula being replaced)

**Interfaces:**
- Consumes: nothing (pure static math over `UnityEngine.Mathf`).
- Produces:
  - `static float MenuLayout.ComputeScale(float worldWidth, float worldDepth, float pixelWidth, float pixelHeight)` → world-units-per-pixel that fits both bounds; `0f` if a pixel dimension ≤ 0.
  - `static int MenuLayout.ItemsPerPage(float listDepthPx, float rowHeight)` → ≥ 1.
  - `static int MenuLayout.PageCount(int itemCount, int itemsPerPage)` → ≥ 1.
  - `static int MenuLayout.ClampPage(int page, int pageCount)` → in `[0, pageCount-1]`.

- [ ] **Step 1: Write the failing test**

```csharp
// Assets/Tests/EditMode/MenuLayoutTests.cs
using Meniscus.UI;
using NUnit.Framework;

namespace Meniscus.Tests.EditMode
{
    public class MenuLayoutTests
    {
        [Test]
        public void ComputeScale_PicksTheTighterOfWidthAndDepth()
        {
            // width-fit = 0.56/1040 ≈ 0.000538; depth-fit = 0.34/900 ≈ 0.000378 -> depth wins
            var scale = MenuLayout.ComputeScale(0.56f, 0.34f, 1040f, 900f);
            Assert.AreEqual(0.34f / 900f, scale, 1e-7f);
        }

        [Test]
        public void ComputeScale_KeepsCanvasWithinBothBounds()
        {
            const float w = 0.56f, d = 0.34f, pw = 1040f, ph = 1500f;
            var scale = MenuLayout.ComputeScale(w, d, pw, ph);
            Assert.LessOrEqual(scale * pw, w + 1e-6f);
            Assert.LessOrEqual(scale * ph, d + 1e-6f);
        }

        [Test]
        public void ComputeScale_ZeroPixelDimension_ReturnsZero()
        {
            Assert.AreEqual(0f, MenuLayout.ComputeScale(0.56f, 0.34f, 0f, 900f));
        }

        [Test]
        public void ItemsPerPage_FloorsAndIsAtLeastOne()
        {
            Assert.AreEqual(5, MenuLayout.ItemsPerPage(520f, 96f)); // floor(5.41)
            Assert.AreEqual(1, MenuLayout.ItemsPerPage(10f, 96f));  // floor(0.10) clamped up
        }

        [Test]
        public void PageCount_CeilsAndIsAtLeastOne()
        {
            Assert.AreEqual(1, MenuLayout.PageCount(0, 5));
            Assert.AreEqual(1, MenuLayout.PageCount(5, 5));
            Assert.AreEqual(2, MenuLayout.PageCount(6, 5));
            Assert.AreEqual(2, MenuLayout.PageCount(9, 5));
        }

        [Test]
        public void ClampPage_StaysInRange()
        {
            Assert.AreEqual(0, MenuLayout.ClampPage(-3, 2));
            Assert.AreEqual(1, MenuLayout.ClampPage(9, 2));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run the EditMode test command (Global Constraints). Expected: FAIL — `MenuLayout` does not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

```csharp
// Assets/Scripts/UI/MenuLayout.cs
using UnityEngine;

namespace Meniscus.UI
{
    /// <summary>
    /// Pure layout math for the book menu: how big the printed canvas may be so it fits within the
    /// page rectangle, and how the catalog divides into pages. No scene or UnityEngine.UI dependency,
    /// so it is unit-tested directly.
    /// </summary>
    public static class MenuLayout
    {
        /// <summary>World units per canvas pixel that keeps the canvas inside both page bounds.</summary>
        public static float ComputeScale(float worldWidth, float worldDepth, float pixelWidth, float pixelHeight)
        {
            if (pixelWidth <= 0f || pixelHeight <= 0f)
                return 0f;

            return Mathf.Min(worldWidth / pixelWidth, worldDepth / pixelHeight);
        }

        public static int ItemsPerPage(float listDepthPx, float rowHeight)
        {
            if (rowHeight <= 0f)
                return 1;

            return Mathf.Max(1, Mathf.FloorToInt(listDepthPx / rowHeight));
        }

        public static int PageCount(int itemCount, int itemsPerPage)
        {
            if (itemsPerPage <= 0)
                return 1;

            return Mathf.Max(1, Mathf.CeilToInt(itemCount / (float)itemsPerPage));
        }

        public static int ClampPage(int page, int pageCount) =>
            Mathf.Clamp(page, 0, Mathf.Max(0, pageCount - 1));
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run the EditMode test command. Expected: PASS (6 tests).

- [ ] **Step 5: Offline compile-check + commit**

```bash
# compile-check (see offline-compile-check memory for the csc.dll path / current <dag>)
git add Assets/Scripts/UI/MenuLayout.cs Assets/Tests/EditMode/MenuLayoutTests.cs
git commit -m "feat: MenuLayout pure fit-scale and pagination math"
```

---

### Task 2: `MenuSelectionModel` — selection, current page, and buy-state view-model

**Files:**
- Create: `Assets/Scripts/UI/MenuSelectionModel.cs`
- Test: `Assets/Tests/EditMode/MenuSelectionModelTests.cs`

**Interfaces:**
- Consumes: `MenuLayout` (Task 1); `Meniscus.Items.ItemDefinition` (`Id`, `DisplayName`, `Description`, `Cost`).
- Produces:
  - `struct BuyState { bool CanBuy; string Label; }`
  - `class MenuSelectionModel` with:
    - `void SetCatalog(IReadOnlyList<ItemDefinition> items, int itemsPerPage)` — drops nulls, resets page to 0 and selection to null.
    - `int Count { get; }`, `int ItemsPerPage { get; }`, `int CurrentPage { get; }`, `int PageCount { get; }`
    - `ItemDefinition Selected { get; }`
    - `IReadOnlyList<ItemDefinition> CurrentPageItems()`
    - `bool CanTurnPrev { get; }`, `bool CanTurnNext { get; }`, `void TurnPrev()`, `void TurnNext()`
    - `void Select(ItemDefinition item)` — ignores items not in the catalog.
    - `BuyState EvaluateBuy(int bankedCash, bool deskFull)`

- [ ] **Step 1: Write the failing test**

```csharp
// Assets/Tests/EditMode/MenuSelectionModelTests.cs
using System.Collections.Generic;
using Meniscus.Items;
using Meniscus.UI;
using NUnit.Framework;

namespace Meniscus.Tests.EditMode
{
    public class MenuSelectionModelTests
    {
        static List<ItemDefinition> Catalog(int n)
        {
            var list = new List<ItemDefinition>();
            for (var i = 0; i < n; i++)
                list.Add(ItemDefinition.Create($"id_{i}", $"Item {i}", $"Desc {i}", 10 * (i + 1), ItemEffectKind.PayoutMultiplier, 0f));
            return list;
        }

        [Test]
        public void SetCatalog_DropsNullsAndResetsPageAndSelection()
        {
            var model = new MenuSelectionModel();
            var list = Catalog(3);
            list.Add(null);
            model.SetCatalog(list, 5);
            Assert.AreEqual(3, model.Count);
            Assert.AreEqual(0, model.CurrentPage);
            Assert.IsNull(model.Selected);
        }

        [Test]
        public void CurrentPageItems_SlicesByPage()
        {
            var model = new MenuSelectionModel();
            model.SetCatalog(Catalog(9), 5);
            Assert.AreEqual(2, model.PageCount);
            Assert.AreEqual(5, model.CurrentPageItems().Count);
            model.TurnNext();
            Assert.AreEqual(1, model.CurrentPage);
            Assert.AreEqual(4, model.CurrentPageItems().Count);
        }

        [Test]
        public void Turn_RespectsBoundsAndCanFlags()
        {
            var model = new MenuSelectionModel();
            model.SetCatalog(Catalog(9), 5);
            Assert.IsFalse(model.CanTurnPrev);
            Assert.IsTrue(model.CanTurnNext);
            model.TurnPrev();                 // no-op at start
            Assert.AreEqual(0, model.CurrentPage);
            model.TurnNext();
            Assert.IsTrue(model.CanTurnPrev);
            Assert.IsFalse(model.CanTurnNext);
            model.TurnNext();                 // no-op at end
            Assert.AreEqual(1, model.CurrentPage);
        }

        [Test]
        public void Select_IgnoresItemsNotInCatalog()
        {
            var model = new MenuSelectionModel();
            var list = Catalog(3);
            model.SetCatalog(list, 5);
            var stranger = ItemDefinition.Create("x", "X", "", 5, ItemEffectKind.PayoutMultiplier, 0f);
            model.Select(stranger);
            Assert.IsNull(model.Selected);
            model.Select(list[1]);
            Assert.AreSame(list[1], model.Selected);
        }

        [Test]
        public void EvaluateBuy_NoSelection_NotBuyableEmptyLabel()
        {
            var model = new MenuSelectionModel();
            model.SetCatalog(Catalog(3), 5);
            var state = model.EvaluateBuy(1000, false);
            Assert.IsFalse(state.CanBuy);
            Assert.AreEqual("", state.Label);
        }

        [Test]
        public void EvaluateBuy_DeskFull_BeatsAffordability()
        {
            var model = new MenuSelectionModel();
            var list = Catalog(3);
            model.SetCatalog(list, 5);
            model.Select(list[0]); // cost 10
            var state = model.EvaluateBuy(1000, deskFull: true);
            Assert.IsFalse(state.CanBuy);
            Assert.AreEqual("Desk full", state.Label);
        }

        [Test]
        public void EvaluateBuy_Unaffordable_ShowsNeed()
        {
            var model = new MenuSelectionModel();
            var list = Catalog(3);
            model.SetCatalog(list, 5);
            model.Select(list[2]); // cost 30
            var state = model.EvaluateBuy(20, deskFull: false);
            Assert.IsFalse(state.CanBuy);
            Assert.AreEqual("Need $30", state.Label);
        }

        [Test]
        public void EvaluateBuy_Affordable_ShowsBuyLabel()
        {
            var model = new MenuSelectionModel();
            var list = Catalog(3);
            model.SetCatalog(list, 5);
            model.Select(list[1]); // cost 20
            var state = model.EvaluateBuy(50, deskFull: false);
            Assert.IsTrue(state.CanBuy);
            Assert.AreEqual("Buy — $20", state.Label);
        }
    }
}
```

> Note: `ItemDefinition.Create(...)` is the existing runtime factory used elsewhere (e.g. `ShopCatalog`). Confirm its exact signature in `Assets/Scripts/Items/ItemDefinition.cs` and match the argument order; if it differs, adjust the test helper accordingly (this does not change the model code).

- [ ] **Step 2: Run tests to verify they fail**

Run the EditMode test command. Expected: FAIL — `MenuSelectionModel`/`BuyState` undefined.

- [ ] **Step 3: Write minimal implementation**

```csharp
// Assets/Scripts/UI/MenuSelectionModel.cs
using System.Collections.Generic;
using Meniscus.Items;
using UnityEngine;

namespace Meniscus.UI
{
    /// <summary>Whether the selected order can be bought, and the label the Buy button should show.</summary>
    public readonly struct BuyState
    {
        public BuyState(bool canBuy, string label)
        {
            CanBuy = canBuy;
            Label = label;
        }

        public bool CanBuy { get; }
        public string Label { get; }
    }

    /// <summary>
    /// View-model for the book menu: holds the catalog split into pages, the player's current
    /// selection, and computes the Buy button state. Pure (no scene objects) so it is unit-tested.
    /// </summary>
    public class MenuSelectionModel
    {
        readonly List<ItemDefinition> catalog = new();

        public int ItemsPerPage { get; private set; } = 1;
        public int CurrentPage { get; private set; }
        public ItemDefinition Selected { get; private set; }

        public int Count => catalog.Count;
        public int PageCount => MenuLayout.PageCount(catalog.Count, ItemsPerPage);
        public bool CanTurnPrev => CurrentPage > 0;
        public bool CanTurnNext => CurrentPage < PageCount - 1;

        public void SetCatalog(IReadOnlyList<ItemDefinition> items, int itemsPerPage)
        {
            catalog.Clear();

            if (items != null)
                foreach (var item in items)
                    if (item != null)
                        catalog.Add(item);

            ItemsPerPage = Mathf.Max(1, itemsPerPage);
            CurrentPage = 0;
            Selected = null;
        }

        public IReadOnlyList<ItemDefinition> CurrentPageItems()
        {
            var start = CurrentPage * ItemsPerPage;
            var page = new List<ItemDefinition>();

            for (var i = start; i < start + ItemsPerPage && i < catalog.Count; i++)
                page.Add(catalog[i]);

            return page;
        }

        public void TurnPrev()
        {
            if (CanTurnPrev)
                CurrentPage--;
        }

        public void TurnNext()
        {
            if (CanTurnNext)
                CurrentPage++;
        }

        public void Select(ItemDefinition item)
        {
            if (item != null && catalog.Contains(item))
                Selected = item;
        }

        public BuyState EvaluateBuy(int bankedCash, bool deskFull)
        {
            if (Selected == null)
                return new BuyState(false, "");

            if (deskFull)
                return new BuyState(false, "Desk full");

            if (bankedCash < Selected.Cost)
                return new BuyState(false, $"Need ${Selected.Cost}");

            return new BuyState(true, $"Buy — ${Selected.Cost}");
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run the EditMode test command. Expected: PASS (8 tests).

- [ ] **Step 5: Offline compile-check + commit**

```bash
git add Assets/Scripts/UI/MenuSelectionModel.cs Assets/Tests/EditMode/MenuSelectionModelTests.cs
git commit -m "feat: MenuSelectionModel selection + pagination + buy-state view-model"
```

---

### Task 3: `ShopManager` read-only queries for the book

**Files:**
- Modify: `Assets/Scripts/UI/ShopManager.cs` (add queries near the existing public surface, ~line 26-99)
- Test: `Assets/Tests/EditMode/ShopManagerTests.cs` (add cases)

**Interfaces:**
- Consumes: existing `economyManager` (`PlayerTotalBankedCash`), `playerInventory` (`Contents()`, `IsFull`).
- Produces (new public members on `ShopManager`):
  - `int BankedCash { get; }` → `economyManager?.PlayerTotalBankedCash ?? 0`
  - `bool IsDeskFull { get; }` → `playerInventory != null && playerInventory.IsFull`
  - `int OwnedCount(ItemDefinition item)` → count of that item in inventory, else 0

- [ ] **Step 1: Write the failing test**

Append to `ShopManagerTests` (reuse its existing fixture/`Configure` setup — match how the file already wires `EconomyManager`/`PlayerInventory`; read the top of the file first):

```csharp
        [Test]
        public void BankedCash_MirrorsEconomyManager()
        {
            // Arrange via the fixture's existing Configure(...) helper, then bank some cash.
            economy.AddBankedCashForTest(120); // use the same helper the existing tests use to set cash
            Assert.AreEqual(120, shop.BankedCash);
        }

        [Test]
        public void IsDeskFull_TrueWhenInventoryFull()
        {
            for (var i = 0; i < GameConstants.DeskCapacity; i++)
                inventory.Grant(ItemDefinition.Create($"f_{i}", $"F{i}", "", 1, ItemEffectKind.PayoutMultiplier, 0f));
            Assert.IsTrue(shop.IsDeskFull);
        }

        [Test]
        public void OwnedCount_ReflectsGrants()
        {
            var item = ItemDefinition.Create("steady_hand", "Steady Hand", "", 50, ItemEffectKind.PayoutMultiplier, 0f);
            Assert.AreEqual(0, shop.OwnedCount(item));
            inventory.Grant(item);
            inventory.Grant(item);
            Assert.AreEqual(2, shop.OwnedCount(item));
        }
```

> Match the field names (`shop`, `economy`, `inventory`) and cash-setup helper to whatever `ShopManagerTests` already uses; the recent commit `7de7f31` pinned its deps, so the fixture already constructs these. If no cash helper exists, set banked cash through the same path the existing affordability tests use.

- [ ] **Step 2: Run tests to verify they fail**

Run the EditMode test command. Expected: FAIL — `BankedCash`/`IsDeskFull`/`OwnedCount` not defined on `ShopManager`.

- [ ] **Step 3: Write minimal implementation**

Add to `ShopManager` (after `Catalog` property, before `ShowShop`):

```csharp
        /// <summary>Banked cash the player can spend right now (0 if economy is unwired).</summary>
        public int BankedCash => economyManager != null ? economyManager.PlayerTotalBankedCash : 0;

        /// <summary>True when the desk cannot hold any more items.</summary>
        public bool IsDeskFull => playerInventory != null && playerInventory.IsFull;

        /// <summary>How many of <paramref name="item"/> the player already owns.</summary>
        public int OwnedCount(ItemDefinition item)
        {
            if (item == null || playerInventory == null)
                return 0;

            foreach (var stack in playerInventory.Contents())
                if (stack.Item == item)
                    return stack.Count;

            return 0;
        }
```

If `ResolveReferences()` is needed to populate `economyManager`/`playerInventory` lazily, call it at the top of each getter as `TryBuyItem` does, to stay consistent.

- [ ] **Step 4: Run tests to verify they pass**

Run the EditMode test command. Expected: PASS (existing ShopManager tests + 3 new).

- [ ] **Step 5: Offline compile-check + commit**

```bash
git add Assets/Scripts/UI/ShopManager.cs Assets/Tests/EditMode/ShopManagerTests.cs
git commit -m "feat: ShopManager read-only queries (banked cash, desk-full, owned count)"
```

---

### Task 4: `BookShopView` — ticket-left / paginated-list-right presentation with select-to-buy

**Files:**
- Modify: `Assets/Scripts/UI/BookShopView.cs`
  - Add fields near `:73-76` (Menu header): `menuWorldDepth`, `pageTurnSeconds` (the latter used in Task 5 but declared here).
  - Replace `BuildMenuCanvas` (`:299-400`) and adjust the scale/interactable code at `:216-236`.
- Reference (read, do not change): `Assets/Scripts/UI/RuntimeUiFactory.cs:42-153` (`CreateWorldCanvas`, `CreateImage`, `CreateText`, `CreateButton` signatures).

**Interfaces:**
- Consumes: `MenuSelectionModel`, `BuyState` (Task 2); `MenuLayout` (Task 1); `ShopManager.BankedCash/IsDeskFull/OwnedCount/TryBuyItem/FinishOrdering` (Task 3 + existing).
- Produces: no new public surface; this is internal presentation. Keeps the existing `previewMode`/`menuGroup` open/close behavior intact.

**Design notes for the implementer:**
- Add a `MenuSelectionModel model = new()` field. In `BuildMenuCanvas`, compute `itemsPerPage` from the right page's list area and call `model.SetCatalog(catalog, itemsPerPage)`.
- Canvas pixel size is now FIXED (e.g. `pixelWidth = 1040f`, `pixelHeight = 1280f`), not grown by item count. Scale via `menuScaleBase = MenuLayout.ComputeScale(menuWorldWidth, menuWorldDepth, pixelWidth, pixelHeight)`.
- Left page (`leftCenter`): title `Saloon Menu`; below it the **order ticket** — references kept as fields so they refresh: `ticketName`, `ticketDesc`, `ticketCostOwn` (`Text`), plus `buyButton`/`buyLabel`. Below the ticket: `Buy` button then `Finish Drink` button.
- Right page (`rightCenter`): the **list area**. Rebuild it per page in `RebuildRightPage()`. Each row is a `CreateButton` whose `onClick` calls `OnSelect(item)` (NOT buy). Price text + owned badge `✓×N` (only when `OwnedCount > 0`) on the row. Corner arrows `‹`/`›` call `OnTurn(-1)/OnTurn(+1)`; a `Page n / m` text is hidden when `model.PageCount == 1`.
- Keep one dictionary `rowBackgrounds` (`ItemDefinition -> Image`) for the current page so `ApplySelectionVisual()` can set the selected row to a solid bar + show its `▸` pointer, and clear others.
- `OnSelect(item)`: `model.Select(item)` → `RefreshTicket()` + `ApplySelectionVisual()`.
- `OnBuyClicked()`: if `model.Selected != null` and `shopManager.TryBuyItem(model.Selected)` returns true → `RefreshTicket()` + `RefreshOwnedBadges()` (no event subscription needed — the book is the only purchaser during the shop phase).
- `RefreshTicket()`: when `model.Selected == null` show hint `Pick an order from the menu →` and hide Buy; else show name/description, `Cost: $X    Own: N`, and set Buy label/interactable from `model.EvaluateBuy(shopManager.BankedCash, shopManager.IsDeskFull)`.
- Replace the old per-row hover colors: hover `new Color(0.30f,0.17f,0.08f, 0.30f)` (was 0.14), selected bar `new Color(0.30f,0.17f,0.08f, 0.55f)`.
- The `menuGroup.interactable`/`alpha` logic at `:216-236` stays; `previewMode` still disables buying (in preview, rows can still select to read detail, but Buy stays non-interactable).

**Verification (this task has no isolated unit test — it is Canvas/MonoBehaviour assembly; verify by compile + in-engine):**

- [ ] **Step 1: Add fields and rewrite the scale block**

Add under the `[Header("Menu")]` block:

```csharp
        [Tooltip("World depth bound for the printed menu so it stays within the page depth.")]
        [SerializeField] float menuWorldDepth = 0.34f;
        [Tooltip("Seconds for a page-turn animation.")]
        [SerializeField, Min(0.05f)] float pageTurnSeconds = 0.35f;
```

Replace the scale computation (old `menuScaleBase = menuWorldWidth / pixelWidth;`) with:

```csharp
        menuScaleBase = MenuLayout.ComputeScale(menuWorldWidth, menuWorldDepth, pixelWidth, pixelHeight);
        menuCanvasTransform.localScale = Vector3.one * menuScaleBase;
```

- [ ] **Step 2: Rewrite `BuildMenuCanvas` into left-ticket + right-list**

Replace `BuildMenuCanvas` so it: creates the fixed-size canvas; builds the two parchment pages + spine; builds the left ticket (title, `ticketName`, `ticketDesc`, `ticketCostOwn`, `buyButton`/`buyLabel`, `Finish Drink`); computes `itemsPerPage` from the right list area height and `rowHeight`; calls `model.SetCatalog(catalog, itemsPerPage)`; builds the right-page arrows + `Page n / m` text; then calls `RebuildRightPage()` and `RefreshTicket()`. Keep `menuGroup` setup (`alpha=0`, `interactable=false`, `blocksRaycasts=false`) exactly as before.

Add the helper methods `RebuildRightPage()`, `OnSelect(ItemDefinition)`, `OnBuyClicked()`, `OnTurn(int dir)`, `RefreshTicket()`, `RefreshOwnedBadges()`, `ApplySelectionVisual()`. Use the verbatim copy strings from Global Constraints. (`OnTurn` for now just changes the page and calls `RebuildRightPage()`; the animation is added in Task 5.)

- [ ] **Step 3: Offline compile-check**

Compile both assemblies (offline-compile-check memory). Expected: clean exit, no `error CS` lines.

- [ ] **Step 4: In-engine verification**

Open `Assets/Scenes/Saloon.unity`, enter Play, trigger the shop phase. Confirm: the printed menu sits within the page edges (no overflow) with the full 9-item catalog; clicking a row highlights it and fills the left ticket (no purchase happens); the Buy button purchases the selected item and the owned badge `✓×N` appears; Buy disables with `Need $X` / `Desk full` when appropriate; `Finish Drink` still closes the shop. Capture a screenshot.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/UI/BookShopView.cs
git commit -m "feat: book shop ticket-left / paginated-list-right select-to-buy menu"
```

---

### Task 5: Animated page flip on the right page

**Files:**
- Modify: `Assets/Scripts/UI/BookShopView.cs` (turning state + `Update` animation; wire `OnTurn` to start a flip)
- Modify: `Assets/Scripts/UI/BookShopBuilder.cs` (add a turnable single-page sheet builder)
- Test: `Assets/Tests/EditMode/MenuLayoutTests.cs` or a new small test for the flip-progress midpoint helper (pure)

**Interfaces:**
- Consumes: `pageTurnSeconds` (Task 4), `model.TurnPrev/TurnNext`, `RebuildRightPage()` (Task 4), `hingePivot`/page transforms.
- Produces:
  - `static bool MenuLayout.CrossedMidpoint(float prevT, float t)` → true when `prevT < 0.5f <= t` (drives the content swap exactly once per flip).
  - In `BookShopBuilder`: `static Transform BuildTurningSheet(Transform parent, BookPropParams p)` returning a thin page-sized sheet pivoted at the spine (X = 0), initially inactive.

- [ ] **Step 1: Write the failing test (pure midpoint helper)**

```csharp
        [Test]
        public void CrossedMidpoint_TrueOnlyWhenPassingHalf()
        {
            Assert.IsFalse(MenuLayout.CrossedMidpoint(0.1f, 0.4f));
            Assert.IsTrue(MenuLayout.CrossedMidpoint(0.4f, 0.5f));
            Assert.IsTrue(MenuLayout.CrossedMidpoint(0.49f, 0.8f));
            Assert.IsFalse(MenuLayout.CrossedMidpoint(0.6f, 0.9f));
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run the EditMode test command. Expected: FAIL — `CrossedMidpoint` undefined.

- [ ] **Step 3: Implement the midpoint helper**

Add to `MenuLayout`:

```csharp
        /// <summary>True when an animation parameter steps from before to at-or-after the halfway point.</summary>
        public static bool CrossedMidpoint(float prevT, float t) => prevT < 0.5f && t >= 0.5f;
```

- [ ] **Step 4: Run test to verify it passes**

Run the EditMode test command. Expected: PASS.

- [ ] **Step 5: Implement the turning sheet + animation**

In `BookShopBuilder`, add `BuildTurningSheet(...)` — a single page-sized thin cube parented under a pivot at the spine (local X = 0), paper-coloured, returned inactive.

In `BookShopView`: add turning state `bool isTurning; float turnT; float prevTurnT; int pendingDir;`. `OnTurn(int dir)` ignores input while `isTurning`, checks `model.CanTurnPrev/Next`, then starts the flip (`isTurning = true; turnT = 0; prevTurnT = 0; pendingDir = dir;` activate sheet). In `Update`, while `isTurning`: advance `turnT += Time.deltaTime / pageTurnSeconds`, rotate the sheet from its start angle toward the opposite side via the same easing used for the cover open; if `MenuLayout.CrossedMidpoint(prevTurnT, turnT)` then apply `model.TurnPrev()/TurnNext()` and `RebuildRightPage()`; set `prevTurnT = turnT`; when `turnT >= 1` finish (deactivate sheet, `isTurning = false`). Arrows' interactable should reflect `model.CanTurnPrev/Next` after each rebuild.

- [ ] **Step 6: Offline compile-check**

Compile both assemblies. Expected: clean, no `error CS`.

- [ ] **Step 7: In-engine verification**

Play the Saloon scene, open the shop with a catalog larger than one page. Confirm: `›`/`‹` flip a page sheet across the spine; the right list swaps to the next/prev page mid-flip; arrows disable at the ends; rapid clicks during a flip are ignored; selection made on one page still shows in the ticket after turning. Capture a screenshot.

- [ ] **Step 8: Commit**

```bash
git add Assets/Scripts/UI/BookShopView.cs Assets/Scripts/UI/BookShopBuilder.cs Assets/Tests/EditMode/MenuLayoutTests.cs
git commit -m "feat: animated page-flip with mid-flip content swap for book shop"
```

---

## Self-Review

**Spec coverage:**
- Auto-fit to page bounds → Task 1 (`ComputeScale`) + Task 4 (wired into canvas). ✓
- Layout: ticket left, list right → Task 4. ✓
- Pagination → Task 1 (`ItemsPerPage`/`PageCount`) + Task 2 (`MenuSelectionModel`) + Task 4 (right page) + Task 5 (turning). ✓
- Select → confirm buy → Task 2 (`Select`, `EvaluateBuy`) + Task 4 (`OnSelect`/`OnBuyClicked`). ✓
- Strong focus + selection visual → Task 4 (hover 0.30 alpha, selected bar 0.55 + `▸`). ✓
- Owned marking `✓×N` → Task 3 (`OwnedCount`) + Task 4 (`RefreshOwnedBadges`). ✓
- Buy disabled reasons `Need $X` / `Desk full` → Task 2 (`EvaluateBuy`) + Task 4 (Buy label/interactable). ✓
- Animated page flip via corner arrows → Task 5 + Task 4 (arrows). ✓
- Out-of-scope respected: no change to `TryBuyItem` logic; turning sheet procedural; fallback overlay untouched. ✓

**Placeholder scan:** No TBD/TODO; every code step shows real code; copy strings are verbatim from Global Constraints. The two "match the existing fixture" notes (Task 2 `ItemDefinition.Create` signature, Task 3 `ShopManagerTests` field/helper names) are explicit verification instructions, not deferred work.

**Type consistency:** `MenuLayout.ComputeScale/ItemsPerPage/PageCount/ClampPage/CrossedMidpoint`, `MenuSelectionModel` members, `BuyState{CanBuy,Label}`, and `ShopManager.BankedCash/IsDeskFull/OwnedCount` are used with the same names/signatures across tasks. `BuildTurningSheet` and the turning-state fields are introduced and consumed only in Task 5.
