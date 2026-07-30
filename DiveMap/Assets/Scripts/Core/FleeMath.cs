using System;

namespace DiveMap.Core
{
    /// <summary>
    /// C5 — what a shoal does when something frightening arrives. Ported from the web's
    /// <c>schoolFlee()</c> (builder.html:1628), the panic trigger in <c>schoolStep()</c>
    /// (:1688-1702), <c>senseAgents()</c> (:1932) and <c>shelterSense()</c> (:1960).
    ///
    /// Two things are worth stating up front, because they are what make the reef feel alive
    /// rather than merely animated:
    ///
    ///   • **Panic is graded, not a switch.** It is 0..1 by distance, so a shoal reacts to a diver
    ///     forty metres out with a ripple and to one at arm's length with a burst. A boolean here
    ///     would give you fish that ignore you and then teleport.
    ///   • **Speed is the trigger, not presence.** The web only panics from the diver when
    ///     <c>camVel &gt; 11</c>. Hover next to a shoal and it accepts you; charge it and it
    ///     scatters. That single rule is most of why the web's reef reads as wildlife.
    ///
    /// The scalar formulas below are the web's, verbatim, so they can be asserted against it.
    /// How they are *applied* differs: the web eases each fish's position toward a formation slot
    /// and adds the flee offset on top, whereas this app runs velocity boids (<c>BoidsJob</c>).
    /// The steering weights that translate one into the other are marked "interpretation" and are
    /// the only numbers here that are not lifted from builder.html.
    /// </summary>
    public static class FleeMath
    {
        // ── the web's constants ───────────────────────────────────────────────────

        /// <summary>Diver speed above which a shoal treats the drone as a predator (web: camVel&gt;11).</summary>
        public const double DiverPanicSpeed = 11.0;

        /// <summary>Panic above which the shoal balls up (web: <c>S._panic&gt;0.6</c>).</summary>
        public const double BallUpPanic = 0.6;

        /// <summary>How long the bait-ball is held after the threat passes (web: <c>t+2.5</c>).</summary>
        public const double BallHoldSeconds = 2.5;

        /// <summary>Sense refresh interval for predators (web: <c>t-u.lastSense&gt;0.7</c>).</summary>
        public const double SenseIntervalSeconds = 0.7;

        /// <summary>Shelter refresh interval (web: <c>t-u.lastShelter&gt;1.2</c>).</summary>
        public const double ShelterIntervalSeconds = 1.2;

        // ── panic ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Radius at which a real predator starts to worry the shoal
        /// (web :1692 — <c>spreadR*0.8 + flen*5</c>).
        /// </summary>
        public static double PredatorPanicRadius(double spreadR, double fishLen)
            => spreadR * 0.8 + fishLen * 5.0;

        /// <summary>
        /// Radius at which a CHARGING diver does (web :1700 — <c>spreadR*0.85 + flen*6</c>).
        /// Slightly wider than a predator's: the drone is big, loud and fast.
        /// </summary>
        public static double DiverPanicRadius(double spreadR, double fishLen)
            => spreadR * 0.85 + fishLen * 6.0;

        /// <summary>
        /// Panic 0..1 from the threat's distance (web: <c>1 - pd/panicR</c>, zero outside).
        /// One at the centre, nothing at the rim.
        /// </summary>
        public static double PanicLevel(double distance, double panicRadius)
        {
            if (panicRadius <= 0.0 || distance >= panicRadius) return 0.0;
            if (distance <= 0.0) return 1.0;
            return 1.0 - distance / panicRadius;
        }

        /// <summary>Is the diver moving fast enough to frighten anything? (web: <c>camVel&gt;11</c>)</summary>
        public static bool DiverIsThreatening(double diverSpeed) => diverSpeed > DiverPanicSpeed;

        /// <summary>
        /// The shoal's panic this frame. A real predator wins over the diver — the web checks the
        /// predator first and only consults the drone when <c>!S._panic</c>.
        /// </summary>
        public static double SchoolPanic(
            double predatorDistance, bool hasPredator,
            double diverDistance, double diverSpeed, bool diverActive,
            double spreadR, double fishLen)
        {
            if (hasPredator)
            {
                double p = PanicLevel(predatorDistance, PredatorPanicRadius(spreadR, fishLen));
                if (p > 0.0) return p;
            }
            if (diverActive && DiverIsThreatening(diverSpeed))
                return PanicLevel(diverDistance, DiverPanicRadius(spreadR, fishLen));
            return 0.0;
        }

        // ── the scatter itself ───────────────────────────────────────────────────

