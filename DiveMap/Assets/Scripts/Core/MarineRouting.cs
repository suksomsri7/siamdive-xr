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
        /// The id prefix of the hand-made "hero" animals — the ones that load a real GLB and are
        /// driven by <c>WhaleController</c> straight from <c>SceneBuilder</c>'s first pass rather
        /// than being picked up by the C6 sweep afterwards.
        /// </summary>
        public const string HeroPrefix = "msh:";

        /// <summary>
        /// Does this placement take the hero path — its own GLB plus a <c>WhaleController</c>,
        /// decided before the ordinary scenery load?
        ///
        /// 🔴 The prefix is NOT sufficient on its own, and believing it was is the bug this
        /// method exists to end. <c>SceneBuilder</c> asked <c>assetId.StartsWith("msh:")</c> and
        /// nothing else, one pass BEFORE it ever looked the module up in the manifest — so the
        /// kind never got a vote. <c>msh:wreck_ship</c> ("เรือจม (สมจริง)", kind <c>WRECK</c>) is
        /// a sunken ship that happens to share the prefix with twenty-eight animals, and it was
        /// handed a swimming brain: it roamed the map, fled the diver, and <see cref="SwimStyle"/>
        /// bent its hull like a carangiform fish because no rule in the style table matches
        /// "wreck_ship" either. A user testing build 261 reported exactly that — "รูปปั้น/สิ่ง
        /// ก่อสร้างบางชิ้นขยับได้เอง".
        ///
        /// The prefix now only chooses WHICH animal path is taken; whether the thing is an animal
        /// at all is <see cref="For"/>'s decision, and that is made from the manifest kind.
        /// </summary>
        public static bool IsHeroSolo(string assetId, string kind)
            => Starts(assetId, HeroPrefix) && For(assetId, kind) == MarineRoute.Solo;

        /// <summary>
        /// Does anything at all animate this placement? The one question a reviewer asks of a
        /// statue, and the inverse of "is it furniture".
        /// </summary>
        public static bool IsAnimated(string assetId, string kind)
            => For(assetId, kind) != MarineRoute.None;

        /// <summary>
        /// What drives <paramref name="assetId"/>, whose manifest entry says
        /// <paramref name="kind"/>.
        ///
        /// Order matters:
        ///   • a <c>warp:</c> gate is built from primitives and is in no manifest — it must never
        ///     be mistaken for anything, whatever kind is passed alongside it.
        ///   • THE KIND GATE COMES BEFORE EVERY PREFIX. The web has exactly one test for "does
        ///     this move" and it is the kind: <c>assignBehavior()</c> returns immediately unless
        ///     the kind is in <c>SWIMMERS = ['MARINE_LIFE','SCHOOL','TURTLE','FISH']</c>
        ///     (builder.html:1766, 2019), and every downstream id regex in the file sits BEHIND
        ///     that return. An id prefix that can promote a non-animal kind to a moving thing is
        ///     therefore a divergence from the web by construction, whatever the prefix is.
        ///   • only once the thing is known to be an animal do the school prefixes get to choose
        ///     WHICH system drives it, because a pod of orcas is SCHOOL-kind AND an animal, and
        ///     it wants the shoal system rather than eleven separate brains.
        ///   • an unknown kind routes to None. A map that names an asset this build has never
        ///     heard of gets a placeholder, not a swimming brain attached to nothing.
        /// </summary>
        public static MarineRoute For(string assetId, string kind)
        {
            if (Starts(assetId, "warp:")) return MarineRoute.None;
            if (!IsAnimalKind(kind)) return MarineRoute.None;
            if (IsSchoolId(assetId)) return MarineRoute.School;
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
