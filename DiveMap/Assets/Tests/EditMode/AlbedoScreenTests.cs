using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// WO-E5c — the albedo screen, held to the run it was derived from.
    ///
    /// 🔴 Every number below was measured, not chosen. <c>black</c> is <c>blackOfSubject</c> from
    /// CI run 30800189252 — the percentage of each model's own pixels that came out of the frame
    /// as exactly (0,0,0). The texture statistics are surface-weighted (every texel carries the 3D
    /// area of the triangles that sample it, so unused atlas gutter drops out) and were taken off
    /// the SAME files that run downloaded: the five that had already been through texlift are
    /// measured after the lift, which is the state their blackOfSubject is also in.
    ///
    /// The fixture exists so that the screen can never quietly drift back to being a statistic
    /// about the bright end. If somebody changes <see cref="SurfaceLight.ScreenMinP1Srgb"/> or
    /// <see cref="SurfaceLight.ScreenMaxPctBelowCrush"/>, these eleven models say immediately
    /// whether the new numbers still separate the pictures that were black from the ones that
    /// were not.
    /// </summary>
    [TestFixture]
    public class AlbedoScreenTests
    {
        /// <summary>name, blackOfSubject %, p1, p5, pctBelow45, pctBelow64, p95.</summary>
        private static readonly object[][] Run30800189252 =
        {
            //                          black    p1   p5  <45    <64    p95
            new object[] { "kraken",             0.64, 50,  94,  0.80,  1.56, 205 },
            new object[] { "poseidon",           1.42, 56,  74,  0.25,  2.14, 177 },
            new object[] { "hardeep",            1.81, 56,  90,  0.48,  1.46, 169 },
            new object[] { "htms732",            0.16, 72,  82,  0.00,  0.20, 155 },
            new object[] { "barracuda",         16.79, 10,  21, 13.58, 17.51, 249 },
            new object[] { "lionfish",           2.80, 25,  39,  6.85, 19.85, 170 },
            new object[] { "singha",             5.64, 32,  57,  2.48,  7.15, 215 },
            new object[] { "chang",              0.12, 87, 106,  0.03,  0.14, 173 },
            new object[] { "ancient_byzantine", 25.82, 23,  42,  5.66, 13.73, 199 },
            new object[] { "domed_temple",      33.32, 13,  30, 11.30, 20.85, 171 },
            new object[] { "grand_byzantine",    1.07, 71,  93,  0.57,  0.72, 170 },
        };

        /// <summary>Above this a model is one the user would call black.</summary>
        private const double BlackEnoughToComplainAbout = 5.0;

        [Test]
        public void TheScreenCatchesEveryModelThatActuallyWentBlack()
        {
            int caught = 0, missed = 0, falsePositives = 0;
            foreach (object[] r in Run30800189252)
            {
                var name = (string)r[0];
                double black = System.Convert.ToDouble(r[1]);
                int p1 = System.Convert.ToInt32(r[2]);
                double below45 = System.Convert.ToDouble(r[4]);

                bool flagged = SurfaceLight.NeedsAlbedoLift(p1, below45);
                bool isBlack = black >= BlackEnoughToComplainAbout;

                if (isBlack && flagged) caught++;
                else if (isBlack) { missed++; Assert.Fail($"{name} rendered {black}% black and the screen let it through"); }
                else if (flagged) falsePositives++;
            }

            Assert.That(caught, Is.EqualTo(4), "all four models that went black must be caught");
            Assert.That(missed, Is.EqualTo(0));
            // lionfish, and only lionfish: a real dark tail (p1 25) that never covers a whole
            // pixel because it is fin stripes on a small, close-framed animal.
            Assert.That(falsePositives, Is.EqualTo(1), "exactly one known false positive");
        }

        [Test]
        public void TheOldP95ScreenHadARecallOfZero()
        {
            // Kept as a test rather than as a paragraph, because "p95 < 160" is still what the
            // asset batch was using and somebody will be tempted by it again: it is a statistic
            // about the BRIGHT end of a texture, and black comes from the dark end.
            int caught = 0, missed = 0;
            string flaggedInstead = null;
            foreach (object[] r in Run30800189252)
            {
                double black = System.Convert.ToDouble(r[1]);
                int p95 = System.Convert.ToInt32(r[6]);
                bool flagged = p95 < 160;
                if (black >= BlackEnoughToComplainAbout && flagged) caught++;
                else if (black >= BlackEnoughToComplainAbout) missed++;
                else if (flagged) flaggedInstead = (string)r[0];
            }
            Assert.That(caught, Is.EqualTo(0), "p95 < 160 caught none of the four");
            Assert.That(missed, Is.EqualTo(4));
            Assert.That(flaggedInstead, Is.EqualTo("htms732"),
                "…and the one file it did flag was the second cleanest map in the set (0.16% black)");
        }

        [Test]
        public void DarkAndBlackAreDifferentAxesWithDifferentOwners()
        {
            // The three Atlantis ruins came out of the same run at 85.02 / 85.35 / 86.90% dark —
            // within two points of each other — while their pure black ran 1.07 → 33.32%. Whatever
            // is making them DIM is common to all three (they are enormous and low-density); what
            // is making them BLACK is their own texture. A geometry rebuild moves the first number
            // and an albedo lift moves the second, and neither will move both.
            double[] dark = { 85.02, 85.35, 86.90 };
            double[] black = { 1.07, 33.32, 25.82 };

            double darkSpread = 0.0, blackSpread = 0.0;
            for (int i = 0; i < dark.Length; i++)
                for (int j = 0; j < dark.Length; j++)
                {
                    darkSpread = System.Math.Max(darkSpread, System.Math.Abs(dark[i] - dark[j]));
                    blackSpread = System.Math.Max(blackSpread, System.Math.Abs(black[i] - black[j]));
                }

            Assert.That(darkSpread, Is.LessThan(3.0), "dark is the same for all three");
            Assert.That(blackSpread, Is.GreaterThan(30.0), "black is not");
        }

        [Test]
        public void TheCrushPointStaysInANarrowBandSoOneScreenHoldsEverywhere()
        {
            // The justification for a depth-independent screen, asserted rather than asserted-in-a-
            // comment: across everything the app renders the crush albedo only moves 45..64.
            int lo = 255, hi = 0;
            for (float metres = 0f; metres <= 100f; metres += 2f)
            {
                int c = SurfaceLight.CrushAlbedoSrgb(metres * DepthLight.UnitsPerMetre,
                                                     SurfaceLight.Facing.Down);
                if (c < lo) lo = c;
                if (c > hi) hi = c;
            }
            Assert.That(lo, Is.GreaterThanOrEqualTo(44), $"crush floor moved to {lo}");
            Assert.That(hi, Is.LessThanOrEqualTo(65), $"crush ceiling moved to {hi}");
            Assert.That(hi - lo, Is.LessThan(25),
                "a screen at one threshold only holds while this band is narrow");
        }
    }

    /// <summary>
    /// WO-E5d — the arithmetic the daylight ladder is read against.
    /// </summary>
    [TestFixture]
    public class DaylightAmbientTests
    {
        /// <summary>EnvMode's daylight bands: sky white x 0.72, ground 0xd8c9a8 x 0.72.</summary>
        private const float DaySky = 0.72f;
        private const float DayGround = 0.847f * 0.72f;

        [Test]
        public void AmbientAloneCannotProduceTheByteTheUserPhotographed()
        {
            // 🔴 The measurement that turned "ซุ้มดำ" from a texture question into a light one.
            //
            // The user switched the app to daylight — no fog, no depth curve, no ambient floor,
            // none of the three multipliers that had been accused in turn — and photographed
            // Atlantis again. The dome's body reads byte 3 (p50), 69% of it under 16.
            //
            // A surface of the ruins' OWN measured albedo, lit by nothing but the daylight ambient
            // and with the sun switched off entirely, cannot be that dark. Not "should not be" —
            // cannot, and this is the sum.
            float albedo = ToneMap.SrgbToLinear(112f / 255f);   // domed_temple, surface-weighted

            foreach (float band in new[] { DayGround, DaySky })
            {
                float scene = albedo * band;
                byte b = ToneMap.LinearToByte(ToneMap.Aces1(scene));
                Assert.That((int)b, Is.GreaterThan(60),
                    $"ambient {band:0.00} alone on albedo {albedo:0.000} must not be dark");
            }

            // …and the shadow cannot take it there either: in the built-in pipeline the shadow term
            // multiplies the DIRECT light and leaves the ambient alone, so switching the sun off
            // entirely is the darkest a shadow could ever make this surface.
            float darkest = albedo * DayGround;
            Assert.That((int)ToneMap.LinearToByte(ToneMap.Aces1(darkest)), Is.GreaterThan(60),
                "the fully shadowed case is still nowhere near the byte 3 that was photographed");
        }

        [Test]
        public void TheToneCurveInvertsSoALadderCanBeReadInLight()
        {
            // A ratio measured in screen bytes is not a ratio of light — the curve is steeply
            // compressive, and the whole ladder depends on being able to get back.
            foreach (float scene in new[] { 0.002f, 0.01f, 0.05f, 0.162f, 0.4f, 0.9f })
            {
                float display = ToneMap.Aces1(scene);
                float back = ToneMap.InverseNeutral(display);
                Assert.That(back, Is.EqualTo(scene).Within(System.Math.Max(1e-4f, scene * 0.02f)),
                    $"InverseNeutral must undo Aces1 at {scene}");
            }
            Assert.That(ToneMap.InverseNeutral(0f), Is.EqualTo(0f));
        }

        [Test]
        public void HowMuchLightIsActuallyMissing()
        {
            // Stated as the one number the report turns on. What the dome measures, against what
            // the same albedo under the dimmest daylight band would give.
            float albedo = ToneMap.SrgbToLinear(112f / 255f);
            float expected = albedo * DayGround;

            // byte 3 -> display-linear -> scene-linear
            float measured = ToneMap.InverseNeutral(ToneMap.SrgbToLinear(3f / 255f));

            double shortfall = expected / System.Math.Max(measured, 1e-9f);
            Assert.That(shortfall, Is.GreaterThan(10.0),
                $"the dome is receiving {shortfall:0.0}x less light than the ambient alone would give");
        }
    }

    /// <summary>
    /// WO-E5h — the rule the ladder learned the expensive way.
    /// </summary>
    [TestFixture]
    public class RungMaskTests
    {
        private const int N = 100;

        private static byte[] Flat(byte v)
        {
            var b = new byte[N * 3];
            for (int i = 0; i < b.Length; i++) b[i] = v;
            return b;
        }

        /// <summary>Twenty pixels of subject on eighty of background.</summary>
        private static byte[] SubjectOn(byte bg, byte subject)
        {
            byte[] b = Flat(bg);
            for (int i = 0; i < 20; i++) { int p = i * 3; b[p] = subject; b[p + 1] = subject; b[p + 2] = subject; }
            return b;
        }

        [Test]
        public void ARungThatChangesTheWholeFrameMustNotBeAllowedToRedefineTheSubject()
        {
            // 🔴 The bug, reproduced. The empty frame is dark background; the shipped frame adds a
            // model on top of it. A later rung brightens EVERYTHING — which is what switching the
            // tone curve off does — and if that rung recomputes "what differs from empty" it
            // claims the whole frame as subject and averages the background in with the model.
            byte[] empty = Flat(20);
            byte[] shipped = SubjectOn(20, 40);
            byte[] brightened = SubjectOn(200, 210);

            bool[] mask = QcPixels.SubjectMask(shipped, empty);
            int inMask = 0;
            foreach (bool m in mask) if (m) inMask++;
            Assert.That(inMask, Is.EqualTo(20), "the model is twenty pixels and stays twenty pixels");

            // The wrong way: let the rung work out its own mask against the empty frame.
            QcPixels.SceneLinearOfSubject(brightened, empty, out double wrongPct, out _);
            Assert.That(wrongPct, Is.EqualTo(100.0).Within(1e-9),
                "this is the subject=100.00% that reached a human as a 6.4x brightening");

            // The right way: the mask the baseline defined.
            QcPixels.SceneLinearOfMask(brightened, mask, out double rightPct, out _);
            Assert.That(rightPct, Is.EqualTo(20.0).Within(1e-9),
                "every rung has to be measured on the same pixels as the rung it is compared with");
        }

        [Test]
        public void TheFixedMaskGivesAComparisonThatMeansSomething()
        {
            // With the mask held fixed, the ratio between two rungs is a statement about the model
            // and nothing else — which is the entire purpose of a ladder.
            byte[] empty = Flat(20);
            byte[] shipped = SubjectOn(20, 40);
            byte[] brightened = SubjectOn(200, 210);
            bool[] mask = QcPixels.SubjectMask(shipped, empty);

            double a = QcPixels.SceneLinearOfMask(shipped, mask, out _, out _);
            double b = QcPixels.SceneLinearOfMask(brightened, mask, out _, out _);
            Assert.That(b, Is.GreaterThan(a), "the brighter rung is brighter on the model's own pixels");

            // …and a null mask is the whole frame, reported honestly rather than silently.
            QcPixels.SceneLinearOfMask(shipped, null, out double allPct, out _);
            Assert.That(allPct, Is.EqualTo(100.0).Within(1e-9));
        }

        [Test]
        public void BlackPercentIsAlsoOnTheFixedMask()
        {
            // blackOfSubject was the one number still read off the noAces rung after the means were
            // discounted, so it has to be on the same pixels too.
            byte[] empty = Flat(20);
            byte[] shipped = SubjectOn(20, 40);
            bool[] mask = QcPixels.SubjectMask(shipped, empty);

            byte[] modelWentBlack = SubjectOn(20, 0);
            QcPixels.SceneLinearOfMask(modelWentBlack, mask, out double pct, out double black);
            Assert.That(pct, Is.EqualTo(20.0).Within(1e-9));
            Assert.That(black, Is.EqualTo(100.0).Within(1e-9),
                "all twenty of the model's pixels are pure black, and none of the background counts");
        }
    }

    /// <summary>
    /// WO-E5j — the three questions that ask WHAT the black region is, without a theory attached.
    /// </summary>
    [TestFixture]
    public class BlackRegionTests
    {
        private const int N = 100;

        private static byte[] Frame(byte bg, byte region, int regionCount)
        {
            var b = new byte[N * 3];
            for (int i = 0; i < N; i++)
            {
                byte v = i < regionCount ? region : bg;
                int p = i * 3;
                b[p] = v; b[p + 1] = v; b[p + 2] = v;
            }
            return b;
        }

        [Test]
        public void BlackThatSurvivesTheMapBeingHiddenIsBackground()
        {
            // The dark region is present in BOTH frames and unchanged: nothing the map drew is
            // responsible for it.
            byte[] withMap = Frame(150, 10, 40);
            byte[] noMap = Frame(150, 10, 40);
            QcPixels.DarkOrigin(withMap, noMap, out double dark, out double fromMap, out double fromBg);
            Assert.That(dark, Is.EqualTo(40.0).Within(1e-9));
            Assert.That(fromMap, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(fromBg, Is.EqualTo(100.0).Within(1e-9));
        }

        [Test]
        public void BlackThatDisappearsWithTheMapIsSomethingTheMapDrew()
        {
            byte[] withMap = Frame(150, 10, 40);
            byte[] noMap = Frame(150, 150, 40);   // hide the map and that region becomes water
            QcPixels.DarkOrigin(withMap, noMap, out double dark, out double fromMap, out double fromBg);
            Assert.That(dark, Is.EqualTo(40.0).Within(1e-9));
            Assert.That(fromMap, Is.EqualTo(100.0).Within(1e-9));
            Assert.That(fromBg, Is.EqualTo(0.0).Within(1e-9));
        }

        [Test]
        public void TheDarkSampleIsSpreadAcrossTheRegionNotTakenFromItsEdge()
        {
            // A ray probe that fires twenty times into the top edge of the dark region measures the
            // top edge, not the region. Evenly spread indices are what make twenty rays a sample.
            byte[] frame = Frame(150, 10, 60);
            int[] sample = QcPixels.DarkPixelSample(frame, 20);
            Assert.That(sample.Length, Is.EqualTo(20));
            Assert.That(sample[0], Is.LessThan(5));
            Assert.That(sample[sample.Length - 1], Is.GreaterThan(50),
                "the sample has to reach the far end of the dark region");

            // Fewer dark pixels than asked for: take them all rather than inventing any.
            Assert.That(QcPixels.DarkPixelSample(Frame(150, 10, 7), 20).Length, Is.EqualTo(7));
            Assert.That(QcPixels.DarkPixelSample(Frame(150, 150, 0), 20).Length, Is.EqualTo(0));
        }

        [Test]
        public void TheTorchIsMeasuredOnExactlyTheSamePixelsBeforeAndAfter()
        {
            // The whole point of the lamp test is that the SAME pixels are compared, so a light
            // that brightens the background instead of the dark region cannot be mistaken for a
            // light that reached it.
            byte[] dark = Frame(150, 10, 40);
            int[] sample = QcPixels.DarkPixelSample(dark, 20);

            byte[] litRegion = Frame(150, 120, 40);      // the lamp reached the dark region
            byte[] litBackground = Frame(240, 10, 40);   // the lamp only lifted the background

            double before = QcPixels.MeanLuminanceAt(dark, sample);
            Assert.That(QcPixels.MeanLuminanceAt(litRegion, sample) / before, Is.GreaterThan(2.0),
                "a lamp that reaches the surface lifts it several times over");
            Assert.That(QcPixels.MeanLuminanceAt(litBackground, sample) / before,
                        Is.EqualTo(1.0).Within(1e-9),
                "…and one that does not touch those pixels must measure no change at all");
        }
    }
}
