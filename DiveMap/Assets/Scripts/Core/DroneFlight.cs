using System;

namespace DiveMap.Core
{
    /// <summary>
    /// P1.1 — the drone's flight model, ported constant-for-constant from the web's
    /// <c>tourUpdate()</c> (builder.html 3725-3749) and kept pure so every rule is unit-tested
    /// instead of costing a 35-minute CI round to eyeball.
    ///
    /// The web's numbers — ⚠️ the SPEEDS below are HISTORY as of 2026-08 (see the re-scale note
    /// further down); the geometry and the sign conventions are still the web's, verbatim:
    ///   • dead zone 0.12 on every axis (a resting thumb must not creep)
    ///   • yaw −= lx · 1.1 · dt                      (turn in place, gentle)      → now 0.9, filtered
    ///   • fwd = −ry, strafe = rx, lift = −ly, SP = 30 u/s   (push UP = forward / ascend) → now 9
    ///   • vertical speed 0.72 × horizontal                                       → now 0.35 / 0.5
    ///   • vel += (target − vel) · 0.09  PER FRAME — the drone's inertia. Still 0.09 at 60 Hz,
    ///     but now compounded over the frame's own length (<see cref="FrameLerp"/>): the web's
    ///     feel came from the lag, not from being SLOWER on a slower phone, which is what the
    ///     literal per-frame rule actually delivered
    ///   • dt = 0.016 × FS, the same real-delta scale the marine system uses
    ///     (<see cref="MarineMath.RealDeltaScale"/>) so fish and drone agree about time
    ///   • camera radius 3.2, floor = seabed + 3.2 + 1.5, ceiling = waterLevel − 2.5
    ///
    /// AXIS NOTE: the web's yaw=0 faces −Z and Unity's faces +Z, and the two worlds are already
    /// z-mirrored by <see cref="WebCoord"/>. Rather than port the mirrored trig and hope, this
    /// works in Unity terms: <c>forward = (sin yaw, 0, cos yaw)</c>, right = (cos yaw, 0, −sin yaw).
    /// Pushing the left stick right therefore turns right, which is the only thing a player can
    /// actually feel.
    ///
    /// ── 2026-08 — "โดรนบินเร็วไป". The web's numbers were a FLYING model, not a diving one ──
    ///
    /// The world's scale is not a guess: <see cref="ItemPicker.UnitsPerMetre"/> = 6, the same
    /// constant the depth readout divides by (builder.html L600 <c>U_PER_M</c>), so the HUD's
    /// "54.0 ม." and these speeds are in the same currency. Converted, the ported numbers were:
    ///
    ///   was  30 u/s horizontal = 5.00 m/s   — 10× a relaxed diver, 5× a diver sprinting,
    ///                                          3.3× a DPV scooter at full tilt
    ///   was  21.6 u/s vertical = 3.60 m/s   — 216 m/min of ascent; a real ascent is 9-18 m/min
    ///
    /// Now, with 1 unit = 1/6 m throughout:
    ///
    ///   forward   9.0 u/s = 1.50 m/s   a DPV at full throttle, which is what a "drone" is
    ///   strafe    6.3 u/s = 1.05 m/s   crabbing sideways is never as efficient as swimming ahead
    ///   ascend    3.15 u/s = 0.53 m/s  (32 m/min — still generous, but no longer a rocket)
    ///   descend   4.5 u/s = 0.75 m/s   going down is the easy direction, and it always was
    ///   yaw       0.9 rad/s = 52 °/s   a full turn in 7 s
    ///
    /// A stick at half deflection lands at 0.33 m/s — a diver finning gently — because the
    /// response is now EXPO (<see cref="Shape"/>) rather than linear, which is what makes it
    /// possible to creep up on a nudibranch and still have a top speed worth having.
    ///
    /// The other half of "คุมไม่อยู่" was the stop: thrust and coast shared one response, so
    /// releasing the stick braked as hard as pushing it. Water does not brake; it drags.
    /// <see cref="Inertia"/> is now the THRUST response and <see cref="Drag"/> the (slower)
    /// coasting one, so letting go glides to a halt over about half a metre.
    ///
    /// ⚠️ <see cref="FleeMath.DiverPanicSpeed"/> — the speed above which a shoal reads the diver
    /// as a predator — was the web's flat 11 u/s, i.e. 37 % of the old top speed. It is now
    /// derived from <see cref="Speed"/> so that fraction survives; hard-coding 11 against a 9 u/s
    /// drone would have meant no fish ever fled again.
    /// </summary>
    public static class DroneFlight
    {
        public const float DeadZone = 0.12f;

