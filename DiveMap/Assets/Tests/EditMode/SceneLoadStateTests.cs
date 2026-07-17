using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    public class SceneLoadStateTests
    {
        [Test]
        public void FreshState_CanSave()
        {
            var s = new SceneLoadState();
            Assert.AreEqual(0, s.PendingCount);
            Assert.IsTrue(s.CanSave);
        }

        [Test]
        public void WhileLoading_CannotSave()
        {
            var s = new SceneLoadState();
            s.BeginLoad(3);
            Assert.AreEqual(3, s.PendingCount);
            Assert.IsFalse(s.CanSave, "Must not allow save while loads are pending (108->31 lesson).");

            s.CompleteLoad();
            Assert.IsFalse(s.CanSave);
            s.CompleteLoad();
            Assert.IsFalse(s.CanSave);
            s.CompleteLoad();
            Assert.IsTrue(s.CanSave, "All loads done -> save allowed again.");
        }

        [Test]
        public void CompleteLoad_ClampsAtZero()
        {
            var s = new SceneLoadState();
            s.CompleteLoad(); // over-complete
            Assert.AreEqual(0, s.PendingCount);
            Assert.IsTrue(s.CanSave);
        }

        [Test]
        public void FailedLoad_DoesNotStickAboveZero()
        {
            // Even if a load fails, its CompleteLoad must run so the counter drains.
            var s = new SceneLoadState();
            s.BeginLoad();          // begin GLB fetch
            s.CompleteLoad();       // fetch failed -> still completed
            Assert.IsTrue(s.CanSave);
        }

        [Test]
        public void Reset_ClearsPending()
        {
            var s = new SceneLoadState();
            s.BeginLoad(5);
            s.Reset();
            Assert.AreEqual(0, s.PendingCount);
            Assert.IsTrue(s.CanSave);
        }

        [Test]
        public void BeginLoad_NonPositive_NoOp()
        {
            var s = new SceneLoadState();
            s.BeginLoad(0);
            s.BeginLoad(-4);
            Assert.AreEqual(0, s.PendingCount);
        }
    }
}
