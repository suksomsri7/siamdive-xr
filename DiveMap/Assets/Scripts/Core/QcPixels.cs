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

        // ── WO-E3: the depth pass ────────────────────────────────────────────────

        /// <summary>
        /// What a reference animal photographed at a known depth says about the three things the
        /// user actually complained about.
        ///
        /// 🔴 The complaint was specific and none of the existing metrics could see it: at 52 m on
        /// Poseidon "ฉลามกลายเป็นเงาแบนสีน้ำเงินไร้รายละเอียด ขณะที่ฉากหลัง/หมอกยังสว่าง".
        /// <see cref="Shot.DarkOfSubjectPercent"/> counts how much of a model is dark, which does
        /// not distinguish "dark animal" from "animal that has fallen behind its own background",
        /// and nothing at all measured whether the depth cue survived a fix. Three numbers do:
        ///
        ///   • <see cref="Contrast"/> — does the subject stand apart from the water at all;
        ///   • <see cref="DynamicRange"/> — is there any shading left INSIDE it, or is it a
        ///     silhouette-shaped flat fill;
        ///   • <see cref="BlueShift"/> — is the frame still getting bluer with depth, i.e. did the
        ///     fix quietly throw away the dimming the user explicitly asked to keep.
        ///
        /// All three are read off the SCREEN bytes, not off scene-linear light, because the screen
        /// is what was complained about and because the tone curve is part of what is under test.
        /// </summary>
        public struct DepthShot
        {
            /// <summary>Mean Rec.709 luminance of the subject's own pixels, 0..1 display.</summary>
            public double SubjectLuminance;
            /// <summary>Mean luminance of everything that is not the subject — the water.</summary>
            public double BackgroundLuminance;
            /// <summary>|subject − background| / background. The silhouette number.</summary>
            public double Contrast;
            /// <summary>5th and 95th percentile of the subject's luminance.</summary>
            public double SubjectP5, SubjectP95;
            /// <summary>P95 − P5. Zero means the subject is one flat colour.</summary>
            public double DynamicRange;
            /// <summary>(B − R)/(B + R) over the whole frame — see <see cref="BlueShift"/>.</summary>
            public double BlueShift;
            /// <summary>mean B / mean R, the raw form, guarded against a red channel at zero.</summary>
            public double BlueOverRed;
            /// <summary>Subject share of the frame, so an empty shot cannot score well.</summary>
            public double SubjectPercent;
            /// <summary>Frame size, 0 when the buffers were unusable.</summary>
            public int Pixels;
        }

        /// <summary>
        /// Contrast floor. Below this the subject and the water it is in are the same brightness,
        /// which on a screen is what "flat silhouette" looks like whichever way round it is.
        /// </summary>
        public const double MinContrast = 0.25;

        /// <summary>
        /// Dynamic-range floor inside the subject. This is the one that actually answers "ไร้
        /// รายละเอียด": an object can pass the contrast gate by being uniformly bright and still be
        /// a cut-out. 0.15 of the display range is roughly the point at which shading reads as
        /// form rather than as noise.
        /// </summary>
        public const double MinDynamicRange = 0.15;

        /// <summary>
        /// Rec.709 luminance of a display-encoded triple. Deliberately NOT linearised: the question
        /// is what the eye gets from the finished frame.
        /// </summary>
        public static double Luminance(byte r, byte g, byte b)
            => (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;

        /// <summary>
        /// Measure one depth pair. Same trick as <see cref="Measure"/>: the subject is whatever
        /// differs between the frame with the animal and the identical frame without it, so an
        /// empty stage measures a 0% subject and cannot be reported as a good picture.
        /// </summary>
        public static DepthShot MeasureDepth(byte[] withSubject, byte[] withoutSubject,
                                             byte tolerance = SubjectTolerance)
        {
            var d = new DepthShot();
            int n = PixelCount(withSubject);
            if (n == 0) return d;
            d.Pixels = n;
            if (withoutSubject == null || withoutSubject.Length != withSubject.Length) return d;

            var subjectLum = new System.Collections.Generic.List<double>();
            double bgSum = 0.0;
            long bgCount = 0;
            double sumR = 0.0, sumB = 0.0;

            for (int i = 0; i < n; i++)
            {
                int p = i * Channels;
                byte r = withSubject[p], g = withSubject[p + 1], b = withSubject[p + 2];
                sumR += r; sumB += b;

                int dr = r - withoutSubject[p];
                int dg = g - withoutSubject[p + 1];
                int db = b - withoutSubject[p + 2];
                if (dr < 0) dr = -dr;
                if (dg < 0) dg = -dg;
                if (db < 0) db = -db;

                double lum = Luminance(r, g, b);
                if (dr <= tolerance && dg <= tolerance && db <= tolerance)
                {
                    bgSum += lum; bgCount++;
                }
                else subjectLum.Add(lum);
            }

            d.SubjectPercent = 100.0 * subjectLum.Count / n;
            d.BackgroundLuminance = bgCount == 0 ? 0.0 : bgSum / bgCount;

            if (subjectLum.Count > 0)
            {
                double s = 0.0;
                for (int i = 0; i < subjectLum.Count; i++) s += subjectLum[i];
                d.SubjectLuminance = s / subjectLum.Count;

                subjectLum.Sort();
                d.SubjectP5 = Percentile(subjectLum, 0.05);
                d.SubjectP95 = Percentile(subjectLum, 0.95);
                d.DynamicRange = d.SubjectP95 - d.SubjectP5;
            }

            // A background of zero would make the ratio meaningless rather than infinite, so it is
            // reported as 0 — "no contrast measurable" — and the verdict below refuses it.
            d.Contrast = d.BackgroundLuminance <= 1e-9
                ? 0.0
                : System.Math.Abs(d.SubjectLuminance - d.BackgroundLuminance) / d.BackgroundLuminance;

            double meanR = sumR / n, meanB = sumB / n;
            // 🔎 B/R is the natural way to say "the frame got bluer", and it is numerically useless
            // here: at 52 m the red channel of the water lands at 0-1/255 after ACES, so the ratio
            // swings between 100 and 400,000 on a single byte. It is still LOGGED, because it is
            // the number a human reads, but the monotonicity check runs on the bounded form
            // (B−R)/(B+R) ∈ [−1,1], which is a strictly increasing function of B/R and therefore
            // says exactly the same thing without dividing by nearly nothing.
            d.BlueOverRed = meanR <= 1e-9 ? 0.0 : meanB / meanR;
            double sum = meanB + meanR;
            d.BlueShift = sum <= 1e-9 ? 0.0 : (meanB - meanR) / sum;
            return d;
        }

        private static double Percentile(System.Collections.Generic.List<double> sorted, double q)
        {
            if (sorted.Count == 0) return 0.0;
            if (sorted.Count == 1) return sorted[0];
            double pos = q * (sorted.Count - 1);
            int lo = (int)pos;
            int hi = lo + 1 >= sorted.Count ? sorted.Count - 1 : lo + 1;
            double f = pos - lo;
            return sorted[lo] + (sorted[hi] - sorted[lo]) * f;
        }

        /// <summary>
        /// One token for what this depth shot proves, in the order the failures matter.
        /// "" when it is fine.
        /// </summary>
        public static string DepthReason(DepthShot d,
                                         double minContrast = MinContrast,
                                         double minDynamicRange = MinDynamicRange,
                                         double minSubjectPercent = MinSubjectPercent)
        {
            if (d.Pixels <= 0) return "readback-empty";
            if (d.SubjectPercent < minSubjectPercent) return "not-in-frame";
            if (d.BackgroundLuminance <= 1e-9) return "no-background";
            if (d.Contrast < minContrast) return "flat-against-water";
            if (d.DynamicRange < minDynamicRange) return "no-shading";
            return "";
        }

        /// <summary>Did this depth shot pass all of its own gates?</summary>
        public static bool DepthPasses(DepthShot d,
                                       double minContrast = MinContrast,
                                       double minDynamicRange = MinDynamicRange,
                                       double minSubjectPercent = MinSubjectPercent)
            => DepthReason(d, minContrast, minDynamicRange, minSubjectPercent).Length == 0;

        /// <summary>The <c>[QCDepth]</c> log line, shaped here so a test can pin it.</summary>
        public static string DepthLine(string name, double metres, bool aces, DepthShot d,
                                       double minContrast = MinContrast,
                                       double minDynamicRange = MinDynamicRange)
        {
            string reason = DepthReason(d, minContrast, minDynamicRange);
            return "[QCDepth] " + (string.IsNullOrEmpty(name) ? "(unnamed)" : name) +
                   " depth=" + metres.ToString("0.0", CultureInfo.InvariantCulture) + "m" +
                   " aces=" + (aces ? "on" : "off") +
                   " verdict=" + (reason.Length == 0 ? "OK" : reason) +
                   " subject=" + Pct(d.SubjectPercent) + "%" +
                   " Lsubj=" + Pct(d.SubjectLuminance) +
                   " Lbg=" + Pct(d.BackgroundLuminance) +
                   " contrast=" + Pct(d.Contrast) +
                   " p5=" + Pct(d.SubjectP5) +
                   " p95=" + Pct(d.SubjectP95) +
                   " range=" + Pct(d.DynamicRange) +
                   " blueShift=" + Pct(d.BlueShift) +
                   " blueOverRed=" + Pct(d.BlueOverRed);
        }

        /// <summary>
        /// Is the depth cue still there?
        ///
        /// 🔑 The user chose realism over readability when the two were put to them — "หรี่แสงตาม
        /// ความลึก = เก็บไว้" — so a change that fixes the contrast by flattening the depth
        /// response would be a regression even though every other number improved. This is the
        /// guard for that decision, and it is why the shots are taken at three depths rather than
        /// at the worst one.
        /// </summary>
        /// <summary>
        /// How far the blue shift may go BACKWARDS between two depths before the cue counts as
        /// lost.
        ///
        /// 🔎 Not zero, and the reason is measurable rather than defensive. The water's red channel
        /// is crushed to byte 0 by the deep end of the curve, so <see cref="DepthShot.BlueShift"/>
        /// saturates at 1.0 somewhere around 50 m: two frames that are both "as blue as a frame
        /// gets" would compare equal, and a strict &gt; would report the cue lost on a scene where
        /// it is working perfectly. This tolerates the plateau while still refusing a real
        /// reversal.
        /// </summary>
        public const double BlueShiftSlack = 0.01;

        /// <param name="shallowToDeep">One shot per depth, in increasing depth order.</param>
        public static string DepthCueVerdict(DepthShot[] shallowToDeep)
        {
            if (shallowToDeep == null || shallowToDeep.Length < 2) return "not-enough-depths";
            for (int i = 0; i < shallowToDeep.Length; i++)
                if (shallowToDeep[i].Pixels <= 0) return "probe-failed";

            for (int i = 1; i < shallowToDeep.Length; i++)
                if (shallowToDeep[i].BlueShift < shallowToDeep[i - 1].BlueShift - BlueShiftSlack)
                    return "depth-cue-lost";

            // …and the plateau must not be the WHOLE range: the deepest frame still has to be
            // meaningfully bluer than the shallowest, or nothing changed with depth at all.
            double first = shallowToDeep[0].BlueShift;
            double last = shallowToDeep[shallowToDeep.Length - 1].BlueShift;
            if (last <= first + BlueShiftSlack) return "depth-cue-flat";
            return "depth-cue-held";
        }

        // ── WO-E5: the WHOLE MAP, from where the player stands ───────────────────
        //
        // 🔴 WHY THIS EXISTS, in the user's own words: "texture ของฉากและวัตถุไม่ตรงตามโมเดลจริงๆ
        // เลย ผมถามคุณ QC ยังไงครับ คุณไม่ได้ถ่ายรูปแคปเจอร์หน้าจอมาดูเองด้วยหรอ".
        //
        // They are right, and the gap is structural rather than careless. Every QC pass in this
        // project photographs either NINE MODELS ALONE against a gradient (QcModelShot) or ONE map,
        // HTMS Chang, from an orbit pose (QcShot angle 1/2). Atlantis, Posidon, Hanuman, Harddeep,
        // T-13 and Tu-1 — the maps the complaints are actually about — have never been in a CI
        // frame, and neither has the diver's-eye pose the player spends the whole game in. So the
        // numbers kept improving in the studio while the game kept looking worse, and both were
        // true.
        //
        // 🔎 THREE FRAMES FROM ONE POSE, and each pair answers a question no single frame can:
        //
        //   withMap  the shot. Everything below is measured against it.
        //   noMap    the same pose with the map hidden — water and backdrop only. Whatever DIFFERS
        //            is the map, which is the same trick <see cref="Measure"/> uses and the same
        //            reason: a beautiful photograph of empty water must score 0% subject and FAIL,
        //            not pass quietly. It is also the only way to measure the background gradient
        //            with nothing standing in front of it.
        //   noFog    the same pose with RenderSettings.fog off. The per-pixel difference from
        //            withMap IS the fog's contribution, so "is there any fog in this picture"
        //            stops being a matter of opinion and becomes a byte count. On the shipped
        //            build this is expected to be ~0 — see WaterFog.RangeAt for why that is
        //            arithmetic rather than a surprise.
        public struct MapShot
        {
            /// <summary>% of the frame darker than <see cref="DarkMax"/> in every channel. The
            /// user measured 33.1% off their own Atlantis screenshot; this is that number, taken
            /// by the app instead of off a phone photo.</summary>
            public double DarkPercent;

            /// <summary>Whole-frame luminance percentiles, 0-1.</summary>
            public double P50, P90, P99;

            /// <summary>% of the frame the map occupies, and how bright it is.</summary>
            public double SubjectPercent;
            public double SubjectLuminance, SubjectP5, SubjectP95;

            /// <summary>Everything that is not the map: water, backdrop, HUD.</summary>
            public double WaterLuminance;

            /// <summary>
            /// Mean |Δluminance| between the frame and its no-fog twin, over the MAP's pixels,
            /// ×100. This is the fog, measured. Under <see cref="MinFogWork"/> the fog is
            /// decoration: it is switched on, it costs a shader variant, and it changes nothing
            /// the player can see.
            /// </summary>
            public double FogWork;

            /// <summary>The largest single-pixel fog contribution, ×100 — so a fog that touches
            /// only the far rim still registers even when its mean is small.</summary>
            public double FogWorkMax;

            /// <summary>
            /// TOP-of-screen minus BOTTOM-of-screen luminance in the MAP-FREE shot, 0-1 — the
            /// background ramp with nothing standing in front of it. Positive means bright above
            /// and dark below, which is the only way round water works.
            ///
            /// 🔴 IN SCREEN TERMS, NOT BUFFER TERMS. Run 30800189252 reported −0.552 / −0.576 /
            /// −0.681 on the first three maps, which read as "the gradient is upside down" and is
            /// not: <c>Texture2D.GetRawTextureData</c> hands back BOTTOM-UP rows, so the first
            /// rows of the buffer are the bottom of the picture and the old subtraction was
            /// bottom minus top. The magnitude was always right. <see cref="BackdropSpanOf"/>
            /// now takes the row order explicitly and <see cref="BandTop"/>/<see cref="BandBottom"/>
            /// are reported separately so the log states which end is bright instead of a reader
            /// having to know the convention.
            /// </summary>
            public double BackdropSpan;

            /// <summary>Mean luminance of the top and bottom bands of the MAP-FREE shot, in
            /// SCREEN order. Logged raw so an inverted readback shows up as two numbers a human
            /// can compare against the PNG rather than as a sign nobody can interpret.</summary>
            public double BandTop, BandBottom;

            /// <summary>
            /// The same span measured on the REAL frame — what the player's eye is actually given.
            ///
            /// 🔎 This is the number the complaint is about, and it is deliberately NOT gated yet.
            /// <see cref="BackdropSpan"/> asks "does the ramp exist and is it the right way up",
            /// which is a structural question with a right answer. This one asks "does the picture
            /// have vertical depth", and on a diver's view the bottom of the frame is bright sand
            /// rather than deep water — so a low value here can mean a flat scene OR a perfectly
            /// good one with a sandy floor, and there is no threshold that separates them until a
            /// run with a picture beside it says where the line is. Measured off the user's own
            /// Posidon screenshot the shipped build sits at 0.031.
            /// </summary>
            public double SeenSpan;

            /// <summary>Renderers that landed, and items the builder gave up on.</summary>
            public int Renderers, Loaded, Failed;

            public int Pixels;
        }

        /// <summary>
        /// Below this the fog is not doing anything a player could see. 0.4 of a byte, averaged
        /// over the map's own pixels: quarter of a byte is under the dither noise of an 8-bit
        /// frame, and half a byte is the smallest difference that survives a screenshot.
        /// </summary>
        public const double MinFogWork = 0.4;

        /// <summary>
        /// The background gradient has to span at least this much luminance across the frame, or
        /// the water is a flat wall. Measured off the user's Posidon screenshot, the shipped build
        /// spans 0.031 (byte 172.6 at the top of the frame to 164.7 at the bottom of the visible
        /// sky) — which is what "ฉากไม่มีมิติเลย" is, in numbers.
        /// </summary>
        public const double MinBackdropSpan = 0.08;

        /// <summary>
        /// Fraction of the frame's rows sampled at each end for <see cref="MapShot.BackdropSpan"/>.
        /// A tenth is wide enough to average out the dither and narrow enough that the seabed,
        /// which fills the bottom of a diver's view, does not eat the sample.
        /// </summary>
        public const double BackdropBandFraction = 0.10;

        /// <summary>
        /// The frame has to contain some WATER, or every other number in this struct is measuring
        /// nothing.
        ///
        /// 🔴 Hanuman, run 30800189252: <c>waterLum=0.000 subject=100.00% fogWork=0.07</c>, and the
        /// pass reported <c>no-fog</c>. That verdict was true and useless — with 100% of the frame
        /// scored as subject there was no background for the fog to be measured against, so the
        /// camera was inside something rather than the fog being broken. A pose that sees no water
        /// is a POSE failure and has to say so, before any conclusion is drawn about the shading.
        /// 0.02 is well under the darkest water this project renders (the deep stop at 100 m still
        /// lands near 0.05) and well over a readback of zeros.
        /// </summary>
        public const double MinWaterLuminance = 0.02;

        /// <summary>
        /// …and the same failure stated from the other side: a frame that is almost entirely
        /// subject is a camera buried in geometry, whatever its luminance says.
        /// </summary>
        public const double MaxSubjectPercent = 95.0;

        /// <summary>
        /// Measure one map pose. <paramref name="height"/> is needed because the backdrop span is
        /// a question about ROWS and a flat byte array has no idea which row it is in; pass 0 and
        /// the span is reported as 0 rather than guessed at.
        /// </summary>
        /// <param name="bottomUpRows">True when row 0 of the buffers is the BOTTOM of the screen,
        /// which is what <c>Texture2D.GetRawTextureData</c> hands back. Passed in rather than
        /// assumed: getting it wrong flips the sign of the one number that says whether the water
        /// is the right way up, and that is exactly what happened in run 30800189252.</param>
        public static MapShot MeasureMap(byte[] withMap, byte[] noMap, byte[] noFog,
                                         int width, int height,
                                         byte tolerance = SubjectTolerance,
                                         bool bottomUpRows = true)
        {
            var m = new MapShot();
            int n = PixelCount(withMap);
            if (n == 0) return m;
            m.Pixels = n;
            m.DarkPercent = AtOrBelowPercent(withMap, (byte)(DarkMax - 1));

            var all = new System.Collections.Generic.List<double>(n);
            var subject = new System.Collections.Generic.List<double>();
            double waterSum = 0.0; long waterCount = 0;
            double fogSum = 0.0; long fogCount = 0; double fogMax = 0.0;

            bool haveNoMap = noMap != null && noMap.Length == withMap.Length;
            bool haveNoFog = noFog != null && noFog.Length == withMap.Length;

            for (int i = 0; i < n; i++)
            {
                int p = i * Channels;
                double lum = Luminance(withMap[p], withMap[p + 1], withMap[p + 2]);
                all.Add(lum);

                bool isSubject = false;
                if (haveNoMap)
                {
                    int dr = withMap[p] - noMap[p];
                    int dg = withMap[p + 1] - noMap[p + 1];
                    int db = withMap[p + 2] - noMap[p + 2];
                    if (dr < 0) dr = -dr;
                    if (dg < 0) dg = -dg;
                    if (db < 0) db = -db;
                    isSubject = dr > tolerance || dg > tolerance || db > tolerance;
                }

                if (isSubject) subject.Add(lum);
                else { waterSum += lum; waterCount++; }

                if (haveNoFog && (isSubject || !haveNoMap))
                {
                    double d = System.Math.Abs(
                        lum - Luminance(noFog[p], noFog[p + 1], noFog[p + 2]));
                    fogSum += d; fogCount++;
                    if (d > fogMax) fogMax = d;
                }
            }

            all.Sort();
            m.P50 = Percentile(all, 0.50);
            m.P90 = Percentile(all, 0.90);
            m.P99 = Percentile(all, 0.99);

            m.SubjectPercent = 100.0 * subject.Count / n;
            m.WaterLuminance = waterCount == 0 ? 0.0 : waterSum / waterCount;
            if (subject.Count > 0)
            {
                double s = 0.0;
                for (int i = 0; i < subject.Count; i++) s += subject[i];
                m.SubjectLuminance = s / subject.Count;
                subject.Sort();
                m.SubjectP5 = Percentile(subject, 0.05);
                m.SubjectP95 = Percentile(subject, 0.95);
            }

            m.FogWork = fogCount == 0 ? 0.0 : 100.0 * fogSum / fogCount;
            m.FogWorkMax = 100.0 * fogMax;
            byte[] clean = haveNoMap ? noMap : withMap;
            m.BackdropSpan = BackdropSpanOf(clean, width, height, bottomUpRows,
                                            out double top, out double bottom);
            m.BandTop = top;
            m.BandBottom = bottom;
            m.SeenSpan = BackdropSpanOf(withMap, width, height, bottomUpRows, out _, out _);
            return m;
        }

        /// <summary>
        /// Mean luminance of the top band of the SCREEN minus the bottom band of the SCREEN.
        ///
        /// 🔴 The row order is a parameter, not an assumption. Unity hands a readback back
        /// bottom-up, so the first rows of the buffer are the bottom of the picture; subtracting
        /// them the other way round is what turned a perfectly healthy −0.68 ramp into a
        /// "backdropSpan FAIL" in run 30800189252. Both bands come back out through
        /// <paramref name="top"/> and <paramref name="bottom"/> so a caller logs the two numbers
        /// rather than a sign, and a genuinely inverted picture is then visible as
        /// "the bright band is the one at the bottom" instead of as a convention argument.
        /// </summary>
        public static double BackdropSpanOf(byte[] rgb, int width, int height,
                                            bool bottomUpRows,
                                            out double top, out double bottom)
        {
            top = 0.0;
            bottom = 0.0;
            int n = PixelCount(rgb);
            if (n == 0 || width <= 0 || height <= 0 || width * height != n) return 0.0;
            int band = (int)(height * BackdropBandFraction);
            if (band < 1) band = 1;
            if (band * 2 >= height) return 0.0;

            double first = BandMean(rgb, width, 0, band);
            double last = BandMean(rgb, width, height - band, band);

            // Row 0 is the bottom of the screen when the buffer is bottom-up.
            top = bottomUpRows ? last : first;
            bottom = bottomUpRows ? first : last;
            return top - bottom;
        }

        private static double BandMean(byte[] rgb, int width, int firstRow, int rows)
        {
            double sum = 0.0;
            long count = 0;
            for (int y = firstRow; y < firstRow + rows; y++)
                for (int x = 0; x < width; x++)
                {
                    int p = (y * width + x) * Channels;
                    sum += Luminance(rgb[p], rgb[p + 1], rgb[p + 2]);
                    count++;
                }
            return count == 0 ? 0.0 : sum / count;
        }

        // ── WO-E5d: the shading ladder ───────────────────────────────────────────
        //
        // 🔴 WHY A LADDER AND NOT ANOTHER NUMBER. The Atlantis ruins have now been measured on the
        // file (albedo, gutter, normals, metal, vertex colours, LOD, occlusion — all clean or
        // cleared) and photographed on a phone in DAYLIGHT mode, where the depth curve, the fog and
        // the ambient floor are all switched off before a line of them runs. They are still black.
        // So the loss is somewhere between "the albedo the file holds" and "the byte on the screen",
        // and no single measurement at either end can say where. A ladder measures every rung.
        //
        // The rungs are chosen so each one removes exactly one thing, in scene-linear (see
        // ToneMap.InverseNeutral — a ratio in screen bytes is not a ratio of light):
        //
        //   shipped      the model as the player sees it
        //   noAces       the same frame with the tone curve off — separates "we are dark" from
        //                "we are in the part of the curve that crushes"
        //   greyAlbedo   the model's albedo replaced by a KNOWN 0.65. Everything else identical,
        //                so this rung is pure irradiance × BRDF
        //   whiteAlbedo  albedo 1.0 — the same question with no albedo left at all
        //   meshNormals  Unity's own shader, white, no textures: the mesh's own normals only
        //   noShadow     the sun's shadow off for one frame
        //   ambientOnly  the SUN off. In daylight the ambient bands are 0.53-0.72 and neutral, so a
        //                surface of albedo 0.16 lit by nothing but them must land near byte 80-100.
        //                It is the rung with a predictable answer, which makes it the one that can
        //                convict: measured on the user's own daylight screenshot the dome's body
        //                sits at byte 3, and no amount of shadow can do that to an ambient term.
        //
        // 🔎 AND A REFERENCE SURFACE IN THE SAME FRAME. Every rung is measured twice — once on the
        // model and once on a plain quad of KNOWN albedo standing beside it, lit by the same sun
        // and the same ambient through the same shader the seabed uses. Without it every number is
        // a measurement of the whole scene; with it, modelLinear / referenceLinear is a measurement
        // of the MODEL, and it has an arithmetic prediction: it should equal the model's own albedo
        // divided by the reference's. Where the ladder departs from that prediction is the rung
        // that owns the bug.
        public struct Rung
        {
            public string Name;
            /// <summary>Mean scene-linear luminance of the model's own pixels.</summary>
            public double ModelLinear;
            /// <summary>…and of the reference quad's, in the same frame.</summary>
            public double ReferenceLinear;
            /// <summary>% of the model's pixels that are exactly (0,0,0).</summary>
            public double BlackPercent;
            public double SubjectPercent;

            /// <summary>The number the whole ladder is for: how much light the model returns per
            /// unit of light a known surface returns, in the same frame.</summary>
            public double Ratio => ReferenceLinear <= 1e-12 ? 0.0 : ModelLinear / ReferenceLinear;
        }

        /// <summary>
        /// Mean SCENE-LINEAR luminance of the pixels where <paramref name="frame"/> differs from
        /// <paramref name="empty"/> — i.e. of the subject — undoing the tone curve on the way.
        /// </summary>
        public static double SceneLinearOfSubject(byte[] frame, byte[] empty,
                                                  out double subjectPercent,
                                                  out double blackPercent,
                                                  byte tolerance = SubjectTolerance)
        {
            subjectPercent = 0.0;
            blackPercent = 0.0;
            int n = PixelCount(frame);
            if (n == 0 || empty == null || empty.Length != frame.Length) return 0.0;

            double sum = 0.0;
            long count = 0, black = 0;
            for (int i = 0; i < n; i++)
            {
                int p = i * Channels;
                int dr = frame[p] - empty[p];
                int dg = frame[p + 1] - empty[p + 1];
                int db = frame[p + 2] - empty[p + 2];
                if (dr < 0) dr = -dr;
                if (dg < 0) dg = -dg;
                if (db < 0) db = -db;
                if (dr <= tolerance && dg <= tolerance && db <= tolerance) continue;

                count++;
                if (frame[p] == 0 && frame[p + 1] == 0 && frame[p + 2] == 0) black++;
                // byte → display-linear → scene-linear
                double display = ToneMap.SrgbToLinear((float)Luminance(frame[p], frame[p + 1], frame[p + 2]));
                sum += ToneMap.InverseNeutral((float)display);
            }
            if (count == 0) return 0.0;
            subjectPercent = 100.0 * count / n;
            blackPercent = 100.0 * black / count;
            return sum / count;
        }

        /// <summary>Mean scene-linear luminance inside a rectangle — the reference quad.</summary>
        public static double SceneLinearOfRect(byte[] frame, int width, int height,
                                               int x0, int y0, int w, int h)
        {
            int n = PixelCount(frame);
            if (n == 0 || width <= 0 || height <= 0 || width * height != n) return 0.0;
            if (w <= 0 || h <= 0) return 0.0;
            if (x0 < 0) x0 = 0;
            if (y0 < 0) y0 = 0;
            if (x0 + w > width) w = width - x0;
            if (y0 + h > height) h = height - y0;
            if (w <= 0 || h <= 0) return 0.0;

            double sum = 0.0;
            long count = 0;
            for (int y = y0; y < y0 + h; y++)
                for (int x = x0; x < x0 + w; x++)
                {
                    int p = (y * width + x) * Channels;
                    double display = ToneMap.SrgbToLinear((float)Luminance(frame[p], frame[p + 1], frame[p + 2]));
                    sum += ToneMap.InverseNeutral((float)display);
                    count++;
                }
            return count == 0 ? 0.0 : sum / count;
        }

        /// <summary>One line per rung — the ladder, in the order it was climbed.</summary>
        public static string RungLine(string model, Rung r, double expectedRatio)
            => $"[QCLadder] {model} rung={r.Name} " +
               $"modelLin={r.ModelLinear:0.00000} refLin={r.ReferenceLinear:0.00000} " +
               $"ratio={r.Ratio:0.0000} expected={expectedRatio:0.0000} " +
               $"shortfall={(expectedRatio <= 1e-9 ? 0.0 : r.Ratio / expectedRatio):0.000}x " +
               $"black={r.BlackPercent:0.00}% subject={r.SubjectPercent:0.00}%";

        /// <summary>One token for what this map shot proves, worst first. "" when it is fine.</summary>
        public static string MapReason(MapShot m,
                                       double minSubjectPercent = MinSubjectPercent,
                                       double minFogWork = MinFogWork,
                                       double minBackdropSpan = MinBackdropSpan)
        {
            if (m.Pixels <= 0) return "readback-empty";
            if (m.Renderers <= 0) return "nothing-loaded";
            if (m.SubjectPercent < minSubjectPercent) return "map-not-in-frame";
            // Before anything about shading: was this a picture of the scene at all? A camera
            // inside a rock scores 100% subject and 0.000 water, and every verdict below it would
            // be a statement about the inside of a rock.
            if (m.SubjectPercent > MaxSubjectPercent) return "camera-buried";
            if (m.WaterLuminance < MinWaterLuminance) return "no-water";
            if (m.FogWork < minFogWork) return "no-fog";
            // A NEGATIVE span is not a small one — it is bright water below and dark water above,
            // which no depth curve produces and no reader would guess from a magnitude.
            if (m.BackdropSpan <= -minBackdropSpan) return "ramp-upside-down";
            if (m.BackdropSpan < minBackdropSpan) return "flat-water";
            return "";
        }

        /// <summary>Did this map pose pass its own gates?</summary>
        public static bool MapPasses(MapShot m,
                                     double minSubjectPercent = MinSubjectPercent,
                                     double minFogWork = MinFogWork,
                                     double minBackdropSpan = MinBackdropSpan)
            => MapReason(m, minSubjectPercent, minFogWork, minBackdropSpan) == "";

        /// <summary>One line per map pose — everything a reviewer needs without the picture.</summary>
        public static string MapLine(string map, string pose, double depthMetres, MapShot m,
                                     double minSubjectPercent = MinSubjectPercent,
                                     double minFogWork = MinFogWork,
                                     double minBackdropSpan = MinBackdropSpan)
        {
            string reason = MapReason(m, minSubjectPercent, minFogWork, minBackdropSpan);
            return $"[QCMap] {map} pose={pose} depth={depthMetres:F1}m " +
                   $"{(reason == "" ? "PASS" : "FAIL")}{(reason == "" ? "" : " " + reason)} " +
                   $"dark={m.DarkPercent:0.00}% p50={m.P50:0.000} p90={m.P90:0.000} p99={m.P99:0.000} " +
                   $"subject={m.SubjectPercent:0.00}% subjLum={m.SubjectLuminance:0.000} " +
                   $"subjP5={m.SubjectP5:0.000} subjP95={m.SubjectP95:0.000} " +
                   $"waterLum={m.WaterLuminance:0.000} " +
                   $"objOverWater={(m.WaterLuminance <= 1e-9 ? 0.0 : m.SubjectLuminance / m.WaterLuminance):0.00} " +
                   $"fogWork={m.FogWork:0.00} fogMax={m.FogWorkMax:0.00} " +
                   $"backdropSpan={m.BackdropSpan:0.000} " +
                   $"bandTop={m.BandTop:0.000} bandBottom={m.BandBottom:0.000} " +
                   $"seenSpan={m.SeenSpan:0.000} " +
                   $"renderers={m.Renderers} loaded={m.Loaded} failed={m.Failed} px={m.Pixels}";
        }
    }
}
