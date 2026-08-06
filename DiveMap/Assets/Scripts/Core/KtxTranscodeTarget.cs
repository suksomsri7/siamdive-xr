namespace DiveMap.Core
{
    /// <summary>
    /// Which GPU format a KTX2 texture must be transcoded INTO.
    ///
    /// Deliberately not <c>UnityEngine.Experimental.Rendering.GraphicsFormat</c>: this file is the
    /// decision, and the decision is the part worth testing on a machine with no Unity Editor. The
    /// two-line translation to a real <c>GraphicsFormat</c> lives in
    /// <c>Runtime/LinearDataTextures.cs</c>, which is the only place that may speak to the KTX
    /// package at all.
    /// </summary>
    public enum KtxTranscodeTarget
    {
        /// <summary>
        /// Do not ask for a format. The KTX package picks one from its own table, which is what
        /// shipped in every build up to 276 — and, on iOS, is the reason this enum exists.
        /// </summary>
        AutoSelect = 0,

        /// <summary>ASTC 4x4, sampled as sRGB. Base colour, emissive, and anything undecided.</summary>
        Astc4x4Srgb = 1,

        /// <summary>ASTC 4x4, sampled as raw numbers. Normal maps.</summary>
        Astc4x4UNorm = 2,
    }

    /// <summary>What to hand <c>KtxTexture.LoadTexture2D</c> for one image.</summary>
    public struct KtxLoadPlan
    {
        /// <summary>
        /// The format to force. <see cref="KtxTranscodeTarget.AutoSelect"/> means "call the
        /// <c>(linear, readable)</c> overload instead", i.e. exactly today's behaviour.
        /// </summary>
        public KtxTranscodeTarget Target;

        /// <summary>
        /// Whether the texture is sampled as linear data rather than as sRGB colour. Carried
        /// alongside <see cref="Target"/> rather than derived from it, because it is still the
        /// value the <see cref="KtxTranscodeTarget.AutoSelect"/> fallback has to pass.
        /// </summary>
        public bool Linear;
    }

    /// <summary>
    /// 🔴 WHY THIS EXISTS — build 276, the whale shark, and a texture the user called "nowhere near
    /// the raw file".
    ///
    /// The pilot re-encoded that model's textures as UASTC at quality 4 from the master PNG, and
    /// measured it: pattern correlation 0.989 against the raw, versus 0.820 for the ETC1S it
    /// replaced. On the device it still looked wrong. The encode was never the problem — the
    /// DECODE was. A KTX2 file does not contain pixels in any GPU format; it contains an
    /// intermediate that is transcoded, at load, into whatever the device supports, and the
    /// transcode target is chosen by the KTX package, not by the file.
    ///
    /// That choice is <c>TranscodeFormatHelper.GetFormatsForImage</c>
    /// (<c>TranscodeFormatHelper.cs:296-345</c>): it walks a priority-ordered table and takes the
    /// FIRST entry the device supports. For an opaque texture the table reaches
    /// <c>ETC1_RGB</c> before it reaches ASTC (<c>TranscodeFormatHelper.cs:100-215</c>), and every
    /// iOS device supports ETC2. So the whale shark's 8-bits-per-pixel UASTC was being unpacked
    /// into 4-bits-per-pixel ETC — a format with one colour line per 4x4 block — before it ever
    /// reached the GPU. Half the bit budget, and the specific artefact of banding a smooth gradient
    /// into blocks. ASTC 4x4 is the same 8 bits per pixel as the source and is supported by every
    /// device this app runs on (A8 and newer).
    ///
    /// 🔴 WHAT THIS DOES NOT CHANGE. Not the colour space, not sRGB-ness, not the linear-data fix
    /// for normal maps that <c>LinearDataTextures</c> already ships. A texture that is sRGB today
    /// is sRGB after this, in a different codec. <see cref="Plan"/> keeps those two decisions
    /// separate on purpose: <see cref="KtxLoadPlan.Linear"/> is computed the same way it was
    /// before this change existed, and <see cref="KtxLoadPlan.Target"/> only ever picks the ASTC
    /// twin that MATCHES it.
    /// </summary>
    public static class KtxTranscodeTargets
    {
        /// <summary>
        /// Whether the add-on should take this image away from glTFast's own loader at all.
        ///
        /// 🔴 THE ASTC GATE IS HERE AND NOT AT LOAD TIME, because a claim is irreversible: once
        /// <c>IsAbleToLoad</c> says yes, glTFast routes the image to us and there is nobody behind
        /// us (<c>GltfImport.cs:1814-1826</c>). On a device with no ASTC — CI's llvmpipe GL is
        /// exactly that — claiming a base colour map would buy nothing and risk everything, so we
        /// do not claim it and the file loads the way it loads today. Normal maps are still
        /// claimed regardless: their fix is the linear flag, which costs nothing and works
        /// everywhere.
        /// </summary>
        /// <param name="isKtx2">Whether the glTF texture is a KTX2 (<c>KHR_texture_basisu</c>).</param>
        /// <param name="isDataImage">Whether the image is used as data (a normal map).</param>
        /// <param name="astcSupported">Whether the device can sample ASTC 4x4, both twins.</param>
        public static bool Claims(bool isKtx2, bool isDataImage, bool astcSupported)
        {
            if (!isKtx2) return false;
            return isDataImage || astcSupported;
        }

        /// <summary>
        /// How to load one claimed image.
        /// </summary>
        /// <param name="isDataImage">Whether the image is used as data (a normal map). This is the
        /// add-on's own verdict from <see cref="GltfTextureRoles"/>, and it is what makes a normal
        /// map linear in a gamma project.</param>
        /// <param name="gltfLinear">The <c>linear</c> flag glTFast passed. False for everything in
        /// a gamma project; true for non-colour textures once the project moves to linear, at
        /// which point this class quietly agrees with it instead of fighting it.</param>
        /// <param name="readable">Whether glTFast asked for a CPU-readable copy.</param>
        /// <param name="astcSrgbSupported">Device support for <c>RGBA_ASTC4X4_SRGB</c>.</param>
        /// <param name="astcUNormSupported">Device support for <c>RGBA_ASTC4X4_UNorm</c>.</param>
        public static KtxLoadPlan Plan(
            bool isDataImage,
            bool gltfLinear,
            bool readable,
            bool astcSrgbSupported,
            bool astcUNormSupported)
        {
            var plan = new KtxLoadPlan
            {
                // Unchanged from the normal-map fix: a data image is linear, and so is anything
                // glTFast already said was linear.
                Linear = isDataImage || gltfLinear,
                Target = KtxTranscodeTarget.AutoSelect,
            };

            // 🔴 READABLE TEXTURES KEEP THE OLD PATH, and this is not a nicety.
            // KTX for Unity's format-forcing overload has no `readable` parameter and hard-codes it
            // to false (<c>KtxTexture.cs:147-174</c>). glTFast asks for readable on exactly the
            // textures it intends to CLONE for a second sampler, and that clone is gated on
            // `originalTexture.isReadable` (<c>GltfImport.cs:2757</c>); miss it and the second
            // variant silently loses its wrap mode with only a warning. An ASTC atlas that tiles
            // wrongly is a worse bug than the ETC2 one being fixed here.
            if (readable) return plan;

            KtxTranscodeTarget want = plan.Linear
                ? KtxTranscodeTarget.Astc4x4UNorm
                : KtxTranscodeTarget.Astc4x4Srgb;

            bool supported = want == KtxTranscodeTarget.Astc4x4UNorm
                ? astcUNormSupported
                : astcSrgbSupported;

            // Asking for a format the device cannot sample is not a silent no-op: the package
            // returns FormatUnsupportedBySystem and no texture at all (<c>KtxTexture.cs:155-158</c>).
            if (supported) plan.Target = want;
            return plan;
        }

        /// <summary>
        /// Adjust a plan once the FILE has been opened and its kind is known.
        ///
        /// 🔴 WHY THIS EXISTS — the ASTC file set (<see cref="AstcAssetPick"/>) puts a KTX2 through
        /// this loader that has nothing to transcode, and <see cref="Plan"/> cannot see that. Plan
        /// runs on the glTF's texture ROLES, before a single byte of the image is opened; whether
        /// the payload is a Basis-Universal intermediate or finished ASTC blocks is a property of
        /// the file, readable only from <c>KtxTexture.needsTranscoding</c> after <c>Open</c>
        /// (<c>KtxTexture.cs:27</c>).
        ///
        /// 🔴 WHAT GOES WRONG WITHOUT IT. Read <c>KtxTexture.LoadTexture2DInternal</c> in order:
        /// <c>:256</c> branches on <c>needsTranscoding</c>, and for a non-Basis file it takes the
        /// ELSE at <c>:289-300</c>, where <c>graphicsFormat</c> is read off the file (<c>:291</c>)
        /// and the <c>targetFormat</c> we asked for is never looked at again — <c>:262</c>, the only
        /// line that consumes it, is inside the branch not taken. So forcing a target on such a file
        /// is at best a no-op, and at worst a false negative: the format-forcing overload hard-codes
        /// <c>linear: true</c> on the way down (<c>KtxTexture.cs:160</c>), and <c>:296</c> then
        /// tests the FILE's format for <c>GraphicsFormatUsage.Linear</c> — linear FILTERING support,
        /// not colour space (<c>TranscodeFormatHelper.cs:454-461</c>) — a strictly narrower question
        /// than the <c>Sample</c> one every other gate in this codebase asks. A device that answers
        /// yes to Sample and no to Linear loses the texture to <c>FormatUnsupportedBySystem</c>
        /// (<c>:298</c>) and only gets it back after two more full open-and-load attempts through
        /// the fallback ladder in <c>LinearDataTextures.LoadImage</c>.
        ///
        /// So: no target, and no linear flag either. Neither is a loss. In the non-transcoding
        /// branch the format comes from the file and <c>linear</c> reaches nothing but that usage
        /// test — <c>LoadTextureData</c> is handed <c>graphicsFormat</c> and never <c>linear</c>
        /// (<c>KtxTexture.cs:347-355</c>) — so the texture that gets created is bit-identical either
        /// way, and asking the narrower question can only cost.
        ///
        /// 🔴 THE COROLLARY, and it belongs to whoever encodes the files: an ASTC GLB carries its
        /// own transfer function, and nothing downstream can override it. A normal map or an ORM
        /// packed as <c>*_SRGB</c> stays wrong on the device no matter what this loader does. The
        /// encode must emit UNorm for data images and sRGB for base colour and emissive — the same
        /// split <see cref="GltfTextureRoles"/> makes at load time for the Basis files.
        /// </summary>
        /// <param name="plan">The role-based plan from <see cref="Plan"/>.</param>
        /// <param name="needsTranscoding"><c>KtxTexture.needsTranscoding</c>, read after a
        /// successful <c>Open</c>. True for the Basis-Universal files that shipped, false for a
        /// file whose textures are already in a GPU format.</param>
        public static KtxLoadPlan PlanForFile(KtxLoadPlan plan, bool needsTranscoding)
        {
            if (needsTranscoding) return plan;
            return new KtxLoadPlan
            {
                Target = KtxTranscodeTarget.AutoSelect,
                Linear = false,
            };
        }
    }
}
