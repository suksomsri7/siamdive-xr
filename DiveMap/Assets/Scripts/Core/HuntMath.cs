using System;

namespace DiveMap.Core
{
    /// <summary>What a predator is doing about the animal it can see.</summary>
    public enum HuntPhase
    {
        /// <summary>No prey in reach, or too full to care. Ordinary roaming applies.</summary>
        Idle = 0,
        /// <summary>Prey is in reach and it is turning onto it, but not yet lined up.</summary>
        Stalk = 1,
        /// <summary>Lined up (aim error under <see cref="HuntMath.AimTolerance"/>) — sprinting.</summary>
        Sprint = 2,
        /// <summary>Inside the strike radius this frame. It fed; hunger drops and it goes off to rest.</summary>
        Strike = 3,
        /// <summary>Deliberately not hunting: it has just eaten, or the chase went on too long.</summary>
        Wander = 4,
    }

    /// <summary>
    /// One predator's live hunting state. A plain mutable struct so an array of them costs one
    /// allocation for the whole reef and none per frame.
    /// </summary>
    public struct HuntDrive
    {
        /// <summary>0..1. Climbs on its own, drops by <see cref="HuntMath.FeedDrop"/> when it feeds.</summary>
        public double Hunger;
        /// <summary>0..1 anaerobic debt. A shark that has been sprinting cannot keep sprinting.</summary>
        public double Fatigue;
        /// <summary>Sim time it may start hunting again. Set after a meal and after a long chase.</summary>
        public double WanderUntil;
        /// <summary>Sim time the current pursuit began, or 0 when it is not pursuing.</summary>
        public double PursueSince;
        /// <summary>Sim time the current sprint ends.</summary>
        public double SprintUntil;
        /// <summary>The multiplier that sprint is worth.</summary>
        public double SprintMul;
        public HuntPhase Phase;
    }

    /// <summary>The outcome of one hunting step — what the caller should do with the animal.</summary>
    public readonly struct HuntStep
    {
        public readonly HuntPhase Phase;
        /// <summary>Heading (rad) it wants, in the MarineMath convention: dir = (cos h, sin h) → (x, z).</summary>
        public readonly double DesiredHeading;
        /// <summary>0..1 how hard to turn onto that heading this frame. 0 = do not steer.</summary>
        public readonly double TurnGain;
        /// <summary>Speed multiplier to apply this frame (1 = cruise).</summary>
        public readonly double SpeedMul;
        /// <summary>True on the single frame it caught something.</summary>
        public readonly bool   Fed;

        public HuntStep(HuntPhase phase, double desiredHeading, double turnGain, double speedMul, bool fed)
        {
            Phase = phase; DesiredHeading = desiredHeading; TurnGain = turnGain;
            SpeedMul = speedMul; Fed = fed;
        }
    }

    /// <summary>
    /// C6 — hunger, pursuit and the gorge-rest cycle. A port of the web's predator block
    /// (builder.html:2179-2199), its sense radii (:1946-1958), its sprint floor and fatigue
    /// (:2422-2429).
    ///
    /// 🔴 What makes this read as a hunt rather than as homing. Four things, and the reef looks
    /// mechanical if any one of them is dropped:
    ///
    ///   • **Satiation.** A predator that has just eaten stops hunting for fourteen seconds
    ///     (<see cref="GorgeRestSeconds"/>) and its hunger falls by 0.4. Without it a shark
    ///     tractor-beams onto the nearest shoal and never leaves — the single most common way a
    ///     predator system reads as broken.
    ///   • **It aims before it sprints.** The web will not burst until the aim error is under
    ///     0.85 rad (:2192). Sprinting while still turning is what a missile does; a shark lines
    ///     up, then goes.
    ///   • **A chase has a time limit.** Six seconds of pursuit without a kill and it gives up
    ///     and patrols somewhere else (:2196). Prey that can out-turn a shark forever is a
    ///     perpetual-motion machine, visible as a shark stuck orbiting a shoal.
    ///   • **Fatigue.** Burst swimming is anaerobic (:2424-2427). Past 0.85 the burst fizzles to
    ///     1.3×, so nothing sprints across the whole map.
    ///
    /// 🔴 The hunger curve is deliberately violent: <c>2.8 − 0.4·h + 17.6·h²</c> (:2192). At
    /// h = 0.15 (just fed, the floor) that is 3.1×; at h = 1.0 it is 20×. A starving shark is a
    /// different animal from a comfortable one, and that is the whole point of having a drive at
    /// all rather than a fixed chase speed. Do not "tidy" this into a lerp.
    ///
    /// Pure arithmetic on doubles — no scene, no allocation, fully testable.
    /// </summary>
    public static class HuntMath
    {
        // ── hunger ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Hunger gained per FRAME at metabolism 1.0 (web :2179 — <c>hunger + 0.0006·metab</c>).
        /// The web accrues per animation frame; <see cref="HungerPerSecond"/> is the same number
        /// restated for a dt-driven caller.
        /// </summary>
        public const double HungerPerFrame = 0.0006;

