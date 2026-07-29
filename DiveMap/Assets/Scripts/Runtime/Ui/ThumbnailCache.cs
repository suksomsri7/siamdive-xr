using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// Small in-memory image cache for map-list thumbnails (WO-XR-05.2).
    ///
    /// Thumbnails come from the Bunny CDN (https://siamdive-cdn.b-cdn.net/dive-media/*.jpg).
    /// Downloads are capped at <see cref="MaxConcurrent"/> so scrolling a long list does
    /// not open dozens of sockets on a phone, and identical URLs are coalesced into a
    /// single request with a waiter list.
    ///
    /// Textures are kept for the lifetime of the app (a handful of small JPEGs); the
    /// cache is cleared only when the component is destroyed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ThumbnailCache : MonoBehaviour
    {
        public const int MaxConcurrent = 4;
        public const int TimeoutSeconds = 15;

        private readonly Dictionary<string, Texture2D> _cache =
            new Dictionary<string, Texture2D>(StringComparer.Ordinal);

        private readonly Dictionary<string, List<Action<Texture2D>>> _waiters =
            new Dictionary<string, List<Action<Texture2D>>>(StringComparer.Ordinal);

        private readonly Queue<string> _queue = new Queue<string>();
        private int _active;

        /// <summary>Number of distinct thumbnails successfully downloaded so far (QC signal).</summary>
        public int LoadedCount { get; private set; }

        public int FailedCount { get; private set; }

        /// <summary>Request a texture. <paramref name="onReady"/> fires immediately on a cache hit.</summary>
        public void Request(string url, Action<Texture2D> onReady)
        {
            if (string.IsNullOrEmpty(url) || onReady == null) return;

            if (_cache.TryGetValue(url, out Texture2D cached))
            {
                Invoke(onReady, cached);
                return;
            }

            if (_waiters.TryGetValue(url, out List<Action<Texture2D>> list))
            {
                list.Add(onReady);
                return;
            }

            _waiters[url] = new List<Action<Texture2D>> { onReady };
            _queue.Enqueue(url);
            Pump();
        }

        private void Pump()
        {
            while (_active < MaxConcurrent && _queue.Count > 0)
            {
                _active++;
                StartCoroutine(Download(_queue.Dequeue()));
            }
        }

        private IEnumerator Download(string url)
        {
            Texture2D tex = null;

            using (UnityWebRequest req = UnityWebRequestTexture.GetTexture(url))
            {
                req.timeout = TimeoutSeconds;
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    tex = DownloadHandlerTexture.GetContent(req);
                    if (tex != null)
                    {
                        tex.wrapMode = TextureWrapMode.Clamp;
                        _cache[url] = tex;
                        LoadedCount++;
                    }
                }
                else
                {
                    FailedCount++;
                    Debug.LogWarning($"[UI] thumb failed ({(long)req.responseCode}) {url} — {req.error}");
                }
            }

            if (_waiters.TryGetValue(url, out List<Action<Texture2D>> list))
            {
                _waiters.Remove(url);
                for (int i = 0; i < list.Count; i++) Invoke(list[i], tex);
            }

            _active--;
            Pump();
        }

        private static void Invoke(Action<Texture2D> cb, Texture2D tex)
        {
            if (cb == null || tex == null) return;
            try { cb(tex); }
            catch (Exception e) { Debug.LogWarning("[UI] thumb callback: " + e.Message); }
        }

        private void OnDestroy()
        {
            _cache.Clear();
            _waiters.Clear();
            _queue.Clear();
        }
    }
}
