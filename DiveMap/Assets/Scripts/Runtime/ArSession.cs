using System.Collections;
using DiveMap.Core;
using DiveMap.Runtime.Ui;
using UnityEngine;

namespace DiveMap.Runtime
{
    /// <summary>
    /// F1/F4 — AR: the dive site standing on a real table, seen through the phone's back camera.
    ///
    /// The web's <c>enterAR()</c> (builder.html:2923) does five things, and so does this: get the
    /// camera, get the attitude sensor, put the site an arm's length away at tabletop size, strip
    /// the underwater world (seabed, surface, backdrop, fog) so the room shows through, and swap
    /// the controls. Leaving undoes all five.
    ///
    /// 🔎 Two deliberate departures from the web, both explained where the code makes them:
    ///   • the CAMERA moves, the map does not (<see cref="ArPlacement"/>) — because this port's
    ///     whales and schools simulate in world space and would keep their full size if the map
    ///     were scaled under them.
    ///   • no ARCore. The web uses a plain camera feed and the attitude sensor, which is what this
    ///     matches; ARCore would add plane detection and real translation, but it also needs XR
    ///     settings that live in ProjectSettings/, and this repo does not track that folder
    ///     (IMPROVEMENTS A2) — so it would work on the machine that configured it and silently
    ///     fall back to nothing everywhere else. Recorded as the upgrade path, not smuggled in.
    ///
    /// ⚠️ What CI can prove about this file is limited and the QC block says so out loud: mode
    /// entry/exit, the placement maths, and that every borrowed piece of the scene is given back.
    /// The feed and the sensor need a phone.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArSession : MonoBehaviour
    {
        public static ArSession Instance { get; private set; }

        /// <summary>Is an AR session on screen right now.</summary>
        public static bool Active => Instance != null && Instance._active;

        /// <summary>Metres per world unit at the moment — what the − / + buttons change.</summary>
        public static double Scale => Instance != null ? Instance._scale : 0;

        /// <summary>The fitted (tabletop) scale for this map, i.e. where zoom starts.</summary>
        public static double FitScale => Instance != null ? Instance._fit : 0;

        // Where the map is, from the last build.
        private Vector3 _center;
        private float _sizeX = 100f, _sizeZ = 100f, _minY;

        private bool _active;
        private double _fit, _scale;

        private Camera _cam;
        private OrbitCamera _orbit;
        private Transform _feed;
        private WebCamTexture _webcam;
        private bool _gyro;

        // Everything borrowed from the scene, so Restore() can be exact rather than approximate.
        private CameraClearFlags _clearFlags;
        private Color _bgColor;
        private float _near, _far;
        private Vector3 _camPos;
        private Quaternion _camRot;
        private bool _fog;
        private GameObject _seabed, _water;
        private bool _seabedWas, _waterWas;
        private Backdrop _backdrop;
        private bool _orbitWas;

        public static ArSession Ensure()
        {
            if (Instance != null) return Instance;
            ModeManager mm = ModeManager.Instance;
            if (mm == null) return null;
            Instance = mm.gameObject.AddComponent<ArSession>();
            return Instance;
        }

        /// <summary>Called after every map build — AR needs the footprint to place the viewer.</summary>
        public static void Configure(SceneBuilder.BuildResult r)
        {
            ArSession s = Ensure();
            if (s == null) return;
            s._center = r.FrameCenter;
            s._sizeX = r.FrameSizeX;
            s._sizeZ = r.FrameSizeZ;
            s._minY = r.FrameMinY;
        }

        /// <summary>Enter AR. False when the mode change is refused (e.g. mid-tour).</summary>
        public static bool Start()
        {
            ArSession s = Ensure();
            if (s == null || ModeManager.Instance == null) return false;
            return ModeManager.Instance.Request(AppMode.Ar);
        }

        private void Awake()
        {
            Instance = this;
            if (ModeManager.Instance != null) ModeManager.Instance.Changed += OnModeChanged;
        }

        private void OnDestroy()
        {
            if (ModeManager.Instance != null) ModeManager.Instance.Changed -= OnModeChanged;
            if (_active) Restore();
            if (Instance == this) Instance = null;
        }

        private void OnModeChanged(AppMode prev, AppMode next)
        {
            if (next == AppMode.Ar && !_active) Begin();
            else if (next != AppMode.Ar && _active) Restore();
        }

        // ── entering ─────────────────────────────────────────────────────────────

        private void Begin()
        {
            _cam = Camera.main;
            if (_cam == null) { Debug.LogWarning("[AR] no camera"); ModeManager.Instance.Exit(); return; }

            _active = true;

            _fit = ArPlacement.FitScale(_sizeX, _sizeZ);
            _scale = _fit;

            // Borrow the scene before changing any of it.
            _clearFlags = _cam.clearFlags;
            _bgColor = _cam.backgroundColor;
            _near = _cam.nearClipPlane;
            _far = _cam.farClipPlane;
            _camPos = _cam.transform.position;
            _camRot = _cam.transform.rotation;
            _fog = RenderSettings.fog;

            _orbit = _cam.GetComponent<OrbitCamera>();
            _orbitWas = _orbit != null && _orbit.enabled;

            HideUnderwaterWorld();

            // The room shows through wherever nothing is drawn.
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            RenderSettings.fog = false;

            ApplyPlacement();
            StartCoroutine(StartCameraFeed());

            // The sensor is what makes it AR rather than a picture. Without one the web falls back
            // to drag-to-orbit, and so does this — a phone with no gyroscope still gets a usable
            // model on the table instead of an error.
            _gyro = SystemInfo.supportsGyroscope;
            if (_gyro)
            {
                Input.gyro.enabled = true;
                if (_orbit != null) _orbit.enabled = false;
            }
            else if (_orbit != null)
            {
                _orbit.enabled = true;
                _orbit.target = _center;
                _orbit.minDistance = 0.05f;
                _orbit.distance = (float)(ArPlacement.Distance / _scale);
                Toast.ShowTr("เครื่องนี้ไม่มีเซนเซอร์ — ลากเพื่อหมุนแทน");
            }

            ArControls.Open();
            Debug.Log($"[AR] begin span=({_sizeX:F0},{_sizeZ:F0}) fit={_fit:F5} " +
                      $"gyro={_gyro} eye={_cam.transform.position}");
        }

        private void HideUnderwaterWorld()
        {
            // The web: `seabed.visible=false; surf.visible=false; scene.background=null;`
            GameObject root = GameObject.Find("Map");
            if (root != null)
            {
                Transform sb = root.transform.Find("Seabed");
                Transform w = root.transform.Find("Water");
                if (sb != null) { _seabed = sb.gameObject; _seabedWas = _seabed.activeSelf; _seabed.SetActive(false); }
                if (w != null) { _water = w.gameObject; _waterWas = _water.activeSelf; _water.SetActive(false); }
            }

            _backdrop = _cam.GetComponent<Backdrop>();
            if (_backdrop != null) _backdrop.SetVisible(false);
        }

        /// <summary>Put the eye where the site reads as a tabletop model, and clip to suit.</summary>
        private void ApplyPlacement()
        {
            Vec3 eye = ArPlacement.CameraPosition(
                new Vec3(_center.x, _center.y, _center.z), _minY, _scale);
            _cam.transform.position = new Vector3((float)eye.X, (float)eye.Y, (float)eye.Z);
            if (!_gyro) _cam.transform.LookAt(_center);

            ArPlacement.Clipping(Mathf.Max(_sizeX, _sizeZ), _scale, out double near, out double far);
            _cam.nearClipPlane = (float)near;
            _cam.farClipPlane = (float)far;

            if (_orbit != null && _orbit.enabled)
                _orbit.distance = (float)(ArPlacement.Distance / _scale);
        }

        /// <summary>One press of − or + (the web's <c>arMinus</c>/<c>arPlus</c>).</summary>
        public void Zoom(bool closer)
        {
            if (!_active) return;
            double next = ArPlacement.Zoom(_scale, _fit, closer);
            if (System.Math.Abs(next - _scale) < 1e-12)
            {
                Toast.ShowTr(closer ? "ใหญ่สุดแล้ว" : "เล็กสุดแล้ว");
                return;
            }
            _scale = next;
            ApplyPlacement();
            ArControls.Refresh();
        }

        // ── the camera feed ──────────────────────────────────────────────────────

        private IEnumerator StartCameraFeed()
        {
            // Android 6+ hands out camera access at runtime, not at install. Asking mid-session is
            // the only correct time: asking at startup for a feature almost nobody opens is how
            // apps get denied permanently.
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                    UnityEngine.Android.Permission.Camera))
            {
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera);
                float waited = 0f;
                while (waited < 20f &&
                       !UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                           UnityEngine.Android.Permission.Camera))
                {
                    waited += Time.unscaledDeltaTime;
                    yield return null;
                }
            }
