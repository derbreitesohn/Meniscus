# Item System v1 (Shop Expansion) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add two new shop effects — "Round for the Dealer" (makes the dealer overflow more easily) and "Bartender's Spectacles" (reveals the true spill odds next round) — to the existing data-driven between-rounds shop.

**Architecture:** Extend the existing `Assets/Scripts/Items/` data layer. Each new behavior is a new `ItemEffectKind` value, a routing arm in `ItemEffectApplier`, a manager hook on `GlassManager`, and a catalog entry. The only structural change is making the spill **relief** actor-aware in `GlassManager.DropCoins` so a penalty can target the dealer only. No inventory, no click-to-use; effects are bought between rounds and auto-apply to the next round (consistent with the current shop).

**Tech Stack:** Unity 6000.3.6f1, C#, Unity Test Framework (NUnit) EditMode tests in the `Meniscus.Tests.EditMode` assembly.

## Global Constraints

- **Spill model (do not change):** `GlassManager.CalculateTrueSpillChance(risk, relief) = MaxSpillChance * (1 - e^(-load²))`, `load = (clamp(risk,0,100) - clamp(relief,0,100)) / (MaxOverflowProbability * 0.5)`. Constants: `MaxOverflowProbability = 100`, `MaxSpillChance = 50`, `SpillSafeZoneThreshold = 0`. `relief` is a subtractive discount on the fill.
- **Effects are next-round buffs:** queued on purchase, promoted in `GlassManager.ResetGlass`, last exactly one round. Numeric queues take `Mathf.Max` (strongest wins, no stacking).
- **Enum append-only:** add new `ItemEffectKind` values at the END of the enum. Unity serializes enums by integer index; inserting in the middle would corrupt any saved `ItemDefinition` assets.
- **Namespaces:** runtime code under `Meniscus.*` (asmdef `Meniscus.Runtime`); tests under `Meniscus.Tests.EditMode` (asmdef `Meniscus.Tests.EditMode`, references `Meniscus.Runtime`).
- **Parallel-work caution:** `GlassManager.cs`, `GameConstants.cs`, and the test files are being edited in parallel by another process. Before editing, re-open each target file and confirm the anchor code still matches what this plan quotes; line numbers may have shifted. **Stage only the files named in each task — never `git add -A`** — to avoid sweeping in unrelated parallel changes.
- **Branch first:** the repo is on `main` with a large pile of unrelated uncommitted work. Create a branch before the first commit: `git switch -c feature/item-system-v1`.

## Running tests

EditMode tests run from the command line (the Unity Editor must be **closed** for the project, or it will hold the project lock):

```bash
"C:\Program Files\Unity\Hub\Editor\6000.3.6f1\Editor\Unity.exe" -runTests -batchmode \
  -projectPath "C:\Users\Huti\Desktop\Git\Meniscus" \
  -testPlatform EditMode \
  -testFilter "<FILTER>" \
  -testResults "C:\Users\Huti\AppData\Local\Temp\claude\C--Users-Huti-Desktop-Git-Meniscus\99e71642-a728-4205-9797-ec48828dd463\scratchpad\test-results.xml" \
  -logFile -
```

- Replace `<FILTER>` with a fully-qualified test name or class (e.g. `Meniscus.Tests.EditMode.GlassManagerTests`).
- **Exit code 0** = all selected tests passed. **Non-zero** = a test failed OR the test assembly failed to compile.
- **TDD note (statically typed):** when a test references a method/field that does not exist yet, the *failing* state is a **compilation error** in the run log (e.g. `'GlassManager' does not contain a definition for 'QueueNextRoundEnemySafeZonePenalty'`), not a runtime assertion failure. That red state is the expected "test fails" outcome.
- The run takes ~1–3 minutes (domain reload). For faster local iteration you may instead use the in-editor **Window ▸ General ▸ Test Runner ▸ EditMode** and Run Selected; the CLI is the source of truth for the checkbox steps.

---

### Task 1: Actor-aware relief + enemy safe-zone penalty (GlassManager)

