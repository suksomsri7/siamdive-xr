using System;

namespace DiveMap.Core
{
    /// <summary>
    /// B2 — the shape of the sea surface, one vertex at a time.
    ///
    /// Ported from builder.html:3931, which is one line and every number in it matters:
    /// <code>
    ///   surfPos.setZ(i, Math.sin(x*0.03 + t*1.1)*3
    ///                 + Math.cos(y*0.045 + t*0.85)*2.4
    ///                 + Math.sin((x+y)*0.02 + t*0.6)*1.6);
    /// </code>
    ///
    /// 🔎 This replaces a version that had the right IDEA — displace the vertices — with invented
    /// constants: two waves instead of three, and a total amplitude of 1.6 units against the web's
    /// 7. The sea was there but it was a ripple on a pond, four times too flat, and no test could
    /// have caught it because nothing said what the right answer was. Opening the web's line was
    /// the only way. (The same trap PARITY has recorded twice: numbers must be read, not guessed.)
    ///
    /// Axes: the web builds the plane in XY and rotates it −90° about X, which maps its local
    /// (x, y) to world (x, −z) and its displaced local z to world up. So the web's <c>y</c> is our
    /// <c>−z</c>. Substituted below rather than left as a comment, because a sign that lives only
    /// in prose gets lost the first time someone edits the formula.
    ///
    /// Three terms, not one: a single train makes every crest parallel and the sea reads as a
    /// corrugated roof. Their periods (1.1 / 0.85 / 0.6) share no common multiple worth the name,
    /// so the pattern does not visibly repeat.
    /// </summary>
    public static class WaterWaveMath
    {
        // Spatial frequencies and amplitudes, exactly the web's.
        public const double FreqX = 0.03, FreqZ = 0.045, FreqDiag = 0.02;
        public const double AmpX = 3.0, AmpZ = 2.4, AmpDiag = 1.6;
        public const double RateX = 1.1, RateZ = 0.85, RateDiag = 0.6;

        /// <summary>The largest height the surface can reach — 7 units, crest to flat.</summary>
        public const double MaxAmplitude = AmpX + AmpZ + AmpDiag;

        /// <summary>
        /// Surface height above the still-water level at world (<paramref name="x"/>,
        /// <paramref name="z"/>) at time <paramref name="t"/> seconds.
        /// </summary>
        public static double Height(double x, double z, double t)
        {
            double webY = -z;   // the web's local y is our −z (see the class note)
            return Math.Sin(x * FreqX + t * RateX) * AmpX
                 + Math.Cos(webY * FreqZ + t * RateZ) * AmpZ
                 + Math.Sin((x + webY) * FreqDiag + t * RateDiag) * AmpDiag;
        }
    }
}
