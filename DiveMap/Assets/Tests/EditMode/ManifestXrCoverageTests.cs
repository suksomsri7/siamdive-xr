using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// Every module the app can be asked to place must have an <c>xrGlbUrl</c>, or be on the short
    /// list of ids that have no GLB at all.
    ///
    /// 🔴 Why this test exists. The Atlantis map shipped for weeks as a field of yellow-green
    /// placeholder boxes and nothing anywhere said so. The eleven <c>ruin:*</c> modules had no XR
    /// build, so the loader fell back to the web file — and the web files declare
    /// <c>EXT_texture_webp</c>, which glTFast does not implement, so every one of them failed the
    /// same way, silently, on every device. Nothing was broken in the app: the manifest simply
    /// pointed somewhere the app cannot read, and only a person looking at the screen could tell.
    ///
    /// So the assertion is about the manifest, not about the loader. Two things are pinned:
    ///   1. every module has an xrGlbUrl (the nineteen procedural ids below are built in code and
    ///      genuinely have no file — they are listed by name so that adding a twentieth is a
    ///      deliberate act, not a silent regression);
    ///   2. no xrGlbUrl points back into the website's model folders, which is what "fell back to
    ///      the web file" looked like from here.
    ///
    /// Reading the shipped JSON rather than a copy is the point — a checked-in fixture would have
    /// stayed green through the entire outage.
    /// </summary>
    public class ManifestXrCoverageTests
    {
        /// <summary>
        /// Ids built procedurally in <c>SceneBuilder</c> — rocks, corals, anemones, the four
        /// stand-in fish, two turtles and two wrecks. They have no GLB anywhere, on the web or on
        /// the CDN, so an absent xrGlbUrl is correct for exactly these and nothing else.
        /// </summary>
        static readonly HashSet<string> Procedural = new HashSet<string>
        {
            "rock:0", "rock:1", "rock:2", "rock:3",
            "coral:0", "coral:1", "coral:2", "coral:3",
            "anemone:0", "anemone:1", "anemone:2",
            "fish:0", "fish:1", "fish:2", "fish:3",
            "turtle:0", "turtle:1",
            "wreck:0", "wreck:1",
        };

        static string Manifest()
        {
            string json = RepoFiles.Read("Assets/StreamingAssets/asset_manifest.json");
            if (json == null)
                Assert.Fail("asset_manifest.json not found; searched upwards from " + RepoFiles.SearchedFrom);
            return json;
        }

        /// <summary>id → the xrGlbUrl next to it, or null when the module has none.</summary>
        static Dictionary<string, string> Modules()
        {
            string json = Manifest();
            var result = new Dictionary<string, string>();
            // Each module is a flat object; the id comes first and xrGlbUrl, when present, is
            // inside the same braces. Splitting on `"id":` keeps this independent of key order.
            string[] chunks = Regex.Split(json, "\"id\"\\s*:\\s*");
            for (int i = 1; i < chunks.Length; i++)
            {
                var idMatch = Regex.Match(chunks[i], "^\"([^\"]+)\"");
                if (!idMatch.Success) continue;
                string body = chunks[i];
                int end = body.IndexOf("\n  }", StringComparison.Ordinal);
                if (end > 0) body = body.Substring(0, end);
                var urlMatch = Regex.Match(body, "\"xrGlbUrl\"\\s*:\\s*\"([^\"]+)\"");
                result[idMatch.Groups[1].Value] = urlMatch.Success ? urlMatch.Groups[1].Value : null;
            }
            Assert.Greater(result.Count, 200, "manifest parsed to too few modules — parser is wrong, not the data");
            return result;
        }

        [Test]
        public void EveryModuleWithAFileHasAnXrBuild()
        {
            var missing = new List<string>();
            foreach (var kv in Modules())
                if (kv.Value == null && !Procedural.Contains(kv.Key))
                    missing.Add(kv.Key);

            Assert.IsEmpty(missing,
                "these modules have no xrGlbUrl and would load the web file (or fail): " + string.Join(", ", missing));
        }

        /// <summary>
        /// The eleven ruins by name. Losing these again is the exact regression that produced a
        /// map of placeholder boxes, and a count alone would not catch a rename.
        /// </summary>
        [Test]
        public void TheAtlantisRuinsPointAtTheCdn()
        {
            var mods = Modules();
            string[] ruins =
            {
                "ruin:byzantine_arch", "ruin:crystal_arch", "ruin:grand_byzantine", "ruin:domed_temple",
                "ruin:fantasy_gate", "ruin:stepped_stone", "ruin:ornate_monument", "ruin:broken_pillars",
                "ruin:long_arch", "ruin:ancient_byzantine", "ruin:ancient_ornate",
            };
            foreach (string id in ruins)
            {
                Assert.IsTrue(mods.ContainsKey(id), id + " has gone missing from the manifest");
                Assert.IsNotNull(mods[id], id + " has no xrGlbUrl — Atlantis renders it as a placeholder box");
                StringAssert.Contains("/models/xr/", mods[id], id + " does not point at an XR build");
            }
        }

        /// <summary>
        /// An xrGlbUrl that resolves to the website is the failure this test was written for: the
        /// site's GLBs require EXT_texture_webp, which glTFast cannot read, so the module loads as
        /// a placeholder on every device with no error anywhere.
        /// </summary>
        [Test]
        public void NoXrUrlFallsBackToTheWebsite()
        {
            var offenders = new List<string>();
            foreach (var kv in Modules())
            {
                if (kv.Value == null) continue;
                if (kv.Value.Contains("maps.siamdive.com/models/warp/") ||
                    kv.Value.Contains("maps.siamdive.com/models/marine/"))
                    offenders.Add(kv.Key + " → " + kv.Value);
            }
            Assert.IsEmpty(offenders, "xrGlbUrl must not point at the website's WebP models: " + string.Join(", ", offenders));
        }
    }
}
