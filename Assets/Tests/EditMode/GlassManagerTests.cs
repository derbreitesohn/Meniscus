using Meniscus.Core;
using Meniscus.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace Meniscus.Tests.EditMode
{
    public class GlassManagerTests
    {
        GameObject glassObject;
        GlassManager glassManager;

        [SetUp]
        public void SetUp()
        {
            glassObject = new GameObject("GlassManager Test Host");
            glassManager = glassObject.AddComponent<GlassManager>();

            // Overflow is probabilistic: each pour rolls against the spill chance, and a spill is only
            // CERTAIN once the fill reaches the brim (the chance saturates at 100%). Pin the roll to the
            // max (the luckiest possible draw) so by default a pour HOLDS everywhere below the brim and the
            // outcome never depends on the RNG; individual tests drop the roll to 0 to force a spill.
            glassManager.SpillRollProvider = () => GameConstants.MaxOverflowProbability;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(glassObject);
        }

        [Test]
        public void ResetGlass_OpensTheGlassAtTheBrimStartFill()
        {
            var coin = CreateCoin("Risk Coin", 45f, 10, true);

            glassManager.DropCoins(new[] { coin }, TurnActor.Player);
            glassManager.ResetGlass();

            // The glass opens full to the brim each round, not empty: fill resets to GlassStartFill.
            Assert.AreEqual(GameConstants.GlassStartFill, glassManager.CurrentOverflowProbability);
            Object.DestroyImmediate(coin.gameObject);
        }

        [Test]
        public void SettleSurface_EasesFillBackDown_ClampedAtZero()
        {
            var coin = CreateCoin("Fill", 30f, 10, true);
            glassManager.DropCoins(new[] { coin }, TurnActor.Player);
            Assert.AreEqual(30f, glassManager.CurrentOverflowProbability, 0.001f);

            glassManager.SettleSurface();
            Assert.AreEqual(30f - GameConstants.SurfaceSettlePerTurn, glassManager.CurrentOverflowProbability, 0.001f);
            Object.DestroyImmediate(coin.gameObject);
        }

        [Test]
        public void DropCoins_WithNoCoins_DoesNotOverflowOrIncreaseRisk()
        {
            var result = glassManager.DropCoins(System.Array.Empty<Coin>(), TurnActor.Player);

            Assert.AreEqual(0f, glassManager.CurrentOverflowProbability);
            Assert.AreEqual(0f, result.RiskBeforeDrop);
            Assert.AreEqual(0f, result.AddedRisk);
            Assert.AreEqual(0f, result.RiskAfterDrop);
            Assert.IsFalse(result.Overflowed);
        }

        [Test]
        public void DropCoins_AddsCombinedRiskToProbability_LessTheComboClawBack()
        {
            var small = CreateCoin("Small Coin", 5f, 10, true);
            var large = CreateCoin("Large Coin", 15f, 30, true);

            var result = glassManager.DropCoins(new[] { small, large }, TurnActor.Player);

            // 20 of combined risk goes in, then a 2-coin combo claws back ComboRiskReliefPerExtraCoin.
            var expectedFill = 20f - GameConstants.ComboRiskReliefPerExtraCoin;
            Assert.AreEqual(expectedFill, glassManager.CurrentOverflowProbability, 0.001f);
            Assert.AreEqual(0f, result.RiskBeforeDrop);
            Assert.AreEqual(20f, result.AddedRisk);                 // AddedRisk is the gross risk poured
            Assert.AreEqual(expectedFill, result.RiskAfterDrop, 0.001f);
            Assert.AreEqual(2, result.CoinCount);

            Object.DestroyImmediate(small.gameObject);
            Object.DestroyImmediate(large.gameObject);
        }

        [Test]
        public void DropCoins_SingleCoin_GetsNoComboClawBack()
        {
            var coin = CreateCoin("Solo Coin", 20f, 10, true);

            var result = glassManager.DropCoins(new[] { coin }, TurnActor.Player);

            // One coin is not a combo: the full 20 stays on the glass.
            Assert.AreEqual(20f, glassManager.CurrentOverflowProbability, 0.001f);
            Assert.AreEqual(20f, result.RiskAfterDrop, 0.001f);

            Object.DestroyImmediate(coin.gameObject);
        }

        [Test]
        public void CalculateTrueSpillChance_IsZeroBelowSafeZone_RampsToCertainAtTheBrim()
        {
            // Below the safe zone the surface tension holds for sure.
            Assert.AreEqual(0f, GlassManager.CalculateTrueSpillChance(0f));
            Assert.AreEqual(0f, GlassManager.CalculateTrueSpillChance(GameConstants.DomeSafeZone));

            // Across the dome the chance climbs monotonically from there.
            var low = GlassManager.CalculateTrueSpillChance(GameConstants.DomeSafeZone + 5f);
            var mid = GlassManager.CalculateTrueSpillChance(
                (GameConstants.DomeSafeZone + GameConstants.DomeCapacity) * 0.5f);
            Assert.Greater(low, 0f);
            Assert.Greater(mid, low);

            // At and beyond the brim (the dome capacity) a spill is CERTAIN — push it there and it goes over.
            Assert.AreEqual(
                GameConstants.MaxOverflowProbability,
                GlassManager.CalculateTrueSpillChance(GameConstants.DomeCapacity));
            Assert.AreEqual(
                GameConstants.MaxOverflowProbability,
                GlassManager.CalculateTrueSpillChance(GameConstants.DomeCapacity + 20f));
        }

        [Test]
        public void DropCoins_InsideTheDome_HasAReadableRampSpillChance()
        {
            var coin = CreateCoin("Dome Fill Coin", 30f, 10, true);

            var result = glassManager.DropCoins(new[] { coin }, TurnActor.Player);

            Assert.AreEqual(30f, result.RiskAfterDrop);
            // fill 30 sits in the dome band: the chance ramps convexly (t^DomeRampExponent) between the
            // safe zone and the brim.
            var t = (30f - GameConstants.DomeSafeZone)
                / (GameConstants.DomeCapacity - GameConstants.DomeSafeZone);
            var expected = GameConstants.MaxOverflowProbability * Mathf.Pow(t, GameConstants.DomeRampExponent);
            Assert.AreEqual(expected, result.TrueSpillChance, 0.1f);
            Assert.IsFalse(result.Overflowed);   // below the brim, even the highest roll holds

            Object.DestroyImmediate(coin.gameObject);
        }


        [Test]
        public void DropCoins_PastTheBrim_IsACertainSpill()
        {
            var heavy = CreateCoin("Heavy Coin", 120f, 100, false);
            // Even the luckiest possible roll spills once the fill is at/over the brim — the chance has
            // saturated at 100%, so overflow is certain regardless of the roll.
            glassManager.SpillRollProvider = () => GameConstants.MaxOverflowProbability;

            var result = glassManager.DropCoins(new[] { heavy }, TurnActor.Enemy);

            Assert.AreEqual(GameConstants.MaxOverflowProbability, glassManager.CurrentOverflowProbability); // fill clamps to the max
            Assert.AreEqual(GameConstants.MaxOverflowProbability, result.TrueSpillChance);                  // certain
            Assert.AreEqual(TurnActor.Enemy, result.Actor);
            Assert.IsTrue(result.Overflowed);

            Object.DestroyImmediate(heavy.gameObject);
        }

        [Test]
        public void DropCoins_BelowTheBrim_SpillsWhenTheRollUndercutsTheChance()
        {
            // A mid-dome fill is a genuine gamble now: the same fill that HOLDS on the luckiest roll
            // (see DropCoins_InsideTheDome…) BREAKS on the unluckiest one.
            var coin = CreateCoin("Dome Fill Coin", 40f, 10, true);

            var chance = GlassManager.CalculateTrueSpillChance(40f);
            Assert.Greater(chance, 0f);
            Assert.Less(chance, GameConstants.MaxOverflowProbability);   // genuinely below the brim, not certain

            glassManager.SpillRollProvider = () => 0f;                   // the unluckiest roll
            var result = glassManager.DropCoins(new[] { coin }, TurnActor.Player);

            Assert.IsTrue(result.Overflowed);   // a low roll under the chance breaks the surface below the brim

            Object.DestroyImmediate(coin.gameObject);
        }


        [Test]
        public void CalculateTrueSpillChance_NegativeRelief_RaisesChanceAboveZeroRelief()
        {
            var atZeroRelief = GlassManager.CalculateTrueSpillChance(50f, 0f);
            var atNegativeRelief = GlassManager.CalculateTrueSpillChance(50f, -12f);

            Assert.Greater(atNegativeRelief, atZeroRelief);
        }


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
        public void DropCoins_DrunkEnemy_OverflowsAtAFillASoberPlayerWouldSurvive()
        {
            // The drunk penalty pushes the enemy's EFFECTIVE fill up the dome, so the enemy hits the brim
            // (a certain, deterministic spill) at an actual fill a sober player would still survive.
            const float actualFill = 72f;   // below the brim for a sober pour, over it once +12 is added

            // Sober player pours to the fill — effective fill is the same, still under the brim → safe.
            var playerCoin = CreateCoin("Player Fill", actualFill, 10, true);
            var playerResult = glassManager.DropCoins(new[] { playerCoin }, TurnActor.Player);
            Assert.IsFalse(playerResult.Overflowed);
            Assert.Less(playerResult.TrueSpillChance, GameConstants.MaxOverflowProbability);

            // Drain back, make the enemy drunk, and pour to the same actual fill — the penalty tips the
            // effective fill over the brim, so now it is a certain spill.
            glassManager.ReduceCurrentRisk(GameConstants.MaxOverflowProbability);
            glassManager.AddEnemyPourPenalty(12f);

            var enemyCoin = CreateCoin("Enemy Fill", actualFill, 10, false);
            var enemyResult = glassManager.DropCoins(new[] { enemyCoin }, TurnActor.Enemy);
            Assert.IsTrue(enemyResult.Overflowed);
            Assert.AreEqual(GameConstants.MaxOverflowProbability, enemyResult.TrueSpillChance);

            Object.DestroyImmediate(playerCoin.gameObject);
            Object.DestroyImmediate(enemyCoin.gameObject);
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

        static Coin CreateCoin(string name, float risk, int payout, bool isPlayerCoin)
        {
            var coinObject = new GameObject(name);
            var coin = coinObject.AddComponent<Coin>();
            coin.Configure(CoinSize.Medium, risk, payout, isPlayerCoin);
            return coin;
        }
    }
}
