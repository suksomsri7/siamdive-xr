using System;
using System.Collections.Generic;
using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// The gold FX, pinned to the shader it actually runs on.
    ///
    /// 🔴 The bug these tests exist for was not arithmetic, it was three STRINGS: <c>ApplyGold</c>
    /// wrote <c>_EmissionColor</c> / <c>_Metallic</c> / <c>_Glossiness</c> onto materials whose
    /// shader is glTFast's <c>glTF/PbrMetallicRoughness</c>, and every write was guarded out by its
    /// own <c>HasProperty</c>. Nothing in the project could see it: there is no Unity here to make
    /// a Material with, the log line counted materials it had looked at rather than materials it
    /// had changed, and the only other witness was a 35-minute CI round photographing a trident
    /// that has always been grey.
    ///
    /// So <see cref="GltfBuiltInRpProperties"/> is that shader's Properties block, copied verbatim
    /// from <c>glTFast 6.19.0 Runtime/Shader/Built-In/glTFPbrMetallicRoughness.shader</c>, and the
    /// tests below ask the real candidate lists whether they can find anything in it. The old
    /// names are kept, as a NEGATIVE control, so the regression cannot come back quietly.
    /// </summary>
    public class GoldShadingTests
    {
        /// <summary>
        /// Every property glTFast's built-in-RP metallic-roughness shader declares. Note what is
        /// NOT here: <c>_EmissionColor</c>, <c>_Metallic</c>, <c>_Glossiness</c>, <c>_Color</c>,
        /// <c>_MainTex</c> — the Standard-shader names. The URP/HDRP shader-graph variants use a
        /// third set again (<c>_BaseColor</c>, <c>_Metallic</c>, <c>_Smoothness</c>), which is why
        /// the code tries candidates instead of hard-coding one dialect.
        /// </summary>
        private static readonly string[] GltfBuiltInRpProperties =
        {
            "baseColorFactor", "baseColorTexture", "baseColorTexture_Rotation", "baseColorTexture_texCoord",
            "alphaCutoff",
            "roughnessFactor",
            "metallicFactor", "metallicRoughnessTexture", "metallicRoughnessTexture_Rotation",
            "metallicRoughnessTexture_texCoord",
            "normalTexture_scale", "normalTexture", "normalTexture_Rotation", "normalTexture_texCoord",
            "occlusionTexture_strength", "occlusionTexture", "occlusionTexture_Rotation",
            "occlusionTexture_texCoord",
            "emissiveFactor", "emissiveTexture", "emissiveTexture_Rotation", "emissiveTexture_texCoord",
            "_Mode", "_SrcBlend", "_DstBlend", "_ZWrite", "_CullMode",
        };

        /// <summary>Unity's Standard shader, for the "and it still works there" half.</summary>
        private static readonly string[] UnityStandardProperties =
        {
            "_Color", "_MainTex", "_Cutoff", "_Glossiness", "_GlossMapScale", "_Metallic",
            "_MetallicGlossMap", "_BumpScale", "_BumpMap", "_OcclusionStrength", "_OcclusionMap",
            "_EmissionColor", "_EmissionMap", "_Mode", "_SrcBlend", "_DstBlend", "_ZWrite",
        };

        private static Func<string, bool> Has(string[] properties)
        {
            var set = new HashSet<string>(properties);
            return set.Contains;
        }

        [Test]
        public void TheOldPropertyNamesAreNotOnGltFastsBuiltInRpShaderAtAll()
        {
            // The whole of the old ApplyGold, as three assertions. Every one of these was wrapped
            // in `if (m.HasProperty(...))`, so this IS the proof that the pass was a no-op: three
            // guards that could never open, on the only shader the app's GLBs are ever given.
            var has = Has(GltfBuiltInRpProperties);
            Assert.IsFalse(has("_EmissionColor"), "_EmissionColor is Standard's name, not glTFast's");
            Assert.IsFalse(has("_Metallic"), "_Metallic is Standard's name; glTFast says metallicFactor");
            Assert.IsFalse(has("_Glossiness"), "_Glossiness is Standard's name; glTFast says roughnessFactor");
        }

        [Test]
        public void EveryChannelOfTheGoldResolvesOnGltFastsBuiltInRpShader()
        {
            var has = Has(GltfBuiltInRpProperties);

            Assert.AreEqual("baseColorFactor", GoldShading.FirstPresent(GoldShading.BaseColorNames, has));
            Assert.AreEqual("metallicFactor", GoldShading.FirstPresent(GoldShading.MetallicNames, has));
            Assert.AreEqual("roughnessFactor", GoldShading.FirstPresent(GoldShading.RoughnessNames, has));
            Assert.AreEqual("emissiveFactor", GoldShading.FirstPresent(GoldShading.EmissiveNames, has));
            Assert.AreEqual("baseColorTexture", GoldShading.FirstPresent(GoldShading.BaseColorTextureNames, has));

            // Roughness is found, so the smoothness fallback must NOT also fire: writing both
            // conventions onto one material is how a surface ends up polished and rough at once.
            Assert.IsNull(GoldShading.FirstPresent(GoldShading.SmoothnessNames, has));
        }

        [Test]
        public void TheSameListsStillWorkOnUnitysStandardShader()
        {
            // The fallback half of the candidate rule: placeholders and the QC pass's white probe
            // material are Standard, and gold must not become a no-op the other way round.
            var has = Has(UnityStandardProperties);

            Assert.AreEqual("_Color", GoldShading.FirstPresent(GoldShading.BaseColorNames, has));
            Assert.AreEqual("_Metallic", GoldShading.FirstPresent(GoldShading.MetallicNames, has));
            Assert.IsNull(GoldShading.FirstPresent(GoldShading.RoughnessNames, has));
            Assert.AreEqual("_Glossiness", GoldShading.FirstPresent(GoldShading.SmoothnessNames, has));
            Assert.AreEqual("_EmissionColor", GoldShading.FirstPresent(GoldShading.EmissiveNames, has));
        }

        [Test]
        public void GltFastsNameIsAlwaysTriedFirst()
        {
            // A material that answers to both dialects — the case that decides which one actually
            // shades the pixel. glTF's must win: it is the one the shader reads.
            var has = Has(Join(GltfBuiltInRpProperties, UnityStandardProperties));
            Assert.AreEqual("baseColorFactor", GoldShading.FirstPresent(GoldShading.BaseColorNames, has));
            Assert.AreEqual("metallicFactor", GoldShading.FirstPresent(GoldShading.MetallicNames, has));
            Assert.AreEqual("emissiveFactor", GoldShading.FirstPresent(GoldShading.EmissiveNames, has));
        }

        [Test]
        public void FirstPresentSurvivesAMaterialThatHasNothing()
        {
            Func<string, bool> nothing = _ => false;
            Assert.IsNull(GoldShading.FirstPresent(GoldShading.BaseColorNames, nothing));
            Assert.IsNull(GoldShading.FirstPresent(null, nothing));
            Assert.IsNull(GoldShading.FirstPresent(GoldShading.BaseColorNames, null));
        }

        [Test]
        public void TheLogLineNamesWhatItCouldNotFind()
        {
            // The no-op case has to be readable AS a no-op, from the log, without a second run.
            string none = GoldShading.Report(null, null, null, null);
            Assert.IsTrue(none.Contains("applied=f"), none);
            StringAssert.Contains("baseColor", none);
            StringAssert.Contains("metallic", none);
            StringAssert.Contains("roughness", none);
            StringAssert.Contains("emissive", none);

            // …and the working case has to name the properties, so "which dialect did it take?"
            // is answerable from one line of a CI log.
            string all = GoldShading.Report("baseColorFactor", "metallicFactor", "roughnessFactor", "emissiveFactor");
            Assert.IsTrue(all.Contains("applied=t"), all);
            StringAssert.Contains("missing=-", all);
            StringAssert.Contains("color=baseColorFactor", all);

            // A partial hit is still a change, and must not be reported as a miss.
            string partial = GoldShading.Report("_Color", null, "_Glossiness", null);
            Assert.IsTrue(partial.Contains("applied=t"), partial);
            StringAssert.Contains("missing=metallic,emissive", partial);
        }

        [Test]
        public void GoldIsAWarmMetalNotADarkOne()
        {
            // Gold's measured F0, in the order that makes it gold at all. A base colour that is
            // dark or neutral on a metallic surface is the black-object bug, not a subtle one:
            // there is no diffuse term left to rescue it.
            Assert.AreEqual(1.0f, GoldShading.BaseColorLinearR, 1e-6f);
            Assert.Greater(GoldShading.BaseColorLinearR, GoldShading.BaseColorLinearG);
            Assert.Greater(GoldShading.BaseColorLinearG, GoldShading.BaseColorLinearB);
            Assert.Greater(GoldShading.BaseColorLinearB, 0.2f, "too little blue and gold turns orange");

            Assert.AreEqual(1f, GoldShading.Metallic, 1e-6f);
            Assert.AreEqual(0.25f, GoldShading.Roughness, 1e-6f);

            // The two conventions are one surface: whichever property the shader speaks, the
            // answer has to describe the same polish.
            Assert.AreEqual(1f - GoldShading.Roughness, GoldShading.Smoothness, 1e-6f);
        }

        [Test]
        public void FullMetalAgainstThisScenesFlatReflectionCubeIsDullNotBright()
        {
            // The claim in GoldShading.Metallic's comment, as arithmetic. AppBoot gives the scene
            // a UNIFORM cube (0.60, 0.72, 0.82) at reflectionIntensity 0.3 and no probe, so a
            // metal's entire colour is that product — no view dependence, no features to catch.
            const float cubeR = 0.60f, cubeG = 0.72f, cubeB = 0.82f, intensity = 0.3f;
            float r = GoldShading.MetalReflectionLinear(GoldShading.BaseColorLinearR, cubeR, intensity);
            float g = GoldShading.MetalReflectionLinear(GoldShading.BaseColorLinearG, cubeG, intensity);
            float b = GoldShading.MetalReflectionLinear(GoldShading.BaseColorLinearB, cubeB, intensity);

            Assert.AreEqual(0.180f, r, 1e-3f);
            Assert.AreEqual(0.165f, g, 1e-3f);
            Assert.AreEqual(0.083f, b, 1e-3f);

            // Still gold-ordered — the tint survives the multiply, which is the one thing the
            // reflection path is good for.
            Assert.Greater(r, g);
            Assert.Greater(g, b);

            // …but dim. This is the number that justifies keeping the emissive: reflection alone
            // puts the brightest channel at under a fifth of the range, and the emissive adds a
            // comparable amount on top without depending on the environment at all.
            Assert.Less(r, 0.25f);
            Assert.AreEqual(GoldShading.EmissiveStrength, r, 0.05f,
                "the emissive is sized against the reflection; if one moves, re-judge the other");
        }

        [Test]
        public void TheEmissiveIsAGlowNotAWhiteout()
        {
            // emissiveFactor is [HDR] on glTFast's shader, so what is written IS what the shader
            // adds — no sRGB decode on the way in. 0.5, the old code's number, would be ≈0.74 in
            // sRGB terms of pure self-lit colour and would erase the model's shading entirely.
            float r = GoldShading.EmissiveLinearR * GoldShading.EmissiveStrength;
            float g = GoldShading.EmissiveLinearG * GoldShading.EmissiveStrength;
            float b = GoldShading.EmissiveLinearB * GoldShading.EmissiveStrength;

            Assert.Greater(r, 0.05f, "below this it is not a glow, it is a rounding error");
            Assert.Less(r, 0.35f, "above this the statue stops having shading");
            Assert.Greater(r, g);
            Assert.Greater(g, b);

            // #ffb733 decoded from sRGB — the web's colour, not a new one invented here.
            Assert.AreEqual(0.4735f, GoldShading.EmissiveLinearG, 1e-3f);
            Assert.AreEqual(0.0331f, GoldShading.EmissiveLinearB, 1e-3f);
        }

        private static string[] Join(string[] a, string[] b)
        {
            var all = new List<string>(a);
            all.AddRange(b);
            return all.ToArray();
        }
    }
}
