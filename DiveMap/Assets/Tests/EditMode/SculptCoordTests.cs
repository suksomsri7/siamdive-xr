using System;
using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// The sculpted floor, in the terms a user sees it: "the trench I dug beside the wreck has
    /// to still be beside the wreck when the app draws it."
    ///
    /// Scope note: these tests cover the NUMBERING only. The suspected angular mirror
    /// (app segment j vs web segment seg−j, and the sign of areaSlopeZ) is documented on
    /// SculptCoord and deliberately NOT implemented — no independent picture of the sand has
    /// been taken that shows it, and a floor is not reshaped on an argument alone.
    /// </summary>
    public class SculptCoordTests
    {
        private const int Rings = 28, Seg = 96;      // the web's grid (builder.html:537)

        private static int Web(int ring, int seg) => 1 + (ring - 1) * Seg + seg;
        private static int App(int ring, int j) => (ring - 1) * Seg + j;

        /// <summary>
        /// The arithmetic that proves the off-by-one without rendering anything: the real
        /// Atlantis map ships env.sculpt with 2689 values for a 28×96 grid, and 28·96 = 2688.
        /// The extra one is the web's centre vertex, sitting in front of everything else.
        /// </summary>
        [Test]
        public void TheWebArrayIsOneLongerThanTheAppsGrid_ThatOneIsTheCentre()
        {
            Assert.AreEqual(2689, SculptCoord.WebLength(Rings, Seg), "builder.html SB_TOPN");
            Assert.AreEqual(2688, SculptCoord.AppLength(Rings, Seg), "rings × seg");
            Assert.AreEqual(1, SculptCoord.WebLength(Rings, Seg) - SculptCoord.AppLength(Rings, Seg));

            Assert.IsTrue(SculptCoord.IsWebLayout(new float[2689], Rings, Seg), "Atlantis' own array");
            Assert.IsFalse(SculptCoord.IsWebLayout(new float[2688], Rings, Seg));
        }

        /// <summary>
        /// A pit keeps its ring AND its segment — it must not slide around the compass, and it
        /// must not move in or out from the middle of the map.
        /// </summary>
        [Test]
        public void APitKeepsItsRingAndSegment()
        {
            foreach (int ring in new[] { 1, 7, 14, 27, 28 })
            {
                foreach (int s in new[] { 0, 1, 12, 24, 47, 48, 71, 95 })
                {
                    var web = new float[SculptCoord.WebLength(Rings, Seg)];
                    web[Web(ring, s)] = -97f;                       // Atlantis' deepest trench

                    float[] app = SculptCoord.WebToApp(web, Rings, Seg);

                    int found = -1;
                    for (int i = 0; i < app.Length; i++)
                    {
                        if (app[i] == 0f) continue;
                        Assert.AreEqual(-1, found, "the pit was copied to more than one place");
                        found = i;
                    }
                    Assert.AreNotEqual(-1, found, $"the pit at web ring {ring} seg {s} vanished");
                    Assert.AreEqual(-97f, app[found], 1e-6f);
                    Assert.AreEqual(App(ring, s), found,
                        $"web ring {ring} seg {s} must land on the app's ring {ring} seg {s}");
                }
            }
        }

        /// <summary>
        /// The shipped behaviour, pinned: handed straight to HeightAt, the web's CENTRE height
        /// was read as ring 1 segment 0 and every sample after it was one segment out.
        /// </summary>
        [Test]
        public void ReadingTheWebArrayRaw_IsOneSegmentOut()
        {
            var web = new float[SculptCoord.WebLength(Rings, Seg)];
            web[0] = -50f;                    // the centre of the floor
            web[Web(1, 0)] = -97f;            // ring 1, segment 0

            // raw: the app's index 0 (ring 1, seg 0) picked up the CENTRE's height
            Assert.AreEqual(-50f, web[App(1, 0)], 1e-6f, "raw read hands ring1/seg0 the centre value");

            // fixed: it picks up ring 1 segment 0, as it should
            float[] app = SculptCoord.WebToApp(web, Rings, Seg);
            Assert.AreEqual(-97f, app[App(1, 0)], 1e-6f);

            // and the shift is exactly one slot, all the way along
            for (int i = 0; i < 200; i++) Assert.AreEqual(web[i + 1], app[i], 1e-6f, "sample " + i);
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

        /// <summary>A save from this app must be the length the web expects to read.</summary>
        [Test]
        public void AppToWeb_WritesTheWebsOwnLength()
        {
            var app = new float[SculptCoord.AppLength(Rings, Seg)];
            app[0] = -12f;
            float[] web = SculptCoord.AppToWeb(app, Rings, Seg);
            Assert.AreEqual(2689, web.Length);
            Assert.AreEqual(-12f, web[0], 1e-6f, "the centre is filled from the innermost ring");
            Assert.AreEqual(-12f, web[Web(1, 0)], 1e-6f);
        }

        [Test]
        public void ShortLegacyArrays_ArePassedThroughUnshifted()
        {
            var legacy = new float[SculptCoord.AppLength(Rings, Seg)];   // an older app's save
            legacy[App(3, 5)] = 12f;

            float[] app = SculptCoord.WebToApp(legacy, Rings, Seg);
            Assert.AreEqual(12f, app[App(3, 5)], 1e-6f, "a map sculpted in the app must not move");
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
