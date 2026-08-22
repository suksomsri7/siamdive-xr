using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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
        private ARAnchorManager _anchors;
        private ARInputManager _input;
        private ARCameraBackground _background;
        /// <summary>How many times AR has been entered this app run — the diagnostic shows it,
        /// because "works the first time, wrong the second" is a whole class of bug on its own.</summary>
        private int _sessions;
        private GameObject _planeTemplate, _templateHolder;

        private Camera _cam;
        private Transform _camParent;
        private Vector3 _camPos;
        private Quaternion _camRot;
        private float _camNear, _camFar;
        private CameraClearFlags _camClear;
        private Color _camBg;

        private Vector3 _center;      // frame centre of the map, world space
        private float _minY;          // lowest point of the map's content, world space
        private float _span;          // widest side of the map, world units
        private float _scale = 1f;    // world units per real metre
        private bool _running;

        // Where the map is standing, in SESSION space (metres, ARKit's own frame). Kept rather than
        // recovered from the origin transform: recovering it means inverting the very transform the
        // pinch is about to rewrite, and one arithmetic slip there moves the map instead of
        // resizing it. Two floats are cheaper than that class of bug.
        private Vector3 _spot;
        private float _yaw;
        private GameObject _mapRoot;

        private ArStep _step = ArStep.Searching;
        private ARAnchor _anchor;
        private ARPlane _spotPlane;

        // The live pinch: sampled when the second finger lands, not accumulated per frame.
        private bool _pinching;
        private double _pinchStartPixels, _pinchStartMetres;

        private readonly List<ARRaycastHit> _hits = new List<ARRaycastHit>();

        /// <summary>QC/UI surface — which step of "put it on the table" the user is on.</summary>
        public static ArStep Step => Instance != null ? Instance._step : ArStep.Searching;

        /// <summary>How wide the site currently reads on the table, in metres.</summary>
        public static double Metres =>
            Instance != null ? ArPinch.MetresFor(Instance._span, Instance._scale) : 0;

        /// <summary>
        /// Why ARKit is not running, kept so the fallback can show it.
        ///
        /// 🔴 The first attempt wrote this straight to the AR HUD — and ArSession's own per-frame
        /// readout ("gyro on · att …") overwrote it on the very next frame, so the reason was on
        /// screen for about 16 milliseconds and the screenshot came back showing the gyro line as
        /// if nothing had been added. A diagnostic that loses a race with the thing it is
        /// diagnosing is worse than none: it looks like the build did not change.
        /// </summary>
        public static string OffReason { get; private set; } = "";

        /// <summary>
        /// Which tracking provider this build is talking to — the log prefix, and the answer to
        /// "why does an Android log say ARKit". Everything in this class except the loader type
        /// and the availability check is AR Foundation, which is provider-agnostic; only these
        /// two spots ever knew the difference, so only they are platform-dependent.
        /// </summary>
        private const string Tag =
#if UNITY_ANDROID && !UNITY_EDITOR
            "[ARCore]";
#else
            "[ARKit]";
#endif

        private static bool Off(string reason)
        {
            OffReason = reason;
            Debug.LogWarning(Tag + " " + reason);
            return false;
        }

        /// <summary>
        /// Whether this device can do real AR. False on everything except an ARKit iPhone/iPad,
        /// which is exactly when the old attitude-only path should be used instead — a phone
        /// without tracking still gets a model it can look around, rather than an error.
        /// </summary>
        public static bool Supported
        {
            get
            {
#if (UNITY_IOS || UNITY_ANDROID) && !UNITY_EDITOR
                // 🔴 Android reaches this too now (ARCore, 18 ส.ค. 2026). The check itself did not
                // have to change: ARSession.state is AR Foundation's own answer and it is already
                // "Unsupported" on the many Android phones with no ARCore support — which is the
                // fallback this app has always had, so a phone without tracking still gets the
                // camera-and-gyro AR the web has, not an error.
                if (ARSession.state == ARSessionState.Unsupported)
                    return Off("ไม่รองรับ " + Tag + " (state=" + ARSession.state + ")");
                return true;
#else
                return Off("build นี้ไม่ได้คอมไพล์ path ของ iOS/Android");
#endif
            }
        }

        public static bool Begin(Vector3 center, float minY, float sizeX, float sizeZ, GameObject mapRoot)
        {
            if (!Supported) return false;
            if (Instance == null)
            {
                var go = new GameObject("ArKitSession");
                DontDestroyOnLoad(go);
                Instance = go.AddComponent<ArKitSession>();
            }
            Instance._mapRoot = mapRoot;
            Instance._minY = minY;
            return Instance.StartSession(center, Mathf.Max(sizeX, sizeZ));
        }

        /// <summary>
        /// The point of the map that lands on the tapped spot: the centre horizontally, but the
        /// BOTTOM of the content vertically.
        ///
        /// Not the frame centre. Putting the centre on the table buries the lower half of the site
        /// in the table — the web anchors on <c>box.min.y</c> for exactly this reason
        /// (builder.html:2923, <c>-box.min.y*sc</c>), and "it sits on the table" is the entire
        /// illusion this mode is selling.
        /// </summary>
        private Vector3 AnchorPoint => new Vector3(_center.x, _minY, _center.z);

        public static void End() => Instance?.StopSession();

        // ── lifecycle ────────────────────────────────────────────────────────────

        private bool StartSession(Vector3 center, float span)
        {
            if (_running) return true;

            _cam = Camera.main;
            if (_cam == null) return Off("ไม่มีกล้องหลัก");

            _center = center;
            _span = Mathf.Max(1f, span);
            _scale = _span / TargetMetres;

            if (!InitialiseXr()) return false;

            BuildRig();
            StartCoroutine(WaitForTracking());
            _running = true;
            _sessions++;
            SetStep(ArStep.Searching);
            Debug.Log($"[ARKit] begin span={_span:F0} scale={_scale:F0} u/m target={TargetMetres} m");
            return true;
        }

        /// <summary>
        /// Bring the XR loader up on demand. The loader is deliberately NOT set to start with the
        /// app (XRGeneralSettings m_InitManagerOnStart = 0): this is an underwater game for all but
        /// a few seconds of its life, and an ARKit session running behind the map view would hold
        /// the camera, drain the battery and ask for a camera permission nobody wanted yet.
        /// </summary>
        /// <summary>
        /// The XR loader this platform's tracking hides behind, as an assembly-qualified NAME.
        ///
        /// A name and not a type reference for the reason the old comment gave and which still
        /// holds on both sides now: each loader lives in a platform-only assembly, so naming
        /// either one directly would stop this file compiling on the Linux test/QC image where
        /// neither module exists.
        /// </summary>
        private const string LoaderTypeName =
