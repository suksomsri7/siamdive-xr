using System.Collections;
using DiveMap.Core;
using UnityEngine;
using UnityEngine.UI;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// P0 — the web's <c>showToast()</c>: one short line near the top of the screen that
    /// fades away on its own (the web puts it at the bottom; here the bottom is the info card
    /// and, from P1, the joysticks). Built before the tour/game work because every mode from here on
    /// needs to say something transient ("แตะจุดยึดที่ 1", "ออฟไลน์ — เหรียญจะซิงก์ทีหลัง")
    /// and the alternative is another modal, which on a phone in one hand is worse.
    ///
    /// Deliberately NOT a queue: a second toast replaces the first, exactly like the web. A
    /// queue means the user reads message 3 about something they did 4 seconds ago.
    ///
    /// Text goes through <see cref="UiStrings.Tr"/> at the call site, and the row is sized with
    /// <see cref="UiKit.RowHeight"/> — a legacy Text row shorter than fontSize × 1.511 drops
    /// the whole line (the WO-XR-05 lesson), and a toast that renders empty is worse than none.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Toast : MonoBehaviour
    {
        private const int FontSize = 30;
        private const float HoldSeconds = 2.2f;
        private const float FadeSeconds = 0.35f;
        // TOP-centre, under the status header. The web puts its toast at the bottom, but on this
        // app the bottom belongs to the info card (a bottom sheet) and, from P1, to the joysticks
        // — the first QC shot showed the toast landing on top of the card.
        private const float TopMargin = 96f;

        private static Toast _instance;

        private CanvasGroup _group;
        private Text _label;
        private Coroutine _run;

        /// <summary>
        /// Show <paramref name="message"/> (already translated). Safe to call before any UI
        /// exists — it just no-ops rather than throwing, so game/tour code never has to guard.
        /// </summary>
        public static void Show(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            Toast t = Ensure();
            if (t == null)
            {
                Debug.Log($"[Ui] toast (no canvas yet): {message}");
                return;
            }
            t.Play(message);
        }

        /// <summary>Convenience: translate the Thai source string, then show it.</summary>
        public static void ShowTr(string thaiSource) => Show(UiStrings.Tr(thaiSource));

        private static Toast Ensure()
        {
            if (_instance != null) return _instance;

            UiShell shell = UiShell.Instance;
            RectTransform host = shell != null ? shell.OverlayRoot : null;
            if (host == null) return null;

            RectTransform node = UiKit.MakeNode(host, "Toast");
            node.anchorMin = new Vector2(0.5f, 1f);
            node.anchorMax = new Vector2(0.5f, 1f);
            node.pivot = new Vector2(0.5f, 1f);
            node.anchoredPosition = new Vector2(0f, -TopMargin);
            node.sizeDelta = new Vector2(900f, UiKit.RowHeight(FontSize, 2) + 28f);

            Image bg = UiKit.MakePanel(node, "Bg", UiKit.PanelBg);
            bg.rectTransform.anchorMin = Vector2.zero;
            bg.rectTransform.anchorMax = Vector2.one;
            bg.rectTransform.offsetMin = Vector2.zero;
            bg.rectTransform.offsetMax = Vector2.zero;

            Text label = UiKit.MakeText(node, "Label", "", FontSize, TextAnchor.MiddleCenter,
                                        UiKit.TextMain);
            RectTransform lrt = label.rectTransform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(24f, 12f);
            lrt.offsetMax = new Vector2(-24f, -12f);

            var group = node.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;   // never steal a tap meant for the map

            var toast = node.gameObject.AddComponent<Toast>();
            toast._group = group;
            toast._label = label;
            _instance = toast;
            return toast;
        }

        private void Play(string message)
        {
            _label.text = message;
            if (_run != null) StopCoroutine(_run);
            _run = StartCoroutine(Cycle());
        }

        private IEnumerator Cycle()
        {
            _group.alpha = 1f;
            yield return new WaitForSeconds(HoldSeconds);
            for (float t = 0f; t < FadeSeconds; t += Time.unscaledDeltaTime)
            {
                _group.alpha = 1f - t / FadeSeconds;
                yield return null;
            }
            _group.alpha = 0f;
            _run = null;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
