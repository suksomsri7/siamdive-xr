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
            // 🔎 FromTables, not For: the two share the thunniform ROW, which is what this line is
            // about, but msh:barracuda has carried a hand-set override since 2026-08-06 (see
            // SwimStyle.SoloTune) and For() would be comparing the override against the row.
            Assert.AreEqual(SwimStyle.FromTables("msh:barracuda", 5.0).Amp, trevally.Amp, Eps,
                            "a yellowtail IS a trevally — same stiff-bodied cruiser");

            // 🔴 …and the SHOAL barracuda is deliberately NOT the same number any more. A pod is a
            // handful of real animals and stays on the thunniform row; school:barracuda is an
            // instanced shoal and takes the web's own wiggle literal (0.06 × 0.65 = 3.96 %), which
            // is a little over half what the size table was giving it. See SchoolWiggle.
            Assert.Less(SwimStyle.For("school:barracuda", 5.0).Amp, trevally.Amp,
                        "a shoal uses the web's literal, not the size table");
        }

        // ── The rest of the web's shoal wiggle (WO-F2, 2026-08-03) ───────────────

        /// <summary>
        /// 🔴 THE test this round exists for, and the reason there is a round at all.
        ///
        /// Build 244 shipped with the shoal BEAT RATE calibrated and independently measured in the
        /// real app (CI 30790730885 logged barracuda 0.80 Hz, scad 1.11 Hz — both exactly the
        /// web's wRate/2π), and the iPhone still came back with "ครีบเร็วมากๆ". A rate that
        /// matches and an eye that does not means the rate was one term out of four, and it was:
        /// `wAmp`, `wWave` and `wStiff` were still coming from the solo-animal size table.
        ///
        /// What the eye actually reads is TIP SPEED, amplitude × 2πf. At an identical rate, the
        /// old 7.5 % amplitude against the web's 3.96 % is a tail moving 1.92× as fast — which is
        /// "เร็วมาก" exactly, and which no Hz reading can show.
        /// </summary>
        [Test]
        public void ShoalWave_IsTheWebsWiggleLine_NotTheSizeTable()
        {
            // builder.html :1506 defaults and the :1098 barracuda override, verbatim.
            Assert.AreEqual(0.18, SwimStyle.SchoolWiggleAmpDefault, Eps);
            Assert.AreEqual(2.5,  SwimStyle.SchoolWiggleWaveDefault, Eps);
            Assert.AreEqual(0.5,  SwimStyle.SchoolWiggleStiffDefault, Eps);
            Assert.AreEqual(0.06, SwimStyle.SchoolWiggleAmpBarracuda, Eps);
            Assert.AreEqual(0.9,  SwimStyle.SchoolWiggleWaveBarracuda, Eps);
            Assert.AreEqual(0.15, SwimStyle.SchoolWiggleStiffBarracuda, Eps);

            SwimWave barra = SwimStyle.For("school:barracuda", BarracudaLen);
            SwimWave scad  = SwimStyle.For("school:scad", ScadLen);

            // AMPLITUDE = wAmp × (wStiff + 0.5), the value of the web's clamped tail envelope at
            // the tail (position.z ≈ −flen/2 — both school GLBs are modelled centred).
            Assert.AreEqual(0.06 * 0.65, barra.Amp, 1e-12);   // 3.96 % — was 7.5 %, i.e. 1.92× fast
            Assert.AreEqual(0.18 * 1.00, scad.Amp,  1e-12);   // 18 %

            // WAVELENGTHS. `position.z * wWave` is radians per MODEL UNIT: the tail envelope beside
            // it divides by flen, this term does not. Over a 1.8624 u barracuda that is 0.267
            // wavelengths — a plank flicking a tail. The size table was giving it 0.85, i.e. 3.19×
            // as many bends in the body, which reads as vibration rather than swimming.
            Assert.AreEqual(1.862 * 0.9 / (2 * Math.PI), barra.Cycles, 1e-3);
            Assert.AreEqual(1.911 * 2.5 / (2 * Math.PI), scad.Cycles,  1e-3);
            Assert.Less(barra.Cycles, 0.3, "ตัวแข็งสะบัดหาง — a stiff body, not a snake");
            Assert.Less(barra.Cycles, scad.Cycles, "a barracuda bends less than a scad");

            // The web has NO gust and NO recoil: uAmp is written once at build time and the tail
            // envelope is clamped at 0, so the nose never swings back.
            foreach (SwimWave w in new[] { barra, scad })
            {
                Assert.AreEqual(0.0, w.Gust, Eps);
                Assert.AreEqual(0.0, w.Recoil, Eps);
                Assert.AreEqual(SwimGait.Body, w.Gait);
            }
        }

        /// <summary>
        /// 🔴 The web NEVER rolls a school fish. The instanced school path writes
        /// <c>o.rotation.y</c> and nothing else — on the calm branch (builder.html :1599) and on
        /// the forward-only one (:1618). Banking is a POD thing (:1721), because a pod is a
        /// handful of real animals rather than an instanced shoal.
        ///
        /// The iPhone screenshot shows barracuda at every roll angle at once, which is a large
        /// part of why it reads as a disorganised scatter rather than a school.
        /// </summary>
        [Test]
        public void Schools_NeverBank_ButPodsAndSoloAnimalsDo()
        {
            foreach (string id in new[] { "school:barracuda", "school:scad", "school:batfish",
                                          "school:parrotfish_prismatic", "Item_7_school:scad" })
                Assert.AreEqual(0.0, SwimStyle.For(id, 12.0).MaxBankRad, Eps, id);

            Assert.Greater(SwimStyle.For("pod:yellowtail", 5.8).MaxBankRad, 0.0);
            Assert.Greater(SwimStyle.For("pod:humpback", 24.0).MaxBankRad, 0.0);
            Assert.Greater(SwimStyle.For("msh:whaleshark", WhaleSharkLen).MaxBankRad, 0.0);
        }

        /// <summary>
        /// A shoal's wave is a set of FRACTIONS, so drawing it bigger changes nothing about it —
        /// the same reason its beat rate does not fall with size. This also guards the one thing
        /// the conversion could plausibly get wrong: <see cref="SwimStyle.SchoolCycles"/> takes
        /// the GLB's LOCAL length (1.86 u), never the drawn world length (17 u), and confusing the
        /// two would give a barracuda 2.45 wavelengths of body wave.
        /// </summary>
        [Test]
        public void ShoalWave_DoesNotDependOnDrawSize()
        {
            foreach (string id in new[] { "school:barracuda", "school:scad" })
            {
                SwimWave small = SwimStyle.For(id, 2.0);
                SwimWave big   = SwimStyle.For(id, 200.0);
                Assert.AreEqual(small.Amp,    big.Amp,    Eps, id);
                Assert.AreEqual(small.Cycles, big.Cycles, Eps, id);
                Assert.AreEqual(small.BeatHz, big.BeatHz, Eps, id);
                Assert.Less(big.Cycles, 1.0, $"{id}: cycles came from the world length");
            }

            // A pod is not a shoal and must still track its size (builder.html :1502 — only the
            // instanced branch gets the vertex wiggle at all).
            Assert.IsFalse(SwimStyle.SchoolWiggle("pod:dolphin", out _, out _, out _));
            Assert.IsFalse(SwimStyle.SchoolWiggle("msh:whaleshark", out _, out _, out _));
            Assert.IsTrue(SwimStyle.SchoolWiggle("Item_2_school:scad", out double a, out _, out _));
            Assert.AreEqual(0.18, a, Eps, "the scene-item prefix must not hide the shoal");
        }

        /// <summary>
        /// The eye's own yardstick: peak tail-tip SPEED, amplitude × 2πf, in body lengths per
        /// second. This is what "ครีบเร็วมาก" is a statement about, and it is what the beat-rate
        /// calibration alone could not fix.
        /// </summary>
        [Test]
        public void ShoalTipSpeed_MatchesTheWeb()
        {
            // Web: flen·wAmp·(wStiff+0.5) metres of travel at wRate rad/s ⇒ tip speed in body
            // lengths per second is simply wAmp·(wStiff+0.5)·wRate.
            double webBarra = 0.06 * 0.65 * 5.0;   // 0.195 L/s
            double webScad  = 0.18 * 1.00 * 7.0;   // 1.260 L/s

            SwimWave barra = SwimStyle.For("school:barracuda", BarracudaLen);
            SwimWave scad  = SwimStyle.For("school:scad", ScadLen);

            Assert.AreEqual(webBarra * SwimStyle.UserSlowMulBarracudaTrevally,
                            barra.Amp * 2 * Math.PI * barra.BeatHz, 1e-9);   // B3
            // scad เข้ากลุ่ม UserSlow แล้ว (user 9 ส.ค.) — ความเร็วปลายหางจึงเป็นสัดส่วนของเว็บ
            // ตามตัวคูณเดียวกัน ไม่ใช่ค่าเว็บดิบ · amp/cycles ยังเท่าเว็บเป๊ะ (ห้ามแตะรูปคลื่น)
            Assert.AreEqual(webScad * SwimStyle.UserSlowMulScad,
                            scad.Amp * 2 * Math.PI * scad.BeatHz, 1e-9);

            // …and the old table's barracuda, for the record: 0.075 at the same rate = 1.92× fast.
            Assert.AreEqual(0.075 / (0.06 * 0.65), (0.075 * 5.0) / webBarra, 1e-9);
            Assert.AreEqual(1.923, (0.075 * 5.0) / webBarra, 0.001);
        }

        // ── Tempo tracks size ─────────────────────────────────────────────────────

        [Test]
        public void BeatRate_FallsWithSize()
        {
            double scad  = SwimStyle.For("school:scad", ScadLen).BeatHz;
            double barra = SwimStyle.For("school:barracuda", BarracudaLen).BeatHz;
            double shark = SwimStyle.For("msh:whaleshark", WhaleSharkLen).BeatHz;

            Assert.Greater(scad, barra);
            // barra > shark ถอดโดยตั้งใจ: B3 (user 8 ส.ค.) กดบาราคูด้าเหลือ 25% —
            // ช้ากว่าฉลามวาฬได้ เพราะเป็นคำสั่งตรงจากตาเครื่องจริง ไม่ใช่กฎขนาดตัว

            // The eye's own yardstick: a small fish flickers, a bus-sized one does not. The two
            // shoals are the web's own constants; the whale shark is the size law plus SlowAnim.
            Assert.AreEqual(SwimStyle.SchoolBeatHzDefault * SwimStyle.UserSlowMulScad,
                            scad, 1e-9, "the web's default wRate × the user's slow-down");
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
                // 🔴 ทั้งสองแถวไม่ใช่ "ค่าเว็บ" อีกต่อไป — user สั่งช้าลงจากตาบนเครื่องจริง
                // (8 ส.ค. บาราคูด้า+กะมง · 9 ส.ค. เพิ่ม scad และช้าลงอีกทั้งกลุ่ม)
                // ค่าเว็บดั้งเดิมยังอยู่ที่ SchoolBeatHzDefault/Barracuda ตัวคูณคือ UserSlow*
                Tuple.Create("school:scad",       ScadLen,       1.114 * SwimStyle.UserSlowMulScad),
                Tuple.Create("school:barracuda",  BarracudaLen,  0.796 * SwimStyle.UserSlowMulBarracudaTrevally),
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
            Assert.AreEqual(SwimStyle.SchoolBeatHzBarracuda * SwimStyle.UserSlowMulBarracudaTrevally,
                            SwimStyle.For("school:barracuda", BarracudaLen).BeatHz, Eps);   // B3

            // …and it survives the scene item prefix, which is how a school id can reach here.
            // scad อยู่ในกลุ่ม UserSlow แล้ว (user 9 ส.ค.) จึงคูณตัวคูณเดียวกัน — ที่ทดสอบตรงนี้
            // คือ "prefix ไม่ทำให้จำสายพันธุ์ไม่ได้" ไม่ใช่ตัวเลขเว็บ
            Assert.AreEqual(SwimStyle.SchoolBeatHzDefault * SwimStyle.UserSlowMulScad,
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
            // พื้นเดิม 0.4 → 0.05 → 0.02: user กดกะมงลงอีกรอบ 9 ส.ค. (0.25 → 0.12)
            Assert.That(SwimStyle.For("pod:yellowtail", 20.8).BeatHz, Is.InRange(0.01, 1.0));
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

            // …and nothing folds in half. Real fish reach 8-18 % of body length to one side; the
            // web's own stiff-bodied barracuda sits below that band ON PURPOSE (0.06 × 0.65 =
            // 3.96 %, builder.html :1098 — "ตัวแข็งสะบัดหาง"), so the floor is 3 % rather than 5 %.
            foreach (string id in new[] { "school:scad", "school:barracuda", "msh:whaleshark",
                                          "msh:humpback_whale", "msh:moray" })
                Assert.That(SwimStyle.For(id, 20.0).Amp, Is.InRange(0.03, 0.20), id);
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
            // 🔴 A SOLO reef fish, not a shoal: the web never rolls an instanced school fish
            // (builder.html :1599/:1618 write rotation.y and nothing else), so school:* now
            // reports a zero bank — see SchoolsNeverBank below.
            double max = SwimStyle.For("losin:moorish_idol", 11.5).MaxBankRad;
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
            double max = SwimStyle.For("losin:moorish_idol", 11.5).MaxBankRad;

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
            double reef  = SwimStyle.For("losin:moorish_idol", 11.5).MaxBankRad;
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

        // ── The solo barracuda (2026-08-06, build 280) ────────────────────────────
        //
        // user, on a real iPhone: "บาราคูด้าโบกหางเร็วไปมากและแคบไป".
        // The acceptance test for that sentence is SoloBarracuda_IsSlowerAndWiderThanBuild280 —
        // the rest of this block says where each of the three numbers came from.

        /// <summary>The generic thunniform row, i.e. what build 280 was drawing.</summary>
        private const double Build280Amp = 0.075;
        private const double Build280Cycles = 0.85;

        /// <summary>The size the map draws it at (QcModelShot's own figure).</summary>
        private const double SoloBarracudaLen = 14.57;

        /// <summary>
        /// 🔴 The user's sentence, both halves of it, as one inequality each.
        ///
        /// "เร็วไปมาก" → the beat comes down. "แคบไป" → the sweep goes up. And a third assertion
        /// that is not in the sentence but is the reason the first two do not fight: tail-tip
        /// SPEED (amp × 2πf) must not rise, or a wider sweep would simply have re-introduced the
        /// speed complaint from the other direction.
        /// </summary>
        [Test]
        public void SoloBarracuda_IsSlowerAndWiderThanBuild280()
        {
            SwimWave was = SwimStyle.FromTables("msh:barracuda", SoloBarracudaLen);
            SwimWave now = SwimStyle.For("msh:barracuda", SoloBarracudaLen);

            Assert.AreEqual(Build280Amp, was.Amp, Eps, "the row build 280 used");
            Assert.AreEqual(Build280Cycles, was.Cycles, Eps, "…and its wavelength count");

            Assert.Less(now.BeatHz, was.BeatHz * 0.55, "เร็วไปมาก — at least ~2× slower");
            Assert.Greater(now.Amp, was.Amp * 1.8, "แคบไป — the sweep must be visibly wider");

            double tipWas = was.Amp * 2.0 * Math.PI * was.BeatHz;
            double tipNow = now.Amp * 2.0 * Math.PI * now.BeatHz;
            Assert.LessOrEqual(tipNow, tipWas,
                               "a wider sweep must not buy back the speed the user complained about");
        }

        /// <summary>
        /// …and "แคบ" is a WAVELENGTH problem before it is an amplitude one. At 0.85 wavelengths
        /// the body carries most of a full S-bend and ripples in tight arcs that partly cancel
        /// along its length; the web's barracuda is a stiff plank whose whole back half swings
        /// together. This pins that the solo fish now bends the way the web's barracuda bends.
        /// </summary>
        [Test]
        public void SoloBarracuda_BendsLikeTheWebsBarracuda_NotLikeAGenericTunnyfish()
        {
            SwimWave solo = SwimStyle.For("msh:barracuda", SoloBarracudaLen);
            SwimWave shoal = SwimStyle.For("school:barracuda", BarracudaLen);

            Assert.AreEqual(shoal.Cycles, solo.Cycles, Eps,
                            "same species, same body wave: builder.html:1098 wiggleWave 0.9");
            Assert.AreEqual(0.267, solo.Cycles, 0.005, "1.8624 u × 0.9 / 2π");
            Assert.Less(solo.Cycles, Build280Cycles / 3.0, "3.19× fewer bends than build 280");
        }

        /// <summary>
        /// The beat is the web's own hand-tuned figure for a barracuda (wiggleRate 5.0 → 0.796 Hz,
        /// builder.html:1098), halved. Both facts are pinned, because "halved" without saying
        /// halved-from-what is how a tuned number turns into a magic one three months later.
        ///
        /// It also has to land between the web's TWO answers for this fish: 0.796 Hz as a shoal,
        /// and ~0.22 Hz as a solo GLB with no clip (`animateGLB()`'s `dart` default, :3603,
        /// <c>ry += sin(T*1.4)</c> with <c>T = t·sp</c> and <c>sp = 0.6…1.4</c>). The user is
        /// looking at the solo one, so ending up nearer the shoal figure than the dart figure
        /// would be answering with the wrong reference.
        /// </summary>
        [Test]
        public void SoloBarracuda_BeatIsTheWebsShoalFigureHalved()
        {
            double solo = SwimStyle.For("msh:barracuda", SoloBarracudaLen).BeatHz;

            Assert.AreEqual(SwimStyle.SchoolBeatHzBarracuda * 0.5 * SwimStyle.UserSlowMulBarracudaTrevally, solo, Eps);
            Assert.AreEqual(0.398 * SwimStyle.UserSlowMulBarracudaTrevally, solo, 0.002, "5.0 rad/s ÷ 2π ÷ 2 × B3 (user)");

            // เส้นกัน "ห้ามช้ากว่าเว็บช้าสุด" ถอดโดยตั้งใจ — B3 คือคำสั่งตรงของ user
            // (เลือกจาก GIF เทียบระดับความช้า 8 ส.ค.) ให้ช้ากว่าเว็บ
            Assert.Less(solo, SwimStyle.SchoolBeatHzBarracuda, "ไม่เร็วกว่าฝูงของเว็บแน่นอน");
        }

        /// <summary>
        /// The amplitude stays inside the band real fish use, which
        /// <see cref="Amplitudes_AreInTheRangeRealFishUse"/> polices for everybody else. 15 % is
        /// the top of the carangiform range and well under an eel's 17-18 %: wide, but still a
        /// fish that swims rather than one that undulates.
        /// </summary>
        [Test]
        public void SoloBarracuda_AmplitudeStaysInsideTheRealFishBand()
        {
            SwimWave w = SwimStyle.For("msh:barracuda", SoloBarracudaLen);
            Assert.That(w.Amp, Is.InRange(0.03, 0.20));
            Assert.Less(w.Amp, SwimStyle.For("msh:moray", 12.0).Amp,
                        "a barracuda is not an eel");
        }

        /// <summary>
        /// The override is a fraction of body length, exactly like everything else in this file, so
        /// scaling the animal up does not change how it swims — only how big the swimming is.
        /// </summary>
        [Test]
        public void SoloTune_DoesNotDependOnDrawSize()
        {
            SwimWave small = SwimStyle.For("msh:barracuda", 6.0);
            SwimWave big = SwimStyle.For("msh:barracuda", 60.0);

            Assert.AreEqual(small.Amp, big.Amp, Eps);
            Assert.AreEqual(small.Cycles, big.Cycles, Eps);
            Assert.AreEqual(small.BeatHz, big.BeatHz, Eps,
                            "a hand-set beat is a hand-set beat — the size law does not get it back");
        }

        /// <summary>
        /// 🔴 The blast radius, stated as a test. One species is tuned; every other animal on the
        /// demo map must come out of <see cref="SwimStyle.For"/> exactly as it did before the table
        /// existed. A tuning table that quietly moved its neighbours would be the same class of
        /// mistake as the 5-changes-in-one-build session of 3 Aug.
        /// </summary>
        [Test]
        public void SoloTune_TouchesOnlyTheOneSpeciesItNames()
        {
            foreach (string id in new[] { "school:barracuda", "school:scad", "msh:whaleshark",
                                          "msh:humpback_whale", "msh:oceanic_manta", "msh:turtle",
                                          "msh:lionfish", "msh:moray", "pod:yellowtail",
                                          "mdl:bull_shark", "msh:crab" })
            {
                SwimWave tuned = SwimStyle.For(id, 20.0);
                SwimWave raw = SwimStyle.FromTables(id, 20.0);
                double slowMul = id.Contains("scad") ? SwimStyle.UserSlowMulScad
                               : (id.Contains("barracuda") || id.Contains("yellowtail")
                                  || id.Contains("trevally")) ? SwimStyle.UserSlowMulBarracudaTrevally : 1.0;
                Assert.AreEqual(raw.BeatHz * slowMul, tuned.BeatHz, Eps, id);
                Assert.AreEqual(raw.Amp, tuned.Amp, Eps, id);
                Assert.AreEqual(raw.Cycles, tuned.Cycles, Eps, id);
            }

            Assert.IsFalse(SwimStyle.SoloTuneFor("school:barracuda").Has,
                           "the SHOAL barracuda is a web transcription and must stay untouched");
            Assert.IsTrue(SwimStyle.SoloTuneFor("msh:barracuda").Has);
        }

        /// <summary>
        /// …and it survives the pivot prefix, the same trap as
        /// <see cref="Classification_SurvivesTheSceneItemPrefix"/>. An override that silently
        /// misses looks exactly like a build where nothing was changed.
        /// </summary>
        [Test]
        public void SoloTune_SurvivesTheSceneItemPrefix()
        {
            Assert.IsTrue(SwimStyle.SoloTuneFor("Item_3_msh:barracuda").Has);
            Assert.AreEqual(SwimStyle.For("msh:barracuda", SoloBarracudaLen).BeatHz,
                            SwimStyle.For("Item_3_msh:barracuda", SoloBarracudaLen).BeatHz, Eps);
        }

        /// <summary>Fields left at <see cref="SwimStyle.NoOverride"/> keep the table's answer.</summary>
        [Test]
        public void SoloTune_LeavesUnsetFieldsAlone()
        {
            SwimWave baseline = SwimStyle.FromTables("msh:barracuda", SoloBarracudaLen);

            SwimWave beatOnly = SwimStyle.Apply(
                baseline, new SwimStyle.SoloTune(0.2, SwimStyle.NoOverride, SwimStyle.NoOverride));
            Assert.AreEqual(0.2, beatOnly.BeatHz, Eps);
            Assert.AreEqual(baseline.Amp, beatOnly.Amp, Eps);
            Assert.AreEqual(baseline.Cycles, beatOnly.Cycles, Eps);

            Assert.AreEqual(baseline.BeatHz, SwimStyle.Apply(baseline, default).BeatHz, Eps);
            Assert.AreEqual(baseline.MaxBankRad, SwimStyle.Apply(baseline, default).MaxBankRad, Eps);
        }

        /// <summary>
        /// Recoil, gust and bank are NOT overridden, and that is deliberate rather than an
        /// oversight: the web does bank a solo animal (builder.html:2483 rolls it up to 0.6 rad,
        /// well past this row's 30°), so there was nothing to correct and the change stays one
        /// change.
        /// </summary>
        [Test]
        public void SoloTune_LeavesBankAndRecoilToTheGaitRow()
        {
            SwimWave was = SwimStyle.FromTables("msh:barracuda", SoloBarracudaLen);
            SwimWave now = SwimStyle.For("msh:barracuda", SoloBarracudaLen);

            Assert.AreEqual(was.Recoil, now.Recoil, Eps);
            Assert.AreEqual(was.Gust, now.Gust, Eps);
            Assert.AreEqual(was.MaxBankRad, now.MaxBankRad, Eps);
            Assert.AreEqual(was.Gait, now.Gait);
        }
    }
}
