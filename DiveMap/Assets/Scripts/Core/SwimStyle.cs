using System;
using System.Text.RegularExpressions;

namespace DiveMap.Core
{
    /// <summary>How an animal produces thrust. The three are not interchangeable.</summary>
    public enum SwimGait
    {
        /// <summary>A wave runs head→tail and the tail sweeps SIDEWAYS. Every bony fish and shark.</summary>
        Body = 0,
        /// <summary>Same wave, but the fluke sweeps UP and DOWN. Whales, dolphins, orcas.</summary>
        Fluke = 1,
        /// <summary>No tail beat at all — the pectoral WINGS flap, tip lagging the shoulder.
        /// Mantas and rays. Driving a manta with a tail wave is the single most obvious
        /// "this was animated by a programmer" tell in the whole reef.</summary>
        Wing = 2,
    }

    /// <summary>One species' body-wave numbers, resolved for the size it is actually drawn at.</summary>
    public readonly struct SwimWave
    {
        public readonly SwimGait Gait;
        /// <summary>Tail-beats (or wing-flaps) per second at cruise.</summary>
        public readonly double BeatHz;
        /// <summary>Tail-tip / wingtip travel to ONE side, as a fraction of body length (or span).</summary>
        public readonly double Amp;
        /// <summary>How many wavelengths fit along the body (or half-span).</summary>
        public readonly double Cycles;
        /// <summary>How much the NOSE swings the other way. 0 = head welded, 0.2 = a real recoil.</summary>
        public readonly double Recoil;
        /// <summary>Slow amplitude drift so the beat is not a metronome (0..1).</summary>
        public readonly double Gust;
        /// <summary>Hard cap on how far this animal leans into a turn (radians).</summary>
        public readonly double MaxBankRad;

        public SwimWave(SwimGait gait, double beatHz, double amp, double cycles,
                        double recoil, double gust, double maxBankRad)
        {
            Gait = gait; BeatHz = beatHz; Amp = amp; Cycles = cycles;
            Recoil = recoil; Gust = gust; MaxBankRad = maxBankRad;
        }
    }

    /// <summary>
    /// What a given animal's swimming should look like — classified from its asset id and
    /// scaled by the size it is actually drawn at. Pure and table-driven, in the same spirit
    /// as <see cref="SpeciesGenome"/>, so the whole thing is testable without a scene.
    ///
    /// 🔴 Why this exists. "ปลาว่ายไม่สมจริง ตัวแข็ง" was reported three times. The first two
    /// answers were one set of numbers for every animal in the sea:
    ///
    ///   • v1 rotated the whole fish about its up axis — nose and tail swinging together,
    ///     which is a plank on a hinge, not a fish.
    ///   • v2 (DM_FishWave) bent the body properly but gave a sardine, a barracuda and a
    ///     manta ray the SAME lateral tail wave, and only ever reached three species at all.
    ///     A manta does not have a tail to beat.
    ///
    /// The eye reads three things and none of them is "is it moving": the direction the wave
    /// travels, how the beat rate tracks body size, and whether the animal leans into its
    /// turns. Those three are what this table encodes.
    ///
    /// The ORDER of the classification is load-bearing, exactly like SpeciesGenome's:
    ///   • <c>/ray/</c> before anything else would swallow "moray" — a moray is an EEL and
    ///     undulates over its whole length; a ray flaps wings. Opposite motions.
    ///   • guitarfish ("guitar_shark") looks like a ray and swims like a shark. Body, not Wing.
    ///   • crabs, shrimp and clams have no swimming gait at all and must come out near-still,
    ///     or the reef floor starts to wobble.
    /// </summary>
    public static class SwimStyle
    {
        /// <summary>
        /// World units per metre, for turning a draw size into a beat rate.
        ///
        /// 🔴 This was 12.0 and that was a bug with a plausible-sounding derivation. The maps are
        /// NOT metric, so the number is a convention — and the project already HAS one:
        /// <see cref="DepthLight.UnitsPerMetre"/> is 6, transcribed from the web's
        /// <c>U_PER_M = 6</c> (builder.html:600), which is what turns a seabed height into the
        /// depth in metres the diver UI prints. Two different metres in one codebase is not a
        /// preference, it is one of them being wrong.
        ///
        /// The 12 came from reading FishMeshFactory's note backwards: a scad is drawn 4.20 u and
        /// was said to "read as ~35 cm", so 4.20/0.35 = 12. But nothing in the app ever agreed to
        /// that; at the project's own scale a 4.20 u fish is 0.70 m, which is a perfectly ordinary
        /// size for the fish being drawn. The consequence was not academic: every animal was
        /// believed to be HALF its real length, and beat rate goes as 1/√L, so every fin in the
        /// sea ran √2 = 1.41× too fast on top of whatever the table said — the "ครีบขยับเร็วไปมาก
        /// เป็นกับสัตว์ทุกตัว" the user reported from a real iPhone.
        ///
        /// It only ever picks a TEMPO, and the tempo should track apparent size — which is what
        /// the eye judges anyway. A user who scales a shark up gets a slower beat, which is
        /// correct whatever the map's units are supposed to mean.
        /// </summary>
        public const double UnitsPerMetre = 6.0;

