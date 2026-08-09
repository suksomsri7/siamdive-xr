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
        /// <summary>
        /// Where the fish is POINTING, in the web's own convention: motion is
        /// <c>(sin Head, cos Head)</c> in (x, z), i.e. <c>Head = atan2(x, z)</c>
        /// (builder.html <c>_fwdSwim</c>, :1585-1620).
        ///
        /// 🔴 Kept separately from <see cref="Vel"/> and not derived from it. On the web's school
        /// path a fish that has reached its slot has a speed of very nearly zero, and
        /// <c>atan2</c> of a near-zero vector is noise — the heading would jitter exactly when the
        /// school is at its calmest, which is the one place the eye is looking.
        /// </summary>
        public float  Head;
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

        // ── WO-F2: the web's SLOT FORMATION (Core/SchoolFormation.cs) ──────────────
        // The web runs no boids on a school at all. Every fish owns a slot, recomputed on the
        // main thread into SlotPos/SlotDir, and swims to it at min(cap, distance × chaseK) — so a
        // fish that has arrived SLOWS DOWN. Boids at a constant MaxSpeed cannot, which is why 160
        // barracuda came back from a real iPhone stretched into a line.
        /// <summary>1 = slot formation (every <c>school:*</c>), 0 = the Reynolds path (pods).</summary>
        public byte   Formation;
        /// <summary>1 = the web's CALM path (builder.html :1591-1600) — <c>school:barracuda</c>.</summary>
        public byte   Calm;
        /// <summary>Slot-chase gain per frame, <c>easeL × 2.2</c> plus the flee ease (:1761).</summary>
        public float  ChaseK;
        /// <summary>Cruise cap per FRAME (= MaxSpeed / 60), so the web's arithmetic transcribes 1:1.</summary>
        public float  CapPerFrame;
        /// <summary>Cruise floor per frame, <c>flen × 0.005</c> (:1610). Zero on the calm path.</summary>
        public float  CruiseFloor;
        /// <summary>Settle band: past it steer at the slot, inside it take the formation's heading (:1601).</summary>
        public float  SettleD;
        /// <summary>
        /// The web's safety wall — R×3.2 for a formation, SR×2.6 for a shoal (:1597, :1611).
        ///
        /// 🔴 NOT <see cref="HomeR"/>, which is R×1.2. The slots themselves reach past that: a
        /// cluster's corner sits at R√2 and a stream strings fish out to 2R along its heading.
        /// Clamping a formation to 1.2R would squash the very shape it is trying to hold.
        /// </summary>
        public float  SafeR;
        /// <summary>
        /// Hard floor for a formation fish, the flat stand-in for the web's <c>_fishFloor</c>
        /// (builder.html :1676, which samples the real seabed height per fish).
        ///
        /// 🔴 Needed because the slot formation is not a pancake. The boids path clamped every
        /// fish to ±VertHalf (= R×0.275) of the anchor; a <c>ball</c> reaches 0.47 R below it and
        /// a <c>tornado</c> 1.8 R above, so the old clamp would have flattened the shapes and
        /// removing it lets a ball dip into the sand. Same datum the school's own mind already
        /// uses for its domain floor (FishMind.SchoolDomain, minY = VertHalf + FishLen).
        /// </summary>
        public float  FloorY;

        /// <summary>
        /// How far the nose may lean off the school's heading toward the direction the fish is
        /// actually travelling, in radians (<see cref="DiveMap.Core.SchoolFormation.CalmNoseCapRad"/>).
        ///
        /// A field rather than a constant ONLY so the QC clip can photograph the three candidates
        /// side by side in one CI round — 0 = the old build (nose welded to the school heading,
        /// the fish crabs), π = the nose follows completely. The user picks from the footage.
        /// </summary>
        public float  NoseCap;
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

        /// <summary>
        /// WO-F2 — this fish's slot this frame, in WORLD units, and the heading the FORMATION
        /// wants it on (xz; (0,0) = "no opinion", which is what a milling shoal has).
        ///
        /// Filled on the main thread by <c>FishSchoolSystem.BuildSlots</c> from
        /// <see cref="DiveMap.Core.SchoolFormation"/> rather than inside the job: the slot is
        /// ~8 trig calls per fish per frame (≈0.3 ms for the whole reef), the mode wheel and the
        /// old→new morph are per-SCHOOL state that would have to be marshalled in anyway, and
        /// keeping the formation maths in Core is what lets <c>tools/test.sh</c> pin it against
        /// builder.html in two seconds instead of a 35-minute CI round.
        /// </summary>
        [ReadOnly] public NativeArray<float3> SlotPos;
        [ReadOnly] public NativeArray<float3> SlotDir;

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

            // ── WO-F2: the web's school. Not boids — a slot and a spring. ──────────
            if (s.Formation != 0)
            {
                FormationStep(i, ref f, s);
                Dst[i] = f;
                return;
            }

            // ── LOD dead-reckoning: skip the neighbour scan, glide along velocity ──
            if (s.Think == 0)
            {
                float3 dp = f.Pos + f.Vel * Dt;
                float3 dv = f.Vel;
                ClampHome(ref dp, ref dv, s);
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
            float dTurn = DeltaAngle(desH, curH);
            float newH = math.abs(dTurn) > 0.025f ? TurnTowardBurst(curH, desH, cap) : curH;

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
            ClampHome(ref np, ref nv, s);
            ClampVertical(ref np, ref nv, s);

            f.Pos = np;
            f.Vel = nv;
            Dst[i] = f;
        }

        // ── WO-F2: builder.html `_fwdSwim` (:1585-1620), transcribed ──────────────
        //
        // 🔴 Mirrored in float rather than calling DiveMap.Core.SchoolFormation, for the same
        // reason the rest of this job mirrors MarineMath: Burst compiles this method and the Core
        // one is double-precision managed C#. The Core version is the one a test can reach, so
        // every constant below is named there (SchoolFormation.TurnCapPerFrame, .CalmChasePerFrame,
        // …) and quoted here — if the two ever disagree, the Core file and its tests are right.
        //
        // Everything is PER FRAME × Fs, exactly as the web writes it, so the numbers can be read
        // straight off builder.html. Fs is the real-delta scale (dt / 16.667 ms, clamped to
        // [0.5, 2.5]), which is what makes the motion track the wall clock on a phone that is
        // not holding 60 fps.
        private void FormationStep(int i, ref FishState f, in SchoolParams s)
        {
            float3 slot = SlotPos[i];
            float3 dir  = SlotDir[i];

            float ddx = slot.x - f.Pos.x, ddz = slot.z - f.Pos.z;
            float dh  = math.sqrt(ddx * ddx + ddz * ddz);

            // "Polarised" = the formation has an opinion about which way to face (cluster and
            // stream: the SAME way for every fish). A milling shoal does not, and passes (0,0).
            bool  polar = (dir.x * dir.x + dir.z * dir.z) > 1e-6f;
            float onDir = polar ? math.atan2(dir.x, dir.z) : f.Head;

            // The web treats "there is any panic at all" as fleeing (schoolFlee returns null at
            // panic 0, :1629); the flee OFFSET itself is already folded into the slot.
            bool flee = s.Panic > 0.001f;

            float cap = s.CapPerFrame;
            float sp;   // this frame's forward step, world units

            if (s.Calm != 0 && polar && !flee)
            {
                // ── CALM POLARISED (builder.html :1591-1600) ──────────────────────
                // Hold the school's heading and ease into the slot. No forward-only skid and no
                // cruise floor: a barracuda that has arrived stops dead relative to the school.
                float mv = math.min(cap * 1.8f, dh * 0.05f) * Fs;
                float mA = dh > 0.001f ? math.atan2(ddx, ddz) : f.Head;

                // 🔴 "ปลาไถลข้าง" (user, 8 ส.ค. 2026, ยืนยันด้วยภาพจมูก-เทียบ-เส้นทาง):
                // จมูกต้องตามทางที่ตัวไปจริง "เมื่อกำลังไปจริง" และกลับไปถือ heading ของฝูงเมื่อ
                // ถึงช่องแล้ว — วัดได้ว่าโหมด cluster/stream (สองในสามของชีวิตฝูง) ปลาเคลื่อนที่
                // เฉียงจากจมูกตัวเอง 64-75° เกือบตลอดเวลา · ห้ามตัดการไถลข้างทิ้ง (มันคือลักษณะ
                // ของเส้นทางนี้) แค่ให้หัวตาม
                //
                // มิเรอร์ของ SchoolFormation.CalmNoseBlend/CalmNoseTarget เป็น float ด้วยเหตุผล
                // เดียวกับทั้งไฟล์นี้ (Burst คอมไพล์เมธอดนี้ ส่วน Core เป็น managed double) —
                // ค่าคงที่มีชื่ออยู่ที่ Core และเทสตรึงไว้ที่นั่น ถ้าสองฝั่งไม่ตรงกัน Core ถูก
                // …และจำกัดมุมเบนจากแนวฝูงไว้ 30° (SchoolFormation.CalmNoseCapRad) เพราะช่องใน
                // โหมด cluster โคจรเป็นวงกลม — ปล่อยให้จมูกตามเต็มที่ = ฝูงเลิกเป็นระเบียบ
                // (polarisation 1.00 → 0.26) ซึ่งคือสิ่งที่ทั้งพอร์ตนี้มีไว้กันตั้งแต่แรก
                float blend = math.saturate(dh * 0.05f / math.max(cap * 1.8f * 0.25f, 1e-6f));
                float want  = onDir + math.clamp(DeltaAngle(mA, onDir) * blend, -s.NoseCap, s.NoseCap);

                float dCalm = DeltaAngle(want, f.Head);
                // deadband: มุมต่างจิ๋ว = ไม่เลี้ยว — ฆ่าอาการ "ส่ายหัวถี่" (user ชี้ข้อ 2)
                // ที่เกิดจากเป้ากระพริบซ้าย/ขวารอบศูนย์ทุกเฟรม
                if (math.abs(dCalm) > 0.025f)
                    f.Head += math.clamp(dCalm, -0.05f, 0.05f) * Fs;

                float stepX = math.sin(mA) * mv, stepZ = math.cos(mA) * mv;

                // 🔴 "ห้ามว่ายถอยหลัง" (user, build 261). THIS is the path that could, and it is
                // the most visible school on the map: `calm` is barracuda, 200 fish in one shoal.
                //
                // The web moves along mA — the direction to the SLOT — while the nose holds the
                // school's heading (:1592-1593). Those are independent, so a fish whose slot has
                // drifted behind it swims backwards, nose first, for as long as it takes to catch
                // up. On the web that is a handful of frames inside a milling cluster and easy to
                // miss; here it is what a user with an iPhone reported seeing.
                //
                // The reversing COMPONENT is removed and nothing else. Sideways survives, because
                // easing across into a slot is the whole character of this path and a fish that
                // may only travel along its nose would have to swim a circle to move a metre
                // sideways. forward is unit length, so the projection is just the dot product.
                float fx = math.sin(f.Head), fz = math.cos(f.Head);
                float into = fx * stepX + fz * stepZ;
                if (into < 0f) { stepX -= fx * into; stepZ -= fz * into; }

                f.Pos.x += stepX;
                f.Pos.z += stepZ;

                ClampSafe(ref f.Pos, s);

                float dyc = slot.y - f.Pos.y;
                f.Pos.y += math.clamp(dyc * 0.06f, -mv * 0.6f, mv * 0.6f);
                if (f.Pos.y > s.CapY) f.Pos.y = s.CapY;
                if (f.Pos.y < s.FloorY) f.Pos.y = s.FloorY;

                // 🔴 The velocity READOUT must be the motion that actually happened, not the one
                // the nose implies. Every other path sets Vel from Head, which on this path would
                // have reported a confident forward cruise while the fish slid backwards — an
                // oracle that cannot see the bug it exists to catch. So this branch reports its
                // own step, and only the direction agrees with the nose by construction now.
                float calmPerSec = 1f / math.max(Dt, 1e-4f);
                f.Vel.x = stepX * calmPerSec;
                f.Vel.z = stepZ * calmPerSec;
                f.Vel.y = 0f;
                return;
            }
            else
            {
                // ── FORWARD-ONLY chase (builder.html :1600-1616) ──────────────────
                // Far from the slot, steer AT it; inside the settle band, take the formation's
                // heading instead — so a fish in position stops chasing its own bobbing slot and
                // the school's heads line up. Then move along the nose and nowhere else.
                float des = dh > s.SettleD ? math.atan2(ddx, ddz) : onDir;
                float dA  = DeltaAngle(des, f.Head);
                if (math.abs(dA) > 0.025f)   // deadband กันส่ายหัว (user 8 ส.ค. ข้อ 2)
                    f.Head += math.clamp(dA, -0.045f, 0.045f) * (flee ? 1.6f : 1f) * Fs;

                // 🔴 The speed law. min(cap, distance × chaseK) — arriving costs speed.
                sp = (math.min(cap * (flee ? 1.5f : 1f), dh * s.ChaseK) + s.CruiseFloor) * Fs;

                // Move along the nose and nowhere else — no reverse, no side-slip.
                //
                // 🔴 And NO solid avoidance on this path, which is the web's own decision rather
                // than an omission: builder.html's `ejectFromSolids` opens with a bare `return;`
                // and the comment "ยกเลิกการชนสัตว์↔สิ่งกีดขวาง (ตาม user) → ปลาว่ายทะลุเรือ/
                // รูปปั้นได้อิสระ" (:1656). A school fish there swims straight through the wreck.
                // Steering it instead would fight the slot it is trying to reach, and the two
                // together are exactly how the web's earlier attempt piled a shoal against a hull
                // ("ไม่กองเป็นกำแพง", :1611). The pods keep the avoidance — they are single big
                // animals, and it was written for them.
                f.Pos.x += math.sin(f.Head) * sp;
                f.Pos.z += math.cos(f.Head) * sp;

                ClampSafe(ref f.Pos, s);

                float dy = slot.y - f.Pos.y, vc = sp * 0.55f;   // diagonal only, never a lift
                f.Pos.y += math.clamp(dy * 0.08f, -vc, vc);
                if (f.Pos.y > s.CapY) f.Pos.y = s.CapY;
                if (f.Pos.y < s.FloorY) f.Pos.y = s.FloorY;
            }

            // Velocity is a READOUT here, not the state: Head is the state (see FishState.Head).
            // Kept in units/second like every other velocity in the sim, so anything that reads it
            // (the QC line, the drone bubble) gets the same units it always did.
            float perSec = sp / math.max(Dt, 1e-4f);
            f.Vel.x = math.sin(f.Head) * perSec;
            f.Vel.z = math.cos(f.Head) * perSec;
            f.Vel.y = 0f;
        }

        /// <summary>
        /// The web's own safety wall for a school fish (:1597, :1611): squeeze it back inside
        /// <see cref="SchoolParams.SafeR"/> of the anchor. Wide on purpose — it exists to stop a
        /// fish leaving the map, not to hold the formation, which the slots already do.
        /// </summary>
        private static void ClampSafe(ref float3 p, in SchoolParams s)
        {
            if (s.SafeR <= 1e-4f) return;
            float dx = p.x - s.Anchor.x, dz = p.z - s.Anchor.z;
            float rr = math.sqrt(dx * dx + dz * dz);
            if (rr <= s.SafeR || rr < 1e-4f) return;
            float k = s.SafeR / rr;
            p.x = s.Anchor.x + dx * k;
            p.z = s.Anchor.z + dz * k;
        }

        /// <summary>Shortest signed angle <paramref name="from"/> → <paramref name="to"/>, i.e. the web's <c>atan2(sin Δ, cos Δ)</c>.</summary>
        private static float DeltaAngle(float to, float from)
            => math.atan2(math.sin(to - from), math.cos(to - from));

        // ── helpers (mirror MarineMath) ───────────────────────────────────────────

        /// <summary>
        /// 🔴 "ห้ามว่ายถอยหลัง" ภาคสอง (user, 8 ส.ค. 2026: "ปลากะมงว่ายถอยหลัง").
        ///
        /// ตัวเก่าดึงแค่ตำแหน่งกลับเข้าเขตบ้าน แต่ปล่อยความเร็วชี้ออกนอกตามเดิม — pod วาดหน้า
        /// ปลาตามความเร็ว (LookRotation(vel)) ผลคือปลาที่แตะขอบ HomeR หน้าชี้ออกแต่ตัวถูกดึง
        /// ถอย = ไถลถอยหลังทั้งที่จมูกชี้ไปหน้า และ pod วนใกล้ขอบเกือบตลอดเวลา.
        ///
        /// แก้แบบเดียวกับกำแพงในเกมทุกเกม: ชนขอบแล้ว "ลื่นไปตามขอบ" — ตัดเฉพาะองค์ประกอบ
        /// ความเร็วที่พุ่งออกนอกรัศมี เหลือแนวสัมผัสไว้ ปลาจึงเลียบขอบต่อโดยหน้ากับการ
        /// เคลื่อนที่ตรงกันเสมอ (invariant เดียวกับ FormationStep/MarineMath).
        /// </summary>
        private static void ClampHome(ref float3 p, ref float3 v, in SchoolParams s)
        {
            float dx = p.x - s.Anchor.x;
            float dz = p.z - s.Anchor.z;
            float rr = math.sqrt(dx * dx + dz * dz);
            if (rr > s.HomeR && rr > 1e-4f)
            {
                float kk = s.HomeR / rr;
                p.x = s.Anchor.x + dx * kk;
                p.z = s.Anchor.z + dz * kk;

                float nx = dx / rr, nz = dz / rr;              // ทิศออกจากบ้าน (หน่วย)
                float outward = v.x * nx + v.z * nz;
                if (outward > 0f)                              // กำลังพุ่งออก -> เหลือแต่แนวสัมผัส
                {
                    v.x -= nx * outward;
                    v.z -= nz * outward;
                }
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
