# DiveMap (SiamDive HoloMap XR) — Design Doc ฉบับเต็ม v1.2

> **สถานะ:** DRAFT รออนุมัติ · 2026-07-17 · ผู้เขียน: Claude (Fable 5)
> **ชื่อแอป:** **DiveMap** (package `com.siamdive.divemap` · ชื่อบน store อาจต้องต่อท้ายเป็น "DiveMap — SiamDive" ถ้าชื่อชนบน Google Play)
> **ทิศทางใหญ่ (v1.2, ตัดสินใจโดย user 2026-07-17):** **Unity = ระบบเต็มตัวจริงในอนาคต แทนเว็บทั้งหมด** (รวม builder) เพราะความละเอียด/ความลื่นเหนือกว่า WebGL — เว็บเดิมเข้าสู่ maintenance mode (ประคอง ไม่เพิ่มฟีเจอร์) จนกว่า Unity จะ parity 100% แล้วจึงปิด builder เว็บ · ระหว่างเปลี่ยนผ่าน แมพทุกอันต้องเปิดได้ทั้งสองฝั่ง → scene format contract (§1.2) ยิ่งศักดิ์สิทธิ์
> **เป้าหมาย:** แผนที่ดำน้ำ 3D ลอยบนโต๊ะแบบ Mixed Reality (สไตล์ HoloMaps) บน **Android XR (Galaxy XR)** ด้วย **Unity 6** — ใช้ **แมพเดียวกับ maps.siamdive.com** แก้ที่ไหนเห็นที่นั่น

---

## 0. วิสัยทัศน์ & ขอบเขต

**ภาพสุดท้าย:** ผู้ใช้ใส่ Galaxy XR → เห็นห้องจริง (passthrough) → จานโฮโลแกรมวงกลมลอยบนโต๊ะ → ในจานคือจุดดำน้ำ 3D ที่สร้างจาก builder เดิม (ปะการัง ฝูงปลา ซากเรือ วาฬ) น้ำทะเลโปร่งแสง แสง caustics — **สั่งงานทุกอย่างด้วยตา+จีบนิ้ว ไม่มี controller ไม่มี toolbar รก**

### Goals (v1)
1. โหลด **แมพจริงจาก DB เดิม** (UserDiveSite) มาแสดงเป็น holomap บนโต๊ะ
2. Interaction ครบด้วย gaze + pinch (เลือก/หมุน/ซูม/pan/เมนู)
3. สัตว์ว่ายจริง (ฝูงปลา + วาฬ breach) ระดับ immersive สูงกว่าเว็บ
4. ความละเอียดโมเดลสูงกว่าเว็บ (LOD tier ใหม่จาก raw masters)
5. ดูอย่างเดียว + แก้ไขเบื้องต้น (ย้าย/หมุน/ลบ/วางวัตถุ) sync กลับ DB

### Goals ระยะยาว (v1.2 — Unity เต็มระบบ)
- ชั้น 1: ดู+สำรวจ+AR (v1 ข้างบน) → ชั้น 2: builder หลักครบ (วาง/แก้/undo/multi-select) → ชั้น 3: **parity 100% กับเว็บ** (sculpt พื้น, เชือก, env tools ทั้งหมด) → ปิด builder เว็บ

### Non-Goals (เฉพาะชั้น 1 — เลื่อนไปชั้น 2-3 ไม่ใช่ตัดทิ้ง)
- ⏳ สร้างแมพจากศูนย์ / sculpt พื้น / เชือก บน Unity → **WO-XR-08/09** (ชั้น 2-3)
- ❌ multiplayer / shared session (หลัง parity)
- ❌ iOS/visionOS (หลัง Android — สถาปัตยกรรมรองรับไว้แล้วเพราะ OpenXR + scene JSON กลาง)

---

## 1. Architecture

### 1.1 หลักการใหญ่: **ข้อมูลเดิม 100% — เพิ่มแค่ renderer ใหม่**

