using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// การแก้แมพย้ายไปอยู่บนเว็บ (WO-PIVOT, 14 ส.ค. 2026).
    ///
    /// สองข้อที่ต้องไม่พังพร้อมกัน: แอปรวมต้องแก้ไม่ได้เลย **และ** บิลด์เดี่ยวต้องแก้ได้ครบ
    /// เหมือนเดิม — ไม่ใช่เรื่องความสะดวก แต่เป็นเพราะด่านตรวจภาพของ CI ทั้งชุด (ลากลูกศร ·
    /// ตะกร้า · ปั้นพื้น) กดปุ่มเหล่านั้นจริง ถ้าปิดหมดทั้งสองผลิตภัณฑ์ เครื่องมือ QC ตายยกชุด
    /// </summary>
    public class EditAccessTests
    {
        [Test]
        public void EmbeddedInTheAppNeverEdits()
        {
            Assert.IsFalse(EditAccess.MapEditingAllowed(serverCanEdit: true, embeddedInHost: true));
            Assert.IsFalse(EditAccess.ShowsEditTools(embeddedInHost: true));
        }

        [Test]
        public void TheStandaloneBuildIsUntouched()
        {
            Assert.IsTrue(EditAccess.MapEditingAllowed(serverCanEdit: true, embeddedInHost: false));
            Assert.IsTrue(EditAccess.ShowsEditTools(embeddedInHost: false));
        }

        [Test]
        public void ItOnlyEverTakesRightsAwayNeverGrantsThem()
        {
            // เซิร์ฟเวอร์ยังเป็นความจริงเดียวเรื่องสิทธิ์ — ตัวนี้ "ปิดเพิ่ม" ได้อย่างเดียว
            Assert.IsFalse(EditAccess.MapEditingAllowed(serverCanEdit: false, embeddedInHost: false));
            Assert.IsFalse(EditAccess.MapEditingAllowed(serverCanEdit: false, embeddedInHost: true));
        }
    }
}
