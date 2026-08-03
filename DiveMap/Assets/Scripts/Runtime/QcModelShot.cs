using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DiveMap.Core;
using GLTFast;
using UnityEngine;

namespace DiveMap.Runtime
{
    /// <summary>
    /// The QC pass that photographs REAL MODELS, one per frame, close enough to see.
    ///
    /// 🔴 Why it had to be written. CI has been taking two wide shots and twenty-five UI shots of a
    /// scene that pulls exactly four GLBs off the CDN (school_scad, school_barracuda,
    /// pod_yellowtail, msh_whaleshark); everything else in that map is geometry this codebase
    /// generates. The black-skin bug lives in the models' own tangents — so for 222 of the 226
    /// modules we ship, no CI screenshot has ever contained the thing being tested. Green shots,
    /// zero evidence. The user's phrasing: หลักฐานจากเว็บไม่นับ ต้องเป็นตัว Unity.
    ///
    /// So: nine representatives are downloaded through the app's OWN loader — manifest → CDN →
    /// <see cref="AssetCacheStore"/> → glTFast → the same TameMetal/GoldFx post-pass a map item
    /// gets — placed one at a time, framed to fill the shot, and photographed. Going through the
    /// cache is deliberate: it means this pass also exercises the generation gate, which is the
    /// mechanism that decides whether a device ever sees a repaired GLB at all.
    ///
    /// 🔎 Two frames per model, not one. Every shot is taken twice from an identical camera pose,
    /// once with the model and once without it, and the pixels that differ ARE the model. That is
    /// what stops this from repeating the failure it exists to fix: a beautiful photograph of empty
    /// water scores 0% subject and is logged FAIL, loudly, instead of being reported as a pass.
    /// The arithmetic lives in <see cref="QcPixels"/> where it is tested.
    ///
    /// 🔎 No lights are added. The models are staged far out to the side of the map, at the same
    /// depth as its content, and lit by exactly what the app lights the map with — the sun, fill
    /// and Trilight ambient from <c>AppBoot.SetupLighting</c>, the reflection cube, the fog, and
    /// the <see cref="UnderwaterShading"/> floor. A QC rig with its own flattering key light would
    /// hide the very bug it is looking for. The capture is taken at END OF FRAME for the same
    /// reason: UnderwaterShading raises the ambient in LateUpdate and hands it back at the top of
    /// the next frame, so a mid-frame capture would photograph the model in light the app never
    /// actually renders it in — and would report black that is not there.
    /// </summary>
    public static class QcModelShot
    {
        /// <summary>
        /// One of each way a model goes wrong: scanned CC0 props, statues that also run the gold FX
        /// path, wrecks (one of them the metallic hull the whole reflection-cube story is about),
        /// and marine models, including the barracuda that has just been re-exported at 2048².
        ///
        /// 🔴 The last three are the point of this round. <c>cc0:statue_singha</c> and
        /// <c>cc0:wreck_chang</c> are the two models the USER complained about by name, and in
        /// every QC run so far neither of them has ever been photographed — the pass proved six
        /// other models were fine while the two under complaint went unseen. <c>cc0:rock_cluster</c>
        /// comes in as the control: 1.8 KB, Draco, no UVs and no textures at all, so anything that
        /// goes wrong on it cannot be a texture problem.
        ///
        /// Both new statue/wreck URLs resolve to <c>maps.siamdive.com</c> rather than the Bunny
        /// CDN, which is deliberate to leave in: that is the path a real map item takes for them,
        /// and a QC pass that quietly tested a different host would be testing a different app.
        /// </summary>
        public static readonly string[] AssetIds =
        {
            "cc0:kraken",
            "stat:verdant_poseidon",
            "cc0:wreck_hardeep",
            "sw:htms732",
            "msh:barracuda",
            "msh:lionfish",
            "cc0:statue_singha",
            "cc0:wreck_chang",
            "cc0:rock_cluster",
        };

        /// <summary>
        /// <c>darkOfSubject</c> as measured in run 30768353482 — the run a human looked at, passed
        /// by eye ("the best these have ever looked": Poseidon showing its dark green stone and a
        /// real specular for the first time, HTMS 732's camouflage crisp) and shipped to
        /// TestFlight. Index-matched to <see cref="AssetIds"/>.
        ///
        /// 🔴 These are not targets and they are not quality scores — a model is allowed to be
        /// dark. They are the answer to "what did this model look like when it was last known
        /// good", and the gate fires when a model gets meaningfully DARKER than its own entry. Re-
        /// record them (all of them, from one run, after looking at the pictures) whenever the
        /// lighting or the material pipeline changes on purpose; a stale baseline is worth more
        /// than no baseline, but a baseline nobody has looked at is worth less than none.
        /// </summary>
        /// <remarks>
        /// 🔴 ALL NINE WERE RESET TO −1 BY WO-E3, ON PURPOSE, AND MUST BE RE-RECORDED.
        ///
        /// HANDOFF §6 rule 2: re-record these whenever the lighting or material pipeline changes on
        /// purpose, from ONE run, AFTER a human has looked at the pictures. WO-E3 changed both, as
        /// hard as they can be changed — gamma → linear (so every normal map in the app is back),
        /// ACES tone mapping, the web's light rig, and the reflection cube down to 0.3. Every
        /// number that was here was measured through a pipeline that no longer exists, so keeping
        /// them would not be a stale baseline, it would be a baseline from a different app, and the
        /// gate would fire on models that got BETTER.
        ///
        /// Writing new numbers here from this session was not on the table either: the author of
        /// this change cannot run CI and has not seen a single frame from it, and rule 1 says
        /// evidence comes from the Unity build. −1 is the honest state: the gate falls back to the
        /// absolute MaxDarkPercent ceiling, the log prints "darkBaseline=none", and the next push
        /// reads the real numbers off a run somebody looked at.
        /// </remarks>
        private static readonly double[] DarkBaselines =
        {
            -1.0,    // cc0:kraken            (was 2.33 — pre-linear, pre-ACES)
            -1.0,    // stat:verdant_poseidon (was 31.47)
            -1.0,    // cc0:wreck_hardeep     (was 36.86)
            -1.0,    // sw:htms732            (was 62.34)
            -1.0,    // msh:barracuda         (was 14.57)
            -1.0,    // msh:lionfish          (was 0.86)
            -1.0,    // cc0:statue_singha     — never had one
            -1.0,    // cc0:wreck_chang       — never had one
            -1.0,    // cc0:rock_cluster      — never had one
        };

