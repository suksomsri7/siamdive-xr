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
        /// <summary>
        /// 🔴 Raised 46 → 70 (WO-MERGE P1h), and this is arithmetic, not tuning.
        ///
        /// A fully fogged frame cannot be darker than the fog COLOUR, and the drone's lights-off
        /// fog colour is <c>DiveLightMath.HeadlightOff</c> = (0.078, 0.271, 0.353) — a mid navy
        /// whose own Rec.601 luminance is ≈56.8 out of 255. So a gate at 46 could never be
        /// satisfied by the very condition it was built to detect, no matter what else was true:
        /// a PERFECT reproduction of the bug would still have been reported as CONTROL-BROKEN.
        ///
        /// The 46 came from a summary comment above that struct which says "ambient ×0.32"; the
        /// field beside it says <c>AmbientMul = 0.55f</c>. I read the comment and not the code —
        /// the exact trap this repo has a rule about.
        ///
        /// Both ends of the new number have evidence: the floor is the shipped fog colour's own
        /// luminance (56.8) plus margin, and the ceiling is a MEASURED healthy frame from CI run
        /// 31458246375 — mean 185.8 and 186.8 on the two passes. 70 sits far from both.
        /// <see cref="DiveMap.Tests"/> pins the floor so it cannot drift back under.
        /// </summary>
        public const double BlankMeanMax = 70.0;

        /// <summary>
        /// Below this spread the frame carries no shapes at all — see the class note.
        ///
        /// ⚠️ UNVALIDATED, deliberately left alone this round. Nobody has yet measured what a real
        /// fogged-out frame's spread actually is; the healthy frames measured 52-70. The dark
        /// probe added in P1h photographs a forced lights-off atmosphere and prints its mean AND
        /// its sd, so the next round can set this from data instead of from anyone's expectation.
        /// </summary>
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
                return $"{ControlBroken} no capture · {b} · {a}";

            if (!IsBlank(before))
                return $"{ControlBroken} the bug did not reproduce, so the fix is unproven · {b} · {a}";

            if (IsBlank(after))
                return $"FAIL the map is still a fog wall after the reset · {b} · {a}";

            return $"PASS bug reproduced, then fixed · {b} · {a}";
        }

        /// <summary>True only for the one outcome that means "fixed, and proved fixed".</summary>
        public static bool Passed(Frame before, Frame after)
            => before.Pixels > 0 && after.Pixels > 0 && IsBlank(before) && !IsBlank(after);

        /// <summary>
        /// Every verdict that is not a real answer starts with this, so one grep finds them all.
        /// </summary>
        public const string ControlBroken = "CONTROL-BROKEN";

        /// <summary>
        /// The verdict when a pass ran out of wall clock (WO-MERGE P1f).
        ///
        /// 🔴 A control that HANGS is a blind instrument, and by this project's own rule a blind
        /// instrument is worse than a red light: CI run 31442231470 was cancelled at 155 minutes
        /// with no verdict at all, and it took the unrelated palette screenshots in the same job
        /// down with it. So the budget is not a safety net around the harness, it is part of the
        /// harness: when it expires the run says so, in the same file and the same format as every
        /// other outcome, and stops. Red and legible beats green and absent — and beats hung.
        /// </summary>
        /// <summary>
        /// The claim this control can actually make (WO-MERGE DARK).
        ///
        /// 🔴 Mean-frame luminance turned out to be blind here, and the harness proved it on
        /// itself. CI b383's dark probe forced the drone's full lights-off atmosphere straight
        /// into RenderSettings and photographed it: <c>mean=181.6</c>, against a healthy frame's
        /// <c>186.9</c>. Five parts in 255. The frame is dominated by something fog and ambient do
        /// not touch — the unlit screen-space backdrop quad — so no threshold over that number
        /// could ever have separated a dark world from a bright one, however faithful the sequence
        /// leading up to it.
        ///
        /// The same run measured the thing that IS real, on the same frames: with the reset
        /// suppressed the tour's dimmed ambient survived into the next map (sky 41.8), and with it
        /// running the next map got the authored value back (sky 93.7). That is precisely what
        /// <c>SceneAtmosphere</c> promises, it is a factor of 2.2, and it is measurable without a
        /// camera at all.
        ///
        /// So the claim is narrowed on purpose: <b>a new map's build restores the authored
        /// ambient</b>. It does not prove the user's dark screen — nothing in this harness does —
        /// and the verdict says so in as many words rather than implying more than it earned.
        /// </summary>
        /// <remarks>
        /// 🔴 WHAT THESE TWO NUMBERS MUST BE (WO-MERGE DARK, after b384).
        ///
        /// b384 read <c>authored=0.450 before=0.167 after=0.369</c> and called it a FAIL. The
        /// reproduction was real — that was the first time in this whole effort the bug appeared
        /// in an instrument — but the comparison was wrong: it measured the ambient LIVE in
        /// RenderSettings, which is several transformations downstream of the thing the fix
        /// promises. The chain is
        ///
        ///     authored ──restore──▶ base ──× depth factor──▶ written ──underwater raise──▶ live
        ///
        /// and only the FIRST arrow belongs to <c>SceneAtmosphere</c>. The other two are working
        /// features: the depth scale is why deep water looks deep, and it varies with wherever the
        /// camera happens to sit at capture. Comparing <c>live</c> to <c>authored</c> therefore
        /// asserts that the depth scale does nothing, which would be a bug if it were true.
        ///
        /// So the caller passes the BASELINE (<c>DepthAtmosphere.BaseSkyGray</c>) — the surface
        /// value everything downstream is computed from. It is depth-independent by construction,
        /// which is exactly what makes the assertion mean the same thing on every run instead of
        /// drifting into meaninglessness the moment a camera moves.
        /// </remarks>
        public static string AtmosphereVerdict(double beforeSky, double afterSky,
                                               double authoredSky, double tolerance = 0.02)
        {
            string n = $"authored={authoredSky:F3} before={beforeSky:F3} after={afterSky:F3}";

            if (authoredSky <= 0.0)
                return $"{ControlBroken} no authored snapshot to compare against · {n}";

            bool beforeDrifted = System.Math.Abs(beforeSky - authoredSky) > tolerance;
            bool afterRestored = System.Math.Abs(afterSky - authoredSky) <= tolerance;

            if (!beforeDrifted)
                return $"{ControlBroken} the suppressed pass did not drift, so the fix is " +
                       $"unproven · {n}";

            if (!afterRestored)
                return $"FAIL the build did not restore the authored ambient · {n}";

            return $"PASS ambient drift reproduced, then restored by the build · {n} " +
                   "(claim: a build restores the authored ambient — NOT that the device's dark " +
                   "screen is fixed)";
        }

        /// <summary>True only when the narrowed claim is both reproduced and fixed.</summary>
        public static bool AtmospherePassed(double beforeSky, double afterSky, double authoredSky,
                                            double tolerance = 0.02)
            => authoredSky > 0.0 &&
               System.Math.Abs(beforeSky - authoredSky) > tolerance &&
               System.Math.Abs(afterSky - authoredSky) <= tolerance;

        public static string BudgetVerdict(string pass, float seconds)
            => $"{ControlBroken} pass '{pass}' exceeded its {seconds:F0}s budget — " +
               "no frame was measured, so nothing is proven either way";
    }
}
