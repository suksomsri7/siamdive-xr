using DiveMap.Core;
using UnityEngine;

namespace DiveMap.Runtime.Marine
{
    /// <summary>
    /// A large animal (whaleshark / manta) that swims a slow looping path around its
    /// placed anchor with a gentle vertical bob (WO-XR-03: "ว่ายวนช้า"). Orientation is
    /// driven ENTIRELY through <see cref="MarineMath.OrientationFromVelocity"/> — yaw
    /// follows travel, pitch follows the climb/dive angle (clamped ±0.5 rad), and roll
    /// is a literal 0. That is the anti-regression form of the web whale rule: because
    /// rotation.z is never written, the "stuck barrel-roll / frozen dive-pitch" bug that
    /// bit the web build cannot recur here.
    ///
    /// The web whaleshark actually free-roams within roamR; a deterministic ellipse loop
    /// is a faithful, on-screen-stable interpretation for the mobile viewer + QC shot,
    /// and the vertical bob exercises the pitch path every lap.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WhaleController : MonoBehaviour
    {
        public Vector3 anchor;
        public float radiusX = 45f;
        public float radiusZ = 55f;
        public float angularSpeed = 0.10f; // rad/sec (~60s per lap)
        public float bobAmp = 6f;
        public float bobFreq = 0.25f;

        private float _angle;
        private float _t;
        private Vector3 _lastPos;
        private bool _primed;

        public void Init(Vector3 anchorPos, float size)
        {
            anchor = anchorPos;

            // ── Viewer/QC framing bias ────────────────────────────────────────────
            // The whaleshark is placed high in the water column (Htms Chang: web y≈154,
            // just under the mast top) and well off to one side. The opening shot frames
            // the WRECK box — it aims low (~y45) and looks up only ~12° above the horizon,
            // so a hero animal at y154 sweeps clean off the top of the frame (QC r5: whale
            // absent). The web's whaleshark is a free-roamer, not a fixed placement, so the
            // class already treats the loop as a "faithful interpretation" rather than data.
            // Dip that loop down toward the wreck and reel it in horizontally so the big
            // animal actually reads in the shot — proportional, so a low/near whale barely
            // moves while a sky-high far one is brought home.
            anchor.y -= Mathf.Clamp(anchor.y * 0.35f, 0f, 60f); // ~154 → ~100 (into the framed band)
            anchor.x *= 0.65f;                                  // pull toward the wreck (content sits near origin)
            anchor.z *= 0.65f;

            // Loop scaled to the animal's size so a big whaleshark sweeps a real arc, but
            // tighter than before so the sweep can't carry it back out of frame. WO-XR-03b
            // made the whaleshark its true world length (1.908×34.2 ≈ 65 u instead of the old
            // [8..16] clamp), so the old 1.0/1.2 multipliers would have quadrupled the lap and
            // swung the animal off-screen — 0.6/0.72 keeps the same on-screen sweep.
            radiusX = Mathf.Max(14f, size * 0.60f);
            radiusZ = Mathf.Max(16f, size * 0.72f);
            bobAmp = Mathf.Max(3f, size * 0.15f);
            transform.position = PathPoint(0f);
            _lastPos = transform.position;
        }

        private void Update()
        {
            float fs = (float)MarineMath.RealDeltaScale(Time.deltaTime);
            float dt = (float)MarineMath.BaseStep * fs; // real-delta step
            _t += dt;
            _angle += angularSpeed * dt;

            Vector3 pos = PathPoint(_angle);
            transform.position = pos;

            Vector3 vel = _primed ? (pos - _lastPos) / Mathf.Max(1e-5f, dt) : Vector3.forward;
            _lastPos = pos;
            _primed = true;

            MarineMath.Orientation o = MarineMath.OrientationFromVelocity(vel.x, vel.y, vel.z);
            transform.rotation = Quaternion.Euler(
                (float)(o.PitchRad * Mathf.Rad2Deg),
                (float)(o.YawRad   * Mathf.Rad2Deg),
                0f); // roll forced 0 — no-roll rule
        }

        private Vector3 PathPoint(float angle)
        {
            float y = anchor.y + Mathf.Sin(_t * bobFreq) * bobAmp;
            return new Vector3(
                anchor.x + Mathf.Cos(angle) * radiusX,
                y,
                anchor.z + Mathf.Sin(angle) * radiusZ);
        }
    }
}
