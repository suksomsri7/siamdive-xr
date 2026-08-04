using System;
using System.Globalization;

namespace DiveMap.Core
{
    /// <summary>
    /// "Did the renderer keep the pattern the texture file has?" — measured on the QC model
    /// frames, in the frame, per model.
    ///
    /// 🔴 WHY THIS EXISTS, and why it is not another darkness metric. The user's report is that
    /// the animals look flat and washed next to the web, and the whale shark is the model that
    /// report is made on: its spots either read or they do not. WO-K chased that all the way down
    /// and the answer was NOT in the files —
    ///
    ///     master 4096² → shipped 2048²   mean abs diff 6.23/255      texture is intact
    ///     spot pattern in the texture     RMS-highpass retention 0.84 pattern is intact
    ///     colour in the texture           ΔRGB 1.95/441               colour is intact
    ///     shipped texture → Unity FRAME   spot retention 0.49         🔴 half the spots gone
    ///
    /// — it is in the last row, which is this renderer. The instrument that produced that last
    /// row was a throwaway python script comparing a CI screenshot against an offline render at a
    /// matched camera (<c>/tmp/fid/band2.py</c>), and a number that only exists in /tmp cannot
    /// gate anything. This class is that measurement, rewritten where CI runs it every time.
    ///
    /// ── what the numbers mean, because ONE of them is a trap ─────────────────────────────────
    /// 🔴 <see cref="Pattern.SpotFrac"/> — the fraction of the model's pixels brighter than their
    /// own neighbourhood by 30% — is a RATIO test, so it falls when the pattern fades AND when the
    /// picture merely gets brighter. Those are different bugs and the same score. Re-measuring
    /// WO-K's own frames with this class's arithmetic separates them:
    ///
    ///     whale shark, same camera        Unity frame     offline render of the file
    ///     mean luminance                   115.05          94.75      🔴 +20.3, +21%
    ///     RMS highpass (absolute)           26.09          25.72      ✅ ratio 1.014
    ///     contrast (hpRms / meanL)           0.2268         0.2715    ratio 0.835
    ///     spotFrac                           0.0621         0.1264    ratio 0.491
    ///
    /// The pattern amplitude, in luminance levels, is INTACT — 26.09 against 25.72. Nothing was
    /// blurred, nothing was mip-dropped, nothing was lost to compression. What happened is that
    /// twenty luminance levels of light were ADDED to every pixel equally, which leaves
    /// <c>L − blur(L)</c> untouched while lifting the <c>1.30 × blur(L)</c> bar it is measured
    /// against by the same 21%. That is the whole of the "51% of the spots are gone" finding: a
    /// uniform additive wash, printed by a multiplicative metric.
    ///
    /// So all four numbers are logged, always, and <see cref="Pattern.MeanL"/> is the one to read
    /// first. A change that fixes the wash moves meanL down and leaves hpRms where it was.
    ///
    /// ── on comparing against <see cref="TryReference"/> ──────────────────────────────────────
    /// ⚠️ The reference row is an OFFLINE RENDER of the shipped GLB at the QC camera, at a
    /// different resolution and with the subject filling a different share of the frame (29% there
    /// against 5% here), and its subject mask was keyed off the background rather than taken from
    /// a model-off twin frame. Its absolute retention ratios therefore carry a real methodology
    /// error and must not be quoted as a gate. Two frames taken by THIS class, on the other hand
    /// — same camera, same mask rule, same blur law — are exactly comparable, so the honest use of
    /// these lines is build-against-build: run, change one thing, run again, read the deltas.
    /// </summary>
    public static class QcFidelity
    {
        /// <summary>
        /// A pixel counts as a "spot" when it is this many times brighter than the local mean.
        /// WO-K's value, kept so the numbers in this file's remark stay comparable.
        /// </summary>
        public const double SpotRatio = 1.30;

        /// <summary>
        /// Blur radius as a fraction of the frame diagonal. Scale-relative on purpose: the
        /// detector then looks for the same physical feature size whatever the frame is, which is
        /// the only way a 1280×720 CI shot and a 720² offline render can be spoken about together.
        /// 0.012 × hypot(1280,720) = 18 px; × hypot(720,720) = 12 px — WO-K's two radii exactly.
        /// </summary>
        public const double BlurDiagonalFraction = 0.012;

        /// <summary>Smallest useful blur radius, for tiny frames in tests.</summary>
        public const int MinBlurRadius = 2;

        /// <summary>
        /// The subject mask is smoothed with this radius and re-thresholded at
        /// <see cref="MaskKeep"/> before it is measured. Not cosmetic: an unsmoothed
        /// difference mask keeps a one-pixel fringe of half-background edge pixels all the way
        /// round the silhouette, and on a 5%-of-frame subject that fringe is a large enough share
        /// of the sample to move both meanL and hpRms on its own.
        /// </summary>
        public const int MaskBlurRadius = 2;

        /// <summary>How much of a pixel's neighbourhood must be subject for it to be measured.</summary>
        public const double MaskKeep = 0.55;