```
                    Supabase (zxrajmngzmjityifoszd) — ห้ามแตะ schema เว็บ
                    ├─ UserDiveSite.items/pins/env  ← SINGLE SOURCE OF TRUTH (มีอยู่แล้ว)
                    ├─ AssetModule (kind, glbUrl, meta)
                    └─ (ใหม่ additive) AssetModule.meta.xr = {glbXrUrl, lodUrls}
                              │
              maps.siamdive.com API (Next 16, Vercel — มีอยู่แล้ว)
              ├─ GET /api/sites/:shortId      ← XR ใช้ตัวเดียวกับเว็บ
              ├─ PATCH /api/sites/:shortId    ← save จาก XR (rev conflict เดิม)
              └─ GET /api/assets              ← asset registry
                              │
        ┌─────────────────────┴─────────────────────┐
   Three.js builder.html                    Unity 6 Android XR (ใหม่)
   (เว็บ/Expo เดิม — ไม่แตะ)                /root/projects/siamdive-xr
                                            ├─ SceneLoader (อ่าน items/pins/env)
                                            ├─ glTFast (โหลด GLB runtime + cache)
                                            ├─ Marine System (ฝูงปลา/พฤติกรรม)
                                            └─ XR Interaction (gaze+pinch)
```

- **ไม่มี MCP, ไม่มี service ใหม่, ไม่มีตารางใหม่** — XR เป็น "อีกหนึ่ง client" ของ API เดิม
- Unity โหลด GLB **runtime จาก URL** (glTFast) + cache ลงเครื่อง → อัปเดต asset ไม่ต้อง build แอปใหม่
- แก้บน builder เว็บ → เปิด XR เห็นเวอร์ชันล่าสุด (ผ่าน `rev` เดิม) และกลับกัน

### 1.2 Scene Format Spec v1 (ของจริงจาก `builder.html serialize()` — freeze เป็นสัญญา)

```jsonc
{
  "name": "Sail Rock",
  "items": [{
    "id": "m1a2b3",              // mid ถาวร
    "assetId": "coral:2",        // "type:variant" (BUILDABLE) หรือ AssetModule.id (GLB)
    "p": [x, y, z],              // เมตร, Y-up, right-handed (Three.js)
    "r": [rx, ry, rz],           // Euler XYZ radians
    "s": [sx, sy, sz],
    "c": "#ff8800",              // optional สี
    "wt": "...", "wn": "...",    // optional warp target/name
    "lb": "..."                  // optional label
  }],
  "pins": [{ "id", "p": [x,y,z], "media": [{"type","url"}] }],
  "env": {
    "waterLevel": 4.2, "areaScale": 1, "areaScaleX": 1, "areaScaleZ": 1,
    "areaThickness": 0.4, "areaSlopeX": 0, "areaSlopeZ": 0,
    "sculpt": [/*heights*/], "sculptDim": [rings, seg],   // พื้นทะเลปั้น
    "ropes": [{ "a": {"mid","lp"}, "b": {...}, "sag", "color", "thick" }]
  }
}
```

**กติกา XR client:**
1. field ที่ไม่รู้จัก → **เก็บไว้ ห้าม drop** (save กลับต้องครบ — บทเรียนเดียวกับ PATCH upsert-overwrite)
2. **แปลงมือ:** Three.js right-handed → Unity left-handed: `z → -z`, Euler แปลงผ่าน quaternion เท่านั้น (ห้ามสลับแกน Euler ตรงๆ) — utility เดียว `CoordJS.ToUnity()/ToWeb()` + unit test round-trip
3. `sculpt` → สร้าง mesh พื้นด้วย polar grid (rings×seg) เหมือน seabed เว็บ — ระวัง winding order (gotcha เดิมจาก headless test)
4. save จาก XR ใช้ PATCH + `rev` เดิม → conflict = โหลดใหม่ถามผู้ใช้

### 1.3 Asset / LOD Pipeline

ของที่มีแล้ว: **raw masters** `/root/asset-masters/` (marine ~100k tris + 4K PBR, wreck, artificial3D, Warp) · **derivatives เว็บ** `public/models/` (38 ราย + marine 34, draco+webp ย่อแรง)

