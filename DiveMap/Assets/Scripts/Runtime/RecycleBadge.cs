using UnityEngine;

namespace DiveMap.Runtime
{
    /// <summary>
    /// E1 — the ♻️ tag that floats over every piece of litter (the web's <c>_trashSprite()</c>,
    /// builder.html:4112-4120). Without it a bottle on a sandy seabed is scenery; with it, it is
    /// something to go and get. It is the only thing telling a player the game has started.
    ///
    /// The arrows are DRAWN, not typed. The web's own comment says why: an emoji ♻️ renders as an
    /// empty box wherever the font is missing, which on Android is common. A 128×128 texture built
    /// at runtime always looks the same.
    ///
    /// It always draws on top (the web sets depthTest:false, renderOrder 999) — a tag you have to
    /// hunt for behind a rock is not a tag.
    /// </summary>
    public static class RecycleBadge
    {
        private const int Size = 128;
        private const float WorldSize = 4.6f;   // web: sprite.scale 4.6
        private const float Lift = 4.4f;        // web: sprite.position.y 4.4

        private static readonly Color Green = new Color(7f / 255f, 100f / 255f, 57f / 255f, 0.99f);

        private static Texture2D _tex;
        private static Material _mat;
        private static Mesh _quad;

        /// <summary>Hang a badge over <paramref name="parent"/> (one piece of litter).</summary>
        public static GameObject Attach(Transform parent)
        {
            if (parent == null) return null;

            var go = new GameObject("RecycleBadge");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, Lift, 0f);

            go.AddComponent<MeshFilter>().sharedMesh = Quad();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = BadgeMaterial();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.AddComponent<Billboard>();
            return go;
        }

        private static Mesh Quad()
        {
            if (_quad != null) return _quad;
            float h = WorldSize * 0.5f;
            _quad = new Mesh { name = "RecycleBadgeQuad" };
            _quad.vertices = new[]
            {
                new Vector3(-h, -h, 0f), new Vector3(h, -h, 0f),
                new Vector3(h, h, 0f),   new Vector3(-h, h, 0f),
            };
            _quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            // Both faces: Billboard turns it to the camera, but a piece of litter tumbling as it
            // sinks can still present the back for a frame.
            _quad.triangles = new[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 };
            _quad.RecalculateBounds();
            return _quad;
        }

        private static Material BadgeMaterial()
        {
            if (_mat != null) return _mat;
            Material src = Resources.Load<Material>("DM_StandardTransparent");
            _mat = src != null ? new Material(src) : new Material(Shader.Find("Standard"));
            if (_mat.HasProperty("_MainTex")) _mat.SetTexture("_MainTex", BadgeTexture());
            _mat.color = Color.white;
            // Unlit-ish: a tag that goes dark at depth cannot do its job.
            if (_mat.HasProperty("_Glossiness")) _mat.SetFloat("_Glossiness", 0f);
            if (_mat.HasProperty("_Metallic")) _mat.SetFloat("_Metallic", 0f);
            if (_mat.HasProperty("_ZWrite")) _mat.SetFloat("_ZWrite", 0f);
            // The web draws it with depthTest off; the closest safe equivalent here is the overlay
            // queue, which keeps it in front of the seabed and the wreck without disabling the
            // depth buffer for everything sharing the material.
            _mat.renderQueue = 4000;
            return _mat;
        }

        /// <summary>
        /// The web's canvas, redrawn with pixels: a green disc with a white rim, then three
        /// round-capped arcs each ending in a filled arrowhead.
        /// </summary>
        private static Texture2D BadgeTexture()
        {
            if (_tex != null) return _tex;

            _tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "RecycleBadge" };
            var px = new Color32[Size * Size];

            const float cx = 64f, cy = 64f;
            const float discR = 60f, rimW = 8f;       // web: arc r60, lineWidth 8
            const float armR = 34f, armW = 12f;       // web: _R 34, lineWidth 12

            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                // Canvas y grows downward; Texture2D y grows upward.
                float dy = (Size - fy) - cy, dx = fx - cx;
                float d = Mathf.Sqrt(dx * dx + dy * dy);

                Color c = new Color(0f, 0f, 0f, 0f);
                if (d <= discR + rimW * 0.5f)
                {
                    c = d >= discR - rimW * 0.5f ? Color.white : Green;

                    // The three arrows sit on top of the disc.
                    if (d < discR - rimW * 0.5f && ArrowAt(dx, dy, armR, armW))
                        c = Color.white;
                }
                px[y * Size + x] = c;
            }

            _tex.SetPixels32(px);
            _tex.Apply(false, false);
            _tex.wrapMode = TextureWrapMode.Clamp;
            _tex.filterMode = FilterMode.Bilinear;
            return _tex;
        }

        /// <summary>Is (dx,dy) inside one of the three recycling arrows?</summary>
        private static bool ArrowAt(float dx, float dy, float armR, float armW)
        {
            float ang = Mathf.Atan2(dy, dx);
            float r = Mathf.Sqrt(dx * dx + dy * dy);

            for (int k = 0; k < 3; k++)
            {
                float baseA = k * 2.0944f - 1.15f;   // web: k*2.0944 - 1.15
                const float span = 1.35f;

                // The arc body: within armW/2 of the ring, and inside the angular span.
                if (Mathf.Abs(r - armR) <= armW * 0.5f)
                {
                    float rel = Mathf.Repeat(ang - baseA, Mathf.PI * 2f);
                    if (rel <= span) return true;
                    // Round caps at both ends (the web sets lineCap 'round').
                    if (NearPoint(dx, dy, baseA, armR, armW * 0.5f)) return true;
                }

                // The arrowhead: a filled triangle at the end of the arc, exactly the web's
                // tangent/normal construction (builder.html:4117-4118).
                float ea = baseA + span;
                float ex = Mathf.Cos(ea) * armR, ey = Mathf.Sin(ea) * armR;
                float tx = -Mathf.Sin(ea), ty = Mathf.Cos(ea);
                float nx = Mathf.Cos(ea), ny = Mathf.Sin(ea);
                if (InTriangle(dx, dy,
                               ex + tx * 16f, ey + ty * 16f,
                               ex + nx * 13f, ey + ny * 13f,
                               ex - nx * 13f, ey - ny * 13f))
                    return true;
            }
            return false;
        }

        private static bool NearPoint(float dx, float dy, float ang, float r, float rad)
        {
            float px = Mathf.Cos(ang) * r, py = Mathf.Sin(ang) * r;
            float ddx = dx - px, ddy = dy - py;
            return ddx * ddx + ddy * ddy <= rad * rad;
        }

        private static bool InTriangle(float px, float py,
                                       float ax, float ay, float bx, float by, float cx2, float cy2)
        {
            float d1 = Side(px, py, ax, ay, bx, by);
            float d2 = Side(px, py, bx, by, cx2, cy2);
            float d3 = Side(px, py, cx2, cy2, ax, ay);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0;
            bool pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        private static float Side(float px, float py, float ax, float ay, float bx, float by)
            => (px - bx) * (ay - by) - (ax - bx) * (py - by);
    }
}
