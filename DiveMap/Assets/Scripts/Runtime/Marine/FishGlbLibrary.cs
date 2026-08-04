using System;
using System.Collections.Generic;
using DiveMap.Core;
using GLTFast;
using UnityEngine;

namespace DiveMap.Runtime.Marine
{
    /// <summary>
    /// WO-XR-04.1 — loads ONE real fish GLB per species and hands the school renderer a
    /// static instancing template: mesh + bake matrix + an instancing-safe material with
    /// the model's own baseColor texture.
    ///
    /// This is the same trick the web plays in <c>buildSchool()</c> (builder.html
    /// 1490-1539): take the first mesh out of the GLB, bake its node transform into the
    /// geometry, throw the skinning away and draw N static instances with a vertex/
    /// transform-level wiggle — a school is never skinned-animated. We cannot edit the
    /// vertices here (Draco meshes come in non-readable), so the node transform is kept
    /// as a <see cref="Template.Bake"/> matrix that the renderer right-multiplies onto
    /// every per-instance TRS instead.
    ///
    /// Everything is defensive: a failed download, a stripped shader property, a missing
    /// texture or a mesh-less GLB all end in <c>fishGlb FALLBACK</c> and the school simply
    /// keeps the procedural <see cref="FishMeshFactory"/> mesh. Templates live in an
    /// INACTIVE holder so no template fish is ever visible at the world origin, and the
    /// <see cref="GltfImport"/> instances are held for the lifetime of the scene — disposing
    /// them frees the very meshes/textures we are still drawing.
    /// </summary>
    public sealed class FishGlbLibrary
    {
        public sealed class Template
        {
            public string     Species;
            public string     Url;
            public bool       IsLod1;
            public int        Tris;        // expected, from FishAssetPick's survey table
            public Mesh       Mesh;
            public Material   Mat;
            /// <summary>GLB node→root transform, recentred; right-multiply onto the instance TRS.</summary>
            public Matrix4x4  Bake;
            /// <summary>Nose→tail length AFTER <see cref="Bake"/> — the renderer divides by this.</summary>
            public float      BakedLen;
        }

        private readonly List<Template>  _ready   = new List<Template>();
        private readonly List<GltfImport> _keepAlive = new List<GltfImport>();

        /// <summary>Templates finished so far (safe to apply immediately).</summary>
        public IReadOnlyList<Template> Ready => _ready;

        /// <summary>
        /// Set by <c>SceneBuilder</c> once the school system exists; called for every
        /// template that lands afterwards so a slow GLB still hot-swaps in mid-scene.
        /// </summary>
        public Action<Template> OnReady { get; set; }

        /// <summary>
        /// Kick off one template load (fire-and-forget). <paramref name="loadState"/> is
        /// incremented/decremented so <c>SceneBuilder</c>'s existing "wait for GLBs" loop
        /// covers fish templates too — that is what makes the QC screenshot show textured
        /// fish instead of catching them mid-swap. Never touches the loaded/failed item
        /// counters: these are extra assets, not map items.
        /// </summary>
        public void BeginLoad(FishAssetPick.Pick pick, Transform holderParent, SceneLoadState loadState)
        {
            loadState?.BeginLoad();
            LoadAsync(pick, holderParent, loadState);
        }

        private async void LoadAsync(FishAssetPick.Pick pick, Transform holderParent, SceneLoadState loadState)
        {
            Template t = null;
            try
            {
                t = await BuildTemplate(pick, holderParent);
            }
            catch (Exception e)
            {
                Debug.Log($"[Marine] fishGlb FALLBACK species={pick.Species} reason=exception:{e.Message}");
                t = null;
            }

            if (t != null)
            {
                _ready.Add(t);
                try { OnReady?.Invoke(t); }
                catch (Exception e) { Debug.LogWarning($"[Marine] fishGlb apply failed species={t.Species}: {e.Message}"); }
            }

            loadState?.CompleteLoad();
        }

