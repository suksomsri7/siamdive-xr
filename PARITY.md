# PARITY — ฟีเจอร์เว็บ (maps.siamdive.com/builder.html) เทียบแอป Unity DiveMap

> เขียน 2026-07-30 เพราะรายการ "งานที่เหลือ" ที่เคยรายงานไว้ **นับตาม WO ใน DESIGN_DOC เท่านั้น**
> แล้ว WO ไม่ได้ครอบคลุมฟีเจอร์จริงของเว็บทั้งหมด (ทัวร์โดรน/เกมเก็บขยะ-เหรียญ/เข็มทิศ/ไฟฉาย/
> ถ่ายรูป/อัดวิดีโอ/มินิแมพ/เสียง/heatmap/warp/pins/เชือก/บัญชี-สิทธิ์/รายการโปรดออฟไลน์ **ไม่มี WO รองรับเลย**)
>
> ที่มาของรายการ: `id=`, `title=`, และชื่อ `function` ทั้งหมดใน `public/builder.html` (4,408 บรรทัด)
> วิธีอ่านสถานะ: ✅ ทำแล้ว · ⚠️ ทำบางส่วน · ❌ ยังไม่มี
>
> **สรุปตัวเลข: 22.5 / 86 ≈ 26% ของฟีเจอร์เว็บ** — อัปเดต 2026-07-30 (+toast, +ทัวร์ D1/D2/D4/D8)
> ถ้าวัดแค่ "ภาพนิ่งของฉาก" ≈ 85%
> เวลาอัปเดตไฟล์นี้: แก้ทั้งช่องสถานะและตัวเลขสรุป ห้ามปล่อยให้ค้าง

## A. ดูแมพ / กล้อง (4.5/7)
| # | ฟีเจอร์เว็บ | หลักฐาน | Unity |
|---|---|---|---|
| A1 | โหลดแมพจาก API + asset manifest | `load()`, `gload()` | ✅ |
| A2 | orbit หมุน/ซูม/แพน (เมาส์ + สัมผัส) | OrbitControls | ✅ |
| A3 | เฟรมเปิดอัตโนมัติให้พอดีเนื้อหา | `frameContent()` | ✅ |
| A4 | รายการแมพ + ค้นหา + thumbnail | `_objList` / API public | ✅ |
| A5 | มินิแมพ + เรด้าแสดงสัตว์/วัตถุรอบตัว | `drawMinimap()` :3714, `radarBtn` | ❌ |
| A6 | เข็มทิศ (เหนือ = แดง) | `compass` | ❌ |
| A7 | perf HUD (fps/draw calls) | `showPerfHud()` :4007 | ⚠️ มีแต่ log `avgFrameMs` ไม่มี HUD |

## B. บรรยากาศ / เรนเดอร์ (5.5/9)
| # | ฟีเจอร์เว็บ | หลักฐาน | Unity |
|---|---|---|---|
| B1 | พื้นทราย superellipse + slab + ขอบ haze | `SAND_R`, `applySeabedTint()` | ✅ WO-04.2 |
| B2 | ผิวน้ำ + คลื่นขยับ | `surf`, wave displace | ⚠️ มีจาน+UV scroll ยังไม่มี vertex wave |
| B3 | ฉากหลังไล่สีแนวตั้ง | `waterBg` :662 | ✅ WO-04.2 |
| B4 | fog ใต้น้ำ | `THREE.Fog(0x123a55,…)` | ✅ WO-04.3 |
| B5 | god rays + caustics | ไม่มีในเว็บ (เราทำเพิ่ม) | ✅ ดีกว่าเว็บ |
| B6 | สลับโหมดกลางวัน ☀️ / โหมดน้ำ 💧 เปิด-ปิด | `setEnv()`, `waterModeBtn`, `bright` | ❌ |
| B7 | heatmap ความลึก + legend | `setDepthView()` :640, `depthLegend` | ❌ |
| B8 | FX ทองเรืองแสง + หนวดไหว | `_fxGold()`, `_fxBeard()` | ❌ |
| B9 | vignette + murk ตอนอยู่ในทัวร์ | `vignette`, `murkUI()` | ❌ |

## C. สัตว์ / AI (3.5/6)
| # | ฟีเจอร์เว็บ | หลักฐาน | Unity |
|---|---|---|---|
| C1 | ฝูงปลา boids + สัตว์ใหญ่ว่ายวน | `buildSchool()`, `schoolStep()` | ✅ WO-03 |
| C2 | ปลาเป็นโมเดล GLB จริง | `InstancedMesh` ใน `buildSchool` | ✅ WO-04.1 |
| C3 | หลบสิ่งกีดขวาง (solid avoidance) | `computeObsR()`, `ejectFromSolids()` | ✅ |
| C4 | genome ต่อสายพันธุ์ + locomotion จาก animation | `speciesGenome()`, `deriveLocomotion()` | ⚠️ ใช้ตารางค่าคงที่ ไม่ derive จาก clip |
| C5 | ปลาตกใจ/หนีผู้เล่น + หาที่หลบ | `schoolFlee()`, `shelterSense()`, `senseAgents()` | ❌ |
| C6 | เสียงสัตว์ตามระยะ | `_animalSfxTick()` | ❌ |

