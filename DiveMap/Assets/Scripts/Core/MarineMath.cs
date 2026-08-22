using System;

namespace DiveMap.Core
{
    /// <summary>
    /// Pure, UnityEngine-free marine-motion math (WO-XR-03). This is the single source
    /// of truth for the behaviour ported from the web builder.html marine system
    /// (v.0678-0680 / current v.0730). The Burst boids job (DiveMap.Runtime) mirrors
    /// these exact formulas in <c>float3</c>; the whale controller calls them directly.
    ///
    /// Everything here is deterministic and unit-testable in EditMode — the same
    /// discipline as <see cref="WebCoord"/>. The regression-prone rules (real-delta
    /// scaling, the whale pitch/no-roll orientation, and the smooth solid-avoidance
    /// steer) live here so a test pins them and they cannot silently drift.
    ///
    /// Ported constants (builder.html line refs):
    ///   • FS real-delta clamp [0.5, 2.5], base dt 0.016, ms base 16.667   (lines 3876-3879)
    ///   • turn cap ±0.045 rad/frame (calm ±0.05)                          (line 1603)
    ///   • vertical ≤ 0.55× forward speed                                  (line 1607)
    ///   • solid avoidR = obsR + (big?18:11), steer (big?0.11:0.2)*(0.4+0.6*cl)  (lines 2392-2405)
    ///   • whale pitch clamp ±0.5 rad, roll = live bank only (zeroed at placement) (lines 2454-2481, 2070-2072)
    ///   • LOD bands dN=140, dM=340 → stepEvery 1 / 2 / 6                   (lines 3923-3936)
    /// </summary>
    public static class MarineMath
    {
        // ── Real-delta time scale (FS) ────────────────────────────────────────────
        public const double BaseStep = 0.016;   // web base timestep (seconds)
        public const double MsBase   = 16.667;  // one 60Hz frame in ms
        public const double FsMin    = 0.5;
        public const double FsMax    = 2.5;

        /// <summary>
        /// Frame-scale FS = clamp(dt / 0.016667, 0.5, 2.5). All per-frame motion is
        /// multiplied by this so movement tracks wall-clock time even at low/variable
        /// FPS (builder.html line 3877, the "real-delta" fix).
        /// </summary>
        public static double RealDeltaScale(double dtSeconds)
        {
            double fs = dtSeconds / (MsBase / 1000.0);
            if (fs < FsMin) return FsMin;
            if (fs > FsMax) return FsMax;
            return fs;
        }

        // ── LOD behaviour throttle (distance bands) ───────────────────────────────
        public const double LodNear = 140.0; // dN at lodQ=0
        public const double LodMid  = 340.0; // dM at lodQ=0

        /// <summary>
        /// How often (in frames) a school should re-run its boids "thinking": every
        /// frame when near, every 2nd frame mid-range, every 6th when far. On skipped
        /// frames the fish dead-reckon along their last velocity (builder.html 3923-3936).
        /// </summary>
        public static int StepEveryForDistance(double dist)
        {
            if (dist < LodNear) return 1;
            if (dist < LodMid)  return 2;
            return 6;
        }

        /// <summary>Far schools skip solid-avoidance entirely (LOD behaviour, WO-XR-03).</summary>
        public static bool AvoidanceActiveForDistance(double dist) => dist < LodMid;

        // ── Heading helpers ───────────────────────────────────────────────────────
        /// <summary>Shortest signed angle from <paramref name="a"/> to <paramref name="b"/> (radians, −π..π).</summary>
        public static double DeltaAngle(double a, double b)
            => Math.Atan2(Math.Sin(b - a), Math.Cos(b - a));

        /// <summary>
        /// Rotate <paramref name="current"/> toward <paramref name="desired"/> by at most
        /// <paramref name="maxStep"/> radians (the ±0.045 rad/frame turn-rate cap, builder.html 1603).
        /// </summary>
        public static double TurnToward(double current, double desired, double maxStep)
        {
            double d = DeltaAngle(current, desired);
            if (d >  maxStep) d =  maxStep;
            if (d < -maxStep) d = -maxStep;
            return current + d;
        }

