using Meniscus.Core;
using Meniscus.Gameplay;
using Meniscus.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Meniscus.Tests.EditMode
{
    public class PresentationPolishTests
    {
        [Test]
        public void CalculateStagedDropPosition_PausesAtRimBeforeSidewaysRelease()
        {
            var start = new Vector3(-1f, 1f, 0f);
            var hold = new Vector3(0.25f, 2.1f, 0f);
            var end = new Vector3(0f, 1.72f, 0f);

            var atStart = CoinDropPresentationController.CalculateStagedDropPosition(start, hold, end, 0f, 0.35f);
            var atHoldStart = CoinDropPresentationController.CalculateStagedDropPosition(start, hold, end, 0.58f, 0.35f);
            var duringPause = CoinDropPresentationController.CalculateStagedDropPosition(start, hold, end, 0.68f, 0.35f);
            var afterRelease = CoinDropPresentationController.CalculateStagedDropPosition(start, hold, end, 0.9f, 0.35f);
            var atEnd = CoinDropPresentationController.CalculateStagedDropPosition(start, hold, end, 1f, 0.35f);

            AssertVectorApproximately(start, atStart);
            AssertVectorApproximately(hold, atHoldStart);
            AssertVectorApproximately(hold, duringPause);
            Assert.Greater(afterRelease.x, end.x);
            Assert.Less(afterRelease.y, hold.y);
            AssertVectorApproximately(end, atEnd);
        }

        [Test]
        public void BuildStatusLine_HidesTrueSpillChanceButShowsReadableTableState()
        {
            var status = SaloonHudController.BuildStatusLine(
                GameState.PlayerTurn,
                2,
                GameConstants.TotalRounds,
                35f,
                GameConstants.SpillSafeZoneThreshold,
                40,
                120,
                8,
                7);

            StringAssert.Contains("Round 2/3", status);
            StringAssert.Contains("Risk Building", status);
            StringAssert.Contains("Round $40", status);
            StringAssert.Contains("Bank $120", status);
            Assert.False(status.Contains("True Spill"));
        }

        [Test]
        public void GetMaximumCoinsForRisk_UsesOneCoinAtHighDangerUnlessForced()
        {
            Assert.AreEqual(2, EnemyAI.GetMaximumCoinsForRisk(4, 0f, 0));
            Assert.AreEqual(1, EnemyAI.GetMaximumCoinsForRisk(4, 55f, 0));
            Assert.AreEqual(2, EnemyAI.GetMaximumCoinsForRisk(4, 80f, 2));
        }

        [Test]
        public void CalculateWaterPulseScale_KeepsWaterFlatAndOnlyExpandsAcrossSurface()
        {
            var waterScale = new Vector3(0.8f, 0.006f, 0.8f);

            var pulseScale = GlassVisualController.CalculateWaterPulseScale(waterScale, 0.02f);

            Assert.Greater(pulseScale.x, waterScale.x);
            Assert.AreEqual(waterScale.y, pulseScale.y);
            Assert.Greater(pulseScale.z, waterScale.z);
        }

        [Test]
        public void CalculateWaterSurfaceLocalY_StaysNearTopWithTinyRiskRise()
        {
            const float stableSurfaceY = 0.62f;
            const float maxRise = 0.015f;

            var emptyGlassY = GlassVisualController.CalculateWaterSurfaceLocalY(0f, stableSurfaceY, maxRise);
            var fullRiskY = GlassVisualController.CalculateWaterSurfaceLocalY(100f, stableSurfaceY, maxRise);

            Assert.AreEqual(stableSurfaceY, emptyGlassY);
            Assert.AreEqual(stableSurfaceY + maxRise, fullRiskY);
            Assert.LessOrEqual(fullRiskY - emptyGlassY, 0.015f);
        }

        [Test]
        public void GameTestController_HidesDebugOverlayByDefault()
        {
            var host = new GameObject("Debug Overlay Host");
            var debugOverlay = host.AddComponent<GameTestController>();
            var serialized = new SerializedObject(debugOverlay);

            Assert.IsFalse(serialized.FindProperty("showDebugOverlay").boolValue);

            Object.DestroyImmediate(host);
        }

        [Test]
        public void EndScreenRestartButton_StartsNewMatchAndHidesOutcome()
        {
            var root = new GameObject("End Screen Restart Fixture");
            var glassManager = root.AddComponent<GlassManager>();
            var economyManager = root.AddComponent<EconomyManager>();
            var gameManager = root.AddComponent<GameManager>();
            var endScreenManager = root.AddComponent<EndScreenManager>();

            var canvasObject = new GameObject("End Canvas");
            canvasObject.transform.SetParent(root.transform);
            var canvas = canvasObject.AddComponent<Canvas>();
            var title = new GameObject("End Title").AddComponent<Text>();
            title.transform.SetParent(canvasObject.transform);
            var detail = new GameObject("End Detail").AddComponent<Text>();
            detail.transform.SetParent(canvasObject.transform);
            var restartButton = new GameObject("Restart Button").AddComponent<Button>();
            restartButton.transform.SetParent(canvasObject.transform);

            endScreenManager.Configure(canvas, title, detail, restartButton, gameManager);
            endScreenManager.ShowOutcome(MatchOutcome.PlayerLost, "test overflow", 0);

            Assert.IsTrue(canvas.enabled);

            restartButton.onClick.Invoke();

            Assert.IsFalse(canvas.enabled);
            Assert.AreEqual(1, gameManager.CurrentRound);
            Assert.AreEqual(GameState.PlayerTurn, gameManager.CurrentState);

            Object.DestroyImmediate(root);

            var eventSystems = Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (var i = 0; i < eventSystems.Length; i++)
                Object.DestroyImmediate(eventSystems[i].gameObject);
        }

        static void AssertVectorApproximately(Vector3 expected, Vector3 actual)
        {
            const float tolerance = 0.001f;
            Assert.AreEqual(expected.x, actual.x, tolerance);
            Assert.AreEqual(expected.y, actual.y, tolerance);
            Assert.AreEqual(expected.z, actual.z, tolerance);
        }
    }
}
