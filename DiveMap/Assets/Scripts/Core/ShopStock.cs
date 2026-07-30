using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace DiveMap.Core
{
    /// <summary>
    /// E5 — what a player has bought, and where they put it.
    ///
    /// The web writes purchases straight into the map (<c>addGLB</c> then <c>dirty=true</c>, saved
    /// by autosave). This app has no map-write API yet (PARITY section J is 0/8), so a purchase is
    /// kept on the device instead and injected into the scene every time that map loads. The
    /// player keeps what they paid for, and nothing is written to somebody else's map.
    ///
    /// Two consequences worth being straight about, both recorded in the toast the player sees:
    ///   • the animal is visible to you, not to other divers, until map saving lands
    ///   • it is stored per map id, so buying in one site does not populate another
    ///
    /// The stored form is the SCENE ITEM ITSELF (the same JSON the API returns), so restoring is
    /// a list append and the animal then goes through exactly the same build path as every other
    /// item — no second spawn route to keep in step with the first.
    /// </summary>
    public static class ShopStock
    {
        public const string PrefPrefix = "shop_stock_";

        /// <summary>The pref key holding one map's purchases.</summary>
        public static string KeyFor(string mapId) => PrefPrefix + (mapId ?? "");

        /// <summary>
        /// Build the scene item for a purchase. Kept pure so the shape is testable without a
        /// scene: id, assetId, position, rotation, scale — the fields SceneBuilder reads.
        /// </summary>
        public static JObject MakeItem(string assetId, double x, double y, double z,
                                       double yawRadians, double scale, long stamp)
        {
            var o = new JObject
            {
                ["id"] = "buy_" + stamp.ToString(),
                ["assetId"] = assetId ?? "",
            };
            o["p"] = new JArray(x, y, z);
            o["r"] = new JArray(0.0, yawRadians, 0.0);
            double s = scale > 0.0 ? scale : 1.0;
            o["s"] = new JArray(s, s, s);
            return o;
        }

        /// <summary>Parse a stored blob into items. A corrupt blob yields nothing, never throws.</summary>
        public static List<JObject> Parse(string json)
        {
            var list = new List<JObject>();
            if (string.IsNullOrEmpty(json)) return list;
            try
            {
                if (JToken.Parse(json) is JArray arr)
                    foreach (JToken t in arr)
                        if (t is JObject o && !string.IsNullOrEmpty((string)o["assetId"]))
                            list.Add(o);
            }
            catch (Exception)
            {
                // A player whose stock blob got mangled loses the display of their purchases,
                // not the app. Returning empty is the only safe answer.
            }
            return list;
        }

        /// <summary>Serialise items back to a blob.</summary>
        public static string Serialise(IEnumerable<JObject> items)
        {
            var arr = new JArray();
            if (items != null)
                foreach (JObject o in items)
                    if (o != null) arr.Add(o);
            return arr.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>
        /// Append <paramref name="items"/> to a scene's item list. Returns how many were added.
        /// Items already present (same id) are skipped, so injecting twice cannot duplicate a
        /// purchase — which matters because a map can be rebuilt without the app restarting.
        /// </summary>
        public static int Inject(SceneData scene, IEnumerable<JObject> items)
        {
            if (scene == null || items == null) return 0;

            if (!(scene.Root["items"] is JArray arr))
            {
                arr = new JArray();
                scene.Root["items"] = arr;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JToken t in arr)
            {
                string id = t is JObject o ? (string)o["id"] : null;
                if (!string.IsNullOrEmpty(id)) seen.Add(id);
            }

            int added = 0;
            foreach (JObject item in items)
            {
                if (item == null) continue;
                string id = (string)item["id"];
                if (!string.IsNullOrEmpty(id) && !seen.Add(id)) continue;
                arr.Add(item.DeepClone());
                added++;
            }
            return added;
        }

        /// <summary>
        /// Where a newly bought animal goes: a little way in front of the buyer, at their depth.
        /// Dropping it exactly on the camera would put it inside the player's face; the web
        /// places by tapping the seabed, which is not available here.
        /// </summary>
        public static void DropPoint(double camX, double camY, double camZ, double yawRadians,
                                     double distance, out double x, out double y, out double z)
        {
            double d = distance > 0.0 ? distance : 1.0;
            x = camX + Math.Cos(yawRadians) * d;
            y = camY;
            z = camZ + Math.Sin(yawRadians) * d;
        }

        /// <summary>How far in front of the diver a purchase is released (world units).</summary>
        public const double DropDistance = 26.0;

        // ── storage ──────────────────────────────────────────────────────────────

        /// <summary>Everything bought at <paramref name="mapId"/>.</summary>
        public static List<JObject> Load(string mapId)
            => Parse(UnityEngine.PlayerPrefs.GetString(KeyFor(mapId), ""));

        /// <summary>Record a purchase. Written through immediately — a crash must not cost coins.</summary>
        public static void Add(string mapId, JObject item)
        {
            if (item == null) return;
            List<JObject> all = Load(mapId);
            all.Add(item);
            UnityEngine.PlayerPrefs.SetString(KeyFor(mapId), Serialise(all));
            UnityEngine.PlayerPrefs.Save();
        }

        /// <summary>Inject this map's purchases into a freshly fetched scene.</summary>
        public static int InjectFromStore(SceneData scene, string mapId)
            => Inject(scene, Load(mapId));
    }
}
