using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DiveMap.Core;
using GLTFast;
using GLTFast.Addons;
using Unity.Collections;
using UnityEngine;

// 🔴 NO `using GLTFast.Schema;` AND NO `using KtxUnity;`. This file is the one place in the
// project that has to speak to both packages plus UnityEngine at once, and their type names
// collide head-on:
//
//     TextureBase   → KtxUnity.TextureBase        vs GLTFast.Schema.TextureBase
//     Texture       → UnityEngine.Texture         vs GLTFast.Schema.Texture
//     Material      → UnityEngine.Material        vs GLTFast.Schema.Material
//     Mesh, Camera, Animation                     → UnityEngine vs GLTFast.Schema
//     Buffer, Type, Path                          → System      vs GLTFast.Schema
//     ManagedNativeArray                          → KtxUnity    vs GLTFast
//
// The first two of those cost a red CI compile (run 30883230215) — and the TextureBase clash
// cost it twice over, because an ambiguous parameter type also means the interface member is
// not implemented (CS0535 on ITextureImageLoader.IsAbleToLoad). The remaining five are live
// landmines for the next edit, so the imports are gone rather than the two names fixed:
// aliases would have to be maintained, and `using` lines that are simply absent cannot rot.
//
// Everything from those two namespaces is spelled out in full below. It is verbose on purpose.

namespace DiveMap.Runtime
{
    /// <summary>
    /// Give the models their surface detail back by loading normal maps as DATA, which is what
    /// they are, instead of as colour.
    ///
    /// 🔴 WHY THIS EXISTS AT ALL — the whole chain, read out of the shipped package sources rather
    /// than recalled, and reproduced in <see cref="NormalMapDecode"/>'s summary:
    /// glTFast decides which glTF textures are colour and which are data ONLY when the project is
    /// in linear colour space (<c>GltfImport.cs:1676</c>). This project is in gamma — a decision
    /// the user made on the device, twice, and it is not being revisited here. So
    /// <c>forceSampleLinear</c> is false for every texture (<c>GltfImport.cs:1796</c>), KTX for
    /// Unity picks the SRGB half of its format table (<c>TranscodeFormatHelper.cs:296</c> and the
    /// paired entries at 100-215), and the normal map arrives on the GPU in an sRGB-typed format
    /// even though its own KTX2 header correctly declares LINEAR. The file was never consulted.
    ///
    /// 🔴 WHAT THIS FIXES AND WHAT IT DOES NOT. It removes the wrong INPUT to that decision rather
    /// than compensating for its output: the texture is re-opened with <c>linear: true</c>, which
    /// is the exact value glTFast itself would have passed in a linear project, so the format that
    /// comes out is the UNorm twin of the one that came out before — same codec, same size, same
    /// mip chain, no extra memory, no extra draw cost, no shader change. It does NOT touch colour
    /// textures, water, fog, lighting, tone mapping or the colour space.
    ///
    /// 🔴 WHY THIS AND NOT A BLIT. The alternative was to re-encode each normal map through a
    /// full-screen shader into a texture we own. That works, and it costs an uncompressed RGBA32
    /// copy — 16 MB per 2048² map, resident, against 4 MB for the ASTC the loader already
    /// produced. A map with twenty models on it would spend a third of a gigabyte to undo a
    /// boolean. Fixing the boolean is the same result for nothing.
    ///
    /// 🔴 WHY THIS AND NOT A SHADER UNDO. The animals could sample through an inverse-sRGB curve
    /// in <c>DM_FishWaveDetail</c>, but every static prop — wrecks, statues, the Atlantis arches —
    /// draws on glTFast's own shader, inside the package, which this project does not fork. A fix
    /// that reaches two thirds of the map is a fix that has to be explained forever.
    ///
    /// 🔴 SELF-DISABLING, like the drop it replaces. <see cref="WasLoadedAsLinearData"/> reports
    /// per texture, so <c>SceneBuilder</c>'s old "throw the normal map away" safety net still
    /// fires for any map that somehow did NOT come through here, and stops firing for the ones
    /// that did. If the project ever does move to linear colour space, glTFast marks the textures
    /// itself, this add-on's <c>linear: true</c> becomes the value it was already getting, and
    /// nothing here has to be remembered or undone.
    /// </summary>
    public static class LinearDataTextures
    {
        private static bool _registered;

