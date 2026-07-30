using System.Collections;
using System.Collections.Generic;
using DiveMap.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace DiveMap.Runtime
{
    /// <summary>
    /// P1.2b — sound. The eight clips already sit on the CDN the app loads maps from
    /// (<c>maps.siamdive.com/audio/</c>), so they are STREAMED rather than bundled: no APK growth,
    /// and no asset to hand-write a .meta for on a machine with no Unity Editor.
    ///
    /// Everything degrades to silence. A clip that fails to download is remembered as failed and
    /// never retried in a loop, mute is honoured before anything is even fetched, and no call
    /// site has to null-check — <see cref="Play"/> on a missing clip simply does nothing. Sound is
    /// the last thing that should be able to break a dive.
    ///
    /// Volumes, the animal-call radii and their cooldowns live in <see cref="DiveAudio"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AudioBank : MonoBehaviour
    {
        public const string MutePrefKey = "muted";

        private static AudioBank _instance;

        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        private readonly HashSet<string> _failed = new HashSet<string>();
        private readonly HashSet<string> _loading = new HashSet<string>();

        private AudioSource _ambience;
        private AudioSource _oneShot;

        public static bool Muted
        {
            get => PlayerPrefs.GetInt(MutePrefKey, 0) == 1;
            set
            {
                PlayerPrefs.SetInt(MutePrefKey, value ? 1 : 0);
                PlayerPrefs.Save();
                if (_instance == null) return;
                // NB: StopAmbience is static — calling it through _instance would not compile.
                if (value)
                {
                    if (_instance._ambience != null) _instance._ambience.Stop();
                }
                else if (_instance._ambienceWanted) _instance.PlayAmbienceNow();
            }
        }

        private bool _ambienceWanted;

        public static AudioBank Ensure()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("AudioBank");
            _instance = go.AddComponent<AudioBank>();
            return _instance;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;

            _ambience = gameObject.AddComponent<AudioSource>();
            _ambience.loop = true;
            _ambience.playOnAwake = false;
            _ambience.volume = DiveAudio.AmbienceVolume;
            _ambience.spatialBlend = 0f;   // 2D bed

            _oneShot = gameObject.AddComponent<AudioSource>();
            _oneShot.loop = false;
            _oneShot.playOnAwake = false;
            _oneShot.spatialBlend = 0f;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        // ── public API ───────────────────────────────────────────────────────────

        /// <summary>Play a short effect by web name ("coin", "click", "humpback"…).</summary>
        public static void PlaySfx(string name, float volume = -1f)
        {
            if (Muted) return;
            AudioBank b = Ensure();
            string clip = DiveAudio.SfxClip(name);
            b.WithClip(clip, c =>
            {
                if (c == null || b._oneShot == null) return;
                b._oneShot.PlayOneShot(c, volume >= 0f ? Mathf.Clamp01(volume) : DiveAudio.SfxVolume(name));
            });
        }

        /// <summary>The drone start cue (entering the tour).</summary>
        public static void PlayCue()
        {
            if (Muted) return;
            AudioBank b = Ensure();
            b.WithClip(DiveAudio.Cue, c =>
            {
                if (c != null && b._oneShot != null) b._oneShot.PlayOneShot(c, DiveAudio.CueVolume);
            });
        }

        /// <summary>Start the looping underwater bed (idempotent).</summary>
        public static void StartAmbience()
        {
            AudioBank b = Ensure();
            b._ambienceWanted = true;
            if (Muted) return;
            b.PlayAmbienceNow();
        }

        /// <summary>Stop the bed (leaving the tour).</summary>
        public static void StopAmbience()
        {
            if (_instance == null) return;
            _instance._ambienceWanted = false;
            if (_instance._ambience != null) _instance._ambience.Stop();
        }

        private void PlayAmbienceNow()
        {
            WithClip(DiveAudio.Ambience, c =>
            {
                if (c == null || _ambience == null || !_ambienceWanted || Muted) return;
                if (_ambience.isPlaying && _ambience.clip == c) return;
                _ambience.clip = c;
                _ambience.volume = DiveAudio.AmbienceVolume;
                _ambience.Play();
            });
        }

        // ── streaming ────────────────────────────────────────────────────────────

        private void WithClip(string clip, System.Action<AudioClip> then)
        {
            if (_clips.TryGetValue(clip, out AudioClip cached)) { then(cached); return; }
            if (_failed.Contains(clip)) return;         // asked once, it did not work: stay silent
            if (_loading.Contains(clip)) return;        // a second request while in flight is a no-op
            StartCoroutine(Fetch(clip, then));
        }

        private IEnumerator Fetch(string clip, System.Action<AudioClip> then)
        {
            _loading.Add(clip);
            string url = DiveAudio.Url(clip);
            using (UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG))
            {
                var handler = (DownloadHandlerAudioClip)req.downloadHandler;
                handler.streamAudio = true;             // start playing before the tail arrives
                req.timeout = 20;
                yield return req.SendWebRequest();

                _loading.Remove(clip);
                if (req.result != UnityWebRequest.Result.Success)
                {
                    _failed.Add(clip);
                    Debug.LogWarning($"[Audio] {clip} unavailable ({req.error}) — continuing silent");
                    yield break;
                }

                AudioClip c = handler.audioClip;
                if (c == null)
                {
                    _failed.Add(clip);
                    Debug.LogWarning($"[Audio] {clip} decoded to nothing — continuing silent");
                    yield break;
                }
                c.name = clip;
                _clips[clip] = c;
                Debug.Log($"[Audio] loaded {clip} ({c.length:F1}s)");
                then(c);
            }
        }

        // ── animal proximity calls ───────────────────────────────────────────────

        private readonly Dictionary<Transform, float> _lastCall = new Dictionary<Transform, float>();
        private float _nextScan;

        /// <summary>
        /// Whale/dolphin calls when the diver comes near, with the web's per-animal cooldowns.
        /// Scanned every 0.3 s (the web's cadence) rather than per frame — the animals move slowly
        /// and this walks the scene's big-animal list.
        /// </summary>
        public static void ProximityTick(Vector3 listener, IList<Transform> animals, IList<string> ids)
        {
            if (Muted || _instance == null || animals == null || ids == null) return;
            AudioBank b = _instance;
            if (Time.time < b._nextScan) return;
            b._nextScan = Time.time + 0.3f;

            int n = Mathf.Min(animals.Count, ids.Count);
            for (int i = 0; i < n; i++)
            {
                Transform t = animals[i];
                if (t == null) continue;
                if (!DiveAudio.TryMatch(ids[i], out DiveAudio.AnimalCall call)) continue;

                float d = Vector3.Distance(listener, t.position);
                float last = b._lastCall.TryGetValue(t, out float seen) ? seen : -999f;
                if (!DiveAudio.ShouldPlay(call, d, Time.time, last)) continue;

                b._lastCall[t] = Time.time;
                PlaySfx(call.Sfx, DiveAudio.ProximityVolume(call.Sfx, d, call.Radius));
                Debug.Log($"[Audio] call {call.Sfx} d={d:F0} of {call.Radius:F0}");
            }
        }
    }
}