#endif
            yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);

            if (!_active) yield break;   // the user left while the dialog was up

            WebCamDevice[] devices = WebCamTexture.devices;
            if (devices == null || devices.Length == 0)
            {
                // The web alerts and backs out entirely. This keeps the model on screen instead —
                // still worth looking at over a black background, and the user chose to be here.
                Debug.LogWarning("[AR] no camera device — model over black");
                Toast.ShowTr("เปิดกล้องไม่ได้ — แสดงแบบจำลองอย่างเดียว");
                _cam.backgroundColor = Color.black;
                yield break;
            }

            // Prefer the BACK camera: a selfie feed with a reef floating in it is not AR.
            string pick = devices[0].name;
            foreach (WebCamDevice d in devices)
            {
                if (!d.isFrontFacing) { pick = d.name; break; }
            }

            _webcam = new WebCamTexture(pick, 1280, 720, 30);
            _webcam.Play();

            float t0 = Time.realtimeSinceStartup;
            while (_webcam.width <= 16 && Time.realtimeSinceStartup - t0 < 5f) yield return null;

            if (!_active) { StopFeed(); yield break; }

            BuildFeedQuad();
            Debug.Log($"[AR] feed {pick} {_webcam.width}x{_webcam.height} " +
                      $"rot={_webcam.videoRotationAngle} mirrored={_webcam.videoVerticallyMirrored}");
        }

        /// <summary>
        /// The feed is a quad parented to the camera, just inside the far plane — the same trick
        /// <see cref="Backdrop"/> uses. It rides the camera, which is correct: the video IS what
        /// the camera is pointing at, so it must not lag behind when the phone turns.
        /// </summary>
        private void BuildFeedQuad()
        {
            Material mat = UnlitMaterial(_webcam);
            if (mat == null)
            {
                Debug.LogWarning("[AR] no unlit material for the feed — model over black");
                _cam.backgroundColor = Color.black;
                return;
            }

            var go = new GameObject("ArFeed");
            go.transform.SetParent(_cam.transform, false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = FeedMesh();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _feed = go.transform;
            FitFeed();
        }

        /// <summary>
        /// Fill the frustum, and honour the two things phone cameras always get wrong: the sensor
        /// is mounted sideways (<c>videoRotationAngle</c>) and some report their rows flipped.
        /// Skip either and the room appears rotated 90° or upside down under a correctly-oriented
        /// model — which reads as the MODEL being broken.
        /// </summary>
        private void FitFeed()
        {
            if (_feed == null || _cam == null || _webcam == null) return;

            float dist = Mathf.Clamp(_cam.farClipPlane * 0.92f, _cam.nearClipPlane * 4f + 0.01f,
                                     _cam.farClipPlane * 0.98f);
            float h = 2f * dist * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float w = h * Mathf.Max(0.01f, _cam.aspect);

            int rot = _webcam.videoRotationAngle;
            bool sideways = rot == 90 || rot == 270;
            // A sideways sensor is fitted to the OTHER screen axis before it is turned upright.
            float qw = sideways ? h : w;
            float qh = sideways ? w : h;

            _feed.localPosition = new Vector3(0f, 0f, dist);
            _feed.localRotation = Quaternion.Euler(0f, 0f, -rot);
            _feed.localScale = new Vector3(qw * 1.04f,
                                           qh * 1.04f * (_webcam.videoVerticallyMirrored ? -1f : 1f),
                                           1f);
        }

        // ── per frame ────────────────────────────────────────────────────────────

        private void LateUpdate()
        {
            if (!_active || _cam == null) return;

            if (_gyro)
            {
                Quaternion a = Input.gyro.attitude;
                Quat r = GyroMath.CameraRotation(new Quat(a.x, a.y, a.z, a.w), ScreenAngle());
                _cam.transform.rotation = new Quaternion((float)r.X, (float)r.Y, (float)r.Z, (float)r.W);
            }

            FitFeed();
        }

        private static double ScreenAngle()
        {
            switch (Screen.orientation)
            {
                case ScreenOrientation.LandscapeLeft: return 90;
                case ScreenOrientation.PortraitUpsideDown: return 180;
                case ScreenOrientation.LandscapeRight: return 270;
                default: return 0;
            }
        }

        // ── leaving ──────────────────────────────────────────────────────────────

        /// <summary>The web's <c>exitAR</c> — hand every borrowed thing back.</summary>
        private void Restore()
        {
            _active = false;
            ArControls.Close();
            StopFeed();

            if (_feed != null) { Destroy(_feed.gameObject); _feed = null; }

            if (_seabed != null) { _seabed.SetActive(_seabedWas); _seabed = null; }
            if (_water != null) { _water.SetActive(_waterWas); _water = null; }
            if (_backdrop != null) { _backdrop.SetVisible(true); _backdrop = null; }
            RenderSettings.fog = _fog;

            if (_cam != null)
            {
                _cam.clearFlags = _clearFlags;
                _cam.backgroundColor = _bgColor;
                _cam.nearClipPlane = _near;
                _cam.farClipPlane = _far;
                _cam.transform.SetPositionAndRotation(_camPos, _camRot);
            }

            if (_gyro) Input.gyro.enabled = false;
            _gyro = false;

            // The orbit rig re-reads the camera pose it was given back, so the map view returns to
            // exactly the shot the user left — not a reset frame.
            if (_orbit != null)
            {
                _orbit.enabled = _orbitWas;
                _orbit.minDistance = 2f;
                _orbit = null;
            }

            Debug.Log("[AR] exit — scene restored");
        }

        private void StopFeed()
        {
            if (_webcam == null) return;
            if (_webcam.isPlaying) _webcam.Stop();
            Destroy(_webcam);
            _webcam = null;
        }

        // ── assets ───────────────────────────────────────────────────────────────

        private static Mesh _mesh;
        private static Mesh FeedMesh()
        {
            if (_mesh != null) return _mesh;
            var m = new Mesh { name = "ArFeedQuad" };
            m.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
            };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            var n = new Vector3[4];
            for (int i = 0; i < 4; i++) n[i] = new Vector3(0f, 0f, -1f);
            m.normals = n;
            m.RecalculateBounds();
            _mesh = m;
            return m;
        }

        /// <summary>
        /// An unlit material carrying the feed. Same property hunt as <see cref="Backdrop"/>: the
        /// project ships one glTF unlit material and the property name it exposes depends on the
        /// pipeline, so it is looked up rather than assumed.
        /// </summary>
        private static Material UnlitMaterial(Texture tex)
        {
            Material src = Resources.Load<Material>("DM_GltfUnlit");
            if (src == null) return null;
            var mat = new Material(src);
            if (mat.shader == null) return null;

            string via = null;
            string[] names = { "_MainTex", "baseColorTexture", "_BaseMap", "_BaseColorTexture" };
            foreach (string n in names)
            {
                if (!mat.HasProperty(n)) continue;
                mat.SetTexture(n, tex);
                via = n;
                break;
            }
            if (via == null) return null;

            if (mat.HasProperty("baseColorFactor")) mat.SetColor("baseColorFactor", Color.white);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Background;
            return mat;
        }
    }
}
