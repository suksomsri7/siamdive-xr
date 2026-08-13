using System.Globalization;

namespace DiveMap.Core
{
    /// <summary>
    /// Does a QC frame actually contain the thing it is filed as evidence of?
    ///
    /// 🔴 Why this exists. <c>qc_ui_gizmo_axes.png</c> from run 31513452365 was reported as "three
    /// axis arrows on the selected object". It was a photograph of the drone tour: open water, a
    /// joystick and a compass. Nothing in the harness noticed, because a screenshot call cannot
    /// fail — it writes whatever is on screen, and a wrong screen is still a valid PNG. The rule
    /// this project keeps paying to relearn is that <b>a shot which does not capture the condition
    /// under test proves nothing</b>, and the only way to enforce it is to make the shot check
    /// itself before it is believed.
    ///
    /// 🔴 And the check is a DIFFERENCE, not a colour match. Colour-keying the red/green/blue
    /// arrows would be guessing at what fog, ambient and the unlit shader leave of those colours on
    /// a given map — the same guessing that produced four rounds of arguing about "light shafts"
    /// that were never built. So the harness renders the same camera twice in ONE frame, once with
    /// the handles drawn and once with them hidden, and the pixels that DIFFER are the handles and
    /// nothing else. An empty screen differs from an empty screen by zero pixels, so a shot of the
    /// wrong screen cannot score.
    ///
    /// Pure arithmetic on two byte buffers: <c>Runtime/Ui/QcGizmoWitness.cs</c> owns the Unity half
    /// (RenderTexture, ReadPixels, the projection), this owns the verdict, and the verdict is the
    /// part that can be tested on a machine with no Unity Editor — including the case that matters
    /// most, which is the instrument correctly saying NO.
    /// </summary>
    public static class QcShotProof
    {
        /// <summary>Bytes per pixel in the readback buffers (RGB24).</summary>
        public const int Channels = 3;

        /// <summary>
        /// Per-channel difference that counts a pixel as "the handles". Same value and same reason
        /// as <see cref="QcPixels.SubjectTolerance"/>: llvmpipe is deterministic frame to frame,
        /// but water and fog are recomputed per render and wobble in the low bits.
        /// </summary>
        public const byte Tolerance = 6;

        /// <summary>Samples taken along each arrow.</summary>
        public const int SamplesPerAxis = 24;

        /// <summary>
        /// Half-width of the window searched at each sample, in pixels. The shaft is 3.2 px wide
        /// and the projected line is an approximation of where it lands (the arrow is a cylinder,
        /// not a line), so a hit is "the handles changed something within 4 px of where this axis
        /// is supposed to be" — tight enough that the OTHER two arrows and the plane quads, which
        /// sit 30 px off-axis, cannot supply it.
        /// </summary>
        public const int SampleRadius = 4;

        /// <summary>How many of the samples along one arrow must find it.</summary>
        public const int MinHitsPerAxis = 6;

        /// <summary>
        /// Projected length under which an arrow is not measurable — it is pointing at the lens.
        ///
        /// 🔴 This is not a loophole, it is geometry: the QC pose looks straight down, so the green
        /// Y arrow projects to a few pixels and there is no image of it to find. An axis excused
        /// here is REPORTED as excused, and the verdict still needs two of the three, so a frame
        /// with nothing in it cannot hide behind this.
        /// </summary>
        public const int MinAxisPixels = 14;

        /// <summary>Measurable axes required before the frame is allowed to mean anything.</summary>
        public const int MinMeasurableAxes = 2;

        /// <summary>What one proof frame measured.</summary>
        public struct Axes
        {
            /// <summary>Samples along X/Y/Z that found the handles, −1 when the axis is too
            /// foreshortened to be measurable at all.</summary>
            public int X, Y, Z;
            /// <summary>Projected length of each arrow in pixels, for the log.</summary>
            public double XLen, YLen, ZLen;
            /// <summary>Pixels in the WHOLE frame that the handles changed. Zero means the frame
            /// does not contain the gizmo, whatever else is in it.</summary>
            public int Changed;
            /// <summary>Frame size in pixels, 0 when the buffers were unusable.</summary>
            public int Pixels;
        }

