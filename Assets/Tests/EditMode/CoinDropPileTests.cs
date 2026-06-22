using Meniscus.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace Meniscus.Tests.EditMode
{
    /// <summary>
    /// Pure-math guards for dropping coins into the glass: the drop target must be derived from the
    /// glass's real (scaled) liquid geometry rather than a hardcoded world offset, coins must shrink to
    /// fit the cup interior, and the resting pile must stay inside the wall while stacking upward.
    /// </summary>
    public class CoinDropPileTests
    {
        // --- Root-cause fix: aim derives from the glass's real scaled geometry, not a magic offset. ---

        [Test]
        public void ColumnPointToWorld_MapsLocalSurfaceThroughGlassScale()
        {
            var glass = new GameObject("Glass").transform;
            glass.position = new Vector3(0f, 1.34f, 0.08f);
            glass.localScale = new Vector3(0.38f, 0.33f, 0.38f);

            // Liquid surface is authored at local Y 0.62, so the real world rim is 1.34 + 0.33*0.62 =
            // 1.5446 - NOT the 1.34 + 0.42 = 1.76 the old hardcoded offset aimed at.
            var surface = CoinDropPresentationController.ColumnPointToWorld(glass, 0.62f);

            Assert.AreEqual(0f, surface.x, 1e-3f);
            Assert.AreEqual(1.5446f, surface.y, 1e-3f);
            Assert.AreEqual(0.08f, surface.z, 1e-3f);

            Object.DestroyImmediate(glass.gameObject);
        }

        [Test]
        public void HorizontalWorldRadius_ScalesLocalRadiusByGlassFootprint()
        {
            var glass = new GameObject("Glass").transform;
            glass.localScale = new Vector3(0.38f, 0.33f, 0.38f);

            // Surface disc: 0.39 local * 0.38 footprint = 0.1482 world.
            Assert.AreEqual(0.1482f, CoinDropPresentationController.HorizontalWorldRadius(glass, 0.39f), 1e-3f);
            // Interior floor: 0.39 * 0.82 local * 0.38 footprint = 0.12152 world.
            Assert.AreEqual(0.12152f, CoinDropPresentationController.HorizontalWorldRadius(glass, 0.39f * 0.82f), 1e-3f);

            Object.DestroyImmediate(glass.gameObject);
        }

        // --- Coins are wider than the cup at table size, so they must shrink to fit the interior. ---

        [Test]
        public void CalculateFitScale_ShrinksAnOversizedCoinToFitInterior()
        {
            // Silver coin 0.34 wide, interior floor diameter ~0.243, fill 50% -> max 0.1215 -> ~0.357.
            var scale = CoinDropPresentationController.CalculateFitScale(0.34f, 0.243f, 0.5f);

            Assert.AreEqual(0.357f, scale, 1e-3f);
            Assert.Less(scale, 1f);
        }

        [Test]
        public void CalculateFitScale_NeverEnlargesACoinThatAlreadyFits()
        {
            var scale = CoinDropPresentationController.CalculateFitScale(0.05f, 0.243f, 0.5f);

            Assert.AreEqual(1f, scale, 1e-4f);
        }

        [Test]
        public void CalculateFitScale_HandlesZeroDiameterSafely()
        {
            Assert.AreEqual(1f, CoinDropPresentationController.CalculateFitScale(0f, 0.243f, 0.5f), 1e-4f);
        }

        // --- Pile layout: coins fan out within the wall and stack in layers as the floor fills. ---

        [Test]
        public void CalculatePileSlotLocalOffset_KeepsEveryCoinInsideTheWall()
        {
            const float interiorRadius = 0.12f;
            const float coinRadius = 0.05f;
            const float coinThickness = 0.015f;

            for (var i = 0; i < 12; i++)
            {
                var slot = CoinDropPresentationController.CalculatePileSlotLocalOffset(
                    i, interiorRadius, coinRadius, coinThickness);
                var horizontal = new Vector2(slot.x, slot.z).magnitude;

                Assert.LessOrEqual(horizontal + coinRadius, interiorRadius + 1e-4f,
                    $"Coin {i} pokes through the glass wall.");
                Assert.GreaterOrEqual(slot.y, 0f, $"Coin {i} rests below the floor.");
            }
        }

        [Test]
        public void CalculatePileSlotLocalOffset_StacksHigherAsThePileGrows()
        {
            const float interiorRadius = 0.12f;
            const float coinRadius = 0.05f;
            const float coinThickness = 0.02f;

            var firstLayer = CoinDropPresentationController.CalculatePileSlotLocalOffset(0, interiorRadius, coinRadius, coinThickness);
            var secondLayer = CoinDropPresentationController.CalculatePileSlotLocalOffset(3, interiorRadius, coinRadius, coinThickness);
            var thirdLayer = CoinDropPresentationController.CalculatePileSlotLocalOffset(6, interiorRadius, coinRadius, coinThickness);

            Assert.Greater(secondLayer.y, firstLayer.y);
            Assert.Greater(thirdLayer.y, secondLayer.y);
        }

        [Test]
        public void CalculatePileSlotLocalOffset_IsDeterministic()
        {
            var a = CoinDropPresentationController.CalculatePileSlotLocalOffset(5, 0.12f, 0.05f, 0.02f);
            var b = CoinDropPresentationController.CalculatePileSlotLocalOffset(5, 0.12f, 0.05f, 0.02f);

            Assert.AreEqual(a, b);
        }
    }
}
