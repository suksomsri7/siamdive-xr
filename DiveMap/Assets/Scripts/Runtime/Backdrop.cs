using DiveMap.Core;
using UnityEngine;

namespace DiveMap.Runtime
{
    /// <summary>
    /// WO-XR-04.2 — the vertical gradient backdrop (bright surface haze above, deep blue
    /// below). Per Fable's survey this, not fog, is what makes the web read as "deep";
    /// Unity has been clearing to one flat blue.
    ///
    /// The web assigns an 8×256 canvas gradient to <c>scene.background</c> (builder.html
    /// 662-667), which three.js stretches across the viewport — a SCREEN-SPACE gradient,
    /// not a sky dome. A quad parented to the camera reproduces exactly that: its top edge
    /// is always the top of the screen, so the gradient never swings when the orbit camera
    /// tilts. It is drawn in the Background queue with ZWrite off, so every opaque and
    /// transparent object in the scene paints over it regardless of distance.
    ///
    /// Degrades safely: the only shader we can count on for an UNLIT full-screen fill is
    /// the glTFast unlit shader kept alive by <c>Resources/DM_GltfUnlit.mat</c>, and its
    /// texture property name differs per package version — so we try a chain of names and,
    /// if none takes the texture, delete the quad and leave the camera's own solid
    /// background colour (today's look) rather than paint a wrong flat rectangle.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Backdrop : MonoBehaviour
    {
        private const int TexWidth = 8, TexHeight = 256;
        /// <summary>
        /// Fraction of the far clip plane the quad sits at. Deliberately almost AT the far
        /// plane: we ask the unlit material for ZWrite-off but glTFast's shader may not expose
        /// the property, and a depth-writing quad in front of the map would clip the far half
        /// of the seabed. Anything past 0.95×far is being clipped by the camera anyway.
        /// </summary>

        private Camera _cam;
        private Transform _quad;
        private float _lastAspect, _lastFov, _lastDist;

        /// <summary>
        /// Attach (idempotently) to <paramref name="cam"/>. Returns the component, or null
        /// when no unlit material could carry the gradient.
        /// </summary>
        public static Backdrop Attach(Camera cam)
        {
            if (cam == null) return null;
            Backdrop existing = cam.GetComponent<Backdrop>();
            if (existing != null) return existing;

            var b = cam.gameObject.AddComponent<Backdrop>();
            if (!b.Build(cam))
            {
                Destroy(b);
                return null;
            }
            return b;
        }

        /// <summary>
        /// 🔴 WO-E3 — the half of the fix that lives in the picture rather than in RenderSettings.
        ///
        /// This gradient is what fills most of the frame, and until now it was baked ONCE at the
        /// surface and never changed again. <see cref="DepthLight"/> meanwhile dimmed the light on
        /// every object by up to two thirds. So at 52 m a shark was lit by a third of the light
        /// while the water behind it was still painted at full surface brightness — the exact
        /// inversion the user reported ("ฉลามกลายเป็นเงาแบน … ขณะที่ฉากหลัง/หมอกยังสว่าง"). No
        /// amount of tuning the lights could fix a background that was not in the same
        /// multiplication.
        ///
        /// Now the ramp is re-baked with the SAME attenuation vector the ambient is scaled by, so
        /// the subject-to-background ratio no longer depends on depth at all. Costs one 8×256
        /// upload per bucket of depth crossed (see <see cref="DepthEpsilon"/>) — a couple of
        /// thousand texels, far below the per-frame noise, and nothing at all while the camera
        /// holds still.
        /// </summary>
        /// <param name="depthUnits">Camera depth below the water surface, in world units.</param>
        public static void SetDepth(float depthUnits)
        {
            if (float.IsNaN(depthUnits) || float.IsInfinity(depthUnits)) depthUnits = 0f;
            if (Mathf.Abs(depthUnits - _bakedDepth) < DepthEpsilon) return;
            _bakedDepth = depthUnits;
            Rebake();
        }

        /// <summary>Surface light, unattenuated — the daylight/above-water case.</summary>
        public static void ClearDepth() => SetDepth(0f);

        /// <summary>
        /// How far the camera has to move, in world units, before the texture is re-baked. Six
        /// units is one metre and a metre cannot move a gradient byte in the shallows, let alone
        /// where the curve has flattened — so anything finer is work that provably cannot alter a
        /// pixel.
        /// </summary>
        private const float DepthEpsilon = 6f;

        private static float _bakedDepth;

        private bool Build(Camera cam)
        {
            _cam = cam;

            Material mat = MakeGradientMaterial(GradientTexture(), out string via);
            if (mat == null)
            {
                Debug.LogWarning("[Scene] backdrop DISABLED — no unlit texture property found " +
                                 "(keeping camera.backgroundColor)");
                return false;
            }

            var go = new GameObject("Backdrop");
            go.transform.SetParent(cam.transform, false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = QuadMesh();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _quad = go.transform;

            Fit();
            Debug.Log($"[Scene] backdrop ready via={via} stops=4 tex={TexWidth}x{TexHeight}");
            return true;
        }

        /// <summary>Hide the gradient (daylight mode uses a flat sky, like the web).</summary>
        public void SetVisible(bool visible)
        {
            if (_quad != null) _quad.gameObject.SetActive(visible);
        }

        private void LateUpdate()
        {
            if (_cam == null || _quad == null) return;
            Fit();
        }

        /// <summary>Resize the quad to exactly fill the frustum at its distance (FOV, aspect
        /// and far clip all change at runtime — device rotation, map-size zoom retune).</summary>
        private void Fit()
        {
            float far = Mathf.Max(1f, _cam.farClipPlane);
            // 🔴 CLOSE to the camera, not out at the far plane.
            //
            // The quad used to sit at 95% of the far plane — about 8,550 units out — and the
            // underwater fog reaches full strength at 280. So the fog painted the ENTIRE backdrop
            // one flat colour and the gradient behind it was never visible: "there is no grading at
            // all, it is all one colour", reported after two rounds of tuning stops that could not
            // possibly show up.
            //
            // Distance does not matter for what this draws: it is a screen-space fill, sized to the
            // frustum at whatever distance it sits, in the Background queue with ZWrite off, so
            // everything in the scene paints over it either way. Putting it just past the near
            // plane means fog contributes ~nothing and the ramp is seen as authored.
            float dist = _cam.nearClipPlane * 4f + 0.01f;
            float aspect = _cam.aspect;
            float fov = _cam.fieldOfView;
            if (Mathf.Approximately(aspect, _lastAspect) &&
                Mathf.Approximately(fov, _lastFov) &&
                Mathf.Approximately(dist, _lastDist)) return;

            _lastAspect = aspect; _lastFov = fov; _lastDist = dist;

            float h = 2f * dist * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            float w = h * Mathf.Max(0.01f, aspect);
            // A hair oversized so no seam shows at the screen edge.
            _quad.localPosition = new Vector3(0f, 0f, dist);
            _quad.localRotation = Quaternion.identity;
            _quad.localScale = new Vector3(w * 1.02f, h * 1.02f, 1f);
        }

        // ── Assets ───────────────────────────────────────────────────────────────

        private static Mesh _quadMesh;
        private static Mesh QuadMesh()
        {
            if (_quadMesh != null) return _quadMesh;
            var m = new Mesh { name = "BackdropQuad" };
            m.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3( 0.5f, -0.5f, 0f),
                new Vector3( 0.5f,  0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f),
            };
            m.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 1f), new Vector2(0f, 1f),
            };
            // Facing the camera (-Z is the camera's viewing direction onto this quad).
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            var n = new Vector3[4];
            for (int i = 0; i < 4; i++) n[i] = new Vector3(0f, 0f, -1f);
            m.normals = n;
            m.RecalculateBounds();
            _quadMesh = m;
            return m;
        }

        private static Texture2D _gradTex;
        /// <summary>The web's 8×256 vertical gradient (v=0 at the TOP of the screen).</summary>
        public static Texture2D GradientTexture()
        {
            if (_gradTex != null) return _gradTex;

            _gradTex = new Texture2D(TexWidth, TexHeight, TextureFormat.RGBA32, false, false)
            {
                name = "BackdropGradient",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            Rebake();
            return _gradTex;
        }

        /// <summary>
        /// Write the ramp, as seen from the current depth, into the existing texture.
        ///
        /// 🔎 THE COLOUR IS DECIDED IN CORE, NOT HERE. <see cref="WaterFog.BackdropAt"/> is the same
        /// function the fog and the ambient floor are computed from, so what is written here cannot
        /// disagree with them — including the fact that the dimming happens in LIGHT rather than in
        /// the numbers (<see cref="ToneMap.ScaleLight"/>). All this does is turn an authored sRGB
        /// value into the byte an sRGB texture holds, which is a multiplication by 255.
        ///
        /// 🔎 And it is done on the TEXTURE rather than as a colour on the material. A texture byte
        /// means the same thing in both colour spaces; a Color handed to Material.SetColor at
        /// runtime does not — whether the engine gamma-decodes it is version- and
        /// property-dependent, and being wrong about it would silently square the attenuation. Two
        /// thousand texels every metre or so is a cheap price for not having to be right about that.
        /// </summary>
        private static void Rebake()
        {
            if (_gradTex == null) return;
            var px = new Color32[TexWidth * TexHeight];
            for (int y = 0; y < TexHeight; y++)
            {
                // Unity's v=0 is the BOTTOM row, the web's gradient stop 0 is the TOP.
                float v = 1f - (float)y / (TexHeight - 1);
                SeabedGeom.Rgb c = WaterFog.BackdropAt(v, _bakedDepth);
                var c32 = new Color32(Byte255(c.R), Byte255(c.G), Byte255(c.B), 255);
                for (int x = 0; x < TexWidth; x++) px[y * TexWidth + x] = c32;
            }
            _gradTex.SetPixels32(px);
            _gradTex.Apply(false, false);
        }

        private static byte Byte255(float srgb)
            => (byte)Mathf.Clamp(Mathf.RoundToInt(srgb * 255f), 0, 255);

        /// <summary>
        /// An unlit material carrying <paramref name="tex"/>. DM_GltfUnlit lives in
        /// Resources purely so glTFast's unlit shader survives build stripping; which
        /// texture property it exposes depends on the package version, so try the known
        /// names and then scan the shader. Returns null if the texture cannot be set —
        /// callers must then skip the backdrop entirely.
        /// </summary>
        private static Material MakeGradientMaterial(Texture tex, out string via)
        {
            via = "none";
            Material src = Resources.Load<Material>("DM_GltfUnlit");
            if (src == null) return null;

            var mat = new Material(src);
            if (mat.shader == null) return null;

            string[] names = { "_MainTex", "baseColorTexture", "_BaseMap", "_BaseColorTexture" };
            foreach (string n in names)
            {
                if (!mat.HasProperty(n)) continue;
                mat.SetTexture(n, tex);
                via = n;
                break;
            }
            if (via == "none")
            {
                Shader sh = mat.shader;
                int count = sh.GetPropertyCount();
                for (int i = 0; i < count && via == "none"; i++)
                {
                    if (sh.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Texture) continue;
                    string n = sh.GetPropertyName(i);
                    string low = n.ToLowerInvariant();
                    if (low.Contains("normal") || low.Contains("occl") || low.Contains("emis")) continue;
                    mat.SetTexture(n, tex);
                    via = n;
                }
            }
            if (via == "none") return null;

            // White base factor so the texture is the colour, and no depth write so the
            // whole scene paints over it.
            if (mat.HasProperty("baseColorFactor")) mat.SetColor("baseColorFactor", Color.white);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Background; // 1000
            return mat;
        }
    }
}
