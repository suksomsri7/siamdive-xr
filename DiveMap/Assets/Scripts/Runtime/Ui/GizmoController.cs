using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using DiveMap.Core;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// Tap to select, drag to transform — the app's stand-in for three.js
    /// <c>TransformControls</c> (<c>tc</c> in builder.html).
    ///
    /// Two rules that keep it out of everyone else's way:
    ///  • **Map view only.** The web's gizmo lives in EDIT mode, which is a different mode from
    ///    the tour. Running it during a dive would fight the joysticks for the same finger and
    ///    put the toolbar on top of the minimap (IMPROVEMENTS F4). <see cref="ModeManager"/>
    ///    already draws that line; this obeys it.
    ///  • **Never while the finger is over UI.** A drag that starts on the toolbar is a button
    ///    press, not a move.
    ///
    /// The transform is written to the scene JSON on every frame of the drag so what you see is
    /// what will be saved, but history and autosave only fire on RELEASE — one snapshot and one
    /// PATCH per gesture, not per frame. That is the same fix the web shipped in v.0735 after a
    /// one-minute drag turned into 46 PATCHes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GizmoController : MonoBehaviour
    {
        private static GizmoController _instance;

        private string _id;
        private bool _dragging;
        private Vector2 _pressPos;
        private int _finger = -1;

        // What the object looked like when the drag began.
        private double _startYaw;
        private double _startScale;
        private double _grabDx, _grabDz;   // offset between the object and the finger's plane hit
        private double _planeY;
        private Transform _target;         // the built GameObject, found once per drag
        private float _scaleUnit = 1f;     // built localScale ÷ the JSON scale that produced it

        private SelectionToolbar.Mode _mode = SelectionToolbar.Mode.Translate;

        /// <summary>The currently selected item id, or null.</summary>
        public static string Selected => _instance != null ? _instance._id : null;

        /// <summary>QC: true while a transform drag is in progress.</summary>
        public static bool IsDragging => _instance != null && _instance._dragging;

        public static GizmoController Ensure()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("GizmoController");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<GizmoController>();
            return _instance;
        }

        private void OnEnable()
        {
            SelectionToolbar.ModeChanged += OnModeChanged;
            SelectionToolbar.Dismissed += Deselect;
        }

        private void OnDisable()
        {
            SelectionToolbar.ModeChanged -= OnModeChanged;
            SelectionToolbar.Dismissed -= Deselect;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void OnModeChanged(SelectionToolbar.Mode mode) => _mode = mode;

        /// <summary>Select an item by id (used by the picker and by QC).</summary>
        public static void Select(string itemId)
        {
            GizmoController g = Ensure();
            g._id = itemId;
            SelectionToolbar.Show(itemId, IsRecolorable(itemId));
        }

        public static void Deselect()
        {
            if (_instance == null) return;
            _instance._id = null;
            _instance._dragging = false;
            MapEditor.Dragging = false;
        }

        /// <summary>
        /// The web only tints rocks (<c>isRecolorable()</c> :2626 — <c>kind === 'ROCK'</c>);
        /// tinting a textured fish just makes it muddy.
        /// </summary>
        private static bool IsRecolorable(string itemId)
        {
            var boot = FindFirstObjectByType<AppBoot>();
            SceneData scene = boot != null ? boot.CurrentScene : null;
            if (scene == null || AppBoot.Manifest == null) return false;

            JObject item = SceneEdit.Find(SceneEdit.Items(scene), itemId);
            string assetId = item != null ? (string)item["assetId"] : null;
            AssetManifest.Module m = assetId != null ? AppBoot.Manifest.Find(assetId) : null;
            return m != null && m.Kind == Palette.Rock;
        }

        /// <summary>
        /// QC only — drive a whole gesture without a real finger. The headless player has no
        /// touch input, so this is the only way a CI run can prove that dragging actually moves
        /// the object AND that exactly one history entry comes out of it.
        /// </summary>
        public static void QcDrag(SelectionToolbar.Mode mode, Vector2 from, Vector2 to)
        {
            GizmoController g = Ensure();
            g._mode = mode;
            g.Press(from, -1);
            g.Move(to);
            g.Release();
        }

        // ── input ────────────────────────────────────────────────────────────────

        private void Update()
        {
            // Editing belongs to the map view, exactly as on the web.
            if (ModeManager.Current != AppMode.View)
            {
                if (_id != null) { SelectionToolbar.Hide(); Deselect(); }
                return;
            }
            if (_id == null) return;

            if (Input.touchCount > 0)
            {
                Touch t = Input.GetTouch(0);
                if (t.phase == TouchPhase.Began) Press(t.position, t.fingerId);
                else if (t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary) Move(t.position);
                else if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) Release();
                return;
            }

            if (Input.GetMouseButtonDown(0)) Press(Input.mousePosition, -1);
            else if (Input.GetMouseButton(0)) Move(Input.mousePosition);
            else if (Input.GetMouseButtonUp(0)) Release();
        }

        private void Press(Vector2 pos, int finger)
        {
            if (UiShell.PointerOverUi()) return;   // a drag that starts on the toolbar is a button press

            _pressPos = pos;
            _finger = finger;
            _dragging = false;

            JObject item = CurrentItem();
            if (item == null) return;

            double[] p = ReadVec(item, "p", 0, 0, 0);
            double[] r = ReadVec(item, "r", 0, 0, 0);
            double[] s = ReadVec(item, "s", 1, 1, 1);

            _planeY = p[1];
            _startYaw = r[1];
            _startScale = s[0];

            // Grab offset: keep the object under the same part of the finger it was grabbed by,
            // instead of snapping its centre to the touch point.
            if (PlaneHit(pos, _planeY, out double hx, out double hz))
            {
                _grabDx = p[0] - hx;
                _grabDz = p[2] - hz;
            }
            else { _grabDx = 0; _grabDz = 0; }

            // Find the built object ONCE, and work out what one unit of JSON scale is worth on
            // it: SceneBuilder bakes the module's defaultScale in, so item.s is a multiplier on
            // that, not the final value. Re-deriving this mid-drag from the object we are
            // ourselves resizing would make it drift.
            _target = FindBuilt(_id);
            _scaleUnit = 1f;
            if (_target != null)
            {
                float asked = Mathf.Max(1e-4f, (float)_startScale);
                _scaleUnit = Mathf.Max(1e-4f, _target.localScale.x / asked);
            }
        }

        /// <summary>The built GameObject for an item — SceneBuilder names them Item_{id}_{assetId}.</summary>
        private static Transform FindBuilt(string itemId)
        {
            GameObject mapRoot = GameObject.Find("Map");
            if (mapRoot == null) return null;

            foreach (Transform child in mapRoot.transform)
            {
                if (!ItemPicker.IsItemName(child.name)) continue;
                if (!ItemPicker.ParseItemName(child.name, out string id, out _)) continue;
                if (id == itemId) return child;
            }
            return null;
        }

        private void Move(Vector2 pos)
        {
            if (_id == null) return;
            if (_finger >= 0 && Input.touchCount > 0 && Input.GetTouch(0).fingerId != _finger) return;

            Vector2 d = pos - _pressPos;
            if (!_dragging)
            {
                if (!GizmoMath.IsDrag(d.x, d.y)) return;   // still a tap
                _dragging = true;
                MapEditor.Dragging = true;                 // hold autosave until the finger lifts
            }

            JObject item = CurrentItem();
            if (item == null) return;
            JArray items = Items();

            switch (_mode)
            {
                case SelectionToolbar.Mode.Translate:
                    if (PlaneHit(pos, _planeY, out double hx, out double hz))
                        SceneEdit.Move(items, _id, hx + _grabDx, _planeY, hz + _grabDz);
                    break;

                case SelectionToolbar.Mode.Rotate:
                {
                    double[] r = ReadVec(item, "r", 0, 0, 0);
                    SceneEdit.Rotate(items, _id, r[0], GizmoMath.YawAfterDrag(_startYaw, d.x), r[2]);
                    break;
                }

                case SelectionToolbar.Mode.Scale:
                {
                    double k = GizmoMath.ScaleAfterDrag(_startScale, d.x);
                    SceneEdit.Scale(items, _id, k, k, k);
                    break;
                }
            }

            // Move the built object with the finger. Rebuilding the whole map per frame would
            // reload every GLB; the JSON is already correct, and the release rebuilds properly.
            PreviewTransform(item);
        }

        private void Release()
        {
            if (!_dragging) { _finger = -1; return; }

            _dragging = false;
            _finger = -1;
            MapEditor.Dragging = false;

            JArray items = Items();
            if (items != null)
            {
                MapEditor.RecordAndApply(items);   // one snapshot + one save per gesture
                Debug.Log($"[Edit] {_mode} committed for {_id}");
            }
        }

        // ── helpers ──────────────────────────────────────────────────────────────

        private static JArray Items()
        {
            var boot = FindFirstObjectByType<AppBoot>();
            SceneData scene = boot != null ? boot.CurrentScene : null;
            return scene != null ? SceneEdit.Items(scene) : null;
        }

        private JObject CurrentItem()
        {
            JArray items = Items();
            return items != null ? SceneEdit.Find(items, _id) : null;
        }

        private static bool PlaneHit(Vector2 screenPos, double planeY, out double x, out double z)
        {
            x = 0; z = 0;
            Camera cam = Camera.main;
            if (cam == null) return false;

            Ray ray = cam.ScreenPointToRay(screenPos);
            return GizmoMath.RayOnPlane(ray.origin.x, ray.origin.y, ray.origin.z,
                                        ray.direction.x, ray.direction.y, ray.direction.z,
                                        planeY, out x, out z);
        }

        /// <summary>
        /// Push the JSON transform onto the live GameObject so the drag is visible immediately.
        /// Rebuilding the map every frame would re-download every GLB; the JSON is already
        /// correct and <see cref="Release"/> does a proper rebuild when the finger lifts.
        /// </summary>
        private void PreviewTransform(JObject item)
        {
            if (_target == null) return;

            double[] p = ReadVec(item, "p", 0, 0, 0);
            double[] r = ReadVec(item, "r", 0, 0, 0);
            double[] s = ReadVec(item, "s", 1, 1, 1);

            double[] up = WebCoord.PositionToUnity(p);
            _target.position = new Vector3((float)up[0], (float)up[1], (float)up[2]);

            WebCoord.Quat q = WebCoord.RotationToUnity(r);
            _target.rotation = new Quaternion((float)q.X, (float)q.Y, (float)q.Z, (float)q.W);

            float k = Mathf.Max(1e-4f, (float)s[0]) * _scaleUnit;
            _target.localScale = new Vector3(k, k, k);
        }

        private static double[] ReadVec(JObject o, string key, double dx, double dy, double dz)
        {
            var v = new[] { dx, dy, dz };
            if (o != null && o[key] is JArray a)
                for (int i = 0; i < 3 && i < a.Count; i++)
                    try { v[i] = a[i].Value<double>(); } catch { /* keep the default */ }
            return v;
        }
    }
}
