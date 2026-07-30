# HANDOFF — DiveMap (Unity Dive Map) สำหรับ AI agent ที่มาทำต่อ

> เอกสารนี้เขียนเพื่อให้ AI coding agent ใดๆ (Codex / Kimi / Claude / อื่นๆ) ทำงานต่อได้ทันที
> อ่านคู่กับ: `DESIGN_DOC.md` (สัญญาหลัก v1.2), `QC_PLAN.md`, `SECURITY_PLAN.md`
> อัปเดตล่าสุด: 2026-07-29 กลางคืน (**WO-XR-04 ปิดครบ 3 ก้อน · CI `30490750535` เขียวทุก job · QC ผ่าน 5/5** · main = `27e0568`)

## 1. โปรเจกต์คืออะไร
- แอป **DiveMap** (`com.siamdive.divemap`) — Unity 6000.0.79f1 ใน `DiveMap/`
- แสดงแมพจุดดำน้ำ 3D จากระบบเว็บ **maps.siamdive.com** (builder.html/Three.js) ผ่าน API เดิม อนาคตแทนเว็บทั้งระบบ
- เป้าหมายระยะนี้ = สาย A (มือถือ Android) ตาม roadmap ใน DESIGN_DOC §5
- **มาตรฐานคุณภาพ: ภาพต้องเทียบเว็บจริงข้างกันแล้วไปทางเดียวกันหรือดีกว่า** (user ตรวจแบบนี้)

## 2. สถานะปัจจุบัน (อะไรเสร็จแล้ว)
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
