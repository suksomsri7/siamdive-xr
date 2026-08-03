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

            // The two colour matrices' leading terms — enough to catch a transposed or retyped copy.
            foreach (string c in new[] { "0.59719", "0.90834", "0.83777", "1.60475", "1.10813", "1.07602" })
                StringAssert.Contains(c, shader, $"ACES matrix term {c} is missing from the shader");

            // And the divisor that is easiest of all to lose.
            StringAssert.Contains("_Exposure / 0.6", shader,
                "the shader has lost three.js's /0.6 — every frame is ~1.5 stops dark");
        }
    }
}
