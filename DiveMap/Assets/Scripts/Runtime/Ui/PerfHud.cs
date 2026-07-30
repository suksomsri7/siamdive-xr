using DiveMap.Core;
using UnityEngine;
using UnityEngine.UI;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// A7 — the frame-rate readout (the web's <c>showPerfHud()</c>, builder.html:4059).
    ///
    /// Same place and same look as the web's: top-left, 12 px monospace green on black, showing
    /// <c>FPS 58 min41 · 1100 fish · d240</c>. The MINIMUM matters more than the average — an
    /// average of 55 with dips to 12 feels broken, and the average hides it.
    ///
    /// Off by default. It is turned on from Settings, and its real job is to let the user answer
    /// "how does it run on your phone?" with a number instead of an impression.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PerfHud : MonoBehaviour
    {
        public const string PrefKey = "perf_hud";

        private static readonly Color Bg  = new Color(0f, 0f, 0f, 0.62f);
        private static readonly Color Fg  = new Color(0.486f, 0.988f, 0.604f, 1f);   // #7CFC9A

        private static PerfHud _instance;

        private Text _txt;
        private float _accum;
        private int _frames;
        private int _fps;
        private int _min = 999;
        private float _next;

        /// <summary>Is the readout on? Remembered per device.</summary>
        public static bool Enabled
        {
            get => PlayerPrefs.GetInt(PrefKey, 0) != 0;
            set
            {
                PlayerPrefs.SetInt(PrefKey, value ? 1 : 0);
                PlayerPrefs.Save();
                Apply();
            }
        }

        /// <summary>Build or remove the readout to match <see cref="Enabled"/>.</summary>
        public static void Apply()
        {
            if (!Enabled)
            {
                if (_instance != null) Destroy(_instance.gameObject);
                _instance = null;
                return;
            }
            if (_instance != null) return;

            RectTransform layer = HudLayer.For(AppMode.View);
            if (layer == null) return;

            Image pill = UiKit.MakeRounded(layer, "PerfHud", Bg, 8f);
            pill.raycastTarget = false;
            RectTransform rt = pill.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            int font = UiKit.CssFont(12f);
            rt.sizeDelta = new Vector2(UiKit.Css(260f), UiKit.RowHeight(font) + UiKit.Css(10f));
            rt.anchoredPosition = new Vector2(UiKit.Css(6f), -UiKit.Css(8f));

            var hud = pill.gameObject.AddComponent<PerfHud>();
            hud._txt = UiKit.MakeLine(rt, "PerfText", "FPS —", font, TextAnchor.MiddleLeft, Fg);
            RectTransform trt = hud._txt.rectTransform;
            UiKit.Stretch(trt);
            trt.offsetMin = new Vector2(UiKit.Css(9f), 0f);
            trt.offsetMax = new Vector2(-UiKit.Css(9f), 0f);

            _instance = hud;
            Debug.Log("[Perf] hud on");
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void Update()
        {
            _accum += Time.unscaledDeltaTime;
            _frames++;
            if (_accum < 0.5f) return;

            _fps = Mathf.RoundToInt(_frames / _accum);
            _accum = 0f;
            _frames = 0;
            // The first half-second after a map loads is all decompression and upload; counting it
            // would pin the minimum at a number the player never actually experiences.
            if (Time.timeSinceLevelLoad > 3f && _fps < _min) _min = _fps;

            if (_txt == null || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.5f;

            var reef = FindFirstObjectByType<Marine.FishSchoolSystem>();
            int fish = reef != null ? reef.FishCount : 0;
            Camera cam = Camera.main;
            float d = cam != null ? cam.transform.position.y : 0f;

            _txt.text = $"FPS {_fps} min{(_min == 999 ? 0 : _min)} · {fish} fish · y{d:F0}";
        }
    }
}
