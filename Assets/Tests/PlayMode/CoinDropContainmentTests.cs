using System.Collections;
using System.Collections.Generic;
using Meniscus.Core;
using Meniscus.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;

namespace Meniscus.Tests.PlayMode
{
    /// <summary>
    /// Guards that dropped coins actually *render* inside the glass, not just that their transforms are
    /// placed inside. A coin model can carry a large off-centre pivot, so a proxy positioned correctly by
    /// its transform could still draw its mesh swung out through the side wall. Drives a real drop and
    /// checks every parked coin's world renderer bounds against the cup interior. (The pure-math
    /// CoinDropPileTests can't catch a visual-vs-transform offset; this is the layer that does.)
    /// </summary>
    public class CoinDropContainmentTests
    {
        [UnityTest]
        public IEnumerator DroppedCoinsRenderInsideTheGlassWall()
        {
            // Batch-mode Wwise can't post events; ignore those unrelated error logs so they don't fail the test.
            LogAssert.ignoreFailingMessages = true;

            var load = SceneManager.LoadSceneAsync("Assets/Scenes/Saloon.unity", LoadSceneMode.Single);
            while (load != null && !load.isDone)
                yield return null;

            yield return null;
            yield return null;

            var glass = GameObject.FindWithTag("Glass");
            Assert.IsNotNull(glass, "Glass-tagged object not found.");
            var visual = glass.GetComponent<GlassVisualController>();
            Assert.IsNotNull(visual, "GlassVisualController not found on the glass.");
            var manager = Object.FindAnyObjectByType<GameManager>();
            Assert.IsNotNull(manager, "No GameManager.");
            var controller = Object.FindAnyObjectByType<CoinDropPresentationController>();
            Assert.IsNotNull(controller, "No CoinDropPresentationController.");

            // The interior wall radius coins are sized/placed against (the liquid's floor disc).
            var floorRadiusWorld = CoinDropPresentationController.HorizontalWorldRadius(
                glass.transform, visual.SurfaceLocalRadius * visual.FloorRadiusScale);
            var axis = glass.transform.position;

            var coins = new List<Coin>();
            foreach (var c in manager.PlayerCoins)
            {
                if (c != null) coins.Add(c);
                if (coins.Count >= 3) break;
            }
            Assert.Greater(coins.Count, 0, "No player coins to drop.");

            var done = false;
            var result = new GlassDropResult(TurnActor.Player, 0f, 0f, 0f, 0f, false, coins.Count);
            controller.PlayDropSequence(TurnActor.Player, coins, result, () => done = true);

            var t = 0f;
            while (!done && t < 8f)
            {
                t += Time.deltaTime;
                yield return null;
            }

            var pile = GameObject.Find("Coins In Glass");
            Assert.IsNotNull(pile, "No 'Coins In Glass' pile was created.");
            Assert.Greater(pile.transform.childCount, 0, "No coins were parked in the glass.");

            foreach (Transform child in pile.transform)
            {
                var bounds = MeasureBounds(child.gameObject);
                var centerOffset = new Vector2(bounds.center.x - axis.x, bounds.center.z - axis.z).magnitude;
                var outerReach = centerOffset + 0.5f * Mathf.Max(bounds.size.x, bounds.size.z);

                // The rendered coin's outer edge must stay within the cup. Before the pivot fix the visible
                // mesh reached ~0.22-0.28 world (out past the ~0.14 wall); contained coins reach < ~0.10.
                Assert.LessOrEqual(outerReach, floorRadiusWorld + 1e-3f,
                    $"Coin '{child.name}' renders out past the glass wall: outer reach {outerReach:F3} " +
                    $"exceeds the interior radius {floorRadiusWorld:F3}.");
            }
        }

        static Bounds MeasureBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            Assert.Greater(renderers.Length, 0, $"Parked coin '{go.name}' has no renderer.");
            var b = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
                b.Encapsulate(renderers[i].bounds);
            return b;
        }
    }
}
