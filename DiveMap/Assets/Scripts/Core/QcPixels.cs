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

        /// <summary>
        /// Brightest-channel ceiling for "this pixel reads as a dark blotch, not as shading".
        /// Chosen off the measured floor rather than by taste: in the offending shots NOTHING on
        /// any model sits below 40 (the ambient + reflection-cube floor, and an offline render of
        /// the same models at the same camera reproduces that floor exactly), while the patches a
        /// human objected to sit in the 40-60 band. Below 40 there is nothing to count and above
        /// 60 real crevice shading starts.
        /// </summary>
        public const byte DarkMax = 60;

        /// <summary>
        /// Fallback dark ceiling for a model with no recorded baseline. Deliberately loose: it is
        /// the "something is catastrophically wrong" line for a model nobody has looked at yet,
        /// not a quality bar. Models we HAVE looked at are judged against their own baseline
        /// through <see cref="DarkGate"/>.
        /// </summary>
        public const double MaxDarkPercent = 70.0;

        /// <summary>
        /// 🔴 Why this is not one absolute number any more. The 2.5% ceiling was calibrated in run
        /// 30756993584, while the import was still stripping metallic-roughness maps — which lifted
        /// every model into a pale, washed-out grey. With the maps restored (run 30768353482, the
        /// one a human passed by eye and shipped to TestFlight) the models render their real
        /// materials: Poseidon's dark green stone measures 31% below the dark ceiling, HTMS 732's
        /// camouflage 62%. Both are correct and both failed the gate. A dark model is not a broken
        /// model.
        ///
        /// And a single-image spatial metric cannot tell the two apart — that was measured, not
        /// assumed. Local contrast (how much darker a pixel is than its own neighbourhood, the most
        /// promising of the candidates) scores the CLEAN barracuda at 16.73% and the MOTTLED
        /// hardeep at 1.07%: the classes overlap, because a barracuda's dark bands and a blotch
        /// look the same to any operator that only sees one frame. Hard-edge fraction and
        /// histogram bimodality were measured on the same nine frames and overlap too.
        ///
        /// So the gate asks the only question that has a right answer: HAS THIS MODEL GOT DARKER
        /// THAN THE LAST TIME SOMEBODY LOOKED AT IT. Both conditions have to trip — a relative jump
        /// so a large baseline needs a real change, and an absolute one so a near-zero baseline is
        /// not set off by llvmpipe noise.
        /// </summary>
        public static double DarkGate(double baselinePercent)
        {
            if (baselinePercent < 0.0) return MaxDarkPercent;   // no baseline recorded
            return System.Math.Max(baselinePercent * DarkRegressionFactor,
                                   baselinePercent + DarkRegressionSlack);
        }

        /// <summary>A baseline may grow by this factor before it counts as a regression.</summary>
        public const double DarkRegressionFactor = 1.4;

        /// <summary>…and by at least this many points, so a 0.25% baseline needs a real jump.</summary>
        public const double DarkRegressionSlack = 3.0;

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
            /// <summary>
            /// Percentage of the MODEL whose brightest channel is below <see cref="DarkMax"/> —
            /// the mottling number. Pure black going to 0.00% is not the same thing as the model
            /// looking right, and the run that proved it is 30756993584: every model came back
            /// pureBlack 0.00% and nearBlack 0.00%, and the kraken and the statue were still
            /// covered in hard-edged navy patches a human spotted immediately. A metric that can
            /// be satisfied by lifting the patches one shade off zero is a metric that will be
            /// gamed by accident.
            /// </summary>
            public double DarkOfSubjectPercent;
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

            int subject = 0, blackSubject = 0, darkSubject = 0;
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
                if (withModel[p] < DarkMax && withModel[p + 1] < DarkMax && withModel[p + 2] < DarkMax) darkSubject++;
            }

            shot.SubjectPercent = 100.0 * subject / n;
            shot.BlackOfSubjectPercent = subject == 0 ? 0.0 : 100.0 * blackSubject / subject;
            shot.DarkOfSubjectPercent = subject == 0 ? 0.0 : 100.0 * darkSubject / subject;
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
                                  double minSubjectPercent = MinSubjectPercent,
                                  double maxDarkPercent = MaxDarkPercent)
            => loaded && renderers > 0 && shot.Pixels > 0 && shot.SubjectPercent >= minSubjectPercent
               && shot.DarkOfSubjectPercent <= maxDarkPercent;

        /// <summary>Why it failed, in one token, or "" when it did not.</summary>
        public static string Reason(bool loaded, int renderers, Shot shot,
                                    double minSubjectPercent = MinSubjectPercent,
                                    double maxDarkPercent = MaxDarkPercent)
        {
            if (!loaded) return "download-or-parse";
            if (renderers <= 0) return "no-renderer";
            if (shot.Pixels <= 0) return "readback-empty";
            if (shot.SubjectPercent < minSubjectPercent) return "not-in-frame";
            // Last, because it is the only one that says the model ARRIVED and still looks wrong.
            if (shot.DarkOfSubjectPercent > maxDarkPercent) return "darker-than-baseline";
            return "";
        }

        /// <summary>
        /// The QC line, built here so its shape is asserted by a test rather than by whoever reads
        /// the log next. The first three fields are fixed by the work order —
        /// <c>[QCModel] &lt;name&gt; pureBlack=X.XX% loaded=OK|FAIL</c> — and everything after them is
        /// there so a FAIL can be diagnosed without opening the picture.
        /// </summary>
        public static string Line(string name, bool loaded, int renderers, Shot shot,
                                  double minSubjectPercent = MinSubjectPercent,
                                  double maxDarkPercent = MaxDarkPercent)
        {
            bool ok = Passes(loaded, renderers, shot, minSubjectPercent, maxDarkPercent);
            string reason = Reason(loaded, renderers, shot, minSubjectPercent, maxDarkPercent);
            return "[QCModel] " + (string.IsNullOrEmpty(name) ? "(unnamed)" : name) +
                   " pureBlack=" + Pct(shot.PureBlackPercent) +
                   "% loaded=" + (ok ? "OK" : "FAIL") +
                   " subject=" + Pct(shot.SubjectPercent) +
                   "% blackOfSubject=" + Pct(shot.BlackOfSubjectPercent) +
                   "% darkOfSubject=" + Pct(shot.DarkOfSubjectPercent) +
                   "% nearBlack=" + Pct(shot.NearBlackPercent) +
                   "% renderers=" + renderers.ToString(CultureInfo.InvariantCulture) +
                   " px=" + shot.Pixels.ToString(CultureInfo.InvariantCulture) +
                   (reason.Length == 0 ? "" : " reason=" + reason);
        }

        /// <summary>
        /// The A/B probe line: the same model photographed with one shading input removed at a
        /// time, so the next CI round says which input the black patches come from instead of the
        /// round after that.
        ///
        /// 🔴 Why this exists. Three rounds have now been spent on the black patches and every
        /// hypothesis that could be tested from the FILES has been eliminated by measurement:
        /// tangents and normals decode clean, the metal channel reads 0.001, the base-colour
        /// atlas's black gutters never reach the surface at the real framing, back faces are 0.04%
        /// of the picture, and dropping the normal map — run 30750409697, confirmed
        /// <c>droppedNormalMap=1</c> on every model — moved <c>blackOfSubject</c> from 10.92% to
        /// 10.92% on the kraken. Offline analysis is out of road: what is left lives inside the
        /// shader, and the only instrument that can see in there is the frame itself.
        ///
        /// So the harness removes one input per frame and reports what the black does:
        ///   • <paramref name="whiteAlbedo"/> — base-colour texture cleared, factor forced white.
        ///     In gamma colour space an albedo of exactly zero multiplies the whole forward pass
        ///     to exactly zero whatever the lighting does, so this is the one hypothesis a picture
        ///     can settle outright. Black survives ⇒ it is not the texture.
        ///   • <paramref name="noMetalRough"/> — metallic-roughness texture cleared, metal 0,
        ///     roughness 0.6. That map is the last input still suspected of arriving sRGB-decoded
        ///     (roughness 0.447 → 0.166 makes the model glossy, and a glossy dielectric against a
        ///     uniform reflection cube is where the flat, blue, albedo-independent wash in the
        ///     current shots comes from).
        ///
        /// The reading is the DIFFERENCE, not the absolute: whichever probe collapses the black is
        /// the input carrying it, and if neither does then both are innocent and the cause is in
        /// the lighting terms — which is itself a result, and one that costs one round instead of
        /// three.
        /// </summary>
        public static string ProbeLine(string name, Shot baseShot, Shot whiteAlbedo, Shot noMetalRough,
                                       Shot meshNormals, Shot whiteGltf, Shot greyAlbedo,
                                       Shot bias4, Shot bias10, double maxDarkPercent = MaxDarkPercent)
            => "[QCProbe] " + (string.IsNullOrEmpty(name) ? "(unnamed)" : name) +
               " blackOfSubject base=" + Pct(baseShot.BlackOfSubjectPercent) +
               "% whiteAlbedo=" + Pct(whiteAlbedo.BlackOfSubjectPercent) +
               "% noMetalRough=" + Pct(noMetalRough.BlackOfSubjectPercent) +
               "% meshNormals=" + Pct(meshNormals.BlackOfSubjectPercent) +
               "% whiteGltf=" + Pct(whiteGltf.BlackOfSubjectPercent) +
               "% greyAlbedo=" + Pct(greyAlbedo.BlackOfSubjectPercent) +
               "% | darkOfSubject base=" + Pct(baseShot.DarkOfSubjectPercent) +
               "% whiteAlbedo=" + Pct(whiteAlbedo.DarkOfSubjectPercent) +
               "% noMetalRough=" + Pct(noMetalRough.DarkOfSubjectPercent) +
               "% meshNormals=" + Pct(meshNormals.DarkOfSubjectPercent) +
               "% whiteGltf=" + Pct(whiteGltf.DarkOfSubjectPercent) +
               "% greyAlbedo=" + Pct(greyAlbedo.DarkOfSubjectPercent) +
               "% mipBias-4=" + Pct(bias4.DarkOfSubjectPercent) +
               "% mipBias-10=" + Pct(bias10.DarkOfSubjectPercent) +
               "% | subject base=" + Pct(baseShot.SubjectPercent) +
               "% whiteAlbedo=" + Pct(whiteAlbedo.SubjectPercent) +
               "% noMetalRough=" + Pct(noMetalRough.SubjectPercent) +
               "% meshNormals=" + Pct(meshNormals.SubjectPercent) +
               "% whiteGltf=" + Pct(whiteGltf.SubjectPercent) +
               "% greyAlbedo=" + Pct(greyAlbedo.SubjectPercent) +
               "% verdict=" + ProbeVerdict(baseShot, whiteAlbedo, noMetalRough) +
               " normalVerdict=" + NormalSourceVerdict(baseShot, meshNormals, whiteGltf, maxDarkPercent) +
               " mottleVerdict=" + MottleVerdict(baseShot, greyAlbedo, maxDarkPercent) +
               " biasVerdict=" + BiasVerdict(baseShot, bias4, bias10, maxDarkPercent);

        /// <summary>
        /// Does pulling the sampler into sharper mips remove the blotches?
        ///
        /// 🔴 Why two biases and why this is a PROBE and not the fix. Measuring the actual mip
        /// levels involved says a uniform bias cannot do the job, and the arithmetic is short
        /// enough to check: on the QC frame the ordinary sampling LOD is 1.01, while at a UV chart
        /// seam the two pixels of a GPU quad land in different charts, so ΔUV of 0.05-0.5 across
        /// one pixel becomes 102-1024 texels — LOD 6.7 to 10.0. <c>mipMapBias</c> shifts EVERY
        /// sample by the same amount, so −4 leaves a seam at LOD 5-6 (still deep in the atlas
        /// average, where 11% of the sampled surface reads below half brightness) and the −8 or so
        /// it would take to rescue a seam drags ordinary sampling to LOD 0 and aliases the whole
        /// model. There is no value that separates the two cases.
        ///
        /// So this ships as two measured frames rather than as a fix: −4 is the value that was
        /// proposed, −10 is enough to defeat even the worst seam. If −10 comes back clean the
        /// arithmetic above is wrong somewhere and a bias IS the answer; if both come back
        /// unchanged then either <c>mipMapBias</c> does not reach a texture Unity is only wrapping
        /// (the same way <c>graphicsFormat</c> does not), or mip selection is not the mechanism —
        /// and the difference between those two shows up as −10 changing nothing at all versus
        /// −10 changing the picture without fixing it.
        /// </summary>
        /// <returns><c>no-mottle</c>, <c>bias-fixes-it</c>, <c>bias-helps</c> (moved but not
        /// enough), <c>bias-no-effect</c> (the API did not reach the sampler, or mips are
        /// innocent), or <c>probe-failed</c>.</returns>
        public static string BiasVerdict(Shot baseShot, Shot bias4, Shot bias10,
                                         double maxDarkPercent = MaxDarkPercent)
        {
            double b = baseShot.DarkOfSubjectPercent;
            if (b <= maxDarkPercent) return "no-mottle";
            if (bias4.Pixels == 0 || bias10.Pixels == 0) return "probe-failed";
            if (!SubjectHeld(baseShot, bias4) || !SubjectHeld(baseShot, bias10)) return "probe-failed";

            if (bias4.DarkOfSubjectPercent <= maxDarkPercent ||
                bias10.DarkOfSubjectPercent <= maxDarkPercent) return "bias-fixes-it";
            // "Did it move at all" is the question that separates a dead API from an innocent one.
            double best = System.Math.Min(bias4.DarkOfSubjectPercent, bias10.DarkOfSubjectPercent);
            return b - best < BiasMovedPercent ? "bias-no-effect" : "bias-helps";
        }

        /// <summary>How much the dark fraction must move for the bias to have done anything at
        /// all. Below this the two frames are the same picture and the API never reached the
        /// sampler.</summary>
        public const double BiasMovedPercent = 1.0;

        /// <summary>
        /// Whether the mottling survives when the albedo stops varying but keeps its BRIGHTNESS.
        ///
        /// 🔴 This exists because the probe it corrects fooled us. Run 30759921618 came back
        /// <c>whiteAlbedo=0.00%</c> on every model and that was read as "the base-colour texture is
        /// the carrier". It is not evidence of anything: this is a GAMMA colour space project, the
        /// forward pass is <c>albedo × light</c>, and an albedo of 1.0 puts every pixel above the
        /// dark ceiling no matter what the lighting does. Rendering the kraken offline with the
        /// same lights and mesh and nothing changed but a flat albedo shows it outright —
        ///
        ///   flat albedo   1.00    0.80    0.65    0.50   real texture
        ///   dark          0.00%   0.00%   0.51%   3.85%     1.44%
        ///
        /// — the "fix" was the WHITE, not the removal of the texture. 0.65 is the kraken's own
        /// measured mean, so a probe at that value keeps the model's brightness and throws away
        /// only its variation. If the patches survive it, the texture is innocent and the contrast
        /// is coming from the lighting; if they vanish, the texture's own dark regions are the
        /// carrier after all and the answer is edge dilation at the source.
        /// </summary>
        /// <returns><c>no-mottle</c>, <c>albedo-variation</c> (patches follow the texture),
        /// <c>lighting</c> (patches survive a flat albedo of the same brightness), or
        /// <c>probe-failed</c>.</returns>
        public static string MottleVerdict(Shot baseShot, Shot greyAlbedo,
                                           double maxDarkPercent = MaxDarkPercent)
        {
            if (baseShot.DarkOfSubjectPercent <= maxDarkPercent) return "no-mottle";
            if (greyAlbedo.Pixels == 0 || !SubjectHeld(baseShot, greyAlbedo)) return "probe-failed";
            return greyAlbedo.DarkOfSubjectPercent <= maxDarkPercent ? "albedo-variation" : "lighting";
        }

        /// <summary>The kraken's own measured mean albedo — a flat stand-in that keeps its
        /// brightness. 0.65 in gamma, which is what <c>Baked_BaseColor</c> averages.</summary>
        public const float MeanAlbedo = 0.65f;

        /// <summary>
        /// The mottling half of the probe, and the one that is meant to end the investigation.
        ///
        /// 🔴 Where this stands. Pure black is gone (run 30756993584, verdict=no-black on all six)
        /// and the models are still covered in hard-edged navy patches: kraken
        /// <see cref="Shot.DarkOfSubjectPercent"/> 14.75%, hardeep 8.69%, against 1.44% for an
        /// offline render of the same GLB at the same camera with the same lights, the same gamma
        /// BRDF and its own sun shadow map. Every INPUT has been eliminated by measurement —
        /// albedo, metallic-roughness, the normal map, the reflection cube (cutting its intensity
        /// makes the dark fraction WORSE: 1.32% → 18.27% → 37.61% at 1.0 → 0.7 → 0.5), and the sun
        /// shadow strength (1.44% → 1.34% from 0.5 → 0.15, i.e. nothing).
        ///
        /// What has never been separated is WHERE THE SHADING NORMAL COMES FROM. So two more
        /// frames, identical in every respect except one:
        ///   • <paramref name="meshNormals"/> — Unity's own Standard shader, white, metallic 0,
        ///     roughness 0.6, no textures at all. Nothing left but the mesh's own normals.
        ///   • <paramref name="whiteGltf"/> — the SAME white, textureless material on glTFast's
        ///     <c>glTF/PbrMetallicRoughness</c>.
        /// The pair differs by the shader and by nothing else, which is what makes the answer
        /// unambiguous instead of another correlation.
        /// </summary>
        /// <returns>
        /// <c>no-mottle</c> — nothing to explain.
        /// <c>gltfast-shader</c> — Standard renders clean, glTFast does not: the shader's normal
        /// path is the killer and the fix is to stop these models using it.
        /// <c>textures</c> — both clean, so a texture was carrying it after all.
        /// <c>mesh-normals</c> — both still mottled with no textures and no maps left: the normals
        /// arriving from the Draco decode are wrong, and the decoder is next.
        /// <c>probe-failed</c> — a frame did not land, or the silhouette moved (a probe that
        /// changed what is on screen changed more than the one thing it was supposed to, and its
        /// verdict is worthless — a magenta error-shader frame lands here).
        /// <c>inconclusive</c> — Standard mottled but glTFast clean, which no hypothesis predicts.
        /// </returns>
        public static string NormalSourceVerdict(Shot baseShot, Shot meshNormals, Shot whiteGltf,
                                                 double maxDarkPercent = MaxDarkPercent)
        {
            if (baseShot.DarkOfSubjectPercent <= maxDarkPercent) return "no-mottle";
            if (meshNormals.Pixels == 0 || whiteGltf.Pixels == 0) return "probe-failed";
            if (!SubjectHeld(baseShot, meshNormals) || !SubjectHeld(baseShot, whiteGltf)) return "probe-failed";

            bool meshClean = meshNormals.DarkOfSubjectPercent <= maxDarkPercent;
            bool gltfClean = whiteGltf.DarkOfSubjectPercent <= maxDarkPercent;
            if (meshClean && !gltfClean) return "gltfast-shader";
            if (meshClean && gltfClean) return "textures";
            if (!meshClean && !gltfClean) return "mesh-normals";
            return "inconclusive";
        }

        /// <summary>
        /// Did the probe photograph the same silhouette? Half a percent of the frame is well inside
        /// llvmpipe's frame-to-frame wobble and well outside "the model moved or vanished".
        /// </summary>
        public static bool SubjectHeld(Shot baseShot, Shot probe, double tolerancePercent = 0.5)
            => System.Math.Abs(baseShot.SubjectPercent - probe.SubjectPercent) <= tolerancePercent;

        /// <summary>
        /// Which input the black followed, in one token. A probe only ACQUITS an input when the
        /// black stays put; the collapse threshold is deliberately generous (a third of the
        /// original) because the question is "did removing this make the patches go away", not
        /// "by exactly how much".
        /// </summary>
        public static string ProbeVerdict(Shot baseShot, Shot whiteAlbedo, Shot noMetalRough)
        {
            double b = baseShot.BlackOfSubjectPercent;
            // Nothing to explain. Say so rather than nominating a culprit for a clean model.
            if (b < 0.5) return "no-black";
            // A probe whose frame never landed cannot acquit anything.
            if (whiteAlbedo.Pixels == 0 || noMetalRough.Pixels == 0) return "probe-failed";

            bool albedoCleared = whiteAlbedo.BlackOfSubjectPercent < b / 3.0;
            bool mrCleared = noMetalRough.BlackOfSubjectPercent < b / 3.0;
            if (albedoCleared && mrCleared) return "both";
            if (albedoCleared) return "base-colour-texture";
            if (mrCleared) return "metallic-roughness-texture";
            return "neither-lighting";
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
