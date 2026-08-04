using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// The hero-effect allowlist, pinned against the web builder it is a port of.
    /// Every id below was read out of <c>builder.html</c>, with its line number.
    /// </summary>
    public class FxRulesTests
    {
        [Test]
        public void OnlyTheTridentIsGold()
        {
            // builder.html:1227 — the ONLY item in the whole catalogue carrying fx:'gold'.
            Assert.IsTrue(FxRules.IsGolden("sw:golden_trident"));

            // 🔴 The bug. Both of these are plain stone on the web — no fx tag at all — and the app
            // was painting them gold because the old rule matched the substring "poseidon".
            // builder.html:1153 and :1242. The second is literally named "โพไซดอนศิลาเขียว":
            // GREEN STONE. Gold is not a tint here, it is emissive + metallic 0.9 + smoothness 0.75.
            Assert.IsFalse(FxRules.IsGolden("cc0:poseidon"));
            Assert.IsFalse(FxRules.IsGolden("stat:verdant_poseidon"));

            // Nothing else in the catalogue may creep in either.
            Assert.IsFalse(FxRules.IsGolden("sw:stone_king"));
            Assert.IsFalse(FxRules.IsGolden("msh:manta"));
            Assert.IsFalse(FxRules.IsGolden("mdl:whitetip_shark"));
            Assert.IsFalse(FxRules.IsGolden(""));
            Assert.IsFalse(FxRules.IsGolden(null));
        }

        [Test]
        public void TheSameAssetIsRecognisedByItsCdnFilename()
        {
            // The map and the QC pass pass the manifest id; a CDN object for the same asset is
            // spelled with an underscore and a LOD suffix. One asset, one answer.
            Assert.IsTrue(FxRules.IsGolden("sw_golden_trident_xr0"));
            Assert.IsTrue(FxRules.IsGolden("sw_golden_trident_xr1.glb"));
            Assert.IsTrue(FxRules.IsGolden("SW:Golden_Trident"));

            // …and the suffix tolerance must not become a free-for-all prefix match.
            Assert.IsFalse(FxRules.IsGolden("sw:golden"));
            Assert.IsFalse(FxRules.IsGolden("sw:trident_of_the_deep"));
        }

        [Test]
        public void NothingSwaysItsBeardBecauseTheUserAskedForThatToStop()
        {
            // builder.html:1228 — "ยกเลิกเคราพริ้วตาม user 2026-07-04". The web applies fx:'beard'
            // to nothing at all; the app was applying it to the stone king and to every poseidon,
            // which is a reversed decision plus a per-frame Update on each statue.
            Assert.IsFalse(FxRules.HasBeard("sw:stone_king"));
            Assert.IsFalse(FxRules.HasBeard("cc0:poseidon"));
            Assert.IsFalse(FxRules.HasBeard("stat:verdant_poseidon"));
            Assert.IsFalse(FxRules.HasBeard("sw:golden_trident"));
        }
    }
}
