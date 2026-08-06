using System;

namespace DiveMap.Core
{
    /// <summary>
    /// How far the viewer is allowed to pull back — and how far the camera's far plane and the
    /// fog have to reach for the map to still be there when it does.
    ///
    /// 🔴 2026-08-06, build 280 on a real iPhone: *"zoom out ได้มากกว่านี้ตามขนาดแมพ — Atlantis
    /// อยากเห็นเต็มแมพ"*. The ceiling was <c>OrbitCamera.maxDistance = 950f</c>, a literal on the
    /// field declaration, with the comment *"matches web builder controls.maxDistance (large
    /// sites)"*. That comment was wrong twice over, and the second way is the interesting one:
    ///
    ///   • 950 is not the web's ceiling for large sites. It is the number the web writes when it
    ///     LEAVES AR (builder.html:2955, <c>controls.maxDistance=950</c>) without calling
    ///     <c>updateViewRange()</c> afterwards — a bug on the web's own side, and the one value in
    ///     the whole file that is guaranteed NOT to be the right one for a big map.
    ///   • The web does not have a single ceiling at all. <c>updateViewRange()</c>
    ///     (builder.html:709-722) recomputes the ceiling, the far plane, the near plane and both
    ///     fog ends FROM THE SIZE OF THE MAP every time the map is resized or the water mode
    ///     changes. Its init value (:497) is 2,600 — already 2.7× what this app was using — and
    ///     that is only the FLOOR.
    ///
    /// So this is not a new feature, it is a transcription of something the web has had all along.
    /// Every constant below is quoted from builder.html:709-722:
    ///
    /// <code>
    ///   const ms = areaScale * Math.max(areaScaleX, areaScaleZ), reach = SAND_R * ms;
    ///   const maxD = Math.max(foggy ? 2600 : 3600, reach * 3.5);
    ///   controls.maxDistance = maxD;
    ///   const far = maxD * 2.5 + reach + 1200, near = Math.max(0.5, far / 40000);
    ///   scene.fog.near = Math.max(500, reach * 1.1);
    ///   scene.fog.far  = Math.max(9000, maxD * 3.4);
    /// </code>
    ///
    /// 🔎 Why the normal look cannot change. Feed the formulas the demo map's own reach — the
    /// web's bare <c>SAND_R = 340</c> — and they return fog 500 … 9,000, which is EXACTLY the pair
    /// <c>AppBoot</c> already hard-codes, and a far plane of 8,040 against its 9,000. An ordinary
    /// map therefore renders identically to build 280 by arithmetic, not by a special case; the
    /// ranges only open up on a map big enough to need them, which is the whole of the request and
    /// the whole of "อย่าเปลี่ยนลุคปกติ".
    ///
    /// 🔴 The one place this deliberately goes BEYOND the web: <see cref="FitDistance"/>. The web's
    /// 3.5× is a desktop number chosen against a landscape window; on a portrait phone the
    /// horizontal field of view is the narrow one and 3.5× still crops the rim off a round map.
    /// The ceiling is therefore <c>max(web's answer, the distance trigonometry says actually fits)</c>
    /// — see <see cref="MaxDistance"/>. Exceeding the web here is intentional and is the user's
    /// order of 6 Aug 2026 ("อยากเห็นเต็มแมพ"), not an accident of porting.
    /// </summary>
    public static class CameraRange
    {
        // ── builder.html:525 / :709-722, one constant per number on those lines ───

        /// <summary>The web's sand disc radius before any area scaling — builder.html:525.</summary>
        public const double SandRadius = 340.0;

        /// <summary>Ceiling as a multiple of the map's reach — builder.html:714.</summary>
        public const double MaxDistK = 3.5;

        /// <summary>Ceiling floor with fog on (underwater) — builder.html:714.</summary>
        public const double MaxDistFloorFoggy = 2600.0;

        /// <summary>…and with fog off (the web's daylight mode, where you can see forever).</summary>
        public const double MaxDistFloorClear = 3600.0;

        /// <summary>Far plane = <c>maxD·2.5 + reach + 1200</c> — builder.html:715.</summary>
        public const double FarK = 2.5;

        /// <summary>…the <c>+ 1200</c> on that same line.</summary>
        public const double FarPad = 1200.0;

        /// <summary>Near plane = <c>max(0.5, far/40000)</c> — builder.html:715.</summary>
        public const double NearDiv = 40000.0;

        /// <summary>…the <c>0.5</c> on that same line.</summary>
        public const double NearFloor = 0.5;

        /// <summary>Fog near = <c>max(500, reach·1.1)</c> — builder.html:719.</summary>
        public const double FogNearK = 1.1;

        /// <summary>…the <c>500</c> on that same line.</summary>
        public const double FogNearFloor = 500.0;

        /// <summary>Fog far = <c>max(9000, maxD·3.4)</c> — builder.html:719.</summary>
        public const double FogFarK = 3.4;

        /// <summary>…the <c>9000</c> on that same line.</summary>
        public const double FogFarFloor = 9000.0;

