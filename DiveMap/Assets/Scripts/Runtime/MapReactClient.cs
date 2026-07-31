using System;
using System.Collections;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace DiveMap.Runtime
{
    /// <summary>
    /// The map hub's ❤️ / 🚩 endpoints.
    ///
    /// Verified against the maps repo (<c>src/app/api/dive-sites/[shortId]/</c>):
    /// <code>
    ///   POST /api/dive-sites/{shortId}/react   { deviceId, kind:"like"|"fav", on }
    ///        → { likeCount, favoriteCount }        400 without deviceId · 429 when rate-limited
    ///   POST /api/dive-sites/{shortId}/report  { deviceId, reason? }
    ///        → { ok, count, hidden }
    /// </code>
    ///
    /// ⚠️ <c>deviceId</c> is REQUIRED by the route — the shipped RN client omits it
    /// (siamdive-rn <c>src/lib/dive-map-client.ts</c> sends only <c>{kind, on}</c>), so every
    /// like there 400s inside a swallowed catch and only ever changes the count on screen.
    /// Do not copy that call shape.
    ///
    /// The server has no per-device reaction table, so re-tapping would keep incrementing:
    /// <see cref="DiveMap.Core.LikedMaps"/> is what stops that, not the API.
    /// </summary>
    public static class MapReactClient
    {
        public const int TimeoutSeconds = 15;

        /// <summary>Counts returned by the react endpoint.</summary>
        public struct Counts
        {
            public int Like;
            public int Favorite;
        }

        /// <summary>
        /// Coroutine. <paramref name="onDone"/> gets the server's counts, or the failure
        /// reason — the caller has already updated the UI optimistically, exactly like the web.
        /// </summary>
        public static IEnumerator React(string shortId, bool on, Action<Counts?> onDone,
                                        string kind = "like", string baseUrl = MapApiClient.DefaultBaseUrl)
        {
            if (string.IsNullOrEmpty(shortId)) { onDone?.Invoke(null); yield break; }

            string url = (baseUrl ?? MapApiClient.DefaultBaseUrl).TrimEnd('/')
                       + "/api/dive-sites/" + UnityWebRequest.EscapeURL(shortId) + "/react";
            var body = new JObject
            {
                ["deviceId"] = WalletClient.DeviceId,
                ["kind"] = kind,
                ["on"] = on,
            };

            string text = null;
            string err = null;
            using (UnityWebRequest req = Post(url, body.ToString(Newtonsoft.Json.Formatting.None)))
            {
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                    text = req.downloadHandler != null ? req.downloadHandler.text : null;
                else
                    err = $"({(long)req.responseCode}) {req.error}";
            }

            if (err != null)
            {
                Debug.LogWarning($"[UI] react {kind} {shortId} failed: {err}");
                onDone?.Invoke(null);
                yield break;
            }

            Counts? counts = null;
            try
            {
                JObject o = JObject.Parse(text ?? "{}");
                counts = new Counts
                {
                    Like = o["likeCount"] != null ? (int)o["likeCount"] : 0,
                    Favorite = o["favoriteCount"] != null ? (int)o["favoriteCount"] : 0,
                };
            }
            catch (Exception e)
            {
                Debug.LogWarning("[UI] react parse failed: " + e.Message);
            }

            if (counts.HasValue)
                Debug.Log($"[UI] react {kind} {shortId} on={on} → likes={counts.Value.Like}");
            onDone?.Invoke(counts);
        }

        /// <summary>Coroutine. Report a community map for moderation.</summary>
        public static IEnumerator Report(string shortId, Action<bool, bool> onDone,
                                         string baseUrl = MapApiClient.DefaultBaseUrl)
        {
            if (string.IsNullOrEmpty(shortId)) { onDone?.Invoke(false, false); yield break; }

            string url = (baseUrl ?? MapApiClient.DefaultBaseUrl).TrimEnd('/')
                       + "/api/dive-sites/" + UnityWebRequest.EscapeURL(shortId) + "/report";
            var body = new JObject { ["deviceId"] = WalletClient.DeviceId };

            bool ok = false, hidden = false;
            using (UnityWebRequest req = Post(url, body.ToString(Newtonsoft.Json.Formatting.None)))
            {
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    ok = true;
                    try
                    {
                        JObject o = JObject.Parse(req.downloadHandler != null ? req.downloadHandler.text : "{}");
                        hidden = o["hidden"] != null && (bool)o["hidden"];
                    }
                    catch { /* the report landed; the flag is cosmetic */ }
                }
                else
                {
                    Debug.LogWarning($"[UI] report {shortId} failed ({(long)req.responseCode}) {req.error}");
                }
            }

            Debug.Log($"[UI] report {shortId} ok={ok} hidden={hidden}");
            onDone?.Invoke(ok, hidden);
        }

        private static UnityWebRequest Post(string url, string json)
        {
            // UnityWebRequest.Post(url, string) form-encodes the body — the API wants raw JSON.
            var req = new UnityWebRequest(url, "POST")
            {
                uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = TimeoutSeconds,
            };
            req.SetRequestHeader("Content-Type", "application/json");
            return req;
        }
    }
}
