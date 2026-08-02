using System;
using System.Collections.Generic;
using UnityEngine;

namespace DiveMap.Core
{
    /// <summary>
    /// Tiny i18n table (WO-XR-05.1) that mirrors the web builder's approach exactly:
    /// the SOURCE strings are Thai (they are the keys), and a single TH→EN dictionary
    /// provides the English rendering — same shape as <c>TR</c> / <c>tr()</c> in
    /// builder.html. Nothing else is needed for two languages, and a missing key
    /// degrades gracefully to the Thai source instead of showing an empty label.
    ///
    /// Language selection: PlayerPrefs "lang" (values "th" / "en"); when unset it
    /// follows <see cref="Application.systemLanguage"/> (Thai → th, otherwise en) —
    /// the same default rule the web uses with navigator.language.
    ///
    /// Lives in Core (not Runtime/Ui) so the EditMode test assembly can reach it.
    /// </summary>
    public static class UiStrings
    {
        public const string LangPrefKey = "lang";
        public const string Thai = "th";
        public const string English = "en";

        // ── TH → EN table ────────────────────────────────────────────────────────
        // Keys are the literal Thai strings used in the UI code. Values must stay
        // pure Latin (UiStringsTests enforces both rules).
        private static readonly Dictionary<string, string> En =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // status line (P0: the header used to stay Thai even in English)
                { "กำลังวางวัตถุ…",         "Placing objects…" },
                { "โหลดแล้ว",              "loaded" },
                { "แทนที่",                "placeholder" },
                { "บันทึกแล้ว",             "Saved" },

                // tour (P1.1)
                { "ทัวร์ดำน้ำ",             "Dive tour" },
                { "ออกทัวร์",              "Exit tour" },
                { "ไฟหน้า",                "Headlamp" },
                { "ปิดเสียง",              "Mute" },
                { "บันทึกภาพลงแกลเลอรีแล้ว",  "Photo saved to your gallery" },
                { "บันทึกภาพในแอปแล้ว",      "Photo saved in the app" },
                { "แสดงความลึก (สี)",        "Depth colours on" },
                { "แสดงพื้นทรายปกติ",        "Depth colours off" },
                { "โหมดกลางวัน",            "Daylight view" },
                { "โหมดใต้น้ำ",             "Underwater view" },
                { "ประตูวาป — เลือกแมพปลายทาง", "Warp gate — pick a destination" },
                { "บันทึกภาพไม่สำเร็จ",      "Could not save the photo" },
                { "จอยซ้าย = ขึ้น/ลง + หัน · จอยขวา = เดินหน้า",
                  "Left stick rises and turns · right stick moves you" },
                // stick labels (web .lbl, 9.5px)
                { "ขึ้น",                   "Up" },
                { "ลง",                    "Down" },
                { "◀ หัน",                 "◀ Turn" },
                { "หัน ▶",                 "Turn ▶" },
                { "หน้า",                  "Fwd" },
                { "ถอย",                   "Rev" },   // NOT "Back": ย้อนกลับ already owns that, and
                                                        // UiStringsTests enforces unique English
                                                        // values so ToLang stays idempotent
                { "◀ สไลด์",               "◀ Slide" },
                { "สไลด์ ▶",               "Slide ▶" },
                { "เปิดเสียง",             "Unmute" },
                { "ยังเข้าทัวร์ไม่ได้",       "Cannot start the tour yet" },
                { "ลากจอยซ้ายเพื่อเลี้ยว/ขึ้นลง · จอยขวาเพื่อเดินหน้า",
                  "Left stick turns and rises · right stick moves you forward" },

                // AR (F1/F4) — the web's #arctl, #exitAR, #arhint
                { "ดูแบบ AR",                       "View in AR" },
                // "✕ ออก AR" removed with the pill it labelled — the AR exit is now a drawn
                // ✕ icon (IconPainter "close"), because NotoSansThai has no U+2715 and the
                // label shipped to a device reading bare "ออก AR".
                // "ขนาด" is already in the table (the gizmo's scale label) — one key, one entry,
                // and a duplicate throws inside the static initialiser, which takes out every test
                // that so much as mentions UiStrings rather than failing where the mistake is.
                { "เล็งกล้องไปที่พื้นเรียบ",             "Point the camera at a flat surface" },
                { "ใหญ่สุดแล้ว",                     "That is as large as it goes" },
                { "เล็กสุดแล้ว",                      "That is as small as it goes" },
                { "เครื่องนี้ไม่มีเซนเซอร์ — ลากเพื่อหมุนแทน",
                  "No motion sensor on this device — drag to look around instead" },
                { "เปิดกล้องไม่ได้ — แสดงแบบจำลองอย่างเดียว",
                  "Camera unavailable — showing the model only" },
                { "เข้า AR ตอนนี้ไม่ได้",              "AR cannot start right now" },

