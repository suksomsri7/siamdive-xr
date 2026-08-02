using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// C4/C5 — the classification the whole predator/prey system rests on. Most of these tests
    /// exist because the web's own comments record them as bugs that were found and fixed; they
    /// are here so the same mistakes cannot be re-introduced in the port.
    /// </summary>
    public class SpeciesGenomeTests
    {
        // ── the three traps the web calls out by name ────────────────────────────

        [Test]
        public void Barracuda_IsNotAPredator()
        {
            // builder.html:1883 — "barracuda ถอดออก: ไม่ใช่นักล่า" (user, 2026-07-09).
            // The demo map has a 160-fish barracuda school; if this regresses it starts
            // emptying the reef around it.
            SpeciesGenome.Genome g = SpeciesGenome.For("school:barracuda");
            Assert.AreNotEqual(SpeciesGenome.DietPredator, g.Diet);
            Assert.AreEqual(0, g.Rank, "prey rank — it flees, it does not hunt");
            Assert.IsFalse(SpeciesGenome.Frightens("school:scad", "school:barracuda"),
                           "a barracuda school must not scatter the scad");
        }

        [Test]
        public void Morays_Hunt_ButRaysDoNot()
        {
            // builder.html:1877-1882 — /ray/ must not lump eagle-ray, stingray and moray together.
            Assert.AreEqual(SpeciesGenome.DietPredator, SpeciesGenome.For("fish:moray").Diet);
            Assert.AreNotEqual(SpeciesGenome.DietPredator, SpeciesGenome.For("fish:stingray").Diet);
            Assert.AreNotEqual(SpeciesGenome.DietPredator, SpeciesGenome.For("fish:eagle_ray").Diet);
        }

        [Test]
        public void GiantFilterFeedersAreHarmless()
        {
            // A whale shark is rank 3 — the biggest thing in the map — and still frightens nobody.
            SpeciesGenome.Genome ws = SpeciesGenome.For("fish:whaleshark");
            Assert.AreEqual(SpeciesGenome.DietFilter, ws.Diet);
            Assert.AreEqual(3, ws.Rank);
            Assert.IsFalse(SpeciesGenome.Frightens("school:scad", "fish:whaleshark"),
                           "the shot every diver wants must not blow the shoal apart");

            Assert.AreEqual(SpeciesGenome.DietFilter, SpeciesGenome.For("fish:manta").Diet);
            Assert.AreEqual(SpeciesGenome.DietFilter, SpeciesGenome.For("whale:humpback").Diet);
        }

        // ── diet ─────────────────────────────────────────────────────────────────

        [Test]
        public void PursuitPredatorsHunt()
        {
            Assert.AreEqual(SpeciesGenome.DietPredator, SpeciesGenome.For("fish:blacktip_shark").Diet);
            Assert.AreEqual(SpeciesGenome.DietPredator, SpeciesGenome.For("pod:orca").Diet);
            Assert.AreEqual(SpeciesGenome.DietPredator, SpeciesGenome.For("fish:tuna").Diet);
        }

        [Test]
        public void AmbushPredatorsHuntToo()
        {
            Assert.AreEqual(SpeciesGenome.DietPredator, SpeciesGenome.For("fish:lionfish").Diet);
            Assert.AreEqual(SpeciesGenome.DietPredator, SpeciesGenome.For("fish:grouper").Diet);
        }

        [Test]
        public void ReefHerbivoresGraze()
        {
            Assert.AreEqual(SpeciesGenome.DietGrazer, SpeciesGenome.For("fish:parrotfish").Diet);
            Assert.AreEqual(SpeciesGenome.DietGrazer, SpeciesGenome.For("fish:green_turtle").Diet);
        }

        [Test]
        public void AnythingUnrecognisedIsPlankton()
        {
            Assert.AreEqual(SpeciesGenome.DietPlanktivore, SpeciesGenome.For("school:scad").Diet);
            Assert.AreEqual(SpeciesGenome.DietPlanktivore, SpeciesGenome.For("").Diet);
            Assert.AreEqual(SpeciesGenome.DietPlanktivore, SpeciesGenome.For(null).Diet);
        }

        // ── rank ─────────────────────────────────────────────────────────────────

        [Test]
        public void RankLadder_ApexThenPredatorThenBigHarmlessThenPrey()
        {
            Assert.AreEqual(3, SpeciesGenome.For("fish:tiger_shark").Rank);
            Assert.AreEqual(2, SpeciesGenome.For("fish:lionfish").Rank);
            Assert.AreEqual(1, SpeciesGenome.For("fish:manta").Rank);
            Assert.AreEqual(0, SpeciesGenome.For("school:scad").Rank);
        }

        [Test]
        public void AMidPredatorFleesAnApexOne()
        {
            // The web's rule is about rank, not diet: a reef shark runs from a tiger shark.
            Assert.IsTrue(SpeciesGenome.Frightens("fish:blacktip_shark", "fish:tiger_shark"));
            Assert.IsFalse(SpeciesGenome.Frightens("fish:tiger_shark", "fish:blacktip_shark"));
        }

        [Test]
        public void NothingIsFrightenedByItsOwnKind()
        {
            Assert.IsFalse(SpeciesGenome.Frightens("school:scad", "school:scad"));
            Assert.IsFalse(SpeciesGenome.Frightens("fish:lionfish", "fish:grouper"),
                           "equal rank — a standoff, not a stampede");
        }

        // ── zone ─────────────────────────────────────────────────────────────────

        [Test]
        public void ZonesPlaceAnimalsWhereTheyBelong()
        {
            Assert.AreEqual(SpeciesGenome.ZoneBottom, SpeciesGenome.For("fish:stingray").Zone);
            Assert.AreEqual(SpeciesGenome.ZoneReef, SpeciesGenome.For("fish:clownfish").Zone);
            Assert.AreEqual(SpeciesGenome.ZonePelagic, SpeciesGenome.For("fish:sailfish").Zone);
            Assert.AreEqual(SpeciesGenome.ZoneMid, SpeciesGenome.For("school:scad").Zone);
        }

        [Test]
        public void BottomBeatsReefBeatsPelagic_WhenAnIdMatchesMoreThanOne()
        {
            // "moray_eel" hits the bottom list; the web's if/else-if order must keep it there
            // rather than falling through to reef or pelagic.
            Assert.AreEqual(SpeciesGenome.ZoneBottom, SpeciesGenome.For("fish:moray_eel").Zone);
        }

        [Test]
        public void TheHandTunedRowDecidesTheZoneBeforeTheNameDoes()
        {
            // 🔴 builder.html:1888-1890 tests cfg.stationary/benthic → bottom and cfg.flat → reef
            // BEFORE the name lists. These seven species in the app's own manifest are only
            // reachable that way, and every one of them was in the wrong water until the table
            // arrived — the mola drifting the open ocean instead of basking by the reef, the
            // seadragon and the clam swimming in mid-water.
            Assert.AreEqual(SpeciesGenome.ZoneReef, SpeciesGenome.For("msh:mola_mola").Zone,
                            "flat:true (:1804) — a basker hangs by the reef, it does not migrate");
            Assert.AreEqual(SpeciesGenome.ZoneReef, SpeciesGenome.For("mdl:batfish").Zone);
            Assert.AreEqual(SpeciesGenome.ZoneReef, SpeciesGenome.For("mdl:batfish_juvenile").Zone);
            Assert.AreEqual(SpeciesGenome.ZoneReef, SpeciesGenome.For("mdl:coralfish").Zone);
            Assert.AreEqual(SpeciesGenome.ZoneBottom, SpeciesGenome.For("mdl:leafy_seadragon").Zone,
                            "stationary:true (:1858) — nothing in the NAME says seadragon");
            Assert.AreEqual(SpeciesGenome.ZoneBottom, SpeciesGenome.For("mdl:giant_clam").Zone);
            Assert.AreEqual(SpeciesGenome.ZoneBottom, SpeciesGenome.For("losin:kaleidoscope_beetle").Zone,
                            "benthic:true (:1824)");
        }

        // ── personality (web :1898-1904) ─────────────────────────────────────────

        [Test]
        public void PredatorsAreBolderThanPrey()
        {
            // :1898 — [0.5,0.92] against [0.12,0.5]. Boldness is what sets the size of the
            // flight bubble a diver has to stay outside of.
            Assert.Greater(SpeciesGenome.For("fish:tiger_shark").Boldness,
                           SpeciesGenome.For("fish:anthias").Boldness);
        }

        [Test]
        public void BigAnimalsConserveEnergy()
        {
            // :1899 — cfg.big ? [0.3,0.6] : [0.45,0.85]. Read off the TABLE, not the name, which
            // is why an id with no row cannot be big however large the animal sounds.
            Assert.Less(SpeciesGenome.For("msh:humpback_whale").Energy,
                        SpeciesGenome.For("losin:blue_tang").Energy);
        }

        [Test]
        public void BoldInspectorsApproachAndShyFishDoNot()
        {
            // :1901-1902 — a triggerfish comes to look at you, a damselfish keeps its distance.
            Assert.Greater(SpeciesGenome.For("mdl:batfish").Curiosity, 0.6,
                           "a batfish follows divers around — web :1901 lists it as an inspector");
            // ⚠️ Known and deliberate: losin:parrotfish_yellowface and losin:parrotfish_honeycomb
            // are DISPLAYED as triggerfish but their ids say parrotfish, so every table in the
            // app classifies them as grazers. The web has exactly the same mismatch and the ids
            // are what maps are saved with — renaming them is a data migration, not a fix here.
            Assert.AreEqual(SpeciesGenome.DietGrazer,
                            SpeciesGenome.For("losin:parrotfish_yellowface").Diet);
            Assert.Less(SpeciesGenome.For("losin:clownfish_two_band").Curiosity, 0.25);
            Assert.Less(SpeciesGenome.For("losin:pygmy_seahorse").Curiosity, 0.25);
        }

        [Test]
        public void FamiliesAreClassified()
        {
            // :1904 — carried for completeness; a pod of orcas is not a pair of humpbacks.
            Assert.AreEqual(SpeciesGenome.FamilyMotherCalf, SpeciesGenome.For("msh:humpback_whale").Family);
            Assert.AreEqual(SpeciesGenome.FamilyMatriarchal, SpeciesGenome.For("pod:orca").Family);
            Assert.AreEqual(SpeciesGenome.FamilyPaternalGuard, SpeciesGenome.For("losin:pygmy_seahorse").Family);
            Assert.AreEqual(SpeciesGenome.FamilyNone, SpeciesGenome.For("school:scad").Family);
        }

        // ── sociability ──────────────────────────────────────────────────────────

        [Test]
        public void SchoolersAreSocial_LonersAreNot()
        {
            Assert.AreEqual(0.85, SpeciesGenome.For("school:scad").Social, 1e-9);
            Assert.IsTrue(SpeciesGenome.For("school:scad").Schooler);

            Assert.AreEqual(0.15, SpeciesGenome.For("fish:great_white_shark").Social, 1e-9);
            Assert.IsFalse(SpeciesGenome.For("fish:great_white_shark").Schooler);

            Assert.AreEqual(0.7, SpeciesGenome.For("fish:anthias").Social, 1e-9);
            Assert.AreEqual(0.45, SpeciesGenome.For("fish:something_new").Social, 1e-9);
        }

        [Test]
        public void AnySchoolPrefixCountsAsASchooler()
        {
            Assert.IsTrue(SpeciesGenome.For("school:batfish").Schooler);
        }

        // ── case ─────────────────────────────────────────────────────────────────

        [Test]
        public void ClassificationIsCaseInsensitive()
        {
            Assert.AreEqual(SpeciesGenome.DietFilter, SpeciesGenome.For("Fish:WhaleShark").Diet);
            Assert.AreEqual(3, SpeciesGenome.For("FISH:ORCA").Rank);
        }
    }
}
