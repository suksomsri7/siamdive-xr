using NUnit.Framework;
using DiveMap.Core;

namespace DiveMap.Tests
{
    /// <summary>
    /// The self-check that stands between "the harness saved a PNG" and "the PNG is evidence".
    ///
    /// 🔴 The test that matters most here is the NEGATIVE one. An instrument that always says yes
    /// is worse than no instrument, because it launders a wrong picture into a passing build —
    /// which is exactly what happened to <c>qc_ui_gizmo_axes.png</c>. So every positive case below
    /// is paired with the frame that must FAIL it.
    /// </summary>
    public class QcShotProofTests
    {
        private const int W = 120, H = 80;
        private const double Ox = 60, Oy = 40;
        // X to the right (40 px), Y up (30 px), Z up-left (40 px) — the three lengths a
        // three-quarter view produces, all comfortably over MinAxisPixels.
        private const double XtX = 100, XtY = 40;
        private const double YtX = 60, YtY = 70;
        private const double ZtX = 28, ZtY = 16;

        /// <summary>A frame with something in it that is NOT the gizmo — a water gradient.</summary>
        private static byte[] Water()
        {
            var b = new byte[W * H * 3];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int p = (y * W + x) * 3;
                    b[p] = (byte)(70 + y / 4);        // the tour HUD's open water: dim, blue-ish
                    b[p + 1] = (byte)(125 + y / 4);
                    b[p + 2] = (byte)(160 + y / 4);
                }
            return b;
        }

        private static byte[] Copy(byte[] src) => (byte[])src.Clone();

