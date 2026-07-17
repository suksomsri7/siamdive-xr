namespace DiveMap.Core
{
    /// <summary>
    /// Guards against saving a scene while asynchronous loads are still pending.
    ///
    /// Port of the hard web lesson recorded in QC_PLAN §1.1 ("Save guard"): the web
    /// autosave once overwrote a scene mid-load and dropped 108→31 items. Rule:
    /// NEVER save while pendingCount > 0.
    ///
    /// Each in-flight async load (GLB fetch, sub-scene, texture) increments the
    /// counter on begin and decrements on completion (success OR failure — a failed
    /// load must not leave the counter stuck above zero and block saving forever).
    /// </summary>
    public sealed class SceneLoadState
    {
        public int PendingCount { get; private set; }

        /// <summary>True only when nothing is in flight.</summary>
        public bool CanSave => PendingCount == 0;

        /// <summary>Register N new pending loads (default 1).</summary>
        public void BeginLoad(int count = 1)
        {
            if (count <= 0) return;
            PendingCount += count;
        }

        /// <summary>Mark one pending load finished (clamped at zero).</summary>
        public void CompleteLoad(int count = 1)
        {
            PendingCount -= count;
            if (PendingCount < 0) PendingCount = 0;
        }

        /// <summary>Reset to a clean idle state.</summary>
        public void Reset() => PendingCount = 0;
    }
}
