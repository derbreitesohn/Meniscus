# Desk Item Boxes — Design

**Date:** 2026-06-22
**Branch:** feature/runtime-object-authoring
**Status:** Approved for planning

## Problem

Items bought from the shop are added silently to `PlayerInventory` and used later
via an on-screen `DeskItemBar` button UI during the player's turn. The shop is
otherwise fully diegetic (a physical book on the desk). The item UI breaks that
fiction.

We want purchased items to be **physical clickable objects on the desk** that the
player clicks to select, then confirms to use. Boxes (Unity cube primitives) are
the placeholder visual for now.

## Decisions (from brainstorming)

- **Replace the bar.** `DeskItemBar` is removed entirely; desk boxes are the only
  item presentation.
- **Interaction:** click a box to select, then confirm to use.
- **Confirm gesture:** selecting a box raises it and shows small diegetic
  **Use / Cancel** targets next to it; Use confirms.
- **Stacks:** one box per stack with a `×N` count label. Using decrements; the box
  disappears at 0.
- **Visibility:** boxes are **always visible** on the desk so the player can see
  what they own, but **Use only works during `PlayerTurn`** (matching when the old
  bar was active). Using off-turn gives a brief "not your turn" cue and is a no-op.
- **Placement:** a row along the near (player) edge of the desk, left→right,
  auto-positioned against the table bounds (with an optional hand-placed anchor).

## Non-goals

- No targeting/aiming of effects — every current effect is non-spatial
  (`PayoutMultiplier`, `SafeZoneBonus`, force-enemy-coins, skip-turn). Use applies
  the effect globally via the existing path.
- No drag-and-drop / physics props.
- No change to the purchase flow, the effect system, or `ItemDefinition` data.
- No final art — boxes are placeholders.

## Architecture

The change is **presentation only**. The purchase → inventory → use → effect path
is untouched:

```
purchase (unchanged) → PlayerInventory.Grant → Changed event → tray.Rebuild (spawn/update box)
click box → tray selects → Use tile → GameManager.TryUseItem → PlayerInventory.TryConsume
          → ItemEffectApplier (unchanged) → Changed event → tray.Rebuild (decrement/remove box)
```

### Reused, unchanged
- `PlayerInventory` — `Changed` event, `Contents()`, `TotalCount`, `IsFull`.
- `GameManager` — `StateChanged`, `CurrentState`, `TryUseItem(ItemDefinition)`,
  `Inventory`.
- `ItemDefinition`, `ItemEffectApplier`, `ItemEffectKind`, `ShopManager`,
  `BookShopView`.

### New components

**`DeskItemBox`** (MonoBehaviour on a placeholder cube)
- Fields: the `ItemDefinition` it represents, the current count, a back-reference
  to the owning `DeskItemTray`.
- A camera-facing 3D `TextMesh` label showing `NAME ×N`.
- A `BoxCollider`; `OnMouseDown` → `tray.OnBoxClicked(this)` (same per-object click
  approach the book already uses via `BookClickTarget`, so `PlayerController` is not
  touched).
- Visual states driven by the tray: **idle** (resting) and **raised** (selected —
  lifts a few mm and/or tints).
- When raised, reveals two child **Use** and **Cancel** tiles (small cubes +
  `TextMesh`, each with its own collider). Each tile relays its `OnMouseDown` to the
  box, which forwards to `tray.OnUseClicked(this)` / `tray.OnCancelClicked(this)`.
  Tiles are hidden when the box is idle.

**`DeskItemTray`** (replaces `DeskItemBar`; same lifecycle & wiring)
- Serialized refs: `GameManager`, `PlayerInventory`, optional `deskItemsAnchor`
  `Transform`. Resolves missing refs the same way `DeskItemBar` did
  (`FindAnyObjectByType`, `gameManager.Inventory`).
- Subscribes to `inventory.Changed` and `gameManager.StateChanged` in
  `OnEnable`/`Configure`; unsubscribes in `OnDisable`. Identical pattern to
  `DeskItemBar`.
- `Rebuild()`: reconcile the live box set to `inventory.Contents()` — one box per
  stack; create boxes for new stacks, update the `×N` label for changed counts,
  destroy boxes for emptied stacks. Lay boxes out left→right along the player's edge
  (see Positioning). Boxes remain visible in **every** game state.
- Owns the **single** selected box (selection invariant: at most one).
  - `OnBoxClicked(box)`: if already selected, no-op (Use/Cancel handle the next
    step); otherwise select it and deselect any previously selected box.
  - `OnUseClicked(box)`: if `CurrentState == PlayerTurn`, call
    `gameManager.TryUseItem(box.Item)` then deselect. Off-turn: trigger a brief
    "not your turn" cue on the box and keep it selected (no consume).
  - `OnCancelClicked(box)`: deselect.
  - Deselect also occurs when another box is clicked or when the game leaves
    `PlayerTurn` (mirrors `PlayerController.OnGameStateChanged` clearing coin
    selection).

**`DeskItemTrayBuilder`** (mirrors `DeskItemBarBuilder` / `BookShopBuilder`)
- Procedurally builds the box primitive, its label, the Use/Cancel tiles, and their
  materials. Pure geometry/visual construction, no game logic.

### Positioning

Three-tier fallback, identical in spirit to `BookShopView`'s book placement:
1. **`deskItemsAnchor`** if assigned — lay the row out from there.
2. Otherwise the tray's **own transform** if it has been meaningfully placed on the
   desk.
3. Otherwise **auto-detect the "Saloon Table"** bounds and position the row along
   the near (player) edge, relative to the desk top.

Row is centered on the anchor point with fixed spacing; spacing/scale accommodates
up to `GameConstants.DeskCapacity` boxes.

## Authoring & removal

- Add an **`Author Desk Item Tray`** menu item to `RuntimeObjectAuthoring`,
  building the tray and wiring `GameManager`/`PlayerInventory` references.
- Add it to **`Author All`** in dependency order, in place of the existing
  `Author Desk Item Bar` step.
- **Remove** `DeskItemBar` and `DeskItemBarBuilder`, their `Author Desk Item Bar`
  menu item, and any scene/test references to them.

## Testing

- The use/effect flow is unchanged and remains covered by existing item-effect and
  `ShopManager` tests.
- New **EditMode** tests for the tray's pure logic:
  - **Reconciliation:** given a sequence of `Contents()` states (add stack,
    increment, decrement, remove), the box set matches one box per stack with the
    correct count label.
  - **Single-selection invariant:** selecting a second box deselects the first; at
    most one box is selected.
  - **Turn gating:** `OnUseClicked` off-turn does not consume; on-turn it calls
    `TryUseItem` exactly once and consumes.
- Interaction plumbing (`OnMouseDown`/colliders) and procedural geometry remain
  untested, consistent with how the book/bar builders are treated. Where needed,
  the selection/reconciliation logic is structured so it can be exercised without a
  live click (e.g. via internal methods the tests call directly).

## Risks

- **Selection vs. existing raycast.** `PlayerController` raycasts on left-click
  during `PlayerTurn` for coins/glass only; box clicks won't match those, so the
  two click paths don't conflict. Confirmed against `PlayerController.HandleClick`.
- **Off-turn click feedback.** Boxes are always visible but only usable on-turn;
  the "not your turn" cue is minimal (placeholder) and can be refined later.
- **Capacity overflow.** Layout must handle up to `DeskCapacity` boxes without
  overlapping; spacing scales to fit.
