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
    /// Two files, one variable, one frame each, from an identical camera — the pass that decides
    /// between the remaining explanations for "ทุกวัตถุดำ" instead of adding another opinion to them.
    ///
    /// 🔴 WHY IT EXISTS, and what it is a correction to. This session has produced two theories
    /// about the dark models and both of them were argued from statistics taken off the files:
    ///
    ///   • "the texture's dark tail" — real, and it turned out to explain <c>blackOfSubject</c>
    ///     (0.12-33%) while the number that actually needs to move is <c>darkOfSubject</c> (65-95%);
    ///   • "ETC1S destroyed the normal maps" — RETRACTED. The measurement behind it was taken over
    ///     a single 4×4 block and quoted as a property of the whole map; swept properly the loss is
    ///     about 15%, not 4,000×, and the web ships MORE damaged normal maps than we do while being
    ///     the picture the user says looks right. See <see cref="GlbShading"/>.
    ///
    /// Both were plausible, both were measured, and both were wrong about the thing that matters.
    /// What neither could do is change one input and look. That is all this pass does.
    ///
    /// 🔎 THE FILES ARE THE EXPERIMENT. Each pair below differs in exactly ONE thing, and the
    /// person who built them held everything else byte-identical — same geometry, same base colour,
    /// same metallic-roughness, same emissive. So the difference between the two frames cannot be
    /// anything except the named variable. That is a much stronger instrument than any probe in
    /// <see cref="QcModelShot"/>, which can only edit material properties at runtime and can never
    /// change what is IN the file.
    ///
    /// 🔎 A SEPARATE PASS, ON PURPOSE. <see cref="QcModelShot"/>'s nine baseline models must keep
    /// photographing exactly what they photographed before — HANDOFF §6 rule 2 — so nothing here
    /// touches its list, its framing or its baselines. The pilots are staged on their own, and a
    /// pilot that 404s costs this pass a line and nothing else.
    /// </summary>
    public static class QcPilotAb
    {
        /// <summary>
        /// One row per experiment: what is being tested, the file that ships today, and the file
        /// that differs from it in exactly one way.
        ///
        /// 🔴 <c>ScaleAssetId</c> is there so both halves are placed at the module's REAL
        /// defaultScale. A pilot photographed at scale 1 beside a shipped model at scale 8 would be
        /// a different experiment — texel density, mip level and the framing distance would all
        /// move with it, and those are the very things under suspicion.
        /// </summary>
        private struct Pair
        {
            public string Variable;      // what differs, in one word
            public string Name;          // for the log and the PNG
            public string ScaleAssetId;  // module whose defaultScale both halves are placed at
            public string ShippedUrl;
            public string PilotUrl;
        }

        private const string Cdn = "https://siamdive-cdn.b-cdn.net/models/xr/";

        /// <summary>
        /// 🔎 The normal-map pilots are the retraction's own test. The claim that ETC1S ruins normal
        /// maps has been withdrawn on the numbers, but withdrawing a claim is not the same as
        /// showing it does not matter — these three files are the same models with the normal map
        /// rebuilt from the 4096² master at 1024² UASTC and NOTHING else changed, so they answer it
        /// with a picture. Two ruins and the kraken: if UASTC moves the ruins and not the kraken,
        /// the encoding matters for some content; if it moves neither, the whole line of enquiry is
        /// closed and the queue is the tangent one below.
        ///
        /// The tangent slot is left empty and wired up rather than commented out. Eight files in
        /// the catalogue ship a normal map with NO TANGENT attribute at all — Singha, HTMS Chang,
        /// Stone King, Golden Trident, Whale Shark, Barracuda School, Scad School, Trevally, all
        /// built on 22 July before <c>fix_tangents.mjs</c> entered the pipeline — and a normal map
        /// without a tangent frame is normal mapping from a basis that does not exist. Adding the
        /// URL is then a one-line change rather than a code change, which is the difference between
        /// the next CI run answering the question and the one after it.
        /// </summary>
        private static readonly Pair[] Pairs =
        {
            new Pair
            {
                Variable = "normalmap-uastc",
                Name = "ruin_domed_temple",
                ScaleAssetId = "ruin:domed_temple",
                ShippedUrl = Cdn + "ruin_domed_temple_xr0.glb",
                PilotUrl = Cdn + "pilot_nmuastc_ruin_domed_temple_xr0.glb",
            },
            new Pair
            {
                Variable = "normalmap-uastc",
                Name = "ruin_grand_byzantine",
                ScaleAssetId = "ruin:grand_byzantine",
                ShippedUrl = Cdn + "ruin_grand_byzantine_xr0.glb",
                PilotUrl = Cdn + "pilot_nmuastc_ruin_grand_byzantine_xr0.glb",
            },
            new Pair
            {
                Variable = "normalmap-uastc",
                Name = "cc0_kraken",
                ScaleAssetId = "cc0:kraken",
                ShippedUrl = Cdn + "cc0_kraken_xr0.glb",
                PilotUrl = Cdn + "pilot_nmuastc_cc0_kraken_xr0.glb",
            },
            // ── the tangent experiment, waiting for its file ──────────────────────
            // new Pair
            // {
            //     Variable = "tangent-added",
            //     Name = "Singha_Statue_Underwater",
            //     ScaleAssetId = "cc0:statue_singha",
            //     ShippedUrl = "https://maps.siamdive.com/models/xr/Singha_Statue_Underwater_xr0.glb",
            //     PilotUrl = Cdn + "pilot_tangent_Singha_Statue_Underwater_xr0.glb",
            // },
        };

        private const float PerFileSeconds = 45f;
        private const float BudgetSeconds = 300f;
        private const float SettleSeconds = 0.4f;
        private const float StageOffset = 8000f;

        /// <summary>
        /// Photograph both halves of every pair and log one <c>[QCPilot]</c> line each.
        /// </summary>
        public static IEnumerator Run(string dir, AssetManifest manifest, Vector3 mapCentre)
        {
            float t0 = Time.realtimeSinceStartup;
            if (string.IsNullOrEmpty(dir)) dir = ".";

            Camera cam = Camera.main;
            if (cam == null)
            {
                Debug.LogWarning("[QCPilot] no main camera — nothing was photographed");
                yield break;
            }
            var orbit = cam.GetComponent<OrbitCamera>();
            if (orbit != null) orbit.enabled = false;

            Vector3 stage = mapCentre + Vector3.right * StageOffset;
            Debug.Log($"[QCPilot] pass start pairs={Pairs.Length} stage={stage} " +
                      $"screen={Screen.width}x{Screen.height} " +
                      $"colorSpace={QualitySettings.activeColorSpace}");

            int w = Mathf.Clamp(Screen.width, 320, 1920);
            int h = Mathf.Clamp(Screen.height, 240, 1080);
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "QcPilotRT", antiAliasing = 1 };
            var readback = new Texture2D(w, h, TextureFormat.RGB24, false, false);

            foreach (Pair pair in Pairs)
            {
                if (Time.realtimeSinceStartup - t0 > BudgetSeconds)
                {
                    Debug.Log($"[QCPilot] {pair.Name} var={pair.Variable} budget-exhausted");
                    continue;
                }
                yield return OnePair(cam, rt, readback, dir, manifest, pair, stage);
                Resources.UnloadUnusedAssets();
                yield return null;
            }

            RenderTexture.active = null;
            cam.targetTexture = null;
            rt.Release();
            Object.Destroy(rt);
            Object.Destroy(readback);
            Debug.Log($"[QCPilot] pass done in {Time.realtimeSinceStartup - t0:F1}s");
        }

        private static IEnumerator OnePair(Camera cam, RenderTexture rt, Texture2D readback, string dir,
                                           AssetManifest manifest, Pair pair, Vector3 stage)
        {
            float scale = 1f;
            AssetManifest.Module mod = manifest != null ? manifest.Get(pair.ScaleAssetId) : null;
            if (mod != null && mod.DefaultScale > 0) scale = (float)mod.DefaultScale;

            QcPixels.Shot shipped = default, pilot = default;
            bool shippedOk = false, pilotOk = false;

            // 🔴 The camera pose is decided ONCE, from the shipped half, and reused verbatim for the
            // pilot. Re-framing each half would let a bounding box that moved by a millimetre change
            // the distance, the mip level and the framing — and then the two frames would differ by
            // the camera as well as by the file, which is the one thing this pass must never do.
            Vector3 camPos = Vector3.zero, lookAt = Vector3.zero;
            bool posed = false;

            for (int half = 0; half < 2; half++)
            {
                bool isPilot = half == 1;
                string url = isPilot ? pair.PilotUrl : pair.ShippedUrl;
                var pivot = new GameObject("QcPilot:" + pair.Name + (isPilot ? ":pilot" : ":shipped"));
                pivot.transform.position = stage;
                pivot.transform.localScale = Vector3.one * scale;

                float loadStart = Time.realtimeSinceStartup;
                Task<GltfImport> task = SceneBuilder.LoadForQc(url, pair.ScaleAssetId, pivot.transform);
                while (!task.IsCompleted && Time.realtimeSinceStartup - loadStart < PerFileSeconds)
                    yield return null;
                GltfImport import = task.Status == TaskStatus.RanToCompletion ? task.Result : null;

                var renderers = new List<Renderer>();
                pivot.GetComponentsInChildren(true, renderers);
                if (import == null || renderers.Count == 0)
                {
                    Debug.Log($"[QCPilot] {pair.Name} var={pair.Variable} " +
                              $"half={(isPilot ? "pilot" : "shipped")} DID-NOT-ARRIVE " +
                              $"loaded={import != null} renderers={renderers.Count} url={url}");
                    Object.Destroy(pivot);
                    yield return null;
                    import?.Dispose();
                    continue;
                }

                if (!posed)
                {
                    Bounds b = WorldBounds(renderers);
                    float aspect = (float)rt.width / Mathf.Max(1, rt.height);
                    Vector3 viewDir = new Vector3(0.55f, 0.32f, 1f).normalized;
                    Vector3 fwd = -viewDir;
                    Vector3 right = Vector3.Cross(Vector3.up, viewDir).normalized;
                    if (right.sqrMagnitude < 0.5f) right = Vector3.right;
                    Vector3 up = Vector3.Cross(viewDir, right).normalized;
                    Vector3 halfExt = b.extents;
                    float dist = (float)QcPixels.FrameDistanceForBox(
                        halfExt.x, halfExt.y, halfExt.z,
                        right.x, right.y, right.z, up.x, up.y, up.z, fwd.x, fwd.y, fwd.z,
                        cam.fieldOfView, aspect);
                    dist = Mathf.Max(dist, b.extents.magnitude + cam.nearClipPlane * 3f);
                    camPos = b.center + viewDir * dist;
                    lookAt = b.center;
                    posed = true;
                }
                cam.transform.position = camPos;
                cam.transform.LookAt(lookAt);

                yield return new WaitForSeconds(SettleSeconds);

                string png = Path.Combine(dir, $"qc_pilot_{pair.Name}_{(isPilot ? "pilot" : "shipped")}.png");
                byte[] with = null, without = null;
                yield return Capture(cam, rt, readback, png, x => with = x);
                pivot.SetActive(false);
                yield return null;
                yield return Capture(cam, rt, readback, null, x => without = x);
                pivot.SetActive(true);

                QcPixels.Shot s = QcPixels.Measure(with, without);
                if (isPilot) { pilot = s; pilotOk = true; } else { shipped = s; shippedOk = true; }

                Debug.Log($"[QCPilot] {pair.Name} var={pair.Variable} " +
                          $"half={(isPilot ? "pilot" : "shipped")} " +
                          $"dark={s.DarkOfSubjectPercent:0.00}% black={s.BlackOfSubjectPercent:0.00}% " +
                          $"subject={s.SubjectPercent:0.00}% load={Time.realtimeSinceStartup - loadStart:F1}s");

                Object.Destroy(pivot);
                yield return null;
                import?.Dispose();
            }

            if (!shippedOk || !pilotOk)
            {
                Debug.Log($"[QCPilot] {pair.Name} var={pair.Variable} VERDICT=incomplete " +
                          $"(shipped={shippedOk} pilot={pilotOk}) — no comparison is possible");
                yield break;
            }

            // The verdict, in the only form that answers the question: how far did the ONE variable
            // move the number that needs to move. Percentage points, not a ratio — dark is already
            // a percentage and a ratio of two percentages reads as a much bigger claim than it is.
            double dDark = shipped.DarkOfSubjectPercent - pilot.DarkOfSubjectPercent;
            double dBlack = shipped.BlackOfSubjectPercent - pilot.BlackOfSubjectPercent;
            Debug.Log($"[QCPilot] {pair.Name} var={pair.Variable} VERDICT={Verdict(dDark, pilot)} " +
                      $"darkShipped={shipped.DarkOfSubjectPercent:0.00}% darkPilot={pilot.DarkOfSubjectPercent:0.00}% " +
                      $"moved={dDark:+0.00;-0.00}pp " +
                      $"blackShipped={shipped.BlackOfSubjectPercent:0.00}% blackPilot={pilot.BlackOfSubjectPercent:0.00}% " +
                      $"blackMoved={dBlack:+0.00;-0.00}pp " +
                      $"subjectShipped={shipped.SubjectPercent:0.00}% subjectPilot={pilot.SubjectPercent:0.00}%");
        }

        /// <summary>
        /// What the pair proves. The threshold is <see cref="QcPixels.BiasMovedPercent"/> — the same
        /// one point this project already uses for "this input moved the picture", because below it
        /// two frames of a live scene are not distinguishable from each other anyway.
        /// </summary>
        private static string Verdict(double movedPp, QcPixels.Shot pilot)
        {
            if (pilot.SubjectPercent < QcPixels.MinSubjectPercent) return "pilot-not-in-frame";
            if (movedPp > 20.0) return "THIS-IS-IT";
            if (movedPp > 5.0) return "contributes";
            if (movedPp > QcPixels.BiasMovedPercent) return "marginal";
            if (movedPp < -QcPixels.BiasMovedPercent) return "WORSE";
            return "no-effect";
        }

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
                    Debug.Log("[QCPilot] shot -> " + pngPath);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[QCPilot] cannot write " + pngPath + ": " + e.Message);
                }
            }
            onBytes(readback.GetRawTextureData());
        }
    }
}