                // AR placement flow (tap → pinch → confirm → ARAnchor). One line per STEP, because
                // each one has to tell the user the single thing that is possible right now — the
                // old overlay said "point at a flat surface" from the moment AR opened until it
                // closed, including while the map was already on the table.
                { "เล็งกล้องไปที่พื้นเรียบ กำลังหาพื้น…",   "Point at a flat surface — looking for one…" },
                { "เจอพื้นแล้ว — แตะตรงที่อยากวางแผนที่",  "Surface found — tap where you want the map" },
                { "ยังไม่เจอพื้นตรงนั้น — เล็งกล้องไปที่พื้นเรียบ",
                  "No surface there yet — aim at a flat one" },
                { "สองนิ้วย่อ-ขยาย · แตะเพื่อย้าย · กดยืนยันเมื่อพอใจ",
                  "Pinch to resize · tap to move · confirm when it looks right" },
                { "✓ ยืนยัน",                        "✓ Confirm" },
                // The no-tracking path: sizing is the only thing it can offer.
                { "สองนิ้วย่อ-ขยาย",                  "Pinch to resize" },
                { "ยึดกับพื้นแล้ว — เดินรอบดูได้เลย",     "Pinned to the floor — walk around it" },
                { "ย้ายตำแหน่ง",                     "Move it" },
                { "ยึดกับพื้นไม่ได้ — วางไว้ตรงนี้ก่อน",   "Could not pin it — leaving it where it is" },
                // The metre unit on the size readout is already in this table further down (the
                // depth label owns "ม." → "m"). One key, one entry: a duplicate throws inside the
                // static initialiser and takes out every test that so much as mentions UiStrings.

                // shell / menu
                { "เมนู",                  "Menu" },
                { "รายการแมพ",             "Maps" },
                { "ตั้งค่า",                "Settings" },
                { "ปิด",                   "Close" },
                { "ย้อนกลับ",              "Back" },
                { "เร็วๆ นี้",              "Coming soon" },

                // shop (E5)
                { "ร้านค้า",                "Shop" },
                { "เหรียญไม่พอ",            "Not enough coins" },
                { "ซื้อแล้ว",               "Purchased" },
                { "ปล่อยลงแมพแล้ว — กำลังโหลดใหม่", "Released — reloading" },
                { "บันทึกลงแมพแล้ว",         "Saved into the map" },
                { "มีคนแก้แมพนี้ก่อน — เก็บไว้ในเครื่องนี้แทน",
                  "Someone edited this map first — kept on this device instead" },
                { "แมพนี้แก้ไม่ได้ — เก็บไว้ในเครื่องนี้แทน",
                  "This map is not editable — kept on this device instead" },
                { "บันทึกไม่สำเร็จ — เก็บไว้ในเครื่องนี้แทน",
                  "Could not save — kept on this device instead" },
                // palette = the shop (placing IS buying, builder.html tryPlace :4298)
                { "ซื้อสัตว์ต้องต่อเน็ต",      "Buying sea life needs a connection" },
                { "เหรียญไม่พอ — ต้องการ",    "Not enough coins — you need" },
                { "ปักหมุด",               "Pin" },
                { "ปั้นพื้น",              "Sculpt floor" },

