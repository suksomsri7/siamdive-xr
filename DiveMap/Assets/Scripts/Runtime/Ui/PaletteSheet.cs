using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DiveMap.Core;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// The palette — which on this product IS the shop the player actually meets, because
    /// **placing an object is the purchase** (see <see cref="PlaceItem"/>). The reference is
    /// <c>docs/refs/web-palette.png</c>, drawn from builder.html's own CSS:
    /// <code>
    ///   #sheet          radius 24 24 0 0 · max-height 72vh · padding 6/16/20 · grip 42×4 white 28%
    ///   #cats button    min-width 64 · padding 9/6 · radius 15 · bg white 6% · border --line · 11 px
    ///                   .on → bg rgba(57,176,232,.22) + border --accent      · icon 23 px
    ///   #variants       grid 3 (portrait) / 4 (landscape) · gap 8 · margin-top 14
    ///   #variants button padding 13/4 · radius 15 · gap 5 · 11 px · thumbnail img 48×48
    ///   price badge     #ffd54a · 11 px bold · margin-top 2
    ///   coinUI          centred pill, black 60%, gold 14 px bold, radius 9, gold disc 13 px
    /// </code>
    ///
    /// Chips: the kinds that have items, then 📍 Pin · ⚙️ Settings · ⛰️ Sculpt floor. The 🌀 Warp
    /// chip is admin-only on the web (<c>PALETTE.SPECIAL = _isAdmin ? … : []</c>) and is
    /// admin-only here, so a signed-in admin sees eleven chips and everyone else ten — which is
    /// exactly what the user's screenshots show for each.
    ///
    /// Thumbnails are the server's pre-rendered PNGs (<see cref="Palette.ThumbUrl"/>), fetched
    /// through the same <see cref="ThumbnailCache"/> as the map hub.
    ///
    /// WO-L — THIS SHEET IS THE EDIT MODE. Opening it enters <see cref="AppMode.Edit"/> and
    /// closing it leaves; there is no separate builder chrome, because on the web there is none
    /// either. The sheet sits over the live scene, the map stays orbitable behind it
    /// (<c>ModeRules.AllowsOrbit(Edit)</c> is true), and the ▶ button decides whether the animals
    /// are swimming or holding still to be arranged. Before this WO the whole class was
    /// unreachable: <c>AppMode.Edit</c> was never requested by anything, and the only callers of
    /// <see cref="Open"/> were inside the CI screenshot harness.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PaletteSheet : MonoBehaviour
    {
        private static PaletteSheet _open;

        // sheet chrome (builder.html CSS, in CSS px)
        private const float SheetRadius = 24f;
        private const float SidePad = 16f;
        private const float ChipGap = 8f;
        private const float ChipMinW = 64f;
        private const float ChipH = 62f;      // icon 23 + gap 4 + label 11 + padding 9×2
        private const float GridGap = 8f;
        private const float GridTop = 14f;
        private const float ThumbSize = 48f;

        private static readonly Color SheetBg = new Color(0.043f, 0.102f, 0.165f, 0.97f);
        private static readonly Color ChipBg = new Color(1f, 1f, 1f, 0.06f);
        private static readonly Color ChipOnBg = new Color(0.224f, 0.690f, 0.910f, 0.22f);
        private static readonly Color Gold = new Color(1f, 0.835f, 0.290f, 1f);   // #ffd54a
        private static readonly Color PinChipBg = new Color(1f, 0.690f, 0.361f, 0.12f); // #cats button.pin
        /// <summary>#playBtn.on — the web's teal gradient, flattened to its midpoint.</summary>
        private static readonly Color PlayOnBg = new Color(0.070f, 0.596f, 0.600f, 0.96f);

        private RectTransform _root;
        private RectTransform _chipRow;
        private ScrollRect _chipScroll;
        private RectTransform _grid;
        private ScrollRect _gridScroll;
        private Text _coinLabel;
        private Button _play;
        private Image _playBg;
        private Color _playIdleBg;
        private GameObject _coinPill;
        /// <summary>Was the app already in Edit when this opened? Then leaving must not exit it.</summary>
        private bool _enteredEdit;

        private Dictionary<string, List<PaletteItem>> _byKind;
        private readonly List<string> _chipKinds = new List<string>();
        private readonly Dictionary<string, Image> _chipBg = new Dictionary<string, Image>();
        private readonly List<GameObject> _cards = new List<GameObject>();
        private ThumbnailCache _thumbs;
        private string _kind;

        // ── QC surface ───────────────────────────────────────────────────────────
        public static bool IsOpen => _open != null;
        public static PaletteSheet Current => _open;
        /// <summary>Chips drawn, including the three tool chips.</summary>
        public int ChipCount => _chipBg.Count;
        /// <summary>QC — is the ▶ toggle currently letting the animals swim?</summary>
        public static bool Playing => ModeRules.EditPlayback;
        /// <summary>Cards in the grid right now.</summary>
        public int CardCount => _cards.Count;
        public string CurrentKind => _kind;
        public int KindCount(string kind) =>
            _byKind != null && _byKind.TryGetValue(kind, out List<PaletteItem> l) ? l.Count : 0;

        /// <summary>QC only — switch tabs without a synthetic touch event.</summary>
        public void QcShowKind(string kind) => ShowKind(kind);

        /// <summary>QC only — flip ▶ ↔ ❚❚ without a synthetic tap.</summary>
        public void QcTogglePlay() => TogglePlay();

        /// <summary>
        /// QC only — drive the chip strip to its right-hand end. Returns false when there is
        /// nothing to scroll, which is itself the answer the CI shot is looking for: the strip
        /// only overflows on a narrow screen, and a run that could not scroll proves the row
        /// fitted rather than that the ScrollRect works.
        /// </summary>
        public bool QcScrollChipsToEnd()
        {
            if (_chipScroll == null || _chipRow == null) return false;
            RectTransform vp = _chipScroll.GetComponent<RectTransform>();
            if (vp == null || _chipRow.sizeDelta.x <= vp.rect.width) return false;
            _chipScroll.horizontalNormalizedPosition = 1f;
            return true;
        }

        /// <summary>QC only — the card button for one asset id, so the buy test presses real UI.</summary>
        public Button QcCard(string assetId)
        {
            for (int i = 0; i < _cards.Count; i++)
            {
                if (_cards[i] == null || _cards[i].name != "Item_" + assetId) continue;
                return _cards[i].GetComponent<Button>();
            }
            return null;
        }

        // ── open / close ─────────────────────────────────────────────────────────

        public static void Open(ThumbnailCache thumbs)
        {
            if (_open != null) { _open.Refresh(); return; }

            // 🔴 Enter Edit BEFORE asking for a layer. HudLayer shows exactly one mode's layer at
            // a time (HudLayer.SetActiveMode), so a sheet parented to Hud_View and then switched
            // to Edit would be built into a container that is immediately deactivated — a palette
            // that "opened" and drew nothing, with no error anywhere.
            bool entered = false;
            if (ModeManager.Instance != null && ModeManager.Current != AppMode.Edit)
                entered = ModeManager.Instance.Request(AppMode.Edit);

            RectTransform layer = HudLayer.For(ModeManager.Current) ?? HudLayer.For(AppMode.Tour);
            if (layer == null)
            {
                if (entered && ModeManager.Instance != null) ModeManager.Instance.Exit();
                return;
            }

            // The tour HUD does not exist in the web's builder. Keep it up and you get two coin
            // badges and a compass sticking out of the sheet.
            TourHud.SetChromeVisible(false);

            RectTransform root = UiKit.MakeNode(layer, "PaletteSheet");
            UiKit.Stretch(root);
            var sheet = root.gameObject.AddComponent<PaletteSheet>();
            sheet._thumbs = thumbs;
            sheet._enteredEdit = entered;
            sheet.Build(root);
            _open = sheet;
        }

        public static void Close()
        {
            if (_open == null) return;
            bool leaveEdit = _open._enteredEdit;
            Destroy(_open.gameObject);
            _open = null;
            TourHud.SetChromeVisible(true);
            // Only undo what this sheet did. A caller that was already in Edit (a tool sheet
            // reopening the palette) keeps its mode; anything else would drop the author out of
            // the builder every time they closed a panel.
            if (leaveEdit && ModeManager.Instance != null && ModeManager.Current == AppMode.Edit)
                ModeManager.Instance.Exit();
        }

        private void OnDestroy()
        {
            if (_open != this) return;
            _open = null;
            // Destroyed without going through Close() (a map reload tears the layer down) —
            // the HUD must still come back, or the player lands in a tour with no controls.
            TourHud.SetChromeVisible(true);
            // 🔴 WO-N: …and so must the MODE. Without this the app is left in AppMode.Edit with
            // no palette on screen and no way to leave it, which before AllowsEditTools meant
            // every editing tool was dead until some other mode change happened to occur.
            if (_enteredEdit && ModeManager.Instance != null && ModeManager.Current == AppMode.Edit)
                ModeManager.Instance.Exit();
        }

        // ── build ────────────────────────────────────────────────────────────────

        private void Build(RectTransform root)
        {
            _root = root;
            _byKind = BuildCatalogue();
            _chipKinds.Clear();
            _chipKinds.AddRange(Palette.ChipKinds(_byKind));

            Button scrim = UiKit.MakeButton(root, "Scrim", null, 0, new Color(0f, 0f, 0f, 0.42f),
                                            Color.white, Close);
            UiKit.Stretch(scrim.GetComponent<RectTransform>());

            BuildHeader(root);

            // The sheet: docked bottom, full width, 72 vh (web max-height:72vh), rounded top.
            Image panel = UiKit.MakeRounded(root, "Sheet", SheetBg, SheetRadius);
            RectTransform prt = panel.rectTransform;
            prt.anchorMin = new Vector2(0f, 0f);
            prt.anchorMax = new Vector2(1f, 0f);
            prt.pivot = new Vector2(0.5f, 0f);
            prt.sizeDelta = new Vector2(0f, Screen.height / UiKit.CanvasScale * 0.72f);
            prt.anchoredPosition = Vector2.zero;

            // Grip: 42×4, radius 3, white 28% (builder.html:128) — and it closes the sheet,
            // which is what the grip does on the web.
            Button grip = UiKit.MakeButton(prt, "Grip", null, 0, new Color(1f, 1f, 1f, 0.28f),
                                           Color.white, Close);
            Image gimg = grip.GetComponent<Image>();
            if (gimg != null) { gimg.sprite = UiKit.RoundedSprite(3f); gimg.type = Image.Type.Sliced; }
            RectTransform grt = grip.GetComponent<RectTransform>();
            grt.anchorMin = new Vector2(0.5f, 1f);
            grt.anchorMax = new Vector2(0.5f, 1f);
            grt.pivot = new Vector2(0.5f, 1f);
            grt.sizeDelta = new Vector2(UiKit.Css(42f), UiKit.Css(4f));
            grt.anchoredPosition = new Vector2(0f, -UiKit.Css(10f));

            BuildChips(prt);
            BuildGrid(prt);

            // First chip open, like the web's firstKind().
            ShowKind(_chipKinds.Count > 0 ? _chipKinds[0] : null);

            Refresh();   // ∞ for the admin, and the pill hidden when there is no signal

            int total = 0;
            foreach (KeyValuePair<string, List<PaletteItem>> kv in _byKind) total += kv.Value.Count;
            Debug.Log($"[Palette] open kinds={_chipKinds.Count} chips={ChipCount} items={total} " +
                      $"coins={TrashGameSystem.Coins} admin={Account.IsAdmin} " +
                      $"live={AssetCatalogClient.Count} first={_kind}");
        }

        /// <summary>‹ back (left) · 🪙 coins (centre) · ▶ play (right) — above the sheet.</summary>
        private void BuildHeader(RectTransform root)
        {
            float size = UiKit.Css(48f);
            float pad = UiKit.Css(14f);

            Button back = UiKit.MakeIconButton(root, "Back", "back", Close, false, size);
            RectTransform brt = back.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0f, 1f);
            brt.anchorMax = new Vector2(0f, 1f);
            brt.pivot = new Vector2(0f, 1f);
            brt.anchoredPosition = new Vector2(pad, -pad);

            // ▶ / ❚❚ — the web's playMode toggle (builder.html:292, :3903), NOT a way into the
            // tour. It decides whether the animals this author is arranging are swimming or
            // holding still; the icon swaps to a pause bar and the button turns teal while they
            // are moving (`#playBtn.on`, CSS :55). Entering the tour stays where it has always
            // been, on the 🤿 action in the menu column — a builder who wanted to fly the drone
            // and got thrown out of the palette instead was the round-4 complaint.
            Button play = UiKit.MakeIconButton(root, "Play", "play", null, false, size);
            _play = play;
            _playBg = play.GetComponent<Image>();
            if (_playBg != null) _playIdleBg = _playBg.color;
            play.onClick.AddListener(TogglePlay);
            RectTransform yrt = play.GetComponent<RectTransform>();
            yrt.anchorMin = new Vector2(1f, 1f);
            yrt.anchorMax = new Vector2(1f, 1f);
            yrt.pivot = new Vector2(1f, 1f);
            yrt.anchoredPosition = new Vector2(-pad, -pad);

            // coinUI: black 60% pill, gold disc + bold gold number.
            Image pill = UiKit.MakeRounded(root, "Coins", new Color(0f, 0f, 0f, 0.6f), 9f);
            pill.raycastTarget = false;
            _coinPill = pill.gameObject;
            RectTransform crt = pill.rectTransform;
            crt.anchorMin = new Vector2(0.5f, 1f);
            crt.anchorMax = new Vector2(0.5f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.sizeDelta = new Vector2(UiKit.Css(104f), UiKit.Css(30f));
            crt.anchoredPosition = new Vector2(0f, -UiKit.Css(16f));

            Image disc = UiKit.MakeCircle(pill.transform, "Disc", new Color(1f, 0.824f, 0.227f, 1f));
            disc.raycastTarget = false;
            RectTransform drt = disc.rectTransform;
            drt.anchorMin = new Vector2(0f, 0.5f);
            drt.anchorMax = new Vector2(0f, 0.5f);
            drt.pivot = new Vector2(0f, 0.5f);
            drt.sizeDelta = new Vector2(UiKit.Css(13f), UiKit.Css(13f));
            drt.anchoredPosition = new Vector2(UiKit.Css(11f), 0f);

            _coinLabel = UiKit.MakeLine(pill.transform, "Value",
                                        Palette.CoinLabel(TrashGameSystem.Coins, Account.IsAdmin),
                                        UiKit.CssFont(14f), TextAnchor.MiddleLeft, Gold);
            _coinLabel.fontStyle = FontStyle.Bold;
            RectTransform vrt = _coinLabel.rectTransform;
            UiKit.Stretch(vrt);
            vrt.offsetMin = new Vector2(UiKit.Css(11f + 13f + 5f), 0f);
            vrt.offsetMax = new Vector2(-UiKit.Css(10f), 0f);

            ApplyPlayVisual();
        }

        /// <summary>
        /// ▶ ↔ ❚❚. The whole toggle is one bool in <see cref="ModeRules"/>, read by the animal
        /// brain; nothing here reaches into the marine code.
        /// </summary>
        private void TogglePlay()
        {
            ModeRules.EditPlayback = !ModeRules.EditPlayback;
            ApplyPlayVisual();
            Debug.Log($"[Palette] playMode={ModeRules.EditPlayback}");
        }

        private void ApplyPlayVisual()
        {
            bool on = ModeRules.EditPlayback;
            UiKit.SetIcon(_play, on ? "pause" : "play");
            if (_playBg != null) _playBg.color = on ? PlayOnBg : _playIdleBg;
        }

        private void BuildChips(RectTransform sheet)
        {
            // 🔴 A REAL horizontally scrollable strip — the web's #cats is overflow-x:auto
            // (builder.html:132). This used to be a bare RectTransform with a comment claiming it
            // scrolled: eleven chips are 776 css px wide, a portrait phone is about 400, and
            // everything past the fifth was laid out beyond the right edge where no finger could
            // reach it. See UiKit.MakeScrollH.
            _chipScroll = UiKit.MakeScrollH(sheet, "Cats", out _chipRow);
            RectTransform vp = _chipScroll.GetComponent<RectTransform>();
            vp.anchorMin = new Vector2(0f, 1f);
            vp.anchorMax = new Vector2(1f, 1f);
            vp.pivot = new Vector2(0.5f, 1f);
            vp.offsetMin = new Vector2(UiKit.Css(SidePad), 0f);
            vp.offsetMax = new Vector2(-UiKit.Css(SidePad), 0f);
            vp.sizeDelta = new Vector2(vp.sizeDelta.x, UiKit.Css(ChipH));
            vp.anchoredPosition = new Vector2(0f, -UiKit.Css(26f));

            float x = 0f, w = UiKit.Css(ChipMinW), gap = UiKit.Css(ChipGap);

            for (int i = 0; i < _chipKinds.Count; i++)
            {
                string kind = _chipKinds[i];
                AddChip(kind, Palette.IconOf(kind), UiStrings.Tr(Palette.LabelOf(kind)), ChipBg,
                        x, w, () => ShowKind(kind));
                x += w + gap;
            }

            // The three tool chips the web appends after the kinds (buildCats :1408-1416). Each
            // one now opens the panel that already existed behind the ☰ column — the sheets were
            // written long ago, they were simply never reachable from the place the user's
            // screenshot puts them. All three write to the map, so all three ask the same
            // question the ☰ buttons ask, at TAP time, because the palette is built before the
            // server has answered whether this account may edit this site.
            AddChip(Palette.PinTool, Palette.ToolIcon(Palette.PinTool),
                    UiStrings.Tr(Palette.ToolLabel(Palette.PinTool)), PinChipBg, x, w,
                    () => WithEditRights(() => { Close(); PinPlacer.Start(); }));
            x += w + gap;

            AddChip(Palette.SettingsTool, Palette.ToolIcon(Palette.SettingsTool),
                    UiStrings.Tr(Palette.ToolLabel(Palette.SettingsTool)), ChipBg, x, w,
                    () => WithEditRights(() => { Close(); MapSettingsSheet.Open(); }));
            x += w + gap;

            AddChip(Palette.SculptTool, Palette.ToolIcon(Palette.SculptTool),
                    UiStrings.Tr(Palette.ToolLabel(Palette.SculptTool)), ChipBg, x, w,
                    () => WithEditRights(() => { Close(); SculptSheet.Open(); }));
            x += w + gap;

            // Content width, or the strip refuses to scroll: a ScrollRect with content no wider
            // than its viewport treats every drag as an over-scroll and springs straight back.
            _chipRow.sizeDelta = new Vector2(Mathf.Max(0f, x - gap), 0f);
        }

        /// <summary>
        /// The gate every editing tool shares. Deliberately identical in wording and timing to
        /// the ☰ column's (UiShell.BuildActions) — two different refusals for the same reason is
        /// how a user learns to distrust both.
        /// </summary>
        private static void WithEditRights(System.Action run)
        {
            var boot = FindFirstObjectByType<AppBoot>();
            if (boot == null || !boot.CanEditCurrent) { Toast.ShowTr("แมพนี้แก้ไม่ได้"); return; }
            run();
        }

        private void AddChip(string key, string icon, string label, Color bg, float x, float w,
                             UnityEngine.Events.UnityAction onClick)
        {
            Image chip = UiKit.MakeRounded(_chipRow, "Chip_" + key, bg, 15f);
            var btn = chip.gameObject.AddComponent<Button>();
            btn.targetGraphic = chip;
            btn.onClick.AddListener(onClick);

            RectTransform rt = chip.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(w, 0f);
            rt.anchoredPosition = new Vector2(x, 0f);

            Image rim = UiKit.MakePanel(chip.transform, "Rim", UiKit.Line);
            rim.sprite = UiKit.RoundedSprite(15f, 1f);
            rim.type = Image.Type.Sliced;
            rim.raycastTarget = false;
            UiKit.Stretch(rim.rectTransform);

            Image glyph = UiKit.MakePanel(chip.transform, "Icon", UiKit.TextMain);
            glyph.sprite = IconPainter.Get(icon);
            glyph.raycastTarget = false;
            RectTransform grt = glyph.rectTransform;
            grt.anchorMin = new Vector2(0.5f, 1f);
            grt.anchorMax = new Vector2(0.5f, 1f);
            grt.pivot = new Vector2(0.5f, 1f);
            grt.sizeDelta = new Vector2(UiKit.Css(23f), UiKit.Css(23f));
            grt.anchoredPosition = new Vector2(0f, -UiKit.Css(9f));

            int size = UiKit.CssFont(11f);
            Text text = UiKit.MakeText(chip.transform, "Label", label, size,
                                       TextAnchor.UpperCenter, UiKit.TextMain);
            RectTransform trt = text.rectTransform;
            trt.anchorMin = new Vector2(0f, 1f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.sizeDelta = new Vector2(-UiKit.Css(4f), UiKit.RowHeight(size));
            trt.anchoredPosition = new Vector2(0f, -UiKit.Css(9f + 23f + 4f));

            _chipBg[key] = chip;
        }

        private void BuildGrid(RectTransform sheet)
        {
            _gridScroll = UiKit.MakeScroll(sheet, "Variants", out _grid);
            RectTransform srt = _gridScroll.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0f, 0f);
            srt.anchorMax = new Vector2(1f, 1f);
            srt.offsetMin = new Vector2(UiKit.Css(SidePad), UiKit.Css(20f));
            srt.offsetMax = new Vector2(-UiKit.Css(SidePad),
                                        -(UiKit.Css(26f) + UiKit.Css(ChipH) + UiKit.Css(GridTop)));
        }

        // ── grid ─────────────────────────────────────────────────────────────────

        /// <summary>3 cards per row in portrait, 4 in landscape — the web's media query.</summary>
        private static int Columns => Screen.width >= Screen.height ? 4 : 3;

        private void ShowKind(string kind)
        {
            _kind = kind;
            foreach (KeyValuePair<string, Image> kv in _chipBg)
            {
                if (kv.Value == null) continue;
                bool on = kv.Key == kind;
                kv.Value.color = on ? ChipOnBg
                                    : (kv.Key == Palette.PinTool ? PinChipBg : ChipBg);
                Transform rim = kv.Value.transform.Find("Rim");
                Image rimImg = rim != null ? rim.GetComponent<Image>() : null;
                if (rimImg != null) rimImg.color = on ? UiKit.Accent : UiKit.Line;
            }

            for (int i = 0; i < _cards.Count; i++) if (_cards[i] != null) Destroy(_cards[i]);
            _cards.Clear();
            if (_grid != null) { _grid.sizeDelta = Vector2.zero; _grid.anchoredPosition = Vector2.zero; }

            if (kind == null || _byKind == null || !_byKind.TryGetValue(kind, out List<PaletteItem> list))
                return;

            int cols = Columns;
            float gap = UiKit.Css(GridGap);
            float width = (Screen.width / UiKit.CanvasScale - UiKit.Css(SidePad) * 2f - gap * (cols - 1)) / cols;
            float height = UiKit.Css(13f) + UiKit.Css(ThumbSize) + UiKit.Css(5f)
                         + UiKit.RowHeight(UiKit.CssFont(11f)) + UiKit.RowHeight(UiKit.CssFont(11f))
                         + UiKit.Css(13f);

            for (int i = 0; i < list.Count; i++)
                AddCard(list[i], i, cols, width, height, gap);

            int rows = (list.Count + cols - 1) / cols;
            _grid.sizeDelta = new Vector2(0f, rows > 0 ? rows * height + (rows - 1) * gap : 0f);

            Debug.Log($"[Palette] kind={kind} items={list.Count} cols={cols} " +
                      $"buyable={(list.Count > 0 && list[0].Buyable ? "yes" : "no")}");
        }

        private void AddCard(PaletteItem item, int index, int cols, float w, float h, float gap)
        {
            Button btn = UiKit.MakeButton(_grid, "Item_" + item.Id, null, 0, ChipBg, UiKit.TextMain,
                                          () => OnPick(item));
            Image bg = btn.GetComponent<Image>();
            if (bg != null) { bg.sprite = UiKit.RoundedSprite(15f); bg.type = Image.Type.Sliced; }

            RectTransform rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2((index % cols) * (w + gap), -(index / cols) * (h + gap));

            Image rim = UiKit.MakePanel(btn.transform, "Rim", UiKit.Line);
            rim.sprite = UiKit.RoundedSprite(15f, 1f);
            rim.type = Image.Type.Sliced;
            rim.raycastTarget = false;
            UiKit.Stretch(rim.rectTransform);

            // 48×48 pre-rendered thumbnail; the kind's chip icon stands in until it lands (and
            // stays if the module has no model at all).
            Image fallback = UiKit.MakePanel(btn.transform, "Glyph", UiKit.TextDim);
            fallback.sprite = IconPainter.Get(Palette.IconOf(item.Kind));
            fallback.raycastTarget = false;
            RectTransform frt = fallback.rectTransform;
            frt.anchorMin = new Vector2(0.5f, 1f);
            frt.anchorMax = new Vector2(0.5f, 1f);
            frt.pivot = new Vector2(0.5f, 1f);
            frt.sizeDelta = new Vector2(UiKit.Css(ThumbSize), UiKit.Css(ThumbSize));
            frt.anchoredPosition = new Vector2(0f, -UiKit.Css(13f));

            if (_thumbs != null && !string.IsNullOrEmpty(item.ThumbUrl))
            {
                RawImage thumb = UiKit.MakeRaw(btn.transform, "Thumb", new Color(1f, 1f, 1f, 0f));
                RectTransform trt = thumb.rectTransform;
                trt.anchorMin = frt.anchorMin;
                trt.anchorMax = frt.anchorMax;
                trt.pivot = frt.pivot;
                trt.sizeDelta = frt.sizeDelta;
                trt.anchoredPosition = frt.anchoredPosition;
                Image fb = fallback;
                _thumbs.Request(item.ThumbUrl, tex =>
                {
                    if (thumb == null || tex == null) return;
                    thumb.texture = tex;
                    thumb.color = Color.white;
                    if (fb != null) fb.gameObject.SetActive(false);
                });
            }

            int size = UiKit.CssFont(11f);
            Text name = UiKit.MakeText(btn.transform, "Name", UiStrings.Tr(item.Name), size,
                                       TextAnchor.UpperCenter, UiKit.TextMain);
            RectTransform nrt = name.rectTransform;
            nrt.anchorMin = new Vector2(0f, 1f);
            nrt.anchorMax = new Vector2(1f, 1f);
            nrt.pivot = new Vector2(0.5f, 1f);
            nrt.sizeDelta = new Vector2(-UiKit.Css(6f), UiKit.RowHeight(size));
            nrt.anchoredPosition = new Vector2(0f, -UiKit.Css(13f + ThumbSize + 5f));

            if (item.Buyable)
            {
                Text price = UiKit.MakeLine(btn.transform, "Price", item.Price.ToString(), size,
                                            TextAnchor.UpperCenter, Gold);
                price.fontStyle = FontStyle.Bold;
                RectTransform prt = price.rectTransform;
                prt.anchorMin = new Vector2(0f, 1f);
                prt.anchorMax = new Vector2(1f, 1f);
                prt.pivot = new Vector2(0.5f, 1f);
                prt.sizeDelta = new Vector2(-UiKit.Css(6f), UiKit.RowHeight(size));
                prt.anchoredPosition = new Vector2(
                    UiKit.Css(6f), -UiKit.Css(13f + ThumbSize + 5f) - UiKit.RowHeight(size) - UiKit.Css(2f));

                Image coin = UiKit.MakeCircle(btn.transform, "PriceCoin", new Color(1f, 0.824f, 0.227f, 1f));
                coin.raycastTarget = false;
                RectTransform crt = coin.rectTransform;
                crt.anchorMin = new Vector2(0.5f, 1f);
                crt.anchorMax = new Vector2(0.5f, 1f);
                crt.pivot = new Vector2(1f, 1f);
                crt.sizeDelta = new Vector2(UiKit.Css(10f), UiKit.Css(10f));
                crt.anchoredPosition = new Vector2(
                    -UiKit.Css(2f),
                    -UiKit.Css(13f + ThumbSize + 5f) - UiKit.RowHeight(size) - UiKit.Css(2f)
                    - (UiKit.RowHeight(size) - UiKit.Css(10f)) * 0.5f);
            }

            _cards.Add(btn.gameObject);
        }

        /// <summary>Tapping a card = the web's tryPlace: buy if it costs, then drop it in.</summary>
        private void OnPick(PaletteItem item)
        {
            if (PlaceItem.TryPlace(item.Id)) Close();
            else Refresh();
        }

        public void Refresh()
        {
            if (_coinLabel != null)
                _coinLabel.text = Palette.CoinLabel(TrashGameSystem.Coins, Account.IsAdmin);
            // The web hides the pill offline (coinUI :4123, "ยอด local อาจ stale") because the
            // balance is server-authoritative: showing a number that cannot be spent, and may be
            // wrong, is worse than showing nothing. An admin's ∞ is not a balance, so it stays.
            if (_coinPill != null)
                _coinPill.SetActive(Account.IsAdmin ||
                                    Application.internetReachability != NetworkReachability.NotReachable);
        }

        // ── catalogue ────────────────────────────────────────────────────────────

        /// <summary>
        /// Map the asset registry onto the palette: the shipped manifest as the base, anything
        /// the backoffice has served since merged over it by id (WO-L item 8 — see
        /// <see cref="AssetCatalog"/> for why that union currently adds nothing visible, and why
        /// it is still the right shape).
        ///
        /// The 🌀 Warp category is included only for a signed-in admin, matching
        /// <c>PALETTE.SPECIAL = _isAdmin ? … : []</c>.
        /// </summary>
        private static Dictionary<string, List<PaletteItem>> BuildCatalogue()
        {
            AssetManifest manifest = AppBoot.Manifest;
            var shipped = new List<PaletteSource>();
            if (manifest != null)
            {
                foreach (AssetManifest.Module m in manifest.All)
                {
                    shipped.Add(new PaletteSource
                    {
                        Id = m.Id,
                        Kind = m.Kind,
                        Name = m.Name,
                        HasGlb = !string.IsNullOrEmpty(m.GlbUrl) || !string.IsNullOrEmpty(m.XrGlbUrl),
                    });
                }
            }
            else
            {
                Debug.LogWarning("[Palette] no asset manifest yet — the grid will be empty");
            }

            List<PaletteSource> sources = AssetCatalog.Merge(shipped, AssetCatalogClient.Live);
            return Palette.Build(sources, MapApiClient.DefaultBaseUrl, Account.IsAdmin);
        }
    }
}
