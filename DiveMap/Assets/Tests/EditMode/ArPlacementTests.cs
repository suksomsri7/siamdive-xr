using System;
using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// Unit tests for where the map stands in AR.
    ///
    /// The centrepiece is <see cref="SameEyeRayAsTheWeb"/>. This port deliberately does something
    /// the web does not — it moves the camera instead of shrinking the map — so the claim "same
    /// picture" has to be proved rather than asserted in a comment. It is proved the only way that
    /// means anything: by checking that every point of the map leaves the eye along the same ray in
    /// both schemes, which is exactly what a perspective camera can see.
    /// </summary>
    public class ArPlacementTests
    {
        // A realistic site: 340 units across, seabed at −6, centred away from the origin.
        private const double SizeX = 340, SizeZ = 260, MinY = -6;
        private static readonly Vec3 Center = new Vec3(25, 40, -18);

        private static Vec3 Sub(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        private static double Len(Vec3 v) => Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);

        private static void AssertSameDirection(Vec3 a, Vec3 b, string because)
        {
            double la = Len(a), lb = Len(b);
            Assert.Greater(la, 1e-12, because);
            double cos = (a.X * b.X + a.Y * b.Y + a.Z * b.Z) / (la * lb);
            Assert.AreEqual(1.0, cos, 1e-12, because);
        }

        // ── the claim that the whole design rests on ─────────────────────────────

        [Test]
        public void SameEyeRayAsTheWeb()
        {
            // The web's scheme: shrink the site by s and hang it in front of an eye at the origin.
            //   arSite.scale = s;  arSite.position = (−ctr.x·s, −minY·s − 0.3, −ctr.z·s − 1.4)
            // This port's scheme: leave the site alone, put the eye at CameraPosition().
            // If the two agree on the DIRECTION of every point, no camera can tell them apart.
            double s = ArPlacement.FitScale(SizeX, SizeZ);
            Vec3 eye = ArPlacement.CameraPosition(Center, MinY, s);

            var samples = new[]
            {
                new Vec3(Center.X + 170, MinY, Center.Z + 130),      // far corner of the seabed
                new Vec3(Center.X - 170, MinY, Center.Z - 130),      // the opposite one
                new Vec3(Center.X, MinY, Center.Z),                  // dead centre of the floor
                new Vec3(Center.X + 3, 96, Center.Z - 40),           // a fish up in the water column
                new Vec3(Center.X - 88, 12, Center.Z + 4),           // something mid-map
            };

            foreach (Vec3 p in samples)
            {
                // Web: eye at origin, so the view vector IS the transformed position. Unity mirrors
                // Z against three.js, so the web's −1.4 forward offset is +1.4 here.
                var web = new Vec3(
                    (p.X - Center.X) * s,
                    (p.Y - MinY) * s - ArPlacement.FloorDrop,
                    (p.Z - Center.Z) * s + ArPlacement.Distance);

                AssertSameDirection(web, Sub(p, eye),
                                    $"point {p} must sit in the same place on screen in both schemes");
            }
        }

        [Test]
        public void TheApparentSizeIsTheOneTheWebChose()
        {
            // Not just proportional — the map must come out ~1.1 m across, or it is not a tabletop.
            double s = ArPlacement.FitScale(SizeX, SizeZ);
            Assert.AreEqual(ArPlacement.TableSpan, ArPlacement.ApparentSpan(SizeX, s), 1e-12);
            Assert.Less(ArPlacement.ApparentSpan(SizeZ, s), ArPlacement.TableSpan, "the short side is shorter");
        }

        [Test]
        public void TheViewerStandsBackFurtherForABiggerSite()
        {
            // The scheme only works if distance tracks size — 1.4 m of apparent distance means a
            // very different number of world units for a 20-unit reef and a 900-unit wreck site.
            double small = ArPlacement.FitScale(20, 20), big = ArPlacement.FitScale(900, 900);
            double dSmall = Center.Z - ArPlacement.CameraPosition(Center, MinY, small).Z;
            double dBig = Center.Z - ArPlacement.CameraPosition(Center, MinY, big).Z;

            Assert.Greater(dBig, dSmall * 40, "a 45× wider site is viewed from 45× further out");
            Assert.AreEqual(ArPlacement.Distance, dSmall * small, 1e-9, "…but always 1.4 m of apparent distance");
            Assert.AreEqual(ArPlacement.Distance, dBig * big, 1e-9);
        }

        [Test]
        public void TheEyeIsAboveTheSeabed_NotInsideIt()
        {
            // FloorDrop is what makes it a model on a table rather than a wall you are standing in.
            double s = ArPlacement.FitScale(SizeX, SizeZ);
            Vec3 eye = ArPlacement.CameraPosition(Center, MinY, s);
            Assert.Greater(eye.Y, MinY, "you look DOWN at the site");
            Assert.AreEqual(ArPlacement.FloorDrop, (eye.Y - MinY) * s, 1e-9, "0.3 m above the seabed");
        }

        [Test]
        public void TheViewerStandsOnTheNearSide()
        {
            // Unity's camera looks toward +Z, so the eye has to be on the −Z side of the map. Get
            // this backwards and AR opens facing empty room with the site behind your head.
            double s = ArPlacement.FitScale(SizeX, SizeZ);
            Assert.Less(ArPlacement.CameraPosition(Center, MinY, s).Z, Center.Z);
        }

        // ── the guard the web wrote for a reason ─────────────────────────────────

        [Test]
        public void AnEmptyMapDoesNotSendTheCameraToInfinity()
        {
            // The web's `|| 100`. A brand-new map has a zero-size bounding box; 1.1/0 is infinity,
            // and an infinite camera position is a black screen with nothing logged anywhere.
            double s = ArPlacement.FitScale(0, 0);
            Assert.AreEqual(ArPlacement.TableSpan / 100.0, s, 1e-12);

            Vec3 eye = ArPlacement.CameraPosition(Center, MinY, s);
            Assert.IsFalse(double.IsNaN(eye.X) || double.IsInfinity(eye.X));
            Assert.IsFalse(double.IsNaN(eye.Y) || double.IsInfinity(eye.Y));
            Assert.IsFalse(double.IsNaN(eye.Z) || double.IsInfinity(eye.Z));
        }

        [Test]
        public void GarbageBoundsFallBackToTheSameDefault()
        {
            double want = ArPlacement.TableSpan / 100.0;
            Assert.AreEqual(want, ArPlacement.FitScale(double.NaN, double.NaN), 1e-12);
            Assert.AreEqual(want, ArPlacement.FitScale(double.PositiveInfinity, 3), 1e-12);
            Assert.AreEqual(want, ArPlacement.FitScale(-5, -5), 1e-12);
        }

        [Test]
        public void ANonsenseScaleReturnsTheCentreRatherThanInfinity()
        {
            Vec3 eye = ArPlacement.CameraPosition(Center, MinY, 0);
            Assert.AreEqual(Center.Z, eye.Z, 1e-12);
        }

        // ── the − / + buttons ────────────────────────────────────────────────────

        [Test]
        public void ZoomStepsMatchTheWeb()
        {
            double fit = ArPlacement.FitScale(SizeX, SizeZ);
            Assert.AreEqual(fit * 1.22, ArPlacement.Zoom(fit, fit, closer: true), 1e-12);
            Assert.AreEqual(fit * 0.82, ArPlacement.Zoom(fit, fit, closer: false), 1e-12);
        }

        [Test]
        public void InAndOutNearlyCancel_AsTheWebIntended()
        {
            // 1.22 × 0.82 = 1.0004. The asymmetry is deliberate; a user who presses + then − should
            // land back where they started, near enough not to notice.
            double fit = ArPlacement.FitScale(SizeX, SizeZ);
            double there = ArPlacement.Zoom(fit, fit, true);
            double back = ArPlacement.Zoom(there, fit, false);
            Assert.AreEqual(fit, back, fit * 0.001);
        }

        [Test]
        public void ZoomingOutForeverCannotLoseTheMap()
        {
            // The web has no limit: eleven presses of − leave the site 8 cm wide with no hint that
            // + is the way back. This is a deliberate difference from the web.
            double fit = ArPlacement.FitScale(SizeX, SizeZ);
            double v = fit;
            for (int i = 0; i < 50; i++) v = ArPlacement.Zoom(v, fit, closer: false);

            Assert.AreEqual(fit * ArPlacement.MinZoom, v, 1e-15);
            Assert.Greater(ArPlacement.ApparentSpan(SizeX, v), 0.2, "still a hand-sized object");
            Assert.IsTrue(ArPlacement.AtLimit(v, fit, closer: false));
            Assert.IsFalse(ArPlacement.AtLimit(v, fit, closer: true), "+ must still be offered");
        }

        [Test]
        public void ZoomingInForeverStopsBeforeTheMapSwallowsTheRoom()
        {
            double fit = ArPlacement.FitScale(SizeX, SizeZ);
            double v = fit;
            for (int i = 0; i < 50; i++) v = ArPlacement.Zoom(v, fit, closer: true);

            Assert.AreEqual(fit * ArPlacement.MaxZoom, v, 1e-15);
            Assert.IsTrue(ArPlacement.AtLimit(v, fit, closer: true));
            Assert.IsFalse(ArPlacement.AtLimit(v, fit, closer: false));
        }

        [Test]
        public void ZoomSurvivesNonsense()
        {
            double fit = ArPlacement.FitScale(SizeX, SizeZ);
            Assert.AreEqual(fit, ArPlacement.Zoom(0, fit, true), 1e-15);
            Assert.AreEqual(fit, ArPlacement.Zoom(double.NaN, fit, false), 1e-15);
            Assert.IsTrue(ArPlacement.AtLimit(1, 0, true), "no map, no zoom");
        }

        // ── clipping ─────────────────────────────────────────────────────────────

        [Test]
        public void TheWholeSiteFitsBetweenTheClipPlanes()
        {
            // At AR distance a 340-unit site sits ~430 units from the eye — a default 1000-unit far
            // plane would cut its back half away, and the symptom (a map that ends in mid-water)
            // reads as a loading bug rather than a camera one.
            foreach (double span in new double[] { 20, 340, 900 })
            {
                double s = ArPlacement.FitScale(span, span);
                ArPlacement.Clipping(span, s, out double near, out double far);
                double dist = ArPlacement.Distance / s;

                Assert.Less(near, dist - span * 0.5, $"span {span}: the near edge is clipped");
                Assert.Greater(far, dist + span * 0.5, $"span {span}: the far edge is clipped");
                Assert.Greater(near, 0, "a zero near plane wrecks depth precision");
                Assert.Less(far / near, 1e6, $"span {span}: depth range this wide z-fights");
            }
        }
    }
}
