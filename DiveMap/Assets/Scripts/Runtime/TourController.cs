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
        // Every solid object twice over: the single AABB it has always had, and the hull that
        // leaves its openings open where the model publishes one. _solids is the slice of these
        // the drone actually carries this stretch of the dive — see RefreshSolids.
        private List<SolidBoxes.Group> _solidGroups = new List<SolidBoxes.Group>();
        private readonly List<SolidBoxes.Box> _solidPick = new List<SolidBoxes.Box>();
        private Vector3 _solidsAt;
        private bool _solidsPicked;
        private bool _anyHull;
        private int _detailedLast = -1;
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

            // The diver's own view of the scenery. A build that carries hulls hands them over
            // here; anything else (an older build result, a map whose models publish nothing)
            // falls back to one box per object — bit for bit the array this method used to make.
            if (r.Solids != null && r.Solids.Count > 0)
            {
                tc._solidGroups = r.Solids;
            }
            else
            {
                var plain = new List<SolidBoxes.Group>(n);
                for (int i = 0; i < n; i++)
                {
                    ObstacleBox o = obstacles[i];
                    plain.Add(new SolidBoxes.Group
                    {
                        Coarse = SolidBoxes.Box.FromMinMax(o.Min.x, o.Min.y, o.Min.z,
                                                           o.Max.x, o.Max.y, o.Max.z),
                        Fine = null,
                    });
                }
                tc._solidGroups = plain;
            }
            // No model on this map publishes a hull — which is every map until the CDN has the
            // files — so the pick can never change and is made once, exactly as before.
            tc._anyHull = false;
            for (int i = 0; i < tc._solidGroups.Count && !tc._anyHull; i++)
                tc._anyHull = tc._solidGroups[i].Fine != null && tc._solidGroups[i].Fine.Length > 0;

            tc._solids = new DroneFlight.Box[0];
            tc._solidsPicked = false;
            tc._detailedLast = -1;

            tc._waterLevel = waterLevel;
            tc._scaleX = scaleX;
            tc._scaleZ = scaleZ;
            tc._animals = r.Animals;
            tc._animalIds = r.AnimalIds;

            // The minimap needs the same world the drone flies in: footprint, solids, schools.
            // ONE dot per object, from the coarse list — a minimap that drew a module's every
            // strut would be a grey smear where the wreck used to be.
            var solidCentres = new List<Vector3>();
            for (int i = 0; i < n; i++)
            {
                ObstacleBox o = obstacles[i];
                solidCentres.Add(new Vector3((o.Min.x + o.Max.x) * 0.5f, 0f,
                                             (o.Min.z + o.Max.z) * 0.5f));
            }
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

        private bool _randomSpawn;

        /// <summary>
        /// Set when the diver left through a warp gate: the next map that finishes loading drops
        /// them straight back into the water at a random point. Cleared as soon as it is used, so
        /// a warp that the player cancels does not hijack the map they open next.
        /// </summary>
        public static bool ArrivingByWarp { get; set; }

        /// <summary>
        /// Set when the player picked a world through 🎮 เล่นเกม! — the web's <c>arenaPlay</c>.
        /// Someone who tapped "play" has already said what they want; handing them the orbit view
        /// and a menu makes them ask for it a second time. Cleared on use like the warp flag.
        /// </summary>
        public static bool ArenaPlay { get; set; }

        /// <summary>
        /// Enter the tour. <paramref name="randomStart"/> drops the diver at a random point in the
        /// map instead of the fixed opening view — the web does this for anyone who arrives by
        /// "play" or through a warp gate, so a familiar map still opens on something new.
        /// </summary>
        public static bool Start(bool randomStart = false)
        {
            TourController tc = Ensure();
            if (tc == null || ModeManager.Instance == null) return false;
            tc._randomSpawn = randomStart;
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
            float startYaw = 0f;   // +Z, i.e. looking at the content we just backed away from

            // D9 — a player who arrived by "play", or through a warp gate, is dropped somewhere
            // random instead (the web's enterTour(randomStart), builder.html:3722) so the same map
            // is not the same dive twice. The two draws come from here so the maths stays pure.
            if (_randomSpawn)
            {
                _randomSpawn = false;
                float mapR = SeabedGeom.SandRadius * Mathf.Max(_scaleX, _scaleZ);
                var centre = new DroneFlight.Vec3(_homeCenter.x, _homeCenter.y, _homeCenter.z);
                DroneFlight.Vec3 pick = DroneFlight.RandomSpawn(
                    centre, mapR,
                    SeabedY(_homeCenter), _waterLevel,
                    UnityEngine.Random.value, UnityEngine.Random.value);
                // Re-read the seabed UNDER the chosen point: the map is sculpted, so the height
                // above the middle says nothing about the height over a reef 100 units away.
                var at = new Vector3(pick.X, pick.Y, pick.Z);
                at.y = Mathf.Max(SeabedY(at) + 18f, _waterLevel * 0.5f);
                at.y = Mathf.Min(at.y, _waterLevel - 8f);
                start = at;
                startYaw = DroneFlight.YawToward(new DroneFlight.Vec3(at.x, at.y, at.z), centre);
                Debug.Log($"[Tour] random spawn at ({at.x:F0},{at.y:F0},{at.z:F0}) mapR={mapR:F0}");
            }

            _state = new DroneFlight.State
            {
                Pos = new DroneFlight.Vec3(start.x, start.y, start.z),
                Vel = new DroneFlight.Vec3(0f, 0f, 0f),
                Yaw = startYaw,
            };

            RefreshSolids(start, force: true);

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

            // D10 — coach a first dive, once per device. Delayed like the web (700 ms) because the
            // HUD is still fading in and a spotlight needs something laid out to point at.
            StartCoroutine(CoachFirstDive());
        }

        /// <summary>
        /// Re-pick the boxes the diver collides with, near-first.
        ///
        /// 🔴 THE BUDGET, and why it is this one. <see cref="DroneFlight.Step"/> walks the whole
        /// array every frame of the dive, on a phone:
        ///
        ///   today            494 objects × 1 box  =    494   (T-13, the biggest map we ship)
        ///   hulls for all    494 × 64             = 31,616   — sixty-four times the work, forever
        ///   here             ≤ 2,048 hull boxes + 1 box per object left over ≈ 2,542 worst case
        ///
        /// The number that made this affordable is not the cap, it is the RADIUS: you can only
        /// swim through a hole you are next to, so the nearest <see cref="SolidBoxes.DetailRadius"/>
        /// units carry real hulls and the rest of the map carries exactly what it carries today.
        /// A flat cap with no radius would have given holes to the first 32 of T-13's 394 identical
        /// frames and left the other 362 as bricks — the same report, a third time.
        ///
        /// Re-picked on DISTANCE travelled, not on a frame count and not on a timer: the tour runs
        /// at 3 fps on CI and 60 on a phone (HANDOFF §4 rule 13), and distance is the only trigger
        /// that means the same thing on both.
        /// </summary>
        private void RefreshSolids(Vector3 at, bool force)
        {
            if (!force && _solidsPicked)
            {
                if (!_anyHull) return;   // nothing to swap in — the pick is already final
                float move = (float)SolidBoxes.RefreshMove;
                if ((at - _solidsAt).sqrMagnitude < move * move) return;
            }

            int detailed = SolidBoxes.Select(_solidGroups, new Vec3(at.x, at.y, at.z), _solidPick);

            if (_solids.Length != _solidPick.Count) _solids = new DroneFlight.Box[_solidPick.Count];
            for (int i = 0; i < _solidPick.Count; i++)
            {
                SolidBoxes.Box b = _solidPick[i];
                _solids[i] = new DroneFlight.Box
                {
                    MinX = (float)b.Min.X, MinY = (float)b.Min.Y, MinZ = (float)b.Min.Z,
                    MaxX = (float)b.Max.X, MaxY = (float)b.Max.Y, MaxZ = (float)b.Max.Z,
                };
            }

            _solidsAt = at;
            _solidsPicked = true;

            // Only when it changes — this runs a couple of times a second — but ALWAYS on the
            // first pick, including the "near=0" case that says no model on this map has a hull.
            if (detailed != _detailedLast)
            {
                Debug.Log($"[Solids] near={detailed}/{_solidGroups.Count} boxes={_solids.Length} " +
                          $"budget={SolidBoxes.DiverBoxBudget} radius={SolidBoxes.DetailRadius:F0}u");
                _detailedLast = detailed;
            }
        }

        private System.Collections.IEnumerator CoachFirstDive()
        {
            if (Ui.TutorialGuide.Seen(Ui.TutorialGuide.TourKey)) yield break;
            // Retry while the HUD settles — the web does the same for up to 20 tries, and marking
            // the tutorial "seen" against a HUD that was not ready yet would silently burn it.
            for (int i = 0; i < 20 && _active; i++)
            {
                yield return new WaitForSecondsRealtime(i == 0 ? 0.7f : 0.9f);
                if (!_active) yield break;
                if (Ui.TutorialGuide.StartTour()) yield break;
            }
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

        /// <summary>
        /// QC only — park the drone <paramref name="distance"/> units from <paramref name="target"/>,
        /// facing it, before a charge.
        ///
        /// The headless player runs at roughly 8 fps and FS is capped at 2.5, so five seconds of
        /// wall clock is only about 1.6 seconds of simulated time — the drone covered 7 units of
        /// the 110 it needed and the QC shot showed a shoal that was never approached. Starting
        /// just outside the panic radius makes the test about whether fish flee, not about how
        /// fast a CI runner is.
        /// </summary>
        public void QcPlaceNear(Vector3 target, float distance)
        {
            Vector3 from = new Vector3(_state.Pos.X, _state.Pos.Y, _state.Pos.Z);
            Vector3 dir = from - target;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) dir = new Vector3(1f, 0f, 0f);
            dir.Normalize();

            Vector3 at = target + dir * Mathf.Max(1f, distance);
            at.y = Mathf.Clamp(target.y, SeabedY(at) + 10f, _waterLevel - 10f);

            _state.Pos = new DroneFlight.Vec3(at.x, at.y, at.z);
            _state.Vel = new DroneFlight.Vec3(0f, 0f, 0f);
            _state.Yaw = Mathf.Atan2(target.z - at.z, target.x - at.x);
            _cam.transform.position = at;
            Debug.Log($"[Tour] qc placed at ({at.x:F0},{at.y:F0},{at.z:F0}) " +
                      $"{distance:F0}u from ({target.x:F0},{target.y:F0},{target.z:F0})");
        }

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

            // Swap in the hulls of whatever is close enough to swim into — a no-op until the diver
            // has actually moved (see RefreshSolids for the budget).
            RefreshSolids(new Vector3(_state.Pos.X, _state.Pos.Y, _state.Pos.Z), force: false);

            // The user's own "ความเร็วโดรน" preset. Read per frame rather than cached at Begin():
            // settings can be opened from the pause sheet mid-dive, and a speed change that only
            // took effect on the NEXT dive would read as a control that does nothing.
            _state = DroneFlight.Step(_state, sticks, dt, seabedY, _waterLevel,
                                      _solids, _scaleX, _scaleZ, SettingsStore.SpeedScale);

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
            // While the sticks are held (a QC charge, or a player actually flying) report every
            // 15 frames WITH the velocity. The 300-frame cadence told us where the drone was but
            // never how fast it was going, which is exactly the number C5 turns on — three QC
            // rounds went by arguing about whether 10.4 u/s was the drone or the measurement.
            bool flying = _qcCharge.HasValue || sticks.Ry != 0f || sticks.Lx != 0f;
            if (_frames == 30 || _frames % 300 == 0 || (flying && _frames % 15 == 0))
            {
                float sp = Mathf.Sqrt(_state.Vel.X * _state.Vel.X + _state.Vel.Z * _state.Vel.Z);
                float top = DroneFlight.Speed * SettingsStore.SpeedScale;
                Debug.Log($"[Tour] frame={_frames} pos=({pos.x:F1},{pos.y:F1},{pos.z:F1}) " +
                          $"yaw={_state.Yaw * Mathf.Rad2Deg:F0}° " +
                          // …and in metres per second, because "is 5.4 fast?" is unanswerable in
                          // world units and immediately answerable against a diver's 0.3-0.5 m/s.
                          $"velXZ={sp:F1}/{top:F1}u/s ({DroneFlight.MetresPerSecond(sp):F2} m/s) " +
                          $"ry={sticks.Ry:F2} " +
                          $"dt={dt:F3} fs={fs:F2} realDt={Time.deltaTime:F3} " +
                          $"depth={DroneFlight.DepthMetres(pos.y, _waterLevel):F1}m " +
                          $"seabedY={seabedY:F1}");
            }
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
