namespace DiveMap.Core
{
    /// <summary>
    /// Why a KTX2 glTF model renders with black patches on this project, expressed as arithmetic
    /// so it is asserted by a test instead of described in a comment.
    ///
    /// 🔴 THE BUG, measured. The QC model pass photographs six CDN models through the app's own
    /// loader. <c>cc0_kraken_xr0</c> came back <c>blackOfSubject=10.04%</c> and
    /// <c>stat_verdant_poseidon_xr0</c> <c>12.46%</c> — hard-edged black polygons all over both.
    /// Everything the GLB itself could be blamed for was measured on the file and cleared:
    ///
    ///   • geometry — 0 NaN normals, 0 zero-length tangents, tangent w is exactly ±1, 0 tangents
    ///     parallel to their normal, 0 degenerate triangles (68,226 verts / 41,794 tris);
    ///   • metal — <c>metallicFactor</c> defaults to 1 but the metallic-roughness map's BLUE
    ///     channel averages 0.001, so the surface is a dielectric and the reflection-cube story
    ///     that <see cref="P:UnityEngine.RenderSettings.customReflectionTexture"/> exists for does
    ///     not apply here;
    ///   • base colour — the atlas is 24% pure-black gutter, but rasterising the real UVs at the
    ///     QC camera's real framing, sampling the KTX2's own 12-level mip chain trilinearly, put
    ///     0.00% of the model's screen area on a black texel (the wreck, which does NOT go black
    ///     in the app, scores worst at 0.10% — the wrong way round);
    ///   • back faces — 0.04% of the model's pixels show one, and glTFast's built-in-RP shader
    ///     flips the normal for them anyway (<c>fragBaseFacing</c>, VFACE).
    ///
    /// What is left is the NORMAL MAP, and the number that convicts it is the shading curve. Bin
    /// the model's pixels by how much light the MESH normal should catch and compare the app's
    /// own screenshot with an offline render of the same GLB at the same camera:
    ///
    ///   geometric N·L bin        0.00   0.15   0.30   0.45   0.60   0.75
    ///   app (Unity build)         100    107    102     97     93     90
    ///   offline, map decoded OK    58     81     98    111    141    179
    ///   offline, map read as sRGB  97    110    114    109    103    110
    ///
    /// The app is FLAT — its shading has come loose from the mesh — and only the sRGB reading
    /// reproduces that. It is not subtle: this project runs in GAMMA colour space
    /// (<c>ProjectSettings m_ActiveColorSpace: 0</c>), and glTFast only works out which textures
    /// are colour and which are data when the project is LINEAR (<c>GltfImport.cs:1674</c>:
    /// <c>if (QualitySettings.activeColorSpace == ColorSpace.Linear)</c>). In gamma the whole
    /// array stays null, <c>forceSampleLinear</c> is false for every texture, KtxUnity therefore
    /// never sets its <c>Linear</c> feature bit, and the transcode target comes out as an sRGB
    /// GPU format. A plain <c>Texture2D</c> would shrug that off — gamma projects do no
    /// conversion — but a KTX2 arrives through <c>Texture2D.CreateExternalTexture</c> wrapping a
    /// texture libktx already uploaded with an sRGB internal format, so the hardware decodes on
    /// every sample no matter what Unity thinks the colour space is.
    ///
    /// The neutral normal 128 is then not 0.5 but <see cref="SrgbToLinear"/>(0.502) = 0.216, and
    /// every texel of a perfectly good tangent-space map tilts <see cref="NeutralTiltDegrees"/> ≈
    /// 53° toward −tangent/−bitangent. Tangent direction follows the UV atlas, so the tilt turns a
    /// different way in every chart — which is exactly why the black arrives as hard-edged
    /// polygons rather than as shading. Where it swings a surface past both lights at once the
    /// forward pass has only ambient left, and Unity's split spherical harmonics (L2 from the
    /// vertex normal, unclamped; L0/L1 from this ruined pixel normal) can sum below zero, which
    /// the frame buffer writes as exactly (0,0,0).
    ///
    /// A flat wreck hull barely notices a 53° tilt — it still faces the sun. A kraken made of
    /// curled tentacles crosses the terminator every few pixels. Hence 10-12% against 4.6%.
    /// </summary>
    public static class GlbShading
    {
        /// <summary>The sRGB EOTF the GPU applies when a texture is uploaded with an sRGB format.
        /// Values outside 0..1 are clamped: this models a hardware sampler, not a formula.</summary>
        public static double SrgbToLinear(double srgb)
        {
            if (srgb <= 0.0) return 0.0;
            if (srgb >= 1.0) return 1.0;
            return srgb <= 0.04045
                ? srgb / 12.92
                : System.Math.Pow((srgb + 0.055) / 1.055, 2.4);
        }

