using System;

namespace DiveMap.Core
{
    /// <summary>What a school is doing right now. Order is the log's order, do not renumber.</summary>
    public enum MindState
    {
        /// <summary>Holding station over home. The resting state, and where a wary shoal sits.</summary>
        Cruise = 0,
        /// <summary>Travelling to a self-chosen point somewhere in its roaming range.</summary>
        Wander = 1,
        /// <summary>Gone to look at a structure — circling it, or parked beside it.</summary>
        Investigate = 2,
        /// <summary>Something frightened it. Driven from outside by <see cref="FleeMath"/>.</summary>
        Startle = 3,
        /// <summary>The threat has gone; coming back together, still jumpy. NOT calm yet.</summary>
        Regroup = 4,
        /// <summary>Closing on something it means to eat. Driven by <see cref="HuntMath"/>.</summary>
        Hunt = 5,
        /// <summary>
        /// Running from something that hunts it, with a direction of its own rather than merely
        /// scattering. Distinct from <see cref="Startle"/>: a startle is a reflex that fades,
        /// an evade is a sustained retreat from an animal that is still coming.
        /// </summary>
        Evade = 6,
    }

    /// <summary>
    /// One species' temperament. Everything a school "wants" is in here, so a new species is a
    /// table row rather than a branch in the runtime.
    /// </summary>
    public readonly struct MindTraits
    {
        /// <summary>0..1 chance it goes to look at a structure instead of wandering.</summary>
        public readonly double Curiosity;
        /// <summary>How far it will drift from where it was placed, as a multiple of its own home radius.</summary>
        public readonly double RoamMul;
        /// <summary>How much of that budget it will spend going up and down.</summary>
        public readonly double DepthMul;
        /// <summary>Seconds it sticks with a decision (min/max — the actual dwell is drawn between them).</summary>
        public readonly double DwellMin;
        public readonly double DwellMax;
        /// <summary>rad/s the school walks around a structure while investigating. 0 = it parks instead.</summary>
        public readonly double OrbitRate;
        /// <summary>Orbit radius as a multiple of the structure's own radius.</summary>
        public readonly double OrbitRadiusMul;
        /// <summary>Extra standoff from the structure, as a multiple of the school's home radius.</summary>
        public readonly double StandoffMul;
        /// <summary>How high above the structure's centre it hangs (× home radius).</summary>
        public readonly double PoiHeightMul;
        /// <summary>Seconds of lingering nerves after a scare. Wariness decays linearly over this.</summary>
        public readonly double WarySeconds;

        public MindTraits(double curiosity, double roamMul, double depthMul,
                          double dwellMin, double dwellMax,
                          double orbitRate, double orbitRadiusMul, double standoffMul,
                          double poiHeightMul, double warySeconds)
        {
            Curiosity = curiosity; RoamMul = roamMul; DepthMul = depthMul;
            DwellMin = dwellMin; DwellMax = dwellMax;
            OrbitRate = orbitRate; OrbitRadiusMul = orbitRadiusMul; StandoffMul = standoffMul;
            PoiHeightMul = poiHeightMul; WarySeconds = warySeconds;
        }
    }

    /// <summary>
    /// The water an animal is allowed to be in: a disc on the seabed plus a depth band. Every
    /// target the mind picks is clamped into this, which is what makes "the shoal never leaves
    /// the map" a property of the code rather than a hope about the tuning.
    /// </summary>
    public readonly struct MindDomain
    {
        public readonly double CenterX, CenterZ, Radius, MinY, MaxY;

        public MindDomain(double centerX, double centerZ, double radius, double minY, double maxY)
        {
            CenterX = centerX; CenterZ = centerZ;
            Radius = radius > 1.0 ? radius : 1.0;
            MinY = minY; MaxY = maxY > minY ? maxY : minY + 1.0;
        }
    }

    /// <summary>Something worth swimming over to look at — in practice the wreck, or a big rock.</summary>
    public readonly struct MindPoi
    {
        public readonly double X, Y, Z, Radius;
        public MindPoi(double x, double y, double z, double radius)
        {
            X = x; Y = y; Z = z; Radius = radius > 0.1 ? radius : 0.1;
        }
    }

    /// <summary>
    /// One school's live brain state. A plain mutable struct held in an array by the runtime —
    /// nothing here allocates, and the whole thing is a pure function of (seed, decision count,
    /// sim clock), so the same seed replays the same reef.
    /// </summary>
    public struct Mind
    {
        public uint      Seed;
        /// <summary>Decisions taken so far. THIS, not the wall clock, is what the RNG is drawn from.</summary>
        public int       Tick;
        public MindState State;
        /// <summary>Sim time the current state may end.</summary>
        public double    Until;
        /// <summary>Sim time the current state began (for the log, and for orbit phase).</summary>
        public double    Since;
        /// <summary>0..1 lingering nerves. 1 right after a scare, decaying over WarySeconds.</summary>
        public double    Wariness;
        /// <summary>Index into the POI array while investigating, −1 otherwise.</summary>
        public int       Poi;
        /// <summary>Integrated orbit angle (radians). Integrated, never sin(clock): see SwimStyle.BeatPhaseStep.</summary>
        public double    OrbitPhase;
        /// <summary>Where the school WANTS its centre to be. The runtime eases toward this, never snaps.</summary>
        public double    TX, TY, TZ;
        public bool      Ready;
    }

    /// <summary>
    /// A hero animal's roaming state (the whale shark). Separate from <see cref="Mind"/> because a
    /// single big animal is steered directly rather than by moving a shoal's home.
    /// </summary>
    public struct Patrol
    {
        public uint   Seed;
        public int    Tick;
        public int    Leg;
        public double X, Y, Z;
        /// <summary>Heading in the XZ plane: direction = (cos h, sin h) → (x, z), the MarineMath convention.</summary>
        public double Heading;
        public double TX, TY, TZ;
        /// <summary>Current speed (u/s). EASED, never assigned — that is what keeps the motion continuous.</summary>
        public double Speed;
        /// <summary>Integrated breathing phase (radians).</summary>
        public double BreathPhase;
        /// <summary>Seconds spent on the current leg (stops a waypoint landing underfoot from thrashing).</summary>
        public double LegT;
        public bool   NearPoi;
        public bool   Ready;
    }

