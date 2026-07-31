using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace DiveMap.Core
{
    /// <summary>
    /// Undo / redo for map editing (the web's <c>pushHist</c> / <c>undo</c> / <c>redo</c>).
    ///
    /// Snapshots, not commands. Every edit is already a whole-array rewrite of the scene JSON
    /// (see <see cref="SceneEdit"/>), so storing the array is both simpler and impossible to get
    /// wrong — there is no "inverse operation" to forget. A map is a few hundred small objects;
    /// <see cref="Capacity"/> snapshots of that is far cheaper than one texture.
    ///
    /// Two rules taken from the web's own bug history (HANDOFF v.0651d/e):
    ///  • never take a baseline while models are still loading — a half-built scene becomes the
    ///    state undo returns you to. <see cref="Push"/> refuses an EMPTY array when the history
    ///    already holds a non-empty one, which is that bug's signature.
    ///  • a new edit after an undo discards the redo tail, or redo would replay a future that
    ///    no longer follows from the present.
    /// </summary>
    public sealed class EditHistory
    {
        /// <summary>How many states are kept. The web keeps 60; this matches.</summary>
        public const int Capacity = 60;

        private readonly List<JArray> _states = new List<JArray>();
        private int _index = -1;

        /// <summary>States held right now (QC/tests).</summary>
        public int Count => _states.Count;

        /// <summary>Where we are in the timeline; -1 = nothing recorded yet.</summary>
        public int Index => _index;

        public bool CanUndo => _index > 0;
        public bool CanRedo => _index >= 0 && _index < _states.Count - 1;

        /// <summary>The state as it stands, or null before the first push.</summary>
        public JArray Current => _index >= 0 && _index < _states.Count ? _states[_index] : null;

        /// <summary>
        /// Record a state. Returns false when the push was refused — identical to the current
        /// state (nothing happened), or the empty-array guard above.
        /// </summary>
        public bool Push(JArray items)
        {
            if (items == null) return false;

            JArray snapshot = (JArray)items.DeepClone();

            // "The scene went empty" is almost always a load that has not finished, not the user
            // deleting everything. Clearing is still possible — Clear() calls PushForced.
            if (snapshot.Count == 0 && Current != null && Current.Count > 0) return false;

            if (Current != null && JToken.DeepEquals(Current, snapshot)) return false;

            return PushForced(snapshot);
        }

        /// <summary>Record a state even if it is empty — for a deliberate "clear the map".</summary>
        public bool PushForced(JArray items)
        {
            if (items == null) return false;
            // Always a clone: the caller keeps mutating the array it handed over, and a stored
            // reference would silently rewrite history behind our back.
            var snapshot = (JArray)items.DeepClone();

            // A new edit invalidates everything ahead of the cursor.
            if (_index < _states.Count - 1)
                _states.RemoveRange(_index + 1, _states.Count - _index - 1);

            _states.Add(snapshot);
            _index = _states.Count - 1;

            if (_states.Count > Capacity)
            {
                int drop = _states.Count - Capacity;
                _states.RemoveRange(0, drop);
                _index -= drop;
            }
            return true;
        }

        /// <summary>Step back. Returns the state to apply, or null when there is nothing to undo.</summary>
        public JArray Undo()
        {
            if (!CanUndo) return null;
            _index--;
            return (JArray)_states[_index].DeepClone();
        }

        /// <summary>Step forward. Returns the state to apply, or null.</summary>
        public JArray Redo()
        {
            if (!CanRedo) return null;
            _index++;
            return (JArray)_states[_index].DeepClone();
        }

        /// <summary>Start over — used when a different map is loaded.</summary>
        public void Reset()
        {
            _states.Clear();
            _index = -1;
        }
    }
}
