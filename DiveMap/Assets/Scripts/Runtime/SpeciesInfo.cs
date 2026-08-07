using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DiveMap.Runtime
{
    /// <summary>
    /// Thai species facts for the info card — StreamingAssets/species_info_th.json.
    ///
    /// The card's rule (user, 2026-08-07): marine animals only, real facts only. This file is
    /// therefore a WHITELIST — an animal with no entry gets a name-only card, never a made-up
    /// paragraph. The fantasy species (sea serpents, prismatic reef fish) are absent on purpose:
    /// they have no real biology to describe, and inventing one would break the no-fabrication
    /// rule the content was written under.
    ///
    /// Same read strategy caveat as <see cref="AssetManifest"/>: on Android, StreamingAssets
    /// lives inside the APK jar and File.ReadAllText cannot see it. iOS ships first (Android is
    /// a later phase) — when Android lands, this loads down the same UnityWebRequest road the
    /// manifest takes. Until then a failed read means "no descriptions", not a crash.
    /// </summary>
    public static class SpeciesInfo
    {
        private static Dictionary<string, string> _byId;

        /// <summary>Description for an assetId, or null — null means "show the name and stop".</summary>
        public static string Get(string assetId)
        {
            if (string.IsNullOrEmpty(assetId)) return null;
            if (_byId == null) Load();
            return _byId.TryGetValue(assetId, out string s) ? s : null;
        }

        private static void Load()
        {
            _byId = new Dictionary<string, string>(System.StringComparer.Ordinal);
            try
            {
                string path = Path.Combine(Application.streamingAssetsPath, "species_info_th.json");
                if (!File.Exists(path)) { Debug.Log("[Species] no species_info_th.json"); return; }
                JObject root = JObject.Parse(File.ReadAllText(path));
                foreach (KeyValuePair<string, JToken> kv in root)
                {
                    if (kv.Value is not JObject o) continue;
                    string look = (string)o["look"];
                    string behavior = (string)o["behavior"];
                    string text = "";
                    if (!string.IsNullOrWhiteSpace(look)) text += "รูปร่าง: " + look.Trim();
                    if (!string.IsNullOrWhiteSpace(behavior))
                        text += (text.Length > 0 ? "\n" : "") + "พฤติกรรม: " + behavior.Trim();
                    if (text.Length > 0) _byId[kv.Key] = text;
                }
                Debug.Log($"[Species] loaded {_byId.Count} entries");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Species] load failed: {e.Message}");
            }
        }
    }
}
