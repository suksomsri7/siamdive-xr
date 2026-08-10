using System;

namespace DiveMap.Core
{
    /// <summary>
    /// "Is this frame a dead world?" (WO-MERGE P1e)
    ///
    /// The bug this measures is not a crash and not a missing object — it is a whole map rendering
    /// as ONE FLAT COLOUR while every piece of UI over it works perfectly: coins ticking, depth
    /// reading 25.8 m, a minimap dotted with the items that plainly loaded. A screenshot of that
    /// is indistinguishable from "the map failed to load" to a human and to every check this repo
    /// already has, which is why it survived four device rounds.
    ///
    /// Two numbers tell it apart from a healthy frame, and BOTH are needed:
    ///   • <see cref="Frame.MeanLuminance"/> — a fog wall at ambient ×0.32 is dark.
    ///   • <see cref="Frame.StdDev"/> — and it is FLAT. A legitimately dark scene (night, deep
    ///     water) still has a wreck, a seabed edge and a shoal in it, so its luminance varies.
    ///     Mean alone would fail an honest night dive; spread alone would pass a bright grey wall.
    ///
    /// Pure, so the verdict is settled on this machine and only the pixels have to come from CI.
    /// </summary>
    public static class QcBlank
    {
        /// <summary>What one captured frame looks like, in two numbers.</summary>
        public struct Frame
        {
            /// <summary>Mean Rec.601 luminance, 0-255.</summary>
            public double MeanLuminance;

            /// <summary>Standard deviation of that luminance across the frame, 0-255.</summary>
            public double StdDev;

            /// <summary>Pixels measured (0 when the capture failed).</summary>
            public int Pixels;
        }

        /// <summary>
        /// A frame this dark AND this flat is the failure. The thresholds are deliberately far
        /// apart from a real map's numbers rather than tuned to the edge: the drone's
        /// lights-off atmosphere is ambient ×0.32 with fog closing at 200 units, which swallows
        /// everything, while any map that is actually drawing has a lit seabed in the lower half
        /// of the frame and a backdrop gradient in the upper.
        /// </summary>
        public const double BlankMeanMax = 46.0;

        /// <summary>Below this spread the frame carries no shapes at all — see the class note.</summary>
        public const double BlankStdDevMax = 9.0;

        /// <summary>Rec.601, the same weights <c>QcPixels.Luminance</c> uses.</summary>
        public static double Luminance(byte r, byte g, byte b)
            => 0.299 * r + 0.587 * g + 0.114 * b;

        /// <summary>
        /// Measure a tightly-packed RGB24 buffer. Anything malformed (null, not a multiple of 3,
        /// empty) comes back as zero pixels, which <see cref="IsBlank"/> refuses to call a pass or
        /// a failure — a capture that did not happen must never be read as evidence either way.
        /// </summary>
        public static Frame Measure(byte[] rgb)
        {
            var f = new Frame();
            if (rgb == null || rgb.Length < 3) return f;

            int n = rgb.Length / 3;
            double sum = 0.0, sumSq = 0.0;
            for (int i = 0; i < n; i++)
            {
                double l = Luminance(rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2]);
                sum += l;
                sumSq += l * l;
            }

            f.Pixels = n;
            f.MeanLuminance = sum / n;
            // Population variance, clamped: floating-point cancellation can push a perfectly
            // uniform frame a hair below zero and Sqrt would answer NaN — which would then read
            // as "not blank" and pass the very frame this exists to catch.
            double variance = Math.Max(0.0, sumSq / n - f.MeanLuminance * f.MeanLuminance);
            f.StdDev = Math.Sqrt(variance);
            return f;
        }

        /// <summary>Dark AND flat AND actually measured.</summary>
        public static bool IsBlank(Frame f)
            => f.Pixels > 0 && f.MeanLuminance <= BlankMeanMax && f.StdDev <= BlankStdDevMax;

        /// <summary>
        /// The whole positive control in one call.
        ///
        /// 🔴 It asserts BOTH directions, and the first one is the one that matters. A run where
        /// the "before" frame is NOT blank has not proved the fix works — it has proved the
        /// harness cannot see the bug, and a green light from a blind instrument is worse than a
        /// red one. That is why <paramref name="before"/> failing is reported as a broken CONTROL
        /// rather than as a pass.
        /// </summary>
        public static string Verdict(Frame before, Frame after)
        {
            string b = $"before mean={before.MeanLuminance:F1} sd={before.StdDev:F1} px={before.Pixels}";
            string a = $"after  mean={after.MeanLuminance:F1} sd={after.StdDev:F1} px={after.Pixels}";

            if (before.Pixels == 0 || after.Pixels == 0)
                return $"CONTROL-BROKEN no capture · {b} · {a}";

            if (!IsBlank(before))
                return $"CONTROL-BROKEN the bug did not reproduce, so the fix is unproven · {b} · {a}";

            if (IsBlank(after))
                return $"FAIL the map is still a fog wall after the reset · {b} · {a}";

            return $"PASS bug reproduced, then fixed · {b} · {a}";
        }

        /// <summary>True only for the one outcome that means "fixed, and proved fixed".</summary>
        public static bool Passed(Frame before, Frame after)
            => before.Pixels > 0 && after.Pixels > 0 && IsBlank(before) && !IsBlank(after);
    }
}
