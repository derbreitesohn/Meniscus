# Book Shop Menu — Fit-to-Page, Select-to-Buy, and Page Turning

**Date:** 2026-06-22
**Status:** Design — awaiting review
**Area:** `Assets/Scripts/UI/BookShopView.cs` (primary), `RuntimeUiFactory.cs`, `BookShopBuilder.cs`

## Problem

The diegetic book shop has two defects:

1. **The printed menu is bigger than the book.** The menu canvas height is
   `120 + itemCount*96 + 150` px, scaled to world by `menuWorldWidth / 1040`.
   With the 9-item catalog this is ~1134 px → ~0.61 m tall, but a page is only
   `pageDepth = 0.38 m` deep. The list overflows the top and bottom edges of the
   pages. (Width fits: 0.56 m menu inside a 0.64 m book.)

2. **No clear indication of what you've chosen.** Rows are plain buttons with a
   faint transient hover tint (14% alpha) and clicking buys instantly. There is
   no persistent focus, no selected state, and no marking of items already owned.

## Goals

- The printed menu always stays within the page rectangle, regardless of item count.
- The player's current choice is unmistakable (persistent selection highlight).
- A deliberate select → confirm purchase flow replaces instant-buy.
- Items already owned are marked with their count.
- The catalog paginates across page spreads with an animated page flip, turned via
  corner arrows.

## Layout — repurpose the spread

The spread is divided into two fixed roles:

```
┌─────────────────────────┬─────────────────────────┐
│  Saloon Menu            │ ‹                     ›  │  ← page-turn arrows
│                         │                          │
│  ── Marked Coin ──      │ ▸ Marked Coin      $35  │  ← focused row (solid bar + ▸)
│  Doubles your payout    │   Loaded Dice      $70  │
│  on the next win.       │   Steady Hand  ✓×1 $50  │  ← owned badge
│                         │   Iron Grip        $90  │
│  Cost: $35    Own: 0    │   Dealer's Debt    $60  │
│                         │                          │
│   [   Buy — $35   ]     │        Page 1 / 2        │  ← page indicator
│   [  Finish Drink  ]    │                          │
└─────────────────────────┴─────────────────────────┘
```

- **Left page — the order ticket (fixed).** Small "Saloon Menu" title, then the
  selected item's name, description, cost, and owned count, then the **Buy** button
  and **Finish Drink** button. When nothing is selected it shows the hint
  *"Pick an order from the menu →"* and the Buy button is hidden/disabled.
- **Right page — the order list (paginated).** Shows one page's worth of item rows,
  corner page-turn arrows (`‹ ›`), and a `Page N / M` indicator.

## Component behaviour

### Auto-fit to page bounds
- New serialized field `menuWorldDepth` (default `0.34`, just inside `pageDepth`).
- `menuScaleBase = min(menuWorldWidth / pixelWidth, menuWorldDepth / pixelHeight)`.
  This clamps the canvas to fit within **both** the width and depth of the page
  rectangle, so the menu can never overflow again.
- `pixelHeight` becomes a fixed design height (no longer grows with item count),
  because the list is now paginated rather than stacked unbounded.

### Pagination
- `itemsPerPage = floor(rightPageListDepthPx / rowHeight)` computed from the right
  page's available depth at a readable row height (target ≈ 5 rows). This keeps rows
  comfortably sized rather than shrinking the whole canvas.
- `pageCount = ceil(catalog.Count / itemsPerPage)`.
- `currentPage` state (0-based). The right-page list rebuilds for the current page;
  the left ticket and footer persist across turns.
- Arrows disable at the ends (`‹` on page 0, `›` on last page). A `Page N / M`
  indicator is hidden when there is only one page.

### Selection (select → confirm)
- Clicking a row no longer purchases. It sets `selectedItem` and:
  - applies a **persistent selected visual** to that row — a solid background bar
    plus a `▸` pointer glyph — managed manually (swap the row image colour / toggle
    the pointer), since Unity's ColorBlock `normal/highlighted` cannot hold a chosen
    state across pointer-exit.
  - refreshes the left ticket with the item's name, description, `Cost: $X`, and
    `Own: N`.
- Selection survives page turns: if the selected item is on another page, its row
  shows selected when that page is shown; the left ticket always reflects `selectedItem`.
- **Buy button** is the confirm step: calls the existing `ShopManager.TryBuyItem`.
  - Label reads `Buy — $X`.
  - Disabled with a reason when unaffordable (`Need $X`) or the desk is full
    (`Desk full`), derived from `EconomyManager` balance and `PlayerInventory.IsFull`.

### Strong focus highlight
- Raise the hover tint from 14% to a clearly visible level (≈ 28–35% alpha) so
  hovered rows are obvious even before selection.
- The selected bar is more saturated/opaque than hover so selection reads as
  distinct from mere hover.

### Owned marking
- Each row shows an owned badge `✓×N` when `PlayerInventory` count for that item ≥ 1
  (read via `Contents()` / `Has`).
- `BookShopView` subscribes to `PlayerInventory.Changed` and refreshes the current
  page's badges and the left ticket's `Own:` line after each purchase.
  Unsubscribe on disable/destroy.

### Animated page flip
- Triggered by the corner arrows (`‹` prev, `›` next).
- A page-shaped sheet (a thin cube like the existing pages, paper-coloured, built via
  `BookShopBuilder`) pivots on the **spine axis** (X = 0): on *next* it sweeps from
  the right side (0°) across to the left (≈180°); on *prev*, the reverse.
- The **right-page list content swaps at the flip midpoint** (≈90°, when the turning
  sheet hides the page), so the player sees the next page's items as the sheet settles.
  The turning sheet itself carries no live UI — it only sells the motion.
- Reuse the existing easing approach from the cover open animation (`openSeconds`-style
  smooth interpolation). New field `pageTurnSeconds` (default ≈ 0.35).
- Input is locked during a turn (ignore further arrow clicks until it completes).

## State added to `BookShopView`

| Field | Purpose |
|---|---|
| `menuWorldDepth` (serialized) | Depth bound for auto-fit |
| `pageTurnSeconds` (serialized) | Flip duration |
| `selectedItem` | Current chosen item (null = none) |
| `currentPage`, `pageCount`, `itemsPerPage` | Pagination |
| row-button registry for the current page | Apply/clear selected + owned visuals |
| left-ticket text refs + Buy button ref | Refresh on selection / inventory change |
| turning state (active, t, direction) | Drive flip animation in `Update` |

## Out of scope

- No change to `ShopManager.TryBuyItem`, economy, or inventory logic — the book only
  changes presentation and the moment of purchase (Buy button vs. row click).
- No new authored assets; the turning sheet is built procedurally like the existing pages.
- The fallback `shopCanvas` overlay path is unchanged.

## Testing

- **Fit:** with the full 9-item catalog (and an inflated 20-item catalog), the menu
  canvas world bounds stay within `pageWidth*2 × pageDepth`. Assert
  `menuScaleBase * pixelHeight <= menuWorldDepth` and width bound.
- **Pagination:** `pageCount` and `itemsPerPage` correct for 1, 5, 6, 9, 20 items;
  arrows disable at ends; indicator hidden for single page.
- **Selection:** clicking a row sets `selectedItem` and does NOT purchase; Buy button
  purchases the selected item; Buy disabled when unaffordable / desk full.
- **Owned:** after a successful buy, the row badge and ticket `Own:` reflect the new
  count via the `PlayerInventory.Changed` subscription.
- Logic (pagination math, selection state, buy-enable conditions) is unit-testable in
  EditMode without the 3D prop; animation is verified in-scene.
