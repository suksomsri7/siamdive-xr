using System;

namespace DiveMap.Core
{
    /// <summary>
    /// C5 — what a shoal does when something frightening arrives. Ported from the web's
    /// <c>schoolFlee()</c> (builder.html:1628), the panic trigger in <c>schoolStep()</c>
    /// (:1688-1702), <c>senseAgents()</c> (:1932) and <c>shelterSense()</c> (:1960).
    ///
    /// Two things are worth stating up front, because they are what make the reef feel alive
    /// rather than merely animated:
    ///
    ///   • **Panic is graded, not a switch.** It is 0..1 by distance, so a shoal reacts to a diver
    ///     forty metres out with a ripple and to one at arm's length with a burst. A boolean here
    ///     would give you fish that ignore you and then teleport.
    ///   • **Speed is the trigger, not presence.** The web only panics from the diver when
    ///     <c>camVel &gt; 11</c>. Hover next to a shoal and it accepts you; charge it and it
    ///     scatters. That single rule is most of why the web's reef reads as wildlife.
    ///
    /// The scalar formulas below are the web's, verbatim, so they can be asserted against it.
    /// How they are *applied* differs: the web eases each fish's position toward a formation slot
    /// and adds the flee offset on top, whereas this app runs velocity boids (<c>BoidsJob</c>).
    /// The steering weights that translate one into the other are marked "interpretation" and are
    /// the only numbers here that are not lifted from builder.html.
    /// </summary>
    public static class FleeMath
    {
        // ── the web's constants ───────────────────────────────────────────────────

        /// <summary>
        /// The web's threshold as a FRACTION of the drone's top speed: <c>camVel &gt; 11</c> against
        /// its <c>SP = 30</c>. Kept as the ratio rather than the number because the number was only
        /// ever "a bit over a third of full throttle" — see <see cref="DiverPanicSpeed"/>.
        /// </summary>
        public const double PanicSpeedFraction = 11.0 / 30.0;

        /// <summary>
        /// Diver speed above which a shoal treats the drone as a predator (web: camVel&gt;11).
        ///
        /// ⚠️ DERIVED, not a literal. The drone's top speed dropped 30 → 9 u/s when the flight
        /// model was re-scaled to real metres (see <see cref="DroneFlight"/>); a hard 11 would then
        /// sit ABOVE anything the drone can reach and no shoal would ever scatter again — C5 would
        /// still pass its unit tests and be dead on the device. Expressed as the web's fraction of
        /// full throttle it keeps meaning what it meant: charge at more than about a third of your
        /// speed and the reef notices.
        /// </summary>
        public static double DiverPanicSpeed => DroneFlight.Speed * PanicSpeedFraction;

        /// <summary>Panic above which the shoal balls up (web: <c>S._panic&gt;0.6</c>).</summary>
        public const double BallUpPanic = 0.6;

        /// <summary>How long the bait-ball is held after the threat passes (web: <c>t+2.5</c>).</summary>
        public const double BallHoldSeconds = 2.5;

        /// <summary>Sense refresh interval for predators (web: <c>t-u.lastSense&gt;0.7</c>).</summary>
        public const double SenseIntervalSeconds = 0.7;

        /// <summary>Shelter refresh interval (web: <c>t-u.lastShelter&gt;1.2</c>).</summary>
        public const double ShelterIntervalSeconds = 1.2;

        // ── panic ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Radius at which a real predator starts to worry the shoal
        /// (web :1692 — <c>spreadR*0.8 + flen*5</c>).
        /// </summary>
        public static double PredatorPanicRadius(double spreadR, double fishLen)
            => spreadR * 0.8 + fishLen * 5.0;

        /// <summary>
        /// Radius at which a CHARGING diver does (web :1700 — <c>spreadR*0.85 + flen*6</c>).
        /// Slightly wider than a predator's: the drone is big, loud and fast.
        /// </summary>
        public static double DiverPanicRadius(double spreadR, double fishLen)
            => spreadR * 0.85 + fishLen * 6.0;

