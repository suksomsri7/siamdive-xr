# HANDOFF — DiveMap (Unity Dive Map) สำหรับ AI agent ที่มาทำต่อ

> เอกสารนี้เขียนเพื่อให้ AI coding agent ใดๆ (Codex / Kimi / Claude / อื่นๆ) ทำงานต่อได้ทันที
> อ่านคู่กับ: `DESIGN_DOC.md` (สัญญาหลัก v1.2), `QC_PLAN.md`, `SECURITY_PLAN.md`
> อัปเดตล่าสุด: **2026-07-31 เช้า** · main = `7326d44`
>
> # 🔴 อ่าน §4.97 ก่อนทุกอย่าง
> **§4.97 RESUME 2026-07-31** — user ทักว่า UI/UX ไม่เหมือนเว็บ พร้อมภาพอ้างอิงใน `docs/refs/`
> (§4.6-§4.96 คือประวัติย้อนหลัง อ่านทีหลังได้ · ตัวเลขความคืบหน้าที่เชื่อได้คือ **`PARITY.md` เท่านั้น** ไม่ใช่ WO ใน DESIGN_DOC)
>
> **สถานะย่อ**: PARITY 44.5/86 (52%) · CI เขียวครบทุก job · APK ล่าสุด `dive3d.suksomsri.cloud/dl/DiveMap-v2-3fca1f9c67.apk`
> **repo เปิด public ชั่วคราว** → cron สลับกลับ private เอง 1 ส.ค. 08:05 น. (ดู §4.96)

## 1. โปรเจกต์คืออะไร
- แอป **DiveMap** (`com.siamdive.divemap`) — Unity 6000.0.79f1 ใน `DiveMap/`
- แสดงแมพจุดดำน้ำ 3D จากระบบเว็บ **maps.siamdive.com** (builder.html/Three.js) ผ่าน API เดิม อนาคตแทนเว็บทั้งระบบ
- เป้าหมายระยะนี้ = สาย A (มือถือ Android) ตาม roadmap ใน DESIGN_DOC §5
- **มาตรฐานคุณภาพ: ภาพต้องเทียบเว็บจริงข้างกันแล้วไปทางเดียวกันหรือดีกว่า** (user ตรวจแบบนี้)

## 2. สถานะปัจจุบัน (อะไรเสร็จแล้ว)
> ⚠️ **รายการข้างล่างนี้หยุดอยู่ที่ 29 ก.ค.** (WO-XR-00 ถึง 05) — งานวันที่ 30-31 ก.ค. อยู่ใน §4.9/§4.96/§4.97
> **แหล่งความจริงเรื่องความคืบหน้าคือ `PARITY.md` เท่านั้น** (86 ฟีเจอร์เว็บ · ตอนนี้ 44.5 = 52%)
> ห้ามประเมินจาก WO ใน DESIGN_DOC — WO ไม่ครอบคลุมฟีเจอร์เว็บจริง (นี่คือเหตุผลที่ PARITY.md ถูกสร้างขึ้น)

**เพิ่มหลัง 29 ก.ค. (สรุปสั้น — รายละเอียดใน §4.9 / §4.96):**
ทัวร์ดำน้ำโดรน (จอย 2 ตัว/ไฟหน้า/ถ่ายรูป/มินิแมพ/เข็มทิศ/ความลึก/vignette) · เกมเก็บขยะ+เหรียญ+ป้าย ♻️ · wallet ออนไลน์ `/api/wallet` · ร้านค้า 89 ราคา · ประตูวาป · ปลาตกใจหนีผู้เล่น (C5) · หมุด 📍 + ดูรูป · สอนท่าเล่น spotlight · จุดเกิดสุ่ม · heatmap ความลึก · โหมดกลางวัน · ตัวเลขเฟรมเรต · เสียง

- ✅ WO-XR-00: CI GameCI 3 targets — Android APK (IL2CPP, ~35 นาที), Windows .exe (Mono), Linux (QC) — เขียวทุก build
- ✅ WO-XR-01 + เก็บงาน: โหลดแมพเดโม `wl6zwxh1tdgn` (Htms Chang) — เรือ KTX2 2048px ตั้งบนพื้นทราย, แสง/reflection ถูกต้อง, น้ำโปร่งแสง 2 หน้า, กล้อง frame แบบเว็บ, ฟอนต์ไทย bundle (NotoSansThai ใน Resources)
- ✅ WO-XR-03 **ปิดแล้ว 2026-07-28** (`a7d12f8` + QC fixes `f31d9fc`): boids 1,100 ตัว 10 ฝูง ตามสูตรเว็บจริง (`buildSchool` ใน builder.html) — scad R=66.0 · barracuda R=143.9 speed 4.0 · pod 67.8/29.7 · วาฬเป็น **GLB จริง** `Whale_Shark_xr0.glb` worldLen 65.3 (เดิม clamp [8,16] ทำให้เล็กผิด 4 เท่า) · QC verdict = ผ่านแบบมีเงื่อนไข แล้วแก้ครบ
- ✅ WO-XR-05.1+05.2 **merge เข้า main แล้ว 2026-07-29** (`0d93c48`): ปุ่ม ☰ + เมนู + navigation stack + Android back + safe area · **รายการแมพจาก `/api/dive-sites/public` จริง** พร้อม thumbnail จาก Bunny CDN + ค้นหา server-side + pagination + จำแมพล่าสุด (PlayerPrefs `shortId`) · QC ภาพยืนยันชื่อแมพไทย/อังกฤษเรนเดอร์ครบ
- ✅ WO-XR-05.3+05.4 **merge เข้า main แล้ว** (`06f88ce`): แตะวัตถุ → การ์ด ชื่อ/ชนิด/ความลึก (AABB slab test, ฝูงปลา fallback ทรงกลม) · หน้าตั้งค่า + สลับ ไทย/English ทั้งแอปทันที (UiStrings 260 คีย์ port จาก `TR` ของเว็บ) + โหมดกราฟิกประหยัด — **WO-XR-05 ครบทั้ง 4 ก้อน**
- ✅ ระบบตาอัตโนมัติ: ทุก push → CI job qc-shot → แอปถ่ายรูปตัวเอง 2 มุม → artifact `qc-screenshot`
- ✅ XR-LOD CDN: `maps.siamdive.com/models/xr/` มี 15 โมเดล (manifest.json count=15) — เรือ/สัตว์หลัก KTX2+Draco
- ✅ WO-XR-04 **ครบ 3 ก้อน merge เข้า main แล้ว 2026-07-29 เย็น** (`3506319` 04.1 → `41a0cab` 04.2 → `f98bb05` 04.3, merge `8cd2c17`):
  - **04.1 ปลา GLB จริง** — ฝูงวาดโมเดลจริงจาก CDN (Scad_School_xr0 670 tris / Barracuda_School_xr0 450 / Trevally_xr1 3,999) แบบ static instancing เหมือน `buildSchool()` ของเว็บ · QC run `30488826434` ยืนยัน `tex=OK` ครบ 3 สายพันธุ์, `baseLen` ตรงเป๊ะ (1.911/1.899/1.862), `schools=10 fish=1100 whale=1`, `whale dot=1.000`
  - **04.2 พื้นทราย + ฉากหลัง** — superellipse slab 340u (แมพเดโม 306×374) + skirt + ก้นแบน, สีทราย+ขอบ haze เบคเป็น texture 1024², ฉากหลังไล่สี 4 stop แบบ screen-space, far plane 1000→9000
  - **04.3 god rays + caustics + fog** — 10 ลำแสงขนานดวงอาทิตย์, caustics additive บนผิวบนของพื้น, fog เชิงเส้น 500-9000 (0x123a55)
