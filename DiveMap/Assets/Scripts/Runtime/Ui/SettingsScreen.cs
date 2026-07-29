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
        private Text _versionLabel;
        private Text _versionValue;

        private Button _langTh, _langEn, _gfxHigh, _gfxLite, _link, _close;

        // ── build ────────────────────────────────────────────────────────────────

        public void Build()
        {
            var self = GetComponent<RectTransform>();
            if (self == null) self = gameObject.AddComponent<RectTransform>();
            UiKit.Stretch(self);

            Button scrim = UiKit.MakeButton(self, "Scrim", null, 0, UiKit.Scrim,
                                            UiKit.TextMain, RaiseClose);
            UiKit.Stretch(scrim.GetComponent<RectTransform>());

            Image card = UiKit.MakePanel(self, "Card", UiKit.PanelBg);
            RectTransform rt = card.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(900f, 900f);
            rt.anchoredPosition = Vector2.zero;

            // Row heights stay well above fontSize × 1.51 (NotoSansThai's line height):
            // legacy Text DROPS a line that does not fit its rect instead of clipping it.
            _title = UiKit.MakeText(rt, "Title", "", 46, TextAnchor.MiddleLeft, UiKit.Teal);
            UiKit.TopRow(_title.rectTransform, 30f, 80f, 40f, 40f);

            // ── language ─────────────────────────────────────────────────────────
            _langLabel = UiKit.MakeText(rt, "LangLabel", "", 34, TextAnchor.MiddleLeft, UiKit.TextDim);
            UiKit.TopRow(_langLabel.rectTransform, 134f, 60f, 40f, 40f);

            _langTh = Choice(rt, "LangTh", 200f, 40f, () => SetLang(UiStrings.Thai));
            _langEn = Choice(rt, "LangEn", 200f, 460f, () => SetLang(UiStrings.English));

            // ── graphics ─────────────────────────────────────────────────────────
            _gfxLabel = UiKit.MakeText(rt, "GfxLabel", "", 34, TextAnchor.MiddleLeft, UiKit.TextDim);
            UiKit.TopRow(_gfxLabel.rectTransform, 324f, 60f, 40f, 40f);

            _gfxHigh = Choice(rt, "GfxHigh", 390f, 40f, () => SetGfx(SettingsStore.High));
            _gfxLite = Choice(rt, "GfxLite", 390f, 460f, () => SetGfx(SettingsStore.Lite));

            // ── version + link ───────────────────────────────────────────────────
            _versionLabel = UiKit.MakeText(rt, "VersionLabel", "", 32, TextAnchor.MiddleLeft, UiKit.TextDim);
            UiKit.TopRow(_versionLabel.rectTransform, 526f, 58f, 40f, 480f);

            _versionValue = UiKit.MakeText(rt, "VersionValue", Application.version, 32,
                                           TextAnchor.MiddleRight, UiKit.TextMain);
            UiKit.TopRow(_versionValue.rectTransform, 526f, 58f, 480f, 40f);

            // The URL itself is never translated — it is the same in both languages.
            Text linkLabel = UiKit.MakeText(rt, "LinkLabel", "maps.siamdive.com", 30,
                                            TextAnchor.MiddleLeft, UiKit.TextDim);
            UiKit.TopRow(linkLabel.rectTransform, 596f, 54f, 40f, 40f);

            _link = UiKit.MakeButton(rt, "LinkButton", "", 32, UiKit.CardBg, UiKit.TextMain, OpenWeb);
            UiKit.TopRow(_link.GetComponent<RectTransform>(), 660f, 88f, 40f, 40f);

            _close = UiKit.MakeButton(rt, "Close", "", 32, UiKit.TealDim, UiKit.TextMain, RaiseClose);
            UiKit.Anchor(_close.GetComponent<RectTransform>(), new Vector2(0.5f, 0f),
                         new Vector2(280f, 88f), new Vector2(0f, 40f));

            Refresh();
        }

        /// <summary>A half-width choice button on the row starting at <paramref name="y"/>.</summary>
        private static Button Choice(RectTransform parent, string name, float y, float x,
                                     UnityEngine.Events.UnityAction action)
        {
            Button b = UiKit.MakeButton(parent, name, "", 34, UiKit.CardBg, UiKit.TextMain, action);
            RectTransform rt = b.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(400f, 88f);
            rt.anchoredPosition = new Vector2(x, -y);
            return b;
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
