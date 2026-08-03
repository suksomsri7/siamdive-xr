using System.Collections;
using System.Collections.Generic;
using System.IO;
using DiveMap.Core;
using UnityEngine;

namespace DiveMap.Runtime
{
    /// <summary>
    /// The QC pass that photographs the REAL MAPS, from where the player's eyes actually are.
    ///
    /// 🔴 WHY IT HAD TO BE WRITTEN — the user's sentence, which is the whole brief:
    /// "texture ของฉากและวัตถุไม่ตรงตามโมเดลจริงๆ เลย ผมถามคุณ QC ยังไงครับ คุณไม่ได้ถ่ายรูป
    /// แคปเจอร์หน้าจอมาดูเองด้วยหรอ".
    ///
    /// They are right, and the hole is structural rather than careless. CI has two kinds of
    /// evidence and neither of them is the game:
    ///
    ///   • <see cref="QcModelShot"/> photographs NINE MODELS, one at a time, alone, against a
    ///     gradient, 4,000 units to the side of everything. A model that is beautiful in that
    ///     studio and black in a map full of other models scores a clean pass.
    ///   • <c>AppBoot.QcShot</c>'s two wide angles photograph ONE map — HTMS Chang — from an orbit
    ///     pose. Atlantis, Posidon, Hanuman, Harddeep, T-13 and Tu-1 have never been in a CI frame
    ///     at all, and the diver's-eye pose the player spends the entire game in has never been
    ///     photographed on any map.
    ///
    /// So the numbers kept getting better while the game kept looking worse, and both were honest.
    /// The user's own measurement off their Atlantis phone screenshot — 33.1% of the frame darker
    /// than luminance 40 — is the baseline this pass exists to beat, and it is a number no QC run
    /// in this project has ever been in a position to produce.
    ///
    /// 🔎 THREE FRAMES PER POSE. Same camera, same instant, one input removed at a time:
    /// the shot, the shot with the map hidden (water and backdrop only), and the shot with
    /// <c>RenderSettings.fog</c> off. The differences ARE the map and the fog respectively, which
    /// is what turns "is there any haze in this picture" into a byte count instead of an argument.
    /// See <see cref="QcPixels.MeasureMap"/>.
    ///
    /// 🔎 NOTHING IS STAGED. The map is built by the app's own <see cref="SceneBuilder"/>, from the
    /// production API, through the same cache and the same loader, and lit by whatever
    /// <c>AppBoot.SetupLighting</c>, <see cref="DepthAtmosphere"/> and <see cref="UnderwaterShading"/>
    /// left in RenderSettings. A QC rig with its own camera height or its own light would be
    /// photographing a different app.
    /// </summary>
    public static class QcMapShot
    {
        /// <summary>
        /// The maps, worst-report first. Taken from
        /// <c>GET /api/dive-sites/public</c> on 2026-08-03; the ids are stable (they are the
        /// short id in the map's own URL) and the names are here so a log line is readable.
        ///
        /// 🔴 The order is the budget. <see cref="BudgetSeconds"/> stops the pass rather than the
        /// build step, so a slow CDN costs the maps at the END of this list — which is why the two
        /// the user photographed are at the front of it.
        /// </summary>
        public static readonly string[] MapIds =
        {
            "miwv1abp1o71",   // Atlantis   — "ยังดำปกติ texture พื้นผิววัตถุเป็นสีดำ"
            "w63m4h7u4vi5",   // Posidon    — "หมอกไม่เห็นเลย ฉากไม่มีมิติ" (61.8 m)
            "yh7hbkdmzur8",   // Hanuman
            "wl6zwxh1tdgn",   // Htms Chang — the only map CI has ever photographed
            "874ti6ignvp9",   // Harddeep
            "21v3chaote45",   // T-13 (ต.13)
            "oy3hlklgnkmy",   // Dive Site Tu -1
        };

        /// <summary>
        /// Per map: fetch + build every GLB in it + three frames. A whole map is a much bigger job
        /// than one model — Posidon alone is 90 items — so this is generous, and a map that has
        /// not landed by then is a FAIL worth reporting rather than a reason to lose the rest.
        /// </summary>
        private const float PerMapSeconds = 150f;

