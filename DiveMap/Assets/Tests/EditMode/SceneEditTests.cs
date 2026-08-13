using System.Collections.Generic;
using DiveMap.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// Unit tests for map editing.
    ///
    /// These operations rewrite the map that gets PATCHed to the server, so a mistake here is
    /// not a rendering glitch — it is somebody's dive site coming back wrong, or not coming back.
    /// Everything is asserted on the JSON, which is what actually gets saved.
    /// </summary>
    public class SceneEditTests
    {
        private static JArray Sample() => JArray.Parse(@"[
            {""id"":""a"",""assetId"":""cc0:rock_c"",""p"":[10,20,30],""r"":[0,0,0],""s"":[1,1,1]},
            {""id"":""b"",""assetId"":""losin:clownfish"",""p"":[0,0,0]},
            {""id"":""c"",""assetId"":""art:1268"",""p"":[-5,1,-5],""c"":""#ff0000"",""n"":""ก้อนหิน""}
        ]");

        // ── find / delete ────────────────────────────────────────────────────────

        [Test]
        public void IndexOf_FindsAndMisses()
        {
            JArray items = Sample();
            Assert.AreEqual(0, SceneEdit.IndexOf(items, "a"));
            Assert.AreEqual(2, SceneEdit.IndexOf(items, "c"));
            Assert.AreEqual(-1, SceneEdit.IndexOf(items, "zzz"));
            Assert.AreEqual(-1, SceneEdit.IndexOf(items, null));
            Assert.AreEqual(-1, SceneEdit.IndexOf(null, "a"));
        }

        [Test]
        public void Delete_RemovesOnlyTheNamedItem()
        {
            JArray items = Sample();
            Assert.IsTrue(SceneEdit.Delete(items, "b"));
            Assert.AreEqual(2, items.Count);
            Assert.IsNull(SceneEdit.Find(items, "b"));
            Assert.IsNotNull(SceneEdit.Find(items, "a"));
            Assert.IsNotNull(SceneEdit.Find(items, "c"));
        }

        [Test]
        public void Delete_UnknownIdIsANoOp()
        {
            JArray items = Sample();
            Assert.IsFalse(SceneEdit.Delete(items, "zzz"));
            Assert.AreEqual(3, items.Count);
        }

        [Test]
        public void DeleteMany_ReportsHowManyWentAndSurvivesUnknownIds()
        {
            JArray items = Sample();
            Assert.AreEqual(2, SceneEdit.DeleteMany(items, new[] { "a", "zzz", "c" }));
            Assert.AreEqual(1, items.Count);
            Assert.AreEqual(0, SceneEdit.DeleteMany(items, null));
        }

        // ── duplicate ────────────────────────────────────────────────────────────

        [Test]
        public void Duplicate_CopiesEverythingButTheId()
        {
            JArray items = Sample();
            JObject copy = SceneEdit.Duplicate(items, "c", 12345);

            Assert.IsNotNull(copy);
            Assert.AreEqual(4, items.Count);
            Assert.AreNotEqual("c", (string)copy["id"], "a duplicate MUST get a new id");
            Assert.AreEqual("art:1268", (string)copy["assetId"]);
            Assert.AreEqual("#ff0000", (string)copy["c"], "colour is part of the object");
            Assert.AreEqual("ก้อนหิน", (string)copy["n"]);
        }

        [Test]
        public void Duplicate_OffsetsOnXZSoTheCopyIsVisible()
        {
            JArray items = Sample();
            JObject copy = SceneEdit.Duplicate(items, "a", 1);

            Assert.AreEqual(10 + SceneEdit.DuplicateOffset, (double)copy["p"][0], 1e-9);
            Assert.AreEqual(20, (double)copy["p"][1], 1e-9, "height is unchanged");
            Assert.AreEqual(30 + SceneEdit.DuplicateOffset, (double)copy["p"][2], 1e-9);
        }

        [Test]
        public void Duplicate_TwiceInTheSameTickStillGivesDifferentIds()
        {
            // The purchase path hit exactly this: two items created in one second collided and
            // the duplicate guard silently dropped the second.
            JArray items = Sample();
            JObject one = SceneEdit.Duplicate(items, "a", 777);
            JObject two = SceneEdit.Duplicate(items, "a", 777);
            Assert.AreNotEqual((string)one["id"], (string)two["id"]);
        }

        [Test]
        public void Duplicate_DoesNotShareStateWithTheOriginal()
        {
            JArray items = Sample();
            JObject copy = SceneEdit.Duplicate(items, "c", 1);
            SceneEdit.Recolor(items, (string)copy["id"], "#00ff00");
            Assert.AreEqual("#ff0000", (string)SceneEdit.Find(items, "c")["c"],
                            "editing the copy must not touch the original");
        }

        [Test]
        public void Duplicate_UnknownIdReturnsNull()
        {
            JArray items = Sample();
            Assert.IsNull(SceneEdit.Duplicate(items, "zzz", 1));
            Assert.AreEqual(3, items.Count);
        }

        // ── transform ────────────────────────────────────────────────────────────

        [Test]
        public void Move_WritesAllThreeAxes()
        {
            JArray items = Sample();
            Assert.IsTrue(SceneEdit.Move(items, "b", 1.5, -2.5, 3.5));
            JToken p = SceneEdit.Find(items, "b")["p"];
            Assert.AreEqual(1.5, (double)p[0], 1e-9);
            Assert.AreEqual(-2.5, (double)p[1], 1e-9);
            Assert.AreEqual(3.5, (double)p[2], 1e-9);
        }

        [Test]
        public void Move_AddsThePositionWhenTheItemHadNone()
        {
            var items = JArray.Parse(@"[{""id"":""x"",""assetId"":""cc0:rock_c""}]");
            Assert.IsTrue(SceneEdit.Move(items, "x", 1, 2, 3));
            Assert.AreEqual(2, (double)items[0]["p"][1], 1e-9);
        }

        [Test]
        public void Rotate_WritesRadians()
        {
            JArray items = Sample();
            Assert.IsTrue(SceneEdit.Rotate(items, "a", 0.1, 0.2, 0.3));
            Assert.AreEqual(0.2, (double)SceneEdit.Find(items, "a")["r"][1], 1e-9);
        }

        [Test]
        public void Scale_ClampsAwayZeroAndNegatives()
        {
            JArray items = Sample();
            SceneEdit.Scale(items, "a", 0, -3, 1000);
            JToken s = SceneEdit.Find(items, "a")["s"];
            Assert.AreEqual(SceneEdit.MinScale, (double)s[0], 1e-9, "0 would make it invisible AND unpickable");
            Assert.AreEqual(SceneEdit.MinScale, (double)s[1], 1e-9);
            Assert.AreEqual(SceneEdit.MaxScale, (double)s[2], 1e-9);
        }

        [Test]
        public void ClampScale_HandlesNaN()
        {
            Assert.AreEqual(1.0, SceneEdit.ClampScale(double.NaN), 1e-9);
        }

        // ── colour ───────────────────────────────────────────────────────────────

        [Test]
        public void Recolor_AcceptsHexAndLowercasesIt()
        {
            JArray items = Sample();
            Assert.IsTrue(SceneEdit.Recolor(items, "a", "#AABBCC"));
            Assert.AreEqual("#aabbcc", (string)SceneEdit.Find(items, "a")["c"]);
        }

        [Test]
        public void Recolor_RefusesMalformedValuesInsteadOfWritingThem()
        {
            JArray items = Sample();
            foreach (string bad in new[] { "red", "#fff", "#gggggg", "aabbcc", "", "#aabbccdd" })
                Assert.IsFalse(SceneEdit.Recolor(items, "a", bad), $"'{bad}' should be refused");
            Assert.IsNull(SceneEdit.Find(items, "a")["c"], "nothing was written");
        }

        [Test]
        public void Recolor_NullClearsTheTint()
        {
            JArray items = Sample();
            Assert.IsTrue(SceneEdit.Recolor(items, "c", null));
            Assert.IsNull(SceneEdit.Find(items, "c")["c"]);
        }

        // ── name ─────────────────────────────────────────────────────────────────

        [Test]
        public void Rename_TrimsAndClips()
        {
            JArray items = Sample();
            Assert.IsTrue(SceneEdit.Rename(items, "a", "  ปะการังกิ่งใหญ่  "));
            Assert.AreEqual("ปะการังกิ่งใหญ่", (string)SceneEdit.Find(items, "a")["n"]);

            SceneEdit.Rename(items, "a", new string('x', 200));
            Assert.AreEqual(60, ((string)SceneEdit.Find(items, "a")["n"]).Length);
        }

        [Test]
        public void Rename_EmptyRemovesTheName()
        {
            JArray items = Sample();
            Assert.IsTrue(SceneEdit.Rename(items, "c", "   "));
            Assert.IsNull(SceneEdit.Find(items, "c")["n"]);
        }

        // ── clear ────────────────────────────────────────────────────────────────

        [Test]
        public void Clear_EmptiesAndReportsTheCount()
        {
            JArray items = Sample();
            Assert.AreEqual(3, SceneEdit.Clear(items));
            Assert.AreEqual(0, items.Count);
            Assert.AreEqual(0, SceneEdit.Clear(null));
        }

        // ── items() on a scene ───────────────────────────────────────────────────

        [Test]
        public void Items_CreatesTheArrayWhenTheSceneHasNone()
        {
            var scene = new SceneData(JObject.Parse(@"{""name"":""x""}"));
            JArray arr = SceneEdit.Items(scene);
            Assert.IsNotNull(arr);
            Assert.AreSame(arr, scene.Root["items"], "the scene must now own that array");
        }

        [Test]
        public void Items_ReturnsTheExistingArray()
        {
            var scene = new SceneData(JObject.Parse(@"{""items"":[{""id"":""a""}]}"));
            Assert.AreEqual(1, SceneEdit.Items(scene).Count);
        }

        [Test]
        public void NewId_IsUniqueAcrossManyCallsInOneTick()
        {
            var seen = new HashSet<string>();
            for (int i = 0; i < 500; i++)
                Assert.IsTrue(seen.Add(SceneEdit.NewId(42)), "duplicate id after " + i + " calls");
        }
    
        [Test]
        public void AReadThroughALiveItemIsNotABeforeValue()
        {
            // 🔴 THE INSTRUMENT TRAP, pinned. Move() replaces the item's "p", and a JObject held
            // from before the move resolves "p" again on every read — so a QC pass that keeps the
            // OBJECT as its "before" and reads a coordinate off it at verdict time is comparing
            // the value to itself. That is not hypothetical: it is what made b390-b393 report
            // "the axis constraint moved nothing" for four CI rounds and ~6 hours, against an app
            // that was moving the object correctly the whole time (startX=-4.87 → -0.18, exactly
            // the 4.689 the solve asked for).
            //
            // The rule the QC pass now follows: capture the NUMBER before the gesture, never the
            // node it lives in.
            JArray items = JArray.Parse(@"[{""id"":""a"",""p"":[-4.87,1.0,128.0]}]");
            JObject live = SceneEdit.Find(items, "a");

            double snapshot = (double)live["p"][0];      // the honest "before"
            Assert.IsTrue(SceneEdit.Move(items, "a", -0.18, 1.0, 128.0));

            Assert.AreEqual(-4.87, snapshot, 1e-9, "a captured number stays put");
            Assert.AreEqual(-0.18, (double)live["p"][0], 1e-9,
                            "…while the same read through the live node returns what was just written");
            // …which is why "before == after" proves nothing when 'before' is a reference.
            Assert.AreEqual((double)live["p"][0], -0.18, 1e-9);
        }
}
}
