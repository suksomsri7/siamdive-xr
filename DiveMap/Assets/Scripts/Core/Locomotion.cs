using System;

namespace DiveMap.Core
{
    /// <summary>
    /// C4 — how far an animal roams and how fast it swims, derived from its genome.
    ///
    /// 🔎 The PARITY row called this "locomotion จาก animation", which is backwards. The web's
    /// <c>deriveLocomotion</c> (builder.html:1915) reads ZONE, DIET, size and personality — no
    /// animation is involved. The clip goes the other way: <c>:2486</c> sets
    /// <c>mixer.timeScale = 0.18 + mv*0.16</c>, so the tail beats faster BECAUSE the fish is
    /// moving faster. Deriving speed from a clip would have inverted the whole system.
    ///
    /// The numbers below are the web's, one line each, with its reasoning kept:
    /// <code>
    ///   stationary      roam 8    swim 0.15   coral and anchored things — they go nowhere
    ///   big             roam 330  swim 0.90   whales — wide, unhurried, steady
    ///   zone pelagic    roam 300  swim 1.30   open water — patrols the whole site, fast
    ///   zone bottom     roam 70/42 swim 0.55  predator/other — territorial, slow
    ///   zone reef       roam 85   swim 0.80   reef fish — stays near home
    ///   otherwise       roam 160  swim 1.00
    ///   diet predator   roam ×1.15/1.35, swim ×1.22/1.28  (big/small)
    /// </code>
    ///
    /// ⚠️ Two things here were ported wrong the first time and only CI caught them: the predator
    /// multiplier applies to stationary animals too (the web has no guard), and the two
    /// personality factors are DIFFERENT — roam varies ±25 %, swim only ±15 %. The radius is then
    /// capped, which is where the "humpback capped at 200" note comes from: 330 × 1.25 is 412, and
    /// with no hand-tuned radius the cap cuts it to 200.
    /// </summary>
    public static class Locomotion
    {
        /// <summary>Roam radius (world units) and swim multiplier for one animal.</summary>
        public struct Result
        {
            public double RoamRadius;
            public double SwimMultiplier;
        }

        /// <summary>
        /// <paramref name="configuredRoam"/> is the hand-tuned per-species radius when the
        /// manifest carries one. The web learned this the hard way — its own comment records
        /// that overwriting it made "every species in a zone roam the same; lionfish hovered
        /// 85u, humpback capped at 200".
        /// </summary>
        public static Result Derive(string zone, string diet, bool big, bool stationary,
                                    double energy = 0.5, double? configuredRoam = null)
        {
            double roam, swim;

            if (stationary)                             { roam = 8;   swim = 0.15; }
            else if (big)                               { roam = 330; swim = 0.90; }
            else if (zone == SpeciesGenome.ZonePelagic) { roam = 300; swim = 1.30; }
            else if (zone == SpeciesGenome.ZoneBottom)
            {
                roam = diet == SpeciesGenome.DietPredator ? 70 : 42;
                swim = 0.55;
            }
            else if (zone == SpeciesGenome.ZoneReef)    { roam = 85;  swim = 0.80; }
            else                                        { roam = 160; swim = 1.00; }

            if (diet == SpeciesGenome.DietPredator)
            {
                roam *= big ? 1.15 : 1.35;
                swim *= big ? 1.22 : 1.28;
            }

            // A configured radius wins over the zone default; personality still varies it.
            double baseRoam = configuredRoam ?? roam;

            // energy 0..1 varies both so two fish of one species do not move in lockstep — but by
            // different amounts: how far it wanders is a bigger personality trait than how fast.
            double e = Clamp01(energy);

            // The cap is the point of the hand-tuned radius: without one an animal is held to 200
            // world units however big it is; with one it is trusted out to 400.
            double cap = configuredRoam.HasValue ? 400 : 200;

            return new Result
            {
                RoamRadius = Math.Min(cap, baseRoam * (0.75 + e * 0.50)),
                SwimMultiplier = swim * (0.85 + e * 0.30),
            };
        }

        /// <summary>
        /// Animation rate from movement (builder.html:2486 — <c>0.18 + mv*0.16</c>). The floor
        /// matters: a fish that has stopped still beats its tail slowly rather than freezing,
        /// which is what "dead fish floating" looks like.
        /// </summary>
        public static double AnimationRate(double movement01)
        {
            return 0.18 + Clamp01(movement01) * 0.16;
        }

        private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);
    }
}
