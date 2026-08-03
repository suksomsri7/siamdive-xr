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
    }
}
