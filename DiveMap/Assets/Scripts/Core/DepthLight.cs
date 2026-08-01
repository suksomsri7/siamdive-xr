using System;

namespace DiveMap.Core
{
    /// <summary>
    /// How much daylight is left at a given depth.
    ///
    /// Asked for after a dive on the phone: "in real life the shallows are brighter than the deep,
    /// so the design should follow that." It should, and it did not — the scene lit the sand under
    /// 40 m exactly as brightly as the surface, which is why the water read as a flat blue room
    /// rather than as water.
    ///
    /// Water absorbs light roughly exponentially with depth (Beer–Lambert), and it does it
    /// UNEVENLY across the spectrum: red is gone by ~5 m, green survives to ~25 m, blue carries
    /// furthest. That is the whole reason deep water looks blue-green and a torch turns a grey
    /// wreck brown again. So this returns a per-channel multiplier, not a single dimmer.
    ///
    /// Numbers are chosen to look like the sea rather than to satisfy a physics text: at the
    /// surface nothing changes, at 18 m (a recreational dive) it is noticeably dimmer and blue,
    /// and it never reaches black — a floor keeps the deepest water readable, because a game the
    /// player cannot see is not realism, it is a bug report.
    /// </summary>
    public static class DepthLight
    {
        /// <summary>World units per metre (builder.html:600).</summary>
        public const float UnitsPerMetre = 6f;

        /// <summary>Depth in metres at which each channel falls to 1/e of its surface value.</summary>
        public const float RedDepth = 5f;
        public const float GreenDepth = 26f;
        public const float BlueDepth = 55f;

        /// <summary>Never darker than this, or the deep is unplayable rather than atmospheric.</summary>
        /// 0.18 was too dark on the phone — the whole scene read as deep navy. The curve still
        /// separates shallow from deep (that is the point), it just does it from a brighter start:
        /// tropical water is bright, and a dive site nobody can see is not realism.
        public const float Floor = 0.35f;

        /// <summary>
        /// Multiplier for (r, g, b) at <paramref name="depthUnits"/> below the surface.
        /// Above the surface — the map view often sits there — nothing is attenuated.
        /// </summary>
        public static void Attenuation(float depthUnits, out float r, out float g, out float b)
        {
            float m = depthUnits / UnitsPerMetre;
            if (m <= 0f || float.IsNaN(m)) { r = g = b = 1f; return; }

            r = Channel(m, RedDepth);
            g = Channel(m, GreenDepth);
            b = Channel(m, BlueDepth);
        }

        private static float Channel(float metres, float scale)
        {
            float v = (float)Math.Exp(-metres / scale);
            return Floor + (1f - Floor) * v;
        }

        /// <summary>
        /// Overall brightness at a depth — the average of the three channels. Handy for a single
        /// dimmer (sun intensity) where a tint would be wrong.
        /// </summary>
        public static float Brightness(float depthUnits)
        {
            Attenuation(depthUnits, out float r, out float g, out float b);
            return (r + g + b) / 3f;
        }

        /// <summary>
        /// How far the eye sees at this depth, as a fraction of the surface value. Light that is
        /// not there cannot come back off a rock 200 units away, so the fog closes in as you go
        /// down — which is what makes descending FEEL like descending.
        /// </summary>
        public static float VisibilityScale(float depthUnits)
        {
            float b = Brightness(depthUnits);
            // Visibility does not collapse as fast as colour does: 0.5 brightness is still a long
            // way from 0.5 visibility underwater.
            return 0.45f + 0.55f * b;
        }
    }
}
