using NUnit.Framework;
using DiveMap.Core;

namespace DiveMap.Tests
{
    /// <summary>
    /// The two-finger resize that replaced the − / + stepper.
    ///
    /// What is worth testing here is not "does multiplication work" but the three ways a pinch goes
    /// wrong on a real phone: it drifts, it divides by zero when the fingers meet, and it lets the
    /// user shrink the site until it cannot be found again. All three are things a screenshot
    /// cannot show and a user reports as "it feels weird".
    /// </summary>
    public class ArPinchTests
    {
        private const double Span = 340;   // the demo map, in world units

        [Test]
        public void SizeAndScaleAreInversesOfEachOther()
        {
            double scale = ArPinch.ScaleFor(Span, 1.1);
            Assert.AreEqual(1.1, ArPinch.MetresFor(Span, scale), 1e-12);
        }

        [Test]
        public void FingersTwiceAsFarApart_MakeItTwiceAsWide()
        {
            Assert.AreEqual(2.0, ArPinch.Pinch(1.0, 200, 400), 1e-12);
            Assert.AreEqual(0.5, ArPinch.Pinch(1.0, 400, 200), 1e-12);
        }

        [Test]
        public void ReturningTheFingersReturnsTheSize_Exactly()
        {
            // The reason the gesture is absolute rather than accumulated. With per-frame ratios,
            // moving out and back leaves a residue that grows with every gesture, and the map ends
            // up a different size from where it started with nothing to blame.
            const double start = 1.1;
            double wandered = start;
            double[] path = { 200, 260, 410, 330, 180, 200 };
            foreach (double px in path) wandered = ArPinch.Pinch(start, 200, px);
            Assert.AreEqual(start, wandered, 1e-12);
        }

        [Test]
        public void FingersTouching_DoesNotSendTheSizeToInfinity()
        {
            // Two fingers a pixel apart is either the start of a gesture or a bad touch report.
            // Dividing by it puts the map at astronomical scale in one frame — the map disappears
            // and no input can bring it back.
            Assert.AreEqual(ArPinch.Clamp(1.1), ArPinch.Pinch(1.1, 0, 300), 1e-12);
            Assert.AreEqual(ArPinch.Clamp(1.1), ArPinch.Pinch(1.1, 300, 0), 1e-12);
            Assert.AreEqual(ArPinch.Clamp(1.1), ArPinch.Pinch(1.1, double.NaN, 300), 1e-12);
        }

        [Test]
        public void ShrinkingForeverCannotLoseTheMap()
        {
            double m = ArPinch.Pinch(1.1, 1000, 10);
            Assert.AreEqual(ArPinch.MinMetres, m, 1e-12);
            Assert.IsTrue(ArPinch.AtLimit(m));
            // Still something a hand can find on a table, which is the point of the stop.
            Assert.Greater(m, 0.1);
        }

        [Test]
        public void GrowingForeverStopsBeforeItSwallowsTheRoom()
        {
            double m = ArPinch.Pinch(1.1, 10, 1000);
            Assert.AreEqual(ArPinch.MaxMetres, m, 1e-12);
            Assert.IsTrue(ArPinch.AtLimit(m));
        }

        [Test]
        public void InsideTheLimits_NothingReadsAsAStop()
        {
            Assert.IsFalse(ArPinch.AtLimit(1.1));
        }

        [Test]
        public void TheDefaultTabletopSizeIsWithinReach()
        {
            // ArKitSession opens at 1.1 m (the web's tabletop size). If that sat outside the pinch
            // limits the map would jump the moment two fingers touched it.
            Assert.GreaterOrEqual(1.1, ArPinch.MinMetres);
            Assert.LessOrEqual(1.1, ArPinch.MaxMetres);
            Assert.AreEqual(1.1, ArPinch.Clamp(1.1), 1e-12);
        }

        [Test]
        public void NonsenseSizesDoNotProduceNonsenseScales()
        {
            Assert.AreEqual(ArPinch.MinMetres, ArPinch.Clamp(double.NaN), 1e-12);
            Assert.AreEqual(0, ArPinch.ScaleFor(Span, 0), 1e-12);
            Assert.AreEqual(0, ArPinch.MetresFor(Span, 0), 1e-12);
            Assert.AreEqual(0, ArPinch.MetresFor(Span, double.NaN), 1e-12);
        }

        [Test]
        public void AMapOfAnySize_PinchesToTheSameMetres()
        {
            // The whole reason the gesture is expressed in metres rather than in a scale factor: a
            // 40-unit reef and a 900-unit wreck site have to behave identically in the hand.
            foreach (double span in new[] { 40.0, 340.0, 900.0 })
            {
                double scale = ArPinch.ScaleFor(span, 1.1);
                double doubled = ArPinch.Pinch(ArPinch.MetresFor(span, scale), 200, 400);
                Assert.AreEqual(2.2, doubled, 1e-12);
            }
        }
    }
}
