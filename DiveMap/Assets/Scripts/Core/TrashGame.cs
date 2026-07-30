using System;

namespace DiveMap.Core
{
    /// <summary>
    /// P3 — the rules of the clean-up game, ported from the web (builder.html 4087-4140) and kept
    /// pure so the cadence, the scoring and the despawn timing are tested rather than felt out on
    /// a phone.
    ///
    /// The web's design, worth stating because it is what makes it a game and not a chore:
    ///   • only inside the tour — leaving it clears the field
    ///   • at most 30 pieces, one new piece every 5 s
    ///   • each falls from just under the surface at 28 u/s, sways, and settles on the sand
    ///   • it lingers 30 s after landing, blinking for the last 5, then gives up
    ///   • picking one up scores <c>points × (1 + height) × (1 + 0.1 × combo)</c>, so catching a
    ///     bag before it lands is worth double a bag on the bottom, and repeats of the same type
    ///     build a combo up to ×2
    ///   • coins are separate: three at a time, replaced every 60 s
    /// </summary>
    public static class TrashGame
    {
        // ── field ────────────────────────────────────────────────────────────────
        public const int MaxTrash = 30;
        public const float SpawnEvery = 5f;
        public const float CoinCycle = 60f;
        public const int CoinsPerCycle = 3;
        public const float FallSpeed = 28f;
        public const float LandOffset = 1.5f;      // above the seabed
        public const float SpawnBelowSurface = 2f;
        public const float LifeAfterLanding = 30f;
        public const float BlinkAfter = 25f;
        public const float CollectRadius = 11f;
        public const int MaxCombo = 10;
        public const int StartingCoins = 600;

        /// <summary>One kind of litter: what it is worth and how often it appears.</summary>
        public struct Kind
        {
            public string Key;
            public int Points;
            public int Weight;
            public Kind(string key, int points, int weight) { Key = key; Points = points; Weight = weight; }
        }

        /// <summary>The web's table (TRASH, builder.html:4089).</summary>
        public static readonly Kind[] Kinds =
        {
            new Kind("can", 2, 28),
            new Kind("bottle", 2, 24),
            new Kind("plastic", 3, 22),
            new Kind("tire", 5, 14),
            new Kind("net", 6, 12),
        };

        /// <summary>Total of the weights, for the weighted pick.</summary>
        public static int TotalWeight
        {
            get
            {
                int t = 0;
                for (int i = 0; i < Kinds.Length; i++) t += Kinds[i].Weight;
                return t;
            }
        }

        /// <summary>
        /// Weighted pick from <paramref name="roll"/> ∈ [0,1). Deterministic, so a test can assert
        /// the distribution and a replay can reproduce a field.
        /// </summary>
        public static Kind Pick(float roll)
        {
            if (roll < 0f) roll = 0f;
            if (roll >= 1f) roll = 0.999999f;
            int target = (int)(roll * TotalWeight);
            int acc = 0;
            for (int i = 0; i < Kinds.Length; i++)
            {
                acc += Kinds[i].Weight;
                if (target < acc) return Kinds[i];
            }
            return Kinds[Kinds.Length - 1];
        }

        /// <summary>Whether another piece may drop right now.</summary>
        public static bool ShouldSpawn(int liveTrash, float now, float lastSpawn)
            => liveTrash < MaxTrash && now - lastSpawn >= SpawnEvery;

        /// <summary>Whether the coin cycle has come round again.</summary>
        public static bool ShouldCycleCoins(float now, float lastCycle)
            => now - lastCycle >= CoinCycle;

        /// <summary>
        /// Score for a pickup. <paramref name="height01"/> is how far the piece still was from the
        /// sand (1 = just under the surface, 0 = landed), <paramref name="combo"/> the current
        /// run of the same kind, <paramref name="bonus"/> the coin's ×2.
        /// </summary>
        public static int Score(Kind kind, float height01, int combo, bool bonus = false)
        {
            if (height01 < 0f) height01 = 0f;
            if (height01 > 1f) height01 = 1f;
            if (combo < 0) combo = 0;
            if (combo > MaxCombo) combo = MaxCombo;
            double g = kind.Points * (1f + height01) * (1f + combo * 0.1f) * (bonus ? 2 : 1);
            return (int)Math.Round(g, MidpointRounding.AwayFromZero);
        }

        /// <summary>Combo after picking up <paramref name="kind"/> following <paramref name="lastKey"/>.</summary>
        public static int NextCombo(string lastKey, string kind, int combo)
            => lastKey == kind ? Math.Min(MaxCombo, combo + 1) : 0;

        /// <summary>How high above the sand a piece is, normalised for <see cref="Score"/>.</summary>
        public static float HeightFactor(float y, float floorY, float spawnY)
        {
            float span = spawnY - floorY;
            if (span < 1f) span = 1f;
            float h = (y - floorY) / span;
            return h < 0f ? 0f : (h > 1f ? 1f : h);
        }

        /// <summary>Has a landed piece expired?</summary>
        public static bool Expired(float ageSinceLanding) => ageSinceLanding > LifeAfterLanding;

        /// <summary>Should a landed piece be visible this instant (it blinks before it goes)?</summary>
        public static bool VisibleWhileFading(float ageSinceLanding, float now)
            => ageSinceLanding < BlinkAfter || ((int)(now * 5f) % 2 == 0);
    }
}
