using System;
using System.Collections.Generic;
using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// Phase 2 goal 2 — "สัตว์ทุกชนิดมีสมองเป็นของตัวเอง". These assert the four properties the
    /// behaviour has to have before it is worth shipping, none of which a screenshot can show:
    ///
    ///   1. it is DETERMINISTIC per seed (so a QC run is reproducible and this file can exist),
    ///   2. it never TELEPORTS (the shoal is eased, never assigned),
    ///   3. it never leaves the MAP (structural, not a tuning accident),
    ///   4. fear has a TAIL — a shoal that was just charged does not go back to sightseeing.
    ///
    /// Where a number here disagrees with <see cref="FishMind"/>, the file is the specification and
    /// the test is the record of what it promised.
    /// </summary>
    public class FishMindTests
    {
        private const double Dt = 1.0 / 30.0;

        private static MindDomain BigDomain()
            => new MindDomain(0, 0, 400, 5, 230);

        private static MindPoi[] Wreck()
            => new[] { new MindPoi(60, 40, -30, 45) };

        /// <summary>Run one school forward and hand back everything the assertions need.</summary>
        private sealed class Log
        {
            public readonly List<MindState> States = new List<MindState>();
            public readonly List<double> Wariness = new List<double>();
            public readonly List<double[]> Targets = new List<double[]>();
            public readonly List<double> HomeSteps = new List<double>();
            public readonly List<double[]> Homes = new List<double[]>();
            public readonly List<int> Ticks = new List<int>();
            public Mind Final;
        }

        /// <summary>
        /// Step a school for <paramref name="seconds"/>, easing its live home toward whatever the
        /// mind wants at the fish's own cruise speed — exactly what FishSchoolSystem does.
        /// <paramref name="panicAt"/> supplies the panic for a given sim time.
        /// </summary>
        private static Log Run(string species, uint seed, double seconds,
                               MindDomain dom, MindPoi[] pois,
                               double ax = 0, double ay = 120, double az = 0,
                               double homeR = 80, double cruise = 10,
                               Func<double, double> panicAt = null)
        {
            MindTraits tr = FishMind.TraitsFor(species);
            var m = new Mind { Seed = seed, Poi = -1 };
            var log = new Log();

            double hx = ax, hy = ay, hz = az;
            int steps = (int)(seconds / Dt);
            for (int i = 0; i < steps; i++)
            {
                double t = i * Dt;
                double panic = panicAt != null ? panicAt(t) : 0.0;

                FishMind.Step(ref m, tr, dom, pois, pois != null ? pois.Length : 0,
                              ax, ay, az, homeR, panic, t, Dt, out MindState _);

                FishMind.EaseHome(hx, hy, hz, m.TX, m.TY, m.TZ, cruise * Dt,
                                  out double nx, out double ny, out double nz);
                double step = Math.Sqrt((nx - hx) * (nx - hx) + (ny - hy) * (ny - hy) + (nz - hz) * (nz - hz));
                hx = nx; hy = ny; hz = nz;

                log.States.Add(m.State);
                log.Wariness.Add(m.Wariness);
                log.Targets.Add(new[] { m.TX, m.TY, m.TZ });
                log.HomeSteps.Add(step);
                log.Homes.Add(new[] { hx, hy, hz });
                log.Ticks.Add(m.Tick);
            }
            log.Final = m;
            return log;
        }

        // ── 1. determinism ───────────────────────────────────────────────────────

        [Test]
        public void TheSameSeedReplaysTheSameSchool()
        {
            Log a = Run("school:barracuda", 12345u, 600, BigDomain(), Wreck());
            Log b = Run("school:barracuda", 12345u, 600, BigDomain(), Wreck());

            for (int i = 0; i < a.States.Count; i++)
            {
                Assert.AreEqual(a.States[i], b.States[i], $"state diverged at step {i}");
                Assert.AreEqual(a.Targets[i][0], b.Targets[i][0], 0.0, $"target.x diverged at step {i}");
                Assert.AreEqual(a.Targets[i][2], b.Targets[i][2], 0.0, $"target.z diverged at step {i}");
            }
        }

        [Test]
        public void DifferentSeedsGiveDifferentSchools()
        {
            Log a = Run("school:scad", 1u, 600, BigDomain(), Wreck());
            Log b = Run("school:scad", 2u, 600, BigDomain(), Wreck());

            int differing = 0;
            for (int i = 0; i < a.Targets.Count; i++)
                if (Math.Abs(a.Targets[i][0] - b.Targets[i][0]) > 1e-6) differing++;

            Assert.Greater(differing, a.Targets.Count / 2,
                "two schools seeded differently must not swim the same route — that was the old "
                + "sin(Time) wander, identical on every school on the map");
        }

        [Test]
        public void TheRandomDrawsAreFlatAndIndependentPerChannel()
        {
            // A biased hash would quietly turn "pick a direction" into "always go north-east".
            double sum = 0; int n = 0;
            for (int tick = 0; tick < 500; tick++)
                for (int ch = 0; ch < 8; ch++) { sum += FishMind.Rand01(7u, tick, ch); n++; }
            Assert.AreEqual(0.5, sum / n, 0.03, "mean of the draw should sit on 0.5");

            // Same tick, different channel must not correlate (angle vs. dwell of one decision).
            int same = 0;
            for (int tick = 0; tick < 500; tick++)
                if (Math.Abs(FishMind.Rand01(7u, tick, 6) - FishMind.Rand01(7u, tick, 9)) < 1e-6) same++;
            Assert.AreEqual(0, same, "channels must be independent");
        }

        // ── 2. it actually changes its mind ──────────────────────────────────────

        [Test]
        public void ASchoolChangesItsMindRepeatedly_AndBothWandersAndInvestigates()
        {
            Log log = Run("school:batfish", 99u, 900, BigDomain(), Wreck());

            int changes = 0;
            for (int i = 1; i < log.States.Count; i++)
                if (log.States[i] != log.States[i - 1]) changes++;

            Assert.GreaterOrEqual(changes, 8, "a 15-minute reef with one state is an aquarium ornament");
            Assert.IsTrue(log.States.Contains(MindState.Wander), "never wandered");
            Assert.IsTrue(log.States.Contains(MindState.Investigate), "never went to look at the wreck");
        }

        [Test]
        public void ADecisionIsHeldForItsDwell_NotReRolledEveryFrame()
        {
            Log log = Run("pod:yellowtail", 5u, 600, BigDomain(), null);

            // Longest run of one state must be at least the species' shortest dwell.
            int best = 1, run = 1;
            for (int i = 1; i < log.States.Count; i++)
            {
                run = log.States[i] == log.States[i - 1] ? run + 1 : 1;
                if (run > best) best = run;
            }
            MindTraits tr = FishMind.TraitsFor("pod:yellowtail");
            Assert.GreaterOrEqual(best * Dt, tr.DwellMin * 0.9,
                "a school that re-decides every frame has no intent, only noise");
        }

        // ── 3. species have different temperaments ───────────────────────────────

        [Test]
        public void ScadRangeFurtherThanBarracuda_InTheRatioTheTableStates()
        {
            // No POIs, so both species take the SAME branch on the SAME random draws (the branch
            // rolls are per decision index, not per second) and the only difference left between
            // them is RoamMul: scad 1.60 against barracuda 0.50. So the ratio is not "roughly
            // bigger" — it is exactly 3.2, decision by decision.
            var dom = new MindDomain(0, 0, 5000, 5, 400); // wide enough that nothing is clamped
            List<double> scad = WanderDistances("school:scad", 4242u, dom);
            List<double> barra = WanderDistances("school:barracuda", 4242u, dom);

            int n = Math.Min(scad.Count, barra.Count);
            Assert.Greater(n, 5, "not enough wanders to compare");
            for (int i = 0; i < n; i++)
                Assert.AreEqual(1.60 / 0.50, scad[i] / barra[i], 1e-9,
                    $"wander {i}: scad {scad[i]:F1} u vs barracuda {barra[i]:F1} u");
        }

        /// <summary>Distance from the placement of every Wander target the school chose, in order.</summary>
        private static List<double> WanderDistances(string species, uint seed, MindDomain dom)
        {
            Log log = Run(species, seed, 1800, dom, null, homeR: 80);
            var outp = new List<double>();
            for (int i = 1; i < log.States.Count; i++)
            {
                if (log.States[i] != MindState.Wander || log.Ticks[i] == log.Ticks[i - 1]) continue;
                double[] p = log.Targets[i];
                outp.Add(Math.Sqrt(p[0] * p[0] + p[2] * p[2]));
            }
            Assert.Greater(outp.Count, 3, $"{species} never wandered — nothing to compare");
            return outp;
        }

        [Test]
        public void BarracudaWalkItselfAroundTheWreck_ScadParkBesideIt()
        {
            Assert.Greater(FishMind.TraitsFor("school:barracuda").OrbitRate, 0.0);
            Assert.AreEqual(0.0, FishMind.TraitsFor("school:scad").OrbitRate, 0.0,
                "a fast shoal does not do a slow lap of the wreck — that is the barracuda's trick");

            Assert.IsTrue(OrbitSweepDegrees("school:barracuda", 31u) > 25.0,
                "a barracuda column investigating the wreck must move round it");
            Assert.AreEqual(0.0, OrbitSweepDegrees("school:scad", 77u), 1e-6,
                "a non-orbiter's target must hold still while it investigates");
        }

        /// <summary>
        /// Degrees the target swept around the POI during the longest single Investigate DECISION.
        /// Runs are cut on the decision counter, not on the state: a school is perfectly free to
        /// choose Investigate twice in a row, and stitching two visits together would read as a
        /// sweep even for a species that never moves once it arrives.
        /// </summary>
        private static double OrbitSweepDegrees(string species, uint seed)
        {
            MindPoi[] pois = Wreck();
            Log log = Run(species, seed, 1800, BigDomain(), pois);

            double best = 0, first = 0, last = 0;
            bool inRun = false;
            for (int i = 0; i < log.States.Count; i++)
            {
                bool fresh = i > 0 && log.Ticks[i] != log.Ticks[i - 1];
                bool inv = log.States[i] == MindState.Investigate;
                if (inRun && (!inv || fresh))
                {
                    double s1 = Math.Abs(MarineMath.DeltaAngle(first, last)) * 180.0 / Math.PI;
                    if (s1 > best) best = s1;
                    inRun = false;
                }
                if (!inv) continue;
                double a = Math.Atan2(log.Targets[i][2] - pois[0].Z, log.Targets[i][0] - pois[0].X);
                if (!inRun) { first = a; inRun = true; }
                last = a;
            }
            if (inRun)
            {
                double s2 = Math.Abs(MarineMath.DeltaAngle(first, last)) * 180.0 / Math.PI;
                if (s2 > best) best = s2;
            }
            return best;
        }

        [Test]
        public void AReefFishStaysNearItsStructure_APelagicOneDoesNot()
        {
            MindTraits reef = FishMind.TraitsFor("fish:clownfish");   // SpeciesGenome zone = reef
            MindTraits pel  = FishMind.TraitsFor("fish:sailfish");    // zone = pelagic
            Assert.Less(reef.RoamMul, pel.RoamMul * 0.5, "a reef fish does not cross the map");
            Assert.Greater(reef.Curiosity, pel.Curiosity, "a reef fish lives on the structure");
        }

        [Test]
        public void EverySpeciesGetsAUsableTemperament()
        {
            string[] ids =
            {
                "school:scad", "school:barracuda", "school:batfish", "pod:yellowtail",
                "fish:whaleshark", "fish:manta", "fish:moray", "fish:crab", "", "who:knows",
            };
            foreach (string id in ids)
            {
                MindTraits t = FishMind.TraitsFor(id);
                Assert.Greater(t.DwellMax, t.DwellMin, $"{id}: dwell range inverted");
                Assert.Greater(t.DwellMin, 0.5, $"{id}: dwell too short to read as intent");
                Assert.Greater(t.RoamMul, 0.0, $"{id}: cannot move at all");
                Assert.Greater(t.WarySeconds, 1.0, $"{id}: fear with no tail");
                Assert.IsTrue(t.Curiosity >= 0.0 && t.Curiosity <= 1.0, $"{id}: curiosity not a probability");
            }
        }

        // ── 4. fear has a tail ───────────────────────────────────────────────────

        [Test]
        public void APanicStartlesTheSchool_AndItRegroupsInsteadOfSnappingBack()
        {
            // Charged from t=30 to t=40, then nothing.
            Log log = Run("school:scad", 8u, 300, BigDomain(), Wreck(),
                          panicAt: t => (t >= 30.0 && t < 40.0) ? 0.8 : 0.0);

            int iStartle = log.States.IndexOf(MindState.Startle);
            Assert.Greater(iStartle, 0, "the school never noticed it was being charged");
            Assert.Less(iStartle * Dt, 31.0, "it took over a second to react");

            int iRegroup = log.States.IndexOf(MindState.Regroup);
            Assert.Greater(iRegroup, iStartle, "it must regroup, not resume");

            // Between the threat leaving and the end of the regroup there is no sightseeing.
            MindTraits tr = FishMind.TraitsFor("school:scad");
            int end = (int)((40.0 + FishMind.StartleMinSeconds + FishMind.RegroupSeconds(tr)) / Dt);
            for (int i = iRegroup; i < Math.Min(end, log.States.Count); i++)
                Assert.AreNotEqual(MindState.Investigate, log.States[i],
                    "a shoal that has just been charged does not go sightseeing — that is a goldfish");
        }

        [Test]
        public void WarinessDecaysOverTheSpeciesOwnTail_NotInstantly()
        {
            Log log = Run("pod:yellowtail", 3u, 300, BigDomain(), Wreck(),
                          panicAt: t => (t >= 20.0 && t < 25.0) ? 0.9 : 0.0);

            MindTraits tr = FishMind.TraitsFor("pod:yellowtail"); // WarySeconds = 16
            double At(double t) => log.Wariness[(int)(t / Dt)];

            Assert.AreEqual(0.9, At(24.5), 0.02, "still at full alert while it is being chased");
            Assert.Greater(At(30.0), 0.3, "five seconds later it is still nervous");
            Assert.Less(At(30.0), 0.9, "…but calming down");
            Assert.AreEqual(0.0, At(20.0 + 5.0 + tr.WarySeconds + 1.0), 1e-9,
                "and fully calm once its own tail has run out");
        }

        [Test]
        public void WhileWaryTheSchoolWillNotGoSightseeing()
        {
            Log log = Run("school:batfish", 21u, 400, BigDomain(), Wreck(),
                          panicAt: t => (t >= 50.0 && t < 60.0) ? 1.0 : 0.0);

            for (int i = 0; i < log.States.Count; i++)
                if (log.Wariness[i] >= FishMind.WaryBlocksCuriosity)
                    Assert.AreNotEqual(MindState.Investigate, log.States[i],
                        $"investigating at wariness {log.Wariness[i]:F2}");
        }

        [Test]
        public void AFlickeringThreatCannotStrobeTheStateMachine()
        {
            // Panic crossing the gate every other frame — the shape a diver hovering right on the
            // speed threshold actually produces.
            Log log = Run("school:scad", 4u, 120, BigDomain(), Wreck(),
                          panicAt: t => ((int)(t / Dt) % 2 == 0) ? 0.4 : 0.0);

            int changes = 0;
            for (int i = 1; i < log.States.Count; i++)
                if (log.States[i] != log.States[i - 1]) changes++;
            Assert.Less(changes, 20, "the minimum startle length is what stops this strobing");
        }

        // ── 5. no teleports, and never off the map ───────────────────────────────

        [Test]
        public void TheShoalsHomeNeverMovesFasterThanTheFishCouldSwim()
        {
            const double cruise = 10.0;
            Log log = Run("school:scad", 66u, 900, BigDomain(), Wreck(), cruise: cruise,
                          panicAt: t => (t % 90.0 < 6.0) ? 0.7 : 0.0);

            double cap = cruise * Dt + 1e-9;
            for (int i = 0; i < log.HomeSteps.Count; i++)
                Assert.LessOrEqual(log.HomeSteps[i], cap,
                    $"step {i} moved {log.HomeSteps[i]:F4} u in one frame, cap {cap:F4}");
        }

        [Test]
        public void NeitherTheTargetNorTheShoalEverLeavesTheMap()
        {
            // The demo map's own numbers: a 187 u seabed and the scad shoal's 79.2 u home radius,
            // placed out near the rim — the case that actually bites.
            var dom = new MindDomain(0, 0, 187, 5, 230);
            const double homeR = 79.2;
            Log log = Run("school:scad", 13u, 1800, dom, Wreck(), ax: 95, ay: 120, az: 40,
                          homeR: homeR, panicAt: t => (t % 120.0 < 8.0) ? 0.9 : 0.0);

            for (int i = 0; i < log.Targets.Count; i++)
            {
                double[] p = log.Targets[i];
                Assert.IsTrue(FishMind.InsideDomain(dom, homeR, p[0], p[1], p[2]),
                    $"target {i} = ({p[0]:F1},{p[1]:F1},{p[2]:F1}) is off the map");
                double[] h = log.Homes[i];
                Assert.IsTrue(FishMind.InsideDomain(dom, homeR, h[0], h[1], h[2]),
                    $"home {i} = ({h[0]:F1},{h[1]:F1},{h[2]:F1}) is off the map");
            }
        }

        [Test]
        public void ASchoolPlacedOffTheSandKeepsItsSpotButCannotDriftFurtherOut()
        {
            // 🔴 The regression this guards: clamping every school to (mapRadius − homeR) quietly
            // MIGRATES a shoal placed outside that ring inward on the first frame, and the reef the
            // author laid out rearranges itself while they watch. SchoolDomain grows the disc just
            // enough to contain the placement instead.
            const double map = 187, homeR = 79.2;
            MindDomain grown = FishMind.SchoolDomain(map, 5, 230, 150, 60, homeR);
            double placed = Math.Sqrt(150.0 * 150.0 + 60.0 * 60.0);

            Assert.AreEqual(placed + homeR, grown.Radius, 1e-9, "the placement must stay reachable");
            Assert.IsTrue(FishMind.InsideDomain(grown, homeR, 150, 120, 60),
                "the school's own placement is now legal");
            Assert.IsFalse(FishMind.InsideDomain(grown, homeR, 150 * 1.2, 120, 60 * 1.2),
                "…but it still cannot drift any further out than it already is");

            // A school placed INSIDE the ring is bounded by the map, not by its own placement.
            MindDomain normal = FishMind.SchoolDomain(map, 5, 230, 10, 10, homeR);
            Assert.AreEqual(map, normal.Radius, 1e-9);
        }

        [Test]
        public void ATargetOutsideTheDomainIsAimedBackIn_AndSwumBackNotTeleported()
        {
            // The clamp itself, and the ease that turns it into motion.
            var dom = new MindDomain(0, 0, 187, 5, 230);
            const double homeR = 79.2, cruise = 10.0;
            Log log = Run("school:scad", 13u, 600, dom, Wreck(), ax: 150, ay: 120, az: 60,
                          homeR: homeR, cruise: cruise);

            foreach (double[] p in log.Targets)
                Assert.IsTrue(FishMind.InsideDomain(dom, homeR, p[0], p[1], p[2]),
                    "the mind aimed off the map");

            foreach (double st in log.HomeSteps)
                Assert.LessOrEqual(st, cruise * Dt + 1e-9, "it was teleported back, not swum back");

            int settle = (int)(60.0 / Dt);
            for (int i = settle; i < log.Homes.Count; i++)
            {
                double[] h = log.Homes[i];
                Assert.IsTrue(FishMind.InsideDomain(dom, homeR, h[0], h[1], h[2]),
                    $"after a minute the shoal is still off the sand at ({h[0]:F1},{h[2]:F1})");
            }
        }

        [Test]
        public void EaseHomeLandsExactlyOnTheTargetRatherThanOvershooting()
        {
            FishMind.EaseHome(0, 0, 0, 3, 0, 4, 100, out double x, out double y, out double z);
            Assert.AreEqual(3.0, x, 1e-12);
            Assert.AreEqual(0.0, y, 1e-12);
            Assert.AreEqual(4.0, z, 1e-12);

            FishMind.EaseHome(0, 0, 0, 3, 0, 4, 2.5, out x, out y, out z);
            Assert.AreEqual(2.5, Math.Sqrt(x * x + y * y + z * z), 1e-12);
        }

        [Test]
        public void ClampToDomainKeepsTheWholeShoalInside_NotJustItsCentre()
        {
            var dom = new MindDomain(0, 0, 100, 0, 50);
            double x = 500, y = 900, z = 0;
            FishMind.ClampToDomain(dom, 30, ref x, ref y, ref z);
            Assert.AreEqual(70.0, x, 1e-9, "centre must stop a shoal-radius short of the rim");
            Assert.AreEqual(50.0, y, 1e-9);
        }

        // ── 6. the hero animal's patrol ──────────────────────────────────────────

        private const double Cruise = 4.7;      // WhaleController's own loop speed on the demo map
        private const double TurnRate = 0.25;   // rad/s
        private const double ArriveR = 32.0;
        private const double RoamR = 120.0;

        private sealed class PatrolLog
        {
            public readonly List<double[]> Pos = new List<double[]>();
            public readonly List<double> Speed = new List<double>();
            public readonly List<double> Heading = new List<double>();
            public readonly List<bool> NearPoi = new List<bool>();
            public int Legs;
        }

        private static PatrolLog RunPatrol(uint seed, double seconds, MindDomain dom, MindPoi[] pois)
        {
            var p = new Patrol { Seed = seed, X = 0, Y = 120, Z = 0 };
            FishMind.PatrolInit(ref p, seed, 0, 120, 0, dom, 0, 120, 0, RoamR,
                                pois, pois != null ? pois.Length : 0, ArriveR, Cruise);
            var log = new PatrolLog();
            int steps = (int)(seconds / Dt);
            for (int i = 0; i < steps; i++)
            {
                if (FishMind.PatrolStep(ref p, Dt, Cruise, TurnRate, dom, 0, 120, 0, RoamR,
                                        pois, pois != null ? pois.Length : 0, ArriveR))
                    log.Legs++;
                log.Pos.Add(new[] { p.X, p.Y, p.Z });
                log.Speed.Add(p.Speed);
                log.Heading.Add(p.Heading);
                log.NearPoi.Add(p.NearPoi);
            }
            return log;
        }

        [Test]
        public void ThePatrolIsContinuous_NoFrameMovesFurtherThanTheAnimalCouldSwim()
        {
            PatrolLog log = RunPatrol(1234u, 900, BigDomain(), Wreck());

            // Worst case: full breath-in speed, plus the 0.55× vertical the whole way.
            double top = Cruise * (1.0 + FishMind.BreathDepth);
            double cap = Math.Sqrt(1.0 + FishMind.VerticalFrac * FishMind.VerticalFrac) * top * Dt * 1.02;
            for (int i = 1; i < log.Pos.Count; i++)
            {
                double[] a = log.Pos[i - 1], b = log.Pos[i];
                double d = Math.Sqrt((b[0] - a[0]) * (b[0] - a[0]) +
                                     (b[1] - a[1]) * (b[1] - a[1]) +
                                     (b[2] - a[2]) * (b[2] - a[2]));
                Assert.LessOrEqual(d, cap, $"frame {i} jumped {d:F3} u (cap {cap:F3}) — that is a teleport");
            }
        }

        [Test]
        public void ThePatrolTurnsAtACappedRate_ItNeverPivotsOnTheSpot()
        {
            PatrolLog log = RunPatrol(88u, 900, BigDomain(), Wreck());
            double cap = TurnRate * Dt + 1e-9;
            for (int i = 1; i < log.Heading.Count; i++)
            {
                double d = Math.Abs(MarineMath.DeltaAngle(log.Heading[i - 1], log.Heading[i]));
                Assert.LessOrEqual(d, cap, $"frame {i} turned {d:F5} rad (cap {cap:F5})");
            }
        }

        [Test]
        public void ThePatrolStaysInTheWater()
        {
            var dom = new MindDomain(0, 0, 187, 20, 200);
            PatrolLog log = RunPatrol(7u, 1800, dom, Wreck());
            foreach (double[] p in log.Pos)
            {
                double r = Math.Sqrt(p[0] * p[0] + p[2] * p[2]);
                Assert.LessOrEqual(r, dom.Radius, $"swam {r:F1} u out — past the sand at {dom.Radius}");
                Assert.GreaterOrEqual(p[1], dom.MinY - 1e-9, "went under the seabed");
                Assert.LessOrEqual(p[1], dom.MaxY + 1e-9, "broke the surface");
            }
        }

        [Test]
        public void ThePatrolIsNotACircle_ItPicksNewWaypoints()
        {
            PatrolLog log = RunPatrol(55u, 900, BigDomain(), Wreck());
            Assert.GreaterOrEqual(log.Legs, 4, "15 minutes on one leg is the ellipse we just removed");

            // …and it does not retrace the same closed loop: the second half of the run must not sit
            // on top of the first half.
            int half = log.Pos.Count / 2;
            double best = double.MaxValue;
            for (int i = half; i < log.Pos.Count; i += 30)
            {
                double d = Math.Sqrt((log.Pos[i][0] - log.Pos[0][0]) * (log.Pos[i][0] - log.Pos[0][0]) +
                                     (log.Pos[i][2] - log.Pos[0][2]) * (log.Pos[i][2] - log.Pos[0][2]));
                if (d < best) best = d;
            }
            Assert.Greater(best, 1.0, "the path closed exactly onto its start — that is an orbit");
        }

        [Test]
        public void ThePatrolBreathes_ItsSpeedIsNotAConstant()
        {
            PatrolLog log = RunPatrol(9u, 300, BigDomain(), null); // no POI, so only the breath moves it
            double lo = double.MaxValue, hi = double.MinValue;
            foreach (double s in log.Speed) { if (s < lo) lo = s; if (s > hi) hi = s; }

            Assert.Greater(hi - lo, Cruise * 0.15, "a constant speed reads as a machine on rails");
            Assert.LessOrEqual(hi, Cruise * (1.0 + FishMind.BreathDepth) + 1e-6, "breathed harder than the table allows");
            Assert.Greater(lo, 0.0, "stopped dead");
        }

        [Test]
        public void ThePatrolSlowsDownBesideTheWreck()
        {
            PatrolLog log = RunPatrol(1234u, 1800, BigDomain(), Wreck());

            bool visited = false;
            double slowest = double.MaxValue, fastAway = 0;
            for (int i = 0; i < log.Speed.Count; i++)
            {
                if (log.NearPoi[i]) { visited = true; if (log.Speed[i] < slowest) slowest = log.Speed[i]; }
                else if (log.Speed[i] > fastAway) fastAway = log.Speed[i];
            }

            Assert.IsTrue(visited, "never went near the wreck in half an hour");
            Assert.Less(slowest, fastAway * 0.75, "it should visibly ease off beside something interesting");
        }

        [Test]
        public void ThePatrolIsDeterministic()
        {
            PatrolLog a = RunPatrol(2468u, 600, BigDomain(), Wreck());
            PatrolLog b = RunPatrol(2468u, 600, BigDomain(), Wreck());
            for (int i = 0; i < a.Pos.Count; i++)
            {
                Assert.AreEqual(a.Pos[i][0], b.Pos[i][0], 0.0, $"x diverged at {i}");
                Assert.AreEqual(a.Pos[i][1], b.Pos[i][1], 0.0, $"y diverged at {i}");
                Assert.AreEqual(a.Pos[i][2], b.Pos[i][2], 0.0, $"z diverged at {i}");
            }
            Assert.AreEqual(a.Legs, b.Legs);
        }

        [Test]
        public void TwoHeroAnimalsWithDifferentSeedsDoNotSwimInFormation()
        {
            PatrolLog a = RunPatrol(11u, 600, BigDomain(), Wreck());
            PatrolLog b = RunPatrol(12u, 600, BigDomain(), Wreck());
            int apart = 0;
            for (int i = 0; i < a.Pos.Count; i++)
            {
                double d = Math.Sqrt((a.Pos[i][0] - b.Pos[i][0]) * (a.Pos[i][0] - b.Pos[i][0]) +
                                     (a.Pos[i][2] - b.Pos[i][2]) * (a.Pos[i][2] - b.Pos[i][2]));
                if (d > 5.0) apart++;
            }
            Assert.Greater(apart, a.Pos.Count / 2, "two whale sharks must not fly in formation");
        }

        [Test]
        public void TheDemoMapsWhaleSharkPatrolsAtTheNumbersTheControllerActuallyComputes()
        {
            // WhaleController.Init for the HTMS Chang whale shark (worldLen 65.3, angularSpeed 0.10):
            //   radiusZ    = 65.3 × 0.72        = 47.02
            //   cruise     = 0.10 × 47.02       =  4.70 u/s
            //   turnRadius = 65.3 × 0.35        = 22.86 u
            //   turnRate   = cruise / turnRadius=  0.206 rad/s
            //   arriveR    = max(65.3×0.5, turnRadius×1.3) = 32.65 u
            //   roamR      = max(47.02×2, arriveR×3)       = 97.95 u
            // and SetWorld on that map gives a 187 u seabed with the surface at y = 240.
            const double size = 65.3, cruise = 4.702, turnRadius = size * 0.35;
            const double turnRate = cruise / turnRadius;
            const double arriveR = 32.65, roamR = 97.95;
            var dom = new MindDomain(0, 0, 187, size * 0.6, 240 - size * 0.6);
            MindPoi[] pois = { new MindPoi(0, 40, 0, 60) };   // the wreck, roughly

            // The tuning invariant that decides whether this is a patrol or an ellipse in disguise:
            // an animal that cannot turn tightly enough to reach its waypoint circles it forever.
            Assert.Greater(arriveR, turnRadius, "arrival radius must exceed the turn radius");
            Assert.GreaterOrEqual(roamR, arriveR * 3.0, "the roam must be a few arrivals across");

            var p = new Patrol { Seed = 4321u };
            FishMind.PatrolInit(ref p, 4321u, 60, 120, 20, dom, 60, 120, 20, roamR,
                                pois, pois.Length, arriveR, cruise);

            double px = p.X, py = p.Y, pz = p.Z, ph = p.Heading;
            int legs = 0, nearPoi = 0;
            double top = cruise * (1.0 + FishMind.BreathDepth);
            double cap = Math.Sqrt(1.0 + FishMind.VerticalFrac * FishMind.VerticalFrac) * top * Dt * 1.02;

            for (int i = 0; i < (int)(1800 / Dt); i++)
            {
                if (FishMind.PatrolStep(ref p, Dt, cruise, turnRate, dom, 60, 120, 20, roamR,
                                        pois, pois.Length, arriveR)) legs++;
                if (p.NearPoi) nearPoi++;

                double d = Math.Sqrt((p.X - px) * (p.X - px) + (p.Y - py) * (p.Y - py) + (p.Z - pz) * (p.Z - pz));
                Assert.LessOrEqual(d, cap, $"frame {i} jumped {d:F3} u");
                Assert.LessOrEqual(Math.Abs(MarineMath.DeltaAngle(ph, p.Heading)), turnRate * Dt + 1e-9,
                    $"frame {i} out-turned its own body");
                Assert.LessOrEqual(Math.Sqrt(p.X * p.X + p.Z * p.Z), dom.Radius, $"frame {i} left the sand");
                Assert.GreaterOrEqual(p.Y, dom.MinY - 1e-9, $"frame {i} went into the seabed");
                Assert.LessOrEqual(p.Y, dom.MaxY + 1e-9, $"frame {i} broke the surface");

                px = p.X; py = p.Y; pz = p.Z; ph = p.Heading;
            }

            Assert.GreaterOrEqual(legs, 6, "half an hour and it barely went anywhere");
            Assert.Greater(nearPoi, 0, "never once passed the wreck");
        }

        // ── 7. the effort the swim style reads off the patrol ────────────────────

        [Test]
        public void TheBreathReachesTheTailThroughSwimStyleEffort()
        {
            // A hero animal's tail beat is driven by SwimStyle.Effort(speed, cruise), so the breath
            // above must actually come out as a change in effort — otherwise the whole thing is
            // invisible.
            double fast = SwimStyle.Effort(Cruise * (1.0 + FishMind.BreathDepth), Cruise);
            double slow = SwimStyle.Effort(Cruise * FishMind.PoiSlowMul, Cruise);
            Assert.Greater(fast, slow + 0.15, "the beat must ease off when the animal does");
            Assert.Greater(slow, 0.0, "a coasting animal still sculls");
        }
    }
}