        /// <summary>
        /// Instance IDs of every texture this add-on loaded as linear data.
        ///
        /// Instance IDs and not references on purpose: holding a <c>Texture2D</c> reference here
        /// would keep the import's textures alive after <c>GltfImport.Dispose()</c>, which is the
        /// one thing the QC pass and the map teardown both depend on. The set therefore grows by
        /// one int per normal map ever loaded in the session — a few hundred at the very most, and
        /// a stale ID can only ever be read by a texture that no longer exists.
        /// </summary>
        private static readonly HashSet<int> Linearised = new HashSet<int>();

        /// <summary>
        /// Install the add-on. Idempotent, and must run BEFORE the <c>GltfImport</c> that needs it
        /// is constructed — <c>ImportAddonRegistry.InjectAllAddons</c> is called from that
        /// constructor (<c>GltfImport.cs:323</c>), so an add-on registered afterwards is simply
        /// not there for that file.
        /// </summary>
        public static void Register()
        {
            if (_registered) return;
            _registered = true;
            ImportAddonRegistry.RegisterImportAddon(new LinearDataTextureAddon());
            Debug.Log("[Shading] normal maps will be loaded as linear data " +
                      $"(colourSpace={QualitySettings.activeColorSpace})");
        }

        internal static void Note(Texture2D texture)
        {
            if (texture != null) Linearised.Add(texture.GetInstanceID());
        }

        /// <summary>Did this texture come through the add-on above?</summary>
        public static bool WasLoadedAsLinearData(UnityEngine.Texture texture)
            => texture != null && Linearised.Contains(texture.GetInstanceID());

        /// <summary>How many textures have been loaded as data so far. For the log line.</summary>
        public static int Count => Linearised.Count;

#if UNITY_EDITOR
        /// <summary>
        /// Mirror of glTFast's own editor-only reset (<c>ImportAddonRegistry</c>): entering play
        /// mode clears its add-on list, so this must forget that it registered or the second play
        /// session runs with no add-on and no symptom until a screenshot is compared.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnLoad()
        {
            _registered = false;
            Linearised.Clear();
        }
#endif
    }

    /// <summary>Registers one <see cref="LinearDataTextureLoader"/> per <c>GltfImport</c>.</summary>
    internal sealed class LinearDataTextureAddon : ImportAddon<LinearDataTextureLoader> { }

    /// <summary>
    /// The per-file worker. Claims the textures a material uses as a normal map and nothing else,
    /// then loads them the way glTFast loads a KTX2 — the same four lines from
    /// <c>KtxImageLoader.cs:31-51</c> — with one boolean changed.
    /// </summary>
    internal sealed class LinearDataTextureLoader : ImportAddonInstance, ITextureImageLoader
    {
        private IGltfReadable _gltf;
        private bool[] _dataImage;
        private bool _resolved;
        private int _claimed;

        public override bool SupportsGltfExtension(string extensionName) => false;

        public override void Inject(GltfImportBase gltfImport) => _gltf = gltfImport as IGltfReadable;

        public override void Inject(IInstantiator instantiator) { }

        public override void Dispose()
        {
            // 🔴 One line per file, always — including zero. The previous attempt at this bug
            // shipped, changed nothing, and its log could not distinguish "ran and found none"
            // from "never ran", which cost a 35-minute CI round to tell apart.
            Debug.Log($"[Shading] linear-data textures claimed={_claimed} " +
                      $"resolved={(_dataImage != null ? _dataImage.Length.ToString() : "n/a")} " +
                      $"sessionTotal={LinearDataTextures.Count}");
            _gltf = null;
            _dataImage = null;
        }

        /// <summary>
        /// 🔴 Content sniffing stays OFF. This overload is glTFast's "can you handle these bytes"
        /// question and it is asked for EVERY image in the file (<c>ImageImport.cs:41</c>). Every
        /// texture in this app is a KTX2, so answering it honestly would hand this loader the base
        /// colour maps as well and load them as linear — a washed-out model, the loudest possible
        /// regression, from a fix nobody would connect to it. The per-texture overload below is
        /// the one that knows what the texture is FOR.
        /// </summary>
        public bool IsAbleToLoad(ReadOnlySpan<byte> data) => false;

        /// <inheritdoc />
        public bool IsAbleToLoad(GLTFast.Schema.TextureBase texture, out int imageIndex)
        {
            imageIndex = texture != null ? texture.GetImageIndex() : -1;
            if (texture == null || imageIndex < 0) return false;

            // KTX2 only. A PNG normal map is not affected by any of this — Unity does no
            // conversion at all on a plain Texture2D in a gamma project — so claiming one would be
            // taking responsibility for a path that is already correct.
            if (!texture.IsKtx) return false;

            Resolve();
            bool mine = _dataImage != null && imageIndex < _dataImage.Length && _dataImage[imageIndex];
            if (mine) _claimed++;
            return mine;
        }

