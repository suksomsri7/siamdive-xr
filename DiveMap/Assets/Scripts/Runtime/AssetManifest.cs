using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace DiveMap.Runtime
{
    /// <summary>
    /// The asset registry shipped in StreamingAssets/asset_manifest.json (275 modules).
    /// Maps an item.assetId → module → absolute GLB URL.
    ///
    /// On Android, StreamingAssets lives INSIDE the APK jar, so it MUST be read with
    /// UnityWebRequest (File.IO fails). We therefore expose <see cref="Load"/> as a
    /// coroutine, and a pure <see cref="FromJson"/> factory for tests / other callers.
    /// </summary>
    public sealed class AssetManifest
    {
        public string BaseUrl { get; private set; }

        private readonly Dictionary<string, Module> _byId =
            new Dictionary<string, Module>(StringComparer.Ordinal);

        public int Count => _byId.Count;

        public sealed class Module
        {
            public string Id;
            public string Kind;
            public string Name;
            public string GlbUrl;
            /// <summary>Optional XR-optimized GLB (KTX2/Draco, LOD0). Preferred over <see cref="GlbUrl"/> when present.</summary>
            public string XrGlbUrl;
            /// <summary>Optional XR LOD1 (lower-poly). Carried in data for later use; not selected yet.</summary>
            public string XrGlbUrlLod1;
            public double DefaultScale = 1;
            public double DefaultY = 0;
            public bool Animated;
            public string School;   // optional
            public string Fx;       // optional
            public string Behavior; // optional
        }

        private AssetManifest() { }

        // ── Loading ──────────────────────────────────────────────────────────────

        public static string ManifestUri()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "asset_manifest.json");
            // On Android the path is already a jar:file:// URI; elsewhere it is a plain
            // filesystem path that UnityWebRequest needs prefixed with file://.
            return path.Contains("://") ? path : "file://" + path;
        }

        /// <summary>Coroutine: read + parse the StreamingAssets manifest.</summary>
        public static IEnumerator Load(Action<AssetManifest> onSuccess, Action<string> onError)
        {
            string uri = ManifestUri();
            using (UnityWebRequest req = UnityWebRequest.Get(uri))
            {
                req.timeout = 20;
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke($"อ่าน asset_manifest.json ไม่ได้: {req.error}");
                    yield break;
                }

                AssetManifest m;
                try
                {
                    m = FromJson(req.downloadHandler.text);
                }
                catch (Exception e)
                {
                    onError?.Invoke("asset_manifest.json ผิดรูปแบบ: " + e.Message);
                    yield break;
                }

                onSuccess?.Invoke(m);
            }
        }

        // ── Parsing (pure, testable) ─────────────────────────────────────────────

        public static AssetManifest FromJson(string json)
        {
            var man = new AssetManifest();
            JObject root = JObject.Parse(json);

            man.BaseUrl = (string)root["baseUrl"];

            if (root["modules"] is JArray modules)
            {
                foreach (JToken t in modules)
                {
                    if (t is not JObject o) continue;
                    string id = (string)o["id"];
                    if (string.IsNullOrEmpty(id)) continue;

                    var mod = new Module
                    {
                        Id = id,
                        Kind = (string)o["kind"],
                        Name = (string)o["name"],
                        GlbUrl = (string)o["glbUrl"],
                        XrGlbUrl = (string)o["xrGlbUrl"],
                        XrGlbUrlLod1 = (string)o["xrGlbUrlLod1"],
                        DefaultScale = o["defaultScale"] != null ? (double)o["defaultScale"] : 1,
                        DefaultY = o["defaultY"] != null ? (double)o["defaultY"] : 0,
                        Animated = o["animated"] != null && (bool)o["animated"],
                        School = (string)o["school"],
                        Fx = (string)o["fx"],
                        Behavior = (string)o["behavior"],
                    };
                    man._byId[id] = mod; // last wins on dup id
                }
            }
            return man;
        }

        // ── Lookup ───────────────────────────────────────────────────────────────

        public Module Get(string assetId)
        {
            if (string.IsNullOrEmpty(assetId)) return null;
            return _byId.TryGetValue(assetId, out Module m) ? m : null;
        }

        /// <summary>
        /// Absolute GLB URL for an assetId, or null when the id is unknown or has no
        /// usable url. Prefers the XR-optimized GLB (<see cref="Module.XrGlbUrl"/>,
        /// KTX2/Draco LOD0) when present, otherwise the web <see cref="Module.GlbUrl"/>.
        /// Normalizes both "/models/x.glb" and "models/x.glb" against baseUrl, and
        /// passes through already-absolute http(s) URLs untouched.
        /// </summary>
        public string ResolveUrl(string assetId)
        {
            Module m = Get(assetId);
            if (m == null) return null;

            // XR variant wins when available (real KTX2 textures vs the WebP web GLBs).
            string raw = !string.IsNullOrWhiteSpace(m.XrGlbUrl) ? m.XrGlbUrl : m.GlbUrl;
            return Normalize(raw);
        }

        /// <summary>
        /// Absolute URL of the XR LOD1 (lower-poly) GLB for an assetId, or null when the id
        /// is unknown or ships no LOD1. WO-XR-04.1: a big school of a heavy model instances
        /// LOD1 instead of LOD0 (see <see cref="DiveMap.Core.FishAssetPick"/>).
        /// </summary>
        public string ResolveLod1Url(string assetId)
        {
            Module m = Get(assetId);
            return m == null ? null : Normalize(m.XrGlbUrlLod1);
        }

        private string Normalize(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            string g = url.Trim();
            if (g.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                g.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return g;
            }

            string baseTrim = string.IsNullOrEmpty(BaseUrl) ? "" : BaseUrl.TrimEnd('/');
            string rel = g.TrimStart('/');
            return baseTrim.Length == 0 ? rel : baseTrim + "/" + rel;
        }
    }
}