Makes the dealer overflow more easily next round via a negative relief that applies only to the enemy, and stops the player's safe-zone bonus from leaking to the enemy.

**Files:**
- Modify: `Assets/Scripts/Core/GlassManager.cs`
- Test: `Assets/Tests/EditMode/GlassManagerTests.cs` (append to existing file)

**Interfaces:**
- Consumes: `GameConstants.SpillSafeZoneThreshold`, `GameConstants.MaxOverflowProbability`; `TurnActor` enum (`Player`, `Enemy`); existing `GlassManager.ResetGlass()`, `SpillRollProvider`, `CalculateTrueSpillChance(float, float)`.
- Produces:
  - `void GlassManager.QueueNextRoundEnemySafeZonePenalty(float penalty)`
  - `float GlassManager.GetSafeZoneRelief(TurnActor actor)`
  - Behavior: `DropCoins(coins, actor)` resolves spill with `GetSafeZoneRelief(actor)`.

- [ ] **Step 1: Write the failing tests**

Append these tests to `Assets/Tests/EditMode/GlassManagerTests.cs` (inside the `GlassManagerTests` class, above the `static Coin CreateCoin(...)` helper — they reuse that helper and the `glassManager` field from `[SetUp]`):

```csharp
        [Test]
        public void GetSafeZoneRelief_Player_UsesSafeZoneBonus_Enemy_UsesNegativePenalty()
        {
            glassManager.QueueNextRoundSafeZoneBonus(10f);
            glassManager.QueueNextRoundEnemySafeZonePenalty(12f);
            glassManager.ResetGlass();

            Assert.AreEqual(10f, glassManager.GetSafeZoneRelief(TurnActor.Player), 0.001f);
            Assert.AreEqual(-12f, glassManager.GetSafeZoneRelief(TurnActor.Enemy), 0.001f);
        }

        [Test]
        public void CalculateTrueSpillChance_NegativeRelief_RaisesChanceAboveZeroRelief()
        {
            var atZeroRelief = GlassManager.CalculateTrueSpillChance(50f, 0f);
            var atNegativeRelief = GlassManager.CalculateTrueSpillChance(50f, -12f);

            Assert.Greater(atNegativeRelief, atZeroRelief);
        }

        [Test]
        public void DropCoins_DrunkEnemy_SpillsWhereSoberPlayerWouldNot()
        {
            glassManager.QueueNextRoundEnemySafeZonePenalty(12f);
            glassManager.ResetGlass();

            // A roll that lands between the player's (lower) and the drunk enemy's (higher) spill chance.
            glassManager.SpillRollProvider = () => 36f;

            var enemyCoin = CreateCoin("Enemy Fill", 50f, 10, false);
            var enemyResult = glassManager.DropCoins(new[] { enemyCoin }, TurnActor.Enemy);

            Assert.IsTrue(enemyResult.Overflowed);                 // drunk enemy busts
            Assert.Greater(enemyResult.TrueSpillChance, GlassManager.CalculateTrueSpillChance(50f, 0f));

            glassManager.ResetGlass();                             // penalty expires
            glassManager.QueueNextRoundEnemySafeZonePenalty(0f);   // no-op, keep relief at 0
            glassManager.ResetGlass();
            glassManager.SpillRollProvider = () => 36f;

            var playerCoin = CreateCoin("Player Fill", 50f, 10, true);
            var playerResult = glassManager.DropCoins(new[] { playerCoin }, TurnActor.Player);

            Assert.IsFalse(playerResult.Overflowed);               // sober player at the same fill is safe

            Object.DestroyImmediate(enemyCoin.gameObject);
            Object.DestroyImmediate(playerCoin.gameObject);
        }

        [Test]
        public void EnemySafeZonePenalty_ExpiresAfterOneReset()
        {
            glassManager.QueueNextRoundEnemySafeZonePenalty(12f);
            glassManager.ResetGlass();
            Assert.AreEqual(-12f, glassManager.GetSafeZoneRelief(TurnActor.Enemy), 0.001f);

            glassManager.ResetGlass();
            Assert.AreEqual(0f, glassManager.GetSafeZoneRelief(TurnActor.Enemy), 0.001f);
        }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run the test command with `<FILTER>` = `Meniscus.Tests.EditMode.GlassManagerTests`.
Expected: FAIL — compilation error, `'GlassManager' does not contain a definition for 'QueueNextRoundEnemySafeZonePenalty'` (and `GetSafeZoneRelief`).

- [ ] **Step 3: Add the penalty fields and queue method**

In `Assets/Scripts/Core/GlassManager.cs`, add fields next to the existing safe-zone bonus fields (after `activeSafeZoneBonus`):

```csharp
        [SerializeField, Min(0f)] float pendingNextRoundEnemySafeZonePenalty;
        [SerializeField, Min(0f)] float activeEnemySafeZonePenalty;