- ❌ ยังไม่มี: AR (WO-XR-02m), โหมดแก้ไข (06), onboarding + ขึ้น Play (07)

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
                Marine/ = FishSchoolSystem, BoidsJob(Burst), WhaleController, SoloAnimalRegistry,
                FishMeshFactory)

🐟 **สมองสัตว์ (C6)** — 4 ตารางใน Core ทั้งหมด pure + มีเทส:
  `SpeciesGenome` (กินอะไร/กลัวใคร/บุคลิก) → `SpeciesBehavior` (BEHAVIOR_CFG 94 แถวจาก builder.html
  + roamR/swimMul) → `SwimStyle` (ท่าว่าย) → `FishMind.TraitsFor` (นิสัยฝูง) · `HuntMath` = หิว/ล่า/หนี
  `MarineRouting.For(id, kind)` ตัดสินว่าอะไรได้สมอง — **kind ไม่ใช่ prefix** (เดิมดูแต่ prefix เลยทำ
  ให้ losin:/mdl: 58 ชนิดกลายเป็นเฟอร์นิเจอร์)
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
11. **`Outline` component บนกราฟิกที่โปร่งใส 100% = เส้นโปร่งใส** (มันก๊อป mesh เดิม) → ใช้ `UiKit.RoundedSprite(r, border)` ที่วาดขอบลง sprite จริง
12. **ป้าย/มาร์กเกอร์ในโลก 3D ต้อง unlit** — ใช้ `DM_GltfUnlit` + ทำเป็นแผ่นกลม**ทึบ** (ตัด transparency ทิ้ง) · ถ้าใช้ shader รับแสงจะกลายเป็นวงกลมเทาใต้น้ำ
13. **QC harness ต้องรอเป็นเฟรม ไม่ใช่วินาที** เมื่อของที่ทดสอบเดินตามเฟรม — CI ได้ **3 fps** (`realDt=0.333` = Unity `maximumDeltaTime`) และ `DroneFlight.Inertia` เป็นต่อเฟรม → `WaitForSecondsRealtime(5f)` = แค่ 15 เฟรม (เสีย CI ไป 3 รอบกว่าจะรู้) · ใช้ `for (int f=0; f<60; f++) yield return null;`
14. **log ต้องพูดตอนที่ 'ไม่มีอะไรเกิดขึ้น' ด้วย** — `[Pins] placed 0` / `[Flee] panic=0.00 camSpeed=10.4/11` · ความเงียบแยกไม่ออกระหว่าง 'พัง' กับ 'เงื่อนไขไม่ถึง'
15. **ชื่อฟังก์ชันที่ตรงคำ ≠ หน้าที่ผู้ใช้ใช้จริง** — พอร์ต `openShop()` เพราะชื่อตรงคำว่า shop แต่ร้านค้าจริงคือ palette (`tryPlace` หักเหรียญตอนวาง) · **เทียบภาพหน้าจอจริงก่อนเสมอ** (`docs/refs/`)

## 4.5 ⚠️ อ่าน `PARITY.md` ก่อนประเมินความคืบหน้า
รายการฟีเจอร์เว็บทั้ง 86 ข้อเทียบแอป (ดึงจาก `id=`/`title=`/`function` ใน builder.html จริง) — **ตอนนี้ ~21%**
WO ใน DESIGN_DOC **ไม่ครอบคลุม** ทัวร์โดรน/เกมขยะ-เหรียญ/เข็มทิศ/มินิแมพ/ถ่ายรูป/อัดวิดีโอ/เสียง/heatmap/warp/pins/เชือก/บัญชี-สิทธิ์/รายการโปรดออฟไลน์ → ห้ามรายงาน % จาก WO เพียวๆ

## 4.6 ▶️ RESUME 2026-07-30 — P0/P0.5/P1.1 ปิดแล้ว (main `64f879a`)
- **P0** toast + ป้ายหัวจอแปลภาษา (เป็นสตริงประกอบ ระบบ retranslate จับไม่ได้ → AppBoot ประกอบใหม่เอง) + โหมดประหยัดลดปลาครึ่ง
- **P0.5** `Core/AppModes.cs` (ModeRules 7 เทส) + `ModeManager` + `Ui/HudLayer` + `InputRig` — orbit gate มี 3 วีโต้ (หน้าจอเปิด/นิ้วบน UI/โหมด)
- **P1.1 ทัวร์บินได้จริง** `Core/DroneFlight.cs` (14 เทส ยกค่าจากเว็บ: dz 0.12, yaw 1.1, SP 30, lift 0.72, inertia 0.09 **ต่อเฟรม ห้ามคูณ dt**) + `TourController` (raycast หาพื้น) + `Ui/JoystickWidget` + `Ui/TourHud`
- QC จับได้ 2 บั๊กแล้วแก้: การ์ดข้อมูลทับจอย (ไม่อยู่ใน nav stack → `SetChromeVisible` ต้องซ่อนเอง) · ระยะเริ่มทัวร์ต้องมาจาก frame box ของเนื้อหา ไม่ใช่รัศมีพื้นทราย
- APK ล่าสุดส่ง user: `dive3d.suksomsri.cloud/dl/DiveMap-tour2-64f879a2*.apk`
- **ถัดไป P1.2**: ไฟหน้าโดรน (spotlight+โคน builder.html:3669) · บับเบิล · vignette/murk (`scene.fog` near 90 far 230 ตอน tour) · **เสียง** 8 ไฟล์ที่ `maps.siamdive.com/audio/*.mp3` (streaming ไม่ต้องยัดใน APK) · แป้นจอยทำเป็นวงกลม (ตอนนี้เหลี่ยม)

## 4.7 ▶️ RESUME 2026-07-30 (เช้ามืด) — P1.2 + UI parity (main `7490c819`)
- **P1.2a** จอยกลมตามเว็บ (`UiKit.CircleSprite`) · **ไฟหน้าโดรน** `Runtime/DroneLights.cs` + `Core/DiveLightMath.cs` (8 เทส) = 2 spotlight + 2 วงไฟบนพื้น + 2 โคนแสง + **สลับบรรยากาศทั้งฉาก** (เปิด fog 170-680 ฟ้า ambient×0.55 / ปิด 70-200 เกือบดำ ×0.32) + คืนค่าฉากเดิมตอนออก · **ปลาหลบโดรน** (droneBubble: ดันตำแหน่งออก ไม่ใช่ steer) · vignette
- **P1.2b** เสียง `Core/DiveAudio.cs` (7 เทส) + `Runtime/AudioBank.cs` — **สตรีมจาก `maps.siamdive.com/audio/`** (APK ไม่โต) · QC ยืนยัน `[Audio] loaded drone_start_cue (9.1s) / underwater_ambience (47.3s)` · เสียงวาฬตามระยะ cooldown แยกต่อตัว · ปุ่ม mute จำค่า
- **UI parity 3 pass** (user สั่ง: "UI/UX ต้องเหมือนเว็บ") → **อ่าน `UI_PARITY.md`** ก่อนแตะ UI ทุกครั้ง
  - tokens = `:root` ของ builder.html (bg #071a2b / accent #39b0e8 / txt #eaf4fb / mut #9fb6c9 / line 10%) · glass 0.88 ไม่ใช่ 0.72 เพราะ uGUI เบลอฉากหลังไม่ได้
  - `Runtime/Ui/IconPainter.cs` วาดไอคอน stroke จาก path 24 หน่วยของเว็บ (15 ไอคอน) — ไม่มี Editor ให้ import SVG และ NotoSansThai ไม่มี glyph ☰
  - **☰ ย้ายไปขวาล่าง วงกลมฟ้า กด = กางคอลัมน์ปุ่มกลม (รายการแมพ/ทัวร์/ตั้งค่า) + สลับไอคอน ☰↔✕** เหมือน `#actions` ของเว็บ · **ลบ slide-in panel ของ 05.1 ทิ้ง**
  - toast **กลางจอ** · tour chrome เป็นวงกลมกระจก (exit/lamp/sound) · เข็มทิศเหนือแดงที่ขอบขวา
- APK: `dive3d.suksomsri.cloud/dl/DiveMap-webui-7cc289a78d.apk`
- **งาน parity ที่เหลือ (เรียงตามที่ผู้ใช้เห็น)**: 1) **bottom sheet** — รายการแมพ/ตั้งค่า/การ์ด ต้องเป็นแผ่นเลื่อนขึ้นทับแมพ ยังเป็นหน้าเต็มจอ (ต้องทำ `UiKit.RoundedSprite()` 9-slice ก่อน) 2) modal สเปกเว็บ 3) `#backBtn` ซ้ายบน + `#hint` pill 4) chip หมวด

