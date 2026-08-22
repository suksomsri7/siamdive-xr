using System;

namespace DiveMap.Core
{
    /// <summary>
    /// E8+D9 — where the diver appears when the drone starts.
    ///
    /// The rule the user asked for: <b>every</b> entry into the drone begins at a warp gate, and a
    /// map with several gates picks one at random. A portal is the one object in a map whose whole
    /// job is "come here" (see <c>WarpGate</c>), so it is also the one landmark a dive can start
    /// from and still make sense: you stepped through, and here you are.
    ///
    /// ── beside the gate, not on it ────────────────────────────────────────────────────────────
    /// The gate hovers 14 u above the sand and its halo is 11 u across, so spawning ON it puts the
    /// camera inside the rings — and, worse, inside <see cref="TriggerRadius"/>, which would open
    /// the destination picker on the first frame of the dive, over and over. The diver is therefore
    /// placed <see cref="Clearance"/> units to one side, which is outside <see cref="RearmRadius"/>
    /// as well, so the gate arms cleanly instead of firing.
    ///
    /// ── which side, and facing where ──────────────────────────────────────────────────────────
    /// Toward the middle of the map, and looking that way. The other candidate was "along the
    /// gate's own facing", but a gate's yaw is whatever the builder happened to drag it to, so half
    /// the maps would open on empty sand with the content behind you. Aiming at the centre is the
    /// rule D9 already uses for a random spawn (<see cref="DroneFlight.RandomSpawn"/> +
    /// <see cref="DroneFlight.YawToward"/>) — "the edge of a map is nothing to look at" — and one
    /// direction then serves both jobs at once: stepping toward the centre is stepping AWAY from
    /// the gate, so the portal is behind you and the map is in front of you.
    ///
    /// ── legal water ───────────────────────────────────────────────────────────────────────────
    /// The spawn is clamped between the sand and the surface with <see cref="DroneFlight"/>'s own
    /// clearances — the same numbers the flight model will enforce on the very next frame, so the
    /// dive cannot begin with the camera being shoved somewhere else — and then settled against the
    /// solids by running <see cref="DroneFlight.Step"/> with the sticks at rest. Reusing Step is
    /// deliberate: the push-out, the sand floor, the surface ceiling and the map boundary are all
    /// rules that already exist and are already tested, and a second copy of them here is a second
    /// copy to get wrong.
    ///
    /// Pure: the caller supplies the gates, the seabed height and the random number, so a QC run
    /// can pin the draw exactly like D9 does.
    /// </summary>
    public static class WarpSpawn
    {
        /// <summary>Fly this close to a gate and the destination picker opens (the web's 13 u).</summary>
        public const float TriggerRadius = 13f;

        /// <summary>The trigger re-arms only after leaving this ring (the web's 16 u).</summary>
        public const float RearmRadius = 16f;

        /// <summary>
        /// How far to one side of the gate the diver lands. Must stay above
        /// <see cref="RearmRadius"/> — a spawn inside the ring is a picker in the player's face
        /// before they have touched a stick — and above the gate's 11 u halo so the dive does not
        /// open with an additive disc across the whole screen.
        /// </summary>
        public const float Clearance = 22f;

        /// <summary>
        /// Settling passes against the solids. One is enough for a spawn in open water; the rest
        /// are for a gate that a builder tucked into a wreck, where being pushed out of one box
        /// lands you in the next.
        /// </summary>
        public const int SettlePasses = 4;

        /// <summary>The 60 Hz frame Step is tuned on — this is a settle, not a flight.</summary>
        private const float SettleDt = 0.016f;

        /// <summary>Where the dive begins. <see cref="Index"/> &lt; 0 = no gate on this map.</summary>
        public struct Result
        {
            /// <summary>The gate that was drawn, or −1 when the map has none.</summary>
            public int Index;
            /// <summary>How many gates the map has (for the log oracle).</summary>
            public int Count;
            public DroneFlight.Vec3 Pos;
            /// <summary>Radians, Unity convention (0 = +Z) — looking at the middle of the map.</summary>
            public float Yaw;

            /// <summary>False = the caller keeps whatever it was going to do (D9, or the fixed view).</summary>
            public bool AtWarp => Index >= 0;
        }

