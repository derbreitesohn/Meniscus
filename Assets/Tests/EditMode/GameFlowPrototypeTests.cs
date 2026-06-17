using System.Collections.Generic;
using Meniscus.Core;
using Meniscus.Gameplay;
using Meniscus.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Meniscus.Tests.EditMode
{
    public class GameFlowPrototypeTests
    {
        [Test]
        public void GetCoinCountForRound_GivesPlayerAtLeastEightCoinsAndScalesUp()
        {
            Assert.AreEqual(8, GameConstants.GetCoinCountForRound(1));
            Assert.AreEqual(10, GameConstants.GetCoinCountForRound(2));
            Assert.AreEqual(12, GameConstants.GetCoinCountForRound(3));
            Assert.GreaterOrEqual(GameConstants.MinCoinsPerActor, 8);
        }

        [Test]
        public void EnemyOverflowBeforeFinalRound_EntersShopPhaseThenFinishDrinkStartsNextRound()
        {
            var fixture = CreateGameFixture();

            fixture.GameManager.StartMatch();
            ForceNextPlayerDropSafe(fixture);
            fixture.GameManager.TryPlayerDropSelectedCoins(new[] { fixture.GameManager.PlayerCoins[0] });
            ForceNextEnemyDropToOverflow(fixture);
            fixture.GameManager.ExecuteEnemyDrop(new[] { fixture.GameManager.EnemyCoins[0] });

            Assert.AreEqual(1, fixture.GameManager.CurrentRound);
            Assert.AreEqual(GameState.ShopPhase, fixture.GameManager.CurrentState);
            Assert.AreEqual(MatchOutcome.None, fixture.GameManager.LastMatchOutcome);
            Assert.IsFalse(fixture.EndCanvas.enabled);

            fixture.GameManager.FinishShopPhase();

            Assert.AreEqual(2, fixture.GameManager.CurrentRound);
            Assert.AreEqual(GameState.PlayerTurn, fixture.GameManager.CurrentState);
            Assert.AreEqual(MatchOutcome.None, fixture.GameManager.LastMatchOutcome);

            fixture.Destroy();
        }

        [Test]
        public void BothHandsEmpty_TriggersRestockWithoutEndingRoundOrResettingGlass()
        {
            var fixture = CreateGameFixture();
            var observedStates = new List<GameState>();
            fixture.GameManager.StateChanged += observedStates.Add;

            fixture.GameManager.StartMatch();
            var playerHand = new List<Coin>(fixture.GameManager.PlayerCoins);
            var enemyHand = new List<Coin>(fixture.GameManager.EnemyCoins);

            for (var i = 0; i < playerHand.Count; i++)
                playerHand[i].riskContribution = i == 0 ? 39f : 0f;

            for (var i = 0; i < enemyHand.Count; i++)
                enemyHand[i].riskContribution = 0f;

            fixture.GameManager.TryPlayerDropSelectedCoins(playerHand);
            fixture.GameManager.ExecuteEnemyDrop(enemyHand);

            Assert.Contains(GameState.RestockPhase, observedStates);
            Assert.AreEqual(1, fixture.GameManager.CurrentRound);
            Assert.AreEqual(GameState.PlayerTurn, fixture.GameManager.CurrentState);
            Assert.AreEqual(39f, fixture.GlassManager.CurrentOverflowProbability);
            Assert.AreEqual(GameConstants.GetCoinCountForRound(1), fixture.GameManager.PlayerCoins.Count);
            Assert.AreEqual(GameConstants.GetCoinCountForRound(1), fixture.GameManager.EnemyCoins.Count);
            Assert.AreEqual(MatchOutcome.None, fixture.GameManager.LastMatchOutcome);

            fixture.Destroy();
        }

        [Test]
        public void PlayerOverflow_ShowsLostScreen()
        {
            var fixture = CreateGameFixture();

            fixture.GameManager.StartMatch();
            ForceNextPlayerDropToOverflow(fixture);
            fixture.GameManager.TryPlayerDropSelectedCoins(new[] { fixture.GameManager.PlayerCoins[0] });

            Assert.AreEqual(GameState.GameOver, fixture.GameManager.CurrentState);
            Assert.AreEqual(MatchOutcome.PlayerLost, fixture.GameManager.LastMatchOutcome);
            Assert.IsTrue(fixture.EndCanvas.enabled);
            Assert.AreEqual("YOU LOST", fixture.EndTitle.text);

            fixture.Destroy();
        }

        [Test]
        public void EnemyOverflowOnFinalRound_ShowsWonScreen()
        {
            var fixture = CreateGameFixture();

            fixture.GameManager.StartMatch();
            WinRoundByEnemyOverflow(fixture);
            fixture.GameManager.FinishShopPhase();
            WinRoundByEnemyOverflow(fixture);
            fixture.GameManager.FinishShopPhase();
            WinRoundByEnemyOverflow(fixture);

            Assert.AreEqual(GameState.GameOver, fixture.GameManager.CurrentState);
            Assert.AreEqual(MatchOutcome.PlayerWon, fixture.GameManager.LastMatchOutcome);
            Assert.IsTrue(fixture.EndCanvas.enabled);
            Assert.AreEqual("YOU WON", fixture.EndTitle.text);

            fixture.Destroy();
        }

        static void WinRoundByEnemyOverflow(GameFixture fixture)
        {
            ForceNextPlayerDropSafe(fixture);
            fixture.GameManager.TryPlayerDropSelectedCoins(new[] { fixture.GameManager.PlayerCoins[0] });
            ForceNextEnemyDropToOverflow(fixture);
            fixture.GameManager.ExecuteEnemyDrop(new[] { fixture.GameManager.EnemyCoins[0] });
        }

        static void ForceNextPlayerDropSafe(GameFixture fixture)
        {
            fixture.GameManager.PlayerCoins[0].riskContribution = 0f;
        }

        static void ForceNextEnemyDropSafe(GameFixture fixture)
        {
            fixture.GameManager.EnemyCoins[0].riskContribution = 0f;
        }

        static void ForceNextPlayerDropToOverflow(GameFixture fixture)
        {
            fixture.GameManager.PlayerCoins[0].riskContribution = 100f;
        }

        static void ForceNextEnemyDropToOverflow(GameFixture fixture)
        {
            fixture.GameManager.EnemyCoins[0].riskContribution = 100f;
        }

        static GameFixture CreateGameFixture()
        {
            var root = new GameObject("Game Flow Fixture");
            var glassManager = root.AddComponent<GlassManager>();
            var economyManager = root.AddComponent<EconomyManager>();
            var endScreenManager = root.AddComponent<EndScreenManager>();
            var gameManager = root.AddComponent<GameManager>();

            var canvasObject = new GameObject("End Canvas");
            canvasObject.transform.SetParent(root.transform);
            var canvas = canvasObject.AddComponent<Canvas>();
            var title = new GameObject("End Title").AddComponent<Text>();
            title.transform.SetParent(canvasObject.transform);
            var detail = new GameObject("End Detail").AddComponent<Text>();
            detail.transform.SetParent(canvasObject.transform);

            endScreenManager.Configure(canvas, title, detail);

            // GameManager resolves manager references from the scene. These explicit references make the
            // test fixture deterministic without exposing test-only hooks on production classes.
            CreateFixtureCoin("Player Coin", true).transform.SetParent(root.transform);
            CreateFixtureCoin("Enemy Coin", false).transform.SetParent(root.transform);

            return new GameFixture(root, gameManager, glassManager, economyManager, canvas, title);
        }

        static Coin CreateFixtureCoin(string name, bool playerOwned)
        {
            var coinObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            coinObject.name = name;
            var coin = coinObject.AddComponent<Coin>();
            coin.Configure(CoinSize.Small, 0f, 10, playerOwned);
            return coin;
        }

        sealed class GameFixture
        {
            readonly GameObject root;

            public GameFixture(
                GameObject root,
                GameManager gameManager,
                GlassManager glassManager,
                EconomyManager economyManager,
                Canvas endCanvas,
                Text endTitle)
            {
                this.root = root;
                GameManager = gameManager;
                GlassManager = glassManager;
                EconomyManager = economyManager;
                EndCanvas = endCanvas;
                EndTitle = endTitle;
            }

            public GameManager GameManager { get; }
            public GlassManager GlassManager { get; }
            public EconomyManager EconomyManager { get; }
            public Canvas EndCanvas { get; }
            public Text EndTitle { get; }

            public void Destroy()
            {
                Object.DestroyImmediate(root);

                var eventSystems = Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);

                for (var i = 0; i < eventSystems.Length; i++)
                    Object.DestroyImmediate(eventSystems[i].gameObject);
            }
        }
    }
}
