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
            // ── with no measurement, the old deduction still applies ──────────────
            const NormalReadVerdict none = NormalReadVerdict.Unknown;

            // The case the app WAS in: gamma colour space and no import add-on, so glTFast never
            // marks a glTF texture as data and every KTX2 transcodes to an sRGB target.
            Assert.IsTrue(GlbShading.NormalMapIsMisdecoded(
                none, gammaColorSpace: true, loadedAsLinearData: false));

            // The case the app is in NOW: still gamma — that is the user's decision and it stands —
            // but the add-on re-opened this texture with linear:true, so there is nothing left to
            // decode wrongly and the map must be kept. This is the line that gives the models
            // their surface back.
            Assert.IsFalse(GlbShading.NormalMapIsMisdecoded(
                none, gammaColorSpace: true, loadedAsLinearData: true));

            // Linear colour space: glTFast tags the map as data itself and KtxUnity transcodes it
            // unsigned-normalised whether or not the add-on is installed. Nothing to fix, and
            // throwing the map away would be pure loss — this is what stops the workaround
            // becoming permanent.
            Assert.IsFalse(GlbShading.NormalMapIsMisdecoded(
                none, gammaColorSpace: false, loadedAsLinearData: false));
            Assert.IsFalse(GlbShading.NormalMapIsMisdecoded(
                none, gammaColorSpace: false, loadedAsLinearData: true));
        }

        [Test]
        public void AMeasurementBeatsTheDeductionInBothDirections()
        {
            // 🔴 The lesson of CI run 30894246930, as a test. That build dropped the normal map on
            // every model in the app because the deduction said gamma ⇒ misdecoded, and the
            // add-on that was supposed to make the deduction false had silently never run. Nothing
            // in the code could notice, because nothing in the code was looking at a pixel.

            // Measured intact ⇒ KEEP, even in gamma, even with no add-on. This is the case the old
            // rule got wrong for two builds, and the case that gives the surface back.
            Assert.IsFalse(GlbShading.NormalMapIsMisdecoded(
                NormalReadVerdict.UnitNormals, gammaColorSpace: true, loadedAsLinearData: false));

            // Measured decoded ⇒ DROP, even though the add-on says it handled this texture. "We
            // fixed it" is a claim; the texels are the evidence, and the evidence wins. This is
            // what stops the fix from re-enabling a map that is genuinely still broken.
            Assert.IsTrue(GlbShading.NormalMapIsMisdecoded(
                NormalReadVerdict.SrgbDecoded, gammaColorSpace: true, loadedAsLinearData: true));

            // …and in linear colour space too: the verdict is about this texture, not about the
            // project.
            Assert.IsTrue(GlbShading.NormalMapIsMisdecoded(
                NormalReadVerdict.SrgbDecoded, gammaColorSpace: false, loadedAsLinearData: true));
            Assert.IsFalse(GlbShading.NormalMapIsMisdecoded(
                NormalReadVerdict.UnitNormals, gammaColorSpace: true, loadedAsLinearData: true));
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
            Assert.AreEqual(3, parameters.Length);
            Assert.AreEqual("verdict", parameters[0].Name);
            Assert.AreEqual("gammaColorSpace", parameters[1].Name);
            Assert.AreEqual("loadedAsLinearData", parameters[2].Name);

            // 🔴 And the first parameter is the one that has to come from a MEASUREMENT. The other
            // two are deductions; they are kept only for the case where no measurement exists.
            Assert.AreEqual(typeof(NormalReadVerdict), parameters[0].ParameterType);
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

        // ── WO-animal-mat: the animals were shaded from half of glTF's arithmetic ────────────

        /// <summary>
        /// The measured metallic-roughness map of every big animal that swims, straight off the
        /// shipped CDN GLBs (<c>webcmp_probe.mjs</c>, UV-surface-area-weighted mean, 0-255).
        /// Every one of them leaves both FACTORS at glTF's default of 1 and puts the real value in
        /// the map — which is precisely why copying the factors alone produced a dead-matte animal.
        /// </summary>
        private static readonly object[] MeasuredAnimals =
        {
            //          model                    map.g   map.b
            new object[] { "mdl_great_white_shark", 128.0, 0.0 },
            new object[] { "mdl_whitetip_shark",    115.8, 130.3 },
            new object[] { "mdl_bull_shark",        118.1, 0.0 },
            new object[] { "msh_manta",             130.2, 1.2 },
            new object[] { "msh_beluga_whale",      148.3, 0.0 },
            new object[] { "msh_leopard_shark",     148.2, 0.0 },
        };

        /// <summary>
        /// The map value as the SHADER receives it. Every texture in this app is a KTX2 and the
        /// project is gamma, so the hardware applies an sRGB decode on every sample — the whole
        /// subject of this file. glTFast's PBR shader reads the identical texture through the
        /// identical sampler, so decoding here is not a fudge: it is the parity being asserted.
        /// </summary>
        private static double Sampled(double storedByte)
            => GlbShading.SrgbToLinear(storedByte / 255.0);

        [Test]
        public void EveryAnimalUsedToRenderDeadMatteAndNowMatchesItsMap()
        {
            // 🔴 THE BUG, as one number. roughnessFactor is 1 on all six, so the old
            // "_Glossiness = 1 − roughnessFactor" was 1 − 1 = ZERO smoothness — chalk — on every
            // animal in the game, while the wreck beside it kept glTFast's shader and looked right.
            float glossFactor = GlbShading.WaveGlossFactor(1f);
            Assert.AreEqual(0f, glossFactor, 1e-6f, "this is the value that shipped, and it is the bug");

            // Same factor, now multiplied by the map the shader finally reads. These are the
            // smoothness values the animals must actually render at.
            var expected = new[] { 0.784, 0.826, 0.819, 0.776, 0.703, 0.703 };
            for (int i = 0; i < MeasuredAnimals.Length; i++)
            {
                var row = (object[])MeasuredAnimals[i];
                double s = GlbShading.ShaderSmoothness(glossFactor, Sampled((double)row[1]));
                Assert.AreEqual(expected[i], s, 5e-4, (string)row[0]);

                // The point, stated as a range rather than a constant: every one of them is a wet
                // animal, and none of them is anywhere near the 0 that shipped.
                Assert.Greater(s, 0.69, (string)row[0]);
                Assert.Less(s, 0.84, (string)row[0]);
            }
        }

        [Test]
        public void TheWhitetipStaysMetalAndNothingElseBecomesMetal()
        {
            // With a glTF-layout map bound the factor is glTF's own 1 and the MAP decides. That is
            // the only way the one genuine metal in the catalogue survives.
            float metalFactor = GlbShading.WaveMetalFactor(1f, hasGltfLayoutMap: true);
            Assert.AreEqual(1f, metalFactor, 1e-6f);

            // 🔴 mdl:whitetip_shark — map.b ≈ 130/255 over essentially its whole surface (63.7% of
            // the texture is empty UV gutter; of the texels that land on the shark, effectively all
            // sit at 0.4-0.6). Authored, not transcode noise: great white's b is 0.0 on the same
            // surface. A wet metallic sheen, and nothing like a mirror.
            double whitetip = GlbShading.ShaderMetallic(metalFactor, Sampled(130.3));
            Assert.AreEqual(0.224, whitetip, 5e-4);
            Assert.Greater(whitetip, 0.15, "the whitetip is the one real metal and must stay one");
            Assert.Less(whitetip, 0.35, "…a sheen, not a mirror with nothing to reflect");

            // Everything else is a dielectric and must render as one — including the 0.06 sheen the
            // old code handed every animal indiscriminately, which is now gone from the five that
            // never asked for it.
            foreach (var o in MeasuredAnimals)
            {
                var row = (object[])o;
                if ((string)row[0] == "mdl_whitetip_shark") continue;
                double m = GlbShading.ShaderMetallic(metalFactor, Sampled((double)row[2]));
                Assert.Less(m, 0.001, (string)row[0] + " ships no metal and must not be given any");
            }
        }

        [Test]
        public void TheStandInZeroIsNotCopiedOntoAShaderThatCanReadTheMap()
        {
            // 🔴 The ordering trap this rule exists for. SceneBuilder.TameMetal runs BEFORE
            // AttachWhale, so by the time CopyMaps reads the material, MappedMetalFactor has already
            // overwritten metallicFactor with 0 — a stand-in for a map the old shader could not
            // read. Copy that 0 onto a shader that CAN read the map and the map is multiplied by
            // zero: the whitetip's metal vanishes and the stand-in has outlived its reason.
            Assert.AreEqual(0f, GlbShading.MappedMetalFactor(1f, hasMetalTexture: true), 1e-6f);
            Assert.AreEqual(1f, GlbShading.WaveMetalFactor(0f, hasGltfLayoutMap: true), 1e-6f,
                            "the map is present, so the map is the metal — not TameMetal's stand-in");
            Assert.AreEqual(0.224,
                            GlbShading.ShaderMetallic(GlbShading.WaveMetalFactor(0f, true), Sampled(130.3)),
                            5e-4);

            // No glTF-layout map: the factor IS the whole material and comes across as it stands,
            // already tamed upstream. cc0:rock_b's scanner leftover reaches CopyMaps as TamedMetal.
            Assert.AreEqual(GlbShading.TamedMetal,
                            GlbShading.WaveMetalFactor(GlbShading.TamedMetal, hasGltfLayoutMap: false), 1e-6f);
            Assert.AreEqual(0f, GlbShading.WaveMetalFactor(0f, hasGltfLayoutMap: false), 1e-6f);

            // ⚠️ A Unity _MetallicGlossMap is metal = R, smoothness = A. It is never bound to
            // _MetalRoughMap and must never license a factor of 1 either — that combination would
            // shade the animal off whatever happens to be in the green and blue channels.
            Assert.AreEqual(0.02f, GlbShading.WaveMetalFactor(0.02f, hasGltfLayoutMap: false), 1e-6f);

            // Whatever arrives, what leaves is a legal metallic value.
            foreach (float f in new[] { -5f, 0f, 0.5f, 1f, 7f })
            {
                foreach (bool map in new[] { true, false })
                {
                    float v = GlbShading.WaveMetalFactor(f, map);
                    Assert.GreaterOrEqual(v, 0f);
                    Assert.LessOrEqual(v, 1f);
                }
            }
        }

        [Test]
        public void AMaterialWithNoMapShadesExactlyAsItDidBefore()
        {
            // The shader's map defaults to "white" (g = b = 1), so every no-map material — the
            // palette placeholder a failed download leaves behind, above all — comes out of the new
            // arithmetic with the factors untouched. This is what makes the change safe.
            foreach (float gloss in new[] { 0f, 0.1f, 0.5f, 1f })
                Assert.AreEqual(gloss, GlbShading.ShaderSmoothness(gloss, 1.0), 1e-9);

            foreach (float metal in new[] { 0f, GlbShading.TamedMetal, 0.5f, 1f })
                Assert.AreEqual(metal, GlbShading.ShaderMetallic(metal, 1.0), 1e-9);

            // And a fully rough map (g = 1) is still allowed to say "matte" — the map is not being
            // second-guessed, only read.
            Assert.AreEqual(0.0, GlbShading.ShaderSmoothness(0f, 1.0), 1e-9);
        }

        [Test]
        public void TheSwimShaderIsNoLongerToldItCannotReadAMap()
        {
            // CopiedMetalFactor collapsed the metal to a flat 0.06 for every animal that shipped a
            // map, because the shader of the day could not sample one. DM_FishWaveDetail now has
            // _MetalRoughMap; re-adding the helper would mean re-deleting the map.
            Assert.IsNull(typeof(GlbShading).GetMethod("CopiedMetalFactor"),
                "the swim shader reads glTF's metallic-roughness map now — a scalar collapse of it " +
                "is what made every animal matte, and 0.06 metal is what it gave the five sharks " +
                "whose maps say zero");
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
