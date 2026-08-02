using System.Collections.Generic;

namespace DiveMap.Core
{
    /// <summary>Per-species switches the web sets by hand. Bit flags so a row is one word.</summary>
    [System.Flags]
    public enum SpeciesFlag
    {
        None       = 0,
        /// <summary>Never leaves its spot — it sways and bobs and that is all (crab, seahorse, clam).</summary>
        Stationary = 1 << 0,
        /// <summary>Lives ON the sand: rests on it, roots in it (nurse shark, stingray, octopus).</summary>
        Benthic    = 1 << 1,
        /// <summary>Hovers near structure in a tiny area (lionfish, boxfish, batfish, mola).</summary>
        Flat       = 1 << 2,
        /// <summary>A big animal: wide roaming, slow beat, low energy, joins loose aggregations.</summary>
        Big        = 1 << 3,
        /// <summary>Sit-and-wait hunter — engages only at half the ordinary prey radius.</summary>
        Ambush     = 1 << 4,
        /// <summary>Can breach clear of the water (humpback).</summary>
        Breacher   = 1 << 5,
        /// <summary>Sleeps hanging vertically (sperm whale).</summary>
        Sleeper    = 1 << 6,
        /// <summary>Feeds by swooping and barrel-rolling (mantas, devil/eagle rays).</summary>
        Manta      = 1 << 7,
        /// <summary>Cannot stop swimming — a ram-ventilating shark drowns if it does.</summary>
        NeverRest  = 1 << 8,
        /// <summary>Its clips play slower than the beat rate alone would suggest (whale shark).</summary>
        SlowAnim   = 1 << 9,
    }

    /// <summary>
    /// The web's hand-tuned per-species table, transcribed row for row — <c>BEHAVIOR_CFG</c>
    /// (builder.html:1772-1870) — plus <c>deriveLocomotion()</c> (builder.html:1927-1944), which
    /// turns a genome plus that row into "how far does this animal roam, and how fast".
    ///
    /// 🔴 Why the table has to exist rather than being folded into <see cref="SpeciesGenome"/>'s
    /// regexes. The genome answers "what KIND of animal is this" from the name, and that is enough
    /// for who-eats-whom. It is NOT enough for how an animal moves: a nurse shark and a blacktip
    /// are both "reef shark" to any regex, and one of them sleeps on the sand while the other
    /// cannot stop swimming at all. The web's answer is a hand-written row per species carrying
    /// three things a name cannot — the flags, a roaming radius in map units, and a speed.
    ///
    /// 🔴 The flags feed BACK into the genome. The web's <c>speciesGenome()</c> tests
    /// <c>cfg.stationary || cfg.benthic</c> → bottom and <c>cfg.flat</c> → reef *before* it looks
    /// at the name (builder.html:1888-1890). Skip that and seven species in this app's own
    /// manifest end up in the wrong water: the mola-mola (flat) becomes a pelagic wanderer instead
    /// of a basker hanging by the reef, the leafy seadragon and the giant clam (stationary) become
    /// mid-water swimmers, and the two batfish drift off the structure they exist to hang around.
    /// That is why <see cref="SpeciesGenome"/> calls in here.
    ///
    /// Pure lookup plus arithmetic. Every number is testable without a scene, and every row is
    /// tagged with the builder.html line it came from so a reviewer can diff it against the source.
    /// </summary>
    public static class SpeciesBehavior
    {
        /// <summary>
        /// "the web left this field out". JS <c>undefined</c>, which is emphatically not zero —
        /// <c>mdl:giant_clam</c> really does have <c>speedMul: 0</c> and it means something.
        /// Negative, because no real speed or radius in the table is.
        /// </summary>
        public const double NoValue = -1.0;

