using System;

namespace DiveMap.Core
{
    /// <summary>
    /// WO-XR-04.1 — which XR GLB (and which LOD) a school/pod species should instance,
    /// and what its authored local length is.
    ///
    /// Pure + table-driven ON PURPOSE. <c>asset_manifest.json</c> carries the two URLs
    /// (<c>xrGlbUrl</c>, <c>xrGlbUrlLod1</c>) but NOT their triangle counts, so the LOD
    /// decision needs numbers measured off the real CDN files (Fable's WO-XR-04 survey):
    ///
    ///   school:scad       Scad_School_xr0.glb      6,650 tris   local len 1.911
    ///                     Scad_School_xr1.glb      3,296 tris   (LOD1, −50.4 %)
    ///   school:barracuda  Barracuda_School_xr0.glb 3,029 tris   local len 1.862
    ///                     Barracuda_School_xr1.glb 1,595 tris   (LOD1, −47.3 %)
    ///   pod:yellowtail    Trevally_xr0.glb         8,000 tris   local len 1.899
    ///                     Trevally_xr1.glb         6,442 tris   (LOD1, −19.5 %)
    ///
    /// ⚠️ The CDN filenames are stable across re-exports, so a rebuilt model arrives with the same
    /// URL and a different triangle count and NOTHING in the app can tell. Re-measure here.
    ///
    /// A species that is NOT in this table returns false → the caller keeps the proven
    /// procedural <c>FishMeshFactory</c> mesh. We never guess a GLB for an unknown id:
    /// an un-surveyed model could be a whole baked school, an un-transcodable texture,
    /// or 200k tris × 120 instances.
    ///
    /// <see cref="LocalLen"/> is only the EXPECTED length (a log-time sanity oracle) —
    /// the renderer scales fish by the length actually measured from the loaded mesh.
    /// </summary>
    public static class FishAssetPick
    {
        /// <summary>
        /// Per-school triangle budget: count × LOD0 tris above this takes LOD1. Budget-based
        /// rather than count-based because the real demo map showed why — the trevally pods
        /// are 50 fish each, under any sane "big school" count, yet at 8,800 tris apiece they
        /// were 880k triangles between them (more than every other fish in the map combined,
        /// and the QC frame cost went 136 → 300 ms). 200k is roughly one heavy pod's worth.
        /// </summary>
        public const int Lod1TriBudget = 200_000;
        /// <summary>A model lighter than this is never worth swapping down.</summary>
        public const int Lod1MinTris = 2000;

        /// <summary>
        /// How much lighter LOD1 has to be before the swap is worth making, in percent.
        ///
        /// 🔴 Why a percentage and not just "smaller". For one day the CDN carried a scad "LOD1" of
        /// 6,616 triangles against a 6,650-triangle LOD0 — smaller, by 34 triangles, i.e. half of
        /// one percent — and its barracuda LOD1 was BIGGER than its LOD0. Under a plain
        /// <c>Tris1 &lt; Tris0</c> test the scad school would have cleared the budget, fetched a
        /// SECOND file over the network, held a second mesh in memory, and drawn 793,920 triangles
        /// instead of 798,000. Those files are gone (the real LODs landed the same day and the
        /// table below has them), but the rule stays: a filename ending in <c>_xr1</c> is a claim,
        /// not a measurement, and this is the only place the claim is ever checked.
        ///
        /// 10 % is set BELOW the tightest real LOD in the table — the trevally's 8,000 → 6,442 =
        /// 19.5 % — and far above that 0.5 % artefact. Genuine decimation beats it by a mile
        /// (scad 50.4 %, barracuda 47.3 %), so this rejects accidents, not LODs.
        ///
        /// <see cref="WorthSwappingDown"/> is the test itself, exposed because the numbers it has
        /// to keep refusing are no longer in the table — they are fixtures in the test file.
        /// </summary>
        public const int Lod1MinSavingPercent = 10;

        /// <summary>
        /// Is dropping from <paramref name="tris0"/> to <paramref name="tris1"/> worth a second
        /// download? Integer maths, so the threshold means exactly what it says at every size.
        /// </summary>
        public static bool WorthSwappingDown(int tris0, int tris1)
            => (long)tris1 * 100L <= (long)tris0 * (100L - Lod1MinSavingPercent);

        public struct Pick
        {
            public string Species;
            public string Url;
            public bool   IsLod1;
            /// <summary>Triangles of ONE fish at the chosen LOD.</summary>
            public int    Tris;
            /// <summary>Authored nose→tail length of the GLB mesh in its own units.</summary>
            public float  LocalLen;
        }

