using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// Which FILE of a model gets downloaded.
    ///
    /// 🔴 Two failure modes, pulling opposite ways, and both of them are silent. Taking the ASTC
    /// file on a device that cannot sample ASTC gives a model with no textures and no error the
    /// user would ever see — CI's llvmpipe GL is exactly such a device, so the QC screenshots would
    /// go grey while every test stayed green. NOT taking it on a device that can leaves the whole
    /// file set unused: the app downloads the same Basis files it always did, the transcode still
    /// runs on every load, and nothing about the build looks different. So both directions are
    /// pinned, and so is the case that will be true of every model on the day this ships — no ASTC
    /// url at all.
    /// </summary>
    public class AstcAssetPickTests
    {
        private const string Astc = "https://cdn/models/xr/turtle_astc.glb";
        private const string Xr = "https://cdn/models/xr/turtle_xr0.glb";

        private const bool Supported = true;
        private const bool Unsupported = false;

        [Test]
        public void SupportedDeviceWithAnAstcBuildTakesIt()
        {
            AstcAssetPick.Choice c = AstcAssetPick.Pick(Astc, Xr, Supported);
            Assert.AreEqual(Astc, c.Url);
            Assert.IsTrue(c.Astc);
        }

        [Test]
        public void UnsupportedDeviceKeepsTheFileItShipsWith()
        {
            // The CI / QC case. Nothing about the render changes on this machine.
            AstcAssetPick.Choice c = AstcAssetPick.Pick(Astc, Xr, Unsupported);
            Assert.AreEqual(Xr, c.Url);
            Assert.IsFalse(c.Astc);
        }

        [Test]
        public void AModelWithNoAstcBuildIsUntouchedEitherWay()
        {
            // Additive: this is all 275 modules today, and it must resolve byte-identically.
            Assert.AreEqual(Xr, AstcAssetPick.Pick(null, Xr, Supported).Url);
            Assert.AreEqual(Xr, AstcAssetPick.Pick(null, Xr, Unsupported).Url);
            Assert.IsFalse(AstcAssetPick.Pick(null, Xr, Supported).Astc);
        }

        [Test]
        public void BlankAndWhitespaceCountAsNoAstcBuild()
        {
            // A manifest generator that writes "" for "not built yet" must not steer the pick.
            Assert.AreEqual(Xr, AstcAssetPick.Pick("", Xr, Supported).Url);
            Assert.AreEqual(Xr, AstcAssetPick.Pick("   ", Xr, Supported).Url);
            Assert.IsFalse(AstcAssetPick.Pick("  ", Xr, Supported).Astc);
        }

        [Test]
        public void AnAstcOnlyModelIsStillReturnedOnADeviceThatCannotSampleIt()
        {
            // Documented on purpose: a missing model is a hole in the seabed, an untextured one is
            // a bug report. If this ever flips, the manifest must be guaranteed to keep a fallback.
            AstcAssetPick.Choice c = AstcAssetPick.Pick(Astc, null, Unsupported);
            Assert.AreEqual(Astc, c.Url);
            Assert.IsTrue(c.Astc);
        }

        [Test]
        public void NothingInMeansNothingOut()
        {
            AstcAssetPick.Choice c = AstcAssetPick.Pick(null, null, Supported);
            Assert.IsNull(c.Url);
            Assert.IsFalse(c.Astc);
        }

        [Test]
        public void TheLogLineNamesTheModelTheLodAndTheAnswer()
        {
            // The one line QC reads to tell which file a device actually took.
            string lod0 = AstcAssetPick.LogLine("msh:whaleshark", AstcAssetPick.Pick(Astc, Xr, Supported));
            StringAssert.Contains("[AssetPick]", lod0);
            StringAssert.Contains("id=msh:whaleshark", lod0);
            StringAssert.Contains("lod=0", lod0);
            StringAssert.Contains("astc=t", lod0);
            StringAssert.Contains(Astc, lod0);

            string lod1 = AstcAssetPick.LogLine(
                "school:scad", AstcAssetPick.Pick(Astc, Xr, Unsupported), lod1: true);
            StringAssert.Contains("lod=1", lod1);
            StringAssert.Contains("astc=f", lod1);
            StringAssert.Contains(Xr, lod1);
        }

        [Test]
        public void TheLogLineSurvivesNulls()
        {
            string s = AstcAssetPick.LogLine(null, AstcAssetPick.Pick(null, null, Unsupported));
            StringAssert.Contains("id=?", s);
            StringAssert.Contains("url=(none)", s);
        }
    }
}
