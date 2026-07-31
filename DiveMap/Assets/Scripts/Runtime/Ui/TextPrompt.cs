using System;
using UnityEngine;
using UnityEngine.UI;
using DiveMap.Core;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// A one-field modal: title, text box, cancel / save. The RN app gets this from
    /// <c>Modal + TextInput</c> (its rename dialog, <c>map.tsx</c> :419) and the web from
    /// <c>_lgModal</c>; Unity has neither, so this is the shared one.
    ///
    /// Deliberately dumb: it hands back the string and closes. Validation belongs to the caller,
    /// because "what counts as a valid name" is different for a map, an object and a username —
    /// and putting all three in here is how one of them quietly gets the wrong rule.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TextPrompt : MonoBehaviour
    {
        private static readonly Color CardBg = new Color(0.055f, 0.137f, 0.212f, 1f);
        private static readonly Color FieldBg = new Color(0.027f, 0.102f, 0.169f, 1f);
        private static readonly Color CancelBg = new Color(1f, 1f, 1f, 0.10f);

        private static TextPrompt _open;

        private InputField _field;
        private Action<string> _onSave;

        public static bool IsOpen => _open != null;
        /// <summary>QC: what is currently typed.</summary>
        public static string Value => _open != null && _open._field != null ? _open._field.text : null;

        public static void Show(string title, string initial, Action<string> onSave, int maxChars = 60)
        {
            Close();
            RectTransform host = UiShell.Instance != null ? UiShell.Instance.OverlayRoot : null;
            if (host == null) return;

            var go = new GameObject("TextPrompt");
            go.transform.SetParent(host, false);
            var rt = go.AddComponent<RectTransform>();
            UiKit.Stretch(rt);
            var p = go.AddComponent<TextPrompt>();
            p._onSave = onSave;
            p.Build(rt, title, initial, maxChars);
            _open = p;
        }

        public static void Close()
        {
            if (_open == null) return;
            Destroy(_open.gameObject);
            _open = null;
        }

        /// <summary>QC only — type a value and press save without a keyboard.</summary>
        public static void QcSubmit(string value)
        {
            if (_open == null) return;
            if (_open._field != null) _open._field.text = value ?? "";
            _open.Save();
        }

        private void OnDestroy()
        {
            if (_open == this) _open = null;
        }

        private void Build(RectTransform root, string title, string initial, int maxChars)
        {
            Button scrim = UiKit.MakeButton(root, "Scrim", null, 0, new Color(0f, 0f, 0f, 0.55f),
                                            Color.white, Close);
            UiKit.Stretch(scrim.GetComponent<RectTransform>());

            float pad = UiKit.Css(20f);
            Image card = UiKit.MakeRounded(root, "Card", CardBg, 18f);
            RectTransform crt = card.rectTransform;
            crt.anchorMin = new Vector2(0.5f, 0.5f);
            crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = new Vector2(
                Mathf.Min(UiKit.Css(360f), Screen.width / UiKit.CanvasScale - UiKit.Css(48f)), 0f);
            crt.anchoredPosition = Vector2.zero;

            float y = pad;
            int tSize = UiKit.CssFont(16f);
            Text t = UiKit.MakeLine(card.transform, "Title", title, tSize, TextAnchor.UpperLeft, UiKit.TextMain);
            t.fontStyle = FontStyle.Bold;
            Row(t.rectTransform, pad, y, UiKit.RowHeight(tSize));
            y += UiKit.LineHeight(tSize) + UiKit.Css(14f);

            Image box = UiKit.MakeRounded(card.transform, "Field", FieldBg, 12f);
            Row(box.rectTransform, pad, y, UiKit.Css(48f));

            _field = UiKit.MakeInput(box.transform, "Input", "", UiKit.CssFont(15f));
            _field.characterLimit = maxChars;
            _field.text = initial ?? "";
            Image fbg = _field.GetComponent<Image>();
            if (fbg != null) fbg.color = new Color(0f, 0f, 0f, 0f);
            UiKit.Stretch(_field.GetComponent<RectTransform>());
            foreach (Graphic g in new Graphic[] { _field.textComponent, _field.placeholder })
            {
                if (g == null) continue;
                UiKit.Stretch(g.rectTransform);
                g.rectTransform.offsetMin = new Vector2(UiKit.Css(13f), 0f);
                g.rectTransform.offsetMax = new Vector2(-UiKit.Css(13f), 0f);
            }
            y += UiKit.Css(48f) + UiKit.Css(16f);

            float btnH = UiKit.Css(46f);
            float half = (crt.sizeDelta.x - pad * 2f - UiKit.Css(10f)) * 0.5f;

            Button cancel = UiKit.MakeButton(card.transform, "Cancel", UiStrings.Tr("ยกเลิก"),
                                             UiKit.CssFont(14f), CancelBg, UiKit.TextMain, Close);
            Round(cancel);
            RectTransform crt2 = cancel.GetComponent<RectTransform>();
            crt2.anchorMin = new Vector2(0f, 1f);
            crt2.anchorMax = new Vector2(0f, 1f);
            crt2.pivot = new Vector2(0f, 1f);
            crt2.sizeDelta = new Vector2(half, btnH);
            crt2.anchoredPosition = new Vector2(pad, -y);

            Button save = UiKit.MakeButton(card.transform, "Save", UiStrings.Tr("บันทึก"),
                                           UiKit.CssFont(14f), UiKit.Accent, UiKit.OnAccent, Save);
            Round(save);
            Text sl = save.GetComponentInChildren<Text>();
            if (sl != null) sl.fontStyle = FontStyle.Bold;
            RectTransform srt = save.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(1f, 1f);
            srt.anchorMax = new Vector2(1f, 1f);
            srt.pivot = new Vector2(1f, 1f);
            srt.sizeDelta = new Vector2(half, btnH);
            srt.anchoredPosition = new Vector2(-pad, -y);
            y += btnH;

            crt.sizeDelta = new Vector2(crt.sizeDelta.x, y + pad);
        }

        private static void Round(Button b)
        {
            Image img = b.GetComponent<Image>();
            if (img == null) return;
            img.sprite = UiKit.RoundedSprite(13f);
            img.type = Image.Type.Sliced;
        }

        private static void Row(RectTransform rt, float pad, float y, float h)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(-pad * 2f, h);
            rt.anchoredPosition = new Vector2(0f, -y);
        }

        private void Save()
        {
            Action<string> cb = _onSave;
            string value = _field != null ? _field.text : "";
            Close();
            cb?.Invoke(value);
        }
    }
}
