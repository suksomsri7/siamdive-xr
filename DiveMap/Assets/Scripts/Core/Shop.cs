using System;
using System.Collections.Generic;

namespace DiveMap.Core
{
    /// <summary>
    /// E5 — the shop. A port of the web's <c>PRICE</c> table, <c>priceOf()</c>, <c>isBuyable()</c>
    /// and <c>buyAnimal()</c> (builder.html:4287-4311).
    ///
    /// The price table is lifted from builder.html verbatim (89 entries, extracted rather than
    /// retyped) because it is a live economy: a player who has saved 12,000 coins for a whale
    /// shark on the web must find the same price in the app. A typo here is not a cosmetic bug,
    /// it is a refund request.
    ///
    /// Three rules from the web that are easy to miss:
    ///   • an id the table does not know costs <see cref="DefaultPrice"/>, never zero
    ///   • only ANIMALS are bought — rocks, coral and wrecks are free scenery (<see cref="IsBuyable"/>)
    ///   • the shop is sorted cheapest-first, so the affordable half is what you see when it opens
    /// </summary>
    public static class Shop
    {
        /// <summary>What an animal costs when the table has never heard of it (web: 500).</summary>
        public const int DefaultPrice = 500;

        /// <summary>Prefixes the web treats as purchasable stock (web: <c>^(msh|losin|mdl|school|pod|quat):</c>).</summary>
        private static readonly string[] BuyablePrefixes = { "msh:", "losin:", "mdl:", "school:", "pod:", "quat:" };

        private static readonly Dictionary<string, int> Price = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            { "losin:shrimp_acrobat", 50 },
            { "losin:shrimp_longhorn", 50 },
            { "msh:crab", 60 },
            { "losin:pygmy_seahorse", 60 },
            { "losin:mantis_shrimp", 70 },
            { "msh:seahorse", 80 },
            { "losin:garden_eel", 80 },
            { "losin:lobster_aurora", 90 },
            { "losin:octopus_ringed", 150 },
            { "losin:clownfish_two_band", 80 },
            { "losin:clownfish_three_band", 80 },
            { "losin:azure_stripe", 90 },
            { "losin:indigo_velvet", 90 },
            { "losin:banded_fish", 90 },
            { "losin:leopard_fish", 90 },
            { "losin:prismatic_reef", 90 },
            { "losin:blue_tang", 100 },
            { "losin:butterflyfish", 100 },
            { "losin:bannerfish", 100 },
            { "msh:boxfish", 110 },
            { "losin:moorish_idol", 120 },
            { "losin:scorpionfish", 120 },
            { "losin:stonefish", 120 },
            { "losin:pufferfish_bigeye", 130 },
            { "losin:pufferfish_golden", 130 },
            { "losin:spotted_harbor", 130 },
            { "losin:snapper_blushing", 140 },
            { "msh:lionfish", 150 },
            { "losin:octopus", 160 },
            { "losin:octopus_crimson", 160 },
            { "losin:parrotfish_mosaic", 300 },
            { "losin:parrotfish_crimson", 300 },
            { "losin:parrotfish_honeycomb", 300 },
            { "losin:parrotfish_prismatic", 300 },
            { "losin:parrotfish_yellowface", 300 },
            { "msh:trevally", 350 },
            { "losin:moray_leopard", 350 },
            { "losin:sea_serpent", 380 },
            { "losin:blue_spotted_stingray", 400 },
            { "msh:tuna", 450 },
            { "msh:barracuda", 500 },
            { "msh:turtle", 600 },
            { "losin:marble_stingray", 650 },
            { "msh:eagle_ray", 700 },
            { "msh:bluefin_tuna", 800 },
            { "msh:sailfish", 900 },
            { "msh:blacktip_reef_shark", 1200 },
            { "msh:whitetip_reef_shark", 1200 },
            { "msh:leopard_shark", 1400 },
            { "msh:nurse_shark", 1400 },
            { "msh:dolphin_real", 1500 },
            { "losin:guitar_shark", 1600 },
            { "msh:mola_mola", 1800 },
            { "msh:silvertip_shark", 2000 },
            { "msh:beluga_whale", 2200 },
            { "msh:thresher_shark", 2800 },
            { "losin:devil_ray", 3000 },
            { "losin:reef_manta_ray", 3500 },
            { "losin:hammerhead_shark", 3500 },
            { "msh:manta", 4000 },
            { "msh:tiger_shark", 4500 },
            { "msh:black_manta", 4500 },
            { "msh:oceanic_manta", 5500 },
            { "msh:orca", 9000 },
            { "msh:whaleshark", 12000 },
            { "msh:humpback_whale", 14000 },
            { "msh:sperm_whale", 16000 },
            { "mdl:sardine", 90 },
            { "mdl:coralfish", 120 },
            { "mdl:batfish_juvenile", 150 },
            { "mdl:batfish", 200 },
            { "mdl:giant_clam", 250 },
            { "mdl:leafy_seadragon", 600 },
            { "mdl:dorado", 700 },
            { "mdl:penguin", 900 },
            { "mdl:penguin_emperor", 1300 },
            { "mdl:seal", 1500 },
            { "mdl:whitetip_shark", 2400 },
            { "mdl:bull_shark", 3800 },
            { "mdl:great_white_shark", 5200 },
            { "school:batfish", 1800 },
            { "school:parrotfish_prismatic", 2200 },
            { "pod:orca", 20000 },
            { "school:barracuda", 1000 },
            { "pod:yellowtail", 1200 },
            { "school:scad", 1500 },
            { "pod:eagle_ray", 2500 },
            { "pod:dolphin", 6000 },
            { "pod:humpback", 18000 },
        };

