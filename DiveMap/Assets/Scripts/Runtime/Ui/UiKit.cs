using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// Code-only uGUI construction helpers (WO-XR-05.1).
    ///
    /// The project deliberately does NOT use TextMeshPro or UI Toolkit: there is no
    /// Unity Editor on the build machine, so an SDF font asset / PanelSettings can
    /// never be baked. Everything is legacy <see cref="Text"/> with the bundled
    /// NotoSansThai face from <see cref="UiFont"/> — the only font proven to render
    /// Thai in the headless Linux CI player.
    ///
    /// This mirrors AppBoot.MakeText/MakeButton in spirit but is a separate,
    /// self-contained implementation: AppBoot is owned by another work order and
    /// must not be refactored.
    ///
    /// Palette: dark glass + teal #4FD1C5 (DESIGN_DOC §3.4).
    /// </summary>
    public static class UiKit
    {
        public static readonly Color Teal     = new Color(0.310f, 0.820f, 0.771f, 1f); // #4FD1C5
        public static readonly Color TealDim  = new Color(0.176f, 0.478f, 0.451f, 1f);
        public static readonly Color PanelBg  = new Color(0.043f, 0.090f, 0.118f, 0.96f);
        public static readonly Color ScreenBg = new Color(0.027f, 0.067f, 0.094f, 0.99f);
        public static readonly Color CardBg   = new Color(0.086f, 0.157f, 0.196f, 1f);
        public static readonly Color Scrim    = new Color(0f, 0f, 0f, 0.55f);
        public static readonly Color TextMain = new Color(0.925f, 0.961f, 0.973f, 1f);
        public static readonly Color TextDim  = new Color(0.647f, 0.737f, 0.780f, 1f);
        public static readonly Color Danger   = new Color(1f, 0.702f, 0.702f, 1f);

        /// <summary>The bundled NotoSansThai face — the only font that renders Thai in CI.</summary>
        public static Font Face => UiFont.Get();

        // ── primitives ───────────────────────────────────────────────────────────

        /// <summary>Empty RectTransform node, stretched to its parent by default.</summary>
        public static RectTransform MakeNode(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            Stretch(rt);
            return rt;
        }

        /// <summary>Solid-colour panel (no sprite — avoids any Resources/shader dependency).</summary>
        public static Image MakePanel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            Stretch(rt);
            var img = go.AddComponent<Image>();
            img.color = color;
            return img;
        }

        public static Text MakeText(Transform parent, string name, string content, int size,
                                    TextAnchor anchor, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = Face;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = color;
            t.text = content ?? "";
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.raycastTarget = false;
            t.supportRichText = false;
            return t;
        }

        /// <summary>Button with a solid background and a centred label. Returns the Button.</summary>
        public static Button MakeButton(Transform parent, string name, string label, int size,
                                        Color bg, Color fg, UnityAction onClick)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            Stretch(rt);

            var img = go.AddComponent<Image>();
            img.color = bg;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = new Color(1f, 1f, 1f, 1f);
            colors.pressedColor = new Color(0.75f, 0.85f, 0.88f, 1f);
            colors.fadeDuration = 0.06f;
            btn.colors = colors;
            if (onClick != null) btn.onClick.AddListener(onClick);

            if (label != null)
            {
                Text t = MakeText(go.transform, "Label", label, size, TextAnchor.MiddleCenter, fg);
                Stretch(t.rectTransform);
                t.rectTransform.offsetMin = new Vector2(12f, 0f);
                t.rectTransform.offsetMax = new Vector2(-12f, 0f);
            }
            return btn;
        }

        /// <summary>Legacy single-line InputField (no InputSystem package in this project).</summary>
        public static InputField MakeInput(Transform parent, string name, string placeholder, int size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            Stretch(rt);

            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.129f, 0.204f, 0.243f, 1f);

            Text text = MakeText(go.transform, "Text", "", size, TextAnchor.MiddleLeft, TextMain);
            text.supportRichText = false;
            Stretch(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(20f, 6f);
            text.rectTransform.offsetMax = new Vector2(-20f, -6f);

            Text ph = MakeText(go.transform, "Placeholder", placeholder, size, TextAnchor.MiddleLeft, TextDim);
            Stretch(ph.rectTransform);
            ph.rectTransform.offsetMin = new Vector2(20f, 6f);
            ph.rectTransform.offsetMax = new Vector2(-20f, -6f);

            var input = go.AddComponent<InputField>();
            input.targetGraphic = bg;
            input.textComponent = text;
            input.placeholder = ph;
            input.lineType = InputField.LineType.SingleLine;
            input.caretColor = Teal;
            input.customCaretColor = true;
            input.selectionColor = new Color(0.310f, 0.820f, 0.771f, 0.35f);
            return input;
        }

        /// <summary>
        /// Vertical ScrollRect with a RectMask2D viewport. <paramref name="content"/> is
        /// anchored top-stretch with pivot (0.5, 1) so children can be laid out by hand
        /// at negative Y — deterministic, no LayoutGroup timing surprises.
        /// </summary>
        public static ScrollRect MakeScroll(Transform parent, string name, out RectTransform content)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            Stretch(rt);

            var scroll = go.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.elasticity = 0.1f;
            scroll.inertia = true;
            scroll.decelerationRate = 0.135f;
            scroll.scrollSensitivity = 40f;

            var viewport = MakeNode(go.transform, "Viewport");
            viewport.gameObject.AddComponent<RectMask2D>();

            // A fully transparent raycast target so a drag that starts on EMPTY list
            // space still reaches the ScrollRect. Without it only drags that begin on a
            // card would scroll (uGUI bubbles drag events up from the hit graphic).
            var catcher = viewport.gameObject.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);
            catcher.raycastTarget = true;

            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(viewport, false);
            content = contentGo.AddComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = new Vector2(0f, 0f);
            content.offsetMax = new Vector2(0f, 0f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 0f);

            scroll.viewport = viewport;
            scroll.content = content;
            return scroll;
        }

        /// <summary>RawImage for a downloaded thumbnail (UnityWebRequestTexture output).</summary>
        public static RawImage MakeRaw(Transform parent, string name, Color placeholder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            Stretch(rt);
            var raw = go.AddComponent<RawImage>();
            raw.color = placeholder;
            raw.raycastTarget = false;
            return raw;
        }

        // ── rect helpers ─────────────────────────────────────────────────────────

        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>Anchor to a corner: (0,0)=bottom-left … (1,1)=top-right.</summary>
        public static void Anchor(RectTransform rt, Vector2 corner, Vector2 size, Vector2 offset)
        {
            rt.anchorMin = corner;
            rt.anchorMax = corner;
            rt.pivot = corner;
            rt.sizeDelta = size;
            rt.anchoredPosition = offset;
        }

        /// <summary>Full-width row pinned under the parent's top edge.</summary>
        public static void TopRow(RectTransform rt, float y, float height, float leftPad, float rightPad)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            // With a horizontal stretch, sizeDelta.x is the width *offset* from the
            // parent's width; anchoredPosition.x shifts the (centre) pivot.
            rt.sizeDelta = new Vector2(-(leftPad + rightPad), height);
            rt.anchoredPosition = new Vector2((leftPad - rightPad) * 0.5f, -y);
        }
    }
}
