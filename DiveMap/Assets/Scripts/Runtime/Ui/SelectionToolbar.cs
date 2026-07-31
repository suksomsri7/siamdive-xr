using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;
using DiveMap.Core;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// The web's <c>#seltool</c> (builder.html:318) — the pill that appears at the bottom when
    /// something is selected:
    /// <code>
    ///   [ ✥ ย้าย | ⟳ หมุน | ⤢ ขนาด ]   🎨 สี   ⧉ ก๊อป   🗑 ลบ   ✓ เสร็จ
    ///   #seltool  bottom 22 · radius 30 · padding 7/9 · gap 7
    ///   .seg      black 25% · radius 22 · padding 3 · buttons 44×38 · .on = accent on #04121f
    ///   .act      42×38 · radius 18 · white 8%   · .del #ff8a9c   · .done accent
    ///   #colorBar bottom 74 · radius 22 · 8 swatches 30 px + ↺ "original" (dashed)
    /// </code>
    ///
    /// The colour row only appears for rocks — <c>isRecolorable()</c> :2626 checks
    /// <c>kind === 'ROCK'</c>, because tinting a textured fish just makes it muddy.
    ///
    /// Every action goes through <see cref="SceneEdit"/> and is recorded in
    /// <see cref="EditHistory"/>, so undo works without this file knowing anything about it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SelectionToolbar : MonoBehaviour
    {
        /// <summary>builder.html:2600 ROCK_COLORS — the eight rock tints, in order.</summary>
        public static readonly string[] RockColors =
        {
            "#9a958c", "#6f685e", "#8a7355", "#c9b894",
            "#a8694a", "#6f7d5a", "#cfd2d4", "#3f3a35",
        };

        public enum Mode { Translate, Rotate, Scale }

        private static readonly Color SegBg = new Color(0f, 0f, 0f, 0.25f);
        private static readonly Color ActBg = new Color(1f, 1f, 1f, 0.08f);
        private static readonly Color DelFg = new Color(1f, 0.541f, 0.612f, 1f);   // #ff8a9c
        private static readonly Color BarBg = new Color(0.043f, 0.102f, 0.165f, 0.94f);

        private static SelectionToolbar _open;

        private RectTransform _bar;
        private RectTransform _colorBar;
        private readonly Dictionary<Mode, Image> _modeBg = new Dictionary<Mode, Image>();
        private Mode _mode = Mode.Translate;
        private string _id;
        private bool _recolorable;

        /// <summary>Raised when the mode segment changes, so the gizmo can retarget.</summary>
        public static event Action<Mode> ModeChanged;

        /// <summary>Raised after any edit that changed the map, with the item that was affected.</summary>
        public static event Action<string> Edited;

        /// <summary>Raised when the user dismisses the selection (✓).</summary>
        public static event Action Dismissed;

        // ── QC surface ───────────────────────────────────────────────────────────
        public static bool IsOpen => _open != null;
        public static SelectionToolbar Current => _open;
        public string SelectedId => _id;
        public Mode CurrentMode => _mode;
        public bool ColorRowVisible => _colorBar != null && _colorBar.gameObject.activeSelf;

        // ── open / close ─────────────────────────────────────────────────────────

        /// <summary>Show the toolbar for <paramref name="itemId"/>.</summary>
        public static void Show(string itemId, bool recolorable)
        {
            RectTransform host = UiShell.Instance != null ? UiShell.Instance.OverlayRoot : null;
            if (host == null) return;

            if (_open != null) { _open.Retarget(itemId, recolorable); return; }

            var go = new GameObject("SelectionToolbar");
            go.transform.SetParent(host, false);
            var rt = go.AddComponent<RectTransform>();
            UiKit.Stretch(rt);
            var bar = go.AddComponent<SelectionToolbar>();
            bar._id = itemId;
            bar._recolorable = recolorable;
            bar.Build(rt);
            _open = bar;
            Debug.Log($"[Edit] selected {itemId} recolorable={recolorable}");
        }

        public static void Hide()
        {
            if (_open == null) return;
            Destroy(_open.gameObject);
            _open = null;
        }

        private void OnDestroy()
        {
            if (_open == this) _open = null;
        }

        private void Retarget(string itemId, bool recolorable)
        {
            _id = itemId;
            _recolorable = recolorable;
            if (_colorBar != null) _colorBar.gameObject.SetActive(false);
            Transform recolor = _bar != null ? _bar.Find("Recolor") : null;
            if (recolor != null) recolor.gameObject.SetActive(recolorable);
            Debug.Log($"[Edit] selected {itemId} recolorable={recolorable}");
        }

        // ── build ────────────────────────────────────────────────────────────────

        private void Build(RectTransform root)
        {
            float h = UiKit.Css(38f);
            float pad = UiKit.Css(7f);
            float gap = UiKit.Css(7f);
            float seg = UiKit.Css(44f);
            float act = UiKit.Css(42f);

            // width = padding + segment(3×44 + 2×3 + 2×3) + gap + up to 4 acts
            float segW = seg * 3f + UiKit.Css(3f) * 4f;
            int acts = (_recolorable ? 4 : 3);
            float width = UiKit.Css(9f) * 2f + segW + gap * acts + act * acts;

            Image pill = UiKit.MakeRounded(root, "Bar", BarBg, 30f);
            _bar = pill.rectTransform;
            _bar.anchorMin = new Vector2(0.5f, 0f);
            _bar.anchorMax = new Vector2(0.5f, 0f);
            _bar.pivot = new Vector2(0.5f, 0f);
            _bar.sizeDelta = new Vector2(width, h + pad * 2f);
            _bar.anchoredPosition = new Vector2(0f, UiKit.Css(22f));

            float x = UiKit.Css(9f);

            // ── mode segment ────────────────────────────────────────────────────
            Image segBg = UiKit.MakeRounded(_bar, "Modes", SegBg, 22f);
            RectTransform srt = segBg.rectTransform;
            srt.anchorMin = new Vector2(0f, 0.5f);
            srt.anchorMax = new Vector2(0f, 0.5f);
            srt.pivot = new Vector2(0f, 0.5f);
            srt.sizeDelta = new Vector2(segW, h + UiKit.Css(6f));
            srt.anchoredPosition = new Vector2(x, 0f);

            AddMode(segBg.rectTransform, Mode.Translate, "move", UiKit.Css(3f), seg, h);
            AddMode(segBg.rectTransform, Mode.Rotate, "rotate", UiKit.Css(3f) + seg + UiKit.Css(3f), seg, h);
            AddMode(segBg.rectTransform, Mode.Scale, "resize",
                    UiKit.Css(3f) + (seg + UiKit.Css(3f)) * 2f, seg, h);
            x += segW + gap;

            if (_recolorable)
            {
                AddAct("Recolor", "palette", UiKit.TextMain, x, act, h, ToggleColorBar);
                x += act + gap;
            }
            AddAct("Dup", "copy", UiKit.TextMain, x, act, h, DoDuplicate);
            x += act + gap;
            AddAct("Del", "trash", DelFg, x, act, h, DoDelete);
            x += act + gap;
            AddAct("Done", "check", UiKit.OnAccent, x, act, h, () => { Dismissed?.Invoke(); Hide(); },
                   UiKit.Accent);

            BuildColorBar(root);
            SetMode(Mode.Translate);
        }

        private void AddMode(RectTransform parent, Mode mode, string icon, float x, float w, float h)
        {
            Image bg = UiKit.MakeRounded(parent, "Mode_" + mode, new Color(0f, 0f, 0f, 0f), 18f);
            var btn = bg.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() => SetMode(mode));

            RectTransform rt = bg.rectTransform;
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, 0f);

            Glyph(bg.transform, icon, UiKit.TextMain, UiKit.Css(18f));
            _modeBg[mode] = bg;
        }

        private void AddAct(string name, string icon, Color tint, float x, float w, float h,
                            UnityEngine.Events.UnityAction onClick, Color? bgColor = null)
        {
            Image bg = UiKit.MakeRounded(_bar, name, bgColor ?? ActBg, 18f);
            var btn = bg.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(onClick);

            RectTransform rt = bg.rectTransform;
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, 0f);

            Glyph(bg.transform, icon, tint, UiKit.Css(17f));
        }

        private static void Glyph(Transform parent, string icon, Color tint, float size)
        {
            Image img = UiKit.MakePanel(parent, "Icon", tint);
            img.sprite = IconPainter.Get(icon);
            img.raycastTarget = false;
            RectTransform rt = img.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = Vector2.zero;
        }

        /// <summary>The eight rock tints plus ↺ "put it back", above the toolbar.</summary>
        private void BuildColorBar(RectTransform root)
        {
            float sw = UiKit.Css(30f);
            float gap = UiKit.Css(8f);
            int n = RockColors.Length + 1;

            Image bar = UiKit.MakeRounded(root, "ColorBar", BarBg, 22f);
            _colorBar = bar.rectTransform;
            _colorBar.anchorMin = new Vector2(0.5f, 0f);
            _colorBar.anchorMax = new Vector2(0.5f, 0f);
            _colorBar.pivot = new Vector2(0.5f, 0f);
            _colorBar.sizeDelta = new Vector2(UiKit.Css(12f) * 2f + n * sw + (n - 1) * gap,
                                              sw + UiKit.Css(8f) * 2f);
            _colorBar.anchoredPosition = new Vector2(0f, UiKit.Css(74f));

            float x = UiKit.Css(12f);

            // ↺ = clear the tint, back to the model's own colours.
            Button orig = Swatch("Original", new Color(1f, 1f, 1f, 0.12f), x, sw,
                                 () => DoRecolor(null));
            Glyph(orig.transform, "undo", UiKit.TextMain, UiKit.Css(15f));
            x += sw + gap;

            foreach (string hex in RockColors)
            {
                string h = hex;
                Swatch("Sw" + h.Substring(1), Hex(h), x, sw, () => DoRecolor(h));
                x += sw + gap;
            }
            _colorBar.gameObject.SetActive(false);
        }

        private Button Swatch(string name, Color color, float x, float size,
                              UnityEngine.Events.UnityAction onClick)
        {
            Image img = UiKit.MakeCircle(_colorBar, name, color);
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);

            RectTransform rt = img.rectTransform;
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = new Vector2(x, 0f);

            Image rim = UiKit.MakeCircle(img.transform, "Rim", new Color(1f, 1f, 1f, 0.5f), 0.07f);
            rim.raycastTarget = false;
            UiKit.Stretch(rim.rectTransform);
            return btn;
        }

        /// <summary>#rrggbb → Color. Only called with the constants above, which are all valid.</summary>
        public static Color Hex(string hex)
        {
            if (!SceneEdit.IsHexColor(hex)) return Color.magenta;
            int r = Convert.ToInt32(hex.Substring(1, 2), 16);
            int g = Convert.ToInt32(hex.Substring(3, 2), 16);
            int b = Convert.ToInt32(hex.Substring(5, 2), 16);
            return new Color(r / 255f, g / 255f, b / 255f, 1f);
        }

        // ── actions ──────────────────────────────────────────────────────────────

        private void SetMode(Mode mode)
        {
            _mode = mode;
            foreach (KeyValuePair<Mode, Image> kv in _modeBg)
                if (kv.Value != null)
                    kv.Value.color = kv.Key == mode ? UiKit.Accent : new Color(0f, 0f, 0f, 0f);

            foreach (KeyValuePair<Mode, Image> kv in _modeBg)
            {
                Transform icon = kv.Value != null ? kv.Value.transform.Find("Icon") : null;
                Image img = icon != null ? icon.GetComponent<Image>() : null;
                if (img != null) img.color = kv.Key == mode ? UiKit.OnAccent : UiKit.TextMain;
            }

            Debug.Log("[Edit] mode=" + mode);
            ModeChanged?.Invoke(mode);
        }

        private void ToggleColorBar()
        {
            if (_colorBar == null) return;
            _colorBar.gameObject.SetActive(!_colorBar.gameObject.activeSelf);
        }

        private void DoRecolor(string hex)
        {
            if (!Edit(items => SceneEdit.Recolor(items, _id, hex))) return;
            Debug.Log($"[Edit] recolor {_id} → {hex ?? "original"}");
        }

        private void DoDuplicate()
        {
            string made = null;
            Edit(items =>
            {
                JObject copy = SceneEdit.Duplicate(items, _id, DateTime.UtcNow.Ticks);
                made = copy != null ? (string)copy["id"] : null;
                return copy != null;
            });
            if (made == null) return;

            Debug.Log($"[Edit] duplicated {_id} → {made}");
            Toast.ShowTr("ก๊อปแล้ว");
            _id = made;   // the web selects the copy, so a second tap makes a third
        }

        private void DoDelete()
        {
            string gone = _id;
            if (!Edit(items => SceneEdit.Delete(items, gone))) return;
            Debug.Log("[Edit] deleted " + gone);
            Toast.ShowTr("ลบแล้ว");
            Dismissed?.Invoke();
            Hide();
        }

        /// <summary>
        /// Run one edit against the live scene, snapshot it for undo, and tell the world.
        /// Returns false when there was no scene or the edit did not apply — the caller must
        /// not then claim it happened.
        /// </summary>
        private bool Edit(Func<JArray, bool> op)
        {
            var boot = FindFirstObjectByType<AppBoot>();
            SceneData scene = boot != null ? boot.CurrentScene : null;
            if (scene == null) { Toast.ShowTr("ยังไม่มีแมพให้แก้"); return false; }

            JArray items = SceneEdit.Items(scene);
            if (!op(items)) return false;

            MapEditor.RecordAndApply(items);
            Edited?.Invoke(_id);
            return true;
        }
    }
}