        /// <summary>
        /// The web's fin rate for an ordinary shoal, in Hz.
        ///
        /// 🔴 The single most important number in this file, because it is the one the user has
        /// actually been looking at. builder.html:1506 wiggles a school's vertices with
        /// <c>sin(uTime * wRate + …)</c> and <c>wRate</c> defaults to 7.0 — RADIANS per second, so
        /// 7/2π = 1.114 Hz — and it does not scale with speed, with size, or with anything else.
        /// Every shoal in the web build, from a sardine to a batfish, beats at exactly this rate.
        ///
        /// That is why schools do not go through the k/√L formula at all: no amount of calibrating
        /// a size law reproduces a constant, and "ของเดิมบนเว็บดีกว่ามาก" is a statement about the
        /// constant.
        /// </summary>
        public const double SchoolBeatHzDefault = 7.0 / (2.0 * Math.PI);   // 1.114 Hz

        /// <summary>
        /// …and the one shoal the web tunes by hand: <c>wiggleRate: 5.0</c> on
        /// <c>school:barracuda</c> (builder.html:1098) = 0.796 Hz, together with a 0.06 amplitude
        /// and a 0.15 stiffness — "ตัวแข็งสะบัดหาง", stiff body, flicking tail, which is what a
        /// barracuda does and what the user asked for on 2026-07-09.
        /// </summary>
        public const double SchoolBeatHzBarracuda = 5.0 / (2.0 * Math.PI);   // 0.796 Hz

        /// <summary>
        /// What <see cref="SpeciesFlag.SlowAnim"/> is worth as a beat-rate multiplier.
        ///
        /// The web expresses the same idea as a clip-playback rate: an ordinary animal plays at
        /// <c>clamp(eff*0.45, 0.9, 3.0)</c> and the whale shark at <c>clamp(eff*0.30, 0.32, 0.85)</c>
        /// (builder.html:2445) — 0.28× to 0.67× of ordinary depending on where in the band it
        /// sits. It cannot be transcribed directly because the web's whale shark GLB carries no
        /// clip at all (no <c>animated:true</c> on its MODULES row), so there the tail simply does
        /// not move; this app bends the mesh instead and therefore has to choose a number.
        /// </summary>
        public const double SlowAnimMul = 0.75;

        /// <summary>Effort floor — a stopped animal sculls, it does not freeze.</summary>
        public const double EffortMin = 0.35;

        /// <summary>
        /// Effort ceiling. Was 2.20; 2.00 keeps the worst case (a small reef fish at a dead
        /// sprint) inside the 2.5 Hz ceiling the calibration is checked against.
        /// </summary>
        public const double EffortMax = 2.00;

        /// <summary>
        /// The effort a SHOAL beats at — always, whatever it is doing.
        ///
        /// 🔴 The web has no effort term at all: <c>uAmp</c> is set once at build time and
        /// <c>wRate</c> is baked into the shader source as a literal. A panicking shoal in the web
        /// swims faster and beats exactly the same. Ours multiplied both the rate and the
        /// amplitude by <c>DartMul</c>, so a scatter came out as a swarm of vibrating fish — the
        /// user's "เร็วไปมาก" with a mechanism. DartMul keeps its job (it is what makes a scatter
        /// a scatter) and loses this one.
        /// </summary>
        public const double SchoolEffort = 1.0;

        /// <summary>Draw size in world units → apparent metres.</summary>
        public static double Metres(double worldLen)
            => (worldLen > 0.0 ? worldLen : 1.0) / UnitsPerMetre;

