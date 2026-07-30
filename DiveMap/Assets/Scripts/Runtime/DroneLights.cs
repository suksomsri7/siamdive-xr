using DiveMap.Core;
using UnityEngine;

namespace DiveMap.Runtime
{
    /// <summary>
    /// P1.2 — the drone's lighting rig and the underwater atmosphere that goes with it, ported
    /// from the web (builder.html 3664-3670 / 3751-3765 / 3828-3840):
    ///
    ///   • a point light travelling with the drone — the "clear bubble" you can always see in
    ///   • two headlamps aimed forward-and-down, throwing two overlapping pools on the sand
    ///   • two soft cones so the beams are visible in the water, not just their result
    ///   • and the atmosphere swap: with the lamps off the water closes in (fog 70-200, nearly
    ///     black-green, ambient ×0.32); with them on it opens up (170-680, blue, ×0.55)
    ///
    /// That last part is what makes the button worth pressing. All values live in
    /// <see cref="DiveLightMath"/> and are unit-tested; this class only builds objects and moves
    /// them. The scene's own fog/ambient are captured on the way in and restored on the way out,
    /// so leaving the tour cannot leave the map view murky.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DroneLights : MonoBehaviour
    {
        private Light _dive;
        private Light _lampA, _lampB;
        private Transform _poolA, _poolB;
        private Transform _beamA, _beamB;
        private bool _on = true;

        // Scene atmosphere as it was before the tour.
        private bool _savedFog;
        private Color _savedFogColor;
        private float _savedFogStart, _savedFogEnd;
        private Color _savedSky, _savedEquator, _savedGround;
        private float _savedSun = 1f, _savedFill = 1f;
        private Light _sun, _fill;
        private bool _saved;

        public bool HeadlightOn => _on;

        public static DroneLights Attach(Transform parent)
        {
            var go = new GameObject("DroneLights");
            if (parent != null) go.transform.SetParent(parent, false);
            var d = go.AddComponent<DroneLights>();
            d.Build();
            return d;
        }

        private void Build()
        {
            // Dive light: travels with you so the near field is never black (web: 0xe2f3ff,
            // range 150, decay 1.1).
            _dive = NewLight("DiveLight", LightType.Point, new Color(0.886f, 0.953f, 1f));
            _dive.range = 150f;

            // Headlamps: bright, long-throw, soft-edged — the web's SpotLight(0xf2f9ff, …, 460,
            // angle 0.9 rad, penumbra 0.65). Unity's spotAngle is the FULL cone, hence ×2.
            _lampA = NewLight("LampA", LightType.Spot, new Color(0.949f, 0.976f, 1f));
            _lampB = NewLight("LampB", LightType.Spot, new Color(0.949f, 0.976f, 1f));
            foreach (Light l in new[] { _lampA, _lampB })
            {
                l.range = 460f;
                l.spotAngle = 0.9f * 2f * Mathf.Rad2Deg;
                l.innerSpotAngle = l.spotAngle * 0.35f;   // penumbra 0.65 → soft outer third
                l.shadows = LightShadows.None;            // two shadowed spots on a phone: no
            }

            Material glow = GlowMaterial();
            _poolA = MakeQuad("PoolA", glow, PoolMesh());
            _poolB = MakeQuad("PoolB", glow, PoolMesh());
            _beamA = MakeQuad("BeamA", glow, ConeMesh());
            _beamB = MakeQuad("BeamB", glow, ConeMesh());

            Apply();
        }

        private Light NewLight(string name, LightType type, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            Light l = go.AddComponent<Light>();
            l.type = type;
            l.color = color;
            l.shadows = LightShadows.None;
            return l;
        }

        private Transform MakeQuad(string name, Material mat, Mesh mesh)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return go.transform;
        }

        // ── per-frame ────────────────────────────────────────────────────────────

        /// <summary>
        /// Place the rig for this frame. <paramref name="groundY"/> is the seabed under the aim
        /// point, which the caller already raycasts for the flight model.
        /// </summary>
        public void Track(Vector3 pos, float yaw, float groundY)
        {
            var fwd = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
            var right = new Vector3(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw));

            _dive.transform.position = pos + new Vector3(0f, 2f, 0f);
            if (!_on) return;   // lamps/pools/beams are hidden anyway

            Vector3 aim = pos + fwd * DiveLightMath.Reach;
            float radius = DiveLightMath.PoolRadius(pos.y, groundY);
            float side = DiveLightMath.PoolOffset(radius);

