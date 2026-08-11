using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// WO-N item 4 — the precedence between the server's <c>admin</c> flag and the admin address.
    ///
    /// Worth its own file because the failure is silent and expensive in both directions: believe
    /// the address over the server and a demoted account keeps ∞ coins and the 🌀 Warp category;
    /// believe a MISSING field as "no" and the operator is charged 14,000 coins for an animal the
    /// web hands them free. The app and the web must reach the same verdict from the same
    /// evidence — `_isAdmin = !!j.admin` (builder.html:4374).
    /// </summary>
    public class AdminIdentityTests
    {
        private const string Admin = "admin@siamdive.com";

        [Test]
        public void ServerFlagWins_EvenAgainstTheAdminAddress()
        {
            // The whole point of the fix. The server also recognises an admin by account row
            // (ADMIN_ACCT), so the address is not the question it is answering.
            Assert.IsTrue(AdminIdentity.Resolve(true, "someone.else@example.com", Admin));
            Assert.IsFalse(AdminIdentity.Resolve(false, Admin, Admin),
                           "a server that says no must outrank an address that looks right");
        }

        [Test]
        public void EmailIsOnlyConsultedWhenTheServerSaidNothing()
        {
            // null = the response had no `admin` field at all (an older server, or a cached
            // identity written before we read it) — NOT the server answering "no".
            Assert.IsTrue(AdminIdentity.Resolve(null, Admin, Admin));
            Assert.IsTrue(AdminIdentity.Resolve(null, "  ADMIN@SiamDive.com  ", Admin),
                          "trimmed and case-insensitive, like the route");
            Assert.IsFalse(AdminIdentity.Resolve(null, "someone.else@example.com", Admin));
        }

        [Test]
        public void NobodyIsAdminByAccident()
        {
            foreach (string e in new[] { null, "", "   " })
            {
                Assert.IsFalse(AdminIdentity.Resolve(null, e, Admin), $"email: {e ?? "null"}");
                // …and a blank admin constant must not turn every signed-out device into an admin.
                Assert.IsFalse(AdminIdentity.Resolve(null, e, ""), $"email: {e ?? "null"}");
            }
            Assert.IsFalse(AdminIdentity.Resolve(null, Admin, null));
        }

        [Test]
        public void StoredTriState_SurvivesTheRoundTrip()
        {
            // PlayerPrefs has no nullable bool and "absent" carries meaning, so the three states
            // ride as an int. A collapse of unknown→false here is the expensive direction.
            Assert.AreEqual(true, AdminIdentity.FromStored(AdminIdentity.ToStored(true)));
            Assert.AreEqual(false, AdminIdentity.FromStored(AdminIdentity.ToStored(false)));
            Assert.IsNull(AdminIdentity.FromStored(AdminIdentity.ToStored(null)));
        }

        [Test]
        public void StoredTriState_AnythingUnrecognisedReadsAsUnknown()
        {
            // A key written by an older build, or garbage — must fall back to the email rule
            // rather than assert a verdict nobody gave.
            foreach (int junk in new[] { -99, 2, 7, int.MinValue, int.MaxValue })
                Assert.IsNull(AdminIdentity.FromStored(junk), junk.ToString());
        }
    }
}
