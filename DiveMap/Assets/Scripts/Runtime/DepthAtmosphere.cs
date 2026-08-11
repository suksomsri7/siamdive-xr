using DiveMap.Core;
using UnityEngine;

namespace DiveMap.Runtime
{
    /// <summary>
    /// Dims and blues the scene with depth, so the shallows read as shallow.
    ///
    /// The maths is in <see cref="DepthLight"/> and tested there; this only decides WHEN to apply
    /// it and how to live alongside the two other things that write the same RenderSettings
    /// (<see cref="EnvMode"/> for daylight/underwater, <c>DroneLights</c> for the headlamp swap).
    ///
    /// 🔎 Rather than own those values it watches them: whatever another system last wrote becomes
    /// the new "surface" baseline, and this scales that baseline by the camera's depth. So turning
    /// the lamps on still opens the water up — it just opens it up by less at 40 m than at 3 m,
    /// which is the point. Ownership would have meant every future light change silently losing to
    /// whichever component ran last, which is the bug that black tail fins and ghost maps were.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DepthAtmosphere : MonoBehaviour
    {
        private static DepthAtmosphere _instance;

        private float _waterLevel;
        private Camera _cam;

        // The scene as some other system wants it at the surface.
        private Color _baseSky, _baseEquator, _baseGround, _baseFog;
        private float _baseFogStart, _baseFogEnd;
        private bool _haveBase;

        // What we wrote last frame — anything else means somebody changed the baseline.
        private Color _wroteSky, _wroteEquator, _wroteGround, _wroteFog;
        private float _wroteFogStart, _wroteFogEnd;

        // ── measurement surface (WO-MERGE DARK) ─────────────────────────────────
        //
        // 🔴 CI b384 finally reproduced the bug: with the reset suppressed the next map inherited
        // ambient 0.167 against an authored 0.450. But the FIXED pass came back 0.369, not 0.450 —
        // 82% — and there are two completely different reasons that could be true: this component
        // legitimately scaling the ambient down for the camera's depth, or the restore genuinely
        // putting back only part of it. Those have opposite fixes, so the chain is published here
        // and the answer is read off a log instead of argued:
        //
        //     authored ──(SceneAtmosphere restore)──▶ base ──(× soft, depth)──▶ wrote ──▶ live
        //                                                                  (UnderwaterShading raise)
        //
        // Each arrow is separately visible, so an 18% gap can be attributed to the arrow that ate
        // it rather than to whichever explanation sounds better.

        /// <summary>The surface baseline this component is scaling FROM (grayscale). -1 = none.</summary>
        public static float BaseSkyGray { get; private set; } = -1f;

        /// <summary>The depth factor it last applied to the ambient (grayscale of <c>soft</c>).</summary>
        public static float SoftGray { get; private set; } = 1f;

        /// <summary>What it last WROTE into RenderSettings (grayscale) — base × soft.</summary>
        public static float WroteSkyGray { get; private set; } = -1f;

        /// <summary>Camera depth below the surface, in world units, at that moment.</summary>
        public static float LastDepth { get; private set; }

        /// <summary>True when the last pass was skipped because the app is in daylight mode.</summary>
        public static bool SkippedForDaylight { get; private set; }

        public static void Configure(float waterLevel)
        {
            if (_instance == null)
            {
                var go = new GameObject("DepthAtmosphere");
                _instance = go.AddComponent<DepthAtmosphere>();
            }
            _instance._waterLevel = waterLevel;
            _instance._haveBase = false;   // new map, new baseline
        }

        private void LateUpdate()
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;

            // Somebody else (EnvMode, the headlamp) wrote new values → they are the new surface.
            //
            // 🔴 …but each baseline follows ONLY ITS OWN SIGNAL (WO-MERGE DARK). This used to be
            // one all-or-nothing test: any difference in any field re-read all six. That made the
            // fog distances a hostage of the ambient, and the ambient is written every frame by
            // more than one system — including this one. Each stray ambient write re-captured the
            // fog distances FROM THIS COMPONENT'S OWN ALREADY-SCALED OUTPUT and multiplied them by
            // vis again, so the fog crept in geometrically instead of tracking the camera's depth.
            //
            // The user's badge photograph is the measurement: authored 500..9000, live 489..8797,
            // both ends down by the same 0.9774 in View mode. One multiplication is the feature;
            // the loop that produces the second one is the bug. See Core/AtmosphereBaseline.
            bool ambientChanged = RenderSettings.ambientSkyColor != _wroteSky ||
                                  RenderSettings.ambientEquatorColor != _wroteEquator ||
                                  RenderSettings.ambientGroundColor != _wroteGround;
            bool fogColorChanged = RenderSettings.fogColor != _wroteFog;
            // Both ends now, not just the far one: _wroteFogStart was recorded and never compared,
            // so a near-plane change alone was invisible to the check that is supposed to see it.
            bool fogDistChanged = !Mathf.Approximately(RenderSettings.fogStartDistance, _wroteFogStart) ||
                                  !Mathf.Approximately(RenderSettings.fogEndDistance, _wroteFogEnd);

            AtmosphereBaseline.Refresh refresh =
                AtmosphereBaseline.Decide(_haveBase, ambientChanged, fogColorChanged, fogDistChanged);

            if ((refresh & AtmosphereBaseline.Refresh.Ambient) != 0)
            {
                _baseSky = RenderSettings.ambientSkyColor;
                _baseEquator = RenderSettings.ambientEquatorColor;
                _baseGround = RenderSettings.ambientGroundColor;
            }
            if ((refresh & AtmosphereBaseline.Refresh.FogColor) != 0)
                _baseFog = RenderSettings.fogColor;
            if ((refresh & AtmosphereBaseline.Refresh.FogDistance) != 0)
            {
                _baseFogStart = RenderSettings.fogStartDistance;
                _baseFogEnd = RenderSettings.fogEndDistance;
            }
            _haveBase = true;

            // Published even when nothing else runs below, so a reader can always tell "the base
            // is wrong" from "the base is right and the depth scale moved it".
            BaseSkyGray = _baseSky.grayscale;

            // In air none of this applies — the web's daylight mode is a view from a boat.
            if (EnvMode.Daylight) { SkippedForDaylight = true; return; }
            SkippedForDaylight = false;

            float depth = _waterLevel - _cam.transform.position.y;
            LastDepth = depth;
            DepthLight.Attenuation(depth, out float r, out float g, out float b);
            var tint = new Color(r, g, b, 1f);

            // Only HALF the attenuation goes on the ambient. The headlamp system already dims the
            // whole scene by its own multiplier, so applying the depth curve on top at full
            // strength stacked two dimmers and the water came out near-black — reported as "still
            // too dark" on a build that had already been brightened once. The depth cue lives
            // mostly in the fog and the colour shift, which is where the eye reads it anyway.
            Color soft = Color.Lerp(Color.white, tint, 0.5f);
            SoftGray = soft.grayscale;
            RenderSettings.ambientSkyColor = _baseSky * soft;
            RenderSettings.ambientEquatorColor = _baseEquator * soft;
            RenderSettings.ambientGroundColor = _baseGround * soft;

            // 🔴 The fog colour comes off the BACKDROP'S OWN RAMP, not from multiplying the base
            // colour down.
            //
            // The old line did the physically-tempting thing — take the authored fog colour and
            // attenuate it like light — and it is what turned the wreck and the fish into black
            // silhouettes. Two independent things were painting the same pixels: the backdrop
            // gradient runs #eaf7fb→#1b5a85, and this was multiplying #123a55 down from there. So a
            // fish 200 units out faded toward a colour roughly a third as bright as the background
            // directly behind it. Nothing was unlit; the fog was simply the wrong colour, and no
            // amount of brightening the lights could have fixed it.
            //
            // Reading the ramp instead makes the two agree BY CONSTRUCTION: whatever the gradient
            // says the water looks like at this depth is what things fade into. Editing a stop in
            // SeabedGeom now moves both. See WaterFog for why the sample sits at the horizon rather
            // than at the top or bottom of the ramp.
            SeabedGeom.Rgb ramp = WaterFog.ColorAt(depth);
            var mood = new SeabedGeom.Rgb(_baseFog.r, _baseFog.g, _baseFog.b);
            SeabedGeom.Rgb fog = WaterFog.Blend(ramp, mood, WaterFog.MoodWeight);
            RenderSettings.fogColor = new Color(fog.R, fog.G, fog.B, 1f);

            float vis = DepthLight.VisibilityScale(depth);
            RenderSettings.fogStartDistance = _baseFogStart * vis;
            RenderSettings.fogEndDistance = _baseFogEnd * vis;

            WroteSkyGray = RenderSettings.ambientSkyColor.grayscale;
            _wroteSky = RenderSettings.ambientSkyColor;
            _wroteEquator = RenderSettings.ambientEquatorColor;
            _wroteGround = RenderSettings.ambientGroundColor;
            _wroteFog = RenderSettings.fogColor;
            _wroteFogStart = RenderSettings.fogStartDistance;
            _wroteFogEnd = RenderSettings.fogEndDistance;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