```

Add the queue method next to `QueueNextRoundSafeZoneBonus`:

```csharp
        public void QueueNextRoundEnemySafeZonePenalty(float penalty)
        {
            var clampedPenalty = Mathf.Clamp(penalty, 0f, GameConstants.MaxOverflowProbability);
            pendingNextRoundEnemySafeZonePenalty =
                Mathf.Max(pendingNextRoundEnemySafeZonePenalty, clampedPenalty);
        }
```

- [ ] **Step 4: Promote the penalty in ResetGlass**

In `ResetGlass()`, add the promote/clear lines alongside the existing bonus promotion:

```csharp
        public void ResetGlass()
        {
            activeSafeZoneBonus = pendingNextRoundSafeZoneBonus;
            pendingNextRoundSafeZoneBonus = 0f;
            activeEnemySafeZonePenalty = pendingNextRoundEnemySafeZonePenalty;
            pendingNextRoundEnemySafeZonePenalty = 0f;
            currentOverflowProbability = 0f;
            ProbabilityChanged?.Invoke(currentOverflowProbability);
        }
```

- [ ] **Step 5: Add GetSafeZoneRelief and route DropCoins through it**

Add the accessor (place it near `CurrentSafeZoneThreshold`):

```csharp
        public float GetSafeZoneRelief(TurnActor actor) =>
            actor == TurnActor.Enemy
                ? GameConstants.SpillSafeZoneThreshold - activeEnemySafeZonePenalty
                : GameConstants.SpillSafeZoneThreshold + activeSafeZoneBonus;
```

In `DropCoins`, replace the spill-chance line so it uses the actor's relief. Change:

```csharp
            var trueSpillChance = CalculateCurrentTrueSpillChance(currentOverflowProbability);
```

to:

```csharp
            var trueSpillChance = CalculateTrueSpillChance(
                currentOverflowProbability, GetSafeZoneRelief(actor));
```

- [ ] **Step 6: Relax the relief clamp so a negative relief bites**

In the static `CalculateTrueSpillChance(float totalRiskWeight, float safeZoneRelief)`, change the relief clamp lower bound from `0f` to `-GameConstants.MaxOverflowProbability`:

```csharp
            var clampedRelief = Mathf.Clamp(
                safeZoneRelief,
                -GameConstants.MaxOverflowProbability,
                GameConstants.MaxOverflowProbability);
