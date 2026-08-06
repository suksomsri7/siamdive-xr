using System;
using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// WO-F3 — the rules for playing a marine GLB's OWN animation clips.
    ///
    /// 🔴 What these are actually protecting. Up to this work nothing in the app ever played a
    /// clip: every animal, rigged or not, was moved by bending its mesh in a shader. The user's
    /// verdict from a real iPhone was "สัตว์ขยับตัวไม่สมจริง … ของเดิมบนเว็บดีกว่ามาก", and the
    /// web's advantage is not a better wave — it is that the web plays the animation the model
    /// was authored with. So the thing worth pinning is not "clips play" (only a device can show
    /// that) but that the NUMBERS and the CHOICES match builder.html line for line:
    ///
    ///   • the playback rate formula in all four of its regimes (:2445-2448),
    ///   • that SlowAnim lands inside 0.32-0.85 and cannot escape it,
    ///   • that a coasting glider drops to 30 %, floored at 0.15,
    ///   • that animMul is applied AFTER the clamp, which is the only way the humpback's 0.72
    ///     can take it below the 0.9 floor it exists to break,
    ///   • that clip choice falls back by NAME in the web's order and never off the end,
    ///   • and that clip and wave are mutually exclusive — the one rule whose violation is
    ///     invisible in code review and unmistakable on screen (a tail folding back on itself).
    ///
    /// The clip names quoted below are not invented: <c>msh_turtle_xr0.glb</c> and
    /// <c>glb_turtle_loggerhead_xr0.glb</c> on the CDN both ship exactly
    /// <c>glide, cruise, burst, turn, patrol, accent</c>.
    /// </summary>
    public class ClipPlayTests
    {
        private const double Eps = 1e-9;

        /// <summary>The six clips the XR turtles actually carry, in the GLB's own order.</summary>
        private static readonly string[] TurtleClips =
            { "glide", "cruise", "burst", "turn", "patrol", "accent" };

        // ── Playback rate: builder.html:2445-2448, transcribed ────────────────────

        /// <summary>
        /// The ordinary branch: <c>max(0.9, min(3.0, eff*0.45))</c>. Checked in all three regimes
        /// (floored / linear / capped) because a formula that is only ever sampled in its middle
        /// is a formula whose clamps are decoration.
        /// </summary>
        [Test]
        public void TimeScale_Ordinary_MatchesTheWebFormula()
        {
            // eff 0.35 (SwimStyle's floor) × 0.45 = 0.1575 → floored at 0.9
            Assert.AreEqual(0.9, ClipPlay.TimeScale(0.35, false, 1.0, false), Eps);
            // eff 1.0 (cruise) × 0.45 = 0.45 → still floored. The web really is like this: an
            // animal at cruise plays its clip at the floor, and speed only starts to matter at 2×.
            Assert.AreEqual(0.9, ClipPlay.TimeScale(1.0, false, 1.0, false), Eps);
            // eff 4.0 × 0.45 = 1.8 → linear region, untouched by either clamp
            Assert.AreEqual(1.8, ClipPlay.TimeScale(4.0, false, 1.0, false), Eps);
            // eff 10 × 0.45 = 4.5 → capped at 3.0
            Assert.AreEqual(3.0, ClipPlay.TimeScale(10.0, false, 1.0, false), Eps);
        }

        /// <summary>
        /// The whale shark's branch: <c>max(0.32, min(0.85, eff*0.30))</c>. The band matters more
        /// than any single value in it — "หางสะบัดช้า กว้าง สง่า" is a statement about a ceiling.
        /// </summary>
        [Test]
        public void TimeScale_SlowAnim_StaysInsideItsOwnBand()
        {
            Assert.AreEqual(ClipPlay.SlowRateMin, ClipPlay.TimeScale(0.0, true, 1.0, false), Eps);
            Assert.AreEqual(0.32, ClipPlay.TimeScale(0.35, true, 1.0, false), Eps); // 0.105 → floor
            Assert.AreEqual(0.60, ClipPlay.TimeScale(2.0, true, 1.0, false), Eps);  // linear
            Assert.AreEqual(0.85, ClipPlay.TimeScale(9.0, true, 1.0, false), Eps);  // 2.7 → cap

            // …and nothing in the effort range this app can produce escapes the band.
            for (double eff = 0.0; eff <= 3.0; eff += 0.05)
            {
                double ts = ClipPlay.TimeScale(eff, true, 1.0, false);
                Assert.GreaterOrEqual(ts, ClipPlay.SlowRateMin - Eps, "eff=" + eff);
                Assert.LessOrEqual(ts, ClipPlay.SlowRateMax + Eps, "eff=" + eff);
            }
        }

        /// <summary>
        /// A SlowAnim animal is always slower than an ordinary one at the same effort. This is the
        /// whole visible point of the flag, and it survives only because the two branches never
        /// share a clamp.
        /// </summary>
        [Test]
        public void TimeScale_SlowAnim_IsAlwaysSlowerThanOrdinary()
        {
            for (double eff = 0.0; eff <= 4.0; eff += 0.1)
                Assert.Less(ClipPlay.TimeScale(eff, true, 1.0, false),
                            ClipPlay.TimeScale(eff, false, 1.0, false),
                            "eff=" + eff);
        }

        /// <summary>Gliding is ×0.3 with a 0.15 floor (:2448), applied last.</summary>
        [Test]
        public void TimeScale_Gliding_IsThirtyPercentWithAFloor()
        {
            double cruising = ClipPlay.TimeScale(4.0, false, 1.0, false);   // 1.8
            Assert.AreEqual(cruising * 0.3, ClipPlay.TimeScale(4.0, false, 1.0, true), Eps);

            // The floor bites exactly where it should: the whale shark's own band bottoms out at
            // 0.32, and 0.32 × 0.3 = 0.096 — below 0.15, so a coasting whale shark's rig keeps
            // ticking instead of freezing. A frozen rig IS the "แช่แข็ง" report.
            Assert.AreEqual(ClipPlay.GlideFloor, ClipPlay.TimeScale(0.35, true, 1.0, true), Eps);

            // …and it never speeds anything up.
            for (double eff = 0.0; eff <= 4.0; eff += 0.1)
                Assert.LessOrEqual(ClipPlay.TimeScale(eff, false, 1.0, true),
                                   ClipPlay.TimeScale(eff, false, 1.0, false) + Eps,
                                   "eff=" + eff);
        }

        /// <summary>
        /// animMul multiplies AFTER the clamp (:2447). The humpback's 0.72 exists precisely to go
        /// under the 0.9 floor; clamping after it would erase the only species-level tuning the
        /// web has for clip speed.
        /// </summary>
        [Test]
        public void TimeScale_AnimMul_AppliesAfterTheClampAndCanGoUnderTheFloor()
        {
            Assert.AreEqual(0.9 * 0.72, ClipPlay.TimeScale(1.0, false, 0.72, false), Eps);
            Assert.Less(ClipPlay.TimeScale(1.0, false, 0.72, false), ClipPlay.RateMin);

            // A missing / zero / negative multiplier is 1, never 0 — a species without a tuned row
            // must not have its rig stopped by the absence of one.
            Assert.AreEqual(0.9, ClipPlay.TimeScale(1.0, false, 0.0, false), Eps);
            Assert.AreEqual(0.9, ClipPlay.TimeScale(1.0, false, -3.0, false), Eps);
        }

        /// <summary>A negative or absurd effort cannot produce a negative (reversed) clip.</summary>
        [Test]
        public void TimeScale_IsNeverNegative()
        {
            Assert.Greater(ClipPlay.TimeScale(-5.0, false, 1.0, false), 0.0);
            Assert.Greater(ClipPlay.TimeScale(-5.0, true, 1.0, true), 0.0);
            Assert.AreEqual(ClipPlay.RateMin, ClipPlay.TimeScale(-5.0, false, 1.0, false), Eps);
        }

        /// <summary>
        /// The species table drives the flags, not a second list kept in this file. Whale shark =
        /// SlowAnim (:1779), humpback = animMul 0.72 (:1800), an ordinary turtle = neither.
        /// </summary>
        [Test]
        public void SpeciesFlags_ComeFromTheWebsOwnTable()
        {
            Assert.IsTrue(ClipPlay.SlowAnimFor("msh:whaleshark"));
            Assert.IsFalse(ClipPlay.SlowAnimFor("msh:turtle"));

            Assert.AreEqual(0.72, ClipPlay.AnimMulFor("msh:humpback_whale"), Eps);
            Assert.AreEqual(1.0, ClipPlay.AnimMulFor("msh:turtle"), Eps);
            Assert.AreEqual(1.0, ClipPlay.AnimMulFor("nothing:at_all"), Eps);
        }

        /// <summary>
        /// …and it reaches the table through the SAME id-stripping rule SwimStyle uses. A hero
        /// animal is wired up from a pivot called <c>Item_7_msh:whaleshark</c>; miss the prefix and
        /// the whale shark silently plays at an ordinary shark's rate, which is the exact class of
        /// bug this project has already paid for once.
        /// </summary>
        [Test]
        public void SpeciesFlags_SurviveThePrefixedPivotName()
        {
            Assert.IsTrue(ClipPlay.SlowAnimFor("Item_7_msh:whaleshark"));
            Assert.IsTrue(ClipPlay.IsGlider("Item_7_msh:whaleshark"));
            // An id whose own name contains '_' must not be cut at the wrong underscore.
            Assert.AreEqual(SpeciesBehavior.For("msh:humpback_whale").AnimMul,
                            ClipPlay.AnimMulFor("Item_12_msh:humpback_whale"), Eps);
        }

        // ── Who is allowed to coast ───────────────────────────────────────────────

        /// <summary>
        /// <c>(big || neverRest || manta)</c> — :2448. A reef fish beats its tail continuously; a
        /// shark, a ray and a whale coast between strokes, and that pause is most of what makes
        /// them read as big.
        /// </summary>
        [Test]
        public void IsGlider_MatchesTheWebsThreeFlags()
        {
            Assert.IsTrue(ClipPlay.IsGlider("msh:whaleshark"));   // Big + NeverRest + SlowAnim
            Assert.IsTrue(ClipPlay.IsGlider("msh:oceanic_manta")); // Big + Manta
            Assert.IsFalse(ClipPlay.IsGlider("msh:lionfish"));
            Assert.IsFalse(ClipPlay.IsGlider("nothing:at_all"));
        }

        /// <summary>
        /// Coasting is "well under its own cruise speed", and only for a glider. An animal that is
        /// not a glider never glides however slowly it moves.
        /// </summary>
        [Test]
        public void Gliding_NeedsBothTheFlagAndTheSlowSpeed()
        {
            Assert.IsTrue(ClipPlay.Gliding(0.2, 1.0, true));
            Assert.IsTrue(ClipPlay.Gliding(0.5, 1.0, true));    // exactly at the ratio
            Assert.IsFalse(ClipPlay.Gliding(0.9, 1.0, true));
            Assert.IsFalse(ClipPlay.Gliding(0.0, 1.0, false));  // not a glider — never
            // A zero/garbage cruise must not make everything a glider by dividing by nothing.
            Assert.IsFalse(ClipPlay.Gliding(5.0, 0.0, true));
        }

        // ── Which clip ────────────────────────────────────────────────────────────

        /// <summary>
        /// On the real turtle clip set every role resolves to the clip a human would pick.
        /// </summary>
        [Test]
        public void IndexOf_ResolvesEveryRoleOnTheRealTurtleClipSet()
        {
            Assert.AreEqual("cruise", TurtleClips[ClipPlay.IndexOf(TurtleClips, ClipRole.Cruise)]);
            Assert.AreEqual("glide", TurtleClips[ClipPlay.IndexOf(TurtleClips, ClipRole.Glide)]);
            Assert.AreEqual("burst", TurtleClips[ClipPlay.IndexOf(TurtleClips, ClipRole.Fast)]);
            Assert.AreEqual("turn", TurtleClips[ClipPlay.IndexOf(TurtleClips, ClipRole.Turn)]);
        }

        /// <summary>
        /// The fallback chain, in the order WO-F3 specified and the web implies:
        /// the role's own names → <c>AAction</c> (Blender's default take) → clip 0.
        /// </summary>
        [Test]
        public void IndexOf_FallsBackByNameThenToAActionThenToClipZero()
        {
            // no 'cruise' → the web's second choice for that role
            Assert.AreEqual("glide",
                new[] { "swim", "glide" }[ClipPlay.IndexOf(new[] { "swim", "glide" }, ClipRole.Cruise)]);

            // neither → Blender's default take, even though it is not first in the file
            string[] blender = { "Take 001", "AAction" };
            Assert.AreEqual("AAction", blender[ClipPlay.IndexOf(blender, ClipRole.Cruise)]);

            // nothing recognisable at all → clip 0, never a miss
            string[] mystery = { "Take 001", "Take 002" };
            Assert.AreEqual(0, ClipPlay.IndexOf(mystery, ClipRole.Cruise));
            Assert.AreEqual(0, ClipPlay.IndexOf(mystery, ClipRole.Fast));
        }

        /// <summary>Case is authored, not parsed: <c>Cruise</c> and <c>cruise</c> are one clip.</summary>
        [Test]
        public void IndexOf_IgnoresCase()
        {
            string[] names = { "Glide", "CRUISE" };
            Assert.AreEqual(1, ClipPlay.IndexOf(names, ClipRole.Cruise));
        }

        /// <summary>No clips = no index. The caller must be able to tell "nothing to play" apart
        /// from "play clip 0", or an empty model silently plays a clip that does not exist.</summary>
        [Test]
        public void IndexOf_ReturnsMinusOneWhenThereIsNothingToPlay()
        {
            Assert.AreEqual(-1, ClipPlay.IndexOf(null, ClipRole.Cruise));
            Assert.AreEqual(-1, ClipPlay.IndexOf(new string[0], ClipRole.Cruise));
        }

        /// <summary>A null entry in the name list must not throw or be matched.</summary>
        [Test]
        public void IndexOf_SurvivesANullName()
        {
            string[] names = { null, "cruise" };
            Assert.AreEqual(1, ClipPlay.IndexOf(names, ClipRole.Cruise));
        }

        // ── Clip or wave, never both ──────────────────────────────────────────────

        /// <summary>
        /// The rule that keeps a rigged animal from being bent twice. A model with clips plays
        /// them and the wave shader is never applied; a model without falls back to exactly what
        /// the app did before WO-F3.
        /// </summary>
        [Test]
        public void Motion_ClipsWinAndTheWaveIsTheFallback()
        {
            Assert.AreEqual(BodyMotion.Clip, ClipPlay.Motion(6, false));
            Assert.AreEqual(BodyMotion.Wave, ClipPlay.Motion(0, false));
            Assert.AreEqual("-", ClipPlay.MotionReason(6, false));
            Assert.AreEqual("no-clips", ClipPlay.MotionReason(0, false));
        }

        /// <summary>
        /// 🔴 A SHOAL always waves, however many clips its GLB carries. A school is drawn with
        /// Graphics.RenderMeshInstanced from one shared mesh — there is no GameObject per fish to
        /// hang an Animation component on, and a SkinnedMeshRenderer cannot be instanced at all.
        /// This is the one case where "the file has clips" is the wrong question.
        /// </summary>
        [Test]
        public void Motion_AnInstancedSchoolAlwaysWaves()
        {
            Assert.AreEqual(BodyMotion.Wave, ClipPlay.Motion(6, true));
            Assert.AreEqual("instanced-school", ClipPlay.MotionReason(6, true));
        }

        /// <summary>Every reason is a single greppable token — the log line is parsed by eye in a
        /// CI step, and a reason with a space in it wraps and stops being greppable.</summary>
        [Test]
        public void MotionReason_IsAlwaysOneToken()
        {
            foreach (string r in new[]
                     {
                         ClipPlay.MotionReason(6, false),
                         ClipPlay.MotionReason(0, false),
                         ClipPlay.MotionReason(6, true),
                     })
            {
                Assert.IsFalse(string.IsNullOrEmpty(r));
                Assert.IsFalse(r.Contains(" "), "reason has a space: " + r);
            }
        }

        // ── The constants themselves ──────────────────────────────────────────────

        /// <summary>
        /// The four rate constants are transcribed from builder.html:2445-2446 and are the entire
        /// content of "แบบเดียวกับเว็บ". Pinned by value so a future tweak has to be a decision.
        /// </summary>
        [Test]
        public void Constants_AreTheWebsOwnNumbers()
        {
            Assert.AreEqual(0.45, ClipPlay.RateK, Eps);
            Assert.AreEqual(0.9, ClipPlay.RateMin, Eps);
            Assert.AreEqual(3.0, ClipPlay.RateMax, Eps);
            Assert.AreEqual(0.30, ClipPlay.SlowRateK, Eps);
            Assert.AreEqual(0.32, ClipPlay.SlowRateMin, Eps);
            Assert.AreEqual(0.85, ClipPlay.SlowRateMax, Eps);
            Assert.AreEqual(0.3, ClipPlay.GlideMul, Eps);
            Assert.AreEqual(0.15, ClipPlay.GlideFloor, Eps);
        }

        // ── The CI oracle (QcAnimShot) ────────────────────────────────────────────

        /// <summary>The clip names on the three rigged models now on the CDN, read out of the GLB
        /// headers themselves. They are what the picker below is actually graded on.</summary>
        private static readonly string[] WhalesharkClips =
            { "glide", "cruise", "burst", "turn", "patrol", "accent" };
        private static readonly string[] BullSharkClips = { "AAction" };
        private static readonly string[] MantaClips =
            { "glide", "cruise", "strong", "barrelroll", "hover", "banking" };

        /// <summary>
        /// 🔴 The three rigged models cover three different shapes of clip set, and the picker has
        /// to survive all three. The manta is the one that would have gone unnoticed: it has six
        /// clips, none of them called <c>burst</c> or <c>turn</c>, so a picker that only knew the
        /// turtle's vocabulary would land on clip 0 for every role and still look plausible in a
        /// log. It resolves through the web's SECOND choices (<c>strong</c>, <c>banking</c>) —
        /// which are in the table only because they were transcribed from builder.html:1439-1440
        /// rather than invented from the turtle.
        /// </summary>
        [Test]
        public void IndexOf_HandlesAllThreeRiggedModelsOnTheCdn()
        {
            // whaleshark — the web's own vocabulary, every role distinct
            Assert.AreEqual("cruise", WhalesharkClips[ClipPlay.IndexOf(WhalesharkClips, ClipRole.Cruise)]);
            Assert.AreEqual("burst", WhalesharkClips[ClipPlay.IndexOf(WhalesharkClips, ClipRole.Fast)]);
            Assert.AreEqual("glide", WhalesharkClips[ClipPlay.IndexOf(WhalesharkClips, ClipRole.Glide)]);

            // bull shark — one clip, Blender's un-renamed default take. Every role must land on it
            // and none may miss: a single-clip model is the commonest thing the pipeline produces.
            foreach (ClipRole role in new[] { ClipRole.Cruise, ClipRole.Glide, ClipRole.Fast, ClipRole.Turn })
                Assert.AreEqual("AAction", BullSharkClips[ClipPlay.IndexOf(BullSharkClips, role)],
                                "role=" + role);

            // manta — six clips under names the turtle does not use
            Assert.AreEqual("cruise", MantaClips[ClipPlay.IndexOf(MantaClips, ClipRole.Cruise)]);
            Assert.AreEqual("glide", MantaClips[ClipPlay.IndexOf(MantaClips, ClipRole.Glide)]);
            Assert.AreEqual("strong", MantaClips[ClipPlay.IndexOf(MantaClips, ClipRole.Fast)]);
            Assert.AreEqual("banking", MantaClips[ClipPlay.IndexOf(MantaClips, ClipRole.Turn)]);
        }

        /// <summary>
        /// The whaleshark rig is the only file that exercises the SlowAnim band end to end, so the
        /// rate the QC pass will print for it is pinned here: whatever else changes, that model
        /// must not come back playing at the ordinary 0.9.
        /// </summary>
        [Test]
        public void RiggedWhaleshark_PlaysInTheSlowBandNotTheOrdinaryOne()
        {
            double ts = ClipPlay.TimeScale(1.0, ClipPlay.SlowAnimFor("msh:whaleshark"),
                                           ClipPlay.AnimMulFor("msh:whaleshark"), false);
            Assert.AreEqual(ClipPlay.SlowRateMin, ts, Eps);
            Assert.Less(ts, ClipPlay.RateMin);
        }

        /// <summary>
        /// The verdict names the FIRST thing wrong, in the order a human would fix them, and the
        /// order is the whole value: a model with no clips also has no bones and no motion, and a
        /// verdict of "frozen-live" on it would send the wrong person looking.
        /// </summary>
        [Test]
        public void Verdict_NamesTheFirstThingWrong()
        {
            // nothing shipped
            Assert.AreEqual("no-clips",
                ClipPlay.Verdict(new ClipPlay.ClipProbe(0, 0, 0, 0, 0, 6)));
            // clips arrived, the skin did not (Draco / import)
            Assert.AreEqual("no-skin",
                ClipPlay.Verdict(new ClipPlay.ClipProbe(6, 0, 0, 0, 0, 6)));
            // curves exist and move nothing — rig transfer aimed at joints that are not these
            Assert.AreEqual("frozen-pose",
                ClipPlay.Verdict(new ClipPlay.ClipProbe(6, 5, 0.0, 0.0, 0.0, 6)));
            // the FILE is fine and the APP is not ticking it. This is the one that used to be
            // invisible, and it is the one that describes this app before WO-F3.
            Assert.AreEqual("frozen-live",
                ClipPlay.Verdict(new ClipPlay.ClipProbe(6, 5, 0.42, 0.0, 0.0, 6)));
            // it works
            Assert.AreEqual("moving",
                ClipPlay.Verdict(new ClipPlay.ClipProbe(6, 5, 0.42, 0.19, 1.8, 6)));
        }

        /// <summary>Only <c>moving</c> passes. A pass function that accepts anything else is how a
        /// green run stops meaning anything.</summary>
        [Test]
        public void Passes_OnlyAcceptsMoving()
        {
            Assert.IsTrue(ClipPlay.Passes(new ClipPlay.ClipProbe(6, 5, 0.42, 0.19, 1.8, 6)));
            Assert.IsFalse(ClipPlay.Passes(new ClipPlay.ClipProbe(6, 5, 0.42, 0.0, 0.0, 6)));
            Assert.IsFalse(ClipPlay.Passes(new ClipPlay.ClipProbe(0, 0, 0, 0, 0, 6)));
        }

        /// <summary>
        /// The gate is "not exactly zero", not a quality bar. A bone that moved 1e-9 is float noise
        /// off a flat curve; one that moved 0.05 on a 1.9-unit model is a real stroke. Both sides
        /// of that line are asserted so nobody later "tightens" it into rejecting real motion.
        /// </summary>
        [Test]
        public void Verdict_ThresholdSeparatesNoiseFromARealStroke()
        {
            Assert.AreEqual("frozen-pose",
                ClipPlay.Verdict(new ClipPlay.ClipProbe(6, 5, 1e-9, 1e-9, 2.0, 6)));
            Assert.AreEqual("moving",
                ClipPlay.Verdict(new ClipPlay.ClipProbe(6, 5, 0.05, 0.05, 2.0, 6)));
            Assert.Less(ClipPlay.MinPoseDelta, 0.001,
                        "the gate must stay far below any real bone movement");
        }

        /// <summary>
        /// The log line is the ONLY evidence this pass produces, so it is worth a test. Every field
        /// a reader needs must be on it, and the whole thing must stay greppable — one line, no
        /// spaces inside a value.
        /// </summary>
        [Test]
        public void ProbeLine_CarriesEveryFieldAndStaysGreppable()
        {
            string line = ClipPlay.ProbeLine(
                "msh:whaleshark", WhalesharkClips, ClipRole.Cruise, "cruise", 2.0, 0.32, "-",
                new ClipPlay.ClipProbe(6, 5, 0.42, 0.19, 1.8, 6));

            StringAssert.StartsWith("[Anim] qc ", line);
            StringAssert.Contains("asset=msh:whaleshark", line);
            StringAssert.Contains("clips=6", line);
            StringAssert.Contains("pick=cruise", line);
            StringAssert.Contains("bones=5", line);
            StringAssert.Contains("timeScale=0.32", line);
            StringAssert.Contains("mode=clip", line);
            StringAssert.Contains("verdict=moving", line);
            Assert.IsFalse(line.Contains("\n"), "the line must stay one line");
        }

        /// <summary>A model that shipped nothing still produces a complete, readable line rather
        /// than a hole in the log — an absent line and a failing line look the same in CI.</summary>
        [Test]
        public void ProbeLine_SurvivesAModelThatShippedNothing()
        {
            string line = ClipPlay.ProbeLine(
                null, null, ClipRole.Cruise, null, 0, 0, "no-animation-component",
                new ClipPlay.ClipProbe(0, 0, 0, 0, 0, 6));

            StringAssert.Contains("clips=0", line);
            StringAssert.Contains("names=-", line);
            StringAssert.Contains("pick=-", line);
            StringAssert.Contains("mode=wave", line);
            StringAssert.Contains("reason=no-animation-component", line);
            StringAssert.Contains("verdict=no-clips", line);
        }

        /// <summary>
        /// The effort that feeds TimeScale is SwimStyle's — one number, two consumers. If those
        /// two ever drift apart, a rigged turtle and an un-rigged one stop slowing down at the
        /// same moment and the reef reads as two different reefs.
        /// </summary>
        [Test]
        public void TimeScale_IsFedByTheSameEffortTheWaveUses()
        {
            double atCruise = SwimStyle.Effort(1.0, 1.0);
            Assert.AreEqual(1.0, atCruise, 1e-12);
            Assert.AreEqual(ClipPlay.TimeScale(atCruise, false, 1.0, false),
                            ClipPlay.TimeScale(1.0, false, 1.0, false), Eps);

            // A stopped animal still turns its rig over: SwimStyle floors effort at 0.35, and the
            // rate formula floors again at 0.9. Neither floor is allowed to be zero.
            Assert.Greater(ClipPlay.TimeScale(SwimStyle.Effort(0.0, 1.0), false, 1.0, false), 0.0);
            Assert.Greater(ClipPlay.TimeScale(SwimStyle.Effort(0.0, 1.0), true, 1.0, true), 0.0);
        }

        // ── The gate: com.unity.modules.animation switches glTFast ON FOR EVERYTHING ──────

        /// <summary>
        /// 🔴 The regression this gate exists to prevent, stated as a test. Putting
        /// com.unity.modules.animation in the manifest makes glTFast attach an Animation component
        /// — with playAutomatically already true — to EVERY GLB that ships a clip, not just to the
        /// animals. The statues were only just stopped from moving; a stray take left in one of
        /// them by the rig-transfer pass would start them again, and it would look like a physics
        /// bug rather than an import setting.
        /// </summary>
        [Test]
        public void OnlySoloAnimals_MayKeepTheirRig()
        {
            // The one case that keeps it: a solo animal, which has a WhaleController to drive it.
            Assert.IsTrue(ClipPlay.MayAnimate("msh:whaleshark", MarineRouting.KindMarineLife));
            Assert.IsTrue(ClipPlay.MayAnimate("mdl:bull_shark", MarineRouting.KindFish));
            Assert.IsTrue(ClipPlay.MayAnimate("msh:turtle", MarineRouting.KindTurtle));
            Assert.AreEqual("solo", ClipPlay.GateReason("msh:whaleshark", MarineRouting.KindMarineLife));

            // 🔴 Statues, wrecks, coral, rock — nothing here moves, whatever the file contains.
            Assert.IsFalse(ClipPlay.MayAnimate("cc0:statue_buddha", "SPECIAL"));
            Assert.IsFalse(ClipPlay.MayAnimate("cc0:wreck_htms", "WRECK"));
            Assert.IsFalse(ClipPlay.MayAnimate("cc0:coral_fan", "CORAL"));
            Assert.IsFalse(ClipPlay.MayAnimate("cc0:rock", null));
            Assert.AreEqual("not-an-animal", ClipPlay.GateReason("cc0:statue_buddha", "SPECIAL"));

            // A statue whose manifest entry is missing or unknown must fail CLOSED.
            Assert.IsFalse(ClipPlay.MayAnimate("cc0:statue_buddha", ""));
            Assert.IsFalse(ClipPlay.MayAnimate("something:new", "SOME_KIND_THIS_BUILD_HAS_NEVER_SEEN"));

            // Shoals and pods: instanced off one shared mesh, so the skin is gone and the clip
            // would drive a template nobody draws. They keep the wave shader, like the web.
            Assert.IsFalse(ClipPlay.MayAnimate("school:barracuda", MarineRouting.KindSchool));
            Assert.IsFalse(ClipPlay.MayAnimate("pod:humpback", MarineRouting.KindSchool));
            Assert.AreEqual("instanced-school", ClipPlay.GateReason("school:barracuda", MarineRouting.KindSchool));
            // …even when the manifest calls a shoal MARINE_LIFE — the id prefix wins in
            // MarineRouting.For, and the gate must inherit that rather than re-decide it.
            Assert.IsFalse(ClipPlay.MayAnimate("school:scad", MarineRouting.KindMarineLife));

            // A warp gate is built from primitives and is in no manifest.
            Assert.IsFalse(ClipPlay.MayAnimate("warp:portal", MarineRouting.KindMarineLife));
        }

        /// <summary>
        /// The gate answers the same question <see cref="MarineRouting.For"/> already answers, and
        /// must never grow a second opinion: a species that starts being routed differently has to
        /// move both behaviours together or it gets a brain with a frozen body, or a rig with
        /// nothing driving it.
        /// </summary>
        [Test]
        public void TheGate_IsExactlyTheSoloRoute_NotASecondOpinion()
        {
            string[] ids = { "msh:whaleshark", "school:barracuda", "pod:humpback", "warp:portal",
                             "cc0:statue_buddha", "mdl:bull_shark", "", null };
            string[] kinds = { MarineRouting.KindMarineLife, MarineRouting.KindSchool,
                               MarineRouting.KindFish, MarineRouting.KindTurtle, "WRECK", "", null };

            foreach (string id in ids)
                foreach (string kind in kinds)
                    Assert.AreEqual(MarineRouting.For(id, kind) == MarineRoute.Solo,
                                    ClipPlay.MayAnimate(id, kind),
                                    $"gate disagreed with routing for id='{id}' kind='{kind}'");
        }

        // ── The whale shark's floor inside the SlowAnim band (2026-08-06, build 280) ──
        //
        // user, on a real iPhone: "ฉลามวาฬโบกหางน้อยไป".
        // The mechanism is in the block above ClipPlay.WhalesharkSlowFloor: the animal was pinned
        // to SlowRateMin and never saw the rest of its own band.

        /// <summary>What build 280 logged: <c>timeScale=0.32 slowAnim=True</c>.</summary>
        private const double Build280WhalesharkTimeScale = 0.32;

        /// <summary>
        /// 🔴 The bug, restated as a test so it cannot come back: at cruise the whale shark sat on
        /// the FLOOR of its band, not somewhere chosen inside it. <see cref="SwimStyle.Effort"/>
        /// returns 1.0 at cruise and <c>1.0 × 0.30 = 0.30</c> is below 0.32, so the clamp — not a
        /// tuning decision — picked the number the user was looking at.
        /// </summary>
        [Test]
        public void Build280sWhaleSharkWasPinnedToTheBandFloor()
        {
            Assert.AreEqual(Build280WhalesharkTimeScale,
                            ClipPlay.TimeScale(1.0, true, 1.0, false, ClipPlay.SlowRateMin), 1e-9);
            Assert.Less(1.0 * ClipPlay.SlowRateK, ClipPlay.SlowRateMin,
                        "cruise effort lands below the floor — which is why it was pinned");

            // 🔎 The band does open above cruise — it takes effort 1.07 to leave the floor, not the
            // 3.5 a first reading of `eff*0.30` suggests. What pins this particular animal there is
            // the OTHER half of its row: msh:whaleshark is Big + NeverRest, so ClipPlay.IsGlider
            // is true for it, and every time it drops under half cruise speed the glide cut takes
            // the rate DOWN again. Across everything a patrolling whale shark actually does, the
            // floor is not a floor it occasionally touches — it is the ceiling of its normal life.
            Assert.IsTrue(ClipPlay.IsGlider("msh:whaleshark"));
            foreach (double eff in new[] { 0.35, 0.6, 1.0 })
            {
                Assert.LessOrEqual(ClipPlay.TimeScale(eff, true, 1.0, false, ClipPlay.SlowRateMin),
                                   Build280WhalesharkTimeScale, $"cruising at effort {eff}");
                Assert.Less(ClipPlay.TimeScale(eff, true, 1.0, true, ClipPlay.SlowRateMin),
                            Build280WhalesharkTimeScale * 0.6, $"coasting at effort {eff}");
            }
        }

        /// <summary>
        /// The fix, and the size of it: the whale shark now cruises at 0.55 instead of 0.32 —
        /// 1.72× — while every other animal in the sea is untouched.
        /// </summary>
        [Test]
        public void TheWhaleSharkCruisesFasterAndNobodyElseMoves()
        {
            double now = ClipPlay.TimeScale(1.0, true, 1.0, false,
                                            ClipPlay.SlowFloorFor("msh:whaleshark"));
            Assert.AreEqual(ClipPlay.WhalesharkSlowFloor, now, 1e-9);
            Assert.Greater(now, Build280WhalesharkTimeScale * 1.6, "โบกหางน้อยไป — visibly more");

            foreach (string id in new[] { "msh:humpback_whale", "msh:barracuda", "msh:turtle",
                                          "msh:oceanic_manta", "mdl:bull_shark", "pod:humpback",
                                          "school:scad", "", null })
                Assert.AreEqual(ClipPlay.SlowRateMin, ClipPlay.SlowFloorFor(id),
                                $"'{id}' must keep the ordinary floor");
        }

        /// <summary>
        /// The ceiling is still the web's. A per-species floor may say where inside the band an
        /// animal sits; it may not raise the limit builder.html:2445 put on a slow animal, and a
        /// nonsense floor must be clamped rather than believed.
        /// </summary>
        [Test]
        public void ThePerSpeciesFloorCannotEscapeTheWebsBand()
        {
            foreach (double floor in new[] { -1.0, 0.0, 0.1, 0.55, 0.85, 5.0, double.NaN })
            {
                double r = ClipPlay.TimeScale(1.0, true, 1.0, false, floor);
                Assert.That(r, Is.InRange(ClipPlay.SlowRateMin, ClipPlay.SlowRateMax),
                            $"floor {floor} escaped [0.32, 0.85]");
            }

            // Effort still reaches: 0.55 is not the top of the band, so a sprinting whale shark
            // still plays faster than a cruising one. A knob that killed the effort term would
            // have replaced one frozen number with another.
            Assert.Greater(ClipPlay.TimeScale(SwimStyle.EffortMax, true, 1.0, false,
                                              ClipPlay.WhalesharkSlowFloor),
                           ClipPlay.TimeScale(1.0, true, 1.0, false, ClipPlay.WhalesharkSlowFloor));
            Assert.Less(ClipPlay.WhalesharkSlowFloor, ClipPlay.SlowRateMax, "headroom must remain");
        }

        /// <summary>
        /// The four-argument overload is exactly the five-argument one at the default floor, so
        /// every existing caller and every existing test keeps its answer to the last bit.
        /// </summary>
        [Test]
        public void TheOldOverloadIsTheNewOneAtTheDefaultFloor()
        {
            foreach (double eff in new[] { 0.0, 0.35, 1.0, 2.0, 3.5 })
                foreach (bool slow in new[] { true, false })
                    foreach (bool glide in new[] { true, false })
                        Assert.AreEqual(ClipPlay.TimeScale(eff, slow, 1.0, glide),
                                        ClipPlay.TimeScale(eff, slow, 1.0, glide, ClipPlay.SlowRateMin),
                                        1e-12, $"eff={eff} slow={slow} glide={glide}");
        }

        /// <summary>
        /// A coasting whale shark still slows down — the glide cut (builder.html:2448) applies
        /// after the floor, not instead of it — but it no longer bottoms out on the hard 0.15
        /// floor, which is what "แช่แข็ง" looks like.
        /// </summary>
        [Test]
        public void GlidingStillCutsTheRate_ButNoLongerToTheFreezeFloor()
        {
            double glide = ClipPlay.TimeScale(1.0, true, 1.0, true, ClipPlay.WhalesharkSlowFloor);
            double cruise = ClipPlay.TimeScale(1.0, true, 1.0, false, ClipPlay.WhalesharkSlowFloor);

            Assert.Less(glide, cruise, "a coasting animal beats slower");
            Assert.AreEqual(ClipPlay.WhalesharkSlowFloor * ClipPlay.GlideMul, glide, 1e-9);
            Assert.Greater(glide, ClipPlay.GlideFloor,
                           "…and is no longer sitting on the freeze floor it used to be clamped to");
        }

        /// <summary>
        /// Bare-id matched, like every other table read here. The runtime hands over
        /// <c>Item_7_msh:whaleshark</c>; an exact-match miss would leave the animal at 0.32 and
        /// look precisely like the build not containing the change.
        /// </summary>
        [Test]
        public void TheFloorLookupSurvivesTheSceneItemPrefix()
        {
            Assert.AreEqual(ClipPlay.WhalesharkSlowFloor, ClipPlay.SlowFloorFor("Item_7_msh:whaleshark"));
            Assert.AreEqual(ClipPlay.WhalesharkSlowFloor, ClipPlay.SlowFloorFor("MSH:WHALESHARK"));
        }
    }
}