        /// <summary>
        /// Stick curve strength, 0 = linear, 1 = pure cubic. 0.6 is the usual RC-transmitter
        /// value. Measured through <see cref="Shape"/> — which also takes the dead zone out of the
        /// range — half a stick gives 22 % thrust (fine control where you are looking at
        /// something) and the full stick still gives 100 %.
        /// </summary>
        public const float Expo = 0.6f;

        public const float YawRate = 0.9f;      // rad/s at full deflection (52 °/s)
        public const float Speed = 9f;          // u/s = 1.50 m/s — see class remarks (was 30)
        public const float StrafeRatio = 0.7f;  // sideways is 1.05 m/s
        public const float AscendRatio = 0.35f; // up is the dangerous direction: 0.53 m/s
        public const float DescendRatio = 0.5f; // down is the easy one: 0.75 m/s
        public const float Inertia = 0.09f;     // THRUST response per 60 Hz frame — see remarks
        public const float Drag = 0.05f;        // coasting response — softer, so a stop glides
        public const float YawResponse = 0.14f; // turn-rate response per 60 Hz frame
        public const float CamRadius = 3.2f;
        public const float FloorClearance = 1.5f;
        public const float CeilingClearance = 2.5f;
        public const float LookAhead = 12f;     // camera.lookAt distance
        /// <summary>
        /// Camera tilt per unit of vertical speed. Raised 0.14 → 0.9 with the speed cut: the tilt
        /// is a fraction of <see cref="LookAhead"/>, so leaving it at 0.14 against a vertical
        /// speed seven times smaller would have flattened the climb out of the shot entirely.
        /// </summary>
        public const float PitchFromLift = 0.9f;

        /// <summary>Handy for logs and tests: world units per second → metres per second.</summary>
        public static float MetresPerSecond(float unitsPerSecond)
            => (float)(unitsPerSecond / ItemPicker.UnitsPerMetre);

        /// <summary>
        /// D9 — where a player who did not choose lands. The web's <c>enterTour(randomStart)</c>
        /// (builder.html:3722) drops them at a random bearing, somewhere between 20 % and 80 % of
        /// the map radius out, and at least 18 units above the sand:
        ///
        ///   <c>rr = mR·(0.2 + rnd·0.6)</c> · <c>y = max(seabedTop + 18, waterLevel·0.5)</c>
        ///
        /// The inner 20 % is excluded on purpose — spawning dead centre puts you inside the wreck
        /// every time — and the outer 20 % because the edge of a map is nothing to look at. The
        /// height floor matters more than it looks: a spawn at sand level starts the dive with the
        /// camera buried, which reads as a broken map rather than a deep one.
        ///
        /// Pure: the caller supplies the two random numbers, so a QC run can pin them.
        /// </summary>
        public static Vec3 RandomSpawn(Vec3 centre, float mapRadius, float seabedTopY, float waterLevel,
                                       float rndAngle01, float rndRadius01)
        {
            float a = Clamp01(rndAngle01) * 6.283185f;
            float rr = mapRadius * (0.2f + Clamp01(rndRadius01) * 0.6f);
            float x = centre.X + (float)Math.Cos(a) * rr;
            float z = centre.Z + (float)Math.Sin(a) * rr;
            float y = Math.Max(seabedTopY + 18f, waterLevel * 0.5f);
            return new Vec3(x, y, z);
        }

        /// <summary>Face the middle of the map from wherever you surfaced.</summary>
        public static float YawToward(Vec3 from, Vec3 target)
            => (float)Math.Atan2(target.Z - from.Z, target.X - from.X);

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        public struct Vec3
        {
            public float X, Y, Z;
            public Vec3(float x, float y, float z) { X = x; Y = y; Z = z; }
        }

        /// <summary>
        /// One solid box. Either in world axes (a whole object's single AABB) or in an object's own
        /// frame (one part of a hull) — <see cref="Solid"/> says which, and the test grows it by the
        /// camera radius on all six sides either way.
        /// </summary>
        public struct Box
        {
            public float MinX, MinY, MinZ, MaxX, MaxY, MaxZ;
        }

