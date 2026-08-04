using NUnit.Framework;
using DiveMap.Core;

namespace DiveMap.Tests
{
    /// <summary>
    /// An underwater object must not be darker than the water it is in.
    ///
    /// This is the "the sharks on Posidon are black" report, and it is worth being precise about
    /// what a test can say. It cannot say a scene looks good. It CAN say that the least light a
    /// side face receives is a fixed fraction of the colour of the water directly behind it —
    /// which is the whole content of "not a silhouette", and is exactly what was false: the
    /// measured ambient on a side face at 55 m was (0.14, 0.20, 0.26) against water at
    /// (0.125, 0.386, 0.563), i.e. about a quarter of the background in the two channels the eye
    /// reads hue from.
    ///
    /// So these assert the RELATIONSHIP. Retune a gradient stop and they still pass; break the
    /// link between the lighting and the water and they fail.
    /// </summary>
    public class UnderwaterLightTests
    {
        private const float M = DepthLight.UnitsPerMetre;

        // Depths worth checking: past the ramp-in, a normal recreational dive, the depth the bug
        // was reported at, and deeper than the ramp goes.
        private static readonly float[] Depths = { 22f, 30f, 40f, 55.1f, 80f, 120f };

        [Test]
        public void AtAndAboveTheSurface_TheFloorIsSilent()
        {
            // The map view sits at or above the waterline and its ambient was tuned over four QC
            // rounds. A floor that reached up there would undo that work the first time it ran.
            foreach (float metres in new[] { -50f, -1f, 0f })
            {
                UnderwaterLight.AmbientFloor(metres * M,
                                             out SeabedGeom.Rgb sky,
                                             out SeabedGeom.Rgb eq,
                                             out SeabedGeom.Rgb gnd);
                Assert.AreEqual(0f, sky.R + sky.G + sky.B, 1e-6, $"sky floor at {metres} m");
                Assert.AreEqual(0f, eq.R + eq.G + eq.B, 1e-6, $"equator floor at {metres} m");
                Assert.AreEqual(0f, gnd.R + gnd.G + gnd.B, 1e-6, $"ground floor at {metres} m");
            }
        }

        [Test]
        public void StrengthRampsInOnceAndStaysIn()
        {
            Assert.AreEqual(0f, UnderwaterLight.Strength(0f), 1e-6);
            Assert.AreEqual(1f, UnderwaterLight.Strength(UnderwaterLight.FullStrengthMetres * M), 1e-5);
            Assert.AreEqual(1f, UnderwaterLight.Strength(UnderwaterLight.FullStrengthMetres * M * 6f), 1e-5);

            float prev = -1f;
            for (float metres = 0f; metres <= 40f; metres += 0.5f)
            {
                float s = UnderwaterLight.Strength(metres * M);
                Assert.GreaterOrEqual(s, prev, $"strength went backwards at {metres} m");
                Assert.LessOrEqual(s, 1f);
                prev = s;
            }
        }

        [Test]
        public void NaNDepth_IsTreatedAsTheSurface()
        {
            // Depth is a subtraction of two world positions; one un-initialised transform and this
            // would otherwise paint the whole scene with NaN.
            Assert.AreEqual(0f, UnderwaterLight.Strength(float.NaN), 1e-6);
        }

        [Test]
        public void ASideFaceIsNeverASilhouette()
        {
            // 🔴 The actual requirement. A mid-grey face lit by nothing but the floor still returns
            // at least MinLitFraction of the water behind it, in every channel.
            foreach (float metres in Depths)
            {
                UnderwaterLight.LitFraction(metres * M, UnderwaterLight.MidGreyAlbedo,
                                            out float r, out float g, out float b);
                Assert.GreaterOrEqual(r, UnderwaterLight.MinLitFraction, $"red at {metres} m");
                Assert.GreaterOrEqual(g, UnderwaterLight.MinLitFraction, $"green at {metres} m");
                Assert.GreaterOrEqual(b, UnderwaterLight.MinLitFraction, $"blue at {metres} m");
            }
        }

