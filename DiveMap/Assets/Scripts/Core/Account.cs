using System;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DiveMap.Core
{
    /// <summary>
    /// Who is signed in on this install, and the rules the server will enforce anyway.
    ///
    /// Validating locally is not about trusting the client — the routes re-check everything.
    /// It is about not burning an OTP email on a typo, and about telling the player what is
    /// wrong before a round trip. Every rule below is copied from the route that owns it:
    /// <code>
    ///   set-username/route.ts:9   const n = String(raw).trim().replace(/\s+/g,' ');
    ///                             if(!/^[฀-๿a-zA-Z0-9 _]{3,20}$/.test(n)) return null;
    ///   set-username/route.ts:6   RESERVED = ["admin","siamdive","system"]
    ///   email/verify/route.ts     6-digit code
    /// </code>
    ///
    /// The signed-in identity is cached in PlayerPrefs (the RN app's <c>sd_auth</c>) so the hub
    /// can label cards "by You" before <c>/api/account/me</c> answers — but the SERVER is the
    /// authority: <see cref="Apply"/> overwrites the cache with whatever /me says, which is how
    /// a stale cache heals itself after a logout on another device.
    /// </summary>
    public static class Account
    {
        public const string EmailKey = "sd_auth_email";
        public const string NameKey = "sd_auth_name";
        public const string ScopeKey = "sd_acct_scope";

        public const int OtpDigits = 6;
        public const int NameMin = 3;
        public const int NameMax = 20;

        /// <summary>The admin address; its sign-in takes a passcode instead of an OTP.</summary>
        public const string AdminEmail = "admin@siamdive.com";

        private static readonly string[] Reserved = { "admin", "siamdive", "system" };

        // Thai block U+0E00–U+0E7F, Latin letters, digits, space, underscore — the route's regex.
        // Written as escapes, not literal Thai: this pattern has to survive every editor and
        // encoding the file passes through, and a mangled character class would silently reject
        // every Thai username.
        private static readonly Regex NameOk =
            new Regex("^[\u0E00-\u0E7Fa-zA-Z0-9 _]{3,20}$", RegexOptions.Compiled);

        // Deliberately loose: the server (and the mail provider) decide what is deliverable.
        // This only catches "no @", "no dot", "spaces" — the typos worth a message not a round trip.
        private static readonly Regex EmailOk =
            new Regex(@"^[^@\s]+@[^@\s.]+(\.[^@\s.]+)+$", RegexOptions.Compiled);

        // ── pure rules ───────────────────────────────────────────────────────────

        public static bool IsValidEmail(string email) =>
            !string.IsNullOrWhiteSpace(email) && EmailOk.IsMatch(email.Trim());

        public static bool IsAdminEmail(string email) =>
            !string.IsNullOrWhiteSpace(email) &&
            string.Equals(email.Trim(), AdminEmail, StringComparison.OrdinalIgnoreCase);

        /// <summary>Trim + collapse runs of whitespace, exactly like the route's cleanName.</summary>
        public static string CleanName(string raw)
        {
            if (raw == null) return "";
            return Regex.Replace(raw.Trim(), @"\s+", " ");
        }

        /// <summary>Why a username would be rejected, or null when it is fine.</summary>
        public static string NameError(string raw)
        {
            string n = CleanName(raw);
            if (!NameOk.IsMatch(n)) return "ชื่อสั้นไป (3-20 ตัว)";
            foreach (string r in Reserved)
                if (string.Equals(n, r, StringComparison.OrdinalIgnoreCase)) return "ชื่อนี้สงวนไว้";
            return null;
        }

        public static bool IsValidName(string raw) => NameError(raw) == null;

        /// <summary>An OTP is exactly six digits; anything else is not worth posting.</summary>
        public static bool IsValidOtp(string code)
        {
            if (code == null) return false;
            string c = code.Trim();
            if (c.Length != OtpDigits) return false;
            for (int i = 0; i < c.Length; i++) if (c[i] < '0' || c[i] > '9') return false;
            return true;
        }

        /// <summary>The letter shown in the round account button (RN: first char, upper-cased).</summary>
        public static string Initial(string name, string email)
        {
            string s = !string.IsNullOrWhiteSpace(name) ? name.Trim()
                     : !string.IsNullOrWhiteSpace(email) ? email.Trim()
                     : "?";
            return char.ToUpperInvariant(s[0]).ToString();
        }

        // ── cached identity ──────────────────────────────────────────────────────

        public static bool IsSignedIn => !string.IsNullOrEmpty(Email);
        public static string Email => PlayerPrefs.GetString(EmailKey, "");
        public static string Name => PlayerPrefs.GetString(NameKey, "");
        public static bool IsAdmin => IsAdminEmail(Email);

        /// <summary>
        /// Record what the server said. Returns true when the ACCOUNT changed (including a
        /// logout), which is the caller's cue to drop anything scoped to the old one — the RN
        /// app's syncAcctScope, and the reason another account's maps never show as "by You".
        /// </summary>
        public static bool Apply(string email, string name)
        {
            string next = (email ?? "").Trim();
            string prev = Email;

            if (string.IsNullOrEmpty(next))
            {
                PlayerPrefs.DeleteKey(EmailKey);
                PlayerPrefs.DeleteKey(NameKey);
            }
            else
            {
                PlayerPrefs.SetString(EmailKey, next);
                PlayerPrefs.SetString(NameKey, name ?? "");
            }
            PlayerPrefs.Save();

            return !string.Equals(prev, next, StringComparison.OrdinalIgnoreCase);
        }

        public static void SignOut() => Apply(null, null);
    }
}