        /// <summary>Frame → world rotation, Unity's component order. Assumed unit length.</summary>
        public struct Quat
        {
            public float X, Y, Z, W;
            public Quat(float x, float y, float z, float w) { X = x; Y = y; Z = z; W = w; }
            public static Quat Identity => new Quat(0f, 0f, 0f, 1f);
        }

        /// <summary>
        /// One solid OBJECT: either a single world box, or a hull of boxes in the object's own
        /// frame plus the placement that frame sits in.
        ///
        /// 🔴 <see cref="Boxes"/> is what stops the "เรือเอียงแล้วตัน" bug coming back. A tilted
        /// wreck's hull used to arrive here already flattened into world axes, which inflated its
        /// collision volume ×4 to ×7 and welded 96 separate boxes into one brick. Now the boxes
        /// arrive in the object's frame and the DIVER is brought into that frame instead — one
        /// inverse rotation per object per frame, whatever the hull's box count.
        ///
        /// <see cref="Bound"/> is the object's world AABB. When <see cref="Boxes"/> is null it IS
        /// the collider (every object before hulls existed, and everything outside the detail
        /// radius today). When there is a hull, it is only the cheap "not near this object" reject:
        /// the hull is fitted inside the same content the bounds were measured from, so nothing in
        /// it can reach outside.
        /// </summary>
        public struct Solid
        {
            public Box Bound;
            public Box[] Boxes;
            public Vec3 Origin;
            public Quat Rot;
            /// <summary>False when <see cref="Rot"/> is identity — skips the quaternion entirely.</summary>
            public bool Rotated;

            /// <summary>The shape every object had before hulls: one box, world axes.</summary>
            public static Solid Aabb(Box world)
                => new Solid { Bound = world, Boxes = null, Rot = Quat.Identity, Rotated = false };
        }

        public struct State
        {
            public Vec3 Pos;
            public Vec3 Vel;
            public float Yaw;      // radians, Unity convention (0 = +Z)
            public float YawVel;   // rad/s — the turn has mass too, so it starts and stops smoothly
        }

        /// <summary>Stick input, −1…1 each. Left = turn/lift, right = thrust/strafe.</summary>
        public struct Sticks
        {
            public float Lx, Ly, Rx, Ry;
        }

        public static float ApplyDeadZone(float v) => Math.Abs(v) < DeadZone ? 0f : v;

        /// <summary>
        /// Dead zone + expo, in one pass. The dead zone is REMOVED from the range rather than
        /// clipped out of it, so the first millimetre of travel past 0.12 gives a whisper of
        /// thrust instead of a step to 12 %; then the cubic blend does the rest.
        /// Full deflection still means full speed — this shapes the middle, not the ends.
        /// </summary>
        public static float Shape(float v)
        {
            float a = Math.Abs(v);
            if (a < DeadZone) return 0f;
            if (a > 1f) a = 1f;
            float t = (a - DeadZone) / (1f - DeadZone);
            float o = (1f - Expo) * t + Expo * t * t * t;
            return v < 0f ? -o : o;
        }

        /// <summary>
        /// The per-frame lerp factor <paramref name="k"/>, corrected for a frame that is not the
        /// 60 Hz one it was tuned on.
        ///
        /// The web applies its 0.09 once per frame and the port kept that deliberately — the feel
        /// IS the frame-rate-relative lag. What it is not meant to be is a different drone on a
        /// different phone: at 30 fps the old code lagged twice as long in wall-clock terms, which
        /// on a mid-range device is exactly the "คุมไม่อยู่" the report describes. Compounding the
        /// same factor over FS frames' worth of time keeps 60 fps bit-for-bit identical
        /// (<c>dt = 0.016 ⇒ FS = 1 ⇒ k</c>) and makes every other frame rate agree with it.
        /// </summary>
        public static float FrameLerp(float k, float dt)
        {
            if (k <= 0f) return 0f;
            if (k >= 1f) return 1f;
            float fs = dt / 0.016f;
            if (fs <= 0f) return 0f;
            if (fs > 4f) fs = 4f;                       // FS is clamped at 2.5 upstream; belt and braces
            if (fs > 0.999f && fs < 1.001f) return k;   // the 60 Hz case, exactly as the web has it
            return 1f - (float)Math.Pow(1f - k, fs);
        }

