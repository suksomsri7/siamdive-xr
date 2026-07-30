using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// P3b — the purse. These rules exist so that a dropped request or a pickup racing a server
    /// reply cannot cost a player coins they actually collected, which is the fastest way to lose
    /// someone's trust in a game economy.
    /// </summary>
    public class WalletTests
    {
        [Test]
        public void Reconcile_AppliesPendingOnTopOfTheServer()
        {
            Assert.AreEqual(640, Wallet.Reconcile(600, 40, 0));
            Assert.AreEqual(560, Wallet.Reconcile(600, 0, 40));
            Assert.AreEqual(610, Wallet.Reconcile(600, 40, 30));
        }

        [Test]
        public void Reconcile_NeverGoesNegative()
        {
            Assert.AreEqual(0, Wallet.Reconcile(10, 0, 999));
            Assert.AreEqual(0, Wallet.Reconcile(0, 0, 5));
        }

        [Test]
        public void EarnAndSpend_ClampSensibly()
        {
            Assert.AreEqual(605, Wallet.Earn(600, 5));
            Assert.AreEqual(600, Wallet.Earn(600, 0));
            Assert.AreEqual(600, Wallet.Earn(600, -10), "a negative earn is not a spend");
            Assert.AreEqual(595, Wallet.Spend(600, 5));
            Assert.AreEqual(0, Wallet.Spend(3, 10), "a purchase cannot take you below zero");
        }

        [Test]
        public void CanAfford_IsTheGateForAPurchase()
        {
            Assert.IsTrue(Wallet.CanAfford(600, 600));
            Assert.IsTrue(Wallet.CanAfford(600, 0));
            Assert.IsFalse(Wallet.CanAfford(599, 600));
            Assert.IsFalse(Wallet.CanAfford(600, -1), "a negative price is not a gift");
        }

        [Test]
        public void HasPending_OnlyWhenSomethingIsActuallyOwed()
        {
            Assert.IsFalse(Wallet.HasPending(0, 0));
            Assert.IsTrue(Wallet.HasPending(1, 0));
            Assert.IsTrue(Wallet.HasPending(0, 1));
        }

        [Test]
        public void NewPlayers_AreSeededRatherThanZeroed()
        {
            Assert.IsTrue(Wallet.NeedsSeed(serverHasWallet: false));
            Assert.IsFalse(Wallet.NeedsSeed(serverHasWallet: true));
            Assert.AreEqual(600, Wallet.StartingCoins);
        }

        [Test]
        public void APickupThatRacesTheServerReplyIsNotLost()
        {
            // A save goes out with +10; a +5 pickup lands while it is in flight; the server replies
            // 610. The player must see 615, not 610.
            const int serverAfterSave = 610;
            Assert.AreEqual(615, Wallet.Reconcile(serverAfterSave, 5, 0));
        }
    }
}
