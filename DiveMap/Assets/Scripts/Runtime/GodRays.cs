using DiveMap.Core;
using UnityEngine;

namespace DiveMap.Runtime
{
    /// <summary>
    /// WO-XR-04.3 — sun shafts falling from the surface. The web has none (this is the
    /// "better than the web" half of DESIGN_DOC §246-248), so the shape and the alpha ramp
    /// are lifted from its drone headlight (builder.html 3667-3670): an open cone, bright at
    /// the tip, dissolving at the wide end.
    ///
    /// Each shaft is one open cone parented to a rig at the water surface, all parallel to
    /// the sun (<see cref="GodRayMath.SunDirection"/>) — beams and shadows must agree or the
    /// picture reads as two suns. Positions/widths come from <see cref="GodRayMath"/> so two
    /// QC screenshots of the same map are comparable.
    ///
    /// Rendering rules that keep the build safe: one clone of Resources/DM_StandardTransparent
    /// (never a runtime Shader.Find), blend overridden to SrcAlpha+One with ZWrite off, no new
    /// shader keyword, and the ramp baked into the texture's RGB **and** alpha so the shafts
    /// still fade even if the alpha channel is ignored by the blend path.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GodRays : MonoBehaviour
    {
        private const int BeamCount = 10;        // plan: 8-12
        private const int ConeSegments = 24;     // ConeGeometry(7.5, 60, 24, 1, open)
        private const float Opacity = 0.30f;
        private static readonly Color Tint = new Color(0.918f, 0.965f, 1f); // 0xeaf6ff

        private Transform[] _beams;
        private Quaternion[] _rest;

        /// <summary>
        /// Build the shafts under <paramref name="parent"/>. <paramref name="center"/> is the
        /// frame centre (the wreck), <paramref name="spread"/> the radius they scatter over,
        /// <paramref name="waterLevel"/> the surface Y they start from and
        /// <paramref name="length"/> how far down they reach.
        /// </summary>
        public static GodRays Attach(Transform parent, Vector3 center, float spread,
                                     float waterLevel, float length)
        {
            var go = new GameObject("GodRays");
            if (parent != null) go.transform.SetParent(parent, false);
            var gr = go.AddComponent<GodRays>();
            gr.Build(center, spread, waterLevel, length);
            return gr;
        }

        private void Build(Vector3 center, float spread, float waterLevel, float length)
        {
            GodRayMath.Vec3 d = GodRayMath.SunDirection();
            var dir = new Vector3(d.X, d.Y, d.Z);
            Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);

            Mesh cone = ConeMesh(ConeSegments);
            Material mat = BeamMaterial();
            if (mat == null)
            {
                Debug.LogWarning("[Scene] godrays DISABLED — no transparent base material");
                return;
            }

            _beams = new Transform[BeamCount];
            _rest = new Quaternion[BeamCount];
            float baseWidth = Mathf.Max(6f, spread * 0.075f);   // ≈8-16 u on the demo map

            for (int i = 0; i < BeamCount; i++)
            {
                GodRayMath.Vec2 off = GodRayMath.BeamOffset(i, BeamCount, spread, 7);
                float widthMul = GodRayMath.BeamWidthMul(i, 7);

                var beam = new GameObject($"Beam{i}");
                beam.transform.SetParent(transform, false);
                // Start AT the surface and shine down-sun.
                beam.transform.position = new Vector3(center.x + off.X, waterLevel, center.z + off.Z);
                beam.transform.rotation = rot;
                // Cone is authored tip-at-origin, opening along +Z over 1 unit, radius 1.
                beam.transform.localScale = new Vector3(baseWidth * widthMul, baseWidth * widthMul, length);

                var mf = beam.AddComponent<MeshFilter>();
                var mr = beam.AddComponent<MeshRenderer>();
                mf.sharedMesh = cone;
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

                _beams[i] = beam.transform;
                _rest[i] = rot;
            }

            Debug.Log($"[Scene] godrays beams={BeamCount} spread={spread:F0} len={length:F0} " +
                      $"dir=({dir.x:F3},{dir.y:F3},{dir.z:F3}) width={baseWidth:F1} opacity={Opacity:F2}");
        }

