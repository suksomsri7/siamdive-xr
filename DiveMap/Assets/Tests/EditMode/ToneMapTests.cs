using NUnit.Framework;
using DiveMap.Core;

namespace DiveMap.Tests
{
    /// <summary>
    /// The film curve, and the two places it lives.
    ///
    /// 🔴 The curve exists twice on purpose — <see cref="ToneMap"/> in C# so the QC harness and
    /// these tests can reason about it, and <c>Shaders/DM_AcesToneMap.shader</c> in HLSL because
    /// that is where a frame actually goes through it. Two copies of one formula is a drift waiting
    /// to happen, and the drift would be invisible: nothing crashes when a tone curve is 5% off,
    /// the picture just stops matching the web. So the coefficients are pinned across both
    /// languages by reading the shader source, and the C# side is pinned to hand-checked values.
    /// </summary>
    public class ToneMapTests
    {
        [Test]
        public void TheExposureIsTheWebs()
        {
            // builder.html:485 — renderer.toneMappingExposure = 1.05.
            Assert.AreEqual(1.05f, ToneMap.Exposure, 1e-6);
        }

        [Test]
        public void TheThreeJsGainIsNotOptional()
        {
            // three.js multiplies exposure by 1/0.6 before the fit. Leaving it out is a silent
            // ~1.5-stop darkening that no number in any file would reveal — the exposure constant
            // would still read 1.05 and every frame would still be wrong.
            Assert.AreEqual(1f / 0.6f, ToneMap.ThreeJsGain, 1e-6);
        }

        [Test]
        public void BlackStaysBlackAndTheCurveNeverGoesBackwards()
        {
            ToneMap.Aces(0f, 0f, 0f, out float r, out float g, out float b);
            Assert.AreEqual(0f, r, 2e-3);
            Assert.AreEqual(0f, g, 2e-3);
            Assert.AreEqual(0f, b, 2e-3);

            float prev = -1f;
            for (float v = 0f; v <= 8f; v += 0.02f)
            {
                ToneMap.Aces(v, v, v, out _, out float mid, out _);
                Assert.GreaterOrEqual(mid, prev - 1e-5f, $"the curve dipped at {v}");
                Assert.LessOrEqual(mid, 1f);
                prev = mid;
            }
        }

        [Test]
        public void ItHasAShoulder_WhichIsTheWholeReasonItIsHere()
        {
            // Without a curve, twice the light is twice the pixel until it clips and stays clipped:
            // every highlight above 1.0 becomes the same white and the shading that carries surface
            // detail is destroyed there. ACES has to keep separating values well past 1.
            ToneMap.Aces(1f, 1f, 1f, out _, out float one, out _);
            ToneMap.Aces(2f, 2f, 2f, out _, out float two, out _);
            ToneMap.Aces(4f, 4f, 4f, out _, out float four, out _);

            Assert.Greater(two, one + 0.02f, "2× the light is the same pixel as 1× — no shoulder");
            Assert.Greater(four, two + 0.005f, "4× and 2× have merged");
            Assert.Less(four, 1.0001f);

            // …and it is a SHOULDER, i.e. compressive: the second doubling must buy less than the
            // first, or it is just a straight line that has not clipped yet.
            Assert.Less(four - two, two - one);
        }

        [Test]
        public void ItHasAToe_SoTheDeepWaterDoesNotSlideToBlack()
        {
            // The other half of why DepthLight's floor could come down from 0.35 to 0.25: a linear
            // 0.02 — roughly the water at 52 m — must still land somewhere a screen can show.
            ToneMap.Aces(0.02f, 0.02f, 0.02f, out _, out float g, out _);
            byte shown = ToneMap.LinearToByte(g);
            Assert.Greater(shown, 20, "a linear 0.02 comes out as near-black; the toe is missing");
        }

        [Test]
        public void RowsOfTheAcesMatricesSumToOne_SoNeutralStaysNeutral()
        {
            // 🔴 The test that would have caught the InputMat typo, and the only kind that could.
            // TheHlslCopyOfTheCurveHasNotDrifted pins C# to HLSL, and both copies carried the SAME
            // wrong number — one had been retyped from the other — so they agreed with each other
            // all the way through WO-E3. Arithmetic does not care how many copies agree.
            //
            // Every row of sRGB→AP1 and of AP1→sRGB sums to 1, because both map white to white.
            // A neutral input therefore gives all three channels the same value going into
            // RRTAndODTFit, and nothing after that can tint it. If grey comes out coloured, a row
            // does not sum to 1. Before the fix, ToneMap.Aces(0.0074, 0.0074, 0.0074) returned
            // bytes (5, 7, 5).
            foreach (float v in new[] { 0.0074f, 0.02f, 0.2f, 0.6f, 1.0f })
            {
                ToneMap.Aces(v, v, v, out float r, out float g, out float b);
                Assert.AreEqual(r, g, 1e-4f,
                    $"neutral {v} came out tinted (r={r}, g={g}) — an ACES matrix row does not sum to 1");
                Assert.AreEqual(g, b, 1e-4f,
                    $"neutral {v} came out tinted (g={g}, b={b}) — an ACES matrix row does not sum to 1");
            }
        }