        private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        // ── Classification. Order matters; see the class comment. ─────────────────
        /// <summary>Things with no swimming gait — they walk, cling, or sit.</summary>
        private static readonly Regex RxStill =
            new Regex("crab|lobster|shrim|mantis_shrimp|clam|urchin|starfish|anemone|coral|barnacle|nudibranch|seahorse|pygmy|stonefish|scorpionfish|flounder|sea_star", Opt);

        /// <summary>
        /// Names the still-list swallows by accident. <c>coral</c> is in it for the coral heads,
        /// and it also matches <c>mdl:coralfish</c> — a perfectly ordinary reef fish that the web
        /// gives <c>speedMul: 0.8</c> (builder.html:1853). Left alone it comes out with a 1.2 %
        /// tail amplitude and hangs in the water like a decal.
        /// </summary>
        private static readonly Regex RxNotStill =
            new Regex("coral_?fish|coralfish|anemone_?fish|clownfish", Opt);

        /// <summary>Anguilliform — the WHOLE body waves, several wavelengths of it.</summary>
        private static readonly Regex RxEel =
            new Regex("moray|eel|sea_serpent|serpent|snake|oarfish|ribbon", Opt);

        /// <summary>Looks like a ray, swims like a shark. Checked BEFORE the wing list.</summary>
        private static readonly Regex RxNotWing =
            new Regex("guitar|sawfish|angel_shark", Opt);

        /// <summary>Pectoral-wing flappers.</summary>
        private static readonly Regex RxWing =
            new Regex("manta|mobula|devil_ray|eagle_ray|stingray|sting_ray|cownose|batoid|(^|[:_])ray($|[_])", Opt);

        /// <summary>Front-flipper rowers — a turtle beats its forelimbs, it has no tail thrust.</summary>
        private static readonly Regex RxTurtle = new Regex("turtle|loggerhead|hawksbill", Opt);

        /// <summary>Vertical tail fluke (and the flipper-driven air-breathers that read the same).</summary>
        private static readonly Regex RxFluke =
            new Regex("whale|dolphin|orca|beluga|humpback|sperm|porpoise|dugong|manatee|seal|penguin", Opt);

        /// <summary>Sharks: a stiff front half and a big slow tail. NOT the same beat as a sardine.</summary>
        private static readonly Regex RxShark =
            new Regex("shark|hammerhead|thresher|silvertip|blacktip|whitetip|nurse|tiger_shark|bull_shark|great_white|leopard_shark|dogfish", Opt);

        /// <summary>Stiff-bodied high-speed cruisers — small tail arc, fast beat.</summary>
        private static readonly Regex RxThunniform =
            new Regex("tuna|bonito|sailfish|marlin|wahoo|dorado|barracuda|trevally|yellowtail|jack|mola", Opt);

        /// <summary>
        /// The bare asset id inside whatever string the caller had to hand. The hero animals are
        /// wired up from a pivot GameObject called <c>Item_7_msh:whaleshark</c>, and the two
        /// lookups below are EXACT-match table reads rather than regexes, so they would silently
        /// miss on the prefixed form and hand a whale shark an ordinary shark's tempo.
        /// Anchored at the end, so a name with no <c>prefix:id</c> token in it (<c>Whale_Shark_xr0</c>)
        /// falls through unchanged and the regex classification still gets its chance.
        /// </summary>
        private static readonly Regex RxBareId = new Regex(@"(?:^|_)([a-z0-9]+:[a-z0-9_]+)$", Opt);

        /// <summary>See <see cref="RxBareId"/>. Never null.</summary>
        private static string Bare(string assetId)
        {
            string id = assetId ?? "";
            Match m = RxBareId.Match(id);
            return m.Success ? m.Groups[1].Value : id;
        }

