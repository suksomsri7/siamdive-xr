using System;

namespace DiveMap.Core
{
    /// <summary>
    /// P1.1 — the drone's flight model, ported constant-for-constant from the web's
    /// <c>tourUpdate()</c> (builder.html 3765-3773 + 3809) and kept pure so every rule is
    /// unit-tested instead of costing a 35-minute CI round to eyeball.
    ///
    /// The web's numbers, verbatim, with the line each one comes from:
    ///   • builder.html:3766 <c>dz=v=&gt;Math.abs(v)&lt;0.12?0:v</c>
    ///       dead zone 0.12 on every axis, and NOTHING else — no curve (a resting thumb must not
    ///       creep; a thumb at half travel gets half thrust)
    ///   • builder.html:3766 <c>const dt=0.016*FS</c> — the same real-delta scale the marine
    ///       system uses (<see cref="MarineMath.RealDeltaScale"/>), so fish and drone agree
    ///       about time. FS itself is builder.html:3918, clamped 0.5…2.5
    ///   • builder.html:3768 <c>d.yaw -= lx*1.1*dt</c>       — turn in place, gentle, UNFILTERED
    ///   • builder.html:3770 <c>fwd=-ry, strafe=rx, lift=-ly, SP=30</c> (push UP = forward/ascend)
    ///   • builder.html:3771 <c>ty=lift*SP*0.72</c>          — vertical is 0.72 × horizontal,
    ///       the SAME both ways, and strafe carries no ratio at all (<c>strafe=rx</c>)
    ///   • builder.html:3772 <c>d.vel.x+=(tx-d.vel.x)*0.09</c> — one inertia for thrust AND coast
    ///   • builder.html:3773 <c>np.x+=d.vel.x*dt</c>         — velocity is in units per SECOND
    ///   • builder.html:3809 <c>camera.lookAt(np.x-sin*12, np.y+d.vel.y*0.14, np.z-cos*12)</c>
    ///   • camera radius 3.2, floor = seabed + 3.2 + 1.5, ceiling = waterLevel − 2.5
    ///
    /// AXIS NOTE: the web's yaw=0 faces −Z and Unity's faces +Z, and the two worlds are already
    /// z-mirrored by <see cref="WebCoord"/>. Rather than port the mirrored trig and hope, this
    /// works in Unity terms: <c>forward = (sin yaw, 0, cos yaw)</c>, right = (cos yaw, 0, −sin yaw).
    /// Pushing the left stick right therefore turns right, which is the only thing a player can
    /// actually feel.
    ///
    /// ── 2026-08-04 — "โดรนเคลื่อนที่ช้าไป" (build 261). Back to the web, exactly ──────────────
    ///
    /// A previous round read the web's speed as a metric implausibility (5 m/s is ten times a
    /// relaxed diver) and re-scaled the whole model down: SP 30→9, yaw 1.1→0.9, lift 0.72→0.35/0.5,
    /// strafe ×0.7, plus two inventions the web does not have — an EXPO stick curve and a separate
    /// (softer) coasting Drag. Measured end to end, that shipped a drone
    ///
    ///   at full stick   9 u/s   vs the web's 30      — 3.3× slower
    ///   at half stick   1.98 u/s vs the web's 15     — 7.6× slower, because expo turns 50 % of
    ///                                                  travel into 22 % of thrust
    ///
    /// and half stick is where a thumb actually lives. That second row is the complaint.
    ///
    /// The realism argument was not wrong, it was aimed at the wrong control: a map is a place you
    /// travel across, and the user has spent months tuning that traversal ON THE WEB and calls the
    /// result "ดีมากๆ". So the web's model is restored verbatim — including linear sticks and a
    /// single 0.09 inertia — and the diver-paced version is kept where a preference belongs, as
    /// <see cref="SettingsStore.SpeedCalm"/> (0.30 × 30 = 9 u/s, i.e. build 261's drone exactly).
    ///
    /// For reference, at <see cref="ItemPicker.UnitsPerMetre"/> = 6 (builder.html L600 U_PER_M —
    /// the constant the depth readout divides by, so the HUD's "54.0 ม." is in this currency):
    ///
    ///   forward / strafe  30 u/s   = 5.00 m/s        ceiling; a thumb rarely holds it
    ///   ascend / descend  21.6 u/s = 3.60 m/s
    ///   yaw               1.1 rad/s = 63 °/s — a full turn in 5.7 s
    ///
    /// TWO things are deliberately NOT the web, and both are bugs the web cannot have:
    ///
    ///   1. <see cref="FrameLerp"/>. The web applies 0.09 once per FRAME with no dt. At 60 Hz that
    ///      is the tuned feel; at 30 Hz it is literally a different drone (half the acceleration in
    ///      wall-clock terms), and a phone's frame rate is not a design decision. FrameLerp
    ///      compounds the same factor over the frame's own length: bit-for-bit identical at 60 Hz
    ///      (dt = 0.016 ⇒ FS = 1 ⇒ k) and agreeing with it everywhere else. The web runs at a
    ///      steady 60 on a desktop, so it never had to answer this.
    ///   2. The solids: hulls in the object's own frame rather than the web's world AABBs. That is
    ///      a superset — see <see cref="Resolve"/> — and does not touch speed.
    ///
    /// ⚠️ <see cref="FleeMath.DiverPanicSpeed"/> — the speed above which a shoal reads the diver as
    /// a predator — is the web's flat 11 u/s expressed as 11/30 of <see cref="Speed"/>. With Speed
    /// back at 30 it evaluates to 11.0 again, i.e. the web's number exactly.
    /// </summary>
    public static class DroneFlight
    {
        /// <summary>builder.html:3766 — <c>dz=v=&gt;Math.abs(v)&lt;0.12?0:v</c>.</summary>
        public const float DeadZone = 0.12f;

