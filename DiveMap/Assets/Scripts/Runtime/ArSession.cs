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

        /// <summary>The map root AR hides parts of — QC checks the same one, not a lookalike.</summary>
        public static GameObject MapRoot => Instance != null ? Instance._mapRoot : null;

        // Where the map is, from the last build.
        private GameObject _mapRoot;
        private Vector3 _center;
        private float _sizeX = 100f, _sizeZ = 100f, _minY;

        private bool _active;
        private double _fit, _scale;

        private Camera _cam;
        private OrbitCamera _orbit;
        private Transform _feed;
        private WebCamTexture _webcam;
        private bool _gyro;
        /// <summary>True when ARKit is driving instead of the attitude fallback.</summary>
        private bool _arkit;

        // Everything borrowed from the scene, so Restore() can be exact rather than approximate.
        private CameraClearFlags _clearFlags;
        private Color _bgColor;
        private float _near, _far;
        private Vector3 _camPos;
        private Quaternion _camRot;
        private bool _fog;
        // The seabed's LOOK is spread over four sibling objects, not one. CI's log said
        // "seabedHidden=True" and the screenshot still showed a glowing white floor — because
        // Caustics is a sibling of Seabed, not a child of it. Hiding by name means hiding all
        // of them, so they are held as a list and each remembers its own previous state.
        private static readonly string[] UnderwaterParts = { "Seabed", "Caustics", "Water", "GodRays" };
        private readonly System.Collections.Generic.List<GameObject> _hidden =
            new System.Collections.Generic.List<GameObject>();
        private readonly System.Collections.Generic.List<bool> _hiddenWas =
            new System.Collections.Generic.List<bool>();
        private Backdrop _backdrop;
        private bool _orbitWas;
        // The orbit rig DERIVES the camera pose from these every frame, so restoring the transform
        // alone is not restoring the view: the rig simply recomputes over it on the next frame.
        // CI caught exactly that (`[QC] ar restored … pos=False`).
        private Vector3 _orbitTarget;
        private float _orbitDistance, _orbitMin;

        // The live pinch on the no-tracking path.
        private bool _pinching;
        private double _pinchStartPixels, _pinchStartMetres;

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
            // Hold the root the builder just made. Looking it up later by name was wrong: the QC
            // run reloads the map (buying an item does), and `GameObject.Find("Map")` can hand
            // back a root that is no longer the one on screen — AR then hid an invisible copy and
            // logged "3/3 hidden" over a seabed the user could still see.
            s._mapRoot = r.Root;
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

            // 🔴 ขออีกครั้งทั้งที่ยังอยู่ใน AR = ไม่มีอะไรเกิดขึ้นเลย (ModeManager ไม่ยิงเหตุการณ์
            // เปลี่ยนโหมดเมื่อโหมดเดิมกับใหม่เท่ากัน) ⇒ กล้องยังอยู่ในริกของแมพก่อนหน้า แมพใหม่
            // ยังถูกสั่งซ่อน = จอดำ (user 16 ส.ค.: "AR แมพแรกไม่มีปัญหา พอแมพที่ 2 จอดำ")
            // เจอกรณีนี้เมื่อไร ให้รื้อของเก่าทิ้งก่อน แล้วเข้าใหม่ให้ครบทุกขั้น
            if (ModeManager.Current == AppMode.Ar)
            {
                Debug.Log("[AR] already in AR — restarting the session for the current map");
                ModeManager.Instance.Exit();
            }
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
            if (_orbit != null)
            {
                // Borrow these HERE, with everything else. Reading them later looked equivalent
                // and was not: ApplyPlacement() runs first and already writes `distance`, so the
                // backup taken after it stored the AR distance and "restoring" it was a no-op.
                // CI said `[QC] ar restored … pos=False` twice before this moved.
                _orbitTarget = _orbit.target;
                _orbitDistance = _orbit.distance;
                _orbitMin = _orbit.minDistance;
            }

            // The room shows through wherever nothing is drawn.
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            RenderSettings.fog = false;

            // Real AR first. ARKit knows where the room is, so the map can sit on a table and the
            // player can walk round it — the two things the attitude-only path can never do. It
            // brings its own camera feed (ARCameraBackground), so the WebCamTexture quad below is
            // only for devices without tracking.
            _arkit = ArKitSession.Begin(_center, _minY, _sizeX, _sizeZ, _mapRoot);

            // 🔴 The sea stays ON when ARKit is driving, and that is a change of intent, not a
            // tweak.
            //
            // Stripping the seabed and the water came from the web, where AR welds the model to
            // your face and the sea would fill the screen. On a TABLE the same objects are the
            // opposite: the sand disc IS the diorama's base and the water surface is what makes it
            // read as a cube of ocean rather than loose props floating over the floorboards.
            //
            // Reported as "the map disappeared and I cannot find it" on a sparsely built map — and
            // it had not disappeared. It was placed correctly (the diagnostic read
            // `map on · eye→centre 0.77 m`), the middle of it was simply EMPTY, because the middle
            // of a dive site is seabed and the seabed was the thing being hidden. A dense map hid
            // the fault; the next map along exposed it.
            HideUnderwaterWorld(hideSea: !_arkit);

            if (_arkit)
            {
                Debug.Log("[AR] ARKit session — plane detection + tap to place");
                ArControls.Open();
                if (Ui.CompassWidget.Instance != null) Ui.CompassWidget.Instance.SetVisible(false);
                return;
            }

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

            // The compass belongs to a map you are flying over, not to a model on your table —
            // and in AR it is the only thing left drawing over the room.
            if (Ui.CompassWidget.Instance != null) Ui.CompassWidget.Instance.SetVisible(false);

            ArControls.Open();
            // No tracking here, so there is no floor to find and no anchor to set: the site is
            // already in front of the viewer and the only thing left to do is size it. That is the
            // Adjusting step minus the confirm, which is what SetHint overriding the step's own
            // wording says. Showing "looking for a surface" on a phone that will never find one is
            // the kind of lie that gets reported as a hang.
            ArControls.SetSizeOnly(Metres);
            Debug.Log($"[AR] begin span=({_sizeX:F0},{_sizeZ:F0}) fit={_fit:F5} " +
                      $"gyro={_gyro} eye={_cam.transform.position}");
        }

        /// <param name="hideSea">
        /// Hide the seabed, water, caustics and sun shafts. True on the no-tracking path (the model
        /// is an arm's length from your eye and the sea would swallow the room); false under ARKit,
        /// where those four objects are the tabletop diorama itself.
        /// </param>
        private void HideUnderwaterWorld(bool hideSea)
        {
            // The web: `seabed.visible=false; surf.visible=false; scene.background=null;`
            // Here that is four objects: the sand, the caustic light dancing 0.4 u above it, the
            // water disc, and the sun shafts. Leave any one of them and the room does not show
            // through — it looks like the AR feed failed rather than like a bug.
            _hidden.Clear();
            _hiddenWas.Clear();
            GameObject root = _mapRoot != null ? _mapRoot : GameObject.Find("Map");
            if (root != null && hideSea)
            {
                var missing = new System.Collections.Generic.List<string>();
                foreach (string name in UnderwaterParts)
                {
                    Transform t = root.transform.Find(name);
                    if (t == null) { missing.Add(name); continue; }
                    _hidden.Add(t.gameObject);
                    _hiddenWas.Add(t.gameObject.activeSelf);
                    t.gameObject.SetActive(false);
                }
                // Naming what was NOT found is the point: a count alone ("3 of 4") leaves the
                // next person to work out which piece is still drawing over the room.
                if (missing.Count > 0)
                    Debug.Log("[AR] no such map part: " + string.Join(", ", missing) +
                              " — nothing to hide, which is fine if this map has none");

                // Everything else still under the root, so a part that turns out to draw over the
                // room can be named immediately instead of costing a CI round to identify.
                var rest = new System.Collections.Generic.List<string>();
                foreach (Transform child in root.transform)
                    if (child.gameObject.activeSelf && System.Array.IndexOf(UnderwaterParts, child.name) < 0)
                        rest.Add(child.name);
                Debug.Log($"[AR] root='{root.name}' still showing {rest.Count} child(ren): " +
                          string.Join(", ", rest.GetRange(0, System.Math.Min(12, rest.Count))));
            }

            _backdrop = _cam.GetComponent<Backdrop>();
            if (_backdrop != null) _backdrop.SetVisible(false);

            // Whatever is still painting the sea has to be NAMED, not guessed at. Twice now a log
            // has said the underwater world was hidden while the screenshot showed a lit seabed,
            // and each guess cost a CI round. Every large renderer still drawing, with its full
            // path — the answer, whatever it turns out to be, is in this line.
            {
                var big = new System.Collections.Generic.List<string>();
                foreach (MeshRenderer mr in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                {
                    if (!mr.enabled || !mr.gameObject.activeInHierarchy) continue;
                    Vector3 sz = mr.bounds.size;
                    if (Mathf.Max(sz.x, sz.z) < 80f) continue;      // ignore ordinary props
                    big.Add($"{Path(mr.transform)}({sz.x:F0}×{sz.z:F0})");
                }
                Debug.Log($"[AR] large renderers still drawing: {big.Count} — " +
                          string.Join(" · ", big.GetRange(0, Mathf.Min(10, big.Count))));
            }


        }

        /// <summary>Full scene path of a transform — "Map/Seabed" tells you what a name alone cannot.</summary>
        private static string Path(Transform t)
        {
            string s = t.name;
            for (Transform p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
            return s;
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

        /// <summary>
        /// Two fingers resize the site on the phones that have no tracking, exactly as they do on
        /// the ones that do (<c>ArKitSession.HandlePinch</c>).
        ///
        /// 🔴 The − / + stepper it replaces is gone from BOTH paths deliberately. Keeping it here
        /// "because this path is only a fallback" would mean the same app taught two different
        /// gestures depending on which phone it woke up on — and the fallback is the path a user is
        /// least able to explain, so it is the last place to put a control nobody else has.
        ///
        /// Same absolute-from-gesture-start rule as the ARKit path: sample the finger distance and
        /// the size when the second finger lands, then map ratio → size. Per-frame accumulation
        /// drifts, and a pinch that does not return to where it started when the fingers do feels
        /// broken without anyone being able to say why.
        /// </summary>
        private void HandlePinch()
        {
            if (Input.touchCount < 2) { _pinching = false; return; }

            Touch a = Input.GetTouch(0), b = Input.GetTouch(1);
            double pixels = Vector2.Distance(a.position, b.position);

            if (!_pinching)
            {
                _pinching = true;
                _pinchStartPixels = pixels;
                _pinchStartMetres = Metres;
                return;
            }

            SetMetres(ArPinch.Pinch(_pinchStartMetres, _pinchStartPixels, pixels));
        }

        /// <summary>
        /// Make the site read as <paramref name="metres"/> across.
        ///
        /// The pinch is a gesture wrapped around this, and QC drives it here — a headless runner
        /// has no fingers, so the alternative is either not testing the sizing at all or testing a
        /// second copy of the clamp that can be right while the one the user reaches is wrong.
        /// </summary>
        public void SetMetres(double metres)
        {
            if (!_active) return;
            double span = Mathf.Max(_sizeX, _sizeZ);
            if (span <= 0) return;
            double m = ArPinch.Clamp(metres);
            // 🔴 NOT ArPinch.ScaleFor. The two AR paths hold RECIPROCAL scales and mixing them is
            // silent: ArKitSession._scale is world units per metre (span / 1.1, a number in the
            // hundreds) while this path's _scale is metres per world unit (ArPlacement.FitScale =
            // 1.1 / span, a number near 0.003). Feeding one to the other's formula does not throw
            // or clamp — it returns span²/1.1, a plausible-looking figure that put the readout at
            // five digits and the camera inside the map.
            double next = m / span;                       // metres per world unit
            if (next <= 0 || System.Math.Abs(next - _scale) < 1e-12) return;

            _scale = next;
            ApplyPlacement();
            ArControls.SetSize(m, ArPinch.AtLimit(m));
        }

        /// <summary>How wide the site reads on the table right now, in metres.</summary>
        public static double Metres => Instance != null
            ? ArPlacement.ApparentSpan(Mathf.Max(Instance._sizeX, Instance._sizeZ), Instance._scale) : 0;

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

            // ARKit runs its own gesture handling on its own rig; two systems reading the same two
            // fingers would fight over the scale.
            //
            // 🔴 And it owns the diagnostic line too. This method used to write "gyro OFF — no
            // sensor reported by this device" over the top of ARKit's readout every frame, because
            // Begin() returns before `_gyro` is ever set on the ARKit path. The screenshot from
            // build 201 therefore said the phone had no gyroscope while ARKit was demonstrably
            // tracking — a diagnostic that reports on the wrong session is worse than none, and
            // this is the second time that exact race has cost a round.
            if (_arkit) return;
            HandlePinch();

            if (_gyro)
            {
                Quaternion a = Input.gyro.attitude;
                Quat r = GyroMath.CameraRotation(new Quat(a.x, a.y, a.z, a.w), ScreenAngle());
                _cam.transform.rotation = new Quaternion((float)r.X, (float)r.Y, (float)r.Z, (float)r.W);

                // The sensor readout that used to sit here is gone with the rest of the AR HUD
                // text. If this path ever needs diagnosing on a device, one call to
                // ArControls.SetDiagnostics with these values brings it straight back — and
                // ArKitSession.OffReason still carries why real AR declined.
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
            if (_arkit)
            {
                ArKitSession.End();
                _arkit = false;
            }
            StopFeed();

            if (_feed != null) { Destroy(_feed.gameObject); _feed = null; }

            int restoredParts = _hidden.Count;
            for (int i = 0; i < _hidden.Count; i++)
                if (_hidden[i] != null) _hidden[i].SetActive(_hiddenWas[i]);
            _hidden.Clear();
            _hiddenWas.Clear();
            if (_backdrop != null) { _backdrop.SetVisible(true); _backdrop = null; }
            RenderSettings.fog = _fog;

            if (_cam != null)
            {
                _cam.clearFlags = _clearFlags;
                _cam.backgroundColor = _bgColor;
                _cam.nearClipPlane = _near;
                _cam.farClipPlane = _far;
                _cam.transform.SetPositionAndRotation(_camPos, _camRot);

                // 🔴 17 ส.ค. 2026 — user: "เข้าโหมด AR แล้วออกมา ไม่ว่าจะเข้าโหมดอะไรก็เสีย
                // ใช้งานไม่ได้" · ค่าที่คืนข้างบนเป็น "ค่าที่เราหยิบยืมไปเอง" แต่ ARKit ยัง
                // **เขียนทับของอื่นบนกล้องตัวเดียวกัน**ระหว่างเซสชัน แล้วไม่มีใครคืนให้:
                //
                //   • projectionMatrix — ARCameraManager ยัดเมทริกซ์ของกล้องจริงใส่ทุกเฟรม
                //     และค่านั้น "ติดค้าง" จนกว่าจะสั่ง Reset ⇒ พอออกจาก AR ฉากถูกวาดด้วย
                //     เลนส์ของกล้องมือถือ ไม่ใช่ของเกม = ภาพเพี้ยน/ดำทั้งจอในทุกโหมดถัดไป
                //   • usePhysicalProperties / targetTexture / rect — ตระกูลเดียวกัน: ตั้งง่าย
                //     ลืมคืนง่าย และอาการที่ได้คือ "จอดำ" เหมือนกันหมดจนแยกไม่ออก
                //
                // คืนให้ครบทีเดียวตรงนี้ ถูกกว่าการไล่เดาว่าตัวไหนเป็นตัวที่พังในรอบหน้า
                _cam.ResetProjectionMatrix();
                _cam.ResetWorldToCameraMatrix();
                _cam.ResetAspect();
                _cam.usePhysicalProperties = false;
                _cam.targetTexture = null;
                _cam.rect = new Rect(0f, 0f, 1f, 1f);
                _cam.enabled = true;
            }

            bool usedOrbitFallback = !_gyro;   // AR borrowed the orbit rig only when there was no sensor
            if (_gyro) Input.gyro.enabled = false;
            _gyro = false;

            // Hand the orbit rig its own state back, not just the camera transform. Without the
            // three lines below the rig recomputes the pose from a target/distance AR left behind
            // and the map opens somewhere else entirely — with nothing logged, because from the
            // camera's point of view it was put back correctly and then moved a frame later.
            if (_orbit != null)
            {
                if (usedOrbitFallback)
                {
                    _orbit.target = _orbitTarget;
                    _orbit.distance = _orbitDistance;
                    _orbit.minDistance = _orbitMin;
                }
                _orbit.enabled = _orbitWas;
                _orbit = null;
            }

            if (Ui.CompassWidget.Instance != null) Ui.CompassWidget.Instance.SetVisible(true);

            Debug.Log($"[AR] exit — scene restored ({restoredParts} underwater part(s) put back) " +
                      $"cam=({(_cam != null ? _cam.name : "none")} fov={(_cam != null ? _cam.fieldOfView : 0f):F1} " +
                      $"rect={(_cam != null ? _cam.rect.width : 0f):F2} enabled={(_cam != null && _cam.enabled)})");
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
