using System.Collections.Generic;
using System.IO;
using DiveMap.Core;
using DiveMap.Runtime;
using NUnit.Framework;
using UnityEngine;

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
            => new AssetCache.Entry { Key = key, Bytes = bytes, LastUsed = used, Gen = AssetCache.Generation };

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
            // Raised from 220 MB (siamdive-rn/src/lib/offline/assets.ts:16) to 1 GB on 2026-08-06,
            // because the ASTC file set makes a model ~16 MB where it was 2-3 MB and the old cap
            // could not hold one map. Pinned so that changing it stays a decision, not a drift.
            Assert.AreEqual(1024L * 1024L * 1024L, AssetCache.BudgetBytes);
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

        // ── generations: getting a repaired file onto a phone that already has the old one ──
        //
        // 🔴 The bug these pin down shipped and was invisible from every angle we look from. 444
        // GLBs were repaired ON THE CDN under their existing URLs — verified clean there, 0 black
        // triangles — and users kept seeing black, because the cache is keyed by URL and a file on
        // disk was served without ever being questioned. "The CDN is fixed" and "the user sees the
        // fix" turned out to be different claims.

        [Test]
        public void AnIndexRowWrittenBeforeGenerationsExistedIsNotFresh()
        {
            // THE test. Every device in the field right now has three-column rows, and every one
            // of those rows points at a pre-repair GLB. If this ever returns true again, the
            // repaired models stop reaching anyone who already opened the map once.
            List<AssetCache.Entry> old = AssetCache.Decode("f0a1b2c3_wreck.glb:1000:12345");

            Assert.AreEqual(1, old.Count, "a legacy row must still be READ — it is the offline copy");
            Assert.AreEqual(0, old[0].Gen, "no generation column means the oldest generation");
            Assert.IsFalse(AssetCache.IsFresh(old[0].Gen), "…and must not be served as current");
        }

        [Test]
        public void ALegacyRowIsKept_NotDiscarded()
        {
            // The tempting shortcut is to drop rows we cannot fully parse, which would empty every
            // existing device's index in one step. That is the "no models, no signal" outage this
            // cache exists to prevent — the file must stay reachable as a fallback, just untrusted.
            List<AssetCache.Entry> old = AssetCache.Decode("a:2048:99");
            Assert.AreEqual(1, old.Count);
            Assert.AreEqual(2048, old[0].Bytes, "still counted against the budget");
            Assert.AreEqual(99, old[0].LastUsed, "still has its place in the eviction order");
        }

        [Test]
        public void AFileThisBuildDownloadedIsFresh()
        {
            Assert.IsTrue(AssetCache.IsFresh(AssetCache.Generation));
        }

        [Test]
        public void AFileFromANewerBuildIsAlsoFresh()
        {
            // TestFlight installs backwards as well as forwards. If a rollback treated newer files
            // as stale it would re-download them, stamp them older, and hand the newer build the
            // same work back on the next launch — a loop that burns a boat's mobile data to
            // arrive at the bytes it already had.
            Assert.IsTrue(AssetCache.IsFresh(AssetCache.Generation + 1));
            Assert.IsFalse(AssetCache.IsFresh(AssetCache.Generation - 1));
        }

        [Test]
        public void TheGenerationSurvivesARoundTrip()
        {
            // Written but not read back = every launch re-downloads everything, which looks like
            // the cache working (no black triangles) while costing the data of having none.
            var all = new List<AssetCache.Entry>
            {
                new AssetCache.Entry { Key = "cur", Bytes = 10, LastUsed = 1, Gen = AssetCache.Generation },
                new AssetCache.Entry { Key = "old", Bytes = 20, LastUsed = 2, Gen = 0 },
            };
            List<AssetCache.Entry> back = AssetCache.Decode(AssetCache.Encode(all));

            Assert.AreEqual(2, back.Count);
            Assert.IsTrue(AssetCache.IsFresh(back[0].Gen), "the one we just wrote");
            Assert.IsFalse(AssetCache.IsFresh(back[1].Gen), "the one from before the repair");
        }

        [Test]
        public void ARowWithAnUnreadableGenerationIsSkipped_NotFatal()
        {
            // Same rule as every other column: a damaged index costs one model, never the app.
            List<AssetCache.Entry> back = AssetCache.Decode("good:10:1:0\nbad:10:1:x\nalso:20:3:1");
            Assert.AreEqual(2, back.Count);
            Assert.AreEqual("good", back[0].Key);
            Assert.AreEqual("also", back[1].Key);
        }

        [Test]
        public void ANegativeGenerationCannotForgeFreshness()
        {
            List<AssetCache.Entry> back = AssetCache.Decode("a:10:1:-7");
            Assert.AreEqual(0, back[0].Gen);
            Assert.IsFalse(AssetCache.IsFresh(back[0].Gen));
        }

        [Test]
        public void EvictionStillIgnoresGeneration()
        {
            // Deliberate, and worth a test so it is not "re-fixed" later: eviction stays pure LRU.
            // Sorting stale files out first sounds free, but a stale file can be the ONLY copy of a
            // model, and dropping it to keep a fresh one for a map the user never opens is how the
            // offline tour breaks. Measured, it never comes up — see the budget test below.
            var all = new List<AssetCache.Entry>
            {
                new AssetCache.Entry { Key = "stale-but-recent", Bytes = 60, LastUsed = 300, Gen = 0 },
                new AssetCache.Entry { Key = "fresh-but-old",    Bytes = 60, LastUsed = 100, Gen = AssetCache.Generation },
            };
            List<AssetCache.Entry> plan = AssetCache.PlanEviction(all, 100);

            Assert.AreEqual(1, plan.Count);
            Assert.AreEqual("fresh-but-old", plan[0].Key, "least recently used, generation is not a factor");
        }

        [Test]
        public void OneMapFitsInsideTheBudgetManyTimesOver()
        {
            // The eviction-thrash question, answered with the real catalogue rather than a guess.
            // The whole XR set is 465.7 MB across 444 files, which is twice the cap and sounds
            // alarming — but nobody downloads the catalogue, they download a map. Measured against
            // the live API on 2026-08-02: the heaviest map is Posidon at 15.0 MB (14 distinct
            // models), T-13 is 11.5 MB despite its 494 objects (10 models, 394 of them one frame),
            // and ALL SIX public maps together come to 42.4 MB across 38 files.
            //
            // So a generation bump re-downloads ~15 MB for the map being opened, not 465 MB, and
            // eviction cannot fight the map that is loading — it would take five times the entire
            // public catalogue to reach the cap.
            const long biggestMapBytes = 15_728_640;   // 15.0 MB, Posidon
            const long allPublicMapsBytes = 44_459_622; // 42.4 MB, the six of them

            Assert.Less(biggestMapBytes, AssetCache.BudgetBytes / 10,
                        "one map must not come close to the cap, or loading it evicts itself");
            Assert.Less(allPublicMapsBytes, AssetCache.BudgetBytes,
                        "every public map at once still fits — eviction is not part of this story");
        }

        // ── "ไฟล์เสียครึ่งเดียว": bytes that arrived, but are not the file ───────
        //
        // 🔴 These matter BECAUSE of the generation bump, not despite it. Every device in the
        // field re-downloads its models exactly once now, and it does that wherever the user
        // happens to be — which for this app is a boat, on wifi that answers every GET with a
        // portal page and 200 OK. Cached, stamped with the current generation, such a file is
        // served forever: the generation gate cannot rescue it, because it is not stale.

        /// <summary>A minimal but structurally valid GLB: "glTF", version 2, length = the array.</summary>
        private static byte[] Glb(int totalLength, int declaredLength)
        {
            var b = new byte[totalLength];
            b[0] = 0x67; b[1] = 0x6C; b[2] = 0x54; b[3] = 0x46;   // "glTF"
            b[4] = 2;                                             // version 2
            b[8] = (byte)(declaredLength & 0xFF);
            b[9] = (byte)((declaredLength >> 8) & 0xFF);
            b[10] = (byte)((declaredLength >> 16) & 0xFF);
            b[11] = (byte)((declaredLength >> 24) & 0xFF);
            return b;
        }

        private const string GlbUrl = "https://siamdive-cdn.b-cdn.net/models/xr/wreck.glb";

        [Test]
        public void AWholeGlb_IsAccepted()
        {
            Assert.IsTrue(AssetCache.LooksComplete(GlbUrl, Glb(2048, 2048)));
        }

        [Test]
        public void AGlbCutOffMidDownload_IsRefused()
        {
            // The header still says 2048; only 900 bytes turned up. This is what a dropped mobile
            // connection looks like, and it is indistinguishable from a good file by size alone.
            Assert.IsFalse(AssetCache.LooksComplete(GlbUrl, Glb(900, 2048)));
        }

        [Test]
        public void ACaptivePortalPage_IsNotAModel()
        {
            // 🔴 The boat-wifi case. 200 OK, a few hundred bytes of HTML, and without this check it
            // becomes "the model" on that device until someone finds "clear downloads".
            byte[] html = System.Text.Encoding.UTF8.GetBytes(
                "<!DOCTYPE html><html><head><title>Sign in to WiFi</title></head><body></body></html>");
            Assert.IsFalse(AssetCache.LooksComplete(GlbUrl, html));
        }

        [Test]
        public void EmptyOrStumpyBytes_AreRefused()
        {
            Assert.IsFalse(AssetCache.LooksComplete(GlbUrl, null));
            Assert.IsFalse(AssetCache.LooksComplete(GlbUrl, new byte[0]));
            Assert.IsFalse(AssetCache.LooksComplete(GlbUrl, new byte[8]), "shorter than a GLB header");
            Assert.IsFalse(AssetCache.LooksComplete(GlbUrl, Glb(64, 0)), "a zero-length header is malformed");
        }

        [Test]
        public void TrailingPadding_IsStillAWholeFile()
        {
            // Conservative on purpose: this method must never be the reason a GOOD file fails to
            // cache. More bytes than declared is legal and harmless; fewer is the broken case.
            Assert.IsTrue(AssetCache.LooksComplete(GlbUrl, Glb(2100, 2048)));
        }

        [Test]
        public void AQueryStringDoesNotHideTheExtension()
        {
            Assert.IsFalse(AssetCache.LooksComplete(GlbUrl + "?v=3", new byte[64]));
            Assert.IsTrue(AssetCache.LooksComplete(GlbUrl + "?v=3", Glb(64, 64)));
        }

        [Test]
        public void TheHullJsonRidesTheSameCache_AndIsNotJudgedAsAGlb()
        {
            // .solids.json goes through Store() too. Holding it to the GLB header would stop every
            // hull ever being cached — "ว่ายทะลุรูไม่ได้" offline, for a check that was meant to help.
            string hull = "https://siamdive-cdn.b-cdn.net/models/xr/wreck.solids.json";
            byte[] json = System.Text.Encoding.UTF8.GetBytes("{\"boxes\":[]}");
            Assert.IsTrue(AssetCache.LooksComplete(hull, json));
            Assert.IsFalse(AssetCache.LooksComplete(hull, new byte[0]), "…but empty is still nothing");
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

    /// <summary>
    /// The GLUE, on a real disk and a real index — the half the pure tests above cannot reach.
    ///
    /// 🔴 The black-triangle bug lived exactly here. <see cref="AssetCache"/>'s rules were never
    /// wrong; <see cref="AssetCacheStore.Resolve"/> simply never asked them anything, so a file on
    /// disk was a hit forever. Pinning the decisions without pinning the lookup that consults them
    /// is how this shipped in the first place, so these four walk the paths a real device takes:
    /// a current copy, a copy from before the CDN was repaired, no signal, and a broken download.
    /// </summary>
    public class AssetCacheStoreTests
    {
        // A URL of its own, so a developer's real cache is never the thing under test.
        private const string Url = "https://siamdive-cdn.b-cdn.net/models/xr/__editmode_probe__.glb";

        private string _savedIndex;
        private bool _hadIndex;

        [SetUp]
        public void SetUp()
        {
            _hadIndex = PlayerPrefs.HasKey(AssetCache.IndexKey);
            _savedIndex = PlayerPrefs.GetString(AssetCache.IndexKey, "");
            RemoveFile();
        }

        [TearDown]
        public void TearDown()
        {
            RemoveFile();
            if (_hadIndex) PlayerPrefs.SetString(AssetCache.IndexKey, _savedIndex);
            else PlayerPrefs.DeleteKey(AssetCache.IndexKey);
            PlayerPrefs.Save();
        }

        private static void RemoveFile()
        {
            string p = AssetCacheStore.PathFor(Url);
            try { if (p != null && File.Exists(p)) File.Delete(p); } catch { }
        }

        private static bool OnDisk => File.Exists(AssetCacheStore.PathFor(Url));

        /// <summary>Put a file on disk without going through Store — i.e. as an older build left it.</summary>
        private static void PutOnDisk() => File.WriteAllBytes(AssetCacheStore.PathFor(Url), Glb(64));

        /// <summary>A structurally valid GLB of <paramref name="n"/> bytes.</summary>
        private static byte[] Glb(int n)
        {
            var b = new byte[n];
            b[0] = 0x67; b[1] = 0x6C; b[2] = 0x54; b[3] = 0x46;
            b[4] = 2;
            b[8] = (byte)(n & 0xFF); b[9] = (byte)((n >> 8) & 0xFF);
            b[10] = (byte)((n >> 16) & 0xFF); b[11] = (byte)((n >> 24) & 0xFF);
            return b;
        }

        /// <summary>Write an index holding just this file. <paramref name="gen"/> below 0 writes a
        /// three-column row — the format every device in the field is carrying right now.</summary>
        private static void IndexAt(int gen)
        {
            string row = AssetCache.KeyFor(Url) + ":64:1000" + (gen >= 0 ? ":" + gen : "");
            PlayerPrefs.SetString(AssetCache.IndexKey, row);
            PlayerPrefs.Save();
        }

        private static string FileUri => "file://" + AssetCacheStore.PathFor(Url);

        [Test]
        public void ACurrentCopy_IsServedStraightOffTheDisk()
        {
            PutOnDisk();
            IndexAt(AssetCache.Generation);
            Assert.AreEqual(FileUri, AssetCacheStore.Resolve(Url), "a file this build wrote is a hit");
        }

        [Test]
        public void ACopyFromBeforeTheCdnWasRepaired_SendsTheLoaderBackToTheNetwork()
        {
            // 🔴 THE regression test for the black triangles, walked end to end: a legacy row, a
            // file present on disk, and the loader must still be told to go and fetch.
            PutOnDisk();
            IndexAt(-1);                       // three columns = written before generations existed

            Assert.AreEqual(Url, AssetCacheStore.Resolve(Url),
                            "an out-of-date copy must NOT be served — this is the whole bug");
            Assert.IsTrue(OnDisk,
                          "…and must not be deleted either: it is the offline fallback until the " +
                          "replacement is actually in hand");
        }

        [Test]
        public void WithNoSignal_TheOutOfDateCopyIsStillBetterThanAGreyPlaceholder()
        {
            PutOnDisk();
            IndexAt(-1);

            Assert.AreEqual(Url, AssetCacheStore.Resolve(Url));   // tried the network first
            Assert.AreEqual(FileUri, AssetCacheStore.StaleUri(Url),
                            "the download failed, so the old model beats no model");
            Assert.IsNotNull(AssetCacheStore.ReadLocal(Url));
        }

        [Test]
        public void OnceTheRepairedBytesLand_TheCopyIsCurrentAndServedAgain()
        {
            PutOnDisk();
            IndexAt(-1);
            Assert.AreEqual(Url, AssetCacheStore.Resolve(Url), "stale first");

            Assert.IsTrue(AssetCacheStore.Store(Url, Glb(128)), "the repaired download is written");

            Assert.AreEqual(FileUri, AssetCacheStore.Resolve(Url),
                            "and from now on it is a hit — the re-fetch happens ONCE per device, " +
                            "not once per map open, which is what keeps CDN egress flat");
        }

        [Test]
        public void AHalfArrivedDownload_IsNeverStamped_AndLeavesTheOldCopyAlone()
        {
            PutOnDisk();
            IndexAt(-1);

            // 900 bytes of a file whose header says 4096 — a dropped connection.
            var truncated = Glb(4096);
            var partial = new byte[900];
            System.Array.Copy(truncated, partial, 900);

            Assert.IsFalse(AssetCacheStore.Store(Url, partial), "a torn download is not a model");
            Assert.AreEqual(Url, AssetCacheStore.Resolve(Url),
                            "still stale — a refused write must not look like a successful one");
            Assert.AreEqual(FileUri, AssetCacheStore.StaleUri(Url),
                            "and the old copy survives to be the offline fallback");
        }

        [Test]
        public void NothingOnDisk_IsSimplyTheUrl()
        {
            IndexAt(AssetCache.Generation);        // index says we have it; the disk disagrees
            Assert.AreEqual(Url, AssetCacheStore.Resolve(Url));
            Assert.IsNull(AssetCacheStore.StaleUri(Url));
            Assert.IsNull(AssetCacheStore.ReadLocal(Url));
        }
    }
}
