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

        /// <summary>One cached file.</summary>
        public struct Entry
        {
            public string Key;
            public long Bytes;
            /// <summary>Unix seconds when it was last read. Ties break by key, so eviction is stable.</summary>
            public long LastUsed;
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
        /// <c>key:bytes:lastUsed</c> rows separated by newlines. A plain format on purpose: this
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
                sb.Append(e.Key).Append(':').Append(Math.Max(0, e.Bytes)).Append(':').Append(e.LastUsed);
            }
            return sb.ToString();
        }

        public static List<Entry> Decode(string s)
        {
            var list = new List<Entry>();
            if (string.IsNullOrEmpty(s)) return list;

            foreach (string row in s.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(row)) continue;
                string[] p = row.Split(':');
                if (p.Length != 3) continue;
                if (!long.TryParse(p[1], out long bytes)) continue;
                if (!long.TryParse(p[2], out long used)) continue;
                list.Add(new Entry { Key = p[0], Bytes = Math.Max(0, bytes), LastUsed = used });
            }
            return list;
        }
    }
}
