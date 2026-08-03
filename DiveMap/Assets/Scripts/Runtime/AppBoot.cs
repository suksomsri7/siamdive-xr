using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using DiveMap.Core;
using DiveMap.Runtime.Marine;

namespace DiveMap.Runtime
{
    /// <summary>
    /// Application bootstrap (WO-XR-01). Wires the flat-screen flow:
    ///   PlayerPrefs "shortId" (else demo) → AssetManifest.Load → MapApiClient.Fetch
    ///   → SceneBuilder.BuildRoutine → OrbitCamera.Frame.
    ///
    /// Builds a minimal uGUI Canvas entirely in code (status line, centre loading
    /// text, error text + retry button). No InputSystem — legacy UI input module.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AppBoot : MonoBehaviour
    {
        public string defaultShortId = MapApiClient.DefaultShortId;

        private OrbitCamera _orbit;
        private SceneBuilder _builder;

        private Text _statusText;
        private Text _centerText;
        private GameObject _errorPanel;
        private Text _errorText;

        private GameObject _mapRoot;
        private string _shortId;
        private bool _offline;

        /// <summary>True when the current map came off the disk rather than the network.</summary>
        public bool IsOffline => _offline;

        private void Start()
        {
            _shortId = PlayerPrefs.GetString("shortId", "");
            if (string.IsNullOrEmpty(_shortId)) _shortId = defaultShortId;

            SetupCamera();
            SetupLighting();
            SetupBuilder();
            BuildUi();

            StartCoroutine(Boot());
        }

        // ── Scene wiring ────────────────────────────────────────────────────────────

        private void SetupCamera()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            // Mid ocean-blue backdrop — the fallback if the gradient quad cannot be built. Read off
            // the ramp itself rather than typed in, so it cannot drift away from the gradient it is
            // standing in for (it did: the ramp moved to the web's stops in WO-E3 and a hard-coded
            // 0.30/0.52/0.66 would still be the old one).
            {
                SeabedGeom.Rgb bg = SeabedGeom.GradientStop(0.5f);
                cam.backgroundColor = new Color(bg.R, bg.G, bg.B, 1f);
            }

            // The seabed is now the web's full 340 u footprint (WO-XR-04.2), and the orbit
            // camera pulls back to 950 u — with Main.unity's 1000 u far plane the far half of
            // the sand would be sliced off mid-shot. The web scales its view range to the map
            // (updateViewRange); 9,000 u matches its underwater far range and still leaves
            // ample depth precision at this scene scale.
            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = 9000f;

            // WO-XR-04.2: the real thing — a screen-space vertical gradient like the web's
            // scene.background (bright surface haze on top → deep blue below). This, not fog,
            // is what gives the web its sense of depth.
            Backdrop.Attach(cam);

            // WO-E3: the film curve the web renders through (builder.html:485). Last thing in the
            // camera's chain and, because the uGUI canvas is ScreenSpaceOverlay, the last thing
            // BEFORE the UI rather than over it. Returns null and attaches nothing if the shader is
            // missing or unsupported — an untone-mapped frame, never a magenta one.
            AcesToneMapping.Attach(cam);

            _orbit = cam.GetComponent<OrbitCamera>();
            if (_orbit == null) _orbit = cam.gameObject.AddComponent<OrbitCamera>();
        }

        // ── Lighting (web builder.html parity) ───────────────────────────────────────
        // QC r2 lifted the *sand* (Unity Standard, metallic 0) with a bright Trilight
        // ambient — but the *wreck* stayed near-black. Root cause (QC r3 diagnosis):
        //
        //   The wreck GLB (HTMS_Chang_xr0) renders through glTFast's built-in-RP
        //   metallic-roughness shader and its baked metallicRoughness map reads highly
        //   metallic. In built-in RP a metal surface has ~zero diffuse albedo — its
        //   colour comes almost entirely from the *environment specular reflection*.
        //   Main.unity has no reflection probe (customReflection null, IndirectSpecular
        //   (0,0,0)), so metal reflected pure black → the hull went black and only the
        //   thin deck band the sun grazes caught any light. Sand is diffuse, so ambient
        //   lit it fine — hence bright sand + black wreck in the same shot.
        //
        // Fix is decoupled by shading path (no GLB/SceneBuilder edits, no shader-property
        // guesswork):
        //   • WRECK (specular)  ← a bright custom reflection cubemap. Metals reflect a lit
        //     underwater environment tinted by their own base colour (F0 = olive) → the
        //     hull reads bright green-olive all round. Does NOT touch diffuse sand.
        //   • SAND  (diffuse)   ← Trilight ambient, pulled down a touch from r2 so the
        //     cream sand settles to a natural tone. Barely moves the metallic wreck.
        //   • Sun soft-shadow strength eased so the camera-facing hull isn't a hard black
        //     self-shadow band (ambient + reflection now fill it).
        //
        // 🔴 That diagnosis is TRUE OF THE WRECK AND OF NOTHING ELSE. Do not reach for it again.
        // The marine and statue GLBs were pulled and their KTX2 maps decoded (thresher, tiger,
        // blacktip, whitetip, leopard, hammerhead, great white, Stone_King, white_cluster): every
        // one of them declares metallicFactor 1 but ships a metallic-roughness texture whose blue
        // channel averages 1-5 out of 255. That is 0.004 metal — a dielectric. The reflection
        // cubemap contributes about 3% to them and the metallic tame-down in SceneBuilder skips
        // them by design. Their base colour maps average sRGB 108-202; they are BRIGHT models.
        // When one of these goes black it is the ambient, not the material. See UnderwaterShading.
        /// <summary>An authored Core colour as a Unity one — the bands live in Core now.</summary>
        private static Color Rgb(SeabedGeom.Rgb c) => new Color(c.R, c.G, c.B);

