using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using DiveMap.Core;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// "รายการแมพ" screen (WO-XR-05.2): a scrollable list of public dive-site maps with
    /// server-side search, pagination and thumbnails.
    ///
    /// Data: <c>GET /api/dive-sites/public?q=&amp;take=30&amp;skip=0</c> (see
    /// <see cref="MapDirectory"/> for the verified contract). Search is debounced ~0.4 s
    /// and re-queries the server — we never filter client-side, otherwise page 2+ would
    /// silently disagree with the server's ordering (likeCount → updatedAt).
    ///
    /// Cards are positioned by hand inside the ScrollRect content (no LayoutGroup): the
    /// row height is fixed, so manual placement is both cheaper and deterministic —
    /// which matters for the headless QC screenshots.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MapListScreen : MonoBehaviour
    {
        // All of these are CSS px, resolved through UiKit.Css at build time: the web's list rows
        // are ~72 px tall with 15 px names and 12 px meta, and the app was drawing 168-unit rows
        // with 36-unit names — the same information at nearly twice the size, which is the most
        // obvious "this is a different app" signal after colour.
        private static float CardHeight => UiKit.Css(74f);
        private static float CardGap => UiKit.Css(8f);
        private const float DebounceSeconds = 0.4f;
        private static float HeaderHeight => UiKit.Css(48f);
        private static float SearchHeight => UiKit.Css(44f);
        private static float SidePad => UiKit.Css(16f);
        private static float ListTop => UiKit.Css(112f);
        private const int RequestTimeout = 20;

        // Card text block. Rows are sized through UiKit.RowHeight(fontSize) — never with
        // a literal, see UiKit.LineHeightRatio for what a too-short row does to Text.
        private static int NameSize => UiKit.CssFont(15f);
        private static int MetaSize => UiKit.CssFont(12f);
        private static float TextLeft => UiKit.Css(108f); // right of the 96 px thumbnail + 8 px inset
        private static float NameTop => UiKit.Css(12f);
        private static float MetaTop => UiKit.Css(38f);

        /// <summary>Raised with the chosen shortId.</summary>
        public event Action<string> MapSelected;

        /// <summary>Raised by the header close button (the shell decides what "back" means).</summary>
        public event Action CloseRequested;

        private RectTransform _content;
        private ScrollRect _scroll;
        private InputField _search;
        private Text _status;
        private GameObject _retryButton;
        private ThumbnailCache _thumbs;

        private readonly List<MapCard> _cards = new List<MapCard>();
        private readonly List<GameObject> _cardViews = new List<GameObject>();
        private readonly List<Text> _cardNames = new List<Text>();

        private string _query = "";
        private int _total = -1;
        private bool _loading;
        private bool _built;
        private Coroutine _debounce;
        private Coroutine _fetch;

        // ── QC / test surface ────────────────────────────────────────────────────
        public int CardCount => _cards.Count;
        public int Total => _total;
        public bool IsLoading => _loading;
        public string Query => _query;
        public string LastError { get; private set; }
        public int ThumbnailsLoaded => _thumbs != null ? _thumbs.LoadedCount : 0;

        // ── build ────────────────────────────────────────────────────────────────

        public void Build(ThumbnailCache thumbs)
        {
            if (_built) return;
            _built = true;
            _thumbs = thumbs;

            var root = GetComponent<RectTransform>();
            if (root == null) root = gameObject.AddComponent<RectTransform>();
            UiKit.Stretch(root);

            // The web shows every list as a bottom SHEET over the live map (#sheet), not as a
            // full-screen page — you keep seeing where you are while you choose.
            RectTransform host = UiKit.MakeSheet(root, "MapSheet",
                                                 () => CloseRequested?.Invoke());

            // Header: title + close.
            Text title = UiKit.MakeText(host, "Title", UiStrings.Tr("รายการแมพ"), UiKit.CssFont(16f),
                                        TextAnchor.MiddleLeft, UiKit.TextMain);
            title.fontStyle = FontStyle.Bold;      // web modal/sheet titles are 600
            UiKit.TopRow(title.rectTransform, UiKit.Css(6f), HeaderHeight - UiKit.Css(6f),
                         SidePad, UiKit.Css(110f));

            Button close = UiKit.MakeButton(host, "CloseButton", UiStrings.Tr("ปิด"), UiKit.CssFont(14f),
                                            UiKit.Accent, UiKit.OnAccent,
                                            () => CloseRequested?.Invoke());
            Image closeBg = close.GetComponent<Image>();
            if (closeBg != null)
            {
                closeBg.sprite = UiKit.RoundedSprite(13f);   // web button radius
                closeBg.type = Image.Type.Sliced;
            }
            UiKit.Anchor(close.GetComponent<RectTransform>(), new Vector2(1f, 1f),
                         new Vector2(UiKit.Css(84f), UiKit.Css(38f)),
                         new Vector2(-SidePad, -UiKit.Css(6f)));

            // Search box (debounced, server-side).
            _search = UiKit.MakeInput(host, "Search", UiStrings.Tr("ค้นหาแมพ"), UiKit.CssFont(15f));
            UiKit.TopRow(_search.GetComponent<RectTransform>(), HeaderHeight + UiKit.Css(8f),
                         SearchHeight, SidePad, SidePad);
            _search.onValueChanged.AddListener(OnSearchChanged);

            // Scrollable card list.
            _scroll = UiKit.MakeScroll(host, "List", out _content);
            var srt = _scroll.GetComponent<RectTransform>();
            srt.anchorMin = Vector2.zero;
            srt.anchorMax = Vector2.one;
            srt.pivot = new Vector2(0.5f, 0.5f);
            srt.offsetMin = new Vector2(SidePad, SidePad);
            srt.offsetMax = new Vector2(-SidePad, -ListTop);
            _scroll.onValueChanged.AddListener(OnScrolled);

            // Status (loading / empty / error) + retry, centred over the list area.
            _status = UiKit.MakeText(host, "Status", "", UiKit.CssFont(13f), TextAnchor.MiddleCenter, UiKit.TextDim);
            var strt = _status.rectTransform;
            strt.anchorMin = new Vector2(0.5f, 0.5f);
            strt.anchorMax = new Vector2(0.5f, 0.5f);
            strt.pivot = new Vector2(0.5f, 0.5f);
            strt.sizeDelta = new Vector2(UiKit.Css(300f), UiKit.Css(56f));
            strt.anchoredPosition = new Vector2(0f, UiKit.Css(18f));

            Button retry = UiKit.MakeButton(host, "RetryButton", UiStrings.Tr("ลองใหม่"), UiKit.CssFont(14f),
                                            UiKit.Accent, UiKit.OnAccent, () => Reload(_query));
            _retryButton = retry.gameObject;
            var rrt = retry.GetComponent<RectTransform>();
            rrt.anchorMin = new Vector2(0.5f, 0.5f);
            rrt.anchorMax = new Vector2(0.5f, 0.5f);
            rrt.pivot = new Vector2(0.5f, 0.5f);
            rrt.sizeDelta = new Vector2(UiKit.Css(120f), UiKit.Css(42f));
            rrt.anchoredPosition = new Vector2(0f, -UiKit.Css(28f));
            _retryButton.SetActive(false);
        }

        // ── public API ───────────────────────────────────────────────────────────

        /// <summary>Load the first page once (no-op if the list already has rows).</summary>
        public void EnsureLoaded()
        {
            if (_cards.Count == 0 && !_loading) Reload(_query);
        }

        /// <summary>Set the search box programmatically (used by the -qcui capture run).</summary>
        public void SetSearch(string q)
        {
            q = q ?? "";
            if (_search != null && _search.text != q) _search.text = q;
            OnSearchChanged(q);
        }

        public void Reload(string q)
        {
            _query = q ?? "";
            _total = -1;
            LastError = null;
            ClearCards();
            StartFetch(_query, 0, true);
        }

        private void OnDisable()
        {
            // Deactivating the layer kills every running coroutine on this GameObject.
            // Clear the in-flight flags so re-opening the screen starts a fresh request
            // instead of waiting forever on a fetch that Unity already killed.
            _loading = false;
            _fetch = null;
            _debounce = null;
        }

        // ── input handlers ───────────────────────────────────────────────────────

        private void OnSearchChanged(string value)
        {
            if (_debounce != null) StopCoroutine(_debounce);
            _debounce = StartCoroutine(DebounceSearch(value ?? ""));
        }

        private IEnumerator DebounceSearch(string value)
        {
            yield return new WaitForSecondsRealtime(DebounceSeconds);
            _debounce = null;
            if (value == _query && _cards.Count > 0) yield break; // nothing changed
            Reload(value);
        }

        private void OnScrolled(Vector2 pos)
        {
            // ScrollRect normalizedPosition.y: 1 = top, 0 = bottom.
            if (pos.y > 0.08f) return;
            TryLoadMore();
        }

        private void TryLoadMore()
        {
            if (_loading || _total < 0) return;
            if (_cards.Count >= _total) return;
            StartFetch(_query, _cards.Count, false);
        }

        // ── networking ───────────────────────────────────────────────────────────

        private void StartFetch(string q, int skip, bool replace)
        {
            if (_fetch != null)
            {
                StopCoroutine(_fetch);
                _fetch = null;
                _loading = false;
            }
            _fetch = StartCoroutine(FetchPage(q, skip, replace));
        }

        private IEnumerator FetchPage(string q, int skip, bool replace)
        {
            _loading = true;
            LastError = null;
            if (replace)
            {
                ShowStatus(UiStrings.Tr("กำลังโหลด…"), false);
            }

            string url = MapDirectory.BuildListUrl(q, MapDirectory.DefaultTake, skip);
            Debug.Log($"[UI] maps fetch {url}");

            string body = null;
            string httpErr = null;

            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                req.timeout = RequestTimeout;
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                    httpErr = $"({(long)req.responseCode}) {req.error}";
                else
                    body = req.downloadHandler != null ? req.downloadHandler.text : null;
            }

            if (httpErr != null || string.IsNullOrEmpty(body))
            {
                _loading = false;
                _fetch = null;
                LastError = httpErr ?? "empty response";
                Debug.LogWarning("[UI] maps fetch failed: " + LastError);
                if (replace) ShowStatus(UiStrings.Tr("โหลดรายการแมพไม่สำเร็จ"), true);
                yield break;
            }

            MapPage page = null;
            string parseErr = null;
            try { page = MapDirectory.ParseList(body); }
            catch (Exception e) { parseErr = e.Message; }

            if (page == null)
            {
                _loading = false;
                _fetch = null;
                LastError = parseErr ?? "parse failed";
                Debug.LogWarning("[UI] maps parse failed: " + LastError);
                if (replace) ShowStatus(UiStrings.Tr("โหลดรายการแมพไม่สำเร็จ"), true);
                yield break;
            }

            if (replace) ClearCards();

            _total = page.Total;
            for (int i = 0; i < page.Cards.Count; i++)
            {
                _cards.Add(page.Cards[i]);
                AddCardView(page.Cards[i], _cards.Count - 1);
            }
            LayoutContent();

            Debug.Log($"[UI] maps page q='{q}' skip={skip} got={page.Cards.Count} total={page.Total}");

            if (_cards.Count == 0) ShowStatus(UiStrings.Tr("ไม่พบแมพ"), false);
            else HideStatus();

            _loading = false;
            _fetch = null;

            if (_cards.Count > 0) StartCoroutine(LogNameDiagnostics());
        }

        /// <summary>
        /// One-shot post-layout dump of what the name row ACTUALLY rendered. There is no
        /// Unity Editor on this machine, so a CI player log is the only way to tell a
        /// data problem from a text-layout problem: <c>chars=0</c> with a non-empty
        /// <c>text</c> means the TextGenerator threw the line away (rect too short),
        /// while an empty <c>text</c> means the data never arrived.
        /// </summary>
        private IEnumerator LogNameDiagnostics()
        {
            yield return null; // let uGUI rebuild the layout
            yield return null; // …and generate the mesh

            int n = Mathf.Min(3, _cardNames.Count);
            for (int i = 0; i < n; i++)
            {
                Text t = _cardNames[i];
                if (t == null) continue;
                Rect r = t.rectTransform.rect;
                TextGenerator g = t.cachedTextGenerator;
                Debug.Log($"[UI] name{i} text='{t.text}' font={(t.font != null ? t.font.name : "NULL")} " +
                          $"size={t.fontSize} fontLine={(t.font != null ? t.font.lineHeight : -1)} " +
                          $"need={UiKit.LineHeight(t.fontSize):F0} rect={r.width:F0}x{r.height:F0} " +
                          $"chars={g.characterCountVisible} lines={g.lineCount} verts={g.vertexCount} " +
                          $"alpha={t.color.a:F2} active={t.isActiveAndEnabled} " +
                          $"h={t.horizontalOverflow} v={t.verticalOverflow}");
            }
        }

        /// <summary>Last path segment of a URL — keeps the QC log readable.</summary>
        private static string Tail(string url)
        {
            if (string.IsNullOrEmpty(url)) return "(none)";
            int i = url.LastIndexOf('/');
            return i >= 0 && i < url.Length - 1 ? "…/" + url.Substring(i + 1) : url;
        }

        // ── view ─────────────────────────────────────────────────────────────────

        private void ClearCards()
        {
            _cards.Clear();
            _cardNames.Clear();
            for (int i = 0; i < _cardViews.Count; i++)
                if (_cardViews[i] != null) Destroy(_cardViews[i]);
            _cardViews.Clear();
            if (_content != null)
            {
                _content.sizeDelta = new Vector2(0f, 0f);
                _content.anchoredPosition = Vector2.zero;
            }
        }

        private void LayoutContent()
        {
            if (_content == null) return;
            float h = _cards.Count * (CardHeight + CardGap);
            _content.sizeDelta = new Vector2(0f, h);
        }

        private void AddCardView(MapCard card, int index)
        {
            Button btn = UiKit.MakeButton(_content, "Card_" + card.ShortId, null, 0,
                                          UiKit.CardBg, UiKit.TextMain, null);
            string shortId = card.ShortId;
            btn.onClick.AddListener(() =>
            {
                Debug.Log("[UI] map selected " + shortId);
                MapSelected?.Invoke(shortId);
            });

            var rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, CardHeight);
            rt.anchoredPosition = new Vector2(0f, -index * (CardHeight + CardGap));

            // Thumbnail (placeholder colour until the CDN image arrives).
            RawImage thumb = UiKit.MakeRaw(btn.transform, "Thumb", new Color(0.13f, 0.22f, 0.27f, 1f));
            var trt = thumb.rectTransform;
            trt.anchorMin = new Vector2(0f, 1f);
            trt.anchorMax = new Vector2(0f, 1f);
            trt.pivot = new Vector2(0f, 1f);
            trt.sizeDelta = new Vector2(UiKit.Css(96f), UiKit.Css(60f));
            trt.anchoredPosition = new Vector2(UiKit.Css(8f), -UiKit.Css(7f));

            if (_thumbs != null && !string.IsNullOrEmpty(card.ThumbUrl))
            {
                _thumbs.Request(card.ThumbUrl, tex =>
                {
                    if (thumb == null || tex == null) return;
                    thumb.texture = tex;
                    thumb.color = Color.white;
                });
            }

            // Name. Row height comes from UiKit.RowHeight — a hard-coded 52 px was too
            // short for one 36 px NotoSansThai line (54.4 px) and the legacy Text
            // component DROPPED the line instead of clipping it, so every card rendered
            // a blank where its name should be (fixed in UiKit: Overflow + RowHeight).
            string label = MapDirectory.CardLabel(card);
            Text name = UiKit.MakeLine(btn.transform, "Name", label, NameSize,
                                       TextAnchor.UpperLeft, UiKit.TextMain);
            StretchRow(name.rectTransform, TextLeft, NameTop, 20f, UiKit.RowHeight(NameSize));

            // Owner · likes.
            string owner = string.IsNullOrEmpty(card.OwnerName)
                ? UiStrings.Tr("ไม่ทราบผู้สร้าง")
                : card.OwnerName;
            string sub = owner + "   ·   " + UiStrings.Tr("ถูกใจ") + " " + card.LikeCount;
            Text meta = UiKit.MakeLine(btn.transform, "Meta", sub, MetaSize, TextAnchor.UpperLeft, UiKit.TextDim);
            StretchRow(meta.rectTransform, TextLeft, MetaTop, 20f, UiKit.RowHeight(MetaSize));

            _cardNames.Add(name);
            if (index < 3)
            {
                Debug.Log($"[UI] card{index} name='{label}' raw='{card.Name}' short={card.ShortId} " +
                          $"thumb={Tail(card.ThumbUrl)} likes={card.LikeCount} owner='{card.OwnerName}'");
            }

            // Teal accent bar on the left edge.
            Image accent = UiKit.MakePanel(btn.transform, "Accent", UiKit.Teal);
            var art = accent.rectTransform;
            art.anchorMin = new Vector2(0f, 0f);
            art.anchorMax = new Vector2(0f, 1f);
            art.pivot = new Vector2(0f, 0.5f);
            art.sizeDelta = new Vector2(6f, 0f);
            art.anchoredPosition = Vector2.zero;
            accent.raycastTarget = false;

            _cardViews.Add(btn.gameObject);
        }

        /// <summary>Left-anchored, right-stretched row inside a card (pivot top-left).</summary>
        private static void StretchRow(RectTransform rt, float x, float y, float rightPad, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(-(x + rightPad), height);
            rt.anchoredPosition = new Vector2(x, -y);
        }

        private void ShowStatus(string text, bool withRetry)
        {
            if (_status != null)
            {
                _status.text = text;
                _status.color = withRetry ? UiKit.Danger : UiKit.TextDim;
                _status.gameObject.SetActive(true);
            }
            if (_retryButton != null) _retryButton.SetActive(withRetry);
        }

        private void HideStatus()
        {
            if (_status != null) _status.gameObject.SetActive(false);
            if (_retryButton != null) _retryButton.SetActive(false);
        }
    }
}
