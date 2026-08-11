using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// The "is this frame a dead world" verdict (WO-MERGE P1e).
    ///
    /// The pixels have to come from CI, but the JUDGEMENT is settled here — including the part
    /// that is easiest to get wrong and most expensive to get wrong: what the harness does when
    /// the bug fails to reproduce. A control that reports PASS because it could not see the bug
    /// would have retired this investigation with the bug still in it.
    /// </summary>
    public class QcBlankTests
    {
        /// <summary>A frame of one flat colour: the fog wall.</summary>
        private static byte[] Flat(byte r, byte g, byte b, int pixels = 256)
        {
            var buf = new byte[pixels * 3];
            for (int i = 0; i < pixels; i++) { buf[i * 3] = r; buf[i * 3 + 1] = g; buf[i * 3 + 2] = b; }
            return buf;
        }

        /// <summary>Half dark, half light: something is in the picture.</summary>
        private static byte[] TwoTone(byte lo, byte hi, int pixels = 256)
        {
            var buf = new byte[pixels * 3];
            for (int i = 0; i < pixels; i++)
            {
                byte v = i < pixels / 2 ? lo : hi;
                buf[i * 3] = v; buf[i * 3 + 1] = v; buf[i * 3 + 2] = v;
            }
            return buf;
        }

        // ── measurement ──────────────────────────────────────────────────────────

        [Test]
        public void FlatFrame_HasNoSpread()
        {
            QcBlank.Frame f = QcBlank.Measure(Flat(20, 30, 45));
            Assert.AreEqual(256, f.Pixels);
            Assert.AreEqual(QcBlank.Luminance(20, 30, 45), f.MeanLuminance, 1e-6);
            Assert.AreEqual(0.0, f.StdDev, 1e-6, "one colour cannot vary");
        }

        [Test]
        public void UniformFrame_NeverProducesNaN()
        {
            // Population variance from sums cancels to a hair below zero on a uniform frame, and
            // Sqrt of that is NaN — which compares false against every threshold and would let the
            // blankest possible frame through as "not blank".
            QcBlank.Frame f = QcBlank.Measure(Flat(37, 37, 37, 4096));
            Assert.IsFalse(double.IsNaN(f.StdDev));
            Assert.IsTrue(QcBlank.IsBlank(f));
        }

        [Test]
        public void TwoToneFrame_HasSpread()
        {
            QcBlank.Frame f = QcBlank.Measure(TwoTone(10, 200));
            Assert.Greater(f.StdDev, 50.0);
        }

        [TestCase(null)]
        [TestCase(new byte[0])]
        public void NoPixels_IsNotEvidence(byte[] buf)
        {
            QcBlank.Frame f = QcBlank.Measure(buf);
            Assert.AreEqual(0, f.Pixels);
            Assert.IsFalse(QcBlank.IsBlank(f), "a capture that did not happen is not a blank frame");
        }

        // ── the blank rule needs BOTH halves ─────────────────────────────────────

        [Test]
        public void TheFogWall_IsBlank()
        {
            // Roughly what the lights-off ambient behind linear fog closing at 200 units puts on
            // screen: dark, navy, and the same everywhere.
            Assert.IsTrue(QcBlank.IsBlank(QcBlank.Measure(Flat(14, 26, 43))));
        }

        [Test]
        public void TheGateCanActuallyAdmitTheRealFogColour()
        {
            // 🔴 The bug in the instrument itself (WO-MERGE P1h). A fully fogged frame cannot be
            // darker than the fog colour, so if the gate sits BELOW that colour's luminance it is
            // unsatisfiable: a perfect reproduction of the device condition would still be
            // reported as "the bug did not reproduce", and two CI rounds would be spent hunting a
            // sequence that was working. The gate was 46; the colour is ≈57.
            //
            // Read from DiveLightMath itself rather than restated, so the day somebody re-tunes
            // the drone's fog this fails instead of quietly going unsatisfiable again.
            DiveLightMath.Atmosphere off = DiveLightMath.HeadlightOff;
            double fogLum = QcBlank.Luminance((byte)(off.FogR * 255f),
                                              (byte)(off.FogG * 255f),
                                              (byte)(off.FogB * 255f));

            Assert.Greater(QcBlank.BlankMeanMax, fogLum,
                           $"a frame made entirely of the lights-off fog colour reads {fogLum:F1}; " +
                           "a blank gate below that can never fire");

            // …and still nowhere near a healthy frame. 185.8 / 186.8 measured on CI 31458246375.
            Assert.Less(QcBlank.BlankMeanMax, 150.0,
                        "the gate must stay far below a healthy frame (measured ~186)");

            // The whole point, stated as the thing that must be true: a frame of exactly the fog
            // colour is blank.
            var wall = new byte[3 * 64];
            for (int i = 0; i < 64; i++)
            {
                wall[i * 3] = (byte)(off.FogR * 255f);
                wall[i * 3 + 1] = (byte)(off.FogG * 255f);
                wall[i * 3 + 2] = (byte)(off.FogB * 255f);
            }
            Assert.IsTrue(QcBlank.IsBlank(QcBlank.Measure(wall)),
                          "the drone's own lights-off fog colour must read as blank");
        }

        [Test]
        public void AnHonestNightDive_IsNotBlank()
        {
            // Dark, but there is a seabed and a wreck in it. Mean alone would fail this frame, and
            // failing it would mean the check could never ship.
            QcBlank.Frame f = QcBlank.Measure(TwoTone(8, 70));
            Assert.Less(f.MeanLuminance, QcBlank.BlankMeanMax, "genuinely a dark frame");
            Assert.IsFalse(QcBlank.IsBlank(f), "…but it has shapes in it");
        }

        [Test]
        public void AFlatBrightWall_IsNotBlank()
        {
            // Spread alone would condemn this. It is flat, but it is not the failure being hunted
            // (that one is dark), and widening the rule to catch it would catch open water too.
            Assert.IsFalse(QcBlank.IsBlank(QcBlank.Measure(Flat(180, 190, 200))));
        }

        // ── the control's own honesty ────────────────────────────────────────────

        [Test]
        public void BugReproducedThenFixed_IsTheOnlyPass()
        {
            QcBlank.Frame before = QcBlank.Measure(Flat(14, 26, 43));
            QcBlank.Frame after = QcBlank.Measure(TwoTone(30, 190));

            Assert.IsTrue(QcBlank.Passed(before, after));
            StringAssert.StartsWith("PASS", QcBlank.Verdict(before, after));
        }

        [Test]
        public void StillBlankAfterTheFix_Fails()
        {
            QcBlank.Frame blank = QcBlank.Measure(Flat(14, 26, 43));
            Assert.IsFalse(QcBlank.Passed(blank, blank));
            StringAssert.StartsWith("FAIL", QcBlank.Verdict(blank, blank));
        }

        [Test]
        public void BugDidNotReproduce_IsABrokenControlNotAPass()
        {
            // 🔴 The assertion this whole file exists for. If the "before" pass comes out healthy,
            // the harness has gone blind — the sequence stopped reproducing, or the suppression
            // switch stopped suppressing. Calling that a PASS would retire the investigation with
            // the bug still in the product, which is exactly how this bug survived four rounds.
            QcBlank.Frame healthy = QcBlank.Measure(TwoTone(30, 190));
            QcBlank.Frame after = QcBlank.Measure(TwoTone(30, 190));

            Assert.IsFalse(QcBlank.Passed(healthy, after));
            StringAssert.StartsWith("CONTROL-BROKEN", QcBlank.Verdict(healthy, after));
        }

        [Test]
        public void AMissingCaptureIsABrokenControl()
        {
            QcBlank.Frame blank = QcBlank.Measure(Flat(14, 26, 43));
            StringAssert.StartsWith("CONTROL-BROKEN", QcBlank.Verdict(blank, default));
            StringAssert.StartsWith("CONTROL-BROKEN", QcBlank.Verdict(default, blank));
        }

        [Test]
        public void ABlownBudgetIsABrokenControl_NotAPassAndNotAHang()
        {
            // 🔴 The lesson from CI run 31442231470: the harness overran, the job was cancelled at
            // 155 minutes, and there was no verdict of any kind — plus it took the unrelated
            // palette screenshots in the same job with it. A control that runs out of time has to
            // SAY so, in the same file and the same shape as every other outcome.
            string v = QcBlank.BudgetVerdict("before", 140f);
            StringAssert.StartsWith(QcBlank.ControlBroken, v);
            StringAssert.Contains("before", v);
            StringAssert.Contains("140s", v);
        }

        [Test]
        public void EveryNonAnswerSharesOnePrefix()
        {
            // One grep finds them all, in a log nobody wrote a parser for.
            Assert.AreEqual("CONTROL-BROKEN", QcBlank.ControlBroken);
            foreach (string v in new[]
                     {
                         QcBlank.BudgetVerdict("after", 12f),
                         QcBlank.Verdict(default, default),
                         QcBlank.Verdict(QcBlank.Measure(TwoTone(30, 190)),
                                         QcBlank.Measure(TwoTone(30, 190))),
                     })
                StringAssert.StartsWith(QcBlank.ControlBroken, v);
        }

        [Test]
        public void TheVerdictCarriesBothSetsOfNumbers()
        {
            // The line goes into verdict.txt and into the CI log, and it is what a human reads to
            // decide whether the thresholds were anywhere near the edge.
            string v = QcBlank.Verdict(QcBlank.Measure(Flat(14, 26, 43)),
                                       QcBlank.Measure(TwoTone(30, 190)));
            StringAssert.Contains("before mean=", v);
            StringAssert.Contains("after", v);
            StringAssert.Contains("sd=", v);
            StringAssert.Contains("px=", v);
        }
    }
}
