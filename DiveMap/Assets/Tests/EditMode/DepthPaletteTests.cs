using DiveMap.Core;
using NUnit.Framework;
using UnityEngine;

namespace DiveMap.Tests
{
    /// <summary>
    /// P2 — the depth ramp. The seabed's heat-map texture and the legend bar are painted from
    /// this one function, so if it drifts the legend starts lying about the map.
    /// </summary>
    public class DepthPaletteTests
    {
        [Test]
        public void Ramp_HitsTheWebsThreeStops()
        {
            DepthPalette.Rgb shallow = DepthPalette.Color(0f);
            Assert.AreEqual(0.55f, shallow.R, 0.001f);
            Assert.AreEqual(0.92f, shallow.G, 0.001f);
            Assert.AreEqual(0.50f, shallow.B, 0.001f);

            DepthPalette.Rgb mid = DepthPalette.Color(0.5f);
            Assert.AreEqual(0.13f, mid.R, 0.001f);
            Assert.AreEqual(0.62f, mid.G, 0.001f);
            Assert.AreEqual(0.88f, mid.B, 0.001f);

            DepthPalette.Rgb deep = DepthPalette.Color(1f);
            Assert.AreEqual(0.06f, deep.R, 0.001f);
            Assert.AreEqual(0.09f, deep.G, 0.001f);
            Assert.AreEqual(0.42f, deep.B, 0.001f);
        }

        [Test]
        public void Ramp_ClampsOutsideZeroToOne()
        {
            Assert.AreEqual(0.55f, DepthPalette.Color(-3f).R, 0.001f);
            Assert.AreEqual(0.42f, DepthPalette.Color(9f).B, 0.001f);
        }

        [Test]
        public void Ramp_OnlyGetsDarkerWithDepth()
        {
            float prev = 9f;
            for (float t = 0f; t <= 1.0001f; t += 0.1f)
            {
                DepthPalette.Rgb c = DepthPalette.Color(t);
                float lum = c.R + c.G + c.B;
                Assert.LessOrEqual(lum, prev + 1e-4f, $"ramp brightened at t={t}");
                prev = lum;
            }
        }

        [Test]
        public void Metres_IsTheWebsSixUnitsPerMetre_ClampedZeroToHundred()
        {
            Assert.AreEqual(40f, DepthPalette.Metres(0f, 240f), 0.01f);
            Assert.AreEqual(0f, DepthPalette.Metres(240f, 240f), 0.01f);
            Assert.AreEqual(0f, DepthPalette.Metres(400f, 240f), 0.01f, "above the surface is not negative depth");
            Assert.AreEqual(100f, DepthPalette.Metres(-1000f, 240f), 0.01f);
        }

        [Test]
        public void ColorForHeight_AgreesWithTheTwoStepsItComposes()
        {
            const float water = 240f;
            const float y = -60f;   // 50 m down
            DepthPalette.Rgb direct = DepthPalette.ColorForHeight(y, water);
            DepthPalette.Rgb composed = DepthPalette.Color(DepthPalette.Metres(y, water) / DepthPalette.MaxMetres);
            Assert.AreEqual(composed.R, direct.R, 1e-5f);
            Assert.AreEqual(composed.G, direct.G, 1e-5f);
            Assert.AreEqual(composed.B, direct.B, 1e-5f);
        }

        [Test]
        public void ADeepPitReadsDifferentlyFromTheFlatAroundIt()
        {
            // The whole point of the view: a 10 m pit must not be the colour of its rim.
            DepthPalette.Rgb rim = DepthPalette.ColorForHeight(0f, 240f);
            DepthPalette.Rgb pit = DepthPalette.ColorForHeight(-60f, 240f);
            float diff = Mathf.Abs(rim.R - pit.R) + Mathf.Abs(rim.G - pit.G) + Mathf.Abs(rim.B - pit.B);
            Assert.Greater(diff, 0.2f);
        }
    }
}