        /// <summary>The byte a tangent-space normal map stores for "no perturbation".</summary>
        public const int NeutralNormalByte = 128;

        /// <summary>
        /// How far off vertical a neutral normal-map texel lands once the sampler has applied an
        /// sRGB decode it should not have applied. Unity's built-in unpack takes x and y from the
        /// red and green channels (<c>xy = rg * 2 − 1</c>) and rebuilds z, so a channel that
        /// should read 0.502 and reads 0.216 instead drags BOTH axes negative by the same amount.
        /// </summary>
        /// <returns>Degrees between the decoded normal and (0,0,1). ≈0 when nothing is wrong.</returns>
        public static double NeutralTiltDegrees()
        {
            double stored = NeutralNormalByte / 255.0;
            double x = SrgbToLinear(stored) * 2.0 - 1.0;
            double y = x;
            double zz = 1.0 - (x * x + y * y);
            if (zz < 0.0) zz = 0.0;
            double z = System.Math.Sqrt(zz);
            return System.Math.Acos(z) * 180.0 / System.Math.PI;
        }

        /// <summary>
        /// Is this material's normal map going to hand the shader garbage?
        ///
        /// 🔴 THIS USED TO ASK THE TEXTURE, AND THE TEXTURE LIES. The first version took a second
        /// argument — is this texture's GPU format an sRGB one — on the reasoning that only the
        /// KTX2 imports are affected and the format would say so. CI run 30747457729 shipped that
        /// version and NOTHING was dropped: blackOfSubject went 10.04 → 10.92 (kraken), 12.46 →
        /// 13.24 (statue). The reason is structural and cannot be worked around from C#.
        /// KtxUnity hands Unity a texture that already exists on the GPU —
        /// <c>Texture2D.CreateExternalTexture(w, h, textureFormat, mipChain, linear, nativePtr)</c>
        /// in <c>KtxNativeInstance.cs:117-126</c> — so the sRGB-ness that matters is baked into the
        /// GL object libktx uploaded, while <c>Texture2D.graphicsFormat</c> on the managed side is
        /// something Unity re-derives from <c>textureFormat</c> + <c>linear</c> + the project's
        /// colour space. In a gamma project that derivation drops the sRGB variant, so the C# API
        /// reports a UNorm format for a texture the hardware is decoding as sRGB. There is no
        /// property to ask; do not re-add the check.
        ///
        /// What is left is the one fact that IS knowable, and it is the fact that causes the bug:
        /// the colour space. In gamma, glTFast never marks any glTF texture as data
        /// (<c>GltfImport.cs:1674</c>) so every KTX2 in the app transcodes to an sRGB target — and
        /// every texture in this app's GLBs is a KTX2, because <c>tools/optimize_xr.mjs</c> puts
        /// them all through toktx. Switch the project to linear and this turns itself off rather
        /// than becoming a permanent loss of surface detail.
        ///
        /// The trade-off, stated plainly: a glTF whose normal map is NOT a KTX2 would be sampled
        /// correctly and is now having a good map thrown away. None exist in the XR pipeline today,
        /// and half a right angle of error on every texel of every model that does is the thing
        /// worth avoiding.
        /// </summary>
        /// <param name="gammaColorSpace">QualitySettings.activeColorSpace == Gamma.</param>
        public static bool NormalMapIsMisdecoded(bool gammaColorSpace) => gammaColorSpace;

        /// <summary>
        /// What <c>metallicFactor</c> should become. Unchanged rule, moved here so it is covered:
        /// a factor above <see cref="MetalFactorFloor"/> with NO metallic-roughness texture is
        /// what a scanner writes when it has no opinion, and against this scene's bright
        /// reflection cube it renders as a black object with a white rim. A material that ships
        /// the texture has been authored and is left alone — its blue channel is the metal, and on
        /// every model measured here that channel is 0.001.
        /// </summary>
        /// <returns>The factor to write, or a negative number for "leave it alone".</returns>
        public static float TamedMetalFactor(float metallicFactor, bool hasMetalTexture)
        {
            if (hasMetalTexture) return -1f;
            if (metallicFactor <= MetalFactorFloor) return -1f;
            return TamedMetal;
        }