        // ── Smooth solid-avoidance (builder.html v.0680, lines 2392-2405) ─────────
        /// <summary>
        /// Predictive "steer around the box before you hit it" solver against one AABB
        /// obstacle, in the XZ plane. Heading convention: a direction of angle <c>h</c>
        /// is (cos h, sin h) in (x, z). Returns false (and leaves targetHeading = heading)
        /// when the fish is clear of the box top or outside the avoid radius.
        ///
        /// avoidR = obsR + (big?18:11). Closeness cl = (avoidR-sd)/avoidR blends a
        /// tangential glide (1-0.6·cl) with a straight push-out (0.6·cl) so the fish
        /// grazes the hull when far and bails out hard when about to hit it.
        /// </summary>
        public static bool SolidAvoid(
            double px, double pz, double fishY, double heading,
            double minX, double maxX, double minY, double maxY, double minZ, double maxZ,
            double obsR, bool big,
            out double targetHeading, out double closeness)
        {
            targetHeading = heading;
            closeness = 0.0;

            double avoidR = obsR + (big ? 18.0 : 11.0);
            if (fishY > maxY + obsR) return false; // clear the top → skip

            double cx = Clamp(px, minX, maxX);
            double cz = Clamp(pz, minZ, maxZ);
            double sx = px - cx, sz = pz - cz;
            double sd = Math.Sqrt(sx * sx + sz * sz);
            if (sd >= avoidR) return false;

            if (sd < 0.001) { sx = Math.Cos(heading); sz = Math.Sin(heading); sd = 1.0; }

            double outDir = Math.Atan2(sz, sx);
            double s1 = outDir + Math.PI / 2.0;
            double s2 = outDir - Math.PI / 2.0;
            double d1 = Math.Abs(DeltaAngle(heading, s1));
            double d2 = Math.Abs(DeltaAngle(heading, s2));
            double tang = d1 < d2 ? s1 : s2;

            double cl = (avoidR - sd) / avoidR;
            if (cl > 1.0) cl = 1.0;
            double bx = Math.Cos(tang) * (1.0 - cl * 0.6) + Math.Cos(outDir) * cl * 0.6;
            double bz = Math.Sin(tang) * (1.0 - cl * 0.6) + Math.Sin(outDir) * cl * 0.6;

            targetHeading = Math.Atan2(bz, bx);
            closeness = cl;
            return true;
        }

        // ── Whale / big-animal orientation: pitch-from-velocity, ZERO roll ────────
        public const double DefaultMaxPitch = 0.5; // web clamp ±0.5 rad (line 2472)

        /// <summary>Unity-space Euler orientation (radians). Roll is ALWAYS 0 for swimmers.</summary>
        public readonly struct Orientation
        {
            public readonly double PitchRad; // Unity rotation.x
            public readonly double YawRad;   // Unity rotation.y
            public readonly double RollRad;  // Unity rotation.z — structurally 0 (no-roll rule)
            public Orientation(double pitch, double yaw, double roll)
            {
                PitchRad = pitch; YawRad = yaw; RollRad = roll;
            }
        }

        /// <summary>
        /// Orientation for a large free-swimming animal from its velocity (Unity space):
        /// yaw follows the travel direction, pitch follows the climb/dive angle (clamped
        /// ±maxPitch), and roll is FORCED to 0. This is the anti-regression form of the
        /// web rule (builder.html 2454-2481 + the placement reset 2070-2072 that zeroes
        /// baseRotX/rotation.z): a swimmer never inherits or accumulates roll. Because
        /// RollRad is a literal 0, banking can never regress into a stuck barrel-roll.
        ///
        /// Sign: climbing (vel.y &gt; 0) gives nose-up = NEGATIVE Unity rotation.x.
        /// </summary>
        public static Orientation OrientationFromVelocity(double vx, double vy, double vz, double maxPitch)
        {
            double horiz = Math.Sqrt(vx * vx + vz * vz);
            double speed = Math.Sqrt(vx * vx + vy * vy + vz * vz);
            if (speed < 1e-9) return new Orientation(0, 0, 0);

            double yaw = Math.Atan2(vx, vz);        // Unity yaw: 0 faces +Z
            double pitch = -Math.Atan2(vy, horiz);  // nose-up when climbing
            if (pitch >  maxPitch) pitch =  maxPitch;
            if (pitch < -maxPitch) pitch = -maxPitch;
            return new Orientation(pitch, yaw, 0.0);
        }

