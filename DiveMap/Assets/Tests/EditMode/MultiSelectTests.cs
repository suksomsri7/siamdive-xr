using System;
using System.Collections.Generic;
using DiveMap.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// Unit tests for group transforms.
    ///
    /// The bug these exist to prevent is silent: scale a group about each object's own origin
    /// instead of the group pivot and everything grows WITHOUT moving apart, so a careful
    /// arrangement collapses into itself. Nothing errors, the undo step looks normal, and the
    /// only symptom is that the reef is now a pile.
    /// </summary>
    public class MultiSelectTests
    {
        private static JArray Three() => JArray.Parse(@"[
            {""id"":""a"",""assetId"":""x"",""p"":[-10,0,0],""s"":[1,1,1],""r"":[0,0,0]},
            {""id"":""b"",""assetId"":""x"",""p"":[10,0,0],""s"":[1,1,1],""r"":[0,0,0]},
            {""id"":""c"",""assetId"":""x"",""p"":[0,0,20],""s"":[2,2,2],""r"":[0,0,0]}
        ]");

        private static readonly string[] All = { "a", "b", "c" };

        [Test]
        public void Pivot_IsTheMeanPosition()
        {
            Assert.IsTrue(MultiSelect.Pivot(Three(), All, out double x, out double y, out double z));
            Assert.AreEqual(0, x, 1e-9);
            Assert.AreEqual(0, y, 1e-9);
            Assert.AreEqual(20.0 / 3.0, z, 1e-9);
        }

        [Test]
        public void Pivot_IgnoresIdsThatAreNotThere()
        {
            Assert.IsTrue(MultiSelect.Pivot(Three(), new[] { "a", "zzz" }, out double x, out _, out _));
            Assert.AreEqual(-10, x, 1e-9, "only 'a' counted");
            Assert.IsFalse(MultiSelect.Pivot(Three(), new[] { "zzz" }, out _, out _, out _));
            Assert.IsFalse(MultiSelect.Pivot(null, All, out _, out _, out _));
        }

        [Test]
        public void MoveBy_ShiftsEveryoneEqually()
        {
            JArray items = Three();
            Assert.AreEqual(3, MultiSelect.MoveBy(items, All, 5, 1, -2));
            Assert.AreEqual(-5, (double)SceneEdit.Find(items, "a")["p"][0], 1e-9);
            Assert.AreEqual(15, (double)SceneEdit.Find(items, "b")["p"][0], 1e-9);
            Assert.AreEqual(1, (double)SceneEdit.Find(items, "c")["p"][1], 1e-9);
        }

        [Test]
        public void ScaleBy_MovesObjectsAPARTAsWellAsGrowingThem()
        {
            // THE test. Pivot is (0, 0, 6.67); at ×2 every offset from it doubles.
            JArray items = Three();
            MultiSelect.Pivot(items, All, out double cx, out _, out double cz);
            Assert.AreEqual(3, MultiSelect.ScaleBy(items, All, 2.0));

            JObject a = SceneEdit.Find(items, "a");
            Assert.AreEqual(cx + (-10 - cx) * 2, (double)a["p"][0], 1e-9, "it moved away from the pivot");
            Assert.AreEqual(2, (double)a["s"][0], 1e-9, "…and it grew");

            JObject c = SceneEdit.Find(items, "c");
            Assert.AreEqual(4, (double)c["s"][0], 1e-9, "an already-scaled item multiplies from its own size");
        }

        [Test]
        public void ScaleBy_IsReversible()
        {
            JArray items = Three();
            MultiSelect.ScaleBy(items, All, 2.0);
            MultiSelect.ScaleBy(items, All, 0.5);
            Assert.AreEqual(-10, (double)SceneEdit.Find(items, "a")["p"][0], 1e-6);
            Assert.AreEqual(1, (double)SceneEdit.Find(items, "a")["s"][0], 1e-6);
        }

        [Test]
        public void ScaleBy_RefusesNonsense()
        {
            Assert.AreEqual(0, MultiSelect.ScaleBy(Three(), All, 0));
            Assert.AreEqual(0, MultiSelect.ScaleBy(Three(), All, -1));
            Assert.AreEqual(0, MultiSelect.ScaleBy(null, All, 2));
        }

        [Test]
        public void RotateBy_OrbitsAndTurnsTogether()
        {
            // Two objects on the X axis, pivot between them: a quarter turn puts them on Z.
            var items = JArray.Parse(@"[
                {""id"":""a"",""p"":[-10,0,0],""r"":[0,0,0]},
                {""id"":""b"",""p"":[10,0,0],""r"":[0,0,0]}
            ]");
            MultiSelect.RotateBy(items, new[] { "a", "b" }, Math.PI / 2);

            JObject a = SceneEdit.Find(items, "a");
            Assert.AreEqual(0, (double)a["p"][0], 1e-6, "it orbited the pivot");
            Assert.AreEqual(-10, (double)a["p"][2], 1e-6);
            Assert.AreEqual(Math.PI / 2, (double)a["r"][1], 1e-6, "…and it turned to match");
        }

        [Test]
        public void RotateBy_FullTurnComesBack()
        {
            JArray items = Three();
            MultiSelect.RotateBy(items, All, Math.PI);
            MultiSelect.RotateBy(items, All, Math.PI);
            Assert.AreEqual(-10, (double)SceneEdit.Find(items, "a")["p"][0], 1e-6);
        }

        // ── snapping ─────────────────────────────────────────────────────────────

        [Test]
        public void Snap_RoundsXZToTheGridAndLeavesHeightAlone()
        {
            var items = JArray.Parse(@"[{""id"":""a"",""p"":[13.2,7.4,-2.1]}]");
            MultiSelect.Snap(items, new[] { "a" });

            JObject a = SceneEdit.Find(items, "a");
            Assert.AreEqual(15, (double)a["p"][0], 1e-9);
            Assert.AreEqual(7.4, (double)a["p"][1], 1e-9, "height is placed deliberately — never snapped");
            Assert.AreEqual(0, (double)a["p"][2], 1e-9);
        }

        [Test]
        public void Snap_RefusesAZeroStep()
        {
            Assert.AreEqual(0, MultiSelect.Snap(Three(), All, 0));
        }

        // ── bulk edit ────────────────────────────────────────────────────────────

        [Test]
        public void DeleteAll_RemovesTheWholeSelection()
        {
            JArray items = Three();
            Assert.AreEqual(2, MultiSelect.DeleteAll(items, new[] { "a", "c" }));
            Assert.AreEqual(1, items.Count);
            Assert.IsNotNull(SceneEdit.Find(items, "b"));
        }

        [Test]
        public void DuplicateAll_CopiesEachOnceAndReturnsTheNewIds()
        {
            // Duplicate appends to the array being iterated; without snapshotting the ids first
            // this loops over its own output forever.
            JArray items = Three();
            List<string> made = MultiSelect.DuplicateAll(items, All, 99);

            Assert.AreEqual(3, made.Count);
            Assert.AreEqual(6, items.Count);
            foreach (string id in made) Assert.IsNotNull(SceneEdit.Find(items, id));
            CollectionAssert.AllItemsAreUnique(made);
        }

        [Test]
        public void DuplicateAll_ToleratesNulls()
        {
            Assert.AreEqual(0, MultiSelect.DuplicateAll(Three(), null, 1).Count);
            Assert.AreEqual(0, MultiSelect.DuplicateAll(null, All, 1).Count);
        }
    }
}
