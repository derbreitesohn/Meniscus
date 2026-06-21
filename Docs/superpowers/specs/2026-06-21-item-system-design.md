# Item System v1 — Shop Expansion

**Date:** 2026-06-21
**Status:** Approved (design) — pending implementation plan
**Topic:** Expand the existing between-rounds shop with two new effect kinds.

## Context

Meniscus is a risk/betting game (Buckshot Roulette lineage): two players take turns
dropping coins into a glass. Each coin adds overflow risk; whoever makes the glass
overflow on their own drop loses. Three rounds, with a shop between rounds.

A data-driven shop was recently built under `Assets/Scripts/Items/`:

- `ItemDefinition` (ScriptableObject) — pure data: id, name, description, cost, effect
  kind, magnitude, icon. Runtime-constructible via `Create(...)`.
- `ItemEffectKind` — `PayoutMultiplier`, `SafeZoneBonus`, `ForceEnemyCoins`.
- `ItemEffectApplier` — one switch mapping each kind to an existing manager hook.
- `ShopCatalog` — code-built default catalog (used when no authored assets are wired).
- `ShopManager` — catalog-driven; `TryBuyItem` charges banked cash and applies the
  effect immediately on purchase.
- `BookShopView` — diegetic book-on-the-desk presentation that renders the catalog.

This design extends that foundation. It does **not** introduce a held inventory or
click-to-use desk items.

## Decisions locked

1. **Interaction model:** keep the existing *buy-between-rounds → effect auto-applies*
   model. No inventory, no in-turn item use.
2. **Scope:** player-only. The enemy (Taro) does not buy or use items in v1.
3. **Architecture:** extend the existing `Items/` data layer. No new architectural
   pattern; new behavior is added as new `ItemEffectKind` values plus the manager hooks
   they call.

## Consequence of the auto-apply model

The glass resets to 0% at the start of every round (`GlassManager.ResetGlass`). A
shop effect can therefore only *prepare the next round*; there is no in-round moment to
act on. Two originally-proposed items depend on an in-the-moment action and are **out of
scope** for this version:

- **Skip your turn (Selbstblock)** — skipping is a tactical in-turn decision; it does not
  translate to a pre-round buff.
- **Taro drinks the glass (reduce current risk)** — there is no current risk between
  rounds; the glass is already at 0%.

Both remain viable only if a click-to-use desk layer is built later. They are recorded in
"Future / out of scope" below.

## In scope (v1)

### Existing effect kinds — data-only growth

