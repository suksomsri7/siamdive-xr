using System.Collections;
using DiveMap.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace DiveMap.Runtime
{
    /// <summary>
    /// P3b — keeps the purse in step with <c>/api/wallet</c>, the same endpoint and the same
    /// protocol the web uses (builder.html 4320-4342):
    ///
    ///   GET  /api/wallet?deviceId=…            → { coins }
    ///   POST /api/wallet {deviceId, earned, spent}  → { coins }     (deltas, server authoritative)
    ///   POST /api/wallet {deviceId, coins}          → seed, once, for a player it has never seen
    ///
    /// The wallet is keyed by DEVICE, not by account — which is why coins work before anyone logs
    /// in, and why this could land ahead of the login work.
    ///
    /// Unacknowledged deltas are held in PlayerPrefs, so quitting mid-dive does not cost the
    /// player their pickups: they go out with the next save.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WalletClient : MonoBehaviour
    {
        private const string PendEarnKey = "wallet_pend_earn";
        private const string PendSpendKey = "wallet_pend_spend";
        private const string DeviceKey = "device_id";

        private static WalletClient _instance;

        private float _saveAt = -1f;
        private bool _inFlight;

        public static WalletClient Ensure()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("WalletClient");
            _instance = go.AddComponent<WalletClient>();
            DontDestroyOnLoad(go);
            return _instance;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        /// <summary>Stable per-install id, the same idea as the web's getDeviceId().</summary>
        public static string DeviceId
        {
            get
            {
                string id = PlayerPrefs.GetString(DeviceKey, "");
                if (!string.IsNullOrEmpty(id)) return id;
                id = SystemInfo.deviceUniqueIdentifier;
                if (string.IsNullOrEmpty(id) || id == SystemInfo.unsupportedIdentifier)
                    id = System.Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString(DeviceKey, id);
                PlayerPrefs.Save();
                return id;
            }
        }

        private static int PendingEarned
        {
            get => PlayerPrefs.GetInt(PendEarnKey, 0);
            set { PlayerPrefs.SetInt(PendEarnKey, Mathf.Max(0, value)); PlayerPrefs.Save(); }
        }

        private static int PendingSpent
        {
            get => PlayerPrefs.GetInt(PendSpendKey, 0);
            set { PlayerPrefs.SetInt(PendSpendKey, Mathf.Max(0, value)); PlayerPrefs.Save(); }
        }

        private static bool Online => Application.internetReachability != NetworkReachability.NotReachable;

        // ── public API ───────────────────────────────────────────────────────────

        /// <summary>Record coins earned locally and schedule a (debounced) save.</summary>
        public static void Earn(int amount)
        {
            if (amount <= 0) return;
            PendingEarned = PendingEarned + amount;
            Ensure().Schedule();
        }

        /// <summary>Record coins spent locally and schedule a save.</summary>
        public static void Spend(int amount)
        {
            if (amount <= 0) return;
            PendingSpent = PendingSpent + amount;
            Ensure().Schedule();
        }

        /// <summary>Pull the server's balance and reconcile it with anything still pending.</summary>
        public static void Refresh(System.Action<int> onCoins)
        {
            WalletClient c = Ensure();
            if (!Online) { onCoins?.Invoke(TrashGameSystem.Coins); return; }
            c.StartCoroutine(c.Load(onCoins));
        }

        /// <summary>Send whatever is pending right now (leaving the tour, app pausing).</summary>
        public static void Flush(System.Action<int> onCoins = null)
        {
            WalletClient c = Ensure();
            if (!Online || c._inFlight) return;
            if (!Wallet.HasPending(PendingEarned, PendingSpent)) return;
            c.StartCoroutine(c.Save(onCoins));
        }

        private void Schedule() => _saveAt = Time.unscaledTime + Wallet.SaveDebounceSeconds;

        private void Update()
        {
            if (_saveAt < 0f || Time.unscaledTime < _saveAt) return;
            _saveAt = -1f;
            Flush();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) Flush();
        }

        private void OnApplicationQuit() => Flush();

        // ── requests ─────────────────────────────────────────────────────────────

        private IEnumerator Load(System.Action<int> onCoins)
        {
            string url = MapApiClient.DefaultBaseUrl + "/api/wallet?deviceId=" + UnityWebRequest.EscapeURL(DeviceId);
            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                req.timeout = 15;
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[Wallet] load failed ({req.error}) — keeping the local balance");
                    onCoins?.Invoke(TrashGameSystem.Coins);
                    yield break;
                }

                int? server = ReadCoins(req.downloadHandler.text);
                if (Wallet.NeedsSeed(server.HasValue == false))
                {
                    // A player this server has never seen: publish the local balance once, as an
                    // absolute, so they do not restart at zero on a new device.
                    yield return Seed(TrashGameSystem.Coins);
                    onCoins?.Invoke(TrashGameSystem.Coins);
                    yield break;
                }

                int coins = Wallet.Reconcile(server.Value, PendingEarned, PendingSpent);
                Debug.Log($"[Wallet] server={server.Value} pending=+{PendingEarned}/-{PendingSpent} → {coins}");
                onCoins?.Invoke(coins);
            }
        }

        private IEnumerator Save(System.Action<int> onCoins)
        {
            int earned = PendingEarned, spent = PendingSpent;
            PendingEarned = 0;
            PendingSpent = 0;
            _inFlight = true;

            string body = "{\"deviceId\":\"" + DeviceId + "\",\"earned\":" + earned + ",\"spent\":" + spent + "}";
            using (UnityWebRequest req = Post(MapApiClient.DefaultBaseUrl + "/api/wallet", body))
            {
                yield return req.SendWebRequest();
                _inFlight = false;

                if (req.result != UnityWebRequest.Result.Success)
                {
                    // Put them back — a dropped request must not cost the player their dive.
                    PendingEarned = PendingEarned + earned;
                    PendingSpent = PendingSpent + spent;
                    Debug.LogWarning($"[Wallet] save failed ({req.error}) — +{earned}/-{spent} re-queued");
                    yield break;
                }

                int? server = ReadCoins(req.downloadHandler.text);
                if (server.HasValue)
                {
                    int coins = Wallet.Reconcile(server.Value, PendingEarned, PendingSpent);
                    Debug.Log($"[Wallet] saved +{earned}/-{spent} → server={server.Value} shown={coins}");
                    onCoins?.Invoke(coins);
                }
            }
        }

        private IEnumerator Seed(int coins)
        {
            string body = "{\"deviceId\":\"" + DeviceId + "\",\"coins\":" + coins + "}";
            using (UnityWebRequest req = Post(MapApiClient.DefaultBaseUrl + "/api/wallet", body))
            {
                yield return req.SendWebRequest();
                Debug.Log($"[Wallet] seeded {coins} ({req.result})");
            }
        }

        private static UnityWebRequest Post(string url, string json)
        {
            var req = new UnityWebRequest(url, "POST")
            {
                uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 15,
            };
            req.SetRequestHeader("Content-Type", "application/json");
            return req;
        }

        /// <summary>Coins out of a wallet response, or null when the server has no wallet for us.</summary>
        private static int? ReadCoins(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                JObject o = JObject.Parse(json);
                JToken t = o["coins"];
                if (t == null || t.Type == JTokenType.Null) return null;
                return (int)t;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Wallet] unreadable response: {e.Message}");
                return null;
            }
        }
    }
}
