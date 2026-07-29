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
            RenderSettings.fog = false; // web daylight = no fog; underwater fog is 400-9000u (negligible near)

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
            SetStatus("กำลังเชื่อมต่อ…");

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

            // Scene from production API (fatal on failure → retry).
            SceneData scene = null;
            string fetchErr = null;
            yield return MapApiClient.Fetch(_shortId, s => scene = s, e => fetchErr = e);

            if (fetchErr != null || scene == null)
            {
                ShowError(fetchErr ?? "โหลดแมพไม่สำเร็จ");
                yield break;
            }

            string mapName = string.IsNullOrEmpty(scene.Name) ? _shortId : scene.Name;
            SetStatus(mapName + " · กำลังวางวัตถุ…");

            SceneBuilder.BuildResult result = default;
            bool done = false;
            yield return _builder.BuildRoutine(scene, manifest, r => { result = r; done = true; });

            if (!done)
            {
                ShowError("สร้างแมพไม่สำเร็จ");
                yield break;
            }

            _mapRoot = result.Root;
            HideCenter();
            HideError();

            string title = string.IsNullOrEmpty(result.MapName) ? mapName : result.MapName;
            SetStatus($"{title}  ·  โหลดแล้ว {result.Loaded} · แทนที่ {result.Failed}");

            if (_orbit != null)
                _orbit.FrameBox(result.FrameCenter, result.FrameSizeX, result.FrameSizeY, result.FrameSizeZ, result.FrameMinY);

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

            // Top-left status line (small).
            _statusText = MakeText(canvas.transform, "Status", font, 26, TextAnchor.UpperLeft);
            var sRt = _statusText.rectTransform;
            sRt.anchorMin = new Vector2(0f, 1f);
            sRt.anchorMax = new Vector2(1f, 1f);
            sRt.pivot = new Vector2(0f, 1f);
            sRt.anchoredPosition = new Vector2(24f, -20f);
            sRt.sizeDelta = new Vector2(-48f, 60f);
            _statusText.text = "";

            // Centre loading text.
            _centerText = MakeText(canvas.transform, "Center", font, 44, TextAnchor.MiddleCenter);
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
            eRt.sizeDelta = new Vector2(900f, 400f);

            _errorText = MakeText(_errorPanel.transform, "ErrorText", font, 38, TextAnchor.MiddleCenter);
            var etRt = _errorText.rectTransform;
            etRt.anchorMin = new Vector2(0f, 0.4f);
            etRt.anchorMax = new Vector2(1f, 1f);
            etRt.offsetMin = Vector2.zero;
            etRt.offsetMax = Vector2.zero;
            _errorText.color = new Color(1f, 0.7f, 0.7f, 1f);

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
            rt.anchoredPosition = new Vector2(0f, 20f);
            rt.sizeDelta = new Vector2(300f, 90f);

            var img = go.AddComponent<Image>();
            img.color = new Color(0.10f, 0.42f, 0.55f, 1f);

            var btn = go.AddComponent<Button>();
            btn.onClick.AddListener(onClick);

            var txtGo = new GameObject("Text");
            txtGo.transform.SetParent(go.transform, false);
            var txt = txtGo.AddComponent<Text>();
            txt.font = font;
            txt.fontSize = 34;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;
            txt.text = label;
            var trt = txt.rectTransform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
        }

        private void SetStatus(string s) { if (_statusText != null) _statusText.text = s; }
        private void ShowCenter(string s) { if (_centerText != null) { _centerText.text = s; _centerText.gameObject.SetActive(true); } }
        private void HideCenter() { if (_centerText != null) _centerText.gameObject.SetActive(false); }

        private void ShowError(string s)
        {
            HideCenter();
            if (_errorText != null) _errorText.text = s;
            if (_errorPanel != null) _errorPanel.SetActive(true);
        }

        private void HideError() { if (_errorPanel != null) _errorPanel.SetActive(false); }
    }
}
