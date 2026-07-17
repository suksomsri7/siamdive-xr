using DiveMap.Runtime;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// Unit tests for AssetManifest.ResolveUrl — the item.assetId → absolute GLB URL
    /// resolution, driven entirely from an embedded manifest string (no I/O, no
    /// UnityWebRequest) so they run in EditMode.
    /// </summary>
    public class AssetManifestTests
    {
        // baseUrl has a trailing slash; glbUrls mix leading-slash, no-slash and absolute.
        private const string ManifestJson = @"{
  ""baseUrl"": ""https://maps.siamdive.com/"",
  ""count"": 4,
  ""modules"": [
    { ""id"": ""rock:0"",  ""kind"": ""ROCK"",  ""name"": ""กลม"",   ""glbUrl"": ""/models/rock_0.glb"" },
    { ""id"": ""coral:2"", ""kind"": ""CORAL"", ""name"": ""พัด"",   ""glbUrl"": ""models/coral_2.glb"" },
    { ""id"": ""cdn:1"",   ""kind"": ""SPECIAL"", ""name"": ""cdn"", ""glbUrl"": ""https://cdn.example.com/a/x.glb"" },
    { ""id"": ""empty:1"", ""kind"": ""ROCK"",  ""name"": ""ว่าง"",  ""glbUrl"": """" }
  ]
}";

        private AssetManifest Load() => AssetManifest.FromJson(ManifestJson);

        [Test]
        public void ResolveUrl_LeadingSlash_NormalizesAgainstBase()
        {
            var m = Load();
            Assert.AreEqual("https://maps.siamdive.com/models/rock_0.glb", m.ResolveUrl("rock:0"));
        }

        [Test]
        public void ResolveUrl_NoLeadingSlash_NormalizesAgainstBase()
        {
            var m = Load();
            // Must not produce a double slash and must not drop the "models/" segment.
            Assert.AreEqual("https://maps.siamdive.com/models/coral_2.glb", m.ResolveUrl("coral:2"));
        }

        [Test]
        public void ResolveUrl_AbsoluteUrl_PassesThroughUnchanged()
        {
            var m = Load();
            Assert.AreEqual("https://cdn.example.com/a/x.glb", m.ResolveUrl("cdn:1"));
        }

        [Test]
        public void ResolveUrl_UnknownId_ReturnsNull()
        {
            var m = Load();
            Assert.IsNull(m.ResolveUrl("warp:0"));   // demo id genuinely absent from manifest
            Assert.IsNull(m.ResolveUrl("does:not:exist"));
        }

        [Test]
        public void ResolveUrl_NullOrEmptyId_ReturnsNull()
        {
            var m = Load();
            Assert.IsNull(m.ResolveUrl(null));
            Assert.IsNull(m.ResolveUrl(""));
        }

        [Test]
        public void ResolveUrl_EmptyGlbUrl_ReturnsNull()
        {
            var m = Load();
            Assert.IsNull(m.ResolveUrl("empty:1"));
        }

        [Test]
        public void Parse_ReadsBaseUrlAndModules()
        {
            var m = Load();
            Assert.AreEqual("https://maps.siamdive.com/", m.BaseUrl);
            Assert.AreEqual(4, m.Count);
            Assert.AreEqual("CORAL", m.Get("coral:2").Kind);
            Assert.IsNull(m.Get("nope"));
        }
    }
}
