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
        private Button _mute;
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

            // Chrome as round 48 px glass circles with stroke icons, exactly the web's tour
            // furniture (#viewbtns / #backBtn): text buttons in a first-person view are an app-ism
            // the web does not have, and the icons carry across languages.
            Button exit = UiKit.MakeIconButton(root, "TourExit", "exit", ExitTour);
            UiKit.Anchor(exit.GetComponent<RectTransform>(), new Vector2(1f, 1f),
                         new Vector2(96f, 96f), new Vector2(-26f, -26f));

            _light = UiKit.MakeIconButton(root, "TourLight", "lamp", ToggleLight);
            UiKit.Anchor(_light.GetComponent<RectTransform>(), new Vector2(1f, 1f),
                         new Vector2(96f, 96f), new Vector2(-26f, -134f));

            _mute = UiKit.MakeIconButton(root, "TourMute", "sound", ToggleMute);
            UiKit.Anchor(_mute.GetComponent<RectTransform>(), new Vector2(1f, 1f),
                         new Vector2(96f, 96f), new Vector2(-26f, -242f));
            RenderMute();

            // The web keeps a compass on the right edge while you dive (#compass) — same place,
            // same red-north needle.
            CompassWidget.Create(root);
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

        /// <summary>Reflect the lamp state: the web tints its active tool button with the accent.</summary>
        public void SetHeadlight(bool on)
        {
            if (_light == null) return;
            Image bg = _light.GetComponent<Image>();
            if (bg != null) bg.color = on ? new Color(0.224f, 0.690f, 0.910f, 0.60f) : UiKit.Glass;
        }

        private void ToggleMute()
        {
            AudioBank.Muted = !AudioBank.Muted;
            RenderMute();
        }

        private void RenderMute()
        {
            if (_mute == null) return;
            bool muted = AudioBank.Muted;
            UiKit.SetIcon(_mute, muted ? "mute" : "sound");
            Image bg = _mute.GetComponent<Image>();
            if (bg != null) bg.color = muted ? UiKit.Glass : new Color(0.224f, 0.690f, 0.910f, 0.60f);
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
