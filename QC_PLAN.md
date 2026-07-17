# Unity Dive Map — ระบบ QC (Quality Control Plan) v1.0

> คู่กับ `DESIGN_DOC.md` · 2026-07-17 · ใช้เป็น gate ทุก WO — **ไม่ผ่าน QC = ไม่นับว่าเสร็จ ไม่ส่งลิงก์ APK ให้ user**
> แนวเดียวกับวัฒนธรรม QC เดิม (QC6 ของ SHARK, headless test ของ maps): เครื่องตรวจก่อน คนตรวจทีหลัง

---

## ชั้นที่ 1 — QC อัตโนมัติใน CI (ทุก push, ไม่ผ่าน = build แดง ไม่ออก APK)

### 1.1 Compile + Unit Tests (Unity Test Framework, EditMode)
| Test | ตรวจอะไร |
|---|---|
| **Coord round-trip** | `ToUnity(ToWeb(p)) == p` ทุก quadrant, Euler→quaternion→Euler ค่าตรง (กันบั๊กแกนกลับ — บั๊กชนิดนี้ตาเปล่าจับยากสุด) |
| **Scene JSON preserve** | โหลด scene ตัวอย่างที่มี **field แปลกปลอม** → save → diff ต้องได้ JSON เท่าเดิม 100% (กติกา "field ไม่รู้จักห้าม drop" — บทเรียน PATCH upsert-overwrite) |
| **Serialize parity** | scene จาก builder เว็บจริง (fixture ชุด: Sail Rock, แมพมี sculpt, มีเชือก, มี school, มี warp) parse ได้ครบทุก item ไม่ตกหล่น |
| **Save guard** | ห้าม save ขณะ `loadPending > 0` (บทเรียนเว็บ: autosave ทับฉากโหลดไม่ครบ 108→31 ชิ้น — **ต้อง port กฎนี้มาเป็น test**) |

### 1.2 Scene Regression Suite (PlayMode, headless)
- โหลด **fixture แมพจริงทุกแบบ** (อย่างน้อย: จาก template ทุกตัว + demo public 3 อัน) → assert: จำนวน item ตรง, ไม่มี GLB โหลด fail, พื้น sculpt สร้าง mesh ได้, เชือกครบ
- fixture เก็บใน repo (`Tests/Fixtures/*.json`) — เพิ่มทุกครั้งที่เจอบั๊กจากแมพจริง = regression ถาวรแบบชุด 107 ข้อของบัญชี

### 1.3 Build Gate
- APK build สำเร็จ + ขนาดไฟล์ ≤ เพดาน (แจ้งเตือนถ้าโต >10% จาก build ก่อน — จับ asset หลุดเข้า build โดยไม่ตั้งใจ)
- ProGuard/R8 ผ่าน, ไม่มี secret string ใน APK (scan ด้วย script grep pattern ก่อนปล่อยลิงก์)

## ชั้นที่ 2 — Visual Parity QC (ก่อนปิดแต่ละ WO ที่กระทบภาพ)

- **Golden screenshot เทียบสองฝั่ง:** แมพเดียวกัน มุมกล้องเดียวกัน → เว็บ (headless chromium ตาม skill เดิม `reference_siamdive_maps_headless_test`) vs Unity (batchmode screenshot บน CI) → วางเทียบข้างกันส่งเข้า Telegram ให้ตรวจตา
- เกณฑ์: ตำแหน่ง/สเกล/ทิศทุกวัตถุตรง (ห้ามกระจก/หมุนเพี้ยน) · โทนสี/น้ำใกล้เคียง (ไม่ต้องเป๊ะ pixel — คนตัดสิน)
- สาย C (builder parity) ใช้ชุดนี้เป็น **เกณฑ์ปิดเว็บ**: แมพ prod จริงสุ่ม ≥20 อัน เปิดสองฝั่งต้องตรงหมด

## ชั้นที่ 3 — Performance QC (ทุก WO, ตัวเลขวัดจริงบนเครื่อง)

| เวที | เป้า | วัดด้วย |
|---|---|---|
| Samsung (สาย A) | ≥ 60fps คงที่ในแมพใหญ่สุด, เปิดแอป→เห็นแมพ ≤ 5s, RAM ≤ 1.5GB | overlay fps ใน dev build + `adb dumpsys` |
| Galaxy XR (สาย B) | ≥ 72fps ทั้งสองตา, ไม่มี frame drop ตอนหมุนจาน | XR performance HUD |
| ฝูงปลา | ≥300 ตัว + วาฬ ไม่หลุดเป้า fps | ฉาก stress test ใน fixture |
- dev build ทุกตัวฝัง **ปุ่มลับเปิด fps/แรม overlay** → user ถ่าย screenshot ส่งกลับได้เอง

## ชั้นที่ 4 — Device Test โดย user (checklist ภาษาไทยแนบทุกลิงก์ APK)

- ทุกลิงก์ APK ใน Telegram แนบ checklist สั้น ≤6 ข้อ (☑ เปิดได้ ☑ โหลดแมพ X ☑ หมุนลื่น ☑ ...) — ตอบในแชทได้เลย
- บั๊กที่ user เจอ → เข้า fixture ชั้น 1.2 ทันที (เจอครั้งเดียว กันตลอดชีพ)

## ชั้นที่ 5 — Data Integrity QC (สำคัญสุดช่วงเปลี่ยนผ่านสองระบบ)

- **ก่อน merge ทุก WO ที่มีการ save:** เทส round-trip กับ **DB dev ก่อนเสมอ** ห้ามเทสเขียนบน prod
- save จาก Unity → เปิดใน builder เว็บ → save จากเว็บ → เปิดใน Unity → วนครบ = ข้อมูลต้องไม่เพี้ยนสักรอบ
- ตรวจ `rev` conflict ทำงานจริง (สองเครื่องแก้พร้อมกัน → ฝ่ายช้าโดนถามไม่ใช่ทับเงียบ)
- backup: ก่อนเฟส WO-XR-06 (save จริงครั้งแรก) snapshot ตาราง UserDiveSite กันเหตุ

---

**กฎเหล็กสรุป:** ① build แดง = ไม่มีลิงก์ APK ② บั๊กจริงทุกตัวกลายเป็น test ถาวร ③ ห้ามเทสเขียนบน prod DB ④ ตัวเลข perf ต้องวัดจริง ไม่ประมาณเอา ⑤ ปิดเว็บได้ต่อเมื่อ parity QC ผ่านทั้งชุด
