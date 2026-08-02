using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// "สัตว์ทะเลทุก species ต้องมีสมอง" — asserted EXHAUSTIVELY, over the app's real asset
    /// manifest rather than over a hand-picked handful.
    ///
    /// 🔴 Why a list and not a spot check. Every previous round of this work tested the four
    /// species the demo map happens to show, and every previous round shipped a manifest where
    /// most of the other ninety-five resolved to a default. A default is not a brain. This file
    /// walks the whole animal list and asserts that all four tables — genome, hand-tuned row,
    /// swim style, temperament — return something usable for every single id, and that the
    /// answers are actually DIFFERENT from one another where the species are.
    ///
    /// The list is checked in rather than parsed at runtime so the test runs identically in Unity
    /// and in tools/test.sh, and <see cref="ListMatchesTheShippedManifest"/> is what stops it
    /// going stale: it re-reads StreamingAssets/asset_manifest.json when it can find it and fails
    /// if a species was added or removed without this list being updated.
    /// </summary>
    public class SpeciesCoverageTests
    {
        /// <summary>
        /// Every animal module in StreamingAssets/asset_manifest.json — kinds MARINE_LIFE,
        /// SCHOOL, FISH and TURTLE. Generated from the manifest; see the test below.
        /// </summary>
        public static readonly string[] Animals =
        {
            "fish:0",
            "fish:1",
            "fish:2",
            "fish:3",
            "turtle:0",
            "turtle:1",
            "glb_turtle_loggerhead",
            "msh:turtle",
            "msh:barracuda",
            "msh:lionfish",
            "msh:crab",
            "msh:seahorse",
            "msh:whaleshark",
            "msh:manta",
            "msh:trevally",
            "msh:boxfish",
            "msh:tiger_shark",
            "msh:silvertip_shark",
            "msh:leopard_shark",
            "msh:nurse_shark",
            "msh:whitetip_reef_shark",
            "msh:blacktip_reef_shark",
            "msh:thresher_shark",
            "msh:bluefin_tuna",
            "msh:tuna",
            "msh:sailfish",
            "msh:sperm_whale",
            "msh:beluga_whale",
            "msh:orca",
            "msh:dolphin_real",
            "msh:humpback_whale",
            "msh:oceanic_manta",
            "msh:black_manta",
            "msh:eagle_ray",
            "msh:mola_mola",
            "school:barracuda",
            "school:scad",
            "pod:humpback",
            "pod:eagle_ray",
            "pod:dolphin",
            "pod:yellowtail",
            "pod:hammerhead",
            "pod:blacktip",
            "school:batfish",
            "school:parrotfish_prismatic",
            "pod:orca",
            "losin:octopus_ringed",
            "losin:azure_stripe",
            "losin:pufferfish_bigeye",
            "losin:banded_fish",
            "losin:blue_spotted_stingray",
            "losin:snapper_blushing",
            "losin:shrimp_acrobat",
            "losin:shrimp_longhorn",
            "losin:parrotfish_mosaic",
            "losin:octopus_crimson",
            "losin:parrotfish_crimson",
            "losin:mantis_shrimp",
            "losin:devil_ray",
            "losin:pufferfish_golden",
            "losin:garden_eel",
            "losin:guitar_shark",
            "losin:hammerhead_shark",
            "losin:parrotfish_honeycomb",
            "losin:indigo_velvet",
            "losin:kaleidoscope_beetle",
            "losin:moray_leopard",
            "losin:leopard_fish",
            "losin:marble_stingray",
            "losin:moorish_idol",
            "losin:sea_serpent",
            "losin:octopus",
            "losin:butterflyfish",
            "losin:parrotfish_prismatic",
            "losin:prismatic_reef",
            "losin:reef_manta_ray",
            "losin:blue_tang",
            "losin:scorpionfish",
            "losin:lobster_aurora",
            "losin:spotted_harbor",
            "losin:stonefish",
            "losin:clownfish_three_band",
            "losin:clownfish_two_band",
            "losin:bannerfish",
            "losin:parrotfish_yellowface",
            "losin:pygmy_seahorse",
            "mdl:bull_shark",
            "mdl:great_white_shark",
            "mdl:whitetip_shark",
            "mdl:dorado",
            "mdl:batfish",
            "mdl:batfish_juvenile",
            "mdl:coralfish",
            "mdl:sardine",
            "mdl:seal",
            "mdl:penguin",
            "mdl:penguin_emperor",
            "mdl:leafy_seadragon",
            "mdl:giant_clam",
        };

        private static readonly HashSet<string> Kinds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "MARINE_LIFE", "SCHOOL", "FISH", "TURTLE" };

        // ── the four tables, for every species ───────────────────────────────────

        [Test]
        public void EverySpeciesResolvesAGenome()
        {
            foreach (string id in Animals)
            {
                SpeciesGenome.Genome g = SpeciesGenome.For(id);
                Assert.IsNotNull(g.Diet, id);
                Assert.IsNotNull(g.Zone, id);
                CollectionAssert.Contains(
                    new[] { SpeciesGenome.DietFilter, SpeciesGenome.DietPredator,
                            SpeciesGenome.DietGrazer, SpeciesGenome.DietPlanktivore }, g.Diet, id);
                CollectionAssert.Contains(
                    new[] { SpeciesGenome.ZoneBottom, SpeciesGenome.ZoneReef,
                            SpeciesGenome.ZonePelagic, SpeciesGenome.ZoneMid }, g.Zone, id);
                Assert.That(g.Rank, Is.InRange(0, 3), id);
                Assert.That(g.Social, Is.InRange(0.0, 1.0), id);
                // Personality ranges must be real ranges, or DrawPersonality returns a constant
                // and every individual of the species is the same individual.
                Assert.Less(g.BoldMin, g.BoldMax, id);
                Assert.Less(g.EnergyMin, g.EnergyMax, id);
                Assert.Less(g.CuriosityMin, g.CuriosityMax, id);
                Assert.Less(g.MetabolismMin, g.MetabolismMax, id);
                Assert.IsNotNull(g.Family, id);
            }
        }

        [Test]
        public void EverySpeciesResolvesALocomotion()
        {
            foreach (string id in Animals)
            {
                SpeciesBehavior.Locomotion loc = SpeciesBehavior.Derive(id, 0.5);
                Assert.Greater(loc.RoamR, 0.0, id);
                Assert.LessOrEqual(loc.RoamR, 400.0, id);
                Assert.Greater(loc.SwimMul, 0.0, id);
                Assert.LessOrEqual(loc.SwimMul, 3.0, id);
                Assert.GreaterOrEqual(SpeciesBehavior.CruiseMul(id, 0.5), 0.0, id);
            }
        }

        [Test]
        public void EverySpeciesResolvesASwimStyle()
        {
            foreach (string id in Animals)
            {
                SwimWave w = SwimStyle.For(id, 10.0);
                Assert.Greater(w.BeatHz, 0.0, id);
                Assert.Greater(w.Amp, 0.0, id);
                Assert.GreaterOrEqual(w.MaxBankRad, 0.0, id);
                CollectionAssert.Contains(new[] { SwimGait.Body, SwimGait.Fluke, SwimGait.Wing },
                                          SwimStyle.GaitFor(id), id);
            }
        }

        [Test]
        public void EverySpeciesResolvesATemperament()
        {
            foreach (string id in Animals)
            {
                MindTraits tr = FishMind.TraitsFor(id);
                Assert.That(tr.Curiosity, Is.InRange(0.0, 1.0), id);
                Assert.Greater(tr.RoamMul, 0.0, id);
                Assert.GreaterOrEqual(tr.DepthMul, 0.0, id);
                Assert.Greater(tr.DwellMin, 0.0, id);
                Assert.Greater(tr.DwellMax, tr.DwellMin, id);
                Assert.Greater(tr.WarySeconds, 0.0, id);
                Assert.GreaterOrEqual(tr.OrbitRate, 0.0, id);
            }
        }

        // ── the point of the exercise: they must not all be the same animal ──────

        [Test]
        public void AlmostEverySpeciesHasAHandTunedRow()
        {
            var missing = new List<string>();
            foreach (string id in Animals)
                if (!SpeciesBehavior.HasRow(id)) missing.Add(id);

            // The nine that legitimately have none are the procedural placeholders the web never
            // wrote a row for either — fish:0-3 and turtle:0-1 are generated meshes, not species,
            // and the two batfish/parrotfish pods below inherit from their school id. They still
            // get a genome, a swim style and a temperament from the zone fallback; that is what
            // the fallback is FOR. If this number grows, a real species arrived without a brain.
            Assert.LessOrEqual(missing.Count, 6,
                "species with no hand-tuned row: " + string.Join(", ", missing));
        }

        [Test]
        public void TemperamentsActuallyDiffer()
        {
            // If the table collapses to a handful of distinct rows, every reef fish behaves
            // identically and the whole exercise was cosmetic. Ten was the count before this work
            // (four named rows + four zone fallbacks + rounding).
            var seen = new HashSet<string>();
            foreach (string id in Animals)
            {
                MindTraits t = FishMind.TraitsFor(id);
                seen.Add($"{t.Curiosity:F3}|{t.RoamMul:F3}|{t.DepthMul:F3}|{t.DwellMin:F2}|" +
                         $"{t.OrbitRate:F3}|{t.StandoffMul:F3}|{t.WarySeconds:F2}");
            }
            Assert.Greater(seen.Count, 30,
                $"only {seen.Count} distinct temperaments across {Animals.Length} species");
        }

        [Test]
        public void RoamingRangesSpanTheWholeReef()
        {
            // A lionfish hovers over a few units of sand; a humpback crosses the map. If the
            // spread is not there the animals are all doing the same thing at different speeds.
            double min = double.MaxValue, max = 0.0;
            foreach (string id in Animals)
            {
                double r = SpeciesBehavior.Derive(id, 0.5).RoamR;
                if (r < min) min = r;
                if (r > max) max = r;
            }
            Assert.Less(min, 20.0, "nothing in the manifest stays put");
            Assert.Greater(max, 300.0, "nothing in the manifest travels");
        }

        /// <summary>
        /// Not an assertion — the audit table itself, printed. Run it when reviewing this work:
        ///
        ///     bash tools/test.sh --where "test =~ DumpAuditTable"
        ///
        /// One line per species with all four tables side by side, which is the only practical way
        /// to eyeball "is this animal classified as the right kind of animal" for ninety-nine of
        /// them. <c>[Explicit]</c> so it never runs as part of the suite.
        /// </summary>
        [Test, Explicit]
        public void DumpAuditTable()
        {
            Console.WriteLine("id | diet | zone | rank | flags | roamR | swimMul | gait | " +
                              "curio | roamMul | dwell | wary | row");
            foreach (string id in Animals)
            {
                SpeciesGenome.Genome g = SpeciesGenome.For(id);
                SpeciesBehavior.Cfg c = SpeciesBehavior.For(id);
                SpeciesBehavior.Locomotion l = SpeciesBehavior.Derive(id, 0.5);
                MindTraits t = FishMind.TraitsFor(id);
                string flags = (c.Stationary ? "S" : "") + (c.Benthic ? "B" : "") +
                               (c.Flat ? "F" : "") + (c.Big ? "G" : "") + (c.Ambush ? "A" : "") +
                               (SwimStyle.IsStill(id) ? "*" : "");
                Console.WriteLine($"{id,-32} {g.Diet,-12} {g.Zone,-8} {g.Rank} {flags,-6} " +
                                  $"{l.RoamR,6:F0} {l.SwimMul,5:F2} {SwimStyle.GaitFor(id),-5} " +
                                  $"{t.Curiosity,5:F2} {t.RoamMul,5:F2} " +
                                  $"{t.DwellMin,5:F0}-{t.DwellMax,-5:F0} {t.WarySeconds,5:F1} " +
                                  $"{(c.Has ? "web" : "fallback")}");
            }
        }

        // ── the list itself ──────────────────────────────────────────────────────

        [Test]
        public void ListMatchesTheShippedManifest()
        {
            string path = ManifestPath();
            if (path == null || !File.Exists(path))
                Assert.Ignore("asset_manifest.json not reachable from here (player build) — " +
                              "the checked-in list is authoritative in that case.");

            // The manifest is pretty-printed one field per line, so "id" and "kind" arrive on
            // consecutive lines rather than together: remember the id and pair it with the kind
            // that follows. Deliberately not a JSON parser — this test must not depend on one.
            var fromFile = new List<string>();
            string pending = null;
            foreach (string line in File.ReadAllLines(path))
            {
                string id = Between(line, "\"id\": \"", "\"");
                if (id != null) { pending = id; continue; }
                string kind = Between(line, "\"kind\": \"", "\"");
                if (kind != null && pending != null && Kinds.Contains(kind)) fromFile.Add(pending);
                if (kind != null) pending = null;
            }

            Assert.Greater(fromFile.Count, 0, "could not parse any animal out of " + path);
            CollectionAssert.AreEquivalent(Animals, fromFile,
                "the manifest gained or lost an animal — regenerate SpeciesCoverageTests.Animals");
        }

        /// <summary>Where the manifest is, relative to THIS source file. Works in both runners.</summary>
        private static string ManifestPath([CallerFilePath] string here = "")
        {
            // …/DiveMap/Assets/Tests/EditMode/SpeciesCoverageTests.cs
            DirectoryInfo d = new FileInfo(here).Directory;          // EditMode
            for (int i = 0; i < 3 && d != null; i++) d = d.Parent;   // Tests → Assets → DiveMap
            return d == null ? null : Path.Combine(d.FullName, "Assets", "StreamingAssets", "asset_manifest.json");
        }

        private static string Between(string s, string a, string b)
        {
            int i = s.IndexOf(a, StringComparison.Ordinal);
            if (i < 0) return null;
            i += a.Length;
            int j = s.IndexOf(b, i, StringComparison.Ordinal);
            return j < 0 ? null : s.Substring(i, j - i);
        }
    }
}
