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
            if (EnvMode.Daylight)
            {
                Backdrop.ClearDepth();
                return;
            }

            float depth = _waterLevel - _cam.transform.position.y;
            DepthLight.Attenuation(depth, out float r, out float g, out float b);
            var tint = new Color(r, g, b, 1f);

            // 🔴 WO-E3: the FULL attenuation, not half of it.
            //
            // The half was compensation. Applying the depth curve to the ambient while the fog and
            // the backdrop were painted from a depth-independent ramp meant the scene got darker
            // and the water did not, so the water "came out near-black" only in the sense that
            // everything IN it did — and halving the curve was the lever that was reachable at the
            // time. Now the fog (below) and the backdrop (Backdrop.SetDepth) are multiplied by this
            // same vector, so the subject-to-background ratio does not move with depth at all and
            // the ambient no longer has to be protected from its own curve. Halving it here would
            // now do the opposite of what it was hired for: it would make the subject drift
            // BRIGHTER than the water as the camera descends.
            RenderSettings.ambientSkyColor = Dim(_baseSky, tint);
            RenderSettings.ambientEquatorColor = Dim(_baseEquator, tint);
            RenderSettings.ambientGroundColor = Dim(_baseGround, tint);

            // The background, dimmed by the very same vector. This is the half of the fix that is
            // visible: the gradient fills most of the frame and was baked once, at the surface.
            Backdrop.SetDepth(depth);

            // The fog: the web's own #123a55, dimmed by the same vector as everything else.
            //
            // 🔴 The history is worth keeping because BOTH previous versions of this line were
            // wrong in ways that look right. Version 1 attenuated the authored fog colour while the
            // backdrop behind it stayed on a bright, depth-independent ramp — so distant geometry
            // faded toward a colour a third as bright as the background directly behind it and read
            // as a black silhouette. Version 2 fixed that by reading the fog OFF the backdrop ramp,
            // which made fog and background agree with each other but left both of them out of the
            // lighting's multiplication, so the subject kept sinking away from the water with
            // depth. Version 3 — this one — puts all three in the same product: authored colour ⊙
            // attenuation, for the fog, for the backdrop, and for the ambient. The web's fog colour
            // is a point on the web's own gradient (WaterFog.FogRampV), so agreeing with the
            // background costs nothing and needs nobody to keep two numbers in step.
            SeabedGeom.Rgb water = WaterFog.ColorAt(depth);
            var mood = new SeabedGeom.Rgb(_baseFog.r, _baseFog.g, _baseFog.b);
            SeabedGeom.Rgb fog = WaterFog.Blend(water, mood, WaterFog.MoodWeight);
            RenderSettings.fogColor = new Color(fog.R, fog.G, fog.B, 1f);

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

        /// <summary>
        /// Dim an authored ambient colour by the attenuation, in light rather than in the numbers.
        ///
        /// The old line was <c>_baseSky * tint</c>, a plain Color multiply, and it is wrong in the
        /// same way it would be wrong to darken a photograph by scaling its JPEG bytes: these
        /// colours are sRGB-authored (the web's hex) and the attenuation is a transmittance. At the
        /// bottom of the curve the two differ by about a factor of three, always in the direction
        /// of too dark. It also has to agree EXACTLY with what the fog and the backdrop do, or the
        /// ratio this whole change is built on drifts with depth — hence the shared
        /// <see cref="ToneMap.ScaleLight"/> rather than a second copy of the arithmetic.
        /// </summary>
        private static Color Dim(Color c, Color k) => new Color(
            ToneMap.ScaleLight(c.r, k.r),
            ToneMap.ScaleLight(c.g, k.g),
            ToneMap.ScaleLight(c.b, k.b),
            c.a);

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