        /// <summary>
        /// The web's per-frame rate at its own 60 fps target. DERIVED, not a literal: the web ran
        /// this inside its frame loop, so porting the constant without the frame rate would make a
        /// shark on a 30 fps phone get hungry half as fast as one on a 120 Hz tablet — a behaviour
        /// that depends on the device is not a behaviour.
        /// </summary>
        public const double HungerPerSecond = HungerPerFrame * 60.0;

        /// <summary>
        /// Hunger floor while hunting (web :2187 — <c>max(0.15, hunger)</c>). A predator that has
        /// just fed still has SOME drive; this is what stops the sprint multiplier collapsing to
        /// nothing and the chase turning into a drift.
        /// </summary>
        public const double HungerFloor = 0.15;

        /// <summary>How much a meal takes off the hunger (web :2193 — <c>rawH − 0.4</c>).</summary>
        public const double FeedDrop = 0.4;

        /// <summary>Seconds a fed predator patrols instead of hunting (web :2193 — <c>t+14</c>).</summary>
        public const double GorgeRestSeconds = 14.0;

        /// <summary>Seconds a fruitless chase may last (web :2196 — <c>t − pursueSince &gt; 6</c>).</summary>
        public const double PursuitLimitSeconds = 6.0;

        /// <summary>Seconds it goes elsewhere after giving up (web :2196 — <c>t+4</c>).</summary>
        public const double GiveUpWanderSeconds = 4.0;

        /// <summary>Hunger after <paramref name="dt"/> seconds of not eating.</summary>
        public static double Hunger(double hunger, double metabolism, double dt)
        {
            double h = hunger + HungerPerSecond * (metabolism > 0.0 ? metabolism : 1.0) * (dt > 0.0 ? dt : 0.0);
            return h > 1.0 ? 1.0 : (h < 0.0 ? 0.0 : h);
        }

        // ── reach ────────────────────────────────────────────────────────────────

        /// <summary>
        /// How close prey has to be before a predator engages it at all
        /// (web :2186 — <c>obsR·6 + 45</c>).
        ///
        /// Note this is LARGER than <see cref="FleeMath.SenseRadius"/> (<c>obsR·4.5 + 28</c>),
        /// which is what it can see. The web senses on the smaller radius and then engages on the
        /// bigger one, so the engagement test never fires on something it has not sensed — the
        /// ordering is what keeps the two radii from having to agree.
        /// </summary>
        public static double PreyRadius(double obsR) => (obsR > 0.0 ? obsR : 0.0) * 6.0 + 45.0;

        /// <summary>
        /// The radius it will actually commit from. An ambusher (moray, scorpionfish, lionfish,
        /// stonefish) waits until prey is at HALF that (web :2187) — that is the difference
        /// between a lionfish and a marlin, and it is one multiplication.
        /// </summary>
        public static double EngageRadius(double preyR, bool ambush) => ambush ? preyR * 0.5 : preyR;

        /// <summary>The range it snaps into the strike (web :2193 — <c>preyR·0.55</c>).</summary>
        public static double StrikeRadius(double preyR) => preyR * 0.55;

        /// <summary>Too close to steer sensibly — the web bails out under 4 units (web :2188).</summary>
        public const double MinEngageDistance = 4.0;

        // ── the burst ────────────────────────────────────────────────────────────

        /// <summary>Aim error (rad) under which it commits to the sprint (web :2192).</summary>
        public const double AimTolerance = 0.85;

        /// <summary>Seconds one burst lasts (web :2192 — <c>t+0.6</c>).</summary>
        public const double SprintSeconds = 0.6;

