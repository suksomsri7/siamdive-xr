# HANDOFF — DiveMap (Unity Dive Map) สำหรับ AI agent ที่มาทำต่อ

> เอกสารนี้เขียนเพื่อให้ AI coding agent ใดๆ (Codex / Kimi / Claude / อื่นๆ) ทำงานต่อได้ทันที
> อ่านคู่กับ: `DESIGN_DOC.md` (สัญญาหลัก v1.2), `QC_PLAN.md`, `SECURITY_PLAN.md`
> อัปเดตล่าสุด: 2026-07-23 (checkpoint หลังรอบ QC r8, origin/main = `5fe688e`)

## 1. โปรเจกต์คืออะไร
- แอป **DiveMap** (`com.siamdive.divemap`) — Unity 6000.0.79f1 ใน `DiveMap/`
- แสดงแมพจุดดำน้ำ 3D จากระบบเว็บ **maps.siamdive.com** (builder.html/Three.js) ผ่าน API เดิม อนาคตแทนเว็บทั้งระบบ
- เป้าหมายระยะนี้ = สาย A (มือถือ Android) ตาม roadmap ใน DESIGN_DOC §5
- **มาตรฐานคุณภาพ: ภาพต้องเทียบเว็บจริงข้างกันแล้วไปทางเดียวกันหรือดีกว่า** (user ตรวจแบบนี้)

## 2. สถานะปัจจุบัน (อะไรเสร็จแล้ว)
- ✅ WO-XR-00: CI GameCI 3 targets — Android APK (IL2CPP, ~35 นาที), Windows .exe (Mono), Linux (QC) — เขียวทุก build
- ✅ WO-XR-01 + เก็บงาน: โหลดแมพเดโม `wl6zwxh1tdgn` (Htms Chang) — เรือ KTX2 2048px ตั้งบนพื้นทราย, แสง/reflection ถูกต้อง, น้ำโปร่งแสง 2 หน้า, กล้อง frame แบบเว็บ, ฟอนต์ไทย bundle (NotoSansThai ใน Resources)
- 🟡 WO-XR-03 (~85%): ระบบฝูงปลา Burst boids 600 ตัว 10 ฝูง + whaleshark — ทำงานจริง เทสเขียว **เหลือรอบปิด (ดู §5)**
- ✅ ระบบตาอัตโนมัติ: ทุก push → CI job qc-shot → แอปถ่ายรูปตัวเอง 2 มุม → artifact `qc-screenshot`
- ✅ XR-LOD CDN: `maps.siamdive.com/models/xr/` มี 15 โมเดล (manifest.json count=15) — เรือ/สัตว์หลัก KTX2+Draco
- ❌ ยังไม่มี: เมนู/UI ทั้งหมด (WO-XR-05), AR (02m), โหมดแก้ไข (06), store (07)

## 3. โครงสร้างโค้ดสำคัญ
```
DiveMap/Assets/Scripts/
  Core/        (pure logic, มี unit test — SceneData JSON-preserve, WebCoord แปลงพิกัด z-flip,
                MarineMath สูตร boids/pitch/no-roll, SceneLoadState)
  Runtime/     (AppBoot จุดเริ่ม+UI+แสง SetupLighting, MapApiClient, AssetManifest resolver xrGlbUrl,
                SceneBuilder สร้างฉาก+GroundToBase+น้ำ+obstacles, OrbitCamera FrameBox,
                Marine/ = FishSchoolSystem, BoidsJob(Burst), WhaleController, FishMeshFactory)
DiveMap/Assets/StreamingAssets/asset_manifest.json  (275 โมดูล, 16 ตัวมี xrGlbUrl)
DiveMap/Assets/Resources/   (materials DM_* กัน shader stripping + NotoSansThai)
DiveMap/Assets/Tests/EditMode/  (45+ เทส — ห้ามแตก)
.github/workflows/build.yml     (CI — จูนแล้ว อย่ารื้อ)
tools/                          (สคริปต์ XR-LOD pipeline)
```

## 4. กติกาเหล็ก (ผิดแล้วพังจริง เคยพังมาแล้ว)
1. **ไม่มี Unity Editor บนเครื่อง** — build/test บน CI เท่านั้น · ไฟล์ asset ใหม่ต้องเขียน `.meta` เอง (GUID สุ่มไม่ซ้ำ ดูตัวอย่างในไฟล์ข้างเคียง)
2. commit author ต้องเป็น `suksomsri7 <suksomsri@gmail.com>` (ห้าม root@)
3. **1 push = 1 CI (~35 นาที)** — รวมงานให้เสร็จแล้ว push ครั้งเดียว · ห้าม push ระหว่างมี build รันอยู่ (แม้ .md จะ paths-ignore ก็ตรวจก่อน)
4. material/shader รันไทม์ต้องอยู่ `Assets/Resources/` ไม่งั้นโดน strip = จอชมพู/ดำ · ห้ามเปิด shader keyword ที่ไม่ถูก include
5. โมเดล metallic ต้องมี reflection (ตั้งแล้วใน AppBoot.SetupLighting — อย่าลบ)
6. CI Linux (llvmpipe) ไม่ apply per-instance matrix ของ RenderMeshInstanced → มี software fallback ใน FishSchoolSystem (อย่าลบ)
7. กฎวาฬ: pitch clamp ±0.5, **roll = 0 เสมอ** (โครงสร้างบังคับใน MarineMath — ห้าม regress)
8. แก้ workflow CI ได้เฉพาะจำเป็นสุดๆ · `ResolveOutputPath` ต้องบังคับ .apk
9. repo maps (`/root/projects/siamdive-maps`, branch **master**): แก้แล้ว push GitHub **ไม่ deploy อัตโนมัติ** — ต้อง `vercel deploy --prod --yes --token <ดู memory reference_vercel_credentials>` · commit เฉพาะไฟล์ตัวเอง (additive)
10. เครื่อง VPS RAM 8GB — ห้ามรันงานหนักขนาน · `sleep` ระดับบนถูก block ในบาง harness → ใช้ curl --retry / background loop

