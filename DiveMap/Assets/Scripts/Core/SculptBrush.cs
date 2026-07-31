using System;

namespace DiveMap.Core
{
    /// <summary>
    /// Shaping the seabed — the web's <c>applyBrush</c> (builder.html:2812), <c>sculptNoise</c>
    /// (:2821) and <c>sculptReset</c> (:2827), rewritten for the polar grid this app stores.
    ///
    /// The two sides index the floor differently and that is the whole difficulty:
    ///   • the web keeps a flat Cartesian vertex array and measures <c>hypot(x-lx, z-lz)</c>
    ///   • this app stores <c>env.sculpt</c> as a POLAR grid, <c>(ring-1)*seg + j</c>, exactly as
    ///     <c>SeabedView.SculptAt</c> reads it (Runtime/SeabedView.cs:120)
    /// So the brush converts each polar sample back to local x/z, then applies the SAME cosine
    /// falloff the web uses. Get that conversion wrong and the pit appears somewhere else — which
    /// is why the mapping is a public, tested function rather than three lines inside a loop.
    ///
    /// Heights are in world units, matching what <c>BuildPolarGrid</c> adds to the surface.
    /// </summary>
    public static class SculptBrush
    {
        /// <summary>Brush radius limits, in world units (the web's slider range).</summary>
        public const float MinRadius = 8f;
        public const float MaxRadius = 160f;
        public const float DefaultRadius = 46f;

        /// <summary>Strength per stroke, before the web's ×0.5.</summary>
        public const float MinStrength = 0.5f;
        public const float MaxStrength = 12f;
        public const float DefaultStrength = 4f;

        /// <summary>builder.html applies <c>brushStrength * 0.5</c> per stroke.</summary>
        public const float StrengthScale = 0.5f;

        /// <summary>Local X/Z of one polar sample. <paramref name="sandRadius"/> = SAND_R.</summary>
        public static void SampleXZ(int index, int rings, int seg, float sandRadius,
                                    out float x, out float z)
        {
            x = 0f; z = 0f;
            if (rings <= 0 || seg <= 0 || index < 0) return;

            int ring = index / seg + 1;          // SculptAt writes (r-1)*seg + j, r starting at 1
            int j = index % seg;

            float frac = ring / (float)rings;     // 0 = centre, 1 = rim
            float ang = j / (float)seg * (Mathf.PI2);
            float r = frac * sandRadius;

            x = (float)Math.Cos(ang) * r;
            z = (float)Math.Sin(ang) * r;
        }

        /// <summary>
        /// One brush stroke. Raises or digs with a cosine falloff — the same
        /// <c>0.5*(1+cos(π·d/R))</c> the web uses, which is what gives a pit soft edges instead
        /// of a cylindrical hole. Returns how many samples moved.
        /// </summary>
        public static int Stroke(float[] sculpt, int rings, int seg, float sandRadius,
                                 float localX, float localZ,
                                 float radius, float strength, bool raise)
        {
            if (sculpt == null || rings <= 0 || seg <= 0) return 0;

            float r = Clamp(radius, MinRadius, MaxRadius);
            float amount = Clamp(strength, MinStrength, MaxStrength) * StrengthScale * (raise ? 1f : -1f);
            int touched = 0;

            for (int i = 0; i < sculpt.Length; i++)
            {
                SampleXZ(i, rings, seg, sandRadius, out float x, out float z);
                float dx = x - localX, dz = z - localZ;
                float d = (float)Math.Sqrt(dx * dx + dz * dz);
                if (d >= r) continue;

                float falloff = 0.5f * (1f + (float)Math.Cos(Math.PI * d / r));
                sculpt[i] += amount * falloff;
                touched++;
            }
            return touched;
        }

        /// <summary>
        /// A whole natural-looking floor in one go (<c>sculptNoise</c>): three octaves of value
        /// noise, faded to flat at the rim so the seabed still meets its own edge cleanly.
        /// Deterministic in <paramref name="seed"/> — the same seed gives the same floor, which
        /// is what makes it testable at all.
        /// </summary>
        public static void Noise(float[] sculpt, int rings, int seg, float sandRadius,
                                 float amplitude, int seed)
        {
            if (sculpt == null || rings <= 0 || seg <= 0) return;

            for (int i = 0; i < sculpt.Length; i++)
            {
                SampleXZ(i, rings, seg, sandRadius, out float x, out float z);
                float radNorm = (float)Math.Sqrt(x * x + z * z) / Math.Max(1f, sandRadius);
                float fade = 1f - SmoothStep(0.7f, 1f, radNorm);

                float n = 0f, a = 1f, f = 0.013f, sum = 0f;
                for (int o = 0; o < 3; o++)
                {
                    n += ValueNoise(x * f + seed * 0.1f, z * f + seed * 0.1f, seed) * a;
                    sum += a;
                    a *= 0.5f;
                    f *= 2.2f;
                }
                sculpt[i] = (n / sum) * amplitude * fade;
            }
        }

        /// <summary>Flat again.</summary>
        public static void Reset(float[] sculpt)
        {
            if (sculpt == null) return;
            for (int i = 0; i < sculpt.Length; i++) sculpt[i] = 0f;
        }

        /// <summary>Depth reading under the brush, in metres — the web's <c>brushDepthText</c>.</summary>
        public static float DepthMetres(float waterLevel, float topY, float unitsPerMetre = 6f)
        {
            if (unitsPerMetre <= 0f) unitsPerMetre = 6f;
            return (waterLevel - topY) / unitsPerMetre;
        }

        // ── noise (the web's sbRnd / sbNoise / sbStep, ported exactly) ────────────

        public static float SmoothStep(float a, float b, float x)
        {
            if (Math.Abs(b - a) < 1e-9f) return x >= b ? 1f : 0f;
            float t = Clamp((x - a) / (b - a), 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// The web's integer hash. Written with <c>unchecked</c> because it RELIES on 32-bit
        /// overflow: JavaScript's <c>|0</c> wraps, and C# would throw in a checked context, which
        /// would give a different floor on the two platforms for the same seed.
        /// </summary>
        public static float Hash(int ix, int iz, int seed)
        {
            unchecked
            {
                int h = ix * 374761393 + iz * 668265263 + seed * 1274126177;
                h = (h ^ (int)((uint)h >> 13)) * 1274126177;
                uint u = (uint)(h ^ (int)((uint)h >> 16));
                return (u / 4294967295f) * 2f - 1f;
            }
        }

        public static float ValueNoise(float x, float z, int seed)
        {
            int ix = (int)Math.Floor(x), iz = (int)Math.Floor(z);
            float fx = x - ix, fz = z - iz;
            float u = fx * fx * (3f - 2f * fx);
            float v = fz * fz * (3f - 2f * fz);

            float a = Hash(ix, iz, seed);
            float b = Hash(ix + 1, iz, seed);
            float c = Hash(ix, iz + 1, seed);
            float d = Hash(ix + 1, iz + 1, seed);
            return (a * (1f - u) + b * u) * (1f - v) + (c * (1f - u) + d * u) * v;
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);

        /// <summary>τ. Core has no UnityEngine.Mathf, and 2π appears in every polar conversion.</summary>
        private static class Mathf
        {
            public const float PI2 = (float)(Math.PI * 2.0);
        }
    }
}
