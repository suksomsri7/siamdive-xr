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
    ///
    /// 🔴 SECOND JOB, ADDED AFTER BUILD 276: this add-on now also decides which GPU format the
    /// KTX2 is transcoded INTO. See <see cref="KtxTranscodeTargets"/> for why (short version: the
    /// KTX package picks ETC over ASTC on iOS, halving the bit budget of every texture in the
    /// app). That is why the colour textures are claimed here too, by a second loader instance —
    /// <see cref="ITextureImageLoader.LoadImage"/> is handed the bytes and nothing else, no texture
    /// index and no role, so the only way for the loader to know what it is holding is for the
    /// instance that CLAIMED it to be the instance that loads it. glTFast records the claiming
    /// add-on per texture (<c>GltfImport.cs:1667-1671</c>) and calls back on exactly that one
    /// (<c>GltfImport.cs:1814-1822</c>), so one instance per role is the whole mechanism.
    /// </summary>
    public static class LinearDataTextures
    {
        private static bool _registered;

        private static bool _astcResolved;
        private static bool _astcSrgb;
        private static bool _astcUNorm;

        /// <summary>
        /// Can this device sample ASTC 4x4 at all? Resolved once, lazily — <see cref="Register"/>
        /// can run before the graphics device is up, and <c>SystemInfo</c> answers honestly only
        /// afterwards.
        ///
        /// 🔴 <c>GraphicsFormatUsage.Sample</c> AND NOT <c>.Linear</c>, for both twins, because
        /// this has to agree with the gate the KTX package itself applies to the format we hand it:
        /// <c>KtxTexture.cs:155</c> calls <c>TranscodeFormatHelper.IsFormatSupported(targetFormat)</c>
        /// whose <c>linear</c> parameter defaults to false, i.e. <c>Sample</c>
        /// (<c>TranscodeFormatHelper.cs:454-461</c>). A stricter test here would forgo ASTC we
        /// could have had; a looser one would hand the package a format it rejects with
        /// <c>FormatUnsupportedBySystem</c> and no texture.
        /// </summary>
        private static void ResolveAstcSupport()
        {
            if (_astcResolved) return;
            _astcResolved = true;
            _astcSrgb = SystemInfo.IsFormatSupported(
                UnityEngine.Experimental.Rendering.GraphicsFormat.RGBA_ASTC4X4_SRGB,
                UnityEngine.Experimental.Rendering.GraphicsFormatUsage.Sample);
            _astcUNorm = SystemInfo.IsFormatSupported(
                UnityEngine.Experimental.Rendering.GraphicsFormat.RGBA_ASTC4X4_UNorm,
                UnityEngine.Experimental.Rendering.GraphicsFormatUsage.Sample);
        }

        internal static bool AstcSrgbSupported
        {
            get { ResolveAstcSupport(); return _astcSrgb; }
        }

        internal static bool AstcUNormSupported
        {
            get { ResolveAstcSupport(); return _astcUNorm; }
        }

        /// <summary>Both twins, which is what <see cref="KtxTranscodeTargets.Claims"/> gates on.</summary>
        internal static bool AstcSupported
        {
            get { ResolveAstcSupport(); return _astcSrgb && _astcUNorm; }
        }

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
            ImportAddonRegistry.RegisterImportAddon(new ColourTextureAddon());
            Debug.Log("[Shading] normal maps will be loaded as linear data " +
                      $"(colourSpace={QualitySettings.activeColorSpace})");
            Debug.Log($"[KtxLoad] astc4x4 srgb={AstcSrgbSupported} unorm={AstcUNormSupported} " +
                      $"→ {(AstcSupported ? "forcing ASTC 4x4" : "auto-select (unchanged)")} " +
                      $"gfx={SystemInfo.graphicsDeviceType}");
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
            _astcResolved = false;
            Linearised.Clear();
        }
