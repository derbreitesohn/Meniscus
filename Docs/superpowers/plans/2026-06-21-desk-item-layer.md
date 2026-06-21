# Desk Item Layer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the auto-apply between-rounds shop with a held, one-shot **desk inventory**: buying grants an item to the player's desk, and the player clicks it to use it during their own turn.

**Architecture:** Approach A from the spec — extend the existing `Assets/Scripts/Items/` data layer. Reuse `ItemDefinition` / `ItemEffectKind` / `ShopCatalog` / `BookShopView` as-is (the enum gains two appended values). Add a `PlayerInventory` (held stacks), a `DeskItemBar` (runtime UI), a `GameManager.TryUseItem` entry point, change `ShopManager.TryBuyItem` from apply-on-buy to grant-on-buy, and rework `GlassManager` + `ItemEffectApplier` from the pending/next-round model to **live one-shot modifiers** consumed by the relevant pour.

**Tech Stack:** Unity 6000.3.6f1, C#, Unity Test Framework (NUnit) EditMode tests in the `Meniscus.Tests.EditMode` assembly.

**Spec:** `Docs/superpowers/specs/2026-06-21-desk-item-layer-design.md`.

## Global Constraints

- **Branch first.** The repo is on `main` with a large pile of unrelated uncommitted work. Before the first commit: `git switch -c feature/desk-item-layer`.
- **The index already holds staged file *deletions*** (the old `GameStateManager` / `RoundResolver` / `MatchState` / `GamePhase` / `GlassState` / `MatchResult` / `RoundResult` / `ParticipantId` classes). These are NOT part of this feature. To avoid sweeping them into your commits, **every commit in this plan stages explicit paths and commits with a pathspec**: `git commit -m "..." -- <path1> <path2>`. Never `git add -A`, never a bare `git commit` without a pathspec.
- **Enum append-only.** Add new `ItemEffectKind` values at the END of the enum. Unity serializes enums by integer index; inserting mid-list corrupts saved data. (No authored `ItemDefinition` assets exist today — the catalog is code-built — so appending is safe.)
- **Effects apply live, scoped to one pour/turn:** self relief and enemy penalty are consumed by the actor's next `DropCoins`; reveal lasts until the next `ResetGlass`; `ReduceCurrentRisk` is immediate. No item auto-applies on purchase.
- **Stacking:** player relief and enemy penalty **sum** when multiple are used (additive). The payout multiplier keeps the existing strongest-wins (`Mathf.Max`) behavior.
- **Desk capacity:** `GameConstants.DeskCapacity = 8`. No per-turn use limit.
- **Namespaces:** runtime code under `Meniscus.*` (asmdef `Meniscus.Runtime`); tests under `Meniscus.Tests.EditMode` (asmdef `Meniscus.Tests.EditMode`, references `Meniscus.Runtime`).
- **Keep these public members working** — they are consumed by `EnemyAI`, `SaloonHudController`, `GlassVisualController`, `CameraController`: `GlassManager.CurrentTrueSpillChance`, `GlassManager.CurrentSafeZoneThreshold`, `GlassManager.CalculateCurrentTrueSpillChance(float)`, `GlassManager.TrueOddsRevealed`, `GlassManager.GetSafeZoneRelief(TurnActor)`. The rework only swaps the *backing* (pending → live); these accessors keep their signatures.
- **Scene/asset wiring** (if any final props are added later) is done in the Unity editor, not by hand-editing `.unity` / `.asset` YAML (per project memory on FBX/scene wiring). This plan's UI is built procedurally at runtime, so no manual YAML wiring is required.

## Running tests

EditMode tests run from the command line with the Unity Editor **closed** (it holds the project lock otherwise):

```bash
"C:/Program Files/Unity/Hub/Editor/6000.3.6f1/Editor/Unity.exe" -runTests -batchmode \
  -projectPath "C:/Users/Huti/Desktop/Git/Meniscus" \
  -testPlatform EditMode \
  -testFilter "<FILTER>" \
  -testResults "C:/Users/Huti/AppData/Local/Temp/claude/C--Users-Huti-Desktop-Git-Meniscus/26225f7e-185d-4c83-aa2a-ab5db4f407df/scratchpad/test-results.xml" \
  -logFile -
```