                // editing (I) — selection toolbar + history
                { "ก๊อปแล้ว",              "Duplicated" },
                { "ลบแล้ว",               "Deleted" },
                { "ยังไม่มีแมพให้แก้",       "No map to edit yet" },
                { "ย้าย",                 "Move" },
                { "หมุน",                 "Rotate" },
                { "ขนาด",                 "Size" },
                { "สี",                   "Colour" },
                { "ก๊อป",                  "Duplicate" },
                { "ลบ",                   "Delete" },
                { "เลิกทำ",                "Undo" },
                { "ทำซ้ำ",                 "Redo" },
                { "ล้างทั้งหมด",            "Clear all" },
                { "แตะอีกครั้งเพื่อล้างทั้งแมพ", "Tap again to clear the whole map" },
                { "ล้างแมพแล้ว",           "Map cleared" },
                { "โมเดลบนแมพ",            "Objects on the map" },
                { "ค้นหา",                 "Search" },
                { "ทุกชนิด",               "All kinds" },
                { "ตั้งชื่อวัตถุ",           "Name this object" },
                { "บันทึก",                "Save" },
                { "แมพนี้แก้ไม่ได้",         "This map is not editable" },
                // version history (I) + clear
                { "ประวัติเวอร์ชัน",         "Version history" },
                { "กู้คืนแล้วของที่แก้หลังจากนั้นจะหายไป", "Restoring discards everything edited since" },
                { "เวอร์ชัน",              "Version" },
                { "กู้คืนเวอร์ชัน",          "Restore version" },
                { "กู้คืน",                "Restore" },
                { "กู้คืนแล้ว",             "Restored" },
                { "กู้คืนไม่สำเร็จ",         "Could not restore" },
                { "ยังไม่มีประวัติ",         "No history yet" },
                { "โหลดประวัติไม่สำเร็จ",     "Could not load the history" },
                { "เฉพาะเจ้าของแมพเท่านั้นที่กู้คืนได้", "Only the map owner can restore" },
                { "เมื่อครู่",              "just now" },
                { "นาทีที่แล้ว",            "min ago" },
                { "ชม. ที่แล้ว",            "h ago" },
                { "วันที่แล้ว",             "d ago" },
                // sculpt (I)
                { "ลากบนพื้นเพื่อปั้น",       "Drag on the floor to sculpt" },
                { "ขุดหลุม",               "Dig" },
                { "ก่อเนิน",               "Raise" },
                { "ขนาดหัวแปรง",           "Brush size" },
                { "ความแรง",               "Strength" },
                { "สุ่มพื้น",              "Randomise" },
                { "รีเซ็ตเรียบ",            "Reset flat" },
                { "สุ่มพื้นแล้ว",           "Floor randomised" },
                { "รีเซ็ตพื้นแล้ว",          "Floor reset" },
                { "พ้นน้ำ",                "above water" },
                { "ลึก",                   "depth" },
                // ropes (H)
                { "ปรับเชือก",              "Rope settings" },
                { "ความห้อย",              "Sag" },
                { "ความหนา",               "Thickness" },
                { "ลบเชือก",               "Delete rope" },
                { "ลบเชือกแล้ว",            "Rope deleted" },
                { "ผูกเชือก",               "Tie a rope" },
                { "แตะจุดยึดที่ 1 (บนวัตถุ)",  "Tap the first anchor (on an object)" },
                { "แตะจุดยึดที่ 2",          "Tap the second anchor" },
                { "แตะให้โดนวัตถุ",          "Tap an object, not the water" },
                { "ยกเลิกเชือก",            "Rope cancelled" },
                { "ต้องเป็นคนละชิ้น",         "Pick two different objects" },
                { "เชื่อมเชือกแล้ว",          "Rope tied" },
                { "ผูกเชือกไม่สำเร็จ",        "Could not tie the rope" },
                // map settings (J2/J6 + I12/I14)
                { "ตั้งค่าแมพ",             "Map settings" },
                { "ชื่อแมพ",               "Map name" },
                { "สาธารณะ",              "Public" },
                { "ส่วนตัว",               "Private" },
                { "สาธารณะ = ใครเปิดก็แก้แมพนี้ได้ ไม่ใช่แค่ดู",
                  "Public means anyone who opens it can EDIT it, not just view it" },
                { "แสดงในการค้นหา",         "Listed in search" },
                { "ไม่แสดงในการค้นหา",       "Hidden from search" },
                { "ให้สิทธิ์แก้ไขทางอีเมล",     "Let specific emails edit" },
                { "ระดับน้ำ",              "Water level" },
                { "ขนาดพื้นที่",            "Area size" },
                { "เปิดสาธารณะแล้ว",         "Now public" },
                { "เป็นส่วนตัวแล้ว",         "Now private" },
                { "บันทึกสิทธิ์แล้ว",         "Permissions saved" },
                { "เฉพาะเจ้าของแมพเท่านั้น",   "Only the map owner can do that" },
                { "บันทึกไม่สำเร็จ",         "Could not save" },
                // pins (G)
                { "แตะบนแผนที่เพื่อปักหมุด",   "Tap the map to drop a pin" },
                { "ปักหมุดแล้ว",            "Pin dropped" },
                { "ลบหมุด",                "Delete pin" },
                { "ลบหมุดแล้ว",             "Pin deleted" },
                { "เพิ่มรูป",               "Add a photo" },
                { "เพิ่มรูปแล้ว",            "Photo added" },
                { "อัปโหลดไม่สำเร็จ",        "Upload failed" },
                { "ตั้งรูปหน้าปกแล้ว",        "Cover photo set" },
                { "กำลังบันทึก…",           "Saving…" },
                { "ตั้งรูปหน้าปก",           "Set cover photo" },
                // offline (J7 / A4)
                { "โหมดออฟไลน์ — ใช้สำเนาในเครื่อง", "Offline — using the copy on this device" },
                { "แมพนี้ยังไม่มีในเครื่อง",   "This map is not on your device yet" },
                { "เลือกแล้ว",             "selected" },
                // arena exit gate (E6)
                { "เก็บเหรียญที่ได้?",        "Keep the coins you earned?" },
                { "เข้าสู่ระบบเก็บ",          "Sign in and keep them" },
                { "ทิ้ง",                  "Discard" },
                { "ภายหลัง",               "Later" },
                { "เปิดแมพนี้ตอนออนไลน์ 1 ครั้ง แล้วจะใช้แบบออฟไลน์ได้",
                  "Open it once while online and it will work offline" },

                // account (J) — wording taken from the RN app's mapI18n.ts
                { "เข้าสู่ระบบ / สมัครสมาชิก", "Sign in / Sign up" },
                { "ใส่อีเมลเพื่อรับรหัส OTP",  "Enter your email to get an OTP code" },
                { "แมพและเหรียญในเครื่องนี้จะถูกผูกเข้าบัญชี",
                  "The maps and coins on this device will be linked to the account" },
                { "ใส่รหัส 6 หลัก",          "Enter the 6-digit code" },
                { "ส่งไปที่",               "Sent to" },
                { "เข้าสู่ระบบแอดมิน",        "Admin sign-in" },
                { "ใส่ passcode แอดมิน",     "Enter the admin passcode" },
                { "ตั้งชื่อผู้ใช้",           "Choose a username" },
                { "3-20 ตัว · ไทย/อังกฤษ/เลข/เว้นวรรค/_", "3-20 chars, letters/numbers/space/_" },
                { "ชื่อของคุณ",             "Your name" },
                { "ส่งรหัส",               "Send code" },
                { "ยืนยัน",                "Verify" },
                { "เสร็จ",                 "Done" },
                { "เข้าสู่ระบบ",            "Sign in" },
                { "เข้าสู่ระบบแล้ว",         "Signed in" },
                { "ออกจากระบบ",            "Log out" },
                { "ออกจากระบบแล้ว",         "Signed out" },
                { "ลบบัญชี",               "Delete account" },
                { "แตะอีกครั้งเพื่อลบบัญชีถาวร", "Tap again to delete permanently" },
                { "ลบบัญชีแล้ว",            "Account deleted" },
                { "ลบบัญชีไม่สำเร็จ",        "Could not delete the account" },
                { "(ยังไม่ตั้งชื่อ)",         "(no name yet)" },
                { "เก็บเข้า My Map",        "Keep in My Map" },
                { "เอาออกจาก My Map",       "Remove from My Map" },
                { "เก็บเข้า My Map แล้ว",    "Kept in My Map" },
                { "เอาออกจาก My Map แล้ว",   "Removed from My Map" },
                // sign-in errors (the routes answer with machine keys; these are the sentences)
                { "อีเมลไม่ถูกต้อง",         "Invalid email" },
                { "เพิ่งส่งไป รอสักครู่",     "Just sent — wait a moment" },
                { "รหัสผิด",                "Wrong code" },
                { "รหัสหมดอายุ",            "Code expired" },
                { "ชื่อสั้นไป (3-20 ตัว)",   "Name must be 3-20 characters" },
                { "ชื่อนี้มีคนใช้แล้ว ลองชื่ออื่น", "Name taken — try another" },
                { "ชื่อนี้สงวนไว้",          "That name is reserved" },
                { "passcode ผิด",          "Wrong passcode" },
                { "เชื่อมต่อไม่ได้",         "Connection failed" },