```
raw master (100k tris, 4K)
 └─ gltf-transform pipeline ใหม่ (optimize_xr.mjs — fork จาก optimize.mjs เดิม)
     ├─ XR-LOD0: ~30-40k tris, KTX2 2K   → วัตถุใกล้/ตัวเด่น (วาฬ, รูปปั้น)
    ├─ XR-LOD1: ~8-12k tris, KTX2 1K    → ระยะกลาง / ฝูงปลาตัวนำ
     └─ (LOD2 = ใช้ไฟล์เว็บเดิมได้เลย)   → ฉากหลัง/ฝูง instance
```

- เก็บที่ `public/models/xr/<name>_lod0.glb` ฯลฯ บน Vercel เดิม (CDN ฟรี) — อ้างจาก `AssetModule.meta.xr` (Json column เดิม, **ไม่ migrate schema**)
- ท่า animation: masters ไม่มี rig → ใช้ derivative ที่ rig แล้วเป็นฐาน หรือ re-rig จาก master ตาม pipeline `reference_marine_asset_pipeline` (**ห้ามข้าม backup+rig**)
- ⚠️ กฎเดิมจากเว็บยังศักดิ์สิทธิ์บน Unity: **pitch จาก velocity, ห้าม rotation.z สะสม** (วาฬ breach)

### 1.4 Marine System บน Unity (แทน trick Three.js เดิม)

| ระบบเว็บเดิม | Unity ใช้ |
|---|---|
| ฝูงปลา InstancedMesh + CPU throttle | **C# Job System + Burst** (boids) → `Graphics.RenderMeshInstanced`; ฝูงใหญ่มาก → compute shader |
| solid-avoidance v.0680 | port สูตรเดิมตรงๆ (SDF sphere per obstacle) — logic ภาษาเดียวกัน แปลเป็น C# |
| adaptive-res hysteresis | ไม่ต้อง — ใช้ **Dynamic Resolution + foveated rendering** ของ Android XR |
| fx:'gold' aura sprites | Shader Graph + particle |
| fx:'beard' vertex sway | Shader Graph vertex offset (แทน onBeforeCompile) |
| น้ำ/caustics | URP transparent volume + caustics texture scroll บนพื้น + fog ใต้น้ำใน volume จาน |

**Perf budget (Galaxy XR):** 72fps × 2 ตา · ≤ 250k tris บนจอ/ตา · ≤ 120 draw calls (หลัง instancing) · foveated rendering เปิดตลอด

---

## 2. Gesture Spec — "จีบนิ้ว" เป็นภาษาหลัก

Stack: **XR Hands** (pinch strength/pose per มือ) + **XR Interaction Toolkit 3** (gaze ray, poke) + Android XR eye tracking

### 2.1 Vocabulary (v1 — 7 ท่า ห้ามเกินนี้ กันจำไม่ไหว)

| # | ท่า | เงื่อนไข | ผล |
|---|---|---|---|
| G1 | **Gaze + pinch แตะ** | pinch < 250ms บนวัตถุที่มอง | เลือกวัตถุ / กดปุ่ม |
| G2 | **Pinch ค้าง + ลาก (มือเดียว, มองจาน)** | ค้าง > 250ms, มองพื้นจาน | หมุน holomap รอบแกนตั้ง (1:1.5 องศาต่อองศามือ, inertia ปล่อยแล้วไหลต่อ) |
| G3 | **Pinch สองมือ ดึงเข้า/ออก** | ทั้งสองมือ pinch | ซูม 0.25×–4× (log scale, สปริงขอบ) |
| G4 | **Pinch สองมือ เลื่อนขนาน** | ทั้งสองมือ pinch, ระยะห่างคงที่ ±10% | pan แผนที่ในจาน |
| G5 | **Gaze วัตถุ + pinch ค้าง 0.6s** | บนวัตถุ | **Radial menu** รอบวัตถุ |
| G6 | **Pinch ค้างบนวัตถุที่เลือก + ลาก** | หลัง G1 เลือกแล้ว (โหมดแก้ไข) | ย้ายวัตถุ (ghost + เส้นดิ่งลงพื้น + snap Y พื้นทะเล) |
| G7 | **หงายฝ่ามือ + มองฝ่ามือ 0.4s** | มือว่าง (ไม่ pinch) | **Palm menu** (เมนูหลัก) |

