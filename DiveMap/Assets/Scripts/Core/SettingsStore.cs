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

        // ── drone speed ──────────────────────────────────────────────────────────
        //
        // One preference, one place. There is no speed control on the dive HUD and there must not
        // be one: the HUD is a thumb's width of screen with a photo button on it, and a value the
        // user sets once for how the app FEELS is not a per-dive decision.

        public const string SpeedPrefKey = "dronespeed";
        public const string SpeedCalm = "calm";
        public const string SpeedNormal = "normal";
        public const string SpeedFast = "fast";

        // ⚠️ These are multipliers of DroneFlight.Speed, which went back to the web's 30 u/s on
        // 2026-08-04 ("โดรนเคลื่อนที่ช้าไป"). A multiplier whose base has tripled does not mean what
        // it used to, so both are re-derived here rather than left to drift.

        /// <summary>
        /// 0.30 × 30 u/s = 9 u/s = 1.50 m/s — a DPV at full throttle. This is EXACTLY build 261's
        /// drone: the metric re-scale that the "เร็วไป" round shipped globally now lives here, where
        /// someone who wants to hang over one coral head can choose it and nobody else pays for it.
        /// </summary>
        /// 0.30 → 0.375 เมื่อฐานลดจาก 30 เป็น 24 u/s (user 9 ส.ค. "โดรนช้าลงอีกนิด"):
        /// พรีเซ็ตนี้มีความหมายว่า "โดรนของ build 261" = 9 u/s = 1.50 m/s ซึ่งเป็นหมุดจริงที่
        /// user เคยเลือก — ถ้าปล่อยไว้ที่ 0.30 มันจะกลายเป็น 7.2 u/s แล้วความหมายหาย
        /// (เทส SettingsStoreTests.EverySpeedPreset_IsAMultipleOfTheWebsFlightModel ตรึงไว้)
        public const float CalmSpeedScale = 0.375f;

        /// <summary>
        /// 1.25 × 30 u/s = 37.5 u/s = 6.25 m/s — for crossing a big site. Cut from 1.45 with the
        /// base restored: 1.45 would now be 43.5 u/s, a speed nothing in the app has ever been
        /// flown at and well past the web's own ceiling.
        /// </summary>
        /// 1.25 × 24 = 30 u/s — พอฐานลดเป็น 24 พรีเซ็ต "เร็ว" จึงกลายเป็นความเร็วเว็บเดิมพอดี
        /// ซึ่งเป็นความหมายที่ดีสำหรับพรีเซ็ตนี้: ใครอยากได้ความเร็วแบบก่อน 9 ส.ค. ก็เลือกอันนี้
        public const float FastSpeedScale = 1.25f;

        /// <summary>
        /// Render-scale used by the "battery saver" preset. 0.75 = 56% of the pixels,
        /// which is the usual sweet spot before UI text starts to look soft.
        /// </summary>
        public const float LiteRenderScale = 0.75f;

        private static string _gfx;
        private static string _speed;

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

        /// <summary>
        /// How fast the drone flies ("calm" / "normal" / "fast"); the setter persists.
        /// The default is <see cref="SpeedNormal"/> — the speed the flight model is tuned at, so
        /// someone who never opens settings gets the one that was designed.
        /// </summary>
        public static string DroneSpeed
        {
            get
            {
                if (_speed == null) _speed = NormalizeSpeed(PlayerPrefs.GetString(SpeedPrefKey, ""));
                return _speed;
            }
            set
            {
                _speed = NormalizeSpeed(value);
                PlayerPrefs.SetString(SpeedPrefKey, _speed);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Clamp any input to a supported preset. Unknown / empty ⇒ <see cref="SpeedNormal"/>.</summary>
        public static string NormalizeSpeed(string value)
        {
            if (string.IsNullOrEmpty(value)) return SpeedNormal;
            string v = value.Trim().ToLowerInvariant();
            if (v == SpeedCalm) return SpeedCalm;
            if (v == SpeedFast) return SpeedFast;
            return SpeedNormal;
        }

        /// <summary>Multiplier a preset applies to <c>DroneFlight.Speed</c>.</summary>
        public static float SpeedScaleOf(string preset)
        {
            string p = NormalizeSpeed(preset);
            if (p == SpeedCalm) return CalmSpeedScale;
            if (p == SpeedFast) return FastSpeedScale;
            return 1f;
        }

        /// <summary>The live multiplier for the stored preference.</summary>
        public static float SpeedScale => SpeedScaleOf(DroneSpeed);

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
        public static void ResetCache() { _gfx = null; _speed = null; }
    }
}
