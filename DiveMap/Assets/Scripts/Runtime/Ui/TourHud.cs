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
    ///   #tourHud    (dropped — the sticks are already labelled; see Build)
    ///   #lightBtn   left 14, top 104            56×56, 2.5px white rim; ON = amber glow
    ///   #radarBtn   left 14, top 174            56×56, toggles the minimap; off = 45 % alpha
    ///   #tourCam    right 14, top 104, gap 14   56×56 (photo; #tourRec cut from v1)
    ///   (mute)      right 14, top 174           ours — the web never builds its _muteFloat.
    ///                                           Took the cart's slot: shopping is not diving.
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

            // ── hint line: REMOVED at the user's request ────────────────────────
            // The web keeps #tourHud ("จอยซ้าย = ขึ้น/ลง + หัน · จอยขวา = เดินหน้า") on screen
            // for the whole dive. It is a caption for controls that are already labelled — each
            // stick carries ขึ้น/ลง/หัน/หน้า/ถอย/สไลด์ around its rim — so it says the same thing
            // twice, in the middle of the view you came to look at. The tutorial still teaches
            // the sticks on the first dive, which is where a beginner needs the words.

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

            // ── camera: RIGHT 14 / TOP 104 (#tourCam; #tourRec cut from v1) ─────
            RoundButton(root, "TourShot", "camera", Chrome, 56f, 2.5f, new Vector2(1f, 1f),
                        new Vector2(-UiKit.Css(14f), -UiKit.Css(104f)), Photo);

            // ── mute: RIGHT 14 / TOP 174 — where the cart used to be ────────────
            // The cart (it opened the palette to buy and place things) is gone from the drone
            // view: diving is not shopping, and it sat on the rail your thumb reaches while
            // flying. Sound is what a diver actually reaches for mid-dive, so it takes the slot
            // rather than staying two rows down the opposite rail where it had been parked for
            // want of a web position to copy. Buying still lives in the builder, untouched.
            _mute = RoundButton(root, "TourMute", "sound", Chrome, 56f, 2.5f, new Vector2(1f, 1f),
                                new Vector2(-UiKit.Css(14f), -UiKit.Css(174f)), ToggleMute);
            RenderMute();

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
            // The web's own line, ported exactly rather than approximated (builder.html #vignette):
            //   radial-gradient(ellipse 78% 80% at 50% 42%, transparent 38%, rgba(4,16,32,.42) 100%)
            // Two earlier passes guessed at this — a circle stretched across a 2.16:1 phone screen,
            // which darkens the left and right edges far more than the top and bottom and reads as
            // a border rather than as depth. An ellipse sized in PERCENT of the element behaves the
            // same at every aspect, which is exactly why CSS specifies it that way.
            _vignette = UiKit.MakePanel(root, "Vignette", new Color(0.016f, 0.063f, 0.125f, 0.42f));
            _vignette.raycastTarget = false;
            _vignette.sprite = VignetteSprite();
            _vignette.type = Image.Type.Simple;
            RectTransform vrt = _vignette.rectTransform;
            UiKit.Stretch(vrt);
            // 🔴 …and then push it well past its parent. This HUD lives inside the SAFE AREA node,
            // which on an iPhone in landscape is inset ~100 px on the notch side and a strip at the
            // bottom. A vignette that stops at the safe area tints the middle of the screen and
            // leaves an untinted border all the way round — which is not a black frame at all, it
            // is a LIGHTER one, and that is exactly what the screenshots show: the picture looks
            // like a rectangle inside a paler surround.
            //
            // Reported five times. Two fixes were aimed at Screen.SetResolution and the render
            // scale on the theory that the drawable was smaller than the display; both missed,
            // because the drawable was always full size. The give-away was in the pixels the whole
            // time: the border is brighter than the middle, so nothing is missing from it —
            // something is only covering the middle.
            //
            // CI cannot reproduce it: a desktop window has no safe area, so the parent already
            // fills the screen and the QC images looked correct every single time.
            float over = UiKit.Css(140f);   // comfortably past any notch or home indicator
            vrt.offsetMin = new Vector2(-over, -over);
            vrt.offsetMax = new Vector2(over, over);
            vrt.SetAsFirstSibling();
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
                // Element-relative, like CSS: 0..1 across the rect, centre at (50%, 42%).
                // Radii are the gradient's own 78% / 80%, so the ellipse scales WITH the screen
                // and the falloff at the left edge matches the falloff at the top edge.
                float u = (x / (float)(n - 1) - 0.5f) / 0.78f;
                float v = (y / (float)(n - 1) - 0.58f) / 0.80f;   // v=0 is the bottom row
                float d = Mathf.Sqrt(u * u + v * v);
                float a = Mathf.Clamp01((d - 0.38f) / 0.62f);     // transparent 38% → full 100%
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
