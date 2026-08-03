using System;

namespace DiveMap.Core
{
    /// <summary>
    /// The film curve the web renders through, in C# so it can be asserted instead of admired.
    ///
    /// 🔴 WHY THIS EXISTS. The user's report was blunt: "texture พื้นผิวแย่มาก … บนเว็บแสดงผลดีกว่ามาก"
    /// — and the app was already loading textures FOUR TIMES the resolution the web loads (2048²
    /// against 512²). So the files were never the problem. Lining the two pipelines up side by
    /// side, three of the four differences were in the same place — the end of the frame:
    ///
    ///     three.js (builder.html:484-485)          Unity (before this change)
    ///     outputColorSpace = SRGBColorSpace        gamma project: no encode, no decode
    ///     toneMapping = ACESFilmicToneMapping      none at all
    ///     toneMappingExposure = 1.05               n/a
    ///
    /// A gamma project with no tone mapping adds light in the wrong space and then writes it
    /// straight to the screen: every lit surface climbs a straight line to white and clips, so
    /// highlights lose their shape, midtones go chalky, and the shading that carries surface
    /// DETAIL is exactly what gets flattened. That is what "the texture looks bad" means when the
    /// texture is fine. ACES is an S-curve — it has a toe and a shoulder, so a highlight rolls off
    /// instead of clipping and there is somewhere for the top end of a normal-mapped surface to go.
    ///
    /// 🔎 This is a PORT, not an interpretation. The maths below is three.js r160's
    /// <c>ACESFilmicToneMapping</c> verbatim (Stephen Hill's fit of the ACES RRT+ODT, the same one
    /// Unreal and Unity's own post stack use), INCLUDING the <c>/ 0.6</c> three.js applies to the
    /// exposure — miss that and every frame comes out 40% dark while the number in the file still
    /// says 1.05. <c>Shaders/DM_AcesToneMap.shader</c> is the same arithmetic in HLSL and
    /// <c>ToneMapTests</c> pins them to each other by hand-checked values, because a curve that
    /// exists in two languages will drift in one of them.
    /// </summary>
    public static class ToneMap
    {
        /// <summary>builder.html:485 — <c>renderer.toneMappingExposure = 1.05</c>.</summary>
        public const float Exposure = 1.05f;

        /// <summary>
        /// three.js scales exposure by 1/0.6 before the fit, so its "1.0" sits where the ACES
        /// reference white does. It is not a fudge factor and it is not ours to choose: drop it and
        /// the app is a stop and a half darker than the web at the same authored exposure.
        /// </summary>
        public const float ThreeJsGain = 1f / 0.6f;

        // ACES colour-space matrices (three.js tonemapping_pars_fragment.glsl.js). GLSL mat3
        // constructors take COLUMNS; these are the same matrices written out as rows.
        private static readonly float[] InR0 = { 0.59719f, 0.35458f, 0.04823f };
        private static readonly float[] InR1 = { 0.07600f, 0.90834f, 0.13383f };
        private static readonly float[] InR2 = { 0.02840f, 0.13383f, 0.83777f };

        private static readonly float[] OutR0 = { 1.60475f, -0.53108f, -0.07367f };
        private static readonly float[] OutR1 = { -0.10208f, 1.10813f, -0.00605f };
        private static readonly float[] OutR2 = { -0.00327f, -0.07276f, 1.07602f };

        /// <summary>
        /// Scene-linear in, display-linear out (still linear — the sRGB encode is the GPU's job,
        /// or <see cref="LinearToSrgb"/>'s when a test wants the byte a screenshot would hold).
        /// </summary>
        public static void Aces(float r, float g, float b,
                                out float outR, out float outG, out float outB,
                                float exposure = Exposure)
        {
            float k = exposure * ThreeJsGain;
            r *= k; g *= k; b *= k;

            float ar = InR0[0] * r + InR0[1] * g + InR0[2] * b;
            float ag = InR1[0] * r + InR1[1] * g + InR1[2] * b;
            float ab = InR2[0] * r + InR2[1] * g + InR2[2] * b;

            ar = Fit(ar); ag = Fit(ag); ab = Fit(ab);

            outR = Clamp01(OutR0[0] * ar + OutR0[1] * ag + OutR0[2] * ab);
            outG = Clamp01(OutR1[0] * ar + OutR1[1] * ag + OutR1[2] * ab);
            outB = Clamp01(OutR2[0] * ar + OutR2[1] * ag + OutR2[2] * ab);
        }

        /// <summary>The RRT + ODT fit itself (Stephen Hill / three.js <c>RRTAndODTFit</c>).</summary>
        public static float Fit(float v)
        {
            float a = v * (v + 0.0245786f) - 0.000090537f;
            float b = v * (0.983729f * v + 0.4329510f) + 0.238081f;
            return b == 0f ? 0f : a / b;
        }

        // ── sRGB transfer function ───────────────────────────────────────────────
        // The same curve as GlbShading.SrgbToLinear (which models a GPU sampler and is asserted
        // against these by a test). Kept as floats here because this side of the project is doing
        // pixels, not proving a normal-map theorem.

        /// <summary>
        /// sRGB → linear. NOT clamped at 1: an ambient band can legitimately be authored above
        /// white (the web's hemisphere light is 0xbfe6ff × 1.05) and clamping it here would quietly
        /// throw the overshoot away — the value would still read 1.05 in the file.
        /// </summary>
        public static float SrgbToLinear(float c)
        {
            if (c <= 0f) return 0f;
            return c <= 0.04045f ? c / 12.92f : (float)Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        /// <summary>Linear → sRGB, likewise unclamped above 1.</summary>
        public static float LinearToSrgb(float c)
        {
            if (c <= 0f) return 0f;
            return c <= 0.0031308f ? c * 12.92f : (float)(1.055 * Math.Pow(c, 1.0 / 2.4) - 0.055);
        }

        /// <summary>
        /// Dim an sRGB-authored colour channel by a LINEAR factor, and hand back an sRGB-authored
        /// channel — the one operation the depth attenuation is.
        ///
        /// 🔴 WHY IT IS NOT A MULTIPLICATION. Every colour in this project is authored the way the
        /// web authors them: as sRGB, the hex out of builder.html. The depth attenuation is not a
        /// colour, it is a transmittance — the fraction of the light that survives the water — and
        /// light multiplies in LINEAR space. Multiply the authored number directly and "half the
        /// light" becomes about a fifth of it, with the error growing the deeper you go, which is
        /// exactly where the picture is being asked to be trustworthy.
        ///
        /// 🔎 Every depth-dependent thing in the scene goes through this one function — the fog,
        /// the backdrop and the ambient — so they are dimmed identically and their ratios cannot
        /// drift with depth. That is the whole guarantee of WO-E3, and it lives in this line.
        /// </summary>
        public static float ScaleLight(float srgb, float k)
        {
            if (k <= 0f || srgb <= 0f) return 0f;
            if (k == 1f) return srgb;
            return LinearToSrgb(SrgbToLinear(srgb) * k);
        }

        /// <summary>The byte an sRGB texture (or a screenshot) holds for a linear value.</summary>
        public static byte LinearToByte(float linear)
        {
            float s = LinearToSrgb(linear) * 255f + 0.5f;
            if (s <= 0f) return 0;
            if (s >= 255f) return 255;
            return (byte)s;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
