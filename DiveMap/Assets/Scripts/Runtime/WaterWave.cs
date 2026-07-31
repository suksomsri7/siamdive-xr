using UnityEngine;

namespace DiveMap.Runtime
{
    /// <summary>
    /// B2 — the water surface actually moves.
    ///
    /// Until now the disc had a scrolling texture and nothing else, which reads as still water
    /// with a pattern sliding over it. This displaces the mesh's own vertices, so the surface has
    /// shape: seen from below (the angle a diver spends the whole dive at) the light through it
    /// breaks up, and the horizon line stops being a perfectly straight edge.
    ///
    /// Two sine waves crossing at an angle rather than one: a single wave train looks like a
    /// corrugated roof from above, and the give-away is that every crest is parallel.
    ///
    /// The mesh is written once per frame from a cached base copy. Reading back the displaced
    /// vertices and re-displacing them would compound the offset and the sea would climb away.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WaterWave : MonoBehaviour
    {
        /// <summary>Peak-to-trough, in world units. The web's surf is subtle; so is this.</summary>
        public float amplitude = 1.6f;
        public float speed = 0.6f;
        /// <summary>World units per wavelength.</summary>
        public float length = 110f;

        private Mesh _mesh;
        private Vector3[] _base;
        private Vector3[] _work;

        private void Start()
        {
            var mf = GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) { enabled = false; return; }

            // A copy: sharedMesh is the asset every other water disc uses, and writing to it
            // would deform them all — and survive a scene reload, because it is an asset.
            _mesh = Instantiate(mf.sharedMesh);
            _mesh.name = "WaterWave";
            mf.sharedMesh = _mesh;

            _base = _mesh.vertices;
            _work = new Vector3[_base.Length];
            _mesh.MarkDynamic();
        }

        private void Update()
        {
            if (_mesh == null || _base == null) return;

            float t = Time.time * speed;
            float k = Mathf.PI * 2f / Mathf.Max(1f, length);

            for (int i = 0; i < _base.Length; i++)
            {
                Vector3 p = _base[i];
                // Two trains, ~50° apart, different periods — no repeating crest line.
                float h = Mathf.Sin(p.x * k + t) * 0.6f
                        + Mathf.Sin((p.x * 0.64f + p.z * 0.77f) * k * 1.37f + t * 1.31f) * 0.4f;
                p.y = _base[i].y + h * amplitude;
                _work[i] = p;
            }

            _mesh.vertices = _work;
            _mesh.RecalculateNormals();   // the light on the surface is the point of the exercise
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }
    }
}
