using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// P1.1 — an analog stick, ported from the web's <c>makeStick()</c> (builder.html 3579-3593):
    /// the knob follows the finger up to a radius of 0.42 × the pad's width and reports −1…1,
    /// snapping back to zero on release.
    ///
    /// Drag is handled through the EventSystem (uGUI legacy, per the project's no-TMP rule) so it
    /// composes with the rest of the UI instead of reading raw touches — <see cref="OrbitCamera"/>
    /// already showed what raw-Input controls cost when a second control scheme appears.
    ///
    /// The pad keeps receiving drag while the finger wanders off it (uGUI sends the whole gesture
    /// to whoever accepted OnPointerDown), which is what makes a thumb-controlled stick usable.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class JoystickWidget : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        private const float RadiusRatio = 0.42f;   // web: R = pad width × 0.42

        private RectTransform _pad;
        private RectTransform _knob;
        private Action<Vector2> _onChange;
        private Vector2 _value;

        /// <summary>Current deflection, −1…1 per axis (screen axes: +y is up).</summary>
        public Vector2 Value => _value;

        /// <summary>
        /// Build a stick at <paramref name="anchor"/> (0/1 corner of the parent) inset by
        /// <paramref name="offset"/>. <paramref name="onChange"/> receives the deflection.
        /// </summary>
        public static JoystickWidget Create(RectTransform parent, string name, Vector2 anchor,
                                            Vector2 offset, float size, Action<Vector2> onChange)
        {
            if (parent == null) return null;

            // ROUND, like the web (its pads are CSS circles). A rectangular pad reads as a
            // button you can miss; a circle tells the thumb where the centre is.
            Image pad = UiKit.MakeCircle(parent, name, new Color(0.043f, 0.090f, 0.118f, 0.50f));
            RectTransform prt = pad.rectTransform;
            prt.anchorMin = anchor;
            prt.anchorMax = anchor;
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(size, size);
            prt.anchoredPosition = offset;

            // Ring on the pad's rim = the deflection limit, so the stick's range is visible.
            Image ring = UiKit.MakeCircle(prt, "Ring", new Color(0.310f, 0.820f, 0.771f, 0.30f), 0.06f);
            ring.raycastTarget = false;
            RectTransform rrt = ring.rectTransform;
            rrt.anchorMin = Vector2.zero;
            rrt.anchorMax = Vector2.one;
            rrt.offsetMin = new Vector2(size * 0.08f, size * 0.08f);
            rrt.offsetMax = new Vector2(-size * 0.08f, -size * 0.08f);

            Image knob = UiKit.MakeCircle(prt, "Knob", new Color(0.310f, 0.820f, 0.771f, 0.88f));
            knob.raycastTarget = false;
            RectTransform krt = knob.rectTransform;
            krt.anchorMin = new Vector2(0.5f, 0.5f);
            krt.anchorMax = new Vector2(0.5f, 0.5f);
            krt.pivot = new Vector2(0.5f, 0.5f);
            krt.sizeDelta = new Vector2(size * 0.42f, size * 0.42f);
            krt.anchoredPosition = Vector2.zero;

            var stick = pad.gameObject.AddComponent<JoystickWidget>();
            stick._pad = prt;
            stick._knob = krt;
            stick._onChange = onChange;
            return stick;
        }

        public void OnPointerDown(PointerEventData e) => Move(e);
        public void OnDrag(PointerEventData e) => Move(e);

        public void OnPointerUp(PointerEventData e)
        {
            _value = Vector2.zero;
            if (_knob != null) _knob.anchoredPosition = Vector2.zero;
            _onChange?.Invoke(_value);
        }

        private void Move(PointerEventData e)
        {
            if (_pad == null) return;

            // Local point inside the pad, in the pad's own units (so canvas scaling is handled).
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _pad, e.position, e.pressEventCamera, out Vector2 local)) return;

            float radius = Mathf.Max(1f, _pad.rect.width * RadiusRatio);
            Vector2 d = local;
            if (d.magnitude > radius) d = d.normalized * radius;

            if (_knob != null) _knob.anchoredPosition = d;
            _value = d / radius;
            _onChange?.Invoke(_value);
        }

        private void OnDisable()
        {
            // A mode switch must not leave the stick held (P0.5's InputRig.Clear covers the rig;
            // this keeps the visible knob honest too).
            if (_value == Vector2.zero) return;
            _value = Vector2.zero;
            if (_knob != null) _knob.anchoredPosition = Vector2.zero;
            _onChange?.Invoke(_value);
        }
    }
}
