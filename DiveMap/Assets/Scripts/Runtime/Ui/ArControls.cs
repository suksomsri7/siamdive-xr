using DiveMap.Core;
using UnityEngine;
using UnityEngine.UI;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// The AR overlay: what to do next, how big it is, and the way out.
    ///
    /// 🔴 The − / + stepper is GONE, and that is the point of this version. The web has one
    /// (builder.html:349 <c>#arMinus</c>/<c>#arPlus</c>) because the web has no tracking: with the
    /// model welded in front of the face, a stepper is the only size control available. With ARKit
    /// the site stands on a real table, and the gesture everybody already knows — two fingers —
    /// says what a stepper only approximates. Removing the two buttons also gives the room back the
    /// bottom of the screen, which in the one mode whose whole purpose is seeing the room is not a
    /// small thing.
    ///
    /// What replaces it is a sequence, because placing something in a room IS a sequence:
    ///   1. <see cref="ArStep.Searching"/> — "point at the floor". Nothing can be done yet and the
    ///      screen says so, rather than inviting a tap that would be ignored.
    ///   2. <see cref="ArStep.Aiming"/>    — a surface has been found (the blue patch is visible).
    ///                                       "Tap where you want it."
    ///   3. <see cref="ArStep.Adjusting"/> — placed. Pinch to size, tap to move, ✓ to commit. The
    ///                                       size is shown in METRES: "1.10 m" is checkable against
    ///                                       the actual table, which "×1.22" never was.
    ///   4. <see cref="ArStep.Anchored"/>  — pinned to an ARAnchor. The prompt becomes the way back.
    ///
    /// Everything is positioned through <see cref="UiKit.Css"/> (UI_PARITY.md): the canvas scales
    /// with the device, so a hard-coded unit is a different size on every phone.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArControls : MonoBehaviour
    {
        private static ArControls _open;

        /// <summary>QC surface — the bar is on screen.</summary>
        public static bool IsOpen => _open != null;

        private Text _hint, _size;
        private Button _action;
        private Text _actionLabel;
        private ArStep _step = ArStep.Searching;

        /// <summary>
        /// Put a line of live state back on the HUD.
        ///
        /// 🔎 Nothing calls this now. It stayed because it earned its keep: AR is the one feature
        /// with no CI, no console and no reachable log, and this line closed four bugs in four
        /// rounds from photographs alone. The text came off the screen the moment AR worked —
        /// asked for directly, and right, because the point of the mode is the room. One call to
        /// <c>SetDiagnostics(ArKitSession.Instance.Status())</c> brings it all back.
        /// </summary>
        public static void SetDiagnostics(string line)
        {
            if (_open == null || _open._hint == null) return;
            _open._hint.text = line;
        }

        /// <summary>Replace the one-line instruction with something more specific than the step's
        /// own wording (e.g. "no surface where you tapped").</summary>
        public static void SetHint(string text) => _open?.ShowHint(text);

        /// <summary>Move to a step: rewrites the instruction and the button in one place, so the
        /// two can never describe different states.</summary>
        public static void SetStep(ArStep step, double metres) => _open?.ApplyStep(step, metres);

        /// <summary>Live during a pinch. <paramref name="atLimit"/> greys the readout at a stop —
        /// the alternative is a map that silently stops responding to the fingers.</summary>
        public static void SetSize(double metres, bool atLimit) => _open?.ShowSize(metres, atLimit);

        /// <summary>
        /// The overlay for a phone with no tracking: size, and nothing else.
        ///
        /// That path has no floor to find and no anchor to set — the site is already in front of
        /// the viewer — so it gets neither the step wording nor the confirm button. Showing
        /// "looking for a surface" on a device that will never find one reads as a hang, and a ✓
        /// that pins nothing is a button that lies about what it did.
        /// </summary>
        public static void SetSizeOnly(double metres) => _open?.ApplySizeOnly(metres);

        public static void Open()
        {
            if (_open != null) return;
            RectTransform host = UiShell.Instance != null ? UiShell.Instance.OverlayRoot : null;
            if (host == null) return;

            var go = new GameObject("ArControls");
            go.transform.SetParent(host, false);
            var rt = go.AddComponent<RectTransform>();
            UiKit.Stretch(rt);
            var c = go.AddComponent<ArControls>();
            c.Build(rt);
            _open = c;
            Debug.Log("[AR] controls open");
        }

        public static void Close()
        {
            if (_open == null) return;
            Destroy(_open.gameObject);
            _open = null;
        }

        private void OnDestroy()
        {
            if (_open == this) _open = null;
        }

        private void ShowHint(string text)
        {
            if (_hint != null) _hint.text = text;
        }

        /// <summary>
        /// Seconds the size stays on screen after the fingers stop. Long enough to read the number
        /// you just set, short enough that the resting screen is the room and nothing else.
        /// </summary>
        private const float SizeLinger = 1.6f;
        private float _sizeUntil;

        private void ShowSize(double metres, bool atLimit)
        {
            if (_size == null) return;
            _size.gameObject.SetActive(true);
            _size.text = metres.ToString("0.00") + " " + UiStrings.Tr("ม.");
            _size.color = atLimit ? UiKit.TextDim : UiKit.TextMain;
            _sizeUntil = Time.unscaledTime + SizeLinger;
        }

        private void Update()
        {
            if (_size != null && _size.gameObject.activeSelf && Time.unscaledTime > _sizeUntil)
                _size.gameObject.SetActive(false);
        }

        private void ApplyStep(ArStep step, double metres)
        {
            _step = step;
            switch (step)
            {
                case ArStep.Searching:
                    ShowHint(UiStrings.Tr("เล็งกล้องไปที่พื้นเรียบ กำลังหาพื้น…"));
                    ShowAction(false, "");
                    if (_size != null) _size.gameObject.SetActive(false);
                    break;

                case ArStep.Aiming:
                    ShowHint(UiStrings.Tr("เจอพื้นแล้ว — แตะตรงที่อยากวางแผนที่"));
                    ShowAction(false, "");
                    if (_size != null) _size.gameObject.SetActive(false);
                    break;

                case ArStep.Adjusting:
                    ShowHint(UiStrings.Tr("สองนิ้วย่อ-ขยาย · แตะเพื่อย้าย · กดยืนยันเมื่อพอใจ"));
                    ShowAction(true, UiStrings.Tr("✓ ยืนยัน"));
                    ShowSize(metres, ArPinch.AtLimit(metres));
                    break;

                case ArStep.Anchored:
                    ShowHint(UiStrings.Tr("ยึดกับพื้นแล้ว — เดินรอบดูได้เลย"));
                    ShowAction(true, UiStrings.Tr("ย้ายตำแหน่ง"));
                    ShowSize(metres, ArPinch.AtLimit(metres));
                    break;
            }
        }

        private void ApplySizeOnly(double metres)
        {
            _step = ArStep.Adjusting;
            ShowHint(UiStrings.Tr("สองนิ้วย่อ-ขยาย"));
            ShowAction(false, "");
            ShowSize(metres, ArPinch.AtLimit(metres));
        }

        private void ShowAction(bool visible, string label)
        {
            if (_action == null) return;
            _action.gameObject.SetActive(visible);
            if (_actionLabel != null) _actionLabel.text = label;
        }

        /// <summary>The one button under the hint does whichever thing the current step needs.
        /// Two buttons that are never both useful is two buttons too many on a phone.</summary>
        private void OnAction()
        {
            if (_step == ArStep.Anchored) ArKitSession.Adjust();
            else ArKitSession.Confirm();
        }

        private void Build(RectTransform root)
        {
            // ✕ — top-right, as a plain close button. Asked for directly, and it is the right
            // shape: AR fills the screen with the room, so the one piece of chrome that dismisses
            // it should be where every phone puts "close" and should cost as little of the view as
            // possible. The old wide "✕ ออก AR" pill sat top-left over the very surface the user
            // is trying to aim at.
            //
            // 🔴 The glyph is DRAWN, not typed. NotoSansThai has no U+2715, so the old label
            // rendered as bare "ออก AR" on the device — visible in the user's screenshot and
            // invisible in code review, exactly like the ＋ button before it. IconPainter's
            // "close" is the web's own path (M6 6l12 12 M18 6 6 18) rasterised at runtime, so it
            // cannot silently go missing.
            Button exit = UiKit.MakeButton(root, "ExitAr", null,
                                           UiKit.CssFont(14f), UiKit.Glass, UiKit.TextMain,
                                           () => { if (ModeManager.Instance != null) ModeManager.Instance.Exit(); });
            RectTransform ert = exit.GetComponent<RectTransform>();
            ert.anchorMin = new Vector2(1f, 1f);
            ert.anchorMax = new Vector2(1f, 1f);
            ert.pivot = new Vector2(1f, 1f);
            ert.sizeDelta = new Vector2(UiKit.Css(44f), UiKit.Css(44f));
            ert.anchoredPosition = new Vector2(-UiKit.Css(12f), -UiKit.Css(12f));
            Image exitBg = exit.GetComponent<Image>();
            if (exitBg != null)
            {
                exitBg.sprite = UiKit.CircleSprite();   // a round dismiss, not a slab
                exitBg.type = Image.Type.Simple;
            }

            var glyph = new GameObject("Glyph");
            glyph.transform.SetParent(exit.transform, false);
            var grt = glyph.AddComponent<RectTransform>();
            grt.anchorMin = new Vector2(0.5f, 0.5f);
            grt.anchorMax = new Vector2(0.5f, 0.5f);
            grt.pivot = new Vector2(0.5f, 0.5f);
            grt.sizeDelta = new Vector2(UiKit.Css(20f), UiKit.Css(20f));
            grt.anchoredPosition = Vector2.zero;
            var gimg = glyph.AddComponent<Image>();
            gimg.sprite = IconPainter.Get("close");
            gimg.color = UiKit.TextMain;
            gimg.raycastTarget = false;

            // ── the size readout, top-LEFT, where the exit pill used to be ───────
            // In metres, because that is the unit the table is in. It sits away from the fingers:
            // a number under a pinch is a number covered by a hand exactly when it is being read.
            _size = UiKit.MakeLine(root, "ArSize", "", UiKit.CssFont(15f),
                                   TextAnchor.UpperLeft, UiKit.TextMain);
            RectTransform srt = _size.rectTransform;
            srt.anchorMin = new Vector2(0f, 1f);
            srt.anchorMax = new Vector2(0f, 1f);
            srt.pivot = new Vector2(0f, 1f);

            srt.sizeDelta = new Vector2(UiKit.Css(120f), UiKit.RowHeight(UiKit.CssFont(15f), 1));
            srt.anchoredPosition = new Vector2(UiKit.Css(12f), -UiKit.Css(12f));
            _size.gameObject.SetActive(false);

            // ── the action button, bottom centre where the size bar used to be ───
            _action = UiKit.MakeButton(root, "ArAction", "", UiKit.CssFont(15f),
                                       UiKit.Accent, UiKit.OnAccent, OnAction);
            RectTransform art = _action.GetComponent<RectTransform>();
            art.anchorMin = new Vector2(0.5f, 0f);
            art.anchorMax = new Vector2(0.5f, 0f);
            art.pivot = new Vector2(0.5f, 0f);
            art.sizeDelta = new Vector2(UiKit.Css(196f), UiKit.Css(52f));
            art.anchoredPosition = new Vector2(0f, UiKit.Css(28f));
            _action.gameObject.SetActive(false);
            _actionLabel = _action.GetComponentInChildren<Text>();

            // #arhint — the web's one-liner, now carrying the step. Says what to DO, because a user
            // holding a phone at a table needs an instruction, not a label saying they are in AR.
            _hint = UiKit.MakeLine(root, "ArHint", "", UiKit.CssFont(13f),
                                   TextAnchor.MiddleCenter, UiKit.TextDim);
            RectTransform hrt = _hint.rectTransform;
            hrt.anchorMin = new Vector2(0.5f, 0f);
            hrt.anchorMax = new Vector2(0.5f, 0f);
            hrt.pivot = new Vector2(0.5f, 0f);
            hrt.sizeDelta = new Vector2(UiKit.Css(340f), UiKit.RowHeight(UiKit.CssFont(13f), 1));
            hrt.anchoredPosition = new Vector2(0f, UiKit.Css(92f));

            ApplyStep(ArKitSession.Step, ArKitSession.Metres);
        }
    }
}