        /// <summary>Baseline for <paramref name="assetId"/>, or −1 when none is recorded.</summary>
        private static double BaselineFor(string assetId)
        {
            for (int i = 0; i < AssetIds.Length && i < DarkBaselines.Length; i++)
                if (AssetIds[i] == assetId) return DarkBaselines[i];
            return -1.0;
        }

        /// <summary>How far to the side of the map the models are staged, world units. Well clear
        /// of the seabed disc (340 u) and its fish, so the only thing that can move between the
        /// two frames of a pair is the model itself.</summary>
        private const float StageOffset = 4000f;

        /// <summary>Per model: download + parse + instantiate. A model that has not landed by now
        /// is a FAIL worth reporting, not a reason to lose the other eight.</summary>
        private const float PerModelSeconds = 45f;

        /// <summary>
        /// Whole-pass budget. llvmpipe renders at 135-300 ms a frame and the nine GLBs are ~19 MB
        /// together; the pass measures well under this, and the ceiling is here so that a CDN going
        /// slow costs us the remaining models' log lines rather than the entire step — including
        /// the two screenshots already written and the artifact upload behind it.
        ///
        /// 170 s covered six models (the pass measured ~24 s of it, probes included). Three more,
        /// two of them ~2 MB and served from maps.siamdive.com rather than the CDN, put the
        /// expected figure near 36 s — but the budget is not sized for the expected case, it is
        /// sized so that a SLOW model still leaves room for the ones behind it: at the 45 s
        /// per-model ceiling, 240 s is what lets four models time out and the remaining five still
        /// be photographed and logged.
        /// </summary>
        private const float BudgetSeconds = 240f;

        /// <summary>Settle time after the model lands, before the pair of frames.</summary>
        private const float SettleSeconds = 0.4f;

        /// <summary>
        /// Photograph every model in <see cref="AssetIds"/> into
        /// <paramref name="dir"/>/qc_model_&lt;name&gt;.png and log one <c>[QCModel]</c> line each.
        /// </summary>
        /// <param name="dir">Where the PNGs go — the folder the other QC shots are written to.</param>
        /// <param name="manifest">The registry the app resolved its own map against.</param>
        /// <param name="mapCentre">Centre of the built map; the stage sits beside it, at its depth.</param>
        public static IEnumerator Run(string dir, AssetManifest manifest, Vector3 mapCentre)
        {
            float t0 = Time.realtimeSinceStartup;
            if (string.IsNullOrEmpty(dir)) dir = ".";

            Camera cam = Camera.main;
            if (cam == null)
            {
                Debug.LogWarning("[QCModel] no main camera — nothing was photographed");
                foreach (string id in AssetIds)
                    Debug.Log(QcPixels.Line(NameFor(manifest, id), false, 0, new QcPixels.Shot()));
                yield break;
            }

            // Nothing else may drive this camera while it is being aimed. The orbit rig is switched
            // off for the same reason angle 2 does it, and the tour is checked because a drone
            // flying the map re-drives Camera.main every frame — which is what made the two
            // existing screenshots identical.
            var orbit = cam.GetComponent<OrbitCamera>();
            if (orbit != null) orbit.enabled = false;
            if (ModeManager.Current != AppMode.View && ModeManager.Instance != null)
            {
                ModeManager.Instance.Exit();
                yield return null;
            }

            Vector3 stage = mapCentre + Vector3.right * StageOffset;
            Debug.Log($"[QCModel] pass start stage={stage} models={AssetIds.Length} " +
                      $"manifest={(manifest != null ? manifest.Count.ToString() : "MISSING")} " +
                      $"screen={Screen.width}x{Screen.height} ambientSky={RenderSettings.ambientSkyColor}");

            int w = Mathf.Clamp(Screen.width, 320, 1920);
            int h = Mathf.Clamp(Screen.height, 240, 1080);
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32)
            {
                name = "QcModelRT",
                antiAliasing = 1,
            };
            var readback = new Texture2D(w, h, TextureFormat.RGB24, false, false);

            int ok = 0, fail = 0;
            foreach (string assetId in AssetIds)
            {
                string name = NameFor(manifest, assetId);
                if (Time.realtimeSinceStartup - t0 > BudgetSeconds)
                {
                    // Out of budget is a FAIL, not a silence. A missing line reads as "the harness
                    // never ran"; this reads as what it is.
                    Debug.Log(QcPixels.Line(name, false, 0, new QcPixels.Shot()) + " budget-exhausted");
                    fail++;
                    continue;
                }

                bool shotOk = false;
                yield return One(cam, rt, readback, dir, manifest, assetId, name, stage,
                                 r => shotOk = r);
                if (shotOk) ok++; else fail++;

                // Sequential on purpose: nine 2048² models resident at once is not what a phone
                // does, and not what this box has to spare either.
                Resources.UnloadUnusedAssets();
                yield return null;
            }

