using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace DiveMap.Runtime.Marine
{
    /// <summary>Per-fish simulation state (blittable, Burst-friendly). World space.</summary>
    public struct FishState
    {
        public float3 Pos;
        public float3 Vel;
        public int    School;   // index into the schools array
        public float  Phase;    // per-instance wiggle / wander phase (0..2π)
    }

    /// <summary>
    /// Per-school parameters. <see cref="Think"/> and <see cref="Avoid"/> are recomputed
    /// on the main thread every frame from the school's distance to the camera (LOD),
    /// so a far school dead-reckons instead of re-running the O(n) neighbour scan.
    /// </summary>
    public struct SchoolParams
    {
        public float3 Anchor;
        public float  FishLen;
        public float  HomeR;      // hard radius the school is pulled back into
        public float  NeighborR;  // alignment/cohesion perception radius
        public float  SepR;       // separation radius
        public float  MaxSpeed;   // cruise speed (units/sec)
        public float  VertHalf;   // half-height of the flat formation slab (web schools are pancakes)
        public float  CapY;       // hard ceiling: waterLevel − fishLen (builder.html capY, line 1725)
        public int    Start;      // first fish index for this school
        public int    Count;      // fish in this school
        public byte   Think;      // 1 = run full boids this frame, 0 = dead-reckon
        public byte   Avoid;      // 1 = run solid-avoidance, 0 = skip (far LOD)

        // ── C5: fear. All four are recomputed on the main thread every frame from
        // DiveMap.Core.FleeMath; the job only applies them. Panic 0 = the calm path,
        // bit-for-bit what it was before C5 landed.
        public float  Panic;      // 0..1 (FleeMath.SchoolPanic)
        public float3 Threat;     // world position of whatever is frightening them
        public float  FleeW;      // steering weight straight away from Threat
        public float  DartMul;    // speed multiplier while fleeing (1 = cruise)
    }

    /// <summary>One solid obstacle as a world-space AABB (the wreck/decor fish steer around).</summary>
    public struct ObstacleBox
    {
        public float3 Min;
        public float3 Max;
        public float  ObsR;
    }

    /// <summary>
    /// Boids step for every fish, in parallel, Burst-compiled (WO-XR-03). Reads a
    /// read-only snapshot (<see cref="Src"/>) and writes the next state to <see cref="Dst"/>
    /// (double-buffered so neighbours all see the same frame). Mirrors
    /// <see cref="DiveMap.Core.MarineMath"/>: forward-only heading integration with the
    /// ±turn-rate cap, real-delta <c>Dt = 0.016·FS</c>, the home-radius pull, the
    /// vertical ≤ 0.55× clamp, and the v.0680 smooth solid-avoidance steer.
    /// </summary>
    [BurstCompile]
    public struct BoidsJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<FishState>   Src;
        [ReadOnly] public NativeArray<SchoolParams> Schools;
        [ReadOnly] public NativeArray<ObstacleBox>  Obstacles;
        [WriteOnly] public NativeArray<FishState>   Dst;

        public float Dt;          // 0.016 · FS  (seconds this step, real-delta scaled)
        public float Fs;          // FS scale (for the per-frame turn-rate cap)
        public float Time;        // wall-clock time (wander/wiggle phase)

        public float WSep;
        public float WAlign;
        public float WCoh;
        public float WAnchor;
        public float WWander;
        public float TurnCap;     // rad/frame at FS=1 (0.045)

        public void Execute(int i)
        {
            FishState f = Src[i];
            SchoolParams s = Schools[f.School];

            // ── LOD dead-reckoning: skip the neighbour scan, glide along velocity ──
            if (s.Think == 0)
            {
                float3 dp = f.Pos + f.Vel * Dt;
                float3 dv = f.Vel;
                ClampHome(ref dp, s);
                ClampVertical(ref dp, ref dv, s);
                f.Pos = dp;
                f.Vel = dv;
                Dst[i] = f;
                return;
            }

            // ── Reynolds aggregates over school-mates ─────────────────────────────
            float3 sep = float3.zero, ali = float3.zero, coh = float3.zero;
            int n = 0;
            int end = s.Start + s.Count;
            for (int j = s.Start; j < end; j++)
            {
                if (j == i) continue;
                FishState o = Src[j];
                float3 d = f.Pos - o.Pos;
                float dist = math.length(d);
                if (dist < s.SepR && dist > 1e-4f) sep += d / (dist * dist);
                if (dist < s.NeighborR)
                {
                    ali += o.Vel;
                    coh += o.Pos;
                    n++;
                }
            }

            float3 steer = float3.zero;
            if (n > 0)
            {
                ali /= n;
                coh = coh / n - f.Pos;
                steer += math.normalizesafe(ali) * WAlign;
                steer += math.normalizesafe(coh) * WCoh;
            }
            steer += math.normalizesafe(sep) * WSep;

            // Anchor pull: keep the school near its placed item (grows past HomeR).
            float3 toAnchor = s.Anchor - f.Pos;
            float ad = math.length(toAnchor);
            if (ad > 1e-4f)
            {
                float pull = WAnchor * (ad > s.HomeR ? 1f + (ad - s.HomeR) / s.HomeR : ad / s.HomeR * 0.15f);
                steer += (toAnchor / ad) * pull;
            }

            // Deterministic wander (no RNG in Burst): rotate a phase-driven vector.
            float wph = f.Phase + Time * 0.7f;
            steer += new float3(math.cos(wph), math.sin(wph * 0.5f) * 0.4f, math.sin(wph)) * WWander;

            // Pancake pull: the web's schools are FLAT (shoal milling box y ∈ ±0.4·SR,
            // cluster scatter ±0.275·R). Without this the boids' free vertical ease turns
            // a 66-unit shoal into a 66-unit ball, which reads nothing like the web.
            if (s.VertHalf > 1e-4f)
            {
                float dyA = f.Pos.y - s.Anchor.y;
                if (math.abs(dyA) > s.VertHalf) steer.y -= math.sign(dyA) * 2.0f;
            }

            // ── C5: burst away from the threat (the web's schoolFlee, builder.html:1628) ──
            // Radial, in the XZ plane, weighted by fear. The web offsets the fish's position;
            // with velocity boids the same read comes from steering hard outward and swimming
            // faster (SchoolParams.DartMul below), which also keeps the shoal's own separation
            // and alignment intact so it bursts rather than shatters.
            if (s.Panic > 0.001f && s.FleeW > 0f)
            {
                float ax = f.Pos.x - s.Threat.x;
                float az = f.Pos.z - s.Threat.z;
                float ad2 = math.sqrt(ax * ax + az * az);
                if (ad2 > 1e-4f)
                {
                    steer.x += ax / ad2 * s.FleeW;
                    steer.z += az / ad2 * s.FleeW;
                }
            }

            // Desired heading in the XZ plane (dir = (cos h, sin h) → (x, z)).
            float3 desiredVel = f.Vel + steer * Dt;
            float curH = math.atan2(f.Vel.z, f.Vel.x);
            float desH = math.atan2(desiredVel.z, desiredVel.x);

            // ── Smooth solid-avoidance (overrides the desired heading) ────────────
            if (s.Avoid == 1)
            {
                for (int k = 0; k < Obstacles.Length; k++)
                {
                    ObstacleBox b = Obstacles[k];
                    if (TrySolidAvoid(f.Pos, curH, b, false, out float th))
                        desH = th;
                }
            }

            // Forward-only integration with the per-frame turn-rate cap (·FS). A frightened fish
            // may turn harder — at the cruise cap alone a startled fish needs over a second to
            // come about, which reads as indifference rather than fear (FleeMath.TurnCapScale).
            float cap = TurnCap * Fs * (1f + 2f * s.Panic);
            float newH = TurnTowardBurst(curH, desH, cap);

            float speed = s.MaxSpeed * (s.DartMul > 0f ? s.DartMul : 1f);
            float3 nv;
            nv.x = math.cos(newH) * speed;
            nv.z = math.sin(newH) * speed;

            // Vertical ease toward cohesion/desired Y, clamped to 0.55× forward.
            float vc = speed * 0.55f;
            float vy = desiredVel.y;
            vy = math.clamp(vy, -vc, vc);
            nv.y = vy;

            float3 np = f.Pos + nv * Dt;
            ClampHome(ref np, s);
            ClampVertical(ref np, ref nv, s);

            f.Pos = np;
            f.Vel = nv;
            Dst[i] = f;
        }

        // ── helpers (mirror MarineMath) ───────────────────────────────────────────

        private static void ClampHome(ref float3 p, in SchoolParams s)
        {
            float dx = p.x - s.Anchor.x;
            float dz = p.z - s.Anchor.z;
            float rr = math.sqrt(dx * dx + dz * dz);
            if (rr > s.HomeR && rr > 1e-4f)
            {
                float kk = s.HomeR / rr;
                p.x = s.Anchor.x + dx * kk;
                p.z = s.Anchor.z + dz * kk;
            }
        }

        /// <summary>
        /// Keep a fish inside its school's flat slab (|y − anchor.y| ≤ VertHalf) and below
        /// the surface ceiling (CapY = waterLevel − fishLen, builder.html line 1725). Both
        /// zero the offending vertical velocity so a fish parked on a limit stops pushing.
        /// </summary>
        private static void ClampVertical(ref float3 p, ref float3 v, in SchoolParams s)
        {
            if (s.VertHalf > 1e-4f)
            {
                float dy = p.y - s.Anchor.y;
                if (dy > s.VertHalf)
                {
                    p.y = s.Anchor.y + s.VertHalf;
                    if (v.y > 0f) v.y = 0f;
                }
                else if (dy < -s.VertHalf)
                {
                    p.y = s.Anchor.y - s.VertHalf;
                    if (v.y < 0f) v.y = 0f;
                }
            }
            if (p.y > s.CapY)
            {
                p.y = s.CapY;
                if (v.y > 0f) v.y = 0f;
            }
        }

        private static float TurnTowardBurst(float current, float desired, float maxStep)
        {
            float d = math.atan2(math.sin(desired - current), math.cos(desired - current));
            d = math.clamp(d, -maxStep, maxStep);
            return current + d;
        }

        /// <summary>float3 mirror of <see cref="DiveMap.Core.MarineMath.SolidAvoid"/> (fish only).</summary>
        private static bool TrySolidAvoid(float3 pos, float heading, in ObstacleBox b, bool big, out float targetHeading)
        {
            targetHeading = heading;
            float avoidR = b.ObsR + (big ? 18f : 11f);
            if (pos.y > b.Max.y + b.ObsR) return false;

            float cx = math.clamp(pos.x, b.Min.x, b.Max.x);
            float cz = math.clamp(pos.z, b.Min.z, b.Max.z);
            float sx = pos.x - cx, sz = pos.z - cz;
            float sd = math.sqrt(sx * sx + sz * sz);
            if (sd >= avoidR) return false;

            if (sd < 0.001f) { sx = math.cos(heading); sz = math.sin(heading); sd = 1f; }

            float outDir = math.atan2(sz, sx);
            float s1 = outDir + math.PI / 2f;
            float s2 = outDir - math.PI / 2f;
            float d1 = math.abs(math.atan2(math.sin(s1 - heading), math.cos(s1 - heading)));
            float d2 = math.abs(math.atan2(math.sin(s2 - heading), math.cos(s2 - heading)));
            float tang = d1 < d2 ? s1 : s2;

            float cl = math.min(1f, (avoidR - sd) / avoidR);
            float bx = math.cos(tang) * (1f - cl * 0.6f) + math.cos(outDir) * cl * 0.6f;
            float bz = math.sin(tang) * (1f - cl * 0.6f) + math.sin(outDir) * cl * 0.6f;
            targetHeading = math.atan2(bz, bx);
            return true;
        }
    }
}
