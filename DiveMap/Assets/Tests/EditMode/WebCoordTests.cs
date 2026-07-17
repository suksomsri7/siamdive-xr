using System;
using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    public class WebCoordTests
    {
        private const double Eps = 1e-9;
        private const double AngleEps = 1e-7; // radians

        // ── Position round-trip across all octants ────────────────────────────────
        [Test]
        public void Position_RoundTrip_AllOctants()
        {
            double[] coords = { -12.5, -3.0, -0.001, 0.0, 0.001, 4.2, 88.0 };
            foreach (var x in coords)
            foreach (var y in new[] { -5.0, 0.0, 7.3 })
            foreach (var z in coords)
            {
                var web = new Vec3(x, y, z);
                var back = WebCoord.PositionToWeb(WebCoord.PositionToUnity(web));
                Assert.AreEqual(web.X, back.X, Eps);
                Assert.AreEqual(web.Y, back.Y, Eps);
                Assert.AreEqual(web.Z, back.Z, Eps);
            }
        }

        [Test]
        public void Position_ZisFlipped()
        {
            var u = WebCoord.PositionToUnity(new Vec3(1, 2, 3));
            Assert.AreEqual(1, u.X, Eps);
            Assert.AreEqual(2, u.Y, Eps);
            Assert.AreEqual(-3, u.Z, Eps);
        }

        // ── Rotation round-trip: compare via quaternion angle, not raw Euler ──────
        [Test]
        public void Rotation_RoundTrip_ManyAngles()
        {
            double d2r = Math.PI / 180.0;
            double[] angles = { -170, -120, -95, -45, -10, 0, 10, 45, 95, 120, 170 };

            foreach (var ax in angles)
            foreach (var ay in new[] { -100.0, -30.0, 0.0, 30.0, 100.0 })
            foreach (var az in new[] { -150.0, 0.0, 60.0 })
            {
                var webEuler = new Vec3(ax * d2r, ay * d2r, az * d2r);

                // web Euler -> Unity quat -> web Euler (save path)
                Quat unityQ = WebCoord.RotationToUnity(webEuler);
                Vec3 backEuler = WebCoord.RotationToWeb(unityQ);

                // Compare orientations, not Euler triples (avoids gimbal ambiguity).
                Quat original = WebCoord.EulerXYZToQuat(webEuler.X, webEuler.Y, webEuler.Z);
                Quat recovered = WebCoord.EulerXYZToQuat(backEuler.X, backEuler.Y, backEuler.Z);
                double err = Quat.AngleBetween(original, recovered);
                Assert.Less(err, AngleEps, $"Euler round-trip drift at ({ax},{ay},{az}) deg: {err} rad");
            }
        }

        [Test]
        public void MirrorZ_IsInvolution()
        {
            var q = new Quat(0.1, -0.4, 0.2, 0.7).Normalized();
            var twice = WebCoord.MirrorZ(WebCoord.MirrorZ(q));
            Assert.Less(Quat.AngleBetween(q, twice), AngleEps);
            Assert.AreEqual(q.X, twice.X, Eps);
            Assert.AreEqual(q.Y, twice.Y, Eps);
            Assert.AreEqual(q.Z, twice.Z, Eps);
            Assert.AreEqual(q.W, twice.W, Eps);
        }

        // ── Known-value: web yaw +90° about +Y  →  Unity yaw -90° about +Y ────────
        // Handedness flip (z→-z) reverses the sign of rotations about the vertical
        // axis. See WebCoord XML docs for the full derivation.
        [Test]
        public void KnownValue_WebYawPlus90_To_UnityYawMinus90()
        {
            var webEuler = new Vec3(0, Math.PI / 2, 0); // +90° about Y (right-handed)
            Quat unityQ = WebCoord.RotationToUnity(webEuler);

            // Expected: -90° about +Y in Unity's left-handed frame.
            Quat expected = Quat.FromAxisAngle(new Vec3(0, 1, 0), -Math.PI / 2);
            Assert.Less(Quat.AngleBetween(unityQ, expected), AngleEps,
                $"Expected Unity yaw -90 about +Y, got {unityQ}");

            // And the intermediate mirrored quaternion has y-component negated.
            Quat webQ = WebCoord.EulerXYZToQuat(0, Math.PI / 2, 0);
            Assert.Greater(webQ.Y, 0, "web +90 yaw should have +qy");
            Assert.Less(unityQ.Y, 0, "unity -90 yaw should have -qy");
        }

        // ── Known-value: pitch about X keeps sign (X axis not flipped alone) ──────
        [Test]
        public void KnownValue_WebPitch_QuatSanity()
        {
            var webEuler = new Vec3(Math.PI / 2, 0, 0); // +90° about X
            Quat webQ = WebCoord.EulerXYZToQuat(webEuler.X, webEuler.Y, webEuler.Z);
            Quat unityQ = WebCoord.RotationToUnity(webEuler);
            // MirrorZ negates qx: X pitch sign flips under z-mirror as derived.
            Assert.AreEqual(-webQ.X, unityQ.X, Eps);
            Assert.AreEqual(webQ.W, unityQ.W, Eps);
        }

        // ── three.js reference value: EulerXYZ(0, π/2, 0) == (0, √2/2, 0, √2/2) ───
        [Test]
        public void EulerXYZToQuat_MatchesThreeJsReference()
        {
            Quat q = WebCoord.EulerXYZToQuat(0, Math.PI / 2, 0);
            double s = Math.Sqrt(2) / 2;
            Assert.AreEqual(0, q.X, Eps);
            Assert.AreEqual(s, q.Y, Eps);
            Assert.AreEqual(0, q.Z, Eps);
            Assert.AreEqual(s, q.W, Eps);
        }

        [Test]
        public void QuatToEulerXYZ_InvertsEulerXYZToQuat()
        {
            double d2r = Math.PI / 180.0;
            var e = new Vec3(23 * d2r, -47 * d2r, 61 * d2r);
            Quat q = WebCoord.EulerXYZToQuat(e.X, e.Y, e.Z);
            Vec3 back = WebCoord.QuatToEulerXYZ(q);
            Quat qBack = WebCoord.EulerXYZToQuat(back.X, back.Y, back.Z);
            Assert.Less(Quat.AngleBetween(q, qBack), AngleEps);
        }
    }
}
