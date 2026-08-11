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

        // ─────────────────────────────────────────────────────────────────────────
        // WO-O — PER-AXIS HANDLES
        //
        // Everything above moves the object with the whole screen: a drag anywhere slides it
        // across a horizontal plane. That is a usable editor and it is NOT what the web is. The
        // web is three.js TransformControls: red/green/blue arrows on the object, and dragging
        // one moves along THAT axis only, with small plane quads near the origin for two-axis
        // moves. The user's reference photo shows exactly that, and the standard for this
        // project is seamlessness with the web.
        //
        // 🔴 The bar set when these were deferred: arrows that do not constrain "look right and
        // lie". So the constraint is the feature, and it lives here as arithmetic that
        // tools/test.sh can check, rather than as vectors improvised inside a MonoBehaviour.
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>Which part of the gizmo a press landed on. <see cref="None"/> = the map behind it.</summary>
        public enum Handle
        {
            None = 0,
            X = 1, Y = 2, Z = 3,       // single-axis arrows
            XY = 4, YZ = 5, XZ = 6,    // the little corner quads: move within that plane
        }

        public static bool IsAxis(Handle h) => h == Handle.X || h == Handle.Y || h == Handle.Z;
        public static bool IsPlane(Handle h) => h == Handle.XY || h == Handle.YZ || h == Handle.XZ;

        /// <summary>
        /// How many world units one screen pixel covers at <paramref name="distance"/> from the
        /// camera, for a perspective camera of vertical field of view
        /// <paramref name="fovYDegrees"/> rendering <paramref name="screenHeightPx"/> pixels tall.
        ///
        /// This is what keeps the handles the SAME SIZE on screen however far away the object is
        /// — the property that makes a gizmo usable, and the reason a fixed world-space size is
        /// wrong: a rock across the map would get arrows too small to hit, and one under the
        /// camera would get arrows that fill the screen. three.js does the same thing through
        /// TransformControls' internal `_getWorldScale`/`size` factor (the web sets `tc.size =
        /// 1.35`, builder.html:532, i.e. 35 % bigger than default for touch).
        /// </summary>
        public static double WorldPerPixel(double distance, double fovYDegrees, double screenHeightPx)
        {
            if (screenHeightPx < 1.0 || fovYDegrees <= 0.0 || fovYDegrees >= 180.0) return 0.0;
            double d = Math.Abs(distance);
            double halfFov = fovYDegrees * 0.5 * Math.PI / 180.0;
            return 2.0 * d * Math.Tan(halfFov) / screenHeightPx;
        }

        /// <summary>
        /// Distance in pixels from a point to a line SEGMENT, in screen space.
        ///
        /// Segment, not infinite line: the arrows are short, and an infinite line would claim
        /// every press that happened to sit along the axis on the far side of the map.
        /// </summary>
        public static double DistanceToSegment2D(double px, double py,
                                                 double ax, double ay,
                                                 double bx, double by)
        {
            double vx = bx - ax, vy = by - ay;
            double len2 = vx * vx + vy * vy;
            if (len2 < 1e-9) return Math.Sqrt((px - ax) * (px - ax) + (py - ay) * (py - ay));

            double t = ((px - ax) * vx + (py - ay) * vy) / len2;
            if (t < 0.0) t = 0.0;
            else if (t > 1.0) t = 1.0;

            double cx = ax + vx * t, cy = ay + vy * t;
            return Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
        }

        /// <summary>Thumb-sized grab radius for an arrow shaft, in screen pixels.</summary>
        public const double AxisGrabPixels = 26.0;

        /// <summary>Grab radius for a plane quad. Smaller: three of them share a small area.</summary>
        public const double PlaneGrabPixels = 20.0;

        /// <summary>
        /// Which handle a press at (<paramref name="px"/>,<paramref name="py"/>) grabbed, given
        /// the SCREEN positions of the gizmo origin, the three arrow tips and the three plane
        /// quad centres. Screen-space on purpose: the tolerance the user cares about is "how
        /// close does my thumb have to be", which is a pixel question, and it stays honest at
        /// any camera distance without a second projection.
        ///
        /// PLANES WIN TIES. Every plane quad sits in the wedge between two arrows, near the
        /// origin, where all three axis segments also pass — scored purely by distance the axes
        /// would swallow them, and the two-axis move would be unreachable. So a plane inside its
        /// own radius is taken first; only then are the axes considered.
        ///
        /// Behind-the-camera handles are passed in as NaN by the caller and are skipped, rather
        /// than being projected to a mirrored position that would grab presses on the wrong side
        /// of the screen.
        /// </summary>
        public static Handle Pick(double px, double py,
                                  double ox, double oy,
                                  double xtx, double xty,
                                  double ytx, double yty,
                                  double ztx, double zty,
                                  double xyx, double xyy,
                                  double yzx, double yzy,
                                  double xzx, double xzy,
                                  double axisTolerance = AxisGrabPixels,
                                  double planeTolerance = PlaneGrabPixels)
        {
            Handle best = Handle.None;
            double bestD = double.PositiveInfinity;

            // 1) planes, by distance to their centre
            TryPoint(px, py, xyx, xyy, planeTolerance, Handle.XY, ref best, ref bestD);
            TryPoint(px, py, yzx, yzy, planeTolerance, Handle.YZ, ref best, ref bestD);
            TryPoint(px, py, xzx, xzy, planeTolerance, Handle.XZ, ref best, ref bestD);
            if (best != Handle.None) return best;

            // 2) axes, by distance to the shaft
            TrySegment(px, py, ox, oy, xtx, xty, axisTolerance, Handle.X, ref best, ref bestD);
            TrySegment(px, py, ox, oy, ytx, yty, axisTolerance, Handle.Y, ref best, ref bestD);
            TrySegment(px, py, ox, oy, ztx, zty, axisTolerance, Handle.Z, ref best, ref bestD);
            return best;
        }

        private static void TryPoint(double px, double py, double cx, double cy, double tol,
                                     Handle h, ref Handle best, ref double bestD)
        {
            if (double.IsNaN(cx) || double.IsNaN(cy)) return;
            double d = Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
            if (d <= tol && d < bestD) { bestD = d; best = h; }
        }

        private static void TrySegment(double px, double py, double ax, double ay,
                                       double bx, double by, double tol,
                                       Handle h, ref Handle best, ref double bestD)
        {
            if (double.IsNaN(ax) || double.IsNaN(ay) || double.IsNaN(bx) || double.IsNaN(by)) return;
            double d = DistanceToSegment2D(px, py, ax, ay, bx, by);
            if (d <= tol && d < bestD) { bestD = d; best = h; }
        }

        /// <summary>
        /// Where along an axis the finger is: the point on the line
        /// (<paramref name="ax"/>,<paramref name="ay"/>,<paramref name="az"/>) + t·(unit axis)
        /// that comes CLOSEST to the pointer ray. Returns t in world units.
        ///
        /// This is the whole constraint. A ray and a line in 3D almost never meet, so "where did
        /// the finger put it" has no exact answer — the closest approach is the one that behaves
        /// the way a hand expects, and it is what TransformControls uses too.
        ///
        /// Fails when the ray is within about half a degree of parallel to the axis: there the
        /// closest point races off to infinity and a pixel of finger movement would fling the
        /// object across the map. The caller keeps the last good value instead, so a handle seen
        /// edge-on simply stops responding rather than exploding.
        /// </summary>
        public static bool AxisParam(double ox, double oy, double oz,
                                     double dx, double dy, double dz,
                                     double ax, double ay, double az,
                                     double ux, double uy, double uz,
                                     out double t)
        {
            t = 0.0;

            double dlen = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            double ulen = Math.Sqrt(ux * ux + uy * uy + uz * uz);
            if (dlen < 1e-9 || ulen < 1e-9) return false;
            dx /= dlen; dy /= dlen; dz /= dlen;
            ux /= ulen; uy /= ulen; uz /= ulen;

            // Closest approach of line A+t·U and ray O+s·D, both directions unit length:
            //   b = U·D,  denom = 1 − b²  (zero ⇔ parallel)
            double b = ux * dx + uy * dy + uz * dz;
            double denom = 1.0 - b * b;
            if (denom < 1e-4) return false;             // ~0.57° of parallel

            double wx = ax - ox, wy = ay - oy, wz = az - oz;
            double dw = ux * wx + uy * wy + uz * wz;    // U·w0
            double ew = dx * wx + dy * wy + dz * wz;    // D·w0

            t = (b * ew - dw) / denom;
            return !double.IsNaN(t) && !double.IsInfinity(t);
        }

        /// <summary>
        /// Where a ray crosses an arbitrary plane through
        /// (<paramref name="px"/>,<paramref name="py"/>,<paramref name="pz"/>) with normal
        /// (<paramref name="nx"/>,<paramref name="ny"/>,<paramref name="nz"/>).
        ///
        /// <see cref="RayOnPlane"/> above only does horizontal planes, which is all the
        /// free-drag mode ever needed. The XY and YZ handles need vertical ones, and rejecting a
        /// hit BEHIND the camera matters more here: those planes are commonly seen near edge-on,
        /// where the far side of the plane is behind the viewer and the intersection would place
        /// the object somewhere the user cannot see.
        /// </summary>
        public static bool RayOnPlaneN(double ox, double oy, double oz,
                                       double dx, double dy, double dz,
                                       double px, double py, double pz,
                                       double nx, double ny, double nz,
                                       out double x, out double y, out double z)
        {
            x = ox; y = oy; z = oz;

            double denom = dx * nx + dy * ny + dz * nz;
            if (Math.Abs(denom) < 1e-6) return false;   // parallel to the plane

            double t = ((px - ox) * nx + (py - oy) * ny + (pz - oz) * nz) / denom;
            if (t <= 0.0 || double.IsNaN(t) || double.IsInfinity(t)) return false;

            x = ox + dx * t;
            y = oy + dy * t;
            z = oz + dz * t;
            return true;
        }

        /// <summary>The unit vector for a single-axis handle; (0,0,0) for anything else.</summary>
        public static void AxisOf(Handle h, out double ux, out double uy, out double uz)
        {
            ux = uy = uz = 0.0;
            if (h == Handle.X) ux = 1.0;
            else if (h == Handle.Y) uy = 1.0;
            else if (h == Handle.Z) uz = 1.0;
        }

        /// <summary>The unit NORMAL of a plane handle; (0,0,0) for anything else.</summary>
        public static void NormalOf(Handle h, out double nx, out double ny, out double nz)
        {
            nx = ny = nz = 0.0;
            if (h == Handle.XY) nz = 1.0;        // the plane spanned by X and Y ⇒ normal Z
            else if (h == Handle.YZ) nx = 1.0;
            else if (h == Handle.XZ) ny = 1.0;
        }
    }
}
