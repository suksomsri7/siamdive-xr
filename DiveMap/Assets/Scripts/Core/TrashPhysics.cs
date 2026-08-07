using System.Collections.Generic;

namespace DiveMap.Core
{
    /// <summary>
    /// Where a falling piece of litter actually lands, and which piece a screen tap picks up.
    ///
    /// 🔴 WHY — the user's report, verbatim: litter falls THROUGH the wreck and the rocks and
    /// half-buries itself in the sand. Both are the same missing fact: the scene's only physics
    /// collider is the seabed (see TourController's raycast note), so a downward ray knows about
    /// sand and nothing else. But the scenery's true shape already ships — the SolidBoxes hulls
    /// the drone collides with — so litter should land on the same boxes the diver bumps into.
    /// One source of truth; a hole the diver can swim through is a hole litter falls through.
    ///
    /// The sink-into-the-sand half has a second cause worth naming: a piece DRIFTS sideways while
    /// it falls, but its landing height was sampled once at spawn. On a slope (every sculpted
    /// map), the floor under the piece is not the floor under where it spawned. The fix is the
    /// caller's: re-ask <see cref="FloorUnder"/> at the CURRENT x/z each falling frame — this
    /// class just makes that question cheap and pure.
    ///
    /// Tap-to-collect: the pick range is the torch's reach (<see cref="DiveLightMath.LampRange"/>),
    /// by design and not by accident — you can only grab what your light can show you. The caller
    /// passes the range in so this file stays free of that policy.
    /// </summary>
    public static class TrashPhysics
    {
        /// <summary>
        /// Fallback when a ray misses a box sideways: how much higher than the box top the piece
        /// may rest when it lands on the coarse bound instead. Zero — the coarse path lands ON the
        /// bound's top face, exactly like the web's "rests on whatever it hit".
        /// </summary>
        private const double Eps = 1e-6;

        /// <summary>
        /// The Y a piece falling straight down through (<paramref name="x"/>, <paramref name="z"/>)
        /// from <paramref name="topY"/> comes to rest at: the highest hull surface under that
        /// column, or <paramref name="seabedY"/> when nothing is in the way.
        ///
        /// Groups are tested coarse-first (world AABB, X/Z only — cheap reject for the 490 objects
        /// the piece is nowhere near), then against their frame-space hull boxes through the same
        /// Origin/Rot the drone uses. A group with no fine hull lands the piece on its coarse top —
        /// fat but never leaky, the same trade every other consumer of these boxes makes.
        /// </summary>
        public static float FloorUnder(float x, float z, float topY, float seabedY,
                                       IReadOnlyList<SolidBoxes.Group> solids)
        {
            double best = seabedY;
            if (solids == null) return (float)best;

            for (int i = 0; i < solids.Count; i++)
            {
                SolidBoxes.Group g = solids[i];
                SolidBoxes.Box c = g.Coarse;
                if (x < c.Min.X || x > c.Max.X || z < c.Min.Z || z > c.Max.Z) continue;
                if (c.Max.Y <= best) continue;   // everything it could offer is below the floor we have

                if (g.Fine == null || g.Fine.Length == 0)
                {
                    // No hull: the coarse box IS the shape. Land on its lid.
                    if (c.Max.Y <= topY + Eps) best = c.Max.Y;
                    continue;
                }

                // World-down, seen from the object's own frame. Rotation preserves length, so the
                // ray parameter t is world units and worldY = topY - t.
                Vec3 o = SolidBoxes.WorldToFrame(
                    g.Origin, g.Rot, new Vec3(x, topY, z));
                Vec3 d = SolidBoxes.Rotate(
                    new Quat(-g.Rot.X, -g.Rot.Y, -g.Rot.Z, g.Rot.W),
                    new Vec3(0f, -1f, 0f));

                for (int b = 0; b < g.Fine.Length; b++)
                {
                    double t = RayBoxEntry(o, d, g.Fine[b]);
                    if (t < 0) continue;
                    double y = topY - t;
                    if (y > best && y <= topY + Eps) best = y;
                }
            }
            return (float)best;
        }

        /// <summary>
        /// Entry distance of ray (o, unit d) into box, or -1 for a miss. Plain slab test; a start
        /// inside the box reports 0 (the piece is already touching — resting where it is).
        /// </summary>
        private static double RayBoxEntry(Vec3 o, Vec3 d, SolidBoxes.Box box)
        {
            double t0 = 0.0, t1 = double.MaxValue;
            if (!Slab(o.X, d.X, box.Min.X, box.Max.X, ref t0, ref t1)) return -1;
            if (!Slab(o.Y, d.Y, box.Min.Y, box.Max.Y, ref t0, ref t1)) return -1;
            if (!Slab(o.Z, d.Z, box.Min.Z, box.Max.Z, ref t0, ref t1)) return -1;
            return t0;
        }

        private static bool Slab(double o, double d, double lo, double hi, ref double t0, ref double t1)
        {
            if (System.Math.Abs(d) < 1e-12) return o >= lo && o <= hi;
            double a = (lo - o) / d, b = (hi - o) / d;
            if (a > b) { double s = a; a = b; b = s; }
            if (a > t0) t0 = a;
            if (b < t1) t1 = b;
            return t0 <= t1;
        }

        /// <summary>
        /// Which piece a tap picks: the one nearest the camera whose centre passes within
        /// <paramref name="pickRadius"/> of the tap ray, no farther than <paramref name="maxDist"/>
        /// (the torch's reach — what the light shows is what the hand can take).
        /// Returns the index into <paramref name="positions"/>, or -1.
        /// </summary>
        public static int PickForTap(Vec3 rayOrigin, Vec3 rayDir,
                                     IReadOnlyList<Vec3> positions,
                                     float pickRadius, float maxDist)
        {
            int best = -1;
            double bestT = double.MaxValue;
            if (positions == null) return -1;
            double len = System.Math.Sqrt(rayDir.X * (double)rayDir.X + rayDir.Y * (double)rayDir.Y
                                          + rayDir.Z * (double)rayDir.Z);
            if (len < 1e-9) return -1;
            double dx = rayDir.X / len, dy = rayDir.Y / len, dz = rayDir.Z / len;

            for (int i = 0; i < positions.Count; i++)
            {
                double px = positions[i].X - rayOrigin.X;
                double py = positions[i].Y - rayOrigin.Y;
                double pz = positions[i].Z - rayOrigin.Z;
                double t = px * dx + py * dy + pz * dz;          // along-ray distance
                if (t < 0 || t > maxDist || t >= bestT) continue;
                double ox = px - t * dx, oy = py - t * dy, oz = pz - t * dz;
                if (ox * ox + oy * oy + oz * oz > pickRadius * (double)pickRadius) continue;
                best = i;
                bestT = t;
            }
            return best;
        }
    }
}
