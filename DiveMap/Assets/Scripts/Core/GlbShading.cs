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
        /// Metallic and roughness for the QC pass's textureless probe frames.
        ///
        /// 🔴 These were briefly a FIX. Between runs 30753720407 and 30765284038 the import
        /// replaced every metallic-roughness map with this pair, because a probe frame measured it
        /// taking blackOfSubject from 10.92/13.45/6.43/5.17% to 0.00% on all four affected models.
        /// It was the right call on the evidence available and the wrong diagnosis: the real cause
        /// was the UV atlas's undilated black gutters being averaged into the deep mips a chart
        /// seam throws the sampler into (see <c>SceneBuilder.TameMetal</c>). Clearing the map
        /// helped only because it stopped one more texture being sampled at the exploded LOD.
        /// Source-side dilation fixed it properly, the maps are back, and these two numbers stay
        /// because the probe frames still need something to shade with — 0 metal is what the maps
        /// measure (0.0004-0.028 across ten models) and 0.6 roughness sits beside their ~0.53 mean.
        /// </summary>
        public const float ProbeValidatedMetallic = 0f;

        /// <inheritdoc cref="ProbeValidatedMetallic"/>
        public const float ProbeValidatedRoughness = 0.6f;

        // ── WO-E5e: the normal maps were destroyed BEFORE the app ever saw them ──
        //
        // 🔴 THE SAME BUG CAME BACK THROUGH A DIFFERENT DOOR. Everything above this line is about a
        // normal map being sRGB-decoded by a sampler in a gamma project, and the fix was to move
        // the project to linear — which worked, and which turned every normal map in the app back
        // ON (<c>[Shading] … dropped=f</c>). What came back on is not a normal map.
        //
        // THE MEASUREMENT, on the very files the CDN serves against the 4096² masters they were
        // built from. ETC1 — Unity reports the device format as <c>RGB_ETC_UNorm</c> — splits every
        // 4×4 into sub-blocks that share ONE base colour plus a per-pixel LUMINANCE modifier added
        // equally to R, G and B. Inside a sub-block R and G can therefore only move TOGETHER. A
        // tangent-space normal map puts x in R and y in G and they must move INDEPENDENTLY, so the
        // share of the local variation lying along (1, −1) is precisely what the format cannot
        // represent. Measured as a fraction of total local variance:
        //
        //                              master PNG 4096²      shipped KTX2 (ETC1S)
        //     domed_temple                 49.96 %                  0.01 %
        //     grand_byzantine              50.11 %                  0.49 %
        //     rms lost, bytes           11.74 / 15.55            0.15 / 1.40
        //
        // Half of every master normal map is independent R/G movement, which is what a normal map
        // IS. Essentially none of it survives to the device. What is left is the component where R
        // and G rise and fall together — i.e. x ≈ y at every texel — so every perturbed normal
        // tilts along the SAME tangent-space diagonal instead of in the direction the surface
        // actually turns.
        //
        // 🔎 And that is why the damage arrives as hard-edged patches rather than as softness,
        // which is the observation this file already explains once for the gamma bug: tangent
        // direction follows the UV chart, so one shared diagonal tilt turns a different way in
        // every chart. The user's daylight screenshot is bimodal for exactly that reason —
        // 68.7 % of the dome's body under byte 16 and 15.7 % at sand level, with 0.62 % in the
        // whole range between.
        //
        // 🔴 IT IS NOT A RUIN PROBLEM. Run 30800189252 reports darkOfSubject 65-95 % across the
        // entire catalogue — poseidon 95.16, htms732 76.08, chang 70.98, kraken 65.38 — because
        // every texture in the app goes through the same toktx ETC1S pass. "ทุกวัตถุ" is right.
        //
        // 🔎 WHAT THIS IS NOT. It is not something a clamp can fix. Spherical harmonics summing
        // below zero writes EXACTLY (0,0,0), so it can only ever account for
        // <c>blackOfSubject</c> — 0.12-33 % — and the number that has to move is
        // <c>darkOfSubject</c>, which is a level, not a clamp. Clamping the ambient would also
        // need a custom shader, and this project has twice shipped one that was stripped from the
        // player build and rendered magenta.

        /// <summary>
        /// Fraction of a tangent-space normal map's local variation that a luminance-modifier block
        /// format such as ETC1 physically cannot store: the part where x rises while y falls.
        ///
        /// Stated as arithmetic rather than as a paragraph so the claim is checkable. For a map
        /// whose x and y are INDEPENDENT — which is the definition of a usable tangent-space normal
        /// map — the two projections carry equal energy and this returns 0.5.
        /// </summary>
        /// <param name="sharedVariance">Energy along (1, 1) — what the format keeps.</param>
        /// <param name="independentVariance">Energy along (1, −1) — what it discards.</param>
        public static double LostToLumaBlockFormat(double sharedVariance, double independentVariance)
        {
            double total = sharedVariance + independentVariance;
            return total <= 0.0 ? 0.0 : independentVariance / total;
        }

        /// <summary>
        /// What a healthy tangent-space normal map measures on
        /// <see cref="LostToLumaBlockFormat"/> — half, because x and y are independent axes.
        /// The masters measure 0.4996 and 0.5011.
        /// </summary>
        public const double HealthyIndependentFraction = 0.5;

        /// <summary>
        /// Below this, a normal map has been flattened onto one diagonal and is no longer a normal
        /// map, whatever its file extension says. The shipped ETC1S files measure 0.0001 and 0.0049
        /// against masters at 0.4996 and 0.5011, so anything in between is already catastrophic;
        /// a tenth of healthy is a generous line and still fails every file in the catalogue today.
        /// </summary>
        public const double MinIndependentFraction = 0.05;

        /// <summary>Has this normal map survived its encoding?</summary>
        public static bool NormalMapSurvivedEncoding(double sharedVariance, double independentVariance)
            => LostToLumaBlockFormat(sharedVariance, independentVariance) >= MinIndependentFraction;

        /// <summary>At or below this a factor is already harmless.</summary>
        public const float MetalFactorFloor = 0.05f;

        /// <summary>Not zero: 0.06 keeps a wet sheen so a rock still reads as underwater.</summary>
        public const float TamedMetal = 0.06f;
    }
}