        /// <summary>Bytes per pixel, matching <see cref="QcPixels.Channels"/>.</summary>
        public const int Channels = QcPixels.Channels;

        /// <summary>One model's pattern statistics, measured over the subject only.</summary>
        public struct Pattern
        {
            /// <summary>Pixels that survived the mask. 0 means nothing was measured.</summary>
            public int SubjectPx;

            /// <summary>Mean of (R+G+B)/3 over the subject. 🔴 The additive-wash detector.</summary>
            public double MeanL;

            /// <summary>
            /// RMS amplitude of everything finer than the blur radius, in luminance levels.
            /// Resolution and compression move this; a lighting change must not.
            /// </summary>
            public double HpRms;

            /// <summary><see cref="HpRms"/> / <see cref="MeanL"/> — WO-K's "contrast".</summary>
            public double Contrast;

            /// <summary>Share of subject pixels brighter than <see cref="SpotRatio"/> × local mean.</summary>
            public double SpotFrac;
        }

        /// <summary>An offline render of the same model at the same camera. See the class remark's warning.</summary>
        public struct Reference
        {
            public double MeanL;
            public double HpRms;
            public double Contrast;
            public double SpotFrac;
        }

        /// <summary>
        /// The blur radius for a frame of this size, in pixels — see <see cref="BlurDiagonalFraction"/>.
        /// </summary>
        public static int BlurRadius(int width, int height)
        {
            double diag = Math.Sqrt((double)width * width + (double)height * height);
            int r = (int)Math.Round(BlurDiagonalFraction * diag, MidpointRounding.AwayFromZero);
            return r < MinBlurRadius ? MinBlurRadius : r;
        }

        /// <summary>
        /// Mean of the (2r+1)² neighbourhood, one summed-area table, window clamped at the edges.
        /// (WO-K's numpy version edge-replicates instead; for a replicated border the two agree,
        /// and either way the subject is nowhere near the frame edge.)
        /// </summary>
        public static double[] BoxBlur(double[] a, int width, int height, int radius)
        {
            var outp = new double[width * height];
            if (a == null || a.Length < width * height || width <= 0 || height <= 0) return outp;
            if (radius < 0) radius = 0;

            int sw = width + 1;
            var sat = new double[sw * (height + 1)];
            for (int y = 0; y < height; y++)
            {
                double row = 0.0;
                int src = y * width;
                int cur = (y + 1) * sw;
                int prev = y * sw;
                for (int x = 0; x < width; x++)
                {
                    row += a[src + x];
                    sat[cur + x + 1] = sat[prev + x + 1] + row;
                }
            }

            for (int y = 0; y < height; y++)
            {
                int y0 = y - radius; if (y0 < 0) y0 = 0;
                int y1 = y + radius; if (y1 > height - 1) y1 = height - 1;
                for (int x = 0; x < width; x++)
                {
                    int x0 = x - radius; if (x0 < 0) x0 = 0;
                    int x1 = x + radius; if (x1 > width - 1) x1 = width - 1;
                    double sum = sat[(y1 + 1) * sw + x1 + 1]
                               - sat[y0 * sw + x1 + 1]
                               - sat[(y1 + 1) * sw + x0]
                               + sat[y0 * sw + x0];
                    outp[y * width + x] = sum / ((x1 - x0 + 1) * (y1 - y0 + 1));
                }
            }
            return outp;
        }

        /// <summary>
        /// The model, and nothing else: pixels that differ from the model-off twin frame by more
        /// than <paramref name="tolerance"/> on any channel, then smoothed and re-thresholded to
        /// drop the half-background fringe. Same rule <see cref="QcPixels.Measure"/> keys on, so
        /// the two lines in the log are talking about the same pixels.
        /// </summary>
        public static bool[] SubjectMask(byte[] withModel, byte[] withoutModel, int width, int height,
                                         byte tolerance = QcPixels.SubjectTolerance)
        {
            int n = width * height;
            var keep = new bool[n < 0 ? 0 : n];
            if (n <= 0 || withModel == null || withoutModel == null) return keep;
            if (withModel.Length < n * Channels || withoutModel.Length != withModel.Length) return keep;

            var raw = new double[n];
            for (int i = 0; i < n; i++)
            {
                int p = i * Channels;
                int dr = withModel[p] - withoutModel[p];
                int dg = withModel[p + 1] - withoutModel[p + 1];
                int db = withModel[p + 2] - withoutModel[p + 2];
                if (dr < 0) dr = -dr;
                if (dg < 0) dg = -dg;
                if (db < 0) db = -db;
                raw[i] = (dr > tolerance || dg > tolerance || db > tolerance) ? 1.0 : 0.0;
            }

            double[] soft = BoxBlur(raw, width, height, MaskBlurRadius);
            for (int i = 0; i < n; i++) keep[i] = soft[i] > MaskKeep;
            return keep;
        }

