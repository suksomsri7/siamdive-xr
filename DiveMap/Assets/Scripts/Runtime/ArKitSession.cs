using System.Collections;
using System.Collections.Generic;
using DiveMap.Core;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;

namespace DiveMap.Runtime
{
    /// <summary>
    /// AR that knows where the room is — ARKit plane detection and tap-to-place, replacing the
    /// attitude-only view that could look around but never move.
    ///
    /// Why this exists: on a phone the old session could turn, and that was all. Walking towards
    /// the wreck did nothing, and the map could not be put on a table because nothing in the app
    /// knew a table was there. Both need ARKit's tracking, which is what this wires up.
    ///
    /// 🔑 The rule this project already learned the hard way: **scale the ORIGIN, never the map.**
    /// <c>WhaleController</c> writes world positions and <c>FishSchoolSystem</c> feeds world
    /// matrices to RenderMeshInstanced, so shrinking the map root leaves the animals full size and
    /// swimming through the furniture. <see cref="XROrigin"/> exists precisely to scale the
    /// relationship between the room and the world instead: one real metre becomes
    /// <see cref="_scale"/> world units, and every simulation keeps running at its own size.
    ///
    /// The placement maths, in one line — to make map point <c>C</c> appear at the tapped real
    /// point <c>P</c>:  <c>origin.position = C − (origin.rotation * P) * s</c>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArKitSession : MonoBehaviour
    {
        /// <summary>The model should read as about this big on the table, in metres.</summary>
        public const float TargetMetres = 1.1f;

        public static ArKitSession Instance { get; private set; }
        public static bool Running => Instance != null && Instance._running;

        private ARSession _session;
        private XROrigin _origin;
        private ARPlaneManager _planes;
        private ARRaycastManager _raycast;
        private ARCameraBackground _background;

        private Camera _cam;
        private Transform _camParent;
        private Vector3 _camPos;
        private Quaternion _camRot;
        private float _camNear, _camFar;
        private CameraClearFlags _camClear;
        private Color _camBg;

        private Vector3 _center;      // frame centre of the map, world space
        private float _span;          // widest side of the map, world units
        private float _scale = 1f;    // world units per real metre
        private bool _running;
        private bool _placed;

        private readonly List<ARRaycastHit> _hits = new List<ARRaycastHit>();

        /// <summary>
        /// Whether this device can do real AR. False on everything except an ARKit iPhone/iPad,
        /// which is exactly when the old attitude-only path should be used instead — a phone
        /// without tracking still gets a model it can look around, rather than an error.
        /// </summary>
        public static bool Supported
        {
            get
            {
#if UNITY_IOS && !UNITY_EDITOR
                bool ok = ARSession.state != ARSessionState.Unsupported;
                if (!ok) Ui.ArControls.SetDiagnostics("ARKit OFF: เครื่องไม่รองรับ (state=" + ARSession.state + ")");
                return ok;
#else
                Ui.ArControls.SetDiagnostics("ARKit OFF: build นี้ไม่ได้เปิด iOS path");
                return false;
#endif
            }
        }

        public static bool Begin(Vector3 center, float sizeX, float sizeZ)
        {
            if (!Supported) return false;
            if (Instance == null)
            {
                var go = new GameObject("ArKitSession");
                DontDestroyOnLoad(go);
                Instance = go.AddComponent<ArKitSession>();
            }
            return Instance.StartSession(center, Mathf.Max(sizeX, sizeZ));
        }

        public static void End() => Instance?.StopSession();

        // ── lifecycle ────────────────────────────────────────────────────────────

        private bool StartSession(Vector3 center, float span)
        {
            if (_running) return true;

            _cam = Camera.main;
            if (_cam == null) return false;

            _center = center;
            _span = Mathf.Max(1f, span);
            _scale = _span / TargetMetres;

            if (!InitialiseXr()) return false;

            BuildRig();
            StartCoroutine(WaitForTracking());
            _running = true;
            _placed = false;
            Debug.Log($"[ARKit] begin span={_span:F0} scale={_scale:F0} u/m target={TargetMetres} m");
            return true;
        }

        /// <summary>
        /// Bring the XR loader up on demand. The loader is deliberately NOT set to start with the
        /// app (XRGeneralSettings m_InitManagerOnStart = 0): this is an underwater game for all but
        /// a few seconds of its life, and an ARKit session running behind the map view would hold
        /// the camera, drain the battery and ask for a camera permission nobody wanted yet.
        /// </summary>
        private static bool InitialiseXr()
        {
            XRGeneralSettings settings = XRGeneralSettings.Instance;
            if (settings == null || settings.Manager == null)
            {
                Debug.LogWarning("[ARKit] no XR settings — falling back to the attitude session");
                Ui.ArControls.SetDiagnostics("ARKit OFF: ไม่พบการตั้งค่า XR");
                return false;
            }
            if (settings.Manager.activeLoader != null) return true;

            settings.Manager.InitializeLoaderSync();
            if (settings.Manager.activeLoader == null)
            {
                Debug.LogWarning("[ARKit] loader would not initialise — falling back");
                Ui.ArControls.SetDiagnostics("ARKit OFF: loader เริ่มไม่ได้");
                return false;
            }
            settings.Manager.StartSubsystems();
            return true;
        }

        private void BuildRig()
        {
            _session = gameObject.AddComponent<ARSession>();
            gameObject.AddComponent<ARInputManager>();

            var originGo = new GameObject("XROrigin");
            originGo.transform.SetParent(transform, false);
            _origin = originGo.AddComponent<XROrigin>();

            // The camera keeps its identity — every system in the app already holds Camera.main —
            // so it is BORROWED into the rig and handed back untouched on exit.
            _camParent = _cam.transform.parent;
            _camPos = _cam.transform.position;
            _camRot = _cam.transform.rotation;
            _camNear = _cam.nearClipPlane;
            _camFar = _cam.farClipPlane;
            _camClear = _cam.clearFlags;
            _camBg = _cam.backgroundColor;

            var offset = new GameObject("CameraOffset");
            offset.transform.SetParent(originGo.transform, false);
            _cam.transform.SetParent(offset.transform, false);
            _cam.transform.localPosition = Vector3.zero;
            _cam.transform.localRotation = Quaternion.identity;

            _origin.Camera = _cam;
            _origin.CameraFloorOffsetObject = offset;

            if (_cam.GetComponent<ARCameraManager>() == null) _cam.gameObject.AddComponent<ARCameraManager>();
            _background = _cam.GetComponent<ARCameraBackground>();
            if (_background == null) _background = _cam.gameObject.AddComponent<ARCameraBackground>();
            if (_cam.GetComponent<ARPoseDriver>() == null) _cam.gameObject.AddComponent<ARPoseDriver>();

            // Clip planes are in WORLD units and the origin is scaled, so a 0.5 u near plane would
            // sit 0.5/scale metres from the eye — millimetres. Scale them with the world.
            _cam.nearClipPlane = Mathf.Max(0.01f, 0.05f * _scale);
            _cam.farClipPlane = Mathf.Max(_cam.nearClipPlane * 1000f, _span * 6f);
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0f, 0f, 0f, 0f);

            _planes = originGo.AddComponent<ARPlaneManager>();
            _planes.requestedDetectionMode = PlaneDetectionMode.Horizontal;
            _raycast = originGo.AddComponent<ARRaycastManager>();

            // Park the world out of the way until the first tap: at scale, an unplaced map would
            // otherwise engulf the room.
            _origin.transform.localScale = Vector3.one * _scale;
            PlaceAt(new Vector3(0f, -1.2f, 1.5f), 0f);
        }

