using System;

namespace DiveMap.Core
{
    /// <summary>
    /// Pure-C# double-precision 3-vector. Used to keep coordinate math independent
    /// of UnityEngine so the conversion is deterministic and unit-testable in EditMode.
    /// </summary>
    public readonly struct Vec3
    {
        public readonly double X, Y, Z;
        public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }
        public double[] ToArray() => new[] { X, Y, Z };
        public static Vec3 FromArray(double[] a) => new Vec3(a[0], a[1], a[2]);
        public override string ToString() => $"({X}, {Y}, {Z})";
    }

    /// <summary>
    /// Pure-C# double-precision quaternion (x, y, z, w). Hamilton convention,
    /// identical component layout to three.js THREE.Quaternion and UnityEngine.Quaternion.
    /// </summary>
    public readonly struct Quat
    {
        public readonly double X, Y, Z, W;
        public Quat(double x, double y, double z, double w) { X = x; Y = y; Z = z; W = w; }
        public static readonly Quat Identity = new Quat(0, 0, 0, 1);

        public double Dot(Quat o) => X * o.X + Y * o.Y + Z * o.Z + W * o.W;

        public Quat Normalized()
        {
            double n = Math.Sqrt(X * X + Y * Y + Z * Z + W * W);
            if (n < 1e-15) return Identity;
            return new Quat(X / n, Y / n, Z / n, W / n);
        }

        /// <summary>Rotation about a unit axis by <paramref name="radians"/>.</summary>
        public static Quat FromAxisAngle(Vec3 axis, double radians)
        {
            double h = radians * 0.5;
            double s = Math.Sin(h);
            return new Quat(axis.X * s, axis.Y * s, axis.Z * s, Math.Cos(h)).Normalized();
        }

        /// <summary>
        /// Absolute rotation angle (radians, 0..pi) between two orientations.
        /// Sign/double-cover agnostic — the correct way to compare orientations
        /// without hitting Euler gimbal ambiguity.
        /// </summary>
        public static double AngleBetween(Quat a, Quat b)
        {
            double d = Math.Abs(a.Normalized().Dot(b.Normalized()));
            if (d > 1.0) d = 1.0;
            return 2.0 * Math.Acos(d);
        }

        public override string ToString() => $"({X}, {Y}, {Z}, {W})";
    }

    /// <summary>
    /// Converts between the SiamDive web builder coordinate space (three.js:
    /// Y-up, RIGHT-handed, Euler order 'XYZ' intrinsic, radians) and Unity
    /// (Y-up, LEFT-handed). This is the single source of truth referenced by
    /// DESIGN_DOC §1.2 rule 2 ("แปลงมือ ... utility เดียว CoordJS.ToUnity()/ToWeb()").
    ///
    /// ── Position ─────────────────────────────────────────────────────────────
    ///   Both spaces are Y-up. Handedness differs only in Z, so:  z → -z.
    ///
    /// ── Rotation ─────────────────────────────────────────────────────────────
    ///   NEVER swap Euler axes directly. Convert the web Euler (XYZ order) to a
    ///   quaternion, then mirror through the Z reflection, then (for saving back)
    ///   convert the mirrored quaternion back to a three.js XYZ Euler.
    ///
    ///   Reflection across the Z axis (the map from RH→LH here) sends a rotation
    ///   quaternion (qx,qy,qz,qw) to (-qx,-qy,qz,qw). Derivation: reflecting space
    ///   by z→-z sends a rotation of angle θ about axis (ax,ay,az) to a rotation of
    ///   angle -θ about the reflected axis (ax,ay,-az). With v = sin(θ/2)·axis and
    ///   w = cos(θ/2):  θ→-θ negates sin(θ/2), and az→-az, so
    ///       v' = -sin(θ/2)·(ax,ay,-az) = sin(θ/2)·(-ax,-ay,az)
    ///   i.e. (qx,qy,qz,qw) → (-qx,-qy,qz,qw), w unchanged. This map is its own
    ///   inverse (an involution), so ToUnity∘ToWeb == identity on rotations.
    ///
    ///   Worked sign check (see WebCoordTests): web Euler (0, +π/2, 0) is a +90°
    ///   yaw about +Y in a right-handed frame. Its quaternion is (0, .7071, 0, .7071).
    ///   Mirroring → (0, -.7071, 0, .7071), which in Unity's left-handed frame is a
    ///   -90° yaw about +Y. Flipping handedness reverses the sign of any rotation
    ///   about the vertical axis — hence +90° (web) ↦ -90° (Unity). This is correct
    ///   and expected, not a bug.
    /// </summary>
    public static class WebCoord
    {
        // ── Position ────────────────────────────────────────────────────────────
        public static Vec3 PositionToUnity(Vec3 web) => new Vec3(web.X, web.Y, -web.Z);
        public static Vec3 PositionToWeb(Vec3 unity) => new Vec3(unity.X, unity.Y, -unity.Z);

        public static double[] PositionToUnity(double[] web) => PositionToUnity(Vec3.FromArray(web)).ToArray();
        public static double[] PositionToWeb(double[] unity) => PositionToWeb(Vec3.FromArray(unity)).ToArray();

        // ── Scale (unchanged) ────────────────────────────────────────────────────
        public static Vec3 Scale(Vec3 s) => s;
        public static double[] Scale(double[] s) => new[] { s[0], s[1], s[2] };

        // ── Rotation ─────────────────────────────────────────────────────────────

        /// <summary>Web Euler (XYZ radians) → Unity-space rotation quaternion.</summary>
        public static Quat RotationToUnity(Vec3 webEulerXYZ)
        {
            Quat webQ = EulerXYZToQuat(webEulerXYZ.X, webEulerXYZ.Y, webEulerXYZ.Z);
            return MirrorZ(webQ);
        }

        public static Quat RotationToUnity(double[] webEulerXYZ)
            => RotationToUnity(Vec3.FromArray(webEulerXYZ));

        /// <summary>Unity-space rotation quaternion → Web Euler (XYZ radians) for save.</summary>
        public static Vec3 RotationToWeb(Quat unityQ)
        {
            Quat webQ = MirrorZ(unityQ);
            return QuatToEulerXYZ(webQ);
        }

        public static double[] RotationToWebArray(Quat unityQ) => RotationToWeb(unityQ).ToArray();

        /// <summary>Z-reflection involution mapping rotations RH↔LH: (x,y,z,w)→(-x,-y,z,w).</summary>
        public static Quat MirrorZ(Quat q) => new Quat(-q.X, -q.Y, q.Z, q.W);

        // ── three.js Euler(XYZ) ⟷ Quaternion (exact port of THREE.js math) ───────

        /// <summary>
        /// Port of THREE.Quaternion.setFromEuler(order = 'XYZ').
        /// Intrinsic XYZ: q = qX * qY * qZ.
        /// </summary>
        public static Quat EulerXYZToQuat(double x, double y, double z)
        {
            double c1 = Math.Cos(x / 2), c2 = Math.Cos(y / 2), c3 = Math.Cos(z / 2);
            double s1 = Math.Sin(x / 2), s2 = Math.Sin(y / 2), s3 = Math.Sin(z / 2);

            double qx = s1 * c2 * c3 + c1 * s2 * s3;
            double qy = c1 * s2 * c3 - s1 * c2 * s3;
            double qz = c1 * c2 * s3 + s1 * s2 * c3;
            double qw = c1 * c2 * c3 - s1 * s2 * s3;
            return new Quat(qx, qy, qz, qw);
        }

        /// <summary>
        /// Port of THREE.Euler.setFromRotationMatrix(order = 'XYZ'), fed by a rotation
        /// matrix built from the quaternion exactly as THREE.Matrix4.makeRotationFromQuaternion.
        /// Returns Euler (x, y, z) in radians.
        /// </summary>
        public static Vec3 QuatToEulerXYZ(Quat q)
        {
            Quat n = q.Normalized();
            double x = n.X, y = n.Y, z = n.Z, w = n.W;

            double x2 = x + x, y2 = y + y, z2 = z + z;
            double xx = x * x2, xy = x * y2, xz = x * z2;
            double yy = y * y2, yz = y * z2, zz = z * z2;
            double wx = w * x2, wy = w * y2, wz = w * z2;

            // Column-major rotation matrix elements (THREE naming mIJ).
            double m11 = 1 - (yy + zz);
            double m12 = xy - wz;
            double m13 = xz + wy;
            double m22 = 1 - (xx + zz);
            double m23 = yz - wx;
            double m32 = yz + wx;
            double m33 = 1 - (xx + yy);

            // setFromRotationMatrix, order 'XYZ':
            double ey = Math.Asin(Clamp(m13, -1.0, 1.0));
            double ex, ez;
            if (Math.Abs(m13) < 0.9999999)
            {
                ex = Math.Atan2(-m23, m33);
                ez = Math.Atan2(-m12, m11);
            }
            else
            {
                ex = Math.Atan2(m32, m22);
                ez = 0.0;
            }
            return new Vec3(ex, ey, ez);
        }

        private static double Clamp(double v, double lo, double hi)
            => v < lo ? lo : (v > hi ? hi : v);
    }
}
