using DiveMap.Core;
using UnityEngine;
using UnityEngine.UI;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// The tour HUD, laid out to the web's own CSS (builder.html 231-277) — not "the same controls
    /// somewhere" but the same control in the same corner at the same size, because someone who
    /// dives on the web and then in the app should not have to find their thumbs again:
    ///
    ///   #tourExit   top-LEFT   max(14,safe)     44×44 circle, rgba(7,26,42,.62)
    ///   #tourDepth  top-RIGHT  max(14,safe)     pill, 19px/800 #9fe0ff, rim rgba(120,200,255,.4)
    ///   #tourHud    top-CENTRE max(15,safe)     12px/600 hint, rgba(7,26,42,.42)
    ///   #lightBtn   left 14, top 104            56×56, 2.5px white rim; ON = amber glow
    ///   #radarBtn   left 14, top 174            56×56, toggles the minimap; off = 45 % alpha
    ///   (mute)      left 14, top 244            ours — the web never builds its _muteFloat
    ///   #tourCam    right 14, top 104, gap 14   56×56 (photo; #tourRec cut from v1)
    ///   .stick      bottom 24, left/right 18    138×138, knob 60×60, four 9.5px labels
    ///   #minimap    bottom 16, centred          118×118 circle, 1.5px rgba(120,200,255,.45)
    ///
    /// Every number goes through <see cref="UiKit.Css"/>, so they are CSS pixels on a phone and in
    /// a 1280×720 QC window alike. The previous pass had exit and depth swapped, the lamp on the
    /// wrong rail, sticks at twice the size and no minimap: present, but wrong.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TourHud : MonoBehaviour
    {
        private static readonly Color Chrome    = new Color(0.027f, 0.102f, 0.165f, 0.50f); // rgba(7,26,42,.5)
        private static readonly Color ExitBg    = new Color(0.027f, 0.102f, 0.165f, 0.62f);
        private static readonly Color DepthBg   = new Color(0.024f, 0.086f, 0.149f, 0.66f); // rgba(6,22,38,.66)
        private static readonly Color DepthTxt  = new Color(0.624f, 0.878f, 1f, 1f);        // #9fe0ff
        private static readonly Color DepthRim  = new Color(0.471f, 0.784f, 1f, 0.40f);
        private static readonly Color HintBg    = new Color(0.027f, 0.102f, 0.165f, 0.42f);
        private static readonly Color HintTxt   = new Color(1f, 1f, 1f, 0.78f);
        private static readonly Color Rim       = new Color(1f, 1f, 1f, 0.85f);             // 2.5px rim
        private static readonly Color LampOn    = new Color(1f, 0.839f, 0.353f, 0.32f);     // rgba(255,214,90,.32)
        private static readonly Color LampOnRim = new Color(1f, 0.878f, 0.541f, 1f);        // #ffe08a
        private static readonly Color LampIcon  = new Color(1f, 0.910f, 0.604f, 1f);        // #ffe89a
        private static readonly Color StickBg   = new Color(0.027f, 0.102f, 0.165f, 0.32f);
        private static readonly Color StickRim  = new Color(1f, 1f, 1f, 0.18f);
        private static readonly Color KnobCol   = new Color(0.298f, 0.702f, 0.878f, 1f);    // #86dcff→#1c84b0
        private static readonly Color LabelCol  = new Color(1f, 1f, 1f, 0.62f);
        private static readonly Color MiniRim   = new Color(0.471f, 0.784f, 1f, 0.45f);

        private Text _depth;
        private float _shownDepth = -999f;
        private Button _light;
        private Image _lightIcon;
        private Button _mute;
        private Button _radar;
        private GameObject _minimap;
        private bool _radarOn = true;   // the web starts with the minimap visible in a tour
        private Image _vignette;

        /// <summary>
        /// Hide/show the whole tour HUD. The palette is a full-screen editing surface on the web
        /// — it lives in edit mode, where none of this chrome exists. Leaving the HUD up behind
        /// it put a SECOND coin badge on screen (the palette draws the web's own centred one) and
        /// left the compass, depth read-out and camera button poking out around the sheet.
        /// </summary>
        public static void SetChromeVisible(bool visible)
        {
            RectTransform layer = HudLayer.For(AppMode.Tour);
            TourHud hud = layer != null ? layer.GetComponentInChildren<TourHud>(true) : null;
            if (hud == null) return;
            hud.gameObject.SetActive(visible);
            if (CompassWidget.Instance != null) CompassWidget.Instance.SetVisible(visible);
            if (visible) CoinCounter.Show(TrashGameSystem.Coins); else CoinCounter.Hide();
        }

        public static TourHud Ensure()
        {
            RectTransform layer = HudLayer.For(AppMode.Tour);
            if (layer == null) return null;

            TourHud existing = layer.GetComponentInChildren<TourHud>(true);
            if (existing != null) return existing;

            RectTransform root = UiKit.MakeNode(layer, "TourHud");
            UiKit.Stretch(root);

            var hud = root.gameObject.AddComponent<TourHud>();
            hud.Build(root);
            return hud;
        }

        private void Build(RectTransform root)
        {
            BuildVignette(root);

            // ── sticks: bottom 24, left/right 18, 138 px, knob 60 px ─────────────
            float stick = UiKit.Css(138f);
            float inset = UiKit.Css(18f);
            float bottom = UiKit.Css(24f);
            JoystickWidget.Create(root, "StickL", new Vector2(0f, 0f),
                                  new Vector2(inset + stick * 0.5f, bottom + stick * 0.5f), stick,
                                  v => InputRig.SetLeft(v), StickBg, StickRim, KnobCol, UiKit.Css(60f));
            JoystickWidget.Create(root, "StickR", new Vector2(1f, 0f),
                                  new Vector2(-(inset + stick * 0.5f), bottom + stick * 0.5f), stick,
                                  v => InputRig.SetRight(v), StickBg, StickRim, KnobCol, UiKit.Css(60f));
            StickLabels(root, "StickL", "ขึ้น", "ลง", "◀ หัน", "หัน ▶");
            StickLabels(root, "StickR", "หน้า", "ถอย", "◀ สไลด์", "สไลด์ ▶");

            // ── exit: TOP-LEFT, 44 px ───────────────────────────────────────────
            RoundButton(root, "TourExit", "close", ExitBg, 44f, 0f, new Vector2(0f, 1f),
                        new Vector2(UiKit.Css(14f), -UiKit.Css(14f)), ExitTour);

            // ── depth: TOP-RIGHT pill, 19px/800 #9fe0ff ─────────────────────────
            Image pill = UiKit.MakePanel(root, "TourDepth", DepthBg);
            pill.sprite = UiKit.RoundedSprite(14f);
            pill.type = Image.Type.Sliced;
            pill.raycastTarget = false;
            RectTransform prt = pill.rectTransform;
            prt.anchorMin = new Vector2(1f, 1f);
            prt.anchorMax = new Vector2(1f, 1f);
            prt.pivot = new Vector2(1f, 1f);
            prt.sizeDelta = new Vector2(UiKit.Css(120f), UiKit.Css(38f));
            prt.anchoredPosition = new Vector2(-UiKit.Css(14f), -UiKit.Css(14f));

            Image pillRim = UiKit.MakePanel(prt, "Rim", DepthRim);
            pillRim.sprite = UiKit.RoundedSprite(14f, 1.5f);
            pillRim.type = Image.Type.Sliced;
            pillRim.raycastTarget = false;
            UiKit.Stretch(pillRim.rectTransform);

            _depth = UiKit.MakeText(prt, "Value", "", UiKit.CssFont(19f), TextAnchor.MiddleCenter, DepthTxt);
            _depth.fontStyle = FontStyle.Bold;
            UiKit.Stretch(_depth.rectTransform);

            // ── hint line: TOP-CENTRE, 12px/600 ─────────────────────────────────
            Image hint = UiKit.MakePanel(root, "TourHint", HintBg);
            hint.sprite = UiKit.RoundedSprite(14f);
            hint.type = Image.Type.Sliced;
            hint.raycastTarget = false;
            RectTransform hrt = hint.rectTransform;
            hrt.anchorMin = new Vector2(0.5f, 1f);
            hrt.anchorMax = new Vector2(0.5f, 1f);
            hrt.pivot = new Vector2(0.5f, 1f);
            hrt.sizeDelta = new Vector2(UiKit.Css(300f), UiKit.Css(28f));
            hrt.anchoredPosition = new Vector2(0f, -UiKit.Css(15f));
            Text hintText = UiKit.MakeText(hrt, "Text",
                UiStrings.Tr("จอยซ้าย = ขึ้น/ลง + หัน · จอยขวา = เดินหน้า"),
                UiKit.CssFont(12f), TextAnchor.MiddleCenter, HintTxt);
            UiKit.Stretch(hintText.rectTransform);

            // ── lamp: LEFT 14 / TOP 104, 56 px, 2.5 px rim ──────────────────────
            _light = RoundButton(root, "TourLight", "lamp", Chrome, 56f, 2.5f, new Vector2(0f, 1f),
                                 new Vector2(UiKit.Css(14f), -UiKit.Css(104f)), ToggleLight);
            Transform ic = _light.transform.Find("Icon");
            _lightIcon = ic != null ? ic.GetComponent<Image>() : null;

            // ── radar: LEFT 14 / TOP 174, 56 px (#radarBtn, builder.html:271) ───
            // This slot belongs to the radar toggle on the web. The mute button below it is ours:
            // the web declares _muteFloat and never builds it, so there is no web position to
            // match — it goes in the next slot down rather than displacing a control the diver
            // may already know where to find.
            _radar = RoundButton(root, "TourRadar", "radar", Chrome, 56f, 2.5f, new Vector2(0f, 1f),
                                 new Vector2(UiKit.Css(14f), -UiKit.Css(174f)), ToggleRadar);

            // ── mute: LEFT 14 / TOP 244 — ours, one slot below the web's last ───
            _mute = RoundButton(root, "TourMute", "sound", Chrome, 56f, 2.5f, new Vector2(0f, 1f),
                                new Vector2(UiKit.Css(14f), -UiKit.Css(244f)), ToggleMute);
            RenderMute();

            // ── camera: RIGHT 14 / TOP 104 (#tourCam; #tourRec cut from v1) ─────
            RoundButton(root, "TourShot", "camera", Chrome, 56f, 2.5f, new Vector2(1f, 1f),
                        new Vector2(-UiKit.Css(14f), -UiKit.Css(104f)), Photo);

            // ── shop: RIGHT 14 / TOP 174 (E5) ───────────────────────────────────
            // The web puts #_shopBtn at right 10 / top 82, but that is its BUILDER chrome; here
            // the same rail already carries the camera at top 104, and a shop button overlapping
            // the shutter is worse than one slot lower. Mirrors the radar on the left rail.
            // The cart opens the PALETTE, not the openShop() list: on the web the palette is
            // where a player buys (placing an object deducts the coins), and the plain list is a
            // secondary path. Porting the list first was the mistake §4.97 records.
            RoundButton(root, "TourShop", "cart", Chrome, 56f, 2.5f, new Vector2(1f, 1f),
                        new Vector2(-UiKit.Css(14f), -UiKit.Css(174f)),
                        () => PaletteSheet.Open(UiShell.Instance != null ? UiShell.Instance.Thumbs : null));

            // ── minimap: bottom 16, centred, 118 px ─────────────────────────────
            Image mini = UiKit.MakeCircle(root, "Minimap", Chrome);
            mini.raycastTarget = false;
            _minimap = mini.gameObject;
            RectTransform mrt = mini.rectTransform;
            mrt.anchorMin = new Vector2(0.5f, 0f);
            mrt.anchorMax = new Vector2(0.5f, 0f);
            mrt.pivot = new Vector2(0.5f, 0f);
            mrt.sizeDelta = new Vector2(UiKit.Css(118f), UiKit.Css(118f));
            mrt.anchoredPosition = new Vector2(0f, UiKit.Css(16f));
            Image miniRim = UiKit.MakeCircle(mrt, "Rim", MiniRim, 0.03f);
            miniRim.raycastTarget = false;
            UiKit.Stretch(miniRim.rectTransform);
            MinimapWidget.Attach(mrt);

            Debug.Log($"[UI] tour hud css-laid-out dpr={UiKit.DevicePixelRatio:F2} " +
                      $"canvasScale={UiKit.CanvasScale:F3} 48css={UiKit.Css(48f):F0}u " +
                      $"stick={stick:F0}u");
        }

        /// <summary>A round chrome button at <paramref name="anchor"/>, sized in CSS px.</summary>
        private static Button RoundButton(RectTransform parent, string name, string icon, Color bg,
                                          float cssSize, float cssRim, Vector2 anchor, Vector2 offset,
                                          UnityEngine.Events.UnityAction onClick)
        {
            Button btn = UiKit.MakeIconButton(parent, name, icon, onClick, false, UiKit.Css(cssSize));
            Image img = btn.GetComponent<Image>();
            if (img != null) img.color = bg;

            RectTransform rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.sizeDelta = new Vector2(UiKit.Css(cssSize), UiKit.Css(cssSize));
            rt.anchoredPosition = offset;

            Transform rimT = btn.transform.Find("Rim");
            Image rim = rimT != null ? rimT.GetComponent<Image>() : null;
            if (rim != null)
            {
                // Tour buttons wear the web's 2.5 px white rim; map-view chrome wears a hairline.
                rim.color = cssRim > 0f ? Rim : UiKit.Line;
                float thickness = cssRim > 0f
                    ? Mathf.Clamp(cssRim / (cssSize * 0.5f), 0.02f, 0.5f)
                    : 0.035f;
                rim.sprite = UiKit.CircleSprite(thickness);
            }
            return btn;
        }

        /// <summary>The web's four 9.5 px stick labels (ขึ้น / ลง / ◀หัน / หัน▶).</summary>
        private static void StickLabels(RectTransform root, string stickName,
                                        string top, string bottom, string left, string right)
        {
            Transform stick = root.Find(stickName);
            if (stick == null) return;
            int size = UiKit.CssFont(9.5f);
            float pad = UiKit.Css(8f);
            Label(stick, "T", top, size, TextAnchor.UpperCenter, new Vector2(0f, -pad));
            Label(stick, "B", bottom, size, TextAnchor.LowerCenter, new Vector2(0f, pad));
            Label(stick, "L", left, size, TextAnchor.MiddleLeft, new Vector2(UiKit.Css(9f), 0f));
            Label(stick, "R", right, size, TextAnchor.MiddleRight, new Vector2(-UiKit.Css(9f), 0f));
        }

        private static void Label(Transform parent, string name, string thai, int size,
                                  TextAnchor anchor, Vector2 offset)
        {
            Text t = UiKit.MakeText(parent, "Lbl" + name, UiStrings.Tr(thai), size, anchor, LabelCol);
            t.fontStyle = FontStyle.Bold;
            t.raycastTarget = false;
            RectTransform rt = t.rectTransform;
            UiKit.Stretch(rt);
            rt.offsetMin = offset;
            rt.offsetMax = offset;
        }

        // ── vignette ─────────────────────────────────────────────────────────────

        private void BuildVignette(RectTransform root)
        {
            // 0.85 was read on a phone as "the picture does not fill the screen": the sprite is a
            // RADIAL gradient stretched over a 2.16:1 landscape display, so the darkening that
            // looks like a soft corner shade on a square editor view becomes two dark bands down
            // the left and right edges — indistinguishable from letterboxing, and the first thing
            // reported about the drone view. Corners now settle at 0.42, and the falloff starts
            // further out (VignetteSprite), so the frame reads as depth rather than as a border.
            _vignette = UiKit.MakePanel(root, "Vignette", new Color(0f, 0.03f, 0.05f, 0.42f));
            _vignette.raycastTarget = false;
            _vignette.sprite = VignetteSprite();
            _vignette.type = Image.Type.Simple;
            RectTransform rt = _vignette.rectTransform;
            UiKit.Stretch(rt);
            rt.offsetMin = new Vector2(-80f, -80f);
            rt.offsetMax = new Vector2(80f, 80f);
            rt.SetAsFirstSibling();
        }

        private static Sprite _vignetteSprite;
        private static Sprite VignetteSprite()
        {
            if (_vignetteSprite != null) return _vignetteSprite;
            const int n = 96;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                name = "TourVignette",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = x / (float)(n - 1) * 2f - 1f;
                float v = y / (float)(n - 1) * 2f - 1f;
                float d = Mathf.Sqrt(u * u + v * v) / 1.4142f;
                // Start at 0.72 of the way out, not 0.55: on a wide screen the edge midpoints sit
                // at d = 0.707, so the old figure began darkening the sides before they were even
                // near a corner.
                float a = Mathf.Clamp01((d - 0.72f) / 0.28f);
                a = a * a * (3f - 2f * a);
                px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply();
            _vignetteSprite = Sprite.Create(tex, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f));
            return _vignetteSprite;
        }

        // ── actions ──────────────────────────────────────────────────────────────

        private static void ExitTour()
        {
            if (ModeManager.Instance != null) ModeManager.Instance.Exit();
        }

        private static void ToggleLight()
        {
            TourController tc = TourController.Active;
            if (tc != null) tc.ToggleHeadlight();
        }

        private static void Photo()
        {
            TourController tc = TourController.Active;
            if (tc != null) tc.TakePhoto();
        }

        private void ToggleMute()
        {
            AudioBank.Muted = !AudioBank.Muted;
            RenderMute();
        }

        private void RenderMute()
        {
            if (_mute == null) return;
            UiKit.SetIcon(_mute, AudioBank.Muted ? "mute" : "sound");
        }

        /// <summary>
        /// A5 — the radar toggle (the web's #radarBtn handler, builder.html:3753): it hides the
        /// minimap and dims its own button to 45 % rather than changing what the minimap draws.
        /// </summary>
        private void ToggleRadar()
        {
            _radarOn = !_radarOn;
            if (_minimap != null) _minimap.SetActive(_radarOn);
            if (_radar != null)
            {
                var cg = _radar.GetComponent<CanvasGroup>() ?? _radar.gameObject.AddComponent<CanvasGroup>();
                cg.alpha = _radarOn ? 1f : 0.45f;
            }
            Debug.Log($"[UI] radar={( _radarOn ? "on" : "off")}");
        }

        /// <summary>Lamp state, styled like the web's #lightBtn.on (amber fill, amber rim + icon).</summary>
        public void SetHeadlight(bool on)
        {
            if (_light == null) return;
            Image bg = _light.GetComponent<Image>();
            if (bg != null) bg.color = on ? LampOn : Chrome;
            Transform rimT = _light.transform.Find("Rim");
            Image rim = rimT != null ? rimT.GetComponent<Image>() : null;
            if (rim != null) rim.color = on ? LampOnRim : Rim;
            if (_lightIcon != null) _lightIcon.color = on ? LampIcon : UiKit.TextMain;
        }

        /// <summary>Live depth, formatted like the web's readout ("21.7 ม.").</summary>
        public void SetDepth(float metres)
        {
            if (_depth == null) return;
            if (Mathf.Abs(metres - _shownDepth) < 0.05f) return;
            _shownDepth = metres;
            _depth.text = $"{metres:F1} {UiStrings.Tr("ม.")}";
        }
    }
}
