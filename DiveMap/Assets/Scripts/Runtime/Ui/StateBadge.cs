using DiveMap.Core;
using UnityEngine;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// One line of state, on screen, when the world looks wrong (WO-MERGE P1e).
    ///
    /// 🔴 Why this exists, and it is the same reason <see cref="FpsBadge"/> exists. Four device
    /// rounds were spent on "the second map is blank" and every one of them ended with a
    /// screenshot that could not distinguish "the map never loaded" from "the map loaded and is
    /// being rendered inside a fog wall". The two have opposite fixes. The round that finally
    /// cracked it did so because the HUD happened to be in frame — the minimap showed item dots,
    /// so the data was plainly there. That was luck, and luck is not an instrument.
    ///
    /// So the next screenshot answers the question by itself: when there is no map root, or when
    /// the frame has been near-uniform for more than three seconds, this prints the handful of
    /// values that separate every candidate explanation — which map was asked for versus which one
    /// is loaded, whether a build is running, whether one is queued, what mode owns the screen,
    /// and the fog and ambient that would produce exactly this picture.
    ///
    /// Library mode only, so it can never appear in the standalone product. <c>badge:1</c> in the
    /// host's boot payload shows it there too, through the same switch as the fps badge — the two
    /// are one debug surface with one control.
    /// </summary>
    public sealed class StateBadge : MonoBehaviour
    {
        /// <summary>How long the picture must stay flat before the line appears.</summary>
        private const float FlatSecondsBeforeShowing = 3f;

        /// <summary>How often to sample the frame. Cheap, but not free — see <see cref="Sample"/>.</summary>
        private const float SampleIntervalSeconds = 0.5f;

        private const int SampleGrid = 6;   // 6×6 = 36 readbacks per sample

        private float _nextSampleAt;
        private float _flatSince = -1f;
        private string _line = "";
        private GUIStyle _style;
        private Texture2D _probe;

        public static void Ensure()
        {
            if (Object.FindFirstObjectByType<StateBadge>() != null) return;
            var go = new GameObject("StateBadge");
            DontDestroyOnLoad(go);
            go.AddComponent<StateBadge>();
        }

        /// <summary>
        /// Only where a debug line belongs: inside somebody else's app, where the 3D screen is one
        /// route among many and a user can hit this by accident. In the standalone build the fps
        /// badge is already on screen and the player is a tester by definition.
        /// </summary>
        private static bool Allowed => NativeBridge.EmbeddedInHost || NativeBoot.BadgeForced;

        private void Update()
        {
            if (!Allowed) return;
            if (Time.unscaledTime < _nextSampleAt) return;
            _nextSampleAt = Time.unscaledTime + SampleIntervalSeconds;

            bool noMap = GameObject.Find("Map") == null;
            bool flat = noMap || IsFrameUniform();

            if (!flat) { _flatSince = -1f; _line = ""; return; }
            if (_flatSince < 0f) _flatSince = Time.unscaledTime;
            if (Time.unscaledTime - _flatSince < FlatSecondsBeforeShowing) return;

            _line = Compose(noMap);
        }

        /// <summary>
        /// Everything needed to tell the candidate explanations apart, in one line short enough to
        /// survive a phone screenshot.
        /// </summary>
        private static string Compose(bool noMap)
        {
            AppBoot boot = FindFirstObjectByType<AppBoot>();
            string want = NativeBoot.Current.ShortId;
            string have = boot != null ? boot.CurrentMapId : "-";

            return $"want {(string.IsNullOrEmpty(want) ? "-" : want)} · have {have}" +
                   $" · {(noMap ? "NO MAP ROOT" : "map built")}" +
                   $" · building {(boot != null && boot.IsBuilding ? "Y" : "N")}" +
                   $" · mode {ModeManager.Current}" +
                   $" · {SceneAtmosphere.StateLine()}" +
                   $" · lib {(NativeBoot.LibraryMode ? "Y" : "N")}";
        }

        /// <summary>
        /// Is the picture one flat colour? Sampled on a coarse grid rather than measured properly:
        /// this runs on a phone twice a second, and the question is not "how uniform" but "is
        /// there anything in the frame at all". 36 pixels answer that.
        ///
        /// ⚠️ Reads the back buffer, which costs a GPU sync. Hence the interval, and hence the
        /// whole thing being behind <see cref="Allowed"/> — this never runs in the shipped
        /// standalone app.
        /// </summary>
        private bool IsFrameUniform()
        {
            int w = Screen.width, h = Screen.height;
            if (w < SampleGrid || h < SampleGrid) return false;

            if (_probe == null) _probe = new Texture2D(SampleGrid, SampleGrid, TextureFormat.RGB24, false);

            try
            {
                // One strided read would need a RenderTexture; instead take a small block from the
                // middle of the screen, which is where a fog wall is most complete (the HUD lives
                // at the edges and would defeat the test).
                int x = Mathf.Max(0, w / 2 - SampleGrid / 2);
                int y = Mathf.Max(0, h / 2 - SampleGrid / 2);
                _probe.ReadPixels(new Rect(x, y, SampleGrid, SampleGrid), 0, 0, false);
                _probe.Apply(false);
            }
            catch (System.Exception)
            {
                // A readback can fail outside the render loop on some drivers. Never claim "flat"
                // on no evidence — a false positive would put a debug line over a healthy map.
                return false;
            }

            QcBlank.Frame f = QcBlank.Measure(_probe.GetRawTextureData());
            return QcBlank.IsBlank(f);
        }

        private void OnGUI()
        {
            if (!Allowed || string.IsNullOrEmpty(_line)) return;

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.Max(9, Mathf.RoundToInt(Screen.height * 0.016f)),
                    alignment = TextAnchor.UpperLeft,
                    wordWrap = true,
                };
            }
            // Amber on black: it has to be legible over whatever flat colour is behind it, and it
            // has to look like a diagnostic rather than part of the product.
            _style.normal.textColor = new Color(1f, 0.78f, 0.25f, 0.95f);

            float pad = Screen.height * 0.012f;
            var rect = new Rect(pad, Screen.height * 0.28f, Screen.width - pad * 2f,
                                Screen.height * 0.12f);
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(rect, _line, _style);
        }
    }
}
