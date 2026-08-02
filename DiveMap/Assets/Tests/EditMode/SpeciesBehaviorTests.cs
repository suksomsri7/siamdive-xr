using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// C6 — the web's hand-tuned per-species table and the locomotion it derives.
    ///
    /// Most of these read as "the row survived the port", which sounds trivial and is not: this
    /// table is the only place in the app that knows a nurse shark sleeps on the sand and a whale
    /// shark cannot stop swimming, and a transcription slip is invisible until somebody looks at
    /// the reef and asks why the crab is in mid-water.
    /// </summary>
    public class SpeciesBehaviorTests
    {
        // ── the table came across ────────────────────────────────────────────────

        [Test]
        public void TheWholeTableIsHere()
        {
            // builder.html:1773-1869 is 94 rows. If this number moves someone edited the table —
            // which is allowed, but the count is the one cheap check that the edit was deliberate.
            Assert.AreEqual(94, SpeciesBehavior.RowCount);
        }

        [Test]
        public void RowsCarryTheirNumbers()
        {
            // builder.html:1779 — {speedMul:0.6, roamR:170, big, neverRest, slowAnim}
            SpeciesBehavior.Cfg ws = SpeciesBehavior.For("msh:whaleshark");
            Assert.IsTrue(ws.Has);
            Assert.AreEqual(0.6, ws.SpeedMul, 1e-9);
            Assert.AreEqual(170.0, ws.RoamR, 1e-9);
            Assert.IsTrue(ws.Big);
            Assert.IsTrue(ws.NeverRest, "a ram-ventilating shark that stops swimming drowns");
            Assert.IsTrue(ws.SlowAnim);
            Assert.IsFalse(ws.Ambush);

            // builder.html:1856 — the humpback is the widest roamer in the table.
            Assert.AreEqual(400.0, SpeciesBehavior.For("msh:humpback_whale").RoamR, 1e-9);
            Assert.IsTrue(SpeciesBehavior.For("msh:humpback_whale").Breacher);

            // builder.html:1801 — the sperm whale sleeps hanging vertically.
            Assert.IsTrue(SpeciesBehavior.For("msh:sperm_whale").Sleeper);
        }

        [Test]
        public void AbsentIsNotZero()
        {
            // 🔴 The trap this file exists to nail down. default(Cfg) has every double at 0.0,
            // and 0.0 is a real hand-tuned value — mdl:giant_clam is speedMul: 0. Confuse the two
            // and Derive pins every un-tuned fish in the game to a point.
            SpeciesBehavior.Cfg none = SpeciesBehavior.For("fish:0");
            Assert.IsFalse(none.Has);
            Assert.IsFalse(none.HasRoamR);
            Assert.IsFalse(none.HasSpeed);

            SpeciesBehavior.Cfg clam = SpeciesBehavior.For("mdl:giant_clam");
            Assert.IsTrue(clam.Has);
            Assert.IsTrue(clam.HasSpeed);
            Assert.AreEqual(0.0, clam.SpeedMul, 1e-9, "the web really does say speedMul: 0");

            Assert.Greater(SpeciesBehavior.Derive("fish:0", 0.5).RoamR, 100.0,
                           "an un-tuned mid-water fish roams the mid-water default, not nothing");
        }

        [Test]
        public void AmbushersAreMarked()
        {
            // The sit-and-wait hunters (builder.html:1776, :1825, :1841).
            Assert.IsTrue(SpeciesBehavior.For("msh:lionfish").Ambush);
            Assert.IsTrue(SpeciesBehavior.For("losin:moray_leopard").Ambush);
            Assert.IsTrue(SpeciesBehavior.For("losin:scorpionfish").Ambush);
            Assert.IsFalse(SpeciesBehavior.For("msh:tiger_shark").Ambush,
                           "a tiger shark patrols; it does not lie in wait");
        }

        // ── deriveLocomotion (builder.html:1927-1944) ────────────────────────────

        [Test]
        public void HandTunedRoamWinsOverTheZoneDefault()
        {
            // 🔴 The web wrote this bug down (:1938-1939): overwriting cfg.roamR made every
            // species in a zone roam the same, so the lionfish patrolled 85 u instead of hovering.
            double lion = SpeciesBehavior.Derive("msh:lionfish", 0.5).RoamR;
            double reefDefault = SpeciesBehavior.Derive("fish:some_unknown_clownfish", 0.5).RoamR;
            Assert.Less(lion, 15.0, "the lionfish's own 7 u must survive");
            Assert.Greater(reefDefault, 50.0, "…while an un-tuned reef fish gets the 85 u default");
        }

        [Test]
        public void TheTwoCeilingsAreDifferent()
        {
            // :1941 — 400 for a hand-tuned species, 200 for a derived one. One ceiling for both
            // either cages the humpback or lets every unnamed mid-water fish roam twice the map.
            Assert.AreEqual(400.0, SpeciesBehavior.Derive("msh:humpback_whale", 1.0).RoamR, 1e-6);

            // An un-tuned pelagic: base 300 × 1.25 = 375 → clamped to 200.
            Assert.AreEqual(200.0, SpeciesBehavior.Derive("fish:unknown_sailfish", 1.0).RoamR, 1e-6);
        }

        [Test]
        public void EnergyMovesRoamAndSpeed()
        {
            // :1942-1943 — an energetic individual roams further and swims faster.
            SpeciesBehavior.Locomotion lazy = SpeciesBehavior.Derive("losin:blue_tang", 0.0);
            SpeciesBehavior.Locomotion keen = SpeciesBehavior.Derive("losin:blue_tang", 1.0);
            Assert.Greater(keen.RoamR, lazy.RoamR);
            Assert.Greater(keen.SwimMul, lazy.SwimMul);

            // The exact web factors: roam ×(0.75 + en·0.5), swim ×(0.85 + en·0.3).
            Assert.AreEqual(lazy.RoamR * (1.25 / 0.75), keen.RoamR, 1e-6);
            Assert.AreEqual(lazy.SwimMul * (1.15 / 0.85), keen.SwimMul, 1e-6);
        }

        [Test]
        public void PredatorsRoamWiderAndSwimHarder()
        {
            // :1937 — roam ×1.35 and swim ×1.28 for a non-big pursuit predator.
            SpeciesBehavior.Cfg none = default;
            SpeciesGenome.Genome shark = SpeciesGenome.For("fish:some_reef_shark");
            SpeciesGenome.Genome prey  = SpeciesGenome.For("fish:some_reef_snapper");
            Assert.AreEqual(SpeciesGenome.DietPredator, shark.Diet);
            Assert.AreNotEqual(SpeciesGenome.DietPredator, prey.Diet);

            Assert.Greater(SpeciesBehavior.Derive(none, shark, 0.5).SwimMul,
                           SpeciesBehavior.Derive(none, prey, 0.5).SwimMul);
        }

        [Test]
        public void StationaryAnimalsGoNowhere()
        {
            // :1931 — roam 8, swim 0.15. The clam and the seadragon are in this list because the
            // NAME never says so: "leafy_seadragon" matches no regex anywhere in the codebase.
            foreach (string id in new[] { "msh:crab", "msh:seahorse", "mdl:giant_clam",
                                          "mdl:leafy_seadragon", "losin:stonefish",
                                          "losin:garden_eel", "losin:pygmy_seahorse" })
            {
                Assert.IsTrue(SpeciesBehavior.IsStationary(id), id);
                // 8 u base (:1931), ×1.35 if it happens to be a predator (:1937 — the stonefish
                // and the garden eel are), ×1.25 at maximum energy = 13.5 u. That is the web's
                // own arithmetic; a "tidier" 8 would mean the port dropped the predator factor.
                Assert.LessOrEqual(SpeciesBehavior.Derive(id, 1.0).RoamR, 13.6, id);
            }
        }

        [Test]
        public void CruiseMulFoldsInTheHandTunedSpeed()
        {
            // The web keeps swimMul and speedMul in two places; a caller that wants one number has
            // to multiply them, and forgetting to is how a 0.15× lionfish ends up at 1×.
            SpeciesBehavior.Cfg lion = SpeciesBehavior.For("msh:lionfish");
            double swim = SpeciesBehavior.Derive("msh:lionfish", 0.5).SwimMul;
            Assert.AreEqual(swim * lion.SpeedMul, SpeciesBehavior.CruiseMul("msh:lionfish", 0.5), 1e-9);

            Assert.Less(SpeciesBehavior.CruiseMul("msh:lionfish", 0.5),
                        SpeciesBehavior.CruiseMul("msh:sailfish", 0.5) * 0.15,
                        "a lionfish and a sailfish must not be within an order of magnitude");
        }

        // ── the stationary animal's own behaviour (builder.html:2166-2172) ───────

        [Test]
        public void SwayIsBoundedAndPeriodic()
        {
            // ±0.4 u of bob and ±0.12 rad of yaw. Bounded matters: the sway is added to a FIXED
            // anchor, so an unbounded term walks a crab across the sand.
            for (double t = 0.0; t < 40.0; t += 0.13)
            {
                Assert.That(SpeciesBehavior.SwayY(t, 1.7, 0.4), Is.InRange(-0.4001, 0.4001));
                Assert.That(SpeciesBehavior.SwayYaw(t, 1.1), Is.InRange(-0.1201, 0.1201));
            }
            // It has to actually move, or "stationary" means "frozen" and the reef looks dead.
            Assert.Greater(System.Math.Abs(SpeciesBehavior.SwayY(0.9, 1.7, 0.0)), 0.05);
        }

        // ── personality (builder.html:1898-1904) ────────────────────────────────

        [Test]
        public void PersonalityIsInRangeAndDeterministic()
        {
            SpeciesBehavior.Personality a = SpeciesBehavior.DrawPersonality("msh:tiger_shark", 7u);
            SpeciesBehavior.Personality b = SpeciesBehavior.DrawPersonality("msh:tiger_shark", 7u);
            Assert.AreEqual(a.Energy, b.Energy, 1e-12, "the same map must replay the same reef");
            Assert.AreEqual(a.Boldness, b.Boldness, 1e-12);

            SpeciesGenome.Genome g = SpeciesGenome.For("msh:tiger_shark");
            Assert.That(a.Energy, Is.InRange(g.EnergyMin, g.EnergyMax));
            Assert.That(a.Boldness, Is.InRange(g.BoldMin, g.BoldMax));
            Assert.That(a.Curiosity, Is.InRange(g.CuriosityMin, g.CuriosityMax));
            Assert.That(a.Metabolism, Is.InRange(g.MetabolismMin, g.MetabolismMax));
        }

        [Test]
        public void TwoIndividualsOfOneSpeciesDiffer()
        {
            // The whole reason personality is a RANGE. Identical draws mean a shoal of clones.
            int distinct = 0;
            double first = SpeciesBehavior.DrawPersonality("losin:blue_tang", 1u).Energy;
            for (uint s = 2; s < 12; s++)
                if (System.Math.Abs(SpeciesBehavior.DrawPersonality("losin:blue_tang", s).Energy - first) > 1e-6)
                    distinct++;
            Assert.Greater(distinct, 6);
        }

        [Test]
        public void NullAndUnknownIdsAreSafe()
        {
            Assert.IsFalse(SpeciesBehavior.For(null).Has);
            Assert.IsFalse(SpeciesBehavior.HasRow(""));
            Assert.IsFalse(SpeciesBehavior.IsStationary(null));
            Assert.Greater(SpeciesBehavior.Derive(null, 0.5).RoamR, 0.0);
        }
    }
}
