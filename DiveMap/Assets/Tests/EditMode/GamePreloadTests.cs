using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// When the trash-game models may be fetched (WO-P).
    ///
    /// Two costs this rule exists to keep at zero, and both are things this project has already
    /// been burned by:
    ///   • 26.07 MB of models resident for a game the player never opens — on a build that shares
    ///     an iOS memory budget with React Native and a WebView, and has been killed by that
    ///     ceiling once already;
    ///   • a 26 MB download racing the map's own GLBs and making every map load slower, to fix a
    ///     placeholder that only shows on some.
    /// </summary>
    public class GamePreloadTests
    {
        [Test]
        public void NoAutoPlay_FetchesNothing_HoweverCheapItWouldBe()
        {
            // 🔴 The memory veto, and it is checked first on purpose: "but it is already cached"
            // is not a reason to hold 26 MB for a game that may never be opened. Every
            // combination below is a map that will not start playing itself.
            foreach (bool cached in new[] { false, true })
            foreach (bool online in new[] { false, true })
                Assert.AreEqual(GamePreload.When.Never,
                                GamePreload.Decide(autoPlay: false, cachedOnDisk: cached, online: online),
                                $"cached={cached} online={online}");
        }

        [Test]
        public void AutoPlayAndAlreadyOnDisk_LoadsDuringTheBuild()
        {
            // The case that fixes the user's screenshot: a world map that auto-tours, on a device
            // that has played before. A local read costs no bandwidth, so it may start at once and
            // has the whole build to finish in — comfortably before the tour seeds its first piece.
            Assert.AreEqual(GamePreload.When.DuringBuild,
                            GamePreload.Decide(autoPlay: true, cachedOnDisk: true, online: true));
        }

        [Test]
        public void AlreadyOnDisk_DoesNotNeedTheNetwork()
        {
            // Offline is not a reason to skip a read from local disk — that is the whole value of
            // the cache, and the offline map path is a supported way to use this app.
            Assert.AreEqual(GamePreload.When.DuringBuild,
                            GamePreload.Decide(autoPlay: true, cachedOnDisk: true, online: false));
        }

        [Test]
        public void AutoPlayButColdCache_WaitsForTheBuildToFinish()
        {
            // 🔴 The map-load-time guard. The models must come over the network, and during the
            // build that bandwidth belongs to the map the player is waiting for.
            Assert.AreEqual(GamePreload.When.AfterBuild,
                            GamePreload.Decide(autoPlay: true, cachedOnDisk: false, online: true));
        }

        [Test]
        public void ColdCacheAndOffline_FetchesNothing()
        {
            // Nothing to read and nowhere to read it from. Primitives plus the upgrade path.
            Assert.AreEqual(GamePreload.When.Never,
                            GamePreload.Decide(autoPlay: true, cachedOnDisk: false, online: false));
        }

        [Test]
        public void OnlyOneMomentIsEverChosen()
        {
            // The two call sites are separate lines in AppBoot; a rule that could answer "both"
            // would start the load twice. The in-flight guard would catch it, but the rule should
            // not need saving.
            foreach (bool a in new[] { false, true })
            foreach (bool c in new[] { false, true })
            foreach (bool o in new[] { false, true })
            {
                GamePreload.When w = GamePreload.Decide(a, c, o);
                Assert.IsFalse(GamePreload.AtBuildStart(w) && GamePreload.AtBuildEnd(w),
                               $"autoPlay={a} cached={c} online={o}");
            }
        }

        [Test]
        public void TheHelpersAgreeWithTheEnum()
        {
            Assert.IsTrue(GamePreload.AtBuildStart(GamePreload.When.DuringBuild));
            Assert.IsFalse(GamePreload.AtBuildStart(GamePreload.When.AfterBuild));
            Assert.IsFalse(GamePreload.AtBuildStart(GamePreload.When.Never));

            Assert.IsTrue(GamePreload.AtBuildEnd(GamePreload.When.AfterBuild));
            Assert.IsFalse(GamePreload.AtBuildEnd(GamePreload.When.DuringBuild));
            Assert.IsFalse(GamePreload.AtBuildEnd(GamePreload.When.Never));
        }

        [Test]
        public void PreloadingImpliesTheTourIsCertain()
        {
            // Stated as an invariant rather than left to the reader: if this ever preloads without
            // autoPlay, the memory argument above has been quietly broken.
            foreach (bool c in new[] { false, true })
            foreach (bool o in new[] { false, true })
                Assert.AreEqual(GamePreload.When.Never, GamePreload.Decide(false, c, o));
        }
    }
}