        /// <summary>One row of the web's BEHAVIOR_CFG.</summary>
        public readonly struct Cfg
        {
            /// <summary>True when the web actually wrote a row for this id.</summary>
            public readonly bool   Has;
            /// <summary>Hand-tuned speed multiplier, or <see cref="NoValue"/>.</summary>
            public readonly double SpeedMul;
            /// <summary>Hand-tuned roaming radius in map units, or <see cref="NoValue"/>.</summary>
            public readonly double RoamR;
            public readonly SpeciesFlag Flags;
            /// <summary>Clip playback multiplier, or <see cref="NoValue"/>.</summary>
            public readonly double AnimMul;

            public Cfg(double speedMul, double roamR, SpeciesFlag flags, double animMul)
            {
                Has = true; SpeedMul = speedMul; RoamR = roamR; Flags = flags; AnimMul = animMul;
            }

            public bool Is(SpeciesFlag f) => (Flags & f) != 0;
            public bool Stationary => Is(SpeciesFlag.Stationary);
            public bool Benthic    => Is(SpeciesFlag.Benthic);
            public bool Flat       => Is(SpeciesFlag.Flat);
            public bool Big        => Is(SpeciesFlag.Big);
            public bool Ambush     => Is(SpeciesFlag.Ambush);
            public bool Breacher   => Is(SpeciesFlag.Breacher);
            public bool Sleeper    => Is(SpeciesFlag.Sleeper);
            public bool Manta      => Is(SpeciesFlag.Manta);
            public bool NeverRest  => Is(SpeciesFlag.NeverRest);
            public bool SlowAnim   => Is(SpeciesFlag.SlowAnim);

            // 🔴 `Has &&` is load-bearing on all three. `default(Cfg)` — the answer for a species
            // the web never wrote a row for — has every double at 0.0, and 0.0 is a PERFECTLY
            // VALID hand-tuned value (mdl:giant_clam really is speedMul:0). Testing the sentinel
            // alone therefore reports "this species was hand-tuned to roam 0 units", and Derive
            // dutifully pins every un-tuned fish in the game to a point.
            /// <summary>Was a roaming radius hand-tuned? The clamp in <see cref="Derive"/> depends on it.</summary>
            public bool HasRoamR   => Has && RoamR >= 0.0;
            /// <summary>Was a speed multiplier hand-tuned?</summary>
            public bool HasSpeed   => Has && SpeedMul >= 0.0;
            /// <summary>Was a clip multiplier hand-tuned?</summary>
            public bool HasAnimMul => Has && AnimMul >= 0.0;
        }

        /// <summary>
        /// The row the web wrote, or an all-absent row — exactly JS's <c>BEHAVIOR_CFG[id] || {}</c>.
        /// Never throws, never allocates.
        /// </summary>
        public static Cfg For(string assetId)
        {
            if (!string.IsNullOrEmpty(assetId) && Table.TryGetValue(assetId, out Cfg c)) return c;
            return default;
        }

        /// <summary>Did the web hand-tune this species at all?</summary>
        public static bool HasRow(string assetId) => For(assetId).Has;

        /// <summary>Number of hand-tuned rows — the oracle for "the whole table came across".</summary>
        public static int RowCount => Table.Count;

        /// <summary>Every id the web wrote a row for.</summary>
        public static IEnumerable<string> Ids => Table.Keys;

