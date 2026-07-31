using System.Collections;
using Newtonsoft.Json.Linq;
using UnityEngine;
using DiveMap.Core;

namespace DiveMap.Runtime
{
    /// <summary>
    /// The editing session: undo history, the rebuild after a change, and autosave.
    ///
    /// The web's shape (builder.html), reproduced:
    /// <code>
    ///   pushHist()      after every mutation
    ///   autosaveTick()  every 1.3 s while dirty
    ///                   :3379  "if(tc.dragging) return"  — v.0735: dragging a gizmo marks the
    ///                          scene dirty every frame, so a 1-minute drag fired ~46 PATCHes
    ///   doSave()        PATCH items (+ name / thumbUrl)
    /// </code>
    ///
    /// Two departures from the web, both deliberate:
    ///  • every save sends <c>baseRev</c>, so a second device's work is never clobbered (the web
    ///    tracks the rev but never sends it — see IMPROVEMENTS A4).
    ///  • a save that comes back 403 does not keep retrying. On an admin world map the answer
    ///    will not change, and a retry loop would hammer the API for the rest of the session.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MapEditor : MonoBehaviour
    {
        /// <summary>builder.html's autosave interval.</summary>
        public const float AutosaveSeconds = 1.3f;

        private static MapEditor _instance;

        private readonly EditHistory _history = new EditHistory();
        private string _mapId;
        private bool _dirty;
        private float _nextSave;
        private bool _saving;
        private bool _refused;   // 403 — stop asking

        /// <summary>True while a gizmo drag is in progress; autosave waits for the finger to lift.</summary>
        public static bool Dragging { get; set; }

        // ── QC surface ───────────────────────────────────────────────────────────
        public static bool CanUndo => _instance != null && _instance._history.CanUndo;
        public static bool CanRedo => _instance != null && _instance._history.CanRedo;
        public static int HistoryCount => _instance != null ? _instance._history.Count : 0;
        public static bool IsDirty => _instance != null && _instance._dirty;
        public static bool SaveRefused => _instance != null && _instance._refused;

        public static MapEditor Ensure()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("MapEditor");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<MapEditor>();
            return _instance;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        /// <summary>
        /// Begin editing a map. Resets the history — undoing across two different maps would
        /// write one map's items into the other.
        /// </summary>
        public static void Begin(string mapId, JArray items)
        {
            MapEditor e = Ensure();
            if (e._mapId == mapId) return;

            e._mapId = mapId;
            e._history.Reset();
            e._history.Push(items);
            e._dirty = false;
            e._refused = false;
            Debug.Log($"[Edit] session {mapId} baseline items={(items != null ? items.Count : 0)}");
        }

        /// <summary>
        /// Record the current state and redraw. Called after every mutation; the snapshot is what
        /// undo returns to, and the rebuild is what the player sees.
        /// </summary>
        public static void RecordAndApply(JArray items)
        {
            MapEditor e = Ensure();
            e._history.Push(items);
            e.MarkDirty();
            Rebuild();
        }

        public static bool Undo()
        {
            MapEditor e = Ensure();
            JArray state = e._history.Undo();
            if (state == null) return false;
            e.Apply(state);
            Debug.Log($"[Edit] undo → {state.Count} items (index {e._history.Index})");
            return true;
        }

        public static bool Redo()
        {
            MapEditor e = Ensure();
            JArray state = e._history.Redo();
            if (state == null) return false;
            e.Apply(state);
            Debug.Log($"[Edit] redo → {state.Count} items (index {e._history.Index})");
            return true;
        }

        /// <summary>Everything off the map — recorded, so it is undoable.</summary>
        public static int ClearAll()
        {
            var boot = FindFirstObjectByType<AppBoot>();
            SceneData scene = boot != null ? boot.CurrentScene : null;
            if (scene == null) return 0;

            JArray items = SceneEdit.Items(scene);
            int n = SceneEdit.Clear(items);
            if (n == 0) return 0;

            MapEditor e = Ensure();
            e._history.PushForced(items);   // an EMPTY state is the point here
            e.MarkDirty();
            Rebuild();
            Debug.Log($"[Edit] cleared {n} item(s)");
            return n;
        }

        private void Apply(JArray state)
        {
            var boot = FindFirstObjectByType<AppBoot>();
            SceneData scene = boot != null ? boot.CurrentScene : null;
            if (scene == null) return;

            scene.Root["items"] = state;
            MarkDirty();
            Rebuild();
        }

        private static void Rebuild()
        {
            var boot = FindFirstObjectByType<AppBoot>();
            if (boot != null) boot.RebuildFromMemory();
        }

        private void MarkDirty()
        {
            _dirty = true;
            _nextSave = Time.realtimeSinceStartup + AutosaveSeconds;
        }

        // ── autosave ─────────────────────────────────────────────────────────────

        private void Update()
        {
            if (!_dirty || _saving || _refused) return;
            // A drag marks the scene dirty every frame; saving mid-drag is ~46 PATCHes a minute
            // for one gesture. Wait for the finger to lift, then save once (web v.0735).
            if (Dragging) { _nextSave = Time.realtimeSinceStartup + AutosaveSeconds; return; }
            if (Time.realtimeSinceStartup < _nextSave) return;

            StartCoroutine(Save());
        }

        /// <summary>Save now (used when leaving the map — the web's leaveModal path).</summary>
        public static void Flush()
        {
            MapEditor e = _instance;
            if (e == null || !e._dirty || e._saving || e._refused) return;
            e.StartCoroutine(e.Save());
        }

        private IEnumerator Save()
        {
            var boot = FindFirstObjectByType<AppBoot>();
            SceneData scene = boot != null ? boot.CurrentScene : null;
            if (scene == null || boot == null) { _dirty = false; yield break; }

            if (!boot.CanEditCurrent)
            {
                // Not an error: most maps a player dives into are admin worlds.
                _refused = true;
                _dirty = false;
                Debug.Log("[Edit] autosave off — this account cannot edit this map");
                yield break;
            }

            _saving = true;
            JArray items = SceneEdit.Items(scene);

            MapSaveClient.Result result = default;
            yield return MapSaveClient.SaveItems(boot.CurrentMapId, items, boot.CurrentRev,
                                                 r => result = r);
            _saving = false;

            if (result.Ok)
            {
                _dirty = false;
                Toast.ShowTr("บันทึกแล้ว");
                yield break;
            }

            if (result.Forbidden)
            {
                _refused = true;
                _dirty = false;
                Toast.ShowTr("แมพนี้แก้ไม่ได้ — เก็บไว้ในเครื่องนี้แทน");
                yield break;
            }

            if (result.Conflict)
            {
                // Someone else saved first. Do NOT retry with the same baseRev — that is a loop.
                _refused = true;
                _dirty = false;
                Toast.ShowTr("มีคนแก้แมพนี้ก่อน — เก็บไว้ในเครื่องนี้แทน");
                yield break;
            }

            // A transient failure: leave it dirty and try again on the next tick.
            _nextSave = Time.realtimeSinceStartup + AutosaveSeconds * 4f;
            Debug.LogWarning("[Edit] autosave failed, will retry: " + result.Error);
        }
    }
}
