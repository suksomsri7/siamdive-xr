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
        // Fix is decoupled by shading path (no GLB/SceneBuilder edits, no shader-property
        // guesswork):
        //   • WRECK (specular)  ← a bright custom reflection cubemap. Metals reflect a lit
        //     underwater environment tinted by their own base colour (F0 = olive) → the
        //     hull reads bright green-olive all round. Does NOT touch diffuse sand.
        //   • SAND  (diffuse)   ← Trilight ambient, pulled down a touch from r2 so the
        //     cream sand settles to a natural tone. Barely moves the metallic wreck.
        //   • Sun soft-shadow strength eased so the camera-facing hull isn't a hard black
        //     self-shadow band (ambient + reflection now fill it).
        private void SetupLighting()
        {
            RenderSettings.ambientMode         = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor     = new Color(0.348f, 0.478f, 0.574f); // r4 −13% (sand still cream vs web); boat lit by reflection cube, ~unaffected
            RenderSettings.ambientEquatorColor = new Color(0.278f, 0.392f, 0.461f); // r4 −13% down (sand faces up = sky+equator ambient)
            RenderSettings.ambientGroundColor  = new Color(0.20f, 0.28f, 0.31f); // 0x123040 lifted (no black undersides)
            // WO-XR-04.3: the web's underwater fog — THREE.Fog(0x123a55, near, far) with
            // near = max(500, reach·1.1) and far = max(9000, maxD·3.4). At orbit distance this
            // is only a 3-7% wash (Fable's survey), and that is the point: it must colour the
            // far rim of a big map, not haze over the wreck. The scene's RenderSettings also
            // ship with fog enabled so the linear-fog shader variants survive build stripping.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.071f, 0.227f, 0.333f); // 0x123a55
            RenderSettings.fogStartDistance = 500f;
            RenderSettings.fogEndDistance = 9000f;

            // Custom reflection so the metallic wreck reflects a lit underwater environment
            // instead of black. Uniform bright blue-white cubemap; a metal surface's spec
            // colour is its own base colour (olive), so the hull reads bright green-olive.
            // Diffuse sand is unaffected — reflection only feeds specular. (built-in RP:
            // feeds unity_SpecCube0 globally via DefaultReflectionMode.Custom.)
            RenderSettings.defaultReflectionMode  = UnityEngine.Rendering.DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = AmbientReflectionCube(new Color(0.60f, 0.72f, 0.82f));
            RenderSettings.reflectionIntensity     = 1f;

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
            sun.intensity = 1.0f;                      // r2 1.05 → eased (sand highlight)
            sun.transform.rotation = Quaternion.Euler(52f, -35f, 0f); // high, angled — web pos (60,160,70)
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.5f;                 // was 1.0 — soften the wreck's self-shadow band
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
                ShowError(fetchErr ?? "โหลดแมพไม่สำเร็จ");
                yield break;
            }

            // E5 — put the player's own purchases back into the map before it is built, so a
            // bought animal goes through exactly the same pipeline as everything else rather
            // than down a second spawn path that would drift out of step with this one.
            int restocked = ShopStock.InjectFromStore(scene, _shortId);
            if (restocked > 0) Debug.Log($"[Shop] restored {restocked} purchased item(s) for {_shortId}");

            // Keep the scene JSON around: it is what a save writes back, and the rev is what
            // stops that save from clobbering an edit made on another device.
            CurrentScene = scene;
            CurrentRev = scene.Root["rev"] != null ? (int)scene.Root["rev"] : -1;
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
            EnvMode.Reset();   // new scene, new lights/water to capture
            Ui.PerfHud.Apply();   // A7 — rebuild the readout if the player left it on

            // D9/E8 — a diver who left through a warp gate lands IN the destination, at a random
            // point, rather than being handed the map screen. Flag cleared on use, so cancelling a
            // warp cannot hijack the next map the player opens.
            if (TourController.ArrivingByWarp)
            {
                TourController.ArrivingByWarp = false;
                Debug.Log("[Tour] warp arrival → entering the tour at a random spawn");
                TourController.Start(randomStart: true);
            }

            // ── Sun shafts (WO-XR-04.3) ─────────────────────────────────────────────
            // Scattered around the content, from the water surface down to just under the
            // seabed, all parallel to the sun set in SetupLighting.
            if (result.Root != null && result.WaterLevel > result.FrameMinY + 5f)
            {
                float spread = Mathf.Clamp(result.Radius * 0.45f, 60f, 220f);
                float length = Mathf.Clamp(result.WaterLevel - result.FrameMinY + 20f, 60f, 400f);
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

            Application.Quit(0);
        }

        private void Retry()
        {
            StopAllCoroutines();
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
            if (_rebuild != null) StopCoroutine(_rebuild);
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
            EnvMode.Reset();
            Ui.PerfHud.Apply();

            if (result.Root != null && result.WaterLevel > result.FrameMinY + 5f)
            {
                float spread = Mathf.Clamp(result.Radius * 0.45f, 60f, 220f);
                float length = Mathf.Clamp(result.WaterLevel - result.FrameMinY + 20f, 60f, 400f);
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
            SetStatus($"{_summaryTitle}  ·  {UiStrings.Tr("โหลดแล้ว")} {_summaryLoaded} · " +
                      $"{UiStrings.Tr("แทนที่")} {_summaryFailed}");
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
