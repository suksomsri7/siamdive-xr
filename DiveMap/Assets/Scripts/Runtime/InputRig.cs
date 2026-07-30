using UnityEngine;

namespace DiveMap.Runtime
{
    /// <summary>
    /// P0.5 — the one place a mode reads "what is the user asking for", so the tour (P1), the
    /// game (P3), AR (P7) and eventually the Galaxy XR build all consume the same signals
    /// instead of each poking <c>Input</c> directly. That last part matters: <see cref="OrbitCamera"/>
    /// already reads raw <c>Input</c> every frame, which is why the UI has to disable the whole
    /// component to stop the map spinning under a menu.
    ///
    /// Axes are WRITTEN by whatever is driving them (on-screen sticks in P1, a gamepad, or an XR
    /// controller later) and READ by the mode's controller. Values persist until the driver
    /// updates them, and <see cref="Clear"/> zeroes everything — a mode switch must never leave
    /// a stick stuck at full deflection.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InputRig : MonoBehaviour
    {
        public static InputRig Instance { get; private set; }

        /// <summary>Left stick: x = turn, y = rise/dive (the web's <c>stickL</c>). −1…1.</summary>
        public static Vector2 Left => Instance != null ? Instance._left : Vector2.zero;

        /// <summary>Right stick: y = forward thrust, x = strafe (the web's <c>stickR</c>). −1…1.</summary>
        public static Vector2 Right => Instance != null ? Instance._right : Vector2.zero;

        /// <summary>True on the frame a tap/click was released without dragging.</summary>
        public static bool Tapped => Instance != null && Instance._tapped;

        private Vector2 _left, _right;
        private bool _tapped;
        private bool _tapConsumedThisFrame;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("InputRig");
            go.AddComponent<InputRig>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void LateUpdate()
        {
            // A tap lives for exactly one frame; consumers read it in Update.
            if (_tapConsumedThisFrame) { _tapped = false; _tapConsumedThisFrame = false; }
            else if (_tapped) _tapConsumedThisFrame = true;
        }

        /// <summary>Driver API: set the left stick (clamped to the unit circle).</summary>
        public static void SetLeft(Vector2 v) { if (Instance != null) Instance._left = Clamp(v); }

        /// <summary>Driver API: set the right stick (clamped to the unit circle).</summary>
        public static void SetRight(Vector2 v) { if (Instance != null) Instance._right = Clamp(v); }

        /// <summary>Driver API: report a tap for this frame.</summary>
        public static void ReportTap() { if (Instance != null) { Instance._tapped = true; Instance._tapConsumedThisFrame = false; } }

        /// <summary>Zero every axis — called on mode changes so nothing is left held down.</summary>
        public static void Clear()
        {
            if (Instance == null) return;
            Instance._left = Vector2.zero;
            Instance._right = Vector2.zero;
            Instance._tapped = false;
        }

        private static Vector2 Clamp(Vector2 v)
            => v.sqrMagnitude > 1f ? v.normalized : v;
    }
}
