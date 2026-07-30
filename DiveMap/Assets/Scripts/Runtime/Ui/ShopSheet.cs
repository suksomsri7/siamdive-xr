using System.Collections.Generic;
using DiveMap.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// E5 — the shop, laid out to the web's own inline styles (builder.html:4301-4312):
    ///
    ///   sheet   bottom, full width, max-height 56vh, rgba(8,18,24,.96), top rim 1px #2a6,
    ///           radius 14 14 0 0, padding 10 / 12 / 14
    ///   header  "🛒 ร้านค้า · 🪙 {coins}"  bold 14 mono #ffd54a, close button #333
    ///   rows    two columns (the web's grid is minmax(150px,1fr)), gap 6, radius 8, padding 8/9,
    ///           12 px mono. Affordable: bg #12303a, text #cfeede, rim #2a6, price #ffd54a.
    ///           Too expensive: bg #1a1a1a, text #777, rim #333, price #666.
    ///
    /// Sorted cheapest-first, so what a player sees when the sheet opens is what they can
    /// actually buy. Tapping a row they cannot afford shakes it (the web's ±4 px, 180 ms)
    /// instead of doing nothing, because silence reads as a broken button.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShopSheet : MonoBehaviour
    {
        private static readonly Color SheetBg  = new Color(8f / 255f, 18f / 255f, 24f / 255f, 0.96f);
        private static readonly Color RimGreen = new Color(0.133f, 0.667f, 0.400f, 1f);   // #2a6
        private static readonly Color Gold     = new Color(1f, 0.835f, 0.290f, 1f);       // #ffd54a
        private static readonly Color RowBg    = new Color(0.071f, 0.188f, 0.227f, 1f);   // #12303a
        private static readonly Color RowTxt   = new Color(0.812f, 0.933f, 0.871f, 1f);   // #cfeede
        private static readonly Color DeadBg   = new Color(0.102f, 0.102f, 0.102f, 1f);   // #1a1a1a
        private static readonly Color DeadTxt  = new Color(0.467f, 0.467f, 0.467f, 1f);   // #777
        private static readonly Color DeadRim  = new Color(0.200f, 0.200f, 0.200f, 1f);   // #333
        private static readonly Color DeadGold = new Color(0.400f, 0.400f, 0.400f, 1f);   // #666
        private static readonly Color CloseBg  = new Color(0.200f, 0.200f, 0.200f, 1f);

        private static ShopSheet _open;

        private RectTransform _root;
        private RectTransform _scrim;
        private Text _header;
        private RectTransform _content;
        private readonly List<Row> _rows = new List<Row>();

        private struct Row
        {
            public string Id;
            public Image  Bg;
            public Text   Name;
            public Text   Price;
            public RectTransform Rt;
        }

        /// <summary>Is the shop on screen?</summary>
        public static bool IsOpen => _open != null;

        /// <summary>Open (or re-open, which is how the web refreshes after a purchase).</summary>
        public static void Open()
        {
            if (_open != null) { _open.Refresh(); return; }

            // The shop lives in the tour, but the QC harness (and a future map-screen entry
            // point) opens it from the view mode too — take whichever layer is actually showing.
            RectTransform layer = HudLayer.For(ModeManager.Current) ?? HudLayer.For(AppMode.Tour);
            if (layer == null) return;

            RectTransform root = UiKit.MakeNode(layer, "ShopSheet");
            UiKit.Stretch(root);
            var sheet = root.gameObject.AddComponent<ShopSheet>();
            sheet.Build(root);
            _open = sheet;
        }

        public static void Close()
        {
            if (_open == null) return;
            Destroy(_open.gameObject);
            _open = null;
        }

        private void OnDestroy()
        {
            if (_open == this) _open = null;
        }

        private void Build(RectTransform root)
        {
            _root = root;

            // Scrim: tapping outside closes, the same as every other sheet in the app.
            Button scrim = UiKit.MakeButton(root, "ShopScrim", null, 0,
                                            new Color(0f, 0f, 0f, 0.42f), Color.white, Close);
            _scrim = scrim.GetComponent<RectTransform>();
            UiKit.Stretch(_scrim);

            // The sheet itself: bottom, full width, 56 % of the screen (web max-height:56vh).
            Image panel = UiKit.MakeRounded(root, "ShopPanel", SheetBg, 14f);
            RectTransform prt = panel.rectTransform;
            prt.anchorMin = new Vector2(0f, 0f);
            prt.anchorMax = new Vector2(1f, 0f);
            prt.pivot = new Vector2(0.5f, 0f);
            float h = Screen.height / UiKit.CanvasScale * 0.56f;
            prt.sizeDelta = new Vector2(0f, h);
            prt.anchoredPosition = Vector2.zero;

            // Top rim, 1 px #2a6 — the one green line that says "shop" on the web.
            Image rim = UiKit.MakePanel(prt, "Rim", RimGreen);
            rim.raycastTarget = false;
            RectTransform rrt = rim.rectTransform;
            rrt.anchorMin = new Vector2(0f, 1f);
            rrt.anchorMax = new Vector2(1f, 1f);
            rrt.pivot = new Vector2(0.5f, 1f);
            rrt.sizeDelta = new Vector2(0f, Mathf.Max(1f, UiKit.Css(1f)));
            rrt.anchoredPosition = Vector2.zero;

            int hFont = UiKit.CssFont(14f);
            float pad = UiKit.Css(12f);
            float headH = UiKit.RowHeight(hFont);

            _header = UiKit.MakeLine(prt, "ShopTitle", "", hFont, TextAnchor.MiddleLeft, Gold);
            RectTransform hrt = _header.rectTransform;
            hrt.anchorMin = new Vector2(0f, 1f);
            hrt.anchorMax = new Vector2(1f, 1f);
            hrt.pivot = new Vector2(0.5f, 1f);
            hrt.offsetMin = new Vector2(pad, 0f);
            hrt.offsetMax = new Vector2(-UiKit.Css(96f), 0f);
            hrt.sizeDelta = new Vector2(hrt.sizeDelta.x, headH);
            hrt.anchoredPosition = new Vector2(hrt.anchoredPosition.x, -UiKit.Css(10f));

            Button close = UiKit.MakeButton(prt, "ShopClose", UiStrings.Tr("ปิด"),
                                            UiKit.CssFont(12f), CloseBg, Color.white, Close);
            RectTransform crt = close.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(1f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(1f, 1f);
            crt.sizeDelta = new Vector2(UiKit.Css(74f), UiKit.RowHeight(UiKit.CssFont(12f)));
            crt.anchoredPosition = new Vector2(-pad, -UiKit.Css(10f));

            ScrollRect scroll = UiKit.MakeScroll(prt, "ShopScroll", out RectTransform content);
            _content = content;
            RectTransform srt = scroll.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0f, 0f);
            srt.anchorMax = new Vector2(1f, 1f);
            srt.offsetMin = new Vector2(pad, UiKit.Css(14f));
            srt.offsetMax = new Vector2(-pad, -(UiKit.Css(10f) + headH + UiKit.Css(8f)));

            BuildRows();
            Refresh();

            Debug.Log($"[Shop] open items={Shop.Count} coins={TrashGameSystem.Coins} " +
                      $"cheapest={Shop.PriceOf(Shop.Catalogue[0])}");
        }

        /// <summary>
        /// Two columns of fixed-height rows, positioned by hand. The web uses a CSS grid of
        /// minmax(150px,1fr); at phone widths that resolves to two columns, which is what this
        /// reproduces without dragging a LayoutGroup into a sheet that never resizes.
        /// </summary>
        private void BuildRows()
        {
            const int cols = 2;
            float gap = UiKit.Css(6f);
            float rowH = UiKit.RowHeight(UiKit.CssFont(12f)) + UiKit.Css(16f);   // padding 8 top+bottom
            // Screen width, not _root.rect.width: the layout has not run yet when this is called,
            // so the rect is still zero and every row would come out invisible.
            float width = (Screen.width / UiKit.CanvasScale - UiKit.Css(24f) - gap) / cols;

            IReadOnlyList<string> cat = Shop.Catalogue;
            for (int i = 0; i < cat.Count; i++)
            {
                string id = cat[i];
                int col = i % cols, row = i / cols;

                Image bg = UiKit.MakeRounded(_content, "Row" + i, RowBg, 8f);
                RectTransform rt = bg.rectTransform;
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.sizeDelta = new Vector2(width, rowH);
                rt.anchoredPosition = new Vector2(col * (width + gap), -row * (rowH + gap));

                var btn = bg.gameObject.AddComponent<Button>();
                btn.targetGraphic = bg;
                string captured = id;
                btn.onClick.AddListener(() => TryBuy(captured));

                Text nm = UiKit.MakeLine(rt, "Name", DisplayName(id), UiKit.CssFont(12f),
                                         TextAnchor.MiddleLeft, RowTxt);
                RectTransform nrt = nm.rectTransform;
                UiKit.Stretch(nrt);
                nrt.offsetMin = new Vector2(UiKit.Css(9f), 0f);
                nrt.offsetMax = new Vector2(-UiKit.Css(52f), 0f);

                Text pr = UiKit.MakeLine(rt, "Price", "", UiKit.CssFont(12f),
                                         TextAnchor.MiddleRight, Gold);
                RectTransform rrt2 = pr.rectTransform;
                UiKit.Stretch(rrt2);
                rrt2.offsetMin = new Vector2(-UiKit.Css(52f), 0f);
                rrt2.offsetMax = new Vector2(-UiKit.Css(9f), 0f);

                _rows.Add(new Row { Id = id, Bg = bg, Name = nm, Price = pr, Rt = rt });
            }

            int rowsTall = (cat.Count + cols - 1) / cols;
            _content.sizeDelta = new Vector2(0f, rowsTall * (rowH + gap));
        }

        /// <summary>Repaint prices and affordability against the current balance.</summary>
        private void Refresh()
        {
            int coins = TrashGameSystem.Coins;
            if (_header != null)
                _header.text = UiStrings.Tr("ร้านค้า") + " · " + coins;

            for (int i = 0; i < _rows.Count; i++)
            {
                Row r = _rows[i];
                int price = Shop.PriceOf(r.Id);
                bool afford = coins >= price;
                r.Bg.color = afford ? RowBg : DeadBg;
                r.Name.color = afford ? RowTxt : DeadTxt;
                r.Price.color = afford ? Gold : DeadGold;
                r.Price.text = price.ToString();
            }
        }

        private void TryBuy(string id)
        {
            int before = TrashGameSystem.Coins;
            int after = Shop.Buy(before, id, out bool bought);
            if (!bought)
            {
                Shake(id);
                Toast.ShowTr("เหรียญไม่พอ");
                return;
            }

            TrashGameSystem.Coins = after;
            WalletClient.Spend(before - after);   // queued, debounced, re-queued on failure
            CoinCounter.Show(after);
            AudioBank.PlaySfx("coin");
            Debug.Log($"[Shop] bought {id} for {before - after} → coins={after}");

            Release(id);
            Refresh();
        }

        /// <summary>
        /// Put the purchase in the water: record it against this map, then rebuild the map so it
        /// is built by the same pipeline as every other item. The rebuild costs a few seconds and
        /// drops the player out of the tour, which is why the toast says so plainly rather than
        /// letting the screen go quiet on them.
        /// </summary>
        private static void Release(string assetId)
        {
            var boot = FindFirstObjectByType<AppBoot>();
            if (boot == null)
            {
                Toast.ShowTr("ซื้อแล้ว");
                Debug.LogWarning("[Shop] no AppBoot — the purchase is stored but cannot be placed now");
                return;
            }

            Camera cam = Camera.main;
            Vector3 at = cam != null ? cam.transform.position : Vector3.zero;
            float yaw = cam != null
                ? Mathf.Atan2(cam.transform.forward.z, cam.transform.forward.x)
                : 0f;

            ShopStock.DropPoint(at.x, at.y, at.z, yaw, ShopStock.DropDistance,
                                out double x, out double y, out double z);

            // A stamp rather than a counter: two purchases in the same second must still get
            // different ids, or Inject's duplicate guard would drop the second one.
            long stamp = System.DateTime.UtcNow.Ticks;
            JObject item = ShopStock.MakeItem(assetId, x, y, z, yaw, 1.0, stamp);
            ShopStock.Add(boot.CurrentMapId, item);
            Debug.Log($"[Shop] released {assetId} at ({x:F0},{y:F0},{z:F0}) on map {boot.CurrentMapId}");

            Close();
            Toast.ShowTr("ปล่อยลงแมพแล้ว — กำลังโหลดใหม่");
            if (ModeManager.Instance != null) ModeManager.Instance.Exit();
            boot.ReloadCurrentMap();
        }

        /// <summary>The web's ±4 px, 180 ms shake on a row you cannot afford.</summary>
        private void Shake(string id)
        {
            for (int i = 0; i < _rows.Count; i++)
                if (_rows[i].Id == id) { StartCoroutine(ShakeRow(_rows[i].Rt)); return; }
        }

        private static System.Collections.IEnumerator ShakeRow(RectTransform rt)
        {
            if (rt == null) yield break;
            Vector2 home = rt.anchoredPosition;
            float dx = UiKit.Css(4f);
            float[] steps = { -dx, dx, 0f };
            foreach (float s in steps)
            {
                rt.anchoredPosition = new Vector2(home.x + s, home.y);
                yield return new WaitForSecondsRealtime(0.06f);
            }
            rt.anchoredPosition = home;
        }

        /// <summary>
        /// A readable name from an asset id: "losin:clownfish_two_band" → "Clownfish two band".
        /// The web reads <c>a.name</c> out of its PALETTE, which this app does not carry; the id
        /// is the honest fallback and still tells a player what they are buying.
        /// </summary>
        private static string DisplayName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            int colon = id.IndexOf(':');
            string tail = colon >= 0 && colon + 1 < id.Length ? id.Substring(colon + 1) : id;
            tail = tail.Replace('_', ' ');
            if (tail.Length > 0) tail = char.ToUpperInvariant(tail[0]) + tail.Substring(1);
            if (id.StartsWith("school:", System.StringComparison.Ordinal)) tail += " (school)";
            else if (id.StartsWith("pod:", System.StringComparison.Ordinal)) tail += " (pod)";
            return tail;
        }
    }
}
