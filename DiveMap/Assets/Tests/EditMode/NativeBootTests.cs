using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// The boot message from the native host (WO-MERGE P1).
    ///
    /// Worth pinning here rather than on CI because the failure this guards against is invisible
    /// from inside Unity: a payload the host sends and Unity misreads produces a 3D screen showing
    /// the WRONG dive site, or Unity's own map hub inside an app that already has one, and neither
    /// leaves an error anywhere. The parse is pure, so every one of those cases is a two-second
    /// answer on this machine instead of a 35-minute CI round and a screenshot.
    /// </summary>
    public class NativeBootTests
    {
        [SetUp]
        public void ClearState() => NativeBoot.Reset();

        [TearDown]
        public void RestoreState() => NativeBoot.Reset();

        // ── the message the RN screen actually sends ─────────────────────────────

        [Test]
        public void RealPayload_ReadsEveryField()
        {
            // Copied from the RN side's boot post, field for field.
            const string json = "{\"shortId\":\"299\",\"deviceId\":\"abc123\",\"lang\":\"th\",\"libraryMode\":1}";

            Assert.IsTrue(NativeBoot.TryParse(json, out NativeBootArgs a));
            Assert.AreEqual("299", a.ShortId);
            Assert.AreEqual("abc123", a.DeviceId);
            Assert.AreEqual("th", a.Lang);
            Assert.AreEqual(true, a.LibraryMode);
            Assert.AreEqual("", a.AuthToken);
        }

        [Test]
        public void UnknownFields_AreIgnoredNotFatal()
        {
            // The host will grow fields (authToken first). An older Unity build in someone's
            // pocket must keep booting when it meets one it has never heard of.
            const string json = "{\"shortId\":\"299\",\"libraryMode\":1,\"tomorrowsField\":{\"a\":[1,2]}}";

            Assert.IsTrue(NativeBoot.TryParse(json, out NativeBootArgs a));
            Assert.AreEqual("299", a.ShortId);
            Assert.AreEqual(true, a.LibraryMode);
        }

        [Test]
        public void AuthToken_IsCarriedEvenThoughNothingConsumesItYet()
        {
            Assert.IsTrue(NativeBoot.TryParse("{\"authToken\":\"tok_1\"}", out NativeBootArgs a));
            Assert.AreEqual("tok_1", a.AuthToken);
        }

        // ── junk must change nothing ─────────────────────────────────────────────

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("not json at all")]
        [TestCase("{\"shortId\":")]
        [TestCase("[1,2,3]")]
        [TestCase("299")]
        [TestCase("\"exit\"")]
        public void Malformed_IsRejectedWhole(string json)
        {
            // Half-applying a broken message is the one outcome with no way back: the map would
            // be gone and the flag that hides the hub might still be set.
            Assert.IsFalse(NativeBoot.TryParse(json, out NativeBootArgs a));
            Assert.AreEqual(null, a.ShortId);
            Assert.AreEqual(null, a.LibraryMode);
        }

        [Test]
        public void EmptyObject_ParsesAndSaysNothing()
        {
            Assert.IsTrue(NativeBoot.TryParse("{}", out NativeBootArgs a));
            Assert.AreEqual("", a.ShortId);
            Assert.AreEqual("", a.DeviceId);
            Assert.AreEqual("", a.Lang);
            Assert.AreEqual(null, a.LibraryMode, "absent is not the same as libraryMode:0");
        }

        // ── shapes a JavaScript caller might reasonably send ─────────────────────

        [Test]
        public void ShortId_AsNumber_ReadsTheSameAsAString()
        {
            // `{shortId: site.shortId}` where shortId came off a JSON API as a number.
            Assert.IsTrue(NativeBoot.TryParse("{\"shortId\":299}", out NativeBootArgs a));
            Assert.AreEqual("299", a.ShortId);
        }

        [TestCase("{\"libraryMode\":1}", true)]
        [TestCase("{\"libraryMode\":true}", true)]
        [TestCase("{\"libraryMode\":\"1\"}", true)]
        [TestCase("{\"libraryMode\":\"true\"}", true)]
        [TestCase("{\"libraryMode\":0}", false)]
        [TestCase("{\"libraryMode\":false}", false)]
        [TestCase("{\"libraryMode\":\"0\"}", false)]
        [TestCase("{\"libraryMode\":\"false\"}", false)]
        public void LibraryMode_AcceptsEveryShapeOfBoolean(string json, bool expected)
        {
            Assert.IsTrue(NativeBoot.TryParse(json, out NativeBootArgs a));
            Assert.AreEqual(expected, a.LibraryMode);
        }

        [Test]
        public void LibraryMode_Null_IsNoOpinion()
        {
            Assert.IsTrue(NativeBoot.TryParse("{\"libraryMode\":null}", out NativeBootArgs a));
            Assert.AreEqual(null, a.LibraryMode);
        }

        [Test]
        public void Whitespace_IsTrimmedOffIdentifiers()
        {
            Assert.IsTrue(NativeBoot.TryParse("{\"shortId\":\" 299 \",\"deviceId\":\" dev \"}",
                                              out NativeBootArgs a));
            Assert.AreEqual("299", a.ShortId);
            Assert.AreEqual("dev", a.DeviceId);
        }

        // ── language ─────────────────────────────────────────────────────────────

        [TestCase("th", "th")]
        [TestCase("en", "en")]
        [TestCase("TH", "th")]
        [TestCase(" En ", "en")]
        public void Lang_IsNormalised(string sent, string expected)
        {
            Assert.IsTrue(NativeBoot.TryParse("{\"lang\":\"" + sent + "\"}", out NativeBootArgs a));
            Assert.AreEqual(expected, a.Lang);
        }

        [TestCase("de")]
        [TestCase("th-TH")]
        [TestCase("zz")]
        public void Lang_Unsupported_LeavesTheChoiceAlone(string sent)
        {
            // "" means "the host said nothing useful", which the receiver reads as "do not touch
            // the current language". Forcing English on a Thai user because the phone reported a
            // locale nobody has strings for would be worse than ignoring it.
            Assert.IsTrue(NativeBoot.TryParse("{\"lang\":\"" + sent + "\"}", out NativeBootArgs a));
            Assert.AreEqual("", a.Lang);
        }

        // ── state, and the merge rule ────────────────────────────────────────────

        [Test]
        public void FreshState_LooksLikeTheStandaloneBuild()
        {
            Assert.IsFalse(NativeBoot.Received);
            Assert.IsFalse(NativeBoot.LibraryMode, "the standalone build must keep its own hub");
            Assert.AreEqual("", NativeBoot.HostDeviceId);
            Assert.AreEqual("", NativeBoot.AuthToken);
        }

        [Test]
        public void Adopt_PublishesTheFlagsTheAppReads()
        {
            NativeBoot.TryParse("{\"shortId\":\"299\",\"deviceId\":\"dev-1\",\"libraryMode\":1}",
                                out NativeBootArgs a);
            NativeBoot.Adopt(a);

            Assert.IsTrue(NativeBoot.Received);
            Assert.IsTrue(NativeBoot.LibraryMode);
            Assert.AreEqual("dev-1", NativeBoot.HostDeviceId);
            Assert.AreEqual("299", NativeBoot.Current.ShortId);
        }

        [Test]
        public void SecondMessage_MergesInsteadOfWiping()
        {
            // The wallet id arrives in the boot message; a token may arrive in a later one. If the
            // second message replaced the first, the player's coins would change identity halfway
            // through a session.
            NativeBoot.TryParse("{\"shortId\":\"299\",\"deviceId\":\"dev-1\",\"libraryMode\":1}", out NativeBootArgs first);
            NativeBoot.Adopt(first);

            NativeBoot.TryParse("{\"authToken\":\"tok_9\"}", out NativeBootArgs second);
            NativeBoot.Adopt(second);

            Assert.AreEqual("dev-1", NativeBoot.HostDeviceId, "deviceId survived a token-only message");
            Assert.AreEqual("299", NativeBoot.Current.ShortId);
            Assert.IsTrue(NativeBoot.LibraryMode, "library mode survived a message that did not mention it");
            Assert.AreEqual("tok_9", NativeBoot.AuthToken);
        }

        [Test]
        public void LibraryMode_CanBeTurnedOffExplicitly()
        {
            // Absence means "no opinion", but a host that says 0 must be obeyed — otherwise the
            // flag would be a one-way latch nobody could clear while testing on a device.
            NativeBoot.TryParse("{\"libraryMode\":1}", out NativeBootArgs on);
            NativeBoot.Adopt(on);
            Assert.IsTrue(NativeBoot.LibraryMode);

            NativeBoot.TryParse("{\"libraryMode\":0}", out NativeBootArgs off);
            NativeBoot.Adopt(off);
            Assert.IsFalse(NativeBoot.LibraryMode);
        }

        [Test]
        public void Reset_PutsTheStandaloneBuildBack()
        {
            NativeBoot.TryParse("{\"deviceId\":\"dev-1\",\"libraryMode\":1}", out NativeBootArgs a);
            NativeBoot.Adopt(a);
            NativeBoot.Reset();

            Assert.IsFalse(NativeBoot.Received);
            Assert.IsFalse(NativeBoot.LibraryMode);
            Assert.AreEqual("", NativeBoot.HostDeviceId);
        }

        // ── the contract with the other repo ─────────────────────────────────────

        [Test]
        public void TheAddressTheHostSendsTo_IsNotChangedByAccident()
        {
            // These three strings are duplicated in siamdive-rn. Changing one here without the
            // other does not fail anything — the message simply goes nowhere and the map never
            // switches — so the test is the only thing that will object.
            Assert.AreEqual("AppBoot", NativeBoot.ReceiverObjectName);
            Assert.AreEqual("OnNativeBoot", NativeBoot.ReceiverMethodName);
            Assert.AreEqual("exit", NativeBoot.ExitMessage);
        }
    }
}
