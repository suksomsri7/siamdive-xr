using Newtonsoft.Json.Linq;
using UnityEngine;
using DiveMap.Core;

namespace DiveMap.Runtime
{
    /// <summary>
    /// Applies brush strokes to the live seabed, and rebuilds ONLY the seabed.
    ///
    /// Why this exists rather than reusing <see cref="AppBoot.RebuildFromMemory"/>: that path
    /// destroys and re-creates the whole map, which means re-loading every GLB. A brush stroke
    /// fires several times a second while a finger is down; doing that would freeze the app on
    /// the first stroke. The web has the same split — <c>applyBrush</c> calls <c>applyArea()</c>,
    /// which reshapes the seabed geometry and touches nothing else.
    ///
    /// The sculpt array lives in <c>env.sculpt</c>, which is what gets PATCHed, so a stroke is a
    /// change to the map exactly like moving an object is.
    /// </summary>
    public static class SeabedSculptor
    {
        /// <summary>The live sculpt heights, or null when the scene has no seabed data yet.</summary>
        public static float[] Heights { get; private set; }
        public static int Rings { get; private set; }
        public static int Segments { get; private set; }
        public static float SandRadius { get; private set; } = 300f;

        /// <summary>
        /// Take the arrays the builder just used, so a stroke edits the SAME data the mesh was
        /// made from. Called by SceneBuilder right after it registers the view.
        /// </summary>
        public static void Register(float[] sculpt, int rings, int seg, float sandRadius)
        {
            Heights = sculpt;
            Rings = rings;
            Segments = seg;
            SandRadius = sandRadius > 1f ? sandRadius : 300f;
            Debug.Log($"[Sculpt] registered rings={rings} seg={seg} samples={(sculpt != null ? sculpt.Length : 0)} " +
                      $"sandR={SandRadius:F0}");
        }

        public static bool Ready => Heights != null && Rings > 0 && Segments > 0;

        /// <summary>
        /// One stroke at a world point. Returns how many samples moved (0 = nothing to do, and
        /// the caller should not claim an edit happened).
        /// </summary>
        public static int Stroke(Vector3 worldPoint, float radius, float strength, bool raise)
        {
            if (!Ready) return 0;

            // The seabed is centred on the map root, so world x/z ARE local x/z here — the
            // builder never offsets it (BuildSeabedAndWater parents it at the origin).
            int touched = SculptBrush.Stroke(Heights, Rings, Segments, SandRadius,
                                             worldPoint.x, worldPoint.z, radius, strength, raise);
            if (touched > 0) Commit();
            return touched;
        }

        /// <summary>Whole-floor noise (the web's "สุ่มพื้น").</summary>
        public static void Randomise(float amplitude, int seed)
        {
            if (!Ready) return;
            SculptBrush.Noise(Heights, Rings, Segments, SandRadius, amplitude, seed);
            Commit();
            Debug.Log($"[Sculpt] randomised amp={amplitude} seed={seed}");
        }

        /// <summary>Flat again (the web's "รีเซ็ตเรียบ").</summary>
        public static void Flatten()
        {
            if (!Ready) return;
            SculptBrush.Reset(Heights);
            Commit();
            Debug.Log("[Sculpt] flattened");
        }

        /// <summary>
        /// Write the heights back into <c>env.sculpt</c> and redraw the floor.
        ///
        /// The JSON is the thing that gets saved, so it has to be updated even though the mesh
        /// is rebuilt from the in-memory array: skip this and the floor looks right until the
        /// next load, then reverts — the most confusing possible failure.
        /// </summary>
        private static void Commit()
        {
            var boot = Object.FindFirstObjectByType<AppBoot>();
            SceneData scene = boot != null ? boot.CurrentScene : null;
            if (scene != null)
            {
                if (!(scene.Root["env"] is JObject env))
                {
                    env = new JObject();
                    scene.Root["env"] = env;
                }
                var arr = new JArray();
                for (int i = 0; i < Heights.Length; i++) arr.Add(Heights[i]);
                env["sculpt"] = arr;
                env["sculptDim"] = new JArray(Rings, Segments);
            }
            Redraw();
        }

        /// <summary>Rebuild the seabed mesh in place — nothing else in the scene is touched.</summary>
        public static void Redraw()
        {
            GameObject mapRoot = GameObject.Find("Map");
            Transform seabed = mapRoot != null ? mapRoot.transform.Find("Seabed") : null;
            if (seabed == null) return;

            var sb = Object.FindFirstObjectByType<SceneBuilder>();
            if (sb == null) return;

            Mesh mesh = sb.RebuildSeabedMesh(Heights, Rings, Segments);
            if (mesh == null) return;

            var mf = seabed.GetComponent<MeshFilter>();
            if (mf != null) mf.sharedMesh = mesh;
            var mc = seabed.GetComponent<MeshCollider>();
            if (mc != null) mc.sharedMesh = mesh;
        }
    }
}