        // ── BEHAVIOR_CFG — builder.html:1773-1869, transcribed row for row ────────
        // The trailing ":NNNN" on each line is the line it came from. The dictionary is keyed by
        // EXACT id (the web does BEHAVIOR_CFG[id], not a pattern match), so the order here is
        // documentation only — but it is the documentation that makes a diff against the web
        // possible at all, so keep it in source order.
        private static readonly Dictionary<string, Cfg> Table = new Dictionary<string, Cfg>
        {
            { "msh:turtle", new Cfg(0.5, 45, SpeciesFlag.None, NoValue) }, // :1773 // realistic sea turtle: slow, glides a lot
            { "glb_turtle_loggerhead", new Cfg(0.5, 45, SpeciesFlag.None, NoValue) }, // :1774
            { "msh:barracuda", new Cfg(0.5, 26, SpeciesFlag.None, NoValue) }, // :1775 // slow cruiser, small area
            { "msh:lionfish", new Cfg(0.15, 7, SpeciesFlag.Flat | SpeciesFlag.Ambush, NoValue) }, // :1776 // hovers near the floor, tiny area — ambush piscivore
            { "msh:crab", new Cfg(NoValue, NoValue, SpeciesFlag.Stationary, NoValue) }, // :1777 // sits on the floor — only sways/turns (no roaming)
            { "msh:seahorse", new Cfg(NoValue, NoValue, SpeciesFlag.Stationary, NoValue) }, // :1778 // stays put, only sways head/tail
            { "msh:whaleshark", new Cfg(0.6, 170, SpeciesFlag.Big | SpeciesFlag.NeverRest | SpeciesFlag.SlowAnim, NoValue) }, // :1779 // ฉลามวาฬ: ช้า สง่า ว่ายตลอดไม่หยุด (ฉลามหยุดว่ายไม่ได้) หางสะบัดกว้างช้า
            { "msh:manta", new Cfg(1.0, 90, SpeciesFlag.Big, NoValue) }, // :1780 // graceful wide glide, faster
            { "msh:trevally", new Cfg(0.9, 80, SpeciesFlag.None, NoValue) }, // :1781 // roams a wide area
            { "msh:boxfish", new Cfg(0.15, 7, SpeciesFlag.Flat, NoValue) }, // :1782 // hovers near the floor, tiny area
            { "school:barracuda", new Cfg(0.25, 6, SpeciesFlag.None, NoValue) }, // :1783 // stays put — the fish churn/reposition among themselves
            { "school:scad", new Cfg(0.2, 6, SpeciesFlag.None, NoValue) }, // :1784 // baitfish shoal stays put; internal motion only
            { "msh:tiger_shark", new Cfg(0.9, 85, SpeciesFlag.Big, NoValue) }, // :1786
            { "msh:silvertip_shark", new Cfg(1.0, 80, SpeciesFlag.None, NoValue) }, // :1787
            { "msh:leopard_shark", new Cfg(0.5, 40, SpeciesFlag.Benthic, NoValue) }, // :1788 // graceful; rests on the sand
            { "msh:nurse_shark", new Cfg(0.4, 28, SpeciesFlag.Benthic, NoValue) }, // :1789 // lies on the sand most of the time
            { "msh:whitetip_reef_shark", new Cfg(0.8, 55, SpeciesFlag.None, NoValue) }, // :1790
            { "msh:blacktip_reef_shark", new Cfg(1.0, 60, SpeciesFlag.None, NoValue) }, // :1791
            { "msh:thresher_shark", new Cfg(0.95, 90, SpeciesFlag.Big, NoValue) }, // :1792
            { "msh:bluefin_tuna", new Cfg(1.5, 95, SpeciesFlag.None, NoValue) }, // :1793 // fast
            { "msh:tuna", new Cfg(1.4, 90, SpeciesFlag.None, NoValue) }, // :1794
            { "msh:sailfish", new Cfg(1.9, 100, SpeciesFlag.None, NoValue) }, // :1795 // fastest fish
            { "msh:sperm_whale", new Cfg(0.7, 120, SpeciesFlag.Big | SpeciesFlag.Sleeper, NoValue) }, // :1796 // sleeps hanging vertical
            { "msh:beluga_whale", new Cfg(0.8, 80, SpeciesFlag.Big, NoValue) }, // :1797
            { "msh:orca", new Cfg(1.05, 100, SpeciesFlag.Big, NoValue) }, // :1798
            { "msh:dolphin_real", new Cfg(1.5, 95, SpeciesFlag.None, NoValue) }, // :1799 // playful & quick
            { "msh:humpback_whale", new Cfg(0.8, 400, SpeciesFlag.Big | SpeciesFlag.Breacher, 0.72) }, // :1800 // breach กระโดดพ้นน้ำ+สปินคอร์กสกรู + ครีบ/หางช้าลง (animMul)
            { "msh:oceanic_manta", new Cfg(1.0, 95, SpeciesFlag.Big | SpeciesFlag.Manta, NoValue) }, // :1801 // swoops + barrel-rolls to feed
            { "msh:black_manta", new Cfg(1.0, 90, SpeciesFlag.Big | SpeciesFlag.Manta, NoValue) }, // :1802
            { "msh:eagle_ray", new Cfg(1.1, 85, SpeciesFlag.Big | SpeciesFlag.Manta, NoValue) }, // :1803
            { "msh:mola_mola", new Cfg(0.35, 36, SpeciesFlag.Flat, NoValue) }, // :1804 // slow basker
            { "losin:octopus_ringed", new Cfg(0.3, 18, SpeciesFlag.Benthic, NoValue) }, // :1805
            { "losin:azure_stripe", new Cfg(0.8, 60, SpeciesFlag.None, NoValue) }, // :1806
            { "losin:pufferfish_bigeye", new Cfg(0.4, 40, SpeciesFlag.None, NoValue) }, // :1807
            { "losin:banded_fish", new Cfg(0.8, 60, SpeciesFlag.None, NoValue) }, // :1808
            { "losin:blue_spotted_stingray", new Cfg(0.5, 35, SpeciesFlag.Benthic, NoValue) }, // :1809
            { "losin:snapper_blushing", new Cfg(0.9, 80, SpeciesFlag.None, NoValue) }, // :1810
            { "losin:shrimp_acrobat", new Cfg(NoValue, NoValue, SpeciesFlag.Stationary, NoValue) }, // :1811
            { "losin:shrimp_longhorn", new Cfg(NoValue, NoValue, SpeciesFlag.Stationary, NoValue) }, // :1812
            { "losin:parrotfish_mosaic", new Cfg(0.7, 70, SpeciesFlag.None, NoValue) }, // :1813
            { "losin:octopus_crimson", new Cfg(0.3, 18, SpeciesFlag.Benthic, NoValue) }, // :1814
            { "losin:parrotfish_crimson", new Cfg(0.7, 70, SpeciesFlag.None, NoValue) }, // :1815
            { "losin:mantis_shrimp", new Cfg(0.25, 14, SpeciesFlag.Benthic, NoValue) }, // :1816
            { "losin:devil_ray", new Cfg(1.1, 110, SpeciesFlag.Big | SpeciesFlag.Manta, NoValue) }, // :1817
            { "losin:pufferfish_golden", new Cfg(0.4, 40, SpeciesFlag.None, NoValue) }, // :1818
            { "losin:garden_eel", new Cfg(NoValue, NoValue, SpeciesFlag.Stationary, NoValue) }, // :1819
            { "losin:guitar_shark", new Cfg(0.5, 50, SpeciesFlag.Benthic, NoValue) }, // :1820
            { "losin:hammerhead_shark", new Cfg(1.3, 300, SpeciesFlag.Big, NoValue) }, // :1821
            { "losin:parrotfish_honeycomb", new Cfg(0.7, 70, SpeciesFlag.None, NoValue) }, // :1822
            { "losin:indigo_velvet", new Cfg(0.6, 40, SpeciesFlag.None, NoValue) }, // :1823
            { "losin:kaleidoscope_beetle", new Cfg(0.3, 14, SpeciesFlag.Benthic, NoValue) }, // :1824
            { "losin:moray_leopard", new Cfg(0.3, 20, SpeciesFlag.Benthic | SpeciesFlag.Ambush, NoValue) }, // :1825 // ambush predator — holds a crevice, lunges at close prey
            { "losin:leopard_fish", new Cfg(0.8, 60, SpeciesFlag.None, NoValue) }, // :1826
            { "losin:lighthouse", new Cfg(NoValue, NoValue, SpeciesFlag.Stationary, NoValue) }, // :1827
            { "losin:marble_stingray", new Cfg(0.5, 40, SpeciesFlag.Benthic, NoValue) }, // :1828
            { "losin:moorish_idol", new Cfg(0.8, 60, SpeciesFlag.None, NoValue) }, // :1829
            { "losin:sea_serpent", new Cfg(0.7, 120, SpeciesFlag.None, NoValue) }, // :1830
            { "losin:octopus", new Cfg(0.3, 18, SpeciesFlag.Benthic, NoValue) }, // :1831
            { "losin:butterflyfish", new Cfg(0.8, 55, SpeciesFlag.None, NoValue) }, // :1832
            { "losin:parrotfish_prismatic", new Cfg(0.7, 70, SpeciesFlag.None, NoValue) }, // :1833
            { "losin:prismatic_reef", new Cfg(0.7, 50, SpeciesFlag.None, NoValue) }, // :1834
            { "losin:reef_manta_ray", new Cfg(1.0, 120, SpeciesFlag.Big | SpeciesFlag.Manta, NoValue) }, // :1835
            { "losin:blue_tang", new Cfg(0.8, 60, SpeciesFlag.None, NoValue) }, // :1836
            { "losin:scorpionfish", new Cfg(0.2, 12, SpeciesFlag.Benthic | SpeciesFlag.Ambush, NoValue) }, // :1837 // sit-and-wait ambush predator
            { "losin:lobster_aurora", new Cfg(0.25, 14, SpeciesFlag.Benthic, NoValue) }, // :1838
            { "losin:spotted_harbor", new Cfg(0.5, 50, SpeciesFlag.None, NoValue) }, // :1839
            { "losin:stonefish", new Cfg(NoValue, NoValue, SpeciesFlag.Stationary, NoValue) }, // :1840
            { "losin:clownfish_three_band", new Cfg(0.5, 18, SpeciesFlag.None, NoValue) }, // :1841
            { "losin:clownfish_two_band", new Cfg(0.5, 18, SpeciesFlag.None, NoValue) }, // :1842
            { "losin:bannerfish", new Cfg(0.8, 55, SpeciesFlag.None, NoValue) }, // :1843
            { "losin:parrotfish_yellowface", new Cfg(0.7, 70, SpeciesFlag.None, NoValue) }, // :1844
            { "losin:pygmy_seahorse", new Cfg(NoValue, NoValue, SpeciesFlag.Stationary, NoValue) }, // :1845
            { "mdl:bull_shark", new Cfg(1.15, 320, SpeciesFlag.Big, NoValue) }, // :1847 // apex ชายฝั่ง ลาดตระเวนกว้าง
            { "mdl:great_white_shark", new Cfg(1.2, 360, SpeciesFlag.Big, NoValue) }, // :1848 // apex มหาสมุทร
            { "mdl:whitetip_shark", new Cfg(1.1, 280, SpeciesFlag.None, NoValue) }, // :1849 // pelagic เดินเรื่อย อยากรู้อยากเห็น
            { "mdl:dorado", new Cfg(1.5, 300, SpeciesFlag.None, NoValue) }, // :1850 // ปลาผิวน้ำเร็วจัด
            { "mdl:batfish", new Cfg(0.7, 60, SpeciesFlag.Flat, NoValue) }, // :1851 // reef ชอบตามนักดำน้ำ
            { "mdl:batfish_juvenile", new Cfg(0.6, 35, SpeciesFlag.Flat, NoValue) }, // :1852 // วัยอ่อนเลียนใบไม้ ลอยช้าใกล้ที่กำบัง
            { "mdl:coralfish", new Cfg(0.8, 50, SpeciesFlag.Flat, NoValue) }, // :1853 // reef ทั่วไป
            { "mdl:sardine", new Cfg(1.1, 120, SpeciesFlag.None, NoValue) }, // :1854 // เหยื่อฝูง (schooler ผ่าน genome)
            { "mdl:seal", new Cfg(1.3, 220, SpeciesFlag.None, NoValue) }, // :1855 // ขี้เล่น ว่องไว นักล่าปลา
            { "mdl:penguin", new Cfg(1.4, 200, SpeciesFlag.None, NoValue) }, // :1856 // บินใต้น้ำ เร็วปราดเปรียว
            { "mdl:penguin_emperor", new Cfg(1.25, 220, SpeciesFlag.None, NoValue) }, // :1857 // ตัวใหญ่ ดำลึก
            { "mdl:leafy_seadragon", new Cfg(0.12, 6, SpeciesFlag.Stationary, NoValue) }, // :1858 // พรางเป็นใบไม้ แทบนิ่ง
            { "mdl:giant_clam", new Cfg(0, 1, SpeciesFlag.Stationary, NoValue) }, // :1859 // หอยมือเสือ อยู่กับที่
            { "school:batfish", new Cfg(NoValue, 8, SpeciesFlag.None, NoValue) }, // :1860 // ฝูงค้างคาว อยู่กับที่ churn ภายใน
            { "school:parrotfish_prismatic", new Cfg(NoValue, 10, SpeciesFlag.None, NoValue) }, // :1861
            { "pod:orca", new Cfg(1.15, 330, SpeciesFlag.Big, NoValue) }, // :1862 // ฝูงเพชฌฆาต ลาดตระเวนกว้าง (apex)
            { "pod:humpback", new Cfg(0.7, 400, SpeciesFlag.Big, 0.72) }, // :1864 // whole map — ครีบ/หางช้าลง (แม่-ลูก ไม่ breach)
            { "pod:eagle_ray", new Cfg(1.0, 280, SpeciesFlag.Big, NoValue) }, // :1865
            { "pod:dolphin", new Cfg(1.3, 330, SpeciesFlag.Big, NoValue) }, // :1866
            { "pod:yellowtail", new Cfg(0.8, 160, SpeciesFlag.None, NoValue) }, // :1867
            { "pod:hammerhead", new Cfg(1.1, 380, SpeciesFlag.Big, NoValue) }, // :1868 // 🦈 ฉลามหัวค้อนฝูง: ใส่สมอง + ว่ายทั่วแมพ (2026-06-26)
            { "pod:blacktip", new Cfg(1.2, 300, SpeciesFlag.Big, NoValue) }, // :1869 // 🦈 ฉลามครีบดำฝูง: ว่ายกว้าง
        };

