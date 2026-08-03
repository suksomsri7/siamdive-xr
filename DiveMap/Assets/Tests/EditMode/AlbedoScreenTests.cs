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
}
