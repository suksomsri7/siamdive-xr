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
        // ── the web's design tokens (builder.html :39) ─────────────────────────
        //   --bg #071a2b · --panel rgba(11,26,42,.72) + blur(18) · --accent #39b0e8
        //   --txt #eaf4fb · --mut #9fb6c9 · --line rgba(255,255,255,.1)
        // The app must look like the same product as the web, so these are THE colours.
        // Teal/TealDim stay as aliases of the accent because 05.x built its screens against
        // them — one rename would touch every screen without changing a pixel of intent.
        public static readonly Color Accent   = new Color(0.224f, 0.690f, 0.910f, 1f); // #39b0e8
        public static readonly Color OnAccent = new Color(0.016f, 0.071f, 0.122f, 1f); // #04121f
        public static readonly Color Teal     = new Color(0.224f, 0.690f, 0.910f, 1f); // = Accent
        public static readonly Color TealDim  = new Color(0.224f, 0.690f, 0.910f, 0.32f); // accent @32% (web .pub)
        /// <summary>Glass surface. uGUI cannot blur what is behind it, so the web's 0.72 alpha is
        /// raised — without the blur, 0.72 over a busy reef is unreadable.</summary>
        public static readonly Color Glass    = new Color(0.043f, 0.102f, 0.165f, 0.88f);
        /// <summary>The web's 1px hairline border (rgba(255,255,255,.1)).</summary>
        public static readonly Color Line     = new Color(1f, 1f, 1f, 0.10f);
        public static readonly Color PanelBg  = new Color(0.043f, 0.102f, 0.165f, 0.94f); // --panel, opaque enough to read
        // Fully opaque: at 0.99 the screen underneath (the slide-in menu, the 3D scene)
        // bled through as a ghost image in the QC screenshots.
        public static readonly Color ScreenBg = new Color(0.027f, 0.102f, 0.169f, 1f); // --bg #071a2b
        public static readonly Color CardBg   = new Color(1f, 1f, 1f, 0.06f);          // web list/chip fill
        public static readonly Color Scrim    = new Color(0f, 0f, 0f, 0.55f);
        public static readonly Color TextMain = new Color(0.918f, 0.957f, 0.984f, 1f); // --txt #eaf4fb
        public static readonly Color TextDim  = new Color(0.624f, 0.714f, 0.788f, 1f); // --mut #9fb6c9
        public static readonly Color Danger   = new Color(0.690f, 0.204f, 0.290f, 0.92f); // web #leaveDiscard

        /// <summary>The bundled NotoSansThai face — the only font that renders Thai in CI.</summary>
        public static Font Face => UiFont.Get();

        // ── text metrics ─────────────────────────────────────────────────────────

        /// <summary>
        /// Height of ONE line of text as a multiple of <c>fontSize</c>, for the bundled
        /// NotoSansThai-Regular face.
        ///
        /// Measured from the TTF itself (unitsPerEm 1000, hhea/OS-2 ascender 1061,
        /// descender -450, lineGap 0, USE_TYPO_METRICS set) ⇒ 1511/1000 = 1.511.
        /// Thai needs that much room: two levels of tone/vowel marks above the base
        /// glyph and a below-vowel under it, so the face is far taller than a Latin-only
        /// font (~1.2).
        ///
        /// WHY THIS CONSTANT EXISTS — the WO-XR-05.2 "map name is invisible" bug:
        /// legacy <see cref="Text"/> with <see cref="VerticalWrapMode.Truncate"/> does not
        /// clip a line that is too tall, it DROPS it. A 36 px name in a 52 px row needs
        /// 36 × 1.511 = 54.4 px, so the single line was discarded and the card rendered
        /// nothing at all — while the 26 px meta line (39.3 px in a 40 px row) survived by
        /// 0.7 px. Size every text row through <see cref="RowHeight"/> and never below it.
        /// </summary>
        public const float LineHeightRatio = 1.511f;

        /// <summary>Pixel height of one rendered line at <paramref name="fontSize"/>.</summary>
        public static float LineHeight(int fontSize)
        {
            if (fontSize <= 0) return 0f;
            return Mathf.Ceil(fontSize * LineHeightRatio);
        }

        /// <summary>
        /// Minimum safe RectTransform height for <paramref name="lines"/> lines at
        /// <paramref name="fontSize"/>, with a small slack so rounding inside Unity's
        /// TextGenerator can never push the last line out of the rect.
        /// </summary>
        public static float RowHeight(int fontSize, int lines = 1)
        {
            if (lines < 1) lines = 1;
            return LineHeight(fontSize) * lines + 6f;
        }

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

        /// <summary>
        /// A soft-edged circle sprite, generated once at runtime. uGUI's default Image is a
        /// rectangle, and the web's joystick is a circle (its pads are CSS border-radius:50%),
        /// so the app needs a real round sprite — shipping a PNG would mean hand-writing a
        /// .meta + import settings for an asset no one can preview on this machine.
        ///
        /// <paramref name="ringThickness"/> 0 = filled disc; &gt;0 = ring of that fraction of the
        /// radius. Edges are smoothstepped over ~1.5 px so they read as round, not as stairs.
        /// </summary>
        public static Sprite CircleSprite(float ringThickness = 0f)
        {
            int key = Mathf.RoundToInt(ringThickness * 100f);
            if (_circles.TryGetValue(key, out Sprite cached) && cached != null) return cached;

            const int size = 128;
            const float r = size * 0.5f;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "UiCircle" + key,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var px = new Color32[size * size];
            float inner = ringThickness > 0f ? r * (1f - ringThickness) : 0f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - r, dy = y + 0.5f - r;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01((r - 0.75f - d) / 1.5f);          // outer edge
                if (inner > 0f) a *= Mathf.Clamp01((d - inner) / 1.5f);   // inner edge (ring)
                byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f);
                px[y * size + x] = new Color32(255, 255, 255, b);
            }
            tex.SetPixels32(px);
            tex.Apply();

            Sprite sprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
            _circles[key] = sprite;
            return sprite;
        }
        private static readonly System.Collections.Generic.Dictionary<int, Sprite> _circles =
            new System.Collections.Generic.Dictionary<int, Sprite>();

        /// <summary>
        /// The web's chrome button: a 48 px glass circle with a hairline rim and a stroke icon
        /// (builder.html #backBtn / #playBtn / #viewbtns button). <paramref name="accent"/> fills
        /// it with the accent instead of glass, like the web's #menuToggle.
        /// </summary>
        public static Button MakeIconButton(Transform parent, string name, string icon,
                                            UnityEngine.Events.UnityAction onClick,
                                            bool accent = false, float size = 96f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(size, size);

            var bg = go.AddComponent<Image>();
            bg.sprite = CircleSprite();
            bg.color = accent ? new Color(0.184f, 0.560f, 0.839f, 0.96f) : Glass; // #2f8fd6-ish
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            if (onClick != null) btn.onClick.AddListener(onClick);

            Image rim = MakeCircle(rt, "Rim", Line, 0.035f);
            rim.raycastTarget = false;
            Stretch(rim.rectTransform);

            if (!string.IsNullOrEmpty(icon))
            {
                Image ic = MakePanel(rt, "Icon", TextMain);
                ic.raycastTarget = false;
                ic.sprite = IconPainter.Get(icon);
                ic.type = Image.Type.Simple;
                RectTransform irt = ic.rectTransform;
                irt.anchorMin = new Vector2(0.5f, 0.5f);
                irt.anchorMax = new Vector2(0.5f, 0.5f);
                irt.pivot = new Vector2(0.5f, 0.5f);
                irt.sizeDelta = new Vector2(size * 0.46f, size * 0.46f);   // web: 22/48 px
                irt.anchoredPosition = Vector2.zero;
            }
            return btn;
        }

        /// <summary>Swap the icon on a button built by <see cref="MakeIconButton"/>.</summary>
        public static void SetIcon(Button btn, string icon)
        {
            if (btn == null) return;
            Transform t = btn.transform.Find("Icon");
            Image img = t != null ? t.GetComponent<Image>() : null;
            if (img != null) img.sprite = IconPainter.Get(icon);
        }

        /// <summary>Round panel — same as <see cref="MakePanel"/> but with a circle sprite.</summary>
        public static Image MakeCircle(Transform parent, string name, Color color,
                                       float ringThickness = 0f)
        {
            Image img = MakePanel(parent, name, color);
            img.sprite = CircleSprite(ringThickness);
            img.type = Image.Type.Simple;
            return img;
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
            // Overflow, NOT Truncate. Truncate silently DELETES any line taller than the
            // rect instead of clipping it, which is how the map-card name managed to
            // disappear completely (see LineHeightRatio). Overflow means the worst case
            // is text that spills a few pixels — never text that vanishes.
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.supportRichText = false;
            return t;
        }

        /// <summary>
        /// Single-line text: never wraps, never truncates. Long content simply runs past
        /// the rect and is clipped by the nearest RectMask2D (the scroll viewport for a
        /// list card), so a long map name can degrade but can never blank the row.
        /// </summary>
        public static Text MakeLine(Transform parent, string name, string content, int size,
                                    TextAnchor anchor, Color color)
        {
            Text t = MakeText(parent, name, content, size, anchor, color);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
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
