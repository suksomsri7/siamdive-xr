using NUnit.Framework;
using DiveMap.Core;

namespace DiveMap.Tests
{
    /// <summary>
    /// The shallows must be brighter than the deep, and the deep must go blue rather than grey.
    /// Both are things a screenshot can argue about and a test cannot.
    /// </summary>
    public class DepthLightTests
    {
        private const float M = DepthLight.UnitsPerMetre;

        [Test]
        public void AtTheSurface_NothingIsAttenuated()
        {
            DepthLight.Attenuation(0f, out float r, out float g, out float b);
            Assert.AreEqual(1f, r, 1e-4);
            Assert.AreEqual(1f, g, 1e-4);
            Assert.AreEqual(1f, b, 1e-4);
        }

        [Test]
        public void AboveTheSurface_IsNotBrighterThanTheSurface()
        {
            // The map view sits above the water; a negative depth must not amplify anything.
            DepthLight.Attenuation(-50f, out float r, out float g, out float b);
            Assert.AreEqual(1f, r, 1e-4);
            Assert.AreEqual(1f, g, 1e-4);
            Assert.AreEqual(1f, b, 1e-4);
        }

        [Test]
        public void DeeperIsAlwaysDarker()
        {
            float prev = float.MaxValue;
            for (int metres = 0; metres <= 60; metres += 5)
            {
                float now = DepthLight.Brightness(metres * M);
                Assert.Less(now, prev, $"{metres} m must be darker than the depth above it");
                prev = now;
            }
        }

        [Test]
        public void RedDiesFirst_WhichIsWhyDeepWaterIsBlue()
        {
            DepthLight.Attenuation(20f * M, out float r, out float g, out float b);
            Assert.Less(r, g, "red is absorbed fastest");
            Assert.Less(g, b, "blue carries furthest");
        }

        [Test]
        public void TheDeepIsDim_ButNeverBlack()
        {
            float deep = DepthLight.Brightness(80f * M);
            Assert.Greater(deep, DepthLight.Floor * 0.99f, "a floor keeps the deep readable");
            Assert.Less(deep, 0.45f, "…and it is genuinely dark down there");
        }

        [Test]
        public void ShallowIsRecognisablyBrighterThanRecreationalDepth()
        {
            // The comparison the user actually made: 5 m vs 30 m must not look the same.
            float shallow = DepthLight.Brightness(5f * M);
            float deep = DepthLight.Brightness(30f * M);
            Assert.Greater(shallow - deep, 0.2f, "the difference has to be visible, not technical");
        }

        [Test]
        public void VisibilityShrinksWithDepth_MoreGentlyThanColour()
        {
            float vShallow = DepthLight.VisibilityScale(0f);
            float vDeep = DepthLight.VisibilityScale(40f * M);
            Assert.AreEqual(1f, vShallow, 1e-4);
            Assert.Less(vDeep, vShallow);
            Assert.Greater(vDeep, DepthLight.Brightness(40f * M),
                           "you can still see further than the colour loss alone suggests");
        }
    }
}