## 4.8 ▶️ RESUME 2026-07-30 เช้า — UI/UX parity 9 pass (main `23cd181d`)
🔴 **user สั่ง: "ตำแหน่งการวางต้องถูกต้องไม่ใช่แค่มี"** → อ่าน **`UI_PARITY.md` §1.5 ก่อนแตะ UI ทุกครั้ง**
- **หน่วยวัด**: เว็บใช้ CSS px · แอปใช้ canvas 1080×1920 สเกล `√(w/1080·h/1920)` → **ทุกตัวเลข UI ต้องผ่าน `UiKit.Css()` / `CssFont()`** (ห้าม hard-code หน่วย canvas อีก) · QC log ยืนยัน `dpr=1.00 canvasScale=0.667 48css=72u`
- **HUD ทัวร์เดิมผิดที่แทบทุกชิ้น** → แก้ครบตาม builder.html 231-277 (ตารางใน UI_PARITY §3.5): exit ซ้ายบน 44 · depth ขวาบน pill 19px/800 #9fe0ff · hint กลางบน · lamp ซ้าย 104 (ติดไฟ=อำพัน) · mute ซ้าย 174 · camera ขวา 104 · stick 138/knob 60 + ป้าย 4 ทิศ · minimap กลางล่าง 118 · **compass ตอนทัวร์ย้ายไปขวาบน right 138/top 15** (เว็บไม่ซ่อน แต่ย้าย) · ซ่อน #count
- **bottom sheet** (`UiKit.MakeSheet`) แทนหน้าเต็มจอ: รายการแมพ/ตั้งค่า · **การ์ดข้อมูล = pill กลางจอแบบ #seltool** (เดิมเป็นแถบเต็มกว้างทับ ☰+เข็มทิศ)
- **`UiKit.RoundedSprite()`** 9-slice ทำมุมโค้งตาม CSS (pill 14 · modal 20 · sheet 24 · ปุ่ม 13)
- `IconPainter` 16 ไอคอน · `MinimapWidget` (พื้น/สิ่งกีดขวาง/ฝูง/สัตว์/ทิศ) · `PhotoSaver` (MediaStore → Pictures/DiveMap, fallback โฟลเดอร์แอป — **เทสได้เฉพาะบนเครื่องจริง**)
- APK: `dive3d.suksomsri.cloud/dl/DiveMap-css-4147131ff8.apk`
- **บทเรียน**: UiStringsTests บังคับ **ค่าอังกฤษห้ามซ้ำ** (ToLang ต้อง idempotent) — "ถอย" ใช้ "Rev" เพราะ "Back" เป็นของ "ย้อนกลับ"

## 4.9 ▶️ RESUME 2026-07-30 สาย — P2 ปิด + P3a เกม (main `67823a34`)
- **P2a heatmap ความลึก**: `Core/DepthPalette.cs` (ramp 3 stop ของเว็บ) + `Runtime/SeabedView.cs` — เว็บสลับ vertex color แต่ Standard shader ไม่อ่าน → **เบคเป็น texture ใน UV เดียวกับพื้น อ่าน sculpt array ตัวเดียวกับเมช** · `Ui/DepthLegend.cs` (บาร์ 14×118 left 12/bottom 70 วาดจาก palette เดียวกัน)
- **P2b โหมดกลางวัน/ใต้น้ำ** `Runtime/EnvMode.cs` — ⚠️ **ค่าแสงของเว็บ (hemi 1.2 / sun 1.6) เป็นของ three.js HemisphereLight** เอามาใส่ Unity Trilight ตรงๆ ทรายขาวโพลน → ใช้ 0.72/1.15 + ปิด GodRays/Caustics ในโหมดกลางวัน
- **P3a เกม** `Core/TrashGame.cs` (11 เทส: 5 ชนิดตามน้ำหนัก · cap 30 · ทุก 5 วิ · ตก 28 u/s · อยู่ 30 วิกะพริบ 5 วิสุดท้าย · **คะแนน pts×(1+h)×(1+combo×0.1)×(coin?2:1)** · เหรียญรอบ 60 วิ ×3) + `Runtime/TrashGameSystem.cs` (เก็บในรัศมี 11u, เมชจาก Unity primitive, เซฟ local PlayerPrefs) + `Ui/CoinCounter.cs` (ป้ายกลางบน + "+N" ลอย)
- **QC เพิ่มขั้นตอน**: กด heatmap/กลางวันแล้วถ่ายรูป · ค้างในทัวร์ 6 วิถ่าย `_game.png` (รอบแรกได้ทะเลว่างเพราะถ่ายที่ 2 วิ)
- APK: `dive3d.suksomsri.cloud/dl/DiveMap-game2-67823a34*.apk` · PARITY 42% · เทส 231

## 4.95 💸 CI ประหยัดโควตา (2026-07-30) — **อ่านก่อนสงสัยว่าทำไมไม่มี APK**
GitHub บล็อกงานทั้งหมดด้วย *"recent account payments have failed or your spending limit needs to be increased"* — repo เป็น private และรอบหนึ่งกิน **44 นาที** (test 5.7 + QC 9.0 + Windows 10.9 + Android 18.4)
→ แก้ workflow: **push ปกติรันแค่ `test` + `qc-shot` (14.7 นาที, −67%)** · ไฟล์ติดตั้งสร้างเมื่อสั่งเท่านั้น
```bash
tools/request_build.sh            # สั่ง build APK+Windows จาก main → ได้ run id
tools/publish_build.sh <run> <tag>  # พอเขียว เอาไฟล์ขึ้น dive3d.suksomsri.cloud/dl/
```
⚠️ **ยังต้องรอโควตา/บิลของ user ก่อน CI ถึงจะกลับมารัน** — โค้ดที่ push แล้วแต่ยังไม่ผ่าน CI: P3b wallet (เทสผ่าน แต่ไม่มี QC) และ E8 warp gate (ยังไม่ผ่านอะไรเลย)

## 4.96 ▶️ RESUME 2026-07-30 กลางคืน — CI ปลดล็อกแล้ว + C5/E5/D9/D10 ปิด (main `HEAD`)

### CI กลับมาใช้ได้แล้ว — วิธีที่ใช้ และกำหนดหมดอายุ
บัญชี suksomsri7 มี **budget ตั้งไว้ $0 พร้อม "Stop usage = Yes" ต่อ product** (Settings → Billing → Budgets and alerts มี 5 อัน) → พอโควตาฟรีหมด Actions หยุดทันที · แก้ budget ไม่ได้เพราะ GitHub บังคับต้องมี payment method ก่อน และบัตร user ตัดไม่ผ่าน
→ **user เลือกเปิด repo เป็น public ชั่วคราว** (public = Actions ฟรีไม่จำกัด) หลังสแกน 98 commit แล้วไม่พบ key/token/.env
- 🔒 **สลับกลับ private อัตโนมัติ 1 ส.ค. 08:05 น. ไทย** — cron `5 1 1 8 *` → `tools/relock_private.sh` (แจ้ง Telegram + รายงานจำนวน fork + ถอด cron ตัวเอง) · รันมือได้ทุกเมื่อ
- ⚠️ repo `shark` **ห้าม public** — ลด CI แทน (ทำไปแล้ว: `paths-ignore` + QC suite หนักรันเฉพาะ workflow_dispatch)
- หลัง 1 ส.ค. CI ที่ลดแล้วทั้ง 2 repo ≈ 1,400 นาที/เดือน จากฟรี 2,000 → ไม่ต้องใช้บัตรอีก

