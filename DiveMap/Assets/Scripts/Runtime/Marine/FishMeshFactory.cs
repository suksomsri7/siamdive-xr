using UnityEngine;

namespace DiveMap.Runtime.Marine
{
    /// <summary>
    /// Builds a cheap procedural fish mesh (≈10 tris) oriented along +Z (forward), unit
    /// length. WO-XR-03 instances this many times per school via Graphics.RenderMeshInstanced.
    ///
    /// Why procedural and not the real GLB fish: the marine GLBs are Draco-compressed,
    /// KTX2-textured, single SKINNED fish. Extracting a static instanced mesh from them
    /// under the headless GLCore/llvmpipe QC player is fragile (the same WebP/KTX2 +
    /// magenta-shader hazards that forced SceneBuilder's placeholder path). A tiny
    /// solid-shaded mesh on the proven DM_Standard material renders deterministically in
    /// the QC screenshot and costs ~10 tris × ~415 fish ≈ 4k tris. Swapping in the real
    /// LOD0 instanced fish is WO-XR-04 (the LOD/immersive pass) territory.
    /// </summary>
    public static class FishMeshFactory
    {
        /// <summary>A ~1.9-unit fish pointing +Z, centred near origin. Cached & shared.</summary>
        public static Mesh Fish()
        {
            if (_fish != null) return _fish;
            _fish = BuildFish();
            return _fish;
        }
        private static Mesh _fish;

        /// <summary>The procedural mesh's true nose→tail length at scale 1 (max AABB
        /// dimension). The old doc called this "unit length" but the raw octahedron below is
        /// only ~1.08 long — callers need the REAL value to size fish against the marine-GLB
        /// pipeline (<see cref="WebNormLen"/>), which was the QC-r6 "fish half too small" bug.</summary>
        public static float BaseLen { get { Fish(); return _baseLen; } }
        private static float _baseLen = 1f;

        /// <summary>Marine-GLB asset-pipeline normalization. Every marine GLB the web renders
        /// — scad 1.911, barracuda 1.897, whaleshark 1.908, sharks/rays/whales all ≈1.9 —
        /// is normalized so its longest axis measures ≈1.9 units (verified from each GLB's
        /// POSITION accessor min/max). The web sizes a fish as flen(≈1.9)×itemScale, so the
        /// procedural fish must reach the same 1.9-unit base to match the web's on-screen size
        /// at a given item scale. (Was: raw ~1.08 mesh × itemScale ⇒ fish ~57% of web size.)</summary>
        public const float WebNormLen = 1.9f;

        /// <summary>Uniform factor that grows the ~1.08-unit procedural mesh to the web's
        /// 1.9-unit fish norm, so world length = BaseLen×(WebScaleFactor×itemScale) =
        /// WebNormLen×itemScale — matching the web's flen×itemScale sizing.</summary>
        public static float WebScaleFactor { get { Fish(); return WebNormLen / _baseLen; } }

        private static Mesh BuildFish()
        {
            // Stretched octahedron body (nose at +Z, tail at −Z) + a vertical tail fin.
            // Body widened (±0.12→±0.18 X, ±0.15→±0.18 Y) so a fish presenting its head/tail
            // to the camera is still a readable blob, not a 1-px sliver — the other half of the
            // QC r5 "tiny dots" miss (a 4.5:1 needle was invisible edge-on).
            var v = new[]
            {
                new Vector3( 0f,    0f,    0.50f), // 0 nose
                new Vector3( 0f,    0.18f, 0.00f), // 1 top
                new Vector3(-0.18f, 0f,    0.00f), // 2 left
                new Vector3( 0f,   -0.18f, 0.00f), // 3 bottom
                new Vector3( 0.18f, 0f,    0.00f), // 4 right
                new Vector3( 0f,    0f,   -0.40f), // 5 tail base
                new Vector3( 0f,    0.30f,-0.58f), // 6 tail fin top
                new Vector3( 0f,   -0.30f,-0.58f), // 7 tail fin bottom
            };

            var t = new[]
            {
                // nose cap
                0,1,2,  0,2,3,  0,3,4,  0,4,1,
                // tail cap
                5,2,1,  5,3,2,  5,4,3,  5,1,4,
                // tail fin (double-sided so it reads from both eyes)
                5,6,7,  5,7,6,
            };

            var m = new Mesh { name = "MarineFish" };
            m.vertices = v;
            m.triangles = t;
            m.RecalculateNormals();
            m.RecalculateBounds();
            // Record the true base length (max AABB dimension) so the school renderer can grow
            // the fish to the web's 1.9-unit GLB norm instead of drawing this ~1.08 mesh raw.
            Vector3 sz = m.bounds.size;
            _baseLen = Mathf.Max(sz.x, Mathf.Max(sz.y, sz.z));
            if (_baseLen < 1e-4f) _baseLen = 1f;
            return m;
        }
    }
}
