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

        private const float CardHeight = 380f;

        private RectTransform _layer;
        private Text _nameText;
        private Text _kindText;
        private Text _depthText;
        private Button _closeButton;
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

            // Rounded top corners + a grip, like the web's sheet — a square slab pushed up from
            // the bottom edge is the one thing here that never looked like the same product.
            Image card = UiKit.MakeRounded(_layer, "Card", UiKit.PanelBg, 24f);
            RectTransform rt = card.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(0f, CardHeight);
            rt.anchoredPosition = Vector2.zero;

            Image grip = UiKit.MakeRounded(rt, "Grip", new Color(1f, 1f, 1f, 0.28f), 3f);
            grip.raycastTarget = false;
            RectTransform grt = grip.rectTransform;
            grt.anchorMin = new Vector2(0.5f, 1f);
            grt.anchorMax = new Vector2(0.5f, 1f);
            grt.pivot = new Vector2(0.5f, 1f);
            grt.sizeDelta = new Vector2(UiKit.Css(42f), UiKit.Css(4f));
            grt.anchoredPosition = new Vector2(0f, -UiKit.Css(10f));

            // Row heights: NotoSansThai renders one line at ~1.51 × fontSize (two levels
            // of tone marks above the base glyph plus a below-vowel). Legacy Text does not
            // clip a line that is too tall for its rect — it DROPS it — so every row here
            // is sized well above fontSize × 1.51.
            _nameText = UiKit.MakeText(rt, "Name", "", 46, TextAnchor.MiddleLeft, UiKit.Teal);
            UiKit.TopRow(_nameText.rectTransform, 24f, 80f, 36f, 200f);

            _kindText = UiKit.MakeText(rt, "Kind", "", 34, TextAnchor.MiddleLeft, UiKit.TextDim);
            UiKit.TopRow(_kindText.rectTransform, 116f, 60f, 36f, 200f);

            _depthText = UiKit.MakeText(rt, "Depth", "", 40, TextAnchor.MiddleLeft, UiKit.TextMain);
            UiKit.TopRow(_depthText.rectTransform, 190f, 70f, 36f, 200f);

            _closeButton = UiKit.MakeButton(rt, "CardClose", UiStrings.Tr("ปิด"), 32,
                                            UiKit.TealDim, UiKit.TextMain, Hide);
            UiKit.Anchor(_closeButton.GetComponent<RectTransform>(), new Vector2(1f, 1f),
                         new Vector2(180f, 80f), new Vector2(-24f, -24f));

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

            var byKey = new Dictionary<string, GameObject>();
            List<ItemPicker.Target> targets = CollectTargets(mapRoot, byKey);
            if (targets.Count == 0) { Hide(); return; }

            Ray ray = cam.ScreenPointToRay(screenPos);
            string key = ItemPicker.Pick(ray.origin, ray.direction, targets);
            if (key == null || !byKey.TryGetValue(key, out GameObject hit)) { Hide(); return; }

            ShowFor(hit, mapRoot);
        }

        /// <summary>
        /// World-space AABB per <c>Item_*</c> child. Labels are excluded (they are
        /// camera-facing TextMeshes whose bounds would swamp small items), and items
        /// with no renderer at all fall back to a sphere.
        /// </summary>
        private static List<ItemPicker.Target> CollectTargets(GameObject mapRoot,
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
                    float radius = FallbackRadius(assetId, child.localScale);
                    list.Add(ItemPicker.Target.Sphere(key, child.position, radius));
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

            EnsureLabels(mapRoot);
        }

        /// <summary>Re-render the open card in the current language (called after a language switch).</summary>
        public void Render()
        {
            if (_layer == null || _openKey == null) return;

            if (_nameText != null) _nameText.text = UiStrings.Tr(_openName);
            if (_kindText != null) _kindText.text = UiStrings.Tr(_openKindKey);
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
