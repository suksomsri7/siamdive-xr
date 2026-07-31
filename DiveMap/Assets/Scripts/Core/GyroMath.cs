using System;

namespace DiveMap.Core
{
    /// <summary>
    /// F2 — turning the phone's attitude sensor into a camera rotation, so looking around the room
    /// looks around the map.
    ///
    /// The web reads the browser's DeviceOrientation Euler angles (builder.html:2921):
    /// <code>
    ///   eul.set(beta*deg, alpha*deg, -gamma*deg, 'YXZ');
    ///   camera.quaternion.setFromEuler(eul).multiply(q1).multiply(axisAngle(Z, -orient));
    ///   //  q1 = −90° about X   ·   orient = screen.orientation.angle
    /// </code>
    ///
    /// 🔎 This port does NOT copy those Euler lines. Unity hands us <c>Input.gyro.attitude</c> as a
    /// quaternion already — going back out through alpha/beta/gamma would reintroduce the gimbal
    /// case the browser API is famous for (look straight up and yaw stops meaning anything), and
    /// three.js is right-handed where Unity is left-handed, so a literal component-for-component
    /// copy would mirror the world. What is kept is the SHAPE of the web's expression, because each
    /// of its three terms is doing a real job:
    ///
    ///   <c>tilt · attitude · screen</c>
    ///   • attitude — where the phone is pointing, from the sensor
    ///   • tilt (left) — the sensor frame has the phone lying flat, screen up; the camera looks out
    ///     of its back. Pre-rotating by 90° about X is what makes "lying on the table" mean
    ///     "looking down at the table" instead of "looking at the horizon".
    ///   • screen (right) — rotating about the VIEW axis, so turning the phone sideways rolls the
    ///     picture back level without changing where it points.
    ///
    /// ⚠️ Honest limit — and it is narrower than it first looked. Two things were expected to need
    /// a phone; only one does:
    ///   • the screen term's SIGN turned out to be provable here. Rolling the phone by θ makes the
    ///     display report θ, and the two must cancel to nothing; only one sign does that (the other
    ///     leaves the world inverted). <c>TurningThePhoneSideways_LeavesThePictureExactlyWhereItWas</c>
    ///     checks it across four poses and four angles.
    ///   • the handedness flip in <see cref="ToUnity"/> genuinely cannot be settled without a
    ///     sensor, because nothing here knows which way the hardware calls "right". It follows the
    ///     transform documented for <c>Input.gyro.attitude</c>. The first device run must confirm
    ///     it: look right, and the view must go right, not left. If it goes left, that one negation
    ///     is the whole fix — everything around it is pinned by tests.
    /// </summary>
    public static class GyroMath
    {
        private const double Deg = Math.PI / 180.0;

        /// <summary>
        /// Camera rotation for a device attitude, in Unity's left-handed space.
        /// <paramref name="screenAngleDeg"/> is the display rotation (0/90/180/270).
        /// </summary>
        public static Quat CameraRotation(Quat attitude, double screenAngleDeg)
        {
            // The screen angle comes from the OS and is normally one of four values, but a NaN
            // here would sail through every rotation below (NaN compares false against any
            // bound, so a length check does not catch it) and reach the camera as a black
            // screen. Treated as "no rotation", which is what an unknown display angle means.
            double screenDeg = double.IsNaN(screenAngleDeg) || double.IsInfinity(screenAngleDeg)
                ? 0 : screenAngleDeg;

            Quat a = ToUnity(attitude);
            Quat tilt = Quat.FromAxisAngle(new Vec3(1, 0, 0), 90 * Deg);
            Quat screen = Quat.FromAxisAngle(new Vec3(0, 0, 1), -screenDeg * Deg);
            return Mul(Mul(tilt, a), screen).Normalized();
        }

        /// <summary>
        /// The sensor reports in a right-handed frame; Unity is left-handed. Negating Z and W
        /// mirrors the rotation into Unity's convention.
        ///
        /// A garbage attitude must not reach the camera: a phone that has not delivered a sample
        /// yet gives all zeros, and normalising that would produce NaN, which in Unity is not a
        /// visible error — it is a black screen that never recovers.
        /// </summary>
        public static Quat ToUnity(Quat gyro)
        {
            if (!IsFinite(gyro)) return Quat.Identity;
            double n = Math.Sqrt(gyro.X * gyro.X + gyro.Y * gyro.Y + gyro.Z * gyro.Z + gyro.W * gyro.W);
            if (n < 1e-9) return Quat.Identity;
            return new Quat(gyro.X, gyro.Y, -gyro.Z, -gyro.W).Normalized();
        }

        /// <summary>Hamilton product — <paramref name="a"/> applied after <paramref name="b"/>.</summary>
        public static Quat Mul(Quat a, Quat b)
        {
            return new Quat(
                a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
                a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
                a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
                a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);
        }

        /// <summary>Rotate a vector by a quaternion (v' = q v q⁻¹).</summary>
        public static Vec3 Rotate(Quat q, Vec3 v)
        {
            Quat n = q.Normalized();
            double x = n.X, y = n.Y, z = n.Z, w = n.W;
            // t = 2 (q_xyz × v);  v' = v + w t + q_xyz × t
            double tx = 2 * (y * v.Z - z * v.Y);
            double ty = 2 * (z * v.X - x * v.Z);
            double tz = 2 * (x * v.Y - y * v.X);
            return new Vec3(
                v.X + w * tx + (y * tz - z * ty),
                v.Y + w * ty + (z * tx - x * tz),
                v.Z + w * tz + (x * ty - y * tx));
        }

        /// <summary>Where the camera looks, for an attitude and screen angle.</summary>
        public static Vec3 Forward(Quat attitude, double screenAngleDeg)
            => Rotate(CameraRotation(attitude, screenAngleDeg), new Vec3(0, 0, 1));

        /// <summary>Which way is up on screen — what the screen term exists to keep level.</summary>
        public static Vec3 Up(Quat attitude, double screenAngleDeg)
            => Rotate(CameraRotation(attitude, screenAngleDeg), new Vec3(0, 1, 0));

        private static bool IsFinite(Quat q)
            => !(double.IsNaN(q.X) || double.IsNaN(q.Y) || double.IsNaN(q.Z) || double.IsNaN(q.W) ||
                 double.IsInfinity(q.X) || double.IsInfinity(q.Y) ||
                 double.IsInfinity(q.Z) || double.IsInfinity(q.W));
    }
}
