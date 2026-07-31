using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;
using DiveMap.Core;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// 📋 "โมเดลบนแมพ" — the web's <c>_objList()</c> (builder.html:4447): every object on the map,
    /// searchable, filterable by kind, with select / rename / delete per row.
    ///
    /// This is not a convenience panel. It is the ONLY way to reach an object that is buried
    /// inside a wreck, hidden behind a rock, or scaled down to a speck — all of which a player
    /// can produce with the gizmo in about ten seconds. Without it, "I can't select my thing
    /// any more" has no answer but reloading the map.
    ///
    /// Ported behaviour:
    ///  • title carries the count, and the count follows the filter (<c>_olRender</c> :4446)
    ///  • picking a row closes the sheet and selects the object (:4459)
    ///  • rename is inline, trimmed, capped (:4461 — the web caps at 40; SceneEdit caps at 60)
    ///  • delete drops the selection first, then removes and records (:4462)
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ObjectListSheet : MonoBehaviour
    {
        private const float RowHeightCss = 52f;
        private const float RowGapCss = 6f;
        private const float PadCss = 14f;

        private static readonly Color CardBg = new Color(0.051f, 0.133f, 0.188f, 0.98f);
        private static readonly Color RowBg = new Color(1f, 1f, 1f, 0.06f);
        private static readonly Color TitleFg = new Color(0.498f, 0.753f, 1f, 1f);   // #7fc0ff
        private static readonly Color DelFg = new Color(1f, 0.541f, 0.612f, 1f);

        private static ObjectListSheet _open;

        private RectTransform _rows;
        private InputField _search;
        private Text _title;
        private string _kindFilter = "";
        private readonly List<GameObject> _rowViews = new List<GameObject>();
        private readonly List<string> _kinds = new List<string>();
        private int _kindIndex;

        // ── QC surface ───────────────────────────────────────────────────────────
        public static bool IsOpen => _open != null;
        public static ObjectListSheet Current => _open;
        public int RowCount => _rowViews.Count;
        public string KindFilter => _kindFilter;

        public static void Open()
        {
            if (_open != null) { _open.Render(); return; }
            RectTransform host = UiShell.Instance != null ? UiShell.Instance.OverlayRoot : null;
            if (host == null) return;

            var go = new GameObject("ObjectListSheet");
            go.transform.SetParent(host, false);
            var rt = go.AddComponent<RectTransform>();
            UiKit.Stretch(rt);
            var sheet = go.AddComponent<ObjectListSheet>();
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

        // ── build ────────────────────────────────────────────────────────────────

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
                Mathf.Min(UiKit.Css(420f), Screen.width / UiKit.CanvasScale - UiKit.Css(40f)),
                Mathf.Min(UiKit.Css(520f), Screen.height / UiKit.CanvasScale - UiKit.Css(80f)));
            crt.anchoredPosition = Vector2.zero;

            float y = pad;
            int tSize = UiKit.CssFont(15f);
            _title = UiKit.MakeLine(card.transform, "Title", "", tSize, TextAnchor.UpperLeft, TitleFg);
            _title.fontStyle = FontStyle.Bold;
            Row(_title.rectTransform, pad, y, UiKit.RowHeight(tSize));
            y += UiKit.LineHeight(tSize) + UiKit.Css(8f);

            // search + kind filter, side by side like the web
            float fieldH = UiKit.Css(40f);
            float half = (crt.sizeDelta.x - pad * 2f - UiKit.Css(6f)) * 0.58f;

            Image box = UiKit.MakeRounded(card.transform, "Search", new Color(0.024f, 0.078f, 0.110f, 1f), 8f);
            RectTransform brt = box.rectTransform;
            brt.anchorMin = new Vector2(0f, 1f);
            brt.anchorMax = new Vector2(0f, 1f);
            brt.pivot = new Vector2(0f, 1f);
            brt.sizeDelta = new Vector2(half, fieldH);
            brt.anchoredPosition = new Vector2(pad, -y);

            _search = UiKit.MakeInput(box.transform, "Field", UiStrings.Tr("ค้นหา"), UiKit.CssFont(13f));
            Image sbg = _search.GetComponent<Image>();
            if (sbg != null) sbg.color = new Color(0f, 0f, 0f, 0f);
            UiKit.Stretch(_search.GetComponent<RectTransform>());
            foreach (Graphic g in new Graphic[] { _search.textComponent, _search.placeholder })
            {
                if (g == null) continue;
                UiKit.Stretch(g.rectTransform);
                g.rectTransform.offsetMin = new Vector2(UiKit.Css(10f), 0f);
                g.rectTransform.offsetMax = new Vector2(-UiKit.Css(10f), 0f);
            }
            _search.onValueChanged.AddListener(_ => Render());

            // uGUI has no <select>; a button that cycles the kinds is the same information in
            // one tap, and there are only ever a handful of kinds on one map.
            Button kind = UiKit.MakeButton(card.transform, "Kind", "", UiKit.CssFont(13f),
                                           RowBg, UiKit.TextMain, CycleKind);
            Image kbg = kind.GetComponent<Image>();
            if (kbg != null) { kbg.sprite = UiKit.RoundedSprite(8f); kbg.type = Image.Type.Sliced; }
            RectTransform krt = kind.GetComponent<RectTransform>();
            krt.anchorMin = new Vector2(1f, 1f);
            krt.anchorMax = new Vector2(1f, 1f);
            krt.pivot = new Vector2(1f, 1f);
            krt.sizeDelta = new Vector2(crt.sizeDelta.x - pad * 2f - half - UiKit.Css(6f), fieldH);
            krt.anchoredPosition = new Vector2(-pad, -y);
            _kindLabel = kind.GetComponentInChildren<Text>();
            y += fieldH + UiKit.Css(8f);

            float listH = crt.sizeDelta.y - y - pad - UiKit.Css(46f) - UiKit.Css(8f);
            ScrollRect scroll = UiKit.MakeScroll(card.transform, "Rows", out _rows);
            RectTransform srt = scroll.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0f, 1f);
            srt.anchorMax = new Vector2(1f, 1f);
            srt.pivot = new Vector2(0.5f, 1f);
            srt.sizeDelta = new Vector2(-pad * 2f, listH);
            srt.anchoredPosition = new Vector2(0f, -y);
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

            CollectKinds();
            Render();
        }
        private Text _kindLabel;

        private static void Row(RectTransform rt, float pad, float y, float h)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(-pad * 2f, h);
            rt.anchoredPosition = new Vector2(0f, -y);
        }

        // ── data ─────────────────────────────────────────────────────────────────

        private static JArray Items()
        {
            var boot = FindFirstObjectByType<AppBoot>();
            SceneData scene = boot != null ? boot.CurrentScene : null;
            return scene != null ? SceneEdit.Items(scene) : null;
        }

        /// <summary>Display name for a row: the object's own name, else the module's, else the id.</summary>
        private static string NameOf(JObject item)
        {
            string given = item != null ? (string)item["n"] : null;
            if (!string.IsNullOrWhiteSpace(given)) return given;

            string assetId = item != null ? (string)item["assetId"] : null;
            AssetManifest.Module m = assetId != null && AppBoot.Manifest != null
                ? AppBoot.Manifest.Get(assetId) : null;
            if (m != null && !string.IsNullOrWhiteSpace(m.Name)) return m.Name;

            // Procedural ids (warp:0, rock:2 …) have no manifest row, and showing the raw id
            // in a list a player reads is the same as showing nothing.
            if (!string.IsNullOrEmpty(assetId))
            {
                if (assetId.StartsWith("warp:", StringComparison.Ordinal)) return UiStrings.Tr("ประตูวาป");
                int c = assetId.IndexOf(':');
                if (c > 0) return UiStrings.Tr(Palette.LabelOf(Palette.FoldKind(assetId.Substring(0, c).ToUpperInvariant(), assetId)));
            }
            return assetId ?? "?";
        }

        private static string KindOf(JObject item)
        {
            string assetId = item != null ? (string)item["assetId"] : null;
            AssetManifest.Module m = assetId != null && AppBoot.Manifest != null
                ? AppBoot.Manifest.Get(assetId) : null;
            return m != null ? Palette.FoldKind(m.Kind, assetId) : "";
        }

        private void CollectKinds()
        {
            _kinds.Clear();
            _kinds.Add("");   // "all"
            JArray items = Items();
            if (items == null) return;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JToken t in items)
            {
                string k = KindOf(t as JObject);
                if (!string.IsNullOrEmpty(k) && seen.Add(k)) _kinds.Add(k);
            }
        }

        private void CycleKind()
        {
            if (_kinds.Count == 0) return;
            _kindIndex = (_kindIndex + 1) % _kinds.Count;
            _kindFilter = _kinds[_kindIndex];
            Render();
        }

        // ── render ───────────────────────────────────────────────────────────────

        private void Render()
        {
            for (int i = 0; i < _rowViews.Count; i++) if (_rowViews[i] != null) Destroy(_rowViews[i]);
            _rowViews.Clear();

            JArray items = Items();
            string q = (_search != null ? _search.text : "").Trim();

            float rowH = UiKit.Css(RowHeightCss), gap = UiKit.Css(RowGapCss);
            int n = 0;

            if (items != null)
                foreach (JToken t in items)
                {
                    if (!(t is JObject o)) continue;
                    string name = NameOf(o);
                    if (_kindFilter.Length > 0 && KindOf(o) != _kindFilter) continue;
                    if (q.Length > 0 && name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    AddRow(o, name, n * (rowH + gap), rowH);
                    n++;
                }

            if (_rows != null) _rows.sizeDelta = new Vector2(0f, n > 0 ? n * (rowH + gap) - gap : 0f);
            if (_title != null)
                _title.text = UiStrings.Tr("โมเดลบนแมพ") + " (" + n + ")" +
                              (_picked.Count >= 2 ? "  ·  " + UiStrings.Tr("เลือกแล้ว") + " " + _picked.Count : "");
            if (_kindLabel != null)
                _kindLabel.text = _kindFilter.Length == 0
                    ? UiStrings.Tr("ทุกชนิด")
                    : UiStrings.Tr(Palette.LabelOf(_kindFilter));

            Debug.Log($"[Edit] object list rows={n} filter='{_kindFilter}' q='{q}'");
        }

        private void AddRow(JObject item, string name, float y, float height)
        {
            string id = (string)item["id"];

            Button row = UiKit.MakeButton(_rows, "Row_" + id, null, 0, RowBg, UiKit.TextMain,
                                          () => { Close(); GizmoController.Select(id); });
            Image bg = row.GetComponent<Image>();
            if (bg != null) { bg.sprite = UiKit.RoundedSprite(10f); bg.type = Image.Type.Sliced; }

            RectTransform rt = row.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, height);
            rt.anchoredPosition = new Vector2(0f, -y);

            float pad = UiKit.Css(10f);
            float act = UiKit.Css(34f);

            int nSize = UiKit.CssFont(13f);
            Text label = UiKit.MakeLine(row.transform, "Name", name, nSize, TextAnchor.MiddleLeft,
                                        UiKit.TextMain);
            RectTransform lrt = label.rectTransform;
            lrt.anchorMin = new Vector2(0f, 0.5f);
            lrt.anchorMax = new Vector2(1f, 0.5f);
            lrt.pivot = new Vector2(0.5f, 0.5f);
            lrt.sizeDelta = new Vector2(-(pad + UiKit.Css(34f) + act * 2f + UiKit.Css(18f)), UiKit.RowHeight(nSize));
            lrt.anchoredPosition = new Vector2((pad + UiKit.Css(34f) - act * 2f - UiKit.Css(14f)) * 0.5f, 0f);

            // ☑ group tick — the left edge, so it never competes with the row's own tap target
            bool picked = _picked.Contains(id);
            Button tick = UiKit.MakeButton(row.transform, "Tick", null, 0,
                                           picked ? UiKit.Accent : new Color(1f, 1f, 1f, 0.10f),
                                           UiKit.TextMain, () => TogglePick(id));
            Image tbg = tick.GetComponent<Image>();
            if (tbg != null) { tbg.sprite = UiKit.RoundedSprite(6f); tbg.type = Image.Type.Sliced; }
            RectTransform tkrt = tick.GetComponent<RectTransform>();
            tkrt.anchorMin = new Vector2(0f, 0.5f);
            tkrt.anchorMax = new Vector2(0f, 0.5f);
            tkrt.pivot = new Vector2(0f, 0.5f);
            tkrt.sizeDelta = new Vector2(UiKit.Css(22f), UiKit.Css(22f));
            tkrt.anchoredPosition = new Vector2(UiKit.Css(8f), 0f);
            if (picked) Glyph(tick.transform, "check", UiKit.OnAccent, UiKit.Css(14f));

            // ✎ rename
            Button ren = UiKit.MakeButton(row.transform, "Rename", null, 0, new Color(0f, 0f, 0f, 0f),
                                          UiKit.TextMain, () => Rename(id, name));
            Glyph(ren.transform, "pencil", UiKit.TextDim, UiKit.Css(16f));
            Place(ren.GetComponent<RectTransform>(), -(pad + act), act);

            // 🗑 delete
            Button del = UiKit.MakeButton(row.transform, "Delete", null, 0, new Color(0f, 0f, 0f, 0f),
                                          DelFg, () => Delete(id));
            Glyph(del.transform, "trash", DelFg, UiKit.Css(16f));
            Place(del.GetComponent<RectTransform>(), -pad, act);

            _rowViews.Add(row.gameObject);
        }

        private static void Place(RectTransform rt, float x, float size)
        {
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = new Vector2(x, 0f);
        }

        private static void Glyph(Transform parent, string icon, Color tint, float size)
        {
            Image img = UiKit.MakePanel(parent, "Icon", tint);
            img.sprite = IconPainter.Get(icon);
            img.raycastTarget = false;
            RectTransform rt = img.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = Vector2.zero;
        }

        // ── actions ──────────────────────────────────────────────────────────────

        private void Rename(string id, string current)
        {
            TextPrompt.Show(UiStrings.Tr("ตั้งชื่อวัตถุ"), current, value =>
            {
                JArray items = Items();
                if (items == null || !SceneEdit.Rename(items, id, value)) return;
                MapEditor.RecordAndApply(items);
                Debug.Log($"[Edit] renamed {id} → '{value}'");
                Render();
            });
        }

        /// <summary>
        /// Group selection. The web puts a checkbox on each row and a "ย้าย/ย่อขยายที่เลือก"
        /// button that appears at 2+; this keeps the same rule — a group of one is just a
        /// selection, so the bar stays hidden until there are two.
        /// </summary>
        private readonly HashSet<string> _picked = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>QC: how many rows are ticked.</summary>
        public int PickedCount => _picked.Count;

        private void TogglePick(string id)
        {
            if (!_picked.Remove(id)) _picked.Add(id);
            Render();
        }

        /// <summary>QC only — tick rows without a finger.</summary>
        public void QcPick(params string[] ids)
        {
            foreach (string id in ids) _picked.Add(id);
            Render();
        }

        /// <summary>Apply one operation to every ticked row.</summary>
        public void GroupAction(string what)
        {
            JArray items = Items();
            if (items == null || _picked.Count < 2) return;

            int n;
            switch (what)
            {
                case "scale": n = MultiSelect.ScaleBy(items, _picked, 1.25); break;
                case "snap":  n = MultiSelect.Snap(items, _picked); break;
                case "dup":   n = MultiSelect.DuplicateAll(items, _picked, DateTime.UtcNow.Ticks).Count; break;
                default:
                    foreach (string id in _picked) RopeSystem.DetachFrom(id);
                    n = MultiSelect.DeleteAll(items, _picked);
                    _picked.Clear();
                    break;
            }
            if (n == 0) return;

            MapEditor.RecordAndApply(items);
            Debug.Log($"[Edit] group {what} on {n} item(s)");
            CollectKinds();
            Render();
        }

        private void Delete(string id)
        {
            JArray items = Items();
            if (items == null) return;

            // Drop the selection first — the toolbar would otherwise be pointing at an object
            // that no longer exists, and its next action would silently do nothing.
            if (GizmoController.Selected == id) { SelectionToolbar.Hide(); GizmoController.Deselect(); }

            if (!SceneEdit.Delete(items, id)) return;
            RopeSystem.DetachFrom(id);
            MapEditor.RecordAndApply(items);
            Toast.ShowTr("ลบแล้ว");
            CollectKinds();
            Render();
        }
    }
}