### 2.2 กติกากันงง (สำคัญกว่าตัวท่า)

- **State machine เดียว exclusive** — `Idle → GazeHover → Selected → Dragging / RadialMenu / TwoHandZoom / TwoHandPan` ห้ามสอง gesture ทำงานพร้อมกัน; สองมือ pinch เมื่อไหร่ → ยกเลิกโหมดมือเดียวทันที
- **Threshold:** pinch เริ่ม = strength ≥ 0.75, ปล่อย = ≤ 0.45 (hysteresis กันกระพริบ) · dead-zone การลาก 8mm ก่อนถือว่า "ลาก"
- **Feedback ทุกจังหวะ:** gaze hover = ขอบวัตถุเรืองนิดๆ + label โผล่ · pinch สำเร็จ = pulse แสง + เสียง tick เบา (spatial audio) · ค้างเพื่อเมนู = วงแหวน progress รอบนิ้ว
- **Undo ลอย:** หลังทุกการแก้ไข โผล่ chip "↩ เลิกทำ" ลอยข้างจาน 5 วิ (จีบเพื่อกด) — กันมือลั่นซึ่งเกิดบ่อยใน XR
- โหมด **ดู** (default) G6 ปิด — ต้องเข้า "โหมดแก้ไข" จาก palm menu ก่อน (กันลากพังแมพโดยไม่ตั้งใจ; สิทธิ์ตาม editPolicy เดิม)

---

## 3. UI Mockup (spatial spec)

### 3.1 Layout รวม

```
                    ~1.0-1.2 m จากตา, สูงระดับโต๊ะ/เอว
        ╭────────────────────────────────────────╮
        │            🐋   (สัตว์ว่ายเหนือแนวปะการัง)   │
        │   〜〜 ผิวน้ำโปร่งแสง caustics 〜〜        │  ← "ตู้ปลาโฮโลแกรม"
        │  🪸   ⛰   🪸🪸    ⚓︎ (ซากเรือ)   🪸       │
   W ───┤▓▓▓▓▓ พื้นทะเล sculpt จาก env ▓▓▓▓▓├─── E
        ╰──○────────────────────────────────○──╯
          ขอบจาน: วงแหวนบางเรืองแสง teal จางๆ
          + ขีดเข็มทิศ N/E/S/W + ชื่อแมพ (มองขอบถึงโผล่)
```

- **จาน:** Ø เริ่ม 0.8 m (ซูมได้ 0.25–4×) · วางบนโต๊ะจริงด้วย plane detection + **spatial anchor** (จำตำแหน่งข้ามการเปิดแอป)
- **สะอาดสุดขีด:** สิ่งถาวรมีแค่ จาน + ขีดเข็มทิศ **เท่านั้น** — ไม่มีปุ่มลอย ไม่มี toolbar; ทุกเมนูมาจาก G5/G7 แล้วหายไปเอง
- Label จุด/pin โผล่เฉพาะตอน gaze (fade 150ms) หันหน้าเข้าหาตาเสมอ (billboard)

### 3.2 Palm Menu (G7 — เมนูหลัก)

```
     บนฝ่ามือ ห่าง 8cm ตามมือแบบหน่วงนุ่มๆ
        ┌──────────────┐
        │  🗺 แมพของฉัน   │   ← จีบเลือก → รายการการ์ดแมพ (โค้งรอบตัว 3 ใบ/แถว)
        │  🔍 ค้นหา      │
        │  ✏️ โหมดแก้ไข   │   ← toggle (แสดงสถานะชัด)
        │  🎬 ทัวร์โดรน    │   ← Tour เดิมจากเว็บ
        │  ⚙️ ตั้งค่า      │
        └──────────────┘
   ปุ่มสูง 3.2cm (≈2.3° ที่ระยะ 0.8m) ช่องไฟ 1cm — เกณฑ์ ≥2° ของ spatial UI
```

