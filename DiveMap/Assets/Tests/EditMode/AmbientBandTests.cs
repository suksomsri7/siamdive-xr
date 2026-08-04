using NUnit.Framework;
using DiveMap.Core;

namespace DiveMap.Tests
{
    /// <summary>
    /// WO-E4 — the ambient ground band, and the torch that is supposed to give the colour back.
    ///
    /// 🔴 Two reports, one file, because they are the two halves of the same sentence. The user
    /// said: "ถ้าแสงสีแดงหาย ทำให้พื้นดำ แต่ในโลกจริงคือเมื่อโดนแสงไฟฉาย สีเดิมจะกลับมา" — and both
    /// halves of that were broken in different places.
    ///
    ///   • The floor half. <see cref="ToneMap.Fit"/> has a hard zero at a scene-linear
    ///     <see cref="ToneMap.BlackFloor"/> = 0.00186, and the ambient ground band's red had been
    ///     dimmed to 0.87 of it — so every down-facing surface in the app rendered red as byte 0
    ///     by construction, and the darkest base colour that could come off an underside at all
    ///     was sRGB 72 against an atlas that is 47.9% darker than 71. That is a statue rendering
    ///     black, and no model change could have reached it.
    ///
    ///   • The torch half. Beer–Lambert absorbs along the PATH the light travels, so the ambient
    ///     (surface → object, tens of metres) loses its red and a lamp (lamp → object → eye, a few
    ///     metres) does not. The maths was already right — nothing here multiplies a Light by the
    ///     depth curve — but on the device the lamps were being demoted to vertex lights by a
    ///     pixelLightCount of 1, so the beam never reached the pixels to prove it.
    ///
    /// Everything below is pure logic and runs on this machine in two seconds. That is deliberate:
    /// the last four rounds of this bug each cost a 35-minute CI round to find out that a number
    /// was still on the wrong side of a threshold.
    /// </summary>
    public class AmbientBandTests
    {
        private const float M = DepthLight.UnitsPerMetre;

        /// <summary>0 (the waterline), a shallow dive, past the ramp, the report depth, the
        /// deepest map we ship, and 100 m for maps deeper than any that exist yet.</summary>
        private static readonly float[] Depths = { 0f, 15f, 23.4f, 30f, 52f, 100f };

        /// <summary>The staging depth of the QC model pass — where the Singha was photographed.</summary>
        private const float QcMetres = 23.4f;

        private static readonly SeabedGeom.Rgb White = new SeabedGeom.Rgb(1f, 1f, 1f);

        /// <summary>The measured cliff/statue base colour: sRGB 71 in all three channels.</summary>
        private static readonly SeabedGeom.Rgb Cliff =
            new SeabedGeom.Rgb(UnderwaterLight.DarkestProtectedSrgb,
                               UnderwaterLight.DarkestProtectedSrgb,
                               UnderwaterLight.DarkestProtectedSrgb);

        /// <summary>Rusting steel / red coral / warm stone — the colour a torch is FOR.</summary>
        private static readonly SeabedGeom.Rgb Red =
            new SeabedGeom.Rgb(200f / 255f, 40f / 255f, 30f / 255f);

        // ── 1. the floor is structural, not a number that happens to fit at one depth ─────

        [Test]
        public void EveryChannelOfTheGroundBandClearsTheToneCurvesBlackFloor()
        {
            // 🔴 THE GUARANTEE. Not "it looks brighter" — every channel, every depth, above the
            // point where ACES clamps to exactly 0. K is not chosen: BandFloorMultiple IS the
            // authored band's own red at the surface, expressed in BlackFloors (3.470), so the
            // rule reads "the ground band never attenuates below the value it starts at, in the
            // channel that has the least to give" and it cannot be brighter than the light rig
            // was authored to be.
            float k = UnderwaterLight.BandFloorMultiple;
            Assert.Greater(k, 3f, "the floor has drifted below the web's own ground band (3.25)");

            foreach (float metres in Depths)
            {
                SeabedGeom.Rgb ratio =
                    UnderwaterLight.BlackFloorRatios(UnderwaterLight.GroundBandAt(metres * M));
                Assert.GreaterOrEqual(ratio.R, k - 1e-3f, $"red at {metres} m is {ratio.R:F2}× BlackFloor");
                Assert.GreaterOrEqual(ratio.G, k - 1e-3f, $"green at {metres} m is {ratio.G:F2}× BlackFloor");
                Assert.GreaterOrEqual(ratio.B, k - 1e-3f, $"blue at {metres} m is {ratio.B:F2}× BlackFloor");
            }
        }

