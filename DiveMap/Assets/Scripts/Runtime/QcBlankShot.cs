using System.Collections;
using System.IO;
using DiveMap.Core;
using UnityEngine;

namespace DiveMap.Runtime
{
    /// <summary>
    /// The positive control for the flat-navy map switch (WO-MERGE P1e).
    ///
    /// 🔴 Project rule: no fix without proof the harness can SEE the bug. A run that only checks
    /// the fixed path proves nothing — a check that would have passed before the fix is not a
    /// check. So this harness does the whole thing twice in ONE CI round and reports both numbers:
    ///
    ///   pass 1 "before"  the atmosphere reset is SUPPRESSED — must come out blank
    ///   pass 2 "after"   the reset runs as shipped                — must come out lit
    ///
    /// and it fails the run if EITHER expectation is missed. A non-blank "before" is reported as
    /// CONTROL-BROKEN rather than as a pass: it means the reproduction stopped reproducing and the
    /// instrument has gone blind, which is worse than a red light.
    ///
    /// What is being reproduced is the SWIPE-BACK exit specifically, because that is the one that
    /// cannot be handled any other way: iOS pauses the engine mid-tour and not one line of Unity
    /// code runs. The harness emulates it exactly — it enters the tour, turns the drone's light
    /// off (fog 70-200, ambient ×0.32), and then simply loads another map WITHOUT calling any exit
    /// path at all. No message, no mode change, nothing. That is the real thing, not a model of it.
    ///
    ///     -qcblank &lt;dir&gt;      writes before.png / after.png / verdict.txt, then quits
    ///                            exit 0 = PASS, exit 1 = FAIL or CONTROL-BROKEN
    /// </summary>
    public static class QcBlankShot
    {
        // ── which maps, and why these two (WO-MERGE P1f) ────────────────────────────────────
        //
        // 🔴 The first version used Htms Chang → Posidon because those are the maps the user was
        // moving between. That is the wrong instinct here. What is being proven is a property of
        // GLOBAL RENDER STATE — does a new build inherit the previous mode's fog and ambient —
        // and that property has nothing whatever to do with how many models a map contains. The
        // only thing map size bought was four heavy loads on llvmpipe (135-300 ms/frame in this
        // project's own measurements), which blew the QC job's 150-minute budget, produced NO
        // verdict, and took the unrelated palette screenshots in the same job down with it.
        //
        // Weight measured against the live API rather than guessed (items / unique assets /
        // schools / msh heroes):
        //     Atlantis    103 / 27 / 17 / 25      T-13        494 / 10 /  0 / 3
        //     Posidon      90 / 14 /  2 / 14      Harddeep     14 /  9 /  5 / 4
        //     Htms Chang   14 /  6 / 10 /  1  ← 14 items but TEN fish species to fetch and
        //                                        template: the most expensive map per item here
        //     Hanuman      13 /  9 /  0 /  1
        //     Tu-1          1 /  1 /  0 /  0
        //
        // So: the lightest map that can host a tour, switching into the lightest map that still
        // has something to look at.

        /// <summary>
        /// Dive Site Tu-1 — one item, one asset, no schools. Its only job is to exist while the
        /// drone's lights-off atmosphere is applied to the scene-wide RenderSettings.
        /// </summary>
        private const string MapA = "oy3hlklgnkmy";

        /// <summary>
        /// Hanuman — 13 items from 9 assets and NO fish schools (the expensive part of a load).
        /// This is the map that gets measured, so it is the one that must be unmistakably
        /// non-uniform when the atmosphere is healthy: it has real scenery and one hero animal,
        /// on top of the seabed and the backdrop gradient every map draws.
        ///
        /// Must differ from <see cref="MapA"/> — the bug is about what B inherits from A.
        /// </summary>
        private const string MapB = "yh7hbkdmzur8";

        /// <summary>
        /// 🔴 The harness must NOT run on the AppBoot GameObject. <c>LoadMap</c> ends in
        /// <c>AppBoot.Retry</c>, whose first act is <c>StopAllCoroutines</c> — a harness living
        /// there would kill itself the first time it asked for a map, and the run would end with
        /// no verdict, no PNGs and a green tick. Its own object, kept across everything.
        /// </summary>
        private sealed class Runner : MonoBehaviour { }

