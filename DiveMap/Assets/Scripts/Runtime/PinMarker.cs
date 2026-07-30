using System.Collections.Generic;
using DiveMap.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DiveMap.Runtime
{
    /// <summary>
    /// G1 — the 📍 markers a diver left on the map (the web's <c>makePinSprite</c>/<c>placePin</c>,
    /// builder.html:2869-2876). Until now the app parsed <c>scene.pins</c> and drew nothing, so a
    /// map someone had annotated looked identical to an empty one.
    ///
    /// The pin head is drawn into a texture rather than typed as an emoji — the web types 📍 into a
    /// canvas and gets away with it because a desktop browser has an emoji font; a Unity player on
    /// Android often does not, and the marker would be an empty box. Same decision as
    /// <see cref="RecycleBadge"/>, same reason.
    ///
    /// Unlit and drawn on a disc so it needs no transparency: a marker that fades into the fog is
    /// not a marker.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PinMarker : MonoBehaviour
    {
        private const int Size = 128;

        private static readonly Color PinRed  = new Color(0.882f, 0.208f, 0.208f, 1f);
        private static readonly Color PinDark = new Color(0.616f, 0.106f, 0.106f, 1f);

        private static Texture2D _tex;
        private static Material _mat;
        private static Mesh _disc;

        private static readonly List<PinMarker> All = new List<PinMarker>();

        /// <summary>Every marker currently in the scene.</summary>
        public static IReadOnlyList<PinMarker> Markers => All;

        /// <summary>The pin's media list, already filtered to what can actually be fetched.</summary>
        public List<PinMedia.Item> Media { get; private set; } = new List<PinMedia.Item>();

        /// <summary>The pin id from the map document (for logs and deep links).</summary>
        public string PinId { get; private set; }

        private void Awake() => All.Add(this);
        private void OnDestroy() => All.Remove(this);

        /// <summary>
        /// Build every marker in <paramref name="scene"/> under <paramref name="parent"/>.
        /// Returns how many were placed.
        /// </summary>
        public static int BuildAll(SceneData scene, Transform parent)
        {
            if (scene == null || parent == null) return 0;

            int made = 0, withMedia = 0;
            foreach (ScenePin pin in scene.Pins())
            {
                double[] p = pin.P;
                if (p == null || p.Length < 3) continue;

                var go = new GameObject("Pin_" + (pin.Id ?? made.ToString()));
                go.transform.SetParent(parent, false);
                go.transform.position = new Vector3((float)p[0],
                                                    (float)(p[1] + PinMedia.MarkerLift),
                                                    (float)p[2]);

                go.AddComponent<MeshFilter>().sharedMesh = Disc();
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = MarkerMaterial();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                go.AddComponent<Billboard>();

                var m = go.AddComponent<PinMarker>();
                m.PinId = pin.Id;
                m.Media = PinMedia.Read(pin.Media);
                if (m.Media.Count > 0) withMedia++;
                made++;
            }

            // Logged even when there are none: silence would be indistinguishable from "the pin
            // code never ran", which is exactly the ambiguity that cost three QC rounds on C5.
            Debug.Log($"[Pins] placed {made} marker(s), {withMedia} with media");
            return made;
        }

        /// <summary>
        /// The marker nearest to <paramref name="ray"/>, within
        /// <see cref="PinMedia.TapRadius"/> of it. Distance to the RAY rather than to the camera,
        /// so a tap picks what is under the finger rather than whatever is closest.
        /// </summary>
        public static PinMarker Pick(Ray ray)
        {
            PinMarker best = null;
            float bestT = float.MaxValue;
            float r = (float)PinMedia.TapRadius;

            for (int i = 0; i < All.Count; i++)
            {
                PinMarker m = All[i];
                if (m == null) continue;
                Vector3 to = m.transform.position - ray.origin;
                float t = Vector3.Dot(to, ray.direction);
                if (t <= 0f) continue;                       // behind the camera
                float d = Vector3.Distance(m.transform.position, ray.origin + ray.direction * t);
                if (d > r) continue;
                if (t >= bestT) continue;                     // a nearer marker already won
                bestT = t; best = m;
            }
            return best;
        }

        // ── mesh / material / texture ────────────────────────────────────────────

        private static Mesh Disc()
        {
            if (_disc != null) return _disc;
            const int seg = 36;
            float r = (float)PinMedia.MarkerSize * 0.5f;

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

            var tris = new int[seg * 6];
            int t2 = 0;
            for (int i = 0; i < seg; i++)
            {
                int b = 1 + i, c = 1 + (i + 1) % seg;
                tris[t2++] = 0; tris[t2++] = b; tris[t2++] = c;
                tris[t2++] = 0; tris[t2++] = c; tris[t2++] = b;   // both faces
            }

            _disc = new Mesh { name = "PinMarkerDisc" };
            _disc.vertices = verts;
            _disc.uv = uvs;
            _disc.triangles = tris;
            var n = new Vector3[verts.Length];
            for (int i = 0; i < n.Length; i++) n[i] = Vector3.back;
            _disc.normals = n;
            _disc.RecalculateBounds();
            return _disc;
        }

        private static Material MarkerMaterial()
        {
            if (_mat != null) return _mat;
            Material src = Resources.Load<Material>("DM_GltfUnlit")
                        ?? Resources.Load<Material>("DM_Standard");
            _mat = src != null ? new Material(src) : new Material(Shader.Find("Standard"));

            Texture2D tex = MarkerTexture();
            if (_mat.HasProperty("baseColorTexture")) _mat.SetTexture("baseColorTexture", tex);
            else if (_mat.HasProperty("_MainTex")) _mat.SetTexture("_MainTex", tex);
            if (_mat.HasProperty("baseColorFactor")) _mat.SetColor("baseColorFactor", Color.white);
            _mat.color = Color.white;
            return _mat;
        }

        /// <summary>A white disc carrying a red map-pin: round head, tapering point, white eye.</summary>
        private static Texture2D MarkerTexture()
        {
            if (_tex != null) return _tex;

            _tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "PinMarker" };
            var px = new Color32[Size * Size];

            const float cx = 64f;
            const float headY = 50f, headR = 30f;   // canvas coords, y down
            const float tipY = 112f;
            const float eyeR = 11f;

            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float fx = x + 0.5f;
                float fy = Size - (y + 0.5f);   // canvas y grows downward

                Color c = Color.white;

                // Head: a circle. Point: a triangle from the head's flanks down to the tip.
                float hd = Mathf.Sqrt((fx - cx) * (fx - cx) + (fy - headY) * (fy - headY));
                bool inHead = hd <= headR;
                bool inPoint = fy >= headY &&
                               Mathf.Abs(fx - cx) <= headR * (1f - (fy - headY) / (tipY - headY));

                if (inHead || inPoint)
                {
                    c = PinRed;
                    // A darker rim gives it an edge against pale sand.
                    if (inHead && hd > headR - 3f) c = PinDark;
                    // The white eye.
                    if (hd <= eyeR) c = Color.white;
                }
                else
                {
                    // Outside the pin the disc stays a soft white so the marker reads against
                    // dark water; the MESH already cuts the circle, so no alpha is needed.
                    float dc = Mathf.Sqrt((fx - cx) * (fx - cx) + (fy - 64f) * (fy - 64f));
                    c = dc > 62f ? PinDark : new Color(1f, 1f, 1f, 1f);
                }

                px[y * Size + x] = c;
            }

            _tex.SetPixels32(px);
            _tex.Apply(false, false);
            _tex.wrapMode = TextureWrapMode.Clamp;
            _tex.filterMode = FilterMode.Bilinear;
            return _tex;
        }
    }
}
