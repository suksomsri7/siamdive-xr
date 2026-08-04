using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    public class LoadProgressTests
    {
        [Test]
        public void FreshState_DrawsNothing()
        {
            var p = new LoadProgress();
            Assert.IsFalse(p.Shown);
            Assert.IsFalse(p.Visible);
            Assert.IsFalse(p.BlocksInput);
            Assert.AreEqual(0f, p.Alpha, 1e-4f);
        }

        [Test]
        public void Show_CoversScreenAtZeroPercent()
        {
            var p = new LoadProgress();
            p.Show();
            Assert.IsTrue(p.Visible);
            Assert.IsTrue(p.BlocksInput, "The cover must swallow taps aimed at the UI behind it.");
            Assert.AreEqual(0, p.Percent);
            Assert.AreEqual(1f, p.Alpha, 1e-4f);
        }

        [Test]
        public void TotalUnknown_ReadsZeroNotFull()
        {
            // Between "Show" and the moment SceneBuilder has parsed the scene, nobody knows how
            // many items there are. That must read 0%, never 100% (which would look finished).
            var p = new LoadProgress();
            p.Show();
            p.Report(0, 0);
            Assert.AreEqual(0f, p.Fraction, 1e-4f);
        }

        [Test]
        public void Fraction_CountsLoadedAndFailedAlike()
        {
            var p = new LoadProgress();
            p.Show();
            p.SetTotal(100);
            p.Report(40, 10);
            Assert.AreEqual(0.5f, p.Fraction, 1e-4f);
            Assert.AreEqual(50, p.Percent);
        }

        [Test]
        public void EveryFileFailed_StillReachesFull()
        {
            // A map whose URLs are all dead must still let the player in — a bar that counts
            // only successes would stop dead and the cover would never come off.
            var p = new LoadProgress();
            p.Show();
            p.SetTotal(8);
            p.Report(0, 8);
            Assert.AreEqual(1f, p.Fraction, 1e-4f);
            Assert.AreEqual(100, p.Percent);
        }

        [Test]
        public void Report_NeverExceedsTotalAndNeverWalksBack()
        {
            var p = new LoadProgress();
            p.Show();
            p.SetTotal(10);
            p.Report(12, 3);            // over-count (extra fish templates etc.)
            Assert.AreEqual(10, p.Done);
            p.Report(4, 0);             // a late, staler reading
            Assert.AreEqual(10, p.Done, "The bar must not run backwards inside one build.");
        }

        [Test]
        public void SetTotal_ShrinkingClampsDone()
        {
            var p = new LoadProgress();
            p.Show();
            p.SetTotal(10);
            p.Report(10, 0);
            p.SetTotal(4);
            Assert.AreEqual(4, p.Done);
            Assert.AreEqual(1f, p.Fraction, 1e-4f);
        }

        [Test]
        public void Complete_FillsBarThenFadesOut()
        {
            var p = new LoadProgress();
            p.Show();
            p.SetTotal(20);
            p.Report(9, 1);
            p.Complete();
            Assert.AreEqual(100, p.Percent);
            Assert.IsTrue(p.Visible);
            Assert.IsFalse(p.BlocksInput, "Once the map is playable the fade must not eat taps.");

            Assert.IsTrue(p.Tick(LoadProgress.FadeSeconds * 0.5f));
            Assert.IsTrue(p.Visible);
            Assert.Less(p.Alpha, 1f);

            Assert.IsFalse(p.Tick(LoadProgress.FadeSeconds * 0.6f));
            Assert.IsFalse(p.Visible);
            Assert.IsFalse(p.Shown);
            Assert.AreEqual(0f, p.Alpha, 1e-4f);
        }

        [Test]
        public void Cancel_MidBuild_TakesCoverDownAtOnce()
        {
            // Retry / map switch: SceneBuilder.DiscardInFlight cancels the build, and whoever
            // cancels cleans up their own mess — including this.
            var p = new LoadProgress();
            p.Show();
            p.SetTotal(50);
            p.Report(20, 0);
            p.Cancel();
            Assert.IsFalse(p.Visible);
            Assert.IsFalse(p.Shown);
            Assert.AreEqual(0f, p.Alpha, 1e-4f);
            Assert.IsFalse(p.Tick(1f), "A cancelled run must stay down.");
        }

        [Test]
        public void Show_AfterACancelledRun_StartsCleanAtZero()
        {
            var p = new LoadProgress();
            p.Show();
            p.SetTotal(50);
            p.Report(31, 2);
            p.Cancel();

            p.Show();                       // the retry
            Assert.AreEqual(0, p.Done);
            Assert.AreEqual(0, p.Total);
            Assert.AreEqual(0, p.Percent);
            Assert.IsTrue(p.BlocksInput);
        }

        [Test]
        public void Show_OverAFinishedRun_StartsCleanAtZero()
        {
            var p = new LoadProgress();
            p.Show();
            p.SetTotal(6);
            p.Report(6, 0);
            p.Complete();

            p.Show();                       // switching to another map
            Assert.IsFalse(p.Finished);
            Assert.AreEqual(0, p.Percent);
            Assert.AreEqual(1f, p.Alpha, 1e-4f);
        }

        [Test]
        public void Watchdog_NeverLeavesAnOpaqueScreenUp()
        {
            var p = new LoadProgress();
            p.Show();
            p.SetTotal(30);
            p.Report(3, 0);                 // …and then the build dies without a word

            Assert.IsTrue(p.Tick(LoadProgress.StuckSeconds - 1f));
            Assert.IsTrue(p.Visible);

            Assert.IsFalse(p.Tick(1f));
            Assert.IsFalse(p.Visible);
            Assert.IsFalse(p.Shown);
        }

        [Test]
        public void Complete_WithoutShow_IsANoOp()
        {
            // QC runs (-qcshot) build maps with no overlay at all; nothing must resurrect one.
            var p = new LoadProgress();
            p.SetTotal(5);
            p.Report(5, 0);
            p.Complete();
            Assert.IsFalse(p.Shown);
            Assert.IsFalse(p.Visible);
            Assert.IsFalse(p.Tick(0.016f));
        }
    }
}
