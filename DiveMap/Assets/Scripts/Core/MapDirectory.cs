using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DiveMap.Core
{
    /// <summary>One row of the public map directory (WO-XR-05.2).</summary>
    public sealed class MapCard
    {
        public string ShortId;
        public string PublicSlug;
        public string Name;
        public string ThumbUrl;
        public string OwnerName;
        public string AccountId;
        public string UpdatedAt;
        public int ViewCount;
        public int LikeCount;
        public int FavoriteCount;
        public bool IsPublic;
    }

    /// <summary>Which "by …" line a card shows. Mirrors siamdive-rn <c>map.tsx</c>.</summary>
    public enum OwnerKind
    {
        /// <summary>The signed-in user's own map ("by You").</summary>
        You,
        /// <summary>An admin/warp-world map ("by SIAMDIVE").</summary>
        Official,
        /// <summary>Someone else's map whose creator has a username ("by &lt;name&gt;").</summary>
        Named,
        /// <summary>Someone else's map with no username ("by Community").</summary>
        Community,
    }

    /// <summary>One page of <see cref="MapCard"/> plus the server's paging counters.</summary>
    public sealed class MapPage
    {
        public readonly List<MapCard> Cards = new List<MapCard>();
        public int Total;
        public int Take;
        public int Skip;

        /// <summary>True when the server has more rows beyond this page.</summary>
        public bool HasMore => Skip + Cards.Count < Total;
    }

    /// <summary>
    /// Pure URL builder + JSON parser for the production map directory.
    ///
    /// Verified contract (curl 2026-07-28):
    ///   GET https://maps.siamdive.com/api/dive-sites/public?q=&amp;take=30&amp;skip=0
    ///   → { "sites": [ { shortId, publicSlug, name, thumbUrl, accountId, isPublic,
    ///                    viewCount, likeCount, favoriteCount, updatedAt, ownerName } ],
    ///       "total": 6, "take": 30, "skip": 0 }
    ///   Search is server-side (name contains, case-insensitive); take is capped at 60.
    ///   ownerName and publicSlug are frequently null.
    ///
    /// No Unity API is used here on purpose (Uri.EscapeDataString instead of
    /// UnityWebRequest.EscapeURL) so the EditMode assembly can exercise it directly.
    /// </summary>
    public static class MapDirectory
    {
        public const string DefaultBaseUrl = "https://maps.siamdive.com";
        public const int DefaultTake = 30;
        public const int MaxTake = 60; // server clamps at 60 — clamp here too, don't guess

        public static string BuildListUrl(string q, int take, int skip, string baseUrl = DefaultBaseUrl)
        {
            string b = (string.IsNullOrEmpty(baseUrl) ? DefaultBaseUrl : baseUrl).TrimEnd('/');
            int t = take <= 0 ? DefaultTake : (take > MaxTake ? MaxTake : take);
            int s = skip < 0 ? 0 : skip;
            string query = string.IsNullOrEmpty(q) ? "" : Uri.EscapeDataString(q.Trim());

            return b + "/api/dive-sites/public?q=" + query
                     + "&take=" + t.ToString(CultureInfo.InvariantCulture)
                     + "&skip=" + s.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Parse a directory response. Throws only on malformed JSON / empty body.</summary>
        public static MapPage ParseList(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("empty directory response", nameof(json));

            // DateParseHandling.None: keep updatedAt as the raw ISO-8601 string the API sent.
            // JObject.Parse would coerce it to a DateTime and stringify it back in machine locale.
            JObject root;
            using (var sr = new StringReader(json))
            using (var jr = new JsonTextReader(sr) { DateParseHandling = DateParseHandling.None })
            {
                root = JObject.Load(jr);
            }
            var page = new MapPage
            {
                Total = ReadInt(root["total"], -1),
                Take = ReadInt(root["take"], DefaultTake),
                Skip = ReadInt(root["skip"], 0),
            };

            if (root["sites"] is JArray sites)
            {
                foreach (JToken tok in sites)
                {
                    if (!(tok is JObject o)) continue;

                    string shortId = ReadString(o["shortId"]);
                    if (string.IsNullOrEmpty(shortId)) continue; // unusable row — skip, don't throw

                    page.Cards.Add(new MapCard
                    {
                        ShortId = shortId,
                        PublicSlug = ReadString(o["publicSlug"]),
                        Name = ReadString(o["name"]),
                        ThumbUrl = ReadString(o["thumbUrl"]),
                        OwnerName = ReadString(o["ownerName"]),
                        AccountId = ReadString(o["accountId"]),
                        UpdatedAt = ReadString(o["updatedAt"]),
                        ViewCount = ReadInt(o["viewCount"], 0),
                        LikeCount = ReadInt(o["likeCount"], 0),
                        FavoriteCount = ReadInt(o["favoriteCount"], 0),
                        IsPublic = ReadBool(o["isPublic"], false),
                    });
                }
            }

            if (page.Total < 0) page.Total = page.Skip + page.Cards.Count;
            return page;
        }

        /// <summary>
        /// The SiamDive admin account. Its maps are the warp worlds behind the "Play Game!"
        /// banner, and they carry the "by SIAMDIVE" byline instead of a creator name.
        /// Same constant as <c>ADMIN_ACCT</c> in siamdive-rn <c>src/app/map.tsx</c>.
        /// </summary>
        public const string AdminAccountId = "cmqrpkm6f000004jrjhztnaq4";

        /// <summary>True when the card belongs to the admin account (a warp world).</summary>
        public static bool IsOfficial(MapCard card) =>
            card != null && string.Equals(card.AccountId, AdminAccountId, StringComparison.Ordinal);

        /// <summary>
        /// Which byline a card shows, exactly as the RN hub decides it:
        /// <c>isMine ? byYou : accountId === ADMIN_ACCT ? byOfficial : ownerName ? "by {name}" : byCommunity</c>.
        /// The caller supplies <paramref name="isMine"/> because this app has no sign-in yet
        /// (queued as item 4) — until it does, nothing is "mine" and the branch is never taken.
        /// </summary>
        public static OwnerKind OwnerKindOf(MapCard card, bool isMine)
        {
            if (isMine) return OwnerKind.You;
            if (IsOfficial(card)) return OwnerKind.Official;
            if (card != null && !string.IsNullOrWhiteSpace(card.OwnerName)) return OwnerKind.Named;
            return OwnerKind.Community;
        }

        /// <summary>Display name for a card — never empty (falls back to the shortId).</summary>
        public static string DisplayName(MapCard card)
        {
            if (card == null) return "";
            string name = string.IsNullOrWhiteSpace(card.Name) ? (card.ShortId ?? "") : card.Name;
            return name.Trim();
        }

        /// <summary>
        /// Characters a list card shows before the name is elided. The card name row is a
        /// single non-wrapping line, so the cap keeps a pathological name from running off
        /// the card instead of letting the text component wrap (or drop) it.
        /// </summary>
        public const int MaxCardNameChars = 34;

        /// <summary>Single-line label for a list card: <see cref="DisplayName"/>, elided.</summary>
        public static string CardLabel(MapCard card) => Ellipsize(DisplayName(card), MaxCardNameChars);

        /// <summary>
        /// Trim <paramref name="s"/> to at most <paramref name="maxChars"/> characters,
        /// appending U+2026 (present in NotoSansThai-Regular, gid 315) when it is cut.
        /// Pure — unit-tested in DiveMap.Tests.EditMode.
        /// </summary>
        public static string Ellipsize(string s, int maxChars)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Trim();
            if (maxChars <= 0) return "";
            if (s.Length <= maxChars) return s;
            if (maxChars == 1) return "…";
            return s.Substring(0, maxChars - 1).TrimEnd() + "…";
        }

        // ── token readers (null / wrong-type tolerant) ───────────────────────────

        private static string ReadString(JToken t)
        {
            if (t == null || t.Type == JTokenType.Null) return null;
            if (t.Type == JTokenType.String) return t.Value<string>();
            if (t.Type == JTokenType.Date) return t.Value<DateTime>().ToString("o", CultureInfo.InvariantCulture);
            return t.ToString();
        }

        private static int ReadInt(JToken t, int fallback)
        {
            if (t == null || t.Type == JTokenType.Null) return fallback;
            try { return t.Value<int>(); }
            catch { return fallback; }
        }

        private static bool ReadBool(JToken t, bool fallback)
        {
            if (t == null || t.Type == JTokenType.Null) return fallback;
            try { return t.Value<bool>(); }
            catch { return fallback; }
        }
    }
}
