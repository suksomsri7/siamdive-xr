# WO — AR / Holomap (PARITY หมวด F, 4 ข้อ)

> 🔴 **สถานะ: ยังไม่เริ่ม — user สั่งพักไว้ (31 ก.ค. 2026)**
> เอกสารนี้เขียนตอนที่ยังอ่านโค้ดเว็บสดๆ อยู่ในหัว เพื่อให้รอบหน้าหยิบทำต่อได้โดยไม่ต้องไล่ใหม่
> ทุกบรรทัดที่อ้างเว็บมี `ไฟล์:บรรทัด` กำกับ (กฎที่เซสชันนี้ตั้งไว้หลังเจอบันทึกจดผิด 5 ครั้ง)

---

## ⚠️ อ่านก่อน: ทำไมหมวดนี้ต่างจากทุกหมวด

**QC headless พิสูจน์หมวดนี้ไม่ได้เลย** — ไม่มีกล้อง ไม่มีไจโร ไม่มี ARCore บน xvfb
ทุกหมวดอื่นในเซสชันนี้ปิดได้เพราะ CI ถ่ายภาพ + วัดตัวเลขได้ · หมวดนี้ทำไม่ได้

→ **แผน QC ที่ต้องใช้แทน**
1. แยกคณิตศาสตร์ออกเป็น Core บริสุทธิ์ (แปลงไจโร→quaternion, สเกลพอดีฝ่ามือ, วางข้างหน้า)
   แล้วเทสด้วย EditMode — ส่วนนี้พิสูจน์ได้ 100% และเป็นส่วนที่พังเงียบที่สุด
2. ส่วนที่เหลือ (กล้อง/permission/ARCore session) **ต้องเทสบนเครื่องจริงเท่านั้น**
   → ตอนรายงานต้องเขียนแยกว่า "ส่วนนี้ไม่มีหลักฐาน" อย่าเหมารวมว่าเสร็จ

---

## F1 · AR วางแมพในกล้อง — `enterAR()` builder.html:2923

### เว็บทำอะไร (ไล่ทีละบรรทัด)
```js
// 1. ขอสิทธิ์ไจโร (iOS 13+ ต้องขอ, Android มีเลย)
if (DeviceOrientationEvent.requestPermission) gyroOn = (await …()) === 'granted';
else gyroOn = ('DeviceOrientationEvent' in window);

// 2. เปิดกล้องหลัง — ถ้าไม่ได้ ยกเลิกทั้งโหมด (ไม่มี AR ที่ไม่มีกล้อง)
camStream = await navigator.mediaDevices.getUserMedia({video:{facingMode:'environment'}});
// error → alert('AR ต้องใช้กล้องและ HTTPS') แล้ว return

// 3. ยก "ทุกวัตถุ + ทุกหมุด" เข้ากลุ่มเดียว พร้อม **สำรองท่าเดิมไว้ทุกชิ้น**
arBackup = items.map(o => ({o, p:o.position.clone(), q:o.quaternion.clone(), s:o.scale.clone()}));
arSite = new THREE.Group(); items.forEach(o => arSite.add(o));

// 4. ย่อทั้งไซต์ให้กว้างสุด = 1.1 เมตร แล้ววางห่างหน้าคน 1.4 ม. ฐานอยู่ต่ำกว่าตา 0.3 ม.
const sc = 1.1 / (Math.max(size.x, size.z) || 100);
arSite.position.set(-ctr.x*sc, -box.min.y*sc - 0.3, -ctr.z*sc - 1.4);

// 5. ปิดพื้น/ผิวน้ำ/ฉากหลัง/หมอก + ทำพื้นหลังโปร่งใส → เห็นภาพกล้องทะลุ
seabed.visible=false; surf.visible=false; scene.background=null; scene.fog=null;
renderer.setClearAlpha(0);

// 6. กล้องไปที่ origin · มีไจโร → ปิด orbit ; ไม่มีไจโร → orbit แทน (min 0.1 max 30)
```

### ที่ต้องระวังตอนพอร์ต Unity
| จุด | เหตุผล |
|---|---|
| **สำรองท่าเดิมทุกชิ้นก่อนย้ายเข้ากลุ่ม** | ออกจาก AR ต้องคืนได้เป๊ะ · ถ้าไม่สำรอง แมพจะเพี้ยนถาวรหลังออก 1 ครั้ง |
| **สเกล 1.1 ม. ไม่ใช่ค่าคงที่ในโค้ด** | มันคือ "ขนาดที่พอดีโต๊ะ" — แมพ 340u กับแมพ 60u ต้องได้ขนาดเท่ากันบนโต๊ะ |
| **`|| 100` ใน `Math.max(size.x,size.z)||100`** | กันหารศูนย์ตอนแมพว่าง — อย่าตัดทิ้ง |
| Unity ใช้ **ARCore/ARFoundation** ไม่ใช่ getUserMedia | ต้องเพิ่ม package + ตั้ง `ProjectSettings` (ซึ่ง**ไม่อยู่ใน git** — ดู IMPROVEMENTS A2 ก่อน) |
| ฉากหลังโปร่งใส | Unity = `cam.clearFlags = SolidColor` + alpha 0 และต้องปิด `Backdrop` (WO-04.2) ด้วย |

---

## F2 · Holomap ไจโร — `applyGyro()` builder.html:2921

