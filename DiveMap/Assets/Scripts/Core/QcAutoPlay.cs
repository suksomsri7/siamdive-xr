namespace DiveMap.Core
{
    /// <summary>
    /// Which command-line runs are NOT allowed to drop themselves into the tour.
    ///
    /// 🔴 Why this is its own rule instead of one more <c>-qcshot</c> check. In run 31513452365 a
    /// single cause took out two separate instruments in the same build:
    ///
    ///   • <c>-qcui</c> — <c>qc_ui_gizmo_axes.png</c> and <c>qc_ui_gizmo.png</c> came back as the
    ///     DRONE TOUR HUD: open water, joysticks, minimap, compass. No selected object, no arrows,
    ///     nothing the work order had built. The log says why —
    ///     <c>[Tour] auto-play → tour at a random spawn</c> arrives after the top-down shot, and
    ///     from there <c>qcui gizmo axes visible=False … mode=Tour</c>: the gizmo deselects itself
    ///     outside View by design, so the harness photographed a screen with nothing on it.
    ///   • <c>-qcblank</c> — the same auto-play landed BETWEEN the positive control's two baseline
    ///     samples and multiplied the second one by the tour's ambient factor: 0.450 → 0.324, which
    ///     is 0.450 × 0.72 exactly. The control reported CONTROL-BROKEN, i.e. the light meter went
    ///     blind, for a reason that had nothing to do with what it was measuring.
    ///
    /// Both harnesses enter the tour THEMSELVES when they want it (UiShell:1630 via
    /// <c>TourController.Start</c>, QcBlankShot:348), so nothing is lost by refusing to do it for
    /// them — what is lost is a mode change arriving 0.6 s into a measurement nobody asked for.
    ///
    /// 🔴 <c>-schoolclip</c> is deliberately NOT on this list. Those clips are the reference the
    /// user judged the fish tuning against, frame by frame; the harness poses the camera itself and
    /// its output has been accepted as-is. Changing what happens underneath it would make the next
    /// clip incomparable with the ones already signed off, and this fix is not about the fish.
    ///
    /// Pure so the list is pinned by a test on this machine rather than by whoever reads
    /// <c>AppBoot</c> next: every entry added here is an instrument that stopped being lied to.
    /// </summary>
    public static class QcAutoPlay
    {
        /// <summary>The QC harness switches that take the tour's hands off the camera.</summary>
        public static readonly string[] SuppressingArgs = { "-qcshot", "-qcui", "-qcblank" };

        /// <summary>
        /// Which switch suppressed the auto-tour, or <c>null</c> for an ordinary run. Returns the
        /// name rather than a bool so the log can say WHICH instrument is holding the camera —
        /// "auto-play suppressed" with no subject is how the last one went unnoticed for a week.
        /// </summary>
        public static string SuppressedBy(string[] args)
        {
            if (args == null) return null;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == null) continue;
                for (int j = 0; j < SuppressingArgs.Length; j++)
                    if (string.Equals(a, SuppressingArgs[j], System.StringComparison.Ordinal))
                        return SuppressingArgs[j];
            }
            return null;
        }

        /// <summary>Is this a QC run that must be left in the mode its harness put it in?</summary>
        public static bool Suppresses(string[] args) => SuppressedBy(args) != null;
    }
}