        /// <summary>
        /// Whole-pass ceiling. Sized so that a slow map still leaves room for the ones behind it:
        /// at the 150 s per-map ceiling, 600 s lets three maps time out and the rest still be
        /// photographed and logged. Everything past it is logged as <c>budget-exhausted</c>, never
        /// silently skipped — a missing line reads as "the harness never ran", which has cost this
        /// project whole CI rounds before.
        /// </summary>
        private const float BudgetSeconds = 600f;

        /// <summary>Frames to let the boids, the atmosphere and the backdrop re-bake settle.</summary>
        private const float SettleSeconds = 1.2f;

        /// <summary>
        /// How high above the seabed the diver's eyes sit, in world units. The drone flies at
        /// roughly two metres off the sand (<c>TourController</c>), and six units is a metre
        /// (<see cref="DepthLight.UnitsPerMetre"/>), so twelve is that pose — not a number picked
        /// to make a picture look good.
        /// </summary>
        private const float EyeHeightUnits = 12f;

        /// <summary>
        /// How far back from the content's centre the diver stands, as a multiple of the content's
        /// half-width. 1.15 puts the camera just outside the content box looking in, which is the
        /// framing in both of the screenshots the user sent: the near objects fill the lower half
        /// of the frame and the far rim is the background.
        /// </summary>
        private const float StandOffOfRadius = 1.15f;

        /// <summary>
        /// Camera pitch, degrees below level. A diver flying a drone looks very slightly down —
        /// enough that the seabed fills the bottom of the frame and the water fills the top, which
        /// is exactly what makes the background gradient measurable at all.
        /// </summary>
        private const float PitchDegrees = 6f;

        /// <summary>
        /// The second pose, for a map whose own framing box has no place to stand in it.
        ///
        /// Hanuman's first pose came back <c>waterLum=0.000 subject=100.00%</c> — the camera was
        /// inside the content, because "half the framing box, plus fifteen per cent" is a distance
        /// that still lands inside a map whose content is spread rather than clustered. Standing
        /// at 2.4 radii and climbing to a quarter of the map's own half-width guarantees the
        /// content is IN FRONT of the camera rather than around it, and the steeper pitch keeps
        /// the seabed in frame from up there. It is still a diver's view, just a diver who has
        /// swum up to look at the site.
        /// </summary>
        private const float RetryStandOffOfRadius = 2.4f;

        /// <inheritdoc cref="RetryStandOffOfRadius"/>
        private const float RetryClimbOfRadius = 0.25f;

        /// <inheritdoc cref="RetryStandOffOfRadius"/>
        private const float RetryPitchDegrees = 14f;

        /// <summary>
        /// Photograph every map in <see cref="MapIds"/> into
        /// <paramref name="dir"/>/qc_map_&lt;shortId&gt;.png and log one <c>[QCMap]</c> line each.
        ///
        /// 🔴 It DESTROYS the map that is on screen and builds each one in its place. That is
        /// deliberate: a map built off to the side would not be standing on its own seabed, would
        /// not be at its own water level, and would therefore be photographed in light no player
        /// ever sees. The caller is <c>AppBoot.QcShot</c>, which quits the moment this returns, so
        /// there is nothing left for the last map to break.
        /// </summary>
        /// <param name="dir">Where the PNGs go — the folder the other QC shots are written to.</param>
        /// <param name="builder">The app's own scene builder.</param>
        /// <param name="manifest">The registry the app resolved its own map against.</param>
        /// <param name="currentRoot">The map currently in the scene; destroyed before the first build.</param>
        public static IEnumerator Run(string dir, SceneBuilder builder, AssetManifest manifest,
                                      GameObject currentRoot)
        {
            float t0 = Time.realtimeSinceStartup;
            if (string.IsNullOrEmpty(dir)) dir = ".";

            Camera cam = Camera.main;
            if (cam == null || builder == null)
            {
                Debug.LogWarning($"[QCMap] cannot run — camera={(cam != null)} builder={(builder != null)}");
                foreach (string id in MapIds)
                    Debug.Log(QcPixels.MapLine(id, "diver", 0f, new QcPixels.MapShot()) + " harness-missing");
                yield break;
            }

            // Nothing else may drive this camera. Same two owners QcModelShot has to switch off,
            // for the same reason: an orbit rig or a drone re-aims Camera.main every frame and the
            // pose below would be gone by the time the shutter opened.
            var orbit = cam.GetComponent<OrbitCamera>();
            if (orbit != null) orbit.enabled = false;
            if (ModeManager.Current != AppMode.View && ModeManager.Instance != null)
            {
                ModeManager.Instance.Exit();
                yield return null;
            }

            if (currentRoot != null)
            {
                Object.Destroy(currentRoot);
                yield return null;
            }

            int w = Mathf.Clamp(Screen.width, 320, 1920);
            int h = Mathf.Clamp(Screen.height, 240, 1080);
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32)
            {
                name = "QcMapRT",
                antiAliasing = 1,
            };
            var readback = new Texture2D(w, h, TextureFormat.RGB24, false, false);