        private IEnumerator WaitForTracking()
        {
            float t0 = Time.realtimeSinceStartup;
            while (_running && ARSession.state != ARSessionState.SessionTracking &&
                   Time.realtimeSinceStartup - t0 < 20f)
            {
                yield return null;
            }
            Debug.Log($"[ARKit] tracking state={ARSession.state} after {Time.realtimeSinceStartup - t0:F1}s");
            if (_running && ARSession.state == ARSessionState.SessionTracking)
                Ui.ArControls.SetHint("เล็งกล้องไปที่พื้นเรียบ แล้วแตะเพื่อวางแผนที่");
        }

        // ── placing ──────────────────────────────────────────────────────────────

        private void Update()
        {
            if (!_running || _raycast == null) return;
            if (Input.touchCount == 0) return;

            Touch t = Input.GetTouch(0);
            if (t.phase != TouchPhase.Began) return;
            if (UnityEngine.EventSystems.EventSystem.current != null &&
                UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject(t.fingerId)) return;

            if (!_raycast.Raycast(t.position, _hits, TrackableType.PlaneWithinPolygon)) return;

            Pose hit = _hits[0].pose;
            // Face the map towards the viewer, upright: only the yaw of the camera is kept, because
            // a map tilted to match a phone held at an angle looks broken on a flat table.
            float yaw = _cam != null ? _cam.transform.eulerAngles.y : 0f;
            PlaceAt(hit.position, yaw);
            _placed = true;
            Ui.ArControls.SetHint("แตะที่พื้นอีกครั้งเพื่อย้าย");
            Debug.Log($"[ARKit] placed at {hit.position} yaw={yaw:F0}° scale={_scale:F0}");
        }

