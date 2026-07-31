using System;

namespace DiveMap.Core
{
    /// <summary>
    /// The arithmetic behind dragging a selected object — move, rotate, scale.
    ///
    /// Pure on purpose. A gizmo is the part of an editor where "it feels wrong" is the only bug
    /// report you ever get, and "feels wrong" is almost always one of these: the drag axis is
    /// inverted, the speed is tied to screen pixels instead of world distance, or a scale can
    /// reach zero and the object becomes unselectable. All three are testable numbers.
    ///
    /// The web's gizmo is three.js <c>TransformControls</c> (<c>tc</c>), which drags along a
    /// ray/plane intersection. This reproduces the same behaviour without the dependency:
    ///  • MOVE   — the object follows the finger across a horizontal plane at its own height,
    ///             so it slides along the seabed rather than flying toward the camera.
    ///  • ROTATE — horizontal drag = yaw. Vertical drag is ignored; on a touch screen a two-axis
    ///             rotation from one finger is unpredictable, and the web only exposes Y here.
    ///  • SCALE  — horizontal drag, exponential so that dragging right doubles and dragging the
    ///             same distance left halves. Linear scaling cannot reach small sizes smoothly.
    /// </summary>
    public static class GizmoMath
    {
        /// <summary>Screen px of horizontal drag for one full turn (rotate mode).</summary>
        public const double PixelsPerTurn = 420.0;

        /// <summary>Screen px of horizontal drag that doubles the size (scale mode).</summary>
        public const double PixelsPerDouble = 260.0;

        /// <summary>
        /// Where a ray crosses the horizontal plane at height <paramref name="planeY"/>.
        /// Returns false when the ray is parallel to the plane, or points away from it — in both
        /// cases there is no sensible drop point and the caller must keep the last good one
        /// rather than teleport the object to infinity.
        /// </summary>
        public static bool RayOnPlane(double ox, double oy, double oz,
                                      double dx, double dy, double dz,
                                      double planeY,
                                      out double x, out double z)
        {
            x = ox; z = oz;
            if (Math.Abs(dy) < 1e-6) return false;      // parallel

            double t = (planeY - oy) / dy;
            if (t <= 0.0 || double.IsNaN(t) || double.IsInfinity(t)) return false;   // behind the camera

            x = ox + dx * t;
            z = oz + dz * t;
            return true;
        }

        /// <summary>
        /// Yaw after a horizontal drag of <paramref name="dxPixels"/>, in radians.
        /// Dragging right turns clockwise seen from above, which is what a finger "pushing" the
        /// near side of an object does.
        /// </summary>
        public static double YawAfterDrag(double startYaw, double dxPixels)
        {
            return Wrap(startYaw + dxPixels / PixelsPerTurn * (Math.PI * 2.0));
        }

        /// <summary>Fold an angle into −π…π so a long drag does not accumulate huge numbers.</summary>
        public static double Wrap(double radians)
        {
            const double twoPi = Math.PI * 2.0;
            double r = radians % twoPi;
            if (r > Math.PI) r -= twoPi;
            else if (r < -Math.PI) r += twoPi;
            return r;
        }

        /// <summary>
        /// Scale after a horizontal drag. Exponential, so the gesture is symmetric: drag 260 px
        /// right → ×2, drag 260 px back → ×1 again, exactly where it started.
        /// </summary>
        public static double ScaleAfterDrag(double startScale, double dxPixels)
        {
            if (startScale <= 0.0) startScale = 1.0;
            double factor = Math.Pow(2.0, dxPixels / PixelsPerDouble);
            return SceneEdit.ClampScale(startScale * factor);
        }

        /// <summary>
        /// True when the pointer has moved far enough to count as a drag rather than a tap.
        /// Below this, a shaky finger on a "select" tap would nudge the object a few units and
        /// the player would never know why their reef drifts.
        /// </summary>
        public const double DragThresholdPixels = 8.0;

        public static bool IsDrag(double dxPixels, double dyPixels)
        {
            return dxPixels * dxPixels + dyPixels * dyPixels >=
                   DragThresholdPixels * DragThresholdPixels;
        }
    }
}
