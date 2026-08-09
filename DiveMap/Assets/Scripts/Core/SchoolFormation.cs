using System;

namespace DiveMap.Core
{
    /// <summary>The nine shapes a school cycles through (builder.html <c>SCHOOL_MODES</c>, :1542).</summary>
    public enum SchoolMode
    {
        Cluster = 0,
        Stream  = 1,
        Vortex  = 2,
        Tornado = 3,
        Cone    = 4,
        ConeUp  = 5,
        Ball    = 6,
    }

    /// <summary>
    /// The web's school system, ported literally: <b>slot formation</b>, not boids.
    ///
    /// 🔴 Why this file exists. The Unity build drove every school with Reynolds boids at
    /// <c>speed = MaxSpeed</c> every frame plus a wander term (BoidsJob :186-192). A fish with a
    /// constant speed and no destination cannot ever stop, so an alignment-dominated flock
    /// stretches into a long thin ribbon and keeps stretching — which is exactly what came back
    /// from a real iPhone (build 244): "สัตว์ทะเลก็ยังเคลื่อนไหวขยับห่าง".
    ///
    /// builder.html does something completely different and it is not subtle:
    ///
    ///   • Every fish owns a SLOT in a named formation (<see cref="Target"/>), recomputed from
    ///     <c>t</c> each frame — there is no neighbour scan anywhere in the web's school code.
    ///   • Its speed is <c>min(cap, distanceToItsSlot × chaseK)</c> (:1610). A fish that has
    ///     ARRIVED therefore slows to the cruise floor <c>flen×0.005</c> and stays put. That one
    ///     term is the whole reason a web school looks like a school: it is a spring, not a
    ///     cruise missile.
    ///   • The formation is swapped every 7-16 s × <c>modeDurMul</c> and MORPHED over 8 s, so the
    ///     shape changes without anything teleporting (:1547-1551, :1755-1760).
    ///
    /// Everything here is per-FRAME in the web's own units (the web multiplies by <c>FS</c>, the
    /// real-delta scale) so the numbers can be compared to builder.html line by line. Convert with
    /// <see cref="MarineMath.SchoolFrameRate"/> when a per-second figure is wanted.
    ///
    /// Coordinates are LOCAL to the school (origin = the school's anchor), exactly as the web's
    /// are local to the group — but in WORLD units, because MarineMath.SchoolGeometryFor has
    /// already multiplied the web's local formulas by the map's item scale.
    /// </summary>
    public static class SchoolFormation
    {
        private const double TwoPi        = 6.283185307179586;
        private const double WebTwoPi     = 6.283;               // the web's own literal (:1521)
        private const double GoldenAngle  = 2.39996;             // (:1522)
        private const double GoldenRatio  = 0.61803398875;       // (:1523)

        // ── Mode wheel (builder.html :1542) ───────────────────────────────────────
        /// <summary>
        /// The weighted bag the next formation is drawn from, VERBATIM including the repeats:
        /// cluster ×4 and stream ×2 out of twelve, so a school spends two thirds of its life
        /// polarised and cruising and only occasionally does something photogenic.
        /// Sampling this with a uniform index is what the web does (<c>SCHOOL_MODES[rand×len|0]</c>, :1746).
        /// </summary>
        public static readonly SchoolMode[] Modes =
        {
            SchoolMode.Cluster, SchoolMode.Cluster, SchoolMode.Cluster, SchoolMode.Stream,
            SchoolMode.Cluster, SchoolMode.Stream,  SchoolMode.Vortex,  SchoolMode.Tornado,
            SchoolMode.Cone,    SchoolMode.Tornado, SchoolMode.ConeUp,  SchoolMode.Ball,
        };

        /// <summary>Base seconds a school holds each shape — builder.html <c>MODE_DUR</c> (:1543).</summary>
        public static double ModeDurSeconds(SchoolMode m)
        {
            switch (m)
            {
                case SchoolMode.Cluster: return 7.0;
                case SchoolMode.Vortex:  return 13.0;
                case SchoolMode.Tornado: return 16.0;
                case SchoolMode.Cone:    return 15.0;
                case SchoolMode.ConeUp:  return 15.0;
                case SchoolMode.Ball:    return 9.0;
                case SchoolMode.Stream:  return 12.0;
                default:                 return 7.0;
            }
        }

        /// <summary>Extra random hold, <c>+ Math.random()*6*dm</c> (:1547).</summary>
        public const double HoldJitterSeconds = 6.0;