        /// <summary>
        /// Integrate one frame. <paramref name="dt"/> = 0.016 × FS. Pure: the caller supplies the
        /// world (<paramref name="seabedY"/> under the NEW position, the water level, the solid
        /// boxes and the map's footprint scale) and gets the next state back.
        ///
        /// <paramref name="speedScale"/> is the user's own preference
        /// (<c>SettingsStore.SpeedScale</c>), applied to the three translation speeds only. The
        /// turn rate is deliberately NOT scaled: how fast you swim and how fast you can aim are
        /// different questions, and someone who picks "ช้า" to look at coral still has to be able
        /// to turn round and find the exit.
        /// </summary>
        public static State Step(State s, Sticks sticks, float dt, float seabedY, float waterLevel,
                                 Solid[] solids, float scaleX, float scaleZ, float speedScale = 1f)
        {
            float lx = Shape(sticks.Lx);
            float ly = Shape(sticks.Ly);
            float rx = Shape(sticks.Rx);
            float ry = Shape(sticks.Ry);

            if (speedScale <= 0.01f) speedScale = 1f;
            float speed = Speed * speedScale;

            // Turn in place. Unity yaw grows clockwise seen from above, so +lx turns right.
            // Through a rate filter, so a flick of the thumb no longer snaps the horizon sideways
            // and letting go coasts the turn to a stop instead of nailing it.
            s.YawVel += (lx * YawRate - s.YawVel) * FrameLerp(YawResponse, dt);
            s.Yaw += s.YawVel * dt;

            float sin = (float)Math.Sin(s.Yaw), cos = (float)Math.Cos(s.Yaw);
            float fwd = -ry;        // stick UP (negative screen Y) = forward
            float strafe = rx * StrafeRatio;
            float lift = -ly;       // stick UP = ascend

            // forward = (sin, 0, cos), right = (cos, 0, −sin)
            float tx = (fwd * sin + strafe * cos) * speed;
            float tz = (fwd * cos - strafe * sin) * speed;
            // Up and down are not the same manoeuvre. A runaway ascent is the one thing in diving
            // that actually hurts you, so the app models the asymmetry rather than teaching the
            // opposite of a briefing.
            float ty = lift * speed * (lift >= 0f ? AscendRatio : DescendRatio);

            // THRUST accelerates; a released stick only drags. Per axis, so gliding forward while
            // you stop strafing behaves the way water does.
            s.Vel.X += (tx - s.Vel.X) * FrameLerp(Math.Abs(tx) > 1e-4f ? Inertia : Drag, dt);
            s.Vel.Y += (ty - s.Vel.Y) * FrameLerp(Math.Abs(ty) > 1e-4f ? Inertia : Drag, dt);
            s.Vel.Z += (tz - s.Vel.Z) * FrameLerp(Math.Abs(tz) > 1e-4f ? Inertia : Drag, dt);

            var np = new Vec3(s.Pos.X + s.Vel.X * dt,
                              s.Pos.Y + s.Vel.Y * dt,
                              s.Pos.Z + s.Vel.Z * dt);

            // Sand and surface. The floor is needed BEFORE the solids: which way out of a solid is
            // allowed depends on where the sand is (see Resolve).
            float floor = seabedY + CamRadius + FloorClearance;

            if (solids != null)
                for (int i = 0; i < solids.Length; i++)
                    Resolve(ref np, ref s.Vel, solids[i], floor);

            if (np.Y < floor) { np.Y = floor; if (s.Vel.Y < 0f) s.Vel.Y = 0f; }
            float ceiling = waterLevel - CeilingClearance;
            if (ceiling > floor && np.Y > ceiling) { np.Y = ceiling; if (s.Vel.Y > 0f) s.Vel.Y = 0f; }

            // Stay inside the rounded-square map (the web's fieldBound, expressed as a fraction
            // of the boundary so non-uniform areaScaleX/Z is handled without extra trig).
            float sx = scaleX > 0.01f ? scaleX : 1f;
            float sz = scaleZ > 0.01f ? scaleZ : 1f;
            float f = SeabedGeom.BoundaryFraction(np.X / sx, np.Z / sz);
            float limit = 1f - (CamRadius + 3f) / SeabedGeom.SandRadius;
            if (f > limit && f > 1e-4f)
            {
                float k = limit / f;
                np.X *= k;
                np.Z *= k;
            }

            s.Pos = np;
            return s;
        }

        /// <summary>
        /// A cube of half-side r reaches r·√3 along a world axis when it is turned to the worst
        /// angle — the slack the world-space reject needs before it is allowed to skip a rotated
        /// object.
        /// </summary>
        private const float Root3 = 1.7320508f;