        private void SetupLighting()
        {
            // 🔴 WO-E3 — THE WEB'S HEMISPHERE LIGHT, not four rounds of compensation.
            //
            // builder.html:510: `new THREE.HemisphereLight(0xbfe6ff, 0x123040, 1.05)`. Unity's
            // Trilight ambient is the same idea with a third band, so it maps directly: sky = the
            // web's sky colour × its intensity, ground = the web's ground colour × the same, and
            // the equator band is what a hemisphere light returns at the horizon — the midpoint.
            //
            // What was here before (sky 0.348/0.478/0.574 and two bands lifted again after the
            // "sharks are black on Posidon" report) is roughly HALF the web's sky term. It got
            // there honestly: in gamma, with no tone mapping, a sky band this bright clipped the
            // sand to white in wide shots, so it was pulled down — and then the shadowed side of
            // everything went black, so the lower bands were pushed back up, twice. That is the
            // signature of compensating for the pipeline rather than fixing it. With light adding
            // up in linear and ACES rolling off the top, the web's own numbers are the ones that
            // belong here, and the reason the web reads better at 512² than this app did at 2048²
            // is largely in these three lines: bright hemisphere, dark water.
            //
            // 🔴 WO-E4: the three literals moved to UnderwaterLight.Web*Band and are read from
            // there. They were the only copy in the project, which is exactly why the ground band
            // could be dimmed below the tone curve's crush point for months without anything being
            // able to notice: the floor that was supposed to catch it was built out of the water
            // colour and had no idea what band it was flooring. Now UnderwaterLight.GroundBandAt
            // and this line are the same numbers by construction, and that function is tested.
            RenderSettings.ambientMode         = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor     = Rgb(UnderwaterLight.WebSkyBand);     // 0xbfe6ff × 1.05
            RenderSettings.ambientEquatorColor = Rgb(UnderwaterLight.WebEquatorBand); // hemisphere at the horizon = midpoint
            RenderSettings.ambientGroundColor  = Rgb(UnderwaterLight.WebGroundBand);  // 0x123040 × 1.05
            // WO-XR-04.3: the web's underwater fog — THREE.Fog(0x123a55, near, far) with
            // near = max(500, reach·1.1) and far = max(9000, maxD·3.4). At orbit distance this
            // is only a 3-7% wash (Fable's survey), and that is the point: it must colour the
            // far rim of a big map, not haze over the wreck. The scene's RenderSettings also
            // ship with fog enabled so the linear-fog shader variants survive build stripping.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            // 🔴 WO-E3: back to the web's #123a55, and this time it is safe to be.
            //
            // It was abandoned because the backdrop drawn behind it had been lifted to a bright
            // cyan ramp, so anything fading toward this colour read as a silhouette against the
            // background — true, and the wrong thing to change. The backdrop is the web's four
            // stops again (SeabedGeom), on which #123a55 is a point (WaterFog.FogRampV), so the fog
            // and the background are now the same water by arithmetic. WaterFog.ColorAt(0) IS this
            // colour; DepthAtmosphere dims it with depth, together with the backdrop and the
            // ambient, by one shared vector.
            {
                SeabedGeom.Rgb f = WaterFog.ColorAt(0f);
                RenderSettings.fogColor = new Color(f.R, f.G, f.B);
            }
            RenderSettings.fogStartDistance = 500f;
            RenderSettings.fogEndDistance = 9000f;

            // Custom reflection so the metallic wreck reflects a lit underwater environment
            // instead of black. Uniform bright blue-white cubemap; a metal surface's spec
            // colour is its own base colour (olive), so the hull reads bright green-olive.
            // Diffuse sand is unaffected — reflection only feeds specular. (built-in RP:
            // feeds unity_SpecCube0 globally via DefaultReflectionMode.Custom.)
            //
            // 🔴 WO-E3: intensity 1 → 0.3, and ONLY as part of this change.
            // HANDOFF §6 records that lowering it on its own was measured and made things WORSE:
            // a 4×4 mip-less cube is a poor reflection but it was carrying real light onto surfaces
            // that had nothing else, because the normal maps were being thrown away (gamma) and
            // nothing rolled off the top of the range (no tone mapping). Both of those reasons are
            // gone in this commit, and a flat blue cube at full strength is now the thing flattening
            // wet surfaces — it adds the same constant to every pixel regardless of which way it
            // faces, which is the definition of losing surface detail.
            RenderSettings.defaultReflectionMode  = UnityEngine.Rendering.DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = AmbientReflectionCube(new Color(0.60f, 0.72f, 0.82f));
            RenderSettings.reflectionIntensity     = 0.3f;

            // Key light (sun): reuse the scene's directional if there is one, else make it.
            Light sun = null;
            foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional && l.gameObject.name != "FillLight") { sun = l; break; }
            if (sun == null)
            {
                var sunGo = new GameObject("Sun");
                sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional;
            }
            sun.color = new Color(1f, 0.957f, 0.839f); // 0xfff3df
            // 🔴 WO-E3: 0.82 → 1.2, the web's own figure (builder.html:511,
            // `new THREE.DirectionalLight(0xfff3df, 1.2)`).
            //
            // The reasoning for 0.82 — "under 50 m the sun is a direction, not a key light" — is
            // good physics and was the wrong lever: the light a diver loses with depth is now taken
            // off by DepthLight, which multiplies the ambient AND the water AND the background by
            // the same curve. Dimming the key on top of that was a second, permanent, depth-blind
            // dimmer, and its real effect was to flatten the shading gradient that carries surface
            // detail — the complaint this whole work order exists for. It also clipped less than it
            // looked like it did: what was "the sand clips to white in wide shots" is the shoulder
            // ACES now handles.
            sun.intensity = 1.2f;
            sun.transform.rotation = Quaternion.Euler(52f, -35f, 0f); // high, angled — web pos (60,160,70)
            // 🔑 The user asked for shadows to STAY (they are not in the web at all). Kept, but
            // eased: 0.5 → 0.35. The shadow term multiplies whatever light is left after the depth
            // curve, and with the ambient now taking the full curve instead of half of it, a
            // half-strength shadow on a deep flank was removing light that is no longer there to
            // spare. 0.35 keeps the shape a shadow gives an object without punching a hole in it.
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.35f;
            // 🔴 ProjectSettings ships Android and iPhone on quality level 2 ("Medium"), which is
            // pixelLightCount = 1. ONE per-pixel light on the phone, whatever the editor or the CI
            // screenshots (level 5, four lights) suggest. With the drone out there are five lights
            // in the scene and the auto ranking is by intensity and distance, so a 2.6 headlamp
            // spot right next to a statue could take the only slot and drop the sun — leaving the
            // statue lit by a cone with hard black either side of it. Pinning the sun to per-pixel
            // costs nothing (it already held the slot most of the time) and makes it deterministic.
            sun.renderMode = LightRenderMode.ForcePixel;
            RenderSettings.sun = sun;

