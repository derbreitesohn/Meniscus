using System.Collections;
using Meniscus.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;

namespace Meniscus.Tests.PlayMode
{
    /// <summary>
    /// Guards that the whiskey liquid actually fills the Standard glass (a tall tapered tumbler) instead of
    /// floating as a tiny cup near the top, and that dropped coins therefore rest on the glass floor inside
    /// the wall. Loads the scene in play mode so GlassPresentationController has configured the liquid, then
    /// checks the runtime liquid geometry against the real glass mesh bounds.
    /// </summary>
    public class StandardGlassLiquidFitTests
    {
        [UnityTest]
        public IEnumerator LiquidFillsTheGlassAndCoinsRestOnTheFloor()
        {
            // Batch-mode Wwise can't post events; ignore those unrelated error logs so they don't fail the test.
            LogAssert.ignoreFailingMessages = true;

            var load = SceneManager.LoadSceneAsync("Assets/Scenes/Saloon.unity", LoadSceneMode.Single);
            while (load != null && !load.isDone)
                yield return null;

            // Let Awake/Start run; GlassPresentationController configures the liquid in Start.
            yield return null;
            yield return null;

            var glass = GameObject.FindWithTag("Glass");
            Assert.IsNotNull(glass, "Glass-tagged object not found in Saloon scene.");

            var visual = glass.GetComponent<GlassVisualController>();
            Assert.IsNotNull(visual, "GlassVisualController not found on the glass.");

            // Measure the real glass interior extent from its mesh (world bounds).
            var glassBounds = MeasureGlassBounds(glass.transform);
            var glassBottomY = glassBounds.min.y;
            var glassTopY = glassBounds.max.y;
            var glassRadius = 0.5f * Mathf.Max(glassBounds.size.x, glassBounds.size.z);

            var floorWorld = CoinDropPresentationController.ColumnPointToWorld(glass.transform, visual.FloorLocalY);
            var surfaceWorldY = CoinDropPresentationController.ColumnPointToWorld(glass.transform, visual.StableSurfaceLocalY).y;
            var floorRadiusWorld = CoinDropPresentationController.HorizontalWorldRadius(
                glass.transform, visual.SurfaceLocalRadius * visual.FloorRadiusScale);

            // Liquid has real depth (not a thin floating disc): surface well above the floor.
            Assert.Greater(surfaceWorldY - floorWorld.y, 0.25f,
                "Liquid is too shallow — it reads as a small cup floating in the glass, not a filled glass.");

            // Coins rest near the glass floor (bottom third), not up at a floating mid-glass cup.
            var bottomThird = glassBottomY + (glassTopY - glassBottomY) / 3f;
            Assert.Less(floorWorld.y, bottomThird,
                $"Coin/liquid floor (world y {floorWorld.y:F3}) sits above the glass's bottom third " +
                $"(<{bottomThird:F3}); coins won't rest on the glass floor.");
            Assert.Greater(floorWorld.y, glassBottomY - 0.05f,
                $"Coin/liquid floor (world y {floorWorld.y:F3}) is below the glass bottom ({glassBottomY:F3}).");

            // Coins fan inside the wall, not splayed outside the cup.
            Assert.Less(floorRadiusWorld, glassRadius,
                $"Coin floor radius {floorRadiusWorld:F3} exceeds the glass radius {glassRadius:F3}; coins splay outside.");
            Assert.Greater(floorRadiusWorld, glassRadius * 0.4f,
                $"Coin floor radius {floorRadiusWorld:F3} is tiny vs the glass radius {glassRadius:F3}; liquid isn't filling the cup.");
        }

        static Bounds MeasureGlassBounds(Transform glass)
        {
            var standard = FindDeep(glass, "Standard_Glas") ?? glass;
            var renderers = standard.GetComponentsInChildren<Renderer>();
            Assert.Greater(renderers.Length, 0, "Glass has no renderers to measure.");
            var b = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
                b.Encapsulate(renderers[i].bounds);
            return b;
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (var i = 0; i < root.childCount; i++)
            {
                var f = FindDeep(root.GetChild(i), name);
                if (f != null) return f;
            }
            return null;
        }
    }
}
