using System;
using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// The sculpted floor, in the terms a user sees it: "the trench I dug beside the wreck has
    /// to still be beside the wreck when the app draws it."
    ///
    /// 🔴 16 ส.ค. 2026 — การสะท้อนเชิงมุมถูกแก้แล้ว (เจ้าของงานรายงาน "ปรับระดับในเว็บ เปิดใน
    /// Unity กลับหัวกลับหาง" ซึ่งตรงกับที่คำนวณค้างไว้) ⇒ เทสชุดนี้ครอบทั้ง "เลขดัชนี" และ
    /// "ทิศ" แล้ว · ส่วนเครื่องหมายของ areaSlopeZ อยู่ที่ SceneBuilder (คนละไฟล์ ตั้งใจให้
    /// หมายเหตุชี้หากันไว้ เพราะแก้อันเดียวจะได้ร่องถูกที่แต่พื้นเอียงผิดทาง)
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
        /// หลุมต้องอยู่วงเดิม (ระยะจากกลางแมพเท่าเดิม) และไปอยู่เซกเมนต์ที่ "สะท้อน" ของมัน —
        /// เพราะ Unity z = −(web z) ⇒ มุม θ ของเว็บ = มุม −θ ของ Unity
        /// </summary>
        [Test]
        public void APitKeepsItsRingAndLandsOnTheMirroredSegment()
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
                    int mirrored = SculptCoord.MirrorSeg(s, Seg);
                    Assert.AreEqual(App(ring, mirrored), found,
                        $"web ring {ring} seg {s} must land on the app's ring {ring} seg {mirrored}");
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

            // fixed: ring 1 segment 0 คือจุดบนแกน +x ซึ่งเป็นจุดเดียวที่การสะท้อนไม่ขยับ (seg 0)
            float[] app = SculptCoord.WebToApp(web, Rings, Seg);
            Assert.AreEqual(-97f, app[App(1, 0)], 1e-6f);

            // ทุกช่องอ่านจากช่องที่สะท้อนแล้ว — ไม่ใช่เลื่อนหนึ่งช่องเฉยๆ อีกต่อไป
            for (int r = 1; r <= 3; r++)
                for (int j = 0; j < Seg; j++)
                    Assert.AreEqual(web[Web(r, SculptCoord.MirrorSeg(j, Seg))], app[App(r, j)], 1e-6f,
                                    $"ring {r} seg {j}");
        }

        /// <summary>การสะท้อนต้องเป็นฟังก์ชันที่สลับกลับตัวเองได้ ไม่งั้นเซฟแล้วเปิดใหม่จะเพี้ยนสะสม</summary>
        [Test]
        public void MirroringTwiceIsTheIdentity()
        {
            for (int j = 0; j < Seg; j++)
                Assert.AreEqual(j, SculptCoord.MirrorSeg(SculptCoord.MirrorSeg(j, Seg), Seg));
            Assert.AreEqual(0, SculptCoord.MirrorSeg(0, Seg), "เซกเมนต์ 0 อยู่บนแกน +x — สะท้อนแล้วอยู่ที่เดิม");
            Assert.AreEqual(Seg / 2, SculptCoord.MirrorSeg(Seg / 2, Seg), "ครึ่งวง (แกน −x) ก็อยู่ที่เดิม");
            Assert.AreEqual(Seg - 1, SculptCoord.MirrorSeg(1, Seg));
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
