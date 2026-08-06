using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// Which GPU format a KTX2 is transcoded into, and — just as important — when the answer is
    /// "leave it alone".
    ///
    /// 🔴 Every test here guards one of two failure modes, and they pull in opposite directions:
    /// forcing ASTC where it is not supported loses the texture entirely, and NOT forcing it is
    /// the build-276 bug. The middle is narrow, so it is pinned from both sides.
    /// </summary>
    public class KtxTranscodeTargetTests
    {
        private const bool Astc = true;
        private const bool NoAstc = false;

        private static KtxLoadPlan Plan(
            bool isData, bool gltfLinear = false, bool readable = false, bool astc = true)
            => KtxTranscodeTargets.Plan(isData, gltfLinear, readable, astc, astc);

        // ── the point of the whole change ────────────────────────────────────────────────────

        [Test]
        public void ColourGetsAstcSrgb()
        {
            // The whale shark's base colour. sRGB is unchanged from today; ETC is not.
            KtxLoadPlan plan = Plan(isData: false);
            Assert.AreEqual(KtxTranscodeTarget.Astc4x4Srgb, plan.Target);
            Assert.IsFalse(plan.Linear);
        }

        [Test]
        public void NormalMapGetsAstcUNormAndStaysLinear()
        {
            // 🔴 Both halves, in one assertion, because they are one decision. The normal-map fix
            // that already shipped is `Linear = true`; if forcing a format ever silently reverted
            // that, the surface detail would go and nobody would suspect the codec change.
            KtxLoadPlan plan = Plan(isData: true);
            Assert.AreEqual(KtxTranscodeTarget.Astc4x4UNorm, plan.Target);
            Assert.IsTrue(plan.Linear);
        }

        [Test]
        public void TheSrgbTwinIsNeverPairedWithLinearSampling()
        {
            // The one combination that would be a visible colour bug rather than a codec change:
            // an sRGB-typed format holding data, or a UNorm format holding colour. Swept over
            // every input rather than spot-checked, since the mapping is small enough to exhaust.
            foreach (bool isData in new[] { false, true })
                foreach (bool gltfLinear in new[] { false, true })
                {
                    KtxLoadPlan plan = Plan(isData, gltfLinear);
                    if (plan.Target == KtxTranscodeTarget.AutoSelect) continue;
                    Assert.AreEqual(
                        plan.Linear,
                        plan.Target == KtxTranscodeTarget.Astc4x4UNorm,
                        $"isData={isData} gltfLinear={gltfLinear}: format and sampling disagree");
                }
        }

        // ── the gates ────────────────────────────────────────────────────────────────────────

        [Test]
        public void NoAstcSupportMeansNoForcing()
        {
            // 🔴 CI's llvmpipe GL renderer is this case. Asking for a format the device cannot
            // sample does not degrade gracefully — the package returns FormatUnsupportedBySystem
            // and no texture (KtxTexture.cs:155-158) — so the answer has to be AutoSelect, which
            // is byte-for-byte what shipped before this change.
            Assert.AreEqual(KtxTranscodeTarget.AutoSelect, Plan(isData: false, astc: NoAstc).Target);
            Assert.AreEqual(KtxTranscodeTarget.AutoSelect, Plan(isData: true, astc: NoAstc).Target);
        }

        [Test]
        public void WithoutAstcTheNormalMapFixStillApplies()
        {
            // The linear flag costs nothing and works everywhere. Losing it on a device with no
            // ASTC would be this change quietly undoing the previous one.
            Assert.IsTrue(Plan(isData: true, astc: NoAstc).Linear);
        }

        [Test]
        public void EachTwinIsGatedOnItsOwnSupportFlag()
        {
            // Belt and braces for a device that reports one twin and not the other. The claim
            // gate requires both, so this should be unreachable — but Plan is the thing that
            // hands a format to the package, and it must never hand over an unsupported one.
            KtxLoadPlan colourNoSrgb = KtxTranscodeTargets.Plan(
                false, false, false, astcSrgbSupported: false, astcUNormSupported: true);
            Assert.AreEqual(KtxTranscodeTarget.AutoSelect, colourNoSrgb.Target);

            KtxLoadPlan dataNoUNorm = KtxTranscodeTargets.Plan(
                true, false, false, astcSrgbSupported: true, astcUNormSupported: false);
            Assert.AreEqual(KtxTranscodeTarget.AutoSelect, dataNoUNorm.Target);
        }

        [Test]
        public void ReadableTexturesAreNeverForced()
        {
            // 🔴 KTX for Unity's format-forcing overload hard-codes readable to false
            // (KtxTexture.cs:147-174). glTFast asks for readable only on textures it means to
            // clone for a second sampler, and that clone is gated on isReadable
            // (GltfImport.cs:2757) — so forcing here would cost a wrap mode and gain a codec.
            foreach (bool isData in new[] { false, true })
                Assert.AreEqual(
                    KtxTranscodeTarget.AutoSelect,
                    Plan(isData, readable: true).Target,
                    $"isData={isData}");
        }

        [Test]
        public void ReadableStillKeepsTheLinearFlag()
        {
            // Declining to force a format is not declining to fix the normal map.
            Assert.IsTrue(Plan(isData: true, readable: true).Linear);
        }

        // ── agreeing with glTFast rather than fighting it ────────────────────────────────────

        [Test]
        public void AProjectInLinearColourSpaceIsNotOverridden()
        {
            // Once the project moves to linear, glTFast marks the non-colour textures itself and
            // passes linear: true. This must agree with it, not argue: same flag, UNorm twin.
            KtxLoadPlan plan = Plan(isData: false, gltfLinear: true);
            Assert.IsTrue(plan.Linear);
            Assert.AreEqual(KtxTranscodeTarget.Astc4x4UNorm, plan.Target);
        }

        [Test]
        public void GltfLinearIsNeverDowngraded()
        {
            // A texture glTFast called linear stays linear whatever the role says. The role can
            // only ever ADD linearity (the normal-map fix), never remove it.
            Assert.IsTrue(Plan(isData: false, gltfLinear: true).Linear);
            Assert.IsTrue(Plan(isData: true, gltfLinear: true).Linear);
        }

        // ── who gets claimed at all ─────────────────────────────────────────────────────────

        [Test]
        public void NonKtxIsNeverClaimed()
        {
            // A PNG has no transcode step to steer and needs no linear fix in a gamma project.
            Assert.IsFalse(KtxTranscodeTargets.Claims(isKtx2: false, isDataImage: true, astcSupported: Astc));
            Assert.IsFalse(KtxTranscodeTargets.Claims(isKtx2: false, isDataImage: false, astcSupported: Astc));
        }

        [Test]
        public void ColourIsClaimedOnlyWhenAstcIsAvailable()
        {
            // 🔴 THE SAFETY PROPERTY OF THE WHOLE CHANGE. A claim is irreversible — glTFast routes
            // the image to us and there is no fallback loader behind us (GltfImport.cs:1814-1826).
            // On hardware where we have nothing to offer a colour texture, we must not take it.
            Assert.IsTrue(KtxTranscodeTargets.Claims(true, isDataImage: false, astcSupported: Astc));
            Assert.IsFalse(KtxTranscodeTargets.Claims(true, isDataImage: false, astcSupported: NoAstc));
        }

        [Test]
        public void NormalMapsAreClaimedEvenWithoutAstc()
        {
            // Their fix predates this one and does not depend on it.
            Assert.IsTrue(KtxTranscodeTargets.Claims(true, isDataImage: true, astcSupported: NoAstc));
        }

        [Test]
        public void AClaimedTextureAlwaysHasSomethingToDo()
        {
            // Ties the two functions together: if Claims says yes on an ASTC device, Plan must
            // either force a format or be doing the linear fix. A claim that changes nothing is a
            // texture taken off glTFast's path for no reason at all.
            foreach (bool isData in new[] { false, true })
            {
                Assert.IsTrue(KtxTranscodeTargets.Claims(true, isData, Astc));
                KtxLoadPlan plan = Plan(isData);
                Assert.IsTrue(
                    plan.Target != KtxTranscodeTarget.AutoSelect || plan.Linear,
                    $"isData={isData}: claimed but the plan is a no-op");
            }
        }

        // ── PlanForFile: what the file itself turns out to be ────────────────────

        [Test]
        public void ABasisFileKeepsTheRolePlanExactly()
        {
            // Every GLB that shipped before the ASTC set. This must be a pure pass-through or the
            // build-276 fix is silently undone.
            foreach (bool isData in new[] { false, true })
            {
                KtxLoadPlan role = Plan(isData);
                KtxLoadPlan actual = KtxTranscodeTargets.PlanForFile(role, needsTranscoding: true);
                Assert.AreEqual(role.Target, actual.Target, $"isData={isData}");
                Assert.AreEqual(role.Linear, actual.Linear, $"isData={isData}");
            }
        }

        [Test]
        public void ANonBasisFileForcesNothing()
        {
            // 🔴 The whole point. KtxTexture.cs:256 branches on needsTranscoding and the else at
            // :289-300 never reads targetFormat — :262 is the only line that does and it is in the
            // branch not taken. Asking anyway is at best a no-op and at worst a false negative,
            // because the forcing overload hard-codes linear:true (:160) and :296 then tests the
            // FILE's format for linear FILTERING support rather than Sample.
            foreach (bool isData in new[] { false, true })
            {
                KtxLoadPlan actual =
                    KtxTranscodeTargets.PlanForFile(Plan(isData), needsTranscoding: false);
                Assert.AreEqual(KtxTranscodeTarget.AutoSelect, actual.Target, $"isData={isData}");
                Assert.IsFalse(actual.Linear, $"isData={isData}: the narrower usage question");
            }
        }

        [Test]
        public void ANonBasisPlanIsTheSameWhateverTheRoleSaid()
        {
            // There is nothing left to vary: the format comes out of the file, and `linear` reaches
            // nothing but a support check (LoadTextureData is handed graphicsFormat and never
            // linear — KtxTexture.cs:347-355). So all four role plans collapse to one.
            KtxLoadPlan a = KtxTranscodeTargets.PlanForFile(Plan(true), false);
            KtxLoadPlan b = KtxTranscodeTargets.PlanForFile(Plan(false), false);
            KtxLoadPlan c = KtxTranscodeTargets.PlanForFile(
                Plan(true, gltfLinear: true, readable: true), false);
            KtxLoadPlan d = KtxTranscodeTargets.PlanForFile(Plan(false, astc: NoAstc), false);

            foreach (KtxLoadPlan p in new[] { a, b, c, d })
            {
                Assert.AreEqual(KtxTranscodeTarget.AutoSelect, p.Target);
                Assert.IsFalse(p.Linear);
            }
        }

        [Test]
        public void ANonBasisFileNeedsNoAstcSupportToBePlanned()
        {
            // PlanForFile is not the ASTC gate — AstcAssetPick is, before the bytes are fetched.
            // If a non-basis file reaches a device with no ASTC anyway (a hard-coded QC url, say),
            // the plan must still be the harmless one rather than a forced format.
            KtxLoadPlan p = KtxTranscodeTargets.PlanForFile(
                Plan(isData: false, astc: NoAstc), needsTranscoding: false);
            Assert.AreEqual(KtxTranscodeTarget.AutoSelect, p.Target);
            Assert.IsFalse(p.Linear);
        }
    }
}
