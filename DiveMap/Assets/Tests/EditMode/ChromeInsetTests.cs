using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// ปุ่มที่เกาะขอบบนต้องไม่ไปนั่งใต้แถบสถานะเมื่อเป็นจอในแอปอื่น.
    ///
    /// อาการที่ทำให้ต้องมีไฟล์นี้: user รายงานว่า "ปุ่ม x ในโหมด AR อยู่สูงไปคลิกไม่ได้" —
    /// ปุ่มอยู่ตรงที่โค้ดสั่งทุกประการ แต่ตรงนั้นคือนาฬิกาของ iOS เพราะในโหมดฝังตัวเราตั้ง
    /// พื้นที่ปลอดภัยเป็นเต็มจอโดยตั้งใจ ⇒ กฎ "ใครหักระยะขอบบน" ย้ายมาอยู่ที่นี่ ที่ซึ่ง
    /// พิสูจน์ได้โดยไม่ต้องบิลด์และไม่ต้องถ่ายรูปหน้าจอ
    /// </summary>
    public class ChromeInsetTests
    {
        [Test]
        public void TheStandaloneBuildNeverInsetsTwice()
        {
            // ที่นั่น Screen.safeArea เชื่อถือได้และถูกหักไปแล้วใน UiShell — หักซ้ำ = ปุ่มลอยกลางจอ
            Assert.AreEqual(0f, ChromeInset.Top(embeddedInHost: false, portrait: true));
            Assert.AreEqual(0f, ChromeInset.Top(embeddedInHost: false, portrait: false));
        }

        [Test]
        public void EmbeddedPortraitClearsTheStatusBar()
        {
            // AR เป็นแนวตั้ง และ 44 pt คือความสูงแถบสถานะของเครื่องที่มีรอยบาก
            Assert.GreaterOrEqual(ChromeInset.Top(embeddedInHost: true, portrait: true), 44f);
        }

        [Test]
        public void EmbeddedLandscapeStaysNearTheEdge()
        {
            // แนวนอนแถบสถานะเกือบไม่มี — เว้นเท่าแนวตั้งจะดันปุ่มลงมาโดยไม่มีเหตุผล
            float landscape = ChromeInset.Top(embeddedInHost: true, portrait: false);
            Assert.Greater(landscape, 0f);
            Assert.Less(landscape, ChromeInset.Top(embeddedInHost: true, portrait: true));
        }
    }
}
