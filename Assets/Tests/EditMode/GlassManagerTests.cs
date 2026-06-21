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

            // Deterministic roll so drop outcomes don't depend on the RNG. Default to the highest
            // possible roll, which never spills (the chance asymptotes below the max); tests that want
            // a forced spill override this with a low roll.
            glassManager.SpillRollProvider = () => GameConstants.MaxOverflowProbability;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(glassObject);
        }

        [Test]
        public void ResetGlass_SetsOverflowProbabilityToZero()
        {
            var coin = CreateCoin("Risk Coin", 45f, 10, true);

            glassManager.DropCoins(new[] { coin }, TurnActor.Player);
            glassManager.ResetGlass();

            Assert.AreEqual(0f, glassManager.CurrentOverflowProbability);
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
        public void DropCoins_AddsCombinedRiskToProbability()
        {
            var small = CreateCoin("Small Coin", 5f, 10, true);
            var large = CreateCoin("Large Coin", 15f, 30, true);

            var result = glassManager.DropCoins(new[] { small, large }, TurnActor.Player);

            Assert.AreEqual(20f, glassManager.CurrentOverflowProbability);
            Assert.AreEqual(0f, result.RiskBeforeDrop);
            Assert.AreEqual(20f, result.AddedRisk);
            Assert.AreEqual(20f, result.RiskAfterDrop);
            Assert.AreEqual(2, result.CoinCount);

            Object.DestroyImmediate(small.gameObject);
            Object.DestroyImmediate(large.gameObject);
        }

        [Test]
        public void CalculateTrueSpillChance_ClimbsTowardCeilingButNeverReachesIt()
        {
            // No grace period: an empty glass is safe, but even a light early pour can spill.
            Assert.AreEqual(0f, GlassManager.CalculateTrueSpillChance(0f));
            Assert.Greater(GlassManager.CalculateTrueSpillChance(10f), 0f);

            // Continuous curve: chance = MaxSpillChance * (1 - e^(-(risk/50)^2)). Small early, climbing.
            Assert.AreEqual(1.96f, GlassManager.CalculateTrueSpillChance(10f), 0.05f);
            Assert.AreEqual(15.12f, GlassManager.CalculateTrueSpillChance(30f), 0.1f);
            Assert.AreEqual(31.61f, GlassManager.CalculateTrueSpillChance(50f), 0.1f);

            // Monotonic increase.
            Assert.Greater(
                GlassManager.CalculateTrueSpillChance(80f),
                GlassManager.CalculateTrueSpillChance(50f));

            // Approaches the ceiling at a maxed meter but never reaches it — always a chance to walk away.
            var atMax = GlassManager.CalculateTrueSpillChance(GameConstants.MaxOverflowProbability);
            Assert.Greater(atMax, 0.95f * GameConstants.MaxSpillChance);
            Assert.Less(atMax, GameConstants.MaxSpillChance);
        }

        [Test]
        public void DropCoins_AtModerateRisk_HasSmallButRealSpillChance()
        {
            var coin = CreateCoin("Moderate Risk Coin", 39f, 10, true);

            var result = glassManager.DropCoins(new[] { coin }, TurnActor.Player);

            Assert.AreEqual(39f, result.RiskAfterDrop);
            Assert.Greater(result.TrueSpillChance, 0f);                 // no safe zone any more
            Assert.AreEqual(22.79f, result.TrueSpillChance, 0.1f);      // 50 * (1 - e^(-(39/50)^2))
            Assert.IsFalse(result.Overflowed);                          // highest roll didn't spill

            Object.DestroyImmediate(coin.gameObject);
        }


        [Test]
        public void DropCoins_AtMaxRisk_StaysBelowCeilingButCanSpill()
        {
            var heavy = CreateCoin("Heavy Coin", 120f, 100, false);
            glassManager.SpillRollProvider = () => 0f; // lowest roll spills whenever any chance exists

            var result = glassManager.DropCoins(new[] { heavy }, TurnActor.Enemy);

            Assert.AreEqual(100f, glassManager.CurrentOverflowProbability);   // risk clamps to the max
            Assert.Greater(result.TrueSpillChance, 0.95f * GameConstants.MaxSpillChance); // near the ceiling
            Assert.Less(result.TrueSpillChance, GameConstants.MaxSpillChance);            // but never reaches it
            Assert.AreEqual(TurnActor.Enemy, result.Actor);
            Assert.IsTrue(result.Overflowed);

            Object.DestroyImmediate(heavy.gameObject);
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

        static Coin CreateCoin(string name, float risk, int payout, bool isPlayerCoin)
        {
            var coinObject = new GameObject(name);
            var coin = coinObject.AddComponent<Coin>();
            coin.Configure(CoinSize.Medium, risk, payout, isPlayerCoin);
            return coin;
        }
    }
}
