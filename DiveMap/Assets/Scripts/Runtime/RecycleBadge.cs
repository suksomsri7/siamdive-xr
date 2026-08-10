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

        /// <summary>
        /// Hang a badge over <paramref name="parent"/> (one piece of litter).
        ///
        /// 🔴 The giant pixelated ♻️ that filled the sky (round-3 device report). The badge is
        /// 4.6 world units across and it was inheriting the LITTER's scale, which is not 1 and is
        /// not bounded:
        /// <code>
        ///   TrashGameSystem.LoadTemplates: holder.transform.localScale = Vector3.one * (want / max)
        /// </code>
        /// — every trash model is normalised so its longest side becomes the wanted size, and the
        /// raw GLBs are authored small, so <c>want/max</c> is a LARGE multiplier. A bottle whose
        /// GLB measures 0.05 u gets ×40, and its badge came out 184 units wide, 176 units up, drawn
        /// unlit and depth-independent: a green wall across the sky. It looked pixelated for the
        /// same reason — a 128² texture stretched over most of the screen — so the blur was the
        /// symptom, not the cause. The primitive fallback shapes scale 1.3-3.4, which is why this
        /// only became grotesque once the real models finished downloading.
        ///
        /// The web has no such coupling: <c>_trashSprite()</c> returns a Sprite with
        /// <c>scale.set(4.6,4.6,1)</c> added to a group that is added straight to the scene with no
        /// scale of its own (builder.html:4137-4140). 4.6 means 4.6 there, always.
        ///
        /// So the badge cancels its parent's scale, per axis. Exact rather than approximate: a
        /// badged piece is never ROTATED (only coins spin, and coins get no badge — see
        /// TrashGameSystem), and without a rotation between the two transforms scale composes
        /// component-wise. The lift is divided too, so 4.4 is 4.4 world units above the litter
        /// whatever the piece's own scale does.
        /// </summary>
        public static GameObject Attach(Transform parent)
        {
            if (parent == null) return null;

            var go = new GameObject("RecycleBadge");
            go.transform.SetParent(parent, false);

            Vector3 ps = parent.lossyScale;
            // A zero or near-zero axis would make the badge infinite. Fall back to 1 (= no
            // compensation) rather than to a number nobody can see past.
            float sx = Mathf.Abs(ps.x) > 1e-4f ? ps.x : 1f;
            float sy = Mathf.Abs(ps.y) > 1e-4f ? ps.y : 1f;
            float sz = Mathf.Abs(ps.z) > 1e-4f ? ps.z : 1f;
            go.transform.localScale = new Vector3(1f / sx, 1f / sy, 1f / sz);
            go.transform.localPosition = new Vector3(0f, Lift / sy, 0f);

            go.AddComponent<MeshFilter>().sharedMesh = Quad();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = BadgeMaterial();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.AddComponent<Billboard>();
            return go;
        }

        /// <summary>
        /// A DISC, not a quad. The badge is round, so cutting the circle out of the mesh rather
        /// than out of the alpha channel means the material never needs transparency — which in
        /// turn means it can be the unlit glTF material without touching a blend mode or enabling
        /// a shader keyword (both of which this project has been bitten by: keywords get stripped
        /// from the player and the material comes back magenta).
        /// </summary>
        private static Mesh Quad()
        {
            if (_quad != null) return _quad;
            const int seg = 40;
            float r = WorldSize * 0.5f;

            var verts = new Vector3[seg + 1];
            var uvs = new Vector2[seg + 1];
            verts[0] = Vector3.zero;
            uvs[0] = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                verts[i + 1] = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f);
                uvs[i + 1] = new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f);
            }

            // Both faces: Billboard turns it to the camera, but a piece of litter tumbling as it
            // sinks can still present the back for a frame.
            var tris = new int[seg * 6];
            int t = 0;
            for (int i = 0; i < seg; i++)
            {
                int b = 1 + i, c = 1 + (i + 1) % seg;
                tris[t++] = 0; tris[t++] = b; tris[t++] = c;
                tris[t++] = 0; tris[t++] = c; tris[t++] = b;
            }

            _quad = new Mesh { name = "RecycleBadgeDisc" };
            _quad.vertices = verts;
            _quad.uv = uvs;
            _quad.triangles = tris;
            var n = new Vector3[verts.Length];
            for (int i = 0; i < n.Length; i++) n[i] = Vector3.back;
            _quad.normals = n;
            _quad.RecalculateBounds();
            return _quad;
        }

        private static Material BadgeMaterial()
        {
            if (_mat != null) return _mat;

            // UNLIT. The first QC round used DM_StandardTransparent and the badge came back a dark
            // grey disc at depth — a gameplay marker that fades with the fog is not a marker. The
            // glTF unlit material is already in the build (every model uses this family), so no
            // new shader can be stripped.
            Material src = Resources.Load<Material>("DM_GltfUnlit")
                        ?? Resources.Load<Material>("DM_Standard");
            _mat = src != null ? new Material(src) : new Material(Shader.Find("Standard"));

            Texture2D tex = BadgeTexture();
            if (_mat.HasProperty("baseColorTexture")) _mat.SetTexture("baseColorTexture", tex);
            else if (_mat.HasProperty("_MainTex")) _mat.SetTexture("_MainTex", tex);

            if (_mat.HasProperty("baseColorFactor")) _mat.SetColor("baseColorFactor", Color.white);
            _mat.color = Color.white;
            if (_mat.HasProperty("_Glossiness")) _mat.SetFloat("_Glossiness", 0f);
            if (_mat.HasProperty("_Metallic")) _mat.SetFloat("_Metallic", 0f);
            return _mat;
        }

        /// <summary>
        /// The web's canvas, redrawn with pixels: a green disc with a white rim, then three
        /// round-capped arcs each ending in a filled arrowhead.
        /// </summary>
        private static Texture2D BadgeTexture()
        {
            if (_tex != null) return _tex;

            // Mipmapped (round 3). At its correct 4.6 units the badge is small on screen and a
            // 128² texture with no mip chain aliases into sparkling white speckle on the white
            // arrows as the drone moves — the opposite failure to the one that was reported and
            // just as distracting. Size stays 128 to keep every drawing constant below EXACTLY the
            // web's canvas numbers (cx 64, r 60, _R 34, arrow 16/13): the pixelation the user saw
            // was the scale bug, and re-deriving the artwork at another resolution would risk the
            // shape to fix something already fixed.
            _tex = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { name = "RecycleBadge" };
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

                // Outside the disc is never drawn (the MESH ends there), so the corner colour only
                // matters for the bilinear filter at the rim — green keeps it from fringing black.
                Color c = Green;
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
            _tex.Apply(true, false);   // true = build the mip chain from the pixels just written
            _tex.wrapMode = TextureWrapMode.Clamp;
            // Trilinear, not Bilinear: bilinear smooths WITHIN one mip level and does nothing about
            // the jump between levels, which on a badge that changes size continuously as the drone
            // approaches is a visible pop.
            _tex.filterMode = FilterMode.Trilinear;
            _tex.anisoLevel = 4;
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
