using System.Collections.Generic;
using UnityEngine;

namespace DiveMap.Runtime
{
    /// <summary>
    /// E8 — the portals that carry a diver between maps (the web's <c>warp:*</c> items,
    /// builder.html:2525 for the look and 3810 for the trigger).
    ///
    /// The web builds two counter-rotating torus rings, two additive core discs, a halo, a point
    /// light and eighteen orbiting motes. That is ported here rather than simplified, because a
    /// warp gate is the one object in the map whose entire job is to say "come here" from a
    /// distance — a dull one is a broken one. The meshes are generated (Unity has no torus
    /// primitive) and the additive material is the same DM_StandardTransparent recipe the god rays
    /// and the drone lights use, so no new shader can be stripped from the build.
    ///
    /// Flying within 13 units opens the destination picker; the trigger re-arms only after leaving
    /// a 16-unit ring, which is the web's own guard against a cancelled picker firing again the
    /// moment you drift.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WarpGate : MonoBehaviour
    {
        // The two radii live in Core (DiveMap.Core.WarpSpawn) because the spawn rule has to keep
        // clear of them — a diver dropped inside the trigger ring opens the destination picker on
        // the first frame of the dive — and Core is the half that can be unit-tested on a machine
        // with no Unity. Same numbers, one place to change them.
        public const float TriggerRadius = Core.WarpSpawn.TriggerRadius;
        public const float RearmRadius = Core.WarpSpawn.RearmRadius;

        /// <summary>The web lifts the gate off the seabed; the spawn rule lands the diver level with it.</summary>
        public const float Height = 14f;

        private static readonly List<WarpGate> All = new List<WarpGate>();

        private Transform _ringA, _ringB, _core, _core2, _halo, _swirl;
        private Light _light;

        /// <summary>Every gate in the loaded map.</summary>
        public static IReadOnlyList<WarpGate> Gates => All;

        /// <summary>Build a gate under <paramref name="parent"/> (an item pivot).</summary>
        public static WarpGate Attach(Transform parent)
        {
            if (parent == null) return null;

            var go = new GameObject("WarpGate");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, Height, 0f);
            var gate = go.AddComponent<WarpGate>();
            gate.Build();
            return gate;
        }

        private void Awake() => All.Add(this);

        private void OnDestroy() => All.Remove(this);

        private void Build()
        {
            _ringA = MakePart("RingBlue", TorusMesh(8f, 0.9f, 18, 40),
                              Solid(new Color(0.165f, 0.424f, 1f), 2.0f));
            _ringB = MakePart("RingWhite", TorusMesh(8f, 0.6f, 16, 36),
                              Solid(new Color(0.918f, 0.965f, 1f), 2.6f));
            _ringB.localRotation = Quaternion.Euler(0f, 90f, 0f);

            _core = MakePart("Core", DiscMesh(7f, 48), Additive(new Color(0.102f, 0.298f, 1f), 0.34f));
            _core2 = MakePart("Core2", DiscMesh(4.4f, 40), Additive(new Color(0.875f, 0.937f, 1f), 0.5f));
            _core2.localPosition = new Vector3(0f, 0f, 0.15f);
            _halo = MakePart("Halo", DiscMesh(11f, 40), Additive(new Color(0.227f, 0.482f, 1f), 0.12f));
            _halo.localPosition = new Vector3(0f, 0f, -0.3f);

            var lightGo = new GameObject("GateLight");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0f, 3f);
            _light = lightGo.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = new Color(0.333f, 0.659f, 1f);
            _light.range = 130f;
            _light.intensity = 3.6f;
            _light.shadows = LightShadows.None;

