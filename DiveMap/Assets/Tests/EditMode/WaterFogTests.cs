using NUnit.Framework;
using DiveMap.Core;

namespace DiveMap.Tests
{
    /// <summary>
    /// The fog and the backdrop must be the same water.
    ///
    /// This is the bug the user reported as "the wreck and the fish are black silhouettes", and it
    /// is worth stating what a test can and cannot say about it. A test cannot tell you a scene
    /// looks right. It CAN tell you that the colour distant geometry fades into is a colour the
    /// background actually uses — which is the whole content of "matching", and is exactly what was
    /// false before: the fog was #123a55 while the ramp behind it never went darker than #1b5a85.
    ///
    /// So these assert the RELATIONSHIP, not the numbers. Retune a gradient stop and they still
    /// pass; break the link between the two and they fail.
    /// </summary>
    public class WaterFogTests
    {
        private const float M = DepthLight.UnitsPerMetre;

        [Test]
        public void EveryDepth_ProducesAColourTheBackdropActuallyUses()
        {
            for (float metres = 0f; metres <= 60f; metres += 2.5f)
            {
                SeabedGeom.Rgb c = WaterFog.ColorAt(metres * M);
                Assert.Less(WaterFog.DistanceFromRamp(c), 2e-3f,
                            $"fog at {metres} m is not a colour on the backdrop ramp");
            }
        }

        [Test]
        public void TheOldWebFogColour_WasNotOnTheRamp()
        {
            // The regression guard, stated as the thing that was wrong. #123a55 — what AppBoot used
            // to set — is nowhere near any point of the gradient, which is why it silhouetted.
            var old = new SeabedGeom.Rgb(0.071f, 0.227f, 0.333f);
            Assert.Greater(WaterFog.DistanceFromRamp(old), 0.1f);
        }

        [Test]
        public void ShallowWaterIsBrighterThanDeep()
        {
            SeabedGeom.Rgb shallow = WaterFog.ColorAt(0f);
            SeabedGeom.Rgb deep = WaterFog.ColorAt(40f * M);
            Assert.Greater(shallow.R + shallow.G + shallow.B, deep.R + deep.G + deep.B);
        }

        [Test]
        public void AboveTheSurface_ReadsAsTheShallowestWater_NotTheDeepest()
        {
            // The map view sits above the water. Feeding a negative depth into a naive
            // depth/DeepMetres would sample the wrong end of the ramp and darken the whole scene
            // the moment the camera rose out of the sea.
            Assert.AreEqual(WaterFog.ShallowV, WaterFog.RampV(-500f), 1e-6);
            Assert.AreEqual(WaterFog.ShallowV, WaterFog.RampV(0f), 1e-6);
        }

        [Test]
        public void BelowTheDeepestStop_StopsDescending()
        {
            Assert.AreEqual(WaterFog.DeepV, WaterFog.RampV(WaterFog.DeepMetres * M), 1e-5);
            Assert.AreEqual(WaterFog.DeepV, WaterFog.RampV(WaterFog.DeepMetres * M * 4f), 1e-5);
        }

        [Test]
        public void DepthMovesTheSampleMonotonicallyDownTheRamp()
        {
            float prev = -1f;
            for (float metres = 0f; metres <= WaterFog.DeepMetres; metres += 1f)
            {
                float v = WaterFog.RampV(metres * M);
                Assert.GreaterOrEqual(v, prev);
                prev = v;
            }
        }

        [Test]
        public void TheHeadlampMoodSurvives_ButCannotDragTheFogOffTheRamp()
        {
            // Both halves matter. Losing the mood entirely would make the headlamp switch invisible
            // (that swap is the feature); letting it win is how the fog got dark in the first place.
            SeabedGeom.Rgb ramp = WaterFog.ColorAt(20f * M);
            var lampOff = new SeabedGeom.Rgb(DiveLightMath.HeadlightOff.FogR,
                                             DiveLightMath.HeadlightOff.FogG,
                                             DiveLightMath.HeadlightOff.FogB);
            var lampOn = new SeabedGeom.Rgb(DiveLightMath.HeadlightOn.FogR,
                                            DiveLightMath.HeadlightOn.FogG,
                                            DiveLightMath.HeadlightOn.FogB);

            SeabedGeom.Rgb off = WaterFog.Blend(ramp, lampOff, WaterFog.MoodWeight);
            SeabedGeom.Rgb on = WaterFog.Blend(ramp, lampOn, WaterFog.MoodWeight);

            Assert.AreNotEqual(off.B, on.B, "turning the lamps on no longer changes the water");
            Assert.Less(WaterFog.DistanceFromRamp(off), 0.06f);
            Assert.Less(WaterFog.DistanceFromRamp(on), 0.06f);
        }

        [Test]
        public void BlendWithNoWeight_IsExactlyTheRamp()
        {
            SeabedGeom.Rgb ramp = WaterFog.ColorAt(10f * M);
            SeabedGeom.Rgb r = WaterFog.Blend(ramp, new SeabedGeom.Rgb(0f, 0f, 0f), 0f);
            Assert.AreEqual(ramp.R, r.R, 1e-6);
            Assert.AreEqual(ramp.G, r.G, 1e-6);
            Assert.AreEqual(ramp.B, r.B, 1e-6);
        }

        [Test]
        public void TheSampleSitsAtTheHorizon_NotAtEitherEndOfTheScreen()
        {
            // v=0 is the surface haze at the top of the frame and v=1 the very bottom. Distant
            // geometry meets the backdrop near the middle, so sampling an extreme would mean the
            // fog matched a part of the picture nothing is drawn against.
            Assert.Greater(WaterFog.ShallowV, 0.2f);
            Assert.Less(WaterFog.ShallowV, WaterFog.DeepV);
            Assert.LessOrEqual(WaterFog.DeepV, 1f);
        }
    }
}
