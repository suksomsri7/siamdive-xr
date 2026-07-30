using System.Collections;
using System.Collections.Generic;
using DiveMap.Core;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// G2/G3 — what is behind a pin: the photos somebody left at that spot (the web's
    /// <c>openPin</c>/<c>renderPin</c>, builder.html:2883-2900). A bottom sheet with the picture,
    /// a "3/7" counter and previous/next, matching the web's #pinModal.
    ///
    /// Two rules carried across deliberately:
    ///   • the url gate (<see cref="PinMedia.IsFetchable"/>) is applied at READ time and again
    ///     here before the request goes out — a pin's url is data from someone else's map
    ///   • <b>video is not played</b>. The web has a &lt;video&gt; element for free; a Unity player
    ///     needs VideoPlayer plus a render texture, and shipping a half-working player is worse
    ///     than saying plainly that this one is a clip. It is labelled and offered as a link.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PinSheet : MonoBehaviour
    {
        private static readonly Color SheetBg = new Color(0.027f, 0.102f, 0.169f, 0.96f);
        private static readonly Color TxtCol  = new Color(0.918f, 0.957f, 0.984f, 1f);   // --txt
        private static readonly Color MutCol  = new Color(0.624f, 0.714f, 0.788f, 1f);   // --mut
        private static readonly Color BtnBg   = new Color(1f, 1f, 1f, 0.14f);
        private static readonly Color Frame   = new Color(0f, 0f, 0f, 0.35f);

        private static PinSheet _open;

        private PinMarker _pin;
        private List<PinMedia.Item> _media = new List<PinMedia.Item>();
        private int _index;

        private RawImage _view;
        private Text _count;
        private Text _note;
        private Coroutine _loading;
        private readonly Dictionary<string, Texture2D> _cache = new Dictionary<string, Texture2D>();

        public static bool IsOpen => _open != null;

        public static void Open(PinMarker pin)
        {
            Close();
            if (pin == null) return;

            RectTransform layer = HudLayer.For(ModeManager.Current);
            if (layer == null) return;

            RectTransform root = UiKit.MakeNode(layer, "PinSheet");
            UiKit.Stretch(root);
            var s = root.gameObject.AddComponent<PinSheet>();
            s._pin = pin;
            s._media = pin.Media ?? new List<PinMedia.Item>();
            s._index = 0;
            s.Build(root);
            _open = s;

            Debug.Log($"[Pins] open pin={pin.PinId} media={s._media.Count}");
        }

        public static void Close()
        {
            if (_open == null) return;
            Destroy(_open.gameObject);
            _open = null;
        }

        private void OnDestroy()
        {
            if (_open == this) _open = null;
            foreach (KeyValuePair<string, Texture2D> kv in _cache)
                if (kv.Value != null) Destroy(kv.Value);
            _cache.Clear();
        }

        private void Build(RectTransform root)
        {
            Button scrim = UiKit.MakeButton(root, "PinScrim", null, 0,
                                            new Color(0f, 0f, 0f, 0.42f), Color.white, Close);
            UiKit.Stretch(scrim.GetComponent<RectTransform>());

            Image panel = UiKit.MakeRounded(root, "PinPanel", SheetBg, 18f);
            RectTransform prt = panel.rectTransform;
            prt.anchorMin = new Vector2(0f, 0f);
            prt.anchorMax = new Vector2(1f, 0f);
            prt.pivot = new Vector2(0.5f, 0f);
            float h = Screen.height / UiKit.CanvasScale * 0.62f;
            prt.sizeDelta = new Vector2(0f, h);
            prt.anchoredPosition = Vector2.zero;

            float pad = UiKit.Css(14f);
            int font = UiKit.CssFont(13f);
            float rowH = UiKit.RowHeight(font);
            float btnH = rowH + UiKit.Css(8f);

            // Close, top-right.
            Button close = UiKit.MakeButton(prt, "PinClose", UiStrings.Tr("ปิด"), font,
                                            BtnBg, TxtCol, Close);
            RectTransform crt = close.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(1f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(1f, 1f);
            crt.sizeDelta = new Vector2(UiKit.Css(74f), btnH);
            crt.anchoredPosition = new Vector2(-pad, -UiKit.Css(12f));

            _count = UiKit.MakeLine(prt, "PinCount", "0/0", font, TextAnchor.MiddleLeft, MutCol);
            RectTransform cnt = _count.rectTransform;
            cnt.anchorMin = new Vector2(0f, 1f);
            cnt.anchorMax = new Vector2(0f, 1f);
            cnt.pivot = new Vector2(0f, 1f);
            cnt.sizeDelta = new Vector2(UiKit.Css(120f), btnH);
            cnt.anchoredPosition = new Vector2(pad, -UiKit.Css(12f));

            // The picture, framed.
            Image frame = UiKit.MakeRounded(prt, "PinFrame", Frame, 10f);
            RectTransform frt = frame.rectTransform;
            frt.anchorMin = new Vector2(0f, 0f);
            frt.anchorMax = new Vector2(1f, 1f);
            frt.offsetMin = new Vector2(pad, pad + btnH + UiKit.Css(10f));
            frt.offsetMax = new Vector2(-pad, -(UiKit.Css(12f) + btnH + UiKit.Css(10f)));

            var viewGo = new GameObject("PinView");
            viewGo.transform.SetParent(frt, false);
            _view = viewGo.AddComponent<RawImage>();
            _view.raycastTarget = false;
            UiKit.Stretch(_view.rectTransform);
            _view.color = new Color(1f, 1f, 1f, 0f);   // nothing loaded yet

            _note = UiKit.MakeText(frt, "PinNote", "", font, TextAnchor.MiddleCenter, MutCol);
            UiKit.Stretch(_note.rectTransform);

            // Previous / next along the bottom.
            Button prev = UiKit.MakeButton(prt, "PinPrev", "‹", UiKit.CssFont(18f), BtnBg, TxtCol,
                                           () => Step(-1));
            RectTransform lrt = prev.GetComponent<RectTransform>();
            lrt.anchorMin = new Vector2(0f, 0f);
            lrt.anchorMax = new Vector2(0f, 0f);
            lrt.pivot = new Vector2(0f, 0f);
            lrt.sizeDelta = new Vector2(UiKit.Css(88f), btnH);
            lrt.anchoredPosition = new Vector2(pad, pad);

            Button next = UiKit.MakeButton(prt, "PinNext", "›", UiKit.CssFont(18f), BtnBg, TxtCol,
                                           () => Step(1));
            RectTransform nrt = next.GetComponent<RectTransform>();
            nrt.anchorMin = new Vector2(1f, 0f);
            nrt.anchorMax = new Vector2(1f, 0f);
            nrt.pivot = new Vector2(1f, 0f);
            nrt.sizeDelta = new Vector2(UiKit.Css(88f), btnH);
            nrt.anchoredPosition = new Vector2(-pad, pad);

            Render();
        }

        private void Step(int delta)
        {
            if (_media.Count == 0) return;
            _index = PinMedia.Wrap(_index + delta, _media.Count);
            Render();
        }

        private void Render()
        {
            _count.text = PinMedia.Counter(_index, _media.Count);

            if (_media.Count == 0)
            {
                _view.color = new Color(1f, 1f, 1f, 0f);
                _note.text = UiStrings.Tr("ยังไม่มีรูป/วิดีโอ");
                return;
            }

            PinMedia.Item it = _media[PinMedia.Wrap(_index, _media.Count)];
            if (it.IsVideo)
            {
                _view.color = new Color(1f, 1f, 1f, 0f);
                _note.text = UiStrings.Tr("คลิปวิดีโอ — เปิดดูได้ในเว็บ");
                return;
            }

            if (_cache.TryGetValue(it.Url, out Texture2D cached) && cached != null)
            {
                Apply(cached);
                return;
            }

            _view.color = new Color(1f, 1f, 1f, 0f);
            _note.text = UiStrings.Tr("กำลังโหลด…");
            if (_loading != null) StopCoroutine(_loading);
            _loading = StartCoroutine(Load(it.Url));
        }

        private IEnumerator Load(string url)
        {
            // Checked again here, not only when the list was read: this is the line the request
            // actually goes out on, and it is the one that has to hold.
            if (!PinMedia.IsFetchable(url))
            {
                _note.text = UiStrings.Tr("ไม่สามารถแสดงไฟล์นี้");
                yield break;
            }

            using (UnityWebRequest req = UnityWebRequestTexture.GetTexture(url))
            {
                req.timeout = 20;
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[Pins] media failed ({req.error}) {url}");
                    _note.text = UiStrings.Tr("โหลดรูปไม่สำเร็จ");
                    yield break;
                }

                var tex = DownloadHandlerTexture.GetContent(req);
                if (tex == null)
                {
                    _note.text = UiStrings.Tr("ไม่สามารถแสดงไฟล์นี้");
                    yield break;
                }

                _cache[url] = tex;
                Debug.Log($"[Pins] media loaded {tex.width}x{tex.height}");
                Apply(tex);
            }
        }

        /// <summary>Show a texture letterboxed inside the frame — never stretched.</summary>
        private void Apply(Texture2D tex)
        {
            _note.text = "";
            _view.texture = tex;
            _view.color = Color.white;

            RectTransform frame = _view.rectTransform.parent as RectTransform;
            if (frame == null || tex.height == 0) return;

            float fw = frame.rect.width, fh = frame.rect.height;
            if (fw <= 1f || fh <= 1f) return;

            float ar = tex.width / (float)tex.height;
            float w = fw, hgt = fw / ar;
            if (hgt > fh) { hgt = fh; w = fh * ar; }

            RectTransform vr = _view.rectTransform;
            vr.anchorMin = new Vector2(0.5f, 0.5f);
            vr.anchorMax = new Vector2(0.5f, 0.5f);
            vr.pivot = new Vector2(0.5f, 0.5f);
            vr.sizeDelta = new Vector2(w, hgt);
            vr.anchoredPosition = Vector2.zero;
        }
    }
}