`PayoutMultiplier`, `SafeZoneBonus`, and `ForceEnemyCoins` already work. The catalog
ships five items today (Marked Coin, Loaded Dice, Steady Hand, Iron Grip, Dealer's Debt).
Additional tiers/flavor for these are pure `ItemDefinition` data and need no code.

### Current spill model (post-rewrite, the baseline this builds on)

The spill curve was rewritten in parallel and is now what we build against:

- `GlassManager.CalculateTrueSpillChance(risk, relief)` = `MaxSpillChance * (1 - e^(-load²))`
  where `load = (clamp(risk,0,100) - clamp(relief,0,100)) / (MaxOverflowProbability * 0.5)`.
- `MaxOverflowProbability = 100` (the fill meter), `MaxSpillChance = 50` (the ceiling the
  curve asymptotes toward but never reaches), `SpillSafeZoneThreshold = 0` (no grace
  period — even early pours carry a small chance).
- **`relief` is a subtractive discount on the fill**, not a threshold floor. The player's
  `SafeZoneBonus` (Steady Hand / Iron Grip) raises relief; `CurrentSafeZoneThreshold`
  (kept for back-compat) is the player's current relief = `SpillSafeZoneThreshold +
  activeSafeZoneBonus`.
- `DropCoins(coins, actor)` currently resolves spill via
  `CalculateCurrentTrueSpillChance(currentOverflowProbability)`, which uses the **player's**
  relief regardless of who drops. A deterministic `SpillRollProvider` seam already exists
  for tests.

### New effect kind: `EnemySafeZonePenalty` — "Round for the Dealer" (Beer)

Makes Taro overflow more easily on his own drops. In the relief model this is a
**negative relief** applied only to the enemy: it *adds* to the enemy's effective fill,
pushing him up the spill curve. This is the offensive mirror of `SafeZoneBonus`.

This is the only change requiring real plumbing: `DropCoins` uses one relief (the
player's) regardless of who drops. We make the relief **actor-aware**.

**`GlassManager` changes:**

- Add `pendingNextRoundEnemySafeZonePenalty` / `activeEnemySafeZonePenalty` fields,
  mirroring the existing pending/active bonus pattern.
- Add `QueueNextRoundEnemySafeZonePenalty(float penalty)` — clamps `penalty >= 0` and
  takes `Mathf.Max` against any pending value (matches existing "strongest wins, no
  stacking" behavior of `QueueNextRoundSafeZoneBonus`).
- In `ResetGlass`, promote pending → active and clear pending (same as the existing
  bonus).
- Add `GetSafeZoneRelief(TurnActor actor)`:
  - **Player:** `+activeSafeZoneBonus` (i.e. `CurrentSafeZoneThreshold`; unchanged).
  - **Enemy:** `-activeEnemySafeZonePenalty` (negative relief ⇒ higher effective fill).
- `DropCoins(coins, actor)` resolves spill via
  `CalculateTrueSpillChance(currentOverflowProbability, GetSafeZoneRelief(actor))`.
- Relax the lower clamp in `CalculateTrueSpillChance(risk, relief)` from `[0, Max]` to
  `[-Max, Max]` so a negative relief can raise the chance. Positive-relief callers and the
  existing tests are unaffected (they pass relief ≥ 0).

**Intentional behavior refinement:** today the player's `activeSafeZoneBonus` (Steady Hand
/ Iron Grip) leaks to the enemy because `DropCoins` uses the player's relief for every
drop. Once relief is actor-aware, the player's bonus applies to the player only (the enemy
gets `-activeEnemySafeZonePenalty`, default 0). This slightly favors the player and is the
correct reading of those items. Called out so it can be vetoed in review.

**Drunk-but-reckless rule:** the enemy AI must *not* play more cautiously to compensate
for its narrowed safe zone — that would cancel the item. The AI's coin-count decision
(`EnemyAI` reads `GlassManager.CurrentTrueSpillChance`) keeps using the un-penalized
player relief, so Taro pours as if sober; only the overflow resolution inside
`DropCoins(coins, Enemy)` uses the penalized enemy relief. Net effect: he takes the same
risks but busts more often. Concretely, `CurrentTrueSpillChance` is left unchanged;
`DropCoins` computes the actor-specific spill chance locally via `GetSafeZoneRelief`.

**Catalog entry:** `Round for the Dealer`, cost **55**, `EnemySafeZonePenalty`,
magnitude **12** (adds 12 to the enemy's effective fill next round — e.g. at fill 50 his
spill chance rises from ≈31.6% to ≈39.3%; effect is largest mid-game and tapers near the
50% ceiling). Magnitude is tunable.

### New effect kind: `RevealTrueOdds` — "Bartender's Spectacles" (Binoculars)

For the next round, the HUD shows the **true spill chance** (the relief-curve real
probability) in addition to the raw fill %. Today the HUD shows only the fill %
(`CurrentOverflowProbability`); the true odds (`CurrentTrueSpillChance`) are hidden, so
this is genuine information with zero balance risk.

**`GlassManager` changes:**

- Add `pendingNextRoundRevealTrueOdds` / `activeRevealTrueOdds` (bool), promoted in
  `ResetGlass` like the numeric buffs. Lasts exactly the next round.
- Add `QueueNextRoundRevealTrueOdds()` (sets pending true).
- Add public `bool TrueOddsRevealed => activeRevealTrueOdds`.

**`SaloonHudController` changes:**

- When `glassManager.TrueOddsRevealed` is true, append the true spill chance to the
  status line, e.g. `Spill 23%`, using the existing `CurrentTrueSpillChance`.
- `BuildStatusLine` today has the signature `(GameState, int currentRound, int
  totalRounds, float totalRiskWeight, int roundEarnings, int bankedCash, int
  playerCoinCount, int enemyCoinCount)`. Add two trailing parameters — `bool
  revealTrueOdds = false`, `float trueSpillChance = 0f` — so it stays a pure, testable
  function and existing callers/tests are source-compatible. `Refresh()` passes
  `glassManager.TrueOddsRevealed` and `glassManager.CurrentTrueSpillChance`.

**Catalog entry:** `Bartender's Spectacles`, cost **30**, `RevealTrueOdds`, magnitude
unused (0).

### `ItemEffectApplier` additions

Two new `case` arms route to the new hooks (both target `GlassManager`), following the
existing null-guard + `WarnMissing` pattern:

```csharp
case ItemEffectKind.EnemySafeZonePenalty:
    if (glass != null) glass.QueueNextRoundEnemySafeZonePenalty(item.Magnitude);
    else WarnMissing(item, nameof(GlassManager));
    break;

case ItemEffectKind.RevealTrueOdds:
    if (glass != null) glass.QueueNextRoundRevealTrueOdds();
    else WarnMissing(item, nameof(GlassManager));
    break;
```

## Files touched

- `Assets/Scripts/Items/ItemEffectKind.cs` — add two enum values.
- `Assets/Scripts/Items/ItemEffectApplier.cs` — add two `case` arms.
- `Assets/Scripts/Items/ShopCatalog.cs` — add the two new items.
- `Assets/Scripts/Core/GlassManager.cs` — per-actor relief (`GetSafeZoneRelief`),
  enemy-penalty queue, reveal flag/queue, relaxed relief clamp, `DropCoins` uses the
  actor relief.
- `Assets/Scripts/UI/SaloonHudController.cs` — show true odds when revealed.
- `Assets/Tests/EditMode/GlassManagerTests.cs` — add tests for the enemy penalty and
  reveal flag (existing file; follow its conventions).
- `Assets/Tests/EditMode/` — a small new test file for `SaloonHudController.BuildStatusLine`
  reveal suffix if one does not already exist.

No change needed to `ItemDefinition`, `ShopManager`, or `BookShopView`: they are
data-driven and already render and purchase any catalog entry.

## Data flow

1. Shop phase opens → `BookShopView` renders `ShopManager.Catalog`.
2. Player clicks an order → `ShopManager.TryBuyItem(item)` charges banked cash, then
   `ItemEffectApplier.Apply(item, economy, glass, game)`.
3. Apply routes by `ItemEffectKind` to a `Queue…` hook, which stores a *pending*
   next-round value.
4. Next `StartRound` → `GlassManager.ResetGlass` promotes pending → active.
5. During the round, `DropCoins(coins, actor)` resolves spill via the actor-specific
   relief (`GetSafeZoneRelief(actor)`); `SaloonHudController` reads `TrueOddsRevealed`.

## Edge cases / error handling

- Null item or missing manager: `ItemEffectApplier` already logs a warning and no-ops.
  New arms follow the same pattern.
- Negative magnitude: `Queue…` hooks clamp to `>= 0`.
- Enemy relief sign: the penalty yields a *negative* relief; the relaxed clamp
  `[-Max, Max]` lets it raise the chance. Effective fill is still bounded because `risk`
  is clamped to `[0, 100]` and the curve asymptotes to `MaxSpillChance`.
- Repeated purchases in one shop phase: numeric effects take `Mathf.Max` (strongest
  wins, no stacking); reveal is idempotent.
- Effects last one round only (promoted on `ResetGlass`, pending cleared), consistent
  with the existing buffs.

## Testing

EditMode (NUnit) tests run against the existing `Meniscus.Tests.EditMode` assembly,
using the `SpillRollProvider` seam for deterministic drops. Follow the patterns in the
current `GlassManagerTests.cs` / `EconomyManagerTests.cs` (GameObject host in `[SetUp]`,
`Object.DestroyImmediate` in `[TearDown]`).

- `GlassManager.CalculateTrueSpillChance(risk, relief)` with a *negative* relief yields a
  higher chance than relief 0 for the same risk (documents the relaxed clamp).
- `GetSafeZoneRelief(actor)`: player = `+activeSafeZoneBonus`, enemy =
  `-activeEnemySafeZonePenalty`, after `ResetGlass` promotes a queued penalty.
- `DropCoins(coins, Enemy)` with a queued penalty produces a higher `TrueSpillChance`
  than the same fill for the Player; a `SpillRollProvider` returning a mid value can spill
  the drunk enemy while leaving the sober player safe.
- The enemy penalty expires after one extra `ResetGlass`.
- `SaloonHudController.BuildStatusLine(...)` with `revealTrueOdds` on/off produces the
  expected `Spill NN%` suffix.
- **PlayMode / manual verification:**
  - Buy "Round for the Dealer", confirm the enemy overflows more readily next round and
    the player's own odds are unchanged.
  - Buy "Bartender's Spectacles", confirm the HUD shows the spill % next round and
    reverts the round after.
  - Confirm a player `SafeZoneBonus` purchase no longer benefits the enemy's drops.

## Future / out of scope

- **Click-to-use desk layer** (held `PlayerInventory`, grant-on-buy instead of
  apply-on-buy, on-desk item bar usable during `PlayerTurn`). Required to revive:
  - **Skip your turn (Selbstblock)**
  - **Taro drinks the glass / 50-50 gamble**
- **Enemy items** (Taro buys/uses items) — required before:
  - **Whistle / item-blocker** and **Steal item**
- **`CoinSizeBias`** ("Crooked Scales") — skew next round's coin-size roll.
- **`GambleBuff`** — high-variance randomized buff (a shop reframing of Taro's 50-50).