        // ── deriveLocomotion — builder.html:1927-1944 ─────────────────────────────

        /// <summary>How far an animal roams and how fast it swims, relative to a mid-water fish.</summary>
        public readonly struct Locomotion
        {
            /// <summary>Roaming radius around the placement, in map units.</summary>
            public readonly double RoamR;
            /// <summary>Speed relative to an ordinary mid-water fish (1.0).</summary>
            public readonly double SwimMul;
            /// <summary>The unclamped, pre-personality zone/diet base — for tests and for the log.</summary>
            public readonly double BaseRoam;
            public Locomotion(double roamR, double swimMul, double baseRoam)
            {
                RoamR = roamR; SwimMul = swimMul; BaseRoam = baseRoam;
            }
        }

        /// <summary>
        /// The web's <c>deriveLocomotion()</c>, verbatim (builder.html:1927-1944).
        ///
        /// 🔴 Two details in here were bugs the web fixed and wrote down, and both are easy to
        /// drop in a port:
        ///   • the hand-tuned <c>cfg.roamR</c> WINS over the zone default. Overwriting it (which
        ///     the web's first cut did) made every species in a zone roam identically — the
        ///     lionfish, which is supposed to hover over seven units of sand, patrolled 85, and
        ///     the humpback was capped at 200 when its row asks for 400.
        ///   • the ceiling is 400 for a hand-tuned species and 200 for a derived one. One clamp
        ///     for both would either cage the humpback or let every unnamed mid-water fish roam
        ///     twice the map.
        ///
        /// <paramref name="energy"/> is the animal's own 0..1 personality draw: energetic
        /// individuals of the same species roam further and swim faster than lazy ones. Pass 0.5
        /// for "the average member of this species".
        /// </summary>
        public static Locomotion Derive(string assetId, double energy)
        {
            Cfg cfg = For(assetId);
            SpeciesGenome.Genome g = SpeciesGenome.For(assetId);
            return Derive(cfg, g, energy);
        }

