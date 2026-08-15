using System.Runtime.InteropServices;
using UnityEngine;

namespace DiveMap.Runtime
{
    /// <summary>
    /// อ่านแรมจากระบบ (iOS) ให้ป้ายมุมจอ — คู่กับ <see cref="MemoryWatch"/> ที่คอยตอบคำเตือน
    /// ตัวนี้ไม่ตัดสินใจอะไรทั้งสิ้น หน้าที่เดียวคือ "บอกตัวเลขที่จริง"
    ///
    /// 🔴 Guard เป็น <c>UNITY_IOS &amp;&amp; !UNITY_EDITOR</c> ตามแบบเดียวกับ <see cref="NativeBridge"/>:
    /// ใน Editor ที่เลือกแพลตฟอร์ม iOS อยู่ DllImport จะไปหา symbol ในโปรเซสของ Editor ซึ่งไม่มี
    /// แล้วพังทั้ง play session ตอนกำลังเขียนโค้ดอยู่
    ///
    /// อ่านทุก <see cref="IntervalSeconds"/> ไม่ใช่ทุกเฟรม — ค่าที่กระพริบทุก 16ms อ่านจากรูปถ่าย
    /// ไม่ได้ ซึ่งผิดวัตถุประสงค์ทั้งหมดของเครื่องมือนี้ (และ task_info เป็น syscall ไม่ใช่ของฟรี)
    /// </summary>
    public static class MemoryMeter
    {
        /// <summary>ครึ่งวินาที เท่ากับจังหวะที่เลข fps อัปเดต — สองเลขบนบรรทัดเดียวกันจึงนิ่งพร้อมกัน.</summary>
        public const float IntervalSeconds = 0.5f;

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern long dm_memFootprintBytes();

        [DllImport("__Internal")]
        private static extern long dm_memAvailableBytes();
#endif

        private static float _nextAt = -1f;
        private static long _footprint = -1, _available = -1, _mono;
        private static string _line = "";

        /// <summary>แรมที่แอปใช้อยู่ (ไบต์) หรือ -1 ถ้าอ่านไม่ได้บนแพลตฟอร์มนี้.</summary>
        public static long FootprintBytes => _footprint;

        /// <summary>เหลือให้ใช้อีกกี่ไบต์ก่อนโดนระบบฆ่า หรือ -1.</summary>
        public static long AvailableBytes => _available;

        /// <summary>ข้อความพร้อมวาด (อัปเดตตามจังหวะข้างบน).</summary>
        public static string Line
        {
            get
            {
                Sample();
                return _line;
            }
        }

        public static Core.MemoryReading.Pressure Pressure => Core.MemoryReading.PressureOf(_available);

        private static void Sample()
        {
            float now = Time.unscaledTime;
            if (_nextAt > 0f && now < _nextAt) return;
            _nextAt = now + IntervalSeconds;

#if UNITY_IOS && !UNITY_EDITOR
            _footprint = dm_memFootprintBytes();
            _available = dm_memAvailableBytes();
#endif
            _mono = System.GC.GetTotalMemory(false);
            _line = Core.MemoryReading.Format(_footprint, _available, _mono);
        }
    }
}