### ปิดไปคืนนี้ (PARITY 36 → 46 / 86 · 53%)
| งาน | สาระ |
|---|---|
| **C5** ปลาตกใจหนีผู้เล่น | `Core/FleeMath.cs` + `Core/SpeciesGenome.cs` (พอร์ต `speciesGenome` ครบ) · panic 0..1 · **โดรนต้องเร็ว >11 u/s เท่านั้น** ลอยเข้าไปเฉยๆ ปลาไม่หนี · bait ball 2.5 วิ · วิ่งเข้าที่กำบัง · เทส 38 ตัว |
| **E5** ร้านค้า | `Core/Shop.cs` ราคา **89 รายการดึงจาก builder.html ตรงตัว** · `Core/ShopStock.cs` เก็บของที่ซื้อลงเครื่องต่อแมพ แล้ว inject กลับเข้า SceneData ตอนโหลด (ใช้ pipeline เดิม ไม่มีเส้นทางสร้างวัตถุเส้นที่สอง) · `Ui/ShopSheet.cs` |
| **D9** จุดเกิดสุ่ม | `DroneFlight.RandomSpawn` วงแหวน 20-80% ของรัศมี สูงเหนือพื้น 18u · วาปข้ามแมพแล้วลงน้ำต่อทันที |
| **D10** สอนท่าเล่น | `Ui/TutorialGuide.cs` spotlight 7 ขั้น · uGUI ไม่มี box-shadow เจาะรู → แผ่นมืด 4 ด้านล้อมช่อง |
| **A5** ปุ่มเรด้า · **E1** ป้าย ♻️ | ตำแหน่งเว็บจริง (ซ้าย 14 / บน 174) · ป้ายวาดเป็น texture ไม่ใช้ emoji |
| **E4** wallet ออนไลน์ | ต่อ `/api/wallet` แล้ว (keyed deviceId เหมือนเว็บ ไม่ต้อง login) · โหลดตอนเริ่มเกม / ส่งตอนออก / ส่งเป็นส่วนต่าง · เน็ตล่มคิวไว้ส่งรอบหน้า · CI ยืนยัน `[Wallet] seeded 600 (Success)` |
| **G1-G3** หมุด | `Core/PinMedia.cs` (กรอง url เหลือ http/https + เทส 11 ตัว) · `PinMarker` หมุดแดงวาดเอง unlit · `PinSheet` ดูรูป + ตัวนับ 3/7 · ⚠️ แมพเดโมไม่มีหมุด → `[Pins] placed 0` ยังไม่มีภาพยืนยัน |
| **A7** ตัวเลขเฟรมเรต | มุมซ้ายบนตามเว็บ · แสดง FPS/**min**/จำนวนปลา · เปิดปิดในหน้าตั้งค่า — ให้ user ตอบ "ลื่นไหม" ด้วยตัวเลข |
| 🐞 **wallet** | `NeedsSeed(server.HasValue == false)` **ตรรกะกลับด้าน** → throw ทุกครั้ง (เจอจาก log QC ไม่ใช่จากเทส) · เพิ่ม overload รับ `int?` ให้กลับด้านไม่ได้อีก |

### บทเรียนคืนนี้ (ห้ามลืม)
1. **PARITY.md ที่ผมเขียนเองมี 3 ข้อที่จดผิด** — แก้แล้วทั้งหมด ตรวจกับ CSS/โค้ดเว็บจริงก่อนลงมือทุกครั้ง:
   - toast เว็บอยู่ **กลางจอ** (`#toast{top:50%;left:50%}` :167) ไม่ใช่ล่าง — ของเราถูกอยู่แล้ว
   - `#hint` เป็นของโหมด **แก้ไข** (`body.view #hint{display:none}` :184) ไม่ต้องทำในหน้าดูแมพ
   - **เว็บไม่มีโดรนบินเอง** — `tourCam`=ปุ่มกล้อง, `_tourInstBuild`=รวม draw call · auto-tour ของเว็บ = เข้าทัวร์อัตโนมัติ+จุดเกิดสุ่ม
2. **`Outline` component บนรูปที่โปร่งใส 100% = เส้นโปร่งใส** (ก๊อป mesh เดิม) → ใช้ `UiKit.RoundedSprite(r, border)` ที่วาดขอบลง sprite จริง
3. **material รับแสงใต้น้ำ = ป้ายเกมกลายเป็นวงกลมเทา** → ทำเป็นแผ่นกลม**ทึบ** + `DM_GltfUnlit` (ตัด transparency ทิ้ง ไม่ต้องแตะ keyword ที่โดน strip)
4. **เข้าโหมดทัวร์ = กล้องวาร์ป** → ความเร็วกล้องเฟรมนั้น 467 u/s (โดรนวิ่งได้ 30) ทำฝูงปลาตกใจฟรี → เกิน 3 เท่าของความเร็วโดรน = ถือเป็นวาร์ป ล้างค่าทิ้ง
5. **push ทับระหว่าง CI รันอยู่ = รอบเดิมถูกยกเลิก** (`concurrency: cancel-in-progress` ต่อ ref) — ถ้าสั่ง build APK ไว้ อย่า push จนกว่าจะจบ

### ✅ ปิดแล้ว — C5 พิสูจน์ได้จาก CI (เคยสงสัยว่าเป็นบั๊กเกม ไม่ใช่)
`[Tour] velXZ=29.6/30` และ `[Flee] panic=0.22 camSpeed=15.7/11` — โดรนเร่งถึงความเร็วเดินทางและปลาตกใจจริง
**สาเหตุที่เคยตันที่ 10.4 u/s: จำนวนเฟรม ไม่ใช่โค้ด**
- CI รันได้ `realDt=0.333` = **3 เฟรม/วินาที** (Unity cap `maximumDeltaTime`)
- `DroneFlight.Inertia = 0.09` เป็น **ต่อเฟรม** (กฎเว็บ ไม่ผูก dt) → 5 วินาที = 15 เฟรม = แค่ ~22 u/s
- 🔑 **บทเรียน: การรอใน QC harness ต้องนับเป็นเฟรม ไม่ใช่วินาที** เมื่อสิ่งที่ทดสอบเดินตามเฟรม
  `for (int f = 0; f < 60; f++) yield return null;` — 60 เฟรม = ~99% ของความเร็ว บนทุกเครื่อง

### ✅ ยืนยันแล้วว่าซื้อของในร้านทำงานครบวงจร (QC run 30586596945)
```
[QC] buy test map=wl6zwxh1tdgn item=losin:shrimp_acrobat price=50 coins=600 stock=0
[Shop] bought losin:shrimp_acrobat for 50 → coins=550
[Shop] released losin:shrimp_acrobat at (1,198,450) on map wl6zwxh1tdgn
[Shop] restored 1 purchased item(s) for wl6zwxh1tdgn      ← โหลดแมพใหม่แล้วของที่ซื้อกลับมา
[QC] buy result coins 600→550 (expected 550) · stock 0→1 (expected 1)
```
⚠️ จุดที่ยังไม่ได้ทำ: ถ้าเปิดร้านจาก**โหมดดูแมพ** จุดปล่อยจะใช้ตำแหน่งกล้อง orbit ที่ลอยสูง (y=198)
— ตอนนี้ปุ่มร้านอยู่ใน HUD ทัวร์อย่างเดียว ผู้เล่นจริงจึงไม่เจอ แต่ถ้าเพิ่มทางเข้าร้านในหน้าดูแมพเมื่อไหร่ ต้อง clamp ความสูงก่อน

