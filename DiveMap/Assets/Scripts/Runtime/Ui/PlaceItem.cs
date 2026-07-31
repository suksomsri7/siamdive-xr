using Newtonsoft.Json.Linq;
using UnityEngine;
using DiveMap.Core;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// The web's <c>tryPlace()</c> (builder.html:4298) — **placing an object IS the purchase**:
    /// <code>
    ///   function tryPlace(a){
    ///     if(isBuyable(a) &amp;&amp; !_isAdmin){
    ///       if(!navigator.onLine){ showToast('ซื้อสัตว์ต้องต่อเน็ต 📶'); return; }
    ///       const p=priceOf(a); if(coins&lt;p){ showToast('เหรียญไม่พอ — ต้องการ 🪙'+p); return; }
    ///       coinSpend(p); coinUI(); scheduleCoinSave();
    ///     }
    ///     addAsset(a);
    ///   }
    /// </code>
    /// Rocks, coral and wrecks are free scenery; animals and schools cost coins.
    ///
    /// This lives in one place on purpose. The palette and the older <c>openShop()</c> list are
    /// two doors into the SAME transaction, exactly as on the web — a second copy of "deduct,
    /// then spawn" is how the two drift apart and one of them starts giving whales away.
    /// </summary>
    public static class PlaceItem
    {
        /// <summary>
        /// Buy (if needed) and drop <paramref name="assetId"/> into the current map.
        /// Returns false when the player could not afford it or there is no map to drop into —
        /// the caller only needs to know whether to play its "no" animation.
        /// </summary>
        public static bool TryPlace(string assetId)
        {
            if (string.IsNullOrEmpty(assetId)) return false;

            if (Shop.IsBuyable(assetId))
            {
                // The web refuses offline because the coin balance is server-authoritative and a
                // stale local number would let the same coins be spent twice.
                if (Application.internetReachability == NetworkReachability.NotReachable)
                {
                    Toast.ShowTr("ซื้อสัตว์ต้องต่อเน็ต");
                    return false;
                }

                int before = TrashGameSystem.Coins;
                int after = Shop.Buy(before, assetId, out bool bought);
                if (!bought)
                {
                    Toast.Show(UiStrings.Tr("เหรียญไม่พอ — ต้องการ") + " " + Shop.PriceOf(assetId));
                    return false;
                }

                TrashGameSystem.Coins = after;
                WalletClient.Spend(before - after);   // queued, debounced, re-queued on failure
                CoinCounter.Show(after);
                AudioBank.PlaySfx("coin");
                Debug.Log($"[Shop] bought {assetId} for {before - after} → coins={after}");
            }

            return Release(assetId);
        }

        /// <summary>
        /// Put the object in the water: record it against this map, then rebuild the map so it is
        /// built by the same pipeline as every other item. The rebuild costs a few seconds and
        /// drops the player out of the tour, which is why the toast says so plainly rather than
        /// letting the screen go quiet on them.
        /// </summary>
        public static bool Release(string assetId)
        {
            var boot = Object.FindFirstObjectByType<AppBoot>();
            if (boot == null)
            {
                Toast.ShowTr("ซื้อแล้ว");
                Debug.LogWarning("[Shop] no AppBoot — the purchase is stored but cannot be placed now");
                return false;
            }

            Camera cam = Camera.main;
            Vector3 at = cam != null ? cam.transform.position : Vector3.zero;
            float yaw = cam != null
                ? Mathf.Atan2(cam.transform.forward.z, cam.transform.forward.x)
                : 0f;

            ShopStock.DropPoint(at.x, at.y, at.z, yaw, ShopStock.DropDistance,
                                out double x, out double y, out double z);

            // A stamp rather than a counter: two purchases in the same second must still get
            // different ids, or Inject's duplicate guard would drop the second one.
            long stamp = System.DateTime.UtcNow.Ticks;
            JObject item = ShopStock.MakeItem(assetId, x, y, z, yaw, 1.0, stamp);
            ShopStock.Add(boot.CurrentMapId, item);
            Debug.Log($"[Shop] released {assetId} at ({x:F0},{y:F0},{z:F0}) on map {boot.CurrentMapId}");

            Toast.ShowTr("ปล่อยลงแมพแล้ว — กำลังโหลดใหม่");
            if (ModeManager.Instance != null) ModeManager.Instance.Exit();
            boot.ReloadCurrentMap();
            return true;
        }
    }
}