        /// <summary>How long this school stays in <paramref name="m"/> — <c>MODE_DUR[m]*dm + rand*6*dm</c> (:1547).</summary>
        public static double HoldSeconds(SchoolMode m, double modeDurMul, double rand01)
        {
            double dm = modeDurMul > 0.0 ? modeDurMul : 1.0;
            double r  = rand01 < 0.0 ? 0.0 : (rand01 > 1.0 ? 1.0 : rand01);
            return ModeDurSeconds(m) * dm + r * HoldJitterSeconds * dm;
        }

        /// <summary>Morph length: 8 s × transDurMul, or 1.6 s flat when panicking (:1549).</summary>
        public const double TransDurSeconds      = 8.0;
        public const double TransDurPanicSeconds = 1.6;

        public static double TransDur(bool fast, double transDurMul)
            => fast ? TransDurPanicSeconds
                    : TransDurSeconds * (transDurMul > 0.0 ? transDurMul : 1.0);

        /// <summary>The morph is finished for everyone once raw progress passes 1 + max(trJit) (:1754).</summary>
        public const double MorphDoneAt = 1.4;

        // ── Per-school constants (builder.html :1535, :1585-1620, :1697) ──────────
        /// <summary>Ring spin, <c>(0.22 + rand*0.22) * spinMul</c> rad/s (:1535).</summary>
        public const double SpinBase = 0.22;
        public const double SpinRand = 0.22;

        /// <summary>Slot-chase ease, <c>easeL = 0.03 * easeMul</c> (:1535) — and the chase gain is <c>easeL*2.2</c> (:1761).</summary>
        public const double EaseLBase  = 0.03;
        public const double ChaseKMul  = 2.2;

        /// <summary>Per-frame turn-rate caps: ±0.045 forward-only, ±0.05 on the calm path (:1603, :1592).</summary>
        public const double TurnCapPerFrame     = 0.045;
        public const double CalmTurnCapPerFrame = 0.05;
        /// <summary>A fleeing fish may turn 1.6× harder (:1603).</summary>
        public const double FleeTurnMul = 1.6;
        /// <summary>…and swim 1.5× faster (:1610).</summary>
        public const double FleeSpeedMul = 1.5;

        /// <summary>Cruise floor so an arrived fish still sculls forward: <c>+ flen*0.005</c>/frame (:1610).</summary>
        public const double CruiseFloorPerFrame = 0.005;

        /// <summary>Calm-path move: <c>min(spCap*1.8, dh*0.05)</c> per frame (:1593).</summary>
        public const double CalmChasePerFrame = 0.05;
        public const double CalmCapMul        = 1.8;

        /// <summary>Vertical ease and the "diagonal only" clamp (:1614, :1596).</summary>
        public const double VerticalEasePerFrame     = 0.08;
        public const double VerticalRatio            = 0.55;
        public const double CalmVerticalEasePerFrame = 0.06;
        public const double CalmVerticalRatio        = 0.6;

        /// <summary>
        /// Settle distance: past it a fish steers at its slot, inside it the fish takes the
        /// FORMATION's heading instead (:1601). Polarised shapes get a wide 3.0×flen band, which is
        /// what stops a school's heads from wagging at their own bobbing slots.
        /// </summary>
        public const double SettleFlenPolar = 3.0;
        public const double SettleFlenFree  = 0.9;

        /// <summary>Safety wall a fish is squeezed back inside: R×3.2 (cluster) / SR×2.6 (shoal) (:1611, :1597).</summary>
        public const double HomeMulCluster = 3.2;
        public const double HomeMulShoal   = 2.6;

        /// <summary>Yaw smoothing applied to the drawn rotation, <c>rotation.y += Δ*0.3</c> (:1618).</summary>
        public const double HeadingRenderLerp = 0.3;

        /// <summary>Panic past this balls the school up; the ball is held ≥2.5 s (:1697).</summary>
        public const double BallUpPanic      = 0.6;
        public const double BallHoldSeconds  = 2.5;

        // ── Shoal (the loose milling aggregation: scad, batfish) — :1727-1742 ─────
        public const double ShoalRetargetMinSeconds = 4.0;
        public const double ShoalRetargetJitter     = 7.0;   // 4 + rand*7
        public const double ShoalStepFlenXZ         = 2.4;   // (rand-0.5)*fl*2.4
        public const double ShoalStepFlenY          = 1.2;
        public const double ShoalYFraction          = 0.4;   // |y| ≤ SR*0.4
        public const double ShoalChaseK             = 0.016; // (:1741)

