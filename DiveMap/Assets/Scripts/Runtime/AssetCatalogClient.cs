using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using DiveMap.Core;

namespace DiveMap.Runtime
{
    /// <summary>
    /// WO-L item 8 — the network half of <see cref="AssetCatalog"/>: fetch the backoffice's
    /// asset rows once in a while so a card added there can reach an installed app.
    ///
    /// 🔴 Read <see cref="AssetCatalog"/>'s header first. Today this adds ZERO visible cards,
    /// because the endpoint only serves the procedural placeholders both products drop, and the
    /// 216 real modules are shipped inside the build on the web exactly as they are here. This
    /// class is the plumbing for the day that changes, and it is written so that day needs no
    /// code: the manifest is the base, the server is merged over it.
    ///
    /// Everything about it is deliberately unable to hurt the palette:
    ///   • it is warmed in the background at startup, never awaited by <c>Open</c>
    ///   • a failure, a timeout or a malformed body leaves <see cref="Live"/> empty, and an
    ///     empty <see cref="Live"/> merges to exactly the shipped manifest
    ///   • it is fetched at most once per <see cref="AssetCatalog.TtlSeconds"/> per process
    /// The alternative — blocking the sheet on a round trip — would trade a real feature (the
    /// palette opens instantly) for a hypothetical one.
    /// </summary>
    public static class AssetCatalogClient
    {
        private const int TimeoutSeconds = 8;

        private static readonly List<PaletteSource> Rows = new List<PaletteSource>();
        private static double _fetchedAt;
        private static bool _inFlight;

        /// <summary>Rows the server knew about, or empty. Never null.</summary>
        public static IReadOnlyList<PaletteSource> Live => Rows;

        /// <summary>How many rows the last successful fetch produced (QC log).</summary>
        public static int Count => Rows.Count;

        /// <summary>True while a fetched catalogue is still inside its TTL.</summary>
        public static bool IsFresh =>
            AssetCatalog.IsFresh(_fetchedAt, Time.realtimeSinceStartup);

        /// <summary>
        /// Kick a background refresh if the cache has gone stale. Safe to call on every map load
        /// and every palette open — a fetch already running, or a cache still fresh, is a no-op.
        /// </summary>
        public static void Warm(MonoBehaviour host, string baseUrl = MapApiClient.DefaultBaseUrl)
        {
            if (host == null || _inFlight || IsFresh) return;
            host.StartCoroutine(Fetch(baseUrl));
        }

        /// <summary>QC/tests — forget the cache so the next Warm really goes to the network.</summary>
        public static void Forget()
        {
            Rows.Clear();
            _fetchedAt = 0d;
        }

        private static IEnumerator Fetch(string baseUrl)
        {
            _inFlight = true;
            string url = AssetCatalog.Url(baseUrl);

            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                req.timeout = TimeoutSeconds;
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    // Not a Toast: the user did not ask for this and losing it costs them nothing
                    // they can see. A log line is enough to explain a stale palette later.
                    Debug.Log($"[Palette] catalogue refresh skipped ({(long)req.responseCode}) {req.error}");
                    _inFlight = false;
                    yield break;
                }

                string body = req.downloadHandler != null ? req.downloadHandler.text : null;
                List<PaletteSource> parsed = AssetCatalog.Parse(body);
                Rows.Clear();
                Rows.AddRange(parsed);
                _fetchedAt = Time.realtimeSinceStartup;
                Debug.Log($"[Palette] catalogue refreshed rows={Rows.Count} from {url}");
            }

            _inFlight = false;
        }
    }
}
