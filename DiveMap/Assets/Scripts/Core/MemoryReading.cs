namespace DiveMap.Core
{
    /// <summary>
    /// การอ่านค่าแรมให้เป็นข้อความบนจอ — ส่วนที่ "ผิดได้" ของเครื่องมือวัด จึงแยกออกมาให้เทสได้
    /// (คดี "แอปดับตอนสลับแมพ" 14 ส.ค. 2026)
    ///
    /// 🔴 เหตุผลที่ต้องมีบรรทัดนี้บนจอเลย แทนที่จะรอ log: การตายแบบที่ user เจอ — กลับไปหน้าโฮม
    /// โดยไม่มีไฟล์รายงานทั้งในเครื่องและใน TestFlight — คือลายเซ็นของ jetsam ซึ่ง "ไม่ทิ้งอะไรไว้
    /// เลย" ตามนิยาม (ดู <c>MemoryWatch</c>). เครื่องมือที่ต้องรอดจากการตายจึงใช้ไม่ได้ ต้องเป็น
    /// เครื่องมือที่ตอบ *ก่อน* ตาย และรูปถ่ายหน้าจอของ user คือช่องทางเดียวที่พิสูจน์แล้วว่าได้ผล
    /// จริงในโปรเจกต์นี้ (เลข fps บนจอเคยปิดคดี "ปลาสั่น" มาแล้วทั้งคดี)
    ///
    /// สิ่งที่ต้องอ่านออกจากรูปเดียว: ตัวเลขไต่ขึ้นทุกครั้งที่สลับแมพแล้วไม่ลง = รั่วสะสม ·
    /// ขึ้นสูงชั่วครู่ตอนโหลดแล้วลง = ยอดพุ่ง (แก้คนละทาง) · "เหลือ" ลู่เข้าศูนย์ = ยืนยัน jetsam
    /// </summary>
    public static class MemoryReading
    {
        /// <summary>ต่ำกว่านี้ถือว่าใกล้โดนฆ่า — ทาสีแดงเพื่อให้เห็นในรูปถ่ายโดยไม่ต้องอ่านเลข.</summary>
        public const long CriticalFreeBytes = 150L * 1024 * 1024;

        /// <summary>ระหว่างนี้ถึง <see cref="CriticalFreeBytes"/> = เหลือง.</summary>
        public const long WarningFreeBytes = 350L * 1024 * 1024;

        /// <summary>ระดับความเสี่ยงจากช่องว่างที่เหลือ. -1 (อ่านไม่ได้) = ปกติ ไม่ใช่วิกฤต.</summary>
        public enum Pressure { Unknown = 0, Ok = 1, Warning = 2, Critical = 3 }

        public static Pressure PressureOf(long availableBytes)
        {
            if (availableBytes < 0) return Pressure.Unknown;
            if (availableBytes < CriticalFreeBytes) return Pressure.Critical;
            if (availableBytes < WarningFreeBytes) return Pressure.Warning;
            return Pressure.Ok;
        }

        /// <summary>
        /// ไบต์ → ข้อความสั้นที่อ่านออกจากรูปถ่ายมือถือ. ต่ำกว่า 1GB ใช้ MB จำนวนเต็ม (ความละเอียด
        /// ระดับ MB คือสิ่งที่ทำให้เห็นว่า "ไต่ขึ้นทีละ 60MB ทุกครั้งที่สลับ") · ตั้งแต่ 1GB ขึ้นไป
        /// ใช้ GB ทศนิยมหนึ่งตำแหน่ง เพราะเลขสี่หลักบนมุมจอเล็ก ๆ อ่านไม่ทัน
        /// </summary>
        public static string Human(long bytes)
        {
            if (bytes < 0) return "?";
            double mb = bytes / (1024.0 * 1024.0);
            if (mb < 1024.0) return $"{mb:0} MB";
            return $"{mb / 1024.0:0.0} GB";
        }

        /// <summary>
        /// บรรทัดแรมสำหรับป้ายมุมจอ.
        ///
        /// <paramref name="footprintBytes"/>/<paramref name="availableBytes"/> = ค่าจากระบบ (-1 ถ้า
        /// อ่านไม่ได้ เช่น Android/Editor) · <paramref name="monoBytes"/> = ตัวสำรองที่มีทุกที่.
        /// เมื่ออ่านค่าระบบไม่ได้จะขึ้น "mono NN MB" ซึ่ง **จงใจให้หน้าตาต่าง** จากบรรทัดปกติ:
        /// ถ้าวันหนึ่งรูปจาก user แสดง mono ล้วน จะได้รู้ทันทีว่ากำลังดูตัวเลขที่ตอบคำถามไม่ได้
        /// แทนที่จะเถียงกันว่าทำไมแรมดู "น้อยจัง"
        /// </summary>
        public static string Format(long footprintBytes, long availableBytes, long monoBytes)
        {
            if (footprintBytes < 0 && availableBytes < 0)
                return $"mono {Human(monoBytes)}";

            string used = footprintBytes >= 0 ? Human(footprintBytes) : "?";
            if (availableBytes < 0) return $"แรม {used}";
            return $"แรม {used} · เหลือ {Human(availableBytes)}";
        }
    }
}