        /// <summary>
        /// The map the player should boot straight into when <c>-qcblank</c> is present.
        ///
        /// Without this the harness pays for a full load of the DEFAULT map (Htms Chang, ten fish
        /// species) before it even starts — the single most expensive map in the catalogue, and
        /// exactly the load P1f moved heaven and earth to avoid.
        /// </summary>
        public const string FirstMap = MapA;

        /// <summary>
        /// 🔴 ONE runner per process, and this guard is the whole lesson of CI run 31451758156.
        ///
        /// <see cref="Begin"/> is called from the tail of <c>AppBoot.Boot</c> — and Boot runs on
        /// EVERY map load, not just the first. The harness loads four maps. So the first runner
        /// spawned a second, which spawned a third, and the run ended with THIRTEEN of them alive
        /// at once: each calling LoadMap, each LoadMap calling Retry, each Retry calling
        /// StopAllCoroutines and aborting the others' builds. Nothing ever finished loading
        /// ("map oy3hlklgnkmy did not finish inside its 60s allowance", seven times), one runner's
        /// atmosphere reset kept exiting the tour another had just entered, and the verdict blamed
        /// the shipped code — "pass 'after' never reached the tour" — for damage the instrument was
        /// doing to itself.
        ///
        /// A static bool rather than a search for an existing Runner: the recursion happens inside
        /// a single frame, before any GameObject the previous call created has been through Awake.
        /// </summary>
        private static bool _started;

        /// <summary>Entry point from <c>AppBoot</c>: starts the control on its own object, once.</summary>
        public static void Begin(string dir, AppBoot boot)
        {
            if (_started) return;
            _started = true;

            var go = new GameObject("QcBlankRunner");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<Runner>().StartCoroutine(Run(dir, boot));
        }

        /// <summary>
        /// Capture size. 640×360 because the measurement is a MEAN and a STANDARD DEVIATION over
        /// the whole frame — 230,400 pixels is a preposterously large sample for two scalars, and
        /// on software GL every pixel is CPU work. Four times cheaper than 720p per capture, and
        /// the numbers it produces are the same to well inside the thresholds.
        /// </summary>
        private const int Width = 640;
        private const int Height = 360;

        // ── budgets: the harness must fail, never hang (WO-MERGE P1f) ───────────────────────
        //
        // 🔴 These are not safety nets bolted on around the harness, they are part of it. A
        // control that hangs is a blind instrument, and it does not fail alone — it takes its
        // whole CI job with it. Every one of them is wall clock, checked between yields, and every
        // one of them ends in a written verdict rather than a stall.
        //
        // Sized so the worst case is bounded and legible rather than generous: 2 passes × 2 loads,
        // both maps deliberately tiny. The shell `timeout` in the workflow is set ABOVE
        // WholeRunSeconds on purpose, so the harness's own budget expires first and there is a
        // verdict.txt to read; the shell timeout is the backstop for a hang below this code.

        /// <summary>
        /// One map load. QcMapShot allows 150 s, but that is sized for 90-item Posidon; these two
        /// maps carry 1 and 13 items, and two of these still fit inside a pass with room over.
        /// </summary>
        private const float LoadTimeoutSeconds = 60f;

        /// <summary>One whole pass: two loads (2×60 s), a tour, and a capture.</summary>
        private const float PassBudgetSeconds = 140f;

        /// <summary>
        /// Both passes plus the verdict — and 60 s under the workflow's 360 s shell timeout, on
        /// purpose, so THIS budget is the one that expires and a verdict always gets written.
        /// 2 × 140 leaves 20 s of slack; the run deadline caps a pass that tries to overrun it.
        /// </summary>
        private const float WholeRunSeconds = 300f;

