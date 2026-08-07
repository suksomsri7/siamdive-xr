using System.Collections.Generic;
using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// Pins litter landing (<see cref="TrashPhysics.FloorUnder"/>) and tap picking
    /// (<see cref="TrashPhysics.PickForTap"/>) — the user's two reports: litter falls THROUGH
    /// the wreck, and litter half-buries itself on slopes because its landing height was
    /// sampled where it spawned, not where it drifted.
    /// </summary>
    public class TrashPhysicsTests
    {
        private static SolidBoxes.Box B(double x0, double y0, double z0,
                                       double x1, double y1, double z1)
            => SolidBoxes.Box.FromMinMax(x0, y0, z0, x1, y1, z1);

        private static SolidBoxes.Group Flat(SolidBoxes.Box worldBox, SolidBoxes.Box[] fine = null)
            => new SolidBoxes.Group
            {
                Coarse = worldBox,
                Fine = fine,
                Origin = default,
                Rot = Quat.Identity,
            };

        [Test]
        public void OpenWater_LandsOnTheSeabed()
        {
            float y = TrashPhysics.FloorUnder(0, 0, 100, 5, new List<SolidBoxes.Group>());
            Assert.That(y, Is.EqualTo(5f));
            Assert.That(TrashPhysics.FloorUnder(0, 0, 100, 5, null), Is.EqualTo(5f));
        }

        [Test]
        public void CoarseOnlyObject_CatchesThePieceOnItsLid()
        {
            // A 10-high crate under the column: the piece rests on its top, not inside it.
            var g = Flat(B(-5, 0, -5, 5, 10, 5));
            float y = TrashPhysics.FloorUnder(1, 1, 100, 0, new[] { g });
            Assert.That(y, Is.EqualTo(10f));
        }

        [Test]
        public void MissesTheCrate_Sideways_FallsToTheSand()
        {
            var g = Flat(B(-5, 0, -5, 5, 10, 5));
            Assert.That(TrashPhysics.FloorUnder(9, 0, 100, 0, new[] { g }), Is.EqualTo(0f));
        }

        [Test]
        public void HullWithAHole_LetsThePieceThrough()
        {
            // An arch: two pillars, nothing in between. Coarse spans the whole thing — the old
            // single-AABB behaviour would land litter mid-air on the lid; the hull must not.
            var arch = Flat(
                B(-10, 0, -2, 10, 20, 2),
                new[] { B(-10, 0, -2, -6, 20, 2), B(6, 0, -2, 10, 20, 2) });
            // Through the opening: seabed.
            Assert.That(TrashPhysics.FloorUnder(0, 0, 100, 0, new[] { arch }), Is.EqualTo(0f));
            // Onto a pillar: its top.
            Assert.That(TrashPhysics.FloorUnder(8, 0, 100, 0, new[] { arch }), Is.EqualTo(20f));
        }

        [Test]
        public void RotatedHull_StillCatchesThePiece()
        {
            // The same pillar turned 90° about Y (a box symmetric in X/Z, so the world shape is
            // identical) — the frame math must give the same answer as the unrotated one.
            var q = new Quat(0f, 0.7071068f, 0f, 0.7071068f);
            var g = new SolidBoxes.Group
            {
                Coarse = B(-4, 0, -4, 4, 12, 4),
                Fine = new[] { B(-4, 0, -4, 4, 12, 4) },
                Origin = default,
                Rot = q,
            };
            float y = TrashPhysics.FloorUnder(0, 0, 100, 0, new[] { g });
            Assert.That(y, Is.EqualTo(12f).Within(1e-3));
        }

        [Test]
        public void SlopeStory_RefloorAtTheDriftedXz_ChangesTheAnswer()
        {
            // Two shelves at different heights. A piece spawned over the low one but drifting
            // over the high one must land on the HIGH one — the whole sink-into-the-floor bug.
            var low = Flat(B(0, 0, 0, 10, 2, 10));
            var high = Flat(B(10, 0, 0, 20, 8, 10));
            var solids = new[] { low, high };
            Assert.That(TrashPhysics.FloorUnder(5, 5, 100, 0, solids), Is.EqualTo(2f));
            Assert.That(TrashPhysics.FloorUnder(15, 5, 100, 0, solids), Is.EqualTo(8f));
        }

        [Test]
        public void PieceAlreadyBelowTheLid_IsNotTeleportedUp()
        {
            // topY below the crate lid: the crate cannot catch what already fell past it.
            var g = Flat(B(-5, 0, -5, 5, 10, 5));
            Assert.That(TrashPhysics.FloorUnder(0, 0, 6, 0, new[] { g }), Is.EqualTo(0f).Within(1e-3),
                "a lid above the probe top must not raise the floor");
        }

        // ── tap picking ─────────────────────────────────────────────────────────────

        private static Vec3 V(float x, float y, float z) => new Vec3(x, y, z);

        [Test]
        public void Tap_PicksTheNearestPieceOnTheRay()
        {
            var pieces = new List<Vec3> { V(0, 0, 30), V(0, 0, 10), V(0, 5, 20) };
            int hit = TrashPhysics.PickForTap(V(0, 0, 0), V(0, 0, 1), pieces, 2.5f, 90f);
            Assert.That(hit, Is.EqualTo(1), "10 beats 30; the one 5 off-axis is outside the radius");
        }

        [Test]
        public void Tap_TorchRange_IsAHardWall()
        {
            var pieces = new List<Vec3> { V(0, 0, 91) };
            Assert.That(TrashPhysics.PickForTap(V(0, 0, 0), V(0, 0, 1), pieces, 2.5f, 90f),
                        Is.EqualTo(-1), "past the light = out of reach");
            Assert.That(TrashPhysics.PickForTap(V(0, 0, 0), V(0, 0, 1), pieces, 2.5f, 92f),
                        Is.EqualTo(0));
        }

        [Test]
        public void Tap_BehindTheCamera_NeverPicks()
        {
            var pieces = new List<Vec3> { V(0, 0, -5) };
            Assert.That(TrashPhysics.PickForTap(V(0, 0, 0), V(0, 0, 1), pieces, 2.5f, 90f),
                        Is.EqualTo(-1));
        }

        [Test]
        public void Tap_UnnormalisedRay_IsNormalisedInside()
        {
            var pieces = new List<Vec3> { V(0, 0, 50) };
            Assert.That(TrashPhysics.PickForTap(V(0, 0, 0), V(0, 0, 10), pieces, 2.5f, 90f),
                        Is.EqualTo(0), "a scaled direction must not scale the range test");
        }
    }
}