### 3.3 Radial Menu (G5 — รอบวัตถุ)

```
            [ℹ️ ข้อมูล]
       [🗑 ลบ]    ⟳  [↻ หมุน]
            [📏 ขนาด]
   วงแหวน 4 ปุ่มรอบวัตถุ · เลือกด้วย gaze+จีบ หรือลากนิ้วไปทิศนั้นแล้วปล่อย
   ℹ️ ข้อมูล = การ์ดสัตว์/จุดดำน้ำ (ชื่อ TH/EN, ความลึก, ฤดู, media จาก pins)
```

### 3.4 ภาษาภาพ (Material Language)

- **โทน:** dark-glass + teal glow `#4FD1C5` (รับกับแบรนด์ SiamDive) · panel = โปร่ง 20% ขอบ 1px เรืองแสง blur พื้นหลัง
- ตัวหนังสือไทย/EN: **Noto Sans Thai** SDF, ขาว 95%, ขนาดต่ำสุด 1.5° ของสายตา
- แสงในจาน: directional จำลองแดดทะลุน้ำ + volumetric shaft จางๆ 2-3 ลำ (อารมณ์ แต่ประหยัด GPU)
- เสียง: ambience ใต้น้ำเบามากรอบจาน (spatial) — ปิดได้ในตั้งค่า

---

## 4. Tech Stack (ล็อกเวอร์ชัน)

| ชั้น | ของที่ใช้ |
|---|---|
| Engine | **Unity 6 LTS (6000.x)** + URP (Vulkan) |
| XR | OpenXR + **Android XR provider** (`com.unity.xr.androidxr-openxr`) + AR Foundation 6 (plane/anchor/passthrough) |
| มือ/ตา | XR Hands 1.5+ · XR Interaction Toolkit 3.x (gaze interactor) |
| โหลดโมเดล | **glTFast** (+ KTX2/Basis, Draco) runtime + disk cache |
| ฝูงปลา | C# Job System + Burst → RenderMeshInstanced |
| Net/JSON | UnityWebRequest + Newtonsoft JSON (field ไม่รู้จักเก็บเป็น JToken — กติกา 1.2 ข้อ 1) |
| Build | **GitHub Actions + GameCI** (cloud build ฟรี 2,000 นาที/เดือน) — user ไม่มีคอม, VPS 2core/3G รัน Editor ไม่ได้ · Unity Build Automation = ทางสำรองถ้า GameCI ติดปัญหา |