        private static IEnumerator Run(string dir, AppBoot boot)
        {
            if (boot == null) { Debug.LogError("[QcBlank] no AppBoot"); Application.Quit(1); yield break; }
            Directory.CreateDirectory(dir);

            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { name = "QcBlankRT" };
            var readback = new Texture2D(Width, Height, TextureFormat.RGB24, false);

            // 🔴 Written NOW, before anything can go slowly. If this process is killed from the
            // outside — the shell timeout, the job's own budget — the file it leaves behind still
            // says what happened instead of not existing, which is what the last run produced.
            // Every exit below overwrites it with the real answer.
            string verdictPath = Path.Combine(dir, "verdict.txt");
            Write(verdictPath, QcBlank.ControlBroken + " the harness was killed before it finished " +
                               "— no pass completed");

            QcBlank.Frame before = default, after = default;
            float runDeadline = Time.realtimeSinceStartup + WholeRunSeconds;
            string budgetBroke = null;

            // ── pass 1: the bug, with the fix held back ──────────────────────────────
            yield return OnePass(dir, boot, rt, readback, suppressFix: true, runDeadline,
                                 f => before = f, s => budgetBroke = s);

            // ── pass 2: the same sequence, shipped behaviour ─────────────────────────
            if (budgetBroke == null)
                yield return OnePass(dir, boot, rt, readback, suppressFix: false, runDeadline,
                                     f => after = f, s => budgetBroke = s);

            string verdict = budgetBroke ?? QcBlank.Verdict(before, after);
            bool ok = budgetBroke == null && QcBlank.Passed(before, after);

            Write(verdictPath, verdict);
            if (ok) Debug.Log("[QcBlank] " + verdict);
            else Debug.LogError("[QcBlank] " + verdict);

            RenderTexture.active = null;
            rt.Release();

            Application.Quit(ok ? 0 : 1);
        }

        /// <summary>Never let a failure to write the verdict be the thing that hides the verdict.</summary>
        private static void Write(string path, string text)
        {
            try { File.WriteAllText(path, text + "\n"); }
            catch (System.Exception e) { Debug.LogError("[QcBlank] verdict not written: " + e.Message); }
        }

        /// <summary>
        /// map A → tour → light off → (nothing: this IS the swipe-back) → map B → measure.
        /// </summary>
        private static IEnumerator OnePass(string dir, AppBoot boot, RenderTexture rt,
                                           Texture2D readback, bool suppressFix, float runDeadline,
                                           System.Action<QcBlank.Frame> onFrame,
                                           System.Action<string> onBudgetBroken)
        {
            string tag = suppressFix ? "before" : "after";
            float started = Time.realtimeSinceStartup;
            // Whichever runs out first: this pass's share, or what is left of the whole run.
            float deadline = Mathf.Min(started + PassBudgetSeconds, runDeadline);
            Debug.Log($"[QcBlank] ── pass '{tag}' (reset {(suppressFix ? "SUPPRESSED" : "on")}) " +
                      $"budget {deadline - started:F0}s ──");

            // Start each pass from a clean map A. The reset always runs for THIS load — the pass is
            // about what map B inherits, and beginning pass 2 inside pass 1's wreckage would test
            // recovery rather than prevention.
            SceneAtmosphere.SuppressResetForQc = false;
            yield return LoadAndSettle(boot, MapA, deadline);
            if (Overdue(tag, deadline, started, onBudgetBroken)) yield break;

            // Into the tour, and turn the light off: that is the state whose atmosphere is
            // near-black by design (DiveLightMath.For(false) → fog 70-200, ambient ×0.32).
            //
            // A false from Start() is not automatically a failure: ArenaEntry auto-plays world
            // maps, so the drone may already be out — and Request(Tour→Tour) answers false. Ask
            // the mode, not the return value.
            TourController.Start();
            // Bounded wait, not a fixed frame count: Request is synchronous so the mode is set at
            // once, but ArenaEntry's auto-play arrives 0.6 s later and on llvmpipe that can be a
            // single frame or five. Waiting for the ANSWER rather than for a number of frames also
            // means this reports honestly when the answer never comes.
            float tourBy = Mathf.Min(Time.realtimeSinceStartup + 5f, deadline);
            while (!ModeRules.IsFirstPerson(ModeManager.Current) &&
                   Time.realtimeSinceStartup < tourBy)
                yield return null;

            if (!ModeRules.IsFirstPerson(ModeManager.Current))
            {
                Debug.LogError("[QcBlank] could not enter the tour — the control cannot run");
                onBudgetBroken(QcBlank.ControlBroken + $" pass '{tag}' never reached the tour, " +
                                                       "so the dark atmosphere was never applied");
                yield break;
            }

            DroneLights lights = Object.FindFirstObjectByType<DroneLights>();
            if (lights == null)
            {
                Debug.LogError("[QcBlank] no DroneLights after entering the tour");
                onBudgetBroken(QcBlank.ControlBroken + $" pass '{tag}' found no DroneLights");
                yield break;
            }
            lights.Set(false);
            for (int i = 0; i < 3; i++) yield return null;
            Debug.Log($"[QcBlank] tour atmosphere armed — {SceneAtmosphere.StateLine()} " +
                      $"mode={ModeManager.Current}");

            // 🔴 The swipe-back. Deliberately NOTHING here: no ExitToHost, no ModeManager.Exit, no
            // native message. iOS just stops giving Unity frames, and the next thing that happens
            // is the user picking another map.
            SceneAtmosphere.SuppressResetForQc = suppressFix;
            yield return LoadAndSettle(boot, MapB, deadline);
            if (Overdue(tag, deadline, started, onBudgetBroken)) yield break;

            // Give the newly built map a few frames to draw before judging it. Six, not twelve:
            // on llvmpipe every one of these is up to 300 ms, and nothing in the scene animates
            // into or out of existence — the build is already finished.
            for (int i = 0; i < 6; i++) yield return null;

            byte[] rgb = null;
            yield return Capture(rt, readback, Path.Combine(dir, tag + ".png"), b => rgb = b);
            QcBlank.Frame f = QcBlank.Measure(rgb);
            Debug.Log($"[QcBlank] {tag}: mean={f.MeanLuminance:F1} sd={f.StdDev:F1} px={f.Pixels} " +
                      $"· {SceneAtmosphere.StateLine()} mode={ModeManager.Current}");
            onFrame(f);

            SceneAtmosphere.SuppressResetForQc = false;
        }

