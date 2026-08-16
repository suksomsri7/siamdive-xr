namespace DiveMap.Core
{
    /// <summary>
    /// The sculpted seabed heights, across the boundary between the web's array and this app's.
    ///
    /// ── The web's array (builder.html:537-551, :3263-3265) ────────────────────────────
    ///   length  = 1 + rings·seg          ("SB_TOPN")
    ///   index 0 = the centre vertex
    ///   ring r (1..rings), segment s → <c>1 + (r-1)·seg + s</c>
    ///
    /// ── This app's array (SceneBuilder.HeightAt, SeabedView.SculptAt, SculptBrush.SampleXZ) ──
    ///   length  = rings·seg              (no centre slot)
    ///   ring r, segment j → <c>(r-1)·seg + j</c>
    ///
    /// The two are offset by exactly one slot, and the arithmetic says so without anyone having
    /// to render a picture: Atlantis ships <c>env.sculpt</c> with 2689 values for a 28×96 grid,
    /// and 28·96 = 2688. The app read that array as though its first value were ring 1
    /// segment 0, when the web had written the CENTRE of the floor there — so every dune and
    /// trench came out one segment (3.75°) around from where it was dug.
    ///
    /// ── 🔴 16 ส.ค. 2026: การกลับด้านเชิงมุม — แก้แล้ว ────────────────────────────────
    /// เดิมจดไว้ว่า "รู้ว่าน่าจะกลับด้าน แต่ยังไม่แก้เพราะไม่มีภาพยืนยัน" · ตอนนี้เจ้าของงาน
    /// รายงานอาการตรงกับที่คำนวณไว้เป๊ะ: "พื้นทรายปรับระดับในเว็บ พอเปิดใน Unity กลับหัวกลับหาง"
    ///
    /// ที่มา: แอปสร้างวงกริดบน z ของ **Unity** (<c>SceneBuilder.BuildPolarGrid: bz = sin(ang)</c>)
    /// แต่ของทุกชิ้นในแมพถูกวางที่ Unity z = −(web z) (<c>WebCoord.PositionToUnity</c>) ⇒ จุดที่
    /// ดัชนีเดียวกันของสองฝั่งอยู่คนละซีกของแกน X = พื้นทรายถูกสะท้อนกระจกเทียบกับซากเรือที่วาง
    /// อยู่บนมัน · ร่องที่ขุดไว้ทางเหนือของแมพจึงไปโผล่ทางใต้
    ///
    /// แก้ที่ "การแปลงเลขดัชนี" ที่เดียว: app segment j ↔ web segment (seg − j) mod seg
    /// (สลับกลับไปกลับมาได้ในตัว — ใช้สูตรเดียวกันทั้งขาเข้าและขาออก)
    ///
    /// 🔴 ความชันของพื้น (<c>env.areaSlopeZ</c>) ต้องกลับเครื่องหมายด้วยเหตุผลเดียวกัน และมันอยู่
    /// คนละที่ (SceneBuilder อ่าน env) — สองอย่างนี้ต้องมาคู่กันเสมอ ไม่งั้นพื้นเอียงผิดทาง
    /// ทั้งที่ร่องถูกที่
    /// </summary>
    public static class SculptCoord
    {
        /// <summary>Length of the array the web writes for this grid.</summary>
        public static int WebLength(int rings, int seg) => 1 + rings * seg;

        /// <summary>Length of the array this app works in.</summary>
        public static int AppLength(int rings, int seg) => rings * seg;

        /// <summary>True when this array carries the web's leading centre slot.</summary>
        public static bool IsWebLayout(float[] a, int rings, int seg)
            => a != null && rings > 0 && seg > 0 && a.Length >= WebLength(rings, seg);

        /// <summary>
        /// <c>env.sculpt</c> → the app's own grid. Returns null for null.
        ///
        /// An array too short to be the web's is passed through unchanged: the only thing that
        /// ever wrote one is an older build of THIS app (SeabedSculptor.Commit before this fix),
        /// and a map sculpted in the app should keep looking the way its owner left it rather
        /// than shifting a slot on first open.
        /// </summary>
        public static float[] WebToApp(float[] web, int rings, int seg)
        {
            if (web == null || rings <= 0 || seg <= 0) return web;

            var app = new float[AppLength(rings, seg)];
            bool fromWeb = IsWebLayout(web, rings, seg);
            int from = fromWeb ? 1 : 0;   // skip the web's centre slot
            for (int r = 1; r <= rings; r++)
            {
                for (int j = 0; j < seg; j++)
                {
                    // 🔴 สะท้อนเฉพาะของที่มาจาก "เว็บ" เท่านั้น · อาร์เรย์สั้น = งานที่ปั้นในแอปรุ่นเก่า
                    // ซึ่งเขียนด้วยพิกัดของแอปอยู่แล้ว การสะท้อนมันคือการย้ายพื้นของคนที่ไม่ได้ขอ
                    int src = from + (r - 1) * seg + (fromWeb ? MirrorSeg(j, seg) : j);
                    if (src < web.Length) app[(r - 1) * seg + j] = web[src];
                }
            }
            return app;
        }

        /// <summary>
        /// ดัชนีเซกเมนต์ของอีกฝั่ง — สะท้อนรอบแกน X เพราะ Unity z = −(web z).
        /// เป็นฟังก์ชันที่สลับกลับตัวเองได้ (<c>Mirror(Mirror(j)) == j</c>) ⇒ ใช้ตัวเดียวกันทั้ง
        /// ขาเข้าและขาออก ไม่มีทางที่สองทิศจะหลุดจากกัน
        /// </summary>
        public static int MirrorSeg(int j, int seg)
        {
            if (seg <= 0) return j;
            int m = (seg - (j % seg)) % seg;
            return m < 0 ? m + seg : m;
        }

        /// <summary>
        /// The app's grid → <c>env.sculpt</c>, so a stroke made on a phone reopens in the same
        /// place on the web. The centre slot the app does not keep is filled from its innermost
        /// ring, which is where the mesh reads the centre's height from anyway
        /// (SceneBuilder.HeightAt, r = 0 → index 0).
        /// </summary>
        public static float[] AppToWeb(float[] app, int rings, int seg)
        {
            if (app == null || rings <= 0 || seg <= 0) return app;

            var web = new float[WebLength(rings, seg)];
            web[0] = app.Length > 0 ? app[0] : 0f;
            for (int r = 1; r <= rings; r++)
            {
                for (int j = 0; j < seg; j++)
                {
                    int from = (r - 1) * seg + j;
                    int to = 1 + (r - 1) * seg + MirrorSeg(j, seg);
                    if (from < app.Length && to < web.Length) web[to] = app[from];
                }
            }
            return web;
        }
    }
}
