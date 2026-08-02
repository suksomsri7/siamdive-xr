using System;
using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// The tests that matter here are the ones about being LIED TO. Counting black pixels in a
    /// buffer is arithmetic; the reason this class exists is that CI has already shipped a set of
    /// green screenshots of a scene containing none of the models they were supposed to prove
    /// something about. So most of what follows asserts that an empty frame, a frame that could
    /// not be read back, and a model that never arrived all come out FAIL — loudly.
    /// </summary>
    public class QcPixelsTests
    {
        // ── helpers ──────────────────────────────────────────────────────────────

        /// <summary>A flat RGB24 buffer of <paramref name="n"/> pixels.</summary>
        private static byte[] Flat(int n, byte r, byte g, byte b)
        {
            var px = new byte[n * 3];
            for (int i = 0; i < n; i++) { px[i * 3] = r; px[i * 3 + 1] = g; px[i * 3 + 2] = b; }
            return px;
        }

        private static void Paint(byte[] buf, int from, int count, byte r, byte g, byte b)
        {
            for (int i = from; i < from + count; i++) { buf[i * 3] = r; buf[i * 3 + 1] = g; buf[i * 3 + 2] = b; }
        }

        /// <summary>The gradient backdrop stands in as "not the model": mid ocean blue.</summary>
        private static byte[] Water(int n) => Flat(n, 77, 133, 168);

        // ── pure black ───────────────────────────────────────────────────────────

        [Test]
        public void PureBlackCountsOnlyExactZero()
        {
            byte[] frame = Water(100);
            Paint(frame, 0, 25, 0, 0, 0);
            Paint(frame, 25, 25, 1, 0, 0);   // one bit off zero — deliberately NOT pure black
            Assert.AreEqual(25.0, QcPixels.PureBlackPercent(frame), 1e-9);
            Assert.AreEqual(50.0, QcPixels.AtOrBelowPercent(frame, QcPixels.NearBlackMax), 1e-9);
        }

        [Test]
        public void AChannelAboveTheCeilingIsNotBlackHowDarkTheOthersAre()
        {
            // A dark-blue surface underwater is not the bug. Only all three channels down.
            byte[] frame = Flat(10, 0, 0, 40);
            Assert.AreEqual(0.0, QcPixels.PureBlackPercent(frame), 1e-9);
            Assert.AreEqual(0.0, QcPixels.AtOrBelowPercent(frame, QcPixels.NearBlackMax), 1e-9);
        }

        [Test]
        public void EmptyOrRaggedBuffersMeasureZeroRatherThanThrow()
        {
            Assert.AreEqual(0, QcPixels.PixelCount(null));
            Assert.AreEqual(0, QcPixels.PixelCount(new byte[2]));      // not even one pixel
            Assert.AreEqual(0.0, QcPixels.PureBlackPercent(null), 1e-9);
            Assert.AreEqual(1, QcPixels.PixelCount(new byte[5]));      // one whole pixel + change
        }

        // ── the subject mask: what proves a model was in the picture ─────────────

        [Test]
        public void TheModelIsWhateverChangedWhenItWasSwitchedOff()
        {
            byte[] off = Water(200);
            byte[] on = Water(200);
            Paint(on, 0, 60, 30, 60, 40);    // the model covers 60 of 200 px

            QcPixels.Shot s = QcPixels.Measure(on, off);
            Assert.AreEqual(30.0, s.SubjectPercent, 1e-9);
            Assert.AreEqual(200, s.Pixels);
        }

        [Test]
        public void AnEmptyFrameScoresZeroSubjectAndFails()
        {
            // 🔴 THE test. Both frames are the same empty water: the shot is evidence that
            // nothing was photographed, and it must not be reported as a model that is fine.
            byte[] off = Water(500);
            byte[] on = Water(500);

            QcPixels.Shot s = QcPixels.Measure(on, off);
            Assert.AreEqual(0.0, s.SubjectPercent, 1e-9);
            Assert.AreEqual(0.0, s.PureBlackPercent, 1e-9);            // and it looks spotless
            Assert.IsFalse(QcPixels.Passes(loaded: true, renderers: 3, shot: s));
            Assert.AreEqual("not-in-frame", QcPixels.Reason(true, 3, s));
            StringAssert.Contains("loaded=FAIL", QcPixels.Line("msh_lionfish_xr0", true, 3, s));
        }

        [Test]
        public void ASliverOfModelIsStillNotEvidence()
        {
            byte[] off = Water(1000);
            byte[] on = Water(1000);
            Paint(on, 0, 49, 200, 200, 200);   // 4.9% — just under the floor
            QcPixels.Shot s = QcPixels.Measure(on, off);
            Assert.Less(s.SubjectPercent, QcPixels.MinSubjectPercent);
            Assert.IsFalse(QcPixels.Passes(true, 1, s));

            Paint(on, 49, 2, 200, 200, 200);   // 5.1% — over it
            s = QcPixels.Measure(on, off);
            Assert.GreaterOrEqual(s.SubjectPercent, QcPixels.MinSubjectPercent);
            Assert.IsTrue(QcPixels.Passes(true, 1, s));
        }

        [Test]
        public void RenderNoiseUnderToleranceIsNotAModel()
        {
            byte[] off = Water(100);
            byte[] on = Water(100);
            for (int i = 0; i < 100; i++) on[i * 3] += QcPixels.SubjectTolerance;   // exactly at tol
            Assert.AreEqual(0.0, QcPixels.Measure(on, off).SubjectPercent, 1e-9);

            for (int i = 0; i < 100; i++) on[i * 3] += 1;                            // one over
            Assert.AreEqual(100.0, QcPixels.Measure(on, off).SubjectPercent, 1e-9);
        }

        [Test]
        public void BlackIsMeasuredAgainstTheModelNotTheWholeFrame()
        {
            // A wreck filling a fifth of the frame, entirely black. 20% of the frame reads as a
            // modest-sounding number; 100% of the MODEL is the accusation.
            byte[] off = Water(1000);
            byte[] on = Water(1000);
            Paint(on, 0, 200, 0, 0, 0);

            QcPixels.Shot s = QcPixels.Measure(on, off);
            Assert.AreEqual(20.0, s.PureBlackPercent, 1e-9);
            Assert.AreEqual(20.0, s.SubjectPercent, 1e-9);
            Assert.AreEqual(100.0, s.BlackOfSubjectPercent, 1e-9);
            Assert.IsTrue(QcPixels.Passes(true, 4, s));   // it WAS photographed — that is the point
        }

        [Test]
        public void MismatchedOrMissingReadbackFailsInsteadOfScoringWell()
        {
            byte[] on = Water(300);
            QcPixels.Shot s = QcPixels.Measure(on, Water(299));   // one pixel short
            Assert.AreEqual(0.0, s.SubjectPercent, 1e-9);
            Assert.IsFalse(QcPixels.Passes(true, 2, s));

            QcPixels.Shot none = QcPixels.Measure(null, null);
            Assert.AreEqual(0, none.Pixels);
            Assert.IsFalse(QcPixels.Passes(true, 2, none));
            Assert.AreEqual("readback-empty", QcPixels.Reason(true, 2, none));
        }

        // ── verdict + log line ───────────────────────────────────────────────────

        [Test]
        public void EveryWayOfHavingNoModelIsItsOwnReason()
        {
            byte[] off = Water(100);
            byte[] on = Water(100);
            Paint(on, 0, 50, 10, 20, 30);
            QcPixels.Shot good = QcPixels.Measure(on, off);

            Assert.AreEqual("download-or-parse", QcPixels.Reason(false, 0, good));
            Assert.AreEqual("no-renderer", QcPixels.Reason(true, 0, good));
            Assert.AreEqual("", QcPixels.Reason(true, 5, good));
            Assert.IsFalse(QcPixels.Passes(false, 5, good));   // a 404 can never be OK
        }

        [Test]
        public void TheLineKeepsTheThreeFieldsTheWorkOrderAsksFor()
        {
            byte[] off = Water(1000);
            byte[] on = Water(1000);
            Paint(on, 0, 400, 0, 0, 0);
            QcPixels.Shot s = QcPixels.Measure(on, off);

            string line = QcPixels.Line("cc0_kraken_xr0", loaded: true, renderers: 7, shot: s);
            StringAssert.StartsWith("[QCModel] cc0_kraken_xr0 pureBlack=40.00% loaded=OK", line);
            StringAssert.Contains("subject=40.00%", line);
            StringAssert.Contains("blackOfSubject=100.00%", line);
            StringAssert.Contains("renderers=7", line);
        }

        [Test]
        public void ANamelessShotStillPrintsSomethingSearchable()
        {
            string line = QcPixels.Line(null, false, 0, new QcPixels.Shot());
            StringAssert.StartsWith("[QCModel] (unnamed) pureBlack=0.00% loaded=FAIL", line);
            StringAssert.Contains("reason=download-or-parse", line);
        }

        [Test]
        public void PercentagesAlwaysUseADotWhateverTheMachinesLocaleIs()
        {
            // The player runs on a CI box whose culture we do not control, and a log line reading
            // "pureBlack=40,00%" would break every grep written against it.
            // Built by hand rather than by name: this runner is compiled with
            // InvariantGlobalization, so "de-DE" cannot be constructed here — but a comma decimal
            // separator is the whole of what we are defending against.
            var comma = (System.Globalization.CultureInfo)
                System.Globalization.CultureInfo.InvariantCulture.Clone();
            comma.NumberFormat.NumberDecimalSeparator = ",";
            System.Globalization.CultureInfo prev = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = comma;
                byte[] off = Water(10);
                byte[] on = Water(10);
                Paint(on, 0, 4, 0, 0, 0);
                string line = QcPixels.Line("x", true, 1, QcPixels.Measure(on, off));
                StringAssert.Contains("pureBlack=40.00%", line);
            }
            finally { System.Threading.Thread.CurrentThread.CurrentCulture = prev; }
        }

        // ── framing ──────────────────────────────────────────────────────────────

        [Test]
        public void FullFillPutsTheModelsSphereExactlyOnTheFrameEdge()
        {
            // fill = 1 → the bounding sphere's angular radius equals the half-FOV.
            const double fov = 60.0, r = 10.0;
            double d = QcPixels.FrameDistance(r, fov, aspect: 1.0, fill: 1.0);
            double halfAngle = Math.Asin(r / d) * 180.0 / Math.PI;
            Assert.AreEqual(fov / 2.0, halfAngle, 1e-9);
        }

        [Test]
        public void AskingForLessFillStandsFurtherOff()
        {
            double near = QcPixels.FrameDistance(10, 60, 16.0 / 9.0, 0.9);
            double far = QcPixels.FrameDistance(10, 60, 16.0 / 9.0, 0.5);
            Assert.Greater(far, near);
        }

        [Test]
        public void ABiggerModelIsPhotographedFromProportionallyFurtherAway()
        {
            double small = QcPixels.FrameDistance(1, 60, 16.0 / 9.0);
            double big = QcPixels.FrameDistance(50, 60, 16.0 / 9.0);
            Assert.AreEqual(50.0, big / small, 1e-9);   // it is a pure scale — no magic sizes
        }

        [Test]
        public void TheTightAxisWins()
        {
            // A 16:9 frame is tighter vertically, so the vertical FOV sets the distance and the
            // aspect makes no difference beyond 1:1.
            double wide = QcPixels.FrameDistance(10, 60, 16.0 / 9.0);
            double square = QcPixels.FrameDistance(10, 60, 1.0);
            Assert.AreEqual(square, wide, 1e-9);

            // A portrait frame is tighter horizontally — stand further back.
            double portrait = QcPixels.FrameDistance(10, 60, 9.0 / 16.0);
            Assert.Greater(portrait, square);
        }

        [Test]
        public void NonsenseGeometryStillReturnsAUsableDistance()
        {
            Assert.Greater(QcPixels.FrameDistance(0, 60, 1.0), 0.0);      // zero-size model
            Assert.Greater(QcPixels.FrameDistance(10, 0, 0), 0.0);        // no FOV, no aspect
            Assert.Greater(QcPixels.FrameDistance(10, 400, 1.0), 0.0);    // FOV out of range
        }
    }
}
