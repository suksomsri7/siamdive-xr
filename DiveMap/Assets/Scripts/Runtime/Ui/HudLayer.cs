using System.Collections.Generic;
using DiveMap.Core;
using UnityEngine;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// P0.5 — one container per <see cref="AppMode"/> for the widgets that belong to that mode
    /// only: joysticks, depth readout, compass, coin counter (P1-P3). Exactly one layer is
    /// active at a time, so a mode's HUD cannot linger after the mode ends — the class of bug
    /// where a joystick keeps steering the camera in the map list.
    ///
    /// Lives under <see cref="UiShell.OverlayRoot"/> (inside the safe area) and NOT in the
    /// navigation stack: a HUD is not a screen, it must not consume the Android back button.
    /// </summary>
    public static class HudLayer
    {
        private static readonly Dictionary<AppMode, RectTransform> Layers =
            new Dictionary<AppMode, RectTransform>();

        private static AppMode _active = AppMode.View;

        /// <summary>
        /// The layer for <paramref name="mode"/>, created on first use. Returns null before the
        /// UI shell exists (headless -qcshot runs), so callers must null-check — every HUD
        /// builder is expected to no-op rather than throw in that case.
        /// </summary>
        public static RectTransform For(AppMode mode)
        {
            if (Layers.TryGetValue(mode, out RectTransform existing) && existing != null)
                return existing;

            UiShell shell = UiShell.Instance;
            RectTransform host = shell != null ? shell.OverlayRoot : null;
            if (host == null) return null;

            RectTransform layer = UiKit.MakeNode(host, "Hud_" + mode);
            layer.anchorMin = Vector2.zero;
            layer.anchorMax = Vector2.one;
            layer.offsetMin = Vector2.zero;
            layer.offsetMax = Vector2.zero;
            // Behind the toast (added later ⇒ drawn later) but above the 3D view.
            layer.SetAsFirstSibling();
            layer.gameObject.SetActive(mode == _active);

            Layers[mode] = layer;
            return layer;
        }

        /// <summary>Show only <paramref name="mode"/>'s layer. Called by <c>ModeManager</c>.</summary>
        public static void SetActiveMode(AppMode mode)
        {
            _active = mode;
            foreach (KeyValuePair<AppMode, RectTransform> kv in Layers)
            {
                if (kv.Value == null) continue;
                kv.Value.gameObject.SetActive(kv.Key == mode);
            }
        }

        /// <summary>Drop references to destroyed layers (map reload / shell rebuild).</summary>
        public static void Reset()
        {
            Layers.Clear();
            _active = AppMode.View;
        }
    }
}