## D. ทัวร์ดำน้ำ (โดรน) — 4/10 (P1.1 เสร็จ)
`enterTour()` :3635 · `exitTour()` · `tourUpdate()`
| # | ฟีเจอร์ | หลักฐาน | Unity |
|---|---|---|---|
| D1 | เข้า/ออกโหมดทัวร์ + ล็อกจอแนวนอน | `enterTour`, `tourLockLandscape()` | ✅ P1.1 |
| D2 | จอยสติ๊ก 2 ตัว (ขึ้น-ลง/เลี้ยว + เดินหน้า) | `makeStick()`, `stickL`, `stickR`, `knobL/R` | ✅ P1.1 (ยังเป็นแป้นเหลี่ยม รอทำวงกลม) |
| D3 | ไฟหน้าโดรน (โคนแสง + spotlight) | `_applyHeadlight()`, `lightBtn`, `mkBeam()` :3669 | ❌ |
| D4 | อ่านค่าความลึกสดขณะบิน | `tourDepth`, `depthMetres()` | ✅ P1.1 |
| D5 | **ถ่ายรูป** | `tourShot`, `captureThumb()` | ❌ |
| D6 | **อัดวิดีโอ + นาฬิกาจับเวลา** | `tourRec`, `stopRec()` :3801, `_recClock()` | 🚫 ตัดออกจาก v1 (user เคาะ) |
| D7 | บับเบิลจากโดรน | `droneBubble()` | ❌ |
| D8 | ชนวัตถุแล้วถูกดันออก | `_ejectFromSolids()` | ✅ P1.1 |
| D9 | ทัวร์อัตโนมัติ (บินเอง) + กล้องทัวร์ | `tourCam`, `_tourInstBuild()` | ❌ |
| D10 | สอนท่าเล่นครั้งแรก | `_tutTour()`, `_guideRun()` | ❌ |

## E. เกม (เก็บขยะ / เหรียญ / ร้านค้า) — 0/8 ❌ ทั้งหมด
| # | ฟีเจอร์ | หลักฐาน |
|---|---|---|
| E1 | ขยะเกิดในแมพ (หลายชนิด + sprite) | `spawnTrash()`, `_buildTrash()`, `_trashSprite()` |
| E2 | เก็บขยะ (แตะ/ว่ายชน) | `collectTrash()` :4087, `_pickTrash()` |
| E3 | เหรียญเกิด + บินเข้ากระเป๋า | `_spawnCoin()` :4069, `flyCoin()` |
| E4 | ยอดเหรียญ + เซฟ/โหลด (ออนไลน์+ออฟไลน์) | `coinUI()`, `saveCoins()`, `_offlineCoinNote()` |
| E5 | ร้านค้า — ซื้อสัตว์มาปล่อย | `openShop()` :4238, `buyAnimal()`, `priceOf()` |
| E6 | โหมด arena / เลือกโลกแล้วเล่น | `_startArenaPlay()`, `_arenaExitGate()` |
| E7 | เสียงเอฟเฟกต์ + เพลงบรรยากาศ + ปิดเสียง | `_playSfx()` :4381, `_ambPlay()`, `_toggleMute()` |
| E8 | ประตูวาปให้ผู้เล่นเดินทางข้ามแมพ | `_warpMenu()`, `_doWarp()`, `_warpFlash()` |

## F. AR / Holomap — 0/4 ❌
| F1 | AR วางแมพในกล้อง (WebXR) | `enterAR()` :2904 · Unity แผน = WO-02m |
| F2 | Holomap ใช้ไจโรหมุนดู | `applyGyro()` :2901, `holostart` |
| F3 | วางโฮโลแกรมข้างหน้าตัว | `placeHoloInFront()` |
| F4 | แถบควบคุม AR (ย่อ/ขยาย/ออก) | `arctl`, `arPlus/arMinus`, `exitAR` |

## G. pins + มีเดีย — 0/5 ❌
`placePin()` :2863 · `openPin()` · `renderPin()` · `addMedia()`/`saveMedia()` · `pinNav` (เลื่อนดูรูปในพิน)

## H. เชือก 🪢 — 0/6 ❌
`_startRope()` :3198 · `_startRopeFree()` :3197 · `catenaryPts()` (ท้องเชือกห้อย) · `_editRope()` (sag/หนา/สี) · `_reanchorRope()` · `removeRope()`

