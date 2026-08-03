using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// WO-E5 — the fog, as arithmetic.
    ///
    /// 🔴 THE BUG THESE TESTS EXIST FOR. The user's report was
    /// "ตอนนี้กลุ่มหมอกก็ไม่เห็นเลย กลายเป็นว่าฉากไม่มีมิติเลย … ก้อนหินไกลยังคมชัดไม่จมหมอก",
    /// and it was not a matter of taste. The shipped range was the web's own linear fog — near 500
    /// u, far 9,000 u — scaled by <see cref="DepthLight.VisibilityScale"/>. At the 61.8 m in their
    /// screenshot that is 322.6 u to 5,805.4 u, on a map whose seabed half-width is
    /// <see cref="SeabedGeom.SandRadius"/> = 340 u. The FURTHEST anything on that map could be from
    /// a diver inside it is ~680 u, which lands at 6.5% fog; the rock they were looking at, at
    /// ~200 u, was at exactly 0%.
    ///
    /// <see cref="ShippedRangeCouldNotReachTheMap"/> is that calculation, kept as a test rather
    /// than as a paragraph, so that if anybody ever puts the web's orbit-framing constants back on
    /// the diver's camera the number that convicts them is already written down.
    /// </summary>
    [TestFixture]
    public class FogRangeTests
    {
        /// <summary>The depth the user was standing at, in world units.</summary>
        private const float PosidonDepth = 61.8f * DepthLight.UnitsPerMetre;

        [Test]
        public void ShippedRangeCouldNotReachTheMap()
        {
            // The old line, verbatim: the web's constants times the depth's visibility.
            float vis = DepthLight.VisibilityScale(PosidonDepth);
            float oldStart = 500f * vis, oldEnd = 9000f * vis;

            Assert.That(vis, Is.EqualTo(0.645f).Within(0.005f), "visibility scale at 61.8 m");
            Assert.That(oldStart, Is.EqualTo(322.6f).Within(2f));
            Assert.That(oldEnd, Is.EqualTo(5805f).Within(20f));

            // A diver in the middle of a 340 u map: the far rim is 2 R away at the very most.
            float farRim = 2f * SeabedGeom.SandRadius;
            float atFarRim = WaterFog.FactorAt(farRim, oldStart, oldEnd);
            Assert.That(atFarRim, Is.LessThan(0.10f),
                "the whole map lived inside the first tenth of the old fog ramp");

            // …and the object actually in the screenshot, at ~200 u, got literally none of it.
            Assert.That(WaterFog.FactorAt(200f, oldStart, oldEnd), Is.EqualTo(0f),
                "the old fog started BEYOND most of the map");
        }

        [Test]
        public void RangeAtCoversTheMapFromInsideIt()
        {
            // A diver near the middle: camera-to-centre is small, so the floor decides the range.
            WaterFog.RangeAt(PosidonDepth, SeabedGeom.SandRadius, 40f,
                             out float start, out float end);

            float nearThing = 100f, midThing = 300f, farRim = 2f * SeabedGeom.SandRadius;
            float fNear = WaterFog.FactorAt(nearThing, start, end);
            float fMid = WaterFog.FactorAt(midThing, start, end);
            float fFar = WaterFog.FactorAt(farRim, start, end);

            // The requirement in one line: things get hazier with distance, measurably, across the
            // map's own scale — and the far rim SINKS rather than vanishes.
            Assert.That(fNear, Is.LessThan(0.10f), "nearby objects must stay crisp");
            Assert.That(fMid, Is.GreaterThan(0.20f), "mid-distance must read as hazy");
            Assert.That(fFar, Is.EqualTo(WaterFog.FarRimFog).Within(0.01f),
                "the far rim is 80% water by construction — FarRimReach is solved for exactly that");
            Assert.That(fFar, Is.LessThan(1.0f), "…but never disappears from it entirely");
            Assert.That(fMid - fNear, Is.GreaterThan(0.15f), "the ramp has to be readable, not token");
        }

        [Test]
        public void TheFarRimSurvivesEveryDepth()
        {
            // The promise the floor exists to make. The old range multiplied the map's own size by
            // the depth's visibility, so the deeper the diver went the shorter the reach got — and
            // a reach shorter than the map is a shorter draw distance wearing a fog's clothes.
            for (float metres = 0f; metres <= 90f; metres += 10f)
            {
                WaterFog.RangeAt(metres * DepthLight.UnitsPerMetre, SeabedGeom.SandRadius, 0f,
                                 out float s, out float e);
                float f = WaterFog.FactorAt(2f * SeabedGeom.SandRadius, s, e);
                Assert.That(f, Is.EqualTo(WaterFog.FarRimFog).Within(0.01f),
                    $"far rim at {metres} m must stay readable, not be erased");
            }
        }

        [Test]
        public void PullingBackDoesNotBlankTheMap()
        {
            // The web's orbit framing: 950 u out from a 340 u map. This is the pose the existing
            // wide QC screenshots are taken from and the one nobody has complained about, so the
            // property that matters is that the change cannot hurt it.
            const float orbitDistance = 950f;
            WaterFog.RangeAt(0f, SeabedGeom.SandRadius, orbitDistance,
                             out float start, out float end);

            float nearRim = orbitDistance - SeabedGeom.SandRadius;
            float farRim = orbitDistance + SeabedGeom.SandRadius;
            Assert.That(WaterFog.FactorAt(nearRim, start, end), Is.LessThan(0.5f),
                "the near half of the map must still be clearly readable from orbit");
            Assert.That(WaterFog.FactorAt(farRim, start, end), Is.LessThan(1.0f),
                "the far rim must not be erased at orbit distance");
        }

        [Test]
        public void FlyingIntoTheMiddleDoesNotCloseTheWaterOnYourFace()
        {
            // Without the 2 R floor the range would follow the camera all the way to zero and the
            // fog would tighten onto the diver as they reached the centre. Pinned: the range does
            // not move at all anywhere inside the map's own footprint.
            WaterFog.RangeAt(PosidonDepth, SeabedGeom.SandRadius, 0f, out float s0, out float e0);
            WaterFog.RangeAt(PosidonDepth, SeabedGeom.SandRadius, 300f, out float s1, out float e1);
            Assert.That(s1, Is.EqualTo(s0).Within(1e-3f));
            Assert.That(e1, Is.EqualTo(e0).Within(1e-3f));
        }

        [Test]
        public void DeepWaterCloudsInAndTheShallowsStayClear()
        {
            // The depth cue the user explicitly asked to KEEP — "ตื้นอ่อน ลึกเข้ม". It lives in the
            // fog's COLOUR rather than in its range, so that the range only ever has one job. The
            // water a distant rock fades into has to be measurably darker at 61.8 m than at the
            // surface, in LIGHT rather than in the authored numbers.
            SeabedGeom.Rgb shallow = WaterFog.ColorAt(0f);
            SeabedGeom.Rgb deep = WaterFog.ColorAt(PosidonDepth);
            float lShallow = ToneMap.SrgbToLinear(shallow.R) * 0.2126f
                           + ToneMap.SrgbToLinear(shallow.G) * 0.7152f
                           + ToneMap.SrgbToLinear(shallow.B) * 0.0722f;
            float lDeep = ToneMap.SrgbToLinear(deep.R) * 0.2126f
                        + ToneMap.SrgbToLinear(deep.G) * 0.7152f
                        + ToneMap.SrgbToLinear(deep.B) * 0.0722f;
            Assert.That(lDeep, Is.LessThan(lShallow * 0.6f),
                "the deep must fade things into much less light than the shallows do");

            // …and bluer, not just dimmer: red goes first underwater and that is the hue cue.
            float shallowBR = ToneMap.SrgbToLinear(shallow.B) / ToneMap.SrgbToLinear(shallow.R);
            float deepBR = ToneMap.SrgbToLinear(deep.B) / ToneMap.SrgbToLinear(deep.R);
            Assert.That(deepBR, Is.GreaterThan(shallowBR), "the deep fog must be bluer");
        }

        [Test]
        public void PullingBackAtDepthSeesLessThanPullingBackAtTheSurface()
        {
            // The camera term is where the depth still shortens the reach — it is only the floor
            // that is protected from it, and the floor only bites inside the map's own footprint.
            const float orbit = 950f;
            WaterFog.RangeAt(0f, SeabedGeom.SandRadius, orbit, out _, out float surfaceEnd);
            WaterFog.RangeAt(PosidonDepth, SeabedGeom.SandRadius, orbit, out _, out float deepEnd);
            Assert.That(deepEnd, Is.LessThan(surfaceEnd * 0.8f),
                "an orbit view at 61.8 m must not see as far as one at the surface");
        }

        [Test]
        public void RangeSurvivesNonsense()
        {
            // Depth is a subtraction of two world positions and the radius comes off a bounding
            // box that can legitimately be empty; neither may produce a NaN fog range, because a
            // NaN fog distance renders the whole frame one flat colour.
            WaterFog.RangeAt(float.NaN, 0f, float.NaN, out float s, out float e);
            Assert.That(float.IsNaN(s), Is.False);
            Assert.That(float.IsNaN(e), Is.False);
            Assert.That(e, Is.GreaterThan(s));
            Assert.That(e, Is.GreaterThan(0f));
        }

        // ── the fog COLOUR ───────────────────────────────────────────────────────

        [Test]
        public void FadingTowardTheWrongRowOfTheRampIsWhatMakesDistantThingsBlack()
        {
            // The failure, in one comparison. #123a55 is the ramp at v = 0.90, which is where the
            // far rim of the map lands on screen for a camera that looks DOWN at it from 950 u
            // away — the web's. A diver looks roughly level and their far rim lands near
            // mid-frame, where the same ramp carries several times the light. Fading a distant
            // rock toward the deep stop while it stands against the mid ramp is what turns haze
            // into darkening, which is exactly what the user photographed.
            SeabedGeom.Rgb atDiver = WaterFog.ColorAt(PosidonDepth, 0.5f);
            SeabedGeom.Rgb atWeb = WaterFog.ColorAt(PosidonDepth, WaterFog.FogRampV);

            // In LIGHT, not in the authored numbers — that distinction is the whole of ToneMap.
            float blueDiver = ToneMap.SrgbToLinear(atDiver.B);
            float blueWeb = ToneMap.SrgbToLinear(atWeb.B);
            Assert.That(blueDiver, Is.GreaterThan(blueWeb * 3f),
                "the water a diver actually looks into carries several times the light #123a55 does");
        }

        [Test]
        public void RampVIsTheScreenRowFlippedAndClamped()
        {
            // The backdrop's v = 0 is the TOP of the frame; Unity's viewport y = 0 is the BOTTOM.
            // Getting this the wrong way round would fade distant geometry toward the SURFACE
            // colour — a much prettier bug, and just as wrong — so the flip is pinned.
            Assert.That(WaterFog.RampVOfViewportY(1f), Is.EqualTo(0f).Within(1e-5f),
                "top of the frame is ramp position 0, the surface colour");
            Assert.That(WaterFog.RampVOfViewportY(0f), Is.EqualTo(1f).Within(1e-5f),
                "bottom of the frame is ramp position 1, the deep stop");
            Assert.That(WaterFog.RampVOfViewportY(0.5f), Is.EqualTo(0.5f).Within(1e-5f));

            // Off-frame and behind-camera projections must not become fog colours.
            Assert.That(WaterFog.RampVOfViewportY(3f), Is.EqualTo(0f).Within(1e-5f));
            Assert.That(WaterFog.RampVOfViewportY(-3f), Is.EqualTo(1f).Within(1e-5f));
            Assert.That(WaterFog.RampVOfViewportY(0.2f, behindCamera: true),
                        Is.EqualTo(WaterFog.FogRampV),
                "a point behind the camera projects to a mirrored y — the honest answer is the web's");
            Assert.That(WaterFog.RampVOfViewportY(float.NaN), Is.EqualTo(WaterFog.FogRampV));
        }

        [Test]
        public void ColorAtDefaultIsStillTheWebsFog()
        {
            // Nothing that already reasons about #123a55 may move: the one-argument overload has
            // to stay bit-for-bit what it was.
            SeabedGeom.Rgb a = WaterFog.ColorAt(PosidonDepth);
            SeabedGeom.Rgb b = WaterFog.ColorAt(PosidonDepth, WaterFog.FogRampV);
            Assert.That(a.R, Is.EqualTo(b.R).Within(0.01f));
            Assert.That(a.G, Is.EqualTo(b.G).Within(0.01f));
            Assert.That(a.B, Is.EqualTo(b.B).Within(0.01f));
        }
    }

    /// <summary>
    /// WO-E5 follow-up — the two ways run 30800189252's <c>[QCMap]</c> lines were unreadable.
    /// </summary>
    [TestFixture]
    public class MapShotMeasureTests
    {
        private const int W = 8, H = 40;

        /// <summary>A frame whose SCREEN top is bright and whose SCREEN bottom is dark, laid out
        /// bottom-up the way <c>Texture2D.GetRawTextureData</c> does it: buffer row 0 is dark.</summary>
        private static byte[] BottomUpRamp(byte darkAtBottom, byte brightAtTop)
        {
            var b = new byte[W * H * 3];
            for (int y = 0; y < H; y++)
            {
                double t = (double)y / (H - 1);            // 0 = buffer row 0 = screen bottom
                byte v = (byte)(darkAtBottom + (brightAtTop - darkAtBottom) * t);
                for (int x = 0; x < W; x++)
                {
                    int p = ((y * W) + x) * 3;
                    b[p] = v; b[p + 1] = v; b[p + 2] = v;
                }
            }
            return b;
        }

        [Test]
        public void ABottomUpReadbackOfAHealthyRampScoresPositive()
        {
            // 🔴 This is the exact bug. Run 30800189252 reported −0.552 / −0.576 / −0.681 on
            // Atlantis, Posidon and Chang, and it read as "the water is upside down". It was not:
            // the buffer is bottom-up, the subtraction was not, and the magnitude was right all
            // along. Told the row order, the same data comes back positive.
            byte[] frame = BottomUpRamp(20, 220);

            double span = QcPixels.BackdropSpanOf(frame, W, H, bottomUpRows: true,
                                                  out double top, out double bottom);
            Assert.That(span, Is.GreaterThan(0.0), "bright above, dark below is a healthy ramp");
            Assert.That(top, Is.GreaterThan(bottom));
            Assert.That(span, Is.GreaterThan(QcPixels.MinBackdropSpan));

            // …and the old reading of the very same bytes, for the record.
            double wrongWayRound = QcPixels.BackdropSpanOf(frame, W, H, bottomUpRows: false,
                                                           out _, out _);
            Assert.That(wrongWayRound, Is.EqualTo(-span).Within(1e-9),
                "the two conventions differ by exactly a sign — the magnitude was never in doubt");
        }

        [Test]
        public void AGenuinelyInvertedRampIsStillCaught()
        {
            // The reason the sign is not simply thrown away: a picture that really is bright below
            // and dark above must fail, and must fail with a verdict that says so rather than with
            // "flat-water", which would send the next reader looking at the gradient stops.
            byte[] inverted = BottomUpRamp(220, 20);   // buffer row 0 (screen bottom) is the bright end
            double span = QcPixels.BackdropSpanOf(inverted, W, H, bottomUpRows: true, out _, out _);
            Assert.That(span, Is.LessThan(-QcPixels.MinBackdropSpan));

            var m = new QcPixels.MapShot
            {
                Pixels = W * H, Renderers = 5, SubjectPercent = 40.0,
                WaterLuminance = 0.3, FogWork = 2.0, BackdropSpan = span,
            };
            Assert.That(QcPixels.MapReason(m), Is.EqualTo("ramp-upside-down"));
        }

        [Test]
        public void APoseWithNoWaterInItIsAPoseFailureNotAFogFailure()
        {
            // 🔴 Hanuman, run 30800189252: waterLum=0.000 subject=100.00% fogWork=0.07, reported
            // as "no-fog". True, and misleading: with the whole frame scored as subject there was
            // no background left for the fog to be measured against, so the number said nothing
            // about the fog and everything about the camera being inside the map.
            var buried = new QcPixels.MapShot
            {
                Pixels = 1000, Renderers = 50, SubjectPercent = 100.0,
                WaterLuminance = 0.0, FogWork = 0.07, BackdropSpan = 0.5,
            };
            Assert.That(QcPixels.MapReason(buried), Is.EqualTo("camera-buried"),
                "a frame that is all subject has to accuse the pose, not the shading");

            // …and the same failure arriving as darkness rather than as coverage.
            var noWater = new QcPixels.MapShot
            {
                Pixels = 1000, Renderers = 50, SubjectPercent = 60.0,
                WaterLuminance = 0.001, FogWork = 0.07, BackdropSpan = 0.5,
            };
            Assert.That(QcPixels.MapReason(noWater), Is.EqualTo("no-water"));

            // A healthy frame still reaches the fog verdict, or the gates above have simply
            // swallowed the thing this pass exists to measure.
            var fogless = new QcPixels.MapShot
            {
                Pixels = 1000, Renderers = 50, SubjectPercent = 40.0,
                WaterLuminance = 0.30, FogWork = 0.07, BackdropSpan = 0.5,
            };
            Assert.That(QcPixels.MapReason(fogless), Is.EqualTo("no-fog"));
        }

        [Test]
        public void MeasureMapReportsBothEndsSoASignNeverHasToBeInterpreted()
        {
            byte[] frame = BottomUpRamp(20, 220);
            QcPixels.MapShot m = QcPixels.MeasureMap(frame, frame, frame, W, H,
                                                     QcPixels.SubjectTolerance, bottomUpRows: true);
            Assert.That(m.BandTop, Is.GreaterThan(m.BandBottom));
            Assert.That(m.BackdropSpan, Is.EqualTo(m.BandTop - m.BandBottom).Within(1e-9));
        }
    }
}
