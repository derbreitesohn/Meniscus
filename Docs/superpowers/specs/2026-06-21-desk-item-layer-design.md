# Desk Item Layer — Click-to-Use Inventory

**Date:** 2026-06-21
**Status:** Approved (design) — pending implementation plan
**Topic:** Replace the auto-apply shop with a held, click-to-use desk inventory.

## Context

Meniscus is a risk/betting game (Buckshot Roulette lineage): the player and the dealer
(Taro) take turns pouring coins into a shared glass. Each coin adds overflow risk; whoever
makes the glass overflow on their own pour loses. Three rounds, with a shop between rounds.

A data-driven shop exists under `Assets/Scripts/Items/`:

- `ItemDefinition` (ScriptableObject) — pure data: id, name, description, cost, effect kind,
  magnitude, icon. Runtime-constructible via `Create(...)`.
- `ItemEffectKind` — `PayoutMultiplier`, `SafeZoneBonus`, `ForceEnemyCoins`,
  `EnemySafeZonePenalty`, `RevealTrueOdds`.
- `ItemEffectApplier` — one switch mapping each kind to a manager hook.
- `ShopCatalog` — code-built default catalog (used when no authored assets are wired).
- `ShopManager` — catalog-driven; `TryBuyItem` charges banked cash and **applies the effect
  immediately on purchase**.
- `BookShopView` — diegetic book-on-the-desk presentation that renders the catalog.

Today, buying an item immediately queues a **passive buff** that auto-applies to the next
round (`GlassManager` / `EconomyManager` / `GameManager` "pending → promote at `ResetGlass`"
hooks). There is no held inventory and no in-turn item use. The earlier item-system v1 design
(`Docs/superpowers/specs/2026-06-21-item-system-design.md`) deliberately locked that model and
listed a "click-to-use desk layer" as out of scope. This design builds exactly that layer and
supersedes the auto-apply interaction model.

### The constraint that makes this work

`GlassManager.ResetGlass()` zeroes the glass at the start of every round, which is why the
between-rounds shop could only *prepare* the next round. But **within a round the glass is
not reset between turns** — it accumulates fill from every pour until someone overflows. So a
held item used *during the player's turn* acts on live state (current fill, current spill
chance). That live mid-round state is what revives "lower the current glass" and "skip your
turn," which had no meaning in the between-rounds model.

## Decisions locked

1. **Interaction model — unified.** Every purchased item becomes a one-shot consumable on the
   player's desk, clicked during the player's turn. The current passive buffs are reworked
   into activatable items. No item auto-applies on purchase.
2. **Turn flow — use freely, then pour.** On the player's turn they may activate any number of
   desk items (no per-turn use limit), then select and drop coins as the single pour that ends
   the turn. `SkipTurn` is the one item that ends the turn *instead* of pouring. The economy
   (each item is a paid one-shot) is the balancing brake.
3. **Lifetime — one-shot, persists until used.** Each purchase is single-use and sits on the
   desk across rounds until activated, then it is consumed. Stockpiling is allowed up to the
   desk capacity.
4. **Scope — player-only.** Only the player has a desk, shop, and inventory. Taro plays the
   base coin-pour AI unchanged. Items may *target* Taro (force his pours, make him bust more
   easily) but Taro never buys or uses items.
5. **Architecture — extend the existing data layer (Approach A).** Reuse `ItemDefinition`,
   `ItemEffectKind` (append two values), `ShopCatalog`, and `BookShopView` as-is. Add a
   `PlayerInventory`, a `DeskItemBar` UI, a `GameManager.TryUseItem` entry point, change
   `ShopManager.TryBuyItem` from apply-on-buy to grant-on-buy, and rework `ItemEffectApplier`
   + `GlassManager` from the pending/next-round model to live, one-shot modifiers.

## Tuning constants

- `GameConstants.DeskCapacity = 8` — max total items held on the desk (duplicates allowed).
- No per-turn use limit.
- New item costs/magnitudes are placeholders, tunable as `ItemDefinition` data.