                // perf readout (A7)
                { "ตัวเลขเฟรมเรต",          "Frame rate" },
                { "แสดง",                  "Show" },
                { "ซ่อน",                  "Hide" },

                // pins (G)
                { "ยังไม่มีรูป/วิดีโอ",       "No photos or video yet" },
                { "คลิปวิดีโอ — เปิดดูได้ในเว็บ", "Video clip — playable on the web" },
                { "ไม่สามารถแสดงไฟล์นี้",     "This file cannot be shown" },
                { "โหลดรูปไม่สำเร็จ",         "The photo could not be loaded" },

                // first-dive tutorial (D10)
                { "ข้าม",                  "Skip" },
                { "ถัดไป",                 "Next" },
                { "เริ่มเลย!",              "Start!" },
                { "จอยซ้าย",               "Left stick" },
                { "จอยขวา",                "Right stick" },
                { "กล้อง",                 "Camera" },
                { "ไฟฉาย",                 "Lamp" },
                { "เหรียญของคุณ",           "Your coins" },
                { "ลาก ขึ้น/ลง เพื่อลอย-ดำลง · ซ้าย/ขวา เพื่อหันกล้อง",
                  "Drag up/down to rise and dive; left/right to turn" },
                { "ลาก หน้า/ถอย เพื่อว่ายไป · ซ้าย/ขวา เพื่อสไลด์ข้าง",
                  "Drag forward/back to swim; left/right to strafe" },
                { "ปุ่มกล้อง: ถ่ายภาพเก็บลงเครื่อง", "Shutter: save a photo to your phone" },
                { "เปิดไฟหน้าโดรน มองเห็นตอนดำลึก", "Turn on the headlight for deeper water" },
                { "เก็บขยะและเหรียญทองที่ตกลงมา = ได้เหรียญ เอาไว้ซื้อสัตว์ทะเลในร้านค้า",
                  "Collect the litter and gold coins that fall, then spend them in the shop" },
                { "ใช้เหรียญซื้อสัตว์ทะเลมาปล่อยลงแมพของคุณ",
                  "Spend coins on sea life and release it into your map" },
                { "กลับไปหน้าแมพเมื่อเที่ยวเสร็จ", "Back to the map when you are done" },

                // map list / search
                { "ค้นหาแมพ",              "Search maps" },
                { "กำลังโหลด…",            "Loading…" },
                { "ไม่พบแมพ",              "No maps found" },
                { "โหลดรายการแมพไม่สำเร็จ",  "Could not load the map list" },
                { "ลองใหม่",               "Retry" },
                { "ถูกใจ",                 "Likes" },
                { "ไม่ทราบผู้สร้าง",         "Unknown creator" },
                { "กำลังเปิดแมพ…",          "Opening map…" },

                // map hub — ported 1:1 from the RN app's mapI18n.ts so the two products
                // say the same words. Keys keep the Latin "dive site" the RN copy uses.
                { "ค้นหา dive site สาธารณะ…", "Search public dive sites…" },
                { "ไม่พบ dive site",        "No dive sites found" },
                { "ยังไม่มี dive site",      "No dive sites yet" },
                { "สร้างโดย",              "by" },              // + owner name
                { "สร้างโดย ชุมชน",         "by Community" },
                { "สร้างโดย คุณ",           "by You" },
                { "โดย SIAMDIVE",          "by SIAMDIVE" },
                { "เปิดแผนที่",             "Go To Map" },
                { "ยกเลิก",                "Cancel" },
                { "รายงาน",                "Report" },
                { "ขอบคุณที่รายงาน",         "Thanks for reporting" },
                { "แมพนี้ถูกซ่อนเพื่อรอตรวจสอบแล้ว", "This map has been hidden pending review" },
                { "ส่งรายงานไม่สำเร็จ",       "Could not send the report" },
                { "ยังไม่เปิดให้ใช้ในแอปนี้",   "Not available in this app yet" },

                // "Play Game!" banner + worlds picker
                { "เล่นเกม!",               "Play Game!" },
                { "ดำลงเก็บเหรียญ เก็บขยะใต้น้ำ", "Dive in, collect coins & clean up the reef" },
                { "เลือกโลกที่จะดำลง — วาประหว่างกันได้", "Pick a world to dive in - warp between them" },
                { "ค้นหาโลก…",             "Search worlds…" },