        /// <summary>
        /// Put the map's centre at a point in the ROOM. Everything else follows from the origin
        /// transform: world = origin.TransformPoint(session), so the origin has to be positioned
        /// such that transforming the tapped session point lands on the map centre.
        /// </summary>
        private void PlaceAt(Vector3 sessionPoint, float yawDeg)
        {
            if (_origin == null) return;
            Quaternion rot = Quaternion.Euler(0f, yawDeg, 0f);
            _origin.transform.rotation = rot;
            _origin.transform.localScale = Vector3.one * _scale;
            _origin.transform.position = _center - (rot * sessionPoint) * _scale;
        }

        /// <summary>One press of − / + : the same model, bigger or smaller on the table.</summary>
        public static void Zoom(bool closer)
        {
            ArKitSession s = Instance;
            if (s == null || !s._running) return;
            float next = Mathf.Clamp(s._scale * (closer ? 0.75f : 1.35f),
                                     s._span / 6f, s._span / 0.25f);
            if (Mathf.Approximately(next, s._scale)) return;
            s._scale = next;
            // Re-place around the same room point so the model grows where it stands.
            Vector3 sessionPoint = Quaternion.Inverse(s._origin.transform.rotation) *
                                   ((s._center - s._origin.transform.position) / s._scale);
            s.PlaceAt(sessionPoint, s._origin.transform.eulerAngles.y);
        }

        // ── leaving ──────────────────────────────────────────────────────────────

        private void StopSession()
        {
            if (!_running) return;
            _running = false;

            if (_cam != null)
            {
                _cam.transform.SetParent(_camParent, false);
                _cam.transform.SetPositionAndRotation(_camPos, _camRot);
                _cam.nearClipPlane = _camNear;
                _cam.farClipPlane = _camFar;
                _cam.clearFlags = _camClear;
                _cam.backgroundColor = _camBg;
                Destroy(_cam.GetComponent<ARPoseDriver>());
                Destroy(_cam.GetComponent<ARCameraBackground>());
                Destroy(_cam.GetComponent<ARCameraManager>());
            }

            if (_origin != null) Destroy(_origin.gameObject);
            if (_session != null) Destroy(_session);

            XRGeneralSettings settings = XRGeneralSettings.Instance;
            if (settings != null && settings.Manager != null && settings.Manager.activeLoader != null)
            {
                settings.Manager.StopSubsystems();
                settings.Manager.DeinitializeLoader();
            }
            Debug.Log($"[ARKit] end placed={_placed}");
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
