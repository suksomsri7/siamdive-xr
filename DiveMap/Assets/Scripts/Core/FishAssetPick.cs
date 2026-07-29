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
        /// <summary>A school this big pays for the LOD1 swap…</summary>
        public const int Lod1MinCount = 100;
        /// <summary>…when its LOD0 is heavier than this.</summary>
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
                case "school:scad":
                    spec = new Spec { Tris0 = 670, Tris1 = 670, LocalLen = 1.911f };
                    return true;
                case "school:barracuda":
                    spec = new Spec { Tris0 = 450, Tris1 = 450, LocalLen = 1.862f };
                    return true;
                case "pod:yellowtail":
                    spec = new Spec { Tris0 = 8800, Tris1 = 3999, LocalLen = 1.899f };
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

            // Heavy LOD0 × a big school = the triangle budget blows up (trevally would be
            // 100 × 8,800 = 880k tris on its own), so those schools take LOD1.
            bool wantLod1 = count >= Lod1MinCount && spec.Tris0 > Lod1MinTris && lod1 != null;
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