- Replace `<FILTER>` with a fully-qualified class (e.g. `Meniscus.Tests.EditMode.GlassManagerTests`) or a single method.
- **Exit code 0** = all selected tests passed. **Non-zero** = a failure OR a compile error in the assembly.
- **TDD note (statically typed):** when a test references a method/field that does not exist yet, the *failing* state is a **compilation error** in the run log (e.g. `'GlassManager' does not contain a definition for 'AddPlayerPourRelief'`), not an assertion failure. That red state is the expected "test fails" outcome. Because the whole assembly must compile, a test referencing a not-yet-added member makes the *entire* filtered run fail to compile — that is fine; it is still your red.
- **Fast compile check (optional, no editor lock):** see project memory `verify-compile-offline` to compile-check the C# via the Bee rsp + Roslyn without launching the editor. Useful between full runs.
- **All new/changed tests in this plan are synchronous** (they call managers directly and never depend on `GameManager`'s async drop-resolution coroutine). Run them with a method- or class-level filter. If some *pre-existing* `GameFlowTests` (the ones that drive a full drop through the `ResolveDrop` coroutine) are red in the working tree, that is in-flight presentation work unrelated to this plan — do not fix it here; just don't claim the whole suite is green.

---

### Task 1: GlassManager live one-shot modifiers (+ migrate the three glass item arms)

Swap the glass buffs from "pending, promoted next round" to "live, consumed by the pour they apply to," and add `ReduceCurrentRisk`. Migrate `ItemEffectApplier`'s three glass arms in lockstep so the runtime assembly keeps compiling. Rewrite the pending-model tests.

**Files:**
- Modify: `Assets/Scripts/Core/GlassManager.cs`
- Modify: `Assets/Scripts/Items/ItemEffectApplier.cs`
- Test: `Assets/Tests/EditMode/GlassManagerTests.cs` (replace 5 tests, add new)
- Test: `Assets/Tests/EditMode/ItemEffectApplierTests.cs` (rewrite both tests)

**Interfaces:**
- Consumes: `GameConstants.SpillSafeZoneThreshold`, `GameConstants.MaxOverflowProbability`; `TurnActor`; existing `CalculateTrueSpillChance(float, float)`, `SpillRollProvider`, `ProbabilityChanged`.
- Produces:
  - `void GlassManager.AddPlayerPourRelief(float relief)` — additive, clamped to `[0, Max-Threshold]`.
  - `void GlassManager.AddEnemyPourPenalty(float penalty)` — additive, clamped to `[0, Max]`.
  - `void GlassManager.RevealTrueOddsForRound()`.
  - `void GlassManager.ReduceCurrentRisk(float amount)` — lowers current fill, clamped ≥ 0, fires `ProbabilityChanged`.
  - `DropCoins(coins, actor)` consumes the actor's one-shot modifier after resolving; `ResetGlass()` clears all modifiers.
  - **Removed:** `QueueNextRoundSafeZoneBonus`, `QueueNextRoundEnemySafeZonePenalty`, `QueueNextRoundRevealTrueOdds`.

- [ ] **Step 1: Rewrite the GlassManager tests (these are your failing tests)**

In `Assets/Tests/EditMode/GlassManagerTests.cs`, **delete these five existing tests** (they exercise the removed pending API): `QueueNextRoundSafeZoneBonus_AppliesForOneResetThenExpires`, `GetSafeZoneRelief_Player_UsesSafeZoneBonus_Enemy_UsesNegativePenalty`, `DropCoins_DrunkEnemy_SpillsWhereSoberPlayerWouldNot`, `EnemySafeZonePenalty_ExpiresAfterOneReset`, `QueueNextRoundRevealTrueOdds_RevealsForOneRoundThenExpires`.

**Keep** all other tests (`ResetGlass_SetsOverflowProbabilityToZero`, `DropCoins_WithNoCoins_*`, `DropCoins_AddsCombinedRiskToProbability`, `CalculateTrueSpillChance_ClimbsTowardCeiling*`, `DropCoins_AtModerateRisk_*`, `DropCoins_AtMaxRisk_*`, `CalculateTrueSpillChance_NegativeRelief_RaisesChanceAboveZeroRelief`).

**Add these tests** to the `GlassManagerTests` class (above the `static Coin CreateCoin(...)` helper; they reuse that helper and the `glassManager` field from `[SetUp]`):

```csharp
        [Test]
        public void AddPlayerPourRelief_LowersPlayerSpillChance_ThenConsumedByPour()
        {
            glassManager.AddPlayerPourRelief(10f);

            Assert.AreEqual(10f, glassManager.GetSafeZoneRelief(TurnActor.Player), 0.001f);
            Assert.AreEqual(10f, glassManager.CurrentSafeZoneThreshold, 0.001f);

            // 10 relief makes risk 50 behave like 40 of effective fill.
            Assert.AreEqual(
                GlassManager.CalculateTrueSpillChance(40f),
                glassManager.CalculateCurrentTrueSpillChance(50f),
                0.001f);

            var coin = CreateCoin("Player Fill", 50f, 10, true);
            glassManager.DropCoins(new[] { coin }, TurnActor.Player);

            // Spent by the pour it applied to.
            Assert.AreEqual(0f, glassManager.GetSafeZoneRelief(TurnActor.Player), 0.001f);
            Object.DestroyImmediate(coin.gameObject);
        }

        [Test]
        public void AddPlayerPourRelief_StacksAdditively()
        {
            glassManager.AddPlayerPourRelief(10f);
            glassManager.AddPlayerPourRelief(18f);

            Assert.AreEqual(28f, glassManager.GetSafeZoneRelief(TurnActor.Player), 0.001f);
        }

        [Test]
        public void AddEnemyPourPenalty_GivesEnemyNegativeRelief_ThenConsumedByEnemyPour()
        {
            glassManager.AddEnemyPourPenalty(12f);
            Assert.AreEqual(-12f, glassManager.GetSafeZoneRelief(TurnActor.Enemy), 0.001f);

            var coin = CreateCoin("Enemy Fill", 10f, 10, false);
            glassManager.DropCoins(new[] { coin }, TurnActor.Enemy);

            Assert.AreEqual(0f, glassManager.GetSafeZoneRelief(TurnActor.Enemy), 0.001f);
            Object.DestroyImmediate(coin.gameObject);
        }

        [Test]
        public void AddEnemyPourPenalty_StacksAdditively()
        {
            glassManager.AddEnemyPourPenalty(12f);
            glassManager.AddEnemyPourPenalty(8f);

            Assert.AreEqual(-20f, glassManager.GetSafeZoneRelief(TurnActor.Enemy), 0.001f);
        }

        [Test]
        public void DropCoins_DrunkEnemy_SpillsWhereSoberPlayerWouldNot()
        {
            glassManager.AddEnemyPourPenalty(12f);

            // A roll between the player's (lower) and the drunk enemy's (higher) spill chance.
            glassManager.SpillRollProvider = () => 36f;

            var enemyCoin = CreateCoin("Enemy Fill", 50f, 10, false);
            var enemyResult = glassManager.DropCoins(new[] { enemyCoin }, TurnActor.Enemy);

            Assert.IsTrue(enemyResult.Overflowed);
            Assert.Greater(enemyResult.TrueSpillChance, GlassManager.CalculateTrueSpillChance(50f, 0f));

            glassManager.ResetGlass();                 // clears the penalty and the fill
            glassManager.SpillRollProvider = () => 36f;

            var playerCoin = CreateCoin("Player Fill", 50f, 10, true);
            var playerResult = glassManager.DropCoins(new[] { playerCoin }, TurnActor.Player);

            Assert.IsFalse(playerResult.Overflowed);   // sober player at the same fill is safe

            Object.DestroyImmediate(enemyCoin.gameObject);
            Object.DestroyImmediate(playerCoin.gameObject);
        }

        [Test]
        public void ReduceCurrentRisk_LowersFill_ClampedAtZero()
        {
            var coin = CreateCoin("Fill", 40f, 10, true);
            glassManager.DropCoins(new[] { coin }, TurnActor.Player);
            Assert.AreEqual(40f, glassManager.CurrentOverflowProbability, 0.001f);

            glassManager.ReduceCurrentRisk(15f);
            Assert.AreEqual(25f, glassManager.CurrentOverflowProbability, 0.001f);

            glassManager.ReduceCurrentRisk(999f);
            Assert.AreEqual(0f, glassManager.CurrentOverflowProbability, 0.001f);
            Object.DestroyImmediate(coin.gameObject);
        }

        [Test]
        public void RevealTrueOddsForRound_RevealsImmediately_ClearedByResetGlass()
        {
            Assert.IsFalse(glassManager.TrueOddsRevealed);

            glassManager.RevealTrueOddsForRound();
            Assert.IsTrue(glassManager.TrueOddsRevealed);

            glassManager.ResetGlass();
            Assert.IsFalse(glassManager.TrueOddsRevealed);
        }

        [Test]
        public void ResetGlass_ClearsPourModifiers()
        {
            glassManager.AddPlayerPourRelief(10f);
            glassManager.AddEnemyPourPenalty(12f);
            glassManager.ResetGlass();

            Assert.AreEqual(0f, glassManager.GetSafeZoneRelief(TurnActor.Player), 0.001f);
            Assert.AreEqual(0f, glassManager.GetSafeZoneRelief(TurnActor.Enemy), 0.001f);
        }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run `<FILTER>` = `Meniscus.Tests.EditMode.GlassManagerTests`.
Expected: FAIL — compile error, `'GlassManager' does not contain a definition for 'AddPlayerPourRelief'` (and `AddEnemyPourPenalty` / `RevealTrueOddsForRound` / `ReduceCurrentRisk`).

- [ ] **Step 3: Replace the GlassManager fields**

In `Assets/Scripts/Core/GlassManager.cs`, replace the field block:

```csharp
        [SerializeField, Range(0f, GameConstants.MaxOverflowProbability)]
        float currentOverflowProbability;
        [SerializeField, Min(0f)] float pendingNextRoundSafeZoneBonus;
        [SerializeField, Min(0f)] float activeSafeZoneBonus;
        [SerializeField, Min(0f)] float pendingNextRoundEnemySafeZonePenalty;
        [SerializeField, Min(0f)] float activeEnemySafeZonePenalty;
        [SerializeField] bool pendingNextRoundRevealTrueOdds;
        [SerializeField] bool activeRevealTrueOdds;
```

with:

```csharp
        [SerializeField, Range(0f, GameConstants.MaxOverflowProbability)]
        float currentOverflowProbability;
        [SerializeField, Min(0f)] float activePlayerPourRelief;
        [SerializeField, Min(0f)] float activeEnemyPourPenalty;
        [SerializeField] bool activeRevealTrueOdds;
```

- [ ] **Step 4: Update the two accessors that referenced the renamed fields**

Change `CurrentSafeZoneThreshold` to read the renamed field:

```csharp
        public float CurrentSafeZoneThreshold => Mathf.Clamp(
            GameConstants.SpillSafeZoneThreshold + activePlayerPourRelief,
            0f,
            GameConstants.MaxOverflowProbability);
```

Change `GetSafeZoneRelief` to read the renamed enemy field:

```csharp
        public float GetSafeZoneRelief(TurnActor actor) =>
            actor == TurnActor.Enemy
                ? GameConstants.SpillSafeZoneThreshold - activeEnemyPourPenalty
                : CurrentSafeZoneThreshold;
```

- [ ] **Step 5: Rewrite `ResetGlass` to clear modifiers (no more promotion)**

```csharp
        public void ResetGlass()
        {
            activePlayerPourRelief = 0f;
            activeEnemyPourPenalty = 0f;
            activeRevealTrueOdds = false;
            currentOverflowProbability = 0f;
            ProbabilityChanged?.Invoke(currentOverflowProbability);
        }
```

- [ ] **Step 6: Replace the three `Queue…` methods with the live hooks**

Replace this block:

```csharp
        public void QueueNextRoundSafeZoneBonus(float bonus)
        {
            var clampedBonus = Mathf.Clamp(
                bonus,
                0f,
                GameConstants.MaxOverflowProbability - GameConstants.SpillSafeZoneThreshold);
            pendingNextRoundSafeZoneBonus = Mathf.Max(pendingNextRoundSafeZoneBonus, clampedBonus);
        }

        public void QueueNextRoundEnemySafeZonePenalty(float penalty)
        {
            var clampedPenalty = Mathf.Clamp(penalty, 0f, GameConstants.MaxOverflowProbability);
            pendingNextRoundEnemySafeZonePenalty =
                Mathf.Max(pendingNextRoundEnemySafeZonePenalty, clampedPenalty);
        }

        public void QueueNextRoundRevealTrueOdds() => pendingNextRoundRevealTrueOdds = true;
```

with:

```csharp
        public void AddPlayerPourRelief(float relief)
        {
            if (relief <= 0f)
                return;

            activePlayerPourRelief = Mathf.Clamp(
                activePlayerPourRelief + relief,
                0f,
                GameConstants.MaxOverflowProbability - GameConstants.SpillSafeZoneThreshold);
        }

        public void AddEnemyPourPenalty(float penalty)
        {
            if (penalty <= 0f)
                return;

            activeEnemyPourPenalty = Mathf.Clamp(
                activeEnemyPourPenalty + penalty,
                0f,
                GameConstants.MaxOverflowProbability);
        }

        public void RevealTrueOddsForRound() => activeRevealTrueOdds = true;

        public void ReduceCurrentRisk(float amount)
        {
            if (amount <= 0f)
                return;

            currentOverflowProbability = Mathf.Max(0f, currentOverflowProbability - amount);
            ProbabilityChanged?.Invoke(currentOverflowProbability);
        }
```

- [ ] **Step 7: Consume the actor's one-shot modifier in `DropCoins`**

In `DropCoins`, between the `result` construction and the `DropResolved?.Invoke(result);` line, insert:

```csharp
            // One-shot pour modifiers are spent by the pour they applied to.
            if (actor == TurnActor.Player)
                activePlayerPourRelief = 0f;
            else
                activeEnemyPourPenalty = 0f;

```

- [ ] **Step 8: Migrate the three glass arms in `ItemEffectApplier`**

In `Assets/Scripts/Items/ItemEffectApplier.cs`, change only the three method calls (leave the `PayoutMultiplier` and `ForceEnemyCoins` arms untouched):

- `glass.QueueNextRoundSafeZoneBonus(item.Magnitude);` → `glass.AddPlayerPourRelief(item.Magnitude);`
- `glass.QueueNextRoundEnemySafeZonePenalty(item.Magnitude);` → `glass.AddEnemyPourPenalty(item.Magnitude);`
- `glass.QueueNextRoundRevealTrueOdds();` → `glass.RevealTrueOddsForRound();`

- [ ] **Step 9: Rewrite the two `ItemEffectApplierTests` for the live model**

Replace both test bodies in `Assets/Tests/EditMode/ItemEffectApplierTests.cs` (no more `ResetGlass` — effects are live now):

```csharp
        [Test]
        public void Apply_EnemySafeZonePenalty_AddsNegativeEnemyReliefImmediately()
        {
            var item = ItemDefinition.Create(
                "round_for_the_dealer", "Round for the Dealer", "",
                55, ItemEffectKind.EnemySafeZonePenalty, 12f);

            ItemEffectApplier.Apply(item, null, glassManager, null);

            Assert.AreEqual(-12f, glassManager.GetSafeZoneRelief(TurnActor.Enemy), 0.001f);
        }

        [Test]
        public void Apply_RevealTrueOdds_RevealsImmediately()
        {
            var item = ItemDefinition.Create(
                "bartenders_spectacles", "Bartender's Spectacles", "",
                30, ItemEffectKind.RevealTrueOdds, 0f);

            ItemEffectApplier.Apply(item, null, glassManager, null);

            Assert.IsTrue(glassManager.TrueOddsRevealed);
        }
```

- [ ] **Step 10: Run the tests to verify they pass**

Run `<FILTER>` = `Meniscus.Tests.EditMode.GlassManagerTests` then `Meniscus.Tests.EditMode.ItemEffectApplierTests`.
Expected: PASS (exit 0) for both.

- [ ] **Step 11: Commit**

```bash
git switch -c feature/desk-item-layer   # first commit only; skip if the branch exists
git commit -m "feat(glass): live one-shot pour modifiers + ReduceCurrentRisk" -- \
  Assets/Scripts/Core/GlassManager.cs \
  Assets/Scripts/Items/ItemEffectApplier.cs \
  Assets/Tests/EditMode/GlassManagerTests.cs \
  Assets/Tests/EditMode/ItemEffectApplierTests.cs
```

---

### Task 2: PlayerInventory + desk capacity

A held stack of owned items that persists across rounds, with an 8-item desk cap.

**Files:**
- Modify: `Assets/Scripts/Core/GameConstants.cs`
- Create: `Assets/Scripts/Items/PlayerInventory.cs`
- Test: `Assets/Tests/EditMode/PlayerInventoryTests.cs`

**Interfaces:**
- Consumes: `GameConstants.DeskCapacity`, `ItemDefinition`.
- Produces:
  - `struct Meniscus.Items.ItemStack { ItemDefinition Item; int Count; }`
  - `class Meniscus.Items.PlayerInventory : MonoBehaviour` with `event Action Changed`, `int TotalCount`, `bool IsFull`, `IReadOnlyList<ItemStack> Contents()`, `bool Grant(ItemDefinition)`, `bool Has(ItemDefinition)`, `bool TryConsume(ItemDefinition)`, `void Clear()`.

- [ ] **Step 1: Write the failing tests**

Create `Assets/Tests/EditMode/PlayerInventoryTests.cs`:

```csharp
using Meniscus.Core;
using Meniscus.Items;
using NUnit.Framework;
using UnityEngine;

namespace Meniscus.Tests.EditMode
{
    public class PlayerInventoryTests
    {
        GameObject host;
        PlayerInventory inventory;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("Inventory Test Host");
            inventory = host.AddComponent<PlayerInventory>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(host);
        }

        static ItemDefinition Item(string id) =>
            ItemDefinition.Create(id, id, "", 10, ItemEffectKind.PayoutMultiplier, 2f);

        [Test]
        public void Grant_ThenHas_AndStacksCounts()
        {
            var a = Item("a");

            Assert.IsTrue(inventory.Grant(a));
            Assert.IsTrue(inventory.Has(a));
            Assert.AreEqual(1, inventory.TotalCount);

            inventory.Grant(a);
            Assert.AreEqual(2, inventory.TotalCount);
            Assert.AreEqual(1, inventory.Contents().Count);
            Assert.AreEqual(2, inventory.Contents()[0].Count);
        }

        [Test]
        public void TryConsume_DecrementsAndRemovesAtZero()
        {
            var a = Item("a");
            inventory.Grant(a);

            Assert.IsTrue(inventory.TryConsume(a));
            Assert.IsFalse(inventory.Has(a));
            Assert.AreEqual(0, inventory.TotalCount);
            Assert.IsFalse(inventory.TryConsume(a));   // already empty
        }

        [Test]
        public void Grant_RejectedWhenFull()
        {
            for (var i = 0; i < GameConstants.DeskCapacity; i++)
                Assert.IsTrue(inventory.Grant(Item($"item{i}")));

            Assert.IsTrue(inventory.IsFull);
            Assert.IsFalse(inventory.Grant(Item("overflow")));
            Assert.AreEqual(GameConstants.DeskCapacity, inventory.TotalCount);
        }

        [Test]
        public void Clear_EmptiesInventory()
        {
            inventory.Grant(Item("a"));
            inventory.Clear();
            Assert.AreEqual(0, inventory.TotalCount);
        }

        [Test]
        public void Changed_FiresOnGrantAndConsume()
        {
            var fired = 0;
            inventory.Changed += () => fired++;

            var a = Item("a");
            inventory.Grant(a);
            inventory.TryConsume(a);

            Assert.AreEqual(2, fired);
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run `<FILTER>` = `Meniscus.Tests.EditMode.PlayerInventoryTests`.
Expected: FAIL — compile error, `PlayerInventory` / `ItemStack` / `GameConstants.DeskCapacity` not found.

- [ ] **Step 3: Add the desk-capacity constant**

In `Assets/Scripts/Core/GameConstants.cs`, add beside the other counts (e.g. after `public const int MaxEnemyCoinsPerTurn = 2;`):

```csharp
        public const int DeskCapacity = 8;
```

- [ ] **Step 4: Create `PlayerInventory`**

Create `Assets/Scripts/Items/PlayerInventory.cs`:

```csharp
using System;
using System.Collections.Generic;
using Meniscus.Core;
using UnityEngine;

namespace Meniscus.Items
{
    /// <summary>One purchased-item stack held on the player's desk.</summary>
    public readonly struct ItemStack
    {
        public ItemStack(ItemDefinition item, int count)
        {
            Item = item;
            Count = count;
        }

        public ItemDefinition Item { get; }
        public int Count { get; }
    }

    /// <summary>
    /// The player's held desk items. Purchases grant a stack here (instead of applying an effect);
    /// using an item on the player's turn consumes one. Persists across rounds within a match and is
    /// capped at <see cref="GameConstants.DeskCapacity"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerInventory : MonoBehaviour
    {
        readonly List<ItemDefinition> items = new();
        readonly List<int> counts = new();

        public event Action Changed;

        public int TotalCount { get; private set; }
        public bool IsFull => TotalCount >= GameConstants.DeskCapacity;

        public IReadOnlyList<ItemStack> Contents()
        {
            var result = new List<ItemStack>(items.Count);

            for (var i = 0; i < items.Count; i++)
                result.Add(new ItemStack(items[i], counts[i]));

            return result;
        }

        public bool Grant(ItemDefinition item)
        {
            if (item == null || IsFull)
                return false;

            var index = IndexOf(item);

            if (index >= 0)
                counts[index]++;
            else
            {
                items.Add(item);
                counts.Add(1);
            }

            TotalCount++;
            Changed?.Invoke();
            return true;
        }

        public bool Has(ItemDefinition item) => item != null && IndexOf(item) >= 0;

        public bool TryConsume(ItemDefinition item)
        {
            var index = item == null ? -1 : IndexOf(item);

            if (index < 0)
                return false;

            counts[index]--;
            TotalCount--;

            if (counts[index] <= 0)
            {
                items.RemoveAt(index);
                counts.RemoveAt(index);
            }

            Changed?.Invoke();
            return true;
        }

        public void Clear()
        {
            if (items.Count == 0 && TotalCount == 0)
                return;

            items.Clear();
            counts.Clear();
            TotalCount = 0;
            Changed?.Invoke();
        }

        int IndexOf(ItemDefinition item)
        {
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i] == item)
                    return i;
            }

            return -1;
        }
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run `<FILTER>` = `Meniscus.Tests.EditMode.PlayerInventoryTests`.
Expected: PASS (exit 0).

- [ ] **Step 6: Commit**

```bash
git commit -m "feat(items): PlayerInventory held desk stacks + DeskCapacity" -- \
  Assets/Scripts/Core/GameConstants.cs \
  Assets/Scripts/Items/PlayerInventory.cs \
  Assets/Tests/EditMode/PlayerInventoryTests.cs
```

(If Unity generated `.meta` files for the new `.cs` while running tests, add them too: `Assets/Scripts/Items/PlayerInventory.cs.meta`, `Assets/Tests/EditMode/PlayerInventoryTests.cs.meta`.)

---

### Task 3: GameManager.TryUseItem + SkipPlayerTurn + inventory wiring

The entry point the desk UI calls to use an item on the player's turn, plus the skip-turn action and inventory lifecycle.

**Files:**
- Modify: `Assets/Scripts/Core/GameManager.cs`
- Test: `Assets/Tests/EditMode/GameFlowTests.cs` (append 3 tests)

**Interfaces:**
- Consumes: `PlayerInventory` (Task 2), `ItemEffectApplier.Apply` (Task 1), `ItemDefinition`.
- Produces:
  - `PlayerInventory GameManager.Inventory { get; }`
  - `bool GameManager.TryUseItem(ItemDefinition item)` — only in `PlayerTurn`, only if in stock; consumes then applies the live effect.
  - `void GameManager.SkipPlayerTurn()` — ends the player's turn (→ `BeginEnemyTurn`).

- [ ] **Step 1: Write the failing tests**

Append to the `GameFlowTests` class in `Assets/Tests/EditMode/GameFlowTests.cs` (reuses the existing `CreateGameFixture()` helper; these tests are synchronous and never trigger the drop coroutine). Add `using Meniscus.Items;` to the file's usings if not present:

```csharp
        [Test]
        public void TryUseItem_DuringPlayerTurn_ConsumesItemAndAppliesEffect()
        {
            var fixture = CreateGameFixture();
            fixture.GameManager.StartMatch();   // → PlayerTurn

            var item = ItemDefinition.Create(
                "steady_hand", "Steady Hand", "", 50, ItemEffectKind.SafeZoneBonus, 10f);
            fixture.GameManager.Inventory.Grant(item);

            Assert.AreEqual(GameState.PlayerTurn, fixture.GameManager.CurrentState);
            Assert.IsTrue(fixture.GameManager.TryUseItem(item));
            Assert.IsFalse(fixture.GameManager.Inventory.Has(item));
            Assert.AreEqual(10f, fixture.GlassManager.GetSafeZoneRelief(TurnActor.Player), 0.001f);

            fixture.Destroy();
        }

        [Test]
        public void TryUseItem_NotInInventory_ReturnsFalse()
        {
            var fixture = CreateGameFixture();
            fixture.GameManager.StartMatch();

            var item = ItemDefinition.Create(
                "steady_hand", "Steady Hand", "", 50, ItemEffectKind.SafeZoneBonus, 10f);

            Assert.IsFalse(fixture.GameManager.TryUseItem(item));

            fixture.Destroy();
        }

        [Test]
        public void SkipPlayerTurn_DuringPlayerTurn_EndsPlayerTurn()
        {
            var fixture = CreateGameFixture();
            fixture.GameManager.StartMatch();   // → PlayerTurn, enemy has coins

            fixture.GameManager.SkipPlayerTurn();

            Assert.AreEqual(GameState.EnemyTurn, fixture.GameManager.CurrentState);

            fixture.Destroy();
        }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run `<FILTER>` = `Meniscus.Tests.EditMode.GameFlowTests.TryUseItem_DuringPlayerTurn_ConsumesItemAndAppliesEffect` (and the other two).
Expected: FAIL — compile error, `GameManager` has no `Inventory` / `TryUseItem` / `SkipPlayerTurn`.

- [ ] **Step 3: Add the using and inventory field**

In `Assets/Scripts/Core/GameManager.cs`, add `using Meniscus.Items;` to the usings. Add a field beside the other `[Header("Managers")]` serialized refs:

```csharp
        [SerializeField] PlayerInventory playerInventory;
```

Add the accessor beside the other public properties (e.g. after `public EconomyManager EconomyManager => economyManager;`):

```csharp
        public PlayerInventory Inventory => playerInventory;
```

- [ ] **Step 4: Resolve and clear the inventory**

In `ResolveReferences()`, add (place it before the `saloonHudController` resolution so the inventory exists when other systems look for it):

```csharp
            if (playerInventory == null)
                playerInventory = FindAnyObjectByType<PlayerInventory>();

            if (playerInventory == null)
                playerInventory = gameObject.AddComponent<PlayerInventory>();
```

In `StartMatch()`, clear it alongside the other reset calls (next to `economyManager?.ClearQueuedShopBonuses();`):

```csharp
            playerInventory?.Clear();
```

- [ ] **Step 5: Add `TryUseItem` and `SkipPlayerTurn`**

Add both methods (e.g. just after `FinishShopPhase()`):

```csharp
        public bool TryUseItem(ItemDefinition item)
        {
            if (currentState != GameState.PlayerTurn)
            {
                Debug.LogWarning($"[GameManager] Ignored item use while state={currentState}.");
                return false;
            }

            if (item == null || playerInventory == null || !playerInventory.Has(item))
                return false;

            playerInventory.TryConsume(item);
            ItemEffectApplier.Apply(item, economyManager, glassManager, this);
            return true;
        }

        public void SkipPlayerTurn()
        {
            if (currentState != GameState.PlayerTurn)
            {
                Debug.LogWarning($"[GameManager] SkipPlayerTurn ignored while state={currentState}.");
                return;
            }

            BeginEnemyTurn();
        }
```

- [ ] **Step 6: Run the tests to verify they pass**

Run `<FILTER>` = `Meniscus.Tests.EditMode.GameFlowTests`. The three new tests pass.
Expected: the three new tests PASS. (If any *pre-existing* coroutine-driven `GameFlowTests` are red in the working tree, that is the unrelated in-flight presentation issue noted in "Running tests" — confirm your three are green via method-level filters.)

- [ ] **Step 7: Commit**

```bash
git commit -m "feat(game): TryUseItem + SkipPlayerTurn + inventory lifecycle" -- \
  Assets/Scripts/Core/GameManager.cs \
  Assets/Tests/EditMode/GameFlowTests.cs
```

---

### Task 4: New effect kinds + applier routing (ReduceCurrentRisk, SkipTurn)

Adds the two new `ItemEffectKind` values and routes them to the Task 1 / Task 3 hooks.

**Files:**
- Modify: `Assets/Scripts/Items/ItemEffectKind.cs`
- Modify: `Assets/Scripts/Items/ItemEffectApplier.cs`
- Test: `Assets/Tests/EditMode/ItemEffectApplierTests.cs` (append ReduceCurrentRisk test)
- Test: `Assets/Tests/EditMode/GameFlowTests.cs` (append SkipTurn integration test)

**Interfaces:**
- Consumes: `GlassManager.ReduceCurrentRisk(float)` (Task 1), `GameManager.SkipPlayerTurn()` + `TryUseItem` (Task 3).
- Produces: `ItemEffectKind.ReduceCurrentRisk`, `ItemEffectKind.SkipTurn`; `ItemEffectApplier.Apply` handles both.

- [ ] **Step 1: Write the failing tests**

Append to `ItemEffectApplierTests` (add `using Meniscus.Gameplay;` to the file's usings):

```csharp
        [Test]
        public void Apply_ReduceCurrentRisk_LowersCurrentFill()
        {
            var fillCoin = MakeCoin(40f);
            glassManager.DropCoins(new[] { fillCoin }, TurnActor.Player);
            Assert.AreEqual(40f, glassManager.CurrentOverflowProbability, 0.001f);

            var item = ItemDefinition.Create(
                "buy_the_house_a_round", "Buy the House a Round", "",
                45, ItemEffectKind.ReduceCurrentRisk, 15f);

            ItemEffectApplier.Apply(item, null, glassManager, null);

            Assert.AreEqual(25f, glassManager.CurrentOverflowProbability, 0.001f);
            Object.DestroyImmediate(fillCoin.gameObject);
        }

        static Coin MakeCoin(float risk)
        {
            var coinObject = new GameObject("Coin");
            var coin = coinObject.AddComponent<Coin>();
            coin.Configure(CoinSize.Medium, risk, 10, true);
            return coin;
        }
```

Append to `GameFlowTests` (exercises the full chain `TryUseItem → Apply → SkipTurn → SkipPlayerTurn`):

```csharp
        [Test]
        public void TryUseItem_SkipTurnItem_EndsPlayerTurn()
        {
            var fixture = CreateGameFixture();
            fixture.GameManager.StartMatch();   // → PlayerTurn

            var skip = ItemDefinition.Create(
                "step_outside", "Step Outside", "", 40, ItemEffectKind.SkipTurn, 0f);
            fixture.GameManager.Inventory.Grant(skip);

            Assert.IsTrue(fixture.GameManager.TryUseItem(skip));
            Assert.AreEqual(GameState.EnemyTurn, fixture.GameManager.CurrentState);
            Assert.IsFalse(fixture.GameManager.Inventory.Has(skip));

            fixture.Destroy();
        }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run `<FILTER>` = `Meniscus.Tests.EditMode.ItemEffectApplierTests`.
Expected: FAIL — compile error, `ItemEffectKind` has no `ReduceCurrentRisk` / `SkipTurn`.

- [ ] **Step 3: Append the two enum values**

In `Assets/Scripts/Items/ItemEffectKind.cs`, append at the END:

```csharp
    public enum ItemEffectKind
    {
        PayoutMultiplier,
        SafeZoneBonus,
        ForceEnemyCoins,
        EnemySafeZonePenalty,
        RevealTrueOdds,
        ReduceCurrentRisk,
        SkipTurn
    }
```

- [ ] **Step 4: Add the two routing arms**

In `Assets/Scripts/Items/ItemEffectApplier.cs`, add two `case` arms inside `switch (item.Effect)`, before the `default:` arm:

```csharp
                case ItemEffectKind.ReduceCurrentRisk:
                    if (glass != null)
                        glass.ReduceCurrentRisk(item.Magnitude);
                    else
                        WarnMissing(item, nameof(GlassManager));
                    break;

                case ItemEffectKind.SkipTurn:
                    if (game != null)
                        game.SkipPlayerTurn();
                    else
                        WarnMissing(item, nameof(GameManager));
                    break;
```

- [ ] **Step 5: Run the tests to verify they pass**

Run `<FILTER>` = `Meniscus.Tests.EditMode.ItemEffectApplierTests`, then the new `GameFlowTests` method `TryUseItem_SkipTurnItem_EndsPlayerTurn`.
Expected: PASS (exit 0).

- [ ] **Step 6: Commit**

```bash
git commit -m "feat(items): add ReduceCurrentRisk and SkipTurn effect kinds" -- \
  Assets/Scripts/Items/ItemEffectKind.cs \
  Assets/Scripts/Items/ItemEffectApplier.cs \
  Assets/Tests/EditMode/ItemEffectApplierTests.cs \
  Assets/Tests/EditMode/GameFlowTests.cs
```

---

### Task 5: ShopManager — buy grants to inventory (not apply-on-buy)

The heart of the feature: a purchase now puts the item on the desk instead of firing its effect.

**Files:**
- Modify: `Assets/Scripts/UI/ShopManager.cs`
- Test: `Assets/Tests/EditMode/ShopManagerTests.cs` (new)

**Interfaces:**
- Consumes: `PlayerInventory` (Task 2), `EconomyManager.TrySpendBankedCash` / `PlayerTotalBankedCash` (existing).
- Produces: `ShopManager.TryBuyItem(item)` charges banked cash and grants the item; returns false (and does not charge) when the item is null, the desk is full, or it is unaffordable.

- [ ] **Step 1: Write the failing tests**

Create `Assets/Tests/EditMode/ShopManagerTests.cs`:

```csharp
using Meniscus.Core;
using Meniscus.Gameplay;
using Meniscus.Items;
using Meniscus.UI;
using NUnit.Framework;
using UnityEngine;

namespace Meniscus.Tests.EditMode
{
    public class ShopManagerTests
    {
        GameObject host;
        EconomyManager economy;
        PlayerInventory inventory;
        ShopManager shop;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("Shop Test Host");
            economy = host.AddComponent<EconomyManager>();
            inventory = host.AddComponent<PlayerInventory>();
            shop = host.AddComponent<ShopManager>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(host);
        }

        // EconomyManager has no public cash setter; bank cash through the normal award path.
        void BankCash(int amount)
        {
            var coinObject = new GameObject("Cash Coin");
            var coin = coinObject.AddComponent<Coin>();
            coin.Configure(CoinSize.Medium, 0f, amount, true);
            economy.AwardSafeDrop(new[] { coin }, 0f);
            economy.BankCurrentRoundEarnings();
            Object.DestroyImmediate(coinObject);
        }

        static ItemDefinition Item(string id, int cost) =>
            ItemDefinition.Create(id, id, "", cost, ItemEffectKind.PayoutMultiplier, 2f);

        [Test]
        public void TryBuyItem_GrantsToInventoryAndChargesCash()
        {
            BankCash(100);
            var item = Item("marked_coin", 35);

            Assert.IsTrue(shop.TryBuyItem(item));
            Assert.IsTrue(inventory.Has(item));
            Assert.AreEqual(65, economy.PlayerTotalBankedCash);
        }

        [Test]
        public void TryBuyItem_Unaffordable_ReturnsFalseAndGrantsNothing()
        {
            BankCash(20);
            var item = Item("loaded_dice", 70);

            Assert.IsFalse(shop.TryBuyItem(item));
            Assert.IsFalse(inventory.Has(item));
            Assert.AreEqual(20, economy.PlayerTotalBankedCash);
        }

        [Test]
        public void TryBuyItem_DeskFull_NotChargedAndNotGranted()
        {
            BankCash(1000);

            for (var i = 0; i < GameConstants.DeskCapacity; i++)
                Assert.IsTrue(inventory.Grant(Item($"filler{i}", 1)));

            var cashBefore = economy.PlayerTotalBankedCash;
            var item = Item("marked_coin", 35);

            Assert.IsFalse(shop.TryBuyItem(item));
            Assert.IsFalse(inventory.Has(item));
            Assert.AreEqual(cashBefore, economy.PlayerTotalBankedCash);
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run `<FILTER>` = `Meniscus.Tests.EditMode.ShopManagerTests`.
Expected: FAIL — assertions fail (today `TryBuyItem` applies the effect and never grants; `inventory.Has(item)` is false).

- [ ] **Step 3: Swap ShopManager's dead glass reference for a PlayerInventory reference**

`glassManager` was only used to feed `ItemEffectApplier.Apply` (removed in Step 4), so it goes dead — remove it and add `playerInventory` in its place.

In `Assets/Scripts/UI/ShopManager.cs`, replace the field declaration:

```csharp
        [SerializeField] GlassManager glassManager;
```

with:

```csharp
        [SerializeField] PlayerInventory playerInventory;
```

In `Configure`, drop the `glass` parameter and its assignment. Change the signature:

```csharp
        public void Configure(
            Canvas canvas,
            EconomyManager economy,
            GameManager manager,
            GlassManager glass = null)
        {
            shopCanvas = canvas;
            economyManager = economy;
            gameManager = manager;
            glassManager = glass;
            HideShop();
        }
```

to:

```csharp
        public void Configure(
            Canvas canvas,
            EconomyManager economy,
            GameManager manager)
        {
            shopCanvas = canvas;
            economyManager = economy;
            gameManager = manager;
            HideShop();
        }
```

(The sole caller, `GameManager.cs:~627` `shopManager.Configure(null, economyManager, this);`, passes three arguments and still compiles.)

In `ResolveReferences()`, replace the `glassManager` resolution:

```csharp
            if (glassManager == null)
                glassManager = FindAnyObjectByType<GlassManager>();
```

with the inventory resolution:

```csharp
            if (playerInventory == null && gameManager != null)
                playerInventory = gameManager.Inventory;

            if (playerInventory == null)
                playerInventory = FindAnyObjectByType<PlayerInventory>();
```

- [ ] **Step 4: Rework `TryBuyItem` to grant instead of apply**

Replace the body of `TryBuyItem`:

```csharp
        public bool TryBuyItem(ItemDefinition item)
        {
            ResolveReferences();

            if (item == null)
            {
                Debug.LogWarning("[ShopManager] Ignored purchase of a null item.");
                return false;
            }

            if (playerInventory == null)
            {
                Debug.LogWarning($"[ShopManager] Cannot buy {item.DisplayName}: PlayerInventory is missing.");
                return false;
            }

            if (playerInventory.IsFull)
            {
                Debug.LogWarning(
                    $"[ShopManager] Desk is full ({playerInventory.TotalCount}/{GameConstants.DeskCapacity}); " +
                    $"cannot buy {item.DisplayName}.");
                return false;
            }

            if (!TrySpend(item.DisplayName, item.Cost))
                return false;

            playerInventory.Grant(item);
            return true;
        }
```

(`ItemEffectApplier.Apply` is no longer called here — items now apply when *used*, in `GameManager.TryUseItem`. With the `glassManager` field removed in Step 3, the `using Meniscus.Core;`/`GlassManager` reference in this file may now be unused; leave the `using` directives untouched unless the compiler warns.)

- [ ] **Step 5: Run the test to verify it passes**

Run `<FILTER>` = `Meniscus.Tests.EditMode.ShopManagerTests`.
Expected: PASS (exit 0).

- [ ] **Step 6: Commit**

```bash
git commit -m "feat(shop): buy grants item to desk inventory instead of applying" -- \
  Assets/Scripts/UI/ShopManager.cs \
  Assets/Tests/EditMode/ShopManagerTests.cs
```

---

### Task 6: Catalog entries for the two new items

Adds "Buy the House a Round" and "Step Outside" to the code-built default catalog so they appear in the shop with no scene wiring.

**Files:**
- Modify: `Assets/Scripts/Items/ShopCatalog.cs`
- Test: `Assets/Tests/EditMode/ShopCatalogTests.cs` (append)

**Interfaces:**
- Consumes: `ItemDefinition.Create(...)`, `ItemEffectKind.ReduceCurrentRisk`, `ItemEffectKind.SkipTurn`.
- Produces: `ShopCatalog.CreateDefaultCatalog()` includes both new items.

- [ ] **Step 1: Write the failing tests**

Append to the `ShopCatalogTests` class in `Assets/Tests/EditMode/ShopCatalogTests.cs` (it already has a `static ItemDefinition Find(List<ItemDefinition>, string)` helper — reuse it):

```csharp
        [Test]
        public void DefaultCatalog_ContainsBuyTheHouseARound()
        {
            var item = Find(ShopCatalog.CreateDefaultCatalog(), "buy_the_house_a_round");

            Assert.IsNotNull(item);
            Assert.AreEqual(ItemEffectKind.ReduceCurrentRisk, item.Effect);
            Assert.AreEqual(45, item.Cost);
            Assert.AreEqual(15f, item.Magnitude, 0.001f);
        }

        [Test]
        public void DefaultCatalog_ContainsStepOutside()
        {
            var item = Find(ShopCatalog.CreateDefaultCatalog(), "step_outside");

            Assert.IsNotNull(item);
            Assert.AreEqual(ItemEffectKind.SkipTurn, item.Effect);
            Assert.AreEqual(40, item.Cost);
        }
```

- [ ] **Step 2: Run the test to verify it fails**

Run `<FILTER>` = `Meniscus.Tests.EditMode.ShopCatalogTests`.
Expected: FAIL — `Assert.IsNotNull` fails (the items are not in the catalog yet).

- [ ] **Step 3: Add the two catalog entries**

In `Assets/Scripts/Items/ShopCatalog.cs`, add these entries to the list returned by `CreateDefaultCatalog()` (after the `bartenders_spectacles` entry, before the closing `};`):

```csharp
                ItemDefinition.Create(
                    "buy_the_house_a_round", "Buy the House a Round",
                    "Lowers the glass right now, easing the danger before you pour.",
                    45, ItemEffectKind.ReduceCurrentRisk, 15f),
                ItemDefinition.Create(
                    "step_outside", "Step Outside",
                    "End your turn without pouring; pass the loaded glass to the dealer.",
                    40, ItemEffectKind.SkipTurn, 0f),
```

- [ ] **Step 4: Run the test to verify it passes**

Run `<FILTER>` = `Meniscus.Tests.EditMode.ShopCatalogTests`.
Expected: PASS (exit 0).

- [ ] **Step 5: Commit**

```bash
git commit -m "feat(items): add Buy the House a Round and Step Outside to catalog" -- \
  Assets/Scripts/Items/ShopCatalog.cs \
  Assets/Tests/EditMode/ShopCatalogTests.cs
```

---

### Task 7: DeskItemBar UI + GameManager bootstrap

A runtime-built on-screen bar showing the held items during the player's turn; clicking one uses it. Built procedurally like `SaloonHudController` / the `ShopManager` fallback canvas. **Verified manually in Play mode** (runtime UI, consistent with the project's other procedural UI which is not unit-tested), plus a small bootstrap guard test.

**Files:**
- Create: `Assets/Scripts/UI/DeskItemBar.cs`
- Modify: `Assets/Scripts/Core/GameManager.cs`
- Test: `Assets/Tests/EditMode/GameFlowTests.cs` (append bootstrap guard test)

**Interfaces:**
- Consumes: `GameManager.StateChanged`, `GameManager.CurrentState`, `GameManager.TryUseItem` (Task 3), `PlayerInventory.Changed` / `Contents()` / `TotalCount` (Task 2), `RuntimeUiFactory`.
- Produces: `DeskItemBar` MonoBehaviour with `void Configure(GameManager, PlayerInventory)`; `GameManager` ensures one exists in `ResolveReferences`.

- [ ] **Step 1: Write the failing bootstrap test**

Append to `GameFlowTests`:

```csharp
        [Test]
        public void StartMatch_BootstrapsDeskItemBarWiredToInventory()
        {
            var fixture = CreateGameFixture();
            fixture.GameManager.StartMatch();

            var bar = Object.FindAnyObjectByType<DeskItemBar>();

            Assert.IsNotNull(bar);
            Assert.IsNotNull(fixture.GameManager.Inventory);

            fixture.Destroy();
        }
```

Add `using Meniscus.UI;` to the `GameFlowTests` usings if not already present.

- [ ] **Step 2: Run the test to verify it fails**

Run `<FILTER>` = `Meniscus.Tests.EditMode.GameFlowTests.StartMatch_BootstrapsDeskItemBarWiredToInventory`.
Expected: FAIL — compile error, `DeskItemBar` not found.

- [ ] **Step 3: Create `DeskItemBar`**

Create `Assets/Scripts/UI/DeskItemBar.cs`:

```csharp
using Meniscus.Core;
using Meniscus.Items;
using UnityEngine;
using UnityEngine.UI;

namespace Meniscus.UI
{
    /// <summary>
    /// On-screen bar of the player's held desk items. Visible only during the player's turn; each
    /// entry is a button that uses one of that item via <see cref="GameManager.TryUseItem"/>. Built
    /// procedurally at runtime (like the saloon HUD) so a diegetic desk presentation can replace it
    /// later without touching the use flow.
    /// </summary>
    [DisallowMultipleComponent]
    public class DeskItemBar : MonoBehaviour
    {
        [SerializeField] GameManager gameManager;
        [SerializeField] PlayerInventory inventory;
        [SerializeField] Canvas barCanvas;

        Transform rowRoot;
        bool subscribed;

        void Awake()
        {
            ResolveReferences();
            EnsureCanvas();
        }

        void OnEnable()
        {
            Subscribe();
            Rebuild();
        }

        void OnDisable()
        {
            Unsubscribe();
        }

        public void Configure(GameManager manager, PlayerInventory playerInventory)
        {
            gameManager = manager;
            inventory = playerInventory;
            EnsureCanvas();
            Subscribe();
            Rebuild();
        }

        void Subscribe()
        {
            if (subscribed)
                return;

            ResolveReferences();

            if (gameManager != null)
                gameManager.StateChanged += OnStateChanged;

            if (inventory != null)
                inventory.Changed += Rebuild;

            subscribed = gameManager != null || inventory != null;
        }

        void Unsubscribe()
        {
            if (gameManager != null)
                gameManager.StateChanged -= OnStateChanged;

            if (inventory != null)
                inventory.Changed -= Rebuild;

            subscribed = false;
        }

        void OnStateChanged(GameState state) => Rebuild();

        void Rebuild()
        {
            ResolveReferences();
            EnsureCanvas();

            if (rowRoot == null || barCanvas == null)
                return;

            for (var i = rowRoot.childCount - 1; i >= 0; i--)
                Destroy(rowRoot.GetChild(i).gameObject);

            var isPlayerTurn = gameManager != null && gameManager.CurrentState == GameState.PlayerTurn;
            barCanvas.enabled = isPlayerTurn && inventory != null && inventory.TotalCount > 0;

            if (!barCanvas.enabled)
                return;

            var contents = inventory.Contents();
            const float buttonWidth = 220f;
            const float spacing = 12f;
            var step = buttonWidth + spacing;
            var startX = -(contents.Count - 1) * 0.5f * step;

            for (var i = 0; i < contents.Count; i++)
            {
                var stack = contents[i];
                var captured = stack.Item;
                var label = $"{stack.Item.DisplayName.ToUpperInvariant()}  x{stack.Count}";

                RuntimeUiFactory.CreateButton(
                    rowRoot,
                    $"{stack.Item.Id} Use",
                    label,
                    new Vector2(buttonWidth, 64f),
                    new Vector2(startX + i * step, 0f),
                    16,
                    () => OnUse(captured));
            }
        }

        void OnUse(ItemDefinition item)
        {
            // TryUseItem consumes from the inventory, whose Changed event re-runs Rebuild.
            if (gameManager != null)
                gameManager.TryUseItem(item);
        }

        void EnsureCanvas()
        {
            if (barCanvas != null && rowRoot != null)
                return;

            barCanvas = RuntimeUiFactory.CreateOverlayCanvas(transform, "Desk Item Bar Canvas", enabled: false);

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

            rowRoot = row.transform;
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

- [ ] **Step 4: Bootstrap the bar from GameManager**

In `Assets/Scripts/Core/GameManager.cs`, add a field beside the other UI refs:

```csharp
        [SerializeField] DeskItemBar deskItemBar;
```

`DeskItemBar` is in `Meniscus.UI` (already imported via `using Meniscus.UI;`). In `ResolveReferences()`, after the `playerInventory` resolution (Task 3) and near the `saloonHudController` bootstrap, add:

```csharp
            if (deskItemBar == null)
                deskItemBar = FindAnyObjectByType<DeskItemBar>();

            if (deskItemBar == null)
            {
                deskItemBar = gameObject.AddComponent<DeskItemBar>();
                deskItemBar.Configure(this, playerInventory);
            }
```

- [ ] **Step 5: Run the bootstrap test to verify it passes**

Run `<FILTER>` = `Meniscus.Tests.EditMode.GameFlowTests.StartMatch_BootstrapsDeskItemBarWiredToInventory`.
Expected: PASS (exit 0).

- [ ] **Step 6: Commit**

```bash
git commit -m "feat(ui): desk item bar, click-to-use during player turn" -- \
  Assets/Scripts/UI/DeskItemBar.cs \
  Assets/Scripts/Core/GameManager.cs \
  Assets/Tests/EditMode/GameFlowTests.cs
```

---

## Final verification

- [ ] **Run the full EditMode suite.** Run `<FILTER>` = `Meniscus.Tests.EditMode`. All tests added/changed by this plan are green. (Pre-existing coroutine-driven `GameFlowTests` failures, if any, are the unrelated in-flight presentation issue — confirm they are unchanged from the pre-task baseline rather than newly broken by your work.)

- [ ] **PlayMode / manual** (open the project in Unity, enter Play mode):
  1. Win a round to reach the shop. Buy several items (and confirm a purchase is refused once the desk hits 8). Banked cash drops by each item's cost.
  2. Finish the shop. Next round, the desk item bar shows your purchases with `x<count>`, and the items persisted across the round boundary.
  3. On your turn, click **Steady Hand** then pour — the pour's spill chance is lower and the item is gone from the bar. Buy/use two relief items and confirm they **sum**.
  4. Use **Buy the House a Round** on a partly-full glass — the fill drops by 15 immediately.
  5. Use **Step Outside** — your turn ends with no pour and the dealer plays.
  6. Use **Round for the Dealer** — the dealer busts more readily on his next pour; your own odds are unchanged.
  7. Use **Bartender's Spectacles** — the HUD shows the `Spill NN%` suffix this round.
  8. The bar is hidden during the dealer's turn, the shop phase, and the end screen.

## Notes for the implementer

- New `.cs` files generate `.meta` files when Unity imports the project (the CLI test run imports it). Add the generated `.meta` alongside each new `.cs` in that file's commit so the repo stays consistent.
- If a quoted anchor has shifted (parallel edits to `GameManager.cs` / `GlassManager.cs` are possible — they show as modified in the working tree), re-read the file and adapt the insertion point. The *intent* of each step (which member to add, what it does) is the contract, not the exact surrounding lines.
- Do not touch the staged file deletions in the index; commit only the explicit paths each step names.