## I. Builder แก้ไข/สร้าง — 0/14 ❌
palette+หมวด+variants (`buildCats`, `showVariants`) · วางวัตถุ (`placeObj`, `tryPlace`, `moveGhost`) · gizmo ย้าย/หมุน/สเกล (`tc`) · multi-select + pivot + snap (`_msStart`, `_msSnap`) · ก๊อป (`dupSelected`) · ลบ · เปลี่ยนสี (`recolorSelected`, `buildColorBar`) · ตั้งชื่อวัตถุ (`_objName`) · รายการวัตถุ + ค้นหา + จัดกลุ่ม (`_olRender`, `_olSearch`) · undo/redo (`pushHist`, `undo`, `redo`) · ประวัติเวอร์ชัน + กู้คืน (`openRevModal`, `restoreRev`) · ล้างทั้งหมด (`histClearScene`) · **sculpt แปรง 6 แบบ** (`applyBrush` :2798, `brRaise/brDig/brFlat/brNoise/brRand/brSize/brStr`) · ตั้งพื้นที่/ความลาด/ระดับน้ำ (`applyArea`, `waterRange`)

> หมายเหตุ: Unity **อ่าน** ค่า sculpt/area/waterLevel มาเรนเดอร์ได้แล้ว (✅) แต่ **แก้ไม่ได้** — ข้อนี้นับเป็น "แก้ไข" ทั้งหมด

## J. บัญชี / เซฟ / แชร์ — 0/8 ❌
`doSave()`/`autosaveTick()` · ตั้งชื่อ+สาธารณะ/ส่วนตัว (`openNameModal`, `setPubLabel`) · thumbnail (`captureThumb`) · login รหัส/อีเมล (`openLogin`, `_lgCodeStep`) · โปรไฟล์ (`_profileSheet`) · **ให้สิทธิ์แก้ไขทางอีเมล** (`openPermission` :3399, `_permSave`) · **รายการโปรด + ทัวร์ออฟไลน์** (`_toggleFav` :3082, `_favCard`) · กันออกทั้งที่ยังไม่เซฟ (`leaveModal`)

## K. UI ทั่วไป (4/6)
| K1 | เมนู ☰ + navigation | ✅ WO-05.1 |
| K2 | การ์ดข้อมูลวัตถุ (ชื่อ/ชนิด/ความลึก) | ✅ WO-05.3 — เว็บมีรูป/มีเดียในการ์ดด้วย → ⚠️ |
| K3 | หน้าตั้งค่า | ✅ WO-05.4 |
| K4 | i18n | ⚠️ Unity ไทย/อังกฤษ · เว็บ 5 ภาษา (th/en/cn/ja/de ตาม routes) |
| K5 | toast แจ้งเตือน | ✅ P0 (บนจอ ไม่ใช่ล่างเหมือนเว็บ) |
| K6 | tutorial / tip cards | ❌ (`_tutMap`, `_tipCard`) |

---

## สรุปที่ต้องเข้าใจให้ตรงกัน
1. **แอปตอนนี้ = "ดูแมพสวยๆ"** — เรนเดอร์ใกล้เว็บแล้ว (~85% ของภาพนิ่ง) แต่**สิ่งที่ผู้ใช้ทำได้**ยังน้อยมาก
2. **ก้อนใหญ่ที่ไม่มี WO รองรับ** และต้องเพิ่มเข้า roadmap:
   - **WO ใหม่ "Viewer Parity"** = ทัวร์โดรน (D) + เครื่องมือดู (มินิแมพ/เข็มทิศ/ถ่ายรูป/อัดวิดีโอ/heatmap/โหมดแสง) ≈ 2-3 สัปดาห์
   - **WO ใหม่ "Game"** = ขยะ/เหรียญ/ร้านค้า/เสียง/warp (E) ≈ 2 สัปดาห์
   - **WO ใหม่ "Account & Offline"** = login/สิทธิ์/รายการโปรด-ออฟไลน์ (J) ≈ 1-1.5 สัปดาห์
   - pins+มีเดีย (G) และเชือก (H) อยู่ใน WO-09 แล้ว แต่ถูกเลื่อนไปสุดท้าย ทั้งที่เป็นของที่ **ผู้ใช้เห็นในโหมดดู** ไม่ใช่แค่เครื่องมือ builder
3. ลำดับที่แนะนำ (ผู้ใช้ได้ของเร็วสุดก่อน): **ทัวร์โดรน+ถ่ายรูป/อัดวิดีโอ → เข็มทิศ/มินิแมพ → เกมเก็บขยะ+เหรียญ → pins/มีเดีย → login+รายการโปรด → AR → builder แก้ไข**
