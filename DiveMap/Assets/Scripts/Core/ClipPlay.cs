using System;

namespace DiveMap.Core
{
    /// <summary>
    /// Which baked behaviour an animal's body should be showing. The names are the WEB's
    /// (builder.html:1436-1441 <c>clipRole</c>), not invented here, because the XR marine GLBs
    /// are authored against that exact vocabulary — <c>msh_turtle_xr0.glb</c> ships
    /// <c>glide, cruise, burst, turn, patrol, accent</c>.
    /// </summary>
    public enum ClipRole
    {
        /// <summary>Ordinary swimming. <c>pick('cruise','glide')</c>.</summary>
        Cruise = 0,
        /// <summary>Coasting between strokes. <c>pick('glide')</c>.</summary>
        Glide = 1,
        /// <summary>A dart, a sprint, a strike. <c>pick('burst','strong','cruise')</c>.</summary>
        Fast = 2,
        /// <summary>Banking through a turn. <c>pick('turn','banking','cruise')</c>.</summary>
        Turn = 3,
    }

    /// <summary>How an animal's body is being moved. The two are mutually exclusive — see
    /// <see cref="ClipPlay.Motion"/>.</summary>
    public enum BodyMotion
    {
        /// <summary>The GLB's own skinned animation clip is playing.</summary>
        Clip = 0,
        /// <summary>No clip to play — the vertex-bending wave shader stands in.</summary>
        Wave = 1,
    }

    /// <summary>
    /// The rules for playing a marine GLB's OWN animation clips, transcribed from the web build.
    ///
    /// 🔴 Why this file exists. Until now nothing in this app ever played a clip. The manifest's
    /// <c>animated</c> flag was parsed and dropped on the floor; every animal, rigged or not, was
    /// moved by bending its mesh in a shader (<see cref="SwimStyle"/> + <c>DM_FishWaveDetail</c>).
    /// That is a good stand-in for a static model and a poor substitute for a hand-authored swim
    /// cycle, which is precisely the report that came back from a real iPhone: "สัตว์ขยับตัวไม่
    /// สมจริง … ของเดิมบนเว็บดีกว่ามาก". The web plays the clip; we did not.
    ///
    /// Everything here is pure so it can be asserted by <c>tools/test.sh</c> on a machine with no
    /// Unity in it — the Unity-side wrapper is <c>Runtime/Marine/BodyClips.cs</c>, which owns the
    /// legacy <c>UnityEngine.Animation</c> component and nothing else.
    ///
    /// ── The two rules that must not drift from the web ────────────────────────────────────────
    ///
    ///   • playback rate — builder.html:2445-2448
    ///       animRate = (slowAnim ? clamp(eff*0.30, 0.32, 0.85)
    ///                            : clamp(eff*0.45, 0.9, 3.0)) * (animMul||1)
    ///       if (glide|swoop  &amp;&amp;  (big||neverRest||manta))  animRate = max(0.15, animRate*0.3)
    ///
    ///   • clip choice — builder.html:1434-1444, by NAME, with index 0 as the last resort.
    /// </summary>
    public static class ClipPlay
    {
        // ── Playback rate (builder.html:2445-2448) ────────────────────────────────

        /// <summary>Ordinary animal: <c>eff * 0.45</c>.</summary>
        public const double RateK = 0.45;
        /// <summary>Ordinary animal floor. Not 0: "ตัวใหญ่/ช้า หางยังตีปกติ ไม่แข็ง" (:2446).</summary>
        public const double RateMin = 0.9;
        /// <summary>Ordinary animal ceiling.</summary>
        public const double RateMax = 3.0;

        /// <summary><see cref="SpeciesFlag.SlowAnim"/> (the whale shark): <c>eff * 0.30</c>.</summary>
        public const double SlowRateK = 0.30;
        /// <summary>SlowAnim floor — "หางสะบัดช้า กว้าง สง่า" (:2445).</summary>
        public const double SlowRateMin = 0.32;
        /// <summary>SlowAnim ceiling.</summary>
        public const double SlowRateMax = 0.85;

        /// <summary>A coasting glider's clip runs at 30 % (:2448).</summary>
        public const double GlideMul = 0.3;
        /// <summary>…but never stops dead. A frozen rig is the "แช่แข็ง" complaint (:2448).</summary>
        public const double GlideFloor = 0.15;