        public static Orientation OrientationFromVelocity(double vx, double vy, double vz)
            => OrientationFromVelocity(vx, vy, vz, DefaultMaxPitch);

        // ── The no-reversing invariant (user, build 261: "ห้ามว่ายถอยหลัง") ────────
        //
        // 🔴 One rule for the whole reef: an animal's NOSE must never point against the way it is
        // actually moving. dot(forward, velocity) > 0, everywhere, for schools and shoals and pods
        // and hero animals alike.
        //
        // It is not a style note. Nothing in the sea reverses — a fish that needs to go the other
        // way turns, and turning takes a radius and takes time. A body sliding backwards while its
        // head points forwards is the single most reliable way to make an expensive model look
        // like a sprite, and a still screenshot cannot show it, which is why it survived to a
        // real device.
        //
        // The web enforces this STRUCTURALLY on its shoal/formation path — `_fwdSwim`
        // (builder.html:1584-1586) keeps a persistent `fi.head`, caps the turn at ±0.045/frame and
        // then translates along the head and nowhere else, so backwards is unrepresentable.
        //
        // Two of the web's OTHER paths do not have that property, and it is worth writing down
        // which of them this port inherited:
        //   • the web's pods use `faceVel` (:1627-1630), an unconditional 0.12 lerp toward the
        //     travel direction, over a position lerp toward a slot — so a pod animal whose slot is
        //     behind it rotates in place while translating backwards. This port did NOT inherit
        //     that: BoidsJob's Reynolds path rebuilds velocity from a turn-capped heading and
        //     advances position along that same velocity, so pods are safe by construction.
        //   • the CALM polarised path (:1592-1593) turns the nose toward the school's heading and
        //     moves the body toward the slot as two independent quantities. That one this port DID
        //     inherit, it is `school:barracuda` — 200 fish in tight formation, the most-looked-at
        //     shoal on the map — and it is what the build-261 report is describing. It is closed in
        //     BoidsJob.FormationStep, mirrored for test in SchoolFormation.ForwardOnlyCalmStep.

        /// <summary>
        /// How well a facing agrees with a motion: <c>dot(forward̂, velocitŷ)</c>, in −1…1.
        ///
        /// A stopped animal returns 1, not 0. "Not moving" is not "moving backwards", and an
        /// invariant that fires on a hovering fish is an invariant that gets switched off.
        /// </summary>
        public static double HeadingDot(Vec3 forward, Vec3 vel)
        {
            double fl = Math.Sqrt(forward.X * forward.X + forward.Y * forward.Y + forward.Z * forward.Z);
            double vl = Math.Sqrt(vel.X * vel.X + vel.Y * vel.Y + vel.Z * vel.Z);
            if (vl < 1e-9) return 1.0;      // hovering — see remarks
            if (fl < 1e-9) return 0.0;      // no facing at all is a real failure, and reads as one
            double d = (forward.X * vel.X + forward.Y * vel.Y + forward.Z * vel.Z) / (fl * vl);
            return d > 1.0 ? 1.0 : (d < -1.0 ? -1.0 : d);
        }

        /// <summary>The invariant itself. False is a bug, not a mood.</summary>
        public static bool SwimsForward(Vec3 forward, Vec3 vel) => HeadingDot(forward, vel) > 0.0;

