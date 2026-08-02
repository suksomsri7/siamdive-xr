using System;
using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// "เรือเอียงแล้วตัน" — the wreck on its side, with its own numbers.
    ///
    /// This is not a shape invented to fail. It is ONE object out of T-13: <c>cc0:wreck_hardeep</c>,
    /// item <c>m51i8bocucw7s</c>, placed at euler (−1.663, −0.139, 0.984) and scale 32.27 — a
    /// 99.5° roll, i.e. a ship lying on its side on the sand — with the 96-box hull the CDN
    /// actually publishes for it, verbatim. The same hull standing upright on the Harddeep map
    /// measured openCore 0.741; here it measured 0.005, and the difference was not the file.
    ///
    /// It was the last step of the old <c>SolidBoxes.ToWorld</c>: rotate each box, then keep the
    /// axis-aligned box AROUND the rotated one. <see cref="FlatteningIntoWorldAxesInflatesTheWreck"/>
    /// puts a number on what that costs (×5.5 for this placement — the survey found ×4.03 to ×7.49
    /// across T-13), and <see cref="TheInteriorIsOpenAgain"/> is the bug as the user meets it: a
    /// point with ten units of clear water around it, that the app called solid steel.
    ///
    /// ⚠️ One thing this test cannot reproduce without an Editor: the app fits the hull to the
    /// object's MEASURED local bounds, and here it is fitted to the bounds the file declares. The
    /// two agree to within <see cref="SolidBoxes.FitsBbox"/>'s tolerance by construction — that
    /// check is what refuses them when they do not — so the shape is right even if the last
    /// fraction of a unit is not.
    /// </summary>
    public class TiltedHullTests
    {
        // ── the placement, copied out of T-13's scene JSON ────────────────────────────
        private static readonly Vec3 WebEuler = new Vec3(-1.663, -0.139, 0.984);
        private static readonly Vec3 WebPos = new Vec3(62.93, 0.0, -383.37);
        private const double PlacedScale = 32.27;

        // T-13's own environment: waterLevel 101, footprint areaScale 1.85 × areaScaleX 1.05 / 1.
        // Without the real footprint the map boundary hauls anything at z ≈ 393 back toward the
        // middle of a 340 u disc, and the test would be measuring the wrong clamp.
        private const float Water = 101f;
        private const float MapScaleX = 1.9425f;
        private const float MapScaleZ = 1.85f;

        /// <summary>
        /// The sand, put below the wreck so the floor clamp cannot answer for it. This wreck is
        /// half buried where T-13 places it (its pivot is at y = 0 and the roll swings half the
        /// hull under that), and the real height there comes from the sculpted seabed mesh, which
        /// no EditMode test can rebuild. Both the old shape and the new one see the same sand, so
        /// the comparison the file is about is unaffected.
        /// </summary>
        private const float SeabedY = -20f;

        // A pocket of open water INSIDE the wreck: 7.1 units clear of every box in the hull, and
        // at least 5.4 units inside the wreck's own world bounds on every side. The old flattened
        // hull called it solid.
        private static readonly Vec3 Hole = new Vec3(46.80, -1.72, 393.38);

        // The middle of the largest plate in the hull. Steel, before and after.
        private static readonly Vec3 Steel = new Vec3(58.56, 5.01, 386.24);

        private static Quat Rot() => WebCoord.RotationToUnity(WebEuler);
        private static Vec3 Origin() => WebCoord.PositionToUnity(WebPos);

        private static SolidBoxes.Model Hull()
        {
            SolidBoxes.Model m = SolidBoxes.Parse(HardeepHull);
            Assert.IsNotNull(m, "the fixture is the published file — if it stops parsing, read why");
            return m;
        }

        /// <summary>
        /// The hull in the object's own frame, the way <c>SceneBuilder.TryHull</c> builds it:
        /// fitted to the content's local box (SceneBuilder's GroundToBase leaves the content
        /// centred on XZ with its base at y = 0) and multiplied by the placement's scale.
        /// </summary>
        private static SolidBoxes.Box[] Frame()
        {
            SolidBoxes.Model m = Hull();
            double w = m.BboxMax.X - m.BboxMin.X;
            double h = m.BboxMax.Y - m.BboxMin.Y;
            double d = m.BboxMax.Z - m.BboxMin.Z;
            SolidBoxes.Box fit = SolidBoxes.Box.FromMinMax(-w / 2, 0, -d / 2, w / 2, h, d / 2);
            Assert.IsTrue(SolidBoxes.FitsBbox(m, fit), "the hull must be recognised as this model's");
            return SolidBoxes.ToFrame(m, fit, new Vec3(PlacedScale, PlacedScale, PlacedScale));
        }

        /// <summary>
        /// 🔴 What the app used to hand the drone, kept HERE and nowhere else: every hull box
        /// rotated into world space and then flattened back to an axis-aligned box. It exists so
        /// the tests can say what changed; putting it back in the shipping code puts the bug back.
        /// </summary>
        private static SolidBoxes.Box[] FlattenedIntoWorldAxes(SolidBoxes.Box[] frame,
                                                               Vec3 origin, Quat rot)
        {
            var outBoxes = new SolidBoxes.Box[frame.Length];
            for (int i = 0; i < frame.Length; i++)
            {
                double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
                for (int c = 0; c < 8; c++)
                {
                    var corner = new Vec3((c & 1) == 0 ? frame[i].Min.X : frame[i].Max.X,
                                          (c & 2) == 0 ? frame[i].Min.Y : frame[i].Max.Y,
                                          (c & 4) == 0 ? frame[i].Min.Z : frame[i].Max.Z);
                    Vec3 w = SolidBoxes.FrameToWorld(origin, rot, corner);
                    if (w.X < minX) minX = w.X; if (w.X > maxX) maxX = w.X;
                    if (w.Y < minY) minY = w.Y; if (w.Y > maxY) maxY = w.Y;
                    if (w.Z < minZ) minZ = w.Z; if (w.Z > maxZ) maxZ = w.Z;
                }
                outBoxes[i] = SolidBoxes.Box.FromMinMax(minX, minY, minZ, maxX, maxY, maxZ);
            }
            return outBoxes;
        }

        private static double Volume(SolidBoxes.Box[] boxes)
        {
            double v = 0;
            for (int i = 0; i < boxes.Length; i++)
                v += (boxes[i].Max.X - boxes[i].Min.X) * (boxes[i].Max.Y - boxes[i].Min.Y)
                   * (boxes[i].Max.Z - boxes[i].Min.Z);
            return v;
        }

        /// <summary>
        /// The drone's own test, in whichever space the boxes are in: box grown by the camera
        /// radius on all six sides, point inside it. <paramref name="shrink"/> exists for the one
        /// question that has to be asked about a point the drone just PUT on a face — coming back
        /// through single precision it lands a millionth of a unit inside the box it was pushed
        /// out of, and "did the push work" must not turn on that.
        /// </summary>
        private static bool Blocks(SolidBoxes.Box[] boxes, Vec3 p, double shrink = 0.0)
        {
            double r = DroneFlight.CamRadius - shrink;
            for (int i = 0; i < boxes.Length; i++)
            {
                SolidBoxes.Box b = boxes[i];
                if (p.X > b.Min.X - r && p.X < b.Max.X + r &&
                    p.Y > b.Min.Y - r && p.Y < b.Max.Y + r &&
                    p.Z > b.Min.Z - r && p.Z < b.Max.Z + r) return true;
            }
            return false;
        }

        /// <summary>The wreck as the drone carries it: hull in its frame, bounds in world.</summary>
        private static DroneFlight.Solid Wreck()
        {
            SolidBoxes.Box[] frame = Frame();
            Quat rot = Rot();
            Vec3 origin = Origin();
            SolidBoxes.Box[] world = FlattenedIntoWorldAxes(frame, origin, rot);

            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            for (int i = 0; i < world.Length; i++)
            {
                if (world[i].Min.X < minX) minX = world[i].Min.X;
                if (world[i].Min.Y < minY) minY = world[i].Min.Y;
                if (world[i].Min.Z < minZ) minZ = world[i].Min.Z;
                if (world[i].Max.X > maxX) maxX = world[i].Max.X;
                if (world[i].Max.Y > maxY) maxY = world[i].Max.Y;
                if (world[i].Max.Z > maxZ) maxZ = world[i].Max.Z;
            }

            var boxes = new DroneFlight.Box[frame.Length];
            for (int i = 0; i < frame.Length; i++)
                boxes[i] = new DroneFlight.Box
                {
                    MinX = (float)frame[i].Min.X, MinY = (float)frame[i].Min.Y,
                    MinZ = (float)frame[i].Min.Z, MaxX = (float)frame[i].Max.X,
                    MaxY = (float)frame[i].Max.Y, MaxZ = (float)frame[i].Max.Z,
                };

            return new DroneFlight.Solid
            {
                Bound = new DroneFlight.Box
                {
                    MinX = (float)minX, MinY = (float)minY, MinZ = (float)minZ,
                    MaxX = (float)maxX, MaxY = (float)maxY, MaxZ = (float)maxZ,
                },
                Boxes = boxes,
                Origin = new DroneFlight.Vec3((float)origin.X, (float)origin.Y, (float)origin.Z),
                Rot = new DroneFlight.Quat((float)rot.X, (float)rot.Y, (float)rot.Z, (float)rot.W),
                Rotated = true,
            };
        }

        // ── the fixture is the placement we think it is ───────────────────────────────

        [Test]
        public void TheWreckIsReallyLyingOnItsSide()
        {
            // Guards the fixture: if WebCoord's handedness ever changes, this test says so instead
            // of the ones below quietly passing on an upright boat.
            Vec3 up = SolidBoxes.Rotate(Rot(), new Vec3(0, 1, 0));
            double tilt = Math.Acos(Math.Max(-1, Math.Min(1, up.Y))) * 180.0 / Math.PI;
            Assert.AreEqual(99.5, tilt, 0.2, "T-13 lays this wreck over on its side");
        }

        [Test]
        public void TheHullIsTheOneTheCdnPublishes()
        {
            SolidBoxes.Model m = Hull();
            Assert.AreEqual(96, m.Boxes.Length);
            Assert.LessOrEqual(m.Boxes.Length, SolidBoxes.MaxBoxesPerModel,
                               "this real file sits exactly ON the cap — lowering it drops the wreck");
        }

        // ── why the boxes stopped being sent to world space ───────────────────────────

        [Test]
        public void FlatteningIntoWorldAxesInflatesTheWreck()
        {
            SolidBoxes.Box[] frame = Frame();
            SolidBoxes.Box[] flat = FlattenedIntoWorldAxes(frame, Origin(), Rot());

            double ratio = Volume(flat) / Volume(frame);
            Assert.Greater(ratio, 4.0,
                           "the old 'slightly fat' was ×" + ratio.ToString("F2") + " for this placement");
            // The volume is the mechanism; a hole that no longer exists is the symptom.
            Assert.IsTrue(Blocks(flat, Hole), "the old shape swallowed the wreck's interior");
        }

        [Test]
        public void AtTiltZeroNothingChangesAtAll()
        {
            // The safety argument for the whole rewrite: an unrotated placement is bit-for-bit the
            // arithmetic that shipped. Everything on a flat reef is this case.
            SolidBoxes.Box[] frame = Frame();
            SolidBoxes.Box[] flat = FlattenedIntoWorldAxes(frame, new Vec3(0, 0, 0), Quat.Identity);
            Assert.AreEqual(frame.Length, flat.Length);
            for (int i = 0; i < frame.Length; i++)
            {
                Assert.AreEqual(frame[i].Min.X, flat[i].Min.X, 1e-9);
                Assert.AreEqual(frame[i].Min.Y, flat[i].Min.Y, 1e-9);
                Assert.AreEqual(frame[i].Min.Z, flat[i].Min.Z, 1e-9);
                Assert.AreEqual(frame[i].Max.X, flat[i].Max.X, 1e-9);
                Assert.AreEqual(frame[i].Max.Y, flat[i].Max.Y, 1e-9);
                Assert.AreEqual(frame[i].Max.Z, flat[i].Max.Z, 1e-9);
            }
        }

        // ── the bug, and the part of it that must NOT change ──────────────────────────

        [Test]
        public void TheInteriorIsOpenAgain()
        {
            SolidBoxes.Box[] frame = Frame();
            Vec3 inFrame = SolidBoxes.WorldToFrame(Origin(), Rot(), Hole);

            Assert.IsFalse(Blocks(frame, inFrame), "the hole the diver can see is water again");
            Assert.IsTrue(Blocks(FlattenedIntoWorldAxes(frame, Origin(), Rot()), Hole),
                          "…and it is the SAME point the old code called steel");
        }

        [Test]
        public void TheSteelIsStillSolid()
        {
            // The other half of the invariant. A fix that opens the hull is only a fix if the
            // plating still stops you.
            SolidBoxes.Box[] frame = Frame();
            Vec3 inFrame = SolidBoxes.WorldToFrame(Origin(), Rot(), Steel);
            Assert.IsTrue(Blocks(frame, inFrame));
        }

        // ── and now the same two points, through the drone ────────────────────────────

        [Test]
        public void TheDiverCanHoverInsideTheWreck()
        {
            DroneFlight.Solid wreck = Wreck();
            var s = new DroneFlight.State
            {
                Pos = new DroneFlight.Vec3((float)Hole.X, (float)Hole.Y, (float)Hole.Z),
            };
            for (int i = 0; i < 60; i++)
                s = DroneFlight.Step(s, new DroneFlight.Sticks(), 0.016f, SeabedY, Water,
                                     new[] { wreck }, MapScaleX, MapScaleZ);

            Assert.AreEqual((float)Hole.X, s.Pos.X, 0.01f, "the wreck shoved a hovering diver sideways");
            Assert.AreEqual((float)Hole.Y, s.Pos.Y, 0.01f, "…or lifted them out through the hull");
            Assert.AreEqual((float)Hole.Z, s.Pos.Z, 0.01f);
        }

        [Test]
        public void TheDiverIsStillPushedOutOfThePlating()
        {
            DroneFlight.Solid wreck = Wreck();
            var s = new DroneFlight.State
            {
                Pos = new DroneFlight.Vec3((float)Steel.X, (float)Steel.Y, (float)Steel.Z),
            };
            s = DroneFlight.Step(s, new DroneFlight.Sticks(), 0.016f, SeabedY, Water,
                                 new[] { wreck }, MapScaleX, MapScaleZ);

            double moved = Math.Sqrt((s.Pos.X - Steel.X) * (s.Pos.X - Steel.X)
                                   + (s.Pos.Y - Steel.Y) * (s.Pos.Y - Steel.Y)
                                   + (s.Pos.Z - Steel.Z) * (s.Pos.Z - Steel.Z));
            Assert.Greater(moved, 1.0, "a diver standing in the steel was left there");

            SolidBoxes.Box[] frame = Frame();
            Vec3 landed = SolidBoxes.WorldToFrame(Origin(), Rot(),
                                                  new Vec3(s.Pos.X, s.Pos.Y, s.Pos.Z));
            Assert.IsFalse(Blocks(frame, landed, 0.01),
                           "the diver came out of one plate and into the next");

            // …and stays there. One pass over 96 touching boxes could in principle ping-pong
            // between two of them forever, which on a phone is a camera shaking in your hands.
            DroneFlight.State rest = s;
            for (int i = 0; i < 30; i++)
                rest = DroneFlight.Step(rest, new DroneFlight.Sticks(), 0.016f, SeabedY, Water,
                                        new[] { wreck }, MapScaleX, MapScaleZ);
            Assert.AreEqual(s.Pos.X, rest.Pos.X, 1e-4, "the push-out is not settling");
            Assert.AreEqual(s.Pos.Y, rest.Pos.Y, 1e-4);
            Assert.AreEqual(s.Pos.Z, rest.Pos.Z, 1e-4);
        }

        [Test]
        public void TheDiverCanSwimBackOutOfTheWreck()
        {
            // The push-out only cancels the part of the velocity going INTO the wall, so being put
            // down against the plating must not be a trap: four seconds of forward stick and the
            // diver is in open water. (The old code's answer to the same situation was to lift them
            // to the top of the wreck, which is the bug, not the escape route.)
            DroneFlight.Solid wreck = Wreck();
            var s = new DroneFlight.State
            {
                Pos = new DroneFlight.Vec3((float)Steel.X, (float)Steel.Y, (float)Steel.Z),
            };
            for (int i = 0; i < 250; i++)
                s = DroneFlight.Step(s, new DroneFlight.Sticks { Ry = -1f }, 0.016f, SeabedY, Water,
                                     new[] { wreck }, MapScaleX, MapScaleZ);

            Vec3 landed = SolidBoxes.WorldToFrame(Origin(), Rot(),
                                                  new Vec3(s.Pos.X, s.Pos.Y, s.Pos.Z));
            Assert.IsFalse(Blocks(Frame(), landed), "the diver could not swim out of the plating");
        }

        [Test]
        public void ADiverInsideTheHullIsNotThrownOverTheWholeWreck()
        {
            // The other half of the report: with only five faces to choose from, ANYONE inside a
            // solid came out of the top of it. On a 55-unit wreck lying on its side that is a
            // vertical teleport, not a collision.
            DroneFlight.Solid wreck = Wreck();
            var s = new DroneFlight.State
            {
                Pos = new DroneFlight.Vec3((float)Steel.X, (float)Steel.Y, (float)Steel.Z),
            };
            s = DroneFlight.Step(s, new DroneFlight.Sticks(), 0.016f, SeabedY, Water,
                                 new[] { wreck }, MapScaleX, MapScaleZ);

            Assert.LessOrEqual(s.Pos.Y, wreck.Bound.MaxY + DroneFlight.CamRadius + 0.001f,
                               "the diver was lifted clear over the top of the wreck");
        }

        // ── the published hull, verbatim ──────────────────────────────────────────────
        // cc0_wreck_hardeep_xr0.solids.json — 96 boxes, normalised 0..1 inside the bbox.
        private const string HardeepHull =
            "{\"v\":1,\"grid\":[44,16,9],\"bbox\":{\"min\":[-0.956235,-0.358521,-0.442557],\"max\":[0.954353,0.355783,-0.037701]},\"boxes\":[" +
            "[0.11364,0,0,0.88636,0.375,0.11111]," +
            "[0.88636,0.1875,0,0.90909,0.25,0.55556]," +
            "[0.09091,0,0.11111,0.93182,0.125,0.33333]," +
            "[0.06818,0.0625,0.11111,0.11364,0.3125,0.77778]," +
            "[0.09091,0.125,0.11111,0.27273,0.375,0.55556]," +
            "[0.34091,0.125,0.11111,0.40909,0.375,0.55556]," +
            "[0.59091,0.125,0.11111,0.77273,0.375,0.55556]," +
            "[0.84091,0.125,0.11111,0.93182,0.1875,0.44444]," +
            "[0.27273,0.1875,0.11111,0.34091,0.375,0.55556]," +
            "[0.77273,0.1875,0.11111,0.88636,0.375,0.55556]," +
            "[0.90909,0.1875,0.11111,0.93182,0.3125,0.66667]," +
            "[0.40909,0.25,0.11111,0.59091,0.375,0.22222]," +
            "[0.86364,0.25,0.11111,0.90909,0.375,0.77778]," +
            "[0.04545,0,0.22222,0.09091,0.0625,0.66667]," +
            "[0.90909,0,0.22222,0.95455,0.3125,0.55556]," +
            "[0.04545,0,0.22222,0.77273,0.125,0.55556]," +
            "[0.31818,0.125,0.22222,0.34091,0.1875,0.77778]," +
            "[0.04545,0.1875,0.22222,0.06818,0.3125,0.55556]," +
            "[0.56818,0.1875,0.22222,0.59091,0.375,0.66667]," +
            "[0.40909,0.25,0.22222,0.54545,0.375,0.55556]," +
            "[0.06818,0.3125,0.22222,0.09091,0.375,0.66667]," +
            "[0.54545,0.3125,0.22222,0.56818,0.375,0.55556]," +
            "[0.84091,0.3125,0.22222,0.93182,0.375,0.66667]," +
            "[0.02273,0,0.33333,0.04545,0.125,0.66667]," +
            "[0.09091,0,0.33333,0.88636,0.125,0.44444]," +
            "[0.95455,0,0.33333,0.97727,0.125,0.66667]," +
            "[0,0.0625,0.33333,0.02273,0.125,0.66667]," +
            "[0.88636,0.0625,0.33333,0.93182,0.125,1]," +
            "[0.97727,0.0625,0.33333,1,0.125,0.66667]," +
            "[0.5,0.125,0.33333,0.54545,0.25,0.44444]," +
            "[0,0.1875,0.33333,0.04545,0.25,0.66667]," +
            "[0.95455,0.1875,0.33333,1,0.25,0.66667]," +
            "[0.02273,0.25,0.33333,0.06818,0.3125,0.66667]," +
            "[0.54545,0.25,0.33333,0.56818,0.3125,0.55556]," +
            "[0.95455,0.25,0.33333,0.97727,0.3125,0.66667]," +
            "[0.04545,0.3125,0.33333,0.06818,0.375,0.66667]," +
            "[0.93182,0.3125,0.33333,0.95455,0.375,0.66667]," +
            "[0.86364,0,0.44444,0.88636,0.1875,0.55556]," +
            "[0.97727,0,0.44444,1,0.0625,0.66667]," +
            "[0,0.125,0.44444,0.06818,0.1875,0.55556]," +
            "[0.40909,0.125,0.44444,0.45455,0.25,0.55556]," +
            "[0.47727,0.125,0.44444,0.52273,0.25,0.55556]," +
            "[0.56818,0.125,0.44444,0.59091,0.1875,0.88889]," +
            "[0.88636,0.125,0.44444,0.93182,0.1875,0.77778]," +
            "[0.95455,0.125,0.44444,1,0.1875,0.66667]," +
            "[0.45455,0.1875,0.44444,0.47727,0.25,0.55556]," +
            "[0.54545,0.1875,0.44444,0.56818,0.25,0.55556]," +
            "[0.97727,0.25,0.44444,1,0.3125,0.66667]," +
            "[0.29545,0.375,0.44444,0.31818,0.5,0.55556]," +
            "[0.81818,0.375,0.44444,0.84091,0.5,0.55556]," +
            "[0.27273,0.4375,0.44444,0.29545,1,0.55556]," +
            "[0.79545,0.4375,0.44444,0.81818,0.625,0.55556]," +
            "[0.29545,0.5625,0.44444,0.31818,0.6875,0.55556]," +
            "[0.77273,0.5625,0.44444,0.79545,0.8125,0.55556]," +
            "[0.75,0.75,0.44444,0.77273,1,0.55556]," +
            "[0.77273,0.9375,0.44444,0.79545,1,0.55556]," +
            "[0.09091,0,0.55556,0.27273,0.1875,0.66667]," +
            "[0.34091,0,0.55556,0.61364,0.0625,0.77778]," +
            "[0.65909,0,0.55556,0.68182,0.3125,0.66667]," +
            "[0.93182,0,0.55556,0.95455,0.1875,0.66667]," +
            "[0.31818,0.0625,0.55556,0.36364,0.125,1]," +
            "[0.40909,0.0625,0.55556,0.65909,0.125,0.77778]," +
            "[0.68182,0.0625,0.55556,0.75,0.25,0.66667]," +
            "[0.86364,0.0625,0.55556,0.88636,0.375,0.66667]," +
            "[0,0.125,0.55556,0.04545,0.1875,0.66667]," +
            "[0.31818,0.125,0.55556,0.38636,0.3125,0.66667]," +
            "[0.40909,0.125,0.55556,0.54545,0.1875,0.66667]," +
            "[0.59091,0.125,0.55556,0.68182,0.3125,0.88889]," +
            "[0.09091,0.1875,0.55556,0.25,0.3125,0.77778]," +
            "[0.29545,0.25,0.55556,0.31818,0.3125,0.66667]," +
            "[0.70455,0.25,0.55556,0.75,0.3125,0.66667]," +
            "[0.93182,0.25,0.55556,0.95455,0.3125,0.66667]," +
            "[0.22727,0.3125,0.55556,0.25,0.375,0.66667]," +
            "[0.59091,0.3125,0.55556,0.61364,0.375,0.66667]," +
            "[0.27273,0.9375,0.55556,0.29545,1,0.66667]," +
            "[0.13636,0,0.66667,0.18182,0.1875,0.77778]," +
            "[0.22727,0,0.66667,0.27273,0.1875,0.77778]," +
            "[0.72727,0,0.66667,0.75,0.1875,0.77778]," +
            "[0.18182,0.0625,0.66667,0.20455,0.1875,0.77778]," +
            "[0.65909,0.0625,0.66667,0.72727,0.1875,0.77778]," +
            "[0.93182,0.0625,0.66667,0.95455,0.1875,0.77778]," +
            "[0.20455,0.125,0.66667,0.22727,0.1875,0.77778]," +
            "[0.34091,0.125,0.66667,0.36364,0.1875,0.77778]," +
            "[0.43182,0.125,0.66667,0.54545,0.1875,0.77778]," +
            "[0.31818,0,0.77778,0.36364,0.0625,1]," +
            "[0.38636,0,0.77778,0.54545,0.0625,0.88889]," +
            "[0.56818,0,0.77778,0.70455,0.1875,0.88889]," +
            "[0.72727,0,0.77778,0.75,0.125,0.88889]," +
            "[0.88636,0,0.77778,0.93182,0.0625,1]," +
            "[0.43182,0.0625,0.77778,0.45455,0.1875,0.88889]," +
            "[0.70455,0.0625,0.77778,0.72727,0.125,0.88889]," +
            "[0.45455,0.125,0.77778,0.52273,0.1875,0.88889]," +
            "[0.29545,0,0.88889,0.31818,0.0625,1],[0.38636,0,0.88889,0.40909,0.0625,1]," +
            "[0.59091,0,0.88889,0.65909,0.125,1]," +
            "[0.61364,0.125,0.88889,0.63636,0.1875,1]" +
            "]}";
    }
}
