using System.Collections.Generic;
using DiveMap.Core;
using DiveMap.Runtime.Marine;
using DiveMap.Runtime.Ui;
using UnityEngine;

namespace DiveMap.Runtime
{
    /// <summary>
    /// P1.1 — flies the camera through the map in <see cref="AppMode.Tour"/>. All the motion
    /// rules live in <see cref="DroneFlight"/> (pure + unit-tested); this class is the plumbing:
    /// take the sticks off <see cref="InputRig"/>, ask the world for the seabed height, hand both
    /// to the model, and put the camera where it says.
    ///
    /// It does NOT own the camera outside the tour: on exit the <see cref="OrbitCamera"/> is
    /// re-enabled and told to re-frame, so leaving the tour returns you to the view you know
    /// rather than to wherever the drone happened to stop.
    ///
    /// The seabed height comes from a downward raycast against the seabed's MeshCollider, which
    /// costs one ray per frame and — unlike a formula — is automatically right for sculpted maps.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TourController : MonoBehaviour
    {
        private static TourController _instance;

        /// <summary>The live controller while a tour is running (used by the HUD buttons).</summary>
        public static TourController Active => _instance != null && _instance._active ? _instance : null;

        private Camera _cam;
        private OrbitCamera _orbit;
        private TourHud _hud;
        private DroneLights _lights;
        private FishSchoolSystem _reef;
        private List<Transform> _animals;
        private List<string> _animalIds;
        private List<Vector3> _miniSolids = new List<Vector3>();
        private List<Vector3> _miniSchools = new List<Vector3>();

        private DroneFlight.State _state;
        private DroneFlight.Box[] _solids = new DroneFlight.Box[0];
        private float _waterLevel = 240f;
        private float _scaleX = 1f, _scaleZ = 1f;
        private Vector3 _homeCenter;
        private Vector3 _homeFrame = new Vector3(100f, 40f, 100f);
        private float _homeMinY;
        private float _startBack = 90f;
        private bool _active;
        private int _frames;

        /// <summary>
        /// Told by <c>AppBoot</c> after each map build: the solids to fly around, the surface, and
        /// the seabed's stretch (for the map boundary).
        /// </summary>
        public static void Configure(SceneBuilder.BuildResult r)
        {
            List<ObstacleBox> obstacles = r.Obstacles;
            float waterLevel = r.WaterLevel;
            float scaleX = r.SeabedScaleX;
            float scaleZ = r.SeabedScaleZ;
            TourController tc = Ensure();
            if (tc == null)
            {
                Debug.LogWarning("[Tour] Configure before ModeManager exists — the tour would " +
                                 "fly with no solids and no map bound; skipped");
                return;
            }

            int n = obstacles != null ? obstacles.Count : 0;
            tc._solids = new DroneFlight.Box[n];
            for (int i = 0; i < n; i++)
            {
                ObstacleBox o = obstacles[i];
                tc._solids[i] = new DroneFlight.Box
                {
                    MinX = o.Min.x, MinY = o.Min.y, MinZ = o.Min.z,
                    MaxX = o.Max.x, MaxY = o.Max.y, MaxZ = o.Max.z,
                };
            }
            tc._waterLevel = waterLevel;
            tc._scaleX = scaleX;
            tc._scaleZ = scaleZ;
            tc._animals = r.Animals;
            tc._animalIds = r.AnimalIds;

            // The minimap needs the same world the drone flies in: footprint, solids, schools.
            var solidCentres = new List<Vector3>();
            for (int i = 0; i < tc._solids.Length; i++)
                solidCentres.Add(new Vector3((tc._solids[i].MinX + tc._solids[i].MaxX) * 0.5f,
                                             0f,
                                             (tc._solids[i].MinZ + tc._solids[i].MaxZ) * 0.5f));
            tc._miniSolids = solidCentres;
            tc._miniSchools = r.SchoolAnchors ?? new List<Vector3>();
            tc._homeCenter = r.FrameCenter;
            tc._homeFrame = new Vector3(r.FrameSizeX, r.FrameSizeY, r.FrameSizeZ);
            tc._homeMinY = r.FrameMinY;
            // Start distance comes from the CONTENT, not the seabed: the first tour shot began
            // 263 u out because the sand is 374 u across, and the wreck was a speck.
            tc._startBack = Mathf.Clamp(Mathf.Max(r.FrameSizeX, r.FrameSizeZ) * 0.9f, 25f, 140f);
        }

        private static TourController Ensure()
        {
            if (_instance != null) return _instance;
            ModeManager mm = ModeManager.Instance;
            if (mm == null) return null;
            _instance = mm.gameObject.AddComponent<TourController>();
            mm.Changed += _instance.OnModeChanged;
            return _instance;
        }

        /// <summary>Enter the tour (menu item / future warp arrival).</summary>
        public static bool Start()
        {
            TourController tc = Ensure();
            if (tc == null || ModeManager.Instance == null) return false;
            return ModeManager.Instance.Request(AppMode.Tour);
        }

        private void OnModeChanged(AppMode prev, AppMode next)
        {
            bool wantActive = ModeRules.IsFirstPerson(next);
            if (wantActive && !_active) Begin();
            else if (!wantActive && _active) End();
        }

        private void Begin()
        {
            _cam = Camera.main;
            if (_cam == null) return;
            _orbit = _cam.GetComponent<OrbitCamera>();
            if (_orbit != null) _orbit.enabled = false;

            // Start just off the content, facing it, at a comfortable height above the sand.
            Vector3 start = _homeCenter - new Vector3(0f, 0f, _startBack);
            start.y = Mathf.Clamp(_homeCenter.y + 12f, SeabedY(start) + 10f, _waterLevel - 12f);

            _state = new DroneFlight.State
            {
                Pos = new DroneFlight.Vec3(start.x, start.y, start.z),
                Vel = new DroneFlight.Vec3(0f, 0f, 0f),
                Yaw = 0f,   // +Z, i.e. looking at the content we just backed away from
            };

            _hud = TourHud.Ensure();
            Ui.MinimapWidget.Configure(_homeCenter, SeabedGeom.SandRadius * Mathf.Max(_scaleX, _scaleZ),
                                       _miniSolids, _miniSchools, _animals);
            if (_lights == null) _lights = DroneLights.Attach(transform);
            _lights.gameObject.SetActive(true);
            _lights.Set(true);
            if (_hud != null) _hud.SetHeadlight(true);
            _reef = UnityEngine.Object.FindFirstObjectByType<FishSchoolSystem>();
            InputRig.Clear();
            _active = true;
            _frames = 0;

            // Sound: the drone's start cue, then the underwater bed (streamed from the CDN;
            // silent if it cannot be fetched).
            AudioBank.PlayCue();
            AudioBank.StartAmbience();

            // The clean-up game runs inside the tour only — the web is emphatic that litter must
            // not rain onto a map you are looking at or editing.
            TrashGameSystem.Ensure(transform).Begin(_homeCenter, _waterLevel, _scaleX, _scaleZ);

            // No toast here: the HUD's own hint line (#tourHud) says this permanently, and the web
            // does not double up. A toast on entry also fought the hint for the same screen space.
            Debug.Log($"[Tour] begin pos=({start.x:F1},{start.y:F1},{start.z:F1}) " +
                      $"solids={_solids.Length} water={_waterLevel:F1} " +
                      $"scale=({_scaleX:F2},{_scaleZ:F2})");
        }

        /// <summary>
        /// Photo button (#tourShot). Captures the frame WITHOUT the HUD — the web's captureThumb
        /// hides its UI first, and a souvenir with joysticks across it is not a souvenir. Saved
        /// into the app's own folder for now; putting it in the phone's gallery needs a MediaStore
        /// call that can only be verified on a device, so that lands as its own step.
        /// </summary>
        public void TakePhoto()
        {
            StartCoroutine(CapturePhoto());
        }

        private System.Collections.IEnumerator CapturePhoto()
        {
            RectTransform hud = Ui.HudLayer.For(AppMode.Tour);
            if (hud != null) hud.gameObject.SetActive(false);
            yield return new WaitForEndOfFrame();

            Texture2D shot = null;
            var result = PhotoSaver.Result.Failed;
            string where = null;
            try
            {
                shot = ScreenCapture.CaptureScreenshotAsTexture();
                byte[] jpg = shot.EncodeToJPG(92);
                string name = $"divemap_{System.DateTime.Now:yyyyMMdd_HHmmss}.jpg";
                result = PhotoSaver.Save(jpg, name, out where);
                Debug.Log($"[Tour] photo {result} → {where} ({jpg.Length / 1024} KB, {shot.width}×{shot.height})");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Tour] photo failed: {e.Message}");
            }
            finally
            {
                if (shot != null) Destroy(shot);
                if (hud != null) hud.gameObject.SetActive(true);
            }

            // Say WHERE it went: "saved" is not useful if the user then cannot find it in their
            // gallery because the insert fell back to the app's own folder.
            Ui.Toast.ShowTr(result == PhotoSaver.Result.Gallery ? "บันทึกภาพลงแกลเลอรีแล้ว"
                          : result == PhotoSaver.Result.AppFolder ? "บันทึกภาพในแอปแล้ว"
                          : "บันทึกภาพไม่สำเร็จ");
            AudioBank.PlaySfx("click");
        }

