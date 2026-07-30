using UnityEngine;
using UnityEngine.UI;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// The web's compass (#compass, builder.html:57-60): a 48 px glass circle on the RIGHT edge,
    /// 80 px above the bottom, holding a two-triangle needle whose north half is red (#ff3b30) and
    /// south half pale (#e9f2fa). It rotates to the camera's heading, so "which way am I facing"
    /// is answered the same way in both products.
    ///
    /// Built as two half-needle images rather than one icon so the north half can be red — the same
    /// trick the web plays with two &lt;polygon&gt; elements.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompassWidget : MonoBehaviour
    {
        private static readonly Color North = new Color(1f, 0.231f, 0.188f, 1f);  // #ff3b30
        private static readonly Color South = new Color(0.914f, 0.949f, 0.980f, 1f); // #e9f2fa

        private RectTransform _needle;
        private Camera _cam;
        private RectTransform _rt;
        private Image _dial;
        private Image _rim;

        public static CompassWidget Instance { get; private set; }

        /// <summary>Add the compass to <paramref name="parent"/> at the web's position.</summary>
        public static CompassWidget Create(RectTransform parent)
        {
            if (parent == null) return null;

            Image dial = UiKit.MakeCircle(parent, "Compass", UiKit.Glass);
            dial.raycastTarget = false;
            RectTransform rt = dial.rectTransform;
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            // Pivot AT the corner, like UiKit.Anchor: with a centred pivot the 96 px circle hung
            // half off the right edge (the first QC shot showed a clipped needle).
            rt.pivot = new Vector2(1f, 0f);
            rt.sizeDelta = new Vector2(UiKit.Css(48f), UiKit.Css(48f));
            // #compass: right 12, bottom 80 + safe-area (builder.html:57).
            rt.anchoredPosition = new Vector2(-UiKit.Css(12f), UiKit.Css(80f));

            Image rim = UiKit.MakeCircle(rt, "Rim", UiKit.Line, 0.035f);
            rim.raycastTarget = false;
            UiKit.Stretch(rim.rectTransform);

            // The needle: one node holding both halves, rotated as a unit.
            RectTransform needle = UiKit.MakeNode(rt, "Needle");
            needle.anchorMin = new Vector2(0.5f, 0.5f);
            needle.anchorMax = new Vector2(0.5f, 0.5f);
            needle.pivot = new Vector2(0.5f, 0.5f);
            needle.sizeDelta = new Vector2(UiKit.Css(30f), UiKit.Css(30f));   // web svg 30px
            needle.anchoredPosition = Vector2.zero;

            AddHalf(needle, "N", North, false);
            AddHalf(needle, "S", South, true);

            var c = dial.gameObject.AddComponent<CompassWidget>();
            c._needle = needle;
            c._rt = rt;
            c._dial = dial;
            c._rim = rim;
            Instance = c;
            c.SetTourLayout(false);
            return c;
        }

        private static void AddHalf(RectTransform parent, string name, Color color, bool flipped)
        {
            Image img = UiKit.MakePanel(parent, name, color);
            img.raycastTarget = false;
            img.sprite = IconPainter.Get("needle");
            img.type = Image.Type.Simple;
            RectTransform rt = img.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0f);          // rotate about the dial centre
            rt.sizeDelta = new Vector2(UiKit.Css(13f), UiKit.Css(14f));
            rt.anchoredPosition = Vector2.zero;
            rt.localRotation = Quaternion.Euler(0f, 0f, flipped ? 180f : 0f);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Hide while the ☰ column is expanded: both live on the right rail (the web puts
        /// #viewbtns at bottom 20 and #compass at bottom 80, so an open column runs straight
        /// through it) and the column is the transient one.
        /// </summary>
        public void SetVisible(bool visible)
        {
            if (_rt != null) _rt.gameObject.SetActive(visible);
        }

        /// <summary>
        /// The web does NOT hide the compass in the tour — it MOVES it (builder.html:234):
        /// <c>body.tour #compass{right:138px; top:max(15px,safe); 44×44; bg rgba(7,26,42,.5);
        /// border:2px rgba(255,255,255,.8)}</c>, i.e. up beside the depth pill, 138 px in from the
        /// right so it clears it. In the map view it sits at right 12 / bottom 80 at 48 px.
        /// </summary>
        public void SetTourLayout(bool tour)
        {
            if (_rt == null) return;

            float size = UiKit.Css(tour ? 44f : 48f);
            _rt.sizeDelta = new Vector2(size, size);

            if (tour)
            {
                _rt.anchorMin = _rt.anchorMax = _rt.pivot = new Vector2(1f, 1f);
                _rt.anchoredPosition = new Vector2(-UiKit.Css(138f), -UiKit.Css(15f));
            }
            else
            {
                _rt.anchorMin = _rt.anchorMax = _rt.pivot = new Vector2(1f, 0f);
                _rt.anchoredPosition = new Vector2(-UiKit.Css(12f), UiKit.Css(80f));
            }

            if (_dial != null)
                _dial.color = tour ? new Color(0.027f, 0.102f, 0.165f, 0.50f) : UiKit.Glass;
            if (_rim != null)
            {
                // 2 px rim in the tour, hairline in the map view.
                _rim.color = tour ? new Color(1f, 1f, 1f, 0.80f) : UiKit.Line;
                _rim.sprite = UiKit.CircleSprite(tour ? Mathf.Clamp(2f / 22f, 0.02f, 0.5f) : 0.035f);
            }
            if (_needle != null)
            {
                float n = UiKit.Css(tour ? 27f : 30f);
                _needle.sizeDelta = new Vector2(n, n);
            }
        }

        private void LateUpdate()
        {
            if (_needle == null) return;
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;

            // Screen-space north: rotate the needle opposite the camera's yaw, so the red half
            // always points at world +Z.
            Vector3 f = _cam.transform.forward;
            float yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            _needle.localRotation = Quaternion.Euler(0f, 0f, yaw);
        }
    }
}
