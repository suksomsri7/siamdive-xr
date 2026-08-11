namespace DiveMap.Core
{
    /// <summary>
    /// When to fetch the trash-game models — before the map is built, after it, or not at all
    /// (WO-P).
    ///
    /// 🔴 The complaint: the player enters a world map, the tour auto-starts 0.6 s later and seeds
    /// three pieces of litter and coins IN THE FIRST FRAME — at which point the model templates
    /// have not begun loading, because they only start when the tour starts. So the first thing
    /// anyone sees when the game begins is a white sphere and a flat brown disc. It is the worst
    /// possible moment for the placeholder to show, and the user photographed exactly that.
    ///
    /// 🔴 Why this is a rule and not just "load them earlier". The six models are <b>26.07 MB</b>
    /// on the CDN (measured: coin 4.46, can 4.34, bottle 3.27, bag 5.55, net 4.80, tire 3.64), and
    /// this app shares an iOS memory budget with React Native and a resident WebView — the ceiling
    /// is live, it has already killed the app once, and 26 MB of meshes and textures is not a
    /// rounding error. Two costs have to stay at zero:
    ///
    ///   PEAK MEMORY — so preloading only ever happens when the tour is CERTAIN to start
    ///                 (<c>ArenaEntry.ShouldAutoPlay</c>). Then the templates are resident a few
    ///                 seconds earlier than they would have been anyway, and the steady state is
    ///                 unchanged. On a map that will not auto-play, nothing is fetched and the
    ///                 behaviour is exactly what shipped.
    ///   MAP LOAD TIME — so a fetch that needs the NETWORK waits until the build has finished,
    ///                 where it cannot compete for bandwidth with the map's own GLBs. Only a fetch
    ///                 that is already on disk is allowed to run during the build, because a local
    ///                 read costs no bandwidth and finishes inside the build's own seconds.
    ///
    /// That last distinction is what actually fixes the reported case: the templates go through
    /// <c>AssetCacheStore</c>, so on any device that has played the game once they are local, and
    /// a local read started at fetch time is comfortably done before the tour seeds its first
    /// piece. The cold-cache case is honestly not fixed here — it still shows primitives for a few
    /// seconds — and that is what the upgrade-on-arrival path exists for.
    /// </summary>
    public static class GamePreload
    {
        /// <summary>When the trash/coin templates should be fetched for this map.</summary>
        public enum When
        {
            /// <summary>Not at all — the tour is not certain, so do not spend the memory.</summary>
            Never = 0,

            /// <summary>
            /// Start now, alongside the map build. Only ever chosen when every model is already
            /// in the on-disk cache, so this is a local read: no bandwidth, no download to lose a
            /// race with, and it lands before the tour seeds its first piece.
            /// </summary>
            DuringBuild = 1,

            /// <summary>
            /// Start once the build has finished. The models have to come over the network, and
            /// during the build that would compete with the map's own assets — which is a
            /// regression the player would feel on every map, to fix a placeholder they see on
            /// some. After the build it is free, and it still buys the auto-tour's 0.6 s.
            /// </summary>
            AfterBuild = 2,
        }

        /// <summary>
        /// Decide, from the three facts that matter.
        ///
        /// <paramref name="autoPlay"/> is <c>ArenaEntry.ShouldAutoPlay</c> — the app's existing
        /// answer to "is this map going to start playing itself", which is also the only case
        /// where spending the memory up front is certainly not wasted. It is checked FIRST and it
        /// is a veto: no amount of "but it is already cached" makes it right to hold 26 MB for a
        /// game the player may never open.
        /// </summary>
        public static When Decide(bool autoPlay, bool cachedOnDisk, bool online)
        {
            if (!autoPlay) return When.Never;
            if (cachedOnDisk) return When.DuringBuild;   // local read — free, and early enough to matter
            if (!online) return When.Never;              // nothing to fetch from, and no disk copy
            return When.AfterBuild;                      // download, but never against the map's own
        }

        /// <summary>Convenience for the call sites, which each care about one moment.</summary>
        public static bool AtBuildStart(When w) => w == When.DuringBuild;

        /// <summary>Convenience for the call sites, which each care about one moment.</summary>
        public static bool AtBuildEnd(When w) => w == When.AfterBuild;
    }
}
