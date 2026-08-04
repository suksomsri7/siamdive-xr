namespace DiveMap.Core
{
    /// <summary>
    /// Which asset ids get a hero effect — the app's copy of the web builder's <c>fx:</c> tag.
    ///
    /// 🔴 WHY THIS IS A LIST AND NOT A <c>Contains</c>. It used to be
    /// <c>id.Contains("golden") || id.Contains("trident") || id.Contains("poseidon")</c>, and the
    /// last term was simply wrong. The web tags exactly ONE item gold (builder.html:1227,
    /// <c>sw:golden_trident</c>, "ตรีศูลทองคำ") and tags nothing else at all; substring matching
    /// swept in two more statues that the web renders as plain stone:
    ///
    ///     cc0:poseidon            "โพไซดอน"            builder.html:1153 — no fx
    ///     stat:verdant_poseidon   "โพไซดอนศิลาเขียว"    builder.html:1242 — no fx
    ///
    /// The second one names the bug: <i>ศิลาเขียว</i> is GREEN STONE. The app was painting it gold
    /// with emissive, metallic 0.9 and smoothness 0.75 — a different material, not a tint. An
    /// allowlist of ids cannot make that mistake, and when the web adds an <c>fx:</c> tag the change
    /// here is one line beside a citation.
    ///
    /// Kept UnityEngine-free so <c>tools/test.sh</c> can run it on this machine; <c>GoldFx</c> is
    /// the thin Unity wrapper that applies the material change.
    /// </summary>
    public static class FxRules
    {
        /// <summary>Ids the web builder tags <c>fx:'gold'</c>. Exactly one, and that is not an oversight.</summary>
        private static readonly string[] GoldenIds = { "sw:golden_trident" };

        /// <summary>
        /// Ids the web builder tags <c>fx:'beard'</c>. EMPTY, and deliberately so.
        ///
        /// 🔴 The app was swaying the beards of <c>sw:stone_king</c> and every "poseidon". The web
        /// does not: builder.html:1228 carries the comment "ยกเลิกเคราพริ้วตาม user 2026-07-04" —
        /// the user asked for the sway to be REMOVED, and it was. Porting it back reinstates a
        /// decision that was already reversed, and pays a per-frame <c>Update</c> on every statue to
        /// do it. The list stays here rather than being deleted so that turning it back on is one
        /// entry, exactly like gold.
        /// </summary>
        private static readonly string[] BeardIds = { };

        /// <summary>Does this asset get the gold treatment?</summary>
        public static bool IsGolden(string assetId) => Matches(assetId, GoldenIds);

        /// <summary>Does this asset get the beard/robe sway?</summary>
        public static bool HasBeard(string assetId) => Matches(assetId, BeardIds);

        /// <summary>
        /// Compare on a normalised id so the same rule works everywhere an id is spelled.
        ///
        /// The map and the QC pass both use the manifest's own form (<c>sw:golden_trident</c>), but
        /// a CDN filename for the same asset is <c>sw_golden_trident_xr0.glb</c> — same id, a colon
        /// turned into an underscore and a LOD suffix. Stripping everything that is not a letter or
        /// a digit makes both read <c>swgoldentrident…</c>, and a "starts with" comparison then
        /// accepts the suffixed filename while still refusing <c>cc0:poseidon</c>.
        /// </summary>
        private static bool Matches(string assetId, string[] ids)
        {
            if (string.IsNullOrEmpty(assetId) || ids == null || ids.Length == 0) return false;
            string id = Normalise(assetId);
            if (id.Length == 0) return false;

            for (int i = 0; i < ids.Length; i++)
            {
                string want = Normalise(ids[i]);
                if (want.Length == 0) continue;
                // Not equality: the CDN adds an _xr0/_xr1 LOD suffix to the very same asset.
                if (id.Length >= want.Length && id.Substring(0, want.Length) == want) return true;
            }
            return false;
        }

        /// <summary>Lower-case letters and digits only — separators and LOD punctuation dropped.</summary>
        private static string Normalise(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c >= 'a' && c <= 'z') sb.Append(c);
                else if (c >= 'A' && c <= 'Z') sb.Append((char)(c + 32));
                else if (c >= '0' && c <= '9') sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
