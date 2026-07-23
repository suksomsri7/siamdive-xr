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
        /// <summary>A unit-length fish pointing +Z, centred near origin. Cached & shared.</summary>
        public static Mesh Fish()
        {
            if (_fish != null) return _fish;
            _fish = BuildFish();
            return _fish;
        }
        private static Mesh _fish;

        private static Mesh BuildFish()
        {
            // Stretched octahedron body (nose at +Z, tail at −Z) + a vertical tail fin.
            var v = new[]
            {
                new Vector3( 0f,    0f,    0.50f), // 0 nose
                new Vector3( 0f,    0.15f, 0.00f), // 1 top
                new Vector3(-0.12f, 0f,    0.00f), // 2 left
                new Vector3( 0f,   -0.15f, 0.00f), // 3 bottom
                new Vector3( 0.12f, 0f,    0.00f), // 4 right
                new Vector3( 0f,    0f,   -0.40f), // 5 tail base
                new Vector3( 0f,    0.26f,-0.58f), // 6 tail fin top
                new Vector3( 0f,   -0.26f,-0.58f), // 7 tail fin bottom
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
            return m;
        }
    }
}