        // ── Per-fish slot seed (builder.html :1520-1533) ──────────────────────────
        /// <summary>
        /// One fish's fixed slot data. Every field is drawn ONCE when the school is built and
        /// never touched again — that permanence is the point. A boid re-decides where it is going
        /// sixty times a second; a web fish has one address for its whole life and merely swims
        /// back to it.
        /// </summary>
        public struct Slot
        {
            public double ClusterX, ClusterY, ClusterZ;  // (:1524) the fish's own address in the blob
            public double Ang;                           // (i/N)*6.283              (:1526)
            public double YSpread;                       // (rand-0.5)*flen*1.4      (:1533)
            public double CylY;                          // (i*0.618…)%1             (:1531)
            public double BallPh;                        // acos(1-2(i+0.5)/N)       (:1529)
            public double BallA;                         // i*2.39996                (:1530)
            public double Lane;                          // i/N - 0.5                (:1533)
            public double TrJit;                         // rand*0.35                (:1532)
            public double BobR, BobPh, HeadJit;          // pods only                (:1527-1528)
        }

        /// <summary>
        /// Build fish <paramref name="i"/>'s slot. <paramref name="r0"/>..<paramref name="r6"/> are
        /// the seven uniform randoms the web draws, passed in so the caller owns the RNG and a test
        /// can pin the deterministic half exactly.
        ///
        /// <paramref name="spanXZ"/> is <c>SR</c> for a shoal and <c>R</c> for a formation, and
        /// <paramref name="spanY"/> is <c>SR*0.7</c> / <c>R*0.55</c> — the web's own asymmetry
        /// (:1523-1524). Schools are pancakes, never balls.
        /// </summary>
        public static Slot SlotFor(int i, int n, double spanXZ, double spanY, double flen,
                                   double r0, double r1, double r2, double r3,
                                   double r4, double r5, double r6)
        {
            if (n < 1) n = 1;
            if (i < 0) i = 0;
            return new Slot
            {
                ClusterX = (r0 - 0.5) * spanXZ * 2.0,
                ClusterY = (r1 - 0.5) * spanY,
                ClusterZ = (r2 - 0.5) * spanXZ * 2.0,
                Ang      = ((double)i / n) * WebTwoPi,
                BobR     = 0.4 + r3 * 0.6,
                BobPh    = r4 * WebTwoPi,
                HeadJit  = (r5 - 0.5) * 0.22,
                BallPh   = Math.Acos(1.0 - 2.0 * (i + 0.5) / n),
                BallA    = i * GoldenAngle,
                CylY     = (i * GoldenRatio) % 1.0,
                TrJit    = r6 * 0.35,
                YSpread  = (r0 - 0.5) * flen * 1.4,
                Lane     = (double)i / n - 0.5,
            };
        }

        // ── The slot itself ───────────────────────────────────────────────────────
        /// <summary>Where a fish should be, and which way the FORMATION says it should point.</summary>
        public struct Target
        {
            public double X, Y, Z;
            /// <summary>Formation heading as a unit XZ vector; (0,0) never happens — every mode sets it.</summary>
            public double VX, VZ;
        }

