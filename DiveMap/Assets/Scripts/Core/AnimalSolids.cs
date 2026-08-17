namespace DiveMap.Core
{
    /// <summary>
    /// สัตว์ตัวใหญ่กันโดรนไม่ให้ทะลุตัว — คณิตศาสตร์ล้วน ทดสอบได้โดยไม่ต้องมีฉาก.
    ///
    /// 🔴 17 ส.ค. 2026 — user: "ทำไมผมบินโดรนทะลุตัวสัตว์ใหญ่ได้อยู่เลย" · คำตอบตรง ๆ คือ
    /// ยังไม่เคยทำ: รายการสิ่งกีดขวางของโดรนสร้างจาก "ของตกแต่งที่โหลดมา" (ซากเรือ · หิน ·
    /// โครงสร้าง) เท่านั้น ส่วนสัตว์อยู่คนละรายการและไม่เคยถูกใส่เข้าไป
    ///
    /// ── ทำไมไม่ยัดสัตว์เข้ารายการสิ่งกีดขวางเดิม ─────────────────────────────────────
    /// รายการนั้นเป็นกล่องนิ่งที่คำนวณครั้งเดียวตอนสร้างแมพ แล้วคัดเฉพาะที่อยู่ใกล้เป็นระยะ ๆ
    /// (SolidBoxes.Select) — เหมาะกับซากเรือที่ไม่ขยับ แต่สัตว์ว่ายทุกเฟรม ถ้าเอาไปปนกัน
    /// ต้องคำนวณกล่องใหม่ทั้งชุดทุกเฟรม ซึ่งเป็นงานที่แพงที่สุดของทัวร์อยู่แล้ว
    ///
    /// ⇒ ที่นี่จึงคิดแบบ "ทรงกลมเคลื่อนที่" อย่างเดียว: ระยะห่างเทียบรัศมี ไม่ต้องมีกล่อง
    /// ไม่ต้องหมุน และไม่ต้องจำอะไรข้ามเฟรม
    ///
    /// ── ทำไมมีขนาดขั้นต่ำ ────────────────────────────────────────────────────────────
    /// ปลาเล็กต้องว่ายผ่านตัวได้ ไม่งั้นการดำผ่านฝูงปลาจะกลายเป็นการชนกำแพงล่องหนนับร้อยครั้ง
    /// — และในโลกจริงปลาเล็กหลบเราเอง (ระบบตกใจทำหน้าที่นั้นอยู่) ส่วนวาฬกับฉลามใหญ่คือสิ่งที่
    /// "ชนแล้วต้องรู้สึก"
    /// </summary>
    public static class AnimalSolids
    {
        /// <summary>รัศมีสัตว์ที่เล็กกว่านี้ (หน่วยโลก) ไม่กันทาง — ปลาเล็กว่ายผ่านตัวได้</summary>
        public const double MinBlockingRadius = 2.5;

        /// <summary>ระยะเผื่อรอบตัวโดรน เพื่อไม่ให้กล้องจ่อติดผิวหนังจนเห็นทะลุตัวสัตว์</summary>
        public const double DiverClearance = 1.2;

        /// <summary>
        /// ดันตำแหน่งออกจากสัตว์ตัวหนึ่ง ถ้ามันซ้อนกันอยู่.
        /// คืน true เมื่อมีการดันจริง (ผู้เรียกใช้ตัดสินใจต่อได้ เช่น หยุดความเร็วด้านนั้น)
        /// </summary>
        public static bool PushOut(double animalX, double animalY, double animalZ, double animalRadius,
                                   ref double x, ref double y, ref double z)
        {
            if (animalRadius < MinBlockingRadius) return false;

            double dx = x - animalX, dy = y - animalY, dz = z - animalZ;
            double need = animalRadius + DiverClearance;
            double d2 = dx * dx + dy * dy + dz * dz;
            if (d2 >= need * need) return false;

            double d = System.Math.Sqrt(d2);
            if (d < 1e-6)
            {
                // อยู่ตรงกลางตัวพอดี (เกิดได้ตอนสัตว์ว่ายมาทับ) — ไม่มีทิศให้ดัน เลือกขึ้นบน
                // เพราะเป็นทางที่ผู้ดำน้ำหนีได้จริงเสมอ และไม่พาไปมุดพื้นทราย
                y = animalY + need;
                return true;
            }

            double k = need / d;
            x = animalX + dx * k;
            y = animalY + dy * k;
            z = animalZ + dz * k;
            return true;
        }

        /// <summary>
        /// สัตว์ตัวนี้ควรตกใจเพราะนักดำน้ำไหม — ใกล้พอ **และ** เข้ามาเร็วพอ.
        ///
        /// เกณฑ์ความเร็วใช้ชุดเดียวกับฝูงปลา (<see cref="FleeMath.DiverIsThreatening"/>) เพื่อให้
        /// "เร็วแค่ไหนถึงน่ากลัว" มีคำตอบเดียวทั้งเกม ส่วนระยะผูกกับขนาดตัว: วาฬยอมให้เข้าใกล้
        /// กว่าปลานกแก้วเมื่อวัดเป็นเท่าของตัวเอง แต่ไกลกว่าเมื่อวัดเป็นเมตร
        /// </summary>
        public static bool DiverStartles(double diverSpeed, double distance, double animalRadius)
        {
            if (animalRadius < MinBlockingRadius) return false;          // ปลาเล็กใช้ระบบฝูง
            if (distance > FleeMath.FleeRadius(animalRadius)) return false;
            // ชนตัวกันแล้วตกใจเสมอ ไม่ว่าจะเข้ามาช้าแค่ไหน — ของใหญ่เท่าบ้านมาแตะตัวคือเหตุ
            if (distance <= animalRadius + DiverClearance) return true;
            return FleeMath.DiverIsThreatening(diverSpeed);
        }
    }
}