            Debug.Log($"[QCMap] pass start maps={MapIds.Length} screen={w}x{h} " +
                      $"manifest={(manifest != null ? manifest.Count.ToString() : "MISSING")} " +
                      $"fogWorkGate={QcPixels.MinFogWork:0.00} spanGate={QcPixels.MinBackdropSpan:0.000}");

            int ok = 0, fail = 0;
            foreach (string shortId in MapIds)
            {
                if (Time.realtimeSinceStartup - t0 > BudgetSeconds)
                {
                    Debug.Log(QcPixels.MapLine(shortId, "diver", 0f, new QcPixels.MapShot()) +
                              " budget-exhausted");
                    fail++;
                    continue;
                }

                bool passed = false;
                yield return One(cam, rt, readback, dir, builder, manifest, shortId, r => passed = r);
                if (passed) ok++; else fail++;

                Resources.UnloadUnusedAssets();
                yield return null;
            }

            RenderTexture.active = null;
            cam.targetTexture = null;
            rt.Release();
            Object.Destroy(rt);
            Object.Destroy(readback);

            Debug.Log($"[QCMap] pass done ok={ok} fail={fail} " +
                      $"in {Time.realtimeSinceStartup - t0:F1}s (budget {BudgetSeconds:F0}s)");
        }

        // ── one map ──────────────────────────────────────────────────────────────

