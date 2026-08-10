using DiveMap.Core;
using UnityEngine;

namespace DiveMap.Runtime
{
    /// <summary>
    /// The scene's authored atmosphere, and the one place that can put it back (WO-MERGE P1e).
    ///
    /// 🔴 The bug this exists for — "enter map A, leave, pick map B, the world is one flat navy
    /// colour while the HUD works perfectly", four device rounds unexplained.
    ///
    /// Fog and ambient are GLOBAL (<c>RenderSettings</c>), they are process-wide, and Unity is
    /// never unloaded in the embedded app — it is paused and hidden. Five different pieces of code
    /// write those globals, and every one of them restores by remembering what it found:
    ///
    ///   DroneLights   tour lights OFF ⇒ fog 70-200 + ambient ×0.32 (near-black, and the contrast
    ///                 IS the feature) · restores in End()/LateUpdate, both of which only run when
    ///                 the MODE leaves Tour
    ///   EnvMode       daylight ⇒ fog off + bright ambient · restores on the next toggle
    ///   DepthAtmosphere / UnderwaterShading   scale whatever they find
    ///
    /// "Restore what I found" is a sound design inside one session and it fails completely the
    /// moment a session can end without anyone being told. In the merged app the user leaves
    /// mid-tour — the top-left exit, or an iOS swipe-back that never reaches Unity at all — so the
    /// mode stays Tour, nobody restores anything, and the darkness outlives the map. The next map
    /// then LAUNDERS it: DepthAtmosphere.Configure clears its baseline for the new map and
    /// re-captures it from whatever RenderSettings currently say, so the tour's near-black becomes
    /// the new map's idea of normal, permanently, for the rest of the process.
    ///
    /// The answer is an ABSOLUTE reference. This class snapshots the atmosphere that
    /// <c>AppBoot.SetupLighting</c> authors — once, before any mode has had a chance to touch it —
    /// and every new map load restores that snapshot before anything else runs. It does not care
    /// who broke the state, whether anyone noticed, or whether a message ever arrived: a build
    /// simply cannot inherit a previous mode's atmosphere any more.
    /// </summary>
    public static class SceneAtmosphere
    {
        private static bool _have;

        private static bool _fog;
        private static FogMode _fogMode;
        private static Color _fogColor;
        private static float _fogStart, _fogEnd;

        private static UnityEngine.Rendering.AmbientMode _ambientMode;
        private static Color _sky, _equator, _ground;
        private static float _ambientIntensity;

        // Directional light intensities: DroneLights scales these by the same ambient multiplier,
        // so a snapshot without them restores the fog and leaves the sun at a third of its power.
        private static float _sunIntensity = 1f, _fillIntensity = 1f;

        /// <summary>True once <see cref="CaptureDefaults"/> has run (diagnostics/overlay).</summary>
        public static bool HasDefaults => _have;

        /// <summary>
        /// Remember the authored atmosphere. Called at the end of <c>AppBoot.SetupLighting</c>,
        /// which is the only code that writes these values from constants rather than from
        /// something it read a moment earlier.
        ///
        /// Once per process on purpose. Re-capturing later would eventually snapshot a state some
        /// mode had already modified, and the absolute reference would quietly become another
        /// relative one — the exact failure this class exists to end.
        /// </summary>
        public static void CaptureDefaults()
        {
            if (_have) return;

            _fog = RenderSettings.fog;
            _fogMode = RenderSettings.fogMode;
            _fogColor = RenderSettings.fogColor;
            _fogStart = RenderSettings.fogStartDistance;
            _fogEnd = RenderSettings.fogEndDistance;

            _ambientMode = RenderSettings.ambientMode;
            _sky = RenderSettings.ambientSkyColor;
            _equator = RenderSettings.ambientEquatorColor;
            _ground = RenderSettings.ambientGroundColor;
            _ambientIntensity = RenderSettings.ambientIntensity;

            ReadLights(out Light sun, out Light fill);
            if (sun != null) _sunIntensity = sun.intensity;
            if (fill != null) _fillIntensity = fill.intensity;

            _have = true;
            Debug.Log($"[Atmos] defaults captured — fog={_fog} {_fogStart:F0}..{_fogEnd:F0} " +
                      $"sky={_sky} sun={_sunIntensity:F2} fill={_fillIntensity:F2}");
        }

