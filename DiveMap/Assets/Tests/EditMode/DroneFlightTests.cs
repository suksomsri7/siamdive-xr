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
            Assert.AreEqual(DroneFlight.YawRate * Dt, s.Yaw, 1e-6f);
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
        public void PushingTheLeftStickUp_Ascends_AtSeventyTwoPercent()
        {
            var up = Fresh();
            var fwd = Fresh();
            for (int i = 0; i < 400; i++)
            {
                up = Step(up, new DroneFlight.Sticks { Ly = -1f });
                fwd = Step(fwd, new DroneFlight.Sticks { Ry = -1f });
            }
            Assert.Greater(up.Vel.Y, 0f);
            Assert.AreEqual(DroneFlight.Speed * DroneFlight.LiftRatio, up.Vel.Y, 0.5f);
            Assert.AreEqual(DroneFlight.Speed, fwd.Vel.Z, 0.5f);
        }

        [Test]
        public void Inertia_RampsUpInsteadOfSnapping()
        {
            var s = Step(Fresh(), new DroneFlight.Sticks { Ry = -1f });
            // One frame of 0.09 lag on a 30 u/s target.
            Assert.AreEqual(DroneFlight.Speed * DroneFlight.Inertia, s.Vel.Z, 1e-3f);
            Assert.Less(s.Vel.Z, DroneFlight.Speed * 0.2f, "the drone must feel heavy, not instant");
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
            for (int i = 0; i < 600; i++) s = Step(s, new DroneFlight.Sticks { Ly = -1f });
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

        private static float Mathf90() => (float)(System.Math.PI / 2.0);
    }
}