        /// <summary>
        /// The invariant in the HORIZONTAL plane — and this, not the 3-D form, is what the runtime
        /// oracles assert.
        ///
        /// 🔴 The reason is a real animal on this reef, not tidiness. A crab, a seahorse, a clam
        /// and a garden eel are drawn by <c>WhaleController</c>'s stationary path: their facing is
        /// a fixed yaw and their only motion is a vertical sway. Their 3-D <see cref="HeadingDot"/>
        /// is therefore exactly 0.0 forever, and a strict <c>&gt; 0</c> rule would report every
        /// stationary animal in the map as swimming backwards, every frame — which is how an
        /// invariant gets a <c>// TODO: too noisy</c> next to it and stops protecting anything.
        ///
        /// Heading is a YAW everywhere in this project; pitch is a decoration on top of it, clamped
        /// to ±0.5 rad so it can never dominate. So the question "is this animal reversing" is a
        /// question about x and z, and rising or sinking is not an answer to it.
        ///
        /// A fish with no horizontal motion at all returns 1.0, for the same reason a stopped one
        /// does in <see cref="HeadingDot"/>: hovering is not reversing.
        /// </summary>
        public static double HeadingDotXZ(Vec3 forward, Vec3 vel)
        {
            double fl = Math.Sqrt(forward.X * forward.X + forward.Z * forward.Z);
            double vl = Math.Sqrt(vel.X * vel.X + vel.Z * vel.Z);
            if (vl < 1e-9) return 1.0;      // hovering, or bobbing straight up — see remarks
            if (fl < 1e-9) return 0.0;
            double d = (forward.X * vel.X + forward.Z * vel.Z) / (fl * vl);
            return d > 1.0 ? 1.0 : (d < -1.0 ? -1.0 : d);
        }

        /// <summary>
        /// The runtime invariant. <paramref name="tolerance"/> is not slack in the rule, it is slack
        /// in the MEASUREMENT: a frame's displacement is a difference of two floats over a small
        /// dt, and a fish easing sideways into its slot produces a dot that hovers around 0 with
        /// float noise on it. The oracles pass −0.05, so anything genuinely reversing (a real one
        /// runs to −1) still trips it and a rounding error does not.
        /// </summary>
        public static bool SwimsForwardXZ(Vec3 forward, Vec3 vel, double tolerance = 0.0)
            => HeadingDotXZ(forward, vel) > -Math.Abs(tolerance);

        /// <summary>
        /// Enforce the invariant on ONE frame's displacement: remove whatever part of
        /// <paramref name="step"/> points behind <paramref name="forward"/>, and leave the rest.
        ///
        /// 🔴 Only the BACKWARDS component goes. Sideways survives on purpose — a real fish does
        /// slip outward through a hard turn, the web's pods do drift across their slots, and a
        /// version of this that projected the step onto the nose axis alone would weld every
        /// animal to a rail and undo the formation work. What is removed is the thing that cannot
        /// happen in water: the tail leading.
        ///
        /// A caller whose facing is derived FROM its velocity (<see cref="OrientationFromVelocity"/>,
        /// and the whole <c>_fwdSwim</c> path) can never trip this and pays only a dot product.
        /// It is the pod lerp and anything that chases a target position directly that need it.
        /// </summary>
        public static Vec3 ForwardOnlyStep(Vec3 forward, Vec3 step)
        {
            double fl2 = forward.X * forward.X + forward.Y * forward.Y + forward.Z * forward.Z;
            if (fl2 < 1e-18) return step;   // no opinion about facing ⇒ nothing to enforce
            double d = forward.X * step.X + forward.Y * step.Y + forward.Z * step.Z;
            if (d >= 0.0) return step;      // already forwards — the overwhelmingly common case
            double k = d / fl2;             // the offending component, in units of `forward`
            return new Vec3(step.X - forward.X * k,
                            step.Y - forward.Y * k,
                            step.Z - forward.Z * k);
        }

        /// <summary>
        /// The fish's local +X ("right") axis in world space for a given Unity Euler
        /// (ZXY order, matching UnityEngine.Quaternion.Euler). With roll = 0 the Y
        /// component is identically 0 — the no-roll invariant tests assert exactly this,
        /// so a future edit that reintroduces roll (banking that sticks) is caught.
        /// </summary>
        public static Vec3 RightVector(Orientation o)
        {
            double x = o.PitchRad, y = o.YawRad, z = o.RollRad;
            // right = Ry(y) * Rx(x) * Rz(z) * (1,0,0)
            // Rz(z)*(1,0,0) = (cos z, sin z, 0)
            double rx = Math.Cos(z), ry = Math.Sin(z), rz = 0.0;
            // Rx(x): rotate about X
            double cx = Math.Cos(x), sx = Math.Sin(x);
            double ax = rx;
            double ay = ry * cx - rz * sx;
            double az = ry * sx + rz * cx;
            // Ry(y): rotate about Y  (x' = x·cos + z·sin, z' = -x·sin + z·cos)
            double cy = Math.Cos(y), sy = Math.Sin(y);
            double wx = ax * cy + az * sy;
            double wy = ay;
            double wz = -ax * sy + az * cy;
            return new Vec3(wx, wy, wz);
        }

