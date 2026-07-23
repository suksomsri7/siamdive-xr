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
        public struct SchoolReg
        {
            public Vector3 Anchor;
            public float   FishLen;   // per-fish world length (≈ item scale)
            public int     Count;
            public Color   Color;
            public string  Species;
        }

        public struct WhaleReg
        {
            public Vector3 Anchor;
            public float   Size;
            public Color   Color;
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
            public float        FishLen;
            public int          PhaseOffset; // spreads the LOD frame-skip load
        }
        private readonly List<SchoolRender> _render = new List<SchoolRender>();

        private Camera _cam;
        private Mesh   _fishMesh;
        private int    _fishCount;
        private int    _whaleCount;
        private int    _frame;
        private float  _accumMs;
        private int    _accumN;

        private const float WiggleRate = 7.0f;   // builder.html wiggle default
        private const float WiggleAmp  = 0.18f;  // radians (~10°), transform-level approximation

        // ── Setup ─────────────────────────────────────────────────────────────────

        public void Configure(
            List<SchoolReg> schools, List<WhaleReg> whales, List<ObstacleBox> obstacles,
            Camera cam, Material baseMat)
        {
            _cam = cam != null ? cam : Camera.main;
            _fishMesh = FishMeshFactory.Fish();

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
                float fl = Mathf.Max(0.3f, s.FishLen);
                float R = fl * Mathf.Max(2.8f, s.Count * 0.07f); // formation radius (builder.html 1495)
                float homeR = R * 3.2f;                          // safety radius (builder.html 1611)
                float maxSpeed = fl * 4.0f;                      // cruise (units/sec)

                _schools[si] = new SchoolParams
                {
                    Anchor    = ToF3(s.Anchor),
                    FishLen   = fl,
                    HomeR     = homeR,
                    NeighborR = fl * 4.0f,
                    SepR      = fl * 1.5f,
                    MaxSpeed  = maxSpeed,
                    Start     = cursor,
                    Count     = s.Count,
                    Think     = 1,
                    Avoid     = 1,
                };

                // Material (clone the proven DM_Standard so instancing renders in the QC shot).
                Material mat = baseMat != null ? new Material(baseMat) : new Material(Shader.Find("Standard"));
                mat.color = s.Color;
                mat.enableInstancing = true;
                if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.15f);
                if (mat.HasProperty("_Metallic"))   mat.SetFloat("_Metallic", 0.1f);

                for (int k = 0; k < s.Count; k++)
                {
                    float ang = rng.NextFloat(0f, math.PI * 2f);
                    float rad = rng.NextFloat(0f, R);
                    float3 off = new float3(math.cos(ang) * rad,
                                            rng.NextFloat(-R * 0.55f, R * 0.55f),
                                            math.sin(ang) * rad);
                    float head = rng.NextFloat(0f, math.PI * 2f);
                    float3 vel = new float3(math.cos(head), 0f, math.sin(head)) * maxSpeed;
                    _cur[cursor] = new FishState
                    {
                        Pos = ToF3(s.Anchor) + off,
                        Vel = vel,
                        School = si,
                        Phase = rng.NextFloat(0f, math.PI * 2f),
                    };
                    cursor++;
                }

                _render.Add(new SchoolRender
                {
                    Mat = mat,
                    Matrices = new Matrix4x4[s.Count],
                    Start = _schools[si].Start,
                    Count = s.Count,
                    FishLen = fl,
                    PhaseOffset = (si * 37) % 101, // spread frame-skip load (builder.html _lodPh)
                });
            }

            // Whales (one looping GameObject each).
            if (whales != null)
            {
                foreach (WhaleReg w in whales)
                {
                    var go = new GameObject("Whale");
                    go.transform.SetParent(transform, false);
                    var mf = go.AddComponent<MeshFilter>();
                    var mr = go.AddComponent<MeshRenderer>();
                    mf.sharedMesh = _fishMesh;
                    Material wm = baseMat != null ? new Material(baseMat) : new Material(Shader.Find("Standard"));
                    wm.color = w.Color;
                    mr.sharedMaterial = wm;
                    go.transform.localScale = Vector3.one * Mathf.Max(1f, w.Size);
                    var wc = go.AddComponent<WhaleController>();
                    wc.Init(w.Anchor, w.Size);
                    _whaleCount++;
                }
            }

            Debug.Log($"[Marine] configured schools={schools.Count} fish={_fishCount} whale={_whaleCount} obstacles={obN}");
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

            // Build matrices + one instanced draw per school.
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
                    mats[k] = Matrix4x4.TRS(pos, rot, Vector3.one * sr.FishLen);
                }

                var rp = new RenderParams(sr.Mat)
                {
                    worldBounds = new Bounds(ToV3(_schools[si].Anchor),
                                             Vector3.one * (_schools[si].HomeR * 2f + sr.FishLen * 6f)),
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = false,
                };
                if (sr.Count > 0)
                    Graphics.RenderMeshInstanced(rp, _fishMesh, 0, mats, sr.Count);
            }

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

        // ── conversions ───────────────────────────────────────────────────────────
        private static float3 ToF3(Vector3 v) => new float3(v.x, v.y, v.z);
        private static Vector3 ToV3(float3 v) => new Vector3(v.x, v.y, v.z);
    }
}
