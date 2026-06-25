using System.Collections.Generic;
using Meniscus.Core;
using Meniscus.Gameplay;
using Meniscus.UI;
using NUnit.Framework;
using UnityEngine;

namespace Meniscus.Tests.EditMode
{
    public class GameFeelTests
    {
        // ── ParticleBurst ──────────────────────────────────────────────────────────────

        [Test]
        public void RadialDirection_IsUnitLengthAndFlatAndVariesByIndex()
        {
            var a = ParticleBurst.RadialDirection(0, 8, 0f);
            var b = ParticleBurst.RadialDirection(3, 8, 0f);

            Assert.AreEqual(1f, a.magnitude, 1e-4f, "Direction must be unit length.");
            Assert.AreEqual(0f, a.y, 1e-4f, "Splash fans out horizontally.");
            Assert.AreNotEqual(a, b, "Different slots must point in different directions.");
        }

        [Test]
        public void BallisticPosition_StartsAtOriginRisesThenReturns()
        {
            var origin = new Vector3(1f, 2f, -3f);
            var velocity = new Vector3(0f, 2f, 0f);
            const float gravity = 10f;

            var atStart = ParticleBurst.BallisticPosition(origin, velocity, gravity, 0f);
            var atApex = ParticleBurst.BallisticPosition(origin, velocity, gravity, 0.2f);  // t = v/g
            var atReturn = ParticleBurst.BallisticPosition(origin, velocity, gravity, 0.4f); // t = 2v/g

            Assert.AreEqual(origin, atStart);
            Assert.Greater(atApex.y, origin.y, "Piece rises before falling.");
            Assert.AreEqual(origin.y, atReturn.y, 1e-3f, "Piece returns to its launch height.");
        }

        [Test]
        public void BallisticPosition_ClampsNegativeTime()
        {
            var origin = new Vector3(0f, 5f, 0f);
            var result = ParticleBurst.BallisticPosition(origin, Vector3.up, 9.81f, -1f);
            Assert.AreEqual(origin, result);
        }

        [Test]
        public void DropletCountForStrength_ScalesAndClampsToRange()
        {
            Assert.AreEqual(8, ParticleBurst.DropletCountForStrength(1f));
            Assert.AreEqual(20, ParticleBurst.DropletCountForStrength(3f));
            Assert.AreEqual(4, ParticleBurst.DropletCountForStrength(0f), "Floor clamps to 4.");
            Assert.AreEqual(20, ParticleBurst.DropletCountForStrength(99f), "Ceiling clamps to 20.");
        }

        // ── GameFeelDirector ───────────────────────────────────────────────────────────

        [Test]
        public void IsTenseDrop_OnlyAtOrAboveAPositiveThreshold()
        {
            Assert.IsFalse(GameFeelDirector.IsTenseDrop(10f, 20f));
            Assert.IsTrue(GameFeelDirector.IsTenseDrop(20f, 20f));
            Assert.IsTrue(GameFeelDirector.IsTenseDrop(35f, 20f));
            Assert.IsFalse(GameFeelDirector.IsTenseDrop(50f, 0f), "A zero threshold disables the beat.");
        }

        [Test]
        public void SlowMoTimeScale_EasesDownHoldsThenReturnsToNormal()
        {
            const float rampIn = 0.1f, hold = 0.2f, rampOut = 0.2f, slow = 0.3f;

            Assert.AreEqual(1f, GameFeelDirector.SlowMoTimeScale(0f, rampIn, hold, rampOut, slow), 1e-4f);
            Assert.AreEqual(0.3f, GameFeelDirector.SlowMoTimeScale(0.15f, rampIn, hold, rampOut, slow), 1e-4f,
                "Holds at the slow scale through the hold window.");
            Assert.AreEqual(1f, GameFeelDirector.SlowMoTimeScale(0.6f, rampIn, hold, rampOut, slow), 1e-4f,
                "Returns to normal speed after the envelope.");

            var rampingIn = GameFeelDirector.SlowMoTimeScale(0.05f, rampIn, hold, rampOut, slow);
            Assert.Greater(rampingIn, slow);
            Assert.Less(rampingIn, 1f);

            var rampingOut = GameFeelDirector.SlowMoTimeScale(0.45f, rampIn, hold, rampOut, slow);
            Assert.Greater(rampingOut, slow);
            Assert.Less(rampingOut, 1f);
        }

        [Test]
        public void TensionValue_MapsSpillChanceToZeroHundredAndClamps()
        {
            Assert.AreEqual(0f, GameFeelDirector.TensionValue(0f), 1e-4f);
            Assert.AreEqual(50f, GameFeelDirector.TensionValue(GameConstants.MaxSpillChance * 0.5f), 1e-3f);
            Assert.AreEqual(100f, GameFeelDirector.TensionValue(GameConstants.MaxSpillChance), 1e-3f);
            Assert.AreEqual(100f, GameFeelDirector.TensionValue(GameConstants.MaxSpillChance * 2f), 1e-3f);
        }

