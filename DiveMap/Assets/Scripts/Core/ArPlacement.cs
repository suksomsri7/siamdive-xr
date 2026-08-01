using System;

namespace DiveMap.Core
{
    /// <summary>
    /// F1 — where the map sits when it is standing on a real table, seen through the phone camera.
    ///
    /// The web (builder.html:2923 <c>enterAR</c>) shrinks the whole site into a group ~1.1 m wide
    /// and parks it 1.4 m in front of a camera at the origin:
    /// <code>
    ///   const sc = 1.1 / (Math.max(size.x, size.z) || 100);
    ///   arSite.scale.setScalar(sc);
    ///   arSite.position.set(-ctr.x*sc, -box.min.y*sc - 0.3, -ctr.z*sc - 1.4);
    /// </code>
    ///
    /// 🔎 This port does NOT shrink the map. It moves the CAMERA instead, and the two are the same
    /// picture — scaling the world about the eye is exactly a change of viewing distance, which a
    /// perspective projection cannot tell apart (<c>ArPlacementTests.SameEyeRayAsTheWeb</c> asserts
    /// this ray by ray). The reason to prefer the camera is that this port's fish do not live in
    /// the map's local space: <c>WhaleController</c> writes <c>transform.position</c> and
    /// <c>FishSchoolSystem</c> feeds world matrices to <c>RenderMeshInstanced</c>. Shrinking the
    /// map root would leave every whale and every school swimming at full size straight through
    /// the tabletop — a bug that only shows on a phone, which is the one place this project cannot
    /// run a test.
    ///
    /// So the AR camera is placed at <c>center − T/s</c>, where T is the web's offset and s its
    /// scale. Nothing else in the scene moves; exiting AR is one camera pose to restore rather than
    /// a per-object backup (the web needs <c>arBackup</c> only because its objects sit loose in the
    /// scene).
    /// </summary>
    public static class ArPlacement
    {
        /// <summary>How wide the site is made to look, in metres. "Fits on a café table."</summary>
        public const double TableSpan = 1.1;

        /// <summary>Metres the seabed sits below eye level, so you look down at it.</summary>
        public const double FloorDrop = 0.3;

        /// <summary>Metres in front of the viewer.</summary>
        public const double Distance = 1.4;

        // The web's − / + zoom steps (builder.html:3465-3466) used to live here. They are gone with
        // the buttons: sizing is a two-finger pinch now, and its maths is <see cref="ArPinch"/>,
        // which works in METRES ACROSS THE TABLE rather than in multiples of a fitted scale — the
        // quantity the user is actually adjusting, and the one the limits mean something in.

        /// <summary>
        /// Metres per world unit for a site whose footprint is <paramref name="sizeX"/> ×
        /// <paramref name="sizeZ"/>. The web's <c>|| 100</c> guard matters: an empty map has a
        /// zero-size box, and 1.1/0 would put the camera at infinity — a black screen with no
        /// error anywhere.
        /// </summary>
        public static double FitScale(double sizeX, double sizeZ)
        {
            double span = Math.Max(sizeX, sizeZ);
            if (double.IsNaN(span) || double.IsInfinity(span) || span <= 0) span = 100;
            return TableSpan / span;
        }

        /// <summary>
        /// Where to stand to see the site at <paramref name="scale"/> metres per unit.
        ///
        /// Derived from the web: it maps a world point p to eye-space <c>s·(p − ctr) + T</c> with
        /// the eye at the origin, where T = (0, −minY·s − 0.3, −1.4). Dividing that view vector by
        /// s (which no projection can see) gives <c>p − (ctr − T/s)</c> — the same picture with the
        /// eye at <c>ctr − T/s</c> and the map untouched.
        ///
        /// Unity is left-handed, so the web's −1.4 (three.js looks down −Z) becomes +1.4 here: the
        /// viewer stands on the −Z side and looks toward +Z.
        /// </summary>
        public static Vec3 CameraPosition(Vec3 center, double minY, double scale)
        {
            if (scale <= 0 || double.IsNaN(scale)) return center;
            return new Vec3(
                center.X,
                minY + FloorDrop / scale,        // eye 0.3 m above the seabed, in world units
                center.Z - Distance / scale);    // 1.4 m back along −Z (Unity forward is +Z)
        }

        /// <summary>Apparent width in metres of a site <paramref name="span"/> units across.</summary>
        public static double ApparentSpan(double span, double scale) => span * scale;

        /// <summary>
        /// Near/far planes for the AR eye. The viewer stands <c>1.4/scale</c> world units out, which
        /// for a 340-unit site is ~430 units — far enough that a default far plane would clip the
        /// back of the map away. Both planes are derived from the distance rather than fixed, so
        /// this holds for a 20-unit site and a 900-unit one alike.
        /// </summary>
        public static void Clipping(double span, double scale, out double near, out double far)
        {
            double dist = scale > 0 ? Distance / scale : 100;
            near = Math.Max(0.05, dist * 0.005);
            far = Math.Max(dist + span * 2.5, dist * 2.0);
        }
    }
}