        /// <summary>Toggle the headlamps (HUD button). Also swaps the whole atmosphere.</summary>
        public void ToggleHeadlight()
        {
            if (_lights == null) return;
            _lights.Toggle();
            if (_hud != null) _hud.SetHeadlight(_lights.HeadlightOn);
        }

        private void End()
        {
            _active = false;
            InputRig.Clear();
            if (_lights != null)
            {
                _lights.RestoreScene();
                _lights.gameObject.SetActive(false);
            }
            AudioBank.StopAmbience();
            TrashGameSystem.Ensure(transform).End();
            // The reef goes back to ignoring us.
            if (_reef != null) _reef.SetRepulsor(Vector3.zero, 0f);
            if (_orbit != null)
            {
                _orbit.enabled = true;
                // Put the map back the way the user left it rather than wherever we stopped.
                _orbit.FrameBox(_homeCenter, _homeFrame.x, _homeFrame.y, _homeFrame.z, _homeMinY);
            }
            Debug.Log("[Tour] end");
        }

        private Vector3? _qcCharge;

        /// <summary>
        /// QC only — fly at <paramref name="target"/> under full throttle. A headless player has
        /// no hands on the sticks, so without this the drone hovers and C5 (fish scattering from a
        /// charging diver) can never be photographed. It drives the SAME
        /// <see cref="DroneFlight"/> path as a real player rather than teleporting the camera,
        /// so the speed the reef reacts to is the speed the drone can actually reach.
        /// </summary>
        public void QcChargeToward(Vector3 target) => _qcCharge = target;