                // info card (05.3)
                { "ความลึก",               "Depth" },
                { "ม.",                    "m" },
                { "ชนิด",                  "Type" },
                { "ไม่ทราบชนิด",            "Unknown type" },

                // settings (05.4)
                { "ภาษา",                  "Language" },
                { "ไทย",                   "Thai" },
                { "คุณภาพกราฟิก",           "Graphics quality" },
                { "คุณภาพสูง",              "High" },
                { "ประหยัดพลังงาน",         "Battery saver" },
                { "ความเร็วโดรน",           "Drone speed" },
                { "ช้า",                    "Slow" },
                { "ปกติ",                   "Normal" },
                { "เร็ว",                   "Fast" },
                { "เวอร์ชันแอป",            "App version" },
                { "เว็บไซต์",               "Website" },
                { "เปิดเว็บไซต์",           "Open website" },

                // AppBoot status / error lines. AppBoot itself is owned by another work
                // order and is NOT edited: UiShell re-renders these Text components in
                // place when the language changes (see UiShell.ApplyLanguage).
                { "กำลังโหลดแมพ…",          "Loading map…" },
                { "กำลังเชื่อมต่อ…",         "Connecting…" },
                { "โหลดแมพไม่สำเร็จ",       "Could not load the map" },
                { "สร้างแมพไม่สำเร็จ",       "Could not build the map" },
                { "เซิร์ฟเวอร์ตอบกลับว่าง",   "Empty response from the server" },

                // ชนิด (kind) — labels ported from builder.html KIND_META (L1037-1038)
                { "หิน",                           "Rock" },
                { "ปะการัง",                       "Coral" },
                { "เรือ",                          "Boat" },
                { "สัตว์ทะเล",                     "Marine life" },
                { "ฝูงปลา",                        "School" },
                { "ปะการังเทียม",                  "Artificial reef" },
                { "ซากเรือ",                       "Wreck" },
                { "ดอกไม้ทะเล",                    "Anemone" },
                { "ปลา",                           "Fish" },
                { "เต่า",                          "Turtle" },
                { "พืช",                           "Plant" },
                { "อื่นๆ",                         "Other" },
                { "นักดำน้ำ",                      "Diver" },
                { "พิเศษ",                         "Special" },
                // kind SPECIAL / assetId warp:* — same wording the web uses (builder.html L796).
                { "ประตูวาป",                      "Warp gate" },

