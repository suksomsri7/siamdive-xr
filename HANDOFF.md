# HANDOFF — DiveMap (Unity Dive Map) สำหรับ AI agent ที่มาทำต่อ

> เอกสารนี้เขียนเพื่อให้ AI coding agent ใดๆ (Codex / Kimi / Claude / อื่นๆ) ทำงานต่อได้ทันที
> อ่านคู่กับ: `DESIGN_DOC.md` (สัญญาหลัก v1.2), `QC_PLAN.md`, `SECURITY_PLAN.md`
> อัปเดตล่าสุด: 2026-07-28 (ปิด WO-XR-03 + QC fixes, origin/main = `f31d9fc`, branch `wo-xr-05` รอ merge)

## 1. โปรเจกต์คืออะไร
- แอป **DiveMap** (`com.siamdive.divemap`) — Unity 6000.0.79f1 ใน `DiveMap/`
- แสดงแมพจุดดำน้ำ 3D จากระบบเว็บ **maps.siamdive.com** (builder.html/Three.js) ผ่าน API เดิม อนาคตแทนเว็บทั้งระบบ
- เป้าหมายระยะนี้ = สาย A (มือถือ Android) ตาม roadmap ใน DESIGN_DOC §5
- **มาตรฐานคุณภาพ: ภาพต้องเทียบเว็บจริงข้างกันแล้วไปทางเดียวกันหรือดีกว่า** (user ตรวจแบบนี้)

## 2. สถานะปัจจุบัน (อะไรเสร็จแล้ว)
- ✅ WO-XR-00: CI GameCI 3 targets — Android APK (IL2CPP, ~35 นาที), Windows .exe (Mono), Linux (QC) — เขียวทุก build
- ✅ WO-XR-01 + เก็บงาน: โหลดแมพเดโม `wl6zwxh1tdgn` (Htms Chang) — เรือ KTX2 2048px ตั้งบนพื้นทราย, แสง/reflection ถูกต้อง, น้ำโปร่งแสง 2 หน้า, กล้อง frame แบบเว็บ, ฟอนต์ไทย bundle (NotoSansThai ใน Resources)
- ✅ WO-XR-03 **ปิดแล้ว 2026-07-28** (`a7d12f8` + QC fixes `f31d9fc`): boids 1,100 ตัว 10 ฝูง ตามสูตรเว็บจริง (`buildSchool` ใน builder.html) — scad R=66.0 · barracuda R=143.9 speed 4.0 · pod 67.8/29.7 · วาฬเป็น **GLB จริง** `Whale_Shark_xr0.glb` worldLen 65.3 (เดิม clamp [8,16] ทำให้เล็กผิด 4 เท่า) · QC verdict = ผ่านแบบมีเงื่อนไข แล้วแก้ครบ
- 🟡 WO-XR-05.1+05.2 (UI shell + เมนู + รายการแมพ + ค้นหา): เขียนเสร็จบน branch **`wo-xr-05`** (`dc2a954`) รอ CI เขียวแล้ว merge เข้า main — **แผนเต็มอยู่ที่ `/root/projects/siamdive-xr-docs/WO-XR-05.md`**
- ✅ ระบบตาอัตโนมัติ: ทุก push → CI job qc-shot → แอปถ่ายรูปตัวเอง 2 มุม → artifact `qc-screenshot`
- ✅ XR-LOD CDN: `maps.siamdive.com/models/xr/` มี 15 โมเดล (manifest.json count=15) — เรือ/สัตว์หลัก KTX2+Draco
- ❌ ยังไม่มี: การ์ดข้อมูล+ตั้งค่า (WO-XR-05.3/05.4), AR (02m), ปลา GLB จริงรายตัว (04), โหมดแก้ไข (06), store (07)

