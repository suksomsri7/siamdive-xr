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

        /// <summary>
        /// Should the numbers be on screen right now? (WO-MERGE P1c)
        ///
        /// 🔴 Two products, two answers, and the reason the check is here rather than at the
        /// <see cref="Ensure"/> call site. Standalone DiveMap keeps the badge unconditionally —
        /// it is not a debug overlay, it is the instrument: every video the user films of the
        /// standalone build carries its own fps reading, which is the only thing that has ever
        /// been able to tell "the fish swim wrong" from "the phone renders at 18 fps". Embedded in
        /// the SiamDive app it is somebody else's product surface and the user asked for it gone.
        ///
        /// Asked every frame instead of once at startup because <c>badge</c> arrives with the
        /// host's boot payload, which lands after the badge object already exists — and because
        /// <see cref="NativeBridge.EmbeddedInHost"/> is a cached packaging fact, so the common
        /// answer costs a P/Invoke into a static bool.
        /// </summary>
        public static bool Visible =>
            !NativeBridge.EmbeddedInHost || DiveMap.Core.NativeBoot.BadgeForced;

        /// <summary>
        /// 🔴 ชั่วคราว — ปิดเมื่อปิดคดี "แอปดับตอนสลับแมพ" (เปิด 14 ส.ค. 2026)
        ///
        /// บรรทัดแรมโชว์แม้ตอนฝังในแอป ทั้งที่ user เคยสั่งให้ซ่อนตัวเลขมุมจอในโหมดนั้น — เพราะ
        /// อาการที่กำลังไล่อยู่ *ไม่ทิ้งหลักฐานไว้เลย* (ไม่มีไฟล์รายงานทั้งในเครื่องและใน TestFlight
        /// = ลายเซ็นของ jetsam) เครื่องมือที่ต้องรอดจากการตายจึงใช้ไม่ได้ทั้งหมด เหลือทางเดียวคือ
        /// ตัวเลขที่ตอบก่อนตาย แล้วอ่านจากรูปถ่ายของ user — วิธีเดียวกับที่เลข fps เคยปิดคดี
        /// "ปลาสั่น" มาแล้วทั้งคดี
        ///
        /// ตั้งเป็น const ตัวเดียวเพื่อให้ปิดคืนได้ด้วยการแก้บรรทัดเดียว ไม่ต้องรื้อโครงป้าย
        /// </summary>
        public const bool HuntingMemoryBug = true;

        /// <summary>บรรทัดแรมควรอยู่บนจอไหม — เห็นเสมอระหว่างล่าบั๊ก, ไม่งั้นตามกฎเดิมของป้าย.</summary>
        public static bool MemoryLineVisible => HuntingMemoryBug || Visible;

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
            // The object stays alive and keeps counting: a debug switch that turned the badge back
            // on should show the fps of the last half second, not start from zero.
            if (!Visible && !MemoryLineVisible) return;

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
            // 🔴 เลขบิลด์ + ปลาได้โมเดลจริงกี่ฝูง — ต่อท้ายเลข fps
            //
            // สองคืนที่ผ่านมาเสียไปกับคำถามที่ตอบไม่ได้สองข้อ: "เครื่องรันบิลด์ไหนอยู่" (เลขบิลด์
            // มีอยู่แล้วแต่อยู่บนแถบสถานะ ซึ่งหายไปตอนดำน้ำ) และ "ปลาโหลดโมเดลใหม่จริงไหม"
            // (user ถามเอง 9 ส.ค.) · ปลาที่โหลด GLB ไม่ติดจะตกไปใช้เมชสำรอง ซึ่งเมื่อก่อนโบกตัว
            // ที่ 1.114 Hz ตายตัวไม่ฟังค่าที่จูน = อธิบาย "จูนอะไรก็ไม่เปลี่ยน" ได้ทั้งหมด
            // ทั้งสองข้อจบด้วยภาพหน้าจอเดียวตั้งแต่บรรทัดนี้
            string build = Core.BuildStamp.Suffix;   // คืนมาเป็น " · bNNN" อยู่แล้ว
            int tot = Marine.FishSchoolSystem.TotalSchools;
            string fish = tot > 0 ? $" · ปลา {Marine.FishSchoolSystem.GlbSchools}/{tot}" : "";

            // แถวล่างสุด = ของเดิม (fps) · เมื่อฝังในแอปแถวนี้ถูกซ่อนตามคำสั่ง user แล้วบรรทัดแรม
            // จะเลื่อนลงมาแทนที่ ไม่ใช่ลอยค้างเว้นช่องว่างไว้ให้สงสัยว่ามีอะไรหายไป
            float bottomY = Screen.height - Screen.height * 0.06f;
            float lineH = Screen.height * 0.05f;
            float rightPad = Screen.height * 0.012f;

            if (Visible)
            {
                GUI.Label(new Rect(0, bottomY, Screen.width - rightPad, lineH),
                          $"{_fps:0} fps{build}{fish}", _style);
            }

            if (!MemoryLineVisible) return;

            // 🔴 สีของบรรทัดนี้คือส่วนที่อ่านได้เร็วกว่าตัวเลข: ในรูปถ่ายมือถือของ user เลขสี่หลัก
            // มุมจออ่านยาก แต่ "แถบล่างเปลี่ยนเป็นแดง" เห็นทันทีว่าเครื่องใกล้ฆ่าแอปแล้ว
            var pressure = MemoryMeter.Pressure;
            _style.normal.textColor =
                pressure == Core.MemoryReading.Pressure.Critical ? new Color(1f, 0.35f, 0.3f, 0.95f)
              : pressure == Core.MemoryReading.Pressure.Warning ? new Color(1f, 0.85f, 0.3f, 0.9f)
                                                                : new Color(1f, 1f, 1f, 0.55f);
            float memY = Visible ? bottomY - lineH * 0.85f : bottomY;
            GUI.Label(new Rect(0, memY, Screen.width - rightPad, lineH),
                      MemoryMeter.Line, _style);
        }
    }
}
