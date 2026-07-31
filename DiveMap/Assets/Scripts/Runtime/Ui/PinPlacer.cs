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
    /// 📍 Drop a pin, and attach a photo to it — the web's <c>togglePinMode</c> / <c>placePin</c>
    /// (:2876) and <c>addMedia</c>.
    ///
    /// A pin sits 6 units above the tapped point (<c>s.position.set(p.x, p.y+6, p.z)</c>), so it
    /// floats clear of the seabed instead of being half-buried in it.
    ///
    /// Media goes through <c>POST /api/dive-sites/media</c> and comes back as a CDN URL. The
    /// server checks the file's MAGIC BYTES rather than the declared content type (a hardening
    /// pass the web shipped after an audit), so anything this sends must be a real image — which
    /// is why the app uploads a PNG it encoded itself rather than passing a file through.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PinPlacer : MonoBehaviour
    {
        private static PinPlacer _active;

        /// <summary>QC surface.</summary>
        public static bool IsPlacing => _active != null;

        private Text _hint;

        public static void Start()
        {
            if (_active != null) { Cancel(); return; }   // the web's toggle
            RectTransform host = UiShell.Instance != null ? UiShell.Instance.OverlayRoot : null;
            if (host == null) return;

            var go = new GameObject("PinPlacer");
            go.transform.SetParent(host, false);
            var rt = go.AddComponent<RectTransform>();
            UiKit.Stretch(rt);
            _active = go.AddComponent<PinPlacer>();
            _active.BuildHint(rt);
            Debug.Log("[Pin] place mode on");
        }

        public static void Cancel()
        {
            if (_active == null) return;
            Destroy(_active.gameObject);
            _active = null;
            Debug.Log("[Pin] place mode off");
        }

        private void OnDestroy()
        {
            if (_active == this) _active = null;
        }

        private void BuildHint(RectTransform root)
        {
            // The web shows #hint as a pill at top 72 with a "เสร็จ" button; same idea.
            Image pill = UiKit.MakeRounded(root, "Hint", new Color(0.027f, 0.102f, 0.165f, 0.92f), 22f);
            RectTransform rt = pill.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(UiKit.Css(340f), UiKit.Css(44f));
            rt.anchoredPosition = new Vector2(0f, -UiKit.Css(72f));

            _hint = UiKit.MakeLine(pill.transform, "Text", UiStrings.Tr("แตะบนแผนที่เพื่อปักหมุด"),
                                   UiKit.CssFont(13f), TextAnchor.MiddleLeft, UiKit.TextMain);
            RectTransform trt = _hint.rectTransform;
            trt.anchorMin = new Vector2(0f, 0.5f);
            trt.anchorMax = new Vector2(1f, 0.5f);
            trt.pivot = new Vector2(0.5f, 0.5f);
            trt.sizeDelta = new Vector2(-UiKit.Css(90f), UiKit.RowHeight(UiKit.CssFont(13f)));
            trt.anchoredPosition = new Vector2(-UiKit.Css(30f), 0f);

            Button done = UiKit.MakeButton(pill.transform, "Done", UiStrings.Tr("เสร็จ"),
                                           UiKit.CssFont(12f), new Color(1f, 1f, 1f, 0.14f),
                                           UiKit.TextMain, Cancel);
            Image bg = done.GetComponent<Image>();
            if (bg != null) { bg.sprite = UiKit.RoundedSprite(16f); bg.type = Image.Type.Sliced; }
            RectTransform drt = done.GetComponent<RectTransform>();
            drt.anchorMin = new Vector2(1f, 0.5f);
            drt.anchorMax = new Vector2(1f, 0.5f);
            drt.pivot = new Vector2(1f, 0.5f);
            drt.sizeDelta = new Vector2(UiKit.Css(64f), UiKit.Css(30f));
            drt.anchoredPosition = new Vector2(-UiKit.Css(8f), 0f);
        }

        private void Update()
        {
            if (_active != this) return;
            if (ModeManager.Current != AppMode.View) { Cancel(); return; }

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
            if (!Physics.Raycast(cam.ScreenPointToRay(pos), out RaycastHit hit, 5000f)) return;

            Place(hit.point);
        }

        /// <summary>QC only — drop a pin at a world point with no touch hardware.</summary>
        public static string QcPlace(Vector3 world)
        {
            PinPlacer p = _active;
            return p != null ? p.Place(world) : PlaceAt(world);
        }

        private string Place(Vector3 world) => PlaceAt(world);

        /// <summary>
        /// Add the pin to the scene JSON and rebuild the markers. The web floats it 6 units up;
        /// so does this, or the marker sinks into whatever was tapped.
        /// </summary>
        private static string PlaceAt(Vector3 world)
        {
            var boot = Object.FindFirstObjectByType<AppBoot>();
            SceneData scene = boot != null ? boot.CurrentScene : null;
            if (scene == null) return null;

            if (!(scene.Root["pins"] is JArray pins))
            {
                pins = new JArray();
                scene.Root["pins"] = pins;
            }

            string id = "pin" + System.DateTime.UtcNow.Ticks.ToString("x");
            double[] web = WebCoord.PositionToUnity(new[] { (double)world.x, world.y + 6.0, (double)world.z });
            // PositionToUnity is its own inverse on Z, so applying it again converts back.
            pins.Add(new JObject
            {
                ["id"] = id,
                ["p"] = new JArray(web[0], web[1], web[2]),
                ["media"] = new JArray(),
            });

            MapEditor.MarkSculpted();   // pins live outside `items`; this is the "map changed" path
            RefreshMarkers();
            Debug.Log($"[Pin] placed {id} at ({world.x:F0},{world.y + 6f:F0},{world.z:F0}) total={pins.Count}");
            return id;
        }

        private static void RefreshMarkers()
        {
            var boot = Object.FindFirstObjectByType<AppBoot>();
            GameObject mapRoot = GameObject.Find("Map");
            if (boot == null || mapRoot == null || boot.CurrentScene == null) return;
            PinMarker.BuildAll(boot.CurrentScene, mapRoot.transform);
        }

        // ── media ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Upload an image and attach it to a pin. <paramref name="png"/> must be real PNG bytes:
        /// the route sniffs magic bytes and refuses anything whose header does not match, no
        /// matter what content type is declared.
        /// </summary>
        public static IEnumerator AddMedia(string pinId, byte[] png, System.Action<bool> onDone)
        {
            if (string.IsNullOrEmpty(pinId) || png == null || png.Length == 0)
            {
                onDone?.Invoke(false);
                yield break;
            }

            var form = new List<IMultipartFormSection>
            {
                new MultipartFormFileSection("file", png, "pin.png", "image/png"),
            };

            string url = MapApiClient.DefaultBaseUrl + "/api/dive-sites/media";
            string mediaUrl = null;

            using (UnityWebRequest req = UnityWebRequest.Post(url, form))
            {
                req.timeout = 40;
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[Pin] upload failed ({req.responseCode}) {req.error}");
                    onDone?.Invoke(false);
                    yield break;
                }
                try
                {
                    JObject o = JObject.Parse(req.downloadHandler.text);
                    mediaUrl = (string)o["url"];
                }
                catch { /* handled below */ }
            }

            if (string.IsNullOrEmpty(mediaUrl)) { onDone?.Invoke(false); yield break; }

            var boot = Object.FindFirstObjectByType<AppBoot>();
            SceneData scene = boot != null ? boot.CurrentScene : null;
            if (scene == null || !(scene.Root["pins"] is JArray pins)) { onDone?.Invoke(false); yield break; }

            foreach (JToken t in pins)
            {
                if (!(t is JObject o) || (string)o["id"] != pinId) continue;
                if (!(o["media"] is JArray media))
                {
                    media = new JArray();
                    o["media"] = media;
                }
                // The server also filters non-CDN urls on save (sanitizePins), so the shape here
                // has to match what it expects: an object with a url, not a bare string.
                media.Add(new JObject { ["type"] = "image", ["url"] = mediaUrl });
                break;
            }

            MapEditor.MarkSculpted();
            RefreshMarkers();
            Debug.Log($"[Pin] media added to {pinId}: {mediaUrl}");
            onDone?.Invoke(true);
        }

        /// <summary>Remove a pin entirely.</summary>
        public static bool Remove(string pinId)
        {
            var boot = Object.FindFirstObjectByType<AppBoot>();
            SceneData scene = boot != null ? boot.CurrentScene : null;
            if (scene == null || !(scene.Root["pins"] is JArray pins)) return false;

            for (int i = 0; i < pins.Count; i++)
                if (pins[i] is JObject o && (string)o["id"] == pinId)
                {
                    pins.RemoveAt(i);
                    MapEditor.MarkSculpted();
                    RefreshMarkers();
                    Debug.Log("[Pin] removed " + pinId);
                    return true;
                }
            return false;
        }
    }
}
