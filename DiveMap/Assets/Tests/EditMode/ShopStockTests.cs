using DiveMap.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// E5 — the purchases a player keeps. These guard the two ways this can quietly rob someone:
    /// losing a purchase, or duplicating it every time the map rebuilds.
    /// </summary>
    public class ShopStockTests
    {
        private static JObject Item(string assetId, string id = null)
        {
            JObject o = ShopStock.MakeItem(assetId, 1, 2, 3, 0.5, 1.0, 1234);
            if (id != null) o["id"] = id;
            return o;
        }

        [Test]
        public void AMadeItemHasEverythingTheBuilderReads()
        {
            JObject o = ShopStock.MakeItem("msh:manta", 10, 20, 30, 1.5, 2.0, 99);
            Assert.AreEqual("msh:manta", (string)o["assetId"]);
            Assert.AreEqual("buy_99", (string)o["id"]);
            Assert.AreEqual(10.0, (double)o["p"][0], 1e-9);
            Assert.AreEqual(20.0, (double)o["p"][1], 1e-9);
            Assert.AreEqual(30.0, (double)o["p"][2], 1e-9);
            Assert.AreEqual(1.5, (double)o["r"][1], 1e-9);
            Assert.AreEqual(2.0, (double)o["s"][0], 1e-9);
        }

        [Test]
        public void AZeroScaleWouldMakeAnInvisibleAnimal()
        {
            JObject o = ShopStock.MakeItem("msh:manta", 0, 0, 0, 0, 0, 1);
            Assert.AreEqual(1.0, (double)o["s"][0], 1e-9, "a purchase must never come out at scale 0");
        }

        [Test]
        public void RoundTripsThroughStorage()
        {
            var items = new[] { Item("msh:manta", "a"), Item("school:scad", "b") };
            string blob = ShopStock.Serialise(items);
            var back = ShopStock.Parse(blob);
            Assert.AreEqual(2, back.Count);
            Assert.AreEqual("msh:manta", (string)back[0]["assetId"]);
            Assert.AreEqual("school:scad", (string)back[1]["assetId"]);
        }

        [Test]
        public void ACorruptBlobLosesTheDisplay_NotTheApp()
        {
            Assert.AreEqual(0, ShopStock.Parse("not json at all").Count);
            Assert.AreEqual(0, ShopStock.Parse("{\"not\":\"an array\"}").Count);
            Assert.AreEqual(0, ShopStock.Parse("").Count);
            Assert.AreEqual(0, ShopStock.Parse(null).Count);
        }

        [Test]
        public void EntriesWithNoAssetIdAreDropped()
        {
            Assert.AreEqual(0, ShopStock.Parse("[{\"id\":\"x\"}]").Count,
                            "an item with nothing to load would build as a placeholder box");
        }

        // ── injection ────────────────────────────────────────────────────────────

        [Test]
        public void PurchasesAreAppendedToTheScene()
        {
            var scene = SceneData.Parse("{\"items\":[{\"id\":\"wreck\",\"assetId\":\"cc0:wreck_chang\"}]}");
            int added = ShopStock.Inject(scene, new[] { Item("msh:manta", "buy_1") });
            Assert.AreEqual(1, added);
            Assert.AreEqual(2, scene.Items().Count);
        }

        [Test]
        public void InjectingTwiceDoesNotDuplicateAPurchase()
        {
            // A map can be rebuilt without the app restarting (Retry, or buying again). If this
            // regresses, one manta becomes two, then four.
            var scene = SceneData.Parse("{\"items\":[]}");
            var stock = new[] { Item("msh:manta", "buy_1") };
            ShopStock.Inject(scene, stock);
            int again = ShopStock.Inject(scene, stock);
            Assert.AreEqual(0, again);
            Assert.AreEqual(1, scene.Items().Count);
        }

        [Test]
        public void ASceneWithNoItemsArrayStillTakesPurchases()
        {
            var scene = SceneData.Parse("{\"name\":\"empty\"}");
            Assert.AreEqual(1, ShopStock.Inject(scene, new[] { Item("msh:manta", "buy_1") }));
            Assert.AreEqual(1, scene.Items().Count);
        }

        [Test]
        public void InjectionNeverThrowsOnNothing()
        {
            Assert.AreEqual(0, ShopStock.Inject(null, new[] { Item("msh:manta") }));
            Assert.AreEqual(0, ShopStock.Inject(SceneData.Parse("{}"), null));
        }

        [Test]
        public void TheInjectedItemIsACopy_NotTheStoredObject()
        {
            // Otherwise the builder mutating an item (it writes nothing today, but it may)
            // would silently rewrite what the player owns.
            var scene = SceneData.Parse("{\"items\":[]}");
            JObject stored = Item("msh:manta", "buy_1");
            ShopStock.Inject(scene, new[] { stored });
            scene.Items()[0].AssetId = "msh:orca";
            Assert.AreEqual("msh:manta", (string)stored["assetId"]);
        }

        // ── placement ────────────────────────────────────────────────────────────

        [Test]
        public void APurchaseLandsInFrontOfTheBuyer_NotInsideThem()
        {
            ShopStock.DropPoint(0, 100, 0, 0, ShopStock.DropDistance, out double x, out double y, out double z);
            Assert.AreEqual(ShopStock.DropDistance, x, 1e-9);
            Assert.AreEqual(100.0, y, 1e-9, "at the buyer's own depth");
            Assert.AreEqual(0.0, z, 1e-9);

            double d = System.Math.Sqrt(x * x + z * z);
            Assert.Greater(d, 10.0, "close enough to see, far enough not to be inside the camera");
        }

        [Test]
        public void TheDropFollowsTheDirectionYouAreFacing()
        {
            ShopStock.DropPoint(0, 0, 0, System.Math.PI / 2, 10, out double x, out double _, out double z);
            Assert.AreEqual(0.0, x, 1e-6);
            Assert.AreEqual(10.0, z, 1e-6);
        }

        [Test]
        public void KeysAreScopedPerMap()
        {
            Assert.AreNotEqual(ShopStock.KeyFor("abc"), ShopStock.KeyFor("def"),
                               "buying at one dive site must not populate another");
            Assert.IsTrue(ShopStock.KeyFor("abc").StartsWith(ShopStock.PrefPrefix));
        }
    }
}