        [Test]
        public void SplashStrengthForCoins_GrowsWithCountAndClamps()
        {
            Assert.AreEqual(0.7f, GameFeelDirector.SplashStrengthForCoins(null), 1e-4f);
            Assert.AreEqual(0.7f, GameFeelDirector.SplashStrengthForCoins(new List<Coin>()), 1e-4f);

            // A list of nulls still has a Count, which is all the strength curve reads.
            var two = GameFeelDirector.SplashStrengthForCoins(new List<Coin>(new Coin[2]));
            var five = GameFeelDirector.SplashStrengthForCoins(new List<Coin>(new Coin[5]));
            Assert.Greater(five, two, "More coins throw a bigger splash.");
            Assert.LessOrEqual(GameFeelDirector.SplashStrengthForCoins(new List<Coin>(new Coin[20])), 2.5f,
                "Splash strength is capped.");
        }

        // ── DangerVignette ───────────────────────────────────────────────────────────

        [Test]
        public void VignetteAlpha_StaysHiddenBelowOnsetThenRisesToMax()
        {
            const float onset = 0.25f, max = 0.5f;

            Assert.AreEqual(0f, DangerVignette.VignetteAlpha(0f, onset, max), 1e-4f);
            Assert.AreEqual(0f, DangerVignette.VignetteAlpha(0.25f, onset, max), 1e-4f, "Zero exactly at onset.");
            Assert.AreEqual(0.5f, DangerVignette.VignetteAlpha(1f, onset, max), 1e-4f, "Full alpha at full danger.");
            Assert.AreEqual(0.5f, DangerVignette.VignetteAlpha(2f, onset, max), 1e-4f, "Danger clamps to 1.");

            var mid = DangerVignette.VignetteAlpha(0.625f, onset, max);
            Assert.Greater(mid, 0f);
            Assert.Less(mid, max);
        }

        [Test]
        public void VignetteAlpha_HiddenWhenOnsetIsImpossible()
        {
            Assert.AreEqual(0f, DangerVignette.VignetteAlpha(1f, 1f, 0.5f), 1e-4f);
        }

        // ── EconomyManager multiplier feedback (shown on the money pop-up) ─────────────

        [Test]
        public void AwardSafeDrop_RecordsBoldnessMultiplierForTheMoneyPopUp()
        {
            var ecoGo = new GameObject("Economy");
            var economy = ecoGo.AddComponent<EconomyManager>();
            var coinGo = new GameObject("Coin");
            var coin = coinGo.AddComponent<Coin>();
            coin.basePayout = 30;
            coin.riskContribution = 10;

            // braved 50% → factor ≈ 2.2213 → 30 × 2.2213 ≈ 67; multiplier ≈ 67/30 ≈ 2.23×; single coin, no combo
            economy.AwardSafeDrop(new List<Coin> { coin }, 50f);

            Assert.AreEqual(2.23f, economy.LastSafeDropMultiplier, 0.05f);
            Assert.IsFalse(economy.LastSafeDropComboApplied);

            Object.DestroyImmediate(coinGo);
            Object.DestroyImmediate(ecoGo);
        }

        [Test]
        public void AwardSafeDrop_FlagsComboAndItsMultiplierForMultiCoinDrops()
        {
            var ecoGo = new GameObject("Economy");
            var economy = ecoGo.AddComponent<EconomyManager>();
            var a = new GameObject("CoinA").AddComponent<Coin>();
            var b = new GameObject("CoinB").AddComponent<Coin>();
            a.basePayout = 20;
            b.basePayout = 20;
            a.riskContribution = 10;
            b.riskContribution = 10;

            // braved 50% → factor ≈ 2.2213; (20 + 20) × 2.2213 × combo 1.15 ≈ 102; multiplier ≈ 102/40 ≈ 2.55×
            economy.AwardSafeDrop(new List<Coin> { a, b }, 50f);

            Assert.AreEqual(2.55f, economy.LastSafeDropMultiplier, 0.05f);
            Assert.IsTrue(economy.LastSafeDropComboApplied);

            Object.DestroyImmediate(a.gameObject);
            Object.DestroyImmediate(b.gameObject);
            Object.DestroyImmediate(ecoGo);
        }

        [Test]
        public void CalculateSafeDropPayout_BoldnessDrivesTheMultiplierNotCoinSize()
        {
            // Pay for boldness: a small coin dared into a near-overflowing glass earns a far higher
            // multiplier — and even out-earns — a big coin poured into a calm one. The reward tracks the
            // danger braved, not the coin's size.
            var ecoGo = new GameObject("Economy");
            var economy = ecoGo.AddComponent<EconomyManager>();

            var bigCoinSafe = new List<Coin> { MakeCoin(50, 15f) };   // Gold, but poured into a calm glass
            var smallCoinBold = new List<Coin> { MakeCoin(10, 5f) };  // Copper, dared at the brim

            var safePayout = economy.CalculateSafeDropPayout(bigCoinSafe, 20f);   // braved 20%
            var boldPayout = economy.CalculateSafeDropPayout(smallCoinBold, 70f); // braved 70%

            Assert.Greater(boldPayout / 10f, safePayout / 50f,
                "Boldness must drive a higher multiplier than merely pouring a bigger coin.");
            Assert.Greater(boldPayout, safePayout,
                "Daring a small coin at the brim even out-earns a big, safe pour.");

            foreach (var c in bigCoinSafe) Object.DestroyImmediate(c.gameObject);
            foreach (var c in smallCoinBold) Object.DestroyImmediate(c.gameObject);
            Object.DestroyImmediate(ecoGo);
        }