        private void Update()
        {
            if (_beams == null) return;
            float t = Time.time;
            for (int i = 0; i < _beams.Length; i++)
            {
                if (_beams[i] == null) continue;
                // Gentle sway about the two axes across the beam, never about its own length
                // (that would just spin the cone) — ±2° as planned.
                float s = GodRayMath.SwayDeg(i, t);
                float s2 = GodRayMath.SwayDeg(i + 5, t * 0.8f);
                _beams[i].localRotation = _rest[i] * Quaternion.Euler(s, s2, 0f);
            }
        }

        // ── Assets ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Open cone: tip at the origin, opening along +Z, unit length and unit radius so one
        /// mesh serves every beam through its transform scale. Each segment gets its own tip
        /// vertex (shared tips would smear the UV ramp and cancel the normals — the fin-black
        /// lesson) and is wound both ways so the shaft is visible from inside and out.
        /// </summary>
        private static Mesh _cone;
        private static Mesh ConeMesh(int seg)
        {
            if (_cone != null) return _cone;

            var verts = new Vector3[seg * 4];
            var uvs = new Vector2[seg * 4];
            var tris = new int[seg * 12];
            int vi = 0, ti = 0;

            for (int j = 0; j < seg; j++)
            {
                float a0 = Mathf.PI * 2f * j / seg;
                float a1 = Mathf.PI * 2f * (j + 1) / seg;
                Vector3 b0 = new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 1f);
                Vector3 b1 = new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 1f);
                float u0 = (float)j / seg, u1 = (float)(j + 1) / seg;

                int t0 = vi;
                verts[vi] = Vector3.zero; uvs[vi++] = new Vector2(u0, 1f); // tip (bright)
                int t1 = vi;
                verts[vi] = Vector3.zero; uvs[vi++] = new Vector2(u1, 1f);
                int p0 = vi;
                verts[vi] = b0; uvs[vi++] = new Vector2(u0, 0f);           // open end (clear)
                int p1 = vi;
                verts[vi] = b1; uvs[vi++] = new Vector2(u1, 0f);

                tris[ti++] = t0; tris[ti++] = p0; tris[ti++] = p1;
                tris[ti++] = t0; tris[ti++] = p1; tris[ti++] = t1;
                // Reverse winding: additive, so a doubled face just reads as the shaft's core.
                tris[ti++] = t0; tris[ti++] = p1; tris[ti++] = p0;
                tris[ti++] = t0; tris[ti++] = t1; tris[ti++] = p1;
            }

            var m = new Mesh { name = "GodRayCone" };
            m.vertices = verts;
            m.uv = uvs;
            m.triangles = tris;
            // Constant normals pointing back up the shaft: with additive blending the shading
            // only needs to be stable, and per-face normals on a two-sided cone cancel to zero.
            var norms = new Vector3[verts.Length];
            for (int i = 0; i < norms.Length; i++) norms[i] = new Vector3(0f, 0f, -1f);
            m.normals = norms;
            m.RecalculateBounds();
            _cone = m;
            return m;
        }

        private static Material _beamMat;
        private static Material BeamMaterial()
        {
            if (_beamMat != null) return _beamMat;

            Material src = Resources.Load<Material>("DM_StandardTransparent");
            var mat = src != null ? new Material(src) : null;
            if (mat == null || mat.shader == null) return null;

            // Same transparent setup WaterMaterial uses (proven on device), then blend
            // overridden to additive. No keyword is enabled that the build might not include.
            if (mat.HasProperty("_Mode")) mat.SetFloat("_Mode", 3f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)(int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)(int)UnityEngine.Rendering.BlendMode.One);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            mat.renderQueue = 3100;   // after the water disc (3000), before nothing else

            var c = Tint; c.a = Opacity;
            mat.color = c;
            mat.mainTexture = RampTexture();
            _beamMat = mat;
            return mat;
        }

        private static Texture2D _ramp;
        /// <summary>
        /// The web's beam gradient (builder.html:3668) as an 8×64 strip: v=1 is the bright
        /// tip, v=0 the invisible open end. The ramp is written into RGB as well as alpha, so
        /// the shaft fades even where only the colour channel reaches the framebuffer.
        /// </summary>
        private static Texture2D RampTexture()
        {
            if (_ramp != null) return _ramp;
            const int w = 8, h = 64;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = "GodRayRamp",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float t = (float)y / (h - 1);           // v=0 open end … v=1 tip
                float a = GodRayMath.RampAlpha(t);
                byte v = (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255);
                for (int x = 0; x < w; x++) px[y * w + x] = new Color32(v, v, v, v);
            }
            tex.SetPixels32(px);
            tex.Apply();
            _ramp = tex;
            return tex;
        }
    }
}
