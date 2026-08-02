using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// WO-XR-04.1 — pins the GLB/LOD choice per fish species. These numbers came off the
    /// real CDN files; if a model is re-exported, this test is the place the new triangle
    /// budget has to be argued.
    /// </summary>
    public class FishAssetPickTests
    {
        private const string Scad0 = "https://maps.siamdive.com/models/xr/Scad_School_xr0.glb";
        private const string Scad1 = "https://maps.siamdive.com/models/xr/Scad_School_xr1.glb";
        private const string Barra0 = "https://maps.siamdive.com/models/xr/Barracuda_School_xr0.glb";
        private const string Barra1 = "https://maps.siamdive.com/models/xr/Barracuda_School_xr1.glb";
        private const string Trev0 = "https://maps.siamdive.com/models/xr/Trevally_xr0.glb";
        private const string Trev1 = "https://maps.siamdive.com/models/xr/Trevally_xr1.glb";

        [Test]
        public void Scad_RealDemoSchoolOf120_TakesLod1()
        {
            // 6,650 at LOD0 against a real 3,296 at LOD1 (−50.4 %). The school of 120 would be
            // 798k triangles on LOD0 and is far over budget → swap: 120 × 3,296 = 395.5k.
            Assert.IsTrue(FishAssetPick.TryPick("school:scad", Scad0, Scad1, 120, out FishAssetPick.Pick p));
            Assert.AreEqual(Scad1, p.Url);
            Assert.IsTrue(p.IsLod1);
            Assert.AreEqual(3296, p.Tris);
            Assert.AreEqual(1.911f, p.LocalLen, 0.001f);
        }

        [Test]
        public void Barracuda_RealDemoSchoolOf160_TakesLod1()
        {
            // 3,029 → 1,595 (−47.3 %). 160 × 3,029 = 485k on LOD0, over budget → swap:
            // 160 × 1,595 = 255.2k.
            Assert.IsTrue(FishAssetPick.TryPick("school:barracuda", Barra0, Barra1, 160, out FishAssetPick.Pick p));
            Assert.AreEqual(Barra1, p.Url);
            Assert.IsTrue(p.IsLod1);
            Assert.AreEqual(1595, p.Tris);
            Assert.AreEqual(1.862f, p.LocalLen, 0.001f);
        }

        [Test]
        public void ASwapMustPayForItself()
        {
            // The threshold sits between the tightest REAL LOD still in the table — the trevally's
            // 8,000 → 6,442 = 19.5 %, which must keep swapping — and the export artefacts that
            // shipped for a few hours on 2 ส.ค., which must never swap. Those two files are gone,
            // so they live here as fixtures: this is the case the rule exists for.
            Assert.LessOrEqual(FishAssetPick.Lod1MinSavingPercent, 19,
                               "raised past the trevally's 19.5 % — the tightest real LOD would stop");
            Assert.Greater(FishAssetPick.Lod1MinSavingPercent, 1,
                           "lowered onto the 0.5 % artefact — a second download for nothing");

            // Refused: an "xr1" that is the same model under another name, or plain bigger.
            Assert.IsFalse(FishAssetPick.WorthSwappingDown(6650, 6616), "scad's 0.5 % non-LOD");
            Assert.IsFalse(FishAssetPick.WorthSwappingDown(3029, 3087), "barracuda's HEAVIER xr1");
            Assert.IsFalse(FishAssetPick.WorthSwappingDown(3000, 3000), "identical files");

            // Taken: every real decimation, tightest first.
            Assert.IsTrue(FishAssetPick.WorthSwappingDown(8000, 6442), "trevally −19.5 %");
            Assert.IsTrue(FishAssetPick.WorthSwappingDown(3029, 1595), "barracuda −47.3 %");
            Assert.IsTrue(FishAssetPick.WorthSwappingDown(6650, 3296), "scad −50.4 %");

            // The line itself: exactly 10 % pays, a hair under does not.
            Assert.IsTrue(FishAssetPick.WorthSwappingDown(10000, 9000));
            Assert.IsFalse(FishAssetPick.WorthSwappingDown(10000, 9001));
        }

        [Test]
        public void Yellowtail_RealDemoPodOf50_TakesLod1()
        {
            // The one row the rebuild did not touch, and the one real LOD in the table.
            // The map's own pods: 50 × 8,000 = 400k tris each, over budget → LOD1 (6,442).
            Assert.IsTrue(FishAssetPick.TryPick("pod:yellowtail", Trev0, Trev1, 50, out FishAssetPick.Pick p));
            Assert.AreEqual(Trev1, p.Url);
            Assert.IsTrue(p.IsLod1);
            Assert.AreEqual(6442, p.Tris);
        }

        [Test]
        public void Yellowtail_HandfulOfFish_StaysOnLod0()
        {
            // 12 × 8,000 = 96k, inside budget → keep the detailed model.
            Assert.IsTrue(FishAssetPick.TryPick("pod:yellowtail", Trev0, Trev1, 12, out FishAssetPick.Pick p));
            Assert.AreEqual(Trev0, p.Url);
            Assert.IsFalse(p.IsLod1);
            Assert.AreEqual(8000, p.Tris);
        }

        [Test]
        public void HugeSchools_StaySwappedDown()
        {
            // 500 × 6,650 = 3.3M is far over the budget and the LOD1 is real, so the swap holds at
            // any size — there is nothing lighter to escalate to.
            Assert.IsTrue(FishAssetPick.TryPick("school:scad", Scad0, Scad1, 500, out FishAssetPick.Pick p));
            Assert.AreEqual(Scad1, p.Url);
            Assert.IsTrue(p.IsLod1);
            Assert.AreEqual(3296, p.Tris);

            Assert.IsTrue(FishAssetPick.TryPick("school:barracuda", Barra0, Barra1, 500, out FishAssetPick.Pick b));
            Assert.AreEqual(Barra1, b.Url);
            Assert.IsTrue(b.IsLod1);
        }

        [Test]
        public void SmallSchoolsKeepTheDetailedModel_HoweverGoodTheLod1Is()
        {
            // 20 × 6,650 = 133k, inside the 200k budget → the budget alone keeps LOD0. A real LOD1
            // is a reason to swap when the map cannot afford LOD0, not a reason to look worse.
            Assert.IsTrue(FishAssetPick.TryPick("school:scad", Scad0, Scad1, 20, out FishAssetPick.Pick p));
            Assert.AreEqual(Scad0, p.Url);
            Assert.IsFalse(p.IsLod1);
            Assert.AreEqual(6650, p.Tris);

            // The barracuda's own boundary: 66 × 3,029 = 199,914, a whisker inside the budget…
            Assert.IsTrue(FishAssetPick.TryPick("school:barracuda", Barra0, Barra1, 66, out FishAssetPick.Pick b));
            Assert.IsFalse(b.IsLod1);
            // …and one fish more (202,943) is over it.
            Assert.IsTrue(FishAssetPick.TryPick("school:barracuda", Barra0, Barra1, 67, out FishAssetPick.Pick c));
            Assert.IsTrue(c.IsLod1);
        }

        [Test]
        public void UnknownSpecies_NoPick_SoTheProceduralMeshStays()
        {
            Assert.IsFalse(FishAssetPick.TryPick("school:mystery", Scad0, Scad1, 120, out FishAssetPick.Pick p));
            Assert.IsNull(p.Url);
            Assert.IsFalse(FishAssetPick.TryPick(null, Scad0, Scad1, 120, out _));
            Assert.IsFalse(FishAssetPick.TryPick("", Scad0, Scad1, 120, out _));
        }

        [Test]
        public void SpeciesIdIsCaseAndWhitespaceTolerant()
        {
            // Whatever the LOD rule decides, a padded/miscased id must decide the SAME thing —
            // asserting a fixed URL here made this test fail the day the models changed, which is
            // not what it is for.
            Assert.IsTrue(FishAssetPick.TryPick("  School:Scad ", Scad0, Scad1, 120, out FishAssetPick.Pick p));
            Assert.IsTrue(FishAssetPick.TryPick("school:scad", Scad0, Scad1, 120, out FishAssetPick.Pick canonical));
            Assert.AreEqual(canonical.Url, p.Url);
            Assert.AreEqual(canonical.IsLod1, p.IsLod1);
            Assert.AreEqual(canonical.Tris, p.Tris);
            Assert.AreEqual(canonical.LocalLen, p.LocalLen, 0.0001f);
        }

        [Test]
        public void NoUrls_NoPick()
        {
            Assert.IsFalse(FishAssetPick.TryPick("school:scad", null, null, 120, out _));
            Assert.IsFalse(FishAssetPick.TryPick("school:scad", "   ", "", 120, out _));
        }

        [Test]
        public void MissingLod0_FallsBackToLod1_AndViceVersa()
        {
            Assert.IsTrue(FishAssetPick.TryPick("pod:yellowtail", null, Trev1, 12, out FishAssetPick.Pick a));
            Assert.AreEqual(Trev1, a.Url);
            Assert.IsTrue(a.IsLod1);

            // Over budget, but no LOD1 shipped → LOD0 rather than no fish at all.
            Assert.IsTrue(FishAssetPick.TryPick("pod:yellowtail", Trev0, null, 50, out FishAssetPick.Pick b));
            Assert.AreEqual(Trev0, b.Url);
            Assert.IsFalse(b.IsLod1);
            Assert.AreEqual(8000, b.Tris);
        }
    }
}
