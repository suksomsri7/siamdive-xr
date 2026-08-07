using System.Collections.Generic;
using DiveMap.Core;
using NUnit.Framework;
using UnityEngine;

namespace DiveMap.Tests
{
    /// <summary>
    /// Unit tests for ItemPicker (WO-XR-05.3).
    ///
    /// The two things that historically break here are (a) splitting the GameObject name
    /// on the wrong underscore — assetIds like "cc0:wreck_chang" contain one — and (b) a
    /// slab test that reports boxes BEHIND the camera as hits, which makes taps select
    /// whatever happens to be behind you. Both are pinned below.
    /// </summary>
    public class ItemPickerTests
    {
        // ── ParseItemName ────────────────────────────────────────────────────────

        [Test]
        public void ParseItemName_KeepsUnderscoresAndColonsInTheAssetId()
        {
            Assert.IsTrue(ItemPicker.ParseItemName("Item_mef2q6k18z591_cc0:wreck_chang",
                                                   out string id, out string assetId));
            Assert.AreEqual("mef2q6k18z591", id);
            Assert.AreEqual("cc0:wreck_chang", assetId);
        }

        [Test]
        public void ParseItemName_HandlesTheRealDemoMapNames()
        {
            var cases = new Dictionary<string, string>
            {
                { "Item_m333f0b6z6kn6_school:scad",    "school:scad" },
                { "Item_mvc5xvrm0ai9k_pod:yellowtail", "pod:yellowtail" },
                { "Item_msa7kk5hxf44l_msh:whaleshark", "msh:whaleshark" },
                { "Item_muykwzniw35qg_warp:0",         "warp:0" },
                { "Item_abc_cc0:artificial_reef_ball", "cc0:artificial_reef_ball" },
            };

            foreach (KeyValuePair<string, string> kv in cases)
            {
                Assert.IsTrue(ItemPicker.ParseItemName(kv.Key, out _, out string assetId), kv.Key);
                Assert.AreEqual(kv.Value, assetId, kv.Key);
            }
        }

        [Test]
        public void ParseItemName_RejectsAnythingThatIsNotAnItem()
        {
            Assert.IsFalse(ItemPicker.ParseItemName(null, out _, out _));
            Assert.IsFalse(ItemPicker.ParseItemName("", out _, out _));
            Assert.IsFalse(ItemPicker.ParseItemName("Map", out _, out _));
            Assert.IsFalse(ItemPicker.ParseItemName("Seabed", out _, out _));
            Assert.IsFalse(ItemPicker.ParseItemName("Item_", out _, out _));
            Assert.IsFalse(ItemPicker.ParseItemName("Item_onlyid", out _, out _));
            Assert.IsFalse(ItemPicker.ParseItemName("Item__noid", out _, out _));
            Assert.IsFalse(ItemPicker.ParseItemName("Item_id_", out _, out _));
        }

        [Test]
        public void IsItemName_MatchesThePrefixOnly()
        {
            Assert.IsTrue(ItemPicker.IsItemName("Item_a_b"));
            Assert.IsFalse(ItemPicker.IsItemName("Water"));
            Assert.IsFalse(ItemPicker.IsItemName(null));
        }

        // ── depth ────────────────────────────────────────────────────────────────

        [Test]
        public void UnitsPerMetre_MatchesTheWebBuilder()
        {
            // builder.html L600: const U_PER_M = 6;
            Assert.AreEqual(6.0, ItemPicker.UnitsPerMetre, 1e-9);
        }

        [Test]
        public void DepthMetres_MatchesTheWebFormula()
        {
            // Htms Chang demo: env.waterLevel = 240, wreck sits at y = 0 → 40.0 m.
            Assert.AreEqual(40.0, ItemPicker.DepthMetres(240.0, 0.0), 1e-9);
            Assert.AreEqual(20.0, ItemPicker.DepthMetres(240.0, 120.0), 1e-9);
        }

        [Test]
        public void DepthMetres_IsClampedLikeTheWeb()
        {
            // Above the surface → 0, never negative.
            Assert.AreEqual(0.0, ItemPicker.DepthMetres(240.0, 300.0), 1e-9);
            // builder.html clamps the readout at 100 m.
            Assert.AreEqual(100.0, ItemPicker.DepthMetres(6000.0, 0.0), 1e-9);
        }

