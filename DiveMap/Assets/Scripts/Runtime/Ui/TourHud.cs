using DiveMap.Core;
using UnityEngine;
using UnityEngine.UI;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// P1.1 — the tour HUD: two analog sticks, the live depth readout and an exit button, on the
    /// <see cref="AppMode.Tour"/> layer so it appears and disappears with the mode and can never
    /// be left steering an orbit camera (P0.5).
    ///
    /// Layout follows the web (builder.html #stickL/#stickR): left stick bottom-left = turn and
    /// rise/dive, right stick bottom-right = thrust, depth top-left under the map header. Only
    /// the sticks and the button take raycasts; the rest of the screen stays tappable so later
    /// features (photo, info card, trash) can use taps.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TourHud : MonoBehaviour
    {
        private const float StickSize = 300f;
        private const float StickInset = 210f;

        private Text _depth;
        private float _shownDepth = -999f;
        private Button _light;
        private Text _lightLabel;
        private Button _mute;
        private Text _muteLabel;
        private Image _vignette;

        public static TourHud Ensure()
        {
            RectTransform layer = HudLayer.For(AppMode.Tour);
            if (layer == null) return null;

            TourHud existing = layer.GetComponentInChildren<TourHud>(true);
            if (existing != null) return existing;

            RectTransform root = UiKit.MakeNode(layer, "TourHud");
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            var hud = root.gameObject.AddComponent<TourHud>();
            hud.Build(root);
            return hud;
        }

        private void Build(RectTransform root)
        {
            JoystickWidget.Create(root, "StickL", new Vector2(0f, 0f),
                                  new Vector2(StickInset, StickInset), StickSize,
                                  v => InputRig.SetLeft(v));
            JoystickWidget.Create(root, "StickR", new Vector2(1f, 0f),
                                  new Vector2(-StickInset, StickInset), StickSize,
                                  v => InputRig.SetRight(v));

            // Depth readout. Its own row height via UiKit so the Thai unit never drops a line.
            _depth = UiKit.MakeText(root, "Depth", "", 34, TextAnchor.UpperLeft, UiKit.TextMain);
            RectTransform drt = _depth.rectTransform;
            drt.anchorMin = new Vector2(0f, 1f);
            drt.anchorMax = new Vector2(0f, 1f);
            drt.pivot = new Vector2(0f, 1f);
            drt.sizeDelta = new Vector2(420f, UiKit.RowHeight(34));
            drt.anchoredPosition = new Vector2(28f, -84f);

            BuildVignette(root);

            Button exit = UiKit.MakeButton(root, "TourExit", UiStrings.Tr("ออกทัวร์"), 30,
                                           UiKit.TealDim, UiKit.TextMain, ExitTour);
            UiKit.Anchor(exit.GetComponent<RectTransform>(), new Vector2(1f, 1f),
                         new Vector2(220f, 84f), new Vector2(-28f, -28f));

            // Headlamp toggle, under the exit button. Labelled with the state it will produce,
            // like the web's lightBtn highlight.
            _light = UiKit.MakeButton(root, "TourLight", UiStrings.Tr("ไฟหน้า"), 30,
                                      UiKit.TealDim, UiKit.TextMain, ToggleLight);
            UiKit.Anchor(_light.GetComponent<RectTransform>(), new Vector2(1f, 1f),
                         new Vector2(220f, 84f), new Vector2(-28f, -124f));
            _lightLabel = _light.GetComponentInChildren<Text>(true);

            // Mute, under the headlamp. State lives in PlayerPrefs so it survives the app.
            _mute = UiKit.MakeButton(root, "TourMute", "", 30, UiKit.TealDim, UiKit.TextMain, ToggleMute);
            UiKit.Anchor(_mute.GetComponent<RectTransform>(), new Vector2(1f, 1f),
                         new Vector2(220f, 84f), new Vector2(-28f, -220f));
            _muteLabel = _mute.GetComponentInChildren<Text>(true);
            RenderMute();
        }

        /// <summary>
        /// Vignette: the web darkens the frame's corners in its tour (#vignette) — underwater you
        /// see through a mask, not a rectangle. Drawn behind the sticks, never taking raycasts,
        /// and built from one generated texture so there is no asset to import.
        /// </summary>
        private void BuildVignette(RectTransform root)
        {
            _vignette = UiKit.MakePanel(root, "Vignette", new Color(0f, 0.03f, 0.05f, 0.85f));
            _vignette.raycastTarget = false;
            _vignette.sprite = VignetteSprite();
            _vignette.type = Image.Type.Simple;
            RectTransform rt = _vignette.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-80f, -80f);   // bleed past the edges so no seam shows
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
                // Distance from centre in "screen halves", so the corners (≈1.41) are darkest.
                float u = x / (float)(n - 1) * 2f - 1f;
                float v = y / (float)(n - 1) * 2f - 1f;
                float d = Mathf.Sqrt(u * u + v * v) / 1.4142f;
                float a = Mathf.Clamp01((d - 0.55f) / 0.45f);
                a = a * a * (3f - 2f * a);
                px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply();
            _vignetteSprite = Sprite.Create(tex, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f));
            return _vignetteSprite;
        }

        private static void ToggleLight()
        {
            TourController tc = TourController.Active;
            if (tc != null) tc.ToggleHeadlight();
        }

        /// <summary>Reflect the lamp state on the button (dim = off).</summary>
        public void SetHeadlight(bool on)
        {
            if (_light == null) return;
            Image bg = _light.GetComponent<Image>();
            if (bg != null) bg.color = on ? UiKit.Teal : UiKit.TealDim;
            if (_lightLabel != null)
                _lightLabel.color = on ? new Color(0.043f, 0.090f, 0.118f) : UiKit.TextMain;
        }

        private void ToggleMute()
        {
            AudioBank.Muted = !AudioBank.Muted;
            RenderMute();
        }

        private void RenderMute()
        {
            if (_muteLabel == null) return;
            bool muted = AudioBank.Muted;
            _muteLabel.text = UiStrings.Tr(muted ? "เปิดเสียง" : "ปิดเสียง");
            Image bg = _mute != null ? _mute.GetComponent<Image>() : null;
            if (bg != null) bg.color = muted ? UiKit.TealDim : UiKit.Teal;
            _muteLabel.color = muted ? UiKit.TextMain : new Color(0.043f, 0.090f, 0.118f);
        }

        private static void ExitTour()
        {
            if (ModeManager.Instance != null) ModeManager.Instance.Exit();
        }

        /// <summary>Called by <see cref="TourController"/> each frame with the drone's depth.</summary>
        public void SetDepth(float metres)
        {
            if (_depth == null) return;
            // Only touch the Text when the rounded value changes — a legacy Text rebuilds its
            // mesh on every assignment, and this runs at 60 Hz.
            if (Mathf.Abs(metres - _shownDepth) < 0.05f) return;
            _shownDepth = metres;
            _depth.text = $"{UiStrings.Tr("ความลึก")} {metres:F1} {UiStrings.Tr("ม.")}";
        }
    }
}
