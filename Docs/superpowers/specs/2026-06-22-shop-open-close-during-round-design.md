# Open/Close the Shop During the Round

**Date:** 2026-06-22
**Status:** Approved (design)

## Goal

Let the player open and close the diegetic book shop **during a round** by clicking
the book prop on the desk. Opening mid-round shows the catalog **read-only** (browse,
no purchasing); buying stays restricted to the post-round `ShopPhase`.

## Background: most of this already exists

The behavior is already coded in `BookShopView`:

- `OnBookClicked()` (`Assets/Scripts/UI/BookShopView.cs:173`) implements the full state
  machine: closed → click → read-only **preview**; previewing → click → closed;
  shop-phase-open → click ignored (close via "Finish Drink").
- `Update()` already drives a "preview" presentation: catalog visible but
  `menuGroup.interactable = false`, "Finish Drink" hidden, a browse hint shown.
- The book is seated closed on the desk for the whole match
  (`ShopManager.Start()` → `BookShopView.PrepareOnDesk`, `ShopManager.cs:34`).

## Root cause: the click never arrives

`BookClickTarget` (`BookShopView.cs:548`) detects clicks via Unity's legacy
`OnMouseDown` / `OnMouseUpAsButton` messages. The project's input backend is
**Input System Package (New) only** (`ProjectSettings/ProjectSettings.asset` →
`activeInputHandler: 1`). Under that backend those legacy messages **never fire**, so
`BookClickTarget` is dead code and `OnBookClicked()` is unreachable.

Every other 3D click in the game (coins, glass) is handled by `PlayerController` via a
manual `Physics.Raycast` driven by `Mouse.current` (`PlayerController.cs:59-83`). The
book was never migrated to that pattern.

## Approach (chosen: A — self-contained in `BookShopView`)

Migrate the book's click detection to the same new-input raycast pattern the rest of
the game uses, keeping it inside `BookShopView` so the book owns its whole interaction.

Rejected alternatives:

- **B — route through `PlayerController`.** Couples shop logic into the coin/glass
  controller, which deliberately early-returns outside `PlayerTurn`.
- **C — `PhysicsRaycaster` + `IPointerClickHandler`.** Introduces a second input
  mechanism not used anywhere else in this codebase.

## Design

### 1. New-input click detection in `BookShopView`

In `BookShopView.Update()`, after the existing animation work, detect a click and
hit-test the book's own collider:

- If `Mouse.current` exists and `leftButton.wasPressedThisFrame`:
  - Raycast from `ActiveCamera()` through `Mouse.current.position` (the camera the book
    already resolves for its pose). Use a generous distance and let trigger colliders be
    hit (the book collider is `isTrigger = true`; `Physics.queriesHitTriggers` defaults
    to true).
  - If the hit collider belongs to **this** book (resolved via
    `GetComponentInParent<BookClickTarget>()` matching this view's marker), and browsing
    is currently allowed (see gate), call `OnBookClicked()`.

This runs every frame regardless of game state; gating + `OnBookClicked`'s existing
self-guards decide whether a click does anything.

### 2. `BookClickTarget` becomes a marker

`BookClickTarget` is **kept** (the authored scene book carries a serialized instance —
`Assets/Scenes/Saloon.unity:681`,`:9403` — so deleting the class would orphan it into a
missing script). Repurpose it:

- Remove the dead `OnMouseDown` / `OnMouseUpAsButton` handlers and the `Clicked`
  delegate.
- Keep it as a small marker that tags the GameObject carrying the book's click collider,
  optionally retaining `LogDiagnostics`.

Remove the now-unused `clickTarget.Clicked = OnBookClicked` wiring in `BuildProp`
(`BookShopView.cs:280`) and `ResolveAuthoredProp` (`:295`). `BuildProp`/the authoring
tool still **add** the marker so the raycast can identify the book.

### 3. Browse gate ("during the round")

`OnBookClicked()` already ignores clicks while the shop phase has the book open. The
only extra rule: don't let a browse-preview open when there is no active round —
block toggling when `GameManager.CurrentState` is `ShopPhase` or `GameOver`.

Express this as a `Func<bool>` predicate that `ShopManager` supplies to the book (so
`BookShopView` stays free of direct `GameManager` knowledge):

- `ShopManager` sets the predicate when it prepares/opens the book, e.g.
  `() => gameManager == null || (gameManager.CurrentState != GameState.ShopPhase &&
  gameManager.CurrentState != GameState.GameOver)`.
- `BookShopView` checks the predicate before invoking `OnBookClicked()` for a click; a
  null predicate means "allowed" (safe default for tests / standalone use).

Allowed round states therefore include `StartRound`, `PlayerTurn`, `EnemyTurn`,
`Resolution`, and `RestockPhase` — i.e. anytime a round is in progress.

### Interaction with `PlayerController`

No conflict. Both systems raycast independently on the same click: `PlayerController`
acts only when it hits a coin/glass, the book toggles only when the book collider is
hit. The book rests off to the side of the play area
(`deskRestForwardOffset`/`deskRestLateralOffset`), so overlap is unlikely; if a click
does hit the book, `PlayerController` finds no coin/glass and does nothing.

## Testing

- The `OnBookClicked()` transitions are pure state logic and remain unit-testable
  (EditMode), as `ShopManagerTests` / `BookShopBuilderTests` already are. Add coverage
  for the browse gate (preview opens when allowed; blocked under `ShopPhase`/`GameOver`).
- `BookShopBuilderTests.cs:31` asserts `BookClickTarget` exists — **stays valid** (marker
  retained).
- Click-raycast wiring itself is play-mode behavior and is verified by running the app
  (open/close the book during a round; confirm read-only, no buying; confirm it does
  nothing on the end screen).

## Scope / out of scope

In scope: working mid-round open/close via the new input system; the browse gate;
removing the dead legacy click path.

Out of scope: any change to purchasing rules, the shop catalog, the `ShopPhase` flow,
or the book's visual/animation design.
