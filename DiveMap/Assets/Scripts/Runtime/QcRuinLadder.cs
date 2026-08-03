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
    /// The ablation that finds out WHERE the Atlantis ruins lose their light — in DAYLIGHT mode,
    /// against a reference surface of known albedo, one rung at a time.
    ///
    /// 🔴 WHY THIS EXISTS, and why every earlier answer was wrong. "ซุ้มดำ" has been chased through
    /// the files and every offline explanation has been measured and cleared on the very GLBs the
    /// app downloads:
    ///
    ///   base colour        surface-weighted mean 112-131, p95 170-199. Not dark textures.
    ///   UV gutter → mips   0.30% of grand_byzantine's surface on a black texel at 2048², 0.00% by
    ///                      64² — it falls with the mip, the opposite of the kraken's bug.
    ///   mesh normals       0.17-1.65% of area disagrees with the winding (controls: 0.06-0.35%).
    ///   metallic           the metal channel averages 0.09-0.21 out of 255. A dielectric.
    ///   occlusion map      absent on every file, web and XR alike. Nothing to bake shade in.
    ///   baseColorFactor    (1,1,1,1) on all eleven.
    ///   COLOR_0            no vertex colours on any of them, or on any control.
    ///   LOD1               only FishAssetPick ever selects it; a ruin always loads LOD0.
    ///
    /// And then the user switched the app to DAYLIGHT and photographed it again. Daylight returns
    /// out of <see cref="DepthAtmosphere"/> and <see cref="UnderwaterShading"/> before either of
    /// them touches anything, drops the fog entirely, and sets a bright neutral ambient
    /// (<see cref="EnvMode"/>: sky white × 0.72, ground 0xd8c9a8 × 0.72). The ruins are still black.
    ///
    /// 🔎 THE NUMBER THAT MAKES THIS A HUNT FOR LIGHT RATHER THAN FOR TEXTURE. In that daylight
    /// frame a surface of the ruins' own measured albedo (sRGB 112 → linear 0.162), lit by NOTHING
    /// BUT the daylight ambient bands and with the sun switched off entirely, works out at byte
    /// 80-100 through the tone curve. The dome's body measures byte 3 (p50), with 69% of it under
    /// 16. Shadow cannot do that — in the built-in pipeline the shadow term multiplies the
    /// DIRECT light and leaves the ambient alone. Roughly thirty times the ambient that surface
    /// should be receiving is going missing, and no statistic about a texture can explain it.
    ///
    /// 🔎 THE REFERENCE QUAD IS THE POINT. Every rung is photographed with a plain quad of known
    /// albedo standing beside the model, on <see cref="SceneBuilder.OpaqueMaterial"/> — the same
    /// material the seabed uses — lit by the same sun and the same ambient. Every number is then a
    /// RATIO against a surface whose answer is known in advance, so a rung that reads low is the
    /// model losing light rather than the whole scene being dim. Without it this pass would be
    /// measuring the studio again.
    /// </summary>
    public static class QcRuinLadder
    {
        /// <summary>
        /// Three ruins and one control. <c>domed_temple</c> and <c>ancient_byzantine</c> are the
        /// two worst (blackOfSubject 33.32% and 25.82% in run 30800189252); <c>grand_byzantine</c>
        /// is the one from the SAME family, the same generator and the same day that came back at
        /// 1.07% — the natural control for anything that blames the family rather than the file.
        /// <c>cc0:kraken</c> is the outside control: a model nobody has ever complained about.
        /// </summary>
        private static readonly string[] AssetIds =
        {
            "ruin:domed_temple",
            "ruin:ancient_byzantine",
            "ruin:grand_byzantine",
            "cc0:kraken",
        };

        /// <summary>
        /// The albedo painted on the reference quad, as an authored sRGB value. Mid-grey rather
        /// than white so the quad sits in the same part of the tone curve the models do — a white
        /// reference would be up on the shoulder, where a ratio against it is compressed and says
        /// less the darker the model gets.
        /// </summary>
        private const float ReferenceAlbedoSrgb = 0.65f;

        private const float PerModelSeconds = 45f;
        private const float BudgetSeconds = 240f;
        private const float SettleSeconds = 0.4f;

        /// <summary>Where the rig is staged — far enough from the map that nothing of the map's own
        /// can drift into a frame between two captures.</summary>
        private const float StageOffset = 6000f;

        /// <summary>
        /// Climb the ladder for every model in <see cref="AssetIds"/> and log one
        /// <c>[QCLadder]</c> line per rung.
        /// </summary>
        public static IEnumerator Run(string dir, AssetManifest manifest, Vector3 mapCentre)
        {
            float t0 = Time.realtimeSinceStartup;
            if (string.IsNullOrEmpty(dir)) dir = ".";

            Camera cam = Camera.main;
            if (cam == null || manifest == null)
            {
                Debug.LogWarning($"[QCLadder] cannot run — camera={(cam != null)} manifest={(manifest != null)}");
                yield break;
            }

            var orbit = cam.GetComponent<OrbitCamera>();
            if (orbit != null) orbit.enabled = false;

            // 🔴 DAYLIGHT, and this is the whole reason the pass is separate from QcModelShot.
            // Underwater there are three depth-dependent multipliers between the albedo and the
            // byte, and every one of them has been accused in turn. In daylight there are none:
            // no fog, no depth curve, no ambient floor, one sun and one bright neutral ambient.
            // Anything still missing at the bottom of this ladder cannot be blamed on the water.
            bool wasDaylight = EnvMode.Daylight;
            EnvMode.Set(true);
            yield return null;

            Debug.Log($"[QCLadder] pass start daylight={EnvMode.Daylight} fog={RenderSettings.fog} " +
                      $"ambSky={RenderSettings.ambientSkyColor} ambEq={RenderSettings.ambientEquatorColor} " +
                      $"ambGnd={RenderSettings.ambientGroundColor} " +
                      $"refAlbedo=sRGB{ReferenceAlbedoSrgb:0.00} " +
                      $"colorSpace={QualitySettings.activeColorSpace} models={AssetIds.Length}");

            int w = Mathf.Clamp(Screen.width, 320, 1920);
            int h = Mathf.Clamp(Screen.height, 240, 1080);
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "QcLadderRT", antiAliasing = 1 };
            var readback = new Texture2D(w, h, TextureFormat.RGB24, false, false);

            foreach (string assetId in AssetIds)
            {
                if (Time.realtimeSinceStartup - t0 > BudgetSeconds)
                {
                    Debug.Log($"[QCLadder] {assetId} budget-exhausted");
                    continue;
                }
                yield return One(cam, rt, readback, dir, manifest, assetId,
                                 mapCentre + Vector3.right * StageOffset);
                Resources.UnloadUnusedAssets();
                yield return null;
            }

            RenderTexture.active = null;
            cam.targetTexture = null;
            rt.Release();
            Object.Destroy(rt);
            Object.Destroy(readback);
            EnvMode.Set(wasDaylight);

            Debug.Log($"[QCLadder] pass done in {Time.realtimeSinceStartup - t0:F1}s");
        }

        private static IEnumerator One(Camera cam, RenderTexture rt, Texture2D readback, string dir,
                                       AssetManifest manifest, string assetId, Vector3 stage)
        {
            string url = manifest.ResolveUrl(assetId);
            string name = assetId.Replace(':', '_');
            if (string.IsNullOrEmpty(url))
            {
                Debug.Log($"[QCLadder] {name} url-unresolved");
                yield break;
            }

            var pivot = new GameObject("QcLadder:" + assetId);
            pivot.transform.position = stage;
            AssetManifest.Module mod = manifest.Get(assetId);
            float scale = mod != null && mod.DefaultScale > 0 ? (float)mod.DefaultScale : 1f;
            pivot.transform.localScale = Vector3.one * scale;

            float loadStart = Time.realtimeSinceStartup;
            Task<GltfImport> task = SceneBuilder.LoadForQc(url, assetId, pivot.transform);
            while (!task.IsCompleted && Time.realtimeSinceStartup - loadStart < PerModelSeconds)
                yield return null;
            GltfImport import = task.Status == TaskStatus.RanToCompletion ? task.Result : null;

            var renderers = new List<Renderer>();
            pivot.GetComponentsInChildren(true, renderers);
            if (import == null || renderers.Count == 0)
            {
                Debug.Log($"[QCLadder] {name} did-not-arrive loaded={import != null} renderers={renderers.Count}");
                Object.Destroy(pivot);
                yield break;
            }

            Bounds b = WorldBounds(renderers);

            // ── the reference quad ───────────────────────────────────────────────
            // Beside the model, facing straight up, one third of the model's size, on the seabed's
            // own material at a known albedo. Facing UP on purpose: it is the same orientation the
            // sand has, so "the model against the reference" is the same comparison the user makes
            // by eye when they say the sand is bright and the ruin is not.
            GameObject reference = null;
            Material refMat = SceneBuilder.OpaqueMaterial();
            if (refMat != null && refMat.shader != null && refMat.shader.isSupported)
            {
                refMat.color = new Color(ReferenceAlbedoSrgb, ReferenceAlbedoSrgb, ReferenceAlbedoSrgb, 1f);
                if (refMat.HasProperty("_Metallic")) refMat.SetFloat("_Metallic", 0f);
                if (refMat.HasProperty("_Glossiness")) refMat.SetFloat("_Glossiness", 1f - GlbShading.ProbeValidatedRoughness);
                if (refMat.HasProperty("_MainTex")) refMat.SetTexture("_MainTex", null);

                reference = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.Destroy(reference.GetComponent<Collider>());
                reference.name = "QcLadderReference";
                reference.GetComponent<MeshRenderer>().sharedMaterial = refMat;
                reference.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // normal = +Y
                reference.transform.localScale = Vector3.one * Mathf.Max(b.extents.magnitude * 0.6f, 1f);
                reference.transform.position = b.center + new Vector3(b.extents.x * 1.9f, 0f, 0f);
            }
            else
            {
                Debug.LogWarning("[QCLadder] no usable reference material — every ratio below is " +
                                 "against nothing and must be read as a raw level, not a ratio");
            }

            // ── the camera ───────────────────────────────────────────────────────
            // Wide enough to hold the model AND the quad, from slightly above, so both are lit and
            // both are in every one of the frames below.
            Bounds framed = b;
            if (reference != null) framed.Encapsulate(reference.GetComponent<Renderer>().bounds);
            Vector3 viewDir = new Vector3(0.35f, 0.45f, 1f).normalized;
            float dist = framed.extents.magnitude * 2.6f + cam.nearClipPlane * 3f;
            cam.transform.position = framed.center + viewDir * dist;
            cam.transform.LookAt(framed.center);

            yield return new WaitForSeconds(SettleSeconds);

            // Where the reference landed on screen, so its pixels can be read without guessing.
            Rect refRect = default;
            bool haveRef = reference != null &&
                           TryViewportRect(cam, reference.GetComponent<Renderer>().bounds, rt.width, rt.height, out refRect);

            Debug.Log($"[QCLadder] {name} staged scale={scale:F2} dist={dist:F1} " +
                      $"bounds={b.size} refRect={(haveRef ? refRect.ToString() : "OFF-SCREEN")} " +
                      $"load={Time.realtimeSinceStartup - loadStart:F1}s url={url}");

            // The empty frame every rung is measured against — model and reference both hidden, so
            // "the subject" is unambiguous and a rung that photographs nothing scores 0% and says so.
            byte[] empty = null;
            pivot.SetActive(false);
            if (reference != null) reference.SetActive(false);
            yield return null;
            yield return Capture(cam, rt, readback, null, x => empty = x);
            pivot.SetActive(true);
            if (reference != null) reference.SetActive(true);
            yield return null;

            // What the ratio SHOULD be if nothing were wrong: the model's own measured albedo over
            // the reference's, both in light. Printed beside every rung so a reader never has to
            // hold the prediction in their head.
            float modelAlbedoLinear = ToneMap.SrgbToLinear(ModelAlbedoSrgb(assetId));
            float refAlbedoLinear = ToneMap.SrgbToLinear(ReferenceAlbedoSrgb);
            double expected = refAlbedoLinear <= 0f ? 0.0 : modelAlbedoLinear / refAlbedoLinear;

            // 🔴 THE MASK IS COMPUTED ONCE, HERE, AND EVERY RUNG BELOW IS MEASURED ON IT.
            //
            // The first version of this ladder let each rung work out its own subject mask from
            // "what differs from the empty frame", and the noAces rung then reported
            // subject=100.00% — switching the tone curve off changes every pixel in the frame, so
            // the mask became the whole picture and the rung's mean was taken over the water and
            // the backdrop as well as the model. Set beside the shipped rung's model-only mean it
            // read as a 6.4× brightening and reached a human before anybody noticed the two numbers
            // described different sets of pixels. Every rung after this one changes the whole frame
            // in some way — the curve, the albedo, the lights, the ambient — so every one of them
            // would have repeated it.
            byte[] shippedFrame = null;
            yield return Capture(cam, rt, readback, Path.Combine(dir, "qc_ladder_" + name + ".png"),
                                 x => shippedFrame = x);
            bool[] mask = QcPixels.SubjectMask(shippedFrame, empty);
            Report(shippedFrame, mask, refRect, haveRef, rt.width, rt.height, name, "shipped", expected);

            yield return Rung(cam, rt, readback, null, mask, refRect, haveRef, name, "noAces", expected,
                              () => AcesToneMapping.Enabled = false);
            AcesToneMapping.Enabled = true;

            yield return AlbedoRung(cam, rt, readback, mask, refRect, haveRef, renderers, name,
                                    "greyAlbedo", expected, (float)QcPixels.MeanAlbedo);
            yield return AlbedoRung(cam, rt, readback, mask, refRect, haveRef, renderers, name,
                                    "whiteAlbedo", expected, 1f);

            yield return ShadowRung(cam, rt, readback, mask, refRect, haveRef, renderers, name, expected);
            yield return SunRung(cam, rt, readback, mask, refRect, haveRef, name, expected);

            // ── WO-E5h: is the ambient reaching these renderers AT ALL? ──────────
            // The arithmetic that makes this the only suspect left: in daylight the dimmest band is
            // 0.61 and the ruins' own albedo is 0.162 linear, so the darkest a surface of theirs can
            // be — sun off entirely — is 0.099 scene-linear, i.e. byte 89. A quarter of the model
            // photographs at byte 0, which needs scene-linear under ToneMap.BlackFloor = 0.00186.
            // That is fifty times less light than the ambient alone would deliver, and the tone
            // curve has been scanned against three.js at 37 points and is exact. So the ambient is
            // not reaching those pixels, and these three rungs are the three ways that happens.
            yield return GiStateRungs(cam, rt, readback, mask, refRect, haveRef, renderers, name, expected);

            Object.Destroy(reference);
            Object.Destroy(pivot);
            yield return null;
            import?.Dispose();
        }

        // ── rungs ────────────────────────────────────────────────────────────────

        private static IEnumerator Rung(Camera cam, RenderTexture rt, Texture2D readback, string png,
                                        bool[] mask, Rect refRect, bool haveRef,
                                        string name, string rung, double expected,
                                        System.Action apply)
        {
            apply?.Invoke();
            yield return null;
            byte[] frame = null;
            yield return Capture(cam, rt, readback, png, x => frame = x);
            Report(frame, mask, refRect, haveRef, rt.width, rt.height, name, rung, expected);
        }

        /// <summary>Replace every base colour with a flat known value — the albedo is gone and only
        /// the light is left.</summary>
        private static IEnumerator AlbedoRung(Camera cam, RenderTexture rt, Texture2D readback,
                                              bool[] mask, Rect refRect, bool haveRef,
                                              List<Renderer> renderers, string name, string rung,
                                              double expected, float albedoSrgb)
        {
            var mats = new List<Material>();
            var savedTex = new List<Texture>();
            var savedCol = new List<Color>();
            foreach (Renderer r in renderers)
            {
                if (r == null) continue;
                foreach (Material m in r.materials)
                {
                    if (m == null) continue;
                    mats.Add(m);
                    string tp = m.HasProperty("baseColorTexture") ? "baseColorTexture"
                              : (m.HasProperty("_MainTex") ? "_MainTex" : null);
                    savedTex.Add(tp != null ? m.GetTexture(tp) : null);
                    if (tp != null) m.SetTexture(tp, null);
                    string cp = m.HasProperty("baseColorFactor") ? "baseColorFactor"
                              : (m.HasProperty("_Color") ? "_Color" : null);
                    savedCol.Add(cp != null ? m.GetColor(cp) : Color.white);
                    if (cp != null) m.SetColor(cp, new Color(albedoSrgb, albedoSrgb, albedoSrgb, 1f));
                }
            }

            byte[] frame = null;
            yield return Capture(cam, rt, readback, null, x => frame = x);
            // The expectation changes with the albedo: this rung is no longer asking about the
            // texture, it is asking whether a KNOWN albedo on this model returns what the same
            // known albedo returns on the quad beside it.
            double exp = ToneMap.SrgbToLinear(albedoSrgb) / ToneMap.SrgbToLinear(ReferenceAlbedoSrgb);
            Report(frame, mask, refRect, haveRef, rt.width, rt.height, name, rung, exp);

            for (int i = 0; i < mats.Count; i++)
            {
                Material m = mats[i];
                string tp = m.HasProperty("baseColorTexture") ? "baseColorTexture"
                          : (m.HasProperty("_MainTex") ? "_MainTex" : null);
                if (tp != null) m.SetTexture(tp, savedTex[i]);
                string cp = m.HasProperty("baseColorFactor") ? "baseColorFactor"
                          : (m.HasProperty("_Color") ? "_Color" : null);
                if (cp != null) m.SetColor(cp, savedCol[i]);
            }
        }

        /// <summary>The sun's shadow off for one frame — restored immediately. The user asked for
        /// shadows to stay, so this is a probe and must never become a change.</summary>
        private static IEnumerator ShadowRung(Camera cam, RenderTexture rt, Texture2D readback,
                                              bool[] mask, Rect refRect, bool haveRef,
                                              List<Renderer> renderers, string name, double expected)
        {
            var lights = new List<Light>();
            var saved = new List<LightShadows>();
            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l == null) continue;
                lights.Add(l); saved.Add(l.shadows); l.shadows = LightShadows.None;
            }
            byte[] frame = null;
            yield return Capture(cam, rt, readback, null, x => frame = x);
            Report(frame, mask, refRect, haveRef, rt.width, rt.height, name, "noShadow", expected);
            for (int i = 0; i < lights.Count; i++)
                if (lights[i] != null) lights[i].shadows = saved[i];
        }

        /// <summary>
        /// 🔴 WO-E5h — the three ways a renderer ends up with no ambient at all, tried one at a
        /// time, plus the state that would have told us without a frame.
        ///
        /// Everything else has been eliminated on measurements: texture, UV gutter, mip drift,
        /// normal map (<c>costOfNormalMap</c> 0.00pp on nine of twelve models), tangents
        /// (<c>tangent-added</c> on a byte-clean pair: no-effect, −0.02pp), encoding
        /// (<c>normalmap-uastc</c>: no-effect), metallic, occlusion, vertex colours, LOD, shadows
        /// (26.75% → 26.42%), fog, depth, geometry, and the tone curve (scanned against three.js at
        /// 37 points, worst relative difference 0.000e+00). What is left is that the light never
        /// arrives, and in built-in RP a dynamic renderer can lose its ambient in exactly these
        /// ways:
        ///
        ///   lightProbeUsage   BlendProbes with probes present but unlit hands the renderer black
        ///                     SH instead of the ambient probe. Off forces the ambient probe.
        ///   lightmapIndex     a renderer whose index is neither −1 nor 65535 takes the LIGHTMAP
        ///                     path and samples a lightmap that does not exist. This is the classic
        ///                     one, it is invisible in the inspector on a runtime-instantiated
        ///                     object, and it produces exactly this symptom: no GI at all, per
        ///                     renderer, regardless of material.
        ///   the ambient itself Trilight ambient is sampled through spherical harmonics, and if the
        ///                     variant that does that has been stripped from the player build there
        ///                     is no ambient for anybody. Flat ambient at a bright neutral is the
        ///                     control: if even THAT does not light the model, nothing reaches it
        ///                     through the SH path and the problem is a shader variant, which this
        ///                     project has shipped broken twice.
        ///
        /// 🔎 The reference quad is the control that makes all of this readable. It is in the same
        /// frame, under the same lights, on the seabed's own material — and it is NOT a glTFast
        /// renderer. Its GI state is logged beside the model's, so "the model's renderers differ
        /// from a renderer that works" is a comparison rather than an assertion.
        /// </summary>
        private static IEnumerator GiStateRungs(Camera cam, RenderTexture rt, Texture2D readback,
                                                bool[] mask, Rect refRect, bool haveRef,
                                                List<Renderer> renderers, string name, double expected)
        {
            // ── what the renderers actually say, before anything is changed ──────
            var reference = GameObject.Find("QcLadderReference");
            Renderer refRenderer = reference != null ? reference.GetComponent<Renderer>() : null;
            LogGiState(name, "reference-quad", refRenderer);
            int shown = 0;
            foreach (Renderer r in renderers)
            {
                if (r == null || shown >= 3) continue;   // three is enough to see a pattern
                LogGiState(name, "model", r);
                shown++;
            }

            // ── rung: force the ambient probe ────────────────────────────────────
            var savedUsage = new List<UnityEngine.Rendering.LightProbeUsage>();
            foreach (Renderer r in renderers)
            {
                savedUsage.Add(r != null ? r.lightProbeUsage : UnityEngine.Rendering.LightProbeUsage.Off);
                if (r != null) r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            }
            yield return Rung(cam, rt, readback, null, mask, refRect, haveRef, name,
                              "lightProbeOff", expected, null);
            for (int i = 0; i < renderers.Count; i++)
                if (renderers[i] != null) renderers[i].lightProbeUsage = savedUsage[i];

            // ── rung: take the lightmap path away ────────────────────────────────
            var savedLightmap = new List<int>();
            foreach (Renderer r in renderers)
            {
                savedLightmap.Add(r != null ? r.lightmapIndex : -1);
                if (r != null) r.lightmapIndex = -1;
            }
            yield return Rung(cam, rt, readback, null, mask, refRect, haveRef, name,
                              "lightmapIndexClear", expected, null);
            for (int i = 0; i < renderers.Count; i++)
                if (renderers[i] != null) renderers[i].lightmapIndex = savedLightmap[i];

            // ── rung: a bright FLAT ambient, no spherical harmonics involved ─────
            // The control for the whole ambient path. Flat ambient reaches a surface through a
            // different route than Trilight's SH, so if the model is still black under a bright
            // flat white the failure is not "which band" but "no ambient at all".
            UnityEngine.Rendering.AmbientMode savedMode = RenderSettings.ambientMode;
            Color savedLight = RenderSettings.ambientLight;
            float savedIntensity = RenderSettings.ambientIntensity;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.8f, 0.8f, 0.8f, 1f);
            RenderSettings.ambientIntensity = 1f;
            yield return Rung(cam, rt, readback, null, mask, refRect, haveRef, name,
                              "flatAmbient0.8", expected, null);
            RenderSettings.ambientMode = savedMode;
            RenderSettings.ambientLight = savedLight;
            RenderSettings.ambientIntensity = savedIntensity;
            yield return null;
        }

        /// <summary>
        /// One line per renderer, always — including when everything looks normal. The rule this
        /// project keeps relearning is that silence is ambiguous between "fine", "skipped" and
        /// "never ran", and a GI field that reads correctly is as much evidence as one that does not.
        /// </summary>
        private static void LogGiState(string model, string which, Renderer r)
        {
            if (r == null)
            {
                Debug.Log($"[QCGi] {model} {which} renderer=MISSING");
                return;
            }
            Debug.Log($"[QCGi] {model} {which} name={r.gameObject.name} " +
                      $"lightProbeUsage={r.lightProbeUsage} " +
                      $"lightmapIndex={r.lightmapIndex} " +
                      $"lightmapScaleOffset={r.lightmapScaleOffset} " +
                      $"realtimeLightmapIndex={r.realtimeLightmapIndex} " +
                      $"reflectionProbeUsage={r.reflectionProbeUsage} " +
                      $"receiveGI={r.receiveGI} " +
                      $"probeAnchor={(r.probeAnchor != null ? r.probeAnchor.name : "(none)")} " +
                      $"isPartOfStaticBatch={r.isPartOfStaticBatch} " +
                      $"receiveShadows={r.receiveShadows} shadowCasting={r.shadowCastingMode} " +
                      $"enabled={r.enabled} shader={(r.sharedMaterial != null && r.sharedMaterial.shader != null ? r.sharedMaterial.shader.name : "(none)")}");
        }

        /// <summary>
        /// 🔴 THE RUNG WITH A PREDICTABLE ANSWER. Every directional light off, so the only thing
        /// left in the scene is the ambient — and in daylight that is a bright neutral 0.53-0.72,
        /// with no depth curve and no fog anywhere near it. A dielectric of albedo 0.162 lit by
        /// that lands at byte 80-100 through the tone curve, on the model and on the quad alike.
        /// If the model comes back at a small fraction of the quad HERE, the loss is in the model's
        /// ambient term and nothing else is left to blame.
        /// </summary>
        private static IEnumerator SunRung(Camera cam, RenderTexture rt, Texture2D readback,
                                           bool[] mask, Rect refRect, bool haveRef,
                                           string name, double expected)
        {
            var lights = new List<Light>();
            var saved = new List<bool>();
            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l == null || l.type != LightType.Directional) continue;
                lights.Add(l); saved.Add(l.enabled); l.enabled = false;
            }
            // 🔎 No re-derived mask here either. Turning the sun off changes the background as
            // well as the model, so a freshly computed mask would swallow the whole frame — which
            // is exactly the mistake the shipped rung's mask exists to prevent.
            byte[] frame = null;
            yield return Capture(cam, rt, readback, null, x => frame = x);
            for (int i = 0; i < lights.Count; i++) if (lights[i] != null) lights[i].enabled = saved[i];
            yield return null;
            Report(frame, mask, refRect, haveRef, rt.width, rt.height, name, "ambientOnly", expected);
        }

        private static void Report(byte[] frame, bool[] mask, Rect refRect, bool haveRef,
                                   int w, int h, string name, string rung, double expected)
        {
            var r = new QcPixels.Rung { Name = rung };
            r.ModelLinear = QcPixels.SceneLinearOfMask(frame, mask,
                                                       out double subject, out double black);
            r.SubjectPercent = subject;
            r.BlackPercent = black;
            r.ReferenceLinear = haveRef
                ? QcPixels.SceneLinearOfRect(frame, w, h,
                                             (int)refRect.x, (int)refRect.y,
                                             (int)refRect.width, (int)refRect.height)
                : 0.0;
            Debug.Log(QcPixels.RungLine(name, r, expected));
        }

        // ── helpers ──────────────────────────────────────────────────────────────

        /// <summary>
        /// The model's own surface-weighted mean base colour, as an authored sRGB value — measured
        /// off the very files the CDN serves and written down here so the ladder can print what the
        /// ratio OUGHT to be beside what it is. Anything not listed falls back to the project's
        /// mid-grey, and the log's <c>expected</c> column then means less; that is why the numbers
        /// are here rather than guessed at.
        /// </summary>
        private static float ModelAlbedoSrgb(string assetId)
        {
            switch (assetId)
            {
                case "ruin:domed_temple":      return 112f / 255f;   // surface mean 112.11
                case "ruin:ancient_byzantine": return 131f / 255f;   // 131.35
                case "ruin:grand_byzantine":   return 130f / 255f;   // 130.39
                case "cc0:kraken":             return 155f / 255f;   // 154.76
                default:                       return (float)QcPixels.MeanAlbedo;
            }
        }

        /// <summary>Where a world-space bounds lands in the readback buffer, as a pixel rect
        /// shrunk to the middle half so no edge pixel of the quad is included.</summary>
        private static bool TryViewportRect(Camera cam, Bounds bounds, int w, int h, out Rect rect)
        {
            rect = default;
            Vector3 c = cam.WorldToViewportPoint(bounds.center);
            if (c.z <= 0f) return false;

            float minX = 1f, minY = 1f, maxX = 0f, maxY = 0f;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? bounds.min.x : bounds.max.x,
                    (i & 2) == 0 ? bounds.min.y : bounds.max.y,
                    (i & 4) == 0 ? bounds.min.z : bounds.max.z);
                Vector3 v = cam.WorldToViewportPoint(corner);
                if (v.z <= 0f) return false;
                minX = Mathf.Min(minX, v.x); maxX = Mathf.Max(maxX, v.x);
                minY = Mathf.Min(minY, v.y); maxY = Mathf.Max(maxY, v.y);
            }
            // Middle half only — the quad's own silhouette must not bring background in.
            float cx = (minX + maxX) * 0.5f, cy = (minY + maxY) * 0.5f;
            float hw = (maxX - minX) * 0.25f, hh = (maxY - minY) * 0.25f;
            int x0 = Mathf.RoundToInt((cx - hw) * w), y0 = Mathf.RoundToInt((cy - hh) * h);
            int rw = Mathf.RoundToInt(hw * 2f * w), rh = Mathf.RoundToInt(hh * 2f * h);
            if (rw < 4 || rh < 4 || x0 < 0 || y0 < 0 || x0 + rw > w || y0 + rh > h) return false;
            rect = new Rect(x0, y0, rw, rh);
            return true;
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
                    Debug.Log("[QCLadder] shot -> " + pngPath);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[QCLadder] cannot write " + pngPath + ": " + e.Message);
                }
            }
            onBytes(readback.GetRawTextureData());
        }
    }
}
