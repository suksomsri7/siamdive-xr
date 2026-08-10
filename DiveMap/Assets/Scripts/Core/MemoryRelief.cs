namespace DiveMap.Core
{
    /// <summary>
    /// When it is worth answering an iOS memory warning (WO-MERGE P1c).
    ///
    /// The rule is separated from the handler for one reason: the handler cannot be tested on this
    /// machine and the rule is the part that can be got wrong. iOS does not send one warning, it
    /// sends a BURST — the system is walking down its list of apps and can deliver several within
    /// a second, and each answer costs a full <c>Resources.UnloadUnusedAssets</c> plus a blocking
    /// GC. Running that four times back to back on a phone already under pressure is a visible
    /// freeze added to a problem that was only nearly fatal, and it is a plausible way of turning
    /// "the app was slow for a moment" into "the app was killed".
    ///
    /// So: answer the first warning immediately, then ignore anything that arrives inside
    /// <see cref="MinGapSeconds"/>. Never the other way round — a delayed first answer is the one
    /// case where the memory really was needed.
    /// </summary>
    public static class MemoryRelief
    {
        /// <summary>
        /// Quiet period after a relief pass. A few seconds: long enough to swallow a burst,
        /// short enough that a genuinely worsening situation gets a second pass while the app
        /// is still alive to give one.
        /// </summary>
        public const float MinGapSeconds = 5f;

        /// <summary>
        /// Should this warning be acted on? <paramref name="lastRunAt"/> is negative before the
        /// first pass, which must always be allowed however early in the process it arrives —
        /// a zero-initialised "last run" plus a clock that starts near zero would otherwise
        /// swallow the very first warning of the session.
        /// </summary>
        public static bool ShouldRelieve(float lastRunAt, float now, float minGap = MinGapSeconds)
        {
            if (lastRunAt < 0f) return true;
            return now - lastRunAt >= minGap;
        }
    }
}