#if UNITY_ANDROID && !UNITY_EDITOR
            "UnityEngine.XR.ARCore.ARCoreLoader, Unity.XR.ARCore";
#else
            "UnityEngine.XR.ARKit.ARKitLoader, Unity.XR.ARKit";
#endif

        private static XRManagerSettings _manager;

        private static bool InitialiseXr()
        {
            if (_manager != null && _manager.activeLoader != null)
            {
                // 🔴 16 ส.ค. 2026 — รอบสองเป็นต้นไปมาทางนี้ และเดิม "คืน true เฉย ๆ"
                // ซึ่งแปลว่า subsystem ที่ถูกหยุดไปตอนออกจาก AR ไม่เคยถูกสตาร์ทกลับ
                // ⇒ เซสชันใหม่ไม่มีกล้อง ไม่มี tracking = จอดำ (user รายงานซ้ำสองรอบ)
                _manager.StartSubsystems();
                Debug.Log("[ARKit] XR already loaded — subsystems restarted");
                return true;
            }

            // Path 1 — the settings asset, if Unity managed to deliver one through Preloaded
            // Assets (CIBuild.PreloadXrSettings puts it there). Its XRGeneralSettings.Awake sets
            // the static instance on the way in, which is the object AR Foundation looks at.
            XRGeneralSettings settings = XRGeneralSettings.Instance;
            string via;
            if (settings != null && settings.Manager != null && settings.Manager.activeLoaders.Count > 0)
            {
                _manager = settings.Manager;
                via = "settings asset";
            }
            else
            {
                // Path 2 — build the manager ourselves.
                _manager = ManagerInCode();
                if (_manager == null) return false;   // ManagerInCode already said why

                // 🔑 Owning the manager is not enough, and this is the part the earlier attempt
                // missed. Nothing in AR Foundation asks US for the loader: ARSession,
                // ARPlaneManager and ARRaycastManager all reach it through
                // XRGeneralSettings.Instance.Manager.activeLoader (see LoaderUtility and
                // SubsystemUtils in the package). A manager held in a private static of this class
                // is invisible to every one of them — the subsystems would come back null and AR
                // would fail one layer further down, looking like a different bug.
                if (settings == null) settings = PublishGlobalSettings();
                if (settings == null) return Off("สร้าง XRGeneralSettings ไม่ได้");
                settings.Manager = _manager;
                via = "code";
            }

            if (XRGeneralSettings.Instance == null || XRGeneralSettings.Instance.Manager == null)
                return Off("XRGeneralSettings.Instance ว่าง — ARFoundation จะหา loader ไม่เจอ");

            if (_manager.activeLoader == null) _manager.InitializeLoaderSync();
            if (_manager.activeLoader == null)
            {
                // 🔴 The failure that cost four TestFlight builds landed exactly here, and said
                // nothing useful. ARKitLoader.Initialize() asks the SubsystemManager for a
                // descriptor called "ARKit-Session"; that descriptor is registered by
                // ARKitSessionSubsystem.RegisterDescriptor(), which returns early unless
                // Api.AtLeast11_0() is true — and every DllImport in the ARKit package, that one
                // included, is compiled out unless UNITY_XR_ARKIT_LOADER_ENABLED is defined for
                // iOS. Without the define the call is a stub returning false, so the loader
                // declines with no error at all and the app falls back to the gyroscope.
                // CIBuild refuses to build without the define now; this message is here for the
                // day somebody builds from an editor that has not got it.
                return Off("loader เริ่มไม่ได้ — ไม่มี subsystem 'ARKit-Session' " +
                           "(มักแปลว่า build นี้ไม่มี define UNITY_XR_ARKIT_LOADER_ENABLED " +
                           "หรือไม่ได้ใส่ libUnityARKit.a)");
            }
            _manager.StartSubsystems();
            Debug.Log($"[ARKit] XR up via {via}: loader={_manager.activeLoader.GetType().Name}");
            OffReason = "";
            return true;
        }

        /// <summary>
        /// An <see cref="XRManagerSettings"/> with an ARKit loader in it, made at runtime.
        ///
        /// Reflection for the type, not a direct reference: ARKitLoader lives in an iOS-only
        /// assembly, so naming it would stop the Linux test and QC builds compiling at all.
        /// </summary>
        private static XRManagerSettings ManagerInCode()
        {
            var mgr = ScriptableObject.CreateInstance<XRManagerSettings>();
            System.Type loaderType = System.Type.GetType(LoaderTypeName);
            if (loaderType == null) { Off($"ไม่พบคลาส loader ({LoaderTypeName}) — build นี้ไม่มีโมดูลนั้น"); return null; }

            var loader = ScriptableObject.CreateInstance(loaderType) as XRLoader;
            if (loader == null) { Off("สร้าง loader ไม่ได้: " + LoaderTypeName); return null; }

            if (!AddLoader(mgr, loader)) { Off("เพิ่ม loader เข้า manager ไม่ได้"); return null; }
            Debug.Log("[ARKit] built an XR manager in code (no settings asset needed)");
            return mgr;
        }

        /// <summary>
        /// Put <paramref name="loader"/> into <paramref name="mgr"/>'s load order.
        ///
        /// 🔴 <c>TryAddLoader</c> alone cannot do this and returning false is all it will tell you
        /// — which is precisely what build 196 reported on screen ("เพิ่ม loader เข้า manager
        /// ไม่ได้"). XR Management keeps a second, private set of loaders it considers REGISTERED,
        /// filled by <c>Awake()</c> from the serialized list, and <c>TryAddLoader</c> refuses
        /// anything that is not already in it (outside the editor, where an extra branch registers
        /// on the fly). A manager created in code has an empty serialized list, so Awake registers
        /// nothing and every add is rejected: the API is deliberately immutable at runtime.
        ///
        /// So do what Awake would have done and then use the public API. Falling back to writing
        /// the serialized list directly keeps this working if the private field is ever renamed —
        /// the version check that matters is <see cref="XRManagerSettings.activeLoaders"/> at the
        /// end, which is public and is what everything downstream reads.
        /// </summary>
        private static bool AddLoader(XRManagerSettings mgr, XRLoader loader)
        {
            var registered = typeof(XRManagerSettings)
                .GetField("m_RegisteredLoaders", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(mgr) as ICollection<XRLoader>;
            if (registered != null && !registered.Contains(loader)) registered.Add(loader);

            if (!mgr.TryAddLoader(loader))
            {
                var list = typeof(XRManagerSettings)
                    .GetField("m_Loaders", BindingFlags.NonPublic | BindingFlags.Instance);
                if (list == null) return false;
                list.SetValue(mgr, new List<XRLoader> { loader });
            }

            IReadOnlyList<XRLoader> active = mgr.activeLoaders;
            return active != null && active.Count > 0 && active[0] == loader;
        }

        /// <summary>
        /// Make <see cref="XRGeneralSettings.Instance"/> exist so AR Foundation can find the loader
        /// through it. In a player <c>Awake()</c> assigns the static itself the moment the object is
        /// created; the setter is editor-only, so anywhere else the field is written directly.
        /// </summary>
        private static XRGeneralSettings PublishGlobalSettings()
        {
            var s = ScriptableObject.CreateInstance<XRGeneralSettings>();
            if (!ReferenceEquals(XRGeneralSettings.Instance, s))
            {
                typeof(XRGeneralSettings)
                    .GetField("s_Instance", BindingFlags.NonPublic | BindingFlags.Static)
                    ?.SetValue(null, s);
            }
            return XRGeneralSettings.Instance;
        }

        private void BuildRig()
        {
            // ARSession เป็น [DisallowMultipleComponent] เช่นกัน และตัวเดิมถูก Destroy ไปตอนออก
            // (Destroy เลื่อนไปปลายเฟรม) ⇒ ถ้าเข้า AR ซ้ำเร็วพอ AddComponent จะเงียบ ๆ ไม่สำเร็จ
            // แล้วเราจะถือ reference ที่เป็น null · หยิบของเดิมมาใช้ก่อนเสมอจึงปลอดภัยกว่า
            _session = gameObject.GetComponent<ARSession>();
            if (_session == null) _session = gameObject.AddComponent<ARSession>();
            // disable→enable เสมอ (ไม่ใช่แค่ enabled=true): ตัวที่ StopSession ปิดเก็บไว้ต้องได้
            // OnEnable รอบใหม่จริง ๆ เพื่อ start subsystem คืน — enabled=true บนตัวที่เปิดอยู่แล้ว
            // เป็น no-op เงียบ ๆ ซึ่งคือรูพรุนแบบเดียวกับที่ ARInputManager เคยโดน (คอมเมนต์ล่าง)
            else { _session.enabled = false; _session.enabled = true; }

            // 🔴 ARInputManager is [DisallowMultipleComponent], and this used to add one every
            // time without ever removing it. That single line is what "the first time is right,
            // the second time the floor is in the wrong place" was, reported three times:
            //
            //   • the second AddComponent silently fails and the FIRST instance stays
            //   • that instance grabbed its XRInputSubsystem in OnEnable — from the loader that
            //     StopSession has since deinitialised — and OnEnable never runs again
            //   • so the NEW session's input subsystem is never Start()ed
            //   • ARPoseDriver reads the camera pose from InputDevices, which that subsystem
            //     feeds, so the camera stops following the phone
            //   • and everything derived from the camera — where a tap ray goes, which way the map
            //     faces, where the detected plane appears relative to you — is then wrong
            //
            // Nothing about it looks like a leak: the session tracks, planes are found, the
            // numbers on screen are all plausible. Only the POSE is stale.
            _input = gameObject.GetComponent<ARInputManager>();
            if (_input == null) _input = gameObject.AddComponent<ARInputManager>();
            else { _input.enabled = false; _input.enabled = true; }   // force OnEnable to re-acquire

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

            // 🔴 CameraYOffset MUST be zero, and it is not zero by default.
            //
            // This is what "the map does not appear" was on build 201. XROrigin ships with
            // k_DefaultCameraYOffset = 1.1176 m — standing eye height, meant for room-scale VR —
            // and applies it to the floor-offset object whenever the tracking origin mode is
            // Device, which is the only mode ARKit reports. Everything else worked: planes were
            // found, the tap placed, the anchor held. The camera was simply 1.1176 m above where
            // the maths put it.
            //
            // And "1.1 m up" is not a small error here, because the origin is SCALED: at
            // _scale ≈ 300 u/m that is ~345 world units, more than the whole map is wide. The site
            // sat straight down out of the frustum while the phone pointed at the table.
            //
            // AR Foundation's own docs say this outright ("You should set the XROrigin.CameraYOffset
            // value to 0 … unless your app has a specific reason to do so", features/device-tracking.md)
            // — it is only ever set on the sample prefab, which a rig built in code never touches.
            // The mode is named rather than left NotSpecified so this does not depend on what the
            // subsystem happens to report on a given device.
            _origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;
            _origin.CameraYOffset = 0f;
            _origin.CameraFloorOffsetObject = offset;

            // 🔴 22 ส.ค. 2026 — "AR เข้าได้แค่รอบแรก รอบสองจอดำทันที" (b453 บนเครื่องจริง:
            // hint 'เจอพื้นแล้ว' ขึ้นบนจอดำสนิท = tracking ทำงาน กล้องไม่ถูกวาด)
            //
            // สามตัวนี้เคยใช้ `GetComponent == null ? AddComponent` คู่กับ Destroy ใน StopSession
            // ซึ่งคือกับดักเดียวกับที่คอมเมนต์บนหัวเมธอดนี้บันทึกไว้กับ ARSession/ARInputManager
            // ทุกตัวอักษร: Destroy มีผล**ปลายเฟรม** · การเข้า AR ซ้ำบนแมพเดิม (ArSession.Start:
            // "already in AR — restarting") ทำ End → Begin ใน**เฟรมเดียวกัน** ⇒ GetComponent
            // เจอซากที่ยังไม่ตาย ⇒ ไม่ Add ตัวใหม่ ⇒ ปลายเฟรมซากตายจริง ⇒ รอบสองไม่มี
            // ARCameraManager (จ่ายภาพ) / ARCameraBackground (วาดภาพ) / ARPoseDriver (ขับท่า
            // กล้อง) เลยสักตัว = จอดำ · ส่วน plane detection อยู่บน XROrigin ซึ่งสร้างใหม่
            // (`new GameObject`) ทุกรอบ จึงทำงานปกติ — ลายเซ็นตรงกับภาพ user เป๊ะ
            //
            // ทางแก้ = สูตรเดียวกับ _input ข้างบน: ปิดเก็บไว้ตอนออก (StopSession) แล้วปลุกที่นี่
            // ด้วย disable→enable เพื่อบังคับ OnEnable ให้ไปเกาะ subsystem ของ session รอบใหม่
            ARCameraManager camMgr = _cam.GetComponent<ARCameraManager>();
            if (camMgr == null) camMgr = _cam.gameObject.AddComponent<ARCameraManager>();
            else { camMgr.enabled = false; camMgr.enabled = true; }
            _background = _cam.GetComponent<ARCameraBackground>();
            if (_background == null) _background = _cam.gameObject.AddComponent<ARCameraBackground>();
            else { _background.enabled = false; _background.enabled = true; }
            ARPoseDriver pose = _cam.GetComponent<ARPoseDriver>();
            if (pose == null) _cam.gameObject.AddComponent<ARPoseDriver>();
            else { pose.enabled = false; pose.enabled = true; }

            // Clip planes are in WORLD units and the origin is scaled, so a 0.5 u near plane would
            // sit 0.5/scale metres from the eye — millimetres. Scale them with the world.
            _cam.nearClipPlane = Mathf.Max(0.01f, 0.05f * _scale);
            _cam.farClipPlane = Mathf.Max(_cam.nearClipPlane * 1000f, _span * 6f);
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0f, 0f, 0f, 0f);

            _planes = originGo.AddComponent<ARPlaneManager>();
            _planes.requestedDetectionMode = PlaneDetectionMode.Horizontal;
            _planes.planePrefab = BuildPlaneTemplate();
            _raycast = originGo.AddComponent<ARRaycastManager>();
            _anchors = originGo.AddComponent<ARAnchorManager>();

            // Nothing is placed yet, so nothing is shown. The old code parked the map 1.2 m below
            // and 1.5 m ahead, which put a reef in the middle of the room before the user had said
            // where it should go — and made the surfaces ARKit was still finding impossible to see
            // behind it. Hiding the map until the tap is what makes "detect the floor, then choose
            // a spot" a step the user can actually perform.
            _origin.transform.localScale = Vector3.one * _scale;
            _spot = new Vector3(0f, -1.2f, 1.5f);
            _yaw = 0f;
            ApplyPlacement();
            ShowMap(false);
        }

        /// <summary>
        /// The translucent patch drawn over each surface ARKit finds.
        ///
        /// It exists because the first step of the flow is a promise: "point at the floor and I
        /// will show you what I found". Without it the user is asked to tap a surface with no way
        /// to know whether one has been detected — which reads as the app ignoring taps.
        ///
        /// 🔑 The template is parked under an INACTIVE holder. AR Foundation copies the prefab's
        /// own <c>activeSelf</c> onto each instance (ARTrackableManager.CreateGameObjectDeactivated),
        /// so a template switched off would produce invisible planes; a template switched on and
        /// left loose in the scene would draw a stray quad at the origin. A parent that is off
        /// gives both: activeSelf stays true, activeInHierarchy does not.
        /// </summary>
        private GameObject BuildPlaneTemplate()
        {
            _templateHolder = new GameObject("ArPlaneTemplate");
            _templateHolder.transform.SetParent(transform, false);
            _templateHolder.SetActive(false);

            var go = new GameObject("ArPlane");
            go.transform.SetParent(_templateHolder.transform, false);
            go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            Material planeMat = PlaneMaterial();
            if (planeMat == null)
                Debug.LogWarning("[ARKit] no plane material — surfaces will be found but not shown, " +
                                 "so the first step of the flow will look like nothing is happening");
            mr.sharedMaterial = planeMat;
            // Adding the visualiser brings ARPlane with it ([RequireComponent]), in that order, so
            // the visualiser's Awake finds the plane component it needs.
            go.AddComponent<ARPlaneMeshVisualizer>();
            _planeTemplate = go;
            return go;
        }

        private static Material _planeMat;
        /// <summary>
        /// Alpha-blended, unlit-ish, on the DM_StandardTransparent base — the same recipe the god
        /// rays and the warp gate use. Deliberately NOT a shader of its own: this project has been
        /// bitten before by a custom shader being stripped from the build and every surface using
        /// it turning magenta on the device, which is the one place it cannot be tested.
        /// </summary>
        private static Material PlaneMaterial()
        {
            if (_planeMat != null) return _planeMat;
            Material src = Resources.Load<Material>("DM_StandardTransparent");
            var mat = src != null ? new Material(src) : null;
            if (mat == null || mat.shader == null) return null;

            if (mat.HasProperty("_Mode")) mat.SetFloat("_Mode", 3f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)(int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)(int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            mat.renderQueue = 3000;
            // The UI accent (#39b0e8, UI_PARITY.md) at low alpha: enough to see the shape of the
            // floor through the camera feed, not enough to hide what is on it.
            mat.color = new Color(0.224f, 0.690f, 0.910f, 0.22f);
            _planeMat = mat;
            return mat;
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
        }

        // ── placing ──────────────────────────────────────────────────────────────

        private void Update()
        {
            if (!_running) return;

            // Searching → Aiming the moment ARKit reports its first surface. Driven off the plane
            // count rather than a callback because the wording on screen has to keep up with what
            // the user can see, and what they can see is the blue patch appearing.
            if (_step == ArStep.Searching && _planes != null && _planes.trackables.count > 0)
                SetStep(ArStep.Aiming);

            HandlePinch();
            HandleTap();
            FollowAnchor();
        }

        /// <summary>
        /// The numbers that decide whether the map is where it should be — written to the log ONCE
        /// per step, not to the screen every frame.
        ///
        /// 🔎 These six lived on the AR HUD while the mode was being built, and earned it: they
        /// closed four bugs in four rounds that had previously cost five rounds of guessing
        /// (camera height, raycast space, accumulating yaw, a stale input manager). AR is the one
        /// feature with no CI, no console and no log a person can reach, so a photograph was the
        /// only channel there was.
        ///
        /// They come off the screen now that it works, because the point of the mode is the room —
        /// asked for directly, and right. They are NOT deleted: the same line goes to the device
        /// log, which is reachable from Xcode when a device is to hand, and one call to
        /// <c>ArControls.SetDiagnostics(Status())</c> puts it back on the HUD if a screenshot is
        /// ever the only channel again.
        /// </summary>
        public string Status()
        {
            if (_cam == null || _origin == null) return "ARKit " + ARSession.state;
            float off = _origin.CameraFloorOffsetObject != null
                ? _origin.CameraFloorOffsetObject.transform.localPosition.y : -1f;
            Vector3 toMap = AnchorPoint - _cam.transform.position;
            float dist = _scale > 0f ? toMap.magnitude / _scale : -1f;
            // Distance alone cannot tell "half a metre in front of me" from "half a metre behind
            // my shoulder", and those need opposite fixes. The angle off the view axis can:
            // 0° is dead ahead, past ~40° it is outside the frame, 180° is behind you.
            float offAxis = toMap.sqrMagnitude > 1e-6f
                ? Vector3.Angle(_cam.transform.forward, toMap) : 0f;
            int planes = _planes != null ? _planes.trackables.count : -1;
            return $"ARKit {ARSession.state} #{_sessions} · {_step} · planes {planes} · " +
                   $"size {ArPinch.MetresFor(_span, _scale):F2} m · scale {_scale:F0} u/m · off {off:F2} m · " +
                   $"map {(_mapRoot == null ? "NULL" : (_mapRoot.activeSelf ? "on" : "off"))} · " +
                   $"eye→centre {dist:F2} m @ {offAxis:F0}°";
        }

        /// <summary>
        /// Two fingers resize the site. Available from the moment it is placed, including after
        /// confirming — the anchor pins WHERE it is, and changing your mind about how big it should
        /// be is not a reason to start over.
        /// </summary>
        private void HandlePinch()
        {
            if (_step == ArStep.Searching || _step == ArStep.Aiming) { _pinching = false; return; }
            if (Input.touchCount < 2) { _pinching = false; return; }

            Touch a = Input.GetTouch(0), b = Input.GetTouch(1);
            double pixels = Vector2.Distance(a.position, b.position);

            if (!_pinching)
            {
                _pinching = true;
                _pinchStartPixels = pixels;
                _pinchStartMetres = ArPinch.MetresFor(_span, _scale);
                return;
            }

            double metres = ArPinch.Pinch(_pinchStartMetres, _pinchStartPixels, pixels);
            var next = (float)ArPinch.ScaleFor(_span, metres);
            if (next <= 0f || Mathf.Approximately(next, _scale)) return;

            _scale = next;
            ApplyPlacement();      // about the SAME room point, so it grows where it stands
            Ui.ArControls.SetSize(metres, ArPinch.AtLimit(metres));
        }

        /// <summary>One finger picks the spot — before the confirm, and again after it if the user
        /// taps somewhere else, which quietly drops the old anchor and starts a new one.</summary>
        private void HandleTap()
        {
            if (_raycast == null || Input.touchCount != 1) return;

            Touch t = Input.GetTouch(0);
            if (t.phase != TouchPhase.Began) return;
            if (UnityEngine.EventSystems.EventSystem.current != null &&
                UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject(t.fingerId)) return;

            if (!_raycast.Raycast(t.position, _hits, TrackableType.PlaneWithinPolygon))
            {
                if (_step == ArStep.Aiming) Ui.ArControls.SetHint(UiStrings.Tr("ยังไม่เจอพื้นตรงนั้น — เล็งกล้องไปที่พื้นเรียบ"));
                return;
            }

            ARRaycastHit hit = _hits[0];

            // 🔴 sessionRelativePose, NOT pose. This one line was "the map does not appear", twice.
            //
            // ARRaycastHit.pose is documented as "in Unity WORLD space" — it is
            // `TrackablesParent.TransformPose(sessionRelativePose)`, so it has already been through
            // the very origin transform this method is about to rewrite. Feeding it back in as if
            // it were session space squares the transform: the origin lands at
            // AnchorPoint − worldPoint × scale, which at scale ≈ 550 is already kilometres out, and
            // every further tap multiplies by another 550. The phone reported it as
            // `eye→centre 2,719,509,000.00 m`.
            //
            // Nothing about it looks wrong at the call site — both are a Pose of the point you
            // tapped, in metres, and both read correctly in a debugger. Only the SPACE differs.
            // The same distinction is why Confirm() converts back to world for AttachAnchor and
            // why FollowAnchor reads the anchor through InverseTransformPoint.
            Vector3 spot = hit.sessionRelativePose.position;

            // ARKit's session frame is a room, not a planet. Anything past this is a space mix-up
            // or a tracking fault, and letting it reach the transform destroys float precision for
            // the rest of the session — better to refuse the tap and say so.
            if (!(spot.magnitude < MaxSessionMetres))
            {
                Debug.LogWarning($"[ARKit] refusing an implausible tap at {spot} " +
                                 $"({spot.magnitude:F0} m from the session origin)");
                Ui.ArControls.SetHint(UiStrings.Tr("ยังไม่เจอพื้นตรงนั้น — เล็งกล้องไปที่พื้นเรียบ"));
                return;
            }

            // Face the map towards the viewer, upright: only the yaw of the camera is kept, because
            // a map tilted to match a phone held at an angle looks broken on a flat table.
            _spot = spot;
            _yaw = SessionYaw();
            _spotPlane = hit.trackable as ARPlane;

            DropAnchor();          // moving invalidates whatever we were pinned to
            ApplyPlacement();
            ShowMap(true);
            SetStep(ArStep.Adjusting);
            Debug.Log($"[ARKit] placed at {_spot} yaw={_yaw:F0}° " +
                      $"metres={ArPinch.MetresFor(_span, _scale):F2} plane={(_spotPlane != null ? "yes" : "no")}");
        }

        /// <summary>
        /// ✓ — pin the site to the room with an ARAnchor.
        ///
        /// 🔑 This is what fixes "the object does not stay still when I walk". Without an anchor the
        /// map hangs off ARKit's origin, and ARKit revises that origin as it learns the room — so
        /// the wreck slides a few centimetres every time the tracker corrects itself. An anchor is
        /// a promise from ARKit to keep ONE point pinned to the physical surface and to move it
        /// with every correction; attaching it to the plane that was tapped (rather than to a bare
        /// pose) is stronger still, because the plane is a thing ARKit keeps re-measuring.
        /// </summary>
        public static void Confirm()
        {
            ArKitSession s = Instance;
            if (s == null || !s._running || s._step == ArStep.Searching || s._step == ArStep.Aiming) return;

            s.DropAnchor();
            if (s._anchors != null && s._spotPlane != null)
            {
                // AttachAnchor takes a pose "in Unity world space" and does the inverse transform
                // itself — the mirror image of the raycast, and the other half of the same trap.
                s._anchor = s._anchors.AttachAnchor(
                    s._spotPlane, new Pose(s.ToWorld(s._spot), Quaternion.identity));
            }

            s.SetStep(ArStep.Anchored);
            Debug.Log($"[ARKit] confirmed — anchor={(s._anchor != null ? s._anchor.trackableId.ToString() : "NONE (plane lost)")} " +
                      $"metres={ArPinch.MetresFor(s._span, s._scale):F2}");
            if (s._anchor == null)
                Ui.Toast.ShowTr("ยึดกับพื้นไม่ได้ — วางไว้ตรงนี้ก่อน");
        }

        /// <summary>Reopen the adjustment step (the "ย้าย/ปรับ" button once anchored).</summary>
        public static void Adjust()
        {
            ArKitSession s = Instance;
            if (s == null || !s._running || s._step != ArStep.Anchored) return;
            s.DropAnchor();
            s.SetStep(ArStep.Adjusting);
        }

        private void DropAnchor()
        {
            if (_anchor == null) return;
            if (_anchors != null) _anchors.TryRemoveAnchor(_anchor);
            _anchor = null;
        }

        /// <summary>
        /// Once anchored, the ANCHOR decides where the map is — every frame.
        ///
        /// The direction of this is the whole point and is easy to get backwards. We do not move the
        /// anchor to the map; we read where ARKit currently believes the anchor is and rebuild the
        /// origin from it. So when the tracker corrects itself — which it does constantly, and more
        /// as you walk — the map is corrected with the room instead of drifting away from it.
        ///
        /// Read in SESSION space, not world space. The anchor's GameObject is parented under the
        /// origin we are about to rewrite, so using its world position would feed the transform its
        /// own output and the map would never move at all.
        /// </summary>
        private void FollowAnchor()
        {
            if (_step != ArStep.Anchored || _anchor == null || _origin == null) return;
            Vector3 spot = ToSession(_anchor.transform.position);
            if (!(spot.magnitude < MaxSessionMetres)) return;   // a lost anchor must not move the map
            _spot = spot;
            ApplyPlacement();
        }

        /// <summary>How far from the session origin a real room point can plausibly be, in metres.</summary>
        private const float MaxSessionMetres = 200f;

        /// <summary>
        /// Which way the viewer is facing, IN SESSION SPACE.
        ///
        /// 🔴 This used to read <c>_cam.transform.eulerAngles.y</c>, and it is the same trap as
        /// the raycast pose: the camera is a child of the origin, so its world yaw is already
        /// `_yaw + the session yaw`. Feeding that back in adds the current rotation to itself on
        /// every tap — the first placement is right, the second is off by the angle you were
        /// facing, the third by twice that. Reported as "the first time it worked, the second time
        /// it put the floor in the wrong place", and it moves the map as well as turning it,
        /// because the position is derived through the same rotation.
        ///
        /// Taken from the forward vector rather than an Euler angle on purpose. Euler decomposition
        /// is unstable exactly when the phone is pitched steeply — which is the normal way to hold
        /// it when you are placing something on the floor, i.e. the case that has to work.
        /// </summary>
        private float SessionYaw()
        {
            if (_cam == null) return _yaw;
            Transform t = SessionSpace;
            Vector3 fwd = t == null ? _cam.transform.forward
                                    : t.InverseTransformDirection(_cam.transform.forward);
            var flat = new Vector2(fwd.x, fwd.z);
            if (flat.sqrMagnitude < 1e-6f)
            {
                // Straight down or straight up: forward has no horizontal part, so the top of the
                // screen is what "facing" means.
                Vector3 up = t == null ? _cam.transform.up : t.InverseTransformDirection(_cam.transform.up);
                flat = new Vector2(up.x, up.z);
            }
            if (flat.sqrMagnitude < 1e-6f) return _yaw;
            return Mathf.Atan2(flat.x, flat.y) * Mathf.Rad2Deg;   // 0° = +Z, Unity's convention
        }

        /// <summary>
        /// The transform between ARKit's session space (metres, room-sized) and Unity world space
        /// (map units, scaled). Every value crossing that line goes through here, because the two
        /// are the same numbers in different units and nothing at a call site distinguishes them.
        /// </summary>
        private Transform SessionSpace => _origin == null ? null
            : (_origin.TrackablesParent != null ? _origin.TrackablesParent : _origin.transform);

        private Vector3 ToSession(Vector3 world)
        {
            Transform t = SessionSpace;
            return t == null ? world : t.InverseTransformPoint(world);
        }

        private Vector3 ToWorld(Vector3 session)
        {
            Transform t = SessionSpace;
            return t == null ? session : t.TransformPoint(session);
        }

        /// <summary>
        /// Put the map's centre at <see cref="_spot"/> in the ROOM. Everything else follows from the
        /// origin transform: world = origin.TransformPoint(session), so the origin has to be placed
        /// such that transforming the chosen session point lands on the map centre.
        /// </summary>
        private void ApplyPlacement()
        {
            if (_origin == null) return;
            Quaternion rot = Quaternion.Euler(0f, _yaw, 0f);
            _origin.transform.rotation = rot;
            _origin.transform.localScale = Vector3.one * _scale;
            _origin.transform.position = AnchorPoint - (rot * _spot) * _scale;
        }

        private void ShowMap(bool visible)
        {
            if (_mapRoot != null && _mapRoot.activeSelf != visible) _mapRoot.SetActive(visible);
        }

        private void SetStep(ArStep step)
        {
            _step = step;

            // The blue patches were guidance. Once the site is pinned they are clutter lying under
            // the thing the user came to look at — so they go, while detection keeps running (the
            // manager stays enabled) because a tap must still be able to move the map afterwards.
            if (_planes != null) _planes.SetTrackablesActive(step != ArStep.Anchored);

            Ui.ArControls.SetStep(step, ArPinch.MetresFor(_span, _scale));
            Debug.Log("[ARKit] " + Status());
        }

        // ── leaving ──────────────────────────────────────────────────────────────

        private void StopSession()
        {
            if (!_running) return;
            _running = false;

            // The map was ours to hide, so it is ours to give back. Leaving AR with the root still
            // switched off is the map-view equivalent of a black screen — and it would have no
            // error attached to it, because from every other system's point of view the scene
            // loaded correctly.
            ShowMap(true);
            DropAnchor();
            _spotPlane = null;
            _pinching = false;
            if (_templateHolder != null) { Destroy(_templateHolder); _templateHolder = null; _planeTemplate = null; }

            if (_cam != null)
            {
                _cam.transform.SetParent(_camParent, false);
                _cam.transform.SetPositionAndRotation(_camPos, _camRot);
                _cam.nearClipPlane = _camNear;
                _cam.farClipPlane = _camFar;
                _cam.clearFlags = _camClear;
                _cam.backgroundColor = _camBg;
                // 🔴 ห้าม Destroy สามตัวนี้ (22 ส.ค. 2026) — เหตุผลเดียวกับ _input ด้านล่าง:
                // Destroy มีผลปลายเฟรม แล้วขาเข้ารอบถัดไป (BuildRig) จะเจอซากผ่าน GetComponent
                // และไม่สร้างตัวใหม่ ⇒ "AR รอบสองจอดำ" ตามที่ user รายงาน · ปิดเก็บไว้แทน
                // (ปิดแล้วไม่กินกล้อง ไม่วาด ไม่ขับท่า) BuildRig จะปลุกด้วย disable→enable
                ARPoseDriver pd = _cam.GetComponent<ARPoseDriver>();
                if (pd != null) pd.enabled = false;
                ARCameraBackground bg = _cam.GetComponent<ARCameraBackground>();
                if (bg != null) bg.enabled = false;
                ARCameraManager cm = _cam.GetComponent<ARCameraManager>();
                if (cm != null) cm.enabled = false;
            }

            if (_origin != null) Destroy(_origin.gameObject);
            // 🔴 22 ส.ค. 2026 — ARSession เลิก Destroy ด้วยเหตุผลเดียวกับสามตัวบนกล้องข้างบน:
            // Destroy มีผลปลายเฟรม · การออก-เข้า AR ในเฟรมเดียว (เข้าซ้ำแมพเดิม / คำสั่ง
            // mode:view + mode:ar มาติดกันในคิวเดียว) ทำให้ BuildRig รอบใหม่หยิบ**ซาก**ผ่าน
            // GetComponent แล้วไม่สร้างตัวใหม่ ⇒ รอบสองไม่มี ARSession = ไม่มีใคร start
            // subsystem กล้อง = จอดำ (b457 พิสูจน์ว่าแก้แค่สามตัวกล้องไม่พอ — ตัวแม่ก็โดน)
            if (_session != null) _session.enabled = false;
            // 🔴 ไม่ทำลาย ARInputManager: มันเป็น [DisallowMultipleComponent] และ OnEnable ของมัน
            // คือจังหวะเดียวที่จะไปหยิบ input subsystem มาถือ — ทำลายแล้วสร้างใหม่ในรอบถัดไป
            // เคยทำให้ท่ากล้องค้าง (ดูหมายเหตุยาวใน BuildRig) · ปิดไว้เฉย ๆ แล้วเปิดใหม่ปลอดภัยกว่า
            if (_input != null) _input.enabled = false;

            // Every field that held a component of the rig, cleared. A destroyed Unity object
            // compares equal to null, so leaving them set is survivable — but the ONE that was not
            // destroyed was invisible precisely because everything else in this list was, and the
            // next person reading it could not tell the difference.
            _session = null; _origin = null; _planes = null; _raycast = null;
            _anchors = null; _background = null;   // _input ถูกเก็บไว้ใช้ต่อ (ดูด้านบน)

            // 🔴 ห้าม DeinitializeLoader (16 ส.ค. 2026 — ARKit ไม่รองรับ initialize ซ้ำในโปรเซส)
            //
            // 🔴 และตั้งแต่ 22 ส.ค. 2026 (รอบ 7): **เลิกเรียก StopSubsystems เองด้วย** —
            // lifecycle เป็นของ component ทางเดียว. เดิมเราทำสองทางพร้อมกัน: ปิด component
            // (ARSession/ARCameraManager/ฯลฯ ซึ่ง OnDisable ของมัน stop subsystem ของตัวเอง
            // อยู่แล้วตามดีไซน์ AR Foundation) แล้วยัง StopSubsystems ทับอีกชั้น ⇒ ตอนกลับเข้า
            // AR รอบสอง OnEnable ของ component ปลุก subsystem ของตัวเองคืน แต่สถานะที่ถูกหยุด
            // "จากข้างนอก" ไม่ถูกนับรวม — ผลบนเครื่องจริง (b461): session #2 วิ่ง เจอ plane
            // จากแผนที่เดิม แต่ state ค้าง SessionInitializing และภาพกล้องไม่มา = จอดำ
            // ผู้จัดการสองคนสั่งเครื่องเดียวกัน ได้สถานะที่ไม่มีใครรู้จัก — เหลือคนเดียวพอ
            Debug.Log($"[ARKit] end step={_step} (subsystems พักโดย component เอง)");
            _step = ArStep.Searching;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
