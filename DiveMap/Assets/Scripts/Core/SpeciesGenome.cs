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

            public Genome(string diet, string zone, int rank, double social, bool schooler)
            {
                Diet = diet; Zone = zone; Rank = rank; Social = social; Schooler = schooler;
            }
        }

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

        /// <summary>Classify an asset id (<c>school:scad</c>, <c>fish:whaleshark</c>, …).</summary>
        public static Genome For(string assetId)
        {
            string id = assetId ?? "";

            string diet = DietPlanktivore;
            if (RxFilter.IsMatch(id)) diet = DietFilter;
            else if (RxAmbush.IsMatch(id)) diet = DietPredator;
            else if (RxPursuit.IsMatch(id)) diet = DietPredator;
            else if (RxGrazer.IsMatch(id)) diet = DietGrazer;
            else if (RxTurtle.IsMatch(id)) diet = DietGrazer;
            else if (RxBaleen.IsMatch(id)) diet = DietFilter;

            string zone = ZoneMid;
            if (RxBottom.IsMatch(id)) zone = ZoneBottom;
            else if (RxReef.IsMatch(id)) zone = ZoneReef;
            else if (RxPelagic.IsMatch(id)) zone = ZonePelagic;

            int rank = RxApex.IsMatch(id) ? 3
                     : diet == DietPredator ? 2
                     : RxRank1.IsMatch(id) ? 1
                     : 0;

            bool schooler = RxSchooler.IsMatch(id) || id.StartsWith("school:", System.StringComparison.OrdinalIgnoreCase);

            double social = schooler ? 0.85
                          : RxSolitary.IsMatch(id) ? 0.15
                          : RxSocial07.IsMatch(id) ? 0.7
                          : 0.45;

            return new Genome(diet, zone, rank, social, schooler);
        }

        /// <summary>Would <paramref name="other"/> frighten <paramref name="self"/>?</summary>
        public static bool Frightens(string selfId, string otherId)
        {
            Genome a = For(selfId), b = For(otherId);
            return FleeMath.IsThreat(a.Rank, b.Rank, b.Diet);
        }
    }
}
