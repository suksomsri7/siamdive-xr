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

        // WO-E5 — the map's own size and where its middle is. The fog range is derived from these
        // and from where the camera is standing (see WaterFog.RangeAt); without them the only
        // lengths available were the web's orbit-framing constants, which is how the fog ended up
        // starting 322 u away on a map 340 u wide.
        private Vector3 _contentCentre;
        private float _contentRadius = SeabedGeom.SandRadius;

        // The scene as some other system wants it at the surface.
        private Color _baseSky, _baseEquator, _baseGround, _baseFog;
        private float _baseFogStart, _baseFogEnd;
        private bool _haveBase;

        // What we wrote last frame — anything else means somebody changed the baseline.
        private Color _wroteSky, _wroteEquator, _wroteGround, _wroteFog;
        private float _wroteFogStart, _wroteFogEnd;

        public static void Configure(float waterLevel)
            => Configure(waterLevel, Vector3.zero, SeabedGeom.SandRadius);

        /// <param name="contentCentre">Middle of the map's content, world space.</param>
        /// <param name="contentRadius">Half-width of the content, world units — the length the fog
        /// range is floored at so that flying into the middle of a map does not close the water in
        /// on the diver's face.</param>
        public static void Configure(float waterLevel, Vector3 contentCentre, float contentRadius)
        {
            if (_instance == null)
            {
                var go = new GameObject("DepthAtmosphere");
                _instance = go.AddComponent<DepthAtmosphere>();
            }
            _instance._waterLevel = waterLevel;
            _instance._contentCentre = contentCentre;
            _instance._contentRadius = contentRadius > 1f ? contentRadius : SeabedGeom.SandRadius;
            _instance._haveBase = false;   // new map, new baseline
            _instance._loggedRange = false;
        }

        /// <summary>
        /// The fog range the frame is actually being drawn with, for a QC pass to print beside the
        /// picture. Zeroes when nothing has configured this yet, which is the honest answer.
        /// </summary>
        public static void CurrentRange(out float start, out float end)
        {
            start = _instance != null ? RenderSettings.fogStartDistance : 0f;
            end = _instance != null ? RenderSettings.fogEndDistance : 0f;
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

            // 🔴 WO-E4 — the ground band is the one that could not survive its own multiplication.
            //
            // Dimming it exactly like the other two is arithmetically consistent and visually
            // fatal: the band starts at 0xffffff's 1.4% (0x123040 × 1.05), so red lands at 0.87 ×
            // ToneMap.BlackFloor, and everything under that comes out of ACES as EXACTLY byte 0.
            // Past ~15 m every down-facing surface in the app had its red pinned to zero whatever
            // the model's albedo, and the darkest base colour that could make it off an underside
            // at all was sRGB 72 — against a Singha atlas that is 47.9% darker than 71.
            //
            // So the dim, the seabed bounce and the floor are one function in Core, tested there,
            // and applied here as a raise: whatever EnvMode or the headlamps left in the baseline
            // is still honoured wherever it is already brighter. UnderwaterShading applies the same
            // function again at order 500 — belt and braces, and idempotent because both are maxes.
            RenderSettings.ambientGroundColor =
                Raise(Dim(_baseGround, tint), UnderwaterLight.GroundBandAt(depth));

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
            // 🔴 WO-E5 — …at the ramp position the HORIZON is actually at, not at the web's.
            //
            // #123a55 is the ramp at v = 0.90, which is where distant geometry meets the background
            // when an orbit camera looks DOWN at a small map from 950 u away. A diver looks roughly
            // level and their horizon lands near mid-screen, where the same ramp is about seven
            // times brighter in blue. Fading distant objects toward the deep stop while they stand
            // against the mid ramp is the "distant geometry reads as a black silhouette" failure
            // this comment's own version 1 describes — it was fixed for the orbit camera and
            // re-created for the diver. WaterFog.HorizonRampV is the projection, and it returns
            // 0.90 for exactly the pitch the web's camera has, so this is not a departure from the
            // web: it is the web's number stated as the thing it was a measurement OF.
            float rampV = FarRimRampV();
            SeabedGeom.Rgb water = WaterFog.ColorAt(depth, rampV);
            var mood = new SeabedGeom.Rgb(_baseFog.r, _baseFog.g, _baseFog.b);
            SeabedGeom.Rgb fog = WaterFog.Blend(water, mood, WaterFog.MoodWeight);
            RenderSettings.fogColor = new Color(fog.R, fog.G, fog.B, 1f);

            // 🔴 WO-E5 — the range, from the two lengths that describe the shot.
            //
            // What was here — the web's 500/9,000 scaled by the depth's visibility — could not
            // reach the map: 322 u to 5,805 u at 61.8 m, against content that is at most 680 u
            // across. The furthest thing a diver could look at was 6.5% fogged and most of the map
            // was 0%. See WaterFog.RangeAt for the whole derivation; the short version is that the
            // web's constants are a fact about the web's ORBIT FRAMING, and the app puts the player
            // inside the same 340 u map instead of 950 u outside it.
            //
            // _baseFogStart/_baseFogEnd are no longer the source of the range, but they are still
            // watched above, because another system writing them is still how this component knows
            // its baseline was replaced.
            float camToContent = Vector3.Distance(_cam.transform.position, _contentCentre);
            WaterFog.RangeAt(depth, _contentRadius, camToContent, out float fStart, out float fEnd);
            RenderSettings.fogStartDistance = fStart;
            RenderSettings.fogEndDistance = fEnd;

            LogRange(depth, camToContent, fStart, fEnd, rampV, fog);

            _wroteSky = RenderSettings.ambientSkyColor;
            _wroteEquator = RenderSettings.ambientEquatorColor;
            _wroteGround = RenderSettings.ambientGroundColor;
            _wroteFog = RenderSettings.fogColor;
            _wroteFogStart = RenderSettings.fogStartDistance;
            _wroteFogEnd = RenderSettings.fogEndDistance;
        }

        /// <summary>
        /// Which row of the backdrop the map's FAR RIM lands on, as a ramp position.
        ///
        /// Not modelled and not tuned: the point on the far side of the content, at the content's
        /// own height, is put through the real camera matrix and the row it comes out on is the
        /// answer. That is the row where distant geometry meets the background, which is the only
        /// row the fog colour has any business matching. For a camera framed the way the web frames
        /// its map it lands near the web's own 0.90; for a diver looking level it lands near
        /// mid-frame, where the ramp carries several times the light.
        ///
        /// Behind the camera — which happens the instant the diver turns around — the projection
        /// is meaningless, and <see cref="WaterFog.RampVOfViewportY"/> answers with the web's
        /// constant rather than with a mirrored number.
        /// </summary>
        private float FarRimRampV()
        {
            Vector3 away = _cam.transform.position - _contentCentre;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-4f) away = _cam.transform.forward;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-4f) return WaterFog.FogRampV;

            // The far side of the content from where the camera is standing, at the content's own
            // height — i.e. the furthest piece of map that is still map.
            Vector3 rim = _contentCentre - away.normalized * _contentRadius;
            rim.y = _contentCentre.y;

            Vector3 vp = _cam.WorldToViewportPoint(rim);
            return WaterFog.RampVOfViewportY(vp.y, vp.z <= 0f);
        }

        // Log throttle: one line per 5 m of depth, plus the first frame of every map. The rule from
        // the handoff is that the log has to speak when nothing happens too — a fog that is doing
        // nothing must SAY it is doing nothing, which is the whole reason this went unnoticed for
        // as long as it did.
        private bool _loggedRange;
        private float _loggedRangeMetres = float.MinValue;

        private void LogRange(float depth, float camToContent, float start, float end,
                              float rampV, SeabedGeom.Rgb fog)
        {
            float metres = depth / DepthLight.UnitsPerMetre;
            if (_loggedRange && Mathf.Abs(metres - _loggedRangeMetres) < 5f) return;
            _loggedRange = true;
            _loggedRangeMetres = metres;

            // The numbers a reviewer would otherwise re-derive off a screenshot: how much fog is on
            // the near rim, the middle and the far rim of the CONTENT — not of the frustum, which
            // is what "fog far = 9000" was silently being read as.
            float near = Mathf.Max(0f, camToContent - _contentRadius);
            float far = camToContent + _contentRadius;
            Debug.Log($"[Fog] depth={metres:F1}m camToContent={camToContent:F0} r={_contentRadius:F0} " +
                      $"range={start:F0}..{end:F0} " +
                      $"factor near({near:F0})={WaterFog.FactorAt(near, start, end) * 100f:F0}% " +
                      $"mid({camToContent:F0})={WaterFog.FactorAt(camToContent, start, end) * 100f:F0}% " +
                      $"far({far:F0})={WaterFog.FactorAt(far, start, end) * 100f:F0}% " +
                      $"rampV={rampV:F2} fog=({fog.R:F3},{fog.G:F3},{fog.B:F3})");
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

        /// <summary>Per-channel max against an authored Core colour — a floor, never a set.</summary>
        private static Color Raise(Color c, SeabedGeom.Rgb floor) => new Color(
            Mathf.Max(c.r, floor.R),
            Mathf.Max(c.g, floor.G),
            Mathf.Max(c.b, floor.B),
            c.a);

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