        /// <summary>
        /// Panic 0..1 from the threat's distance (web: <c>1 - pd/panicR</c>, zero outside).
        /// One at the centre, nothing at the rim.
        /// </summary>
        public static double PanicLevel(double distance, double panicRadius)
        {
            if (panicRadius <= 0.0 || distance >= panicRadius) return 0.0;
            if (distance <= 0.0) return 1.0;
            return 1.0 - distance / panicRadius;
        }

        /// <summary>Is the diver moving fast enough to frighten anything? (web: <c>camVel&gt;11</c>)</summary>
        public static bool DiverIsThreatening(double diverSpeed) => diverSpeed > DiverPanicSpeed;

        // ── "ปลาตกใจเร็วไปมาก ควรมีระยะตกใจ และสัมพันธ์กับความเร็วโดรน" (user, 21 ส.ค. 2026) ──
        //
        // วัดของเดิมก่อนแก้ (โดรน 12 u/s · เกณฑ์ 4.4 u/s):
        //
        //     ฝูง        ระยะชน   ระยะตกใจเต็ม   โซนที่ไล่ระดับได้จริง
        //     R=8         8.6 ม.       10.4 ม.        1.8 ม.
        //     R=12       12.9 ม.       15.6 ม.        2.7 ม.
        //     R=20       20.5 ม.       20.0 ม.       -0.5 ม.   ← ไม่มีเลย
        //     R=30       30.5 ม.       28.5 ม.       -2.0 ม.   ← ไม่มีเลย
        //
        // ⇒ สองเรื่องที่รวมกันแล้วได้อาการ "ตกใจง่ายเกิน":
        //   1. กฎ "โดนตัว = ตกใจเต็ม 1.0" (เพิ่ม 15 ส.ค. ตามที่ user ขอ) ไม่สนความเร็วเลย และ
        //      ระยะของมัน (ขอบฝูง+1 ตัวปลา) **กว้างกว่า**ระยะตกใจแบบไล่ระดับในฝูงใหญ่
        //      ⇒ ลอยเข้าไปช้าที่สุดเท่าที่ทำได้ พอแตะขอบฝูงก็แตกฮือเต็มพิกัดทันที ไม่มีขั้นกลาง
        //   2. ความเร็วเป็นแค่ประตูเปิด/ปิด (เกิน 37% คันเร่ง = ใช่) แล้ว**ระยะคงที่**
        //      ⇒ คลานผ่านที่ 38% กับพุ่งใส่ที่ 100% ฝูงตอบสนองเท่ากันเป๊ะ
        //
        // แก้ตามที่ user บรรยาย: ระยะตกใจโตตามความเร็ว — ช้า = ต้องถึงตัวถึงจะรู้สึก ·
        // เร็ว = รู้ตัวตั้งแต่ไกล · และ "โดนตัวตอนลอยนิ่ง" = สะดุ้งหลบ ไม่ใช่แตกกระเจิง

        /// <summary>
        /// ความน่ากลัวของโดรน 0..1 จากความเร็ว: 0 ที่เกณฑ์ (<see cref="DiverPanicSpeed"/>)
        /// และ 1 ที่คันเร่งเต็ม. เป็นสัดส่วนของคันเร่งเหมือน <see cref="PanicSpeedFraction"/>
        /// จึงเลื่อนตามเองถ้าความเร็วโดรนถูกจูนอีก (เคยพลาดมาแล้วตอนตัวเลข 11 ถูกฮาร์ดโค้ด).
        /// </summary>
        public static double ThreatFraction(double diverSpeed)
        {
            double lo = DiverPanicSpeed;
            double hi = DroneFlight.Speed;
            if (hi <= lo) return diverSpeed > lo ? 1.0 : 0.0;
            return Clamp01((diverSpeed - lo) / (hi - lo));
        }

