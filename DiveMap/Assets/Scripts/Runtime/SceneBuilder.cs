using System;
using System.Collections;
using System.Collections.Generic;
using GLTFast;
using UnityEngine;
using DiveMap.Core;
using DiveMap.Runtime.Marine;

namespace DiveMap.Runtime
{
    /// <summary>
    /// Turns a <see cref="SceneData"/> into a live 3D map: a "Map" root with one
    /// child GameObject per item (WebCoord-converted transform + glTFast GLB, or a
    /// coloured placeholder on failure), plus a sculpted polar-grid seabed and a
    /// transparent water disc.
    ///
    /// Per-item failures MUST NOT abort the scene (WO-XR-01): production GLBs use
    /// WebP textures which glTFast may reject — we catch and spawn a placeholder,
    /// counting loaded vs failed. Async loads are tracked through <see cref="SceneLoadState"/>
    /// so nothing claims "done" while a GLB is still in flight (the 108→31 save-guard lesson).
    /// </summary>
    public sealed class SceneBuilder : MonoBehaviour
    {
        public struct BuildResult
        {
            public GameObject Root;
            public int Loaded;
            public int Failed;
            public Vector3 Center;
            public float Radius;
            public string MapName;

            // Framing box (decor/structure only, swimmers excluded) — fed to OrbitCamera.FrameBox
            // so the opening shot centres on the real content (e.g. the wreck), not the whole
            // seabed + water column.
            public Vector3 FrameCenter;
            public float FrameSizeX;
            public float FrameSizeY;
            public float FrameSizeZ;
            public float FrameMinY;

            /// <summary>env.waterLevel of this map — the surface the sun shafts start from.</summary>
            public float WaterLevel;

            /// <summary>Solid decor as world AABBs — the fish steer around these and, from P1.1,
            /// so does the drone.</summary>
            public List<ObstacleBox> Obstacles;

            /// <summary>Seabed footprint stretch (areaScale × areaScaleX/Z) — the tour needs it
            /// to keep the drone inside the map.</summary>
            public float SeabedScaleX;
            public float SeabedScaleZ;

            /// <summary>Big animals (msh:*) and their asset ids — P1.2b plays their calls when
            /// the diver comes near.</summary>
            public List<Transform> Animals;
            public List<string> AnimalIds;

            /// <summary>School anchor points — the tour minimap plots them.</summary>
            public List<Vector3> SchoolAnchors;
        }

        // Kinds that swim (drift through the water column) — excluded from the opening-shot
        // framing box and NOT grounded to the seabed (they float at their stored Y).
        private static bool IsSwimmer(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return false;
            switch (kind.ToUpperInvariant())
            {
                case "MARINE_LIFE":
                case "SCHOOL":
                case "FISH":
                case "TURTLE":
                    return true;
                default:
                    return false;
            }
        }

        // ── Marine routing (WO-XR-03) ─────────────────────────────────────────────
        // school:* and pod:* → instanced boid swarm; msh:* → looping big animal.
        private static bool IsSchoolItem(string assetId)
            => !string.IsNullOrEmpty(assetId) &&
               (assetId.StartsWith("school:", StringComparison.OrdinalIgnoreCase) ||
                assetId.StartsWith("pod:", StringComparison.OrdinalIgnoreCase));

        private static bool IsBigAnimalItem(string assetId)
            => !string.IsNullOrEmpty(assetId) &&
               assetId.StartsWith("msh:", StringComparison.OrdinalIgnoreCase);

        // Mid silver-grey albedo (QC r7). The old near-white (0.86,0.92,0.80) drew each fish as
        // a blazing-white kite with a pitch-black shadow side (max lit/shadow contrast). A mid
        // silver-grey — a real fish's countershaded silver — drops the lit side so the shadow
        // side (Trilight ambient + reflection-cube sheen) is a natural dim grey, not black, and
        // the flock reads as a silver cloud. Still no runtime _EMISSION keyword (build strips it → magenta).
        private static Color SpeciesColor(string assetId)
        {
            string a = assetId.ToLowerInvariant();
            if (a.StartsWith("school:scad")) return new Color(0.58f, 0.64f, 0.60f);       // silver-olive (yellowstripe scad)
            if (a.StartsWith("school:barracuda")) return new Color(0.56f, 0.60f, 0.64f);  // cool steel-silver
            if (a.StartsWith("pod:yellowtail")) return new Color(0.66f, 0.62f, 0.42f);    // muted silver-gold
            if (a.StartsWith("pod:")) return new Color(0.56f, 0.62f, 0.66f);
            return new Color(0.55f, 0.62f, 0.66f); // generic school silver-grey
        }

        /// <summary>
        /// Resolve a placed school/pod item into a runtime registration. ALL geometry comes
        /// from <see cref="MarineMath.SchoolGeometryFor"/> — the verified builder.html
        /// formulas (shoal SR, cluster R, pod golden-disc podR) multiplied by the saved
        /// item.s — so nothing about the school's span is invented here. Public + static so
        /// the EditMode tests pin the real production numbers, not a copy of them.
        /// </summary>
        public static FishSchoolSystem.SchoolReg MakeSchoolReg(string assetId, Vector3 anchor, float itemScale)
        {
            MarineMath.SchoolGeometry g = MarineMath.SchoolGeometryFor(assetId, itemScale);
            return new FishSchoolSystem.SchoolReg
            {
                Anchor          = anchor,
                FishWorldLen    = (float)g.FishWorldLen,
                FormRadiusWorld = (float)g.RadiusWorld,
                VertHalfWorld   = (float)g.VertHalfWorld,
                SpeedCap        = (float)g.SpeedCap,
                SizeMin         = (float)g.Spec.SizeMin,
                SizeMax         = (float)g.Spec.SizeMax,
                IsPod           = g.IsPod,
                Count           = g.Spec.UnityCount,
                Color           = SpeciesColor(assetId),
                Species         = assetId,
            };
        }

        /// <summary>Fish-per-school multiplier in the lite graphics mode (P0).</summary>
        private const float LiteFishMul = 0.5f;

        // Preliminary seabed sizing (WO-XR-01). areaScale multiplies this base radius.
        private const float BaseSeabedRadius = 15f;
        private const float PerItemLoadTimeout = 25f;   // per GLB, soft
        private const float OverallLoadTimeout = 120f;  // whole scene, hard safety

        private readonly SceneLoadState _loadState = new SceneLoadState();
        // WO-XR-04.1: real fish GLB templates. Held in a FIELD, not a local, so the
        // GltfImport instances behind the meshes/textures we instance every frame stay
        // referenced for the lifetime of the scene (a collected/disposed import would pull
        // the mesh out from under RenderMeshInstanced).
        private FishGlbLibrary _fishGlb;
        private int _loaded;
        private int _failed;
        // env.waterLevel of the map being built (Htms Chang = 240) — the schools need it for
        // the surface ceiling (builder.html capY = waterLevel − 8 per fish size).
        private float _waterLevel = 4f;
        // Seabed footprint stretch of the map being built (P1.1: the tour clamps the drone to it).
        private float _seabedScaleX = 1f;
        private float _seabedScaleZ = 1f;

        private static readonly Dictionary<string, Color> KindColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
        {
            { "ROCK",       new Color(0.55f, 0.55f, 0.58f) },
            { "CORAL",      new Color(0.95f, 0.45f, 0.55f) },
            { "ANEMONE",    new Color(0.85f, 0.35f, 0.75f) },
            { "FISH",       new Color(0.95f, 0.80f, 0.25f) },
            { "SCHOOL",     new Color(0.30f, 0.70f, 0.95f) },
            { "TURTLE",     new Color(0.35f, 0.65f, 0.40f) },
            { "WRECK",      new Color(0.45f, 0.40f, 0.35f) },
            { "ARTIFICIAL", new Color(0.60f, 0.60f, 0.70f) },
            { "BOAT",       new Color(0.70f, 0.35f, 0.25f) },
            { "SPECIAL",    new Color(0.90f, 0.75f, 0.30f) },
        };

