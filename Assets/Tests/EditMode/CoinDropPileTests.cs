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

        // --- Drop scaling: the coin must reach its cup-fit size EARLY in the flight — while still out over
        // the table — so a full-size coin (almost as wide as the cup) is never carried over / into the glass
        // and can't punch through the side wall in flight. ---

        [Test]
        public void CalculateStagedDropScale_KeepsFullSizeOnlyAtPickup()
        {
            var full = Vector3.one;
            var fitted = Vector3.one * 0.3f;

            // Full size only for the very first sliver of the lift (the pickup), then it begins to shrink.
            Assert.AreEqual(full, CoinDropPresentationController.CalculateStagedDropScale(full, fitted, 0f));
            Assert.AreEqual(full, CoinDropPresentationController.CalculateStagedDropScale(full, fitted, 0.05f));
            Assert.Less(
                CoinDropPresentationController.CalculateStagedDropScale(full, fitted, 0.3f).x, full.x,
                "The coin should already be shrinking by 30% of the flight.");
        }

        [Test]
        public void CalculateStagedDropScale_IsFullyFittedLongBeforeReachingTheGlass()
        {
            var full = Vector3.one;
            var fitted = Vector3.one * 0.3f;

            // Fully fitted by 45% of the arc — long before the coin nears the glass (poise/plunge happen near
            // t 1) — and stays fitted through the poise and entry.
            Assert.AreEqual(fitted, CoinDropPresentationController.CalculateStagedDropScale(full, fitted, 0.45f));
            Assert.AreEqual(fitted, CoinDropPresentationController.CalculateStagedDropScale(full, fitted, 0.6f));
            Assert.AreEqual(fitted, CoinDropPresentationController.CalculateStagedDropScale(full, fitted, 0.9f));
            Assert.AreEqual(fitted, CoinDropPresentationController.CalculateStagedDropScale(full, fitted, 1f));
        }

        [Test]
        public void CalculateStagedDropScale_ShrinksMonotonicallyAcrossTheFlight()
        {
            var full = Vector3.one;
            var fitted = Vector3.one * 0.3f;
            var previous = float.MaxValue;

            for (var t = 0f; t <= 1f; t += 0.02f)
            {
                var scale = CoinDropPresentationController.CalculateStagedDropScale(full, fitted, t).x;

                Assert.LessOrEqual(scale, previous + 1e-4f, $"Scale grew at t={t}.");
                Assert.GreaterOrEqual(scale, fitted.x - 1e-4f, $"Scale undershot the fit size at t={t}.");
                previous = scale;
            }
        }

        // --- Hard containment: a coin's outer edge can never cross the interior wall, whatever the placement
        // math or how the liquid disc was authored relative to the real glass. ---

        [Test]
        public void ClampInsideWall_LeavesACoinThatAlreadyFitsUntouched()
        {
            var axis = new Vector3(0.3f, 1.2f, -0.4f);
            var point = axis + new Vector3(0.02f, 0f, 0.01f); // 0.0224 from axis, edge 0.0224+0.02 < 0.12

            var clamped = CoinDropPresentationController.ClampInsideWall(point, axis, 0.12f, 0.02f);

            Assert.AreEqual(point.x, clamped.x, 1e-5f);
            Assert.AreEqual(point.z, clamped.z, 1e-5f);
            Assert.AreEqual(point.y, clamped.y, 1e-5f);
        }

        [Test]
        public void ClampInsideWall_PullsAnOverhangingCoinBackInsideKeepingItsHeight()
        {
            var axis = new Vector3(0f, 1.2f, 0f);
            var point = new Vector3(0.5f, 1.2f, 0f); // way outside

            var clamped = CoinDropPresentationController.ClampInsideWall(point, axis, 0.12f, 0.02f);

            // Centre pulled to (interior - coinRadius) = 0.10 from the axis; height unchanged.
            var horizontal = new Vector2(clamped.x - axis.x, clamped.z - axis.z).magnitude;
            Assert.AreEqual(0.10f, horizontal, 1e-4f);
            Assert.AreEqual(1.2f, clamped.y, 1e-5f);
        }

        [Test]
        public void ClampInsideWall_KeepsTheEdgeInsideForEveryDirectionAndOffset()
        {
            var axis = new Vector3(0.1f, 1.3f, -0.2f);
            const float interior = 0.12f;
            const float coinRadius = 0.05f;

            for (var step = 0; step < 24; step++)
            {
                var angle = step * (Mathf.PI * 2f / 24f);

                for (var reach = 0f; reach <= 0.4f; reach += 0.05f)
                {
                    var point = axis + new Vector3(Mathf.Cos(angle) * reach, 0f, Mathf.Sin(angle) * reach);
                    var clamped = CoinDropPresentationController.ClampInsideWall(point, axis, interior, coinRadius);
                    var edge = new Vector2(clamped.x - axis.x, clamped.z - axis.z).magnitude + coinRadius;

                    Assert.LessOrEqual(edge, interior + 1e-4f,
                        $"Coin edge crossed the wall at angle {angle:F2}, reach {reach:F2}.");
                }
            }
        }

        [Test]
        public void InteriorWorldRadiusAtHeight_InterpolatesFloorToSurfaceAndClampsOutside()
        {
            // Floor radius 0.12 at y 1.0, surface radius 0.20 at y 1.4.
            Assert.AreEqual(0.12f, CoinDropPresentationController.InteriorWorldRadiusAtHeight(1.0f, 1.0f, 0.12f, 1.4f, 0.20f), 1e-4f);
            Assert.AreEqual(0.20f, CoinDropPresentationController.InteriorWorldRadiusAtHeight(1.4f, 1.0f, 0.12f, 1.4f, 0.20f), 1e-4f);
            Assert.AreEqual(0.16f, CoinDropPresentationController.InteriorWorldRadiusAtHeight(1.2f, 1.0f, 0.12f, 1.4f, 0.20f), 1e-4f);
            // Below the floor / above the surface clamp to the nearest end (never wider than authored).
            Assert.AreEqual(0.12f, CoinDropPresentationController.InteriorWorldRadiusAtHeight(0.5f, 1.0f, 0.12f, 1.4f, 0.20f), 1e-4f);
            Assert.AreEqual(0.20f, CoinDropPresentationController.InteriorWorldRadiusAtHeight(2.0f, 1.0f, 0.12f, 1.4f, 0.20f), 1e-4f);
        }

        // --- Drop trajectory: one continuous arc over the glass centre (no detour to the rim, no freeze). ---

        [Test]
        public void CalculateArcPosition_StartsAtStartAndEndsAtEnd()
        {
            var start = new Vector3(-0.4f, 1.1f, 0.2f);
            var end = new Vector3(0f, 1.5f, 0.08f);

            Assert.AreEqual(start, CoinDropPresentationController.CalculateArcPosition(start, end, 0f, 0.34f));
            Assert.AreEqual(end, CoinDropPresentationController.CalculateArcPosition(start, end, 1f, 0.34f));
        }

        [Test]
        public void CalculateArcPosition_RisesAboveTheStraightLineSoTheCoinArcsOverTheGlass()
        {
            var start = new Vector3(-0.4f, 1.1f, 0.2f);
            var end = new Vector3(0f, 1.5f, 0.08f);
            const float height = 0.34f;

            var mid = CoinDropPresentationController.CalculateArcPosition(start, end, 0.5f, height);
            var straightMidY = (start.y + end.y) * 0.5f;

            // The peak clears the straight chord by the full arc height, so the coin lifts up and over the cup
            // instead of sliding flat into it.
            Assert.AreEqual(straightMidY + height, mid.y, 1e-4f);
            Assert.Greater(mid.y, start.y);
            Assert.Greater(mid.y, end.y);
        }

        [Test]
        public void CalculateCarryReleaseRotation_BlendsStartToReleaseContinuouslyWithNoHeldSegment()
        {
            var start = Quaternion.identity;
            var carry = Quaternion.AngleAxis(12f, Vector3.right);
            var release = Quaternion.AngleAxis(52f, Vector3.right);

            // Endpoints land exactly on start / carry (midpoint) / release.
            Assert.That(Quaternion.Angle(start,
                CoinDropPresentationController.CalculateCarryReleaseRotation(start, carry, release, 0f)), Is.LessThan(1e-3f));
            Assert.That(Quaternion.Angle(carry,
                CoinDropPresentationController.CalculateCarryReleaseRotation(start, carry, release, 0.5f)), Is.LessThan(1e-3f));
            Assert.That(Quaternion.Angle(release,
                CoinDropPresentationController.CalculateCarryReleaseRotation(start, carry, release, 1f)), Is.LessThan(1e-3f));

            // The rotation keeps advancing across the midpoint — there is no frozen "hold" plateau.
            var before = CoinDropPresentationController.CalculateCarryReleaseRotation(start, carry, release, 0.45f);
            var after = CoinDropPresentationController.CalculateCarryReleaseRotation(start, carry, release, 0.55f);
            Assert.Greater(Quaternion.Angle(before, after), 0.1f);
        }

        // --- Camera baiting: side angle always on overflow + high risk, a random fake-out otherwise. ---

        [Test]
        public void ShouldUseSideCamera_AlwaysSwingsSideOnRealOverflow()
        {
            // Even a zero-risk, never-bait roll must still swing side when the drop actually overflowed.
            Assert.IsTrue(CoinDropPresentationController.ShouldUseSideCamera(
                overflowed: true, spillChance: 0f, maxSpillChance: 50f, highRiskThreshold01: 0.5f, baitChance: 0f, roll: 0.99f));
        }

        [Test]
        public void ShouldUseSideCamera_SwingsSideOnHighRiskSafeDropRegardlessOfRoll()
        {
            // 30/50 = 0.6 danger >= 0.5 threshold -> side, even though the roll would not trigger a fake-out.
            Assert.IsTrue(CoinDropPresentationController.ShouldUseSideCamera(
                overflowed: false, spillChance: 30f, maxSpillChance: 50f, highRiskThreshold01: 0.5f, baitChance: 0.34f, roll: 0.99f));
        }

        [Test]
        public void ShouldUseSideCamera_LowRiskSafeDropIsAFakeOutGatedByTheRoll()
        {
            // 5/50 = 0.1 danger < 0.5 threshold -> only a random fake-out at baitChance.
            Assert.IsTrue(CoinDropPresentationController.ShouldUseSideCamera(
                overflowed: false, spillChance: 5f, maxSpillChance: 50f, highRiskThreshold01: 0.5f, baitChance: 0.34f, roll: 0.2f),
                "A roll below baitChance should fake-out to the side.");
            Assert.IsFalse(CoinDropPresentationController.ShouldUseSideCamera(
                overflowed: false, spillChance: 5f, maxSpillChance: 50f, highRiskThreshold01: 0.5f, baitChance: 0.34f, roll: 0.5f),
                "A roll at/above baitChance stays on the front view.");
        }

        // --- Plunge splash timing: the splash fires when the coin crosses the surface, not at the floor. ---

        [Test]
        public void CalculateSurfaceCrossFraction_IsPartwayDownWhenPoisedAboveSurface()
        {
            // Poised at 1.30, rests at 1.10, surface at 1.26 -> crosses 4/20 = 0.2 of the way down.
            Assert.AreEqual(0.2f, CoinDropPresentationController.CalculateSurfaceCrossFraction(1.30f, 1.10f, 1.26f), 1e-4f);
        }

        [Test]
        public void CalculateSurfaceCrossFraction_IsZeroWhenAlreadyAtOrBelowSurface()
        {
            Assert.AreEqual(0f, CoinDropPresentationController.CalculateSurfaceCrossFraction(1.26f, 1.10f, 1.26f), 1e-4f);
            Assert.AreEqual(0f, CoinDropPresentationController.CalculateSurfaceCrossFraction(1.20f, 1.10f, 1.26f), 1e-4f);
        }
    }
}