    /// <summary>
    /// Phase 2 goal 2 — "สัตว์ทุกชนิดมีสมองเป็นของตัวเอง". Every school gets its own intent
    /// (where it wants to be and why), and every species gets its own temperament.
    ///
    /// 🔴 What this is NOT. It is not an agent per fish. 1,100 fish on a phone already spend their
    /// budget in <see cref="DiveMap.Runtime.Marine.BoidsJob"/>; a second per-fish brain would cost
    /// the frame twice over for a difference nobody can see at shoal distance. What the eye
    /// actually reads at that distance is the SHOAL's intent: whether it is going somewhere,
    /// whether it noticed the wreck, whether it is still nervous. So the brain is per school —
    /// ten of them on the demo map, decided once a frame, allocation-free — and the per-fish
    /// variation stays where it already is (phase, size jitter, the boids' own separation).
    ///
    /// Three rules this file exists to keep:
    ///
    ///   • **Deterministic.** The RNG is a hash of (seed, decision index, channel). Nothing reads
    ///     the wall clock for randomness, so a test can replay a school for ten minutes and assert
    ///     the exact sequence of decisions — the thing that made the old "wander" term (a sine of
    ///     <c>Time</c>) untestable and, worse, identical for every school on the map.
    ///   • **No teleports.** The mind only ever moves a TARGET. The runtime eases the real home
    ///     toward it at no more than the fish's own cruise speed (<see cref="EaseHome"/>), so a
    ///     school can never be dragged faster than it could have swum. This is the same rule the
    ///     flee code already learned the hard way.
    ///   • **Fear has a tail.** A shoal that has just been charged does not go straight back to
    ///     sightseeing. <see cref="Mind.Wariness"/> decays over <see cref="MindTraits.WarySeconds"/>
    ///     and, while it is up, shortens dwells, shrinks the roaming range and blocks curiosity
    ///     entirely. Without it the reef reads as goldfish: startled, then instantly fine.
    /// </summary>
    public static class FishMind
    {
        public const double TwoPi = 6.283185307179586;

        /// <summary>Panic at which a school stops whatever it was doing (matches the [Flee] log gate).</summary>
        public const double StartlePanic = 0.05;
        /// <summary>Panic below which the threat counts as gone.</summary>
        public const double CalmPanic = 0.02;
        /// <summary>A startle lasts at least this long, so a threat flickering across the gate cannot strobe the state.</summary>
        public const double StartleMinSeconds = 1.5;
        /// <summary>Above this, a school is too nervous to go and look at anything.</summary>
        public const double WaryBlocksCuriosity = 0.35;

        // ── deterministic randomness ─────────────────────────────────────────────

        /// <summary>splitmix32 finaliser — a good avalanche in four instructions, no state, no alloc.</summary>
        private static uint Mix(uint x)
        {
            x += 0x9E3779B9u;
            x ^= x >> 16; x *= 0x21F0AAADu;
            x ^= x >> 15; x *= 0x735A2D97u;
            x ^= x >> 15;
            return x;
        }

        /// <summary>
        /// A 0..1 draw for (<paramref name="seed"/>, <paramref name="tick"/>, <paramref name="channel"/>).
        /// The channel keeps the angle, the distance and the dwell of ONE decision independent —
        /// drawing them from consecutive ticks instead would correlate a school's direction with
        /// how long it then stays there, which is visible as "it always turns left when it hurries".
        /// </summary>
        public static double Rand01(uint seed, int tick, int channel)
        {
            uint h = Mix(seed ^ Mix((uint)tick * 0x9E3779B9u ^ (uint)channel * 0x85EBCA6Bu));
            return h * (1.0 / 4294967296.0);
        }

        // ── species temperament ──────────────────────────────────────────────────

