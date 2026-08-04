using System;
using UnityEngine;
using UnityEngine.UI;
using DiveMap.Core;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// The loading screen: a full-bleed <c>--bg</c> cover with a label and a progress bar,
    /// shown from the moment a map starts loading until it is playable.
    ///
    /// Web parity (builder.html):
    ///  • <c>#load</c> (builder.html:223) — <c>position:fixed; inset:0; background:#071a2b</c>,
    ///    contents centred, above everything; hidden in one line once the scene is ready
    ///    (builder.html:4431).
    ///  • The v.0668 mini progress ring (builder.html:434-449) is what puts a NUMBER on the
    ///    model downloads: <c>% = done/total</c>, counting failures as done (its error callback
    ///    bumps the same counter as its success callback), fading out 0.35 s after the last one.
    ///  DEVIATION, on purpose: the web draws that number as a 38 px ring in the corner because
    ///  its models stream in lazily behind a map the user can already see. The app cannot show
    ///  the map until the build finishes, so the number belongs on the cover itself — a bar,
    ///  which is what was asked for. Every other value here is the web's.
    ///
    /// 🔴 NEVER in <c>-qcshot</c> mode. QcMapShot builds seven maps and photographs each one;
    /// an opaque #071a2b cover in those frames would fail the whole regression set at once.
    /// The gate is at the point of creation below, so there is no path that can build one.
    ///
    /// All arithmetic lives in <see cref="LoadProgress"/> (Core, UnityEngine-free, tested by
    /// tools/test.sh). This class is sprites, a CanvasGroup and one Update.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LoadOverlay : MonoBehaviour
    {
        private static LoadOverlay _instance;

        private LoadProgress _progress;
        private CanvasGroup _group;
        private Text _label;
        private Text _percent;
        private RectTransform _barTrack;
        private RectTransform _barFill;
        private float _trackWidth;

        /// <summary>The bar's width. The web's modal body is 86vw capped at 380 − 2×20 padding
        /// (builder.html:67); 240 stays inside that on the narrowest phone.</summary>
        private const float BarWidthCss = 240f;
        private const float BarHeightCss = 6f;
        private const float BarRadiusCss = 3f;
        /// <summary>The web's <c>#holostart</c> column gap (builder.html:216).</summary>
        private const float GapCss = 14f;
        /// <summary>Dive-mask logo box = the web's <c>#load</c> spinner slot (.sp 46×46, builder.html:224).</summary>
        private const float LogoCss = 46f;

        /// <summary>
        /// Put the cover up and bind it to the build's live counters. No-op in a QC screenshot
        /// run. <paramref name="progress"/> is owned by <see cref="SceneBuilder"/>, which is the
        /// only thing that knows how many files there are and how many have landed.
        /// </summary>
        public static void Show(LoadProgress progress)
        {
            if (progress == null) return;
            if (IsQcShot)
            {
                // Not even a hidden GameObject: nothing to accidentally enable mid-shot.
                Debug.Log("[UI] -qcshot present → loading overlay disabled for this run");
                return;
            }

            LoadOverlay ui = Ensure();
            if (ui == null) return;

            progress.Show();
            ui._progress = progress;
            ui.gameObject.SetActive(true);
            ui.Refresh();
        }

        /// <summary>The map is playable — fill the bar and fade the cover away.</summary>
        public static void Hide()
        {
            if (_instance == null || _instance._progress == null) return;
            _instance._progress.Complete();
            _instance.Refresh();
        }

        /// <summary>
        /// Take the cover down AT ONCE, without the fade: the load failed and the error modal
        /// underneath is the thing the player now needs to see (and tap).
        /// </summary>
        public static void Cancel()
        {
            if (_instance == null || _instance._progress == null) return;
            _instance._progress.Cancel();
            _instance.Refresh();
            _instance.gameObject.SetActive(false);
        }

        /// <summary>True when this player was launched to take QC screenshots.</summary>
        private static bool IsQcShot
        {
            get { return !string.IsNullOrEmpty(GetArg("-qcshot")); }
        }

        // Same idiom as AppBoot.GetArg / UiShell.GetArg — the convention in this codebase for
        // reading a CLI switch. Duplicated rather than shared so this file has no dependency
        // on either of them (UiShell does not exist at all in a -qcshot run).
        private static string GetArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        private static LoadOverlay Ensure()
        {
            if (_instance != null) return _instance;

            var go = new GameObject("LoadCanvas");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above AppBoot's BootCanvas (0) and the UI shell (10): while the map is loading this
            // is the only thing on screen, and — with blocksRaycasts below — the only thing a
            // finger can hit. Tapping the hamburger through a loading screen used to open the
            // menu over a half-built map.
            canvas.sortingOrder = 100;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();

            _instance = go.AddComponent<LoadOverlay>();
            _instance.Build();
            return _instance;
        }

        private void Build()
        {
            _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 1f;
            _group.blocksRaycasts = true;

            // #load: inset 0, --bg, opaque. Full-bleed on purpose (NOT the shell's safe-area
            // root) — the web's cover runs under the notch too.
            Image bg = UiKit.MakePanel(transform, "Bg", UiKit.ScreenBg);
            UiKit.Stretch(bg.rectTransform);
            bg.raycastTarget = true;   // swallow every tap aimed at the UI behind the cover

            int labelSize = UiKit.CssFont(15f);
            _label = UiKit.MakeText(transform, "Label", UiStrings.Tr("กำลังโหลดแมพ…"),
                                    labelSize, TextAnchor.LowerCenter, UiKit.TextMain);
            RectTransform lRt = _label.rectTransform;
            lRt.anchorMin = new Vector2(0.5f, 0.5f);
            lRt.anchorMax = new Vector2(0.5f, 0.5f);
            lRt.pivot = new Vector2(0.5f, 0f);
            lRt.sizeDelta = new Vector2(UiKit.Css(320f), UiKit.RowHeight(labelSize));
            lRt.anchoredPosition = new Vector2(0f, UiKit.Css(GapCss + BarHeightCss * 0.5f));

            // 🎭 The dive-mask logo, above the label — the brand mark takes the slot the web's
            // #load spinner occupies (.sp, 46×46, builder.html:224). Same artwork as #tourBtn,
            // tinted --txt like the web's inverted-to-white filter.
            Image logo = UiKit.MakePanel(transform, "Logo", UiKit.TextMain);
            logo.sprite = IconPainter.Get("mask");
            logo.type = Image.Type.Simple;
            logo.preserveAspect = true;
            logo.raycastTarget = false;
            RectTransform gRt = logo.rectTransform;
            gRt.anchorMin = new Vector2(0.5f, 0.5f);
            gRt.anchorMax = new Vector2(0.5f, 0.5f);
            gRt.pivot = new Vector2(0.5f, 0f);
            gRt.sizeDelta = new Vector2(UiKit.Css(LogoCss), UiKit.Css(LogoCss));
            gRt.anchoredPosition = new Vector2(
                0f, lRt.anchoredPosition.y + UiKit.RowHeight(labelSize) + UiKit.Css(GapCss));

            // Track: the ring's own track colour, rgba(255,255,255,.18) (builder.html:441).
            Image track = UiKit.MakeRounded(transform, "BarTrack",
                                            new Color(1f, 1f, 1f, 0.18f), BarRadiusCss);
            _barTrack = track.rectTransform;
            _barTrack.anchorMin = new Vector2(0.5f, 0.5f);
            _barTrack.anchorMax = new Vector2(0.5f, 0.5f);
            _barTrack.pivot = new Vector2(0.5f, 0.5f);
            _barTrack.anchoredPosition = Vector2.zero;
            _trackWidth = UiKit.Css(BarWidthCss);
            _barTrack.sizeDelta = new Vector2(_trackWidth, UiKit.Css(BarHeightCss));
            track.raycastTarget = false;

            Image fill = UiKit.MakeRounded(track.transform, "BarFill", UiKit.Accent, BarRadiusCss);
            _barFill = fill.rectTransform;
            _barFill.anchorMin = new Vector2(0f, 0f);
            _barFill.anchorMax = new Vector2(0f, 1f);
            _barFill.pivot = new Vector2(0f, 0.5f);
            _barFill.anchoredPosition = Vector2.zero;
            _barFill.sizeDelta = new Vector2(0f, 0f);
            fill.raycastTarget = false;

            int pctSize = UiKit.CssFont(12f);
            _percent = UiKit.MakeLine(transform, "Percent", "", pctSize,
                                      TextAnchor.UpperCenter, UiKit.TextDim);
            RectTransform pRt = _percent.rectTransform;
            pRt.anchorMin = new Vector2(0.5f, 0.5f);
            pRt.anchorMax = new Vector2(0.5f, 0.5f);
            pRt.pivot = new Vector2(0.5f, 1f);
            pRt.sizeDelta = new Vector2(UiKit.Css(320f), UiKit.RowHeight(pctSize));
            pRt.anchoredPosition = new Vector2(0f, -UiKit.Css(GapCss + BarHeightCss * 0.5f));
        }

        private void Update()
        {
            if (_progress == null) return;

            bool wasFinished = _progress.Finished;
            // Unscaled: a loading screen must keep ticking even if something has paused time.
            bool alive = _progress.Tick(Time.unscaledDeltaTime);
            if (!alive && !wasFinished)
                Debug.LogWarning($"[UI] loading overlay force-hidden after " +
                                 $"{LoadProgress.StuckSeconds:F0}s — the build never reported done");

            Refresh();
            if (!alive) gameObject.SetActive(false);
        }

        /// <summary>Paint the current state (called on show, on hide and every frame between).</summary>
        private void Refresh()
        {
            if (_progress == null) return;

            if (_group != null)
            {
                _group.alpha = _progress.Alpha;
                _group.blocksRaycasts = _progress.BlocksInput;
            }

            if (_barFill != null)
            {
                // Re-read the track width every frame: an orientation change moves CanvasScale,
                // and a bar sized once at boot would be the wrong length for the rest of the run.
                _trackWidth = UiKit.Css(BarWidthCss);
                _barTrack.sizeDelta = new Vector2(_trackWidth, UiKit.Css(BarHeightCss));
                _barFill.sizeDelta = new Vector2(_trackWidth * _progress.Fraction, 0f);
            }

            if (_percent != null)
            {
                // Before the scene is parsed there is no honest number to show — an ellipsis
                // says "working" where a frozen "0%" would say "stuck".
                _percent.text = _progress.Total > 0 || _progress.Finished
                    ? _progress.Percent + "%"
                    : "…";
            }
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
