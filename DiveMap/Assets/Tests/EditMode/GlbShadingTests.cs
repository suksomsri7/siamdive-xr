using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// The black-patch bug, pinned to numbers.
    ///
    /// These are not "does the helper return a bool" tests. Each one is a measurement taken off the
    /// real GLBs and the real CI screenshot, written down so that if somebody later decides the
    /// workaround in <c>SceneBuilder.DropMisdecodedNormalMap</c> is paranoia they have to argue
    /// with an arithmetic failure rather than with a comment.
    /// </summary>
    public class GlbShadingTests
    {
        [Test]
        public void SrgbDecodeMatchesTheStandardCurve()
        {
            // The two ends are exact by definition.
            Assert.AreEqual(0.0, GlbShading.SrgbToLinear(0.0), 1e-12);
            Assert.AreEqual(1.0, GlbShading.SrgbToLinear(1.0), 1e-12);

            // Mid-grey is the whole story: 0.5 in, 0.214 out. That gap is the bug.
            Assert.AreEqual(0.2140, GlbShading.SrgbToLinear(0.5), 1e-4);

            // The linear toe below 0.04045, where the curve is a straight 1/12.92.
            Assert.AreEqual(0.02 / 12.92, GlbShading.SrgbToLinear(0.02), 1e-12);

            // A sampler clamps; it does not extrapolate.
            Assert.AreEqual(0.0, GlbShading.SrgbToLinear(-1.0), 1e-12);
            Assert.AreEqual(1.0, GlbShading.SrgbToLinear(4.0), 1e-12);

            // Monotonic, or "brighter texel" would stop meaning "brighter".
            double prev = -1.0;
            for (int i = 0; i <= 255; i++)
            {
                double v = GlbShading.SrgbToLinear(i / 255.0);
                Assert.Greater(v, prev);
                prev = v;
            }
        }

        [Test]
        public void ANeutralNormalTexelTiltsFiftyThreeDegreesWhenDecodedAsSrgb()
        {
            // 128 is what every one of the three GLBs measured stores for "flat": their normal maps
            // average (128,128,249) with 0.2% of texels off unit length. Decoded correctly that is
            // a 0° perturbation. Decoded as sRGB it is not a small error, it is most of a right
            // angle — and it points the same way for every texel in a chart, which is why the QC
            // shot shows hard-edged black polygons instead of noise.
            double tilt = GlbShading.NeutralTiltDegrees();
            Assert.Greater(tilt, 50.0);
            Assert.Less(tilt, 56.0);
        }

        [Test]
        public void TheWorkaroundSwitchesItselfOffWhenTheImportIsFixed()
        {
            // The case the app is in today: gamma colour space, so glTFast never marks a glTF
            // texture as data and every KTX2 transcodes to an sRGB target.
            Assert.IsTrue(GlbShading.NormalMapIsMisdecoded(gammaColorSpace: true));

            // Linear colour space: glTFast tags the map as data and KtxUnity transcodes it
            // unsigned-normalised. Nothing to fix, and throwing the map away would be pure loss —
            // this is what stops the workaround becoming permanent.
            Assert.IsFalse(GlbShading.NormalMapIsMisdecoded(gammaColorSpace: false));
        }

        [Test]
        public void TheDecisionDoesNotDependOnAskingTheTexture()
        {
            // 🔴 Regression guard for CI run 30747457729. That build gated the drop on the
            // texture's own GraphicsFormat being sRGB, dropped nothing, and cost a full round:
            // KtxUnity textures arrive through Texture2D.CreateExternalTexture, so the managed
            // GraphicsFormat is re-derived from the project colour space and reports UNorm for a
            // GL object the hardware decodes as sRGB. The colour space is the only input that is
            // both knowable from C# and actually causal — if a second parameter ever reappears
            // here, this test is the argument against it.
            Assert.AreEqual(1, typeof(GlbShading)
                .GetMethod(nameof(GlbShading.NormalMapIsMisdecoded))
                .GetParameters().Length);
        }

        [Test]
        public void MetalIsOnlyTamedWhereNobodyChoseIt()
        {
            // cc0:rock_b — factor 0.4, no texture. The original report: black rock, white rim.
            Assert.AreEqual(GlbShading.TamedMetal, GlbShading.TamedMetalFactor(0.4f, hasMetalTexture: false), 1e-6f);

            // Every model in this QC pass: factor defaults to 1 but ships a metallic-roughness
            // texture whose blue channel measures 0.001. Authored. Hands off — which is also why
            // taming metal was never going to fix the black patches.
            Assert.Less(GlbShading.TamedMetalFactor(1.0f, hasMetalTexture: true), 0f);

            // Already harmless, with or without a map.
            Assert.Less(GlbShading.TamedMetalFactor(0.0f, hasMetalTexture: false), 0f);
            Assert.Less(GlbShading.TamedMetalFactor(GlbShading.MetalFactorFloor, hasMetalTexture: false), 0f);

            // Not zero — a rock with no sheen at all reads as chalk, not as underwater.
            Assert.Greater(GlbShading.TamedMetal, 0f);
            Assert.Less(GlbShading.TamedMetal, GlbShading.MetalFactorFloor * 2f);
        }

        [Test]
        public void CopyingAMaterialOntoTheSwimShaderDoesNotTurnTheAnimalIntoAMirror()
        {
            // WhaleController.CopyMaps moves a glTF material onto DM_FishWaveDetail, which cannot
            // read glTF's packed metallic-roughness texture. Every XR model measured declares
            // metallicFactor 1 against a map whose metal channel is 0.001 — carry the 1 over on its
            // own and the whale is a mirror with nothing to reflect, which is black.
            Assert.AreEqual(GlbShading.TamedMetal,
                            GlbShading.CopiedMetalFactor(1.0f, sourceHadMetalTexture: true), 1e-6f);

            // No texture: the factor really is the whole material, so it comes across — after the
            // same taming, because a scanner's leftover 0.4 is no more authored here than there.
            Assert.AreEqual(GlbShading.TamedMetal,
                            GlbShading.CopiedMetalFactor(0.4f, sourceHadMetalTexture: false), 1e-6f);
            Assert.AreEqual(0.0f, GlbShading.CopiedMetalFactor(0.0f, sourceHadMetalTexture: false), 1e-6f);
            Assert.AreEqual(0.02f, GlbShading.CopiedMetalFactor(0.02f, sourceHadMetalTexture: false), 1e-6f);

            // Whatever arrives, what leaves is a legal metallic value.
            foreach (float f in new[] { -5f, 0f, 0.5f, 1f, 7f })
            {
                foreach (bool tex in new[] { true, false })
                {
                    float v = GlbShading.CopiedMetalFactor(f, tex);
                    Assert.GreaterOrEqual(v, 0f);
                    Assert.LessOrEqual(v, 1f);
                }
            }
        }
    }
}