        /// <summary>
        /// ระยะที่ฝูงเริ่มรู้สึกถึงโดรน — โตตามความเร็ว.
        ///
        /// ที่ความเร็วเกณฑ์ = <see cref="ContactRadius"/> (ต้องถึงตัวจริง ๆ) · ที่คันเร่งเต็ม =
        /// ระยะของเว็บ (<see cref="DiverPanicRadius"/>) แต่ **อย่างน้อยต้องมีระยะเตือน 6 ช่วงตัว
        /// เสมอ** เพราะในฝูงใหญ่สูตรของเว็บให้ค่าน้อยกว่าระยะชน ⇒ ถ้าไม่กันไว้ ฝูงใหญ่จะไม่มีวัน
        /// ตกใจก่อนโดนชนเลยไม่ว่าจะพุ่งเร็วแค่ไหน ซึ่งเป็นอาการตรงข้ามกับที่ user ขอ.
        /// </summary>
        public static double StartleRadius(double spreadR, double fishLen, double diverSpeed)
        {
            double near = ContactRadius(spreadR, fishLen);
            double far = DiverPanicRadius(spreadR, fishLen);
            double warned = near + Math.Max(fishLen, 0.5) * 6.0;
            if (far < warned) far = warned;
            return near + (far - near) * ThreatFraction(diverSpeed);
        }

        /// <summary>ความตกใจขั้นต่ำเมื่อโดนตัวขณะโดรนแทบไม่เคลื่อนที่ — สะดุ้งหลบ ไม่ใช่แตกฝูง.</summary>
        public const double ContactPanicFloor = 0.35;

        /// <summary>
        /// ตกใจแค่ไหนเมื่อโดรน "ถึงตัว". ยังตกใจเสมอแม้ลอยนิ่ง (กฎ 15 ส.ค. ของ user ยังอยู่ครบ —
        /// ปลาต้องหลบสิ่งที่เข้ามาประชิด ไม่งั้นโดรนจะดูทะลุตัวปลา) แต่แรงตามความเร็วที่ชน:
        /// ลอยไปเบียด = 0.35 · พุ่งเต็มสปีดใส่ = 1.0
        /// </summary>
        public static double ContactPanic(double diverSpeed)
            => ContactPanicFloor + (1.0 - ContactPanicFloor) * ThreatFraction(diverSpeed);

        // ── "ฝูงสั่นถี่ๆ" (user, 8-9 ส.ค. 2026) — the gate, not the swimming ──────
        //
        // 🔴 <see cref="DiverIsThreatening(double)"/> is a HARD binary test on a NOISY signal, and
        // panic is rebuilt from scratch every frame with no memory (FishSchoolSystem.ApplyFear).
        // A diver holding station near the threshold therefore flips the whole school between two
        // completely different motion laws at frame rate — measured on the Harddeep barracuda with
        // a realistic stick jitter (±1.6 u/s, the app's own 0.25 smoothing):
        //
        //     diver speed   gate flips/s   raw heading wobble   peak-to-peak
        //         9 u/s          0.0              0.39°             8.7°
        //        11 u/s          8.7              1.25°            26.5°   ← at the threshold
        //        12 u/s          3.7              0.74°            26.7°
        //        20 u/s          0.0              0.68°            26.7°
        //
        // Each flip also teleports the fish's SLOT by the whole flee push (tens of units) and
        // swaps the calm ease for the forward-only chase. Nothing about the fish's swimming is
        // wrong; the thing telling it what to do is chattering.
        //
        // Two independent guards, because they fix different halves: the Schmitt band stops the
        // BOOLEAN chattering, the ease stops the panic VALUE stepping when the gate does flip.

        /// <summary>Release the threat gate at 0.8× the speed that armed it (Schmitt band).</summary>
        public const double ThreatSpeedRelease = 0.8;

        /// <summary>Fear arrives fast…</summary>
        public const double PanicAttackSeconds = 0.15;
        /// <summary>…and leaves slowly. A shoal does not relax the instant a diver coasts.</summary>
        public const double PanicReleaseSeconds = 0.8;