        /// <summary>
        /// The web's fixed rate for this shoal, or a negative number when this is not a shoal.
        ///
        /// 🔴 <c>school:</c> and not <c>pod:</c>, and that is the web's own line rather than a
        /// guess: builder.html sets <c>instanced = !pod</c> (:1502) and only the instanced branch
        /// gets the vertex wiggle at all. A pod is a handful of real animals at natural size —
        /// <c>pod:humpback</c> is two humpback whales — and 1.11 Hz is a sardine's tempo, not a
        /// whale's. Pods stay on the size law, which at their drawn sizes (16 × defaultScale, so
        /// 20-24 u) lands them at 0.5-0.7 Hz where they belong.
        /// </summary>
        public static double SchoolBeatHz(string assetId)
        {
            string id = Bare(assetId);
            if (!id.StartsWith("school:", StringComparison.OrdinalIgnoreCase)) return -1.0;
            return id.StartsWith("school:barracuda", StringComparison.OrdinalIgnoreCase)
                 ? SchoolBeatHzBarracuda
                 : SchoolBeatHzDefault;
        }

        /// <summary>Which gait this asset id swims with.</summary>
        public static SwimGait GaitFor(string assetId)
        {
            string id = assetId ?? "";
            if (RxEel.IsMatch(id)) return SwimGait.Body;      // an eel is a body wave, just a longer one
            if (RxNotWing.IsMatch(id)) return SwimGait.Body;
            // 🔴 SHARKS BEFORE FLUKES, and this line is the whole reason the ordering is written
            // down. "whaleshark" contains "whale". A whale shark is a SHARK — its tail is vertical
            // and sweeps SIDEWAYS — and it is the hero animal on the demo map, the very animal in
            // the screenshot that came back with "ตัวแข็งเป็นแท่ง". Classify it as a fluke and it
            // beats its tail up and down like a dolphin, which is worse than not moving at all.
            if (RxShark.IsMatch(id)) return SwimGait.Body;
            if (RxTurtle.IsMatch(id)) return SwimGait.Wing;   // front flippers = wings
            if (RxWing.IsMatch(id)) return SwimGait.Wing;
            if (RxFluke.IsMatch(id)) return SwimGait.Fluke;
            return SwimGait.Body;
        }

        /// <summary>
        /// True when this asset has no swimming motion to give it at all.
        ///
        /// 🔴 Two sources, and the second one is not optional. The name list above catches the
        /// obvious ones (crab, clam, urchin). The web ALSO marks species stationary by hand
        /// (<c>BEHAVIOR_CFG</c>'s <c>stationary:true</c>), and three animals in this app's own
        /// manifest are only reachable that way: <c>mdl:leafy_seadragon</c> (no "seahorse" in the
        /// name — it is a seaDRAGON), <c>mdl:giant_clam</c>… which the name list does catch, and
        /// <c>losin:garden_eel</c>, which the name list actively catches the WRONG way: it matches
        /// "eel", and the eel clause below un-stills it. A garden eel is anchored in its burrow
        /// and sways; giving it a two-wavelength anguilliform swim sends a colony of them
        /// undulating across the sand like a field of snakes. The hand-tuned row wins.
        /// </summary>
        public static bool IsStill(string assetId)
        {
            string id = assetId ?? "";
            if (SpeciesBehavior.For(id).Stationary) return true;
            if (RxNotStill.IsMatch(id)) return false;
            return RxStill.IsMatch(id) && !RxEel.IsMatch(id);
        }

