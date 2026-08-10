using System;
using System.Collections;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using DiveMap.Core;

namespace DiveMap.Runtime
{
    /// <summary>
    /// Fetches a live dive-site scene from the production SiamDive maps API and
    /// returns it as a <see cref="SceneData"/> (JObject-preserving, DESIGN_DOC §1.2).
    ///
    /// Verified production contract (WO-XR-01):
    ///   GET https://maps.siamdive.com/api/dive-sites/{shortId}
    ///     → { "site": { ... } }   (wrapper)   OR   { ... }   (flat)   — handle both.
    ///   site.items / site.pins / site.env may each be a real JSON value OR a
    ///   string-encoded JSON blob — decode defensively before wrapping.
    ///
    /// Coroutine-based (UnityWebRequest) so nothing blocks the main thread; no .Result.
    /// </summary>
    public static class MapApiClient
    {
        public const string DefaultBaseUrl = "https://maps.siamdive.com";
        public const string DefaultShortId = "wl6zwxh1tdgn"; // Htms Chang demo (14 items)
        public const int TimeoutSeconds = 20;

        /// <summary>
        /// 🔴 The <c>?deviceId=</c> is not optional decoration (WO-MERGE P1d). It is the ONLY thing
        /// in this request that says who is asking, and <c>canEdit</c> in the response is the
        /// server's answer to exactly that question. Without it every map came back
        /// <c>canEdit:false</c> — including the owner's own, including admin's — and the app told
        /// the owner "This map is not editable". See <see cref="SiteRequest"/> for the web call
        /// this now matches and for why no token or email belongs here.
        ///
        /// <paramref name="deviceId"/> defaults to null so the pure/URL-only callers and tests can
        /// still ask for the anonymous form; <see cref="Fetch"/> always passes the real one.
        /// </summary>
        public static string BuildUrl(string shortId, string baseUrl = DefaultBaseUrl,
                                      string deviceId = null)
            => SiteRequest.Url(baseUrl ?? DefaultBaseUrl, shortId, deviceId);

        /// <summary>
        /// Coroutine. Calls <paramref name="onSuccess"/> with the parsed scene, or
        /// <paramref name="onError"/> with a human-readable message (HTTP or parse).
        /// </summary>
        public static IEnumerator Fetch(
            string shortId,
            Action<SceneData> onSuccess,
            Action<string> onError,
            string baseUrl = DefaultBaseUrl)
        {
            // WalletClient.DeviceId is the app's single identity — and in the embedded build it is
            // the HOST's device id, injected at boot, which is what makes the SiamDive app's
            // signed-in user recognisable to this request as the owner of their own maps.
            string url = BuildUrl(shortId, baseUrl, WalletClient.DeviceId);

            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                req.timeout = TimeoutSeconds;
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke($"โหลดแมพไม่สำเร็จ ({(long)req.responseCode}) {req.error}");
                    yield break;
                }

                string body = req.downloadHandler != null ? req.downloadHandler.text : null;
                if (string.IsNullOrEmpty(body))
                {
                    onError?.Invoke("เซิร์ฟเวอร์ตอบกลับว่าง");
                    yield break;
                }

                SceneData scene;
                try
                {
                    scene = ParseSiteResponse(body);
                }
                catch (Exception e)
                {
                    onError?.Invoke("อ่านข้อมูลแมพไม่ได้: " + e.Message);
                    yield break;
                }

                onSuccess?.Invoke(scene);
            }
        }

        /// <summary>
        /// Parse a raw API body into SceneData, unwrapping { site: {...} } and
        /// decoding any string-encoded items/pins/env. Exposed for testability.
        /// </summary>
        public static SceneData ParseSiteResponse(string body)
        {
            JObject root = JObject.Parse(body);

            // Unwrap { site: {...} } when present; otherwise treat root as the site.
            JObject site = root["site"] is JObject s ? s : root;

            // canEdit / owned sit OUTSIDE site (route.ts:93) and would be lost by the unwrap.
            // They are the server's own verdict on whether a save will be accepted — the app
            // needs it BEFORE charging a player for something it cannot persist.
            if (root["canEdit"] != null) site["canEdit"] = root["canEdit"];
            if (root["owned"] != null) site["owned"] = root["owned"];

            // items / pins / env may arrive as JSON strings — decode in place so the
            // rest of the pipeline always sees real JArray / JObject tokens.
            NormalizeEmbedded(site, "items");
            NormalizeEmbedded(site, "pins");
            NormalizeEmbedded(site, "env");

            return new SceneData(site);
        }

        private static void NormalizeEmbedded(JObject site, string key)
        {
            JToken tok = site[key];
            if (tok == null) return;

            // Already structured — leave untouched (preserve unknown fields, §1.2 rule 1).
            if (tok.Type == JTokenType.Object || tok.Type == JTokenType.Array) return;

            if (tok.Type == JTokenType.String)
            {
                string raw = tok.Value<string>();
                if (string.IsNullOrWhiteSpace(raw)) return;
                try
                {
                    site[key] = JToken.Parse(raw);
                }
                catch
                {
                    // Not JSON after all — leave the original value in place.
                }
            }
        }
    }
}
