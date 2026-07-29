using System;
using System.Collections.Generic;
using UnityEngine;

namespace DiveMap.Core
{
    /// <summary>
    /// Tiny i18n table (WO-XR-05.1) that mirrors the web builder's approach exactly:
    /// the SOURCE strings are Thai (they are the keys), and a single TH→EN dictionary
    /// provides the English rendering — same shape as <c>TR</c> / <c>tr()</c> in
    /// builder.html. Nothing else is needed for two languages, and a missing key
    /// degrades gracefully to the Thai source instead of showing an empty label.
    ///
    /// Language selection: PlayerPrefs "lang" (values "th" / "en"); when unset it
    /// follows <see cref="Application.systemLanguage"/> (Thai → th, otherwise en) —
    /// the same default rule the web uses with navigator.language.
    ///
    /// Lives in Core (not Runtime/Ui) so the EditMode test assembly can reach it.
    /// </summary>
    public static class UiStrings
    {
        public const string LangPrefKey = "lang";
        public const string Thai = "th";
        public const string English = "en";

        // ── TH → EN table ────────────────────────────────────────────────────────
        // Keys are the literal Thai strings used in the UI code. Values must stay
        // pure Latin (UiStringsTests enforces both rules).
        private static readonly Dictionary<string, string> En =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // shell / menu
                { "เมนู",                  "Menu" },
                { "รายการแมพ",             "Maps" },
                { "ตั้งค่า",                "Settings" },
                { "ปิด",                   "Close" },
                { "ย้อนกลับ",              "Back" },
                { "เร็วๆ นี้",              "Coming soon" },

                // map list / search
                { "ค้นหาแมพ",              "Search maps" },
                { "กำลังโหลด…",            "Loading…" },
                { "ไม่พบแมพ",              "No maps found" },
                { "โหลดรายการแมพไม่สำเร็จ",  "Could not load the map list" },
                { "ลองใหม่",               "Retry" },
                { "ถูกใจ",                 "Likes" },
                { "ไม่ทราบผู้สร้าง",         "Unknown creator" },
                { "กำลังเปิดแมพ…",          "Opening map…" },
            };

        /// <summary>The whole TH→EN table (read-only) — used by tests and QC tooling.</summary>
        public static IEnumerable<KeyValuePair<string, string>> Table => En;

        public static int Count => En.Count;

        // ── Language ─────────────────────────────────────────────────────────────

        private static string _lang;

        /// <summary>Language the app falls back to when PlayerPrefs has no value.</summary>
        public static string DefaultLang =>
            Application.systemLanguage == SystemLanguage.Thai ? Thai : English;

        /// <summary>Current UI language ("th" / "en"); setting it persists to PlayerPrefs.</summary>
        public static string Lang
        {
            get
            {
                if (_lang == null) _lang = Normalize(PlayerPrefs.GetString(LangPrefKey, ""));
                return _lang;
            }
            set
            {
                _lang = Normalize(value);
                PlayerPrefs.SetString(LangPrefKey, _lang);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Clamp any input to a supported language code (unknown → system default).</summary>
        public static string Normalize(string lang)
        {
            if (string.IsNullOrEmpty(lang)) return DefaultLang;
            string l = lang.Trim().ToLowerInvariant();
            if (l == Thai) return Thai;
            if (l == English) return English;
            return DefaultLang;
        }

        // ── Translation ──────────────────────────────────────────────────────────

        /// <summary>Translate a Thai source string into the current language.</summary>
        public static string Tr(string source) => Tr(source, Lang);

        /// <summary>Pure overload (no PlayerPrefs) — used by tests and by explicit renders.</summary>
        public static string Tr(string source, string lang)
        {
            if (string.IsNullOrEmpty(source)) return source;
            if (lang != English) return source; // "th" (and anything unknown) = source as-is
            return En.TryGetValue(source, out string v) && !string.IsNullOrEmpty(v) ? v : source;
        }

        /// <summary>True when the string contains any character in the Thai Unicode block.</summary>
        public static bool ContainsThai(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c >= '\u0E00' && c <= '\u0E7F') return true;
            }
            return false;
        }
    }
}
