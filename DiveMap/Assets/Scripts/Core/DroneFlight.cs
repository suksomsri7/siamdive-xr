using System;

namespace DiveMap.Core
{
    /// <summary>
    /// P1.1 — the drone's flight model, ported constant-for-constant from the web's
    /// <c>tourUpdate()</c> (builder.html 3725-3749) and kept pure so every rule is unit-tested
    /// instead of costing a 35-minute CI round to eyeball.
    ///
    /// The web's numbers, all of them load-bearing:
    ///   • dead zone 0.12 on every axis (a resting thumb must not creep)
    ///   • yaw −= lx · 1.1 · dt                      (turn in place, gentle)
    ///   • fwd = −ry, strafe = rx, lift = −ly, SP = 30 u/s   (push UP = forward / ascend)
    ///   • vertical speed 0.72 × horizontal
    ///   • vel += (target − vel) · 0.09  PER FRAME — this is the drone's inertia, and it is
    ///     deliberately not dt-scaled: the web's own feel comes from the frame-rate-relative
    ///     lag, and dt-correcting it makes the drone feel sharp/twitchy instead of heavy
    ///   • dt = 0.016 × FS, the same real-delta scale the marine system uses
    ///     (<see cref="MarineMath.RealDeltaScale"/>) so fish and drone agree about time
    ///   • camera radius 3.2, floor = seabed + 3.2 + 1.5, ceiling = waterLevel − 2.5
    ///
    /// AXIS NOTE: the web's yaw=0 faces −Z and Unity's faces +Z, and the two worlds are already
    /// z-mirrored by <see cref="WebCoord"/>. Rather than port the mirrored trig and hope, this
    /// works in Unity terms: <c>forward = (sin yaw, 0, cos yaw)</c>, right = (cos yaw, 0, −sin yaw).
    /// Pushing the left stick right therefore turns right, which is the only thing a player can
    /// actually feel.
    /// </summary>
    public static class DroneFlight
    {
        public const float DeadZone = 0.12f;
        public const float YawRate = 1.1f;      // rad/s at full deflection
        public const float Speed = 30f;         // u/s (web: SP, raised 20→30 on 2026-06-27)
        public const float LiftRatio = 0.72f;
        public const float Inertia = 0.09f;     // per frame, NOT per second — see class remarks
        public const float CamRadius = 3.2f;
        public const float FloorClearance = 1.5f;
        public const float CeilingClearance = 2.5f;
        public const float LookAhead = 12f;     // camera.lookAt distance
        public const float PitchFromLift = 0.14f;

        public struct Vec3
        {
            public float X, Y, Z;
            public Vec3(float x, float y, float z) { X = x; Y = y; Z = z; }
        }

        /// <summary>One solid box in world space, already grown by the camera radius test.</summary>
        public struct Box
        {
            public float MinX, MinY, MinZ, MaxX, MaxY, MaxZ;
        }

        public struct State
        {
            public Vec3 Pos;
            public Vec3 Vel;
            public float Yaw;      // radians, Unity convention (0 = +Z)
        }

        /// <summary>Stick input, −1…1 each. Left = turn/lift, right = thrust/strafe.</summary>
        public struct Sticks
        {
            public float Lx, Ly, Rx, Ry;
        }

        public static float ApplyDeadZone(float v) => Math.Abs(v) < DeadZone ? 0f : v;

        /// <summary>
        /// Integrate one frame. <paramref name="dt"/> = 0.016 × FS. Pure: the caller supplies the
        /// world (<paramref name="seabedY"/> under the NEW position, the water level, the solid
        /// boxes and the map's footprint scale) and gets the next state back.
        /// </summary>
        public static State Step(State s, Sticks sticks, float dt, float seabedY, float waterLevel,
                                 Box[] solids, float scaleX, float scaleZ)
        {
            float lx = ApplyDeadZone(sticks.Lx);
            float ly = ApplyDeadZone(sticks.Ly);
            float rx = ApplyDeadZone(sticks.Rx);
            float ry = ApplyDeadZone(sticks.Ry);

            // Turn in place. Unity yaw grows clockwise seen from above, so +lx turns right.
            s.Yaw += lx * YawRate * dt;

            float sin = (float)Math.Sin(s.Yaw), cos = (float)Math.Cos(s.Yaw);
            float fwd = -ry;        // stick UP (negative screen Y) = forward
            float strafe = rx;
            float lift = -ly;       // stick UP = ascend

            // forward = (sin, 0, cos), right = (cos, 0, −sin)
            float tx = (fwd * sin + strafe * cos) * Speed;
            float tz = (fwd * cos - strafe * sin) * Speed;
            float ty = lift * Speed * LiftRatio;

            s.Vel.X += (tx - s.Vel.X) * Inertia;
            s.Vel.Y += (ty - s.Vel.Y) * Inertia;
            s.Vel.Z += (tz - s.Vel.Z) * Inertia;

            var np = new Vec3(s.Pos.X + s.Vel.X * dt,
                              s.Pos.Y + s.Vel.Y * dt,
                              s.Pos.Z + s.Vel.Z * dt);

            // Solids: push out through the SHALLOWEST face, and never downward — the web lets
            // you come to rest on top of a wreck but never shoves you through the seabed.
            if (solids != null)
            {
                for (int i = 0; i < solids.Length; i++)
                {
                    Box o = solids[i];
                    if (np.X <= o.MinX - CamRadius || np.X >= o.MaxX + CamRadius ||
                        np.Y <= o.MinY - CamRadius || np.Y >= o.MaxY + CamRadius ||
                        np.Z <= o.MinZ - CamRadius || np.Z >= o.MaxZ + CamRadius) continue;

                    float pxL = np.X - (o.MinX - CamRadius);
                    float pxR = (o.MaxX + CamRadius) - np.X;
                    float pzL = np.Z - (o.MinZ - CamRadius);
                    float pzR = (o.MaxZ + CamRadius) - np.Z;
                    float pyT = (o.MaxY + CamRadius) - np.Y;
                    float m = Math.Min(Math.Min(pxL, pxR), Math.Min(Math.Min(pzL, pzR), pyT));

                    if (m == pxL) { np.X = o.MinX - CamRadius; s.Vel.X = Math.Min(0f, s.Vel.X); }
                    else if (m == pxR) { np.X = o.MaxX + CamRadius; s.Vel.X = Math.Max(0f, s.Vel.X); }
                    else if (m == pzL) { np.Z = o.MinZ - CamRadius; s.Vel.Z = Math.Min(0f, s.Vel.Z); }
                    else if (m == pzR) { np.Z = o.MaxZ + CamRadius; s.Vel.Z = Math.Max(0f, s.Vel.Z); }
                    else { np.Y = o.MaxY + CamRadius; if (s.Vel.Y < 0f) s.Vel.Y = 0f; }
                }
            }

            // Sand and surface.
            float floor = seabedY + CamRadius + FloorClearance;
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
