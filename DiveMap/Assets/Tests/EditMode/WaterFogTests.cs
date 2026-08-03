using NUnit.Framework;
using DiveMap.Core;

namespace DiveMap.Tests
{
    /// <summary>
    /// The fog, the backdrop and the light on the subject must be the same water.
    ///
    /// 🔴 WHY THESE CHANGED MEANING IN WO-E3. The previous version of this file asserted a real
    /// property — "the fog is a colour the background actually uses" — and the app still had the
    /// bug, because that property is not enough. Fog and background can agree with each other
    /// perfectly while BOTH of them ignore the depth curve that is dimming everything standing in
    /// front of them, and that is exactly what was happening: at 52 m the subject was lit at a
    /// third of surface strength and the water was painted at full. The user's words were
    /// "ฉลามกลายเป็นเงาแบน … ขณะที่ฉากหลัง/หมอกยังสว่าง", which is a statement about a RATIO, and
    /// the old tests could not see a ratio.
    ///
    /// So the tests here now assert the thing that actually had to become true: the water and the
    /// light are the same multiplication, therefore the subject-to-background ratio does not depend
    /// on depth. Four of the old tests (RampV / ShallowV / DeepV / DeepMetres — the ramp-walk) are
    /// gone with the mechanism they described; everything they were protecting is protected here by
    /// a stronger statement.
    ///
    /// A test still cannot tell you a scene looks right. It CAN tell you that no depth exists at
    /// which the water and the light have come apart — which is the whole content of the fix.
    /// </summary>
    public class WaterFogTests
    {
        private const float M = DepthLight.UnitsPerMetre;
        private static readonly float[] Depths = { 0f, 3f, 10f, 15f, 22f, 30f, 40f, 52f, 60f, 90f };

        [Test]
        public void TheWebsFogColourIsAPointOnTheWebsOwnBackdropRamp()
        {
            // The reason the whole scheme is possible, and it is the web's doing, not ours:
            // THREE.Fog(0x123a55) sits on the gradient builder.html paints behind it, just under
            // the horizon. Move a gradient stop and this fails, which is the point of pinning it.
            SeabedGeom.Rgb atRamp = SeabedGeom.GradientStop(WaterFog.FogRampV);
            Assert.AreEqual(WaterFog.BaseFog.R, atRamp.R, 0.012f);
            Assert.AreEqual(WaterFog.BaseFog.G, atRamp.G, 0.012f);
            Assert.AreEqual(WaterFog.BaseFog.B, atRamp.B, 0.012f);

            // …and just under the horizon rather than at either end of the frame: v=0 is the
            // surface haze at the top and v=1 the very bottom, and distant geometry meets the
            // background nearer the middle-low part of the picture.
            Assert.Greater(WaterFog.FogRampV, 0.5f);
            Assert.Less(WaterFog.FogRampV, 1f);
        }

        [Test]
        public void EveryDepth_ProducesAColourTheBackdropActuallyUses()
        {
            foreach (float metres in Depths)
            {
                SeabedGeom.Rgb c = WaterFog.ColorAt(metres * M);
                Assert.Less(WaterFog.DistanceFromRamp(c, metres * M), 0.012f,
                            $"fog at {metres} m is not a colour on the backdrop ramp");
            }
        }

