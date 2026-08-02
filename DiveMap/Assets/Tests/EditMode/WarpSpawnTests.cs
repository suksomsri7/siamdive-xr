using System;
using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// 🌀 "Every dive starts at a warp gate; random when there are several."
    ///
    /// Everything here is a rule someone can break without the app looking broken until a player
    /// is already in the water: the draw stops being random, the spawn creeps inside the gate's
    /// trigger ring (the destination picker opens itself, forever), the height stops being clamped
    /// (the dive opens with the camera in the sand or above the surface), or a map with no gate
    /// stops behaving the way D9 always did.
    /// </summary>
    public class WarpSpawnTests
    {
        private const float Water = 240f;
        private static readonly DroneFlight.Vec3 Centre = new DroneFlight.Vec3(0f, 0f, 0f);

        private static DroneFlight.Vec3 V(float x, float y, float z) => new DroneFlight.Vec3(x, y, z);

        private static float Dist(DroneFlight.Vec3 a, DroneFlight.Vec3 b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static WarpSpawn.Result Place(DroneFlight.Vec3[] gates, int idx,
                                              float seabedY = 0f, float water = Water,
                                              DroneFlight.Solid[] solids = null)
            => WarpSpawn.Place(gates, idx, Centre, seabedY, water, solids, 1f, 1f);

        // ── the draw ─────────────────────────────────────────────────────────────

        [Test]
        public void OneGate_IsAlwaysTheOneYouGet()
        {
            Assert.AreEqual(0, WarpSpawn.PickIndex(1, 0f));
            Assert.AreEqual(0, WarpSpawn.PickIndex(1, 0.5f));
            Assert.AreEqual(0, WarpSpawn.PickIndex(1, 1f));
        }

        [Test]
        public void NoGate_SaysSo()
        {
            Assert.AreEqual(-1, WarpSpawn.PickIndex(0, 0.5f));
            Assert.AreEqual(-1, WarpSpawn.PickIndex(-3, 0.5f));
        }

        [Test]
        public void TheDrawSpreadsOverEveryGate()
        {
            var seen = new bool[4];
            for (int i = 0; i <= 100; i++) seen[WarpSpawn.PickIndex(4, i / 100f)] = true;
            for (int i = 0; i < seen.Length; i++)
                Assert.IsTrue(seen[i], $"gate {i} can never be drawn — the pick is not random");

            // …in equal quarters, and inside the array at both ends.
            Assert.AreEqual(0, WarpSpawn.PickIndex(4, 0f));
            Assert.AreEqual(0, WarpSpawn.PickIndex(4, 0.2499f));
            Assert.AreEqual(1, WarpSpawn.PickIndex(4, 0.25f));
            Assert.AreEqual(3, WarpSpawn.PickIndex(4, 0.9999f));
            Assert.AreEqual(3, WarpSpawn.PickIndex(4, 1f), "rnd01 == 1 must not run off the end");
            Assert.AreEqual(0, WarpSpawn.PickIndex(4, -0.5f), "a bad draw is clamped, not thrown");
            Assert.AreEqual(3, WarpSpawn.PickIndex(4, 4f));
        }

        [Test]
        public void TheSameDrawIsTheSameGate()
        {
            for (int i = 0; i < 50; i++)
                Assert.AreEqual(WarpSpawn.PickIndex(7, i / 50f), WarpSpawn.PickIndex(7, i / 50f));
        }

        // ── no gate = the old behaviour, untouched ───────────────────────────────

        [Test]
        public void AMapWithNoGates_LeavesTheCallerAlone()
        {
            Assert.IsFalse(Place(null, 0).AtWarp, "null gate list must fall back to D9");
            Assert.IsFalse(Place(new DroneFlight.Vec3[0], 0).AtWarp);
            Assert.IsFalse(Place(new[] { V(50f, 60f, 0f) }, 1).AtWarp, "index past the end");
            Assert.IsFalse(Place(new[] { V(50f, 60f, 0f) }, -1).AtWarp);
        }

        // ── beside the gate, not inside it ───────────────────────────────────────

        [Test]
        public void SpawnIsBesideTheGate_OutsideItsTriggerRing()
        {
            Assert.Greater(WarpSpawn.Clearance, WarpSpawn.RearmRadius,
                           "a spawn inside the re-arm ring opens the picker on the first frame");
            Assert.Greater(WarpSpawn.RearmRadius, WarpSpawn.TriggerRadius);

            var gates = new[] { V(100f, 60f, 0f) };
            WarpSpawn.Result r = Place(gates, 0);

            Assert.IsTrue(r.AtWarp);
            Assert.AreEqual(0, r.Index);
            Assert.AreEqual(1, r.Count);
            Assert.AreEqual(WarpSpawn.Clearance, Dist(r.Pos, gates[0]), 0.01f,
                            "the diver must land beside the portal, not on it");
            Assert.Greater(WarpSpawn.NearestGateDistance(gates, r.Pos), WarpSpawn.RearmRadius);
        }

        [Test]
        public void SpawnStepsTowardTheMiddle_SoTheGateIsBehindYou()
        {
            var gates = new[] { V(120f, 60f, -80f) };
            WarpSpawn.Result r = Place(gates, 0);

            // Same side of the gate as the map's content…
            float toCentreX = Centre.X - r.Pos.X, toCentreZ = Centre.Z - r.Pos.Z;
            float fromGateX = r.Pos.X - gates[0].X, fromGateZ = r.Pos.Z - gates[0].Z;
            Assert.Greater(toCentreX * fromGateX + toCentreZ * fromGateZ, 0f,
                           "walking out of the gate must be walking toward the content");
            // …and closer to it than the gate is.
            Assert.Less(r.Pos.X * r.Pos.X + r.Pos.Z * r.Pos.Z,
                        gates[0].X * gates[0].X + gates[0].Z * gates[0].Z);

            // Facing the middle of the map — the same rule D9 uses for a random spawn.
            Assert.AreEqual(DroneFlight.YawToward(r.Pos, Centre), r.Yaw, 1e-6f);
        }

        [Test]
        public void AGateOnTheExactCentre_StillGetsAPlace()
        {
            var gates = new[] { V(0f, 60f, 0f) };
            WarpSpawn.Result r = Place(gates, 0);

            Assert.IsTrue(r.AtWarp);
            Assert.AreEqual(WarpSpawn.Clearance, Dist(r.Pos, gates[0]), 0.01f,
                            "there is no 'toward the middle' here, but there is still a place to stand");
            Assert.IsFalse(float.IsNaN(r.Pos.X) || float.IsNaN(r.Pos.Z) || float.IsNaN(r.Yaw));
        }

        [Test]
        public void TheSameGateAlwaysGivesTheSamePlace()
        {
            var gates = new[] { V(-90f, 55f, 40f), V(70f, 80f, -20f) };
            WarpSpawn.Result a = Place(gates, 1);
            WarpSpawn.Result b = Place(gates, 1);
            Assert.AreEqual(a.Pos.X, b.Pos.X, 0f);
            Assert.AreEqual(a.Pos.Y, b.Pos.Y, 0f);
            Assert.AreEqual(a.Pos.Z, b.Pos.Z, 0f);
            Assert.AreEqual(a.Yaw, b.Yaw, 0f);
        }

        [Test]
        public void EveryGateInTheMapCanBeSpawnedAt()
        {
            var gates = new[] { V(-120f, 50f, 0f), V(0f, 50f, 130f), V(90f, 50f, -90f) };
            for (int i = 0; i < gates.Length; i++)
            {
                WarpSpawn.Result r = Place(gates, i);
                Assert.AreEqual(i, r.Index);
                Assert.AreEqual(3, r.Count);
                Assert.AreEqual(WarpSpawn.Clearance, Dist(r.Pos, gates[i]), 0.01f);
            }
        }

        // ── legal water ──────────────────────────────────────────────────────────

        [Test]
        public void SpawnIsNeverInTheSand()
        {
            const float sand = 90f;
            // A gate whose pivot sits below the seabed (a map sculpted after the gate was placed).
            var gates = new[] { V(60f, 88f, 0f) };
            WarpSpawn.Result r = Place(gates, 0, seabedY: sand);

            float floor = sand + DroneFlight.CamRadius + DroneFlight.FloorClearance;
            Assert.GreaterOrEqual(r.Pos.Y, floor - 1e-3f, "the dive opened with the camera buried");
            Assert.AreEqual(floor, WarpSpawn.ClampDepth(0f, sand, Water), 1e-4f);
        }

        [Test]
        public void SpawnIsNeverAboveTheSurface()
        {
            var gates = new[] { V(60f, 900f, 0f) };
            WarpSpawn.Result r = Place(gates, 0);

            float ceiling = Water - DroneFlight.CeilingClearance;
            Assert.LessOrEqual(r.Pos.Y, ceiling + 1e-3f, "the dive opened in mid-air");
            Assert.AreEqual(ceiling, WarpSpawn.ClampDepth(999f, 0f, Water), 1e-4f);
        }

        [Test]
        public void WaterThinnerThanTheDrone_PutsTheFloorFirst()
        {
            // Sand almost at the surface: floor and ceiling cross. Being stopped on the sand is the
            // answer the flight model gives, so it is the answer here too.
            float y = WarpSpawn.ClampDepth(100f, seabedTopY: 99f, waterLevel: 100f);
            Assert.AreEqual(99f + DroneFlight.CamRadius + DroneFlight.FloorClearance, y, 1e-4f);
        }

        [Test]
        public void AHeightInsideTheWaterIsLeftAlone()
        {
            Assert.AreEqual(120f, WarpSpawn.ClampDepth(120f, 0f, Water), 1e-4f);
        }

        // ── solids ───────────────────────────────────────────────────────────────

        [Test]
        public void AGateInsideAWreck_DoesNotSpawnYouInsideIt()
        {
            // The gate stands at x=100; the landing spot is 22 u toward the centre, i.e. x=78 —
            // right inside this block.
            DroneFlight.Solid wreck = DroneFlight.Solid.Aabb(new DroneFlight.Box
            {
                MinX = 60f, MinY = 40f, MinZ = -40f,
                MaxX = 90f, MaxY = 80f, MaxZ = 40f,
            });
            var gates = new[] { V(100f, 60f, 0f) };

            WarpSpawn.Result loose = Place(gates, 0);
            Assert.Less(loose.Pos.X, 90f, "the test is pointless unless the plain spawn is inside");

            WarpSpawn.Result r = Place(gates, 0, solids: new[] { wreck });
            Assert.IsTrue(r.AtWarp);

            bool outside = r.Pos.X <= 60f - DroneFlight.CamRadius + 1e-3f
                        || r.Pos.X >= 90f + DroneFlight.CamRadius - 1e-3f
                        || r.Pos.Y <= 40f - DroneFlight.CamRadius + 1e-3f
                        || r.Pos.Y >= 80f + DroneFlight.CamRadius - 1e-3f
                        || r.Pos.Z <= -40f - DroneFlight.CamRadius + 1e-3f
                        || r.Pos.Z >= 40f + DroneFlight.CamRadius - 1e-3f;
            Assert.IsTrue(outside, $"spawned inside the wreck at ({r.Pos.X},{r.Pos.Y},{r.Pos.Z})");
        }

        [Test]
        public void TheDiverStartsStill()
        {
            // The settle runs the flight model, so it must not hand the diver a shove: a dive that
            // opens already drifting is a dive nobody asked to start.
            var gates = new[] { V(100f, 60f, 0f) };
            WarpSpawn.Result a = Place(gates, 0);
            WarpSpawn.Result b = Place(gates, 0, solids: new DroneFlight.Solid[0]);
            Assert.AreEqual(a.Pos.X, b.Pos.X, 1e-4f);
            Assert.AreEqual(a.Pos.Y, b.Pos.Y, 1e-4f);
            Assert.AreEqual(a.Pos.Z, b.Pos.Z, 1e-4f);
        }

        // ── two gates that overlap each other's rings ────────────────────────────

        [Test]
        public void LandingSpotIsPushedOffASecondGate()
        {
            // Gate A at x=60 → the plain landing spot is x=38, which is exactly where gate B is.
            // Being born on top of B would open the picker before the player touched a stick.
            var gates = new[] { V(60f, 50f, 0f), V(38f, 50f, 0f) };
            WarpSpawn.Result r = Place(gates, 0);

            Assert.IsTrue(r.AtWarp);
            Assert.GreaterOrEqual(WarpSpawn.NearestGateDistance(gates, r.Pos), WarpSpawn.RearmRadius,
                                  "spawned inside another gate's ring");
        }

        [Test]
        public void NearestGateDistance_MeasuresAllThreeAxes()
        {
            var gates = new[] { V(0f, 0f, 0f), V(30f, 40f, 0f) };
            Assert.AreEqual(50f, WarpSpawn.NearestGateDistance(gates, V(60f, 80f, 0f)), 1e-3f);
            Assert.AreEqual(float.MaxValue, WarpSpawn.NearestGateDistance(null, V(0f, 0f, 0f)));
            Assert.AreEqual(float.MaxValue,
                            WarpSpawn.NearestGateDistance(new DroneFlight.Vec3[0], V(0f, 0f, 0f)));
        }
    }
}
