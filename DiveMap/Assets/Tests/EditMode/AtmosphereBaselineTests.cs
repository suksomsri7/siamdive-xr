using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// The fog-baseline rule (WO-MERGE DARK).
    ///
    /// This is the arithmetic behind a number the user photographed: the badge read
    /// <c>fog on 489-8797</c> where <c>AppBoot.SetupLighting</c> authors <c>500 … 9000</c> — both
    /// ends down by the same 0.9774, in View mode, on a healthy-looking frame. One application of
    /// the depth visibility scale is the intended feature. The question that mattered was whether
    /// a second one can happen, because a multiplication that repeats walks the fog in until the
    /// world is a wall of fog colour with a working HUD over it — which is exactly what the user
    /// has been reporting for four rounds.
    /// </summary>
    public class AtmosphereBaselineTests
    {
        /// <summary>The factor measured on the device: 8797/9000.</summary>
        private const double MeasuredVis = 0.9774;

        // ── the rule that closes the loop ────────────────────────────────────────

        [Test]
        public void AFreshMapReadsEverything()
        {
            Assert.AreEqual(AtmosphereBaseline.Refresh.All,
                            AtmosphereBaseline.Decide(false, false, false, false));
        }

        [Test]
        public void AmbientChurnMustNotReBaselineTheFogDistances()
        {
            // 🔴 THE BUG, as one assertion. The old code re-read all six values whenever any one
            // of them differed — so an ambient write (and more than one system writes ambient
            // every frame, including the depth scaler itself) re-captured the fog distances from
            // this component's OWN already-scaled output and multiplied them by vis again.
            AtmosphereBaseline.Refresh r =
                AtmosphereBaseline.Decide(haveBase: true, ambientChanged: true,
                                          fogColorChanged: false, fogDistanceChanged: false);

            Assert.AreEqual(AtmosphereBaseline.Refresh.Ambient, r);
            Assert.AreEqual(0, (int)(r & AtmosphereBaseline.Refresh.FogDistance),
                            "an ambient change must never move the fog-distance baseline");
        }

        [Test]
        public void FogColourChurnMustNotReBaselineTheFogDistancesEither()
        {
            AtmosphereBaseline.Refresh r =
                AtmosphereBaseline.Decide(true, false, fogColorChanged: true, fogDistanceChanged: false);
            Assert.AreEqual(AtmosphereBaseline.Refresh.FogColor, r);
        }

        [Test]
        public void ARealFogDistanceChangeIsStillAdopted()
        {
            // The feature must survive the fix: when another system genuinely opens the water up
            // (the headlamp, daylight), that IS the new surface baseline.
            AtmosphereBaseline.Refresh r =
                AtmosphereBaseline.Decide(true, false, false, fogDistanceChanged: true);
            Assert.AreEqual(AtmosphereBaseline.Refresh.FogDistance, r);
        }

        [Test]
        public void NothingChanged_RefreshesNothing()
        {
            Assert.AreEqual(AtmosphereBaseline.Refresh.None,
                            AtmosphereBaseline.Decide(true, false, false, false));
        }

        [Test]
        public void EachSignalIsIndependent()
        {
            AtmosphereBaseline.Refresh r = AtmosphereBaseline.Decide(true, true, true, true);
            Assert.AreEqual(AtmosphereBaseline.Refresh.All, r);
        }

        // ── why it mattered: the compounding, measured ───────────────────────────

        [Test]
        public void OneApplicationIsTheFeature_AndMatchesWhatTheDeviceShowed()
        {
            // 9000 × 0.9774 = 8796.6 → the badge printed 8797. The instrument and the arithmetic
            // agree, which is what makes the rest of this file trustworthy.
            Assert.AreEqual(8796.6, AtmosphereBaseline.Decay(9000, MeasuredVis, 1), 1.0);
            Assert.AreEqual(488.7, AtmosphereBaseline.Decay(500, MeasuredVis, 1), 1.0);
        }

        [Test]
        public void RepeatedApplicationsWalkTheFogIn()
        {
            // Geometric, not linear — which is why it goes from invisible to total.
            double after10 = AtmosphereBaseline.Decay(9000, MeasuredVis, 10);
            double after100 = AtmosphereBaseline.Decay(9000, MeasuredVis, 100);
            double after300 = AtmosphereBaseline.Decay(9000, MeasuredVis, 300);

            Assert.Greater(after10, 7000, "ten rounds is still an ordinary-looking map");
            Assert.Less(after100, 1000, "a hundred and the far plane is inside the map");
            Assert.Less(after300, 20, "three hundred and everything past a few metres is fog");
        }

        [Test]
        public void HowFarFromAWallIsAnswerable()
        {
            // The number the drift log prints, so a device log says how close it got rather than
            // just that something moved.
            int rounds = AtmosphereBaseline.RoundsToReach(9000, MeasuredVis, 500);
            Assert.Greater(rounds, 100);
            Assert.Less(rounds, 200);
            Assert.Less(AtmosphereBaseline.Decay(9000, MeasuredVis, rounds), 500.0);
        }

        [Test]
        public void AScaleThatDoesNotShrinkIsReportedAsSuch()
        {
            // vis == 1 (the camera at the surface) never reaches a wall, and the log must say
            // "not shrinking" rather than print a nonsense count.
            Assert.AreEqual(-1, AtmosphereBaseline.RoundsToReach(9000, 1.0, 500));
            Assert.AreEqual(-1, AtmosphereBaseline.RoundsToReach(9000, 1.4, 500));
            Assert.AreEqual(-1, AtmosphereBaseline.RoundsToReach(9000, 0.9, 90000));
        }

        [Test]
        public void ZeroOrNegativeApplicationsChangeNothing()
        {
            Assert.AreEqual(9000.0, AtmosphereBaseline.Decay(9000, MeasuredVis, 0), 1e-9);
            Assert.AreEqual(9000.0, AtmosphereBaseline.Decay(9000, MeasuredVis, -3), 1e-9);
        }
    }
}