        /// <summary>
        /// The temperament of whatever <paramref name="assetId"/> names. Classified from the id and
        /// from <see cref="SpeciesGenome"/>'s zone, so a species nobody wrote a row for still gets a
        /// sensible personality from where it lives in the water column rather than a default that
        /// makes every unknown fish behave identically.
        ///
        /// The named rows are the four the demo map actually shows, and they are deliberately
        /// different from one another — that difference IS the feature:
        ///   • barracuda — a slow tornado that barely leaves its column (swimMul 0.06: it is the
        ///     calm one) but will walk itself around the wreck for a minute at a time.
        ///   • scad      — a nervous, fast shoal that changes its mind every ten seconds and covers
        ///     real ground.
        ///   • batfish   — hangs about the structure. Highest curiosity on the map.
        ///   • yellowtail (pod) — mid-water, unhurried, incurious, long legs.
        /// </summary>
        public static MindTraits TraitsFor(string assetId)
        {
            string id = assetId ?? "";

            // 🔴 OrbitRadiusMul and StandoffMul are small on purpose, and the demo map is why.
            // The wreck's POI radius is 62 u and a scad shoal's own home radius is 79; an early cut
            // used 1.6 and 0.9 and put the "investigate the wreck" target 170 u from the wreck's
            // CENTRE — further away than the shoal had started. The shoal then swam 250 u across the
            // map to go and look at something it was already closer to. Standing off by a fraction
            // of the shoal's own radius, just outside the structure, is what "went to look at it"
            // actually means.
            // ── 1. the demo map's own four, FROZEN, matched by exact id ──────────────
            // These numbers were tuned against the demo map and its QC shots; nothing below may
            // change them. Matched exactly rather than by substring so that the fifteen OTHER
            // species whose names contain "barracuda", "batfish" or "sardine" are free to have
            // rows of their own — msh:barracuda roams 26 u and school:barracuda roams 6, and
            // before this they were the same animal.
            if (Same(id, "school:barracuda"))
                //          curio roam depth  dwellMin/Max  orbit  orbitR stand  poiH  wary
                return new MindTraits(0.55, 0.50, 0.22, 26.0, 48.0, 0.055, 1.30, 0.30, 0.20, 14.0);
            if (Same(id, "school:scad"))
                return new MindTraits(0.30, 1.60, 0.50, 9.0, 18.0, 0.000, 1.20, 0.45, 0.25, 10.0);
            if (Same(id, "school:batfish"))
                return new MindTraits(0.80, 0.70, 0.28, 18.0, 34.0, 0.030, 1.10, 0.20, 0.30, 12.0);
            if (Same(id, "pod:yellowtail"))
                return new MindTraits(0.25, 1.20, 0.35, 20.0, 38.0, 0.000, 1.25, 0.55, 0.30, 16.0);

            // ── 2. every species the WEB hand-tuned gets its own row, from that row ──
            // Before this, ninety species shared four zone fallbacks: every reef fish in the game
            // had the same curiosity, the same dwell and the same roaming range, so a lionfish
            // (which hovers over seven units of sand) and a parrotfish (seventy) were the same
            // animal wearing a different mesh.
            SpeciesBehavior.Cfg cfg = SpeciesBehavior.For(id);
            if (cfg.Has) return FromWebRow(cfg, SpeciesGenome.For(id));

            // ── 3. no row anywhere, but the name is recognisable ─────────────────────
            // Keeps a hand-authored map that says "fish:sardine" or "msh:scad_school" behaving
            // like the shoal it is naming rather than like a generic mid-water fish.
            if (Has(id, "barracuda"))
                return new MindTraits(0.55, 0.50, 0.22, 26.0, 48.0, 0.055, 1.30, 0.30, 0.20, 14.0);
            if (Has(id, "scad") || Has(id, "sardine") || Has(id, "anchov"))
                return new MindTraits(0.30, 1.60, 0.50, 9.0, 18.0, 0.000, 1.20, 0.45, 0.25, 10.0);
            if (Has(id, "batfish"))
                return new MindTraits(0.80, 0.70, 0.28, 18.0, 34.0, 0.030, 1.10, 0.20, 0.30, 12.0);
            if (Has(id, "yellowtail") || Has(id, "trevally") || Has(id, "jack"))
                return new MindTraits(0.25, 1.20, 0.35, 20.0, 38.0, 0.000, 1.25, 0.55, 0.30, 16.0);

            // ── 4. nothing at all — fall back to where this animal lives ─────────────
            SpeciesGenome.Genome g = SpeciesGenome.For(id);
            if (g.Zone == SpeciesGenome.ZoneBottom)
                return new MindTraits(0.70, 0.25, 0.10, 16.0, 30.0, 0.015, 1.05, 0.12, 0.05, 10.0);
            if (g.Zone == SpeciesGenome.ZoneReef)
                return new MindTraits(0.85, 0.35, 0.18, 12.0, 24.0, 0.022, 1.10, 0.15, 0.20, 11.0);
            if (g.Zone == SpeciesGenome.ZonePelagic)
                return new MindTraits(0.20, 1.50, 0.45, 22.0, 42.0, 0.000, 1.35, 0.60, 0.35, 15.0);

            return new MindTraits(0.45, 0.90, 0.32, 14.0, 28.0, 0.018, 1.20, 0.35, 0.25, 12.0);
        }

