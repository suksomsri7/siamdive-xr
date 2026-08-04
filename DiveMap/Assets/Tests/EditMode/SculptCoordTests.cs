using System;
using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// The seabed the user stands on, in the terms they see it: "the trench I dug on the north
    /// side of the wreck has to still be north of the wreck when the app draws it".
    /// </summary>
    public class SculptCoordTests
    {
        private const int Rings = 28, Seg = 96;      // the web's grid (builder.html:537)

        /// <summary>Index of one web vertex.</summary>
        private static int Web(int ring, int seg) => 1 + (ring - 1) * Seg + seg;

        /// <summary>Index of one app vertex.</summary>
        private static int App(int ring, int j) => (ring - 1) * Seg + j;

        /// <summary>Unity local x/z of an app sample, the way SculptBrush.SampleXZ places it.</summary>
        private static void AppXZ(int ring, int j, out double x, out double z)
        {
            double a = 2.0 * Math.PI * j / Seg;
            double r = (double)ring / Rings;
            x = Math.Cos(a) * r; z = Math.Sin(a) * r;
        }

        /// <summary>Web local x/z of a web sample (builder.html:548).</summary>
        private static void WebXZ(int ring, int s, out double x, out double z)
        {
            double a = 2.0 * Math.PI * s / Seg;
            double r = (double)ring / Rings;
            x = Math.Cos(a) * r; z = Math.Sin(a) * r;
        }

        /// <summary>
        /// THE test. A pit dug at one spot on the web floor has to end up at the SAME spot on
        /// the app's floor — same place on the map, allowing for Unity's z running backwards.
        /// Checked all the way round the compass, not just at the four cardinal points.
        /// </summary>
        [Test]
        public void APitKeepsItsPlaceOnTheMap()
        {
            foreach (int ring in new[] { 1, 7, 14, 27, 28 })
            {
                foreach (int s in new[] { 0, 1, 12, 24, 47, 48, 71, 95 })
                {
                    var web = new float[SculptCoord.WebLength(Rings, Seg)];
                    web[Web(ring, s)] = -97f;                       // Atlantis' deepest trench

                    float[] app = SculptCoord.WebToApp(web, Rings, Seg);

                    // find where the app put it
                    int found = -1;
                    for (int i = 0; i < app.Length; i++)
                    {
                        if (app[i] == 0f) continue;
                        Assert.AreEqual(-1, found, "the pit was copied to more than one place");
                        found = i;
                    }
                    Assert.AreNotEqual(-1, found, $"the pit at web ring {ring} seg {s} vanished");
                    Assert.AreEqual(-97f, app[found], 1e-6f);

                    int foundRing = found / Seg + 1, foundJ = found % Seg;
                    Assert.AreEqual(ring, foundRing, "a pit must not change how far out it is");

                    WebXZ(ring, s, out double wx, out double wz);
                    AppXZ(foundRing, foundJ, out double ax, out double az);

                    // same point of the map: Unity x is the web's x, Unity z is its negative.
                    Assert.AreEqual(wx, ax, 1e-9, $"east/west moved (web ring {ring} seg {s})");
                    Assert.AreEqual(-wz, az, 1e-9, $"north/south moved (web ring {ring} seg {s})");
                }
            }
        }

        /// <summary>
        /// The build-261 behaviour, pinned: read raw, a pit on the map's north side was drawn on
        /// its south side and one segment over.
        /// </summary>
        [Test]
        public void ReadingTheWebArrayRaw_PutsThePitOnTheWrongSide()
        {
            const int ring = Rings, s = Seg / 4;            // due web +Z ("north" on the builder)
            var web = new float[SculptCoord.WebLength(Rings, Seg)];
            web[Web(ring, s)] = -97f;

            // what shipped: env.sculpt handed straight to HeightAt, index (r-1)*seg + j
            int rawIndex = Web(ring, s);
            int rawRing = rawIndex / Seg + 1, rawJ = rawIndex % Seg;
            AppXZ(rawRing, rawJ, out _, out double rawZ);

            WebXZ(ring, s, out _, out double wantWebZ);
            AppXZ(ring, SculptCoord.MirrorSegment(s, Seg), out _, out double fixedZ);

            Assert.Greater(wantWebZ, 0.5, "sanity: the pit is on the web's +Z side");
            Assert.Less(fixedZ, -0.5, "the fix puts it on Unity's -Z side, which is the same place");
            Assert.Greater(rawZ, 0.5, "and the shipped code put it on the opposite side of the map");
        }

        [Test]
        public void AppToWeb_IsTheInverseOfWebToApp()
        {
            var rng = new Random(7);
            var app = new float[SculptCoord.AppLength(Rings, Seg)];
            for (int i = 0; i < app.Length; i++) app[i] = (float)(rng.NextDouble() * 40 - 20);

            float[] back = SculptCoord.WebToApp(SculptCoord.AppToWeb(app, Rings, Seg), Rings, Seg);
            Assert.AreEqual(app.Length, back.Length);
            for (int i = 0; i < app.Length; i++) Assert.AreEqual(app[i], back[i], 1e-6f, "sample " + i);
        }

        [Test]
        public void MirrorSegment_IsItsOwnInverse_AndKeepsSegmentZero()
        {
            Assert.AreEqual(0, SculptCoord.MirrorSegment(0, Seg), "angle 0 is on the mirror line");
            Assert.AreEqual(Seg / 2, SculptCoord.MirrorSegment(Seg / 2, Seg), "so is 180°");
            for (int j = 0; j < Seg; j++)
            {
                int m = SculptCoord.MirrorSegment(j, Seg);
                Assert.GreaterOrEqual(m, 0);
                Assert.Less(m, Seg);
                Assert.AreEqual(j, SculptCoord.MirrorSegment(m, Seg));
            }
        }

        /// <summary>A floor tilted towards the web's +Z must be tilted towards Unity's −Z.</summary>
        [Test]
        public void SlopeZ_Flips_AndIsItsOwnInverse()
        {
            Assert.AreEqual(-0.35, SculptCoord.SlopeZ(0.35), 1e-12);
            Assert.AreEqual(0.35, SculptCoord.SlopeZ(SculptCoord.SlopeZ(0.35)), 1e-12);
            Assert.AreEqual(0.0, SculptCoord.SlopeZ(0.0), 1e-12);

            // the deep end stays the deep end: web height at (0,0,+1) == app height at (0,0,-1)
            const double slopeZ = 0.35;
            double webDeep = 1.0 * slopeZ;
            double appDeep = -1.0 * SculptCoord.SlopeZ(slopeZ);
            Assert.AreEqual(webDeep, appDeep, 1e-12);
        }

        [Test]
        public void ShortLegacyArrays_ArePassedThrough_NotMirrored()
        {
            var legacy = new float[SculptCoord.AppLength(Rings, Seg)];   // an older app's save
            legacy[App(3, 5)] = 12f;

            float[] app = SculptCoord.WebToApp(legacy, Rings, Seg);
            Assert.AreEqual(12f, app[App(3, 5)], 1e-6f);
        }

        [Test]
        public void Null_And_DegenerateGrids_DoNotThrow()
        {
            Assert.IsNull(SculptCoord.WebToApp(null, Rings, Seg));
            Assert.IsNull(SculptCoord.AppToWeb(null, Rings, Seg));
            var a = new float[4];
            Assert.AreSame(a, SculptCoord.WebToApp(a, 0, Seg));
            Assert.AreSame(a, SculptCoord.AppToWeb(a, Rings, 0));
        }
    }
}