        /// <summary>
        /// Coroutine that builds the whole scene and reports a <see cref="BuildResult"/>.
        /// </summary>
        /// <summary>
        /// The root of the build currently running, so a caller that cancels one can clean up
        /// after it. Null once the build has handed its result over.
        /// </summary>
        public GameObject InFlightRoot { get; private set; }

        /// <summary>Destroy a build that was abandoned part-way. Safe to call at any time.</summary>
        public void DiscardInFlight()
        {
            if (InFlightRoot == null) return;
            Debug.Log("[Scene] discarding a cancelled build's root — it would have kept drawing");
            Destroy(InFlightRoot);
            InFlightRoot = null;
        }

        public IEnumerator BuildRoutine(SceneData scene, AssetManifest manifest, Action<BuildResult> onDone)
        {
            _loaded = 0;
            _failed = 0;
            _loadState.Reset();

            var root = new GameObject("Map");
            // Publish it immediately. A build is a long async job (every GLB is a download), and
            // the caller is allowed to cancel one — StopCoroutine simply stops running the method,
            // so without this the half-built root is orphaned in the scene FOREVER, drawing a
            // second seabed and a second wreck behind the real ones. That is what AR uncovered:
            // "underwaterHidden=4/4" was true of the live map while a ghost map kept painting.
            InFlightRoot = root;

            SceneEnv env = scene?.Env;

            // ── Environment first (seabed + water) so items sit above it ──────────
            float seabedRadius = BuildSeabedAndWater(root.transform, scene, env, out Bounds bounds);

            // ── Items ─────────────────────────────────────────────────────────────
            IReadOnlyList<SceneItem> items = scene?.Items() ?? new List<SceneItem>();
            var decorGos = new List<GameObject>();   // structure/scenery (for framing box)
            var allGos = new List<GameObject>();      // fallback when a map is swimmers-only

            // WO-XR-03: SCHOOL/pod items become live instanced boid swarms and msh:* big
            // animals become looping swimmers, instead of a single static GLB blob. We
            // collect their registrations here and spin up one FishSchoolSystem after the
            // static loads settle (so obstacle bounds — the wreck — are known).
            var schoolRegs = new List<FishSchoolSystem.SchoolReg>();
            var animals = new List<Transform>();
            var animalIds = new List<string>();
            int whaleCount = 0;

            // P0: the "โหมดกราฟิกประหยัด" setting only scaled the render resolution — the reef
            // kept all 1,100 fish, which is the expensive half on a weak phone. Halve the swarm
            // instead (never below 8 per school, or a "school" stops reading as one). Applied
            // HERE, not in MakeSchoolReg, so the web formulas and their tests stay untouched;
            // the QC run is always high-gfx, so 1,100 remains the oracle.
            bool liteGfx = SettingsStore.IsLite(SettingsStore.Gfx);

            foreach (SceneItem item in items)
            {
                var itemGo = new GameObject(ItemName(item));
                itemGo.transform.SetParent(root.transform, false);
                ApplyTransform(itemGo.transform, item, manifest);

                bounds.Encapsulate(itemGo.transform.localPosition);

                string aid = item.AssetId ?? "";
                if (IsSchoolItem(aid))
                {
                    FishSchoolSystem.SchoolReg reg =
                        MakeSchoolReg(aid, itemGo.transform.position, itemGo.transform.localScale.x);
                    if (liteGfx) reg.Count = Mathf.Max(8, Mathf.RoundToInt(reg.Count * LiteFishMul));
                    schoolRegs.Add(reg);
                    _loaded++;
                    continue; // no static GLB — the swarm renders itself
                }
                if (IsBigAnimalItem(aid))
                {
                    // WO-XR-03b: a big animal is a REAL GLB (Whale_Shark_xr0.glb, KTX2+Draco)
                    // driven by WhaleController — no more procedural "paper kite" mesh, and no
                    // [8..16] size clamp: the saved item.s is authoritative (1.908×34.2 ≈ 65 u).
                    // Swimmers are NEVER grounded — they float at their stored Y.
                    whaleCount++;
                    animals.Add(itemGo.transform);
                    animalIds.Add(aid);
                    string wurl = manifest != null ? manifest.ResolveUrl(aid) : null;
                    AssetManifest.Module wmod = manifest != null ? manifest.Get(aid) : null;
                    if (string.IsNullOrEmpty(wurl))
                    {
                        Debug.LogWarning($"[Marine] whale asset={aid} has no GLB url — placeholder");
                        SpawnPlaceholder(itemGo.transform, item, wmod);
                        AttachWhale(itemGo.transform, aid, null, false, 0f);
                        _failed++;
                    }
                    else
                    {
                        _loadState.BeginLoad();
                        LoadBigAnimalAsync(wurl, itemGo.transform, item, wmod, aid);
                    }
                    continue;
                }

                string url = manifest != null ? manifest.ResolveUrl(item.AssetId) : null;
                AssetManifest.Module module = manifest != null ? manifest.Get(item.AssetId) : null;
                bool swimmer = IsSwimmer(module?.Kind);

                allGos.Add(itemGo);
                if (!swimmer) decorGos.Add(itemGo);

                if (aid.StartsWith("warp:", StringComparison.OrdinalIgnoreCase))
                {
                    // E8: a portal, not a model — the web builds its gate from primitives too
                    // (builder.html:2525) and it is not in the asset manifest.
                    WarpGate.Attach(itemGo.transform);
                    _loaded++;
                    continue;
                }

                if (string.IsNullOrEmpty(url))
                {
                    // Unknown assetId with no gate to build → placeholder.
                    SpawnPlaceholder(itemGo.transform, item, module);
                    _failed++;
                }
                else
                {
                    _loadState.BeginLoad();
                    // Static scenery (wreck/rock/coral/statue…) is grounded so its base
                    // sits on the seabed — matching the web's bakeStatic() recenter+drop.
                    LoadItemAsync(url, itemGo.transform, item, module, ground: !swimmer);
                }
            }

            // ── Fish GLB templates (WO-XR-04.1) ───────────────────────────────────
            // One real GLB per school/pod species (Scad_School_xr0 / Barracuda_School_xr0 /
            // Trevally_xr1), started HERE — before the wait below — so the templates are in
            // hand by the time the schools draw their first frame and the QC eye never
            // catches a shoal of procedural lozenges. Fish templates are extra assets, so
            // they move _loadState only: the "โหลดแล้ว N" item oracle must not budge.
            if (liteGfx)
            {
                int total = 0;
                for (int i = 0; i < schoolRegs.Count; i++) total += schoolRegs[i].Count;
                Debug.Log($"[Marine] gfx=lite fishMul={LiteFishMul:F2} fish={total} " +
                          $"(applies on map load — changing the setting takes effect next map)");
            }

            _fishGlb = new FishGlbLibrary();
            if (schoolRegs.Count > 0)
            {
                var templates = new GameObject("FishTemplates");
                templates.transform.SetParent(root.transform, false);
                templates.SetActive(false);   // a template fish must never render

                // LOD choice is per SPECIES but driven by the biggest school of it.
                var maxCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (FishSchoolSystem.SchoolReg reg in schoolRegs)
                {
                    if (string.IsNullOrEmpty(reg.Species)) continue;
                    maxCount.TryGetValue(reg.Species, out int have);
                    maxCount[reg.Species] = Mathf.Max(have, reg.Count);
                }

                foreach (KeyValuePair<string, int> kv in maxCount)
                {
                    string lod0 = manifest != null ? manifest.ResolveUrl(kv.Key) : null;
                    string lod1 = manifest != null ? manifest.ResolveLod1Url(kv.Key) : null;
                    if (FishAssetPick.TryPick(kv.Key, lod0, lod1, kv.Value, out FishAssetPick.Pick pick))
                        _fishGlb.BeginLoad(pick, templates.transform, _loadState);
                    else
                        Debug.Log($"[Marine] fishGlb FALLBACK species={kv.Key} " +
                                  $"reason=not-surveyed (keeping procedural mesh)");
                }
            }

            // Wait for all in-flight GLB loads, with a hard safety timeout.
            float t = 0f;
            while (_loadState.PendingCount > 0 && t < OverallLoadTimeout)
            {
                t += Time.deltaTime;
                yield return null;
            }

            // ── Marine system (WO-XR-03) ──────────────────────────────────────────
            // Obstacles = the loaded static decor (the wreck) as world-space AABBs so
            // fish steer around them (warp gates excluded — not solid). Built AFTER the
            // GLB loads settle so renderer bounds are real.
            // Built once and shared: the fish steer around these, the drone collides with them
            // (P1.1), and the info card will pick against them later.
            var obstacles = new List<ObstacleBox>();
            foreach (GameObject go in decorGos)
            {
                if (go == null) continue;
                if (go.name.Contains("_warp:")) continue; // portals are not solid
                var one = new List<GameObject> { go };
                if (TryContentBounds(one, out Bounds ob))
                    obstacles.Add(new ObstacleBox { Min = ob.min, Max = ob.max, ObsR = 4f });
            }

            if (schoolRegs.Count > 0 || whaleCount > 0)
            {
                var marine = root.AddComponent<FishSchoolSystem>();
                marine.Configure(schoolRegs, obstacles, Camera.main, BaseMat(false), _waterLevel, whaleCount);

                // Swap in the real fish (WO-XR-04.1): whatever is already loaded now, plus
                // anything that lands later — a school that never gets a template keeps the
                // procedural mesh, so the reef is never empty.
                foreach (FishGlbLibrary.Template tpl in _fishGlb.Ready)
                    marine.ApplyGlbTemplate(tpl.Species, tpl.Mesh, tpl.Mat, tpl.Bake, tpl.BakedLen);

                FishSchoolSystem marineRef = marine;
                _fishGlb.OnReady = tpl =>
                {
                    if (marineRef != null)
                        marineRef.ApplyGlbTemplate(tpl.Species, tpl.Mesh, tpl.Mat, tpl.Bake, tpl.BakedLen);
                };
            }

            Vector3 center = bounds.center;
            float radius = Mathf.Max(bounds.extents.magnitude, seabedRadius, 1f);

            // Framing box: prefer real rendered bounds of the scenery; fall back to all
            // items, then to the seabed disc. This is what the camera actually frames.
            Bounds frameBox;
            if (!TryContentBounds(decorGos, out frameBox) && !TryContentBounds(allGos, out frameBox))
            {
                frameBox = new Bounds(new Vector3(0f, 0f, 0f), new Vector3(seabedRadius * 2f, 2f, seabedRadius * 2f));
            }

            // G1 — the pins a diver left here. The scene JSON has carried them all along and
            // nothing drew them, so an annotated map looked exactly like a bare one.
            PinMarker.BuildAll(scene, root.transform);

            // Handed over — it is the caller's now, so cancelling a LATER build must not destroy it.
            InFlightRoot = null;

            onDone?.Invoke(new BuildResult
            {
                Root = root,
                Loaded = _loaded,
                Failed = _failed,
                Center = center,
                Radius = radius,
                MapName = scene?.Name,
                FrameCenter = frameBox.center,
                FrameSizeX = frameBox.size.x,
                FrameSizeY = frameBox.size.y,
                FrameSizeZ = frameBox.size.z,
                FrameMinY = frameBox.min.y,
                WaterLevel = _waterLevel,
                Obstacles = obstacles,
                SeabedScaleX = _seabedScaleX,
                SeabedScaleZ = _seabedScaleZ,
                Animals = animals,
                AnimalIds = animalIds,
                SchoolAnchors = schoolRegs.ConvertAll(r => r.Anchor),
            });
        }

