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

            Button exit = UiKit.MakeButton(root, "TourExit", UiStrings.Tr("ออกทัวร์"), 30,
                                           UiKit.TealDim, UiKit.TextMain, ExitTour);
            UiKit.Anchor(exit.GetComponent<RectTransform>(), new Vector2(1f, 1f),
                         new Vector2(220f, 84f), new Vector2(-28f, -28f));
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
