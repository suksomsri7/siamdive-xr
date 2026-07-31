using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// Unit tests for the sign-in rules.
    ///
    /// The point is not to police the user — the routes re-validate everything. It is that the
    /// local rules must be the SAME rules, or the app either burns an OTP email on a typo it
    /// should have caught, or refuses a name the server would have accepted. Each assertion
    /// below quotes the route it mirrors.
    /// </summary>
    public class AccountTests
    {
        // ── username: set-username/route.ts cleanName ────────────────────────────
        //   const n = String(raw).trim().replace(/\s+/g,' ')
        //   /^[฀-๿a-zA-Z0-9 _]{3,20}$/

        [Test]
        public void CleanName_TrimsAndCollapsesWhitespace()
        {
            Assert.AreEqual("Tu Nakarin", Account.CleanName("  Tu    Nakarin "));
            Assert.AreEqual("a b", Account.CleanName("a\t\nb"));
            Assert.AreEqual("", Account.CleanName(null));
        }

        [Test]
        public void Name_AcceptsThaiLatinDigitsSpaceUnderscore()
        {
            Assert.IsTrue(Account.IsValidName("สมศรี"));
            Assert.IsTrue(Account.IsValidName("Tu Nakarin"));
            Assert.IsTrue(Account.IsValidName("diver_99"));
            Assert.IsTrue(Account.IsValidName("abc"), "3 characters is the floor");
            Assert.IsTrue(Account.IsValidName("12345678901234567890"), "20 is the ceiling");
        }

        [Test]
        public void Name_RejectsWhatTheRouteRejects()
        {
            Assert.IsFalse(Account.IsValidName("ab"), "too short");
            Assert.IsFalse(Account.IsValidName("123456789012345678901"), "21 characters");
            Assert.IsFalse(Account.IsValidName("bad-name"), "hyphen is not in the class");
            Assert.IsFalse(Account.IsValidName("emoji 🐟"));
            Assert.IsFalse(Account.IsValidName("   "));
            Assert.IsFalse(Account.IsValidName(null));
        }

        [Test]
        public void Name_LeadingAndTrailingSpacesDoNotCountTowardTheLimit()
        {
            // cleanName trims BEFORE the length test, so this is a valid 3-char name.
            Assert.IsTrue(Account.IsValidName("  abc  "));
        }

        [Test]
        public void Name_ReservedWordsAreRefusedCaseInsensitively()
        {
            foreach (string r in new[] { "admin", "Admin", "SIAMDIVE", "system" })
                Assert.AreEqual("ชื่อนี้สงวนไว้", Account.NameError(r), r + " is reserved");
        }

        [Test]
        public void NameError_IsNullWhenTheNameIsFine()
        {
            Assert.IsNull(Account.NameError("นักดำน้ำ 1"));
        }

        // ── email ────────────────────────────────────────────────────────────────

        [Test]
        public void Email_AcceptsOrdinaryAddresses()
        {
            Assert.IsTrue(Account.IsValidEmail("suksomsri@gmail.com"));
            Assert.IsTrue(Account.IsValidEmail(" a.b+tag@sub.example.co.th "));
        }

        [Test]
        public void Email_RejectsTheTypesWorthCatchingBeforeSendingAnOtp()
        {
            Assert.IsFalse(Account.IsValidEmail("nobody"));
            Assert.IsFalse(Account.IsValidEmail("no@domain"), "a bare host cannot receive mail");
            Assert.IsFalse(Account.IsValidEmail("two spaces@x.com"));
            Assert.IsFalse(Account.IsValidEmail("a@@b.com"));
            Assert.IsFalse(Account.IsValidEmail(""));
            Assert.IsFalse(Account.IsValidEmail(null));
        }

        [Test]
        public void AdminEmail_IsRecognisedRegardlessOfCase()
        {
            Assert.IsTrue(Account.IsAdminEmail("admin@siamdive.com"));
            Assert.IsTrue(Account.IsAdminEmail("  Admin@SiamDive.com "));
            Assert.IsFalse(Account.IsAdminEmail("admin@example.com"));
            Assert.IsFalse(Account.IsAdminEmail(null));
        }

        // ── OTP ──────────────────────────────────────────────────────────────────

        [Test]
        public void Otp_IsExactlySixDigits()
        {
            Assert.IsTrue(Account.IsValidOtp("123456"));
            Assert.IsTrue(Account.IsValidOtp(" 000000 "));
            Assert.IsFalse(Account.IsValidOtp("12345"));
            Assert.IsFalse(Account.IsValidOtp("1234567"));
            Assert.IsFalse(Account.IsValidOtp("12345a"));
            Assert.IsFalse(Account.IsValidOtp(""));
            Assert.IsFalse(Account.IsValidOtp(null));
        }

        // ── avatar initial ───────────────────────────────────────────────────────

        [Test]
        public void Initial_PrefersTheNameThenTheEmail()
        {
            Assert.AreEqual("T", Account.Initial("Tu Nakarin", "x@y.com"));
            Assert.AreEqual("X", Account.Initial("", "x@y.com"));
            Assert.AreEqual("?", Account.Initial(null, null));
            Assert.AreEqual("?", Account.Initial("   ", "  "));
        }

        [Test]
        public void Initial_HandlesThaiNames()
        {
            Assert.AreEqual("ส", Account.Initial("สมศรี", "x@y.com"),
                            "ToUpperInvariant leaves Thai alone rather than mangling it");
        }

        // ── every message the sign-in flow can show must be translated ───────────

        [Test]
        public void EverySignInMessage_HasAnEnglishRendering()
        {
            string[] shown =
            {
                "เข้าสู่ระบบ / สมัครสมาชิก", "ใส่อีเมลเพื่อรับรหัส OTP",
                "แมพและเหรียญในเครื่องนี้จะถูกผูกเข้าบัญชี",
                "ใส่รหัส 6 หลัก", "ส่งไปที่", "เข้าสู่ระบบแอดมิน", "ใส่ passcode แอดมิน",
                "ตั้งชื่อผู้ใช้", "3-20 ตัว · ไทย/อังกฤษ/เลข/เว้นวรรค/_", "ชื่อของคุณ",
                "ส่งรหัส", "ยืนยัน", "เสร็จ", "เข้าสู่ระบบ", "เข้าสู่ระบบแล้ว",
                "ออกจากระบบ", "ออกจากระบบแล้ว", "ลบบัญชี", "แตะอีกครั้งเพื่อลบบัญชีถาวร",
                "ลบบัญชีแล้ว", "ลบบัญชีไม่สำเร็จ", "(ยังไม่ตั้งชื่อ)",
                "อีเมลไม่ถูกต้อง", "เพิ่งส่งไป รอสักครู่", "รหัสผิด", "รหัสหมดอายุ",
                "ชื่อสั้นไป (3-20 ตัว)", "ชื่อนี้มีคนใช้แล้ว ลองชื่ออื่น", "ชื่อนี้สงวนไว้",
                "passcode ผิด", "เชื่อมต่อไม่ได้",
                "เก็บเข้า My Map", "เอาออกจาก My Map",
                "เก็บเข้า My Map แล้ว", "เอาออกจาก My Map แล้ว",
                "สร้างโดย คุณ",
            };
            foreach (string th in shown)
                Assert.AreNotEqual(th, UiStrings.Tr(th, UiStrings.English),
                                   $"'{th}' would render as Thai in an English UI");
        }
    }
}