        // ── School formation geometry (builder.html buildSchool, lines 1490-1526) ──
        //
        // The web builds a school by loading a GLB that contains ONE fish, measuring its
        // max AABB dimension (flen, "local" web units), then laying N=asset.school copies
        // out around the group origin. The GROUP is then scaled by item.s straight from the
        // saved map (loadMap line 3264-3266: buildSchool(gltf,a) → g.scale.fromArray(it.s)),
        // so every span below is  local formula × item.s  in world units.
        //
        //   shoal   SR   = flen × (3 + ∛N × 1.6)                       (line 1497)
        //   cluster R    = flen × max(2.8, N×0.07) × formR             (line 1496)
        //   pod     podR = animalW × 0.72 × √N,  animalW = 16×defaultScale (1499-1502)
        //
        // Vertical extents (the web schools are flat pancakes, never spheres):
        //   shoal   y ∈ ±0.4·SR   (runtime milling box, line 1727-1735)
        //   cluster y ∈ ±0.275·R  (initial scatter, line 1524)
        //   pod     y ∈ ±0.25·podR (golden-disc slot, line 1523)
        //
        // Speed caps are per-FRAME in the web (_fwdSwim), so ×60 gives units/second:
        //   shoal      flen × 0.04  × swimMul  (line 1738)
        //   formation  flen × 0.065 × swimMul  (line 1765)
        //
        // Unity holds fewer boids than the web holds fish (mobile Burst budget), but the
        // SPAN must still use the WEB N — otherwise the shoal shrinks to a marble.

        public const double SchoolFrameRate        = 60.0;  // web per-frame caps → per-second
        public const double ShoalSpeedPerFrame     = 0.04;

        /// <summary>
        /// 🔴 "ฝูงปลาข้างเหลือง ระยะห่างระหว่างตัวมากไป ทำให้ฝูงเล็กลงแน่นขึ้น และทำให้ปลา
        /// เคลื่อนที่ช้าลง" (user 9 ส.ค.) — สองตัวคูณสำหรับฝูงแบบ Shoal เท่านั้น
        ///
        /// รัศมีฝูงมาจากสูตรเว็บ flen×(3+∛N×1.6) ซึ่งกาง 500 ตัวไว้ แต่แอปวาดจริงแค่ 120 ตัว
        /// (UnityCount) — ความหนาแน่นจึงบางกว่าเว็บโดยโครงสร้าง ไม่ใช่ความรู้สึกของ user
        /// ย่อรัศมี 0.6 ⇒ ปริมาตรเหลือ ~22% = แน่นขึ้นราว 4.6 เท่าที่จำนวนตัวเท่าเดิม
        /// </summary>
        public const double ShoalTightenMul = 0.6;
        /// <summary>…และว่ายช้าลงตามที่ user ขอ (ไม่แตะจังหวะหาง ซึ่งเป็นคนละเรื่อง).</summary>
        public const double ShoalSpeedUserMul = 0.6;
        public const double FormationSpeedPerFrame = 0.065;
        public const double PodSpeedPerFrame       = 0.02;  // pods hold slots + bob, they don't dash

