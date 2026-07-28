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

        // ── School formation geometry (WO-XR-03a, builder.html buildSchool 1490-1526) ──
        //
        // Reference numbers come from the REAL demo map (wl6zwxh1tdgn, Htms Chang):
        //   school:scad       ×7  item.s 2.2 (×5) / 2.21 (×2)   GLB maxd 1.911
        //   school:barracuda  ×1  item.s 9.2                    GLB maxd 1.862
        //   pod:yellowtail    ×2  item.s 0.64 and 0.28          trevally GLB maxd 1.899
        //   msh:whaleshark    ×1  item.s 34.2                   GLB maxd 1.908

        [Test]
        public void ShoalRadius_MatchesWebScadShoal()
        {
            // SR = flen × (3 + ∛500 × 1.6) = 1.911 × 15.699 ≈ 30.0 (local)
            double srLocal = MarineMath.ShoalRadius(1.911, 500);
            Assert.AreEqual(30.0, srLocal, 0.1);
            // …× item.s 2.2 ⇒ the school spans 66 units, NOT the 4.2-unit marble of QC r7.
            Assert.AreEqual(66.0, srLocal * 2.2, 0.1);
            Assert.AreEqual(66.3, srLocal * 2.21, 0.1); // the two s=2.21 scad items
        }

        [Test]
        public void FormationRadius_MatchesWebBarracudaCluster()
        {
            // R = flen × max(2.8, 200×0.07) × formR 0.6 = 1.862 × 14 × 0.6 ≈ 15.64 (local)
            double rLocal = MarineMath.FormationRadius(1.862, 200, 0.6);
            Assert.AreEqual(15.64, rLocal, 0.02);
            Assert.AreEqual(143.9, rLocal * 9.2, 0.2);
        }

        [Test]
        public void FormationRadius_UsesThe2Point8FloorForTinySchools()
        {
            // N×0.07 = 0.7 < 2.8 → the web's max() floor takes over.
            Assert.AreEqual(2.0 * 2.8, MarineMath.FormationRadius(2.0, 10, 1.0), 1e-9);
        }

        [Test]
        public void PodRadius_MatchesWebYellowtailGoldenDisc()
        {
            // animalW = 16 × defaultScale 1.3 = 20.8 (local), podR = 20.8 × 0.72 × √50
            double animalLocal = MarineMath.PodAnimalLocalLen(1.3);
            Assert.AreEqual(20.8, animalLocal, 1e-9);

            double podLocal = MarineMath.PodRadius(animalLocal, 50);
            Assert.AreEqual(105.9, podLocal, 0.1);
            Assert.AreEqual(29.6, podLocal * 0.28, 0.1);
            Assert.AreEqual(67.8, podLocal * 0.64, 0.1);

            // A pod uses the 3× animal floor — NEVER the 8× school floor.
            double animalWorld = animalLocal * 0.28;
            Assert.GreaterOrEqual(podLocal * 0.28, MarineMath.PodRadiusFloorMul * animalWorld);
        }

        [Test]
        public void SpeedCaps_MatchWebPerFrameCaps()
        {
            // shoal: flen × 0.04 /frame × 60 × item.s
            double scad = MarineMath.SpeedCapPerSecond(1.911, MarineMath.ShoalSpeedPerFrame, 1.0) * 2.2;
            Assert.AreEqual(10.1, scad, 0.1);

            // formation: flen × 0.065 × swimMul 0.06 /frame × 60 × item.s — barracuda is CALM.
            double barra = MarineMath.SpeedCapPerSecond(1.862, MarineMath.FormationSpeedPerFrame, 0.06) * 9.2;
            Assert.AreEqual(4.0, barra, 0.1);
            Assert.Less(barra, scad); // the calm cluster must stay slower than the shoal
        }

        [Test]
        public void WhaleWorldLen_IsItemScaleTimesGlbMaxd_NoClamp()
        {
            double len = MarineMath.WhaleWorldLen(1.908, 34.2);
            Assert.AreEqual(65.3, len, 0.1);
            // The QC-r7 clamp [8..16] would have shrunk this to 16 — assert it is gone.
            Assert.Greater(len, 16.0);
        }

        // ── Production resolver: assetId + item.s → world geometry ────────────────

        [Test]
        public void SchoolGeometryFor_ScadShoal()
        {
            MarineMath.SchoolGeometry g = MarineMath.SchoolGeometryFor("school:scad", 2.2);
            Assert.AreEqual(4.20, g.FishWorldLen, 0.02);
            Assert.AreEqual(66.0, g.RadiusWorld, 0.1);
            Assert.AreEqual(26.4, g.VertHalfWorld, 0.1);   // 0.40 × SR — a flat pancake
            Assert.AreEqual(10.1, g.SpeedCap, 0.1);
            Assert.AreEqual(120, g.Spec.UnityCount);
            Assert.AreEqual(500, g.Spec.WebCount);          // the SPAN uses the WEB count
            Assert.IsFalse(g.IsPod);
        }

        [Test]
        public void SchoolGeometryFor_BarracudaCluster()
        {
            MarineMath.SchoolGeometry g = MarineMath.SchoolGeometryFor("school:barracuda", 9.2);
            Assert.AreEqual(17.1, g.FishWorldLen, 0.1);
            Assert.AreEqual(143.9, g.RadiusWorld, 0.2);
            Assert.AreEqual(39.6, g.VertHalfWorld, 0.2);    // 0.275 × R
            Assert.AreEqual(4.0, g.SpeedCap, 0.1);
            Assert.AreEqual(160, g.Spec.UnityCount);
            Assert.IsFalse(g.IsPod);
        }

        [Test]
        public void SchoolGeometryFor_YellowtailPods()
        {
            MarineMath.SchoolGeometry big = MarineMath.SchoolGeometryFor("pod:yellowtail", 0.64);
            Assert.AreEqual(13.3, big.FishWorldLen, 0.1);   // 16 × 1.3 × 0.64
            Assert.AreEqual(67.8, big.RadiusWorld, 0.1);
            Assert.AreEqual(16.9, big.VertHalfWorld, 0.1);  // 0.25 × podR
            Assert.IsTrue(big.IsPod);
            Assert.AreEqual(50, big.Spec.UnityCount);
            Assert.AreEqual(0.55, big.Spec.SizeMin, 1e-9);
            Assert.AreEqual(1.35, big.Spec.SizeMax, 1e-9);

            MarineMath.SchoolGeometry small = MarineMath.SchoolGeometryFor("pod:yellowtail", 0.28);
            Assert.AreEqual(5.82, small.FishWorldLen, 0.02);
            Assert.AreEqual(29.6, small.RadiusWorld, 0.1);
            Assert.AreEqual(7.4, small.VertHalfWorld, 0.1);
        }

        [Test]
        public void SchoolGeometryFor_RadiusNeverBreaksItsFloor()
        {
            // Every species placed in the demo map, at its real item.s.
            var placed = new[]
            {
                new Tuple<string, double>("school:scad", 2.2),
                new Tuple<string, double>("school:scad", 2.21),
                new Tuple<string, double>("school:barracuda", 9.2),
                new Tuple<string, double>("pod:yellowtail", 0.64),
                new Tuple<string, double>("pod:yellowtail", 0.28),
            };

            foreach (Tuple<string, double> p in placed)
            {
                MarineMath.SchoolGeometry g = MarineMath.SchoolGeometryFor(p.Item1, p.Item2);
                double floorMul = g.IsPod ? MarineMath.PodRadiusFloorMul : MarineMath.SchoolRadiusFloorMul;
                Assert.GreaterOrEqual(g.RadiusWorld, floorMul * g.FishWorldLen - 1e-9,
                    p.Item1 + " @ s=" + p.Item2 + " broke its formation-radius floor");
                Assert.Greater(g.VertHalfWorld, 0.0);
                Assert.Greater(g.SpeedCap, 0.0);
            }
        }

        [Test]
        public void SchoolGeometryFor_TinyScaleStillClearsTheSchoolFloor()
        {
            // A deliberately shrunken school: the 8×fish floor must catch it (never a 0.3-unit
            // ball of 120 fish — the "yellowtail 20 in 0.3 m" regression).
            MarineMath.SchoolGeometry g = MarineMath.SchoolGeometryFor("school:scad", 0.02);
            Assert.GreaterOrEqual(g.RadiusWorld, MarineMath.SchoolRadiusFloorMul * g.FishWorldLen - 1e-9);
        }

        [Test]
        public void DemoMapFishBudget_Is1100()
        {
            int scad = MarineMath.SpeciesFor("school:scad").UnityCount;
            int barracuda = MarineMath.SpeciesFor("school:barracuda").UnityCount;
            int pod = MarineMath.SpeciesFor("pod:yellowtail").UnityCount;
            Assert.AreEqual(1100, scad * 7 + barracuda + pod * 2);
        }

        [Test]
        public void SpeciesFor_UnknownIdFallsBackToAUsableShoal()
        {
            MarineMath.SpeciesSpec s = MarineMath.SpeciesFor("warp:0");
            Assert.AreEqual(MarineMath.SchoolFormation.Shoal, s.Formation);
            Assert.Greater(s.UnityCount, 0);
            Assert.Greater(s.WebCount, 0);
            Assert.Greater(s.FishLenLocal, 0.0);
        }
    }
}