        /// <summary>
        /// The wave numbers for <paramref name="assetId"/> at the size it is drawn
        /// (<paramref name="worldLen"/>, world units along its longest axis).
        ///
        /// Beat rate follows the observed 1/√L relationship — a big animal is unhurried doing the
        /// same thing a minnow does. Amplitudes are tail-tip travel to one side as a fraction of
        /// length, in the range real fish use (8-12 % carangiform, up to 18 % anguilliform); a
        /// manta's wingtip travels far more, which is why it needs its own number.
        ///
        /// 🔴 Every <c>k</c> below was re-derived on 2026-08-03 against the web, from a real iPhone
        /// report: "ครีบขยับเร็วไปมาก เป็นกับสัตว์ทุกตัว · ของเดิมบนเว็บดีกว่ามาก". Two things were
        /// wrong at once and they multiplied:
        ///
        ///   • <see cref="UnitsPerMetre"/> was 12 against the rest of the project's 6, so every
        ///     animal was taken for half its length and 1/√L handed out a spurious 1.41×;
        ///   • the k values themselves were set by eye against nothing, and the eye was calibrated
        ///     on a desktop preview rather than on the web build the user was comparing against.
        ///
        /// Together they ran 2.6-8.9× fast. The rates here are what lands the animals the map
        /// actually places within ±20 % of a calibrated target — see SwimStyleTests'
        /// <c>BeatRates_MatchTheCalibratedTargets</c>, which is the acceptance test for this work
        /// and quotes each target and the size it is measured at.
        ///
        /// The ceilings came down harder than the k's did (shark 4.0 → 2.0, thunniform 6.0 → 2.2)
        /// because a ceiling is only ever reached by a very small animal, and a very small animal
        /// beating four times a second is precisely the buzz being removed.
        /// </summary>
        public static SwimWave For(string assetId, double worldLen)
        {
            string id = assetId ?? "";
            double m = Metres(worldLen);
            double root = Math.Sqrt(m > 1e-4 ? m : 1e-4);

            if (IsStill(id))
                return new SwimWave(SwimGait.Body, Rate(id, 0.8 / root, 0.15, 1.2),
                                    0.012, 1.0, 0.0, 0.5, Deg(6));

            SwimGait gait = GaitFor(id);

            if (gait == SwimGait.Wing)
            {
                bool turtle = RxTurtle.IsMatch(id);
                // A manta's flap is slow and enormous — wingtips sweeping a fifth of the span each
                // way. A turtle rows shallower. At EQUAL length the turtle now comes out the
                // slower of the two, which reads wrong on paper and right on screen: the map draws
                // an oceanic manta at 62 u and a turtle at 20 u, so what the eye compares is
                // 0.33 Hz of manta against 0.38 Hz of turtle.
                double hz = turtle ? Rate(id, 0.7  / root, 0.25, 1.10)
                                   : Rate(id, 1.05 / root, 0.12, 1.35);
                return new SwimWave(SwimGait.Wing, hz,
                                    turtle ? 0.12 : 0.20,
                                    turtle ? 0.35 : 0.45,
                                    0.0,
                                    turtle ? 0.20 : 0.30,
                                    Deg(turtle ? 14 : 30));
            }

            if (gait == SwimGait.Fluke)
                return new SwimWave(SwimGait.Fluke, Rate(id, 1.15 / root, 0.18, 1.85),
                                    0.10, 0.60, 0.06, 0.25, Deg(18));

            // 🔎 The work order did not name a figure for anguilliform, so this one is the
            // carangiform correction (1.4/3.0 = 0.467) applied to the old 2.6 and 5.0. An eel is a
            // fish and was mis-scaled by exactly the same two mistakes; and what makes an eel read
            // as an eel is its 2.1 wavelengths and 17 % amplitude, not its tempo.
            if (RxEel.IsMatch(id))
                return new SwimWave(SwimGait.Body, Rate(id, 1.2 / root, 0.30, 2.3),
                                    0.17, 2.10, 0.25, 0.30, Deg(12));

            if (RxShark.IsMatch(id))
                return new SwimWave(SwimGait.Body, Rate(id, 1.1 / root, 0.25, 2.0),
                                    0.085, 0.75, 0.08, 0.30, Deg(26));

            if (RxThunniform.IsMatch(id))
                return new SwimWave(SwimGait.Body, Rate(id, 1.35 / root, 0.40, 2.2),
                                    0.075, 0.85, 0.06, 0.22, Deg(30));

            // Everything else: an ordinary carangiform fish.
            return new SwimWave(SwimGait.Body, Rate(id, 1.4 / root, 0.35, 2.2),
                                0.110, 0.95, 0.10, 0.28, Deg(32));
        }

        /// <summary>
        /// The one place a raw k/√L figure turns into a published beat rate, so that the two
        /// overrides cannot be forgotten on a gait: a shoal ignores the size law entirely and uses
        /// the web's constant, and a <see cref="SpeciesFlag.SlowAnim"/> animal beats slower than
        /// its size alone would say.
        ///
        /// Order matters. The shoal constant wins outright (it is not a correction to a rate, it
        /// IS the rate), and SlowAnim is applied AFTER the clamp so that scaling a whale shark up
        /// keeps making it slower instead of stopping at the shark floor.
        /// </summary>
        private static double Rate(string assetId, double raw, double lo, double hi)
        {
            double shoal = SchoolBeatHz(assetId);
            if (shoal > 0.0) return shoal;

            double hz = Clamp(raw, lo, hi);
            if (SpeciesBehavior.For(Bare(assetId)).SlowAnim) hz *= SlowAnimMul;
            return hz;
        }

        // ── Geometry ──────────────────────────────────────────────────────────────

