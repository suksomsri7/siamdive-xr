using System;
using System.Collections.Generic;

namespace DiveMap.Core
{
    /// <summary>
    /// J7 — deciding what stays on the phone so a dive opens with no signal.
    ///
    /// <see cref="OfflineStore"/> already keeps the map's JSON, which is what made the ☁ badge
    /// honest. But a map whose models are missing opens as a field of grey boxes: the JSON is the
    /// smaller half of "ทัวร์ออฟไลน์". This is the other half — the GLB files themselves.
    ///
    /// Ported from the shipped app's rule (siamdive-rn/src/lib/offline/assets.ts:16):
    /// <c>ASSET_BUDGET_BYTES = 220MB</c>, least-recently-used dropped first. The number is not
    /// arbitrary — it is what that app has been living with on real phones, so it is the one to
    /// match rather than invent a new one.
    ///
    /// Everything here is pure: naming, budgeting and eviction are exactly the parts that fail
    /// silently (a name collision serves the wrong model; a broken budget fills the phone), and
    /// exactly the parts a headless test can pin down.
    /// </summary>
    public static class AssetCache
    {
        /// <summary>220 MB, the shipped app's cap.</summary>
        public const long BudgetBytes = 220L * 1024L * 1024L;

        /// <summary>
        /// 🔴 Bump this when the bytes behind a CDN URL change without the URL changing.
        ///
        /// The cache is keyed by URL and nothing else — no ETag, no size, no expiry. That was fine
        /// while a model's URL and its contents were the same fact. They are not: 444 GLBs on
        /// <c>siamdive-cdn.b-cdn.net/models/xr/</c> were re-uploaded over their existing URLs to
        /// repair NaN tangents (the black triangles), and every device that had already opened
        /// those maps kept serving itself the broken copy — forever, because a hit is a hit and
        /// nothing ever asked whether it was still the right file.
        ///
        /// Revalidating over the network was the obvious alternative and it is the wrong one here.
        /// Bunny sends no <c>ETag</c>, and its <c>Last-Modified</c> is the moment the EDGE filled
        /// its cache (measured: <c>last-modified</c> 03:33:57 against <c>cdn-cachedat</c> 03:33:58
        /// on a file untouched since), so it changes every time an edge expires a file and a
        /// conditional GET would re-download hundreds of MB of identical bytes — on a boat, over
        /// mobile data, once per model per map open. A number compiled into the build costs
        /// nothing, needs no signal, and cannot be wrong about its own build.
        ///
        /// An index row written before this field existed decodes as generation 0, which is
        /// exactly the set of files that needs replacing.
        ///
        /// generation 3 (2026-08-03): the UV-gutter dilation pass rewrote the base-colour and
        /// metallic-roughness textures of the re-exported models, and the singha statue's solids
        /// were rebuilt — all of it uploaded OVER the existing <c>models/xr/</c> URLs. Without the
        /// bump a device that has opened these maps once keeps serving itself the pre-dilation
        /// bytes and the fix reads as "แก้แล้วเหมือนไม่ได้แก้".
        ///
        /// generation 4 (2026-08-03): the albedo lift. Five models' base-colour textures were
        /// re-encoded — singha, HTMS Chang, htms732, hardeep, poseidon — after the in-frame probe
        /// proved their black came from the texture, not the lighting: forcing a white albedo took
        /// blackOfSubject from 20.21% to 0.00%, and raising every light in the app had moved it by
        /// nothing at all. Same URLs again, and maps.siamdive.com serves /models/* as
        /// <c>immutable, max-age=31536000</c>, so without this a phone that has opened Hanuman once
        /// would keep the old dark bytes for a year.
        /// </summary>
        /// generation 5 (2026-08-03): the rig batch. 132 files — 66 animal models, both LODs —
        /// were rebuilt with a skeleton and swim clips transferred onto the high-resolution master
        /// and uploaded over the same URLs. The app had never played a clip in its life, so a
        /// device holding generation-4 bytes would keep a catalogue of animals that cannot move
        /// while the manifest next to it says <c>"animated": true</c>.
        public const int Generation = 5;