        [Test]
        public void TheFloorIsTiedToTheCurve_NotTypedIn()
        {
            // If somebody moves the exposure, the crush point moves; a literal would not, and the
            // failure would be silent. Restating the relationship here is the only way a test can
            // notice that the constant stopped meaning what its name says.
            Assert.AreEqual(UnderwaterLight.BandFloorLinear,
                            ToneMap.BlackFloor * UnderwaterLight.BandFloorMultiple, 1e-7f);
            Assert.AreEqual(ToneMap.SrgbToLinear(UnderwaterLight.WebGroundBand.R),
                            UnderwaterLight.BandFloorLinear, 1e-7f);
        }

        [Test]
        public void TheBandIsTheOneAppBootWrites()
        {
            // The three literals used to live only in AppBoot.SetupLighting, which is why nothing
            // in Core could reason about the band it was supposed to be flooring. Two copies of a
            // colour is a drift waiting to happen and this one already happened once.
            string boot = RepoFiles.Read("Assets/Scripts/Runtime/AppBoot.cs");
            Assert.NotNull(boot, $"cannot find AppBoot from {RepoFiles.SearchedFrom}");
            StringAssert.Contains("UnderwaterLight.WebSkyBand", boot);
            StringAssert.Contains("UnderwaterLight.WebEquatorBand", boot);
            StringAssert.Contains("UnderwaterLight.WebGroundBand", boot);

            // …and they are still the web's own hemisphere light (builder.html:510).
            Assert.AreEqual(0.074f, UnderwaterLight.WebGroundBand.R, 1e-6f);  // 0x12 × 1.05
            Assert.AreEqual(0.787f, UnderwaterLight.WebSkyBand.R, 1e-6f);     // 0xbf × 1.05
        }

        // ── 2 & 3. the symptom the user actually photographed ────────────────────────────

        [Test]
        public void AWhiteUndersideIsNeverBlack()
        {
            foreach (float metres in Depths)
            {
                float d = metres * M;
                SurfaceLight.Bytes(White, d, SurfaceLight.Facing.Down, SurfaceLight.NoLamp,
                                   out byte r, out byte g, out byte b);
                Assert.IsFalse(r == 0 && g == 0 && b == 0,
                               $"a white down-facing surface is pure black at {metres} m");
                Assert.Greater(b, 20,
                               $"a white underside at {metres} m is byte {b} in blue — that is a hole, not a surface");
            }
        }

        [Test]
        public void TheDarkestProtectedAlbedoIsNeverBlack_WhicheverWayItFaces()
        {
            // 🔴 THE TEST THAT CATCHES WHAT THE USER SEES. sRGB 71 is not a hypothetical: it is
            // the measured base colour of the cliff/statue atlas the Singha sits on, and 47.9% of
            // that atlas is darker than it. Before WO-E4 this was (0,0,0) from 23 m down — i.e.
            // half the statue was guaranteed black before a single texel was sampled.
            foreach (float metres in Depths)
            {
                float d = metres * M;
                foreach (SurfaceLight.Facing f in new[]
                         { SurfaceLight.Facing.Down, SurfaceLight.Facing.Side, SurfaceLight.Facing.Up })
                {
                    SurfaceLight.Bytes(Cliff, d, f, SurfaceLight.NoLamp,
                                       out byte r, out byte g, out byte b);
                    Assert.IsFalse(r == 0 && g == 0 && b == 0,
                                   $"sRGB 71 facing {f} at {metres} m renders pure black");
                }
            }
        }

        [Test]
        public void TheCrushThresholdMovedWellBelowTheAtlasItWasCuttingThrough()
        {
            // The same requirement stated as the number a reviewer can read straight off the
            // [Water] log line: the darkest base colour that still comes off an underside.
            // Shipped band at the QC depth: 72, against an atlas that is 47.9% darker than 71.
            int crush = SurfaceLight.CrushAlbedoSrgb(QcMetres * M, SurfaceLight.Facing.Down);
            Assert.Less(crush, 55,
                        $"an underside at the QC depth still crushes everything below sRGB {crush}");

            // And it must not get worse than that anywhere a map can go.
            foreach (float metres in Depths)
                Assert.LessOrEqual(SurfaceLight.CrushAlbedoSrgb(metres * M, SurfaceLight.Facing.Down), 66,
                                   $"crush threshold at {metres} m");
        }

        // ── 4. and it still dims with depth, which the user asked to keep ────────────────

