using System.Text.RegularExpressions;

namespace DiveMap.Core
{
    /// <summary>
    /// C4/C5 — who eats whom, and who is afraid of whom. A port of the web's
    /// <c>speciesGenome()</c> (builder.html:1874-1905), which classifies any asset id by name.
    ///
    /// The ORDER of the checks is load-bearing and the web says so in its own comments — the
    /// specific patterns must be tested before the general ones. Two traps in particular:
    ///
    ///   • <c>/ray/</c> would otherwise swallow eagle-ray, stingray AND moray. Morays are ambush
    ///     predators; the rays are benthic shellfish-eaters that chase nothing. Lumping them
    ///     together makes a stingray clear a whole reef by drifting over it.
    ///   • <b>barracuda is deliberately NOT a predator</b> (web comment, user decision 2026-07-09).
    ///     It shoals like prey and does not scatter anything. If you "fix" this, the demo map's
    ///     barracuda school starts emptying the reef around it.
    ///
    /// Pure string classification, so the whole table is testable without a scene.
    /// </summary>
    public static class SpeciesGenome
    {
        public const string DietFilter      = "filter";
        public const string DietPredator    = "predator";
        public const string DietGrazer      = "grazer";
        public const string DietPlanktivore = "planktivore";

        public const string ZoneBottom  = "bottom";
        public const string ZoneReef    = "reef";
        public const string ZonePelagic = "pelagic";
        public const string ZoneMid     = "mid";

        public readonly struct Genome
        {
            public readonly string Diet;
            public readonly string Zone;
            /// <summary>3 = apex/giant, afraid of nothing · 2 = mid predator · 1 = flees apex only · 0 = prey.</summary>
            public readonly int    Rank;
            public readonly double Social;
            public readonly bool   Schooler;

            // ── personality RANGES (web :1898-1903). An individual is drawn from these, which is
            // why they are ranges and not numbers: two clownfish on the same reef are not the
            // same clownfish. SpeciesBehavior.DrawPersonality does the draw, deterministically.
            /// <summary>How close it lets a threat come before it bolts (web :1898).</summary>
            public readonly double BoldMin, BoldMax;
            /// <summary>How far and how fast it goes; a big animal is conserving it (web :1899).</summary>
            public readonly double EnergyMin, EnergyMax;
            /// <summary>Whether it comes to inspect the diver or keeps a flight bubble (web :1902).</summary>
            public readonly double CuriosityMin, CuriosityMax;
            /// <summary>How fast it gets hungry again (web :1903). Read by the hunting code.</summary>
            public readonly double MetabolismMin, MetabolismMax;
            /// <summary>
            /// Social structure (web :1904): <c>mother-calf</c>, <c>matriarchal-pod</c>,
            /// <c>paternal-guard</c> or <c>none</c>. Not yet steering anything — carried so the
            /// port is complete and so a pod of orcas can be told from a pair of humpbacks.
            /// </summary>
            public readonly string Family;

            public Genome(string diet, string zone, int rank, double social, bool schooler,
                          double boldMin, double boldMax,
                          double energyMin, double energyMax,
                          double curiosityMin, double curiosityMax,
                          double metabolismMin, double metabolismMax,
                          string family)
            {
                Diet = diet; Zone = zone; Rank = rank; Social = social; Schooler = schooler;
                BoldMin = boldMin; BoldMax = boldMax;
                EnergyMin = energyMin; EnergyMax = energyMax;
                CuriosityMin = curiosityMin; CuriosityMax = curiosityMax;
                MetabolismMin = metabolismMin; MetabolismMax = metabolismMax;
                Family = family;
            }

            /// <summary>Midpoint of a range — "the average member of this species".</summary>
            public double Boldness   => (BoldMin + BoldMax) * 0.5;
            public double Energy     => (EnergyMin + EnergyMax) * 0.5;
            public double Curiosity  => (CuriosityMin + CuriosityMax) * 0.5;
            public double Metabolism => (MetabolismMin + MetabolismMax) * 0.5;
        }

        // ── family structures (web :1904) ────────────────────────────────────────
        public const string FamilyMotherCalf    = "mother-calf";
        public const string FamilyMatriarchal   = "matriarchal-pod";
        public const string FamilyPaternalGuard = "paternal-guard";
        public const string FamilyNone          = "none";

        private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        private static readonly Regex RxFilter    = new Regex("whaleshark|mobula|devil_ray|manta|mola", Opt);
        private static readonly Regex RxAmbush    = new Regex("moray|eel|lionfish|scorpion|stonefish|grouper", Opt);
        private static readonly Regex RxPursuit   = new Regex("shark|hammerhead|blacktip|whitetip|silvertip|thresher|tuna|sailfish|orca|dolphin|sperm|dorado|seal|penguin", Opt);
        private static readonly Regex RxGrazer    = new Regex("parrot|tang|surgeon|butterfly|rabbitfish|idol|banner", Opt);
        private static readonly Regex RxTurtle    = new Regex("turtle", Opt);
        private static readonly Regex RxBaleen    = new Regex("whale|beluga|humpback", Opt);

        private static readonly Regex RxSchooler  = new Regex("scad|barracuda|trevally|yellowtail|sardine|jack", Opt);

        private static readonly Regex RxBottom    = new Regex("crab|seahorse|nurse|leopard|stingray|flounder|octopus|shrim|lobster|mantis|eel|scorpion|stonefish|guitar", Opt);
        private static readonly Regex RxReef      = new Regex("lionfish|boxfish|grouper|turtle|clownfish|puffer|tang|butterfly|bannerfish|moorish|parrot|idol|sea_serpent", Opt);
        private static readonly Regex RxPelagic   = new Regex("sailfish|tuna|dolphin|orca|mola|manta|ray|whaleshark|sperm|humpback|dorado|penguin|seal", Opt);