        /// <summary>
        /// Which gate this dive starts at. Deterministic in <paramref name="rnd01"/> — the caller
        /// owns the RNG (the app hands it <c>UnityEngine.Random.value</c>, a test hands it a
        /// number) so the same draw always gives the same gate.
        /// </summary>
        public static int PickIndex(int count, float rnd01)
        {
            if (count <= 0) return -1;
            if (count == 1) return 0;
            float r = Clamp01(rnd01);
            int i = (int)(r * count);
            if (i >= count) i = count - 1;   // rnd01 == 1 exactly
            if (i < 0) i = 0;
            return i;
        }

        /// <summary>
        /// Keep a height in the water: above the sand by the drone's own floor clearance, below the
        /// surface by its ceiling clearance. Floor wins when the water is thinner than both, which
        /// is the order <see cref="DroneFlight.Step"/> resolves them in.
        /// </summary>
        public static float ClampDepth(float y, float seabedTopY, float waterLevel)
        {
            float floor = seabedTopY + DroneFlight.CamRadius + DroneFlight.FloorClearance;
            float ceiling = waterLevel - DroneFlight.CeilingClearance;
            if (y < floor) y = floor;
            if (ceiling > floor && y > ceiling) y = ceiling;
            return y;
        }

        /// <summary>
        /// Distance from <paramref name="p"/> to the nearest gate — in all three axes, because that
        /// is what the gate trigger itself measures (<c>TourController.CheckWarpGates</c>).
        /// </summary>
        public static float NearestGateDistance(DroneFlight.Vec3[] gates, DroneFlight.Vec3 p)
        {
            if (gates == null || gates.Length == 0) return float.MaxValue;
            float best = float.MaxValue;
            for (int i = 0; i < gates.Length; i++)
            {
                float dx = gates[i].X - p.X, dy = gates[i].Y - p.Y, dz = gates[i].Z - p.Z;
                float d = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (d < best) best = d;
            }
            return best;
        }

        /// <summary>
        /// Put the diver beside gate <paramref name="index"/>, facing <paramref name="centre"/>.
        ///
        /// <paramref name="seabedTopY"/> is the sand under the gate, <paramref name="solids"/> the
        /// boxes the drone collides with (null is fine — a map still loading its hulls simply gets
        /// the clamp and the boundary), and <paramref name="scaleX"/>/<paramref name="scaleZ"/> the
        /// seabed stretch that defines the map's edge.
        /// </summary>
        public static Result Place(DroneFlight.Vec3[] gates, int index, DroneFlight.Vec3 centre,
                                   float seabedTopY, float waterLevel,
                                   DroneFlight.Solid[] solids, float scaleX, float scaleZ)
        {
            int n = gates == null ? 0 : gates.Length;
            if (n == 0 || index < 0 || index >= n)
                return new Result { Index = -1, Count = n };

            DroneFlight.Vec3 g = gates[index];

            // Horizontally out of the gate, toward the middle of the map.
            float dx = centre.X - g.X, dz = centre.Z - g.Z;
            float len = (float)Math.Sqrt(dx * dx + dz * dz);
            if (len < 1e-3f)
            {
                // A gate standing on the exact centre of the map: no "toward the middle" exists, so
                // any direction is as good as any other and +X is the one that is reproducible.
                dx = 1f; dz = 0f; len = 1f;
            }
            dx /= len; dz /= len;

            DroneFlight.Vec3 pos = Settle(Beside(g, dx, dz, Clearance, seabedTopY, waterLevel),
                                          seabedTopY, waterLevel, solids, scaleX, scaleZ);

            // The settle is allowed to move the diver — out of a wreck, off the boundary — and a
            // gate wedged against scenery can push them back INTO the ring it just took them out
            // of. One retry from further out costs nothing and saves the picker opening on frame 1.
            if (NearestGateDistance(gates, pos) < RearmRadius)
            {
                DroneFlight.Vec3 further =
                    Settle(Beside(g, dx, dz, Clearance * 1.75f, seabedTopY, waterLevel),
                           seabedTopY, waterLevel, solids, scaleX, scaleZ);
                if (NearestGateDistance(gates, further) > NearestGateDistance(gates, pos))
                    pos = further;
            }

            // 🔴 22 ส.ค. 2026 (b457 บนเครื่องจริง — แมพ Chang มี warp:0 สองอัน) — ประตูชนะ D9
            // โดยตั้งใจ (E8) ⇒ ด่าน SpawnIsClear ที่เพิ่มให้ D9 ไม่เคยถูกใช้บนแมพนี้เลย และ
            // Settle ก็ปล่อยจุด "สมดุลแรง" ผ่าน (นิ่ง ≠ ว่าง — บทเรียนเดียวกับ D9) ⇒ ผู้เล่นเกิด
            // ติดใต้ท้องเรือ**จุดเดิมเป๊ะทุกครั้ง** เพราะประตูไม่สุ่ม. ประตูที่พาไปเกิดในที่ที่
            // ขยับไม่ได้ = ประตูที่ใช้ไม่ได้ — ปฏิเสธแล้วให้ผู้เรียกตกไปเส้น D9 ซึ่งมีด่านครบ
            // (พาสแรกที่ solids ยัง null ไม่โดนเกณฑ์นี้ — SpawnIsClear(null) = ว่างเสมอ)
            if (!DroneFlight.SpawnIsClear(pos, solids))
                return new Result { Index = -1, Count = n };

            return new Result
            {
                Index = index,
                Count = n,
                Pos = pos,
                // Measured from where the diver ENDED up, not from where they were aimed: a settle
                // that slid them past a wreck must not leave them looking at its far side.
                Yaw = DroneFlight.YawToward(pos, centre),
            };
        }