        [Test]
        public void TheGroundBandStillDimsWithDepth()
        {
            // 🔴 The other side of the fix, and the one a "just brighten it" patch fails. Lifting
            // the band is only allowed as long as descending still looks like descending.
            float at15 = SurfaceLight.Luminance(UnderwaterLight.GroundBandAt(15f * M));
            float at52 = SurfaceLight.Luminance(UnderwaterLight.GroundBandAt(52f * M));
            float at100 = SurfaceLight.Luminance(UnderwaterLight.GroundBandAt(100f * M));

            Assert.Less(at52, at15 * 0.8f,
                        $"52 m is {at52 / at15:P0} of 15 m — the depth cue has been flattened away");
            Assert.Less(at100, at52, "100 m is not darker than 52 m");

            // The sky and equator bands were never near the floor and take the full curve, so the
            // scene as a whole dims harder than the ground band does.
            float sky15 = SurfaceLight.Luminance(UnderwaterLight.SkyBandAt(15f * M));
            float sky52 = SurfaceLight.Luminance(UnderwaterLight.SkyBandAt(52f * M));
            Assert.Less(sky52, sky15 * 0.65f);
        }

        [Test]
        public void AnUndersideIsStillClearlyDarkerThanAFlank()
        {
            // The trap on the other side of the fix: three bands that converge make every object
            // read as a sticker. The bounce gain is bounded by this, not by taste.
            foreach (float metres in Depths)
            {
                float d = metres * M;
                float gnd = SurfaceLight.Luminance(UnderwaterLight.GroundBandAt(d));
                float eq = SurfaceLight.Luminance(UnderwaterLight.EquatorBandAt(d));
                float sky = SurfaceLight.Luminance(UnderwaterLight.SkyBandAt(d));
                Assert.Less(gnd, eq * 0.75f, $"underside vs flank at {metres} m");
                Assert.Less(eq, sky, $"flank vs top at {metres} m");
            }
        }

        [Test]
        public void TheBounceRampsInWithDepth_SoTheMapViewIsUntouched()
        {
            // Above the waterline the band is exactly what AppBoot authored — the map view was
            // tuned over four QC rounds and nothing here may reach up there.
            SeabedGeom.Rgb air = UnderwaterLight.GroundBandAt(-20f * M);
            Assert.AreEqual(UnderwaterLight.WebGroundBand.R, air.R, 1e-6f);
            Assert.AreEqual(UnderwaterLight.WebGroundBand.G, air.G, 1e-6f);
            Assert.AreEqual(UnderwaterLight.WebGroundBand.B, air.B, 1e-6f);

            Assert.AreEqual(1f, UnderwaterLight.SeabedBounce(0f), 1e-6f);
            Assert.AreEqual(UnderwaterLight.SeabedBounceGain,
                            UnderwaterLight.SeabedBounce(60f * M), 1e-5f);

            float prev = -1f;
            for (float metres = 0f; metres <= 40f; metres += 0.5f)
            {
                float s = UnderwaterLight.SeabedBounce(metres * M);
                Assert.GreaterOrEqual(s, prev, $"the bounce went backwards at {metres} m");
                prev = s;
            }
        }

        // ── 6, 7, 8. the torch ──────────────────────────────────────────────────────────

        /// <summary>Scene-linear red off the rust-red wall at this depth, lamp at
        /// <paramref name="lampDistance"/> (<see cref="SurfaceLight.NoLamp"/> for none).</summary>
        private static float RedOf(float metres, float lampDistance)
        {
            SurfaceLight.Radiance(Red, metres * M, SurfaceLight.Facing.Side, lampDistance,
                                  out float r, out _, out _);
            return r;
        }

        [Test]
        public void TheTorchGivesTheRedBack()
        {
            // 🔴 "เมื่อโดนแสงไฟฉาย สีเดิมจะกลับมา" as arithmetic. A rust-red surface, lit only by the
            // ambient, loses three quarters of its red by 52 m — that is the depth cue the user
            // asked to KEEP. Switch the lamp on at the drone's own working distance and the red
            // comes back past where it was at the surface, because the lamp is white and nothing
            // on its path takes the red out again.
            float surface = RedOf(0f, SurfaceLight.NoLamp);

            float deepDark = RedOf(52f, SurfaceLight.NoLamp);
            Assert.Less(deepDark, surface * 0.4f,
                        $"at 52 m the red is still {deepDark / surface:P0} of the surface — nothing was absorbed");

            float deepLit = RedOf(52f, DiveLightMath.Reach);
            Assert.GreaterOrEqual(deepLit, surface * 0.7f,
                                  $"the lamp only gives back {deepLit / surface:P0} of the surface red");
        }