        // ── Item transform ────────────────────────────────────────────────────────

        private static void ApplyTransform(Transform tr, SceneItem item, AssetManifest manifest)
        {
            double[] p = item.P;
            double[] r = item.R;
            double[] s = item.S;

            if (p != null && p.Length >= 3)
            {
                Vec3 up = WebCoord.PositionToUnity(new Vec3(p[0], p[1], p[2]));
                tr.localPosition = new Vector3((float)up.X, (float)up.Y, (float)up.Z);
            }

            if (r != null && r.Length >= 3)
            {
                Quat q = WebCoord.RotationToUnity(new Vec3(r[0], r[1], r[2]));
                tr.localRotation = new Quaternion((float)q.X, (float)q.Y, (float)q.Z, (float)q.W);
            }

            if (s != null && s.Length >= 3)
            {
                tr.localScale = new Vector3((float)s[0], (float)s[1], (float)s[2]);
            }
            else
            {
                float ds = manifest?.Get(item.AssetId) is AssetManifest.Module m ? (float)m.DefaultScale : 1f;
                tr.localScale = Vector3.one * (ds <= 0 ? 1f : ds);
            }
        }

        // ── Async GLB load (fire-and-forget, tracked via SceneLoadState) ───────────

        /// <summary>
        /// J7 — what to actually hand glTFast: the on-device copy when there is one, otherwise the
        /// network, fetching the bytes ourselves on the way so the next dive does not need a signal.
        ///
        /// The download happens here rather than letting glTFast do it because there is no way to
        /// ask glTFast for the bytes it fetched; the alternative is downloading every model twice,
        /// which on a boat's mobile data is not a detail.
        ///
        /// Every failure path ends at the original URL, so the worst case is exactly the behaviour
        /// before this existed: glTFast tries, fails, and the item becomes a placeholder.
        /// </summary>
        private static async System.Threading.Tasks.Task<string> CachedUri(string url)
        {
            if (!DiveMap.Core.AssetCache.IsCacheable(url)) return url;

            string local = AssetCacheStore.Resolve(url);
            if (!ReferenceEquals(local, url) && local != url) return local;   // already on the device

            byte[] data = await AssetCacheStore.Download(url);
            if (data == null) return url;                                     // offline → let it fail as before
            return AssetCacheStore.Store(url, data) ? AssetCacheStore.FileUri(url) : url;
        }


        private async void LoadItemAsync(string url, Transform parent, SceneItem item, AssetManifest.Module module, bool ground)
        {
            bool ok = false;
            try
            {
                var gltf = new GltfImport();
                ok = await gltf.Load(await CachedUri(url));
                if (ok)
                {
                    // Instantiate the main scene as children of the per-item GameObject.
                    ok = await gltf.InstantiateMainSceneAsync(parent);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SceneBuilder] GLB load failed for {item?.AssetId} ({url}): {e.Message}");
                ok = false;
            }

            if (ok)
            {
                // Match the web builder's bakeStatic(): recentre the model on X/Z and drop
                // its base to the pivot (localPosition Y=0) so it rests ON the seabed instead
                // of the GLB's own (often centred) pivot half-sinking into the sand.
                if (ground && parent != null) GroundToBase(parent);

                // B8 — the hero statues: gold glow, and the beard/robe drifting in the current.
                // Applied AFTER the model exists, because both walk its real renderers/bounds.
                if (parent != null)
                {
                    string fxId = item != null ? (item.AssetId ?? "") : "";
                    if (GoldFx.IsGolden(fxId)) GoldFx.ApplyGold(parent.gameObject);
                    if (GoldFx.HasBeard(fxId)) GoldFx.ApplyBeard(parent.gameObject);
                }
                _loaded++;
            }
            else
            {
                // parent may still be alive; guard against scene teardown.
                if (parent != null) SpawnPlaceholder(parent, item, module);
                _failed++;
            }

            _loadState.CompleteLoad();
        }