        public const float YawRate = 1.1f;      // rad/s at full deflection — builder.html:3768
        /// <summary>
        /// 30 → 24 u/s (5.00 → 4.00 m/s) — user 9 ส.ค.: "ปรับให้โดรนเคลื่อนที่ช้าลงอีกนิด"
        ///
        /// ประวัติของเลขนี้: เคยถูกลดเป็นเมตริกจริงแล้ว user บอก "โดรนเคลื่อนที่ช้าไป" (4 ส.ค.)
        /// จึงกลับมาที่ค่าเว็บ 30 · รอบนี้ลดลง 20% ซึ่งยังอยู่เหนือค่าที่เคยถูกบ่นว่าช้า
        /// (build 261 = 9 u/s ซึ่งตอนนี้เป็นพรีเซ็ต "สงบ")
        ///
        /// ⚠️ FleeMath.DiverPanicSpeed ผูกกับเลขนี้เป็นสัดส่วน (11/30) จึงเลื่อนตามเป็น 8.8 u/s
        /// โดยอัตโนมัติ — เกณฑ์ "ว่ายเร็วพอจะทำให้ฝูงตกใจ" ยังเป็นสัดส่วนเดิมของความเร็วเต็ม
        /// ซึ่งเป็นสิ่งที่ตั้งใจตั้งแต่แรก (ดูคอมเมนต์บน DiverPanicSpeed)
        /// </summary>
        public const float Speed = 24f;         // u/s = 4.00 m/s (เว็บ 3770 = 30; user ขอช้าลง)
        public const float StrafeRatio = 1f;    // the web strafes at SP   — builder.html:3770-3771
        public const float AscendRatio = 0.72f; // builder.html:3771 — ty = lift·SP·0.72
        public const float DescendRatio = 0.72f;// …the same factor both ways; the web has one term
        public const float Inertia = 0.09f;     // response per 60 Hz frame — builder.html:3772
        public const float CamRadius = 3.2f;
        public const float FloorClearance = 1.5f;
        public const float CeilingClearance = 2.5f;
        public const float LookAhead = 12f;     // camera.lookAt distance  — builder.html:3809
        /// <summary>Camera tilt per unit of vertical speed — builder.html:3809 (<c>d.vel.y*0.14</c>).</summary>
        public const float PitchFromLift = 0.14f;

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

        /// <summary>
        /// builder.html:3766 — <c>dz=v=&gt;Math.abs(v)&lt;0.12?0:v</c>, and nothing else. Past the dead
        /// zone the stick is LINEAR: half travel is half thrust, which is the whole reason the web
        /// feels quick to a thumb that never reaches the edge of the pad.
        ///
        /// Over-range input is clamped to ±1 — the web never sees more than ±1 from its own pad,
        /// but a gamepad or a QC harness can, and 1.4 × SP is not a speed anything here was
        /// designed around.
        /// </summary>
        public static float ApplyDeadZone(float v)
        {
            if (Math.Abs(v) < DeadZone) return 0f;
            if (v > 1f) return 1f;
            if (v < -1f) return -1f;
            return v;
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
            float lx = ApplyDeadZone(sticks.Lx);
            float ly = ApplyDeadZone(sticks.Ly);
            float rx = ApplyDeadZone(sticks.Rx);
            float ry = ApplyDeadZone(sticks.Ry);

            if (speedScale <= 0.01f) speedScale = 1f;
            float speed = Speed * speedScale;

            // Turn in place — builder.html:3768 <c>d.yaw -= lx*1.1*dt</c>, unfiltered. Unity yaw
            // grows clockwise seen from above, so +lx turns right (the web's sign is mirrored with
            // its −Z forward; see the AXIS NOTE).
            s.YawVel = lx * YawRate;
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

            // builder.html:3772 — one inertia, thrust and coast alike. dt-corrected so 30 fps is
            // the same drone as 60 (see FrameLerp); at 60 fps this IS the web's line.
            float lerpK = FrameLerp(Inertia, dt);
            s.Vel.X += (tx - s.Vel.X) * lerpK;
            s.Vel.Y += (ty - s.Vel.Y) * lerpK;
            s.Vel.Z += (tz - s.Vel.Z) * lerpK;

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
