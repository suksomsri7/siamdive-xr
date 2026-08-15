using System;

namespace DiveMap.Core
{
    /// <summary>
    /// โหมดที่แอปเจ้าบ้านสั่งมาพร้อม boot payload (WO-PIVOT, 14 ส.ค. 2026).
    ///
    /// ทิศทางใหม่ของโปรเจกต์: การสร้าง/แก้แมพเกิดบนเว็บ ส่วน Unity เป็น "จอ" ที่ถูกเรียกใช้ —
    /// ผู้ใช้แตะหมุดบนแผนที่ในแอปแล้วเลือกเอาเลยว่าจะดู (Preview) · ส่อง AR · หรือทัวร์โดรน
    /// ⇒ Unity ต้องเข้าโหมดนั้น **ตั้งแต่แมพขึ้น** ไม่ใช่ให้ผู้ใช้ไปหาปุ่มในเมนูของ Unity อีกที
    ///
    /// 🔴 กติกาข้อเดียวที่สำคัญที่สุด: เมื่อเจ้าบ้านสั่งโหมดมา คำสั่งนั้นต้องชนะ auto-play ของ
    /// <see cref="ArenaEntry"/> เสมอ. ของเดิมจะโยนผู้เล่นเข้าทัวร์เองที่ 0.6 วินาทีหลังแมพขึ้น
    /// ถ้าไม่ปิดทับ ผู้ใช้ที่กด "ดูแมพ" จะเห็นแมพครึ่งวินาทีแล้วถูกลากเข้าทัวร์ — อาการที่ดูเหมือน
    /// บั๊กสุ่มและไล่ยากมาก เพราะเงื่อนไข auto-play ขึ้นกับสิทธิ์แก้ไข/ออนไลน์/เจ้าของแมพ
    ///
    /// แยกเป็น pure function เพื่อให้เทสได้บนเครื่องนี้ (2 วินาที) แทนที่จะรอ CI 110 นาที
    /// </summary>
    public static class BootMode
    {
        public enum Requested
        {
            /// <summary>เจ้าบ้านไม่ได้สั่ง — พฤติกรรมเดิมทุกอย่าง (รวม auto-play).</summary>
            None = 0,
            /// <summary>ดูแมพจากภายนอก (โคจรรอบ) = <c>AppMode.View</c>.</summary>
            Preview = 1,
            /// <summary>ทัวร์โดรน.</summary>
            Tour = 2,
            /// <summary>วางแมพบนโต๊ะจริงผ่านกล้อง.</summary>
            Ar = 3,
            /// <summary>ทัวร์ + ชั้นเกมเก็บขยะ/เหรียญ.</summary>
            Game = 4,
        }

        /// <summary>
        /// อ่านค่าที่เจ้าบ้านส่งมา. ยอมรับหลายคำพ้องเพราะฝั่ง RN/เว็บเรียกสิ่งเดียวกันคนละชื่อ
        /// มาตลอด (จอ 3D = preview/view, AR = ar/holomap) และคำที่ไม่รู้จักต้องกลายเป็น
        /// <see cref="Requested.None"/> เสมอ — ไม่ใช่ค่าเริ่มต้นอะไรสักอย่าง เพราะการเดาผิดที่นี่
        /// แปลว่าผู้ใช้ถูกพาไปโหมดที่ไม่ได้ขอ ซึ่งแย่กว่าการไม่ทำอะไรเลย
        /// </summary>
        public static Requested Parse(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return Requested.None;
            switch (raw.Trim().ToLowerInvariant())
            {
                case "preview":
                case "view":
                case "3d":
                    return Requested.Preview;
                case "tour":
                case "drone":
                    return Requested.Tour;
                case "ar":
                case "holomap":
                    return Requested.Ar;
                case "game":
                case "play":
                    return Requested.Game;
                default:
                    return Requested.None;
            }
        }

        /// <summary>
        /// เจ้าบ้านสั่งมาหรือไม่ — ถ้าใช่ auto-play ต้องถูกปิดทับทั้งหมด (ดูหมายเหตุด้านบน).
        /// </summary>
        public static bool OverridesAutoPlay(Requested mode) => mode != Requested.None;

        /// <summary>
        /// โหมดที่ต้องเข้าจริงหลังแมพขึ้น เมื่อรวมคำสั่งของเจ้าบ้านกับสิ่งที่แอปจะทำเอง.
        /// <paramref name="autoPlay"/> = คำตัดสินของ <see cref="ArenaEntry.ShouldAutoPlay"/>.
        /// </summary>
        public static Requested Resolve(Requested hostMode, bool autoPlay)
        {
            if (hostMode != Requested.None) return hostMode;
            return autoPlay ? Requested.Tour : Requested.None;
        }
    }
}
