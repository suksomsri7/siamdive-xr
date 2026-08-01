# WO — AR / Holomap (PARITY หมวด F, 4 ข้อ)

> ## สถานะ (31 ก.ค. 2026)
> | | | |
> |---|---|---|
> | **F1 AR วางแมพในกล้อง** | ✅ **ทำแล้ว** | `Runtime/ArSession.cs` + `Core/ArPlacement.cs` (16 เทส) |
> | **F2 ไจโร** | ⚠️ ครึ่ง | `Core/GyroMath.cs` (14 เทส) ใช้กับ AR แล้ว · หน้า `holostart` ยังไม่ทำ |
> | **F3 โหมด holomap** | 🔴 **พักไว้ — user สั่ง** | ยังไม่เริ่ม |
> | **F4 แถบควบคุม AR** | ✅ **ทำแล้ว** | `Ui/ArControls.cs` |
>
> user สั่ง 2 รอบ: *"Holomap ยังไม่ต้องทำ แต่บันทึกไว้ก่อน"* แล้ว *"AR บนมือถือต้องทำนะครับ"*
> → ทำ **AR (F1+F4)** · พัก **holomap (F3 + ครึ่งของ F2)**
>
> ⚠️ **ยังไม่มีใครรัน AR บนเครื่องจริงเลย** — CI พิสูจน์ได้แค่คณิตศาสตร์กับการเข้า/ออกโหมด
> ข้อ "ตรวจบนเครื่อง" ท้ายไฟล์คือสิ่งที่ต้องทำก่อนเชื่อว่ามันใช้ได้
>
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

---

# ✅ ที่ทำไปแล้ว (AR) — และสิ่งที่ต่างจากเว็บโดยตั้งใจ

## 1. ย้ายกล้อง ไม่ย่อแมพ — `Core/ArPlacement.cs`

เว็บย่อทั้งไซต์แล้วแขวนหน้าคน พอร์ตนี้ทำตรงข้าม: **แมพอยู่เฉยๆ กล้องถอยออกไปแทน**

**ทำไม** — วาฬกับฝูงปลาในพอร์ตนี้ไม่ได้อยู่ใน local space ของแมพ:
`WhaleController` เขียน `transform.position` และ `FishSchoolSystem` ป้อน world matrix
เข้า `RenderMeshInstanced` ถ้าย่อ root ลง วาฬจะยังตัวเท่าเดิม ว่ายทะลุโต๊ะออกไปทั้งห้อง
— บั๊กที่โผล่เฉพาะบนมือถือ ซึ่งเป็นที่เดียวที่โปรเจกต์นี้รันเทสไม่ได้

**ทำไมภาพเหมือนกัน** — การย่อโลกรอบดวงตา = การถอยกล้อง กล้อง perspective แยกไม่ออก
เว็บส่งจุด p ไปที่ `s·(p−ctr) + T` โดยตาอยู่ที่ origin หารด้วย s (ซึ่งไม่มีการฉายภาพไหนเห็น)
ได้ `p − (ctr − T/s)` = ตาอยู่ที่ `ctr − T/s` แมพไม่ขยับ
เทส `SameEyeRayAsTheWeb` เช็คทีละจุด (มุมไกล/กลางพื้น/ปลากลางน้ำ) ว่าทิศจากตาตรงกันทั้งสองสูตร

**ผลพลอยได้**: ออกจาก AR = คืน pose กล้องอันเดียว ไม่ต้องมี `arBackup` ต่อวัตถุแบบเว็บ
(เว็บต้องมีเพราะวัตถุมันวางลอยอยู่ใน scene ไม่มี root)

## 2. เครื่องหมายเทอมหมุนจอ — พิสูจน์ได้ headless (นึกว่าต้องใช้เครื่อง)

ตอนแรกจดไว้ว่า "เครื่องหมาย 2 ตัวต้องใช้มือถือเทสเท่านั้น" **ผิดไปหนึ่งตัว**
พอเขียนโจทย์ใหม่เป็น *"ผู้ใช้หมุนเครื่อง θ แล้วจอรายงาน θ — สองอย่างต้องหักล้างกันเป็นศูนย์"*
มันกลายเป็นข้อที่เช็คได้ทันที และ**มีไซน์เดียวที่ผ่าน** (อีกอันให้ error 2.0 = โลกกลับหัวสนิท)

