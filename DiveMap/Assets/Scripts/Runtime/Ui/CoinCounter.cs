using System.Collections;
using DiveMap.Core;
using UnityEngine;
using UnityEngine.UI;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// The web's coin badge (<c>coinUI</c>, builder.html:4106): centred at the top, black 60%,
    /// gold #ffd54a bold 14 px, 9 px radius, 6/11 padding — plus the "+N" that flies from the
    /// pickup toward the badge, which is the bit that makes collecting feel like scoring.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CoinCounter : MonoBehaviour
    {
        private static readonly Color Gold = new Color(1f, 0.835f, 0.290f);   // #ffd54a

        private static CoinCounter _instance;

        private RectTransform _rt;
        private Text _label;

        public static CoinCounter Ensure()
        {
            if (_instance != null) return _instance;

            RectTransform host = HudLayer.For(AppMode.Tour);
            if (host == null) return null;

            Image pill = UiKit.MakeRounded(host, "CoinBadge", new Color(0f, 0f, 0f, 0.6f), 9f);
            pill.raycastTarget = false;
            RectTransform rt = pill.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(UiKit.Css(96f), UiKit.Css(28f));
            // Under the hint line, which owns top 15 (the web stacks them the same way).
            // 52 px put the coins under the (now removed) hint line and left them floating in the
            // middle of the view. Tucked up to the top rail instead, level with the exit button
            // and the depth pill, where the eye already goes for status.
            rt.anchoredPosition = new Vector2(0f, -UiKit.Css(16f));

            Text label = UiKit.MakeText(rt, "Value", "0", UiKit.CssFont(14f), TextAnchor.MiddleCenter, Gold);
            label.fontStyle = FontStyle.Bold;
            UiKit.Stretch(label.rectTransform);

            var c = pill.gameObject.AddComponent<CoinCounter>();
            c._rt = rt;
            c._label = label;
            _instance = c;
            return c;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        public static void Show(int coins)
        {
            CoinCounter c = Ensure();
            if (c == null) return;
            c._rt.gameObject.SetActive(true);
            c._label.text = coins.ToString();
        }

        public static void Hide()
        {
            if (_instance != null && _instance._rt != null) _instance._rt.gameObject.SetActive(false);
        }

        /// <summary>The web's flyCoin: "+N" drifts up to the badge and fades.</summary>
        public static void Fly(int amount)
        {
            CoinCounter c = _instance;
            if (c == null || amount <= 0) return;
            c.StartCoroutine(c.FlyRoutine(amount));
        }

        private IEnumerator FlyRoutine(int amount)
        {
            RectTransform host = HudLayer.For(AppMode.Tour);
            if (host == null) yield break;

            Text t = UiKit.MakeText(host, "CoinFly", "+" + amount, UiKit.CssFont(16f),
                                    TextAnchor.MiddleCenter, Gold);
            t.fontStyle = FontStyle.Bold;
            t.raycastTarget = false;
            RectTransform rt = t.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(UiKit.Css(90f), UiKit.RowHeight(UiKit.CssFont(16f)));

            Vector2 from = new Vector2(0f, -UiKit.Css(30f));
            Vector2 to = _rt != null ? _rt.anchoredPosition : new Vector2(0f, UiKit.Css(200f));
            const float dur = 0.65f;
            for (float e = 0f; e < dur; e += Time.unscaledDeltaTime)
            {
                float k = e / dur;
                float ease = 1f - (1f - k) * (1f - k);
                rt.anchoredPosition = Vector2.Lerp(from, to, ease);
                Color c = t.color; c.a = 1f - k; t.color = c;
                rt.localScale = Vector3.one * (1f - 0.3f * k);
                yield return null;
            }
            Destroy(t.gameObject);
        }
    }
}
