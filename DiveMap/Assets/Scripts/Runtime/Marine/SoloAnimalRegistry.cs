using DiveMap.Core;
using UnityEngine;

namespace DiveMap.Runtime.Marine
{
    /// <summary>
    /// C6 phase 2 — how the solo animals on a map find out about each other.
    ///
    /// 🔴 Why a registry rather than each animal scanning for itself. The obvious shape is a
    /// <c>FindObjectsByType</c> (or a physics overlap) inside each controller's Update. That is
    /// an allocation per animal per frame and an O(n²) scan per animal per frame — on a phone,
    /// with forty animals, that is 1,600 distance tests sixty times a second plus forty arrays
    /// of garbage. This does the same work ONCE every <see cref="FleeMath.SenseIntervalSeconds"/>
    /// (0.7 s, the web's own tick at builder.html:2178) into arrays that are allocated when the
    /// population grows and never again.
    ///
    /// 🔴 The budget. Past <see cref="MarineRouting.SoloHuntBudget"/> animals the surplus is
    /// registered but never scanned: it still swims, still has its species' roaming range, still
    /// flees the diver — it simply does not hunt. A map can legitimately contain two hundred
    /// placements of the same fish and 40,000 distance tests per tick is not a thing to hand a
    /// phone for a behaviour nobody can see at that density.
    ///
    /// Static because there is one reef. <see cref="Clear"/> is called when a map is torn down;
    /// a controller that is destroyed removes itself, so a stale entry cannot outlive its
    /// transform and hand out a position from a scene that no longer exists.
    /// </summary>
    public static class SoloAnimalRegistry
    {
        /// <summary>What the scan needs to know about one animal. Written at registration.</summary>
        public struct Entry
        {
            public WhaleController Owner;
            public int     Rank;
            public string  Diet;
            public float   ObsR;         // half the animal's own length — every radius builds on it
            public bool    MayHunt;      // inside the budget?
            public Vector3 Pos;          // refreshed each sense tick

            // ── results, written by Scan ──
            public bool    HasPrey;
            public Vector3 Prey;
            public bool    HasHunter;
            public Vector3 Hunter;
        }

        private static Entry[] _entries = new Entry[16];
        private static int     _count;
        private static float   _nextScan;
        /// <summary>True once anything in the population hunts. A reef of grazers never scans.</summary>
        private static bool    _predatorsExist;

        public static int Count => _count;

        // ── นักดำน้ำ (โดรน) — 17 ส.ค. 2026 ────────────────────────────────────────────
        // user: "ฟังก์ชันสัตว์ตกใจ...ยังไม่ได้ทำหรอ" · ระบบตกใจของ **ฝูงปลา** ทำมานานแล้ว
        // (FishSchoolSystem อ่านความเร็วนักดำน้ำผ่าน FleeMath) แต่สัตว์เดี่ยวตัวใหญ่ไม่เคยรับรู้
        // ว่ามีคนอยู่ตรงนั้นเลย — มันรู้จักแค่ "ผู้ล่า" กับ "เหยื่อ" ที่เป็นสัตว์ด้วยกัน
        //
        // เก็บเป็น static ตัวเดียว ไม่ยัดใส่ Entry ทุกตัว: นักดำน้ำมีคนเดียวเสมอ
        public static Vector3 DiverPos { get; private set; }
        public static float DiverSpeed { get; private set; }
        public static bool HasDiver { get; private set; }

        /// <summary>ทัวร์เรียกทุกเฟรมระหว่างดำ · เรียก <see cref="ClearDiver"/> เมื่อออกจากทัวร์</summary>
        public static void SetDiver(Vector3 pos, float speed)
        {
            DiverPos = pos; DiverSpeed = speed; HasDiver = true;
        }

        public static void ClearDiver() { HasDiver = false; DiverSpeed = 0f; }
        public static bool PredatorsExist => _predatorsExist;
        /// <summary>How many of the registered animals are inside the hunt budget.</summary>
        public static int HuntingCount => _count < MarineRouting.SoloHuntBudget
                                        ? _count : MarineRouting.SoloHuntBudget;

        /// <summary>
        /// Add an animal. Returns its index, which is also its place in the hunt budget queue —
        /// so the animals an author placed FIRST are the ones that keep their hunting when a map
        /// goes over budget, which at least makes the cut-off deterministic and authorable.
        /// </summary>
        public static int Register(WhaleController owner, string assetId, float obsR)
        {
            if (owner == null) return -1;

            if (_count >= _entries.Length)
            {
                var bigger = new Entry[_entries.Length * 2];
                System.Array.Copy(_entries, bigger, _count);
                _entries = bigger;
            }

            SpeciesGenome.Genome g = SpeciesGenome.For(assetId);
            int index = _count++;
            _entries[index] = new Entry
            {
                Owner = owner,
                Rank = g.Rank,
                Diet = g.Diet,
                ObsR = obsR > 0.01f ? obsR : 1f,
                MayHunt = MarineRouting.MayHunt(index),
                Pos = owner.transform.position,
            };
            if (g.Diet == SpeciesGenome.DietPredator) _predatorsExist = true;
            return index;
        }