เหลือ handedness ตัวเดียวที่ต้องใช้เซนเซอร์จริง เพราะไม่มีอะไรในเครื่องนี้รู้ว่าฮาร์ดแวร์
เรียกทิศไหนว่า "ขวา" → **ถ้าภาพกลับซ้าย-ขวาบนเครื่อง แก้ที่ negation เดียวใน `GyroMath.ToUnity`**
ที่เหลือถูกล็อกด้วยเทสหมดแล้ว

บทเรียนกว้างกว่านั้น: *"พิสูจน์ไม่ได้" หลายครั้งแปลว่า "ยังไม่ได้เขียนโจทย์ให้ถูก"*

## 3. ไม่ใช้ ARCore (ตั้งใจ)

เว็บใช้ฟีดกล้องเปล่า + เซนเซอร์ทิศ ไม่มี plane detection — พอร์ตนี้ทำตามนั้น
ARCore จะได้ตรวจจับระนาบจริงกับการขยับตัว (translation) แต่ต้องตั้งค่า XR ใน `ProjectSettings/`
ซึ่ง **repo นี้ไม่ได้ track โฟลเดอร์นั้น** (IMPROVEMENTS A2) → จะใช้ได้แค่บนเครื่องที่ตั้งค่าไว้
แล้วเงียบๆ ตกกลับเป็นไม่มีอะไรบนเครื่องอื่น จดเป็นทางอัปเกรด ไม่ลักไก่ใส่

## 4. เพิ่มจากเว็บ

- **ปุ่ม −/+ หรี่เมื่อสุดระยะ** — เว็บกด − 11 ครั้งแมพเหลือ 8 ซม. โดยไม่มีอะไรบอกว่าปุ่มไหนพากลับ
- **ไม่มีกล้องก็ยังดูได้** — เว็บ `alert()` แล้วถอยออกทั้งโหมด อันนี้แสดงแบบจำลองบนพื้นดำต่อ
- **ไม่มีไจโรก็ยังใช้ได้** — ตกกลับเป็นลากนิ้วหมุน (เว็บก็ทำ แต่ไม่บอกผู้ใช้ว่าเกิดอะไรขึ้น)

---

# 🔴 ต้องตรวจบนเครื่องจริง (ยังไม่มีหลักฐานใดๆ)

CI ตรวจให้แล้ว: เข้า/ออกโหมด · ตำแหน่งตา · zoom หยุดที่ขอบ · **คืนฉากครบทุกอย่างที่ยืมไป**
(fog / near / far / pose กล้อง / seabed / แถบควบคุม) — ข้อสุดท้ายคือข้อที่เน่าเงียบที่สุด
เพราะถ้าลืมคืน อาการจะไปโผล่ทีหลังในหน้าอื่นจนไม่มีใครโยงกลับมาที่ AR

**ที่ CI พิสูจน์ไม่ได้เลย — ต้องลง APK:**

| # | ตรวจอะไร | ถ้าผิดต้องแก้ตรงไหน |
|---|---|---|
| 1 | ขออนุญาตกล้องแล้วเห็นภาพห้องจริง | `ArSession.StartCameraFeed` · สิทธิ์ถูกแทรกโดย `Editor/AndroidCameraPermission.cs` แล้ว (ตรวจได้ใน log build: `[Build] android manifest: camera permission …`) |
| 2 | **หันขวาแล้วภาพไปขวา** (ไม่ใช่ซ้าย) | `GyroMath.ToUnity` — negation เดียว |
| 3 | หมุนเครื่องเป็นแนวนอนแล้วเส้นขอบฟ้ายังราบ | เทสล็อกไว้แล้ว ถ้าพลาดแปลว่า `Screen.orientation` รายงานไม่ตรง |
| 4 | ภาพจากกล้องไม่เอียง 90°/กลับหัว | `ArSession.FitFeed` (`videoRotationAngle` / `videoVerticallyMirrored`) |
| 5 | แมพดูขนาดพอดีโต๊ะ (~1.1 ม.) ไม่ใช่เท่าห้อง | `ArPlacement.TableSpan` |
| 6 | เฟรมเรตพอใช้ (ฟีดกล้อง + ฉากเต็ม) | ถ้าตก อาจต้องลดความละเอียดฟีด |
| 7 | ออกจาก AR แล้วหน้าแมพเหมือนเดิมเป๊ะ | CI ตรวจแล้ว แต่ยืนยันด้วยตาอีกที |

---

# ลำดับงานที่เหลือ (holomap)

