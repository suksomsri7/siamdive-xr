using System;

namespace DiveMap.Core
{
    /// <summary>
    /// Which baselines a depth-scaling system may refresh, and why refreshing the wrong one
    /// compounds (WO-MERGE DARK).
    ///
    /// 🔴 The measurement that started this. The user photographed the diagnostic badge on a
    /// healthy frame and it read <c>fog on 489-8797</c>. <c>AppBoot.SetupLighting</c> authors
    /// <c>500 … 9000</c>. Both ends are down by the SAME factor — 0.9774 — and the app was in
    /// View mode on a map reached by switching. So the live fog is not the authored fog: it has
    /// been multiplied by the depth visibility scale and left that way.
    ///
    /// One multiplication is correct and intended: <c>DepthAtmosphere</c> exists to close the fog
    /// in as the camera goes deeper. The danger is the SECOND one, and the mechanism is a
    /// feedback loop:
    ///
    ///   1. it keeps a "baseline" — what some other system wants at the surface —
    ///      and each frame writes <c>baseline × vis</c> into RenderSettings;
    ///   2. it re-captures that baseline from the LIVE values whenever it notices somebody
    ///      else has written any of them;
    ///   3. …and the check was all-or-nothing: a change to the AMBIENT re-captured the fog
    ///      DISTANCES too — from values this component had itself already scaled.
    ///
    /// So every stray ambient write multiplies the fog distances by <c>vis</c> again, and the
    /// fog walks in toward the camera geometrically. <see cref="Decay"/> is that arithmetic:
    /// at vis 0.977 it takes ~130 re-captures to close 9000 units down to 500, and ~250 to reach
    /// 60 — at which point everything past a few metres is solid fog colour and the screen is a
    /// flat dark navy with a working HUD over it. Whether the app ever reaches that many is a
    /// question for the log, not for this comment; what this file guarantees is that it cannot
    /// happen through the ambient channel any more.
    ///
    /// The rule: <b>each baseline follows only its own signal.</b> Ambient churn may re-baseline
    /// ambient. Only a fog-distance change may re-baseline fog distances.
    /// </summary>
    public static class AtmosphereBaseline
    {
        /// <summary>Which groups of the baseline should be re-read from the live settings.</summary>
        [Flags]
        public enum Refresh
        {
            None = 0,
            Ambient = 1 << 0,
            FogColor = 1 << 1,
            FogDistance = 1 << 2,
            All = Ambient | FogColor | FogDistance,
        }

        /// <summary>
        /// Decide what to refresh.
        ///
        /// <paramref name="haveBase"/> false means there is no baseline yet — a fresh map — and
        /// everything is read at once. After that, each flag is raised only by a change in the
        /// values it governs, which is the whole fix: the old code raised all three whenever any
        /// one of them moved.
        /// </summary>
        public static Refresh Decide(bool haveBase, bool ambientChanged, bool fogColorChanged,
                                     bool fogDistanceChanged)
        {
            if (!haveBase) return Refresh.All;

            Refresh r = Refresh.None;
            if (ambientChanged) r |= Refresh.Ambient;
            if (fogColorChanged) r |= Refresh.FogColor;
            if (fogDistanceChanged) r |= Refresh.FogDistance;
            return r;
        }

        /// <summary>
        /// What <paramref name="authored"/> becomes after <paramref name="applications"/> rounds
        /// of being scaled by <paramref name="vis"/> — the compounding, written down.
        ///
        /// Exists to be asserted against, not called in the app: the point of the fix is that the
        /// only reachable value of <paramref name="applications"/> is 1.
        /// </summary>
        public static double Decay(double authored, double vis, int applications)
        {
            if (applications <= 0) return authored;
            return authored * Math.Pow(vis, applications);
        }

        /// <summary>
        /// How many compounding rounds it takes for <paramref name="authored"/> to fall to
        /// <paramref name="target"/>. Answers "how far is this drift from being a fog wall?"
        /// in one number, which is what a log line needs to be worth reading.
        /// Returns -1 when the scale cannot get there (vis at or above 1, or a target above the
        /// value it starts from).
        /// </summary>
        public static int RoundsToReach(double authored, double vis, double target)
        {
            if (vis <= 0.0 || vis >= 1.0) return -1;
            if (target >= authored || target <= 0.0 || authored <= 0.0) return -1;
            return (int)Math.Ceiling(Math.Log(target / authored) / Math.Log(vis));
        }
    }
}