        /// <summary>
        /// One fish's slot in <paramref name="mode"/> at time <paramref name="t"/>, in school-local
        /// coordinates — builder.html <c>_formTarget</c> (:1556-1580), transcribed line for line.
        ///
        /// <paramref name="groupHeading"/> is the school's own heading and is only read by
        /// <see cref="SchoolMode.Cluster"/>, where the whole point is that every fish faces the
        /// same way (a POLARISED school). That single line is the difference between the web's
        /// barracuda and the ribbon of independently-aimed boids the iPhone photographed.
        /// </summary>
        /// <param name="breatheAlongHeading">
        /// false = the pre-fix circular breath (see the Cluster branch). QC ONLY: the "before"
        /// clip has to be the real before, or a side-by-side proves nothing.
        /// </param>
        public static Target FormTarget(in Slot fi, SchoolMode mode, double t, double R,
                                        double spin, double flen, double streamDir,
                                        double groupHeading, bool breatheAlongHeading = true)
        {
            Target o;
            switch (mode)
            {
                case SchoolMode.Vortex:
                {
                    double a = fi.Ang + t * spin;
                    o.X = Math.Cos(a) * R; o.Z = Math.Sin(a) * R; o.Y = fi.YSpread;
                    o.VX = -Math.Sin(a); o.VZ = Math.Cos(a);
                    return o;
                }
                case SchoolMode.Tornado:
                {
                    double a = fi.Ang + t * spin, rr = R * 0.55, cylH = R * 1.8;
                    o.X = Math.Cos(a) * rr; o.Z = Math.Sin(a) * rr; o.Y = fi.CylY * cylH;
                    o.VX = -Math.Sin(a); o.VZ = Math.Cos(a);
                    return o;
                }
                case SchoolMode.Cone:
                {
                    double a = fi.Ang + t * spin, coneH = R * 1.8, rr = R * (1.0 - 0.8 * fi.CylY);
                    o.X = Math.Cos(a) * rr; o.Z = Math.Sin(a) * rr; o.Y = fi.CylY * coneH;
                    o.VX = -Math.Sin(a); o.VZ = Math.Cos(a);
                    return o;
                }
                case SchoolMode.ConeUp:
                {
                    double a = fi.Ang + t * spin, coneH = R * 1.8, rr = R * (0.2 + 0.8 * fi.CylY);
                    o.X = Math.Cos(a) * rr; o.Z = Math.Sin(a) * rr; o.Y = fi.CylY * coneH;
                    o.VX = -Math.Sin(a); o.VZ = Math.Cos(a);
                    return o;
                }
                case SchoolMode.Ball:
                {
                    double a = fi.BallA + t * spin * 1.3, rr = R * 0.55, sp = Math.Sin(fi.BallPh);
                    o.X = Math.Cos(a) * sp * rr; o.Z = Math.Sin(a) * sp * rr;
                    o.Y = Math.Cos(fi.BallPh) * rr * 0.85;
                    o.VX = -Math.Sin(a); o.VZ = Math.Cos(a);
                    return o;
                }
                case SchoolMode.Stream:
                {
                    double d = streamDir, fwd = fi.Lane * R * 4.0;
                    double sway = Math.Sin(t * 1.1 + fi.Ang * 1.6) * R * 0.3;
                    o.X = Math.Cos(d) * fwd - Math.Sin(d) * (sway + fi.YSpread * 0.3);
                    o.Z = Math.Sin(d) * fwd + Math.Cos(d) * (sway + fi.YSpread * 0.3);
                    o.Y = fi.YSpread * 0.5 + Math.Sin(t * 0.7 + fi.Ang) * R * 0.12;
                    o.VX = Math.Cos(d); o.VZ = Math.Sin(d);
                    return o;
                }
                default:
                {
                    // Cluster — a POLARISED school: a spaced, gently breathing slot, and EVERY
                    // fish faces the school's own heading.
                    //
                    // 🔴 The breathing runs FORE-AND-AFT along that heading, not round a circle —
                    // and this one line is the root of "ปลาไถลข้าง" (user, 8-9 ส.ค. 2026).
                    //
                    // builder.html :1576-1578 puts sin() on X and cos() on Z at the SAME phase,
                    // which is the parametric form of a circle: every fish's slot orbits a ring of
                    // half a body length, once every ~12.6 s. A fish chasing a target that goes
                    // round in a circle has a travel direction that sweeps all 360° — while its
                    // nose is pinned to the school's heading. Measured on the Harddeep barracuda:
                    // it travels 52° off its own nose, and 69 % of frames are past 45°. The eye
                    // reads a school of fish crabbing sideways.
                    //
                    // Breathing along the heading instead keeps everything that made the shape —
                    // same amplitude, same rate, same per-fish phase, same address, still a
                    // POLARISED cluster — and drops the crab to 14° with the school's own order
                    // untouched (user picked this, 9 ส.ค., after seeing the numbers per mode).
                    // The ring shapes (vortex/tornado/cone/ball) are NOT touched: their slots are
                    // meant to travel round a ring and their fish face along it.
                    double breath = Math.Sin(t * 0.5 + fi.Ang * 1.7) * flen * 0.5;
                    o.VX = Math.Cos(groupHeading); o.VZ = Math.Sin(groupHeading);
                    o.X = fi.ClusterX + (breatheAlongHeading ? breath * o.VX : breath);
                    o.Y = fi.ClusterY + Math.Sin(t * 0.7 + fi.Ang * 1.3) * flen * 0.35;
                    o.Z = fi.ClusterZ + (breatheAlongHeading
                                         ? breath * o.VZ
                                         : Math.Cos(t * 0.5 + fi.Ang * 1.7) * flen * 0.5);
                    return o;
                }
            }
        }

        // ── Mode scheduling ───────────────────────────────────────────────────────
        /// <summary>Which shape a school is in, which it is coming FROM, and when it changes.</summary>
        public struct ModeState
        {
            public SchoolMode Mode;
            public SchoolMode PrevMode;
            public bool       HasPrev;
            public double     Until;
            public double     TransT0;
            public double     TransDur;
            public double     StreamDir;
            public double     PrevStreamDir;
            public uint       Seed;
            public int        Tick;
        }

        /// <summary>A school opens in <c>cluster</c> with the timer already expired (:1535 <c>modeUntil:0</c>).</summary>
        public static ModeState NewState(uint seed, double streamDir) => new ModeState
        {
            Mode = SchoolMode.Cluster, PrevMode = SchoolMode.Cluster, HasPrev = false,
            Until = 0.0, TransT0 = 0.0, TransDur = TransDurSeconds,
            StreamDir = streamDir, PrevStreamDir = streamDir, Seed = seed, Tick = 0,
        };

