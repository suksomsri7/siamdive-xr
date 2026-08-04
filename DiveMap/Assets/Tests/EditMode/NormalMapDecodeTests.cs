using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// The normal-map decode, pinned to numbers.
    ///
    /// 🔴 These tests exist because the LAST argument about normal maps in this project was won by
    /// a comment. The conclusion was right, the remedy — delete every normal map on every model —
    /// was not, and no test in the repo could tell the difference between "the map is decoded
    /// wrongly" and "the map is gone", because neither was ever a number. Every claim below is one.
    /// </summary>
    public class NormalMapDecodeTests
    {
        [Test]
        public void TheTransferFunctionsAreExactInverses()
        {
            // decode(encode(x)) == x across the whole range, including both sides of the 0.0031308
            // knee where the curve changes from linear to a power law. If this ever fails, any
            // shader-side compensation written against these numbers is compensating in the wrong
            // direction and the models get worse, not better.
            for (int i = 0; i <= 1000; i++)
            {
                double x = i / 1000.0;
                Assert.AreEqual(x, NormalMapDecode.SrgbToLinear(NormalMapDecode.LinearToSrgb(x)), 1e-9,
                                $"round trip at {x}");
                Assert.AreEqual(x, NormalMapDecode.LinearToSrgb(NormalMapDecode.SrgbToLinear(x)), 1e-9,
                                $"reverse round trip at {x}");
            }

            // The knee itself, from the sRGB specification.
            Assert.AreEqual(0.04045, NormalMapDecode.LinearToSrgb(0.0031308), 1e-6);
        }

        [Test]
        public void ACorrectlySampledMapUnpacksToUnitVectors()
        {
            byte[] map = SyntheticNormalMap();

            // The whole point of a tangent-space map: every texel is a unit vector. Sampled the
            // right way it still is, to within what 8 bits can express.
            double error = NormalMapDecode.MeanLengthError(map, srgbDecoded: false);
            Assert.Less(error, 0.01,
                        $"a correctly sampled normal map is unit length (mean error {error:F4})");
            Assert.IsTrue(NormalMapDecode.ReadsAsUnitNormals(map, srgbDecoded: false));
        }

        [Test]
        public void AnSrgbDecodeStopsThemBeingNormalsAtAll()
        {
            byte[] map = SyntheticNormalMap();

            // …and sampled the wrong way they are not unit vectors, not approximately, not within
            // any tolerance worth having. This is the measurement that convicts the sRGB path, and
            // it does it from the DATA rather than from the colour space.
            //
            // 🔴 MeanLengthError, not MeanLength. The first version of this test averaged the
            // lengths themselves and the broken reading came back at 1.032 — the stretches and the
            // shrinks cancelling almost exactly — which would have shipped a metric that reports a
            // ruined map as a healthy one.
            double broken = NormalMapDecode.MeanLengthError(map, srgbDecoded: true);
            double fine = NormalMapDecode.MeanLengthError(map, srgbDecoded: false);
            Assert.Greater(broken, 0.1,
                           $"an sRGB-decoded normal map should not read as unit length (got {broken:F3})");
            Assert.Greater(broken, fine * 20.0);
            Assert.IsFalse(NormalMapDecode.ReadsAsUnitNormals(map, srgbDecoded: true));
        }

        [Test]
        public void TheRightSamplingReturnsTheNORMALTHEAUTHORBAKED()
        {
            // 🔴 The criterion the work order asked for, stated properly: decode(encode(n)) ≈ n.
            // Not "the average normal points up" — the fixture below averages 36° off vertical and
            // is entirely correct, because a bake that does not tilt anything is a bake with no
            // detail in it. What has to be small is the error against what the author WROTE.
            byte[] map = SyntheticNormalMap();
            double[] authored = AuthoredNormals();

            double right = MeanAngleErrorDegrees(map, authored, srgbDecoded: false);
            Assert.Less(right, NormalMapDecode.MaxDecodeTiltErrorDegrees,
                        $"correct sampling should return the authored normal (off by {right:F2}°)");
            Assert.Less(right, 0.5, "and in practice only 8-bit rounding is left");

            // Sampled through an sRGB decode it is tens of degrees off, in a direction set by the
            // UV chart the texel happens to live in — which is why the damage shows up as
            // hard-edged polygons rather than as shading.
            double wrong = MeanAngleErrorDegrees(map, authored, srgbDecoded: true);
            Assert.Greater(wrong, 20.0, $"an sRGB decode should ruin the direction (got {wrong:F1}°)");
            Assert.Greater(wrong, right * 20.0);
        }

        [Test]
        public void TheNeutralTexelIsTheHeadlineNumber()
        {
            // (128,128,255) means "do not perturb this surface". Read as stored, it is flat to
            // within a third of a degree — the rest is 8-bit rounding, since 128/255 is not
            // exactly 0.5.
            double right = NormalMapDecode.TiltDegrees(128, 128, 255, srgbDecoded: false);
            Assert.Less(right, 0.5, $"neutral texel should read flat (got {right:F2}°)");

            // Read through an sRGB decode, the same texel tilts most of half a right angle, always
            // toward −tangent/−bitangent. A map that says "nothing here" is then doing more damage
            // than no map at all — which is what the old code measured, and why it chose to delete
            // the maps rather than fix the read.
            double wrong = NormalMapDecode.TiltDegrees(128, 128, 255, srgbDecoded: true);
            Assert.Greater(wrong, 30.0, $"an sRGB decode should tilt the neutral texel (got {wrong:F1}°)");

            // And the fix has to move THAT number, not a percentage somewhere downstream.
            Assert.Greater(wrong - right, 30.0);

            // Across a whole map of neutral texels the same thing holds on average — this is the
            // statistic the app's log line prints, and the shape of the "the map is a flat lie"
            // failure: uniform, so it does not even look like noise.
            var neutral = new byte[64 * 3];
            for (int i = 0; i < 64; i++)
            {
                neutral[i * 3] = 128;
                neutral[i * 3 + 1] = 128;
                neutral[i * 3 + 2] = 255;
            }
            Assert.Less(NormalMapDecode.MeanTiltDegrees(neutral, srgbDecoded: false), 0.5);
            Assert.Greater(NormalMapDecode.MeanTiltDegrees(neutral, srgbDecoded: true), 30.0);
            Assert.AreEqual(0.0, NormalMapDecode.MeanTiltDegrees(null, srgbDecoded: true), 1e-9);
            Assert.AreEqual(0.0, NormalMapDecode.MeanLength(null, srgbDecoded: true), 1e-9);
            Assert.AreEqual(0.0, NormalMapDecode.MeanLengthError(null, srgbDecoded: true), 1e-9);
        }

        /// <summary>
        /// A stand-in for a real bake: unit tangent-space normals encoded the way glTF stores them
        /// (x→R, y→G, z→B, each mapped from [−1,1] to [0,255]). Mostly near-flat with a spread of
        /// real perturbations, which is what a photogrammetry bake looks like — a fixture of pure
        /// neutral texels would let a broken decode pass by symmetry.
        /// </summary>
        private static byte[] SyntheticNormalMap()
        {
            double[] authored = AuthoredNormals();
            int n = authored.Length / 3;
            var rgb = new byte[n * 3];
            for (int i = 0; i < n * 3; i++) rgb[i] = Encode(authored[i]);
            return rgb;
        }

        /// <summary>The unit vectors the fixture map is built from, before encoding.</summary>
        private static double[] AuthoredNormals()
        {
            const int n = 64 * 64;
            var v = new double[n * 3];
            for (int i = 0; i < n; i++)
            {
                // Deterministic, spread across a realistic range of slopes (±0.6 in x and y).
                double x = 0.6 * System.Math.Sin(i * 0.37);
                double y = 0.6 * System.Math.Cos(i * 0.21);
                double zz = 1.0 - (x * x + y * y);
                if (zz < 0.0) zz = 0.0;
                v[i * 3] = x;
                v[i * 3 + 1] = y;
                v[i * 3 + 2] = System.Math.Sqrt(zz);
            }
            return v;
        }

        /// <summary>Mean angle between what the shader gets and what the author wrote.</summary>
        private static double MeanAngleErrorDegrees(byte[] rgb, double[] authored, bool srgbDecoded)
        {
            int n = rgb.Length / 3;
            double sum = 0.0;
            for (int i = 0; i < n; i++)
            {
                NormalMapDecode.Unpack(rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2], srgbDecoded,
                                       out double x, out double y, out double z);
                sum += NormalMapDecode.AngleDegrees(
                    x, y, z, authored[i * 3], authored[i * 3 + 1], authored[i * 3 + 2]);
            }
            return sum / n;
        }

        private static byte Encode(double component)
        {
            double v = (component + 1.0) * 0.5 * 255.0;
            if (v < 0.0) v = 0.0;
            if (v > 255.0) v = 255.0;
            return (byte)System.Math.Round(v);
        }
    }
}
