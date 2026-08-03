using System;

namespace DiveMap.Core
{
    /// <summary>
    /// The least light an underwater surface is allowed to be lit by, expressed as the colour of
    /// the water it is standing in.
    ///
    /// 🔴 Why this exists — the measurement, not the hunch.
    ///
    /// "The sharks on Posidon are black" was chased twice through the models and both times the
    /// models were innocent. Pulling the GLBs the app actually loads and decoding their KTX2
    /// textures says so in numbers (thresher / tiger / blacktip / whitetip / leopard / hammerhead /
    /// great white, plus Stone_King and white_cluster):
    ///
    ///   • base colour, averaged over the used part of the atlas: sRGB 108-202 (0.42-0.79).
    ///     The shark maps are nearly WHITE. Nothing here is a dark model.
    ///   • metallic-roughness, blue channel = metal: average 1-5 / 255 → 0.004-0.02.
    ///     metallicFactor is 1, but 1 × 0.004 is not metal, so the reflection cubemap and the
    ///     metallic tame-down in SceneBuilder are both irrelevant to these objects (and the
    ///     tame-down skips them anyway — it only touches materials with no metal map).
    ///   • green channel = roughness: 0.30-0.53. Ordinary. alphaMode OPAQUE, emissive map black.
    ///
    /// So the albedo term was never the problem. The light term was. At 55 m, in the tour, the
    /// ambient an object's side face receives had fallen to roughly (0.14, 0.20, 0.26) — the
    /// authored ambient, timesed down by the depth curve and again by the headlamp's atmosphere
    /// multiplier — while the WATER right behind it, painted by the backdrop ramp and the fog, is
    /// (0.125, 0.386, 0.563). A mid-grey face therefore returned about a quarter of the light of
    /// the water directly behind it, and under a third of it in green and blue, the two channels
    /// the eye reads hue from. An opaque object that is darker than the medium it sits in is not a
    /// dark object; it is a hole. That is what "black silhouette" means, and it is why brightening
    /// the models could never have worked.
    ///
    /// The fix is the same structural trick <see cref="WaterFog"/> uses for the fog: stop authoring
    /// a second set of numbers that somebody has to keep in step with the water, and read the water
    /// instead. The floor here IS the backdrop ramp at the camera's depth — so an up-facing white
    /// surface returns exactly as much light as the water around it and can never silhouette, a
    /// side face returns <see cref="EquatorOfSky"/> of that, and an underside
    /// <see cref="GroundOfSky"/> — not zero, because the seabed on these maps is a 340-unit sheet
    /// of bright sand and it bounces. Retune a gradient stop and the lighting follows it.
    ///
    /// UnityEngine-free like the rest of Core, so the relationship ("an underwater surface is not
    /// darker than the water") is a property a test can assert rather than a screenshot somebody
    /// has to squint at.
    /// </summary>
    public static class UnderwaterLight
    {
        /// <summary>
        /// Depth, in metres, by which the floor is fully in.
        ///
        /// It ramps rather than switching on because the map view sits at or above the surface and
        /// its ambient was tuned over four QC rounds — this must not touch it. The problem being
        /// fixed is a DEEP-water problem (the dimmers multiply, and the water they are dimming
        /// toward gets more saturated the further down you go), so the correction arrives with the
        /// depth that causes it. 22 m is past the recreational shallows and short of where
        /// <see cref="WaterFog"/>'s ramp bottoms out, so the two depth cues never fight.
        /// </summary>
        public const float FullStrengthMetres = 22f;

        /// <summary>
        /// A side face sees less of the downwelling light than an up-facing one. Kept well above
        /// zero: this is the number that decides whether the flank of a shark reads as a shark or
        /// as a hole, and it is the term that had collapsed.
        /// </summary>
        public const float EquatorOfSky = 0.80f;

