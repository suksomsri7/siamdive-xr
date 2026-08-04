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
    /// 🟡 KNOWN, DELIBERATELY NOT FIXED HERE — the angular direction. This app builds its polar
    /// grid on UNITY's z (SceneBuilder.BuildPolarGrid: <c>bz = sin(ang)</c>) while items are
    /// placed at Unity z = −web z (WebCoord.PositionToUnity), which on paper means the app's
    /// segment j holds the web's segment (seg − j), and that <c>env.areaSlopeZ</c> would need
    /// its sign flipped too. Reading both files says so — but no independent picture of the SAND
    /// has been taken that shows it, and a seabed is not something to reshape on an argument
    /// alone. What would settle it: a render (or a depth ray) of the app's floor against the
    /// web's on a map with a deep sculpt — Atlantis has a 97-unit trench and is the map for it.
    /// Until then this converter changes the NUMBERING only, never the geometry.
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
            int from = IsWebLayout(web, rings, seg) ? 1 : 0;   // skip the web's centre slot
            for (int i = 0; i < app.Length && i + from < web.Length; i++) app[i] = web[i + from];
            return app;
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
            for (int i = 0; i < app.Length && i + 1 < web.Length; i++) web[i + 1] = app[i];
            return web;
        }
    }
}