## 4.97 ▶️ RESUME 2026-07-31 เช้า — **อ่านบล็อกนี้ก่อนทุกอย่าง** (user ทักด้วยภาพ 2 รูป)

### 🔴 สิ่งที่ทำผิดและต้องแก้ก่อนทำอะไรต่อ
user ส่งภาพหน้าจอเว็บจริง 2 รูป — **เก็บไว้ในรีโปแล้ว เปิดดูได้เลย**:
`docs/refs/web-palette.png` (หน้า palette = ร้านค้า) · `docs/refs/web-maplist.png` (หน้ารายการแมพ)
พร้อมย้ำว่า **"ต้องไร้รอยต่อ ux ui ต้องเหมือนกัน"**

**(1) พอร์ตร้านค้าผิดหน้า** — บนเว็บ **การวางวัตถุคือการซื้อ** (`tryPlace()` :4298 หักเหรียญตอนวาง)
→ **palette คือร้านค้า** ที่ผู้ใช้เจอจริง แต่ผมไปพอร์ต `openShop()` (:4301) ซึ่งเป็นรายการสำรองในโค้ด
→ แล้วยังจัด palette ไว้ในหมวด I "Builder แก้ไข" ที่เลื่อนไปทำท้ายสุด — **จัดลำดับผิดตั้งแต่ต้น**
หน้า palette ที่ต้องทำ (จากภาพ):
- ชิปหมวด **10 อัน**: Rock 🪨 · Coral 🪸 · Boat ⛵ · Marine life 🐢 · School 🐟 · Artificial reef 🗿 · Special ✨ · Pin 📍 · Settings ⚙️ · Sculpt floor 🏔️ (ชิปที่เลือกมีขอบฟ้า)
- กริดการ์ด **รูปเรนเดอร์ 3D จริง** + ชื่อใต้รูป (Rock / Large rock / Rock pile / Rock 1 …)
- 🪙 600 กลางบน · ‹ ย้อนกลับ ซ้ายบน · ▶ เล่น ขวาบน · แผ่นล่างมี grip

**(2) หน้ารายการแมพเคลม ✅ ทั้งที่ขาดครึ่งหน้า** (A4 แก้เป็น ⚠️ แล้ว)
ขาด: **แบนเนอร์ "Play Game!"** (ตราทอง + "Dive in, collect coins & clean up the reef" + ปุ่มเล่นเขียว) ·
ปุ่ม **+** สร้างแมพ · ปุ่ม**บัญชี** (วงกลมตัวอักษรแรก) · การ์ด **2 คอลัมน์รูปใหญ่** ·
**♡ + จำนวนถูกใจ** · **☁ เก็บออฟไลน์** · **⋯ เมนู** · `by ชื่อคนสร้าง` (API ส่ง `ownerName` มาแล้ว **แต่โค้ดไม่ได้ใช้**) ·
ไอคอนแว่นในช่องค้นหา + placeholder "ค้นหาจุดดำน้ำสาธารณะ…"

### ✅ ลำดับงานใหม่ที่เสนอ user ไว้ (รอ user เคาะ — ยังไม่เริ่ม)
1. **หน้ารายการแมพให้เหมือนภาพ** (~3-4 ชม) — หน้าแรกที่คนเปิดแอปเจอ
2. **palette = ร้านค้าจริง** (~4-5 ชม) — งานหนักสุดคือ **เรนเดอร์ thumbnail 3D จาก GLB** เอง
3. **วางวัตถุจริง** (แตะ palette → วางลงแมพ = ซื้อ) — ทำให้ข้อ 2 มีความหมาย
4. **J บัญชี** (login) — ปลดล็อกปุ่ม + และปุ่มบัญชีในข้อ 1
5. gizmo/undo/sculpt → เชือก → AR

### 🖼️ โมเดลความละเอียดต่ำ — ตอบ user แล้วว่าแก้ทีหลังได้
- **ต้นฉบับอยู่ครบ**: `/root/asset-masters/` **7.6 GB · 207 ไฟล์** · `MANIFEST.md` เขียนว่า *"DO NOT optimize in place — kept for future high-quality work"*
- ตัวอย่าง: รูปปั้นสิงห์ raw **47.6 MB / 80k tris / 4K×4** → ที่ใช้จริง **0.46 MB** (tris 25% · tex 1024) · วาฬ raw 28-35 MB
- เหตุผลที่ย่อ: WebView เคยพัง GPU (VRAM 90MB→3MB/โมเดล) — **แอป native ไม่ติดข้อจำกัดนี้**
- ⚠️ **สัตว์ raw ไม่มี rig/animation** → เอามาแทนตรงๆ ไม่ได้ ต้องรัน `optimize → auto-rig → swim clip` ใหม่ทีละตัว (ดู [[reference_marine_asset_pipeline]])
- แนะนำทำ **3 tier** (ไกล/ใกล้/พระเอก) ไม่ใช่ยก 100k tris ทั้งหมด — เป็นงานปรับคุณภาพ ทำหลังฟีเจอร์

## 4.98 ▶️ RESUME 2026-07-31 — ✅ ปิดข้อ 1 หน้ารายการแมพ (รอ CI)

### 🔑 สิ่งที่ต้องรู้ก่อนแตะหน้านี้ต่อ
**ภาพ `docs/refs/web-maplist.png` ไม่ใช่ builder.html — เป็นแอป React Native**
`/root/projects/siamdive-rn/src/app/map.tsx` (623 บรรทัด) + `src/lib/mapI18n.ts`
→ ค่าทุกตัว (สี/ระยะ/ขนาด/ข้อความ EN+TH) ถอดจาก StyleSheet ของไฟล์นั้นตรงๆ ไม่ได้เดาจากภาพ
**ทุกงาน UI ที่เหลือให้เปิดไฟล์นี้ก่อนเสมอ** (ดู [[feedback_verify_web_source_before_porting]])

### ทำอะไรไป
| ของใหม่ | สาระ |
|---|---|
| `Ui/MapListScreen.cs` | **เขียนใหม่ทั้งไฟล์** — จาก bottom sheet แถวเดี่ยว 74px → **หน้าเต็ม** + กริด 2 คอลัมน์ · header `‹ [🔍 ค้นหา] + (บัญชี)` · แบนเนอร์ Play Game! · การ์ด: รูป 100px มุมมน + ชื่อ + `by …` + ♡n + ⋯ |
| `Ui/WorldsPopup.cs` | แตะแบนเนอร์ → เลือกโลก (กรอง `accountId == ADMIN_ACCT`) → **ลงน้ำเลย** (`ArrivingByWarp`) |
| `Ui/ActionSheet.cs` | แทน `Alert.alert(title, buttons)` ของ RN — ใช้กับเมนู ⋯ |
| `Core/MapGridLayout.cs` | เลขวางกริดล้วนๆ + เทส 13 ตัว (กันบั๊ก "ทุกการ์ดไปคอลัมน์ 0") |
| `Core/LikedMaps.cs` | จำว่าเครื่องนี้กดหัวใจแมพไหน + เทส 11 ตัว |
| `Runtime/MapReactClient.cs` | `POST /react` + `/report` จริง |
| `StreamingAssets/coin.png` | เหรียญทอง SCUBA DIVING ก๊อปจาก RN (วาดเองไม่ได้ เป็นภาพถ่าย) |
| `UiKit.RoundedCutoutSprite` | ครอบมุมรูปให้มน (RawImage 9-slice ไม่ได้ · เลี่ยง stencil Mask) |
| ไอคอนใหม่ | `search / plus / heart / heartfill / dots / person / image` (Ionicons 24 viewBox) |