        /// <summary>One step out of the gate, at a legal height.</summary>
        private static DroneFlight.Vec3 Beside(DroneFlight.Vec3 gate, float dx, float dz, float dist,
                                             float seabedTopY, float waterLevel)
            => new DroneFlight.Vec3(gate.X + dx * dist,
                                    // The gate's own height is already a comfortable one — the web
                                    // lifts every portal 14 u off the sand — so the diver arrives
                                    // level with the ring they came through, not under it.
                                    ClampDepth(gate.Y, seabedTopY, waterLevel),
                                    gate.Z + dz * dist);

        /// <summary>
        /// Let the flight model itself decide whether this spot is legal: sticks at rest, so the
        /// only thing that can move the diver is the push-out, the floor, the ceiling or the map
        /// boundary. Velocity starts and stays at zero, so the dive still begins from a standstill.
        ///
        /// 🔴 public ตั้งแต่ 22 ส.ค. 2026 — จุดเกิดสุ่ม (D9) ต้องผ่านด่านเดียวกันนี้ด้วย.
        /// user (build จริง แมพ Chang): "จะไปเกิดใต้ท้องเรือ" — D9 เลือกจุดจาก มุม+รัศมี+ความสูง
        /// เหนือทรายเท่านั้น ไม่เคยรู้จัก solids เลย ⇒ จุดที่ทรายมีเรือคร่อมอยู่ = เกิดในโพรง/ในลำเรือ.
        /// ประตูวาปมีด่านนี้มาตลอด ("pushed out of a wreck the gate happens to stand in") —
        /// จุดเกิดสุ่มโดนของจริงเข้าถึงรู้ว่าขาด
        /// </summary>
        public static DroneFlight.Vec3 Settle(DroneFlight.Vec3 pos, float seabedTopY, float waterLevel,
                                              DroneFlight.Solid[] solids, float scaleX, float scaleZ)
        {
            var s = new DroneFlight.State { Pos = pos };
            for (int i = 0; i < SettlePasses; i++)
            {
                DroneFlight.State next = DroneFlight.Step(s, default(DroneFlight.Sticks), SettleDt,
                                                          seabedTopY, waterLevel, solids,
                                                          scaleX, scaleZ);
                float dx = next.Pos.X - s.Pos.X, dy = next.Pos.Y - s.Pos.Y, dz = next.Pos.Z - s.Pos.Z;
                s = next;
                if (dx * dx + dy * dy + dz * dz < 1e-6f) break;   // nothing left to resolve
            }
            return s.Pos;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
