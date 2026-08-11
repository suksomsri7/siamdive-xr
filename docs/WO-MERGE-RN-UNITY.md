# WO-MERGE — รวมแอป: Unity DiveMap เข้าเป็น "จอ 3D" ของแอป SiamDive (RN)

> เขียน 2026-08-10 (Fable วางแผน / Opus ทำงาน) · สถานะ: **แผน — ยังไม่เริ่ม**
> การตัดสินใจของ user (เคาะแล้ว ห้ามรื้อ): **①** เก็บ `com.siamdive.app` เป็นแอปตัวจริง
> **②** iOS ให้เรียบร้อยก่อน แล้วค่อย Android **③** รวมจริง (ไม่ทำ deep-link ชั่วคราว)
> เหตุผลของงาน: Unity ถูกสร้างมาเพื่อ**แทนเกม 3D เดิมที่เป็นเว็บ** (ไม่สวย) ·
> หน้าแรก**คงเป็น WebView** เพราะต้องอิง siamdive.com

---

## 0. ภาพรวม — เปลี่ยนจุดเดียว

```
ตอนนี้:  index.tsx (WebView siamdive.com) → ปุ่ม 3D (RN) → map.tsx (RN hub) → builder.tsx (WebView maps.siamdive.com)
เป้า:    index.tsx (เหมือนเดิม)          → ปุ่ม 3D (เดิม) → map.tsx (เดิม)   → 🎯 UnityView (UnityFramework ฝังในแอป)
```

- ฝั่ง RN แตะไฟล์หลักไฟล์เดียว: เส้นทางที่ `map.tsx` เปิด `builder.tsx` → ชี้ไปจอ Unity แทน (มี feature flag ถอยกลับได้)
- ฝั่ง Unity: เพิ่ม "library mode" — บูตตรงเข้าแมพที่สั่ง ไม่เปิดหน้า hub ของตัวเอง
- **PARITY ยืนยันแล้ว Unity แทนเว็บได้ทั้งก้อน** (Builder I 14/14 · บัญชี J 8/8 · เหลือจริงแค่ F3 holomap ที่ user สั่งพักเอง)

## 1. สัญญาข้อมูลข้ามจอ (ถอดจาก builder.tsx ของจริง — บรรทัดอ้างในโค้ด)

| ข้อมูล | ตอนนี้ (RN→WebView) | หลังรวม (RN→Unity) |
|---|---|---|
| `shortId` | query param (builder.tsx:99) | เขียน `PlayerPrefs "shortId"` ก่อนบูต — **AppBoot อ่านคีย์นี้อยู่แล้ว** (AppBoot.cs:52) |
| `deviceId` | query param (:98) — ผูก wallet `/api/wallet` | ส่งเข้า Unity ให้ใช้ตัวเดียวกัน → เหรียญ/ของที่ซื้อตรงกันอัตโนมัติ |
| `lang` | query param `UI_LANG` | ส่งเข้า → `UiStrings.ToLang` |
| login `sd_auth` | เว็บ postMessage `__auth` → RN เก็บ AsyncStorage (:203) | **RN เป็นเจ้าของ auth** ฉีด token เข้า Unity · Unity ห้ามเปิด flow login ซ้อนเมื่ออยู่ใน library mode |
| ⭐ fav / ☁ offline | เว็บ postMessage `__fav` (:168-189) | จัดการใน `map.tsx` (RN hub) อยู่แล้ว — Unity ไม่ต้องรู้จัก |
| ออกจากจอ 3D | ปุ่ม back ของ RN | Unity ส่ง message `exit` → RN ซ่อน/pause UnityView แล้ว pop route |

## 2. เฟสงาน

### P0 — Spike พิสูจน์ความเสี่ยงอันดับหนึ่งก่อนลงทุน (2-3 รอบ CI)
เป้า: **UnityFramework จาก GameCI ขึ้นจอในแอป Expo dev-client บน iPhone จริง 1 เครื่อง**
1. CI job ใหม่ (workflow_dispatch แยก — **ห้ามแตะ job TestFlight เดิม** ใช้เป็นช่อง QC ฝูงปลาต่อ):
   ต่อท้าย BuildIos → `xcodebuild -scheme UnityFramework archive` → แพ็ก `UnityFramework.xcframework` (รวม Data folder) → อัปขึ้น Bunny storage (แพทเทิร์น `tools/publish_build.sh`) ตั้งชื่อด้วย commit sha
2. RN branch: ลง `@azesmway/react-native-unity` + expo config plugin (เขียนเอง) ที่ดึง xcframework ตาม sha ที่ pin ไว้ตอน prebuild
   - npm ประกาศรองรับ New Architecture แล้ว (RN 0.81 + SDK 54 = new arch เปิด) — **ต้องพิสูจน์เอง ห้ามเชื่อ README**
3. เกณฑ์ผ่าน: เปิดจอ Unity เห็น Htms Chang + badge `fps · bNNN · ปลา 10/10` + กดออกกลับ RN ได้ + เข้า-ออกซ้ำ 5 รอบไม่ crash
- ❌ ถ้า bridge ไม่รอด new arch: ทางสำรอง = เขียน native module ฝั่ง iOS เอง (~2-3 วันเพิ่ม) — ตัดสินใจตอนเจอ

