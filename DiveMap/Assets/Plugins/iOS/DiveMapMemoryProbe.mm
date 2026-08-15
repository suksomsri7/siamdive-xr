// อ่าน "แรมที่ระบบใช้ตัดสินว่าจะฆ่าแอปนี้ไหม" — เครื่องมือของคดี "แอปดับตอนสลับแมพ" (14 ส.ค. 2026)
//
// ทำไมต้องเป็นโค้ดเนทีฟ ไม่ใช่ตัวเลขที่ Unity มีอยู่แล้ว:
//
//   • Profiler.GetTotalAllocatedMemoryLong() คืน 0 ใน release build (ต้องมี ENABLE_PROFILER) —
//     บิลด์ที่ user เล่นคือ release เสมอ ดังนั้นเลขนั้นตอบคำถามนี้ไม่ได้เลย
//   • GC.GetTotalMemory() เห็นแค่ mono heap ซึ่งเป็นเศษเสี้ยว: mesh/texture ของแมพอยู่ในหน่วยความจำ
//     เนทีฟทั้งหมด และนั่นคือก้อนที่โตขึ้นตอนสลับแมพ
//
// สองเลขที่คืนกลับไป:
//
//   phys_footprint  = ก้อนที่ jetsam (ตัวเก็บกวาดของ iOS) ใช้ตัดสินใจจริง ๆ ไม่ใช่ resident size
//   os_proc_available_memory() = เหลืออีกเท่าไรก่อนโดนฆ่า (iOS 13+) — เลขนี้ตรงคำถามที่สุด
//     เพราะเพดานของแต่ละเครื่อง/แต่ละสถานการณ์ไม่เท่ากัน การรู้ว่า "ใช้ไป 900MB" ไม่ได้บอกว่า
//     ใกล้ตายหรือยัง แต่ "เหลือ 80MB" บอกทันที
//
// ทั้งคู่คืน -1 เมื่ออ่านไม่ได้ ฝั่ง C# จะตกไปใช้ตัวเลข mono แทน — ไม่มีทางที่จอจะว่างเปล่า
// เพราะเครื่องมือวัดตัวเองมีปัญหา (บทเรียนซ้ำของโปรเจกต์นี้: เครื่องมือที่เงียบ = เครื่องมือที่โกหก)

#import <mach/mach.h>
#import <os/proc.h>

extern "C" long dm_memFootprintBytes(void)
{
    task_vm_info_data_t info;
    mach_msg_type_number_t count = TASK_VM_INFO_COUNT;
    kern_return_t kr = task_info(mach_task_self(), TASK_VM_INFO, (task_info_t)&info, &count);
    if (kr != KERN_SUCCESS) return -1;
    return (long)info.phys_footprint;
}

extern "C" long dm_memAvailableBytes(void)
{
    if (@available(iOS 13.0, *)) {
        return (long)os_proc_available_memory();
    }
    return -1;
}