1. **`Core/HoloPlacement.cs` + เทส** — `placeHoloInFront` (1.6 ม. หน้า, ต่ำกว่าตา 0.35 ม.) + สเกล 0.85/1.18
2. หน้า `holostart` ("🌐 holomap / แตะเพื่อเริ่ม") — จอเปล่าที่ต้องแตะก่อน เพราะ iOS
   ขอสิทธิ์เซนเซอร์ได้เฉพาะใน user gesture
3. `AppMode.Holo` + `ModeRules` — ⚠️ **ดูบั๊ก v.0733 ข้างบน** ต้องกันโหมดเดิมออกให้ครบ
4. แถบ `hbWater/hbSpin/hbMinus/hbPlus/hbMove` (คนละแถบกับ `#arctl` — `body.holo` ซ่อน `#arctl`)
5. ท่าทาง 1 นิ้ว=หมุนแมพ / 2 นิ้ว=pinch

**ประเมิน**: ข้อ 1 ~1 ชม. (พิสูจน์ได้ครบ) · ข้อ 2-5 ~1 วัน · เทสเครื่องจริงอีกรอบ

---

## ARKit — ก้าวที่ 1 เสร็จแล้ว, ก้าวที่ 2 ยังไม่เริ่ม (1 ส.ค. 2026)

user เคาะให้ทำ ARKit จริงหลังเทสบนไอโฟนแล้วพบว่า "ขยับเข้าออกไม่ได้ วางบนโต๊ะไม่ได้"
— ซึ่งถูกต้องตามที่ระบบเป็น: เซนเซอร์หมุนบอกได้แค่ว่าหันไปทางไหน ไม่รู้ว่าเครื่องอยู่ตรงไหน
และไม่รู้จักพื้นจริง

### ✅ ก้าวที่ 1 (commit 5df8d65) — ลงแพ็กเกจ + ตั้งค่า XR
EditMode tests เขียว = แพ็กเกจ resolve ได้และคอมไพล์ผ่านทุก job

- `com.unity.xr.arfoundation` / `com.unity.xr.arkit` **6.0.8** · `com.unity.xr.management` **4.7.0**
- เครื่อง build ไม่มี Unity Editor → เขียน asset เองทั้งหมด (กด UI ไม่ได้)
  - `Assets/XR/Loaders/ARKitLoader.asset`
  - `Assets/XR/Settings/XRManagerSettings-iOS.asset` (m_Loaders → ARKitLoader)
  - `Assets/XR/Settings/XRGeneralSettings-iOS.asset` (m_Manager → ตัวบน)
  - `Assets/XR/XRGeneralSettingsPerBuildTarget.asset` (buildTarget: **4** = iOS)
  - ลงทะเบียนใน `ProjectSettings/EditorBuildSettings.asset` →
    `m_configObjects: com.unity.xr.management.loader_settings`
- GUID สคริปต์ (ดึงจาก .meta ในแพ็กเกจจริง ห้ามพิมพ์เอง):
  `ARKitLoader a18c4d6661b404073b154020b9e2d993` ·
  `XRGeneralSettings d236b7d11115f2143951f1e14045df39` ·
  `XRManagerSettings f4c3631f5e58749a59194e0cf6baf6d5` ·
  `XRGeneralSettingsPerBuildTarget d2dc886499c26824283350fa532d087d`
- **`m_InitManagerOnStart = 0` และ `m_AutomaticLoading = 0` โดยตั้งใจ** — ถ้าให้ XR เริ่มเอง
  ตอนบูต กล้อง ARKit จะยึดกล้องหลักตั้งแต่หน้าแรก ทั้งที่แอปเป็นเกมใต้น้ำเกือบตลอดเวลา
  → ต้อง `XRGeneralSettings.Instance.Manager.InitializeLoaderSync()` เองตอนกดปุ่ม AR
  และ `DeinitializeLoader()` ตอนออก

### 🔜 ก้าวที่ 2 — ตัว AR จริง (ยังไม่เขียนโค้ด)
ตรวจ API จากแพ็กเกจ 6.0.8 มาแล้ว ไม่ต้องเดา:

- **`ARPoseDriver`** มีอยู่ใน `Runtime/ARFoundation/ARPoseDriver.cs` → ขับกล้องด้วยท่าทางจาก
  ARKit ได้โดย**ไม่ต้องลง Input System** (โปรเจกต์นี้ `activeInputHandler: 0` = Input Manager เดิม
  ถ้าไปใช้ TrackedPoseDriver ของ Input System จะต้องสลับ backend ทั้งโปรเจกต์)
