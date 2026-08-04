using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// 🔴 The point of these tests is ONE claim, and every project decision downstream of WO-K
    /// rests on it: <b>a uniform additive wash leaves the pattern amplitude untouched and still
    /// destroys the spot score.</b>
    ///
    /// That claim is why "half the whale shark's spots are gone at the Unity stage" turned out to
    /// be a lighting bug rather than a texture, mip or compression bug, and why the fix was to
    /// take away env specular rather than to re-export the model. <see cref="AWashHidesSpots"/>
    /// pins it on a synthetic frame where the wash is exact and known, so nobody has to take the
    /// arithmetic on trust the next time a screenshot argues otherwise.
    /// </summary>
    public class QcFidelityTests
    {
        private const int W = 60;
        private const int H = 60;
        private const byte Background = 40;
        private const byte Base = 90;

        /// <summary>
        /// A frame with a regular grid of spots of GRADED amplitude, plus <paramref name="wash"/>
        /// added to every pixel. Graded on purpose: with one amplitude the spots are all on the
        /// same side of the detector's threshold and a wash either moves all of them or none, so
        /// the test would pass for a metric that cannot see the effect at all.
        /// </summary>
        private static byte[] Frame(int wash)
        {
            var buf = new byte[W * H * QcPixels.Channels];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int v = Base + wash;
                if (x % 5 == 2 && y % 5 == 2)
                {
                    int cell = (x / 5) * 7 + (y / 5) * 3;
                    v += 20 + 2 * (cell % 20);            // spot amplitudes 20…58
                }
                int p = (y * W + x) * QcPixels.Channels;
                buf[p] = buf[p + 1] = buf[p + 2] = (byte)v;
            }
            return buf;
        }

        private static byte[] Empty()
        {
            var buf = new byte[W * H * QcPixels.Channels];
            for (int i = 0; i < buf.Length; i++) buf[i] = Background;
            return buf;
        }

        [Test]
        public void BlurRadiusFollowsTheFrameDiagonal()
        {
            // WO-K's two radii, which is what makes its numbers and these comparable.
            Assert.AreEqual(18, QcFidelity.BlurRadius(1280, 720), "the CI QC frame");
            Assert.AreEqual(12, QcFidelity.BlurRadius(720, 720), "the offline reference render");
            Assert.AreEqual(QcFidelity.MinBlurRadius, QcFidelity.BlurRadius(8, 8),
                            "a tiny frame must not get a zero-radius detector");
        }

        [Test]
        public void BoxBlurOfAFlatFieldIsTheSameFlatField()
        {
            var a = new double[W * H];
            for (int i = 0; i < a.Length; i++) a[i] = 77.0;
            double[] blurred = QcFidelity.BoxBlur(a, W, H, 4);
            // Including the corners: the window is clamped and divided by its clamped area, so an
            // edge pixel is not quietly darkened by counting pixels that are not there.
            foreach (double v in blurred) Assert.AreEqual(77.0, v, 1e-9);
        }

        /// <summary>
        /// 🔴 THE ONE. Add 20 luminance levels to every pixel and nothing else:
        ///   • meanL rises by exactly 20            (the wash is visible, and measurable)
        ///   • hpRms does not move at all           (the pattern is completely intact)
        ///   • spotFrac falls anyway                (…and the spot score says it is not)
        /// This is the shape of the whale shark's 0.49, reproduced with no renderer involved.
        /// </summary>
        [Test]
        public void AWashHidesSpots()
        {
            byte[] off = Empty();
            QcFidelity.Pattern clean = QcFidelity.Measure(Frame(0), off, W, H);
            QcFidelity.Pattern washed = QcFidelity.Measure(Frame(20), off, W, H);

            Assert.AreEqual(W * H, clean.SubjectPx, "the whole synthetic frame is subject");
            Assert.AreEqual(clean.SubjectPx, washed.SubjectPx, "the wash must not move the mask");

            Assert.AreEqual(clean.MeanL + 20.0, washed.MeanL, 1e-6,
                            "meanL is the additive-wash detector and must track it exactly");
            Assert.AreEqual(clean.HpRms, washed.HpRms, 1e-6,
                            "🔴 the pattern amplitude is untouched by a wash — this is the finding");
            Assert.Less(washed.SpotFrac, clean.SpotFrac,
                        "🔴 …and the ratio-based spot score falls regardless, which is the trap");
            Assert.Less(washed.Contrast, clean.Contrast,
                        "contrast falls for the same reason: the numerator held, the divisor grew");
            Assert.Greater(clean.SpotFrac, 0.0, "the synthetic frame must actually contain spots");
        }

        [Test]
        public void AnEmptyFrameScoresNothingRatherThanWell()
        {
            byte[] off = Empty();
            QcFidelity.Pattern pat = QcFidelity.Measure(off, off, W, H);
            Assert.AreEqual(0, pat.SubjectPx);
            Assert.AreEqual(0.0, pat.MeanL, 1e-9);
            Assert.AreEqual(0.0, pat.SpotFrac, 1e-9);
            StringAssert.Contains("no-subject", QcFidelity.Line("empty", "msh:whaleshark", pat));
        }

        [Test]
        public void ABadReadbackIsNotAScore()
        {
            byte[] off = Empty();
            Assert.AreEqual(0, QcFidelity.Measure(null, off, W, H).SubjectPx);
            Assert.AreEqual(0, QcFidelity.Measure(Frame(0), null, W, H).SubjectPx);
            Assert.AreEqual(0, QcFidelity.Measure(Frame(0), new byte[7], W, H).SubjectPx,
                            "mismatched buffer lengths must not be measured");
        }

        [Test]
        public void OnlyTheWhaleSharkHasAMeasuredReference()
        {
            QcFidelity.Reference r;
            Assert.IsTrue(QcFidelity.TryReference("msh:whaleshark", out r));
            Assert.AreEqual(0.1264, r.SpotFrac, 1e-9, "WO-K's offline render of the shipped GLB");
            Assert.AreEqual(94.75, r.MeanL, 1e-9);
            Assert.AreEqual(r.Contrast * r.MeanL, r.HpRms, 0.01,
                            "the reference row must stay internally consistent");

            // 🔴 Everything else prints ref=none on purpose. A borrowed reference is not evidence.
            Assert.IsFalse(QcFidelity.TryReference("msh:manta", out r));
            Assert.IsFalse(QcFidelity.TryReference(null, out r));
        }

        [Test]
        public void TheLineCarriesTheAbsolutesEvenWithoutAReference()
        {
            byte[] off = Empty();
            QcFidelity.Pattern pat = QcFidelity.Measure(Frame(0), off, W, H);

            string none = QcFidelity.Line("manta", "msh:manta", pat);
            StringAssert.Contains("[QCFidelity] manta", none);
            StringAssert.Contains("meanL=", none);
            StringAssert.Contains("hpRms=", none);
            StringAssert.Contains("spotFrac=", none);
            StringAssert.Contains("ref=none", none);
            StringAssert.DoesNotContain("spotRetention", none);

            string withRef = QcFidelity.Line("whaleshark", "msh:whaleshark", pat);
            StringAssert.Contains("spotRetention=", withRef);
            StringAssert.Contains("hpRmsRetention=", withRef);
            StringAssert.Contains("wash=", withRef);
        }
    }
}