        /// <summary>
        /// The threat gate WITH hysteresis: once a diver is frightening they stay frightening
        /// until they slow well below the arming speed, instead of toggling on stick noise.
        /// </summary>
        public static bool DiverIsThreatening(double diverSpeed, bool wasThreatening)
            => diverSpeed > (wasThreatening ? DiverPanicSpeed * ThreatSpeedRelease : DiverPanicSpeed);

        /// <summary>
        /// Move panic toward <paramref name="target"/> on the wall clock — quickly up, slowly
        /// down. Exponential, so it is frame-rate independent (the whole point: a per-frame lerp
        /// would put the fix back in the class of bug it is fixing).
        /// </summary>
        public static double EasePanic(double current, double target, double dtSeconds)
        {
            if (dtSeconds <= 0.0) return current;
            double tau = target > current ? PanicAttackSeconds : PanicReleaseSeconds;
            double k = 1.0 - Math.Exp(-dtSeconds / tau);
            return current + (target - current) * k;
        }

        /// <summary>
        /// <see cref="SchoolPanic"/> with the hysteretic gate. <paramref name="wasThreatening"/>
        /// is this school's own memory of the gate, and it comes back out so the caller can store it.
        /// </summary>
        public static double SchoolPanic(
            double predatorDistance, bool hasPredator,
            double diverDistance, double diverSpeed, bool diverActive,
            double spreadR, double fishLen,
            bool wasThreatening, out bool nowThreatening)
        {
            nowThreatening = diverActive && DiverIsThreatening(diverSpeed, wasThreatening);
            if (hasPredator)
            {
                double p = PanicLevel(predatorDistance, PredatorPanicRadius(spreadR, fishLen));
                if (p > 0.0) return p;
            }

            // 🔴 15 ส.ค. 2026 — user: "หากบินโดรนไปชนสัตว์ต้องให้สัตว์ว่ายเร็วขึ้นมาก ๆ เป็น
            // พฤติกรรมตกใจ"
            //
            // เกณฑ์เดิมตกใจเฉพาะเมื่อนักดำน้ำ "ว่ายเร็วพอ" ⇒ ลอยนิ่ง ๆ เข้าไปจ่อกลางฝูงแล้วฝูง
            // ไม่สนใจเลย ซึ่งผิดธรรมชาติ (ของจริงปลาหลบทุกอย่างที่เข้ามาประชิด ไม่ว่าจะเร็วแค่ไหน)
            // และเป็นเหตุผลที่ user รู้สึกว่าโดรน "ทะลุ" ตัวปลา — ปลาไม่เคยพยายามหลบเลย
            //
            // ระยะประชิด = หนึ่งช่วงตัวปลาจากขอบฝูง: ใกล้กว่านั้นถือว่าชน ⇒ ตกใจเสมอไม่ว่าจะ
            // เคลื่อนที่อยู่หรือไม่ · 🔴 21 ส.ค. 2026 แรงของมันไม่ใช่ 1.0 ตายตัวอีกแล้ว แต่ขึ้นกับ
            // ความเร็วที่ชน (ดู ContactPanic) — ของเดิมทำให้ "ลอยเข้าไปช้า ๆ" กับ "พุ่งใส่เต็มสปีด"
            // ได้ผลเท่ากันเป๊ะ ซึ่งคือ "ปลาตกใจเร็วไปมาก" ที่ user รายงาน
            if (diverActive && diverDistance <= ContactRadius(spreadR, fishLen))
                return ContactPanic(diverSpeed);

            // เกินระยะประชิด: ระยะตกใจโตตามความเร็ว แทนที่จะคงที่
            return nowThreatening
                ? PanicLevel(diverDistance, StartleRadius(spreadR, fishLen, diverSpeed))
                : 0.0;
        }