```

(`clampedRisk` and the rest of the method are unchanged. Positive-relief callers and existing tests are unaffected.)

- [ ] **Step 7: Run the tests to verify they pass**

Run the test command with `<FILTER>` = `Meniscus.Tests.EditMode.GlassManagerTests`.
Expected: PASS (exit code 0) — the four new tests plus all pre-existing `GlassManagerTests` stay green.

- [ ] **Step 8: Commit**

```bash
git switch -c feature/item-system-v1   # first commit only; skip if branch already exists
git add Assets/Scripts/Core/GlassManager.cs Assets/Tests/EditMode/GlassManagerTests.cs
git commit -m "feat(glass): actor-aware spill relief + enemy safe-zone penalty"
```

---

### Task 2: Reveal-true-odds flag (GlassManager)

Adds a one-round flag that says "show the true spill odds this round," queued by a shop item.

**Files:**
- Modify: `Assets/Scripts/Core/GlassManager.cs`
- Test: `Assets/Tests/EditMode/GlassManagerTests.cs` (append)

**Interfaces:**
- Consumes: existing `ResetGlass()`.
- Produces:
  - `void GlassManager.QueueNextRoundRevealTrueOdds()`
  - `bool GlassManager.TrueOddsRevealed { get; }`

- [ ] **Step 1: Write the failing test**

Append to `GlassManagerTests` class:

```csharp
        [Test]
        public void QueueNextRoundRevealTrueOdds_RevealsForOneRoundThenExpires()
        {
            Assert.IsFalse(glassManager.TrueOddsRevealed);

            glassManager.QueueNextRoundRevealTrueOdds();
            Assert.IsFalse(glassManager.TrueOddsRevealed);   // pending, not yet active

            glassManager.ResetGlass();
            Assert.IsTrue(glassManager.TrueOddsRevealed);    // active this round

            glassManager.ResetGlass();
            Assert.IsFalse(glassManager.TrueOddsRevealed);   // expired
        }
```

- [ ] **Step 2: Run the test to verify it fails**

Run with `<FILTER>` = `Meniscus.Tests.EditMode.GlassManagerTests.QueueNextRoundRevealTrueOdds_RevealsForOneRoundThenExpires`.
Expected: FAIL — compilation error, `'GlassManager' does not contain a definition for 'TrueOddsRevealed'`.

- [ ] **Step 3: Add the reveal fields, queue method, and accessor**

In `GlassManager.cs`, add fields beside the penalty fields:

```csharp
        [SerializeField] bool pendingNextRoundRevealTrueOdds;
        [SerializeField] bool activeRevealTrueOdds;
```

Add the accessor near the other public properties:

```csharp
        public bool TrueOddsRevealed => activeRevealTrueOdds;
```

Add the queue method near the other `Queue…` methods:

```csharp
        public void QueueNextRoundRevealTrueOdds() => pendingNextRoundRevealTrueOdds = true;
```

- [ ] **Step 4: Promote the flag in ResetGlass**

Add to `ResetGlass()` (alongside the other promotions, before `currentOverflowProbability = 0f;`):

```csharp
            activeRevealTrueOdds = pendingNextRoundRevealTrueOdds;
            pendingNextRoundRevealTrueOdds = false;
```

- [ ] **Step 5: Run the test to verify it passes**

Run with `<FILTER>` = `Meniscus.Tests.EditMode.GlassManagerTests`.
Expected: PASS (exit code 0).

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Core/GlassManager.cs Assets/Tests/EditMode/GlassManagerTests.cs
git commit -m "feat(glass): one-round reveal-true-odds flag"
```

---

### Task 3: HUD shows true odds when revealed (SaloonHudController)

When the reveal flag is active, the status line gains a `Spill NN%` suffix.

**Files:**
- Modify: `Assets/Scripts/UI/SaloonHudController.cs`
- Create: `Assets/Tests/EditMode/SaloonHudControllerTests.cs`

**Interfaces:**
- Consumes: `GlassManager.TrueOddsRevealed`, `GlassManager.CurrentTrueSpillChance` (existing).
- Produces: extended pure function
  `string SaloonHudController.BuildStatusLine(GameState, int currentRound, int totalRounds, float totalRiskWeight, int roundEarnings, int bankedCash, int playerCoinCount, int enemyCoinCount, bool revealTrueOdds = false, float trueSpillChance = 0f)`.

- [ ] **Step 1: Write the failing test**

Create `Assets/Tests/EditMode/SaloonHudControllerTests.cs`:

