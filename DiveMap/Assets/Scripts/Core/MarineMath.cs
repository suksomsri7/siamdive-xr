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
        public const double FormationSpeedPerFrame = 0.065;
        public const double PodSpeedPerFrame       = 0.02;  // pods hold slots + bob, they don't dash
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

            public SpeciesSpec(string assetId, double fishLenLocal, int webCount, int unityCount,
                               SchoolFormation formation, double formR, double swimMul,
                               double defaultScale, double sizeMin, double sizeMax)
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
                return new SpeciesSpec(a, 1.862, 200, 160, SchoolFormation.Cluster, 0.6, 0.06, 8.0, 0.85, 1.15);
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
                speed     = SpeedCapPerSecond(animalLocal, PodSpeedPerFrame, sp.SwimMul) * s;
            }
            else if (sp.Formation == SchoolFormation.Shoal)
            {
                fishWorld = sp.FishLenLocal * s;
                radius    = ShoalRadius(sp.FishLenLocal, sp.WebCount) * s;
                double floor = SchoolRadiusFloorMul * fishWorld;
                if (radius < floor) radius = floor;
                vertHalf  = radius * ShoalVertFactor;
                speed     = SpeedCapPerSecond(sp.FishLenLocal, ShoalSpeedPerFrame, sp.SwimMul) * s;
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
