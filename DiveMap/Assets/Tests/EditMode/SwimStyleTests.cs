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

        // …and the rest of the placed sizes the calibration was done against. Every one of them is
        // the GLB's own ~1.91 u max dimension times the item scale the map stores (SceneBuilder's
        // MarineMath.WhaleWorldLen), which is why they are so much larger than defaultScale looks:
        // the whale shark is 1.908 × 34.2 ≈ 65 u. See WhaleController's note.
        private const double BullSharkLen = 32.0;
        private const double MantaLen     = 62.0;
        private const double TurtleLen    = 20.0;
        private const double LionfishLen  = 11.0;

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

            // A turtle's stroke is much shallower than a manta's — a fraction of the travel, over
            // a shorter fraction of the limb.
            SwimWave turtle = SwimStyle.For("msh:green_turtle", 12.0);
            SwimWave manta  = SwimStyle.For("msh:manta", 12.0);
            Assert.Less(turtle.Amp, manta.Amp);
            Assert.Less(turtle.Cycles, manta.Cycles);

            // 🔴 …and it is the faster of the two AT THE SIZES THEY ARE DRAWN, which is the only
            // comparison the eye ever makes. At equal length the manta now comes out marginally
            // faster (k 1.05 against the turtle's 0.7), because the two k's were calibrated
            // independently against their own placed sizes rather than against each other, and the
            // map never draws them at the same size: a turtle is 20 u and an oceanic manta 62 u.
            // Asserting the equal-size ordering instead would be pinning a number no user sees.
            Assert.Greater(SwimStyle.For("msh:turtle", TurtleLen).BeatHz,
                           SwimStyle.For("msh:oceanic_manta", MantaLen).BeatHz,
                           "as placed, the turtle rows faster than the manta flaps");
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
        public void TheHandTunedRowCanStillAnAnimalTheNameMisses()
        {
            // 🔴 builder.html's stationary:true rows. Two of these are unreachable from the name:
            //   • mdl:leafy_seadragon (:1858) — a seaDRAGON. "seahorse" does not match it, so
            //     without the table it swims about like an ordinary fish, which is the exact
            //     opposite of an animal whose entire survival strategy is looking like a weed.
            //   • losin:garden_eel (:1832) — the name list catches it the WRONG way: "eel"
            //     un-stills it and it gets a two-wavelength anguilliform swim. A colony of garden
            //     eels then undulates across the sand like a field of snakes.
            Assert.IsTrue(SwimStyle.IsStill("mdl:leafy_seadragon"));
            Assert.IsTrue(SwimStyle.IsStill("losin:garden_eel"));
            Assert.IsTrue(SwimStyle.IsStill("mdl:giant_clam"));
            Assert.IsTrue(SwimStyle.IsStill("losin:stonefish"));
            Assert.Less(SwimStyle.For("mdl:leafy_seadragon", 4.0).Amp, 0.02);
        }

        [Test]
        public void CoralfishIsAFish()
        {
            // 🔴 "coral" is in the still-list for the coral heads, and it also matches
            // mdl:coralfish — an ordinary reef fish the web gives speedMul 0.8 (:1853). Left
            // alone it came out with a 1.2 % tail amplitude and hung in the water like a decal.
            Assert.IsFalse(SwimStyle.IsStill("mdl:coralfish"));
            Assert.Greater(SwimStyle.For("mdl:coralfish", 4.0).Amp, 0.05);
            // …and anything that really is a coral head must still be still.
            Assert.IsTrue(SwimStyle.IsStill("msh:coral_head"));
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

            // The eye's own yardstick: a small fish flickers, a bus-sized one does not. The two
            // shoals are the web's own constants; the whale shark is the size law plus SlowAnim.
            Assert.AreEqual(SwimStyle.SchoolBeatHzDefault, scad, 1e-9, "the web's default wRate");
            Assert.That(shark, Is.InRange(0.18, 0.45), "a whale shark is unhurried, not frozen");

            // 1/√L, so quadrupling the length halves the beat. Asked of a SOLO animal: a shoal
            // ignores the size law on purpose, which is what the next assertion pins.
            double a = SwimStyle.For("mdl:bull_shark", 8.0).BeatHz;
            double b = SwimStyle.For("mdl:bull_shark", 32.0).BeatHz;
            Assert.AreEqual(0.5, b / a, 1e-6);

            // 🔴 …and a shoal does NOT fall with size, because the web's rate is a literal in the
            // shader and takes no argument at all (builder.html:1506). Every previous attempt at
            // this file tried to reproduce a constant with a size law.
            Assert.AreEqual(SwimStyle.For("school:scad", 4.0).BeatHz,
                            SwimStyle.For("school:scad", 400.0).BeatHz, Eps,
                            "a shoal's fin rate is the web's constant, whatever size it is drawn");
        }

        // ── The calibration itself: the acceptance test for the 2026-08-03 fin-rate work ──

        /// <summary>
        /// 🔴 THE test this round exists for. A real iPhone came back with "ครีบขยับเร็วไปมาก เป็น
        /// กับสัตว์ทุกตัว · ของเดิมบนเว็บดีกว่ามาก", and the investigation found two multiplying
        /// mistakes (UnitsPerMetre 12 against the project's 6, and k values set against nothing),
        /// which together ran 2.6-8.9× fast.
        ///
        /// Each row is an animal the map really places, at the size it is really drawn, against the
        /// rate agreed with the web — ±20 %, because these are perceptual targets and not physics.
        /// The two shoals are exact rather than approximate: they are the web's own literals.
        /// </summary>
        [Test]
        public void BeatRates_MatchTheCalibratedTargets()
        {
            // id, drawn length (u), target Hz
            var rows = new[]
            {
                Tuple.Create("school:scad",       ScadLen,       1.114),
                Tuple.Create("school:barracuda",  BarracudaLen,  0.796),
                Tuple.Create("mdl:bull_shark",    BullSharkLen,  0.45),
                Tuple.Create("msh:oceanic_manta", MantaLen,      0.36),
                Tuple.Create("msh:whaleshark",    WhaleSharkLen, 0.25),
                Tuple.Create("msh:turtle",        TurtleLen,     0.39),
            };

            foreach (var row in rows)
            {
                double hz = SwimStyle.For(row.Item1, row.Item2).BeatHz;
                double target = row.Item3;
                Assert.That(hz, Is.InRange(target * 0.8, target * 1.2),
                            $"{row.Item1} @{row.Item2:0.#}u = {hz:0.000} Hz, target {target:0.000} ±20%");
            }

            // A lionfish is the fastest thing on the reef and still must not flutter.
            Assert.LessOrEqual(SwimStyle.For("msh:lionfish", LionfishLen).BeatHz, 1.1,
                               "a lionfish hovers; its fins do not buzz");
        }

        /// <summary>
        /// Not an assertion — the calibration table itself, printed, in the same spirit as
        /// SpeciesCoverageTests.DumpAuditTable:
        ///
        ///     bash tools/test.sh --where "test =~ DumpBeatRates"
        ///
        /// One line per placed animal with the rate it actually resolves to, what it peaks at
        /// sprinting, and how far that is from the target. Run it when reviewing a change to the
        /// k values — a diff of the constants does not tell anyone what the fish will look like.
        /// <c>[Explicit]</c> so it never runs as part of the suite.
        /// </summary>
        [Test, Explicit]
        public void DumpBeatRates()
        {
            var rows = new[]
            {
                Tuple.Create("school:scad",        ScadLen,       1.114),
                Tuple.Create("school:barracuda",   BarracudaLen,  0.796),
                Tuple.Create("school:batfish",     5.70,          1.114),
                Tuple.Create("mdl:bull_shark",     BullSharkLen,  0.45),
                Tuple.Create("msh:oceanic_manta",  MantaLen,      0.36),
                Tuple.Create("msh:whaleshark",     WhaleSharkLen, 0.25),
                Tuple.Create("msh:turtle",         TurtleLen,     0.39),
                Tuple.Create("msh:lionfish",       LionfishLen,   -1.0),
                Tuple.Create("msh:barracuda",      16.0,          -1.0),
                Tuple.Create("msh:humpback_whale", 68.0,          -1.0),
                Tuple.Create("losin:moray_leopard", 12.0,         -1.0),
                Tuple.Create("pod:yellowtail",     20.8,          -1.0),
                Tuple.Create("pod:humpback",       24.0,          -1.0),
                Tuple.Create("msh:crab",           1.81,          -1.0),
            };

            Console.WriteLine($"{"species",-22} {"len(u)",7} {"gait",-6} {"beatHz",7} " +
                              $"{"peak",7} {"target",7} {"off%",7}");
            foreach (var r in rows)
            {
                SwimWave w = SwimStyle.For(r.Item1, r.Item2);
                double effortMax = SwimStyle.SchoolBeatHz(r.Item1) > 0.0
                                 ? SwimStyle.SchoolEffort : SwimStyle.EffortMax;
                string off = r.Item3 > 0.0
                           ? $"{(w.BeatHz / r.Item3 - 1.0) * 100.0,6:0.0}%"
                           : "     —";
                string tgt = r.Item3 > 0.0 ? $"{r.Item3,7:0.000}" : "      —";
                Console.WriteLine($"{r.Item1,-22} {r.Item2,7:0.00} {w.Gait,-6} {w.BeatHz,7:0.000} " +
                                  $"{w.BeatHz * effortMax,7:0.000} {tgt} {off}");
            }
        }

        /// <summary>
        /// The web's two shoal rates, transcribed rather than tuned — <c>wiggleRate</c> defaults to
        /// 7.0 rad/s (builder.html:1506) and <c>school:barracuda</c> overrides it to 5.0 (:1098).
        /// They are radians per second in the shader, so the Hz is that over 2π.
        /// </summary>
        [Test]
        public void ShoalsUseTheWebsConstant_AndPodsDoNot()
        {
            Assert.AreEqual(7.0 / (2 * Math.PI), SwimStyle.SchoolBeatHzDefault, 1e-12);
            Assert.AreEqual(5.0 / (2 * Math.PI), SwimStyle.SchoolBeatHzBarracuda, 1e-12);

            Assert.AreEqual(SwimStyle.SchoolBeatHzDefault,
                            SwimStyle.For("school:batfish", 6.0).BeatHz, Eps);
            Assert.AreEqual(SwimStyle.SchoolBeatHzDefault,
                            SwimStyle.For("school:parrotfish_prismatic", 6.0).BeatHz, Eps);
            Assert.AreEqual(SwimStyle.SchoolBeatHzBarracuda,
                            SwimStyle.For("school:barracuda", BarracudaLen).BeatHz, Eps);

            // …and it survives the scene item prefix, which is how a school id can reach here.
            Assert.AreEqual(SwimStyle.SchoolBeatHzDefault,
                            SwimStyle.For("Item_3_school:scad", ScadLen).BeatHz, Eps);

            // 🔴 A pod is NOT a shoal. builder.html only wiggles the instanced branch
            // (`instanced = !pod`, :1502), and a pod is a handful of real animals at natural size —
            // pod:humpback is two humpback whales. Handing them a sardine's 1.11 Hz would be the
            // same category error as the one this round is fixing, in the other direction.
            foreach (string id in new[] { "pod:humpback", "pod:orca", "pod:eagle_ray",
                                          "pod:yellowtail", "pod:hammerhead" })
                Assert.AreNotEqual(SwimStyle.SchoolBeatHzDefault, SwimStyle.For(id, 24.0).BeatHz, id);

            // Pods land where a big animal belongs: 24 u of whale is half a beat a second.
            Assert.That(SwimStyle.For("pod:humpback", 24.0).BeatHz, Is.InRange(0.3, 0.9));
            Assert.That(SwimStyle.For("pod:yellowtail", 20.8).BeatHz, Is.InRange(0.4, 1.0));
        }

        /// <summary>
        /// The whale shark's own row. It is the hero animal of the demo map and the one the web
        /// singles out (<c>slowAnim: true</c>, builder.html:1779) — its tail is supposed to look
        /// unhurried and grand, and it is also the animal whose screenshot came back as
        /// "ตัวแข็งเป็นแท่ง", so it may not be slowed into stillness either.
        /// </summary>
        [Test]
        public void TheWhaleSharkIsSlowedByItsOwnFlag_NotByAccident()
        {
            Assert.IsTrue(SpeciesBehavior.For("msh:whaleshark").SlowAnim, "the flag is the source");

            double slowed = SwimStyle.For("msh:whaleshark", WhaleSharkLen).BeatHz;
            double plain  = SwimStyle.For("msh:tiger_shark", WhaleSharkLen).BeatHz;

            Assert.AreEqual(SwimStyle.SlowAnimMul, slowed / plain, 1e-9,
                            "same gait, same size — the only difference is the flag");
            Assert.Less(slowed, plain);
            Assert.Greater(slowed, 0.15, "unhurried, not frozen");

            // No other animal in the manifest carries the flag, so nothing else may be slowed.
            Assert.AreEqual(SwimStyle.For("msh:tiger_shark", 32.0).BeatHz,
                            SwimStyle.For("mdl:bull_shark", 32.0).BeatHz, Eps);
        }

        /// <summary>
        /// 🔴 The regression guard, and the one assertion here that is about a CEILING rather than
        /// a target: whatever else changes, nothing in this sea may beat faster than 2.5 Hz even
        /// while sprinting. 2.5 is a little over twice the web's shoal rate — past it, fins read as
        /// vibration rather than swimming, which is the report this work started from.
        ///
        /// Effort is the multiplier that gets there: a solo animal at a dead sprint reaches
        /// <see cref="SwimStyle.EffortMax"/>, while a shoal is pinned at
        /// <see cref="SwimStyle.SchoolEffort"/> and cannot be pushed at all.
        /// </summary>
        [Test]
        public void NothingBeatsFasterThan2Point5Hz_EvenSprinting()
        {
            const double Ceiling = 2.5;

            var placed = new[]
            {
                Tuple.Create("school:scad",       ScadLen),
                Tuple.Create("school:barracuda",  BarracudaLen),
                Tuple.Create("school:batfish",    5.7),
                Tuple.Create("mdl:bull_shark",    BullSharkLen),
                Tuple.Create("msh:oceanic_manta", MantaLen),
                Tuple.Create("msh:whaleshark",    WhaleSharkLen),
                Tuple.Create("msh:turtle",        TurtleLen),
                Tuple.Create("msh:lionfish",      LionfishLen),
                Tuple.Create("msh:barracuda",     16.0),
                Tuple.Create("msh:humpback_whale", 68.0),
                Tuple.Create("losin:moray_leopard", 12.0),
                Tuple.Create("pod:yellowtail",    20.8),
                Tuple.Create("pod:humpback",      24.0),
            };

            foreach (var row in placed)
            {
                double effortMax = SwimStyle.SchoolBeatHz(row.Item1) > 0.0
                                 ? SwimStyle.SchoolEffort
                                 : SwimStyle.EffortMax;
                double peak = SwimStyle.For(row.Item1, row.Item2).BeatHz * effortMax;
                Assert.LessOrEqual(peak, Ceiling,
                                   $"{row.Item1} @{row.Item2:0.#}u peaks at {peak:0.00} Hz");
            }

            // …and the same over EVERY species in the manifest, at the size SpeciesCoverageTests
            // probes them with, so a new animal cannot arrive with a tempo nobody looked at.
            foreach (string id in SpeciesCoverageTests.Animals)
            {
                double effortMax = SwimStyle.SchoolBeatHz(id) > 0.0
                                 ? SwimStyle.SchoolEffort
                                 : SwimStyle.EffortMax;
                double peak = SwimStyle.For(id, 10.0).BeatHz * effortMax;
                Assert.LessOrEqual(peak, Ceiling, $"{id} @10u peaks at {peak:0.00} Hz");
            }
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
            Assert.AreEqual(SwimStyle.EffortMin, SwimStyle.Effort(0.0, 10.0), Eps);
            Assert.AreEqual(SwimStyle.EffortMax, SwimStyle.Effort(1e6, 10.0), Eps);
            Assert.AreEqual(SwimStyle.EffortMin, SwimStyle.Effort(-5.0, 10.0), Eps);
            Assert.That(SwimStyle.Effort(5.0, 0.0),
                        Is.InRange(SwimStyle.EffortMin, SwimStyle.EffortMax), "no divide by zero");

            // 🔴 The ceiling came down 2.2 → 2.0 as part of the fin-rate calibration: it is the
            // multiplier that decides how fast the fastest thing in the sea can possibly beat, and
            // NothingBeatsFasterThan2Point5Hz_EvenSprinting is what it is sized against.
            Assert.AreEqual(2.0, SwimStyle.EffortMax, Eps);

            // A shoal does not get one of these at all — see SchoolEffort.
            Assert.AreEqual(1.0, SwimStyle.SchoolEffort, Eps);
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
            // 🔴 6 units to the metre, not 12. The whole project has exactly one answer to this
            // question — DepthLight.UnitsPerMetre, transcribed from the web's U_PER_M (:600) — and
            // this file having a second one is what made every fin in the sea run √2 too fast.
            Assert.AreEqual(6.0, SwimStyle.UnitsPerMetre, Eps);
            Assert.AreEqual(DepthLight.UnitsPerMetre, SwimStyle.UnitsPerMetre, 1e-6,
                            "one project, one metre");

            // A 4.20 u scad is 0.70 m and a 17.1 u barracuda 2.85 m — which is the size the map
            // really draws them at, whatever the old comment claimed.
            Assert.AreEqual(0.70, SwimStyle.Metres(ScadLen), 0.01);
            Assert.AreEqual(2.85, SwimStyle.Metres(BarracudaLen), 0.05);
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