**Workflow ผม↔คุณ (ไม่มีคอม ไม่มีปัญหา):** ผมเขียนโปรเจกต์ทั้งก้อน (C#, scene/prefab YAML, shader graph) → push GitHub (`suksomsri7`, ระวังกฎ author ≠ root@) → **GitHub Actions build APK อัตโนมัติ** → ส่งลิงก์ APK ผ่าน Telegram → คุณติดตั้งบนมือถือ Samsung (สาย A) หรือ Galaxy XR (สาย B) → ส่ง screenshot/ความรู้สึกกลับ → ผมแก้ วนแบบเดียวกับ siamdive-rn
**Setup ครั้งเดียว:** activate Unity Personal license สำหรับ CI — ผม gen ไฟล์ .alf บน CI → คุณอัปโหลดที่ license.unity3d.com จากมือถือ (ผมพาทำทีละขั้นภาษาไทย) → ได้ .ulf ส่งกลับมา → ผมเก็บเป็น GitHub secret

---

## 5. Roadmap — Work Orders

> DoD ทุก WO: โค้ด push แล้ว + CI build เขียว + คุณเทสบนอุปกรณ์จริงผ่าน + ไม่ regress ของเดิม (เว็บไม่แตะเลยโดยนิยาม)
>
> **จัดเป็น 3 สาย (ยังไม่มี Galaxy XR · Unity = ระบบเต็มในอนาคต):**
> - **สาย A — แอปมือถือ DiveMap (เริ่มทันที เทสบน Samsung):** WO-XR-00 (CI) → 01 (loader) → 03 (ฝูงปลา) → **02m (AR บนโต๊ะผ่านกล้อง ARCore — AR Foundation โค้ดเดียวกับ headset)** → 04 (LOD/immersive)
> - **สาย B — Galaxy XR (รอเครื่อง):** WO-XR-02 → 05 → 06 → 07 (โค้ด AR reuse จาก 02m)
> - **สาย C — Builder เต็มระบบ (สู่ parity แล้วปิดเว็บ):** WO-XR-08 → 09 → 10 (เริ่มหลังสาย A เสถียร ทำคู่สาย B ได้)

### WO-XR-02m · Mobile AR Holomap (ARCore)
plane detection วางจานบนโต๊ะจริงผ่านกล้องมือถือ + แตะ/ลาก/pinch จอ ควบคุมแบบเดียวกับ gesture spec (แทนจีบนิ้ว) · spatial anchor
**DoD:** วางแมพบนโต๊ะจริงผ่านมือถือ เดินรอบดูได้ ลื่น 60fps — ดีกว่า holomap gyro เดิมชัดเจน | ~1 สัปดาห์

### WO-XR-00 · CI Build Pipeline (GameCI) — *ทำก่อนทุกอย่าง*
สร้าง repo GitHub + workflow GameCI build Android APK · activate Unity Personal license (ขั้น manual ครั้งเดียวของ user ผ่านมือถือ) · Library cache ให้ build เร็ว · แจ้งลิงก์ APK อัตโนมัติเข้า Telegram
**DoD:** push โค้ด → ได้ลิงก์ APK ติดตั้งบน Samsung ได้จริง | ~2-3 วัน

### WO-XR-01 · โครงโปรเจกต์ + Scene Loader บนจอธรรมดา (ไม่ XR)
สร้าง Unity project + glTFast + `SceneLoader` อ่าน `GET /api/sites/:shortId` จริง → แสดง Sail Rock (demo public) บนจอ PC/มือถือ orbit ดูได้ · Coord converter + unit test round-trip · seabed sculpt + ropes + น้ำเบื้องต้น
**DoD:** Sail Rock บน Unity หน้าตา "จำได้ว่าแมพเดียวกัน" กับเว็บ | ~1-1.5 สัปดาห์

### WO-XR-02 · Android XR: จานบนโต๊ะ + gaze/pinch พื้นฐาน
OpenXR + passthrough + plane detection → วางจาน + spatial anchor · G1 เลือก, G2 หมุน, G3 ซูม, G4 pan · ขอบจาน/เข็มทิศ/clipping วัตถุนอกจาน
**DoD:** ใส่ Galaxy XR เห็น Sail Rock บนโต๊ะจริง หมุน/ซูมด้วยจีบนิ้วลื่น 72fps | ~2 สัปดาห์ *(← milestone ว้าวแรก)*

### WO-XR-03 · Marine System: ฝูงปลา + สัตว์ว่าย
Boids Jobs+Burst port สูตร v.0678-0680 (real-delta, solid-avoidance) · animation clip จาก derivatives · กฎ pitch/no-roll วาฬ · LOD ระยะ
**DoD:** ฝูง ≥300 ตัว + วาฬ 72fps คงที่ ว่ายหลบหินนุ่มเท่าเว็บหรือดีกว่า | ~2 สัปดาห์

### WO-XR-04 · XR-LOD pipeline + immersive pass
`optimize_xr.mjs` gen LOD0/1 จาก raw masters (นำร่อง 10 ตัวเด่น: วาฬ, มันตา, ฉลามวาฬ, golden_trident, สิงห์...) + `AssetModule.meta.xr` · caustics/god rays/fog · FX gold+beard ใน Shader Graph · spatial audio
**DoD:** เทียบข้างกันชัดว่า "คมกว่าเว็บ" ใน headset, fps ไม่ตก | ~1.5 สัปดาห์

### WO-XR-05 · UI ชั้นเต็ม: Palm menu + Radial + การ์ดข้อมูล
G5/G6/G7 · palm menu + รายการแมพ + ค้นหา · การ์ดข้อมูลสัตว์/pin (media) · Tour โดรนในจาน · i18n TH/EN (dict แนวเดิม)
**DoD:** เปิดแอป→เลือกแมพ→สำรวจ→ดูข้อมูล ครบโดยไม่แตะ controller | ~2 สัปดาห์

### WO-XR-06 · โหมดแก้ไข + sync สองทาง
G6 ย้าย/หมุน/ลบ/วาง (palette จาก AssetModule) · PATCH + rev conflict · undo chip · เคารพ editPolicy
**DoD:** ย้ายปะการังใน XR → refresh builder เว็บเห็นตรงกัน และกลับกัน | ~1.5-2 สัปดาห์

### WO-XR-07 · Polish + ปล่อยจริง
onboarding สอนท่า 30 วิ (ครั้งแรก) · error/offline cache · perf QC gate · icon/store listing → **Google Play (Android XR track)**
**DoD:** ผ่าน QC ทั้ง flow + ขึ้น store | ~1-2 สัปดาห์

### สาย C — Builder เต็มระบบ (Unity แทนเว็บ)

### WO-XR-08 · Builder Core บน Unity
palette วางวัตถุครบทุกหมวด (AssetModule) · ย้าย/หมุน/สเกล gizmo แบบ touch/pinch · multi-select + copy + ลบ · undo/redo stack · ป้ายชื่อ/สี/warp · สร้างแมพใหม่จากศูนย์ + template
**DoD:** สร้างแมพใหม่ทั้งอันใน Unity ได้โดยไม่ต้องเปิดเว็บ, เปิดในเว็บแล้วตรงเป๊ะ | ~3 สัปดาห์

### WO-XR-09 · Sculpt + เชือก + Env Tools (สู่ parity)
sculpt พื้นทะเล (แปรงยก/กด, polar grid rings×seg format เดิม) · ระบบเชือก 🪢 (ผูกจุด, sag, สี) · ตั้งระดับน้ำ/ความลาด/ขนาดพื้นที่ · pins+media
**DoD:** ทุกฟีเจอร์ builder เว็บทำได้ใน Unity — checklist parity ครบ 100% | ~3-4 สัปดาห์

### WO-XR-10 · Parity QC + Sunset เว็บ
QC ข้ามระบบ: แมพจริงทุก template + demo เปิดสองฝั่งเทียบภาพตรงกัน · migrate ผู้ใช้ (แจ้งในเว็บ→ชวนโหลดแอป) · เว็บเหลือ read-only viewer (ลิงก์แชร์/SEO) หรือปิดตามที่ user ตัดสิน
**DoD:** user ประกาศปิด builder เว็บได้โดยไม่มีใครเสียงาน | ~1-2 สัปดาห์

**รวม:** สาย A ~6-7 สัปดาห์ (แอปมือถือใช้จริง) · +สาย B ~5-6 สัปดาห์เมื่อได้เครื่อง · +สาย C ~7-9 สัปดาห์ถึง parity ปิดเว็บ — milestone จับต้องได้ทุก 1-2 สัปดาห์ตลอดทาง

---

## 6. Risks & คำถามเปิด

| ความเสี่ยง | แผนรับ |
|---|---|
| ยังไม่มีเครื่อง Galaxy XR จริง? | **Android XR Emulator** (Android Studio) เทส WO-01/02 ได้ระดับหนึ่ง แต่ hand tracking จริงต้องเครื่อง — *ถ้ายังไม่มีเครื่อง ควรรู้ก่อนเริ่ม WO-02* |
| Unity บนเครื่องคุณ (spec/license) | Unity Personal ฟรี (รายได้ <$200k) · ต้องการเครื่อง RAM ≥16GB |
| glTFast กับไฟล์ draco+webp เดิม | webp texture ใน GLB ไม่ standard ทุกตัว → XR-LOD ใช้ KTX2 เท่านั้น (WO-04); เทสตั้งแต่ WO-01 |
| ฝูงปลาใหญ่กิน CPU | เพดาน instance ต่อฉาก + LOD พฤติกรรม (ฝูงไกล = ไม่คิด avoidance) |
| Android XR แพลตฟอร์มใหม่ API ขยับ | ตรึงเวอร์ชัน package ทุกตัว, อัปเมื่อจำเป็นเท่านั้น |

**คำตอบจาก user (2026-07-17):**
1. Galaxy XR **ยังไม่มี — กำลังพยายามหามาทดลอง** → จัด roadmap เป็นสาย A (มือถือ)/สาย B (รอเครื่อง) แล้ว
2. คอม**ไม่มี** → ใช้ **GitHub Actions + GameCI** build บนคลาวด์ (WO-XR-00)
3. ชื่อแอป = **DiveMap**

---

## 6.5 การเสียบแทนแอปเดิม (iOS/Android Migration)

**หลัก: store listing = กล่อง — bundle/package ID เดิม + ลายเซ็นเดิม = ผู้ใช้ได้ Unity เป็น "อัปเดตธรรมดา"**

- **Android:** ไม่มีของเก่าให้แทน — แอปเดิมยังไม่เคยขึ้น Play (APK เทส + Console รอ verify) → DiveMap Unity ขึ้น Play เป็นตัวจริงตัวแรกเลย จบสาย A
- **iOS:** แอป SiamDive Maps LIVE (id6787005046) → Unity build ด้วย **bundle ID เดิม** เซ็นบัญชี Apple เดิม ส่งเป็นเวอร์ชันใหม่ (2.0) = ผู้ใช้เดิมกดอัปเดตได้ Unity ทันที ยอดโหลด/รีวิว/APNs คงเดิม · iOS build ใช้ macOS runner (แพง 10× — รันเฉพาะรอบ release) · ทำหลัง Android ตามแผน
- **ย้ายแมพผู้ใช้ (สองทางประกบ):**
  1. หลัก — อัปเดตทับไม่ลบ data dir → Unity อ่าน deviceId เก่าจาก storage ของแอป Expo เดิม (iOS: app container เดิม / Android: AsyncStorage files) → ใช้ต่อเนื่องเงียบๆ ผู้ใช้ไม่รู้สึกอะไร
  2. ตาข่าย — ชวนผูกอีเมล OTP (ระบบ multi-device เดิม) ก่อน/หลังสลับ → กู้ข้ามเครื่องได้ตลอด
- แอป Expo (siamdive-rn) หยุดพัฒนาเมื่อ DiveMap ขึ้น store — ไม่ลบ repo (อ้างอิง native bridge เดิม)

## 7. เอกสารคู่กัน (อ่านครบชุดก่อนเริ่มโค้ด)

| ไฟล์ | เนื้อหา |
|---|---|
| `DESIGN_DOC.md` (ไฟล์นี้) | สัญญาหลัก: architecture + gesture + UI + roadmap 3 สาย |
| `QC_PLAN.md` | ระบบ QC 5 ชั้น (CI tests / visual parity / perf / device checklist / data integrity) — gate ทุก WO |
| `SECURITY_PLAN.md` | ความปลอดภัย: no-secret-in-APK, สิทธิ์ตัดสินที่ server, keystore, UGC, incident response |

## 8. สถานะโปรเจกต์
- **2026-07-17 Night Run #1 → ⏸️ PAUSED โดย user:**
  - WO-XR-00: 95% — repo+CI+license ✅, เทส 26/26 เขียวบน CI, **ค้างผล build APK แรก (run 29593800372)**
  - WO-XR-01: 85% — โค้ด SceneLoader ครบ (แมพ demo Htms Chang), รอ APK + เทสบนเครื่อง
  - XR-LOD: ✅ 10/10 (`/root/asset-masters/xr_lod/`) ยังไม่อัป CDN
  - รายละเอียด/บทเรียน → memory `project_siamdive_xr_holomap`
- แก้ spec ต้องแก้เอกสารก่อนโค้ดเสมอ