#endif
    }

    /// <summary>Registers one <see cref="LinearDataTextureLoader"/> per <c>GltfImport</c>.</summary>
    internal sealed class LinearDataTextureAddon : ImportAddon<LinearDataTextureLoader> { }

    /// <summary>Registers one <see cref="ColourTextureLoader"/> per <c>GltfImport</c>.</summary>
    internal sealed class ColourTextureAddon : ImportAddon<ColourTextureLoader> { }

    /// <summary>
    /// Claims the normal maps. Loads them as linear data (the original job of this file) and, where
    /// the device allows it, into ASTC 4x4 UNorm.
    /// </summary>
    internal sealed class LinearDataTextureLoader : KtxRoleTextureLoader
    {
        protected override bool WantsDataImages => true;
        protected override string RoleName => "data";
    }

    /// <summary>
    /// Claims every other KTX2 image — base colour, emissive, and the metallic-roughness and
    /// occlusion maps that <see cref="GltfTextureRoles.MetallicRoughnessAndOcclusionAreData"/>
    /// deliberately leaves undecided.
    ///
    /// 🔴 IT DOES NOT CHANGE HOW THEY ARE SAMPLED. This loader passes glTFast's own <c>linear</c>
    /// flag straight through, so every one of these textures keeps exactly the sRGB-ness it has
    /// today; the only thing it changes is the codec they are transcoded into. And it only exists
    /// at all when the device supports ASTC — see <see cref="KtxTranscodeTargets.Claims"/>, which
    /// makes it decline every texture on hardware where it would have nothing to offer, so the
    /// file loads down glTFast's untouched path exactly as before.
    /// </summary>
    internal sealed class ColourTextureLoader : KtxRoleTextureLoader
    {
        protected override bool WantsDataImages => false;
        protected override string RoleName => "colour";
    }

    /// <summary>
    /// The per-file worker. Claims the KTX2 images of ONE role — see <see cref="WantsDataImages"/> —
    /// and loads them the way glTFast loads a KTX2, the same four lines from
    /// <c>KtxImageLoader.cs:32-52</c>, with the linear flag and the transcode target chosen by
    /// <see cref="KtxTranscodeTargets"/> instead of left to the package.
    ///
    /// 🔴 THE ROLE IS CARRIED BY THE INSTANCE, not by an argument, because there is no argument:
    /// <c>LoadImage</c> receives the bytes, two booleans and a cancellation token, and none of them
    /// says which texture this is. Two instances, one per role, is what makes the role knowable —
    /// glTFast calls back on whichever instance claimed the texture. The two claim disjoint sets
    /// (<c>isData != WantsDataImages</c> declines), so their order of registration cannot matter.
    /// </summary>
    internal abstract class KtxRoleTextureLoader : ImportAddonInstance, ITextureImageLoader
    {
        private IGltfReadable _gltf;
        private bool[] _dataImage;
        private bool _resolved;
        private int _claimed;
        private int _loaded;

        /// <summary>
        /// Which half of the file this instance is responsible for: true for the images used as
        /// normal maps, false for everything else.
        /// </summary>
        protected abstract bool WantsDataImages { get; }

        /// <summary>The word this instance uses for itself in the log.</summary>
        protected abstract string RoleName { get; }

        public override bool SupportsGltfExtension(string extensionName) => false;

        /// <summary>
        /// 🔴 THE LINE THAT WAS MISSING, and the whole reason CI run 30894246930 came back with
        /// <c>linearData=f</c> on every model.
        ///
        /// <c>Inject</c> is not a notification — it is where an add-on instance ADDS ITSELF to the
        /// import, and an instance that does not call
        /// <c>GltfImportBase.AddImportAddonInstance</c> (<c>GltfImport.cs:360-364</c>, the only
        /// writer of <c>m_Addons</c>) is never in the collection that
        /// <c>GltfImport.cs:1660</c> queries with
        /// <c>m_Addons?.SubCollection&lt;ITextureImageLoader&gt;()</c>. That expression returned
        /// null, the loader was never asked about a single texture, and the null-conditional meant
        /// it did so in complete silence — no exception, no warning, and the
        /// "claimed=" line below never printed at all.
        ///
        /// glTFast's own documentation sample does exactly this and nothing else:
        /// <c>DocExamples/PngTextureAddon.cs:21</c> — <c>gltfImport.AddImportAddonInstance(this);</c>
        /// </summary>
        public override void Inject(GltfImportBase gltfImport)
        {
            _gltf = gltfImport as IGltfReadable;
            gltfImport.AddImportAddonInstance(this);
        }

        public override void Inject(IInstantiator instantiator) { }

        public override void Dispose()
        {
            // 🔴 One line per file, always — including zero. The previous attempt at this bug
            // shipped, changed nothing, and its log could not distinguish "ran and found none"
            // from "never ran", which cost a 35-minute CI round to tell apart.
            Debug.Log($"[Shading] role={RoleName} textures claimed={_claimed} " +
                      $"resolved={(_dataImage != null ? _dataImage.Length.ToString() : "n/a")} " +
                      $"sessionTotal={LinearDataTextures.Count}");
            _gltf = null;
            _dataImage = null;
        }

        /// <summary>
        /// 🔴 Content sniffing stays OFF. This overload is glTFast's "can you handle these bytes"
        /// question, asked with no idea which texture the bytes belong to
        /// (<c>ImageImport.cs:43</c>). Answering it would hand this instance images of both roles
        /// with no way to tell them apart — which is the entire thing the two-instance split
        /// exists to avoid. The per-texture overload below is the one that knows what the texture
        /// is FOR.
        /// </summary>
        public bool IsAbleToLoad(ReadOnlySpan<byte> data) => false;

        /// <inheritdoc />
        public bool IsAbleToLoad(GLTFast.Schema.TextureBase texture, out int imageIndex)
        {
            // 🔴 GetImageIndex() and never `texture.source`: whatever is returned here becomes
            // glTFast's image index for this texture (<c>GltfImport.cs:1667</c>,
            // <c>GetImageIndexFromTexture</c> at <c>GltfImport.cs:2798-2805</c>), and for a
            // KHR_texture_basisu texture the two differ — source points at the PNG fallback.
            // Returning the wrong one would silently repoint the data fetch and the texture name.
            imageIndex = texture != null ? texture.GetImageIndex() : -1;
            if (texture == null || imageIndex < 0) return false;

            // KTX2 only. A PNG is not affected by any of this — Unity does no conversion at all on
            // a plain Texture2D in a gamma project, and there is no transcode step to steer — so
            // claiming one would be taking responsibility for a path that is already correct.
            if (!texture.IsKtx) return false;

            Resolve();

            // A file this could not classify keeps today's behaviour in full: neither instance
            // claims anything, and glTFast loads every one of its textures itself.
            if (_dataImage == null || imageIndex >= _dataImage.Length) return false;

            bool isData = _dataImage[imageIndex];
            if (isData != WantsDataImages) return false;
            if (!KtxTranscodeTargets.Claims(true, isData, LinearDataTextures.AstcSupported)) return false;

            _claimed++;
            return true;
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
            // For a normal map `linear` arrives false — that was the original bug. The plan
            // recomputes it from the role, and picks the ASTC twin that matches whatever it lands
            // on, so the codec can never disagree with the colour space.
            KtxLoadPlan plan = KtxTranscodeTargets.Plan(
                WantsDataImages,
                linear,
                readable,
                LinearDataTextures.AstcSrgbSupported,
                LinearDataTextures.AstcUNormSupported);

            int index = _loaded++;
            ImageResult result = await Load(data, plan, readable, generateMipMaps, RoleName, index);
            if (result.Texture != null)
            {
                if (plan.Linear) LinearDataTextures.Note(result.Texture);
                return result;
            }

            // 🔴 EVERY FAILURE FALLS BACK, never to nothing. Claiming a texture takes it off
            // glTFast's path for good (<c>GltfImport.cs:1814-1826</c>) — there is nobody behind
            // this loader — and a missing base colour map is an untextured model, a far louder
            // regression than the block artefacts this change exists to remove.
            if (plan.Target != KtxTranscodeTarget.AutoSelect)
            {
                Debug.LogWarning($"[KtxLoad] role={RoleName} tex={index} forcing {plan.Target} " +
                                 "failed — retrying with the package's own choice");
                plan.Target = KtxTranscodeTarget.AutoSelect;
                result = await Load(data, plan, readable, generateMipMaps, RoleName, index);
                if (result.Texture != null)
                {
                    if (plan.Linear) LinearDataTextures.Note(result.Texture);
                    return result;
                }
            }

            Debug.LogWarning($"[KtxLoad] role={RoleName} tex={index} load failed — retrying the " +
                             "way glTFast would have loaded it");
            return await Load(
                data,
                new KtxLoadPlan { Target = KtxTranscodeTarget.AutoSelect, Linear = linear },
                readable,
                generateMipMaps,
                RoleName,
                index);
        }

        private static async Task<ImageResult> Load(
            NativeArray<byte>.ReadOnly data,
            KtxLoadPlan plan,
            bool readable,
            bool generateMipMaps,
            string role,
            int index)
        {
            var ktx = new KtxUnity.KtxTexture();
            try
            {
                if (ktx.Open(data) != KtxUnity.ErrorCode.Success) return ImageResult.Null;

                KtxUnity.TextureResult result;
                if (plan.Target == KtxTranscodeTarget.AutoSelect)
                {
                    // Byte for byte what glTFast does (<c>KtxImageLoader.cs:42</c>).
                    result = await ktx.LoadTexture2D(plan.Linear, readable);
                }
                else
                {
                    // 🔴 The overload that forces the transcode target
                    // (<c>KtxTexture.cs:147-174</c>). Its defaults — layer 0, faceSlice 0,
                    // mipLevel 0, mipChain true — are the same values the (linear, readable)
                    // overload passes down (<c>KtxTexture.cs:122-144</c>), so nothing but the
                    // format changes. It has no `readable` parameter, which is exactly why
                    // Plan() refuses to reach this branch when readable was asked for.
                    result = await ktx.LoadTexture2D(ToGraphicsFormat(plan.Target));
                }

                if (result == null || result.errorCode != KtxUnity.ErrorCode.Success
                    || result.texture == null)
                {
                    if (result != null && result.errorCode != KtxUnity.ErrorCode.Success)
                    {
                        Debug.LogWarning($"[KtxLoad] role={role} tex={index} " +
                                         $"target={plan.Target} errorCode={result.errorCode}");
                    }
                    return ImageResult.Null;
                }

                // 🔴 The proof line. `got=` is read off the texture that actually exists, not off
                // the format that was requested, because the whole reason this change exists is
                // that the two were not the same.
                Debug.Log($"[KtxLoad] role={role} tex={index} target={plan.Target} " +
                          $"got={result.texture.graphicsFormat} " +
                          $"size={result.texture.width}x{result.texture.height} " +
                          $"mips={result.texture.mipmapCount} linear={plan.Linear} " +
                          $"readable={readable}");

                // Kept from glTFast's KTX path (<c>KtxImageLoader.cs:46-49</c>), which no longer
                // runs for these textures. Losing a diagnostic is how a claim goes quiet.
                if (generateMipMaps && result.texture.mipmapCount <= 1)
                    Debug.LogWarning("KTX texture does not contain mipmaps.");

                // Extension method called as a plain static: `IsYFlipped` lives on
                // KtxUnity.TextureOrientationExtension and extension syntax would need the
                // `using KtxUnity` this file deliberately does not have.
                //
                // 🔴 NOT OPTIONAL, and more load-bearing now than it was: glTFast reads the flip
                // out of the ImageResult (<c>GltfImport.cs:2388-2392</c>) and feeds it to the UV
                // transform (<c>MaterialGenerator.cs:357</c>). Dropping it used to mis-flip normal
                // maps; with the colour maps claimed too it would mis-flip the whole model.
                return new ImageResult(
                    result.texture,
                    KtxUnity.TextureOrientationExtension.IsYFlipped(result.orientation));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[KtxLoad] role={role} tex={index} open/transcode threw: "
                                 + e.Message);
                return ImageResult.Null;
            }
            finally
            {
                ktx.Dispose();
            }
        }

        private static UnityEngine.Experimental.Rendering.GraphicsFormat ToGraphicsFormat(
            KtxTranscodeTarget target)
        {
            switch (target)
            {
                case KtxTranscodeTarget.Astc4x4UNorm:
                    return UnityEngine.Experimental.Rendering.GraphicsFormat.RGBA_ASTC4X4_UNorm;
                default:
                    return UnityEngine.Experimental.Rendering.GraphicsFormat.RGBA_ASTC4X4_SRGB;
            }
        }
    }
}
