using DiveMap.Runtime.Marine;
using UnityEngine;

namespace DiveMap.Runtime
{
    /// <summary>
    /// P2 — the web's ☀️/💧 environment switch (<c>setEnv()</c>, builder.html:678-697). Two looks
    /// for the same map:
    ///
    ///   UNDERWATER (default) — the gradient backdrop, fog 0x123a55, hemi 0xbfe6ff/0x123040 at
    ///   1.05, sun 0xfff3df at 1.2, the water surface visible.
    ///   DAYLIGHT — flat sky 0xbfe2ef, NO fog at all (the map never fades however far you zoom),
    ///   hemi white/0xd8c9a8 at 1.2, sun white at 1.6, no water surface — and the fish hidden,
    ///   because without water they would be swimming in mid-air. That last line is the web's
    ///   own reasoning and it is the detail that makes the mode look deliberate.
    ///
    /// The values are captured from the scene the first time so switching back restores exactly
    /// what WO-XR-04 tuned, rather than a second hard-coded copy of it that can drift.
    /// </summary>
    public static class EnvMode
    {
        private static readonly Color AirSky   = new Color(0.749f, 0.886f, 0.937f); // 0xbfe2ef
        private static readonly Color AirHemi  = Color.white;
        private static readonly Color AirGround = new Color(0.847f, 0.788f, 0.659f); // 0xd8c9a8
        private static readonly Color AirSun   = Color.white;

        private static bool _daylight;
        private static bool _saved;

        private static Color _sky, _equator, _ground, _sunColor;
        private static bool _fog;
        private static float _sunIntensity = 1f;
        private static Light _sun;

        public static bool Daylight => _daylight;

        /// <summary>Toggle daylight/underwater. Returns the state it ended in.</summary>
        public static bool Toggle() => Set(!_daylight);

        public static bool Set(bool daylight)
        {
            Capture();
            _daylight = daylight;

            Camera cam = Camera.main;
            Backdrop backdrop = cam != null ? cam.GetComponent<Backdrop>() : null;

            if (daylight)
            {
                RenderSettings.fog = false;                       // the web drops fog entirely
                RenderSettings.ambientSkyColor = AirHemi * 1.2f;
                RenderSettings.ambientEquatorColor = Color.Lerp(AirHemi, AirGround, 0.5f) * 1.2f;
                RenderSettings.ambientGroundColor = AirGround * 1.2f;
                if (_sun != null) { _sun.color = AirSun; _sun.intensity = 1.6f; }
                if (cam != null) cam.backgroundColor = AirSky;
                if (backdrop != null) backdrop.SetVisible(false);  // flat sky, not the gradient
            }
            else
            {
                RenderSettings.fog = _fog;
                RenderSettings.ambientSkyColor = _sky;
                RenderSettings.ambientEquatorColor = _equator;
                RenderSettings.ambientGroundColor = _ground;
                if (_sun != null) { _sun.color = _sunColor; _sun.intensity = _sunIntensity; }
                if (backdrop != null) backdrop.SetVisible(true);
            }

            SetWaterVisible(!daylight);
            SetFishVisible(!daylight);

            Debug.Log($"[Scene] env={(daylight ? "daylight" : "underwater")} fog={RenderSettings.fog}");
            return _daylight;
        }

        private static void Capture()
        {
            if (_saved) return;
            _fog = RenderSettings.fog;
            _sky = RenderSettings.ambientSkyColor;
            _equator = RenderSettings.ambientEquatorColor;
            _ground = RenderSettings.ambientGroundColor;

            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type != LightType.Directional || l.gameObject.name == "FillLight") continue;
                _sun = l;
                _sunColor = l.color;
                _sunIntensity = l.intensity;
                break;
            }
            _saved = true;
        }

        /// <summary>A new map means new objects — forget the old scene's captured values.</summary>
        public static void Reset()
        {
            _saved = false;
            _daylight = false;
        }

        private static void SetWaterVisible(bool visible)
        {
            GameObject map = GameObject.Find("Map");
            if (map == null) return;
            Transform water = map.transform.Find("Water");
            if (water != null) water.gameObject.SetActive(visible);
        }

        /// <summary>
        /// Fish follow the water: with the surface gone they would be swimming in mid-air (the
        /// web hides its swimmers for exactly this reason).
        /// </summary>
        private static void SetFishVisible(bool visible)
        {
            FishSchoolSystem reef = Object.FindFirstObjectByType<FishSchoolSystem>();
            if (reef != null) reef.enabled = visible;

            foreach (WhaleController w in Object.FindObjectsByType<WhaleController>(FindObjectsSortMode.None))
            {
                Renderer[] rends = w.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < rends.Length; i++) rends[i].enabled = visible;
            }
        }
    }
}
