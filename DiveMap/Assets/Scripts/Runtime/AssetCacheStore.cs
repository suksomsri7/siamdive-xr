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
    /// Least-recently-used eviction comes from the shipped app
    /// (siamdive-rn/src/lib/offline/assets.ts:16); the cap is this app's own (1 GB — see
    /// <see cref="AssetCache.BudgetBytes"/>, which explains why it is no longer that app's
    /// 220 MB). The decisions themselves live in <see cref="AssetCache"/>
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
        /// <summary>Files we had but would not serve because their generation is behind.</summary>
        public static int StaleSkipped { get; private set; }
        /// <summary>Times a stale file was served anyway because the network could not replace it.</summary>
        public static int StaleServed { get; private set; }
        /// <summary>Downloads refused because the bytes were not the whole file — see <see cref="Store"/>.</summary>
        public static int Rejected { get; private set; }

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

        /// <summary>
        /// Is there a file on disk for this URL — of ANY generation? "We have something" and "we
        /// have something worth serving" are different questions since generations existed; this
        /// answers the first. Ask <see cref="Resolve"/> for the second.
        /// </summary>
        public static bool Has(string url)
        {
            string p = PathFor(url);
            return p != null && File.Exists(p);
        }

        /// <summary>
        /// What to hand the loader: a local <c>file://</c> when we have a copy we still trust,
        /// otherwise the URL unchanged. Touches the entry so eviction knows this model is still in
        /// use — a model used every dive must not be dropped for one downloaded once and never
        /// opened again.
        ///
        /// 🔴 "Still trust" is the whole point of this method now. Before generations, a file on
        /// disk was served unconditionally, so the 444 GLBs repaired on the CDN under their
        /// existing URLs never reached anyone who had opened those maps once. A copy written by an
        /// older generation is deliberately NOT returned here — the caller goes to the network —
        /// but it is not deleted either: <see cref="StaleUri"/> can still reach it if the network
        /// turns out not to be there. Nothing is thrown away before its replacement is in hand.
        /// </summary>
        public static string Resolve(string url)
        {
            string key = AssetCache.KeyFor(url);
            string p = key == null ? null : Path.Combine(Directory, key);
            if (p == null || !File.Exists(p))
            {
                if (AssetCache.IsCacheable(url)) Misses++;
                return url;
            }

            List<AssetCache.Entry> all = Entries;
            int at = IndexOf(all, key);

            // On disk but not in the index — an index lost to a crash, or a build older than the
            // index format. We cannot know which generation wrote it, so it is the oldest one.
            // Adopt it at 0 so the budget can see it; the next successful download re-stamps it.
            if (at < 0)
            {
                long size = 0;
                try { size = new FileInfo(p).Length; } catch { }
                if (size > 0) Record(key, size, 0);
            }

            int gen = at >= 0 ? all[at].Gen : 0;
            if (!AssetCache.IsFresh(gen))
            {
                StaleSkipped++;
                return url;                       // fetch the repaired bytes; keep the old file
            }

            Hits++;
            Touch(all, at, key);
            return "file://" + p;
        }

        /// <summary>
        /// The local copy whatever its generation, or null when there is none.
        ///
        /// The offline half of <see cref="Resolve"/>: a model we know is out of date is still a
        /// model, and on a boat with no signal it is the difference between the dive the user
        /// downloaded and a field of grey placeholders. Only ever reached after a download has
        /// actually failed, so a device with a signal never sees the old file.
        /// </summary>
        public static string StaleUri(string url)
        {
            string p = PathFor(url);
            if (p == null || !File.Exists(p)) return null;
            StaleServed++;
            return "file://" + p;
        }

        /// <summary>Bytes straight off the disk copy, any generation, or null. No web request for
        /// a file we are holding.</summary>
        public static byte[] ReadLocal(string url)
        {
            string p = PathFor(url);
            if (p == null || !File.Exists(p)) return null;
            try { return File.ReadAllBytes(p); }
            catch (Exception e) { Debug.LogWarning("[Cache] cannot read " + p + ": " + e.Message); return null; }
        }

        /// <summary>The local URI for a file we know is there.</summary>
        public static string FileUri(string url)
        {
            string p = PathFor(url);
            return p != null && File.Exists(p) ? "file://" + p : url;
        }

        /// <summary>
        /// A download that never comes back is worse than one that fails, because the caller's
        /// fallback — the stale copy on disk — never gets its turn. 60 s clears the largest model
        /// we ship (4.2 MB) on anything above ~600 kbps and still leaves half of SceneBuilder's
        /// 120 s scene budget for the fallback to land.
        /// </summary>
        public const int TimeoutSeconds = 60;

        /// <summary>
        /// Download the bytes. Returns null on any failure — offline, 404, timeout — because
        /// every one of those means the same thing to the caller: carry on without it.
        ///
        /// 🔴 The no-cache headers are not superstition. Bunny serves these models with
        /// <c>cache-control: public, max-age=2592000</c> (measured), and this request goes out
        /// through NSURLSession on iOS, which keeps its own shared URL cache on disk. Having just
        /// decided the copy WE hold is out of date, being handed the platform's copy of the same
        /// out-of-date bytes would defeat the generation check silently and look exactly like the
        /// bug not being fixed. We only reach this method when we already want the network.
        /// </summary>
        /// <summary>
        /// โหลดไฟล์ พร้อมลองซ้ำหนึ่งครั้ง.
        ///
        /// 🔴 16 ส.ค. 2026 — user: "บางครั้งเข้า Unity แต่โมเดลมาไม่ครบ" · คำว่า "บางครั้ง" คือ
        /// ลายเซ็นของการสะดุดชั่วคราว ไม่ใช่ไฟล์หาย: แมพหนึ่งอันยิงขอไฟล์หลายสิบก้อนพร้อมกันบน
        /// เน็ตมือถือ พลาดก้อนหนึ่ง = ของชิ้นนั้นกลายเป็นกล่องเทาทั้งรอบ และไม่มีอะไรลองใหม่ให้
        /// ⇒ ลองซ้ำหนึ่งครั้งหลังเว้นจังหวะสั้น ๆ (ครั้งเดียวพอ: ถ้าล้มสองครั้งติดมักคือไม่มีเน็ตจริง
        /// ซึ่งมีทางลงอยู่แล้ว — ใช้สำเนาเก่าในเครื่อง)
        /// </summary>
        public static async Task<byte[]> Download(string url)
        {
            byte[] first = await DownloadOnce(url);
            if (first != null) return first;
            await Task.Delay(700);
            byte[] second = await DownloadOnce(url);
            if (second == null) Debug.Log("[Cache] ยังโหลดไม่ได้หลังลองซ้ำ: " + url);
            return second;
        }

        private static Task<byte[]> DownloadOnce(string url)
        {
            var tcs = new TaskCompletionSource<byte[]>();
            try
            {
                UnityWebRequest req = UnityWebRequest.Get(url);
                req.timeout = TimeoutSeconds;
                req.SetRequestHeader("Cache-Control", "no-cache");
                req.SetRequestHeader("Pragma", "no-cache");
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

            // 🔴 Never stamp rubbish with the current generation. A half-arrived download or a
            // captive portal's HTML login page (served 200, which is how a boat's wifi answers
            // everything) would otherwise be cached as a model and served for good — the one
            // failure the generation gate cannot undo, because such a file is not stale.
            // Refusing costs a re-fetch: the caller falls back to the network URL, as it did
            // before this cache existed.
            if (!AssetCache.LooksComplete(url, data))
            {
                Rejected++;
                Debug.LogWarning($"[Cache] refusing {data.Length} B for {url} — not a complete GLB " +
                                 "(truncated download, or a portal/error page served as 200)");
                return false;
            }

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
            Record(key, data.Length, AssetCache.Generation);
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

        private static int IndexOf(List<AssetCache.Entry> all, string key)
        {
            for (int i = 0; i < all.Count; i++) if (all[i].Key == key) return i;
            return -1;
        }

        private static void Record(string key, long bytes, int gen)
        {
            List<AssetCache.Entry> all = Entries;
            var row = new AssetCache.Entry { Key = key, Bytes = bytes, LastUsed = Now, Gen = gen };
            int at = IndexOf(all, key);
            if (at >= 0) all[at] = row; else all.Add(row);
            Save(all);
        }

        /// <summary>Mark an already-decoded entry as used just now. <paramref name="at"/> below zero
        /// means it is not in the index, which <see cref="Resolve"/> has already dealt with.</summary>
        private static void Touch(List<AssetCache.Entry> all, int at, string key)
        {
            if (at < 0 || at >= all.Count) return;
            all[at] = new AssetCache.Entry
            {
                Key = key,
                Bytes = all[at].Bytes,
                LastUsed = Now,
                Gen = all[at].Gen,     // touching is not re-validating
            };
            Save(all);
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
