using System.Globalization;

namespace DiveMap.Core
{
    /// <summary>
    /// The arithmetic behind the model QC shots (<c>-qcshot</c> → <c>qc_model_*.png</c>).
    ///
    /// 🔴 Why this exists at all. CI photographed a QC scene that pulls exactly four models off
    /// the CDN and builds everything else out of procedural geometry, so the black-skin bug —
    /// which lives in the GLBs' own tangents — could not appear in a CI screenshot even in
    /// principle. Twenty-five green screenshots proved nothing about 222 models nobody had ever
    /// photographed. The counter to that is not "take more pictures"; it is to make the picture
    /// carry a number that a human did not choose after the fact.
    ///
    /// 🔴 And the number has to be able to say "there was nothing there". The lesson this project
    /// keeps re-learning: <b>a shot that passes but contains no model is evidence that nothing was
    /// photographed, not evidence that the model is fine.</b> So every measurement here is taken
    /// against a SECOND frame of the identical camera pose with the model switched off. Pixels
    /// that differ between the two are the model and nothing else — no colour-keying, no guessing
    /// what the backdrop gradient looks like at that pixel, and no way for an empty frame to score
    /// well: an empty frame differs from an empty frame by 0%.
    ///
    /// Pure integers and doubles on purpose: the harness in <c>Runtime/QcModelShot.cs</c> owns the
    /// Unity half (RenderTexture, ReadPixels, PNG), this owns every decision, and the decisions are
    /// the part that can be tested on a machine with no Unity Editor.
    /// </summary>
    public static class QcPixels
    {
        /// <summary>Bytes per pixel in the readback buffers (RGB24).</summary>
        public const int Channels = 3;

        /// <summary>
        /// "Near black" ceiling per channel. Pure (0,0,0) is the headline number because it is
        /// unarguable, but a surface killed by a NaN tangent does not always land on exactly zero
        /// once the fog and the ambient floor have had their say — sRGB 8 is still black to a diver
        /// and nothing lit in this scene ever gets that dark.
        /// </summary>
        public const byte NearBlackMax = 8;

        /// <summary>
        /// Per-channel difference that counts a pixel as "the model, not the backdrop". Not zero:
        /// llvmpipe is deterministic frame to frame, but the water/fog and the gradient backdrop
        /// are recomputed each frame and can wobble by a bit or two in the low bits.
        /// </summary>
        public const byte SubjectTolerance = 6;

        /// <summary>
        /// How much of the frame the model must occupy before the shot is allowed to mean
        /// anything. The framing aims for 45-70% coverage; 5% is "something is genuinely there",
        /// far enough below the target to survive a thin model (the lionfish is mostly fins) and
        /// far enough above zero that a missing model can never sneak through.
        /// </summary>
        public const double MinSubjectPercent = 5.0;

        /// <summary>What one QC shot measured. Percentages are 0..100.</summary>
        public struct Shot
        {
            /// <summary>Exactly (0,0,0) pixels, as a percentage of the WHOLE frame.</summary>
            public double PureBlackPercent;
            /// <summary>Every channel ≤ <see cref="NearBlackMax"/>, as a percentage of the whole frame.</summary>
            public double NearBlackPercent;
            /// <summary>Pixels that changed when the model was switched off — the model's own area.</summary>
            public double SubjectPercent;
            /// <summary>Pure black as a percentage of the MODEL, which is the number that accuses.</summary>
            public double BlackOfSubjectPercent;
            /// <summary>Frame size in pixels, 0 when the buffers were unusable.</summary>
            public int Pixels;
        }

        /// <summary>Pixel count of an RGB24 buffer (0 for null/short/ragged input).</summary>
        public static int PixelCount(byte[] rgb)
        {
            if (rgb == null || rgb.Length < Channels) return 0;
            return rgb.Length / Channels;
        }

        /// <summary>Percentage of pixels that are exactly (0,0,0).</summary>
        public static double PureBlackPercent(byte[] rgb) => AtOrBelowPercent(rgb, 0);

        /// <summary>Percentage of pixels whose every channel is at or below <paramref name="max"/>.</summary>
        public static double AtOrBelowPercent(byte[] rgb, byte max)
        {
            int n = PixelCount(rgb);
            if (n == 0) return 0.0;
            int hit = 0;
            for (int i = 0; i < n; i++)
            {
                int p = i * Channels;
                if (rgb[p] <= max && rgb[p + 1] <= max && rgb[p + 2] <= max) hit++;
            }
            return 100.0 * hit / n;
        }

