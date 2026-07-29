using System;
using System.Collections.Generic;
using UnityEngine;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// Screen stack for the shell (WO-XR-05.1).
    ///
    /// A "screen" is just a GameObject layer under the UI canvas. Pushing activates it
    /// and raises it to the top sibling; popping deactivates it and fires its close
    /// callback. <see cref="StackChanged"/> lets the shell gate the 3D camera in one
    /// place (stack empty ⇒ orbit allowed).
    ///
    /// Android back / desktop Escape pops one screen. When nothing is open we do
    /// NOTHING — never Application.Quit: killing the app on a stray back press is the
    /// single most common Unity-on-Android UX bug and there is no confirm dialog yet.
    ///
    /// Legacy UnityEngine.Input only (no InputSystem package in this project).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiNav : MonoBehaviour
    {
        private sealed class Entry
        {
            public string Name;
            public GameObject Root;
            public Action OnClose;
        }

        private readonly List<Entry> _stack = new List<Entry>();

        /// <summary>Raised with the new depth every time the stack changes.</summary>
        public event Action<int> StackChanged;

        public int Count => _stack.Count;

        public string TopName => _stack.Count > 0 ? _stack[_stack.Count - 1].Name : null;

        public bool IsOpen(GameObject root)
        {
            for (int i = 0; i < _stack.Count; i++)
                if (_stack[i].Root == root) return true;
            return false;
        }

        public void Push(string name, GameObject root, Action onClose = null)
        {
            if (root == null || IsOpen(root)) return;

            root.SetActive(true);
            root.transform.SetAsLastSibling();
            _stack.Add(new Entry { Name = name, Root = root, OnClose = onClose });
            Debug.Log($"[UI] push {name} (depth={_stack.Count})");
            StackChanged?.Invoke(_stack.Count);
        }

        public bool Pop()
        {
            if (_stack.Count == 0) return false;

            Entry e = _stack[_stack.Count - 1];
            _stack.RemoveAt(_stack.Count - 1);
            if (e.Root != null) e.Root.SetActive(false);

            try { e.OnClose?.Invoke(); }
            catch (Exception ex) { Debug.LogWarning("[UiNav] close handler: " + ex.Message); }

            Debug.Log($"[UI] pop {e.Name} (depth={_stack.Count})");
            StackChanged?.Invoke(_stack.Count);
            return true;
        }

        public void PopAll()
        {
            while (Pop()) { }
        }

        private void Update()
        {
            // Android hardware back arrives as Escape through the legacy input module.
            if (Input.GetKeyDown(KeyCode.Escape) && _stack.Count > 0)
                Pop();
        }
    }
}