        [Test]
        public void TheTorchIsNotDimmedByDepth_BecauseItsPathIsShortNotDeep()
        {
            // The structural version of the same thing: the lamp's own contribution is IDENTICAL
            // at every depth. If some future change routes the lamp through DepthLight.Attenuation
            // "for consistency", this is what says no.
            SurfaceLight.Irradiance(0f, SurfaceLight.Facing.Side, DiveLightMath.Reach,
                                    out float lr0, out float lg0, out float lb0);
            SurfaceLight.Irradiance(0f, SurfaceLight.Facing.Side, SurfaceLight.NoLamp,
                                    out float ar0, out float ag0, out float ab0);

            foreach (float metres in new[] { 15f, 30f, 52f, 100f })
            {
                float d = metres * M;
                SurfaceLight.Irradiance(d, SurfaceLight.Facing.Side, DiveLightMath.Reach,
                                        out float lr, out float lg, out float lb);
                SurfaceLight.Irradiance(d, SurfaceLight.Facing.Side, SurfaceLight.NoLamp,
                                        out float ar, out float ag, out float ab);
                Assert.AreEqual(lr0 - ar0, lr - ar, 1e-6f, $"lamp red at {metres} m");
                Assert.AreEqual(lg0 - ag0, lg - ag, 1e-6f, $"lamp green at {metres} m");
                Assert.AreEqual(lb0 - ab0, lb - ab, 1e-6f, $"lamp blue at {metres} m");
            }

            // …and the ambient, which travels from the surface, very much is dimmed.
            Assert.Less(ar0 * 0.5f, ar0);
            SurfaceLight.Irradiance(52f * M, SurfaceLight.Facing.Side, SurfaceLight.NoLamp,
                                    out float deepR, out _, out _);
            Assert.Less(deepR, ar0 * 0.5f, "the ambient's red survived 52 m — the depth curve is gone");
        }

        [Test]
        public void TheTorchPutsTheHueBackWhereTheSurfaceHadIt()
        {
            // Red against blue: at the surface a rust-red wall reads 17.7:1. Unlit at depth the
            // water has taken it down to a third of that (the picture has the WATER's colour, not
            // the wall's). With the lamp on it comes back inside a factor of two at every depth.
            float surface = SurfaceLight.RedToBlue(Red, 0f, SurfaceLight.Facing.Side);

            foreach (float metres in new[] { 15f, 30f, 52f })
            {
                float d = metres * M;
                float lit = SurfaceLight.RedToBlue(Red, d, SurfaceLight.Facing.Side, DiveLightMath.Reach);
                Assert.Greater(lit, surface * 0.5f, $"lamp-lit hue at {metres} m is {lit:F1}:1");
                Assert.Less(lit, surface * 2.0f, $"lamp-lit hue at {metres} m is {lit:F1}:1");

                // The test has teeth only if the unlit case fails it, which is the report.
                float dark = SurfaceLight.RedToBlue(Red, d, SurfaceLight.Facing.Side);
                Assert.Less(dark, surface * 0.5f,
                            $"unlit at {metres} m is {dark:F1}:1 — the water is not taking the red");
            }
        }

        [Test]
        public void TheTorchDoesNotLightTheWholeMap()
        {
            // 🔴 A report this project has already had once. The web throws its lamps 460 u across
            // a 340 u map; 90 u is the user's own number and it is not ours to widen.
            Assert.LessOrEqual(DiveLightMath.LampRange, 150f,
                               "the lamp range has been widened — 'ไฟฉายสว่างทั้งแมพ' is a report, not a theory");

            Assert.AreEqual(0f, DiveLightMath.LampFalloff(DiveLightMath.LampRange), 1e-9f);
            Assert.AreEqual(0f, DiveLightMath.LampFalloff(200f), 1e-9f);
            Assert.AreEqual(0f, DiveLightMath.LampFalloff(340f), 1e-9f,
                            "the far rim of the seabed disc is being lit by the torch");

            // Stated on the picture rather than on the constant: a surface past the range renders
            // EXACTLY as it does with no lamp at all.
            SurfaceLight.Radiance(Red, 52f * M, SurfaceLight.Facing.Side, 200f,
                                  out float fr, out float fg, out float fb);
            SurfaceLight.Radiance(Red, 52f * M, SurfaceLight.Facing.Side, SurfaceLight.NoLamp,
                                  out float nr, out float ng, out float nb);
            Assert.AreEqual(nr, fr, 1e-9f);
            Assert.AreEqual(ng, fg, 1e-9f);
            Assert.AreEqual(nb, fb, 1e-9f);

            // And it does fall off inside the range — a torch, not a floodlight.
            Assert.Greater(DiveLightMath.LampFalloff(5f), DiveLightMath.LampFalloff(50f) * 4f);
        }