        /// <summary>
        /// Is a cached file's generation still good enough to serve?
        ///
        /// <c>&gt;=</c>, not <c>==</c>: a device that ran a newer build and then rolled back (TestFlight
        /// installs both ways) holds files NEWER than this build expects, and re-downloading them
        /// would land the identical bytes, re-stamp them older, and hand the newer build the same
        /// work back. Older is stale; newer is simply fine.
        /// </summary>
        public static bool IsFresh(int generation) => generation >= Generation;

        /// <summary>
        /// Do these bytes look like the WHOLE file we asked for?
        ///
        /// 🔴 The generation bump makes this necessary rather than merely tidy. Before it, a device
        /// downloaded each model once and the odds of catching a bad response were whatever they
        /// were on the day. Now every device in the field re-fetches its models exactly once — and
        /// the place people open this app is a boat, on a captive-portal wifi that answers a GET
        /// for a GLB with an HTML login page and a cheerful <c>200 OK</c>. Those bytes would be
        /// written to disk, stamped with the CURRENT generation, and served forever: a permanently
        /// broken model that the generation gate can never rescue, because it is not stale — it is
        /// exactly as new as this build expects.
        ///
        /// The check is the glTF container's own header, which is the only thing here that can
        /// answer "is this the file or a story about the file":
        ///   • bytes 0-3  magic <c>glTF</c> — an HTML page, a JSON error, a redirect body all fail
        ///   • bytes 8-11 total length — a download cut off mid-flight declares more than it has
        ///
        /// Deliberately conservative in both directions. A declared length SHORTER than the data is
        /// accepted (trailing padding is legal and harmless), and anything that is not a
        /// <c>.glb</c> — the <c>.solids.json</c> hulls ride this same cache — is accepted on being
        /// non-empty, because this method must never be the reason a good file fails to cache.
        /// Rejecting costs one re-download; accepting rubbish costs the model until "clear
        /// downloads".
        /// </summary>
        public static bool LooksComplete(string url, byte[] data)
        {
            if (data == null || data.Length == 0) return false;
            if (!IsGlb(url)) return true;

            if (data.Length < 12) return false;
            // "glTF", little-endian magic 0x46546C67.
            if (data[0] != 0x67 || data[1] != 0x6C || data[2] != 0x54 || data[3] != 0x46) return false;

            long declared = data[8]
                          | ((long)data[9] << 8)
                          | ((long)data[10] << 16)
                          | ((long)data[11] << 24);
            return declared >= 12 && declared <= data.Length;
        }

