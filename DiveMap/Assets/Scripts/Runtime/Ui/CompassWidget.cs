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

        /// <summary>Add the compass to <paramref name="parent"/> at the web's position.</summary>
        public static CompassWidget Create(RectTransform parent)
        {
            if (parent == null) return null;

            Image dial = UiKit.MakeCircle(parent, "Compass", UiKit.Glass);
            dial.raycastTarget = false;
            RectTransform rt = dial.rectTransform;
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(96f, 96f);
            rt.anchoredPosition = new Vector2(-26f, 268f);   // clear of ☰ (44) and the actions column

            Image rim = UiKit.MakeCircle(rt, "Rim", UiKit.Line, 0.035f);
            rim.raycastTarget = false;
            UiKit.Stretch(rim.rectTransform);

            // The needle: one node holding both halves, rotated as a unit.
            RectTransform needle = UiKit.MakeNode(rt, "Needle");
            needle.anchorMin = new Vector2(0.5f, 0.5f);
            needle.anchorMax = new Vector2(0.5f, 0.5f);
            needle.pivot = new Vector2(0.5f, 0.5f);
            needle.sizeDelta = new Vector2(64f, 64f);
            needle.anchoredPosition = Vector2.zero;

            AddHalf(needle, "N", North, false);
            AddHalf(needle, "S", South, true);

            var c = dial.gameObject.AddComponent<CompassWidget>();
            c._needle = needle;
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
            rt.sizeDelta = new Vector2(26f, 28f);
            rt.anchoredPosition = Vector2.zero;
            rt.localRotation = Quaternion.Euler(0f, 0f, flipped ? 180f : 0f);
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