        /// <summary>
        /// builder.html <c>_schoolSetMode</c> (:1546-1552). Asking for the mode it is already in
        /// only EXTENDS the timer — it must not restart the morph, or a school that keeps drawing
        /// "cluster" out of the bag would blend from cluster to cluster forever and never settle.
        /// </summary>
        public static void SetMode(ref ModeState s, SchoolMode m, double t, bool fast,
                                   double modeDurMul, double transDurMul,
                                   double randHold, double randDir)
        {
            double hold = HoldSeconds(m, modeDurMul, randHold);
            if (m == s.Mode) { s.Until = t + hold; return; }

            s.PrevMode = s.Mode;
            s.PrevStreamDir = s.StreamDir;
            s.HasPrev = true;
            s.TransT0 = t;
            s.TransDur = TransDur(fast, transDurMul);
            s.Mode = m;
            s.Until = t + hold;
            if (m == SchoolMode.Stream) s.StreamDir = randDir * TwoPi;   // new heading each migration (:1551)
        }

        /// <summary>
        /// Advance the wheel. Returns true when the shape changed this call.
        ///
        /// The panic rule is the web's (:1697): sustained fear balls the school up with a FAST
        /// morph and pins the timer 2.5 s out, so it cannot flick back open the moment the diver
        /// slows. <paramref name="fixedMode"/> is the <c>asset.fixedMode</c> lock (:1745) — a
        /// school that has one returns to it as soon as the fear drops below 0.3.
        /// </summary>
        public static bool Step(ref ModeState s, double t, double panic, bool isPod,
                                double modeDurMul, double transDurMul,
                                SchoolMode? fixedMode = null)
        {
            SchoolMode before = s.Mode;

            if (panic > BallUpPanic && !isPod)
            {
                if (s.Mode != SchoolMode.Ball)
                    SetMode(ref s, SchoolMode.Ball, t, true, modeDurMul, transDurMul,
                            Rand(ref s, 0), Rand(ref s, 1));
                double hold = t + BallHoldSeconds;
                if (hold > s.Until) s.Until = hold;
                return before != s.Mode;
            }

            if (fixedMode.HasValue)
            {
                if (s.Mode != fixedMode.Value && panic < 0.3)
                    SetMode(ref s, fixedMode.Value, t, false, modeDurMul, transDurMul,
                            Rand(ref s, 2), Rand(ref s, 3));
                return before != s.Mode;
            }

            if (t > s.Until)
            {
                int pick = (int)(Rand(ref s, 4) * Modes.Length);
                if (pick >= Modes.Length) pick = Modes.Length - 1;
                if (pick < 0) pick = 0;
                SetMode(ref s, Modes[pick], t, false, modeDurMul, transDurMul,
                        Rand(ref s, 5), Rand(ref s, 6));
                s.Tick++;
            }
            return before != s.Mode;
        }

        private static double Rand(ref ModeState s, int channel)
            => FishMind.Rand01(s.Seed, s.Tick, 60 + channel);

        /// <summary>
        /// This fish's blend from the old shape to the new one: smoothstep of
        /// <c>(t-T0)/dur − trJit</c> (:1755-1757). The per-fish stagger is what makes a school
        /// REFORM progressively instead of the whole cloud snapping into the new shape together.
        /// 1 = fully in the new formation.
        /// </summary>
        public static double MorphBlend(in ModeState s, double t, double trJit)
        {
            if (!s.HasPrev) return 1.0;
            double dur = s.TransDur > 1e-6 ? s.TransDur : TransDurSeconds;
            double kk = (t - s.TransT0) / dur - trJit;
            if (kk <= 0.0) return 0.0;
            if (kk >= 1.0) return 1.0;
            return kk * kk * (3.0 - 2.0 * kk);
        }

        /// <summary>True once even the most staggered fish has finished morphing (:1754).</summary>
        public static bool MorphFinished(in ModeState s, double t)
        {
            if (!s.HasPrev) return true;
            double dur = s.TransDur > 1e-6 ? s.TransDur : TransDurSeconds;
            return (t - s.TransT0) / dur > MorphDoneAt;
        }

        // ── The speed law — the single most important formula in the port ──────────
        /// <summary>
        /// builder.html :1610 — <c>min(spCap × (flee?1.5:1), dh × chaseK) + flen × 0.005</c>,
        /// per FRAME, before the <c>FS</c> real-delta scale.
        ///
        /// 🔴 The <c>dh × chaseK</c> branch is the entire difference between the web's schools and
        /// the boids that replaced them. <paramref name="distToSlot"/> is the fish's distance from
        /// its OWN slot, so a fish in position swims at the cruise floor and the school holds
        /// together; only stragglers accelerate, and only up to the cap. Boids at a constant
        /// MaxSpeed have no such term, which is why they spread.
        /// </summary>
        public static double StepSpeedPerFrame(double distToSlot, double chaseK, double capPerFrame,
                                               double flen, bool fleeing)
        {
            double cap = capPerFrame * (fleeing ? FleeSpeedMul : 1.0);
            double want = distToSlot * chaseK;
            return (want < cap ? want : cap) + flen * CruiseFloorPerFrame;
        }