        /// <summary>
        /// How long a box of size (<paramref name="sizeX"/>, <paramref name="sizeY"/>,
        /// <paramref name="sizeZ"/>) is when measured along the unit direction
        /// (<paramref name="ax"/>, <paramref name="ay"/>, <paramref name="az"/>).
        ///
        /// 🔴 The shader bends the mesh in the MESH's own space, but every length the marine
        /// pipeline records — <c>BakedLen</c>, the school's world length — is measured AFTER the
        /// glTF node transform has been applied. Feeding a post-bake length to <c>_WaveLen</c>
        /// is wrong by whatever scale that node carries: the nose→tail parameter <c>u</c> then
        /// stops reaching 1.0 at the tail, so the envelope that is supposed to concentrate the
        /// bend in the back third lands in the middle of the fish instead — a hinge, which is
        /// the exact thing the wave was written to stop looking like.
        ///
        /// This is the projected extent of an axis-aligned box, which is exact (no vertex reads —
        /// a Draco mesh is non-readable and <c>mesh.vertices</c> throws).
        /// </summary>
        public static double AxisExtent(double sizeX, double sizeY, double sizeZ,
                                        double ax, double ay, double az)
            => Math.Abs(ax) * Math.Abs(sizeX)
             + Math.Abs(ay) * Math.Abs(sizeY)
             + Math.Abs(az) * Math.Abs(sizeZ);

        // ── Per-frame drivers ─────────────────────────────────────────────────────

        /// <summary>
        /// How far the beat advances in <paramref name="dt"/> seconds, in radians.
        ///
        /// 🔴 The phase is INTEGRATED on the CPU and handed to the shader, instead of the
        /// shader computing <c>sin(_Time.y · speed)</c>. That form cannot have its speed
        /// changed: raising the beat rate at t = 900 s shifts the argument by hundreds of
        /// radians in one frame and the tail teleports. Integrating the phase makes a speed
        /// change continuous, which is what lets a fish beat harder when it darts.
        /// </summary>
        public static double BeatPhaseStep(double beatHz, double dt)
            => 2.0 * Math.PI * (beatHz > 0.0 ? beatHz : 0.0) * (dt > 0.0 ? dt : 0.0);

        /// <summary>
        /// Tail effort from how hard the animal is actually swimming: <paramref name="speed"/>
        /// against its own cruise speed. A gliding fish barely moves its tail; one sprinting
        /// throws it. Multiplies both the amplitude and the beat rate.
        ///
        /// Calibrated so that swimming AT cruise returns exactly 1.0, because that is what the
        /// table's amplitudes are quoted at and what <c>_WaveEffort</c> documents itself as. A
        /// stopped fish still gets 0.35 rather than 0: an animal holding station sculls, and a
        /// tail that stops dead is the stiffness this whole file exists to remove.
        ///
        /// 🔴 This is for SOLO animals. A shoal uses <see cref="SchoolEffort"/> — a constant —
        /// because the web has no effort term at all and multiplying a school's rate by its panic
        /// factor was half of "ครีบขยับเร็วไปมาก".
        /// </summary>
        public static double Effort(double speed, double cruiseSpeed)
        {
            double c = cruiseSpeed > 1e-4 ? cruiseSpeed : 1.0;
            double r = (speed > 0.0 ? speed : 0.0) / c;
            return Clamp(0.30 + 0.70 * r, EffortMin, EffortMax);
        }

        /// <summary>
        /// How far to lean into a turn. A fish does not pivot flat like a compass needle — it
        /// rolls into the corner and the whole body arcs. Saturating (tanh) rather than
        /// clamped so a hard turn eases up to the limit instead of hitting a wall, and it can
        /// never accumulate: it is a pure function of the CURRENT turn rate, so the stuck
        /// barrel-roll that bit the web build has nothing to latch onto.
        /// </summary>
        public static double BankRad(double yawRateRadPerSec, double maxBankRad)
        {
            const double Reference = 1.2; // rad/s ≈ a decisive turn
            double max = maxBankRad > 0.0 ? maxBankRad : 0.0;
            return -max * Math.Tanh(yawRateRadPerSec / Reference);
        }

        private static double Deg(double d) => d * Math.PI / 180.0;

        private static double Clamp(double v, double lo, double hi)
            => v < lo ? lo : (v > hi ? hi : v);
    }
}
