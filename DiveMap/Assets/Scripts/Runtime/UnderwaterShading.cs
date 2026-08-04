using DiveMap.Core;
using UnityEngine;

namespace DiveMap.Runtime
{
    /// <summary>
    /// Keeps underwater surfaces lit by at least as much light as the water they are in.
    ///
    /// The measurement and the reasoning live in <see cref="UnderwaterLight"/> — short version:
    /// the sharks, the statues and the corals are all bright models (base colour averages sRGB
    /// 108-202, metal channel 0.004), and they went black because the ambient reaching a side face
    /// had fallen to about a quarter of the colour of the water directly behind it. An opaque
    /// object dimmer than the medium it is in reads as a hole, not as an object.
    ///
    /// 🔎 HOW it is applied matters as much as what. Three systems already write the same three
    /// RenderSettings ambient colours — <see cref="EnvMode"/> for daylight, <see cref="DepthAtmosphere"/>
    /// for the depth curve, <c>DroneLights</c> for the headlamp swap — and every previous attempt to
    /// add a fourth owner ended with one of them silently losing to whichever ran last. So this
    /// never owns anything and never argues:
    ///
    ///   • it runs LAST in the frame (execution order 500, after every default-order LateUpdate),
    ///     so it sees the value the scene actually settled on;
    ///   • it only ever RAISES a channel — anything already brighter than the water is left alone,
    ///     which is why turning the headlamps on still opens the water up;
    ///   • and it BORROWS rather than keeps. <see cref="UnderwaterAmbientReturn"/> puts the
    ///     original values back at the top of the next frame, before anybody looks. Rendering
    ///     happens after LateUpdate and the whole Update phase happens before any LateUpdate, so
    ///     the frame is drawn with the raised values and every other system still reads exactly
    ///     what it last wrote. Without that, DepthAtmosphere would adopt our raised value as its
    ///     new surface baseline and the scene would stay deep-blue all the way back up to the
    ///     surface.
    ///
    /// No shader and no material property is touched. This project has been burned twice by custom
    /// shaders being stripped out of the iOS build and rendering magenta, and by keywords that were
    /// enabled on a variant that did not exist; RenderSettings cannot be stripped.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(500)]
    public sealed class UnderwaterShading : MonoBehaviour
    {
        private static UnderwaterShading _instance;

        private float _waterLevel;
        private Camera _cam;

        // What the rest of the scene had before we raised it, and what we left in its place. Both
        // are needed: the second is how we tell "still ours" from "somebody wrote after us".
        private bool _borrowed;
        private Color _lentSky, _lentEquator, _lentGround;
        private Color _paidSky, _paidEquator, _paidGround;

        // Log throttle. Rule from the handoff: the log has to speak when NOTHING happens too,
        // otherwise silence is indistinguishable between "working" and "never ran".
        private bool _everLogged;
        private float _loggedMetres = float.MinValue;
        private bool _loggedDaylight;

        /// <summary>
        /// Attach (or re-point) the floor for a freshly built map. Called beside
        /// <c>TourController.Configure</c> — same information, same moment.
        /// </summary>
        public static void Configure(float waterLevel)
        {
            if (_instance == null)
            {
                var go = new GameObject("UnderwaterShading");
                _instance = go.AddComponent<UnderwaterShading>();
                go.AddComponent<UnderwaterAmbientReturn>();   // the other half of borrow-and-return
            }
            _instance._waterLevel = waterLevel;
            _instance._cam = null;            // new map, the camera may have been rebuilt
            _instance._everLogged = false;
            _instance._loggedMetres = float.MinValue;
        }

        /// <summary>
        /// The depth the floor is working at right now, in world units — so a QC pass can print
        /// the band it is photographing THROUGH instead of guessing at it from the map's centre.
        /// Falls back to the surface when there is no camera yet, which is the honest answer.
        /// </summary>
        public float CameraDepthUnits
        {
            get
            {
                Camera c = _cam != null ? _cam : Camera.main;
                return c != null ? _waterLevel - c.transform.position.y : 0f;
            }
        }

