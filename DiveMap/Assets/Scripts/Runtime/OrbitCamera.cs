using UnityEngine;

namespace DiveMap.Runtime
{
    /// <summary>
    /// Touch + mouse orbit camera for the flat-screen viewer (WO-XR-01; the XR
    /// gaze/pinch equivalents come in WO-XR-02). Legacy UnityEngine.Input only — no
    /// InputSystem package.
    ///
    ///   Touch:  1-finger drag = orbit · 2-finger pinch = zoom · 2-finger drag = pan
    ///   Mouse:  left-drag = orbit · wheel = zoom · right-drag = pan
    ///
    /// Pitch is clamped 5..85° and distance is clamped to a min/max envelope.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OrbitCamera : MonoBehaviour
    {
        public Vector3 target = Vector3.zero;
        public float distance = 20f;

        public float minPitch = 5f;
        public float maxPitch = 85f;
        public float minDistance = 2f;
        public float maxDistance = 950f; // matches web builder controls.maxDistance (large sites)

        public float orbitSpeed = 0.3f;      // deg per pixel
        public float pinchZoomSpeed = 0.02f; // per pixel of pinch delta
        public float mouseZoomSpeed = 2.5f;  // per wheel notch
        public float panSpeed = 0.0015f;     // world units per pixel per distance

        private float _yaw = 45f;
        private float _pitch = 35f;

        private float _prevPinchDist;
        private Vector2 _prevTwoFingerMid;

        private void Start() => Apply();

        /// <summary>Frame the map: centre on <paramref name="center"/> and back off to fit radius.</summary>
        public void Frame(Vector3 center, float radius)
        {
            target = center;
            var cam = GetComponent<Camera>();
            float fov = cam != null ? cam.fieldOfView : 60f;
            float fit = radius / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            distance = Mathf.Clamp(fit * 1.25f, minDistance, maxDistance);
            _yaw = 45f;
            _pitch = 35f;
            Apply();
        }

        /// <summary>
        /// Frame around a content bounding box the way the web builder's frameContent()
        /// does: aim at the lower part of the box, back off by the HORIZONTAL extent only
        /// (so a tall water column / tall statue doesn't push the camera miles away), and
        /// look down at a gentle ~18° angle so both the seabed and the water surface stay
        /// in shot. Camera ends up at (cx, aimY + dist*0.32, cz + dist).
        /// </summary>
        public void FrameBox(Vector3 center, float sizeX, float sizeY, float sizeZ, float minY)
        {
            Framing f = ComputeFraming(center.x, center.z, sizeX, sizeY, sizeZ, minY, minDistance, maxDistance);
            target = new Vector3(f.TargetX, f.TargetY, f.TargetZ);
            _yaw = f.Yaw;
            _pitch = f.Pitch;
            distance = f.Distance;
            Apply();
        }

        /// <summary>Pure framing solution (no Unity object access) so it is unit-testable.</summary>
        public struct Framing
        {
            public float TargetX, TargetY, TargetZ;
            public float Yaw, Pitch, Distance;
        }

        public static Framing ComputeFraming(
            float centerX, float centerZ, float sizeX, float sizeY, float sizeZ, float minY,
            float minDistance, float maxDistance)
        {
            float r = Mathf.Max(sizeX, sizeZ) * 0.5f;
            if (r <= 0f) r = 30f;

            // web: dist = min(cap, r*1.45 + 40); camera raised by dist*0.32, pushed back by dist.
            float dist = Mathf.Min(maxDistance, r * 1.45f + 40f);
            float vert = dist * 0.32f;

            float aimY = minY + Mathf.Min(sizeY * 0.4f, 45f);

            float pitch = Mathf.Atan2(vert, dist) * Mathf.Rad2Deg; // ~17.7°
            float distance = Mathf.Sqrt(dist * dist + vert * vert); // |offset| so pos == target+(0,vert,dist)

            return new Framing
            {
                TargetX = centerX,
                TargetY = aimY,
                TargetZ = centerZ,
                Yaw = 180f,       // put the camera on the +Z side looking toward -Z (matches web +Z back-off)
                Pitch = Mathf.Clamp(pitch, 5f, 85f),
                Distance = Mathf.Clamp(distance, minDistance, maxDistance),
            };
        }

        private void Update()
        {
            int touches = Input.touchCount;
            if (touches == 1)
            {
                HandleOneFinger();
            }
            else if (touches >= 2)
            {
                HandleTwoFingers();
            }
            else
            {
                _prevPinchDist = 0f;
                HandleMouse();
            }
            Apply();
        }

        private void HandleOneFinger()
        {
            Touch t = Input.GetTouch(0);
            if (t.phase == TouchPhase.Moved)
            {
                _yaw += t.deltaPosition.x * orbitSpeed;
                _pitch -= t.deltaPosition.y * orbitSpeed;
                ClampPitch();
            }
            _prevPinchDist = 0f;
        }

        private void HandleTwoFingers()
        {
            Touch a = Input.GetTouch(0);
            Touch b = Input.GetTouch(1);

            float curDist = Vector2.Distance(a.position, b.position);
            Vector2 curMid = (a.position + b.position) * 0.5f;

            if (_prevPinchDist > 0f)
            {
                // Zoom from pinch delta.
                float delta = curDist - _prevPinchDist;
                distance -= delta * pinchZoomSpeed * distance;
                distance = Mathf.Clamp(distance, minDistance, maxDistance);

                // Pan from the movement of the two-finger midpoint.
                Vector2 midDelta = curMid - _prevTwoFingerMid;
                Pan(midDelta);
            }

            _prevPinchDist = curDist;
            _prevTwoFingerMid = curMid;
        }

        private void HandleMouse()
        {
            if (Input.GetMouseButton(0))
            {
                _yaw += Input.GetAxis("Mouse X") * orbitSpeed * 12f;
                _pitch -= Input.GetAxis("Mouse Y") * orbitSpeed * 12f;
                ClampPitch();
            }
            else if (Input.GetMouseButton(1))
            {
                var d = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 12f;
                Pan(d);
            }

            float wheel = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(wheel) > 0.0001f)
            {
                distance -= wheel * mouseZoomSpeed * distance;
                distance = Mathf.Clamp(distance, minDistance, maxDistance);
            }
        }

        private void Pan(Vector2 screenDelta)
        {
            // Move the target opposite the drag, in the camera's screen plane.
            Vector3 right = transform.right;
            Vector3 up = transform.up;
            float scale = panSpeed * distance;
            target -= (right * screenDelta.x + up * screenDelta.y) * scale;
        }

        private void ClampPitch() => _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);

        private void Apply()
        {
            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 pos = target - (rot * Vector3.forward) * distance;
            transform.SetPositionAndRotation(pos, rot);
        }
    }
}