        /// <summary>
        /// Put the authored atmosphere back, unconditionally.
        ///
        /// Safe to call when nothing is wrong — on a healthy boot it writes the same values that
        /// are already there. That is the point: the cheap path must be the unconditional one, or
        /// it grows a condition that is eventually wrong.
        /// </summary>
        public static void RestoreDefaults()
        {
            if (!_have) return;

            RenderSettings.fog = _fog;
            RenderSettings.fogMode = _fogMode;
            RenderSettings.fogColor = _fogColor;
            RenderSettings.fogStartDistance = _fogStart;
            RenderSettings.fogEndDistance = _fogEnd;

            RenderSettings.ambientMode = _ambientMode;
            RenderSettings.ambientSkyColor = _sky;
            RenderSettings.ambientEquatorColor = _equator;
            RenderSettings.ambientGroundColor = _ground;
            RenderSettings.ambientIntensity = _ambientIntensity;

            ReadLights(out Light sun, out Light fill);
            if (sun != null) sun.intensity = _sunIntensity;
            if (fill != null) fill.intensity = _fillIntensity;
        }

        /// <summary>
        /// Everything a new map needs done before it is built. Called from <c>AppBoot.Boot</c>,
        /// which is the single door every full map load comes through — first open, map switch,
        /// Retry and the queued host switch alike.
        ///
        /// Order is not arbitrary:
        ///   1. leave any non-View mode, so the tour hands back the camera, the joysticks, the
        ///      HUD and the orbit rig. Without this the drone keeps flying the new map with its
        ///      overlay on — which is exactly what the device screenshots showed: a live HUD with
        ///      a sane depth reading over a world nobody could see.
        ///   2. make DroneLights forget the snapshot it took of the PREVIOUS map. Its restore is
        ///      relative, so left armed it would write map A's atmosphere over map B a frame later.
        ///   3. drop EnvMode's captured daylight/underwater pair for the same reason.
        ///   4. and only then the absolute restore, which is what actually guarantees the result.
        ///
        /// Step 4 alone would fix the reported bug. Steps 1-3 are there so the fix cannot be
        /// undone by a stale relative restore arriving one frame later.
        /// </summary>
        /// <summary>
        /// QC ONLY — hold the reset back so the harness can photograph the bug it fixes
        /// (<see cref="QcBlankShot"/>). Never set outside a <c>-qcblank</c> run; there is no
        /// command-line flag and no setting that reaches it, and it is reset to false by the
        /// harness itself after each pass.
        /// </summary>
        public static bool SuppressResetForQc { get; set; }

        public static void ResetForNewMap()
        {
            if (SuppressResetForQc)
            {
                Debug.LogWarning("[Atmos] reset SUPPRESSED (-qcblank control pass) — " +
                                 "this build is deliberately reproducing the bug");
                return;
            }

            AppMode was = ModeManager.Current;
            if (was != AppMode.View && ModeManager.Instance != null)
            {
                // Exit() runs the normal path — TourController.End, lights off, orbit re-enabled —
                // so nothing here has to know what a tour is.
                ModeManager.Instance.Exit();
            }

            foreach (DroneLights d in Object.FindObjectsByType<DroneLights>(FindObjectsInactive.Include,
                                                                           FindObjectsSortMode.None))
                d.ForgetSceneSnapshot();

            EnvMode.Reset();
            RestoreDefaults();

            Debug.Log($"[Atmos] reset for a new map (mode was {was}) — " +
                      $"fog={RenderSettings.fog} {RenderSettings.fogStartDistance:F0}.." +
                      $"{RenderSettings.fogEndDistance:F0} sky={RenderSettings.ambientSkyColor}");
        }

        /// <summary>
        /// One line describing the global atmosphere right now, for the on-screen diagnostic.
        /// Deliberately terse: it has to be readable in a phone screenshot taken by a user who is
        /// not looking for it.
        /// </summary>
        public static string StateLine()
        {
            return $"fog {(RenderSettings.fog ? "on" : "off")} " +
                   $"{RenderSettings.fogStartDistance:F0}-{RenderSettings.fogEndDistance:F0} " +
                   $"amb {RenderSettings.ambientSkyColor.grayscale:F2}";
        }

        /// <summary>
        /// The sun and the fill light, found the same way <see cref="DroneLights"/> finds them —
        /// by type and by the "FillLight" name — so the two agree about which light is which.
        /// Inactive lights included: a mode may have switched one off.
        /// </summary>
        private static void ReadLights(out Light sun, out Light fill)
        {
            sun = null;
            fill = null;
            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include,
                                                                FindObjectsSortMode.None))
            {
                if (l.type != LightType.Directional) continue;
                if (l.gameObject.name == "FillLight") { if (fill == null) fill = l; }
                else if (sun == null) sun = l;
            }
        }
    }
}