        // ── ray / AABB ───────────────────────────────────────────────────────────

        private static ItemPicker.Target Box(string key, Vector3 centre, float half)
        {
            var h = new Vector3(half, half, half);
            return new ItemPicker.Target(key, centre - h, centre + h);
        }

        [Test]
        public void RayAabb_HitsAndReportsTheEntryDistance()
        {
            Assert.IsTrue(ItemPicker.RayAabb(Vector3.zero, Vector3.right,
                                             new Vector3(9f, -1f, -1f), new Vector3(11f, 1f, 1f),
                                             out float t));
            Assert.AreEqual(9f, t, 1e-4f);
        }

        [Test]
        public void RayAabb_OriginInsideTheBoxIsAHitAtZero()
        {
            Assert.IsTrue(ItemPicker.RayAabb(Vector3.zero, Vector3.forward,
                                             new Vector3(-1f, -1f, -1f), new Vector3(1f, 1f, 1f),
                                             out float t));
            Assert.AreEqual(0f, t, 1e-6f);
        }

        [Test]
        public void RayAabb_IgnoresBoxesBehindTheOrigin()
        {
            Assert.IsFalse(ItemPicker.RayAabb(Vector3.zero, Vector3.right,
                                              new Vector3(-11f, -1f, -1f), new Vector3(-9f, 1f, 1f),
                                              out _));
        }

        [Test]
        public void RayAabb_ParallelRayOutsideTheSlabMisses()
        {
            // Travelling along +X at y = 5 while the box only spans y ∈ [-1, 1].
            Assert.IsFalse(ItemPicker.RayAabb(new Vector3(0f, 5f, 0f), Vector3.right,
                                              new Vector3(9f, -1f, -1f), new Vector3(11f, 1f, 1f),
                                              out _));
        }

        // ── Pick ─────────────────────────────────────────────────────────────────

        [Test]
        public void Pick_ReturnsTheNearestIntersectedBox()
        {
            var targets = new List<ItemPicker.Target>
            {
                Box("far",  new Vector3(30f, 0f, 0f), 2f),
                Box("near", new Vector3(10f, 0f, 0f), 2f),
                Box("side", new Vector3(10f, 50f, 0f), 2f),
            };

            string key = ItemPicker.Pick(Vector3.zero, Vector3.right, targets, out float d);
            Assert.AreEqual("near", key);
            Assert.AreEqual(8f, d, 1e-4f);
        }

        [Test]
        public void Pick_ReturnsNullWhenTheRayMissesEverything()
        {
            var targets = new List<ItemPicker.Target>
            {
                Box("a", new Vector3(10f, 0f, 0f), 2f),
                Box("b", new Vector3(30f, 0f, 0f), 2f),
            };

            Assert.IsNull(ItemPicker.Pick(Vector3.zero, Vector3.up, targets));
            Assert.IsNull(ItemPicker.Pick(Vector3.zero, Vector3.left, targets));
        }

        [Test]
        public void Pick_HandlesEmptyAndNullInput()
        {
            Assert.IsNull(ItemPicker.Pick(Vector3.zero, Vector3.right, null));
            Assert.IsNull(ItemPicker.Pick(Vector3.zero, Vector3.right, new List<ItemPicker.Target>()));
        }

        [Test]
        public void SphereTarget_BuildsABoxAroundTheCentre()
        {
            // The fallback volume for renderer-less items (instanced fish schools).
            ItemPicker.Target t = ItemPicker.Target.Sphere("school", new Vector3(0f, 100f, 0f), 66f);
            Assert.AreEqual(new Vector3(-66f, 34f, -66f), t.Min);
            Assert.AreEqual(new Vector3(66f, 166f, 66f), t.Max);
            Assert.IsNotNull(ItemPicker.Pick(new Vector3(0f, 100f, -500f), Vector3.forward,
                                             new[] { t }));
        }

        // ── kind labels ──────────────────────────────────────────────────────────