        /// <summary>
        /// Did this pass run out of time? Reports it as a verdict rather than letting the harness
        /// carry on measuring a scene that never finished assembling.
        /// </summary>
        private static bool Overdue(string tag, float deadline, float started,
                                    System.Action<string> onBudgetBroken)
        {
            if (Time.realtimeSinceStartup <= deadline) return false;
            string v = QcBlank.BudgetVerdict(tag, Time.realtimeSinceStartup - started);
            Debug.LogError("[QcBlank] " + v);
            onBudgetBroken(v);
            return true;
        }

        /// <summary>
        /// Ask for a map and wait until the build is finished — bounded by BOTH this map's own
        /// allowance and the pass deadline, whichever comes first. Returns either way; the caller
        /// checks the clock, because "the map did not finish" and "the pass is over" are different
        /// facts and only the second one ends the run.
        /// </summary>
        private static IEnumerator LoadAndSettle(AppBoot boot, string shortId, float passDeadline)
        {
            boot.LoadMap(shortId);
            // One frame for Retry to start the coroutine before IsBuilding can mean anything.
            yield return null;

            float deadline = Mathf.Min(Time.realtimeSinceStartup + LoadTimeoutSeconds, passDeadline);
            while (boot.IsBuilding)
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Debug.LogWarning($"[QcBlank] map {shortId} did not finish inside its " +
                                     $"{LoadTimeoutSeconds:F0}s allowance — measuring what there is");
                    yield break;
                }
                yield return null;
            }
        }

        /// <summary>
        /// Render the main camera into an offscreen target and read it back as RGB24.
        ///
        /// Its own target rather than a grab of the back buffer: on CI the player runs under xvfb
        /// on software GL and a back-buffer read there has produced empty frames before. The camera
        /// is restored afterwards so the harness cannot be the reason a later shot is wrong.
        ///
        /// 🔴 And it is what makes the measurement mean anything. Every canvas in this app is
        /// ScreenSpaceOverlay (UiShell:149, LoadOverlay:121) and the badges are OnGUI, so NONE of
        /// them render into a camera's target texture — this reads the 3D world alone. That is
        /// precisely the distinction the device screenshots could not make: a live HUD over a dead
        /// world. Point this at the back buffer instead and the tour HUD's coins and minimap would
        /// give every frame enough spread to pass.
        /// </summary>
        private static IEnumerator Capture(RenderTexture rt, Texture2D readback, string pngPath,
                                           System.Action<byte[]> onBytes)
        {
            Camera cam = Camera.main;
            if (cam == null) { onBytes(null); yield break; }

            yield return new WaitForEndOfFrame();

            RenderTexture prevTarget = cam.targetTexture;
            RenderTexture prevActive = RenderTexture.active;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                readback.ReadPixels(new Rect(0f, 0f, rt.width, rt.height), 0, 0, false);
                readback.Apply(false);
            }
            finally
            {
                cam.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
            }

            if (!string.IsNullOrEmpty(pngPath))
            {
                try { File.WriteAllBytes(pngPath, readback.EncodeToPNG()); }
                catch (System.Exception e) { Debug.LogWarning("[QcBlank] png write failed: " + e.Message); }
            }

            onBytes(readback.GetRawTextureData());
        }
    }
}
