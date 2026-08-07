using System;
using System.Collections.Generic;
using System.Linq;

namespace DiveMap.Core
{
    /// <summary>
    /// Texture-quality ladder: pick, per map, the highest texture tier the device can hold.
    ///
    /// 🔴 WHY THIS EXISTS — build 282 shipped raw-PNG textures to every model and iOS killed the
    /// app inside two maps; build 298 crossed the same line on one map (Atlantis, 829 MB of
    /// texture VRAM) while a lighter map at 940 MB was fine; build 300 proved the converse — the
    /// same Atlantis at 561 MB loads. The file set was never the problem twice: the problem is
    /// that the app loaded whatever the manifest named, with no idea what the device could
    /// afford. This class is that missing idea.
    ///
    /// The CDN now carries each model in up to three ZERO-COOK tiers (master-byte PNG, resized
    /// only, never GPU-compressed — the user rejected ETC1S/UASTC/ASTC by eye on 291):
    ///   k1 ≤1024px ≈ 22 MB · k2 ≤2048px ≈ 89 MB · k4 = full master ≈ 224 MB (4-slot model)
    ///
    /// The decision is deliberately per MAP, not per install: T-13 holds 10 distinct assets and
    /// can afford k4 everywhere; Atlantis holds 27 and cannot. <c>Choose</c> is pure — the caller
    /// (SceneBuilder) hands in the map's laddered entries, a budget, and the bytes already spoken
    /// for by non-laddered assets — so EditMode tests pin every branch of the policy without a
    /// device in the room.
    ///
    /// Policy, in order:
    ///   1. Uniform base: the highest tier whose map-wide total fits the budget.
    ///   2. If even all-k1 does not fit, k1 anyway — it is the floor that build 300 proved safe,
    ///      and refusing to load a map is strictly worse than loading it soft.
    ///   3. Spend what remains upgrading individual assets, biggest k4 first — the 4096-texture
    ///      heroes are the models the diver swims up to; a rock never deserves the last 200 MB
    ///      more than a whale shark does.
    ///
    /// Budget numbers live in <see cref="BudgetBytes"/> and are the tunable part: they encode
    /// "what fraction of a phone's RAM is safely ours for map textures" and nothing else. They
    /// start conservative on purpose — the calibration points we own are one user's device
    /// (Atlantis 829 MB dead / 561 MB alive, Posidon 940 MB alive), which also says the texture
    /// total is not the whole story (item count and meshes ride on top). Tune them from device
    /// logs, one build at a time.
    /// </summary>
    public static class TexTiers
    {
        /// <summary>Tier indices, low to high. Used as array offsets everywhere below.</summary>
        public const int K1 = 0, K2 = 1, K4 = 2;

        public static readonly string[] TierNames = { "k1", "k2", "k4" };

        /// <summary>
        /// One laddered asset in the map being planned. Urls/Vram are indexed by
        /// <see cref="K1"/>/<see cref="K2"/>/<see cref="K4"/>; the manifest patcher writes all
        /// three (aliases resolved to the file that actually exists), so a module either has a
        /// complete ladder here or does not appear in the list at all.
        /// </summary>
        public struct Entry
        {
            public string Id;
            public string[] Urls;    // length 3
            public long[] Vram;      // length 3, bytes

            public bool Valid =>
                Urls != null && Vram != null && Urls.Length == 3 && Vram.Length == 3
                && !string.IsNullOrWhiteSpace(Urls[K1]) && !string.IsNullOrWhiteSpace(Urls[K2])
                && !string.IsNullOrWhiteSpace(Urls[K4])
                && Vram[K1] > 0 && Vram[K2] > 0 && Vram[K4] > 0;
        }

        /// <summary>What <see cref="Choose"/> decided, for the caller's one log line.</summary>
        public struct Plan
        {
            /// <summary>assetId → URL of the chosen tier. Empty when nothing was laddered.</summary>
            public Dictionary<string, string> Url;

            /// <summary>assetId → chosen tier index (for logs/tests).</summary>
            public Dictionary<string, int> Tier;

            public int BaseTier;
            public int Upgraded;
            public long TotalBytes;
            public bool OverBudget;   // true when even all-k1 exceeded the budget (loaded anyway)
        }

