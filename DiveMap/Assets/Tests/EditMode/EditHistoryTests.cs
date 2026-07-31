using DiveMap.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// Unit tests for undo / redo.
    ///
    /// The web lost a whole map to this once (HANDOFF v.0651d/e: "แมพหาย#2" — autosave wrote a
    /// half-loaded scene, undo's baseline was that half-scene, 108 items became 31). The guard
    /// that came out of it is asserted here so it cannot be removed by accident.
    /// </summary>
    public class EditHistoryTests
    {
        private static JArray Items(params string[] ids)
        {
            var a = new JArray();
            foreach (string id in ids) a.Add(new JObject { ["id"] = id });
            return a;
        }

        [Test]
        public void Empty_HasNothingToUndoOrRedo()
        {
            var h = new EditHistory();
            Assert.IsFalse(h.CanUndo);
            Assert.IsFalse(h.CanRedo);
            Assert.IsNull(h.Current);
            Assert.IsNull(h.Undo());
            Assert.IsNull(h.Redo());
        }

        [Test]
        public void Push_StoresASnapshotNotAReference()
        {
            var h = new EditHistory();
            JArray live = Items("a");
            h.Push(live);

            live.Add(new JObject { ["id"] = "b" });   // the caller keeps editing
            Assert.AreEqual(1, h.Current.Count, "history must not follow later edits");
        }

        [Test]
        public void Push_IgnoresAStateIdenticalToTheCurrentOne()
        {
            var h = new EditHistory();
            Assert.IsTrue(h.Push(Items("a")));
            Assert.IsFalse(h.Push(Items("a")), "nothing changed — do not burn a history slot");
            Assert.AreEqual(1, h.Count);
        }

        [Test]
        public void UndoRedo_WalksTheTimeline()
        {
            var h = new EditHistory();
            h.Push(Items("a"));
            h.Push(Items("a", "b"));
            h.Push(Items("a", "b", "c"));

            Assert.AreEqual(2, h.Undo().Count);
            Assert.AreEqual(1, h.Undo().Count);
            Assert.IsFalse(h.CanUndo);

            Assert.AreEqual(2, h.Redo().Count);
            Assert.AreEqual(3, h.Redo().Count);
            Assert.IsFalse(h.CanRedo);
        }

        [Test]
        public void Undo_ReturnsACopySoTheCallerCannotCorruptHistory()
        {
            var h = new EditHistory();
            h.Push(Items("a"));
            h.Push(Items("a", "b"));

            JArray got = h.Undo();
            got.Add(new JObject { ["id"] = "hack" });

            Assert.AreEqual(2, h.Redo().Count, "the stored state was not touched");
        }

        [Test]
        public void EditingAfterUndo_DropsTheRedoTail()
        {
            var h = new EditHistory();
            h.Push(Items("a"));
            h.Push(Items("a", "b"));
            h.Push(Items("a", "b", "c"));

            h.Undo();                       // back to 2 items
            Assert.IsTrue(h.CanRedo);
            h.Push(Items("a", "x"));        // a different future

            Assert.IsFalse(h.CanRedo, "the old future no longer follows from the present");
            Assert.AreEqual(3, h.Count);
        }

        // ── the "map went empty" guard ───────────────────────────────────────────

        [Test]
        public void Push_RefusesAnEmptyStateWhenTheMapHadContent()
        {
            // This is the v.0651 bug: models still loading → the scene looks empty → a snapshot
            // of nothing becomes the state undo returns you to.
            var h = new EditHistory();
            h.Push(Items("a", "b", "c"));
            Assert.IsFalse(h.Push(new JArray()), "an empty scene is a load in progress, not an edit");
            Assert.AreEqual(3, h.Current.Count);
        }

        [Test]
        public void Push_AllowsAnEmptyStateAsTheVeryFirstOne()
        {
            var h = new EditHistory();
            Assert.IsTrue(h.Push(new JArray()), "a genuinely empty new map is fine");
            Assert.AreEqual(0, h.Current.Count);
        }

        [Test]
        public void PushForced_IsHowADeliberateClearGetsRecorded()
        {
            var h = new EditHistory();
            h.Push(Items("a", "b"));
            Assert.IsTrue(h.PushForced(new JArray()));
            Assert.AreEqual(0, h.Current.Count);
            Assert.IsTrue(h.CanUndo, "and it is undoable");
            Assert.AreEqual(2, h.Undo().Count);
        }

        // ── bounds ───────────────────────────────────────────────────────────────

        [Test]
        public void Capacity_DropsTheOldestStatesAndKeepsTheCursorValid()
        {
            var h = new EditHistory();
            for (int i = 0; i < EditHistory.Capacity + 20; i++) h.Push(Numbered(i));

            Assert.AreEqual(EditHistory.Capacity, h.Count);
            Assert.AreEqual(EditHistory.Capacity - 1, h.Index);
            Assert.IsTrue(h.CanUndo);
            Assert.IsNotNull(h.Undo());
        }

        private static JArray Numbered(int n)
        {
            var a = new JArray();
            for (int i = 0; i <= n; i++) a.Add(new JObject { ["id"] = "i" + i });
            return a;
        }

        [Test]
        public void Push_NullIsRefused()
        {
            var h = new EditHistory();
            Assert.IsFalse(h.Push(null));
            Assert.IsFalse(h.PushForced(null));
        }

        [Test]
        public void Reset_ClearsEverything()
        {
            var h = new EditHistory();
            h.Push(Items("a"));
            h.Push(Items("a", "b"));
            h.Reset();

            Assert.AreEqual(0, h.Count);
            Assert.AreEqual(-1, h.Index);
            Assert.IsFalse(h.CanUndo);
            Assert.IsNull(h.Current);
        }
    }
}