        /// <summary>
        /// Measure one QC model frame against its model-off twin. A readback that went wrong, or a
        /// frame with no model in it, returns an all-zero <see cref="Pattern"/> — never a score.
        /// </summary>
        public static Pattern Measure(byte[] withModel, byte[] withoutModel, int width, int height,
                                      byte tolerance = QcPixels.SubjectTolerance)
        {
            var pat = new Pattern();
            int n = width * height;
            if (n <= 0 || withModel == null || withoutModel == null) return pat;
            if (withModel.Length < n * Channels || withoutModel.Length != withModel.Length) return pat;

            bool[] keep = SubjectMask(withModel, withoutModel, width, height, tolerance);

            var lum = new double[n];
            for (int i = 0; i < n; i++)
            {
                int p = i * Channels;
                lum[i] = (withModel[p] + withModel[p + 1] + withModel[p + 2]) / 3.0;
            }

            double[] lo = BoxBlur(lum, width, height, BlurRadius(width, height));

            int px = 0, spots = 0;
            double sumL = 0.0, sumHp = 0.0, sumHp2 = 0.0;
            for (int i = 0; i < n; i++)
            {
                if (!keep[i]) continue;
                px++;
                double l = lum[i];
                double hp = l - lo[i];
                sumL += l;
                sumHp += hp;
                sumHp2 += hp * hp;
                if (l > lo[i] * SpotRatio) spots++;
            }
            if (px == 0) return pat;

            pat.SubjectPx = px;
            pat.MeanL = sumL / px;
            double meanHp = sumHp / px;
            double var2 = sumHp2 / px - meanHp * meanHp;
            pat.HpRms = var2 <= 0.0 ? 0.0 : Math.Sqrt(var2);
            pat.Contrast = pat.MeanL <= 1e-9 ? 0.0 : pat.HpRms / pat.MeanL;
            pat.SpotFrac = (double)spots / px;
            return pat;
        }

        // ── The offline-render reference rows ────────────────────────────────────────────────

        /// <summary>
        /// The whale shark's shipped GLB rendered offline at the QC camera (yaw 0.503 / pitch
        /// 0.275, the direction <c>QcModelShot</c> frames from), measured by WO-K's
        /// <c>/tmp/fid/band2.py</c> with the arithmetic this class reimplements. Total row, whole
        /// silhouette. See the class remark before quoting the ratios anywhere.
        /// </summary>
        private static readonly Reference Whaleshark = new Reference
        {
            MeanL = 94.75,      // band-count-weighted mean of 72.5 / 66.7 / 77.3 / 112.9 / 131.6
            HpRms = 25.72,      // = contrast 0.2715 × meanL 94.75
            Contrast = 0.2715,
            SpotFrac = 0.1264,
        };

        /// <summary>
        /// The offline reference for <paramref name="assetId"/>, if one has been measured.
        ///
        /// 🔴 Only the whale shark has one, because only the whale shark has been rendered at the
        /// QC camera and measured. Adding a row means rendering that model at
        /// <c>viewDir = normalize(0.55, 0.32, 1)</c> and running the same arithmetic — NOT copying
        /// a neighbour's numbers, which would turn this from evidence into decoration.
        /// </summary>
        public static bool TryReference(string assetId, out Reference reference)
        {
            if (assetId == "msh:whaleshark") { reference = Whaleshark; return true; }
            reference = new Reference();
            return false;
        }

        /// <summary>
        /// One <c>[QCFidelity]</c> line per model. Absolute numbers first — they are the ones that
        /// compare cleanly build to build — then the reference ratios where there is a reference.
        /// </summary>
        public static string Line(string name, string assetId, Pattern pat)
        {
            string head = "[QCFidelity] " + (string.IsNullOrEmpty(name) ? "(unnamed)" : name) +
                          " asset=" + (assetId ?? "(none)") +
                          " px=" + pat.SubjectPx.ToString(CultureInfo.InvariantCulture) +
                          " meanL=" + N(pat.MeanL) +
                          " hpRms=" + N(pat.HpRms) +
                          " contrast=" + N4(pat.Contrast) +
                          " spotFrac=" + N4(pat.SpotFrac);

            if (pat.SubjectPx == 0) return head + " ref=none reason=no-subject";

            Reference r;
            if (!TryReference(assetId, out r)) return head + " ref=none";

            return head +
                   " ref(meanL=" + N(r.MeanL) + " hpRms=" + N(r.HpRms) +
                   " contrast=" + N4(r.Contrast) + " spotFrac=" + N4(r.SpotFrac) + ")" +
                   " wash=" + Signed(pat.MeanL - r.MeanL) +
                   " hpRmsRetention=" + N4(Ratio(pat.HpRms, r.HpRms)) +
                   " contrastRetention=" + N4(Ratio(pat.Contrast, r.Contrast)) +
                   " spotRetention=" + N4(Ratio(pat.SpotFrac, r.SpotFrac));
        }

        private static double Ratio(double a, double b) => b <= 1e-9 ? 0.0 : a / b;

        private static string N(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);

        private static string N4(double v) => v.ToString("0.0000", CultureInfo.InvariantCulture);

        private static string Signed(double v) =>
            (v >= 0 ? "+" : "") + v.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
