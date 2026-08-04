using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// The relief probe — the picture-side half of the normal-map work, and the half that can fail
    /// on the device where the arithmetic cannot.
    ///
    /// 🔴 Why this measurement had to be invented. Every existing QC number in this project counts
    /// DARK pixels, because every previous investigation was about black patches. The report this
    /// change answers is a different one — "ผิวแบน/เหลี่ยม ไม่สมูทเหมือน Meshy" — and a flat,
    /// evenly lit animal scores perfectly on all of them. A metric that cannot see the symptom
    /// cannot confirm the fix, and the last five builds were judged by exactly such metrics.
    /// </summary>
    public class NormalReliefTests
    {
        private const int W = 32, H = 32;

        [Test]
        public void DetailInTheShadingRaisesTheScore()
        {
            byte[] empty = Background();
            byte[] flat = Subject(detail: 0);
            byte[] detailed = Subject(detail: 20);

            double flatScore = QcPixels.ReliefScore(flat, empty, W, H);
            double detailedScore = QcPixels.ReliefScore(detailed, empty, W, H);

            Assert.Greater(detailedScore, flatScore,
                           "high-frequency shading is what a normal map contributes");
            Assert.Greater(detailedScore, flatScore * QcPixels.ReliefLiveRatio);
        }

        [Test]
        public void TheVerdictNamesWhatTheModelLooksLike()
        {
            byte[] empty = Background();
            byte[] flat = Subject(detail: 0);
            byte[] detailed = Subject(detail: 20);

            QcPixels.Shot detailedShot = QcPixels.Measure(detailed, empty);
            QcPixels.Shot flatShot = QcPixels.Measure(flat, empty);

            double withMap = QcPixels.ReliefScore(detailed, empty, W, H);
            double withoutMap = QcPixels.ReliefScore(flat, empty, W, H);

            // The map is bound and it is doing something: this is the answer the fix has to
            // produce on the whale shark and the manta.
            Assert.AreEqual("normal-map-live",
                            QcPixels.ReliefVerdict(withMap, withoutMap, detailedShot, flatShot));

            // Removing the map changed nothing — the map is reaching the shader as a flat lie,
            // which is what an sRGB-decoded neutral map does once the tilt is uniform.
            Assert.AreEqual("normal-map-dead",
                            QcPixels.ReliefVerdict(withoutMap, withoutMap, flatShot, flatShot));

            // Present but barely earning its memory: reported as itself rather than rounded to a
            // side, because "the fix half worked" is a real outcome and the one most likely to be
            // argued about.
            Assert.AreEqual("normal-map-weak",
                            QcPixels.ReliefVerdict(withoutMap * 1.08, withoutMap, flatShot, flatShot));
        }

        [Test]
        public void AProbeThatMovedTheModelAnswersNothing()
        {
            byte[] empty = Background();
            byte[] detailed = Subject(detail: 20);
            QcPixels.Shot shot = QcPixels.Measure(detailed, empty);

            // A probe frame whose silhouette moved changed more than the one thing it was meant
            // to, and its ratio is meaningless — the same rule the other probes already follow.
            var moved = new QcPixels.Shot { Pixels = shot.Pixels, SubjectPercent = shot.SubjectPercent + 5.0 };
            Assert.AreEqual("probe-failed", QcPixels.ReliefVerdict(2.0, 1.0, shot, moved));

            // A frame that never landed likewise.
            Assert.AreEqual("probe-failed",
                            QcPixels.ReliefVerdict(2.0, 1.0, shot, new QcPixels.Shot()));
            Assert.AreEqual("probe-failed", QcPixels.ReliefVerdict(0.0, 1.0, shot, shot));

            // And a model that never had a normal map is not a failure and not a dead map. It is
            // a model with no normal map, and saying so stops it being counted as evidence either
            // way — several models in the QC set are exactly this.
            Assert.AreEqual("no-normal-map",
                            QcPixels.ReliefVerdict(1.0, 1.0, shot, shot, hadNormalMap: false));
        }

        [Test]
        public void OnlyStepsInsideTheModelAreCounted()
        {
            // Deliberately no gradient at all inside the subject: every step measured must be 0.
            // If the silhouette edge were being counted, this would score the full 128-vs-30 jump
            // and a long thin animal would read as the most detailed thing in the frame.
            byte[] empty = Background();
            byte[] flat = Subject(detail: 0, gradient: false);
            Assert.AreEqual(0.0, QcPixels.ReliefScore(flat, empty, W, H), 1e-9);
        }

        [Test]
        public void UnmeasurableInputScoresZeroRatherThanLying()
        {
            byte[] empty = Background();
            byte[] detailed = Subject(detail: 20);

            Assert.AreEqual(0.0, QcPixels.ReliefScore(null, empty, W, H), 1e-9);
            Assert.AreEqual(0.0, QcPixels.ReliefScore(detailed, null, W, H), 1e-9);
            Assert.AreEqual(0.0, QcPixels.ReliefScore(detailed, empty, W, H + 1), 1e-9, "size mismatch");
            Assert.AreEqual(0.0, QcPixels.ReliefScore(detailed, new byte[9], W, H), 1e-9, "ragged pair");
            Assert.AreEqual(0.0, QcPixels.ReliefScore(detailed, empty, 1, 1), 1e-9);
        }

        /// <summary>A frame with nothing in it: uniform, well away from black.</summary>
        private static byte[] Background()
        {
            var rgb = new byte[W * H * QcPixels.Channels];
            for (int i = 0; i < rgb.Length; i++) rgb[i] = 30;
            return rgb;
        }

        /// <summary>
        /// The same frame with a square "model" in the middle at a mid grey, optionally with a
        /// slow lighting gradient across it (the shading the MESH carries) and optionally with a
        /// per-pixel detail term (the shading a normal map adds).
        /// </summary>
        private static byte[] Subject(int detail, bool gradient = true)
        {
            byte[] rgb = Background();
            for (int y = 8; y < 24; y++)
            {
                for (int x = 8; x < 24; x++)
                {
                    int v = 120;
                    if (gradient) v += (y - 8) / 4;             // ≈0.25 of a level per pixel step
                    if (detail > 0 && ((x + y) & 1) == 1) v += detail;
                    int p = (y * W + x) * QcPixels.Channels;
                    rgb[p] = rgb[p + 1] = rgb[p + 2] = (byte)v;
                }
            }
            return rgb;
        }
    }
}
