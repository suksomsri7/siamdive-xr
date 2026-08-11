using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// WO-O — the per-axis handles.
    ///
    /// These tests exist because of a promise: axis arrows that do not actually constrain "look
    /// right and lie", and this repo has been burned by that class of thing before. A screenshot
    /// cannot tell a real X-only drag from a free drag that happens to move rightwards — the
    /// numbers can, so the constraint is asserted here rather than eyeballed in CI.
    /// </summary>
    public class GizmoHandleTests
    {
        private const double Eps = 1e-6;

        // ── screen-constant sizing ───────────────────────────────────────────────

        [Test]
        public void WorldPerPixel_GrowsWithDistanceSoTheGizmoStaysTheSameSizeOnScreen()
        {
            // The whole point of the scaling: twice as far away ⇒ each pixel covers twice as much
            // world, so an arrow asked to be 90 px long is built twice as big and LOOKS identical.
            double near = GizmoMath.WorldPerPixel(10.0, 60.0, 1000.0);
            double far = GizmoMath.WorldPerPixel(20.0, 60.0, 1000.0);
            Assert.AreEqual(near * 2.0, far, Eps);

            // 60° vertical fov, 1000 px tall, 10 units away: half-height = 10·tan(30°) ≈ 5.7735,
            // full height ≈ 11.547 world units over 1000 px.
            Assert.AreEqual(11.547005 / 1000.0, near, 1e-6);
        }

        [Test]
        public void WorldPerPixel_RefusesNonsenseRatherThanReturningInfinity()
        {
            // A zero would make every handle zero-length (invisible but harmless); an infinity
            // would make one fill the universe and take every press on screen.
            Assert.AreEqual(0.0, GizmoMath.WorldPerPixel(10.0, 60.0, 0.0));
            Assert.AreEqual(0.0, GizmoMath.WorldPerPixel(10.0, 0.0, 1000.0));
            Assert.AreEqual(0.0, GizmoMath.WorldPerPixel(10.0, 180.0, 1000.0));
            // Behind the camera is still a distance, not a negative size.
            Assert.Greater(GizmoMath.WorldPerPixel(-10.0, 60.0, 1000.0), 0.0);
        }

        // ── screen-space picking ─────────────────────────────────────────────────

        [Test]
        public void DistanceToSegment2D_ClampsToTheEnds()
        {
            // Perpendicular in the middle…
            Assert.AreEqual(5.0, GizmoMath.DistanceToSegment2D(50, 5, 0, 0, 100, 0), Eps);
            // …but past the tip it is the distance to the TIP, not to the infinite line. An
            // infinite line would let a press far beyond the arrow still grab it.
            Assert.AreEqual(30.0, GizmoMath.DistanceToSegment2D(130, 0, 0, 0, 100, 0), Eps);
            Assert.AreEqual(10.0, GizmoMath.DistanceToSegment2D(-10, 0, 0, 0, 100, 0), Eps);
            // A degenerate segment is a point, not a divide-by-zero.
            Assert.AreEqual(5.0, GizmoMath.DistanceToSegment2D(0, 5, 7, 0, 7, 0), 5.0);
        }

        /// <summary>Origin at (100,100); X right, Y up the screen, Z down-left; planes between them.</summary>
        private static GizmoMath.Handle PickAt(double px, double py) =>
            GizmoMath.Pick(px, py,
                           100, 100,      // origin
                           200, 100,      // X tip  (right)
                           100, 200,      // Y tip  (up)
                           40, 60,        // Z tip  (down-left)
                           140, 140,      // XY quad
                           80, 130,       // YZ quad
                           130, 85);      // XZ quad

        [Test]
        public void Pick_EachArrowClaimsItsOwnShaft()
        {
            Assert.AreEqual(GizmoMath.Handle.X, PickAt(180, 100));
            Assert.AreEqual(GizmoMath.Handle.Y, PickAt(100, 185));
            Assert.AreEqual(GizmoMath.Handle.Z, PickAt(55, 70));
        }

        [Test]
        public void Pick_MissesEntirelyWhenTheThumbIsNowhereNear()
        {
            // The map behind the gizmo has to stay reachable, or a selected object would make the
            // whole screen un-orbitable.
            Assert.AreEqual(GizmoMath.Handle.None, PickAt(600, 600));
            Assert.AreEqual(GizmoMath.Handle.None, PickAt(100, 400));
        }

        [Test]
        public void Pick_PlanesWinTheCrowdedAreaNearTheOrigin()
        {
            // 🔴 The quads sit in the wedge between two arrows, where the axis segments also
            // pass. Scored purely by distance the axes would swallow them and the two-axis move
            // would be unreachable — so a press inside a quad's radius takes the quad.
            Assert.AreEqual(GizmoMath.Handle.XY, PickAt(140, 140));
            Assert.AreEqual(GizmoMath.Handle.XZ, PickAt(130, 85));
            Assert.AreEqual(GizmoMath.Handle.YZ, PickAt(80, 130));
        }

        [Test]
        public void Pick_SkipsHandlesTheCallerCouldNotProject()
        {
            // A handle behind the camera arrives as NaN. Projected naively it lands mirrored on
            // the far side of the screen and grabs presses meant for the map.
            GizmoMath.Handle h = GizmoMath.Pick(180, 100,
                                                100, 100,
                                                double.NaN, double.NaN,   // X behind the camera
                                                100, 200,
                                                40, 60,
                                                double.NaN, double.NaN,
                                                80, 130,
                                                130, 85);
            Assert.AreNotEqual(GizmoMath.Handle.X, h);
            Assert.AreNotEqual(GizmoMath.Handle.XY, h);
        }

        [Test]
        public void Pick_ToleranceIsThumbSizedNotPixelPerfect()
        {
            // 26 px of slack on a shaft; a phone thumb is ~40 px across, and a gizmo you have to
            // hit exactly is a gizmo people stop using.
            Assert.Greater(GizmoMath.AxisGrabPixels, 20.0);
            Assert.AreEqual(GizmoMath.Handle.X, PickAt(180, 100 + GizmoMath.AxisGrabPixels - 1));
            Assert.AreEqual(GizmoMath.Handle.None, PickAt(180, 100 + GizmoMath.AxisGrabPixels + 1));
        }

        // ── the constraint itself ────────────────────────────────────────────────

        [Test]
        public void AxisParam_ReadsStraightOffTheAxisWhenTheRayCrossesIt()
        {
            // Camera 10 above the origin looking straight down; the ray through world (7,0,0)
            // meets the X axis exactly at t = 7.
            bool ok = GizmoMath.AxisParam(7, 10, 0, 0, -1, 0,
                                          0, 0, 0, 1, 0, 0, out double t);
            Assert.IsTrue(ok);
            Assert.AreEqual(7.0, t, 1e-9);
        }

        [Test]
        public void AxisParam_IsMeasuredFromTheAxisOriginNotTheWorldOrigin()
        {
            // The handles hang off the object, which is rarely at (0,0,0). Getting this wrong
            // teleports the object to the origin on the first frame of every drag.
            bool ok = GizmoMath.AxisParam(7, 10, 3, 0, -1, 0,
                                          5, 0, 3, 1, 0, 0, out double t);
            Assert.IsTrue(ok);
            Assert.AreEqual(2.0, t, 1e-9);
        }

        [Test]
        public void AxisParam_RefusesAnAxisSeenEndOn()
        {
            // Looking straight down the X axis, one pixel of finger movement would fling the
            // object across the map. Refusing means the handle stops responding — visibly inert,
            // which a user reads as "not that one", instead of destroying their layout.
            bool ok = GizmoMath.AxisParam(-10, 0, 0, 1, 0, 0,
                                          0, 0, 0, 1, 0, 0, out _);
            Assert.IsFalse(ok);
        }

        [Test]
        public void AxisParam_SurvivesUnnormalisedInput()
        {
            // Camera rays and axis vectors arrive from Unity in whatever length they happen to
            // have; the answer must be in world units regardless.
            bool ok = GizmoMath.AxisParam(7, 10, 0, 0, -3.5, 0,
                                          0, 0, 0, 4, 0, 0, out double t);
            Assert.IsTrue(ok);
            Assert.AreEqual(7.0, t, 1e-9);
        }

        [Test]
        public void AxisParam_ADiagonalRayStillLandsOnTheAxis()
        {
            // The realistic case: an orbit camera looking down at 45°. Closest approach, not an
            // intersection — but for a ray that does cross, it is the crossing.
            double inv = 1.0 / System.Math.Sqrt(2.0);
            bool ok = GizmoMath.AxisParam(4, 4, 0, 0, -inv, inv,
                                          0, 0, 4, 1, 0, 0, out double t);
            Assert.IsTrue(ok);
            Assert.AreEqual(4.0, t, 1e-6);
        }

        // ── plane handles ────────────────────────────────────────────────────────

        [Test]
        public void RayOnPlaneN_HitsAVerticalPlane()
        {
            // XY plane (normal Z) through z = 3: a ray straight along +Z lands on it.
            bool ok = GizmoMath.RayOnPlaneN(1, 2, 0, 0, 0, 1,
                                            0, 0, 3, 0, 0, 1,
                                            out double x, out double y, out double z);
            Assert.IsTrue(ok);
            Assert.AreEqual(1.0, x, Eps);
            Assert.AreEqual(2.0, y, Eps);
            Assert.AreEqual(3.0, z, Eps);
        }

        [Test]
        public void RayOnPlaneN_RefusesParallelAndBehind()
        {
            // Parallel — no crossing at all.
            Assert.IsFalse(GizmoMath.RayOnPlaneN(0, 0, 0, 1, 0, 0,
                                                 0, 0, 3, 0, 0, 1, out _, out _, out _));
            // Behind the camera: the plane crosses at t < 0, and honouring it would drop the
            // object somewhere the user cannot see. Vertical handles are often near edge-on, so
            // this is a case that happens, not a theoretical one.
            Assert.IsFalse(GizmoMath.RayOnPlaneN(0, 0, 5, 0, 0, 1,
                                                 0, 0, 3, 0, 0, 1, out _, out _, out _));
        }

        [Test]
        public void RayOnPlaneN_AgreesWithTheHorizontalHelperItGeneralises()
        {
            // RayOnPlane (free-drag mode) and RayOnPlaneN with normal Y must not disagree, or the
            // XZ quad and the old whole-screen drag would move an object to two different places.
            GizmoMath.RayOnPlane(3, 10, -2, 0.1, -1, 0.2, 4, out double ax, out double az);
            bool ok = GizmoMath.RayOnPlaneN(3, 10, -2, 0.1, -1, 0.2,
                                            0, 4, 0, 0, 1, 0,
                                            out double bx, out double by, out double bz);
            Assert.IsTrue(ok);
            Assert.AreEqual(ax, bx, 1e-9);
            Assert.AreEqual(az, bz, 1e-9);
            Assert.AreEqual(4.0, by, 1e-9);
        }

        // ── touch arbitration: the map must stay orbitable ───────────────────────

        [Test]
        public void Pick_APressBesideTheGizmoFallsThroughToTheCamera()
        {
            // 🔴 The ambiguity case, deliberately. Selecting an object must NOT swallow the whole
            // screen: a press near the object but off every handle has to return None so the
            // press reaches the orbit. Get this wrong and the map stops rotating the moment
            // anything is selected, which reads as the app freezing.
            //
            // 40 px below the origin: past the 26 px shaft radius and past the 20 px quad radius,
            // and on no arrow (X goes right, Y up, Z down-LEFT).
            Assert.AreEqual(GizmoMath.Handle.None, PickAt(100, 60));
            // Straight up beyond the Y arrow's tip — the segment clamp means the shaft does not
            // extend forever.
            Assert.AreEqual(GizmoMath.Handle.None, PickAt(100, 260));
        }

        [Test]
        public void Pick_TheOriginItselfResolvesToSomething()
        {
            // Dead centre every handle overlaps. Any answer is defensible EXCEPT None — a press
            // on the middle of the gizmo that fell through to the camera would spin the map while
            // the user was trying to move an object.
            Assert.AreNotEqual(GizmoMath.Handle.None, PickAt(100, 100));
        }

        // ── handle → geometry lookups ────────────────────────────────────────────

        [Test]
        public void AxisAndNormalLookups_AreConsistentAndTotal()
        {
            GizmoMath.AxisOf(GizmoMath.Handle.X, out double ux, out double uy, out double uz);
            Assert.AreEqual(1.0, ux); Assert.AreEqual(0.0, uy); Assert.AreEqual(0.0, uz);
            GizmoMath.AxisOf(GizmoMath.Handle.Z, out ux, out uy, out uz);
            Assert.AreEqual(1.0, uz);

            // A plane's normal is the axis it does NOT contain — XY moves in X and Y, so Z.
            GizmoMath.NormalOf(GizmoMath.Handle.XY, out double nx, out double ny, out double nz);
            Assert.AreEqual(1.0, nz); Assert.AreEqual(0.0, nx); Assert.AreEqual(0.0, ny);
            GizmoMath.NormalOf(GizmoMath.Handle.YZ, out nx, out ny, out nz);
            Assert.AreEqual(1.0, nx);
            GizmoMath.NormalOf(GizmoMath.Handle.XZ, out nx, out ny, out nz);
            Assert.AreEqual(1.0, ny);

            // Every handle is exactly one of axis / plane / none — a gap here is a drag that
            // silently does nothing.
            foreach (GizmoMath.Handle h in System.Enum.GetValues(typeof(GizmoMath.Handle)))
            {
                bool axis = GizmoMath.IsAxis(h), plane = GizmoMath.IsPlane(h);
                Assert.IsFalse(axis && plane, h.ToString());
                Assert.AreEqual(h == GizmoMath.Handle.None, !axis && !plane, h.ToString());
            }
        }
    }
}
