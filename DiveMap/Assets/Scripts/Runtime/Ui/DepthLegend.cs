using DiveMap.Core;
using UnityEngine;
using UnityEngine.UI;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// The web's depth legend (#depthLegend, builder.html:257-259): a 14×118 px bar carrying the
    /// same ramp as the map, at left 12 / bottom 70, with 10 px bold labels above and below. It is
    /// shown only while the heat-map is on, and never in the tour — the web hides it there.
    ///
    /// The bar is a generated texture rather than three stacked Images so it is literally the same
    /// <see cref="DepthPalette"/> the seabed is painted with: a legend that disagrees with its map
    /// is worse than no legend.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DepthLegend : MonoBehaviour
    {
        public static DepthLegend Instance { get; private set; }

        private RectTransform _rt;

        public static DepthLegend Create(RectTransform parent)
        {
            if (parent == null) return null;

            RectTransform root = UiKit.MakeNode(parent, "DepthLegend");
            root.anchorMin = new Vector2(0f, 0f);
            root.anchorMax = new Vector2(0f, 0f);
            root.pivot = new Vector2(0f, 0f);
            root.sizeDelta = new Vector2(UiKit.Css(40f), UiKit.Css(160f));
            root.anchoredPosition = new Vector2(UiKit.Css(12f), UiKit.Css(70f));

            // 0 m label on top, 100 m underneath — shallow at the top, like the bar.
            Text top = UiKit.MakeText(root, "Top", "0 " + UiStrings.Tr("ม."), UiKit.CssFont(10f),
                                      TextAnchor.UpperCenter, new Color(0.863f, 0.937f, 1f));
            top.fontStyle = FontStyle.Bold;
            UiKit.Stretch(top.rectTransform);
            top.rectTransform.offsetMin = new Vector2(0f, UiKit.Css(140f));

            Image bar = UiKit.MakeRounded(root, "Bar", Color.white, 7f);
            bar.sprite = null;                       // the ramp texture IS the image
            bar.type = Image.Type.Simple;
            bar.raycastTarget = false;
            var brt = bar.rectTransform;
            brt.anchorMin = new Vector2(0.5f, 0f);
            brt.anchorMax = new Vector2(0.5f, 0f);
            brt.pivot = new Vector2(0.5f, 0f);
            brt.sizeDelta = new Vector2(UiKit.Css(14f), UiKit.Css(118f));
            brt.anchoredPosition = new Vector2(0f, UiKit.Css(16f));
            bar.sprite = RampSprite();

            Text bottom = UiKit.MakeText(root, "Bottom", "100 " + UiStrings.Tr("ม."), UiKit.CssFont(10f),
                                         TextAnchor.LowerCenter, new Color(0.863f, 0.937f, 1f));
            bottom.fontStyle = FontStyle.Bold;
            UiKit.Stretch(bottom.rectTransform);
            bottom.rectTransform.offsetMax = new Vector2(0f, -UiKit.Css(140f));

            var legend = root.gameObject.AddComponent<DepthLegend>();
            legend._rt = root;
            Instance = legend;
            root.gameObject.SetActive(false);
            return legend;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void SetVisible(bool visible)
        {
            if (_rt != null) _rt.gameObject.SetActive(visible);
        }

        private static Sprite _ramp;
        private static Sprite RampSprite()
        {
            if (_ramp != null) return _ramp;
            const int w = 8, h = 128;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = "DepthRamp",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                // Texture v=0 is the bottom row = the deep end.
                float t = 1f - y / (float)(h - 1);
                DepthPalette.Rgb c = DepthPalette.Color(t);
                var c32 = new Color32(
                    (byte)Mathf.Clamp(Mathf.RoundToInt(c.R * 255f), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(c.G * 255f), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(c.B * 255f), 0, 255),
                    255);
                for (int x = 0; x < w; x++) px[y * w + x] = c32;
            }
            tex.SetPixels32(px);
            tex.Apply();
            _ramp = Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f));
            return _ramp;
        }
    }
}