        /// <summary>Pixel count of an RGB24 buffer (0 for null/short input).</summary>
        public static int PixelCount(byte[] rgb)
        {
            if (rgb == null || rgb.Length < Channels) return 0;
            return rgb.Length / Channels;
        }

        /// <summary>
        /// Compare the handles-on frame with its handles-off twin along the three projected arrows.
        ///
        /// All screen coordinates are in the BUFFER's own space — origin at row 0, which for a
        /// Unity readback is the bottom of the picture, the same convention
        /// <c>Camera.WorldToScreenPoint</c> uses. Passing NaN for a point (the handle is behind the
        /// camera) marks that axis unmeasurable rather than sampling a mirrored coordinate on the
        /// wrong side of the screen.
        /// </summary>
        public static Axes Arrows(byte[] withHandles, byte[] withoutHandles, int width, int height,
                                  double ox, double oy,
                                  double xTipX, double xTipY,
                                  double yTipX, double yTipY,
                                  double zTipX, double zTipY,
                                  byte tolerance = Tolerance,
                                  int samples = SamplesPerAxis, int radius = SampleRadius)
        {
            var a = new Axes { X = -1, Y = -1, Z = -1 };
            int n = PixelCount(withHandles);
            if (n == 0 || width <= 0 || height <= 0 || n != width * height) return a;
            if (withoutHandles == null || withoutHandles.Length != withHandles.Length) return a;
            a.Pixels = n;

            var changed = new bool[n];
            int changedCount = 0;
            for (int i = 0; i < n; i++)
            {
                int p = i * Channels;
                int dr = withHandles[p] - withoutHandles[p];
                int dg = withHandles[p + 1] - withoutHandles[p + 1];
                int db = withHandles[p + 2] - withoutHandles[p + 2];
                if (dr < 0) dr = -dr;
                if (dg < 0) dg = -dg;
                if (db < 0) db = -db;
                bool c = dr > tolerance || dg > tolerance || db > tolerance;
                changed[i] = c;
                if (c) changedCount++;
            }
            a.Changed = changedCount;

            a.X = AxisHits(changed, width, height, ox, oy, xTipX, xTipY, samples, radius, out a.XLen);
            a.Y = AxisHits(changed, width, height, ox, oy, yTipX, yTipY, samples, radius, out a.YLen);
            a.Z = AxisHits(changed, width, height, ox, oy, zTipX, zTipY, samples, radius, out a.ZLen);
            return a;
        }

        /// <summary>
        /// How many samples along one arrow land on a changed pixel. Sampling starts at 15% of the
        /// way out and stops at the tip: at the origin all three arrows and all three plane quads
        /// overlap, so a hit there says nothing about WHICH handle was drawn.
        /// </summary>
        private static int AxisHits(bool[] changed, int width, int height,
                                    double ox, double oy, double tx, double ty,
                                    int samples, int radius, out double length)
        {
            length = 0.0;
            if (double.IsNaN(ox) || double.IsNaN(oy) || double.IsNaN(tx) || double.IsNaN(ty))
                return -1;

            double dx = tx - ox, dy = ty - oy;
            length = System.Math.Sqrt(dx * dx + dy * dy);
            if (length < MinAxisPixels) return -1;
            if (samples < 2) samples = 2;

            int hits = 0;
            for (int s = 0; s < samples; s++)
            {
                double t = 0.15 + 0.85 * s / (samples - 1);
                int px = (int)System.Math.Round(ox + dx * t);
                int py = (int)System.Math.Round(oy + dy * t);
                if (Hit(changed, width, height, px, py, radius)) hits++;
            }
            return hits;
        }

        /// <summary>Did anything within <paramref name="radius"/> px of this point change?</summary>
        private static bool Hit(bool[] changed, int width, int height, int px, int py, int radius)
        {
            if (radius < 0) radius = 0;
            for (int y = py - radius; y <= py + radius; y++)
            {
                if (y < 0 || y >= height) continue;
                for (int x = px - radius; x <= px + radius; x++)
                {
                    if (x < 0 || x >= width) continue;
                    if (changed[y * width + x]) return true;
                }
            }
            return false;
        }

