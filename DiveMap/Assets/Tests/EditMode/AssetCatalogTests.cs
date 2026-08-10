using System.Collections.Generic;
using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// WO-L item 8 — the live catalogue merge. Every one of these runs against a response from
    /// the network, so the failures worth pinning are the ugly ones: a body that is not JSON, a
    /// row with no id, a server that repeats something the build already shipped. None of them
    /// may empty the palette, because the palette IS the shop.
    /// </summary>
    public class AssetCatalogTests
    {
        private static PaletteSource S(string id, string kind = "ROCK", string name = "x")
            => new PaletteSource { Id = id, Kind = kind, Name = name, HasGlb = true };

        // ── url ──────────────────────────────────────────────────────────────────

        [Test]
        public void Url_JoinsWithoutDoublingTheSlash()
        {
            Assert.AreEqual("https://maps.siamdive.com/api/assets",
                            AssetCatalog.Url("https://maps.siamdive.com"));
            Assert.AreEqual("https://maps.siamdive.com/api/assets",
                            AssetCatalog.Url("https://maps.siamdive.com/"));
        }

        // ── parse ────────────────────────────────────────────────────────────────

        [Test]
        public void Parse_ReadsTheWrappedShapeTheEndpointActuallyReturns()
        {
            // Verified live: {"assets":[{"id":"rock:0","kind":"ROCK",…}, …]}
            const string body =
                "{\"assets\":[{\"id\":\"rock:0\",\"kind\":\"ROCK\",\"name\":\"กลม\",\"glbUrl\":null}," +
                "{\"id\":\"cc0:portal\",\"kind\":\"SPECIAL\",\"name\":\"Portal\",\"glbUrl\":\"https://x/p.glb\"}]}";

            List<PaletteSource> rows = AssetCatalog.Parse(body);

            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual("rock:0", rows[0].Id);
            Assert.AreEqual("ROCK", rows[0].Kind);
            Assert.AreEqual("กลม", rows[0].Name);
            Assert.IsFalse(rows[0].HasGlb, "a null glbUrl means no thumbnail was ever rendered");
            Assert.IsTrue(rows[1].HasGlb);
        }

        [Test]
        public void Parse_AlsoAcceptsABareArray()
        {
            // What the endpoint returned before it was wrapped; an old cached body may be one.
            List<PaletteSource> rows = AssetCatalog.Parse("[{\"id\":\"cc0:rock_a\",\"kind\":\"ROCK\"}]");
            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual("cc0:rock_a", rows[0].Id);
            Assert.AreEqual("cc0:rock_a", rows[0].Name, "a nameless row falls back to its id");
        }

        [Test]
        public void Parse_SurvivesEverythingTheNetworkCanHandUs()
        {
            // 🔴 The whole point: a bad body degrades to "shipped manifest only", never to an
            // exception on the way into a sheet the user just tapped open.
            foreach (string bad in new[] { null, "", "   ", "not json", "<html>502</html>", "42",
                                           "{\"assets\":null}", "{\"nope\":[]}" })
                Assert.AreEqual(0, AssetCatalog.Parse(bad).Count, $"body: {bad ?? "null"}");
        }

        [Test]
        public void Parse_SkipsRowsWithNoUsableId()
        {
            List<PaletteSource> rows = AssetCatalog.Parse(
                "{\"assets\":[{\"kind\":\"ROCK\"},{\"id\":\"\"},{\"id\":\"  \"},{\"id\":\" ok:1 \"}]}");
            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual("ok:1", rows[0].Id, "ids are trimmed — a stray space is a new category");
        }

        // ── merge ────────────────────────────────────────────────────────────────

        [Test]
        public void Merge_ShippedIsTheBaseAndKeepsItsOrder()
        {
            List<PaletteSource> merged = AssetCatalog.Merge(
                new[] { S("a"), S("b"), S("c") }, null);

            Assert.AreEqual(3, merged.Count);
            Assert.AreEqual("a", merged[0].Id);
            Assert.AreEqual("b", merged[1].Id);
            Assert.AreEqual("c", merged[2].Id);
        }

        [Test]
        public void Merge_AnEmptyServerAnswerChangesNothing()
        {
            // The case that happens on every offline launch, and the reason this is a union and
            // not a replacement: no signal must not mean no palette.
            var shipped = new[] { S("a"), S("b") };
            Assert.AreEqual(2, AssetCatalog.Merge(shipped, new List<PaletteSource>()).Count);
            Assert.AreEqual(2, AssetCatalog.Merge(shipped, null).Count);
            Assert.AreEqual(0, AssetCatalog.Merge(null, null).Count);
        }

        [Test]
        public void Merge_TheServerWinsACollisionButDoesNotReorder()
        {
            // A row in both means the backoffice edited something the build shipped — a rename or
            // a re-pointed GLB — so the server is the newer truth. It must NOT jump to the end:
            // the grid order is what a QC screenshot is compared against.
            var shipped = new[] { S("a", name: "old"), S("b"), S("c") };
            var live = new[] { S("b", name: "renamed"), S("d") };

            List<PaletteSource> merged = AssetCatalog.Merge(shipped, live);

            Assert.AreEqual(4, merged.Count);
            Assert.AreEqual("a", merged[0].Id);
            Assert.AreEqual("b", merged[1].Id);
            Assert.AreEqual("renamed", merged[1].Name);
            Assert.AreEqual("c", merged[2].Id);
            Assert.AreEqual("d", merged[3].Id, "genuinely new rows land at the end");
        }

        [Test]
        public void Merge_ADuplicateInsideOneSourceIsCollapsed()
        {
            // A manifest that lists an id twice would otherwise draw the same card twice, and
            // buying from the second one would look like the first had failed.
            List<PaletteSource> merged = AssetCatalog.Merge(
                new[] { S("a", name: "first"), S("a", name: "second") }, null);
            Assert.AreEqual(1, merged.Count);
            Assert.AreEqual("second", merged[0].Name);
        }

        // ── ttl ──────────────────────────────────────────────────────────────────

        [Test]
        public void IsFresh_NeverFetchedIsNeverFresh()
        {
            Assert.IsFalse(AssetCatalog.IsFresh(0d, 100d));
            Assert.IsFalse(AssetCatalog.IsFresh(-1d, 100d));
        }

        [Test]
        public void IsFresh_HoldsForTheTtlAndThenLetsGo()
        {
            Assert.IsTrue(AssetCatalog.IsFresh(100d, 100d));
            Assert.IsTrue(AssetCatalog.IsFresh(100d, 100d + AssetCatalog.TtlSeconds - 1d));
            Assert.IsFalse(AssetCatalog.IsFresh(100d, 100d + AssetCatalog.TtlSeconds));
            Assert.IsFalse(AssetCatalog.IsFresh(100d, 100d + AssetCatalog.TtlSeconds + 1d));
        }

        [Test]
        public void IsFresh_AClockThatWentBackwardsIsStaleNotFreshForever()
        {
            // Time.realtimeSinceStartup restarts at zero on a domain reload. Treating a negative
            // age as "fresh" would pin the catalogue for the rest of the process.
            Assert.IsFalse(AssetCatalog.IsFresh(500d, 10d));
        }
    }
}