            RenderTexture.active = null;
            cam.targetTexture = null;
            rt.Release();
            Object.Destroy(rt);
            Object.Destroy(readback);

            Debug.Log($"[QCModel] pass done ok={ok} fail={fail} " +
                      $"in {Time.realtimeSinceStartup - t0:F1}s (budget {BudgetSeconds:F0}s)");
        }

        // ── WO-E3: the depth pass ────────────────────────────────────────────────

        /// <summary>
        /// The reference animal for the depth shots. A marine model rather than a wreck or a
        /// statue because the complaint was about ANIMALS ("ฉลามกลายเป็นเงาแบน"), and the
        /// barracuda specifically because it is long, thin and pale — the shape and the albedo that
        /// showed the problem worst, and one already carrying a recorded dark baseline.
        /// </summary>
        private const string DepthAssetId = "msh:barracuda";

        /// <summary>
        /// The three depths, in metres.
        ///
        /// 15 is a shallow recreational dive, 30 the far end of one, and 52.4 is the depth the user
        /// was standing at on Poseidon when they reported the bug — the number is not a round one
        /// on purpose. Three, not one, because the requirement is not "it looks right at 52 m", it
        /// is "the subject-to-water ratio does not depend on depth", and one sample cannot say
        /// anything about a slope.
        /// </summary>
        private static readonly float[] DepthMetres = { 15f, 30f, 52f };

        /// <summary>
        /// Photograph the reference animal at three depths, with the tone curve on and off, and log
        /// the numbers that answer the user's report.
        ///
        /// 🔎 A/B IN ONE RUN. The ACES toggle is flipped between the two halves of each pair rather
        /// than between two CI runs. Two runs differ in the CDN's mood, the boids' phase and the
        /// llvmpipe frame time; one run differs in exactly the thing under test. HANDOFF §6 rule 1
        /// — evidence comes from the Unity build itself — is what makes that worth the extra four
        /// frames a depth.
        /// </summary>
        public static IEnumerator RunDepth(string dir, AssetManifest manifest, Vector3 mapCentre,
                                           float waterLevel)
        {
            if (string.IsNullOrEmpty(dir)) dir = ".";
            Camera cam = Camera.main;
            if (cam == null)
            {
                Debug.LogWarning("[QCDepth] no main camera — nothing was photographed");
                yield break;
            }

            string url = manifest != null ? manifest.ResolveUrl(DepthAssetId) : null;
            string name = NameFor(manifest, DepthAssetId);
            if (string.IsNullOrEmpty(url))
            {
                Debug.LogWarning("[QCDepth] url unresolved for " + DepthAssetId + " — pass skipped");
                yield break;
            }

            var orbit = cam.GetComponent<OrbitCamera>();
            if (orbit != null) orbit.enabled = false;

            int w = Mathf.Clamp(Screen.width, 320, 1920);
            int h = Mathf.Clamp(Screen.height, 240, 1080);
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32)
            {
                name = "QcDepthRT",
                antiAliasing = 1,
            };
            var readback = new Texture2D(w, h, TextureFormat.RGB24, false, false);

            var pivot = new GameObject("QcDepth:" + DepthAssetId);
            float scale = 1f;
            AssetManifest.Module mod = manifest.Get(DepthAssetId);
            if (mod != null && mod.DefaultScale > 0) scale = (float)mod.DefaultScale;
            pivot.transform.localScale = Vector3.one * scale;
            pivot.transform.position = new Vector3(mapCentre.x + StageOffset, waterLevel - 90f, mapCentre.z);

            float loadStart = Time.realtimeSinceStartup;
            Task<GltfImport> task = SceneBuilder.LoadForQc(url, DepthAssetId, pivot.transform);
            while (!task.IsCompleted && Time.realtimeSinceStartup - loadStart < PerModelSeconds)
                yield return null;
            GltfImport import = task.Status == TaskStatus.RanToCompletion ? task.Result : null;