        /// <summary>
        /// And an underside sees only what the sand throws back — but the sand here is a bright
        /// 340-unit reflector directly below, so that is most of it, not a token amount. This is
        /// what lets a shark's white belly read as white while its dark back stays dark: the
        /// countershading the model was painted with survives instead of being crushed to black.
        /// </summary>
        public const float GroundOfSky = 0.70f;

        /// <summary>
        /// The requirement, stated as a number so a test can hold it: a mid-grey side face lit by
        /// nothing but this floor returns at least this fraction of the water behind it, in EVERY
        /// channel. Below roughly a quarter the eye stops reading an object and starts reading a
        /// silhouette, which is exactly where the scene was.
        /// </summary>
        public const float MinLitFraction = 0.30f;

        /// <summary>Albedo the fraction above is quoted for — an ordinary mid-grey surface.</summary>
        public const float MidGreyAlbedo = 0.45f;

        /// <summary>
        /// How much of the floor applies at this depth: 0 at and above the surface, easing to 1 by
        /// <see cref="FullStrengthMetres"/>. Smoothstepped so there is no visible step as the diver
        /// crosses that depth, and NaN-safe because depth is a subtraction of two world positions.
        /// </summary>
        public static float Strength(float depthUnits)
        {
            float metres = depthUnits / DepthLight.UnitsPerMetre;
            if (float.IsNaN(metres) || metres <= 0f) return 0f;

            float t = metres / FullStrengthMetres;
            if (t >= 1f) return 1f;
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// The three Trilight ambient colours an underwater scene must not fall below at this
        /// depth. Above the surface all three are black, i.e. no opinion — the map view keeps
        /// whatever it was given.
        /// </summary>
        public static void AmbientFloor(float depthUnits,
                                        out SeabedGeom.Rgb sky,
                                        out SeabedGeom.Rgb equator,
                                        out SeabedGeom.Rgb ground)
        {
            float k = Strength(depthUnits);
            SeabedGeom.Rgb water = WaterFog.ColorAt(depthUnits);

            sky     = Scale(water, k);
            equator = Scale(water, k * EquatorOfSky);
            ground  = Scale(water, k * GroundOfSky);
        }

        public static SeabedGeom.Rgb Scale(SeabedGeom.Rgb c, float k) =>
            new SeabedGeom.Rgb(c.R * k, c.G * k, c.B * k);

        /// <summary>
        /// Per-channel maximum — a FLOOR, never a set.
        ///
        /// 🔎 This is deliberately the only way the value is applied. Three other systems write the
        /// same three RenderSettings colours (the daylight toggle, the depth curve, the headlamp's
        /// atmosphere swap) and every previous attempt to make one of them the owner ended with a
        /// later writer silently winning — the black tail fins and the ghost maps in the log both
        /// came from that. Refusing to go below a value cannot lose an argument, because it never
        /// has one: whatever another system asked for survives wherever it is already brighter.
        /// </summary>
        public static SeabedGeom.Rgb Raise(SeabedGeom.Rgb current, SeabedGeom.Rgb floor) =>
            new SeabedGeom.Rgb(Math.Max(current.R, floor.R),
                               Math.Max(current.G, floor.G),
                               Math.Max(current.B, floor.B));

        /// <summary>
        /// What a side face of albedo <paramref name="albedo"/> returns compared with the water
        /// directly behind it, when the floor is the only light on it. 1 means it matches the
        /// water; the reported scene was at 0.21-0.26 in green and blue.
        /// </summary>
        public static void LitFraction(float depthUnits, float albedo,
                                       out float r, out float g, out float b)
        {
            // Only the sideways term is wanted here; the other two are discarded.
            float k = Strength(depthUnits) * EquatorOfSky;
            SeabedGeom.Rgb water = WaterFog.ColorAt(depthUnits);
            SeabedGeom.Rgb equator = Scale(water, k);

            r = Ratio(equator.R * albedo, water.R);
            g = Ratio(equator.G * albedo, water.G);
            b = Ratio(equator.B * albedo, water.B);
        }

        private static float Ratio(float lit, float water) => water <= 1e-6f ? 1f : lit / water;
    }
}
