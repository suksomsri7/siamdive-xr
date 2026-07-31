using System.Collections.Generic;
using DiveMap.Core;
using UnityEngine;
using UnityEngine.UI;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// D10 — the first-dive coaching, a port of the web's spotlight guide
    /// (<c>_guideRun</c> / <c>_tutTour</c>, builder.html:4169-4238).
    ///
    /// It dims the screen, cuts a hole around ONE control at a time, and explains that control.
    /// The web's own version history records why it ends up this way: v.0665 shipped a single card
    /// listing everything and v.0666 replaced it with the spotlight, because a card that describes
    /// four buttons at once teaches none of them.
    ///
    /// uGUI has no "box-shadow: 0 0 0 4000px" to punch a hole with, and a stencil mask would need
    /// a second material. Four dim panels around the target — above, below, left, right — give
    /// exactly the same picture with plain Images, and the hole is genuinely transparent so the
    /// highlighted control is fully visible and still tappable.
    ///
    /// Shown once per device (<c>sd_tut_tour2</c>), Skip ends it, tapping the dim advances.
    /// Steps whose target is missing or not laid out yet are skipped rather than shown empty —
    /// the coin counter, for instance, only exists on a map with the game running.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TutorialGuide : MonoBehaviour
    {
        public const string TourKey = "sd_tut_tour2";

        private static readonly Color Dim      = new Color(0f, 0f, 6f / 255f, 0.74f);
        private static readonly Color Ring     = new Color(1f, 0.835f, 0.290f, 1f);   // #ffd54a
        private static readonly Color TipBg    = new Color(0.051f, 0.133f, 0.188f, 1f); // #0d2230
        private static readonly Color TipRim   = new Color(0.133f, 0.667f, 0.400f, 1f); // #2a6
        private static readonly Color TipTxt   = new Color(0.812f, 0.933f, 0.871f, 1f); // #cfeede
        private static readonly Color NextBg   = new Color(0.102f, 0.800f, 0.333f, 1f); // #1c5
        private static readonly Color NextTxt  = new Color(0f, 0.267f, 0.133f, 1f);     // #042
        private static readonly Color SkipRim  = new Color(1f, 1f, 1f, 0.30f);

        private struct Step
        {
            public string Target;   // name of the RectTransform to spotlight
            public string Title;
            public string Detail;
        }

        private readonly List<Step> _steps = new List<Step>();
        private int _at = -1;

        private RectTransform _root;
        private RectTransform _layer;
        private Image _top, _bottom, _left, _right, _ring;
        private Text _title, _detail, _count;
        private Button _next;
        private Text _nextLabel;
        private RectTransform _tip;

        /// <summary>Has this device already been shown the dive tutorial?</summary>
        public static bool Seen(string key) => PlayerPrefs.GetInt(key, 0) != 0;

        private static void Mark(string key)
        {
            PlayerPrefs.SetInt(key, 1);
            PlayerPrefs.Save();
        }

        /// <summary>Forget it, so the next dive teaches again (Settings / QC).</summary>
        public static void Forget(string key)
        {
            PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Dismiss any guide on screen. The QC harness needs this: a spotlight over the tour is
        /// the right picture for one shot and ruins every other one.
        /// </summary>
        public static void CloseAny()
        {
            RectTransform layer = HudLayer.For(AppMode.Tour);
            if (layer == null) return;
            TutorialGuide g = layer.GetComponentInChildren<TutorialGuide>(true);
            if (g != null) Destroy(g.gameObject);
        }

        /// <summary>
        /// Coach a first dive. Returns false when there is nothing to do — already seen, no HUD,
        /// or the joysticks have not been laid out yet (the web retries for the same reason: the
        /// sticks are still fading in while the screen rotates).
        /// </summary>
        public static bool StartTour(bool force = false)
        {
            if (!force && Seen(TourKey)) return false;

            RectTransform layer = HudLayer.For(AppMode.Tour);
            if (layer == null) return false;

            // The sticks are the whole point of the first two steps; if they are not on screen
            // yet there is nothing to point at, and marking the tutorial seen would burn it.
            if (Find(layer, "StickL") == null) return false;

            Mark(TourKey);

            RectTransform root = UiKit.MakeNode(layer, "TutorialGuide");
            UiKit.Stretch(root);
            var g = root.gameObject.AddComponent<TutorialGuide>();
            g._layer = layer;
            g.Build(root);

            g._steps.Add(new Step { Target = "StickL", Title = "จอยซ้าย",
                                    Detail = "ลาก ขึ้น/ลง เพื่อลอย-ดำลง · ซ้าย/ขวา เพื่อหันกล้อง" });
            g._steps.Add(new Step { Target = "StickR", Title = "จอยขวา",
                                    Detail = "ลาก หน้า/ถอย เพื่อว่ายไป · ซ้าย/ขวา เพื่อสไลด์ข้าง" });
            g._steps.Add(new Step { Target = "TourShot", Title = "กล้อง",
                                    Detail = "ปุ่มกล้อง: ถ่ายภาพเก็บลงเครื่อง" });
            g._steps.Add(new Step { Target = "TourLight", Title = "ไฟฉาย",
                                    Detail = "เปิดไฟหน้าโดรน มองเห็นตอนดำลึก" });
            g._steps.Add(new Step { Target = "CoinBadge", Title = "เหรียญของคุณ",
                                    Detail = "เก็บขยะและเหรียญทองที่ตกลงมา = ได้เหรียญ เอาไว้ซื้อสัตว์ทะเลในร้านค้า" });
            // The shop step went with the cart button. A spotlight step whose target no longer
            // exists is worse than a missing step: it dims the screen and points at nothing.
            g._steps.Add(new Step { Target = "TourMute", Title = "เสียง",
                                    Detail = "ปิด/เปิดเสียงใต้น้ำ" });
            g._steps.Add(new Step { Target = "TourExit", Title = "ออกทัวร์",
                                    Detail = "กลับไปหน้าแมพเมื่อเที่ยวเสร็จ" });

            Debug.Log($"[Tutorial] tour guide start steps={g._steps.Count}");
            g.Next();
            return true;
        }

        private void Build(RectTransform root)
        {
            _root = root;

            // Four dim panels. The one under everything also swallows taps → "tap the dim = next",
            // which is how the web lets someone rush through without hunting for the button.
            _top = MakeDim(root, "DimTop");
            _bottom = MakeDim(root, "DimBottom");
            _left = MakeDim(root, "DimLeft");
            _right = MakeDim(root, "DimRight");

            // A real RING, not an Outline component: Outline duplicates the graphic's own mesh, so
            // on a fully transparent image it draws a transparent copy — the QC shot came back with
            // the spotlight border missing entirely. RoundedSprite's border argument rasterises the
            // ring into the sprite, which is visible whatever the fill is.
            _ring = UiKit.MakePanel(root, "Spotlight", Ring);
            _ring.sprite = UiKit.RoundedSprite(16f, 2f);
            _ring.type = Image.Type.Sliced;
            _ring.raycastTarget = false;
            _ring.rectTransform.anchorMin = new Vector2(0f, 0f);
            _ring.rectTransform.anchorMax = new Vector2(0f, 0f);
            _ring.rectTransform.pivot = new Vector2(0.5f, 0.5f);

            // Tip bubble: min(76vw, 300) wide.
            float w = Mathf.Min(Screen.width / UiKit.CanvasScale * 0.76f, UiKit.Css(300f));
            Image tip = UiKit.MakeRounded(root, "Tip", TipBg, 13f);
            RectTransform trt = tip.rectTransform;
            trt.anchorMin = new Vector2(0f, 0f);
            trt.anchorMax = new Vector2(0f, 0f);
            trt.pivot = new Vector2(0f, 0f);
            trt.sizeDelta = new Vector2(w, UiKit.Css(120f));
            _tip = trt;

            Image rim = UiKit.MakePanel(trt, "TipRim", TipRim);
            rim.sprite = UiKit.RoundedSprite(13f, 1f);
            rim.type = Image.Type.Sliced;
            rim.raycastTarget = false;
            UiKit.Stretch(rim.rectTransform);

            float pad = UiKit.Css(13f);
            int tFont = UiKit.CssFont(14f);
            int dFont = UiKit.CssFont(13f);

            _title = UiKit.MakeText(trt, "Title", "", tFont, TextAnchor.UpperLeft, Ring);
            RectTransform tirt = _title.rectTransform;
            tirt.anchorMin = new Vector2(0f, 1f);
            tirt.anchorMax = new Vector2(1f, 1f);
            tirt.pivot = new Vector2(0.5f, 1f);
            tirt.offsetMin = new Vector2(pad, 0f);
            tirt.offsetMax = new Vector2(-pad, 0f);
            tirt.sizeDelta = new Vector2(tirt.sizeDelta.x, UiKit.RowHeight(tFont));
            tirt.anchoredPosition = new Vector2(tirt.anchoredPosition.x, -UiKit.Css(12f));

            _detail = UiKit.MakeText(trt, "Detail", "", dFont, TextAnchor.UpperLeft, TipTxt);
            RectTransform drt = _detail.rectTransform;
            drt.anchorMin = new Vector2(0f, 1f);
            drt.anchorMax = new Vector2(1f, 1f);
            drt.pivot = new Vector2(0.5f, 1f);
            drt.offsetMin = new Vector2(pad, 0f);
            drt.offsetMax = new Vector2(-pad, 0f);
            drt.sizeDelta = new Vector2(drt.sizeDelta.x, UiKit.RowHeight(dFont, 3));
            drt.anchoredPosition = new Vector2(drt.anchoredPosition.x,
                                               -(UiKit.Css(12f) + UiKit.RowHeight(tFont) + UiKit.Css(4f)));

            int sFont = UiKit.CssFont(12f);
            float btnH = UiKit.RowHeight(sFont) + UiKit.Css(6f);

            _count = UiKit.MakeLine(trt, "Count", "", UiKit.CssFont(11f),
                                    TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.55f));
            RectTransform crt = _count.rectTransform;
            crt.anchorMin = new Vector2(0f, 0f);
            crt.anchorMax = new Vector2(0f, 0f);
            crt.pivot = new Vector2(0f, 0f);
            crt.sizeDelta = new Vector2(UiKit.Css(60f), btnH);
            crt.anchoredPosition = new Vector2(pad, UiKit.Css(12f));

            Button skip = UiKit.MakeButton(trt, "Skip", UiStrings.Tr("ข้าม"), sFont,
                                           new Color(0f, 0f, 0f, 0f), Color.white, End);
            RectTransform skrt = skip.GetComponent<RectTransform>();
            skrt.anchorMin = new Vector2(1f, 0f);
            skrt.anchorMax = new Vector2(1f, 0f);
            skrt.pivot = new Vector2(1f, 0f);
            skrt.sizeDelta = new Vector2(UiKit.Css(72f), btnH);
            skrt.anchoredPosition = new Vector2(-(pad + UiKit.Css(86f)), UiKit.Css(12f));
            Image skipImg = skip.GetComponent<Image>();
            if (skipImg != null)
            {
                skipImg.color = SkipRim;
                skipImg.sprite = UiKit.RoundedSprite(8f, 1f);
                skipImg.type = Image.Type.Sliced;
            }

            _next = UiKit.MakeButton(trt, "Next", UiStrings.Tr("ถัดไป"), sFont, NextBg, NextTxt, Next);
            RectTransform nrt = _next.GetComponent<RectTransform>();
            nrt.anchorMin = new Vector2(1f, 0f);
            nrt.anchorMax = new Vector2(1f, 0f);
            nrt.pivot = new Vector2(1f, 0f);
            nrt.sizeDelta = new Vector2(UiKit.Css(80f), btnH);
            nrt.anchoredPosition = new Vector2(-pad, UiKit.Css(12f));
            _nextLabel = _next.GetComponentInChildren<Text>();
        }

        private Image MakeDim(RectTransform parent, string name)
        {
            Image img = UiKit.MakePanel(parent, name, Dim);
            img.raycastTarget = true;
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = Color.white;
            colors.pressedColor = Color.white;
            colors.fadeDuration = 0f;
            btn.colors = colors;
            btn.onClick.AddListener(Next);
            RectTransform rt = img.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            return img;
        }

        private void Next()
        {
            _at++;
            Advance();
        }

        /// <summary>Move to the next step that actually has something on screen to point at.</summary>
        private void Advance()
        {
            while (_at < _steps.Count)
            {
                RectTransform target = Find(_layer, _steps[_at].Target);
                if (target != null && target.rect.width > 1f && target.rect.height > 1f)
                {
                    Show(_steps[_at], target);
                    return;
                }
                Debug.Log($"[Tutorial] skipping step '{_steps[_at].Target}' — not on screen");
                _at++;
            }
            End();
        }

        private void Show(Step st, RectTransform target)
        {
            // Target rect in the guide's own space.
            Vector3[] corners = new Vector3[4];
            target.GetWorldCorners(corners);
            Vector2 min = _root.InverseTransformPoint(corners[0]);
            Vector2 max = _root.InverseTransformPoint(corners[2]);

            float pad = UiKit.Css(7f);
            Rect canvas = _root.rect;
            float x1 = min.x - canvas.xMin - pad, x2 = max.x - canvas.xMin + pad;
            float y1 = min.y - canvas.yMin - pad, y2 = max.y - canvas.yMin + pad;
            float W = canvas.width, H = canvas.height;

            Place(_top, 0f, y2, W, Mathf.Max(0f, H - y2));
            Place(_bottom, 0f, 0f, W, Mathf.Max(0f, y1));
            Place(_left, 0f, y1, Mathf.Max(0f, x1), Mathf.Max(0f, y2 - y1));
            Place(_right, x2, y1, Mathf.Max(0f, W - x2), Mathf.Max(0f, y2 - y1));

            _ring.rectTransform.sizeDelta = new Vector2(x2 - x1, y2 - y1);
            _ring.rectTransform.anchoredPosition = new Vector2((x1 + x2) * 0.5f, (y1 + y2) * 0.5f);

            _title.text = UiStrings.Tr(st.Title);
            _detail.text = UiStrings.Tr(st.Detail);
            _count.text = (_at + 1) + " / " + _steps.Count;
            bool last = _at == _steps.Count - 1;
            if (_nextLabel != null) _nextLabel.text = UiStrings.Tr(last ? "เริ่มเลย!" : "ถัดไป");

            // The bubble goes on the roomier side and NEVER over the spotlight — a tip covering
            // the button it is describing is worse than no tip.
            float tw = _tip.sizeDelta.x, th = _tip.sizeDelta.y;
            float tx = Mathf.Clamp((x1 + x2) * 0.5f - tw * 0.5f, UiKit.Css(10f), W - tw - UiKit.Css(10f));
            float ty = ((y1 + y2) * 0.5f > H * 0.5f)
                ? y1 - th - UiKit.Css(14f)
                : y2 + UiKit.Css(14f);
            if (ty < UiKit.Css(10f) || ty + th > H - UiKit.Css(10f) ||
                (tx < x2 && tx + tw > x1 && ty < y2 && ty + th > y1))
            {
                tx = (x1 > W - x2) ? x1 - tw - UiKit.Css(14f) : x2 + UiKit.Css(14f);
                tx = Mathf.Clamp(tx, UiKit.Css(10f), W - tw - UiKit.Css(10f));
                ty = Mathf.Clamp((y1 + y2) * 0.5f - th * 0.5f, UiKit.Css(10f), H - th - UiKit.Css(10f));
            }
            _tip.anchoredPosition = new Vector2(tx, ty);
        }

        private static void Place(Image img, float x, float y, float w, float h)
        {
            if (img == null) return;
            img.rectTransform.anchoredPosition = new Vector2(x, y);
            img.rectTransform.sizeDelta = new Vector2(w, h);
            img.gameObject.SetActive(w > 0.5f && h > 0.5f);
        }

        private void End()
        {
            Debug.Log($"[Tutorial] tour guide end at step {_at + 1}/{_steps.Count}");
            Destroy(gameObject);
        }

        /// <summary>Depth-first search for a named RectTransform, including inactive ones.</summary>
        private static RectTransform Find(Transform where, string name)
        {
            if (where == null) return null;
            for (int i = 0; i < where.childCount; i++)
            {
                Transform c = where.GetChild(i);
                if (c.name == name) return c as RectTransform;
                RectTransform hit = Find(c, name);
                if (hit != null) return hit;
            }
            return null;
        }
    }
}
