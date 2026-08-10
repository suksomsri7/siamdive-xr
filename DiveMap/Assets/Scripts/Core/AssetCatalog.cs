using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace DiveMap.Core
{
    /// <summary>
    /// WO-L item 8 — where the palette's rows come from, and how a row added in the backoffice
    /// reaches a phone that is already installed.
    ///
    /// 🔴 READ THIS BEFORE EXPECTING /api/assets TO ADD CARDS.
    /// The web builder has TWO sources and only one of them is a server:
    /// <code>
    ///   /api/assets      builder.html:1053  → 20 rows today, and every one of them is a
    ///                                         PROCEDURAL placeholder (rock:0-3, coral, anemone,
    ///                                         fish, turtle, wreck) that Palette.IsProcedural
    ///                                         drops on both products
    ///   CC0_MODULES      builder.html:1060  → 255 rows, a JavaScript CONSTANT in the page
    /// </code>
    /// So the 216 cards a user actually sees are shipped inside the web build, exactly as ours
    /// are shipped inside StreamingAssets/asset_manifest.json. There is no endpoint that serves
    /// them; "the backoffice" only manages the procedural set. Verified live while writing this:
    /// <c>GET /api/assets</c> → <c>{"assets":[…20…]}</c>, kinds ROCK 4 · CORAL 4 · FISH 4 ·
    /// ANEMONE 3 · TURTLE 3 · WRECK 2.
    ///
    /// The fetch is therefore built as a UNION that is correct rather than as a fix that is
    /// theatre: the manifest stays the base, anything the server knows is merged over the top by
    /// id, and the day the backoffice starts serving GLB-backed modules they appear without an
    /// app build. Today the union adds zero visible cards, and that is the honest outcome — the
    /// missing procedural primitives are WO-M, not this.
    ///
    /// Everything here is pure so the merge order, the id collision rule and the TTL are settled
    /// by tests on this machine instead of by a 35-minute CI round with a screenshot.
    /// </summary>
    public static class AssetCatalog
    {
        /// <summary>
        /// How long a fetched catalogue is trusted. Ten minutes is chosen against the cost of
        /// being wrong in each direction: too long and an author waits for a card they just
        /// added; too short and every palette open pays a round trip before it can draw. A
        /// session in the builder is minutes, not hours.
        /// </summary>
        public const double TtlSeconds = 600d;

        /// <summary>The path the web reads its DB-driven rows from (builder.html:1053).</summary>
        public const string Path = "/api/assets";

        public static string Url(string baseUrl)
            => (baseUrl ?? "").TrimEnd('/') + Path;

        /// <summary>
        /// Is a catalogue fetched at <paramref name="fetchedAt"/> still good at
        /// <paramref name="now"/>? A never-fetched catalogue (<paramref name="fetchedAt"/> ≤ 0)
        /// is never fresh, and a clock that went backwards counts as stale rather than as
        /// fresh-forever — realtimeSinceStartup resets to zero on a domain reload.
        /// </summary>
        public static bool IsFresh(double fetchedAt, double now, double ttl = TtlSeconds)
        {
            if (fetchedAt <= 0d) return false;
            double age = now - fetchedAt;
            return age >= 0d && age < ttl;
        }

        /// <summary>
        /// Parse the <c>/api/assets</c> body into palette rows. Returns an empty list — never
        /// null and never throws — for anything unexpected: this runs on a response from the
        /// network, and a malformed body must degrade to "the shipped manifest only", not to a
        /// palette that fails to open.
        ///
        /// Shape (verified live): <c>{"assets":[{"id","kind","name","glbUrl"?}, …]}</c>. A bare
        /// top-level array is accepted too, because that is what the endpoint returned before it
        /// was wrapped and an old cache may still hold one.
        /// </summary>
        public static List<PaletteSource> Parse(string json)
        {
            var rows = new List<PaletteSource>();
            if (string.IsNullOrWhiteSpace(json)) return rows;

            JToken root;
            try { root = JToken.Parse(json); }
            catch (Exception) { return rows; }

            // 🔴 `root["assets"]` is NOT safe on an arbitrary token: indexing a JValue throws
            // InvalidOperationException rather than returning null, so a body of `42` — or any
            // bare scalar a proxy might hand back — would take the exception path out of a
            // coroutine. Caught by Parse_SurvivesEverythingTheNetworkCanHandUs.
            JArray arr = root as JArray ?? (root as JObject)?["assets"] as JArray;
            if (arr == null) return rows;

            foreach (JToken t in arr)
            {
                if (!(t is JObject o)) continue;
                string id = (string)o["id"];
                if (string.IsNullOrWhiteSpace(id)) continue;
                string glb = (string)o["glbUrl"];
                rows.Add(new PaletteSource
                {
                    Id = id.Trim(),
                    Kind = (string)o["kind"] ?? "",
                    Name = (string)o["name"] ?? id.Trim(),
                    HasGlb = !string.IsNullOrWhiteSpace(glb),
                });
            }
            return rows;
        }

        /// <summary>
        /// Shipped manifest first, server rows merged over it by id.
        ///
        /// The server wins a collision on purpose: a row exists in both only when the backoffice
        /// has edited something the build already had, and the edit is the newer truth (a rename,
        /// a re-pointed GLB). Manifest order is preserved for everything else so a QC screenshot
        /// of the grid stays comparable between builds — <see cref="Palette.Build"/> re-sorts
        /// only the two priced categories, everything else is drawn in the order it arrives.
        /// </summary>
        public static List<PaletteSource> Merge(IEnumerable<PaletteSource> shipped,
                                                IEnumerable<PaletteSource> live)
        {
            var merged = new List<PaletteSource>();
            var index = new Dictionary<string, int>(StringComparer.Ordinal);

            if (shipped != null)
            {
                foreach (PaletteSource s in shipped)
                {
                    if (s == null || string.IsNullOrEmpty(s.Id)) continue;
                    if (index.TryGetValue(s.Id, out int at)) { merged[at] = s; continue; }
                    index[s.Id] = merged.Count;
                    merged.Add(s);
                }
            }

            if (live != null)
            {
                foreach (PaletteSource s in live)
                {
                    if (s == null || string.IsNullOrEmpty(s.Id)) continue;
                    if (index.TryGetValue(s.Id, out int at)) merged[at] = s;
                    else { index[s.Id] = merged.Count; merged.Add(s); }
                }
            }

            return merged;
        }
    }
}
