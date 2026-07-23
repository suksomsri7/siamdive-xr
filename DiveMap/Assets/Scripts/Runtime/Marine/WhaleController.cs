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
            // Loop scaled to the animal's size so a big whaleshark sweeps a big arc.
            radiusX = Mathf.Max(20f, size * 1.3f);
            radiusZ = Mathf.Max(24f, size * 1.6f);
            bobAmp = Mathf.Max(3f, size * 0.18f);
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
