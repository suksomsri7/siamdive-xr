using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// "บินทะลุตัววาฬไม่ได้" และ "ปลาเล็กยังว่ายผ่านตัวได้" — สองข้อนี้ต้องจริงพร้อมกัน
    /// (user 17 ส.ค. 2026: "ทำไมผมบินโดรนทะลุตัวสัตว์ใหญ่ได้อยู่เลย")
    /// </summary>
    public class AnimalSolidsTests
    {
        [Test]
        public void SmallFishDoNotBlockTheDiver()
        {
            double x = 0, y = 0, z = 0;
            // ปลายาวไม่ถึงเกณฑ์ = ว่ายผ่านตัวได้ ไม่งั้นการดำผ่านฝูงจะกลายเป็นการชนกำแพงล่องหน
            Assert.IsFalse(AnimalSolids.PushOut(0, 0, 0, AnimalSolids.MinBlockingRadius - 0.1, ref x, ref y, ref z));
            Assert.AreEqual(0, x); Assert.AreEqual(0, y); Assert.AreEqual(0, z);
        }

        [Test]
        public void ADiverInsideAWhaleIsPushedToItsSurface()
        {
            double x = 2, y = 0, z = 0;                       // อยู่ในตัว (รัศมี 10)
            Assert.IsTrue(AnimalSolids.PushOut(0, 0, 0, 10, ref x, ref y, ref z));

            double d = System.Math.Sqrt(x * x + y * y + z * z);
            Assert.AreEqual(10 + AnimalSolids.DiverClearance, d, 1e-6, "ต้องถูกดันออกมาพอดีที่ผิว + ระยะเผื่อ");
            Assert.Greater(x, 0, "ต้องออกทางเดิมที่เข้าไป ไม่ใช่ทะลุไปอีกฝั่ง");
        }

        [Test]
        public void OutsideTheAnimalNothingMoves()
        {
            double x = 40, y = 0, z = 0;
            Assert.IsFalse(AnimalSolids.PushOut(0, 0, 0, 10, ref x, ref y, ref z));
            Assert.AreEqual(40, x, 1e-9);
        }

        [Test]
        public void DeadCentreGoesUp_NotIntoTheSand()
        {
            double x = 0, y = 0, z = 0;
            Assert.IsTrue(AnimalSolids.PushOut(0, 0, 0, 8, ref x, ref y, ref z));
            Assert.AreEqual(8 + AnimalSolids.DiverClearance, y, 1e-6);
        }

        /// <summary>
        /// 🔴 22 ส.ค. 2026 — สัญญาข้อนี้เปลี่ยนตามที่ user รายงาน ("ขยี่ให้โดรนช้าแล้ว ฉลามวาฬ
        /// ยังว่ายหนีเร็ว"). ของเดิม: ความเร็วเป็นประตูเปิด/ปิดแล้วระยะคงที่ ⇒ เกินเกณฑ์แค่นิดเดียว
        /// ก็ทำให้สัตว์ตกใจได้จากระยะเต็ม FleeRadius. ของใหม่: **ระยะโตตามความเร็ว** เหมือนที่ฝูงปลา
        /// ได้ไปเมื่อ 21 ส.ค. (FleeMath.StartleRadius) — ช้า = ต้องถึงตัว · เต็มสปีด = ระยะเดิมเป๊ะ
        /// </summary>
        [Test]
        public void StartleRadius_GrowsWithDiverSpeed()
        {
            const double r = 10;
            double contact = r + AnimalSolids.DiverClearance;
            double reach = FleeMath.FleeRadius(r);           // ระยะเดิม = ระยะตอนพุ่งเต็มสปีด
            double crawl = FleeMath.DiverPanicSpeed * 0.2;   // ต่ำกว่าเกณฑ์
            double charge = DroneFlight.Speed;               // คันเร่งเต็ม

            // คลานเข้าไป: ไม่รู้สึกอะไรจนกว่าจะถึงตัว — นี่คือสิ่งที่ user ขอ
            Assert.IsFalse(AnimalSolids.DiverStartles(crawl, contact + 1, r));
            Assert.IsTrue(AnimalSolids.DiverStartles(crawl, contact - 0.1, r),
                          "ของใหญ่มาแตะตัวต้องหลบเสมอ — กฎ 15 ส.ค. ของ user ยังอยู่");

            // พุ่งเต็มสปีด: ระยะเดิมทุกประการ ของที่เคยดีต้องไม่เสีย
            Assert.IsTrue(AnimalSolids.DiverStartles(charge, reach - 1, r));
            Assert.IsFalse(AnimalSolids.DiverStartles(charge, reach + 1, r));

            // และมันต้องไล่ระดับจริง ไม่ใช่กระโดดสองขั้น
            double mid = AnimalSolids.DiverStartleRadius(r, (FleeMath.DiverPanicSpeed + charge) * 0.5);
            Assert.Greater(mid, contact);
            Assert.Less(mid, reach);

            // ปลาเล็กไม่ใช้ทางนี้ (ระบบฝูงดูแลอยู่แล้ว)
            Assert.IsFalse(AnimalSolids.DiverStartles(charge, 1, AnimalSolids.MinBlockingRadius - 0.1));
        }

        /// <summary>แรงหนีก็ต้องไล่ระดับด้วย ไม่ใช่แค่ระยะ — ไม่งั้นแตะตัวทีเดียวก็สปรินต์เต็มแรง.</summary>
        [Test]
        public void DiverFleeSprint_IsGentleWhenTheDroneCrawls()
        {
            const double r = 10;
            double reach = FleeMath.FleeRadius(r);
            double full = FleeMath.FleeSprint(2, reach);

            double crawl = FleeMath.DiverFleeSprint(2, reach, FleeMath.DiverPanicSpeed * 0.2);
            double charge = FleeMath.DiverFleeSprint(2, reach, DroneFlight.Speed);

            Assert.AreEqual(full, charge, 1e-9, "พุ่งเต็มสปีด = พฤติกรรมเดิม");
            Assert.Less(crawl, full, "ลอยเข้าไปช้า ๆ ต้องได้แค่สะบัดตัวหลบ");
            Assert.Greater(crawl, 1.0, "…แต่ยังต้องขยับหนีจริง ไม่ใช่ยืนเฉย");

            // ผู้ล่ายังหนีเต็มแรงเสมอ — ทางเดิมไม่ถูกแตะ
            Assert.AreEqual(full, FleeMath.FleeSprint(2, reach), 1e-9);
        }
    }
}