        /// <summary>
        /// 🔴 22 ส.ค. 2026 — user (เรื่องฝูงปลากะมง = <c>pod:yellowtail</c>):
        /// "ควรปรับให้ว่ายน้ำช้า เคลื่อนที่ไปข้างหน้าช้าลงอีกนิด"
        ///
        /// เส้นทาง Pod ไม่เคยมีตัวคูณของ user เลย — ตอนที่ Shoal ได้
        /// <see cref="ShoalSpeedUserMul"/> ไปเมื่อ 9 ส.ค. pod ถูกข้ามไป เพราะรอบนั้น user พูดถึง
        /// "ฝูงปลาข้างเหลือง" ซึ่งตอนนั้นยังเดินเส้น Shoal อยู่ ⇒ ไม่ใช่ค่าที่ตั้งผิด แต่เป็นช่องที่
        /// ยังไม่มีใครเปิด
        ///
        /// 🔴 ลดที่ความเร็ว**เดินทาง**เท่านั้น ไม่แตะจังหวะหาง (<see cref="SwimStyle"/>) —
        /// เป็นคนละเรื่องกัน และการสับสนสองอย่างนี้คือสิ่งที่ทำให้เสียไปสามคืนเมื่อ 8-9 ส.ค.
        /// (ดู FISH_TUNING.md §6)
        /// </summary>
        public const double PodSpeedUserMul        = 0.75;  // ช้าลง 25%
        public const double ShoalVertFactor        = 0.40;
        public const double ClusterVertFactor      = 0.275;
        public const double PodVertFactor          = 0.25;
        public const double SchoolRadiusFloorMul   = 8.0;   // school:* floor = 8 × fish length
        public const double PodRadiusFloorMul      = 3.0;   // pod:*    floor = 3 × animal length
        public const double PodAnimalLenMul        = 16.0;  // animalW = 16 × defaultScale (line 1499)

        public enum SchoolFormation
        {
            Cluster = 0, // default vortex/ball formation (barracuda)
            Shoal   = 1, // loose flat aggregation (scad, batfish)
            Pod     = 2, // few big animals on a golden-disc (yellowtail, dolphins)
        }

        /// <summary>shoal radius SR = flen × (3 + ∛N × 1.6) — builder.html line 1497 (local units).</summary>
        public static double ShoalRadius(double fishLenLocal, int webCount)
        {
            if (webCount < 1) webCount = 1;
            return fishLenLocal * (3.0 + Math.Pow(webCount, 1.0 / 3.0) * 1.6);
        }

        /// <summary>formation radius R = flen × max(2.8, N×0.07) × formR — line 1496 (local units).</summary>
        public static double FormationRadius(double fishLenLocal, int webCount, double formR)
        {
            if (webCount < 1) webCount = 1;
            double f = formR > 0.0 ? formR : 1.0;
            return fishLenLocal * Math.Max(2.8, webCount * 0.07) * f;
        }

        /// <summary>One pod animal's LOCAL length: animalW = 16 × defaultScale — line 1499-1501.</summary>
        public static double PodAnimalLocalLen(double defaultScale)
            => PodAnimalLenMul * (defaultScale > 0.0 ? defaultScale : 1.0);

        /// <summary>golden-disc pod radius = animalW × 0.72 × √N — line 1502 (local units).</summary>
        public static double PodRadius(double animalLocalLen, int webCount)
        {
            if (webCount < 1) webCount = 1;
            return animalLocalLen * 0.72 * Math.Sqrt(webCount);
        }

        /// <summary>Web per-frame swim cap → units/second (×60), before the item scale.</summary>
        public static double SpeedCapPerSecond(double lenLocal, double perFrameFactor, double swimMul)
            => lenLocal * perFrameFactor * (swimMul > 0.0 ? swimMul : 1.0) * SchoolFrameRate;

        /// <summary>
        /// World length of a big animal placed as a single GLB: the model's own max AABB
        /// dimension × the saved item scale. NO clamp — the map's item.s is authoritative
        /// (a whaleshark at s=34.2 on a 1.908-unit GLB really is ~65 units long).
        /// </summary>
        public static double WhaleWorldLen(double glbMaxDim, double itemScale)
            => (glbMaxDim > 0.0 ? glbMaxDim : 1.0) * (itemScale > 0.0 ? itemScale : 1.0);