        static Coin MakeCoin(int basePayout, float risk)
        {
            var coin = new GameObject("Coin").AddComponent<Coin>();
            coin.basePayout = basePayout;
            coin.riskContribution = risk;
            return coin;
        }

        // ── Easing.OutBack (banner / card / pop springs) ───────────────────────────────

        [Test]
        public void OutBack_HitsExactEndpointsClampsAndOvershootsBetween()
        {
            Assert.AreEqual(0f, Easing.OutBack(0f), 1e-4f, "Starts at 0.");
            Assert.AreEqual(1f, Easing.OutBack(1f), 1e-4f, "Ends exactly at 1.");
            Assert.AreEqual(0f, Easing.OutBack(-5f), 1e-4f, "Clamps below 0.");
            Assert.AreEqual(1f, Easing.OutBack(5f), 1e-4f, "Clamps above 1.");

            var overshoots = false;
            for (var t = 0.5f; t < 1f; t += 0.05f)
                if (Easing.OutBack(t) > 1f) overshoots = true;

            Assert.IsTrue(overshoots, "OutBack must spring past 1 before settling.");
        }

        // ── Spring (selection lift / button press) ─────────────────────────────────────

        [Test]
        public void Spring_UnderDamped_OvershootsThenSettlesAtTarget()
        {
            var spring = new Spring(0f, 260f, 20f);
            var maxValue = 0f;

            for (var i = 0; i < 600; i++)
                maxValue = Mathf.Max(maxValue, spring.Step(1f, 1f / 60f));

            Assert.Greater(maxValue, 1f, "An under-damped spring overshoots its target.");
            Assert.AreEqual(1f, spring.Value, 0.01f, "It settles at the target.");
        }

        [Test]
        public void Spring_Snap_SetsValueAndClearsVelocity()
        {
            var spring = new Spring(0f, 260f, 20f);
            spring.Step(1f, 1f / 60f);            // build up some velocity first
            spring.Snap(0.5f);

            Assert.AreEqual(0.5f, spring.Value, 1e-5f);
            Assert.AreEqual(0f, spring.Velocity, 1e-5f);
        }

        [Test]
        public void Spring_Step_ClampsHugeTimestepInsteadOfExploding()
        {
            var spring = new Spring(0f, 260f, 20f);
            var value = spring.Step(1f, 1000f);   // a pathological dt (load hitch / first frame)

            Assert.IsFalse(float.IsNaN(value) || float.IsInfinity(value), "A huge dt must not blow up the spring.");
            Assert.Less(Mathf.Abs(value), 5f, "The clamped step keeps the move bounded.");
        }

        // ── RoundIntroCard label ───────────────────────────────────────────────────────

        [Test]
        public void RoundLabel_IncludesTotalWhenKnownAndDropsItWhenNot()
        {
            Assert.AreEqual("ROUND 2 OF 3", RoundIntroCard.RoundLabel(2, 3));
            Assert.AreEqual("ROUND 1", RoundIntroCard.RoundLabel(1, 0), "Drops the total when it is unknown.");
        }

        // ── CameraController loss orbit ─────────────────────────────────────────────────

        [Test]
        public void OrbitPosition_CirclesTheCenterAtRadiusAndHeight()
        {
            var center = new Vector3(1f, 2f, 3f);

            var at0 = CameraController.OrbitPosition(center, 5f, 0.5f, 0f);
            Assert.AreEqual(center.x + 5f, at0.x, 1e-3f, "Angle 0 sits +radius on x.");
            Assert.AreEqual(center.z, at0.z, 1e-3f);
            Assert.AreEqual(center.y + 0.5f, at0.y, 1e-3f, "Lifted by height.");

            var at90 = CameraController.OrbitPosition(center, 5f, 0.5f, 90f);
            Assert.AreEqual(center.x, at90.x, 1e-3f, "A quarter turn swings to +z.");
            Assert.AreEqual(center.z + 5f, at90.z, 1e-3f);

            // Any angle stays on the circle of the given radius (flat distance from the centre).
            var p = CameraController.OrbitPosition(center, 5f, 0.5f, 37f);
            var flat = new Vector2(p.x - center.x, p.z - center.z);
            Assert.AreEqual(5f, flat.magnitude, 1e-3f, "Stays on the orbit radius.");
        }
    }
}