        /// <summary>
        /// What <c>_Metallic</c> should become when a glTF material is COPIED onto a shader that
        /// cannot read glTF's packed metallic-roughness texture — the swim-wave material the big
        /// animals get.
        ///
        /// 🔴 The factor on its own is not an approximation of the material, it is half of a
        /// multiplication. Every XR model measured here ships <c>metallicFactor = 1</c> against a
        /// texture whose metal channel reads 0.001, so the surface is a dielectric; carry the 1
        /// across without the texture and the animal becomes a mirror, and a mirror in a scene
        /// with one small reflection cube is black. Where there is no texture the factor IS the
        /// material and copying it is right — subject to the same taming
        /// <see cref="TamedMetalFactor"/> applies, because a scanner's leftover 0.4 is no more
        /// authored on this shader than on the original.
        /// </summary>
        public static float CopiedMetalFactor(float metallicFactor, bool sourceHadMetalTexture)
        {
            if (sourceHadMetalTexture) return TamedMetal;
            float tamed = TamedMetalFactor(metallicFactor, hasMetalTexture: false);
            return tamed >= 0f ? tamed : Clamp01(metallicFactor);
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        /// <summary>
        /// Should this material's metallic-roughness TEXTURE be replaced by scalars?
        ///
        /// 🔴 This one is not deduced, it is measured — CI run 30753720407, the A/B probe pass.
        /// The harness photographed each model three times: as shipped, with the base-colour
        /// texture cleared, and with the metallic-roughness texture cleared. <c>blackOfSubject</c>:
        ///
        ///   model      as shipped   white albedo   no metal-rough
        ///   kraken       10.92%        0.00%          0.00%
        ///   poseidon     13.45%        0.00%          0.00%
        ///   hardeep       6.43%        0.00%          0.00%
        ///   barracuda     5.17%        0.00%          0.00%
        ///   htms732       0.00%        0.00%          0.00%
        ///   lionfish      0.00%        0.00%          0.00%
        ///
        /// The probe's replacement shading is numerically almost the same material: the map's metal
        /// channel measures 0.0004-0.028 across ten models (it is a dielectric everywhere) and its
        /// roughness averages ~0.53 against the probe's flat 0.6. Nothing about the LOOK changed,
        /// and every black pixel went away. What separates the four that break from the two that do
        /// not is how much the roughness channel MOVES: standard deviation 0.175-0.269 on the
        /// broken models against 0.023 on htms732, whose map is effectively a constant — and
        /// <c>_METALLICGLOSSMAP</c> is a shader_feature that forks the whole vertex output struct
        /// and the fragment entry point (<c>glTFUnityStandardCore.cginc:168</c> passes an extra
        /// interpolator that the variant without it does not have).
        ///
        /// 🔴 What this is NOT: an explanation. The mechanism inside that variant is still unknown,
        /// and this deliberately does not pretend otherwise — three rounds have been lost to
        /// mechanisms that sounded right. What it is: the exact material state CI has already
        /// photographed producing zero black, applied at import instead of in the probe. The probe
        /// stays in the pass; the next run must come back <c>verdict=no-black</c> on all six, and
        /// if it does not, this is wrong and reverting it is one line.
        ///
        /// Cost, stated plainly: models lose per-texel roughness variation and render at a uniform
        /// <see cref="ProbeValidatedRoughness"/>. On a scan whose map is nearly flat anyway that is
        /// invisible; on the shiniest model measured (barracuda, mean roughness 0.145) it will read
        /// slightly more matte. Against 5-13% of the model rendering pure black, that is the trade
        /// worth making until the variant bug itself is found.
        /// </summary>
        public static bool ReplaceMetalRoughTextureWithScalars(bool hasMetalRoughTexture)
            => hasMetalRoughTexture;

        /// <summary>Metallic the probe frame used. The maps measure 0.0004-0.028 — dielectric.</summary>
        public const float ProbeValidatedMetallic = 0f;

        /// <summary>Roughness the probe frame used, against a measured map average of ~0.53.</summary>
        public const float ProbeValidatedRoughness = 0.6f;

        /// <summary>At or below this a factor is already harmless.</summary>
        public const float MetalFactorFloor = 0.05f;

        /// <summary>Not zero: 0.06 keeps a wet sheen so a rock still reads as underwater.</summary>
        public const float TamedMetal = 0.06f;
    }
}
