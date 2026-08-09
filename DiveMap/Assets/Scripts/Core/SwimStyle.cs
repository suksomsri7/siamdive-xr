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

        // ── The rest of the web's shoal wiggle (builder.html :1506-1507, :1098) ────
        //
        // 🔴 2026-08-03, build 244 on a real iPhone: "สัตว์ทะเลก็ยังเคลื่อนไหว…ครีบเร็วมากๆ" —
        // AFTER the beat rate had been calibrated and MEASURED to match the web exactly
        // (CI 30790730885: barracuda 0.80 Hz, scad 1.11 Hz, both = wRate/2π). A rate that matches
        // and an eye that disagrees means the rate was never the whole story, and it was not:
        // `wRate` is one of FOUR numbers on the web's shader line, and only that one had been
        // transcribed. The other three were still coming out of the solo-animal size table.
        //
        // The web's line, in full:
        //
        //     _tail = clamp(wStiff − position.z/flen, 0, 1)
        //     transformed.x += sin(uTime*wRate + aPhase + position.z*wWave) * (flen*wAmp) * _tail
        //
        // What that means, term by term, against what this file was handing the same fish:
        //
        //   • AMPLITUDE. Peak deflection is `flen*wAmp*(wStiff+0.5)` — the mesh is centred, so
        //     the tail sits at position.z ≈ −flen/2 and the clamp evaluates to wStiff+0.5 there.
        //     For school:barracuda that is 0.06 × 0.65 = 3.96 % of body length. The thunniform
        //     row was giving it 7.5 %. Tail-tip SPEED is amplitude × 2πf, so at an identical beat
        //     rate the Unity barracuda's tail was travelling 1.92× as fast as the web's — which
        //     is precisely "ครีบเร็วมาก" with a mechanism, and precisely the kind of thing a Hz
        //     reading cannot show.
        //
        //   • WAVELENGTHS. `position.z*wWave` is radians per MODEL UNIT, not per body (the tail
        //     envelope right next to it normalises by flen; this term does not). Across a
        //     1.8624 u barracuda that is 1.8624 × 0.9 = 1.68 rad = 0.267 wavelengths — a stiff
        //     plank flicking a tail, "ตัวแข็งสะบัดหาง", exactly what the user asked for on
        //     2026-07-09. This file was using 0.85 wavelengths: 3.19× as many bends in the body,
        //     which reads as a fish vibrating rather than swimming.
        //
        //   • GUST and RECOIL. The web has neither. `uAmp` is written once at build time and the
        //     envelope is clamped at 0, so the nose never swings back and the amplitude never
        //     drifts.
        //
        //   • BANK. The web NEVER rolls a school fish: the instanced path only ever writes
        //     `o.rotation.y` (builder.html :1618, and again on the calm path :1599). Rolling is a
        //     pod thing (:1721). 30° of independent roll per fish is a large part of why the
        //     iPhone screenshot reads as a disorganised scatter rather than a school.
        //
        /// <summary>Web shoal default <c>wiggleAmp</c> — builder.html :1506.</summary>
        public const double SchoolWiggleAmpDefault   = 0.18;
        /// <summary>Web shoal default <c>wiggleWave</c>, RADIANS PER MODEL UNIT — builder.html :1506.</summary>
        public const double SchoolWiggleWaveDefault  = 2.5;
        /// <summary>Web shoal default <c>wiggleStiff</c> (head-rigid fraction) — builder.html :1506.</summary>
        public const double SchoolWiggleStiffDefault = 0.5;

        /// <summary>…and the hand-tuned barracuda row — builder.html :1098.</summary>
        public const double SchoolWiggleAmpBarracuda   = 0.06;
        public const double SchoolWiggleWaveBarracuda  = 0.9;
        public const double SchoolWiggleStiffBarracuda = 0.15;

        /// <summary>
        /// The web's three wiggle numbers for this shoal. False when the id is not a
        /// <c>school:</c> — same rule and same reason as <see cref="SchoolBeatHz"/> (a pod is
        /// never instanced and never gets the vertex wiggle at all, builder.html :1502).
        /// </summary>
        public static bool SchoolWiggle(string assetId, out double wAmp, out double wWave, out double wStiff)
        {
            string id = Bare(assetId);
            if (!id.StartsWith("school:", StringComparison.OrdinalIgnoreCase))
            {
                wAmp = 0.0; wWave = 0.0; wStiff = 0.0;
                return false;
            }
            if (id.StartsWith("school:barracuda", StringComparison.OrdinalIgnoreCase))
            {
                wAmp = SchoolWiggleAmpBarracuda;
                wWave = SchoolWiggleWaveBarracuda;
                wStiff = SchoolWiggleStiffBarracuda;
            }
            else
            {
                wAmp = SchoolWiggleAmpDefault;
                wWave = SchoolWiggleWaveDefault;
                wStiff = SchoolWiggleStiffDefault;
            }
            return true;
        }

        /// <summary>
        /// Peak tail deflection as a fraction of body length: <c>wAmp × clamp(wStiff + 0.5, 0, 1)</c>.
        ///
        /// The 0.5 is the tail's own <c>position.z/flen</c>. Both school GLBs are modelled centred
        /// on their long axis to within 1 % (barracuda_school.glb z ∈ [−0.9486, +0.9138] over a
        /// 1.8624 u length; scad_school.glb z ∈ [−0.9537, +0.9569] over 1.9105), so the exact
        /// half is the right transcription and not a rounding.
        /// </summary>
        public static double SchoolAmp(double wAmp, double wStiff)
            => wAmp * Clamp(wStiff + 0.5, 0.0, 1.0);

        /// <summary>
        /// <c>wWave</c> (radians per model unit) → wavelengths along the body, which is what
        /// <c>_WaveCycles</c> means. See the block comment above: the web's own term is NOT
        /// normalised by body length, so the conversion needs the GLB's local length.
        /// </summary>
        public static double SchoolCycles(double flenLocal, double wWave)
            => (flenLocal > 0.0 ? flenLocal : 1.0) * wWave / (2.0 * Math.PI);

        /// <summary>
        /// A hand-set override for one SOLO species. Any field left at <see cref="NoOverride"/>
        /// keeps whatever the size law and the gait row worked out.
        ///
        /// 🔴 2026-08-06, build 280 on a real iPhone: *"บาราคูด้าโบกหางเร็วไปมากและแคบไป"*.
        ///
        /// The fish in question is <c>msh:barracuda</c> — the SOLO one, a single 14.57 u animal
        /// with no clip in its GLB, so it is driven by this file's wave and not by ClipPlay. It
        /// has never been near the web's barracuda numbers, because those live on the SHOAL
        /// (<c>school:barracuda</c>, builder.html:1098) and only the shoal branch reads them. The
        /// solo animal fell through to the generic thunniform row: 0.87 Hz, 7.5 % amplitude,
        /// 0.85 wavelengths.
        ///
        /// What the web does with the SAME fish, by both of its own routes:
        ///
        ///   • As a shoal (:1098, hand-tuned by the web's author for this species):
        ///     wiggleRate 5.0 → 0.796 Hz · wiggleAmp 0.06 with wiggleStiff 0.15 → 3.96 % ·
        ///     wiggleWave 0.9 across a 1.8624 u body → 0.267 wavelengths.
        ///   • As a solo GLB with no clip, the web does not bend the mesh at all —
        ///     <c>animateGLB()</c> falls to its <c>dart</c> default (builder.html:3603) and yaws
        ///     the WHOLE fish: <c>ry += sin(T*1.4)*0.18*a</c> with <c>T = t*sp+ph</c>,
        ///     <c>sp = 0.6…1.4</c>, <c>a = 0.7…1.3</c>. That is 0.13-0.31 Hz (0.22 at the middle
        ///     of the random range) of ±10° whole-body swing — very slow, and very WIDE, because
        ///     the entire body is the lever.
        ///
        /// Both of the user's words fall straight out of that comparison. 0.87 Hz against 0.22 Hz
        /// is "เร็วไปมาก" — literally four times. And 0.85 wavelengths against 0.267 is why it is
        /// "แคบ": at 0.85 the body carries most of a full S-bend, so the fish ripples in small
        /// tight arcs that partly cancel along its length, where the web's 0.267 is a stiff plank
        /// whose whole back half swings one way together. Narrowness here is a WAVELENGTH
        /// problem before it is an amplitude one, and turning the amplitude up without fixing the
        /// wavelength would have bought a wider ripple rather than a wider sweep.
        ///
        /// Where each of the three numbers comes from:
        ///
        ///   • CYCLES ← the web, exactly. <see cref="SoloBarracudaCycles"/> is the same 0.267 the
        ///     shoal branch computes from builder.html:1098, read through the same conversion.
        ///     No judgement in it.
        ///   • BEAT   ← the web's 0.796 Hz for this species, HALVED. The web's own two answers for
        ///     a barracuda are 0.796 (shoal) and ~0.22 (solo dart); the user is looking at the solo
        ///     one and says the app is much too fast. Halving the shoal figure lands at 0.398 Hz —
        ///     between the web's two numbers, and 2.2× slower than build 280.
        ///   • AMP    ← ours, and the only one of the three that is a judgement call, because BOTH
        ///     of the web's routes are narrower than what this app already draws and the user is
        ///     asking for wider. 0.15 is picked so that TAIL-TIP SPEED (amp × 2πf, which is what an
        ///     eye reads as "fast") comes out at 0.375 body-lengths/s against build 280's 0.408 —
        ///     very slightly slower, not faster, while the sweep itself is twice as wide. Slower
        ///     AND wider was the request; widening the sweep without slowing the tip would have
        ///     answered half of it and undone the other half.
        ///
        /// It is a TABLE and not a branch inside <see cref="For"/> on purpose: the next report of
        /// this kind should be three numbers moving in one place with this reasoning still attached
        /// to them, not another special case buried in the middle of the size law.
        /// </summary>
        public readonly struct SoloTune
        {
            /// <summary>True when a row exists at all.</summary>
            public readonly bool Has;
            /// <summary>Beats per second, or <see cref="NoOverride"/>.</summary>
            public readonly double BeatHz;
            /// <summary>Tail-tip travel as a fraction of body length, or <see cref="NoOverride"/>.</summary>
            public readonly double Amp;
            /// <summary>Wavelengths along the body, or <see cref="NoOverride"/>.</summary>
            public readonly double Cycles;

            public SoloTune(double beatHz, double amp, double cycles)
            {
                Has = true; BeatHz = beatHz; Amp = amp; Cycles = cycles;
            }
        }

        /// <summary>
        /// "This field is not overridden". Negative, because no beat rate, amplitude or wavelength
        /// count ever is — the same sentinel convention, and for the same reason, as
        /// <see cref="SpeciesBehavior.NoValue"/>.
        /// </summary>
        public const double NoOverride = -1.0;

        /// <summary>Beat rate for the solo barracuda: the web's shoal figure for this species, halved.</summary>
        public const double SoloBarracudaBeatHz = SchoolBeatHzBarracuda * 0.5;   // 0.398 Hz

        /// <summary>
        /// Tail-tip travel to one side, as a fraction of body length. The one number in
        /// <see cref="SoloTuneFor"/> that is not a transcription — set by the tail-tip-speed
        /// identity described on <see cref="SoloTune"/>, not by eye.
        /// </summary>
        public const double SoloBarracudaAmp = 0.15;

        /// <summary>
        /// Wavelengths along the body — the web's own <c>wiggleWave: 0.9</c> for a barracuda
        /// (builder.html:1098), read through the same <see cref="SchoolCycles"/> conversion the
        /// shoal branch uses, so the two can never drift apart. ≈ 0.267.
        ///
        /// A property rather than a <c>const</c> because it reads <see cref="MarineMath"/>'s
        /// table: a static field initialiser here would pull another type's static constructor in
        /// at an order this file does not control, for a number that is wanted once per spawn.
        /// </summary>
        public static double SoloBarracudaCycles
            => SchoolCycles(MarineMath.SpeciesFor("school:barracuda").FishLenLocal,
                            SchoolWiggleWaveBarracuda);

        /// <summary>
        /// The hand-set row for this animal, or an all-absent one. Bare-id matched, so the pivot
        /// name <c>Item_3_msh:barracuda</c> finds it — see <see cref="RxBareId"/>, the same trap
        /// that would otherwise hand a whale shark an ordinary shark's tempo.
        /// </summary>
        public static SoloTune SoloTuneFor(string assetId)
        {
            string id = Bare(assetId);
            if (string.Equals(id, "msh:barracuda", StringComparison.OrdinalIgnoreCase))
                return new SoloTune(SoloBarracudaBeatHz, SoloBarracudaAmp, SoloBarracudaCycles);
            return default;
        }

        /// <summary>
        /// <paramref name="w"/> with every field the row overrides replaced. Split out from
        /// <see cref="For"/> so the override rule is testable without going through a species.
        /// </summary>
        public static SwimWave Apply(SwimWave w, SoloTune t)
        {
            if (!t.Has) return w;
            return new SwimWave(
                w.Gait,
                t.BeatHz >= 0.0 ? t.BeatHz : w.BeatHz,
                t.Amp    >= 0.0 ? t.Amp    : w.Amp,
                t.Cycles >= 0.0 ? t.Cycles : w.Cycles,
                w.Recoil, w.Gust, w.MaxBankRad);
        }

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

        /// <summary>
        /// See <see cref="RxBareId"/>. Public so <see cref="ClipPlay"/> can reach the SAME rule
        /// instead of writing a second one — both files do exact-match table reads against
        /// <see cref="SpeciesBehavior"/>, so both fail identically if the prefix is not stripped.
        /// </summary>
        public static string BareId(string assetId) => Bare(assetId);

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
            => UserSlow(assetId, Apply(FromTables(assetId, worldLen), SoloTuneFor(assetId)));

        /// <summary>
        /// คำตัดสิน user ผ่านกระบวนการ GIF — แตะ BeatHz ตัวเดียว cycles/amp/envelope ห้ามแตะ
        /// (ชุดจูนที่แตะรูปคลื่นถูก user ปฏิเสธบนเครื่องจริงมาแล้ว 8 ส.ค.).
        ///
        /// 8 ส.ค. = 0.25 ("แบบ B ระดับ B3") กับบาราคูด้า+กะมง
        /// 9 ส.ค. = 0.12 และ **เพิ่มปลาข้างเหลือง (scad) เข้ากลุ่ม** — user: "ส่วนหางขยับ
        /// ช้ากว่านี้มากๆ" ทั้งสามชนิด
        ///
        /// 🔴 ทำไม scad ถึงโดดออกมาชัด: มันไม่เคยอยู่ในกลุ่มนี้เลย จึงวิ่งที่ 1.11 Hz เต็มสูตร
        /// ขณะที่อีกสองตัวถูกลดไปเหลือ 0.20/0.23 แล้ว — ต่างกัน 5 เท่าในฉากเดียวกัน และ scad
        /// ยังมี amp 0.18 (กวาด 18% ของลำตัว เทียบบาราคูด้า 0.039) จึงเป็นตัวที่ตาจับได้ก่อน
        /// เพื่อน · เข้ากลุ่มแล้วเหลือ 0.13 Hz
        /// </summary>
        public const double UserSlowMulBarracudaTrevally = 0.12;

        private static SwimWave UserSlow(string assetId, SwimWave w)
        {
            string id = Bare(assetId ?? "");
            bool slow = id.IndexOf("barracuda", StringComparison.OrdinalIgnoreCase) >= 0
                     || id.IndexOf("yellowtail", StringComparison.OrdinalIgnoreCase) >= 0
                     || id.IndexOf("trevally", StringComparison.OrdinalIgnoreCase) >= 0
                     || id.IndexOf("scad", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!slow) return w;
            return new SwimWave(w.Gait, w.BeatHz * UserSlowMulBarracudaTrevally,
                                w.Amp, w.Cycles, w.Recoil, w.Gust, w.MaxBankRad);
        }

        /// <summary>
        /// <see cref="For"/> before <see cref="SoloTuneFor"/> gets a say — the size law and the
        /// gait rows on their own.
        ///
        /// Kept reachable so a test can pin what the general rule says about an animal separately
        /// from what one hand-set row says about one animal, which is the distinction the whole
        /// <see cref="SoloTune"/> block exists to keep visible.
        /// </summary>
        public static SwimWave FromTables(string assetId, double worldLen)
        {
            string id = assetId ?? "";
            double m = Metres(worldLen);
            double root = Math.Sqrt(m > 1e-4 ? m : 1e-4);

            if (IsStill(id))
                return new SwimWave(SwimGait.Body, Rate(id, 0.8 / root, 0.15, 1.2),
                                    0.012, 1.0, 0.0, 0.5, Deg(6));

            // 🔴 A SHOAL leaves the size law entirely — every number, not just the rate.
            //
            // Its whole wave is a literal compiled into the web's shader, so a size law cannot
            // reproduce it any more than it could reproduce the constant beat rate (see the
            // SchoolWiggle* block). This branch is the fix for "ครีบยังเร็วมาก" on build 244,
            // where the rate already matched the web and the amplitude was still 1.9× and the
            // wavelength 3.2×. Recoil and gust are zero because the web has neither, and the bank
            // is zero because the web's instanced schools only ever write rotation.y.
            //
            // worldLen is deliberately unused here: the web's numbers are fractions of the body,
            // so a school scaled up beats and bends by the same fractions — which is what
            // ShoalWave_DoesNotDependOnDrawSize pins.
            if (SchoolWiggle(id, out double wAmp, out double wWave, out double wStiff))
                return new SwimWave(
                    SwimGait.Body,
                    SchoolBeatHz(id),
                    SchoolAmp(wAmp, wStiff),
                    SchoolCycles(MarineMath.SpeciesFor(Bare(id)).FishLenLocal, wWave),
                    0.0,    // recoil — the web's envelope is clamped at 0; the nose never swings back
                    0.0,    // gust   — uAmp is written once at build time and never drifts
                    0.0);   // bank   — builder.html :1599/:1618 write rotation.y and nothing else

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
