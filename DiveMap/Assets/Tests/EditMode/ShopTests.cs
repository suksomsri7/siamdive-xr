using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// E5 — the economy. These assert prices against builder.html because the two must agree
    /// exactly: a player saves coins on the web and spends them in the app, out of the same purse.
    /// </summary>
    public class ShopTests
    {
        [Test]
        public void TheWholeWebTableCameAcross()
        {
            Assert.AreEqual(89, Shop.Count, "builder.html's PRICE has 89 entries");
        }

        [Test]
        public void PricesMatchTheWeb_AcrossTheWholeRange()
        {
            Assert.AreEqual(50, Shop.PriceOf("losin:shrimp_acrobat"), "the cheapest thing in the sea");
            Assert.AreEqual(100, Shop.PriceOf("losin:blue_tang"));
            Assert.AreEqual(500, Shop.PriceOf("msh:barracuda"));
            Assert.AreEqual(1500, Shop.PriceOf("school:scad"));
            Assert.AreEqual(4000, Shop.PriceOf("msh:manta"));
            Assert.AreEqual(12000, Shop.PriceOf("msh:whaleshark"));
            Assert.AreEqual(18000, Shop.PriceOf("pod:humpback"));
            Assert.AreEqual(20000, Shop.PriceOf("pod:orca"), "the most expensive thing in the sea");
        }

        [Test]
        public void AnUnknownAnimalCostsTheDefault_NotNothing()
        {
            // A new asset appearing on the CDN before the table is updated must not be free.
            Assert.AreEqual(Shop.DefaultPrice, Shop.PriceOf("msh:brand_new_fish"));
            Assert.AreEqual(500, Shop.DefaultPrice);
            Assert.AreEqual(Shop.DefaultPrice, Shop.PriceOf(""));
            Assert.AreEqual(Shop.DefaultPrice, Shop.PriceOf(null));
        }

        [Test]
        public void OnlyAnimalsAreSold_SceneryIsFree()
        {
            Assert.IsTrue(Shop.IsBuyable("msh:manta"));
            Assert.IsTrue(Shop.IsBuyable("losin:clownfish_two_band"));
            Assert.IsTrue(Shop.IsBuyable("school:scad"));
            Assert.IsTrue(Shop.IsBuyable("pod:orca"));
            Assert.IsTrue(Shop.IsBuyable("mdl:sardine"));
            Assert.IsTrue(Shop.IsBuyable("quat:anything"));

            Assert.IsFalse(Shop.IsBuyable("cc0:wreck_chang"), "the wreck is the map, not stock");
            Assert.IsFalse(Shop.IsBuyable("rock:boulder"));
            Assert.IsFalse(Shop.IsBuyable("warp:gate"));
            Assert.IsFalse(Shop.IsBuyable(""));
            Assert.IsFalse(Shop.IsBuyable(null));
        }

        [Test]
        public void TheCatalogueIsCheapestFirst()
        {
            var cat = Shop.Catalogue;
            Assert.AreEqual(Shop.Count, cat.Count);
            for (int i = 1; i < cat.Count; i++)
                Assert.LessOrEqual(Shop.PriceOf(cat[i - 1]), Shop.PriceOf(cat[i]),
                                   $"{cat[i - 1]} should not cost more than {cat[i]}");
            Assert.AreEqual(50, Shop.PriceOf(cat[0]));
            Assert.AreEqual(20000, Shop.PriceOf(cat[cat.Count - 1]));
        }

        [Test]
        public void TheCatalogueOrderIsStable()
        {
            // Dictionary order is not guaranteed between runs; a shop whose rows move between
            // openings is unusable with a thumb. Ties are broken by id, so two reads agree.
            var a = Shop.Catalogue;
            var b = Shop.Catalogue;
            CollectionAssert.AreEqual(a, b);
        }

        // ── buying ───────────────────────────────────────────────────────────────

        [Test]
        public void APurchaseYouCanAffordGoesThrough()
        {
            int after = Shop.Buy(600, "losin:blue_tang", out bool bought);   // 100
            Assert.IsTrue(bought);
            Assert.AreEqual(500, after);
        }

        [Test]
        public void ExactChangeIsEnough()
        {
            int after = Shop.Buy(100, "losin:blue_tang", out bool bought);
            Assert.IsTrue(bought);
            Assert.AreEqual(0, after);
        }

        [Test]
        public void APurchaseYouCannotAffordChangesNothing()
        {
            int after = Shop.Buy(99, "losin:blue_tang", out bool bought);
            Assert.IsFalse(bought);
            Assert.AreEqual(99, after, "the balance must not move — the web shakes the row instead");
        }

        [Test]
        public void FreeSceneryIsNotSoldThroughTheShop()
        {
            int after = Shop.Buy(9999, "rock:boulder", out bool bought);
            Assert.IsFalse(bought, "the shop only handles stock; scenery does not pass through it");
            Assert.AreEqual(9999, after);
        }

        [Test]
        public void BuyingNeverGoesNegative()
        {
            int after = Shop.Buy(0, "pod:orca", out bool bought);
            Assert.IsFalse(bought);
            Assert.AreEqual(0, after);
        }

        [Test]
        public void CanBuyAgreesWithBuy()
        {
            foreach (string id in new[] { "losin:blue_tang", "pod:orca", "rock:boulder" })
            {
                bool gate = Shop.CanBuy(1000, id);
                Shop.Buy(1000, id, out bool bought);
                Assert.AreEqual(gate, bought, id);
            }
        }

        [Test]
        public void AWholeDivesWorthOfCoinsBuysSomethingRealButNotEverything()
        {
            // A new player starts on 600 (Wallet.StartingCoins). They should be able to buy
            // something immediately — an economy that opens with nothing affordable teaches the
            // player the shop is not for them.
            int affordable = 0;
            foreach (string id in Shop.Catalogue)
                if (Shop.CanBuy(Wallet.StartingCoins, id)) affordable++;
            Assert.Greater(affordable, 10, "a starting balance must buy more than a token");
            Assert.Less(affordable, Shop.Count, "…and must not buy the whole ocean");
        }
    }
}