## The item set (9 items, all one-shot, used on the player's turn)

| # | Item | Cost | On click | Scope |
|---|------|------|----------|-------|
| 1 | Marked Coin | 35 | Your pour this turn pays ×2 if safe | this pour |
| 2 | Loaded Dice | 70 | Your pour this turn pays ×3 if safe | this pour |
| 3 | Steady Hand | 50 | −10 spill risk on your pour this turn | this pour |
| 4 | Iron Grip | 90 | −18 spill risk on your pour this turn | this pour |
| 5 | Dealer's Debt | 60 | Taro must pour 2 coins next turn | enemy next turn |
| 6 | Round for the Dealer | 55 | Taro's next pour carries +12 fill (busts easier) | enemy next turn |
| 7 | Bartender's Spectacles | 30 | Reveals true spill % on the HUD this round | this round |
| 8 | Buy the House a Round *(new)* | 45 | Lowers the current glass fill by 15 right now | live glass |
| 9 | Step Outside / Skip *(new)* | 40 | Ends your turn without pouring | turn action |

Behavior notes:

- **Self buffs (1–4)** apply to the single pour the player makes this turn, then they are
  spent. Payout multiplier (1–2) reuses the existing
  `EconomyManager.QueueNextSafeDropPayoutMultiplier` (the imminent pour consumes it). Risk
  relief (3–4) is applied to the player's relief for the next player pour, then cleared.
- **Enemy items (5–6)** arm Taro's *next* turn. Forced coins (5) reuses
  `GameManager.QueueEnemyForcedCoinCount`. Enemy penalty (6) is applied to the enemy's relief
  for the next enemy pour, then cleared.
- **#7** sets the reveal flag live for the current round; cleared at the next `ResetGlass`.
- **#8** is the headline new item; only meaningful when the glass already has fill. Because the
  glass is shared, lowering it helps whoever pours next — by design, the player uses it right
  before their own risky pour.
- **#9** lets the player bail on a dangerous glass and pass it to Taro.

## Components

### New — `PlayerInventory`

Plain `MonoBehaviour` on the GameManager rig; persists across rounds within a match.

- State: owned items as stacks (`item → count`).
- API: `void Grant(ItemDefinition)`, `bool Has(ItemDefinition)`, `bool TryConsume(ItemDefinition)`,
  `IReadOnlyList<(ItemDefinition item, int count)> Contents`, `int TotalCount`, `void Clear()`.
- Event: `event Action Changed` (the desk UI subscribes).
- Capacity: rejects `Grant` when `TotalCount >= GameConstants.DeskCapacity`.

### Changed — `ShopManager.TryBuyItem` (buy → grant)

```
if (item == null) return false;
if (inventory.TotalCount >= GameConstants.DeskCapacity) { warn; return false; }   // not charged
if (!TrySpend(item.DisplayName, item.Cost)) return false;
inventory.Grant(item);
return true;
```

`ItemEffectApplier.Apply` is no longer called at purchase. `ShopManager` gains an
`inventory` reference (resolved like its other manager refs). The `BuyMarkedCoin` /
`BuySteadyHand` / `BuyDealersDebt` name-wrappers and the fallback overlay catalog still work
(they call `TryBuyItem`, which now grants).

### New — `GameManager.TryUseItem` + `SkipPlayerTurn`

```
public bool TryUseItem(ItemDefinition item)
{
    if (currentState != GameState.PlayerTurn) return false;
    if (item == null || !inventory.Has(item)) return false;
    inventory.TryConsume(item);
    ItemEffectApplier.ApplyActive(item, economyManager, glassManager, this);
    return true;
}

public void SkipPlayerTurn()   // routed to by the SkipTurn effect
{
    if (currentState != GameState.PlayerTurn) return;
    BeginEnemyTurn();
}
```

No per-turn counter. `GameManager` holds the `PlayerInventory` reference (added to
`ResolveReferences`, created if absent). `StartMatch` calls `inventory.Clear()`.

### Reworked — `ItemEffectApplier.ApplyActive`

