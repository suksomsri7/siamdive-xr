using System;
using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// P1.1 — the flight model is where a "it feels wrong" bug costs a CI round to see, so every
    /// rule the web encodes is pinned here: dead zone, turn direction, thrust direction, inertia,
    /// the solid push-out (including "never downward"), the sand floor, the surface ceiling and
    /// the map boundary.
    /// </summary>
    public class DroneFlightTests
    {
        private const float Dt = 0.016f;
        private const float Water = 240f;

        private static DroneFlight.State Fresh(float x = 0f, float y = 100f, float z = 0f, float yaw = 0f)
            => new DroneFlight.State { Pos = new DroneFlight.Vec3(x, y, z), Yaw = yaw };

        private static DroneFlight.State Step(DroneFlight.State s, DroneFlight.Sticks sticks,
                                              float seabedY = 0f, DroneFlight.Solid[] solids = null)
            => DroneFlight.Step(s, sticks, Dt, seabedY, Water, solids, 1f, 1f);

        [Test]
        public void RestingThumb_DoesNotCreep()
        {
            var s = Fresh();
            var sticks = new DroneFlight.Sticks { Lx = 0.11f, Ly = -0.11f, Rx = 0.05f, Ry = -0.119f };
            for (int i = 0; i < 60; i++) s = Step(s, sticks);
            Assert.AreEqual(0f, s.Yaw, 1e-6f, "sub-dead-zone input turned the drone");
            Assert.AreEqual(100f, s.Pos.Y, 1e-4f);
            Assert.AreEqual(0f, s.Pos.X, 1e-4f);
            Assert.AreEqual(0f, s.Pos.Z, 1e-4f);
        }

        [Test]
        public void DeadZoneIsTheWebs012()
        {
            Assert.AreEqual(0f, DroneFlight.ApplyDeadZone(0.119f), 1e-6f);
            Assert.AreEqual(0f, DroneFlight.ApplyDeadZone(-0.119f), 1e-6f);
            Assert.AreEqual(0f, DroneFlight.ApplyDeadZone(-0.1f), 1e-6f);
            Assert.AreEqual(0.5f, DroneFlight.ApplyDeadZone(0.5f), 1e-6f);
            Assert.AreEqual(-0.5f, DroneFlight.ApplyDeadZone(-0.5f), 1e-6f);
            Assert.AreEqual(0.12f, DroneFlight.ApplyDeadZone(0.12f), 1e-6f, "0.12 itself passes");
            Assert.AreEqual(1f, DroneFlight.ApplyDeadZone(1.4f), 1e-6f, "over-range is clamped");
            Assert.AreEqual(-1f, DroneFlight.ApplyDeadZone(-1.4f), 1e-6f);
        }

        /// <summary>
        /// 🔴 The stick is LINEAR past the dead zone — builder.html:3766 applies <c>dz()</c> and
        /// nothing else. An expo curve was added here once and it is the single biggest reason
        /// build 261 read as "ช้าไป": a thumb at half travel was getting 22 % of thrust.
        /// </summary>
        [Test]
        public void TheStick_IsLinear_NotExpo()
        {
            Assert.AreEqual(0.5f, DroneFlight.ApplyDeadZone(0.5f), 1e-6f);
            Assert.AreEqual(0.3f, DroneFlight.ApplyDeadZone(0.3f), 1e-6f);
            Assert.AreEqual(-0.75f, DroneFlight.ApplyDeadZone(-0.75f), 1e-6f);

            // And that linearity survives all the way to the world: half a stick, half the speed.
            var half = Fresh();
            var full = Fresh();
            for (int i = 0; i < 600; i++)
            {
                half = Step(half, new DroneFlight.Sticks { Ry = -0.5f });
                full = Step(full, new DroneFlight.Sticks { Ry = -1f });
            }
            Assert.AreEqual(full.Vel.Z * 0.5f, half.Vel.Z, 0.05f);
        }

        [Test]
        public void PushingTheLeftStickRight_TurnsRight()
        {
            var s = Step(Fresh(), new DroneFlight.Sticks { Lx = 1f });
            // Unity yaw grows clockwise seen from above = turning right.
            Assert.Greater(s.Yaw, 0f);
            // builder.html:3768 is unfiltered: full stick is the full rate on the FIRST frame.
            Assert.AreEqual(DroneFlight.YawRate, s.YawVel, 1e-6f);
            Assert.AreEqual(DroneFlight.YawRate * Dt, s.Yaw, 1e-6f);

            for (int i = 0; i < 200; i++) s = Step(s, new DroneFlight.Sticks { Lx = 1f });
            Assert.AreEqual(DroneFlight.YawRate, s.YawVel, 1e-6f);
        }

        /// <summary>
        /// The web's yaw has no mass at all (builder.html:3768 writes straight into d.yaw). A rate
        /// filter was added here once; it made aiming feel like steering a boat, so it is gone.
        /// </summary>
        [Test]
        public void ReleasingTheTurn_StopsTheTurn_LikeTheWeb()
        {
            var s = Fresh();
            for (int i = 0; i < 200; i++) s = Step(s, new DroneFlight.Sticks { Lx = 1f });
            float held = s.Yaw;

            s = Step(s, new DroneFlight.Sticks());
            Assert.AreEqual(0f, s.YawVel, 1e-6f, "the web's turn stops the frame you let go");
            Assert.AreEqual(held, s.Yaw, 1e-6f);
        }

        [Test]
        public void PushingTheRightStickUp_MovesForward_AlongTheYaw()
        {
            var s = Fresh();
            var sticks = new DroneFlight.Sticks { Ry = -1f };   // screen up = negative Y
            for (int i = 0; i < 120; i++) s = Step(s, sticks);
            Assert.Greater(s.Pos.Z, 1f, "yaw 0 must travel +Z in Unity");
            Assert.AreEqual(0f, s.Pos.X, 0.01f);

            // Facing +X (yaw 90°) the same push must travel +X.
            var t = Fresh(yaw: Mathf90());
            for (int i = 0; i < 120; i++) t = Step(t, sticks);
            Assert.Greater(t.Pos.X, 1f);
            Assert.AreEqual(0f, t.Pos.Z, 0.5f);
        }

        [Test]
        public void PushingTheLeftStickUp_Ascends_AtTheWebs072()
        {
            var up = Fresh();
            var down = Fresh(y: 400f);
            var fwd = Fresh();
            for (int i = 0; i < 400; i++)
            {
                up = Step(up, new DroneFlight.Sticks { Ly = -1f });
                down = Step(down, new DroneFlight.Sticks { Ly = 1f }, seabedY: -1000f);
                fwd = Step(fwd, new DroneFlight.Sticks { Ry = -1f });
            }
            Assert.Greater(up.Vel.Y, 0f);
            // builder.html:3771 — ty = lift·SP·0.72, ONE factor, so up and down are symmetric.
            Assert.AreEqual(DroneFlight.Speed * 0.72f, up.Vel.Y, 0.2f);
            Assert.AreEqual(-DroneFlight.Speed * 0.72f, down.Vel.Y, 0.2f);
            Assert.AreEqual(System.Math.Abs(down.Vel.Y), up.Vel.Y, 0.01f, "the web has no asymmetry");
            Assert.AreEqual(DroneFlight.Speed, fwd.Vel.Z, 0.2f);
            Assert.Less(up.Vel.Y, fwd.Vel.Z, "vertical is still 0.72 of horizontal");
        }

        [Test]
        public void Strafing_IsFullSpeed_LikeTheWeb()
        {
            var side = Fresh();
            var fwd = Fresh();
            for (int i = 0; i < 400; i++)
            {
                side = Step(side, new DroneFlight.Sticks { Rx = 1f });
                fwd = Step(fwd, new DroneFlight.Sticks { Ry = -1f });
            }
            // builder.html:3770-3771 — `strafe=rx` carries no ratio; it goes into tx/tz at SP.
            Assert.AreEqual(1f, DroneFlight.StrafeRatio, 1e-6f);
            Assert.AreEqual(DroneFlight.Speed, side.Vel.X, 0.2f);   // yaw 0 ⇒ right = +X
            Assert.AreEqual(fwd.Vel.Z, side.Vel.X, 0.05f);
        }

        [Test]
        public void Inertia_RampsUpInsteadOfSnapping()
        {
            var s = Step(Fresh(), new DroneFlight.Sticks { Ry = -1f });
            // One frame of 0.09 thrust lag on a full-throttle target, at the 60 Hz step the
            // constant is defined on.
            Assert.AreEqual(DroneFlight.Speed * DroneFlight.Inertia, s.Vel.Z, 1e-3f);
            Assert.Less(s.Vel.Z, DroneFlight.Speed * 0.2f, "the drone must feel heavy, not instant");
        }

        // ── 2026-08-04: "โดรนเคลื่อนที่ช้าไป" (build 261) — back to the web, exactly ──────────

        /// <summary>
        /// 🔴 THE GUARD FOR THIS WHOLE CHUNK. Every translation constant, against the web line it
        /// comes from. A previous round re-scaled all of them for metric realism (SP 30→9 and a
        /// 0.7 strafe / 0.35 / 0.5 lift split) and the result was reported as too slow to travel a
        /// map with. The user's own reference is the web, so the web's numbers are the contract.
        /// </summary>
        [Test]
        public void FlightConstants_MatchTheWebExactly()
        {
            Assert.AreEqual(0.12f, DroneFlight.DeadZone, 1e-6f, "builder.html:3766");
            Assert.AreEqual(1.1f, DroneFlight.YawRate, 1e-6f, "builder.html:3768");
            // 🔴 ตัวเดียวในไฟล์นี้ที่ไม่ใช่ค่าเว็บอีกต่อไป: user สั่งลด 9 ส.ค. ("โดรนเคลื่อนที่
            // ช้าลงอีกนิด") · ค่าเว็บคือ 30 ส่วนที่ใช้จริงคือ 24 = 80% · เลขอื่นทุกตัวในเทสนี้
            // ยังตรึงกับ builder.html เหมือนเดิม
            Assert.AreEqual(24f, DroneFlight.Speed, 1e-6f, "user 9 ส.ค. — เว็บ 3770 SP=30 × 0.8");
            Assert.AreEqual(1f, DroneFlight.StrafeRatio, 1e-6f, "builder.html:3770 — strafe=rx");
            Assert.AreEqual(0.72f, DroneFlight.AscendRatio, 1e-6f, "builder.html:3771");
            Assert.AreEqual(0.72f, DroneFlight.DescendRatio, 1e-6f, "builder.html:3771 — one factor");
            Assert.AreEqual(0.09f, DroneFlight.Inertia, 1e-6f, "builder.html:3772");
            Assert.AreEqual(12f, DroneFlight.LookAhead, 1e-6f, "builder.html:3809");
            Assert.AreEqual(0.14f, DroneFlight.PitchFromLift, 1e-6f, "builder.html:3809");
            Assert.AreEqual(3.2f, DroneFlight.CamRadius, 1e-6f, "builder.html:3775 — camR");
        }

        /// <summary>
        /// The same numbers in the unit the arguments are actually had in. Keeping this test means
        /// the next person to reach for "but a diver only swims at 0.5 m/s" sees the trade written
        /// down instead of re-litigating it: a map is a place you TRAVEL, and the pace the user
        /// tuned for that on the web is 5 m/s. The realism build is one tap away
        /// (<see cref="SettingsStore.SpeedCalm"/>), which is where a preference belongs.
        /// </summary>
        [Test]
        public void TopSpeed_IsTheWebs5MetresPerSecond()
        {
            Assert.AreEqual(6.0, ItemPicker.UnitsPerMetre, 1e-9,
                            "the whole conversion hangs off this — see builder.html U_PER_M");

            // 5.00 → 4.00 m/s (user 9 ส.ค. ลดความเร็วโดรน 20%) · อัตราส่วนขึ้น/ลง/สไลด์ยังเป็นของเว็บ
            Assert.AreEqual(4.0f, DroneFlight.MetresPerSecond(DroneFlight.Speed), 0.01f);
            Assert.AreEqual(4.0f, DroneFlight.MetresPerSecond(DroneFlight.Speed * DroneFlight.StrafeRatio), 0.01f);
            Assert.AreEqual(2.88f, DroneFlight.MetresPerSecond(DroneFlight.Speed * DroneFlight.AscendRatio), 0.01f);
            Assert.AreEqual(2.88f, DroneFlight.MetresPerSecond(DroneFlight.Speed * DroneFlight.DescendRatio), 0.01f);

            // And the preset that carries build 261's drone forward for anyone who preferred it.
            // 0.30 is SettingsStore.CalmSpeedScale, inlined: SettingsStore needs PlayerPrefs and so
            // cannot be compiled into tools/test.sh's harness.
            Assert.AreEqual(1.2f, DroneFlight.MetresPerSecond(DroneFlight.Speed * 0.30f), 0.01f);
        }

        /// <summary>
        /// The regression in a single number. Build 261 gave a thumb at half travel 1.98 u/s —
        /// expo (0.22) on top of a base cut to 9. The web gives 15. That 7.6× is the report.
        /// </summary>
        [Test]
        public void HalfAStick_IsHalfTheWebsSpeed_NotAEighth()
        {
            var s = Fresh();
            for (int i = 0; i < 600; i++) s = Step(s, new DroneFlight.Sticks { Ry = -0.5f });
            Assert.AreEqual(DroneFlight.Speed * 0.5f, s.Vel.Z, 0.2f, "half a stick, half of top speed");
            Assert.Greater(s.Vel.Z, 9f, "…and still faster than build 261 managed at FULL stick");
        }

        /// <summary>
        /// One inertia, thrust and coast alike (builder.html:3772). A separate, softer Drag was
        /// added here once; the web has no such term and the asymmetry is not free — it lengthens
        /// every stop, which reads as sluggishness rather than as water.
        /// </summary>
        [Test]
        public void ReleasingTheStick_CoastsOnTheWebsSingleInertia()
        {
            var s = Fresh();
            for (int i = 0; i < 400; i++) s = Step(s, new DroneFlight.Sticks { Ry = -1f });
            float cruise = s.Vel.Z;
            Assert.AreEqual(DroneFlight.Speed, cruise, 0.2f);

            // One frame of release is exactly one frame of the same 0.09 lerp toward zero.
            s = Step(s, new DroneFlight.Sticks());
            Assert.AreEqual(cruise * (1f - DroneFlight.Inertia), s.Vel.Z, 1e-3f);

            for (int i = 0; i < 600; i++) s = Step(s, new DroneFlight.Sticks());
            Assert.AreEqual(0f, s.Vel.Z, 0.05f, "it does settle");
        }

        /// <summary>
        /// The response constants are per-60 Hz-frame, but a phone is not a 60 Hz frame. The old
        /// code applied them once per frame whatever its length, so the same drone was twice as
        /// laggy at 30 fps — half of "คุมไม่อยู่" was the device, not the number.
        /// </summary>
        [Test]
        public void Response_IsTheSame_AtAnyFrameRate()
        {
            // 60 Hz is untouched: the web's rule, bit for bit.
            Assert.AreEqual(DroneFlight.Inertia, DroneFlight.FrameLerp(DroneFlight.Inertia, 0.016f), 1e-6f);

            // Half the frames, each twice as long ⇒ the same velocity after the same second.
            var fast = Fresh();
            var slow = Fresh();
            for (int i = 0; i < 60; i++)
                fast = DroneFlight.Step(fast, new DroneFlight.Sticks { Ry = -1f }, 0.016f, 0f, Water, null, 1f, 1f);
            for (int i = 0; i < 30; i++)
                slow = DroneFlight.Step(slow, new DroneFlight.Sticks { Ry = -1f }, 0.032f, 0f, Water, null, 1f, 1f);

            Assert.AreEqual(fast.Vel.Z, slow.Vel.Z, 0.05f, "the drone must not depend on the phone");
            Assert.AreEqual(fast.Pos.Z, slow.Pos.Z, 0.5f);
        }

        /// <summary>
        /// The settings preset (SettingsStore.SpeedScale) multiplies the three translation speeds
        /// and NOTHING else — a slower diver must still be able to turn round at the same rate.
        /// </summary>
        [Test]
        public void SpeedScale_MovesTheCeiling_ButNotTheTurnRate()
        {
            var slow = Fresh();
            var quick = Fresh();
            var turnSlow = Fresh();
            var turnQuick = Fresh();
            for (int i = 0; i < 400; i++)
            {
                slow = DroneFlight.Step(slow, new DroneFlight.Sticks { Ry = -1f }, Dt, 0f, Water, null, 1f, 1f, 0.30f);
                quick = DroneFlight.Step(quick, new DroneFlight.Sticks { Ry = -1f }, Dt, 0f, Water, null, 1f, 1f, 1.25f);
                turnSlow = DroneFlight.Step(turnSlow, new DroneFlight.Sticks { Lx = 1f }, Dt, 0f, Water, null, 1f, 1f, 0.30f);
                turnQuick = DroneFlight.Step(turnQuick, new DroneFlight.Sticks { Lx = 1f }, Dt, 0f, Water, null, 1f, 1f, 1.25f);
            }
            Assert.AreEqual(DroneFlight.Speed * 0.30f, slow.Vel.Z, 0.2f);   // SettingsStore.CalmSpeedScale
            Assert.AreEqual(DroneFlight.Speed * 1.25f, quick.Vel.Z, 0.2f);   // SettingsStore.FastSpeedScale
            Assert.AreEqual(turnSlow.Yaw, turnQuick.Yaw, 1e-4f, "aiming is not a speed setting");

            // A nonsense scale must never freeze the drone in the water.
            var safe = DroneFlight.Step(Fresh(), new DroneFlight.Sticks { Ry = -1f }, Dt, 0f, Water, null, 1f, 1f, 0f);
            Assert.Greater(safe.Vel.Z, 0f);
        }

        [Test]
        public void SandFloor_IsNeverPiercedAndKillsDownwardVelocity()
        {
            var s = Fresh(y: 20f);
            for (int i = 0; i < 400; i++) s = Step(s, new DroneFlight.Sticks { Ly = 1f }, seabedY: 10f);
            float floor = 10f + DroneFlight.CamRadius + DroneFlight.FloorClearance;
            Assert.AreEqual(floor, s.Pos.Y, 0.001f);
            Assert.GreaterOrEqual(s.Vel.Y, 0f);
        }

        [Test]
        public void Surface_IsNeverBroken()
        {
            var s = Fresh(y: 200f);
            // 1200 frames, not 600: the ascent is now 0.53 m/s, so 37 units of water takes
            // twelve simulated seconds rather than two.
            for (int i = 0; i < 1200; i++) s = Step(s, new DroneFlight.Sticks { Ly = -1f });
            Assert.AreEqual(Water - DroneFlight.CeilingClearance, s.Pos.Y, 0.001f);
            Assert.LessOrEqual(s.Vel.Y, 0f);
        }

        [Test]
        public void Solids_PushOutSideways_NotThroughTheWreck()
        {
            var box = new DroneFlight.Box { MinX = -20f, MaxX = 20f, MinY = 0f, MaxY = 60f, MinZ = -20f, MaxZ = 20f };
            var s = Fresh(x: 0f, y: 30f, z: -60f);
            for (int i = 0; i < 400; i++)
                s = Step(s, new DroneFlight.Sticks { Ry = -1f }, solids: new[] { DroneFlight.Solid.Aabb(box) });

            // Flew north into the box → stopped on its −Z face, camera radius clear of it.
            Assert.LessOrEqual(s.Pos.Z, box.MinZ - DroneFlight.CamRadius + 0.001f);
            Assert.GreaterOrEqual(s.Vel.Z, -0.001f, "velocity into the wall must be cancelled");
        }

        [Test]
        public void Solids_MayRestOnTop_ButNeverPushDown()
        {
            var box = new DroneFlight.Box { MinX = -20f, MaxX = 20f, MinY = 0f, MaxY = 60f, MinZ = -20f, MaxZ = 20f };
            // Just above the top face, sinking: the shallowest exit is upward.
            var s = new DroneFlight.State
            {
                Pos = new DroneFlight.Vec3(0f, 61f, 0f),
                Vel = new DroneFlight.Vec3(0f, -20f, 0f),
            };
            s = Step(s, new DroneFlight.Sticks { Ly = 1f }, solids: new[] { DroneFlight.Solid.Aabb(box) });
            Assert.GreaterOrEqual(s.Pos.Y, box.MaxY + DroneFlight.CamRadius - 0.001f);
            Assert.AreEqual(0f, s.Vel.Y, 0.001f);
        }

        [Test]
        public void Solids_ASolidRestingOnTheSandStillHasNoWayOutUnderneath()
        {
            // The sixth face exists now, so the "never through the seabed" rule has to be shown
            // holding rather than assumed from the fact that the face was missing. A box whose
            // underside is buried: the downward exit lands below the floor, so it is off the menu
            // however shallow it looks.
            var box = new DroneFlight.Box { MinX = -20f, MaxX = 20f, MinY = 0f, MaxY = 8f, MinZ = -20f, MaxZ = 20f };
            var s = new DroneFlight.State
            {
                // Deep inside, nearer the bottom face than any other — the old five faces would
                // have sent this diver up, and so must the six.
                Pos = new DroneFlight.Vec3(0f, 1f, 0f),
            };
            s = Step(s, new DroneFlight.Sticks(), seabedY: 0f,
                     solids: new[] { DroneFlight.Solid.Aabb(box) });

            Assert.GreaterOrEqual(s.Pos.Y, box.MaxY + DroneFlight.CamRadius - 0.001f,
                                  "the shallowest face was the underside, and it is buried in sand");
        }

        [Test]
        public void Solids_PushDownOutOfSomethingHangingInMidWater()
        {
            // 🔴 The other half of "ชนกำแพงล่องหน". With only five faces, ANY diver who ended up
            // inside a solid was lifted to the top of it — over a wreck, over a swim-through, over
            // whatever it was they were trying to pass under. The way out of the underside of an
            // arch, hovering 60 units above the sand, is DOWN.
            var box = new DroneFlight.Box { MinX = -40f, MaxX = 40f, MinY = 60f, MaxY = 100f, MinZ = -40f, MaxZ = 40f };
            var s = new DroneFlight.State
            {
                Pos = new DroneFlight.Vec3(0f, 62f, 0f),   // just inside the underside
            };
            s = Step(s, new DroneFlight.Sticks(), seabedY: 0f,
                     solids: new[] { DroneFlight.Solid.Aabb(box) });

            Assert.LessOrEqual(s.Pos.Y, box.MinY - DroneFlight.CamRadius + 0.001f,
                               "the shallowest way out was down and the diver went up instead");
            Assert.Greater(s.Pos.Y, 0f, "…but not through the seabed");
        }

        [Test]
        public void Solids_ARotatedHullIsTestedInItsOwnFrame()
        {
            // One bar, 4 units thick, turned 45° about Y. The old code stored it as the world box
            // AROUND it — 60 units wide instead of 4 — so a diver 20 units off the bar's own face
            // was "inside" it. In its own frame there is nothing there but water.
            var bar = new DroneFlight.Box { MinX = -40f, MaxX = 40f, MinY = -2f, MaxY = 2f, MinZ = -2f, MaxZ = 2f };
            float h = (float)Math.Sin(Math.PI / 8), c = (float)Math.Cos(Math.PI / 8);
            var solid = new DroneFlight.Solid
            {
                // The bounds the app carries: the object's whole world AABB, which for a bar at 45°
                // is a wide flat square. It is a reject test, not the shape.
                Bound = new DroneFlight.Box { MinX = -30f, MaxX = 30f, MinY = -2f, MaxY = 2f, MinZ = -30f, MaxZ = 30f },
                Boxes = new[] { bar },
                Origin = new DroneFlight.Vec3(0f, 0f, 0f),
                Rot = new DroneFlight.Quat(0f, h, 0f, c),
                Rotated = true,
            };

            // World (14.14, 0, 14.14) is 20 units off the bar's own side — open water — but it is
            // well inside the 60-unit world square, which is all the old code could see.
            var s = new DroneFlight.State { Pos = new DroneFlight.Vec3(14.142f, 0f, 14.142f) };
            DroneFlight.State after = Step(s, new DroneFlight.Sticks(), seabedY: -100f,
                                           solids: new[] { solid });
            Assert.AreEqual(14.142f, after.Pos.X, 0.01f, "pushed out of a bar that is not there");
            Assert.AreEqual(14.142f, after.Pos.Z, 0.01f);

            // …and the bar itself still stops you: world (20, 0, −20) is 28 units along it.
            var inside = new DroneFlight.State { Pos = new DroneFlight.Vec3(20f, 0f, -20f) };
            DroneFlight.State pushed = Step(inside, new DroneFlight.Sticks(), seabedY: -100f,
                                            solids: new[] { solid });
            float moved = (float)Math.Sqrt((pushed.Pos.X - 20f) * (pushed.Pos.X - 20f)
                                         + (pushed.Pos.Z + 20f) * (pushed.Pos.Z + 20f)
                                         + pushed.Pos.Y * pushed.Pos.Y);
            Assert.Greater(moved, 1f, "the bar let a diver stand inside it");
        }

        [Test]
        public void MapBoundary_KeepsTheDroneOverTheSand()
        {
            var s = Fresh(x: 300f, y: 100f, z: 0f);
            for (int i = 0; i < 600; i++)
                s = DroneFlight.Step(s, new DroneFlight.Sticks { Ry = -1f }, Dt, 0f, Water, null, 1f, 1f);
            float f = SeabedGeom.BoundaryFraction(s.Pos.X, s.Pos.Z);
            Assert.LessOrEqual(f, 1f, "the drone left the seabed footprint");
        }

        [Test]
        public void MapBoundary_RespectsNonUniformStretch()
        {
            // areaScaleX 0.9 / areaScaleZ 1.1 — the demo map. The bound must follow the stretch.
            var s = Fresh(x: 0f, y: 100f, z: 0f, yaw: 0f);
            for (int i = 0; i < 900; i++)
                s = DroneFlight.Step(s, new DroneFlight.Sticks { Ry = -1f }, Dt, 0f, Water, null, 0.9f, 1.1f);
            Assert.Less(s.Pos.Z, SeabedGeom.SandRadius * 1.1f + 1f);
            Assert.Greater(s.Pos.Z, 100f, "it should still have travelled a long way");
        }

        [Test]
        public void LookTarget_AimsAlongTheYaw_AndTiltsWithClimb()
        {
            var s = Fresh();
            DroneFlight.Vec3 flat = DroneFlight.LookTarget(s);
            Assert.AreEqual(DroneFlight.LookAhead, flat.Z, 1e-4f);
            Assert.AreEqual(100f, flat.Y, 1e-4f);

            s.Vel = new DroneFlight.Vec3(0f, 20f, 0f);
            DroneFlight.Vec3 climbing = DroneFlight.LookTarget(s);
            Assert.Greater(climbing.Y, flat.Y);
        }

        [Test]
        public void DepthMetres_MatchesTheWebsSixUnitsPerMetre()
        {
            Assert.AreEqual(40f, DroneFlight.DepthMetres(0f, Water), 0.01f);   // seabed of the demo map
            Assert.AreEqual(0f, DroneFlight.DepthMetres(Water, Water), 0.01f);
            Assert.AreEqual(0f, DroneFlight.DepthMetres(Water + 50f, Water), 0.01f, "never negative");
            Assert.AreEqual(100f, DroneFlight.DepthMetres(-1000f, Water), 0.01f, "clamped at 100");
        }

        // ── D9: random spawn (builder.html:3722) ─────────────────────────────────

        [Test]
        public void RandomSpawn_StaysInsideTheWebsAnnulus()
        {
            var c = new DroneFlight.Vec3(0f, 0f, 0f);
            const float R = 200f;
            for (int i = 0; i <= 20; i++)
            {
                float u = i / 20f;
                DroneFlight.Vec3 p = DroneFlight.RandomSpawn(c, R, 0f, 240f, u, u);
                float d = (float)System.Math.Sqrt(p.X * p.X + p.Z * p.Z);
                Assert.GreaterOrEqual(d, R * 0.2f - 0.01f, "never dead centre — that is inside the wreck");
                Assert.LessOrEqual(d, R * 0.8f + 0.01f, "never out at the empty rim either");
            }
        }

        [Test]
        public void RandomSpawn_NeverStartsYouBuriedInTheSand()
        {
            // A spawn at sand level opens the dive with the camera inside the seabed, which reads
            // as a broken map. The web's floor is seabedTop + 18.
            var c = new DroneFlight.Vec3(0f, 0f, 0f);
            DroneFlight.Vec3 p = DroneFlight.RandomSpawn(c, 200f, 100f, 40f, 0.3f, 0.5f);
            Assert.AreEqual(118f, p.Y, 1e-4f, "seabed 100 + 18 wins over waterLevel/2 = 20");

            DroneFlight.Vec3 q = DroneFlight.RandomSpawn(c, 200f, 0f, 240f, 0.3f, 0.5f);
            Assert.AreEqual(120f, q.Y, 1e-4f, "…and waterLevel/2 wins when the sand is far below");
        }

        [Test]
        public void RandomSpawn_IsDeterministicForTheSameDraw()
        {
            var c = new DroneFlight.Vec3(5f, 0f, -7f);
            DroneFlight.Vec3 a = DroneFlight.RandomSpawn(c, 120f, 10f, 200f, 0.42f, 0.61f);
            DroneFlight.Vec3 b = DroneFlight.RandomSpawn(c, 120f, 10f, 200f, 0.42f, 0.61f);
            Assert.AreEqual(a.X, b.X, 1e-6f);
            Assert.AreEqual(a.Z, b.Z, 1e-6f);
        }

        [Test]
        public void RandomSpawn_IsCentredOnTheMap_NotOnTheOrigin()
        {
            var c = new DroneFlight.Vec3(1000f, 0f, -500f);
            DroneFlight.Vec3 p = DroneFlight.RandomSpawn(c, 100f, 0f, 200f, 0f, 0f);
            float d = (float)System.Math.Sqrt((p.X - c.X) * (p.X - c.X) + (p.Z - c.Z) * (p.Z - c.Z));
            Assert.AreEqual(20f, d, 0.01f);
        }

        [Test]
        public void RandomSpawn_ToleratesADrawOutsideZeroToOne()
        {
            var c = new DroneFlight.Vec3(0f, 0f, 0f);
            DroneFlight.Vec3 p = DroneFlight.RandomSpawn(c, 100f, 0f, 200f, 5f, -3f);
            float d = (float)System.Math.Sqrt(p.X * p.X + p.Z * p.Z);
            Assert.GreaterOrEqual(d, 20f - 0.01f);
            Assert.LessOrEqual(d, 80f + 0.01f);
        }

        [Test]
        public void YawToward_PointsAtTheMiddle()
        {
            var from = new DroneFlight.Vec3(10f, 0f, 0f);
            var mid = new DroneFlight.Vec3(0f, 0f, 0f);
            float yaw = DroneFlight.YawToward(from, mid);
            Assert.AreEqual((float)System.Math.PI, System.Math.Abs(yaw), 1e-4f, "due −X");
        }

        private static float Mathf90() => (float)(System.Math.PI / 2.0);
    }
}