            var renderers = new List<Renderer>();
            pivot.GetComponentsInChildren(true, renderers);
            if (import == null || renderers.Count == 0)
            {
                Debug.LogWarning($"[QCDepth] {name} did not arrive (loaded={import != null} " +
                                 $"renderers={renderers.Count}) — pass skipped, url={url}");
            }
            else
            {
                var acesOn = new QcPixels.DepthShot[DepthMetres.Length];
                bool acesAvailable = cam.GetComponent<AcesToneMapping>() != null;
                Debug.Log($"[QCDepth] pass start model={name} depths={DepthMetres.Length} " +
                          $"aces={(acesAvailable ? "available" : "NOT-ATTACHED")} " +
                          $"colorSpace={QualitySettings.activeColorSpace} waterLevel={waterLevel:F1}");

                for (int i = 0; i < DepthMetres.Length; i++)
                {
                    float metres = DepthMetres[i];
                    float y = waterLevel - metres * DepthLight.UnitsPerMetre;
                    pivot.transform.position = new Vector3(mapCentre.x + StageOffset, y, mapCentre.z);

                    // Frame it the same way the model pass does, so the two sets of numbers are
                    // about the same picture.
                    Bounds b = WorldBounds(renderers);
                    float aspect = (float)rt.width / Mathf.Max(1, rt.height);
                    Vector3 viewDir = new Vector3(0.55f, 0.32f, 1f).normalized;
                    Vector3 fwd = -viewDir;
                    Vector3 right = Vector3.Cross(Vector3.up, viewDir).normalized;
                    if (right.sqrMagnitude < 0.5f) right = Vector3.right;
                    Vector3 up = Vector3.Cross(viewDir, right).normalized;
                    Vector3 half = b.extents;
                    float dist = (float)QcPixels.FrameDistanceForBox(
                        half.x, half.y, half.z,
                        right.x, right.y, right.z,
                        up.x, up.y, up.z,
                        fwd.x, fwd.y, fwd.z,
                        cam.fieldOfView, aspect);
                    dist = Mathf.Max(dist, b.extents.magnitude + cam.nearClipPlane * 3f);
                    cam.transform.position = b.center + viewDir * dist;
                    cam.transform.LookAt(b.center);

                    // The camera has to BE at the depth as well as pointing at it: every system
                    // that dims with depth (DepthAtmosphere, UnderwaterShading, Backdrop) reads the
                    // CAMERA's y, not the subject's. Framing from a three-quarter view lifts the
                    // camera above the model, so this is a real difference of several metres.
                    Debug.Log($"[QCDepth] {name} target={metres:F1}m camY={cam.transform.position.y:F1} " +
                              $"camDepth={(waterLevel - cam.transform.position.y) / DepthLight.UnitsPerMetre:F1}m " +
                              $"dist={dist:F1}");

                    // Long enough for LateUpdate to have run several times: the atmosphere, the
                    // ambient floor and the backdrop re-bake all settle over frames, not instantly.
                    yield return new WaitForSeconds(SettleSeconds * 2f);

                    for (int pass = 0; pass < 2; pass++)
                    {
                        bool aces = pass == 0;
                        AcesToneMapping.Enabled = aces;
                        yield return null;

                        string png = aces
                            ? Path.Combine(dir, $"qc_depth_{Mathf.RoundToInt(metres)}m.png")
                            : Path.Combine(dir, $"qc_depth_{Mathf.RoundToInt(metres)}m_noaces.png");

                        byte[] withSubject = null, withoutSubject = null;
                        yield return Capture(cam, rt, readback, png, bytes => withSubject = bytes);
                        pivot.SetActive(false);
                        yield return null;
                        yield return Capture(cam, rt, readback, null, bytes => withoutSubject = bytes);
                        pivot.SetActive(true);
                        yield return null;

                        QcPixels.DepthShot d = QcPixels.MeasureDepth(withSubject, withoutSubject);
                        if (aces) acesOn[i] = d;
                        Debug.Log(QcPixels.DepthLine(name, metres, aces, d));
                    }
                    AcesToneMapping.Enabled = true;
                }

                Debug.Log($"[QCDepth] pass done depthCue={QcPixels.DepthCueVerdict(acesOn)} " +
                          $"minContrast={QcPixels.MinContrast:0.00} " +
                          $"minRange={QcPixels.MinDynamicRange:0.00}");
            }

