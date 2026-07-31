using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using DiveMap.Core;

namespace DiveMap.Runtime
{
    /// <summary>
    /// The account API (PARITY section J). Verified against the routes in siamdive-maps:
    /// <code>
    ///   GET  /api/account/me?deviceId=              → { linked, email, name, admin }
    ///   POST /api/account/email/request-otp {email} → 200 · 400 invalid_email · 429 too_soon
    ///   POST /api/account/email/verify {deviceId,email,code}
    ///                                               → { ok, email, needName, name } · 400 otp_invalid
    ///   POST /api/account/set-username {deviceId,email,name}
    ///                                               → { ok, name } · 409 name_taken/name_reserved
    ///   POST /api/account/admin-login {deviceId,passcode} → { email, name } · 401 wrong_passcode
    ///   POST /api/account/logout {deviceId}
    ///   POST /api/account/delete {deviceId}
    ///   GET  /api/dive-sites?deviceId=              → { sites:[…] }   (My Map)
    ///   GET  /api/dive-sites/favorites?deviceId=    → { shortIds:[], sites:[…] }
    ///   POST /api/dive-sites/favorites {deviceId,shortId,on}
    /// </code>
    ///
    /// Note what verify does server-side: it adopts every map this DEVICE made into the account
    /// (<c>updateMany where deviceId, accountId:null</c>) and folds the device wallet in. Signing
    /// in is therefore not read-only — which is why the UI says so before it sends the code.
    /// </summary>
    public static class AccountClient
    {
        public const int TimeoutSeconds = 20;

        private static string Base => MapApiClient.DefaultBaseUrl;
        private static string Device => WalletClient.DeviceId;

        /// <summary>What /api/account/me answers.</summary>
        public struct Me
        {
            public bool Linked;
            public string Email;
            public string Name;
            public bool Admin;
        }

        // ── identity ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Ask the server who this device belongs to and write it into <see cref="Account"/>.
        /// <paramref name="onDone"/> gets (me, accountChanged). A network failure leaves the
        /// cached identity alone — being offline is not the same as being logged out.
        /// </summary>
        public static IEnumerator FetchMe(Action<Me, bool> onDone)
        {
            string url = Base + "/api/account/me?deviceId=" + UnityWebRequest.EscapeURL(Device);
            JObject o = null;
            yield return Get(url, r => o = r, e => Debug.LogWarning("[Account] me failed: " + e));

            if (o == null) { onDone?.Invoke(default, false); yield break; }

            var me = new Me
            {
                Linked = Read(o, "linked") == "True" || (o["linked"] != null && (bool)o["linked"]),
                Email = Read(o, "email"),
                Name = Read(o, "name"),
                Admin = o["admin"] != null && (bool)o["admin"],
            };
            bool changed = Account.Apply(me.Linked ? me.Email : null, me.Name);
            Debug.Log($"[Account] me linked={me.Linked} name='{me.Name}' admin={me.Admin} changed={changed}");
            onDone?.Invoke(me, changed);
        }

        /// <summary>Send the six-digit code. <paramref name="onDone"/> gets null, or an error key.</summary>
        public static IEnumerator RequestOtp(string email, Action<string> onDone)
        {
            var body = new JObject { ["email"] = (email ?? "").Trim() };
            yield return Post(Base + "/api/account/email/request-otp", body,
                              _ => onDone?.Invoke(null), e => onDone?.Invoke(e));
        }

        /// <summary>
        /// Verify the code. On success <paramref name="onDone"/> gets (needName, error=null);
        /// the identity is already written to <see cref="Account"/>.
        /// </summary>
        public static IEnumerator Verify(string email, string code, Action<bool, string> onDone)
        {
            string mail = (email ?? "").Trim();
            var body = new JObject
            {
                ["deviceId"] = Device,
                ["email"] = mail,
                ["code"] = (code ?? "").Trim(),
            };

            JObject res = null; string err = null;
            yield return Post(Base + "/api/account/email/verify", body, r => res = r, e => err = e);

            if (err != null || res == null) { onDone?.Invoke(false, err ?? "verify_failed"); yield break; }

            bool needName = res["needName"] != null && (bool)res["needName"];
            string name = Read(res, "name");
            Account.Apply(Read(res, "email") ?? mail, name);
            Debug.Log($"[Account] verified needName={needName} name='{name}'");
            onDone?.Invoke(needName, null);
        }

        /// <summary>Claim a username (new accounts only). <paramref name="onDone"/> error or null.</summary>
        public static IEnumerator SetUsername(string email, string name, Action<string> onDone)
        {
            var body = new JObject
            {
                ["deviceId"] = Device,
                ["email"] = (email ?? "").Trim(),
                ["name"] = Account.CleanName(name),
            };

            JObject res = null; string err = null;
            yield return Post(Base + "/api/account/set-username", body, r => res = r, e => err = e);

            if (err != null || res == null) { onDone?.Invoke(err ?? "name_failed"); yield break; }
            Account.Apply((email ?? "").Trim(), Read(res, "name"));
            onDone?.Invoke(null);
        }

        /// <summary>Admin sign-in — passcode instead of an OTP.</summary>
        public static IEnumerator AdminLogin(string passcode, Action<string> onDone)
        {
            var body = new JObject { ["deviceId"] = Device, ["passcode"] = (passcode ?? "").Trim() };
            JObject res = null; string err = null;
            yield return Post(Base + "/api/account/admin-login", body, r => res = r, e => err = e);

            if (err != null || res == null) { onDone?.Invoke(err ?? "login_failed"); yield break; }
            Account.Apply(Read(res, "email") ?? Account.AdminEmail, Read(res, "name"));
            onDone?.Invoke(null);
        }