        private static IEnumerator One(Camera cam, RenderTexture rt, Texture2D readback, string dir,
                                       SceneBuilder builder, AssetManifest manifest, string shortId,
                                       System.Action<bool> onDone)
        {
            float t0 = Time.realtimeSinceStartup;

            SceneData scene = null;
            string err = null;
            yield return MapApiClient.Fetch(shortId, s => scene = s, e => err = e);
            if (scene == null)
            {
                Debug.Log(QcPixels.MapLine(shortId, "diver", 0f, new QcPixels.MapShot()) +
                          " fetch-failed err=" + (err ?? "(null)"));
                onDone(false);
                yield break;
            }

            SceneBuilder.BuildResult result = default;
            bool built = false;
            Coroutine job = builder.StartCoroutine(
                builder.BuildRoutine(scene, manifest, r => { result = r; built = true; }));
            while (!built && Time.realtimeSinceStartup - t0 < PerMapSeconds) yield return null;
            if (!built)
            {
                builder.StopCoroutine(job);
                builder.DiscardInFlight();
                Debug.Log(QcPixels.MapLine(shortId, "diver", 0f, new QcPixels.MapShot()) +
                          $" build-timeout after {Time.realtimeSinceStartup - t0:F0}s");
                onDone(false);
                yield break;
            }

            // Everything the app does to a freshly built map, in the order AppBoot does it. Miss
            // one of these and the frame is lit by the PREVIOUS map's water level, which is the
            // sort of thing that makes a QC picture worse than no picture.
            EnvMode.Reset();
            DepthAtmosphere.Configure(result.WaterLevel, result.Center, result.Radius);
            UnderwaterShading.Configure(result.WaterLevel);

            var renderers = new List<Renderer>();
            if (result.Root != null) result.Root.GetComponentsInChildren(true, renderers);

            // ── the pose ─────────────────────────────────────────────────────────
            // Stand back from the content, at eye height above its floor, looking very slightly
            // down at its middle. Derived from the map's own framing box so it works on a 40 u
            // wreck and on a 340 u city without a per-map number anywhere.
            float radius = Mathf.Max(20f, 0.5f * Mathf.Max(result.FrameSizeX, result.FrameSizeZ));

            QcPixels.MapShot shot = default;
            string name = string.IsNullOrEmpty(result.MapName) ? shortId : result.MapName;
            float depthMetres = 0f;

            // 🔴 TWO ATTEMPTS, and the second one is not a retry for luck.
            //
            // Hanuman, run 30800189252: waterLum=0.000 subject=100.00%. Every pixel of the frame
            // differed from the map-hidden twin, which means the camera was inside the map's
            // geometry — a 340 u framing box whose content happens to sit where the diver stands.
            // The pass reported "no-fog", which was arithmetically true and completely misleading:
            // with no background in the frame there was nothing for the fog to be measured
            // against. A pose that cannot see water has failed at being a POSE, and the honest
            // response is to take a different one rather than to publish a conclusion about
            // shading drawn from the inside of a rock.
            //
            // The second pose backs off and climbs, which is the only direction that can help: it
            // is the map's own bounding box that put the camera inside it, so standing further out
            // and higher is the move that guarantees the content is in front rather than around.
            for (int attempt = 0; attempt < 2; attempt++)
            {
                bool retry = attempt == 1;
                float standOff = retry ? RetryStandOffOfRadius : StandOffOfRadius;
                float climb = retry ? radius * RetryClimbOfRadius : EyeHeightUnits;
                float pitch = retry ? RetryPitchDegrees : PitchDegrees;

                Vector3 centre = result.FrameCenter;
                float eyeY = result.FrameMinY + climb;
                // Keep the diver under the surface whatever the map's geometry says — a camera
                // above the water is a different scene entirely (EnvMode.Daylight branches on it).
                eyeY = Mathf.Min(eyeY, result.WaterLevel - 3f * DepthLight.UnitsPerMetre);

                Vector3 back = new Vector3(0.62f, 0f, 0.78f).normalized;   // fixed, reproducible bearing
                Vector3 eye = new Vector3(centre.x, 0f, centre.z) + back * (radius * standOff);
                eye.y = eyeY;
                cam.transform.position = eye;
                cam.transform.rotation = Quaternion.Euler(pitch, Quaternion.LookRotation(
                    new Vector3(centre.x - eye.x, 0f, centre.z - eye.z)).eulerAngles.y, 0f);

                float depthUnits = result.WaterLevel - eye.y;
                depthMetres = depthUnits / DepthLight.UnitsPerMetre;

                // Long enough for several LateUpdates: the atmosphere, the backdrop bake and the
                // ambient floor all settle over frames, not instantly, and the boids need a moment
                // to stop being stacked on their spawn point.
                yield return new WaitForSeconds(SettleSeconds);

                DepthAtmosphere.CurrentRange(out float fogStart, out float fogEnd);
                float camToContent = Vector3.Distance(cam.transform.position, result.Center);
                Debug.Log($"[QCMap] {shortId} '{result.MapName}' posed={(retry ? "diver-retry" : "diver")} " +
                          $"eye={eye} depth={depthMetres:F1}m " +
                          $"radius={radius:F0} camToContent={camToContent:F0} " +
                          $"fog={(RenderSettings.fog ? "on" : "OFF")} range={fogStart:F0}..{fogEnd:F0} " +
                          $"fogColor={RenderSettings.fogColor} " +
                          $"nearRim={WaterFog.FactorAt(Mathf.Max(0f, camToContent - result.Radius), fogStart, fogEnd) * 100f:F0}% " +
                          $"farRim={WaterFog.FactorAt(camToContent + result.Radius, fogStart, fogEnd) * 100f:F0}% " +
                          $"loaded={result.Loaded} failed={result.Failed} renderers={renderers.Count} " +
                          $"build={Time.realtimeSinceStartup - t0:F0}s");

                // ── the three frames ─────────────────────────────────────────────
                // The PNG is only rewritten when the shot is one worth keeping, so a map that
                // needed the second pose ships the picture that was actually measured.
                string png = Path.Combine(dir, "qc_map_" + shortId + ".png");
                byte[] withMap = null, noMap = null, noFog = null;
                yield return Capture(cam, rt, readback, png, b => withMap = b);

                bool hadFog = RenderSettings.fog;
                RenderSettings.fog = false;
                yield return null;
                yield return Capture(cam, rt, readback, null, b => noFog = b);
                RenderSettings.fog = hadFog;
                yield return null;

                if (result.Root != null) result.Root.SetActive(false);
                yield return null;
                yield return Capture(cam, rt, readback, null, b => noMap = b);
                if (result.Root != null) result.Root.SetActive(true);
                yield return null;

                // 🔎 bottomUpRows: Texture2D.GetRawTextureData hands rows back BOTTOM-UP, so the
                // first rows of the buffer are the bottom of the picture. Passing it in rather
                // than assuming it is the whole point — the first run of this pass reported
                // backdropSpan −0.552/−0.576/−0.681 and read as "the gradient is upside down"
                // when the magnitude had been right all along and only the subtraction was
                // backwards. bandTop/bandBottom are logged separately so the two ends can be
                // compared against the PNG instead of against a convention.
                shot = QcPixels.MeasureMap(withMap, noMap, noFog, rt.width, rt.height,
                                           QcPixels.SubjectTolerance, bottomUpRows: true);
                shot.Renderers = renderers.Count;
                shot.Loaded = result.Loaded;
                shot.Failed = result.Failed;

                Debug.Log(QcPixels.MapLine(name + "/" + shortId,
                                           retry ? "diver-retry" : "diver", depthMetres, shot));

                // The second pose is only worth its four seconds when the first one failed at
                // being a pose. Anything else — dark, flat, fogless — is a finding, and taking
                // another photograph of it would be looking for a prettier answer.
                string reason = QcPixels.MapReason(shot);
                if (reason != "camera-buried" && reason != "no-water" && reason != "map-not-in-frame")
                    break;
                if (retry)
                    Debug.LogWarning($"[QCMap] {shortId} still {reason} after the second pose — " +
                                     "this map's framing box does not contain a place to stand");
            }

            bool pass = QcPixels.MapPasses(shot);

            if (result.Root != null) Object.Destroy(result.Root);
            yield return null;
            onDone(pass);
        }

        // ── capture ──────────────────────────────────────────────────────────────

        /// <summary>
        /// One frame, rendered on demand into <paramref name="rt"/> and read straight back — the
        /// same routine <see cref="QcModelShot"/> uses, and for the same two reasons: the bytes
        /// have to come back to us to be counted, and the capture has to be at END OF FRAME because
        /// <see cref="UnderwaterShading"/> raises the ambient in LateUpdate and hands it back at the
        /// top of the next frame. A mid-frame capture would photograph light the app never renders.
        /// </summary>
        private static IEnumerator Capture(Camera cam, RenderTexture rt, Texture2D readback,
                                           string pngPath, System.Action<byte[]> onBytes)
        {
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
                RenderTexture.active = prevActive;
                cam.targetTexture = prevTarget;
            }

            if (!string.IsNullOrEmpty(pngPath))
            {
                try
                {
                    File.WriteAllBytes(pngPath, readback.EncodeToPNG());
                    Debug.Log("[QCMap] shot -> " + pngPath);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[QCMap] cannot write " + pngPath + ": " + e.Message);
                }
            }

            onBytes(readback.GetRawTextureData());
        }
    }
}
