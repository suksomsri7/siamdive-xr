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

        // ─────────────────────────────────────────────────────────────────────────
        //  Placing a model: what the PLAYER sees, not what the trigonometry says.
        //
        //  The user's build-261 report was "หน้า-หลังสลับ และตำแหน่งการวางไม่ตรง" — every
        //  model's front was its back, and big models sat well away from where the web
        //  draws them. Cause: glTFast mirrors imported meshes on X while this app places
        //  them on the web's Z mirror, and the two differ by a half turn. See the
        //  derivation above WebCoord.ImportedAxisFix.
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>A model authored facing its own +Z: where does its nose point in Unity?</summary>
        private static Vec3 Facing(double webYawRad, bool axisFix)
        {
            var pos = new Vec3(0, 0, 0);
            var rot = new Vec3(0, webYawRad, 0);
            var one = new Vec3(1, 1, 1);
            Vec3 nose = WebCoord.UnityWorldPoint(pos, rot, one, new Vec3(0, 0, 1), axisFix);
            Vec3 tail = WebCoord.UnityWorldPoint(pos, rot, one, new Vec3(0, 0, -1), axisFix);
            return new Vec3(nose.X - tail.X, nose.Y - tail.Y, nose.Z - tail.Z);
        }

        private static void AssertPointsAt(Vec3 got, double x, double y, double z, string what)
        {
            double n = Math.Sqrt(got.X * got.X + got.Y * got.Y + got.Z * got.Z);
            Assert.Greater(n, 1e-9, "degenerate facing");
            Assert.AreEqual(x, got.X / n, 1e-6, what + " (X)");
            Assert.AreEqual(y, got.Y / n, 1e-6, what + " (Y)");
            Assert.AreEqual(z, got.Z / n, 1e-6, what + " (Z)");
        }

        /// <summary>
        /// The compass test. A statue that faces due "web north" (+Z on the builder's floor)
        /// must face Unity −Z, because the web's +Z IS Unity's −Z — and turning it a quarter
        /// turn on the web must turn it a quarter turn the same way in the app.
        /// </summary>
        [Test]
        public void PlacedModel_FacesTheDirectionTheWebDrewIt()
        {
            double d2r = Math.PI / 180.0;
            AssertPointsAt(Facing(0, true), 0, 0, -1, "web yaw 0 must face Unity -Z");
            AssertPointsAt(Facing(90 * d2r, true), 1, 0, 0, "web yaw 90 must face Unity +X");
            AssertPointsAt(Facing(180 * d2r, true), 0, 0, 1, "web yaw 180 must face Unity +Z");
            AssertPointsAt(Facing(270 * d2r, true), -1, 0, 0, "web yaw 270 must face Unity -X");
        }

        /// <summary>The bug itself, pinned: without the fix every one of those is reversed.</summary>
        [Test]
        public void WithoutTheFix_EveryModelFacesBackwards()
        {
            double d2r = Math.PI / 180.0;
            foreach (var yaw in new[] { 0.0, 37.0, 90.0, 180.0, 251.0 })
            {
                Vec3 g = Facing(yaw * d2r, true);
                double gn = Math.Sqrt(g.X * g.X + g.Y * g.Y + g.Z * g.Z);
                Vec3 good = new Vec3(g.X / gn, g.Y / gn, g.Z / gn);
                Vec3 bad = Facing(yaw * d2r, false);
                AssertPointsAt(new Vec3(-bad.X, -bad.Y, -bad.Z), good.X, good.Y, good.Z,
                    $"build-261 facing at web yaw {yaw} should be the exact reverse");
            }
        }

        /// <summary>
        /// The whole model, not just its nose: every point of it lands where the web puts it
        /// (after the map's own z→−z), for real items off the Atlantis map — the domed temple
        /// at scale 402, the byzantine arch turned 1.524 rad, Poseidon flipped on X and Z.
        /// </summary>
        [Test]
        public void PlacedModel_MatchesTheWeb_PointForPoint()
        {
            var items = new[]
            {
                // pos,                              euler XYZ (rad),                 uniform scale
                (new Vec3(-47.71, 0, 61.99),         new Vec3(0, 0, 0),               402.05),  // ruin:domed_temple
                (new Vec3(1118.25, 0, 46.18),        new Vec3(0, 1.524, 0),           210.44),  // ruin:byzantine_arch
                (new Vec3(-557.32, 0, -731.19),      new Vec3(3.142, -0.048, 3.142),   91.06),  // cc0:poseidon
                (new Vec3(545.42, 0, -683.46),       new Vec3(-3.142, 0.393, -3.142),  97.32),  // stat:stormbringer
                (new Vec3(-1108.09, -11.1, -1164.61),new Vec3(0, 0, 0),               192.55),  // ruin:ornate_monument
                (new Vec3(-27.61, -2.69, -1140.47),  new Vec3(0, 0, 0),               260.71),  // ruin:long_arch
            };

            // Eight corners of the model's own unit box — a whole body, not a symmetric point.
            var corners = new[]
            {
                new Vec3(-0.5, 0, -0.5), new Vec3(0.5, 0, -0.5), new Vec3(-0.5, 0, 0.5), new Vec3(0.5, 0, 0.5),
                new Vec3(-0.5, 1, -0.5), new Vec3(0.5, 1, -0.5), new Vec3(-0.5, 1, 0.5), new Vec3(0.5, 1, 0.5),
            };

            foreach (var (pos, rot, s) in items)
            {
                var scale = new Vec3(s, s, s);
                foreach (var c in corners)
                {
                    Vec3 web = WebCoord.WebWorldPoint(pos, rot, scale, c);
                    Vec3 expect = WebCoord.PositionToUnity(web);
                    Vec3 got = WebCoord.UnityWorldPoint(pos, rot, scale, c, axisFix: true);

                    // 1 mm on a 400-unit model.
                    Assert.AreEqual(expect.X, got.X, 1e-3, $"X at {c} of item {pos}");
                    Assert.AreEqual(expect.Y, got.Y, 1e-3, $"Y at {c} of item {pos}");
                    Assert.AreEqual(expect.Z, got.Z, 1e-3, $"Z at {c} of item {pos}");
                }
            }
        }

        /// <summary>
        /// The number the user could see: the arch's front face is 420.9 units — two thirds of
        /// its own width — from where the web draws it, until the fix is applied.
        /// </summary>
        [Test]
        public void WithoutTheFix_TheByzantineArchIsHundredsOfUnitsOut()
        {
            var pos = new Vec3(1118.25, 0, 46.18);
            var rot = new Vec3(0, 1.524, 0);
            var scale = new Vec3(210.44, 210.44, 210.44);
            var front = new Vec3(0, 0, 1);   // the doorway's facing edge, in model units

            Vec3 want = WebCoord.PositionToUnity(WebCoord.WebWorldPoint(pos, rot, scale, front));
            Vec3 was = WebCoord.UnityWorldPoint(pos, rot, scale, front, axisFix: false);
            Vec3 now = WebCoord.UnityWorldPoint(pos, rot, scale, front, axisFix: true);

            double miss = Math.Sqrt((want.X - was.X) * (want.X - was.X) +
                                    (want.Y - was.Y) * (want.Y - was.Y) +
                                    (want.Z - was.Z) * (want.Z - was.Z));
            Assert.AreEqual(2 * 210.44, miss, 0.5, "the half turn throws the front face by twice its reach");
            Assert.Less(Math.Abs(want.X - now.X) + Math.Abs(want.Z - now.Z), 1e-3, "and the fix lands it");
        }

        /// <summary>Ry(180°) ∘ (X-mirror) == (Z-mirror): the identity the fix rests on.</summary>
        [Test]
        public void ImportedAxisFix_TurnsTheXMirrorIntoTheZMirror()
        {
            foreach (var v in new[] { new Vec3(1, 0, 0), new Vec3(0, 1, 0), new Vec3(0, 0, 1),
                                      new Vec3(3.5, -2, 7.25) })
            {
                var xMirrored = new Vec3(-v.X, v.Y, v.Z);              // glTFast
                Vec3 fixedUp = WebCoord.FixImportedPoint(xMirrored);   // + the half turn
                Assert.AreEqual(v.X, fixedUp.X, Eps);
                Assert.AreEqual(v.Y, fixedUp.Y, Eps);
                Assert.AreEqual(-v.Z, fixedUp.Z, Eps);                 // == PositionToUnity's mirror

                // Same statement through the quaternion path.
                Vec3 byQuat = WebCoord.ImportedAxisFix.Rotate(xMirrored);
                Assert.AreEqual(fixedUp.X, byQuat.X, 1e-12);
                Assert.AreEqual(fixedUp.Y, byQuat.Y, 1e-12);
                Assert.AreEqual(fixedUp.Z, byQuat.Z, 1e-12);
            }
        }

        [Test]
        public void QuatMultiply_MatchesUnityOrder()
        {
            // Rotating (0,0,1) by "yaw 90 then pitch 90" must equal the product in that order.
            Quat yaw = Quat.FromAxisAngle(new Vec3(0, 1, 0), Math.PI / 2);
            Quat pitch = Quat.FromAxisAngle(new Vec3(1, 0, 0), Math.PI / 2);
            var v = new Vec3(0, 0, 1);

            Vec3 stepwise = pitch.Rotate(yaw.Rotate(v));
            Vec3 product = (pitch * yaw).Rotate(v);
            Assert.AreEqual(stepwise.X, product.X, 1e-12);
            Assert.AreEqual(stepwise.Y, product.Y, 1e-12);
            Assert.AreEqual(stepwise.Z, product.Z, 1e-12);
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
