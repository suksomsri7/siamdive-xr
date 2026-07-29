using System.Collections.Generic;
using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// Unit tests for UiStrings (WO-XR-05.1).
    ///
    /// The rules here are the same "latin gate" the SiamDive web/translation pipeline
    /// learned the hard way: a translation table is only useful if every key has a
    /// non-empty value AND no value silently leaks source-language characters. A Thai
    /// string sitting in the English column renders as Thai in an English UI and is
    /// invisible in code review, so it is asserted instead.
    /// </summary>
    public class UiStringsTests
    {
        [Test]
        public void Table_IsNotEmpty()
        {
            Assert.Greater(UiStrings.Count, 5, "the string table looks suspiciously small");
        }

        [Test]
        public void EveryKey_IsThaiSource()
        {
            foreach (KeyValuePair<string, string> kv in UiStrings.Table)
            {
                Assert.IsNotEmpty(kv.Key, "empty key in the string table");
                Assert.IsTrue(UiStrings.ContainsThai(kv.Key),
                    $"key '{kv.Key}' is not a Thai source string — keys ARE the Thai text");
            }
        }

        [Test]
        public void EveryEnglishValue_IsNonEmpty()
        {
            foreach (KeyValuePair<string, string> kv in UiStrings.Table)
                Assert.IsFalse(string.IsNullOrWhiteSpace(kv.Value),
                    $"key '{kv.Key}' has no English value");
        }

        [Test]
        public void EveryEnglishValue_PassesTheLatinGate()
        {
            foreach (KeyValuePair<string, string> kv in UiStrings.Table)
                Assert.IsFalse(UiStrings.ContainsThai(kv.Value),
                    $"English value for '{kv.Key}' still contains Thai: '{kv.Value}'");
        }

        [Test]
        public void NoDuplicateEnglishValues()
        {
            var seen = new HashSet<string>();
            foreach (KeyValuePair<string, string> kv in UiStrings.Table)
                Assert.IsTrue(seen.Add(kv.Value),
                    $"English value '{kv.Value}' is used by more than one key");
        }

        // ── Tr ───────────────────────────────────────────────────────────────────

        [Test]
        public void Tr_ThaiReturnsTheSourceUnchanged()
        {
            foreach (KeyValuePair<string, string> kv in UiStrings.Table)
                Assert.AreEqual(kv.Key, UiStrings.Tr(kv.Key, UiStrings.Thai));
        }

        [Test]
        public void Tr_EnglishReturnsTheTableValue()
        {
            Assert.AreEqual("Maps", UiStrings.Tr("รายการแมพ", UiStrings.English));
            Assert.AreEqual("Settings", UiStrings.Tr("ตั้งค่า", UiStrings.English));
            Assert.AreEqual("Retry", UiStrings.Tr("ลองใหม่", UiStrings.English));
        }

        [Test]
        public void Tr_UnknownKeyFallsBackToTheSource()
        {
            const string unknown = "ข้อความที่ยังไม่ได้แปล";
            Assert.AreEqual(unknown, UiStrings.Tr(unknown, UiStrings.English));
        }

        [Test]
        public void Tr_EmptyInputIsPassedThrough()
        {
            Assert.IsNull(UiStrings.Tr(null, UiStrings.English));
            Assert.AreEqual("", UiStrings.Tr("", UiStrings.English));
        }

        [Test]
        public void Tr_CoversTheInfoCardAndSettingsVocabulary()
        {
            // WO-XR-05.3 / 05.4 — every string the new screens render.
            string[] keys =
            {
                "ความลึก", "ม.", "ชนิด", "ไม่ทราบชนิด",
                "ภาษา", "ไทย", "คุณภาพกราฟิก", "คุณภาพสูง", "ประหยัดพลังงาน",
                "เวอร์ชันแอป", "เว็บไซต์", "เปิดเว็บไซต์",
            };

            foreach (string k in keys)
            {
                string en = UiStrings.Tr(k, UiStrings.English);
                Assert.AreNotEqual(k, en, $"'{k}' has no English rendering");
                Assert.IsFalse(UiStrings.ContainsThai(en), $"'{k}' → '{en}' still contains Thai");
            }
        }

        [Test]
        public void Tr_CoversTheAssetNamesTheInfoCardShows()
        {
            // Names come straight out of asset_manifest.json; these are the ones on the
            // demo map, i.e. exactly what the QC screenshot must be able to render in EN.
            Assert.AreEqual("HTMS Chang", UiStrings.Tr("HTMS ช้าง", UiStrings.English));
            Assert.AreEqual("Whale shark", UiStrings.Tr("ฉลามวาฬ", UiStrings.English));
        }

        // ── ToLang (live re-render) ──────────────────────────────────────────────

        [Test]
        public void ToLang_TranslatesThaiForward()
        {
            Assert.AreEqual("Settings", UiStrings.ToLang("ตั้งค่า", UiStrings.English));
            Assert.AreEqual("ตั้งค่า", UiStrings.ToLang("ตั้งค่า", UiStrings.Thai));
        }

        [Test]
        public void ToLang_TranslatesAnAlreadyEnglishStringBackToThai()
        {
            // This is the one that makes "switch back to Thai" work without a rebuild.
            Assert.AreEqual("ตั้งค่า", UiStrings.ToLang("Settings", UiStrings.Thai));
            Assert.AreEqual("รายการแมพ", UiStrings.ToLang("Maps", UiStrings.Thai));
        }

        [Test]
        public void ToLang_IsIdempotent()
        {
            foreach (KeyValuePair<string, string> kv in UiStrings.Table)
            {
                string once = UiStrings.ToLang(kv.Key, UiStrings.English);
                Assert.AreEqual(once, UiStrings.ToLang(once, UiStrings.English), kv.Key);

                string back = UiStrings.ToLang(once, UiStrings.Thai);
                Assert.AreEqual(kv.Key, back, kv.Key);
            }
        }

        [Test]
        public void ToLang_LeavesDynamicContentAlone()
        {
            // Map names, owner names and composed lines must survive untouched.
            Assert.AreEqual("Htms Chang", UiStrings.ToLang("Htms Chang", UiStrings.English));
            Assert.AreEqual("Htms Chang", UiStrings.ToLang("Htms Chang", UiStrings.Thai));
            Assert.AreEqual("ลึก 40.0 ม.", UiStrings.ToLang("ลึก 40.0 ม.", UiStrings.English));
            Assert.IsNull(UiStrings.ToLang(null, UiStrings.Thai));
            Assert.AreEqual("", UiStrings.ToLang("", UiStrings.English));
        }

        // ── language normalisation ───────────────────────────────────────────────

        [Test]
        public void Normalize_AcceptsSupportedCodesCaseInsensitively()
        {
            Assert.AreEqual(UiStrings.Thai, UiStrings.Normalize("th"));
            Assert.AreEqual(UiStrings.Thai, UiStrings.Normalize(" TH "));
            Assert.AreEqual(UiStrings.English, UiStrings.Normalize("en"));
            Assert.AreEqual(UiStrings.English, UiStrings.Normalize("EN"));
        }

        [Test]
        public void Normalize_UnknownFallsBackToASupportedCode()
        {
            string fallback = UiStrings.Normalize("kr");
            Assert.IsTrue(fallback == UiStrings.Thai || fallback == UiStrings.English, fallback);
            Assert.AreEqual(UiStrings.DefaultLang, fallback);
            Assert.AreEqual(UiStrings.DefaultLang, UiStrings.Normalize(null));
            Assert.AreEqual(UiStrings.DefaultLang, UiStrings.Normalize(""));
        }

        // ── ContainsThai ─────────────────────────────────────────────────────────

        [Test]
        public void ContainsThai_DetectsTheThaiBlockOnly()
        {
            Assert.IsTrue(UiStrings.ContainsThai("รายการแมพ"));
            Assert.IsTrue(UiStrings.ContainsThai("HTMS ช้าง"));
            Assert.IsFalse(UiStrings.ContainsThai("Maps"));
            Assert.IsFalse(UiStrings.ContainsThai("Loading…"));
            Assert.IsFalse(UiStrings.ContainsThai(""));
            Assert.IsFalse(UiStrings.ContainsThai(null));
        }
    }
}
