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

        // ── WO-E5e, RETRACTED ────────────────────────────────────────────────────

        [Test]
        public void IndependentXyEnergyIsAStatementAboutAWindowNotAboutATexture()
        {
            // 🔴 This fixture used to gate on "did the normal map survive its encoding", built on a
            // measurement taken over one 4×4 ETC1 block and quoted as a property of the whole map.
            // The gate is gone; the table that killed it stays, because the mistake is the reusable
            // part. Same definition, same files, window swept — independent x/y energy, per cent:
            //
            //     window        2×2     4×4     8×8    16×16   64×64   whole
            //     master       49.96   50.02   49.89   49.87   49.79   49.70
            //     shipped       0.02    0.01   31.29   38.27   41.73   42.21
            //
            // A real normal map is scale-invariant because x and y are independent axes at every
            // frequency. ETC1S flattens x onto y inside a block and keeps a base colour per block,
            // so the loss is high-frequency only: 42.21 of 49.70 survives, about 85%.
            double[] master = { 49.96, 50.02, 49.89, 49.87, 49.79, 49.70 };
            double[] shipped = { 0.02, 0.01, 31.29, 38.27, 41.73, 42.21 };

            double masterSpread = 0.0;
            for (int i = 0; i < master.Length; i++)
                for (int j = 0; j < master.Length; j++)
                    masterSpread = System.Math.Max(masterSpread, System.Math.Abs(master[i] - master[j]));
            Assert.That(masterSpread, Is.LessThan(1.0),
                "a real normal map reads the same at every window — that is what makes the number quotable");

            double shippedSpread = shipped[shipped.Length - 1] - shipped[1];
            Assert.That(shippedSpread, Is.GreaterThan(40.0),
                "the shipped file does not — so no single window's answer describes it");

            Assert.That(shipped[shipped.Length - 1] / master[master.Length - 1], Is.GreaterThan(0.80),
                "about 85% of the master's independent content actually survives to the device");
        }

        [Test]
        public void TheWebScoresWorseOnThisMetricAndIsTheGoodPicture()
        {
            // The fact that should have stopped the claim before it was written. The web ships
            // ETC1S normal maps at 512² and measures LOWER independent x/y energy than we do at
            // 2048², while being the render the user holds up as correct. A mechanism that is
            // stronger on the good picture than on the bad one is not what separates them.
            double[] web = { 0.2995, 0.3333, 0.3058, 0.2724 };   // kraken, singha, chang, htms732
            double ours = 0.39;                                   // our weakest of the same set
            foreach (double w in web)
                Assert.That(w, Is.LessThan(ours),
                    "the web is more damaged on this metric than we are, and looks better");
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