                // ชื่อ asset จาก asset_manifest.json — EN จาก builder.html TR (L742-1022); 15 คีย์ท้ายที่เว็บยังไม่มี แปลเพิ่มที่นี่
                { "กลม",                           "Round" },
                { "สูง",                           "Tall" },
                { "แบน",                           "Flat" },
                { "กอง",                           "Cluster" },
                { "เขากวาง",                       "Staghorn" },
                { "สมอง",                          "Brain" },
                { "พัด",                           "Fan" },
                { "ท่อ",                           "Tube" },
                { "ชมพู",                          "Pink" },
                { "ม่วง",                          "Purple" },
                { "เขียว",                         "Green" },
                { "ฝูงเหลือง",                     "Yellow school" },
                { "ฝูงฟ้า",                        "Blue school" },
                { "ฝูงส้ม",                        "Orange school" },
                { "ตัวใหญ่",                       "Large" },
                { "เล็ก",                          "Small" },
                { "สมอ",                           "Anchor" },
                { "เต่าทะเล (Loggerhead)",         "Loggerhead Turtle" },
                { "ปะการังต้นอำพัน",               "Amber coral tree" },
                { "ปะการังสมอง",                   "Brain coral" },
                { "ปะการังแดงเข้ม 1",              "Crimson coral 1" },
                { "ปะการังแดงเข้ม 2",              "Crimson coral 2" },
                { "กัลปังหาแดง",                   "Red sea fan" },
                { "ปะการังหนวดมงกุฎ",              "Crown tentacle coral" },
                { "ปะการังเบญจมาศทอง",             "Golden chrysanthemum coral" },
                { "ฟองน้ำทอง",                     "Golden sponge" },
                { "ปะการังต้นทอง",                 "Golden coral tree" },
                { "ปะการังเห็ดขน",                 "Hairy mushroom coral" },
                { "ปะการังงาช้าง",                 "Ivory coral" },
                { "ปะการังงาช้างกอ",               "Ivory coral cluster" },
                { "ปะการังงาช้างต้น",              "Ivory coral tree" },
                { "ปะการังต้นส้ม",                 "Orange coral tree" },
                { "ปะการังพีชกอ",                  "Peach coral cluster" },
                { "ปะการังเมฆชมพู",                "Pink cloud coral" },
                { "ปะการังบานชมพู",                "Pink bloom coral" },
                { "ปะการังช่อชมพู",                "Pink bouquet coral" },
                { "ปะการังต้นกุหลาบ",              "Rose coral tree" },
                { "ปะการังขาวกอ",                  "White coral cluster" },
                { "เต่าทะเล (สมจริง)",             "Sea turtle (realistic)" },
                { "บาราคูด้า",                     "Barracuda" },
                { "ปลาสิงโต",                      "Lionfish" },
                { "ปู",                            "Crab" },
                { "ม้าน้ำ",                        "Seahorse" },
                { "ฉลามวาฬ",                       "Whale shark" },
                { "กระเบนแมนต้า",                  "Manta ray" },
                { "ปลาหางเหลือง",                  "Yellowtail" },
                { "ปลากล่อง",                      "Boxfish" },
                { "ฉลามเสือ",                      "Tiger shark" },
                { "ฉลามครีบเงิน",                  "Silvertip shark" },
                { "ฉลามเสือดาว",                   "Leopard shark" },
                { "ฉลามพยาบาล",                    "Nurse shark" },
                { "ฉลามครีบขาว",                   "Whitetip reef shark" },
                { "ฉลามครีบดำ",                    "Blacktip reef shark" },
                { "ฉลามหางยาว",                    "Thresher shark" },
                { "ทูน่าครีบน้ำเงิน",              "Bluefin tuna" },
                { "ปลาทูน่า",                      "Tuna" },
                { "ปลากระโทงร่ม",                  "Sailfish" },
                { "วาฬสเปิร์ม",                    "Sperm whale" },
                { "วาฬเบลูกา",                     "Beluga whale" },
                { "วาฬเพชฌฆาต",                    "Orca" },
                { "โลมา (สมจริง)",                 "Dolphin (realistic)" },
                { "วาฬหลังค่อม",                   "Humpback whale" },
                { "กระเบนแมนต้ายักษ์",             "Giant manta ray" },
                { "กระเบนแมนต้าดำ",                "Black manta ray" },
                { "กระเบนนก",                      "Eagle ray" },
                { "ปลาโมลาโมลา",                   "Mola mola" },
                { "ฝูงบาราคูด้า",                  "Barracuda school" },
                { "ฝูงปลาข้างเหลือง",              "Yellowstripe scad school" },
                { "วาฬหลังค่อม แม่-ลูก",           "Humpback whale, mother & calf" },
                { "ฝูงกระเบนนก",                   "Eagle ray school" },
                { "ฝูงโลมา",                       "Dolphin pod" },
                { "ฝูงปลาหางเหลือง",               "Yellowtail school" },
                { "ฝูงฉลามหัวค้อน",                "Hammerhead shark school" },
                { "ฝูงฉลามครีบดำ",                 "Blacktip shark school" },
                { "ฝูงปลาค้างคาว",                 "Batfish School" },
                { "ฝูงนกแก้วปริซึม",               "Prismatic Parrotfish School" },
                { "ฝูงวาฬเพชฌฆาต",                 "Orca Pod" },
                { "เรือดำน้ำ",                     "Dive boat" },
                { "เรือยนต์",                      "Motorboat" },
                { "เรือเล็ก",                      "Small boat" },
                { "เรือใบ",                        "Sailboat" },
                { "เรือสำราญ",                     "Cruise ship" },
                { "เรือยอชต์",                     "Yacht" },
                { "แพยาง",                         "Raft" },
                { "เรือไม้",                       "Wooden boat" },
                { "หินใหญ่",                       "Large rock" },
                { "กองหิน",                        "Rock pile" },
                { "หิน 1",                         "Rock 1" },
                { "หิน 2",                         "Rock 2" },
                { "หิน 3",                         "Rock 3" },
                { "หิน 4",                         "Rock 4" },
                { "หินใหญ่ 2",                     "Large rock 2" },
                { "หินกลาง",                       "Medium rock" },
                { "หินเล็ก",                       "Small rock" },
                { "ก้อนหินใหญ่",                   "Large boulder" },
                { "หินก้อน 1",                     "Boulder 1" },
                { "หินก้อน 2",                     "Boulder 2" },
                { "กองหิน 2",                      "Rock pile 2" },
                { "กองหิน 3",                      "Rock pile 3" },
                { "เรือจม (สมจริง)",               "Shipwreck (realistic)" },
                { "เครื่องบินจม",                  "Sunken plane" },
                { "มอเตอร์ไซค์จม",                 "Sunken motorcycle" },
                { "รถยนต์จม",                      "Sunken car" },
                { "ถังจม",                         "Sunken barrel" },
                { "รถบรรทุกจม",                    "Sunken truck" },
                { "ยางรถจม",                       "Sunken tire" },
                { "ตู้คอนเทนเนอร์",                "Container" },
                { "HTMS ช้าง",                     "HTMS Chang" },
                { "HTMS ปราบ",                     "HTMS Prab" },
                { "เรือฮาร์ดีพ",                   "Hardeep wreck" },
                { "รูปปั้นสิงห์ใต้น้ำ",            "Underwater Singha lion statue" },
                { "คราเคน",                        "Kraken" },
                { "โพไซดอน",                       "Poseidon" },
                { "ต้นไม้",                        "Tree" },
                { "ป่าสน",                         "Pine forest" },
                { "ต้นปาล์ม",                      "Palm tree" },
                { "กลุ่มปาล์ม",                    "Palm cluster" },
                { "ภูเขา",                         "Mountain" },
                { "ยอดเขา",                        "Peak" },
                { "สาหร่ายเคลป์",                  "Kelp" },
                { "สาหร่าย",                       "Seaweed" },
                { "พุ่มไม้",                       "Bush" },
                { "หญ้า",                          "Grass" },
                { "หญ้าสูง",                       "Tall grass" },
                { "ภูมิประเทศ",                    "Terrain" },
                { "ทุ่นธงแดง",                     "Red Flag Buoy" },
                { "ทุ่นเหลือง",                    "Yellow Buoy" },
                { "เรือบุญสูง",                    "Boonsung Wreck" },
                { "ตรีศูลทองคำ",                   "Golden Trident" },
                { "ราชาศิลา",                      "Stone King" },
                { "ซุ้มโค้งไบแซนไทน์",             "Byzantine Arch" },
                { "ซุ้มคริสตัลเรืองแสง",           "Glowing Crystal Arch" },
                { "วิหารไบแซนไทน์",                "Byzantine Temple" },
                { "วิหารโดมโบราณ",                 "Ancient Domed Temple" },
                { "ประตูแฟนตาซี",                  "Fantasy Gate" },
                { "หินขั้นบันได",                  "Stepped Rock" },
                { "อนุสรณ์สลักลาย",                "Carved Memorial" },
                { "เสาหักโบราณ",                   "Broken Ancient Column" },
                { "ซุ้มโค้งยาว",                   "Long Arcade" },
                { "ซากไบแซนไทน์",                  "Byzantine Ruins" },
                { "หินสลักโบราณ",                  "Ancient Carved Stone" },
                { "โพไซดอนศิลาเขียว",              "Green Stone Poseidon" },
                { "เทพนักรบพายุ",                  "Storm Warrior God" },
                { "นักรบผู้ทะยาน",                 "Charging Warrior" },
                { "ก้อนหินปะการังเกาะ",            "Coral-Encrusted Boulder" },
                { "เสาหินปกคลุมตะไคร่",            "Lichen-Covered Pillar" },
                { "แท่งหินโมโนลิธ",                "Monolith" },
                { "ซากเครื่องบินทะเล",             "Seaplane wreck" },
                { "รูปปั้นนักปั่นจักรยาน",         "Cyclist statue" },
                { "รูปปั้นคนนอน",                  "Reclining person statue" },
                { "รูปปั้นเต่า",                   "Turtle statue" },
                { "รูปปั้นสิงห์",                  "Singha lion statue" },
                { "รูปปั้นสิงโตจีน",               "Chinese lion statue" },
                { "เศียรพระหิน",                   "Stone Buddha head" },
                { "พระพุทธรูปนั่ง",                "Seated Buddha statue" },
                { "รูปปั้นกลุ่มเด็ก",              "Group of children statue" },
                { "รูปปั้นเด็กล้อมวง",             "Children in a circle statue" },
                { "รูปปั้นคนกอดเข่า",              "Person hugging knees statue" },
                { "รูปปั้นคู่รักกอดกัน",           "Embracing couple statue" },
                { "รูปปั้นคู่รักจูบ",              "Kissing couple statue" },
                { "รูปปั้นขบวนคน",                 "Procession statue" },
                { "รูปปั้นเด็กเดินกลุ่ม",          "Children walking together statue" },
                { "รูปปั้นคนล้อมวงกอด",            "People embracing in a circle statue" },
                { "รูปปั้นคนจับมือล้อมวง",         "People holding hands in a circle statue" },
                { "รูปปั้นแขนกอดขา",               "Arms hugging legs statue" },
                { "รูปปั้นคนเอื้อมฟ้า",            "Person reaching to the sky statue" },
                { "รูปปั้นมือ",                    "Hand statue" },
                { "รูปปั้นคนกางแขน",               "Person with open arms statue" },
                { "รูปปั้นคนอ่านหนังสือ",          "Person reading statue" },
                { "รูปปั้นคนอ่านหนังสือ 2",        "Person reading statue 2" },
                { "รูปปั้นเด็กกอดเข่า",            "Child hugging knees statue" },
                { "รูปปั้นคนคุกเข่า",              "Kneeling person statue" },
                { "รูปปั้นเด็กคุกเข่า",            "Kneeling child statue" },
                { "รูปปั้นชายชราผอม",              "Thin old man statue" },
                { "รูปปั้นคนใส่เสื้อคลุม",         "Cloaked person statue" },
                { "รูปปั้นคนใส่เสื้อคลุม 2",       "Cloaked person statue 2" },
                { "ปะการังเทียมกล่องซ้อน",         "Stacked-box artificial reef" },
                { "รูปปั้นคนถือกระเป๋า",           "Person holding a bag statue" },
                { "ปะการังเทียมกิ่งดาว",           "Star-branch artificial reef" },
                { "ปะการังเทียมกิ่งดาว 2",         "Star-branch artificial reef 2" },
                { "สมอเรือพร้อมโซ่",               "Anchor with chain" },
                { "ซากรถราง",                      "Tram wreck" },
                { "ซากรถราง 2",                    "Tram wreck 2" },
                { "ซากรถรางปะการัง",               "Coral tram wreck" },
                { "รถถังจม",                       "Sunken tank" },
                { "รถถังจม 2",                     "Sunken tank 2" },
                { "โครงโดมปะการังเทียม",           "Artificial reef dome frame" },
                { "โดมปะการังเทียมปุ่ม",           "Knobbed artificial reef dome" },
                { "พีระมิดปะการังเทียม",           "Artificial reef pyramid" },
                { "พีระมิดโครงเหล็ก",              "Steel-frame pyramid" },
                { "ปะการังเทียมกากบาท",            "Cross artificial reef" },
                { "ปะการังเทียมกากบาท 2",          "Cross artificial reef 2" },
                { "เสาหินซ้อน",                    "Stacked stone column" },
                { "หอกล่องซ้อน",                   "Stacked box tower" },
                { "ท่อปะการังเทียม",               "Artificial reef tube" },
                { "โมดูลสามเหลี่ยม",               "Triangle module" },
                { "โมดูลสามเหลี่ยม 2",             "Triangle module 2" },
                { "กล่องคอนกรีตเจาะ",              "Perforated concrete box" },
                { "บล็อกคอนกรีต 4 ช่อง",           "Four-hole concrete block" },
                { "หินรังผึ้ง",                    "Honeycomb rock" },
                { "ชั้นคอนกรีตปะการังเทียม",       "Concrete-layer artificial reef" },
                { "เรฟบอลกลุ่ม",                   "Reef-ball cluster" },
                { "โดมเรฟบอลพรุน",                 "Porous reef-ball dome" },
                { "โดมหลบภัย",                     "Shelter dome" },
                { "โดมหลบภัย 2",                   "Shelter dome 2" },
                { "กรอบกล่องโปร่ง",                "Open box frame" },
                { "กล่องโปร่งซ้อน",                "Stacked open box" },
                { "กล่องคอนกรีตซ้อน",              "Stacked concrete box" },
                { "ซากรถมินิ",                     "Mini car wreck" },
                { "รถจี๊ปจม",                      "Sunken jeep" },
                { "รูปปั้นพะยูน",                  "Dugong statue" },
                { "โถส้วมจม",                      "Sunken toilet" },
                { "ซากมอเตอร์ไซค์",                "Motorcycle wreck" },
                { "ซากมอเตอร์ไซค์ชอปเปอร์",        "Chopper motorcycle wreck" },
            };