        /// <summary>As <see cref="Derive(string,double)"/>, with both tables already resolved.</summary>
        public static Locomotion Derive(in Cfg cfg, in SpeciesGenome.Genome g, double energy)
        {
            double en = energy < 0.0 ? 0.0 : (energy > 1.0 ? 1.0 : energy);
            bool big = cfg.Big;

            double roam, sw;
            if (cfg.Stationary)                       { roam = 8.0;  sw = 0.15; }  // :1931 ปะการัง/ตัวนิ่ง
            else if (big)                             { roam = 330.0; sw = 0.9; }  // :1932 วาฬ/ตัวใหญ่
            else if (g.Zone == SpeciesGenome.ZonePelagic) { roam = 300.0; sw = 1.3; } // :1933 ปลาน้ำเปิด
            else if (g.Zone == SpeciesGenome.ZoneBottom)                              // :1934 หน้าดิน
            { roam = g.Diet == SpeciesGenome.DietPredator ? 70.0 : 42.0; sw = 0.55; }
            else if (g.Zone == SpeciesGenome.ZoneReef)    { roam = 85.0;  sw = 0.8; } // :1935 แนวปะการัง
            else                                          { roam = 160.0; sw = 1.0; } // :1936 กลางน้ำทั่วไป

            if (g.Diet == SpeciesGenome.DietPredator)                                 // :1937
            {
                roam *= big ? 1.15 : 1.35;
                sw   *= big ? 1.22 : 1.28;
            }

            double baseRoam = cfg.HasRoamR ? cfg.RoamR : roam;                        // :1940
            double ceiling  = cfg.HasRoamR ? 400.0 : 200.0;                           // :1941
            double roamR    = baseRoam * (0.75 + en * 0.5);
            if (roamR > ceiling) roamR = ceiling;
            double swimMul  = sw * (0.85 + en * 0.3);                                 // :1942

            return new Locomotion(roamR, swimMul, roam);
        }