        /// <summary>
        /// Work out which images are data. Deliberately lazy: <see cref="Inject(GltfImportBase)"/>
        /// runs from the <c>GltfImport</c> CONSTRUCTOR, long before any JSON has been parsed, so
        /// there is nothing to read then. The first
        /// <see cref="IsAbleToLoad(GLTFast.Schema.TextureBase,out int)"/>
        /// call happens after the parse (<c>GltfImport.cs:1662</c>), which is the earliest the
        /// materials exist.
        /// </summary>
        private void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            if (_gltf == null) return;

            try
            {
                int textureCount = _gltf.TextureCount;
                if (textureCount <= 0) return;

                var materials = new List<GltfMaterialTextures>();
                for (int i = 0; i < _gltf.MaterialCount; i++)
                {
                    GLTFast.Schema.MaterialBase m = _gltf.GetSourceMaterial(i);
                    if (m == null) continue;
                    GltfMaterialTextures t = GltfMaterialTextures.None;
                    t.Normal = IndexOf(m.NormalTexture);
                    t.Occlusion = IndexOf(m.OcclusionTexture);
                    t.Emissive = IndexOf(m.EmissiveTexture);
                    GLTFast.Schema.PbrMetallicRoughnessBase pbr = m.PbrMetallicRoughness;
                    if (pbr != null)
                    {
                        t.BaseColour = IndexOf(pbr.BaseColorTexture);
                        t.MetallicRoughness = IndexOf(pbr.MetallicRoughnessTexture);
                    }
                    materials.Add(t);
                }

                var imageOfTexture = new int[textureCount];
                int imageCount = 0;
                for (int i = 0; i < textureCount; i++)
                {
                    GLTFast.Schema.TextureBase txt = _gltf.GetSourceTexture(i);
                    int img = txt != null ? txt.GetImageIndex() : -1;
                    imageOfTexture[i] = img;
                    if (img + 1 > imageCount) imageCount = img + 1;
                }

                GltfTextureRole[] roles = GltfTextureRoles.Resolve(textureCount, materials);
                _dataImage = GltfTextureRoles.DataImages(roles, imageOfTexture, imageCount);
            }
            catch (Exception e)
            {
                // A file this cannot classify keeps today's behaviour rather than losing its
                // textures. Loud, because silence here looks exactly like "the fix did nothing".
                _dataImage = null;
                Debug.LogWarning("[Shading] normal-map classification failed, textures load " +
                                 "unchanged: " + e.Message);
            }
        }

        private static int IndexOf(GLTFast.Schema.TextureInfoBase info)
            => info != null ? info.index : -1;

        /// <inheritdoc />
        public async Task<ImageResult> LoadImage(
            NativeArray<byte>.ReadOnly data,
            bool linear,
            bool readable,
            bool generateMipMaps,
            CancellationToken cancellationToken)
        {
            // `linear` arrives false here — that is the bug, and it is the only thing overridden.
            ImageResult result = await Load(data, true, readable);
            if (result.Texture != null)
            {
                LinearDataTextures.Note(result.Texture);
                return result;
            }

            // 🔴 Fall back to glTFast's own answer rather than to nothing. A missing normal map is
            // a flat model; a missing TEXTURE is an untextured one, and this loader has taken
            // responsibility for the slot, so there is nobody behind it to catch a failure.
            Debug.LogWarning("[Shading] linear load of a normal map failed — retrying the way " +
                             "glTFast would have loaded it");
            return await Load(data, linear, readable);
        }

        private static async Task<ImageResult> Load(
            NativeArray<byte>.ReadOnly data, bool linear, bool readable)
        {
            var ktx = new KtxUnity.KtxTexture();
            try
            {
                if (ktx.Open(data) != KtxUnity.ErrorCode.Success) return ImageResult.Null;
                KtxUnity.TextureResult result = await ktx.LoadTexture2D(linear, readable);
                if (result == null || result.errorCode != KtxUnity.ErrorCode.Success
                    || result.texture == null)
                    return ImageResult.Null;
                // Extension method called as a plain static: `IsYFlipped` lives on
                // KtxUnity.TextureOrientationExtension and extension syntax would need the
                // `using KtxUnity` this file deliberately does not have.
                return new ImageResult(
                    result.texture,
                    KtxUnity.TextureOrientationExtension.IsYFlipped(result.orientation));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Shading] KTX2 open/transcode threw: " + e.Message);
                return ImageResult.Null;
            }
            finally
            {
                ktx.Dispose();
            }
        }
    }
}
