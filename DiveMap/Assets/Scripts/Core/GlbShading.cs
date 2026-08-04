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

        // ── WO-E5m: the half TameMetal was missing, found by measuring instead of reasoning ──
        //
        // 🔴 THE MEASUREMENT, not a theory. A probe set `metallicFactor = 0` on the loaded materials
        // and changed NOTHING else — same shader, same albedo, same textures, same keywords — and
        // photographed the same models from the same camera:
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
        // 🔎 The route was NOT established and the fix does not depend on it. `metal = tex.b ×
        // metallicFactor` is only as good as `tex.b`, and tex.b arrives through an ETC1S transcode
        // that compresses R, G and B together — with roughness sitting at ~0.66 in G, there is a
        // channel right next to the metal one carrying two thirds of full scale. Whether that is
        // what happens on the device is a guess; the frames are the evidence.
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
            if (!hasMetalTexture) return -1f;                           // TamedMetalFactor's case
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

        // ── WO-animal-mat: the animals were shaded from HALF of glTF's arithmetic ───────────────
        //
        // 🔴 THE BUG, in one line of maths. glTF says
        //
        //       roughness = roughnessFactor × map.g        metal = metallicFactor × map.b
        //
        // and <c>WhaleController.CopyMaps</c> was carrying only the FACTORS onto the swim shader,
        // because that shader could not read a map in glTF's channel layout. Every marine model in
        // the catalogue leaves <c>roughnessFactor</c> at glTF's default of 1 and puts the real value
        // in the map, so "1 − roughnessFactor" evaluated to 1 − 1 = 0: smoothness ZERO, a dead-matte
        // chalk finish, on every animal that swims. The static wrecks never had this because they
        // keep glTFast's own PBR shader, which reads the map — hence "the fish look wrong and the
        // wreck next to them looks right".
        //
        // 🔴 MEASURED, on the shipped CDN files (webcmp_probe.mjs, UV-surface-area-weighted mean of
        // the metallic-roughness map, 0-255):
        //
        //     model                     metallicFactor  roughnessFactor   map.g   map.b
        //     mdl_great_white_shark_xr0        1               1          128.0     0.0
        //     mdl_whitetip_shark_xr0           1               1          115.8   130.3
        //     mdl_bull_shark_xr0               1               1          118.1     0.0
        //     msh_manta_xr0                    1               1          130.2     1.2
        //     msh_beluga_whale_xr0             1               1          148.3     0.0
        //     msh_leopard_shark_xr0            1               1          148.2     0.0
        //
        // Six models, every factor at the default, every real value in the map. The fix is to sample
        // the map — DM_FishWaveDetail gained <c>_MetalRoughMap</c> in glTF layout — and these two
        // functions are that shader's arithmetic, written where a test can reach it.
        //
        // 🔎 whitetip is the one genuine metal in the set and the reason a blanket zero is wrong:
        // 63.7% of its map is empty UV gutter, and of the texels that are actually on the shark,
        // essentially all of them sit at b ≈ 0.4-0.6. That is an authored constant, not transcode
        // noise — great white's b is 0.0 over the same surface.

        /// <summary>
        /// <c>o.Smoothness</c> as DM_FishWaveDetail computes it: <c>1 − (1 − _Glossiness) × map.g</c>.
        /// With a white map (g = 1) this is <c>_Glossiness</c>, so a material that ships no
        /// metallic-roughness texture shades exactly as it did before the map existed.
        /// </summary>
        /// <param name="glossFactor">_Glossiness, i.e. 1 − roughnessFactor.</param>
        /// <param name="mapRoughG">The map's GREEN channel as the shader receives it, 0-1.</param>
        public static double ShaderSmoothness(double glossFactor, double mapRoughG)
            => 1.0 - (1.0 - glossFactor) * mapRoughG;

        /// <summary>
        /// <c>o.Metallic</c> as DM_FishWaveDetail computes it: <c>_Metallic × map.b</c> — glTF's
        /// <c>metallicFactor × map.b</c> exactly.
        /// </summary>
        /// <param name="metalFactor">_Metallic, from <see cref="WaveMetalFactor"/>.</param>
        /// <param name="mapMetalB">The map's BLUE channel as the shader receives it, 0-1.</param>
        public static double ShaderMetallic(double metalFactor, double mapMetalB)
            => metalFactor * mapMetalB;

        /// <summary>
        /// What <c>_Metallic</c> should become when a glTF material is copied onto the swim shader,
        /// now that the swim shader CAN read the map.
        ///
        /// 🔴 Why this is not <see cref="CopiedMetalFactor"/> and not the source's own factor either.
        /// <see cref="MappedMetalFactor"/> has already overwritten <c>metallicFactor</c> with 0 on
        /// this material — <c>SceneBuilder.TameMetal</c> runs before <c>AttachWhale</c>, by design —
        /// and that 0 is a STAND-IN for a map that the shader of the day could not read. Copy the
        /// stand-in onto a shader that reads the map and the map is multiplied by zero: whitetip's
        /// authored metal disappears, and every other animal gets the same answer twice.
        ///
        /// So where a glTF-layout map is present the factor is 1 — glTF's own default, the value the
        /// file actually declares — and the map speaks. Where there is none, the factor IS the whole
        /// material and it comes across as it stands, already tamed by TameMetal upstream.
        /// </summary>
        /// <param name="metallicFactor">The source material's factor, post-TameMetal.</param>
        /// <param name="hasGltfLayoutMap">
        /// Is a map in glTF's OWN layout (G = rough, B = metal) being handed to the shader? A Unity
        /// <c>_MetallicGlossMap</c> does NOT count: its metal is in R and its smoothness in A, so it
        /// is never bound to <c>_MetalRoughMap</c> and must not license a factor of 1 either.
        /// </param>
        public static float WaveMetalFactor(float metallicFactor, bool hasGltfLayoutMap)
            => hasGltfLayoutMap ? 1f : Clamp01(metallicFactor);

        /// <summary>
        /// What <c>_Glossiness</c> should become: <c>1 − roughnessFactor</c>, unchanged — but it is
        /// only correct now that <see cref="ShaderSmoothness"/> multiplies the map back in. On its
        /// own it reads 0 on every model in this catalogue, which was the bug.
        /// </summary>
        public static float WaveGlossFactor(float roughnessFactor) => Clamp01(1f - roughnessFactor);

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

        /// <summary>At or below this a factor is already harmless.</summary>
        public const float MetalFactorFloor = 0.05f;

        /// <summary>Not zero: 0.06 keeps a wet sheen so a rock still reads as underwater.</summary>
        public const float TamedMetal = 0.06f;
    }
}