        /// <summary>
        /// Not a gate — the table, so the numbers in a work-order report can be reproduced in two
        /// seconds instead of quoted from somebody's notes.
        ///
        ///     bash tools/test.sh --where "test =~ DumpBandTable" --explicit
        /// </summary>
        [Test, Explicit]
        public void DumpBandTable()
        {
            TestContext.Out.WriteLine(
                "   m | bounce |          ground band (authored)         |   x BlackFloor    | white-down | sRGB71-down | crushAlb | gnd/eq");
            foreach (float metres in new[] { 0f, 5f, 15f, 23.4f, 30f, 52f, 100f })
            {
                float d = metres * M;
                SeabedGeom.Rgb b = UnderwaterLight.GroundBandAt(d);
                SeabedGeom.Rgb k = UnderwaterLight.BlackFloorRatios(b);
                SurfaceLight.Bytes(White, d, SurfaceLight.Facing.Down, SurfaceLight.NoLamp,
                                   out byte wr, out byte wg, out byte wb);
                SurfaceLight.Bytes(Cliff, d, SurfaceLight.Facing.Down, SurfaceLight.NoLamp,
                                   out byte cr, out byte cg, out byte cb);
                TestContext.Out.WriteLine(
                    $"{metres,6:F1} |  x{UnderwaterLight.SeabedBounce(d):F2} | " +
                    $"({b.R:F5},{b.G:F5},{b.B:F5}) | ({k.R,6:F2},{k.G,6:F2},{k.B,6:F2}) | " +
                    $"({wr,3},{wg,3},{wb,3}) | ({cr,3},{cg,3},{cb,3}) |  sRGB{SurfaceLight.CrushAlbedoSrgb(d, SurfaceLight.Facing.Down),3} | " +
                    $"{SurfaceLight.Luminance(b) / SurfaceLight.Luminance(UnderwaterLight.EquatorBandAt(d)):F3}");
            }

            TestContext.Out.WriteLine();
            TestContext.Out.WriteLine("rust-red wall sRGB(200,40,30), side-facing, lamp at Reach=54u:");
            TestContext.Out.WriteLine("   m | lamp |      radiance linear      |    bytes     | red vs surface | R:B");
            float surfRed = RedOf(0f, SurfaceLight.NoLamp);
            foreach (float metres in new[] { 0f, 15f, 30f, 52f })
            foreach (float lamp in new[] { SurfaceLight.NoLamp, DiveLightMath.Reach })
            {
                float d = metres * M;
                SurfaceLight.Radiance(Red, d, SurfaceLight.Facing.Side, lamp,
                                      out float r, out float g, out float bl);
                SurfaceLight.Bytes(Red, d, SurfaceLight.Facing.Side, lamp,
                                   out byte br, out byte bg, out byte bb);
                TestContext.Out.WriteLine(
                    $"{metres,6:F0} | {(float.IsPositiveInfinity(lamp) ? "off " : "ON  ")} | " +
                    $"({r:F5},{g:F5},{bl:F5}) | ({br,3},{bg,3},{bb,3}) | {r / surfRed,10:P1} | {r / bl,7:F2}");
            }
        }

        [Test]
        public void PathTransmittanceIsBeerLambert_WithNoPlayabilityFloor()
        {
            // DepthLight.Attenuation floors at 0.25 so the deep stays readable. A lamp's path has
            // no such licence — and the numbers here are the evidence for not attenuating the lamp
            // at all: at the drone's round-trip distance a literal model leaves 2.7% of the red,
            // i.e. it would delete the feature.
            DepthLight.PathTransmittance(0f, out float r0, out float g0, out float b0);
            Assert.AreEqual(1f, r0, 1e-6f);
            Assert.AreEqual(1f, g0, 1e-6f);
            Assert.AreEqual(1f, b0, 1e-6f);

            DepthLight.PathTransmittance(2f * M, out float r2, out _, out float b2);
            Assert.Greater(r2, 0.6f, "red should still be mostly there over a diver's torch distance");
            Assert.Greater(b2, r2, "blue outlives red at every distance");

            DepthLight.PathTransmittance(18f * M, out float r18, out _, out _);
            Assert.Less(r18, 0.05f);
            Assert.Less(r18, DepthLight.Floor,
                        "PathTransmittance has inherited the ambient's playability floor");
        }
    }
}
