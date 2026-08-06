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

        // ── Per-species floor inside the SlowAnim band ────────────────────────────
        //
        // 🔴 2026-08-06, build 280 on a real iPhone: *"ฉลามวาฬโบกหางน้อยไป"*.
        //
        // The whale shark is the one animal in the app wearing the <see cref="SpeciesFlag.SlowAnim"/>
        // flag (SpeciesBehavior.cs:139, transcribed from builder.html:1779), so it is the one animal
        // whose clip runs on the `eff*0.30` branch of <see cref="TimeScale"/> instead of `eff*0.45`.
        // Its log line from build 280 reads `clips=6 pick=cruise timeScale=0.32 slowAnim=True`, and
        // 0.32 is not a tuned number — it is <see cref="SlowRateMin"/>, the FLOOR. At cruise
        // <see cref="SwimStyle.Effort"/> returns 1.0, `1.0 × 0.30 = 0.30` falls below the floor, and
        // the clamp lifts it back to 0.32. It is pinned there: the animal has to work 3.5× harder
        // than cruise before the band's own arithmetic lets it off the bottom, which a patrolling
        // whale shark never does. The band [0.32, 0.85] exists and this animal only ever sees the
        // first number in it.
        //
        // What the web does, for the record, is nothing that helps: builder.html:1072 ships
        // `msh:whaleshark` with no clip at all, so on the web the tail simply does not move and the
        // GLB falls to `animateGLB()`'s `swim` case (:3599), which bobs the body and rolls it a few
        // degrees. There is no web number to copy here; the rig, the six clips and the whole clip
        // path are this app's, and the choice of where inside the web's band to sit is ours to make.
        //
        // So: raise the FLOOR for this species and leave the ceiling where the web put it. The
        // effort term keeps working (0.55 → 0.85 is still live headroom), the animal can never play
        // faster than the web's own limit for a slow animal, and if the next build comes back with
        // "ยังน้อยอยู่" or "มากไป" it is one number here that moves.

        /// <summary>
        /// Where <c>msh:whaleshark</c> cruises inside the <see cref="SlowRateMin"/>…<see cref="SlowRateMax"/>
        /// band. 1.72× build 280's 0.32.
        ///
        /// Derived rather than eyeballed, from this app's OWN answer for the same animal. A whale
        /// shark is drawn at ≈65 u; <see cref="SwimStyle.For"/> puts a 65 u shark at
        /// <c>clamp(1.1/√(65/6), 0.25, 2.0) × 0.75 = 0.25 Hz</c> — one tail beat every four
        /// seconds. A ~2 s cruise clip carrying one beat plays at 0.16 Hz at a time scale of 0.32,
        /// which is 1.6× slower than the app's own mesh-bending table says the very same species
        /// should beat; 0.32 × 1.6 ≈ 0.51, taken up to 0.55 to sit clear of the old value while
        /// still leaving a third of the band above it.
        ///
        /// 🔎 What this CANNOT fix, said plainly because the next report will decide it: the swing
        /// is baked into the clip, so this changes how OFTEN the tail sweeps and not how FAR. If
        /// "โบกหางน้อยไป" turns out to mean the arc is too small rather than too rare, no value
        /// here reaches it and the next lever is a procedural bend blended over the clip — which
        /// the web has no precedent for either (it does no bone or morph work over a clip anywhere
        /// in builder.html) and which is deliberately NOT part of this change.
        /// </summary>
        public const double WhalesharkSlowFloor = 0.55;

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
            => TimeScale(effort, slowAnim, animMul, gliding, SlowRateMin);

        /// <summary>
        /// <see cref="TimeScale(double,bool,double,bool)"/> with the SlowAnim band's floor named
        /// by the caller — see <see cref="WhalesharkSlowFloor"/> and <see cref="SlowFloorFor"/>.
        ///
        /// Only the floor is per-species. The ceiling stays <see cref="SlowRateMax"/> for
        /// everybody, because that one IS the web's (builder.html:2445) and a species knob has no
        /// business raising a limit the web set.
        /// </summary>
        public static double TimeScale(double effort, bool slowAnim, double animMul, bool gliding,
                                       double slowFloor)
        {
            double e = effort > 0.0 ? effort : 0.0;
            double floor = Clamp(slowFloor > 0.0 ? slowFloor : SlowRateMin, SlowRateMin, SlowRateMax);
            double r = slowAnim
                ? Clamp(e * SlowRateK, floor, SlowRateMax)
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

        /// <summary>
        /// Where this species sits at the bottom of the SlowAnim band —
        /// <see cref="WhalesharkSlowFloor"/> for the whale shark, <see cref="SlowRateMin"/> for
        /// everything else. See the block above <see cref="WhalesharkSlowFloor"/>.
        ///
        /// Bare-id matched for the same reason every other table read in this file is: the pivot
        /// the runtime hands over is called <c>Item_7_msh:whaleshark</c>, and an exact-match lookup
        /// on that string finds nothing and silently leaves the animal at the old floor — which
        /// would look exactly like the change not working.
        /// </summary>
        public static double SlowFloorFor(string assetId)
        {
            string id = Bare(assetId);
            if (string.Equals(id, "msh:whaleshark", StringComparison.OrdinalIgnoreCase))
                return WhalesharkSlowFloor;
            return SlowRateMin;
        }

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

        // ── The gate: who is ALLOWED to have a rig at all ─────────────────────────

        /// <summary>
        /// 🔴 THE SAFETY RULE FOR THIS WHOLE CHANGE. True only for an object that has something to
        /// drive its clips; false for everything else, which must have its
        /// <c>UnityEngine.Animation</c> component stripped the moment the GLB lands.
        ///
        /// Adding <c>com.unity.modules.animation</c> to the manifest does not just let US play
        /// clips — it switches glTFast's whole animation path back on for EVERY model in the
        /// library. <c>GameObjectInstantiator.AddAnimation</c> (GameObjectInstantiator.cs:126-146)
        /// adds an <see cref="UnityEngine.Animation"/> component to any GLB carrying clips and
        /// assigns <c>animation.clip = clips[0]</c>; Unity's <c>playAutomatically</c> defaults to
        /// TRUE, so that clip starts on its own. Nothing in this app asked for it and nothing in
        /// this app would stop it.
        ///
        /// That is not hypothetical. The 3D pipeline has been running rig transfer across the
        /// library, and a statue or a wreck that came back with a stray take in it would begin
        /// moving the moment this module landed — which is the exact regression that was just
        /// fixed for the temple statues. A screenshot of a statue mid-clip looks like a physics
        /// bug, not like an import setting.
        ///
        /// So the permission is positive and narrow: <see cref="MarineRoute.Solo"/> only.
        ///   • Solo — has a <c>WhaleController</c>, which binds the clips and drives them.
        ///   • School — instanced from one shared mesh; a SkinnedMeshRenderer cannot be instanced,
        ///     so the skin is dropped and the clip would animate a template nobody draws.
        ///   • None (statues, wrecks, coral, warp gates, everything else) — nothing should move.
        /// </summary>
        public static bool MayAnimate(string assetId, string kind)
            => MarineRouting.For(assetId, kind) == MarineRoute.Solo;

        /// <summary>
        /// Why <see cref="MayAnimate"/> said no, for the <c>[AnimGate]</c> log line — one token,
        /// so "the statue moved again" is one grep rather than another CI round.
        /// </summary>
        public static string GateReason(string assetId, string kind)
        {
            switch (MarineRouting.For(assetId, kind))
            {
                case MarineRoute.Solo:   return "solo";
                case MarineRoute.School: return "instanced-school";
                default:                 return "not-an-animal";
            }
        }

        // ── The CI oracle: did the clip actually MOVE anything? ───────────────────

        /// <summary>
        /// The smallest skeleton displacement, in world units, that counts as motion.
        ///
        /// 🔴 It is a "not exactly zero" gate, not a quality bar, and that is deliberate. The XR
        /// marine models are ~1.9 units across before their item scale, so a flipper stroke moves a
        /// bone by 0.05-0.5 — three orders of magnitude clear of this. Anything at or under 1e-4 is
        /// a rig that was bound and then never driven, which is the ONE failure that looks
        /// identical to success in every other log line we have.
        /// </summary>
        public const double MinPoseDelta = 1e-4;

        /// <summary>Clip-seconds that must be consumed for the live probe to count as running.</summary>
        public const double MinTimeAdvanced = 1e-3;

        /// <summary>
        /// One model's answer to "is the clip really playing", measured two independent ways.
        ///
        /// 🔴 Two ways, because they fail separately and the difference names the owner.
        /// <see cref="PoseDelta"/> poses the rig by hand (set the time, sample, read the bones) and
        /// so tests the FILE and the import: non-zero means glTFast built real curves against real
        /// joints. <see cref="LiveDelta"/> and <see cref="TimeAdvanced"/> let Unity's own animation
        /// update run for a few frames and so test the APP: non-zero means the component is
        /// enabled, un-culled, and being ticked. A file that is fine inside a harness that is not
        /// running it produces poseDelta &gt; 0 with liveDelta = 0, and nothing else in CI can say so.
        /// </summary>
        public readonly struct ClipProbe
        {
            /// <summary>Clips glTFast handed over.</summary>
            public readonly int Clips;
            /// <summary>Bones on the skinned renderer. 0 = the skin did not survive import.</summary>
            public readonly int Bones;
            /// <summary>Largest bone movement between t=0 and t=½·length, posed deterministically.</summary>
            public readonly double PoseDelta;
            /// <summary>Largest bone movement across <see cref="Frames"/> of ordinary Update.</summary>
            public readonly double LiveDelta;
            /// <summary>Clip-seconds consumed in those frames.</summary>
            public readonly double TimeAdvanced;
            /// <summary>How many frames the live probe ran for.</summary>
            public readonly int Frames;

            public ClipProbe(int clips, int bones, double poseDelta, double liveDelta,
                             double timeAdvanced, int frames)
            {
                Clips = clips; Bones = bones; PoseDelta = poseDelta;
                LiveDelta = liveDelta; TimeAdvanced = timeAdvanced; Frames = frames;
            }
        }

        /// <summary>
        /// One greppable token naming the FIRST thing that is wrong, in the order a human would
        /// have to fix them. Never a sentence: the CI step greps these lines out of a 20 MB log and
        /// a verdict with a space in it wraps and stops being findable.
        ///
        ///   <c>no-clips</c>      — the GLB shipped no animation (asset pipeline's problem)
        ///   <c>no-skin</c>       — clips arrived but the skinned mesh did not (Draco/import)
        ///   <c>frozen-pose</c>   — curves exist and move nothing (rig transfer targeted wrong joints)
        ///   <c>frozen-live</c>   — the file is fine; the app is not ticking it (our problem)
        ///   <c>moving</c>        — it works
        /// </summary>
        public static string Verdict(ClipProbe p)
        {
            if (p.Clips <= 0) return "no-clips";
            if (p.Bones <= 0) return "no-skin";
            if (p.PoseDelta <= MinPoseDelta) return "frozen-pose";
            if (p.TimeAdvanced <= MinTimeAdvanced || p.LiveDelta <= MinPoseDelta) return "frozen-live";
            return "moving";
        }

        /// <summary>Did this model pass? Only <c>moving</c> does.</summary>
        public static bool Passes(ClipProbe p) => Verdict(p) == "moving";

        /// <summary>
        /// The <c>[Anim]</c> line for one QC model. Built here rather than in the harness for the
        /// same reason <c>QcPixels.Line</c> is: a log line that is the ONLY evidence a run produces
        /// is a thing worth having a test for.
        /// </summary>
        public static string ProbeLine(string assetId, string[] names, ClipRole role,
                                       string pick, double length, double timeScale,
                                       string reason, ClipProbe p)
        {
            string verdict = Verdict(p);
            return "[Anim] qc asset=" + (assetId ?? "-")
                 + " clips=" + p.Clips
                 + " names=" + (names == null || names.Length == 0 ? "-" : string.Join(",", names))
                 + " pick=" + (string.IsNullOrEmpty(pick) ? "-" : pick)
                 + " role=" + role
                 + " length=" + length.ToString("0.00") + "s"
                 + " timeScale=" + timeScale.ToString("0.00")
                 + " bones=" + p.Bones
                 + " poseDelta=" + p.PoseDelta.ToString("0.0000")
                 + " liveDelta=" + p.LiveDelta.ToString("0.0000")
                 + " advanced=" + p.TimeAdvanced.ToString("0.000") + "s/" + p.Frames + "f"
                 + " mode=" + (Motion(p.Clips, false) == BodyMotion.Clip ? "clip" : "wave")
                 + " reason=" + (string.IsNullOrEmpty(reason) ? "-" : reason)
                 + " verdict=" + verdict;
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
