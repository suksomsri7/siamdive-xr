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

        /// <summary>
        /// The shared image cache. The palette pulls the server's pre-rendered item thumbnails
        /// through the same queue as the map hub's cards — one cap on concurrent downloads for
        /// the whole app rather than one per screen.
        /// </summary>
        public ThumbnailCache Thumbs => _thumbs;

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
            // TWO columns. One column of 12 buttons needs 12×58 + the toggle's 78 = 774 css px,
            // which does not fit a 720-tall phone — the QC menu shot showed the twelfth button
            // (AR) simply missing off the top edge, with nothing in any log to say so.
            _actions.sizeDelta = new Vector2(UiKit.Css(48f * 2f + 10f),
                                             UiKit.Css(48f * ActionRows + 40f));
            _actions.anchoredPosition = new Vector2(-UiKit.Css(12f), UiKit.Css(20f + 48f + 10f));

            ActionButton(0, "list", OpenMapList);
            ActionButton(1, "mask", StartTour);
            ActionButton(2, "depth", ToggleDepthView);   // the web's #depthViewBtn
            ActionButton(3, "wave", ToggleEnv);          // the web's #env (☀️ / 💧)
            ActionButton(4, "gear", OpenSettings);

            // 🔳 AR — the web's enterAR. Sits with the "look at the map" tools rather than the
            // editing ones: it is a way of viewing this site, not a way of changing it.
            ActionButton(11, "ar", () =>
            {
                if (!ArSession.Start()) Toast.ShowTr("เข้า AR ตอนนี้ไม่ได้");
            });

            // 📋 objects — only useful on a map this account can write to, and the button is
            // built once at startup when that is not yet known, so it decides at TAP time.
            ActionButton(5, "objects", () =>
            {
                var boot = FindFirstObjectByType<AppBoot>();
                if (boot == null || !boot.CanEditCurrent) { Toast.ShowTr("แมพนี้แก้ไม่ได้"); return; }
                ObjectListSheet.Open();
            });

            // 🕘 version history — the safety net under undo, which only reaches back as far as
            // this session. Owner-only on the server, so it says so rather than failing quietly.
            ActionButton(6, "history", () =>
            {
                var boot = FindFirstObjectByType<AppBoot>();
                if (boot == null || string.IsNullOrEmpty(boot.CurrentMapId)) return;
                RevisionSheet.Open();
            });

            // 📍 drop a pin
            ActionButton(10, "pin", () =>
            {
                var boot = FindFirstObjectByType<AppBoot>();
                if (boot == null || !boot.CanEditCurrent) { Toast.ShowTr("แมพนี้แก้ไม่ได้"); return; }
                CloseActions();
                PinPlacer.Start();
            });

            // ⚙️ map settings — name, public/private, search listing, editors, water, area, clear
            ActionButton(9, "sliders", () =>
            {
                var boot = FindFirstObjectByType<AppBoot>();
                if (boot == null || !boot.CanEditCurrent) { Toast.ShowTr("แมพนี้แก้ไม่ได้"); return; }
                CloseActions();
                MapSettingsSheet.Open();
            });

            // 🪢 tie a rope between two objects
            ActionButton(8, "rope", () =>
            {
                var boot = FindFirstObjectByType<AppBoot>();
                if (boot == null || !boot.CanEditCurrent) { Toast.ShowTr("แมพนี้แก้ไม่ได้"); return; }
                CloseActions();
                RopeSheet.StartTie();
            });

            // ⛰️ sculpt the floor
            ActionButton(7, "mountain", () =>
            {
                var boot = FindFirstObjectByType<AppBoot>();
                if (boot == null || !boot.CanEditCurrent) { Toast.ShowTr("แมพนี้แก้ไม่ได้"); return; }
                CloseActions();
                SculptSheet.Open();
            });
            _actions.gameObject.SetActive(false);
        }

        /// <summary>How tall the action column is allowed to get before it wraps.</summary>
        private const int ActionRows = 6;

        private void ActionButton(int index, string icon, UnityEngine.Events.UnityAction action)
        {
            Button b = UiKit.MakeIconButton(_actions, "Action_" + icon, icon, () =>
            {
                CloseActions();
                action?.Invoke();
            }, false, UiKit.Css(48f));
            RectTransform rt = b.GetComponent<RectTransform>();
            // Right-aligned inside the column box: widening the box for a second column must not
            // shift the first one, or every existing button moves away from the ☰ it hangs under.
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.sizeDelta = new Vector2(UiKit.Css(48f), UiKit.Css(48f));
            // Fill bottom-up, then wrap into a second column to the LEFT — away from the screen
            // edge, so a wrapped button is still under the thumb rather than at the rim.
            int row = index % ActionRows;
            int col = index / ActionRows;
            rt.anchoredPosition = new Vector2(-col * UiKit.Css(58f), row * UiKit.Css(58f));
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
            _mapList.WorldsRequested += OpenWorlds;
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
            // Remember that this exit was a warp, so arriving at the destination puts the diver
            // straight back in the water at a random point rather than at a menu — the web's
            // warp lands you IN the next map, not looking at it.
            TourController.ArrivingByWarp = true;
            if (ModeManager.Instance != null) ModeManager.Instance.Exit();
            OpenMapList();
        }

        /// <summary>
        /// The web's <c>leaveModal</c>: never walk away from unsaved work in silence. Autosave
        /// usually has it already — this covers the gap between the last edit and the 1.3 s tick,
        /// and tells the player plainly when the map is one it cannot save to.
        /// </summary>
        /// <summary>
        /// E6 — the arena exit gate (<c>_arenaExitGate</c> :4398). Leaving a game session with
        /// coins earned but no account means those coins are gone: the wallet is keyed to the
        /// device, and the player has no way back to them from another phone.
        ///
        /// The web's rule exactly: signed in → flush and go; nothing earned → go; otherwise ask
        /// once, with the number, and let them leave anyway.
        /// </summary>
        public void ArenaExitGate(Action go)
        {
            int earned = TrashGameSystem.EarnedThisSession;

            if (DiveMap.Core.Account.IsSignedIn) { WalletClient.Flush(); go?.Invoke(); return; }
            if (earned <= 0) { go?.Invoke(); return; }

            ActionSheet sheet = ActionSheet.Show(
                UiStrings.Tr("เก็บเหรียญที่ได้?") + "  " + earned);
            if (sheet == null) { go?.Invoke(); return; }

            sheet.AddItem(UiStrings.Tr("เข้าสู่ระบบเก็บ"), () =>
            {
                // Do NOT leave yet: the coins have to reach the account first, and navigating
                // away mid-request is exactly how they get lost.
                LoginSheet.SignedIn += OnceThenGo;
                LoginSheet.Open();
                void OnceThenGo()
                {
                    LoginSheet.SignedIn -= OnceThenGo;
                    WalletClient.Flush();
                    go?.Invoke();
                }
            });
            sheet.AddItem(UiStrings.Tr("ทิ้ง"), () => go?.Invoke(), true);
            sheet.AddCancel(UiStrings.Tr("ภายหลัง"));
            Debug.Log($"[Game] arena exit gate — {earned} coin(s) at risk");
        }

        private void GuardUnsaved()
        {
            if (!MapEditor.IsDirty) return;
            if (MapEditor.SaveRefused) { Toast.ShowTr("แมพนี้แก้ไม่ได้ — เก็บไว้ในเครื่องนี้แทน"); return; }
            MapEditor.Flush();
            Toast.ShowTr("กำลังบันทึก…");
        }

        public void OpenMapList()
        {
            GuardUnsaved();
            // Coins earned this session vanish if the player walks away without an account.
            if (TrashGameSystem.EarnedThisSession > 0 && !DiveMap.Core.Account.IsSignedIn)
            {
                ArenaExitGate(OpenMapListNow);
                return;
            }
            OpenMapListNow();
        }

        private void OpenMapListNow()
        {
            CloseActions();   // opening a screen collapses the column (and keeps QC shots clean)
            if (_nav == null || _mapsLayer == null) return;
            _nav.Push("maps", _mapsLayer);
            if (_mapList != null) _mapList.EnsureLoaded();
        }

        /// <summary>
        /// "Play Game!" → the worlds picker, then dive straight in. Same trip as the warp gate:
        /// <see cref="TourController.ArrivingByWarp"/> puts the diver in the water at the
        /// destination instead of parking them in front of a menu — the banner promises
        /// "dive in", so landing on an orbit camera would be a broken promise.
        /// </summary>
        public void OpenWorlds()
        {
            if (_mapList == null) return;
            WorldsPopup.Show(_mapList.Cards, _thumbs, shortId =>
            {
                // The web keeps these two apart (arenaPlay vs _warpPlay) and so does this now.
                // Borrowing the warp flag worked, but it made the banner the ONLY way into a
                // world: a player who opened the very same map from its card in the list got the
                // orbit view instead, because nothing else set the flag. The rule is about the
                // map, not the door you came through — see Core.ArenaEntry.
                TourController.ArenaPlay = true;
                OnMapSelected(shortId);
            });
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
            // Card bylines are "by " + a name that is not in the table, so the sweep above can
            // never match them — the screen re-composes its own composed strings.
            if (_mapList != null) _mapList.RefreshLanguage();
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
        internal static bool PointerOverUi()
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

        /// <summary>How many of the named map parts are currently hidden (QC only).</summary>
        private static int ArPartsHidden(Transform map, string[] names)
        {
            int n = 0;
            foreach (string s in names)
            {
                Transform t = map != null ? map.Find(s) : null;
                if (t != null && !t.gameObject.activeSelf) n++;
            }
            return n;
        }

        /// <summary>How many of the named parts this map actually has (QC only).</summary>
        private static int ArPartsPresent(Transform map, string[] names)
        {
            int n = 0;
            foreach (string s in names) if (map != null && map.Find(s) != null) n++;
            return n;
        }

        /// <summary>How many parts came back exactly as they were found (QC only).</summary>
        private static int ArPartsMatch(Transform map, string[] names, System.Collections.Generic.List<bool> was)
        {
            int n = 0;
            for (int i = 0; i < names.Length; i++)
            {
                Transform t = map != null ? map.Find(names[i]) : null;
                bool now = t != null && t.gameObject.activeSelf;
                if (now == was[i]) n++;
            }
            return n;
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
                // The banner's coin comes from StreamingAssets, so it is part of "the page is
                // drawn" — a shot taken before it lands looks like the banner has a hole in it.
                if (_mapList != null && !_mapList.IsLoading && _mapList.CardCount > 0 &&
                    _mapList.BannerReady &&
                    _mapList.ThumbnailsLoaded >= Mathf.Min(3, _mapList.CardCount)) break;
                yield return new WaitForSecondsRealtime(0.25f);
            }
            Debug.Log($"[QC] offline cached={DiveMap.Runtime.OfflineStore.Count} " +
                      $"demo={DiveMap.Runtime.OfflineStore.Has(MapApiClient.DefaultShortId)}");
            int worlds = 0;
            if (_mapList != null)
                foreach (MapCard c in _mapList.Cards) if (MapDirectory.IsOfficial(c)) worlds++;
            Debug.Log($"[UI] qcui maps cards={(_mapList != null ? _mapList.CardCount : -1)} " +
                      $"total={(_mapList != null ? _mapList.Total : -1)} " +
                      $"thumbs={(_mapList != null ? _mapList.ThumbnailsLoaded : -1)} " +
                      $"banner={(_mapList != null && _mapList.BannerReady ? "ok" : "MISSING")} " +
                      $"worlds={worlds} " +
                      $"err={(_mapList != null ? _mapList.LastError : "no screen")}");
            yield return new WaitForSecondsRealtime(0.6f);
            ScreenCapture.CaptureScreenshot(prefix + "_maps.png");
            Debug.Log("[UI] qcui shot -> " + prefix + "_maps.png");
            yield return new WaitForSecondsRealtime(1.5f);

            // 3a) J — sign-in. Only the FIRST step is photographed: the later ones need a real
            // OTP from a real mailbox, which no headless run can have. What this proves is that
            // the button opens the right sheet and the sheet draws.
            LoginSheet.Open();
            yield return new WaitForSecondsRealtime(0.8f);
            LoginSheet login = LoginSheet.Current;
            Debug.Log($"[UI] qcui login open={(login != null)} step={(login != null ? login.StepName : null)} " +
                      $"signedIn={DiveMap.Core.Account.IsSignedIn}");
            ScreenCapture.CaptureScreenshot(prefix + "_login.png");
            Debug.Log("[UI] qcui shot -> " + prefix + "_login.png");
            yield return new WaitForSecondsRealtime(1.2f);
            LoginSheet.Close();
            yield return new WaitForSecondsRealtime(0.4f);

            // 3b) the "Play Game!" banner's destination — proves the banner is wired, not paint.
            OpenWorlds();
            yield return new WaitForSecondsRealtime(1.0f);
            WorldsPopup popup = UnityEngine.Object.FindFirstObjectByType<WorldsPopup>();
            Debug.Log($"[UI] qcui worlds popup={(popup != null ? "open" : "MISSING")} " +
                      $"rows={(popup != null ? popup.RowCount : -1)}");
            ScreenCapture.CaptureScreenshot(prefix + "_worlds.png");
            Debug.Log("[UI] qcui shot -> " + prefix + "_worlds.png");
            yield return new WaitForSecondsRealtime(1.2f);
            if (popup != null) popup.Close();
            yield return new WaitForSecondsRealtime(0.5f);

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

            // 6.1) A7 — the frame-rate readout, over the map where it actually sits. Captured on
            // its own and switched back off, so it cannot creep into the parity shots.
            CloseAll();
            bool perfWas = PerfHud.Enabled;
            PerfHud.Enabled = true;
            yield return new WaitForSecondsRealtime(1.5f);   // it needs a sample window to show a number
            ScreenCapture.CaptureScreenshot(prefix + "_perf.png");
            Debug.Log("[UI] qcui shot -> " + prefix + "_perf.png");
            yield return new WaitForSecondsRealtime(1.2f);
            PerfHud.Enabled = perfWas;
            yield return new WaitForSecondsRealtime(0.4f);

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
            // 6.5) I — editing. This block MUST run in the MAP VIEW, not the tour: the gizmo
            // deliberately deselects the moment the mode is not View (that is the fix for
            // IMPROVEMENTS F4). Running it inside the tour is what made the second gesture
            // report "released with no drag" against an empty id — the code was right and the
            // test was in the wrong mode.
            // The toolbar's actions are the ones that change the map, so
            // this drives the whole chain: select → duplicate → undo → redo, checking the
            // item count after each. A screenshot proves the pill is drawn; the counts prove
            // it did what it says.
            {
            AppBoot eb = FindFirstObjectByType<AppBoot>();
            DiveMap.Core.SceneData sc = eb != null ? eb.CurrentScene : null;
            Newtonsoft.Json.Linq.JArray its = sc != null ? DiveMap.Core.SceneEdit.Items(sc) : null;
            string pick = its != null && its.Count > 0
                ? (string)its[0]["id"] : null;

            Debug.Log($"[QC] edit test map items={(its != null ? its.Count : -1)} pick={pick}");
            if (pick != null)
            {
                SelectionToolbar.Show(pick, true);
                yield return new WaitForSecondsRealtime(0.8f);
                ScreenCapture.CaptureScreenshot(prefix + "_seltool.png");
                Debug.Log("[UI] qcui shot -> " + prefix + "_seltool.png");
                yield return new WaitForSecondsRealtime(1.0f);

                int before = its.Count;
                Button dup = FindDeep(UiShell.Instance.OverlayRoot, "Dup")?.GetComponent<Button>();
                if (dup != null) dup.onClick.Invoke(); else Debug.LogWarning("[QC] no Dup button");
                yield return new WaitForSecondsRealtime(2.5f);

                DiveMap.Core.SceneData sc2 = eb.CurrentScene;
                int afterDup = DiveMap.Core.SceneEdit.Items(sc2).Count;
                bool undone = MapEditor.Undo();
                yield return new WaitForSecondsRealtime(2.5f);
                int afterUndo = DiveMap.Core.SceneEdit.Items(eb.CurrentScene).Count;
                bool redone = MapEditor.Redo();
                yield return new WaitForSecondsRealtime(2.0f);
                int afterRedo = DiveMap.Core.SceneEdit.Items(eb.CurrentScene).Count;

                Debug.Log($"[QC] edit result items {before}→{afterDup} (dup, expected {before + 1}) " +
                          $"→{afterUndo} (undo, expected {before}) →{afterRedo} (redo, expected {before + 1}) " +
                          $"· undone={undone} redone={redone} history={MapEditor.HistoryCount} " +
                          $"saveRefused={MapEditor.SaveRefused}");

                // 6.93) the gizmo. No touch input exists in a headless player, so the
                // gesture is driven directly — what is being proven is the maths and the
                // one-snapshot-per-gesture rule, not the finger.
                Newtonsoft.Json.Linq.JObject before1 =
                    DiveMap.Core.SceneEdit.Find(DiveMap.Core.SceneEdit.Items(eb.CurrentScene), pick);
                double px0 = (double)before1["p"][0], pz0 = (double)before1["p"][2];
                // Capture the yaw BEFORE the drags: the expected value is relative to it,
                // and the last round's "expected 3.142" silently assumed it was zero.
                double yaw0 = before1["r"] != null ? (double)before1["r"][1] : 0.0;
                int hist0 = MapEditor.HistoryCount;

                GizmoController.Select(pick);
                GizmoController.QcDrag(SelectionToolbar.Mode.Translate,
                                       new Vector2(640f, 400f), new Vector2(760f, 470f));
                yield return new WaitForSecondsRealtime(2.5f);

                Newtonsoft.Json.Linq.JObject after1 =
                    DiveMap.Core.SceneEdit.Find(DiveMap.Core.SceneEdit.Items(eb.CurrentScene), pick);
                double px1 = (double)after1["p"][0], pz1 = (double)after1["p"][2];

                GizmoController.QcDrag(SelectionToolbar.Mode.Rotate,
                                       new Vector2(640f, 400f), new Vector2(850f, 400f));
                yield return new WaitForSecondsRealtime(2.0f);
                double yaw = (double)DiveMap.Core.SceneEdit
                    .Find(DiveMap.Core.SceneEdit.Items(eb.CurrentScene), pick)["r"][1];

                Debug.Log($"[QC] gizmo move ({px0:F1},{pz0:F1})→({px1:F1},{pz1:F1}) " +
                          $"moved={(px1 != px0 || pz1 != pz0)} · yaw {yaw0:F3}→{yaw:F3} " +
                          $"(expected {DiveMap.Core.GizmoMath.YawAfterDrag(yaw0, 210):F3}) " +
                          $"· history {hist0}→{MapEditor.HistoryCount} (expected +2, one per gesture)");

                // Put the map back: undo everything this block did.
                MapEditor.Undo(); yield return new WaitForSecondsRealtime(1.2f);
                MapEditor.Undo(); yield return new WaitForSecondsRealtime(1.2f);
                MapEditor.Undo();
                yield return new WaitForSecondsRealtime(2.0f);
                SelectionToolbar.Hide();
                GizmoController.Deselect();
                yield return new WaitForSecondsRealtime(0.4f);

                // 6.6) the object list — the only route to something buried inside a wreck.
                ObjectListSheet.Open();
                yield return new WaitForSecondsRealtime(1.0f);
                ObjectListSheet ol = ObjectListSheet.Current;
                Debug.Log($"[QC] object list open={(ol != null)} rows={(ol != null ? ol.RowCount : -1)} " +
                          $"filter='{(ol != null ? ol.KindFilter : null)}'");
                ScreenCapture.CaptureScreenshot(prefix + "_objlist.png");
                Debug.Log("[UI] qcui shot -> " + prefix + "_objlist.png");
                yield return new WaitForSecondsRealtime(1.2f);
                // I4 — group select, then a group scale about the shared pivot. The measurable
                // part is that the items move APART, not just grow: scaling about each object's
                // own origin collapses an arrangement into a pile and errors nowhere.
                if (ol != null && its.Count >= 2)
                {
                    string g1 = (string)its[0]["id"], g2 = (string)its[1]["id"];
                    var pivotOk = DiveMap.Core.MultiSelect.Pivot(
                        DiveMap.Core.SceneEdit.Items(eb.CurrentScene), new[] { g1, g2 },
                        out double _, out double _, out double _);
                    double gap0 = Gap(eb, g1, g2);
                    ol.QcPick(g1, g2);
                    yield return new WaitForSecondsRealtime(0.6f);
                    ScreenCapture.CaptureScreenshot(prefix + "_multiselect.png");
                    Debug.Log("[UI] qcui shot -> " + prefix + "_multiselect.png");
                    ol.GroupAction("scale");
                    yield return new WaitForSecondsRealtime(2.0f);
                    double gap1 = Gap(eb, g1, g2);
                    Debug.Log($"[QC] multiselect picked={ol.PickedCount} pivotOk={pivotOk} " +
                              $"gap {gap0:F1}→{gap1:F1} (expected ×1.25 = {gap0 * 1.25:F1})");
                    MapEditor.Undo();
                    yield return new WaitForSecondsRealtime(1.5f);
                }

                ObjectListSheet.Close();
                yield return new WaitForSecondsRealtime(0.4f);

                // 6.7) version history. The demo map is not owned by this device, so the server
                // answers 403 — which is the branch worth photographing: it must SAY so, not
                // sit on an empty list looking broken.
                RevisionSheet.Open();
                yield return new WaitForSecondsRealtime(2.5f);
                RevisionSheet rs = RevisionSheet.Current;
                Debug.Log($"[QC] revisions open={(rs != null)} rows={(rs != null ? rs.RowCount : -1)} " +
                          $"err={(rs != null ? rs.LastError : null)}");
                ScreenCapture.CaptureScreenshot(prefix + "_revisions.png");
                Debug.Log("[UI] qcui shot -> " + prefix + "_revisions.png");
                yield return new WaitForSecondsRealtime(1.2f);
                RevisionSheet.Close();
                yield return new WaitForSecondsRealtime(0.4f);

                // 6.8) sculpt. Paint at the centre of the screen — the orbit camera looks at the
                // seabed, so that ray lands on the floor — then check the heights actually moved.
                SculptSheet.Open();
                yield return new WaitForSecondsRealtime(0.8f);
                SculptSheet ss = SculptSheet.Current;
                float peakBefore = PeakHeight();
                if (ss != null)
                {
                    ss.QcPaint(new Vector2(Screen.width * 0.5f, Screen.height * 0.42f));
                    yield return new WaitForSecondsRealtime(1.2f);
                }
                Debug.Log($"[QC] sculpt open={(ss != null)} ready={SeabedSculptor.Ready} " +
                          $"samples={(SeabedSculptor.Heights != null ? SeabedSculptor.Heights.Length : -1)} " +
                          $"peak {peakBefore:F2}→{PeakHeight():F2} raise={(ss != null && ss.Raise)}");
                ScreenCapture.CaptureScreenshot(prefix + "_sculpt.png");
                Debug.Log("[UI] qcui shot -> " + prefix + "_sculpt.png");
                yield return new WaitForSecondsRealtime(1.2f);
                SeabedSculptor.Flatten();          // leave the demo map as we found it
                yield return new WaitForSecondsRealtime(0.6f);
                SculptSheet.Close();
                yield return new WaitForSecondsRealtime(0.4f);

                // 6.9) ropes. The demo map has none, so one is tied between the first two
                // objects and measured: it must MESH (a rope that parses but draws nothing is
                // the failure mode), then follow its object, then vanish when that object goes.
                RopeSystem rs2 = RopeSystem.Ensure();
                if (rs2 != null && its.Count >= 2)
                {
                    var ea = new DiveMap.Core.RopeEnd
                        { ItemId = (string)its[0]["id"], Lx = 0, Ly = 6, Lz = 0 };
                    var eb2 = new DiveMap.Core.RopeEnd
                        { ItemId = (string)its[1]["id"], Lx = 0, Ly = 6, Lz = 0 };
                    DiveMap.Core.Rope made = rs2.Add(ea, eb2);
                    yield return new WaitForSecondsRealtime(1.0f);

                    Debug.Log($"[QC] rope added={(made != null)} count={rs2.Count} drawn={rs2.DrawnCount} " +
                              $"sag={(made != null ? made.Sag : -1):F1}");
                    ScreenCapture.CaptureScreenshot(prefix + "_rope.png");
                    Debug.Log("[UI] qcui shot -> " + prefix + "_rope.png");
                    yield return new WaitForSecondsRealtime(1.2f);

                    // the edit panel, on the rope we just made
                    RopeSheet.Open(made.Id);
                    yield return new WaitForSecondsRealtime(0.8f);
                    Debug.Log($"[QC] rope panel open={RopeSheet.IsOpen} id={RopeSheet.Current?.RopeId}");
                    ScreenCapture.CaptureScreenshot(prefix + "_ropepanel.png");
                    Debug.Log("[UI] qcui shot -> " + prefix + "_ropepanel.png");
                    yield return new WaitForSecondsRealtime(1.0f);
                    RopeSheet.Close();
                    yield return new WaitForSecondsRealtime(0.4f);

                    // 6.94) pins — drop one on the seabed, then attach a photo to it. The
                    // upload is a real round trip to the media route, which sniffs magic bytes,
                    // so the bytes have to be a genuine PNG (EncodeToPNG makes one).
                    string pinId = PinPlacer.QcPlace(new Vector3(40f, 60f, 40f));
                    yield return new WaitForSecondsRealtime(1.0f);
                    int pinCount = eb.CurrentScene != null && eb.CurrentScene.Root["pins"] is Newtonsoft.Json.Linq.JArray pa
                        ? pa.Count : -1;
                    Debug.Log($"[QC] pin placed id={pinId} pins={pinCount} markers={PinMarker.Markers.Count}");

                    var tex = new Texture2D(8, 8);
                    for (int px = 0; px < 8; px++) for (int py = 0; py < 8; py++) tex.SetPixel(px, py, Color.cyan);
                    tex.Apply();
                    bool uploaded = false;
                    yield return PinPlacer.AddMedia(pinId, tex.EncodeToPNG(), ok => uploaded = ok);
                    Debug.Log($"[QC] pin media uploaded={uploaded}");
                    ScreenCapture.CaptureScreenshot(prefix + "_pin.png");
                    Debug.Log("[UI] qcui shot -> " + prefix + "_pin.png");
                    yield return new WaitForSecondsRealtime(1.0f);
                    PinPlacer.Remove(pinId);
                    yield return new WaitForSecondsRealtime(0.6f);

                    // 6.945) J3 — the map cover. Runs the WHOLE path (capture → upload → PATCH),
                    // because the interesting failures are all at the seams: chrome left in the
                    // frame, a half-read framebuffer, a url the PATCH rejects.
                    string cover = null;
                    yield return ThumbnailCapture.CaptureAndSave(u => cover = u);
                    Debug.Log($"[QC] cover url={cover ?? "(none)"}");
                    yield return new WaitForSecondsRealtime(0.6f);

                    // 6.95) map settings — the sheet that carries name / public / search /
                    // editors / water / area / clear.
                    MapSettingsSheet.Open();
                    yield return new WaitForSecondsRealtime(1.0f);
                    Debug.Log($"[QC] map settings open={MapSettingsSheet.IsOpen} " +
                              $"public={(MapSettingsSheet.Current != null && MapSettingsSheet.Current.PublicToggle)}");
                    ScreenCapture.CaptureScreenshot(prefix + "_mapsettings.png");
                    Debug.Log("[UI] qcui shot -> " + prefix + "_mapsettings.png");
                    yield return new WaitForSecondsRealtime(1.2f);
                    MapSettingsSheet.Close();
                    yield return new WaitForSecondsRealtime(0.4f);

                    RopeSystem.DetachFrom((string)its[0]["id"]);
                    yield return new WaitForSecondsRealtime(0.6f);
                    Debug.Log($"[QC] rope after detach count={rs2.Count} drawn={rs2.DrawnCount} (expected 0/0)");
                }
                yield return new WaitForSecondsRealtime(0.4f);
            }
            }

            // 6.13) AR (F1/F4). This must run BEFORE the tour: AR is enterable from View only
            // (ModeRules.CanEnter), so the same block placed after the tour would log "refused"
            // and read as a broken feature — the lesson the gizmo QC taught the hard way.
            //
            // What CI can honestly check here is the half that does not need hardware: the mode
            // switch, the overlay, where the viewer is put, and — the part most likely to rot —
            // that leaving hands the scene back EXACTLY as it was found. The feed and the gyro
            // need a phone, and the log says so rather than implying a pass.
            {
                Camera arCam = Camera.main;
                Vector3 poseBefore = arCam != null ? arCam.transform.position : Vector3.zero;
                Quaternion rotBefore = arCam != null ? arCam.transform.rotation : Quaternion.identity;
                float nearBefore = arCam != null ? arCam.nearClipPlane : 0f;
                float farBefore = arCam != null ? arCam.farClipPlane : 0f;
                bool fogBefore = RenderSettings.fog;
                // Check ALL FOUR underwater parts, not just the sand. The first AR run logged
                // seabedHidden=True and the screenshot still showed a glowing white floor: the
                // caustic sheet is a SIBLING of Seabed, so hiding Seabed left it drawing.
                string[] arParts = { "Seabed", "Caustics", "Water", "GodRays" };
                // Same root ArSession uses. Checking a DIFFERENT root than the one being hidden
                // is how the log came back "3/3 hidden" over a seabed that was plainly on screen.
                Transform arMap = ArSession.MapRoot != null ? ArSession.MapRoot.transform
                                : GameObject.Find("Map") != null ? GameObject.Find("Map").transform : null;
                var arWas = new System.Collections.Generic.List<bool>();
                foreach (string n in arParts)
                {
                    Transform t = arMap != null ? arMap.Find(n) : null;
                    arWas.Add(t != null && t.gameObject.activeSelf);
                }

                bool entered = ArSession.Start();
                yield return new WaitForSecondsRealtime(1.5f);

                double fit = ArSession.FitScale;
                double apparent = DiveMap.Core.ArPlacement.ApparentSpan(1f, fit);
                Debug.Log($"[QC] ar entered={entered} mode={ModeManager.Current} " +
                          $"controls={ArControls.IsOpen} chrome={ModeRules.AllowsMenu(ModeManager.Current)} " +
                          $"fit={fit:F5} (1 world unit reads as {apparent * 100:F2} cm) " +
                          $"underwaterHidden={ArPartsHidden(arMap, arParts)}/{ArPartsPresent(arMap, arParts)} " +
                          $"(of {arParts.Length} looked for; see [AR] for any this map lacks) " +
                          $"fog={RenderSettings.fog} compass={(CompassWidget.Instance == null || !CompassWidget.Instance.IsVisible)}");
                Debug.Log("[QC] ar NOT COVERED HERE: camera feed and gyroscope — headless has " +
                          "neither. Both need a device run; see docs/WO-AR-HOLOMAP.md.");

                // How many map roots exist. Anything but 1 means a cancelled build was orphaned
                // and a ghost map is drawing behind the real one — invisible in every other mode
                // (it lines up exactly with the real map) and only obvious once AR hides the sea.
                int roots = 0;
                foreach (GameObject go in FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                    if (go.name == "Map" && go.transform.parent == null) roots++;
                Debug.Log($"[QC] ar map roots={roots} (expected 1 — more means a leaked build)");

                // J7 — the models kept for a dive with no signal. Hits should be 0 on a cold CI
                // run and non-zero on the second map, which is the whole claim being made.
                // `files=` matters: the counters are per-process, so a second launch legitimately
                // reports stored=0 while every model came off the disk. Without the on-disk count
                // that line reads like the cache did nothing.
                Debug.Log($"[QC] offline assets files={AssetCacheStore.Entries.Count} " +
                          $"stored={AssetCacheStore.Stored} " +
                          $"hits={AssetCacheStore.Hits} misses={AssetCacheStore.Misses} " +
                          $"evicted={AssetCacheStore.Evicted} size={AssetCacheStore.TotalLabel} " +
                          $"(cap {DiveMap.Core.AssetCache.FormatSize(DiveMap.Core.AssetCache.BudgetBytes)})");
                ScreenCapture.CaptureScreenshot(prefix + "_ar.png");
                Debug.Log("[UI] qcui shot -> " + prefix + "_ar.png");
                yield return new WaitForSecondsRealtime(1.2f);

                // − and + must move the viewer and must stop rather than run away.
                double s0 = ArSession.Scale;
                if (ArSession.Instance != null) ArSession.Instance.Zoom(true);
                yield return new WaitForSecondsRealtime(0.5f);
                double s1 = ArSession.Scale;
                for (int i = 0; i < 40; i++) ArSession.Instance?.Zoom(false);
                yield return new WaitForSecondsRealtime(0.5f);
                double s2 = ArSession.Scale;
                Debug.Log($"[QC] ar zoom {s0:F5} → {s1:F5} (expected ×1.22 = {s0 * 1.22:F5}) " +
                          $"→ 40 presses of − floors at {s2:F5} " +
                          $"(expected {fit * DiveMap.Core.ArPlacement.MinZoom:F5}, i.e. it cannot be lost)");

                if (ModeManager.Instance != null) ModeManager.Instance.Exit();
                yield return new WaitForSecondsRealtime(1.2f);

                // The restore check. A mode that quietly keeps the fog off, or the seabed hidden,
                // or the near plane at 2 units, poisons every screen after it — and would show up
                // as "the map looks wrong after AR" long after anyone connects the two.
                bool posOk = arCam != null && (arCam.transform.position - poseBefore).magnitude < 0.01f;
                bool rotOk = arCam != null && Quaternion.Angle(arCam.transform.rotation, rotBefore) < 0.1f;
                bool clipOk = arCam != null && Mathf.Approximately(arCam.nearClipPlane, nearBefore) &&
                              Mathf.Approximately(arCam.farClipPlane, farBefore);
                Debug.Log($"[QC] ar restored mode={ModeManager.Current} pos={posOk} rot={rotOk} " +
                          $"clip={clipOk} fog={(RenderSettings.fog == fogBefore)} " +
                          $"underwater={ArPartsMatch(arMap, arParts, arWas)}/{arParts.Length} " +
                          $"controlsGone={!ArControls.IsOpen}");
                ScreenCapture.CaptureScreenshot(prefix + "_ar_restored.png");
                Debug.Log("[UI] qcui shot -> " + prefix + "_ar_restored.png");
                yield return new WaitForSecondsRealtime(0.8f);
            }

            // shell chrome, which no unit test can show.
            if (TourController.Start())
            {
                yield return new WaitForSecondsRealtime(1.2f);
                // D10 auto-starts a spotlight guide on a first dive. Every other tour shot needs
                // it gone, so it is dismissed here and photographed deliberately further down.
                TutorialGuide.CloseAny();
                ScreenCapture.CaptureScreenshot(prefix + "_tour.png");
                Debug.Log("[UI] qcui shot -> " + prefix + "_tour.png");

                // Stay long enough for the litter to fall into view (28 u/s from just under the
                // surface): the first game shot was taken 2 s in and caught an empty ocean.
                yield return new WaitForSecondsRealtime(6f);
                ScreenCapture.CaptureScreenshot(prefix + "_game.png");
                Debug.Log("[UI] qcui shot -> " + prefix + "_game.png");
                yield return new WaitForSecondsRealtime(1.2f);

                // 6.8) C5 — charge the scad shoal. Fear only exists above 11 u/s
                // (FleeMath.DiverPanicSpeed), so a parked drone proves nothing: this is the only
                // way the QC eye can tell "the fish scatter" from "the fish are indifferent".
                var reef = FindFirstObjectByType<Marine.FishSchoolSystem>();
                TourController tour = TourController.Active;
                if (reef != null && tour != null &&
                    reef.TryGetNearestSchool(Camera.main != null ? Camera.main.transform.position : Vector3.zero,
                                             "scad", out Vector3 shoal, out float shoalR))
                {
                    Debug.Log($"[UI] qcui charging the scad shoal at ({shoal.x:F0},{shoal.y:F0},{shoal.z:F0}) R={shoalR:F0}");
                    // Start WELL INSIDE the panic radius (0.3×, i.e. panic ≈ 0.7 → the shoal balls
                    // up). Starting just outside it looked fairer but was not a test: at CI speed
                    // the drone closed one unit per second and reached 92 of a 93-unit radius by
                    // the time the shutter went — panic 0.01, and a picture proving nothing. The
                    // speed rule is still honoured; the log shows the drone passing 11 u/s.
                    tour.QcPlaceNear(shoal, (float)DiveMap.Core.FleeMath.DiverPanicRadius(shoalR, 4.2) * 0.3f);
                    yield return new WaitForSecondsRealtime(0.3f);
                    tour.QcChargeToward(shoal);
                    // Wait in FRAMES, not seconds. The drone accelerates by a fixed 9 % per FRAME
                    // (DroneFlight.Inertia is deliberately not dt-scaled — it is the web's rule),
                    // so on a 3 fps runner five seconds of wall clock is fifteen frames and the
                    // drone never reaches the 11 u/s that frightens anything. Sixty frames gets it
                    // to ~99 % of cruise on any machine; on a phone that is one second.
                    for (int f = 0; f < 60; f++) yield return null;
                    ScreenCapture.CaptureScreenshot(prefix + "_flee.png");
                    Debug.Log("[UI] qcui shot -> " + prefix + "_flee.png");
                    yield return new WaitForSecondsRealtime(1.2f);
                    tour.QcStopCharge();
                }
                else Debug.LogWarning("[UI] qcui could not find a scad shoal to charge");

                // 6.9) E5 — the PALETTE, which is the shop a player actually meets (placing is
                // buying). Thumbnails come off the CDN, so wait for a few before the shot or the
                // grid photographs as a wall of fallback glyphs.
                PaletteSheet.Open(_thumbs);
                float tp = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - tp < 8f)
                {
                    if (PaletteSheet.Current != null && _thumbs != null &&
                        _thumbs.LoadedCount >= 8) break;
                    yield return new WaitForSecondsRealtime(0.25f);
                }
                PaletteSheet pal = PaletteSheet.Current;
                Debug.Log($"[UI] qcui palette open={(pal != null)} chips={(pal != null ? pal.ChipCount : -1)} " +
                          $"cards={(pal != null ? pal.CardCount : -1)} kind={(pal != null ? pal.CurrentKind : null)} " +
                          $"thumbs={(_thumbs != null ? _thumbs.LoadedCount : -1)}");
                ScreenCapture.CaptureScreenshot(prefix + "_palette.png");
                Debug.Log("[UI] qcui shot -> " + prefix + "_palette.png");
                yield return new WaitForSecondsRealtime(1.2f);

                // …and the paid tab, where the 🪙 price badges live.
                if (pal != null)
                {
                    pal.QcShowKind(DiveMap.Core.Palette.MarineLife);
                    yield return new WaitForSecondsRealtime(2.5f);
                    Debug.Log($"[UI] qcui palette animals cards={pal.CardCount} " +
                              $"thumbs={(_thumbs != null ? _thumbs.LoadedCount : -1)}");
                    ScreenCapture.CaptureScreenshot(prefix + "_palette_buy.png");
                    Debug.Log("[UI] qcui shot -> " + prefix + "_palette_buy.png");
                    yield return new WaitForSecondsRealtime(1.2f);
                }
                PaletteSheet.Close();
                yield return new WaitForSecondsRealtime(0.4f);

                // The older openShop() list is still reachable and still has to work.
                ShopSheet.Open();
                yield return new WaitForSecondsRealtime(0.8f);
                ScreenCapture.CaptureScreenshot(prefix + "_shop.png");
                Debug.Log("[UI] qcui shot -> " + prefix + "_shop.png");
                yield return new WaitForSecondsRealtime(1.2f);
                ShopSheet.Close();
                yield return new WaitForSecondsRealtime(0.4f);

            // 6.95) D10 — the first-dive spotlight. Forced, because the automatic path marks
                // itself seen on the first CI player run and would never appear in the second.
                TutorialGuide.Forget(TutorialGuide.TourKey);
                bool tut = TutorialGuide.StartTour(force: true);
                Debug.Log($"[UI] qcui tutorial started={tut}");
                yield return new WaitForSecondsRealtime(1.0f);
                ScreenCapture.CaptureScreenshot(prefix + "_tutorial.png");
                Debug.Log("[UI] qcui shot -> " + prefix + "_tutorial.png");
                yield return new WaitForSecondsRealtime(1.2f);
                TutorialGuide.CloseAny();
                yield return new WaitForSecondsRealtime(0.4f);
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
            yield return new WaitForSecondsRealtime(1f);

            // 8) E5 — actually BUY something. This is the one path in the shop that had never run:
            // spend → write the stock → reload the map → the animal is rebuilt by the normal item
            // pipeline. It is also the path where a bug costs the player real coins, so it is worth
            // a QC round of its own. Runs LAST because it reloads the map underneath everything.
            {
                AppBoot boot = FindFirstObjectByType<AppBoot>();
                string mapId = boot != null ? boot.CurrentMapId : "";
                int before = TrashGameSystem.Coins;
                string want = DiveMap.Core.Shop.Catalogue[0];          // cheapest, always affordable
                int price = DiveMap.Core.Shop.PriceOf(want);
                int stockBefore = DiveMap.Core.ShopStock.Load(mapId).Count;

                Debug.Log($"[QC] buy test map={mapId} item={want} price={price} " +
                          $"coins={before} stock={stockBefore}");

                // Buy through the PALETTE — that is the door a player uses, so that is the door
                // the money path has to be proven through. Pressing the card rather than calling
                // the method keeps the button wiring inside the test.
                PaletteSheet.Open(_thumbs);
                yield return new WaitForSecondsRealtime(0.6f);
                PaletteSheet buyPal = PaletteSheet.Current;
                Button buyCard = null;
                if (buyPal != null)
                {
                    buyPal.QcShowKind(DiveMap.Core.Palette.MarineLife);
                    yield return new WaitForSecondsRealtime(0.4f);
                    buyCard = buyPal.QcCard(want);
                    if (buyCard == null)   // the cheapest animal may live under SCHOOL
                    {
                        buyPal.QcShowKind(DiveMap.Core.Palette.School);
                        yield return new WaitForSecondsRealtime(0.4f);
                        buyCard = buyPal.QcCard(want);
                    }
                }
                if (buyCard != null) buyCard.onClick.Invoke();
                else Debug.LogWarning("[QC] buy test — could not find the palette card to press");

                yield return new WaitForSecondsRealtime(2f);
                int after = TrashGameSystem.Coins;
                int stockAfter = DiveMap.Core.ShopStock.Load(mapId).Count;
                Debug.Log($"[QC] buy result coins {before}→{after} (expected {before - price}) · " +
                          $"stock {stockBefore}→{stockAfter} (expected {stockBefore + 1})");

                // Give the reload time to rebuild, then photograph the map with the purchase in it.
                yield return new WaitForSecondsRealtime(8f);
                ScreenCapture.CaptureScreenshot(prefix + "_bought.png");
                Debug.Log("[UI] qcui shot -> " + prefix + "_bought.png");
                yield return new WaitForSecondsRealtime(1.5f);

                // Put the player back where they started: a QC run must not leave a bought animal
                // and a spent balance behind for the next one to trip over.
                TrashGameSystem.Coins = before;
                PlayerPrefs.DeleteKey(DiveMap.Core.ShopStock.KeyFor(mapId));
                PlayerPrefs.Save();
                Debug.Log("[QC] buy test cleaned up");
            }

            yield return new WaitForSecondsRealtime(1f);
            Debug.Log("[UI] qcui done");
            Application.Quit(0);
        }

        /// <summary>QC helper — distance between two items, so a group scale can be measured.</summary>
        private static double Gap(AppBoot boot, string a, string b)
        {
            Newtonsoft.Json.Linq.JArray items = DiveMap.Core.SceneEdit.Items(boot.CurrentScene);
            Newtonsoft.Json.Linq.JObject oa = DiveMap.Core.SceneEdit.Find(items, a);
            Newtonsoft.Json.Linq.JObject ob = DiveMap.Core.SceneEdit.Find(items, b);
            if (oa == null || ob == null) return -1;
            double dx = (double)oa["p"][0] - (double)ob["p"][0];
            double dy = (double)oa["p"][1] - (double)ob["p"][1];
            double dz = (double)oa["p"][2] - (double)ob["p"][2];
            return System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>QC helper — the tallest sculpt sample, so a stroke can be measured.</summary>
        private static float PeakHeight()
        {
            float[] h = SeabedSculptor.Heights;
            if (h == null) return 0f;
            float peak = 0f;
            for (int i = 0; i < h.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(h[i]));
            return peak;
        }

        private static Transform FindDeep(Transform where, string name)
        {
            if (where == null) return null;
            for (int i = 0; i < where.childCount; i++)
            {
                Transform c = where.GetChild(i);
                if (c.name == name) return c;
                Transform hit = FindDeep(c, name);
                if (hit != null) return hit;
            }
            return null;
        }
    }
}
