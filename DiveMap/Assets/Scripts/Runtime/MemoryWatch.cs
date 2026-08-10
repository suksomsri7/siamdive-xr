using DiveMap.Core;
using DiveMap.Runtime.Marine;
using UnityEngine;

namespace DiveMap.Runtime
{
    /// <summary>
    /// Answer iOS memory warnings, and — at least as importantly — LEAVE A RECORD (WO-MERGE P1c).
    ///
    /// 🔴 Why this exists: the app was killed twice entering Htms Chang from the merged iOS build,
    /// with no crash log of its own. That signature is jetsam — the system reclaiming a process
    /// that asked for more than its share — and jetsam is silent by construction: there is no
    /// exception, no stack, nothing in the player log, because the process is simply gone. The
    /// working theory is that React Native, a resident WebView and Unity share one budget and
    /// Chang's load peak (10 schools, ~1,100 fish, a whale and a wreck) is what tips it over.
    ///
    /// A theory is not a diagnosis, and the next device round has to be able to settle it. So the
    /// first job of this class is EVIDENCE: <c>Application.lowMemory</c> is iOS's
    /// <c>didReceiveMemoryWarning</c>, and it fires shortly before jetsam does. A device log with
    /// these lines in it, at the moment the app died, is proof; a log without them says the cause
    /// was something else entirely and saves the next session from optimising the wrong thing.
    /// That is why the log line carries the map, the fish counts and the mode rather than just
    /// "low memory".
    ///
    /// The second job is relief, and it is best-effort by nature. <c>Resources.UnloadUnusedAssets</c>
    /// is the only lever that reaches textures and meshes nothing references any more — which after
    /// a map switch is most of the previous reef — and a GC pass is needed first, because an asset
    /// is only "unused" once the managed reference to it has actually been collected. Neither is
    /// free, hence <see cref="MemoryRelief"/> and its quiet period.
    ///
    /// Runs in the standalone build too. Answering a memory warning is right in both products, and
    /// the standalone app is what the fish QC is filmed on — a freeze there would be reported.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MemoryWatch : MonoBehaviour
    {
        private static MemoryWatch _instance;

        /// <summary>Negative until the first pass — see <see cref="MemoryRelief.ShouldRelieve"/>.</summary>
        private float _lastReliefAt = -1f;

        /// <summary>How many warnings this session, answered or not (part of the evidence).</summary>
        private int _warnings;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null) return;
            var go = new GameObject("MemoryWatch");
            _instance = go.AddComponent<MemoryWatch>();
            DontDestroyOnLoad(go);
        }

        private void OnEnable() => Application.lowMemory += OnLowMemory;

        private void OnDisable() => Application.lowMemory -= OnLowMemory;

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void OnLowMemory()
        {
            _warnings++;
            float now = Time.realtimeSinceStartup;

            // 🔴 LogWarning, not Log. This has to be findable in a device log that was captured
            // for a completely different reason, by somebody scrolling for the last thing the app
            // said before it disappeared. Everything needed to tell "Chang's load peak" apart from
            // "a slow leak over five maps" is on the line: which map, how big its reef is, and
            // whether this is the first warning or the ninth.
            AppBoot boot = FindFirstObjectByType<AppBoot>();
            string map = boot != null ? boot.CurrentMapId : "(none)";
            int schools = FishSchoolSystem.TotalSchools;
            int glb = FishSchoolSystem.GlbSchools;

            Debug.LogWarning(
                $"[Memory] iOS LOW MEMORY WARNING #{_warnings} — map={map} schools={glb}/{schools} " +
                $"embedded={NativeBridge.EmbeddedInHost} eco={NativeBoot.EcoMode} " +
                $"mono={System.GC.GetTotalMemory(false) / (1024 * 1024)}MB " +
                $"t={now:0.0}s · this is the warning iOS sends shortly before it kills the process");

            if (!MemoryRelief.ShouldRelieve(_lastReliefAt, now))
            {
                // A burst, not a second event. Counted and named so the log still shows how hard
                // the system was pushing, without paying for another unload.
                Debug.LogWarning($"[Memory] warning #{_warnings} inside the " +
                                 $"{MemoryRelief.MinGapSeconds}s quiet period — relief skipped");
                return;
            }
            _lastReliefAt = now;

            // GC first: Resources.UnloadUnusedAssets only frees what nothing references any more,
            // and a texture whose last managed reference is uncollected garbage is still "used".
            // Doing it the other way round is the classic way to make this call look useless.
            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
            AsyncOperation op = Resources.UnloadUnusedAssets();
            if (op != null) op.completed += _ => Debug.LogWarning("[Memory] unload of unused assets finished");
            Debug.LogWarning("[Memory] relief pass started (GC + UnloadUnusedAssets)");
        }
    }
}
