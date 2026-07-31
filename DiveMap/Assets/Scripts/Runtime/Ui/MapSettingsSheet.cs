using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;
using DiveMap.Core;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// ⚙️ Map settings — the web's name modal (<c>openNameModal</c>), permission dialog
    /// (<c>openPermission</c> :3399), water/area panel (<c>showSettings</c> :2740) and
    /// "clear everything" (<c>histClearScene</c>), in one sheet.
    ///
    /// One thing this screen says that the web's does not. The route maps "public" onto
    /// <c>editPolicy: "all"</c>, and that policy is checked on PATCH — so public does not mean
    /// "people can look at it", it means **anyone who opens it can change it**. The web's toggle
    /// is labelled สาธารณะ/ส่วนตัว and leaves that to be discovered; here it is written under the
    /// switch, because the cost of misreading it is someone else editing your dive site.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MapSettingsSheet : MonoBehaviour
    {
        private static MapSettingsSheet _open;

        private static readonly Color CardBg = new Color(0.051f, 0.133f, 0.188f, 0.98f);
        private static readonly Color RowBg = new Color(1f, 1f, 1f, 0.06f);
        private static readonly Color TitleFg = new Color(0.498f, 0.753f, 1f, 1f);
        private static readonly Color DangerFg = new Color(0.898f, 0.282f, 0.302f, 1f);

        private Text _nameLabel, _publicLabel, _searchLabel, _waterLabel, _areaLabel, _clearLabel;
        private bool _isPublic, _searchable = true;
        private bool _clearArmed;
        private RectTransform _content;

        public static bool IsOpen => _open != null;
        public static MapSettingsSheet Current => _open;
        public bool PublicToggle => _isPublic;

        public static void Open()
        {
            if (_open != null) return;
            RectTransform host = UiShell.Instance != null ? UiShell.Instance.OverlayRoot : null;
            if (host == null) return;

            var go = new GameObject("MapSettingsSheet");
            go.transform.SetParent(host, false);
            var rt = go.AddComponent<RectTransform>();
            UiKit.Stretch(rt);
            var sheet = go.AddComponent<MapSettingsSheet>();
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
            var boot = FindFirstObjectByType<AppBoot>();
            SceneData scene = boot != null ? boot.CurrentScene : null;
            _isPublic = scene != null && scene.Root["isPublic"] != null && (bool)scene.Root["isPublic"];
            _searchable = scene == null || scene.Root["searchable"] == null || (bool)scene.Root["searchable"];

            Button scrim = UiKit.MakeButton(root, "Scrim", null, 0, new Color(0f, 0f, 0f, 0.55f),
                                            Color.white, Close);
            UiKit.Stretch(scrim.GetComponent<RectTransform>());

            float pad = UiKit.Css(16f);
            Image card = UiKit.MakeRounded(root, "Card", CardBg, 18f);
            RectTransform crt = card.rectTransform;
            crt.anchorMin = new Vector2(0.5f, 0.5f);
            crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = new Vector2(
                Mathf.Min(UiKit.Css(400f), Screen.width / UiKit.CanvasScale - UiKit.Css(40f)),
                Mathf.Min(UiKit.Css(560f), Screen.height / UiKit.CanvasScale - UiKit.Css(60f)));
            crt.anchoredPosition = Vector2.zero;

            int tSize = UiKit.CssFont(15f);
            Text title = UiKit.MakeLine(card.transform, "Title", UiStrings.Tr("ตั้งค่าแมพ"), tSize,
                                        TextAnchor.UpperLeft, TitleFg);
            title.fontStyle = FontStyle.Bold;
            Row(title.rectTransform, pad, pad, UiKit.RowHeight(tSize));

            float top = pad + UiKit.LineHeight(tSize) + UiKit.Css(10f);
            float listH = crt.sizeDelta.y - top - pad - UiKit.Css(46f) - UiKit.Css(8f);

            ScrollRect scroll = UiKit.MakeScroll(card.transform, "Rows", out _content);
            RectTransform srt = scroll.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0f, 1f);
            srt.anchorMax = new Vector2(1f, 1f);
            srt.pivot = new Vector2(0.5f, 1f);
            srt.sizeDelta = new Vector2(-pad * 2f, listH);
            srt.anchoredPosition = new Vector2(0f, -top);

            float y = 0f, rowH = UiKit.Css(52f), gap = UiKit.Css(8f);

            _nameLabel = AddRow("Name", y, rowH, RenameMap); y += rowH + gap;
            _publicLabel = AddRow("Public", y, rowH, TogglePublic); y += rowH + gap;

            int hSize = UiKit.CssFont(11.5f);
            Text warn = UiKit.MakeText(_content, "PublicWarn",
                                       UiStrings.Tr("สาธารณะ = ใครเปิดก็แก้แมพนี้ได้ ไม่ใช่แค่ดู"),
                                       hSize, TextAnchor.UpperLeft, UiKit.TextDim);
            RowIn(warn.rectTransform, y, UiKit.RowHeight(hSize, 2));
            y += UiKit.LineHeight(hSize) + gap;

            _searchLabel = AddRow("Searchable", y, rowH, ToggleSearchable); y += rowH + gap;
            AddRow("Editors", y, rowH, EditEditors, UiStrings.Tr("ให้สิทธิ์แก้ไขทางอีเมล")); y += rowH + gap;
            _waterLabel = AddStepper("Water", y, rowH, () => StepWater(-10f), () => StepWater(10f));
            y += rowH + gap;
            _areaLabel = AddStepper("Area", y, rowH, () => StepArea(-0.1f), () => StepArea(0.1f));
            y += rowH + gap * 2f;

            AddRow("Cover", y, rowH, CaptureCover, UiStrings.Tr("ตั้งรูปหน้าปก")); y += rowH + gap;
            _clearLabel = AddRow("Clear", y, rowH, ClearAll, UiStrings.Tr("ล้างทั้งหมด"), DangerFg);
            y += rowH;

            _content.sizeDelta = new Vector2(0f, y);

            Button close = UiKit.MakeButton(card.transform, "Close", UiStrings.Tr("ปิด"),
                                            UiKit.CssFont(14f), new Color(0.2f, 0.267f, 0.333f, 1f),
                                            UiKit.TextMain, Close);
            Image cbg = close.GetComponent<Image>();
            if (cbg != null) { cbg.sprite = UiKit.RoundedSprite(10f); cbg.type = Image.Type.Sliced; }
            RectTransform clrt = close.GetComponent<RectTransform>();
            clrt.anchorMin = new Vector2(0f, 0f);
            clrt.anchorMax = new Vector2(1f, 0f);
            clrt.pivot = new Vector2(0.5f, 0f);
            clrt.sizeDelta = new Vector2(-pad * 2f, UiKit.Css(46f));
            clrt.anchoredPosition = new Vector2(0f, pad);

            Refresh();
            Debug.Log($"[Map] settings open public={_isPublic} searchable={_searchable}");
        }

        private Text AddRow(string name, float y, float h, UnityEngine.Events.UnityAction onClick,
                            string fixedLabel = null, Color? fg = null)
        {
            Button b = UiKit.MakeButton(_content, name, fixedLabel ?? "", UiKit.CssFont(13f),
                                        RowBg, fg ?? UiKit.TextMain, onClick);
            Image bg = b.GetComponent<Image>();
            if (bg != null) { bg.sprite = UiKit.RoundedSprite(10f); bg.type = Image.Type.Sliced; }
            RowIn(b.GetComponent<RectTransform>(), y, h);
            return b.GetComponentInChildren<Text>();
        }

        private Text AddStepper(string name, float y, float h,
                                UnityEngine.Events.UnityAction minus, UnityEngine.Events.UnityAction plus)
        {
            Image bg = UiKit.MakeRounded(_content, name, RowBg, 10f);
            RowIn(bg.rectTransform, y, h);

            Button dec = UiKit.MakeButton(bg.transform, "Minus", "−", UiKit.CssFont(18f),
                                          new Color(0f, 0f, 0f, 0f), UiKit.TextMain, minus);
            Side(dec.GetComponent<RectTransform>(), 0f);
            Button inc = UiKit.MakeButton(bg.transform, "Plus", "+", UiKit.CssFont(18f),
                                          new Color(0f, 0f, 0f, 0f), UiKit.TextMain, plus);
            Side(inc.GetComponent<RectTransform>(), 1f);

            int size = UiKit.CssFont(13f);
            Text t = UiKit.MakeLine(bg.transform, "Label", "", size, TextAnchor.MiddleCenter, UiKit.TextMain);
            RectTransform trt = t.rectTransform;
            trt.anchorMin = new Vector2(0f, 0.5f);
            trt.anchorMax = new Vector2(1f, 0.5f);
            trt.pivot = new Vector2(0.5f, 0.5f);
            trt.sizeDelta = new Vector2(-UiKit.Css(96f), UiKit.RowHeight(size));
            trt.anchoredPosition = Vector2.zero;
            return t;
        }

        private static void Side(RectTransform rt, float side)
        {
            rt.anchorMin = new Vector2(side, 0.5f);
            rt.anchorMax = new Vector2(side, 0.5f);
            rt.pivot = new Vector2(side, 0.5f);
            rt.sizeDelta = new Vector2(UiKit.Css(46f), UiKit.Css(46f));
            rt.anchoredPosition = Vector2.zero;
        }

        private void RowIn(RectTransform rt, float y, float h)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, h);
            rt.anchoredPosition = new Vector2(0f, -y);
        }

        private static void Row(RectTransform rt, float pad, float y, float h)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(-pad * 2f, h);
            rt.anchoredPosition = new Vector2(0f, -y);
        }

        // ── state ────────────────────────────────────────────────────────────────

        private void Refresh()
        {
            var boot = FindFirstObjectByType<AppBoot>();
            SceneData scene = boot != null ? boot.CurrentScene : null;

            if (_nameLabel != null)
                _nameLabel.text = UiStrings.Tr("ชื่อแมพ") + "  " +
                                  (scene != null && !string.IsNullOrWhiteSpace(scene.Name)
                                       ? scene.Name : UiStrings.Tr("(ยังไม่ตั้งชื่อ)"));
            if (_publicLabel != null)
                _publicLabel.text = UiStrings.Tr(_isPublic ? "สาธารณะ" : "ส่วนตัว");
            if (_searchLabel != null)
                _searchLabel.text = UiStrings.Tr(_searchable ? "แสดงในการค้นหา" : "ไม่แสดงในการค้นหา");
            if (_waterLabel != null)
                _waterLabel.text = UiStrings.Tr("ระดับน้ำ") + "  " + Mathf.RoundToInt(WaterLevel());
            if (_areaLabel != null)
                _areaLabel.text = UiStrings.Tr("ขนาดพื้นที่") + "  " + AreaScale().ToString("0.0") + "×";
            if (_clearLabel != null && !_clearArmed)
                _clearLabel.text = UiStrings.Tr("ล้างทั้งหมด");
        }

        private static float WaterLevel()
        {
            var boot = FindFirstObjectByType<AppBoot>();
            SceneEnv env = boot != null && boot.CurrentScene != null ? boot.CurrentScene.Env : null;
            return env != null ? (float)env.WaterLevel : 240f;
        }

        private static float AreaScale()
        {
            var boot = FindFirstObjectByType<AppBoot>();
            SceneEnv env = boot != null && boot.CurrentScene != null ? boot.CurrentScene.Env : null;
            return env != null ? (float)env.AreaScale : 1f;
        }

        private static JObject Env()
        {
            var boot = FindFirstObjectByType<AppBoot>();
            SceneData scene = boot != null ? boot.CurrentScene : null;
            if (scene == null) return null;
            if (!(scene.Root["env"] is JObject env))
            {
                env = new JObject();
                scene.Root["env"] = env;
            }
            return env;
        }

        // ── actions ──────────────────────────────────────────────────────────────

        private void RenameMap()
        {
            var boot = FindFirstObjectByType<AppBoot>();
            SceneData scene = boot != null ? boot.CurrentScene : null;
            if (scene == null) return;

            TextPrompt.Show(UiStrings.Tr("ชื่อแมพ"), scene.Name, value =>
            {
                string name = (value ?? "").Trim();
                if (name.Length == 0) return;
                scene.Root["name"] = name;
                StartCoroutine(MapSaveClient.Rename(boot.CurrentMapId, name, r =>
                {
                    Toast.ShowTr(r.Ok ? "บันทึกแล้ว" : "บันทึกไม่สำเร็จ");
                    Refresh();
                }));
            }, maxChars: 120);
        }

        private void TogglePublic()
        {
            var boot = FindFirstObjectByType<AppBoot>();
            if (boot == null) return;
            bool next = !_isPublic;

            StartCoroutine(MapSaveClient.SetPublic(boot.CurrentMapId, next, r =>
            {
                if (!r.Ok) { Toast.ShowTr(r.Forbidden ? "เฉพาะเจ้าของแมพเท่านั้น" : "บันทึกไม่สำเร็จ"); return; }
                _isPublic = next;
                if (boot.CurrentScene != null) boot.CurrentScene.Root["isPublic"] = next;
                Refresh();
                Toast.ShowTr(next ? "เปิดสาธารณะแล้ว" : "เป็นส่วนตัวแล้ว");
            }));
        }

        private void ToggleSearchable()
        {
            var boot = FindFirstObjectByType<AppBoot>();
            if (boot == null) return;
            bool next = !_searchable;

            StartCoroutine(MapSaveClient.SetSearchable(boot.CurrentMapId, next, r =>
            {
                if (!r.Ok) { Toast.ShowTr(r.Forbidden ? "เฉพาะเจ้าของแมพเท่านั้น" : "บันทึกไม่สำเร็จ"); return; }
                _searchable = next;
                if (boot.CurrentScene != null) boot.CurrentScene.Root["searchable"] = next;
                Refresh();
            }));
        }

        /// <summary>Comma-separated emails → editPolicy "some".</summary>
        private void EditEditors()
        {
            var boot = FindFirstObjectByType<AppBoot>();
            if (boot == null) return;

            TextPrompt.Show(UiStrings.Tr("ให้สิทธิ์แก้ไขทางอีเมล"), "", value =>
            {
                var emails = new List<string>();
                foreach (string part in (value ?? "").Split(','))
                    if (Account.IsValidEmail(part)) emails.Add(part.Trim());

                if (emails.Count == 0) { Toast.ShowTr("อีเมลไม่ถูกต้อง"); return; }
                StartCoroutine(MapSaveClient.SetEditors(boot.CurrentMapId, emails.ToArray(), r =>
                    Toast.ShowTr(r.Ok ? "บันทึกสิทธิ์แล้ว"
                                      : (r.Forbidden ? "เฉพาะเจ้าของแมพเท่านั้น" : "บันทึกไม่สำเร็จ"))));
            }, maxChars: 300);
        }

        private void StepWater(float d)
        {
            JObject env = Env();
            if (env == null) return;
            env["waterLevel"] = Mathf.Clamp(WaterLevel() + d, -100f, 600f);   // the web's slider range
            MapEditor.MarkSculpted();
            SeabedSculptor.Redraw();
            Refresh();
        }

        private void StepArea(float d)
        {
            JObject env = Env();
            if (env == null) return;
            float next = Mathf.Clamp(AreaScale() + d, 0.3f, 6f);              // the web's slider range
            env["areaScale"] = next;

            var sb = FindFirstObjectByType<SceneBuilder>();
            if (sb != null)
            {
                sb.GetSeabedShape(out float sx, out float sz, out float slopeX, out float slopeZ,
                                  out float thickness);
                // areaScale multiplies both axes; areaScaleX/Z are separate multipliers on top.
                float ax = (float)(Env()["areaScaleX"] != null ? (double)Env()["areaScaleX"] : 1.0);
                float az = (float)(Env()["areaScaleZ"] != null ? (double)Env()["areaScaleZ"] : 1.0);
                sb.SetSeabedShape(next * ax, next * az, slopeX, slopeZ, thickness);
            }
            MapEditor.MarkSculpted();
            SeabedSculptor.Redraw();
            Refresh();
        }

        /// <summary>
        /// Photograph the map as it looks right now and make that its cover. The sheet closes
        /// first: a cover with this panel across it would be worse than no cover at all.
        /// </summary>
        private void CaptureCover()
        {
            var runner = FindFirstObjectByType<AppBoot>();
            if (runner == null) return;
            Close();
            runner.StartCoroutine(ThumbnailCapture.CaptureAndSave(_ => { }));
        }

        /// <summary>Two taps — this empties the whole map.</summary>
        private void ClearAll()
        {
            if (!_clearArmed)
            {
                _clearArmed = true;
                if (_clearLabel != null) _clearLabel.text = UiStrings.Tr("แตะอีกครั้งเพื่อล้างทั้งแมพ");
                return;
            }
            _clearArmed = false;

            int n = MapEditor.ClearAll();
            Toast.ShowTr("ล้างแมพแล้ว");
            Debug.Log($"[Map] cleared {n} item(s) from settings");
            Refresh();
        }
    }
}
