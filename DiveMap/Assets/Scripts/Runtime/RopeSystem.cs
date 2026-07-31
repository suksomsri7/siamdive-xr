using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using DiveMap.Core;
using DiveMap.Runtime.Ui;   // GizmoController, SelectionToolbar — child namespace needs a using

namespace DiveMap.Runtime
{
    /// <summary>
    /// Draws the map's ropes and keeps them attached while their objects move.
    ///
    /// Each end is stored in its object's LOCAL space (<see cref="RopeMath"/>), so following a
    /// dragged object is just <c>TransformPoint</c> every frame — no re-anchoring, no drift, and
    /// a rope stays put through rotation and scaling as well as translation.
    ///
    /// The mesh is a tube: <see cref="RopeMath.Samples"/> points along the curve, a ring of
    /// <see cref="Sides"/> vertices at each. Unity has no TubeGeometry, and a LineRenderer would
    /// be camera-facing — a rope seen from above would collapse to a hairline, which is exactly
    /// the angle a diver looks at the seabed from.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RopeSystem : MonoBehaviour
    {
        /// <summary>Vertices around the tube. The web uses 6.</summary>
        public const int Sides = 6;

        private static RopeSystem _instance;
        public static RopeSystem Instance => _instance;

        private readonly List<Rope> _ropes = new List<Rope>();
        private readonly Dictionary<string, GameObject> _views =
            new Dictionary<string, GameObject>(System.StringComparer.Ordinal);
        private readonly List<double[]> _curve = new List<double[]>();
        private Material _material;

        /// <summary>QC surface.</summary>
        public int Count => _ropes.Count;
        public int DrawnCount => _views.Count;
        public IReadOnlyList<Rope> Ropes => _ropes;

        public static RopeSystem Ensure()
        {
            if (_instance != null) return _instance;
            GameObject mapRoot = GameObject.Find("Map");
            if (mapRoot == null) return null;

            var go = new GameObject("Ropes");
            go.transform.SetParent(mapRoot.transform, false);
            _instance = go.AddComponent<RopeSystem>();
            return _instance;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        /// <summary>Load from <c>env.ropes</c> — called once per map build.</summary>
        public static void Load(SceneData scene)
        {
            RopeSystem sys = Ensure();
            if (sys == null) return;

            sys._ropes.Clear();
            JArray arr = scene?.Env?.Ropes;
            sys._ropes.AddRange(RopeMath.Parse(arr));
            sys.RebuildAll();
            Debug.Log($"[Rope] loaded {sys._ropes.Count} rope(s)");
        }

        /// <summary>Write back into <c>env.ropes</c>. Ropes ride along in the normal PATCH.</summary>
        public void Save()
        {
            var boot = FindFirstObjectByType<AppBoot>();
            SceneData scene = boot != null ? boot.CurrentScene : null;
            if (scene == null) return;

            if (!(scene.Root["env"] is JObject env))
            {
                env = new JObject();
                scene.Root["env"] = env;
            }
            env["ropes"] = RopeMath.Serialise(_ropes);
            MapEditor.MarkSculpted();   // same "the map changed, save it" path as the seabed
        }

        // ── editing ──────────────────────────────────────────────────────────────

        public Rope Add(RopeEnd a, RopeEnd b)
        {
            if (!WorldOf(a, out Vector3 pa) || !WorldOf(b, out Vector3 pb)) return null;

            var rope = new Rope
            {
                Id = RopeMath.NewId(_ropes.Count),
                A = a,
                B = b,
                Sag = RopeMath.DefaultSagFor(Vector3.Distance(pa, pb)),
            };
            _ropes.Add(rope);
            Refresh(rope);
            Save();
            Debug.Log($"[Rope] added {rope.Id} {a.ItemId}↔{b.ItemId} sag={rope.Sag:F1}");
            return rope;
        }

        public bool Remove(string ropeId)
        {
            int i = _ropes.FindIndex(r => r.Id == ropeId);
            if (i < 0) return false;
            _ropes.RemoveAt(i);
            DestroyView(ropeId);
            Save();
            Debug.Log("[Rope] removed " + ropeId);
            return true;
        }

        /// <summary>
        /// Drop every rope tied to a deleted object. Called by the delete paths — a rope left
        /// pointing at nothing stays in the saved JSON and silently fails to draw, which is data
        /// that looks fine and behaves as if it is gone.
        /// </summary>
        public static void DetachFrom(string itemId)
        {
            RopeSystem sys = _instance;
            if (sys == null) return;

            var doomed = new List<string>();
            foreach (Rope r in sys._ropes)
                if (r.A.ItemId == itemId || r.B.ItemId == itemId) doomed.Add(r.Id);

            if (doomed.Count == 0) return;
            RopeMath.DetachFrom(sys._ropes, itemId);
            foreach (string id in doomed) sys.DestroyView(id);
            sys.Save();
            Debug.Log($"[Rope] detached {doomed.Count} from deleted {itemId}");
        }

        public Rope Find(string ropeId) => _ropes.Find(r => r.Id == ropeId);

        /// <summary>Re-mesh one rope after its sag / colour / thickness changed.</summary>
        public void Refresh(Rope rope)
        {
            if (rope == null) return;
            if (!WorldOf(rope.A, out Vector3 a) || !WorldOf(rope.B, out Vector3 b))
            {
                DestroyView(rope.Id);   // an end is missing — draw nothing rather than a stub
                return;
            }

            RopeMath.Curve(a.x, a.y, a.z, b.x, b.y, b.z, rope.Sag, _curve);

            if (!_views.TryGetValue(rope.Id, out GameObject view) || view == null)
            {
                view = new GameObject("Rope_" + rope.Id);
                view.transform.SetParent(transform, false);
                view.AddComponent<MeshFilter>();
                view.AddComponent<MeshRenderer>();
                _views[rope.Id] = view;
            }

            var mf = view.GetComponent<MeshFilter>();
            mf.sharedMesh = BuildTube(_curve, (float)rope.Thick);

            var mr = view.GetComponent<MeshRenderer>();
            mr.sharedMaterial = MaterialFor(rope.Color);
        }

        public void RebuildAll()
        {
            foreach (Rope r in _ropes) Refresh(r);
        }

        /// <summary>
        /// Ropes follow their objects. Done in LateUpdate so it runs AFTER the gizmo has moved
        /// the object this frame — the other order leaves the rope one frame behind, which on a
        /// drag reads as the rope being made of elastic.
        /// </summary>
        private void LateUpdate()
        {
            if (_ropes.Count == 0) return;
            if (!GizmoController.IsDragging) return;   // nothing is moving; the meshes are current
            RebuildAll();
        }

        // ── geometry ─────────────────────────────────────────────────────────────

        private bool WorldOf(RopeEnd end, out Vector3 world)
        {
            world = Vector3.zero;
            if (string.IsNullOrEmpty(end.ItemId)) return false;

            GameObject mapRoot = GameObject.Find("Map");
            if (mapRoot == null) return false;

            foreach (Transform child in mapRoot.transform)
            {
                if (!ItemPicker.IsItemName(child.name)) continue;
                if (!ItemPicker.ParseItemName(child.name, out string id, out _)) continue;
                if (id != end.ItemId) continue;

                world = child.TransformPoint(new Vector3((float)end.Lx, (float)end.Ly, (float)end.Lz));
                return true;
            }
            return false;
        }

        /// <summary>A tube around a polyline: a ring of <see cref="Sides"/> verts per sample.</summary>
        private static Mesh BuildTube(List<double[]> points, float radius)
        {
            if (points == null || points.Count < 2) return null;
            if (radius <= 0.01f) radius = (float)RopeMath.DefaultThick;

            int n = points.Count;
            var verts = new Vector3[n * Sides];
            var norms = new Vector3[n * Sides];
            var tris = new int[(n - 1) * Sides * 6];

            Vector3 prevUp = Vector3.up;
            for (int i = 0; i < n; i++)
            {
                var p = new Vector3((float)points[i][0], (float)points[i][1], (float)points[i][2]);

                // Direction along the curve; at the ends, borrow the neighbour's.
                Vector3 next = i < n - 1
                    ? new Vector3((float)points[i + 1][0], (float)points[i + 1][1], (float)points[i + 1][2])
                    : p;
                Vector3 prev = i > 0
                    ? new Vector3((float)points[i - 1][0], (float)points[i - 1][1], (float)points[i - 1][2])
                    : p;
                Vector3 dir = (next - prev).sqrMagnitude > 1e-8f ? (next - prev).normalized : Vector3.forward;

                // Carry the frame along the curve instead of recomputing it from world-up, or the
                // ring flips where the rope passes through vertical and the tube pinches.
                Vector3 right = Vector3.Cross(prevUp, dir);
                if (right.sqrMagnitude < 1e-6f) right = Vector3.Cross(Vector3.right, dir);
                right.Normalize();
                Vector3 up = Vector3.Cross(dir, right).normalized;
                prevUp = up;

                for (int s = 0; s < Sides; s++)
                {
                    float a = Mathf.PI * 2f * s / Sides;
                    Vector3 offset = (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * radius;
                    verts[i * Sides + s] = p + offset;
                    norms[i * Sides + s] = offset.normalized;
                }
            }

            int t = 0;
            for (int i = 0; i < n - 1; i++)
                for (int s = 0; s < Sides; s++)
                {
                    int a = i * Sides + s;
                    int b = i * Sides + (s + 1) % Sides;
                    int c = (i + 1) * Sides + s;
                    int d = (i + 1) * Sides + (s + 1) % Sides;
                    tris[t++] = a; tris[t++] = c; tris[t++] = b;
                    tris[t++] = b; tris[t++] = c; tris[t++] = d;
                }

            var mesh = new Mesh { name = "Rope" };
            mesh.vertices = verts;
            mesh.normals = norms;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }

        private readonly Dictionary<string, Material> _materials =
            new Dictionary<string, Material>(System.StringComparer.Ordinal);

        private Material MaterialFor(string hex)
        {
            string key = RopeMath.NormaliseColor(hex);
            if (_materials.TryGetValue(key, out Material m) && m != null) return m;

            if (_material == null) _material = SceneBuilder.OpaqueMaterial();
            m = new Material(_material) { color = SelectionToolbar.Hex(key) };
            _materials[key] = m;
            return m;
        }

        private void DestroyView(string ropeId)
        {
            if (!_views.TryGetValue(ropeId, out GameObject view)) return;
            _views.Remove(ropeId);
            if (view != null) Destroy(view);
        }
    }
}
