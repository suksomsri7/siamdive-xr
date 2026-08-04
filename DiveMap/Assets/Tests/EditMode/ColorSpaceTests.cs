using NUnit.Framework;
using DiveMap.Core;

namespace DiveMap.Tests
{
    /// <summary>
    /// The project renders in LINEAR, and the normal-map workaround is therefore off.
    ///
    /// 🔴 WHY THIS IS A TEST AND NOT A COMMENT. Every normal map in this app was being thrown away
    /// at load time — <c>SceneBuilder.DropMisdecodedNormalMap</c>, gated on
    /// <see cref="GlbShading.NormalMapIsMisdecoded"/> — because in a GAMMA project glTFast never
    /// marks a glTF texture as data (<c>GltfImport.cs:1674</c> tests
    /// <c>activeColorSpace == ColorSpace.Linear</c>), so every KTX2 transcodes to an sRGB GPU
    /// format and a neutral normal texel decodes to 0.216 instead of 0.502 — half a right angle of
    /// error on every texel, measured at 53.4° in <c>GlbShading.NeutralTiltDegrees</c>.
    ///
    /// The switch that turns all of that off is ONE LINE IN A YAML FILE. No C# can observe it
    /// outside the editor, nothing fails loudly if it reverts in a merge, and the symptom of it
    /// reverting is not a crash — it is that surfaces quietly go flat again, which took this
    /// project two weeks and six wrong hypotheses to diagnose the first time. So the line itself is
    /// asserted, and the chain from it to the workaround is asserted with it.
    /// </summary>
    public class ColorSpaceTests
    {
        private const string SettingsPath = "ProjectSettings/ProjectSettings.asset";

        [Test]
        public void TheProjectRendersInLinear()
        {
            string text = RepoFiles.Read(SettingsPath);
            Assert.NotNull(text, $"cannot find {SettingsPath} from {RepoFiles.SearchedFrom}");

            // 0 = Gamma, 1 = Linear. Anything else is Unity having changed the enum under us, which
            // is worth failing on rather than guessing about.
            StringAssert.Contains("m_ActiveColorSpace: 1", text,
                "the project is not in Linear colour space — every KTX2 normal map in the app is "
                + "being decoded as sRGB again (53.4° of error per texel) and SceneBuilder is "
                + "dropping all of them. See GlbShading.");
            Assert.IsFalse(text.Contains("m_ActiveColorSpace: 0"),
                "ProjectSettings still carries a Gamma colour space line");
        }

        [Test]
        public void InLinear_TheNormalMapWorkaroundTurnsItselfOff()
        {
            // 🔴 REWRITTEN FOR THE 3-ARGUMENT FORM (d60891f). When this test was first written the
            // drop had exactly one input — the colour space — so "linear retires the workaround"
            // was a single assertion. It now has three, because d60891f stopped DEDUCING whether a
            // normal map had been sRGB-decoded and started MEASURING it on the GPU
            // (NormalMapProbe → NormalReadVerdict). The colour space is only the fallback for when
            // the measurement could not be taken.
            //
            // That makes this test more important rather than less: the claim "switching the
            // project to Linear retires the workaround" is now a claim about the DEFAULT branch,
            // and the default branch is the one that runs on every texture the probe cannot read.
            // So assert the whole Unknown row, both ways round on the add-on flag.
            Assert.IsFalse(GlbShading.NormalMapIsMisdecoded(NormalReadVerdict.Unknown,
                                                           gammaColorSpace: false,
                                                           loadedAsLinearData: false),
                "in Linear an unmeasurable normal map must be KEPT — glTFast marks it linear itself "
                + "(GltfImport.cs:1676), so there is nothing left to work around");
            Assert.IsFalse(GlbShading.NormalMapIsMisdecoded(NormalReadVerdict.Unknown,
                                                           gammaColorSpace: false,
                                                           loadedAsLinearData: true),
                "and the add-on's flag must not be able to change that answer — exactly one system "
                + "decides the sampling in Linear, and it is glTFast (see LinearDataTextures)");
            Assert.IsTrue(GlbShading.NormalMapIsMisdecoded(NormalReadVerdict.Unknown,
                                                          gammaColorSpace: true,
                                                          loadedAsLinearData: false),
                "in Gamma, with nothing claiming the texture, the maps are still garbage");

            // The measured branches are colour-space-blind on purpose: a map that was READ as
            // unit normals is good in either space, and one measured as sRGB-decoded is bad in
            // either space. Pinned here so that "Linear fixed it" is never read as "Linear makes
            // the probe's verdict irrelevant".
            Assert.IsFalse(GlbShading.NormalMapIsMisdecoded(NormalReadVerdict.UnitNormals,
                                                           gammaColorSpace: true,
                                                           loadedAsLinearData: false),
                "a measured-good map is kept whatever the project setting says");
            Assert.IsTrue(GlbShading.NormalMapIsMisdecoded(NormalReadVerdict.SrgbDecoded,
                                                          gammaColorSpace: false,
                                                          loadedAsLinearData: true),
                "a measured-bad map is dropped even in Linear — the measurement outranks the theory");
        }

        [Test]
        public void TheErrorThatWasBeingWorkedAround_WasWorthWorkingAround()
        {
            // Kept as the justification for the drop existing at all, so that nobody re-enables the
            // gamma path thinking 53° is a rounding error.
            double tilt = GlbShading.NeutralTiltDegrees();
            Assert.Greater(tilt, 50.0);
            Assert.Less(tilt, 56.0);
        }

        [Test]
        public void TheAcesShaderIsAlwaysIncluded_SoItCannotBeStrippedIntoMagenta()
        {
            // This project has shipped a magenta iOS build twice from a stripped custom shader.
            // Belt: the material lives in Resources. Braces: the shader's GUID is in Always
            // Included Shaders. AcesToneMapping also refuses to attach if the shader is
            // unsupported, but that is the third line of defence, not the first.
            string graphics = RepoFiles.Read("ProjectSettings/GraphicsSettings.asset");
            Assert.NotNull(graphics, $"cannot find GraphicsSettings.asset from {RepoFiles.SearchedFrom}");

            string meta = RepoFiles.Read("Assets/Shaders/DM_AcesToneMap.shader.meta");
            Assert.NotNull(meta, "the ACES shader has no .meta — its GUID is not stable");

            string guid = null;
            foreach (string line in meta.Split('\n'))
                if (line.StartsWith("guid:")) guid = line.Substring(5).Trim();
            Assert.NotNull(guid, "no guid line in DM_AcesToneMap.shader.meta");

            StringAssert.Contains(guid, graphics,
                "the ACES shader is not in Always Included Shaders and can be stripped from a build");

            string mat = RepoFiles.Read("Assets/Resources/DM_AcesToneMap.mat");
            Assert.NotNull(mat, "the ACES material is not in Resources");
            StringAssert.Contains(guid, mat, "the material does not point at the shader it names");
        }
    }
}
