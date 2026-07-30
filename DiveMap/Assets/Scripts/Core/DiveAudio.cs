using System;

namespace DiveMap.Core
{
    /// <summary>
    /// P1.2b — which sound plays, how loud, and when it is allowed to play again. Ported from the
    /// web (builder.html 4366-4397) and pure, because "why did the whale bark twelve times"
    /// should be a test, not a bug report.
    ///
    /// The clips themselves already exist on the CDN the app is loading maps from
    /// (<c>maps.siamdive.com/audio/*.mp3</c>, 8 files), so nothing is added to the APK.
    /// </summary>
    public static class DiveAudio
    {
        public const string BaseUrl = "https://maps.siamdive.com/audio/";

        // Web volumes: ambience 0.5 (looping), cue 0.85, and the SFX table at :4379.
        public const float AmbienceVolume = 0.5f;
        public const float CueVolume = 0.85f;

        public const string Ambience = "underwater_ambience";
        public const string Cue = "drone_start_cue";

        /// <summary>Clip file name (no extension) → url.</summary>
        public static string Url(string clip) => BaseUrl + clip + ".mp3";

        /// <summary>Default volume for one of the short effects (web <c>_SFXVOL</c>).</summary>
        public static float SfxVolume(string name)
        {
            switch (name)
            {
                case "coin":     return 0.7f;
                case "humpback": return 0.9f;
                case "dolphin":  return 0.85f;
                case "sperm":    return 0.9f;
                case "click":    return 0.4f;
                default:         return 1f;
            }
        }

        /// <summary>File name for an effect: the web stores them as <c>sfx_&lt;name&gt;.mp3</c>.</summary>
        public static string SfxClip(string name) => "sfx_" + name;

        // ── animal proximity calls (builder.html 4383-4393) ───────────────────────

        /// <summary>One animal call: which asset ids trigger it, how far it carries, how often.</summary>
        public struct AnimalCall
        {
            public string Match;      // substring of the assetId
            public string Sfx;        // effect name
            public float Radius;      // units
            public float Cooldown;    // seconds
        }

        /// <summary>The web's table, in its order (first match wins per animal).</summary>
        public static readonly AnimalCall[] Animals =
        {
            new AnimalCall { Match = "humpback", Sfx = "humpback", Radius = 140f, Cooldown = 16f },
            new AnimalCall { Match = "sperm",    Sfx = "sperm",    Radius = 125f, Cooldown = 13f },
            new AnimalCall { Match = "dolphin",  Sfx = "dolphin",  Radius = 95f,  Cooldown = 11f },
        };

        /// <summary>
        /// Volume for a call heard at <paramref name="distance"/> within <paramref name="radius"/>:
        /// linear falloff to a floor of 0.12, so a whale at the edge of earshot is faint but never
        /// silent (the web's own curve).
        /// </summary>
        public static float ProximityVolume(string sfx, float distance, float radius)
        {
            if (radius <= 0.01f) return 0f;
            float fall = 1f - distance / radius;
            if (fall < 0.12f) fall = 0.12f;
            if (fall > 1f) fall = 1f;
            float v = SfxVolume(sfx) * fall;
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }

        /// <summary>
        /// Whether a call may fire: the animal has to be inside its radius and its own cooldown
        /// must have elapsed. <paramref name="lastPlayed"/> is per-animal, exactly like the web's
        /// per-object <c>_sfxT</c> — one dolphin going off does not silence the others.
        /// </summary>
        public static bool ShouldPlay(in AnimalCall call, float distance, float now, float lastPlayed)
            => distance < call.Radius && now - lastPlayed > call.Cooldown;

        /// <summary>Find the call an assetId triggers, if any.</summary>
        public static bool TryMatch(string assetId, out AnimalCall call)
        {
            call = default;
            if (string.IsNullOrEmpty(assetId)) return false;
            string id = assetId.ToLowerInvariant();
            for (int i = 0; i < Animals.Length; i++)
            {
                if (id.IndexOf(Animals[i].Match, StringComparison.Ordinal) < 0) continue;
                call = Animals[i];
                return true;
            }
            return false;
        }
    }
}