        private async System.Threading.Tasks.Task<Template> BuildTemplate(
            FishAssetPick.Pick pick, Transform holderParent)
        {
            string species = pick.Species;

            // SceneBuilder.NewImport, not `new GltfImport()`: the schooling fish load through this
            // path and nothing else, and the normal-map add-on has to be registered before the
            // constructor runs to reach them at all.
            GltfImport gltf = SceneBuilder.NewImport();
            bool ok = await gltf.Load(pick.Url);
            if (!ok)
            {
                Debug.Log($"[Marine] fishGlb FALLBACK species={species} url={pick.Url} reason=load-failed");
                gltf.Dispose();
                return null;
            }

            // Inactive holder FIRST: an active template would pop up at the origin for a
            // frame (and let any bundled AnimationClip start playing) before we hide it.
            var holder = new GameObject("FishTemplate_" + species);
            holder.SetActive(false);
            if (holderParent != null) holder.transform.SetParent(holderParent, false);

            ok = await gltf.InstantiateMainSceneAsync(holder.transform);
            if (!ok)
            {
                Debug.Log($"[Marine] fishGlb FALLBACK species={species} url={pick.Url} reason=instantiate-failed");
                gltf.Dispose();
                UnityEngine.Object.Destroy(holder);
                return null;
            }
            _keepAlive.Add(gltf); // meshes/textures we draw from — never Dispose

            // First renderer wins (these XR fish GLBs are 1 mesh / 1 primitive). Skinned
            // first: the marine models are authored as a single skinned fish.
            Mesh mesh = null;
            Material srcMat = null;
            Transform node = null;

            var smr = holder.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < smr.Length && mesh == null; i++)
            {
                if (smr[i] == null || smr[i].sharedMesh == null) continue;
                mesh = smr[i].sharedMesh; srcMat = smr[i].sharedMaterial; node = smr[i].transform;
            }
            if (mesh == null)
            {
                var mr = holder.GetComponentsInChildren<MeshRenderer>(true);
                for (int i = 0; i < mr.Length && mesh == null; i++)
                {
                    if (mr[i] == null) continue;
                    var mf = mr[i].GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    mesh = mf.sharedMesh; srcMat = mr[i].sharedMaterial; node = mr[i].transform;
                }
            }
            if (mesh == null)
            {
                Debug.Log($"[Marine] fishGlb FALLBACK species={species} url={pick.Url} reason=no-mesh");
                return null;
            }

            // Bake = the mesh node's transform relative to the GLB root, exactly the web's
            // geo.applyMatrix4(o.matrixWorld) (builder.html:1503) — without it a fish authored
            // inside a scaled/rotated node draws at the wrong size or lies on its side.
            Matrix4x4 bake = holder.transform.worldToLocalMatrix * node.localToWorldMatrix;

            // Measure the baked AABB from mesh.bounds corners. mesh.vertices is off-limits:
            // a Draco mesh arrives non-readable and reading it throws.
            Bounds mb = mesh.bounds;
            Vector3 c = mb.center, e = mb.extents;
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                Vector3 p = bake.MultiplyPoint3x4(c + new Vector3(sx * e.x, sy * e.y, sz * e.z));
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            Vector3 size = max - min;
            Vector3 centre = (min + max) * 0.5f;

            // Recentre on the pivot so a fish yaws about its own middle: the boids matrix
            // rotates about the instance origin, and an off-centre template would swing the
            // whole body around a point outside itself (visible as a wobbling shoal).
            bake = Matrix4x4.Translate(-centre) * bake;

            float bakedLen = Mathf.Max(size.z, Mathf.Max(size.x, size.y));
            if (bakedLen < 1e-4f)
            {
                Debug.Log($"[Marine] fishGlb FALLBACK species={species} url={pick.Url} reason=degenerate-bounds");
                return null;
            }

            // Base colour texture. Property names differ per glTFast shader variant, so try
            // the documented ones and, failing that, scan the shader's own texture slots.
            Texture tex = FindBaseColor(srcMat, out string texVia);

            Material mat = MakeInstancingMaterial(tex);
            if (mat == null)
            {
                Debug.Log($"[Marine] fishGlb FALLBACK species={species} url={pick.Url} reason=no-material");
                return null;
            }

            // A textured fish must NOT also be tinted by SpeciesColor — that is the
            // procedural mesh's job. White albedo × texture = the model's own colours.
            bool texOk = tex != null;
            if (!texOk)
            {
                // Still worth instancing: the real silhouette beats the 10-tri lozenge, and
                // the GLB's vertex colours/lighting read fine. Log it as a partial.
                Debug.Log($"[Marine] fishGlb species={species} tex=MISSING (drawing untextured GLB mesh)");
            }

