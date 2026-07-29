using System;
using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// Unit tests for MapDirectory (WO-XR-05.2) — the URL builder and the parser for
    /// GET /api/dive-sites/public.
    ///
    /// The fixtures below are the LITERAL prod responses captured with curl on
    /// 2026-07-28 (6 public maps; a "Chang" search returning exactly the Htms Chang
    /// demo site). Keeping the real bytes means a server-side contract change breaks a
    /// test instead of the app.
    /// </summary>
    public class MapDirectoryTests
    {
        // curl 'https://maps.siamdive.com/api/dive-sites/public?q=&take=30&skip=0'
        private const string RealListJson = @"{""sites"":[{""shortId"":""yh7hbkdmzur8"",""publicSlug"":""hanuman-b9vp8"",""name"":""Hanuman"",""thumbUrl"":""https://siamdive-cdn.b-cdn.net/dive-media/tadj25wji5mquftvx2.jpg"",""accountId"":""cmqrpkm6f000004jrjhztnaq4"",""isPublic"":false,""viewCount"":0,""likeCount"":1,""favoriteCount"":1,""updatedAt"":""2026-07-17T06:19:49.302Z"",""ownerName"":null},{""shortId"":""w63m4h7u4vi5"",""publicSlug"":null,""name"":""Posidon"",""thumbUrl"":""https://siamdive-cdn.b-cdn.net/dive-media/le3upl4faydmr3ll9xk.jpg"",""accountId"":""cmqrpkm6f000004jrjhztnaq4"",""isPublic"":false,""viewCount"":0,""likeCount"":0,""favoriteCount"":1,""updatedAt"":""2026-07-19T04:49:24.522Z"",""ownerName"":null},{""shortId"":""874ti6ignvp9"",""publicSlug"":null,""name"":""Harddeep"",""thumbUrl"":""https://siamdive-cdn.b-cdn.net/dive-media/u66g1d1elasmqvt2u33.jpg"",""accountId"":""cmqrpkm6f000004jrjhztnaq4"",""isPublic"":false,""viewCount"":0,""likeCount"":0,""favoriteCount"":1,""updatedAt"":""2026-07-18T11:41:08.494Z"",""ownerName"":null},{""shortId"":""wl6zwxh1tdgn"",""publicSlug"":""htms-chang-j1570"",""name"":""Htms Chang"",""thumbUrl"":""https://siamdive-cdn.b-cdn.net/dive-media/x6u41xbq0mpmque6erp.jpg"",""accountId"":""cmqrpkm6f000004jrjhztnaq4"",""isPublic"":false,""viewCount"":0,""likeCount"":0,""favoriteCount"":0,""updatedAt"":""2026-07-09T01:45:19.752Z"",""ownerName"":null},{""shortId"":""21v3chaote45"",""publicSlug"":null,""name"":""T-13 (ต.13)"",""thumbUrl"":""https://siamdive-cdn.b-cdn.net/dive-media/60mygg06w9hmqza1gzm.jpg"",""accountId"":""cmqrpkm6f000004jrjhztnaq4"",""isPublic"":false,""viewCount"":0,""likeCount"":0,""favoriteCount"":0,""updatedAt"":""2026-07-08T23:04:17.560Z"",""ownerName"":null},{""shortId"":""oy3hlklgnkmy"",""publicSlug"":null,""name"":""Dive Site Tu -1"",""thumbUrl"":""https://siamdive-cdn.b-cdn.net/dive-media/4uvgivnparumqu79kbf.jpg"",""accountId"":""cmqsvx18h000004l8cji95y2r"",""isPublic"":false,""viewCount"":0,""likeCount"":0,""favoriteCount"":0,""updatedAt"":""2026-06-26T03:41:58.448Z"",""ownerName"":""Tu Nakarin""}],""total"":6,""take"":30,""skip"":0}";

        // curl 'https://maps.siamdive.com/api/dive-sites/public?q=Chang&take=30&skip=0'
        private const string RealSearchJson = @"{""sites"":[{""shortId"":""wl6zwxh1tdgn"",""publicSlug"":""htms-chang-j1570"",""name"":""Htms Chang"",""thumbUrl"":""https://siamdive-cdn.b-cdn.net/dive-media/x6u41xbq0mpmque6erp.jpg"",""accountId"":""cmqrpkm6f000004jrjhztnaq4"",""isPublic"":false,""viewCount"":0,""likeCount"":0,""favoriteCount"":0,""updatedAt"":""2026-07-09T01:45:19.752Z"",""ownerName"":null}],""total"":1,""take"":30,""skip"":0}";

        // ── BuildListUrl ─────────────────────────────────────────────────────────

        [Test]
        public void BuildListUrl_DefaultsMatchProdShape()
        {
            Assert.AreEqual(
                "https://maps.siamdive.com/api/dive-sites/public?q=&take=30&skip=0",
                MapDirectory.BuildListUrl("", MapDirectory.DefaultTake, 0));
        }

        [Test]
        public void BuildListUrl_NullQueryIsEmpty()
        {
            StringAssert.Contains("?q=&take=30&skip=0", MapDirectory.BuildListUrl(null, 30, 0));
        }

        [Test]
        public void BuildListUrl_EscapesSpacesAndThai()
        {
            string url = MapDirectory.BuildListUrl("HTMS \u0E0A\u0E49\u0E32\u0E07", 30, 0);
            StringAssert.Contains("q=HTMS%20", url);
            Assert.IsFalse(url.Contains(" "), "raw space leaked into the query string");
            Assert.IsFalse(UiStrings.ContainsThai(url), "raw Thai leaked into the query string");
        }

        [Test]
        public void BuildListUrl_TrimsQuery()
        {
            StringAssert.Contains("q=Chang&", MapDirectory.BuildListUrl("  Chang  ", 30, 0));
        }

        [Test]
        public void BuildListUrl_ClampsTakeAndSkip()
        {
            string url = MapDirectory.BuildListUrl("", 999, -5);
            StringAssert.Contains("take=60", url);   // server caps at 60
            StringAssert.Contains("skip=0", url);

            StringAssert.Contains("take=30", MapDirectory.BuildListUrl("", 0, 0)); // 0 → default
        }

        [Test]
        public void BuildListUrl_HonoursCustomBaseUrl()
        {
            StringAssert.StartsWith("http://localhost:3000/api/dive-sites/public",
                MapDirectory.BuildListUrl("", 30, 0, "http://localhost:3000/"));
        }

        // ── ParseList: the real payload ──────────────────────────────────────────

        [Test]
        public void ParseList_RealPayload_ReadsPagingAndAllRows()
        {
            MapPage page = MapDirectory.ParseList(RealListJson);

            Assert.AreEqual(6, page.Total);
            Assert.AreEqual(30, page.Take);
            Assert.AreEqual(0, page.Skip);
            Assert.AreEqual(6, page.Cards.Count);
            Assert.IsFalse(page.HasMore);
        }

        [Test]
        public void ParseList_RealPayload_FirstRowFieldsAreExact()
        {
            MapCard c = MapDirectory.ParseList(RealListJson).Cards[0];

            Assert.AreEqual("yh7hbkdmzur8", c.ShortId);
            Assert.AreEqual("hanuman-b9vp8", c.PublicSlug);
            Assert.AreEqual("Hanuman", c.Name);
            StringAssert.StartsWith("https://siamdive-cdn.b-cdn.net/dive-media/", c.ThumbUrl);
            Assert.AreEqual(1, c.LikeCount);
            Assert.AreEqual(1, c.FavoriteCount);
            Assert.AreEqual(0, c.ViewCount);
            Assert.IsFalse(c.IsPublic);
            Assert.IsNull(c.OwnerName);
            StringAssert.StartsWith("2026-", c.UpdatedAt);
        }

        [Test]
        public void ParseList_RealPayload_KeepsNonNullOwnerName()
        {
            MapPage page = MapDirectory.ParseList(RealListJson);
            MapCard owned = page.Cards.Find(x => x.ShortId == "oy3hlklgnkmy");

            Assert.IsNotNull(owned, "expected the Dive Site Tu -1 row");
            Assert.AreEqual("Tu Nakarin", owned.OwnerName);
            Assert.AreEqual("Dive Site Tu -1", owned.Name);
        }

        [Test]
        public void ParseList_RealPayload_ContainsTheDemoMapUsedByAppBoot()
        {
            MapPage page = MapDirectory.ParseList(RealListJson);
            MapCard demo = page.Cards.Find(x => x.ShortId == "wl6zwxh1tdgn");

            Assert.IsNotNull(demo, "the wl6zwxh1tdgn demo map disappeared from the directory");
            Assert.AreEqual("Htms Chang", demo.Name);
        }

        [Test]
        public void ParseList_RealSearchPayload_IsNarrowedServerSide()
        {
            MapPage page = MapDirectory.ParseList(RealSearchJson);

            Assert.AreEqual(1, page.Total);
            Assert.AreEqual(1, page.Cards.Count);
            Assert.AreEqual("wl6zwxh1tdgn", page.Cards[0].ShortId);
            Assert.IsFalse(page.HasMore);
        }

        // ── ParseList: defensive paths ───────────────────────────────────────────

        [Test]
        public void ParseList_NullThumbUrlDoesNotThrow()
        {
            const string json = "{\"sites\":[{\"shortId\":\"abc123\",\"name\":\"No Thumb\"," +
                                "\"thumbUrl\":null,\"ownerName\":null,\"publicSlug\":null}]," +
                                "\"total\":1,\"take\":30,\"skip\":0}";

            MapPage page = MapDirectory.ParseList(json);

            Assert.AreEqual(1, page.Cards.Count);
            Assert.IsNull(page.Cards[0].ThumbUrl);
            Assert.AreEqual(0, page.Cards[0].LikeCount); // missing counters default to 0
            Assert.AreEqual("No Thumb", MapDirectory.DisplayName(page.Cards[0]));
        }

        [Test]
        public void ParseList_SkipsRowsWithoutShortId()
        {
            const string json = "{\"sites\":[{\"name\":\"orphan\"},{\"shortId\":\"ok1\"}]," +
                                "\"total\":2,\"take\":30,\"skip\":0}";

            MapPage page = MapDirectory.ParseList(json);

            Assert.AreEqual(1, page.Cards.Count);
            Assert.AreEqual("ok1", page.Cards[0].ShortId);
            Assert.AreEqual("ok1", MapDirectory.DisplayName(page.Cards[0])); // name falls back to id
        }

        [Test]
        public void ParseList_HasMoreDrivesPagination()
        {
            const string json = "{\"sites\":[{\"shortId\":\"a\"},{\"shortId\":\"b\"}]," +
                                "\"total\":40,\"take\":30,\"skip\":0}";

            MapPage page = MapDirectory.ParseList(json);

            Assert.IsTrue(page.HasMore);
            Assert.AreEqual(40, page.Total);
        }

        [Test]
        public void ParseList_MissingTotalFallsBackToRowCount()
        {
            MapPage page = MapDirectory.ParseList("{\"sites\":[{\"shortId\":\"a\"}],\"skip\":10}");

            Assert.AreEqual(11, page.Total); // skip + rows
            Assert.IsFalse(page.HasMore);
        }

        [Test]
        public void ParseList_EmptySitesArrayIsEmptyNotNull()
        {
            MapPage page = MapDirectory.ParseList("{\"sites\":[],\"total\":0,\"take\":30,\"skip\":0}");

            Assert.IsNotNull(page.Cards);
            Assert.AreEqual(0, page.Cards.Count);
            Assert.IsFalse(page.HasMore);
        }

        [Test]
        public void ParseList_EmptyBodyThrows()
        {
            Assert.Throws<ArgumentException>(() => MapDirectory.ParseList(""));
            Assert.Throws<ArgumentException>(() => MapDirectory.ParseList(null));
        }

        // ── CardLabel / Ellipsize ────────────────────────────────────────────────
        // The card name is drawn on ONE non-wrapping line, so the label must be capped
        // in code rather than left to the text component (which used to drop the whole
        // line when it did not fit — the "map name is invisible" bug).

        [Test]
        public void CardLabel_KeepsEveryRealProdNameIntact()
        {
            MapPage page = MapDirectory.ParseList(RealListJson);

            Assert.AreEqual("Hanuman", MapDirectory.CardLabel(page.Cards[0]));
            Assert.AreEqual("Htms Chang", MapDirectory.CardLabel(page.Cards[3]));
            Assert.AreEqual("T-13 (ต.13)", MapDirectory.CardLabel(page.Cards[4]));
            Assert.AreEqual("Dive Site Tu -1", MapDirectory.CardLabel(page.Cards[5]));

            foreach (MapCard c in page.Cards)
            {
                string label = MapDirectory.CardLabel(c);
                Assert.IsNotEmpty(label, "a card label must never be blank — " + c.ShortId);
                Assert.LessOrEqual(label.Length, MapDirectory.MaxCardNameChars);
            }
        }

        [Test]
        public void CardLabel_FallsBackToShortIdAndNeverReturnsNull()
        {
            Assert.AreEqual("yh7hbkdmzur8",
                            MapDirectory.CardLabel(new MapCard { ShortId = "yh7hbkdmzur8", Name = "  " }));
            Assert.AreEqual("", MapDirectory.CardLabel(null));
        }

        [Test]
        public void CardLabel_ElidesAnOverlongName()
        {
            var card = new MapCard { ShortId = "x", Name = new string('A', 80) };
            string label = MapDirectory.CardLabel(card);

            Assert.AreEqual(MapDirectory.MaxCardNameChars, label.Length);
            StringAssert.EndsWith("…", label);
        }

        [Test]
        public void Ellipsize_LeavesShortStringsAloneAndTrims()
        {
            Assert.AreEqual("Hanuman", MapDirectory.Ellipsize("Hanuman", 34));
            Assert.AreEqual("Hanuman", MapDirectory.Ellipsize("  Hanuman  ", 34));
            Assert.AreEqual("Hanuman", MapDirectory.Ellipsize("Hanuman", 7)); // exact fit, no cut
        }

        [Test]
        public void Ellipsize_CutsAtTheLimitWithoutATrailingSpace()
        {
            Assert.AreEqual("Htms…", MapDirectory.Ellipsize("Htms Chang", 5));
            Assert.AreEqual("…", MapDirectory.Ellipsize("Htms Chang", 1));
        }

        [Test]
        public void Ellipsize_HandlesNullEmptyAndNonPositiveLimits()
        {
            Assert.AreEqual("", MapDirectory.Ellipsize(null, 34));
            Assert.AreEqual("", MapDirectory.Ellipsize("", 34));
            Assert.AreEqual("", MapDirectory.Ellipsize("Hanuman", 0));
            Assert.AreEqual("", MapDirectory.Ellipsize("Hanuman", -3));
        }
    }
}