```csharp
using Meniscus.Core;
using Meniscus.UI;
using NUnit.Framework;

namespace Meniscus.Tests.EditMode
{
    public class SaloonHudControllerTests
    {
        [Test]
        public void BuildStatusLine_WithoutReveal_HasNoSpillSuffix()
        {
            var line = SaloonHudController.BuildStatusLine(
                GameState.PlayerTurn, 1, 3, 20f, 0, 0, 8, 8);

            Assert.IsFalse(line.Contains("Spill"));
        }

        [Test]
        public void BuildStatusLine_WithReveal_AppendsSpillPercent()
        {
            var line = SaloonHudController.BuildStatusLine(
                GameState.PlayerTurn, 1, 3, 20f, 0, 0, 8, 8,
                revealTrueOdds: true, trueSpillChance: 23.4f);

            Assert.IsTrue(line.Contains("Spill 23%"));
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run with `<FILTER>` = `Meniscus.Tests.EditMode.SaloonHudControllerTests`.
Expected: FAIL — compilation error, `BuildStatusLine` does not take 10 arguments / no overload.

- [ ] **Step 3: Extend BuildStatusLine and pass the new values from Refresh**

In `SaloonHudController.cs`, change the `BuildStatusLine` signature and body:

```csharp
        public static string BuildStatusLine(
            GameState state,
            int currentRound,
            int totalRounds,
            float totalRiskWeight,
            int roundEarnings,
            int bankedCash,
            int playerCoinCount,
            int enemyCoinCount,
            bool revealTrueOdds = false,
            float trueSpillChance = 0f)
        {
            var riskLabel = totalRiskWeight < 30f
                ? "Steady"
                : totalRiskWeight < 60f
                    ? "Risk Building"
                    : totalRiskWeight < 85f
                        ? "Danger"
                        : "Critical";

            var spillSuffix = revealTrueOdds ? $"   Spill {trueSpillChance:0}%" : string.Empty;

            return
                $"Round {currentRound}/{totalRounds}   {state}   {riskLabel} {totalRiskWeight:0}%   " +
                $"Round ${roundEarnings}   Bank ${bankedCash}   You {playerCoinCount} / Dealer {enemyCoinCount}" +
                spillSuffix;
        }
```

In `Refresh()`, pass the two new arguments at the end of the `BuildStatusLine(...)` call:

```csharp
                gameManager.EnemyCoins.Count,
                glassManager != null && glassManager.TrueOddsRevealed,
                glassManager == null ? 0f : glassManager.CurrentTrueSpillChance);
```

(The first eight arguments are unchanged; append the two lines above where the call currently ends with `gameManager.EnemyCoins.Count);`.)

- [ ] **Step 4: Run the test to verify it passes**

Run with `<FILTER>` = `Meniscus.Tests.EditMode.SaloonHudControllerTests`.
Expected: PASS (exit code 0).

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/UI/SaloonHudController.cs Assets/Tests/EditMode/SaloonHudControllerTests.cs
git commit -m "feat(hud): show true spill odds when revealed"
```

---

### Task 4: New effect kinds + applier routing (Items)

Adds the two `ItemEffectKind` values and routes them to the Task 1 / Task 2 hooks.

**Files:**
- Modify: `Assets/Scripts/Items/ItemEffectKind.cs`
- Modify: `Assets/Scripts/Items/ItemEffectApplier.cs`
- Create: `Assets/Tests/EditMode/ItemEffectApplierTests.cs`

**Interfaces:**
- Consumes: `GlassManager.QueueNextRoundEnemySafeZonePenalty(float)`, `GlassManager.QueueNextRoundRevealTrueOdds()`, `GlassManager.GetSafeZoneRelief(TurnActor)`, `GlassManager.TrueOddsRevealed`, `GlassManager.ResetGlass()`; `ItemDefinition.Create(id, displayName, description, cost, effect, magnitude)`.
- Produces: `ItemEffectKind.EnemySafeZonePenalty`, `ItemEffectKind.RevealTrueOdds`; `ItemEffectApplier.Apply` handles both.

- [ ] **Step 1: Write the failing test**

Create `Assets/Tests/EditMode/ItemEffectApplierTests.cs`. The applier only needs `GlassManager` for these two kinds, so pass `null` for economy/game:

```csharp
using Meniscus.Core;
using Meniscus.Items;
using NUnit.Framework;
using UnityEngine;

namespace Meniscus.Tests.EditMode
{
    public class ItemEffectApplierTests
    {
        GameObject glassObject;
        GlassManager glassManager;

        [SetUp]
        public void SetUp()
        {
            glassObject = new GameObject("GlassManager Test Host");
            glassManager = glassObject.AddComponent<GlassManager>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(glassObject);
        }

        [Test]
        public void Apply_EnemySafeZonePenalty_QueuesNegativeEnemyRelief()
        {
            var item = ItemDefinition.Create(
                "round_for_the_dealer", "Round for the Dealer", "",
                55, ItemEffectKind.EnemySafeZonePenalty, 12f);

            ItemEffectApplier.Apply(item, null, glassManager, null);
            glassManager.ResetGlass();

            Assert.AreEqual(-12f, glassManager.GetSafeZoneRelief(TurnActor.Enemy), 0.001f);
        }

        [Test]
        public void Apply_RevealTrueOdds_QueuesRevealForNextRound()
        {
            var item = ItemDefinition.Create(
                "bartenders_spectacles", "Bartender's Spectacles", "",
                30, ItemEffectKind.RevealTrueOdds, 0f);

            ItemEffectApplier.Apply(item, null, glassManager, null);
            glassManager.ResetGlass();

            Assert.IsTrue(glassManager.TrueOddsRevealed);
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run with `<FILTER>` = `Meniscus.Tests.EditMode.ItemEffectApplierTests`.
Expected: FAIL — compilation error, `ItemEffectKind` does not contain `EnemySafeZonePenalty` / `RevealTrueOdds`.

- [ ] **Step 3: Add the enum values (append only)**

In `Assets/Scripts/Items/ItemEffectKind.cs`, append the two values at the END:

```csharp
    public enum ItemEffectKind
    {
        PayoutMultiplier,
        SafeZoneBonus,
        ForceEnemyCoins,
        EnemySafeZonePenalty,
        RevealTrueOdds
    }
```

- [ ] **Step 4: Add the routing arms**

In `Assets/Scripts/Items/ItemEffectApplier.cs`, add two `case` arms inside the `switch (item.Effect)` block, before the `default:` arm:

```csharp
                case ItemEffectKind.EnemySafeZonePenalty:
                    if (glass != null)
                        glass.QueueNextRoundEnemySafeZonePenalty(item.Magnitude);
                    else
                        WarnMissing(item, nameof(GlassManager));
                    break;

                case ItemEffectKind.RevealTrueOdds:
                    if (glass != null)
                        glass.QueueNextRoundRevealTrueOdds();
                    else
                        WarnMissing(item, nameof(GlassManager));
                    break;
```

- [ ] **Step 5: Run the test to verify it passes**

Run with `<FILTER>` = `Meniscus.Tests.EditMode.ItemEffectApplierTests`.
Expected: PASS (exit code 0).

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Items/ItemEffectKind.cs Assets/Scripts/Items/ItemEffectApplier.cs Assets/Tests/EditMode/ItemEffectApplierTests.cs
git commit -m "feat(items): add EnemySafeZonePenalty and RevealTrueOdds effect kinds"
```

---

### Task 5: Catalog entries (ShopCatalog)

Adds the two new items to the code-built default catalog so they appear in the shop with no scene wiring.

**Files:**
- Modify: `Assets/Scripts/Items/ShopCatalog.cs`
- Create: `Assets/Tests/EditMode/ShopCatalogTests.cs`

**Interfaces:**
- Consumes: `ItemDefinition.Create(...)`, `ItemEffectKind.EnemySafeZonePenalty`, `ItemEffectKind.RevealTrueOdds`, `ItemDefinition` accessors (`Id`, `Cost`, `Effect`, `Magnitude`).
- Produces: `ShopCatalog.CreateDefaultCatalog()` includes both new items.