### บทเรียนรอบ 2026-07-28 (กันทำซ้ำ)
- **อ่านสูตรเว็บให้ถูกชั้นก่อนตั้งค่าเสมอ** — span ของฝูง = สูตร local × `item.s` และ N ในสูตรต้องใช้ N ของ**เว็บ** (scad 500) ไม่ใช่ N ที่ Unity วาดจริง (120) ไม่งั้นฝูงหด
- **โมเดลที่ถูก clamp ขนาด = red flag** — ขนาดจริงมาจาก `maxd × item.s` ห้ามใส่ช่วง magic number
- **normals หักล้างกัน = ดำสนิท** — แผ่น double-sided ต้องมี vertex ของตัวเองต่อหน้า ไม่งั้น `RecalculateNormals()` เฉลี่ย +X กับ −X ได้ศูนย์ (ครีบหางปลาดำทั้งฝูง)
- **`JObject.Parse` ของ Newtonsoft แปลง ISO date เป็น DateTime อัตโนมัติ** → ใช้ `JsonTextReader{DateParseHandling=None}` ถ้าต้องการสตริงดิบ
- **ทำงานขนาน 2 executor ได้ด้วย `git worktree` + branch แยก** · CI trigger เฉพาะ main → ตรวจ compile ของ branch ด้วย `workflow_dispatch` (concurrency group แยกตาม ref จึงไม่ cancel กัน)
- VPS **ไม่มีคำสั่ง `zip`** — ส่งไฟล์ให้ user ใช้ artifact zip ของ GitHub ตรงๆ หรือ python zipfile

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
### 5.1 ✅ ปิดแล้ว — WO-XR-03 (2026-07-28)
formation ตามสูตรเว็บ + วาฬ GLB จริง + QC fixes (ครีบดำ/gloss/heading log) · commit `a7d12f8` → `f31d9fc`

### 5.2 ▶️ RESUME — รอผล CI ของ `wo-xr-05b` แล้ว merge
สถานะ ณ 2026-07-29 13:15 UTC:
- **main = `0d93c48`** (merge `wo-xr-05` เข้ามาแล้ว) = WO-XR-03 + QC fixes + **UI 05.1/05.2** (เมนู + รายการแมพ + ค้นหา) · CI ของ merge commit กำลังรัน
- **branch `wo-xr-05b` = `e137941`** = 05.3 การ์ดข้อมูล + 05.4 ตั้งค่า/i18n 259 คีย์ (merge ตัวแก้ชื่อแมพเข้ามาแล้ว) · **CI ยิงไว้แล้ว รอผล** → ถ้าเขียว: `git merge wo-xr-05b` บน main → push → ส่ง build ให้ user
- worktree: `/root/projects/siamdive-xr-ui` (wo-xr-05) + `/root/projects/siamdive-xr-ui2` (wo-xr-05b) — `git worktree remove` ทั้งคู่เมื่อ merge ครบ

**บทเรียนสำคัญรอบนี้ (ห้ามลืม): legacy `Text` + `VerticalWrapMode.Truncate` จะ "ทิ้งทั้งบรรทัด" ถ้าความสูงกล่อง < fontSize × 1.511** (metric จริงของ NotoSansThai-Regular: ascender 1061 / descender 450 / unitsPerEm 1000, USE_TYPO_METRICS) — เป็นเหตุให้ชื่อแมพหายทั้งที่ข้อมูลถูกทุกอย่าง · ตอนนี้ `UiKit.MakeText` ใช้ `Overflow` แล้ว + มี `UiKit.RowHeight(size, lines)` ให้เรียกแทนการใส่เลขดิบ **ใช้ทุกครั้งที่สร้างแถวข้อความใหม่**

ไฟล์เทสที่ส่ง user แล้ว:
- รุ่นมีเมนู (main `8d76e76`): `dive3d.suksomsri.cloud/dl/DiveMap-menu-977231d605.apk` · `.../DiveMap-win-menu-977231d605.zip`
- รุ่นก่อนหน้า (แก้ปลาดำ ไม่มีเมนู): `.../DiveMap-r9-6f5298db2d.apk`

### 5.3 ทำต่อจากแผน WO-XR-05 (`/root/projects/siamdive-xr-docs/WO-XR-05.md`)
05.3 การ์ดข้อมูล (แตะวัตถุ → ชื่อ/ชนิด/ความลึก) → 05.4 ตั้งค่า + i18n ไทย/EN เต็มระบบ

### 5.4 เก็บเล็ก
- ปลายังเป็นเมช procedural ไม่มี texture (เว็บเป็นปลาเขียว-เงิน) → งานจริงคือ **WO-XR-04** ปลา GLB รายตัว · **อย่ายัดรวมรอบอื่น** (เสี่ยง magenta/KTX2 บน llvmpipe)
- ตรวจ log `[Marine] whale heading dot(forward,vel)` ควร ≈ +1.0 ถ้าติดลบ = GLB หันกลับ ให้หมุน child yaw 180° (ห้ามแก้ WhaleController)
- ทรายเป็นวงรีครีมแบน เว็บมี sculpt + ไล่สีน้ำเงิน · hull เรือมีแผ่นดำ (backface ของ wreck GLB)

### 5.5 คิวใหญ่ถัดไป (user อาจสลับลำดับ — ถามก่อน)
WO-XR-04 (ปลา GLB จริง + caustics/FX) ↔ WO-XR-02m AR (ARCore) → WO-XR-06 โหมดแก้ไข → WO-XR-07 ขึ้น Play

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