### P1 — Unity "library mode" (1-2 รอบ CI)
- launch param จาก native: `shortId / deviceId / lang / authToken / libraryMode=1`
- `libraryMode=1` → ข้ามหน้า hub/รายการแมพของ Unity (จอที่พอร์ตไว้ 31 ก.ค. **เก็บไว้** เป็นโหมด standalone สำหรับ TestFlight DiveMap — ไม่ลบ) · ปุ่ม back ตัวลึกสุด → ส่ง `exit` แทนเปิด hub
- ปิด flow login ของ Unity ใน library mode (รับ session ฉีดอย่างเดียว)
- 🔴 กติกาเดิมคงอยู่ทั้งหมด: ห้ามแตะค่าจูนฝูงปลา (FISH_TUNING §5) · `msh:*` ห้ามโดน · เทสต้องเขียว EditMode บน CI

### P2 — RN integration (ไม่มีรอบ CI Unity — เทสผ่าน dev client)
- จอใหม่ `src/app/unity.tsx` + flag `USE_UNITY_3D` (default off จนกว่า QC ผ่าน) — `map.tsx` เลือกเส้นทางตาม flag
- **ก่อน mount UnityView ต้องปล่อยหน่วยความจำ WebView หน้าแรก** (iOS ยึด WKWebView คืนตอน GPU pressure อยู่แล้ว — builder.tsx:233 จดไว้เอง)
- Unity โหลดครั้งเดียวต่อโปรเซส: ออกจากจอ = **pause+ซ่อน ห้าม unload** (ปัญหา unload/reload ของ Unity เป็นที่รู้กัน)
- **ออฟไลน์: คงเส้นทาง WebView `file://` snapshot เดิมไว้** — Unity ต้องใช้เน็ตดึง map JSON · ออฟไลน์→WebView, ออนไลน์→Unity · (สอง engine ไม่ชนกันเพราะไม่เคยรันพร้อมกัน)

### P3 — ท่อ build จริง (2-3 รอบ)
- ลำดับ: GameCI (xcframework→Bunny) → bump sha ใน RN repo → **EAS build iOS** → TestFlight `com.siamdive.app`
- 🔴 **EAS build ต้องรอ user สั่งทุกครั้ง** (กินโควตา — feedback_no_eas_build_without_order) · EXPO_TOKEN มีแล้ว (reference_expo_token)
- ⚠️ ขนาด: แอปโต ~90 MB · xcframework artifact ระดับหลายร้อย MB — Bunny เก็บ, git ห้าม commit

### P4 — QC บนเครื่องจริง + ส่ง user
เช็คลิสต์เครื่องจริง (CI จับไม่ได้): แรมตอน WebView+Unity สลับกัน · เข้า-ออก 3D ×10 · เสียง (Unity ตั้ง audio session Playback — เช็คไม่ชนเสียงเว็บ) · safe area/หมุนจอ · deviceId เดิม=เหรียญเดิม · login จาก RN ใช้ใน Unity ได้ · **ถ่ายรูป/วิดีโอให้ user ดูก่อนสั่ง build ทุกครั้ง** (กติกา §🧭)

### P5 — เก็บงาน (หลัง user อนุมัติ)
- เปิด flag เป็น default · ลบเส้นทาง WebView ออนไลน์ (เก็บ offline snapshot ไว้) · อัป version 1.1.0
- TestFlight DiveMap (`com.siamdive.divemap`) **คงไว้เป็นช่อง QC ภายใน** — ไม่ปิด App record
- **Android = WO แยก** ทำหลัง iOS เรียบร้อย (unityLibrary gradle module — ง่ายกว่า iOS)

## 3. ความเสี่ยง

| ความเสี่ยง | โอกาส | แผนรับ |
|---|---|---|
| bridge ไม่รอด new arch จริง | กลาง | P0 พิสูจน์ก่อนลงทุน · สำรอง = native module เอง |
| แรมไม่พอ (WebView+Unity) | กลาง | ปล่อย WebView ก่อนเข้า Unity · วัดบนเครื่องจริงใน P0 |
| Unity unload พัง | สูงถ้าฝืน | ไม่ unload เลย — pause+ซ่อนเท่านั้น |
| EAS worker ดึง artifact ใหญ่ช้า/fail | ต่ำ | Bunny CDN + retry ใน config plugin |
| เสียง/orientation ชนกัน | ต่ำ | อยู่ในเช็คลิสต์ P4 |

## 4. สิ่งที่ห้ามทำ
- ห้ามแตะ `index.tsx` / `map.tsx` เกินจุดเชื่อม (ของที่ต้องไม่หาย: offline redirect 3 ชั้น · CONTACT_BRIDGE push · ซ่อนปุ่ม 3D หน้า detail/plan)
- ห้ามลบหน้า hub ใน Unity (standalone QC ยังใช้)
- ห้ามอัป Expo SDK ปนกับงานนี้ (AGENTS.md ชี้ v56 แต่ติดตั้ง 54 — ถ้าจะอัปเป็นงานแยก ถาม user ก่อน)
- ห้าม EAS build / ห้ามยิง TestFlight โดย user ไม่สั่ง
