using System;
using UnityEngine;

namespace DiveMap.Core
{
    /// <summary>
    /// Persisted user settings (WO-XR-05.4): UI language and graphics quality.
    ///
    /// Same contract as <see cref="UiStrings.Lang"/> — PlayerPrefs is the store, any
    /// unrecognised value degrades to the default instead of propagating garbage into
    /// the UI. Language lives in UiStrings (it is the i18n table's own switch); this
    /// class owns "gfx" and re-exports language so a settings screen has one door.
    ///
    /// Pure logic only (PlayerPrefs + validation): applying quality to the engine is
    /// the runtime layer's job, so this file stays reachable from the EditMode tests.
    /// </summary>
    public static class SettingsStore
    {
        public const string GfxPrefKey = "gfx";
        public const string High = "high";
        public const string Lite = "lite";

        /// <summary>
        /// Render-scale used by the "battery saver" preset. 0.75 = 56% of the pixels,
        /// which is the usual sweet spot before UI text starts to look soft.
        /// </summary>
        public const float LiteRenderScale = 0.75f;

        private static string _gfx;

        /// <summary>Graphics preset ("high" / "lite"); the setter persists to PlayerPrefs.</summary>
        public static string Gfx
        {
            get
            {
                if (_gfx == null) _gfx = NormalizeGfx(PlayerPrefs.GetString(GfxPrefKey, ""));
                return _gfx;
            }
            set
            {
                _gfx = NormalizeGfx(value);
                PlayerPrefs.SetString(GfxPrefKey, _gfx);
                PlayerPrefs.Save();
            }
        }

        /// <summary>UI language ("th" / "en") — delegates to the i18n table.</summary>
        public static string Lang
        {
            get => UiStrings.Lang;
            set => UiStrings.Lang = value;
        }

        /// <summary>Clamp any input to a supported preset. Unknown / empty ⇒ <see cref="High"/>.</summary>
        public static string NormalizeGfx(string value)
        {
            if (string.IsNullOrEmpty(value)) return High;
            string v = value.Trim().ToLowerInvariant();
            return v == Lite ? Lite : High;
        }

        public static bool IsLite(string gfx) => NormalizeGfx(gfx) == Lite;

        /// <summary>
        /// Backbuffer size for a preset. "high" keeps the native size; "lite" scales it
        /// by <see cref="LiteRenderScale"/> and never returns a degenerate (&lt;1 px) size.
        /// </summary>
        public static void ScaledResolution(int nativeWidth, int nativeHeight, string gfx,
                                            out int width, out int height)
        {
            width = Mathf.Max(1, nativeWidth);
            height = Mathf.Max(1, nativeHeight);
            if (!IsLite(gfx)) return;

            width = Mathf.Max(1, Mathf.RoundToInt(width * LiteRenderScale));
            height = Mathf.Max(1, Mathf.RoundToInt(height * LiteRenderScale));
        }

        /// <summary>Drop the in-memory cache so the next read hits PlayerPrefs again (tests).</summary>
        public static void ResetCache() => _gfx = null;
    }
}
