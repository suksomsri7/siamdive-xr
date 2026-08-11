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
        /// Put the object in the water — in place, now, with no round trip.
        ///
        /// 🔴 WO-N item 7. This used to end in <c>ReloadCurrentMap()</c>, which is
        /// <c>AppBoot.Retry()</c> — a full re-FETCH from the server that throws the in-memory
        /// scene away and redraws the last saved copy. That is what the user saw and reported as
        /// "เลือกของแล้วระบบรีเฟรชใหม่ โหลดใหม่ ทำงานไม่ได้จริง": pick a card, the map blanks and
        /// reloads for several seconds, and it also called <c>ModeManager.Exit()</c> so the
        /// builder was torn down under them. On a map the account cannot write to — which is most
        /// of them, since admin worlds are <c>editPolicy: "none"</c> — the save 403s first, so the
        /// reload was pure cost: seconds of black screen to end up where you started.
        ///
        /// The web does none of that. <c>tryPlace()</c> deducts coins and calls <c>addAsset(a)</c>,
        /// which builds the object into the live scene and sets <c>dirty=true</c>; the 1.3 s
        /// autosave tick writes it later, invisibly (builder.html:3391-3403). We now do exactly
        /// that: mutate the scene in memory, hand it to <see cref="MapEditor.RecordAndApply"/> —
        /// which pushes an undo snapshot, marks the map dirty for the same autosave every other
        /// edit uses, and rebuilds FROM MEMORY — and return. No fetch, no mode change, and the
        /// placement is undoable, which it never was before.
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
            // what they paid for. Keeping it even when the autosave later succeeds is safe:
            // ShopStock.Inject skips ids the scene already has (ShopStock.cs:79-107).
            ShopStock.Add(boot.CurrentMapId, item);

            SceneData scene = boot.CurrentScene;
            if (scene == null)
            {
                Toast.ShowTr("ซื้อแล้ว");
                Debug.LogWarning("[Shop] no scene in memory — stored on the device for the next load");
                return false;
            }

            // The web's addAsset(): it is in the world the moment you pick it. Inject creates the
            // items array if a brand-new map has none, and dedupes by id.
            ShopStock.Inject(scene, new[] { item });
            Debug.Log($"[Shop] released {assetId} at ({x:F0},{y:F0},{z:F0}) on map {boot.CurrentMapId} " +
                      $"canEdit={boot.CanEditCurrent}");

            if (boot.CanEditCurrent && scene.Root["items"] is JArray items)
            {
                // Undo snapshot + dirty + rebuild-from-memory, the same three things a move, a
                // delete and a recolour do. The PATCH is the autosave's job, not ours.
                MapEditor.RecordAndApply(items);
                Toast.ShowTr("วางลงแมพแล้ว");
            }
            else
            {
                // No write rights: the piece is theirs on this device only, and ShopStock.Inject
                // will put it back on every future load of this map. Still rebuild so they can
                // see it immediately — the old code made them wait for a reload to find out.
                boot.RebuildFromMemory();
                Toast.ShowTr("แมพนี้แก้ไม่ได้ — เก็บไว้ในเครื่องนี้แทน");
            }
            return true;
        }
    }
}
