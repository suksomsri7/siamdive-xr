using UnityEngine;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// ตัวเลข fps มุมจอ — เกิดจากวิดีโอ 8 ส.ค. 2026 ที่พิสูจน์ว่าปลา "สั่น/กระตุก" เพราะเกม
    /// เรนเดอร์ ~15-20fps ไม่ใช่เพราะท่าว่าย (จูน 7 รอบไม่มีผล) · CI วัดไม่ได้ (ภาพนิ่ง/3fps)
    /// เครื่องมือเดียวที่วัดได้จริงคือวิดีโอจากเครื่อง user — เลขนี้ทำให้ทุกวิดีโอถัดไป
    /// เป็นเครื่องวัด fps ไปในตัว. เล็ก จาง มุมขวาล่าง — อยู่ทุก build จนกว่าปัญหา fps จะปิด.
    /// </summary>
    public sealed class FpsBadge : MonoBehaviour
    {
        private float _acc, _fps;
        private int _n;
        private GUIStyle _style;

        public static void Ensure()
        {
            if (Object.FindFirstObjectByType<FpsBadge>() != null) return;
            var go = new GameObject("FpsBadge");
            DontDestroyOnLoad(go);
            go.AddComponent<FpsBadge>();
        }

        private void Update()
        {
            _acc += Time.unscaledDeltaTime;
            _n++;
            if (_acc >= 0.5f)
            {
                _fps = _n / _acc;
                _acc = 0f;
                _n = 0;
            }
        }

        private void OnGUI()
        {
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.RoundToInt(Screen.height * 0.018f),
                    alignment = TextAnchor.LowerRight,
                };
            }
            _style.normal.textColor = _fps >= 45f ? new Color(1f, 1f, 1f, 0.55f)
                                    : _fps >= 25f ? new Color(1f, 0.85f, 0.3f, 0.8f)
                                                  : new Color(1f, 0.35f, 0.3f, 0.9f);
            GUI.Label(new Rect(0, Screen.height - Screen.height * 0.06f,
                               Screen.width - Screen.height * 0.012f, Screen.height * 0.05f),
                      $"{_fps:0} fps", _style);
        }
    }
}