        /// <summary>Per-species web catalog row (builder.html MODULES lines 1098-1109).</summary>
        public readonly struct SpeciesSpec
        {
            public readonly string          AssetId;
            /// <summary>Max AABB dimension of ONE fish inside the species GLB (web/local units).</summary>
            public readonly double          FishLenLocal;
            /// <summary>asset.school — the web's fish-per-school count; drives the SPAN.</summary>
            public readonly int             WebCount;
            /// <summary>Boids the Unity player actually simulates (mobile budget).</summary>
            public readonly int             UnityCount;
            public readonly SchoolFormation Formation;
            public readonly double          FormR;
            public readonly double          SwimMul;
            public readonly double          DefaultScale;
            /// <summary>Per-fish size jitter (web: 0.85-1.15 for schools, podMin/podMax for pods).</summary>
            public readonly double          SizeMin;
            public readonly double          SizeMax;

            // ── The rest of the web catalog row, read by the slot formation (WO-F2) ──
            // These five were dropped when this table was first transcribed because boids had
            // nothing to do with them. The web's school does: they are what makes a barracuda
            // hold one shape for half a minute and turn its ring over at a tenth the usual rate.
            /// <summary>asset.modeDurMul — how long a shape is held (barracuda 2.2, builder.html :1098).</summary>
            public readonly double          ModeDurMul;
            /// <summary>asset.transDurMul — how slowly it morphs into the next one (barracuda 2.5).</summary>
            public readonly double          TransDurMul;
            /// <summary>asset.spinMul — ring/column rotation rate (barracuda 0.1, i.e. a tenth).</summary>
            public readonly double          SpinMul;
            /// <summary>asset.easeMul — slot-chase gain multiplier (barracuda 0.8).</summary>
            public readonly double          EaseMul;
            /// <summary>
            /// asset.calm — takes the web's CALM path (builder.html :1591-1600): the fish holds the
            /// school's heading and eases into its slot with no cruise floor, instead of the
            /// forward-only chase. <c>school:barracuda</c> is the only one, and it is the school in
            /// the iPhone screenshot.
            /// </summary>
            public readonly bool            Calm;

            public SpeciesSpec(string assetId, double fishLenLocal, int webCount, int unityCount,
                               SchoolFormation formation, double formR, double swimMul,
                               double defaultScale, double sizeMin, double sizeMax,
                               double modeDurMul = 1.0, double transDurMul = 1.0,
                               double spinMul = 1.0, double easeMul = 1.0, bool calm = false)
            {
                AssetId = assetId;
                FishLenLocal = fishLenLocal;
                WebCount = webCount;
                UnityCount = unityCount;
                Formation = formation;
                FormR = formR;
                SwimMul = swimMul;
                DefaultScale = defaultScale;
                SizeMin = sizeMin;
                SizeMax = sizeMax;
                ModeDurMul = modeDurMul;
                TransDurMul = transDurMul;
                SpinMul = spinMul;
                EaseMul = easeMul;
                Calm = calm;
            }
        }

        /// <summary>
        /// Species table. flen values are measured from the real XR GLBs on the CDN
        /// (scad 1.911, barracuda 1.862, trevally 1.899); the rest of each row is the
        /// web catalog verbatim. UnityCount is the mobile boids budget — the demo map
        /// (7 scad + 1 barracuda + 2 pods) sums to 7×120 + 160 + 2×50 = 1,100 fish.
        /// </summary>
        public static SpeciesSpec SpeciesFor(string assetId)
        {
            string a = string.IsNullOrEmpty(assetId) ? "" : assetId.ToLowerInvariant();

            if (a.StartsWith("school:scad", StringComparison.Ordinal))
                return new SpeciesSpec(a, 1.911, 500, 120, SchoolFormation.Shoal, 1.0, 1.0, 3.5, 0.85, 1.15);
            if (a.StartsWith("school:barracuda", StringComparison.Ordinal))
                // formR 0.6 · swimMul 0.06 · modeDurMul 2.2 · transDurMul 2.5 · spinMul 0.1 ·
                // easeMul 0.8 · calm — builder.html :1098, the whole row.
                return new SpeciesSpec(a, 1.862, 200, 160, SchoolFormation.Cluster, 0.6, 0.06, 8.0, 0.85, 1.15,
                                       2.2, 2.5, 0.1, 0.8, true);
            if (a.StartsWith("school:batfish", StringComparison.Ordinal))
                return new SpeciesSpec(a, 1.900, 100, 90, SchoolFormation.Shoal, 1.0, 1.0, 3.0, 0.85, 1.15);
            if (a.StartsWith("pod:yellowtail", StringComparison.Ordinal))
                return new SpeciesSpec(a, 1.899, 50, 50, SchoolFormation.Pod, 1.0, 1.0, 1.3, 0.55, 1.35);
            if (a.StartsWith("pod:", StringComparison.Ordinal))
                return new SpeciesSpec(a, 1.900, 10, 10, SchoolFormation.Pod, 1.0, 1.0, 1.5, 0.70, 1.20);
            if (a.StartsWith("school:", StringComparison.Ordinal))
                return new SpeciesSpec(a, 1.900, 100, 90, SchoolFormation.Shoal, 1.0, 1.0, 3.0, 0.85, 1.15);

            return new SpeciesSpec(a, 1.900, 60, 60, SchoolFormation.Shoal, 1.0, 1.0, 2.5, 0.85, 1.15);
        }

