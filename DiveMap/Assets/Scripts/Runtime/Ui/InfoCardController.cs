using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using DiveMap.Core;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// "Tap an object → info card" (WO-XR-05.3).
    ///
    /// Flow: tap (not a drag, not on UI) → ray from <see cref="Camera.main"/> → nearest
    /// item AABB via <see cref="ItemPicker"/> → bottom sheet with name / type / depth.
    ///
    /// Three design constraints from the work order, all load-bearing:
    ///  • No colliders are added to the scene — SceneBuilder is owned by another work
    ///    order, and item AABBs are gathered from the live renderers instead.
    ///  • Schools and pods render through RenderMeshInstanced and therefore have NO
    ///    Renderer at all. Without a fallback volume a shoal would be untappable, so
    ///    renderer-less items get a sphere sized by the very same MarineMath geometry
    ///    the swarm itself was built from.
    ///  • The orbit camera keeps working while the card is open: the card is a bottom
    ///    sheet, not a modal, and it is NOT pushed onto the UiNav stack (a push would
    ///    disable the camera).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InfoCardController : MonoBehaviour
    {
        /// <summary>Max finger travel that still counts as a tap rather than a camera drag.</summary>
        private const float TapMaxMovePixels = 20f;

        /// <summary>Max press duration of a tap (a long press is a drag/hold, not a pick).</summary>
        private const float TapMaxSeconds = 0.35f;

        /// <summary>Fallback water level when a map has no "Water" node (SceneBuilder's own default).</summary>
        private const float DefaultWaterLevel = 4f;

        // CSS px, like the rest of the chrome: the web's info strip is ~110 px tall with a 16 px
        // title, not a 380-unit slab with 46-unit text.
        private static float CardHeight => UiKit.Css(66f);   // two lines + padding, like #seltool

        private RectTransform _layer;
        private Text _nameText;
        private Text _kindText;
        private Text _depthText;
        private Text _descText;
        private RectTransform _cardRt;
        private UnityEngine.UI.RawImage _previewImg;
        private SpeciesPreview _preview;
        private ScrollRect _descScroll;
        private string _openAssetId;
        private Button _closeButton;
        private Button _editButton;
        private string _openId;
        private UnityEngine.UI.RawImage _thumb;
        private UiNav _nav;

        private AssetManifest _manifest;

        // Item labels (item.lb) are NOT recoverable from the scene graph — SceneBuilder
        // only bakes them into a TextMesh for placeholder items. They are fetched lazily
        // from the same map JSON AppBoot already loaded, on the first successful pick, so
        // a user who never taps an object never pays for the request.
        private Dictionary<string, string> _labels;
        private GameObject _labelsMapRoot;
        private bool _labelsBusy;

        private string _openKey;
        private string _openName;
        private string _openKindKey;
        private string _openDesc;
        private double _openDepth;

        private bool _tracking;
        private Vector2 _downPos;
        private float _downTime;
        private bool _downOverUi;

        public bool IsVisible => _layer != null && _layer.gameObject.activeSelf;

        /// <summary>Name / type / depth of the card currently shown (QC + tests).</summary>
        public string CurrentName => _openName;
        public string CurrentKind => _openKindKey;
        public double CurrentDepth => _openDepth;

        // ── build ────────────────────────────────────────────────────────────────

        public void Build(RectTransform parent, UiNav nav)
        {
            _nav = nav;
            _layer = UiKit.MakeNode(parent, "CardLayer");

            // A CENTRED pill, like the web's #seltool (bottom 22, radius 30, auto width): a
            // full-width bar sat on top of the ☰ and the compass, which live at the right edge.
            // Capped at 86vw / 380 CSS px, the same envelope the web gives its modals.
            float width = Mathf.Min(Screen.width / UiKit.CanvasScale * 0.86f, UiKit.Css(380f));
            Image card = UiKit.MakeRounded(_layer, "Card", UiKit.Glass, 20f);
            RectTransform rt = card.rectTransform;
            _cardRt = rt;
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(width, CardHeight);
            rt.anchoredPosition = new Vector2(0f, UiKit.Css(22f));

            float pad = UiKit.Css(14f);
            float y = UiKit.Css(10f);

            _nameText = UiKit.MakeText(rt, "Name", "", UiKit.CssFont(15f), TextAnchor.MiddleLeft, UiKit.Accent);
            _nameText.fontStyle = FontStyle.Bold;
            UiKit.TopRow(_nameText.rectTransform, y, UiKit.RowHeight(UiKit.CssFont(15f)), pad, UiKit.Css(44f));
            y += UiKit.RowHeight(UiKit.CssFont(15f));

            // Kind and depth share one muted line, dot-separated like the web's meta lines.
            _kindText = UiKit.MakeText(rt, "Kind", "", UiKit.CssFont(12f), TextAnchor.MiddleLeft, UiKit.TextDim);
            UiKit.TopRow(_kindText.rectTransform, y, UiKit.RowHeight(UiKit.CssFont(12f)), pad, UiKit.Css(44f));

            _depthText = UiKit.MakeText(rt, "Depth", "", UiKit.CssFont(12f), TextAnchor.MiddleRight, UiKit.TextMain);
            UiKit.TopRow(_depthText.rectTransform, y, UiKit.RowHeight(UiKit.CssFont(12f)), UiKit.Css(120f), UiKit.Css(44f));

            // ── ส่วนขยายของสัตว์ (สเปก user 7 ส.ค.): โมเดล 3D หมุนด้วยนิ้ว + ข้อความเลื่อนอ่าน ──
            // ทั้งสองชิ้นวางใต้บรรทัด meta และโผล่เฉพาะการ์ดกลางจอ (มีข้อมูล)
            var pv = new GameObject("Preview", typeof(UnityEngine.UI.RawImage));
            pv.transform.SetParent(rt, false);
            _previewImg = pv.GetComponent<UnityEngine.UI.RawImage>();
            var pvrt = _previewImg.rectTransform;
            pvrt.anchorMin = new Vector2(0f, 1f);
            pvrt.anchorMax = new Vector2(1f, 1f);
            pvrt.pivot = new Vector2(0.5f, 1f);
            _preview = SpeciesPreview.Ensure(_previewImg);

            var scGo = new GameObject("DescScroll", typeof(RectTransform), typeof(ScrollRect),
                                      typeof(UnityEngine.UI.RectMask2D));
            scGo.transform.SetParent(rt, false);
            _descScroll = scGo.GetComponent<ScrollRect>();
            var scrt = (RectTransform)scGo.transform;
            scrt.anchorMin = new Vector2(0f, 1f);
            scrt.anchorMax = new Vector2(1f, 1f);
            scrt.pivot = new Vector2(0.5f, 1f);
            _descText = UiKit.MakeText(scrt, "Desc", "", UiKit.CssFont(13f), TextAnchor.UpperLeft, UiKit.TextMain);
            var drt = _descText.rectTransform;
            drt.anchorMin = new Vector2(0f, 1f);
            drt.anchorMax = new Vector2(1f, 1f);
            drt.pivot = new Vector2(0.5f, 1f);
            _descText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _descText.verticalOverflow = VerticalWrapMode.Overflow;
            _descScroll.content = drt;
            _descScroll.horizontal = false;
            _descScroll.vertical = true;
            _descScroll.movementType = ScrollRect.MovementType.Clamped;
            _descScroll.scrollSensitivity = 20f;

            // ✕ icon rather than a text button: the web's dismiss affordances are icons, and a
            // "ปิด" label is wider than the pill can spare.
            _closeButton = UiKit.MakeIconButton(rt, "CardClose", "close", Hide, false, UiKit.Css(30f));
            Image closeBg = _closeButton.GetComponent<Image>();
            if (closeBg != null) closeBg.color = new Color(1f, 1f, 1f, 0.10f);
            UiKit.Anchor(_closeButton.GetComponent<RectTransform>(), new Vector2(1f, 0.5f),
                         new Vector2(UiKit.Css(30f), UiKit.Css(30f)),
                         new Vector2(-UiKit.Css(10f), 0f));

            // ✎ EDIT — the bridge between "I tapped something" and the editing tools. Without it
            // nothing in section I is reachable by a real user: the gizmo had no way to be told
            // what to select. Only shown when the server says this account may write to the map
            // (AppBoot.CanEditCurrent), so it never appears on somebody else's dive site.
            _editButton = UiKit.MakeIconButton(rt, "CardEdit", "move", BeginEditingOpenItem,
                                               true, UiKit.Css(30f));
            UiKit.Anchor(_editButton.GetComponent<RectTransform>(), new Vector2(1f, 0.5f),
                         new Vector2(UiKit.Css(30f), UiKit.Css(30f)),
                         new Vector2(-UiKit.Css(46f), 0f));
            _editButton.gameObject.SetActive(false);

            // The web's card carries a picture; so does this one now that the palette proved the
            // server renders one per asset (models/thumbs/<id>.png). Same cache as everywhere else.
            _thumb = UiKit.MakeRaw(rt, "Thumb", new Color(1f, 1f, 1f, 0f));
            RectTransform thrt = _thumb.rectTransform;
            thrt.anchorMin = new Vector2(0f, 0.5f);
            thrt.anchorMax = new Vector2(0f, 0.5f);
            thrt.pivot = new Vector2(0f, 0.5f);
            thrt.sizeDelta = new Vector2(UiKit.Css(44f), UiKit.Css(44f));
            thrt.anchoredPosition = new Vector2(UiKit.Css(10f), 0f);
            _thumb.gameObject.SetActive(false);

            _layer.gameObject.SetActive(false);
            StartCoroutine(LoadManifest());
        }

        private IEnumerator LoadManifest()
        {
            yield return AssetManifest.Load(m => _manifest = m,
                                            e => Debug.LogWarning("[UI] card manifest: " + e));
            Debug.Log($"[UI] card manifest modules={(_manifest != null ? _manifest.Count : 0)}");
        }

        // ── input ────────────────────────────────────────────────────────────────

        private void Update()
        {
            if (_layer == null) return;

            // Android back / Escape closes the card first, but only when no screen is
            // open — UiNav owns the key while its stack is non-empty.
            if (IsVisible && (_nav == null || _nav.Count == 0) && Input.GetKeyDown(KeyCode.Escape))
            {
                Hide();
                return;
            }

            if (_nav != null && _nav.Count > 0) { _tracking = false; return; }

            if (Input.touchCount > 0)
            {
                Touch t = Input.GetTouch(0);
                if (t.phase == TouchPhase.Began) BeginPress(t.position, t.fingerId);
                else if (t.phase == TouchPhase.Ended) EndPress(t.position);
                else if (t.phase == TouchPhase.Canceled) _tracking = false;
                return;
            }

            if (Input.GetMouseButtonDown(0)) BeginPress(Input.mousePosition, -1);
            else if (Input.GetMouseButtonUp(0)) EndPress(Input.mousePosition);
        }

        private void BeginPress(Vector2 pos, int fingerId)
        {
            _tracking = true;
            _downPos = pos;
            _downTime = Time.unscaledTime;
            _downOverUi = PointerOverUi(fingerId);
        }

        private void EndPress(Vector2 pos)
        {
            if (!_tracking) return;
            _tracking = false;

            if (_downOverUi) return;
            if (Time.unscaledTime - _downTime > TapMaxSeconds) return;
            if ((pos - _downPos).magnitude > TapMaxMovePixels) return;

            PickAt(pos);
        }

        /// <summary>
        /// On touch devices the fingerId overload is mandatory: the parameterless one
        /// only tracks the mouse and returns false on a phone, which would let every tap
        /// on the card itself fall through to the 3D pick.
        /// </summary>
        private static bool PointerOverUi(int fingerId)
        {
            EventSystem es = EventSystem.current;
            if (es == null) return false;
            return fingerId >= 0 ? es.IsPointerOverGameObject(fingerId) : es.IsPointerOverGameObject();
        }

        // ── picking ──────────────────────────────────────────────────────────────

        private void PickAt(Vector2 screenPos)
        {
            Camera cam = Camera.main;
            GameObject mapRoot = GameObject.Find("Map");
            if (cam == null || mapRoot == null) { Hide(); return; }

            Ray pinRay = cam.ScreenPointToRay(screenPos);

            // G1 — pins win over the scenery underneath them. A marker is small and deliberately
            // floats six units above whatever it is marking, so if the wreck it sits on took the
            // tap instead, the pin could never be opened at all.
            PinMarker pin = PinMarker.Pick(pinRay);
            if (pin != null)
            {
                Hide();
                PinSheet.Open(pin);
                return;
            }

            var byKey = new Dictionary<string, GameObject>();
            List<ItemPicker.Target> targets = CollectTargets(mapRoot, byKey);
            if (targets.Count == 0) { Hide(); return; }

            Ray ray = cam.ScreenPointToRay(screenPos);
            // พื้นทราย (collider เดียวในซีน) เป็นตัวบัง: แตะทราย = ทรายชนะทุกทรงกลมกลางน้ำ
            float maxT = float.PositiveInfinity;
            if (Physics.Raycast(ray, out RaycastHit ground, 5000f)) maxT = ground.distance + 0.5f;
            string key = ItemPicker.PickBest(ray.origin, ray.direction, targets, maxT);
            if (key == null || !byKey.TryGetValue(key, out GameObject hit)) { Hide(); return; }

            // 🔴 WO-N item 6 — in an editing context the tap belongs to the GIZMO, not to a read
            // card. The web has no card at all: pointerup → select(rootOf(hit)) (builder.html
            // :3060-3096). On 9005 an author who tapped a rock to move it got an animal fact
            // sheet instead, and had to find the ✎ inside it to reach the tools — one extra step
            // the reference product does not have, on the interaction an author performs most.
            //
            // The rule lives in ModeRules.SelectsOnTap so this and the card can never disagree:
            // whoever can act on the tap owns it. A viewer, or anyone in a tour, still gets the
            // card, because for them a tap has nothing else to do.
            // It also fixes something nobody had reported because it looked like nothing at all:
            // ShowFor bails out for every non-marine object (the 7 Aug rule — only animals have a
            // story to tell), so tapping a ROCK used to do literally nothing. Rocks were only
            // selectable through the 📋 list. Now every object answers a tap.
            var boot = FindFirstObjectByType<AppBoot>();
            if (ModeRules.SelectsOnTap(ModeManager.Current, boot != null && boot.CanEditCurrent) &&
                ItemPicker.ParseItemName(hit.name, out string tapId, out _) &&
                !string.IsNullOrEmpty(tapId))
            {
                Hide();
                GizmoController.Select(tapId);
                Debug.Log("[UI] tap → select " + tapId);
                return;
            }

            ShowFor(hit, mapRoot);
        }

        /// <summary>
        /// World-space AABB per <c>Item_*</c> child. Labels are excluded (they are
        /// camera-facing TextMeshes whose bounds would swamp small items), and items
        /// with no renderer at all fall back to a sphere.
        /// </summary>
        private List<ItemPicker.Target> CollectTargets(GameObject mapRoot,
                                                        Dictionary<string, GameObject> byKey)
        {
            var list = new List<ItemPicker.Target>();
            if (mapRoot == null) return list;

            Transform root = mapRoot.transform;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                string key = child.name;
                if (!ItemPicker.IsItemName(key)) continue;
                if (byKey.ContainsKey(key)) continue;

                // 🔴 การ์ดเปิดเฉพาะสัตว์ (คำสั่ง user) — ดังนั้นของที่ไม่ใช่สัตว์ต้อง "ไม่รับ
                // คลิก" ตั้งแต่ชั้นเลือกเป้า ไม่ใช่แค่เงียบทีหลัง: กล่องคลิกของเรือ Chang
                // ใหญ่คลุมทั้งบริเวณ พอปลาว่ายใกล้เรือ ray โดนเรือชนะ -> เงียบ = user มองว่า
                // "คลิกฝูงไม่ได้" (รายงานรอบ 3). ตัด decor ออก = ปลาไม่มีวันถูกเรือบัง.
                ItemPicker.ParseItemName(key, out _, out string aidEarly);
                AssetManifest.Module modEarly = _manifest != null ? _manifest.Get(aidEarly) : null;
                if (DiveMap.Core.MarineRouting.For(aidEarly, modEarly?.Kind)
                    == DiveMap.Core.MarineRoute.None) continue;

                bool has = false;
                Bounds b = default;
                Renderer[] renderers = child.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer r in renderers)
                {
                    if (r == null || r.transform.name == "Label") continue;
                    if (!has) { b = r.bounds; has = true; }
                    else b.Encapsulate(r.bounds);
                }

                if (has)
                {
                    list.Add(new ItemPicker.Target(key, b.min, b.max));
                }
                else
                {
                    ItemPicker.ParseItemName(key, out _, out string assetId);
                    // ฝูงปลา: ใช้กล่องรอบตำแหน่งปลาจริง ณ ตอนนี้ (FishSchoolSystem) ไม่ใช่
                    // ทรงกลมสูตรรอบ pivot — สามรูปหลักฐานของ user (แตะทรายได้บาราคูด้า /
                    // แตะบาราคูด้าได้ scad / แตะฉลามวาฬได้ scad) คือทรงกลมใหญ่เกินจริงทั้งนั้น
                    if (TryLiveSchoolBounds(assetId, child.position, out Bounds live))
                    {
                        // 🔴 ห้าม continue ตรงนี้ — เคยข้าม byKey[key] ด้านล่าง = Pick เจอ key
                        // แต่หาวัตถุไม่เจอ = การ์ดเงียบทั้งที่คลิกโดน (บั๊ก "คลิกฝูงไม่ได้เลย")
                        list.Add(new ItemPicker.Target(key, live.min, live.max));
                    }
                    else
                    {
                        float radius = FallbackRadius(assetId, child.localScale);
                        list.Add(ItemPicker.Target.Sphere(key, child.position, radius));
                    }
                }

                byKey[key] = child.gameObject;
            }
            return list;
        }

        /// <summary>
        /// Pick radius for a renderer-less item. Schools/pods are instanced swarms, so we
        /// use the swarm's OWN formation radius from MarineMath — the same number
        /// SceneBuilder.MakeSchoolReg feeds to FishSchoolSystem — rather than inventing
        /// a size. Anything else falls back to its item scale.
        /// </summary>

        /// <summary>
        /// กล่องปลาจริงของฝูงชนิดนี้ที่ "ใกล้ pivot ของ item นี้ที่สุด" — แมพหนึ่งมีฝูงชนิด
        /// เดียวกันหลายจุด (Chang: scad ×7) เลยจับคู่ด้วยระยะจากบ้านของมัน.
        /// </summary>
        private static bool TryLiveSchoolBounds(string assetId, Vector3 pivot, out Bounds live)
        {
            live = default;
            if (string.IsNullOrEmpty(assetId)) return false;
            string a = assetId.ToLowerInvariant();
            if (!a.StartsWith("school:") && !a.StartsWith("pod:")) return false;
            var sys = UnityEngine.Object.FindFirstObjectByType<DiveMap.Runtime.Marine.FishSchoolSystem>();
            if (sys == null) return false;
            float bestD = float.PositiveInfinity;
            bool found = false;
            for (int i = 0; i < sys.SchoolCount; i++)
            {
                if (!sys.TryGetSchoolBounds(i, out string sp, out Bounds b)) continue;
                if (!string.Equals(sp, assetId, System.StringComparison.OrdinalIgnoreCase)) continue;
                float d = (b.center - pivot).sqrMagnitude;
                if (d < bestD) { bestD = d; live = b; found = true; }
            }
            return found;
        }

        private static float FallbackRadius(string assetId, Vector3 localScale)
        {
            float scale = Mathf.Max(0.01f, Mathf.Max(localScale.x, Mathf.Max(localScale.y, localScale.z)));

            string a = string.IsNullOrEmpty(assetId) ? "" : assetId.ToLowerInvariant();
            if (a.StartsWith("school:") || a.StartsWith("pod:"))
            {
                MarineMath.SchoolGeometry g = MarineMath.SchoolGeometryFor(assetId, localScale.x);
                return Mathf.Max(2f, (float)g.RadiusWorld);
            }
            return Mathf.Max(2f, scale);
        }

        // ── card ─────────────────────────────────────────────────────────────────

        /// <summary>QC / deep-link entry point: show the card for the first item with this assetId.</summary>
        public bool ShowCardFor(string assetId)
        {
            if (string.IsNullOrEmpty(assetId)) return false;
            GameObject mapRoot = GameObject.Find("Map");
            if (mapRoot == null) { Debug.LogWarning("[UI] ShowCardFor: no Map root"); return false; }

            Transform root = mapRoot.transform;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (!ItemPicker.ParseItemName(child.name, out _, out string aid)) continue;
                if (aid != assetId) continue;
                ShowFor(child.gameObject, mapRoot);
                return true;
            }

            Debug.LogWarning("[UI] ShowCardFor: no item with assetId=" + assetId);
            return false;
        }

        private void ShowFor(GameObject item, GameObject mapRoot)
        {
            if (item == null || _layer == null) return;
            if (!ItemPicker.ParseItemName(item.name, out string id, out string assetId)) return;

            AssetManifest.Module module = _manifest != null ? _manifest.Get(assetId) : null;

            // 🔴 คำสั่ง user 7 ส.ค.: popup ขึ้นเฉพาะสัตว์ทะเลเท่านั้น — เรือจม รูปปั้น ซุ้ม
            // ปะการัง หิน แตะแล้วต้องเงียบสนิท. เกณฑ์ = MarineRouting ตัวเดียวกับที่ตัดสินว่า
            // อะไรได้สมองว่ายน้ำ (หนึ่งแหล่งความจริง — ของที่ว่ายได้คือของที่มีเรื่องให้เล่า).
            if (DiveMap.Core.MarineRouting.For(assetId, module?.Kind)
                == DiveMap.Core.MarineRoute.None)
            {
                Hide();
                return;
            }
            _openDesc = SpeciesInfo.Get(assetId);
            _openAssetId = assetId;

            string label = null;
            if (_labels != null && id != null) _labels.TryGetValue(id, out label);

            _openKey = item.name;
            _openName = !string.IsNullOrWhiteSpace(label) ? label
                      : (!string.IsNullOrWhiteSpace(module?.Name) ? module.Name : assetId);
            _openKindKey = ItemPicker.KindLabel(module?.Kind, assetId);
            _openDepth = ItemPicker.DepthMetres(WaterLevel(mapRoot), item.transform.position.y);

            _layer.gameObject.SetActive(true);
            _layer.SetAsLastSibling();
            Render();

            Debug.Log($"[UI] card name={_openName} kind={_openKindKey} depth={_openDepth:F1} " +
                      $"asset={assetId} id={id}");

            // Cover image for the tapped object, if the server rendered one.
            if (_thumb != null)
            {
                _thumb.gameObject.SetActive(false);
                _thumb.texture = null;
                _thumb.color = new Color(1f, 1f, 1f, 0f);
                string thumbUrl = Palette.ThumbUrl(assetId, module != null, MapApiClient.DefaultBaseUrl);
                ThumbnailCache cache = UiShell.Instance != null ? UiShell.Instance.Thumbs : null;
                if (cache != null && !string.IsNullOrEmpty(thumbUrl))
                {
                    RawImage target = _thumb;
                    cache.Request(thumbUrl, tex =>
                    {
                        if (target == null || tex == null) return;
                        target.texture = tex;
                        target.color = Color.white;
                        target.gameObject.SetActive(true);
                    });
                }
            }

            // Editable maps get the ✎ button; everyone else just reads the card.
            var boot = FindFirstObjectByType<AppBoot>();
            bool canEdit = boot != null && boot.CanEditCurrent;
            if (_editButton != null) _editButton.gameObject.SetActive(canEdit);
            _openId = id;

            EnsureLabels(mapRoot);
        }

        /// <summary>Hand the tapped item to the editing tools and get out of the way.</summary>
        private void BeginEditingOpenItem()
        {
            if (string.IsNullOrEmpty(_openId)) return;
            string id = _openId;
            Hide();
            GizmoController.Select(id);
            Debug.Log("[UI] card → edit " + id);
        }

        /// <summary>Re-render the open card in the current language (called after a language switch).</summary>
        public void Render()
        {
            if (_layer == null || _openKey == null) return;

            if (_nameText != null) _nameText.text = UiStrings.Tr(_openName);
            if (_kindText != null) _kindText.text = UiStrings.Tr(_openKindKey);
            if (_descText != null) _descText.text = _openDesc ?? "";
            // user 7 ส.ค.: เอารูปสัตว์เล็กด้านซ้ายออก — การ์ดมีโมเดล 3D จริงแล้ว รูปนิ่งซ้ำซ้อน
            if (_thumb != null) _thumb.gameObject.SetActive(false);
            if (_cardRt != null)
            {
                // มีเรื่องเล่า = การ์ดกลางจอ: หัว + โมเดล 3D หมุนได้ + ข้อความเลื่อนอ่าน
                // ไม่มี = แผ่นล่างชื่อ/ชนิด/ความลึกแบบเดิม (ไม่มีการแต่งข้อมูลเพิ่มเด็ดขาด)
                bool hasDesc = !string.IsNullOrEmpty(_openDesc);
                float w = _cardRt.sizeDelta.x;
                float pvH = hasDesc ? Mathf.Min(w * 0.62f, UiKit.Css(210f)) : 0f;
                float scH = hasDesc ? UiKit.Css(150f) : 0f;
                if (_previewImg != null)
                {
                    _previewImg.gameObject.SetActive(hasDesc);
                    var prt = _previewImg.rectTransform;
                    prt.sizeDelta = new Vector2(0f, pvH);
                    prt.anchoredPosition = new Vector2(0f, -CardHeight);
                }
                if (_descScroll != null)
                {
                    _descScroll.gameObject.SetActive(hasDesc);
                    var srt = (RectTransform)_descScroll.transform;
                    srt.sizeDelta = new Vector2(-UiKit.Css(28f), scH);
                    srt.anchoredPosition = new Vector2(0f, -CardHeight - pvH - UiKit.Css(8f));
                    Canvas.ForceUpdateCanvases();
                    var drt2 = _descText.rectTransform;
                    drt2.sizeDelta = new Vector2(0f, Mathf.Max(scH, _descText.preferredHeight + UiKit.Css(8f)));
                    _descScroll.verticalNormalizedPosition = 1f;
                }
                if (hasDesc)
                {
                    _cardRt.anchorMin = _cardRt.anchorMax = new Vector2(0.5f, 0.5f);
                    _cardRt.pivot = new Vector2(0.5f, 0.5f);
                    _cardRt.anchoredPosition = Vector2.zero;
                    if (_preview != null && _manifest != null)
                        _preview.Show(_manifest.ResolveUrl(_openAssetId), _openAssetId);
                }
                else
                {
                    _cardRt.anchorMin = _cardRt.anchorMax = new Vector2(0.5f, 0f);
                    _cardRt.pivot = new Vector2(0.5f, 0f);
                    _cardRt.anchoredPosition = new Vector2(0f, UiKit.Css(22f));
                }
                _cardRt.sizeDelta = new Vector2(w, CardHeight + pvH + scH + (hasDesc ? UiKit.Css(24f) : 0f));
            }
            if (_depthText != null)
                _depthText.text = $"{UiStrings.Tr("ความลึก")} {_openDepth:F1} {UiStrings.Tr("ม.")}";

            if (_closeButton != null)
            {
                Text t = _closeButton.GetComponentInChildren<Text>();
                if (t != null) t.text = UiStrings.Tr("ปิด");
            }
        }

        public void Hide()
        {
            _preview?.Clear();
            if (_layer == null) return;
            if (_layer.gameObject.activeSelf) Debug.Log("[UI] card closed");
            _layer.gameObject.SetActive(false);
            _openKey = null;
        }

        /// <summary>
        /// Water level of the built map. SceneBuilder places the "Water" disc exactly at
        /// <c>env.waterLevel</c> (local Y under the Map root), so the value is readable
        /// from the scene graph without a second API round-trip.
        /// </summary>
        private static float WaterLevel(GameObject mapRoot)
        {
            if (mapRoot == null) return DefaultWaterLevel;
            Transform water = mapRoot.transform.Find("Water");
            return water != null ? water.localPosition.y : DefaultWaterLevel;
        }

        // ── item labels (item.lb) ────────────────────────────────────────────────

        private void EnsureLabels(GameObject mapRoot)
        {
            if (_labelsBusy) return;
            if (_labels != null && _labelsMapRoot == mapRoot) return;

            _labelsMapRoot = mapRoot;
            _labelsBusy = true;
            StartCoroutine(FetchLabels());
        }

        private IEnumerator FetchLabels()
        {
            string shortId = PlayerPrefs.GetString("shortId", "");
            if (string.IsNullOrEmpty(shortId))
            {
                AppBoot boot = Object.FindFirstObjectByType<AppBoot>();
                shortId = boot != null && !string.IsNullOrEmpty(boot.defaultShortId)
                        ? boot.defaultShortId
                        : MapApiClient.DefaultShortId;
            }

            SceneData scene = null;
            string err = null;
            yield return MapApiClient.Fetch(shortId, s => scene = s, e => err = e);

            _labelsBusy = false;
            if (scene == null)
            {
                // Non-fatal: the card simply keeps showing the manifest name.
                Debug.LogWarning("[UI] card labels unavailable: " + err);
                yield break;
            }

            var map = new Dictionary<string, string>();
            foreach (SceneItem item in scene.Items())
            {
                if (string.IsNullOrEmpty(item.Id) || string.IsNullOrWhiteSpace(item.Label)) continue;
                map[item.Id] = item.Label;
            }
            _labels = map;
            Debug.Log($"[UI] card labels={map.Count} (map {shortId})");

            // A card opened before the labels arrived should adopt its custom name now.
            if (IsVisible && _openKey != null &&
                ItemPicker.ParseItemName(_openKey, out string openId, out _) &&
                map.TryGetValue(openId, out string lb) && !string.IsNullOrWhiteSpace(lb))
            {
                _openName = lb;
                Render();
            }
        }
    }
}