        [Test]
        public void TheFogAndTheBackdropAreDimmedByTheSameVector()
        {
            // THE fix, stated as arithmetic. Whatever the attenuation does to the fog it does,
            // channel for channel, to the background behind it — so the two can never drift apart
            // however the curve is retuned.
            foreach (float metres in Depths)
            {
                float d = metres * M;
                SeabedGeom.Rgb k = WaterFog.Attenuation(d);
                SeabedGeom.Rgb fog = WaterFog.ColorAt(d);
                SeabedGeom.Rgb back = WaterFog.BackdropAt(0.5f, d);

                // In LIGHT, not in the numbers: the attenuation is a transmittance, so the authored
                // sRGB is decoded, scaled and re-encoded (ToneMap.ScaleLight). A plain multiply
                // here would be about three times too dark at the bottom of the curve.
                Assert.AreEqual(ToneMap.ScaleLight(WaterFog.BaseFog.R, k.R), fog.R, 1e-6, $"fog red at {metres} m");
                Assert.AreEqual(ToneMap.ScaleLight(WaterFog.BaseFog.G, k.G), fog.G, 1e-6);
                Assert.AreEqual(ToneMap.ScaleLight(WaterFog.BaseFog.B, k.B), fog.B, 1e-6);

                SeabedGeom.Rgb surface = SeabedGeom.GradientStop(0.5f);
                Assert.AreEqual(ToneMap.ScaleLight(surface.R, k.R), back.R, 1e-6, $"backdrop red at {metres} m");
                Assert.AreEqual(ToneMap.ScaleLight(surface.G, k.G), back.G, 1e-6);
                Assert.AreEqual(ToneMap.ScaleLight(surface.B, k.B), back.B, 1e-6);

                // …and the light that comes back out of the encode is exactly the light that went
                // in, which is the property everything else rests on.
                Assert.AreEqual(ToneMap.SrgbToLinear(WaterFog.BaseFog.G) * k.G,
                                ToneMap.SrgbToLinear(fog.G), 1e-6, $"fog green light at {metres} m");
            }
        }

        [Test]
        public void TheSubjectToBackgroundRatioDoesNotDependOnDepth()
        {
            // 🔑 The acceptance criterion, in Core, where it can be a proof rather than a
            // screenshot. A subject lit by ambient scaled by the same attenuation (DepthAtmosphere)
            // returns the same fraction of the water behind it at 3 m as at 90 m. That is what
            // stops a shark becoming a silhouette when a diver descends, and it holds by
            // construction — not because somebody balanced two curves.
            var ambientAtSurface = new SeabedGeom.Rgb(0.430f, 0.572f, 0.657f); // AppBoot equator band
            const float albedo = 0.45f;

            double refR = 0, refG = 0, refB = 0;
            bool first = true;
            foreach (float metres in Depths)
            {
                float d = metres * M;
                SeabedGeom.Rgb k = WaterFog.Attenuation(d);
                SeabedGeom.Rgb water = WaterFog.ColorAt(d);

                // Everything in LIGHT, because that is what a renderer adds up and what an eye
                // compares. DepthAtmosphere dims the ambient with the same ToneMap.ScaleLight the
                // water goes through, so the two attenuations cancel exactly.
                double r = Lit(ambientAtSurface.R, k.R, albedo) / ToneMap.SrgbToLinear(water.R);
                double g = Lit(ambientAtSurface.G, k.G, albedo) / ToneMap.SrgbToLinear(water.G);
                double b = Lit(ambientAtSurface.B, k.B, albedo) / ToneMap.SrgbToLinear(water.B);

                if (first) { refR = r; refG = g; refB = b; first = false; continue; }
                Assert.AreEqual(refR, r, 1e-3, $"red ratio moved at {metres} m");
                Assert.AreEqual(refG, g, 1e-3, $"green ratio moved at {metres} m");
                Assert.AreEqual(refB, b, 1e-3, $"blue ratio moved at {metres} m");
            }
        }

        /// <summary>Light coming off a surface of <paramref name="albedo"/> lit by an authored
        /// ambient channel that DepthAtmosphere has dimmed for this depth.</summary>
        private static double Lit(float ambientSrgb, float k, float albedo)
            => ToneMap.SrgbToLinear(ToneMap.ScaleLight(ambientSrgb, k)) * ToneMap.SrgbToLinear(albedo);

