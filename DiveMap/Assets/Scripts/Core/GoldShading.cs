using System;

namespace DiveMap.Core
{
    /// <summary>
    /// What "gold" means on a glTF metallic-roughness material, and — more importantly — WHICH
    /// SHADER PROPERTIES to write it to. Extracted from <c>Runtime/GoldFx.cs</c> for the same
    /// reason <see cref="GlbShading"/> was extracted from <c>SceneBuilder</c>: the part that was
    /// wrong was a list of strings, and a list of strings can be checked on this machine in two
    /// seconds instead of by a 35-minute CI round and a screenshot.
    ///
    /// 🔴 THE BUG THIS FILE EXISTS FOR. <c>ApplyGold</c> shipped writing <c>_EmissionColor</c>,
    /// <c>_Metallic</c> and <c>_Glossiness</c> — Unity Standard's names. Every GLB in this app is
    /// instantiated by glTFast onto <c>glTF/PbrMetallicRoughness</c>, whose built-in-RP variant
    /// (<c>Runtime/Shader/Built-In/glTFPbrMetallicRoughness.shader</c>, glTFast 6.19.0) declares:
    ///
    ///     baseColorFactor  baseColorTexture  alphaCutoff  roughnessFactor  metallicFactor
    ///     metallicRoughnessTexture  normalTexture(+_scale)  occlusionTexture(+_strength)
    ///     emissiveFactor  emissiveTexture   (+ _Mode/_SrcBlend/_DstBlend/_ZWrite/_CullMode)
    ///
    /// NOT ONE of the three names the old code used is in that list, so every write was guarded
    /// out by its own <c>HasProperty</c> check and the trident has never been gold. The only line
    /// it logged counted materials it had SEEN, not materials it had CHANGED, so the log said
    /// "gold on sw:golden_trident materials=3" for a pass that did nothing at all.
    ///
    /// (The URP/HDRP variants are a different set again — <c>_BaseColor</c>, <c>_Metallic</c>,
    /// <c>_Smoothness</c> on the shader-graph materials — which is exactly why the rule here is
    /// "try the candidates, take the first the material actually has" rather than "know the
    /// shader". Same shape as <c>SceneBuilder.MetalFactorNames</c> and <c>QcModelShot.PropOn</c>.)
    /// </summary>
    public static class GoldShading
    {
        // ── The property names, glTFast's first, then Unity Standard's / shader-graph's ───────
        // Order matters: a material that somehow has both must be driven through glTFast's,
        // because that is the one its shader actually reads.

        /// <summary>Base colour tint. On a metal this is F0 — the colour of the reflection.</summary>
        public static readonly string[] BaseColorNames = { "baseColorFactor", "_BaseColor", "_Color" };

        /// <summary>Metalness scalar.</summary>
        public static readonly string[] MetallicNames = { "metallicFactor", "_Metallic" };

        /// <summary>Roughness scalar — glTF's sense: 0 = mirror.</summary>
        public static readonly string[] RoughnessNames = { "roughnessFactor" };

        /// <summary>The INVERSE of roughness, which is what Unity's own shaders take.</summary>
        public static readonly string[] SmoothnessNames = { "_Glossiness", "_Smoothness" };

        /// <summary>Emissive colour. <c>[HDR]</c> on glTFast's shader — see <see cref="EmissiveLinearR"/>.</summary>
        public static readonly string[] EmissiveNames = { "emissiveFactor", "_EmissionColor" };

        /// <summary>Base-colour texture slot: it MULTIPLIES the factor, so it can eat the gold.</summary>
        public static readonly string[] BaseColorTextureNames = { "baseColorTexture", "_MainTex" };

        // ── The look ──────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Gold's base colour, LINEAR — the standard measured F0 of gold, ≈ #ffe39d once encoded
        /// to sRGB. It is deliberately not near-black: on a metallic-roughness workflow the base
        /// colour of a metal is not "how dark the object is", it is the tint of everything the
        /// object reflects, and a dark base colour on a metal is the recipe for the black-object
        /// bug this project already paid for once (see <see cref="GlbShading.TamedMetalFactor"/>).
        ///
        /// 🔴 Written to <c>baseColorFactor</c> in GAMMA, not linear. glTFast writes its own
        /// baseColorFactor as <c>baseColorLinear.gamma</c> (<c>BuiltInMaterialGenerator.cs:383</c>)
        /// because Unity converts plain <c>Color</c> shader properties sRGB→linear on upload in a
        /// linear project; <c>emissiveFactor</c> is <c>[HDR]</c> and skips that conversion, which
        /// is why glTFast sets IT linear and un-converted one line later (:387). Both halves of
        /// that asymmetry are copied here on purpose.
        /// </summary>
        public const float BaseColorLinearR = 1.000f;

        /// <inheritdoc cref="BaseColorLinearR"/>
        public const float BaseColorLinearG = 0.766f;

        /// <inheritdoc cref="BaseColorLinearR"/>
        public const float BaseColorLinearB = 0.336f;

