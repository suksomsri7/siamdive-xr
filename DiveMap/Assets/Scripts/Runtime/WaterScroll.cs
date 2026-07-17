using UnityEngine;

namespace DiveMap.Runtime
{
    /// <summary>
    /// Slowly scrolls a material's main texture UV to fake gentle water movement /
    /// caustics on the transparent water disc. Preliminary (WO-XR-01) — the full
    /// caustics + god-ray pass is WO-XR-04.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WaterScroll : MonoBehaviour
    {
        public Vector2 speed = new Vector2(0.015f, 0.01f);

        private Material _mat;
        private Vector2 _offset;

        private void Awake()
        {
            var r = GetComponent<Renderer>();
            if (r != null) _mat = r.material; // instance so we do not touch shared asset
        }

        private void Update()
        {
            if (_mat == null) return;
            _offset += speed * Time.deltaTime;
            _mat.mainTextureOffset = _offset;
        }
    }
}
