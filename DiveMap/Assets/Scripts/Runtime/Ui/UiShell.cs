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

        /// <summary>
        /// Safe-area root for transient overlays that are not part of the navigation stack —
        /// <see cref="Toast"/> now, the tour/game HUD next (P1/P3). Inside the safe area so a
        /// notch or a gesture bar never clips them.
        /// </summary>
        public RectTransform OverlayRoot => _safe;

        private Canvas _canvas;
        private RectTransform _safe;
        private UiNav _nav;
        private ThumbnailCache _thumbs;

        private GameObject _hamburger;
        private GameObject _backButton;
        private Button _menuToggleBtn;
        private RectTransform _actions;
        private GameObject _mapsLayer;
        private MapListScreen _mapList;
        private GameObject _settingsLayer;
        private SettingsScreen _settings;
        private InfoCardController _card;

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

            // Saved graphics preset must be live before the first frame is presented.
            // "high" is a no-op by construction (it restores the captured defaults), so
            // a default install renders exactly as it did before WO-XR-05.4.
            if (SettingsStore.Gfx != SettingsStore.High)
                SettingsScreen.ApplyGraphics(SettingsStore.Gfx);

            BuildCanvas();
            BuildHamburger();
            BuildMapsScreen();
            BuildSettingsScreen();
            BuildInfoCard();

            _nav = gameObject.AddComponent<UiNav>();
            _nav.StackChanged += OnStackChanged;
            if (_card != null) _card.Build(_safe, _nav);

            Debug.Log($"[UI] shell ready (lang={UiStrings.Lang}, gfx={SettingsStore.Gfx}, " +
                      $"strings={UiStrings.Count})");
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
            // The web's ☰ lives at the BOTTOM-RIGHT (#viewbtns > #menuToggle) as a blue-filled
            // 48 px circle, not top-right as a square — someone moving between web and app must
            // find the menu under the same thumb.
            // #menuToggle sits in #viewbtns: right 12, bottom 20 + safe-area, 48×48 (builder.html:105).
            Button btn = UiKit.MakeIconButton(_safe, "MenuButton", "menu", ToggleActions,
                                              accent: true, size: UiKit.Css(48f));
            _hamburger = btn.gameObject;
            UiKit.Anchor(btn.GetComponent<RectTransform>(), new Vector2(1f, 0f),
                         new Vector2(UiKit.Css(48f), UiKit.Css(48f)),
                         new Vector2(-UiKit.Css(12f), UiKit.Css(20f)));

            // (The three hand-built Image bars are gone: IconPainter draws the web's own ☰ path.)
            _menuToggleBtn = btn;
            BuildActions();

            // The web's compass lives in the MAP VIEW (#compass: right 12, bottom 80), not in the
            // tour — the tour has the minimap instead. Same here.
            CompassWidget.Create(_safe);
            DepthLegend.Create(_safe);

            // #backBtn: left 12, top max(16,safe), 48 px glass circle with the chevron. Shown only
            // while something is open, which is exactly when the web shows it.
            Button back = UiKit.MakeIconButton(_safe, "BackButton", "back", CloseTop,
                                               false, UiKit.Css(48f));
            _backButton = back.gameObject;
            UiKit.Anchor(back.GetComponent<RectTransform>(), new Vector2(0f, 1f),
                         new Vector2(UiKit.Css(48f), UiKit.Css(48f)),
                         new Vector2(UiKit.Css(12f), -UiKit.Css(16f)));
            _backButton.SetActive(false);
        }

        /// <summary>
        /// The web's ☰ does not open a panel — it EXPANDS a column of round icon buttons above
        /// itself (#actions, builder.html:3432, icon swapping ☰ ↔ ✕). Matching that is most of
        /// what makes the app feel like the same product, and it keeps the map visible while you
        /// choose, which a full-screen menu does not.
        /// </summary>
        private void BuildActions()
        {
            _actions = UiKit.MakeNode(_safe, "Actions");
            _actions.anchorMin = new Vector2(1f, 0f);
            _actions.anchorMax = new Vector2(1f, 0f);
            _actions.pivot = new Vector2(1f, 0f);
            // The column sits directly above the toggle: 48 px buttons with the web's 10 px gap.
            _actions.sizeDelta = new Vector2(UiKit.Css(48f), UiKit.Css(48f * 5f + 40f));
            _actions.anchoredPosition = new Vector2(-UiKit.Css(12f), UiKit.Css(20f + 48f + 10f));

            ActionButton(0, "list", OpenMapList);
            ActionButton(1, "mask", StartTour);
            ActionButton(2, "depth", ToggleDepthView);   // the web's #depthViewBtn
            ActionButton(3, "wave", ToggleEnv);          // the web's #env (☀️ / 💧)
            ActionButton(4, "gear", OpenSettings);
            _actions.gameObject.SetActive(false);
        }

        private void ActionButton(int index, string icon, UnityEngine.Events.UnityAction action)
        {
            Button b = UiKit.MakeIconButton(_actions, "Action_" + icon, icon, () =>
            {
                CloseActions();
                action?.Invoke();
            }, false, UiKit.Css(48f));
            RectTransform rt = b.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(UiKit.Css(48f), UiKit.Css(48f));
            rt.anchoredPosition = new Vector2(0f, index * UiKit.Css(58f));   // 48 + 10 gap
        }

        /// <summary>Expand/collapse the action column and swap ☰ ↔ ✕, like the web.</summary>
        public void ToggleActions()
        {
            if (_actions == null) return;
            bool open = !_actions.gameObject.activeSelf;
            _actions.gameObject.SetActive(open);
            UiKit.SetIcon(_menuToggleBtn, open ? "close" : "menu");
            if (CompassWidget.Instance != null) CompassWidget.Instance.SetVisible(!open);
        }

        public void CloseActions()
        {
            if (_actions == null || !_actions.gameObject.activeSelf) return;
            _actions.gameObject.SetActive(false);
            UiKit.SetIcon(_menuToggleBtn, "menu");
            if (CompassWidget.Instance != null) CompassWidget.Instance.SetVisible(true);
        }

        // (The slide-in menu panel from WO-XR-05.1 is gone: the web has no menu panel, it has the
        // #actions column built in BuildActions above. Removing it also removes a second place
        // where menu items had to be kept in sync.)

        private void BuildMapsScreen()
        {
            _mapsLayer = UiKit.MakeNode(_safe, "MapsLayer").gameObject;
            _mapList = _mapsLayer.AddComponent<MapListScreen>();
            _mapList.Build(_thumbs);
            _mapList.CloseRequested += CloseTop;
            _mapList.MapSelected += OnMapSelected;
            _mapsLayer.SetActive(false);
        }

        /// <summary>Settings screen (WO-XR-05.4) — replaces the 05.1 "coming soon" card.</summary>
        private void BuildSettingsScreen()
        {
            _settingsLayer = UiKit.MakeNode(_safe, "SettingsLayer").gameObject;
            _settings = _settingsLayer.AddComponent<SettingsScreen>();
            _settings.Build();
            _settings.CloseRequested += CloseTop;
            _settings.LanguageChanged += ApplyLanguage;
            _settingsLayer.SetActive(false);
        }

        /// <summary>
        /// Info card (WO-XR-05.3). It lives on the shell GameObject rather than on a
        /// screen layer because it is NOT a screen: it never enters the nav stack, so the
        /// orbit camera keeps responding while the card is open.
        /// </summary>
        private void BuildInfoCard()
        {
            _card = gameObject.AddComponent<InfoCardController>();
        }

        // ── navigation ───────────────────────────────────────────────────────────

        /// <summary>
        /// Kept for existing callers (QC harness, Android back): the web has no menu PANEL, so this
        /// expands the action column instead of pushing a screen.
        /// </summary>
        public void OpenMenu()
        {
            if (_actions == null) return;
            if (_actions.gameObject.activeSelf) return;
            ToggleActions();
        }

        /// <summary>
        /// Enter the drone tour (P1.1). The menu closes itself: ModeManager hides the chrome on
        /// the way in, and a menu left open over a first-person mode would eat the joystick.
        /// </summary>
        public void StartTour()
        {
            CloseActions();
            CloseAll();
            if (!TourController.Start()) Toast.ShowTr("ยังเข้าทัวร์ไม่ได้");
        }

        /// <summary>
        /// Depth heat-map (the web's #depthViewBtn): recolours the seabed by how deep it is and
        /// shows the legend. Stays on while you fly the map, off in the tour — where the web
        /// hides its legend too.
        /// </summary>
        public void ToggleDepthView()
        {
            bool on = SeabedView.Toggle();
            if (DepthLegend.Instance != null) DepthLegend.Instance.SetVisible(on);
            Toast.ShowTr(on ? "แสดงความลึก (สี)" : "แสดงพื้นทรายปกติ");
        }

        /// <summary>
        /// Daylight ☀️ / underwater 💧, the web's #env button — which swaps its own icon between
        /// the sun and the waves to show what the NEXT press gives you.
        /// </summary>
        public void ToggleEnv()
        {
            bool daylight = EnvMode.Toggle();
            Button b = _actions != null ? _actions.Find("Action_wave")?.GetComponent<Button>() : null;
            if (b != null) UiKit.SetIcon(b, daylight ? "wave" : "sun");
            Toast.ShowTr(daylight ? "โหมดกลางวัน" : "โหมดใต้น้ำ");
        }

        /// <summary>
        /// Destination picker for a warp gate: the map list, opened straight from the tour. The web
        /// leaves the tour to choose (its warp reloads the page), and so does this — a sheet over a
        /// first-person view would fight the joysticks.
        /// </summary>
        public void OpenWarpPicker()
        {
            if (ModeManager.Instance != null) ModeManager.Instance.Exit();
            OpenMapList();
        }

        public void OpenMapList()
        {
            CloseActions();   // opening a screen collapses the column (and keeps QC shots clean)
            if (_nav == null || _mapsLayer == null) return;
            _nav.Push("maps", _mapsLayer);
            if (_mapList != null) _mapList.EnsureLoaded();
        }

        public void OpenSettings()
        {
            CloseActions();
            if (_nav == null || _settingsLayer == null) return;
            if (_settings != null) _settings.Refresh();
            _nav.Push("settings", _settingsLayer);
        }

        /// <summary>The info card controller (QC + deep links).</summary>
        public InfoCardController Card => _card;

        /// <summary>
        /// Re-render the whole UI in the current language, without rebuilding a single
        /// screen. Every live <see cref="Text"/> is passed through
        /// <see cref="UiStrings.ToLang"/>, which maps a displayed string back to its Thai
        /// source and forward into the target language; strings that are not in the table
        /// (map names, owner names, numbers) are returned untouched.
        ///
        /// This is why screens owned by other work orders — MapListScreen and AppBoot's
        /// own BootCanvas status/error lines — follow the language switch even though
        /// neither file is edited here.
        /// </summary>
        public void ApplyLanguage()
        {
            string lang = UiStrings.Lang;
            int touched = 0;

            foreach (Text t in UnityEngine.Object.FindObjectsByType<Text>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t == null || string.IsNullOrEmpty(t.text)) continue;

                // Never rewrite what the user typed into a text field.
                InputField field = t.GetComponentInParent<InputField>(true);
                if (field != null && field.textComponent == t) continue;

                string next = UiStrings.ToLang(t.text, lang);
                if (!string.Equals(next, t.text, StringComparison.Ordinal))
                {
                    t.text = next;
                    touched++;
                }
            }

            if (_card != null) _card.Render();
            if (_settings != null) _settings.Refresh();
            // The map header is a composed string, so the loop above can never match it —
            // AppBoot re-composes it from its parts (P0).
            AppBoot boot = UnityEngine.Object.FindFirstObjectByType<AppBoot>();
            if (boot != null) boot.RefreshStatusLanguage();

            Debug.Log($"[UI] language={lang} retranslated={touched} texts");
        }

        public void CloseTop()
        {
            if (_nav != null) _nav.Pop();
        }

        public void CloseAll()
        {
            if (_nav != null) _nav.PopAll();
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
            // Hide the ☰ affordance while a screen owns the display (or while a mode hides it),
            // and show the web's back chevron in its place.
            if (_hamburger != null) _hamburger.SetActive(depth == 0 && _chromeVisible);
            if (_backButton != null) _backButton.SetActive(depth > 0 && _chromeVisible);
            // The info card is not in the stack, so it would otherwise sit UNDER an opened sheet
            // and reappear behind it (the settings QC shot showed exactly that).
            if (depth > 0 && _card != null) _card.Hide();
            SetOrbitEnabled(depth == 0);
        }

        /// <summary>The shell's own canvas (sortingOrder 10, above AppBoot's BootCanvas).</summary>
        public Canvas ShellCanvas => _canvas;

        /// <summary>
        /// Show/hide the shell chrome (the ☰ button) for the current mode — a first-person tour
        /// owns the whole screen (P0.5). Any open screen is closed on the way in, so the user
        /// cannot end up flying the drone with the map list still on top.
        /// </summary>
        /// <summary>
        /// Apply one mode's chrome rules in one place, mirroring the web's <c>body.tour</c> block
        /// (builder.html:233-234): hide #backBtn / #viewbtns / #count, and MOVE the compass up
        /// beside the depth pill rather than hiding it.
        /// </summary>
        public void ApplyModeChrome(AppMode mode)
        {
            bool mapView = ModeRules.AllowsMenu(mode);
            SetChromeVisible(mapView);

            if (CompassWidget.Instance != null)
                CompassWidget.Instance.SetTourLayout(ModeRules.IsFirstPerson(mode));

            // The web hides #depthLegend in the tour/AR/preview.
            if (DepthLegend.Instance != null && !mapView) DepthLegend.Instance.SetVisible(false);

            AppBoot boot = UnityEngine.Object.FindFirstObjectByType<AppBoot>();
            if (boot != null) boot.SetStatusVisible(mapView);   // the web hides #count in the tour
        }

        public void SetChromeVisible(bool visible)
        {
            if (!visible)
            {
                CloseActions();
                CloseAll();
                // The info card is NOT in the nav stack (by design — it must not block the
                // orbit camera), so CloseAll misses it. The first tour QC shot caught it sitting
                // over both joysticks.
                if (_card != null) _card.Hide();
            }
            if (_hamburger != null) _hamburger.SetActive(visible && (_nav == null || _nav.Count == 0));
            if (_backButton != null) _backButton.SetActive(visible && _nav != null && _nav.Count > 0);
            _chromeVisible = visible;
        }
        private bool _chromeVisible = true;

        private void Update()
        {
            ApplySafeArea(false);

            // Three independent vetoes: an open screen, a finger on a UI element, and the
            // current mode (a first-person tour must not also orbit — P0.5).
            bool allow = _nav == null || _nav.Count == 0;
            if (allow && !ModeManager.OrbitAllowed) allow = false;
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

            // 0) Deterministic language. The CI player reports SystemLanguage.English, so
            // without this the Thai shots would silently come out in English and the
            // th→en comparison at the end would prove nothing.
            UiStrings.Lang = UiStrings.Thai;
            ApplyLanguage();

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
            yield return new WaitForSecondsRealtime(1.5f);

            // 5) info card (WO-XR-05.3). Shown directly by assetId — simulating a touch
            // in a headless player is not reproducible, and the pick math already has
            // unit tests (ItemPickerTests); what this shot proves is the CARD.
            CloseAll();
            yield return new WaitForSecondsRealtime(0.5f);
            bool card = _card != null && _card.ShowCardFor("cc0:wreck_chang");
            Debug.Log($"[UI] qcui card shown={card} name={(_card != null ? _card.CurrentName : null)} " +
                      $"kind={(_card != null ? _card.CurrentKind : null)} " +
                      $"depth={(_card != null ? _card.CurrentDepth : -1):F1}");
            yield return new WaitForSecondsRealtime(0.8f);
            ScreenCapture.CaptureScreenshot(prefix + "_card.png");
            Debug.Log("[UI] qcui shot -> " + prefix + "_card.png");
            yield return new WaitForSecondsRealtime(1.5f);

            // 6) settings (WO-XR-05.4)
            OpenSettings();
            yield return new WaitForSecondsRealtime(0.8f);
            ScreenCapture.CaptureScreenshot(prefix + "_settings.png");
            Debug.Log("[UI] qcui shot -> " + prefix + "_settings.png");
            yield return new WaitForSecondsRealtime(1.5f);

            // 6.2) depth heat-map (P2a) and 6.3) daylight (P2b): both are one-press view changes
            // that no unit test can show, so the QC eye takes them.
            CloseAll();
            yield return new WaitForSecondsRealtime(0.4f);
            ToggleDepthView();
            yield return new WaitForSecondsRealtime(1.0f);
            ScreenCapture.CaptureScreenshot(prefix + "_depth.png");
            Debug.Log("[UI] qcui shot -> " + prefix + "_depth.png");
            yield return new WaitForSecondsRealtime(1.2f);
            ToggleDepthView();          // back to sand

            ToggleEnv();
            yield return new WaitForSecondsRealtime(1.0f);
            ScreenCapture.CaptureScreenshot(prefix + "_daylight.png");
            Debug.Log("[UI] qcui shot -> " + prefix + "_daylight.png");
            yield return new WaitForSecondsRealtime(1.2f);
            ToggleEnv();                // back underwater

            // 6.5) toast (P0). Proves the new transient line renders — and renders THAI — before
            // the tour/game work starts leaning on it. Captured with the map visible, not over a
            // panel, because that is where it will actually appear.
            CloseAll();
            yield return new WaitForSecondsRealtime(0.4f);
            Toast.ShowTr("บันทึกแล้ว");
            yield return new WaitForSecondsRealtime(0.6f);
            ScreenCapture.CaptureScreenshot(prefix + "_toast.png");
            Debug.Log("[UI] qcui shot -> " + prefix + "_toast.png");
            yield return new WaitForSecondsRealtime(1.5f);

            // 6.7) tour HUD (P1.1): joysticks + depth + exit, with the drone parked (no input in
            // a headless run). Proves the HUD builds and that the mode swap actually hides the
            // shell chrome, which no unit test can show.
            if (TourController.Start())
            {
                yield return new WaitForSecondsRealtime(1.2f);
                ScreenCapture.CaptureScreenshot(prefix + "_tour.png");
                Debug.Log("[UI] qcui shot -> " + prefix + "_tour.png");

                // Stay long enough for the litter to fall into view (28 u/s from just under the
                // surface): the first game shot was taken 2 s in and caught an empty ocean.
                yield return new WaitForSecondsRealtime(6f);
                ScreenCapture.CaptureScreenshot(prefix + "_game.png");
                Debug.Log("[UI] qcui shot -> " + prefix + "_game.png");
                yield return new WaitForSecondsRealtime(1.2f);
                if (ModeManager.Instance != null) ModeManager.Instance.Exit();
                yield return new WaitForSecondsRealtime(0.8f);
            }
            else Debug.LogWarning("[UI] qcui could not enter the tour");

            // 7) English: the menu + the still-open card must come back with no Thai left.
            UiStrings.Lang = UiStrings.English;
            ApplyLanguage();
            CloseAll();
            OpenMenu();
            yield return new WaitForSecondsRealtime(1.0f);
            ScreenCapture.CaptureScreenshot(prefix + "_en.png");
            Debug.Log("[UI] qcui shot -> " + prefix + "_en.png (card name=" +
                      (_card != null ? _card.CurrentName : null) + ")");

            // Leave the preference as we found it — the QC player writes real PlayerPrefs.
            UiStrings.Lang = UiStrings.Thai;

            yield return new WaitForSecondsRealtime(2f);
            Debug.Log("[UI] qcui done");
            Application.Quit(0);
        }
    }
}
