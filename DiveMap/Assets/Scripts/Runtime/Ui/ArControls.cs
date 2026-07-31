using DiveMap.Core;
using UnityEngine;
using UnityEngine.UI;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// F4 — the AR overlay: the size bar, the way out, and the one line of guidance.
    ///
    /// The web (builder.html:349, CSS :97):
    /// <code>
    ///   &lt;div id="arctl"&gt;&lt;button id="arMinus"&gt;−&lt;/button&gt;&lt;span&gt;ขนาด&lt;/span&gt;
    ///                        &lt;button id="arPlus"&gt;＋&lt;/button&gt;&lt;/div&gt;
    ///   #arctl{ left:50%; bottom: calc(28px + env(safe-area-inset-bottom)); }
    ///   body.holo #arctl{ display:none !important }
    /// </code>
    ///
    /// The last line matters and is easy to skim past: AR and holomap are separate modes with
    /// separate bars. When holomap is built (deferred — docs/WO-AR-HOLOMAP.md), it gets its own
    /// controls; merging them would put a "ขนาด" stepper on a mode whose zoom works differently.
    ///
    /// Deliberate additions over the web, both because AR is the one mode with no other chrome on
    /// screen: the buttons grey out at the zoom stops (the web lets you shrink the site to 8 cm
    /// with no clue how to get back), and the hint line names the gesture rather than the feature.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArControls : MonoBehaviour
    {
        private static ArControls _open;

        /// <summary>QC surface — the bar is on screen.</summary>
        public static bool IsOpen => _open != null;

        private Text _minus, _plus;

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

        /// <summary>Re-read the zoom stops after a − / + press.</summary>
        public static void Refresh() => _open?.UpdateLimits();

        private void OnDestroy()
        {
            if (_open == this) _open = null;
        }

        private void Build(RectTransform root)
        {
            // ✕ ออก AR — top-left, where the tour's exit lives, so leaving is in the same place
            // whichever mode swallowed the screen.
            Button exit = UiKit.MakeButton(root, "ExitAr", UiStrings.Tr("✕ ออก AR"),
                                           UiKit.CssFont(14f), UiKit.Glass, UiKit.TextMain,
                                           () => { if (ModeManager.Instance != null) ModeManager.Instance.Exit(); });
            RectTransform ert = exit.GetComponent<RectTransform>();
            ert.anchorMin = new Vector2(0f, 1f);
            ert.anchorMax = new Vector2(0f, 1f);
            ert.pivot = new Vector2(0f, 1f);
            ert.sizeDelta = new Vector2(UiKit.Css(112f), UiKit.Css(40f));
            ert.anchoredPosition = new Vector2(UiKit.Css(12f), -UiKit.Css(12f));

            // ── the size bar, centred at the bottom like the web's #arctl ────────
            Image bar = UiKit.MakeRounded(root, "ArCtl", UiKit.Glass, 24f);
            RectTransform brt = bar.rectTransform;
            brt.anchorMin = new Vector2(0.5f, 0f);
            brt.anchorMax = new Vector2(0.5f, 0f);
            brt.pivot = new Vector2(0.5f, 0f);
            brt.sizeDelta = new Vector2(UiKit.Css(196f), UiKit.Css(52f));
            brt.anchoredPosition = new Vector2(0f, UiKit.Css(28f));

            _minus = Step(bar.transform, "ArMinus", "−", 0f, () => Zoom(false));
            Text label = UiKit.MakeLine(bar.transform, "Label", UiStrings.Tr("ขนาด"),
                                        UiKit.CssFont(14f), TextAnchor.MiddleCenter, UiKit.TextDim);
            RectTransform lrt = label.rectTransform;
            lrt.anchorMin = new Vector2(0.5f, 0.5f);
            lrt.anchorMax = new Vector2(0.5f, 0.5f);
            lrt.pivot = new Vector2(0.5f, 0.5f);
            lrt.sizeDelta = new Vector2(UiKit.Css(80f), UiKit.Css(24f));
            lrt.anchoredPosition = Vector2.zero;
            _plus = Step(bar.transform, "ArPlus", "＋", 1f, () => Zoom(true));

            // #arhint — the web's one-liner. Says what to DO ("point the camera at a flat
            // surface"), because a user holding a phone at a reef needs an instruction, not a
            // label saying they are in AR mode.
            Text hint = UiKit.MakeLine(root, "ArHint", UiStrings.Tr("เล็งกล้องไปที่พื้นเรียบ"),
                                       UiKit.CssFont(13f), TextAnchor.MiddleCenter, UiKit.TextDim);
            RectTransform hrt = hint.rectTransform;
            hrt.anchorMin = new Vector2(0.5f, 0f);
            hrt.anchorMax = new Vector2(0.5f, 0f);
            hrt.pivot = new Vector2(0.5f, 0f);
            hrt.sizeDelta = new Vector2(UiKit.Css(280f), UiKit.Css(22f));
            hrt.anchoredPosition = new Vector2(0f, UiKit.Css(88f));

            UpdateLimits();
        }

        private Text Step(Transform parent, string name, string glyph, float side, UnityEngine.Events.UnityAction on)
        {
            Button b = UiKit.MakeButton(parent, name, glyph, UiKit.CssFont(22f),
                                        new Color(1f, 1f, 1f, 0.08f), UiKit.TextMain, on);
            RectTransform rt = b.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(side, 0.5f);
            rt.anchorMax = new Vector2(side, 0.5f);
            rt.pivot = new Vector2(side, 0.5f);
            rt.sizeDelta = new Vector2(UiKit.Css(48f), UiKit.Css(40f));
            rt.anchoredPosition = new Vector2(side == 0f ? UiKit.Css(6f) : -UiKit.Css(6f), 0f);
            return b.GetComponentInChildren<Text>();
        }

        private static void Zoom(bool closer)
        {
            if (ArSession.Instance != null) ArSession.Instance.Zoom(closer);
        }

        /// <summary>Grey out whichever end the user has reached — the web shows no stop at all.</summary>
        private void UpdateLimits()
        {
            double scale = ArSession.Scale, fit = ArSession.FitScale;
            if (_minus != null)
                _minus.color = ArPlacement.AtLimit(scale, fit, false) ? UiKit.TextDim : UiKit.TextMain;
            if (_plus != null)
                _plus.color = ArPlacement.AtLimit(scale, fit, true) ? UiKit.TextDim : UiKit.TextMain;
        }
    }
}