        /// <summary>
        /// Measure one shot against its model-off twin. Buffers of different lengths (or either
        /// one missing) yield an all-zero result, which <see cref="Passes"/> then refuses — a
        /// readback that went wrong must never be reported as a clean model.
        /// </summary>
        public static Shot Measure(byte[] withModel, byte[] withoutModel,
                                   byte nearBlackMax = NearBlackMax, byte tolerance = SubjectTolerance)
        {
            var shot = new Shot();
            int n = PixelCount(withModel);
            if (n == 0) return shot;
            shot.Pixels = n;
            shot.PureBlackPercent = PureBlackPercent(withModel);
            shot.NearBlackPercent = AtOrBelowPercent(withModel, nearBlackMax);

            if (withoutModel == null || withoutModel.Length != withModel.Length) return shot;

            int subject = 0, blackSubject = 0;
            for (int i = 0; i < n; i++)
            {
                int p = i * Channels;
                int dr = withModel[p] - withoutModel[p];
                int dg = withModel[p + 1] - withoutModel[p + 1];
                int db = withModel[p + 2] - withoutModel[p + 2];
                if (dr < 0) dr = -dr;
                if (dg < 0) dg = -dg;
                if (db < 0) db = -db;
                if (dr <= tolerance && dg <= tolerance && db <= tolerance) continue;
                subject++;
                if (withModel[p] == 0 && withModel[p + 1] == 0 && withModel[p + 2] == 0) blackSubject++;
            }

            shot.SubjectPercent = 100.0 * subject / n;
            shot.BlackOfSubjectPercent = subject == 0 ? 0.0 : 100.0 * blackSubject / subject;
            return shot;
        }

        /// <summary>
        /// Did this shot photograph a model? <paramref name="loaded"/> is the loader's verdict,
        /// <paramref name="renderers"/> what actually landed in the scene, and
        /// <see cref="Shot.SubjectPercent"/> what reached the camera. All three have to agree,
        /// because each of them has been the thing that was wrong: a 404 (loaded), a GLB that
        /// parses to an empty scene (renderers), and a model framed off-screen (subject).
        /// </summary>
        public static bool Passes(bool loaded, int renderers, Shot shot,
                                  double minSubjectPercent = MinSubjectPercent)
            => loaded && renderers > 0 && shot.Pixels > 0 && shot.SubjectPercent >= minSubjectPercent;

        /// <summary>Why it failed, in one token, or "" when it did not.</summary>
        public static string Reason(bool loaded, int renderers, Shot shot,
                                    double minSubjectPercent = MinSubjectPercent)
        {
            if (!loaded) return "download-or-parse";
            if (renderers <= 0) return "no-renderer";
            if (shot.Pixels <= 0) return "readback-empty";
            if (shot.SubjectPercent < minSubjectPercent) return "not-in-frame";
            return "";
        }

        /// <summary>
        /// The QC line, built here so its shape is asserted by a test rather than by whoever reads
        /// the log next. The first three fields are fixed by the work order —
        /// <c>[QCModel] &lt;name&gt; pureBlack=X.XX% loaded=OK|FAIL</c> — and everything after them is
        /// there so a FAIL can be diagnosed without opening the picture.
        /// </summary>
        public static string Line(string name, bool loaded, int renderers, Shot shot,
                                  double minSubjectPercent = MinSubjectPercent)
        {
            bool ok = Passes(loaded, renderers, shot, minSubjectPercent);
            string reason = Reason(loaded, renderers, shot, minSubjectPercent);
            return "[QCModel] " + (string.IsNullOrEmpty(name) ? "(unnamed)" : name) +
                   " pureBlack=" + Pct(shot.PureBlackPercent) +
                   "% loaded=" + (ok ? "OK" : "FAIL") +
                   " subject=" + Pct(shot.SubjectPercent) +
                   "% blackOfSubject=" + Pct(shot.BlackOfSubjectPercent) +
                   "% nearBlack=" + Pct(shot.NearBlackPercent) +
                   "% renderers=" + renderers.ToString(CultureInfo.InvariantCulture) +
                   " px=" + shot.Pixels.ToString(CultureInfo.InvariantCulture) +
                   (reason.Length == 0 ? "" : " reason=" + reason);
        }

        private static string Pct(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);

        // ── framing ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Distance from a model's centre that makes its bounding sphere fill
        /// <paramref name="fill"/> of the frame's smaller dimension.
        ///
        /// The whole point of the new shots is that the model is BIG in them: the existing wide
        /// framing puts a wreck across forty pixels, where a black hull and a dark hull are the
        /// same picture. <paramref name="fill"/> 0.8 leaves a fifth of the frame as backdrop,
        /// which is also what gives <see cref="Measure"/> a clean edge to find.
        /// </summary>
        /// <param name="radius">Bounding-sphere radius of the model, world units.</param>
        /// <param name="verticalFovDegrees">The camera's field of view.</param>
        /// <param name="aspect">Width / height. Below 1 the horizontal axis is the tight one.</param>
        /// <param name="fill">Fraction of the frame the model should span (0..1).</param>
        public static double FrameDistance(double radius, double verticalFovDegrees, double aspect,
                                           double fill = 0.8)
        {
            if (radius <= 0.0) radius = 1.0;
            if (fill <= 0.01) fill = 0.01;
            if (fill > 1.0) fill = 1.0;
            if (aspect <= 0.0) aspect = 1.0;

            double vFov = Clamp(verticalFovDegrees, 1.0, 179.0) * System.Math.PI / 180.0;
            double halfV = vFov * 0.5;
            // Horizontal half-angle for this aspect; the binding one is whichever is smaller.
            double halfH = System.Math.Atan(System.Math.Tan(halfV) * aspect);
            double half = System.Math.Min(halfV, halfH);
            return radius / (fill * System.Math.Sin(half));
        }

        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
