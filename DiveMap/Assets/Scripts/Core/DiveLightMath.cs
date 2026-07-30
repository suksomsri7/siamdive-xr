using System;

namespace DiveMap.Core
{
    /// <summary>
    /// P1.2 — the numbers behind the drone's dive light and headlamps, ported from the web
    /// (<c>_applyHeadlight()</c> builder.html:3828-3840 and the per-frame placement at
    /// 3751-3765). Pure, so the "does the light actually change anything" question is answered
    /// by tests rather than by staring at two screenshots.
    ///
    /// The important design point, which is easy to lose: the headlight is not just a light.
    /// The web swaps the WHOLE underwater atmosphere with it — fog range, fog colour and ambient
    /// intensity — so that turning it off makes the water close in around you (near 70 / far 200,
    /// almost black-green) and turning it on opens the view up (near 170 / far 680, blue). That
    /// contrast IS the feature; a headlight that only adds a cone reads as a torch on full
    /// daylight, which is what the app looks like today.
    /// </summary>
    public static class DiveLightMath
    {
        /// <summary>Fog + ambient preset for one headlight state.</summary>
        public struct Atmosphere
        {
            public float FogNear, FogFar;
            public float FogR, FogG, FogB;
            /// <summary>Multiplier on the scene's ambient/sun intensity.</summary>
            public float AmbientMul;
            /// <summary>Intensity of the point light that travels with the drone.</summary>
            public float DiveLight;
        }

        /// <summary>Headlight ON: 0x18638a, near 170, far 680, ambient ×0.55, dive light 2.2.</summary>
        public static Atmosphere HeadlightOn => new Atmosphere
        {
            FogNear = 170f, FogFar = 680f,
            FogR = 0.094f, FogG = 0.388f, FogB = 0.541f,   // 0x18638a
            AmbientMul = 0.55f,
            DiveLight = 2.2f,
        };

        /// <summary>Headlight OFF: 0x08303f, near 70, far 200, ambient ×0.32, dive light 0.5.</summary>
        public static Atmosphere HeadlightOff => new Atmosphere
        {
            FogNear = 70f, FogFar = 200f,
            FogR = 0.031f, FogG = 0.188f, FogB = 0.247f,   // 0x08303f
            AmbientMul = 0.32f,
            DiveLight = 0.5f,
        };

        public static Atmosphere For(bool headlightOn) => headlightOn ? HeadlightOn : HeadlightOff;

        // ── headlamp placement (builder.html 3752-3757) ───────────────────────────

        /// <summary>How far ahead of the drone the lamps aim.</summary>
        public const float Reach = 54f;
        /// <summary>Lateral separation of the two lamps at the drone itself.</summary>
        public const float LampSeparation = 2f;

        /// <summary>
        /// Radius of the light pool on the sand: 1.2 × height above it, clamped 20…92 — a low
        /// pass gives a tight bright circle, a high pass a broad wash.
        /// </summary>
        public static float PoolRadius(float droneY, float groundY)
        {
            float r = (droneY - groundY) * 1.2f;
            if (r < 20f) r = 20f;
            if (r > 92f) r = 92f;
            return r;
        }

        /// <summary>Sideways offset of each pool centre — half the radius, so the two overlap.</summary>
        public static float PoolOffset(float poolRadius) => poolRadius * 0.5f;

        /// <summary>
        /// Beam scale for a lamp whose pool is <paramref name="poolRadius"/> across at
        /// <paramref name="distance"/> away (web: rad/9 wide, distance/60 long, min 0.5).
        /// </summary>
        public static void BeamScale(float poolRadius, float distance, out float width, out float length)
        {
            width = Math.Max(0.5f, poolRadius / 9f);
            length = Math.Max(0.5f, distance / 60f);
        }

        // ── fish bubble (builder.html droneBubble, :1668-1673) ────────────────────

        /// <summary>Radius of the cavity the drone pushes fish out of (~16 u across in the web).</summary>
        public const float FishBubble = 8f;

        /// <summary>
        /// How far a fish at horizontal distance <paramref name="d"/> from the drone is displaced
        /// outward. The web's own curve: <c>(bub−d)/bub · bub</c> — full bubble radius right at
        /// the centre, nothing at the rim, so fish part around you instead of through you.
        /// </summary>
        public static float BubblePush(float d, float bubble = FishBubble)
        {
            if (d >= bubble || d <= 0.01f) return 0f;
            return (bubble - d) / bubble * bubble;
        }
    }
}