        /// <summary>
        /// Push the diver out of one solid object, through the shallowest face that is allowed.
        ///
        /// ── the frame ────────────────────────────────────────────────────────────────────────
        /// A hull arrives in the OBJECT's frame, so the diver is brought into it (one inverse
        /// rotation for the whole hull, not one per box) and the six-face test runs there. The
        /// frame is in world units — <c>SolidBoxes.ToFrame</c> bakes the placement's scale into the
        /// boxes — so <see cref="CamRadius"/> is still 3.2 in it and there is no per-axis radius to
        /// divide, which is what makes a non-uniform scale (T-13 lays a wreck out at
        /// 37.7 × 100.9 × 37.7) safe rather than a special case.
        ///
        /// ── which face ───────────────────────────────────────────────────────────────────────
        /// 🔴 The old code offered FIVE faces: ±X, ±Z and +Y. That is not "never push down", it is
        /// "always push up": anyone who ended up inside a solid — which the tilted-hull bug made
        /// routine — was lifted to the top of it and set down on the roof of the wreck, whatever
        /// they were doing. The sixth face is back, and the rule it used to stand in for is now
        /// stated directly: a face is refused when it would push the diver DOWNWARD and land them
        /// under the sand. Sideways and upward exits are never refused, and a solid resting ON the
        /// seabed still cannot be left through its underside — the arithmetic there is unchanged,
        /// which is why "may rest on top, never pushed through the floor" still holds.
        ///
        /// Ties go to the old five faces in their old order, so the sixth only ever wins when it is
        /// strictly the shallowest way out. If every face is refused (fully buried in the sand under
        /// a solid) the shallowest one is taken anyway and the floor clamp downstream has the last
        /// word — being stopped is always better than being nowhere.
        /// </summary>
        private static void Resolve(ref Vec3 np, ref Vec3 vel, Solid o, float floorY)
        {
            Box bd = o.Bound;
            float slack = o.Rotated ? CamRadius * Root3 : CamRadius;
            if (np.X <= bd.MinX - slack || np.X >= bd.MaxX + slack ||
                np.Y <= bd.MinY - slack || np.Y >= bd.MaxY + slack ||
                np.Z <= bd.MinZ - slack || np.Z >= bd.MaxZ + slack) return;

            Box[] boxes = o.Boxes;
            int n = boxes == null ? 1 : boxes.Length;
            if (n == 0) return;

            Vec3 p = new Vec3(np.X - o.Origin.X, np.Y - o.Origin.Y, np.Z - o.Origin.Z);
            Vec3 v = vel;
            // How much world HEIGHT one unit along each of the frame's own axes buys — the middle
            // row of the rotation matrix. The sand rule is about world Y, and in a frame tilted 99°
            // no local axis points that way.
            float ux = 0f, uy = 1f, uz = 0f;
            if (o.Rotated)
            {
                p = Unrotate(o.Rot, p);
                v = Unrotate(o.Rot, v);
                AxisHeight(o.Rot, out ux, out uy, out uz);
            }

            bool hit = false;
            for (int k = 0; k < n; k++)
            {
                Box b = boxes == null ? bd : boxes[k];

                float pxL = p.X - (b.MinX - CamRadius); if (pxL <= 0f) continue;
                float pxR = (b.MaxX + CamRadius) - p.X; if (pxR <= 0f) continue;
                float pyB = p.Y - (b.MinY - CamRadius); if (pyB <= 0f) continue;
                float pyT = (b.MaxY + CamRadius) - p.Y; if (pyT <= 0f) continue;
                float pzL = p.Z - (b.MinZ - CamRadius); if (pzL <= 0f) continue;
                float pzR = (b.MaxZ + CamRadius) - p.Z; if (pzR <= 0f) continue;

                // Where the diver is in world height right now — exact, because the frame is metric.
                float wy = o.Origin.Y + ux * p.X + uy * p.Y + uz * p.Z;

                int face = -1, fallback = -1;
                float best = float.MaxValue, worst = float.MaxValue;
                // The old five first, in the old order, so a tie never changes today's answer.
                Consider(0, pxL, -ux, wy, floorY, ref face, ref best, ref fallback, ref worst);
                Consider(1, pxR, ux, wy, floorY, ref face, ref best, ref fallback, ref worst);
                Consider(4, pzL, -uz, wy, floorY, ref face, ref best, ref fallback, ref worst);
                Consider(5, pzR, uz, wy, floorY, ref face, ref best, ref fallback, ref worst);
                Consider(3, pyT, uy, wy, floorY, ref face, ref best, ref fallback, ref worst);
                Consider(2, pyB, -uy, wy, floorY, ref face, ref best, ref fallback, ref worst);

                switch (face >= 0 ? face : fallback)
                {
                    case 0: p.X = b.MinX - CamRadius; if (v.X > 0f) v.X = 0f; break;
                    case 1: p.X = b.MaxX + CamRadius; if (v.X < 0f) v.X = 0f; break;
                    case 2: p.Y = b.MinY - CamRadius; if (v.Y > 0f) v.Y = 0f; break;
                    case 3: p.Y = b.MaxY + CamRadius; if (v.Y < 0f) v.Y = 0f; break;
                    case 4: p.Z = b.MinZ - CamRadius; if (v.Z > 0f) v.Z = 0f; break;
                    default: p.Z = b.MaxZ + CamRadius; if (v.Z < 0f) v.Z = 0f; break;
                }
                hit = true;
            }

            if (!hit) return;   // untouched objects must not pay a float round trip

            if (o.Rotated) { p = Rotate(o.Rot, p); v = Rotate(o.Rot, v); }
            np = new Vec3(o.Origin.X + p.X, o.Origin.Y + p.Y, o.Origin.Z + p.Z);
            vel = v;
        }