### ⚠️ 3 อย่างที่ตั้งใจ**ไม่ทำ** (อย่าเคลมว่าเสร็จ)
1. **☁ เก็บออฟไลน์** — แอปยังไม่มีที่เก็บแมพในเครื่องเลย ใส่ไอคอนไปก็โกหก → รอทำระบบออฟไลน์จริง
2. **`by You`** — ต้อง login ก่อน (หมวด J = ข้อ 4 ในคิว) · `MapDirectory.OwnerKindOf` มีกิ่ง `isMine` รออยู่แล้ว
3. **ปุ่ม + และปุ่มบัญชี** — วาดครบตามภาพ แต่กดแล้วขึ้น toast "ยังไม่เปิดให้ใช้ในแอปนี้"

### 🐞 เจอบั๊กในแอป RN ที่ขายอยู่จริง (ยังไม่แก้ — คนละ repo)
`siamdive-rn/src/lib/dive-map-client.ts:73` — `react()` **ไม่ได้ส่ง `deviceId`** แต่ route บังคับ
(`react/route.ts:16` → 400 `deviceId required`) และ error ถูกกลืนใน `try{}catch{}`
→ **ปุ่มหัวใจในแอปมือถือขึ้นเลขให้ดูเฉยๆ ไม่เคยบันทึกจริง** · ฝั่ง Unity ส่ง deviceId ถูกแล้ว
แก้ 1 บรรทัด ถ้า user สั่ง

### 📌 §4.97 มี 1 ข้อที่จดผิด
"API ส่ง `ownerName` มาแล้วแต่โค้ดไม่ได้ใช้" — **ไม่จริง** โค้ดเดิม (`MapListScreen.cs:410-413`)
ใช้ `card.OwnerName` อยู่แล้ว ที่ขาดจริงคือกิ่ง `by SIAMDIVE` (admin) และรูปแบบ `by <ชื่อ>` ของ RN

### ต้องเช็คอะไรตอน CI เขียว
```
[UI] maps cards=N total=N banner=ok worlds=5 err=            ← banner=MISSING แปลว่าเหรียญไม่ขึ้น
[UI] grid cols=2 cardW=… cardH=… banner=on coin=ok
[UI] card0 name='…' meta='by SIAMDIVE' likes=1 …             ← chars=0 = ข้อความหาย (ดู LineHeightRatio)
[UI] qcui worlds popup=open rows=5
```
ภาพใหม่ในอาร์ติแฟกต์: `qc_ui_maps.png` (เทียบกับ `docs/refs/web-maplist.png`) + **`qc_ui_worlds.png`**
⚠️ QC รันที่ 1280×720 แนวนอน → กริด 2 คอลัมน์จะกว้างมาก **ไม่ใช่บั๊ก** (RN ก็ `numColumns={2}` ตายตัว)

### ✅ ผล CI run `30593417408` (เขียว) — ตรวจแล้ว
```
[UI] maps page q='' skip=0 got=6 total=6
[UI] grid cols=2 cardW=927 cardH=308 banner=on coin=ok
[UI] card0 name='Hanuman' meta='โดย SIAMDIVE' likes=1 rect=897x38 chars=7 lines=1
[UI] qcui maps cards=6 total=6 thumbs=5 banner=ok worlds=5 err=
[UI] qcui worlds popup=open rows=5
[UI] qcui search q='Chang' cards=1 → banner=off        ← ซ่อนแบนเนอร์ตอนค้นหา ถูกแล้ว
```
ยืนยันจากภาพ: header ครบ 4 ปุ่ม · แบนเนอร์เหรียญทองขึ้นจริง · กริด 2 คอลัมน์ · **รูปมุมมนจริง**
(`RoundedCutoutSprite` ทำงาน) · ชื่อหนา + `โดย SIAMDIVE` + ♡n + ⋯ (มีพื้นหลังจางตามเว็บ) · popup เลือกโลก 5 แถว

### ❗ 2 อย่างที่ยัง**ไม่มีภาพพิสูจน์** (อย่าเคลมว่าตรวจแล้ว)
1. **หน้านี้ตอนภาษาอังกฤษ** — `qc_ui_en.png` ถ่ายหน้า info card ไม่ใช่หน้ารายการแมพ
   `RefreshLanguage()` ถูกเรียกจริง (`[UI] language=en retranslated=32`) และตารางคำผ่านเทส latin-gate
   แต่ยังไม่เห็นภาพ → **ให้เพิ่ม shot ตอนรอบ CI ถัดไป** (เปิด map list ซ้ำหลังสลับภาษา)
2. **เมนู ⋯ (`ActionSheet`)** — ยังไม่มี shot เลย · qcui ไม่ได้กดปุ่มการ์ด
3. เลย์เอาต์บนจอ**แนวตั้งจริง** ยังไม่เคยเห็น (QC เป็นแนวนอนล้วน) → เห็นตอนส่ง APK

## 5. งานถัดไปทันที (คิวเรียงแล้ว)
### 5.1 ✅ ปิดแล้ว — WO-XR-03 (2026-07-28)
formation ตามสูตรเว็บ + วาฬ GLB จริง + QC fixes (ครีบดำ/gloss/heading log) · commit `a7d12f8` → `f31d9fc`

### 5.2 ✅ ปิดแล้ว — WO-XR-05 ครบ 4 ก้อน + build ตัวเต็มส่ง user แล้ว
`06f88ce` (CI run `30456759388` เขียว) → APK `dive3d.suksomsri.cloud/dl/DiveMap-full-16704d4e60.apk` + Windows zip เดียวกัน

**บทเรียนห้ามลืม: legacy `Text` + `VerticalWrapMode.Truncate` "ทิ้งทั้งบรรทัด" ถ้ากล่องเตี้ยกว่า fontSize × 1.511** (NotoSansThai-Regular: ascender 1061 / descender 450 / unitsPerEm 1000, USE_TYPO_METRICS) — ใช้ `UiKit.RowHeight(size, lines)` เสมอ อย่าใส่ความสูงเป็นเลขดิบ

### 5.2b ✅ ปิดแล้ว — WO-XR-04 ทั้ง 3 ก้อน (QC run `30490750535`, main `dc5c92d0`)
oracle จาก `qc_player.log` ผ่านครบ:
```
[Scene] backdrop ready via=baseColorTexture stops=4 tex=8x256
[Scene] sand texture 1024² baked speckle=12.1u/cell haze=0.55→1.00 rim=(0.05,0.20,0.33)
[Scene] seabed rx=306.0 rz=374.0 rings=28 seg=96 thickness=40.0 skirt=OK waterLevel=240.0 itemMaxR=256.7
[Scene] godrays beams=10 spread=220 len=260 dir=(-0.353,-0.788,0.504) width=16.5 opacity=0.30
[Marine] fishGlb species=school:scad … tex=OK baseLen=1.911 expected=1.911
[Marine] fishGlb species=school:barracuda … tex=OK baseLen=1.862 expected=1.862
[Marine] fishGlb species=pod:yellowtail … Trevally_xr1.glb lod1=True tris=3999 tex=OK baseLen=1.869
[Marine] configured schools=10 fish=1100 whale=1 · whale heading dot=1.000
```
ภาพ: ฉากหลังไล่สีจริง · ลำแสง 10 ลำขนานดวงอาทิตย์ · ขอบทรายละลายเป็นน้ำเงิน · ปลามี texture ครบ 3 · การ์ด/เมนู/ตั้งค่าไม่ regress
ไฟล์เทสที่ส่ง user: `dive3d.suksomsri.cloud/dl/DiveMap-fish-dc5c92d0fb.apk` + `DiveMap-win-fish-dc5c92d0fb.zip` · ภาพเทียบ `xr04-before-after.png`, `xr04-closeup-before-after.png`

