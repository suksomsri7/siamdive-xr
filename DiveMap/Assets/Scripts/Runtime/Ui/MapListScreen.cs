using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using DiveMap.Core;
using DiveMap.Runtime;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// The map hub — the first screen anyone sees.
    ///
    /// Ported element-for-element from the shipped React Native hub
    /// (siamdive-rn <c>src/app/map.tsx</c>), which is what the reference screenshot
    /// <c>docs/refs/web-maplist.png</c> shows. The 05.2 version of this file was a bottom
    /// sheet with 74 px single-column rows and no banner, no create button, no account
    /// button, no likes and no per-card menu — the same data, a different product.
    ///
    /// Every number below is the RN stylesheet value in CSS px, resolved through
    /// <see cref="UiKit.Css"/>; placement inside the grid is <see cref="MapGridLayout"/>:
    /// <code>
    ///   wrap        padding 16 · background #071a2b
    ///   header      44 px row, gap 10 : back(#0e2336) · search(#0e2336 r22) · add(#1c74b0) · account
    ///   arena       #0a3a4a, border 1.5 #1c8fa8, r18, padding 14, coin badge 80, CTA 34 #2fd49b
    ///   card        #0e2336 r17 padding 10 · thumb h100 r12 #155078 · name · "by …" · ♡ n … ⋯
    /// </code>
    ///
    /// Signed out, the list is the public directory (<c>GET /api/dive-sites/public</c>); signed
    /// in it is My Map + favourites, exactly like the RN hub. The signed-out default differs
    /// deliberately: RN shows favourites only, which on a fresh install is an empty screen, and
    /// a viewer whose front page is blank has nothing to view.
    ///
    /// Everything the reference screenshot shows is here, including ☁ (a real on-device copy —
    /// <see cref="DiveMap.Runtime.OfflineStore"/>) and "by You". The + button is the one control
    /// still short of its destination: creating a map needs a name-and-place flow this screen
    /// does not own yet, so it says so rather than opening something half-built.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MapListScreen : MonoBehaviour
    {
        // ── chrome (RN styles, CSS px) ───────────────────────────────────────────
        private static float SidePad => UiKit.Css(MapGridLayout.SidePad);
        /// <summary>RN uses paddingTop 56 to clear the status bar; the shell already insets the safe area.</summary>
        private static float PadTop => UiKit.Css(12f);
        private static float HeaderH => UiKit.Css(44f);
        private static float HeaderGap => UiKit.Css(10f);
        private static float BtnSize => UiKit.Css(44f);

        // ── card (RN s.card / s.thumb / s.cardRow) ───────────────────────────────
        private static float CardPad => UiKit.Css(10f);
        private static float ThumbH => UiKit.Css(100f);
        private static float ThumbGap => UiKit.Css(8f);
        private static float RowH => UiKit.Css(26f);
        private static float RowGap => UiKit.Css(8f);
        private static int NameSize => UiKit.CssFont(14f);   // RN Text default
        private static int MetaSize => UiKit.CssFont(12f);
        private static int CountSize => UiKit.CssFont(12f);

        private const float DebounceSeconds = 0.4f;
        private const int RequestTimeout = 20;

        // Colours the RN sheet names inline (they are hub-only, so they stay here rather
        // than in UiKit's shared web palette).
        private static readonly Color FieldBg = new Color(0.055f, 0.137f, 0.212f, 1f); // #0e2336
        private static readonly Color AddBg = new Color(0.110f, 0.455f, 0.690f, 1f);   // #1c74b0
        private static readonly Color ProfBg = new Color(1f, 1f, 1f, 0.07f);
        private static readonly Color ProfRim = new Color(0.812f, 0.894f, 0.961f, 0.30f); // #cfe4f5 @30%
        private static readonly Color ArenaBg = new Color(0.039f, 0.227f, 0.290f, 1f); // #0a3a4a
        private static readonly Color ArenaLine = new Color(0.110f, 0.561f, 0.659f, 1f); // #1c8fa8
        private static readonly Color ArenaTitle = new Color(1f, 0.835f, 0.290f, 1f);  // #ffd54a
        private static readonly Color ArenaSub = new Color(0.749f, 0.894f, 0.937f, 1f); // #bfe4ef
        private static readonly Color ArenaCta = new Color(0.184f, 0.831f, 0.608f, 1f); // #2fd49b
        private static readonly Color ThumbBg = new Color(0.082f, 0.314f, 0.471f, 1f);  // #155078
        private static readonly Color ThumbEmptyBg = new Color(0.063f, 0.192f, 0.290f, 1f); // #10314a
        private static readonly Color ThumbEmptyIcon = new Color(0.227f, 0.353f, 0.471f, 1f); // #3a5a78
        private static readonly Color HeartOn = new Color(1f, 0.353f, 0.478f, 1f);     // #ff5a7a
        private static readonly Color MenuIcon = new Color(0.812f, 0.886f, 0.949f, 1f); // #cfe2f2

        /// <summary>Raised with the chosen shortId.</summary>
        public event Action<string> MapSelected;

        /// <summary>Raised by the back button (the shell decides what "back" means).</summary>
        public event Action CloseRequested;

        /// <summary>Raised by the "Play Game!" banner — the shell owns the worlds popup.</summary>
        public event Action WorldsRequested;

        private RectTransform _content;
        private ScrollRect _scroll;
        private InputField _search;
        private Text _status;
        private GameObject _retryButton;
        private ThumbnailCache _thumbs;
        private RectTransform _banner;
        private RawImage _coin;

        private readonly List<MapCard> _cards = new List<MapCard>();
        private readonly List<CardView> _views = new List<CardView>();

        private Button _accountBtn;
        private Image _accountBg, _accountIcon, _accountRim;
        private Text _accountInitial;

        /// <summary>shortIds this account owns — the only thing that can say "by You".</summary>
        private readonly HashSet<string> _mine = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _favourites = new HashSet<string>(StringComparer.Ordinal);
        private Coroutine _identity;

        private string _query = "";
        private int _total = -1;
        private bool _loading;
        private bool _built;
        private Coroutine _debounce;
        private Coroutine _fetch;

        /// <summary>Everything about one card that has to survive a language switch or a like.</summary>
        private sealed class CardView
        {
            public MapCard Card;
            public GameObject Root;
            public Text Name;
            public Text Meta;
            public Text LikeCount;
            public Image Heart;
            public int Likes;
        }

        // ── QC / test surface ────────────────────────────────────────────────────
        public int CardCount => _cards.Count;
        public int Total => _total;
        public bool IsLoading => _loading;
        public string Query => _query;
        public string LastError { get; private set; }
        public int ThumbnailsLoaded => _thumbs != null ? _thumbs.LoadedCount : 0;
        /// <summary>True once the coin badge texture is on screen (QC: the banner is complete).</summary>
        public bool BannerReady => _coin != null && _coin.texture != null;
        /// <summary>Cards whose account is the admin warp-world account.</summary>
        public IReadOnlyList<MapCard> Cards => _cards;

        // ── build ────────────────────────────────────────────────────────────────

        public void Build(ThumbnailCache thumbs)
        {
            if (_built) return;
            _built = true;
            _thumbs = thumbs;

            var root = GetComponent<RectTransform>();
            if (root == null) root = gameObject.AddComponent<RectTransform>();
            UiKit.Stretch(root);

            // The hub is a PAGE, not a sheet: it is where the app starts, so there is nothing
            // behind it to keep visible (RN: a plain View with backgroundColor #071a2b).
            Image bg = UiKit.MakePanel(root, "Bg", UiKit.ScreenBg);
            UiKit.Stretch(bg.rectTransform);
            bg.raycastTarget = true;   // swallow taps that miss a card

            BuildHeader(root);
            BuildList(root);
            BuildStatus(root);
        }

        private void BuildHeader(RectTransform root)
        {
            // back · [search] · + · account — a fixed row; the search box takes the slack.
            float x = SidePad;
            Button back = CircleButton(root, "BackButton", "back", FieldBg, UiKit.TextMain,
                                       UiKit.Css(22f), () => CloseRequested?.Invoke());
            Place(back.GetComponent<RectTransform>(), x, BtnSize);
            x += BtnSize + HeaderGap;

            float rightBlock = BtnSize * 2f + HeaderGap * 2f;   // + and account, with their gaps
            float searchW = ScreenWidth() - SidePad * 2f - BtnSize - HeaderGap - rightBlock;

            RectTransform searchBox = BuildSearchBox(root);
            Place(searchBox, x, searchW);
            x += searchW + HeaderGap;

            Button add = CircleButton(root, "AddButton", "plus", AddBg, Color.white,
                                      UiKit.Css(26f), () => Toast.ShowTr("ยังไม่เปิดให้ใช้ในแอปนี้"));
            Place(add.GetComponent<RectTransform>(), x, BtnSize);
            x += BtnSize + HeaderGap;

            // Signed out → the outline person, which opens sign-in. Signed in → the initial on
            // the accent fill, which opens the profile. Same button, RN's two states.
            _accountBtn = CircleButton(root, "AccountButton", "person", ProfBg,
                                       new Color(0.812f, 0.894f, 0.961f, 1f), UiKit.Css(26f),
                                       OnAccountTapped);
            _accountBg = _accountBtn.GetComponent<Image>();
            Transform glyph = _accountBtn.transform.Find("Icon");
            _accountIcon = glyph != null ? glyph.GetComponent<Image>() : null;

            Image rim = UiKit.MakeCircle(_accountBtn.transform, "Rim", ProfRim, 0.035f);
            rim.raycastTarget = false;
            UiKit.Stretch(rim.rectTransform);
            _accountRim = rim;

            _accountInitial = UiKit.MakeLine(_accountBtn.transform, "Initial", "", UiKit.CssFont(18f),
                                             TextAnchor.MiddleCenter, Color.white);
            _accountInitial.fontStyle = FontStyle.Bold;
            UiKit.Stretch(_accountInitial.rectTransform);
            _accountInitial.gameObject.SetActive(false);

            Place(_accountBtn.GetComponent<RectTransform>(), x, BtnSize);
            RenderAccount();
        }

        private void OnAccountTapped()
        {
            if (Account.IsSignedIn) ProfileSheet.Open();
            else LoginSheet.Open();
        }

        /// <summary>Swap the account button between its signed-out and signed-in faces.</summary>
        private void RenderAccount()
        {
            bool on = Account.IsSignedIn;
            if (_accountBg != null) _accountBg.color = on ? AddBg : ProfBg;
            if (_accountRim != null) _accountRim.color = on ? UiKit.Accent : ProfRim;
            if (_accountIcon != null) _accountIcon.gameObject.SetActive(!on);
            if (_accountInitial != null)
            {
                _accountInitial.gameObject.SetActive(on);
                if (on) _accountInitial.text = Account.Initial(Account.Name, Account.Email);
            }
        }

        private RectTransform BuildSearchBox(RectTransform root)
        {
            Image box = UiKit.MakeRounded(root, "SearchBox", FieldBg, 22f);
            RectTransform rt = box.rectTransform;

            // Ionicons "search" at 16, then the field — RN paddingHorizontal 14, gap 8.
            Image icon = UiKit.MakePanel(box.transform, "Icon", UiKit.TextDim);
            icon.sprite = IconPainter.Get("search");
            icon.raycastTarget = false;
            RectTransform irt = icon.rectTransform;
            irt.anchorMin = new Vector2(0f, 0.5f);
            irt.anchorMax = new Vector2(0f, 0.5f);
            irt.pivot = new Vector2(0f, 0.5f);
            irt.sizeDelta = new Vector2(UiKit.Css(16f), UiKit.Css(16f));
            irt.anchoredPosition = new Vector2(UiKit.Css(14f), 0f);

            _search = UiKit.MakeInput(box.transform, "Search",
                                      UiStrings.Tr("ค้นหา dive site สาธารณะ…"), UiKit.CssFont(15f));
            FlattenInput(_search, UiKit.Css(14f) + UiKit.Css(16f) + UiKit.Css(8f), UiKit.Css(14f));
            _search.onValueChanged.AddListener(OnSearchChanged);

            return rt;
        }

        /// <summary>
        /// Drop <see cref="UiKit.MakeInput"/>'s own chrome so the field can sit inside a rounded
        /// box: the plain grey rectangle becomes transparent, and the 20-unit text inset it adds
        /// for a standalone field is replaced by the caller's padding. Those 20 units are RAW
        /// canvas units, not CSS px — compensating for them with <c>Css(20)</c> is off by the
        /// device pixel ratio on every phone, which is exactly the kind of "right on the CI
        /// window, wrong on the handset" bug Css() exists to prevent.
        /// </summary>
        private static void FlattenInput(InputField input, float left, float right)
        {
            Image bg = input.GetComponent<Image>();
            if (bg != null) bg.color = new Color(0f, 0f, 0f, 0f);

            UiKit.Stretch(input.GetComponent<RectTransform>());
            foreach (Graphic g in new Graphic[] { input.textComponent, input.placeholder })
            {
                if (g == null) continue;
                RectTransform rt = g.rectTransform;
                UiKit.Stretch(rt);
                rt.offsetMin = new Vector2(left, 0f);
                rt.offsetMax = new Vector2(-right, 0f);
            }
        }

        private void BuildList(RectTransform root)
        {
            _scroll = UiKit.MakeScroll(root, "List", out _content);
            var srt = _scroll.GetComponent<RectTransform>();
            srt.anchorMin = Vector2.zero;
            srt.anchorMax = Vector2.one;
            srt.pivot = new Vector2(0.5f, 0.5f);
            srt.offsetMin = new Vector2(SidePad, 0f);
            srt.offsetMax = new Vector2(-SidePad, -(PadTop + HeaderH));
            _scroll.onValueChanged.AddListener(OnScrolled);

            BuildBanner();
        }

        /// <summary>The "Play Game!" banner (RN <c>ListHeaderComponent</c>).</summary>
        private void BuildBanner()
        {
            Image card = UiKit.MakeRounded(_content, "Arena", ArenaBg, 18f);
            _banner = card.rectTransform;
            _banner.anchorMin = new Vector2(0f, 1f);
            _banner.anchorMax = new Vector2(1f, 1f);
            _banner.pivot = new Vector2(0.5f, 1f);
            _banner.sizeDelta = new Vector2(0f, UiKit.Css(MapGridLayout.BannerHeight));
            _banner.anchoredPosition = new Vector2(
                0f, -UiKit.Css(MapGridLayout.ListPadTop + MapGridLayout.BannerMarginTop));

            // 1.5 px #1c8fa8 rim (RN borderWidth/borderColor).
            Image rim = UiKit.MakePanel(card.transform, "Rim", ArenaLine);
            rim.sprite = UiKit.RoundedSprite(18f, 1.5f);
            rim.type = Image.Type.Sliced;
            rim.raycastTarget = false;
            UiKit.Stretch(rim.rectTransform);

            // Tapping the banner opens the worlds popup, exactly like RN's openWorlds().
            var btn = card.gameObject.AddComponent<Button>();
            btn.targetGraphic = card;
            btn.onClick.AddListener(() => WorldsRequested?.Invoke());

            float pad = UiKit.Css(MapGridLayout.BannerPad);
            float badge = UiKit.Css(MapGridLayout.BannerBadge);

            // The gold SCUBA DIVING badge. It is a photographic asset — there is no drawing it
            // with strokes — so the same PNG the RN app ships is read from StreamingAssets
            // through the thumbnail cache (the manifest loader proves the URI shape on Android).
            _coin = UiKit.MakeRaw(card.transform, "Badge", new Color(1f, 1f, 1f, 0f));
            RectTransform crt = _coin.rectTransform;
            crt.anchorMin = new Vector2(0f, 0.5f);
            crt.anchorMax = new Vector2(0f, 0.5f);
            crt.pivot = new Vector2(0f, 0.5f);
            crt.sizeDelta = new Vector2(badge, badge);
            crt.anchoredPosition = new Vector2(pad, 0f);
            if (_thumbs != null)
            {
                _thumbs.Request(CoinUri(), tex =>
                {
                    if (_coin == null || tex == null) return;
                    _coin.texture = tex;
                    _coin.color = Color.white;
                });
            }

            float textLeft = pad + badge + UiKit.Css(13f);   // RN arena gap: 13
            float ctaBlock = UiKit.Css(34f) + UiKit.Css(13f) + pad;

            // RN centres the text column (arena alignItems:center), so the two lines are
            // measured as a block and placed around the banner's middle — not from its top.
            int titleSize = UiKit.CssFont(16f);
            int subSize = UiKit.CssFont(12f);
            float lineGap = UiKit.Css(3f);   // arenaSub marginTop
            float block = UiKit.LineHeight(titleSize) + lineGap + UiKit.LineHeight(subSize);
            float top = (UiKit.Css(MapGridLayout.BannerHeight) - block) * 0.5f;

            Text title = UiKit.MakeLine(card.transform, "Title", UiStrings.Tr("เล่นเกม!"),
                                        titleSize, TextAnchor.UpperLeft, ArenaTitle);
            title.fontStyle = FontStyle.Bold;   // RN fontWeight 800
            Row(title.rectTransform, textLeft, ctaBlock, top, UiKit.RowHeight(titleSize));

            Text sub = UiKit.MakeText(card.transform, "Sub",
                                      UiStrings.Tr("ดำลงเก็บเหรียญ เก็บขยะใต้น้ำ"),
                                      subSize, TextAnchor.UpperLeft, ArenaSub);
            Row(sub.rectTransform, textLeft, ctaBlock, top + UiKit.LineHeight(titleSize) + lineGap,
                UiKit.RowHeight(subSize, 2));

            // ▶ CTA: 34 px green circle, right-aligned, filled play glyph at 15.
            Image cta = UiKit.MakeCircle(card.transform, "Cta", ArenaCta);
            cta.raycastTarget = false;
            RectTransform ctart = cta.rectTransform;
            ctart.anchorMin = new Vector2(1f, 0.5f);
            ctart.anchorMax = new Vector2(1f, 0.5f);
            ctart.pivot = new Vector2(1f, 0.5f);
            ctart.sizeDelta = new Vector2(UiKit.Css(34f), UiKit.Css(34f));
            ctart.anchoredPosition = new Vector2(-pad, 0f);

            Image play = UiKit.MakePanel(cta.transform, "Icon", UiKit.OnAccent);
            play.sprite = IconPainter.Get("play");
            play.raycastTarget = false;
            RectTransform prt = play.rectTransform;
            prt.anchorMin = new Vector2(0.5f, 0.5f);
            prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(UiKit.Css(15f), UiKit.Css(15f));
            prt.anchoredPosition = new Vector2(UiKit.Css(1f), 0f);   // optical centre of a triangle
        }

        /// <summary>StreamingAssets URI for the coin badge (jar:// on Android, file:// elsewhere).</summary>
        public static string CoinUri()
        {
            string path = System.IO.Path.Combine(Application.streamingAssetsPath, "coin.png");
            return path.Contains("://") ? path : "file://" + path;
        }

        private void BuildStatus(RectTransform root)
        {
            _status = UiKit.MakeText(root, "Status", "", UiKit.CssFont(13f), TextAnchor.MiddleCenter, UiKit.TextDim);
            var strt = _status.rectTransform;
            strt.anchorMin = new Vector2(0.5f, 0.5f);
            strt.anchorMax = new Vector2(0.5f, 0.5f);
            strt.pivot = new Vector2(0.5f, 0.5f);
            strt.sizeDelta = new Vector2(UiKit.Css(300f), UiKit.Css(56f));
            strt.anchoredPosition = new Vector2(0f, UiKit.Css(18f));

            Button retry = UiKit.MakeButton(root, "RetryButton", UiStrings.Tr("ลองใหม่"), UiKit.CssFont(14f),
                                            UiKit.Accent, UiKit.OnAccent, () => Reload(_query));
            Image rbg = retry.GetComponent<Image>();
            if (rbg != null) { rbg.sprite = UiKit.RoundedSprite(13f); rbg.type = Image.Type.Sliced; }
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
            // Who is signed in decides which cards say "by You", so ask before drawing any.
            if (_identity == null) _identity = StartCoroutine(RefreshIdentity());
            if (_cards.Count == 0 && !_loading) Reload(_query);
        }

        private void OnEnable()
        {
            LoginSheet.SignedIn += OnAccountChanged;
            ProfileSheet.Changed += OnAccountChanged;
        }

        private void OnAccountChanged()
        {
            RenderAccount();
            _mine.Clear();
            _favourites.Clear();
            if (_identity != null) StopCoroutine(_identity);
            _identity = StartCoroutine(RefreshIdentity());
            Reload(_query);
        }

        /// <summary>
        /// Ask the server who owns this device, then which maps are theirs. Runs beside the list
        /// fetch rather than in front of it — a slow /me must not hold up the cards, it only
        /// changes a byline once it lands.
        /// </summary>
        private System.Collections.IEnumerator RefreshIdentity()
        {
            yield return AccountClient.FetchMe((me, changed) =>
            {
                RenderAccount();
                if (changed)
                {
                    // A different account (including a logout): everything scoped to the old one
                    // is now a lie. The RN app calls this syncAcctScope.
                    _mine.Clear();
                    _favourites.Clear();
                }
            });

            yield return AccountClient.MyMaps(cards =>
            {
                _mine.Clear();
                for (int i = 0; i < cards.Count; i++) _mine.Add(cards[i].ShortId);
            });

            yield return AccountClient.Favourites(cards =>
            {
                _favourites.Clear();
                for (int i = 0; i < cards.Count; i++) _favourites.Add(cards[i].ShortId);
            });

            Debug.Log($"[UI] identity signedIn={Account.IsSignedIn} name='{Account.Name}' " +
                      $"mine={_mine.Count} favourites={_favourites.Count}");

            _identity = null;
            // Signed in with an empty search box, the list should be My Map, not the directory.
            if (Account.IsSignedIn && string.IsNullOrEmpty(_query.Trim())) Reload(_query);
            else RefreshLanguage();   // otherwise just re-label the cards already on screen
        }

        /// <summary>Set the search box programmatically (used by the -qcui capture run).</summary>
        public void SetSearch(string q)
        {
            q = q ?? "";
            if (_search != null && _search.text != q) _search.text = q;
            OnSearchChanged(q);
        }

        /// <summary>
        /// The RN hub shows "My Map" (own maps + favourites) by default and switches to the
        /// public directory the moment you type. This app keeps the public directory as the
        /// signed-OUT default rather than RN's favourites-only list, which on a fresh install is
        /// an empty screen — a viewer whose front page is blank has nothing to view. Signed in,
        /// the behaviour is RN's exactly.
        /// </summary>
        public void Reload(string q)
        {
            _query = q ?? "";
            _total = -1;
            LastError = null;
            ClearCards();

            if (Account.IsSignedIn && string.IsNullOrEmpty(_query.Trim()))
                StartMine();
            else
                StartFetch(_query, 0, true);
        }

        private void StartMine()
        {
            if (_fetch != null) { StopCoroutine(_fetch); _fetch = null; }
            _fetch = StartCoroutine(LoadMine());
        }

        /// <summary>My Map = the account's own maps, then favourites that are not already there.</summary>
        private System.Collections.IEnumerator LoadMine()
        {
            _loading = true;
            ShowStatus(UiStrings.Tr("กำลังโหลด…"), false);

            List<MapCard> mine = null, favs = null;
            yield return AccountClient.MyMaps(c => mine = c);
            yield return AccountClient.Favourites(c => favs = c);

            _mine.Clear();
            if (mine != null) for (int i = 0; i < mine.Count; i++) _mine.Add(mine[i].ShortId);
            _favourites.Clear();
            if (favs != null) for (int i = 0; i < favs.Count; i++) _favourites.Add(favs[i].ShortId);

            ClearCards();
            if (mine != null)
                foreach (MapCard c in mine) { _cards.Add(c); AddCardView(c, _cards.Count - 1); }
            if (favs != null)
                foreach (MapCard c in favs)
                {
                    if (_mine.Contains(c.ShortId)) continue;   // already listed as your own
                    _cards.Add(c);
                    AddCardView(c, _cards.Count - 1);
                }

            _total = _cards.Count;   // these routes are not paged; there is no "load more"
            LayoutContent();
            if (_banner != null) _banner.gameObject.SetActive(ShowBanner);

            if (_cards.Count == 0) ShowStatus(UiStrings.Tr("ยังไม่มี dive site"), false);
            else HideStatus();

            Debug.Log($"[UI] my maps mine={_mine.Count} favourites={_favourites.Count} cards={_cards.Count}");
            _loading = false;
            _fetch = null;
        }

        /// <summary>
        /// Re-compose the card bylines after a language switch. UiShell's global Text sweep
        /// cannot do it: "by &lt;owner name&gt;" is assembled from a translated prefix and a
        /// name that is not in the table, so the composed string never matches a key —
        /// the same reason AppBoot re-composes its status line.
        /// </summary>
        public void RefreshLanguage()
        {
            for (int i = 0; i < _views.Count; i++)
            {
                CardView v = _views[i];
                if (v == null || v.Meta == null) continue;
                v.Meta.text = OwnerLine(v.Card);
            }
            if (_search != null && _search.placeholder is Text ph)
                ph.text = UiStrings.Tr("ค้นหา dive site สาธารณะ…");
        }

        private void OnDisable()
        {
            // Static events outlive this screen; a subscription left behind would fire into a
            // destroyed object the next time anybody signs in.
            LoginSheet.SignedIn -= OnAccountChanged;
            ProfileSheet.Changed -= OnAccountChanged;

            // Deactivating the layer kills every running coroutine on this GameObject.
            // Clear the in-flight flags so re-opening the screen starts a fresh request
            // instead of waiting forever on a fetch that Unity already killed.
            _loading = false;
            _fetch = null;
            _debounce = null;
            _identity = null;
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
            if (replace) ShowStatus(UiStrings.Tr("กำลังโหลด…"), false);

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

            // The banner is a "browse" affordance; the web hides it while searching.
            if (_banner != null) _banner.gameObject.SetActive(ShowBanner);

            if (_cards.Count == 0)
                ShowStatus(UiStrings.Tr(string.IsNullOrEmpty(_query)
                                            ? "ยังไม่มี dive site"
                                            : "ไม่พบ dive site"), false);
            else HideStatus();

            _loading = false;
            _fetch = null;

            if (_cards.Count > 0) StartCoroutine(LogCardDiagnostics());
        }

        /// <summary>
        /// One-shot post-layout dump of what a card ACTUALLY rendered. There is no Unity Editor
        /// on this machine, so a CI player log is the only way to tell a data problem from a
        /// text-layout problem: <c>chars=0</c> with a non-empty <c>text</c> means the
        /// TextGenerator threw the line away (rect too short), while an empty <c>text</c> means
        /// the data never arrived.
        /// </summary>
        private IEnumerator LogCardDiagnostics()
        {
            yield return null; // let uGUI rebuild the layout
            yield return null; // …and generate the mesh

            Debug.Log($"[UI] grid cols={MapGridLayout.Columns} cardW={CardWidth():F0} cardH={CardHeight():F0} " +
                      $"banner={(ShowBanner ? "on" : "off")} coin={(BannerReady ? "ok" : "pending")}");

            int n = Mathf.Min(3, _views.Count);
            for (int i = 0; i < n; i++)
            {
                CardView v = _views[i];
                if (v == null || v.Name == null) continue;
                Rect r = v.Name.rectTransform.rect;
                TextGenerator g = v.Name.cachedTextGenerator;
                Debug.Log($"[UI] card{i} name='{v.Name.text}' meta='{(v.Meta != null ? v.Meta.text : null)}' " +
                          $"likes={v.Likes} liked={LikedMaps.IsLiked(v.Card.ShortId)} " +
                          $"owner='{v.Card.OwnerName}' official={MapDirectory.IsOfficial(v.Card)} " +
                          $"rect={r.width:F0}x{r.height:F0} chars={g.characterCountVisible} lines={g.lineCount}");
            }
        }

        // ── view ─────────────────────────────────────────────────────────────────

        private bool ShowBanner => string.IsNullOrEmpty(_query.Trim());

        /// <summary>
        /// Usable width in canvas units. The shell anchors every screen inside the SAFE area,
        /// so a landscape notch must come off the top before the header is measured — using the
        /// raw screen width would push the account button under the cutout.
        /// </summary>
        private static float ScreenWidth()
        {
            Rect safe = Screen.safeArea;
            float w = safe.width > 1f ? safe.width : Screen.width;
            return w / UiKit.CanvasScale;
        }

        private float ListWidth() => ScreenWidth() - SidePad * 2f;

        private float CardWidth() =>
            MapGridLayout.CardWidth(ListWidth(), MapGridLayout.Columns, UiKit.Css(MapGridLayout.Gap));

        /// <summary>
        /// Card height, derived from the real font metrics rather than the RN pixel values.
        /// RN gets ~1.2 × fontSize per line; the bundled NotoSansThai needs 1.511 (two levels of
        /// tone marks plus a below-vowel), so copying RN's 196 px would clip Thai names. The
        /// SPACING rules are RN's — only the line boxes grow.
        /// </summary>
        private float CardHeight() => TextTop() + NameLine() + MetaLine() + UiKit.Css(3f) + RowGap + RowH + CardPad;

        private float TextTop() => CardPad + ThumbH + ThumbGap;
        private float NameLine() => UiKit.LineHeight(NameSize);
        private float MetaLine() => UiKit.LineHeight(MetaSize);

        private float HeaderBlock() =>
            MapGridLayout.HeaderBlock(ShowBanner, UiKit.Css(MapGridLayout.ListPadTop),
                                      UiKit.Css(MapGridLayout.BannerMarginTop),
                                      UiKit.Css(MapGridLayout.BannerHeight),
                                      UiKit.Css(MapGridLayout.Gap));

        private void ClearCards()
        {
            _cards.Clear();
            for (int i = 0; i < _views.Count; i++)
                if (_views[i] != null && _views[i].Root != null) Destroy(_views[i].Root);
            _views.Clear();
            if (_content != null)
            {
                _content.sizeDelta = new Vector2(0f, 0f);
                _content.anchoredPosition = Vector2.zero;
            }
        }

        private void LayoutContent()
        {
            if (_content == null) return;
            _content.sizeDelta = new Vector2(0f, MapGridLayout.ContentHeight(
                _cards.Count, CardHeight(), HeaderBlock(), MapGridLayout.Columns,
                UiKit.Css(MapGridLayout.Gap), UiKit.Css(MapGridLayout.ListPadTop)));
        }

        private void AddCardView(MapCard card, int index)
        {
            float gap = UiKit.Css(MapGridLayout.Gap);
            float w = CardWidth();
            float h = CardHeight();

            Button btn = UiKit.MakeButton(_content, "Card_" + card.ShortId, null, 0, FieldBg, UiKit.TextMain, null);
            Image cardBg = btn.GetComponent<Image>();
            if (cardBg != null) { cardBg.sprite = UiKit.RoundedSprite(17f); cardBg.type = Image.Type.Sliced; }

            string shortId = card.ShortId;
            btn.onClick.AddListener(() =>
            {
                Debug.Log("[UI] map selected " + shortId);
                MapSelected?.Invoke(shortId);
            });

            var rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(
                MapGridLayout.CardX(index, w, MapGridLayout.Columns, gap),
                -MapGridLayout.CardY(index, h, HeaderBlock(), MapGridLayout.Columns, gap));

            var view = new CardView { Card = card, Root = btn.gameObject, Likes = card.LikeCount };

            BuildThumb(btn.transform, card);
            BuildCardText(btn.transform, card, view);
            BuildCardRow(btn.transform, card, view);

            _views.Add(view);
        }

        private void BuildThumb(Transform parent, MapCard card)
        {
            // RN: `thumb { height:100, borderRadius:12 }` on the <Image> itself. uGUI has no
            // corner radius, and a RawImage cannot be 9-sliced, so the rounded plate goes behind
            // the photo and a corner cutout in the CARD's colour goes over it — see
            // UiKit.RoundedCutoutSprite for why this beats a stencil Mask here.
            Image plate = UiKit.MakeRounded(parent, "ThumbPlate", ThumbBg, 12f);
            RectTransform prt = plate.rectTransform;
            prt.anchorMin = new Vector2(0f, 1f);
            prt.anchorMax = new Vector2(1f, 1f);
            prt.pivot = new Vector2(0.5f, 1f);
            prt.sizeDelta = new Vector2(-CardPad * 2f, ThumbH);
            prt.anchoredPosition = new Vector2(0f, -CardPad);
            plate.raycastTarget = false;

            if (string.IsNullOrEmpty(card.ThumbUrl))
            {
                // RN: a #10314a plate with a 26 px image-outline glyph.
                plate.color = ThumbEmptyBg;
                Image glyph = UiKit.MakePanel(plate.transform, "Empty", ThumbEmptyIcon);
                glyph.sprite = IconPainter.Get("image");
                glyph.raycastTarget = false;
                RectTransform grt = glyph.rectTransform;
                grt.anchorMin = new Vector2(0.5f, 0.5f);
                grt.anchorMax = new Vector2(0.5f, 0.5f);
                grt.pivot = new Vector2(0.5f, 0.5f);
                grt.sizeDelta = new Vector2(UiKit.Css(26f), UiKit.Css(26f));
                grt.anchoredPosition = Vector2.zero;
                return;
            }

            RawImage thumb = UiKit.MakeRaw(plate.transform, "Thumb", new Color(1f, 1f, 1f, 0f));
            UiKit.Stretch(thumb.rectTransform);

            // …and the corners come back on top, in the card's own colour.
            Image corners = UiKit.MakePanel(plate.transform, "Corners", FieldBg);
            corners.sprite = UiKit.RoundedCutoutSprite(12f);
            corners.type = Image.Type.Sliced;
            corners.raycastTarget = false;
            UiKit.Stretch(corners.rectTransform);

            if (_thumbs != null)
            {
                _thumbs.Request(card.ThumbUrl, tex =>
                {
                    if (thumb == null || tex == null) return;
                    thumb.texture = tex;
                    thumb.color = Color.white;
                });
            }
        }

        private void BuildCardText(Transform parent, MapCard card, CardView view)
        {
            float top = TextTop();

            Text name = UiKit.MakeLine(parent, "Name", MapDirectory.CardLabel(card), NameSize,
                                       TextAnchor.UpperLeft, UiKit.TextMain);
            name.fontStyle = FontStyle.Bold;   // RN cardName fontWeight 600
            Row(name.rectTransform, CardPad, CardPad, top, UiKit.RowHeight(NameSize));

            Text meta = UiKit.MakeLine(parent, "Meta", OwnerLine(card), MetaSize,
                                       TextAnchor.UpperLeft, UiKit.TextDim);
            Row(meta.rectTransform, CardPad, CardPad, top + NameLine() + UiKit.Css(3f),
                UiKit.RowHeight(MetaSize));

            view.Name = name;
            view.Meta = meta;
        }

        /// <summary>RN <c>s.cardRow</c>: ♡ count … ⋯</summary>
        private void BuildCardRow(Transform parent, MapCard card, CardView view)
        {
            float rowTop = TextTop() + NameLine() + MetaLine() + UiKit.Css(3f) + RowGap;
            bool liked = LikedMaps.IsLiked(card.ShortId);

            // ❤️ like — a hit area rather than a visible button, like RN's Pressable+hitSlop.
            Button like = UiKit.MakeButton(parent, "Like", null, 0, new Color(0f, 0f, 0f, 0f),
                                           UiKit.TextMain, null);
            RectTransform lrt = like.GetComponent<RectTransform>();
            lrt.anchorMin = new Vector2(0f, 1f);
            lrt.anchorMax = new Vector2(0f, 1f);
            lrt.pivot = new Vector2(0f, 1f);
            lrt.sizeDelta = new Vector2(UiKit.Css(52f), RowH);
            lrt.anchoredPosition = new Vector2(CardPad, -rowTop);

            Image heart = UiKit.MakePanel(like.transform, "Heart", liked ? HeartOn : UiKit.TextDim);
            heart.sprite = IconPainter.Get(liked ? "heartfill" : "heart");
            heart.raycastTarget = false;
            RectTransform hrt = heart.rectTransform;
            hrt.anchorMin = new Vector2(0f, 0.5f);
            hrt.anchorMax = new Vector2(0f, 0.5f);
            hrt.pivot = new Vector2(0f, 0.5f);
            hrt.sizeDelta = new Vector2(UiKit.Css(17f), UiKit.Css(17f));
            hrt.anchoredPosition = Vector2.zero;

            Text count = UiKit.MakeLine(like.transform, "Count", card.LikeCount.ToString(), CountSize,
                                        TextAnchor.MiddleLeft, UiKit.TextDim);
            count.fontStyle = FontStyle.Bold;   // RN reactN fontWeight 700
            RectTransform nrt = count.rectTransform;
            nrt.anchorMin = new Vector2(0f, 0.5f);
            nrt.anchorMax = new Vector2(0f, 0.5f);
            nrt.pivot = new Vector2(0f, 0.5f);
            nrt.sizeDelta = new Vector2(UiKit.Css(30f), UiKit.RowHeight(CountSize));
            nrt.anchoredPosition = new Vector2(UiKit.Css(17f) + UiKit.Css(3f), 0f);

            view.Heart = heart;
            view.LikeCount = count;
            like.onClick.AddListener(() => ToggleLike(view));

            // ☁ offline-ready. Shown only for maps that really do have a usable copy on this
            // device — the badge is a promise that the dive opens with no signal, so it must not
            // appear on a map that would open empty.
            if (OfflineStore.Has(card.ShortId))
            {
                Image cloud = UiKit.MakePanel(parent, "Offline", Color.white);
                cloud.sprite = IconPainter.Get("cloud");
                cloud.raycastTarget = false;
                cloud.color = new Color(1f, 1f, 1f, 0.9f);
                RectTransform crt2 = cloud.rectTransform;
                crt2.anchorMin = new Vector2(1f, 1f);
                crt2.anchorMax = new Vector2(1f, 1f);
                crt2.pivot = new Vector2(1f, 1f);
                crt2.sizeDelta = new Vector2(UiKit.Css(16f), UiKit.Css(16f));
                crt2.anchoredPosition = new Vector2(-(CardPad + UiKit.Css(40f)), -(rowTop + UiKit.Css(5f)));
            }

            // ⋯ menu — 30×26, r8, white 7%
            Image menuBg = UiKit.MakeRounded(parent, "Menu", new Color(1f, 1f, 1f, 0.07f), 8f);
            var menu = menuBg.gameObject.AddComponent<Button>();
            menu.targetGraphic = menuBg;
            RectTransform mrt = menuBg.rectTransform;
            mrt.anchorMin = new Vector2(1f, 1f);
            mrt.anchorMax = new Vector2(1f, 1f);
            mrt.pivot = new Vector2(1f, 1f);
            mrt.sizeDelta = new Vector2(UiKit.Css(30f), UiKit.Css(26f));
            mrt.anchoredPosition = new Vector2(-CardPad, -rowTop);

            Image dots = UiKit.MakePanel(menuBg.transform, "Icon", MenuIcon);
            dots.sprite = IconPainter.Get("dots");
            dots.raycastTarget = false;
            RectTransform drt = dots.rectTransform;
            drt.anchorMin = new Vector2(0.5f, 0.5f);
            drt.anchorMax = new Vector2(0.5f, 0.5f);
            drt.pivot = new Vector2(0.5f, 0.5f);
            drt.sizeDelta = new Vector2(UiKit.Css(18f), UiKit.Css(18f));
            drt.anchoredPosition = Vector2.zero;

            menu.onClick.AddListener(() => OpenCardMenu(card));
        }

        /// <summary>The "by …" line. Composed here so a language switch can rebuild it.</summary>
        private string OwnerLine(MapCard card)
        {
            bool isMine = card != null && !string.IsNullOrEmpty(card.ShortId) && _mine.Contains(card.ShortId);
            switch (MapDirectory.OwnerKindOf(card, isMine))
            {
                case OwnerKind.You:       return UiStrings.Tr("สร้างโดย คุณ");
                case OwnerKind.Official:  return UiStrings.Tr("โดย SIAMDIVE");
                case OwnerKind.Named:     return UiStrings.Tr("สร้างโดย") + " " + card.OwnerName.Trim();
                default:                  return UiStrings.Tr("สร้างโดย ชุมชน");
            }
        }

        // ── actions ──────────────────────────────────────────────────────────────

        private void ToggleLike(CardView view)
        {
            if (view == null || view.Card == null) return;
            string id = view.Card.ShortId;
            bool on = !LikedMaps.IsLiked(id);

            // Optimistic, then reconciled with the server's number — the web does the same.
            LikedMaps.Set(id, on);
            view.Likes = Mathf.Max(0, view.Likes + (on ? 1 : -1));
            RenderLike(view, on);

            StartCoroutine(MapReactClient.React(id, on, counts =>
            {
                if (!counts.HasValue || view.LikeCount == null) return;
                view.Likes = counts.Value.Like;
                view.LikeCount.text = view.Likes.ToString();
            }));
        }

        private void RenderLike(CardView view, bool liked)
        {
            if (view.Heart != null)
            {
                view.Heart.sprite = IconPainter.Get(liked ? "heartfill" : "heart");
                view.Heart.color = liked ? HeartOn : UiKit.TextDim;
            }
            if (view.LikeCount != null) view.LikeCount.text = view.Likes.ToString();
        }

        /// <summary>
        /// Per-card ⋯ menu. RN offers Go To Map / View in AR / Rename / Delete / Report; this app
        /// has no sign-in, so nothing here is "mine" — that leaves the two actions a visitor can
        /// take. "View in AR" is dropped because this app IS the AR client.
        /// </summary>
        private void OpenCardMenu(MapCard card)
        {
            ActionSheet sheet = ActionSheet.Show(MapDirectory.DisplayName(card));
            if (sheet == null) return;   // no shell (unit/QC harness) — the card tap still works
            sheet.AddItem(UiStrings.Tr("เปิดแผนที่"), () => MapSelected?.Invoke(card.ShortId));

            // ⭐ favourite = "keep it in My Map". Server-side and keyed by account when signed in,
            // so it follows the diver to their next phone (favorites/route.ts ownerKey).
            bool faved = _favourites.Contains(card.ShortId);
            sheet.AddItem(UiStrings.Tr(faved ? "เอาออกจาก My Map" : "เก็บเข้า My Map"),
                          () => ToggleFavourite(card, !faved));

            sheet.AddItem(UiStrings.Tr("รายงาน"), () => ReportMap(card), true);
            sheet.AddCancel(UiStrings.Tr("ยกเลิก"));
        }

        private void ToggleFavourite(MapCard card, bool on)
        {
            // Optimistic, like the like button: the list re-labels immediately and the server
            // call only corrects it if it fails.
            if (on) _favourites.Add(card.ShortId); else _favourites.Remove(card.ShortId);
            StartCoroutine(AccountClient.ToggleFavourite(card.ShortId, on, ok =>
            {
                if (!ok)
                {
                    if (on) _favourites.Remove(card.ShortId); else _favourites.Add(card.ShortId);
                    Toast.ShowTr("เชื่อมต่อไม่ได้");
                    return;
                }
                Toast.ShowTr(on ? "เก็บเข้า My Map แล้ว" : "เอาออกจาก My Map แล้ว");
            }));
        }

        private void ReportMap(MapCard card)
        {
            StartCoroutine(MapReactClient.Report(card.ShortId, (ok, hidden) =>
            {
                if (!ok) { Toast.ShowTr("ส่งรายงานไม่สำเร็จ"); return; }
                Toast.ShowTr(hidden ? "แมพนี้ถูกซ่อนเพื่อรอตรวจสอบแล้ว" : "ขอบคุณที่รายงาน");
            }));
        }

        // ── small rect helpers ───────────────────────────────────────────────────

        /// <summary>Header slot: left-anchored at <paramref name="x"/>, 44 px tall, below the top pad.</summary>
        private void Place(RectTransform rt, float x, float width)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(width, HeaderH);
            rt.anchoredPosition = new Vector2(x, -PadTop);
        }

        /// <summary>
        /// A text row inside a card/banner: top-anchored at <paramref name="y"/> and stretched
        /// between the <paramref name="left"/> and <paramref name="right"/> insets.
        /// </summary>
        private static void Row(RectTransform rt, float left, float right, float y, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(-(left + right), height);
            rt.anchoredPosition = new Vector2((left - right) * 0.5f, -y);
        }

        private static Button CircleButton(Transform parent, string name, string icon, Color bg,
                                           Color tint, float iconSize, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();

            var img = go.AddComponent<Image>();
            img.sprite = UiKit.CircleSprite();
            img.color = bg;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            if (onClick != null) btn.onClick.AddListener(onClick);

            Image glyph = UiKit.MakePanel(rt, "Icon", tint);
            glyph.sprite = IconPainter.Get(icon);
            glyph.raycastTarget = false;
            RectTransform grt = glyph.rectTransform;
            grt.anchorMin = new Vector2(0.5f, 0.5f);
            grt.anchorMax = new Vector2(0.5f, 0.5f);
            grt.pivot = new Vector2(0.5f, 0.5f);
            grt.sizeDelta = new Vector2(iconSize, iconSize);
            grt.anchoredPosition = Vector2.zero;
            return btn;
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
