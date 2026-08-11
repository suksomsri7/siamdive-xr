using DiveMap.Core;
using UnityEngine;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// A one-line answer to "why can I not see the map?", on screen, for the person holding the
    /// phone (WO-MERGE DARK).
    ///
    /// 🔴 REWRITTEN after the first version misfired on the user. It used to sample a 6×6 block
    /// of pixels from the CENTRE of the screen and call the frame "flat" if they matched. The user
    /// photographed it complaining about "ข้อความสีเหลืองที่มีปัญหา" over a map that was rendering
    /// perfectly — wreck, sand, schools, water, all there. What was flat in the middle of that
    /// frame was the object INFO CARD, a large dark panel that opens exactly where the probe was
    /// looking. The probe could not tell the world from the UI drawn over it, because a back-buffer
    /// read cannot: ScreenSpaceOverlay canvases are in it. (QcBlankShot already knew this and
    /// renders the camera to its own target for precisely this reason.)
    ///
    /// So the pixel heuristic is GONE — not fixed, removed. It cost a GPU readback twice a second
    /// on a phone to answer a question pixels answer badly, and the structural signals it was
    /// standing in for are cheaper, unambiguous, and each names a DIFFERENT bug:
    ///
    ///     no map root                → the build produced nothing, or something destroyed it
    ///     root exists, inactive      → something switched the whole map off
    ///     root active, 0 renderers   → it was built but nothing in it can draw
    ///     still building after 25 s  → the load is stuck, not the rendering
    ///
    /// Those four are what separate the surviving explanations. Anything else — a map that is
    /// built, active and full of live renderers — means the fault is in the camera, the backdrop
    /// or the atmosphere, and the line says so and prints the numbers that tell those apart.
    ///
    /// Library mode only, so it can never appear in the standalone product; <c>badge:1</c> shows
    /// it in standalone too, through the same switch as the fps badge.
    /// </summary>
    public sealed class StateBadge : MonoBehaviour
    {
        /// <summary>How long a bad state must persist before saying anything.</summary>
        private const float BadSecondsBeforeShowing = 3f;

        /// <summary>A load that has not finished by now is stuck, not slow.</summary>
        private const float StuckBuildSeconds = 25f;

        /// <summary>Structural checks are cheap, but not free — twice a second is plenty.</summary>
        private const float SampleIntervalSeconds = 0.5f;

        private float _nextSampleAt;
        private float _badSince = -1f;
        private float _buildingSince = -1f;
        private string _line = "";
        private GUIStyle _style;

        public static void Ensure()
        {
            if (Object.FindFirstObjectByType<StateBadge>() != null) return;
            var go = new GameObject("StateBadge");
            DontDestroyOnLoad(go);
            go.AddComponent<StateBadge>();
        }

        private static bool Allowed => NativeBridge.EmbeddedInHost || NativeBoot.BadgeForced;

        private void Update()
        {
            if (!Allowed) return;
            if (Time.unscaledTime < _nextSampleAt) return;
            _nextSampleAt = Time.unscaledTime + SampleIntervalSeconds;

            AppBoot boot = FindFirstObjectByType<AppBoot>();

            // "Stuck" is measured, not guessed: a build that is still running after 25 s is a
            // different fault from one that finished and drew nothing.
            bool building = boot != null && boot.IsBuilding;
            if (!building) _buildingSince = -1f;
            else if (_buildingSince < 0f) _buildingSince = Time.unscaledTime;
            bool stuck = building && Time.unscaledTime - _buildingSince > StuckBuildSeconds;

            DarkTrace.Snapshot s = DarkTrace.Last;
            // A build in flight legitimately has no map root; only judge the root when nothing is
            // loading, or the badge would accuse every normal map switch of failing.
            bool rootBad = !building && s.State != DarkTrace.MapState.Live;

            if (!rootBad && !stuck) { _badSince = -1f; _line = ""; return; }
            if (_badSince < 0f) _badSince = Time.unscaledTime;
            if (Time.unscaledTime - _badSince < BadSecondsBeforeShowing) return;

            // Refresh the structural facts only now that something looks wrong — this is the one
            // place the trace is worth gathering, and it also puts a full [DarkTrace] block in the
            // device log at the moment the user is looking at the problem.
            s = DarkTrace.Log("badge");
            _line = Compose(boot, s, stuck);
        }

        /// <summary>
        /// Thai first and plainly worded: the reader is the user, and the planner has asked them
        /// to photograph this. Everything a developer needs to tell the candidates apart is on the
        /// same line, after the sentence.
        /// </summary>
        private static string Compose(AppBoot boot, DarkTrace.Snapshot s, bool stuck)
        {
            string want = NativeBoot.Current.ShortId;
            string have = boot != null ? boot.CurrentMapId : "-";
            string why = stuck ? "โหลดแมพค้าง (ยังโหลดไม่เสร็จ)" : DarkTrace.Explain(s);

            return "⚠ " + why +
                   $"\nแมพที่ขอ {(string.IsNullOrEmpty(want) ? "-" : want)} · ที่เปิดอยู่ {have}" +
                   $" · root {s.Roots} {(s.State == DarkTrace.MapState.Live ? "ok" : s.State.ToString())}" +
                   $" · วาดได้ {s.EnabledRenderers}/{s.Renderers}" +
                   $" · กล้อง {(s.CameraOn ? "on" : "OFF")} {s.Near:F1}-{s.Far:F0}" +
                   $" · ฉากหลัง {s.Backdrops}@{s.BackdropDist:F1}{(s.BackdropZWrite ? " ZW" : "")}" +
                   $" · {SceneAtmosphere.StateLine()}";
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
            _style.normal.textColor = new Color(1f, 0.78f, 0.25f, 0.95f);

            float pad = Screen.height * 0.012f;
            var rect = new Rect(pad, Screen.height * 0.26f, Screen.width - pad * 2f,
                                Screen.height * 0.18f);
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(rect, _line, _style);
        }
    }
}
