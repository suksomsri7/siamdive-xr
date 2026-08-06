namespace DiveMap.Core
{
    /// <summary>
    /// Which FILE of a model to download: the ASTC-textured build, or the one that shipped.
    ///
    /// 🔴 WHY A SECOND FILE AT ALL. <see cref="KtxTranscodeTargets"/> fixed the DECODE — it stops
    /// the KTX package unpacking a UASTC texture into 4-bit ETC on a device that can sample ASTC.
    /// What it cannot fix is that the file still ships a Basis-Universal intermediate: the transcode
    /// happens on the phone, at load, every time, and an intermediate is not the same bits as an
    /// ASTC block. A GLB whose textures are already ASTC 4x4 skips the transcode entirely
    /// (<c>KtxTexture.cs:289-300</c>, the <c>needsTranscoding == false</c> branch, which uploads the
    /// file's own <c>graphicsFormat</c> untouched) — no intermediate, no per-load CPU, and the
    /// encoder's output is what the GPU samples.
    ///
    /// 🔴 WHY IT IS A CHOICE AND NOT A REPLACEMENT. An ASTC file is unreadable on a device with no
    /// ASTC support: the package answers <c>FormatUnsupportedBySystem</c> and hands back no texture
    /// (<c>KtxTexture.cs:296-299</c>). Every iOS device this app runs on supports ASTC (A8 and
    /// newer), but CI does not — the test/QC runner is a headless llvmpipe GL context — and a QC
    /// pass that renders untextured models is a QC pass that cannot see the thing it exists to
    /// check. So the pick is gated on the same <c>SystemInfo.IsFormatSupported</c> answer the
    /// transcode target is gated on, and a device that says no keeps the file it has today, byte
    /// for byte.
    ///
    /// Pure and here rather than in <c>AssetManifest</c> because "which URL" is the whole decision,
    /// and it is the part worth testing on a machine with no Unity Editor.
    /// </summary>
    public static class AstcAssetPick
    {
        /// <summary>The file to fetch, and whether it is the ASTC one (for the log line).</summary>
        public struct Choice
        {
            /// <summary>The URL to load. Never blank when either input was usable.</summary>
            public string Url;

            /// <summary>True when <see cref="Url"/> is the ASTC build.</summary>
            public bool Astc;
        }

        /// <summary>
        /// Pick between the ASTC build of a model and the one shipped before it existed.
        ///
        /// Additive by construction: a module with no ASTC url — which, on the day this lands, is
        /// every single one of them — returns <paramref name="fallbackUrl"/> unchanged, so the
        /// manifest can grow the field one model at a time with no flag day.
        /// </summary>
        /// <param name="astcUrl">The <c>xrGlbUrlAstc</c> field, or null when the model has none.</param>
        /// <param name="fallbackUrl">What this model resolved to before ASTC existed.</param>
        /// <param name="astcSupported">Whether the device can sample ASTC 4x4 — BOTH twins, the
        /// same gate <see cref="KtxTranscodeTargets.Claims"/> uses, because a file can carry an
        /// sRGB texture and a UNorm one and the device has to read both.</param>
        public static Choice Pick(string astcUrl, string fallbackUrl, bool astcSupported)
        {
            bool hasAstc = !string.IsNullOrWhiteSpace(astcUrl);
            bool hasFallback = !string.IsNullOrWhiteSpace(fallbackUrl);

            if (hasAstc && astcSupported)
            {
                return new Choice { Url = astcUrl, Astc = true };
            }

            // 🔴 AN UNSUPPORTED ASTC FILE STILL BEATS NOTHING. Reaching here with no fallback means
            // the manifest lists a model in ASTC only; returning null would drop that model from the
            // map entirely — a hole in the seabed — where loading it costs, at worst, its textures
            // (the mesh, the animation and the placement are all unaffected by the texture codec).
            // A grey model is a bug report. A missing one is a map that looks finished and is not.
            if (!hasFallback && hasAstc)
            {
                return new Choice { Url = astcUrl, Astc = true };
            }

            return new Choice { Url = fallbackUrl, Astc = false };
        }

        /// <summary>
        /// The one-line proof, emitted once per model per manifest. Reads what was CHOSEN rather
        /// than what was available, because "the field is in the manifest" and "the device took it"
        /// are the two facts that go wrong independently.
        /// </summary>
        public static string LogLine(string assetId, Choice choice, bool lod1 = false)
        {
            return string.Concat(
                "[AssetPick] id=", assetId ?? "?",
                lod1 ? " lod=1" : " lod=0",
                " astc=", choice.Astc ? "t" : "f",
                " url=", choice.Url ?? "(none)");
        }
    }
}