        /// <summary>
        /// The CALM path (:1591-1600) — used by any school flagged <c>calm</c> in the web catalog
        /// while it is polarised and not fleeing, which for <c>school:barracuda</c> is almost
        /// always. It has no cruise floor at all: a barracuda that has reached its slot stops
        /// dead relative to the school and simply holds heading. "สโลว์ + เรียงหัวเป็นระเบียบ".
        /// </summary>
        public static double CalmStepPerFrame(double distToSlot, double capPerFrame)
        {
            double cap = capPerFrame * CalmCapMul;
            double want = distToSlot * CalmChasePerFrame;
            return want < cap ? want : cap;
        }

        /// <summary>
        /// 🔴 "ห้ามว่ายถอยหลัง" (user, build 261) — the pure mirror of the rule
        /// <c>BoidsJob.FormationStep</c> applies on the calm path. Kept here because BoidsJob is a
        /// Burst job over <c>Unity.Mathematics</c> and <c>tools/test.sh</c> cannot compile it, so
        /// without this the one path that could actually reverse would be the one path with no
        /// test on it.
        ///
        /// The calm path is the only one that can reverse, and it is not an oversight in the port
        /// — it is the web's own shape. builder.html:1592-1593 turns the NOSE toward the school's
        /// heading and moves the BODY toward the slot, as two independent quantities, so a fish
        /// whose slot has drifted behind it travels backwards until it catches up. On the web that
        /// is a few frames inside a milling cluster; here it is 200 barracuda doing it in formation
        /// where a diver is looking straight at them.
        ///
        /// Only the reversing COMPONENT is removed. Sideways easing is the whole character of this
        /// path (see <see cref="CalmStepPerFrame"/>) and a fish restricted to its own nose axis
        /// would have to swim a circle to move a metre across.
        ///
        /// <paramref name="headYaw"/> and <paramref name="moveYaw"/> are Unity yaws
        /// (<c>atan2(x, z)</c>, 0 = +Z). Returns the corrected (x, z) step.
        /// </summary>
        public static void ForwardOnlyCalmStep(double headYaw, double moveYaw, double distance,
                                               out double stepX, out double stepZ)
        {
            stepX = Math.Sin(moveYaw) * distance;
            stepZ = Math.Cos(moveYaw) * distance;

            double fx = Math.Sin(headYaw), fz = Math.Cos(headYaw);   // unit by construction
            double into = fx * stepX + fz * stepZ;
            if (into >= 0.0) return;                                  // already forwards

            stepX -= fx * into;
            stepZ -= fz * into;
        }

        /// <summary>Chase gain for the formation path: <c>easeL × 2.2</c>, easeL = 0.03 × easeMul (:1535, :1761).</summary>
        public static double ChaseK(double easeMul, double fleeEase)
            => (EaseLBase * (easeMul > 0.0 ? easeMul : 1.0) + fleeEase) * ChaseKMul;

        // ── "ปลาไถลข้าง" (user, 8 ส.ค. 2026) ──────────────────────────────────────
        //
        // 🔴 What was measured. On the calm path the NOSE holds the school's heading while the
        // BODY eases toward the slot, as two independent quantities (:1592-1593). Measuring the
        // angle between the two — which nothing had ever measured, because heading and position
        // are each perfectly smooth on their own — gives, for the Harddeep barracuda:
        //
        //     cluster  64° off its own nose, 69 % of frames        (both POLARISED modes)
        //     stream   75° off its own nose, 93 % of frames
        //     vortex   14°,  tornado/ball/cone 28-30°               (nose = ring tangent, fine)
        //
        // i.e. in the two shapes a school spends two thirds of its life in, 160 barracuda hold
        // their noses parallel and crab sideways up to half a body length. The user reports it as
        // "ไถลข้าง". It survived ten rounds of tuning because every one of those rounds moved the
        // wave, the beat, the turn deadband or the drawn-rotation damping — and not one of them
        // touches the DIRECTION the body travels.
        //
        // The fix is not to forbid sideways easing: that easing is the character of this path
        // (see ForwardOnlyCalmStep) and a fish restricted to its nose axis would have to swim a
        // circle to move a metre across. It is to let the nose FOLLOW the body when the body is
        // actually going somewhere, and only hold the school's heading when it has arrived —
        // which is also what keeps the old "atan2 of a near-zero step is noise" failure closed.
        /// <summary>
        /// Fraction of the calm cruise step at which the nose is FULLY on the direction of travel.
        ///
        /// Measured, not guessed (Harddeep barracuda, speed-weighted crab angle — the angle the eye
        /// actually reads, since a fish barely moving cannot look like it is sliding):
        ///
        ///     knee     cluster   stream   vortex     nose shimmer
        ///     off        52.4°    75.0°    13.9°           0.03°
        ///     1.00       26.4°    10.5°     3.8°           0.24°
        ///     0.50        6.7°    10.6°     0.8°           0.24°
        ///     0.25        1.4°    10.6°     0.7°           0.24°   ← here
        ///     0.12        1.4°    10.6°     0.7°           0.24°   (saturated)
        ///
        /// 0.25 is the first value that has converged. The nose shimmer is flat across every knee
        /// and the school's slot RMS TIGHTENS (0.21 → 0.16 body lengths), so the knee itself is
        /// free. The residual 10.6° on `stream` is that shape's own sway and is meant to be there.
        /// </summary>
        public const double CalmNoseKnee = 0.25;