            PlaceLamp(_lampA, _poolA, _beamA,
                      pos - right * DiveLightMath.LampSeparation - new Vector3(0f, 1f, 0f),
                      new Vector3(aim.x - right.x * side, groundY + 1.5f, aim.z - right.z * side),
                      radius);
            PlaceLamp(_lampB, _poolB, _beamB,
                      pos + right * DiveLightMath.LampSeparation - new Vector3(0f, 1f, 0f),
                      new Vector3(aim.x + right.x * side, groundY + 1.5f, aim.z + right.z * side),
                      radius);
        }

        private void PlaceLamp(Light lamp, Transform pool, Transform beam,
                               Vector3 from, Vector3 to, float radius)
        {
            lamp.transform.position = from;
            Vector3 dir = to - from;
            float dist = dir.magnitude;
            if (dist > 0.01f) lamp.transform.rotation = Quaternion.LookRotation(dir / dist, Vector3.up);

            // Light pool on the sand, a hair above it so it never z-fights.
            pool.position = new Vector3(to.x, to.y - 0.9f, to.z);
            pool.rotation = Quaternion.Euler(90f, 0f, 0f);   // face up
            pool.localScale = new Vector3(radius * 2f, radius * 2f, 1f);

            // Visible beam from the lamp to the pool.
            DiveLightMath.BeamScale(radius, dist, out float w, out float len);
            beam.position = from;
            if (dist > 0.01f) beam.rotation = Quaternion.LookRotation(dir / dist, Vector3.up);
            beam.localScale = new Vector3(w * 9f, w * 9f, len * 60f);
        }

        // ── on/off + atmosphere ──────────────────────────────────────────────────

        public void Toggle() => Set(!_on);

        public void Set(bool on)
        {
            _on = on;
            Apply();
        }

        private void Apply()
        {
            DiveLightMath.Atmosphere a = DiveLightMath.For(_on);

            _dive.intensity = a.DiveLight;
            _lampA.intensity = _on ? 2.6f : 0f;
            _lampB.intensity = _on ? 2.6f : 0f;
            _poolA.gameObject.SetActive(_on);
            _poolB.gameObject.SetActive(_on);
            _beamA.gameObject.SetActive(_on);
            _beamB.gameObject.SetActive(_on);

            EnsureSaved();
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(a.FogR, a.FogG, a.FogB);
            RenderSettings.fogStartDistance = a.FogNear;
            RenderSettings.fogEndDistance = a.FogFar;
            RenderSettings.ambientSkyColor = _savedSky * a.AmbientMul;
            RenderSettings.ambientEquatorColor = _savedEquator * a.AmbientMul;
            RenderSettings.ambientGroundColor = _savedGround * a.AmbientMul;
            if (_sun != null) _sun.intensity = _savedSun * a.AmbientMul;
            if (_fill != null) _fill.intensity = _savedFill * a.AmbientMul;

            Debug.Log($"[Tour] headlight={( _on ? "on" : "off")} fog={a.FogNear:F0}-{a.FogFar:F0} " +
                      $"ambient×{a.AmbientMul:F2} diveLight={a.DiveLight:F1}");
        }

        private void EnsureSaved()
        {
            if (_saved) return;
            _savedFog = RenderSettings.fog;
            _savedFogColor = RenderSettings.fogColor;
            _savedFogStart = RenderSettings.fogStartDistance;
            _savedFogEnd = RenderSettings.fogEndDistance;
            _savedSky = RenderSettings.ambientSkyColor;
            _savedEquator = RenderSettings.ambientEquatorColor;
            _savedGround = RenderSettings.ambientGroundColor;

            foreach (Light l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type != LightType.Directional) continue;
                if (l.gameObject.name == "FillLight") { _fill = l; _savedFill = l.intensity; }
                else if (_sun == null) { _sun = l; _savedSun = l.intensity; }
            }
            _saved = true;
        }

        /// <summary>Put the scene's own atmosphere back (called when the tour ends).</summary>
        public void RestoreScene()
        {
            if (!_saved) return;
            RenderSettings.fog = _savedFog;
            RenderSettings.fogColor = _savedFogColor;
            RenderSettings.fogStartDistance = _savedFogStart;
            RenderSettings.fogEndDistance = _savedFogEnd;
            RenderSettings.ambientSkyColor = _savedSky;
            RenderSettings.ambientEquatorColor = _savedEquator;
            RenderSettings.ambientGroundColor = _savedGround;
            if (_sun != null) _sun.intensity = _savedSun;
            if (_fill != null) _fill.intensity = _savedFill;
            Debug.Log("[Tour] scene atmosphere restored");
        }

        private void OnDestroy() => RestoreScene();

        // ── meshes / material ────────────────────────────────────────────────────

        private static Material _glow;
        /// <summary>
        /// Additive, unlit-ish glow on the proven DM_StandardTransparent base (same recipe as the
        /// god rays: blend overridden, ZWrite off, no new keyword). One material for pools and
        /// beams — they are the same light.
        /// </summary>
        private static Material GlowMaterial()
        {
            if (_glow != null) return _glow;
            Material src = Resources.Load<Material>("DM_StandardTransparent");
            var mat = src != null ? new Material(src) : new Material(Shader.Find("Standard"));
            if (mat.HasProperty("_Mode")) mat.SetFloat("_Mode", 3f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)(int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)(int)UnityEngine.Rendering.BlendMode.One);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0f);
            mat.renderQueue = 3090;
            mat.color = new Color(0.902f, 0.957f, 1f, 0.32f);   // 0xe6f4ff
            mat.mainTexture = GlowTexture();
            _glow = mat;
            return mat;
        }

        private static Texture2D _glowTex;
        /// <summary>Radial falloff (the web's discTex): opaque centre, nothing at the rim.</summary>
        private static Texture2D GlowTexture()
        {
            if (_glowTex != null) return _glowTex;
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                name = "DroneGlow",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = x / (float)(n - 1) * 2f - 1f;
                float v = y / (float)(n - 1) * 2f - 1f;
                float d = Mathf.Sqrt(u * u + v * v);
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (3f - 2f * a);              // smoothstep — soft rim
                a *= Mathf.Pow(y / (float)(n - 1), 1.7f); // web: near edge brighter than far
                byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f);
                px[y * n + x] = new Color32(b, b, b, b);
            }
            tex.SetPixels32(px);
            tex.Apply();
            _glowTex = tex;
            return tex;
        }

        private static Mesh _pool;
        /// <summary>Unit quad in the XY plane (rotated to lie flat by the caller).</summary>
        private static Mesh PoolMesh()
        {
            if (_pool != null) return _pool;
            var m = new Mesh { name = "DronePool" };
            m.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
            };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 };  // both faces
            var nrm = new Vector3[4];
            for (int i = 0; i < 4; i++) nrm[i] = new Vector3(0f, 0f, -1f);
            m.normals = nrm;
            m.RecalculateBounds();
            _pool = m;
            return m;
        }

        private static Mesh _cone;
        /// <summary>Open cone: tip at the origin, opening along +Z to unit radius at z=1.</summary>
        private static Mesh ConeMesh()
        {
            if (_cone != null) return _cone;
            const int seg = 18;
            var verts = new Vector3[seg * 4];
            var uvs = new Vector2[seg * 4];
            var tris = new int[seg * 12];
            int vi = 0, ti = 0;
            for (int j = 0; j < seg; j++)
            {
                float a0 = Mathf.PI * 2f * j / seg, a1 = Mathf.PI * 2f * (j + 1) / seg;
                int t0 = vi; verts[vi] = Vector3.zero; uvs[vi++] = new Vector2(0.5f, 1f);
                int t1 = vi; verts[vi] = Vector3.zero; uvs[vi++] = new Vector2(0.5f, 1f);
                int p0 = vi; verts[vi] = new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 1f); uvs[vi++] = new Vector2(0.5f, 0f);
                int p1 = vi; verts[vi] = new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 1f); uvs[vi++] = new Vector2(0.5f, 0f);
                tris[ti++] = t0; tris[ti++] = p0; tris[ti++] = p1;
                tris[ti++] = t0; tris[ti++] = p1; tris[ti++] = t1;
                tris[ti++] = t0; tris[ti++] = p1; tris[ti++] = p0;
                tris[ti++] = t0; tris[ti++] = t1; tris[ti++] = p1;
            }
            var m = new Mesh { name = "DroneBeam" };
            m.vertices = verts;
            m.uv = uvs;
            m.triangles = tris;
            var nrm = new Vector3[verts.Length];
            for (int i = 0; i < nrm.Length; i++) nrm[i] = new Vector3(0f, 0f, -1f);
            m.normals = nrm;
            m.RecalculateBounds();
            _cone = m;
            return m;
        }
    }
}
