using System;

namespace DiveMap.Core
{
    /// <summary>
    /// Two fingers deciding how big the site is on the table.
    ///
    /// This replaces the − / + stepper the web had (<c>#arMinus</c>/<c>#arPlus</c>). Asked for
    /// directly after diving the build on a phone, and it is the right call for a reason worth
    /// writing down: a stepper is a guess about how much bigger you meant, so it needs a step size
    /// nobody can pick well — 1.22× was too coarse for fine framing and too fine for "make it fill
    /// the table" — and it costs two buttons of screen in the one mode whose whole point is that
    /// the room is visible. A pinch has no step size. It is also what every other AR app on the
    /// phone does, so nobody has to be taught it.
    ///
    /// The quantity here is METRES ACROSS THE TABLE, not the internal scale factor. That is what
    /// the user is actually adjusting, it is what the limits are meaningful in ("no smaller than a
    /// coaster, no bigger than a dining table"), and it keeps the maths independent of how big the
    /// map happens to be in world units — a 40-unit reef and a 900-unit wreck site pinch the same.
    /// </summary>
    public static class ArPinch
    {
        /// <summary>Smallest the site may be made, in metres. Below this it is a detail-free lump.</summary>
        public const double MinMetres = 0.15;

        /// <summary>Largest, in metres. Past a dining table it no longer fits in the camera.</summary>
        public const double MaxMetres = 3.0;

        /// <summary>
        /// Below this many pixels a two-finger distance is noise — fingers touching, or one finger
        /// registering twice. Dividing by it would send the scale to infinity in one frame.
        /// </summary>
        public const double MinPixels = 8.0;

        /// <summary>How wide the site reads, in metres, at <paramref name="scale"/> units per metre.</summary>
        public static double MetresFor(double worldSpan, double scale)
        {
            if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale)) return 0;
            return worldSpan / scale;
        }

        /// <summary>World units per metre that make <paramref name="worldSpan"/> read as
        /// <paramref name="metres"/> across.</summary>
        public static double ScaleFor(double worldSpan, double metres)
        {
            if (metres <= 0 || double.IsNaN(metres)) return 0;
            return worldSpan / metres;
        }

        /// <summary>
        /// Where the pinch has got to. <paramref name="startMetres"/> and
        /// <paramref name="startPixels"/> are sampled when the second finger lands, so the site
        /// tracks the fingers absolutely instead of accumulating per-frame ratios — accumulation
        /// drifts, and a gesture that does not come back to where it started when the fingers do
        /// feels broken even when nobody can say why.
        /// </summary>
        public static double Pinch(double startMetres, double startPixels, double nowPixels)
        {
            if (startPixels < MinPixels || nowPixels < MinPixels ||
                double.IsNaN(startPixels) || double.IsNaN(nowPixels))
                return Clamp(startMetres);
            return Clamp(startMetres * (nowPixels / startPixels));
        }

        /// <summary>Keep the site within reach of both a table and a pair of eyes.</summary>
        public static double Clamp(double metres)
        {
            if (double.IsNaN(metres)) return MinMetres;
            if (metres < MinMetres) return MinMetres;
            if (metres > MaxMetres) return MaxMetres;
            return metres;
        }

        /// <summary>True at a stop, so the hint can say so instead of the map silently refusing.</summary>
        public static bool AtLimit(double metres) =>
            metres <= MinMetres + 1e-9 || metres >= MaxMetres - 1e-9;
    }
}
