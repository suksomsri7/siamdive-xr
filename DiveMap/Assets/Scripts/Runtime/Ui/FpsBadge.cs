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
        private GUIStyle _tailStyle;

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
        ///
        /// 🔴 16 ส.ค. 2026 — ปิดแล้ว (user: "เอาตัวเลขแรมออก") คดีปิดไปแล้ว: อาการคือ jetsam
        /// ตอนแอปถูกย่อ ไม่ใช่แรมรั่ว และทางแก้ (ปล่อยฉากทั้งก้อนตอนพักแอป) ก็อยู่ในบิลด์แล้ว
        /// ⇒ ตัวเลขนี้หมดหน้าที่ · เหลือโค้ดไว้เพราะถ้าอาการกลับมา เปิดบรรทัดเดียวได้เครื่องมือคืน
        /// </summary>
        public const bool HuntingMemoryBug = false;

        /// <summary>บรรทัดแรมควรอยู่บนจอไหม — เห็นเสมอระหว่างล่าบั๊ก, ไม่งั้นตามกฎเดิมของป้าย.</summary>
        public static bool MemoryLineVisible => HuntingMemoryBug || Visible;

        public static void Ensure()
        {
            if (Object.FindFirstObjectByType<FpsBadge>() != null) return;
            var go = new GameObject("FpsBadge");
            DontDestroyOnLoad(go);
            go.AddComponent<FpsBadge>();
        }

        // ── หาง log บนจอ (ชั่วคราว — คดี "ออกจาก AR แล้วพัง" 22 ส.ค. 2026) ─────────────
        //
        // 🔴 ห้ารอบของคดีนี้เผาไปกับการอนุมานจากภาพนิ่ง เพราะ log ที่ตอบคำถามได้จริง
        // ([Mode] ใครเปลี่ยนโหมด · [Tour] spawn=warp/default · [AR] exit · [UI] orbit on/off)
        // อยู่ใน syslog ของ iOS ซึ่งไม่มีทางถึงมือเรา ⇒ เอาบรรทัดท้าย ๆ ขึ้นจอไปกับ badge:
        // ภาพหน้าจอใบเดียว = timeline จริงของเครื่อง · ถอดพร้อม badge เมื่อปิดคดี
        private static readonly System.Collections.Generic.Queue<string> _tail
            = new System.Collections.Generic.Queue<string>();
        private const int TailLines = 7;

        private void OnEnable() => Application.logMessageReceived += OnLog;
        private void OnDisable() => Application.logMessageReceived -= OnLog;

        private static void OnLog(string msg, string stack, LogType type)
        {
            if (string.IsNullOrEmpty(msg)) return;
            // เฉพาะบรรทัดที่เล่าเรื่องโหมด/กล้อง/จุดเกิด — ไม่ใช่ log ทั้งแอป
            if (!(msg.StartsWith("[Mode]") || msg.StartsWith("[Tour]") ||
                  msg.StartsWith("[AR]") || msg.StartsWith("[ARKit]") ||
                  msg.StartsWith("[Native]") ||
                  (msg.StartsWith("[UI]") && msg.Contains("orbit")))) return;
            if (_tail.Count >= TailLines) _tail.Dequeue();
            // ตัดให้พอดีจอ — ภาพถ่ายมือถืออ่านได้ถึง ~90 ตัวอักษร
            _tail.Enqueue(msg.Length > 90 ? msg.Substring(0, 90) : msg);
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

            // 🔴 22 ส.ค. 2026 — โหมด + สถานะกล้องโคจร ต่อท้าย (ชั่วคราว ระหว่างคดี "ขยับไม่ได้")
            // คำถามที่ภาพหน้าจอรอบก่อนตอบไม่ได้คือ "Unity คิดว่าตัวเองอยู่โหมดไหน และ orbit
            // เปิดอยู่ไหม" — สองคำนี้บนป้ายทำให้ภาพใบเดียวชี้ตัวการได้เลย (ธรรมเนียมเดียวกับ
            // ที่เลข fps ปิดคดีปลาสั่น และ bNNN ปิดคดี "เครื่องรันบิลด์ไหน")
            Camera bc = Camera.main;
            OrbitCamera bo = bc != null ? bc.GetComponent<OrbitCamera>() : null;
            string state = $" · {ModeManager.Current}{(bo != null && bo.enabled ? "+o" : "-o")}";

            // แถวล่างสุด = ของเดิม (fps) · เมื่อฝังในแอปแถวนี้ถูกซ่อนตามคำสั่ง user แล้วบรรทัดแรม
            // จะเลื่อนลงมาแทนที่ ไม่ใช่ลอยค้างเว้นช่องว่างไว้ให้สงสัยว่ามีอะไรหายไป
            float bottomY = Screen.height - Screen.height * 0.06f;
            float lineH = Screen.height * 0.05f;
            float rightPad = Screen.height * 0.012f;

            if (Visible)
            {
                GUI.Label(new Rect(0, bottomY, Screen.width - rightPad, lineH),
                          $"{_fps:0} fps{build}{fish}{state}", _style);

                // หาง log (ดูคอมเมนต์ที่ _tail) + บรรทัดกล้อง: ตำแหน่งจริง vs จุดที่ orbit จะพาไป
                // — ภาพเดียวบอกได้เลยว่ากล้อง "ค้าง" เพราะ Apply ไม่วิ่ง หรือเพราะ target ผิด
                if (_tailStyle == null)
                {
                    _tailStyle = new GUIStyle(_style)
                    {
                        fontSize = Mathf.RoundToInt(Screen.height * 0.013f),
                    };
                }
                _tailStyle.normal.textColor = new Color(1f, 1f, 1f, 0.45f);
                float tailH = Screen.height * 0.028f;
                float y = bottomY - lineH * 0.9f;
                if (bc != null)
                {
                    Vector3 cp = bc.transform.position;
                    string cam = bo != null
                        ? $"cam({cp.x:F0},{cp.y:F0},{cp.z:F0}) tgt({bo.target.x:F0},{bo.target.y:F0},{bo.target.z:F0}) d={bo.distance:F0}"
                        : $"cam({cp.x:F0},{cp.y:F0},{cp.z:F0}) no-orbit";
                    GUI.Label(new Rect(0, y, Screen.width - rightPad, tailH), cam, _tailStyle);
                    y -= tailH;
                }
                // คิวเก็บเก่า→ใหม่ — วาดจากล่างขึ้นบนให้บรรทัดล่าสุดอยู่ล่างสุด
                string[] lines = _tail.ToArray();
                for (int i = lines.Length - 1; i >= 0; i--)
                {
                    GUI.Label(new Rect(0, y, Screen.width - rightPad, tailH), lines[i], _tailStyle);
                    y -= tailH;
                }
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