## 5. งานถัดไปทันที (คิวเรียงแล้ว)
### 5.1 รอบปิด WO-XR-03 (ค้างกลางคัน — ยังไม่มีโค้ดค้าง เริ่มใหม่ได้เลย)
a) **formation ฝูงเล็กเกินจริง** (scad Ø4.2m — เว็บแผ่กว้างกว่ามาก):
   อ่าน `/root/projects/siamdive-maps/public/builder.html` L~2496 `g.scale.setScalar((16*pickScale(asset))/maxd)` + `pickScale()` + catalog SCHOOL L1098-1109 (defaultScale: scad 3.5, barracuda 8.0, yellowtail 1.3) + `N=asset.school` L1493 (เว็บ: scad 500 ตัว/ฝูง)
   → คำนวณ span จริงเป็นเลขก่อน แล้วตั้ง formation ใน FishSchoolSystem ให้ตรง + เพิ่มปลารวม ~1200 (budget มือถือ) + floor รัศมีขั้นต่ำ ~8×fishLen (กัน yellowtail 20 ตัวอัดใน 0.3m)
b) **วาฬ**: เลิกใช้ procedural mesh (ดูเป็นว่าวกระดาษ) → โหลด `https://maps.siamdive.com/models/xr/Whale_Shark_xr0.glb` (ตรวจ 200 ก่อน) เป็นตัววาฬ + attach WhaleController · ขนาด world ~16m · swimmer ไม่ต้อง ground
c) push → CI → QC 2 มุมเทียบเว็บ → ผ่านแล้วปิด WO-XR-03
### 5.2 ส่งไฟล์ให้ user เทส
APK + Windows .exe จาก artifact CI ล่าสุด → วาง `/var/www/dive3d/dl/<ชื่อสุ่ม>.zip` → แจ้งลิงก์ `https://dive3d.suksomsri.cloud/dl/...`
### 5.3 เก็บเล็ก
ทรายโทนครีมกว่าเว็บนิดหน่อย (AppBoot ambient) · ตรวจ sculpt พื้นใน close-up
### 5.4 คิวใหญ่ถัดไป (user อาจสลับลำดับ — ถามก่อน)
WO-XR-05 UI/เมนู (รายการแมพ/ค้นหา/การ์ดข้อมูล/i18n) ↔ WO-XR-02m AR (ARCore) → WO-XR-04 (ปลา GLB จริง + caustics/FX)

## 6. เครื่องมือ/การเข้าถึง (บนเครื่อง VPS นี้)
- **GitHub API** (ดู CI/โหลด artifact): token อยู่ใน `~/.git-credentials` → `GH_TOKEN=$(sed -n 's#https://suksomsri7:\([^@]*\)@github.com#\1#p' ~/.git-credentials)` แล้ว curl `api.github.com/repos/suksomsri7/siamdive-xr/actions/...` (gh CLI ไม่ได้ login)
- **QC artifact**: run ล่าสุด → artifact `qc-screenshot` → `qc_screenshot.png` (มุมกว้าง), `qc_screenshot2.png` (ประชิดฝูงปลา), `qc_player.log` (grep `[Marine]`/`[QC]`)
- **ภาพเว็บอ้างอิง**: `NODE_PATH=/tmp/node_modules node /root/dive3d/qc_web_reference.mjs wl6zwxh1tdgn /tmp/qc_web_reference.png` (puppeteer + swiftshader)
- Vercel token / Neon / อื่นๆ: อยู่ใน memory dir `/root/.claude/projects/-root/memory/reference_*.md`
- ส่งไฟล์ใหญ่ให้ user: วาง `/var/www/dive3d/dl/` (Telegram ส่ง >50MB ไม่ได้)

## 7. โปรโตคอล QC (ถ้ามี orchestrator/reviewer แยกจาก executor)
1. executor ทำงานตาม work order → commit → push ครั้งเดียว → **หยุด รายงานสิ่งที่ทำ + จุดเสี่ยง**
2. reviewer รอ CI เสร็จ → โหลด qc-screenshot 2 มุม + player log → เทียบ side-by-side กับภาพเว็บอ้างอิง + ตรวจตัวเลขใน log
3. reviewer เขียน verdict: ผ่าน / ไม่ผ่าน+สาเหตุ+สมมติฐานเรียงลำดับ → ส่งเป็น work order รอบถัดไป
4. ประวัติ QC ที่ผ่านมา (บทเรียนสำคัญ): เรือจม=pivot ไม่ ground, มืด=metallic ไร้ reflection, ปลาหาย=llvmpipe instancing, ปลายักษ์=ตีความ scale ผิดชั้น (item.s = ขนาดก้อนฝูง ไม่ใช่ปลารายตัว) — **อย่าเดา ให้อ่านโค้ดเว็บ/ข้อมูล API เป็นเลขจริงก่อนตั้งค่าเสมอ**
