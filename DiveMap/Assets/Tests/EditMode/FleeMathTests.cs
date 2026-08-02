using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// C5 — asserted against builder.html's own arithmetic, not against what looks reasonable.
    /// Where a number here disagrees with the web, the web is right and this app is wrong.
    /// </summary>
    public class FleeMathTests
    {
        // ── panic radius / level ─────────────────────────────────────────────────

        [Test]
        public void PanicRadii_MatchTheWebsFormulas()
        {
            // web :1692  panicR = spreadR*0.8 + flen*5
            Assert.AreEqual(60.0 * 0.8 + 3.0 * 5.0, FleeMath.PredatorPanicRadius(60, 3), 1e-9);
            // web :1700  dR = spreadR*0.85 + flen*6
            Assert.AreEqual(60.0 * 0.85 + 3.0 * 6.0, FleeMath.DiverPanicRadius(60, 3), 1e-9);
        }

        [Test]
        public void TheDiverScaresFishFromFurtherAwayThanAPredatorDoes()
        {
            // The drone is bigger and louder than the reef's own hunters — the web's own choice.
            Assert.Greater(FleeMath.DiverPanicRadius(60, 3), FleeMath.PredatorPanicRadius(60, 3));
        }

        [Test]
        public void PanicIsGradedByDistance_OneAtTheCentre_ZeroAtTheRim()
        {
            Assert.AreEqual(1.0, FleeMath.PanicLevel(0, 100), 1e-9);
            Assert.AreEqual(0.5, FleeMath.PanicLevel(50, 100), 1e-9);
            Assert.AreEqual(0.0, FleeMath.PanicLevel(100, 100), 1e-9, "at the rim there is no fear");
            Assert.AreEqual(0.0, FleeMath.PanicLevel(140, 100), 1e-9, "and none outside it");
        }

        [Test]
        public void ADegeneratePanicRadiusCannotDivideByZero()
        {
            Assert.AreEqual(0.0, FleeMath.PanicLevel(0, 0), 1e-9);
            Assert.AreEqual(0.0, FleeMath.PanicLevel(5, -3), 1e-9);
        }

        // ── the speed rule, which is the whole character of the reef ─────────────

        [Test]
        public void HoveringIsAccepted_ChargingIsNot()
        {
            double threshold = FleeMath.DiverPanicSpeed;
            Assert.IsFalse(FleeMath.DiverIsThreatening(0));
            Assert.IsFalse(FleeMath.DiverIsThreatening(threshold), "the web's test is strictly greater");
            Assert.IsTrue(FleeMath.DiverIsThreatening(threshold + 0.01));
            Assert.IsTrue(FleeMath.DiverIsThreatening(DroneFlight.Speed));
        }

        /// <summary>
        /// 🔴 The threshold is a FRACTION of the drone's top speed, not the web's literal 11 u/s.
        /// When the flight model was re-scaled to real metres (30 → 9 u/s) a hard 11 became
        /// unreachable: every unit test would still have passed and no fish would ever have fled
        /// again. This pins the relationship instead of the number.
        /// </summary>
        [Test]
        public void PanicSpeed_TracksTheDronesTopSpeed()
        {
            Assert.AreEqual(11.0 / 30.0, FleeMath.PanicSpeedFraction, 1e-9, "the web's own ratio");
            Assert.AreEqual(DroneFlight.Speed * FleeMath.PanicSpeedFraction, FleeMath.DiverPanicSpeed, 1e-9);

            Assert.Less(FleeMath.DiverPanicSpeed, DroneFlight.Speed,
                        "a drone that cannot reach the threshold can never frighten anything");
            Assert.Greater(FleeMath.DiverPanicSpeed, DroneFlight.Speed * 0.2,
                           "…and a threshold this low would leave the reef permanently panicked");

            // Hovering and sightseeing are tolerated; a charge is not.
            Assert.IsFalse(FleeMath.DiverIsThreatening(DroneFlight.Speed * 0.25));
            Assert.IsTrue(FleeMath.DiverIsThreatening(DroneFlight.Speed * 0.9));
        }

        [Test]
        public void ASlowDiverInsideTheRadiusCausesNoPanicAtAll()
        {
            double p = FleeMath.SchoolPanic(
                predatorDistance: 0, hasPredator: false,
                // Relative to the threshold, not a literal: the drone's top speed is a tuning
                // number and this test is about "drifting", which is whatever is below the line.
                diverDistance: 1, diverSpeed: FleeMath.DiverPanicSpeed * 0.5, diverActive: true,
                spreadR: 60, fishLen: 3);
            Assert.AreEqual(0.0, p, 1e-9, "drifting up to a shoal must not scatter it");
        }

        [Test]
        public void AChargingDiverInsideTheRadiusDoesCausePanic()
        {
            double p = FleeMath.SchoolPanic(
                predatorDistance: 0, hasPredator: false,
                // Full throttle — the fastest the drone can actually go, so this is a charge the
                // player can really perform rather than one only the old 30 u/s model could.
                diverDistance: 1, diverSpeed: DroneFlight.Speed, diverActive: true,
                spreadR: 60, fishLen: 3);
            Assert.Greater(p, 0.9);
        }

        [Test]
        public void APredatorOutranksTheDiver()
        {
            // Predator right on top of the shoal, diver charging further out: the shark wins.
            double p = FleeMath.SchoolPanic(
                predatorDistance: 0, hasPredator: true,
                diverDistance: 40, diverSpeed: 30, diverActive: true,
                spreadR: 60, fishLen: 3);
            Assert.AreEqual(1.0, p, 1e-9);
        }

        [Test]
        public void ADistantPredatorFallsThroughToTheDiverCheck()
        {
            // web: the drone branch runs only when the predator produced no panic (!S._panic)
            double p = FleeMath.SchoolPanic(
                predatorDistance: 9999, hasPredator: true,
                diverDistance: 0, diverSpeed: 30, diverActive: true,
                spreadR: 60, fishLen: 3);
            Assert.AreEqual(1.0, p, 1e-9);
        }

        [Test]
        public void OutOfTheTour_NothingScaresThem()
        {
            double p = FleeMath.SchoolPanic(0, false, 0, 99, diverActive: false, spreadR: 60, fishLen: 3);
            Assert.AreEqual(0.0, p, 1e-9);
        }

        // ── the scatter ──────────────────────────────────────────────────────────

        [Test]
        public void FleePush_MatchesTheWeb()
        {
            // web :1631  push = panic*(spreadR*0.18 + flen*1.6)
            Assert.AreEqual(0.5 * (60 * 0.18 + 3 * 1.6), FleeMath.FleePush(0.5, 60, 3), 1e-9);
            Assert.AreEqual(0.0, FleeMath.FleePush(0, 60, 3), 1e-9);
        }

        [Test]
        public void TheBurstStaysNearTheThreat_NotAcrossTheMap()
        {
            // The web tuned this down deliberately (comment dated 2026-07-09). A full-panic scatter
            // must stay well inside the shoal's own radius, or the shoal simply leaves.
            double push = FleeMath.FleePush(1.0, 60, 3);
            Assert.Less(push, 60 * 0.5, "a panicking shoal bursts apart, it does not evacuate");
        }

        [Test]
        public void FleeEase_IsCappedAtTheWebsCeiling()
        {
            // web :1632  L = min(0.22, panic*0.18)
            Assert.AreEqual(0.09, FleeMath.FleeEase(0.5), 1e-9);
            Assert.AreEqual(0.18, FleeMath.FleeEase(1.0), 1e-9);
            Assert.AreEqual(0.0, FleeMath.FleeEase(0.0), 1e-9);
        }

        [Test]
        public void DartSpeedRisesWithFearButStaysSwimmable()
        {
            Assert.AreEqual(1.0, FleeMath.DartSpeedScale(0), 1e-9);
            Assert.AreEqual(1.6, FleeMath.DartSpeedScale(1), 1e-9);
            Assert.AreEqual(1.6, FleeMath.DartSpeedScale(4), 1e-9, "clamped — no rocket fish");
        }

        [Test]
        public void AFrightenedFishTurnsHarderThanACruisingOne()
        {
            Assert.AreEqual(1.0, FleeMath.TurnCapScale(0), 1e-9, "calm fish keep the cruise cap");
            Assert.AreEqual(3.0, FleeMath.TurnCapScale(1), 1e-9);
            Assert.AreEqual(3.0, FleeMath.TurnCapScale(9), 1e-9, "clamped");
        }

        // ── bait ball ────────────────────────────────────────────────────────────

        [Test]
        public void TheShoalBallsUpOnlyUnderSustainedStrongFear()
        {
            Assert.IsFalse(FleeMath.ShouldBallUp(0.6, isPod: false), "the web's test is > 0.6");
            Assert.IsTrue(FleeMath.ShouldBallUp(0.61, isPod: false));
        }

        [Test]
        public void APodOfBigAnimalsNeverBallsUp()
        {
            // Dolphins and whales do not form bait balls; the web excludes pods explicitly (:1697).
            Assert.IsFalse(FleeMath.ShouldBallUp(1.0, isPod: true));
        }

        [Test]
        public void BallingUpShrinksTheShoal_AndCalmLeavesItAlone()
        {
            Assert.AreEqual(100.0, FleeMath.BallHomeRadius(100, 0), 1e-9);
            Assert.AreEqual(55.0, FleeMath.BallHomeRadius(100, 1), 1e-9);
            Assert.Less(FleeMath.BallHomeRadius(100, 1), FleeMath.BallHomeRadius(100, 0.5));
        }

        [Test]
        public void TheBallIsHeldAfterTheThreatPasses()
        {
            Assert.AreEqual(2.5, FleeMath.BallHoldSeconds, 1e-9);
        }

        // ── who is actually dangerous ────────────────────────────────────────────

        [Test]
        public void OnlyHigherRanksAreThreats()
        {
            Assert.IsTrue(FleeMath.IsThreat(myRank: 1, otherRank: 2, otherDiet: "predator"));
            Assert.IsFalse(FleeMath.IsThreat(myRank: 2, otherRank: 2, otherDiet: "predator"),
                           "an equal is not a predator");
            Assert.IsFalse(FleeMath.IsThreat(myRank: 3, otherRank: 1, otherDiet: "predator"));
        }

        [Test]
        public void FilterFeedersAreHarmlessNoMatterHowBig()
        {
            // A whale shark cruising a shoal of scad is the shot every diver wants. If this fails,
            // the shoal explodes and the map's centrepiece is ruined.
            Assert.IsFalse(FleeMath.IsThreat(myRank: 1, otherRank: 9, otherDiet: "filter"));
            Assert.IsFalse(FleeMath.IsThreat(myRank: 1, otherRank: 9, otherDiet: "FILTER"));
            Assert.IsTrue(FleeMath.IsThreat(myRank: 1, otherRank: 9, otherDiet: "predator"));
        }

        [Test]
        public void SenseRadius_MatchesTheWeb()
        {
            // web :1934  senseR = obsR*4.5 + 28
            Assert.AreEqual(6 * 4.5 + 28, FleeMath.SenseRadius(6), 1e-9);
        }

        // ── shelter ──────────────────────────────────────────────────────────────

        [Test]
        public void CalmFishDoNotRunForCover()
        {
            Assert.AreEqual(0.0, FleeMath.ShelterBias(0), 1e-9);
            Assert.AreEqual(0.0, FleeMath.ShelterLerp(0, hasShelter: true), 1e-9);
        }

        [Test]
        public void FrightenedFishHeadForCover_WhenThereIsAny()
        {
            Assert.Greater(FleeMath.ShelterLerp(1.0, hasShelter: true), 0.0);
            Assert.AreEqual(0.0, FleeMath.ShelterLerp(1.0, hasShelter: false), 1e-9,
                            "open water: nowhere to hide, so no bias at all");
        }

        [Test]
        public void TheShoalNeverAimsPastTheShelterIntoIt()
        {
            // A lerp of 1.0 would park every fish inside the coral. Cap it well below.
            Assert.Less(FleeMath.ShelterLerp(1.0, true), 0.7);
        }

        [Test]
        public void ArrivingAtCoverStopsTheChase()
        {
            Assert.IsTrue(FleeMath.AtShelter(distanceToShelter: 5, shelterR: 6));
            Assert.IsTrue(FleeMath.AtShelter(distanceToShelter: 6.5, shelterR: 6),
                          "a little past the rim still counts — the 15 % margin");
            Assert.IsFalse(FleeMath.AtShelter(distanceToShelter: 30, shelterR: 6));
            // 6 × 1.15 is 6.899999… in binary, so the edge itself is not a useful assertion;
            // what matters is that the margin exists and has roughly the right size.
            Assert.IsFalse(FleeMath.AtShelter(distanceToShelter: 7.5, shelterR: 6));
        }

        // ── refresh cadence ──────────────────────────────────────────────────────

        [Test]
        public void SensingIsThrottled_ItIsAnOnScanPerAnimal()
        {
            Assert.AreEqual(0.7, FleeMath.SenseIntervalSeconds, 1e-9);
            Assert.AreEqual(1.2, FleeMath.ShelterIntervalSeconds, 1e-9);
            Assert.Greater(FleeMath.ShelterIntervalSeconds, FleeMath.SenseIntervalSeconds,
                           "cover does not move; predators do");
        }
    }
}
