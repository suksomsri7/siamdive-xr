using System;
using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// Unit tests for the drag arithmetic.
    ///
    /// A gizmo only ever gets reported as "it feels wrong", so the things that make it feel
    /// wrong are pinned here as numbers: inverted axes, a scale that can reach zero, a drag that
    /// teleports the object when the ray misses the plane, and a tap that moves things.
    /// </summary>
    public class GizmoMathTests
    {
        // ── ray / plane ──────────────────────────────────────────────────────────

        [Test]
        public void RayOnPlane_HitsWhereItShould()
        {
            // From (0,100,0) straight down onto y=0.
            Assert.IsTrue(GizmoMath.RayOnPlane(0, 100, 0, 0, -1, 0, 0, out double x, out double z));
            Assert.AreEqual(0, x, 1e-9);
            Assert.AreEqual(0, z, 1e-9);

            // 45° forward-and-down from 10 units up → lands 10 units along +Z.
            Assert.IsTrue(GizmoMath.RayOnPlane(0, 10, 0, 0, -1, 1, 0, out _, out double z2));
            Assert.AreEqual(10, z2, 1e-9);
        }

        [Test]
        public void RayOnPlane_RefusesAParallelRay()
        {
            Assert.IsFalse(GizmoMath.RayOnPlane(0, 5, 0, 1, 0, 0, 0, out _, out _),
                           "a horizontal ray never meets a horizontal plane");
        }

        [Test]
        public void RayOnPlane_RefusesAPlaneBehindTheCamera()
        {
            // Looking UP, with the plane below: the intersection is behind the viewer. Following
            // it would fling the object to the far side of the map.
            Assert.IsFalse(GizmoMath.RayOnPlane(0, 5, 0, 0, 1, 0, 0, out _, out _));
        }

        [Test]
        public void RayOnPlane_LeavesTheOutputsAloneWhenItFails()
        {
            GizmoMath.RayOnPlane(7, 5, 9, 1, 0, 0, 0, out double x, out double z);
            Assert.AreEqual(7, x, 1e-9, "a failed hit must not produce a garbage position");
            Assert.AreEqual(9, z, 1e-9);
        }

        [Test]
        public void RayOnPlane_WorksAtANonZeroHeight()
        {
            Assert.IsTrue(GizmoMath.RayOnPlane(0, 50, 0, 0, -1, 0, 20, out _, out _),
                          "objects sit at their own height, not at y=0");
        }

        // ── rotate ───────────────────────────────────────────────────────────────

        [Test]
        public void Yaw_AFullTurnIsOneDragOfPixelsPerTurn()
        {
            double y = GizmoMath.YawAfterDrag(0, GizmoMath.PixelsPerTurn);
            Assert.AreEqual(0, y, 1e-6, "a whole turn comes back to where it started");
        }

        [Test]
        public void Yaw_HalfADragIsHalfATurn()
        {
            Assert.AreEqual(Math.PI, Math.Abs(GizmoMath.YawAfterDrag(0, GizmoMath.PixelsPerTurn / 2)), 1e-6);
        }

        [Test]
        public void Yaw_DraggingBackUndoesDraggingForward()
        {
            double there = GizmoMath.YawAfterDrag(0.4, 130);
            double back = GizmoMath.YawAfterDrag(there, -130);
            Assert.AreEqual(0.4, back, 1e-6, "the gesture must be symmetric or it feels like drift");
        }

        [Test]
        public void Wrap_KeepsAnglesInRange()
        {
            Assert.AreEqual(0, GizmoMath.Wrap(Math.PI * 2), 1e-9);
            Assert.AreEqual(-Math.PI / 2, GizmoMath.Wrap(Math.PI * 1.5), 1e-9);
            Assert.LessOrEqual(Math.Abs(GizmoMath.Wrap(Math.PI * 40 + 1)), Math.PI);
        }

        // ── scale ────────────────────────────────────────────────────────────────

        [Test]
        public void Scale_DoublesAfterTheNamedDistance()
        {
            Assert.AreEqual(2.0, GizmoMath.ScaleAfterDrag(1.0, GizmoMath.PixelsPerDouble), 1e-6);
            Assert.AreEqual(0.5, GizmoMath.ScaleAfterDrag(1.0, -GizmoMath.PixelsPerDouble), 1e-6);
        }

        [Test]
        public void Scale_IsSymmetric()
        {
            double up = GizmoMath.ScaleAfterDrag(1.0, 173);
            Assert.AreEqual(1.0, GizmoMath.ScaleAfterDrag(up, -173), 1e-6);
        }

        [Test]
        public void Scale_CanNeverReachZeroOrRunAway()
        {
            Assert.AreEqual(SceneEdit.MinScale, GizmoMath.ScaleAfterDrag(1.0, -100000), 1e-9,
                            "a zero-scale object is invisible AND cannot be picked again");
            Assert.AreEqual(SceneEdit.MaxScale, GizmoMath.ScaleAfterDrag(1.0, 100000), 1e-9);
        }

        [Test]
        public void Scale_TreatsAMissingStartAsOne()
        {
            Assert.AreEqual(2.0, GizmoMath.ScaleAfterDrag(0.0, GizmoMath.PixelsPerDouble), 1e-6);
        }

        // ── tap vs drag ──────────────────────────────────────────────────────────

        [Test]
        public void IsDrag_IgnoresAShakyTap()
        {
            Assert.IsFalse(GizmoMath.IsDrag(0, 0));
            Assert.IsFalse(GizmoMath.IsDrag(5, 5), "7.07 px — still a tap");
            Assert.IsTrue(GizmoMath.IsDrag(0, GizmoMath.DragThresholdPixels));
            Assert.IsTrue(GizmoMath.IsDrag(-40, 3));
        }
    }
}
