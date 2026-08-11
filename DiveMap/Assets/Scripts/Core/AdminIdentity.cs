using System;

namespace DiveMap.Core
{
    /// <summary>
    /// WO-N item 4 — who counts as the admin, and in what order the evidence is believed.
    ///
    /// 🔴 THE SERVER'S FLAG WINS. The web settles this in one line:
    /// <code>_isAdmin = !!j.admin;   // builder.html:4374</code>
    /// It never compares an address. Ours did — <c>IsAdminEmail(Email)</c> against the constant
    /// <c>admin@siamdive.com</c> — while <c>AccountClient</c> parsed the server's <c>admin</c>
    /// field, logged it, and threw it away (the bug this file exists to fix). The two agree only
    /// as long as the admin account keeps that exact address: the server also recognises an admin
    /// by account row (<c>ADMIN_ACCT=cmqrpkm6f…</c>), so an operator signed in as anything else
    /// got ∞ coins and the 🌀 Warp chip on the web and was charged 14,000 for a whale shark in
    /// the app. "Seamless with the web" has to mean the same source of truth, not the same
    /// outcome by coincidence.
    ///
    /// The email match survives ONLY as a fallback for a response that carries no <c>admin</c>
    /// key at all — an older server, or a cached identity written before this field was read.
    /// It is not the rule; it is what we do when the rule is unavailable.
    ///
    /// Pure and separate from <see cref="Account"/> (which needs PlayerPrefs) so the precedence
    /// is settled by a test on this machine rather than by a 35-minute CI round.
    /// </summary>
    public static class AdminIdentity
    {
        /// <summary>
        /// <paramref name="serverFlag"/> is null when the response had no <c>admin</c> field —
        /// which is NOT the same as the server saying "no". A server that says false must be
        /// believed over an address that looks right, or a demoted account keeps its ∞.
        /// </summary>
        public static bool Resolve(bool? serverFlag, string email, string adminEmail)
        {
            if (serverFlag.HasValue) return serverFlag.Value;
            return !string.IsNullOrWhiteSpace(email) &&
                   !string.IsNullOrWhiteSpace(adminEmail) &&
                   string.Equals(email.Trim(), adminEmail.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        // ── the tri-state, as it survives in PlayerPrefs ─────────────────────────
        // PlayerPrefs has no nullable bool, and "absent" carries meaning here, so the three
        // states are stored as an int rather than as a bool plus a second "known" key that
        // could get out of step with it.

        public const int Unknown = -1;
        public const int No = 0;
        public const int Yes = 1;

        public static int ToStored(bool? flag) => !flag.HasValue ? Unknown : (flag.Value ? Yes : No);

        /// <summary>Anything that is not a stored yes/no reads as "the server never told us".</summary>
        public static bool? FromStored(int stored)
        {
            if (stored == Yes) return true;
            if (stored == No) return false;
            return null;
        }
    }
}