        /// <summary>
        /// How far a fish is thrown outward from the threat
        /// (web :1631 — <c>panic*(spreadR*0.18 + flen*1.6)</c>).
        ///
        /// The comment in builder.html is explicit that this was tuned DOWN on 2026-07-09: the
        /// shoal is meant to burst apart *near* the threat, not evacuate the map. Keep it small.
        /// </summary>
        public static double FleePush(double panic, double spreadR, double fishLen)
        {
            if (panic <= 0.0) return 0.0;
            return panic * (spreadR * 0.18 + fishLen * 1.6);
        }

        /// <summary>
        /// Extra positional ease while fleeing — how *sharply* the dart happens
        /// (web :1632 — <c>min(0.22, panic*0.18)</c>).
        /// </summary>
        public static double FleeEase(double panic)
        {
            if (panic <= 0.0) return 0.0;
            double e = panic * 0.18;
            return e > 0.22 ? 0.22 : e;
        }

        /// <summary>Does this level of fear ball the shoal up? (web :1697)</summary>
        public static bool ShouldBallUp(double panic, bool isPod) => !isPod && panic > BallUpPanic;

        /// <summary>
        /// The home radius while balled up. The web reuses its tight <c>'ball'</c> formation; here
        /// the equivalent is to shrink the boids' home radius, which pulls the same shoal into the
        /// same shape without a second formation system. **Interpretation.**
        /// </summary>
        public static double BallHomeRadius(double homeR, double panic)
        {
            if (panic <= 0.0) return homeR;
            double k = 1.0 - 0.45 * Clamp01(panic);
            return homeR * k;
        }

        /// <summary>
        /// Dart speed multiplier. The web darts by raising the position ease (FleeEase); with
        /// velocity boids the same visual comes from swimming harder. Scaled so a full-panic fish
        /// is 1.6× cruise, which matches the web's burst over a couple of seconds.
        /// **Interpretation**, calibrated to <see cref="FleeEase"/>.
        /// </summary>
        public static double DartSpeedScale(double panic) => 1.0 + 0.6 * Clamp01(panic);

        /// <summary>
        /// Steering weight pushing a fish directly away from the threat. **Interpretation** — the
        /// boids equivalent of the web's radial position offset.
        /// </summary>
        public static double FleeSteerWeight(double panic, double spreadR, double fishLen)
            => FleePush(panic, spreadR, fishLen) * 0.35;

        /// <summary>
        /// How much harder a frightened fish may turn. At the cruise cap (0.045 rad/frame) a
        /// startled fish needs well over a second to come about, which reads as indifference —
        /// the one thing a startle must never look like. **Interpretation.**
        /// </summary>
        public static double TurnCapScale(double panic) => 1.0 + 2.0 * Clamp01(panic);

        // ── who counts as a threat (senseAgents) ─────────────────────────────────

        /// <summary>Perception radius of an individual animal (web :1934 — <c>obsR*4.5 + 28</c>).</summary>
        public static double SenseRadius(double obsR) => obsR * 4.5 + 28.0;

        /// <summary>
        /// Is <paramref name="otherRank"/> a threat to something of <paramref name="myRank"/>?
        ///
        /// The web's rule (:1939) has one detail that is easy to drop and very visible if you do:
        /// **filter feeders are harmless.** A whale shark or a manta must be able to cruise through
        /// a shoal of scad without it exploding, because that is exactly the shot every diver wants.
        /// Only a strictly higher rank that actually hunts counts.
        /// </summary>
        public static bool IsThreat(int myRank, int otherRank, string otherDiet)
        {
            if (otherRank <= myRank) return false;
            if (string.Equals(otherDiet, "filter", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        // ── shelter (shelterSense) ───────────────────────────────────────────────

        /// <summary>
        /// How strongly a frightened fish runs for the nearest cover. Zero when calm, so a shoal
        /// only heads for the reef when something is after it. **Interpretation** of the web's
        /// <c>u.shelter</c> bias, which feeds the same heading term.
        /// </summary>
        public static double ShelterBias(double panic) => 0.9 * Clamp01(panic);

        /// <summary>
        /// Where a shoal actually aims while fleeing: its own home, dragged toward cover in
        /// proportion to fear. Returned as a 0..1 lerp factor from home to shelter.
        /// </summary>
        public static double ShelterLerp(double panic, bool hasShelter)
            => hasShelter ? ShelterBias(panic) * 0.55 : 0.0;

        /// <summary>
        /// A fish inside the shelter's own radius has arrived — it should stop steering at it and
        /// mill, otherwise the shoal piles into the coral.
        /// </summary>
        public static bool AtShelter(double distanceToShelter, double shelterR)
            => distanceToShelter <= shelterR * 1.15;

        private static double Clamp01(double v) => v < 0.0 ? 0.0 : (v > 1.0 ? 1.0 : v);
    }
}
