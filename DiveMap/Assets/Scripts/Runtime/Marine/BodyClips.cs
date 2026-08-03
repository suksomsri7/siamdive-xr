using System.Collections.Generic;
using DiveMap.Core;
using UnityEngine;

namespace DiveMap.Runtime.Marine
{
    /// <summary>
    /// The animal's OWN baked animation, played off the GLB.
    ///
    /// 🔴 Everything Unity-specific about clip playback lives here and nothing else does: the
    /// rules (which clip, how fast, clip-or-wave) are in <see cref="ClipPlay"/> where
    /// <c>tools/test.sh</c> can assert them on a machine with no Unity in it.
    ///
    /// ── How the clips get here (verified against glTFast 6.19.0, not assumed) ─────────────────
    ///
    /// <c>ImportSettings.animationMethod</c> defaults to <c>AnimationMethod.Legacy</c>
    /// (ImportSettings.cs:85), so <c>GltfImport.Load()</c> with no settings builds LEGACY
    /// <see cref="AnimationClip"/>s (<c>Animations/AnimationModuleProcessor.cs:52-62</c> —
    /// <c>legacy = true, wrapMode = WrapMode.Loop</c>). At instantiation
    /// <c>GameObjectInstantiator.AddAnimation</c> (GameObjectInstantiator.cs:126-146) sees the
    /// legacy flag, adds a plain <see cref="Animation"/> component to the GLB's SCENE root — a
    /// CHILD of the item pivot, which is why this binds with
    /// <c>GetComponentInChildren</c> — and registers every clip under its own name.
    ///
    /// 🔴 …and all of that is compiled out unless <c>UNITY_ANIMATION</c> is defined, which comes
    /// from <c>com.unity.modules.animation</c> being in the project manifest
    /// (glTFast.asmdef <c>versionDefines</c>). It was NOT in ours. That single missing line is why
    /// "nothing ever played a clip" was true no matter what any of this code did, and it is added
    /// in <c>DiveMap/Packages/manifest.json</c> in the same change as this file. Without it
    /// <see cref="Bind"/> reports <c>no-animation-component</c> for every model on earth and the
    /// wave shader quietly takes over — which is exactly today's behaviour, so the failure is a
    /// regression to the old look rather than a broken scene.
    /// </summary>
    public sealed class BodyClips
    {
        private Animation _anim;
        private string[] _names = System.Array.Empty<string>();
        private AnimationState[] _states = System.Array.Empty<AnimationState>();
        private int _cur = -1;
        private ClipRole _curRole = ClipRole.Cruise;
        private float _speed = -1f;

        /// <summary>How many clips this model actually shipped.</summary>
        public int Count => _names.Length;

        /// <summary>True when there is a clip playing and the wave shader must stay out of it.</summary>
        public bool Active => _anim != null && _states.Length > 0;

        /// <summary>Name of the clip currently selected, or <c>-</c>.</summary>
        public string CurrentName => _cur >= 0 && _cur < _names.Length ? _names[_cur] : "-";

        /// <summary>Length of the selected clip in seconds, or 0.</summary>
        public float CurrentLength =>
            _cur >= 0 && _cur < _states.Length && _states[_cur] != null ? _states[_cur].length : 0f;

        /// <summary>Every clip name, in the GLB's own order — for the log line.</summary>
        public string NamesCsv => _names.Length == 0 ? "-" : string.Join(",", _names);

        /// <summary>The same names as an array, for <see cref="ClipPlay.ProbeLine"/>.</summary>
        public string[] NamesArray => _names;

        /// <summary>
        /// Find the GLB's animation and take it over. Returns a one-token reason on failure and
        /// <c>-</c> on success; never throws, and a failure leaves <see cref="Active"/> false so
        /// the caller falls back to the wave without a branch of its own.
        /// </summary>
        public string Bind(GameObject root)
        {
            _anim = null;
            _names = System.Array.Empty<string>();
            _states = System.Array.Empty<AnimationState>();
            _cur = -1;
            _speed = -1f;

            if (root == null) return "no-object";

            // true = include inactive: the pivot's children are active by the time a solo animal is
            // wired up, but the hero path attaches during the load and a one-frame difference in
            // activation state is not something this should depend on.
            _anim = root.GetComponentInChildren<Animation>(true);
            if (_anim == null) return "no-animation-component";

            var names = new List<string>(8);
            var states = new List<AnimationState>(8);
            foreach (AnimationState st in _anim)
            {
                if (st == null || st.clip == null) continue;
                names.Add(st.name);
                states.Add(st);
            }
            if (states.Count == 0) { _anim = null; return "no-clips"; }

            _names = names.ToArray();
            _states = states.ToArray();

            // glTFast already writes WrapMode.Loop onto the clip, but a clip is shared between
            // every instance of one import and an AnimationState is not — pinning it here means a
            // second turtle cannot inherit a wrap mode somebody else changed.
            for (int i = 0; i < _states.Length; i++)
                if (_states[i] != null) _states[i].wrapMode = WrapMode.Loop;

            // The component defaults to playAutomatically, so clip 0 (whatever that is) is already
            // running. Stop it: the first Play() below is what decides the opening pose, and a
            // cross-fade out of an unrelated clip on frame one reads as a twitch.
            _anim.playAutomatically = false;
            _anim.Stop();

            // 🔴 AlwaysAnimate, stated rather than inherited. It IS the default, but the default is
            // exactly the kind of thing a future Unity upgrade changes quietly, and the failure it
            // would produce — a rig that animates when you look at it and freezes when you do not —
            // is invisible in a screenshot and impossible to reproduce on purpose. The QC pass
            // measures an off-screen model with nothing rendering it, so without this the oracle
            // would be measuring the culling rule instead of the clip.
            _anim.cullingType = AnimationCullingType.AlwaysAnimate;
            return "-";
        }

