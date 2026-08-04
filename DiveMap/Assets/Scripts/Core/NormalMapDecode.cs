namespace DiveMap.Core
{
    /// <summary>
    /// What a tangent-space normal map texel becomes by the time the shader sees it, written as
    /// arithmetic so the claim "the map is being decoded wrongly" is a number and not an opinion.
    ///
    /// 🔴 THE MECHANISM, traced through the packages this project actually ships (not from memory
    /// — the package tarballs were unpacked and read):
    ///
    ///   1. <c>com.unity.cloud.gltfast@6.19.0/Runtime/Scripts/GltfImport.cs:1676</c>
    ///      <c>if (QualitySettings.activeColorSpace == ColorSpace.Linear)</c> is the ONLY place the
    ///      <c>textureGamma</c> array is ever allocated. This project runs in GAMMA
    ///      (<c>ProjectSettings m_ActiveColorSpace: 0</c>), so it stays null.
    ///   2. …line 1796: <c>var forceSampleLinear = textureGamma != null &amp;&amp; !textureGamma[i];</c>
    ///      With the array null this is FALSE for every texture in the file — including the normal
    ///      maps, which glTFast would otherwise have flagged as data.
    ///   3. …that false travels into <c>KtxImageLoader.cs:42</c>
    ///      (<c>await ktx.LoadTexture2D(linear, readable)</c>) and on into
    ///      <c>com.unity.cloud.ktx@3.7.0/Runtime/Scripts/KtxTexture.cs:269</c>
    ///      (<c>GetFormat(m_Ktx, m_Ktx, linear)</c>).
    ///   4. <c>TranscodeFormatHelper.cs:296 GetPreferredFormat(..., isLinear)</c> only sets
    ///      <c>TextureFeatures.Linear</c> when that flag is true, and the format table at
    ///      lines 100-215 pairs every entry: <c>RGBA_ASTC4X4_SRGB</c> vs <c>RGBA_ASTC4X4_UNorm</c>,
    ///      <c>RGBA_BC7_SRGB</c> vs <c>RGBA_BC7_UNorm</c>, and so on. Without the Linear bit the
    ///      SRGB half of each pair is what matches first.
    ///   5. <c>KtxNativeInstance.cs:318</c> then builds the texture with exactly that format:
    ///      <c>new Texture2D(width, height, gf, flags)</c>.
    ///
    /// So a normal map — a file whose own KTX2 header correctly declares a LINEAR transfer
    /// function, which was verified on the CDN — is handed to the GPU in an sRGB-typed format
    /// because of a decision made three packages away, from the project's colour space alone. The
    /// file is not consulted at any step.
    ///
    /// 🔴 WHERE THE FIX WENT. Not here. <see cref="DiveMap.Runtime"/>'s glTFast import add-on
    /// claims exactly the textures a material uses as a normal map and re-opens them with
    /// <c>linear: true</c>, which is the same value glTFast itself would pass in a linear project.
    /// That removes step 2 rather than compensating for step 5, so nothing in this file is applied
    /// to a pixel at runtime — it exists to make the two readings measurable and testable, and to
    /// give the shipped log line a number to print.
    ///
    /// 🔴 THE UNPACK MODELLED HERE IS THE RGB ONE. glTF normal maps are plain RGB: x in red, y in
    /// green, z in blue, all three stored. That is what <c>DM_UnpackNormalRGB</c> in
    /// <c>DM_FishWave.cginc</c> now does and what glTFast's own shader does. It is NOT
    /// <c>UnpackNormalDXT5nm</c>, which rebuilds z from two channels — <see cref="GlbShading"/>'s
    /// older <c>NeutralTiltDegrees</c> models that one and is kept for the log line it feeds.
    /// </summary>
    public static class NormalMapDecode
    {
        /// <summary>The sRGB EOTF a GPU applies to a texture uploaded in an sRGB format.</summary>
        public static double SrgbToLinear(double srgb) => GlbShading.SrgbToLinear(srgb);

        /// <summary>
        /// Its exact inverse — the OETF. Present so <c>decode(encode(x)) == x</c> is a test rather
        /// than an assumption, and so anyone reaching for a shader-side compensation has the right
        /// curve in front of them instead of the 2.2 approximation.
        /// </summary>
        public static double LinearToSrgb(double linear)
        {
            if (linear <= 0.0) return 0.0;
            if (linear >= 1.0) return 1.0;
            return linear <= 0.0031308
                ? linear * 12.92
                : 1.055 * System.Math.Pow(linear, 1.0 / 2.4) - 0.055;
        }

        /// <summary>
        /// One texel, as the fragment shader receives it after <c>rgb * 2 - 1</c>.
        /// </summary>
        /// <param name="srgbDecoded">True to model the broken path: the sampler applied an sRGB
        /// decode to a texture that stores data, before the shader's unpack.</param>
        public static void Unpack(byte r, byte g, byte b, bool srgbDecoded,
                                  out double x, out double y, out double z)
        {
            x = Channel(r, srgbDecoded) * 2.0 - 1.0;
            y = Channel(g, srgbDecoded) * 2.0 - 1.0;
            z = Channel(b, srgbDecoded) * 2.0 - 1.0;
        }

        private static double Channel(byte v, bool srgbDecoded)
        {
            double c = v / 255.0;
            return srgbDecoded ? SrgbToLinear(c) : c;
        }

        /// <summary>
        /// Length of the unpacked vector. A tangent-space map stores unit vectors, so a correctly
        /// sampled map comes back at 1 to within its own 8-bit quantisation, and a length that is
        /// not 1 is proof the sampler did something to the numbers on the way.
        /// </summary>
        public static double Length(byte r, byte g, byte b, bool srgbDecoded)
        {
            Unpack(r, g, b, srgbDecoded, out double x, out double y, out double z);
            return System.Math.Sqrt(x * x + y * y + z * z);
        }

        /// <summary>
        /// Angle between the unpacked normal and the surface it is perturbing, in degrees.
        /// A neutral texel (128,128,255) reads ~0.3° correctly and ~40° after an sRGB decode: the
        /// map that says "this surface is flat" is instead tilting it most of half a right angle,
        /// and always toward −tangent/−bitangent, which is why the damage arrives as hard-edged
        /// per-UV-chart patches rather than as shading.
        /// </summary>
        public static double TiltDegrees(byte r, byte g, byte b, bool srgbDecoded)
        {
            Unpack(r, g, b, srgbDecoded, out double x, out double y, out double z);
            double len = System.Math.Sqrt(x * x + y * y + z * z);
            if (len < 1e-9) return 90.0;
            double cos = z / len;
            if (cos > 1.0) cos = 1.0;
            if (cos < -1.0) cos = -1.0;
            return System.Math.Acos(cos) * 180.0 / System.Math.PI;
        }

        /// <summary>Mean <see cref="TiltDegrees"/> over an RGB byte buffer (3 bytes per texel).</summary>
        public static double MeanTiltDegrees(byte[] rgb, bool srgbDecoded)
        {
            if (rgb == null || rgb.Length < 3) return 0.0;
            double sum = 0.0;
            int n = rgb.Length / 3;
            for (int i = 0; i < n; i++)
                sum += TiltDegrees(rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2], srgbDecoded);
            return sum / n;
        }

        /// <summary>Mean <see cref="Length"/> over an RGB byte buffer (3 bytes per texel).</summary>
        public static double MeanLength(byte[] rgb, bool srgbDecoded)
        {
            if (rgb == null || rgb.Length < 3) return 0.0;
            double sum = 0.0;
            int n = rgb.Length / 3;
            for (int i = 0; i < n; i++)
                sum += Length(rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2], srgbDecoded);
            return sum / n;
        }

        /// <summary>
        /// Mean DISTANCE of the unpacked length from 1, which is the honest version of the
        /// question <see cref="MeanLength"/> only looks like it answers.
        ///
        /// 🔴 Averaging the lengths themselves lets errors cancel: on a real bake an sRGB decode
        /// stretches some texels and shrinks others, and the mean came back at 1.03 in the first
        /// version of the test that used it — a badly broken map presenting as a nearly perfect
        /// one. Averaging the absolute error cannot cancel, and separates the two readings by two
        /// orders of magnitude.
        /// </summary>
        public static double MeanLengthError(byte[] rgb, bool srgbDecoded)
        {
            if (rgb == null || rgb.Length < 3) return 0.0;
            double sum = 0.0;
            int n = rgb.Length / 3;
            for (int i = 0; i < n; i++)
                sum += System.Math.Abs(
                    Length(rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2], srgbDecoded) - 1.0);
            return sum / n;
        }

        /// <summary>Angle between two vectors in degrees. 0 when they point the same way.</summary>
        public static double AngleDegrees(double ax, double ay, double az,
                                          double bx, double by, double bz)
        {
            double la = System.Math.Sqrt(ax * ax + ay * ay + az * az);
            double lb = System.Math.Sqrt(bx * bx + by * by + bz * bz);
            if (la < 1e-12 || lb < 1e-12) return 180.0;
            double cos = (ax * bx + ay * by + az * bz) / (la * lb);
            if (cos > 1.0) cos = 1.0;
            if (cos < -1.0) cos = -1.0;
            return System.Math.Acos(cos) * 180.0 / System.Math.PI;
        }

        /// <summary>
        /// How far a texel may land from the direction its author baked in before the READING is
        /// what is wrong rather than the map. 8-bit quantisation alone is worth a few tenths of a
        /// degree; five degrees is far above that and far below the tens of degrees an sRGB decode
        /// costs.
        ///
        /// 🔴 This is an error against the AUTHORED normal, not a tilt away from flat. A real bake
        /// tilts its texels by design — the fixture in the tests averages 36° off vertical and is
        /// perfectly correct — so "the average normal is close to straight up" is not a criterion,
        /// it is a description of a map with nothing in it.
        /// </summary>
        public const double MaxDecodeTiltErrorDegrees = 5.0;

        /// <summary>
        /// …and how far the mean unpacked LENGTH may sit from 1. Same reasoning: quantisation is
        /// worth ~0.002, an sRGB decode is worth ~0.3.
        /// </summary>
        public const double MaxLengthError = 0.05;

        /// <summary>
        /// Does this map read as a tangent-space normal map at all, sampled this way? Used by the
        /// tests, and the shape any future runtime probe should take: it asks the DATA, not the
        /// colour space, and it has a right answer that does not depend on this project.
        /// </summary>
        public static bool ReadsAsUnitNormals(byte[] rgb, bool srgbDecoded)
            => MeanLengthError(rgb, srgbDecoded) <= MaxLengthError;
    }
}
