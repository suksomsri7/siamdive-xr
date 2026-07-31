using UnityEngine;
using UnityEngine.UI;
using DiveMap.Core;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// ⛰️ "ปั้นพื้น" — the web's <c>showSculpt()</c> panel, plus the finger that drives it.
    ///
    /// Controls, in the web's order:
    ///   ขุดหลุม / ก่อเนิน · ขนาดหัวแปรง · ความแรง · สุ่มพื้น · รีเซ็ตเรียบ
    ///
    /// Two things this panel does that the list of buttons does not show:
    ///  • the sheet MINIMISES rather than closing while you sculpt — the web's
    ///    <c>#sheet.floormode</c> — because a panel covering the floor you are shaping is
    ///    useless. Here it collapses to a strip at the bottom.
    ///  • a stroke commits ONE history entry per finger-down, not per frame. The same rule the
    ///    gizmo follows, for the same reason: autosave otherwise fires all the way through.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SculptSheet : MonoBehaviour
    {
        private static SculptSheet _open;

        private static readonly Color SheetBg = new Color(0.043f, 0.102f, 0.165f, 0.96f);
        private static readonly Color OnBg = new Color(0.224f, 0.690f, 0.910f, 0.28f);
        private static readonly Color OffBg = new Color(1f, 1f, 1f, 0.06f);

        private RectTransform _panel;
        private Image _raiseBg, _digBg;
        private Text _radiusLabel, _strengthLabel, _depthLabel;

        private bool _raise = true;
        private float _radius = SculptBrush.DefaultRadius;
        private float _strength = SculptBrush.DefaultStrength;

        private bool _stroking;
        private bool _changedThisStroke;

        // ── QC surface ───────────────────────────────────────────────────────────
        public static bool IsOpen => _open != null;
        public static SculptSheet Current => _open;
        public bool Raise => _raise;
        public float Radius => _radius;
        public float Strength => _strength;

        public static void Open()
        {
            if (_open != null) return;
            RectTransform host = UiShell.Instance != null ? UiShell.Instance.OverlayRoot : null;
            if (host == null) return;

            var go = new GameObject("SculptSheet");
            go.transform.SetParent(host, false);
            var rt = go.AddComponent<RectTransform>();
            UiKit.Stretch(rt);
            var sheet = go.AddComponent<SculptSheet>();
            sheet.Build(rt);
            _open = sheet;
            Debug.Log($"[Sculpt] panel open ready={SeabedSculptor.Ready}");
        }

        public static void Close()
        {
            if (_open == null) return;
            Destroy(_open.gameObject);
            _open = null;
        }

        private void OnDestroy()
        {
            if (_open == this) { _open = null; MapEditor.Dragging = false; }
        }

        // ── build ────────────────────────────────────────────────────────────────

        private void Build(RectTransform root)
        {
            // NO scrim: the floor has to stay reachable, which is the whole point of the mode.
            float h = UiKit.Css(176f);
            Image panel = UiKit.MakeRounded(root, "Panel", SheetBg, 20f);
            _panel = panel.rectTransform;
            _panel.anchorMin = new Vector2(0f, 0f);
            _panel.anchorMax = new Vector2(1f, 0f);
            _panel.pivot = new Vector2(0.5f, 0f);
            _panel.sizeDelta = new Vector2(0f, h);
            _panel.anchoredPosition = Vector2.zero;

            float pad = UiKit.Css(16f);
            float y = UiKit.Css(12f);

            int tSize = UiKit.CssFont(14f);
            Text title = UiKit.MakeLine(panel.transform, "Title",
                                        UiStrings.Tr("ลากบนพื้นเพื่อปั้น"), tSize,
                                        TextAnchor.UpperLeft, UiKit.TextDim);
            Row(title.rectTransform, pad, y, UiKit.RowHeight(tSize));

            _depthLabel = UiKit.MakeLine(panel.transform, "Depth", "", tSize,
                                         TextAnchor.UpperRight, new Color(0.624f, 0.878f, 1f, 1f));
            _depthLabel.fontStyle = FontStyle.Bold;
            Row(_depthLabel.rectTransform, pad, y, UiKit.RowHeight(tSize));
            y += UiKit.LineHeight(tSize) + UiKit.Css(10f);

            // dig / raise
            float bw = (Screen.width / UiKit.CanvasScale - pad * 2f - UiKit.Css(8f)) * 0.5f;
            _digBg = ModeButton(panel.transform, "Dig", UiStrings.Tr("ขุดหลุม"), pad, y, bw,
                                () => SetRaise(false));
            _raiseBg = ModeButton(panel.transform, "Raise", UiStrings.Tr("ก่อเนิน"),
                                  pad + bw + UiKit.Css(8f), y, bw, () => SetRaise(true));
            y += UiKit.Css(44f) + UiKit.Css(10f);

            // radius / strength — steppers rather than sliders: uGUI's Slider needs a handle,
            // a fill and a background to be usable with a thumb, and two taps say the same thing.
            _radiusLabel = Stepper(panel.transform, "Radius", UiStrings.Tr("ขนาดหัวแปรง"), pad, y, bw,
                                   () => StepRadius(-1), () => StepRadius(1));
            _strengthLabel = Stepper(panel.transform, "Strength", UiStrings.Tr("ความแรง"),
                                     pad + bw + UiKit.Css(8f), y, bw,
                                     () => StepStrength(-1), () => StepStrength(1));
            y += UiKit.Css(44f) + UiKit.Css(10f);

            float aw = (Screen.width / UiKit.CanvasScale - pad * 2f - UiKit.Css(16f)) / 3f;
            Action(panel.transform, "Random", UiStrings.Tr("สุ่มพื้น"), pad, y, aw, Randomise);
            Action(panel.transform, "Flat", UiStrings.Tr("รีเซ็ตเรียบ"),
                   pad + aw + UiKit.Css(8f), y, aw, Flatten);
            Action(panel.transform, "Done", UiStrings.Tr("เสร็จ"),
                   pad + (aw + UiKit.Css(8f)) * 2f, y, aw, Close, UiKit.Accent);

            SetRaise(_raise);
            UpdateLabels();
        }

        private static void Row(RectTransform rt, float pad, float y, float h)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(-pad * 2f, h);
            rt.anchoredPosition = new Vector2(0f, -y);
        }

        private Image ModeButton(Transform parent, string name, string label, float x, float y,
                                 float w, UnityEngine.Events.UnityAction onClick)
        {
            Button b = UiKit.MakeButton(parent, name, label, UiKit.CssFont(14f), OffBg,
                                        UiKit.TextMain, onClick);
            Image bg = b.GetComponent<Image>();
            if (bg != null) { bg.sprite = UiKit.RoundedSprite(12f); bg.type = Image.Type.Sliced; }
            Place(b.GetComponent<RectTransform>(), x, y, w, UiKit.Css(44f));
            return bg;
        }

        private Text Stepper(Transform parent, string name, string label, float x, float y, float w,
                             UnityEngine.Events.UnityAction minus, UnityEngine.Events.UnityAction plus)
        {
            Image bg = UiKit.MakeRounded(parent, name, OffBg, 12f);
            Place(bg.rectTransform, x, y, w, UiKit.Css(44f));

            Button dec = UiKit.MakeButton(bg.transform, "Minus", "−", UiKit.CssFont(18f),
                                          new Color(0f, 0f, 0f, 0f), UiKit.TextMain, minus);
            PlaceIn(dec.GetComponent<RectTransform>(), 0f, UiKit.Css(40f));

            Button inc = UiKit.MakeButton(bg.transform, "Plus", "+", UiKit.CssFont(18f),
                                          new Color(0f, 0f, 0f, 0f), UiKit.TextMain, plus);
            PlaceIn(inc.GetComponent<RectTransform>(), 1f, UiKit.Css(40f));

            int size = UiKit.CssFont(12f);
            Text t = UiKit.MakeLine(bg.transform, "Label", label, size, TextAnchor.MiddleCenter,
                                    UiKit.TextMain);
            RectTransform trt = t.rectTransform;
            trt.anchorMin = new Vector2(0f, 0.5f);
            trt.anchorMax = new Vector2(1f, 0.5f);
            trt.pivot = new Vector2(0.5f, 0.5f);
            trt.sizeDelta = new Vector2(-UiKit.Css(80f), UiKit.RowHeight(size));
            trt.anchoredPosition = Vector2.zero;
            return t;
        }

        private static void Action(Transform parent, string name, string label, float x, float y,
                                   float w, UnityEngine.Events.UnityAction onClick, Color? bgColor = null)
        {
            Button b = UiKit.MakeButton(parent, name, label, UiKit.CssFont(13f),
                                        bgColor ?? OffBg,
                                        bgColor.HasValue ? UiKit.OnAccent : UiKit.TextMain, onClick);
            Image bg = b.GetComponent<Image>();
            if (bg != null) { bg.sprite = UiKit.RoundedSprite(12f); bg.type = Image.Type.Sliced; }
            Place(b.GetComponent<RectTransform>(), x, y, w, UiKit.Css(42f));
        }

        private static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, -y);
        }

        private static void PlaceIn(RectTransform rt, float side, float w)
        {
            rt.anchorMin = new Vector2(side, 0.5f);
            rt.anchorMax = new Vector2(side, 0.5f);
            rt.pivot = new Vector2(side, 0.5f);
            rt.sizeDelta = new Vector2(w, w);
            rt.anchoredPosition = Vector2.zero;
        }

        // ── controls ─────────────────────────────────────────────────────────────

        private void SetRaise(bool raise)
        {
            _raise = raise;
            if (_raiseBg != null) _raiseBg.color = raise ? OnBg : OffBg;
            if (_digBg != null) _digBg.color = raise ? OffBg : OnBg;
        }

        private void StepRadius(int dir)
        {
            _radius = Mathf.Clamp(_radius + dir * 12f, SculptBrush.MinRadius, SculptBrush.MaxRadius);
            UpdateLabels();
        }

        private void StepStrength(int dir)
        {
            _strength = Mathf.Clamp(_strength + dir * 1f, SculptBrush.MinStrength, SculptBrush.MaxStrength);
            UpdateLabels();
        }

        private void UpdateLabels()
        {
            if (_radiusLabel != null)
                _radiusLabel.text = UiStrings.Tr("ขนาดหัวแปรง") + "  " + Mathf.RoundToInt(_radius);
            if (_strengthLabel != null)
                _strengthLabel.text = UiStrings.Tr("ความแรง") + "  " + _strength.ToString("0.#");
        }

        private void Randomise()
        {
            SeabedSculptor.Randomise(28f, Random.Range(1, 1000000));
            Commit();
            Toast.ShowTr("สุ่มพื้นแล้ว");
        }

        private void Flatten()
        {
            SeabedSculptor.Flatten();
            Commit();
            Toast.ShowTr("รีเซ็ตพื้นแล้ว");
        }

        // ── the finger ───────────────────────────────────────────────────────────

        private void Update()
        {
            if (ModeManager.Current != AppMode.View) { Close(); return; }

            bool down, held, up;
            Vector2 pos;

            if (Input.touchCount > 0)
            {
                Touch t = Input.GetTouch(0);
                pos = t.position;
                down = t.phase == TouchPhase.Began;
                held = t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary;
                up = t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled;
            }
            else
            {
                pos = Input.mousePosition;
                down = Input.GetMouseButtonDown(0);
                held = Input.GetMouseButton(0);
                up = Input.GetMouseButtonUp(0);
            }

            if (down)
            {
                if (UiShell.PointerOverUi()) return;   // the panel's own buttons
                _stroking = true;
                _changedThisStroke = false;
                MapEditor.Dragging = true;             // hold autosave until the finger lifts
            }
            if (_stroking && (down || held)) Paint(pos);
            if (up && _stroking)
            {
                _stroking = false;
                MapEditor.Dragging = false;
                if (_changedThisStroke) Commit();
            }
        }

        /// <summary>QC only — one stroke at a screen point, with no touch hardware.</summary>
        public void QcPaint(Vector2 screenPos)
        {
            _changedThisStroke = false;
            Paint(screenPos);
            if (_changedThisStroke) Commit();
        }

        private void Paint(Vector2 screenPos)
        {
            Camera cam = Camera.main;
            if (cam == null || !SeabedSculptor.Ready) return;

            // Hit the seabed's own collider — the floor is the only thing that can be sculpted,
            // and a ray/plane test would let a brush work through a wreck.
            Ray ray = cam.ScreenPointToRay(screenPos);
            if (!Physics.Raycast(ray, out RaycastHit hit, 5000f)) return;
            if (hit.collider == null || hit.collider.gameObject.name != "Seabed") return;

            int touched = SeabedSculptor.Stroke(hit.point, _radius, _strength, _raise);
            if (touched > 0) _changedThisStroke = true;

            if (_depthLabel != null)
            {
                float m = SculptBrush.DepthMetres(WaterLevel(), hit.point.y);
                _depthLabel.text = (m < 0f ? UiStrings.Tr("พ้นน้ำ") + " +" + (-m).ToString("0.0")
                                           : UiStrings.Tr("ลึก") + " " + m.ToString("0.0"))
                                 + " " + UiStrings.Tr("ม.");
            }
        }

        private static float WaterLevel()
        {
            var boot = FindFirstObjectByType<AppBoot>();
            SceneEnv env = boot != null && boot.CurrentScene != null ? boot.CurrentScene.Env : null;
            return env != null ? (float)env.WaterLevel : 240f;
        }

        /// <summary>
        /// Record the floor for undo and let autosave pick it up. The sculpt array lives in
        /// <c>env</c>, not <c>items</c>, so this snapshots the item list purely to move the
        /// history cursor — the floor itself rides along in the same PATCH.
        /// </summary>
        private void Commit()
        {
            var boot = FindFirstObjectByType<AppBoot>();
            if (boot == null || boot.CurrentScene == null) return;
            MapEditor.MarkSculpted();
            Debug.Log("[Sculpt] stroke committed");
        }
    }
}