        /// <summary>
        /// …but the knee alone cannot be the whole answer, and this is the number that says why.
        ///
        /// 🔴 In `cluster` the slot ORBITS: <c>X += sin(t·0.5+…)·flen·0.5</c> and
        /// <c>Z += cos(same phase)·flen·0.5</c> is a CIRCLE of half a body length (:1576-1578).
        /// A fish that follows a circling target has a travel direction that sweeps all 360°, so
        /// letting the nose follow it fully turns a polarised school into a milling swarm —
        /// which is the one thing the port exists to prevent (Cluster_IsPolarised…, and the user's
        /// own "สโลว์ + เรียงหัวเป็นระเบียบ"). Measured on the Harddeep barracuda:
        ///
        ///     deviation cap   polarisation   crab angle (speed-weighted)
        ///     off (was)              1.00          52.4°   ← the reported "ไถลข้าง"
        ///     20°                    0.95          38.1°
        ///     30°                    0.90          32.2°   ← here
        ///     40°                    0.85          27.2°
        ///     55°                    0.73          20.4°
        ///     uncapped               0.26           1.4°   ← no longer a school
        ///
        /// 30° keeps the school visibly ordered (0.90) while taking 20° off the crab. It is a
        /// judgement between two things the user has asked for at different times, so it is a
        /// knob with a table behind it rather than a derivation — and the user picks the row.
        /// </summary>
        public const double CalmNoseCapRad = 30.0 * Math.PI / 180.0;

        /// <summary>
        /// How much the nose should follow the direction of travel this frame: 0 while the fish is
        /// parked on its slot (hold the school's heading — a stationary fish has no travel
        /// direction and reading one is how the heading used to jitter), 1 once it is easing at
        /// <see cref="CalmNoseKnee"/> of the calm cruise step.
        /// </summary>
        public static double CalmNoseBlend(double distToSlot, double capPerFrame)
        {
            double cruise = capPerFrame * CalmCapMul * CalmNoseKnee;
            if (cruise <= 1e-12 || distToSlot <= 0.0) return 0.0;
            double w = distToSlot * CalmChasePerFrame / cruise;
            return w > 1.0 ? 1.0 : w;
        }

        /// <summary>
        /// The heading the nose should aim at: the school's heading blended toward the direction
        /// the body is actually moving, by <see cref="CalmNoseBlend"/>. Angles are Unity yaws.
        /// </summary>
        public static double CalmNoseTarget(double schoolHeading, double moveHeading, double blend)
        {
            if (blend <= 0.0) return schoolHeading;
            if (blend > 1.0) blend = 1.0;
            double d = Math.Atan2(Math.Sin(moveHeading - schoolHeading),
                                  Math.Cos(moveHeading - schoolHeading)) * blend;
            if (d >  CalmNoseCapRad) d =  CalmNoseCapRad;   // stay a SCHOOL — see CalmNoseCapRad
            if (d < -CalmNoseCapRad) d = -CalmNoseCapRad;
            return schoolHeading + d;
        }

        /// <summary>
        /// Which heading a fish steers at: its slot while it is still far from it, the FORMATION's
        /// heading once it has arrived (:1600-1602). The settle band is deliberately wide for a
        /// polarised shape — chasing a slot that is itself bobbing is what makes heads wag.
        /// </summary>
        public static double SettleDistance(double flen, bool polarised)
            => flen * (polarised ? SettleFlenPolar : SettleFlenFree);

