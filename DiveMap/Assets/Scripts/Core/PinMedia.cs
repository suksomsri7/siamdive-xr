using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace DiveMap.Core
{
    /// <summary>
    /// G — the photos and clips a diver pinned to a spot on the map (the web's <c>pins[].media</c>,
    /// <c>renderPin()</c> at builder.html:2890).
    ///
    /// The URL check is the important part of this class and it is not cosmetic. The web carries a
    /// comment explaining what it is defending against: a pin's media url is DATA FROM THE MAP
    /// DOCUMENT, so on a shared map it came from someone else. Their version concatenated it into
    /// innerHTML, which let a crafted url close the attribute and run script on their origin — and
    /// that origin holds <c>localStorage.sd_device</c>, the bearer secret for editing and deleting
    /// every map the viewer owns.
    ///
    /// A Unity player cannot be XSS'd, but the same url still goes into a web request, so the same
    /// gate applies: <b>http and https only</b>. <c>file://</c> would read the player's own disk and
    /// <c>javascript:</c>/<c>data:</c> have no business here either.
    /// </summary>
    public static class PinMedia
    {
        public const string KindVideo = "video";
        public const string KindImage = "image";

        public readonly struct Item
        {
            public readonly string Url;
            public readonly string Kind;
            public Item(string url, string kind) { Url = url; Kind = kind; }
            public bool IsVideo => string.Equals(Kind, KindVideo, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Is this url safe to fetch? Only absolute http/https. Everything else — including a
        /// relative path, which would resolve against whatever base the player happens to use —
        /// is refused.
        /// </summary>
        public static bool IsFetchable(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri u)) return false;
            return u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps;
        }

        /// <summary>
        /// Read a pin's media list, dropping anything that cannot be fetched. Refusing here rather
        /// than at load time means the counter ("2 / 5") never promises a slide that will come up
        /// blank.
        /// </summary>
        public static List<Item> Read(JArray media)
        {
            var list = new List<Item>();
            if (media == null) return list;

            foreach (JToken t in media)
            {
                if (!(t is JObject o)) continue;
                string url = (string)o["url"];
                if (!IsFetchable(url)) continue;
                string kind = (string)o["type"];
                list.Add(new Item(url.Trim(), string.IsNullOrEmpty(kind) ? KindImage : kind));
            }
            return list;
        }

        /// <summary>
        /// Wrap an index into a list, the web's <c>(i % n + n) % n</c>. Works for negatives, so
        /// "previous" from the first slide lands on the last one.
        /// </summary>
        public static int Wrap(int index, int count)
        {
            if (count <= 0) return 0;
            int i = index % count;
            return i < 0 ? i + count : i;
        }

        /// <summary>"3 / 7" — or "0/0" when the pin is empty, exactly as the web labels it.</summary>
        public static string Counter(int index, int count)
            => count <= 0 ? "0/0" : (Wrap(index, count) + 1) + "/" + count;

        /// <summary>How far above the pinned point the marker floats (web: <c>p.y + 6</c>).</summary>
        public const double MarkerLift = 6.0;

        /// <summary>Marker size in world units (web sprite scale 9).</summary>
        public const double MarkerSize = 9.0;

        /// <summary>How close a tap must be, in world units, to count as hitting a marker.</summary>
        public const double TapRadius = 7.0;
    }
}