        /// <summary>
        /// Texture-VRAM budget for a device reporting <paramref name="sysMemMB"/> of system RAM
        /// (UnityEngine's <c>SystemInfo.systemMemorySize</c>, passed in so this stays testable).
        /// Deliberately a step table, not a formula — each row is a class of device we can name,
        /// and a row can be tuned after a device log proves it wrong without touching the rest.
        /// </summary>
        public static long BudgetBytes(int sysMemMB)
        {
            if (sysMemMB >= 7168) return 950L * MB;   // 8 GB+ — recent Pro phones, tablets
            if (sysMemMB >= 5120) return 750L * MB;   // 6 GB
            if (sysMemMB >= 3584) return 550L * MB;   // 4 GB — the calibration device class
            return 320L * MB;                          // 3 GB and the long Android tail
        }

        private const long MB = 1024L * 1024L;

        /// <summary>
        /// Fixed cost charged for an asset that ships no ladder (ETC1S/KTX2 file, school
        /// instancing set, the odd texture-less rock). 8 bpp on-device for a 2048² set ≈ 5.6 MB
        /// plus mips — 6 MB is close enough for a reservation, and being a reservation it only
        /// ever errs the safe way.
        /// </summary>
        public const long FixedBytesPerAsset = 6L * MB;

        /// <summary>
        /// Decide a tier per laddered asset so the map's texture total fits
        /// <paramref name="budgetBytes"/> after <paramref name="fixedBytes"/> (non-laddered
        /// assets) is reserved. Pure and deterministic; see the class remarks for the policy.
        /// </summary>
        public static Plan Choose(IReadOnlyList<Entry> entries, long budgetBytes, long fixedBytes)
        {
            var plan = new Plan
            {
                Url = new Dictionary<string, string>(StringComparer.Ordinal),
                Tier = new Dictionary<string, int>(StringComparer.Ordinal),
                BaseTier = K1,
            };
            List<Entry> live = entries?.Where(e => e.Valid).ToList() ?? new List<Entry>();
            if (live.Count == 0) return plan;

            long budget = budgetBytes - fixedBytes;

            // 1) uniform base — highest tier whose total fits
            int baseTier = K1;
            for (int t = K4; t >= K1; t--)
            {
                long total = 0;
                foreach (Entry e in live) total += e.Vram[t];
                if (total <= budget) { baseTier = t; break; }
                if (t == K1) plan.OverBudget = true;   // even the floor is over — take it anyway
            }

            long running = 0;
            foreach (Entry e in live)
            {
                plan.Tier[e.Id] = baseTier;
                running += e.Vram[baseTier];
            }

            // 2) spend the remainder, biggest hero first, one asset to its best fitting tier
            if (baseTier < K4 && !plan.OverBudget)
            {
                foreach (Entry e in live.OrderByDescending(x => x.Vram[K4])
                                        .ThenBy(x => x.Id, StringComparer.Ordinal))
                {
                    for (int t = K4; t > baseTier; t--)
                    {
                        long delta = e.Vram[t] - e.Vram[baseTier];
                        if (delta < 0) continue;               // alias quirk — never "upgrade" down
                        if (running + delta > budget) continue;
                        plan.Tier[e.Id] = t;
                        running += delta;
                        plan.Upgraded++;
                        break;
                    }
                }
            }

            foreach (Entry e in live) plan.Url[e.Id] = e.Urls[plan.Tier[e.Id]];
            plan.BaseTier = baseTier;
            plan.TotalBytes = running;
            return plan;
        }

        // ── The per-map plan the resolver consults ──────────────────────────────────────────
        //
        // SceneBuilder computes a Plan at the top of every map build and parks it here;
        // AssetManifest.ResolveUrl asks UrlFor on every lookup. A map build is single-threaded
        // over its own manifest, so a plain static swap is enough — and Clear() at the start of
        // the NEXT build means a stale plan can never outlive its map.

        private static Dictionary<string, string> _urls;

        public static void SetPlan(Plan plan) => _urls = plan.Url;

        public static void Clear() => _urls = null;

        /// <summary>Chosen URL for this asset, or null when the current map planned none.</summary>
        public static string UrlFor(string assetId)
        {
            if (_urls == null || string.IsNullOrEmpty(assetId)) return null;
            return _urls.TryGetValue(assetId, out string u) ? u : null;
        }
    }
}
