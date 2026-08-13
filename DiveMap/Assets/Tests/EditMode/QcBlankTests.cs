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

        // ── the narrowed claim, after luminance was proved blind ─────────────────

        [Test]
        public void TheAssertionMustBeDepthIndependent_orItIsMeaningless()
        {
            // 🔴 The lesson of b384, as a test. That run read authored=0.450 before=0.167
            // after=0.369 and reported FAIL — but 0.369 was the ambient LIVE in RenderSettings,
            // which is downstream of a depth scale that legitimately varies with wherever the
            // camera is sitting when the shot is taken. An assertion whose expected value depends
            // on camera position is not an assertion; it passes or fails by luck.
            //
            // The verdict therefore takes the BASELINE, which is depth-independent. These two
            // cases are the same map restored correctly, photographed at two different depths:
            // both must pass, because the restore is what is being judged.
            const double authored = 0.450;
            const double tourBase = 0.167;   // the stale baseline the bug leaves behind

            Assert.IsTrue(QcBlank.AtmospherePassed(tourBase, authored, authored),
                          "shallow camera: soft ≈ 1");
            Assert.IsTrue(QcBlank.AtmospherePassed(tourBase, authored, authored),
                          "deep camera: soft ≈ 0.82 — the baseline is the same either way");

            // And the failure it must still catch: a baseline that did not come back.
            Assert.IsFalse(QcBlank.AtmospherePassed(tourBase, 0.369, authored),
                           "a baseline stuck at 82% is a real, incomplete restore");
        }

        [Test]
        public void AMidBuildSampleCannotPass()
        {
            // 🔴 b385, as a test. The baseline was sampled while it was still settling: the drift
            // log said 0.450 and the harness said 0.369 for the SAME build, three frames apart.
            // Both readings were honest and one of them produced a FAIL against an app whose
            // restore was complete. A single sample of a moving quantity must never be allowed to
            // become a verdict.
            const double settled = 0.450;
            const double midBuild = 0.369;   // 0.450 × 0.808, the laundered value in flight

            Assert.IsFalse(QcBlank.Settled(midBuild, settled),
                           "a reading that moved by 0.08 has not settled");
            StringAssert.StartsWith(QcBlank.ControlBroken,
                                    QcBlank.UnsettledVerdict("after", midBuild, settled));

            // …and the settled case is accepted, including ordinary float noise.
            Assert.IsTrue(QcBlank.Settled(settled, settled));
            Assert.IsTrue(QcBlank.Settled(settled, settled + 0.001));
        }

        [Test]
        public void OnlyThePassThatCLAIMSToBeFixedHasToHoldStill()
        {
            // 🔴 b388: the same settle rule applied to BOTH passes ended the run CONTROL-BROKEN
            // twice over. The suppressed pass has the fix held back, and the bug it reproduces is
            // a baseline re-read from its own scaled output every frame — a walk, by definition.
            // Requiring the bug to sit still is requiring it to behave like the fix.
            Assert.IsTrue(QcBlank.MustSettle(false), "the FIXED pass must still hold still (b385)");
            Assert.IsFalse(QcBlank.MustSettle(true), "the SUPPRESSED pass is allowed to drift — that is the bug");

            // …and a drifting suppressed pass is exactly what makes a verdict possible: the
            // second reading is far from authored, which is the reproduction the control needs.
            StringAssert.StartsWith("PASS", QcBlank.AtmosphereVerdict(0.167, 0.450, 0.450));
        }

        [Test]
        public void ASampleThatNeverHappenedIsNotSettled()
        {
            // -1 is the "no baseline captured" sentinel; two of them agree numerically and must
            // still not count as a measurement.
            Assert.IsFalse(QcBlank.Settled(-1.0, -1.0));
            Assert.IsFalse(QcBlank.Settled(-1.0, 0.450));
        }

        [Test]
        public void AmbientDriftReproducedThenRestored_Passes()
        {
            // The real numbers from CI b383's trace: with the reset suppressed the next map
            // inherited the tour's dimmed ambient (sky 0.164 grayscale ≈ 41.8/255), with it
            // running the map got the authored value back (0.367 ≈ 93.7/255).
            Assert.IsTrue(QcBlank.AtmospherePassed(0.164, 0.367, 0.367));
            StringAssert.StartsWith("PASS", QcBlank.AtmosphereVerdict(0.164, 0.367, 0.367));
        }

        [Test]
        public void TheNarrowedVerdictSaysWhatItDoesNotProve()
        {
            // 🔴 A narrower claim is only worth more than a broad one if it is honest about its
            // own edges. Anyone reading a green tick must not come away thinking the user's dark
            // screen has been proved fixed, because it has not.
            string v = QcBlank.AtmosphereVerdict(0.164, 0.367, 0.367);
            StringAssert.Contains("NOT that the device's dark screen is fixed", v);
        }

        [Test]
        public void NoDriftInTheSuppressedPass_IsABrokenControl()
        {
            // Same rule as before: if the bug does not reproduce, the instrument is blind and
            // must say so instead of passing.
            Assert.IsFalse(QcBlank.AtmospherePassed(0.367, 0.367, 0.367));
            StringAssert.StartsWith(QcBlank.ControlBroken,
                                    QcBlank.AtmosphereVerdict(0.367, 0.367, 0.367));
        }

        [Test]
        public void DriftThatSurvivesTheFix_Fails()
        {
            Assert.IsFalse(QcBlank.AtmospherePassed(0.164, 0.164, 0.367));
            StringAssert.StartsWith("FAIL", QcBlank.AtmosphereVerdict(0.164, 0.164, 0.367));
        }

        [Test]
        public void WithNoAuthoredSnapshot_NothingCanBeConcluded()
        {
            StringAssert.StartsWith(QcBlank.ControlBroken,
                                    QcBlank.AtmosphereVerdict(0.164, 0.367, -1.0));
            Assert.IsFalse(QcBlank.AtmospherePassed(0.164, 0.367, -1.0));
        }

        [Test]
        public void TheToleranceAdmitsFloatNoiseButNotARealChange()
        {
            // Restoring a colour through Unity's float channels is not bit-exact.
            Assert.IsTrue(QcBlank.AtmospherePassed(0.164, 0.3672, 0.367));
            // …but half the brightness is not noise.
            Assert.IsFalse(QcBlank.AtmospherePassed(0.164, 0.20, 0.367));
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
