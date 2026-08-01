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
            if (!_haveBase ||
                RenderSettings.ambientSkyColor != _wroteSky ||
                RenderSettings.ambientEquatorColor != _wroteEquator ||
                RenderSettings.ambientGroundColor != _wroteGround ||
                RenderSettings.fogColor != _wroteFog ||
                !Mathf.Approximately(RenderSettings.fogEndDistance, _wroteFogEnd))
            {
                _baseSky = RenderSettings.ambientSkyColor;
                _baseEquator = RenderSettings.ambientEquatorColor;
                _baseGround = RenderSettings.ambientGroundColor;
                _baseFog = RenderSettings.fogColor;
                _baseFogStart = RenderSettings.fogStartDistance;
                _baseFogEnd = RenderSettings.fogEndDistance;
                _haveBase = true;
            }

            // In air none of this applies — the web's daylight mode is a view from a boat.
            if (EnvMode.Daylight) return;

            float depth = _waterLevel - _cam.transform.position.y;
            DepthLight.Attenuation(depth, out float r, out float g, out float b);
            var tint = new Color(r, g, b, 1f);

            // Only HALF the attenuation goes on the ambient. The headlamp system already dims the
            // whole scene by its own multiplier, so applying the depth curve on top at full
            // strength stacked two dimmers and the water came out near-black — reported as "still
            // too dark" on a build that had already been brightened once. The depth cue lives
            // mostly in the fog and the colour shift, which is where the eye reads it anyway.
            Color soft = Color.Lerp(Color.white, tint, 0.5f);
            RenderSettings.ambientSkyColor = _baseSky * soft;
            RenderSettings.ambientEquatorColor = _baseEquator * soft;
            RenderSettings.ambientGroundColor = _baseGround * soft;

            // The water itself goes deeper blue-green as the red drains out of the light in it.
            RenderSettings.fogColor = _baseFog * new Color(r, Mathf.Lerp(r, g, 0.5f), b, 1f);

            float vis = DepthLight.VisibilityScale(depth);
            RenderSettings.fogStartDistance = _baseFogStart * vis;
            RenderSettings.fogEndDistance = _baseFogEnd * vis;

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
