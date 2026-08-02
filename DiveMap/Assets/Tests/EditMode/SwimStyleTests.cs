using System;
using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// Unit tests for SwimStyle — the per-species swim table behind DM_FishWave.
    ///
    /// 🔴 "ปลาว่ายไม่สมจริง ไม่สมูท ตัวแข็งมาก" has now been reported three times. The first two
    /// fixes were one set of numbers applied to every animal in the sea, so what these tests pin
    /// is not "a number is 0.085" but the RELATIONSHIPS that decide whether an animal reads as
    /// swimming at all:
    ///
    ///   • the classification ORDER (a whale shark is a shark, a moray is not a ray),
    ///   • beat rate falling with size, so a whale is unhurried and a sardine is not,
    ///   • amplitude being a FRACTION of body length, so a 65-unit shark and a 4-unit scad are
    ///     the same fish at different sizes rather than one of them thrashing,
    ///   • the beat being integrable, so the rate can change without the tail teleporting,
    ///   • the bank being a pure, saturating function of the CURRENT turn rate, so the stuck
    ///     barrel-roll that bit the web build has nothing to accumulate in.
    /// </summary>
    public class SwimStyleTests
    {
        private const double Eps = 1e-9;

        // Sizes the marine pipeline actually draws these animals at (world units).
        private const double ScadLen      = 4.20;
        private const double BarracudaLen = 17.1;
        private const double WhaleSharkLen = 65.0;

        // ── Classification: the order is load-bearing ─────────────────────────────

        /// <summary>
        /// 🔴 THE regression this table was written for. "whaleshark" contains "whale", and the
        /// whale shark is the hero animal on the demo map — the one in the screenshot the user
        /// sent back. Classified as a fluke it beats its tail UP AND DOWN like a dolphin.
        /// It is a shark: vertical tail, sideways sweep.
        /// </summary>
        [Test]
        public void WhaleShark_IsAShark_NotAWhale()
        {
            Assert.AreEqual(SwimGait.Body, SwimStyle.GaitFor("msh:whaleshark"));
            Assert.AreEqual(SwimGait.Body, SwimStyle.GaitFor("Whale_Shark_xr0"));
            // …while an actual whale still flukes.
            Assert.AreEqual(SwimGait.Fluke, SwimStyle.GaitFor("msh:humpback_whale"));
            Assert.AreEqual(SwimGait.Fluke, SwimStyle.GaitFor("msh:sperm_whale"));
        }

        [Test]
        public void Moray_IsAnEel_NotARay()
        {
            Assert.AreEqual(SwimGait.Body, SwimStyle.GaitFor("msh:moray"));
            Assert.AreEqual(SwimGait.Body, SwimStyle.GaitFor("msh:giant_moray_eel"));
            Assert.IsFalse(SwimStyle.IsStill("msh:moray"), "an eel is the opposite of motionless");

            // An eel undulates over its whole length: several wavelengths, big amplitude.
            SwimWave eel = SwimStyle.For("msh:moray", 12.0);
            Assert.Greater(eel.Cycles, 1.5, "anguilliform = more than one wave on the body");
            Assert.Greater(eel.Amp, SwimStyle.For("msh:scad_school", 12.0).Amp);
        }

        [Test]
        public void Rays_Flap_AndLookalikesDoNot()
        {
            Assert.AreEqual(SwimGait.Wing, SwimStyle.GaitFor("msh:manta"));
            Assert.AreEqual(SwimGait.Wing, SwimStyle.GaitFor("msh:eagle_ray"));
            Assert.AreEqual(SwimGait.Wing, SwimStyle.GaitFor("msh:stingray"));
            Assert.AreEqual(SwimGait.Wing, SwimStyle.GaitFor("msh:ray"));

            // Looks like a ray, swims like a shark.
            Assert.AreEqual(SwimGait.Body, SwimStyle.GaitFor("msh:guitar_shark"));
            Assert.AreEqual(SwimGait.Body, SwimStyle.GaitFor("msh:sawfish"));
            Assert.AreEqual(SwimGait.Body, SwimStyle.GaitFor("msh:angel_shark"));
        }

        [Test]
        public void Turtles_Row_WithTheirForelimbs()
        {
            Assert.AreEqual(SwimGait.Wing, SwimStyle.GaitFor("msh:green_turtle"));
            Assert.AreEqual(SwimGait.Wing, SwimStyle.GaitFor("msh:hawksbill"));

            // A turtle's stroke is faster and much shallower than a manta's.
            SwimWave turtle = SwimStyle.For("msh:green_turtle", 12.0);
            SwimWave manta  = SwimStyle.For("msh:manta", 12.0);
            Assert.Greater(turtle.BeatHz, manta.BeatHz);
            Assert.Less(turtle.Amp, manta.Amp);
        }

        [Test]
        public void ThingsThatDoNotSwim_StayStill()
        {
            foreach (string id in new[] { "msh:crab", "msh:lobster", "msh:giant_clam",
                                          "msh:sea_urchin", "msh:anemone", "msh:seahorse" })
            {
                Assert.IsTrue(SwimStyle.IsStill(id), id);
                // A near-zero amplitude, or the reef floor wobbles.
                Assert.Less(SwimStyle.For(id, 3.0).Amp, 0.02, id);
            }

            Assert.IsFalse(SwimStyle.IsStill("school:scad"));
            Assert.IsFalse(SwimStyle.IsStill("msh:whaleshark"));
        }

        [Test]
        public void TheDemoMapsSpecies_AllClassify()
        {
            Assert.AreEqual(SwimGait.Body, SwimStyle.GaitFor("school:scad"));
            Assert.AreEqual(SwimGait.Body, SwimStyle.GaitFor("school:barracuda"));
            Assert.AreEqual(SwimGait.Body, SwimStyle.GaitFor("pod:yellowtail"));

            // A barracuda and a trevally are stiff-bodied cruisers: a smaller tail arc than an
            // ordinary reef fish, not the same generic wave the last two attempts gave everything.
            SwimWave barra = SwimStyle.For("school:barracuda", BarracudaLen);
            SwimWave scad  = SwimStyle.For("school:scad", ScadLen);
            Assert.Less(barra.Amp, scad.Amp, "thunniform bodies barely bend");

            SwimWave trevally = SwimStyle.For("pod:yellowtail", 5.0);
            Assert.AreEqual(SwimStyle.For("school:barracuda", 5.0).Amp, trevally.Amp, Eps,
                            "a yellowtail IS a trevally — same stiff-bodied cruiser");
        }

        // ── Tempo tracks size ─────────────────────────────────────────────────────

        [Test]
        public void BeatRate_FallsWithSize()
        {
            double scad  = SwimStyle.For("school:scad", ScadLen).BeatHz;
            double barra = SwimStyle.For("school:barracuda", BarracudaLen).BeatHz;
            double shark = SwimStyle.For("msh:whaleshark", WhaleSharkLen).BeatHz;

            Assert.Greater(scad, barra);
            Assert.Greater(barra, shark);

            // The eye's own yardstick: a hand-sized fish flickers, a bus-sized one does not.
            Assert.That(scad, Is.InRange(3.5, 7.0), "a 35 cm scad beats a few times a second");
            Assert.That(shark, Is.InRange(0.4, 1.6), "a whale shark is unhurried, not frozen");

            // 1/√L, so quadrupling the length halves the beat.
            double a = SwimStyle.For("school:scad", 4.0).BeatHz;
            double b = SwimStyle.For("school:scad", 16.0).BeatHz;
            Assert.AreEqual(0.5, b / a, 1e-6);
        }

        [Test]
        public void BeatRate_IsAlwaysAPositiveFiniteNumber()
        {
            foreach (double len in new[] { 0.0, -5.0, 1e-6, 1.0, 65.0, 5000.0 })
            foreach (string id in new[] { "school:scad", "msh:whaleshark", "msh:manta",
                                          "msh:humpback_whale", "msh:moray", "msh:crab", null })
            {
                SwimWave w = SwimStyle.For(id, len);
                Assert.Greater(w.BeatHz, 0.0, $"{id}@{len}");
                Assert.IsFalse(double.IsNaN(w.BeatHz) || double.IsInfinity(w.BeatHz), $"{id}@{len}");
                Assert.GreaterOrEqual(w.Amp, 0.0, $"{id}@{len}");
            }
        }

        // ── Amplitude is a fraction of length, not a world constant ───────────────

        /// <summary>
        /// 🔴 The scale question. A whale shark is drawn ~65 world units long and a scad ~4.2.
        /// Amplitude is quoted as a fraction of body length, so the shark's tail sweeps ~15× the
        /// scad's in world units and exactly the SAME fraction of itself — which is what makes
        /// the two look like the same animal at different sizes. A world-constant amplitude would
        /// either whip the scad in half or leave the shark rigid.
        /// </summary>
        [Test]
        public void Amplitude_IsRelativeToBodyLength()
        {
            SwimWave scad  = SwimStyle.For("school:scad", ScadLen);
            SwimWave shark = SwimStyle.For("msh:whaleshark", WhaleSharkLen);

            // Same animal drawn twice as big = twice the travel, same fraction.
            Assert.AreEqual(SwimStyle.For("msh:whaleshark", WhaleSharkLen * 2).Amp, shark.Amp, Eps,
                            "amplitude must not depend on the size it is drawn at");

            double scadWorld  = scad.Amp * ScadLen;
            double sharkWorld = shark.Amp * WhaleSharkLen;
            Assert.Greater(sharkWorld, scadWorld * 5, "a big animal moves its tail further");

            // …and neither one folds in half. Real fish reach 8-18 % of body length to one side.
            foreach (string id in new[] { "school:scad", "school:barracuda", "msh:whaleshark",
                                          "msh:humpback_whale", "msh:moray" })
                Assert.That(SwimStyle.For(id, 20.0).Amp, Is.InRange(0.05, 0.20), id);
        }

        // ── Beat phase: integrable, so the rate can change ────────────────────────

        /// <summary>
        /// The beat is integrated on the CPU rather than computed as sin(_Time.y · rate) in the
        /// shader. This is why: with the _Time form, raising the rate at t = 900 s moves the
        /// argument by hundreds of radians in one frame and the tail teleports. Integrating, the
        /// same rate change is a change of SLOPE and the tail keeps its position.
        /// </summary>
        [Test]
        public void BeatPhase_SurvivesARateChange()
        {
            const double dt = 1.0 / 60.0;

            double phase = 0.0;
            for (int i = 0; i < 54000; i++) phase += SwimStyle.BeatPhaseStep(2.0, dt); // 15 min

            double slow = SwimStyle.BeatPhaseStep(2.0, dt);
            double fast = SwimStyle.BeatPhaseStep(6.0, dt);   // fish bolts: 3× the beat
            Assert.AreEqual(3.0, fast / slow, 1e-9, "3× the rate is 3× the step");
            Assert.Less(fast, 1.0, "…and one frame is still a fraction of a radian, never a jump");

            // Compare against what the shader's old form would have done at the same moment.
            double tNow = 54000 * dt;
            double teleport = Math.Abs((tNow * 6.0 * 2 * Math.PI) - (tNow * 2.0 * 2 * Math.PI));
            Assert.Greater(teleport, 100.0,
                           "sin(_Time.y·rate) would have jumped this far — the bug being avoided");
        }

        [Test]
        public void BeatPhaseStep_IsWellBehaved()
        {
            Assert.AreEqual(2 * Math.PI, SwimStyle.BeatPhaseStep(1.0, 1.0), 1e-9);
            Assert.AreEqual(0.0, SwimStyle.BeatPhaseStep(0.0, 1.0), Eps);
            Assert.AreEqual(0.0, SwimStyle.BeatPhaseStep(-3.0, 1.0), Eps, "no running backwards");
            Assert.AreEqual(0.0, SwimStyle.BeatPhaseStep(3.0, -1.0), Eps, "no running backwards");
        }

        // ── Effort ────────────────────────────────────────────────────────────────

        [Test]
        public void Effort_IsOneAtCruise_AndBounded()
        {
            Assert.AreEqual(1.0, SwimStyle.Effort(10.0, 10.0), 1e-9, "the table's amplitudes are cruise values");

            Assert.Less(SwimStyle.Effort(2.0, 10.0), 1.0, "gliding");
            Assert.Greater(SwimStyle.Effort(25.0, 10.0), 1.0, "sprinting");

            // Never zero (a stopped fish sculls) and never unbounded (a dart is not a seizure).
            Assert.That(SwimStyle.Effort(0.0, 10.0), Is.InRange(0.3, 0.5));
            Assert.That(SwimStyle.Effort(1e6, 10.0), Is.InRange(2.0, 2.5));
            Assert.That(SwimStyle.Effort(-5.0, 10.0), Is.InRange(0.3, 0.5));
            Assert.That(SwimStyle.Effort(5.0, 0.0), Is.InRange(0.35, 2.2), "no divide by zero");
        }

        // ── Bank: pure, saturating, and unable to get stuck ───────────────────────

        [Test]
        public void Bank_LeansIntoTheTurn_AndSaturates()
        {
            double max = SwimStyle.For("school:scad", ScadLen).MaxBankRad;
            Assert.Greater(max, 0.0);

            Assert.AreEqual(0.0, SwimStyle.BankRad(0.0, max), Eps, "straight and level");

            double left  = SwimStyle.BankRad(+1.2, max);
            double right = SwimStyle.BankRad(-1.2, max);
            Assert.AreEqual(-left, right, 1e-12, "symmetric");
            Assert.Less(Math.Abs(left), max, "tanh eases up to the limit, never reaches it");

            // Harder turn = more lean, but it can never exceed the species' own limit.
            Assert.Greater(Math.Abs(SwimStyle.BankRad(3.0, max)), Math.Abs(left));
            foreach (double rate in new[] { 5.0, 50.0, 1e6, -1e6 })
                Assert.LessOrEqual(Math.Abs(SwimStyle.BankRad(rate, max)), max, $"rate {rate}");
        }

        /// <summary>
        /// 🔴 The anti-regression test. The web build's fish got stuck in a barrel roll because
        /// the roll was accumulated. This one is a pure function of the CURRENT turn rate: the
        /// same input always gives the same output, and a fish that stops turning is level again
        /// on the very next call, whatever it did before.
        /// </summary>
        [Test]
        public void Bank_CannotAccumulate()
        {
            double max = SwimStyle.For("school:scad", ScadLen).MaxBankRad;

            double once = SwimStyle.BankRad(2.0, max);
            for (int i = 0; i < 10000; i++)
                Assert.AreEqual(once, SwimStyle.BankRad(2.0, max), Eps, "pure function");

            Assert.AreEqual(0.0, SwimStyle.BankRad(0.0, max), Eps,
                            "stop turning and the roll is gone immediately");
            Assert.AreEqual(0.0, SwimStyle.BankRad(5.0, 0.0), Eps, "a species that never banks");
        }

        [Test]
        public void Bank_LimitsAreSpeciesSpecific()
        {
            // A fast, agile reef fish throws itself into a corner; a whale does not, and a crab
            // on the sand does not lean at all to speak of.
            double reef  = SwimStyle.For("school:scad", ScadLen).MaxBankRad;
            double whale = SwimStyle.For("msh:humpback_whale", 120.0).MaxBankRad;
            double crab  = SwimStyle.For("msh:crab", 3.0).MaxBankRad;

            Assert.Greater(reef, whale);
            Assert.Greater(whale, crab);
            Assert.Less(reef, Math.PI / 2, "leaning, not capsizing");
        }

        // ── Geometry helper ───────────────────────────────────────────────────────

        /// <summary>
        /// The mesh-space length the shader needs. Exact for an axis-aligned box, which is all
        /// mesh.bounds ever is — and bounds is the only measurement available, because a Draco
        /// mesh is non-readable and mesh.vertices throws.
        /// </summary>
        [Test]
        public void AxisExtent_MeasuresTheBoxAlongADirection()
        {
            // Nose→tail along +Z of a 2 × 1 × 8 box.
            Assert.AreEqual(8.0, SwimStyle.AxisExtent(2, 1, 8, 0, 0, 1), Eps);
            Assert.AreEqual(2.0, SwimStyle.AxisExtent(2, 1, 8, 1, 0, 0), Eps);
            Assert.AreEqual(1.0, SwimStyle.AxisExtent(2, 1, 8, 0, 1, 0), Eps);

            // Direction sign is irrelevant — a fish is as long facing either way.
            Assert.AreEqual(8.0, SwimStyle.AxisExtent(2, 1, 8, 0, 0, -1), Eps);

            // A rotated node: 45° in the XZ plane sees the box's diagonal extent.
            double r = Math.Sqrt(0.5);
            Assert.AreEqual(2 * r + 8 * r, SwimStyle.AxisExtent(2, 1, 8, r, 0, r), 1e-9);

            Assert.AreEqual(0.0, SwimStyle.AxisExtent(2, 1, 8, 0, 0, 0), Eps);
        }

        [Test]
        public void Metres_ConvertsDrawSizeToApparentSize()
        {
            // The two sizes the marine pipeline documents: a 4.20 u scad reads as ~35 cm and a
            // 17.1 u barracuda as ~1.5 m.
            Assert.AreEqual(0.35, SwimStyle.Metres(ScadLen), 0.01);
            Assert.AreEqual(1.43, SwimStyle.Metres(BarracudaLen), 0.05);
            Assert.AreEqual(1.0 / SwimStyle.UnitsPerMetre, SwimStyle.Metres(1.0), Eps);
            Assert.Greater(SwimStyle.Metres(0.0), 0.0, "never zero — it divides a beat rate");
            Assert.Greater(SwimStyle.Metres(-4.0), 0.0);
        }

        // ── Null / junk input ─────────────────────────────────────────────────────

        [Test]
        public void UnknownAndNullIds_GetAnOrdinaryFish()
        {
            SwimWave fallback = SwimStyle.For("", 5.0);
            Assert.AreEqual(SwimGait.Body, fallback.Gait);
            Assert.Greater(fallback.Amp, 0.0);

            Assert.AreEqual(SwimGait.Body, SwimStyle.GaitFor(null));
            Assert.IsFalse(SwimStyle.IsStill(null));
            Assert.AreEqual(fallback.Amp, SwimStyle.For(null, 5.0).Amp, Eps);
            Assert.AreEqual(fallback.Amp, SwimStyle.For("msh:some_new_fish_2027", 5.0).Amp, Eps);
        }

        /// <summary>
        /// The hero animals are wired up from the pivot GameObject's name
        /// (<c>Item_7_msh:whaleshark</c>), so classification has to survive the prefix.
        /// </summary>
        [Test]
        public void Classification_SurvivesTheSceneItemPrefix()
        {
            Assert.AreEqual(SwimGait.Body, SwimStyle.GaitFor("Item_7_msh:whaleshark"));
            Assert.AreEqual(SwimGait.Wing, SwimStyle.GaitFor("Item_12_msh:manta"));
            Assert.AreEqual(SwimStyle.For("msh:whaleshark", 65.0).Amp,
                            SwimStyle.For("Item_7_msh:whaleshark", 65.0).Amp, Eps);
        }
    }
}
