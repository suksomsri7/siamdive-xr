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
        private bool _armed;               // a press that was refused must not arm a drag

        private SelectionToolbar.Mode _mode = SelectionToolbar.Mode.Translate;

        // WO-O — which arrow/quad this drag grabbed, and where along it the finger started.
        private GizmoMath.Handle _handle = GizmoMath.Handle.None;
        private double _grabT;                 // axis drags: the axis parameter under the finger
        private double[] _grabPos = { 0, 0, 0 };  // plane drags: object − hit, so it does not snap
        private double[] _startPos = { 0, 0, 0 };

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
            _instance._handle = GizmoMath.Handle.None;
            GizmoHandles.Current?.Hide();
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
            AssetManifest.Module m = assetId != null ? AppBoot.Manifest.Get(assetId) : null;
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

        /// <summary>
        /// QC — force the handles to lay themselves out now, so a screenshot taken in the same
        /// frame as the selection has arrows in it. Update() would do this a frame later, and the
        /// CI harness captures immediately after selecting.
        /// </summary>
        public static void QcLayOutHandles()
        {
            if (_instance == null || _instance._id == null) return;
            _instance.UpdateHandles();
        }

        /// <summary>QC — which handle a press at this screen point would grab.</summary>
        public static GizmoMath.Handle QcHandleAt(Vector2 screenPos)
            => GizmoHandles.Current != null ? GizmoHandles.Current.PickAt(screenPos)
                                            : GizmoMath.Handle.None;

        /// <summary>QC — the handle the CURRENT drag grabbed (None = free/whole-screen drag).</summary>
        public static GizmoMath.Handle QcGrabbed =>
            _instance != null ? _instance._handle : GizmoMath.Handle.None;

        /// <summary>
        /// QC — what the LAST press grabbed, kept after the gesture ends.
        ///
        /// 🔴 <see cref="QcGrabbed"/> cannot answer this question after the fact:
        /// <see cref="Release"/> clears <c>_handle</c>, which is correct for the app and useless
        /// for evidence. Re-picking at the press point afterwards is worse than useless — a
        /// SUCCESSFUL constrained drag moves the object, and the handles move with it, so the
        /// arrow is no longer under the old coordinate and the re-pick returns None. b389 read
        /// exactly that None and it was indistinguishable from "the press missed".
        /// </summary>
        public static GizmoMath.Handle QcLastPressed { get; private set; }

        /// <summary>QC — the last press never reached the gizmo at all (the UI ate it).</summary>
        public static bool QcLastPressBlockedByUi { get; private set; }

        /// <summary>
        /// QC — the arithmetic of the last constrained move, in the order it happens:
        /// where along the axis the finger grabbed, where it is now, the difference that is
        /// applied to the object, and whether the closest-approach solve was usable at all.
        ///
        /// 🔴 b390 needed these: the press grabbed X (proved), the gesture committed (proved), and
        /// the object did not move by a millimetre. Every remaining explanation lives in these
        /// four numbers, and none of them is visible from outside.
        /// </summary>
        public static double QcLastGrabT { get; private set; }
        public static double QcLastAxisT { get; private set; }
        public static double QcLastAlong { get; private set; }
        public static bool QcLastAxisOk { get; private set; }

        /// <summary>QC — the write itself: what was asked for, whether the array accepted it, and
        /// what the array said one line later.</summary>
        public static double QcLastWroteX { get; private set; }
        public static bool QcLastMoveWrote { get; private set; }
        public static double QcLastReadBackX { get; private set; }

        /// <summary>
        /// QC — WHERE THE GESTURE THINKS THE OBJECT IS, and which array it read that from.
        ///
        /// 🔴 b392: the solve was right (along=4.689) and the write asked for x=-0.18, which is
        /// only possible if the press read the object at x=-4.87 — while the QC pass, reading the
        /// same id through <c>AppBoot.CurrentScene</c> in the same seconds, saw -0.18. One of the
        /// two is looking at a stale items array, and the hash says which: two different numbers
        /// are two different arrays, and then the drag is moving an object nobody is watching.
        /// </summary>
        public static double QcLastStartX { get; private set; }
        public static double QcLastBuiltX { get; private set; }
        public static int QcLastItemsHash { get; private set; }

        // ── input ────────────────────────────────────────────────────────────────

        private void Update()
        {
            // Editing belongs to the modes that look at the map from outside — View and Edit.
            // 🔴 WO-N: this used to be `!= AppMode.View`, which meant the 🛒 palette (the only
            // thing that enters Edit) silently deselected whatever the author had picked.
            if (!ModeRules.AllowsEditTools(ModeManager.Current))
            {
                if (_id != null) { SelectionToolbar.Hide(); Deselect(); }
                return;
            }
            if (_id == null) { GizmoHandles.Current?.Hide(); return; }

            UpdateHandles();

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

        /// <summary>
        /// Keep the arrows on the object and the right size, every frame.
        ///
        /// Only in Translate. The web draws rings for rotate and boxes for scale; we have neither
        /// yet, and showing translate arrows while the toolbar says ⟳ would be the exact lie this
        /// work order exists to avoid — the handles would not do what they look like they do.
        /// Rotate and scale keep the whole-screen drag they have always had.
        /// </summary>
        private void UpdateHandles()
        {
            GizmoHandles h = GizmoHandles.Ensure();
            if (_mode != SelectionToolbar.Mode.Translate) { h.Hide(); return; }

            Transform t = _dragging && _target != null ? _target : FindBuilt(_id);
            if (t == null) { h.Hide(); return; }
            h.ShowAt(t.position);
        }

        private void Press(Vector2 pos, int finger)
        {
            // A rejected press must DISARM the gesture, not just return. Returning early left
            // _pressPos and _startYaw holding the previous drag's values, so the next Move()
            // measured its delta from an old anchor and rotated from an old angle — the object
            // jumped, and which gesture it belonged to was anyone's guess.
            _armed = false;
            _dragging = false;
            _pressPos = pos;
            _finger = finger;
            QcLastPressed = GizmoMath.Handle.None;
            QcLastPressBlockedByUi = false;

            if (UiShell.PointerOverUi())
            {
                // a drag that starts on the toolbar is a button press
                QcLastPressBlockedByUi = true;
                return;
            }

            JObject item = CurrentItem();
            if (item == null) return;

            double[] p = ReadVec(item, "p", 0, 0, 0);
            double[] r = ReadVec(item, "r", 0, 0, 0);
            double[] s = ReadVec(item, "s", 1, 1, 1);

            _planeY = p[1];
            _startYaw = r[1];
            _startScale = s[0];
            _startPos = new[] { p[0], p[1], p[2] };

            // 🔴 WO-O — TOUCH ARBITRATION, the rule the whole feature hangs on.
            //
            // A press that lands ON a handle is a constrained drag. A press that lands anywhere
            // else must fall through to the camera exactly as before, or selecting an object
            // would freeze the orbit and the map would feel broken. So the handle is decided
            // ONCE, here, from the press position — never re-tested mid-drag, because a fast
            // finger leaves the 26 px shaft within two frames and re-testing would drop the
            // constraint halfway through the gesture.
            //
            // Only translate has handles; in rotate/scale this resolves to None and the old
            // whole-screen behaviour is what runs.
            _handle = _mode == SelectionToolbar.Mode.Translate && GizmoHandles.Current != null
                    ? GizmoHandles.Current.PickAt(pos)
                    : GizmoMath.Handle.None;
            GizmoHandles.Current?.SetHot(_handle);
            QcLastPressed = _handle;   // …and it survives Release, unlike _handle itself

            // Grab offset: keep the object under the same part of the finger it was grabbed by,
            // instead of snapping its centre to the touch point.
            if (PlaneHit(pos, _planeY, out double hx, out double hz))
            {
                _grabDx = p[0] - hx;
                _grabDz = p[2] - hz;
            }
            else { _grabDx = 0; _grabDz = 0; }

            // …and the same idea for the constrained handles, in their own coordinates.
            if (GizmoMath.IsAxis(_handle))
            {
                // Remember WHERE ALONG the axis the finger grabbed. The object then moves by the
                // CHANGE in that parameter, so grabbing the tip of the arrow does not snap the
                // object's centre out to the tip.
                GizmoMath.AxisOf(_handle, out double ux, out double uy, out double uz);
                _grabT = AxisAt(pos, p, ux, uy, uz, out bool okT) ? _axisT : 0.0;
                if (!okT) _handle = GizmoMath.Handle.None;   // edge-on: refuse rather than fling
                QcLastPressed = _handle;                     // …including the refusal
            }
            else if (GizmoMath.IsPlane(_handle))
            {
                GizmoMath.NormalOf(_handle, out double nx, out double ny, out double nz);
                if (PlaneHitN(pos, p, nx, ny, nz, out double qx, out double qy, out double qz))
                    _grabPos = new[] { p[0] - qx, p[1] - qy, p[2] - qz };
                else _grabPos = new double[] { 0, 0, 0 };
            }

            // Find the built object ONCE, and work out what one unit of JSON scale is worth on
            // it: SceneBuilder bakes the module's defaultScale in, so item.s is a multiplier on
            // that, not the final value. Re-deriving this mid-drag from the object we are
            // ourselves resizing would make it drift.
            _armed = true;
            _target = FindBuilt(_id);
            QcLastStartX = _startPos[0];
            QcLastBuiltX = _target != null ? _target.position.x : double.NaN;
            JArray pressItems = Items();
            QcLastItemsHash = pressItems != null ? pressItems.GetHashCode() : 0;
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
            if (_id == null || !_armed) return;
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
                    if (GizmoMath.IsAxis(_handle))
                    {
                        // 🔴 THE CONSTRAINT. One axis moves; the other two keep the values they
                        // had when the finger went down. This is the difference between a real
                        // axis handle and an arrow that is only decoration.
                        GizmoMath.AxisOf(_handle, out double ux, out double uy, out double uz);
                        bool solved = AxisAt(pos, _startPos, ux, uy, uz, out bool ok) && ok;
                        QcLastAxisOk = solved;
                        QcLastGrabT = _grabT;
                        QcLastAxisT = _axisT;
                        QcLastAlong = solved ? _axisT - _grabT : 0.0;
                        if (solved)
                        {
                            double along = _axisT - _grabT;
                            QcLastWroteX = _startPos[0] + ux * along;
                            QcLastMoveWrote = SceneEdit.Move(items, _id,
                                                             _startPos[0] + ux * along,
                                                             _startPos[1] + uy * along,
                                                             _startPos[2] + uz * along);
                            // What the array says IMMEDIATELY after the write. If this is the new
                            // value and the scene has the old one a second later, something after
                            // the gesture is putting it back — a different question entirely from
                            // "the write never happened", and the two are indistinguishable from
                            // outside (b391: along=4.689 computed, object unmoved).
                            JObject back = SceneEdit.Find(items, _id);
                            QcLastReadBackX = back != null && back["p"] != null
                                            ? (double)back["p"][0] : double.NaN;
                        }
                        // !ok = the axis is within half a degree of edge-on. Hold the last good
                        // position: a pixel of movement there would fling the object off the map.
                    }
                    else if (GizmoMath.IsPlane(_handle))
                    {
                        GizmoMath.NormalOf(_handle, out double nx, out double ny, out double nz);
                        if (PlaneHitN(pos, _startPos, nx, ny, nz,
                                      out double qx, out double qy, out double qz))
                            SceneEdit.Move(items, _id, qx + _grabPos[0], qy + _grabPos[1],
                                           qz + _grabPos[2]);
                    }
                    else if (PlaneHit(pos, _planeY, out double hx, out double hz))
                    {
                        // No handle grabbed: the original whole-screen slide, unchanged. Keeping
                        // it means a user who never notices the arrows can still move things.
                        SceneEdit.Move(items, _id, hx + _grabDx, _planeY, hz + _grabDz);
                    }
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
            bool wasDragging = _dragging;
            _dragging = false;
            _finger = -1;
            _armed = false;
            _handle = GizmoMath.Handle.None;
            GizmoHandles.Current?.SetHot(GizmoMath.Handle.None);
            MapEditor.Dragging = false;

            if (!wasDragging)
            {
                Debug.Log($"[Edit] {_mode} released with no drag (tap) for {_id}");
                return;
            }

            JArray items = Items();
            if (items == null) { Debug.LogWarning("[Edit] no scene to commit to"); return; }

            // Whether the snapshot was ACCEPTED matters: a refused push means undo will not
            // step back over this gesture, which is the difference between "it saved" and "it
            // looked like it saved".
            bool recorded = MapEditor.RecordAndApply(items);
            Debug.Log($"[Edit] {_mode} committed for {_id} recorded={recorded} " +
                      $"history={MapEditor.HistoryCount}");
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

        /// <summary>
        /// Axis parameter under the pointer, written to <see cref="_axisT"/>. Returns false when
        /// the axis is seen too close to end-on for the answer to mean anything.
        /// </summary>
        private bool AxisAt(Vector2 screenPos, double[] origin,
                            double ux, double uy, double uz, out bool ok)
        {
            ok = false;
            _axisT = 0.0;
            Camera cam = Camera.main;
            if (cam == null) return false;

            // The gizmo is drawn in UNITY space but the scene JSON is in WEB space, and the two
            // differ by a handedness flip (WebCoord). Do the geometry in Unity space, where the
            // camera ray lives, then convert the answer back — converting the ray instead would
            // mean maintaining a second, mirrored copy of the same transform.
            double[] uo = WebCoord.PositionToUnity(origin);
            Vec3 a = WebCoord.DirectionToUnity(ux, uy, uz);

            Ray ray = cam.ScreenPointToRay(screenPos);
            ok = GizmoMath.AxisParam(ray.origin.x, ray.origin.y, ray.origin.z,
                                     ray.direction.x, ray.direction.y, ray.direction.z,
                                     uo[0], uo[1], uo[2],
                                     a.X, a.Y, a.Z,
                                     out double t);
            _axisT = t;
            return ok;
        }

        /// <summary>Last axis parameter computed by <see cref="AxisAt"/>.</summary>
        private double _axisT;

        /// <summary>Ray hit on an arbitrary plane through a WEB-space point, in web space.</summary>
        private static bool PlaneHitN(Vector2 screenPos, double[] through,
                                      double nx, double ny, double nz,
                                      out double x, out double y, out double z)
        {
            x = y = z = 0;
            Camera cam = Camera.main;
            if (cam == null) return false;

            double[] up = WebCoord.PositionToUnity(through);
            Vec3 un = WebCoord.DirectionToUnity(nx, ny, nz);

            Ray ray = cam.ScreenPointToRay(screenPos);
            if (!GizmoMath.RayOnPlaneN(ray.origin.x, ray.origin.y, ray.origin.z,
                                       ray.direction.x, ray.direction.y, ray.direction.z,
                                       up[0], up[1], up[2], un.X, un.Y, un.Z,
                                       out double hx, out double hy, out double hz))
                return false;

            double[] w = WebCoord.PositionToWeb(new[] { hx, hy, hz });
            x = w[0]; y = w[1]; z = w[2];
            return true;
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

            Quat q = WebCoord.RotationToUnity(r);   // Quat is a DiveMap.Core type, not nested
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
