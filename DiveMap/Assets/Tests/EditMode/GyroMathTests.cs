using System;
using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// Unit tests for the AR head-tracking transform.
    ///
    /// These are written as INVARIANTS rather than expected numbers, on purpose. A test that
    /// asserts "attitude q gives quaternion (0.5, 0.5, -0.5, 0.5)" only re-states whatever the
    /// implementation happened to do, and would pass just as happily with the world mirrored. Each
    /// test below states something that must be true of any correct AR camera and would fail if the
    /// terms were composed in the wrong order or on the wrong side.
    ///
    /// Writing them this way paid for itself: the screen term's sign looked like something only a
    /// phone could settle, until it was stated as "a physical roll and the reported display angle
    /// must cancel" — which is checkable here, and only one sign passes. What is left needing a
    /// device is the single handedness negation in <c>ToUnity</c>; everything around it is pinned,
    /// so a mirrored view on the first device run is a one-character fix, not a rewrite.
    /// </summary>
    public class GyroMathTests
    {
        private const double Deg = Math.PI / 180.0;
        private static readonly Vec3 X = new Vec3(1, 0, 0);
        private static readonly Vec3 Y = new Vec3(0, 1, 0);
        private static readonly Vec3 Z = new Vec3(0, 0, 1);

        /// <summary>Phone raised to eye level, screen facing you — the pose AR is used in.</summary>
        private static readonly Quat Upright = Quat.FromAxisAngle(new Vec3(1, 0, 0), 90 * Deg);

        private static double Len(Vec3 v) => Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        private static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        private static void AssertFinite(Quat q)
        {
            Assert.IsFalse(double.IsNaN(q.X) || double.IsNaN(q.Y) || double.IsNaN(q.Z) || double.IsNaN(q.W),
                           "a NaN rotation is a black screen that never recovers");
        }

        // ── the physical anchor ──────────────────────────────────────────────────

        [Test]
        public void PhoneFlatOnTheTable_LooksDownAtTheTable()
        {
            // The one claim here that is physics and not convention: with the phone lying flat and
            // screen up, its REAR camera faces the table. So the AR view must point down.
            Vec3 fwd = GyroMath.Forward(Quat.Identity, 0);
            Assert.AreEqual(-1.0, fwd.Y, 1e-9, "the rear camera faces the table, so the view looks down");
            Assert.AreEqual(0.0, fwd.X, 1e-9);
            Assert.AreEqual(0.0, fwd.Z, 1e-9);
        }

        [Test]
        public void LiftingThePhoneUpright_BringsTheViewToTheHorizon()
        {
            // Tip the phone up 90° about its own X — the gesture of raising it to look through.
            Vec3 fwd = GyroMath.Forward(Upright, 0);
            Assert.AreEqual(0.0, fwd.Y, 1e-9, "held upright, the view is level — not the floor, not the sky");
            Assert.AreEqual(1.0, Len(fwd), 1e-9);
            Assert.AreEqual(1.0, GyroMath.Up(Upright, 0).Y, 1e-9, "…and the right way up");
        }

        // ── the screen term ──────────────────────────────────────────────────────

        [Test]
        public void ScreenRotation_TurnsThePictureWithoutMovingWhereItPoints()
        {
            // THE test for the right-hand term. Rotating the display must roll the image about the
            // view axis. Put it on the left instead and the camera would swing off target every
            // time the phone was turned sideways.
            Quat att = Quat.FromAxisAngle(X, -70 * Deg);

            Vec3 fwd0 = GyroMath.Forward(att, 0);
            foreach (double angle in new double[] { 90, 180, 270 })
            {
                Vec3 fwd = GyroMath.Forward(att, angle);
                Assert.AreEqual(fwd0.X, fwd.X, 1e-9, $"at {angle}° the view drifted");
                Assert.AreEqual(fwd0.Y, fwd.Y, 1e-9, $"at {angle}° the view drifted");
                Assert.AreEqual(fwd0.Z, fwd.Z, 1e-9, $"at {angle}° the view drifted");
            }
        }

        [Test]
        public void ScreenRotation_RollsTheImageByExactlyThatAngle()
        {
            Quat att = Quat.FromAxisAngle(X, -70 * Deg);
            Vec3 up0 = GyroMath.Up(att, 0);

            foreach (double angle in new double[] { 30, 90, 180, 270 })
            {
                Vec3 up = GyroMath.Up(att, angle);
                double cos = Dot(up0, up) / (Len(up0) * Len(up));
                Assert.AreEqual(Math.Cos(angle * Deg), cos, 1e-9,
                                $"screen at {angle}° must roll the picture {angle}°, no more, no less");
            }
        }

        [Test]
        public void ScreenRotation_ComesFullCircle()
        {
            Quat att = Quat.FromAxisAngle(Y, 33 * Deg);
            Assert.AreEqual(0.0, Quat.AngleBetween(GyroMath.CameraRotation(att, 0),
                                                   GyroMath.CameraRotation(att, 360)), 1e-9);
        }

        [Test]
        public void TurningThePhoneSideways_LeavesThePictureExactlyWhereItWas()
        {
            // The oracle for the screen term, and the reason its sign is not guesswork: when the
            // user rolls the phone by θ, the display reports θ, and the two must cancel to
            // NOTHING. Only one sign does that — the other leaves the world upside down (a
            // 2.0 error in the up vector, i.e. fully inverted), which is why this is asserted over
            // several attitudes and angles rather than one.
            var poses = new[]
            {
                Upright,
                Quat.FromAxisAngle(X, 70 * Deg),
                GyroMath.Mul(Quat.FromAxisAngle(Y, 35 * Deg), Upright),
                Quat.Identity,
            };

            foreach (Quat pose in poses)
            foreach (double roll in new double[] { 45, 90, 180, 270 })
            {
                // The roll happens about the phone's OWN screen normal, hence right-multiplied.
                Quat rolled = GyroMath.Mul(pose, Quat.FromAxisAngle(Z, roll * Deg));
                Vec3 before = GyroMath.Up(pose, 0);
                Vec3 after = GyroMath.Up(rolled, roll);

                Assert.AreEqual(before.X, after.X, 1e-9, $"roll {roll}° was not cancelled");
                Assert.AreEqual(before.Y, after.Y, 1e-9, $"roll {roll}° was not cancelled");
                Assert.AreEqual(before.Z, after.Z, 1e-9, $"roll {roll}° was not cancelled");
            }
        }

        // ── composition ──────────────────────────────────────────────────────────

        [Test]
        public void TheResultIsAlwaysAUnitRotation()
        {
            // Sensors deliver slightly denormalised quaternions all day; drift there would slowly
            // skew the whole scene rather than fail.
            var att = new Quat(0.31, -0.62, 0.11, 0.77);   // deliberately not unit length
            Quat r = GyroMath.CameraRotation(att, 137);
            Assert.AreEqual(1.0, Math.Sqrt(r.X * r.X + r.Y * r.Y + r.Z * r.Z + r.W * r.W), 1e-9);
            Assert.AreEqual(1.0, Len(GyroMath.Forward(att, 137)), 1e-9);
        }

        [Test]
        public void DistinctAttitudesGiveDistinctViews()
        {
            // Guards against a term that silently swallows the sensor (e.g. multiplying by zero).
            Vec3 a = GyroMath.Forward(Quat.FromAxisAngle(Y, 10 * Deg), 0);
            Vec3 b = GyroMath.Forward(Quat.FromAxisAngle(Y, 100 * Deg), 0);
            Assert.Greater(Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) + Math.Abs(a.Z - b.Z), 0.5);
        }

        [Test]
        public void TurningThePhoneTurnsTheViewByTheSameAmount()
        {
            // The transform is a rotation composition, so it must preserve angles: turn 40° and the
            // view turns 40°. A scaled or skewed term would show up here as drift.
            Quat turned = GyroMath.Mul(Quat.FromAxisAngle(Y, 40 * Deg), Upright);
            double moved = Quat.AngleBetween(GyroMath.CameraRotation(Upright, 0),
                                             GyroMath.CameraRotation(turned, 0));
            Assert.AreEqual(40 * Deg, moved, 1e-9);
        }

        // ── bad input ────────────────────────────────────────────────────────────

        [Test]
        public void ASensorThatHasNotReportedYet_IsIgnoredNotPropagated()
        {
            // Before the first sample Unity hands back all zeros. Normalising that is NaN.
            AssertFinite(GyroMath.CameraRotation(new Quat(0, 0, 0, 0), 0));
            Assert.AreEqual(0.0, Quat.AngleBetween(GyroMath.ToUnity(new Quat(0, 0, 0, 0)), Quat.Identity), 1e-9);
        }

        [Test]
        public void NaNAndInfinityNeverReachTheCamera()
        {
            AssertFinite(GyroMath.CameraRotation(new Quat(double.NaN, 0, 0, 1), 0));
            AssertFinite(GyroMath.CameraRotation(new Quat(0, double.PositiveInfinity, 0, 1), 90));
            AssertFinite(GyroMath.CameraRotation(Quat.Identity, double.NaN));
        }

        // ── the helpers ──────────────────────────────────────────────────────────

        [Test]
        public void Mul_AppliesTheRightHandFirst()
        {
            // Order matters everywhere above; assert it directly so a "fix" to Mul cannot quietly
            // reverse every composition in the file.
            Quat yaw = Quat.FromAxisAngle(Y, 90 * Deg);
            Quat pitch = Quat.FromAxisAngle(X, 90 * Deg);

            // pitch-then-yaw sends +Z to… pitch: +Z→−Y (unchanged by the later yaw about Y)
            Vec3 v = GyroMath.Rotate(GyroMath.Mul(yaw, pitch), Z);
            Assert.AreEqual(-1.0, v.Y, 1e-9);

            // the other order lands somewhere else entirely
            Vec3 w = GyroMath.Rotate(GyroMath.Mul(pitch, yaw), Z);
            Assert.AreEqual(1.0, w.X, 1e-9);
        }

        [Test]
        public void Rotate_PreservesLength()
        {
            Vec3 v = GyroMath.Rotate(Quat.FromAxisAngle(new Vec3(0.577, 0.577, 0.577), 1.1), new Vec3(3, -4, 12));
            Assert.AreEqual(13.0, Len(v), 1e-9);
        }

        [Test]
        public void Rotate_IdentityChangesNothing()
        {
            Vec3 v = GyroMath.Rotate(Quat.Identity, new Vec3(1, 2, 3));
            Assert.AreEqual(1, v.X, 1e-12);
            Assert.AreEqual(2, v.Y, 1e-12);
            Assert.AreEqual(3, v.Z, 1e-12);
        }
    }
}