        /// <summary>Give back whatever we raised, if it is still ours to give back.</summary>
        internal static void ReturnBorrowed()
        {
            if (_instance != null) _instance.Restore();
        }

        private void Restore()
        {
            if (!_borrowed) return;
            _borrowed = false;

            // If another system wrote over our value in between then that is its intent, not a
            // leftover of ours, and putting the old colour back would be us overwriting it.
            if (RenderSettings.ambientSkyColor == _paidSky) RenderSettings.ambientSkyColor = _lentSky;
            if (RenderSettings.ambientEquatorColor == _paidEquator) RenderSettings.ambientEquatorColor = _lentEquator;
            if (RenderSettings.ambientGroundColor == _paidGround) RenderSettings.ambientGroundColor = _lentGround;
        }

        private void LateUpdate()
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;

            // The daylight mode is a view from a boat — the web's own framing. Nothing below the
            // surface applies, and tinting it with deep-water blue would be plainly wrong.
            if (EnvMode.Daylight)
            {
                if (!_loggedDaylight || !_everLogged)
                {
                    Debug.Log("[Water] daylight — underwater floor off, ambient untouched");
                    _loggedDaylight = true;
                    _everLogged = true;
                }
                return;
            }
            _loggedDaylight = false;

            float depth = _waterLevel - _cam.transform.position.y;
            float metres = depth / DepthLight.UnitsPerMetre;
            // Depth is a subtraction of two world positions. One NaN and every ambient channel goes
            // NaN, the scene renders black, and the log throttle below (which compares depths)
            // stops throttling and buries the frame in a million lines.
            if (float.IsNaN(metres) || float.IsInfinity(metres)) return;

            UnderwaterLight.AmbientFloor(depth,
                                         out SeabedGeom.Rgb fSky,
                                         out SeabedGeom.Rgb fEquator,
                                         out SeabedGeom.Rgb fGround);

            _lentSky = RenderSettings.ambientSkyColor;
            _lentEquator = RenderSettings.ambientEquatorColor;
            _lentGround = RenderSettings.ambientGroundColor;

            // 🔴 WO-E4. The water-derived floor above never reached the GROUND band — water ⊙ 0.70
            // in authored numbers lands below the depth-dimmed band it was meant to catch, so the
            // one band that was falling under the tone curve's crush point was the one band the
            // floor could not see. UnderwaterLight.GroundBandAt is that band stated properly (dim
            // ⊙ seabed bounce ⊙ per-channel floor at BlackFloor × 3.47), and this component is
            // where it becomes unarguable: order 500 is after every default-order LateUpdate, so
            // whatever EnvMode, DepthAtmosphere or the headlamps did, this is the last word — and
            // it only ever raises, so none of them lose anything they were already brighter at.
            SeabedGeom.Rgb gBand = depth > 0f ? UnderwaterLight.GroundBandAt(depth) : fGround;

            _paidSky = Raise(_lentSky, fSky);
            _paidEquator = Raise(_lentEquator, fEquator);
            _paidGround = Raise(Raise(_lentGround, fGround), gBand);

            RenderSettings.ambientSkyColor = _paidSky;
            RenderSettings.ambientEquatorColor = _paidEquator;
            RenderSettings.ambientGroundColor = _paidGround;
            _borrowed = true;

            LogState(metres, depth);
        }

        private static Color Raise(Color current, SeabedGeom.Rgb floor)
        {
            var c = new SeabedGeom.Rgb(current.r, current.g, current.b);
            SeabedGeom.Rgb raised = UnderwaterLight.Raise(c, floor);
            return new Color(raised.R, raised.G, raised.B, current.a);
        }

