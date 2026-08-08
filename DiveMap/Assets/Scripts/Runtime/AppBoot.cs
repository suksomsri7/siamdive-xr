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

        /// <summary>
        /// One-shot pin for the 1K experiment (2026-08-07): the user judges texture ladders on
        /// the real device, and the first thing they should see after installing this build is
        /// Htms Chang — the map whose whale shark they know byte-for-byte. PlayerPrefs "shortId"
        /// remembers the last map, so a device that was in Atlantis would boot into Atlantis and
        /// the comparison would start on the wrong reef. Bump the number to re-pin on a later
        /// build; leaving it alone makes this a no-op after the first launch.
        /// </summary>
        private const string FirstMapPin = "299";

        private void Start()
        {
            _shortId = PlayerPrefs.GetString("shortId", "");
            if (PlayerPrefs.GetString("firstMapPin", "") != FirstMapPin)
            {
                PlayerPrefs.SetString("firstMapPin", FirstMapPin);
                _shortId = "";   // fall through to the default map (Htms Chang) once
            }
            if (string.IsNullOrEmpty(_shortId)) _shortId = defaultShortId;

            // 🔴 ราก "30fps สีเหลืองนิ่งสนิท" (fps badge ของ user, 8 ส.ค.): Unity บน iOS
            // ล็อก targetFrameRate ไว้ที่ 30 โดย default — แอปนี้ไม่เคยตั้งค่า จึงวิ่งครึ่งจอ
            // มาตลอดไม่ว่าเครื่องแรงแค่ไหน · ที่ 30fps ระบบเลี้ยว "ต่อเฟรม" ก้าวใหญ่ 2 เท่า
            // (Fs=2) = ปลากระตุก · ตั้ง 60 ให้เต็มจอมาตรฐาน (ProMotion 120 ไว้ทีหลัง).
            Application.targetFrameRate = 60;

            Ui.FpsBadge.Ensure();   // เลข fps มุมจอ — วิดีโอทุกคลิปจาก user กลายเป็นเครื่องวัด
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
            // Mid ocean-blue backdrop (web waterBg gradient reads ~this at frame centre).
            // The old near-black (0.02,0.08,0.15) sank the whole shot into darkness (QC r2).
            // Still set: it is the fallback if the gradient backdrop cannot be built.
            cam.backgroundColor = new Color(0.30f, 0.52f, 0.66f, 1f);

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
        // 🔴 HISTORY, NOT INSTRUCTIONS — the first bullet below was undone twice over and the
        // reflection cube it justifies is now BLACK at intensity 0. WO-E5m already writes the
        // wreck's metallicFactor to 0 (SceneBuilder.TameMetal → GlbShading.MappedMetalFactor), so
        // the hull has full diffuse and the premise "a metal surface has ~zero diffuse albedo"
        // no longer describes anything in this app; and WO-L measured what the cube was actually
        // doing to everything else. See the reflection block further down before touching it.
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
        private void SetupLighting()
        {
            RenderSettings.ambientMode         = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor     = new Color(0.348f, 0.478f, 0.574f); // r4 −13% (sand still cream vs web); boat lit by reflection cube, ~unaffected
            // 🔴 The two lower bands lifted (equator 0.278→0.315, ground 0.20→0.235) after the
            // "sharks are black on Posidon" report. These are the only light a surface gets when it
            // is not facing the sun, and the sand — which faces straight up into the SKY band and
            // straight into the sun — was reading ten times brighter than a shark's flank in the
            // same frame. The deep-water half of that fix is <see cref="UnderwaterShading"/>; this
            // is the part that also helps the map view, where the depth floor deliberately does
            // nothing. Kept monotonic (sky > equator > ground) so objects still shade top to bottom
            // instead of going flat.
            RenderSettings.ambientEquatorColor = new Color(0.315f, 0.430f, 0.500f);
            RenderSettings.ambientGroundColor  = new Color(0.235f, 0.325f, 0.375f); // 0x123040 lifted (no black undersides)
            // WO-XR-04.3: the web's underwater fog — THREE.Fog(0x123a55, near, far) with
            // near = max(500, reach·1.1) and far = max(9000, maxD·3.4). At orbit distance this
            // is only a 3-7% wash (Fable's survey), and that is the point: it must colour the
            // far rim of a big map, not haze over the wreck. The scene's RenderSettings also
            // ship with fog enabled so the linear-fog shader variants survive build stripping.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            // 🔴 NOT the web's #123a55 any more. That colour is about a third as bright as the
            // backdrop gradient drawn behind everything, so distant geometry faded toward navy over
            // a bright cyan background and read as a black silhouette. The fog now reads off the
            // same ramp as the backdrop (WaterFog) — this is the surface end of it, and
            // DepthAtmosphere moves it down the ramp as the camera descends.
            {
                SeabedGeom.Rgb f = WaterFog.ColorAt(0f);
                RenderSettings.fogColor = new Color(f.R, f.G, f.B);
            }
            RenderSettings.fogStartDistance = 500f;
            RenderSettings.fogEndDistance = 9000f;

            // ── WO-L (4 ส.ค.): NO ENVIRONMENT SPECULAR. It is off, and it must stay off. ────────
            //
            // The web has no envmap at all. builder.html hands three.js three lights and nothing
            // else, so every specular highlight on that page is a highlight from a light. This
            // scene used to hand every shader a 4×4 uniform blue-white cubemap at
            // reflectionIntensity 1 — the single largest departure from the page we are trying to
            // match, and the one the "the animals look flat and washed out" report is about.
            //
            // 🔴 IT IS AN ADDITIVE WASH, and the arithmetic says how much. A uniform cube carries
            // no direction, so what it delivers is the same number on every texel of a smooth
            // surface. Built-in RP, gamma colour space, a marine dielectric (metallic ≈ 0,
            // smoothness ≈ 0.71):
            //
            //     surfaceReduction   1 − 0.28·roughness·perceptualRoughness  = 0.993
            //     cube luminance     (0.60 + 0.72 + 0.82) / 3                = 0.713
            //     FresnelLerp mean   0.04 + (0.75 − 0.04)·⟨(1−N·V)⁵⟩         = 0.074
            //       (⟨(1−N·V)⁵⟩ = 2·B(2,6) = 0.0476, the area mean over a sphere silhouette)
            //     ───────────────────────────────────────────────────────────────────────────
            //     0.993 × 0.713 × 0.074 = 0.052   →   +13.3 of 255, on every lit pixel
            //
            // Measured against WO-K's offline render of the whale shark's own shipped GLB at this
            // exact camera, the Unity frame was +21.9 levels brighter (115.06 vs 93.11) with the
            // pattern amplitude INTACT (hpRms 26.10 vs 27.21, ratio 0.96). The cube is 13.3 of
            // that 21.9. Simulating its removal on that frame moves contrast retention 0.835 →
            // 0.966 and hpRms retention 0.959 → 0.984. <see cref="DiveMap.Core.QcFidelity"/> is
            // that measurement, wired into the model QC pass so the next build reports it.
            //
            // 🔴 THE REASON IT WAS ADDED IS GONE — check this before ever putting it back. The
            // cube was hired to stop the metallic wreck reflecting black (the long remark above),
            // and since WO-E5m every material that ships a metallic-roughness map has had its
            // metallicFactor written to 0 by SceneBuilder.TameMetal → GlbShading.MappedMetalFactor.
            // The wreck is one of those. After TameMetal the whole app contains exactly one
            // material above 0.06 metal: the whitetip shark at 0.224 (GlbShading's measured table),
            // and it keeps 74.5% of its diffuse albedo plus both lights' direct specular — which is
            // precisely, and only, what the web gives it.
            //
            // ⚠️ 3 ส.ค. left a note saying "ห้ามลด intensity เดี่ยวๆ วัดแล้วแย่ลง". That verdict
            // came from darkOfSubject on a studio rig, which measures how much of a model is dark
            // and therefore scores any wash as an improvement. It cannot see this bug. Superseded.
            //
            // Custom mode with a black cube rather than no cube: unity_SpecCube0 stays bound and
            // defined for every shader variant, and the intensity is a second, independent zero.
            RenderSettings.defaultReflectionMode  = UnityEngine.Rendering.DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = AmbientReflectionCube(Color.black);
            RenderSettings.reflectionIntensity     = 0f;

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
            // r2 1.05 → 1.0 → 0.82. Under 50 m of water the sun is not a key light any more, it is
            // a direction; the light that reaches you is overwhelmingly scattered and comes from
            // everywhere. The rig here was an above-water rig — one hard directional plus a thin
            // ambient — and that is precisely the combination that gives bright up-facing sand, a
            // hard terminator across a statue, and no light at all on a shark's flank. Trading a
            // fifth of the sun for the ambient lift above keeps the sand where it was (it was
            // clipping to white in wide shots anyway) and gives every vertical surface something.
            sun.intensity = 0.82f;
            sun.transform.rotation = Quaternion.Euler(52f, -35f, 0f); // high, angled — web pos (60,160,70)
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.5f;                 // was 1.0 — soften the wreck's self-shadow band
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
            fill.intensity = 0.65f;                          // r2 0.55 → a touch more lift on the shadowed hull
            fill.transform.rotation = Quaternion.Euler(-14f, 145f, 0f); // opposite/low — web pos (-90,40,-70)
            // ⚠️ On the phone this light is NOT per-pixel (see the note on the sun): with one slot
            // and the sun holding it, the fill is folded into the ambient probe instead. Tuning its
            // intensity therefore does much less on a device than it appears to do in a CI
            // screenshot — the job it was hired for, opening up shadowed undersides, is carried by
            // ambientGroundColor above and by the underwater floor. Left in because in SH it still
            // costs nothing and it does hold the Windows/desktop build together.
        }

        // A tiny uniform-colour cubemap used as the scene's custom reflection. Cached so
        // Retry/rebuild doesn't leak a new cubemap.
        //
        // 🔴 Called with Color.black, on purpose — see the reflection block in SetupLighting.
        // The cache is keyed on nothing but existence, so this has exactly one caller by
        // construction: a second caller with a different colour would silently get the first
        // one's cubemap. If a probe ever needs its own, give it its own field.
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
            // The loading screen (the web's #load cover) goes up here — first open, map switch
            // and Retry all come through Boot(), so this one line covers all three. It is bound
            // to the builder's own counters, and it is never created in a -qcshot run.
            Ui.LoadOverlay.Show(_builder != null ? _builder.Progress : null);

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
            // Built and framed = playable. Fill the bar and fade the cover off the map, exactly
            // where the web drops #load (builder.html:4431 — one line, right after its loop starts).
            Ui.LoadOverlay.Hide();

            string title = string.IsNullOrEmpty(result.MapName) ? mapName : result.MapName;
            SetLoadSummary(title, result.Loaded, result.Failed);

            // The view RANGE: how far back the player may go, and how far the camera and the fog
            // have to reach for the map to still be there when they get there. Independent of the
            // framing below by construction — see OrbitCamera.FrameDistanceCap.
            ApplyViewRange(result);

            if (_orbit != null)
                _orbit.FrameBox(result.FrameCenter, result.FrameSizeX, result.FrameSizeY, result.FrameSizeZ, result.FrameMinY);

            // ── Tour (P1.1) ─────────────────────────────────────────────────────────
            // Hand the drone its world: what to collide with, where the surface is, how the
            // seabed is stretched, and where "home" is for the exit re-frame.
            TourController.Configure(result);
            ArSession.Configure(result);   // F1 — AR needs the footprint to place the viewer
            EnvMode.Reset();   // new scene, new lights/water to capture
            DepthAtmosphere.Configure(result.WaterLevel);   // shallow bright, deep dark and blue
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
            // ── SchoolClip (CI): -schoolclip <dir> → ถ่ายคลิปฝูง 90 เฟรมต่อเนื่อง ──
            // เกิดจากมหากาพย์ "ฝูงส่ายหัว" 8 ส.ค.: ภาพนิ่ง QC พิสูจน์การเคลื่อนไหวไม่ได้
            // และ orchestrator เคยใช้เครื่อง user เป็นเครื่องทดสอบ 9 รอบ — คลิปนี้ทำให้เห็น
            // การเคลื่อนไหวจริงของ sim ก่อนส่งงานทุกครั้ง (Time.captureFramerate ตรึง dt
            // ให้เวลาเกมเดิน 1/30 วิ/เฟรมแม้ llvmpipe เรนเดอร์ช้า = คลิปเล่นความเร็วจริง)
            string clipDir = GetArg("-schoolclip");
            if (!string.IsNullOrEmpty(clipDir))
            {
                var marineC = _mapRoot != null ? _mapRoot.GetComponent<FishSchoolSystem>() : null;
                StartCoroutine(SchoolClip(clipDir, marineC));
            }

            string qcPath = GetArg("-qcshot");
            if (!string.IsNullOrEmpty(qcPath) && string.IsNullOrEmpty(clipDir))
            {
                // FishSchoolSystem sits on the Map root (added by SceneBuilder when there are
                // schools); the close-up angle-2 uses it to find the scad shoal nearest the wreck.
                var marine = _mapRoot != null ? _mapRoot.GetComponent<FishSchoolSystem>() : null;
                StartCoroutine(QcShot(qcPath, marine, result.FrameCenter));
            }
        }

        private static string GetArg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        private IEnumerator SchoolClip(string dir, FishSchoolSystem marine)
        {
            System.IO.Directory.CreateDirectory(dir);
            yield return new WaitForSeconds(6f);   // ฝูง morph เข้าที่ + template GLB ลง

            Camera cam = Camera.main;
            var orbit = cam != null ? cam.GetComponent<OrbitCamera>() : null;
            if (orbit != null) orbit.enabled = false;

            Bounds target = default;
            bool found = false;
            if (marine != null)
                for (int i = 0; i < marine.SchoolCount && !found; i++)
                    if (marine.TryGetSchoolBounds(i, out string sp, out Bounds b)
                        && sp != null && sp.Contains("barracuda"))
                    { target = b; found = true; }
            if (!found && marine != null && marine.SchoolCount > 0)
                found = marine.TryGetSchoolBounds(0, out _, out target);
            Debug.Log($"[Clip] school found={found} centre={target.center} size={target.size}");
            if (cam != null && found)
            {
                float d = target.size.magnitude * 0.9f + 10f;
                cam.transform.position = target.center + new Vector3(0.4f, 0.25f, 0.9f).normalized * d;
                cam.transform.LookAt(target.center);
            }

            Time.captureFramerate = 30;   // เวลาเกมเดินคงที่ 1/30 ต่อเฟรมที่แคป
            for (int f = 0; f < 90; f++)
            {
                // เล็งกลางฝูงใหม่ทุก 30 เฟรม (ฝูงว่ายออกนอกเฟรมได้)
                if (cam != null && marine != null && f % 30 == 0 && f > 0)
                    for (int i = 0; i < marine.SchoolCount; i++)
                        if (marine.TryGetSchoolBounds(i, out string sp2, out Bounds b2)
                            && sp2 != null && sp2.Contains("barracuda"))
                        { cam.transform.LookAt(b2.center); break; }
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"clip_{f:000}.png"));
                yield return new WaitForEndOfFrame();
            }
            Time.captureFramerate = 0;
            Debug.Log("[Clip] done 90 frames");
            yield return null;
            Application.Quit();
        }

        private IEnumerator QcShot(string path, FishSchoolSystem marine, Vector3 boatCenter)
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

            // ── ใบที่ 9-15: แมพจริงทั้ง 7 จากมุมสายตานักดำน้ำ ─────────────────────────
            // Everything above photographs either nine models alone in a studio or ONE map — HTMS
            // Chang — from an orbit pose. The user's answer to that was "ผมถามคุณ QC ยังไงครับ
            // คุณไม่ได้ถ่ายรูปแคปเจอร์หน้าจอมาดูเองด้วยหรอ", and they were right: Atlantis,
            // Posidon and Hanuman — the three maps every complaint has been about — had never
            // been in a CI frame, and neither had the diver's-eye pose the player spends the
            // whole game in.
            //
            // 🔴 CI-ONLY, AND THAT IS STRUCTURAL, NOT A FLAG. This whole method only runs when
            // -qcshot was passed on the command line (see OnBuilt), a switch nothing but the
            // workflow ever passes; the method ends in Application.Quit(0). A player build
            // therefore never reaches this line, which matters because the pass DESTROYS the map
            // on screen and rebuilds seven others in its place.
            //
            // LAST, for the same reason: nothing runs after it but the quit, so there is nothing
            // left for the final map to break.
            yield return QcMapShot.Run(dir, _builder, Manifest, _mapRoot);
            _mapRoot = null;   // QcMapShot destroyed it; do not leave a dangling reference behind.

            // ── WO-F3: do the animated models actually ANIMATE? ────────────────────
            // Every pass above measures pixels, and no number of pixels can answer that: a rigged
            // animal frozen on frame 0 photographs exactly like a rigged animal mid-stroke. This
            // one takes no picture — it loads three rigged GLBs, plays them, and measures how far
            // the skeleton moved. Last, so that a failure or a timeout in it cannot cost any of
            // the passes above their evidence.
            yield return QcAnimShot.Run(boatCenter);

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

        /// <summary>
        /// The web's <c>updateViewRange()</c> (builder.html:709-722), applied to the map that has
        /// just finished building: zoom-out ceiling, camera far and near planes, and both ends of
        /// the fog — all four scaled from the map's own content radius.
        ///
        /// 🔴 2026-08-06, build 280: *"zoom out ได้มากกว่านี้ตามขนาดแมพ — Atlantis อยากเห็นเต็ม
        /// แมพ"*. Until now every one of those four was a literal that never looked at the map:
        /// ceiling 950 (OrbitCamera's field default, which is the web's post-AR BUG value — see the
        /// comment there), far 9,000 and fog 500…9,000 (set once in SetupLighting). A big map was
        /// therefore capped at a distance chosen for a small one.
        ///
        /// 🔎 Why this cannot change the look of an ordinary map, and it is arithmetic rather than
        /// a promise: feed <see cref="CameraRange"/> the web's bare sand radius, 340 u, and it
        /// returns fog 500…9,000 — the exact pair being replaced — and a far plane of 8,040 against
        /// the old 9,000, which is 950 u of zoom plus 340 u of map with 6.7 km to spare. The
        /// numbers only move once the map is big enough that they had to.
        ///
        /// The radius is the FLAT footprint, matching the web's <c>reach</c> — the sand disc, or
        /// the scenery's horizontal extent where a map's structures overhang it. Deliberately not
        /// <c>result.Radius</c>, which is a 3D half-diagonal including the water column and reads
        /// ≈504 on a 340 u map; see <c>SceneBuilder.BuildResult.SeabedRadius</c>.
        /// </summary>
        private void ApplyViewRange(SceneBuilder.BuildResult result)
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            // The web's `foggy` is `!!scene.fog`, and its daylight mode sets scene.fog = null
            // (builder.html:682) — where you can see much further, hence the higher floor.
            bool foggy = RenderSettings.fog;

            float reach = Mathf.Max(result.SeabedRadius,
                                    Mathf.Max(result.FrameSizeX, result.FrameSizeZ) * 0.5f);

            float aspect = cam.aspect > 0.001f ? cam.aspect : (float)Screen.width / Mathf.Max(1, Screen.height);
            CameraRange.ViewRange v = CameraRange.For(reach, foggy, cam.fieldOfView, aspect);

            if (_orbit != null) _orbit.maxDistance = (float)v.MaxDistance;

            cam.farClipPlane = (float)v.Far;
            cam.nearClipPlane = (float)v.Near;

            if (RenderSettings.fog)
            {
                RenderSettings.fogStartDistance = (float)v.FogNear;
                RenderSettings.fogEndDistance = (float)v.FogFar;
            }

            // One line, because "I still can't zoom out" and "I zoomed out and the map vanished"
            // are different bugs with the same screenshot, and only the ceiling tells them apart.
            Debug.Log($"[View] reach={reach:F0} (sand={result.SeabedRadius:F0}) " +
                      $"fov={cam.fieldOfView:F0} aspect={aspect:F2} " +
                      $"foggy={foggy} maxDist={v.MaxDistance:F0} " +
                      $"(web={Mathf.Max(foggy ? 2600f : 3600f, reach * (float)CameraRange.MaxDistK):F0} " +
                      $"fit={CameraRange.FitDistance(reach, cam.fieldOfView, aspect):F0}) " +
                      $"far={v.Far:F0} near={v.Near:F2} fog={v.FogNear:F0}..{v.FogFar:F0}");
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
            // Whatever failed, the cover must come off NOW: the retry button is under it, and a
            // loading screen over a dead load is the black-screen bug users report as "แอปค้าง".
            Ui.LoadOverlay.Cancel();
            if (_errorText != null) _errorText.text = s;
            if (_errorPanel != null) _errorPanel.SetActive(true);
        }

        private void HideError() { if (_errorPanel != null) _errorPanel.SetActive(false); }
    }
}
