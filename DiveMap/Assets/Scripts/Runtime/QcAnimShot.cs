using System.Collections;
using System.Threading.Tasks;
using DiveMap.Core;
using DiveMap.Runtime.Marine;
using GLTFast;
using UnityEngine;

namespace DiveMap.Runtime
{
    /// <summary>
    /// The pass that proves a marine GLB's animation actually PLAYS — in Unity, on the build, not
    /// on the web and not in a unit test.
    ///
    /// 🔴 Why it is a separate pass and not three more entries in <see cref="QcModelShot"/>.
    /// That harness photographs nine models and grades them against <c>DarkBaselines</c> — numbers
    /// a human looked at a real picture and signed off on. Adding rows to its list would move
    /// nothing about those nine, but it WOULD spend its per-model time budget, share its camera and
    /// its render texture, and put three ungraded models into a pass whose whole contract is "every
    /// model in here has a baseline". This pass takes no pictures at all: it loads, plays, measures
    /// the skeleton, prints one line and destroys everything. The nine baselines cannot move
    /// because nothing in this file can reach them.
    ///
    /// ── What it measures, and why it is two numbers ────────────────────────────────────────────
    ///
    ///   • <c>poseDelta</c> — the rig posed BY HAND at t=0 and t=½·length via
    ///     <c>Animation.Sample()</c>. Deterministic, frame-rate-free. Tests the FILE and the
    ///     import: is there a curve, and does it aim at a joint that exists?
    ///   • <c>liveDelta</c> / <c>advanced</c> — Unity's own animation update, left to run for
    ///     <see cref="LiveFrames"/> FRAMES. Tests the APP: is the component enabled, un-culled and
    ///     being ticked at the speed we set?
    ///
    /// A file that is fine inside a harness that is not running it gives poseDelta &gt; 0 and
    /// liveDelta = 0. Nothing else in CI can tell those two apart, and until this pass existed the
    /// honest answer to "does the app play clips" was "nobody has ever checked".
    ///
    /// 🔴 FRAMES, not seconds. The CI runner renders at roughly 3 fps under xvfb, so
    /// <c>WaitForSeconds(1f)</c> is three frames on the runner and sixty on a desk — this project
    /// has already lost three CI rounds to a wall-clock wait that was generous on a laptop and
    /// nothing at all on the runner. Counting frames makes the measurement mean the same thing on
    /// both, and the download is the only thing here still governed by a clock (because a stalled
    /// socket does not consume frames).
    /// </summary>
    public static class QcAnimShot
    {
        /// <summary>
        /// The rigged models, by absolute URL.
        ///
        /// 🔴 Absolute, and NOT through <see cref="AssetManifest"/>, on purpose. These are new
        /// files (<c>*_rig_xr0</c>) that no map item points at yet: the shipped manifest still
        /// resolves <c>msh:whaleshark</c> to the un-rigged master. Wiring them into the manifest
        /// would change what every existing map loads on the strength of a CI probe, which is a
        /// much larger decision than "prove the clip player works". When the manifest is
        /// re-pointed, this list becomes redundant and should be deleted rather than maintained.
        ///
        /// The three are chosen to cover the three shapes the clip picker has to handle, so a
        /// green run means something:
        ///   • whaleshark — six web-named clips AND <see cref="SpeciesFlag.SlowAnim"/>, so it is
        ///     also the only place the 0.32-0.85 rate band is exercised on a real file;
        ///   • bull shark — ONE clip called <c>AAction</c>, Blender's un-renamed default take,
        ///     which is the fallback rung nothing else reaches;
        ///   • oceanic manta — six clips under DIFFERENT names (<c>strong</c>, <c>banking</c>,
        ///     <c>hover</c>, <c>barrelroll</c>), which is the case where a picker that only knew
        ///     the turtle's vocabulary would silently land on clip 0 and look fine.
        /// </summary>
        public static readonly string[][] Rigs =
        {
            new[] { "msh:whaleshark",    "https://siamdive-cdn.b-cdn.net/models/xr/msh_whaleshark_rig_xr0.glb" },
            new[] { "mdl:bull_shark",    "https://siamdive-cdn.b-cdn.net/models/xr/mdl_bull_shark_rig_xr0.glb" },
            new[] { "msh:oceanic_manta", "https://siamdive-cdn.b-cdn.net/models/xr/msh_oceanic_manta_rig_xr0.glb" },
        };

        /// <summary>
        /// Staged far out to the side, and NOT at <see cref="QcModelShot"/>'s 4000 — the depth pass
        /// parks its model there, and two harnesses sharing a spot is how one of them ends up
        /// measuring the other's leftovers.
        /// </summary>
        private const float StageOffset = 6000f;

        /// <summary>
        /// Download budget per model, in WALL-CLOCK seconds — the one thing here that cannot be
        /// counted in frames, because a socket that has stalled does not consume any.
        /// </summary>
        private const float PerModelSeconds = 30f;

        /// <summary>
        /// Frames the live probe runs for. Six is ~2 s of clip at the runner's ~3 fps and ~0.1 s on
        /// a desk — either is many multiples of <see cref="ClipPlay.MinTimeAdvanced"/>, which is
        /// all this has to clear. It is a "did it move at all" gate, not a stopwatch.
        /// </summary>
        private const int LiveFrames = 6;

