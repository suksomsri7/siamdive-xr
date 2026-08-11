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

        /// <summary>Hamilton product, component-for-component identical to
        /// <c>UnityEngine.Quaternion.operator *</c> and <c>THREE.Quaternion.multiplyQuaternions</c>.
        /// <c>a * b</c> means "apply b, then a" — the same reading as Unity's.</summary>
        public static Quat operator *(Quat a, Quat b) => new Quat(
            a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
            a.W * b.Y + a.Y * b.W + a.Z * b.X - a.X * b.Z,
            a.W * b.Z + a.Z * b.W + a.X * b.Y - a.Y * b.X,
            a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

        /// <summary>Rotate a vector by this quaternion (v' = q·v·q⁻¹).</summary>
        public Vec3 Rotate(Vec3 v)
        {
            // t = 2·(q_vec × v);  v' = v + w·t + q_vec × t
            double tx = 2.0 * (Y * v.Z - Z * v.Y);
            double ty = 2.0 * (Z * v.X - X * v.Z);
            double tz = 2.0 * (X * v.Y - Y * v.X);
            return new Vec3(
                v.X + W * tx + (Y * tz - Z * ty),
                v.Y + W * ty + (Z * tx - X * tz),
                v.Z + W * tz + (X * ty - Y * tx));
        }

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

        /// <summary>
        /// A DIRECTION across the handedness flip (WO-O). Same linear map as
        /// <see cref="PositionToUnity(Vec3)"/> — the flip is a reflection with no translation, so
        /// a direction transforms exactly like a point. Spelled out as its own method because
        /// reusing the position one for a vector reads like a bug even when it is not, and the
        /// gizmo's axis handles need the web's +Z to become Unity's −Z or the blue arrow drags
        /// the object backwards.
        /// </summary>
        public static Vec3 DirectionToUnity(Vec3 web) => new Vec3(web.X, web.Y, -web.Z);

        public static Vec3 DirectionToUnity(double x, double y, double z)
            => new Vec3(x, y, -z);

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

        // ── Imported geometry: glTFast's X-mirror → this app's Z-mirror ──────────
        //
        // 🔴 THE BUG THIS FIXES (user report, build 261: "หน้า-หลังสลับ และตำแหน่งการวาง
        //    ไม่ตรง"). Both handedness maps below are individually valid; using ONE for the
        //    mesh and the OTHER for the transform is not.
        //
        //   • glTFast negates X on its way in — verified in the pinned package source,
        //     com.unity.cloud.gltfast 6.19.0: Runtime/Scripts/Jobs.cs:771 and :887
        //     (`tmp.x *= -1` on every POSITION/NORMAL), Runtime/Scripts/NodeExtension.cs:63-76
        //     (node translation `-t[0]`, node rotation `(x,-y,-z,w)`), plus the flipped
        //     triangle winding that compensates for the reflection.
        //   • The web (three.js) and therefore every saved item transform is Y-up
        //     RIGHT-handed, so PositionToUnity/RotationToUnity above negate Z.
        //
        //   Composing the two:  diag(1,1,-1) · diag(-1,1,1) = diag(-1,1,-1) = Ry(180°).
        //   So an imported model sat in the scene rotated a HALF TURN about its own vertical
        //   axis — front for back — and the further its geometry reaches from its pivot the
        //   further that half turn throws it (Atlantis' domed temple is placed at scale 402:
        //   its façade landed 804 units from where the web draws it).
        //
        //   The correction is exactly that half turn, applied to the imported hierarchy so
        //   the mesh ends up Z-mirrored like everything else:
        //       Ry(180°) · diag(-1,1,1) = diag(1,1,-1)  ✔
        //   It is a proper rotation (det +1), so glTFast's winding fix and its normals stay
        //   valid — this is a re-orientation, not a second reflection.

        /// <summary>Half turn about +Y: converts glTFast's X-mirrored import into the
        /// Z-mirrored convention the rest of this app (and the web) uses.</summary>
        public static readonly Quat ImportedAxisFix = new Quat(0, 1, 0, 0);

        /// <summary>The fix applied to a point of the imported hierarchy.</summary>
        public static Vec3 FixImportedPoint(Vec3 v) => new Vec3(-v.X, v.Y, -v.Z);

        /// <summary>The fix applied to an orientation of the imported hierarchy.</summary>
        public static Quat FixImportedRotation(Quat q) => ImportedAxisFix * q;

        // ── The whole placement pipeline, as one testable pair of functions ───────
        //
        // These exist so "does the app put this model where the web puts it" can be ASKED,
        // in the units a player sees, instead of being argued from trigonometry. See
        // WebCoordTests.PlacedModel_MatchesTheWeb_PointForPoint.

        /// <summary>
        /// Where a point of the model — <paramref name="modelPoint"/>, in the GLB's own authored
        /// space — is drawn by the WEB builder for an item saved with this position/rotation/scale.
        /// (builder.html:3281-3291 assigns p/r/s straight onto the loaded group.)
        /// </summary>
        public static Vec3 WebWorldPoint(Vec3 webPos, Vec3 webEulerXYZ, Vec3 scale, Vec3 modelPoint)
        {
            Quat rot = EulerXYZToQuat(webEulerXYZ.X, webEulerXYZ.Y, webEulerXYZ.Z);
            Vec3 scaled = new Vec3(modelPoint.X * scale.X, modelPoint.Y * scale.Y, modelPoint.Z * scale.Z);
            Vec3 turned = rot.Rotate(scaled);
            return new Vec3(webPos.X + turned.X, webPos.Y + turned.Y, webPos.Z + turned.Z);
        }

        /// <summary>
        /// Where this app draws that same model point: glTFast's X-mirror, then (when
        /// <paramref name="axisFix"/>) the half turn above, then the item transform.
        /// Pass <c>false</c> to reproduce the build-261 behaviour.
        /// </summary>
        public static Vec3 UnityWorldPoint(Vec3 webPos, Vec3 webEulerXYZ, Vec3 scale, Vec3 modelPoint,
                                           bool axisFix = true)
        {
            // What glTFast hands Unity for that vertex.
            Vec3 imported = new Vec3(-modelPoint.X, modelPoint.Y, modelPoint.Z);
            if (axisFix) imported = FixImportedPoint(imported);

            Vec3 scaled = new Vec3(imported.X * scale.X, imported.Y * scale.Y, imported.Z * scale.Z);
            Vec3 turned = RotationToUnity(webEulerXYZ).Rotate(scaled);
            Vec3 pivot = PositionToUnity(webPos);
            return new Vec3(pivot.X + turned.X, pivot.Y + turned.Y, pivot.Z + turned.Z);
        }

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
