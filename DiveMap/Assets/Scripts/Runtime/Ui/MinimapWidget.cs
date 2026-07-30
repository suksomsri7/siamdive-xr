using System.Collections.Generic;
using DiveMap.Core;
using UnityEngine;
using UnityEngine.UI;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// The web's tour minimap (#minimap, builder.html:269 + drawMinimap :3714): a 118 px disc at
    /// the bottom centre showing where you are inside the map footprint, which way you face, and
    /// where the structures and animals are around you.
    ///
    /// The web draws it into a &lt;canvas&gt;; here it is a <see cref="Texture2D"/> repainted a few
    /// times a second (not per frame — nothing on it moves fast enough to notice, and this runs
    /// while 1,100 fish are simulating).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MinimapWidget : MonoBehaviour
    {
        private const int Tex = 118;                 // the web's canvas size, in CSS px
        private const float Interval = 0.2f;         // repaint cadence

        private static readonly Color32 Clear   = new Color32(0, 0, 0, 0);
        private static readonly Color32 Ring    = new Color32(120, 200, 255, 90);
        private static readonly Color32 Solid   = new Color32(230, 240, 250, 210);   // wreck/structure
        private static readonly Color32 Fish    = new Color32(140, 220, 180, 190);   // schools
        private static readonly Color32 Animal  = new Color32(255, 214, 90, 220);    // big animals
        private static readonly Color32 Self    = new Color32(88, 220, 255, 255);    // the diver

        private RawImage _image;
        private Texture2D _tex;
        private Color32[] _px;
        private float _next;

        private Vector3 _center;
        private float _radius = 340f;
        private readonly List<Vector3> _solids = new List<Vector3>();
        private readonly List<Vector3> _schools = new List<Vector3>();
        private readonly List<Transform> _animals = new List<Transform>();

        public static MinimapWidget Instance { get; private set; }

        public static MinimapWidget Attach(RectTransform parent)
        {
            if (parent == null) return null;

            var go = new GameObject("MinimapCanvas");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            UiKit.Stretch(rt);
            rt.offsetMin = new Vector2(UiKit.Css(3f), UiKit.Css(3f));
            rt.offsetMax = new Vector2(-UiKit.Css(3f), -UiKit.Css(3f));

            var img = go.AddComponent<RawImage>();
            img.raycastTarget = false;

            var w = go.AddComponent<MinimapWidget>();
            w._image = img;
            w._tex = new Texture2D(Tex, Tex, TextureFormat.RGBA32, false)
            {
                name = "Minimap",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            w._px = new Color32[Tex * Tex];
            img.texture = w._tex;
            Instance = w;
            return w;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// What the map contains, in world units: the footprint to fit, the solid decor, the school
        /// anchors and the big animals (which move, so they stay as transforms).
        /// </summary>
        public static void Configure(Vector3 center, float radius,
                                     IList<Vector3> solids, IList<Vector3> schools,
                                     IList<Transform> animals)
        {
            MinimapWidget m = Instance;
            if (m == null) return;

            m._center = center;
            m._radius = Mathf.Max(20f, radius);
            m._solids.Clear();
            m._schools.Clear();
            m._animals.Clear();
            if (solids != null) m._solids.AddRange(solids);
            if (schools != null) m._schools.AddRange(schools);
            if (animals != null) m._animals.AddRange(animals);
            m._next = 0f;
        }

        private void LateUpdate()
        {
            if (_tex == null || Time.time < _next) return;
            _next = Time.time + Interval;

            Camera cam = Camera.main;
            if (cam == null) return;
            Paint(cam.transform.position, cam.transform.forward);
        }

        private void Paint(Vector3 eye, Vector3 forward)
        {
            for (int i = 0; i < _px.Length; i++) _px[i] = Clear;

            // A thin inner ring marks the map's edge, so "how much further can I go" is visible.
            const float half = Tex * 0.5f;
            float rEdge = half - 2f;
            for (int a = 0; a < 720; a++)
            {
                float t = Mathf.Deg2Rad * a * 0.5f;
                Plot(half + Mathf.Cos(t) * rEdge, half + Mathf.Sin(t) * rEdge, Ring, 1);
            }

            // Everything is drawn in MAP space (north up), like the web's canvas.
            for (int i = 0; i < _solids.Count; i++) Blip(_solids[i], Solid, 2);
            for (int i = 0; i < _schools.Count; i++) Blip(_schools[i], Fish, 2);
            for (int i = 0; i < _animals.Count; i++)
            {
                Transform t = _animals[i];
                if (t != null) Blip(t.position, Animal, 2);
            }

            // The diver: a dot plus a short heading whisker.
            Vector2 me = ToTex(eye);
            Plot(me.x, me.y, Self, 3);
            Vector2 dir = new Vector2(forward.x, forward.z).normalized;
            for (int s = 3; s <= 11; s++)
                Plot(me.x + dir.x * s, me.y + dir.y * s, Self, 1);

            _tex.SetPixels32(_px);
            _tex.Apply(false, false);
        }

        private Vector2 ToTex(Vector3 world)
        {
            const float half = Tex * 0.5f;
            float k = (half - 3f) / _radius;
            return new Vector2(half + (world.x - _center.x) * k,
                               half + (world.z - _center.z) * k);
        }

        private void Blip(Vector3 world, Color32 color, int size)
        {
            Vector2 p = ToTex(world);
            Plot(p.x, p.y, color, size);
        }

        private void Plot(float cx, float cy, Color32 color, int size)
        {
            int x0 = Mathf.RoundToInt(cx), y0 = Mathf.RoundToInt(cy);
            for (int dy = -size; dy <= size; dy++)
            for (int dx = -size; dx <= size; dx++)
            {
                if (dx * dx + dy * dy > size * size) continue;
                int x = x0 + dx, y = y0 + dy;
                if (x < 0 || y < 0 || x >= Tex || y >= Tex) continue;
                // Stay inside the dial.
                float ox = x - Tex * 0.5f, oy = y - Tex * 0.5f;
                if (ox * ox + oy * oy > (Tex * 0.5f - 1f) * (Tex * 0.5f - 1f)) continue;
                _px[y * Tex + x] = color;
            }
        }
    }
}
