using System.Collections.Generic;
using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// Pins the texture-tier policy (<see cref="TexTiers"/>) — the class that exists because
    /// builds 282/298 loaded whatever the manifest named and iOS killed the app. Every branch
    /// here is a promise to the one calibration we own: Atlantis 829 MB dead, 561 MB alive,
    /// Posidon 940 MB alive, all on the same phone.
    /// </summary>
    public class TexTiersTests
    {
        private const long MB = 1024L * 1024L;

        private static TexTiers.Entry E(string id, long k1, long k2, long k4) => new TexTiers.Entry
        {
            Id = id,
            Urls = new[] { id + "_k1.glb", id + "_k2.glb", id + "_r0p.glb" },
            Vram = new[] { k1, k2, k4 },
        };

        /// <summary>A 4-slot hero: the real ladder numbers (22 / 89 / 224 MB).</summary>
        private static TexTiers.Entry Hero(string id) => E(id, 22 * MB, 89 * MB, 224 * MB);

        [Test]
        public void Budget_StepsWithDeviceRam()
        {
            Assert.That(TexTiers.BudgetBytes(8192), Is.EqualTo(950 * MB));
            Assert.That(TexTiers.BudgetBytes(6144), Is.EqualTo(750 * MB));
            Assert.That(TexTiers.BudgetBytes(4096), Is.EqualTo(550 * MB));
            Assert.That(TexTiers.BudgetBytes(3072), Is.EqualTo(320 * MB));
            // Boundary rows, exactly at the seam.
            Assert.That(TexTiers.BudgetBytes(7168), Is.EqualTo(950 * MB));
            Assert.That(TexTiers.BudgetBytes(7167), Is.EqualTo(750 * MB));
        }

        [Test]
        public void SmallMap_GetsTheMobileCap_NotK4()
        {
            // 8 ส.ค.: วิดีโอเครื่องจริงพิสูจน์ k4 ทั้งแมพ = ~15fps (แบนด์วิดท์ GPU) —
            // เพดานมือถือคือ K2 แม้งบแรมเหลือเฟือ · k4 ต้องขอ explicit (XR/inspect)
            var plan = TexTiers.Choose(new List<TexTiers.Entry> { Hero("a"), Hero("b") },
                                       950 * MB, 0);
            Assert.That(plan.BaseTier, Is.EqualTo(TexTiers.K2));
            Assert.That(plan.Url["a"], Does.EndWith("_k2.glb"));
            Assert.That(plan.OverBudget, Is.False);

            // เส้นทาง XR ในอนาคต: ส่ง maxTier=K4 ตรงๆ ยังไปถึง k4 ได้
            var xr = TexTiers.Choose(new List<TexTiers.Entry> { Hero("a"), Hero("b") },
                                     950 * MB, 0, TexTiers.K4);
            Assert.That(xr.BaseTier, Is.EqualTo(TexTiers.K4));
        }

        [Test]
        public void HeavyMap_DropsToTheTierThatFits()
        {
            // Atlantis shape: 20 heroes. k4 = 4480 MB, k2 = 1780 MB, k1 = 440 MB.
            var entries = new List<TexTiers.Entry>();
            for (int i = 0; i < 20; i++) entries.Add(Hero("m" + i.ToString("D2")));

            var plan = TexTiers.Choose(entries, 550 * MB, 0);
            Assert.That(plan.BaseTier, Is.EqualTo(TexTiers.K1), "only all-k1 (440) fits 550");
            Assert.That(plan.OverBudget, Is.False);
            Assert.That(plan.TotalBytes, Is.LessThanOrEqualTo(550 * MB));
        }

        [Test]
        public void Remainder_UpgradesBiggestHeroFirst()
        {
            // Two heroes and one small model, 320 MB budget. Base = k1 (22+22+2 = 46 MB).
            // Upgrades big-first: hero "big" reaches k4 (+202 → 248), "big2" k1→k2 fails at k4
            // (+202 → 450 > 320) but fits k2 (+67 → 315). The pebble stays k1.
            var entries = new List<TexTiers.Entry>
            {
                Hero("big"), Hero("big2"), E("pebble", 2 * MB, 2 * MB, 2 * MB),
            };
            var plan = TexTiers.Choose(entries, 320 * MB, 0);

            // เพดาน K2: ฮีโร่ตัวใหญ่สุดได้ K2 (ไม่ใช่ K4 อีกแล้ว) — งบเหลือพออัปทั้งคู่
            Assert.That(plan.Tier["big"], Is.EqualTo(TexTiers.K2));
            Assert.That(plan.Tier["big2"], Is.EqualTo(TexTiers.K2));
            Assert.That(plan.TotalBytes, Is.LessThanOrEqualTo(320 * MB));
        }

        [Test]
        public void OverBudget_TakesTheFloorAndSaysSo()
        {
            // Even all-k1 exceeds the budget: load anyway (a soft map beats no map), flag it,
            // and spend nothing on upgrades.
            var entries = new List<TexTiers.Entry>();
            for (int i = 0; i < 20; i++) entries.Add(Hero("m" + i.ToString("D2")));

            var plan = TexTiers.Choose(entries, 100 * MB, 0);
            Assert.That(plan.OverBudget, Is.True);
            Assert.That(plan.BaseTier, Is.EqualTo(TexTiers.K1));
            Assert.That(plan.Upgraded, Is.Zero);
            foreach (var kv in plan.Tier) Assert.That(kv.Value, Is.EqualTo(TexTiers.K1));
        }

        [Test]
        public void FixedBytes_ShrinkTheBudget()
        {
            // One hero, budget 250 MB: k4 (224) fits alone, but 30 MB of non-laddered
            // reservations pushes it down to k2.
            var plan = TexTiers.Choose(new List<TexTiers.Entry> { Hero("a") },
                                       250 * MB, 30 * MB);
            Assert.That(plan.BaseTier, Is.EqualTo(TexTiers.K2));
        }

        [Test]
        public void InvalidOrEmptyEntries_PlanNothing()
        {
            var plan = TexTiers.Choose(new List<TexTiers.Entry>
            {
                new TexTiers.Entry { Id = "broken", Urls = new string[3], Vram = new long[3] },
            }, 950 * MB, 0);
            Assert.That(plan.Url, Is.Empty);

            plan = TexTiers.Choose(null, 950 * MB, 0);
            Assert.That(plan.Url, Is.Empty);
        }

        [Test]
        public void UrlFor_AnswersOnlyWhileAPlanIsParked()
        {
            TexTiers.Clear();
            Assert.That(TexTiers.UrlFor("a"), Is.Null);

            var plan = TexTiers.Choose(new List<TexTiers.Entry> { Hero("a") }, 950 * MB, 0);
            TexTiers.SetPlan(plan);
            Assert.That(TexTiers.UrlFor("a"), Does.EndWith("_k2.glb"));
            Assert.That(TexTiers.UrlFor("missing"), Is.Null);

            TexTiers.Clear();
            Assert.That(TexTiers.UrlFor("a"), Is.Null);
        }

        [Test]
        public void Alias_NeverUpgradesDownward()
        {
            // A 512-texture model whose three tiers are the same file (alias): delta is 0,
            // the "upgrade" is free and harmless — but a mis-ordered ladder (k4 < k1) must
            // never subtract from the running total.
            var entries = new List<TexTiers.Entry>
            {
                E("alias", 2 * MB, 2 * MB, 2 * MB),
                E("weird", 30 * MB, 20 * MB, 10 * MB),   // nonsense ladder
            };
            // งบ 40MB (เพดาน K2): base K2 = 2+20 = 22MB — ห้ามมี "อัปเกรดลง" ไปคว้า k4
            // ราคาถูกกว่า (10MB) แม้จะถูกกว่า เพราะลำดับ tier ต้องเดินขึ้นเท่านั้น
            var plan = TexTiers.Choose(entries, 40 * MB, 0);
            Assert.That(plan.TotalBytes, Is.EqualTo(22 * MB));
            Assert.That(plan.Tier["weird"], Is.EqualTo(TexTiers.K2));
        }
    }
}
