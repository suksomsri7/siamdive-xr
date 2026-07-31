using System;
using System.Collections.Generic;
using System.Text;

namespace DiveMap.Core
{
    /// <summary>
    /// Which maps this device keeps a copy of — the index behind the hub's ☁ badge and the
    /// reason a dive still opens with no signal.
    ///
    /// Everything here is pure: the paths, the index codec, and the rule for what counts as a
    /// usable copy. The file I/O lives in <c>OfflineStore</c> so this stays testable.
    ///
    /// Design note worth keeping: the index is stored SEPARATELY from the map files. Listing a
    /// directory to answer "is this map available offline?" would mean touching the filesystem
    /// once per card while the hub scrolls, on a phone, for a question that changes about twice
    /// a session.
    /// </summary>
    public static class OfflineMaps
    {
        public const string IndexKey = "offlineMaps";
        public const string FolderName = "maps";

        /// <summary>Filename for a map's cached JSON. Ids are [a-z0-9]{6,16} so this is safe.</summary>
        public static string FileName(string shortId)
        {
            if (string.IsNullOrEmpty(shortId)) return null;
            var sb = new StringBuilder(shortId.Length + 5);
            foreach (char c in shortId)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') ||
                          c == '-' || c == '_';
                sb.Append(ok ? c : '_');   // never let an id escape the folder
            }
            return sb.ToString() + ".json";
        }

        /// <summary>Parse the stored index (same CSV shape as <see cref="LikedMaps"/>).</summary>
        public static HashSet<string> Decode(string csv)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(csv)) return set;
            foreach (string part in csv.Split(','))
            {
                string id = part.Trim();
                if (id.Length > 0) set.Add(id);
            }
            return set;
        }

        public static string Encode(IEnumerable<string> ids)
        {
            if (ids == null) return "";
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var sb = new StringBuilder();
            foreach (string raw in ids)
            {
                if (raw == null) continue;
                string id = raw.Trim();
                if (id.Length == 0 || id.IndexOf(',') >= 0 || !seen.Add(id)) continue;
                if (sb.Length > 0) sb.Append(',');
                sb.Append(id);
            }
            return sb.ToString();
        }

        public static string Toggle(string csv, string shortId, bool on)
        {
            if (string.IsNullOrEmpty(shortId)) return csv ?? "";
            HashSet<string> set = Decode(csv);
            if (on) set.Add(shortId.Trim()); else set.Remove(shortId.Trim());
            return Encode(set);
        }

        /// <summary>
        /// Is this cached JSON usable? A map with no items is almost always a truncated write
        /// or a failed fetch that got saved anyway — opening it offline would show an empty sea
        /// and look like the map was lost.
        /// </summary>
        public static bool IsUsable(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length < 20) return false;
            try
            {
                var scene = SceneData.Parse(json);
                return scene != null && scene.Items().Count > 0;
            }
            catch { return false; }
        }
    }
}
