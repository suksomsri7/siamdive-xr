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
                                              float seabedY = 0f, DroneFlight.Box[] solids = null)
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
        }

        [Test]
        public void PushingTheLeftStickRight_TurnsRight()
        {
            var s = Step(Fresh(), new DroneFlight.Sticks { Lx = 1f });
            // Unity yaw grows clockwise seen from above = turning right.
            Assert.Greater(s.Yaw, 0f);
            // The turn has mass: one frame of the rate filter, not the full rate straight away.
            Assert.AreEqual(DroneFlight.YawRate * DroneFlight.YawResponse, s.YawVel, 1e-5f);
            Assert.AreEqual(s.YawVel * Dt, s.Yaw, 1e-6f);

            // …and it reaches the full rate if you hold it.
            for (int i = 0; i < 200; i++) s = Step(s, new DroneFlight.Sticks { Lx = 1f });
            Assert.AreEqual(DroneFlight.YawRate, s.YawVel, 1e-3f);
        }

        [Test]
        public void ReleasingTheTurn_CoastsInsteadOfStopping()
        {
            var s = Fresh();
            for (int i = 0; i < 200; i++) s = Step(s, new DroneFlight.Sticks { Lx = 1f });
            float held = s.Yaw;

            s = Step(s, new DroneFlight.Sticks());
            Assert.Greater(s.YawVel, 0f, "letting go must not nail the horizon in place");
            Assert.Less(s.YawVel, DroneFlight.YawRate);
            Assert.Greater(s.Yaw, held, "and the turn keeps carrying for a moment");

            for (int i = 0; i < 300; i++) s = Step(s, new DroneFlight.Sticks());
            Assert.AreEqual(0f, s.YawVel, 1e-3f, "…but it does settle");
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
        public void PushingTheLeftStickUp_Ascends_SlowerThanItSwimsForward()
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
            Assert.AreEqual(DroneFlight.Speed * DroneFlight.AscendRatio, up.Vel.Y, 0.2f);
            Assert.AreEqual(-DroneFlight.Speed * DroneFlight.DescendRatio, down.Vel.Y, 0.2f);
            Assert.AreEqual(DroneFlight.Speed, fwd.Vel.Z, 0.2f);

            // 🔴 The rule the whole re-scale exists for: a runaway ascent is the dangerous one.
            Assert.Less(up.Vel.Y, System.Math.Abs(down.Vel.Y), "up must be slower than down");
            Assert.Less(up.Vel.Y, fwd.Vel.Z, "…and slower than swimming forward");
        }

        [Test]
        public void Strafing_IsSlowerThanSwimmingForward()
        {
            var side = Fresh();
            var fwd = Fresh();
            for (int i = 0; i < 400; i++)
            {
                side = Step(side, new DroneFlight.Sticks { Rx = 1f });
                fwd = Step(fwd, new DroneFlight.Sticks { Ry = -1f });
            }
            // yaw 0 ⇒ right = +X.
            Assert.AreEqual(DroneFlight.Speed * DroneFlight.StrafeRatio, side.Vel.X, 0.2f);
            Assert.Less(side.Vel.X, fwd.Vel.Z);
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

        // ── the 2026-08 re-scale: "โดรนบินเร็วไป" ────────────────────────────────

        /// <summary>
        /// 🔴 THE POINT OF THE WHOLE CHANGE, in the only unit that can be argued about: metres.
        /// A real diver cruises at 0.3-0.5 m/s, sprints at ~1 and a DPV scooter tops out at
        /// ~1.5. The ported web model flew at 5.0 m/s and the report was "โดรนบินเร็วไป".
        /// If a future edit puts the drone back above scooter speed, it fails here rather than
        /// in someone's hands.
        /// </summary>
        [Test]
        public void TopSpeed_IsAScooter_NotAnAeroplane()
        {
            Assert.AreEqual(6.0, ItemPicker.UnitsPerMetre, 1e-9,
                            "the whole conversion hangs off this — see builder.html U_PER_M");

            float fwd = DroneFlight.MetresPerSecond(DroneFlight.Speed);
            Assert.AreEqual(1.5f, fwd, 0.01f, "forward = a DPV at full throttle");
            Assert.LessOrEqual(fwd, 1.6f, "faster than a scooter is not a dive, it is a flight");
            Assert.GreaterOrEqual(fwd, 1.0f, "…and slower than a sprinting diver is a chore");

            Assert.AreEqual(1.05f, DroneFlight.MetresPerSecond(DroneFlight.Speed * DroneFlight.StrafeRatio), 0.01f);
            Assert.AreEqual(0.525f, DroneFlight.MetresPerSecond(DroneFlight.Speed * DroneFlight.AscendRatio), 0.01f);
            Assert.AreEqual(0.75f, DroneFlight.MetresPerSecond(DroneFlight.Speed * DroneFlight.DescendRatio), 0.01f);

            // 32 m/min of ascent. Generous against a real 9-18, but the same order of magnitude —
            // the old model climbed at 216 m/min.
            Assert.Less(DroneFlight.MetresPerSecond(DroneFlight.Speed * DroneFlight.AscendRatio) * 60f, 40f);
        }

        [Test]
        public void HalfAStick_IsADiverFinningGently()
        {
            var s = Fresh();
            for (int i = 0; i < 400; i++) s = Step(s, new DroneFlight.Sticks { Ry = -0.5f });
            float ms = DroneFlight.MetresPerSecond(s.Vel.Z);
            // Expo: half deflection is a look-at-the-coral pace, not half of a scooter.
            Assert.Greater(ms, 0.2f);
            Assert.Less(ms, 0.55f, "half a stick must land in a real diver's cruising band");
        }

        [Test]
        public void Shape_IsExpo_SoASmallPushIsASmallMove()
        {
            Assert.AreEqual(0f, DroneFlight.Shape(0.11f), 1e-6f, "dead zone still holds");
            Assert.AreEqual(0f, DroneFlight.Shape(-0.11f), 1e-6f);
            Assert.AreEqual(1f, DroneFlight.Shape(1f), 1e-6f, "full stick is still full speed");
            Assert.AreEqual(-1f, DroneFlight.Shape(-1f), 1e-6f);
            Assert.AreEqual(1f, DroneFlight.Shape(1.4f), 1e-6f, "over-range is clamped, not amplified");

            // Just past the dead zone the output is a whisper, not a step.
            Assert.Less(DroneFlight.Shape(0.13f), 0.05f);
            // Monotonic and always below the linear ramp in the middle.
            float prev = 0f;
            for (float v = 0.12f; v <= 1f; v += 0.02f)
            {
                float o = DroneFlight.Shape(v);
                Assert.GreaterOrEqual(o, prev - 1e-6f, "the curve must never go backwards");
                Assert.LessOrEqual(o, v + 1e-6f, "expo is never MORE than linear");
                prev = o;
            }
            Assert.AreEqual(-DroneFlight.Shape(0.6f), DroneFlight.Shape(-0.6f), 1e-6f, "symmetric");
        }

        [Test]
        public void ReleasingTheStick_GlidesToAStop_InsteadOfBraking()
        {
            Assert.Less(DroneFlight.Drag, DroneFlight.Inertia,
                        "water drags; it does not brake — coasting must be the softer response");

            var s = Fresh();
            for (int i = 0; i < 400; i++) s = Step(s, new DroneFlight.Sticks { Ry = -1f });
            float cruise = s.Vel.Z;
            float z0 = s.Pos.Z;

            s = Step(s, new DroneFlight.Sticks());
            Assert.Greater(s.Vel.Z, cruise * 0.9f, "one frame after release it has barely slowed");

            // It does stop, and the glide is about half a metre — enough to feel like water,
            // little enough to park in front of a nudibranch.
            for (int i = 0; i < 600; i++) s = Step(s, new DroneFlight.Sticks());
            Assert.AreEqual(0f, s.Vel.Z, 0.05f);
            float glideMetres = (s.Pos.Z - z0) / (float)ItemPicker.UnitsPerMetre;
            Assert.Greater(glideMetres, 0.2f);
            Assert.Less(glideMetres, 1.2f, "a coast this long would make aiming impossible");
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
                slow = DroneFlight.Step(slow, new DroneFlight.Sticks { Ry = -1f }, Dt, 0f, Water, null, 1f, 1f, 0.65f);
                quick = DroneFlight.Step(quick, new DroneFlight.Sticks { Ry = -1f }, Dt, 0f, Water, null, 1f, 1f, 1.45f);
                turnSlow = DroneFlight.Step(turnSlow, new DroneFlight.Sticks { Lx = 1f }, Dt, 0f, Water, null, 1f, 1f, 0.65f);
                turnQuick = DroneFlight.Step(turnQuick, new DroneFlight.Sticks { Lx = 1f }, Dt, 0f, Water, null, 1f, 1f, 1.45f);
            }
            Assert.AreEqual(DroneFlight.Speed * 0.65f, slow.Vel.Z, 0.2f);
            Assert.AreEqual(DroneFlight.Speed * 1.45f, quick.Vel.Z, 0.2f);
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
                s = Step(s, new DroneFlight.Sticks { Ry = -1f }, solids: new[] { box });

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
            s = Step(s, new DroneFlight.Sticks { Ly = 1f }, solids: new[] { box });
            Assert.GreaterOrEqual(s.Pos.Y, box.MaxY + DroneFlight.CamRadius - 0.001f);
            Assert.AreEqual(0f, s.Vel.Y, 0.001f);
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