        public static IEnumerator Logout(Action onDone)
        {
            var body = new JObject { ["deviceId"] = Device };
            yield return Post(Base + "/api/account/logout", body, _ => { }, e =>
                Debug.LogWarning("[Account] logout: " + e));
            Account.SignOut();
            Debug.Log("[Account] signed out");
            onDone?.Invoke();
        }

        /// <summary>Delete the account. Maps stay on the device; the email link is removed.</summary>
        public static IEnumerator DeleteAccount(Action<bool> onDone)
        {
            var body = new JObject { ["deviceId"] = Device };
            bool ok = false;
            yield return Post(Base + "/api/account/delete", body, _ => ok = true, e =>
                Debug.LogWarning("[Account] delete: " + e));
            if (ok) Account.SignOut();
            Debug.Log("[Account] delete ok=" + ok);
            onDone?.Invoke(ok);
        }

        // ── lists ────────────────────────────────────────────────────────────────

        /// <summary>My Map: every map this account (or, logged out, this device) owns.</summary>
        public static IEnumerator MyMaps(Action<List<MapCard>> onDone)
        {
            string url = Base + "/api/dive-sites?deviceId=" + UnityWebRequest.EscapeURL(Device);
            yield return Sites(url, onDone, "myMaps");
        }

        /// <summary>Server-side favourites, keyed by account when signed in, else by device.</summary>
        public static IEnumerator Favourites(Action<List<MapCard>> onDone)
        {
            string url = Base + "/api/dive-sites/favorites?deviceId=" + UnityWebRequest.EscapeURL(Device);
            yield return Sites(url, onDone, "favourites");
        }

        /// <summary>Star / un-star a map. <paramref name="onDone"/> reports whether it stuck.</summary>
        public static IEnumerator ToggleFavourite(string shortId, bool on, Action<bool> onDone)
        {
            var body = new JObject { ["deviceId"] = Device, ["shortId"] = shortId, ["on"] = on };
            bool ok = false;
            yield return Post(Base + "/api/dive-sites/favorites", body, _ => ok = true, e =>
                Debug.LogWarning("[Account] favourite: " + e));
            Debug.Log($"[Account] favourite {shortId} on={on} ok={ok}");
            onDone?.Invoke(ok);
        }

        private static IEnumerator Sites(string url, Action<List<MapCard>> onDone, string what)
        {
            JObject o = null;
            yield return Get(url, r => o = r, e => Debug.LogWarning($"[Account] {what} failed: " + e));

            var cards = new List<MapCard>();
            if (o != null)
            {
                // MapDirectory.ParseList understands { sites:[…] } — these routes return the same
                // rows without a `total`, and its fallback (total = skip + count) covers that.
                try { cards = MapDirectory.ParseList(o.ToString(Newtonsoft.Json.Formatting.None)).Cards; }
                catch (Exception e) { Debug.LogWarning($"[Account] {what} parse: " + e.Message); }
            }
            Debug.Log($"[Account] {what} → {cards.Count}");
            onDone?.Invoke(cards);
        }

        // ── plumbing ─────────────────────────────────────────────────────────────

        private static string Read(JObject o, string key)
        {
            JToken t = o != null ? o[key] : null;
            return t == null || t.Type == JTokenType.Null ? null : t.ToString();
        }

        private static IEnumerator Get(string url, Action<JObject> ok, Action<string> fail)
        {
            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                req.timeout = TimeoutSeconds;
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    fail?.Invoke($"({(long)req.responseCode}) {req.error}");
                    yield break;
                }
                JObject parsed = null;
                try { parsed = JObject.Parse(req.downloadHandler.text); }
                catch (Exception e) { fail?.Invoke("parse: " + e.Message); yield break; }
                ok?.Invoke(parsed);
            }
        }

        /// <summary>
        /// POST JSON. On a non-2xx the body's <c>error</c> key is handed back verbatim, because
        /// these routes answer with machine keys (<c>name_taken</c>, <c>wrong_code</c>) that the
        /// UI turns into sentences — a generic "failed" would lose which of them it was.
        /// </summary>
        private static IEnumerator Post(string url, JObject body, Action<JObject> ok, Action<string> fail)
        {
            byte[] payload = System.Text.Encoding.UTF8.GetBytes(body.ToString(Newtonsoft.Json.Formatting.None));
            using (var req = new UnityWebRequest(url, "POST")
            {
                uploadHandler = new UploadHandlerRaw(payload),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = TimeoutSeconds,
            })
            {
                req.SetRequestHeader("Content-Type", "application/json");
                yield return req.SendWebRequest();

                string text = req.downloadHandler != null ? req.downloadHandler.text : null;
                JObject parsed = null;
                try { if (!string.IsNullOrEmpty(text)) parsed = JObject.Parse(text); }
                catch { /* an error page is not JSON; the status still tells us what happened */ }

                if (req.result != UnityWebRequest.Result.Success)
                {
                    string key = parsed != null ? Read(parsed, "error") : null;
                    fail?.Invoke(key ?? $"http_{(long)req.responseCode}");
                    yield break;
                }
                ok?.Invoke(parsed ?? new JObject());
            }
        }
    }
}
