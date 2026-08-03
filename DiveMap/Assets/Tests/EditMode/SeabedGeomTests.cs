using System;
using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// WO-XR-04.2 — pins the seabed footprint, the sand gradient/haze and the backdrop
    /// gradient to builder.html's own numbers. If one of these drifts, the Unity map stops
    /// matching the web side-by-side, which is the project's acceptance bar.
    /// </summary>
    public class SeabedGeomTests
    {
        // ── Footprint (superellipse n=4) ──────────────────────────────────────────

        [Test]
        public void BoundaryDist_OnAxes_IsSandRadius()
        {
            Assert.AreEqual(340f, SeabedGeom.BoundaryDist(0f), 0.01f);
            Assert.AreEqual(340f, SeabedGeom.BoundaryDist((float)(Math.PI / 2)), 0.01f);
            Assert.AreEqual(340f, SeabedGeom.BoundaryDist((float)Math.PI), 0.01f);
            Assert.AreEqual(340f, SeabedGeom.BoundaryDist((float)(1.5 * Math.PI)), 0.01f);
        }

        [Test]
        public void BoundaryDist_InCorners_ReachesTheRoundedSquare()
        {
            // 340 × 2^(1/4) = 404.33 — the rounded-square corner, 19% further out than a circle.
            Assert.AreEqual(404.33f, SeabedGeom.BoundaryDist((float)(Math.PI / 4)), 0.1f);
            Assert.AreEqual(404.33f, SeabedGeom.BoundaryDist((float)(3 * Math.PI / 4)), 0.1f);
        }

        [Test]
        public void BoundaryFraction_IsOneOnTheBoundary_AndZeroAtTheCentre()
        {
            Assert.AreEqual(0f, SeabedGeom.BoundaryFraction(0f, 0f), 1e-4f);
            Assert.AreEqual(1f, SeabedGeom.BoundaryFraction(340f, 0f), 1e-3f);
            Assert.AreEqual(1f, SeabedGeom.BoundaryFraction(0f, -340f), 1e-3f);
            float corner = 340f / (float)Math.Pow(2.0, 0.25);   // on the boundary at 45°
            Assert.AreEqual(1f, SeabedGeom.BoundaryFraction(corner, corner), 1e-3f);
            Assert.Less(SeabedGeom.BoundaryFraction(100f, 100f), 1f);
        }

        // ── Sand colour ───────────────────────────────────────────────────────────

        [Test]
        public void VertexNoise_MatchesTheWebFormula()
        {
            // 0.82 + sin(0)·0.05 + cos(0)·0.04
            Assert.AreEqual(0.86f, SeabedGeom.VertexNoise(0), 1e-4f);
            for (int i = 0; i < 50; i++)
            {
                float n = SeabedGeom.VertexNoise(i);
                Assert.GreaterOrEqual(n, 0.73f);
                Assert.LessOrEqual(n, 0.91f);
            }
        }

        [Test]
        public void SandColor_AtTheCentre_IsTheWebsTopSand()
        {
            SeabedGeom.Rgb c = SeabedGeom.SandColor(1f, 0f, SeabedGeom.VertexNoise(0));
            Assert.AreEqual(0.7052f, c.R, 0.001f);
            Assert.AreEqual(0.6364f, c.G, 0.001f);
            Assert.AreEqual(0.4902f, c.B, 0.001f);
        }

        [Test]
        public void SandColor_AtTheRim_IsPureDeepWaterTint()
        {
            // Fully hazed at rad ≥ 1 whatever the speckle — this is the edge that has to melt
            // into the blue instead of ending in a cream cut-off.
            SeabedGeom.Rgb c = SeabedGeom.SandColor(1f, 1f, SeabedGeom.VertexNoise(7));
            Assert.AreEqual(0.05f, c.R, 1e-4f);
            Assert.AreEqual(0.20f, c.G, 1e-4f);
            Assert.AreEqual(0.33f, c.B, 1e-4f);
        }

        [Test]
        public void SandColor_HazeIsHalfwayAt0775()
        {
            // smoothstep((rad−0.55)/0.45) = 0.5 exactly at the midpoint of the fade band.
            const float noise = 0.86f;
            SeabedGeom.Rgb mid = SeabedGeom.SandColor(1f, 0.775f, noise);
            SeabedGeom.Rgb sand = SeabedGeom.SandColor(1f, 0f, noise);
            Assert.AreEqual((sand.R + SeabedGeom.WaterTint.R) * 0.5f, mid.R, 0.001f);
            Assert.AreEqual((sand.G + SeabedGeom.WaterTint.G) * 0.5f, mid.G, 0.001f);
            Assert.AreEqual((sand.B + SeabedGeom.WaterTint.B) * 0.5f, mid.B, 0.001f);
        }

        [Test]
        public void SandColor_InsideTheHazeBand_IsUntouchedSand()
        {
            SeabedGeom.Rgb a = SeabedGeom.SandColor(1f, 0f, 0.86f);
            SeabedGeom.Rgb b = SeabedGeom.SandColor(1f, 0.55f, 0.86f);
            Assert.AreEqual(a.R, b.R, 1e-5f);
            Assert.AreEqual(a.G, b.G, 1e-5f);
            Assert.AreEqual(a.B, b.B, 1e-5f);
        }

        [Test]
        public void SandColor_BottomOfTheSlabIsDarkerThanTheTop()
        {
            SeabedGeom.Rgb top = SeabedGeom.SandColor(1f, 0f, 0.86f);
            SeabedGeom.Rgb bottom = SeabedGeom.SandColor(0f, 0f, 0.86f);
            Assert.Less(bottom.R, top.R);
            Assert.Less(bottom.G, top.G);
            Assert.Less(bottom.B, top.B);
            Assert.AreEqual(0.55f * 0.86f, bottom.R, 1e-4f);
        }

        // ── Backdrop gradient ─────────────────────────────────────────────────────

        [Test]
        public void GradientStop_HitsEveryStopOfTheRamp()
        {
            // 🔴 The WEB's four stops (builder.html:665), restored by WO-E3.
            //
            // These had been replaced by six lighter ones after "the background is still dark" was
            // reported twice — a true observation of a GAMMA build with no tone mapping, where
            // #06243a is written to the screen as byte 6/36/58 and reads as a black band. Through
            // the pipeline the web actually uses (linear light → ACES at 1.05) the same hex lands
            // at 0/18/42, which is the picture the user is comparing this app against and calling
            // better. Lifting them was compensation for a missing tone mapper; the tone mapper is
            // here now, so the compensation goes.
            AssertRgb(0.8902f, 0.9490f, 0.9725f, SeabedGeom.GradientStop(0f));     // #e3f2f8 ผิวน้ำ
            AssertRgb(0.6627f, 0.8314f, 0.9098f, SeabedGeom.GradientStop(0.38f));  // #a9d4e8 ตื้น
            AssertRgb(0.2471f, 0.5765f, 0.7765f, SeabedGeom.GradientStop(0.52f));  // #3f93c6 ฟ้าใส
            AssertRgb(0.0235f, 0.1412f, 0.2275f, SeabedGeom.GradientStop(1f));     // #06243a ลึกสุด
        }

        [Test]
        public void GradientStop_ClampsOutsideZeroToOne()
        {
            AssertRgb(0.8902f, 0.9490f, 0.9725f, SeabedGeom.GradientStop(-2f));
            AssertRgb(0.0235f, 0.1412f, 0.2275f, SeabedGeom.GradientStop(9f));
        }

        [Test]
        public void GradientStop_InterpolatesBetweenStops_AndOnlyDarkensDownwards()
        {
            SeabedGeom.Rgb mid = SeabedGeom.GradientStop(0.19f); // halfway 0 → 0.38
            Assert.AreEqual((0.8902f + 0.6627f) * 0.5f, mid.R, 0.002f);
            Assert.AreEqual((0.9490f + 0.8314f) * 0.5f, mid.G, 0.002f);
            Assert.AreEqual((0.9725f + 0.9098f) * 0.5f, mid.B, 0.002f);

            float prev = float.MaxValue;   // the top stop is the brightest (lum 2.812)
            for (float v = 0f; v <= 1.0001f; v += 0.05f)
            {
                SeabedGeom.Rgb c = SeabedGeom.GradientStop(v);
                float lum = c.R + c.G + c.B;
                Assert.LessOrEqual(lum, prev + 1e-4f, $"backdrop brightened going down at v={v}");
                prev = lum;
            }
        }

        private static void AssertRgb(float r, float g, float b, SeabedGeom.Rgb c)
        {
            Assert.AreEqual(r, c.R, 0.002f);
            Assert.AreEqual(g, c.G, 0.002f);
            Assert.AreEqual(b, c.B, 0.002f);
        }
    }
}
