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
        public float maxDistance = 300f;

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
