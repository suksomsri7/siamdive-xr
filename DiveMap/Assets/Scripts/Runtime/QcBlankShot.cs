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
        /// <summary>Htms Chang — the reef the report was filed against (QcMapShot.MapIds).</summary>
        private const string MapA = "wl6zwxh1tdgn";

        /// <summary>
        /// A different map to switch INTO — Posidon, one of the two the user was moving between.
        /// The bug is about what map B inherits from map A, so the two must not be the same id:
        /// <c>LoadMap</c> would still reload, but <c>SwitchMapFromHost</c> short-circuits on
        /// equality and the sequence would stop resembling what the user did.
        /// </summary>
        private const string MapB = "w63m4h7u4vi5";

        /// <summary>
        /// 🔴 The harness must NOT run on the AppBoot GameObject. <c>LoadMap</c> ends in
        /// <c>AppBoot.Retry</c>, whose first act is <c>StopAllCoroutines</c> — a harness living
        /// there would kill itself the first time it asked for a map, and the run would end with
        /// no verdict, no PNGs and a green tick. Its own object, kept across everything.
        /// </summary>
        private sealed class Runner : MonoBehaviour { }

        /// <summary>Entry point from <c>AppBoot</c>: starts the control on its own object.</summary>
        public static void Begin(string dir, AppBoot boot)
        {
            var go = new GameObject("QcBlankRunner");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<Runner>().StartCoroutine(Run(dir, boot));
        }

        private const int Width = 1280;
        private const int Height = 720;

        /// <summary>How long one map load may take on CI's software GL before we give up on it.</summary>
        private const float LoadTimeoutSeconds = 240f;

        private static IEnumerator Run(string dir, AppBoot boot)
        {
            if (boot == null) { Debug.LogError("[QcBlank] no AppBoot"); Application.Quit(1); yield break; }
            Directory.CreateDirectory(dir);

            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { name = "QcBlankRT" };
            var readback = new Texture2D(Width, Height, TextureFormat.RGB24, false);

            QcBlank.Frame before = default, after = default;

            // ── pass 1: the bug, with the fix held back ──────────────────────────────
            yield return OnePass(dir, boot, rt, readback, suppressFix: true, f => before = f);

            // ── pass 2: the same sequence, shipped behaviour ─────────────────────────
            yield return OnePass(dir, boot, rt, readback, suppressFix: false, f => after = f);

            string verdict = QcBlank.Verdict(before, after);
            bool ok = QcBlank.Passed(before, after);

            File.WriteAllText(Path.Combine(dir, "verdict.txt"), verdict + "\n");
            if (ok) Debug.Log("[QcBlank] " + verdict);
            else Debug.LogError("[QcBlank] " + verdict);

            RenderTexture.active = null;
            rt.Release();

            Application.Quit(ok ? 0 : 1);
        }

        /// <summary>
        /// map A → tour → light off → (nothing: this IS the swipe-back) → map B → measure.
        /// </summary>
        private static IEnumerator OnePass(string dir, AppBoot boot, RenderTexture rt,
                                           Texture2D readback, bool suppressFix,
                                           System.Action<QcBlank.Frame> onFrame)
        {
            string tag = suppressFix ? "before" : "after";
            Debug.Log($"[QcBlank] ── pass '{tag}' (reset {(suppressFix ? "SUPPRESSED" : "on")}) ──");

            // Start each pass from a clean map A. The reset always runs for THIS load — the pass is
            // about what map B inherits, and beginning pass 2 inside pass 1's wreckage would test
            // recovery rather than prevention.
            SceneAtmosphere.SuppressResetForQc = false;
            yield return LoadAndSettle(boot, MapA);

            // Into the tour, and turn the light off: that is the state whose atmosphere is
            // near-black by design (DiveLightMath.For(false) → fog 70-200, ambient ×0.32).
            if (!TourController.Start())
            {
                Debug.LogError("[QcBlank] could not enter the tour — the control cannot run");
                onFrame(default);
                yield break;
            }
            for (int i = 0; i < 10; i++) yield return null;   // let Begin() build the drone

            DroneLights lights = Object.FindFirstObjectByType<DroneLights>();
            if (lights == null)
            {
                Debug.LogError("[QcBlank] no DroneLights after entering the tour");
                onFrame(default);
                yield break;
            }
            lights.Set(false);
            for (int i = 0; i < 5; i++) yield return null;
            Debug.Log($"[QcBlank] tour atmosphere armed — {SceneAtmosphere.StateLine()} " +
                      $"mode={ModeManager.Current}");

            // 🔴 The swipe-back. Deliberately NOTHING here: no ExitToHost, no ModeManager.Exit, no
            // native message. iOS just stops giving Unity frames, and the next thing that happens
            // is the user picking another map.
            SceneAtmosphere.SuppressResetForQc = suppressFix;
            yield return LoadAndSettle(boot, MapB);

            // Give the newly built map a few frames to draw before judging it.
            for (int i = 0; i < 12; i++) yield return null;

            byte[] rgb = null;
            yield return Capture(rt, readback, Path.Combine(dir, tag + ".png"), b => rgb = b);
            QcBlank.Frame f = QcBlank.Measure(rgb);
            Debug.Log($"[QcBlank] {tag}: mean={f.MeanLuminance:F1} sd={f.StdDev:F1} px={f.Pixels} " +
                      $"· {SceneAtmosphere.StateLine()} mode={ModeManager.Current}");
            onFrame(f);

            SceneAtmosphere.SuppressResetForQc = false;
        }

        /// <summary>Ask for a map and wait until the build is finished (or the budget is gone).</summary>
        private static IEnumerator LoadAndSettle(AppBoot boot, string shortId)
        {
            boot.LoadMap(shortId);
            // One frame for Retry to start the coroutine before IsBuilding can mean anything.
            yield return null;

            float deadline = Time.realtimeSinceStartup + LoadTimeoutSeconds;
            while (boot.IsBuilding)
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Debug.LogWarning("[QcBlank] map " + shortId + " did not finish inside the budget");
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