| Effect kind | Hook called (live) |
|---|---|
| `PayoutMultiplier` | `economy.QueueNextSafeDropPayoutMultiplier(mag)` *(unchanged hook)* |
| `ForceEnemyCoins` | `game.QueueEnemyForcedCoinCount(round(mag))` *(unchanged hook)* |
| `SafeZoneBonus` | `glass.AddPlayerPourRelief(mag)` |
| `EnemySafeZonePenalty` | `glass.AddEnemyPourPenalty(mag)` |
| `RevealTrueOdds` | `glass.RevealTrueOddsForRound()` |
| `ReduceCurrentRisk` *(new)* | `glass.ReduceCurrentRisk(mag)` |
| `SkipTurn` *(new)* | `game.SkipPlayerTurn()` |

Each arm keeps the existing null-guard + `WarnMissing` pattern. The static `Apply` may be
renamed `ApplyActive` (no other caller remains once `ShopManager` switches to grant-on-buy).

### Reworked — `GlassManager` (pending model → live one-shot modifiers)

Remove the `pendingNextRound*` fields and the promote-at-`ResetGlass` logic. Replace with
modifiers that are applied immediately and consumed by the relevant pour:

- `float activePlayerPourRelief`, `float activeEnemyPourPenalty`, `bool activeRevealTrueOdds`.
- `void AddPlayerPourRelief(float)` — **additive** (sums; the player paid for each), clamped ≥ 0.
- `void AddEnemyPourPenalty(float)` — **additive** (sums), clamped ≥ 0.

**Stacking decision (flagged for review):** because items are now consumables the player uses
deliberately, risk relief and enemy penalty **sum** when multiple are used (Steady Hand + Iron
Grip = −28 relief on one pour) rather than the v1 "strongest wins" (`Mathf.Max`). The payout
multiplier keeps the existing `QueueNextSafeDropPayoutMultiplier` strongest-wins behavior
unchanged for v1 (so Marked Coin + Loaded Dice = ×3, not ×6) to avoid swingy multiplicative
stacking; revisit if multiplicative payout stacking is wanted.
- `void RevealTrueOddsForRound()` — sets the reveal flag true.
- `void ReduceCurrentRisk(float amount)` — `currentOverflowProbability =
  Mathf.Max(0, currentOverflowProbability - amount)`, then fires `ProbabilityChanged`.
- `GetSafeZoneRelief(actor)`: player = `SpillSafeZoneThreshold + activePlayerPourRelief`,
  enemy = `SpillSafeZoneThreshold − activeEnemyPourPenalty`.
- `DropCoins(coins, actor)`: after resolving the spill, **consume the actor's one-shot
  modifier** (zero `activePlayerPourRelief` after a player pour; zero `activeEnemyPourPenalty`
  after an enemy pour). The relaxed relief clamp `[−Max, Max]` from v1 stays so the enemy
  penalty can raise the chance.
- `ResetGlass()`: zero all modifiers and the reveal flag (consumables don't linger via the
  glass), set `currentOverflowProbability = 0`, fire `ProbabilityChanged`.

### Changed — `ItemEffectKind`

Append (Unity serializes enums by index; never insert mid-list):

```
PayoutMultiplier, SafeZoneBonus, ForceEnemyCoins, EnemySafeZonePenalty, RevealTrueOdds,
ReduceCurrentRisk, SkipTurn
```

### New — `DeskItemBar` (world UI, runtime-built like `BookShopView` / `SaloonHudController`)

- A row of clickable item buttons on the player's side of the desk, each showing name + stack
  count, built with `RuntimeUiFactory`.
- Subscribes to `GameManager.StateChanged` and `PlayerInventory.Changed`: interactable only
  during `PlayerTurn`, greyed otherwise; refreshes counts on buy/use.
- Click → `GameManager.TryUseItem(item)`.
- Any final hand-modelled props get wired in the Unity editor, not by hand-editing scene/asset
  YAML (per project memory on FBX/scene wiring).

### `ShopCatalog`

