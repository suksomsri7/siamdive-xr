using DiveMap.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// G — pin media. The URL tests are the ones that matter: a pin's url comes out of a map
    /// document that, on a shared map, somebody else wrote.
    /// </summary>
    public class PinMediaTests
    {
        [Test]
        public void OnlyHttpAndHttpsAreFetched()
        {
            Assert.IsTrue(PinMedia.IsFetchable("https://cdn.siamdive.com/a.jpg"));
            Assert.IsTrue(PinMedia.IsFetchable("http://cdn.siamdive.com/a.jpg"));

            Assert.IsFalse(PinMedia.IsFetchable("file:///etc/passwd"), "would read the player's disk");
            Assert.IsFalse(PinMedia.IsFetchable("javascript:alert(1)"));
            Assert.IsFalse(PinMedia.IsFetchable("data:image/png;base64,AAAA"));
            Assert.IsFalse(PinMedia.IsFetchable("ftp://host/a.jpg"));
        }

        [Test]
        public void RelativeUrlsAreRefused()
        {
            // A relative path resolves against whatever base the player happens to use — which is
            // not something a map document gets to decide.
            Assert.IsFalse(PinMedia.IsFetchable("/uploads/a.jpg"));
            Assert.IsFalse(PinMedia.IsFetchable("a.jpg"));
        }

        [Test]
        public void EmptyAndRubbishAreRefused()
        {
            Assert.IsFalse(PinMedia.IsFetchable(null));
            Assert.IsFalse(PinMedia.IsFetchable(""));
            Assert.IsFalse(PinMedia.IsFetchable("   "));
            Assert.IsFalse(PinMedia.IsFetchable("not a url at all"));
        }

        [Test]
        public void SurroundingWhitespaceDoesNotDefeatTheCheck()
        {
            Assert.IsTrue(PinMedia.IsFetchable("  https://cdn.siamdive.com/a.jpg  "));
            Assert.AreEqual("https://cdn.siamdive.com/a.jpg",
                            PinMedia.Read(JArray.Parse("[{\"url\":\"  https://cdn.siamdive.com/a.jpg  \"}]"))[0].Url,
                            "…and the stored url is the trimmed one");
        }

        // ── reading a pin ────────────────────────────────────────────────────────

        [Test]
        public void UnfetchableEntriesAreDroppedBeforeTheyAreCounted()
        {
            var arr = JArray.Parse(@"[
                {""url"":""https://a/1.jpg""},
                {""url"":""javascript:alert(1)""},
                {""url"":""https://a/2.mp4"",""type"":""video""}
            ]");
            var items = PinMedia.Read(arr);
            Assert.AreEqual(2, items.Count, "the counter must not promise a slide that cannot load");
            Assert.IsFalse(items[0].IsVideo);
            Assert.IsTrue(items[1].IsVideo);
        }

        [Test]
        public void MediaWithNoTypeIsAnImage()
        {
            var items = PinMedia.Read(JArray.Parse("[{\"url\":\"https://a/1.jpg\"}]"));
            Assert.AreEqual(PinMedia.KindImage, items[0].Kind);
            Assert.IsFalse(items[0].IsVideo);
        }

        [Test]
        public void AMissingOrOddMediaListIsJustEmpty()
        {
            Assert.AreEqual(0, PinMedia.Read(null).Count);
            Assert.AreEqual(0, PinMedia.Read(JArray.Parse("[]")).Count);
            Assert.AreEqual(0, PinMedia.Read(JArray.Parse("[1,2,\"three\"]")).Count);
            Assert.AreEqual(0, PinMedia.Read(JArray.Parse("[{\"no\":\"url\"}]")).Count);
        }

        // ── paging ───────────────────────────────────────────────────────────────

        [Test]
        public void PreviousFromTheFirstSlideWrapsToTheLast()
        {
            Assert.AreEqual(4, PinMedia.Wrap(-1, 5));
            Assert.AreEqual(0, PinMedia.Wrap(5, 5));
            Assert.AreEqual(1, PinMedia.Wrap(6, 5));
            Assert.AreEqual(3, PinMedia.Wrap(-2, 5));
        }

        [Test]
        public void WrappingAnEmptyPinCannotDivideByZero()
        {
            Assert.AreEqual(0, PinMedia.Wrap(3, 0));
            Assert.AreEqual(0, PinMedia.Wrap(-3, 0));
        }

        [Test]
        public void TheCounterReadsLikeTheWebs()
        {
            Assert.AreEqual("1/5", PinMedia.Counter(0, 5));
            Assert.AreEqual("5/5", PinMedia.Counter(4, 5));
            Assert.AreEqual("5/5", PinMedia.Counter(-1, 5), "wrapped, not clamped");
            Assert.AreEqual("0/0", PinMedia.Counter(0, 0), "an empty pin says so");
        }

        [Test]
        public void MarkerGeometryMatchesTheWeb()
        {
            Assert.AreEqual(6.0, PinMedia.MarkerLift, 1e-9, "web: p.y + 6");
            Assert.AreEqual(9.0, PinMedia.MarkerSize, 1e-9, "web: sprite.scale 9");
        }
    }
}