- ชิ้นส่วนที่ต้องสร้างตอนกดปุ่ม AR: `ARSession` · `XROrigin` (จาก com.unity.xr.core-utils) ·
  กล้องหลักย้ายไปเป็นลูกของ XROrigin + `ARCameraManager` + `ARCameraBackground` + `ARPoseDriver` ·
  `ARPlaneManager` (ตรวจจับพื้น) · `ARRaycastManager` (แตะเพื่อวาง)
- **คณิตศาสตร์การวาง — อย่าย่อแมพ ย่อ XROrigin แทน** (กฎเดิมของโปรเจกต์: `WhaleController`
  เขียน world position และ `FishSchoolSystem` ป้อน world matrix เข้า RenderMeshInstanced
  ย่อ root = สัตว์ตัวเท่าเดิมทะลุโต๊ะ):
  - อยากให้แมพกว้าง `span` หน่วย ปรากฏกว้าง 1.1 เมตร → `s = span / 1.1`
  - ตั้ง `origin.localScale = s`, `origin.rotation` = yaw อย่างเดียว (ห้ามเอียง)
  - อยากให้จุดกลางแมพ `C` (world) ไปโผล่ที่จุดที่ผู้ใช้แตะ `P` (session space):
    **`origin.position = C - (origin.rotation * P) * s`**
- ปุ่ม/UX: reticle บอกว่าเจอพื้นแล้ว · แตะ = วาง · แตะซ้ำ = ย้าย · ปุ่มขนาดเดิมยังใช้ได้
  (เปลี่ยน `s`) · ถ้าเครื่องไม่รองรับ ARKit ให้ตกกลับไปทาง gyro เดิมทั้งดุ้น
- ⚠️ ของที่ CI พิสูจน์ไม่ได้เลย: กล้อง/พื้น/การเดิน — ต้องเทสบนเครื่องจริงเท่านั้น
  บรรทัดค่าเซนเซอร์บนจอ AR (ArControls.SetDiagnostics) ยังอยู่ ใช้ต่อได้เพื่อดูสถานะ ARKit

---

## 🔴 1 ส.ค. (เย็น) — เจอสาเหตุจริงที่ ARKit ไม่เคยสตาร์ท (อ่านก่อนแตะ ARKit อีก)

**ไล่ผิดชั้นมา 4 รอบ** ทุกความพยายามก่อนหน้านี้อยู่กับ "จะส่งไฟล์ตั้งค่า XR ให้ถึง player ยังไง"
(เขียน asset มือ → EditorBuildSettings → PreloadedAssets → สร้าง manager เองในโค้ด)
แต่ **ไฟล์ตั้งค่าไม่ใช่สิ่งที่เปิด ARKit** ตัวที่เปิดคือ scripting define ตัวเดียว:

### `UNITY_XR_ARKIT_LOADER_ENABLED` (ของ NamedBuildTarget `iPhone`)

หลักฐานจากซอร์สแพ็กเกจ (`com.unity.xr.arkit@6.0.8`) ไม่ใช่การเดา:

1. `Runtime/ARKitApi.cs:49` — `Api.AtLeast11_0()` เป็น `DllImport` **เฉพาะเมื่อมี define**
   ถ้าไม่มี บรรทัด 147 คืน `false` ตายตัว
2. `Runtime/ARKitSessionSubsystem.cs:392` — `RegisterDescriptor()` **`if (!Api.AtLeast11_0()) return;`**
   → ไม่มี descriptor ชื่อ `"ARKit-Session"` ในระบบเลย
3. `Runtime/ARKitLoader.cs:137` — `Initialize()` หา subsystem ไม่เจอ → `sessionSubsystem == null`
   → คืน `false` **โดยไม่มี error ใด ๆ** → แอปตกกลับไปใช้ไจโรอย่างที่ออกแบบไว้
4. `Editor/ARKitBuildProcessor.cs:204` — `ShouldIncludeRuntimePluginsInBuild()` คืน `false`
   ถ้า `loaderEnabled` เป็น false → **`libUnityARKit.a` ไม่ถูกก๊อปเข้า Xcode project ด้วยซ้ำ**
   (`loaderEnabled` ตั้งจาก `UpdateARKitDefines()` ซึ่งใน batch mode ถูกเรียกจาก `PreprocessBuild`
   ที่อยู่ใน `#if UNITY_IOS && UNITY_XR_ARKIT_LOADER_ENABLED` — วนกลับมาที่ define ตัวเดิม)

