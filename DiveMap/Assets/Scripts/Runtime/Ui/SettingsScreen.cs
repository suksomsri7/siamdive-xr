using System;
using UnityEngine;
using UnityEngine.UI;
using DiveMap.Core;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// Settings screen (WO-XR-05.4): UI language, graphics preset, app version and a
    /// link back to the web builder. Replaces the "เร็วๆ นี้" placeholder from 05.1.
    ///
    /// Language changes take effect immediately — no restart. The screen only writes the
    /// preference and raises <see cref="LanguageChanged"/>; the shell re-renders every
    /// live Text through <see cref="UiStrings.ToLang"/>, which is what lets screens that
    /// this work order must not edit (the map list, AppBoot's status line) follow along.
    ///
    /// The graphics preset is deliberately narrow in v1: QualitySettings + backbuffer
    /// scale + shadows only. Fish counts live in FishSchoolSystem/SceneBuilder, which are
    /// owned by another work order — a "fewer fish" toggle is a follow-up, not this WO.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SettingsScreen : MonoBehaviour
    {
        private const string WebUrl = "https://maps.siamdive.com";

        public event Action CloseRequested;
        public event Action LanguageChanged;

        private Text _title;
        private Text _langLabel;
        private Text _gfxLabel;
        private Text _perfLabel;
        private Text _versionLabel;
        private Text _versionValue;

        private Button _langTh, _langEn, _gfxHigh, _gfxLite, _perfOn, _perfOff, _link, _close;

        // ── build ────────────────────────────────────────────────────────────────

        public void Build()
        {
            var self = GetComponent<RectTransform>();
            if (self == null) self = gameObject.AddComponent<RectTransform>();
            UiKit.Stretch(self);

            // Settings is a bottom sheet like every other list on the web (#sheet), not a floating
            // card: same dismiss gesture, same corner radius, map still visible behind it.
            RectTransform rt = UiKit.MakeSheet(self, "SettingsSheet", RaiseClose, 0.62f);

            // Row geometry in CSS px (web: 16 px sheet title, 12 px section labels, 44 px rows,
            // 16 px side padding). Row HEIGHTS still come from UiKit.RowHeight so a Thai line can
            // never be dropped, whatever the font size resolves to.
            float pad = UiKit.Css(16f);
            float gap = UiKit.Css(10f);
            float rowH = UiKit.Css(44f);
            float y = UiKit.Css(4f);

            _title = UiKit.MakeText(rt, "Title", "", UiKit.CssFont(16f), TextAnchor.MiddleLeft, UiKit.Accent);
            _title.fontStyle = FontStyle.Bold;
            UiKit.TopRow(_title.rectTransform, y, UiKit.RowHeight(UiKit.CssFont(16f)), pad, pad);
            y += UiKit.RowHeight(UiKit.CssFont(16f)) + gap;

            // ── language ─────────────────────────────────────────────────────────
            _langLabel = UiKit.MakeText(rt, "LangLabel", "", UiKit.CssFont(12f), TextAnchor.MiddleLeft, UiKit.TextDim);
            UiKit.TopRow(_langLabel.rectTransform, y, UiKit.RowHeight(UiKit.CssFont(12f)), pad, pad);
            y += UiKit.RowHeight(UiKit.CssFont(12f)) + UiKit.Css(4f);

            float half = (Screen.width / UiKit.CanvasScale - pad * 2f - gap) * 0.5f;
            _langTh = Choice(rt, "LangTh", y, pad, half, rowH, () => SetLang(UiStrings.Thai));
            _langEn = Choice(rt, "LangEn", y, pad + half + gap, half, rowH, () => SetLang(UiStrings.English));
            y += rowH + gap * 1.6f;

            // ── graphics ─────────────────────────────────────────────────────────
            _gfxLabel = UiKit.MakeText(rt, "GfxLabel", "", UiKit.CssFont(12f), TextAnchor.MiddleLeft, UiKit.TextDim);
            UiKit.TopRow(_gfxLabel.rectTransform, y, UiKit.RowHeight(UiKit.CssFont(12f)), pad, pad);
            y += UiKit.RowHeight(UiKit.CssFont(12f)) + UiKit.Css(4f);

            _gfxHigh = Choice(rt, "GfxHigh", y, pad, half, rowH, () => SetGfx(SettingsStore.High));
            _gfxLite = Choice(rt, "GfxLite", y, pad + half + gap, half, rowH, () => SetGfx(SettingsStore.Lite));
            y += rowH + gap * 1.6f;

            // ── frame-rate readout (A7) ──────────────────────────────────────────
            // Here rather than behind a hidden gesture, because its purpose is to let the person
            // holding the phone answer "how does it run?" with a number.
            _perfLabel = UiKit.MakeText(rt, "PerfLabel", "", UiKit.CssFont(12f), TextAnchor.MiddleLeft, UiKit.TextDim);
            UiKit.TopRow(_perfLabel.rectTransform, y, UiKit.RowHeight(UiKit.CssFont(12f)), pad, pad);
            y += UiKit.RowHeight(UiKit.CssFont(12f)) + UiKit.Css(4f);

            _perfOn  = Choice(rt, "PerfOn",  y, pad, half, rowH, () => SetPerf(true));
            _perfOff = Choice(rt, "PerfOff", y, pad + half + gap, half, rowH, () => SetPerf(false));
            y += rowH + gap * 1.6f;

            // ── version + link ───────────────────────────────────────────────────
            _versionLabel = UiKit.MakeText(rt, "VersionLabel", "", UiKit.CssFont(12f), TextAnchor.MiddleLeft, UiKit.TextDim);
            UiKit.TopRow(_versionLabel.rectTransform, y, UiKit.RowHeight(UiKit.CssFont(12f)), pad, pad);

            _versionValue = UiKit.MakeText(rt, "VersionValue", Application.version, UiKit.CssFont(12f),
                                           TextAnchor.MiddleRight, UiKit.TextMain);
            UiKit.TopRow(_versionValue.rectTransform, y, UiKit.RowHeight(UiKit.CssFont(12f)), pad, pad);
            y += UiKit.RowHeight(UiKit.CssFont(12f)) + gap;

            // The URL itself is never translated — it is the same in both languages.
            Text linkLabel = UiKit.MakeText(rt, "LinkLabel", "maps.siamdive.com", UiKit.CssFont(12f),
                                            TextAnchor.MiddleLeft, UiKit.TextDim);
            UiKit.TopRow(linkLabel.rectTransform, y, UiKit.RowHeight(UiKit.CssFont(12f)), pad, pad);
            y += UiKit.RowHeight(UiKit.CssFont(12f)) + UiKit.Css(4f);

            _link = UiKit.MakeButton(rt, "LinkButton", "", UiKit.CssFont(14f), UiKit.CardBg, UiKit.TextMain, OpenWeb);
            Round(_link);
            UiKit.TopRow(_link.GetComponent<RectTransform>(), y, rowH, pad, pad);

            // Close pinned to the sheet's own bottom-right, web button size (radius 13).
            _close = UiKit.MakeButton(rt, "Close", "", UiKit.CssFont(14f), UiKit.Accent, UiKit.OnAccent, RaiseClose);
            Round(_close);
            UiKit.Anchor(_close.GetComponent<RectTransform>(), new Vector2(1f, 0f),
                         new Vector2(UiKit.Css(96f), UiKit.Css(42f)), new Vector2(-pad, pad));

            Refresh();
        }

        /// <summary>A choice chip on the row starting at <paramref name="y"/>, sized in CSS px.</summary>
        private static Button Choice(RectTransform parent, string name, float y, float x,
                                     float w, float h, UnityEngine.Events.UnityAction action)
        {
            Button b = UiKit.MakeButton(parent, name, "", UiKit.CssFont(14f), UiKit.CardBg,
                                        UiKit.TextMain, action);
            Round(b);
            RectTransform rt = b.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, -y);
            return b;
        }

        /// <summary>Give a button the web's 13 px corner radius (its .row button rule).</summary>
        private static void Round(Button b)
        {
            Image img = b != null ? b.GetComponent<Image>() : null;
            if (img == null) return;
            img.sprite = UiKit.RoundedSprite(13f);
            img.type = Image.Type.Sliced;
        }

        // ── state ────────────────────────────────────────────────────────────────

        private void SetLang(string lang)
        {
            if (UiStrings.Lang == lang) return;
            UiStrings.Lang = lang;
            Debug.Log("[UI] language -> " + UiStrings.Lang);
            Refresh();
            LanguageChanged?.Invoke();
        }

        private void SetGfx(string gfx)
        {
            if (SettingsStore.Gfx == SettingsStore.NormalizeGfx(gfx)) return;
            SettingsStore.Gfx = gfx;
            ApplyGraphics(SettingsStore.Gfx);
            Refresh();
        }

        private void SetPerf(bool on)
        {
            if (PerfHud.Enabled == on) return;
            PerfHud.Enabled = on;   // the setter persists and rebuilds the readout
            Debug.Log("[UI] perf hud -> " + (on ? "on" : "off"));
            Refresh();
        }

        private static void OpenWeb()
        {
            Debug.Log("[UI] open " + WebUrl);
            Application.OpenURL(WebUrl);
        }

        private void RaiseClose() => CloseRequested?.Invoke();

        /// <summary>Re-render every label + the selected-state highlighting.</summary>
        public void Refresh()
        {
            if (_title == null) return;

            _title.text = UiStrings.Tr("ตั้งค่า");
            _langLabel.text = UiStrings.Tr("ภาษา");
            _gfxLabel.text = UiStrings.Tr("คุณภาพกราฟิก");
            _perfLabel.text = UiStrings.Tr("ตัวเลขเฟรมเรต");
            _versionLabel.text = UiStrings.Tr("เวอร์ชันแอป");
            _versionValue.text = Application.version;

            string lang = UiStrings.Lang;
            // "English" stays "English" in both languages — it is the endonym, and the
            // Thai option is translated so an English UI has no Thai glyphs left on it.
            SetChoice(_langTh, UiStrings.Tr("ไทย"), lang == UiStrings.Thai);
            SetChoice(_langEn, "English", lang == UiStrings.English);

            string gfx = SettingsStore.Gfx;
            SetChoice(_gfxHigh, UiStrings.Tr("คุณภาพสูง"), gfx == SettingsStore.High);
            SetChoice(_gfxLite, UiStrings.Tr("ประหยัดพลังงาน"), gfx == SettingsStore.Lite);

            bool perf = PerfHud.Enabled;
            SetChoice(_perfOn, UiStrings.Tr("แสดง"), perf);
            SetChoice(_perfOff, UiStrings.Tr("ซ่อน"), !perf);

            SetLabel(_link, UiStrings.Tr("เปิดเว็บไซต์"));
            SetLabel(_close, UiStrings.Tr("ปิด"));
        }

        private static void SetChoice(Button b, string label, bool selected)
        {
            if (b == null) return;
            SetLabel(b, label);

            var img = b.GetComponent<Image>();
            if (img != null) img.color = selected ? UiKit.Teal : UiKit.CardBg;

            Text t = b.GetComponentInChildren<Text>();
            if (t != null) t.color = selected ? UiKit.ScreenBg : UiKit.TextMain;
        }

        private static void SetLabel(Button b, string label)
        {
            if (b == null) return;
            Text t = b.GetComponentInChildren<Text>();
            if (t != null) t.text = label;
        }

        // ── graphics preset ──────────────────────────────────────────────────────

        private static bool _captured;
        private static ShadowQuality _defShadows;
        private static float _defShadowDistance;
        private static int _defAntiAliasing;
        private static int _defWidth, _defHeight;

        /// <summary>
        /// Apply a preset to the engine. The FIRST call snapshots the project's own
        /// quality settings so "high" restores exactly what shipped instead of a guessed
        /// set of values — the lighting/reflection setup in AppBoot was tuned against
        /// those defaults and must not drift.
        /// </summary>
        public static void ApplyGraphics(string gfx)
        {
            if (!_captured)
            {
                _defShadows = QualitySettings.shadows;
                _defShadowDistance = QualitySettings.shadowDistance;
                _defAntiAliasing = QualitySettings.antiAliasing;
                _defWidth = Mathf.Max(1, Screen.width);
                _defHeight = Mathf.Max(1, Screen.height);
                _captured = true;
            }

            bool lite = SettingsStore.IsLite(gfx);

            QualitySettings.shadows = lite ? ShadowQuality.Disable : _defShadows;
            QualitySettings.shadowDistance = lite ? 0f : _defShadowDistance;
            QualitySettings.antiAliasing = lite ? 0 : _defAntiAliasing;

            SettingsStore.ScaledResolution(_defWidth, _defHeight, gfx, out int w, out int h);
            if (w != Screen.width || h != Screen.height)
                Screen.SetResolution(w, h, Screen.fullScreen);

            Debug.Log($"[UI] gfx={SettingsStore.NormalizeGfx(gfx)} shadows={QualitySettings.shadows} " +
                      $"aa={QualitySettings.antiAliasing} res={w}x{h}");
        }
    }
}