        /// <summary>QC only — hands off the sticks.</summary>
        public void QcStopCharge() => _qcCharge = null;

        private void Update()
        {
            if (!_active || _cam == null) return;

            var sticks = new DroneFlight.Sticks
            {
                Lx = InputRig.Left.x,
                // Screen up is +y on a uGUI stick but the web's ly is negative-up; keep the web's
                // sign convention so DroneFlight's ported formulas stay literal.
                Ly = -InputRig.Left.y,
                Rx = InputRig.Right.x,
                Ry = -InputRig.Right.y,
            };

            if (_qcCharge.HasValue)
            {
                Vector3 tgt = _qcCharge.Value;
                float want = Mathf.Atan2(tgt.z - _state.Pos.Z, tgt.x - _state.Pos.X);
                float err = Mathf.Atan2(Mathf.Sin(want - _state.Yaw), Mathf.Cos(want - _state.Yaw));
                sticks.Lx = Mathf.Clamp(err * 1.5f, -1f, 1f);   // steer onto the bearing
                sticks.Ly = 0f;
                sticks.Rx = 0f;
                sticks.Ry = -1f;                                 // full ahead (web sign: up = −)
            }

            float fs = (float)MarineMath.RealDeltaScale(Time.deltaTime);
            float dt = (float)MarineMath.BaseStep * fs;

            Vector3 probe = new Vector3(_state.Pos.X + _state.Vel.X * dt, 0f,
                                        _state.Pos.Z + _state.Vel.Z * dt);
            float seabedY = SeabedY(probe);

            _state = DroneFlight.Step(_state, sticks, dt, seabedY, _waterLevel,
                                      _solids, _scaleX, _scaleZ);

            var pos = new Vector3(_state.Pos.X, _state.Pos.Y, _state.Pos.Z);
            DroneFlight.Vec3 look = DroneFlight.LookTarget(_state);
            _cam.transform.position = pos;
            _cam.transform.LookAt(new Vector3(look.X, look.Y, look.Z), Vector3.up);

            if (_hud != null) _hud.SetDepth(DroneFlight.DepthMetres(pos.y, _waterLevel));
            if (_lights != null) _lights.Track(pos, _state.Yaw, SeabedY(pos + _cam.transform.forward * DiveLightMath.Reach));
            if (_reef != null) _reef.SetRepulsor(pos, DiveLightMath.FishBubble * 2f);
            AudioBank.ProximityTick(pos, _animals, _animalIds);
            CheckWarpGates(pos);

            _frames++;
            if (_frames == 30 || _frames % 300 == 0)
                Debug.Log($"[Tour] frame={_frames} pos=({pos.x:F1},{pos.y:F1},{pos.z:F1}) " +
                          $"yaw={_state.Yaw * Mathf.Rad2Deg:F0}° " +
                          $"depth={DroneFlight.DepthMetres(pos.y, _waterLevel):F1}m " +
                          $"seabedY={seabedY:F1}");
        }

