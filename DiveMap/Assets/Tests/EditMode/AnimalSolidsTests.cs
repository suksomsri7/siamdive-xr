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

        [Test]
        public void StartleNeedsSpeed_ExceptOnContact()
        {
            double slow = FleeMath.DiverPanicSpeed * 0.2;
            double fast = FleeMath.DiverPanicSpeed * 1.5;
            const double r = 10;

            // ไกลแต่มาเร็ว = ตกใจ · ไกลและมาช้า = ไม่ตกใจ (ไม่งั้นสัตว์จะวิ่งหนีตลอดเวลา)
            Assert.IsTrue(AnimalSolids.DiverStartles(fast, 40, r));
            Assert.IsFalse(AnimalSolids.DiverStartles(slow, 40, r));

            // แตะตัวกันแล้วตกใจเสมอ ไม่ว่าจะเข้ามาช้าแค่ไหน
            Assert.IsTrue(AnimalSolids.DiverStartles(slow, r, r));

            // พ้นรัศมีรับรู้ = ไม่รู้เรื่องเลย แม้จะพุ่งมาเร็ว
            Assert.IsFalse(AnimalSolids.DiverStartles(fast, FleeMath.FleeRadius(r) + 1, r));

            // ปลาเล็กไม่ใช้ทางนี้ (ระบบฝูงดูแลอยู่แล้ว)
            Assert.IsFalse(AnimalSolids.DiverStartles(fast, 1, AnimalSolids.MinBlockingRadius - 0.1));
        }
    }
}