        [Test]
        public void TheOldCompensatedRampWouldHavePutTheFogOffTheBackground()
        {
            // The regression guard, stated as the thing that was wrong. #123a55 against the lifted
            // six-stop ramp (#eaf7fb → #1b5a85) was nowhere near any point of it, which is why
            // distant geometry silhouetted and why the fog got abandoned in the first place.
            var liftedDeepestStop = new SeabedGeom.Rgb(0.106f, 0.353f, 0.522f); // #1b5a85
            float gap = System.Math.Max(
                System.Math.Abs(liftedDeepestStop.R - WaterFog.BaseFog.R),
                System.Math.Max(System.Math.Abs(liftedDeepestStop.G - WaterFog.BaseFog.G),
                                System.Math.Abs(liftedDeepestStop.B - WaterFog.BaseFog.B)));
            Assert.Greater(gap, 0.1f, "the ramp that caused the silhouette is no longer a warning");
        }

        [Test]
        public void ShallowWaterIsBrighterThanDeep()
        {
            SeabedGeom.Rgb shallow = WaterFog.ColorAt(0f);
            SeabedGeom.Rgb deep = WaterFog.ColorAt(40f * M);
            Assert.Greater(shallow.R + shallow.G + shallow.B, deep.R + deep.G + deep.B);
        }

        [Test]
        public void GoingDeeperKeepsTakingLightAway_TheCueTheUserAskedToKeep()
        {
            // 🔑 "หรี่แสงตามความลึก = เก็บไว้". The cue has two halves and this is the one that is
            // monotone all the way down: the water gets darker with every metre, without exception.
            float prev = float.MaxValue;
            foreach (float metres in Depths)
            {
                SeabedGeom.Rgb c = WaterFog.ColorAt(metres * M);
                float lum = c.R + c.G + c.B;
                Assert.Less(lum, prev, $"the water stopped dimming at {metres} m");
                prev = lum;
            }
        }

        [Test]
        public void RedIsAlwaysTheFirstToGo_AndBlueTheLast()
        {
            // The colour half of the cue, stated as the ordering rather than as a ratio. Below the
            // surface, at EVERY depth, red keeps less of its surface value than green and green
            // less than blue — which is why deep water is blue and why a torch turns a grey wreck
            // brown again.
            foreach (float metres in Depths)
            {
                if (metres <= 0f) continue;
                SeabedGeom.Rgb k = WaterFog.Attenuation(metres * M);
                Assert.Less(k.R, k.G, $"red outlasted green at {metres} m");
                Assert.Less(k.G, k.B, $"green outlasted blue at {metres} m");
            }
        }

        [Test]
        public void TheFloorEventuallyPullsTheHueBackTowardNeutral_AndThatIsKnown()
        {
            // 🔴 A MEASURED PROPERTY OF THE CURRENT MODEL, WRITTEN DOWN SO IT IS NOT REDISCOVERED
            // AS A BUG. DepthLight applies its floor PER CHANNEL, so once red has bottomed out at
            // Floor and blue is still falling toward it, the blue-to-red RATIO stops climbing and
            // starts easing back: with Floor = 0.25 it peaks near 20 m and is lower at 52 m than at
            // 15 m. Physically the hue should keep separating forever; a floor that lifts red as
            // hard as it lifts blue is what stops it.
            //
            // It is left alone here because the floor is a readability device the user's own
            // decision depends on, and because the CUE THE EYE SEES DOES NOT INVERT: the water is
            // still monotonically dimmer, red is still always the weakest channel, and by the time
            // the frame has been through ACES the displayed blue-to-red ratio of the water rises
            // from ~2 at the surface to ~29 at 52 m (the red byte is crushed to 0). Changing the
            // floor's shape is a separate decision with its own look consequences — it makes deep
            // water pure blue-green with no red at all — and it is not this work order's to take.
            float shallow = Ratio(15f), mid = Ratio(20f), deep = Ratio(52f);
            Assert.Greater(mid, shallow, "the hue ratio no longer peaks around 20 m");
            Assert.Less(deep, mid, "the floor no longer relaxes the hue — re-read this test");
            // …and it never inverts to "the deep is redder than the surface".
            Assert.Greater(deep, Ratio(0f) * 1.5f);
        }

