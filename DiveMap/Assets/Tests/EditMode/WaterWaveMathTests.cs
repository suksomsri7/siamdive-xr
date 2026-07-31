using System;
using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// Unit tests for the sea surface.
    ///
    /// The bug these exist to prevent already happened once: the runtime had a wave, it moved, it
    /// looked plausible in isolation, and it was four times too flat because the constants were
    /// invented rather than read from the web. Nothing failed. So these tests assert the actual
    /// NUMBERS against the web's line (builder.html:3931), not just that "there is a wave".
    /// </summary>
    public class WaterWaveMathTests
    {
        /// <summary>The web's expression, transcribed independently as the oracle.</summary>
        private static double Web(double webX, double webY, double t)
        {
            return Math.Sin(webX * 0.03 + t * 1.1) * 3
                 + Math.Cos(webY * 0.045 + t * 0.85) * 2.4
                 + Math.Sin((webX + webY) * 0.02 + t * 0.6) * 1.6;
        }

        [Test]
        public void MatchesTheWebAtEveryPointItWasCheckedAt()
        {
            // The web's local y is our −z (its plane is rotated −90° about X). If that mapping is
            // ever "simplified" away, these comparisons break — which is the point of them.
            double[] xs = { 0, 12.5, -87, 340, -340 };
            double[] zs = { 0, -3.25, 210, -170, 88 };
            double[] ts = { 0, 0.4, 7.9, 123.75 };

            foreach (double x in xs)
            foreach (double z in zs)
            foreach (double t in ts)
            {
                Assert.AreEqual(Web(x, -z, t), WaterWaveMath.Height(x, z, t), 1e-12,
                                $"({x},{z}) at t={t}");
            }
        }

        [Test]
        public void TheSeaIsAsTallAsTheWebsSea()
        {
            // The regression that prompted these tests was amplitude, so it is asserted directly:
            // 3 + 2.4 + 1.6 = 7, and a sampled sweep must come close to it.
            Assert.AreEqual(7.0, WaterWaveMath.MaxAmplitude, 1e-12);

            double peak = 0;
            for (int i = 0; i < 4000; i++)
            {
                double t = i * 0.05;
                peak = Math.Max(peak, Math.Abs(WaterWaveMath.Height(i * 1.7, i * -2.3, t)));
            }
            Assert.Greater(peak, 5.0, "a real swell, not the 1.6-unit ripple this replaced");
            Assert.LessOrEqual(peak, 7.0 + 1e-9, "and never more than the three terms can give");
        }

        [Test]
        public void TheSurfaceIsNeverFlat()
        {
            // Three terms with unrelated periods: at any instant the sea must have shape, or the
            // light through it has nothing to break up.
            foreach (double t in new double[] { 0, 1.3, 6.28, 40 })
            {
                double lo = double.MaxValue, hi = double.MinValue;
                for (int i = 0; i < 200; i++)
                {
                    double h = WaterWaveMath.Height(i * 3.4 - 340, i * 3.1 - 310, t);
                    lo = Math.Min(lo, h);
                    hi = Math.Max(hi, h);
                }
                Assert.Greater(hi - lo, 3.0, $"at t={t} the surface is nearly level");
            }
        }

        [Test]
        public void ItMoves()
        {
            // A wave that does not change with time is a bumpy plate.
            double a = WaterWaveMath.Height(20, -35, 0);
            double b = WaterWaveMath.Height(20, -35, 1.0);
            Assert.Greater(Math.Abs(a - b), 0.2);
        }

        [Test]
        public void TheCrestsAreNotAllParallel()
        {
            // One train would make height depend on a single direction: walk along the crest line
            // and nothing changes. Here, moving along X at fixed Z and along Z at fixed X must both
            // change the height, and by different amounts.
            const double t = 2.5;
            double alongX = Math.Abs(WaterWaveMath.Height(0, 0, t) - WaterWaveMath.Height(52, 0, t));
            double alongZ = Math.Abs(WaterWaveMath.Height(0, 0, t) - WaterWaveMath.Height(0, 52, t));

            Assert.Greater(alongX, 0.1);
            Assert.Greater(alongZ, 0.1);
            Assert.Greater(Math.Abs(alongX - alongZ), 0.05, "the two axes must not be twins");
        }

        [Test]
        public void SameInputSameHeight()
        {
            // The mesh is rewritten from a cached base every other frame; a formula with any state
            // in it would make the sea creep upward over a long dive.
            Assert.AreEqual(WaterWaveMath.Height(11, 22, 3.5), WaterWaveMath.Height(11, 22, 3.5), 0);
        }

        [Test]
        public void HandlesTheWholeMapWithoutBlowingUp()
        {
            // The seabed reaches 340 units and maps can stretch past it; every vertex on the disc
            // goes through here, so an infinity or a NaN would take the whole surface with it.
            foreach (double x in new double[] { -900, 0, 900 })
            foreach (double z in new double[] { -900, 0, 900 })
            {
                double h = WaterWaveMath.Height(x, z, 999.0);
                Assert.IsFalse(double.IsNaN(h) || double.IsInfinity(h));
                Assert.LessOrEqual(Math.Abs(h), 7.0 + 1e-9);
            }
        }
    }
}
