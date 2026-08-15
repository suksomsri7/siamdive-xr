using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// โหมดที่แอปเจ้าบ้านสั่งมา (WO-PIVOT, 14 ส.ค. 2026).
    ///
    /// สิ่งที่เทสชุดนี้ตรึงไว้คือกฎข้อเดียวที่พังแล้วผู้ใช้เห็นทันที: **คำสั่งของเจ้าบ้านชนะ
    /// auto-play** — คนที่แตะหมุดแล้วเลือก "ดูแมพ" ต้องได้ดูแมพ ไม่ใช่เห็นแมพครึ่งวินาทีแล้ว
    /// ถูกโยนเข้าทัวร์โดรนเพราะเงื่อนไขของ ArenaEntry บังเอิญเป็นจริง
    /// </summary>
    public class BootModeTests
    {
        [Test]
        public void TheThreeModesTheAppOffersAreUnderstood()
        {
            Assert.AreEqual(BootMode.Requested.Preview, BootMode.Parse("preview"));
            Assert.AreEqual(BootMode.Requested.Ar, BootMode.Parse("ar"));
            Assert.AreEqual(BootMode.Requested.Tour, BootMode.Parse("tour"));
        }

        [Test]
        public void SynonymsAndCasingFromTheJsSideAreAccepted()
        {
            // ฝั่ง RN/เว็บเรียกของเดียวกันคนละชื่อมาตลอด — ยอมรับทั้งคู่ดีกว่าให้ผู้ใช้เจอจอที่
            // ไม่ตรงกับปุ่มที่กด เพราะสะกดไม่ตรงตัวเดียว
            Assert.AreEqual(BootMode.Requested.Preview, BootMode.Parse("view"));
            Assert.AreEqual(BootMode.Requested.Ar, BootMode.Parse("HoloMap"));
            Assert.AreEqual(BootMode.Requested.Tour, BootMode.Parse("  Drone "));
            Assert.AreEqual(BootMode.Requested.Game, BootMode.Parse("play"));
        }

        [Test]
        public void AnythingUnknownMeansDoNothing()
        {
            // ห้ามมีค่าเริ่มต้นเป็นโหมดใดโหมดหนึ่ง: การเดาผิด = พาผู้ใช้ไปโหมดที่ไม่ได้ขอ
            Assert.AreEqual(BootMode.Requested.None, BootMode.Parse(""));
            Assert.AreEqual(BootMode.Requested.None, BootMode.Parse(null));
            Assert.AreEqual(BootMode.Requested.None, BootMode.Parse("ทัวร์"));
            Assert.AreEqual(BootMode.Requested.None, BootMode.Parse("edit"));   // แก้ไข = ของเว็บแล้ว
        }

        [Test]
        public void AHostRequestBeatsAutoPlay()
        {
            // นี่คือกฎทั้งหมดของ WO นี้
            Assert.IsTrue(BootMode.OverridesAutoPlay(BootMode.Requested.Preview));
            Assert.AreEqual(BootMode.Requested.Preview,
                            BootMode.Resolve(BootMode.Requested.Preview, autoPlay: true));
            Assert.AreEqual(BootMode.Requested.Ar,
                            BootMode.Resolve(BootMode.Requested.Ar, autoPlay: true));
        }

        [Test]
        public void WithNoHostRequestNothingChanges()
        {
            // บิลด์เดี่ยว: ไม่มีใครส่งข้อความนี้ → พฤติกรรมเดิมทุกอย่าง รวมถึง auto-play เดิม
            Assert.IsFalse(BootMode.OverridesAutoPlay(BootMode.Requested.None));
            Assert.AreEqual(BootMode.Requested.Tour,
                            BootMode.Resolve(BootMode.Requested.None, autoPlay: true));
            Assert.AreEqual(BootMode.Requested.None,
                            BootMode.Resolve(BootMode.Requested.None, autoPlay: false));
        }
    }
}
