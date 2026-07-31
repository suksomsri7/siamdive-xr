using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// The stand-in for React Native's <c>Alert.alert(title, undefined, buttons)</c>, which is
    /// what the hub uses for its per-card menu (Go To Map / Report / Cancel). Unity has no
    /// native action sheet, so this draws the same thing: a title, a stack of full-width rows,
    /// and a Cancel row, over a scrim that dismisses on tap.
    ///
    /// It is transient and outside the nav stack (like <see cref="Toast"/>), so it lives on
    /// <see cref="UiShell.OverlayRoot"/> and destroys itself on any choice.
    /// </summary>
    public sealed class ActionSheet : MonoBehaviour
    {
        private const float CardWidthCss = 340f;
        private const float RowHeightCss = 48f;
        private const float RowGapCss = 8f;
        private const float PadCss = 16f;

        private RectTransform _card;
        private readonly List<RectTransform> _rows = new List<RectTransform>();
        private float _titleHeight;

        /// <summary>Open an empty sheet. Add rows, then <see cref="AddCancel"/>.</summary>
        public static ActionSheet Show(string title)
        {
            RectTransform host = UiShell.Instance != null ? UiShell.Instance.OverlayRoot : null;
            if (host == null) return null;

            var go = new GameObject("ActionSheet");
            go.transform.SetParent(host, false);
            var rt = go.AddComponent<RectTransform>();
            UiKit.Stretch(rt);
            var sheet = go.AddComponent<ActionSheet>();
            sheet.Build(rt, title);
            return sheet;
        }

        private void Build(RectTransform root, string title)
        {
            Button scrim = UiKit.MakeButton(root, "Scrim", null, 0, new Color(0f, 0f, 0f, 0.5f),
                                            UiKit.TextMain, Close);
            UiKit.Stretch(scrim.GetComponent<RectTransform>());

            Image card = UiKit.MakeRounded(root, "Card", UiKit.PanelBg, 18f);
            _card = card.rectTransform;
            _card.anchorMin = new Vector2(0.5f, 0.5f);
            _card.anchorMax = new Vector2(0.5f, 0.5f);
            _card.pivot = new Vector2(0.5f, 0.5f);
            _card.sizeDelta = new Vector2(UiKit.Css(CardWidthCss), 0f);
            _card.anchoredPosition = Vector2.zero;

            if (!string.IsNullOrEmpty(title))
            {
                int size = UiKit.CssFont(15f);
                _titleHeight = UiKit.RowHeight(size) + UiKit.Css(6f);
                Text t = UiKit.MakeLine(card.transform, "Title", title, size, TextAnchor.MiddleCenter, UiKit.TextMain);
                t.fontStyle = FontStyle.Bold;
                RectTransform trt = t.rectTransform;
                trt.anchorMin = new Vector2(0f, 1f);
                trt.anchorMax = new Vector2(1f, 1f);
                trt.pivot = new Vector2(0.5f, 1f);
                trt.sizeDelta = new Vector2(-UiKit.Css(PadCss) * 2f, UiKit.RowHeight(size));
                trt.anchoredPosition = new Vector2(0f, -UiKit.Css(PadCss));
            }
            Relayout();
        }

        /// <summary>Add a choice. <paramref name="destructive"/> tints it like RN's style:"destructive".</summary>
        public ActionSheet AddItem(string label, System.Action onChoose, bool destructive = false)
        {
            if (_card == null) return this;

            Button b = UiKit.MakeButton(_card, "Item_" + label, label, UiKit.CssFont(15f),
                                        UiKit.CardBg, destructive ? UiKit.Danger : UiKit.TextMain,
                                        () => { Close(); onChoose?.Invoke(); });
            Image bg = b.GetComponent<Image>();
            if (bg != null) { bg.sprite = UiKit.RoundedSprite(12f); bg.type = Image.Type.Sliced; }
            _rows.Add(b.GetComponent<RectTransform>());
            Relayout();
            return this;
        }

        /// <summary>Add the dismiss row and finish the sheet.</summary>
        public ActionSheet AddCancel(string label)
        {
            if (_card == null) return this;

            Button b = UiKit.MakeButton(_card, "Cancel", label, UiKit.CssFont(15f),
                                        new Color(1f, 1f, 1f, 0.04f), UiKit.TextDim, Close);
            Image bg = b.GetComponent<Image>();
            if (bg != null) { bg.sprite = UiKit.RoundedSprite(12f); bg.type = Image.Type.Sliced; }
            _rows.Add(b.GetComponent<RectTransform>());
            Relayout();
            return this;
        }

        /// <summary>Rows are placed by hand (no LayoutGroup) so the height is exact immediately.</summary>
        private void Relayout()
        {
            float pad = UiKit.Css(PadCss);
            float rowH = UiKit.Css(RowHeightCss);
            float gap = UiKit.Css(RowGapCss);
            float y = pad + _titleHeight;

            for (int i = 0; i < _rows.Count; i++)
            {
                RectTransform rt = _rows[i];
                if (rt == null) continue;
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.sizeDelta = new Vector2(-pad * 2f, rowH);
                rt.anchoredPosition = new Vector2(0f, -y);
                y += rowH + gap;
            }

            if (_rows.Count > 0) y -= gap;
            _card.sizeDelta = new Vector2(_card.sizeDelta.x, y + pad);
        }

        public void Close()
        {
            if (this != null && gameObject != null) Destroy(gameObject);
        }
    }
}
