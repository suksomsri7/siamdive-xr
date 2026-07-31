using System.Collections.Generic;
using System.IO;
using UnityEngine;
using DiveMap.Core;

namespace DiveMap.Runtime
{
    /// <summary>
    /// The on-device copy of a map: written every time one loads successfully, read when the
    /// network cannot supply it.
    ///
    /// This is what the ☁ badge in the hub has been promising all along, and what "ทัวร์ออฟไลน์"
    /// means — a dive you have opened once will open again on a boat with no signal.
    ///
    /// What it deliberately does NOT do: cache the GLB models. Those are hundreds of megabytes
    /// and Unity's own cache already handles repeat downloads; a map that opens offline with
    /// placeholder shapes is still a map, and pretending otherwise would mean shipping a
    /// multi-gigabyte download button. That limit is stated in the UI rather than hidden.
    /// </summary>
    public static class OfflineStore
    {
        private static string Folder => Path.Combine(Application.persistentDataPath, OfflineMaps.FolderName);

        /// <summary>Every map with a usable local copy.</summary>
        public static HashSet<string> Cached => OfflineMaps.Decode(PlayerPrefs.GetString(OfflineMaps.IndexKey, ""));

        public static bool Has(string shortId) =>
            !string.IsNullOrEmpty(shortId) && Cached.Contains(shortId);

        public static int Count => Cached.Count;

        /// <summary>
        /// Keep this map for offline use. Called after every successful load, so "maps you have
        /// opened" and "maps you can open offline" are the same set — no separate download step
        /// to forget.
        /// </summary>
        public static bool Save(string shortId, SceneData scene)
        {
            if (string.IsNullOrEmpty(shortId) || scene == null) return false;

            try
            {
                Directory.CreateDirectory(Folder);
                string json = scene.ToJson();
                if (!OfflineMaps.IsUsable(json)) return false;   // never cache an empty sea

                // Write to a temp file and move: a half-written map is worse than none, because
                // it looks cached and then opens empty.
                string path = Path.Combine(Folder, OfflineMaps.FileName(shortId));
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);

                PlayerPrefs.SetString(OfflineMaps.IndexKey,
                                      OfflineMaps.Toggle(PlayerPrefs.GetString(OfflineMaps.IndexKey, ""),
                                                         shortId, true));
                PlayerPrefs.Save();
                Debug.Log($"[Offline] cached {shortId} ({json.Length} bytes) total={Count}");
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Offline] save failed: " + e.Message);
                return false;
            }
        }

        /// <summary>The cached scene, or null. Never throws — a bad file is the same as none.</summary>
        public static SceneData Load(string shortId)
        {
            if (string.IsNullOrEmpty(shortId)) return null;
            try
            {
                string path = Path.Combine(Folder, OfflineMaps.FileName(shortId));
                if (!File.Exists(path)) return null;

                string json = File.ReadAllText(path);
                if (!OfflineMaps.IsUsable(json)) { Forget(shortId); return null; }

                Debug.Log($"[Offline] loaded {shortId} from disk ({json.Length} bytes)");
                return SceneData.Parse(json);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Offline] load failed: " + e.Message);
                return null;
            }
        }

        /// <summary>Drop a copy (corrupt, or the player asked).</summary>
        public static void Forget(string shortId)
        {
            if (string.IsNullOrEmpty(shortId)) return;
            try
            {
                string path = Path.Combine(Folder, OfflineMaps.FileName(shortId));
                if (File.Exists(path)) File.Delete(path);
            }
            catch { /* the index entry is what matters */ }

            PlayerPrefs.SetString(OfflineMaps.IndexKey,
                                  OfflineMaps.Toggle(PlayerPrefs.GetString(OfflineMaps.IndexKey, ""),
                                                     shortId, false));
            PlayerPrefs.Save();
            Debug.Log("[Offline] forgot " + shortId);
        }
    }
}
