namespace DiveMap.Core
{
    /// <summary>
    /// State of the "loading" screen: how much of the map has arrived, and whether the
    /// overlay that reports it is on screen, fading out, or gone.
    ///
    /// Port of the web's two loading affordances (builder.html):
    ///   • <c>#load</c> — a full-bleed <c>--bg</c> cover over everything until the scene
    ///     is ready to play (builder.html:223, hidden at 4431).
    ///   • the v.0668 mini progress ring (builder.html:434-449) — % of the model files
    ///     that have finished, which fades away 0.35 s after the last one lands.
    ///
    /// UnityEngine-free on purpose: this is the half that can be tested on this machine
    /// (tools/test.sh) rather than in a 35-minute CI run, and the MonoBehaviour that draws
    /// it (Runtime/Ui/LoadOverlay) holds nothing but sprites and a CanvasGroup.
    ///
    /// Two rules the numbers must obey, both learned the hard way:
    ///  • A FAILED file counts as finished. The bar tracks <c>loaded + failed</c>, not
    ///    <c>loaded</c>: a map with one dead URL would otherwise stop at 99% forever and
    ///    the player would sit on a blue screen watching a bar that can never fill.
    ///  • A build that is CANCELLED half-way (Retry, map switch) must clear this state —
    ///    the same lesson as SceneBuilder.DiscardInFlight, where a stopped coroutine left
    ///    its half-built map in the scene because nobody cleaned up after the canceller.
    ///    Here the leftover is a full-screen cover that nothing would ever take down.
    /// </summary>
    public sealed class LoadProgress
    {
        /// <summary>Fade-out of the finished overlay — the web's <c>transition:opacity .35s</c>.</summary>
        public const float FadeSeconds = 0.35f;

        /// <summary>
        /// Hard ceiling on how long the cover may stay up. SceneBuilder's own safety timeout is
        /// 120 s (OverallLoadTimeout); this is that plus room for the fetch in front of it. If it
        /// ever trips, something upstream died without telling anyone — and a dead build must
        /// still not leave the player staring at an opaque screen with no way out.
        /// </summary>
        public const float StuckSeconds = 150f;

        /// <summary>True from <see cref="Show"/> until the fade has finished (or a cancel).</summary>
        public bool Shown { get; private set; }

        /// <summary>True once the build is done — the bar reads full and the fade has started.</summary>
        public bool Finished { get; private set; }

        /// <summary>Files (scene items) this build has to place. 0 = not counted yet.</summary>
        public int Total { get; private set; }

        /// <summary>Files that are done: loaded OR failed.</summary>
        public int Done { get; private set; }

        /// <summary>Overlay opacity 0..1 (1 while loading, ramps to 0 over <see cref="FadeSeconds"/>).</summary>
        public float Alpha { get; private set; }

        /// <summary>Seconds since <see cref="Show"/>, for the stuck-overlay watchdog.</summary>
        public float Elapsed { get; private set; }

        /// <summary>True while the overlay should be drawn at all.</summary>
        public bool Visible { get { return Shown && Alpha > 0f; } }

        /// <summary>
        /// True while the overlay must swallow touches. It stops blocking as soon as the fade
        /// is under way: the map is playable at that point and the last 0.35 s of a ghost image
        /// must not eat the player's first tap.
        /// </summary>
        public bool BlocksInput { get { return Visible && !Finished; } }

        /// <summary>Fraction of the map that has arrived, 0..1.</summary>
        public float Fraction
        {
            get
            {
                if (Finished) return 1f;
                if (Total <= 0) return 0f;   // item count not known yet (fetch/manifest phase)
                float f = (float)Done / Total;
                if (f < 0f) return 0f;
                if (f > 1f) return 1f;
                return f;
            }
        }

        /// <summary>Fraction as whole percent, 0..100.</summary>
        public int Percent { get { return (int)(Fraction * 100f + 0.5f); } }

        /// <summary>Put the cover up and reset the count. Safe to call over a run already showing.</summary>
        public void Show()
        {
            Shown = true;
            Finished = false;
            Total = 0;
            Done = 0;
            Alpha = 1f;
            Elapsed = 0f;
        }

        /// <summary>How many items this build will place (SceneBuilder, once the scene is parsed).</summary>
        public void SetTotal(int total)
        {
            Total = total > 0 ? total : 0;
            if (Done > Total) Done = Total;
        }

        /// <summary>Report the builder's live counters. Failures count as finished (see class doc).</summary>
        public void Report(int loaded, int failed)
        {
            int done = (loaded > 0 ? loaded : 0) + (failed > 0 ? failed : 0);
            if (Total > 0 && done > Total) done = Total;
            // Monotonic: the bar never walks backwards inside one build, whatever order the
            // async completions land in.
            if (done > Done) Done = done;
        }

        /// <summary>The map is playable — fill the bar and start the fade.</summary>
        public void Complete()
        {
            if (!Shown) return;
            Finished = true;
            Done = Total;
        }

        /// <summary>
        /// The build was abandoned (Retry, map switch, error). Take the cover down AT ONCE —
        /// no fade, because whoever cancelled is usually about to start another build and a
        /// half-faded ghost would sit on top of the new one.
        /// </summary>
        public void Cancel()
        {
            Shown = false;
            Finished = false;
            Alpha = 0f;
            Total = 0;
            Done = 0;
            Elapsed = 0f;
        }

        /// <summary>
        /// Advance the fade / watchdog by one frame. Returns true while anything is still drawn.
        /// </summary>
        public bool Tick(float dt)
        {
            if (!Shown) return false;
            if (dt > 0f) Elapsed += dt;

            if (Finished)
            {
                Alpha -= (dt > 0f ? dt : 0f) / FadeSeconds;
                if (Alpha <= 0f)
                {
                    Alpha = 0f;
                    Shown = false;
                    return false;
                }
                return true;
            }

            // Nothing finished this build and it has been up far too long: something upstream
            // died silently. Never leave the player under an opaque screen.
            if (Elapsed >= StuckSeconds)
            {
                Cancel();
                return false;
            }
            return true;
        }
    }
}