Add the two new items so they appear with no scene wiring:
- `buy_the_house_a_round` — `ReduceCurrentRisk`, magnitude 15, cost 45.
- `step_outside` — `SkipTurn`, magnitude 0 (unused), cost 40.

## Data flow

1. Win a round → `EnterShopPhase` → `BookShopView` renders `ShopManager.Catalog` (unchanged).
2. Click an order → `ShopManager.TryBuyItem`: desk-full check → `TrySpendBankedCash` →
   `PlayerInventory.Grant`. No effect applied.
3. Finish shop → `StartRound` → `ResetGlass` (clears leftover glass modifiers) →
   `BeginPlayerTurn`.
4. `DeskItemBar` shows the player's stock. Click items → `GameManager.TryUseItem` consumes from
   inventory and applies the live effect.
5. Select coins → click the glass → pour resolves; this-pour buffs are consumed by that pour;
   armed enemy items wait for Taro.
6. Taro's turn consumes forced-coins / enemy-penalty, then they clear.
7. Repeat until someone overflows.

## Edge cases / error handling

- Buy with a full desk → rejected and **not** charged; warning logged.
- `TryUseItem` off-turn or out-of-stock → returns false, nothing consumed.
- `ReduceCurrentRisk` at 0 fill → clamps to 0 (allowed; the player's call).
- `SkipPlayerTurn` → `BeginEnemyTurn`; the existing empty-pool handling still applies.
- Enemy item armed but Taro never pours (player overflows, or Taro is out of coins) → the
  modifier is simply wasted; acceptable for a discretionary consumable.
- `StartMatch` clears the inventory, glass modifiers, and turn state. Items persist across
  rounds within a match, not across matches.

## Testing

EditMode (NUnit) against the `Meniscus.Tests.EditMode` assembly, using the existing
`SpillRollProvider` seam for deterministic drops and the GameObject-host `[SetUp]` /
`Object.DestroyImmediate` `[TearDown]` conventions.

- `PlayerInventory`: grant / consume / stacking / capacity-8 rejection / `Clear`.
- `GameManager.TryUseItem`: rejects off-turn and out-of-stock; consumes stock and applies the
  effect; `SkipPlayerTurn` transitions `PlayerTurn → EnemyTurn`.
- `ItemEffectApplier.ApplyActive`: each kind routes to its live hook (assert via the
  `GlassManager` / `EconomyManager` seams).
- `GlassManager`: `ReduceCurrentRisk` clamps ≥ 0 and fires `ProbabilityChanged`;
  `AddPlayerPourRelief` is consumed after a player `DropCoins`; `AddEnemyPourPenalty` is
  consumed after an enemy `DropCoins`; a negative enemy relief still raises the spill chance;
  `ResetGlass` clears all modifiers. (Rewrites the v1 pending-model tests.)
- `EconomyManager`: payout multiplier still consumed by the imminent safe drop (existing test
  largely holds).
- `ShopManager.TryBuyItem`: grants instead of applies; does not charge when the desk is full.
- `SaloonHudController`: the `Spill NN%` reveal suffix still works, now driven by the immediate
  reveal flag.
- **PlayMode / manual:**
  - Win to the shop, buy several items, finish; confirm they appear on the desk and persist
    into the next round.
  - On your turn, use Steady Hand then pour; confirm the pour's spill chance is lower and the
    item is consumed.
  - Use Buy the House a Round on a partly-full glass; confirm the fill drops immediately.
  - Use Step Outside; confirm your turn ends with no pour and Taro plays.
  - Use Round for the Dealer; confirm Taro busts more readily on his next pour and your own
    odds are unchanged.

## Future / out of scope

- **Enemy items** (Taro buys/uses items: block, steal, force-pour) — needs enemy inventory +
  AI use-decision logic + counterplay UI.
- **Per-item usability gating** in the desk UI (e.g. dimming Buy the House a Round at 0 fill).
- **`CoinSizeBias`** ("Crooked Scales") — skew the next round's coin-size roll.
- **Authored `ItemDefinition` assets + icons** replacing the code-built catalog.