**user เคาะแล้ว (2026-07-29 กลางคืน): "ลำแสงควรนุ่มกว่านี้มากๆ"** → เปลี่ยนจากกรวยทึบเป็น **billboard quad หันเข้ากล้อง** (แกนล็อกตามดวงอาทิตย์ หมุนรอบแกนตัวเองเท่านั้น) + falloff 64² = ramp ยาว × fade ตรงผิวน้ำ × **bell ยกกำลังสองตามความกว้าง** (alpha = 0 ทุกขอบ → ไม่มี silhouette) + opacity 0.30→**0.11** + กว้างขึ้น (spread×0.16, 12-48u) · เทสใหม่ 4 ตัวใน `GodRayMathTests` ปักว่าขอบทุกด้านเป็นศูนย์
**ที่ยังไม่เคาะ**: ทรายช็อตกว้างดูขาวสว่าง (caustics alpha 0.13) — รอ user บอก

**perf**: llvmpipe avgFrameMs 135.6 (ก่อน 04) → 300.6 (หลัง) — เป็นเลข software renderer ที่วาดทีละตัว 1,100 ครั้ง ไม่ใช่ตัวแทนมือถือ (มือถือใช้ instanced path) · เกณฑ์ LOD ที่ใช้จริง = `count × tris > 200k` (pod จริง 50 ตัว/ฝูง → count-based ของแผนเดิมพลาด)

**บทเรียนรอบนี้**
- `asset_manifest.json` **ไม่มี tris** → การเลือก LOD ต้องมีตารางจาก survey ใน `Core/FishAssetPick.cs` (species ที่ไม่อยู่ในตาราง = ใช้เมชโพรซีดิวรัลต่อ ไม่เดา GLB)
- mesh จาก Draco เป็น **non-readable** → ห้าม `mesh.vertices`; bake node matrix เป็น `Matrix4x4` คูณท้าย instance TRS แทน `geo.applyMatrix4` ของเว็บ · วัดขนาดจากมุม `mesh.bounds`
- glTFast unlit/PBR shader ใช้ชื่อ property **`baseColorTexture`** (ยืนยันจาก log ทั้ง fish material และ backdrop) — `_MainTex` ไม่มี
- ฉากหลังของเว็บเป็น **screen-space** (canvas texture ใน `scene.background`) ไม่ใช่ sky dome → ใน Unity ใช้ quad ลูกกล้อง + queue Background + วางที่ 0.95×far (กันกรณี shader เขียน depth แล้วบังพื้นไกล)
- **far plane 1000 ไม่พอ** เมื่อพื้นเป็น 340u เต็ม (zoom out 950u) → ตั้ง 9000 (near 0.5)
- fog: ต้องเปิดใน `Main.unity` RenderSettings ด้วย ไม่ใช่แค่ runtime ไม่งั้น shader variant ถูก strip
- `Mathf.PerlinNoise`/`Math.Pow` ในลูป texture 1024² = ล้านครั้ง → ทำตาราง per-angle + sqrt-สองชั้น (เว็บก็ทำ)
- เขียนเทสเอง**พลาดเอง**ได้: `prev=2f` แต่ luminance stop บนสุด 2.812 → CI แดงทั้งรอบเพราะเทส ไม่ใช่โค้ด (166/167 ผ่าน) — ตรวจค่าเริ่มต้นของ monotonic test ให้เป็น `float.MaxValue`

### 5.3 ✅ ปิดแล้ว — WO-XR-05 ทั้ง 4 ก้อน (แผนเดิม `/root/projects/siamdive-xr-docs/WO-XR-05.md`)
งานต่อยอดที่ยังค้าง: ป้ายสถานะบนหัวจอของ AppBoot เป็นสตริงประกอบ (`"{ชื่อ} · โหลดแล้ว N · แทนที่ M"`) ยังไม่แปลตามภาษา · โหมดประหยัดยังไม่ลดจำนวนปลา (ต้องแตะ FishSchoolSystem) · การ์ดยังไม่โชว์รูปจาก pin (SceneBuilder ยังไม่สร้าง pin ในฉาก)

### 5.4 เก็บเล็ก
- ปลายังเป็นเมช procedural ไม่มี texture (เว็บเป็นปลาเขียว-เงิน) → งานจริงคือ **WO-XR-04** ปลา GLB รายตัว · **อย่ายัดรวมรอบอื่น** (เสี่ยง magenta/KTX2 บน llvmpipe)
- ตรวจ log `[Marine] whale heading dot(forward,vel)` ควร ≈ +1.0 ถ้าติดลบ = GLB หันกลับ ให้หมุน child yaw 180° (ห้ามแก้ WhaleController)
- ⚠️ **`WhaleController` ชื่อผิดแล้ว** — ตั้งแต่ C6 phase 2 มันขับ**สัตว์เดี่ยวทุกตัว** (88 placement) ไม่ใช่แค่วาฬ
  อ่านว่า "SoloAnimalController" (log ใช้ `[Solo]` แล้ว) · ยังไม่ rename เพราะ type ถูกอ้าง 6 จุด + doc 3 ที่
  และเครื่อง dev ไม่มี UnityEngine.dll → `check.sh` เช็ค syntax อย่างเดียว rename พลาดจุดเดียว = CI แดง 35 นาที
  **ให้ rename ในรอบที่มีงบ CI อยู่แล้ว** (แก้ EnvMode, SceneBuilder ×3, FishSchoolSystem, ArPlacement, ArKitSession + doc)
- ตรวจ log `[Solo] attached=N of M` — N<M = GLB ไม่ลง · M ต่ำผิด = routing ไม่รู้จักสัตว์
- ทรายเป็นวงรีครีมแบน เว็บมี sculpt + ไล่สีน้ำเงิน · hull เรือมีแผ่นดำ (backface ของ wreck GLB)

### 5.5 คิวใหญ่ถัดไป (user อาจสลับลำดับ — ถามก่อน)
WO-XR-04 (ปลา GLB จริง + caustics/FX) ↔ WO-XR-02m AR (ARCore) → WO-XR-06 โหมดแก้ไข → WO-XR-07 ขึ้น Play

## 6. เครื่องมือ/การเข้าถึง (บนเครื่อง VPS นี้)
- **GitHub API** (ดู CI/โหลด artifact): token อยู่ใน `~/.git-credentials` → `GH_TOKEN=$(sed -n 's#https://suksomsri7:\([^@]*\)@github.com#\1#p' ~/.git-credentials)` แล้ว curl `api.github.com/repos/suksomsri7/siamdive-xr/actions/...` (gh CLI ไม่ได้ login)
- **QC artifact**: run ล่าสุด → artifact `qc-screenshot` → `qc_screenshot.png` (มุมกว้าง), `qc_screenshot2.png` (ประชิดฝูงปลา), `qc_player.log` (grep `[Marine]`/`[QC]`)
- **ภาพเว็บอ้างอิง**: `NODE_PATH=/tmp/node_modules node /root/dive3d/qc_web_reference.mjs wl6zwxh1tdgn /tmp/qc_web_reference.png` (puppeteer + swiftshader)
- Vercel token / Neon / อื่นๆ: อยู่ใน memory dir `/root/.claude/projects/-root/memory/reference_*.md`
- ส่งไฟล์ใหญ่ให้ user: วาง `/var/www/dive3d/dl/` (Telegram ส่ง >50MB ไม่ได้)
- **สั่ง build APK/Windows** (push ปกติ**ไม่**สร้างไฟล์ติดตั้ง เพื่อประหยัดโควตา — ดู §4.95):
  ```bash
  bash tools/request_build.sh main        # → ได้ run id
  bash tools/publish_build.sh <run> <tag> # เขียวแล้วเอาขึ้น dive3d.suksomsri.cloud/dl/
  ```
  ⚠️ **ห้าม push ระหว่างที่ build ค้างอยู่** — `concurrency: cancel-in-progress` ต่อ ref จะยกเลิกรอบนั้นทิ้ง (พลาดมาแล้ว 3 ครั้ง)
  ถ้า push ไปแล้วให้ cancel รอบ push ก่อนแล้วค่อย `request_build.sh` ใหม่