        /// <summary>
        /// The speed this animal is actually drawn at: the derived <c>swimMul</c> scaled by the
        /// hand-tuned <c>cfg.speedMul</c> when the web wrote one.
        ///
        /// The web applies the two in different places (<c>u.speedMul</c> is read by the position
        /// integrator, <c>u.swimMul</c> by the roaming code), and multiplying them is what a
        /// caller that only wants ONE number has to do. Kept here so nobody has to remember it.
        /// </summary>
        public static double CruiseMul(string assetId, double energy)
        {
            Cfg cfg = For(assetId);
            double sw = Derive(cfg, SpeciesGenome.For(assetId), energy).SwimMul;
            return cfg.HasSpeed ? sw * cfg.SpeedMul : sw;
        }

        // ── the stationary animal's whole behaviour — builder.html:2166-2172 ──────

        /// <summary>
        /// A stationary animal bobs on the spot and turns its head. That IS the behaviour: a crab
        /// that swims is worse than a crab that does nothing.
        ///
        /// The web (builder.html:2168-2170) holds it at its anchor, adds
        /// <c>sin(t·vSp + vph) · 0.4</c> to Y and <c>sin(t·0.5 + turnPh) · 0.12</c> to the yaw,
        /// and halves the clip's timescale (<c>mixer.timeScale = 0.5</c>). All three are here so
        /// a caller cannot get one of them and forget the others.
        /// </summary>
        public const double SwayAmpY      = 0.4;
        /// <summary>Yaw sway, radians either side (web :2170).</summary>
        public const double SwayAmpYaw    = 0.12;
        /// <summary>Yaw sway rate, rad/s of phase (web :2170).</summary>
        public const double SwayYawRate   = 0.5;
        /// <summary>Clip timescale for a stationary animal (web :2167).</summary>
        public const double SwayClipScale = 0.5;

