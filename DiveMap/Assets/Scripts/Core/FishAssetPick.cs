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
    ///   school:scad       Scad_School_xr0.glb        670 tris   local len 1.911
    ///   school:barracuda  Barracuda_School_xr0.glb   450 tris   local len 1.862
    ///   pod:yellowtail    Trevally_xr0.glb         8,800 tris   local len 1.899
    ///                     Trevally_xr1.glb         3,999 tris   (LOD1)
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
                // 🔴 Re-measured 2 ส.ค. against the rebuilt CDN files. These numbers are not
                // decoration: Pick() multiplies Tris0 by the school size to decide whether a
                // school drops to LOD1, so a table that still says 670 while the file is 3,000
                // under-counts the load by 4.5× and keeps LOD0 on a school that should have
                // stepped down. The old figures came from files that were built from the WEB
                // pipeline's already-decimated output — they were never the ceiling, just the
                // last thing anyone had measured.
                //
                // The fish on HTMS Chang are why: reported as "สัตว์ทะเลก็ยังแตกละเอียด" on a map
                // where nothing had been touched, and the reason was 450–670 triangles per fish.
                case "school:scad":
                    spec = new Spec { Tris0 = 3000, Tris1 = 3000, LocalLen = 1.911f };
                    return true;
                case "school:barracuda":
                    spec = new Spec { Tris0 = 3000, Tris1 = 3000, LocalLen = 1.862f };
                    return true;
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
            // `Tris1 < Tris0` is new, and it matters now that some species have no lighter variant
            // to fall back to. The rebuilt scad and barracuda come from a 3,000-triangle source, so
            // their LOD1 is also 3,000 — swapping would fetch a SECOND file over the network and
            // draw exactly the same number of triangles. The count guard alone would have done it.
            bool wantLod1 = load > Lod1TriBudget && spec.Tris0 > Lod1MinTris
                            && spec.Tris1 < spec.Tris0 && lod1 != null;
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
