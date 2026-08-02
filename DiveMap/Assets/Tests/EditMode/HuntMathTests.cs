using System;
using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// C6 — hunger, pursuit and the gorge-rest cycle (builder.html:2179-2199, :2422-2429).
    ///
    /// The web ships its own oracle for this — <c>window.__huntTest(h)</c> at builder.html:1967,
    /// which sets a predator's hunger, drops prey inside its strike range and reports the sprint
    /// multiplier that came out. <see cref="HuntTestOracle"/> below is that same experiment, run
    /// against this port, and it is the test that would catch the formula being "tidied up".
    /// </summary>
    public class HuntMathTests
    {
        private const double ObsR = 5.0;
        private static double PreyR => HuntMath.PreyRadius(ObsR);   // 5·6 + 45 = 75

        private static HuntDrive Fed(double hunger) => new HuntDrive { Hunger = hunger };

        // ── hunger ───────────────────────────────────────────────────────────────

        [Test]
        public void HungerClimbsAndSaturates()
        {
            double h = 0.0;
            for (int i = 0; i < 100; i++) h = HuntMath.Hunger(h, 1.0, 0.1);
            Assert.Greater(h, 0.0);
            for (int i = 0; i < 100000; i++) h = HuntMath.Hunger(h, 1.0, 0.1);
            Assert.AreEqual(1.0, h, 1e-12, "hunger is a 0..1 drive, not an accumulator");
        }

        [Test]
        public void MetabolismSetsHowFastItGetsHungry()
        {
            // web :2179 — hunger + 0.0006·metab. The range is 0.6..1.25 (:1903), so a fast
            // metaboliser is hungry twice as often as a slow one. That is the only thing making
            // two sharks on the same reef hunt on different schedules.
            double slow = HuntMath.Hunger(0.0, 0.6, 1.0);
            double fast = HuntMath.Hunger(0.0, 1.25, 1.0);
            Assert.AreEqual(slow * (1.25 / 0.6), fast, 1e-12);
        }

        [Test]
        public void HungerIsFrameRateIndependent()
        {
            // 🔴 The web accrues per FRAME. Porting the constant without the frame rate makes a
            // shark on a 30 fps phone hungry half as fast as one on a 120 Hz tablet, and a
            // behaviour that depends on the device is not a behaviour.
            double big = HuntMath.Hunger(0.0, 1.0, 1.0);
            double small = 0.0;
            for (int i = 0; i < 60; i++) small = HuntMath.Hunger(small, 1.0, 1.0 / 60.0);
            Assert.AreEqual(big, small, 1e-9);
        }

        // ── the burst ────────────────────────────────────────────────────────────

        [Test]
        public void SprintIsViolentlyNonLinearInHunger()
        {
            // web :2192 — 2.8 − 0.4h + 17.6h². A starving shark is a different animal.
            Assert.AreEqual(2.8 - 0.4 * 0.15 + 17.6 * 0.15 * 0.15,
                            HuntMath.SprintMultiplier(0.15), 1e-12);
            Assert.AreEqual(20.0, HuntMath.SprintMultiplier(1.0), 1e-9);
            Assert.Greater(HuntMath.SprintMultiplier(1.0) / HuntMath.SprintMultiplier(0.15), 6.0,
                           "starving must be several times harder than comfortable");
        }

        [Test]
        public void SprintFloorsAtTheSatiationLevel()
        {
            // :2187 — max(0.15, hunger). A predator that has just eaten still has some drive, or
            // the chase collapses into a drift.
            Assert.AreEqual(HuntMath.SprintMultiplier(0.15), HuntMath.SprintMultiplier(0.0), 1e-12);
        }

        [Test]
        public void AmbushersEngageAtHalfTheRange()
        {
            // :2187 — the one multiplication that separates a lionfish from a marlin.
            Assert.AreEqual(PreyR, HuntMath.EngageRadius(PreyR, false), 1e-12);
            Assert.AreEqual(PreyR * 0.5, HuntMath.EngageRadius(PreyR, true), 1e-12);
        }

        [Test]
        public void BigAnimalsTurnLazily()
        {
            // :2189 — (big ? 0.14 : 0.2) · (0.7 + h).
            Assert.Less(HuntMath.TurnGain(true, 0.5), HuntMath.TurnGain(false, 0.5));
            Assert.Greater(HuntMath.TurnGain(false, 1.0), HuntMath.TurnGain(false, 0.2),
                           "a hungrier animal turns onto its prey harder");
        }

        // ── the step ─────────────────────────────────────────────────────────────

        [Test]
        public void ItAimsBeforeItSprints()
        {
            // 🔴 :2192 — the aim gate. Sprinting while still turning is what a missile does.
            HuntDrive d = Fed(0.8);
            // Prey dead ahead (+x) but the animal is facing −x: aim error π, well over 0.85.
            // 60 u: inside the 75 u engage radius but outside the 41 u strike radius, so the
            // phase under test is the aim gate and not the kill.
            HuntStep away = HuntMath.Step(ref d, true, 60.0, 0.0, Math.PI, ObsR, false, false, 1.0, 10.0, 0.016);
            Assert.AreEqual(HuntPhase.Stalk, away.Phase);
            Assert.Greater(away.TurnGain, 0.0, "it must still be turning onto the prey");

            HuntDrive e = Fed(0.8);
            HuntStep onIt = HuntMath.Step(ref e, true, 60.0, 0.0, 0.0, ObsR, false, false, 1.0, 10.0, 0.016);
            Assert.AreEqual(HuntPhase.Sprint, onIt.Phase);
            Assert.Greater(e.SprintMul, 5.0);
        }

        [Test]
        public void StrikingFeedsItAndSendsItAway()
        {
            // :2193 — the gorge-rest cycle. Without it a shark tractor-beams onto one shoal and
            // never leaves, which is the single most common way a predator reads as broken.
            HuntDrive d = Fed(0.9);
            double inside = HuntMath.StrikeRadius(PreyR) * 0.5;
            HuntStep s = HuntMath.Step(ref d, true, inside, 0.0, 0.0, ObsR, false, false, 1.0, 10.0, 0.0);

            Assert.AreEqual(HuntPhase.Strike, s.Phase);
            Assert.IsTrue(s.Fed);
            Assert.AreEqual(0.9 - HuntMath.FeedDrop, d.Hunger, 1e-9);
            Assert.AreEqual(10.0 + HuntMath.GorgeRestSeconds, d.WanderUntil, 1e-9);

            // …and it now ignores prey sitting right in front of it.
            HuntStep after = HuntMath.Step(ref d, true, inside, 0.0, 0.0, ObsR, false, false, 1.0, 11.0, 0.016);
            Assert.AreEqual(HuntPhase.Wander, after.Phase);
            Assert.IsFalse(after.Fed);
        }

        [Test]
        public void AChaseHasATimeLimit()
        {
            // :2196 — six seconds of pursuit without a kill and it patrols elsewhere. Prey that
            // can out-turn a shark forever is a perpetual-motion machine.
            HuntDrive d = Fed(0.5);
            double t = 0.0;
            // Just outside the strike radius, dead ahead, so it sprints and never connects.
            double dist = HuntMath.StrikeRadius(PreyR) + 5.0;
            HuntPhase last = HuntPhase.Idle;
            for (int i = 0; i < 500; i++)
            {
                t += 0.05;
                last = HuntMath.Step(ref d, true, dist, 0.0, 0.0, ObsR, false, false, 1.0, t, 0.05).Phase;
                if (last == HuntPhase.Wander) break;
            }
            Assert.AreEqual(HuntPhase.Wander, last, "it never gave up");
            Assert.LessOrEqual(t, HuntMath.PursuitLimitSeconds + 0.2);
        }

        [Test]
        public void OutOfRangeAndTooCloseBothMeanNoHunt()
        {
            HuntDrive d = Fed(0.9);
            Assert.AreEqual(HuntPhase.Idle,
                HuntMath.Step(ref d, true, PreyR * 2.0, 0.0, 0.0, ObsR, false, false, 1.0, 1.0, 0.016).Phase,
                "prey beyond the engage radius");

            HuntDrive e = Fed(0.9);
            Assert.AreEqual(HuntPhase.Idle,
                HuntMath.Step(ref e, true, HuntMath.MinEngageDistance * 0.5, 0.0, 0.0, ObsR, false, false, 1.0, 1.0, 0.016).Phase,
                "on top of it — :2188 bails out under 4 u rather than dividing by nothing");
        }

        [Test]
        public void NoPreyClearsThePursuitClock()
        {
            // :2199 — else if(predator){ pursueSince = 0 }. Without it the six-second limit
            // carries across a gap in the sensing and the next hunt is cut short at random.
            HuntDrive d = Fed(0.5);
            HuntMath.Step(ref d, true, 60.0, 0.0, 0.0, ObsR, false, false, 1.0, 1.0, 0.016);
            Assert.Greater(d.PursueSince, 0.0);
            HuntMath.Step(ref d, false, 0.0, 0.0, 0.0, ObsR, false, false, 1.0, 1.1, 0.016);
            Assert.AreEqual(0.0, d.PursueSince);
        }

        // ── fatigue and the patrol floor ─────────────────────────────────────────

        [Test]
        public void BurstSwimmingIsAnaerobic()
        {
            // :2424-2427 — nothing sprints across the whole map.
            double f = 0.0;
            for (int i = 0; i < 200; i++) f = HuntMath.Fatigue(f, true, 1.0 / 60.0);
            Assert.Greater(f, HuntMath.FatigueExhausted);
            Assert.AreEqual(HuntMath.ExhaustedBurst, HuntMath.BurstAfterFatigue(20.0, f), 1e-12);

            for (int i = 0; i < 400; i++) f = HuntMath.Fatigue(f, false, 1.0 / 60.0);
            Assert.AreEqual(0.0, f, 1e-12, "it recovers while cruising");
            Assert.AreEqual(20.0, HuntMath.BurstAfterFatigue(20.0, f), 1e-12);
        }

        [Test]
        public void PatrollingPredatorsAreAlreadyFast()
        {
            // :2429 — sprint = max(sprint, 5.0), except for ambushers.
            HuntDrive idle = default;
            Assert.AreEqual(HuntMath.PatrolSprintFloor, HuntMath.PredatorSpeedMul(idle, false, 0.0), 1e-12);
            Assert.AreEqual(1.0, HuntMath.PredatorSpeedMul(idle, true, 0.0), 1e-12,
                            "a scorpionfish that patrols at 5× is not a scorpionfish");
        }

        [Test]
        public void OnlyBenthicPredatorsMayRest()
        {
            // :2180 — a pursuit predator never rests, but a nurse shark genuinely sleeps on sand.
            Assert.IsFalse(HuntMath.MayRest(SpeciesGenome.DietPredator, false));
            Assert.IsTrue(HuntMath.MayRest(SpeciesGenome.DietPredator, true));
            Assert.IsTrue(HuntMath.MayRest(SpeciesGenome.DietPlanktivore, false));

            Assert.IsTrue(SpeciesBehavior.For("msh:nurse_shark").Benthic,
                          "…and the table has to actually say so");
            Assert.IsFalse(SpeciesBehavior.For("msh:tiger_shark").Benthic);
        }

        // ── the web's own oracle ─────────────────────────────────────────────────

        [Test]
        public void HuntTestOracle()
        {
            // builder.html:1967 window.__huntTest(h): set the hunger, put prey at 0.45·preyR on
            // +x and 0.25·preyR on +z, step, and read the sprint multiplier back.
            foreach (double h in new[] { 0.0, 0.15, 0.4, 0.7, 1.0 })
            {
                HuntDrive d = Fed(h);
                double px = PreyR * 0.45, pz = PreyR * 0.25;
                HuntStep s = HuntMath.Step(ref d, true, px, pz, Math.Atan2(pz, px),
                                           ObsR, false, false, 1.0, 100.0, 0.0);
                Assert.AreNotEqual(HuntPhase.Idle, s.Phase, $"h={h}");
                Assert.AreEqual(HuntMath.SprintMultiplier(h), d.SprintMul, 1e-9, $"h={h}");
            }
        }

        // ── the mind layer (FishMind.StepHunter) ────────────────────────────────

        private static MindDomain Wide() => new MindDomain(0, 0, 4000, 5, 400);

        [Test]
        public void AHungryPredatorEntersHunt()
        {
            var m = new Mind { Seed = 3u, Poi = -1 };
            var d = Fed(0.9);
            MindTraits tr = FishMind.TraitsFor("msh:tiger_shark");
            SpeciesGenome.Genome g = SpeciesGenome.For("msh:tiger_shark");
            var q = new FishMind.Quarry(true, 40.0, 60.0, 40.0, false, 0, 0, ObsR);

            FishMind.StepHunter(ref m, ref d, tr, Wide(), null, 0,
                                0, 60, 0, 0, 0, 0.0,
                                20.0, 0.0, q, g, false, false, 1.0, 0.016,
                                out _, out HuntStep hs);

            Assert.AreEqual(MindState.Hunt, m.State);
            Assert.AreEqual(HuntPhase.Sprint, hs.Phase);
            Assert.AreEqual(40.0, m.TX, 1e-6, "it steers at the prey, not at its own anchor");
        }

        [Test]
        public void FearOutranksHunger()
        {
            // 🔴 web :1957 — a predator with something bigger on it has NO prey. A shark calmly
            // eating while something eats it is the one thing that instantly reads as a bug.
            var m = new Mind { Seed = 3u, Poi = -1 };
            var d = Fed(1.0);
            MindTraits tr = FishMind.TraitsFor("msh:blacktip_reef_shark");
            SpeciesGenome.Genome g = SpeciesGenome.For("msh:blacktip_reef_shark");
            var q = new FishMind.Quarry(true, 40.0, 60.0, 40.0, true, 12.0, 6.0, ObsR);

            FishMind.StepHunter(ref m, ref d, tr, Wide(), null, 0,
                                0, 60, 0, 0, 0, 0.0,
                                20.0, 0.0, q, g, false, false, 1.0, 0.016,
                                out _, out HuntStep hs);

            Assert.AreEqual(HuntPhase.Idle, hs.Phase, "it must stop hunting");
            Assert.AreEqual(MindState.Evade, m.State);
            // Evading means going the OTHER way from the hunter, which sits at +x,+z.
            Assert.Less(m.TX, 0.0);
            Assert.Less(m.TZ, 0.0);
        }

        [Test]
        public void PanicStillWinsOverEverything()
        {
            // A full-blown startle owns the school; neither Hunt nor Evade may overwrite it.
            var m = new Mind { Seed = 3u, Poi = -1 };
            var d = Fed(1.0);
            MindTraits tr = FishMind.TraitsFor("msh:blacktip_reef_shark");
            SpeciesGenome.Genome g = SpeciesGenome.For("msh:blacktip_reef_shark");
            var q = new FishMind.Quarry(true, 40.0, 60.0, 40.0, true, 12.0, 6.0, ObsR);

            // Frame 1 primes the mind (Ready), frame 2 is the one under test.
            FishMind.StepHunter(ref m, ref d, tr, Wide(), null, 0, 0, 60, 0, 0, 0, 0.0,
                                20.0, 0.0, q, g, false, false, 1.0, 0.016, out _, out _);
            FishMind.StepHunter(ref m, ref d, tr, Wide(), null, 0, 0, 60, 0, 0, 0, 0.0,
                                20.0, 0.9, q, g, false, false, 1.0, 0.016, out _, out _);

            Assert.AreEqual(MindState.Startle, m.State);
        }

        [Test]
        public void ANonPredatorNeverHunts()
        {
            var m = new Mind { Seed = 3u, Poi = -1 };
            var d = Fed(1.0);
            MindTraits tr = FishMind.TraitsFor("school:scad");
            SpeciesGenome.Genome g = SpeciesGenome.For("school:scad");
            var q = new FishMind.Quarry(true, 40.0, 60.0, 40.0, false, 0, 0, ObsR);

            for (int i = 0; i < 50; i++)
                FishMind.StepHunter(ref m, ref d, tr, Wide(), null, 0, 0, 60, 0, 0, 0, 0.0,
                                    20.0, 0.0, q, g, false, false, 1.0, 0.05, out _, out HuntStep hs);

            Assert.AreNotEqual(MindState.Hunt, m.State);
        }

        [Test]
        public void HuntReleasesBackToOrdinaryLife()
        {
            // A stale Hunt is worse than no hunt: the shoal sits pointing at a fish that left.
            var m = new Mind { Seed = 5u, Poi = -1 };
            var d = Fed(0.9);
            MindTraits tr = FishMind.TraitsFor("msh:tiger_shark");
            SpeciesGenome.Genome g = SpeciesGenome.For("msh:tiger_shark");
            var near = new FishMind.Quarry(true, 40.0, 60.0, 40.0, false, 0, 0, ObsR);
            var gone = new FishMind.Quarry(false, 0, 0, 0, false, 0, 0, ObsR);

            FishMind.StepHunter(ref m, ref d, tr, Wide(), null, 0, 0, 60, 0, 0, 0, 0.0,
                                20.0, 0.0, near, g, false, false, 1.0, 0.016, out _, out _);
            Assert.AreEqual(MindState.Hunt, m.State);

            FishMind.StepHunter(ref m, ref d, tr, Wide(), null, 0, 0, 60, 0, 0, 0, 0.0,
                                20.0, 0.0, gone, g, false, false, 1.0, 0.016, out _, out _);
            Assert.AreNotEqual(MindState.Hunt, m.State);
            Assert.AreNotEqual(MindState.Evade, m.State);
        }

        [Test]
        public void LabelsAreOneToken()
        {
            foreach (HuntPhase p in new[] { HuntPhase.Idle, HuntPhase.Stalk, HuntPhase.Sprint,
                                            HuntPhase.Strike, HuntPhase.Wander })
            {
                string s = HuntMath.Label(p);
                Assert.IsFalse(string.IsNullOrEmpty(s));
                Assert.IsFalse(s.Contains(" "), s);
            }
            Assert.AreEqual("Hunt", FishMind.Label(MindState.Hunt));
            Assert.AreEqual("Evade", FishMind.Label(MindState.Evade));
        }
    }
}
