using System.Collections.Generic;
using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// Which textures the import add-on is allowed to claim.
    ///
    /// 🔴 This is the blast radius of the whole change, so it is the part with the most tests. The
    /// add-on takes a texture away from glTFast's normal loading path; getting the classification
    /// wrong does not produce a subtle regression, it produces a washed-out model. Every test here
    /// is really the same test: does this ever claim something that is not a normal map.
    /// </summary>
    public class GltfTextureRolesTests
    {
        private static GltfMaterialTextures Mat(int baseColour = -1, int normal = -1,
                                                int metallicRoughness = -1, int occlusion = -1,
                                                int emissive = -1)
        {
            GltfMaterialTextures m = GltfMaterialTextures.None;
            m.BaseColour = baseColour;
            m.Normal = normal;
            m.MetallicRoughness = metallicRoughness;
            m.Occlusion = occlusion;
            m.Emissive = emissive;
            return m;
        }

        [Test]
        public void TheDefaultStructWouldClaimEverythingSoNoneExists()
        {
            // 🔴 A C# struct zeroes its fields, and a glTF texture index of 0 is a real texture.
            // `default(GltfMaterialTextures)` therefore says "this material uses texture 0 as its
            // base colour AND its normal map AND its emissive", which resolves to Colour and
            // quietly disables the fix for the commonest single-texture model there is. None is
            // the only safe empty value and this is the test that keeps it that way.
            GltfMaterialTextures none = GltfMaterialTextures.None;
            Assert.AreEqual(-1, none.BaseColour);
            Assert.AreEqual(-1, none.Normal);
            Assert.AreEqual(-1, none.MetallicRoughness);
            Assert.AreEqual(-1, none.Occlusion);
            Assert.AreEqual(-1, none.Emissive);
        }

        [Test]
        public void OnlyTheNormalMapIsClaimed()
        {
            // The shape every model in this app has: one material, four maps.
            var roles = GltfTextureRoles.Resolve(4, new List<GltfMaterialTextures>
            {
                Mat(baseColour: 0, metallicRoughness: 1, normal: 2, emissive: 3),
            });

            Assert.AreEqual(GltfTextureRole.Colour, roles[0], "base colour");
            Assert.AreEqual(GltfTextureRole.Unused, roles[1], "metallic-roughness: not this round");
            Assert.AreEqual(GltfTextureRole.Data, roles[2], "normal map");
            Assert.AreEqual(GltfTextureRole.Colour, roles[3], "emissive");
        }

        [Test]
        public void ATextureUsedAsBothStaysColour()
        {
            // Malformed, but the failure modes are not symmetric: an albedo sampled as linear is a
            // washed-out model visible from across the room, and a normal map sampled as sRGB is a
            // tilt. When in doubt, keep today's behaviour.
            var roles = GltfTextureRoles.Resolve(1, new List<GltfMaterialTextures>
            {
                Mat(normal: 0),
                Mat(baseColour: 0),
            });
            Assert.AreEqual(GltfTextureRole.Colour, roles[0]);

            // …and the same however the materials are ordered. The two-pass resolve is what makes
            // this true; a single pass would give a different answer per file.
            roles = GltfTextureRoles.Resolve(1, new List<GltfMaterialTextures>
            {
                Mat(baseColour: 0),
                Mat(normal: 0),
            });
            Assert.AreEqual(GltfTextureRole.Colour, roles[0]);
        }

        [Test]
        public void MetallicRoughnessIsDataOnlyWhenSomebodyAsksForIt()
        {
            var materials = new List<GltfMaterialTextures> { Mat(metallicRoughness: 0, occlusion: 1) };

            // Off by default — that is the one-change-per-build rule written as a constant, not an
            // opinion about whether they are data. They are.
            Assert.IsFalse(GltfTextureRoles.MetallicRoughnessAndOcclusionAreData);
            var roles = GltfTextureRoles.Resolve(2, materials);
            Assert.AreEqual(GltfTextureRole.Unused, roles[0]);
            Assert.AreEqual(GltfTextureRole.Unused, roles[1]);

            // …and the resolver is already right for the day that work order is picked up, so that
            // change is one boolean and not a rewrite.
            roles = GltfTextureRoles.Resolve(2, materials, metallicRoughnessAndOcclusionAreData: true);
            Assert.AreEqual(GltfTextureRole.Data, roles[0]);
            Assert.AreEqual(GltfTextureRole.Data, roles[1]);
        }

        [Test]
        public void GarbageInputIsSurvived()
        {
            Assert.AreEqual(0, GltfTextureRoles.Resolve(0, null).Length);
            Assert.AreEqual(0, GltfTextureRoles.Resolve(-3, null).Length);

            // An index past the end of the textures array is a corrupt file, not a crash.
            var roles = GltfTextureRoles.Resolve(2, new List<GltfMaterialTextures>
            {
                Mat(normal: 99, baseColour: -7),
            });
            Assert.AreEqual(GltfTextureRole.Unused, roles[0]);
            Assert.AreEqual(GltfTextureRole.Unused, roles[1]);
        }

        [Test]
        public void ImagesAreOnlyDataWhenEveryTexturePointingAtThemIs()
        {
            // Two glTF textures, different samplers, one shared image: texture 0 is a normal map,
            // texture 1 is a base colour, and they are the same picture. The loader works at image
            // granularity, so the image cannot be both — and it resolves to colour, same as above.
            var roles = new[] { GltfTextureRole.Data, GltfTextureRole.Colour };
            bool[] data = GltfTextureRoles.DataImages(roles, new[] { 0, 0 }, 1);
            Assert.IsFalse(data[0], "a shared image with any colour use is not claimed");

            // The ordinary case: separate images, and the normal map's image is claimed.
            data = GltfTextureRoles.DataImages(roles, new[] { 0, 1 }, 2);
            Assert.IsTrue(data[0]);
            Assert.IsFalse(data[1]);
        }

        [Test]
        public void ImageMappingSurvivesGarbageToo()
        {
            Assert.AreEqual(0, GltfTextureRoles.DataImages(null, null, 0).Length);
            Assert.AreEqual(2, GltfTextureRoles.DataImages(null, null, 2).Length);

            // A texture with no image (index −1) contributes nothing rather than indexing at −1.
            bool[] data = GltfTextureRoles.DataImages(
                new[] { GltfTextureRole.Data, GltfTextureRole.Data }, new[] { -1, 0 }, 1);
            Assert.IsTrue(data[0]);

            // Ragged inputs: shorter of the two wins, no exception.
            data = GltfTextureRoles.DataImages(
                new[] { GltfTextureRole.Data }, new[] { 0, 1, 2 }, 3);
            Assert.IsTrue(data[0]);
            Assert.IsFalse(data[1]);
            Assert.IsFalse(data[2]);
        }
    }
}
