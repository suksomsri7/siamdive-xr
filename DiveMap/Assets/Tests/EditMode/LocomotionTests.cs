using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// Unit tests for how far animals roam.
    ///
    /// These numbers are the difference between a reef that feels alive and one where every
    /// creature patrols the same circle. The web's own comment records the bug: overwriting the
    /// per-species radius made "every species in a zone roam the same; lionfish hovered 85u,
    /// humpback capped at 200" — so the override is asserted, not just the defaults.
    /// </summary>
    public class LocomotionTests
    {
        private const double Mid = 0.5;   // neutral energy → no variation

        [Test]
        public void Stationary_BarelyMoves()
        {
            Locomotion.Result r = Locomotion.Derive(SpeciesGenome.ZoneReef, SpeciesGenome.DietGrazer,
                                                    big: false, stationary: true, Mid);
            Assert.AreEqual(8, r.RoamRadius, 0.01);
            Assert.AreEqual(0.15, r.SwimMultiplier, 0.001);
        }

        [Test]
        public void Stationary_WinsOverEveryOtherRule()
        {
            // A stationary predator is still an anchored thing, not a hunter.
            Locomotion.Result r = Locomotion.Derive(SpeciesGenome.ZonePelagic, SpeciesGenome.DietPredator,
                                                    big: true, stationary: true, Mid);
            Assert.AreEqual(8, r.RoamRadius, 0.01);
        }

        [Test]
        public void Big_RoamsWideAndUnhurried()
        {
            Locomotion.Result r = Locomotion.Derive(SpeciesGenome.ZonePelagic, SpeciesGenome.DietFilter,
                                                    big: true, stationary: false, Mid);
            Assert.AreEqual(330, r.RoamRadius, 0.01, "big beats zone");
            Assert.AreEqual(0.90, r.SwimMultiplier, 0.001);
        }

        [Test]
        public void Pelagic_PatrolsFarAndFast()
        {
            Locomotion.Result r = Locomotion.Derive(SpeciesGenome.ZonePelagic, SpeciesGenome.DietPlanktivore,
                                                    false, false, Mid);
            Assert.AreEqual(300, r.RoamRadius, 0.01);
            Assert.AreEqual(1.30, r.SwimMultiplier, 0.001);
        }

        [Test]
        public void Bottom_IsTerritorialAndSlow()
        {
            Locomotion.Result grazer = Locomotion.Derive(SpeciesGenome.ZoneBottom, SpeciesGenome.DietGrazer,
                                                         false, false, Mid);
            Assert.AreEqual(42, grazer.RoamRadius, 0.01);
            Assert.AreEqual(0.55, grazer.SwimMultiplier, 0.001);
        }

        [Test]
        public void Reef_StaysNearHome()
        {
            Locomotion.Result r = Locomotion.Derive(SpeciesGenome.ZoneReef, SpeciesGenome.DietGrazer,
                                                    false, false, Mid);
            Assert.AreEqual(85, r.RoamRadius, 0.01);
        }

        [Test]
        public void UnknownZone_FallsBackToMidwater()
        {
            Locomotion.Result r = Locomotion.Derive("nowhere", SpeciesGenome.DietGrazer, false, false, Mid);
            Assert.AreEqual(160, r.RoamRadius, 0.01);
            Assert.AreEqual(1.00, r.SwimMultiplier, 0.001);
        }

        // ── predators ────────────────────────────────────────────────────────────

        [Test]
        public void SmallPredator_HuntsWiderAndFaster()
        {
            Locomotion.Result r = Locomotion.Derive(SpeciesGenome.ZoneBottom, SpeciesGenome.DietPredator,
                                                    false, false, Mid);
            Assert.AreEqual(70 * 1.35, r.RoamRadius, 0.01);
            Assert.AreEqual(0.55 * 1.28, r.SwimMultiplier, 0.001);
        }

        [Test]
        public void BigPredator_GetsTheSmallerMultipliers()
        {
            // A shark is already fast; the web scales it less than a small hunter.
            Locomotion.Result r = Locomotion.Derive(SpeciesGenome.ZonePelagic, SpeciesGenome.DietPredator,
                                                    big: true, stationary: false, energy: Mid);
            Assert.AreEqual(330 * 1.15, r.RoamRadius, 0.01);
            Assert.AreEqual(0.90 * 1.22, r.SwimMultiplier, 0.001);
        }

        // ── the per-species override ─────────────────────────────────────────────

        [Test]
        public void ConfiguredRadius_WinsOverTheZoneDefault()
        {
            // The bug this guards: without it, every reef fish roamed 85u regardless of species.
            Locomotion.Result r = Locomotion.Derive(SpeciesGenome.ZoneReef, SpeciesGenome.DietGrazer,
                                                    false, false, Mid, configuredRoam: 210);
            Assert.AreEqual(210, r.RoamRadius, 0.01);
        }

        [Test]
        public void ConfiguredRadius_DoesNotChangeSwimSpeed()
        {
            Locomotion.Result r = Locomotion.Derive(SpeciesGenome.ZoneReef, SpeciesGenome.DietGrazer,
                                                    false, false, Mid, configuredRoam: 210);
            Assert.AreEqual(0.80, r.SwimMultiplier, 0.001, "the override is a radius, not a speed");
        }

        // ── personality ──────────────────────────────────────────────────────────

        [Test]
        public void Energy_VariesBothByTwentyPercent()
        {
            Locomotion.Result lazy = Locomotion.Derive(SpeciesGenome.ZoneReef, SpeciesGenome.DietGrazer,
                                                       false, false, energy: 0.0);
            Locomotion.Result keen = Locomotion.Derive(SpeciesGenome.ZoneReef, SpeciesGenome.DietGrazer,
                                                       false, false, energy: 1.0);
            Assert.AreEqual(85 * 0.8, lazy.RoamRadius, 0.01);
            Assert.AreEqual(85 * 1.2, keen.RoamRadius, 0.01);
            Assert.Less(lazy.SwimMultiplier, keen.SwimMultiplier);
        }

        [Test]
        public void Energy_OutOfRangeIsClamped()
        {
            Locomotion.Result a = Locomotion.Derive(SpeciesGenome.ZoneReef, "x", false, false, -5);
            Locomotion.Result b = Locomotion.Derive(SpeciesGenome.ZoneReef, "x", false, false, 0);
            Assert.AreEqual(b.RoamRadius, a.RoamRadius, 1e-9);
        }

        [Test]
        public void NothingEverReachesZero()
        {
            Locomotion.Result r = Locomotion.Derive(SpeciesGenome.ZoneReef, "x", false, true, 0.0);
            Assert.GreaterOrEqual(r.RoamRadius, 4.0, "a zero radius would pin the animal to a point");
            Assert.Greater(r.SwimMultiplier, 0.0);
        }

        // ── animation rate ───────────────────────────────────────────────────────

        [Test]
        public void AnimationRate_FollowsMovementNotTheOtherWayRound()
        {
            Assert.AreEqual(0.18, Locomotion.AnimationRate(0), 1e-9,
                            "a stopped fish still beats its tail — a frozen one reads as dead");
            Assert.AreEqual(0.34, Locomotion.AnimationRate(1), 1e-9);
            Assert.AreEqual(0.26, Locomotion.AnimationRate(0.5), 1e-9);
        }

        [Test]
        public void AnimationRate_ClampsItsInput()
        {
            Assert.AreEqual(0.18, Locomotion.AnimationRate(-3), 1e-9);
            Assert.AreEqual(0.34, Locomotion.AnimationRate(9), 1e-9);
        }
    }
}
