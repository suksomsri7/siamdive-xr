using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using DiveMap.Core;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// 🕘 Version history — the web's <c>openRevModal()</c> / <c>restoreRev()</c>.
    ///
    /// Contract (siamdive-maps <c>[shortId]/revisions/route.ts</c>):
    /// <code>
    ///   GET  …/revisions?deviceId=          → { revisions: [ {id, rev, name, createdAt} ] }  (20, newest first)
    ///   POST …/revisions { deviceId, revisionId } → restores items/pins/env, bumps rev
    ///        403 unless you OWN the map — note this is stricter than PATCH, which also lets
    ///        editPolicy "all" through. A collaborator can edit but cannot roll the map back.
    /// </code>
    ///
    /// Snapshots are taken by the server itself on every content-changing PATCH
    /// (<c>captureRevision</c>), so this is the safety net under everything else in section I:
    /// the answer to "I deleted the wrong thing three edits ago and undo is gone".
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RevisionSheet : MonoBehaviour
    {
        private const float RowHeightCss = 56f;
        private const float RowGapCss = 6f;
        private const float PadCss = 16f;

        private static readonly Color CardBg = new Color(0.051f, 0.133f, 0.188f, 0.98f);
        private static readonly Color RowBg = new Color(1f, 1f, 1f, 0.06f);
        private static readonly Color TitleFg = new Color(0.498f, 0.753f, 1f, 1f);

        private static RevisionSheet _open;

        private RectTransform _rows;
        private Text _title, _status;
        private readonly List<GameObject> _rowViews = new List<GameObject>();
        private readonly List<(string Id, int Rev, string When)> _revisions =
            new List<(string, int, string)>();

        // ── QC surface ───────────────────────────────────────────────────────────
        public static bool IsOpen => _open != null;
        public static RevisionSheet Current => _open;
        public int RowCount => _rowViews.Count;
        public string LastError { get; private set; }

        public static void Open()
        {
            if (_open != null) return;
            RectTransform host = UiShell.Instance != null ? UiShell.Instance.OverlayRoot : null;
            if (host == null) return;

            var go = new GameObject("RevisionSheet");
            go.transform.SetParent(host, false);
            var rt = go.AddComponent<RectTransform>();
            UiKit.Stretch(rt);
            var sheet = go.AddComponent<RevisionSheet>();
            sheet.Build(rt);
            _open = sheet;
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
        }

        private void Build(RectTransform root)
        {
            Button scrim = UiKit.MakeButton(root, "Scrim", null, 0, new Color(0f, 0f, 0f, 0.55f),
                                            Color.white, Close);
            UiKit.Stretch(scrim.GetComponent<RectTransform>());

            float pad = UiKit.Css(PadCss);
            Image card = UiKit.MakeRounded(root, "Card", CardBg, 18f);
            RectTransform crt = card.rectTransform;
            crt.anchorMin = new Vector2(0.5f, 0.5f);
            crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = new Vector2(
                Mathf.Min(UiKit.Css(400f), Screen.width / UiKit.CanvasScale - UiKit.Css(40f)),
                Mathf.Min(UiKit.Css(460f), Screen.height / UiKit.CanvasScale - UiKit.Css(80f)));
            crt.anchoredPosition = Vector2.zero;

            float y = pad;
            int tSize = UiKit.CssFont(15f);
            _title = UiKit.MakeLine(card.transform, "Title", UiStrings.Tr("ประวัติเวอร์ชัน"),
                                    tSize, TextAnchor.UpperLeft, TitleFg);
            _title.fontStyle = FontStyle.Bold;
            Row(_title.rectTransform, pad, y, UiKit.RowHeight(tSize));
            y += UiKit.LineHeight(tSize) + UiKit.Css(4f);

            int hSize = UiKit.CssFont(12f);
            Text hint = UiKit.MakeText(card.transform, "Hint",
                                       UiStrings.Tr("กู้คืนแล้วของที่แก้หลังจากนั้นจะหายไป"),
                                       hSize, TextAnchor.UpperLeft, UiKit.TextDim);
            Row(hint.rectTransform, pad, y, UiKit.RowHeight(hSize, 2));
            y += UiKit.LineHeight(hSize) + UiKit.Css(10f);

            float listH = crt.sizeDelta.y - y - pad - UiKit.Css(46f) - UiKit.Css(8f);
            ScrollRect scroll = UiKit.MakeScroll(card.transform, "Rows", out _rows);
            RectTransform srt = scroll.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0f, 1f);
            srt.anchorMax = new Vector2(1f, 1f);
            srt.pivot = new Vector2(0.5f, 1f);
            srt.sizeDelta = new Vector2(-pad * 2f, listH);
            srt.anchoredPosition = new Vector2(0f, -y);

            _status = UiKit.MakeText(card.transform, "Status", UiStrings.Tr("กำลังโหลด…"),
                                     UiKit.CssFont(13f), TextAnchor.MiddleCenter, UiKit.TextDim);
            Row(_status.rectTransform, pad, y + listH * 0.4f, UiKit.RowHeight(UiKit.CssFont(13f)));
            y += listH + UiKit.Css(8f);

            Button close = UiKit.MakeButton(card.transform, "Close", UiStrings.Tr("ปิด"),
                                            UiKit.CssFont(14f), new Color(0.2f, 0.267f, 0.333f, 1f),
                                            UiKit.TextMain, Close);
            Image cbg = close.GetComponent<Image>();
            if (cbg != null) { cbg.sprite = UiKit.RoundedSprite(10f); cbg.type = Image.Type.Sliced; }
            RectTransform clrt = close.GetComponent<RectTransform>();
            clrt.anchorMin = new Vector2(0f, 1f);
            clrt.anchorMax = new Vector2(1f, 1f);
            clrt.pivot = new Vector2(0.5f, 1f);
            clrt.sizeDelta = new Vector2(-pad * 2f, UiKit.Css(46f));
            clrt.anchoredPosition = new Vector2(0f, -y);

            StartCoroutine(Load());
        }

        private static void Row(RectTransform rt, float pad, float y, float h)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(-pad * 2f, h);
            rt.anchoredPosition = new Vector2(0f, -y);
        }

        // ── data ─────────────────────────────────────────────────────────────────

        private IEnumerator Load()
        {
            var boot = FindFirstObjectByType<AppBoot>();
            string mapId = boot != null ? boot.CurrentMapId : null;
            if (string.IsNullOrEmpty(mapId)) { Fail("no_map"); yield break; }

            string url = MapApiClient.DefaultBaseUrl + "/api/dive-sites/"
                       + UnityWebRequest.EscapeURL(mapId) + "/revisions?deviceId="
                       + UnityWebRequest.EscapeURL(WalletClient.DeviceId);

            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                req.timeout = 20;
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    // 403 here means "not the owner" — a collaborator may edit but not roll back.
                    Fail(req.responseCode == 403 ? "forbidden" : $"http_{req.responseCode}");
                    yield break;
                }

                try
                {
                    JObject o = JObject.Parse(req.downloadHandler.text);
                    _revisions.Clear();
                    if (o["revisions"] is JArray arr)
                        foreach (JToken t in arr)
                            _revisions.Add(((string)t["id"],
                                            t["rev"] != null ? (int)t["rev"] : -1,
                                            Ago((string)t["createdAt"])));
                }
                catch (Exception e) { Fail("parse: " + e.Message); yield break; }
            }

            Debug.Log($"[Edit] revisions {mapId} → {_revisions.Count}");
            Render();
        }

        private void Fail(string why)
        {
            LastError = why;
            Debug.LogWarning("[Edit] revisions failed: " + why);
            if (_status != null)
            {
                _status.gameObject.SetActive(true);
                _status.text = UiStrings.Tr(why == "forbidden"
                    ? "เฉพาะเจ้าของแมพเท่านั้นที่กู้คืนได้"
                    : "โหลดประวัติไม่สำเร็จ");
            }
        }

        /// <summary>
        /// "3 ชม. ที่แล้ว" from an ISO timestamp. A raw UTC string is unreadable, and the
        /// question a player is asking is "how long ago", not "at what o'clock".
        /// </summary>
        public static string Ago(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return "";
            if (!DateTime.TryParse(iso, System.Globalization.CultureInfo.InvariantCulture,
                                   System.Globalization.DateTimeStyles.AdjustToUniversal |
                                   System.Globalization.DateTimeStyles.AssumeUniversal,
                                   out DateTime when))
                return iso;

            TimeSpan d = DateTime.UtcNow - when;
            if (d.TotalMinutes < 1) return UiStrings.Tr("เมื่อครู่");
            if (d.TotalHours < 1) return Math.Floor(d.TotalMinutes) + " " + UiStrings.Tr("นาทีที่แล้ว");
            if (d.TotalDays < 1) return Math.Floor(d.TotalHours) + " " + UiStrings.Tr("ชม. ที่แล้ว");
            return Math.Floor(d.TotalDays) + " " + UiStrings.Tr("วันที่แล้ว");
        }

        private void Render()
        {
            for (int i = 0; i < _rowViews.Count; i++) if (_rowViews[i] != null) Destroy(_rowViews[i]);
            _rowViews.Clear();

            if (_status != null)
            {
                _status.gameObject.SetActive(_revisions.Count == 0);
                if (_revisions.Count == 0) _status.text = UiStrings.Tr("ยังไม่มีประวัติ");
            }

            float rowH = UiKit.Css(RowHeightCss), gap = UiKit.Css(RowGapCss);
            for (int i = 0; i < _revisions.Count; i++) AddRow(_revisions[i], i * (rowH + gap), rowH);

            if (_rows != null)
                _rows.sizeDelta = new Vector2(0f, _revisions.Count > 0
                    ? _revisions.Count * (rowH + gap) - gap : 0f);
        }

        private void AddRow((string Id, int Rev, string When) rev, float y, float height)
        {
            Button row = UiKit.MakeButton(_rows, "Rev_" + rev.Id, null, 0, RowBg, UiKit.TextMain,
                                          () => Confirm(rev));
            Image bg = row.GetComponent<Image>();
            if (bg != null) { bg.sprite = UiKit.RoundedSprite(10f); bg.type = Image.Type.Sliced; }

            RectTransform rt = row.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, height);
            rt.anchoredPosition = new Vector2(0f, -y);

            float pad = UiKit.Css(12f);
            int nSize = UiKit.CssFont(13f);
            Text label = UiKit.MakeLine(row.transform, "Label",
                                        UiStrings.Tr("เวอร์ชัน") + " " + rev.Rev,
                                        nSize, TextAnchor.MiddleLeft, UiKit.TextMain);
            RectTransform lrt = label.rectTransform;
            lrt.anchorMin = new Vector2(0f, 0.5f);
            lrt.anchorMax = new Vector2(0f, 0.5f);
            lrt.pivot = new Vector2(0f, 0.5f);
            lrt.sizeDelta = new Vector2(UiKit.Css(140f), UiKit.RowHeight(nSize));
            lrt.anchoredPosition = new Vector2(pad, 0f);

            Text when = UiKit.MakeLine(row.transform, "When", rev.When, UiKit.CssFont(12f),
                                       TextAnchor.MiddleRight, UiKit.TextDim);
            RectTransform wrt = when.rectTransform;
            wrt.anchorMin = new Vector2(1f, 0.5f);
            wrt.anchorMax = new Vector2(1f, 0.5f);
            wrt.pivot = new Vector2(1f, 0.5f);
            wrt.sizeDelta = new Vector2(UiKit.Css(150f), UiKit.RowHeight(UiKit.CssFont(12f)));
            wrt.anchoredPosition = new Vector2(-pad, 0f);

            _rowViews.Add(row.gameObject);
        }

        /// <summary>Restoring throws away newer work, so it asks first.</summary>
        private void Confirm((string Id, int Rev, string When) rev)
        {
            ActionSheet sheet = ActionSheet.Show(
                UiStrings.Tr("กู้คืนเวอร์ชัน") + " " + rev.Rev + " · " + rev.When);
            if (sheet == null) return;
            sheet.AddItem(UiStrings.Tr("กู้คืน"), () => StartCoroutine(Restore(rev.Id)), true);
            sheet.AddCancel(UiStrings.Tr("ยกเลิก"));
        }

        private IEnumerator Restore(string revisionId)
        {
            var boot = FindFirstObjectByType<AppBoot>();
            if (boot == null) yield break;

            var body = new JObject
            {
                ["deviceId"] = WalletClient.DeviceId,
                ["revisionId"] = revisionId,
            };
            byte[] payload = System.Text.Encoding.UTF8.GetBytes(body.ToString(Newtonsoft.Json.Formatting.None));
            string url = MapApiClient.DefaultBaseUrl + "/api/dive-sites/"
                       + UnityWebRequest.EscapeURL(boot.CurrentMapId) + "/revisions";

            bool ok = false;
            long code = 0;
            using (var req = new UnityWebRequest(url, "POST")
            {
                uploadHandler = new UploadHandlerRaw(payload),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 25,
            })
            {
                req.SetRequestHeader("Content-Type", "application/json");
                yield return req.SendWebRequest();
                ok = req.result == UnityWebRequest.Result.Success;
                code = req.responseCode;
            }

            Debug.Log($"[Edit] restore {revisionId} ok={ok} code={code}");
            Close();

            if (!ok)
            {
                Toast.ShowTr(code == 403 ? "เฉพาะเจ้าของแมพเท่านั้นที่กู้คืนได้" : "กู้คืนไม่สำเร็จ");
                yield break;
            }

            // The server rewrote the map; the copy in memory is now the stale one. This is the
            // one editing path that genuinely must re-fetch rather than rebuild from memory.
            Toast.ShowTr("กู้คืนแล้ว");
            boot.ReloadCurrentMap();
        }
    }
}