        /// <summary>
        /// How long to blend from one role's clip to the next, in seconds.
        ///
        /// 🔎 The web uses <c>crossFadeTo(next, 1.2, true)</c> (builder.html:2114) — 1.2 s. This is
        /// 0.4 s because WO-F3 asked for 0.4 s, and because Unity's legacy
        /// <c>UnityEngine.Animation.CrossFade</c> blends toward a clip that is ALSO being
        /// re-timed every frame, where a long blend reads as a lag rather than a transition.
        /// Named and public so the difference is a decision on the record, not a typo.
        /// </summary>
        public const float CrossFadeSec = 0.4f;

        /// <summary>
        /// Below this fraction of its own cruise speed, a glider is considered to be coasting.
        ///
        /// 🔎 An interpretation, and marked as one. The web has an explicit <c>state</c> string and
        /// tests <c>/glide|swoop/</c> against it; this app's solo animals have no such string —
        /// their behaviour is a patrol speed and a <see cref="HuntPhase"/>. Speed against cruise is
        /// the same fact the web's glide state is a label for, and it is the fact
        /// <see cref="SwimStyle.Effort"/> already reads, so no second source of truth is created.
        /// </summary>
        public const double GlideSpeedRatio = 0.5;

        /// <summary>
        /// The web's <c>animRate</c>, exactly (builder.html:2445-2448).
        /// </summary>
        /// <param name="effort">
        /// How hard the animal is swimming — <see cref="SwimStyle.Effort"/>, which is the SAME
        /// number that drives the wave shader's beat. One effort, two consumers.
        /// </param>
        /// <param name="slowAnim">The species' <see cref="SpeciesFlag.SlowAnim"/> flag.</param>
        /// <param name="animMul">
        /// The species' hand-tuned clip multiplier (humpback 0.72), or 1 for everything else.
        /// </param>
        /// <param name="gliding">Coasting between strokes — see <see cref="Gliding"/>.</param>
        public static double TimeScale(double effort, bool slowAnim, double animMul, bool gliding)
        {
            double e = effort > 0.0 ? effort : 0.0;
            double r = slowAnim
                ? Clamp(e * SlowRateK, SlowRateMin, SlowRateMax)
                : Clamp(e * RateK, RateMin, RateMax);

            // × animMul comes AFTER the clamp, exactly as on the web (:2447): the humpback's 0.72
            // is meant to take it BELOW the ordinary 0.9 floor, which is the whole point of it.
            r *= animMul > 0.0 ? animMul : 1.0;

            // …and the glide cut comes after that (:2448), for the same reason.
            if (gliding) r = Math.Max(GlideFloor, r * GlideMul);
            return r;
        }

        /// <summary>
        /// The animals the web lets coast: <c>(u.big || u.neverRest || u.manta)</c> (:2448).
        /// A reef fish beats its tail the whole time; a shark, a ray and a turtle do not.
        /// </summary>
        public static bool IsGlider(string assetId)
        {
            SpeciesBehavior.Cfg c = SpeciesBehavior.For(Bare(assetId));
            return c.Big || c.NeverRest || c.Manta;
        }

        /// <summary>Is this animal coasting right now? See <see cref="GlideSpeedRatio"/>.</summary>
        public static bool Gliding(double speed, double cruiseSpeed, bool glider)
        {
            if (!glider) return false;
            double c = cruiseSpeed > 1e-4 ? cruiseSpeed : 1.0;
            return (speed > 0.0 ? speed : 0.0) <= c * GlideSpeedRatio;
        }

        /// <summary>The species' hand-tuned clip multiplier, or 1. Never 0, never negative.</summary>
        public static double AnimMulFor(string assetId)
        {
            SpeciesBehavior.Cfg c = SpeciesBehavior.For(Bare(assetId));
            return c.HasAnimMul && c.AnimMul > 0.0 ? c.AnimMul : 1.0;
        }

        /// <summary>The species' <see cref="SpeciesFlag.SlowAnim"/> flag.</summary>
        public static bool SlowAnimFor(string assetId)
            => SpeciesBehavior.For(Bare(assetId)).SlowAnim;

        // ── Which clip (builder.html:1434-1444) ───────────────────────────────────

