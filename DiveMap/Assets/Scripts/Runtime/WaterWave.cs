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
    /// The shape itself is <see cref="DiveMap.Core.WaterWaveMath"/> — the web's three-term
    /// formula, ported number for number. This component's job is only to move a mesh with it.
    ///
    /// ⚠️ This file previously carried its own invented constants (two terms, total amplitude
    /// 1.6 against the web's 7). It looked right in isolation — there WAS a moving surface — and
    /// it was four times too flat. Numbers that describe how something looks have to come from
    /// the thing being copied.
    ///
    /// The mesh is written from a cached base copy. Reading back the displaced vertices and
    /// re-displacing them would compound the offset and the sea would climb away.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WaterWave : MonoBehaviour
    {
        /// <summary>
        /// Scales the whole formula. 1 = the web exactly; anything else is a deliberate departure,
        /// so it is one number in one place rather than three amplitudes to keep in step.
        /// </summary>
        public float amplitude = 1f;

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

        private int _frame;

        private void Update()
        {
            if (_mesh == null || _base == null) return;

            // Every other frame, like the web (builder.html:3929 — "คลื่น+normals ทุก 2 เฟรม
            // (~30fps) = ลด CPU ครึ่ง แทบไม่เห็นต่าง"). Recomputing normals over a 72-segment disc
            // is the expensive half, and water at 30 Hz is indistinguishable from water at 60.
            if ((_frame++ & 1) != 0) return;

            float t = Time.time;
            for (int i = 0; i < _base.Length; i++)
            {
                Vector3 p = _base[i];
                p.y = _base[i].y + (float)DiveMap.Core.WaterWaveMath.Height(p.x, p.z, t) * amplitude;
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