        private bool _warpArmed = true;

        /// <summary>
        /// Fly into a gate and the destination picker opens (the web: trigger at 13 u, re-arm only
        /// after leaving 16 u, so cancelling the picker while still inside the ring does not
        /// immediately re-open it).
        /// </summary>
        private void CheckWarpGates(Vector3 pos)
        {
            var gates = WarpGate.Gates;
            if (gates == null || gates.Count == 0) return;

            bool near = false;
            for (int i = 0; i < gates.Count; i++)
            {
                WarpGate g = gates[i];
                if (g == null) continue;
                float d = Vector3.Distance(pos, g.transform.position);
                if (d < WarpGate.RearmRadius) near = true;
                if (d < WarpGate.TriggerRadius && _warpArmed)
                {
                    _warpArmed = false;
                    AudioBank.PlaySfx("click");
                    Ui.Toast.ShowTr("ประตูวาป — เลือกแมพปลายทาง");
                    if (Ui.UiShell.Instance != null) Ui.UiShell.Instance.OpenWarpPicker();
                    return;
                }
            }
            if (!near) _warpArmed = true;
        }

        /// <summary>
        /// Seabed height under <paramref name="at"/>. A ray from the surface downward hits the
        /// seabed's MeshCollider (the only collider in the scene), so sculpted maps are handled
        /// for free; if the ray misses — off the edge of the sand — fall back to y=0, the plane
        /// the map is authored around.
        /// </summary>
        private float SeabedY(Vector3 at)
        {
            var from = new Vector3(at.x, _waterLevel + 50f, at.z);
            if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, _waterLevel + 400f))
                return hit.point.y;
            return 0f;
        }
    }
}
