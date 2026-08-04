using System.Collections.Generic;
using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// "รูคือรู" — a hole is a hole.
    ///
    /// The reef module the user flew into twice is an open cube frame, and the app made it a brick.
    /// Everything that can put the hole back in the wrong place — or take it away again — is a pure
    /// decision, so it is pinned here rather than found on a phone: the normalised→world mapping,
    /// non-uniform scale, the yaw every placed item carries, a file that is missing or malformed,
    /// and a box list that would otherwise multiply the per-frame collision cost by sixty.
    ///
    /// The invariant behind most of these: NO input to this system may make a solid object
    /// passable. A bad file, a stingy budget and an absent file must all land on exactly the
    /// single box the object has today.
    /// </summary>
    public class SolidBoxesTests
    {
        private const double Eps = 1e-9;

        // A unit cube frame in 0..1 model space: four vertical corner posts, hole down the middle.
        private static string FrameJson(string bbox = "\"min\":[-1,-1,-1], \"max\":[1,1,1]")
            => "{ \"v\":1, \"grid\":[32,16,32], \"bbox\":{" + bbox + "}, \"boxes\":[" +
               "[0,0,0, 0.2,1,0.2]," +
               "[0.8,0,0, 1,1,0.2]," +
               "[0,0,0.8, 0.2,1,1]," +
               "[0.8,0,0.8, 1,1,1]" +
               "] }";

        private static Quat Yaw(double degrees)
            => Quat.FromAxisAngle(new Vec3(0, 1, 0), degrees * System.Math.PI / 180.0);

        private static SolidBoxes.Box Unit(double half = 1.0)
            => SolidBoxes.Box.FromMinMax(-half, -half, -half, half, half, half);

        // ── the URL beside the model ──────────────────────────────────────────────

        [Test]
        public void UrlFor_SitsBesideTheGlb()
        {
            Assert.AreEqual("https://siamdive-cdn.b-cdn.net/models/xr/art_1330_xr0.solids.json",
                            SolidBoxes.UrlFor("https://siamdive-cdn.b-cdn.net/models/xr/art_1330_xr0.glb"));
        }

        [Test]
        public void UrlFor_KeepsAQueryString()
        {
            Assert.AreEqual("https://cdn/x/a.solids.json?v=3", SolidBoxes.UrlFor("https://cdn/x/a.glb?v=3"));
        }

        [Test]
        public void UrlFor_RefusesAnythingThatIsNotAGlb()
        {
            Assert.IsNull(SolidBoxes.UrlFor(null));
            Assert.IsNull(SolidBoxes.UrlFor(""));
            Assert.IsNull(SolidBoxes.UrlFor("https://cdn/x/a.gltf"),
                          "asking the CDN for a hull we cannot name is a 404 per item per map");
        }

        // ── reading the file ──────────────────────────────────────────────────────

        [Test]
        public void Parse_ReadsTheContract()
        {
            SolidBoxes.Model m = SolidBoxes.Parse(FrameJson());
            Assert.IsNotNull(m);
            Assert.AreEqual(1, m.Version);
            Assert.AreEqual(4, m.Boxes.Length);
            Assert.AreEqual(-1, m.BboxMin.X, Eps);
            Assert.AreEqual(1, m.BboxMax.Z, Eps);
            Assert.AreEqual(0.2, m.Boxes[0].Max.X, Eps);
        }

        [Test]
        public void Parse_AnAbsentFileIsNotAnError()
        {
            // Most models will never have one. "No hull" and "no file" must be the same answer.
            Assert.IsNull(SolidBoxes.Parse(null));
            Assert.IsNull(SolidBoxes.Parse(""));
            Assert.IsNull(SolidBoxes.Parse("   "));
        }

        [Test]
        public void Parse_AnEmptyBoxListReadsAsEmpty()
        {
            SolidBoxes.Model m = SolidBoxes.Parse(
                "{\"v\":1,\"bbox\":{\"min\":[0,0,0],\"max\":[1,1,1]},\"boxes\":[]}");
            Assert.IsNotNull(m);
            Assert.IsTrue(m.IsEmpty, "readable but nothing solid — the caller keeps its single AABB");
            Assert.IsNull(SolidBoxes.ToFrame(m, Unit(), new Vec3(1, 1, 1)));
        }

        [Test]
        public void Parse_MalformedJsonIsRefusedNotThrown()
        {
            Assert.IsNull(SolidBoxes.Parse("{ this is not json"));
            Assert.IsNull(SolidBoxes.Parse("[1,2,3]"));
            Assert.IsNull(SolidBoxes.Parse("null"));
            Assert.IsNull(SolidBoxes.Parse("{\"v\":1}"), "no bbox, no mapping");
            // JToken's string indexer throws on an array, and this file comes off a CDN onto an
            // async thread where an exception is a silent dead end.
            Assert.IsNull(SolidBoxes.Parse("{\"v\":1,\"bbox\":[],\"boxes\":[]}"));
            Assert.IsNull(SolidBoxes.Parse(
                "{\"v\":1,\"bbox\":{\"min\":[0,0,0],\"max\":[1,1,1]},\"boxes\":{}}"));
        }

        [Test]
        public void Parse_RefusesAVersionItDoesNotKnow()
        {
            string v2 = FrameJson().Replace("\"v\":1", "\"v\":2");
            Assert.IsNull(SolidBoxes.Parse(v2),
                          "a format we have not read must fall back, not be guessed at");
        }

        [Test]
        public void Parse_RefusesADegenerateBbox()
        {
            Assert.IsNull(SolidBoxes.Parse(FrameJson("\"min\":[0,0,0], \"max\":[0,1,1]")),
                          "normalised-inside-a-flat-bbox means nothing");
        }

        [Test]
        public void Parse_RefusesARowThatIsNotSixNumbers()
        {
            Assert.IsNull(SolidBoxes.Parse(
                "{\"v\":1,\"bbox\":{\"min\":[0,0,0],\"max\":[1,1,1]},\"boxes\":[[0,0,0,1,1]]}"));
            Assert.IsNull(SolidBoxes.Parse(
                "{\"v\":1,\"bbox\":{\"min\":[0,0,0],\"max\":[1,1,1]},\"boxes\":[[0,0,0,1,1,\"x\"]]}"));
        }

        [Test]
        public void Parse_TidiesUpReversedAndOverflowingNumbers()
        {
            SolidBoxes.Model m = SolidBoxes.Parse(
                "{\"v\":1,\"bbox\":{\"min\":[0,0,0],\"max\":[1,1,1]}," +
                "\"boxes\":[[0.9,1.0000001,0.9, 0.1,-0.0000001,0.1]]}");
            Assert.IsNotNull(m);
            Assert.AreEqual(1, m.Boxes.Length);
            Assert.AreEqual(0.1, m.Boxes[0].Min.X, 1e-7);
            Assert.AreEqual(0.9, m.Boxes[0].Max.X, 1e-7);
            Assert.AreEqual(0.0, m.Boxes[0].Min.Y, 1e-7);
            Assert.AreEqual(1.0, m.Boxes[0].Max.Y, 1e-7);
        }

        [Test]
        public void Parse_DropsABoxWithNoThickness()
        {
            // Grown by DroneFlight.CamRadius a zero-volume box is a 6.4-unit invisible ball.
            SolidBoxes.Model m = SolidBoxes.Parse(
                "{\"v\":1,\"bbox\":{\"min\":[0,0,0],\"max\":[1,1,1]}," +
                "\"boxes\":[[0.5,0.5,0.5, 0.5,0.5,0.5],[0,0,0, 1,1,1]]}");
            Assert.IsNotNull(m);
            Assert.AreEqual(1, m.Boxes.Length);
        }

        [Test]
        public void Parse_RefusesABoxListThatWouldExplodeTheObstacleCount()
        {
            // The map that started this has 494 objects built from 10 assets. One generator bug
            // that emits 500 boxes for one of them would be 500 × 394 boxes tested EVERY FRAME.
            // Refusing the file leaves the object exactly as solid as it is today.
            var sb = new System.Text.StringBuilder();
            sb.Append("{\"v\":1,\"bbox\":{\"min\":[0,0,0],\"max\":[1,1,1]},\"boxes\":[");
            for (int i = 0; i < 500; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("[0,0,0,0.1,0.1,0.1]");
            }
            sb.Append("]}");

            Assert.IsNull(SolidBoxes.Parse(sb.ToString()));
            Assert.Less(SolidBoxes.MaxBoxesPerModel, 500);
            Assert.GreaterOrEqual(SolidBoxes.MaxBoxesPerModel, 64, "the contract's own ceiling must fit");
        }

        // ── normalised → the object's own frame ───────────────────────────────────

        [Test]
        public void ToFrame_MapsTheNormalisedBoxOntoTheObjectsOwnBounds()
        {
            SolidBoxes.Model m = SolidBoxes.Parse(
                "{\"v\":1,\"bbox\":{\"min\":[-1,-1,-1],\"max\":[1,1,1]}," +
                "\"boxes\":[[0,0,0, 0.25,0.5,1]]}");

            // fit = the object's content, 4 wide, 10 tall, 2 deep, sitting on its own pivot.
            SolidBoxes.Box fit = SolidBoxes.Box.FromMinMax(-2, 0, -1, 2, 10, 1);
            SolidBoxes.Box[] w = SolidBoxes.ToFrame(m, fit, new Vec3(1, 1, 1), SolidBoxes.Mirror.None);

            Assert.AreEqual(1, w.Length);
            Assert.AreEqual(-2, w[0].Min.X, Eps);
            Assert.AreEqual(-1, w[0].Max.X, Eps);   // 0.25 of 4 units, from −2
            Assert.AreEqual(0, w[0].Min.Y, Eps);
            Assert.AreEqual(5, w[0].Max.Y, Eps);    // 0.5 of 10
            Assert.AreEqual(-1, w[0].Min.Z, Eps);
            Assert.AreEqual(1, w[0].Max.Z, Eps);    // the full depth
        }

        [Test]
        public void ToFrame_DoesNotMoveWithTheObject_ThePlacementDoes()
        {
            // The boxes are measured from the object's own pivot, so moving the object changes
            // Group.Origin and nothing else — which is also why a hull no longer has to be rebuilt
            // when something is dragged around the map.
            SolidBoxes.Model m = SolidBoxes.Parse(FrameJson());
            SolidBoxes.Box[] w = SolidBoxes.ToFrame(m, Unit(), new Vec3(1, 1, 1), SolidBoxes.Mirror.None);

            // First post: 0..0.2 of a −1..1 box = −1 .. −0.6, and it stays there.
            Assert.AreEqual(-1, w[0].Min.X, Eps);
            Assert.AreEqual(-0.6, w[0].Max.X, 1e-9);

            var origin = new Vec3(100, 7, -40);
            Vec3 corner = SolidBoxes.FrameToWorld(origin, Quat.Identity,
                                                  new Vec3(w[0].Min.X, w[0].Min.Y, w[0].Min.Z));
            Assert.AreEqual(99, corner.X, Eps);
            Assert.AreEqual(6, corner.Y, Eps);
            Assert.AreEqual(-41, corner.Z, Eps);
        }

        [Test]
        public void ToFrame_HonoursNonUniformScale()
        {
            // Baked in here rather than divided out of the camera radius later: T-13 places a wreck
            // at 37.7 × 100.9 × 37.7, and a radius that is 3.2 on one axis and 1.2 on another is a
            // sphere test nobody wrote.
            SolidBoxes.Model m = SolidBoxes.Parse(
                "{\"v\":1,\"bbox\":{\"min\":[-1,-1,-1],\"max\":[1,1,1]}," +
                "\"boxes\":[[0,0,0, 1,0.5,1]]}");

            SolidBoxes.Box[] w = SolidBoxes.ToFrame(m, Unit(), new Vec3(3, 10, 0.5),
                                                    SolidBoxes.Mirror.None);

            Assert.AreEqual(-3, w[0].Min.X, Eps);
            Assert.AreEqual(3, w[0].Max.X, Eps);
            Assert.AreEqual(-10, w[0].Min.Y, Eps);
            Assert.AreEqual(0, w[0].Max.Y, Eps);     // 0.5 of −1..1 is 0, × 10 is still 0
            Assert.AreEqual(-0.5, w[0].Min.Z, Eps);
            Assert.AreEqual(0.5, w[0].Max.Z, Eps);
        }

        [Test]
        public void ToFrame_ANegativeScaleMirrorsTheBoxRatherThanInvertingIt()
        {
            // A mirrored placement is legal and rare; a box with Min > Max is neither, and every
            // test downstream (penetration depths) would silently read as "no collision".
            SolidBoxes.Model m = SolidBoxes.Parse(
                "{\"v\":1,\"bbox\":{\"min\":[-1,-1,-1],\"max\":[1,1,1]}," +
                "\"boxes\":[[0,0,0, 0.25,1,1]]}");

            SolidBoxes.Box[] w = SolidBoxes.ToFrame(m, Unit(), new Vec3(-2, 1, 1),
                                                    SolidBoxes.Mirror.None);
            Assert.Less(w[0].Min.X, w[0].Max.X);
            Assert.AreEqual(1, w[0].Min.X, Eps);     // −1 .. −0.5, mirrored and doubled
            Assert.AreEqual(2, w[0].Max.X, Eps);
        }

        [Test]
        public void FrameToWorld_RotatesAboutY_ExactlyAtAQuarterTurn()
        {
            // Items are placed with a yaw, so this is the normal case, not an edge case.
            SolidBoxes.Model m = SolidBoxes.Parse(
                "{\"v\":1,\"bbox\":{\"min\":[-1,-1,-1],\"max\":[1,1,1]}," +
                "\"boxes\":[[0,0,0, 0.25,1,1]]}");

            SolidBoxes.Box fit = SolidBoxes.Box.FromMinMax(-2, 0, -1, 2, 4, 1);
            SolidBoxes.Box[] w = SolidBoxes.ToFrame(m, fit, new Vec3(1, 1, 1), SolidBoxes.Mirror.None);
            // In the frame that box is x −2..−1, y 0..4, z −1..1.
            // Unity yaw +90° sends (x,y,z) → (z,y,−x): the −2 corner lands at z = 2.
            Vec3 lo = SolidBoxes.FrameToWorld(default, Yaw(90),
                                              new Vec3(w[0].Min.X, w[0].Min.Y, w[0].Min.Z));
            Vec3 hi = SolidBoxes.FrameToWorld(default, Yaw(90),
                                              new Vec3(w[0].Max.X, w[0].Max.Y, w[0].Max.Z));
            Assert.AreEqual(-1, lo.X, 1e-9);
            Assert.AreEqual(0, lo.Y, 1e-9);
            Assert.AreEqual(2, lo.Z, 1e-9);
            Assert.AreEqual(1, hi.X, 1e-9);
            Assert.AreEqual(4, hi.Y, 1e-9);
            Assert.AreEqual(1, hi.Z, 1e-9);
        }

        [Test]
        public void ToFrame_AYawedHullIsTheSameHull()
        {
            // 🔴 The bug, in its smallest form. This used to assert the opposite — that a turned
            // slab comes back as a WIDER box, "fat but never leaky". It is leaky in the only sense
            // that matters to a diver: a slab 0.2 thick turned 45° became a box 1.4 thick, and 96
            // of those welded a wreck shut (see TiltedHullTests for the real one).
            //
            // The rotation is no longer applied to the boxes at all, so there is nothing to inflate.
            SolidBoxes.Model m = SolidBoxes.Parse(
                "{\"v\":1,\"bbox\":{\"min\":[-1,-1,-1],\"max\":[1,1,1]}," +
                "\"boxes\":[[0,0,0.4, 1,1,0.6]]}");   // a thin slab across X

            SolidBoxes.Box[] boxes = SolidBoxes.ToFrame(m, Unit(), new Vec3(1, 1, 1),
                                                        SolidBoxes.Mirror.None);
            Assert.AreEqual(0.4, boxes[0].Max.Z - boxes[0].Min.Z, 1e-9);

            // …and turning the DIVER into the frame instead is exact: a point 0.5 off the slab's
            // face along the object's own Z is 0.5 off it however the object is turned.
            var offFace = new Vec3(0, 0, boxes[0].Max.Z + 0.5);
            foreach (double deg in new[] { 0.0, 37.0, 45.0, 90.0, 173.0 })
            {
                Vec3 world = SolidBoxes.FrameToWorld(new Vec3(10, -3, 7), Yaw(deg), offFace);
                Vec3 back = SolidBoxes.WorldToFrame(new Vec3(10, -3, 7), Yaw(deg), world);
                Assert.AreEqual(offFace.Z, back.Z, 1e-9, "the frame round trip must be exact");
                Assert.Greater(back.Z, boxes[0].Max.Z, "the water beside the slab stays water");
            }
        }

        [Test]
        public void ToFrame_MirrorReflectsInsideTheObjectAndNeverOutsideIt()
        {
            // The one unverifiable sign in this file (glTFast's handedness flip). Both mappings are
            // pinned so switching SolidBoxes.Mirror.Importer is a one-line change with a known
            // result, and so nobody has to re-derive it from a photograph.
            SolidBoxes.Model m = SolidBoxes.Parse(
                "{\"v\":1,\"bbox\":{\"min\":[-1,-1,-1],\"max\":[1,1,1]}," +
                "\"boxes\":[[0,0,0, 0.25,1,1]]}");

            SolidBoxes.Box[] plain = SolidBoxes.ToFrame(m, Unit(), new Vec3(1, 1, 1),
                                                        SolidBoxes.Mirror.None);
            SolidBoxes.Box[] flipX = SolidBoxes.ToFrame(m, Unit(), new Vec3(1, 1, 1),
                                                        SolidBoxes.Mirror.FlipX);

            Assert.AreEqual(-1, plain[0].Min.X, Eps);
            Assert.AreEqual(-0.5, plain[0].Max.X, Eps);
            Assert.AreEqual(0.5, flipX[0].Min.X, Eps);   // the mirror image, same object
            Assert.AreEqual(1, flipX[0].Max.X, Eps);

            // A symmetric hull — an open cube frame, the shape actually reported — does not care.
            SolidBoxes.Model frame = SolidBoxes.Parse(FrameJson());
            SolidBoxes.Box[] a = SolidBoxes.ToFrame(frame, Unit(), new Vec3(1, 1, 1),
                                                    SolidBoxes.Mirror.None);
            SolidBoxes.Box[] b = SolidBoxes.ToFrame(frame, Unit(), new Vec3(1, 1, 1),
                                                    SolidBoxes.Mirror.FlipX);
            Assert.AreEqual(a.Length, b.Length);
            for (int i = 0; i < a.Length; i++)
            {
                bool matched = false;
                for (int j = 0; j < b.Length && !matched; j++) matched = Same(a[i], b[j], 1e-9);
                Assert.IsTrue(matched, "post " + i + " has no partner in the mirrored hull");
            }
        }

        /// <summary>
        /// A hull is fitted to the model as it is ACTUALLY STANDING in the map, and since
        /// SceneBuilder.FixImportedAxes turns every scenery model into the web's Z mirror (the
        /// same mirror WebCoord.PositionToUnity puts its item transform in), a hull that is
        /// still in glTFast's raw X mirror is a hull for the model's reflection: solid where the
        /// wreck has a doorway and open where it has a wall.
        ///
        /// So the default must follow the placement, and these two must move together. If the
        /// day comes that FixImportedAxes goes away, this is the test that says so out loud.
        /// </summary>
        [Test]
        public void TheDefaultMirror_IsTheOneAPlacedModelIsActuallyIn()
        {
            Assert.IsFalse(SolidBoxes.Mirror.Placed.X, "a placed scenery model is not X-mirrored");
            Assert.IsTrue(SolidBoxes.Mirror.Placed.Z, "it is Z-mirrored, like its item transform");
            Assert.IsFalse(SolidBoxes.Mirror.Placed.Y);

            // WebCoord agrees: the placement flips Z and only Z.
            Vec3 u = WebCoord.PositionToUnity(new Vec3(1, 2, 3));
            Assert.AreEqual(1, u.X, Eps);
            Assert.AreEqual(2, u.Y, Eps);
            Assert.AreEqual(-3, u.Z, Eps);

            // ...and the no-argument ToFrame is that one, not the raw importer mirror.
            SolidBoxes.Model m = SolidBoxes.Parse(
                "{\"v\":1,\"bbox\":{\"min\":[-1,-1,-1],\"max\":[1,1,1]}," +
                "\"boxes\":[[0,0,0.2, 0.25,1,1]]}");
            SolidBoxes.Box[] byDefault = SolidBoxes.ToFrame(m, Unit(), new Vec3(1, 1, 1));
            SolidBoxes.Box[] placed = SolidBoxes.ToFrame(m, Unit(), new Vec3(1, 1, 1),
                                                          SolidBoxes.Mirror.Placed);
            SolidBoxes.Box[] importer = SolidBoxes.ToFrame(m, Unit(), new Vec3(1, 1, 1),
                                                            SolidBoxes.Mirror.Importer);
            Assert.IsTrue(Same(byDefault[0], placed[0], 1e-9), "the default must be Mirror.Placed");
            Assert.IsFalse(Same(placed[0], importer[0], 1e-9),
                           "sanity: this fixture can tell the two mirrors apart");
        }

        private static bool Same(SolidBoxes.Box x, SolidBoxes.Box y, double tol)
            => System.Math.Abs(x.Min.X - y.Min.X) < tol && System.Math.Abs(x.Max.X - y.Max.X) < tol
            && System.Math.Abs(x.Min.Y - y.Min.Y) < tol && System.Math.Abs(x.Max.Y - y.Max.Y) < tol
            && System.Math.Abs(x.Min.Z - y.Min.Z) < tol && System.Math.Abs(x.Max.Z - y.Max.Z) < tol;

        [Test]
        public void ToFrame_LeavesAHoleInTheMiddleOfTheFrame()
        {
            // The whole point, stated as the user states it: the centre of an open cube frame is
            // not inside any solid box — and stays that way at 37°, which is where the old
            // flatten-to-world-axes step lost it.
            SolidBoxes.Model m = SolidBoxes.Parse(FrameJson());
            SolidBoxes.Box[] w = SolidBoxes.ToFrame(m, Unit(50), new Vec3(1, 1, 1),
                                                    SolidBoxes.Mirror.None);

            var origin = new Vec3(12, -4, 30);
            Quat rot = Yaw(37);
            // The diver hovering at the middle of the module, seen from the module's own frame.
            Vec3 centre = SolidBoxes.WorldToFrame(origin, rot, origin);
            for (int i = 0; i < w.Length; i++)
                Assert.Greater(SolidBoxes.DistanceSq(w[i], centre), 0.0,
                               "box " + i + " swallows the middle of the frame");
        }

        // ── does this hull belong to this object ──────────────────────────────────

        [Test]
        public void FitsBbox_AcceptsTheModelItWasMadeFor()
        {
            SolidBoxes.Model m = SolidBoxes.Parse(FrameJson("\"min\":[-1,-2,-1], \"max\":[1,2,1]"));
            Assert.IsTrue(SolidBoxes.FitsBbox(m, SolidBoxes.Box.FromMinMax(-1, 0, -1, 1, 4, 1)),
                          "grounding moves the content, it does not resize it");
        }

        [Test]
        public void FitsBbox_RejectsAHullFromADifferentAsset()
        {
            SolidBoxes.Model m = SolidBoxes.Parse(FrameJson("\"min\":[-1,-1,-1], \"max\":[1,1,1]"));
            Assert.IsFalse(SolidBoxes.FitsBbox(m, SolidBoxes.Box.FromMinMax(-1, 0, -1, 1, 40, 1)),
                           "a hull stretched 20× onto the wrong model reads as a broken map");
            Assert.IsFalse(SolidBoxes.FitsBbox(null, Unit()));
        }

        // ── the budget ────────────────────────────────────────────────────────────

        private static SolidBoxes.Group GroupAt(double x, int fineCount)
        {
            SolidBoxes.Box coarse = SolidBoxes.Box.FromMinMax(x - 1, 0, -1, x + 1, 2, 1);
            if (fineCount <= 0) return new SolidBoxes.Group { Coarse = coarse, Fine = null };

            var fine = new SolidBoxes.Box[fineCount];
            for (int i = 0; i < fineCount; i++)
                fine[i] = SolidBoxes.Box.FromMinMax(x - 1, i * 0.01, -1, x + 1, i * 0.01 + 0.005, 1);
            return new SolidBoxes.Group { Coarse = coarse, Fine = fine };
        }

        [Test]
        public void Select_WithNoHullsIsExactlyTodaysBehaviour()
        {
            // The fallback is the contract: one box per object, in order, always solid.
            var groups = new List<SolidBoxes.Group>();
            for (int i = 0; i < 494; i++) groups.Add(GroupAt(i * 10, 0));

            var into = new List<SolidBoxes.Solid>();
            int detailed = SolidBoxes.Select(groups, new Vec3(0, 0, 0), into);

            Assert.AreEqual(0, detailed);
            Assert.AreEqual(494, into.Count);
            Assert.AreEqual(494, SolidBoxes.BoxCount(into));
            for (int i = 0; i < groups.Count; i++)
            {
                Assert.AreEqual(groups[i].Coarse.Min.X, into[i].Bound.Min.X, Eps, "order must be stable");
                Assert.IsNull(into[i].Boxes, "no hull means the object IS its world box");
                Assert.AreEqual(i, into[i].Index);
            }
        }

        [Test]
        public void Select_GivesTheNearestObjectsTheirHullAndTheRestTheirBox()
        {
            var groups = new List<SolidBoxes.Group>();
            for (int i = 0; i < 20; i++) groups.Add(GroupAt(i * 10, 4));   // 0, 10, 20 … 190

            var into = new List<SolidBoxes.Solid>();
            // Radius 35 reaches the objects at 0/10/20/30 (their boxes are ±1 wide).
            int detailed = SolidBoxes.Select(groups, new Vec3(0, 1, 0), into, 35.0, 1000);

            Assert.AreEqual(4, detailed);
            Assert.AreEqual(20, into.Count, "one entry per object, hull or not");
            Assert.AreEqual(4 * 4 + 16, SolidBoxes.BoxCount(into), "4 hulls of 4 boxes + 16 single boxes");
        }

        [Test]
        public void Select_CarriesTheObjectsFrameWithItsHull()
        {
            // Without the placement the boxes mean nothing — this is the field whose absence made
            // a tilted wreck solid, so its presence is pinned.
            var groups = new List<SolidBoxes.Group>
            {
                new SolidBoxes.Group
                {
                    Coarse = Unit(10),
                    Fine = new[] { Unit(2) },
                    Origin = new Vec3(5, 6, 7),
                    Rot = Yaw(99.5),
                },
            };
            var into = new List<SolidBoxes.Solid>();
            Assert.AreEqual(1, SolidBoxes.Select(groups, new Vec3(0, 0, 0), into, 100.0, 100));

            Assert.IsNotNull(into[0].Boxes);
            Assert.AreEqual(5, into[0].Origin.X, Eps);
            Assert.AreEqual(7, into[0].Origin.Z, Eps);
            Assert.AreEqual(Yaw(99.5).Y, into[0].Rot.Y, 1e-12);
        }

        [Test]
        public void Select_ADroppedHullFallsBackToWorldAxes()
        {
            // An object that misses the budget carries its world AABB, so its frame must NOT come
            // with it — a leftover rotation would turn the fallback box into a wrong box.
            var groups = new List<SolidBoxes.Group>
            {
                new SolidBoxes.Group
                {
                    Coarse = Unit(10), Fine = new[] { Unit(2) },
                    Origin = new Vec3(5, 6, 7), Rot = Yaw(99.5),
                },
            };
            var into = new List<SolidBoxes.Solid>();
            Assert.AreEqual(0, SolidBoxes.Select(groups, new Vec3(0, 0, 0), into, 100.0, 0));

            Assert.IsNull(into[0].Boxes);
            Assert.AreEqual(0, into[0].Origin.X, Eps);
            Assert.AreEqual(0, into[0].Origin.Y, Eps);
            Assert.AreEqual(0, into[0].Origin.Z, Eps);
            Assert.AreEqual(Quat.Identity.W, into[0].Rot.W, Eps);
        }

        [Test]
        public void Select_NeverLetsTheBudgetMakeAnObjectPassable()
        {
            var groups = new List<SolidBoxes.Group>();
            for (int i = 0; i < 400; i++) groups.Add(GroupAt(i * 2, 64));   // all in range, all fat

            var into = new List<SolidBoxes.Solid>();
            int detailed = SolidBoxes.Select(groups, new Vec3(0, 1, 0), into, 100000.0,
                                             SolidBoxes.DiverBoxBudget);

            Assert.AreEqual(SolidBoxes.DiverBoxBudget / 64, detailed);
            Assert.AreEqual(groups.Count, into.Count, "every object is still in the list");
            Assert.LessOrEqual(SolidBoxes.BoxCount(into), SolidBoxes.DiverBoxBudget + groups.Count,
                               "the guard is the point: 400 × 64 = 25,600 boxes per frame");
            // Every object still contributes something solid.
            int accounted = detailed * 64 + (groups.Count - detailed);
            Assert.AreEqual(accounted, SolidBoxes.BoxCount(into));
        }

        [Test]
        public void Select_TakesWholeHullsOnly()
        {
            // Half a hull is a wall across the middle of a doorway — worse than the brick, because
            // it is invisible.
            var groups = new List<SolidBoxes.Group> { GroupAt(0, 10), GroupAt(5, 10) };
            var into = new List<SolidBoxes.Solid>();
            int detailed = SolidBoxes.Select(groups, new Vec3(0, 1, 0), into, 100.0, 15);

            Assert.AreEqual(1, detailed, "only one hull fits in 15 boxes");
            Assert.AreEqual(11, SolidBoxes.BoxCount(into), "10 hull boxes + the other object's single box");
        }

        [Test]
        public void Select_IsStableForIdenticalModulesAtTheSameDistance()
        {
            // T-13 is 394 copies of one frame: ties are the normal case, not the edge case, and a
            // set that reshuffles every refresh would pop holes open and shut while you hover.
            var groups = new List<SolidBoxes.Group> { GroupAt(-10, 8), GroupAt(10, 8), GroupAt(-10, 8) };
            var a = new List<SolidBoxes.Solid>();
            var b = new List<SolidBoxes.Solid>();
            SolidBoxes.Select(groups, new Vec3(0, 1, 0), a, 100.0, 16);
            SolidBoxes.Select(groups, new Vec3(0, 1, 0), b, 100.0, 16);

            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].Bound.ToString(), b[i].Bound.ToString());
                Assert.AreEqual(a[i].Boxes == null, b[i].Boxes == null);
            }
        }

        [Test]
        public void Select_SurvivesNothingToChooseFrom()
        {
            var into = new List<SolidBoxes.Solid> { new SolidBoxes.Solid { Bound = Unit() } };
            Assert.AreEqual(0, SolidBoxes.Select(null, new Vec3(0, 0, 0), into));
            Assert.AreEqual(0, into.Count, "a stale pick left behind would collide with thin air");
            Assert.AreEqual(0, SolidBoxes.Select(new List<SolidBoxes.Group>(), new Vec3(0, 0, 0), into));
        }

        [Test]
        public void DistanceSq_IsZeroInsideTheBox()
        {
            SolidBoxes.Box b = SolidBoxes.Box.FromMinMax(-1, -1, -1, 1, 1, 1);
            Assert.AreEqual(0, SolidBoxes.DistanceSq(b, new Vec3(0, 0, 0)), Eps);
            Assert.AreEqual(0, SolidBoxes.DistanceSq(b, new Vec3(1, 1, 1)), Eps);
            Assert.AreEqual(9, SolidBoxes.DistanceSq(b, new Vec3(4, 0, 0)), Eps);
            Assert.AreEqual(18, SolidBoxes.DistanceSq(b, new Vec3(4, 4, 0)), Eps);
        }

        [Test]
        public void TheBudgetStaysInTheSameOrderAsTodaysCost()
        {
            // A reviewer raising DiverBoxBudget should have to change this line too.
            Assert.LessOrEqual(SolidBoxes.DiverBoxBudget, 4096,
                               "DroneFlight.Step walks every box, every frame, on a phone");
            Assert.Less(SolidBoxes.RefreshMove, SolidBoxes.DetailRadius,
                        "an object must enter the detail radius before the diver can reach it");
            Assert.Less(SolidBoxes.RefreshMove * 4, SolidBoxes.DetailRadius,
                        "with margin: the drone does 30 u/s and CI runs at 3 fps");
        }
    }
}