        [Test]
        public void TheLightRigItself_NoLongerNeedsTheFloorToAvoidSilhouettes()
        {
            // 🔴 THIS TEST CHANGED MEANING IN WO-E3, AND THE CHANGE IS THE POINT.
            //
            // It used to compare the floor against one measured number — the equator ambient at
            // 55.1 m on the reported screenshot, (0.135, 0.202, 0.264) — and demand the floor beat
            // it. That comparison is void now, and not because the floor got worse: the WATER got
            // darker. It used to be painted from a lifted six-stop ramp that ignored depth entirely
            // (0.125, 0.386, 0.563 at 55 m); it is now the web's own #123a55 dimmed by the same
            // curve as the light (0.018, 0.079, 0.176). Holding the old floor against the old water
            // would be testing a scene that no longer exists.
            //
            // What replaces it is stronger, because it does not involve the floor at all: with the
            // web's own hemisphere ambient (AppBoot's equator band) and the full depth curve on it,
            // a mid-grey side face is ALREADY at least as bright as the water behind it, at every
            // depth. The floor stops being the thing holding the picture up and goes back to being
            // what its name says — a floor, for whatever else writes the ambient.
            var authoredEquator = new SeabedGeom.Rgb(0.430f, 0.572f, 0.657f); // AppBoot, 0xbfe6ff/0x123040 hemisphere
            const float albedo = UnderwaterLight.MidGreyAlbedo;

            foreach (float metres in Depths)
            {
                float d = metres * M;
                SeabedGeom.Rgb k = WaterFog.Attenuation(d);
                SeabedGeom.Rgb water = WaterFog.ColorAt(d);

                // In light, the way DepthAtmosphere actually dims it (ToneMap.ScaleLight).
                float r = Lit(authoredEquator.R, k.R, albedo) / ToneMap.SrgbToLinear(water.R);
                float g = Lit(authoredEquator.G, k.G, albedo) / ToneMap.SrgbToLinear(water.G);
                float b = Lit(authoredEquator.B, k.B, albedo) / ToneMap.SrgbToLinear(water.B);

                Assert.GreaterOrEqual(r, UnderwaterLight.MinLitFraction, $"red at {metres} m");
                Assert.GreaterOrEqual(g, UnderwaterLight.MinLitFraction, $"green at {metres} m");
                Assert.GreaterOrEqual(b, UnderwaterLight.MinLitFraction, $"blue at {metres} m");
            }
        }

        /// <summary>Light off a surface of <paramref name="albedo"/> under an authored ambient
        /// channel that the depth curve has dimmed.</summary>
        private static float Lit(float ambientSrgb, float k, float albedo)
            => ToneMap.SrgbToLinear(ToneMap.ScaleLight(ambientSrgb, k)) * ToneMap.SrgbToLinear(albedo);

        [Test]
        public void RaisingStillWorks_WhenSomethingElseHasDimmedTheAmbient()
        {
            // The floor's remaining job: DroneLights and EnvMode both write the ambient, and either
            // of them can leave a channel under the water. Whatever they leave, this lifts.
            var dimmed = new SeabedGeom.Rgb(0.001f, 0.002f, 0.003f);
            UnderwaterLight.AmbientFloor(55.1f * M,
                                         out SeabedGeom.Rgb sky,
                                         out SeabedGeom.Rgb eq,
                                         out SeabedGeom.Rgb gnd);

            SeabedGeom.Rgb raised = UnderwaterLight.Raise(dimmed, eq);
            Assert.AreEqual(eq.R, raised.R, 1e-6);
            Assert.AreEqual(eq.G, raised.G, 1e-6);
            Assert.AreEqual(eq.B, raised.B, 1e-6);
        }

        [Test]
        public void ObjectsStillShadeFromTopToBottom()
        {
            // Never flat. If the three bands collapsed to one value every object would read as a
            // sticker — which is the other half of "looks wrong underwater", and the trap a naive
            // "just brighten everything" fix falls into.
            foreach (float metres in Depths)
            {
                UnderwaterLight.AmbientFloor(metres * M,
                                             out SeabedGeom.Rgb sky,
                                             out SeabedGeom.Rgb eq,
                                             out SeabedGeom.Rgb gnd);
                Assert.Greater(sky.G, eq.G, $"top and side match at {metres} m");
                Assert.Greater(eq.G, gnd.G, $"side and underside match at {metres} m");
                Assert.Greater(sky.B, eq.B);
                Assert.Greater(eq.B, gnd.B);
            }
        }

        [Test]
        public void TheUndersideKeepsMostOfTheLight_BecauseTheSeabedIsBrightSand()
        {
            // A shark's white belly reading as white is what makes it a shark rather than a
            // silhouette with a stripe. Undersides on these maps sit over a 340-unit sheet of lit
            // sand, so "below" is dimmer than "sideways" but not by much.
            Assert.Greater(UnderwaterLight.GroundOfSky, 0.5f);
            Assert.Less(UnderwaterLight.GroundOfSky, UnderwaterLight.EquatorOfSky);
        }