        /// <summary>
        /// ระยะที่ถือว่า "โดนตัว" — ขอบฝูงบวกหนึ่งช่วงตัวปลา. ตกใจเต็มพิกัดในระยะนี้เสมอ
        /// แม้นักดำน้ำจะลอยนิ่ง เพราะของจริงปลาหลบสิ่งที่เข้ามาประชิดทุกกรณี
        /// </summary>
        public static double ContactRadius(double spreadR, double fishLen)
            => spreadR + Math.Max(fishLen, 0.5);

        /// <summary>
        /// The shoal's panic this frame. A real predator wins over the diver — the web checks the
        /// predator first and only consults the drone when <c>!S._panic</c>.
        /// </summary>
        public static double SchoolPanic(
            double predatorDistance, bool hasPredator,
            double diverDistance, double diverSpeed, bool diverActive,
            double spreadR, double fishLen)
        {
            if (hasPredator)
            {
                double p = PanicLevel(predatorDistance, PredatorPanicRadius(spreadR, fishLen));
                if (p > 0.0) return p;
            }
            // 🔴 โอเวอร์โหลดนี้คือ "กฎของเว็บเปล่า ๆ" ไว้เทียบ ไม่มีทั้งกฎประชิด (15 ส.ค.) และ
            // hysteresis — ของจริงที่แอปเรียกคือตัว 8 อาร์กิวเมนต์ข้างบน. ที่เปลี่ยนตรงนี้มีอย่าง
            // เดียวคือระยะที่โตตามความเร็ว เพราะนั่นคือส่วนที่เว็บไม่มีและ user ขอเข้ามา (21 ส.ค.)
            if (diverActive && DiverIsThreatening(diverSpeed))
                return PanicLevel(diverDistance, StartleRadius(spreadR, fishLen, diverSpeed));
            return 0.0;
        }

        // ── the scatter itself ───────────────────────────────────────────────────

        /// <summary>
        /// How far a fish is thrown outward from the threat
        /// (web :1631 — <c>panic*(spreadR*0.18 + flen*1.6)</c>).
        ///
        /// The comment in builder.html is explicit that this was tuned DOWN on 2026-07-09: the
        /// shoal is meant to burst apart *near* the threat, not evacuate the map. Keep it small.
        /// </summary>
        public static double FleePush(double panic, double spreadR, double fishLen)
        {
            if (panic <= 0.0) return 0.0;
            return panic * (spreadR * 0.18 + fishLen * 1.6);
        }

        /// <summary>
        /// Extra positional ease while fleeing — how *sharply* the dart happens
        /// (web :1632 — <c>min(0.22, panic*0.18)</c>).
        /// </summary>
        public static double FleeEase(double panic)
        {
            if (panic <= 0.0) return 0.0;
            double e = panic * 0.18;
            return e > 0.22 ? 0.22 : e;
        }

        /// <summary>Does this level of fear ball the shoal up? (web :1697)</summary>
        public static bool ShouldBallUp(double panic, bool isPod) => !isPod && panic > BallUpPanic;

        /// <summary>
        /// The home radius while balled up. The web reuses its tight <c>'ball'</c> formation; here
        /// the equivalent is to shrink the boids' home radius, which pulls the same shoal into the
        /// same shape without a second formation system. **Interpretation.**
        /// </summary>
        public static double BallHomeRadius(double homeR, double panic)
        {
            if (panic <= 0.0) return homeR;
            double k = 1.0 - 0.45 * Clamp01(panic);
            return homeR * k;
        }

        /// <summary>
        /// Dart speed multiplier. The web darts by raising the position ease (FleeEase); with
        /// velocity boids the same visual comes from swimming harder. Scaled so a full-panic fish
        /// is 1.6× cruise, which matches the web's burst over a couple of seconds.
        /// **Interpretation**, calibrated to <see cref="FleeEase"/>.
        /// </summary>
        public static double DartSpeedScale(double panic) => 1.0 + 0.6 * Clamp01(panic);

        /// <summary>
        /// Steering weight pushing a fish directly away from the threat. **Interpretation** — the
        /// boids equivalent of the web's radial position offset.
        /// </summary>
        public static double FleeSteerWeight(double panic, double spreadR, double fishLen)
            => FleePush(panic, spreadR, fishLen) * 0.35;