        /// <summary>
        /// EN → TH, built lazily from <see cref="En"/>. Needed to switch the UI BACK to
        /// Thai without rebuilding every screen: a live <c>Text</c> already holds the
        /// English rendering, so the only way home is the reverse lookup.
        /// UiStringsTests guarantees the English column has no duplicates, so this map
        /// is unambiguous.
        /// </summary>
        private static Dictionary<string, string> _th;

        private static Dictionary<string, string> ThaiByEnglish()
        {
            if (_th != null) return _th;
            var map = new Dictionary<string, string>(En.Count, StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> kv in En)
                if (!string.IsNullOrEmpty(kv.Value)) map[kv.Value] = kv.Key;
            _th = map;
            return _th;
        }

        /// <summary>The whole TH→EN table (read-only) — used by tests and QC tooling.</summary>
        public static IEnumerable<KeyValuePair<string, string>> Table => En;

        public static int Count => En.Count;

        // ── Language ─────────────────────────────────────────────────────────────

        private static string _lang;

        /// <summary>Language the app falls back to when PlayerPrefs has no value.</summary>
        public static string DefaultLang =>
            Application.systemLanguage == SystemLanguage.Thai ? Thai : English;

        /// <summary>Current UI language ("th" / "en"); setting it persists to PlayerPrefs.</summary>
        public static string Lang
        {
            get
            {
                if (_lang == null) _lang = Normalize(PlayerPrefs.GetString(LangPrefKey, ""));
                return _lang;
            }
            set
            {
                _lang = Normalize(value);
                PlayerPrefs.SetString(LangPrefKey, _lang);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Drop the cached language so the next read hits PlayerPrefs again (tests).</summary>
        public static void ResetLangCache() => _lang = null;

        /// <summary>Clamp any input to a supported language code (unknown → system default).</summary>
        public static string Normalize(string lang)
        {
            if (string.IsNullOrEmpty(lang)) return DefaultLang;
            string l = lang.Trim().ToLowerInvariant();
            if (l == Thai) return Thai;
            if (l == English) return English;
            return DefaultLang;
        }

        // ── Translation ──────────────────────────────────────────────────────────

        /// <summary>Translate a Thai source string into the current language.</summary>
        public static string Tr(string source) => Tr(source, Lang);

        /// <summary>Pure overload (no PlayerPrefs) — used by tests and by explicit renders.</summary>
        public static string Tr(string source, string lang)
        {
            if (string.IsNullOrEmpty(source)) return source;
            if (lang != English) return source; // "th" (and anything unknown) = source as-is
            return En.TryGetValue(source, out string v) && !string.IsNullOrEmpty(v) ? v : source;
        }

        /// <summary>
        /// Render an ALREADY-DISPLAYED string in <paramref name="lang"/>, whichever
        /// language it is currently in. This is what makes "switch language and see it
        /// immediately" possible without rebuilding (or even knowing about) every
        /// screen: walk the live <c>Text</c> components and pass their content through
        /// this function.
        ///
        /// It first normalises the input back to its Thai source key (identity if it is
        /// already Thai, reverse lookup if it is a known English rendering) and then
        /// translates forward. Anything not in the table — map names, owner names,
        /// numbers, composed sentences — is returned untouched, so dynamic content is
        /// never mangled. The operation is idempotent: applying it twice is a no-op.
        /// </summary>
        public static string ToLang(string displayed, string lang)
        {
            if (string.IsNullOrEmpty(displayed)) return displayed;

            string source = displayed;
            if (!En.ContainsKey(source) && ThaiByEnglish().TryGetValue(source, out string th))
                source = th;

            return Tr(source, lang);
        }

        /// <summary>True when the string contains any character in the Thai Unicode block.</summary>
        public static bool ContainsThai(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c >= '\u0E00' && c <= '\u0E7F') return true;
            }
            return false;
        }
    }
}
