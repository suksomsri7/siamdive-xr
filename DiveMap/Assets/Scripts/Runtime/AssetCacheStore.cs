using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DiveMap.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace DiveMap.Runtime
{
    /// <summary>
    /// J7 — the models kept on the device, so a map opened once opens again with no signal.
    ///
    /// <see cref="OfflineStore"/> keeps the map's JSON; this keeps its GLBs. Both halves are
    /// needed before "ทัวร์ออฟไลน์" means anything: a map with its JSON but no models opens as a
    /// field of grey placeholders, which is worse than an honest error because it looks like the
    /// map itself is broken.
    ///
    /// 🔎 Read-through, not a download button. Every model the app fetches is written to disk on
    /// the way past, so "maps you have opened" and "maps you can open offline" stay the same set —
    /// the same principle <see cref="OfflineStore"/> uses for the JSON, and the reason neither
    /// needs a button the user has to remember to press before they leave the pier.
    ///
    /// The 220 MB cap and least-recently-used eviction come from the shipped app
    /// (siamdive-rn/src/lib/offline/assets.ts:16), so the two products agree on how much of a
    /// phone this is allowed to take. The decisions themselves live in <see cref="AssetCache"/>
    /// where they can be tested; this class only does the I/O.
    /// </summary>
    public static class AssetCacheStore
    {
        private static string _dir;

        /// <summary>Where the files live. Created on first use.</summary>
        public static string Directory
        {
            get
            {
                if (_dir != null) return _dir;
                _dir = Path.Combine(Application.persistentDataPath, "glb");
                try { System.IO.Directory.CreateDirectory(_dir); }
                catch (Exception e) { Debug.LogWarning("[Cache] cannot create " + _dir + ": " + e.Message); }
                return _dir;
            }
        }

        // ── QC surface ───────────────────────────────────────────────────────────
        public static int Hits { get; private set; }
        public static int Misses { get; private set; }
        public static int Stored { get; private set; }
        public static int Evicted { get; private set; }

        /// <summary>Everything on disk, from the index.</summary>
        public static List<AssetCache.Entry> Entries =>
            AssetCache.Decode(PlayerPrefs.GetString(AssetCache.IndexKey, ""));

        public static long TotalBytes => AssetCache.TotalBytes(Entries);

        /// <summary>Human-readable total, for the settings line.</summary>
        public static string TotalLabel => AssetCache.FormatSize(TotalBytes);

        public static string PathFor(string url)
        {
            string key = AssetCache.KeyFor(url);
            return key == null ? null : Path.Combine(Directory, key);
        }

        public static bool Has(string url)
        {
            string p = PathFor(url);
            return p != null && File.Exists(p);
        }

        /// <summary>
        /// What to hand the loader: a local <c>file://</c> when we have it, otherwise the URL
        /// unchanged. Touches the entry so eviction knows this model is still in use — a model
        /// used every dive must not be dropped for one downloaded once and never opened again.
        /// </summary>
        public static string Resolve(string url)
        {
            string p = PathFor(url);
            if (p == null || !File.Exists(p))
            {
                if (AssetCache.IsCacheable(url)) Misses++;
                return url;
            }

            Hits++;
            Touch(AssetCache.KeyFor(url));
            return "file://" + p;
        }

        /// <summary>The local URI for a file we know is there.</summary>
        public static string FileUri(string url)
        {
            string p = PathFor(url);
            return p != null && File.Exists(p) ? "file://" + p : url;
        }

        /// <summary>
        /// Download the bytes. Returns null on any failure — offline, 404, timeout — because
        /// every one of those means the same thing to the caller: carry on without it.
        /// </summary>
        public static Task<byte[]> Download(string url)
        {
            var tcs = new TaskCompletionSource<byte[]>();
            try
            {
                UnityWebRequest req = UnityWebRequest.Get(url);
                UnityWebRequestAsyncOperation op = req.SendWebRequest();
                op.completed += _ =>
                {
                    byte[] data = req.result == UnityWebRequest.Result.Success
                        ? req.downloadHandler.data : null;
                    if (data == null)
                        Debug.Log($"[Cache] fetch failed {req.result} {url}");
                    req.Dispose();
                    tcs.TrySetResult(data);
                };
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Cache] fetch threw: " + e.Message);
                tcs.TrySetResult(null);
            }
            return tcs.Task;
        }

        /// <summary>
        /// Write a model to the cache. False when it could not be stored, which is not an error
        /// worth stopping for: the map still loads from the bytes we already have in hand.
        /// </summary>
        public static bool Store(string url, byte[] data)
        {
            if (data == null || data.Length == 0) return false;
            string key = AssetCache.KeyFor(url);
            string path = PathFor(url);
            if (key == null || path == null) return false;

            try
            {
                // Write beside, then move: a half-written GLB that survives a kill would be served
                // forever afterwards as a "cache hit" and never re-downloaded.
                string tmp = path + ".part";
                File.WriteAllBytes(tmp, data);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Cache] cannot write " + path + ": " + e.Message);
                return false;
            }

            Stored++;
            Record(key, data.Length);
            Enforce();
            return true;
        }

        /// <summary>Drop everything (the settings screen's "clear downloads").</summary>
        public static void Clear()
        {
            foreach (AssetCache.Entry e in Entries) Delete(e.Key);
            PlayerPrefs.DeleteKey(AssetCache.IndexKey);
            PlayerPrefs.Save();
            Debug.Log("[Cache] cleared");
        }

        // ── index bookkeeping ────────────────────────────────────────────────────

        private static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        private static void Record(string key, long bytes)
        {
            List<AssetCache.Entry> all = Entries;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Key != key) continue;
                all[i] = new AssetCache.Entry { Key = key, Bytes = bytes, LastUsed = Now };
                Save(all);
                return;
            }
            all.Add(new AssetCache.Entry { Key = key, Bytes = bytes, LastUsed = Now });
            Save(all);
        }

        private static void Touch(string key)
        {
            List<AssetCache.Entry> all = Entries;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Key != key) continue;
                all[i] = new AssetCache.Entry { Key = key, Bytes = all[i].Bytes, LastUsed = Now };
                Save(all);
                return;
            }

            // On disk but not in the index — an index lost to a crash, or an older build. Adopt it
            // rather than ignore it, or the file would never be counted and never evicted.
            string path = Path.Combine(Directory, key);
            long size = 0;
            try { if (File.Exists(path)) size = new FileInfo(path).Length; } catch { }
            if (size > 0) Record(key, size);
        }

        private static void Save(List<AssetCache.Entry> all)
        {
            PlayerPrefs.SetString(AssetCache.IndexKey, AssetCache.Encode(all));
            PlayerPrefs.Save();
        }

        private static void Enforce()
        {
            List<AssetCache.Entry> all = Entries;
            List<AssetCache.Entry> plan = AssetCache.PlanEviction(all);
            if (plan.Count == 0) return;

            var gone = new HashSet<string>();
            foreach (AssetCache.Entry e in plan)
            {
                Delete(e.Key);
                gone.Add(e.Key);
                Evicted++;
            }
            all.RemoveAll(e => gone.Contains(e.Key));
            Save(all);
            Debug.Log($"[Cache] evicted {plan.Count} file(s) to stay under " +
                      $"{AssetCache.FormatSize(AssetCache.BudgetBytes)} — now {TotalLabel}");
        }

        private static void Delete(string key)
        {
            try
            {
                string p = Path.Combine(Directory, key);
                if (File.Exists(p)) File.Delete(p);
            }
            catch (Exception e) { Debug.LogWarning("[Cache] cannot delete " + key + ": " + e.Message); }
        }
    }
}
