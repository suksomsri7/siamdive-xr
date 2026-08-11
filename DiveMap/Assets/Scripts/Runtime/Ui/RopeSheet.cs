using UnityEngine;
using UnityEngine.UI;
using DiveMap.Core;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// 🪢 "ปรับเชือก" — the web's <c>_editRope</c> (builder.html:3234): sag, colour, thickness,
    /// re-anchor, delete. Plus the tie mode itself (<c>_startRopeFree</c> :3220), because a panel
    /// that edits ropes is useless on a map where none can be made.
    ///
    /// Tie mode is two taps on two objects. The anchor is stored in the tapped object's LOCAL
    /// space, which is what lets the rope survive that object being moved afterwards — see
    /// <see cref="RopeMath"/>. Tapping empty water cancels, exactly as the web does
    /// (<c>แตะที่ว่าง = ยกเลิก</c>).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RopeSheet : MonoBehaviour
    {
        private static RopeSheet _open;
        private static RopeSheet _tying;

        private static readonly Color PanelBg = new Color(0.051f, 0.133f, 0.188f, 0.97f);
        private static readonly Color RowBg = new Color(1f, 1f, 1f, 0.06f);
        private static readonly Color TitleFg = new Color(0.498f, 0.753f, 1f, 1f);
        private static readonly Color DelFg = new Color(1f, 0.541f, 0.612f, 1f);

        private Rope _rope;
        private Text _sagLabel, _thickLabel;
        private RopeEnd? _firstPick;

        // ── QC surface ───────────────────────────────────────────────────────────
        public static bool IsOpen => _open != null;
        public static bool IsTying => _tying != null;
        public static RopeSheet Current => _open;
        public string RopeId => _rope != null ? _rope.Id : null;

        // ── tie mode ─────────────────────────────────────────────────────────────

        /// <summary>Start "tap two objects to tie a rope".</summary>
        public static void StartTie()
        {
            if (_tying != null) return;
            RectTransform host = UiShell.Instance != null ? UiShell.Instance.OverlayRoot : null;
            if (host == null) return;

            var go = new GameObject("RopeTie");
            go.transform.SetParent(host, false);
            var rt = go.AddComponent<RectTransform>();
            UiKit.Stretch(rt);
            _tying = go.AddComponent<RopeSheet>();
            _tying.BuildHint(rt);
            Toast.ShowTr("แตะจุดยึดที่ 1 (บนวัตถุ)");
            Debug.Log("[Rope] tie mode started");
        }

        public static void CancelTie()
        {
            if (_tying == null) return;
            Destroy(_tying.gameObject);
            _tying = null;
            Debug.Log("[Rope] tie mode cancelled");
        }

        private void BuildHint(RectTransform root)
        {
            Image pill = UiKit.MakeRounded(root, "Hint", PanelBg, 22f);
            RectTransform rt = pill.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(UiKit.Css(320f), UiKit.Css(44f));
            rt.anchoredPosition = new Vector2(0f, -UiKit.Css(72f));

            _hint = UiKit.MakeLine(pill.transform, "Text", UiStrings.Tr("แตะจุดยึดที่ 1 (บนวัตถุ)"),
                                   UiKit.CssFont(13f), TextAnchor.MiddleCenter, UiKit.TextMain);
            UiKit.Stretch(_hint.rectTransform);
        }
        private Text _hint;

        /// <summary>QC only — pick an anchor without a finger.</summary>
        public static void QcPick(string itemId, Vector3 localPoint)
        {
            if (_tying == null) return;
            _tying.Pick(new RopeEnd
            {
                ItemId = itemId, Lx = localPoint.x, Ly = localPoint.y, Lz = localPoint.z,
            });
        }

        private void Update()
        {
            if (_tying != this) return;
            // WO-N — View *or* Edit; see ModeRules.AllowsEditTools.
            if (!ModeRules.AllowsEditTools(ModeManager.Current)) { CancelTie(); return; }

            bool down;
            Vector2 pos;
            if (Input.touchCount > 0)
            {
                Touch t = Input.GetTouch(0);
                pos = t.position;
                down = t.phase == TouchPhase.Began;
            }
            else { pos = Input.mousePosition; down = Input.GetMouseButtonDown(0); }
            if (!down || UiShell.PointerOverUi()) return;

            Camera cam = Camera.main;
            if (cam == null) return;

            // The tap has to land ON an object. Empty water cancels, which is the web's rule and
            // also the only escape a player has from a mode with no visible exit.
            if (!Physics.Raycast(cam.ScreenPointToRay(pos), out RaycastHit hit, 5000f) ||
                hit.collider == null || hit.collider.gameObject.name == "Seabed")
            {
                Toast.ShowTr("ยกเลิกเชือก");
                CancelTie();
                return;
            }

            Transform item = ItemRootOf(hit.collider.transform);
            if (item == null || !ItemPicker.ParseItemName(item.name, out string id, out _))
            {
                Toast.ShowTr("แตะให้โดนวัตถุ");
                return;
            }

            Vector3 local = item.InverseTransformPoint(hit.point);
            Pick(new RopeEnd { ItemId = id, Lx = local.x, Ly = local.y, Lz = local.z });
        }

        /// <summary>Walk up to the object SceneBuilder named — the hit is usually a child mesh.</summary>
        private static Transform ItemRootOf(Transform t)
        {
            for (Transform n = t; n != null; n = n.parent)
                if (ItemPicker.IsItemName(n.name)) return n;
            return null;
        }

        private void Pick(RopeEnd end)
        {
            if (_firstPick == null)
            {
                _firstPick = end;
                if (_hint != null) _hint.text = UiStrings.Tr("แตะจุดยึดที่ 2");
                Debug.Log("[Rope] first anchor on " + end.ItemId);
                return;
            }

            RopeEnd a = _firstPick.Value;
            if (a.ItemId == end.ItemId)
            {
                Toast.ShowTr("ต้องเป็นคนละชิ้น");
                return;   // a rope from an object to itself is a knot, not a rope
            }

            RopeSystem sys = RopeSystem.Ensure();
            Rope made = sys != null ? sys.Add(a, end) : null;
            CancelTie();

            if (made == null) { Toast.ShowTr("ผูกเชือกไม่สำเร็จ"); return; }
            Toast.ShowTr("เชื่อมเชือกแล้ว");
            Open(made.Id);
        }

        // ── edit panel ───────────────────────────────────────────────────────────

        public static void Open(string ropeId)
        {
            Close();
            RopeSystem sys = RopeSystem.Instance;
            Rope rope = sys != null ? sys.Find(ropeId) : null;
            if (rope == null) return;

            RectTransform host = UiShell.Instance != null ? UiShell.Instance.OverlayRoot : null;
            if (host == null) return;

            var go = new GameObject("RopeSheet");
            go.transform.SetParent(host, false);
            var rt = go.AddComponent<RectTransform>();
            UiKit.Stretch(rt);
            var sheet = go.AddComponent<RopeSheet>();
            sheet._rope = rope;
            sheet.BuildPanel(rt);
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
            if (_tying == this) _tying = null;
        }

        private void BuildPanel(RectTransform root)
        {
            Button scrim = UiKit.MakeButton(root, "Scrim", null, 0, new Color(0f, 0f, 0f, 0.45f),
                                            Color.white, Close);
            UiKit.Stretch(scrim.GetComponent<RectTransform>());

            float pad = UiKit.Css(16f);
            Image card = UiKit.MakeRounded(root, "Card", PanelBg, 18f);
            RectTransform crt = card.rectTransform;
            crt.anchorMin = new Vector2(0.5f, 0.5f);
            crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = new Vector2(
                Mathf.Min(UiKit.Css(360f), Screen.width / UiKit.CanvasScale - UiKit.Css(40f)), 0f);
            crt.anchoredPosition = Vector2.zero;

            float y = pad;
            int tSize = UiKit.CssFont(15f);
            Text title = UiKit.MakeLine(card.transform, "Title", UiStrings.Tr("ปรับเชือก"), tSize,
                                        TextAnchor.UpperLeft, TitleFg);
            title.fontStyle = FontStyle.Bold;
            Row(title.rectTransform, pad, y, UiKit.RowHeight(tSize));
            y += UiKit.LineHeight(tSize) + UiKit.Css(10f);

            float w = crt.sizeDelta.x - pad * 2f;
            _sagLabel = Stepper(card.transform, "Sag", pad, y, w, () => StepSag(-4), () => StepSag(4));
            y += UiKit.Css(44f) + UiKit.Css(8f);
            _thickLabel = Stepper(card.transform, "Thick", pad, y, w,
                                  () => StepThick(-0.15), () => StepThick(0.15));
            y += UiKit.Css(44f) + UiKit.Css(12f);

            // seven colours
            float sw = UiKit.Css(30f), gap = UiKit.Css(8f);
            float startX = pad + (w - (RopeMath.Colors.Length * sw + (RopeMath.Colors.Length - 1) * gap)) * 0.5f;
            for (int i = 0; i < RopeMath.Colors.Length; i++)
            {
                string hex = RopeMath.Colors[i];
                Image sIm = UiKit.MakeCircle(card.transform, "C" + i, SelectionToolbar.Hex(hex));
                var b = sIm.gameObject.AddComponent<Button>();
                b.targetGraphic = sIm;
                b.onClick.AddListener(() => SetColor(hex));
                Place(sIm.rectTransform, startX + i * (sw + gap), y, sw, sw);

                Image rim = UiKit.MakeCircle(sIm.transform, "Rim",
                                             hex == _rope.Color ? TitleFg : new Color(0.2f, 0.27f, 0.33f, 1f),
                                             0.10f);
                rim.raycastTarget = false;
                UiKit.Stretch(rim.rectTransform);
            }
            y += sw + UiKit.Css(14f);

            float bw = (w - UiKit.Css(8f)) * 0.5f;
            Button del = UiKit.MakeButton(card.transform, "Delete", UiStrings.Tr("ลบเชือก"),
                                          UiKit.CssFont(14f), RowBg, DelFg, DeleteRope);
            Round(del);
            Place(del.GetComponent<RectTransform>(), pad, y, bw, UiKit.Css(46f));

            Button done = UiKit.MakeButton(card.transform, "Done", UiStrings.Tr("เสร็จ"),
                                           UiKit.CssFont(14f), UiKit.Accent, UiKit.OnAccent, Close);
            Round(done);
            Place(done.GetComponent<RectTransform>(), pad + bw + UiKit.Css(8f), y, bw, UiKit.Css(46f));
            y += UiKit.Css(46f);

            crt.sizeDelta = new Vector2(crt.sizeDelta.x, y + pad);
            UpdateLabels();
        }

        private static void Round(Button b)
        {
            Image img = b.GetComponent<Image>();
            if (img == null) return;
            img.sprite = UiKit.RoundedSprite(12f);
            img.type = Image.Type.Sliced;
        }

        private static void Row(RectTransform rt, float pad, float y, float h)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(-pad * 2f, h);
            rt.anchoredPosition = new Vector2(0f, -y);
        }

        private static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, -y);
        }

        private Text Stepper(Transform parent, string name, float x, float y, float w,
                             UnityEngine.Events.UnityAction minus, UnityEngine.Events.UnityAction plus)
        {
            Image bg = UiKit.MakeRounded(parent, name, RowBg, 12f);
            Place(bg.rectTransform, x, y, w, UiKit.Css(44f));

            Button dec = UiKit.MakeButton(bg.transform, "Minus", "−", UiKit.CssFont(18f),
                                          new Color(0f, 0f, 0f, 0f), UiKit.TextMain, minus);
            PlaceIn(dec.GetComponent<RectTransform>(), 0f);
            Button inc = UiKit.MakeButton(bg.transform, "Plus", "+", UiKit.CssFont(18f),
                                          new Color(0f, 0f, 0f, 0f), UiKit.TextMain, plus);
            PlaceIn(inc.GetComponent<RectTransform>(), 1f);

            int size = UiKit.CssFont(13f);
            Text t = UiKit.MakeLine(bg.transform, "Label", "", size, TextAnchor.MiddleCenter, UiKit.TextMain);
            RectTransform trt = t.rectTransform;
            trt.anchorMin = new Vector2(0f, 0.5f);
            trt.anchorMax = new Vector2(1f, 0.5f);
            trt.pivot = new Vector2(0.5f, 0.5f);
            trt.sizeDelta = new Vector2(-UiKit.Css(88f), UiKit.RowHeight(size));
            trt.anchoredPosition = Vector2.zero;
            return t;
        }

        private static void PlaceIn(RectTransform rt, float side)
        {
            rt.anchorMin = new Vector2(side, 0.5f);
            rt.anchorMax = new Vector2(side, 0.5f);
            rt.pivot = new Vector2(side, 0.5f);
            rt.sizeDelta = new Vector2(UiKit.Css(42f), UiKit.Css(42f));
            rt.anchoredPosition = Vector2.zero;
        }

        // ── actions ──────────────────────────────────────────────────────────────

        private void StepSag(double d) => Apply(() => _rope.Sag = Mathf.Clamp((float)(_rope.Sag + d), 0f, 80f));
        private void StepThick(double d) => Apply(() => _rope.Thick = Mathf.Clamp((float)(_rope.Thick + d), 0.15f, 3f));
        private void SetColor(string hex) => Apply(() => _rope.Color = RopeMath.NormaliseColor(hex));

        private void Apply(System.Action change)
        {
            if (_rope == null) return;
            change();
            RopeSystem sys = RopeSystem.Instance;
            if (sys != null) { sys.Refresh(_rope); sys.Save(); }
            UpdateLabels();
        }

        private void UpdateLabels()
        {
            if (_rope == null) return;
            if (_sagLabel != null)
                _sagLabel.text = UiStrings.Tr("ความห้อย") + "  " + Mathf.RoundToInt((float)_rope.Sag);
            if (_thickLabel != null)
                _thickLabel.text = UiStrings.Tr("ความหนา") + "  " + _rope.Thick.ToString("0.00");
        }

        private void DeleteRope()
        {
            RopeSystem sys = RopeSystem.Instance;
            if (sys != null && _rope != null) sys.Remove(_rope.Id);
            Close();
            Toast.ShowTr("ลบเชือกแล้ว");
        }
    }
}