        /// <summary>
        /// One line every 5 m of depth change, with the numbers a reviewer would otherwise have to
        /// guess at from a screenshot: where the camera is, what colour the water is there, what a
        /// mid-grey side face returns compared with it, and whether the floor did anything at all.
        /// </summary>
        private void LogState(float metres, float depthUnits)
        {
            if (_everLogged && Mathf.Abs(metres - _loggedMetres) < 5f) return;
            _loggedMetres = metres;
            _everLogged = true;

            if (UnderwaterLight.Strength(depthUnits) <= 0f)
            {
                Debug.Log($"[Water] depth={metres:F1}m — at or above the surface, ambient untouched");
                return;
            }

            SeabedGeom.Rgb water = WaterFog.ColorAt(depthUnits);
            // The number the whole fix is about, read off what the frame ACTUALLY got rather than
            // off the floor: how bright a mid-grey side face is next to the water behind it. Under
            // ~0.3 and it is a silhouette; the reported screenshot was at 0.21-0.26 in green/blue.
            const float a = UnderwaterLight.MidGreyAlbedo;
            float lr = water.R > 1e-6f ? _paidEquator.r * a / water.R : 1f;
            float lg = water.G > 1e-6f ? _paidEquator.g * a / water.G : 1f;
            float lb = water.B > 1e-6f ? _paidEquator.b * a / water.B : 1f;
            bool raised = _paidEquator != _lentEquator;

            // 🔴 WO-E4 — the two numbers that make one log line enough to answer "is the picture
            // above the floor or not", so nobody has to re-derive them off a screenshot again:
            //
            //   gnd/black  the ground band's channels in ToneMap.BlackFloors. Anything UNDER 1.00
            //              is byte 0 on every down-facing surface in the scene, whatever the model
            //              is painted. It read (0.89, 9.70, 22.56) before this change.
            //   crushAlb   the darkest base colour that can still come off a down-facing surface
            //              here, as an sRGB byte. It read 72 at this depth; the Singha atlas is
            //              47.9% darker than 71, which is the whole of the "สิงห์ดำทั้งก้อน" bug.
            var gnd = new SeabedGeom.Rgb(_paidGround.r, _paidGround.g, _paidGround.b);
            SeabedGeom.Rgb ratio = UnderwaterLight.BlackFloorRatios(gnd);
            int crush = SurfaceLight.CrushAlbedoSrgb(depthUnits, SurfaceLight.Facing.Down);

            Debug.Log($"[Water] depth={metres:F1}m water=({water.R:F3},{water.G:F3},{water.B:F3}) " +
                      $"ambient sky=({_paidSky.r:F3},{_paidSky.g:F3},{_paidSky.b:F3}) " +
                      $"eq=({_paidEquator.r:F3},{_paidEquator.g:F3},{_paidEquator.b:F3}) " +
                      $"gnd=({_paidGround.r:F3},{_paidGround.g:F3},{_paidGround.b:F3}) " +
                      $"gnd/black=({ratio.R:F2},{ratio.G:F2},{ratio.B:F2}) crushAlb=sRGB{crush} " +
                      $"bounce=x{UnderwaterLight.SeabedBounce(depthUnits):F2} " +
                      $"midGrey/water=({lr:F2},{lg:F2},{lb:F2}) floor={(raised ? "lifted" : "not needed")}");
        }

        private void OnDisable() => Restore();

        private void OnDestroy()
        {
            Restore();
            if (_instance == this) _instance = null;
        }
    }

    /// <summary>
    /// The return half of <see cref="UnderwaterShading"/>'s borrow-and-return.
    ///
    /// It exists as a second component purely because a MonoBehaviour has one execution order for
    /// all of its messages, and this job needs the opposite end of the frame from the raise: order
    /// −500 so it runs before anything else in the Update phase (including the tour handing the
    /// headlamps their "scene as it was" snapshot), while the raise runs at +500 in the LateUpdate
    /// phase. Unity runs the whole Update phase before any LateUpdate, so the ordering holds.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-500)]
    public sealed class UnderwaterAmbientReturn : MonoBehaviour
    {
        private void Update() => UnderwaterShading.ReturnBorrowed();
    }
}
