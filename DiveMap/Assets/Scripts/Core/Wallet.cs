using System;

namespace DiveMap.Core
{
    /// <summary>
    /// P3b — the purse's accounting, ported from the web (builder.html 4320-4342). The design is
    /// worth stating because it is what keeps coins from evaporating:
    ///
    ///   • the SERVER is authoritative for the balance, but the client sends DELTAS
    ///     (<c>earned</c>/<c>spent</c>), never a total — two devices then cannot overwrite each
    ///     other's earnings
    ///   • deltas that have not been acknowledged stay pending, and a reconcile re-applies them on
    ///     top of whatever the server says, so a reply that raced a pickup does not lose it
    ///   • a failed POST puts its deltas BACK in the pending pile rather than dropping them
    ///   • a player the server has never seen is seeded once with the starting balance
    ///
    /// Pure, so all of that is testable without a network.
    /// </summary>
    public static class Wallet
    {
        /// <summary>New players start here (the web's <c>coins=600</c>).</summary>
        public const int StartingCoins = TrashGame.StartingCoins;

        /// <summary>Debounce before a save goes out, so a combo does not fire six requests.</summary>
        public const float SaveDebounceSeconds = 4f;

        /// <summary>
        /// The balance to display given the server's number and what has not been acknowledged
        /// yet. Never negative.
        /// </summary>
        public static int Reconcile(int serverCoins, int pendingEarned, int pendingSpent)
        {
            long v = (long)serverCoins + Math.Max(0, pendingEarned) - Math.Max(0, pendingSpent);
            if (v < 0) v = 0;
            if (v > int.MaxValue) v = int.MaxValue;
            return (int)v;
        }

        /// <summary>Local balance after earning <paramref name="amount"/> (clamped at zero).</summary>
        public static int Earn(int coins, int amount)
        {
            if (amount <= 0) return coins;
            long v = (long)coins + amount;
            return v > int.MaxValue ? int.MaxValue : (int)v;
        }

        /// <summary>Local balance after spending — a purchase can never take you below zero.</summary>
        public static int Spend(int coins, int amount)
        {
            if (amount <= 0) return coins;
            int v = coins - amount;
            return v < 0 ? 0 : v;
        }

        /// <summary>Can this purchase go through?</summary>
        public static bool CanAfford(int coins, int price) => price >= 0 && coins >= price;

        /// <summary>Is there anything worth sending?</summary>
        public static bool HasPending(int pendingEarned, int pendingSpent)
            => pendingEarned > 0 || pendingSpent > 0;

        /// <summary>
        /// A server that returns no wallet means a player it has never seen: seed once with the
        /// local balance rather than starting them at zero.
        /// </summary>
        public static bool NeedsSeed(bool serverHasWallet) => !serverHasWallet;
    }
}