        /// <summary>Drop an animal. Swap-with-last, so removal is O(1) and never allocates.</summary>
        public static void Unregister(WhaleController owner)
        {
            for (int i = 0; i < _count; i++)
            {
                if (_entries[i].Owner != owner) continue;
                _entries[i] = _entries[_count - 1];
                _entries[_count - 1] = default;
                _count--;
                if (i < _count)
                {
                    // 🔴 Two things have to move with the entry, and forgetting either is a bug
                    // that only shows up after something is destroyed mid-session:
                    //   • the moved animal must be TOLD its new index. It caches the slot and
                    //     reads its senses by it, so leaving the old one behind makes it read
                    //     another animal's prey and hunter — a shark chasing a fish it cannot
                    //     see, next to a fish fleeing nothing.
                    //   • it inherits the vacated index's budget, not its own old one, or a map
                    //     that loses an early animal keeps a permanent hole in the hunting
                    //     population while an over-budget animal stays switched off.
                    _entries[i].MayHunt = MarineRouting.MayHunt(i);
                    if (_entries[i].Owner != null) _entries[i].Owner.SetRegistrySlot(i);
                }
                return;
            }
        }

        /// <summary>Forget everything. Called when a map is torn down.</summary>
        public static void Clear()
        {
            for (int i = 0; i < _count; i++) _entries[i] = default;
            _count = 0;
            _nextScan = 0f;
            _predatorsExist = false;
        }

        /// <summary>
        /// Refresh every animal's nearest prey and nearest hunter, at most once every
        /// <see cref="FleeMath.SenseIntervalSeconds"/>. Safe to call from every animal's Update:
        /// the first one through the door each tick does the work and the rest return instantly.
        /// </summary>
        public static void Tick(float time)
        {
            if (_count < 2 || !_predatorsExist) return;
            if (time < _nextScan) return;
            _nextScan = time + (float)FleeMath.SenseIntervalSeconds;

            // Positions first, so the scan below reads one consistent snapshot rather than a mix
            // of this frame's and last tick's — two animals steering off different snapshots of
            // each other is how a chase develops a wobble nobody can explain.
            for (int i = 0; i < _count; i++)
            {
                WhaleController o = _entries[i].Owner;
                if (o != null) _entries[i].Pos = o.transform.position;
            }

            int scan = HuntingCount;
            for (int i = 0; i < scan; i++)
            {
                Entry me = _entries[i];
                if (me.Owner == null) continue;

                me.HasPrey = false;
                me.HasHunter = false;
                float senseR = (float)FleeMath.SenseRadius(me.ObsR);
                float best2 = senseR * senseR;
                float bestPrey2 = senseR * senseR;
                bool amPredator = me.Diet == SpeciesGenome.DietPredator;

                for (int j = 0; j < _count; j++)
                {
                    if (j == i) continue;
                    Entry other = _entries[j];
                    if (other.Owner == null) continue;

                    float dx = other.Pos.x - me.Pos.x, dz = other.Pos.z - me.Pos.z;
                    float d2 = dx * dx + dz * dz;
                    if (d2 > senseR * senseR) continue;

                    // Who frightens me — the same FleeMath relation the shoals use, so a filter
                    // feeder the size of a bus still scares nobody.
                    if (FleeMath.IsThreat(me.Rank, other.Rank, other.Diet) && d2 < best2)
                    {
                        best2 = d2; me.HasHunter = true; me.Hunter = other.Pos;
                    }
                    // …and who I frighten, which is the same test read the other way round. A
                    // thing can therefore never be both my prey and my predator.
                    else if (amPredator && d2 < bestPrey2 &&
                             FleeMath.IsThreat(other.Rank, me.Rank, me.Diet))
                    {
                        bestPrey2 = d2; me.HasPrey = true; me.Prey = other.Pos;
                    }
                }

                _entries[i] = me;
            }
        }

        /// <summary>What the last scan found for animal <paramref name="index"/>.</summary>
        public static bool TryGet(int index, out Entry entry)
        {
            if (index < 0 || index >= _count) { entry = default; return false; }
            entry = _entries[index];
            return true;
        }
    }
}