        // ── Big animal (whaleshark) = real GLB + WhaleController (WO-XR-03b) ─────────

        /// <summary>
        /// Load a <c>msh:*</c> big animal's GLB under its item pivot, centre it on the pivot
        /// (a swimmer is NOT grounded — it floats at its stored Y), then attach the looping
        /// <see cref="WhaleController"/>. A failed load falls back to a placeholder + the
        /// controller so the scene never aborts (the KTX2/Draco risk).
        /// </summary>
        private async void LoadBigAnimalAsync(string url, Transform parent, SceneItem item,
                                              AssetManifest.Module module, string assetId)
        {
            bool ok = false;
            try
            {
                var gltf = new GltfImport();
                ok = await gltf.Load(await CachedUri(url));
                if (ok) ok = await gltf.InstantiateMainSceneAsync(parent);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SceneBuilder] whale GLB load failed for {assetId} ({url}): {e.Message}");
                ok = false;
            }

            float worldLen = 0f;
            if (ok && parent != null)
            {
                CenterToPivot(parent);
                if (TryLocalBounds(parent, out Bounds lb))
                {
                    // lb is in the pivot's LOCAL space, so its max dimension IS the GLB's own
                    // maxd (Whale_Shark_xr0 = 1.908); × item.s gives the true world length.
                    float localMax = Mathf.Max(lb.size.x, Mathf.Max(lb.size.y, lb.size.z));
                    float itemScale = Mathf.Max(0.01f, parent.localScale.x);
                    worldLen = (float)MarineMath.WhaleWorldLen(localMax, itemScale);
                }
                _loaded++;
            }
            else
            {
                if (parent != null) SpawnPlaceholder(parent, item, module);
                _failed++;
            }

            AttachWhale(parent, assetId, url, ok, worldLen);
            _loadState.CompleteLoad();
        }

        /// <summary>Attach the swim loop and log the QC oracle line for this animal.</summary>
        private static void AttachWhale(Transform pivot, string assetId, string url, bool ok, float worldLen)
        {
            if (pivot == null) return;
            float itemScale = Mathf.Max(0.01f, pivot.localScale.x);
            // Fallback estimate only when the GLB never landed (the XR marine models measure
            // ~1.9 units across, so item.s × 1.9 is the right order of magnitude).
            if (worldLen <= 0.01f) worldLen = (float)MarineMath.WhaleWorldLen(1.9, itemScale);

            var wc = pivot.gameObject.AddComponent<WhaleController>();
            wc.Init(pivot.position, worldLen);

            Debug.Log($"[Marine] whale asset={assetId} loaded={ok} url={url ?? "(none)"} " +
                      $"itemScale={itemScale:F2} worldLen={worldLen:F1} anchor={pivot.position}");
        }

        /// <summary>
        /// Recentre a freshly-instantiated GLB on its pivot in all three axes. Unlike
        /// <see cref="GroundToBase"/> this does NOT drop the base to Y=0 — a swimmer must
        /// stay at its stored depth and rotate about its own middle.
        /// </summary>
        private static void CenterToPivot(Transform pivot)
        {
            if (!TryLocalBounds(pivot, out Bounds local)) return;
            Vector3 offset = -local.center;
            if (offset.sqrMagnitude < 1e-8f) return;
            for (int i = 0; i < pivot.childCount; i++)
                pivot.GetChild(i).localPosition += offset;
        }

        // ── Grounding (bakeStatic parity) ─────────────────────────────────────────────

        /// <summary>
        /// Recentre the freshly-instantiated GLB content under <paramref name="pivot"/> so
        /// that, in the pivot's local space, the content is centred on X/Z and its lowest
        /// point sits at Y=0. Mirrors the web builder's bakeStatic() so that a stored item
        /// position with Y=0 places the model's BASE on the seabed (not its centre).
        /// No-op when there is nothing renderable yet.
        /// </summary>
        private static void GroundToBase(Transform pivot)
        {
            if (!TryLocalBounds(pivot, out Bounds local)) return;

            var offset = new Vector3(-local.center.x, -local.min.y, -local.center.z);
            if (offset.sqrMagnitude < 1e-8f) return;

            for (int i = 0; i < pivot.childCount; i++)
                pivot.GetChild(i).localPosition += offset;
        }