        /// <summary>
        /// Gold is a metal, so 1. Not a hedge, and not subject to
        /// <see cref="GlbShading.TamedMetalFactor"/>: that rule exists to catch a SCANNER's
        /// leftover <c>metallicFactor = 1</c> against a metal map that reads 0.001 — an accident.
        /// This is an authored decision about a prop that is supposed to be solid metal, and it
        /// runs AFTER <c>TameMetal</c> in <c>SceneBuilder</c>, so it wins.
        ///
        /// 🔴 WHAT THIS WILL ACTUALLY LOOK LIKE, since it is worth knowing before someone reports
        /// it as a bug. A fully metallic surface has no diffuse term: all of its colour comes from
        /// the environment, tinted by <see cref="BaseColorLinearR"/>. <c>AppBoot</c> gives this
        /// scene a UNIFORM colour cube (0.60, 0.72, 0.82) at <c>reflectionIntensity = 0.3</c>, so
        /// the flat, no-highlight part of the trident settles at
        /// <see cref="MetalReflectionLinear"/> ≈ (0.18, 0.165, 0.083) linear ≈ #767151 in sRGB —
        /// a dull olive-gold, not a bright one, and identical from every angle because the cube
        /// has no features to reflect. The sun's specular lobe (roughness <see cref="Roughness"/>)
        /// supplies the only real highlight. That is why <see cref="EmissiveStrength"/> is not
        /// zero: the emissive is what makes it read as GOLD rather than as dark brass, and it is
        /// the only part of the look that does not depend on the reflection environment at all.
        /// </summary>
        public const float Metallic = 1f;

        /// <summary>Polished, not mirror: 0.25 keeps the sun's highlight a lobe rather than a dot.</summary>
        public const float Roughness = 0.25f;

        /// <summary>Unity's shaders take the complement. Same surface, other convention.</summary>
        public static float Smoothness => 1f - Roughness;

        /// <summary>The web's emissive gold #ffb733, decoded from sRGB to LINEAR.</summary>
        public const float EmissiveLinearR = 1.0000f;

        /// <inheritdoc cref="EmissiveLinearR"/>
        public const float EmissiveLinearG = 0.4735f;

        /// <inheritdoc cref="EmissiveLinearR"/>
        public const float EmissiveLinearB = 0.0331f;

        /// <summary>
        /// How much of that emissive gold to add. 0.18 is chosen against the number above: the
        /// reflection the metal gets from the scene's cube is ≈0.18 linear in red, so this roughly
        /// doubles the brightest channel of a statue standing in open water and does more than
        /// that for one in shadow — visible as "glowing", nowhere near blowing out the tone curve.
        /// The old code's 0.5 was written when this value was believed to be gamma-encoded; on an
        /// <c>[HDR]</c> property 0.5 LINEAR is ≈0.74 sRGB of pure emission and would flatten the
        /// model into a silhouette-less blob.
        /// </summary>
        public const float EmissiveStrength = 0.18f;

        /// <summary>
        /// What a fully metallic surface of base colour <paramref name="baseColorLinear"/> returns
        /// when the only thing to reflect is a uniform cube — this scene's case exactly. Written
        /// down as arithmetic so the claim in <see cref="Metallic"/> is asserted by a test rather
        /// than believed.
        /// </summary>
        public static float MetalReflectionLinear(float baseColorLinear, float cubeLinear, float reflectionIntensity)
            => baseColorLinear * cubeLinear * reflectionIntensity;

        /// <summary>First of <paramref name="candidates"/> that <paramref name="has"/> accepts, or null.</summary>
        public static string FirstPresent(string[] candidates, Func<string, bool> has)
        {
            if (candidates == null || has == null) return null;
            foreach (string name in candidates)
                if (has(name)) return name;
            return null;
        }

        /// <summary>
        /// The per-material log line, built here so it is covered by a test.
        ///
        /// 🔴 It names the properties that were MISSING as well as the ones that were found, and
        /// it is emitted whether or not anything was applied. This is the rule the rest of the
        /// shading code already follows (<c>SceneBuilder.DropMisdecodedNormalMap</c>): silence, or
        /// a line that only counts what it looked at, cannot tell "it worked" from "it skipped
        /// everything" from "it never ran" — and that ambiguity is precisely how a no-op survived
        /// in this file for as long as it did.
        /// </summary>
        public static string Report(string colorProp, string metalProp, string glossProp, string emissiveProp)
        {
            string missing = "";
            if (colorProp == null) missing += "baseColor,";
            if (metalProp == null) missing += "metallic,";
            if (glossProp == null) missing += "roughness,";
            if (emissiveProp == null) missing += "emissive,";

            // "applied" is about this MATERIAL, not about this property: one channel that landed
            // is still a material that changed, and four that did not is the no-op above.
            bool applied = colorProp != null || metalProp != null || glossProp != null || emissiveProp != null;

            return "color=" + (colorProp ?? "-") +
                   " metal=" + (metalProp ?? "-") +
                   " rough=" + (glossProp ?? "-") +
                   " emissive=" + (emissiveProp ?? "-") +
                   " missing=" + (missing.Length == 0 ? "-" : missing.TrimEnd(',')) +
                   " applied=" + (applied ? "t" : "f");
        }
    }
}
