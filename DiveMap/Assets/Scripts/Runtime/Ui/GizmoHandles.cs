using UnityEngine;
using DiveMap.Core;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// WO-O — the arrows themselves: the red/green/blue axis handles and the three little plane
    /// quads that the user's reference photo of the web builder shows on a selected object.
    ///
    /// WHY IT IS DRAWN HERE RATHER THAN IMPORTED. There is no gizmo geometry anywhere in the
    /// project (the WO-N inventory confirmed it), and the reference is three.js
    /// <c>TransformControls</c>, which is not a thing we can take. So the meshes are generated:
    /// a shaft cylinder plus a cone head per axis, a flat quad per plane. Six small procedural
    /// meshes built once and shared — the same approach <c>PinMarker</c> and <c>WarpGate</c>
    /// already use for their procedural visuals.
    ///
    /// TWO PROPERTIES THAT MAKE A GIZMO USABLE, both easy to get wrong:
    ///
    ///  • **Constant size on screen.** Rebuilt every frame from
    ///    <see cref="GizmoMath.WorldPerPixel"/>, so an arrow asked to be 90 px long is 90 px long
    ///    whether the rock is under the camera or across the map. A fixed world size would give
    ///    a distant object arrows too small to hit and a near one arrows that swallow the screen.
    ///    The web does the same thing through TransformControls' own scaling, turned up 35 % for
    ///    touch (<c>tc.size = 1.35</c>, builder.html:532) — this uses a touch-sized 90 px to the
    ///    same end.
    ///
    ///  • **Always visible.** The handles render on top of everything with depth testing off and
    ///    an overlay queue. A gizmo that disappears inside the rock it is attached to is worse
    ///    than none: the user sees nothing and concludes the selection did not take. This is the
    ///    one place in the app where drawing through geometry is correct.
    ///
    /// The highlight is a straight colour swap on the grabbed handle, matching the web's yellow
    /// hover/active state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GizmoHandles : MonoBehaviour
    {
        /// <summary>Arrow length in SCREEN pixels — the number that sets the apparent size.</summary>
        public const float AxisPixels = 90f;
        /// <summary>Shaft thickness, screen px.</summary>
        private const float ShaftPixels = 3.2f;
        /// <summary>Cone head length / width, screen px.</summary>
        private const float HeadPixels = 22f;
        private const float HeadWidthPixels = 9f;
        /// <summary>Plane quad size and how far along each axis its centre sits, screen px.</summary>
        private const float QuadPixels = 20f;
        private const float QuadOffsetPixels = 30f;

        // three.js TransformControls' own axis colours, which is what the user's photo shows.
        private static readonly Color XCol = new Color(1f, 0.25f, 0.28f, 1f);
        private static readonly Color YCol = new Color(0.35f, 0.92f, 0.36f, 1f);
        private static readonly Color ZCol = new Color(0.32f, 0.55f, 1f, 1f);
        private static readonly Color Hot = new Color(1f, 0.92f, 0.25f, 1f);   // grabbed

        private static GizmoHandles _instance;

        private Transform _root;
        private readonly Transform[] _axis = new Transform[3];    // X, Y, Z shaft+head parents
        private readonly Transform[] _quad = new Transform[3];    // XY, YZ, XZ
        private readonly Renderer[][] _axisRends = new Renderer[3][];
        private readonly Renderer[] _quadRends = new Renderer[3];
        private readonly Color[] _axisBase = { XCol, YCol, ZCol };
        private readonly Color[] _quadBase = { ZCol, XCol, YCol };  // tinted by their normal axis

        private GizmoMath.Handle _hot = GizmoMath.Handle.None;
        private bool _shown;

        /// <summary>World position the handles are drawn around (the selected object).</summary>
        public Vector3 Origin { get; private set; }

        public static GizmoHandles Ensure()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("GizmoHandles");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<GizmoHandles>();
            _instance.Build();
            return _instance;
        }

        public static GizmoHandles Current => _instance;
        /// <summary>QC — are the arrows on screen right now?</summary>
        public static bool Visible => _instance != null && _instance._shown;

        // ── build ────────────────────────────────────────────────────────────────

        private void Build()
        {
            _root = new GameObject("Handles").transform;
            _root.SetParent(transform, false);

            for (int i = 0; i < 3; i++)
            {
                Vector3 dir = i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;
                var parent = new GameObject($"Axis_{(char)('X' + i)}").transform;
                parent.SetParent(_root, false);
                // Point +Y (the cylinder/cone build axis) down the world axis this handle owns.
                parent.localRotation = Quaternion.FromToRotation(Vector3.up, dir);

                Renderer shaft = MakePart(parent, "Shaft", Cylinder(), _axisBase[i]);
                Renderer head = MakePart(parent, "Head", Cone(), _axisBase[i]);
                _axis[i] = parent;
                _axisRends[i] = new[] { shaft, head };
            }

            for (int i = 0; i < 3; i++)
            {
                var parent = new GameObject($"Quad_{i}").transform;
                parent.SetParent(_root, false);
                // XY quad lies in the XY plane, so its own +Y build axis turns to face +Z, etc.
                Vector3 n = i == 0 ? Vector3.forward : i == 1 ? Vector3.right : Vector3.up;
                parent.localRotation = Quaternion.FromToRotation(Vector3.up, n);
                _quadRends[i] = MakePart(parent, "Quad", Quad(), _quadBase[i]);
                _quad[i] = parent;
            }

            SetShown(false);
        }

        private static Renderer MakePart(Transform parent, string name, Mesh mesh, Color c)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = HandleMaterial(c);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return mr;
        }

        private static readonly System.Collections.Generic.Dictionary<int, Material> Mats =
            new System.Collections.Generic.Dictionary<int, Material>();

        /// <summary>
        /// Unlit, depth-test off, overlay queue. Unlit because a handle must read the same in the
        /// dark water of a night map as at the surface — it is UI that happens to live in the 3D
        /// scene, and shading it would make the blue Z arrow vanish against blue fog.
        /// </summary>
        private static Material HandleMaterial(Color c)
        {
            int key = c.GetHashCode();
            if (Mats.TryGetValue(key, out Material cached) && cached != null) return cached;

            Material src = Resources.Load<Material>("DM_GltfUnlit")
                        ?? Resources.Load<Material>("DM_Standard");
            var mat = src != null ? new Material(src) : new Material(Shader.Find("Unlit/Color"));
            mat.color = c;
            if (mat.HasProperty("baseColorFactor")) mat.SetColor("baseColorFactor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", c * 0.8f);
            }
            // 8 = Always. The gizmo draws through the object it is attached to on purpose.
            if (mat.HasProperty("_ZTest")) mat.SetInt("_ZTest", 8);
            mat.renderQueue = 4000;   // after Overlay (3000-3500) and after WarpGate's 3080
            Mats[key] = mat;
            return mat;
        }

        // ── meshes (unit-sized; scaled per frame) ────────────────────────────────

        private static Mesh _cyl, _cone, _quadMesh;

        /// <summary>Unit cylinder: radius 0.5, height 1, base at y=0 — grows up +Y.</summary>
        private static Mesh Cylinder()
        {
            if (_cyl != null) return _cyl;
            const int seg = 10;
            var v = new Vector3[seg * 2];
            var t = new int[seg * 6];
            for (int i = 0; i < seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                float x = Mathf.Cos(a) * 0.5f, z = Mathf.Sin(a) * 0.5f;
                v[i] = new Vector3(x, 0f, z);
                v[i + seg] = new Vector3(x, 1f, z);
            }
            int k = 0;
            for (int i = 0; i < seg; i++)
            {
                int n = (i + 1) % seg;
                t[k++] = i; t[k++] = i + seg; t[k++] = n + seg;
                t[k++] = i; t[k++] = n + seg; t[k++] = n;
            }
            _cyl = new Mesh { name = "GizmoShaft", vertices = v, triangles = t };
            _cyl.RecalculateNormals();
            _cyl.RecalculateBounds();
            return _cyl;
        }

        /// <summary>Unit cone: radius 0.5 at y=0, apex at y=1.</summary>
        private static Mesh Cone()
        {
            if (_cone != null) return _cone;
            const int seg = 12;
            var v = new Vector3[seg + 2];
            v[0] = Vector3.zero;                 // base centre
            v[seg + 1] = new Vector3(0f, 1f, 0f);  // apex
            for (int i = 0; i < seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                v[i + 1] = new Vector3(Mathf.Cos(a) * 0.5f, 0f, Mathf.Sin(a) * 0.5f);
            }
            var t = new int[seg * 6];
            int k = 0;
            for (int i = 0; i < seg; i++)
            {
                int a = i + 1, b = (i + 1) % seg + 1;
                t[k++] = 0; t[k++] = b; t[k++] = a;              // base
                t[k++] = seg + 1; t[k++] = a; t[k++] = b;        // side
            }
            _cone = new Mesh { name = "GizmoHead", vertices = v, triangles = t };
            _cone.RecalculateNormals();
            _cone.RecalculateBounds();
            return _cone;
        }

        /// <summary>Unit double-sided quad in the XZ plane, centred, 1×1.</summary>
        private static Mesh Quad()
        {
            if (_quadMesh != null) return _quadMesh;
            var v = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, 0.5f), new Vector3(-0.5f, 0f, 0.5f),
            };
            var t = new[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 };   // both faces
            _quadMesh = new Mesh { name = "GizmoQuad", vertices = v, triangles = t };
            _quadMesh.RecalculateNormals();
            _quadMesh.RecalculateBounds();
            return _quadMesh;
        }

        // ── per-frame placement ──────────────────────────────────────────────────

        public void Hide() => SetShown(false);

        /// <summary>Put the handles on <paramref name="worldPos"/> and size them for the camera.</summary>
        public void ShowAt(Vector3 worldPos)
        {
            Camera cam = Camera.main;
            if (cam == null) { SetShown(false); return; }

            Origin = worldPos;
            _root.position = worldPos;
            _root.rotation = Quaternion.identity;   // world-aligned, like TransformControls' default space

            // 🔴 The size rule. Distance measured along the camera's FORWARD axis, not the raw
            // gap: near the edge of a wide field of view the straight-line distance is noticeably
            // longer than the depth, and using it makes the handles swell as the object drifts
            // off-centre — the same reason a projection matrix uses z, not |v|.
            float depth = Vector3.Dot(worldPos - cam.transform.position, cam.transform.forward);
            if (depth <= 0.01f) { SetShown(false); return; }   // behind the camera

            float wpp = (float)GizmoMath.WorldPerPixel(depth, cam.fieldOfView, Screen.height);
            if (wpp <= 0f) { SetShown(false); return; }

            float len = AxisPixels * wpp;
            float shaft = ShaftPixels * wpp;
            float head = HeadPixels * wpp;
            float headW = HeadWidthPixels * wpp;
            float quad = QuadPixels * wpp;
            float off = QuadOffsetPixels * wpp;

            for (int i = 0; i < 3; i++)
            {
                Transform s = _axis[i].Find("Shaft");
                Transform h = _axis[i].Find("Head");
                s.localScale = new Vector3(shaft, Mathf.Max(0.001f, len - head), shaft);
                s.localPosition = Vector3.zero;
                h.localScale = new Vector3(headW, head, headW);
                h.localPosition = new Vector3(0f, len - head, 0f);
            }

            // Each quad sits in the wedge between the two axes it spans.
            _quad[0].localPosition = new Vector3(off, off, 0f);    // XY
            _quad[1].localPosition = new Vector3(0f, off, off);    // YZ
            _quad[2].localPosition = new Vector3(off, 0f, off);    // XZ
            for (int i = 0; i < 3; i++)
                _quad[i].localScale = new Vector3(quad, quad, quad);

            SetShown(true);
        }

        /// <summary>Highlight the grabbed handle (the web's yellow active state).</summary>
        public void SetHot(GizmoMath.Handle h)
        {
            if (_hot == h) return;
            _hot = h;
            for (int i = 0; i < 3; i++)
            {
                bool on = (int)h == i + 1;                       // Handle.X/Y/Z are 1..3
                Color c = on ? Hot : _axisBase[i];
                if (_axisRends[i] != null)
                    for (int j = 0; j < _axisRends[i].Length; j++)
                        if (_axisRends[i][j] != null) _axisRends[i][j].sharedMaterial = HandleMaterial(c);
            }
            for (int i = 0; i < 3; i++)
            {
                bool on = (int)h == i + 4;                       // Handle.XY/YZ/XZ are 4..6
                if (_quadRends[i] != null)
                    _quadRends[i].sharedMaterial = HandleMaterial(on ? Hot : _quadBase[i]);
            }
        }

        private void SetShown(bool on)
        {
            _shown = on;
            if (_root != null && _root.gameObject.activeSelf != on) _root.gameObject.SetActive(on);
        }

        // ── screen projection, for picking ───────────────────────────────────────

        /// <summary>
        /// Screen position of a handle anchor, or NaN when it is behind the camera.
        ///
        /// NaN rather than the mirrored point Unity returns: <c>WorldToScreenPoint</c> with a
        /// negative z gives a coordinate on the WRONG side of the screen, and a handle projected
        /// there would silently steal presses aimed at the map. <see cref="GizmoMath.Pick"/>
        /// skips NaN for exactly this reason.
        /// </summary>
        private static Vector2 Project(Camera cam, Vector3 world)
        {
            Vector3 p = cam.WorldToScreenPoint(world);
            if (p.z <= 0.01f) return new Vector2(float.NaN, float.NaN);
            return new Vector2(p.x, p.y);
        }

        /// <summary>Which handle is under <paramref name="screenPos"/>, in screen space.</summary>
        public GizmoMath.Handle PickAt(Vector2 screenPos)
        {
            Camera cam = Camera.main;
            if (!_shown || cam == null) return GizmoMath.Handle.None;

            Vector2 o = Project(cam, Origin);
            Vector2 xt = Project(cam, _axis[0].TransformPoint(new Vector3(0f, TipLocal(), 0f)));
            Vector2 yt = Project(cam, _axis[1].TransformPoint(new Vector3(0f, TipLocal(), 0f)));
            Vector2 zt = Project(cam, _axis[2].TransformPoint(new Vector3(0f, TipLocal(), 0f)));
            Vector2 q0 = Project(cam, _quad[0].position);
            Vector2 q1 = Project(cam, _quad[1].position);
            Vector2 q2 = Project(cam, _quad[2].position);

            return GizmoMath.Pick(screenPos.x, screenPos.y,
                                  o.x, o.y, xt.x, xt.y, yt.x, yt.y, zt.x, zt.y,
                                  q0.x, q0.y, q1.x, q1.y, q2.x, q2.y);
        }

        /// <summary>Tip of an arrow in its parent's local space (shaft + head).</summary>
        private float TipLocal()
        {
            Transform h = _axis[0].Find("Head");
            return h != null ? h.localPosition.y + h.localScale.y : 1f;
        }
    }
}