        /// <summary>Draw one arrow from the origin, 3 px wide, in its axis colour.</summary>
        private static void Draw(byte[] buf, double tx, double ty, byte r, byte g, byte bl)
        {
            double dx = tx - Ox, dy = ty - Oy;
            int steps = 400;
            for (int s = 0; s <= steps; s++)
            {
                double t = s / (double)steps;
                int px = (int)System.Math.Round(Ox + dx * t);
                int py = (int)System.Math.Round(Oy + dy * t);
                for (int oy = -1; oy <= 1; oy++)
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        int x = px + ox, y = py + oy;
                        if (x < 0 || x >= W || y < 0 || y >= H) continue;
                        int p = (y * W + x) * 3;
                        buf[p] = r; buf[p + 1] = g; buf[p + 2] = bl;
                    }
            }
        }

        private static byte[] WithAllThree()
        {
            byte[] on = Copy(Water());
            Draw(on, XtX, XtY, 255, 64, 71);     // GizmoHandles.XCol
            Draw(on, YtX, YtY, 89, 235, 92);     // YCol
            Draw(on, ZtX, ZtY, 82, 140, 255);    // ZCol
            return on;
        }

        private static QcShotProof.Axes Measure(byte[] on, byte[] off,
                                                double ox = Ox, double oy = Oy,
                                                double ytx = YtX, double yty = YtY)
            => QcShotProof.Arrows(on, off, W, H, ox, oy, XtX, XtY, ytx, yty, ZtX, ZtY);

        [Test]
        public void ThreeArrowsOnScreen_AreFound()
        {
            byte[] off = Water();
            QcShotProof.Axes a = Measure(WithAllThree(), off);

            Assert.AreEqual(3, QcShotProof.Measurable(a));
            Assert.GreaterOrEqual(a.X, QcShotProof.MinHitsPerAxis);
            Assert.GreaterOrEqual(a.Y, QcShotProof.MinHitsPerAxis);
            Assert.GreaterOrEqual(a.Z, QcShotProof.MinHitsPerAxis);
            Assert.IsTrue(QcShotProof.Passes(a));
            Assert.AreEqual("", QcShotProof.Reason(a));
            StringAssert.Contains("proved", QcShotProof.Line("gizmo_axes", a, "mode=View"));
        }

        [Test]
        public void TheShotThatSTARTEDThis_Fails()
        {
            // 🔴 The b386 frame, reduced to its essence: a screen with no gizmo on it. Hiding
            // handles that were never drawn changes nothing, so the two renders are identical —
            // and identical renders are how "I photographed the wrong screen" looks from inside
            // the harness. A beautiful picture of open water must score zero and say so.
            byte[] water = Water();
            QcShotProof.Axes a = Measure(Copy(water), water);

            Assert.AreEqual(0, a.Changed);
            Assert.IsFalse(QcShotProof.Passes(a));
            Assert.AreEqual("no-gizmo-in-frame", QcShotProof.Reason(a));
            StringAssert.Contains("INVALID SHOT", QcShotProof.Line("gizmo_axes", a, "mode=Tour"));
        }

        [Test]
        public void AnArrowThatPointsAtTheLens_IsExcusedButSaidOutLoud()
        {
            // The straight-down QC pose foreshortens Y to nothing. Two axes are still proof; the
            // log carries "y=n/a" so nobody later reads three ticks where there were two.
            byte[] on = Copy(Water());
            Draw(on, XtX, XtY, 255, 64, 71);
            Draw(on, ZtX, ZtY, 82, 140, 255);
            // Y tip 4 px from the origin — under MinAxisPixels.
            QcShotProof.Axes a = Measure(on, Water(), ytx: Ox, yty: Oy + 4);

            Assert.AreEqual(-1, a.Y);
            Assert.AreEqual(2, QcShotProof.Measurable(a));
            Assert.IsTrue(QcShotProof.Passes(a));
            StringAssert.Contains("y=n/a", QcShotProof.Line("gizmo_axes", a, null));
        }

        [Test]
        public void OneArrowAloneIsNotThreeArrows()
        {
            // The half-drawn case: the X handle exists, the other two are on screen and absent.
            // A gizmo that lost two of its axes is a real regression and must not pass because
            // SOMETHING changed in the frame.
            byte[] on = Copy(Water());
            Draw(on, XtX, XtY, 255, 64, 71);
            QcShotProof.Axes a = Measure(on, Water());

            Assert.Greater(a.Changed, 0);
            Assert.IsFalse(QcShotProof.Passes(a));
            Assert.AreEqual("axis-not-drawn:YZ", QcShotProof.Reason(a));
        }

        [Test]
        public void AWholeFrameDifferenceWOULDPass_soBothRendersMustShareOneFrame()
        {
            // 🔴 The false-positive guard. The two frames differ EVERYWHERE — the sim moved between
            // renders, or the readback is garbage — and the axes are not drawn. Sampling within
            // 4 px of the axis lines then finds "changes" all along them, which is why the harness
            // renders both frames inside one Unity frame. This pins what the arithmetic alone can
            // and cannot promise: it would pass, so the Unity half must never hand it two frames
            // from different moments.
            byte[] off = Water();
            byte[] on = Copy(off);
            for (int i = 0; i < on.Length; i++) on[i] = (byte)(on[i] ^ 0x40);
            QcShotProof.Axes a = Measure(on, off);

            Assert.IsTrue(QcShotProof.Passes(a),
                "documenting the limit: a whole-frame difference is indistinguishable from arrows");
            Assert.AreEqual(W * H, a.Changed, "…and it is visible as changed == the whole frame");
        }

        [Test]
        public void AHandleBehindTheCamera_IsNotEvidence()
        {
            byte[] on = WithAllThree();
            QcShotProof.Axes a = QcShotProof.Arrows(on, Water(), W, H,
                                                    double.NaN, double.NaN,
                                                    XtX, XtY, YtX, YtY, ZtX, ZtY);
            Assert.AreEqual(0, QcShotProof.Measurable(a));
            Assert.IsFalse(QcShotProof.Passes(a));
            Assert.AreEqual("arrows-off-screen", QcShotProof.Reason(a));
        }

        [Test]
        public void AReadbackThatWentWrong_IsNeverAPass()
        {
            // Buffers of different sizes, a null twin, a zero-length frame: every one of these is
            // "the instrument failed", and none of them may look like a clean shot.
            foreach (QcShotProof.Axes a in new[]
            {
                QcShotProof.Arrows(WithAllThree(), new byte[9], W, H, Ox, Oy, XtX, XtY, YtX, YtY, ZtX, ZtY),
                QcShotProof.Arrows(WithAllThree(), null, W, H, Ox, Oy, XtX, XtY, YtX, YtY, ZtX, ZtY),
                QcShotProof.Arrows(null, null, W, H, Ox, Oy, XtX, XtY, YtX, YtY, ZtX, ZtY),
                QcShotProof.Arrows(WithAllThree(), Water(), W + 1, H, Ox, Oy, XtX, XtY, YtX, YtY, ZtX, ZtY),
            })
            {
                Assert.IsFalse(QcShotProof.Passes(a));
                Assert.AreEqual("readback-empty", QcShotProof.Reason(a));
                StringAssert.Contains("INVALID SHOT", QcShotProof.Line("gizmo", a, null));
            }
        }

        [Test]
        public void TheFailedLine_SaysINVALIDSHOTTooWhenNoFrameWasEverTaken()
        {
            string line = QcShotProof.FailedLine("gizmo_axes", "handles-hidden", "mode=Tour selected=null");
            StringAssert.Contains("INVALID SHOT", line);
            StringAssert.Contains("reason=handles-hidden", line);
            StringAssert.Contains("mode=Tour", line);
        }
    }
}