        /// <summary>Everything the runtime needs to place + drive one school, in WORLD units.</summary>
        public readonly struct SchoolGeometry
        {
            public readonly SpeciesSpec Spec;
            /// <summary>One fish's world length (u) = local length × item.s.</summary>
            public readonly double FishWorldLen;
            /// <summary>Formation/shoal/pod radius (u), floors applied.</summary>
            public readonly double RadiusWorld;
            /// <summary>Half-height of the flat formation slab (u).</summary>
            public readonly double VertHalfWorld;
            /// <summary>Cruise speed cap (u/s).</summary>
            public readonly double SpeedCap;
            public bool IsPod => Spec.Formation == SchoolFormation.Pod;

            public SchoolGeometry(SpeciesSpec spec, double fishWorldLen, double radiusWorld,
                                  double vertHalfWorld, double speedCap)
            {
                Spec = spec;
                FishWorldLen = fishWorldLen;
                RadiusWorld = radiusWorld;
                VertHalfWorld = vertHalfWorld;
                SpeedCap = speedCap;
            }
        }

        /// <summary>
        /// Resolve a placed school item (assetId + saved item.s) into world-unit geometry.
        /// This is the ONE place the web formulas turn into the numbers the boids use, so a
        /// test can pin every species against builder.html.
        /// </summary>
        public static SchoolGeometry SchoolGeometryFor(string assetId, double itemScale)
        {
            SpeciesSpec sp = SpeciesFor(assetId);
            double s = itemScale > 0.0 ? itemScale : 1.0;

            double fishWorld, radius, vertHalf, speed;

            if (sp.Formation == SchoolFormation.Pod)
            {
                double animalLocal = PodAnimalLocalLen(sp.DefaultScale);
                fishWorld = animalLocal * s;
                radius    = PodRadius(animalLocal, sp.WebCount) * s;
                double floor = PodRadiusFloorMul * fishWorld;   // pods: 3×, NEVER the 8× school floor
                if (radius < floor) radius = floor;
                vertHalf  = radius * PodVertFactor;
                speed     = SpeedCapPerSecond(animalLocal, PodSpeedPerFrame, sp.SwimMul)
                          * s * PodSpeedUserMul;
            }
            else if (sp.Formation == SchoolFormation.Shoal)
            {
                fishWorld = sp.FishLenLocal * s;
                radius    = ShoalRadius(sp.FishLenLocal, sp.WebCount) * s * ShoalTightenMul;
                double floor = SchoolRadiusFloorMul * fishWorld;
                if (radius < floor) radius = floor;
                vertHalf  = radius * ShoalVertFactor;
                speed     = SpeedCapPerSecond(sp.FishLenLocal, ShoalSpeedPerFrame, sp.SwimMul)
                          * s * ShoalSpeedUserMul;
            }
            else
            {
                fishWorld = sp.FishLenLocal * s;
                radius    = FormationRadius(sp.FishLenLocal, sp.WebCount, sp.FormR) * s;
                double floor = SchoolRadiusFloorMul * fishWorld;
                if (radius < floor) radius = floor;
                vertHalf  = radius * ClusterVertFactor;
                speed     = SpeedCapPerSecond(sp.FishLenLocal, FormationSpeedPerFrame, sp.SwimMul) * s;
            }

            return new SchoolGeometry(sp, fishWorld, radius, vertHalf, speed);
        }

        // ── util ─────────────────────────────────────────────────────────────────
        public static double Clamp(double v, double lo, double hi)
            => v < lo ? lo : (v > hi ? hi : v);
    }
}