        private static bool Has(string id, string needle)
            => id.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool Same(string id, string other)
            => string.Equals(id, other, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Units of the web's <c>roamR</c> that one unit of this app's <see cref="MindTraits.RoamMul"/>
        /// is worth.
        ///
        /// 🔴 The two systems do not measure the same thing and pretending they do is how a port
        /// goes wrong quietly. The web's <c>roamR</c> is an ABSOLUTE radius in map units around
        /// where the animal was placed (1 for a giant clam, 400 for a humpback). This app's
        /// <c>RoamMul</c> is RELATIVE to the school's own home radius, because the thing that
        /// wanders here is a shoal, not a fish. What can be carried across is the ORDERING and the
        /// spread, and 100 u per unit does exactly that: the frozen rows above sit at 0.5-1.6, the
        /// web's rows span 1-400, and dividing by 100 lands the web's spread on the app's band.
        /// The clamp keeps a 400 u humpback from dragging a shoal twice across the map and a 1 u
        /// clam from pinning one to a point.
        /// </summary>
        public const double WebRoamPerMul = 100.0;
        /// <summary>Narrowest and widest a derived row may roam. See <see cref="WebRoamPerMul"/>.</summary>
        public const double DerivedRoamMin = 0.08, DerivedRoamMax = 1.80;

        /// <summary>
        /// Build a temperament from the species' own hand-tuned web row plus its genome. Pure, so
        /// every species in the manifest can be asserted against it without a scene.
        ///
        /// Which trait comes from where, and why:
        ///   • <b>roam</b> — the web's <c>roamR</c>, rescaled (<see cref="WebRoamPerMul"/>).
        ///   • <b>curiosity</b> — the genome's own curiosity range (web :1901-1902: a triggerfish
        ///     comes to look at you, a damselfish does not), tilted by where it lives. A reef fish
        ///     lives ON structure so it visits it constantly; a pelagic has no reason to.
        ///   • <b>dwell</b> — inversely from the species' cruise speed. A sailfish changes its
        ///     mind every six seconds and a lionfish hangs in one spot for a minute, and that
        ///     difference is most of what tells them apart at a distance.
        ///   • <b>wariness</b> — from boldness (web :1898). A predator settles faster than prey.
        /// </summary>
        private static MindTraits FromWebRow(in SpeciesBehavior.Cfg cfg, in SpeciesGenome.Genome g)
        {
            bool bottom  = g.Zone == SpeciesGenome.ZoneBottom;
            bool reef    = g.Zone == SpeciesGenome.ZoneReef;
            bool pelagic = g.Zone == SpeciesGenome.ZonePelagic;

            // Roam. A stationary animal is pinned at the floor of the band, not at zero: even a
            // clam's "home" has to have a radius or the clamp maths divides by it.
            double webRoam = cfg.HasRoamR
                           ? cfg.RoamR
                           : SpeciesBehavior.Derive(cfg, g, 0.5).BaseRoam;
            double roamMul = Clamp(webRoam / WebRoamPerMul, DerivedRoamMin, DerivedRoamMax);
            if (cfg.Stationary) roamMul = DerivedRoamMin;

            // Depth budget: how much of the roaming is spent going up and down.
            double depthMul = cfg.Stationary ? 0.04
                            : bottom  ? 0.10
                            : reef    ? 0.20
                            : pelagic ? 0.45
                            :           0.32;

            // Curiosity. The genome midpoint is the species' disposition; the zone says how much
            // structure there is to be curious ABOUT.
            double curiosity = g.Curiosity * (reef ? 1.25 : bottom ? 1.15 : pelagic ? 0.55 : 1.0);
            if (cfg.Big) curiosity *= 0.7;          // a big animal slides past, it does not sightsee
            curiosity = Clamp(curiosity, 0.05, 0.95);

            // Dwell from speed: fast animals are restless. CruiseMul already folds the web's
            // hand-tuned speedMul into the derived swimMul, so a 0.15× lionfish and a 1.9×
            // sailfish come out an order of magnitude apart, which is the point.
            double speed = SpeciesBehavior.Derive(cfg, g, 0.5).SwimMul;
            if (cfg.HasSpeed) speed *= cfg.SpeedMul;
            double dwellMin = Clamp(14.0 / (speed > 0.12 ? speed : 0.12), 6.0, 40.0);
            double dwellMax = dwellMin * 1.9;

            // Orbiting a structure. Pelagic animals pass by; reef and bottom dwellers walk round.
            double orbitRate = cfg.Stationary || pelagic ? 0.0 : (cfg.Big ? 0.012 : 0.022);
            double orbitRMul = bottom ? 1.05 : reef ? 1.10 : pelagic ? 1.35 : 1.25;
            double standoff  = (bottom ? 0.12 : reef ? 0.15 : pelagic ? 0.60 : 0.35) * (cfg.Big ? 1.3 : 1.0);
            double poiHeight = bottom ? 0.05 : reef ? 0.20 : pelagic ? 0.35 : 0.30;

            // Nerves. Bold animals stop being nervous sooner (web :1898).
            double wary = Clamp(8.0 + 10.0 * (1.0 - g.Boldness), 8.0, 18.0);

            return new MindTraits(curiosity, roamMul, depthMul, dwellMin, dwellMax,
                                  orbitRate, orbitRMul, standoff, poiHeight, wary);
        }

        private static double Clamp(double v, double lo, double hi)
            => v < lo ? lo : (v > hi ? hi : v);

        // ── the school brain ─────────────────────────────────────────────────────

        /// <summary>
        /// Advance one school's mind by <paramref name="dt"/> seconds and leave the place it wants
        /// its centre to be in <see cref="Mind.TX"/>/<c>TY</c>/<c>TZ</c>.
        ///
        /// <paramref name="anchorX"/>… is the AUTHORED placement (never the live, eased home — that
        /// one is dragged about every frame and measuring against it lets a school chase its own
        /// tail across the map). <paramref name="panic"/> comes straight from
        /// <see cref="FleeMath.SchoolPanic"/>; this method never decides whether to be afraid, only
        /// what being afraid does to the plan.
        ///
        /// Returns true on the frames where the state changed — the runtime logs exactly those.
        /// </summary>
        public static bool Step(
            ref Mind m, in MindTraits tr, in MindDomain dom,
            MindPoi[] pois, int poiCount,
            double anchorX, double anchorY, double anchorZ,
            double homeR, double panic, double time, double dt,
            out MindState previous)
        {
            if (poiCount < 0) poiCount = 0;
            if (pois == null) poiCount = 0;
            if (poiCount > (pois != null ? pois.Length : 0)) poiCount = pois != null ? pois.Length : 0;
            if (homeR <= 0.0) homeR = 1.0;
            if (dt < 0.0) dt = 0.0;

            previous = m.State;

            // First frame: sit over the placement and give it a first dwell. Reported as a change so
            // the log always opens with a [Mind] line — a school that never printed and a school
            // that never ran look the same otherwise (HANDOFF §4 rule 14).
            if (!m.Ready)
            {
                m.Ready = true;
                m.State = MindState.Cruise;
                m.Poi = -1;
                m.Since = time;
                m.Until = time + Dwell(m, tr, 0.0);
                m.TX = anchorX; m.TY = anchorY; m.TZ = anchorZ;
                ClampToDomain(dom, homeR, ref m.TX, ref m.TY, ref m.TZ);
                return true;
            }

            // Nerves decay on a clock, and are floored by whatever fear is live right now, so a
            // shoal being chased stays at full alert for as long as the chase lasts.
            double wary = m.Wariness - (tr.WarySeconds > 0.1 ? dt / tr.WarySeconds : dt);
            if (wary < 0.0) wary = 0.0;
            if (panic > wary) wary = panic > 1.0 ? 1.0 : panic;
            m.Wariness = wary;

            bool changed = false;

            if (panic > StartlePanic)
            {
                // Frightened. The flee steering owns the fish; the mind's job is only to stop
                // sightseeing and to point home so that whatever happens next starts from there.
                if (m.State != MindState.Startle)
                {
                    m.State = MindState.Startle;
                    m.Poi = -1;
                    m.Since = time;
                    changed = true;
                }
                m.Until = time + StartleMinSeconds;
                m.TX = anchorX; m.TY = anchorY; m.TZ = anchorZ;
            }
            else if (m.State == MindState.Startle)
            {
                // 🔴 The threat has gone — and this is the branch that stops the reef reading as
                // goldfish. It does NOT go back to what it was doing. It regroups over its own
                // home for a spell, and comes out of it still wary, which keeps curiosity off and
                // the roaming range small for a good while longer.
                if (panic <= CalmPanic && time >= m.Until)
                {
                    m.State = MindState.Regroup;
                    m.Since = time;
                    m.Until = time + RegroupSeconds(tr);
                    m.TX = anchorX; m.TY = anchorY; m.TZ = anchorZ;
                    changed = true;
                }
            }
            else if (time >= m.Until)
            {
                changed = true;
                m.Since = time;
                Choose(ref m, tr, dom, pois, poiCount, anchorX, anchorY, anchorZ, homeR, time);
            }

            // An orbiting school walks itself around the structure instead of parking on one side.
            // Integrated per frame, so a change of orbit rate is continuous.
            if (m.State == MindState.Investigate && m.Poi >= 0 && m.Poi < poiCount && tr.OrbitRate > 0.0)
            {
                m.OrbitPhase += tr.OrbitRate * dt;
                if (m.OrbitPhase >= TwoPi) m.OrbitPhase %= TwoPi;
                AimAtPoi(ref m, tr, dom, pois[m.Poi], homeR);
            }

            return changed;
        }

        /// <summary>
        /// What a predator or a prey animal knows about the other animals around it this frame.
        /// A plain struct so <see cref="StepHunter"/> keeps a sane signature and the runtime can
        /// fill it in place with no allocation.
        /// </summary>
        public readonly struct Quarry
        {
            /// <summary>Something this animal eats is within its sense radius.</summary>
            public readonly bool   HasPrey;
            public readonly double PreyX, PreyY, PreyZ;
            /// <summary>Something that eats THIS animal is within its sense radius.</summary>
            public readonly bool   HasHunter;
            public readonly double HunterX, HunterZ;
            /// <summary>Obstacle radius of this animal — every sense radius is built from it.</summary>
            public readonly double ObsR;

            public Quarry(bool hasPrey, double preyX, double preyY, double preyZ,
                          bool hasHunter, double hunterX, double hunterZ, double obsR)
            {
                HasPrey = hasPrey; PreyX = preyX; PreyY = preyY; PreyZ = preyZ;
                HasHunter = hasHunter; HunterX = hunterX; HunterZ = hunterZ;
                ObsR = obsR > 0.0 ? obsR : 1.0;
            }
        }

        /// <summary>
        /// C6 — <see cref="Step"/> with the predator/prey layer on top: the same brain, plus
        /// <see cref="MindState.Hunt"/> and <see cref="MindState.Evade"/>.
        ///
        /// The precedence is the web's and it is not negotiable (builder.html:2182-2199): fear
        /// first, hunger second, ordinary life last. A shark that has something bigger on it stops
        /// hunting — the web literally clears <c>mem.prey</c> in that case (:1957) — and a hungry
        /// animal still runs. Getting this backwards produces the one thing that instantly reads
        /// as a bug: a predator calmly eating while something eats it.
        ///
        /// 🔴 Everything else is delegated to <see cref="Step"/> unchanged, deliberately. This
        /// method adds states; it does not get to re-decide dwell, curiosity, wariness or the
        /// domain clamp, all of which are already tested. When neither hunting nor evading applies
        /// the two methods are the same function.
        ///
        /// <paramref name="selfX"/>/<paramref name="selfZ"/> is where the animal (or the shoal's
        /// centre) actually is — NOT the anchor. The anchor is where it was placed and a hunt is
        /// about where things are now.
        /// </summary>
        public static bool StepHunter(
            ref Mind m, ref HuntDrive drive,
            in MindTraits tr, in MindDomain dom,
            MindPoi[] pois, int poiCount,
            double anchorX, double anchorY, double anchorZ,
            double selfX, double selfZ, double heading,
            double homeR, double panic,
            in Quarry q, in SpeciesGenome.Genome gen, bool ambush, bool big,
            double time, double dt,
            out MindState previous, out HuntStep hunt)
        {
            bool predator = gen.Diet == SpeciesGenome.DietPredator;

            // ── fear first (web :2182, and :1957 — a frightened predator has no prey) ─────
            double fleeR = FleeMath.FleeRadius(q.ObsR);
            bool hunted = false;
            double hunterD = 0.0;
            if (q.HasHunter)
            {
                double hx = selfX - q.HunterX, hz = selfZ - q.HunterZ;
                hunterD = Math.Sqrt(hx * hx + hz * hz);
                hunted = hunterD < fleeR;
            }

            bool mayHunt = predator && !hunted && panic <= StartlePanic;

            hunt = HuntMath.Step(ref drive,
                                 mayHunt && q.HasPrey, q.PreyX - selfX, q.PreyZ - selfZ,
                                 heading, q.ObsR, ambush, big,
                                 gen.Metabolism, time, dt);

            // ── the ordinary brain always runs, so wariness and dwell keep their clocks ───
            bool changed = Step(ref m, tr, dom, pois, poiCount,
                                anchorX, anchorY, anchorZ, homeR, panic, time, dt, out previous);

            // Startle (a burst of fear) outranks both of the states below — Step has already set
            // it and its target, and nothing here may overwrite that.
            if (m.State == MindState.Startle) return changed;

            // ── evade: seen, not yet panicking. Put distance between them. ───────────────
            if (hunted && hunterD > 1e-6)
            {
                if (m.State != MindState.Evade) { m.Since = time; changed = true; }
                m.State = MindState.Evade;
                m.Poi = -1;
                m.Until = time + StartleMinSeconds;
                double ax = (selfX - q.HunterX) / hunterD, az = (selfZ - q.HunterZ) / hunterD;
                double run = homeR * (1.0 + tr.RoamMul);
                m.TX = selfX + ax * run;
                m.TZ = selfZ + az * run;
                m.TY = anchorY;
                ClampToDomain(dom, homeR, ref m.TX, ref m.TY, ref m.TZ);
                return changed;
            }

            // ── hunt: it is closing on something ─────────────────────────────────────────
            if (hunt.Phase == HuntPhase.Stalk || hunt.Phase == HuntPhase.Sprint || hunt.Phase == HuntPhase.Strike)
            {
                if (m.State != MindState.Hunt) { m.Since = time; changed = true; }
                m.State = MindState.Hunt;
                m.Poi = -1;
                // A hunt is re-decided every frame from where the prey IS, so the dwell is only a
                // floor that stops the state flickering when the prey crosses the engage radius.
                m.Until = time + HuntMath.SprintSeconds;
                m.TX = q.PreyX; m.TY = q.PreyY; m.TZ = q.PreyZ;
                ClampToDomain(dom, homeR, ref m.TX, ref m.TY, ref m.TZ);
                return changed;
            }

            // Hunt/Evade have lapsed but Step left the state alone because its dwell has not
            // expired — force a fresh decision so the animal does not sit in a stale Hunt.
            if (m.State == MindState.Hunt || m.State == MindState.Evade)
            {
                m.Since = time;
                Choose(ref m, tr, dom, pois, poiCount, anchorX, anchorY, anchorZ, homeR, time);
                changed = true;
            }

            return changed;
        }

        /// <summary>How long the regroup itself lasts before ordinary life resumes.</summary>
        public static double RegroupSeconds(in MindTraits tr)
            => (tr.WarySeconds > 0.1 ? tr.WarySeconds : 1.0) * 0.5;

        /// <summary>Pick the next thing to do. Called only when the previous decision has expired.</summary>
        private static void Choose(
            ref Mind m, in MindTraits tr, in MindDomain dom,
            MindPoi[] pois, int poiCount,
            double anchorX, double anchorY, double anchorZ,
            double homeR, double time)
        {
            m.Tick++;
            double wary = m.Wariness;

            // Reach: how far out a structure still counts as "over there, worth a look".
            double reach = homeR * (1.0 + tr.RoamMul * 2.0);
            int eligible = 0;
            for (int i = 0; i < poiCount; i++)
            {
                double dx = pois[i].X - anchorX, dz = pois[i].Z - anchorZ;
                if (dx * dx + dz * dz <= reach * reach) eligible++;
            }

            bool curious = eligible > 0
                        && wary < WaryBlocksCuriosity
                        && Rand01(m.Seed, m.Tick, 1) < tr.Curiosity * (1.0 - wary);

            if (curious)
            {
                int want = (int)(Rand01(m.Seed, m.Tick, 2) * eligible);
                if (want >= eligible) want = eligible - 1;
                int seen = 0, chosen = -1;
                for (int i = 0; i < poiCount; i++)
                {
                    double dx = pois[i].X - anchorX, dz = pois[i].Z - anchorZ;
                    if (dx * dx + dz * dz > reach * reach) continue;
                    if (seen == want) { chosen = i; break; }
                    seen++;
                }
                if (chosen >= 0)
                {
                    m.State = MindState.Investigate;
                    m.Poi = chosen;
                    // 🔴 It arrives on the side it is ALREADY on, give or take half a radian. Drawing
                    // that angle uniformly (which is what the first cut did) sends the shoal the long
                    // way round to the far side of the wreck — a 250 u migration to look at something
                    // it was already beside, and the reef visibly empties while it happens. The orbit
                    // below then walks it round from wherever it arrived.
                    m.OrbitPhase = Math.Atan2(anchorZ - pois[chosen].Z, anchorX - pois[chosen].X)
                                 + (Rand01(m.Seed, m.Tick, 3) - 0.5) * 0.9;
                    m.Until = time + Dwell(m, tr, wary);
                    AimAtPoi(ref m, tr, dom, pois[chosen], homeR);
                    return;
                }
            }

            m.Poi = -1;

            // A wary shoal, or an occasional draw, just holds station over home. Without this every
            // school is permanently in transit, which is its own kind of wrong.
            bool hold = wary >= WaryBlocksCuriosity || Rand01(m.Seed, m.Tick, 4) < 0.22;
            if (hold)
            {
                m.State = MindState.Cruise;
                m.Until = time + Dwell(m, tr, wary);
                double a = Rand01(m.Seed, m.Tick, 5) * TwoPi;
                double d = homeR * 0.20 * (1.0 - 0.6 * wary);
                m.TX = anchorX + Math.Cos(a) * d;
                m.TZ = anchorZ + Math.Sin(a) * d;
                m.TY = anchorY;
                ClampToDomain(dom, homeR, ref m.TX, ref m.TY, ref m.TZ);
                return;
            }

            m.State = MindState.Wander;
            m.Until = time + Dwell(m, tr, wary);
            double ang = Rand01(m.Seed, m.Tick, 6) * TwoPi;
            double dist = homeR * tr.RoamMul * (0.35 + 0.65 * Rand01(m.Seed, m.Tick, 7)) * (1.0 - 0.6 * wary);
            m.TX = anchorX + Math.Cos(ang) * dist;
            m.TZ = anchorZ + Math.Sin(ang) * dist;
            m.TY = anchorY + (Rand01(m.Seed, m.Tick, 8) * 2.0 - 1.0)
                           * homeR * tr.DepthMul * (1.0 - 0.5 * wary);
            ClampToDomain(dom, homeR, ref m.TX, ref m.TY, ref m.TZ);
        }

        /// <summary>Where to sit relative to a structure, at the current orbit angle.</summary>
        private static void AimAtPoi(ref Mind m, in MindTraits tr, in MindDomain dom,
                                     in MindPoi poi, double homeR)
        {
            double r = poi.Radius * tr.OrbitRadiusMul + homeR * tr.StandoffMul;
            m.TX = poi.X + Math.Cos(m.OrbitPhase) * r;
            m.TZ = poi.Z + Math.Sin(m.OrbitPhase) * r;
            m.TY = poi.Y + homeR * tr.PoiHeightMul;
            ClampToDomain(dom, homeR, ref m.TX, ref m.TY, ref m.TZ);
        }

        /// <summary>Dwell for one decision. Nerves shorten it — a wary shoal fidgets.</summary>
        private static double Dwell(in Mind m, in MindTraits tr, double wary)
        {
            double lo = tr.DwellMin > 0.5 ? tr.DwellMin : 0.5;
            double hi = tr.DwellMax > lo ? tr.DwellMax : lo + 1.0;
            double d = lo + (hi - lo) * Rand01(m.Seed, m.Tick, 9);
            return d * (1.0 - 0.4 * Clamp01(wary));
        }

        // ── keeping everything inside the map ────────────────────────────────────

        /// <summary>
        /// Pull a point inside the domain, leaving <paramref name="margin"/> of clearance at the
        /// rim. The runtime passes the school's own home radius as the margin, so it is the WHOLE
        /// shoal that stays on the map, not merely its centre — which is the difference between
        /// "the sand ends and the fish stop" and "half the shoal is swimming over the void".
        /// </summary>
        public static void ClampToDomain(in MindDomain dom, double margin,
                                         ref double x, ref double y, ref double z)
        {
            double lim = dom.Radius - (margin > 0.0 ? margin : 0.0);
            if (lim < dom.Radius * 0.15) lim = dom.Radius * 0.15;

            double dx = x - dom.CenterX, dz = z - dom.CenterZ;
            double d = Math.Sqrt(dx * dx + dz * dz);
            if (d > lim && d > 1e-6)
            {
                double k = lim / d;
                x = dom.CenterX + dx * k;
                z = dom.CenterZ + dz * k;
            }
            if (y < dom.MinY) y = dom.MinY;
            if (y > dom.MaxY) y = dom.MaxY;
        }

        /// <summary>
        /// The water ONE school is allowed to roam, given the map's own radius and where the school
        /// was actually placed.
        ///
        /// 🔴 The max() is the whole point. A map may legitimately place a shoal past the point
        /// where a clamp at (mapRadius − homeR) would put it — a hand-edited map, or simply a big
        /// shoal near the rim. Clamping it to the map alone would then quietly MIGRATE it inward on
        /// the first frame, at cruise speed, and the reef the author laid out would rearrange itself
        /// while they watched. Growing the domain just enough to contain the placement means the
        /// school keeps its spot and still cannot wander any further out than it already is.
        /// </summary>
        public static MindDomain SchoolDomain(double mapRadius, double minY, double maxY,
                                              double anchorX, double anchorZ, double homeR)
        {
            double need = Math.Sqrt(anchorX * anchorX + anchorZ * anchorZ) + (homeR > 0.0 ? homeR : 0.0);
            return new MindDomain(0.0, 0.0, mapRadius > need ? mapRadius : need, minY, maxY);
        }

        /// <summary>True when (x,z,y) is inside the domain, allowing <paramref name="margin"/> at the rim.</summary>
        public static bool InsideDomain(in MindDomain dom, double margin, double x, double y, double z)
        {
            double dx = x - dom.CenterX, dz = z - dom.CenterZ;
            double lim = dom.Radius - (margin > 0.0 ? margin : 0.0);
            if (lim < dom.Radius * 0.15) lim = dom.Radius * 0.15;
            return dx * dx + dz * dz <= lim * lim + 1e-6
                && y >= dom.MinY - 1e-6 && y <= dom.MaxY + 1e-6;
        }

        /// <summary>
        /// Move a school's live centre toward what its mind wants, by AT MOST
        /// <paramref name="maxStep"/> units. The cap is the fish's own cruise speed × dt, so the
        /// shoal can never be dragged faster than it could have swum — the property that makes
        /// "no teleport" structural rather than a tuning accident. Same shape as Unity's
        /// MoveTowards, restated here so it can be asserted without a scene.
        /// </summary>
        public static void EaseHome(double cx, double cy, double cz,
                                    double tx, double ty, double tz,
                                    double maxStep,
                                    out double nx, out double ny, out double nz)
        {
            double dx = tx - cx, dy = ty - cy, dz = tz - cz;
            double d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (maxStep < 0.0) maxStep = 0.0;
            if (d <= maxStep || d < 1e-9)
            {
                nx = tx; ny = ty; nz = tz;
                return;
            }
            double k = maxStep / d;
            nx = cx + dx * k; ny = cy + dy * k; nz = cz + dz * k;
        }

        /// <summary>Short label for the QC log. One token, so a reviewer can grep a state machine.</summary>
        public static string Label(MindState s)
        {
            switch (s)
            {
                case MindState.Cruise:      return "Cruise";
                case MindState.Wander:      return "Wander";
                case MindState.Investigate: return "Investigate";
                case MindState.Startle:     return "Startle";
                case MindState.Regroup:     return "Regroup";
                case MindState.Hunt:        return "Hunt";
                case MindState.Evade:       return "Evade";
                default:                    return "?";
            }
        }

        // ── the hero animal's patrol ─────────────────────────────────────────────

        /// <summary>Breaths per second. One slow cycle every ~12 s.</summary>
        public const double BreathHz = 0.085;
        /// <summary>How much the breath moves the speed (±22 %).</summary>
        public const double BreathDepth = 0.22;
        /// <summary>Cruise fraction it drops to while sliding past something interesting.</summary>
        public const double PoiSlowMul = 0.45;
        /// <summary>
        /// How near counts as "sliding past it", as a multiple of the structure's radius.
        ///
        /// 🔴 Barely over 1. The demo map's wreck has a 62 u POI radius and the animal roams 98 u
        /// from an anchor 63 u off the origin, so at the first cut's 2.2 (a 136 u bubble) it was
        /// "beside the wreck" for 900 seconds out of 900 — permanently at 45 % throttle, and the
        /// slowdown carried no information at all. A behaviour that is always on is not a behaviour.
        /// </summary>
        public const double PoiSlowReach = 1.25;
        /// <summary>Acceleration cap, as a fraction of cruise per second. Speed is EASED, never set.</summary>
        public const double AccelFrac = 0.5;
        /// <summary>Vertical speed cap as a fraction of forward — MarineMath's rule, applied to the hero too.</summary>
        public const double VerticalFrac = 0.55;
        /// <summary>Chance a new leg heads for a structure rather than open water.</summary>
        public const double PoiVisitChance = 0.45;
        /// <summary>A leg lasts at least this long, so a waypoint landing underfoot cannot thrash.</summary>
        public const double MinLegSeconds = 1.0;

        /// <summary>
        /// Start a patrol at <paramref name="x"/>,<paramref name="y"/>,<paramref name="z"/>. The
        /// first waypoint is drawn immediately so the animal is already going somewhere on frame
        /// one rather than snapping into motion a second later.
        /// </summary>
        public static void PatrolInit(ref Patrol p, uint seed,
                                      double x, double y, double z,
                                      in MindDomain dom,
                                      double anchorX, double anchorY, double anchorZ,
                                      double roamR, MindPoi[] pois, int poiCount,
                                      double arriveR, double cruise)
        {
            p.Seed = seed;
            p.Tick = 0;
            p.Leg = 0;
            p.X = x; p.Y = y; p.Z = z;
            p.Speed = cruise > 0.0 ? cruise : 1.0;
            p.BreathPhase = Rand01(seed, 0, 20) * TwoPi;
            p.Heading = Rand01(seed, 0, 21) * TwoPi;
            p.LegT = 0.0;
            p.NearPoi = false;
            p.Ready = true;
            ClampToDomain(dom, arriveR, ref p.X, ref p.Y, ref p.Z);
            PickWaypoint(ref p, dom, anchorX, anchorY, anchorZ, roamR, pois, poiCount, arriveR);
        }

        /// <summary>
        /// One step of the hero animal's roam. Returns true when a new leg was started.
        ///
        /// 🔴 Why this replaced a fixed ellipse. A perfect ellipse at a constant speed is the one
        /// motion the eye immediately files as "machine": you can predict where it will be in
        /// twenty seconds. Three things break that, and all three are here — waypoints drawn from
        /// the domain instead of a parametrised curve, a speed that breathes, and a slowdown when
        /// it passes something worth passing.
        ///
        /// Nothing in here can teleport. The heading turns at a capped rate, the speed is eased
        /// under an acceleration cap, and the position is integrated from those two — so the
        /// largest possible step is speed × dt, which is what the tests assert.
        /// </summary>
        public static bool PatrolStep(
            ref Patrol p, double dt, double cruise, double turnRatePerSec,
            in MindDomain dom,
            double anchorX, double anchorY, double anchorZ,
            double roamR, MindPoi[] pois, int poiCount, double arriveR)
        {
            if (pois == null) poiCount = 0;
            if (poiCount < 0) poiCount = 0;
            if (poiCount > (pois != null ? pois.Length : 0)) poiCount = pois != null ? pois.Length : 0;
            if (cruise <= 0.0) cruise = 1.0;
            if (arriveR <= 0.0) arriveR = cruise * 2.0;
            if (turnRatePerSec <= 0.0) turnRatePerSec = 0.25;
            if (dt <= 0.0) return false;

            if (!p.Ready)
            {
                PatrolInit(ref p, p.Seed, p.X, p.Y, p.Z, dom,
                           anchorX, anchorY, anchorZ, roamR, pois, poiCount, arriveR, cruise);
                return true;
            }

            p.LegT += dt;

            bool newLeg = false;
            double wdx = p.TX - p.X, wdz = p.TZ - p.Z;
            if (p.LegT >= MinLegSeconds && wdx * wdx + wdz * wdz <= arriveR * arriveR)
            {
                PickWaypoint(ref p, dom, anchorX, anchorY, anchorZ, roamR, pois, poiCount, arriveR);
                newLeg = true;
            }

            // Heading: turn toward the waypoint at a capped rate. Keeping the animal off the rim is
            // STEERING, not a clamp — a clamped position is a teleport wearing a hat.
            double aimX = p.TX, aimZ = p.TZ;
            double cdx = p.X - dom.CenterX, cdz = p.Z - dom.CenterZ;
            double cd = Math.Sqrt(cdx * cdx + cdz * cdz);
            double keep = dom.Radius - arriveR * 2.0;
            if (keep < dom.Radius * 0.2) keep = dom.Radius * 0.2;
            if (cd > keep) { aimX = dom.CenterX; aimZ = dom.CenterZ; }

            double desired = Math.Atan2(aimZ - p.Z, aimX - p.X);
            p.Heading = MarineMath.TurnToward(p.Heading, desired, turnRatePerSec * dt);

            // Breathing. Integrated, so the rate can change without the speed jumping.
            p.BreathPhase += TwoPi * BreathHz * dt;
            if (p.BreathPhase >= TwoPi) p.BreathPhase %= TwoPi;
            double want = cruise * (1.0 + BreathDepth * Math.Sin(p.BreathPhase));

            // Slow down beside anything worth looking at — the "แวะช้า" the goal asks for, and the
            // one behaviour that makes a big animal read as interested rather than on rails.
            p.NearPoi = false;
            for (int i = 0; i < poiCount; i++)
            {
                double dx = p.X - pois[i].X, dz = p.Z - pois[i].Z;
                double rr = pois[i].Radius * PoiSlowReach;
                if (dx * dx + dz * dz <= rr * rr) { p.NearPoi = true; break; }
            }
            if (p.NearPoi) want *= PoiSlowMul;

            double accel = cruise * AccelFrac * dt;
            double sd = want - p.Speed;
            if (sd > accel) sd = accel;
            if (sd < -accel) sd = -accel;
            p.Speed += sd;
            if (p.Speed < cruise * 0.10) p.Speed = cruise * 0.10;

            p.X += Math.Cos(p.Heading) * p.Speed * dt;
            p.Z += Math.Sin(p.Heading) * p.Speed * dt;

            // Vertical: ease toward the waypoint's depth, never faster than 0.55× forward.
            double vy = (p.TY - p.Y) * 0.5;
            double vc = p.Speed * VerticalFrac;
            if (vy > vc) vy = vc;
            if (vy < -vc) vy = -vc;
            p.Y += vy * dt;
            if (p.Y < dom.MinY) p.Y = dom.MinY;
            if (p.Y > dom.MaxY) p.Y = dom.MaxY;

            return newLeg;
        }

        private static void PickWaypoint(ref Patrol p, in MindDomain dom,
                                         double anchorX, double anchorY, double anchorZ,
                                         double roamR, MindPoi[] pois, int poiCount, double arriveR)
        {
            p.Tick++;
            p.Leg++;
            p.LegT = 0.0;

            if (roamR <= 0.0) roamR = arriveR * 6.0;

            if (poiCount > 0 && Rand01(p.Seed, p.Tick, 11) < PoiVisitChance)
            {
                int pick = (int)(Rand01(p.Seed, p.Tick, 12) * poiCount);
                if (pick >= poiCount) pick = poiCount - 1;
                MindPoi q = pois[pick];
                double a = Rand01(p.Seed, p.Tick, 13) * TwoPi;
                double rr = q.Radius * (1.4 + 0.8 * Rand01(p.Seed, p.Tick, 14));
                p.TX = q.X + Math.Cos(a) * rr;
                p.TZ = q.Z + Math.Sin(a) * rr;
                p.TY = q.Y + q.Radius * 0.8;
            }
            else
            {
                double a = Rand01(p.Seed, p.Tick, 15) * TwoPi;
                double d = roamR * (0.45 + 0.55 * Rand01(p.Seed, p.Tick, 16));
                p.TX = anchorX + Math.Cos(a) * d;
                p.TZ = anchorZ + Math.Sin(a) * d;
                p.TY = anchorY + (Rand01(p.Seed, p.Tick, 17) * 2.0 - 1.0) * roamR * 0.28;
            }

            // A waypoint that lands underfoot would be "reached" the same frame it was drawn and the
            // animal would stutter through a dozen legs in a second. Push it out along the heading.
            double mdx = p.TX - p.X, mdz = p.TZ - p.Z;
            double md = Math.Sqrt(mdx * mdx + mdz * mdz);
            double need = arriveR * 3.0;
            if (md < need)
            {
                double ux = md > 1e-6 ? mdx / md : Math.Cos(p.Heading);
                double uz = md > 1e-6 ? mdz / md : Math.Sin(p.Heading);
                p.TX = p.X + ux * need;
                p.TZ = p.Z + uz * need;
            }

            ClampToDomain(dom, arriveR * 2.0, ref p.TX, ref p.TY, ref p.TZ);
        }

        private static double Clamp01(double v) => v < 0.0 ? 0.0 : (v > 1.0 ? 1.0 : v);
    }
}
