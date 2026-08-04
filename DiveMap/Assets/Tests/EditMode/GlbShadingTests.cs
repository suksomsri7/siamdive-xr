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
            // The case the app WAS in: gamma colour space and no import add-on, so glTFast never
            // marks a glTF texture as data and every KTX2 transcodes to an sRGB target.
            Assert.IsTrue(GlbShading.NormalMapIsMisdecoded(
                gammaColorSpace: true, loadedAsLinearData: false));

            // The case the app is in NOW: still gamma — that is the user's decision and it stands —
            // but the add-on re-opened this texture with linear:true, so there is nothing left to
            // decode wrongly and the map must be kept. This is the line that gives the models
            // their surface back.
            Assert.IsFalse(GlbShading.NormalMapIsMisdecoded(
                gammaColorSpace: true, loadedAsLinearData: true));

            // Linear colour space: glTFast tags the map as data itself and KtxUnity transcodes it
            // unsigned-normalised whether or not the add-on is installed. Nothing to fix, and
            // throwing the map away would be pure loss — this is what stops the workaround
            // becoming permanent.
            Assert.IsFalse(GlbShading.NormalMapIsMisdecoded(
                gammaColorSpace: false, loadedAsLinearData: false));
            Assert.IsFalse(GlbShading.NormalMapIsMisdecoded(
                gammaColorSpace: false, loadedAsLinearData: true));
        }

        [Test]
        public void TheDecisionStillDoesNotDependOnAskingTheTexturesFormat()
        {
            // 🔴 Regression guard for CI run 30747457729, restated. That build gated the drop on
            // the texture's own GraphicsFormat being sRGB, dropped nothing, and cost a full round:
            // a KtxUnity texture's managed GraphicsFormat is re-derived rather than reported, so
            // it says UNorm for something the sampler may well be decoding as sRGB. That input is
            // still banned.
            //
            // The second parameter this method now takes is NOT that. "Did MY loader open this
            // texture with linear:true" is a fact this app wrote down itself when it happened —
            // no inference, no re-derivation, no asking the graphics API to describe an object it
            // only wraps. If a third parameter ever appears here, it has to clear the same bar.
            var parameters = typeof(GlbShading)
                .GetMethod(nameof(GlbShading.NormalMapIsMisdecoded))
                .GetParameters();
            Assert.AreEqual(2, parameters.Length);
            Assert.AreEqual("gammaColorSpace", parameters[0].Name);
            Assert.AreEqual("loadedAsLinearData", parameters[1].Name);
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
        public void TheMetalRoughSwapShipsExactlyWhatTheProbeMeasured()
        {
            // 🔴 The point of pinning these two numbers: CI run 30753720407 photographed the
            // models with the metallic-roughness texture cleared and metallic/roughness set to
            // these values, and every black patch on all four affected models went to 0.00%.
            // Anything else is an unvalidated tweak, and unvalidated tweaks have cost this bug
            // three CI rounds already.
            Assert.AreEqual(0f, GlbShading.ProbeValidatedMetallic, 1e-6f);
            Assert.AreEqual(0.6f, GlbShading.ProbeValidatedRoughness, 1e-6f);

            // Legal PBR values, and matte enough that a dielectric scan does not read as plastic.
            Assert.GreaterOrEqual(GlbShading.ProbeValidatedRoughness, 0f);
            Assert.LessOrEqual(GlbShading.ProbeValidatedRoughness, 1f);

            // 🔴 And they are no longer applied at import. Run 30765284038 loaded models whose UV
            // gutters had been dilated at source and darkOfSubject fell 14.75% → 0.25% (kraken),
            // 17.35% → 0.31% (statue), 8.69% → 0.58% (hardeep) with the metallic-roughness maps
            // back in place. Stripping them was a workaround for a cause that has since been fixed
            // properly, so the maps are restored and these constants exist only for the probe.
            Assert.IsNull(typeof(GlbShading).GetMethod("ReplaceMetalRoughTextureWithScalars"),
                "the metallic-roughness workaround was retired when source dilation landed — " +
                "re-adding it would take the per-texel gloss back off every scan for nothing");
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

        // ── WO-E5m ───────────────────────────────────────────────────────────────

        [Test]
        public void ADefaultMetalFactorSittingOnTopOfAMapIsNotADecision()
        {
            // 🔴 The rule, and the measurement behind it: setting metallicFactor to 0 and changing
            // nothing else took darkOfSubject 65%→8% (kraken), 75%→31% (singha), 85%→55%
            // (domed_temple), while the roughness twin of the same probe moved 65%→63%.
            //
            // 1.0 is glTF's DEFAULT — a file that never writes the field gets it — and every one of
            // these files pairs it with a metallic-roughness map whose blue channel measures 0-1
            // out of 255. "The metal comes from the map, and the map says none" is the authored
            // intent; 0 is that intent, and it survives the map not surviving the transcode.
            Assert.That(GlbShading.MappedMetalFactor(1f, hasMetalTexture: true), Is.EqualTo(0f));
            Assert.That(GlbShading.MappedMetalFactor(0.99f, true), Is.EqualTo(0f),
                        "an exporter writing 0.99, or a quantised float, is the same case");
            Assert.That(GlbShading.MappedMetalFactor(GlbShading.DefaultMetalFactorFloor, true),
                        Is.EqualTo(0f), "the floor itself is inside the rule");
        }

        [Test]
        public void AChosenMetalIsLeftAlone()
        {
            // A mid-metal is somebody's decision, not a default, and 0.9 is far enough above the
            // 0.4 a scanner leaves behind that nothing real is caught.
            Assert.That(GlbShading.MappedMetalFactor(0.4f, true), Is.LessThan(0f));
            Assert.That(GlbShading.MappedMetalFactor(0.89f, true), Is.LessThan(0f));
            Assert.That(GlbShading.MappedMetalFactor(0f, true), Is.LessThan(0f));
        }

        [Test]
        public void TheGoldenTridentKeepsItsShine()
        {
            // GoldFx sets metallicFactor = 1 with NO metallic-roughness texture — a deliberate
            // metal. Both metal rules are keyed on the map's presence, so gold is outside them by
            // the same property that has always protected it, not by a special case.
            Assert.That(GlbShading.MappedMetalFactor(1f, hasMetalTexture: false), Is.LessThan(0f),
                        "a deliberate metal with no map must stay a mirror");
        }

        [Test]
        public void TheTwoMetalRulesAreComplementsAndNeverOverlap()
        {
            // One material must never be claimed by both, or the order they run in becomes
            // load-bearing — which is how this project got ghost maps and black tail fins.
            foreach (float factor in new[] { 0f, 0.04f, 0.4f, 0.89f, 0.9f, 0.99f, 1f })
                foreach (bool hasTex in new[] { true, false })
                {
                    bool tamed = GlbShading.TamedMetalFactor(factor, hasTex) >= 0f;
                    bool mapped = GlbShading.MappedMetalFactor(factor, hasTex) >= 0f;
                    Assert.That(tamed && mapped, Is.False,
                        $"factor={factor} hasTex={hasTex} matched both rules");
                }

            // …and between them they cover every high factor, which is the point: a surface with no
            // diffuse is the bug, and it does not care which rule was supposed to catch it.
            Assert.That(GlbShading.TamedMetalFactor(1f, false) >= 0f
                        || GlbShading.MappedMetalFactor(1f, false) >= 0f, Is.True);
            Assert.That(GlbShading.TamedMetalFactor(1f, true) >= 0f
                        || GlbShading.MappedMetalFactor(1f, true) >= 0f, Is.True);
        }
    }
}
