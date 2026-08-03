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

        // ── WO-E5e, RETRACTED. Kept because the mistake is worth more than the claim was ──
        //
        // 🔴 THE CLAIM: "ETC1S destroyed the normal maps — 49.96% of the master's independent x/y
        // movement survives as 0.01% on the device." It was wrong, and it was wrong in a way that
        // is easy to repeat, so the correction is written down instead of deleted.
        //
        // The measurement was real. The INFERENCE from it was not. Independent x/y energy is a
        // function of the NEIGHBOURHOOD it is measured over, and the first version measured it over
        // one 4×4 ETC1 block and then quoted the answer as if it described the whole map. Measured
        // properly — same definition, same files, window swept:
        //
        //     window        2×2     4×4     8×8    16×16   64×64   whole
        //     master       49.96   50.02   49.89   49.87   49.79   49.70    (4096² PNG)
        //     shipped       0.02    0.01   31.29   38.27   41.73   42.21    (2048² ETC1S)
        //     kraken        0.07    0.05   31.08   37.53   40.74   41.22
        //
        // A real normal map is scale-invariant: the master reads ~50% at every window, because x
        // and y are independent axes at every spatial frequency. ETC1S flattens x onto y INSIDE a
        // block — that part of the first measurement was correct and is what 0.01% describes — but
        // each block keeps its own base colour, so everything coarser than 4×4 survives. 42.21 of
        // the master's 49.70 is 85%. The loss is real, it is high-frequency detail, and it is about
        // 15%. It is not 4,000×.
        //
        // Two facts that should have stopped the claim before it was written:
        //   • corr(R, G) on the shipped map is 0.164 (master 0.006). Had x collapsed onto y it
        //     would be ~1.0. Sample texels: (146,105,253) (233,125,183) (171,72,229) — plainly
        //     independent channels.
        //   • THE WEB SHIPS ETC1S NORMAL MAPS TOO, at 512², and scores WORSE on this very metric
        //     (kraken 0.2995, singha 0.3333, chang 0.3058, htms732 0.2724 against our 0.39-0.42) —
        //     while being the thing the user holds up as looking right. A mechanism that is
        //     stronger on the good picture than on the bad one is not the cause of the difference.
        //
        // 🔎 The lesson, stated so the next person gets it for free: a statistic computed over a
        // window is a statement about that window. Before quoting one as a property of a texture,
        // sweep the window and check it is flat — the master is, and that is what made it a fair
        // number to quote; the shipped file is not, and that is what made it an unfair one.
        //
        // No gate is derived from any of this. There was one — a "normal map survived its encoding"
        // threshold — and it was removed rather than re-tuned, because the quantity it screened on
        // does not separate the pictures that look right from the ones that do not.

        // ── WO-E5m: the half TameMetal was missing, found by measuring instead of reasoning ──
        //
        // 🔴 THE MEASUREMENT, not the theory. Run 30821xxxxx set `metallicFactor = 0` on the loaded
        // materials and changed NOTHING else — same shader, same albedo, same textures, same
        // keywords — and photographed the same models from the same camera:
        //
        //                       darkOfSubject          blackOfSubject
        //     kraken            65.39% → 8.42%          0.64% → 0.01%
        //     singha            74.71% → 31.26%         3.47% → 0.00%
        //     domed_temple      84.76% → 54.61%        33.40% → 8.94%
        //     ancient_byzantine 87.17% → 49.41%        26.31% → 11.41%
        //     hardeep           69.88% → 37.00%         1.81% → 0.45%
        //     poseidon          95.22% → 62.04%         1.37% → 0.00%
        //
        // Seven models, one float, and the roughness twin of the same probe moved almost nothing
        // (65→63, 85→77). This is the metal factor and it is not close.
        //
        // 🔎 An earlier version of this block blamed the shader's `#else` branch — the case where
        // `_METALLICGLOSSMAP` is stripped and `metallicFactor` becomes the whole metal. That was
        // wrong: the app's own [Metal] line reports the keyword ON for every material carrying a
        // map. The texture IS being sampled. The factor still ruins the surface, because
        // `metal = tex.b × metallicFactor` is only as good as `tex.b`, and tex.b arrives through an
        // ETC1S transcode that compresses R, G and B together — with roughness sitting at ~0.66 in
        // G, there is a channel right next to the metal one carrying two thirds of full scale.
        // Whether that is the exact route was NOT established, and the fix does not depend on it:
        // the frames say the factor is the problem, and the frames are the evidence.
        //
        // 🔎 WHY 1.0 IS THERE AT ALL, and why zeroing it is not a loss. 1.0 is glTF's DEFAULT
        // metallicFactor — a file that never writes the field gets it. Every one of these files
        // pairs it with a metallic-roughness texture whose blue channel measures 0-1 out of 255.
        // The authored intent is therefore "the metal comes from the map, and the map says none".
        // Setting the factor to 0 delivers exactly that intent, and it delivers it even when the
        // map's blue channel does not survive the trip to the device.

        /// <summary>
        /// What <c>metallicFactor</c> should become on a material that ships a metallic-roughness
        /// TEXTURE — the case <see cref="TamedMetalFactor"/> deliberately leaves alone.
        ///
        /// 🔴 The two rules are complements, and the split is the same one either way: a factor is
        /// only meaningful if something authored it. <see cref="TamedMetalFactor"/> handles "a high
        /// factor and NO map" — a scanner's leftover. This handles "a high factor and a map", where
        /// the map is the authored value and the factor is glTF's default sitting on top of it.
        /// Neither rule touches a material the other one does; a test pins that.
        ///
        /// The golden trident is outside both by the same property that has always protected it: it
        /// sets <c>metallicFactor = 1</c> with NO metallic-roughness texture, so it is a deliberate
        /// metal and stays a mirror.
        /// </summary>
        /// <returns>The factor to write, or a negative number for "leave it alone".</returns>
        public static float MappedMetalFactor(float metallicFactor, bool hasMetalTexture)
        {
            if (!hasMetalTexture) return -1f;              // TamedMetalFactor's case, not this one
            if (metallicFactor < DefaultMetalFactorFloor) return -1f;   // somebody chose this number
            return 0f;
        }

        /// <summary>
        /// At or above this, a <c>metallicFactor</c> sitting on top of a metallic-roughness map is
        /// glTF's default rather than a decision. 0.9 rather than exactly 1.0 so that an exporter
        /// writing 0.99, or a float that has been through a quantise, is still recognised — and far
        /// enough above the 0.4 a scanner leaves behind that a genuine mid-metal is never caught.
        /// </summary>
        public const float DefaultMetalFactorFloor = 0.9f;

        /// <summary>At or below this a factor is already harmless.</summary>
        public const float MetalFactorFloor = 0.05f;

        /// <summary>Not zero: 0.06 keeps a wet sheen so a rock still reads as underwater.</summary>
        public const float TamedMetal = 0.06f;
    }
}
