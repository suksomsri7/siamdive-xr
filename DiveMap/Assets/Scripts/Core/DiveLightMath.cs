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

        /// <summary>
        /// Headlight ON. Fog colour and the dive light are the web's; the reach is NOT.
        ///
        /// 🔎 Deliberate divergence, asked for after diving the build on a phone: the web lifts
        /// AMBIENT to 0.55 and pushes fog out to 680 u when you switch the lamps on, which lights
        /// the entire map at once — the seabed 300 u behind you brightens as much as the rock in
        /// front of the lamp. A torch does not do that, and the moment it stops behaving like a
        /// torch there is no reason to carry one.
        ///
        /// So the ambient lift is small (0.32 → 0.38, enough that switching on reads as a change
        /// in the water itself) and the light now comes from the lamps, which have a range. Fog
        /// far 380 u ≈ 63 m: past that the map fades into the dark whether the lamps are on or
        /// not, which is what gives the beam something to be brighter THAN.
        /// (U_PER_M = 6, builder.html:600.)
        /// </summary>
        public static Atmosphere HeadlightOn => new Atmosphere
        {
            FogNear = 140f, FogFar = 280f,
            FogR = 0.180f, FogG = 0.478f, FogB = 0.643f,   // อ่อนลงไปทางฟ้าตามที่ user ขอ
            AmbientMul = 0.72f,
            DiveLight = 2.2f,
        };

        /// <summary>
        /// How far a lamp throws, in world units. The web says 460 u = 77 m, which on a 340 u map
        /// is "everything" — the far rim is lit as brightly as the sand under the drone. A real
        /// dive torch is useful to about 25 m in clear water, and that is the number here.
        /// </summary>
        /// 150 u (25 m) was still "far too bright" on the phone: a torch that reaches a quarter of
        /// the map lights the whole scene by reflection. 90 u = 15 m, which is what a real dive
        /// light gives you before the beam is lost in the water.
        // 15 ส.ค. 2026 — user ขอลดสองรอบ: 90 → 62 → 50
        // ลำแสงที่ยิงไกลเกินทำให้พื้นทั้งผืนสว่างเท่ากันหมด มิติความลึกหายไป และของที่อยู่ไกล
        // ดูเหมือนอยู่ใกล้ — ระยะสั้นลงคืน "ขอบของแสง" ซึ่งเป็นสิ่งที่ทำให้รู้สึกว่ากำลังดำน้ำ
        public const float LampRange = 50f;

        /// <summary>Headlight OFF: 0x08303f, near 70, far 200, ambient ×0.55, dive light 0.5.</summary>
        /// <remarks>
        /// 🔴 This line said "×0.32" until 11 ส.ค. while the field below said 0.55, and it cost a CI
        /// round: the qcblank positive control took its "blank frame" threshold from the comment
        /// (mean ≤ 46) when a fully fogged frame can never be darker than this fog colour itself —
        /// 0x08303f is a mid navy, Rec.601 luminance 56.8. The gate was unsatisfiable, so a perfect
        /// reproduction of the bug still reported "did not reproduce". Read the field, not the prose.
        /// Same stale figure appears in DroneLights' header; both are corrected.
        /// </remarks>
        public static Atmosphere HeadlightOff => new Atmosphere
        {
            FogNear = 70f, FogFar = 200f,
            FogR = 0.078f, FogG = 0.271f, FogB = 0.353f,   // ปิดไฟก็ยังไม่ทึบเท่าเดิม
            AmbientMul = 0.55f,
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