        /// <summary>Every id the shop sells, cheapest first (the web sorts by priceOf).</summary>
        public static IReadOnlyList<string> Catalogue
        {
            get
            {
                if (_catalogue != null) return _catalogue;
                var ids = new List<string>(Price.Keys);
                // Ties broken by id so the order is identical on every device and every run —
                // a shop that reshuffles itself between openings is unusable on a phone.
                ids.Sort((a, b) =>
                {
                    int c = Price[a].CompareTo(Price[b]);
                    return c != 0 ? c : string.CompareOrdinal(a, b);
                });
                _catalogue = ids;
                return _catalogue;
            }
        }
        private static List<string> _catalogue;

        /// <summary>Price of one animal (web: <c>PRICE[a.id] ?? 500</c>).</summary>
        public static int PriceOf(string assetId)
        {
            if (string.IsNullOrEmpty(assetId)) return DefaultPrice;
            return Price.TryGetValue(assetId, out int p) ? p : DefaultPrice;
        }

        /// <summary>Is this something the shop charges for at all? Scenery is free.</summary>
        public static bool IsBuyable(string assetId)
        {
            if (string.IsNullOrEmpty(assetId)) return false;
            for (int i = 0; i < BuyablePrefixes.Length; i++)
                if (assetId.StartsWith(BuyablePrefixes[i], StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>
        /// Does this placement cost the CURRENT user anything? The web's tryPlace opens with
        /// <c>if (isBuyable(a) &amp;&amp; !_isAdmin)</c> (builder.html:4298) — the admin builds the
        /// official game worlds, where a whale shark is set dressing, not a 14,000-coin purchase,
        /// and an admin balance is displayed as ∞ precisely because nothing is ever deducted from
        /// it. Kept next to <see cref="IsBuyable"/> so the two can never disagree, and pure so the
        /// rule is asserted by a test rather than by a comment in the UI layer.
        /// </summary>
        public static bool ShouldCharge(string assetId, bool isAdmin)
            => IsBuyable(assetId) && !isAdmin;

        /// <summary>Can this purchase go through right now?</summary>
        public static bool CanBuy(int coins, string assetId)
            => IsBuyable(assetId) && Wallet.CanAfford(coins, PriceOf(assetId));

        /// <summary>
        /// The balance after buying, or the balance unchanged when it cannot be afforded. The
        /// web's buyAnimal returns false and shakes the row rather than going negative.
        /// </summary>
        public static int Buy(int coins, string assetId, out bool bought)
        {
            bought = CanBuy(coins, assetId);
            return bought ? Wallet.Spend(coins, PriceOf(assetId)) : coins;
        }

        /// <summary>Number of items the shop stocks (for tests and the QC log).</summary>
        public static int Count => Price.Count;
    }
}