        /// <summary>Where in the cycle the deterministic probe poses the rig. Half a cycle is the
        /// furthest any looping clip can be from its own start.</summary>
        private const float PoseFraction = 0.5f;

        public static IEnumerator Run(Vector3 mapCentre)
        {
            Vector3 stage = mapCentre + Vector3.right * StageOffset;
            Debug.Log($"[Anim] pass start models={Rigs.Length} liveFrames={LiveFrames} " +
                      $"budget={PerModelSeconds:F0}s/model stage={stage}");

            int pass = 0;
            for (int i = 0; i < Rigs.Length; i++)
            {
                bool ok = false;
                yield return One(Rigs[i][0], Rigs[i][1],
                                 stage + Vector3.forward * (i * 200f), v => ok = v);
                if (ok) pass++;
            }

            // "N of M" and never a bare N: 0 of 3 with every line saying no-clips is the asset
            // pipeline, 0 of 3 with every line saying frozen-live is this app, and a bare count
            // says neither. That ambiguity is the reason the clip player went unnoticed for months.
            Debug.Log($"[Anim] pass done {pass} of {Rigs.Length} rigged models are moving");
        }

        private static IEnumerator One(string assetId, string url, Vector3 stage,
                                       System.Action<bool> onDone)
        {
            var pivot = new GameObject("QcAnim:" + assetId);
            pivot.transform.position = stage;

            float loadStart = Time.realtimeSinceStartup;
            // keepAnimation: this pass exists to MEASURE the rig, so it is the one caller that
            // must not have it stripped (see ClipPlay.MayAnimate — everything else does).
            Task<GltfImport> task = SceneBuilder.LoadForQc(url, assetId, pivot.transform, keepAnimation: true);
            while (!task.IsCompleted && Time.realtimeSinceStartup - loadStart < PerModelSeconds)
                yield return null;

            // RanToCompletion, not IsCompleted: a faulted task is "completed" too and reading its
            // Result rethrows, which would take the remaining models down with it.
            GltfImport import = task.Status == TaskStatus.RanToCompletion ? task.Result : null;
            if (import == null)
            {
                Debug.Log($"[Anim] qc asset={assetId} clips=0 names=- pick=- role=Cruise " +
                          $"length=0.00s timeScale=0.00 bones=0 poseDelta=0.0000 liveDelta=0.0000 " +
                          $"advanced=0.000s/0f mode=wave reason=load-failed verdict=no-clips " +
                          $"url={url} secs={(Time.realtimeSinceStartup - loadStart):F1}");
                Object.Destroy(pivot);
                onDone(false);
                yield break;
            }

            // The app's own binder, not a private copy of it. A QC pass with its own loader proves
            // things about the QC pass.
            var clips = new BodyClips();
            string reason = clips.Bind(pivot);

            var smr = pivot.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Transform[] bones = smr != null ? smr.bones : null;
            int boneCount = bones != null ? bones.Length : 0;

            clips.Play(ClipRole.Cruise);

            double timeScale = ClipPlay.TimeScale(
                1.0, ClipPlay.SlowAnimFor(assetId), ClipPlay.AnimMulFor(assetId), false);
            clips.SetSpeed((float)timeScale);

            // Probe 1 — deterministic. No frames, no clock.
            double poseDelta = clips.PoseDelta(bones, PoseFraction);

            // Probe 2 — live. Let Unity drive it for a fixed number of FRAMES.
            float t0 = clips.CurrentTime;
            Vector3[] before = BodyClips.Capture(bones);
            for (int f = 0; f < LiveFrames; f++) yield return null;
            double liveDelta = BodyClips.MaxMove(bones, before);
            double advanced = Mathf.Abs(clips.CurrentTime - t0);

            // 🔴 A looping clip can land back where it started: at speed 0.9 with six ~0.3 s frames
            // the whaleshark's 2 s cycle can wrap, and a wrapped clip reads as advanced ≈ 0 with a
            // liveDelta that is real. Take the wrap into account rather than calling a working rig
            // frozen — the cycle length is known, so this is arithmetic, not a guess.
            if (advanced < ClipPlay.MinTimeAdvanced && liveDelta > ClipPlay.MinPoseDelta)
                advanced = clips.CurrentLength;

            var probe = new ClipPlay.ClipProbe(clips.Count, boneCount, poseDelta, liveDelta,
                                               advanced, LiveFrames);

            Debug.Log(ClipPlay.ProbeLine(assetId, clips.NamesArray, clips.CurrentRole,
                                         clips.CurrentName, clips.CurrentLength, timeScale,
                                         reason, probe)
                      + $" url={url} secs={(Time.realtimeSinceStartup - loadStart):F1}");

            bool pass = ClipPlay.Passes(probe);
            if (!pass)
                Debug.LogWarning($"[Anim] FAIL asset={assetId} verdict={ClipPlay.Verdict(probe)} " +
                                 $"— see the [Anim] line above for which of the four causes it is");

            Object.Destroy(pivot);
            yield return null;               // let the destroy land before the import goes
            import.Dispose();
            Resources.UnloadUnusedAssets();
            onDone(pass);
        }
    }
}