        /// <summary>AABB of all mesh renderers under <paramref name="pivot"/>, expressed in
        /// the pivot's LOCAL space (labels excluded). Exact under rotation via mesh corners.</summary>
        private static bool TryLocalBounds(Transform pivot, out Bounds local)
        {
            local = default;
            var renderers = pivot.GetComponentsInChildren<MeshRenderer>(true);
            bool has = false;
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            Matrix4x4 worldToPivot = pivot.worldToLocalMatrix;

            foreach (var r in renderers)
            {
                if (r == null || r.transform.name == "Label") continue;
                var mf = r.GetComponent<MeshFilter>();
                Mesh mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null) continue;

                Bounds mb = mesh.bounds; // local to the renderer
                Matrix4x4 m = worldToPivot * r.transform.localToWorldMatrix;
                Vector3 c = mb.center, e = mb.extents;
                for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Vector3 corner = m.MultiplyPoint3x4(c + new Vector3(sx * e.x, sy * e.y, sz * e.z));
                    min = Vector3.Min(min, corner);
                    max = Vector3.Max(max, corner);
                    has = true;
                }
            }

            if (!has) return false;
            local = new Bounds((min + max) * 0.5f, max - min);
            return true;
        }

        /// <summary>World-space AABB over a set of item GameObjects (labels excluded).</summary>
        private static bool TryContentBounds(List<GameObject> gos, out Bounds bounds)
        {
            bounds = default;
            bool has = false;
            foreach (var go in gos)
            {
                if (go == null) continue;
                var renderers = go.GetComponentsInChildren<MeshRenderer>(true);
                foreach (var r in renderers)
                {
                    if (r == null || r.transform.name == "Label") continue;
                    if (!has) { bounds = r.bounds; has = true; }
                    else bounds.Encapsulate(r.bounds);
                }
            }
            return has;
        }

        // ── Placeholder ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Write the failed asset's name over its placeholder box. Off in anything a player can
        /// hold; on in the Editor, where the point is to find out what did not load.
        /// </summary>
        public static bool ShowPlaceholderNames { get; set; } = Application.isEditor;

        private static void SpawnPlaceholder(Transform parent, SceneItem item, AssetManifest.Module module)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Placeholder";
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = Vector3.zero;
            cube.transform.localScale = Vector3.one; // parent already carries item scale

            var rend = cube.GetComponent<Renderer>();
            if (rend != null) rend.sharedMaterial = PlaceholderMaterial(item, module);

            // The name is a DEVELOPER'S aid: it says which asset failed to load. A player sees a
            // wall of a hundred identical frames, so a hundred labels — the phone build showed the
            // word "โครงโปร่ง" stamped across the whole screen in letters taller than the objects
            // they belonged to. The grey box stays (the map keeps its shape and its collisions);
            // the caption goes, and the count of failures is already reported in the status line
            // and in the QC log, which is where it can be read without standing in front of it.
            if (ShowPlaceholderNames)
            {
                string label = item?.Label ?? module?.Name ?? item?.AssetId ?? "?";
                AddLabel(parent, label);
            }
        }

        private static Color ColorForItem(SceneItem item, AssetManifest.Module module)
        {
            string kind = module?.Kind;
            if (string.IsNullOrEmpty(kind))
            {
                // Fall back to the assetId prefix (e.g. "warp:0", "pod:dolphin").
                string aid = item?.AssetId ?? "";
                int colon = aid.IndexOf(':');
                string prefix = colon > 0 ? aid.Substring(0, colon) : aid;
                switch (prefix.ToLowerInvariant())
                {
                    case "rock": kind = "ROCK"; break;
                    case "coral": kind = "CORAL"; break;
                    case "anemone": kind = "ANEMONE"; break;
                    case "fish": kind = "FISH"; break;
                    case "school": kind = "SCHOOL"; break;
                    case "pod": case "msh": kind = "SPECIAL"; break;
                    case "wreck": kind = "WRECK"; break;
                    case "cc0": kind = "ROCK"; break;
                    default: kind = null; break;
                }
            }
            if (kind != null && KindColors.TryGetValue(kind, out Color c)) return c;
            return new Color(0.5f, 0.5f, 0.5f); // gray fallback
        }

        // Material ฐานจาก Resources — บังคับให้ Standard shader (+variant โปร่งใส) ถูกฝังเข้า build
        // (Shader.Find เฉยๆ ใช้ใน build ไม่ได้ ถ้าไม่มี asset อ้าง shader → โดน strip → จอชมพู
        //  บทเรียนจริงจากเทสบน Windows Server รอบแรก)
        private static Material BaseMat(bool transparent)
        {
            var src = Resources.Load<Material>(transparent ? "DM_StandardTransparent" : "DM_Standard");
            if (src != null) return new Material(src); // clone กัน asset โดนแก้
            return new Material(Shader.Find("Standard")); // fallback ใน editor
        }

        /// <summary>
        /// An opaque Standard material for anything built outside this class (ropes). Goes
        /// through <see cref="BaseMat"/> so it uses the SAME Resources material — a shader found
        /// by name at runtime gets stripped from a player build and renders magenta.
        /// </summary>
        public static Material OpaqueMaterial() => BaseMat(false);

        private static Material PlaceholderMaterial(SceneItem item, AssetManifest.Module module)
        {
            var mat = BaseMat(false);
            mat.color = ColorForItem(item, module);
            mat.SetFloat("_Glossiness", 0f); // flat-shaded look
            return mat;
        }

        private static void AddLabel(Transform parent, string text)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0.9f, 0f);

            var tm = go.AddComponent<TextMesh>();
            // Bundled Thai font so labels like "HTMS ช้าง" keep their Thai glyphs on the
            // font-less Linux player (a TextMesh needs the font's material on its renderer).
            Font font = UiFont.Get();
            if (font != null)
            {
                tm.font = font;
                var mr = go.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = font.material;
            }
            tm.text = text;
            tm.characterSize = 0.15f;
            tm.fontSize = 48;
            tm.anchor = TextAnchor.LowerCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.white;

            go.AddComponent<Billboard>();
        }

        private static string ItemName(SceneItem item)
        {
            if (item == null) return "Item";
            string id = item.Id ?? "?";
            string aid = item.AssetId ?? "?";
            return $"Item_{id}_{aid}";
        }

        // ── Seabed (polar grid) + Water ─────────────────────────────────────────────

        private float BuildSeabedAndWater(Transform root, SceneData scene, SceneEnv env, out Bounds bounds)
        {
            float areaScale = env != null ? (float)env.AreaScale : 1f;
            float scaleX = env != null ? (float)env.AreaScaleX : 1f;
            float scaleZ = env != null ? (float)env.AreaScaleZ : 1f;
            float slopeX = env != null ? (float)env.AreaSlopeX : 0f;
            float slopeZ = env != null ? (float)env.AreaSlopeZ : 0f;
            float waterLevel = env != null ? (float)env.WaterLevel : 4f;
            _waterLevel = waterLevel;

            // WO-XR-04.2: the footprint is the web's own — a 340 u superellipse scaled by
            // areaScale × areaScaleX/Z (demo map ⇒ 306 × 374), not the old 15 u disc.
            float thickness = env != null && env.AreaThickness > 0.01 ? (float)env.AreaThickness : 1f;
            float sx = Mathf.Max(0.05f, areaScale) * Mathf.Max(0.05f, scaleX);
            float sz = Mathf.Max(0.05f, areaScale) * Mathf.Max(0.05f, scaleZ);

            // Items must still stand ON the sand: if anything is placed outside the web
            // footprint (hand-edited map, or a scale the web never had), grow rather than
            // leave a wreck floating over open water — and say so in the log.
            float itemMaxR = 0f;
            if (scene != null)
            {
                foreach (SceneItem it in scene.Items())
                {
                    double[] p = it.P;
                    if (p == null || p.Length < 3) continue;
                    Vec3 up = WebCoord.PositionToUnity(new Vec3(p[0], p[1], p[2]));
                    float d = Mathf.Sqrt((float)(up.X * up.X + up.Z * up.Z));
                    if (d > itemMaxR) itemMaxR = d;
                }
            }

            float rx = SeabedGeom.SandRadius * sx;
            float rz = SeabedGeom.SandRadius * sz;
            float need = itemMaxR * 1.05f;
            float inner = Mathf.Min(rx, rz);
            if (need > inner && inner > 0.01f)
            {
                float grow = need / inner;
                sx *= grow; sz *= grow;
                rx *= grow; rz *= grow;
                Debug.LogWarning($"[Scene] seabed grown ×{grow:F2} — an item sits {itemMaxR:F1} u out, " +
                                 $"past the web footprint ({inner:F1} u)");
            }
            float radius = Mathf.Max(rx, rz);
            _seabedScaleX = sx;
            _seabedScaleZ = sz;

            int rings = SeabedGeom.Rings, seg = SeabedGeom.Segments;
            int[] dim = env?.SculptDimensions();
            if (dim != null && dim.Length >= 2 && dim[0] > 1 && dim[1] > 2)
            {
                rings = Mathf.Clamp(dim[0], 2, 128);
                seg = Mathf.Clamp(dim[1], 3, 256);
            }

            // Most maps have never been sculpted, so env.sculpt is absent and ReadSculpt gives
            // null. Allocating the grid ANYWAY (all zeros — the same flat floor) is what makes
            // the brush able to work at all: the QC run showed samples=0 and every stroke was a
            // no-op on an array with nothing in it.
            float[] sculpt = ReadSculpt(env);
            if (sculpt == null || sculpt.Length < rings * seg) sculpt = new float[rings * seg];

            var seabed = new GameObject("Seabed");
            seabed.transform.SetParent(root, false);
            var mf = seabed.AddComponent<MeshFilter>();
            var mr = seabed.AddComponent<MeshRenderer>();
            mf.sharedMesh = BuildPolarGrid(rings, seg, sx, sz, slopeX, slopeZ, sculpt, thickness,
                                           out Mesh topSurface);
            Material sandMat = SandMaterial();
            mr.sharedMaterial = sandMat;
            SeabedView.Register(sandMat, sculpt, slopeX, slopeZ, waterLevel, sx, sz, rings, seg);

            // Sculpting rebuilds ONLY this mesh (a brush stroke must not re-download every GLB),
            // so it needs the exact parameters this build used.
            _sbSx = sx; _sbSz = sz; _sbSlopeX = slopeX; _sbSlopeZ = slopeZ; _sbThickness = thickness;
            SeabedSculptor.Register(sculpt, rings, seg, SeabedGeom.SandRadius);
            seabed.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;

            // ── Caustics (WO-XR-04.3) ─────────────────────────────────────────────
            // Rippling light on the sand: the top surface again, 0.4 u up (an offset, never
            // ZWrite tricks — that is how you get flickering z-fighting), drawn additively
            // with the caustic noise slowly drifting across it.
            if (topSurface != null)
            {
                var caustics = new GameObject("Caustics");
                caustics.transform.SetParent(root, false);
                caustics.transform.localPosition = new Vector3(0f, 0.4f, 0f);
                caustics.AddComponent<MeshFilter>().sharedMesh = topSurface;
                var cmr = caustics.AddComponent<MeshRenderer>();
                cmr.sharedMaterial = CausticsMaterial();
                cmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                cmr.receiveShadows = false;
                caustics.AddComponent<WaterScroll>().speed = new Vector2(0.004f, 0.003f);
            }

            // Water disc at env.waterLevel.
            var water = new GameObject("Water");
            water.transform.SetParent(root, false);
            water.transform.localPosition = new Vector3(0f, waterLevel, 0f);
            var wmf = water.AddComponent<MeshFilter>();
            var wmr = water.AddComponent<MeshRenderer>();
            // Double-sided disc: the orbit camera commonly sits BELOW the water level
            // (looking up at a tall wreck), and a single-sided (Cull Back) plane would be
            // invisible from underneath — the "no water in the QC shot" bug.
            // Squircle, like the seabed (builder.html 654-659 reshapes the round surf plane
            // onto the same rounded-square boundary) so the water edge does not cut a circle
            // out of a rounded-square floor.
            wmf.sharedMesh = BuildDisc(sx * 1.02f, sz * 1.02f, 72, doubleSided: true);
            wmr.sharedMaterial = WaterMaterial();
            water.AddComponent<WaterScroll>();
            // B2 — the surface has shape, not just a sliding texture. Seen from below (where a
            // diver spends the dive) this is what breaks up the light coming through it.
            water.AddComponent<WaterWave>();

            bounds = new Bounds(Vector3.zero, new Vector3(rx * 2f, Mathf.Max(waterLevel, 2f) * 2f, rz * 2f));
            Debug.Log($"[Scene] seabed rx={rx:F1} rz={rz:F1} rings={rings} seg={seg} " +
                      $"thickness={SeabedGeom.SandThickness * thickness:F1} skirt=OK " +
                      $"waterLevel={waterLevel:F1} itemMaxR={itemMaxR:F1}");
            return radius;
        }

        private static float[] ReadSculpt(SceneEnv env)
        {
            if (env?.Sculpt == null) return null;
            var arr = env.Sculpt;
            var heights = new float[arr.Count];
            for (int i = 0; i < arr.Count; i++)
            {
                try { heights[i] = (float)(double)arr[i]; }
                catch { heights[i] = 0f; }
            }
            return heights;
        }

        /// <summary>
        /// The seabed as the web builds it (builder.html 538-558): a superellipse-footprint
        /// radial grid for the sculptable TOP surface, plus a side skirt and a flat bottom so
        /// the sand is a real slab with visible thickness instead of a floating oval.
        ///
        /// Three details are load-bearing:
        ///   • The skirt and bottom get their OWN vertices. Sharing the top rim would make
        ///     RecalculateNormals average an up normal with a sideways one (and, for the
        ///     double-sided skirt, +radial with −radial → zero = solid black; the fish-fin
        ///     lesson from QC r8). Their normals are therefore assigned explicitly.
        ///   • The up-facing winding guard only averages the TOP surface's normals — with the
        ///     bottom's −Y normals in the mix the average is meaningless and the guard would
        ///     flip a perfectly good seabed upside down.
        ///   • The sculpt height array is indexed exactly as before ((r−1)·seg+j), so saved
        ///     web sculpts still land on the right vertices.
        /// </summary>
        private float _sbSx = 1f, _sbSz = 1f, _sbSlopeX, _sbSlopeZ, _sbThickness;

        /// <summary>
        /// Re-mesh the seabed from a modified sculpt array, reusing the slope/scale/thickness the
        /// last full build resolved. Returns the new mesh, or null when this builder has not made
        /// a seabed yet.
        /// </summary>
        public Mesh RebuildSeabedMesh(float[] sculpt, int rings, int seg)
        {
            if (sculpt == null || rings <= 1 || seg <= 2) return null;
            return BuildPolarGrid(rings, seg, _sbSx, _sbSz, _sbSlopeX, _sbSlopeZ, sculpt,
                                  _sbThickness, out _);
        }

        /// <summary>
        /// Change the seabed's footprint / tilt / thickness (the web's <c>applyArea</c>). The
        /// caller re-meshes afterwards; these are the same numbers <c>env</c> stores, so the
        /// values here and the values saved cannot drift apart.
        /// </summary>
        public void SetSeabedShape(float sx, float sz, float slopeX, float slopeZ, float thickness)
        {
            _sbSx = sx; _sbSz = sz;
            _sbSlopeX = slopeX; _sbSlopeZ = slopeZ;
            _sbThickness = thickness;
        }

        public void GetSeabedShape(out float sx, out float sz, out float slopeX, out float slopeZ,
                                   out float thickness)
        {
            sx = _sbSx; sz = _sbSz;
            slopeX = _sbSlopeX; slopeZ = _sbSlopeZ;
            thickness = _sbThickness;
        }

        private static Mesh BuildPolarGrid(int rings, int seg, float sx, float sz,
                                           float slopeX, float slopeZ, float[] sculpt, float thickness,
                                           out Mesh topSurface)
        {
            int topN = 1 + rings * seg;
            int skirtN = seg * 8;             // 4 verts × (outward + inward) per segment
            int botN = 1 + seg;
            int vertCount = topN + skirtN + botN;

            var verts = new Vector3[vertCount];
            var uvs = new Vector2[vertCount];
            var norms = new Vector3[vertCount];

            // ── Top surface ───────────────────────────────────────────────────────
            // Heights come from the UNSCALED (base) coords, exactly like the web's
            // applyArea(): the slope is a property of the map, not of its stretch.
            float minTop = float.MaxValue;
            verts[0] = new Vector3(0f, HeightAt(0f, 0f, slopeX, slopeZ, sculpt, seg, 0, 0), 0f);
            uvs[0] = new Vector2(0.5f, 0.5f);
            minTop = Mathf.Min(minTop, verts[0].y);

            for (int r = 1; r <= rings; r++)
            {
                float frac = (float)r / rings;
                for (int j = 0; j < seg; j++)
                {
                    float ang = (Mathf.PI * 2f) * j / seg;
                    float bd = SeabedGeom.BoundaryDist(ang) * frac;
                    float bx = Mathf.Cos(ang) * bd;      // base (unscaled) coords
                    float bz = Mathf.Sin(ang) * bd;
                    float y = HeightAt(bx, bz, slopeX, slopeZ, sculpt, seg, r, j);
                    int idx = 1 + (r - 1) * seg + j;
                    verts[idx] = new Vector3(bx * sx, y, bz * sz);
                    uvs[idx] = new Vector2(0.5f + 0.5f * frac * Mathf.Cos(ang),
                                           0.5f + 0.5f * frac * Mathf.Sin(ang));
                    if (y < minTop) minTop = y;
                }
            }

            // ── Slab bottom + skirt ───────────────────────────────────────────────
            // Flat bottom kept below the deepest sculpted pit (builder.html:606).
            float bottomY = minTop - SeabedGeom.SandThickness * Mathf.Max(0.05f, thickness);

            int botC = topN + skirtN;               // bottom centre
            int botR = botC + 1;                    // bottom rim ring
            verts[botC] = new Vector3(0f, bottomY, 0f);
            uvs[botC] = new Vector2(1f, 0.5f);      // rim UV → hazed, never lit sand
            norms[botC] = Vector3.down;
            for (int j = 0; j < seg; j++)
            {
                float ang = (Mathf.PI * 2f) * j / seg;
                float bd = SeabedGeom.BoundaryDist(ang);
                verts[botR + j] = new Vector3(Mathf.Cos(ang) * bd * sx, bottomY, Mathf.Sin(ang) * bd * sz);
                uvs[botR + j] = new Vector2(0.5f + 0.5f * Mathf.Cos(ang), 0.5f + 0.5f * Mathf.Sin(ang));
                norms[botR + j] = Vector3.down;
            }

            var tris = new List<int>((rings * seg) * 6 + seg * 12 + seg * 3);

            // Skirt: own verts, drawn from both sides (the web's seabed material is
            // DoubleSide so the sand wall shows from inside the map too).
            for (int j = 0; j < seg; j++)
            {
                int j2 = (j + 1) % seg;
                int topA = 1 + (rings - 1) * seg + j;
                int topB = 1 + (rings - 1) * seg + j2;
                Vector3 tA = verts[topA], tB = verts[topB];
                Vector3 bA = new Vector3(verts[botR + j].x, bottomY, verts[botR + j].z);
                Vector3 bB = new Vector3(verts[botR + j2].x, bottomY, verts[botR + j2].z);

                // Outward normal of this segment: horizontal, away from the centre.
                Vector3 mid = (tA + tB) * 0.5f;
                Vector3 outward = new Vector3(mid.x, 0f, mid.z);
                outward = outward.sqrMagnitude > 1e-6f ? outward.normalized : Vector3.right;

                int o = topN + j * 8;               // 4 outward verts, then 4 inward
                verts[o + 0] = tA; verts[o + 1] = tB; verts[o + 2] = bA; verts[o + 3] = bB;
                verts[o + 4] = tA; verts[o + 5] = tB; verts[o + 6] = bA; verts[o + 7] = bB;
                for (int k = 0; k < 4; k++)
                {
                    // Rim UVs: fully hazed, matching the web (rad ≥ 1 at the boundary).
                    uvs[o + k] = uvs[botR + (k == 1 || k == 3 ? j2 : j)];
                    uvs[o + 4 + k] = uvs[o + k];
                    norms[o + k] = outward;
                    norms[o + 4 + k] = -outward;
                }

                // Both windings, so whichever one Unity treats as front-facing, the wall is
                // never an invisible or black band.
                tris.Add(o + 0); tris.Add(o + 2); tris.Add(o + 3);
                tris.Add(o + 0); tris.Add(o + 3); tris.Add(o + 1);
                tris.Add(o + 4); tris.Add(o + 7); tris.Add(o + 6);
                tris.Add(o + 4); tris.Add(o + 5); tris.Add(o + 7);
            }

            // Bottom fan (single-sided, faces down — it is under the sand).
            for (int j = 0; j < seg; j++)
            {
                int j2 = (j + 1) % seg;
                tris.Add(botC); tris.Add(botR + j); tris.Add(botR + j2);
            }


            // Top-surface triangles are collected separately as well: the caustics overlay
            // (WO-XR-04.3) is the top surface alone, raised a hair — it must not glow on the
            // slab's sides or under its bottom.
            var topTris = new List<int>(rings * seg * 6);

            // Inner fan: center → first ring.
            for (int j = 0; j < seg; j++)
            {
                int b = 1 + j;
                int c = 1 + (j + 1) % seg;
                topTris.Add(0); topTris.Add(b); topTris.Add(c);
            }

            // Ring quads.
            for (int r = 1; r < rings; r++)
            {
                int ringStart = 1 + (r - 1) * seg;
                int nextStart = 1 + r * seg;
                for (int j = 0; j < seg; j++)
                {
                    int j2 = (j + 1) % seg;
                    int v00 = ringStart + j;
                    int v01 = ringStart + j2;
                    int v10 = nextStart + j;
                    int v11 = nextStart + j2;
                    topTris.Add(v00); topTris.Add(v10); topTris.Add(v11);
                    topTris.Add(v00); topTris.Add(v11); topTris.Add(v01);
                }
            }

            tris.AddRange(topTris);

            var mesh = new Mesh { name = "SeabedPolarGrid" };
            if (vertCount > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            // Winding verification: the TOP SURFACE must face up. Averaged over the whole
            // mesh this test is now meaningless (the bottom fan's normals are −Y by design),
            // so it only looks at the top vertices.
            bool flipped = false;
            if (AverageNormalY(mesh, topN) < 0f)
            {
                int[] t = mesh.triangles;
                for (int i = 0; i < t.Length; i += 3)
                {
                    (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]);
                }
                mesh.triangles = t;
                mesh.RecalculateNormals();
                flipped = true;
            }

            // Skirt + bottom normals are assigned, not derived: the skirt is drawn from both
            // sides off shared positions, so RecalculateNormals would cancel +radial against
            // −radial and light the whole sand wall black.
            Vector3[] final = mesh.normals;
            if (final != null && final.Length == vertCount)
            {
                for (int i = topN; i < vertCount; i++) final[i] = norms[i];
                mesh.normals = final;
            }

            // Top surface on its own, for the caustics overlay: same vertices, same (possibly
            // flipped) winding, so the two meshes can never disagree about which way is up.
            topSurface = new Mesh { name = "SeabedTopSurface" };
            var tv = new Vector3[topN];
            var tu = new Vector2[topN];
            var tn = new Vector3[topN];
            Array.Copy(verts, tv, topN);
            Array.Copy(uvs, tu, topN);
            if (final != null && final.Length >= topN) Array.Copy(final, tn, topN);
            int[] tt = topTris.ToArray();
            if (flipped)
            {
                for (int i = 0; i < tt.Length; i += 3) (tt[i + 1], tt[i + 2]) = (tt[i + 2], tt[i + 1]);
            }
            if (topN > 65000) topSurface.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            topSurface.vertices = tv;
            topSurface.uv = tu;
            topSurface.triangles = tt;
            topSurface.normals = tn;
            topSurface.RecalculateBounds();

            return mesh;
        }

        private static float HeightAt(float x, float z, float slopeX, float slopeZ, float[] sculpt, int seg, int r, int j)
        {
            float y = x * slopeX + z * slopeZ;
            if (sculpt != null && sculpt.Length > 0)
            {
                int idx = r == 0 ? 0 : (r - 1) * seg + j;
                if (idx >= 0 && idx < sculpt.Length) y += sculpt[idx];
            }
            return y;
        }

        /// <summary>Average Y of the first <paramref name="count"/> normals (all of them when
        /// count ≤ 0). Bounded so the seabed's up-facing check ignores its own slab bottom.</summary>
        private static float AverageNormalY(Mesh mesh, int count = 0)
        {
            Vector3[] n = mesh.normals;
            if (n == null || n.Length == 0) return 1f;
            int take = count > 0 ? Mathf.Min(count, n.Length) : n.Length;
            float sum = 0f;
            for (int i = 0; i < take; i++) sum += n[i].y;
            return sum / take;
        }

        /// <summary>
        /// Flat disc on the XZ plane. <paramref name="sx"/>/<paramref name="sz"/> scale the
        /// same superellipse boundary the seabed uses (builder.html reshapes the water plane
        /// onto the squircle, 654-659), so water and sand share one footprint.
        /// </summary>
        private static Mesh BuildDisc(float sx, float sz, int seg, bool doubleSided = false)
        {
            var verts = new Vector3[seg + 1];
            var uvs = new Vector2[seg + 1];
            verts[0] = Vector3.zero;
            uvs[0] = new Vector2(0.5f, 0.5f);
            for (int j = 0; j < seg; j++)
            {
                float ang = (Mathf.PI * 2f) * j / seg;
                float bd = SeabedGeom.BoundaryDist(ang);
                float x = Mathf.Cos(ang) * bd * sx;
                float z = Mathf.Sin(ang) * bd * sz;
                verts[1 + j] = new Vector3(x, 0f, z);
                uvs[1 + j] = new Vector2(0.5f + 0.5f * Mathf.Cos(ang), 0.5f + 0.5f * Mathf.Sin(ang));
            }

            var triList = new List<int>(seg * (doubleSided ? 6 : 3));
            for (int j = 0; j < seg; j++)
            {
                triList.Add(0); triList.Add(1 + j); triList.Add(1 + (j + 1) % seg);
            }
            // Second, reverse-wound copy so the disc renders from above AND below without
            // needing a Cull-Off shader variant (which the build strips → magenta lesson).
            if (doubleSided)
            {
                for (int j = 0; j < seg; j++)
                {
                    triList.Add(0); triList.Add(1 + (j + 1) % seg); triList.Add(1 + j);
                }
            }

            var mesh = new Mesh { name = "WaterDisc" };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = triList.ToArray();
            if (doubleSided)
            {
                // Coplanar opposing tris make RecalculateNormals cancel to ~zero (→ black
                // lighting). Force a constant up normal so shading is stable from both sides.
                var normals = new Vector3[verts.Length];
                for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;
                mesh.normals = normals;
            }
            else
            {
                mesh.RecalculateNormals();
            }
            mesh.RecalculateBounds();
            // Single-sided path keeps the up-facing winding guarantee; double-sided already
            // covers both hemispheres so we leave its (constant-up) normals as built.
            if (!doubleSided && AverageNormalY(mesh) < 0f)
            {
                int[] t = mesh.triangles;
                for (int i = 0; i < t.Length; i += 3)
                {
                    (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]);
                }
                mesh.triangles = t;
                mesh.RecalculateNormals();
            }
            return mesh;
        }

        private static Material SandMaterial()
        {
            var mat = BaseMat(false);
            // WHITE, deliberately: the sand's colour now lives in the texture (bottom-to-top
            // gradient × speckle × haze rim). Leaving the old cream tint here would multiply
            // it in twice and wash the deep-water rim back to sand.
            mat.color = Color.white;
            mat.SetFloat("_Glossiness", 0.1f);
            mat.SetFloat("_Metallic", 0f);
            mat.mainTexture = SandTexture();
            return mat;
        }

        // Baked from SeabedGeom.SandColor because the built-in Standard shader ignores mesh
        // vertex colours (the web's mechanism) and a custom vertex-colour shader would be
        // stripped out of the build → magenta. The seabed's UVs are radial, so a texel's
        // distance from the texture centre IS its distance along the boundary ray.
        private const int SandTexSize = 1024;
        private static Texture2D _sandTex;
        private static Texture2D SandTexture()
        {
            if (_sandTex != null) return _sandTex;

            var tex = new Texture2D(SandTexSize, SandTexSize, TextureFormat.RGBA32, true)
            {
                name = "SeabedSand",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            // Speckle grain ≈ one sculpt cell (340/28 ≈ 12 u) so the sand reads as sand at
            // the orbit distance instead of as noise or as a flat plate.
            const float unitsPerTexel = 2f * SeabedGeom.SandRadius / SandTexSize;
            const float speckleUnits = SeabedGeom.SandRadius / SeabedGeom.Rings;
            float freq = unitsPerTexel / speckleUnits;

            // One boundary distance per ANGLE, not per texel: the superellipse only depends on
            // the direction, so a 2,048-entry table turns a million trig+root evaluations into
            // a lookup (this bakes on the main thread during the scene load).
            const int angleSteps = 2048;
            var bdTable = new float[angleSteps];
            for (int i = 0; i < angleSteps; i++)
                bdTable[i] = SeabedGeom.BoundaryDist(Mathf.PI * 2f * i / angleSteps);

            var px = new Color32[SandTexSize * SandTexSize];
            for (int y = 0; y < SandTexSize; y++)
            {
                float dz = 2f * ((y + 0.5f) / SandTexSize - 0.5f);
                for (int x = 0; x < SandTexSize; x++)
                {
                    float dx = 2f * ((x + 0.5f) / SandTexSize - 0.5f);
                    float frac = Mathf.Sqrt(dx * dx + dz * dz);
                    float ang = Mathf.Atan2(dz, dx);
                    // The web's rad = hypot(x,z)/SAND_R — so the corners of the squircle sit
                    // past 1.0 and are fully hazed, while the axis edges reach 1.0 exactly.
                    int ai = (int)((ang < 0f ? ang + Mathf.PI * 2f : ang) * angleSteps / (Mathf.PI * 2f));
                    if (ai < 0) ai = 0; else if (ai >= angleSteps) ai = angleSteps - 1;
                    float radNorm = frac * bdTable[ai] / SeabedGeom.SandRadius;

                    float n = 0.82f + (Mathf.PerlinNoise(x * freq, y * freq) - 0.5f) * 0.18f;
                    SeabedGeom.Rgb c = SeabedGeom.SandColor(1f, radNorm, n);
                    px[y * SandTexSize + x] = new Color32(
                        (byte)Mathf.Clamp(Mathf.RoundToInt(c.R * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(c.G * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(c.B * 255f), 0, 255),
                        255);
                }
            }
            tex.SetPixels32(px);
            tex.Apply(true, false);
            _sandTex = tex;
            Debug.Log($"[Scene] sand texture {SandTexSize}² baked speckle={speckleUnits:F1}u/cell " +
                      $"haze=0.55→1.00 rim=({SeabedGeom.WaterTint.R:F2},{SeabedGeom.WaterTint.G:F2},{SeabedGeom.WaterTint.B:F2})");
            return tex;
        }

        private static Material WaterMaterial()
        {
            var mat = BaseMat(true);
            // Transparent rendering mode (material ฐานตั้ง keyword มาแล้ว — เซ็ตซ้ำกัน regress)
            mat.SetFloat("_Mode", 3f);
            mat.SetFloat("_SrcBlend", (float)(int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)(int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            // The web's own surface colour + opacity (builder.html:651, 0x4aa8da @ 0.5). The
            // r2-era hand-picked blue was compensating for a flat backdrop; with the gradient
            // backdrop in place (WO-XR-04.2) the web value reads correctly.
            mat.color = new Color(0.290f, 0.659f, 0.855f, 0.5f);
            mat.SetFloat("_Glossiness", 0.85f);

            var tex = GenerateCausticTexture(64);
            mat.mainTexture = tex;
            mat.mainTextureScale = new Vector2(4f, 4f);
            return mat;
        }

        /// <summary>
        /// WO-XR-04.3 — additive caustics on the sand. Same transparent base + blend override
        /// as the god rays (no new shader keyword), one queue slot BEFORE them so the shafts
        /// read as being in front of the floor glow, and a low alpha: this is meant to be
        /// noticed as light on sand, not as a second texture.
        /// </summary>
        private static Material CausticsMaterial()
        {
            var mat = BaseMat(true);
            if (mat.HasProperty("_Mode")) mat.SetFloat("_Mode", 3f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)(int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)(int)UnityEngine.Rendering.BlendMode.One);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            mat.renderQueue = 3050;
            mat.color = new Color(0.78f, 0.92f, 1f, 0.13f);
            mat.mainTexture = GenerateCausticTexture(128);
            mat.mainTextureScale = new Vector2(8f, 8f);
            return mat;
        }

        private static Texture2D GenerateCausticTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float n = Mathf.PerlinNoise(x * 0.12f, y * 0.12f);
                    n = Mathf.Pow(n, 2.2f);
                    byte a = (byte)Mathf.Clamp(120 + n * 135f, 0, 255);
                    px[y * size + x] = new Color32(200, 235, 255, a);
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}