        /// <summary>
        /// How much harder a frightened fish may turn. At the cruise cap (0.045 rad/frame) a
        /// startled fish needs well over a second to come about, which reads as indifference —
        /// the one thing a startle must never look like. **Interpretation.**
        /// </summary>
        public static double TurnCapScale(double panic) => 1.0 + 2.0 * Clamp01(panic);

        // ── who counts as a threat (senseAgents) ─────────────────────────────────

        /// <summary>Perception radius of an individual animal (web :1934 — <c>obsR*4.5 + 28</c>).</summary>
        public static double SenseRadius(double obsR) => obsR * 4.5 + 28.0;

        /// <summary>
        /// Radius at which an individual animal turns and swims away from a hunter
        /// (web :2183 — <c>obsR*5 + 30</c>).
        ///
        /// 🔴 Wider than <see cref="PredatorPanicRadius"/> on purpose, and the gap between them is
        /// a behaviour rather than sloppy tuning: outside the panic radius the animal has SEEN the
        /// predator and is quietly putting distance between them; inside it, it bursts. A reef
        /// where prey does nothing at all until the shark is on top of it reads as scenery.
        /// </summary>
        public static double FleeRadius(double obsR) => obsR * 5.0 + 30.0;

        /// <summary>
        /// Sprint multiplier of a fleeing animal (web :2184 — <c>1.7 + 0.6·(1 − d/fleeR)</c>):
        /// 1.7× at the rim of the flight bubble, 2.3× with the predator on its tail.
        /// </summary>
        public static double FleeSprint(double distance, double fleeRadius)
        {
            if (fleeRadius <= 0.0) return 1.7;
            double k = 1.0 - distance / fleeRadius;
            if (k < 0.0) k = 0.0;
            if (k > 1.0) k = 1.0;
            return 1.7 + 0.6 * k;
        }

        /// <summary>Fear added per sense tick while a hunter is inside the bubble (web :2183).</summary>
        public const double FearPerFleeTick = 0.25;

        /// <summary>
        /// Is <paramref name="otherRank"/> a threat to something of <paramref name="myRank"/>?
        ///
        /// The web's rule (:1939) has one detail that is easy to drop and very visible if you do:
        /// **filter feeders are harmless.** A whale shark or a manta must be able to cruise through
        /// a shoal of scad without it exploding, because that is exactly the shot every diver wants.
        /// Only a strictly higher rank that actually hunts counts.
        /// </summary>
        public static bool IsThreat(int myRank, int otherRank, string otherDiet)
        {
            if (otherRank <= myRank) return false;
            if (string.Equals(otherDiet, "filter", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        // ── shelter (shelterSense) ───────────────────────────────────────────────

        /// <summary>
        /// How strongly a frightened fish runs for the nearest cover. Zero when calm, so a shoal
        /// only heads for the reef when something is after it. **Interpretation** of the web's
        /// <c>u.shelter</c> bias, which feeds the same heading term.
        /// </summary>
        public static double ShelterBias(double panic) => 0.9 * Clamp01(panic);

        /// <summary>
        /// Where a shoal actually aims while fleeing: its own home, dragged toward cover in
        /// proportion to fear. Returned as a 0..1 lerp factor from home to shelter.
        /// </summary>
        public static double ShelterLerp(double panic, bool hasShelter)
            => hasShelter ? ShelterBias(panic) * 0.55 : 0.0;

        /// <summary>
        /// A fish inside the shelter's own radius has arrived — it should stop steering at it and
        /// mill, otherwise the shoal piles into the coral.
        /// </summary>
        public static bool AtShelter(double distanceToShelter, double shelterR)
            => distanceToShelter <= shelterR * 1.15;

        private static double Clamp01(double v) => v < 0.0 ? 0.0 : (v > 1.0 ? 1.0 : v);
    }
}