            // 18 motes orbiting the ring, alternating white and pale blue.
            _swirl = new GameObject("Swirl").transform;
            _swirl.SetParent(transform, false);
            Mesh mote = DiscMesh(0.34f, 8);
            for (int i = 0; i < 18; i++)
            {
                Transform t = MakePart("Mote" + i, mote,
                    Additive(i % 2 == 0 ? Color.white : new Color(0.525f, 0.769f, 1f), 0.9f), _swirl);
                t.localPosition = new Vector3(Mathf.Cos(i) * 7.4f, Mathf.Sin(i) * 7.4f, 0f);
            }
        }

        private Transform MakePart(string name, Mesh mesh, Material mat, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go.transform;
        }

        private void Update()
        {
            float t = Time.time;
            if (_ringA != null) _ringA.Rotate(0.03f * 60f * Time.deltaTime, 0f, 0f);
            if (_ringB != null) _ringB.Rotate(-0.045f * 60f * Time.deltaTime, 0f, 0f);
            if (_core != null)
            {
                _core.localScale = Vector3.one * (1f + Mathf.Sin(t * 2.6f) * 0.10f);
                _core.Rotate(0f, 0f, 0.012f * 60f * Time.deltaTime);
            }
            if (_core2 != null)
            {
                _core2.localScale = Vector3.one * (1f + Mathf.Sin(t * 2.6f + 1f) * 0.16f);
                _core2.Rotate(0f, 0f, -0.03f * 60f * Time.deltaTime);
            }
            if (_halo != null) _halo.localScale = Vector3.one * (1f + Mathf.Sin(t * 1.6f) * 0.12f);
            if (_light != null) _light.intensity = 3.0f + Mathf.Sin(t * 3.2f) * 1.4f;

            if (_swirl != null)
            {
                for (int i = 0; i < _swirl.childCount; i++)
                {
                    Transform d = _swirl.GetChild(i);
                    float sp = 0.6f + (i % 3) * 0.25f;
                    float tilt = (i % 2 == 0 ? 1f : -1f) * 0.6f;
                    float a = t * 0.03f * 60f * sp + i / 18f * 6.283f;
                    float rr = 7.4f * (0.82f + 0.18f * Mathf.Sin(t * 1.4f + i));
                    d.localPosition = new Vector3(Mathf.Cos(a) * rr,
                                                  Mathf.Sin(a) * rr * Mathf.Cos(tilt),
                                                  Mathf.Sin(a) * rr * Mathf.Sin(tilt));
                }
            }
        }

        // ── materials / meshes ───────────────────────────────────────────────────

        private static Material Solid(Color c, float glow)
        {
            Material src = Resources.Load<Material>("DM_Standard");
            var mat = src != null ? new Material(src) : new Material(Shader.Find("Standard"));
            // No _EMISSION keyword (it is stripped from the build → magenta). A bright albedo plus
            // the gate's own point light carries the glow instead.
            mat.color = c * Mathf.Clamp(glow, 1f, 2f);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.6f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.4f);
            return mat;
        }

        private static Material Additive(Color c, float alpha)
        {
            Material src = Resources.Load<Material>("DM_StandardTransparent");
            var mat = src != null ? new Material(src) : new Material(Shader.Find("Standard"));
            if (mat.HasProperty("_Mode")) mat.SetFloat("_Mode", 3f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)(int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)(int)UnityEngine.Rendering.BlendMode.One);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3080;
            Color col = c; col.a = alpha;
            mat.color = col;
            return mat;
        }

        private static readonly Dictionary<string, Mesh> Meshes = new Dictionary<string, Mesh>();

        /// <summary>A torus in the XY plane (Unity has no torus primitive).</summary>
        private static Mesh TorusMesh(float major, float minor, int minorSeg, int majorSeg)
        {
            string key = $"torus{major}_{minor}_{minorSeg}_{majorSeg}";
            if (Meshes.TryGetValue(key, out Mesh cached) && cached != null) return cached;

            var verts = new Vector3[(majorSeg + 1) * (minorSeg + 1)];
            var norms = new Vector3[verts.Length];
            var uvs = new Vector2[verts.Length];
            var tris = new int[majorSeg * minorSeg * 6];

            int v = 0;
            for (int i = 0; i <= majorSeg; i++)
            {
                float u = i / (float)majorSeg * Mathf.PI * 2f;
                Vector3 centre = new Vector3(Mathf.Cos(u) * major, Mathf.Sin(u) * major, 0f);
                Vector3 outward = new Vector3(Mathf.Cos(u), Mathf.Sin(u), 0f);
                for (int j = 0; j <= minorSeg; j++)
                {
                    float w = j / (float)minorSeg * Mathf.PI * 2f;
                    Vector3 n = outward * Mathf.Cos(w) + Vector3.forward * Mathf.Sin(w);
                    verts[v] = centre + n * minor;
                    norms[v] = n;
                    uvs[v] = new Vector2(i / (float)majorSeg, j / (float)minorSeg);
                    v++;
                }
            }

            int t2 = 0;
            for (int i = 0; i < majorSeg; i++)
            for (int j = 0; j < minorSeg; j++)
            {
                int a = i * (minorSeg + 1) + j;
                int b = a + minorSeg + 1;
                tris[t2++] = a; tris[t2++] = b; tris[t2++] = a + 1;
                tris[t2++] = a + 1; tris[t2++] = b; tris[t2++] = b + 1;
            }

            var m = new Mesh { name = key };
            m.vertices = verts;
            m.normals = norms;
            m.uv = uvs;
            m.triangles = tris;
            m.RecalculateBounds();
            Meshes[key] = m;
            return m;
        }

        /// <summary>A flat disc in the XY plane, drawn from both sides.</summary>
        private static Mesh DiscMesh(float radius, int seg)
        {
            string key = $"disc{radius}_{seg}";
            if (Meshes.TryGetValue(key, out Mesh cached) && cached != null) return cached;

            var verts = new Vector3[seg + 1];
            var uvs = new Vector2[seg + 1];
            verts[0] = Vector3.zero;
            uvs[0] = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                verts[i + 1] = new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f);
                uvs[i + 1] = new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f);
            }

            var tris = new int[seg * 6];
            int t = 0;
            for (int i = 0; i < seg; i++)
            {
                int b = 1 + i, c = 1 + (i + 1) % seg;
                tris[t++] = 0; tris[t++] = b; tris[t++] = c;
                tris[t++] = 0; tris[t++] = c; tris[t++] = b;   // both faces
            }

            var m = new Mesh { name = key };
            m.vertices = verts;
            m.uv = uvs;
            m.triangles = tris;
            var n = new Vector3[verts.Length];
            for (int i = 0; i < n.Length; i++) n[i] = Vector3.back;
            m.normals = n;
            m.RecalculateBounds();
            Meshes[key] = m;
            return m;
        }
    }
}
