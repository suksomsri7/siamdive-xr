using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// The dive-site GET URL (WO-MERGE P1d).
    ///
    /// One string concatenation, and it is tested because of what it cost: the app told the OWNER
    /// of a map "This map is not editable", in both products, for as long as this URL has existed.
    /// <c>canEdit</c> is computed server-side from the caller's device→account link, so a request
    /// with no <c>deviceId</c> can only ever come back <c>canEdit:false</c> — there was no
    /// permission bug to find, and nothing in any log said the request was under-specified.
    ///
    /// The web's call is the contract (builder.html:3541), verified against prod:
    ///   GET /api/dive-sites/{shortId}?deviceId={encodeURIComponent(deviceId)}
    /// and it sends nothing else — no token, no header, no email.
    /// </summary>
    public class SiteRequestTests
    {
        private const string Base = "https://maps.siamdive.com";

        [Test]
        public void TheDeviceIdIsOnTheUrl()
        {
            // The whole point. If this assertion is ever "simplified" away, editing breaks again
            // and the symptom appears three layers from here.
            Assert.AreEqual(Base + "/api/dive-sites/wl6zwxh1tdgn?deviceId=dev-1",
                            SiteRequest.Url(Base, "wl6zwxh1tdgn", "dev-1"));
        }

        [Test]
        public void NoDeviceId_StillProducesAValidAnonymousUrl()
        {
            // Somebody opening a public map before anything has identified them. The server answers
            // this perfectly well with canEdit:false — what must NOT happen is a dangling "?" or
            // the string "null" arriving as a device id.
            string url = SiteRequest.Url(Base, "abc", null);
            Assert.AreEqual(Base + "/api/dive-sites/abc", url);
            Assert.IsFalse(url.Contains("?"));
            Assert.IsFalse(url.Contains("null"));

            Assert.AreEqual(Base + "/api/dive-sites/abc", SiteRequest.Url(Base, "abc", ""));
        }

        [Test]
        public void ATrailingSlashOnTheBaseDoesNotDoubleUp()
        {
            Assert.AreEqual(Base + "/api/dive-sites/abc?deviceId=d",
                            SiteRequest.Url(Base + "/", "abc", "d"));
        }

        [Test]
        public void TheDeviceIdIsPercentEncoded()
        {
            // The host injects this string; it is not ours to trust. A space, an ampersand or a
            // '+' passed through raw would either truncate the query or silently change the id the
            // server looks up — which reads as "the wrong account" rather than "a bad URL".
            Assert.AreEqual(Base + "/api/dive-sites/abc?deviceId=a%20b",
                            SiteRequest.Url(Base, "abc", "a b"));
            Assert.AreEqual(Base + "/api/dive-sites/abc?deviceId=a%26b%3Dc",
                            SiteRequest.Url(Base, "abc", "a&b=c"));
            // EscapeDataString, not form encoding: '+' must survive as %2B and not become a space.
            Assert.AreEqual(Base + "/api/dive-sites/abc?deviceId=a%2Bb",
                            SiteRequest.Url(Base, "abc", "a+b"));
        }

        [Test]
        public void TheShortIdIsEncodedToo()
        {
            Assert.AreEqual(Base + "/api/dive-sites/a%2Fb", SiteRequest.Url(Base, "a/b", null));
        }

        [Test]
        public void NothingElseIsSent()
        {
            // 🔴 Pinned as a NEGATIVE. The web used to append &email= and it was removed as an
            // editPolicy:'some' bypass — the server resolves the caller's email from the device
            // link and ignores whatever the client claims. Re-adding it here would look helpful.
            string url = SiteRequest.Url(Base, "abc", "dev-1");
            Assert.IsFalse(url.Contains("email"));
            Assert.IsFalse(url.Contains("token"));
            Assert.AreEqual(1, url.Split('?').Length - 1, "exactly one query string");
            Assert.IsFalse(url.Contains("&"), "exactly one parameter");
        }

        [Test]
        public void RealDeviceIdShapesSurviveUntouched()
        {
            // iOS identifierForVendor (upper-case hex with dashes) and the app's own GUID "N"
            // fallback. Neither should be rewritten by the escaper.
            const string vendor = "1B2C3D4E-5F60-7182-93A4-B5C6D7E8F901";
            Assert.AreEqual(Base + "/api/dive-sites/x?deviceId=" + vendor,
                            SiteRequest.Url(Base, "x", vendor));

            const string guidN = "9f8e7d6c5b4a39281706f5e4d3c2b1a0";
            Assert.AreEqual(Base + "/api/dive-sites/x?deviceId=" + guidN,
                            SiteRequest.Url(Base, "x", guidN));
        }
    }
}