- [ ] **Step 1: Write the failing test**

Create `Assets/Tests/EditMode/ShopCatalogTests.cs`:

```csharp
using System.Collections.Generic;
using Meniscus.Items;
using NUnit.Framework;

namespace Meniscus.Tests.EditMode
{
    public class ShopCatalogTests
    {
        static ItemDefinition Find(List<ItemDefinition> catalog, string id) =>
            catalog.Find(item => item != null && item.Id == id);

        [Test]
        public void DefaultCatalog_ContainsRoundForTheDealer()
        {
            var beer = Find(ShopCatalog.CreateDefaultCatalog(), "round_for_the_dealer");

            Assert.IsNotNull(beer);
            Assert.AreEqual(ItemEffectKind.EnemySafeZonePenalty, beer.Effect);
            Assert.AreEqual(55, beer.Cost);
            Assert.AreEqual(12f, beer.Magnitude, 0.001f);
        }

        [Test]
        public void DefaultCatalog_ContainsBartendersSpectacles()
        {
            var specs = Find(ShopCatalog.CreateDefaultCatalog(), "bartenders_spectacles");

            Assert.IsNotNull(specs);
            Assert.AreEqual(ItemEffectKind.RevealTrueOdds, specs.Effect);
            Assert.AreEqual(30, specs.Cost);
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run with `<FILTER>` = `Meniscus.Tests.EditMode.ShopCatalogTests`.
Expected: FAIL — both assertions fail with `Assert.IsNotNull` (the items are not in the catalog yet).

- [ ] **Step 3: Add the two catalog entries**

In `Assets/Scripts/Items/ShopCatalog.cs`, add these two entries to the list returned by `CreateDefaultCatalog()` (after the `dealers_debt` entry, before the closing `};`):

```csharp
                ItemDefinition.Create(
                    "round_for_the_dealer", "Round for the Dealer",
                    "The dealer drinks: he overflows more easily next round.",
                    55, ItemEffectKind.EnemySafeZonePenalty, 12f),
                ItemDefinition.Create(
                    "bartenders_spectacles", "Bartender's Spectacles",
                    "Reveals the true spill odds on the glass next round.",
                    30, ItemEffectKind.RevealTrueOdds, 0f),
```

- [ ] **Step 4: Run the test to verify it passes**

Run with `<FILTER>` = `Meniscus.Tests.EditMode.ShopCatalogTests`.
Expected: PASS (exit code 0).

- [ ] **Step 5: Run the full EditMode suite**

Run with `<FILTER>` = `Meniscus.Tests.EditMode` (whole assembly).
Expected: PASS (exit code 0) — all new and pre-existing tests green.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Items/ShopCatalog.cs Assets/Tests/EditMode/ShopCatalogTests.cs
git commit -m "feat(items): add Round for the Dealer and Bartender's Spectacles to catalog"
```

---

## Manual verification (after Task 5)

Open the project in Unity and enter Play mode:

1. Win a round to reach the shop. Both new items appear in the book/menu with their costs.
2. Buy **Round for the Dealer**, finish the shop. Next round, the dealer should bust noticeably more often on his pours than normal; your own pours feel unchanged.
3. Buy **Bartender's Spectacles**, finish the shop. Next round, the HUD status line shows a `Spill NN%` suffix; the round after, the suffix is gone.
4. Buy **Steady Hand**, finish the shop. Confirm the dealer's pours are *not* made safer by it (player-only relief).

## Notes for the implementer

- New `.cs` files generate `.meta` files when Unity next imports. `git add <path>.cs` will also pick up the generated `.meta` if present; if you run the CLI test command (which imports the project) before committing, the `.meta` files will exist — add them too so the repo stays consistent.
- If the parallel work has renamed or moved any quoted anchor (e.g. `Refresh()`'s `BuildStatusLine` call, or `ResetGlass`), re-read the file and adapt the insertion point; the *intent* of each step (which field/method to add, what it does) is the contract, not the exact surrounding lines.