```js
const deg = Math.PI/180;
const zee = (0,0,1);
const q1  = quaternion(-√½, 0, 0, √½);        // −90° รอบแกน X: จาก "อุปกรณ์" เป็น "โลก"
eul.set(beta*deg, alpha*deg, -gamma*deg, 'YXZ');   // ⚠️ ลำดับ YXZ และ gamma ติดลบ
camera.quaternion.setFromEuler(eul).multiply(q1).multiply(qtmp.setFromAxisAngle(zee, -orient));
//                                                                                  ↑ orient = screen.orientation.angle
```

**นี่คือส่วนที่ควรทำเป็น Core บริสุทธิ์ + เทส** — สูตรนี้พังเงียบมาก:
- ลำดับ Euler ผิด (`YXZ` ไม่ใช่ `XYZ`) → หมุนถูกตอนถือตรง เพี้ยนตอนเอียง
- ลืมเครื่องหมายลบหน้า `gamma` → ซ้าย-ขวากลับด้าน
- ลืม `-orient` → พอหมุนจอเป็นแนวนอน โลกเอียง 90°

**เทสที่ควรเขียน** (ทั้งหมดเป็นเลขล้วน ไม่ต้องมีเครื่อง):
1. alpha/beta/gamma = 0 → กล้องมองไปทิศไหน (ต้องคงที่และซ้ำได้)
2. หมุน alpha 90° → yaw เปลี่ยน 90° · beta/gamma ไม่ขยับ
3. เอียง gamma +45° กับ −45° → ต้องได้ผลลัพธ์กลับด้านกันพอดี
4. `orient` 0/90/180/270 → โลกต้องตั้งตรงทุกกรณี
5. ค่า null/NaN จากเซนเซอร์ → ต้องไม่ทำ quaternion กลายเป็น NaN (จอจะดำค้าง)

---

## F3 · วางโฮโลแกรมข้างหน้าตัว — `placeHoloInFront()`

```js
const dir = camera.getWorldDirection();
if (dir.lengthSq() < 0.001) dir.set(0,0,-1);      // ⚠️ กันเวกเตอร์ศูนย์
holoGroup.position.set(dir.x*1.6, dir.y*1.6 - 0.35, dir.z*1.6);
```
1.6 ม. ข้างหน้า · ต่ำกว่าแนวตา 0.35 ม. (วางบนโต๊ะ ไม่ใช่ลอยกลางหน้า)

ปุ่มที่คู่กัน (`hbWater/hbSpin/hbMinus/hbPlus/hbMove`):
- `hbWater` เปิด/ปิดผิวน้ำ · `hbSpin` หมุนอัตโนมัติ
- `hbMinus` ×0.85 · `hbPlus` ×1.18 — **ไม่สมมาตรโดยตั้งใจ** (0.85×1.18 ≈ 1.003)
- ท่าทาง: 1 นิ้ว = หมุนแมพ · 2 นิ้ว = pinch zoom

---

## F4 · แถบควบคุม AR — `#arctl` builder.html:97

```css
#arctl{ position:fixed; z-index:14; left:50%;
        bottom: calc(28px + env(safe-area-inset-bottom)); transform:translateX(-50%); }
body.ar #arhint, body.ar #arctl { display:flex }          /* :101 */
body.holo #arctl { display:none !important }              /* :189 — โหมด holo ใช้แถบของตัวเอง */
```
`[− ] ขนาด [＋]` + `#exitAR` + `#arhint`

⚠️ **`body.holo` ซ่อน `#arctl`** → AR กับ Holomap เป็นคนละโหมด มีแถบคนละอัน
อย่าทำเป็นแถบเดียวใช้ร่วมกัน

---

## 🐞 บั๊กที่เว็บเคยเจอในหมวดนี้ (จาก changelog v.0733)

> *"auto-tour ไม่แทรก AR/holomap อีก — เติม `!HOLO_MODE` ใน gate (v.0731 ลืม ทำให้เปิดโลกเกม
> ใน AR ได้ `body holostart+view+tour` พร้อมกัน จอย/HUD ทับจอ AR)"*

**บทเรียนตรงกับที่เซสชันนี้เจอ 2 ครั้ง** (เหรียญซ้อนใน palette · แถบเครื่องมือทับมินิแมพ):
ทุกครั้งที่เพิ่มโหมดใหม่ ต้องไล่ว่าโหมดเดิม**ทั้งหมด**ถูกกันออกหรือยัง
→ ใน Unity: `ModeManager` ต้องมี `AppMode.Ar` และ `ModeRules` ต้องปฏิเสธ Tour↔Ar

---

## ลำดับที่แนะนำ

1. **Core/GyroMath.cs + เทส** (F2 ครึ่งที่พิสูจน์ได้) — ทำได้เลยไม่ต้องมีเครื่อง
2. **Core/HoloPlacement.cs + เทส** (สเกลพอดีฝ่ามือ + วางข้างหน้า จาก F1/F3)
3. `AppMode.Ar` + `ModeRules` + ซ่อน HUD ทุกตัว (ดูบั๊ก v.0733 ข้างบน)
4. ARFoundation + ProjectSettings — ⚠️ **แก้ IMPROVEMENTS A2 ก่อน** (ProjectSettings ไม่อยู่ใน git
   → ตั้งค่า AR แล้วจะหายทุก build)
5. แถบควบคุม F4
6. **ส่งเครื่องจริงเทส** — ไม่มีทางลัด

**ประเมิน**: ข้อ 1-2 ~2 ชม. (พิสูจน์ได้ครบ) · ข้อ 3-5 ~1-2 วัน · ข้อ 6 ขึ้นกับ user
