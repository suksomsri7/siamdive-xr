# UI PARITY — แอปต้องหน้าตา/การใช้งานเหมือน maps.siamdive.com

> user สั่ง 2026-07-30: **"เพื่อให้การใช้งานของ user ไร้รอยต่อ UI และ UX ต้องเหมือนที่หน้าเว็บ"**
> ที่มา: `<style>` + markup ของ `public/builder.html` (บรรทัด 39-190, 290-317) — อ้างอิงเลขบรรทัดไว้ทุกข้อ

## 1. Design tokens (builder.html:39 `:root`)
| token | ค่า | ใช้ที่ Unity |
|---|---|---|
| `--bg` | `#071a2b` | `UiKit.ScreenBg` |
| `--panel` | `rgba(11,26,42,.72)` + `backdrop-filter: blur(18px)` | `UiKit.Glass` = **0.88** — uGUI เบลอฉากหลังไม่ได้ ถ้าใช้ 0.72 เฉยๆ อ่านไม่ออกบนแนวปะการัง (เป็นข้อเบี่ยงเบนที่ตั้งใจ + คอมเมนต์ไว้ในโค้ด) |
| `--accent` | `#39b0e8` | `UiKit.Accent` (`Teal`/`TealDim` เป็น alias) |
| `--txt` | `#eaf4fb` | `UiKit.TextMain` |
| `--mut` | `#9fb6c9` | `UiKit.TextDim` |
| `--line` | `rgba(255,255,255,.1)` | `UiKit.Line` (ขอบ hairline ของปุ่มกลม) |
| ปุ่ม primary | bg `--accent`, ตัวหนังสือ `#04121f` | `UiKit.OnAccent` |
| ปุ่ม danger | `rgba(176,52,74,.92)` | `UiKit.Danger` |
| ฟอนต์ | Noto Sans Thai 400/500/600/700 | `NotoSansThai-Regular.ttf` ใน Resources |

## 1.5 หน่วยวัด — สำคัญที่สุด
เว็บวางเลย์เอาต์ด้วย **CSS px** · แอปใช้ canvas 1080×1920 สเกลด้วย `√(w/1080 · h/1920)`
→ เลขหน่วย canvas ที่ hard-code ไว้ **ขนาดจริงต่างกันทุกเครื่อง** จึงต้องผ่าน **`UiKit.Css(px)`** เสมอ:
```
canvas units = cssPx × dpr / canvasScale        (dpr = Screen.dpi/160 บนมือถือ, 1 บนจอคอม/CI)
UiKit.CssFont(px)   สำหรับ Text.fontSize
```
ยืนยันจาก QC log: `dpr=1.00 canvasScale=0.667 48css=72u` (1280×720) → ปุ่ม 48 CSS px = 48 px จริง
🔴 **ห้ามเขียนเลขหน่วย canvas ตรงๆ ใน UI ใหม่อีก** ทุกตัวเลขต้องมาจาก CSS ของเว็บ

## 2. Chrome (ตำแหน่งต้องตรง — ผู้ใช้ต้องเจอปุ่มที่นิ้วเดิม)
| เว็บ | ตำแหน่ง/ขนาด | สถานะแอป |
|---|---|---|
| `#backBtn` | ซ้ายบน วงกลม 48px กระจก ไอคอน `m15 5-7 7 7 7` | ✅ pass 6 |
| `#playBtn` | ขวาบน วงกลม 48px (ให้สัตว์ขยับ) | 🚫 แอปเล่นตลอด ไม่มีปุ่ม |
| `#menuToggle` | **ขวาล่าง** วงกลม 48px **gradient ฟ้า** กด = กาง `#actions` + สลับไอคอน ☰↔✕ | ✅ pass 2 |
| `#actions` | คอลัมน์ปุ่มกลมเหนือ ☰ gap 10px | ✅ pass 2 (รายการแมพ/ทัวร์/ตั้งค่า) |
| `#compass` | ขวา ล่าง+80px วงกลม 48px เข็มเหนือ `#ff3b30` ใต้ `#e9f2fa` | ✅ pass 3 (อยู่ใน HUD ทัวร์) |
| `#toast` | **กลางจอ** radius 14 padding 13/22 14px/600 | ✅ pass 1 |
| `#load` | เต็มจอ `inset:0` bg `#071a2b` z-20 · กลางจอมี spinner `.sp` 46×46 (builder.html:223-225) | ✅ `Ui/LoadOverlay` — canvas sortingOrder 100 (เหนือ shell) · slot 46px ของ spinner = **โลโก้หน้ากาก ai-mask.png** ย้อม `--txt` · ใต้ลงมา label + **แถบ progress 240×6 r3** (track `rgba(255,255,255,.18)` + fill accent — เลขจากวงแหวน v.0668 builder.html:434-449) + % · gap 14 ทุกชั้น · 🔴 ไม่สร้างเลยใน `-qcshot` |
| `#hint` | บน 72px pill radius 22 | ❌ |
| `#sheet` | bottom sheet เต็มความกว้าง radius 24 24 0 0 max-height 72vh + grip 42×4 | ✅ pass 5-7 (รายการแมพ/ตั้งค่า/การ์ด) |
| modal | 86vw max 380 radius 20 padding 20 · หัว 16/600 · เนื้อ 13 muted · ปุ่มแถว gap 10 radius 13 | ✅ pass 6 (กล่อง error) |
| `#backBtn` | ซ้ายบน 48px chevron | ✅ pass 6 (โผล่เมื่อมีหน้าเปิด) |
| chip หมวด | min-width 64 radius 15 bg white 6% + ขอบ line · เลือก = accent 22% + ขอบ accent | ❌ |

