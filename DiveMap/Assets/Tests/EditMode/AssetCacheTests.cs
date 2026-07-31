using System.Collections.Generic;
using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// Unit tests for the offline model cache.
    ///
    /// Two failures here are silent and expensive. A name collision serves one map's model in
    /// another map's place — which reads as corrupt data, not as a cache fault, and nobody looks
    /// at the cache. A broken budget fills the user's phone; they uninstall rather than report it.
    /// Both are cheap to pin down here and nearly impossible to notice in a screenshot.
    /// </summary>
    public class AssetCacheTests
    {
        private static AssetCache.Entry E(string key, long bytes, long used)
            => new AssetCache.Entry { Key = key, Bytes = bytes, LastUsed = used };

        // ── naming ───────────────────────────────────────────────────────────────

        [Test]
        public void TwoModelsCalledTheSameThingGetDifferentFiles()
        {
            // THE collision test. Every module on the CDN is free to be called model.glb.
            string a = AssetCache.KeyFor("https://cdn.example.com/a/model.glb");
            string b = AssetCache.KeyFor("https://cdn.example.com/b/model.glb");
            Assert.AreNotEqual(a, b, "same filename, different model — these must not share a file");
        }

        [Test]
        public void TheSameUrlAlwaysGivesTheSameFile()
        {
            // Otherwise nothing is ever a cache hit and the "cache" is a download folder.
            const string url = "https://cdn.example.com/models/wreck_chang.glb";
            Assert.AreEqual(AssetCache.KeyFor(url), AssetCache.KeyFor(url));
            Assert.AreEqual(AssetCache.KeyFor(url), AssetCache.KeyFor("  " + url + "  "), "trimmed");
        }

        [Test]
        public void AKeyIsSafeToUseAsAFileName()
        {
            // A URL is outside input. "../" in a name walks out of the cache folder, and on a
            // phone that means writing wherever the app can write.
            string key = AssetCache.KeyFor("https://x.test/../../etc/passwd?a=b&c=d#frag");
            Assert.IsFalse(key.Contains("/"), key);
            Assert.IsFalse(key.Contains("\\"), key);
            Assert.IsFalse(key.Contains(".."), key);
            Assert.IsFalse(key.Contains("?"), key);
            Assert.IsFalse(key.Contains("#"), key);
        }

        [Test]
        public void AKeyStaysReadableSoTheFolderCanBeInspected()
        {
            string key = AssetCache.KeyFor("https://cdn.example.com/models/wreck_chang_xr0.glb");
            StringAssert.Contains("wreck_chang", key, "a person should be able to see what this is");
        }

        [Test]
        public void QueryStringsDoNotChangeTheReadablePart_ButDoChangeTheKey()
        {
            // Versioned URLs (?v=3) are a DIFFERENT file and must not overwrite the old one while
            // something is still using it.
            string v1 = AssetCache.KeyFor("https://x.test/m.glb?v=1");
            string v2 = AssetCache.KeyFor("https://x.test/m.glb?v=2");
            Assert.AreNotEqual(v1, v2);
            StringAssert.Contains("m.glb", v1);
        }

        [Test]
        public void NonsenseUrlsGetNoKey()
        {
            Assert.IsNull(AssetCache.KeyFor(null));
            Assert.IsNull(AssetCache.KeyFor("   "));
        }

        [Test]
        public void OnlyRemoteFilesAreWorthCaching()
        {
            Assert.IsTrue(AssetCache.IsCacheable("https://cdn.example.com/a.glb"));
            Assert.IsTrue(AssetCache.IsCacheable("http://cdn.example.com/a.glb"));
            Assert.IsFalse(AssetCache.IsCacheable("file:///data/a.glb"), "already on the device");
            Assert.IsFalse(AssetCache.IsCacheable("/data/a.glb"));
            Assert.IsFalse(AssetCache.IsCacheable(null));
        }

        // ── the budget ───────────────────────────────────────────────────────────

        [Test]
        public void UnderBudgetNothingIsDeleted()
        {
            var all = new List<AssetCache.Entry> { E("a", 10, 1), E("b", 20, 2) };
            CollectionAssert.IsEmpty(AssetCache.PlanEviction(all, 100));
        }

        [Test]
        public void OverBudgetTheLeastRecentlyUsedGoesFirst()
        {
            var all = new List<AssetCache.Entry>
            {
                E("new", 60, 300),
                E("old", 60, 100),
                E("mid", 60, 200),
            };
            // 180 stored, budget 100: dropping "old" leaves 120 — still over — so "mid" goes too,
            // and then it stops at 60. (The first version of this test asked for a budget of 130,
            // where ONE eviction is already enough, and called the correct answer a failure.)
            List<AssetCache.Entry> plan = AssetCache.PlanEviction(all, 100);

            Assert.AreEqual(2, plan.Count, "180 → 120 → 60; two must go to get under 100");
            Assert.AreEqual("old", plan[0].Key, "least recently used first");
            Assert.AreEqual("mid", plan[1].Key);
        }

        [Test]
        public void ItStopsAsSoonAsItIsUnderBudget()
        {
            // Evicting more than needed throws away downloads the user paid for in data.
            var all = new List<AssetCache.Entry> { E("a", 50, 1), E("b", 50, 2), E("c", 50, 3) };
            List<AssetCache.Entry> plan = AssetCache.PlanEviction(all, 120);
            Assert.AreEqual(1, plan.Count);
            Assert.AreEqual("a", plan[0].Key);
        }

        [Test]
        public void EvictionOrderIsStableWhenTimesTie()
        {
            // Fresh installs stamp everything with the same second. Without a tiebreak the plan
            // varies run to run, and a bug that only shows sometimes is the worst kind.
            var all = new List<AssetCache.Entry> { E("b", 50, 7), E("a", 50, 7), E("c", 50, 7) };
            List<AssetCache.Entry> first = AssetCache.PlanEviction(all, 100);
            List<AssetCache.Entry> again = AssetCache.PlanEviction(all, 100);
            Assert.AreEqual(first[0].Key, again[0].Key);
            Assert.AreEqual("a", first[0].Key, "alphabetical is the tiebreak");
        }

        [Test]
        public void TheDefaultBudgetIsTheShippedAppsBudget()
        {
            // 220 MB comes from siamdive-rn/src/lib/offline/assets.ts:16 — a number proven on real
            // phones. Changing it should be a decision, not a drift.
            Assert.AreEqual(220L * 1024L * 1024L, AssetCache.BudgetBytes);
        }

        [Test]
        public void EmptyAndNullAreHandled()
        {
            CollectionAssert.IsEmpty(AssetCache.PlanEviction(null, 10));
            CollectionAssert.IsEmpty(AssetCache.PlanEviction(new List<AssetCache.Entry>(), 10));
            Assert.AreEqual(0, AssetCache.TotalBytes(null));
        }

        [Test]
        public void NegativeSizesCannotShrinkTheTotal()
        {
            // A failed write can leave a nonsense length; treating it as negative would let the
            // cache believe it has room it does not have.
            Assert.AreEqual(10, AssetCache.TotalBytes(new List<AssetCache.Entry> { E("a", 10, 1), E("b", -99, 2) }));
        }

        // ── the index ────────────────────────────────────────────────────────────

        [Test]
        public void TheIndexSurvivesARoundTrip()
        {
            var all = new List<AssetCache.Entry> { E("a1", 100, 5), E("b2", 200, 6) };
            List<AssetCache.Entry> back = AssetCache.Decode(AssetCache.Encode(all));

            Assert.AreEqual(2, back.Count);
            Assert.AreEqual("a1", back[0].Key);
            Assert.AreEqual(100, back[0].Bytes);
            Assert.AreEqual(5, back[0].LastUsed);
        }

        [Test]
        public void ABadRowIsSkipped_NotFatal()
        {
            // This index is read at startup. If a damaged row could throw, a corrupt cache would
            // stop the app from opening at all — far worse than losing one cached model.
            List<AssetCache.Entry> back = AssetCache.Decode("good:10:1\ngarbage\nx:notanumber:2\n\nalso:20:3");
            Assert.AreEqual(2, back.Count);
            Assert.AreEqual("good", back[0].Key);
            Assert.AreEqual("also", back[1].Key);
        }

        [Test]
        public void AKeyContainingTheSeparatorIsRefused()
        {
            // Writing it would produce a row that decodes as something else entirely.
            Assert.AreEqual("", AssetCache.Encode(new List<AssetCache.Entry> { E("a:b", 1, 1) }));
        }

        [Test]
        public void EmptyIndexDecodesToNothing()
        {
            CollectionAssert.IsEmpty(AssetCache.Decode(null));
            CollectionAssert.IsEmpty(AssetCache.Decode(""));
        }

        // ── the size shown to the user ───────────────────────────────────────────

        [Test]
        public void SizesReadTheWayAPersonWouldSayThem()
        {
            Assert.AreEqual("512 B", AssetCache.FormatSize(512));
            Assert.AreEqual("1.5 KB", AssetCache.FormatSize(1536));
            Assert.AreEqual("2 MB", AssetCache.FormatSize(2 * 1024 * 1024));
            Assert.AreEqual("1.5 GB", AssetCache.FormatSize(1536L * 1024 * 1024));
            Assert.AreEqual("0 B", AssetCache.FormatSize(-5));
        }
    }
}