        [Test]
        public void KindLabel_PrefersTheAssetIdWhenItIsMoreSpecific()
        {
            // The web files trees, statues and shipwrecks alike under kind WRECK.
            Assert.AreEqual("ซากเรือ", ItemPicker.KindLabel("WRECK", "cc0:wreck_chang"));
            Assert.AreEqual("พืช", ItemPicker.KindLabel("WRECK", "nat:tree"));
            Assert.AreEqual("ฝูงปลา", ItemPicker.KindLabel(null, "school:scad"));
            Assert.AreEqual("ฝูงปลา", ItemPicker.KindLabel(null, "pod:yellowtail"));
            Assert.AreEqual("ประตูวาป", ItemPicker.KindLabel(null, "warp:0"));
        }

        [Test]
        public void KindLabel_FallsBackToTheManifestKind()
        {
            Assert.AreEqual("ปะการัง", ItemPicker.KindLabel("CORAL", "cor:amber_tree"));
            Assert.AreEqual("หิน", ItemPicker.KindLabel("ROCK", "rock:0"));
            Assert.AreEqual("สัตว์ทะเล", ItemPicker.KindLabel("MARINE_LIFE", "msh:whaleshark"));
            Assert.AreEqual("เต่า", ItemPicker.KindLabel("turtle", "turtle:0")); // case-insensitive
            Assert.AreEqual("อื่นๆ", ItemPicker.KindLabel("SOMETHING_NEW", "x:y"));
            Assert.AreEqual("อื่นๆ", ItemPicker.KindLabel(null, null));
        }

        [Test]
        public void KindLabel_AlwaysReturnsATranslatableKey()
        {
            string[] kinds =
            {
                "ROCK", "CORAL", "BOAT", "MARINE_LIFE", "SCHOOL", "ARTIFICIAL", "WRECK",
                "DIVER", "SPECIAL", "ANEMONE", "FISH", "TURTLE", "PLANT", "TERRAIN",
                "OTHER", "UNKNOWN_KIND", null,
            };

            foreach (string kind in kinds)
            {
                string label = ItemPicker.KindLabel(kind, "msh:whatever");
                Assert.IsTrue(UiStrings.ContainsThai(label), $"kind {kind} → '{label}' is not Thai");
                Assert.IsFalse(UiStrings.ContainsThai(UiStrings.Tr(label, UiStrings.English)),
                    $"kind {kind} → '{label}' has no English translation");
            }
        }
    
        // ── PickBest: "เล็งใครมากที่สุด" (บั๊ก Chang: แตะบาราคูด้าได้ปลาข้างเหลือง) ──

        [Test]
        public void PickBest_AimedCentre_BeatsNearerGrazedEdge()
        {
            // ฝูง scad ใกล้กล้อง แต่ ray เฉี่ยวขอบมัน · ฝูงบาราคูด้าไกลกว่า แต่ ray ผ่ากลาง
            var scad = new ItemPicker.Target("Item_1|school:scad",
                new Vector3(-12f, -12f, 8f), new Vector3(2f, 2f, 22f));      // ขอบเฉียด ray
            var barra = new ItemPicker.Target("Item_2|school:barracuda",
                new Vector3(-5f, -5f, 35f), new Vector3(5f, 5f, 45f));       // ศูนย์กลางบน ray
            string hit = ItemPicker.PickBest(Vector3.zero, new Vector3(0f, 0f, 1f),
                                             new[] { scad, barra });
            Assert.That(hit, Is.EqualTo("Item_2|school:barracuda"),
                "nearest-t เดิมเลือก scad ที่แค่เฉี่ยว — ต้องได้ตัวที่ผู้เล่นเล็งจริง");
        }

        [Test]
        public void PickBest_Occlusion_StopsAtTheSand()
        {
            var school = new ItemPicker.Target("Item_1|school:barracuda",
                new Vector3(-5f, -5f, 30f), new Vector3(5f, 5f, 40f));
            Assert.That(ItemPicker.PickBest(Vector3.zero, new Vector3(0f, 0f, 1f),
                                            new[] { school }, 20f), Is.Null,
                "เลยจุดชนพื้น = มองไม่เห็น = คลิกไม่ได้");
        }
}
}
