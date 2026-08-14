using UnityEngine;
using UnityEngine.UI;
using DiveMap.Core;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// WO-N item 3 — the ↺ ↻ pair the user asked for at the bottom right of the edit map.
    ///
    /// 🔴 The engine behind this has been finished and unreachable for a long time.
    /// <see cref="Core.EditHistory"/> is a 60-state ring, <see cref="MapEditor.Undo"/> /
    /// <see cref="MapEditor.Redo"/> work, and every gesture in the app pushes a snapshot
    /// (GizmoController on drag-end, SelectionToolbar on delete/duplicate/recolour,
    /// ObjectListSheet on rename/delete, MapEditor.ClearAll). What did not exist was a button:
    /// the ONLY callers of Undo/Redo were inside the `-qcui` screenshot harness, and
    /// <c>CanUndo</c>/<c>CanRedo</c> had no readers at all. PARITY row I10's ✅ was true of the
    /// engine and false of the product — an author could ruin a map in one tap and had no way
    /// back short of leaving without saving.
    ///
    /// WHERE IT SITS. The web keeps #undoBtn/#redoBtn inside the collapsible ☰ stack
    /// (builder.html:299-300, #actions is max-height:0 until opened), so on the web they are
    /// two taps away. The user asked for them on the map itself, and they are right: undo is the
    /// one control that has to be reachable at the speed of the mistake. So they sit directly
    /// above the ☰ toggle, in the same 48 px circular glass style and the same right-hand column
    /// the web uses — the size and the shape are the web's, the depth is not.
    ///
    /// WHEN IT SHOWS. Only where it can do something: an editing mode
    /// (<see cref="ModeRules.AllowsEditTools"/>) on a map this account may write to. A viewer
    /// and a diver on a tour never see it. A button with nothing to undo is DIMMED rather than
    /// hidden, matching the web's `disabled` — a control that vanishes and reappears under the
    /// thumb is worse than one that is visibly not available yet.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UndoBar : MonoBehaviour
    {
        private const float Size = 48f;
        private const float Gap = 10f;
        /// <summary>Bottom offset of the ☰ toggle (builder.html:104) — this stacks above it.</summary>
        private const float ToggleBottom = 20f;

        private static UndoBar _instance;

        private Button _undo;
        private Button _redo;
        private Image _undoBg;
        private Image _redoBg;
        private Image _undoIcon;
        private Image _redoIcon;
        private CanvasGroup _group;

        private static readonly Color OnTint = new Color(1f, 1f, 1f, 1f);
        private static readonly Color OffTint = new Color(1f, 1f, 1f, 0.3f);   // web: :disabled

        // ── QC surface ───────────────────────────────────────────────────────────
        public static bool IsVisible => _instance != null && _instance._group != null &&
                                        _instance._group.alpha > 0.5f;
        public static UndoBar Current => _instance;
        /// <summary>QC — press ↺ without a synthetic touch.</summary>
        public void QcUndo() => DoUndo();
        /// <summary>QC — press ↻ without a synthetic touch.</summary>
        public void QcRedo() => DoRedo();

        public static UndoBar Create(RectTransform parent)
        {
            if (_instance != null) return _instance;
            RectTransform root = UiKit.MakeNode(parent, "UndoBar");
            UiKit.Stretch(root);
            var bar = root.gameObject.AddComponent<UndoBar>();
            bar.Build(root);
            _instance = bar;
            return bar;
        }

        private void Build(RectTransform root)
        {
            _group = root.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.interactable = false;
            _group.blocksRaycasts = false;

            // Two rows above the ☰: redo directly over the toggle, undo over redo. Undo ends up
            // furthest from the thumb's resting point on purpose — it is the one you reach for
            // deliberately, and putting the destructive-feeling one where a stray tap lands would
            // be the wrong way round.
            _redo = MakeButton(root, "Redo", "redo", DoRedo, ToggleBottom + Size + Gap,
                               out _redoBg, out _redoIcon);
            _undo = MakeButton(root, "Undo", "undo", DoUndo, ToggleBottom + (Size + Gap) * 2f,
                               out _undoBg, out _undoIcon);
        }

        private static Button MakeButton(RectTransform parent, string name, string icon,
                                         UnityEngine.Events.UnityAction action, float bottom,
                                         out Image bg, out Image glyph)
        {
            Button b = UiKit.MakeIconButton(parent, name, icon, action, false, UiKit.Css(Size));
            UiKit.Anchor(b.GetComponent<RectTransform>(), new Vector2(1f, 0f),
                         new Vector2(UiKit.Css(Size), UiKit.Css(Size)),
                         new Vector2(-UiKit.Css(12f), UiKit.Css(bottom)));
            bg = b.GetComponent<Image>();
            Transform t = b.transform.Find("Icon");
            glyph = t != null ? t.GetComponent<Image>() : null;
            return b;
        }

        private void DoUndo()
        {
            if (!MapEditor.CanUndo) return;
            bool ok = MapEditor.Undo();
            Debug.Log($"[Edit] undo pressed ok={ok} left={MapEditor.HistoryCount}");
            // The selection refers to an item the restored snapshot may no longer contain, and a
            // toolbar pointing at a deleted object is how you get a null-reference on the next tap.
            SelectionToolbar.Hide();
            GizmoController.Deselect();
            Refresh();
        }

        private void DoRedo()
        {
            if (!MapEditor.CanRedo) return;
            bool ok = MapEditor.Redo();
            Debug.Log($"[Edit] redo pressed ok={ok}");
            SelectionToolbar.Hide();
            GizmoController.Deselect();
            Refresh();
        }

        /// <summary>
        /// Cheap enough to run every frame (four bools and at most four colour writes), and that
        /// is deliberate: history changes from a dozen places — a drag ending, a delete, a
        /// restore from the revision sheet — and an event for each would be one more thing to
        /// forget to raise. The bar going stale is exactly the bug that makes an author stop
        /// trusting undo.
        /// </summary>
        private void Update() => Refresh();

        /// <summary>
        /// The ☰ column is open, so stand down.
        ///
        /// 🔴 Both stacks are anchored bottom-right and both count slots upward from the ☰: the
        /// action buttons from BuildActions, ↺↻ from Build above. Open the column and they land in
        /// the same squares — the user's screenshot shows ↺ drawn straight on top of the 🤿 tour
        /// button, two glyphs in one circle. The compass already steps aside for the column
        /// (UiShell.ToggleActions); this is the same rule for the same reason.
        /// </summary>
        public static void SetSuppressed(bool suppressed)
        {
            _suppressed = suppressed;
            if (_instance != null) _instance.Refresh();
        }

        private static bool _suppressed;

        private void Refresh()
        {
            var boot = FindFirstObjectByType<AppBoot>();
            bool show = !_suppressed && boot != null && boot.CanEditCurrent &&
                        ModeRules.AllowsEditTools(ModeManager.Current);

            if (_group != null)
            {
                _group.alpha = show ? 1f : 0f;
                _group.interactable = show;
                _group.blocksRaycasts = show;
            }
            if (!show) return;

            bool canUndo = MapEditor.CanUndo;
            bool canRedo = MapEditor.CanRedo;
            if (_undo != null) _undo.interactable = canUndo;
            if (_redo != null) _redo.interactable = canRedo;
            if (_undoIcon != null) _undoIcon.color = canUndo ? OnTint : OffTint;
            if (_redoIcon != null) _redoIcon.color = canRedo ? OnTint : OffTint;
            if (_undoBg != null) _undoBg.color = Tint(_undoBg.color, canUndo);
            if (_redoBg != null) _redoBg.color = Tint(_redoBg.color, canRedo);
        }

        private static Color Tint(Color c, bool on)
        {
            c.a = on ? 0.72f : 0.34f;   // UiKit.Glass alpha, halved when unavailable
            return c;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