        /// <summary>Everything <c>updateViewRange()</c> writes, in one value.</summary>
        public readonly struct ViewRange
        {
            /// <summary>Orbit zoom-out ceiling, world units.</summary>
            public readonly double MaxDistance;
            /// <summary>Camera far clip plane.</summary>
            public readonly double Far;
            /// <summary>Camera near clip plane.</summary>
            public readonly double Near;
            /// <summary>Linear fog start distance.</summary>
            public readonly double FogNear;
            /// <summary>Linear fog end distance.</summary>
            public readonly double FogFar;

            public ViewRange(double maxDistance, double far, double near, double fogNear, double fogFar)
            {
                MaxDistance = maxDistance; Far = far; Near = near; FogNear = fogNear; FogFar = fogFar;
            }
        }

        /// <summary>
        /// The distance at which a sphere of radius <paramref name="reach"/> exactly fills the
        /// NARROWER of the two fields of view.
        ///
        /// Straight trigonometry, no fudge factor: a sphere of radius r seen from distance d
        /// subtends a half-angle of <c>asin(r/d)</c>, so it fits inside a half-FOV of θ exactly
        /// when <c>d ≥ r / sin θ</c>. The half-angles themselves are the standard perspective
        /// pair — vertical is <c>fov/2</c>, and horizontal is <c>atan(tan(fov/2) · aspect)</c>,
        /// which on a portrait phone is much the smaller of the two and is therefore the one that
        /// decides whether the map fits.
        ///
        /// Worked, for the values this app actually ships with (fov 60°, a 1080×2340 phone held
        /// upright, aspect 0.4615):
        ///   tan 30° = 0.5774 → tan θ_h = 0.5774 × 0.4615 = 0.2665 → θ_h = 14.92°
        ///   d = r / sin 14.92° = 3.88 r
        /// against the web's 3.5 r — so on a phone the web's own multiplier leaves about a tenth
        /// of the map's width outside the frame, and "อยากเห็นเต็มแมพ" is not satisfiable by
        /// transcribing 3.5 alone. On a landscape tablet (aspect 1.6) the vertical FOV is the
        /// narrow one, this returns 2.00 r, and the web's 3.5 r wins instead.
        /// </summary>
        /// <param name="reach">Content radius in world units.</param>
        /// <param name="fovDeg">The camera's VERTICAL field of view, degrees.</param>
        /// <param name="aspect">Viewport width ÷ height.</param>
        public static double FitDistance(double reach, double fovDeg, double aspect)
        {
            if (!(reach > 0.0)) return 0.0;

            double fov = Clamp(fovDeg > 0.0 ? fovDeg : 60.0, 1.0, 179.0);
            double tanV = Math.Tan(fov * 0.5 * Math.PI / 180.0);
            double a = aspect > 1e-6 ? aspect : 1.0;
            double tanH = tanV * a;

            double halfAngle = Math.Atan(Math.Min(tanV, tanH));
            double s = Math.Sin(halfAngle);
            if (s < 1e-6) return reach / 1e-6;
            return reach / s;
        }

        /// <summary>
        /// The zoom-out ceiling for a map of this size: the web's answer, or the distance that
        /// actually fits it on this screen, whichever is further.
        ///
        /// See the class comment for why the second term exists and why exceeding the web here is
        /// deliberate. The <c>max</c> means the web's number is a FLOOR on every device it was
        /// right for, so nothing that already framed correctly moves.
        /// </summary>
        public static double MaxDistance(double reach, bool foggy, double fovDeg, double aspect)
        {
            double r = reach > 0.0 ? reach : SandRadius;
            double floor = foggy ? MaxDistFloorFoggy : MaxDistFloorClear;
            double web = Math.Max(floor, r * MaxDistK);
            return Math.Max(web, FitDistance(r, fovDeg, aspect));
        }

        /// <summary>
        /// <c>updateViewRange()</c>, whole (builder.html:709-722), for a map whose content radius
        /// is <paramref name="reach"/>.
        ///
        /// The far plane and the fog are derived FROM the ceiling rather than fixed alongside it,
        /// which is the part that makes a bigger ceiling usable instead of just legal: pull back to
        /// <c>maxD</c> and the far rim of the map sits at <c>maxD + reach</c>, comfortably inside
        /// <c>maxD·2.5 + reach + 1200</c>, and the fog — which ends at <c>maxD·3.4</c> — has only
        /// washed it by roughly the same fraction it washes the rim of a small map at a small
        /// ceiling. The map is still there at full zoom-out, and it is the same colour it was.
        /// </summary>
        public static ViewRange For(double reach, bool foggy, double fovDeg, double aspect)
        {
            double r = reach > 0.0 ? reach : SandRadius;

            double maxD = MaxDistance(r, foggy, fovDeg, aspect);
            double far = maxD * FarK + r + FarPad;
            double near = Math.Max(NearFloor, far / NearDiv);
            double fogNear = Math.Max(FogNearFloor, r * FogNearK);
            double fogFar = Math.Max(FogFarFloor, maxD * FogFarK);

            return new ViewRange(maxD, far, near, fogNear, fogFar);
        }

        private static double Clamp(double v, double lo, double hi)
            => v < lo ? lo : (v > hi ? hi : v);
    }
}