        private struct Spec
        {
            public int   Tris0;
            public int   Tris1;
            public float LocalLen;
        }

        private static bool TrySpec(string assetId, out Spec spec)
        {
            spec = default;
            if (string.IsNullOrWhiteSpace(assetId)) return false;
            switch (assetId.Trim().ToLowerInvariant())
            {
                // 🔴 These numbers are not decoration: Pick() multiplies Tris0 by the school size to
                // decide whether a school drops to LOD1, so a stale row silently mis-sizes the
                // whole map. They are counted off the REAL files on the CDN and nothing else — the
                // 450/670 figures this table shipped with came from the web pipeline's already
                // decimated output and were never the ceiling ("สัตว์ทะเลก็ยังแตกละเอียด" on HTMS
                // Chang); the 3,000s that replaced them were the count of the files as they stood
                // that morning, and the morning's files are not the ones on the CDN any more.
                //
                // 🔴 Counted again 2 ส.ค. (twice that day) as the model team rebuilt the school GLBs
                // over the SAME filenames. Nothing in the app can notice a rebuild — the manifest
                // URL does not change — so this table is the only place it can be told, and a
                // re-export that keeps its filename must always come back here.
                //
                //   school:scad       xr0 6,650   xr1 3,296   → −50.4 %, a real LOD
                //   school:barracuda  xr0 3,029   xr1 1,595   → −47.3 %, a real LOD
                //
                // The first rebuild of the day shipped an "xr1" that was 0.5 % lighter for the scad
                // and HEAVIER for the barracuda. Both were written down here exactly as measured
                // rather than flattered (and no xr1 was ever pointed at an xr0 file): this table's
                // whole job is to say what is actually on the CDN, and a number invented to make
                // the picker behave hides a broken export from the only people who could fix it.
                // The measurement caught it; Lod1MinSavingPercent refused the pointless swap until
                // the real LODs landed a few hours later. Keep doing exactly that.
                case "school:scad":
                    spec = new Spec { Tris0 = 6650, Tris1 = 3296, LocalLen = 1.911f };
                    return true;
                case "school:barracuda":
                    spec = new Spec { Tris0 = 3029, Tris1 = 1595, LocalLen = 1.862f };
                    return true;
                // Untouched by either rebuild — same files, same numbers. Still the TIGHTEST real
                // saving in the table (19.5 %), which is what Lod1MinSavingPercent has to stay
                // under: raise the threshold past this row and the pod stops swapping down.
                case "pod:yellowtail":
                    spec = new Spec { Tris0 = 8000, Tris1 = 6442, LocalLen = 1.899f };
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Pick the GLB for <paramref name="assetId"/>. <paramref name="count"/> is the
        /// largest instance count any one school of this species will draw. Returns false
        /// (and a zeroed pick) for an unsurveyed species or when neither URL is usable —
        /// the caller must then fall back to the procedural mesh.
        /// </summary>
        public static bool TryPick(string assetId, string xrGlbUrl, string xrGlbUrlLod1,
                                   int count, out Pick pick)
        {
            pick = default;
            if (!TrySpec(assetId, out Spec spec)) return false;

            string lod0 = string.IsNullOrWhiteSpace(xrGlbUrl) ? null : xrGlbUrl.Trim();
            string lod1 = string.IsNullOrWhiteSpace(xrGlbUrlLod1) ? null : xrGlbUrlLod1.Trim();
            if (lod0 == null && lod1 == null) return false;

            // Heavy LOD0 × a whole school = the triangle budget blows up, so those take LOD1.
            long load = (long)Math.Max(1, count) * spec.Tris0;
            // …but only if there is a genuinely lighter file to go to. A species whose "LOD1" is
            // the same model under another name costs a second download and saves nothing, which
            // is worse than the budget it was meant to protect.
            bool wantLod1 = load > Lod1TriBudget && spec.Tris0 > Lod1MinTris
                            && WorthSwappingDown(spec.Tris0, spec.Tris1) && lod1 != null;
            if (lod0 == null) wantLod1 = true;   // only LOD1 shipped
            if (lod1 == null) wantLod1 = false;  // only LOD0 shipped

            pick = new Pick
            {
                Species  = assetId,
                Url      = wantLod1 ? lod1 : lod0,
                IsLod1   = wantLod1,
                Tris     = wantLod1 ? spec.Tris1 : spec.Tris0,
                LocalLen = spec.LocalLen,
            };
            return true;
        }
    }
}
