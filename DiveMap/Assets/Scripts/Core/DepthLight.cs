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
        ///
        /// 🔴 WO-E3: 0.35 → 0.25. Both of the earlier numbers were chosen against a pipeline with
        /// no tone mapping, where the only protection a midtone had was to start high — anything
        /// that got dark STAYED dark all the way to the byte. ACES has a toe: it lifts and holds
        /// the bottom of the range instead of letting it slide to black (a linear 0.02 comes out at
        /// byte 40, not 12). The floor's job is unchanged — the deep must stay readable — but with
        /// the curve doing that job the floor can go back to attenuating rather than propping, and
        /// a quarter of surface light at 55 m is much nearer what the water actually does.
        ///
        /// Now also multiplies the FOG and the BACKDROP, not just the subject (see
        /// <see cref="WaterFog"/>), so lowering it dims the whole picture together rather than
        /// pushing the subject further behind the background.
        public const float Floor = 0.25f;

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
        /// 🔴 DEPTH IS NOT THE SAME QUESTION AS PATH, AND ONLY ONE OF THEM IS WHAT A TORCH ANSWERS.
        ///
        /// The user's sentence, which is the whole design note: "ถ้าแสงสีแดงหาย ทำให้พื้นดำ แต่ในโลกจริง
        /// คือเมื่อโดนแสงไฟฉาย สีเดิมจะกลับมา". It is right, and it is right because Beer–Lambert
        /// absorbs along the DISTANCE THE LIGHT TRAVELS THROUGH WATER, not along the depth of the
        /// thing being lit:
        ///
        ///   • daylight / ambient — surface → object. At 30 m that path IS 30 m, so red is gone
        ///     (e^-30/5 ≈ 0.002) and <see cref="Attenuation"/> is the correct model for it.
        ///   • a torch — lamp → object → eye. A diver's torch is held a metre or two from the wall,
        ///     so its path is a metre or two whatever the depth, red survives almost intact, and
        ///     the rust comes back orange. That is why divers carry one.
        ///
        /// So the depth curve must never be applied to a lamp. This function is the honest model of
        /// what a lamp's path costs, and it exists to be read as much as to be called — the numbers
        /// it returns are why <see cref="DiveLightMath"/>'s lamps are NOT attenuated at all:
        ///
        ///   path  2 m → R 0.670    path  9 m → R 0.165    path 18 m → R 0.027
        ///
        /// The drone aims its lamps <see cref="DiveLightMath.Reach"/> = 54 u = 9 m ahead and the
        /// camera trails it by about as much again, so a literal round-trip path is ~18 m and a
        /// literal model would leave 2.7% of the red — i.e. it would delete the feature. The app's
        /// camera stands ten times further from the subject than a diver's mask does; applying the
        /// diver's-eye absorption to the app's camera distance would be modelling the wrong
        /// geometry. Not attenuating the lamp is the closer approximation to what the user is
        /// describing, and it is deliberate rather than an oversight.
        ///
        /// No <see cref="Floor"/> here: the floor is a playability prop for the ambient (the deep
        /// must stay readable), and a short path does not need propping up.
        /// </summary>
        public static void PathTransmittance(float pathUnits, out float r, out float g, out float b)
        {
            float m = pathUnits / UnitsPerMetre;
            if (m <= 0f || float.IsNaN(m)) { r = g = b = 1f; return; }

            r = (float)Math.Exp(-m / RedDepth);
            g = (float)Math.Exp(-m / GreenDepth);
            b = (float)Math.Exp(-m / BlueDepth);
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
