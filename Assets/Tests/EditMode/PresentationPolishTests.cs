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
        public void BuildStatusLine_ShowsRoundAndTurnWithoutOddsOrMoney()
        {
            var status = SaloonHudController.BuildStatusLine(2, GameConstants.TotalRounds, "Your turn");

            StringAssert.Contains("ROUND 2 / 3", status);
            StringAssert.Contains("Your turn", status);
            Assert.False(status.Contains("Spill"));
            Assert.False(status.Contains("$"));
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
        public void CalculateWallRingY_LerpsFloorToRimAndClamps()
        {
            Assert.AreEqual(0.06f, GlassVisualController.CalculateWallRingY(0.06f, 0.62f, 0f), 1e-4f);
            Assert.AreEqual(0.62f, GlassVisualController.CalculateWallRingY(0.06f, 0.62f, 1f), 1e-4f);
            Assert.AreEqual(0.34f, GlassVisualController.CalculateWallRingY(0.06f, 0.62f, 0.5f), 1e-4f);
            Assert.AreEqual(0.62f, GlassVisualController.CalculateWallRingY(0.06f, 0.62f, 2f), 1e-4f); // clamps
            Assert.AreEqual(0.06f, GlassVisualController.CalculateWallRingY(0.06f, 0.62f, -1f), 1e-4f); // clamps
        }

        [Test]
        public void CreateTransparentLiquidMaterial_RendersAsSolidVolumeNotHollowShell()
        {
            // A closed liquid body (cap + walls + bottom) must read as a solid amber column, not a
            // see-through "cup". Two-sided culling with ZWrite off shows the back/inner faces through
            // the front, so the body looks hollow. The fix: write depth and cull back faces so only
            // the nearest front surface shows.
            var amber = new Color(0.55f, 0.27f, 0.05f, 0.82f);
            var material = GlassVisualController.CreateTransparentLiquidMaterial("Test Liquid", amber);

            Assert.AreEqual(1f, material.GetFloat("_ZWrite"), "Liquid must write depth so back faces don't show through.");
            Assert.AreEqual(
                (float)UnityEngine.Rendering.CullMode.Back,
                material.GetFloat("_Cull"),
                "Liquid must cull back faces so the interior walls aren't visible through the front.");

            Object.DestroyImmediate(material);
        }

        [Test]
        public void RunDownProgress_RisesFromZeroToOneAndClamps()
        {
            Assert.AreEqual(0f, GlassSpillEffect.RunDownProgress(0f, 0.4f), 1e-4f);
            Assert.AreEqual(1f, GlassSpillEffect.RunDownProgress(0.4f, 0.4f), 1e-4f);
            Assert.AreEqual(1f, GlassSpillEffect.RunDownProgress(1f, 0.4f), 1e-4f); // clamps past the end

            var mid = GlassSpillEffect.RunDownProgress(0.2f, 0.4f);
            Assert.Greater(mid, 0f);
            Assert.Less(mid, 1f);
        }

        [Test]
        public void GlassTypeDefinition_Create_DefaultsLiquidFloorFit()
        {
            var glass = GlassTypeDefinition.Create("test", "Test Glass", null);

            Assert.AreEqual(0.06f, glass.FillBottomLocalY, 1e-4f);
            Assert.AreEqual(0.82f, glass.FillBottomRadiusScale, 1e-4f);

            Object.DestroyImmediate(glass);
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
    }
}
