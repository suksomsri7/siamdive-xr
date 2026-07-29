using System.Collections.Generic;
using DiveMap.Core;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace DiveMap.Runtime.Marine
{
    /// <summary>
    /// Runtime marine system (WO-XR-03). Replaces the static-GLB fish blobs with live
    /// swarms: N boids per SCHOOL / pod item (Burst <see cref="BoidsJob"/> +
    /// <see cref="Graphics.RenderMeshInstanced"/>) plus one looping <see cref="WhaleController"/>
    /// per big animal. Schools are anchored to their placed item position and steer around
    /// the scene's solid obstacles (the wreck) with the ported v.0680 smooth avoidance.
    ///
    /// LOD behaviour throttle: each frame every school's distance to the camera decides
    /// whether it re-runs the O(n) boids scan (near), every 2nd frame (mid), every 6th
    /// (far); far schools also skip avoidance. Skipped frames dead-reckon so motion stays
    /// smooth. One RenderMeshInstanced draw per school ⇒ ~10 draw calls for the whole reef.
    ///
    /// A summary line — <c>[Marine] schools=.. fish=.. whale=.. avgFrameMs=..</c> — is
    /// logged to the player log so the CI orchestrator can verify counts and frame cost
    /// from the headless QC run.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FishSchoolSystem : MonoBehaviour
    {
        // ── Registration inputs (filled by SceneBuilder) ──────────────────────────
        /// <summary>
        /// One placed school, already resolved into WORLD units by
        /// <see cref="DiveMap.Core.MarineMath.SchoolGeometryFor"/> (web formulas × item.s).
        /// Nothing here is re-derived from item.scale inside this class — that conflation
        /// was the QC-r6/r7 bug chain.
        /// </summary>
        public struct SchoolReg
        {
            public Vector3 Anchor;
            public float   FishWorldLen;    // ONE fish's world length (u): scad 4.20, barracuda 17.1
            public float   FormRadiusWorld; // shoal SR / cluster R / pod podR in world units
            public float   VertHalfWorld;   // half-height of the flat formation slab (u)
            public float   SpeedCap;        // cruise cap (u/s) from the web per-frame cap ×60
            public float   SizeMin;         // per-fish size jitter (0.85 school / podMin pod)
            public float   SizeMax;         // …                    (1.15 school / podMax pod)
            public bool    IsPod;           // pods space out (SepR 1.0×) instead of packing (1.5×)
            public int     Count;
            public Color   Color;
            public string  Species;
        }

        // Boids weights (tuned; the web uses formation targets rather than pure Reynolds,
        // so these govern the swarm cohesion while the ported constants below govern the
        // regression-prone motion — turn cap, real-delta, home clamp, avoidance).
        private const float WSep = 1.6f, WAlign = 1.0f, WCoh = 1.0f, WAnchor = 0.6f, WWander = 0.5f;
        private const float TurnCap = 0.045f; // rad/frame at FS=1 (builder.html line 1603)

        private NativeArray<FishState>    _cur;
        private NativeArray<FishState>    _nxt;
        private NativeArray<SchoolParams> _schools;
        private NativeArray<ObstacleBox>  _obstacles;
        private bool _alloc;

        private struct SchoolRender
        {
            public Material     Mat;
            public Matrix4x4[]  Matrices;
            public int          Start;
            public int          Count;
            // WO-XR-04.1: the mesh this school draws. Starts as the procedural
            // FishMeshFactory fish and is hot-swapped to the species' real GLB mesh by
            // ApplyGlbTemplate the moment its template lands (a GLB can arrive after
            // Configure). Bake carries the GLB's node transform, right-multiplied onto
            // every instance matrix (the web bakes it into the geometry instead — we
            // cannot, Draco meshes are non-readable).
            public Mesh         Mesh;
            public Matrix4x4    Bake;
            public bool         HasBake;
            public float        DrawScale;   // uniform TRS scale to draw the fish at web size
            public float[]      SizeMul;     // per-fish size jitter (web: 0.85-1.15 / podMin-podMax)
            public int          PhaseOffset; // spreads the LOD frame-skip load
            public Vector3      Anchor;      // school centre (for QC nearest-school framing)
            public float        HomeR;       // shoal radius (for QC framing distance)
            public string       Species;     // asset id, e.g. "school:scad"
        }
        private readonly List<SchoolRender> _render = new List<SchoolRender>();

        private Camera _cam;
        private Mesh   _fishMesh;
        private int    _fishCount;
        private int    _whaleCount;
        private int    _frame;
        private float  _accumMs;
        private int    _accumN;
        private bool   _useInstancing = true;

        // Software GL renderers (Mesa llvmpipe on the headless CI, SwiftShader) report
        // SystemInfo.supportsInstancing == true yet silently draw NOTHING from
        // Graphics.RenderMeshInstanced — the per-instance unity_ObjectToWorld never lands,
        // so every fish collapses to world origin (occluded under the wreck) and the QC eye
        // sees an empty reef while the non-instanced whale renders fine. Detect those and
        // fall back to one Graphics.RenderMesh per fish (415 draws is nothing for the QC
        // shot). Real mobile GPUs keep the single-draw instanced path untouched.
        private static bool IsSoftwareRenderer()
        {
            string n = SystemInfo.graphicsDeviceName;
            if (string.IsNullOrEmpty(n)) return false;
            n = n.ToLowerInvariant();
            return n.Contains("llvmpipe") || n.Contains("softpipe") ||
                   n.Contains("swiftshader") || n.Contains("software") || n.Contains("microsoft basic");
        }

        private const float WiggleRate = 7.0f;   // builder.html wiggle default
        private const float WiggleAmp  = 0.18f;  // radians (~10°), transform-level approximation

        // ── Setup ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Spin up every school. All spans/speeds arrive PRE-RESOLVED in world units from
        /// <see cref="DiveMap.Core.MarineMath.SchoolGeometryFor"/>; big animals (whaleshark)
        /// are real GLBs built by SceneBuilder, so this class only reports their count.
        /// </summary>
        public void Configure(
            List<SchoolReg> schools, List<ObstacleBox> obstacles,
            Camera cam, Material baseMat, float waterLevel, int whaleCount)
        {
            _cam = cam != null ? cam : Camera.main;
            _fishMesh = FishMeshFactory.Fish();

            _useInstancing = SystemInfo.supportsInstancing && !IsSoftwareRenderer();
            Debug.Log($"[Marine] supportsInstancing={SystemInfo.supportsInstancing} " +
                      $"device='{SystemInfo.graphicsDeviceName}' useInstancing={_useInstancing} " +
                      $"(software fallback → per-fish RenderMesh so the QC eye always sees fish)");

            // Obstacles (may be empty).
            int obN = obstacles != null ? obstacles.Count : 0;
            _obstacles = new NativeArray<ObstacleBox>(obN, Allocator.Persistent);
            for (int i = 0; i < obN; i++) _obstacles[i] = obstacles[i];

            // Count fish and lay them out grouped by school.
            int total = 0;
            for (int i = 0; i < schools.Count; i++) total += Mathf.Max(0, schools[i].Count);
            _fishCount = total;

            _cur = new NativeArray<FishState>(total, Allocator.Persistent);
            _nxt = new NativeArray<FishState>(total, Allocator.Persistent);
            _schools = new NativeArray<SchoolParams>(schools.Count, Allocator.Persistent);
            _alloc = true;

            var rng = new Unity.Mathematics.Random(0x51AD5EED);
            int cursor = 0;
            for (int si = 0; si < schools.Count; si++)
            {
                SchoolReg s = schools[si];
                // Everything below is ALREADY in world units (MarineMath.SchoolGeometryFor =
                // the web formula × the saved item.s). Nothing is re-derived from item.scale
                // here — that is exactly what produced the Ø4.2-unit scad marble in QC r7.
                //   scad  s=2.2 → fish 4.20 u, SR 66.0 u, ±26.4 u, 10.1 u/s
                //   barra s=9.2 → fish 17.1 u, R  143.9 u, ±39.6 u,  4.0 u/s (calm, swimMul 0.06)
                float fishWorld = Mathf.Max(0.05f, s.FishWorldLen);
                float R         = Mathf.Max(fishWorld * 2f, s.FormRadiusWorld);
                float vertHalf  = Mathf.Max(fishWorld, s.VertHalfWorld);
                float homeR     = R * 1.2f;   // soft outer wall just past the formation span
                float maxSpeed  = Mathf.Max(0.2f, s.SpeedCap);
                // Pods are SPACED animals (golden disc ≈1.3 body-lengths apart), schools pack.
                float sepR      = fishWorld * (s.IsPod ? 1.0f : 1.5f);
                float capY      = waterLevel - fishWorld;   // never poke through the surface

                _schools[si] = new SchoolParams
                {
                    Anchor    = ToF3(s.Anchor),
                    FishLen   = fishWorld,
                    HomeR     = homeR,
                    NeighborR = fishWorld * 4.0f,
                    SepR      = sepR,
                    MaxSpeed  = maxSpeed,
                    VertHalf  = vertHalf,
                    CapY      = capY,
                    Start     = cursor,
                    Count     = s.Count,
                    Think     = 1,
                    Avoid     = 1,
                };

                // Material (clone the proven DM_Standard so instancing renders in the QC shot).
                Material mat = baseMat != null ? new Material(baseMat) : new Material(Shader.Find("Standard"));
                mat.color = s.Color;
                mat.enableInstancing = true;
                // Non-metallic (r5: a metallic fish reads dark unless it catches a highlight —
                // avoid the "dark dots" regression) but with a light silvery gloss so the scene's
                // bright reflection cube (0.60,0.72,0.82) adds a soft blue-grey sheen on the
                // shadow side — the "ด้านเงาไม่ดำสนิท" fix. The albedo is now a mid silver-grey
                // (SceneBuilder), not near-white, so the lit/shadow contrast is a fish, not a kite.
                // QC r8: 0.3 blew out the flat body facets to pure white under the reflection
                // cubemap. The web fish read as matte green-silver, so keep the highlight low.
                if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.1f);
                if (mat.HasProperty("_Metallic"))   mat.SetFloat("_Metallic", 0f);

                // Per-fish size jitter, exactly as the web scatters it (0.85+rand·0.3 for a
                // school, podMin..podMax for a pod) so a shoal is not 120 identical clones.
                float sizeMin = s.SizeMin > 0.01f ? s.SizeMin : 1f;
                float sizeMax = s.SizeMax > sizeMin ? s.SizeMax : sizeMin;
                var sizeMul = new float[Mathf.Max(0, s.Count)];

                for (int k = 0; k < s.Count; k++)
                {
                    // Web scatter (builder.html 1523-1525): a flat BOX x,z ∈ ±span,
                    // y ∈ ±vertHalf — a pancake, not a ball.
                    float3 off = new float3(rng.NextFloat(-R, R),
                                            rng.NextFloat(-vertHalf, vertHalf),
                                            rng.NextFloat(-R, R));
                    float head = rng.NextFloat(0f, math.PI * 2f);
                    float3 vel = new float3(math.cos(head), 0f, math.sin(head)) * maxSpeed;
                    _cur[cursor] = new FishState
                    {
                        Pos = ToF3(s.Anchor) + off,
                        Vel = vel,
                        School = si,
                        Phase = rng.NextFloat(0f, math.PI * 2f),
                    };
                    sizeMul[k] = rng.NextFloat(sizeMin, sizeMax);
                    cursor++;
                }

                // Draw scale: worldLen = BaseLen × drawScale = the fish's real world length
                // (flen_local × item.s) — the web's own per-fish size, no invented "metres".
                float drawScale = fishWorld / FishMeshFactory.BaseLen;
                Debug.Log($"[Marine] school={si} species={s.Species} count={s.Count} " +
                          $"clusterR={R:F1} fishLen={fishWorld:F2} vertHalf={vertHalf:F1} " +
                          $"speedCap={maxSpeed:F1} homeR={homeR:F1} sepR={sepR:F2} capY={capY:F1} " +
                          $"anchor={s.Anchor}");

                _render.Add(new SchoolRender
                {
                    Mat = mat,
                    Matrices = new Matrix4x4[s.Count],
                    Start = _schools[si].Start,
                    Count = s.Count,
                    Mesh = _fishMesh,
                    Bake = Matrix4x4.identity,
                    HasBake = false,
                    DrawScale = drawScale,
                    SizeMul = sizeMul,
                    PhaseOffset = (si * 37) % 101, // spread frame-skip load (builder.html _lodPh)
                    Anchor = s.Anchor,
                    HomeR = homeR,
                    Species = s.Species,
                });
            }

            // Big animals (whaleshark) are REAL GLBs placed by SceneBuilder — this system
            // only reports the count so the [Marine] summary stays the QC oracle.
            _whaleCount = Mathf.Max(0, whaleCount);

            Debug.Log($"[Marine] configured schools={schools.Count} fish={_fishCount} " +
                      $"whale={_whaleCount} obstacles={obN} waterLevel={waterLevel:F1}");
        }

        /// <summary>
        /// WO-XR-04.1 — swap every school of <paramref name="species"/> over to a real GLB
        /// template (<see cref="FishGlbLibrary"/>). Safe to call before or after the first
        /// frame; schools of other species are untouched and a species with no template just
        /// keeps the procedural mesh, so the reef can never end up empty.
        ///
        /// The draw scale is RE-DERIVED from the loaded mesh's own baked length, not from
        /// FishMeshFactory.BaseLen: the fish's world length (web formula × item.s) is the
        /// oracle, so a 1.911-unit scad GLB and a 1.15-unit procedural fish must both end up
        /// drawn at 4.20 u. Returns how many schools were swapped.
        /// </summary>
        public int ApplyGlbTemplate(string species, Mesh mesh, Material mat, Matrix4x4 bake, float bakedLen)
        {
            if (mesh == null || bakedLen < 1e-4f || string.IsNullOrEmpty(species)) return 0;

            int applied = 0;
            float lastScale = 0f;
            for (int si = 0; si < _render.Count; si++)
            {
                SchoolRender sr = _render[si];
                if (!string.Equals(sr.Species, species, System.StringComparison.OrdinalIgnoreCase)) continue;

                float fishWorld = si < _schools.Length ? _schools[si].FishLen : 0f;
                if (fishWorld <= 0f) continue;

                sr.Mesh = mesh;
                if (mat != null) sr.Mat = mat;
                sr.Bake = bake;
                sr.HasBake = true;
                sr.DrawScale = fishWorld / bakedLen;
                _render[si] = sr;
                lastScale = sr.DrawScale;
                applied++;
            }

            if (applied > 0)
                Debug.Log($"[Marine] fishGlb applied species={species} schools={applied} " +
                          $"bakedLen={bakedLen:F3} drawScale={lastScale:F3} " +
                          $"submesh0Tris={(mesh.subMeshCount > 0 ? mesh.GetIndexCount(0) / 3 : 0)}");
            return applied;
        }

        // ── Per-frame sim + render ────────────────────────────────────────────────

        private void Update()
        {
            if (!_alloc || _fishCount == 0) return;

            float fs = (float)MarineMath.RealDeltaScale(Time.deltaTime);
            float dt = (float)MarineMath.BaseStep * fs;
            float t = Time.time;
            Vector3 camPos = _cam != null ? _cam.transform.position : Vector3.zero;

            // Per-school LOD: decide think/avoid this frame from camera distance.
            for (int si = 0; si < _schools.Length; si++)
            {
                SchoolParams sp = _schools[si];
                float dist = Vector3.Distance(camPos, (Vector3)ToV3(sp.Anchor));
                int stepEvery = MarineMath.StepEveryForDistance(dist);
                int phase = si < _render.Count ? _render[si].PhaseOffset : 0;
                sp.Think = (byte)(((_frame + phase) % stepEvery == 0) ? 1 : 0);
                sp.Avoid = (byte)(MarineMath.AvoidanceActiveForDistance(dist) ? 1 : 0);
                _schools[si] = sp;
            }

            var job = new BoidsJob
            {
                Src = _cur, Dst = _nxt, Schools = _schools, Obstacles = _obstacles,
                Dt = dt, Fs = fs, Time = t,
                WSep = WSep, WAlign = WAlign, WCoh = WCoh, WAnchor = WAnchor, WWander = WWander,
                TurnCap = TurnCap,
            };
            job.Schedule(_fishCount, 32).Complete();

            // Swap buffers.
            var tmp = _cur; _cur = _nxt; _nxt = tmp;

            // Build matrices + one instanced draw per school (or per-fish under software GL).
            int drawn = 0;
            for (int si = 0; si < _render.Count; si++)
            {
                SchoolRender sr = _render[si];
                Matrix4x4[] mats = sr.Matrices;
                for (int k = 0; k < sr.Count; k++)
                {
                    FishState f = _cur[sr.Start + k];
                    Vector3 pos = ToV3(f.Pos);
                    Vector3 vel = ToV3(f.Vel);
                    Quaternion rot = vel.sqrMagnitude > 1e-6f
                        ? Quaternion.LookRotation(vel, Vector3.up)
                        : Quaternion.identity;
                    // Transform-level wiggle: side-to-side waggle about local up.
                    float wig = Mathf.Sin(t * WiggleRate + f.Phase) * WiggleAmp * Mathf.Rad2Deg;
                    rot *= Quaternion.Euler(0f, wig, 0f);
                    float sc = sr.DrawScale * (sr.SizeMul != null && k < sr.SizeMul.Length ? sr.SizeMul[k] : 1f);
                    mats[k] = Matrix4x4.TRS(pos, rot, Vector3.one * sc);
                    // GLB template: bake the mesh node's own transform in AFTER the instance
                    // TRS, so the fish keeps its authored orientation inside the swim heading.
                    if (sr.HasBake) mats[k] = mats[k] * sr.Bake;
                }

                if (sr.Count <= 0) continue;
                Mesh mesh = sr.Mesh != null ? sr.Mesh : _fishMesh;
                if (mesh == null) continue;

                if (_useInstancing)
                {
                    var rp = new RenderParams(sr.Mat)
                    {
                        worldBounds = new Bounds(ToV3(_schools[si].Anchor),
                                                 Vector3.one * (_schools[si].HomeR * 2.5f + _schools[si].FishLen * 8f)),
                        shadowCastingMode = ShadowCastingMode.Off,
                        receiveShadows = false,
                    };
                    Graphics.RenderMeshInstanced(rp, mesh, 0, mats, sr.Count);
                }
                else
                {
                    // Software-GL fallback: draw each fish on its own so the QC eye sees the
                    // swarm even where RenderMeshInstanced would no-op. Per-object culling uses
                    // the mesh bounds × matrix, so no batch-cull risk.
                    var rp = new RenderParams(sr.Mat)
                    {
                        shadowCastingMode = ShadowCastingMode.Off,
                        receiveShadows = false,
                    };
                    for (int k = 0; k < sr.Count; k++)
                        Graphics.RenderMesh(rp, mesh, 0, mats[k]);
                }
                drawn += sr.Count;
            }

            // First-frames diagnostic so the orchestrator can confirm fish were submitted
            // (path + count) straight from the player log next QC round.
            if (_frame < 3)
                Debug.Log($"[Marine] frame={_frame} path={(_useInstancing ? "instanced" : "per-fish")} instancesSubmitted={drawn}");

            // Frame-cost accounting + periodic summary for the orchestrator.
            _accumMs += Time.deltaTime * 1000f;
            _accumN++;
            _frame++;
            if (_frame == 20 || _frame % 200 == 0)
            {
                float avg = _accumN > 0 ? _accumMs / _accumN : 0f;
                Debug.Log($"[Marine] schools={_render.Count} fish={_fishCount} whale={_whaleCount} avgFrameMs={avg:F1}");
                _accumMs = 0f; _accumN = 0;
            }
        }

        private void OnDestroy()
        {
            if (!_alloc) return;
            if (_cur.IsCreated) _cur.Dispose();
            if (_nxt.IsCreated) _nxt.Dispose();
            if (_schools.IsCreated) _schools.Dispose();
            if (_obstacles.IsCreated) _obstacles.Dispose();
            _alloc = false;
        }

        // ── QC / camera helpers ─────────────────────────────────────────────────────
        /// <summary>
        /// Centre + shoal radius of the school NEAREST <paramref name="from"/> whose species id
        /// contains <paramref name="speciesContains"/> (case-insensitive; null/empty = any).
        /// Deterministic — reads the fixed per-school anchors set at Configure. Used by the QC
        /// screenshot to frame a close-up on the scad shoal nearest the wreck.
        /// </summary>
        public bool TryGetNearestSchool(Vector3 from, string speciesContains, out Vector3 anchor, out float homeR)
        {
            anchor = Vector3.zero;
            homeR = 0f;
            float best = float.MaxValue;
            bool found = false;
            string filt = string.IsNullOrEmpty(speciesContains) ? null : speciesContains.ToLowerInvariant();
            for (int i = 0; i < _render.Count; i++)
            {
                SchoolRender sr = _render[i];
                if (filt != null && (string.IsNullOrEmpty(sr.Species) || !sr.Species.ToLowerInvariant().Contains(filt)))
                    continue;
                float d = (sr.Anchor - from).sqrMagnitude;
                if (d < best) { best = d; anchor = sr.Anchor; homeR = sr.HomeR; found = true; }
            }
            return found;
        }

        // ── conversions ───────────────────────────────────────────────────────────
        private static float3 ToF3(Vector3 v) => new float3(v.x, v.y, v.z);
        private static Vector3 ToV3(float3 v) => new Vector3(v.x, v.y, v.z);
    }
}
