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

        // ── the ambient bands themselves, and the floor under the ground band ─────
        //
        // 🔴 WO-E4. Everything above this line is about the floor DepthAtmosphere/EnvMode/DroneLights
        // must not drop under. Everything below is about the band those systems start from, because
        // that is where the black was coming from and the floor above never reached it.

        /// <summary>
        /// The web's hemisphere light, verbatim — builder.html:510,
        /// <c>new THREE.HemisphereLight(0xbfe6ff, 0x123040, 1.05)</c>, mapped onto Unity's three
        /// Trilight bands (sky = the web's sky × its intensity, ground = the web's ground × the
        /// same, equator = what a hemisphere returns at the horizon, i.e. the midpoint).
        ///
        /// 🔎 These used to live as three literals in <c>AppBoot.SetupLighting</c> and nowhere else,
        /// which is why nothing could reason about them: the floor in this file was built out of the
        /// WATER colour and had no idea what the band it was flooring actually was. They are here so
        /// that <see cref="GroundBandAt"/> and AppBoot cannot drift apart, and so the guarantee
        /// below is a property of a function instead of a hope about a literal.
        /// </summary>
        public static readonly SeabedGeom.Rgb WebSkyBand = new SeabedGeom.Rgb(0.787f, 0.947f, 1.050f);
        public static readonly SeabedGeom.Rgb WebEquatorBand = new SeabedGeom.Rgb(0.430f, 0.572f, 0.657f);
        public static readonly SeabedGeom.Rgb WebGroundBand = new SeabedGeom.Rgb(0.074f, 0.198f, 0.264f);

        /// <summary>
        /// 🔴 THE FLOOR THE WHOLE OF WO-E4 IS ABOUT, and it is not a number somebody picked.
        ///
        /// It is the RED of <see cref="WebGroundBand"/> at the surface, in scene-linear —
        /// 0.006451, which is <see cref="ToneMap.BlackFloor"/> × 3.470. The rule it states is
        /// "the ambient ground band never attenuates below the value it has at the surface, in the
        /// channel that has the least to give", and the reason it has to exist is arithmetic:
        ///
        ///   <see cref="DepthLight.Attenuation"/>'s red is 0.2500 at depth (it bottoms out at
        ///   <see cref="DepthLight.Floor"/>), so the band's red lands at 0.87 × BlackFloor — BELOW
        ///   the point where <see cref="ToneMap.Fit"/> goes negative and <c>Clamp01</c> makes it
        ///   exactly byte 0. Past about 15 m, every down-facing surface in the app had its red
        ///   channel pinned to 0 by construction, whatever the model's albedo. Measured, not
        ///   inferred: the app logged its own band as gnd=(0.022, 0.148, 0.231) at the QC depth and
        ///   sRGB 0.022 decodes to 0.0017 linear.
        ///
        /// 🔎 Why it is tied to BlackFloor rather than typed in. A literal would survive exactly
        /// until the next person edited the tone curve's exposure, at which point the crush point
        /// would move and the literal would not, silently. Expressing it as "the surface value of
        /// the darkest channel" also means it can never be BRIGHTER than the light rig was
        /// authored to be, so it cannot become a second, competing exposure control.
        /// </summary>
        public static float BandFloorLinear => ToneMap.SrgbToLinear(WebGroundBand.R);

        /// <summary>
        /// <see cref="BandFloorLinear"/> expressed in <see cref="ToneMap.BlackFloor"/>s: 3.470.
        /// For scale, the web's own fog red sits at 3.255 and the shipped app at depth sits at
        /// 0.87 — the two straddle the crush point, and that is the difference the user has been
        /// describing as "บนเว็บแสดงผลดีกว่ามาก".
        /// </summary>
        public static float BandFloorMultiple => BandFloorLinear / ToneMap.BlackFloor;

        /// <summary>
        /// How much brighter than "the web's hemisphere ground colour, dimmed" an underside really
        /// is on these maps — the seabed bounce.
        ///
        /// 🔴 Why a gain and not a bigger floor. The floor above rescues RED, which is the channel
        /// that dies first; it does nothing for the failure the user actually photographed, which
        /// is a whole statue reading black. That one is about level, not hue: with the band as
        /// shipped, the darkest albedo that can still make it off a down-facing surface at the QC
        /// depth is sRGB 72 — and 47.9% of the Singha atlas is darker than 71. Half the model was
        /// guaranteed to be pure black wherever it faced down, by arithmetic, before a single
        /// texel was sampled.
        ///
        /// 2.5 moves that crush threshold to sRGB 44 at the QC depth (23.4 m) and 53 at 52 m, i.e.
        /// below the dark mass of the atlas rather than through the middle of it. It is bounded
        /// above by the thing an underside has to keep being: at 2.5 the ground band reaches 0.36
        /// of the equator band in blue (it was 0.146) — clearly darker than a flank, which is what
        /// stops a model reading as a sticker; parity with the flank would need 6.9.
        ///
        /// 🔎 It is NOT the web's number, because the web is not modelling this scene. three.js's
        /// 0x123040 is a mood colour for open water; these maps sit on a 340-unit sheet of lit
        /// sand directly below every object, and this file's own docs have said so since it was
        /// written ("the sand here is a bright 340-unit reflector directly below, so that is most
        /// of it, not a token amount"). The shipped band delivered 1.4% of the sky band. This is
        /// the line finally agreeing with its own comment.
        ///
        /// Ramped in by <see cref="Strength"/> like everything else here, so the map view and the
        /// shallows keep the tuning they already have and the correction arrives with the depth
        /// that causes it.
        /// </summary>
        public const float SeabedBounceGain = 2.5f;

        /// <summary>The bounce gain in force at this depth: 1 at and above the surface, easing to
        /// <see cref="SeabedBounceGain"/> by <see cref="FullStrengthMetres"/>.</summary>
        public static float SeabedBounce(float depthUnits)
            => 1f + (SeabedBounceGain - 1f) * Strength(depthUnits);

        /// <summary>
        /// The ambient GROUND band the scene must render at this depth, as an authored (sRGB)
        /// colour ready for <c>RenderSettings.ambientGroundColor</c>.
        ///
        /// 🔎 This models BOTH writers, on purpose, so that one function is the answer to "what
        /// does a down-facing surface actually get": the depth dim <c>DepthAtmosphere</c> applies
        /// (authored ⊙ attenuation, in light — <see cref="ToneMap.ScaleLight"/>), the seabed bounce
        /// above, and then the per-channel floor. <c>UnderwaterShading</c> raises the frame's real
        /// value to this, and it runs last and only ever raises, so no other writer can undercut
        /// it. There is no second copy of the arithmetic anywhere.
        ///
        /// Above the surface it is simply the authored band and no floor is applied — the map view
        /// was tuned over four QC rounds and this must not reach up there.
        /// </summary>
        public static SeabedGeom.Rgb GroundBandAt(float depthUnits)
        {
            DepthLight.Attenuation(depthUnits, out float kr, out float kg, out float kb);
            float g = SeabedBounce(depthUnits);

            var band = new SeabedGeom.Rgb(ToneMap.ScaleLight(WebGroundBand.R, kr * g),
                                          ToneMap.ScaleLight(WebGroundBand.G, kg * g),
                                          ToneMap.ScaleLight(WebGroundBand.B, kb * g));
            if (depthUnits <= 0f || float.IsNaN(depthUnits)) return band;

            float floor = BandFloorLinear;
            return new SeabedGeom.Rgb(ToneMap.LiftLight(band.R, floor),
                                      ToneMap.LiftLight(band.G, floor),
                                      ToneMap.LiftLight(band.B, floor));
        }

        /// <summary>
        /// The ambient EQUATOR band at this depth — a side face's light. No bounce and no floor:
        /// this band was never near the crush point (its weakest channel is 20 × BlackFloor at any
        /// depth), and it is here so the ground band has something measurable to be a fraction OF.
        /// </summary>
        public static SeabedGeom.Rgb EquatorBandAt(float depthUnits)
        {
            DepthLight.Attenuation(depthUnits, out float kr, out float kg, out float kb);
            return new SeabedGeom.Rgb(ToneMap.ScaleLight(WebEquatorBand.R, kr),
                                      ToneMap.ScaleLight(WebEquatorBand.G, kg),
                                      ToneMap.ScaleLight(WebEquatorBand.B, kb));
        }

        /// <summary>The ambient SKY band at this depth — an up-facing surface's light.</summary>
        public static SeabedGeom.Rgb SkyBandAt(float depthUnits)
        {
            DepthLight.Attenuation(depthUnits, out float kr, out float kg, out float kb);
            return new SeabedGeom.Rgb(ToneMap.ScaleLight(WebSkyBand.R, kr),
                                      ToneMap.ScaleLight(WebSkyBand.G, kg),
                                      ToneMap.ScaleLight(WebSkyBand.B, kb));
        }

        /// <summary>
        /// An authored band's channels in <see cref="ToneMap.BlackFloor"/>s — the one number that
        /// says, from a log line alone, whether the band is above the tone curve's crush point.
        /// Under 1.0 means that channel is byte 0 on every down-facing surface in the scene.
        /// </summary>
        public static SeabedGeom.Rgb BlackFloorRatios(SeabedGeom.Rgb authoredSrgb)
            => new SeabedGeom.Rgb(ToneMap.SrgbToLinear(authoredSrgb.R) / ToneMap.BlackFloor,
                                  ToneMap.SrgbToLinear(authoredSrgb.G) / ToneMap.BlackFloor,
                                  ToneMap.SrgbToLinear(authoredSrgb.B) / ToneMap.BlackFloor);

        /// <summary>
        /// The darkest base colour this project promises to keep off byte 0 — sRGB 71, measured on
        /// the cliff/statue atlas the Singha sits on (47.9% of its texels are darker than this).
        /// Quoted as the authored byte because that is how the measurement was taken.
        /// </summary>
        public const float DarkestProtectedSrgb = 71f / 255f;
    }
}
