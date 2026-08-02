using System;

namespace DiveMap.Core
{
    /// <summary>Which marine system, if any, drives a placed item.</summary>
    public enum MarineRoute
    {
        /// <summary>Scenery. Coral, rock, wreck, boat, warp gate — nothing animates it.</summary>
        None = 0,
        /// <summary>An instanced boid swarm (<c>FishSchoolSystem</c>).</summary>
        School = 1,
        /// <summary>One animal with a brain of its own (<c>WhaleController</c>).</summary>
        Solo = 2,
    }

    /// <summary>
    /// C6 phase 2 — which placed items are ANIMALS, and therefore which get a brain.
    ///
    /// 🔴 The bug this exists to end. <c>SceneBuilder</c> routed exactly two id prefixes:
    /// <c>school:</c>/<c>pod:</c> to the shoal system and <c>msh:</c> to the solo controller.
    /// Everything else fell through to the scenery path — which is where <c>losin:*</c> (39
    /// species), <c>mdl:*</c> (13), <c>glb_turtle_loggerhead</c>, <c>fish:*</c> and
    /// <c>turtle:*</c> live. Fifty-eight species of fish, shark, ray, octopus and turtle were
    /// loaded as furniture: correct mesh, correct texture, correct place, and absolutely still.
    /// They had a genome, a swim style and a temperament by then; nothing ever asked for them.
    ///
    /// The routing is by MANIFEST KIND rather than by id prefix, and that is the whole point —
    /// a prefix is a naming convention that the next batch of assets will not follow, whereas
    /// the kind is a property of the thing. <c>losin:hammerhead_shark</c> is MARINE_LIFE for the
    /// same reason <c>msh:whaleshark</c> is.
    ///
    /// Pure string classification, so "does a coral head get a swimming brain" is a unit test
    /// rather than something you find out by looking at a reef.
    /// </summary>
    public static class MarineRouting
    {
        // ── manifest kinds ───────────────────────────────────────────────────────
        public const string KindMarineLife = "MARINE_LIFE";
        public const string KindSchool     = "SCHOOL";
        public const string KindFish       = "FISH";
        public const string KindTurtle     = "TURTLE";

        /// <summary>
        /// How many solo animals on one map may run the predator/prey scan.
        ///
        /// 🔴 The scan is O(n²) per sense tick. Twenty animals is 400 distance tests every
        /// 0.7 s, which is free; two hundred is 40,000, which is not — and a map CAN have two
        /// hundred, because nothing stops an author dropping the same fish two hundred times.
        /// Past this many, the surplus animals still swim, still flee the diver and still have
        /// their species' own roaming range; they simply do not hunt each other. That is the
        /// right thing to drop: at the distance you can see two hundred animals at once, no one
        /// is reading which of them is stalking which.
        /// </summary>
        public const int SoloHuntBudget = 40;

        /// <summary>Is this manifest kind an animal — something that should be alive?</summary>
        public static bool IsAnimalKind(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return false;
            return Eq(kind, KindMarineLife) || Eq(kind, KindSchool)
                || Eq(kind, KindFish) || Eq(kind, KindTurtle);
        }

        /// <summary>True for the two id prefixes that mean "this placement is a whole shoal".</summary>
        public static bool IsSchoolId(string assetId)
            => Starts(assetId, "school:") || Starts(assetId, "pod:");

        /// <summary>
        /// What drives <paramref name="assetId"/>, whose manifest entry says
        /// <paramref name="kind"/>.
        ///
        /// Order matters:
        ///   • a <c>warp:</c> gate is built from primitives and is in no manifest — it must never
        ///     be mistaken for anything, whatever kind is passed alongside it.
        ///   • the school prefixes win over the kind, because a pod of orcas is SCHOOL-kind AND
        ///     an animal, and it wants the shoal system rather than eleven separate brains.
        ///   • an unknown kind routes to None. A map that names an asset this build has never
        ///     heard of gets a placeholder, not a swimming brain attached to nothing.
        /// </summary>
        public static MarineRoute For(string assetId, string kind)
        {
            if (Starts(assetId, "warp:")) return MarineRoute.None;
            if (IsSchoolId(assetId)) return MarineRoute.School;
            if (!IsAnimalKind(kind)) return MarineRoute.None;
            if (Eq(kind, KindSchool)) return MarineRoute.School;
            return MarineRoute.Solo;
        }

        /// <summary>
        /// Does this placement get its own <c>WhaleController</c>? The question SceneBuilder
        /// actually asks.
        /// </summary>
        public static bool IsSolo(string assetId, string kind)
            => For(assetId, kind) == MarineRoute.Solo;

        /// <summary>
        /// May the <paramref name="index"/>-th solo animal on this map run the hunt scan?
        /// See <see cref="SoloHuntBudget"/>.
        /// </summary>
        public static bool MayHunt(int index) => index >= 0 && index < SoloHuntBudget;

        /// <summary>
        /// A stable per-placement seed: the asset id folded with where the item was PUT.
        ///
        /// 🔴 Position, never a counter and never the clock. Five sharks dropped on one map must
        /// behave like five sharks rather than one shark drawn five times — but the same map
        /// reopened tomorrow must give the same five, or a QC screenshot means nothing and the
        /// author's reef rearranges itself every time they look at it. Quantised to a tenth of a
        /// unit so float drift in the save file cannot reseed an animal.
        /// </summary>
        public static uint PlacementSeed(string assetId, double x, double y, double z)
        {
            uint h = 2166136261u;
            string id = assetId ?? "";
            for (int i = 0; i < id.Length; i++)
            {
                h ^= char.ToLowerInvariant(id[i]);
                h *= 16777619u;
            }
            h ^= (uint)(int)Math.Round(x * 10.0); h *= 16777619u;
            h ^= (uint)(int)Math.Round(y * 10.0); h *= 16777619u;
            h ^= (uint)(int)Math.Round(z * 10.0); h *= 16777619u;
            return h == 0u ? 1u : h;
        }

        private static bool Eq(string a, string b)
            => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static bool Starts(string s, string p)
            => !string.IsNullOrEmpty(s) && s.StartsWith(p, StringComparison.OrdinalIgnoreCase);
    }
}