            var t = new Template
            {
                Species  = species,
                Url      = pick.Url,
                IsLod1   = pick.IsLod1,
                Tris     = pick.Tris,
                Mesh     = mesh,
                Mat      = mat,
                Bake     = bake,
                BakedLen = bakedLen,
            };

            Debug.Log($"[Marine] fishGlb species={species} url={pick.Url} lod1={pick.IsLod1} " +
                      $"tris={pick.Tris} tex={(texOk ? "OK" : "MISSING")} via={texVia} " +
                      $"baseLen={bakedLen:F3} expected={pick.LocalLen:F3} " +
                      $"size=({size.x:F2},{size.y:F2},{size.z:F2}) submeshes={mesh.subMeshCount}");

            return t;
        }

        /// <summary>
        /// The GLB's baseColor map: <c>mainTexture</c> → <c>baseColorTexture</c> →
        /// <c>_MainTex</c> → first texture slot on the shader whose name looks like a base
        /// colour. HasProperty-guarded throughout: asking a glTFast shader variant for a
        /// property it never declared logs an error per call.
        /// </summary>
        private static Texture FindBaseColor(Material m, out string via)
        {
            via = "none";
            if (m == null) return null;

            if (m.HasProperty("_MainTex"))
            {
                Texture t = m.GetTexture("_MainTex");
                if (t != null) { via = "_MainTex"; return t; }
            }
            if (m.HasProperty("baseColorTexture"))
            {
                Texture t = m.GetTexture("baseColorTexture");
                if (t != null) { via = "baseColorTexture"; return t; }
            }

            Shader sh = m.shader;
            if (sh == null) return null;
            int n = sh.GetPropertyCount();
            for (int i = 0; i < n; i++)
            {
                if (sh.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Texture) continue;
                string name = sh.GetPropertyName(i);
                string low = name.ToLowerInvariant();
                if (!low.Contains("base") && !low.Contains("albedo") && !low.Contains("main")) continue;
                if (low.Contains("normal") || low.Contains("occl") || low.Contains("emis")) continue;
                Texture t = m.GetTexture(name);
                if (t != null) { via = name; return t; }
            }
            return null;
        }

        /// <summary>
        /// Instancing-safe material: a clone of the proven DM_Standard (in Resources, so it
        /// survives shader stripping — a runtime-found shader is the classic magenta build)
        /// carrying the GLB texture. glTFast's own material is NOT reused: its shader variants
        /// are not compiled with instancing and would draw every fish at the world origin.
        /// No new shader keyword is enabled (_EMISSION/_DETAIL_* = magenta on device).
        /// </summary>
        private static Material MakeInstancingMaterial(Texture tex)
        {
            // 🐟 DM_FishWave first: same Standard lighting, plus a travelling bend down the body.
            // It lives in Resources for exactly the reason DM_Standard does — a shader only
            // referenced from code is stripped from the build and comes back magenta on the
            // device. If it is missing for any reason we fall through to the flat material and the
            // fish simply do not bend, which is today's behaviour rather than a broken one.
            Material src = Resources.Load<Material>("DM_FishWave");
            if (src == null || src.shader == null)
            {
                Debug.LogWarning("[Marine] ไม่พบ DM_FishWave — ปลาจะไม่โค้งตัว ใช้ DM_Standard แทน");
                src = Resources.Load<Material>("DM_Standard");
            }
            Material mat = src != null ? new Material(src) : new Material(Shader.Find("Standard"));
            if (mat == null || mat.shader == null) return null;

            mat.color = Color.white;             // texture supplies the colour
            if (tex != null && mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.1f); // matte, per QC r8
            if (mat.HasProperty("_Metallic"))   mat.SetFloat("_Metallic", 0f);
            // Turning this on at RUNTIME is not enough on a real GPU. The INSTANCING_ON shader
            // variant only survives the build if some material ASSET has instancing ticked, and
            // none did — so on the phone the schools drew nothing at all while CI, which falls
            // back to one draw per fish on software GL, saw a full reef and reported success.
            // Same family of trap as the stripped transparent variant and the stripped fog:
            // the fix lives in Resources/*.mat (m_EnableInstancingVariants: 1), not here.
            mat.enableInstancing = true;
            return mat;
        }
    }
}
