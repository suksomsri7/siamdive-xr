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
        /// The maps are NOT metric — HTMS Chang has a water level of 240 units — so this is a
        /// perceptual scale, derived from the two sizes the marine system already documents:
        /// a scad is drawn 4.20 u long and reads as a ~35 cm fish, a barracuda 17.1 u and reads
        /// as ~1.5 m (FishMeshFactory's own note). 4.20/0.35 = 12.0 and 17.1/1.5 = 11.4.
        ///
        /// It only ever picks a TEMPO, and the tempo should track apparent size — which is what
        /// the eye judges anyway. A user who scales a shark up gets a slower beat, which is
        /// correct whatever the map's units are supposed to mean.
        /// </summary>
        public const double UnitsPerMetre = 12.0;

        /// <summary>Draw size in world units → apparent metres.</summary>
        public static double Metres(double worldLen)
            => (worldLen > 0.0 ? worldLen : 1.0) / UnitsPerMetre;

        private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        // ── Classification. Order matters; see the class comment. ─────────────────
        /// <summary>Things with no swimming gait — they walk, cling, or sit.</summary>
        private static readonly Regex RxStill =
            new Regex("crab|lobster|shrim|mantis_shrimp|clam|urchin|starfish|anemone|coral|barnacle|nudibranch|seahorse|pygmy|stonefish|scorpionfish|flounder|sea_star", Opt);

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

        /// <summary>True when this asset has no swimming motion to give it at all.</summary>
        public static bool IsStill(string assetId)
            => RxStill.IsMatch(assetId ?? "") && !RxEel.IsMatch(assetId ?? "");

        /// <summary>
        /// The wave numbers for <paramref name="assetId"/> at the size it is drawn
        /// (<paramref name="worldLen"/>, world units along its longest axis).
        ///
        /// Beat rate follows the observed 1/√L relationship — a 35 cm scad beats about five
        /// times a second, a 1.5 m barracuda about two and a half, a 10 m whale about one every
        /// two seconds — which is exactly why a whale looks unhurried doing the same thing a
        /// minnow does. Amplitudes are tail-tip travel to one side as a fraction of length, in
        /// the range real fish use (8-12 % carangiform, up to 18 % anguilliform); a manta's
        /// wingtip travels far more, which is why it needs its own number.
        /// </summary>
        public static SwimWave For(string assetId, double worldLen)
        {
            string id = assetId ?? "";
            double m = Metres(worldLen);
            double root = Math.Sqrt(m > 1e-4 ? m : 1e-4);

            if (IsStill(id))
                return new SwimWave(SwimGait.Body, Clamp(0.8 / root, 0.15, 1.2),
                                    0.012, 1.0, 0.0, 0.5, Deg(6));

            SwimGait gait = GaitFor(id);

            if (gait == SwimGait.Wing)
            {
                bool turtle = RxTurtle.IsMatch(id);
                // A manta's flap is slow and enormous — half a beat per second at 5 m, wingtips
                // sweeping a fifth of the span each way. A turtle rows faster and shallower.
                double hz = turtle ? Clamp(1.4 / root, 0.25, 2.2)
                                   : Clamp(1.1 / root, 0.12, 1.4);
                return new SwimWave(SwimGait.Wing, hz,
                                    turtle ? 0.12 : 0.20,
                                    turtle ? 0.35 : 0.45,
                                    0.0,
                                    turtle ? 0.20 : 0.30,
                                    Deg(turtle ? 14 : 30));
            }

            if (gait == SwimGait.Fluke)
                return new SwimWave(SwimGait.Fluke, Clamp(1.6 / root, 0.18, 2.6),
                                    0.10, 0.60, 0.06, 0.25, Deg(18));

            if (RxEel.IsMatch(id))
                return new SwimWave(SwimGait.Body, Clamp(2.6 / root, 0.30, 5.0),
                                    0.17, 2.10, 0.25, 0.30, Deg(12));

            if (RxShark.IsMatch(id))
                return new SwimWave(SwimGait.Body, Clamp(2.2 / root, 0.25, 4.0),
                                    0.085, 0.75, 0.08, 0.30, Deg(26));

            if (RxThunniform.IsMatch(id))
                return new SwimWave(SwimGait.Body, Clamp(3.4 / root, 0.40, 6.0),
                                    0.075, 0.85, 0.06, 0.22, Deg(30));

            // Everything else: an ordinary carangiform fish.
            return new SwimWave(SwimGait.Body, Clamp(3.0 / root, 0.35, 5.5),
                                0.110, 0.95, 0.10, 0.28, Deg(32));
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
        /// </summary>
        public static double Effort(double speed, double cruiseSpeed)
        {
            double c = cruiseSpeed > 1e-4 ? cruiseSpeed : 1.0;
            double r = (speed > 0.0 ? speed : 0.0) / c;
            return Clamp(0.30 + 0.70 * r, 0.35, 2.20);
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
