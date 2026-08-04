using System;
using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// Unit tests for <see cref="SchoolFormation"/> — the web's slot-formation school, ported.
    ///
    /// 🔴 What these pin, and why each one exists.
    ///
    /// Build 244 on a real iPhone came back with "สัตว์ทะเลก็ยังเคลื่อนไหวขยับห่าง" over a
    /// screenshot of a barracuda school stretched into a thin line. The fin rate had already been
    /// calibrated and MEASURED to match the web, so the school itself was the problem, and the
    /// mechanism was structural rather than a tuning value: Unity ran Reynolds boids at
    /// <c>speed = MaxSpeed</c> every frame (BoidsJob :186-192), and the web runs no boids at all.
    ///
    /// So the assertions below are about the three things that make the web's version a SCHOOL:
    ///
    ///   • a fish has a fixed slot and the slot formulas are the web's numbers exactly,
    ///   • a fish that has REACHED its slot slows down — the property boids cannot have,
    ///   • the shape changes on the web's own 7-16 s × modeDurMul wheel, and morphs rather
    ///     than snapping.
    /// </summary>
    public class SchoolFormationTests
    {
        private const double Eps = 1e-9;

        // The demo map's barracuda school, resolved by MarineMath (item.s = 9.2):
        //   fish 17.13 u · R 143.9 u · cap 4.01 u/s
        private const double Flen = 1.862 * 9.2;
        private const double R    = 1.862 * 14.0 * 0.6 * 9.2;

        private static SchoolFormation.Slot MakeSlot(int i, int n)
            => SchoolFormation.SlotFor(i, n, R, R * 0.55, Flen,
                                       0.31, 0.62, 0.17, 0.45, 0.88, 0.05, 0.71);

        // ── The mode wheel is the web's, verbatim ─────────────────────────────────

        /// <summary>
        /// builder.html :1542. The REPEATS are the content: cluster appears four times and stream
        /// twice out of twelve, so two thirds of a school's life is spent polarised and cruising.
        /// A uniform list of the nine shapes would put a school in a tornado a third of the time,
        /// which is a screensaver, not a reef.
        /// </summary>
        [Test]
        public void ModeWheel_IsTheWebsWeightedBag()
        {
            Assert.AreEqual(12, SchoolFormation.Modes.Length);

            int cluster = 0, stream = 0, tornado = 0, vortex = 0, cone = 0, coneUp = 0, ball = 0;
            foreach (SchoolMode m in SchoolFormation.Modes)
            {
                switch (m)
                {
                    case SchoolMode.Cluster: cluster++; break;
                    case SchoolMode.Stream:  stream++;  break;
                    case SchoolMode.Tornado: tornado++; break;
                    case SchoolMode.Vortex:  vortex++;  break;
                    case SchoolMode.Cone:    cone++;    break;
                    case SchoolMode.ConeUp:  coneUp++;  break;
                    case SchoolMode.Ball:    ball++;    break;
                }
            }
            Assert.AreEqual(4, cluster, "cluster ×4");
            Assert.AreEqual(2, stream,  "stream ×2");
            Assert.AreEqual(2, tornado, "tornado ×2");
            Assert.AreEqual(1, vortex);
            Assert.AreEqual(1, cone);
            Assert.AreEqual(1, coneUp);
            Assert.AreEqual(1, ball);
        }

        /// <summary>builder.html <c>MODE_DUR</c> (:1543), every row.</summary>
        [Test]
        public void ModeDurations_AreTheWebsTable()
        {
            Assert.AreEqual(7.0,  SchoolFormation.ModeDurSeconds(SchoolMode.Cluster), Eps);
            Assert.AreEqual(13.0, SchoolFormation.ModeDurSeconds(SchoolMode.Vortex),  Eps);
            Assert.AreEqual(16.0, SchoolFormation.ModeDurSeconds(SchoolMode.Tornado), Eps);
            Assert.AreEqual(15.0, SchoolFormation.ModeDurSeconds(SchoolMode.Cone),    Eps);
            Assert.AreEqual(15.0, SchoolFormation.ModeDurSeconds(SchoolMode.ConeUp),  Eps);
            Assert.AreEqual(9.0,  SchoolFormation.ModeDurSeconds(SchoolMode.Ball),    Eps);
            Assert.AreEqual(12.0, SchoolFormation.ModeDurSeconds(SchoolMode.Stream),  Eps);
        }

        /// <summary>
        /// <c>MODE_DUR[m]*dm + rand*6*dm</c> (:1547). The work order asks for "7-16 s ×
        /// modeDurMul", so that is what is asserted: every shape, both ends of the random.
        /// </summary>
        [Test]
        public void HoldTime_IsSevenToSixteenSecondsTimesModeDurMul()
        {
            foreach (SchoolMode m in SchoolFormation.Modes)
            {
                double lo = SchoolFormation.HoldSeconds(m, 1.0, 0.0);
                double hi = SchoolFormation.HoldSeconds(m, 1.0, 1.0);
                Assert.AreEqual(SchoolFormation.ModeDurSeconds(m), lo, Eps);
                Assert.AreEqual(lo + 6.0, hi, Eps);
                Assert.That(lo, Is.InRange(7.0, 16.0), $"{m} base hold");

                // …and modeDurMul scales the WHOLE window, jitter included — barracuda's 2.2
                // is what makes it hold a shape for half a minute instead of seven seconds.
                Assert.AreEqual(lo * 2.2, SchoolFormation.HoldSeconds(m, 2.2, 0.0), 1e-9);
                Assert.AreEqual(hi * 2.2, SchoolFormation.HoldSeconds(m, 2.2, 1.0), 1e-9);
            }
        }

        // ── Slot geometry: every mode against the web's own arithmetic ────────────

        /// <summary>
        /// builder.html :1557-1558. A flat ring of radius exactly R at the fish's own ySpread,
        /// every fish tangent to the circle.
        /// </summary>
        [Test]
        public void Vortex_IsAFlatRingOfRadiusR()
        {
            SchoolFormation.Slot fi = MakeSlot(3, 20);
            const double t = 4.0, spin = 0.033;
            SchoolFormation.Target g =
                SchoolFormation.FormTarget(fi, SchoolMode.Vortex, t, R, spin, Flen, 0.0, 0.0);

            double a = fi.Ang + t * spin;
            Assert.AreEqual(Math.Cos(a) * R, g.X, 1e-9);
            Assert.AreEqual(Math.Sin(a) * R, g.Z, 1e-9);
            Assert.AreEqual(fi.YSpread, g.Y, 1e-9);
            Assert.AreEqual(R, Math.Sqrt(g.X * g.X + g.Z * g.Z), 1e-6, "radius is exactly R");
            // Tangent, and a unit vector: the fish swims AROUND the ring, not at its centre.
            Assert.AreEqual(1.0, Math.Sqrt(g.VX * g.VX + g.VZ * g.VZ), 1e-9);
            Assert.AreEqual(0.0, g.VX * g.X + g.VZ * g.Z, 1e-6, "heading ⟂ radius");
        }

        /// <summary>builder.html :1560 — a vertical CYLINDER: radius R*0.55, height R*1.8.</summary>
        [Test]
        public void Tornado_IsACylinderOfRadius055RAndHeight18R()
        {
            const double t = 2.0, spin = 0.04;
            double minY = double.MaxValue, maxY = double.MinValue;
            for (int i = 0; i < 64; i++)
            {
                SchoolFormation.Slot fi = MakeSlot(i, 64);
                SchoolFormation.Target g =
                    SchoolFormation.FormTarget(fi, SchoolMode.Tornado, t, R, spin, Flen, 0.0, 0.0);
                Assert.AreEqual(R * 0.55, Math.Sqrt(g.X * g.X + g.Z * g.Z), 1e-6, "constant radius");
                Assert.AreEqual(fi.CylY * R * 1.8, g.Y, 1e-9);
                minY = Math.Min(minY, g.Y); maxY = Math.Max(maxY, g.Y);
            }
            // The golden-ratio CylY fills the column instead of drawing one helix (:1531).
            Assert.Less(minY, R * 0.1);
            Assert.Greater(maxY, R * 1.6);
        }

        /// <summary>
        /// builder.html :1562/:1564 — the funnel and its inverse. Cone is WIDE at the floor
        /// (rr = R(1−0.8·cylY)) and ConeUp is narrow there (rr = R(0.2+0.8·cylY)); getting the two
        /// the same way round is the whole difference between the shapes.
        /// </summary>
        [Test]
        public void Cone_TapersUp_And_ConeUp_TapersDown()
        {
            SchoolFormation.Slot lo = MakeSlot(0, 8);    // cylY = 0      → the floor
            SchoolFormation.Slot hi = MakeSlot(0, 8);
            hi.CylY = 1.0;                               // the top of the column

            double RadiusOf(SchoolFormation.Slot f, SchoolMode m)
            {
                SchoolFormation.Target g = SchoolFormation.FormTarget(f, m, 0.0, R, 0.0, Flen, 0.0, 0.0);
                return Math.Sqrt(g.X * g.X + g.Z * g.Z);
            }

            Assert.AreEqual(R * 1.0, RadiusOf(lo, SchoolMode.Cone),   1e-6, "cone: wide base");
            Assert.AreEqual(R * 0.2, RadiusOf(hi, SchoolMode.Cone),   1e-6, "cone: point on top");
            Assert.AreEqual(R * 0.2, RadiusOf(lo, SchoolMode.ConeUp), 1e-6, "cone_up: point at the floor");
            Assert.AreEqual(R * 1.0, RadiusOf(hi, SchoolMode.ConeUp), 1e-6, "cone_up: wide on top");
        }

        /// <summary>
        /// builder.html :1566 — a SPHERE of radius R*0.55 (0.85 of it vertically), latitudes
        /// spread by acos and longitudes by the golden angle so nothing clumps at the poles.
        /// This is also the panic shape, so a loose ball is a bait ball that does not work.
        /// </summary>
        [Test]
        public void Ball_IsATightSphere_EvenlyCovered()
        {
            const double rr = R * 0.55;
            double minPh = double.MaxValue, maxPh = double.MinValue;
            for (int i = 0; i < 40; i++)
            {
                SchoolFormation.Slot fi = MakeSlot(i, 40);
                SchoolFormation.Target g =
                    SchoolFormation.FormTarget(fi, SchoolMode.Ball, 0.0, R, 0.0, Flen, 0.0, 0.0);
                double horiz = Math.Sqrt(g.X * g.X + g.Z * g.Z);
                Assert.AreEqual(Math.Sin(fi.BallPh) * rr, horiz, 1e-6);
                Assert.AreEqual(Math.Cos(fi.BallPh) * rr * 0.85, g.Y, 1e-9);
                // Everything is inside the ball radius — no stragglers.
                Assert.LessOrEqual(Math.Sqrt(horiz * horiz + g.Y * g.Y), rr + 1e-6);
                minPh = Math.Min(minPh, fi.BallPh); maxPh = Math.Max(maxPh, fi.BallPh);
            }
            Assert.Less(minPh, 0.3);            // a fish near each pole
            Assert.Greater(maxPh, Math.PI - 0.3);
        }

        /// <summary>
        /// builder.html :1568-1573. A migration LINE: fish are strung along the stream heading by
        /// their lane (±2R) and — the part that matters — every one of them faces the same way.
        /// </summary>
        [Test]
        public void Stream_IsALine_AndEveryFishFacesTheSameWay()
        {
            const double dir = 1.1;
            double first = 0.0;
            double along0 = 0.0, along1 = 0.0;
            for (int i = 0; i < 16; i++)
            {
                SchoolFormation.Slot fi = MakeSlot(i, 16);
                SchoolFormation.Target g =
                    SchoolFormation.FormTarget(fi, SchoolMode.Stream, 3.0, R, 0.0, Flen, dir, 0.0);
                Assert.AreEqual(Math.Cos(dir), g.VX, 1e-9);
                Assert.AreEqual(Math.Sin(dir), g.VZ, 1e-9);
                double along = g.X * Math.Cos(dir) + g.Z * Math.Sin(dir);
                if (i == 0) { first = along; along0 = along; }
                along1 = along;
            }
            Assert.AreEqual(-0.5 * R * 4.0, first, 1e-6, "lane −0.5 → 2R behind the centre");
            Assert.Greater(along1 - along0, R, "the school is strung out along its heading");
        }

        /// <summary>
        /// 🔴 builder.html :1575-1580, and the single most important line in the port.
        ///
        /// In <c>cluster</c> — four of the twelve slots on the wheel — every fish faces the
        /// SCHOOL's heading, not its own. That is what a polarised school IS, and it is exactly
        /// what Reynolds boids cannot guarantee: alignment is a soft steering weight, so a boid
        /// flock is only ever approximately polarised and drifts apart at the ends. The iPhone
        /// photographed the result.
        /// </summary>
        [Test]
        public void Cluster_IsPolarised_EveryFishOnTheSchoolsHeading()
        {
            const double heading = 2.2;
            for (int i = 0; i < 24; i++)
            {
                SchoolFormation.Slot fi = MakeSlot(i, 24);
                SchoolFormation.Target g =
                    SchoolFormation.FormTarget(fi, SchoolMode.Cluster, 5.0, R, 0.0, Flen, 0.0, heading);
                Assert.AreEqual(Math.Cos(heading), g.VX, 1e-9, $"fish {i}");
                Assert.AreEqual(Math.Sin(heading), g.VZ, 1e-9, $"fish {i}");
            }
        }

        /// <summary>
        /// …and the cluster slot only BREATHES around its fixed address: ±0.5·flen in x/z and
        /// ±0.35·flen in y (:1576-1578). It never translates, which is why the school does not
        /// pull itself apart while it holds this shape.
        /// </summary>
        [Test]
        public void ClusterSlot_StaysWithinHalfAFishLengthOfItsAddress()
        {
            SchoolFormation.Slot fi = MakeSlot(7, 32);
            double maxDx = 0.0, maxDy = 0.0, maxDz = 0.0;
            for (double t = 0.0; t < 200.0; t += 0.05)
            {
                SchoolFormation.Target g =
                    SchoolFormation.FormTarget(fi, SchoolMode.Cluster, t, R, 0.0, Flen, 0.0, 0.0);
                maxDx = Math.Max(maxDx, Math.Abs(g.X - fi.ClusterX));
                maxDy = Math.Max(maxDy, Math.Abs(g.Y - fi.ClusterY));
                maxDz = Math.Max(maxDz, Math.Abs(g.Z - fi.ClusterZ));
            }
            Assert.AreEqual(Flen * 0.5,  maxDx, Flen * 0.01);
            Assert.AreEqual(Flen * 0.35, maxDy, Flen * 0.01);
            Assert.AreEqual(Flen * 0.5,  maxDz, Flen * 0.01);
        }

        /// <summary>The per-fish seeds, builder.html :1526-1533 — the deterministic half, exactly.</summary>
        [Test]
        public void SlotSeeds_MatchTheWebsFormulas()
        {
            const int n = 50;
            for (int i = 0; i < n; i++)
            {
                SchoolFormation.Slot fi = MakeSlot(i, n);
                Assert.AreEqual((double)i / n * 6.283, fi.Ang, 1e-12, "ang = (i/N)*6.283");
                Assert.AreEqual(Math.Acos(1.0 - 2.0 * (i + 0.5) / n), fi.BallPh, 1e-12);
                Assert.AreEqual(i * 2.39996, fi.BallA, 1e-9);
                Assert.AreEqual((i * 0.61803398875) % 1.0, fi.CylY, 1e-12);
                Assert.AreEqual((double)i / n - 0.5, fi.Lane, 1e-12);
                Assert.That(fi.CylY, Is.InRange(0.0, 1.0));
            }
            // The scatter is a PANCAKE — the y span is the caller's spanY, not spanXZ (:1523-1524).
            SchoolFormation.Slot hiY = SchoolFormation.SlotFor(0, 8, R, R * 0.55, Flen,
                                                              1.0, 1.0, 1.0, 0, 0, 0, 0);
            Assert.AreEqual(R,          hiY.ClusterX, 1e-9);
            Assert.AreEqual(R * 0.275,  hiY.ClusterY, 1e-9);
            Assert.AreEqual(R,          hiY.ClusterZ, 1e-9);
        }

        // ── The speed law — the property boids do not have ───────────────────────

        /// <summary>
        /// 🔴 THE test this whole work order exists for (builder.html :1610).
        ///
        /// A fish's speed is <c>min(cap, distanceToItsSlot × chaseK)</c>. Far from its slot it
        /// swims at the cap; ARRIVED, it drops to the cruise floor. Unity's boids ran at
        /// <c>speed = MaxSpeed</c> unconditionally (BoidsJob :189-192) — a fish that can never
        /// slow down cannot hold a formation, and 160 of them stretch into the ribbon the user
        /// photographed.
        /// </summary>
        [Test]
        public void SpeedLaw_SlowsDownOnceTheFishReachesItsSlot()
        {
            const double cap = 4.0 / 60.0;                 // 4.0 u/s barracuda cap, per frame
            double chaseK = SchoolFormation.ChaseK(0.8, 0.0);   // easeMul 0.8 → 0.0528/frame
            double floor = Flen * SchoolFormation.CruiseFloorPerFrame;

            double far  = SchoolFormation.StepSpeedPerFrame(R,      chaseK, cap, Flen, false);
            double near = SchoolFormation.StepSpeedPerFrame(0.0,    chaseK, cap, Flen, false);
            double mid  = SchoolFormation.StepSpeedPerFrame(cap / chaseK * 0.5, chaseK, cap, Flen, false);

            Assert.AreEqual(cap + floor, far, 1e-12, "a straggler is capped, not unbounded");
            Assert.AreEqual(floor, near, 1e-12, "arrived → the cruise floor and nothing more");
            Assert.Less(near, far, "arriving must COST speed — this is what boids lack");
            Assert.AreEqual(cap * 0.5 + floor, mid, 1e-12, "linear in the distance between");

            // 🔎 A transcription note that took a red test to notice, and it is the web's own
            // behaviour rather than a port bug: for school:barracuda the cruise floor (flen×0.005)
            // is LARGER than the slot-chase cap (flen×0.065×swimMul, swimMul 0.06), so on this
            // path an arrived barracuda would still drift at 5.1 u/s. The web never runs it there
            // — `calm: true` sends every barracuda down CalmStepPerFrame, which has no floor at
            // all. On a shoal that really does use this path (scad: swimMul 1) the floor is an
            // eighth of the cap, which is the "ไม่หยุดนิ่ง" it was written to be.
            const double ScadFlen = 1.911 * 2.2;             // 4.20 u
            double scadCap = ScadFlen * 0.04 * 1.0;           // shoal cap, per frame (:1741)
            double scadFloor = ScadFlen * SchoolFormation.CruiseFloorPerFrame;
            Assert.Less(scadFloor, scadCap * 0.2, "the floor is a scull, not a swim");
            Assert.Less(SchoolFormation.StepSpeedPerFrame(0.0, SchoolFormation.ShoalChaseK, scadCap, ScadFlen, false),
                        SchoolFormation.StepSpeedPerFrame(1e6, SchoolFormation.ShoalChaseK, scadCap, ScadFlen, false) * 0.2,
                        "arriving costs a shoal 80 % of its speed");

            // Monotone in the distance to the slot, and never negative.
            double prev = -1.0;
            for (double d = 0.0; d < R; d += R / 200.0)
            {
                double v = SchoolFormation.StepSpeedPerFrame(d, chaseK, cap, Flen, false);
                Assert.GreaterOrEqual(v, prev - 1e-12);
                Assert.GreaterOrEqual(v, 0.0);
                prev = v;
            }

            // Fleeing raises only the CAP (×1.5, :1610) — a frightened fish already at its slot
            // is not launched across the map.
            Assert.AreEqual(cap * 1.5 + floor,
                            SchoolFormation.StepSpeedPerFrame(R, chaseK, cap, Flen, true), 1e-12);
            Assert.AreEqual(floor,
                            SchoolFormation.StepSpeedPerFrame(0.0, chaseK, cap, Flen, true), 1e-12);
        }

        /// <summary>
        /// The calm path (:1593) — <c>school:barracuda</c>'s, almost always. Same spring, and it
        /// has no cruise floor at all: an arrived barracuda stops dead relative to the school and
        /// only holds heading. "ตัวแข็ง…สโลว์ + เรียงหัวเป็นระเบียบ".
        /// </summary>
        [Test]
        public void CalmSpeedLaw_StopsCompletelyAtTheSlot()
        {
            const double cap = 4.0 / 60.0;
            Assert.AreEqual(0.0, SchoolFormation.CalmStepPerFrame(0.0, cap), 1e-12);
            Assert.AreEqual(cap * 1.8, SchoolFormation.CalmStepPerFrame(1e6, cap), 1e-12,
                            "capped at 1.8× the cruise cap");
            // Linear until the 1.8× cap binds, which for these numbers is at 2.4 u — a seventh of
            // a barracuda. Past that the fish is simply at its cap, which is the point: the calm
            // path is a slow spring, not a dash.
            double dLin = cap * SchoolFormation.CalmCapMul / SchoolFormation.CalmChasePerFrame * 0.5;
            Assert.AreEqual(dLin * 0.05, SchoolFormation.CalmStepPerFrame(dLin, cap), 1e-12,
                            "…but linear in the distance until it binds");
            Assert.AreEqual(cap * 1.8, SchoolFormation.CalmStepPerFrame(R, cap), 1e-12,
                            "a whole formation radius away it is still only at the cap");
        }

        /// <summary>
        /// The chase gain, <c>easeL*2.2</c> with <c>easeL = 0.03*easeMul</c> (:1535, :1761), and
        /// what the web's own barracuda numbers make of it.
        /// </summary>
        [Test]
        public void ChaseGain_IsEaseLTimes2Point2()
        {
            Assert.AreEqual(0.03 * 2.2, SchoolFormation.ChaseK(1.0, 0.0), 1e-12);
            Assert.AreEqual(0.03 * 0.8 * 2.2, SchoolFormation.ChaseK(0.8, 0.0), 1e-12);
            // Panic adds FleeMath.FleeEase on top, exactly as the web adds flp.L (:1760).
            Assert.Greater(SchoolFormation.ChaseK(0.8, FleeMath.FleeEase(1.0)),
                           SchoolFormation.ChaseK(0.8, 0.0));
            // A bad easeMul must not produce a zero or negative gain.
            Assert.AreEqual(0.03 * 2.2, SchoolFormation.ChaseK(0.0, 0.0), 1e-12);
        }

        /// <summary>
        /// A whole school, simulated with nothing but the ported law, converges onto its slots and
        /// STAYS there — the end-to-end version of the speed test, and the thing the screenshot
        /// says the boids never did.
        /// </summary>
        [Test]
        public void SimulatedSchool_ConvergesOntoItsSlotsAndHolds()
        {
            const int n = 60;
            const double cap = 4.0 / 60.0;
            double chaseK = SchoolFormation.ChaseK(0.8, 0.0);

            var slots = new SchoolFormation.Slot[n];
            var px = new double[n]; var py = new double[n]; var pz = new double[n];
            var rnd = new Random(7);
            for (int i = 0; i < n; i++)
            {
                slots[i] = SchoolFormation.SlotFor(i, n, R, R * 0.55, Flen,
                                                   rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble(),
                                                   rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble(),
                                                   rnd.NextDouble());
                // Start them scattered over twice the formation, i.e. genuinely lost.
                px[i] = (rnd.NextDouble() - 0.5) * R * 4.0;
                py[i] = (rnd.NextDouble() - 0.5) * R;
                pz[i] = (rnd.NextDouble() - 0.5) * R * 4.0;
            }

            double Rms(double t)
            {
                double s = 0.0;
                for (int i = 0; i < n; i++)
                {
                    SchoolFormation.Target g = SchoolFormation.FormTarget(
                        slots[i], SchoolMode.Cluster, t, R, 0.0, Flen, 0.0, 0.0);
                    double dx = px[i] - g.X, dy = py[i] - g.Y, dz = pz[i] - g.Z;
                    s += dx * dx + dy * dy + dz * dz;
                }
                return Math.Sqrt(s / n);
            }

            double start = Rms(0.0);
            // 240 s at 60 fps, straight-line motion toward the slot (the heading cap only slows
            // convergence, so leaving it out is the conservative version of this assertion). The
            // calm path is capped at 1.8 × 4.0 u/s, so crossing 300 u takes about a minute — this
            // school really is as unhurried as the web's.
            double t0 = 0.0;
            for (int step = 0; step < 14400; step++)
            {
                t0 = step / 60.0;
                for (int i = 0; i < n; i++)
                {
                    SchoolFormation.Target g = SchoolFormation.FormTarget(
                        slots[i], SchoolMode.Cluster, t0, R, 0.0, Flen, 0.0, 0.0);
                    double dx = g.X - px[i], dy = g.Y - py[i], dz = g.Z - pz[i];
                    double d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (d < 1e-9) continue;
                    double v = SchoolFormation.CalmStepPerFrame(d, cap);
                    if (v > d) v = d;
                    px[i] += dx / d * v; py[i] += dy / d * v; pz[i] += dz / d * v;
                }
            }

            double end = Rms(t0);
            Assert.Less(end, start * 0.05, $"school never converged: {start:F1} → {end:F1}");
            Assert.Less(end, Flen, "every fish should sit within a body length of its slot");
        }

        // ── Mode switching ───────────────────────────────────────────────────────

        /// <summary>
        /// The wheel turns on the web's schedule, and nothing else moves it. Driven with
        /// barracuda's own <c>modeDurMul = 2.2</c>, so the window is 15.4-48.4 s.
        /// </summary>
        [Test]
        public void ModeSwitching_HappensOnlyInsideTheWebsWindow()
        {
            SchoolFormation.ModeState s = SchoolFormation.NewState(0xBEEF, 0.0);
            double last = 0.0;
            int changes = 0;
            double minGap = double.MaxValue, maxGap = 0.0;

            for (double t = 0.0; t < 1200.0; t += 1.0 / 30.0)
            {
                if (!SchoolFormation.Step(ref s, t, 0.0, false, 2.2, 2.5)) continue;
                if (changes > 0)
                {
                    double gap = t - last;
                    minGap = Math.Min(minGap, gap);
                    maxGap = Math.Max(maxGap, gap);
                }
                last = t; changes++;
            }

            Assert.Greater(changes, 10, "the school must actually cycle");
            // 7 s and 16 s are the ends of MODE_DUR; ×2.2, and up to +6×2.2 of jitter. A "gap"
            // here can span several draws of the same shape (which only extend the timer), so the
            // floor is what is pinned — the ceiling is unbounded by construction.
            Assert.GreaterOrEqual(minGap, 7.0 * 2.2 - 0.1, $"changed after only {minGap:F1}s");
            Assert.LessOrEqual(16.0 * 2.2 + 6.0 * 2.2 + 0.1, maxGap + 1e9); // documented, not bounding
            Assert.Greater(maxGap, 0.0);

            // …and with modeDurMul = 1 the floor comes back down to the web's own 7 s.
            SchoolFormation.ModeState s1 = SchoolFormation.NewState(0x1234, 0.0);
            double last1 = 0.0, min1 = double.MaxValue; int n1 = 0;
            for (double t = 0.0; t < 1200.0; t += 1.0 / 30.0)
            {
                if (!SchoolFormation.Step(ref s1, t, 0.0, false, 1.0, 1.0)) continue;
                if (n1 > 0) min1 = Math.Min(min1, t - last1);
                last1 = t; n1++;
            }
            Assert.Greater(n1, 30);
            Assert.GreaterOrEqual(min1, 7.0 - 0.1);
            Assert.Less(min1, 16.0 + 6.0, "…and it does change inside one window");
        }

        /// <summary>
        /// Re-drawing the shape a school is ALREADY in only extends the timer (:1548). Without
        /// that early return the morph would restart from cluster to cluster — the school would
        /// blend forever and never settle — and cluster is four of the twelve slots, so it comes
        /// up constantly.
        /// </summary>
        [Test]
        public void ReDrawingTheSameMode_ExtendsTheTimerWithoutRestartingTheMorph()
        {
            SchoolFormation.ModeState s = SchoolFormation.NewState(1, 0.0);
            SchoolFormation.SetMode(ref s, SchoolMode.Stream, 10.0, false, 1.0, 1.0, 0.0, 0.25);
            double t0 = s.TransT0;
            double until = s.Until;

            SchoolFormation.SetMode(ref s, SchoolMode.Stream, 14.0, false, 1.0, 1.0, 0.5, 0.9);
            Assert.AreEqual(t0, s.TransT0, Eps, "the morph must not restart");
            Assert.Greater(s.Until, until, "…but the hold does extend");
            Assert.AreEqual(SchoolMode.Stream, s.Mode);
        }

        /// <summary>
        /// Panic past 0.6 balls the school up on a FAST morph and pins it for 2.5 s (:1697), and a
        /// pod — a handful of real animals — never does (FleeMath.ShouldBallUp).
        /// </summary>
        [Test]
        public void Panic_BallsTheSchoolUp_AndHoldsIt()
        {
            SchoolFormation.ModeState s = SchoolFormation.NewState(5, 0.0);
            SchoolFormation.Step(ref s, 0.0, 0.0, false, 1.0, 1.0);

            Assert.IsTrue(SchoolFormation.Step(ref s, 30.0, 0.9, false, 1.0, 1.0));
            Assert.AreEqual(SchoolMode.Ball, s.Mode);
            Assert.AreEqual(SchoolFormation.TransDurPanicSeconds, s.TransDur, Eps,
                            "panic morphs fast — 1.6 s, not 8");
            Assert.GreaterOrEqual(s.Until, 30.0 + SchoolFormation.BallHoldSeconds - Eps);

            // The threshold is the web's 0.6 and it agrees with FleeMath's.
            Assert.AreEqual(FleeMath.BallUpPanic, SchoolFormation.BallUpPanic, Eps);
            Assert.AreEqual(FleeMath.BallHoldSeconds, SchoolFormation.BallHoldSeconds, Eps);

            SchoolFormation.ModeState pod = SchoolFormation.NewState(6, 0.0);
            SchoolFormation.Step(ref pod, 30.0, 0.9, true, 1.0, 1.0);
            Assert.AreNotEqual(SchoolMode.Ball, pod.Mode, "a pod of whales does not bait-ball");
        }

        /// <summary>
        /// The morph is a per-fish staggered smoothstep (:1755-1757): it starts at 0, ends at 1,
        /// never overshoots, and a fish with a larger <c>trJit</c> is always BEHIND one with less
        /// — which is what makes a school reform progressively instead of snapping.
        /// </summary>
        [Test]
        public void Morph_IsAStaggeredSmoothstep()
        {
            SchoolFormation.ModeState s = SchoolFormation.NewState(2, 0.0);
            SchoolFormation.SetMode(ref s, SchoolMode.Tornado, 0.0, false, 1.0, 1.0, 0.0, 0.0);
            Assert.AreEqual(8.0, s.TransDur, Eps);

            Assert.AreEqual(0.0, SchoolFormation.MorphBlend(s, 0.0, 0.0), Eps);
            Assert.AreEqual(0.5, SchoolFormation.MorphBlend(s, 4.0, 0.0), 1e-12);
            Assert.AreEqual(1.0, SchoolFormation.MorphBlend(s, 8.0, 0.0), Eps);

            double prev = -1.0;
            for (double t = 0.0; t <= 12.0; t += 0.1)
            {
                double e = SchoolFormation.MorphBlend(s, t, 0.0);
                Assert.That(e, Is.InRange(0.0, 1.0));
                Assert.GreaterOrEqual(e, prev - 1e-12, "monotone — no snap-back");
                prev = e;
            }

            // Stagger: the latest fish (trJit 0.35) lags the earliest.
            Assert.Less(SchoolFormation.MorphBlend(s, 3.0, 0.35),
                        SchoolFormation.MorphBlend(s, 3.0, 0.0));
            // …and everyone is finished by 1.4× the duration (:1754).
            Assert.IsFalse(SchoolFormation.MorphFinished(s, 8.0));
            Assert.IsTrue(SchoolFormation.MorphFinished(s, 8.0 * 1.4 + 0.1));

            // transDurMul stretches it — barracuda's 2.5 is a 20 s morph.
            SchoolFormation.ModeState slow = SchoolFormation.NewState(3, 0.0);
            SchoolFormation.SetMode(ref slow, SchoolMode.Vortex, 0.0, false, 1.0, 2.5, 0.0, 0.0);
            Assert.AreEqual(20.0, slow.TransDur, Eps);
        }

        // ── The cohesion metrics themselves ──────────────────────────────────────

        /// <summary>
        /// The QC oracle has to be able to tell the two pictures apart — a school sitting on its
        /// slots, and the same fish smeared over four times the radius with random headings. If it
        /// cannot, a log line saying "the school is fine" is worth nothing, which is how build 244
        /// shipped.
        /// </summary>
        [Test]
        public void CohesionMetrics_SeparateASchoolFromASmear()
        {
            const int n = 80;
            var sx = new double[n]; var sy = new double[n]; var sz = new double[n];
            var tight = new double[n]; var tightY = new double[n]; var tightZ = new double[n];
            var loose = new double[n]; var looseY = new double[n]; var looseZ = new double[n];
            var hTight = new double[n]; var hLoose = new double[n];

            var rnd = new Random(11);
            for (int i = 0; i < n; i++)
            {
                SchoolFormation.Slot fi = SchoolFormation.SlotFor(
                    i, n, R, R * 0.55, Flen,
                    rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble(),
                    rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble());
                SchoolFormation.Target g = SchoolFormation.FormTarget(
                    fi, SchoolMode.Cluster, 0.0, R, 0.0, Flen, 0.0, 1.0);
                sx[i] = g.X; sy[i] = g.Y; sz[i] = g.Z;

                // On its slot, on the school's heading.
                tight[i] = g.X + (rnd.NextDouble() - 0.5) * Flen * 0.1;
                tightY[i] = g.Y; tightZ[i] = g.Z;
                hTight[i] = 1.0;

                // Smeared over 4× the radius with a heading of its own — the screenshot.
                loose[i] = (rnd.NextDouble() - 0.5) * R * 8.0;
                looseY[i] = (rnd.NextDouble() - 0.5) * R * 2.0;
                looseZ[i] = (rnd.NextDouble() - 0.5) * R * 8.0;
                hLoose[i] = rnd.NextDouble() * Math.PI * 2.0;
            }

            SchoolFormation.Cohesion school = SchoolFormation.Measure(
                tight, tightY, tightZ, sx, sy, sz, hTight, n, 0, 0, 0, R, Flen);
            SchoolFormation.Cohesion smear = SchoolFormation.Measure(
                loose, looseY, looseZ, sx, sy, sz, hLoose, n, 0, 0, 0, R, Flen);

            Assert.Less(school.SlotRmsFlen, 0.1, "on-slot: RMS well under one body length");
            Assert.Greater(smear.SlotRmsFlen, 5.0, "smeared: many body lengths off-slot");

            // Not 1.0: the web scatters cluster slots across a BOX of half-side R (:1524), whose
            // corners are R√2 from the centre, so a well-formed school reads ~0.78 rather than 1.
            // What the metric has to do is separate it from the smear, and it does — by 3×.
            Assert.Greater(school.InsideFrac, 0.7, "a formed school is mostly inside its own radius");
            Assert.Less(smear.InsideFrac, school.InsideFrac / 2.5);

            Assert.AreEqual(1.0, school.Polarisation, 1e-9, "polarised — the cluster rule");
            Assert.Less(smear.Polarisation, 0.35, "random headings cancel out");

            Assert.Greater(smear.NeighbourFlen, school.NeighbourFlen,
                           "a smear puts more water between neighbours");
        }

        /// <summary>Degenerate inputs must not throw or produce NaN — this runs in the render loop.</summary>
        [Test]
        public void CohesionMetrics_SurviveDegenerateInput()
        {
            SchoolFormation.Cohesion c = SchoolFormation.Measure(
                null, null, null, null, null, null, null, 0, 0, 0, 0, 1, 1);
            Assert.AreEqual(0.0, c.SlotRmsFlen, Eps);

            var one = new double[1];
            SchoolFormation.Cohesion c1 = SchoolFormation.Measure(
                one, one, one, one, one, one, one, 1, 0, 0, 0, 10, 0.0);
            Assert.IsFalse(double.IsNaN(c1.SlotRmsFlen) || double.IsNaN(c1.NeighbourFlen)
                        || double.IsNaN(c1.Polarisation) || double.IsNaN(c1.InsideFrac));
        }

        /// <summary>
        /// Every mode produces a finite slot and a unit heading, for every fish, over a long run —
        /// this is fed straight into a transform matrix, so one NaN is a school of fish at the
        /// world origin.
        /// </summary>
        [Test]
        public void EveryMode_IsFiniteAndUnitHeaded()
        {
            foreach (SchoolMode m in Enum.GetValues(typeof(SchoolMode)))
            for (int i = 0; i < 12; i++)
            {
                SchoolFormation.Slot fi = MakeSlot(i, 12);
                for (double t = 0.0; t < 400.0; t += 37.0)
                {
                    SchoolFormation.Target g =
                        SchoolFormation.FormTarget(fi, m, t, R, 0.03, Flen, 0.7, 1.3);
                    Assert.IsFalse(double.IsNaN(g.X) || double.IsNaN(g.Y) || double.IsNaN(g.Z), $"{m}");
                    double len = Math.Sqrt(g.VX * g.VX + g.VZ * g.VZ);
                    Assert.AreEqual(1.0, len, 1e-9, $"{m} heading must be a unit vector");
                }
            }
        }

        /// <summary>
        /// The turn cap is the web's ±0.045 rad/frame (±0.05 calm) and it takes the SHORT way
        /// round — a fish must never spin the long way to face 1° left.
        /// </summary>
        [Test]
        public void TurnStep_IsCappedAndTakesTheShortWayRound()
        {
            const double cap = SchoolFormation.TurnCapPerFrame;
            Assert.AreEqual(0.045, cap, Eps);
            Assert.AreEqual(0.05, SchoolFormation.CalmTurnCapPerFrame, Eps);

            Assert.AreEqual(cap, SchoolFormation.TurnStep(0.0, 3.0, cap), Eps);
            Assert.AreEqual(-cap, SchoolFormation.TurnStep(0.0, -3.0, cap), Eps);
            Assert.AreEqual(0.01, SchoolFormation.TurnStep(0.0, 0.01, cap), 1e-12);

            // 0.1 rad short of a full turn: the answer is −0.045, not +0.045.
            Assert.Less(SchoolFormation.TurnStep(0.0, 2.0 * Math.PI - 0.1, cap), 0.0);
        }

        /// <summary>
        /// The settle band (:1601). Polarised shapes get 3.0 fish lengths and free ones 0.9 —
        /// wide, so a fish stops steering at its own bobbing slot and takes the school's heading
        /// instead. Narrow it and the heads wag, which is what "ไม่นิ่ง" looks like up close.
        /// </summary>
        [Test]
        public void SettleBand_IsWideForPolarisedFormations()
        {
            Assert.AreEqual(Flen * 3.0, SchoolFormation.SettleDistance(Flen, true), Eps);
            Assert.AreEqual(Flen * 0.9, SchoolFormation.SettleDistance(Flen, false), Eps);
            Assert.Greater(SchoolFormation.SettleDistance(Flen, true),
                           SchoolFormation.SettleDistance(Flen, false));
        }
    }
}