        /// <summary>
        /// The clip names this role prefers, best first. Transcribed from the web's
        /// <c>clipRole</c> table; <see cref="ClipRole.Glide"/> is the web's <c>calm</c>.
        /// </summary>
        public static string[] RoleNames(ClipRole role)
        {
            switch (role)
            {
                case ClipRole.Glide: return new[] { "glide" };
                case ClipRole.Fast:  return new[] { "burst", "strong", "cruise" };
                case ClipRole.Turn:  return new[] { "turn", "banking", "cruise" };
                default:             return new[] { "cruise", "glide" };
            }
        }

        /// <summary>
        /// Index into <paramref name="names"/> of the clip to play for <paramref name="role"/>,
        /// or −1 when there is nothing to play at all.
        ///
        /// Order: the role's own names (the web's list) → <c>AAction</c> → clip 0.
        ///
        /// 🔴 <c>AAction</c> is not a web name and is not decoration. Blender's glTF exporter
        /// writes the default action out under that name, and a model that came through the 3D
        /// pipeline with a single un-renamed take is otherwise indistinguishable from a model with
        /// no clip worth playing. Falling through to clip 0 would reach it anyway; naming it makes
        /// the [Anim] log line say WHY that clip was chosen, which is the difference between a log
        /// that diagnoses and a log that reassures.
        /// </summary>
        public static int IndexOf(string[] names, ClipRole role)
        {
            if (names == null || names.Length == 0) return -1;
            string[] wanted = RoleNames(role);
            for (int i = 0; i < wanted.Length; i++)
            {
                int hit = Find(names, wanted[i]);
                if (hit >= 0) return hit;
            }
            int blender = Find(names, "AAction");
            return blender >= 0 ? blender : 0;
        }

        /// <summary>Exact, case-insensitive. glTF clip names are authored, not parsed.</summary>
        private static int Find(string[] names, string wanted)
        {
            for (int i = 0; i < names.Length; i++)
                if (names[i] != null &&
                    string.Equals(names[i], wanted, StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        // ── Clip or wave — never both ─────────────────────────────────────────────

        /// <summary>
        /// Which of the two body-motion systems this animal gets.
        ///
        /// 🔴 They are mutually exclusive and the reason is not stylistic. <c>DM_FishWaveDetail</c>
        /// bends the mesh's vertices along its own long axis; a skinned clip moves the BONES under
        /// those same vertices. Run both and the bend is applied on top of a body that is already
        /// bending — the tail doubles back on itself. So a rigged model plays its clip and keeps
        /// the GLB's own materials, and only a model with nothing to play gets the wave.
        ///
        /// 🔴 …and a SHOAL always gets the wave, whatever its GLB ships. A school is drawn with
        /// <c>Graphics.RenderMeshInstanced</c> from one shared mesh: there is no
        /// GameObject per fish to hang an <c>UnityEngine.Animation</c> on, and a
        /// SkinnedMeshRenderer cannot be instanced at all. That is why the two paths are separated
        /// by this function rather than by "does the file have clips".
        /// </summary>
        public static BodyMotion Motion(int clipCount, bool instanced)
            => !instanced && clipCount > 0 ? BodyMotion.Clip : BodyMotion.Wave;

        /// <summary>
        /// Why <see cref="Motion"/> answered the way it did, for the <c>[Anim]</c> log line. One
        /// token, greppable, and it is the whole point of the line: "the turtle is not animating"
        /// has four different causes and a screenshot separates none of them.
        /// </summary>
        public static string MotionReason(int clipCount, bool instanced)
        {
            if (instanced) return "instanced-school";
            if (clipCount <= 0) return "no-clips";
            return "-";
        }

        // ── Shared with SwimStyle ─────────────────────────────────────────────────

        /// <summary>
        /// <c>Item_7_msh:whaleshark</c> → <c>msh:whaleshark</c>. The table lookups above are exact
        /// matches, so the prefixed pivot name must be stripped first. Delegated to
        /// <see cref="SwimStyle.BareId"/> rather than re-implemented: a second copy of this rule is
        /// a second way for a whale shark to silently be read as an ordinary animal.
        /// </summary>
        private static string Bare(string assetId) => SwimStyle.BareId(assetId);

        private static double Clamp(double v, double lo, double hi)
            => v < lo ? lo : (v > hi ? hi : v);
    }
}
