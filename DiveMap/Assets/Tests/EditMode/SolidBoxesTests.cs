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
            Assert.IsNull(SolidBoxes.ToWorld(m, Unit(), default, Quat.Identity, new Vec3(1, 1, 1)));
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

        // ── normalised → world ────────────────────────────────────────────────────

        [Test]
        public void ToWorld_MapsTheNormalisedBoxOntoTheObjectsOwnBounds()
        {
            SolidBoxes.Model m = SolidBoxes.Parse(
                "{\"v\":1,\"bbox\":{\"min\":[-1,-1,-1],\"max\":[1,1,1]}," +
                "\"boxes\":[[0,0,0, 0.25,0.5,1]]}");

            // fit = the object's content, 4 wide, 10 tall, 2 deep, sitting on its own pivot.
            SolidBoxes.Box fit = SolidBoxes.Box.FromMinMax(-2, 0, -1, 2, 10, 1);
            SolidBoxes.Box[] w = SolidBoxes.ToWorld(m, fit, new Vec3(0, 0, 0), Quat.Identity,
                                                    new Vec3(1, 1, 1), SolidBoxes.Mirror.None);

            Assert.AreEqual(1, w.Length);
            Assert.AreEqual(-2, w[0].Min.X, Eps);
            Assert.AreEqual(-1, w[0].Max.X, Eps);   // 0.25 of 4 units, from −2
            Assert.AreEqual(0, w[0].Min.Y, Eps);
            Assert.AreEqual(5, w[0].Max.Y, Eps);    // 0.5 of 10
            Assert.AreEqual(-1, w[0].Min.Z, Eps);
            Assert.AreEqual(1, w[0].Max.Z, Eps);    // the full depth
        }

        [Test]
        public void ToWorld_MovesWithTheObject()
        {
            SolidBoxes.Model m = SolidBoxes.Parse(FrameJson());
            SolidBoxes.Box[] w = SolidBoxes.ToWorld(m, Unit(), new Vec3(100, 7, -40), Quat.Identity,
                                                    new Vec3(1, 1, 1), SolidBoxes.Mirror.None);
            // First post: 0..0.2 of a −1..1 box = −1 .. −0.6, then translated.
            Assert.AreEqual(99, w[0].Min.X, Eps);
            Assert.AreEqual(99.4, w[0].Max.X, 1e-9);
            Assert.AreEqual(6, w[0].Min.Y, Eps);
            Assert.AreEqual(-41, w[0].Min.Z, Eps);
        }

        [Test]
        public void ToWorld_HonoursNonUniformScale()
        {
            SolidBoxes.Model m = SolidBoxes.Parse(
                "{\"v\":1,\"bbox\":{\"min\":[-1,-1,-1],\"max\":[1,1,1]}," +
                "\"boxes\":[[0,0,0, 1,0.5,1]]}");

            SolidBoxes.Box[] w = SolidBoxes.ToWorld(m, Unit(), new Vec3(0, 0, 0), Quat.Identity,
                                                    new Vec3(3, 10, 0.5), SolidBoxes.Mirror.None);

            Assert.AreEqual(-3, w[0].Min.X, Eps);
            Assert.AreEqual(3, w[0].Max.X, Eps);
            Assert.AreEqual(-10, w[0].Min.Y, Eps);
            Assert.AreEqual(0, w[0].Max.Y, Eps);     // 0.5 of −1..1 is 0, × 10 is still 0
            Assert.AreEqual(-0.5, w[0].Min.Z, Eps);
            Assert.AreEqual(0.5, w[0].Max.Z, Eps);
        }

        [Test]
        public void ToWorld_RotatesAboutY_ExactlyAtAQuarterTurn()
        {
            // Items are placed with a yaw, so this is the normal case, not an edge case.
            SolidBoxes.Model m = SolidBoxes.Parse(
                "{\"v\":1,\"bbox\":{\"min\":[-1,-1,-1],\"max\":[1,1,1]}," +
                "\"boxes\":[[0,0,0, 0.25,1,1]]}");

            SolidBoxes.Box fit = SolidBoxes.Box.FromMinMax(-2, 0, -1, 2, 4, 1);
            // Unrotated that box is x −2..−1, y 0..4, z −1..1.
            // Unity yaw +90° sends (x,y,z) → (z,y,−x): x becomes −1..1, z becomes 1..2.
            SolidBoxes.Box[] w = SolidBoxes.ToWorld(m, fit, new Vec3(0, 0, 0), Yaw(90),
                                                    new Vec3(1, 1, 1), SolidBoxes.Mirror.None);

            Assert.AreEqual(-1, w[0].Min.X, 1e-9);
            Assert.AreEqual(1, w[0].Max.X, 1e-9);
            Assert.AreEqual(0, w[0].Min.Y, 1e-9);
            Assert.AreEqual(4, w[0].Max.Y, 1e-9);
            Assert.AreEqual(1, w[0].Min.Z, 1e-9);
            Assert.AreEqual(2, w[0].Max.Z, 1e-9);
        }

        [Test]
        public void ToWorld_AYawedHullIsFatNeverLeaky()
        {
            // DroneFlight only tests AABBs, so an off-axis part is stored as the box AROUND it.
            // The direction of that error is the whole safety argument: a fat box is the old
            // behaviour coming back for one part; a thin one is a diver inside the scenery.
            SolidBoxes.Model m = SolidBoxes.Parse(
                "{\"v\":1,\"bbox\":{\"min\":[-1,-1,-1],\"max\":[1,1,1]}," +
                "\"boxes\":[[0,0,0.4, 1,1,0.6]]}");   // a thin slab across X

            SolidBoxes.Box[] flat = SolidBoxes.ToWorld(m, Unit(), default, Quat.Identity,
                                                       new Vec3(1, 1, 1), SolidBoxes.Mirror.None);
            SolidBoxes.Box[] turned = SolidBoxes.ToWorld(m, Unit(), default, Yaw(45),
                                                        new Vec3(1, 1, 1), SolidBoxes.Mirror.None);

            double flatZ = flat[0].Max.Z - flat[0].Min.Z;
            double turnedZ = turned[0].Max.Z - turned[0].Min.Z;
            Assert.Greater(turnedZ, flatZ, "a 45° slab needs a wider axis-aligned box");
            // …and still inside the object it belongs to (√2 of the unit box's half-diagonal).
            Assert.LessOrEqual(turned[0].Max.X, 1.4143);
            Assert.GreaterOrEqual(turned[0].Min.X, -1.4143);
        }

        [Test]
        public void ToWorld_MirrorReflectsInsideTheObjectAndNeverOutsideIt()
        {
            // The one unverifiable sign in this file (glTFast's handedness flip). Both mappings are
            // pinned so switching SolidBoxes.Mirror.Importer is a one-line change with a known
            // result, and so nobody has to re-derive it from a photograph.
            SolidBoxes.Model m = SolidBoxes.Parse(
                "{\"v\":1,\"bbox\":{\"min\":[-1,-1,-1],\"max\":[1,1,1]}," +
                "\"boxes\":[[0,0,0, 0.25,1,1]]}");

            SolidBoxes.Box[] plain = SolidBoxes.ToWorld(m, Unit(), default, Quat.Identity,
                                                        new Vec3(1, 1, 1), SolidBoxes.Mirror.None);
            SolidBoxes.Box[] flipX = SolidBoxes.ToWorld(m, Unit(), default, Quat.Identity,
                                                        new Vec3(1, 1, 1), SolidBoxes.Mirror.FlipX);

            Assert.AreEqual(-1, plain[0].Min.X, Eps);
            Assert.AreEqual(-0.5, plain[0].Max.X, Eps);
            Assert.AreEqual(0.5, flipX[0].Min.X, Eps);   // the mirror image, same object
            Assert.AreEqual(1, flipX[0].Max.X, Eps);

            // A symmetric hull — an open cube frame, the shape actually reported — does not care.
            SolidBoxes.Model frame = SolidBoxes.Parse(FrameJson());
            SolidBoxes.Box[] a = SolidBoxes.ToWorld(frame, Unit(), default, Quat.Identity,
                                                    new Vec3(1, 1, 1), SolidBoxes.Mirror.None);
            SolidBoxes.Box[] b = SolidBoxes.ToWorld(frame, Unit(), default, Quat.Identity,
                                                    new Vec3(1, 1, 1), SolidBoxes.Mirror.FlipX);
            Assert.AreEqual(a.Length, b.Length);
            for (int i = 0; i < a.Length; i++)
            {
                bool matched = false;
                for (int j = 0; j < b.Length && !matched; j++) matched = Same(a[i], b[j], 1e-9);
                Assert.IsTrue(matched, "post " + i + " has no partner in the mirrored hull");
            }
        }

        private static bool Same(SolidBoxes.Box x, SolidBoxes.Box y, double tol)
            => System.Math.Abs(x.Min.X - y.Min.X) < tol && System.Math.Abs(x.Max.X - y.Max.X) < tol
            && System.Math.Abs(x.Min.Y - y.Min.Y) < tol && System.Math.Abs(x.Max.Y - y.Max.Y) < tol
            && System.Math.Abs(x.Min.Z - y.Min.Z) < tol && System.Math.Abs(x.Max.Z - y.Max.Z) < tol;

        [Test]
        public void ToWorld_LeavesAHoleInTheMiddleOfTheFrame()
        {
            // The whole point, stated as the user states it: the centre of an open cube frame is
            // not inside any solid box.
            SolidBoxes.Model m = SolidBoxes.Parse(FrameJson());
            SolidBoxes.Box[] w = SolidBoxes.ToWorld(m, Unit(50), new Vec3(0, 0, 0), Yaw(37),
                                                    new Vec3(1, 1, 1), SolidBoxes.Mirror.None);
            var centre = new Vec3(0, 0, 0);
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

            var into = new List<SolidBoxes.Box>();
            int detailed = SolidBoxes.Select(groups, new Vec3(0, 0, 0), into);

            Assert.AreEqual(0, detailed);
            Assert.AreEqual(494, into.Count);
            for (int i = 0; i < groups.Count; i++)
                Assert.AreEqual(groups[i].Coarse.Min.X, into[i].Min.X, Eps, "order must be stable");
        }

        [Test]
        public void Select_GivesTheNearestObjectsTheirHullAndTheRestTheirBox()
        {
            var groups = new List<SolidBoxes.Group>();
            for (int i = 0; i < 20; i++) groups.Add(GroupAt(i * 10, 4));   // 0, 10, 20 … 190

            var into = new List<SolidBoxes.Box>();
            // Radius 35 reaches the objects at 0/10/20/30 (their boxes are ±1 wide).
            int detailed = SolidBoxes.Select(groups, new Vec3(0, 1, 0), into, 35.0, 1000);

            Assert.AreEqual(4, detailed);
            Assert.AreEqual(4 * 4 + 16, into.Count, "4 hulls of 4 boxes + 16 single boxes");
        }

        [Test]
        public void Select_NeverLetsTheBudgetMakeAnObjectPassable()
        {
            var groups = new List<SolidBoxes.Group>();
            for (int i = 0; i < 400; i++) groups.Add(GroupAt(i * 2, 64));   // all in range, all fat

            var into = new List<SolidBoxes.Box>();
            int detailed = SolidBoxes.Select(groups, new Vec3(0, 1, 0), into, 100000.0,
                                             SolidBoxes.DiverBoxBudget);

            Assert.AreEqual(SolidBoxes.DiverBoxBudget / 64, detailed);
            Assert.LessOrEqual(into.Count, SolidBoxes.DiverBoxBudget + groups.Count,
                               "the guard is the point: 400 × 64 = 25,600 boxes per frame");
            // Every object still contributes something solid.
            int accounted = detailed * 64 + (groups.Count - detailed);
            Assert.AreEqual(accounted, into.Count);
        }

        [Test]
        public void Select_TakesWholeHullsOnly()
        {
            // Half a hull is a wall across the middle of a doorway — worse than the brick, because
            // it is invisible.
            var groups = new List<SolidBoxes.Group> { GroupAt(0, 10), GroupAt(5, 10) };
            var into = new List<SolidBoxes.Box>();
            int detailed = SolidBoxes.Select(groups, new Vec3(0, 1, 0), into, 100.0, 15);

            Assert.AreEqual(1, detailed, "only one hull fits in 15 boxes");
            Assert.AreEqual(11, into.Count, "10 hull boxes + the other object's single box");
        }

        [Test]
        public void Select_IsStableForIdenticalModulesAtTheSameDistance()
        {
            // T-13 is 394 copies of one frame: ties are the normal case, not the edge case, and a
            // set that reshuffles every refresh would pop holes open and shut while you hover.
            var groups = new List<SolidBoxes.Group> { GroupAt(-10, 8), GroupAt(10, 8), GroupAt(-10, 8) };
            var a = new List<SolidBoxes.Box>();
            var b = new List<SolidBoxes.Box>();
            SolidBoxes.Select(groups, new Vec3(0, 1, 0), a, 100.0, 16);
            SolidBoxes.Select(groups, new Vec3(0, 1, 0), b, 100.0, 16);

            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++) Assert.AreEqual(a[i].ToString(), b[i].ToString());
        }

        [Test]
        public void Select_SurvivesNothingToChooseFrom()
        {
            var into = new List<SolidBoxes.Box> { Unit() };
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
