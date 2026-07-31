using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DiveMap.Core
{
    /// <summary>
    /// Which maps this install has hearted, mirroring the RN hub's <c>likedMaps</c> store.
    ///
    /// The server counter (<c>POST /api/dive-sites/{id}/react</c>) is a bare increment with no
    /// per-device reaction table — its own route comment says "1-per-device is enforced
    /// client-side". So the client MUST remember what it liked, or every re-tap inflates the
    /// count and the heart never reads as "already liked" after a restart.
    ///
    /// The codec is separated from PlayerPrefs so it can be unit-tested without a player.
    /// </summary>
    public static class LikedMaps
    {
        public const string PrefKey = "likedMaps";
        private const char Separator = ',';

        // ── pure codec ───────────────────────────────────────────────────────────

        /// <summary>Parse the stored CSV. Blank/duplicate entries are dropped, never thrown on.</summary>
        public static HashSet<string> Decode(string csv)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(csv)) return set;

            string[] parts = csv.Split(Separator);
            for (int i = 0; i < parts.Length; i++)
            {
                string id = parts[i].Trim();
                if (id.Length > 0) set.Add(id);
            }
            return set;
        }

        /// <summary>Serialise ids back to CSV, skipping blanks and duplicates.</summary>
        public static string Encode(IEnumerable<string> ids)
        {
            if (ids == null) return "";
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var sb = new StringBuilder();
            foreach (string raw in ids)
            {
                if (raw == null) continue;
                string id = raw.Trim();
                // A comma inside an id would split into two phantom likes on the next read.
                if (id.Length == 0 || id.IndexOf(Separator) >= 0 || !seen.Add(id)) continue;
                if (sb.Length > 0) sb.Append(Separator);
                sb.Append(id);
            }
            return sb.ToString();
        }

        /// <summary>Add/remove one id in a stored CSV. Pure — returns the new CSV.</summary>
        public static string Toggle(string csv, string id, bool on)
        {
            if (string.IsNullOrEmpty(id)) return csv ?? "";
            HashSet<string> set = Decode(csv);
            if (on) set.Add(id.Trim()); else set.Remove(id.Trim());
            return Encode(set);
        }

        // ── PlayerPrefs wrappers ─────────────────────────────────────────────────

        public static HashSet<string> All => Decode(PlayerPrefs.GetString(PrefKey, ""));

        public static bool IsLiked(string shortId)
        {
            if (string.IsNullOrEmpty(shortId)) return false;
            return Decode(PlayerPrefs.GetString(PrefKey, "")).Contains(shortId.Trim());
        }

        public static void Set(string shortId, bool on)
        {
            if (string.IsNullOrEmpty(shortId)) return;
            PlayerPrefs.SetString(PrefKey, Toggle(PlayerPrefs.GetString(PrefKey, ""), shortId, on));
            PlayerPrefs.Save();
        }
    }
}