        private static readonly Regex RxApex      = new Regex("whaleshark|sperm_whale|humpback|blue_whale|orca|tiger_shark|thresher|silvertip|great_white|bull_shark", Opt);
        private static readonly Regex RxRank1     = new Regex("manta|mola|beluga|dolphin|eagle_ray", Opt);

        private static readonly Regex RxSolitary  = new Regex("shark|whale|sperm|beluga|mola|grouper|turtle|orca|moray|scorpion|stonefish|octopus|lobster|mantis|crab|barracuda", Opt);
        private static readonly Regex RxSocial07  = new Regex("fusilier|snapper|blush|bannerfish|tang|surgeon|butterfly|anthias|parrot|idol|moorish|azure|banded|prismatic|spotted|indigo|leopard_fish|damsel", Opt);

        /// <summary>Bold inspectors — they come and look at the diver (web :1901).</summary>
        private static readonly Regex RxBoldLook  = new Regex("trigger|batfish|remora|whitetip|silvertip|barracuda|grouper|napoleon|trevally|seal|penguin", Opt);
        /// <summary>Shy fish — they keep a flight bubble (web :1902).</summary>
        private static readonly Regex RxShy       = new Regex("butterfly|anthias|damsel|goby|blenny|seahorse|pygmy|shrimp|clownfish", Opt);

        private static readonly Regex RxMotherCalf  = new Regex("humpback|sperm|beluga|whale", Opt);
        private static readonly Regex RxMatriarchal = new Regex("dolphin|orca", Opt);
        private static readonly Regex RxPaternal    = new Regex("seahorse|clownfish|cardinal|jawfish", Opt);

        /// <summary>Classify an asset id (<c>school:scad</c>, <c>fish:whaleshark</c>, …).</summary>
        public static Genome For(string assetId)
        {
            string id = assetId ?? "";

            // 🔴 The hand-tuned row comes FIRST, because the web's zone test reads it (:1888-1890).
            // Without this the mola-mola, both batfish, the coralfish, the leafy seadragon, the
            // giant clam and the kaleidoscope beetle all land in the wrong water — see the note on
            // SpeciesBehavior. `cfg` is a struct off a dictionary lookup: no allocation.
            SpeciesBehavior.Cfg cfg = SpeciesBehavior.For(id);

            string diet = DietPlanktivore;
            if (RxFilter.IsMatch(id)) diet = DietFilter;
            else if (RxAmbush.IsMatch(id)) diet = DietPredator;
            else if (RxPursuit.IsMatch(id)) diet = DietPredator;
            else if (RxGrazer.IsMatch(id)) diet = DietGrazer;
            else if (RxTurtle.IsMatch(id)) diet = DietGrazer;
            else if (RxBaleen.IsMatch(id)) diet = DietFilter;

            string zone = ZoneMid;
            if (cfg.Stationary || cfg.Benthic || RxBottom.IsMatch(id)) zone = ZoneBottom;   // :1888
            else if (cfg.Flat || RxReef.IsMatch(id)) zone = ZoneReef;                       // :1889
            else if (RxPelagic.IsMatch(id)) zone = ZonePelagic;                             // :1890

            int rank = RxApex.IsMatch(id) ? 3
                     : diet == DietPredator ? 2
                     : RxRank1.IsMatch(id) ? 1
                     : 0;

            bool schooler = RxSchooler.IsMatch(id) || id.StartsWith("school:", System.StringComparison.OrdinalIgnoreCase);

            double social = schooler ? 0.85
                          : RxSolitary.IsMatch(id) ? 0.15
                          : RxSocial07.IsMatch(id) ? 0.7
                          : 0.45;

            // boldness (:1898) — a predator lets things get close; prey does not.
            double boldMin = diet == DietPredator ? 0.5 : 0.12;
            double boldMax = diet == DietPredator ? 0.92 : 0.5;

            // energy (:1899) — a big animal is conserving it. Note this reads cfg.big, NOT a name:
            // "big" is a hand-tuned decision, which is why the table has to be consulted.
            double energyMin = cfg.Big ? 0.3 : 0.45;
            double energyMax = cfg.Big ? 0.6 : 0.85;

            // curiosity (:1901-1902) — bold inspectors approach the diver, shy fish keep a bubble.
            double curioMin, curioMax;
            if (RxBoldLook.IsMatch(id))   { curioMin = 0.55; curioMax = 0.95; }
            else if (RxShy.IsMatch(id))   { curioMin = 0.05; curioMax = 0.30; }
            else                          { curioMin = 0.20; curioMax = 0.70; }

            string family = RxMotherCalf.IsMatch(id)  ? FamilyMotherCalf     // :1904
                          : RxMatriarchal.IsMatch(id) ? FamilyMatriarchal
                          : RxPaternal.IsMatch(id)    ? FamilyPaternalGuard
                          : FamilyNone;

            return new Genome(diet, zone, rank, social, schooler,
                              boldMin, boldMax,
                              energyMin, energyMax,
                              curioMin, curioMax,
                              0.6, 1.25,          // metabolism (:1903) — the same range for everything
                              family);
        }

        /// <summary>Would <paramref name="other"/> frighten <paramref name="self"/>?</summary>
        public static bool Frightens(string selfId, string otherId)
        {
            Genome a = For(selfId), b = For(otherId);
            return FleeMath.IsThreat(a.Rank, b.Rank, b.Diet);
        }
    }
}