        /// <summary>Axes that were long enough on screen to be looked for.</summary>
        public static int Measurable(Axes a)
            => (a.X >= 0 ? 1 : 0) + (a.Y >= 0 ? 1 : 0) + (a.Z >= 0 ? 1 : 0);

        /// <summary>
        /// Is this frame evidence? Every axis that COULD be seen has to have been seen, and at
        /// least two of them had to be visible in the first place.
        /// </summary>
        public static bool Passes(Axes a, int minHits = MinHitsPerAxis,
                                  int minMeasurable = MinMeasurableAxes)
        {
            if (a.Pixels <= 0 || a.Changed <= 0) return false;
            if (Measurable(a) < minMeasurable) return false;
            if (a.X >= 0 && a.X < minHits) return false;
            if (a.Y >= 0 && a.Y < minHits) return false;
            if (a.Z >= 0 && a.Z < minHits) return false;
            return true;
        }

        /// <summary>Why it is not evidence, in one token, or "" when it is.</summary>
        public static string Reason(Axes a, int minHits = MinHitsPerAxis,
                                    int minMeasurable = MinMeasurableAxes)
        {
            if (a.Pixels <= 0) return "readback-empty";
            // The headline case: hiding the handles changed nothing, so they were never drawn.
            if (a.Changed <= 0) return "no-gizmo-in-frame";
            if (Measurable(a) < minMeasurable) return "arrows-off-screen";
            string missing = "";
            if (a.X >= 0 && a.X < minHits) missing += "X";
            if (a.Y >= 0 && a.Y < minHits) missing += "Y";
            if (a.Z >= 0 && a.Z < minHits) missing += "Z";
            return missing.Length == 0 ? "" : "axis-not-drawn:" + missing;
        }

        /// <summary>
        /// The line CI greps. <c>INVALID SHOT</c> is the token that turns the job red, and it is
        /// spelled out in words rather than encoded in an exit code because the thing being
        /// reported is "do not believe this picture" and it has to survive being read by a human
        /// scrolling a 90 000-line player log.
        /// </summary>
        public static string Line(string shot, Axes a, string state,
                                  int minHits = MinHitsPerAxis,
                                  int minMeasurable = MinMeasurableAxes)
        {
            bool ok = Passes(a, minHits, minMeasurable);
            string reason = Reason(a, minHits, minMeasurable);
            return "[QcShot] " + (string.IsNullOrEmpty(shot) ? "(unnamed)" : shot) +
                   " " + (ok ? "proved" : "INVALID SHOT") +
                   " arrows x=" + Hits(a.X) + " y=" + Hits(a.Y) + " z=" + Hits(a.Z) +
                   " of " + SamplesPerAxis.ToString(CultureInfo.InvariantCulture) +
                   " · axisPx x=" + Px(a.XLen) + " y=" + Px(a.YLen) + " z=" + Px(a.ZLen) +
                   " · changed=" + a.Changed.ToString(CultureInfo.InvariantCulture) +
                   " px=" + a.Pixels.ToString(CultureInfo.InvariantCulture) +
                   (string.IsNullOrEmpty(state) ? "" : " · " + state) +
                   (reason.Length == 0 ? "" : " reason=" + reason);
        }

        /// <summary>
        /// The same line for a shot that never got as far as a readback — a hidden gizmo, no
        /// camera, nothing selected. Still spells INVALID SHOT: "the harness could not check" and
        /// "the harness checked and it was fine" must never look the same in a log.
        /// </summary>
        public static string FailedLine(string shot, string reason, string state)
            => "[QcShot] " + (string.IsNullOrEmpty(shot) ? "(unnamed)" : shot) +
               " INVALID SHOT" +
               (string.IsNullOrEmpty(state) ? "" : " · " + state) +
               " reason=" + (string.IsNullOrEmpty(reason) ? "unknown" : reason);

        private static string Hits(int h)
            => h < 0 ? "n/a" : h.ToString(CultureInfo.InvariantCulture);

        private static string Px(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);
    }
}
