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

            // ── Swim style (SwimStyle) ────────────────────────────────────────────
            // The beat is INTEGRATED here rather than read off _Time in the shader, which is
            // what makes it legal for Effort to change the rate mid-swim: sin(_Time.y·rate)
            // jumps by hundreds of radians when `rate` moves at t = 900 s and the tail
            // teleports. See SwimStyle.BeatPhaseStep.
            public double       WavePhase;   // radians, wrapped to [0,2π)
            public float        BeatHz;      // cruise tail-beats/sec for this species AT THIS SIZE
            public float        Cruise;      // the school's own cruise speed (u/s) — Effort's divisor
            public float        MaxBank;     // radians of roll this species allows in a turn
            public float[]      Yaw;         // per-fish heading last frame (for the bank)
            public float[]      YawRate;     // per-fish smoothed turn rate (rad/s)
            public bool         YawPrimed;   // false until Yaw[] holds a real reading
        }
        private readonly List<SchoolRender> _render = new List<SchoolRender>();

        /// <summary>
        /// C5 — the fixed facts each school needs in order to be afraid, resolved once at
        /// Configure. Anchor and HomeR live here as the AUTHORED values because the sim's copies
        /// are rewritten every frame (the shoal is dragged toward cover and tightened into a bait
        /// ball); losing the originals would let a shoal wander off after one scare.
        /// </summary>
        private struct SchoolFear
        {
            public Vector3 Anchor0;
            public float   HomeR0;
            public int     Rank;
            public string  Diet;
            public bool    IsPod;
            public bool    HasShelter;
            public Vector3 Shelter;
            public float   ShelterR;
            public float   BallUntil;   // Time.time the bait ball may relax (FleeMath.BallHoldSeconds)
            public Vector3 HomeNow;     // where the shoal is CURRENTLY centred (eased, never snapped)
            public bool    HomeInit;
            public float   HomeRNow;    // its CURRENT radius (eased — a bait ball forms fast, not instantly)
            public float   LogAt;       // next Time.time this school may write a [Flee] line
        }
        private SchoolFear[] _fear = System.Array.Empty<SchoolFear>();

        // ── Phase 2 goal 2: every school has a brain of its own (Core/FishMind.cs) ──
        // Ten structs, stepped once per school per frame on the main thread. No allocation, no
        // per-fish cost: what the eye reads at shoal distance is the SHOAL's intent (is it going
        // somewhere, did it notice the wreck, is it still nervous), and that is what these hold.
        private Mind[]       _mind   = System.Array.Empty<Mind>();
        private MindTraits[] _traits = System.Array.Empty<MindTraits>();
        /// <summary>The things on this map worth swimming over to look at (the wreck, big rocks).</summary>
        private MindPoi[]    _pois   = System.Array.Empty<MindPoi>();
        private int          _poiCount;
        /// <summary>Radius of the sand. Every target a mind picks is clamped inside it.</summary>
        private float        _domainR = 1f;

        private Camera _cam;
        // P1.2: the tour's fish-repelling bubble (zero radius = nobody is diving).
        private Vector3 _repulsorPos;
        private float   _repulsorRadius;
        // C5: the diver's own speed, which is what decides whether the reef tolerates them.
        private Vector3 _prevCamPos;
        private float   _camSpeed;
        private bool    _camSeen;
        private Mesh   _fishMesh;
        private int    _fishCount;

        /// <summary>Boids currently simulated (A7 perf readout).</summary>
        public int FishCount => _fishCount;
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

        // 🔴 The rigid waggle, kept ONLY for materials that cannot bend.
        //
        // Rotating the whole fish about its up axis is not swimming — nose and tail swing together
        // and the body stays a plank. It was the best a transform-level approximation could do,
        // and it is why the schools were reported as "ปลาไม่ว่าย" while visibly moving. With
        // DM_FishWave the bend happens in the vertex shader, per fish, and applying this on top
        // would add a second, contradictory motion.
        private const float WiggleRate = 7.0f;   // builder.html wiggle default
        private const float WiggleAmp  = 0.18f;  // radians (~10°), transform-level approximation

        private const double TwoPi = 6.283185307179586;

        // Cached property ids. _WavePhase and _WaveEffort are written every frame per school, and
        // Material.SetFloat(string) hashes the name on every call.
        private static readonly int IdWaveLen    = Shader.PropertyToID("_WaveLen");
        private static readonly int IdWaveSpan   = Shader.PropertyToID("_WaveSpan");
        private static readonly int IdWaveAmp    = Shader.PropertyToID("_WaveAmp");
        private static readonly int IdWaveCycles = Shader.PropertyToID("_WaveCycles");
        private static readonly int IdWaveAnchor = Shader.PropertyToID("_WaveAnchor");
        private static readonly int IdWaveRecoil = Shader.PropertyToID("_WaveRecoil");
        private static readonly int IdWaveGust   = Shader.PropertyToID("_WaveGust");
        private static readonly int IdWaveEffort = Shader.PropertyToID("_WaveEffort");
        private static readonly int IdWavePhase  = Shader.PropertyToID("_WavePhase");
        private static readonly int IdWaveMode   = Shader.PropertyToID("_WaveMode");
        private static readonly int IdWaveFwd    = Shader.PropertyToID("_WaveFwd");
        private static readonly int IdWaveSide   = Shader.PropertyToID("_WaveSide");
        private static readonly int IdWaveDir    = Shader.PropertyToID("_WaveDir");

        /// <summary>
        /// Point the body wave down THIS species' body, at the size it is actually drawn.
        ///
        /// Everything numeric comes from <see cref="SwimStyle"/>; this method only converts it
        /// into the mesh's own coordinate system, which is the part that cannot be unit-tested
        /// and the part the old version got wrong twice over:
        ///
        /// 🔴 <paramref name="bakedLen"/> is measured AFTER the glTF node transform. The shader
        /// bends <c>v.vertex</c>, which is BEFORE it. Any scale on that node made <c>u</c> stop
        /// reaching 1.0 at the tail, so the u² envelope — the whole reason the deflection piles
        /// up in the back third instead of hinging in the middle — landed in the wrong place.
        ///
        /// 🔴 The wave axes are not (0,0,1)/(1,0,0) either. The renderer right-multiplies
        /// <paramref name="bake"/> onto the instance TRS, so the direction that comes out as
        /// "forward" in the swim heading is <c>bake⁻¹ · +Z</c> in mesh space. A fish authored
        /// inside a rotated node would otherwise have been bent across its width.
        /// </summary>
        private static void TuneWave(ref SchoolRender sr, Mesh mesh, Matrix4x4 bake,
                                     float bakedLen, float worldLen, int si)
        {
            Material m = sr.Mat;
            if (m == null || mesh == null || !Bends(m) || bakedLen < 1e-4f) return;

            // Mesh-space axes that map to the swim frame's forward / right / up.
            Matrix4x4 inv = bake.inverse;
            Vector3 fwd  = SafeDir(inv.MultiplyVector(Vector3.forward), Vector3.forward);
            Vector3 side = SafeDir(inv.MultiplyVector(Vector3.right),   Vector3.right);
            Vector3 up   = SafeDir(inv.MultiplyVector(Vector3.up),      Vector3.up);

            // Body length / half-span IN MESH UNITS. mesh.bounds is metadata, not vertex data —
            // safe on the Draco meshes, which are non-readable and throw on .vertices.
            Vector3 ms = mesh.bounds.size;
            float meshLen  = (float)SwimStyle.AxisExtent(ms.x, ms.y, ms.z, fwd.x, fwd.y, fwd.z);
            float meshSpan = 0.5f * (float)SwimStyle.AxisExtent(ms.x, ms.y, ms.z, side.x, side.y, side.z);
            if (meshLen < 1e-4f) meshLen = bakedLen;

            SwimWave w = SwimStyle.For(sr.Species, worldLen);

            // Where the thrust goes: a fish and a shark sweep SIDEWAYS, a whale's fluke and a
            // ray's wings go UP AND DOWN. Getting this one line wrong is the difference between
            // a manta and a floating carpet.
            Vector3 thrust = w.Gait == SwimGait.Body ? side : up;

            m.SetFloat(IdWaveLen,    meshLen);
            m.SetFloat(IdWaveSpan,   Mathf.Max(1e-4f, meshSpan));
            m.SetFloat(IdWaveAmp,    (float)w.Amp);
            m.SetFloat(IdWaveCycles, (float)w.Cycles);
            m.SetFloat(IdWaveAnchor, 1f);
            m.SetFloat(IdWaveRecoil, (float)w.Recoil);
            m.SetFloat(IdWaveGust,   (float)w.Gust);
            m.SetFloat(IdWaveMode,   w.Gait == SwimGait.Wing ? 1f : 0f);
            m.SetFloat(IdWaveEffort, 1f);
            m.SetFloat(IdWavePhase,  0f);
            m.SetVector(IdWaveFwd,  fwd);
            m.SetVector(IdWaveSide, side);
            m.SetVector(IdWaveDir,  thrust);

            sr.WavePhase = 0.0;

            // The QC oracle for this whole path. A screenshot cannot show a beat rate, and the
            // amplitude is the number that decides whether the animal reads as swimming or as a
            // rubber band: ampWorld is the tail-tip travel to ONE side in world units, so a 65 u
            // shark should print ~5.5 and a 4.2 u scad ~0.46.
            Debug.Log($"[Swim] school={si} species={sr.Species} gait={w.Gait} " +
                      $"worldLen={worldLen:F2} meshLen={meshLen:F3} bakedLen={bakedLen:F3} " +
                      $"beatHz={w.BeatHz:F2} amp={w.Amp:F3} ampWorld={(w.Amp * worldLen):F2} " +
                      $"cycles={w.Cycles:F2} bankDeg={(w.MaxBankRad * Mathf.Rad2Deg):F0} " +
                      $"fwd={fwd} thrust={thrust}");
        }

        /// <summary>
        /// Does this material carry the body wave? Asked by PROPERTY, not by shader name: the
        /// schools use DiveMap/FishWave and the hero animals DiveMap/FishWaveDetail, and a name
        /// test silently stopped bending the moment a second variant existed.
        /// </summary>
        private static bool Bends(Material m) => m != null && m.HasProperty(IdWavePhase);

        private static Vector3 SafeDir(Vector3 v, Vector3 fallback)
            => v.sqrMagnitude > 1e-8f ? v.normalized : fallback;

        // ── Setup ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Spin up every school. All spans/speeds arrive PRE-RESOLVED in world units from
        /// <see cref="DiveMap.Core.MarineMath.SchoolGeometryFor"/>; big animals (whaleshark)
        /// are real GLBs built by SceneBuilder, so this class only reports their count.
        /// </summary>
        /// <param name="domainRadius">
        /// Radius of the seabed disc (SceneBuilder's own <c>seabedRadius</c>). Every place a school
        /// decides to go is clamped inside it, leaving the school's own home radius of clearance at
        /// the rim — so it is the WHOLE shoal that stays over the sand, not merely its centre. Pass
        /// 0 and it is derived from where the schools were placed, so an old two-arg caller still
        /// gets a map rather than an unbounded one.
        /// </param>
        public void Configure(
            List<SchoolReg> schools, List<ObstacleBox> obstacles,
            Camera cam, Material baseMat, float waterLevel, int whaleCount,
            float domainRadius = 0f)
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

            _fear = new SchoolFear[schools.Count];
            _mind = new Mind[schools.Count];
            _traits = new MindTraits[schools.Count];

            // Points of interest = the biggest structures on the map. Capped at eight: T-13 is 494
            // placed objects built from 10 assets, and a school choosing between 494 pebbles is
            // both pointless and a per-frame scan nobody asked for. Biggest-first, because what a
            // school swims over to look at is the wreck, not a brick.
            BuildPois(obN);

            _domainR = domainRadius > 1f ? domainRadius : 0f;

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
                    Panic     = 0f,
                    DartMul   = 1f,
                };

                // C5 — who this school is, and where it would hide. The web re-scans for cover
                // every 1.2 s (shelterSense); here the obstacles are static for the life of the
                // map, so the nearest one is resolved once and never scanned again.
                SpeciesGenome.Genome gen = SpeciesGenome.For(s.Species);
                var fear = new SchoolFear
                {
                    Anchor0 = s.Anchor,
                    HomeR0  = homeR,
                    Rank    = gen.Rank,
                    Diet    = gen.Diet,
                    IsPod   = s.IsPod,
                };
                float bestD2 = float.MaxValue;
                for (int oi = 0; oi < obN; oi++)
                {
                    ObstacleBox b = _obstacles[oi];
                    Vector3 c = new Vector3((b.Min.x + b.Max.x) * 0.5f,
                                            (b.Min.y + b.Max.y) * 0.5f,
                                            (b.Min.z + b.Max.z) * 0.5f);
                    float ddx = c.x - s.Anchor.x, ddz = c.z - s.Anchor.z;
                    float d2 = ddx * ddx + ddz * ddz;
                    if (d2 >= bestD2) continue;
                    bestD2 = d2;
                    fear.HasShelter = true;
                    fear.Shelter = c;
                    fear.ShelterR = b.ObsR;
                }
                // Seeded here so the shoal's live centre is valid on frame ZERO. The diver's
                // distance is measured against it (a shoal that has wandered 100 u off is not
                // where it was placed), and a zero vector on the first frame would read as a
                // charge from the map origin.
                fear.HomeNow = s.Anchor;
                fear.HomeInit = true;
                fear.HomeRNow = homeR;
                _fear[si] = fear;

                // This school's own brain. The seed is the species name folded with the school
                // index, so two barracuda schools on the same map behave differently but the SAME
                // map always replays identically — which is what makes the QC run reproducible and
                // FishMindTests possible at all.
                _traits[si] = FishMind.TraitsFor(s.Species);
                _mind[si] = new Mind { Seed = MindSeed(s.Species, si), Poi = -1 };

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

                // How this species swims, at the size it is drawn. Resolved HERE and not only in
                // ApplyGlbTemplate, because the beat rate and the bank are transform-level: a
                // school still waiting on its GLB (or one that never gets a template) must still
                // lean into its turns rather than skid round flat like a compass needle.
                SwimWave wave = SwimStyle.For(s.Species, fishWorld);

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
                    WavePhase = 0.0,
                    BeatHz = (float)wave.BeatHz,
                    Cruise = maxSpeed,
                    MaxBank = (float)wave.MaxBankRad,
                    Yaw = new float[Mathf.Max(0, s.Count)],
                    YawRate = new float[Mathf.Max(0, s.Count)],
                    YawPrimed = false,
                });
            }

            // Big animals (whaleshark) are REAL GLBs placed by SceneBuilder — this system
            // only reports the count so the [Marine] summary stays the QC oracle.
            _whaleCount = Mathf.Max(0, whaleCount);

            // No seabed radius from the caller → derive one that at least contains every school it
            // was given, so "stay on the map" still means something rather than nothing.
            if (_domainR <= 1f)
            {
                float far = 0f;
                for (int si = 0; si < _schools.Length; si++)
                {
                    float3 a = _schools[si].Anchor;
                    far = Mathf.Max(far, Mathf.Sqrt(a.x * a.x + a.z * a.z) + _schools[si].HomeR);
                }
                _domainR = Mathf.Max(60f, far * 1.35f);
            }

            Debug.Log($"[Marine] configured schools={schools.Count} fish={_fishCount} " +
                      $"whale={_whaleCount} obstacles={obN} waterLevel={waterLevel:F1}");
            Debug.Log($"[Mind] configured schools={schools.Count} pois={_poiCount} " +
                      $"domainR={_domainR:F0} (targets are clamped inside it, minus each " +
                      $"school's own home radius)");
        }

        /// <summary>
        /// The eight biggest obstacles, as things a school might go and look at. Selected by
        /// insertion into a fixed array — no sort, no allocation beyond the one array, and it runs
        /// once at Configure.
        /// </summary>
        private void BuildPois(int obN)
        {
            const int Max = 8;
            _pois = new MindPoi[Max];
            _poiCount = 0;
            for (int oi = 0; oi < obN; oi++)
            {
                ObstacleBox b = _obstacles[oi];
                float cx = (b.Min.x + b.Max.x) * 0.5f;
                float cy = (b.Min.y + b.Max.y) * 0.5f;
                float cz = (b.Min.z + b.Max.z) * 0.5f;
                float r = Mathf.Max(b.Max.x - b.Min.x, b.Max.z - b.Min.z) * 0.5f + b.ObsR;
                var poi = new MindPoi(cx, cy, cz, r);

                if (_poiCount < Max) { _pois[_poiCount++] = poi; continue; }
                int smallest = 0;
                for (int k = 1; k < Max; k++)
                    if (_pois[k].Radius < _pois[smallest].Radius) smallest = k;
                if (r > _pois[smallest].Radius) _pois[smallest] = poi;
            }
        }

        /// <summary>
        /// A stable seed for one school: FNV-1a of the species name, folded with the school index.
        /// Deterministic across runs and devices — <c>GetHashCode()</c> on a string is not.
        /// </summary>
        internal static uint MindSeed(string species, int schoolIndex)
        {
            uint h = 2166136261u;
            if (!string.IsNullOrEmpty(species))
                for (int i = 0; i < species.Length; i++) { h ^= species[i]; h *= 16777619u; }
            return h ^ ((uint)schoolIndex * 0x9E3779B9u);
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
                // 🔴 One material PER SCHOOL, not the template's shared instance. _WavePhase is
                // written every frame and Graphics.RenderMeshInstanced reads the material when it
                // RENDERS, not when it is submitted — so two schools sharing a material would both
                // draw with whichever phase was written last, and the tuning (_WaveLen, _WaveAmp)
                // of whichever school ran last would win for both. The barracuda schools on HTMS
                // Chang are the same species at different item scales, so that is not theoretical.
                // Configure already clones per school for exactly this reason; the instanced
                // variant survives because it is ticked on the Resources material ASSET, and a
                // clone inherits it.
                if (mat != null) sr.Mat = new Material(mat) { enableInstancing = true };
                sr.Bake = bake;
                sr.HasBake = true;
                sr.DrawScale = fishWorld / bakedLen;
                TuneWave(ref sr, mesh, bake, bakedLen, fishWorld, si);
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

            // C5 — how fast the diver is moving. This single number decides whether the reef
            // tolerates them or scatters (FleeMath.DiverPanicSpeed). Smoothed, because one long
            // frame on a phone must not read as a charge.
            if (_camSeen && dt > 1e-4f)
            {
                // HORIZONTAL only, and divided by the SIMULATION step rather than the wall clock —
                // both straight from the web (builder.html:3940, hypot(dx,dz)/(0.016·FS)). A diver
                // dropping straight down past a shoal is not charging it, and the sim step is what
                // makes the reading frame-rate independent: FS is capped at 2.5, so on a slow
                // device the drone covers less ground per frame. Measured against the wall clock
                // the QC player (~8 fps) reported 10 u/s for a drone doing 30 — under the 11 u/s
                // threshold, so C5 looked like it did nothing at all.
                float mx = camPos.x - _prevCamPos.x, mz = camPos.z - _prevCamPos.z;
                float inst = Mathf.Sqrt(mx * mx + mz * mz) / dt;
                // A TELEPORT is not a charge. Entering the tour moves the camera from the orbit
                // rig to the dive spawn in one frame; the QC log caught that as camSpeed=467 u/s
                // (the drone's own top speed is 30) and the whole reef panicked at a diver who had
                // not moved. Anything past a few times the drone's maximum is a jump, so the
                // history is discarded rather than blended.
                if (inst > DroneFlight.Speed * 3f) _camSpeed = 0f;
                else _camSpeed = Mathf.Lerp(_camSpeed, inst, 0.25f);
            }
            _prevCamPos = camPos;
            _camSeen = true;
            bool diverActive = _repulsorRadius > 0.01f;   // the tour sets the bubble; nothing else does

            // Per-school LOD: decide think/avoid this frame from camera distance.
            for (int si = 0; si < _schools.Length; si++)
            {
                SchoolParams sp = _schools[si];
                float dist = Vector3.Distance(camPos, (Vector3)ToV3(sp.Anchor));
                int stepEvery = MarineMath.StepEveryForDistance(dist);
                int phase = si < _render.Count ? _render[si].PhaseOffset : 0;
                sp.Think = (byte)(((_frame + phase) % stepEvery == 0) ? 1 : 0);
                sp.Avoid = (byte)(MarineMath.AvoidanceActiveForDistance(dist) ? 1 : 0);
                ApplyFear(si, ref sp, camPos, diverActive, t);
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

            // P1.2 — the drone's bubble (the web's droneBubble, builder.html:1668): fish are
            // DISPLACED out of a cavity around the diver rather than steered, which is what makes
            // a shoal part around you instead of swimming through your face. Applied after the
            // sim so the boids keep their formation and only the last centimetres are nudged.
            if (_repulsorRadius > 0.01f)
            {
                float rx = _repulsorPos.x, rz = _repulsorPos.z;
                for (int i = 0; i < _fishCount; i++)
                {
                    FishState f = _cur[i];
                    float dx = f.Pos.x - rx, dz = f.Pos.z - rz;
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    float push = DiveLightMath.BubblePush(d, _repulsorRadius);
                    if (push <= 0f) continue;
                    f.Pos.x += dx / d * push;
                    f.Pos.z += dz / d * push;
                    _cur[i] = f;
                }
            }

            // Build matrices + one instanced draw per school (or per-fish under software GL).
            int drawn = 0;
            for (int si = 0; si < _render.Count; si++)
            {
                SchoolRender sr = _render[si];
                bool bends = Bends(sr.Mat);
                Matrix4x4[] mats = sr.Matrices;

                // ── Drive the beat ────────────────────────────────────────────────
                // Effort: how hard this school is actually swimming. DartMul is 1 at cruise and
                // rises as the shoal panics, so a frightened school beats faster AND harder — the
                // thing that makes a scatter read as a scatter rather than a speed-up.
                float effort = (float)SwimStyle.Effort(sr.Cruise * _schools[si].DartMul, sr.Cruise);
                if (bends)
                {
                    // dt, not Time.deltaTime: the boids move on the sim step (real-delta clamped
                    // to [0.5,2.5]×), so beating on the wall clock would drift out of step with
                    // the swimming on a stuttering frame.
                    sr.WavePhase += SwimStyle.BeatPhaseStep(sr.BeatHz * effort, dt);
                    if (sr.WavePhase >= TwoPi) sr.WavePhase %= TwoPi;   // never let a float lose its beat
                    sr.Mat.SetFloat(IdWavePhase, (float)sr.WavePhase);
                    sr.Mat.SetFloat(IdWaveEffort, effort);
                }

                for (int k = 0; k < sr.Count; k++)
                {
                    FishState f = _cur[sr.Start + k];
                    Vector3 pos = ToV3(f.Pos);
                    Vector3 vel = ToV3(f.Vel);
                    Quaternion rot = vel.sqrMagnitude > 1e-6f
                        ? Quaternion.LookRotation(vel, Vector3.up)
                        : Quaternion.identity;

                    // ── Lean into the turn ────────────────────────────────────────
                    // A fish does not pivot flat. Rolling into the corner is one of the three
                    // things the eye actually reads as "alive", and it is free here because the
                    // heading is already being rebuilt every frame.
                    //
                    // This cannot become the stuck barrel-roll that bit the web build: the roll
                    // is a pure tanh of the CURRENT turn rate (SwimStyle.BankRad), so there is no
                    // accumulator to get stuck, and the sim's own TurnCap bounds the input.
                    if (sr.MaxBank > 1e-4f && sr.Yaw != null && k < sr.Yaw.Length)
                    {
                        float yaw = Mathf.Atan2(vel.x, vel.z);
                        float rate = 0f;
                        if (sr.YawPrimed)
                        {
                            float dYaw = Mathf.DeltaAngle(sr.Yaw[k] * Mathf.Rad2Deg,
                                                          yaw * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                            // Smoothed, because a far school only re-thinks every 6th frame and
                            // dead-reckons in between: the raw rate would spike on think frames
                            // and be zero on the others, flicking the roll.
                            rate = Mathf.Lerp(sr.YawRate[k], dYaw / Mathf.Max(dt, 1e-4f), 0.25f);
                        }
                        sr.Yaw[k] = yaw;
                        sr.YawRate[k] = rate;
                        float bank = (float)SwimStyle.BankRad(rate, sr.MaxBank) * Mathf.Rad2Deg;
                        rot *= Quaternion.Euler(0f, 0f, bank);
                    }

                    // Only the fish that cannot bend get the old plank-waggle.
                    if (!bends)
                    {
                        float wig = Mathf.Sin(t * WiggleRate + f.Phase) * WiggleAmp * Mathf.Rad2Deg;
                        rot *= Quaternion.Euler(0f, wig, 0f);
                    }
                    float sc = sr.DrawScale * (sr.SizeMul != null && k < sr.SizeMul.Length ? sr.SizeMul[k] : 1f);
                    mats[k] = Matrix4x4.TRS(pos, rot, Vector3.one * sc);
                    // GLB template: bake the mesh node's own transform in AFTER the instance
                    // TRS, so the fish keeps its authored orientation inside the swim heading.
                    if (sr.HasBake) mats[k] = mats[k] * sr.Bake;
                }
                sr.YawPrimed = true;
                _render[si] = sr;

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

                // Heartbeat. The per-decision lines above go quiet for half a minute at a time when
                // a school is on a long dwell, and "quiet" must never be indistinguishable from
                // "the brain is not running" (HANDOFF §4 rule 14). One line, every school, no alloc
                // beyond the string itself — and only every 200 frames.
                if (_mind.Length > 0)
                {
                    var sb = new System.Text.StringBuilder(96);
                    sb.Append("[Mind] alive");
                    for (int si = 0; si < _mind.Length; si++)
                        sb.Append(' ').Append(si).Append(':')
                          .Append(FishMind.Label(_mind[si].State))
                          .Append('/').Append(_mind[si].Wariness.ToString("F1"));
                    Debug.Log(sb.ToString());
                }
            }
        }

        /// <summary>
        /// C5 — work out what school <paramref name="si"/> is afraid of this frame and translate
        /// that into the four fields the job consumes. Everything numeric comes from
        /// <see cref="FleeMath"/>; this method only decides WHICH threat applies.
        ///
        /// Order matters and mirrors the web (builder.html:1688-1702): a real predator is checked
        /// first, and the diver is only considered when the predator produced no fear at all.
        /// </summary>
        private void ApplyFear(int si, ref SchoolParams sp, Vector3 camPos, bool diverActive, float t)
        {
            if (si >= _fear.Length) return;
            SchoolFear fx = _fear[si];

            // Nearest school that actually frightens this one. Schools are few (the demo map has
            // ten), so the O(n²) scan costs nothing and needs no throttling — unlike the web,
            // which scans every placed animal and so re-senses only every 0.7 s.
            bool hasPred = false;
            float predDist = float.MaxValue;
            Vector3 predPos = Vector3.zero;
            for (int oj = 0; oj < _fear.Length; oj++)
            {
                if (oj == si) continue;
                SchoolFear other = _fear[oj];
                if (!FleeMath.IsThreat(fx.Rank, other.Rank, other.Diet)) continue;
                // The AUTHORED anchor, not the sim's: that one is dragged toward cover every
                // frame, so measuring against it would make two frightened schools chase each
                // other's fear around the map.
                Vector3 op = other.Anchor0;
                float d = Mathf.Sqrt((op.x - fx.Anchor0.x) * (op.x - fx.Anchor0.x) +
                                     (op.z - fx.Anchor0.z) * (op.z - fx.Anchor0.z));
                if (d >= predDist) continue;
                predDist = d; predPos = op; hasPred = true;
            }

            // 🔴 Measured from where the shoal ACTUALLY IS, not where it was placed. Before the mind
            // landed those were the same point; now a school can legitimately be a hundred units
            // from its placement, and measuring the diver against the placement would panic a shoal
            // nobody was near — and ignore one being charged. (The predator scan above deliberately
            // still uses the authored anchors: those are dragged toward cover BY fear, so measuring
            // fear against them is the feedback loop the C5 comment warns about.)
            Vector3 centre = fx.HomeInit ? fx.HomeNow : fx.Anchor0;
            float diverDist = Mathf.Sqrt((camPos.x - centre.x) * (camPos.x - centre.x) +
                                         (camPos.z - centre.z) * (camPos.z - centre.z));

            float panic = (float)FleeMath.SchoolPanic(
                predDist, hasPred,
                diverDist, _camSpeed, diverActive,
                fx.HomeR0, sp.FishLen);

            // Which of the two is the thing to swim away from — the same test that produced the
            // panic, so the shoal never bursts away from something that did not scare it.
            bool predWon = hasPred &&
                           FleeMath.PanicLevel(predDist, FleeMath.PredatorPanicRadius(fx.HomeR0, sp.FishLen)) > 0.0;
            sp.Threat = ToF3(predWon ? predPos : camPos);

            sp.Panic   = panic;
            sp.FleeW   = (float)FleeMath.FleeSteerWeight(panic, fx.HomeR0, sp.FishLen);
            sp.DartMul = (float)FleeMath.DartSpeedScale(panic);

            // Bait ball: held for a couple of seconds past the scare so the shoal does not
            // snap back open the instant the diver slows down (web :1697 modeUntil).
            if (FleeMath.ShouldBallUp(panic, fx.IsPod))
                fx.BallUntil = t + (float)FleeMath.BallHoldSeconds;
            bool balled = t < fx.BallUntil;
            float wantR = balled
                ? (float)FleeMath.BallHomeRadius(fx.HomeR0, Mathf.Max(panic, (float)FleeMath.BallUpPanic))
                : fx.HomeR0;
            // Eased for the same reason as the home itself: ClampHome squeezes every fish inside
            // this radius, so dropping it 45 % in a single frame would yank the outer ring of the
            // shoal inward all at once. A bait ball forms fast, not instantly.
            if (fx.HomeRNow <= 0f) fx.HomeRNow = fx.HomeR0;
            fx.HomeRNow = Mathf.MoveTowards(fx.HomeRNow, wantR, sp.MaxSpeed * Mathf.Max(Time.deltaTime, 1e-4f));
            sp.HomeR = fx.HomeRNow;

            // ── What this school WANTS to be doing (Core/FishMind.cs) ─────────────────
            // The mind only ever moves a TARGET; the ease below is what turns it into motion, so a
            // school can never be dragged faster than it could have swum. The domain is rebuilt per
            // school (a struct — free) because the ceiling and floor are the school's own: CapY is
            // waterLevel − fishLen and the slab is ±VertHalf around the centre, so clamping the
            // CENTRE to those bounds is what keeps the whole pancake under the surface.
            double minY = sp.VertHalf + sp.FishLen;
            double maxY = sp.CapY - sp.VertHalf;
            if (maxY < minY + 1.0) maxY = minY + 1.0;
            MindDomain dom = FishMind.SchoolDomain(_domainR, minY, maxY,
                                                   fx.Anchor0.x, fx.Anchor0.z, fx.HomeR0);

            Mind mind = _mind[si];
            bool minded = FishMind.Step(
                ref mind, _traits[si], dom, _pois, _poiCount,
                fx.Anchor0.x, fx.Anchor0.y, fx.Anchor0.z,
                fx.HomeR0, panic, t, Time.deltaTime, out MindState was);
            _mind[si] = mind;

            // One line PER DECISION — not per frame, and not only when something dramatic happens.
            // A reviewer has to be able to read the whole state machine off the player log, because
            // a screenshot cannot show intent: a shoal parked over the wreck because it chose to
            // and one parked there because the wander term is broken look identical.
            if (minded)
            {
                string species = si < _render.Count ? _render[si].Species : "?";
                // The very first line of a school's life is its birth, not a transition: the mind
                // opens in Cruise, so "Cruise→Cruise" would be the one line in the log that reads
                // like a bug. (A Startle on decision 0 has was != State, so it is unaffected.)
                string from = (was == mind.State && mind.Tick == 0) ? "start" : FishMind.Label(was);
                Debug.Log($"[Mind] school={si} species={species} " +
                          $"state={from}→{FishMind.Label(mind.State)} " +
                          $"target=({mind.TX:F0},{mind.TY:F0},{mind.TZ:F0}) " +
                          $"n={mind.Tick} dwell={(mind.Until - t):F0}s wary={mind.Wariness:F2} " +
                          $"poi={mind.Poi} panic={panic:F2} " +
                          $"home=({fx.HomeNow.x:F0},{fx.HomeNow.z:F0}) " +
                          $"roam={(_traits[si].RoamMul * fx.HomeR0):F0}u");
            }

            // Run for cover: drag the shoal's home toward the nearest structure in proportion to
            // fear. Fish already at the reef stay put rather than piling into it. Applied ON TOP of
            // whatever the mind wanted — fear overrides curiosity, never the other way round.
            Vector3 want = new Vector3((float)mind.TX, (float)mind.TY, (float)mind.TZ);
            if (fx.HasShelter && panic > 0.001f)
            {
                float toCover = Mathf.Sqrt((fx.Shelter.x - want.x) * (fx.Shelter.x - want.x) +
                                           (fx.Shelter.z - want.z) * (fx.Shelter.z - want.z));
                if (!FleeMath.AtShelter(toCover, fx.ShelterR))
                {
                    float k = (float)FleeMath.ShelterLerp(panic, true);
                    want.x = Mathf.Lerp(want.x, fx.Shelter.x, k);
                    want.z = Mathf.Lerp(want.z, fx.Shelter.z, k);
                }
            }

            // …but the home is EASED there and back, never snapped. ClampHome pulls every fish
            // into a circle around this point, so moving it instantly would teleport the whole
            // shoal — which is precisely what happens the moment a charging diver stops and the
            // panic falls to zero in a few frames. Capping the home's own speed at the fish's
            // cruise speed makes that impossible: the shoal can never be dragged faster than it
            // could have swum.
            if (!fx.HomeInit) { fx.HomeNow = fx.Anchor0; fx.HomeInit = true; }
            fx.HomeNow = Vector3.MoveTowards(fx.HomeNow, want, sp.MaxSpeed * Mathf.Max(Time.deltaTime, 1e-4f));
            sp.Anchor = ToF3(fx.HomeNow);

            _fear[si] = fx;

            // One line a second while anything is frightened — the QC log is how a reviewer
            // confirms this ran at all, and silence here was exactly how the wallet stayed
            // broken for three rounds.
            // Also logged when the diver is merely MOVING, not only when something panics: a silent
            // log was indistinguishable from "the feature is broken" when the real answer was
            // "the drone measured 10 u/s". A reviewer needs the speed to tell those apart.
            // Throttled PER SCHOOL. A single shared timer meant whichever school happened to
            // run first each second took the slot, so the shoal the diver was actually charging
            // never appeared in the log at all.
            if (diverActive && (panic > 0.05f || _camSpeed > 4f) && t > fx.LogAt)
            {
                fx.LogAt = t + 1f;
                Debug.Log($"[Flee] school={si} panic={panic:F2} src={(predWon ? "predator" : "diver")} " +
                          $"camSpeed={_camSpeed:F1}/{FleeMath.DiverPanicSpeed:F0} dist={diverDist:F0} " +
                          $"R={FleeMath.DiverPanicRadius(fx.HomeR0, sp.FishLen):F0} " +
                          $"homeR={sp.HomeR:F0}/{fx.HomeR0:F0} balled={balled}");
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

        /// <summary>
        /// P1.2 — tell the reef that a diver is here: fish are pushed out of a bubble of
        /// <paramref name="radius"/> around <paramref name="pos"/>. Radius 0 clears it.
        /// </summary>
        public void SetRepulsor(Vector3 pos, float radius)
        {
            _repulsorPos = pos;
            _repulsorRadius = Mathf.Max(0f, radius);
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