            // Fill light: cool bounce from the far side so the wreck's shadowed hull reads
            // as olive-green instead of black (idempotent by name across Retry/rebuilds).
            const string fillName = "FillLight";
            var existing = GameObject.Find(fillName);
            Light fill = existing != null ? existing.GetComponent<Light>() : null;
            if (fill == null)
            {
                var fillGo = new GameObject(fillName);
                fill = fillGo.AddComponent<Light>();
                fill.type = LightType.Directional;
                fill.shadows = LightShadows.None;
            }
            fill.color = new Color(0.247f, 0.471f, 0.659f); // 0x3f78a8
            fill.intensity = 0.5f;                           // WO-E3: the web's own 0.5 (builder.html:512)
            fill.transform.rotation = Quaternion.Euler(-14f, 145f, 0f); // opposite/low — web pos (-90,40,-70)
            // ⚠️ On the phone this light is NOT per-pixel (see the note on the sun): with one slot
            // and the sun holding it, the fill is folded into the ambient probe instead. Tuning its
            // intensity therefore does much less on a device than it appears to do in a CI
            // screenshot — the job it was hired for, opening up shadowed undersides, is carried by
            // ambientGroundColor above and by the underwater floor. Left in because in SH it still
            // costs nothing and it does hold the Windows/desktop build together.
        }

        // A tiny uniform-colour cubemap used as the scene's custom reflection. Built-in RP
        // has no reflection probe in Main.unity, so metallic surfaces (the wreck) would
        // otherwise reflect black. Cached so Retry/rebuild doesn't leak a new cubemap.
        private static Cubemap _reflectionCube;
        private static Cubemap AmbientReflectionCube(Color c)
        {
            if (_reflectionCube != null) return _reflectionCube;
            const int n = 4;
            var cube = new Cubemap(n, TextureFormat.RGBA32, false);
            var px = new Color[n * n];
            for (int i = 0; i < px.Length; i++) px[i] = c;
            for (var f = CubemapFace.PositiveX; f <= CubemapFace.NegativeZ; f++)
                cube.SetPixels(px, f);
            cube.Apply(false);
            _reflectionCube = cube;
            return cube;
        }

        private void SetupBuilder()
        {
            var go = new GameObject("SceneBuilder");
            _builder = go.AddComponent<SceneBuilder>();
        }

        // ── Boot flow ───────────────────────────────────────────────────────────────

        private IEnumerator Boot()
        {
            HideError();
            ShowCenter("กำลังโหลดแมพ…");
            SetStatus(UiStrings.Tr("กำลังเชื่อมต่อ…"));

            if (_mapRoot != null)
            {
                Destroy(_mapRoot);
                _mapRoot = null;
            }

            // Manifest (non-fatal: without it every item becomes a placeholder).
            AssetManifest manifest = null;
            string manifestErr = null;
            yield return AssetManifest.Load(m => manifest = m, e => manifestErr = e);
            if (manifestErr != null)
                Debug.LogWarning("[AppBoot] manifest: " + manifestErr);
            // The palette browses the same registry the scene builder resolves ids against —
            // one load, one list, so the shop can never offer something the map cannot build.
            if (manifest != null) Manifest = manifest;

            // Scene from production API (fatal on failure → retry).
            SceneData scene = null;
            string fetchErr = null;
            yield return MapApiClient.Fetch(_shortId, s => scene = s, e => fetchErr = e);

            if (fetchErr != null || scene == null)
            {
                // No signal, or the server said no. If this map has been opened before, its copy
                // is on disk — that is the whole point of keeping one.
                scene = OfflineStore.Load(_shortId);
                if (scene == null)
                {
                    ShowError(fetchErr ?? "โหลดแมพไม่สำเร็จ");
                    yield break;
                }
                Debug.Log("[AppBoot] served " + _shortId + " from the offline copy");
                SetStatus(UiStrings.Tr("โหมดออฟไลน์ — ใช้สำเนาในเครื่อง"));
                _offline = true;
            }
            else _offline = false;

            // E5 — put the player's own purchases back into the map before it is built, so a
            // bought animal goes through exactly the same pipeline as everything else rather
            // than down a second spawn path that would drift out of step with this one.
            int restocked = ShopStock.InjectFromStore(scene, _shortId);
            if (restocked > 0) Debug.Log($"[Shop] restored {restocked} purchased item(s) for {_shortId}");

            // Keep the scene JSON around: it is what a save writes back, and the rev is what
            // stops that save from clobbering an edit made on another device.
            CurrentScene = scene;
            CurrentRev = scene.Root["rev"] != null ? (int)scene.Root["rev"] : -1;
            // Keep a copy of every map that loads. "Maps you have opened" and "maps you can open
            // offline" are then the same set — there is no download step to forget.
            if (!_offline) OfflineStore.Save(_shortId, scene);
            CanEditCurrent = scene.Root["canEdit"] != null && (bool)scene.Root["canEdit"];
            Debug.Log($"[AppBoot] map {_shortId} rev={CurrentRev} canEdit={CanEditCurrent} " +
                      $"policy={scene.Root["editPolicy"]}");

            // Start (or switch) the editing session. Undo history is per map: undoing across two
            // maps would write one map's items into the other.
            MapEditor.Begin(_shortId, SceneEdit.Items(scene));

            string mapName = string.IsNullOrEmpty(scene.Name) ? _shortId : scene.Name;
            SetStatus(mapName + " · " + UiStrings.Tr("กำลังวางวัตถุ…"));

            SceneBuilder.BuildResult result = default;
            bool done = false;
            yield return _builder.BuildRoutine(scene, manifest, r => { result = r; done = true; });

            if (!done)
            {
                ShowError("สร้างแมพไม่สำเร็จ");
                yield break;
            }

            _mapRoot = result.Root;
            RopeSystem.Load(scene);   // env.ropes → tubes, once the objects they tie to exist
            HideCenter();
            HideError();

            string title = string.IsNullOrEmpty(result.MapName) ? mapName : result.MapName;
            SetLoadSummary(title, result.Loaded, result.Failed);

            if (_orbit != null)
                _orbit.FrameBox(result.FrameCenter, result.FrameSizeX, result.FrameSizeY, result.FrameSizeZ, result.FrameMinY);

            // ── Tour (P1.1) ─────────────────────────────────────────────────────────
            // Hand the drone its world: what to collide with, where the surface is, how the
            // seabed is stretched, and where "home" is for the exit re-frame.
            TourController.Configure(result);
            ArSession.Configure(result);   // F1 — AR needs the footprint to place the viewer
            EnvMode.Reset();   // new scene, new lights/water to capture
            // shallow bright, deep dark and blue — and, since WO-E5, the fog range too: it is
            // derived from the map's own size and where the camera is standing rather than from the
            // web's orbit-framing constants, which could not reach a map this small (WaterFog.RangeAt).
            DepthAtmosphere.Configure(result.WaterLevel, result.Center, result.Radius);
            // …and the floor under all of that: however many dimmers stack on the way down, an
            // object underwater is never lit by less than the water it is standing in.
            UnderwaterShading.Configure(result.WaterLevel);
            Ui.PerfHud.Apply();   // A7 — rebuild the readout if the player left it on

            // D9/E8 — a diver who left through a warp gate lands IN the destination, at a random
            // point, rather than being handed the map screen. Flag cleared on use, so cancelling a
            // warp cannot hijack the next map the player opens.
            bool arena = TourController.ArenaPlay;
            bool warped = TourController.ArrivingByWarp;
            bool autoPlay = Core.ArenaEntry.ShouldAutoPlay(
                accountId: (string)scene.Root["accountId"],
                canEdit: scene.Root["canEdit"] != null && (bool)scene.Root["canEdit"],
                arenaPlay: arena,
                arrivedByWarp: warped,
                online: Application.internetReachability != NetworkReachability.NotReachable,
                arMode: ArSession.Active);

            // Both flags are cleared whatever the gate decided: a cancelled warp or a world the
            // player backed out of must not hijack the next map they open.
            TourController.ArrivingByWarp = false;
            TourController.ArenaPlay = false;

            // 🔴 …except in -qcshot mode, where the tour is what broke the evidence.
            //
            // The QC map belongs to SIAMDIVE and cannot be edited, so ArenaEntry drops the player
            // into the tour 600 ms after the map loads and the drone re-drives Camera.main every
            // frame from then on. QcShot below disables the ORBIT rig before aiming the camera, but
            // nothing stopped the drone: both screenshots were therefore taken from the drone's
            // pose, into open water, and came out as the same gradient — byte-identical files that
            // were read for weeks as "two angles of the map". UiShell's -qcui pass had already hit
            // this and leaves the tour explicitly (UiShell:812); this is the same fix at the source.
            // -qcui is untouched, so its shots keep their current behaviour.
            if (autoPlay && string.IsNullOrEmpty(GetArg("-qcshot")))
            {
                Debug.Log($"[Tour] auto-play (arena={arena} warp={warped}) → tour at a random spawn");
                StartCoroutine(StartTourAfterDelay());
            }
            else if (autoPlay)
            {
                Debug.Log("[Tour] auto-play suppressed — -qcshot needs a camera nobody else is flying");
            }

            // ── Sun shafts (WO-XR-04.3) ─────────────────────────────────────────────
            // Scattered around the content, from the water surface down to just under the
            // seabed, all parallel to the sun set in SetupLighting.
            if (result.Root != null && result.WaterLevel > result.FrameMinY + 5f)
            {
                float spread = Mathf.Clamp(result.Radius * 0.45f, 60f, 220f);
                float length = Mathf.Clamp(result.WaterLevel - result.FrameMinY + 20f, 60f, 400f);
                // Sun shafts, back on at the user's request. They were switched off while hunting
                // "blue wedges on the water" that turned out to be the water disc z-fighting with
                // its own underside — the shafts were never the thing being reported. What they
                // keep from that hunt: they fade as the view turns along them, they fade out above
                // the surface, and they start 8 m down so nothing lies across the waterline.
                GodRays.Attach(result.Root.transform, result.FrameCenter, spread, result.WaterLevel, length);
            }

            // ── QC screenshot mode (CI): -qcshot <path> → รอเฟรม settle → แคป → ปิดตัวเอง ──
            // ใช้ใน headless CI (xvfb) เพื่อให้ orchestrator เห็นภาพจริงทุก build (QC_PLAN ชั้น 2)
            string qcPath = GetArg("-qcshot");
            if (!string.IsNullOrEmpty(qcPath))
            {
                // FishSchoolSystem sits on the Map root (added by SceneBuilder when there are
                // schools); the close-up angle-2 uses it to find the scad shoal nearest the wreck.
                var marine = _mapRoot != null ? _mapRoot.GetComponent<FishSchoolSystem>() : null;
                StartCoroutine(QcShot(qcPath, marine, result.FrameCenter, result.WaterLevel));
            }
        }

        private static string GetArg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        private IEnumerator QcShot(string path, FishSchoolSystem marine, Vector3 boatCenter,
                                   float waterLevel)
        {
            // ── มุมที่ 1: wide framing (เดิม) ────────────────────────────────────────
            // รอให้ render settle 2 วิ (GLB วาง เฟรมแรกๆ อาจยังไม่ครบ)
            yield return new WaitForSeconds(2f);
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log($"[QC] screenshot -> {path}");
            yield return new WaitForSeconds(1f); // ให้ไฟล์เขียนเสร็จ

            // ── มุมที่ 2: โคลสอัพฝูง scad ที่ใกล้เรือที่สุด (เห็นทั้งฝูง + เรือเป็นฉากหลัง) ──
            // path2 = qc_screenshot.png → qc_screenshot2.png (โฟลเดอร์เดียวกัน)
            string dir = Path.GetDirectoryName(path);
            string path2 = Path.Combine(
                string.IsNullOrEmpty(dir) ? "." : dir,
                Path.GetFileNameWithoutExtension(path) + "2" + Path.GetExtension(path));

            if (marine != null &&
                marine.TryGetNearestSchool(boatCenter, "scad", out Vector3 anchor, out float homeR))
            {
                Camera cam = Camera.main;
                // Deterministic pose — disable the orbit controller so its Update() can't
                // re-drive the transform back to the wide-framing yaw/pitch each frame.
                if (_orbit != null) _orbit.enabled = false;

                // Look from BEYOND the shoal (far side from the wreck) back toward it, so the
                // wreck sits behind the fish as a backdrop. The web-accurate scad shoal now
                // spans SR≈66 u (homeR≈79), so the old 25-30 u stand-off put the camera INSIDE
                // the swarm — pull back proportionally instead.
                Vector3 fwd = anchor - boatCenter; fwd.y = 0f;
                if (fwd.sqrMagnitude < 1e-3f) fwd = Vector3.forward;
                fwd.Normalize();
                float dist = Mathf.Clamp(homeR * 1.6f, 40f, 220f);
                Vector3 camPos = anchor + fwd * dist + Vector3.up * (dist * 0.28f);
                cam.transform.position = camPos;
                cam.transform.LookAt(anchor);
                Debug.Log($"[QC] angle2 anchor={anchor} camPos={camPos} dist={dist:F1} homeR={homeR:F1}");

                // รอ 4 เฟรม + เศษเวลา ให้ boids เดินต่อและ transform นิ่ง
                yield return null; yield return null; yield return null; yield return null;
                yield return new WaitForSeconds(0.25f);
                ScreenCapture.CaptureScreenshot(path2);
                Debug.Log($"[QC] screenshot2 -> {path2}");
                yield return new WaitForSeconds(1f);
            }
            else
            {
                Debug.LogWarning($"[QC] no scad school for angle2 (marine={(marine != null)}) — skipping {path2}");
            }

            // ── ใบที่ 3-8: โมเดลจริงจาก CDN ตัวละใบ + นับพิกเซลดำ ────────────────────
            // The two angles above photograph a scene built almost entirely from geometry this
            // code generates: only four GLBs in it come off the CDN. Everything the model bugs
            // live in — 222 of 226 modules — has never been in a CI frame. This is that frame.
            yield return QcModelShot.Run(dir, Manifest, boatCenter);

            // ── WO-E3: the same animal at 15 / 30 / 52 m, tone curve on and off ─────
            // The model pass above photographs each model at ONE depth and asks "is it dark". The
            // user's report was not about darkness, it was about the subject falling behind its own
            // background as the camera descended — a slope, not a value. This is the only frame in
            // CI that can see that, and it is the acceptance evidence for this work order.
            yield return QcModelShot.RunDepth(dir, Manifest, boatCenter, waterLevel);

            // ── WO-F3: do the animated models actually ANIMATE? ────────────────────
            // Every pass above measures pixels, and no number of pixels can answer that: a rigged
            // animal frozen on frame 0 photographs exactly like a rigged animal mid-stroke. This
            // one takes no picture — it loads three rigged GLBs, plays them, and measures how far
            // the skeleton moved. Last, so that a failure or a timeout in it cannot cost any of
            // the passes above their evidence.
            yield return QcAnimShot.Run(boatCenter);

            // ── WO-E5f: two files, one variable, one frame each ────────────────────
            // Every theory this session has produced about the dark models was argued from
            // statistics taken off the files, and two of them were wrong — one explained the wrong
            // number, the other was a window artefact quoted as a property. What none of them could
            // do is change one input and look. These pairs differ in exactly one thing and are
            // byte-identical in everything else, so the two frames cannot disagree for any other
            // reason. First, because it is the pass whose answer decides what the rest is for.
            yield return QcPilotAb.Run(dir, Manifest, boatCenter);

            // ── WO-E5d: WHERE do the Atlantis ruins lose their light? ──────────────
            // In DAYLIGHT, against a reference surface of known albedo, one shading input removed
            // at a time. Every offline explanation for "ซุ้มดำ" has been measured on the files and
            // cleared, and the user's own daylight screenshot rules out the water: a surface of the
            // ruins' measured albedo lit by nothing but the daylight ambient works out at byte
            // 80-100, and the dome's body photographs at byte 3. This is the pass that says which
            // rung loses it. Before QcMapShot, because that one rebuilds the scene.
            yield return QcRuinLadder.Run(dir, Manifest, boatCenter);

            // ── WO-E5: the maps themselves, from where the player's eyes are ───────
            // Everything above photographs either nine models alone in a studio or ONE map from an
            // orbit pose. The user's answer to that was "ผมถามคุณ QC ยังไงครับ คุณไม่ได้ถ่ายรูป
            // แคปเจอร์หน้าจอมาดูเองด้วยหรอ" — and they were right: Atlantis, Posidon and Hanuman,
            // the three maps every complaint has been about, had never been in a CI frame.
            //
            // LAST, because it rebuilds the scene one map at a time and destroys what is on screen
            // to do it. Nothing runs after it but the quit, so there is nothing left to break.
            yield return QcMapShot.Run(dir, _builder, Manifest, _mapRoot);
            _mapRoot = null;

            Application.Quit(0);
        }

        private void Retry()
        {
            // StopAllCoroutines kills whatever build was in flight, and a stopped coroutine does
            // not clean up after itself: the "Map" root it had already created, with its seabed,
            // its water and every item loaded so far, stays in the scene with nobody holding it.
            // Reloading a map is exactly what the shop does after a purchase, so this leaked a
            // whole ghost map on a path users take often — two seabeds, two wrecks, twice the
            // draw calls, and no error anywhere. AR is what finally made it visible.
            StopAllCoroutines();
            _rebuild = null;
            if (_builder != null) _builder.DiscardInFlight();
            StartCoroutine(Boot());
        }

        /// <summary>The map on screen right now (E5 stores purchases against it).</summary>
        public string CurrentMapId => _shortId;

        /// <summary>
        /// The asset registry, once the boot sequence has read it. Static because the palette
        /// outlives any one map load and must not re-download StreamingAssets to draw a grid.
        /// Null until the first load finishes.
        /// </summary>
        public static AssetManifest Manifest { get; private set; }

        /// <summary>
        /// The scene as loaded, purchases already injected. Saving writes THIS back — the built
        /// GameObjects are a rendering of it, not the source of truth, so a save never has to
        /// reverse-engineer the scene graph.
        /// </summary>
        public SceneData CurrentScene { get; private set; }

        /// <summary>
        /// The map's revision when it was fetched, for the PATCH optimistic-concurrency guard.
        /// -1 = unknown (the GET did not carry one), which makes the save unguarded.
        /// </summary>
        public int CurrentRev { get; private set; } = -1;

        /// <summary>
        /// The server's verdict on whether THIS account may write to THIS map (route.ts:93).
        /// False on every admin world map (editPolicy "none"), which is most of what a player
        /// dives into — so a purchase there is kept on the device rather than saved, and the
        /// player is told which of the two happened.
        /// </summary>
        public bool CanEditCurrent { get; private set; }

        /// <summary>
        /// E5 — rebuild the map that is already open. Used after a purchase: the animal has been
        /// written to the stock, and a rebuild is what puts it in the water through the normal
        /// item pipeline. A few seconds of reload is a fair price for having one build path
        /// instead of two that can disagree.
        /// </summary>
        public void ReloadCurrentMap() => Retry();

        /// <summary>
        /// Rebuild from the scene ALREADY in memory, without going back to the server.
        ///
        /// An edit changes <see cref="CurrentScene"/>; re-fetching would throw that away and
        /// redraw the last saved copy — the change would appear to undo itself. This is the path
        /// every editing operation uses; <see cref="ReloadCurrentMap"/> stays for the cases that
        /// genuinely want the server's version back.
        /// </summary>
        public void RebuildFromMemory()
        {
            if (CurrentScene == null) { Retry(); return; }
            if (_rebuild != null)
            {
                // Stopping a coroutine does not undo what it has already done. The build it was
                // running had already created its "Map" root and hung a seabed, a water disc and
                // every loaded item off it — all of which would keep rendering behind the new map
                // with nobody holding a reference to them.
                StopCoroutine(_rebuild);
                if (_builder != null) _builder.DiscardInFlight();
            }
            _rebuild = StartCoroutine(RebuildRoutine());
        }
        private Coroutine _rebuild;

        private IEnumerator RebuildRoutine()
        {
            if (_mapRoot != null) { Destroy(_mapRoot); _mapRoot = null; }

            SceneBuilder.BuildResult result = default;
            bool done = false;
            yield return _builder.BuildRoutine(CurrentScene, Manifest, r => { result = r; done = true; });
            if (!done) yield break;

            _mapRoot = result.Root;
            RopeSystem.Load(CurrentScene);
            TourController.Configure(result);
            ArSession.Configure(result);   // F1 — AR needs the footprint to place the viewer
            EnvMode.Reset();
            UnderwaterShading.Configure(result.WaterLevel);
            Ui.PerfHud.Apply();

            if (result.Root != null && result.WaterLevel > result.FrameMinY + 5f)
            {
                float spread = Mathf.Clamp(result.Radius * 0.45f, 60f, 220f);
                float length = Mathf.Clamp(result.WaterLevel - result.FrameMinY + 20f, 60f, 400f);
                // Sun shafts, back on at the user's request. They were switched off while hunting
                // "blue wedges on the water" that turned out to be the water disc z-fighting with
                // its own underside — the shafts were never the thing being reported. What they
                // keep from that hunt: they fade as the view turns along them, they fade out above
                // the surface, and they start 8 m down so nothing lies across the waterline.
                GodRays.Attach(result.Root.transform, result.FrameCenter, spread, result.WaterLevel, length);
            }

            Debug.Log($"[AppBoot] rebuilt from memory · items={result.Loaded} failed={result.Failed}");
            _rebuild = null;
        }

        /// <summary>
        /// Switch to another dive-site map (WO-XR-05.2 map list). Persisting the id also
        /// gives "remember the last map" for free, because Start() already reads the
        /// PlayerPrefs "shortId" before falling back to the demo site.
        /// </summary>
        public void LoadMap(string shortId)
        {
            if (string.IsNullOrEmpty(shortId)) return;
            _shortId = shortId;
            PlayerPrefs.SetString("shortId", shortId);
            PlayerPrefs.Save();
            Retry();
        }

        // ── UI (built in code) ────────────────────────────────────────────────────────

        private void BuildUi()
        {
            EnsureEventSystem();

            var canvasGo = new GameObject("BootCanvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            // Bundled Noto Sans Thai (Latin + Thai) — the Linux CI player has no Thai
            // system font, so the builtin LegacyRuntime.ttf drops every Thai glyph.
            Font font = UiFont.Get();

            // Status line — the web's #count slot: CENTRED at top 96, 11 px, muted
            // (builder.html:90). It used to be a 26-unit line pinned top-left, which is a slot the
            // web does not use at all.
            _statusText = MakeText(canvas.transform, "Status", font, Ui.UiKit.CssFont(11f),
                                   TextAnchor.UpperCenter);
            var sRt = _statusText.rectTransform;
            sRt.anchorMin = new Vector2(0f, 1f);
            sRt.anchorMax = new Vector2(1f, 1f);
            sRt.pivot = new Vector2(0.5f, 1f);
            sRt.anchoredPosition = new Vector2(0f, -Ui.UiKit.Css(96f));
            sRt.sizeDelta = new Vector2(-Ui.UiKit.Css(24f), Ui.UiKit.RowHeight(Ui.UiKit.CssFont(11f)));
            _statusText.color = new Color(0.624f, 0.714f, 0.788f, 1f);   // --mut #9fb6c9
            _statusText.text = "";

            // Centre loading text (the web's #toast slot uses the same middle-of-screen position).
            _centerText = MakeText(canvas.transform, "Center", font, Ui.UiKit.CssFont(15f),
                                   TextAnchor.MiddleCenter);
            var cRt = _centerText.rectTransform;
            cRt.anchorMin = new Vector2(0.5f, 0.5f);
            cRt.anchorMax = new Vector2(0.5f, 0.5f);
            cRt.pivot = new Vector2(0.5f, 0.5f);
            cRt.anchoredPosition = Vector2.zero;
            cRt.sizeDelta = new Vector2(900f, 200f);

            // Error panel: message + retry button.
            _errorPanel = new GameObject("ErrorPanel");
            _errorPanel.transform.SetParent(canvas.transform, false);
            var eRt = _errorPanel.AddComponent<RectTransform>();
            eRt.anchorMin = new Vector2(0.5f, 0.5f);
            eRt.anchorMax = new Vector2(0.5f, 0.5f);
            eRt.pivot = new Vector2(0.5f, 0.5f);
            eRt.anchoredPosition = Vector2.zero;
            // The web's modal geometry (#nameModal/#leaveModal, builder.html:67): 86vw capped at
            // 380 CSS px, 20 px radius, 20 px padding — glass, not a bare label on the scene.
            float modalW = Mathf.Min(Screen.width / Ui.UiKit.CanvasScale * 0.86f, Ui.UiKit.Css(380f));
            eRt.sizeDelta = new Vector2(modalW, Ui.UiKit.Css(190f));

            Image modalBg = Ui.UiKit.MakeRounded(_errorPanel.transform, "Bg", Ui.UiKit.Glass, 20f);
            Ui.UiKit.Stretch(modalBg.rectTransform);

            _errorText = MakeText(_errorPanel.transform, "ErrorText", font, Ui.UiKit.CssFont(16f),
                                  TextAnchor.UpperCenter);
            _errorText.fontStyle = FontStyle.Bold;      // the web's 600 weight
            var etRt = _errorText.rectTransform;
            etRt.anchorMin = new Vector2(0f, 0f);
            etRt.anchorMax = new Vector2(1f, 1f);
            etRt.offsetMin = new Vector2(Ui.UiKit.Css(20f), Ui.UiKit.Css(70f));
            etRt.offsetMax = new Vector2(-Ui.UiKit.Css(20f), -Ui.UiKit.Css(20f));
            _errorText.color = Ui.UiKit.TextMain;

            MakeButton(_errorPanel.transform, font, "ลองใหม่", Retry);

            _errorPanel.SetActive(false);
        }

        private static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        private static Text MakeText(Transform parent, string name, Font font, int size, TextAnchor anchor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = Color.white;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        private static void MakeButton(Transform parent, Font font, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("RetryButton");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            // The web's modal button row: radius 13, padding 13, 14px/600, accent fill with
            // #04121f text (builder.html:72-75).
            rt.anchoredPosition = new Vector2(0f, Ui.UiKit.Css(20f));
            rt.sizeDelta = new Vector2(Ui.UiKit.Css(150f), Ui.UiKit.Css(44f));

            var img = go.AddComponent<Image>();
            img.color = Ui.UiKit.Accent;
            img.sprite = Ui.UiKit.RoundedSprite(13f);
            img.type = Image.Type.Sliced;

            var btn = go.AddComponent<Button>();
            btn.onClick.AddListener(onClick);

            var txtGo = new GameObject("Text");
            txtGo.transform.SetParent(go.transform, false);
            var txt = txtGo.AddComponent<Text>();
            txt.font = font;
            txt.fontSize = Ui.UiKit.CssFont(14f);
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Ui.UiKit.OnAccent;
            txt.text = label;
            var trt = txt.rectTransform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
        }

        private void SetStatus(string s) { if (_statusText != null) _statusText.text = s; }

        // ── status line language (P0) ─────────────────────────────────────────────
        // The header is the one label the shell's re-translate pass cannot fix: it is a
        // COMPOSED string ("Htms Chang · โหลดแล้ว 12 · แทนที่ 2"), and UiShell.ApplyLanguage
        // looks each Text's whole content up in the table, so a composed line never matches
        // and stayed Thai in English. Keep the parts and re-compose on demand instead.
        private string _summaryTitle;
        private int _summaryLoaded = -1;
        private int _summaryFailed;

        /// <summary>
        /// The web's <c>setTimeout(…, 600)</c>. The pause is not cosmetic: the last GLBs are still
        /// arriving as the map "finishes", and dropping the diver in mid-load starts the tour
        /// inside a world that is still growing objects around them.
        /// </summary>
        private System.Collections.IEnumerator StartTourAfterDelay()
        {
            yield return new WaitForSeconds(Core.ArenaEntry.StartDelaySeconds);
            if (this == null || _mapRoot == null) yield break;   // player left while we waited
            TourController.Start(randomStart: true);
        }

        private void SetLoadSummary(string title, int loaded, int failed)
        {
            _summaryTitle = title;
            _summaryLoaded = loaded;
            _summaryFailed = failed;
            RenderLoadSummary();
        }

        private void RenderLoadSummary()
        {
            if (_summaryLoaded < 0) return;
            // The status line is off the screen at the user's request — it sat over the map for
            // the whole session to report a number nobody needs after the map has drawn. The same
            // text still goes to the log, where a QC run or a bug report can read it, and the
            // build number moved into the menu so a screenshot can still be dated.
            string line = $"{_summaryTitle}  ·  {UiStrings.Tr("โหลดแล้ว")} {_summaryLoaded} · " +
                          $"{UiStrings.Tr("แทนที่")} {_summaryFailed}{Core.BuildStamp.Suffix}" +
                          $"{Core.BuildStamp.ScreenInfo}";
            Debug.Log("[UI] " + line);
            SetStatus("");
        }

        /// <summary>
        /// Show/hide the status line. The web hides #count in the tour (builder.html:233) — the
        /// depth pill and the hint line own the top of the screen there.
        /// </summary>
        public void SetStatusVisible(bool visible)
        {
            if (_statusText != null) _statusText.enabled = visible;
        }

        /// <summary>Called by <c>UiShell.ApplyLanguage</c> after a language switch.</summary>
        public void RefreshStatusLanguage() => RenderLoadSummary();
        private void ShowCenter(string s) { if (_centerText != null) { _centerText.text = s; _centerText.gameObject.SetActive(true); } }
        private void HideCenter() { if (_centerText != null) _centerText.gameObject.SetActive(false); }

        private void ShowError(string s)
        {
            // Translate here rather than at every call site: every error string in this class is
            // a Thai source key, and one missed Tr() is an English UI with a Thai error box.
            s = UiStrings.Tr(s);
            HideCenter();
            if (_errorText != null) _errorText.text = s;
            if (_errorPanel != null) _errorPanel.SetActive(true);
        }

        private void HideError() { if (_errorPanel != null) _errorPanel.SetActive(false); }
    }
}
