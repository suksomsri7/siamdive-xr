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
        public void Scad_KeepsLod0_BecauseItsLod1IsNoLighter()
        {
            // Re-measured 2 ส.ค.: 3,000 tris, up from the 670 that shipped before. The source is
            // 3,000 too, so LOD1 cannot be smaller — and a swap that fetches another file to draw
            // the same triangles is pure loss.
            Assert.IsTrue(FishAssetPick.TryPick("school:scad", Scad0, Scad1, 120, out FishAssetPick.Pick p));
            Assert.AreEqual(Scad0, p.Url);
            Assert.IsFalse(p.IsLod1);
            Assert.AreEqual(3000, p.Tris);
            Assert.AreEqual(1.911f, p.LocalLen, 0.001f);
        }

        [Test]
        public void Barracuda_KeepsLod0()
        {
            Assert.IsTrue(FishAssetPick.TryPick("school:barracuda", Barra0, Barra1, 160, out FishAssetPick.Pick p));
            Assert.AreEqual(Barra0, p.Url);
            Assert.IsFalse(p.IsLod1);
            Assert.AreEqual(3000, p.Tris);
            Assert.AreEqual(1.862f, p.LocalLen, 0.001f);
        }

        [Test]
        public void Yellowtail_RealDemoPodOf50_TakesLod1()
        {
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
        public void LightModels_NeverSwapDown_HoweverBigTheSchool()
        {
            // 500 × 3,000 = 1.5M is far over the raw budget, and LOD1 is 3,000 as well —
            // the rule must not trade a network round trip for zero triangles saved.
            Assert.IsTrue(FishAssetPick.TryPick("school:scad", Scad0, Scad1, 500, out FishAssetPick.Pick p));
            Assert.AreEqual(Scad0, p.Url);
            Assert.IsFalse(p.IsLod1);
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
            Assert.IsTrue(FishAssetPick.TryPick("  School:Scad ", Scad0, Scad1, 120, out FishAssetPick.Pick p));
            Assert.AreEqual(Scad0, p.Url);
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
