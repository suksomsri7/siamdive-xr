using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// บรรทัดแรมบนป้ายมุมจอ (คดี "แอปดับตอนสลับแมพ" 14 ส.ค. 2026).
    ///
    /// เล็กแต่ต้องมีเทส เพราะเครื่องมือวัดตัวนี้จะถูกอ่านจาก **รูปถ่ายหน้าจอ** เท่านั้น และ
    /// โปรเจกต์นี้เสียเวลาไปหลายวันกับเครื่องมือวัดที่โกหกมาแล้วสามรอบ. สิ่งที่ห้ามพลาดคือ
    /// "อ่านค่าไม่ได้" ต้องหน้าตาไม่เหมือน "อ่านได้แล้วเลขน้อย" — ไม่งั้นรูปที่ user ส่งมาจะถูก
    /// ตีความว่าแรมเหลือเฟือ ทั้งที่จริงคือเราไม่ได้วัดอะไรเลย
    /// </summary>
    public class MemoryReadingTests
    {
        private const long MB = 1024L * 1024L;

        [Test]
        public void ReadableNumbersShowBothUsedAndHeadroom()
        {
            string s = MemoryReading.Format(812 * MB, 1900 * MB, 40 * MB);
            Assert.AreEqual("แรม 812 MB · เหลือ 1.9 GB", s);
        }

        [Test]
        public void AFailedReadLooksDifferentFromALowNumber()
        {
            // ทั้งสองค่าอ่านไม่ได้ (Android/Editor) → ต้องประกาศตัวว่าเป็นเลข mono ไม่ใช่แรมจริง
            string fallback = MemoryReading.Format(-1, -1, 37 * MB);
            Assert.AreEqual("mono 37 MB", fallback);
            StringAssert.DoesNotContain("แรม", fallback);

            // อ่านได้ครึ่งเดียว: บอกเท่าที่รู้ ห้ามเดา "เหลือ" ขึ้นมาเอง
            Assert.AreEqual("แรม 900 MB", MemoryReading.Format(900 * MB, -1, 12 * MB));
        }

        [Test]
        public void MegabytesStayWholeSoASixtyMegLeakIsVisible()
        {
            // ความละเอียดระดับ MB คือทั้งหมดของเครื่องมือนี้: ถ้าปัดเป็น GB ตั้งแต่ต้น การไต่ขึ้น
            // ทีละ ~60MB ต่อการสลับแมพหนึ่งครั้งจะมองไม่เห็นเลยในรูปถ่าย
            Assert.AreEqual("640 MB", MemoryReading.Human(640 * MB));
            Assert.AreEqual("700 MB", MemoryReading.Human(700 * MB));
            Assert.AreEqual("1.0 GB", MemoryReading.Human(1024 * MB));
            Assert.AreEqual("?", MemoryReading.Human(-1));
        }

        [Test]
        public void PressureTurnsRedBeforeTheSystemKills()
        {
            Assert.AreEqual(MemoryReading.Pressure.Ok, MemoryReading.PressureOf(900 * MB));
            Assert.AreEqual(MemoryReading.Pressure.Warning, MemoryReading.PressureOf(200 * MB));
            Assert.AreEqual(MemoryReading.Pressure.Critical, MemoryReading.PressureOf(80 * MB));
        }

        [Test]
        public void AnUnreadableHeadroomIsNotAnAlarm()
        {
            // -1 = ไม่รู้ ไม่ใช่ "ใกล้ตาย". ถ้าตีเป็นวิกฤต ป้ายจะแดงทั้งจอบนทุกเครื่อง Android
            // แล้วสีก็จะเลิกมีความหมายทันที
            Assert.AreEqual(MemoryReading.Pressure.Unknown, MemoryReading.PressureOf(-1));
        }
    }
}