        /// <summary>
        /// The sprint multiplier for a given hunger (web :2192 —
        /// <c>2.8 − 0.4·h + 17.6·h²</c>). Quadratic on purpose: see the class note.
        /// </summary>
        public static double SprintMultiplier(double hunger)
        {
            double h = hunger < HungerFloor ? HungerFloor : (hunger > 1.0 ? 1.0 : hunger);
            return 2.8 - 0.4 * h + 17.6 * h * h;
        }

        /// <summary>
        /// How hard it turns onto the prey (web :2189 — <c>(big?0.14:0.2)·(0.7+h)</c>). A big
        /// animal turns lazily however hungry it is, which is why the two factors are separate.
        /// </summary>
        public static double TurnGain(bool big, double hunger)
        {
            double h = hunger < HungerFloor ? HungerFloor : (hunger > 1.0 ? 1.0 : hunger);
            return (big ? 0.14 : 0.2) * (0.7 + h);
        }

        /// <summary>
        /// The web's baseline for a hunting predator that is not currently bursting
        /// (web :2429 — <c>sprint = max(sprint, 5.0)</c>), i.e. a shark on patrol already moves
        /// five times an ordinary fish's cruise. Ambushers are excluded: a scorpionfish that
        /// patrols at 5× is not a scorpionfish.
        /// </summary>
        public const double PatrolSprintFloor = 5.0;

        /// <summary>Fatigue gained per frame while bursting (web :2426 — <c>+0.009</c>).</summary>
        public const double FatigueGainPerFrame = 0.009;
        /// <summary>Fatigue shed per frame while cruising (web :2426 — <c>−0.005</c>).</summary>
        public const double FatigueDropPerFrame = 0.005;
        /// <summary>Above this the burst fizzles (web :2427).</summary>
        public const double FatigueExhausted = 0.85;
        /// <summary>What a burst is worth once exhausted (web :2427).</summary>
        public const double ExhaustedBurst = 1.3;
        /// <summary>A burst counts as a burst above this (web :2425 — <c>burst &gt; 1.05</c>).</summary>
        public const double BurstThreshold = 1.05;

        /// <summary>Fatigue after <paramref name="dt"/> seconds. Same 60 fps derivation as hunger.</summary>
        public static double Fatigue(double fatigue, bool bursting, double dt)
        {
            double rate = (bursting ? FatigueGainPerFrame : -FatigueDropPerFrame) * 60.0;
            double f = fatigue + rate * (dt > 0.0 ? dt : 0.0);
            return f < 0.0 ? 0.0 : (f > 1.0 ? 1.0 : f);
        }

        /// <summary>The burst it actually gets, after fatigue (web :2422-2427).</summary>
        public static double BurstAfterFatigue(double burst, double fatigue)
        {
            if (burst < 1.0) burst = 1.0;
            if (fatigue > FatigueExhausted && burst > ExhaustedBurst) return ExhaustedBurst;
            return burst;
        }

        /// <summary>
        /// Speed multiplier for a predator this frame: its patrol floor, or the live burst,
        /// whichever is larger, capped by fatigue. Ambushers get neither — they sit still.
        /// </summary>
        public static double PredatorSpeedMul(in HuntDrive d, bool ambush, double time)
        {
            double burst = (d.SprintUntil > time && d.SprintMul > 1.0) ? d.SprintMul : 1.0;
            burst = BurstAfterFatigue(burst, d.Fatigue);
            if (!ambush && burst < PatrolSprintFloor) burst = PatrolSprintFloor;
            return burst;
        }

