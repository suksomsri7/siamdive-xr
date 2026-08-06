using System;
using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// <see cref="CameraRange"/> — the zoom-out ceiling and the far/fog ranges that have to keep
    /// up with it, transcribed from the web's <c>updateViewRange()</c> (builder.html:709-722).
    ///
    /// The acceptance test for the work order of 2026-08-06 ("zoom out ได้มากกว่านี้ตามขนาดแมพ —
    /// Atlantis อยากเห็นเต็มแมพ") is <see cref="AnOrdinaryMapKeepsExactlyTheOldRanges"/> together
    /// with <see cref="APortraitPhoneCanSeeTheWholeMapAtFullZoomOut"/>: the first says nothing the
    /// user already liked has moved, the second says the thing they asked for is now true.
    /// </summary>
    public class CameraRangeTests
    {
        private const double Eps = 1e-6;

        /// <summary>A 1080×2340 phone held upright — what the user is actually testing on.</summary>
        private const double PhoneAspect = 1080.0 / 2340.0;

        /// <summary>The app's camera. AppBoot never changes fieldOfView outside tour mode.</summary>
        private const double Fov = 60.0;

        // ── The part that must not change ─────────────────────────────────────────

        /// <summary>
        /// 🔴 The regression this whole change is judged against. AppBoot shipped fog 500…9,000
        /// and a 9,000 far plane as literals; an ordinary map has to come out of the formulas with
        /// the same numbers, or "อย่าเปลี่ยนลุคปกติ" has been broken on every map in the app in
        /// order to fix one.
        ///
        /// The web's bare sand radius is 340 (builder.html:525), i.e. area scale 1 — the demo map.
        /// </summary>
        [Test]
        public void AnOrdinaryMapKeepsExactlyTheOldRanges()
        {
            CameraRange.ViewRange v = CameraRange.For(CameraRange.SandRadius, true, Fov, PhoneAspect);

            Assert.AreEqual(500.0, v.FogNear, Eps, "fog start must be the 500 AppBoot already used");
            Assert.AreEqual(9000.0, v.FogFar, Eps, "fog end must be the 9000 AppBoot already used");

            // 340 × 1.1 = 374, under the 500 floor; 2600 × 3.4 = 8840, under the 9000 floor. Both
            // ends land on their floors, which is WHY the old literals were right for this map.
            Assert.Less(CameraRange.SandRadius * CameraRange.FogNearK, CameraRange.FogNearFloor);
            Assert.Less(v.MaxDistance * CameraRange.FogFarK, CameraRange.FogFarFloor);
        }

        /// <summary>
        /// 🔴 …and the reason that test is about the DEMO map and not about an arbitrary number:
        /// the sand radius these formulas are calibrated against is the same 340 the seabed is
        /// actually built from. Two sand radii in one codebase is not a preference, it is one of
        /// them being wrong — the same trap <c>SwimStyle.UnitsPerMetre</c> fell into.
        /// </summary>
        [Test]
        public void TheSandRadiusIsTheOneTheSeabedIsBuiltFrom()
        {
            Assert.AreEqual(SeabedGeom.SandRadius, CameraRange.SandRadius, 1e-6);
        }

        /// <summary>
        /// …and the far plane on that same map still clears the whole scene with room to spare, so
        /// dropping from 9,000 to the formula's answer cannot clip anything.
        /// </summary>
        [Test]
        public void AnOrdinaryMapsFarPlaneStillClearsEverything()
        {
            CameraRange.ViewRange v = CameraRange.For(CameraRange.SandRadius, true, Fov, PhoneAspect);

            // Standing at the ceiling, the far rim of the map is maxD + reach away.
            double farthestThingVisible = v.MaxDistance + CameraRange.SandRadius;
            Assert.Greater(v.Far, farthestThingVisible,
                           "the far plane must outreach the map seen from the zoom ceiling");
            Assert.AreEqual(2600.0 * 2.5 + 340.0 + 1200.0, v.Far, 1e-3, "builder.html:715, verbatim");
        }

        // ── The part the user asked for ───────────────────────────────────────────

        /// <summary>
        /// 🔴 The ask itself, stated as geometry rather than as a feeling: from the ceiling, the
        /// whole map must fall inside the frustum.
        ///
        /// A sphere of radius r at distance d subtends <c>asin(r/d)</c>, so "it fits" is
        /// <c>asin(r/maxD) ≤ θ</c> for the NARROWER half-FOV θ — on a phone held upright, the
        /// horizontal one. Checked across four map sizes because the ceiling changes which of its
        /// two terms is in charge as the map grows.
        /// </summary>
        [Test]
        public void APortraitPhoneCanSeeTheWholeMapAtFullZoomOut()
        {
            double tanV = Math.Tan(Fov * 0.5 * Math.PI / 180.0);
            double halfAngle = Math.Atan(Math.Min(tanV, tanV * PhoneAspect));

            foreach (double reach in new[] { 340.0, 680.0, 1020.0, 2000.0 })
            {
                double maxD = CameraRange.MaxDistance(reach, true, Fov, PhoneAspect);
                double subtended = Math.Asin(Math.Min(1.0, reach / maxD));
                Assert.LessOrEqual(subtended, halfAngle + 1e-9,
                                   $"reach {reach}: the map still does not fit at full zoom-out");
            }
        }

        /// <summary>
        /// …and it is still THERE when it fits: the far plane outreaches the map's far rim and the
        /// fog has not yet swallowed it. A ceiling you can reach but which shows you nothing is the
        /// same bug wearing a different screenshot, which is why both are pinned together.
        /// </summary>
        [Test]
        public void AtFullZoomOutTheMapIsInsideBothTheFarPlaneAndTheFog()
        {
            foreach (double reach in new[] { 340.0, 680.0, 1020.0, 2000.0 })
            {
                CameraRange.ViewRange v = CameraRange.For(reach, true, Fov, PhoneAspect);
                double rim = v.MaxDistance + reach;

                Assert.Greater(v.Far, rim, $"reach {reach}: far plane clips the map");
                Assert.Greater(v.FogFar, rim, $"reach {reach}: fog is total before the map ends");

                // Linear fog: how much of the way to fully-fogged the far rim sits. Anything past
                // 1.0 is an invisible map. The centre of the map is what matters most and it is
                // nearer still.
                double fogged = (rim - v.FogNear) / (v.FogFar - v.FogNear);
                Assert.Less(fogged, 0.85, $"reach {reach}: the rim is washed out at full zoom-out");
            }
        }

        /// <summary>
        /// The ceiling grows with the map. Not a tautology worth skipping: the bug being fixed was
        /// a CONSTANT ceiling, so a test that a bigger map gets a bigger number is the direct
        /// negation of it.
        /// </summary>
        [Test]
        public void TheCeilingGrowsWithTheMap()
        {
            double prev = 0.0;
            foreach (double reach in new[] { 340.0, 1200.0, 2400.0, 4800.0 })
            {
                double maxD = CameraRange.MaxDistance(reach, true, Fov, PhoneAspect);
                Assert.Greater(maxD, prev, $"reach {reach} must allow more zoom-out than the last");
                prev = maxD;
            }
        }

        /// <summary>
        /// Every map, however small, gets at least the ceiling the app shipped with — 950 — and in
        /// fact at least the web's own floor of 2,600. Nobody loses reach.
        /// </summary>
        [Test]
        public void NoMapEndsUpTighterThanTheOldFixedCeiling()
        {
            foreach (double reach in new[] { 1.0, 30.0, 120.0, 340.0 })
                Assert.GreaterOrEqual(CameraRange.MaxDistance(reach, true, Fov, PhoneAspect),
                                      CameraRange.MaxDistFloorFoggy,
                                      $"reach {reach} fell below the web's own floor");
        }

        // ── The web's arithmetic, term by term ────────────────────────────────────

        /// <summary>
        /// builder.html:714 <c>max(foggy ? 2600 : 3600, reach*3.5)</c>, on a viewport wide enough
        /// that the trigonometric term is not the one in charge (see
        /// <see cref="FitDistance_IsTheWebsAnswerOnLandscapeAndMoreOnPortrait"/>).
        /// </summary>
        [Test]
        public void MaxDistance_IsTheWebsFormula_OnALandscapeViewport()
        {
            const double Landscape = 16.0 / 9.0;

            Assert.AreEqual(2600.0, CameraRange.MaxDistance(340.0, true, Fov, Landscape), 1e-3,
                            "small map underwater: the 2600 floor");
            Assert.AreEqual(3600.0, CameraRange.MaxDistance(340.0, false, Fov, Landscape), 1e-3,
                            "small map in daylight: the higher 3600 floor, fog being off");
            Assert.AreEqual(2000.0 * 3.5, CameraRange.MaxDistance(2000.0, true, Fov, Landscape), 1e-3,
                            "big map: reach × 3.5");
        }

        /// <summary>
        /// Fog-off raises the ceiling, because the web's daylight mode deletes the fog entirely
        /// (builder.html:682) and you can see much further with it gone.
        /// </summary>
        [Test]
        public void ClearWaterAllowsAtLeastAsMuchZoomOutAsFoggy()
        {
            foreach (double reach in new[] { 100.0, 340.0, 1500.0 })
                Assert.GreaterOrEqual(CameraRange.MaxDistance(reach, false, Fov, PhoneAspect),
                                      CameraRange.MaxDistance(reach, true, Fov, PhoneAspect),
                                      $"reach {reach}");
        }

        // ── The one place this deliberately exceeds the web ───────────────────────

        /// <summary>
        /// 🔴 The web's 3.5× was chosen against a landscape browser window. Held upright, a phone's
        /// horizontal field of view is the narrow one and 3.5× is not enough — which is why this is
        /// a <c>max</c> of the web's number and a real fit distance, and why exceeding the web here
        /// is a decision on the record and not a porting slip.
        ///
        /// The two numbers, worked by hand for fov 60°:
        ///   portrait  aspect 0.4615 → θ = atan(tan30° × 0.4615) = 14.92° → 1/sin θ = 3.88
        ///   landscape aspect 1.7778 → θ = 30° (vertical is now the narrow one) → 1/sin θ = 2.00
        /// </summary>
        [Test]
        public void FitDistance_IsTheWebsAnswerOnLandscapeAndMoreOnPortrait()
        {
            Assert.AreEqual(3.88, CameraRange.FitDistance(1.0, Fov, PhoneAspect), 0.01,
                            "portrait: 1/sin(14.92°)");
            Assert.AreEqual(2.00, CameraRange.FitDistance(1.0, Fov, 16.0 / 9.0), 0.01,
                            "landscape: the vertical FOV is the narrow one, 1/sin(30°)");

            Assert.Greater(CameraRange.FitDistance(1.0, Fov, PhoneAspect), CameraRange.MaxDistK,
                           "if this ever drops below 3.5 the trigonometric term is dead code");
            Assert.Less(CameraRange.FitDistance(1.0, Fov, 16.0 / 9.0), CameraRange.MaxDistK,
                        "…and on landscape the web's number must still be the one that wins");
        }

        /// <summary>Fit distance is linear in the map's size — it is a ratio, not a curve.</summary>
        [Test]
        public void FitDistance_ScalesLinearlyWithReach()
        {
            double unit = CameraRange.FitDistance(1.0, Fov, PhoneAspect);
            Assert.AreEqual(unit * 750.0, CameraRange.FitDistance(750.0, Fov, PhoneAspect), 1e-6);
        }

        /// <summary>A wider lens needs less room. Trivially true, and cheap to keep true.</summary>
        [Test]
        public void AWiderFovNeedsLessDistance()
        {
            Assert.Less(CameraRange.FitDistance(500.0, 90.0, PhoneAspect),
                        CameraRange.FitDistance(500.0, 60.0, PhoneAspect));
        }

        // ── Degenerate input ──────────────────────────────────────────────────────

        /// <summary>
        /// A map that reports no size at all still gets a usable view. <c>SceneBuilder</c> floors
        /// its radius at 1 u, but a zero here must not produce a zero ceiling and a camera that
        /// cannot move — it falls back to the web's own sand radius.
        /// </summary>
        [Test]
        public void ZeroOrNegativeReach_FallsBackToTheWebsSandRadius()
        {
            CameraRange.ViewRange zero = CameraRange.For(0.0, true, Fov, PhoneAspect);
            CameraRange.ViewRange neg = CameraRange.For(-5.0, true, Fov, PhoneAspect);
            CameraRange.ViewRange sand = CameraRange.For(CameraRange.SandRadius, true, Fov, PhoneAspect);

            Assert.AreEqual(sand.MaxDistance, zero.MaxDistance, Eps);
            Assert.AreEqual(sand.MaxDistance, neg.MaxDistance, Eps);
            Assert.Greater(zero.Far, 0.0);
        }

        /// <summary>A nonsense aspect or FOV must not divide by zero or return infinity.</summary>
        [Test]
        public void NonsenseViewportsStayFinite()
        {
            foreach (double aspect in new[] { 0.0, -1.0, 1e-9 })
                Assert.That(CameraRange.FitDistance(340.0, Fov, aspect), Is.Not.NaN.And.LessThan(1e12),
                            $"aspect {aspect}");

            foreach (double fov in new[] { 0.0, -30.0, 400.0 })
                Assert.That(CameraRange.FitDistance(340.0, fov, PhoneAspect), Is.Not.NaN.And.LessThan(1e12),
                            $"fov {fov}");
        }

        /// <summary>
        /// The near plane comes off the far plane (<c>far/40000</c>, builder.html:715) and never
        /// goes below 0.5, which is the value AppBoot already ships. Depth precision is a ratio, so
        /// a far plane that grows with the map has to bring the near plane with it or the seabed
        /// starts z-fighting at exactly the map sizes this change unlocks.
        /// </summary>
        [Test]
        public void NearPlaneTracksTheFarPlaneAndNeverDropsBelowTheShippedHalfUnit()
        {
            CameraRange.ViewRange small = CameraRange.For(340.0, true, Fov, PhoneAspect);
            Assert.AreEqual(0.5, small.Near, Eps, "small map keeps AppBoot's 0.5");

            CameraRange.ViewRange huge = CameraRange.For(20000.0, true, Fov, PhoneAspect);
            Assert.AreEqual(huge.Far / CameraRange.NearDiv, huge.Near, 1e-9);
            Assert.Greater(huge.Near, small.Near, "a far plane this deep needs the near plane out too");
        }
    }
}
