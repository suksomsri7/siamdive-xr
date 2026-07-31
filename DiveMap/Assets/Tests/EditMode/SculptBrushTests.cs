using System;
using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// Unit tests for seabed sculpting.
    ///
    /// The risk here is silent and geometric: the app stores the floor as a POLAR grid while the
    /// web stores it as a Cartesian one, so an index conversion that is subtly wrong still
    /// produces a plausible-looking dune — just not where the player put their finger. That
    /// cannot be seen in a screenshot, only measured.
    /// </summary>
    public class SculptBrushTests
    {
        private const int Rings = 28, Seg = 96;      // the grid SceneBuilder uses
        private const float SandR = 300f;

        private static float[] Flat() => new float[Rings * Seg];

        // ── index → position ─────────────────────────────────────────────────────

        [Test]
        public void SampleXZ_MatchesSculptAtsIndexing()
        {
            // SeabedView.SculptAt reads (ring-1)*seg + j with ring starting at 1, so index 0 is
            // the FIRST ring, not the centre — and its radius is one ring out, not zero.
            SculptBrush.SampleXZ(0, Rings, Seg, SandR, out float x, out float z);
            float r = (float)Math.Sqrt(x * x + z * z);
            Assert.AreEqual(SandR / Rings, r, 0.01f, "index 0 sits on ring 1");
            Assert.AreEqual(0f, Math.Atan2(z, x), 1e-5, "j=0 is angle 0");
        }

        [Test]
        public void SampleXZ_LastIndexIsOnTheRim()
        {
            SculptBrush.SampleXZ(Rings * Seg - 1, Rings, Seg, SandR, out float x, out float z);
            Assert.AreEqual(SandR, (float)Math.Sqrt(x * x + z * z), 0.01f);
        }

        [Test]
        public void SampleXZ_WalksAllTheWayRoundOneRing()
        {
            // A quarter of the way through the first ring must be a quarter turn.
            SculptBrush.SampleXZ(Seg / 4, Rings, Seg, SandR, out float x, out float z);
            Assert.AreEqual(Math.PI / 2, Math.Atan2(z, x), 1e-3);
        }

        [Test]
        public void SampleXZ_ToleratesNonsense()
        {
            SculptBrush.SampleXZ(-1, Rings, Seg, SandR, out float x, out float z);
            Assert.AreEqual(0f, x); Assert.AreEqual(0f, z);
            SculptBrush.SampleXZ(0, 0, 0, SandR, out x, out z);
            Assert.AreEqual(0f, x); Assert.AreEqual(0f, z);
        }

        // ── stroke ───────────────────────────────────────────────────────────────

        [Test]
        public void Stroke_RaisesUnderTheBrushAndLeavesTheRestAlone()
        {
            float[] h = Flat();
            // Brush centred on the sample at index 0.
            SculptBrush.SampleXZ(0, Rings, Seg, SandR, out float cx, out float cz);
            int touched = SculptBrush.Stroke(h, Rings, Seg, SandR, cx, cz, 40f, 4f, raise: true);

            Assert.Greater(touched, 0, "something must move");
            Assert.Greater(h[0], 0f, "the centre of the brush rises most");

            // A sample on the far side of the map must be untouched.
            int far = Rings * Seg / 2 + Seg / 2;
            Assert.AreEqual(0f, h[far], 1e-6, "the brush is local, not global");
        }

        [Test]
        public void Stroke_DigsWhenRaiseIsFalse()
        {
            float[] h = Flat();
            SculptBrush.SampleXZ(0, Rings, Seg, SandR, out float cx, out float cz);
            SculptBrush.Stroke(h, Rings, Seg, SandR, cx, cz, 40f, 4f, raise: false);
            Assert.Less(h[0], 0f);
        }

        [Test]
        public void Stroke_FallsOffToZeroAtTheEdgeOfTheBrush()
        {
            // cos falloff: exactly at d = R the contribution is 0, so there is no visible step
            // where the brush ends. A linear falloff would leave a rim.
            float[] h = Flat();
            SculptBrush.SampleXZ(0, Rings, Seg, SandR, out float cx, out float cz);
            SculptBrush.Stroke(h, Rings, Seg, SandR, cx, cz, 40f, 10f, raise: true);

            float peak = h[0];
            float furthest = 0f;
            for (int i = 0; i < h.Length; i++)
            {
                SculptBrush.SampleXZ(i, Rings, Seg, SandR, out float x, out float z);
                float d = (float)Math.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz));
                if (d > 36f && d < 40f) furthest = Math.Max(furthest, h[i]);
            }
            Assert.Greater(peak, furthest * 4f, "the edge of the stroke is far weaker than its centre");
        }

        [Test]
        public void Stroke_IsAdditiveSoRepeatedStrokesDeepenTheSamePit()
        {
            float[] h = Flat();
            SculptBrush.SampleXZ(0, Rings, Seg, SandR, out float cx, out float cz);
            SculptBrush.Stroke(h, Rings, Seg, SandR, cx, cz, 40f, 4f, raise: true);
            float once = h[0];
            SculptBrush.Stroke(h, Rings, Seg, SandR, cx, cz, 40f, 4f, raise: true);
            Assert.AreEqual(once * 2f, h[0], 1e-4, "a second pass over the same spot doubles it");
        }

        [Test]
        public void Stroke_ClampsRadiusAndStrength()
        {
            float[] a = Flat(), b = Flat();
            SculptBrush.SampleXZ(0, Rings, Seg, SandR, out float cx, out float cz);
            SculptBrush.Stroke(a, Rings, Seg, SandR, cx, cz, 99999f, 99999f, true);
            SculptBrush.Stroke(b, Rings, Seg, SandR, cx, cz,
                               SculptBrush.MaxRadius, SculptBrush.MaxStrength, true);
            Assert.AreEqual(b[0], a[0], 1e-4, "an absurd slider value is clamped, not obeyed");
        }

        [Test]
        public void Stroke_ToleratesNulls()
        {
            Assert.AreEqual(0, SculptBrush.Stroke(null, Rings, Seg, SandR, 0, 0, 40, 4, true));
            Assert.AreEqual(0, SculptBrush.Stroke(Flat(), 0, 0, SandR, 0, 0, 40, 4, true));
        }

        // ── noise ────────────────────────────────────────────────────────────────

        [Test]
        public void Noise_IsDeterministicForASeed()
        {
            float[] a = Flat(), b = Flat();
            SculptBrush.Noise(a, Rings, Seg, SandR, 30f, 12345);
            SculptBrush.Noise(b, Rings, Seg, SandR, 30f, 12345);
            for (int i = 0; i < a.Length; i++) Assert.AreEqual(a[i], b[i], 1e-6);
        }

        [Test]
        public void Noise_DiffersBetweenSeeds()
        {
            float[] a = Flat(), b = Flat();
            SculptBrush.Noise(a, Rings, Seg, SandR, 30f, 1);
            SculptBrush.Noise(b, Rings, Seg, SandR, 30f, 2);

            bool anyDifferent = false;
            for (int i = 0; i < a.Length && !anyDifferent; i++)
                if (Math.Abs(a[i] - b[i]) > 1e-4) anyDifferent = true;
            Assert.IsTrue(anyDifferent);
        }

        [Test]
        public void Noise_FadesToFlatAtTheRim()
        {
            // The floor must meet its own edge cleanly, or the seabed skirt shows a jagged seam.
            float[] h = Flat();
            SculptBrush.Noise(h, Rings, Seg, SandR, 40f, 7);

            float rimMax = 0f, innerMax = 0f;
            for (int i = 0; i < h.Length; i++)
            {
                SculptBrush.SampleXZ(i, Rings, Seg, SandR, out float x, out float z);
                float rad = (float)Math.Sqrt(x * x + z * z) / SandR;
                if (rad > 0.98f) rimMax = Math.Max(rimMax, Math.Abs(h[i]));
                else if (rad < 0.5f) innerMax = Math.Max(innerMax, Math.Abs(h[i]));
            }
            Assert.Less(rimMax, 0.01f, "the rim is flat");
            Assert.Greater(innerMax, 0.5f, "…but the middle is not");
        }

        [Test]
        public void Noise_RespectsAmplitude()
        {
            float[] small = Flat(), big = Flat();
            SculptBrush.Noise(small, Rings, Seg, SandR, 10f, 99);
            SculptBrush.Noise(big, Rings, Seg, SandR, 40f, 99);

            float sMax = 0f, bMax = 0f;
            for (int i = 0; i < small.Length; i++)
            {
                sMax = Math.Max(sMax, Math.Abs(small[i]));
                bMax = Math.Max(bMax, Math.Abs(big[i]));
            }
            Assert.AreEqual(4f, bMax / Math.Max(1e-6f, sMax), 0.01f);
        }

        // ── reset / helpers ──────────────────────────────────────────────────────

        [Test]
        public void Reset_FlattensEverything()
        {
            float[] h = Flat();
            SculptBrush.Noise(h, Rings, Seg, SandR, 30f, 5);
            SculptBrush.Reset(h);
            foreach (float v in h) Assert.AreEqual(0f, v);
            Assert.DoesNotThrow(() => SculptBrush.Reset(null));
        }

        [Test]
        public void SmoothStep_IsTheWebsSbStep()
        {
            Assert.AreEqual(0f, SculptBrush.SmoothStep(0, 1, -1), 1e-6);
            Assert.AreEqual(0.5f, SculptBrush.SmoothStep(0, 1, 0.5f), 1e-6);
            Assert.AreEqual(1f, SculptBrush.SmoothStep(0, 1, 2), 1e-6);
            Assert.AreEqual(0f, SculptBrush.SmoothStep(1, 1, 0), 1e-6, "a zero-width step must not divide by zero");
        }

        [Test]
        public void Hash_StaysInRangeAndDoesNotThrowOnOverflow()
        {
            // The web's hash relies on 32-bit wraparound; a checked context would throw.
            for (int i = -5; i <= 5; i++)
            {
                float v = SculptBrush.Hash(i * 100000, i * 999983, 7);
                Assert.GreaterOrEqual(v, -1f);
                Assert.LessOrEqual(v, 1f);
            }
        }

        [Test]
        public void DepthMetres_MatchesTheWebsReadout()
        {
            // (waterLevel − topY) / 6, and a floor ABOVE the water reads negative.
            Assert.AreEqual(10f, SculptBrush.DepthMetres(240f, 180f), 1e-4);
            Assert.Less(SculptBrush.DepthMetres(240f, 300f), 0f);
            Assert.AreEqual(10f, SculptBrush.DepthMetres(240f, 180f, 0f), 1e-4, "a zero scale falls back to 6");
        }
    }
}