## 3. ไอคอน
เว็บใช้ stroke SVG 24×24 (`stroke:#fff; stroke-width:2-2.2; round caps`)
Unity: `Runtime/Ui/IconPainter.cs` วาดจาก **path data ชุดเดียวกัน** (พิกัด 24 หน่วย) แล้ว rasterise ด้วย distance-to-segment AA → sprite 96×96 cache ต่อไอคอน
มีแล้ว: `menu close back wave sun lamp sound mute camera play exit list mask gear compass needle`
ข้อยกเว้น — **`mask` (ปุ่มทัวร์/โดรน + จอโหลด)**: เว็บไม่ได้วาดเป็น stroke path แต่ใช้ไฟล์ภาพ `ai-mask.png` ย้อมขาว
(`builder.html:307` `#tourBtn` และ `:338` `#viewTour` — `filter:brightness(0) invert(1)`) แอปจึง **ใช้ไฟล์เดียวกัน**
ที่ `Assets/Resources/ai-mask.png` (RGB ทำเป็นขาวล่วงหน้า = ผลลัพธ์ของ filter นั้น → ย้อมสีได้ตรง + ไม่มีขอบดำจาก
พิกเซลโปร่งใสไม่ว่า importer ตั้งค่ายังไง) ขนาดในปุ่ม = **27/48** ตามเว็บ ไม่ใช่ 22/48 ของไอคอนเส้น
ถ้าเท็กซ์เจอร์หายจาก build → `IconPainter` ตกกลับไปวาด path `case "mask"` เหมือนเดิมโดยอัตโนมัติ
เหตุผลที่ไม่ใช้ฟอนต์ไอคอน: NotoSansThai ไม่มี glyph ☰ (ของเดิมจึงเป็น Image 3 แถบวางมือ) และเครื่องนี้ไม่มี Unity Editor จะ import SVG/bake atlas ไม่ได้

## 3.5 HUD ทัวร์ (builder.html 231-277) — ตรงครบแล้ว pass 4-6
| ชิ้น | เว็บ | แอป |
|---|---|---|
| `#tourExit` | ซ้ายบน max(14,safe) 44×44 rgba(7,26,42,.62) | ✅ |
| `#tourDepth` | ขวาบน pill 19px/800 `#9fe0ff` ขอบ rgba(120,200,255,.4) radius 14 | ✅ |
| `#tourHud` | กลางบน max(15,safe) 12px/600 rgba(7,26,42,.42) | ✅ |
| `#lightBtn` | ซ้าย 14 / บน 104 · 56×56 ขอบ 2.5px ขาว · ติด = rgba(255,214,90,.32)+`#ffe08a`+ไอคอน `#ffe89a` | ✅ |
| `#radarBtn` slot | ซ้าย 14 / บน 174 | ✅ ใช้เป็นปุ่มปิดเสียง (เรด้าอยู่ใน P2) |
| `#tourCam` | ขวา 14 / บน 104 gap 14 · 56×56 | ✅ ถ่ายรูป (อัดวิดีโอตัดออกจาก v1) |
| `.stick` | ล่าง 24 · ซ้าย/ขวา 18 · 138×138 · knob 60 · ป้าย 9.5px 4 ทิศ | ✅ |
| `#minimap` | ล่าง 16 กลางจอ 118×118 ขอบ rgba(120,200,255,.45) | ✅ วาดพื้น/สิ่งกีดขวาง/ฝูง/สัตว์/ทิศ |
| `body.tour #compass` | **ย้าย**ไปขวาบน right 138 / top 15 · 44×44 ขอบ 2px | ✅ |
| `body.tour` ซ่อน | `#backBtn #viewbtns #sheet #count #top` | ✅ ผ่าน `UiShell.ApplyModeChrome` |

## 4. งานที่เหลือเพื่อ parity เต็ม
1. chip หมวด/variants เมื่อทำ palette (P8) · `#seltool` เมื่อทำโหมดแก้ไข
2. `#hint` pill ในหน้าดูแมพ (ตอนนี้มีเฉพาะใน HUD ทัวร์)
3. minimap: เว็บมีปุ่มเรด้าเปิด/ปิด (`#radarBtn`) — แอปยังโชว์ตลอด
4. เว็บซ่อนชื่อแมพ (`#name` hidden) — แอปโชว์บรรทัดเดียวจางๆ แบบ `#count` (11px muted กลางบน) ถือว่าตรงสล็อตเดียวกัน