- **ภาพ QC ชุด UI** (`-qcui`): `qc_ui_menu/maps/search/card/settings/perf/depth/daylight/toast/tour/game/flee/shop/tutorial/bought/en.png` + `qc_ui_player.log`
  grep คีย์ที่มีประโยชน์: `[Flee]` `[Shop]` `[Pins]` `[Wallet]` `[Tutorial]` `[Perf]` `[QC] buy`
- **ภาพอ้างอิง UI จาก user**: `docs/refs/web-palette.png` · `docs/refs/web-maplist.png` (เทียบก่อนทำ UI ทุกครั้ง)
- **โมเดลต้นฉบับความละเอียดสูง**: `/root/asset-masters/` (7.6 GB · MANIFEST.md) — ดู §4.97

## 7. โปรโตคอล QC (ถ้ามี orchestrator/reviewer แยกจาก executor)
1. executor ทำงานตาม work order → commit → push ครั้งเดียว → **หยุด รายงานสิ่งที่ทำ + จุดเสี่ยง**
2. reviewer รอ CI เสร็จ → โหลด qc-screenshot 2 มุม + player log → เทียบ side-by-side กับภาพเว็บอ้างอิง + ตรวจตัวเลขใน log
3. reviewer เขียน verdict: ผ่าน / ไม่ผ่าน+สาเหตุ+สมมติฐานเรียงลำดับ → ส่งเป็น work order รอบถัดไป
4. ประวัติ QC ที่ผ่านมา (บทเรียนสำคัญ): เรือจม=pivot ไม่ ground, มืด=metallic ไร้ reflection, ปลาหาย=llvmpipe instancing, ปลายักษ์=ตีความ scale ผิดชั้น (item.s = ขนาดก้อนฝูง ไม่ใช่ปลารายตัว) — **อย่าเดา ให้อ่านโค้ดเว็บ/ข้อมูล API เป็นเลขจริงก่อนตั้งค่าเสมอ**

---

## §6 — Session 2026-08-02→03 (มาราธอนปิด 4 เป้า + คดีปื้นดำ) — จุดต่องานสำหรับ session ใหม่

### สถานะ ณ จบ session
- main = `b391101` · **build TestFlight ยิงแล้ว run `30771246633`** (workflow_dispatch ios=true) — เช็คผล: `gh run view 30771246633` ถ้า UPLOAD SUCCEEDED รอ user เทส
- เทส EditMode ~351 (`bash tools/test.sh` รัน pure-logic บนเครื่องได้ ไม่ต้องรอ CI · `tools/check.sh` = Roslyn syntax)
- ใน build นี้: สมองสัตว์เต็มระบบ (FishMind+SpeciesBehavior 94 แถว+HuntMath+Solo 88 ตัว) · SwimStyle+body wave · โดรน 9u/s=1.5m/s + spawn ที่ warp gate · OBB frame-space collision + push-out 6 หน้า · แคช Generation=2 + LooksComplete · จอ QC โหลดโมเดลจริง 6 ตัว + probe 7 เฟรม + gate baseline

### คดีปื้นดำ (ปิดแล้ว — อ่านบล็อก "the black-patch case, closed" หัว SceneBuilder.TameMetal)
ราก 3 ชั้น: ① tangent NaN ในไฟล์ (ซ่อม 444) ② แคชไม่รับไฟล์ซ่อม (Generation gate) ③ **UV gutter ดำ + chart seam → GPU เลือก LOD ลึก = ค่าเฉลี่ย atlas มืด** → แก้ที่ pipeline: jump-flood gutter dilation (`siamdive-xr-models/dilate_gutter.mjs` + `glb_swap_image.py` — สลับ texture โดย geometry ไม่ขยับ 1 ไบต์ → solids ตรงเดิม) ทำแล้ว 5 โมเดล 10 ไฟล์
- MR texture คืนแล้ว (สี/วาวจริงโชว์ครั้งแรก) · **normal map ยังถอดใน Gamma** (sRGB misdecode 53.4°/texel — คืนอัตโนมัติเมื่อย้าย Linear, เงื่อนไขใน `GlbShading.NormalMapIsMisdecoded`)
- วิธีที่ชนะ: probe A/B ในเฟรมจริง (whiteAlbedo/noMetalRough/meshNormals/whiteGltf/greyAlbedo/mipBias) + 4 verdict — สมมติฐานผิด 5 ตัวตายด้วยการวัด: etc1s transcode bit-identical · mip bleed ไม่แตะผิว · normal map A/B 10.92→10.92 · env cube ลดแล้วแย่ลง (มันพยุงเงา) · bias ไม่มีค่าที่แยก seam(LOD7-10) จากปกติ(1.01)

### กติกาที่เพิ่มใน session นี้ (ห้ามลืม)
1. **หลักฐานภาพต้องมาจากตัว Unity เท่านั้น** — เว็บ render/proxy ไม่นับ (user จับได้ว่าลักไก่ 1 ครั้ง)
2. gate `darker-than-baseline` ต่อโมเดล — **เปลี่ยน lighting/material pipeline เมื่อไหร่ ต้อง re-record baseline หลังมองรูปด้วยตา** (`QcModelShot.DarkBaselines`)
3. เฟรมเดียวแยก "ลายเข้มจริง" จาก "ปื้นเสีย" ไม่ได้ — วัดพิสูจน์แล้ว 3 metric
4. อย่าใส่ mipMapBias (biasVerdict=no-mottle แล้ว — มี regression guard ในเทส)
5. dilation batch กว้าง: เกณฑ์คัดคือ **dLum@mip5 ติดลบ** (`mipdrift.mjs`) ไม่ใช่ lum<0.5

### คิวถัดไป (เรียงความสำคัญ)
1. รอ user เทส build บนไอโฟน (ลอดรู T-13/ช้าง · ซุ้ม Atlantis · โดรนช้า+spawn warp · ปลามีสมอง · รูปปั้นสี-วาวจริง)
2. **Linear color space migration** — คืน normal map ทั้งระบบ (เทียบภาพทั้งแอปก่อน)
3. dilation batch กว้างทั้งแคตตาล็อก (คัดด้วย mipdrift)
4. hero animal ไม่เคยใช้ LOD1 (`SceneBuilder.LoadBigAnimalAsync` ไม่เรียก ResolveLod1Url — ประหยัด 71%/ตัว)
5. GoldFx.ApplyGold เป็น no-op (ชื่อ property ไม่ตรง glTF shader) · env spec ฟ้าแบน (cubemap 4×4 ไม่มี mip — **ห้ามลด intensity เดี่ยวๆ** วัดแล้วแย่ลง ต้องแก้เป็นชุด)
6. pod:yellowtail รอ raw trevally จาก user · cor:crimson1/nat:peak ต้องหา asset ใหม่ · procedural 19 ตัวถ้าจะสวยต้องสร้างใหม่ (Meshy)
7. WhaleController → SoloAnimalController rename (จุดอ้าง 6 ที่ — ดู §5.4) · roamR วาฬจาก cfg (170 vs 98 กระทบเฟรมช็อต QC)
8. เว็บ: cache เบราว์เซอร์เก่าเห็นซุ้ม Atlantis เดิมได้ถึง 1 ปี (immutable) — เติม query string ฝั่ง builder.html หลังเทส offline shim

### โมเดล (245/275 ชี้ Bunny 2048²)
ledger ทั้งหมดใน `/root/projects/siamdive-xr-models/*.jsonl` (commit `0cd3674`) · raw archive `/root/asset-masters/` + `predilation_backup/` (rollback dilation ได้) · maps repo = `4d8a1d7`
