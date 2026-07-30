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
        private const float Opacity = 0.11f;     // r1 used 0.30 and read as translucent solids
        private static readonly Color Tint = new Color(0.918f, 0.965f, 1f); // 0xeaf6ff

        private Transform[] _beams;
        private Vector3 _dir;                    // sun direction, shared by every shaft
        private Camera _cam;

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
            _dir = new Vector3(d.X, d.Y, d.Z);
            _cam = Camera.main;

            Mesh quad = BeamMesh();
            Material mat = BeamMaterial();
            if (mat == null)
            {
                Debug.LogWarning("[Scene] godrays DISABLED — no transparent base material");
                return;
            }

            _beams = new Transform[BeamCount];
            // Wide and faint beats narrow and bright: a soft shaft has to be broad enough that
            // its feathered edges have room to fade.
            float baseWidth = Mathf.Clamp(spread * 0.16f, 12f, 48f);

            for (int i = 0; i < BeamCount; i++)
            {
                GodRayMath.Vec2 off = GodRayMath.BeamOffset(i, BeamCount, spread, 7);
                float widthMul = GodRayMath.BeamWidthMul(i, 7);

                var beam = new GameObject($"Beam{i}");
                beam.transform.SetParent(transform, false);
                // Start AT the surface and shine down-sun.
                beam.transform.position = new Vector3(center.x + off.X, waterLevel, center.z + off.Z);
                // Quad is authored across local X and along local +Z (top end at z=0).
                beam.transform.localScale = new Vector3(baseWidth * widthMul, 1f, length);

                var mf = beam.AddComponent<MeshFilter>();
                var mr = beam.AddComponent<MeshRenderer>();
                mf.sharedMesh = quad;
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

                _beams[i] = beam.transform;
            }

            Orient();   // face the camera before the first frame is drawn
            Debug.Log($"[Scene] godrays beams={BeamCount} spread={spread:F0} len={length:F0} " +
                      $"dir=({_dir.x:F3},{_dir.y:F3},{_dir.z:F3}) width={baseWidth:F1} " +
                      $"opacity={Opacity:F2} soft=billboard");
        }

        private void LateUpdate() => Orient();

        /// <summary>
        /// Each shaft keeps its axis along the sun but spins about that axis to face the camera
        /// — the standard light-shaft billboard. A fixed cone showed its silhouette from the
        /// side; a camera-facing feathered quad never does, and it costs 2 triangles.
        /// </summary>
        private void Orient()
        {
            if (_beams == null) return;
            if (_cam == null) _cam = Camera.main;
            Vector3 camPos = _cam != null ? _cam.transform.position : Vector3.zero;
            float t = Time.time;

            for (int i = 0; i < _beams.Length; i++)
            {
                Transform b = _beams[i];
                if (b == null) continue;

                // Gentle sway of the shaft's own direction (±2°), never a spin about its length.
                Quaternion sway = Quaternion.Euler(GodRayMath.SwayDeg(i, t),
                                                   GodRayMath.SwayDeg(i + 5, t * 0.8f), 0f);
                Vector3 dir = sway * _dir;

                // The quad lies in local XZ, so its normal is local Y → aim that at the camera.
                Vector3 toCam = camPos - b.position;
                Vector3 up = toCam - dir * Vector3.Dot(toCam, dir);   // strip the along-shaft part
                if (up.sqrMagnitude < 1e-6f)
                {
                    // Camera looking straight down the shaft: any perpendicular will do.
                    up = Vector3.Cross(dir, Vector3.right);
                    if (up.sqrMagnitude < 1e-6f) up = Vector3.Cross(dir, Vector3.forward);
                }
                b.rotation = Quaternion.LookRotation(dir, up);
            }
        }

        // ── Assets ───────────────────────────────────────────────────────────────

        /// <summary>
        /// One unit quad: across local X (−0.5…0.5), along local +Z (0 at the surface end,
        /// 1 at the deep end), so a beam's width and length are just transform scale. Wound
        /// both ways with their own vertices and explicit ±Y normals — a shared-vertex
        /// two-sided quad has its normals averaged to zero and lights black (the fish-fin
        /// lesson), and a shaft has to read from either side of the billboard.
        /// </summary>
        private static Mesh _quad;
        private static Mesh BeamMesh()
        {
            if (_quad != null) return _quad;

            var verts = new Vector3[8];
            var uvs = new Vector2[8];
            var norms = new Vector3[8];
            for (int side = 0; side < 2; side++)
            {
                int o = side * 4;
                verts[o + 0] = new Vector3(-0.5f, 0f, 0f);
                verts[o + 1] = new Vector3( 0.5f, 0f, 0f);
                verts[o + 2] = new Vector3( 0.5f, 0f, 1f);
                verts[o + 3] = new Vector3(-0.5f, 0f, 1f);
                uvs[o + 0] = new Vector2(0f, 1f);   // v=1 = the surface end (bright)
                uvs[o + 1] = new Vector2(1f, 1f);
                uvs[o + 2] = new Vector2(1f, 0f);   // v=0 = the deep end (gone)
                uvs[o + 3] = new Vector2(0f, 0f);
                Vector3 n = side == 0 ? Vector3.up : Vector3.down;
                for (int k = 0; k < 4; k++) norms[o + k] = n;
            }

            var m = new Mesh { name = "GodRayBeam" };
            m.vertices = verts;
            m.uv = uvs;
            m.normals = norms;
            m.triangles = new[]
            {
                0, 2, 1, 0, 3, 2,   // +Y face
                4, 5, 6, 4, 6, 7,   // −Y face
            };
            m.RecalculateBounds();
            _quad = m;
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
        /// The shaft's whole falloff in one 64×64 map: the web's length ramp (builder.html
        /// :3668) × a fade where it meets the surface × a squared bell across the width. Every
        /// edge of the texture is 0, which is what removes the hard silhouette. Written into
        /// RGB as well as alpha, so the shaft still fades if only the colour channel reaches
        /// the framebuffer.
        /// </summary>
        private static Texture2D RampTexture()
        {
            if (_ramp != null) return _ramp;
            const int w = 64, h = 64;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = "GodRayBeamFalloff",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = (float)y / (h - 1);           // v=0 deep end … v=1 surface end
                for (int x = 0; x < w; x++)
                {
                    float u = (float)x / (w - 1);
                    float a = GodRayMath.BeamAlpha(u, v);
                    byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255);
                    px[y * w + x] = new Color32(b, b, b, b);
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            _ramp = tex;
            return tex;
        }
    }
}
