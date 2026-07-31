using System.Collections.Generic;
using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// Unit tests for the ❤️ store's codec.
    ///
    /// Why this matters more than it looks: the react endpoint is a bare counter with no
    /// per-device table ("1-per-device is enforced client-side" — its own route comment), so
    /// this CSV is the ONLY thing standing between a double tap and an inflated like count.
    /// A codec that loses or duplicates ids silently un-likes maps or double-counts them.
    /// </summary>
    public class LikedMapsTests
    {
        [Test]
        public void Decode_EmptyInputsGiveAnEmptySet()
        {
            Assert.AreEqual(0, LikedMaps.Decode(null).Count);
            Assert.AreEqual(0, LikedMaps.Decode("").Count);
            Assert.AreEqual(0, LikedMaps.Decode(",,, ,").Count, "blank fields are not ids");
        }

        [Test]
        public void Decode_TrimsAndDeduplicates()
        {
            HashSet<string> set = LikedMaps.Decode(" wl6zwxh1tdgn , w63m4h7u4vi5,wl6zwxh1tdgn ");
            Assert.AreEqual(2, set.Count);
            Assert.IsTrue(set.Contains("wl6zwxh1tdgn"));
            Assert.IsTrue(set.Contains("w63m4h7u4vi5"));
        }

        [Test]
        public void Decode_IsCaseSensitive()
        {
            // shortIds are case-sensitive on the server; folding case here would let a
            // wrong-case id read as "already liked".
            Assert.AreEqual(2, LikedMaps.Decode("Abc,abc").Count);
        }

        [Test]
        public void Encode_RoundTrips()
        {
            const string csv = "wl6zwxh1tdgn,w63m4h7u4vi5,874ti6ignvp9";
            Assert.AreEqual(3, LikedMaps.Decode(LikedMaps.Encode(LikedMaps.Decode(csv))).Count);
        }

        [Test]
        public void Encode_DropsBlanksNullsAndDuplicates()
        {
            Assert.AreEqual("a,b", LikedMaps.Encode(new[] { "a", "", null, " ", "b", "a" }));
            Assert.AreEqual("", LikedMaps.Encode(null));
        }

        [Test]
        public void Encode_RefusesIdsContainingTheSeparator()
        {
            // "a,b" stored raw would come back as two likes on the next read.
            Assert.AreEqual("ok", LikedMaps.Encode(new[] { "a,b", "ok" }));
        }

        [Test]
        public void Toggle_AddsThenRemoves()
        {
            string csv = LikedMaps.Toggle("", "wl6zwxh1tdgn", true);
            Assert.IsTrue(LikedMaps.Decode(csv).Contains("wl6zwxh1tdgn"));

            csv = LikedMaps.Toggle(csv, "wl6zwxh1tdgn", false);
            Assert.IsFalse(LikedMaps.Decode(csv).Contains("wl6zwxh1tdgn"));
        }

        [Test]
        public void Toggle_OnTwiceStoresOneEntry()
        {
            string csv = LikedMaps.Toggle(LikedMaps.Toggle("", "abc", true), "abc", true);
            Assert.AreEqual(1, LikedMaps.Decode(csv).Count, "a re-tap must not double the stored id");
        }

        [Test]
        public void Toggle_OffOnSomethingNeverLikedIsANoOp()
        {
            Assert.AreEqual(1, LikedMaps.Decode(LikedMaps.Toggle("abc", "zzz", false)).Count);
        }

        [Test]
        public void Toggle_KeepsTheOtherEntries()
        {
            string csv = LikedMaps.Toggle("a,b,c", "b", false);
            HashSet<string> set = LikedMaps.Decode(csv);
            Assert.AreEqual(2, set.Count);
            Assert.IsTrue(set.Contains("a"));
            Assert.IsTrue(set.Contains("c"));
        }

        [Test]
        public void Toggle_IgnoresEmptyIds()
        {
            Assert.AreEqual("a", LikedMaps.Toggle("a", "", true));
            Assert.AreEqual("a", LikedMaps.Toggle("a", null, true));
        }
    }
}
