using System;
using DiveMap.Core;
using DiveMap.Runtime.Ui;
using UnityEngine;

namespace DiveMap.Runtime
{
    /// <summary>
    /// P0.5 — who owns the screen right now (<see cref="AppMode"/>), and the three things that
    /// must change together when that answer changes: orbit input, the ☰ affordance, and the
    /// HUD layer. Built before the tour (P1) on purpose: the alternative is every new mode
    /// bolting another bool onto AppBoot/UiShell, which is exactly how the web's builder.html
    /// grew to 4,408 lines of interdependent flags.
    ///
    /// The rules themselves live in <see cref="ModeRules"/> (pure, unit-tested). This class only
    /// applies them, and it never destroys the map — a mode change is a change of controls and
    /// overlay, not a reload.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ModeManager : MonoBehaviour
    {
        public static ModeManager Instance { get; private set; }

        /// <summary>Current mode; <see cref="AppMode.View"/> until something changes it.</summary>
        public static AppMode Current => Instance != null ? Instance._mode : AppMode.View;

        /// <summary>
        /// Read by <c>UiShell</c> as one input to its orbit gate (the shell still has the last
        /// word, because an open screen must block the camera whatever the mode).
        /// </summary>
        public static bool OrbitAllowed => ModeRules.AllowsOrbit(Current);

        /// <summary>(previous, next) — fired after the switch has been applied.</summary>
        public event Action<AppMode, AppMode> Changed;

        private AppMode _mode = AppMode.View;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("ModeManager");
            go.AddComponent<ModeManager>();
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

        /// <summary>
        /// Ask to enter <paramref name="mode"/>. Returns false when the move is not legal
        /// (<see cref="ModeRules.CanEnter"/>) — callers can ignore the result: a refused
        /// request leaves everything exactly as it was, so no UI ends up half-switched.
        /// </summary>
        public bool Request(AppMode mode)
        {
            if (!ModeRules.CanEnter(_mode, mode))
            {
                Debug.Log($"[Mode] refused {_mode}→{mode}");
                return false;
            }

            AppMode prev = _mode;
            _mode = mode;
            Apply(prev, mode);
            Debug.Log($"[Mode] {prev}→{mode} orbit={ModeRules.AllowsOrbit(mode)} " +
                      $"hud={ModeRules.ShowsHud(mode)} menu={ModeRules.AllowsMenu(mode)}");
            Changed?.Invoke(prev, mode);
            return true;
        }

        /// <summary>Leave the current mode (always back to the map view).</summary>
        public bool Exit() => Request(ModeRules.ExitTarget(_mode));

        private void Apply(AppMode prev, AppMode next)
        {
            // Never carry a held stick across a mode change.
            InputRig.Clear();
            // …nor the builder's ▶ playback switch (WO-L). It only means anything inside Edit,
            // and a tour that inherited it would be indistinguishable from one where the animal
            // brains had simply stopped.
            ModeRules.EditPlayback = false;
            HudLayer.SetActiveMode(next);

            // Orientation: the web locks its tour to landscape. Restore the default afterwards
            // rather than leaving the app stuck sideways in the map list.
            //
            // 🔴 …but only when Unity IS the app (WO-MERGE P1b, bug 4). Embedded, Screen.orientation
            // is a process-wide setting reaching straight out of the framework into somebody else's
            // UIViewController stack: Unity would turn the whole React Native app sideways, including
            // the navigation bar and the WebView behind it, and — worse — would leave it that way if
            // the player backed out of the tour through the host's own back button instead of ours.
            // So embedded we SIGNAL and the host decides; the host is the only party that knows what
            // the rest of its screen can survive.
            string tourSignal = NativeBoot.TourSignal(prev, next);
            if (NativeBridge.HostAttached)
            {
                if (tourSignal != null) NativeBridge.Send(tourSignal);
            }
            else if (ModeRules.LocksLandscape(next) && !ModeRules.LocksLandscape(prev))
            {
                Screen.orientation = ScreenOrientation.LandscapeLeft;
            }
            else if (!ModeRules.LocksLandscape(next) && ModeRules.LocksLandscape(prev))
            {
                Screen.orientation = ScreenOrientation.AutoRotation;
            }

            UiShell shell = UiShell.Instance;
            if (shell != null) shell.ApplyModeChrome(next);
        }
    }
}
