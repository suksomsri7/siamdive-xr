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

        /// <summary>
        /// Every module, for callers that browse the registry rather than resolve one id —
        /// the palette groups the whole thing into its display categories.
        /// </summary>
        public IEnumerable<Module> All => _byId.Values;

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
            /// <summary>
            /// Optional LOD0 whose KTX2 textures are already ASTC 4x4 rather than a
            /// Basis-Universal intermediate. Taken over <see cref="XrGlbUrl"/> only on a device that
            /// can sample ASTC — see <see cref="DiveMap.Core.AstcAssetPick"/> for why it is a
            /// choice and not a replacement.
            /// </summary>
            public string XrGlbUrlAstc;
            /// <summary>ASTC twin of <see cref="XrGlbUrlLod1"/>, picked by the same rule.</summary>
            public string XrGlbUrlAstcLod1;
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
                        // Absent from every module the day this shipped, and that is the design:
                        // a missing key reads as null and the model resolves exactly as before, so
                        // the ASTC file set can be filled in one model at a time.
                        XrGlbUrlAstc = (string)o["xrGlbUrlAstc"],
                        XrGlbUrlAstcLod1 = (string)o["xrGlbUrlAstcLod1"],
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
            return Normalize(PickAstc(assetId, m.XrGlbUrlAstc, raw, false));
        }

        /// <summary>
        /// Absolute URL of the XR LOD1 (lower-poly) GLB for an assetId, or null when the id
        /// is unknown or ships no LOD1. WO-XR-04.1: a big school of a heavy model instances
        /// LOD1 instead of LOD0 (see <see cref="DiveMap.Core.FishAssetPick"/>).
        /// </summary>
        public string ResolveLod1Url(string assetId)
        {
            Module m = Get(assetId);
            if (m == null) return null;
            return Normalize(PickAstc(assetId, m.XrGlbUrlAstcLod1, m.XrGlbUrlLod1, true));
        }

        // ── ASTC file set ────────────────────────────────────────────────────────

        /// <summary>
        /// Test seam. Null means "ask the device"; an EditMode test sets it because CI's GL context
        /// answers no to ASTC and a test that only ever sees the fallback proves half the rule.
        /// </summary>
        internal static bool? AstcSupportOverride;

        /// <summary>
        /// 🔴 THE SAME QUESTION THE TRANSCODE TARGET ASKS, deliberately the same accessor rather
        /// than a second <c>SystemInfo</c> call: if these two ever disagreed, the app would download
        /// an ASTC file and then decline to claim its textures — the one combination in which the
        /// model loads and looks worse than it did before.
        /// </summary>
        private static bool AstcSupported =>
            AstcSupportOverride ?? LinearDataTextures.AstcSupported;

        /// <summary>
        /// Logged ids, so the choice is provable without one line per instance of a model — a
        /// reef with 40 copies of the same coral would otherwise bury the log it exists to write.
        /// Per-instance rather than static: a reloaded manifest is a new run and deserves new lines.
        /// </summary>
        private readonly HashSet<string> _pickLogged = new HashSet<string>(StringComparer.Ordinal);

        private string PickAstc(string assetId, string astcUrl, string fallbackUrl, bool lod1)
        {
            DiveMap.Core.AstcAssetPick.Choice choice =
                DiveMap.Core.AstcAssetPick.Pick(astcUrl, fallbackUrl, AstcSupported);

            // Only models that actually ship an ASTC build are worth a line; for the other 275 the
            // pick is a no-op and saying so 275 times is noise, not evidence.
            if (!string.IsNullOrWhiteSpace(astcUrl) &&
                _pickLogged.Add((lod1 ? "1:" : "0:") + assetId))
            {
                Debug.Log(DiveMap.Core.AstcAssetPick.LogLine(assetId, choice, lod1));
            }

            return choice.Url;
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