        [Test]
        public void TheToeAlsoHasAHardFloor_AndThatIsWhereBlackOfSubjectComesFrom()
        {
            // 🔴 The half of the toe nobody had written down. Fit()'s numerator carries a negative
            // constant (-0.000090537), so the curve is NEGATIVE below its positive root and Aces()
            // clamps that to exactly 0. "ACES lifts the bottom of the range" is true at a linear
            // 0.02 (the test above) and false below ToneMap.BlackFloor, where it does the opposite.
            //
            // This matters because QcPixels.BlackOfSubjectPercent counts pixels that are EXACTLY
            // (0,0,0). Since the pipeline moved to linear + ACES that statistic no longer means
            // "no light reached this pixel" — it means "less light than the toe can represent",
            // which is a much weaker accusation against the model being photographed.
            Assert.Less(ToneMap.Fit(ToneMap.FitZeroCrossing * 0.9f), 0f,
                        "Fit() must be negative below its root — that is what makes the clamp bite");
            Assert.AreEqual(0f, ToneMap.Fit(ToneMap.FitZeroCrossing), 1e-6f,
                        "FitZeroCrossing is not where Fit() actually crosses zero");
            Assert.Greater(ToneMap.Fit(ToneMap.FitZeroCrossing * 1.1f), 0f);
        }

        [Test]
        public void BlackFloorIsTheExactLinearValueThatBecomesByteZero()
        {
            // Stated for grey because both ACES matrices have rows summing to 1, so a neutral
            // input stays neutral and the transform collapses to Fit(v · Exposure · ThreeJsGain).
            ToneMap.Aces(ToneMap.BlackFloor * 0.95f, ToneMap.BlackFloor * 0.95f,
                         ToneMap.BlackFloor * 0.95f, out float r0, out float g0, out float b0);
            Assert.AreEqual(0, ToneMap.LinearToByte(r0), "just under BlackFloor must be byte 0");
            Assert.AreEqual(0, ToneMap.LinearToByte(g0));
            Assert.AreEqual(0, ToneMap.LinearToByte(b0));

            ToneMap.Aces(ToneMap.BlackFloor * 4f, ToneMap.BlackFloor * 4f, ToneMap.BlackFloor * 4f,
                         out _, out float g1, out _);
            Assert.Greater(ToneMap.LinearToByte(g1), 0,
                           "well above BlackFloor must survive, or the floor is in the wrong place");

            // The number in human units: BlackFloor is byte 6 in a plain sRGB encode, so the whole
            // 1..6 band the old gamma pipeline could show is now collapsed onto 0.
            Assert.AreEqual(6, ToneMap.LinearToByte(ToneMap.BlackFloor), 1,
                            "BlackFloor should sit around byte 6 before tone mapping");
        }

        [Test]
        public void TheBandTheUserPhotographedWasUnderTheFloor_AndThatIsWhyTheStatueWasBlack()
        {
            // 🔎 Measured, not invented: at the QC staging depth (waterLevel 240, stage y 98.06 →
            // 23.4 m) the app logged its own ambient as
            //     [Water] ... gnd=(0.022,0.148,0.231)
            // A surface facing straight down sees only that band. Its RED channel in linear is
            // 0.0017, BELOW ToneMap.BlackFloor before any albedo is applied — so red was pinned to
            // byte 0 on every down-facing surface in the scene even for a pure white model.
            //
            // 🔴 This is kept as the BEFORE picture and asserted as arithmetic, not as the app's
            // current state: WO-E4 changed the band (UnderwaterLight.GroundBandAt) and
            // AmbientBandTests holds the after. Both belong in the repo — a fix nobody can state
            // the shape of is a fix nobody can tell has regressed.
            float gr = ToneMap.SrgbToLinear(0.022f);
            float gg = ToneMap.SrgbToLinear(0.148f);
            float gb = ToneMap.SrgbToLinear(0.231f);
            Assert.Less(gr, ToneMap.BlackFloor,
                        "the ground ambient band's red is supposed to be under the floor at 23 m");

            ToneMap.Aces(gr, gg, gb, out float r, out float g, out float b);   // albedo 1.0 = white
            Assert.AreEqual(0, ToneMap.LinearToByte(r),
                            "red should be crushed even at albedo 1 — if not, re-measure the band");
            Assert.Greater(ToneMap.LinearToByte(g), 0, "green still has something left at 23 m");
            Assert.Greater(ToneMap.LinearToByte(b), ToneMap.LinearToByte(g),
                           "blue outlives green underwater; that ordering is the depth cue");
        }

