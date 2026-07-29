using System;

namespace DiveMap.Core
{
    /// <summary>
    /// WO-XR-04.2 — the seabed's shape and colour, ported verbatim from builder.html
    /// (lines 522-618) and kept UnityEngine-free so EditMode tests pin the web's own
    /// numbers rather than a Unity-side approximation.
    ///
    /// What the web actually does, and why each piece matters to the picture:
    ///   • The footprint is a SUPERELLIPSE (|x/R|⁴+|z/R|⁴=1, <see cref="ShapeN"/>) of
    ///     radius <see cref="SandRadius"/> = 340 u — a rounded square, not the circle
    ///     Unity has been drawing. Non-uniform areaScaleX/Z stretch it (0.9 × 1.1 on the
    ///     demo map ⇒ 306 × 374).
    ///   • Sand colour is a vertex gradient from the slab's dark bottom to its light top
    ///     (<paramref name="t"/>) times a per-vertex speckle, and the outer 45% of the
    ///     radius dissolves into the deep-water tint <see cref="WaterTint"/> with a
    ///     smoothstep. That HAZE rim is why the web's floor melts into the blue instead
    ///     of ending in the flat cream oval Unity showed.
    ///   • The background is a vertical 4-stop gradient (<see cref="GradientStop"/>).
    ///     Per Fable's survey this — NOT fog — is what makes the web read as "deep".
    ///
    /// The Unity side bakes <see cref="SandColor"/> into a texture because the built-in
    /// Standard shader ignores mesh vertex colours (and a custom shader would be stripped
    /// from the build → the magenta lesson).
    /// </summary>
    public static class SeabedGeom
    {
        /// <summary>Half-extent of the sand footprint in scene units (builder.html:525).</summary>
        public const float SandRadius = 340f;
        /// <summary>Slab depth; the flat bottom sits this far under the lowest top point (builder.html:525).</summary>
        public const float SandThickness = 40f;
        /// <summary>Superellipse exponent: 2 = circle, 4 = rounded square (builder.html:526).</summary>
        public const int ShapeN = 4;
        /// <summary>Top-surface grid resolution (builder.html:537) — 28 × 96 ≈ 2.7k verts.</summary>
        public const int Rings = 28;
        public const int Segments = 96;

        /// <summary>Deep-water tint the rim fades into, ≈ the underwater fog colour (builder.html:567).</summary>
        public static readonly Rgb WaterTint = new Rgb(0.05f, 0.20f, 0.33f);

        public struct Rgb
        {
            public float R, G, B;
            public Rgb(float r, float g, float b) { R = r; G = g; B = b; }
        }

        // ── Footprint ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Distance from the centre to the superellipse boundary in direction
        /// <paramref name="angleRad"/> (builder.html seBd, line 543): 340 along the axes,
        /// 340·2^¼ ≈ 404.33 into the corners.
        /// </summary>
        public static float BoundaryDist(float angleRad)
        {
            // n is fixed at 4, so square-twice / sqrt-twice replaces both Math.Pow calls —
            // the web takes the same shortcut ("fast ^4/^¼ path", builder.html:530). This runs
            // a million times while baking the sand texture, where two pow() per texel cost
            // real seconds of startup.
            double ca = Math.Cos(angleRad); ca *= ca; ca *= ca;
            double sa = Math.Sin(angleRad); sa *= sa; sa *= sa;
            double p = ca + sa;
            if (p <= 1e-12) return SandRadius;
            return (float)(SandRadius / Math.Sqrt(Math.Sqrt(p)));
        }

        /// <summary>
        /// Normalised distance-to-boundary of a local seabed coord, matching the web's
        /// sculptAt() (builder.html:593): 0 = centre, 1 = edge in ANY direction. Used to
        /// decide whether an item still stands on the sand.
        /// </summary>
        public static float BoundaryFraction(float lx, float lz)
        {
            double fx = Math.Pow(Math.Abs(lx) / SandRadius, ShapeN);
            double fz = Math.Pow(Math.Abs(lz) / SandRadius, ShapeN);
            return (float)Math.Pow(fx + fz, 1.0 / ShapeN);
        }

        // ── Sand colour ───────────────────────────────────────────────────────────

        /// <summary>
        /// The web's per-vertex speckle (builder.html:571): a cheap deterministic wobble
        /// of the sand brightness around 0.82, ±0.09. Index-based, so it is only reusable
        /// where the vertex order is the web's — the Unity texture path feeds a spatial
        /// noise into <see cref="SandColor"/> instead, in the same 0.73…0.91 range.
        /// </summary>
        public static float VertexNoise(int i)
            => (float)(0.82 + Math.Sin(i * 12.9) * 0.05 + Math.Cos(i * 7.3) * 0.04);

        /// <summary>
        /// Sand colour (builder.html:570-575). <paramref name="t"/> 0 = slab bottom,
        /// 1 = top surface; <paramref name="radNorm"/> = hypot(x,z)/340 (NOT normalised to
        /// the boundary — the web's own definition, so the corners sit past 1.0 and are
        /// fully hazed); <paramref name="noise"/> = the speckle multiplier.
        /// </summary>
        public static Rgb SandColor(float t, float radNorm, float noise)
        {
            float r = (0.55f + 0.27f * t) * noise;
            float g = (0.48f + 0.26f * t) * noise;
            float b = (0.36f + 0.21f * t) * noise;

            // HAZE rim: full fade over the outer 45% of the radius, smoothstepped.
            float f = (radNorm - 0.55f) / 0.45f;
            if (f < 0f) f = 0f; else if (f > 1f) f = 1f;
            float k = f * f * (3f - 2f * f);

            return new Rgb(r + (WaterTint.R - r) * k,
                           g + (WaterTint.G - g) * k,
                           b + (WaterTint.B - b) * k);
        }

        // ── Background gradient ───────────────────────────────────────────────────

        // builder.html:663-667 — the vertical backdrop, top (v=0) to bottom (v=1).
        private static readonly float[] StopPos = { 0f, 0.38f, 0.52f, 1f };
        private static readonly Rgb[] StopCol =
        {
            new Rgb(0.890f, 0.949f, 0.973f), // #e3f2f8 bright surface haze
            new Rgb(0.663f, 0.831f, 0.910f), // #a9d4e8
            new Rgb(0.247f, 0.576f, 0.776f), // #3f93c6
            new Rgb(0.024f, 0.141f, 0.227f), // #06243a deep water
        };

        /// <summary>
        /// Backdrop colour at vertical position <paramref name="v"/> (0 = top of the sky
        /// dome, 1 = straight down), linearly interpolated between the web's 4 stops.
        /// </summary>
        public static Rgb GradientStop(float v)
        {
            if (v <= StopPos[0]) return StopCol[0];
            int last = StopPos.Length - 1;
            if (v >= StopPos[last]) return StopCol[last];

            for (int i = 0; i < last; i++)
            {
                if (v > StopPos[i + 1]) continue;
                float span = StopPos[i + 1] - StopPos[i];
                float f = span <= 1e-6f ? 0f : (v - StopPos[i]) / span;
                Rgb a = StopCol[i], b = StopCol[i + 1];
                return new Rgb(a.R + (b.R - a.R) * f,
                               a.G + (b.G - a.G) * f,
                               a.B + (b.B - a.B) * f);
            }
            return StopCol[last];
        }
    }
}
