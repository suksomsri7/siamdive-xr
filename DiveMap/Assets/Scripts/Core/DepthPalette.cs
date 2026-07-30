using System;

namespace DiveMap.Core
{
    /// <summary>
    /// P2 — the depth heat-map's colours and its legend, ported from the web (builder.html
    /// 626-643). A three-stop ramp, bright green-teal in the shallows through blue to a dark
    /// navy at 100 m, chosen there for contrast rather than realism: this view exists to be
    /// READ, not to look like water.
    ///
    /// Pure so the ramp can be tested and reused by both the seabed texture and the legend bar
    /// without either drifting from the other.
    /// </summary>
    public static class DepthPalette
    {
        /// <summary>The deepest depth the ramp resolves; the web clamps its scale at 100 m.</summary>
        public const float MaxMetres = 100f;

        private static readonly float[][] Stops =
        {
            new[] { 0.55f, 0.92f, 0.50f },   // shallow
            new[] { 0.13f, 0.62f, 0.88f },
            new[] { 0.06f, 0.09f, 0.42f },   // deep
        };

        public struct Rgb
        {
            public float R, G, B;
            public Rgb(float r, float g, float b) { R = r; G = g; B = b; }
        }

        /// <summary>Colour for a normalised depth (0 = surface, 1 = <see cref="MaxMetres"/>).</summary>
        public static Rgb Color(float t)
        {
            float f = t * 2f;
            if (f < 0f) f = 0f;
            if (f > 2f) f = 2f;
            int i = (int)Math.Floor(f);
            if (i > 1) i = 1;
            float a = f - i;
            float[] A = Stops[i], B = Stops[i + 1];
            return new Rgb(A[0] + (B[0] - A[0]) * a,
                           A[1] + (B[1] - A[1]) * a,
                           A[2] + (B[2] - A[2]) * a);
        }

        /// <summary>
        /// Depth in metres of a seabed height, the web's <c>depthMetres()</c>: the water column
        /// above it divided by <see cref="ItemPicker.UnitsPerMetre"/>, clamped to 0-100.
        /// </summary>
        public static float Metres(float topY, float waterLevel)
        {
            double d = (waterLevel - topY) / ItemPicker.UnitsPerMetre;
            if (d < 0) d = 0;
            if (d > MaxMetres) d = MaxMetres;
            return (float)d;
        }

        /// <summary>Colour for a seabed height directly (metres → normalised → ramp).</summary>
        public static Rgb ColorForHeight(float topY, float waterLevel)
            => Color(Metres(topY, waterLevel) / MaxMetres);
    }
}
