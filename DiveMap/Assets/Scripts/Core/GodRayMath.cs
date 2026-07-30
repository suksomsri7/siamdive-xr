using System;

namespace DiveMap.Core
{
    /// <summary>
    /// WO-XR-04.3 — the deterministic math behind the sun shafts. Pure and UnityEngine-free
    /// so EditMode tests pin it: a QC screenshot can only tell us the beams look right, not
    /// that they will land in the same place on the next run.
    ///
    /// The web has no god rays at all (grep = 0) — this is the "better than the web" half of
    /// DESIGN_DOC §246-248 — but it does have the drone headlight (builder.html 3667-3670),
    /// so the cone shape and the alpha ramp are borrowed from there rather than invented.
    /// </summary>
    public static class GodRayMath
    {
        /// <summary>Sun rotation in AppBoot.SetupLighting — the shafts must be parallel to it
        /// or the light in the picture comes from two directions at once.</summary>
        public const float SunPitchDeg = 52f;
        public const float SunYawDeg = -35f;

        /// <summary>Alpha ramp stops of the web's beam texture (builder.html:3668), from the
        /// wide open end (t=0, invisible) to the bright tip (t=1).</summary>
        public const float RampMidT = 0.72f, RampMidA = 0.5f, RampTipA = 0.95f;

        public struct Vec2 { public float X, Z; public Vec2(float x, float z) { X = x; Z = z; } }
        public struct Vec3 { public float X, Y, Z; public Vec3(float x, float y, float z) { X = x; Y = y; Z = z; } }

        /// <summary>
        /// Unit direction a Unity <c>Quaternion.Euler(pitch, yaw, 0) * Vector3.forward</c>
        /// points in — Unity's ZXY order reduces to yaw(Y) ∘ pitch(X) for a zero roll.
        /// Euler(52, −35) ⇒ (−0.353, −0.788, 0.505): down, and toward −X/+Z.
        /// </summary>
        public static Vec3 Direction(float pitchDeg, float yawDeg)
        {
            double p = pitchDeg * Math.PI / 180.0;
            double y = yawDeg * Math.PI / 180.0;
            // Pitch about X: forward (0,0,1) → (0, −sin p, cos p).
            double fy = -Math.Sin(p);
            double fz = Math.Cos(p);
            // Yaw about Y (Unity, left-handed): (x,y,z) → (x·cos + z·sin, y, −x·sin + z·cos).
            double x2 = fz * Math.Sin(y);
            double z2 = fz * Math.Cos(y);
            double len = Math.Sqrt(x2 * x2 + fy * fy + z2 * z2);
            if (len < 1e-9) return new Vec3(0f, -1f, 0f);
            return new Vec3((float)(x2 / len), (float)(fy / len), (float)(z2 / len));
        }

        /// <summary>Sun shaft direction (pointing DOWN-current, i.e. the way the light travels).</summary>
        public static Vec3 SunDirection() => Direction(SunPitchDeg, SunYawDeg);

        /// <summary>
        /// Where beam <paramref name="i"/> of <paramref name="count"/> enters the water,
        /// as an offset on the XZ plane inside <paramref name="radius"/>. A golden-angle
        /// spiral with a hashed radial jitter: even coverage, no clumps, and identical every
        /// run (a random scatter would make two QC screenshots incomparable).
        /// </summary>
        public static Vec2 BeamOffset(int i, int count, float radius, int seed = 0)
        {
            if (count <= 0) count = 1;
            if (i < 0) i = 0;
            const double golden = 2.399963229728653; // π(3−√5) rad
            double ang = i * golden + seed * 0.7853981633974483;
            // sqrt keeps the spiral area-uniform; the jitter breaks the perfect ring look.
            double t = (i + 0.5) / count;
            double jitter = 0.85 + 0.30 * Frac(Math.Sin((i + 1) * 12.9898 + seed * 78.233) * 43758.5453);
            double r = radius * Math.Sqrt(t) * jitter;
            if (r > radius) r = radius;
            return new Vec2((float)(Math.Cos(ang) * r), (float)(Math.Sin(ang) * r));
        }

        /// <summary>
        /// Beam width multiplier for beam <paramref name="i"/> — a deterministic 0.55…1.0 so
        /// the shafts are not N identical cones.
        /// </summary>
        public static float BeamWidthMul(int i, int seed = 0)
        {
            double h = Frac(Math.Sin((i + 3) * 45.164 + seed * 12.9898) * 24634.6345);
            return (float)(0.55 + 0.45 * h);
        }

        /// <summary>
        /// The web beam's alpha at <paramref name="t"/> (0 = open far end, 1 = bright tip),
        /// piecewise-linear through its three canvas gradient stops.
        /// </summary>
        public static float RampAlpha(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return RampTipA;
            if (t <= RampMidT) return RampMidA * (t / RampMidT);
            return RampMidA + (RampTipA - RampMidA) * ((t - RampMidT) / (1f - RampMidT));
        }

        /// <summary>
        /// Across-the-beam softness (u = 0 one edge … 1 the other). A squared smoothstep bell:
        /// zero right at both edges, so a shaft has NO silhouette — the first version drew a
        /// cone whose alpha only varied along its length, and a cone with a hard rim reads as a
        /// translucent solid, not as light. Squaring the bell is what makes it "much softer"
        /// rather than merely dimmer.
        /// </summary>
        public static float SoftProfile(float u)
        {
            if (u <= 0f || u >= 1f) return 0f;
            float t = 1f - Math.Abs(2f * u - 1f);   // 0 at the edges, 1 in the middle
            float s = t * t * (3f - 2f * t);        // smoothstep
            return s * s;
        }

        /// <summary>
        /// Fade over the last <paramref name="band"/> of the shaft's length at the surface end
        /// (v = 1). Without it the beam starts with a hard bright cut exactly on the water
        /// plane; real shafts emerge out of the surface glare.
        /// </summary>
        public static float TopFade(float v, float band = 0.12f)
        {
            if (band <= 0f) return 1f;
            float t = (1f - v) / band;
            if (t >= 1f) return 1f;
            if (t <= 0f) return 0f;
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// Full alpha of a shaft texel: length ramp × surface-end fade × across-width softness.
        /// Zero on every edge of the quad, so nothing about the shaft has an outline.
        /// </summary>
        public static float BeamAlpha(float u, float v)
            => RampAlpha(v) * TopFade(v) * SoftProfile(u);

        /// <summary>Slow, deterministic sway angle (degrees) for beam <paramref name="i"/> at
        /// <paramref name="time"/> seconds — ±<paramref name="amplitudeDeg"/>.</summary>
        public static float SwayDeg(int i, float time, float amplitudeDeg = 2f)
            => (float)(Math.Sin(time * 0.22 + i * 1.37) * amplitudeDeg);

        /// <summary>Slow alpha breathing multiplier (0.75…1.0) so the shafts feel alive.</summary>
        public static float BreathMul(int i, float time)
            => (float)(0.875 + 0.125 * Math.Sin(time * 0.31 + i * 0.83));

        private static double Frac(double v)
        {
            double f = v - Math.Floor(v);
            return f < 0 ? f + 1 : f;
        }
    }
}