        /// <summary>
        /// One candidate face. <paramref name="heightGain"/> is the world Y this face's outward
        /// direction gains per unit pushed, so <c>depth × heightGain</c> is where the diver would
        /// end up — the sand rule, and nothing else, decides whether the face is on the menu.
        /// </summary>
        private static void Consider(int face, float depth, float heightGain, float wy, float floorY,
                                     ref int bestFace, ref float bestDepth,
                                     ref int anyFace, ref float anyDepth)
        {
            if (depth < anyDepth) { anyDepth = depth; anyFace = face; }
            float gain = depth * heightGain;
            if (gain < 0f && wy + gain < floorY) return;   // would shove the diver under the sand
            if (depth < bestDepth) { bestDepth = depth; bestFace = face; }
        }

        /// <summary>v′ = v + 2w(q⃗ × v) + 2 q⃗ × (q⃗ × v) — the standard rotate, no matrix needed.</summary>
        private static Vec3 Rotate(Quat q, Vec3 v)
        {
            float tx = 2f * (q.Y * v.Z - q.Z * v.Y);
            float ty = 2f * (q.Z * v.X - q.X * v.Z);
            float tz = 2f * (q.X * v.Y - q.Y * v.X);
            return new Vec3(v.X + q.W * tx + (q.Y * tz - q.Z * ty),
                            v.Y + q.W * ty + (q.Z * tx - q.X * tz),
                            v.Z + q.W * tz + (q.X * ty - q.Y * tx));
        }

        /// <summary>World → the frame: the same rotate by the conjugate.</summary>
        private static Vec3 Unrotate(Quat q, Vec3 v)
            => Rotate(new Quat(-q.X, -q.Y, -q.Z, q.W), v);

        /// <summary>The world Y component of each frame axis — the rotation matrix's middle row.</summary>
        private static void AxisHeight(Quat q, out float ux, out float uy, out float uz)
        {
            ux = 2f * (q.X * q.Y + q.Z * q.W);
            uy = 1f - 2f * (q.X * q.X + q.Z * q.Z);
            uz = 2f * (q.Y * q.Z - q.X * q.W);
        }

        /// <summary>
        /// Where the camera looks: forward along the yaw, tilted slightly toward the vertical
        /// motion (builder.html:3769) so climbing feels like climbing.
        /// </summary>
        public static Vec3 LookTarget(State s)
        {
            float sin = (float)Math.Sin(s.Yaw), cos = (float)Math.Cos(s.Yaw);
            return new Vec3(s.Pos.X + sin * LookAhead,
                            s.Pos.Y + s.Vel.Y * PitchFromLift,
                            s.Pos.Z + cos * LookAhead);
        }

        /// <summary>Depth in metres for the HUD — the web's <c>depthMetres()</c>, clamped 0-100.</summary>
        public static float DepthMetres(float y, float waterLevel)
        {
            double d = (waterLevel - y) / ItemPicker.UnitsPerMetre;
            if (d < 0) d = 0;
            if (d > 100) d = 100;
            return (float)d;
        }
    }
}
