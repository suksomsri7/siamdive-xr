# Unity Dive Map — ระบบความปลอดภัย (Security Plan) v1.0

> คู่กับ `DESIGN_DOC.md` · 2026-07-17 · ครอบคลุม: ตัวแอป, API, ข้อมูลผู้ใช้, CI/CD, กุญแจทั้งหมด
> บทเรียนที่ฝังมาแล้ว: My Plan Security (ownership check), no-shared-credentials, Vercel author rule

---

## 1. กฎเหล็กอันดับหนึ่ง: **แอปห้ามคุยกับ Supabase ตรงๆ เด็ดขาด**

```
Unity APK  →  maps.siamdive.com API (Vercel)  →  Supabase
   ✅ มีแค่ URL ของ API                            🔒 DATABASE_URL อยู่ฝั่ง server เท่านั้น
```
- **ไม่มี** DATABASE_URL / service key / Supabase key ใดๆ ใน APK — ทุก byte ใน APK ถือว่าถูกแกะได้เสมอ
- QC gate สแกน APK หา pattern secret (postgres://, sb_secret, eyJ...) ก่อนปล่อยลิงก์ทุกครั้ง (อยู่ใน QC_PLAN ชั้น 1.3)
- ถ้าอนาคตต้อง key ฝั่ง client (เช่น analytics) → ใช้ key ประเภท publishable เท่านั้น + แยก key ต่อ project ตามกฎ no-shared-credentials

## 2. ตัวตนผู้ใช้ & สิทธิ์ (ใช้โมเดลเดิมของ maps — ไม่ประดิษฐ์ใหม่)

- **deviceId** สุ่มตอนเปิดแอปแรก เก็บใน Android Keystore-backed storage (EncryptedSharedPreferences ผ่าน plugin) — ไม่ใช่ไฟล์ธรรมดา
- **accountId + OTP อีเมล** (ระบบ multi-device ที่ออกแบบไว้ใน maps offline plan) = ทางย้ายเครื่อง/กู้บัญชี — Unity ใช้ endpoint ชุดเดียวกับเว็บ
- **การบังคับสิทธิ์อยู่ฝั่ง server เสมอ** (editPolicy/owner/searchable ตรวจใน API — มีอยู่แล้ว): client แค่ซ่อนปุ่ม, server คือคนตัดสินจริง — Unity **ห้าม** ใส่ logic "เชื่อ client" เพิ่มใน API
- ก่อนเริ่ม WO-XR-06 (save จาก Unity): **audit API PATCH ซ้ำหนึ่งรอบ** ตาม checklist My Plan Security เดิม (ownership, ห้าม ?deviceId= จาก query, ห้าม leak อีเมลใน response)

## 3. Transport & API

- HTTPS เท่านั้น + Android `networkSecurityConfig` บล็อก cleartext ทั้งแอป
- (เฟสหลัง ถ้าจำเป็น) certificate pinning — ยังไม่ทำใน v1 เพราะเพิ่มภาระ rotate cert
- Rate limit ฝั่ง Vercel API สำหรับ endpoint เขียน (PATCH/POST) กัน abuse — ตรวจว่าของเดิมมีหรือยังตอน WO-XR-06, ถ้าไม่มีให้เพิ่มแบบ additive
- ข้อความ error จาก API ห้าม leak รายละเอียดภายใน (stack, SQL) — ตรวจใน audit เดียวกัน

## 4. ข้อมูลผู้ใช้ในเครื่อง

- cache แมพ/GLB ในเครื่อง = ข้อมูล public/ของตัวเองเท่านั้น เก็บใน app-private storage (คนอื่นในเครื่องอ่านไม่ได้)
- แมพ private ของคนอื่นไม่มีทางเข้ามาในเครื่องอยู่แล้ว (server กรอง) — คงหลักนี้ไว้
- ปุ่ม "ลบข้อมูลในเครื่อง" ในตั้งค่า (ล้าง cache + deviceId ใหม่) — รองรับเครื่องยืม/ขายต่อ
- ไม่เก็บ location / กล้อง AR ไม่บันทึกภาพ (ARCore ประมวลผลสด ไม่ save เฟรม) — ประกาศชัดใน privacy policy ตอนขึ้น store

## 5. CI/CD & Supply Chain

| ของ | ที่เก็บ | กฎ |
|---|---|---|
| Unity license (.ulf) | GitHub Actions secret | ห้าม commit ลง repo |
| **Android keystore (ลายเซ็นแอป)** | GitHub secret (base64) + **สำรอง 2 ที่**: VPS `/root/secure/` + ให้ user เก็บไฟล์ไว้เอง | ⚠️ **หายแล้วหายเลย = อัปเดตแอปบน store ไม่ได้ตลอดไป** — สำคัญสุดในไฟล์นี้ |
| GitHub repo | private เสมอ | commit author ≠ root@ (กฎ Vercel เดิม) |
| Dependencies | ตรึงเวอร์ชันทุก package (Unity + npm ใน pipeline) | อัปเมื่อจำเป็น + อ่าน changelog เท่านั้น |
- CI ไม่รัน workflow จาก fork/PR ภายนอก (repo private อยู่แล้ว แต่ตั้ง explicit)
- APK ที่ส่งใน Telegram = dev build ลายเซ็น debug — ตัวขึ้น store ค่อยเซ็น release keystore (แยกกัน)

## 6. เนื้อหาจากผู้ใช้ (UGC) — แมพ public + media pins

- URL media ใน pins: อนุญาตเฉพาะ https + whitelist โดเมนที่ระบบอัปโหลดใช้ (ไม่โหลด URL แปลกปลอมเข้า texture — กันทั้ง SSRF ฝั่ง viewer และเนื้อหาไม่เหมาะสมฝัง GLB/ภาพ)
- ชื่อแมพ/label = plain text เสมอ (TextMeshPro ปิด rich text tag จาก user input — กัน tag injection)
- ปุ่ม report แมพ public (เฟส store) — ตามข้อกำหนด Google Play UGC

## 7. เมื่อเกิดเหตุ (Incident Response ฉบับย่อ)

1. secret รั่ว (key โผล่ใน repo/APK) → rotate ทันทีที่ต้นทาง (Vercel env / GitHub secret) → force push ลบประวัติ → ตรวจ log การใช้
2. พบช่องโหว่ API → ปิดที่ server ก่อน (deploy Vercel เร็วสุด) → ค่อยตาม client
3. ข้อมูล UserDiveSite เสียหาย → กู้จาก revision snapshots (ตาราง revision เดิม) + backup ก่อนเฟส save
4. ทุกเหตุ → จดลง memory + เพิ่ม test กันซ้ำ (กฎเดียวกับ QC)

---

**สรุปสั้นสำหรับอ่านรอบเดียว:** ① ไม่มี secret ใน APK — API เป็นกำแพงเดียว ② สิทธิ์ตัดสินที่ server เท่านั้น ③ keystore สำรอง 2 ที่ ห้ามหาย ④ audit API หนึ่งรอบก่อนเปิด save จาก Unity ⑤ UGC โหลดเฉพาะโดเมน whitelist
