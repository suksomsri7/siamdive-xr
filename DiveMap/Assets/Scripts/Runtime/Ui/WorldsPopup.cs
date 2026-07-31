using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DiveMap.Core;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// "Play Game!" → pick a world. The banner on the hub is not decoration: in the RN app it
    /// calls <c>openWorlds()</c>, which lists every admin-account map (the warp worlds) and
    /// dives straight into the one you tap.
    ///
    /// Ported from siamdive-rn <c>src/app/map.tsx</c> (the <c>wl*</c> styles):
    /// <code>
    ///   backdrop rgba(3,12,20,.72) · card #0d2230 border 1.5 #2a6cff r18 pad 16 maxWidth 380
    ///   title #7fc0ff 800 17 · sub #9fb6c9 11.5 · search #0a1a26 border #1c4a5e r10 h40
    ///   row #12303a border #1c5c6e r10 pad 8 · thumb 46 r8 · name #cfeede 14 600 · ▶ #2fd49b
    /// </code>
    /// The 🌀 in the RN title is dropped: NotoSansThai has no emoji coverage, and a tofu box in
    /// the title is worse than no glyph (same rule as the game HUD badges).
    /// </summary>
    public sealed class WorldsPopup : MonoBehaviour
    {
        private const float CardWidthCss = 380f;
        private const float PadCss = 16f;
        private const float RowHeightCss = 62f;   // 46 thumb + 8 padding top/bottom
        private const float RowGapCss = 7f;
        private const float ListMaxCss = 340f;

        private static readonly Color Backdrop = new Color(0.012f, 0.047f, 0.078f, 0.72f);
        private static readonly Color CardBg = new Color(0.051f, 0.133f, 0.188f, 1f);   // #0d2230
        private static readonly Color CardLine = new Color(0.165f, 0.424f, 1f, 1f);     // #2a6cff
        private static readonly Color TitleFg = new Color(0.498f, 0.753f, 1f, 1f);      // #7fc0ff
        private static readonly Color SearchBg = new Color(0.039f, 0.102f, 0.149f, 1f); // #0a1a26
        private static readonly Color SearchLine = new Color(0.110f, 0.290f, 0.369f, 1f); // #1c4a5e
        private static readonly Color RowBg = new Color(0.071f, 0.188f, 0.227f, 1f);    // #12303a
        private static readonly Color RowLine = new Color(0.110f, 0.361f, 0.431f, 1f);  // #1c5c6e
        private static readonly Color RowFg = new Color(0.812f, 0.933f, 0.871f, 1f);    // #cfeede
        private static readonly Color PlayFg = new Color(0.184f, 0.831f, 0.608f, 1f);   // #2fd49b
        private static readonly Color ThumbBg = new Color(0.063f, 0.192f, 0.290f, 1f);  // #10314a
        private static readonly Color CloseBg = new Color(0.110f, 0.204f, 0.259f, 1f);  // #1c3442

        private ThumbnailCache _thumbs;
        private Action<string> _onPick;
        private RectTransform _content;
        private InputField _search;
        private Text _empty;
        private readonly List<MapCard> _all = new List<MapCard>();
        private readonly List<GameObject> _rows = new List<GameObject>();

        /// <summary>Rows currently listed (QC signal).</summary>
        public int RowCount => _rows.Count;

        /// <summary>
        /// Open the picker over <paramref name="worlds"/> — the caller supplies the cards it
        /// already has, so the popup never issues its own request (the hub's page is the same
        /// <c>/api/dive-sites/public</c> response the RN app filters client-side).
        /// </summary>
        public static WorldsPopup Show(IEnumerable<MapCard> worlds, ThumbnailCache thumbs, Action<string> onPick)
        {
            RectTransform host = UiShell.Instance != null ? UiShell.Instance.OverlayRoot : null;
            if (host == null) return null;

            var go = new GameObject("WorldsPopup");
            go.transform.SetParent(host, false);
            var rt = go.AddComponent<RectTransform>();
            UiKit.Stretch(rt);

            var popup = go.AddComponent<WorldsPopup>();
            popup._thumbs = thumbs;
            popup._onPick = onPick;
            if (worlds != null)
                foreach (MapCard c in worlds)
                    if (MapDirectory.IsOfficial(c)) popup._all.Add(c);
            popup.Build(rt);
            Debug.Log($"[UI] worlds popup open worlds={popup._all.Count}");
            return popup;
        }

        private void Build(RectTransform root)
        {
            Button scrim = UiKit.MakeButton(root, "Backdrop", null, 0, Backdrop, UiKit.TextMain, Close);
            UiKit.Stretch(scrim.GetComponent<RectTransform>());

            float pad = UiKit.Css(PadCss);
            Image card = UiKit.MakeRounded(root, "Card", CardBg, 18f);
            RectTransform crt = card.rectTransform;
            crt.anchorMin = new Vector2(0.5f, 0.5f);
            crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.anchoredPosition = Vector2.zero;

            Image rim = UiKit.MakePanel(card.transform, "Rim", CardLine);
            rim.sprite = UiKit.RoundedSprite(18f, 1.5f);
            rim.type = Image.Type.Sliced;
            rim.raycastTarget = false;
            UiKit.Stretch(rim.rectTransform);

            float y = pad;

            int titleSize = UiKit.CssFont(17f);
            Text title = UiKit.MakeLine(card.transform, "Title", UiStrings.Tr("เล่นเกม!"),
                                        titleSize, TextAnchor.UpperLeft, TitleFg);
            title.fontStyle = FontStyle.Bold;
            TopRow(title.rectTransform, pad, y, UiKit.RowHeight(titleSize));
            y += UiKit.LineHeight(titleSize) + UiKit.Css(2f);

            int subSize = UiKit.CssFont(11.5f);
            Text sub = UiKit.MakeLine(card.transform, "Sub",
                                      UiStrings.Tr("เลือกโลกที่จะดำลง — วาประหว่างกันได้"),
                                      subSize, TextAnchor.UpperLeft, UiKit.TextDim);
            TopRow(sub.rectTransform, pad, y, UiKit.RowHeight(subSize));
            y += UiKit.LineHeight(subSize) + UiKit.Css(10f);

            // search
            Image box = UiKit.MakeRounded(card.transform, "Search", SearchBg, 10f);
            TopRow(box.rectTransform, pad, y, UiKit.Css(40f));
            Image boxRim = UiKit.MakePanel(box.transform, "Rim", SearchLine);
            boxRim.sprite = UiKit.RoundedSprite(10f, 1f);
            boxRim.type = Image.Type.Sliced;
            boxRim.raycastTarget = false;
            UiKit.Stretch(boxRim.rectTransform);

            Image sIcon = UiKit.MakePanel(box.transform, "Icon", UiKit.TextDim);
            sIcon.sprite = IconPainter.Get("search");
            sIcon.raycastTarget = false;
            RectTransform sirt = sIcon.rectTransform;
            sirt.anchorMin = new Vector2(0f, 0.5f);
            sirt.anchorMax = new Vector2(0f, 0.5f);
            sirt.pivot = new Vector2(0f, 0.5f);
            sirt.sizeDelta = new Vector2(UiKit.Css(15f), UiKit.Css(15f));
            sirt.anchoredPosition = new Vector2(UiKit.Css(11f), 0f);

            FlattenInput(_search = UiKit.MakeInput(box.transform, "Field", UiStrings.Tr("ค้นหาโลก…"),
                                                  UiKit.CssFont(14f)),
                         UiKit.Css(11f + 15f + 7f), UiKit.Css(11f));
            _search.onValueChanged.AddListener(_ => Render());
            y += UiKit.Css(40f) + UiKit.Css(8f);

            // list — capped at 340 CSS px like RN's maxHeight, and never taller than it needs.
            float rowH = UiKit.Css(RowHeightCss), rowGap = UiKit.Css(RowGapCss);
            int shown = _all.Count > 0 ? _all.Count : 1;
            float listH = Mathf.Min(UiKit.Css(ListMaxCss), shown * (rowH + rowGap));
            float listTop = y;
            ScrollRect scroll = UiKit.MakeScroll(card.transform, "List", out _content);
            TopRow(scroll.GetComponent<RectTransform>(), pad, y, listH);
            y += listH + UiKit.Css(6f);

            // "no worlds" sits inside the (empty) list area, not below it.
            _empty = UiKit.MakeLine(card.transform, "Empty", UiStrings.Tr("ไม่พบ dive site"),
                                    UiKit.CssFont(13f), TextAnchor.MiddleCenter, UiKit.TextDim);
            TopRow(_empty.rectTransform, pad, listTop + (listH - UiKit.RowHeight(UiKit.CssFont(13f))) * 0.5f,
                   UiKit.RowHeight(UiKit.CssFont(13f)));

            Button close = UiKit.MakeButton(card.transform, "Close", UiStrings.Tr("ยกเลิก"),
                                            UiKit.CssFont(14f), CloseBg, new Color(0.812f, 0.894f, 0.961f, 1f), Close);
            Image cbg = close.GetComponent<Image>();
            if (cbg != null) { cbg.sprite = UiKit.RoundedSprite(10f); cbg.type = Image.Type.Sliced; }
            Text cl = close.GetComponentInChildren<Text>();
            if (cl != null) cl.fontStyle = FontStyle.Bold;
            TopRow(close.GetComponent<RectTransform>(), pad, y, UiKit.Css(42f));
            y += UiKit.Css(42f);

            crt.sizeDelta = new Vector2(Mathf.Min(UiKit.Css(CardWidthCss),
                                                  Screen.width / UiKit.CanvasScale - UiKit.Css(44f)),
                                        y + pad);
            Render();
        }

        private void Render()
        {
            for (int i = 0; i < _rows.Count; i++) if (_rows[i] != null) Destroy(_rows[i]);
            _rows.Clear();

            string q = (_search != null ? _search.text : "").Trim();
            float rowH = UiKit.Css(RowHeightCss), gap = UiKit.Css(RowGapCss);
            int n = 0;

            for (int i = 0; i < _all.Count; i++)
            {
                MapCard c = _all[i];
                string name = MapDirectory.DisplayName(c);
                if (q.Length > 0 && name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                AddRow(c, name, n * (rowH + gap), rowH);
                n++;
            }

            if (_content != null) _content.sizeDelta = new Vector2(0f, n > 0 ? n * (rowH + gap) - gap : 0f);
            if (_empty != null) _empty.gameObject.SetActive(n == 0);
        }

        private void AddRow(MapCard card, string name, float y, float height)
        {
            Button row = UiKit.MakeButton(_content, "World_" + card.ShortId, null, 0, RowBg, UiKit.TextMain,
                                          () => { string id = card.ShortId; Close(); _onPick?.Invoke(id); });
            Image bg = row.GetComponent<Image>();
            if (bg != null) { bg.sprite = UiKit.RoundedSprite(10f); bg.type = Image.Type.Sliced; }

            RectTransform rt = row.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, height);
            rt.anchoredPosition = new Vector2(0f, -y);

            Image rim = UiKit.MakePanel(row.transform, "Rim", RowLine);
            rim.sprite = UiKit.RoundedSprite(10f, 1f);
            rim.type = Image.Type.Sliced;
            rim.raycastTarget = false;
            UiKit.Stretch(rim.rectTransform);

            float pad = UiKit.Css(8f), thumb = UiKit.Css(46f);
            Image plate = UiKit.MakeRounded(row.transform, "Thumb", ThumbBg, 8f);
            RectTransform prt = plate.rectTransform;
            prt.anchorMin = new Vector2(0f, 0.5f);
            prt.anchorMax = new Vector2(0f, 0.5f);
            prt.pivot = new Vector2(0f, 0.5f);
            prt.sizeDelta = new Vector2(thumb, thumb);
            prt.anchoredPosition = new Vector2(pad, 0f);
            plate.raycastTarget = false;

            if (!string.IsNullOrEmpty(card.ThumbUrl) && _thumbs != null)
            {
                RawImage img = UiKit.MakeRaw(plate.transform, "Img", new Color(1f, 1f, 1f, 0f));
                UiKit.Stretch(img.rectTransform);

                Image corners = UiKit.MakePanel(plate.transform, "Corners", RowBg);
                corners.sprite = UiKit.RoundedCutoutSprite(8f);
                corners.type = Image.Type.Sliced;
                corners.raycastTarget = false;
                UiKit.Stretch(corners.rectTransform);

                _thumbs.Request(card.ThumbUrl, tex =>
                {
                    if (img == null || tex == null) return;
                    img.texture = tex;
                    img.color = Color.white;
                });
            }

            int size = UiKit.CssFont(14f);
            Text label = UiKit.MakeLine(row.transform, "Name", name, size, TextAnchor.MiddleLeft, RowFg);
            label.fontStyle = FontStyle.Bold;   // RN wlName fontWeight 600
            RectTransform lrt = label.rectTransform;
            lrt.anchorMin = new Vector2(0f, 0.5f);
            lrt.anchorMax = new Vector2(1f, 0.5f);
            lrt.pivot = new Vector2(0.5f, 0.5f);
            lrt.sizeDelta = new Vector2(-(pad + thumb + UiKit.Css(11f) + UiKit.Css(34f)), UiKit.RowHeight(size));
            lrt.anchoredPosition = new Vector2((pad + thumb + UiKit.Css(11f) - UiKit.Css(34f)) * 0.5f, 0f);

            Image play = UiKit.MakePanel(row.transform, "Play", PlayFg);
            play.sprite = IconPainter.Get("play");
            play.raycastTarget = false;
            RectTransform yrt = play.rectTransform;
            yrt.anchorMin = new Vector2(1f, 0.5f);
            yrt.anchorMax = new Vector2(1f, 0.5f);
            yrt.pivot = new Vector2(1f, 0.5f);
            yrt.sizeDelta = new Vector2(UiKit.Css(16f), UiKit.Css(16f));
            yrt.anchoredPosition = new Vector2(-UiKit.Css(11f), 0f);

            _rows.Add(row.gameObject);
        }

        /// <summary>See <c>MapListScreen.FlattenInput</c> — the 20-unit inset is RAW, not CSS.</summary>
        private static void FlattenInput(InputField input, float left, float right)
        {
            Image bg = input.GetComponent<Image>();
            if (bg != null) bg.color = new Color(0f, 0f, 0f, 0f);

            UiKit.Stretch(input.GetComponent<RectTransform>());
            foreach (Graphic g in new Graphic[] { input.textComponent, input.placeholder })
            {
                if (g == null) continue;
                UiKit.Stretch(g.rectTransform);
                g.rectTransform.offsetMin = new Vector2(left, 0f);
                g.rectTransform.offsetMax = new Vector2(-right, 0f);
            }
        }

        private static void TopRow(RectTransform rt, float pad, float y, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(-pad * 2f, height);
            rt.anchoredPosition = new Vector2(0f, -y);
        }

        public void Close()
        {
            if (this != null && gameObject != null) Destroy(gameObject);
        }
    }
}
