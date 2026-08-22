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

        /// <summary>
        /// How far the player may pull back. A DEFAULT ONLY — <c>AppBoot.ApplyViewRange</c> raises
        /// it per map from <see cref="DiveMap.Core.CameraRange"/>, the way the web's
        /// <c>updateViewRange()</c> does (builder.html:709-722).
        ///
        /// 🔴 The old comment on this line said "matches web builder controls.maxDistance (large
        /// sites)". It does not. 950 is what the web writes when it LEAVES AR (builder.html:2955)
        /// and then forgets to call <c>updateViewRange()</c> — a bug on the web's side, and the one
        /// number in that file guaranteed to be wrong for a large site. The web's own floor is
        /// 2,600 and it scales from there. Kept as the field default only so a scene that somehow
        /// never gets a map still behaves exactly as it did.
        /// </summary>
        public float maxDistance = 950f;

        /// <summary>
        /// The cap on the OPENING SHOT's back-off — deliberately NOT <see cref="maxDistance"/>.
        ///
        /// 🔴 These two were the same number until 2026-08-06, and that coupling is a trap: raising
        /// the zoom-out ceiling for a big map would have silently pushed the opening shot (and the
        /// tour's exit re-frame, TourController:520) back with it, changing the first thing the
        /// user sees on every large map while answering a question they asked about the LAST thing.
        /// The web keeps them separate too and is blunter about it — <c>frameContent()</c> caps at
        /// a flat 520 (builder.html:3512) no matter what <c>controls.maxDistance</c> says. 950 is
        /// this app's existing framing cap, kept to the unit so that no map's opening shot moves.
        /// </summary>
        public const float FrameDistanceCap = 950f;

        public float orbitSpeed = 0.3f;      // deg per pixel
        public float pinchZoomSpeed = 0.02f; // per pixel of pinch delta
        public float mouseZoomSpeed = 2.5f;  // per wheel notch
        public float panSpeed = 0.0015f;     // world units per pixel per distance

        private float _yaw = 45f;

        /// <summary>
        /// Point the camera straight down at the map — the "from above" view a player gets by
        /// dragging up, and the one the QC harness photographs. It exists because a whole class of
        /// complaint (light shafts lying across the water, the seabed rim) is invisible from the
        /// diver's eye-level angle every other QC shot is taken at, and shipping a build whose
        /// worst angle nobody has looked at is how the same report comes back three times.
        /// </summary>
        public void LookStraightDown()
        {
            _pitch = maxPitch;
        }

        private float _pitch = 35f;

        private float _prevPinchDist;
        private Vector2 _prevTwoFingerMid;

        // ── นิ้วที่เป็นของ UI (จอย/ปุ่ม) ต้องไม่ขยับกล้อง ────────────────────────────
        //
        // 🔴 22 ส.ค. 2026 — กติกานี้เคยอยู่ที่ UiShell ในรูป "นิ้วใดอยู่บน UI = ปิด OrbitCamera
        // ทั้ง component" ซึ่ง touch ผี (นิ้วที่ระบบไม่เคยส่ง touch-up ให้ ตอนแอปเจ้าบ้านสลับจอ/
        // หมุนจอ — ของจริงบน Unity-as-a-library) ใช้ปักกล้องค้างได้ทั้งระบบ. ที่ถูกคือจำเป็น
        // รายนิ้ว ณ จังหวะ Began: นิ้วที่เริ่มบน UI ไม่มีสิทธิ์หมุน/ย่อ/แพนตลอดชีวิตของมัน
        // ส่วนนิ้วอื่นและการ re-frame อัตโนมัติ (Apply ทุกเฟรม) ไม่เกี่ยวข้องกับมันเลย
        private int _uiFingerA = -1, _uiFingerB = -1;
        private bool _mouseOnUi;

        private static bool OverUi(int fingerId)
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null) return false;
            return fingerId >= 0 ? es.IsPointerOverGameObject(fingerId)
                                 : es.IsPointerOverGameObject();
        }

        private bool IsUiFinger(int id) => id == _uiFingerA || id == _uiFingerB;

        private void TrackFinger(Touch t)
        {
            if (t.phase == TouchPhase.Began && OverUi(t.fingerId))
            {
                if (_uiFingerA < 0) _uiFingerA = t.fingerId;
                else if (_uiFingerB < 0) _uiFingerB = t.fingerId;
            }
            else if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
            {
                if (t.fingerId == _uiFingerA) _uiFingerA = -1;
                if (t.fingerId == _uiFingerB) _uiFingerB = -1;
            }
        }

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
            // The cap is FrameDistanceCap and not maxDistance — see the field's comment.
            float dist = Mathf.Min(FrameDistanceCap, r * 1.45f + 40f);
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
                // …and the final clamp is capped by BOTH, for the same reason the line above is:
                // in build 280 maxDistance was always 950, i.e. always FrameDistanceCap, so
                // min(the two) reproduces every opening shot the app has ever framed to the unit,
                // whatever ceiling the map is later given.
                Distance = Mathf.Clamp(distance, minDistance, Mathf.Min(maxDistance, FrameDistanceCap)),
            };
        }

        private void Update()
        {
            int touches = Input.touchCount;
            for (int i = 0; i < touches; i++) TrackFinger(Input.GetTouch(i));

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
            if (t.phase == TouchPhase.Moved && !IsUiFinger(t.fingerId))
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
            // นิ้วใดนิ้วหนึ่งเป็นของ UI = ทั้ง gesture ไม่ใช่ของกล้อง (พฤติกรรมเดิมของ UiShell)
            if (IsUiFinger(a.fingerId) || IsUiFinger(b.fingerId)) { _prevPinchDist = 0f; return; }

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
            // กดเมาส์เริ่มบน UI = ลากทั้งช่วงนั้นเป็นของ UI (คู่ขนานกับ TrackFinger ฝั่ง touch)
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) _mouseOnUi = OverUi(-1);
            if (!Input.GetMouseButton(0) && !Input.GetMouseButton(1)) _mouseOnUi = false;
            if (_mouseOnUi) return;

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