ปกติ Unity Editor ใส่ define นี้เองจาก `LoaderEnabledCheck` ซึ่งเป็น **EditorCoroutine ที่
`return` ทันทีใน batch mode** (`Editor/ARKitBuildProcessor.cs:318`) → บนเครื่องที่ไม่มี Editor
มันจึงไม่เคยถูกใส่ **ต้อง commit ไว้ใน `ProjectSettings.asset` เอง**:

```yaml
  scriptingDefineSymbols:
    iPhone: UNITY_XR_ARKIT_LOADER_ENABLED
```

⚠️ define ไม่พอถ้าไฟล์ตั้งค่า XR พัง: `UpdateARKitDefines()` ยังต้องอ่าน
`XRGeneralSettingsPerBuildTarget` → `Manager.activeLoaders` เจอ `ARKitLoader` ถึงจะก๊อป `.a`
→ `CIBuild.ReportXrLoaders()` พิมพ์รายชื่อ loader ที่ Unity เห็นจริงตอนเริ่ม build
และ `ArkitDefinePresent()` **ล้ม build ทันที** ถ้า define หาย (แทนที่จะเสีย 45 นาทีแล้วได้แอปไม่มี AR)

### บั๊กรอบ 196 (`เพิ่ม loader เข้า manager ไม่ได้`) — คนละตัว แต่จริงเหมือนกัน
`XRManagerSettings.TryAddLoader()` (mgmt 4.7.0 บรรทัด 256) ปฏิเสธ loader ที่ไม่อยู่ใน
`m_RegisteredLoaders` ซึ่ง `Awake()` เติมจาก **list ที่ serialize ไว้เท่านั้น** —
manager ที่ `CreateInstance` ในโค้ดจึงมี list ว่าง Awake ลงทะเบียนศูนย์ตัว ทุกการ add ถูกปฏิเสธ
(API ตั้งใจให้ immutable ตอน runtime) → แก้ด้วยการเติม `m_RegisteredLoaders` ผ่าน reflection
ก่อน แล้วค่อยเรียก API สาธารณะ + มี fallback เขียน `m_Loaders` ตรง ๆ

และที่สำคัญกว่า: **ARFoundation ไม่ได้ถาม manager ของเรา** ทุกตัว (`ARSession`, `ARPlaneManager`,
`ARRaycastManager`) หา loader ผ่าน `XRGeneralSettings.Instance.Manager.activeLoader`
(`Runtime/ARFoundation/LoaderUtility.cs:23`) → ต้อง publish ลง static ตัวนั้นด้วย
ไม่งั้นพังลึกลงไปอีกชั้นและดูเหมือนบั๊กคนละตัว

### flow ใหม่ที่ทำแล้ว (ตามที่ user ออกแบบ)
`Core/ArStep` = Searching → Aiming → Adjusting → Anchored
- ตรวจเจอพื้น → วาดแผ่นฟ้าโปร่ง (`ARPlaneMeshVisualizer` + DM_StandardTransparent)
  · template ต้องอยู่ใต้ parent ที่ปิดอยู่ เพราะ AF ก๊อป `activeSelf` ของ prefab ไปให้ instance
- แตะ = เลือกจุด (แมพซ่อนอยู่จนกว่าจะแตะ ไม่ใช่ลอยอยู่กลางห้องแบบเดิม)
- **สองนิ้วย่อขยาย** (`Core/ArPinch` — หน่วยเป็นเมตรบนโต๊ะ ไม่ใช่ตัวคูณ) · **เอาปุ่ม −/+ ออกทั้ง 2 เส้นทาง**
- ✓ ยืนยัน → `ARAnchorManager.AttachAnchor(plane, pose)` แล้ว **อ่านตำแหน่ง anchor กลับมา
  คำนวณ origin ใหม่ทุกเฟรม** (ทิศทางสำคัญ: anchor เป็นตัวบอกแมพ ไม่ใช่แมพบอก anchor)
  · อ่านใน session space (`TrackablesParent.InverseTransformPoint`) ไม่ใช่ world space
  ไม่งั้นป้อน transform ด้วยผลลัพธ์ของตัวเอง แมพจะไม่ขยับเลย