        /// <summary>Is this URL a GLB, ignoring any query or fragment on the end?</summary>
        private static bool IsGlb(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            string u = url.Trim();
            int cut = u.IndexOfAny(new[] { '?', '#' });
            if (cut >= 0) u = u.Substring(0, cut);
            return u.EndsWith(".glb", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>One cached file.</summary>
        public struct Entry
        {
            public string Key;
            public long Bytes;
            /// <summary>Unix seconds when it was last read. Ties break by key, so eviction is stable.</summary>
            public long LastUsed;
            /// <summary>
            /// <see cref="Generation"/> at the time it was written. 0 for anything stored before
            /// generations existed — see <see cref="Decode"/>.
            /// </summary>
            public int Gen;
        }

        /// <summary>
        /// Filesystem name for a URL. Not the URL's own filename: two modules can both end in
        /// <c>model.glb</c>, and serving one map's model for another's is the kind of bug that
        /// looks like corrupt data rather than a cache fault.
        ///
        /// A hash of the whole URL, plus a readable tail so a person looking in the folder can
        /// tell what they are seeing. The tail is sanitised because a URL is user-ish input and
        /// "../../" in a filename escapes the cache directory.
        /// </summary>
        public static string KeyFor(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            string u = url.Trim();

            uint h = 2166136261;
            unchecked
            {
                foreach (char c in u) { h ^= c; h *= 16777619; }   // FNV-1a, same as the web's ids
            }

            string tail = u;
            int slash = tail.LastIndexOf('/');
            if (slash >= 0 && slash + 1 < tail.Length) tail = tail.Substring(slash + 1);
            int q = tail.IndexOf('?');
            if (q >= 0) tail = tail.Substring(0, q);

            var safe = new System.Text.StringBuilder(24);
            foreach (char c in tail)
            {
                if (safe.Length >= 24) break;
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                          (c >= '0' && c <= '9') || c == '-' || c == '_' || c == '.';
                safe.Append(ok ? c : '_');
            }
            if (safe.Length == 0) safe.Append("asset");

            return h.ToString("x8") + "_" + safe;
        }

        /// <summary>Worth keeping? Only real remote files — a local path is already local.</summary>
        public static bool IsCacheable(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            string u = url.Trim();
            return u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                   u.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Which entries to delete to get back under <paramref name="budget"/>, oldest use first.
        /// Returns them in the order they should go; empty when there is nothing to do.
        ///
        /// Deliberately returns a PLAN rather than deleting: the caller owns the filesystem, and a
        /// decision that can be tested is worth more than one buried in an I/O loop.
        /// </summary>
        public static List<Entry> PlanEviction(IEnumerable<Entry> entries, long budget = BudgetBytes)
        {
            var plan = new List<Entry>();
            if (entries == null) return plan;

            var all = new List<Entry>(entries);
            long total = 0;
            foreach (Entry e in all) total += Math.Max(0, e.Bytes);
            if (total <= budget) return plan;

            all.Sort((a, b) =>
            {
                int c = a.LastUsed.CompareTo(b.LastUsed);
                return c != 0 ? c : string.CompareOrdinal(a.Key ?? "", b.Key ?? "");
            });

            foreach (Entry e in all)
            {
                if (total <= budget) break;
                plan.Add(e);
                total -= Math.Max(0, e.Bytes);
            }
            return plan;
        }

        /// <summary>Total size of a set of entries — what the settings screen shows.</summary>
        public static long TotalBytes(IEnumerable<Entry> entries)
        {
            long total = 0;
            if (entries == null) return 0;
            foreach (Entry e in entries) total += Math.Max(0, e.Bytes);
            return total;
        }

        /// <summary>"38.4 MB" — for the one line of UI that tells the user what they are storing.</summary>
        public static string FormatSize(long bytes)
        {
            if (bytes < 0) bytes = 0;
            if (bytes < 1024) return bytes + " B";
            double kb = bytes / 1024.0;
            if (kb < 1024) return kb.ToString("0.#") + " KB";
            double mb = kb / 1024.0;
            if (mb < 1024) return mb.ToString("0.#") + " MB";
            return (mb / 1024.0).ToString("0.##") + " GB";
        }

        // ── the index, stored as one string ──────────────────────────────────────

        /// <summary>PlayerPrefs key holding the whole index.</summary>
        public const string IndexKey = "assetCache";

        /// <summary>
        /// <c>key:bytes:lastUsed:gen</c> rows separated by newlines. A plain format on purpose: this
        /// has to survive being read by a future version, and a row that cannot be parsed is
        /// skipped rather than taking the index with it (a corrupt cache index that throws would
        /// mean the app cannot start).
        /// </summary>
        public static string Encode(IEnumerable<Entry> entries)
        {
            if (entries == null) return "";
            var sb = new System.Text.StringBuilder();
            foreach (Entry e in entries)
            {
                if (string.IsNullOrEmpty(e.Key) || e.Key.IndexOf(':') >= 0) continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(e.Key).Append(':').Append(Math.Max(0, e.Bytes)).Append(':').Append(e.LastUsed)
                  .Append(':').Append(Math.Max(0, e.Gen));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Read the index back. Three-column rows are the format that shipped before generations
        /// existed and are kept readable rather than discarded — throwing them away would empty
        /// every existing device's cache in one step, which is the exact outage ("no models, no
        /// signal") that <see cref="AssetCache"/> exists to prevent. They decode at generation 0:
        /// still on disk, still usable when there is no network, but no longer trusted as current.
        /// </summary>
        public static List<Entry> Decode(string s)
        {
            var list = new List<Entry>();
            if (string.IsNullOrEmpty(s)) return list;

            foreach (string row in s.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(row)) continue;
                string[] p = row.Split(':');
                if (p.Length != 3 && p.Length != 4) continue;
                if (!long.TryParse(p[1], out long bytes)) continue;
                if (!long.TryParse(p[2], out long used)) continue;

                int gen = 0;
                if (p.Length == 4 && !int.TryParse(p[3], out gen)) continue;

                list.Add(new Entry
                {
                    Key = p[0],
                    Bytes = Math.Max(0, bytes),
                    LastUsed = used,
                    Gen = Math.Max(0, gen),
                });
            }
            return list;
        }
    }
}
