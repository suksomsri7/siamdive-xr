using System.Collections;
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

            // WO-L: the web's guard is `isBuyable(a) && !_isAdmin`, not `isBuyable(a)`. Charging
            // the admin was a real difference, not a nicety — the official game worlds are built
            // on that account, and a whale shark placed there is set dressing, so the app was
            // asking its own author for 14,000 coins they are shown as having infinitely many of.
            if (Shop.ShouldCharge(assetId, Account.IsAdmin))
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

            // Local first, always. The server write can fail (no rights, no signal) and the
            // player has already been charged — the on-device copy is what guarantees they keep
            // what they paid for.
            ShopStock.Add(boot.CurrentMapId, item);
            Debug.Log($"[Shop] released {assetId} at ({x:F0},{y:F0},{z:F0}) on map {boot.CurrentMapId}");

            boot.StartCoroutine(SaveThenReload(boot, item));
            return true;
        }

        /// <summary>
        /// Try to make the placement permanent, then rebuild the map.
        ///
        /// This is the difference between "you can see your fish" and "everyone can": the web
        /// autosaves into the map, this app could only keep purchases on the device (ShopStock's
        /// own comment records that as a limitation). Now it attempts the real write and falls
        /// back to the device copy — which is the correct outcome on an admin world map, where
        /// editPolicy is "none" and a 403 is the server working as intended, not an error.
        /// </summary>
        private static IEnumerator SaveThenReload(AppBoot boot, JObject item)
        {
            SceneData scene = boot.CurrentScene;
            bool saved = false;

            // Don't spend a round trip to be told 403: the GET already said whether this account
            // may write here. Most maps a player dives into are admin worlds (editPolicy "none").
            if (!boot.CanEditCurrent)
            {
                Toast.ShowTr("แมพนี้แก้ไม่ได้ — เก็บไว้ในเครื่องนี้แทน");
            }
            else if (scene != null && scene.Root["items"] is JArray items)
            {
                // The scene already carries this item (ShopStock injected it on the last load, or
                // Inject will on the next); add it here too so the array we send is complete.
                ShopStock.Inject(scene, new[] { item });

                MapSaveClient.Result result = default;
                yield return MapSaveClient.SaveItems(boot.CurrentMapId, items, boot.CurrentRev,
                                                     r => result = r);
                saved = result.Ok;

                if (saved)
                {
                    // It lives in the map now; a second copy on the device would show up twice
                    // for this player and nobody else.
                    ShopStock.Remove(boot.CurrentMapId, (string)item["id"]);
                    Toast.ShowTr("บันทึกลงแมพแล้ว");
                }
                else if (result.Conflict) Toast.ShowTr("มีคนแก้แมพนี้ก่อน — เก็บไว้ในเครื่องนี้แทน");
                else if (result.Forbidden) Toast.ShowTr("แมพนี้แก้ไม่ได้ — เก็บไว้ในเครื่องนี้แทน");
                else Toast.ShowTr("บันทึกไม่สำเร็จ — เก็บไว้ในเครื่องนี้แทน");
            }
            else
            {
                Toast.ShowTr("ปล่อยลงแมพแล้ว — กำลังโหลดใหม่");
            }

            Debug.Log($"[Shop] placement saved to server={saved}");
            if (ModeManager.Instance != null) ModeManager.Instance.Exit();
            boot.ReloadCurrentMap();
        }
    }
}