        /// <summary>Vertical offset from the anchor at time <paramref name="t"/>.</summary>
        public static double SwayY(double t, double bobRate, double bobPhase)
            => System.Math.Sin(t * bobRate + bobPhase) * SwayAmpY;

        /// <summary>Yaw offset from the resting facing at time <paramref name="t"/>.</summary>
        public static double SwayYaw(double t, double turnPhase)
            => System.Math.Sin(t * SwayYawRate + turnPhase) * SwayAmpYaw;

        /// <summary>
        /// Does this animal hold station instead of roaming? True for the web's
        /// <c>cfg.stationary</c> rows, and for nothing else — a species with no row swims.
        /// </summary>
        public static bool IsStationary(string assetId) => For(assetId).Stationary;

        // ── personality — builder.html:1898-1904, drawn deterministically ─────────

        /// <summary>
        /// One individual's temperament, drawn from its species' ranges. The web draws these once
        /// per placed animal with <c>Math.random()</c>; here the draw is a hash of the animal's own
        /// seed, so the same map replays the same reef — the rule the rest of
        /// <see cref="FishMind"/> already lives by.
        /// </summary>
        public readonly struct Personality
        {
            public readonly double Energy, Boldness, Curiosity, Metabolism;
            public Personality(double energy, double boldness, double curiosity, double metabolism)
            {
                Energy = energy; Boldness = boldness; Curiosity = curiosity; Metabolism = metabolism;
            }
        }

        /// <summary>
        /// Draw a <see cref="Personality"/> for one individual. Four independent channels, so an
        /// animal's boldness is not correlated with its energy — drawing them from consecutive
        /// ticks of one stream is visible as "the fast ones are always the brave ones".
        /// </summary>
        public static Personality DrawPersonality(string assetId, uint seed)
        {
            SpeciesGenome.Genome g = SpeciesGenome.For(assetId);
            return new Personality(
                Lerp(g.EnergyMin,     g.EnergyMax,     FishMind.Rand01(seed, 0, 30)),
                Lerp(g.BoldMin,       g.BoldMax,       FishMind.Rand01(seed, 0, 31)),
                Lerp(g.CuriosityMin,  g.CuriosityMax,  FishMind.Rand01(seed, 0, 32)),
                Lerp(g.MetabolismMin, g.MetabolismMax, FishMind.Rand01(seed, 0, 33)));
        }

        private static double Lerp(double a, double b, double k) => a + (b - a) * k;
    }
}