        [Test]
        public void LiftLightRaisesInLight_AndNeverDims()
        {
            // The mirror of ScaleLight, and wrong in the same way if done in the wrong space: the
            // value handed back is authored sRGB, the requirement is about radiance.
            const float min = 0.01f;
            float lifted = ToneMap.LiftLight(0.0f, min);
            Assert.AreEqual(min, ToneMap.SrgbToLinear(lifted), 1e-6f);

            // Already brighter → untouched, byte for byte.
            Assert.AreEqual(0.5f, ToneMap.LiftLight(0.5f, min), 1e-6f);

            // And doing it twice changes nothing.
            Assert.AreEqual(lifted, ToneMap.LiftLight(lifted, min), 1e-6f);

            // A naive max() on the authored numbers would raise this to 0.01 sRGB, which is a
            // linear 0.00077 — thirteen times short of what was asked for.
            Assert.Greater(lifted, min, "the lift was done in the authored numbers, not in light");
        }

        [Test]
        public void TheSrgbTransferFunctionRoundTrips_AndAgreesWithGlbShading()
        {
            // GlbShading models the GPU sampler and is the file the normal-map diagnosis was
            // written against; this one does pixels. They have to be the same curve or the QC
            // numbers and the shading theory are about different apps.
            for (float s = 0f; s <= 1f; s += 0.01f)
            {
                float lin = ToneMap.SrgbToLinear(s);
                Assert.AreEqual(GlbShading.SrgbToLinear(s), lin, 1e-5, $"at sRGB {s}");
                Assert.AreEqual(s, ToneMap.LinearToSrgb(lin), 1e-4, $"round trip at {s}");
            }
        }

        [Test]
        public void LinearToByte_ClampsRatherThanWraps()
        {
            Assert.AreEqual(0, ToneMap.LinearToByte(-5f));
            Assert.AreEqual(255, ToneMap.LinearToByte(9f));
            Assert.AreEqual(255, ToneMap.LinearToByte(1f));
            Assert.AreEqual(0, ToneMap.LinearToByte(0f));
        }

        [Test]
        public void AttenuatingInLinearIsNotTheSameAsAttenuatingTheBytes()
        {
            // The reason Backdrop.Rebake decodes before it multiplies. Halving the light is not
            // halving the byte, and the gap is largest exactly where the deep water lives — so
            // getting this wrong would over-darken the thing the whole work order is about.
            const float srgb = 0.5f, k = 0.5f;
            byte correct = ToneMap.LinearToByte(ToneMap.SrgbToLinear(srgb) * k);
            var naive = (byte)(srgb * k * 255f);
            Assert.Greater(correct, naive + 20,
                           "byte-space dimming and light-space dimming have stopped differing");
        }

        [Test]
        public void TheHlslCopyOfTheCurveHasNotDrifted()
        {
            string shader = RepoFiles.Read("Assets/Shaders/DM_AcesToneMap.shader");
            Assert.NotNull(shader, $"cannot find the ACES shader from {RepoFiles.SearchedFrom}");

            // The RRT+ODT fit's five constants, verbatim.
            foreach (string c in new[] { "0.0245786", "0.000090537", "0.983729", "0.4329510", "0.238081" })
                StringAssert.Contains(c, shader, $"RRTAndODTFit constant {c} is missing from the shader");

            // 🔴 ALL EIGHTEEN terms, not the six diagonal ones. The old list of six was why the
            // InputMat typo (0.13383 where 0.01566 belongs) survived: it sat off the diagonal, in
            // both copies, and every term this test looked at was correct.
            foreach (string c in new[] { "0.59719", "0.35458", "0.04823",
                                         "0.07600", "0.90834", "0.01566",
                                         "0.02840", "0.13383", "0.83777",
                                         "1.60475", "-0.53108", "-0.07367",
                                         "-0.10208", "1.10813", "-0.00605",
                                         "-0.00327", "-0.07276", "1.07602" })
                StringAssert.Contains(c, shader, $"ACES matrix term {c} is missing from the shader");

            // And the divisor that is easiest of all to lose.
            StringAssert.Contains("_Exposure / 0.6", shader,
                "the shader has lost three.js's /0.6 — every frame is ~1.5 stops dark");
        }
    }
}