        /// <summary>
        /// Start the clip for <paramref name="role"/>. The first call snaps; later calls
        /// cross-fade over <see cref="ClipPlay.CrossFadeSec"/>. Re-selecting the role that is
        /// already playing does nothing at all — the web's <c>if(next===u.curAction) return</c>
        /// (builder.html:2110), without which every frame would restart the blend and the animal
        /// would hold frame 0 forever.
        /// </summary>
        public void Play(ClipRole role)
        {
            if (!Active) return;
            int idx = ClipPlay.IndexOf(_names, role);
            if (idx < 0 || idx >= _names.Length) return;
            if (idx == _cur) { _curRole = role; return; }

            bool first = _cur < 0;
            _cur = idx;
            _curRole = role;
            if (first) _anim.Play(_names[idx]);
            else _anim.CrossFade(_names[idx], ClipPlay.CrossFadeSec);
        }

        /// <summary>The role currently selected — for the log line.</summary>
        public ClipRole CurrentRole => _curRole;

        /// <summary>
        /// Set playback speed on EVERY state, not just the current one: during a cross-fade two
        /// clips are running and a fast burst blending into a slow-motion cruise is a stutter with
        /// a plausible-looking cause. Skipped when the number has not moved, so a cruising animal
        /// costs nothing per frame.
        /// </summary>
        public void SetSpeed(float speed)
        {
            if (!Active) return;
            if (_speed >= 0f && Mathf.Abs(speed - _speed) < 1e-3f) return;
            _speed = speed;
            for (int i = 0; i < _states.Length; i++)
                if (_states[i] != null) _states[i].speed = speed;
        }

        /// <summary>
        /// Offset the cycle so two animals of one species are not in lockstep — the web's
        /// <c>mx.setTime(Math.random()*duration)</c> (builder.html:1524), except seeded from the
        /// PLACEMENT rather than Math.random so the same map replays the same reef and a QC
        /// screenshot means something.
        /// </summary>
        public void SeedPhase(uint seed)
        {
            if (!Active || _cur < 0) return;
            AnimationState st = _states[_cur];
            if (st == null || st.length <= 0f) return;
            st.time = (float)FishMindPhase(seed) * st.length;
        }

        /// <summary>0..1 from the placement seed. Same hash family as the rest of the reef.</summary>
        private static double FishMindPhase(uint seed) => FishMind.Rand01(seed, 0, 77);

        // ── CI oracle (QcAnimShot) ────────────────────────────────────────────────

        /// <summary>Clip time in seconds, or 0. The live probe reads this before and after.</summary>
        public float CurrentTime =>
            _cur >= 0 && _cur < _states.Length && _states[_cur] != null ? _states[_cur].time : 0f;

        /// <summary>
        /// Pose the rig BY HAND at t=0 and again at <paramref name="frac"/>·length, and return the
        /// largest distance any bone moved between the two.
        ///
        /// 🔴 Deterministic, and that is the whole value of it. It does not wait for a frame, does
        /// not depend on <c>Time.deltaTime</c>, and cannot be fooled by a CI machine running at
        /// 3 fps — <c>Animation.Sample()</c> writes the pose synchronously. So a zero from this
        /// method means the CURVES are empty or aimed at joints that are not these joints, which
        /// is a completely different bug from "the app never ticked it", and the two used to be
        /// indistinguishable.
        ///
        /// Leaves the clip exactly where it found it, so calling it does not disturb playback.
        /// </summary>
        public double PoseDelta(Transform[] bones, float frac)
        {
            if (!Active || _cur < 0 || bones == null || bones.Length == 0) return 0.0;
            AnimationState st = _states[_cur];
            if (st == null || st.length <= 0f) return 0.0;

            float saved = st.time;

            st.time = 0f;
            _anim.Sample();
            Vector3[] a = Capture(bones);

            st.time = Mathf.Clamp01(frac) * st.length;
            _anim.Sample();
            double moved = MaxMove(bones, a);

            st.time = saved;
            _anim.Sample();
            return moved;
        }

        /// <summary>World position of every bone, for a before/after comparison.</summary>
        public static Vector3[] Capture(Transform[] bones)
        {
            if (bones == null) return System.Array.Empty<Vector3>();
            var p = new Vector3[bones.Length];
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null) p[i] = bones[i].position;
            return p;
        }

        /// <summary>
        /// Largest distance any bone has moved since <paramref name="before"/> was captured.
        /// MAX and not mean: a swim cycle moves the tail a long way and the skull barely at all,
        /// so an average over a 5-joint rig is dominated by the joints that are supposed to be
        /// still and would report "frozen" for a perfectly good clip.
        /// </summary>
        public static double MaxMove(Transform[] bones, Vector3[] before)
        {
            if (bones == null || before == null) return 0.0;
            int n = Mathf.Min(bones.Length, before.Length);
            double worst = 0.0;
            for (int i = 0; i < n; i++)
            {
                if (bones[i] == null) continue;
                double d = (bones[i].position - before[i]).magnitude;
                if (d > worst) worst = d;
            }
            return worst;
        }
    }
}
