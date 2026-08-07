using System.Threading.Tasks;
using GLTFast;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// The little live 3D animal inside the info card — the user's spec, verbatim: "รูปภาพก็ควร
    /// ขึ้นเป็นรูปสัตว์หนึ่งตัวมาแล้วก็สามารถใช้นิ้วลากหมุนได้ 360 องศา".
    ///
    /// One stage, far below every map (y −4000 — the deepest seabed sits near 0 and the QC stage
    /// uses a similar trick), one orthographic-ish camera rendering that stage into a
    /// RenderTexture, and one RawImage in the card showing it. Dragging the RawImage spins the
    /// model's yaw; nothing else in the scene can see the stage because nothing else looks there.
    /// The GLB comes down the exact road every model takes (SceneBuilder.LoadForQc: cache,
    /// TameMetal, clip strip) so the preview shows what the reef shows.
    /// </summary>
    public sealed class SpeciesPreview : MonoBehaviour, IDragHandler
    {
        private static SpeciesPreview _instance;

        private const float StageY = -4000f;
        private const int Rt = 512;
        private const float DegPerPx = 0.5f;

        private Camera _cam;
        private RenderTexture _rt;
        private Transform _stage;      // parent of the loaded model
        private GltfImport _import;
        private string _loadedUrl;
        private float _yaw;

        /// <summary>Attach to (or reuse on) the RawImage that should display the preview.</summary>
        public static SpeciesPreview Ensure(RawImage view)
        {
            if (view == null) return null;
            SpeciesPreview p = view.GetComponent<SpeciesPreview>();
            if (p == null) p = view.gameObject.AddComponent<SpeciesPreview>();
            _instance = p;
            p.Wire(view);
            return p;
        }

        private void Wire(RawImage view)
        {
            if (_rt == null)
            {
                _rt = new RenderTexture(Rt, Rt, 16, RenderTextureFormat.ARGB32);
                _rt.Create();
            }
            view.texture = _rt;

            if (_stage == null)
            {
                var root = new GameObject("SpeciesStage");
                DontDestroyOnLoad(root);
                root.transform.position = new Vector3(0f, StageY, 0f);
                _stage = new GameObject("Model").transform;
                _stage.SetParent(root.transform, false);

                var camGo = new GameObject("SpeciesCam");
                camGo.transform.SetParent(root.transform, false);
                _cam = camGo.AddComponent<Camera>();
                _cam.clearFlags = CameraClearFlags.SolidColor;
                _cam.backgroundColor = new Color(0.05f, 0.12f, 0.20f, 1f);
                _cam.targetTexture = _rt;
                _cam.nearClipPlane = 0.05f;
                _cam.farClipPlane = 200f;
                _cam.enabled = true;

                // Its own key light, aimed with the camera — the stage is far from every map
                // light and the card must not depend on whichever map is loaded.
                var lightGo = new GameObject("SpeciesLight");
                lightGo.transform.SetParent(camGo.transform, false);
                var l = lightGo.AddComponent<Light>();
                l.type = LightType.Directional;
                l.intensity = 1.35f;
                l.transform.rotation = Quaternion.Euler(28f, -20f, 0f);
            }
        }

        /// <summary>Load and frame this model (any GLB URL the manifest resolves).</summary>
        public async void Show(string url, string assetId)
        {
            if (string.IsNullOrEmpty(url) || _stage == null) return;
            if (_loadedUrl == url) { gameObject.SetActive(true); return; }
            Clear();
            _loadedUrl = url;

            GltfImport import = await SceneBuilder.LoadForQc(url, assetId, _stage);
            if (import == null || this == null || _stage == null || _loadedUrl != url) return;
            _import = import;

            // Frame: model centred by LoadForQc; put the camera at a distance that fits it.
            var rends = _stage.GetComponentsInChildren<Renderer>(true);
            if (rends.Length == 0) return;
            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            float radius = Mathf.Max(0.5f, b.extents.magnitude);
            _cam.transform.position = _stage.parent.position + new Vector3(0f, radius * 0.15f, radius * 2.3f);
            _cam.transform.LookAt(_stage.parent.position);
            _yaw = 0f;
            _stage.localRotation = Quaternion.identity;
        }

        public void OnDrag(PointerEventData e)
        {
            if (_stage == null) return;
            _yaw -= e.delta.x * DegPerPx;
            // แนวตั้งเล็กน้อยพอให้ "หมุนดูได้รอบ" โดยไม่หลุดเฟรม
            _stage.localRotation = Quaternion.Euler(0f, _yaw, 0f);
        }

        /// <summary>Drop the current model (called when the card closes or changes subject).</summary>
        public void Clear()
        {
            _loadedUrl = null;
            if (_stage != null)
                for (int i = _stage.childCount - 1; i >= 0; i--)
                    Destroy(_stage.GetChild(i).gameObject);
            _import?.Dispose();
            _import = null;
        }

        private void OnDestroy()
        {
            Clear();
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
            if (_instance == this) _instance = null;
        }
    }
}