        private static float Ratio(float metres)
        {
            SeabedGeom.Rgb c = WaterFog.ColorAt(metres * M);
            return c.B / c.R;
        }

        [Test]
        public void AboveTheSurface_NothingIsAttenuated()
        {
            // The map view sits above the water and must not read as "deeper than the deep".
            SeabedGeom.Rgb k = WaterFog.Attenuation(-500f);
            Assert.AreEqual(1f, k.R, 1e-6);
            Assert.AreEqual(1f, k.G, 1e-6);
            Assert.AreEqual(1f, k.B, 1e-6);

            SeabedGeom.Rgb fog = WaterFog.ColorAt(-500f);
            Assert.AreEqual(WaterFog.BaseFog.R, fog.R, 1e-6);
            Assert.AreEqual(WaterFog.BaseFog.G, fog.G, 1e-6);
            Assert.AreEqual(WaterFog.BaseFog.B, fog.B, 1e-6);
        }

        [Test]
        public void TheDeepNeverGoesBlack()
        {
            // The floor's job, unchanged by WO-E3 lowering it: a dive site nobody can see is not
            // realism. At any depth every channel keeps at least the floor's share of the surface.
            SeabedGeom.Rgb k = WaterFog.Attenuation(1000f * M);
            Assert.GreaterOrEqual(k.R, DepthLight.Floor - 1e-6);
            Assert.GreaterOrEqual(k.G, DepthLight.Floor - 1e-6);
            Assert.GreaterOrEqual(k.B, DepthLight.Floor - 1e-6);
        }

        [Test]
        public void TheHeadlampMoodSurvives_ButCannotDragTheFogOffTheWater()
        {
            // Both halves matter. Losing the mood entirely would make the headlamp switch invisible
            // (that swap is the feature); letting it win is how the fog got dark in the first place.
            float d = 20f * M;
            SeabedGeom.Rgb water = WaterFog.ColorAt(d);
            var lampOff = new SeabedGeom.Rgb(DiveLightMath.HeadlightOff.FogR,
                                             DiveLightMath.HeadlightOff.FogG,
                                             DiveLightMath.HeadlightOff.FogB);
            var lampOn = new SeabedGeom.Rgb(DiveLightMath.HeadlightOn.FogR,
                                            DiveLightMath.HeadlightOn.FogG,
                                            DiveLightMath.HeadlightOn.FogB);

            SeabedGeom.Rgb off = WaterFog.Blend(water, lampOff, WaterFog.MoodWeight);
            SeabedGeom.Rgb on = WaterFog.Blend(water, lampOn, WaterFog.MoodWeight);

            Assert.AreNotEqual(off.B, on.B, "turning the lamps on no longer changes the water");
            Assert.Less(WaterFog.DistanceFromRamp(off, d), 0.09f);
            Assert.Less(WaterFog.DistanceFromRamp(on, d), 0.09f);
        }

        [Test]
        public void OneMetreIsOneMetreEverywhereInTheProject()
        {
            // WO-E3 item 6. Two different metres in one project is what once made every fin beat √2
            // fast (SwimStyle's 12 against everyone else's 6), and this file converts between world
            // units and metres in every single method. SwimStyleTests already pins its own constant
            // to DepthLight; this pins the third one, so the set is closed.
            Assert.AreEqual(6.0, ItemPicker.UnitsPerMetre, 1e-9);
            Assert.AreEqual((double)DepthLight.UnitsPerMetre, ItemPicker.UnitsPerMetre, 1e-9);
        }

        [Test]
        public void BlendWithNoWeight_IsExactlyTheWater()
        {
            SeabedGeom.Rgb water = WaterFog.ColorAt(10f * M);
            SeabedGeom.Rgb r = WaterFog.Blend(water, new SeabedGeom.Rgb(0f, 0f, 0f), 0f);
            Assert.AreEqual(water.R, r.R, 1e-6);
            Assert.AreEqual(water.G, r.G, 1e-6);
            Assert.AreEqual(water.B, r.B, 1e-6);
        }
    }
}