        /// <summary>Signed shortest turn from <paramref name="from"/> to <paramref name="to"/>, clamped to ±<paramref name="cap"/>.</summary>
        public static double TurnStep(double from, double to, double cap)
        {
            double d = Math.Atan2(Math.Sin(to - from), Math.Cos(to - from));
            return d < -cap ? -cap : (d > cap ? cap : d);
        }

        // ── Cohesion metrics: the QC oracle for "is it a school or a smear" ────────
        /// <summary>
        /// What a reviewer needs in ONE log line to tell a school from a scatter. A screenshot
        /// cannot answer it (the eye cannot integrate 160 positions) and neither can a beat rate,
        /// which is how build 244 shipped with the fins calibrated and the school still wrong.
        /// </summary>
        public readonly struct Cohesion
        {
            /// <summary>RMS distance from each fish to its OWN slot, in fish lengths.</summary>
            public readonly double SlotRmsFlen;
            /// <summary>Fraction of fish inside the formation radius of the school centre (0..1).</summary>
            public readonly double InsideFrac;
            /// <summary>Mean nearest-neighbour distance, in fish lengths.</summary>
            public readonly double NeighbourFlen;
            /// <summary>
            /// |mean heading unit vector| — the textbook polarisation order parameter. 1 = every
            /// fish points the same way (a cruising school), 0 = a milling swarm. The web's
            /// cluster/stream modes are ~1 BY CONSTRUCTION; Reynolds boids never reach it.
            /// </summary>
            public readonly double Polarisation;

            public Cohesion(double slotRmsFlen, double insideFrac, double neighbourFlen, double polarisation)
            {
                SlotRmsFlen = slotRmsFlen; InsideFrac = insideFrac;
                NeighbourFlen = neighbourFlen; Polarisation = polarisation;
            }
        }

        /// <summary>
        /// Measure a school. <paramref name="px"/>/<paramref name="py"/>/<paramref name="pz"/> are
        /// fish positions and <paramref name="sx"/>/<paramref name="sy"/>/<paramref name="sz"/>
        /// their slots, both in the SAME frame (school-local or world — only differences are read).
        /// <paramref name="heading"/> is each fish's yaw in radians.
        /// <paramref name="stride"/> subsamples the O(n²) neighbour pass.
        /// </summary>
        public static Cohesion Measure(double[] px, double[] py, double[] pz,
                                       double[] sx, double[] sy, double[] sz,
                                       double[] heading, int count,
                                       double cx, double cy, double cz,
                                       double formR, double flen, int stride = 1)
        {
            if (count <= 0 || px == null) return new Cohesion(0, 0, 0, 0);
            if (flen <= 1e-6) flen = 1.0;
            if (stride < 1) stride = 1;

            double sumSq = 0.0;
            int inside = 0;
            double hx = 0.0, hz = 0.0;
            for (int i = 0; i < count; i++)
            {
                if (sx != null)
                {
                    double dx = px[i] - sx[i], dy = py[i] - sy[i], dz = pz[i] - sz[i];
                    sumSq += dx * dx + dy * dy + dz * dz;
                }
                double ex = px[i] - cx, ey = py[i] - cy, ez = pz[i] - cz;
                if (Math.Sqrt(ex * ex + ey * ey + ez * ez) <= formR) inside++;
                if (heading != null) { hx += Math.Cos(heading[i]); hz += Math.Sin(heading[i]); }
            }

            double nnSum = 0.0; int nnN = 0;
            for (int i = 0; i < count; i += stride)
            {
                double best = double.MaxValue;
                for (int j = 0; j < count; j++)
                {
                    if (j == i) continue;
                    double dx = px[i] - px[j], dy = py[i] - py[j], dz = pz[i] - pz[j];
                    double d2 = dx * dx + dy * dy + dz * dz;
                    if (d2 < best) best = d2;
                }
                if (best < double.MaxValue) { nnSum += Math.Sqrt(best); nnN++; }
            }

            return new Cohesion(
                Math.Sqrt(sumSq / count) / flen,
                (double)inside / count,
                nnN > 0 ? nnSum / nnN / flen : 0.0,
                heading != null ? Math.Sqrt(hx * hx + hz * hz) / count : 0.0);
        }

        /// <summary>Short label for the QC log.</summary>
        public static string Label(SchoolMode m)
        {
            switch (m)
            {
                case SchoolMode.Cluster: return "cluster";
                case SchoolMode.Stream:  return "stream";
                case SchoolMode.Vortex:  return "vortex";
                case SchoolMode.Tornado: return "tornado";
                case SchoolMode.Cone:    return "cone";
                case SchoolMode.ConeUp:  return "cone_up";
                case SchoolMode.Ball:    return "ball";
                default:                 return "?";
            }
        }
    }
}