### 🔴 build 199 ล้ม — บทเรียนที่ปิดเรื่องไฟล์ตั้งค่า XR ได้จริง
`** ARCHIVE FAILED **` · Xcode link ไม่เจอสัญลักษณ์ `_UnityARKit_*` ~40 ตัว
= C# คอมไพล์ `DllImport("__Internal")` แล้ว (เพราะ define มา) แต่ **`libUnityARKit.a` ไม่ได้ถูกก๊อปเข้า Xcode project**

diagnostic ที่ใส่ไว้ตอบในบรรทัดเดียวว่าทำไม:
```
[CIBuild] iOS scripting defines = 'UNITY_XR_ARKIT_LOADER_ENABLED' → ARKit ENABLED
[CIBuild] XR: Unity found NO iOS settings — ARKit's native plug-in will be left out
```
→ `XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(iOS)` **คืน null**
ทั้งที่ไฟล์ YAML ครบ · GUID สคริปต์ถูกทุกตัว · `buildTarget: 4` ถูก · ลงทะเบียนใน EditorBuildSettings แล้ว

🔑 **บทเรียน: ที่ผิดคือ "วิธี" ไม่ใช่ไฟล์ใดไฟล์หนึ่ง** — เขียน .asset ด้วยมือมา 5 รอบ ทุกรอบดูถูกต้องแต่ Unity ไม่ยอมรับ
เครื่อง build ไม่มี Editor ก็จริง **แต่ CI runner มี** และทุก operation เป็น public API →
`CIBuild.EnsureXrSettings()` ประกอบ chain ทั้งชุดตอน build ด้วย API ของ Unity เอง
(`LoadOrCreate` ลบทิ้ง+สร้างใหม่ถ้าไฟล์เดิม deserialize ไม่ได้ · `TryAddLoader` · `SetSettingsForBuildTarget` ·
`EditorBuildSettings.AddConfigObject`) แล้ว **ตรวจซ้ำผ่าน lookup ของ Unity เอง** ถ้ายังไม่มี `ARKitLoader` ในลิสต์ = ล้ม build ทันที
→ ไม่มีอะไรขึ้นกับ YAML ที่เขียนมือแล้ว (ไฟล์ใน repo อาจ stale ได้ ไม่เป็นไร เพราะโค้ดซ่อมทุกรอบ)

### ⚠️ ITMS-90984 (build 201) — คำเตือน ไม่ใช่การตีกลับ แต่ซ่อนบั๊กของโปรดักต์ไว้
Apple ส่งอีเมลหลังอัปโหลดสำเร็จ: *"Although delivery was successful…"* → **build 201 อยู่บน TestFlight เทสได้ปกติ**
`UPLOAD SUCCEEDED with no errors, 1 warning` · เนื้อหา: `UIRequiredDeviceCapabilities: [arkit]` ไม่รองรับบน visionOS

แต่สิ่งที่คำเตือนนี้ชี้ไปคือบั๊กจริง: `ARKitSettings.Requirement.Required` เป็น**ค่าแรกของ enum = ค่า default**
และเราไม่เคยลงทะเบียน ARKitSettings asset ไว้ → `GetOrCreateSettings()` คืน instance ชั่วคราวที่เป็น Required เสมอ
→ ใส่ `arkit` ลง plist = **เครื่องที่ไม่มี ARKit ติดตั้งแอปไม่ได้เลย**

🔑 **ขัดกับดีไซน์ของแอปเอง** — `ArSession` มีทางสำรอง gyro/drag-to-orbit + toast "เครื่องนี้ไม่มีเซนเซอร์"
เขียนไว้สำหรับเครื่องพวกนั้นโดยเฉพาะ พอบังคับ Required โค้ดชุดนั้นเข้าไม่ถึงเลยบน iOS
(iPad mini 4 / Air 2 รัน iOS 15 ได้ แต่ไม่มี ARKit)

แก้: `CIBuild.MakeArkitOptional()` สร้าง `Assets/XR/Settings/ARKitSettings.asset` ตั้ง `requirement = Optional`
แล้วลงทะเบียนเป็น config object key `UnityEditor.XR.ARKit.ARKitSettings` (reflection เพราะอยู่ใน editor assembly ของแพ็กเกจ)
· ไม่ fatal ถ้าล้ม — Required ก็ยังติดตั้งและใช้งานได้บนเครื่องที่ user มี ไม่คุ้มที่จะเสีย build ทั้งรอบ