            AcesToneMapping.Enabled = true;
            RenderTexture.active = null;
            cam.targetTexture = null;
            rt.Release();
            Object.Destroy(rt);
            Object.Destroy(readback);
            Object.Destroy(pivot);
            yield return null;
            import?.Dispose();
            Resources.UnloadUnusedAssets();
        }

        // ── one model ────────────────────────────────────────────────────────────

        private static IEnumerator One(Camera cam, RenderTexture rt, Texture2D readback, string dir,
                                       AssetManifest manifest, string assetId, string name,
                                       Vector3 stage, System.Action<bool> onDone)
        {
            string url = manifest != null ? manifest.ResolveUrl(assetId) : null;
            if (string.IsNullOrEmpty(url))
            {
                Debug.Log(QcPixels.Line(name, false, 0, new QcPixels.Shot()) +
                          " url=(unresolved) asset=" + assetId);
                onDone(false);
                yield break;
            }

            var pivot = new GameObject("QcModel:" + assetId);
            pivot.transform.position = stage;
            float scale = 1f;
            AssetManifest.Module mod = manifest.Get(assetId);
            if (mod != null && mod.DefaultScale > 0) scale = (float)mod.DefaultScale;
            pivot.transform.localScale = Vector3.one * scale;

            // The app's own loader — cache, generation gate, glTFast, TameMetal, GoldFx.
            float loadStart = Time.realtimeSinceStartup;
            Task<GltfImport> task = SceneBuilder.LoadForQc(url, assetId, pivot.transform);
            while (!task.IsCompleted && Time.realtimeSinceStartup - loadStart < PerModelSeconds)
                yield return null;

            // RanToCompletion, not IsCompleted: a faulted task is "completed" too, and reading its
            // Result rethrows — which would take the remaining eight models down with it.
            GltfImport import = task.Status == TaskStatus.RanToCompletion ? task.Result : null;
            bool loaded = import != null;
            float loadSecs = Time.realtimeSinceStartup - loadStart;

            var renderers = new List<Renderer>();
            pivot.GetComponentsInChildren(true, renderers);
            int rendererCount = renderers.Count;

            LogClips(assetId, pivot.transform);

            double baseline = BaselineFor(assetId);
            double darkGate = QcPixels.DarkGate(baseline);

            QcPixels.Shot shot = default;
            if (loaded && rendererCount > 0)
            {
                Bounds b = WorldBounds(renderers);
                float radius = Mathf.Max(b.extents.magnitude, 0.05f);
                float aspect = (float)rt.width / Mathf.Max(1, rt.height);

                // Three-quarter view from slightly above: one lit flank, one shadowed flank and the
                // top all in the same frame, so a surface that is black on one side only cannot
                // hide behind a head-on shot.
                Vector3 viewDir = new Vector3(0.55f, 0.32f, 1f).normalized;

                // Frame the BOX, not the bounding sphere. The first run failed four of six models
                // on `not-in-frame` (subject 1.8-3.9% against a 5% floor) and every one of them was
                // long and thin — a sphere sized by a wreck's length parks the camera far enough
                // back that the hull is a splinter. See QcPixels.FrameDistanceForBox.
                Vector3 fwd = -viewDir;
                Vector3 right = Vector3.Cross(Vector3.up, viewDir).normalized;
                if (right.sqrMagnitude < 0.5f) right = Vector3.right;   // straight up/down view
                Vector3 up = Vector3.Cross(viewDir, right).normalized;
                Vector3 half = b.extents;
                float dist = (float)QcPixels.FrameDistanceForBox(
                    half.x, half.y, half.z,
                    right.x, right.y, right.z,
                    up.x, up.y, up.z,
                    fwd.x, fwd.y, fwd.z,
                    cam.fieldOfView, aspect);
                // Never inside the near plane, whatever the model's size says.
                dist = Mathf.Max(dist, radius + cam.nearClipPlane * 3f);

                cam.transform.position = b.center + viewDir * dist;
                cam.transform.LookAt(b.center);

                yield return new WaitForSeconds(SettleSeconds);

                string png = Path.Combine(dir, "qc_model_" + name + ".png");
                byte[] withModel = null, withoutModel = null;
                yield return Capture(cam, rt, readback, png, bytes => withModel = bytes);

                // …and the same frame with nothing in it. Whatever differs is the model.
                pivot.SetActive(false);
                yield return null;
                yield return Capture(cam, rt, readback, null, bytes => withoutModel = bytes);
                pivot.SetActive(true);

                shot = QcPixels.Measure(withModel, withoutModel);

                Debug.Log($"[QCModel] {name} framed radius={radius:F2} dist={dist:F2} " +
                          $"scale={scale:F2} camY={cam.transform.position.y:F1} " +
                          $"load={loadSecs:F1}s url={url}");

                // A/B probes: same camera, same model, one shading input removed at a time, then
                // the same white textureless material on each of the two shaders. Four extra
                // frames a model — ~10 s across the pass, against a 240 s budget the six-model
                // version of it used 17 s of. See QcPixels.ProbeLine and QcPixels.NormalSourceVerdict.
                QcPixels.Shot whiteAlbedo = default, noMetalRough = default;
                QcPixels.Shot meshNormals = default, whiteGltf = default, greyAlbedo = default;
                QcPixels.Shot bias4 = default, bias10 = default;
                yield return Probe(cam, rt, readback, renderers, withoutModel, ProbeKind.WhiteAlbedo,
                                   s => whiteAlbedo = s);
                yield return Probe(cam, rt, readback, renderers, withoutModel, ProbeKind.NoMetalRough,
                                   s => noMetalRough = s);
                yield return Probe(cam, rt, readback, renderers, withoutModel, ProbeKind.WhiteGltf,
                                   s => whiteGltf = s);
                yield return Probe(cam, rt, readback, renderers, withoutModel, ProbeKind.GreyAlbedo,
                                   s => greyAlbedo = s);
                yield return ProbeMeshNormals(cam, rt, readback, renderers, withoutModel,
                                              s => meshNormals = s);
                yield return ProbeMipBias(cam, rt, readback, renderers, withoutModel, -4f, s => bias4 = s);
                yield return ProbeMipBias(cam, rt, readback, renderers, withoutModel, -10f, s => bias10 = s);
                Debug.Log(QcPixels.ProbeLine(name, shot, whiteAlbedo, noMetalRough, meshNormals,
                                             whiteGltf, greyAlbedo, bias4, bias10, darkGate));
            }
            else
            {
                Debug.LogWarning($"[QCModel] {name} did not arrive: loaded={loaded} " +
                                 $"renderers={rendererCount} after {loadSecs:F1}s url={url}");
            }

            Debug.Log(QcPixels.Line(name, loaded, rendererCount, shot,
                                    QcPixels.MinSubjectPercent, darkGate) +
                      (baseline < 0 ? " darkBaseline=none" : $" darkBaseline={baseline:0.00}%") +
                      $" darkGate={darkGate:0.00}%");
            bool pass = QcPixels.Passes(loaded, rendererCount, shot,
                                        QcPixels.MinSubjectPercent, darkGate);

            // Destroy the instance BEFORE disposing the import, and let the destroy actually
            // happen: Destroy is deferred to the end of the frame, and the import owns the meshes
            // and textures the instance is still drawing from until then.
            Object.Destroy(pivot);
            yield return null;
            import?.Dispose();

            onDone(pass);
        }

        // ── WO-F3: does this model actually ship playable clips? ─────────────────

        /// <summary>
        /// One <c>[Anim]</c> line per QC model. Costs nothing (no render, no extra download) and
        /// is the only place in CI that can answer the question the whole clip work turns on:
        /// does the file HAVE animation, and did glTFast hand it over?
        ///
        /// 🔴 The two failures it separates look identical on a screenshot and have opposite
        /// owners. <c>clips=0</c> across every model means the asset pipeline baked the animation
        /// out of the GLBs (the 3D side's problem); <c>clips=N</c> with a mode of <c>wave</c>
        /// means the app threw them away (this side's). The turtles on the CDN carry six clips —
        /// glide, cruise, burst, turn, patrol, accent — so a run in which msh:barracuda reports
        /// clips=0 is a measurement of the pipeline, not of this code.
        ///
        /// It reports whatever the QC list happens to contain: none of those nine models is a
        /// turtle today, and swapping one in would move a photometric baseline that a human
        /// already signed off on. This is deliberately the cheap half of the evidence.
        /// </summary>
        private static void LogClips(string assetId, Transform pivot)
        {
            var names = new List<string>(8);
            string reason = "-";
            Animation anim = pivot != null ? pivot.GetComponentInChildren<Animation>(true) : null;
            if (anim == null)
            {
                reason = "no-animation-component";
            }
            else
            {
                foreach (AnimationState st in anim)
                    if (st != null && st.clip != null) names.Add(st.name);
                if (names.Count == 0) reason = "no-clips";
            }

            string[] arr = names.ToArray();
            int pick = ClipPlay.IndexOf(arr, ClipRole.Cruise);
            BodyMotion mode = ClipPlay.Motion(arr.Length, false);
            if (reason == "-") reason = ClipPlay.MotionReason(arr.Length, false);

            // "shot", not "qc": QcAnimShot's lines are also [Anim] qc, and they carry a verdict
            // this one cannot (it never plays anything). Two prefixes so a grep can ask for either.
            Debug.Log($"[Anim] shot asset={assetId} clips={arr.Length} " +
                      $"names={(arr.Length == 0 ? "-" : string.Join(",", arr))} " +
                      $"pick={(pick >= 0 ? arr[pick] : "-")} " +
                      $"mode={(mode == BodyMotion.Clip ? "clip" : "wave")} reason={reason}");
        }

        // ── A/B probes ───────────────────────────────────────────────────────────

        /// <summary>Which shading input the probe frame removes.</summary>
        private enum ProbeKind { WhiteAlbedo, NoMetalRough, WhiteGltf, GreyAlbedo }

        /// <summary>Base-colour tint, glTFast's name then Standard's.</summary>
        private static readonly string[] ColorProps = { "baseColorFactor", "_Color" };

        /// <summary>First of <paramref name="names"/> this material actually has, or null.</summary>
        private static string PropOn(Material m, string[] names)
        {
            foreach (string n in names) if (m.HasProperty(n)) return n;
            return null;
        }

        /// <summary>Texture slots glTFast fills, in its own property names and Standard's.</summary>
        private static readonly string[][] AllTextureSlots =
        {
            new[] { "baseColorTexture", "_MainTex" },
            new[] { "metallicRoughnessTexture", "_MetallicGlossMap" },
            new[] { "normalTexture", "_BumpMap" },
            new[] { "emissiveTexture", "_EmissionMap" },
            new[] { "occlusionTexture", "_OcclusionMap" },
        };

        /// <summary>
        /// Probe 3 — the model rendered by Unity's OWN Standard shader on a plain white material:
        /// no textures at all, metallic 0, smoothness matching the roughness 0.6 the import now
        /// sets. Nothing is left that could carry the mottling except the mesh's own normals, and
        /// this is the only frame in the pass whose shading does not go through glTFast.
        ///
        /// Paired with <see cref="ProbeKind.WhiteGltf"/>, which is the identical white textureless
        /// material on glTFast's shader: the two differ by the SHADER and by nothing else, so
        /// <see cref="QcPixels.NormalSourceVerdict"/> can finally separate "the shader's normal
        /// path" from "the normals the Draco decode produced".
        ///
        /// 🔴 The material comes from <see cref="SceneBuilder.OpaqueMaterial"/>, i.e. the same
        /// Resources asset the seabed and the ropes use, NOT <c>Shader.Find("Standard")</c> — a
        /// shader reached only from code is stripped from a player build and renders magenta, and
        /// a magenta frame that scored well would be the worst possible outcome here. If the
        /// material or its shader does not survive the build, this reports an empty shot, which
        /// <see cref="QcPixels.NormalSourceVerdict"/> reads as <c>probe-failed</c> rather than as
        /// an answer.
        /// </summary>
        private static IEnumerator ProbeMeshNormals(Camera cam, RenderTexture rt, Texture2D readback,
                                                    List<Renderer> renderers, byte[] withoutModel,
                                                    System.Action<QcPixels.Shot> onShot)
        {
            Material white = SceneBuilder.OpaqueMaterial();
            if (white == null || white.shader == null || !white.shader.isSupported)
            {
                Debug.LogWarning("[QCProbe] mesh-normal probe skipped — no usable Standard material " +
                                 "(a magenta frame must not be scored)");
                onShot(default);
                yield break;
            }
            white.color = Color.white;
            if (white.HasProperty("_Metallic")) white.SetFloat("_Metallic", GlbShading.ProbeValidatedMetallic);
            if (white.HasProperty("_Glossiness")) white.SetFloat("_Glossiness", 1f - GlbShading.ProbeValidatedRoughness);
            if (white.HasProperty("_MainTex")) white.SetTexture("_MainTex", null);

            var saved = new List<Material[]>();
            foreach (Renderer r in renderers)
            {
                if (r == null) { saved.Add(null); continue; }
                saved.Add(r.sharedMaterials);
                var swap = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                for (int i = 0; i < swap.Length; i++) swap[i] = white;
                r.sharedMaterials = swap;
            }

            byte[] probed = null;
            yield return Capture(cam, rt, readback, null, bytes => probed = bytes);
            onShot(QcPixels.Measure(probed, withoutModel));

            for (int i = 0; i < renderers.Count; i++)
            {
                if (renderers[i] == null || saved[i] == null) continue;
                renderers[i].sharedMaterials = saved[i];
            }
            Object.Destroy(white);
        }

        /// <summary>
        /// Take one probe frame with <paramref name="kind"/>'s input removed, measure it against
        /// the model-off frame that is already in hand, and put every material back exactly as it
        /// was — the main shot has already been written, but the map view is photographed after
        /// this pass and must not inherit a debug material.
        ///
        /// Property names are glTFast's (<c>baseColorTexture</c>, <c>metallicRoughnessTexture</c>),
        /// with Unity's Standard names as a fallback so the probe still says something if a model
        /// ever arrives on a different shader. A material that has neither is left alone and
        /// simply contributes nothing to the probe.
        /// </summary>
        private static IEnumerator Probe(Camera cam, RenderTexture rt, Texture2D readback,
                                         List<Renderer> renderers, byte[] withoutModel,
                                         ProbeKind kind, System.Action<QcPixels.Shot> onShot)
        {
            var mats = new List<Material>();
            foreach (Renderer r in renderers)
            {
                if (r == null) continue;
                foreach (Material m in r.materials) if (m != null) mats.Add(m);
            }

            // Probe 3b: everything off at once — every texture slot cleared, colour white, the same
            // metallic/roughness scalars, on glTFast's own shader. The one thing it still shares
            // with the shipped material is the shader, which is exactly what ProbeMeshNormals is
            // the control for.
            if (kind == ProbeKind.WhiteGltf)
            {
                var savedAll = new List<Texture[]>();
                var savedTint = new List<Color>();
                var savedMr = new List<Vector2>();
                foreach (Material m in mats)
                {
                    var slots = new Texture[AllTextureSlots.Length];
                    for (int k = 0; k < AllTextureSlots.Length; k++)
                    {
                        string pk = PropOn(m, AllTextureSlots[k]);
                        slots[k] = pk != null ? m.GetTexture(pk) : null;
                        if (pk != null) m.SetTexture(pk, null);
                    }
                    savedAll.Add(slots);
                    string cp = PropOn(m, ColorProps);
                    savedTint.Add(cp != null ? m.GetColor(cp) : Color.white);
                    if (cp != null) m.SetColor(cp, Color.white);
                    savedMr.Add(new Vector2(
                        m.HasProperty("metallicFactor") ? m.GetFloat("metallicFactor") : 0f,
                        m.HasProperty("roughnessFactor") ? m.GetFloat("roughnessFactor") : 0f));
                    if (m.HasProperty("metallicFactor")) m.SetFloat("metallicFactor", GlbShading.ProbeValidatedMetallic);
                    if (m.HasProperty("roughnessFactor")) m.SetFloat("roughnessFactor", GlbShading.ProbeValidatedRoughness);
                    m.DisableKeyword("_METALLICGLOSSMAP");
                    m.DisableKeyword("_NORMALMAP");
                    m.DisableKeyword("_EMISSION");
                    m.DisableKeyword("_OCCLUSION");
                }

                byte[] shotBytes = null;
                yield return Capture(cam, rt, readback, null, bytes => shotBytes = bytes);
                onShot(QcPixels.Measure(shotBytes, withoutModel));

                for (int i = 0; i < mats.Count; i++)
                {
                    Material m = mats[i];
                    for (int k = 0; k < AllTextureSlots.Length; k++)
                    {
                        string pk = PropOn(m, AllTextureSlots[k]);
                        if (pk != null) m.SetTexture(pk, savedAll[i][k]);
                    }
                    string cp = PropOn(m, ColorProps);
                    if (cp != null) m.SetColor(cp, savedTint[i]);
                    if (m.HasProperty("metallicFactor")) m.SetFloat("metallicFactor", savedMr[i].x);
                    if (m.HasProperty("roughnessFactor")) m.SetFloat("roughnessFactor", savedMr[i].y);
                    if (savedAll[i][1] != null) m.EnableKeyword("_METALLICGLOSSMAP");
                    if (savedAll[i][2] != null) m.EnableKeyword("_NORMALMAP");
                    if (savedAll[i][3] != null) m.EnableKeyword("_EMISSION");
                    if (savedAll[i][4] != null) m.EnableKeyword("_OCCLUSION");
                }
                yield break;
            }

            var savedTex = new List<Texture>();
            var savedA = new List<float>();
            var savedColor = new List<Color>();
            var savedB = new List<float>();

            bool albedoKind = kind == ProbeKind.WhiteAlbedo || kind == ProbeKind.GreyAlbedo;
            // The tint the probe paints on. White answers "is the model dark because its albedo is
            // dark", which is arithmetic, not evidence; mid-grey keeps the model's own measured
            // brightness and removes only the texture's VARIATION, which is the actual question.
            float g = (float)QcPixels.MeanAlbedo;
            Color tint = kind == ProbeKind.GreyAlbedo ? new Color(g, g, g, 1f) : Color.white;
            string texProp = albedoKind ? "baseColorTexture" : "metallicRoughnessTexture";
            string texAlt = albedoKind ? "_MainTex" : "_MetallicGlossMap";

            foreach (Material m in mats)
            {
                string p = m.HasProperty(texProp) ? texProp : (m.HasProperty(texAlt) ? texAlt : null);
                savedTex.Add(p != null ? m.GetTexture(p) : null);
                if (p != null) m.SetTexture(p, null);

                if (albedoKind)
                {
                    // Factor set as well: clearing the slot alone leaves baseColorFactor tinting a
                    // "white" default, which is not the same experiment.
                    string c = m.HasProperty("baseColorFactor") ? "baseColorFactor"
                             : (m.HasProperty("_Color") ? "_Color" : null);
                    savedColor.Add(c != null ? m.GetColor(c) : Color.white);
                    if (c != null) m.SetColor(c, tint);
                    savedA.Add(0f);
                    savedB.Add(0f);
                }
                else
                {
                    savedColor.Add(Color.white);
                    savedA.Add(m.HasProperty("metallicFactor") ? m.GetFloat("metallicFactor") : 0f);
                    savedB.Add(m.HasProperty("roughnessFactor") ? m.GetFloat("roughnessFactor") : 0f);
                    if (m.HasProperty("metallicFactor")) m.SetFloat("metallicFactor", 0f);
                    if (m.HasProperty("roughnessFactor")) m.SetFloat("roughnessFactor", 0.6f);
                    m.DisableKeyword("_METALLICGLOSSMAP");
                }
            }

            byte[] probed = null;
            yield return Capture(cam, rt, readback, null, bytes => probed = bytes);
            onShot(QcPixels.Measure(probed, withoutModel));

            for (int i = 0; i < mats.Count; i++)
            {
                Material m = mats[i];
                string p = m.HasProperty(texProp) ? texProp : (m.HasProperty(texAlt) ? texAlt : null);
                if (p != null) m.SetTexture(p, savedTex[i]);
                if (albedoKind)
                {
                    string c = m.HasProperty("baseColorFactor") ? "baseColorFactor"
                             : (m.HasProperty("_Color") ? "_Color" : null);
                    if (c != null) m.SetColor(c, savedColor[i]);
                }
                else
                {
                    if (m.HasProperty("metallicFactor")) m.SetFloat("metallicFactor", savedA[i]);
                    if (m.HasProperty("roughnessFactor")) m.SetFloat("roughnessFactor", savedB[i]);
                    if (savedTex[i] != null) m.EnableKeyword("_METALLICGLOSSMAP");
                }
            }
        }

        /// <summary>
        /// Pull the base-colour sampler into sharper mips for one frame and see what the blotches
        /// do. The reasoning, and why this is not shipped as a fix, is in
        /// <see cref="QcPixels.BiasVerdict"/>.
        ///
        /// <c>mipMapBias</c> is Unity-side sampler state rather than anything baked into the GPU
        /// object, so unlike <c>graphicsFormat</c> it ought to reach a texture that arrived through
        /// <c>CreateExternalTexture</c> — "ought to" being exactly the kind of claim this pass
        /// exists to stop people making, hence the frame. Restored afterwards because the map view
        /// is photographed after this pass and shares these textures.
        /// </summary>
        private static IEnumerator ProbeMipBias(Camera cam, RenderTexture rt, Texture2D readback,
                                                List<Renderer> renderers, byte[] withoutModel,
                                                float bias, System.Action<QcPixels.Shot> onShot)
        {
            var touched = new List<Texture>();
            var saved = new List<float>();
            foreach (Renderer r in renderers)
            {
                if (r == null) continue;
                foreach (Material m in r.materials)
                {
                    if (m == null) continue;
                    string p = PropOn(m, AllTextureSlots[0]);
                    Texture tex = p != null ? m.GetTexture(p) : null;
                    if (tex == null || touched.Contains(tex)) continue;
                    touched.Add(tex);
                    saved.Add(tex.mipMapBias);
                    tex.mipMapBias = bias;
                }
            }
            if (touched.Count == 0)
            {
                // No base-colour texture to bias — nothing to learn, and an empty shot reads as
                // probe-failed rather than as a clean result.
                onShot(default);
                yield break;
            }

            byte[] probed = null;
            yield return Capture(cam, rt, readback, null, bytes => probed = bytes);
            onShot(QcPixels.Measure(probed, withoutModel));

            for (int i = 0; i < touched.Count; i++) touched[i].mipMapBias = saved[i];
        }

        // ── capture ──────────────────────────────────────────────────────────────

        /// <summary>
        /// One frame, rendered on demand into <paramref name="rt"/> and read straight back.
        ///
        /// Not <c>ScreenCapture.CaptureScreenshot</c>: that hands the bytes to a file and never to
        /// us, so there would be nothing to count — and the numbers, not the pictures, are what
        /// make this pass evidence. Waiting for end of frame first is not optional; see the class
        /// remarks on UnderwaterShading.
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
                    Debug.Log("[QCModel] shot -> " + pngPath);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[QCModel] cannot write " + pngPath + ": " + e.Message);
                }
            }

            // A copy, because the next capture reuses this same Texture2D and the caller is
            // holding both frames at once to compare them.
            onBytes(readback.GetRawTextureData());
        }

        // ── helpers ──────────────────────────────────────────────────────────────

        private static Bounds WorldBounds(List<Renderer> renderers)
        {
            var b = new Bounds();
            bool has = false;
            foreach (Renderer r in renderers)
            {
                if (r == null || !r.enabled) continue;
                if (!has) { b = r.bounds; has = true; }
                else b.Encapsulate(r.bounds);
            }
            return has ? b : new Bounds(Vector3.zero, Vector3.one);
        }

        /// <summary>
        /// The GLB's own file name — <c>cc0_kraken_xr0</c> — because that is what the artifact and
        /// the CDN both call it, and an id with a colon in it cannot be a file name anyway.
        /// </summary>
        private static string NameFor(AssetManifest manifest, string assetId)
        {
            string url = manifest != null ? manifest.ResolveUrl(assetId) : null;
            if (!string.IsNullOrEmpty(url))
            {
                int q = url.IndexOf('?');
                if (q >= 0) url = url.Substring(0, q);
                string file = Path.GetFileNameWithoutExtension(url);
                if (!string.IsNullOrEmpty(file)) return file;
            }
            return assetId.Replace(':', '_');
        }
    }
}
