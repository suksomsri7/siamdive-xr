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

## 2. Chrome (ตำแหน่งต้องตรง — ผู้ใช้ต้องเจอปุ่มที่นิ้วเดิม)
| เว็บ | ตำแหน่ง/ขนาด | สถานะแอป |
|---|---|---|
| `#backBtn` | ซ้ายบน วงกลม 48px กระจก ไอคอน `m15 5-7 7 7 7` | ❌ ยังไม่ทำ (แอปใช้ปุ่ม back ของ Android) |
| `#playBtn` | ขวาบน วงกลม 48px (ให้สัตว์ขยับ) | 🚫 แอปเล่นตลอด ไม่มีปุ่ม |
| `#menuToggle` | **ขวาล่าง** วงกลม 48px **gradient ฟ้า** กด = กาง `#actions` + สลับไอคอน ☰↔✕ | ✅ pass 2 |
| `#actions` | คอลัมน์ปุ่มกลมเหนือ ☰ gap 10px | ✅ pass 2 (รายการแมพ/ทัวร์/ตั้งค่า) |
| `#compass` | ขวา ล่าง+80px วงกลม 48px เข็มเหนือ `#ff3b30` ใต้ `#e9f2fa` | ✅ pass 3 (อยู่ใน HUD ทัวร์) |
| `#toast` | **กลางจอ** radius 14 padding 13/22 14px/600 | ✅ pass 1 |
| `#hint` | บน 72px pill radius 22 | ❌ |
| `#sheet` | bottom sheet เต็มความกว้าง radius 24 24 0 0 max-height 72vh + grip 42×4 | ❌ **งานถัดไป** (แอปยังเป็นหน้าเต็มจอ) |
| modal | 86vw max 380 radius 20 padding 20 · หัว 16/600 · เนื้อ 13 muted · ปุ่มแถว gap 10 radius 13 | ❌ |
| chip หมวด | min-width 64 radius 15 bg white 6% + ขอบ line · เลือก = accent 22% + ขอบ accent | ❌ |

## 3. ไอคอน
เว็บใช้ stroke SVG 24×24 (`stroke:#fff; stroke-width:2-2.2; round caps`)
Unity: `Runtime/Ui/IconPainter.cs` วาดจาก **path data ชุดเดียวกัน** (พิกัด 24 หน่วย) แล้ว rasterise ด้วย distance-to-segment AA → sprite 96×96 cache ต่อไอคอน
มีแล้ว: `menu close back wave sun lamp sound mute camera play exit list mask gear compass needle`
เหตุผลที่ไม่ใช้ฟอนต์ไอคอน: NotoSansThai ไม่มี glyph ☰ (ของเดิมจึงเป็น Image 3 แถบวางมือ) และเครื่องนี้ไม่มี Unity Editor จะ import SVG/bake atlas ไม่ได้

## 4. งานที่เหลือเพื่อ parity เต็ม (เรียงตามที่ผู้ใช้เห็นก่อน)
1. **bottom sheet** — รายการแมพ/ตั้งค่า/การ์ดข้อมูล ต้องเป็นแผ่นเลื่อนขึ้นทับแมพ (เห็นแมพอยู่ข้างหลัง) ไม่ใช่หน้าเต็มจอ · ต้องมี `UiKit.RoundedSprite()` 9-slice ก่อน
2. **modal** ตามสเปกข้อ 2 (ยืนยัน/ตั้งชื่อ)
3. **#backBtn** ซ้ายบน + `#hint` pill
4. chip หมวด/variants เมื่อทำ palette (P8)
5. เว็บซ่อนชื่อแมพ (`#name` hidden) — แอปโชว์บรรทัดเดียวจางๆ ที่หัวจอเหมือน `#count` (11px muted) ถือว่าตรงสล็อตเดียวกัน
