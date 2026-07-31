namespace DiveMap.Core
{
    /// <summary>
    /// Who gets dropped straight into the game when a map finishes loading, instead of being left
    /// looking at it from the outside.
    ///
    /// The web decides this in one line (builder.html:3544):
    /// <code>
    ///   if (navigator.onLine &amp;&amp; !HOLO_MODE &amp;&amp; (_tourWorld || (_isWorldMap &amp;&amp; !_canEdit)))
    ///       _startArenaPlay();
    ///   //  _tourWorld  = arenaPlay || _warpPlay
    ///   //  _isWorldMap = site.accountId === ADMIN_ACCT
    /// </code>
    /// and <c>_startArenaPlay</c> is "player mode, then <c>enterTour(true)</c> 600 ms later".
    ///
    /// 🔎 This port had only the warp half wired up, so opening an official world through
    /// 🎮 เล่นเกม! loaded the map and stopped — the player landed in the orbit view with a menu,
    /// with no joystick, no coins and no falling trash, and had to find the tour button to start
    /// playing at all. On the web that is one tap; here it was a hunt. The banner was ported before
    /// the web itself fixed this (their v.0731), which is how the missing half came across.
    ///
    /// Kept as a pure function because every clause is a rule someone will want to change, and
    /// each one is a way for the player to end up in the wrong mode.
    /// </summary>
    public static class ArenaEntry
    {
        /// <summary>Milliseconds the web waits before entering the tour (its <c>setTimeout</c>).</summary>
        public const float StartDelaySeconds = 0.6f;

        /// <summary>
        /// Should this map start playing itself?
        /// </summary>
        /// <param name="accountId">the map owner's account id, as the API reports it.</param>
        /// <param name="canEdit">whether this device may edit the map — an owner opening their own
        /// world wants the builder, not to be thrown into a game they cannot leave into edit mode.</param>
        /// <param name="arenaPlay">arrived through 🎮 เล่นเกม! (the web's <c>arenaPlay</c>).</param>
        /// <param name="arrivedByWarp">arrived through a warp gate (the web's <c>_warpPlay</c>).</param>
        /// <param name="online">offline maps still open and can be toured, but coins cannot be
        /// banked, so the web does not auto-start the game there.</param>
        /// <param name="arMode">AR and holomap bring their own camera and overlay; the web had to
        /// add this clause after auto-tour started drawing a joystick over the AR view.</param>
        public static bool ShouldAutoPlay(string accountId, bool canEdit, bool arenaPlay,
                                          bool arrivedByWarp, bool online, bool arMode)
        {
            if (!online || arMode) return false;
            bool tourWorld = arenaPlay || arrivedByWarp;
            bool worldMap = string.Equals(accountId, MapDirectory.AdminAccountId,
                                          System.StringComparison.Ordinal);
            return tourWorld || (worldMap && !canEdit);
        }
    }
}