        [Test]
        public void TheFloorNeverOutshinesTheWater()
        {
            // A floor brighter than the medium would blow surfaces out and make objects glow, which
            // is the failure mode on the other side of this fix.
            foreach (float metres in Depths)
            {
                SeabedGeom.Rgb water = WaterFog.ColorAt(metres * M);
                UnderwaterLight.AmbientFloor(metres * M,
                                             out SeabedGeom.Rgb sky,
                                             out SeabedGeom.Rgb eq,
                                             out SeabedGeom.Rgb gnd);
                Assert.LessOrEqual(sky.R, water.R + 1e-6, $"red at {metres} m");
                Assert.LessOrEqual(sky.G, water.G + 1e-6, $"green at {metres} m");
                Assert.LessOrEqual(sky.B, water.B + 1e-6, $"blue at {metres} m");
                Assert.LessOrEqual(eq.G, sky.G + 1e-6);
                Assert.LessOrEqual(gnd.G, sky.G + 1e-6);
            }
        }

        [Test]
        public void TheFloorIsTheSameWaterTheBackdropIsPaintedWith()
        {
            // The structural link, and the reason this cannot drift: at full strength the sky floor
            // IS a colour off the backdrop's own gradient — the same ramp the fog reads. Edit a
            // stop and the lighting moves with it.
            foreach (float metres in new[] { 22f, 30f, 40f, 55.1f, 90f })
            {
                UnderwaterLight.AmbientFloor(metres * M,
                                             out SeabedGeom.Rgb sky,
                                             out SeabedGeom.Rgb eq,
                                             out SeabedGeom.Rgb gnd);
                // The ramp is sampled AT THIS DEPTH now (WO-E3): the backdrop is dimmed by the same
                // attenuation as everything else, so "a colour the water uses" is a question that
                // only means anything once you say how deep you are.
                Assert.Less(WaterFog.DistanceFromRamp(sky, metres * M), 0.012f,
                            $"the ambient floor at {metres} m is not a colour the water uses");
            }
        }

        [Test]
        public void RaiseOnlyEverRaises_AndSayingItTwiceChangesNothing()
        {
            var dim = new SeabedGeom.Rgb(0.05f, 0.08f, 0.12f);
            var floor = new SeabedGeom.Rgb(0.10f, 0.30f, 0.45f);

            SeabedGeom.Rgb once = UnderwaterLight.Raise(dim, floor);
            SeabedGeom.Rgb twice = UnderwaterLight.Raise(once, floor);
            Assert.AreEqual(once.R, twice.R, 1e-6);
            Assert.AreEqual(once.G, twice.G, 1e-6);
            Assert.AreEqual(once.B, twice.B, 1e-6);

            // Anything already brighter than the water is left exactly alone — this is what keeps
            // the headlamp swap, the daylight mode and the depth curve working.
            var bright = new SeabedGeom.Rgb(0.90f, 0.95f, 1.00f);
            SeabedGeom.Rgb kept = UnderwaterLight.Raise(bright, floor);
            Assert.AreEqual(bright.R, kept.R, 1e-6);
            Assert.AreEqual(bright.G, kept.G, 1e-6);
            Assert.AreEqual(bright.B, kept.B, 1e-6);
        }

        [Test]
        public void TheFloorKeepsTheWatersHue_NotJustItsBrightness()
        {
            // Scaling, never tinting: a grey floor would give every fish the same washed-out cast
            // and put it in a different colour family from the water behind it — which is the shape
            // of the original bug, just brighter.
            const float metres = 30f;
            SeabedGeom.Rgb water = WaterFog.ColorAt(metres * M);
            UnderwaterLight.AmbientFloor(metres * M,
                                         out SeabedGeom.Rgb sky,
                                         out SeabedGeom.Rgb eq,
                                         out SeabedGeom.Rgb gnd);

            Assert.AreEqual(water.G / water.R, sky.G / sky.R, 1e-4);
            Assert.AreEqual(water.B / water.R, sky.B / sky.R, 1e-4);
            Assert.AreEqual(water.B / water.G, eq.B / eq.G, 1e-4);
            Assert.AreEqual(water.B / water.G, gnd.B / gnd.G, 1e-4);
        }
    }
}
