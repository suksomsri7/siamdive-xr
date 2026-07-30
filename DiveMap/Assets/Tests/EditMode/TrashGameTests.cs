using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// P3 — the clean-up game's rules. Cadence and scoring are exactly the things that feel fine
    /// for thirty seconds and then turn out to spawn forever or pay nothing, so they are pinned.
    /// </summary>
    public class TrashGameTests
    {
        [Test]
        public void KindTable_IsTheWebs()
        {
            Assert.AreEqual(5, TrashGame.Kinds.Length);
            Assert.AreEqual("can", TrashGame.Kinds[0].Key);
            Assert.AreEqual(2, TrashGame.Kinds[0].Points);
            Assert.AreEqual(6, TrashGame.Kinds[4].Points, "the fishing net is the prize");
            Assert.AreEqual(100, TrashGame.TotalWeight);
        }

        [Test]
        public void Pick_FollowsTheWeights()
        {
            Assert.AreEqual("can", TrashGame.Pick(0f).Key);
            Assert.AreEqual("can", TrashGame.Pick(0.27f).Key);
            Assert.AreEqual("bottle", TrashGame.Pick(0.30f).Key);
            Assert.AreEqual("plastic", TrashGame.Pick(0.55f).Key);
            Assert.AreEqual("tire", TrashGame.Pick(0.76f).Key);
            Assert.AreEqual("net", TrashGame.Pick(0.95f).Key);
            Assert.AreEqual("net", TrashGame.Pick(1.5f).Key, "out of range still returns something");
        }

        [Test]
        public void Pick_IsRoughlyTheDeclaredDistribution()
        {
            int cans = 0, nets = 0;
            const int n = 10000;
            for (int i = 0; i < n; i++)
            {
                string k = TrashGame.Pick(i / (float)n).Key;
                if (k == "can") cans++;
                else if (k == "net") nets++;
            }
            Assert.AreEqual(0.28f, cans / (float)n, 0.01f);
            Assert.AreEqual(0.12f, nets / (float)n, 0.01f);
        }

        [Test]
        public void Spawn_RespectsTheCapAndTheInterval()
        {
            Assert.IsTrue(TrashGame.ShouldSpawn(0, 10f, 0f));
            Assert.IsFalse(TrashGame.ShouldSpawn(0, 3f, 0f), "5 s between pieces");
            Assert.IsFalse(TrashGame.ShouldSpawn(TrashGame.MaxTrash, 100f, 0f), "30 pieces is the cap");
            Assert.IsTrue(TrashGame.ShouldSpawn(TrashGame.MaxTrash - 1, 100f, 0f));
        }

        [Test]
        public void CoinCycle_IsEverySixtySeconds()
        {
            Assert.IsFalse(TrashGame.ShouldCycleCoins(59f, 0f));
            Assert.IsTrue(TrashGame.ShouldCycleCoins(60f, 0f));
            Assert.AreEqual(3, TrashGame.CoinsPerCycle);
        }

        [Test]
        public void Score_PaysDoubleForCatchingItBeforeItLands()
        {
            TrashGame.Kind bag = TrashGame.Pick(0.55f);   // plastic, 3 points
            int landed = TrashGame.Score(bag, 0f, 0);
            int caught = TrashGame.Score(bag, 1f, 0);
            Assert.AreEqual(3, landed);
            Assert.AreEqual(6, caught);
        }

        [Test]
        public void Score_BuildsWithTheCombo_AndCapsAtTen()
        {
            TrashGame.Kind can = TrashGame.Kinds[0];
            Assert.AreEqual(2, TrashGame.Score(can, 0f, 0));
            Assert.AreEqual(4, TrashGame.Score(can, 0f, 10), "×2 at a full combo");
            Assert.AreEqual(TrashGame.Score(can, 0f, 10), TrashGame.Score(can, 0f, 99), "combo is capped");
        }

        [Test]
        public void Score_DoublesForABonusCoin()
        {
            TrashGame.Kind net = TrashGame.Kinds[4];
            Assert.AreEqual(TrashGame.Score(net, 0f, 0) * 2, TrashGame.Score(net, 0f, 0, bonus: true));
        }

        [Test]
        public void Combo_ResetsWhenTheKindChanges()
        {
            Assert.AreEqual(1, TrashGame.NextCombo("can", "can", 0));
            Assert.AreEqual(2, TrashGame.NextCombo("can", "can", 1));
            Assert.AreEqual(0, TrashGame.NextCombo("can", "tire", 5), "a different kind breaks the run");
            Assert.AreEqual(TrashGame.MaxCombo, TrashGame.NextCombo("can", "can", TrashGame.MaxCombo));
        }

        [Test]
        public void HeightFactor_IsOneAtTheSurfaceAndZeroOnTheSand()
        {
            Assert.AreEqual(1f, TrashGame.HeightFactor(238f, 0f, 238f), 1e-4f);
            Assert.AreEqual(0f, TrashGame.HeightFactor(0f, 0f, 238f), 1e-4f);
            Assert.AreEqual(0.5f, TrashGame.HeightFactor(119f, 0f, 238f), 1e-3f);
            Assert.AreEqual(0f, TrashGame.HeightFactor(-50f, 0f, 238f), 1e-4f, "never negative");
        }

        [Test]
        public void LandedLitter_BlinksThenExpires()
        {
            Assert.IsFalse(TrashGame.Expired(29f));
            Assert.IsTrue(TrashGame.Expired(31f));
            Assert.IsTrue(TrashGame.VisibleWhileFading(10f, 0f), "solid before it starts fading");
            // In the blink window visibility alternates with time.
            bool a = TrashGame.VisibleWhileFading(27f, 0.0f);
            bool b = TrashGame.VisibleWhileFading(27f, 0.2f);
            Assert.AreNotEqual(a, b);
        }
    }
}
