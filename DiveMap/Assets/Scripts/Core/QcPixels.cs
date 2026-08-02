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
        /// anything. Far enough above zero that a missing model can never sneak through — an
        /// empty frame scores exactly 0 — and no higher, because this gate is about the LOADER,
        /// not about the model's shape.
        ///
        /// 🔴 Was 5.0, lowered after the first model-QC run. With
        /// <see cref="FrameDistanceForBox"/> putting the bounding box across 90% of the frame,
        /// <c>msh:barracuda</c> still measures ~4.8%: a spindle-shaped fish photographed
        /// three-quarters on has a silhouette worth about 7% of its own bounding box, so there is
        /// no distance at which it reaches 5% without being clipped — and a clipped model has no
        /// backdrop border for <see cref="Measure"/> to key its edge against. Failing it said
        /// "this did not load", which was false. At 3% a 1280×720 shot still hands
        /// <see cref="Shot.BlackOfSubjectPercent"/> ~28,000 pixels to count, which resolves the
        /// black fraction to about a tenth of a percent — far finer than the effect being watched
        /// for (10.04% on the kraken, 12.46% on the statue).
        /// </summary>
        public const double MinSubjectPercent = 3.0;

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

        /// <summary>
        /// Distance that makes a model's BOX — not its bounding sphere — fill <paramref name="fill"/>
        /// of the frame.
        ///
        /// 🔴 Four of the six models in the first model-QC run failed <c>not-in-frame</c> with
        /// subject 1.8-3.9% against a 5% floor, and every one of them is long and thin: a wreck
        /// 60 m end to end and 6 m tall, a lionfish that is mostly fins.
        /// <see cref="FrameDistance"/> frames the bounding SPHERE, whose radius is set by the long
        /// axis, so a 10:1 model is pushed back until its LENGTH spans 80% of the frame and its
        /// silhouette — which is what <see cref="Measure"/> counts — covers a few percent of the
        /// disc. The shot is then technically of the model and useless as evidence.
        ///
        /// This solves the box's eight corners directly: the smallest distance at which every
        /// corner still projects inside <paramref name="fill"/> of the frame. That is exact rather
        /// than conservative — a sphere has to contain the corners with room to spare, this does
        /// not — and because it is a per-corner constraint the model can never be clipped, which
        /// matters more here than the extra pixels: <see cref="Measure"/> needs a clean border of
        /// backdrop around the subject to find its edge at all.
        ///
        /// All vectors are in world space and need not be normalised beyond right/up/forward being
        /// unit and mutually perpendicular — the camera basis, in other words. The camera is
        /// assumed to sit at <c>centre − forward × distance</c> and look at the centre.
        /// </summary>
        /// <param name="halfX">Model half-extent along world X.</param>
        /// <param name="halfY">Model half-extent along world Y.</param>
        /// <param name="halfZ">Model half-extent along world Z.</param>
        /// <param name="rightX">Camera right vector, world space.</param>
        /// <param name="upX">Camera up vector, world space.</param>
        /// <param name="fwdX">Camera forward vector, world space.</param>
        /// <param name="verticalFovDegrees">The camera's field of view.</param>
        /// <param name="aspect">Width / height.</param>
        /// <param name="fill">Fraction of the frame the model should span (0..1).</param>
        public static double FrameDistanceForBox(
            double halfX, double halfY, double halfZ,
            double rightX, double rightY, double rightZ,
            double upX, double upY, double upZ,
            double fwdX, double fwdY, double fwdZ,
            double verticalFovDegrees, double aspect, double fill = BoxFill)
        {
            halfX = System.Math.Abs(halfX);
            halfY = System.Math.Abs(halfY);
            halfZ = System.Math.Abs(halfZ);
            if (fill <= 0.01) fill = 0.01;
            if (fill > 1.0) fill = 1.0;
            if (aspect <= 0.0) aspect = 1.0;

            // A model with no size still has to be photographed from somewhere.
            if (halfX + halfY + halfZ <= 0.0) halfX = halfY = halfZ = 1.0;

            double vFov = Clamp(verticalFovDegrees, 1.0, 179.0) * System.Math.PI / 180.0;
            double tanV = System.Math.Tan(vFov * 0.5);
            double tanH = tanV * aspect;

            double need = 0.0;
            for (int c = 0; c < 8; c++)
            {
                double px = ((c & 1) == 0 ? -halfX : halfX);
                double py = ((c & 2) == 0 ? -halfY : halfY);
                double pz = ((c & 4) == 0 ? -halfZ : halfZ);

                // Corner in camera axes. Depth is measured from the camera, which sits `distance`
                // back along −forward, so a corner leaning toward the lens costs extra distance.
                double x = px * rightX + py * rightY + pz * rightZ;
                double y = px * upX + py * upY + pz * upZ;
                double toward = -(px * fwdX + py * fwdY + pz * fwdZ);

                // |x| ≤ fill·tanH·(d − toward)  ⇒  d ≥ toward + |x|/(fill·tanH). Same for y.
                double dx = toward + System.Math.Abs(x) / (fill * tanH);
                double dy = toward + System.Math.Abs(y) / (fill * tanV);
                if (dx > need) need = dx;
                if (dy > need) need = dy;
            }
            return need;
        }

        /// <summary>
        /// How much of the frame the bounding BOX may span. Higher than <see cref="FrameDistance"/>'s
        /// 0.8 on purpose: the box is a tight bound and its extreme corner is exactly where this
        /// puts it, so 0.9 still leaves a 5% border of backdrop on every side for
        /// <see cref="Measure"/> to key against, where the sphere rule wastes that margin on empty
        /// space the model never reaches.
        /// </summary>
        public const double BoxFill = 0.9;

        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
