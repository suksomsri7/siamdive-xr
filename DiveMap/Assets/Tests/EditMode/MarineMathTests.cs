using System;
using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// Unit tests for MarineMath — the regression-prone WO-XR-03 rules ported from
    /// builder.html: real-delta FS clamp, LOD frame-skip bands, the ±turn-rate cap, the
    /// whale pitch/no-roll orientation, and the v.0680 smooth solid-avoidance steer.
    /// </summary>
    public class MarineMathTests
    {
        private const double Eps = 1e-9;

        // ── FS real-delta clamp (builder.html 3877) ───────────────────────────────
        [Test]
        public void RealDeltaScale_ClampsToRange()
        {
            Assert.AreEqual(0.5, MarineMath.RealDeltaScale(0.0001), Eps); // tiny dt floored
            Assert.AreEqual(2.5, MarineMath.RealDeltaScale(1.0), Eps);    // huge dt capped
            Assert.AreEqual(1.0, MarineMath.RealDeltaScale(0.016667), 1e-3); // ~60Hz ≈ 1
        }

        // ── LOD behaviour throttle bands (builder.html 3923) ──────────────────────
        [Test]
        public void StepEveryForDistance_Bands()
        {
            Assert.AreEqual(1, MarineMath.StepEveryForDistance(50));
            Assert.AreEqual(1, MarineMath.StepEveryForDistance(139));
            Assert.AreEqual(2, MarineMath.StepEveryForDistance(141));
            Assert.AreEqual(2, MarineMath.StepEveryForDistance(339));
            Assert.AreEqual(6, MarineMath.StepEveryForDistance(341));
            Assert.AreEqual(6, MarineMath.StepEveryForDistance(2000));
        }

        [Test]
        public void AvoidanceActive_OnlyWithinMidBand()
        {
            Assert.IsTrue(MarineMath.AvoidanceActiveForDistance(100));
            Assert.IsTrue(MarineMath.AvoidanceActiveForDistance(339));
            Assert.IsFalse(MarineMath.AvoidanceActiveForDistance(340));
            Assert.IsFalse(MarineMath.AvoidanceActiveForDistance(500));
        }

        // ── Turn-rate cap (builder.html 1603) ─────────────────────────────────────
        [Test]
        public void TurnToward_CapsStepBothDirections()
        {
            Assert.AreEqual(0.045, MarineMath.TurnToward(0, 1.0, 0.045), 1e-12);
            Assert.AreEqual(-0.045, MarineMath.TurnToward(0, -1.0, 0.045), 1e-12);
            Assert.AreEqual(0.02, MarineMath.TurnToward(0, 0.02, 0.045), 1e-12); // small = exact
        }

        [Test]
        public void TurnToward_TakesShortWayAroundPi()
        {
            // From +3.0 rad to −3.0 rad is a short +0.283 hop across ±π, not a −6 swing.
            double next = MarineMath.TurnToward(3.0, -3.0, 0.045);
            Assert.Greater(next, 3.0);                 // moved the SHORT way (positive, wraps past π)
            Assert.AreEqual(3.045, next, 1e-9);
        }

        // ── Whale pitch / no-roll orientation (builder.html 2454-2481) ────────────
        [Test]
        public void Orientation_Level_HasNoPitchAndNoRoll()
        {
            var o = MarineMath.OrientationFromVelocity(1, 0, 1);
            Assert.AreEqual(0.0, o.PitchRad, 1e-9);
            Assert.AreEqual(0.0, o.RollRad, 0.0);       // roll is a literal 0
            Assert.AreEqual(0.0, MarineMath.RightVector(o).Y, 1e-9); // no bank
        }

        [Test]
        public void Orientation_Climb_IsNoseUp_Dive_IsNoseDown()
        {
            var climb = MarineMath.OrientationFromVelocity(0, 1, 1);
            var dive  = MarineMath.OrientationFromVelocity(0, -1, 1);
            Assert.Less(climb.PitchRad, 0.0);           // nose-up = negative rotation.x
            Assert.Greater(dive.PitchRad, 0.0);         // nose-down = positive rotation.x
            Assert.AreEqual(0.0, climb.RollRad, 0.0);
            Assert.AreEqual(0.0, dive.RollRad, 0.0);
        }

        [Test]
        public void Orientation_PitchClampedToHalfRadian()
        {
            // Nearly straight up → asin blows toward π/2 but must clamp to −0.5.
            var o = MarineMath.OrientationFromVelocity(0, 100, 0.0001);
            Assert.AreEqual(-MarineMath.DefaultMaxPitch, o.PitchRad, 1e-9);
        }

        [Test]
        public void Orientation_NeverRolls_AcrossManyVelocities()
        {
            var rnd = new Random(12345);
            for (int i = 0; i < 500; i++)
            {
                double vx = rnd.NextDouble() * 20 - 10;
                double vy = rnd.NextDouble() * 20 - 10;
                double vz = rnd.NextDouble() * 20 - 10;
                var o = MarineMath.OrientationFromVelocity(vx, vy, vz);
                Assert.AreEqual(0.0, o.RollRad, 0.0, "roll must always be exactly 0");
                Assert.AreEqual(0.0, MarineMath.RightVector(o).Y, 1e-9, "right-vector must stay horizontal (no bank)");
                Assert.LessOrEqual(Math.Abs(o.PitchRad), MarineMath.DefaultMaxPitch + 1e-12);
            }
        }

        // ── Smooth solid-avoidance (builder.html v.0680, 2392-2405) ───────────────
        [Test]
        public void SolidAvoid_ActiveWhenApproachingBox()
        {
            // Box [-5,5]^3, obsR 4 → avoidR 15. Fish at x=15 (surface dist 10) heading -x.
            bool active = MarineMath.SolidAvoid(
                15, 0, 0, Math.PI,
                -5, 5, -5, 5, -5, 5, 4, false,
                out double th, out double cl);
            Assert.IsTrue(active);
            Assert.AreEqual((15.0 - 10.0) / 15.0, cl, 1e-9); // closeness = (avoidR-sd)/avoidR
            Assert.AreNotEqual(Math.PI, th);                 // heading was steered away
        }

        [Test]
        public void SolidAvoid_InactiveWhenFarOrOverTheTop()
        {
            // Far past avoidR.
            Assert.IsFalse(MarineMath.SolidAvoid(
                100, 0, 0, Math.PI, -5, 5, -5, 5, -5, 5, 4, false, out _, out _));
            // Above the box top (fishY 20 > maxY 5 + obsR 4 = 9) → swim over it.
            Assert.IsFalse(MarineMath.SolidAvoid(
                6, 0, 20, Math.PI, -5, 5, -5, 5, -5, 5, 4, false, out _, out _));
        }
    }
}