        // ── the step ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Advance one predator's hunt. Pure: everything it knows arrives as an argument and
        /// everything it changes is in <paramref name="d"/>.
        ///
        /// <paramref name="hasPrey"/> false (nothing sensed, or something bigger is nearby and it
        /// is the one running) drops it straight to <see cref="HuntPhase.Idle"/> and clears the
        /// pursuit clock — the web's <c>else if(predator){ pursueSince = 0 }</c> at :2199.
        ///
        /// Distances are in the XZ plane, the same plane the web hunts in: a shark that dives at
        /// its prey from directly above looks like a bird.
        /// </summary>
        public static HuntStep Step(
            ref HuntDrive d,
            bool hasPrey, double dx, double dz,
            double currentHeading,
            double obsR, bool ambush, bool big,
            double metabolism, double time, double dt)
        {
            if (dt < 0.0) dt = 0.0;

            // Hunger climbs whatever it is doing (web :2179).
            d.Hunger = Hunger(d.Hunger, metabolism, dt);

            bool bursting = d.SprintUntil > time && d.SprintMul > BurstThreshold;
            d.Fatigue = Fatigue(d.Fatigue, bursting, dt);

            // Just fed, or gave up — it is out of the game for a while (web :2186 `t > wanderUntil`).
            if (time < d.WanderUntil)
            {
                d.PursueSince = 0.0;
                d.Phase = HuntPhase.Wander;
                return new HuntStep(HuntPhase.Wander, currentHeading, 0.0,
                                    PredatorSpeedMul(d, ambush, time), false);
            }

            if (!hasPrey)
            {
                d.PursueSince = 0.0;                                   // :2199
                d.Phase = HuntPhase.Idle;
                return new HuntStep(HuntPhase.Idle, currentHeading, 0.0,
                                    PredatorSpeedMul(d, ambush, time), false);
            }

            double hd = Math.Sqrt(dx * dx + dz * dz);
            double preyR = PreyRadius(obsR);
            double engageR = EngageRadius(preyR, ambush);

            if (hd >= engageR || hd <= MinEngageDistance)              // :2188 / :2197
            {
                d.PursueSince = 0.0;
                d.Phase = HuntPhase.Idle;
                return new HuntStep(HuntPhase.Idle, currentHeading, 0.0,
                                    PredatorSpeedMul(d, ambush, time), false);
            }

            double rawH = d.Hunger;
            double hgr  = rawH < HungerFloor ? HungerFloor : rawH;     // :2187
            double toPrey = Math.Atan2(dz, dx);
            double turn = TurnGain(big, hgr);                          // :2189

            // Aim error against the heading it is actually showing.
            double aimErr = Math.Abs(Math.Atan2(Math.Sin(toPrey - currentHeading),
                                                Math.Cos(toPrey - currentHeading)));

            HuntPhase phase = HuntPhase.Stalk;
            bool fed = false;

            if (aimErr < AimTolerance)                                 // :2192
            {
                d.SprintUntil = time + SprintSeconds;
                d.SprintMul   = SprintMultiplier(hgr);
                phase = HuntPhase.Sprint;

                if (hd < StrikeRadius(preyR))                          // :2193
                {
                    phase = HuntPhase.Strike;
                    fed = true;
                    d.Hunger = rawH - FeedDrop < 0.0 ? 0.0 : rawH - FeedDrop;
                    d.WanderUntil = time + GorgeRestSeconds;
                    d.PursueSince = 0.0;
                }
            }

            if (!fed)
            {
                if (d.PursueSince <= 0.0) d.PursueSince = time;        // :2195
                if (time - d.PursueSince > PursuitLimitSeconds)        // :2196
                {
                    d.WanderUntil = time + GiveUpWanderSeconds;
                    d.PursueSince = 0.0;
                    d.Phase = HuntPhase.Wander;
                    return new HuntStep(HuntPhase.Wander, currentHeading, 0.0,
                                        PredatorSpeedMul(d, ambush, time), false);
                }
            }

            d.Phase = phase;
            return new HuntStep(phase, toPrey, turn, PredatorSpeedMul(d, ambush, time), fed);
        }

        /// <summary>
        /// Should this animal be allowed to rest? A pursuit predator that is not benthic never
        /// does (web :2180) — and a benthic one (nurse shark, leopard shark) genuinely sleeps on
        /// the sand, which is why the exception is not a hack.
        /// </summary>
        public static bool MayRest(string diet, bool benthic)
            => benthic || !string.Equals(diet, SpeciesGenome.DietPredator, StringComparison.OrdinalIgnoreCase);

        /// <summary>Short label for the QC log — one token, greppable.</summary>
        public static string Label(HuntPhase p)
        {
            switch (p)
            {
                case HuntPhase.Idle:   return "Idle";
                case HuntPhase.Stalk:  return "Stalk";
                case HuntPhase.Sprint: return "Sprint";
                case HuntPhase.Strike: return "Strike";
                case HuntPhase.Wander: return "Wander";
                default:               return "?";
            }
        }
    }
}
