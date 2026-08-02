using System;
using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// The tests that matter here are the ones about being LIED TO. Counting black pixels in a
    /// buffer is arithmetic; the reason this class exists is that CI has already shipped a set of
    /// green screenshots of a scene containing none of the models they were supposed to prove
    /// something about. So most of what follows asserts that an empty frame, a frame that could
    /// not be read back, and a model that never arrived all come out FAIL — loudly.
    /// </summary>
    public class QcPixelsTests
    {
        // ── helpers ──────────────────────────────────────────────────────────────

        /// <summary>A flat RGB24 buffer of <paramref name="n"/> pixels.</summary>
        private static byte[] Flat(int n, byte r, byte g, byte b)
        {
            var px = new byte[n * 3];
            for (int i = 0; i < n; i++) { px[i * 3] = r; px[i * 3 + 1] = g; px[i * 3 + 2] = b; }
            return px;
        }

        private static void Paint(byte[] buf, int from, int count, byte r, byte g, byte b)
        {
            for (int i = from; i < from + count; i++) { buf[i * 3] = r; buf[i * 3 + 1] = g; buf[i * 3 + 2] = b; }
        }

        /// <summary>The gradient backdrop stands in as "not the model": mid ocean blue.</summary>
        private static byte[] Water(int n) => Flat(n, 77, 133, 168);

        // ── pure black ───────────────────────────────────────────────────────────

        [Test]
        public void PureBlackCountsOnlyExactZero()
        {
            byte[] frame = Water(100);
            Paint(frame, 0, 25, 0, 0, 0);
            Paint(frame, 25, 25, 1, 0, 0);   // one bit off zero — deliberately NOT pure black
            Assert.AreEqual(25.0, QcPixels.PureBlackPercent(frame), 1e-9);
            Assert.AreEqual(50.0, QcPixels.AtOrBelowPercent(frame, QcPixels.NearBlackMax), 1e-9);
        }

        [Test]
        public void AChannelAboveTheCeilingIsNotBlackHowDarkTheOthersAre()
        {
            // A dark-blue surface underwater is not the bug. Only all three channels down.
            byte[] frame = Flat(10, 0, 0, 40);
            Assert.AreEqual(0.0, QcPixels.PureBlackPercent(frame), 1e-9);
            Assert.AreEqual(0.0, QcPixels.AtOrBelowPercent(frame, QcPixels.NearBlackMax), 1e-9);
        }

        [Test]
        public void EmptyOrRaggedBuffersMeasureZeroRatherThanThrow()
        {
            Assert.AreEqual(0, QcPixels.PixelCount(null));
            Assert.AreEqual(0, QcPixels.PixelCount(new byte[2]));      // not even one pixel
            Assert.AreEqual(0.0, QcPixels.PureBlackPercent(null), 1e-9);
            Assert.AreEqual(1, QcPixels.PixelCount(new byte[5]));      // one whole pixel + change
        }

        // ── the subject mask: what proves a model was in the picture ─────────────

        [Test]
        public void TheModelIsWhateverChangedWhenItWasSwitchedOff()
        {
            byte[] off = Water(200);
            byte[] on = Water(200);
            Paint(on, 0, 60, 30, 60, 40);    // the model covers 60 of 200 px

            QcPixels.Shot s = QcPixels.Measure(on, off);
            Assert.AreEqual(30.0, s.SubjectPercent, 1e-9);
            Assert.AreEqual(200, s.Pixels);
        }

        [Test]
        public void AnEmptyFrameScoresZeroSubjectAndFails()
        {
            // 🔴 THE test. Both frames are the same empty water: the shot is evidence that
            // nothing was photographed, and it must not be reported as a model that is fine.
            byte[] off = Water(500);
            byte[] on = Water(500);

            QcPixels.Shot s = QcPixels.Measure(on, off);
            Assert.AreEqual(0.0, s.SubjectPercent, 1e-9);
            Assert.AreEqual(0.0, s.PureBlackPercent, 1e-9);            // and it looks spotless
            Assert.IsFalse(QcPixels.Passes(loaded: true, renderers: 3, shot: s));
            Assert.AreEqual("not-in-frame", QcPixels.Reason(true, 3, s));
            StringAssert.Contains("loaded=FAIL", QcPixels.Line("msh_lionfish_xr0", true, 3, s));
        }

        [Test]
        public void ASliverOfModelIsStillNotEvidence()
        {
            byte[] off = Water(1000);
            byte[] on = Water(1000);
            Paint(on, 0, 29, 200, 200, 200);   // 2.9% — just under the floor
            QcPixels.Shot s = QcPixels.Measure(on, off);
            Assert.Less(s.SubjectPercent, QcPixels.MinSubjectPercent);
            Assert.IsFalse(QcPixels.Passes(true, 1, s));

            Paint(on, 29, 2, 200, 200, 200);   // 3.1% — over it
            s = QcPixels.Measure(on, off);
            Assert.GreaterOrEqual(s.SubjectPercent, QcPixels.MinSubjectPercent);
            Assert.IsTrue(QcPixels.Passes(true, 1, s));
        }

        [Test]
        public void TheFloorIsAboutTheLoaderNotTheModelsShape()
        {
            // Why the floor is 3 and not 5. A spindle — barracuda, lionfish — projects a
            // silhouette worth about 7% of its own bounding box, so even framed with the box
            // across 90% of the frame it lands near 4.8%. At 5 that was reported as
            // "not-in-frame", i.e. as a failed load, which is a lie about a model that arrived
            // and rendered. At 3 it passes and its blackOfSubject is still worth reading.
            Assert.Less(QcPixels.MinSubjectPercent, 4.8);

            // …and the floor must never be so low that an empty frame or single-pixel noise
            // could clear it. Nothing at all still scores zero and still FAILS.
            byte[] empty = Water(1000);
            QcPixels.Shot none = QcPixels.Measure(empty, Water(1000));
            Assert.AreEqual(0.0, none.SubjectPercent, 1e-9);
            Assert.IsFalse(QcPixels.Passes(true, 1, none));
            Assert.Greater(QcPixels.MinSubjectPercent, 1.0);
        }

        [Test]
        public void RenderNoiseUnderToleranceIsNotAModel()
        {
            byte[] off = Water(100);
            byte[] on = Water(100);
            for (int i = 0; i < 100; i++) on[i * 3] += QcPixels.SubjectTolerance;   // exactly at tol
            Assert.AreEqual(0.0, QcPixels.Measure(on, off).SubjectPercent, 1e-9);

            for (int i = 0; i < 100; i++) on[i * 3] += 1;                            // one over
            Assert.AreEqual(100.0, QcPixels.Measure(on, off).SubjectPercent, 1e-9);
        }

        [Test]
        public void BlackIsMeasuredAgainstTheModelNotTheWholeFrame()
        {
            // A wreck filling a fifth of the frame, entirely black. 20% of the frame reads as a
            // modest-sounding number; 100% of the MODEL is the accusation.
            byte[] off = Water(1000);
            byte[] on = Water(1000);
            Paint(on, 0, 200, 0, 0, 0);

            QcPixels.Shot s = QcPixels.Measure(on, off);
            Assert.AreEqual(20.0, s.PureBlackPercent, 1e-9);
            Assert.AreEqual(20.0, s.SubjectPercent, 1e-9);
            Assert.AreEqual(100.0, s.BlackOfSubjectPercent, 1e-9);
            // It WAS photographed — the framing gate has nothing to complain about.
            Assert.AreEqual(20.0, s.SubjectPercent, 1e-9);
            Assert.GreaterOrEqual(s.SubjectPercent, QcPixels.MinSubjectPercent);
            // …and the mottling gate refuses it anyway, which is the whole point of adding it:
            // an all-black wreck used to score "OK" on everything the pass knew how to ask.
            Assert.IsFalse(QcPixels.Passes(true, 4, s));
            Assert.AreEqual("mottled", QcPixels.Reason(true, 4, s));
        }

        [Test]
        public void MismatchedOrMissingReadbackFailsInsteadOfScoringWell()
        {
            byte[] on = Water(300);
            QcPixels.Shot s = QcPixels.Measure(on, Water(299));   // one pixel short
            Assert.AreEqual(0.0, s.SubjectPercent, 1e-9);
            Assert.IsFalse(QcPixels.Passes(true, 2, s));

            QcPixels.Shot none = QcPixels.Measure(null, null);
            Assert.AreEqual(0, none.Pixels);
            Assert.IsFalse(QcPixels.Passes(true, 2, none));
            Assert.AreEqual("readback-empty", QcPixels.Reason(true, 2, none));
        }

        // ── verdict + log line ───────────────────────────────────────────────────

        [Test]
        public void EveryWayOfHavingNoModelIsItsOwnReason()
        {
            byte[] off = Water(100);
            byte[] on = Water(100);
            // Lit, not black: this test is about the "nothing arrived" reasons, so the model has
            // to be one the brightness gate is happy with or it would answer "mottled" to
            // everything.
            Paint(on, 0, 50, 120, 150, 170);
            QcPixels.Shot good = QcPixels.Measure(on, off);
            Assert.AreEqual(0.0, good.DarkOfSubjectPercent, 1e-9);

            Assert.AreEqual("download-or-parse", QcPixels.Reason(false, 0, good));
            Assert.AreEqual("no-renderer", QcPixels.Reason(true, 0, good));
            Assert.AreEqual("", QcPixels.Reason(true, 5, good));
            Assert.IsFalse(QcPixels.Passes(false, 5, good));   // a 404 can never be OK
        }

        [Test]
        public void TheLineKeepsTheThreeFieldsTheWorkOrderAsksFor()
        {
            byte[] off = Water(1000);
            byte[] on = Water(1000);
            Paint(on, 0, 400, 0, 0, 0);
            QcPixels.Shot s = QcPixels.Measure(on, off);

            // A frame that is 40% pure black is a FAIL now, and the headline fields still read the
            // way the work order fixed them.
            string line = QcPixels.Line("cc0_kraken_xr0", loaded: true, renderers: 7, shot: s);
            StringAssert.StartsWith("[QCModel] cc0_kraken_xr0 pureBlack=40.00% loaded=FAIL", line);
            StringAssert.Contains("subject=40.00%", line);
            StringAssert.Contains("blackOfSubject=100.00%", line);
            StringAssert.Contains("darkOfSubject=100.00%", line);
            StringAssert.Contains("reason=mottled", line);
            StringAssert.Contains("renderers=7", line);

            // The same model, lit: OK, and the dark number is what changed the verdict.
            byte[] lit = Water(1000);
            Paint(lit, 0, 400, 120, 150, 170);
            QcPixels.Shot ok = QcPixels.Measure(lit, off);
            StringAssert.Contains("loaded=OK", QcPixels.Line("cc0_kraken_xr0", true, 7, ok));
            StringAssert.Contains("darkOfSubject=0.00%", QcPixels.Line("cc0_kraken_xr0", true, 7, ok));
        }

        [Test]
        public void ANamelessShotStillPrintsSomethingSearchable()
        {
            string line = QcPixels.Line(null, false, 0, new QcPixels.Shot());
            StringAssert.StartsWith("[QCModel] (unnamed) pureBlack=0.00% loaded=FAIL", line);
            StringAssert.Contains("reason=download-or-parse", line);
        }

        [Test]
        public void PercentagesAlwaysUseADotWhateverTheMachinesLocaleIs()
        {
            // The player runs on a CI box whose culture we do not control, and a log line reading
            // "pureBlack=40,00%" would break every grep written against it.
            // Built by hand rather than by name: this runner is compiled with
            // InvariantGlobalization, so "de-DE" cannot be constructed here — but a comma decimal
            // separator is the whole of what we are defending against.
            var comma = (System.Globalization.CultureInfo)
                System.Globalization.CultureInfo.InvariantCulture.Clone();
            comma.NumberFormat.NumberDecimalSeparator = ",";
            System.Globalization.CultureInfo prev = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = comma;
                byte[] off = Water(10);
                byte[] on = Water(10);
                Paint(on, 0, 4, 0, 0, 0);
                string line = QcPixels.Line("x", true, 1, QcPixels.Measure(on, off));
                StringAssert.Contains("pureBlack=40.00%", line);
            }
            finally { System.Threading.Thread.CurrentThread.CurrentCulture = prev; }
        }

        // ── framing ──────────────────────────────────────────────────────────────

        [Test]
        public void FullFillPutsTheModelsSphereExactlyOnTheFrameEdge()
        {
            // fill = 1 → the bounding sphere's angular radius equals the half-FOV.
            const double fov = 60.0, r = 10.0;
            double d = QcPixels.FrameDistance(r, fov, aspect: 1.0, fill: 1.0);
            double halfAngle = Math.Asin(r / d) * 180.0 / Math.PI;
            Assert.AreEqual(fov / 2.0, halfAngle, 1e-9);
        }

        [Test]
        public void AskingForLessFillStandsFurtherOff()
        {
            double near = QcPixels.FrameDistance(10, 60, 16.0 / 9.0, 0.9);
            double far = QcPixels.FrameDistance(10, 60, 16.0 / 9.0, 0.5);
            Assert.Greater(far, near);
        }

        [Test]
        public void ABiggerModelIsPhotographedFromProportionallyFurtherAway()
        {
            double small = QcPixels.FrameDistance(1, 60, 16.0 / 9.0);
            double big = QcPixels.FrameDistance(50, 60, 16.0 / 9.0);
            Assert.AreEqual(50.0, big / small, 1e-9);   // it is a pure scale — no magic sizes
        }

        [Test]
        public void TheTightAxisWins()
        {
            // A 16:9 frame is tighter vertically, so the vertical FOV sets the distance and the
            // aspect makes no difference beyond 1:1.
            double wide = QcPixels.FrameDistance(10, 60, 16.0 / 9.0);
            double square = QcPixels.FrameDistance(10, 60, 1.0);
            Assert.AreEqual(square, wide, 1e-9);

            // A portrait frame is tighter horizontally — stand further back.
            double portrait = QcPixels.FrameDistance(10, 60, 9.0 / 16.0);
            Assert.Greater(portrait, square);
        }

        [Test]
        public void NonsenseGeometryStillReturnsAUsableDistance()
        {
            Assert.Greater(QcPixels.FrameDistance(0, 60, 1.0), 0.0);      // zero-size model
            Assert.Greater(QcPixels.FrameDistance(10, 0, 0), 0.0);        // no FOV, no aspect
            Assert.Greater(QcPixels.FrameDistance(10, 400, 1.0), 0.0);    // FOV out of range
        }

        // ── mottling ─────────────────────────────────────────────────────────────

        [Test]
        public void PureBlackAtZeroDoesNotMeanTheModelLooksRight()
        {
            // 🔴 The run this metric was added for: 30756993584 came back pureBlack 0.00% and
            // nearBlack 0.00% on all six models, and the kraken and the statue were still covered
            // in hard-edged navy patches. Lifting a black patch to sRGB 45 satisfies every number
            // the pass used to have; it satisfies nothing a human is looking at.
            byte[] off = Water(1000);
            byte[] on = Water(1000);
            Paint(on, 0, 200, 45, 50, 55);      // navy blotch — off zero, still a blotch
            Paint(on, 200, 300, 120, 150, 170); // lit surface

            QcPixels.Shot s = QcPixels.Measure(on, off);
            Assert.AreEqual(0.0, s.PureBlackPercent, 1e-9);
            Assert.AreEqual(0.0, s.NearBlackPercent, 1e-9);
            Assert.AreEqual(0.0, s.BlackOfSubjectPercent, 1e-9);
            // …and the new number is the one that speaks.
            Assert.AreEqual(40.0, s.DarkOfSubjectPercent, 1e-9);
            Assert.IsFalse(QcPixels.Passes(true, 1, s));
            Assert.AreEqual("mottled", QcPixels.Reason(true, 1, s));
        }

        [Test]
        public void TheDarkCeilingSitsBetweenTheFloorAndRealShading()
        {
            // Nothing in the offending shots sits below 40 — that is the ambient + reflection-cube
            // floor, and an offline render of the same models at the same camera reproduces it
            // exactly — so a ceiling at or below 40 would count nothing at all.
            Assert.Greater(QcPixels.DarkMax, 40);
            // And above ~70 it would start counting honest crevice shading as a defect.
            Assert.LessOrEqual(QcPixels.DarkMax, 70);

            // Strictly below the ceiling, on every channel: a pixel that is dark in red but bright
            // in blue is water-coloured shading, not a blotch.
            byte[] off = Water(100);
            byte[] on = Water(100);
            Paint(on, 0, 20, (byte)(QcPixels.DarkMax - 1), (byte)(QcPixels.DarkMax - 1), (byte)(QcPixels.DarkMax - 1));
            Paint(on, 20, 20, 10, 10, 200);   // dark red/green, bright blue — not a blotch
            Paint(on, 40, 20, 120, 150, 170);
            QcPixels.Shot s = QcPixels.Measure(on, off);
            Assert.AreEqual(100.0 * 20 / 60, s.DarkOfSubjectPercent, 1e-9);
        }

        [Test]
        public void TheMottlingGateLeavesRoomForAGenuinelyShadowedModel()
        {
            // An offline render of the kraken — same camera, same lights, same gamma BRDF, with
            // its own sun shadow map — measures 1.32-1.44% dark. Real crevice shading has to pass.
            var honest = new QcPixels.Shot
            { SubjectPercent = 12.5, DarkOfSubjectPercent = 1.44, Pixels = 921600 };
            Assert.IsTrue(QcPixels.Passes(true, 1, honest));
            Assert.AreEqual("", QcPixels.Reason(true, 1, honest));

            // The kraken as CI last photographed it: 14.75%. That has to fail.
            var mottled = new QcPixels.Shot
            { SubjectPercent = 12.56, DarkOfSubjectPercent = 14.75, Pixels = 921600 };
            Assert.IsFalse(QcPixels.Passes(true, 1, mottled));
            Assert.AreEqual("mottled", QcPixels.Reason(true, 1, mottled));

            // The threshold sits between the two, not on top of either.
            Assert.Greater(QcPixels.MaxDarkPercent, 1.44);
            Assert.Less(QcPixels.MaxDarkPercent, 14.75);
        }

        [Test]
        public void AMissingModelIsStillReportedAsMissingNotAsMottled()
        {
            // Order matters in Reason(): an empty frame has 0% dark, so if "mottled" were checked
            // first a model that never arrived would be reported as a lighting problem.
            byte[] empty = Water(1000);
            QcPixels.Shot none = QcPixels.Measure(empty, Water(1000));
            Assert.AreEqual("not-in-frame", QcPixels.Reason(true, 1, none));
            Assert.AreEqual("download-or-parse", QcPixels.Reason(false, 1, none));
        }

        // ── the A/B probe ────────────────────────────────────────────────────────

        private static QcPixels.Shot Probed(double black, double subject = 10.0, int px = 1000)
            => new QcPixels.Shot { BlackOfSubjectPercent = black, SubjectPercent = subject, Pixels = px };

        private static QcPixels.Shot Dark(double dark, double subject = 12.56, int px = 921600)
            => new QcPixels.Shot { DarkOfSubjectPercent = dark, SubjectPercent = subject, Pixels = px };

        [Test]
        public void AProbeOnlyAccusesTheInputTheBlackFollowed()
        {
            // The kraken as it stands: 10.92% black. Clearing the base colour makes it vanish ⇒
            // the texture was carrying it.
            Assert.AreEqual("base-colour-texture",
                QcPixels.ProbeVerdict(Probed(10.92), Probed(0.10), Probed(10.80)));

            // …and the mirror case.
            Assert.AreEqual("metallic-roughness-texture",
                QcPixels.ProbeVerdict(Probed(10.92), Probed(10.70), Probed(0.20)));

            // 🔴 The answer this pass exists to be able to give. Three rounds have been spent
            // nominating a culprit that turned out to be innocent; "neither" has to be a result
            // the harness can report, not a gap it falls through.
            Assert.AreEqual("neither-lighting",
                QcPixels.ProbeVerdict(Probed(10.92), Probed(10.80), Probed(11.10)));

            Assert.AreEqual("both",
                QcPixels.ProbeVerdict(Probed(10.92), Probed(0.05), Probed(0.05)));
        }

        [Test]
        public void ACleanModelIsNotHandedACulprit()
        {
            // htms732 and the lionfish measure 0.00%. A verdict of "base-colour-texture" on a model
            // with nothing wrong would be noise that reads like evidence.
            Assert.AreEqual("no-black",
                QcPixels.ProbeVerdict(Probed(0.0), Probed(0.0), Probed(0.0)));
        }

        [Test]
        public void AProbeFrameThatNeverLandedAcquitsNothing()
        {
            // Pixels == 0 means the readback failed. Treating that as "the black went away" would
            // convict whichever probe crashed.
            Assert.AreEqual("probe-failed",
                QcPixels.ProbeVerdict(Probed(10.92), Probed(0.0, 0.0, 0), Probed(10.8)));
            Assert.AreEqual("probe-failed",
                QcPixels.ProbeVerdict(Probed(10.92), Probed(10.8), Probed(0.0, 0.0, 0)));
        }

        [Test]
        public void TheProbeLineCarriesEveryNumberItsVerdictRestsOn()
        {
            string line = QcPixels.ProbeLine("cc0_kraken_xr0", Probed(10.92, 12.56),
                                             Probed(0.10, 12.56), Probed(10.80, 12.55),
                                             Probed(0.00, 12.56), Probed(0.00, 12.56), Probed(0.00, 12.56),
                                             Probed(0.00, 12.56), Probed(0.00, 12.56));
            StringAssert.StartsWith("[QCProbe] cc0_kraken_xr0", line);
            StringAssert.Contains("base=10.92%", line);
            StringAssert.Contains("whiteAlbedo=0.10%", line);
            StringAssert.Contains("noMetalRough=10.80%", line);
            // The subject percentages ride along because a probe that changed the SILHOUETTE
            // changed more than the one input it was supposed to, and its verdict is worthless.
            StringAssert.Contains("subject base=12.56%", line);
            StringAssert.Contains("verdict=base-colour-texture", line);
        }

        // ── probe 3: where the shading normal comes from ─────────────────────────

        [Test]
        public void StandardCleanAndGltfastMottledConvictsTheShader()
        {
            // The prediction on the table: an offline render of the kraken with nothing but its
            // mesh normals measures 1.44% dark against the app's 14.75%. If Unity's own Standard
            // shader reproduces the clean number on the same mesh and glTFast's does not, the
            // difference is the shader and nothing else — the two frames share the white,
            // textureless material.
            Assert.AreEqual("gltfast-shader",
                QcPixels.NormalSourceVerdict(Dark(14.75), meshNormals: Dark(1.44), whiteGltf: Dark(13.9)));
        }

        [Test]
        public void BothStillMottledSendsUsBackToTheDecoder()
        {
            // No textures, no maps, both shaders — and the patches are still there. Then what is
            // wrong arrived with the geometry, and com.unity.cloud.draco is next.
            Assert.AreEqual("mesh-normals",
                QcPixels.NormalSourceVerdict(Dark(14.75), meshNormals: Dark(13.2), whiteGltf: Dark(14.1)));
        }

        [Test]
        public void BothCleanMeansATextureWasCarryingItAfterAll()
        {
            Assert.AreEqual("textures",
                QcPixels.NormalSourceVerdict(Dark(14.75), meshNormals: Dark(1.2), whiteGltf: Dark(1.3)));
        }

        [Test]
        public void ACleanModelIsNotInterrogated()
        {
            // htms732 measures 0.00% dark. Running a verdict on it would invent a culprit.
            Assert.AreEqual("no-mottle",
                QcPixels.NormalSourceVerdict(Dark(0.0), Dark(0.0), Dark(0.0)));
            Assert.AreEqual("no-mottle",
                QcPixels.NormalSourceVerdict(Dark(QcPixels.MaxDarkPercent), Dark(9.9), Dark(9.9)));
        }

        [Test]
        public void AMagentaOrMissingProbeFrameIsNeverAnAnswer()
        {
            // 🔴 The failure this pass must not have. If the Standard material got stripped from
            // the build the model renders magenta — bright, not dark, so it would score a clean
            // 0.00% and "prove" the shader theory. Pixels==0 (the harness refused to run it) and a
            // silhouette that moved both land on probe-failed instead.
            Assert.AreEqual("probe-failed",
                QcPixels.NormalSourceVerdict(Dark(14.75), meshNormals: default, whiteGltf: Dark(13.9)));
            Assert.AreEqual("probe-failed",
                QcPixels.NormalSourceVerdict(Dark(14.75), meshNormals: Dark(1.44), whiteGltf: default));
            // Silhouette drifted by more than half a percent of the frame: the probe changed what
            // is on screen, so it changed more than the one thing it was meant to.
            Assert.AreEqual("probe-failed",
                QcPixels.NormalSourceVerdict(Dark(14.75), meshNormals: Dark(1.44, 9.0), whiteGltf: Dark(13.9)));

            Assert.IsTrue(QcPixels.SubjectHeld(Dark(14.75), Dark(1.44, 12.56 + 0.4)));
            Assert.IsFalse(QcPixels.SubjectHeld(Dark(14.75), Dark(1.44, 12.56 + 0.9)));
        }

        [Test]
        public void TheProbeLineCarriesTheDarkNumberForEveryFrame()
        {
            string line = QcPixels.ProbeLine("cc0_kraken_xr0",
                Dark(14.75), Dark(9.9), Dark(9.8), Dark(1.44), Dark(13.9), Dark(12.1),
                Dark(13.8), Dark(1.10));
            StringAssert.Contains("darkOfSubject base=14.75%", line);
            StringAssert.Contains("meshNormals=1.44%", line);
            StringAssert.Contains("whiteGltf=13.90%", line);
            StringAssert.Contains("normalVerdict=gltfast-shader", line);
            StringAssert.Contains("greyAlbedo=12.10%", line);
            StringAssert.Contains("mottleVerdict=lighting", line);
            StringAssert.Contains("mipBias-4=13.80%", line);
            StringAssert.Contains("mipBias-10=1.10%", line);
            StringAssert.Contains("biasVerdict=bias-fixes-it", line);
        }

        [Test]
        public void AWhiteAlbedoProbeProvesNothingAndAGreyOneDoes()
        {
            // 🔴 The correction. Run 30759921618 read whiteAlbedo=0.00% as "the base-colour texture
            // is the carrier". In a gamma project the forward pass is albedo × light, so an albedo
            // of 1.0 lifts every pixel over the ceiling whatever the lighting is doing — an offline
            // render of the kraken with the same lights and mesh and only the albedo flattened
            // measures 0.00% at 1.00, 0.00% at 0.80, 0.51% at 0.65 and 3.85% at 0.50. The white
            // frame was measuring the white.
            //
            // MeanAlbedo is the model's own measured brightness, so this probe keeps how bright the
            // model is and removes only how much its texture VARIES.
            Assert.AreEqual(0.65f, QcPixels.MeanAlbedo, 1e-6f);

            // Patches survive a flat albedo of the same brightness ⇒ the texture is innocent.
            Assert.AreEqual("lighting", QcPixels.MottleVerdict(Dark(14.75), Dark(12.1)));
            // Patches follow the texture's own dark regions ⇒ edge dilation at the source.
            Assert.AreEqual("albedo-variation", QcPixels.MottleVerdict(Dark(14.75), Dark(0.9)));
            // Clean model, no interrogation; broken probe, no verdict.
            Assert.AreEqual("no-mottle", QcPixels.MottleVerdict(Dark(0.0), Dark(0.0)));
            Assert.AreEqual("probe-failed", QcPixels.MottleVerdict(Dark(14.75), default));
            Assert.AreEqual("probe-failed", QcPixels.MottleVerdict(Dark(14.75), Dark(0.9, 9.0)));
        }

        [Test]
        public void TheBiasProbeSeparatesADeadApiFromAnInnocentOne()
        {
            // The prediction the arithmetic makes: −4 leaves a seam at LOD 5-6 and changes little,
            // −10 defeats even the worst seam. If −10 comes back clean, a bias IS the fix.
            Assert.AreEqual("bias-fixes-it",
                QcPixels.BiasVerdict(Dark(14.75), bias4: Dark(13.8), bias10: Dark(1.10)));

            // Moved a long way without getting there: mips are involved, a uniform bias is not
            // enough — which is what the seam-LOD arithmetic predicts.
            Assert.AreEqual("bias-helps",
                QcPixels.BiasVerdict(Dark(14.75), bias4: Dark(13.0), bias10: Dark(7.5)));

            // 🔴 Did not move at ALL: either mipMapBias never reached a texture Unity is only
            // wrapping — the same trap graphicsFormat set two rounds ago — or mip selection is
            // innocent. Either way the answer is not "try a bigger number".
            Assert.AreEqual("bias-no-effect",
                QcPixels.BiasVerdict(Dark(14.75), bias4: Dark(14.7), bias10: Dark(14.6)));
            Assert.Greater(QcPixels.BiasMovedPercent, 0.0);

            Assert.AreEqual("no-mottle", QcPixels.BiasVerdict(Dark(0.0), Dark(0.0), Dark(0.0)));
            Assert.AreEqual("probe-failed", QcPixels.BiasVerdict(Dark(14.75), default, Dark(1.1)));
            Assert.AreEqual("probe-failed",
                QcPixels.BiasVerdict(Dark(14.75), Dark(1.1, 9.0), Dark(1.1)));
        }

        // ── framing a box instead of a sphere ────────────────────────────────────

        /// <summary>The QC camera's own basis: three-quarter view from slightly above.</summary>
        private static void QcBasis(out double[] right, out double[] up, out double[] fwd)
        {
            double[] v = { 0.55, 0.32, 1.0 };
            double n = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
            for (int i = 0; i < 3; i++) v[i] /= n;
            fwd = new[] { -v[0], -v[1], -v[2] };
            // right = up × viewDir, normalised
            double[] r = { 1.0 * v[2] - 0.0 * v[1], 0.0 * v[0] - 0.0 * v[2], 0.0 * v[1] - 1.0 * v[0] };
            double rn = Math.Sqrt(r[0] * r[0] + r[1] * r[1] + r[2] * r[2]);
            for (int i = 0; i < 3; i++) r[i] /= rn;
            right = r;
            up = new[]
            {
                v[1] * r[2] - v[2] * r[1],
                v[2] * r[0] - v[0] * r[2],
                v[0] * r[1] - v[1] * r[0],
            };
        }

        private static double BoxDist(double hx, double hy, double hz)
        {
            QcBasis(out double[] r, out double[] u, out double[] f);
            return QcPixels.FrameDistanceForBox(hx, hy, hz,
                                                r[0], r[1], r[2], u[0], u[1], u[2], f[0], f[1], f[2],
                                                60.0, 1280.0 / 720.0);
        }

        [Test]
        public void ALongThinWreckIsFramedMuchCloserThanItsBoundingSphere()
        {
            // sw:htms732 — subject 3.02% and reason=not-in-frame in the first model-QC run. Half
            // extents roughly 30 × 3 × 4: a hull thirty times longer than it is tall. Its bounding
            // sphere radius is ~30, so sphere framing stands off far enough to fit the LENGTH and
            // the silhouette collapses to a splinter.
            double sphereRadius = Math.Sqrt(30 * 30 + 3 * 3 + 4 * 4);
            double sphere = QcPixels.FrameDistance(sphereRadius, 60.0, 1280.0 / 720.0);
            double box = BoxDist(30, 3, 4);

            Assert.Less(box, sphere, "box framing must come closer than sphere framing");
            // Not a rounding difference — the whole point is that the model gets several times
            // more of the frame.
            Assert.Less(box, sphere * 0.75);
        }

        [Test]
        public void NoCornerOfTheBoxIsEverClipped()
        {
            // The one thing that must not happen: Measure() keys the subject off the backdrop, so a
            // model touching the frame edge has no edge to find. Project all eight corners at the
            // returned distance and check every one lands inside the frame with the border intact.
            QcBasis(out double[] r, out double[] u, out double[] f);
            double tanV = Math.Tan(30.0 * Math.PI / 180.0);
            double aspect = 1280.0 / 720.0;
            double tanH = tanV * aspect;

            foreach (double[] h in new[]
                     {
                         new[] { 30.0, 3.0, 4.0 },   // wreck
                         new[] { 1.0, 1.0, 1.0 },    // cube
                         new[] { 0.4, 3.0, 0.3 },    // statue: tall and thin
                         new[] { 6.0, 0.6, 0.5 },    // fish
                     })
            {
                double d = QcPixels.FrameDistanceForBox(h[0], h[1], h[2],
                    r[0], r[1], r[2], u[0], u[1], u[2], f[0], f[1], f[2], 60.0, aspect);
                for (int c = 0; c < 8; c++)
                {
                    double px = ((c & 1) == 0 ? -h[0] : h[0]);
                    double py = ((c & 2) == 0 ? -h[1] : h[1]);
                    double pz = ((c & 4) == 0 ? -h[2] : h[2]);
                    double x = px * r[0] + py * r[1] + pz * r[2];
                    double y = px * u[0] + py * u[1] + pz * u[2];
                    double z = d - (px * -f[0] + py * -f[1] + pz * -f[2]);
                    Assert.Greater(z, 0.0, "corner behind the camera");
                    Assert.LessOrEqual(Math.Abs(x) / z, QcPixels.BoxFill * tanH + 1e-9);
                    Assert.LessOrEqual(Math.Abs(y) / z, QcPixels.BoxFill * tanV + 1e-9);
                }
            }
        }

        [Test]
        public void TheFramingIsTightNotMerelySafe()
        {
            // Exactness, the other half of the previous test: at least one corner must sit ON the
            // fill boundary, or the rule is just "stand back a bit" with extra steps.
            QcBasis(out double[] r, out double[] u, out double[] f);
            double tanV = Math.Tan(30.0 * Math.PI / 180.0);
            double aspect = 1280.0 / 720.0;
            double tanH = tanV * aspect;
            double[] h = { 30.0, 3.0, 4.0 };
            double d = QcPixels.FrameDistanceForBox(h[0], h[1], h[2],
                r[0], r[1], r[2], u[0], u[1], u[2], f[0], f[1], f[2], 60.0, aspect);

            double worst = 0.0;
            for (int c = 0; c < 8; c++)
            {
                double px = ((c & 1) == 0 ? -h[0] : h[0]);
                double py = ((c & 2) == 0 ? -h[1] : h[1]);
                double pz = ((c & 4) == 0 ? -h[2] : h[2]);
                double x = px * r[0] + py * r[1] + pz * r[2];
                double y = px * u[0] + py * u[1] + pz * u[2];
                double z = d - (px * -f[0] + py * -f[1] + pz * -f[2]);
                worst = Math.Max(worst, Math.Max(Math.Abs(x) / (z * tanH), Math.Abs(y) / (z * tanV)));
            }
            Assert.AreEqual(QcPixels.BoxFill, worst, 1e-9);
        }

        [Test]
        public void BiggerModelsAreFramedFurtherAway()
        {
            // Scale invariance: doubling the model doubles the distance, so "fill" means the same
            // fraction of the frame at every size.
            double one = BoxDist(2, 1, 5);
            double two = BoxDist(4, 2, 10);
            Assert.AreEqual(2.0, two / one, 1e-9);
        }

        [Test]
        public void TheModelIsNeverBehindTheCamera()
        {
            // The returned distance has to clear the model's own depth half-extent, or a deep
            // model is framed with its front half behind the lens.
            QcBasis(out double[] r, out double[] u, out double[] f);
            double deep = QcPixels.FrameDistanceForBox(0.01, 0.01, 50,
                                                       r[0], r[1], r[2], u[0], u[1], u[2], f[0], f[1], f[2],
                                                       60.0, 1280.0 / 720.0);
            double halfDepth = 0.01 * Math.Abs(f[0]) + 0.01 * Math.Abs(f[1]) + 50 * Math.Abs(f[2]);
            Assert.Greater(deep, halfDepth);
        }

        [Test]
        public void BoxFramingSurvivesNonsenseToo()
        {
            QcBasis(out double[] r, out double[] u, out double[] f);
            Assert.Greater(QcPixels.FrameDistanceForBox(0, 0, 0,
                               r[0], r[1], r[2], u[0], u[1], u[2], f[0], f[1], f[2], 60.0, 1.0), 0.0);
            Assert.Greater(QcPixels.FrameDistanceForBox(-3, 2, -1,
                               r[0], r[1], r[2], u[0], u[1], u[2], f[0], f[1], f[2], 0.0, 0.0), 0.0);
            Assert.Greater(QcPixels.FrameDistanceForBox(3, 2, 1,
                               r[0], r[1], r[2], u[0], u[1], u[2], f[0], f[1], f[2], 400.0, 1.0, 5.0), 0.0);
        }
    }
}
