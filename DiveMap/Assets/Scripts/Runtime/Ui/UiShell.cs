using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using DiveMap.Core;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// The app's UI shell (WO-XR-05.1): hamburger button, slide-in menu, screen stack,
    /// safe-area handling and the camera-input gate.
    ///
    /// It bootstraps itself with <see cref="RuntimeInitializeOnLoadMethod"/> so neither
    /// Main.unity nor AppBoot has to change — the scene file and the boot sequence stay
    /// exactly as WO-XR-01/03 left them.
    ///
    /// Two hard rules baked in here:
    ///  • In <c>-qcshot</c> mode the shell is never created at all, so the marine/wreck
    ///    QC screenshots the reviewer compares against the web stay pixel-comparable.
    ///  • The 3D orbit camera is disabled whenever a screen is open, and also while a
    ///    pointer/finger is over a UI element — <see cref="OrbitCamera"/> reads raw
    ///    <c>Input</c> every frame and cannot be touched by this work order.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiShell : MonoBehaviour
    {
        public static UiShell Instance { get; private set; }

        private const float MenuWidth = 620f;
        private const float SlideSeconds = 0.18f;

        private Canvas _canvas;
        private RectTransform _safe;
        private UiNav _nav;
        private ThumbnailCache _thumbs;

        private GameObject _hamburger;
        private GameObject _menuLayer;
        private RectTransform _menuPanel;
        private GameObject _mapsLayer;
        private MapListScreen _mapList;
        private GameObject _soonLayer;

        private OrbitCamera _orbit;
        private float _nextOrbitLookup;
        private bool _orbitWanted = true;

        private Rect _appliedSafeArea;
        private Vector2Int _appliedScreen;

        // ── bootstrap ────────────────────────────────────────────────────────────

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;

            // QC screenshot run (marine/wreck framing): the shell must not exist.
            if (!string.IsNullOrEmpty(GetArg("-qcshot")))
            {
                Debug.Log("[UI] -qcshot present → UI shell disabled for this run");
                return;
            }

            var go = new GameObject("UiShell");
            go.AddComponent<UiShell>();
        }

        private static string GetArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        // ── lifecycle ────────────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            EnsureEventSystem();
            BuildCanvas();
            BuildHamburger();
            BuildMenu();
            BuildMapsScreen();
            BuildSoonScreen();

            _nav = gameObject.AddComponent<UiNav>();
            _nav.StackChanged += OnStackChanged;

            Debug.Log($"[UI] shell ready (lang={UiStrings.Lang}, strings={UiStrings.Count})");
        }

        private IEnumerator Start()
        {
            string prefix = GetArg("-qcui");
            if (string.IsNullOrEmpty(prefix)) yield break;
            yield return QcUi(prefix);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ── canvas / safe area ───────────────────────────────────────────────────

        private static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        private void BuildCanvas()
        {
            var canvasGo = new GameObject("UiCanvas");
            canvasGo.transform.SetParent(transform, false);

            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 10; // above AppBoot's BootCanvas (0)

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();

            _safe = UiKit.MakeNode(canvasGo.transform, "SafeArea");
            ApplySafeArea(true);

            _thumbs = gameObject.AddComponent<ThumbnailCache>();
        }

        private void ApplySafeArea(bool force)
        {
            if (_safe == null) return;

            Rect sa = Screen.safeArea;
            var screen = new Vector2Int(Screen.width, Screen.height);
            if (screen.x <= 0 || screen.y <= 0) return;
            if (!force && sa == _appliedSafeArea && screen == _appliedScreen) return;

            _appliedSafeArea = sa;
            _appliedScreen = screen;

            _safe.anchorMin = new Vector2(sa.xMin / screen.x, sa.yMin / screen.y);
            _safe.anchorMax = new Vector2(sa.xMax / screen.x, sa.yMax / screen.y);
            _safe.offsetMin = Vector2.zero;
            _safe.offsetMax = Vector2.zero;
        }

        // ── widgets ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Top-right ☰ button. The three bars are Images, not a "☰" glyph: NotoSansThai
        /// has no guaranteed coverage of U+2630 and a missing glyph renders as a blank
        /// box in the headless player.
        /// </summary>
        private void BuildHamburger()
        {
            Button btn = UiKit.MakeButton(_safe, "MenuButton", null, 0, UiKit.PanelBg,
                                          UiKit.TextMain, OpenMenu);
            _hamburger = btn.gameObject;
            UiKit.Anchor(btn.GetComponent<RectTransform>(), new Vector2(1f, 1f),
                         new Vector2(104f, 104f), new Vector2(-24f, -24f));

            for (int i = 0; i < 3; i++)
            {
                Image bar = UiKit.MakePanel(btn.transform, "Bar" + i, UiKit.Teal);
                bar.raycastTarget = false;
                var rt = bar.rectTransform;
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(52f, 7f);
                rt.anchoredPosition = new Vector2(0f, 17f - i * 17f);
            }
        }

        private void BuildMenu()
        {
            _menuLayer = UiKit.MakeNode(_safe, "MenuLayer").gameObject;

            // Scrim: closes the menu and swallows every tap behind the panel.
            Button scrim = UiKit.MakeButton(_menuLayer.transform, "Scrim", null, 0, UiKit.Scrim,
                                            UiKit.TextMain, CloseTop);
            UiKit.Stretch(scrim.GetComponent<RectTransform>());

            Image panel = UiKit.MakePanel(_menuLayer.transform, "MenuPanel", UiKit.PanelBg);
            _menuPanel = panel.rectTransform;
            _menuPanel.anchorMin = new Vector2(1f, 0f);
            _menuPanel.anchorMax = new Vector2(1f, 1f);
            _menuPanel.pivot = new Vector2(1f, 0.5f);
            _menuPanel.sizeDelta = new Vector2(MenuWidth, 0f);
            _menuPanel.anchoredPosition = Vector2.zero;

            Text title = UiKit.MakeText(_menuPanel, "Title", UiStrings.Tr("เมนู"), 46,
                                        TextAnchor.MiddleLeft, UiKit.Teal);
            UiKit.TopRow(title.rectTransform, 34f, 80f, 36f, 36f);

            MenuItem(0, UiStrings.Tr("รายการแมพ"), OpenMapList);
            MenuItem(1, UiStrings.Tr("ตั้งค่า"), OpenSettingsPlaceholder);

            Button close = UiKit.MakeButton(_menuPanel, "MenuClose", UiStrings.Tr("ปิด"), 32,
                                            UiKit.TealDim, UiKit.TextMain, CloseTop);
            UiKit.Anchor(close.GetComponent<RectTransform>(), new Vector2(0.5f, 0f),
                         new Vector2(240f, 84f), new Vector2(0f, 48f));

            _menuLayer.SetActive(false);
        }

        private void MenuItem(int index, string label, UnityEngine.Events.UnityAction action)
        {
            Button b = UiKit.MakeButton(_menuPanel, "Item" + index, label, 38, UiKit.CardBg,
                                        UiKit.TextMain, action);
            var rt = b.GetComponent<RectTransform>();
            UiKit.TopRow(rt, 150f + index * 124f, 104f, 36f, 36f);
            Text t = b.GetComponentInChildren<Text>();
            if (t != null) t.alignment = TextAnchor.MiddleLeft;
        }

        private void BuildMapsScreen()
        {
            _mapsLayer = UiKit.MakeNode(_safe, "MapsLayer").gameObject;
            _mapList = _mapsLayer.AddComponent<MapListScreen>();
            _mapList.Build(_thumbs);
            _mapList.CloseRequested += CloseTop;
            _mapList.MapSelected += OnMapSelected;
            _mapsLayer.SetActive(false);
        }

        /// <summary>Placeholder until WO-XR-05.4 builds the real settings screen.</summary>
        private void BuildSoonScreen()
        {
            _soonLayer = UiKit.MakeNode(_safe, "SoonLayer").gameObject;

            Button scrim = UiKit.MakeButton(_soonLayer.transform, "Scrim", null, 0, UiKit.Scrim,
                                            UiKit.TextMain, CloseTop);
            UiKit.Stretch(scrim.GetComponent<RectTransform>());

            Image card = UiKit.MakePanel(_soonLayer.transform, "Card", UiKit.PanelBg);
            var crt = card.rectTransform;
            crt.anchorMin = new Vector2(0.5f, 0.5f);
            crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = new Vector2(820f, 420f);
            crt.anchoredPosition = Vector2.zero;

            Text title = UiKit.MakeText(crt, "Title", UiStrings.Tr("ตั้งค่า"), 46,
                                        TextAnchor.MiddleCenter, UiKit.Teal);
            UiKit.TopRow(title.rectTransform, 48f, 80f, 40f, 40f);

            Text body = UiKit.MakeText(crt, "Body", UiStrings.Tr("เร็วๆ นี้"), 36,
                                       TextAnchor.MiddleCenter, UiKit.TextDim);
            UiKit.TopRow(body.rectTransform, 160f, 80f, 40f, 40f);

            Button close = UiKit.MakeButton(crt, "Close", UiStrings.Tr("ปิด"), 32,
                                            UiKit.TealDim, UiKit.TextMain, CloseTop);
            UiKit.Anchor(close.GetComponent<RectTransform>(), new Vector2(0.5f, 0f),
                         new Vector2(240f, 84f), new Vector2(0f, 40f));

            _soonLayer.SetActive(false);
        }

        // ── navigation ───────────────────────────────────────────────────────────

        public void OpenMenu()
        {
            if (_nav == null || _menuLayer == null) return;
            if (_nav.IsOpen(_menuLayer)) return;
            _nav.Push("menu", _menuLayer);
            StartCoroutine(SlideIn());
        }

        public void OpenMapList()
        {
            if (_nav == null || _mapsLayer == null) return;
            _nav.Push("maps", _mapsLayer);
            if (_mapList != null) _mapList.EnsureLoaded();
        }

        public void OpenSettingsPlaceholder()
        {
            if (_nav == null || _soonLayer == null) return;
            _nav.Push("settings", _soonLayer);
        }

        public void CloseTop()
        {
            if (_nav != null) _nav.Pop();
        }

        public void CloseAll()
        {
            if (_nav != null) _nav.PopAll();
        }

        private IEnumerator SlideIn()
        {
            if (_menuPanel == null) yield break;
            _menuPanel.anchoredPosition = new Vector2(MenuWidth, 0f); // start off-screen right
            float t = 0f;
            while (t < SlideSeconds)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / SlideSeconds);
                k = 1f - (1f - k) * (1f - k); // ease-out
                _menuPanel.anchoredPosition = new Vector2(Mathf.Lerp(MenuWidth, 0f, k), 0f);
                yield return null;
            }
            _menuPanel.anchoredPosition = Vector2.zero;
        }

        private void OnMapSelected(string shortId)
        {
            if (string.IsNullOrEmpty(shortId)) return;
            CloseAll();

            AppBoot boot = UnityEngine.Object.FindFirstObjectByType<AppBoot>();
            if (boot == null)
            {
                Debug.LogWarning("[UI] no AppBoot in scene — cannot load " + shortId);
                return;
            }
            Debug.Log("[UI] loading map " + shortId);
            boot.LoadMap(shortId);
        }

        // ── camera input gate ────────────────────────────────────────────────────

        private void OnStackChanged(int depth)
        {
            // Hide the ☰ affordance while a screen owns the display.
            if (_hamburger != null) _hamburger.SetActive(depth == 0);
            SetOrbitEnabled(depth == 0);
        }

        /// <summary>The shell's own canvas (sortingOrder 10, above AppBoot's BootCanvas).</summary>
        public Canvas ShellCanvas => _canvas;

        private void Update()
        {
            ApplySafeArea(false);

            bool allow = _nav == null || _nav.Count == 0;
            if (allow && PointerOverUi()) allow = false;
            SetOrbitEnabled(allow);
        }

        private void SetOrbitEnabled(bool on)
        {
            if (_orbit == null && Time.unscaledTime >= _nextOrbitLookup)
            {
                _nextOrbitLookup = Time.unscaledTime + 0.5f;
                _orbit = UnityEngine.Object.FindFirstObjectByType<OrbitCamera>();
            }
            if (_orbit == null) { _orbitWanted = on; return; }
            if (_orbitWanted == on && _orbit.enabled == on) return;
            _orbitWanted = on;
            _orbit.enabled = on;
        }

        /// <summary>
        /// True when a pointer/finger is currently over a uGUI element. On touch devices
        /// the fingerId overload is mandatory — the parameterless one only tracks the
        /// mouse and always returns false on a phone.
        /// </summary>
        private static bool PointerOverUi()
        {
            EventSystem es = EventSystem.current;
            if (es == null) return false;

            int touches = Input.touchCount;
            if (touches > 0)
            {
                for (int i = 0; i < touches; i++)
                {
                    Touch t = Input.GetTouch(i);
                    if (t.phase == TouchPhase.Canceled || t.phase == TouchPhase.Ended) continue;
                    if (es.IsPointerOverGameObject(t.fingerId)) return true;
                }
                return false;
            }
            return es.IsPointerOverGameObject();
        }

        // ── headless QC capture (-qcui <prefix>) ─────────────────────────────────

        private IEnumerator QcUi(string prefix)
        {
            Debug.Log("[UI] qcui start prefix=" + prefix);

            // 1) wait for the map to exist (SceneBuilder's root is named "Map").
            float t0 = Time.realtimeSinceStartup;
            while (GameObject.Find("Map") == null && Time.realtimeSinceStartup - t0 < 30f)
                yield return new WaitForSecondsRealtime(0.25f);
            bool mapReady = GameObject.Find("Map") != null;
            Debug.Log($"[UI] qcui map={(mapReady ? "ready" : "TIMEOUT")} after {Time.realtimeSinceStartup - t0:F1}s");
            yield return new WaitForSecondsRealtime(1.5f);

            // 2) menu
            OpenMenu();
            yield return new WaitForSecondsRealtime(1.0f);
            ScreenCapture.CaptureScreenshot(prefix + "_menu.png");
            Debug.Log("[UI] qcui shot -> " + prefix + "_menu.png");
            yield return new WaitForSecondsRealtime(1.5f);

            // 3) map list (live prod API + CDN thumbnails)
            OpenMapList();
            float t1 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t1 < 10f)
            {
                if (_mapList != null && !_mapList.IsLoading && _mapList.CardCount > 0 &&
                    _mapList.ThumbnailsLoaded >= Mathf.Min(3, _mapList.CardCount)) break;
                yield return new WaitForSecondsRealtime(0.25f);
            }
            Debug.Log($"[UI] qcui maps cards={(_mapList != null ? _mapList.CardCount : -1)} " +
                      $"total={(_mapList != null ? _mapList.Total : -1)} " +
                      $"thumbs={(_mapList != null ? _mapList.ThumbnailsLoaded : -1)} " +
                      $"err={(_mapList != null ? _mapList.LastError : "no screen")}");
            yield return new WaitForSecondsRealtime(0.6f);
            ScreenCapture.CaptureScreenshot(prefix + "_maps.png");
            Debug.Log("[UI] qcui shot -> " + prefix + "_maps.png");
            yield return new WaitForSecondsRealtime(1.5f);

            // 4) search "Chang" (server-side; expect exactly the Htms Chang demo map)
            if (_mapList != null)
            {
                _mapList.SetSearch("Chang");
                float t2 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t2 < 10f)
                {
                    if (_mapList.Query == "Chang" && !_mapList.IsLoading) break;
                    yield return new WaitForSecondsRealtime(0.25f);
                }
                Debug.Log($"[UI] qcui search q='{_mapList.Query}' cards={_mapList.CardCount} " +
                          $"total={_mapList.Total} err={_mapList.LastError}");
            }
            yield return new WaitForSecondsRealtime(0.8f);
            ScreenCapture.CaptureScreenshot(prefix + "_search.png");
            Debug.Log("[UI] qcui shot -> " + prefix + "_search.png");

            yield return new WaitForSecondsRealtime(2f);
            Debug.Log("[UI] qcui done");
            Application.Quit(0);
        }
    }
}
