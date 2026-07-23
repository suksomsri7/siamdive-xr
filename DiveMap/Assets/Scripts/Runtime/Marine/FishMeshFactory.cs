using UnityEngine;

namespace DiveMap.Runtime.Marine
{
    /// <summary>
    /// Builds a cheap procedural fish mesh (≈10 tris) oriented along +Z (forward). WO-XR-03
    /// instances this many times per school via Graphics.RenderMeshInstanced.
    ///
    /// Shape (QC r7): a slim, LATERALLY-COMPRESSED body (thin side-to-side, taller
    /// top-to-bottom) + a vertical tail fin — a fish silhouette, not the old symmetric
    /// diamond/lozenge that read as a paper kite when drawn huge. The renderer scales each
    /// fish to a small per-species world length (≈0.35 m scad … ≈1.5 m barracuda), so the
    /// mesh is authored at unit-ish size and its true length is measured back via
    /// <see cref="BaseLen"/> for the draw-scale maths.
    ///
    /// Why procedural and not the real GLB fish: the marine GLBs are Draco-compressed,
    /// KTX2-textured, single SKINNED fish. Extracting a static instanced mesh from them
    /// under the headless GLCore/llvmpipe QC player is fragile (the same WebP/KTX2 +
    /// magenta-shader hazards that forced SceneBuilder's placeholder path). A tiny
    /// solid-shaded mesh on the proven DM_Standard material renders deterministically in
    /// the QC screenshot. Swapping in the real LOD0 instanced fish is WO-XR-04 territory.
    /// </summary>
    public static class FishMeshFactory
    {
        /// <summary>A slim fish pointing +Z, centred near origin. Cached &amp; shared.</summary>
        public static Mesh Fish()
        {
            if (_fish != null) return _fish;
            _fish = BuildFish();
            return _fish;
        }
        private static Mesh _fish;

        /// <summary>The procedural mesh's true nose→tail length at scale 1 (max AABB
        /// dimension, ≈1.15). The school renderer needs the REAL value to convert a wanted
        /// per-species world length (metres) into a uniform TRS draw scale
        /// (drawScale = worldLen / BaseLen).</summary>
        public static float BaseLen { get { Fish(); return _baseLen; } }
        private static float _baseLen = 1f;

        private static Mesh BuildFish()
        {
            // Slim, laterally-compressed body (nose at +Z, tail at −Z) + a vertical tail fin.
            // KEY (QC r7): width X (±0.12) < height Y (±0.15..0.17) so the cross-section is a
            // tall, narrow fish shape — NOT the old symmetric ±0.18/±0.18 lozenge that read as
            // a diamond "kite". Kept wide enough (0.24 across) that a fish presenting head/tail
            // to the camera is still a readable blob, not a 1-px sliver (the r5 edge-on miss).
            var v = new[]
            {
                new Vector3( 0f,    0f,    0.55f), // 0 nose
                new Vector3( 0f,    0.17f, 0.00f), // 1 top (dorsal)
                new Vector3(-0.12f, 0f,    0.00f), // 2 left  (thin)
                new Vector3( 0f,   -0.15f, 0.00f), // 3 bottom (belly)
                new Vector3( 0.12f, 0f,    0.00f), // 4 right (thin)
                new Vector3( 0f,    0f,   -0.42f), // 5 tail base
                new Vector3( 0f,    0.28f,-0.60f), // 6 tail fin top
                new Vector3( 0f,   -0.28f,-0.60f), // 7 tail fin bottom
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
            // Record the true base length (max AABB dimension ≈1.15) so the school renderer can
            // scale each fish to its small per-species world length.
            Vector3 sz = m.bounds.size;
            _baseLen = Mathf.Max(sz.x, Mathf.Max(sz.y, sz.z));
            if (_baseLen < 1e-4f) _baseLen = 1f;
            return m;
        }
    }
}
