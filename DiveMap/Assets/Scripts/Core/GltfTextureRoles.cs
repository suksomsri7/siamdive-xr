using System.Collections.Generic;

namespace DiveMap.Core
{
    /// <summary>What a glTF texture is FOR, which decides how it must be sampled.</summary>
    public enum GltfTextureRole
    {
        /// <summary>No material references it. Nothing to decide.</summary>
        Unused = 0,

        /// <summary>Base colour or emissive: authored in sRGB, must be decoded as sRGB.</summary>
        Colour = 1,

        /// <summary>Normal (and optionally metallic-roughness / occlusion): numbers, not colour.
        /// Must reach the shader exactly as stored.</summary>
        Data = 2,
    }

    /// <summary>The texture indices one glTF material references. −1 means "no such texture".</summary>
    public struct GltfMaterialTextures
    {
        public int BaseColour;
        public int MetallicRoughness;
        public int Normal;
        public int Occlusion;
        public int Emissive;

        /// <summary>A material that references nothing — the only safe zero value, since the
        /// default struct would claim texture 0 in five slots at once.</summary>
        public static GltfMaterialTextures None => new GltfMaterialTextures
        {
            BaseColour = -1,
            MetallicRoughness = -1,
            Normal = -1,
            Occlusion = -1,
            Emissive = -1,
        };
    }

    /// <summary>
    /// Which textures of a glTF file must be sampled as data rather than as colour.
    ///
    /// 🔴 This is glTFast's own rule, lifted out to where it can run in a gamma project. glTFast
    /// builds the identical mapping at <c>GltfImport.cs:1678-1709</c> — walk the materials, mark
    /// base colour, emissive and the two spec-gloss slots as colour, treat everything else as data
    /// — but only inside <c>if (activeColorSpace == ColorSpace.Linear)</c>, so in gamma the whole
    /// classification is skipped and every texture, normal maps included, is transcoded to an sRGB
    /// GPU format. See <see cref="NormalMapDecode"/> for the full trace.
    ///
    /// 🔴 CONFLICTS RESOLVE TOWARD COLOUR, DELIBERATELY. A file that uses one image as both a base
    /// colour and a normal map is malformed, but the failure modes are not symmetric: sampling a
    /// normal map as sRGB tilts a surface, while sampling an ALBEDO as linear turns the whole model
    /// into a washed-out flat — visible from across the room and blamed on this change. So an image
    /// with any colour use at all keeps the existing behaviour.
    /// </summary>
    public static class GltfTextureRoles
    {
        /// <summary>
        /// Whether metallic-roughness and occlusion maps count as data too.
        ///
        /// 🔴 FALSE ON PURPOSE, and this is the whole scope discipline of the work order in one
        /// constant. They ARE data — glTFast marks them so in a linear project, and they are being
        /// sRGB-decoded today exactly like the normal maps, which drags sampled roughness down and
        /// makes every surface read shinier than it was authored. But metallic/roughness is a
        /// separate visible change that the project has already been burned mixing in with others
        /// (build 255), and it has its own open work order. Flipping this to true is the entire
        /// diff when that one is picked up; the resolver and its tests already cover it.
        /// </summary>
        public const bool MetallicRoughnessAndOcclusionAreData = false;

        /// <summary>
        /// Classify every texture index in the file.
        /// </summary>
        /// <param name="textureCount">Number of entries in the glTF <c>textures</c> array.</param>
        /// <param name="materials">One entry per glTF material.</param>
        /// <param name="metallicRoughnessAndOcclusionAreData">See
        /// <see cref="MetallicRoughnessAndOcclusionAreData"/>.</param>
        public static GltfTextureRole[] Resolve(
            int textureCount,
            IReadOnlyList<GltfMaterialTextures> materials,
            bool metallicRoughnessAndOcclusionAreData = MetallicRoughnessAndOcclusionAreData)
        {
            if (textureCount <= 0) return new GltfTextureRole[0];
            var roles = new GltfTextureRole[textureCount];
            if (materials == null) return roles;

            for (int i = 0; i < materials.Count; i++)
            {
                GltfMaterialTextures m = materials[i];
                Mark(roles, m.Normal, GltfTextureRole.Data);
                if (metallicRoughnessAndOcclusionAreData)
                {
                    Mark(roles, m.MetallicRoughness, GltfTextureRole.Data);
                    Mark(roles, m.Occlusion, GltfTextureRole.Data);
                }
            }

            // Second pass, so colour always wins however the materials were ordered.
            for (int i = 0; i < materials.Count; i++)
            {
                GltfMaterialTextures m = materials[i];
                Mark(roles, m.BaseColour, GltfTextureRole.Colour);
                Mark(roles, m.Emissive, GltfTextureRole.Colour);
                // Metallic-roughness and occlusion are deliberately left Unused when the flag is
                // off: "we did not take a side", which the loader reads as "load it exactly the
                // way it loads today".
            }

            return roles;
        }

        private static void Mark(GltfTextureRole[] roles, int index, GltfTextureRole role)
        {
            if (index < 0 || index >= roles.Length) return;
            roles[index] = role;
        }

        /// <summary>
        /// Lift the per-texture verdict to the per-IMAGE one, because that is the granularity the
        /// loader works at: two glTF textures with different samplers can share one image, and an
        /// image only counts as data when EVERY texture pointing at it does.
        /// </summary>
        /// <param name="roles">Output of <see cref="Resolve"/>.</param>
        /// <param name="imageOfTexture">Image index per texture index, same length as
        /// <paramref name="roles"/>. −1 for a texture with no image.</param>
        /// <param name="imageCount">Number of entries in the glTF <c>images</c> array.</param>
        public static bool[] DataImages(GltfTextureRole[] roles, int[] imageOfTexture, int imageCount)
        {
            if (imageCount <= 0) return new bool[0];
            var data = new bool[imageCount];
            var vetoed = new bool[imageCount];
            if (roles == null || imageOfTexture == null) return data;

            int n = roles.Length < imageOfTexture.Length ? roles.Length : imageOfTexture.Length;
            for (int t = 0; t < n; t++)
            {
                int img = imageOfTexture[t];
                if (img < 0 || img >= imageCount) continue;
                if (roles[t] == GltfTextureRole.Data) data[img] = true;
                else if (roles[t] == GltfTextureRole.Colour) vetoed[img] = true;
            }

            for (int i = 0; i < imageCount; i++)
                if (vetoed[i]) data[i] = false;

            return data;
        }
    }
}
