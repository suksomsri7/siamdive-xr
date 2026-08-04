namespace DiveMap.Core
{
    /// <summary>
    /// The sculpted seabed, across the same right-handed/left-handed border as
    /// <see cref="WebCoord"/> — and it is a border, because the two sides number the floor's
    /// vertices differently as well as mirroring it.
    ///
    /// ── The web's array (builder.html:537-551, :3263-3265) ────────────────────────────
    ///   length  = 1 + rings·seg          ("SB_TOPN")
    ///   index 0 = the centre vertex
    ///   ring r (1..rings), segment s → <c>1 + (r-1)·seg + s</c>
    ///   that vertex sits at WEB local (cos a·bd, 0, sin a·bd), a = 2π·s/seg
    ///
    /// ── This app's array (SceneBuilder.HeightAt, SeabedView.SculptAt, SculptBrush.SampleXZ) ──
    ///   length  = rings·seg              (no centre slot)
    ///   ring r, segment j → <c>(r-1)·seg + j</c>
    ///   that vertex sits at UNITY local (cos a·bd, 0, sin a·bd), a = 2π·j/seg
    ///
    /// Unity z = −web z, so the Unity vertex at angle a is the WEB vertex at angle −a: the
    /// segment index has to be reflected, <c>j → (seg − j) mod seg</c>. Feeding the web's
    /// array straight in — which is what shipped — laid the sculpt on the floor mirrored
    /// front-to-back AND shifted one segment across (the web's centre value pushed everything
    /// along by one), so on Atlantis a 97-unit trench sat on the opposite side of the map from
    /// the ruins that were placed around it.
    ///
    /// Fixed HERE, at the boundary, rather than inside the mesh builder: the brush, the depth
    /// bake and the mesh all already agree with each other in Unity space, and the one thing
    /// that disagreed was the JSON.
    /// </summary>
    public static class SculptCoord
    {
        /// <summary>The web segment index that holds the Unity segment <paramref name="j"/>.</summary>
        public static int MirrorSegment(int j, int seg)
        {
            if (seg <= 0) return 0;
            int m = (seg - (j % seg)) % seg;
            return m < 0 ? m + seg : m;
        }

        /// <summary>Length of the array the web writes for this grid.</summary>
        public static int WebLength(int rings, int seg) => 1 + rings * seg;

        /// <summary>Length of the array this app works in.</summary>
        public static int AppLength(int rings, int seg) => rings * seg;

        /// <summary>
        /// <c>env.sculpt</c> → the app's own grid. Returns null for null.
        ///
        /// An array too short to be the web's is passed through unchanged: the only thing that
        /// ever wrote one is an older build of THIS app (SeabedSculptor.Commit before the fix),
        /// and a map sculpted in the app should keep looking the way its owner left it rather
        /// than being mirrored on first open.
        /// </summary>
        public static float[] WebToApp(float[] web, int rings, int seg)
        {
            if (web == null || rings <= 0 || seg <= 0) return web;

            var app = new float[AppLength(rings, seg)];
            if (web.Length < WebLength(rings, seg))
            {
                for (int i = 0; i < app.Length && i < web.Length; i++) app[i] = web[i];
                return app;
            }

            for (int r = 1; r <= rings; r++)
            {
                for (int j = 0; j < seg; j++)
                    app[(r - 1) * seg + j] = web[1 + (r - 1) * seg + MirrorSegment(j, seg)];
            }
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

            for (int r = 1; r <= rings; r++)
            {
                for (int j = 0; j < seg; j++)
                {
                    int src = (r - 1) * seg + MirrorSegment(j, seg);
                    web[1 + (r - 1) * seg + j] = src < app.Length ? app[src] : 0f;
                }
            }
            return web;
        }

        /// <summary>
        /// <c>env.areaSlopeZ</c> into the app's frame. The web tilts its floor by
        /// <c>z_web · slopeZ</c>, and z_web = −z_unity, so the same tilt is −slopeZ here.
        /// Its own inverse, like the Z flip it comes from.
        /// </summary>
        public static double SlopeZ(double slopeZ) => -slopeZ;
    }
}
