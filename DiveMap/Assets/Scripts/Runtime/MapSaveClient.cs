using System;
using System.Collections;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace DiveMap.Runtime
{
    /// <summary>
    /// Writing a map back to the server (PARITY J1 — the web's <c>doSave()</c> / <c>autosaveTick</c>).
    ///
    /// Contract, read off <c>src/app/api/dive-sites/[shortId]/route.ts</c>:
    /// <code>
    ///   PATCH /api/dive-sites/{shortId}
    ///     { deviceId, baseRev?, name?, items?, pins?, env?, thumbUrl?,
    ///       isPublic?|editPolicy?, searchable?, copyAllowed?, editEmails? }
    ///     → 200 { site: { shortId, rev, name, isPublic, publicSlug, thumbUrl, … } }
    ///       400 deviceId required
    ///       403 Forbidden        — editPolicy said no
    ///       404 Not found
    ///       409 { conflict:true, site } — baseRev is stale, here is the newer copy
    /// </code>
    ///
    /// Two things the route does that the caller must respect:
    ///  • Who you are comes from the server-side device→account link, never from the body
    ///    (:113 "NEVER trust a client-sent accountId"). Sending an email or accountId is pointless.
    ///  • <c>baseRev</c> is OPT-IN. Omit it and a save silently clobbers whatever a second device
    ///    wrote in the meantime. This client always sends it, and surfaces the 409 rather than
    ///    retrying blind — losing someone else's edit is worse than failing to save.
    /// </summary>
    public static class MapSaveClient
    {
        public const int TimeoutSeconds = 25;

        /// <summary>Outcome of a save. <see cref="Rev"/> is the map's new revision on success.</summary>
        public struct Result
        {
            public bool Ok;
            /// <summary>True when the server refused because this account may not edit the map.</summary>
            public bool Forbidden;
            /// <summary>True when someone else saved first — the local copy is stale.</summary>
            public bool Conflict;
            public int Rev;
            public string Error;
        }

        /// <summary>
        /// Write the map's item list. <paramref name="baseRev"/> is the revision the caller
        /// last read; pass a negative number only when deliberately overwriting.
        /// </summary>
        public static IEnumerator SaveItems(string shortId, JArray items, int baseRev,
                                            Action<Result> onDone)
        {
            var body = new JObject { ["items"] = items ?? new JArray() };
            yield return Patch(shortId, body, baseRev, onDone);
        }

        /// <summary>
        /// Items AND env in one write. Sculpting lives in <c>env.sculpt</c>, so a save that only
        /// carried items would let a re-shaped seabed look right until the next load and then
        /// silently revert — the most confusing failure a builder can have.
        /// </summary>
        public static IEnumerator SaveMap(string shortId, JArray items, JObject env, int baseRev,
                                          Action<Result> onDone)
        {
            var body = new JObject { ["items"] = items ?? new JArray() };
            if (env != null) body["env"] = env;
            yield return Patch(shortId, body, baseRev, onDone);
        }

        /// <summary>Rename (the web's name modal). Names are clipped to 120 chars server-side.</summary>
        public static IEnumerator Rename(string shortId, string name, Action<Result> onDone)
        {
            var body = new JObject { ["name"] = (name ?? "").Trim() };
            yield return Patch(shortId, body, -1, onDone);
        }

        /// <summary>
        /// Public / private. The route maps this onto <c>editPolicy</c>: public means
        /// <c>"all"</c> — anyone may EDIT, not merely view. The UI has to say that plainly,
        /// because "public" reads like "visible" and it is really "editable by strangers".
        /// </summary>
        public static IEnumerator SetPublic(string shortId, bool isPublic, Action<Result> onDone)
        {
            var body = new JObject { ["editPolicy"] = isPublic ? "all" : "none" };
            yield return Patch(shortId, body, -1, onDone);
        }

        /// <summary>Whether the map appears in the public directory at all.</summary>
        public static IEnumerator SetSearchable(string shortId, bool searchable, Action<Result> onDone)
        {
            var body = new JObject { ["searchable"] = searchable };
            yield return Patch(shortId, body, -1, onDone);
        }

        /// <summary>Grant edit rights to specific emails (editPolicy "some").</summary>
        public static IEnumerator SetEditors(string shortId, string[] emails, Action<Result> onDone)
        {
            var arr = new JArray();
            if (emails != null)
                foreach (string e in emails)
                    if (!string.IsNullOrWhiteSpace(e)) arr.Add(e.Trim().ToLowerInvariant());

            var body = new JObject { ["editPolicy"] = "some", ["editEmails"] = arr };
            yield return Patch(shortId, body, -1, onDone);
        }

        /// <summary>
        /// Set the map's cover image. <paramref name="thumbUrl"/> must already be a CDN url from
        /// the media route — the PATCH stores the string as given, so anything else would put a
        /// dead link on every card in the hub.
        /// </summary>
        public static IEnumerator SetThumbnail(string shortId, string thumbUrl, Action<Result> onDone)
        {
            var body = new JObject { ["thumbUrl"] = thumbUrl };
            yield return Patch(shortId, body, -1, onDone);
        }

        /// <summary>Create an empty map. <paramref name="onDone"/> gets its shortId, or null.</summary>
        public static IEnumerator Create(string name, Action<string, string> onDone)
        {
            var body = new JObject
            {
                ["deviceId"] = WalletClient.DeviceId,
                ["name"] = string.IsNullOrWhiteSpace(name) ? "Dive Site" : name.Trim(),
            };

            JObject res = null; string err = null;
            yield return Send("POST", MapApiClient.DefaultBaseUrl + "/api/dive-sites", body,
                              (o, _) => res = o, (e, _) => err = e);

            if (err != null || res == null) { onDone?.Invoke(null, err ?? "create_failed"); yield break; }

            JToken site = res["site"] ?? res;
            string shortId = (string)site["shortId"];
            Debug.Log($"[Map] created {shortId} name='{name}'");
            onDone?.Invoke(shortId, shortId == null ? "no_short_id" : null);
        }

        public static IEnumerator Delete(string shortId, Action<bool> onDone)
        {
            string url = MapApiClient.DefaultBaseUrl + "/api/dive-sites/"
                       + UnityWebRequest.EscapeURL(shortId)
                       + "?deviceId=" + UnityWebRequest.EscapeURL(WalletClient.DeviceId);

            bool ok = false;
            using (UnityWebRequest req = UnityWebRequest.Delete(url))
            {
                req.timeout = TimeoutSeconds;
                yield return req.SendWebRequest();
                ok = req.result == UnityWebRequest.Result.Success;
                if (!ok) Debug.LogWarning($"[Map] delete {shortId} ({(long)req.responseCode}) {req.error}");
            }
            Debug.Log($"[Map] delete {shortId} ok={ok}");
            onDone?.Invoke(ok);
        }

        // ── plumbing ─────────────────────────────────────────────────────────────

        private static IEnumerator Patch(string shortId, JObject body, int baseRev, Action<Result> onDone)
        {
            if (string.IsNullOrEmpty(shortId))
            {
                onDone?.Invoke(new Result { Error = "no_map" });
                yield break;
            }

            body["deviceId"] = WalletClient.DeviceId;
            if (baseRev >= 0) body["baseRev"] = baseRev;

            string url = MapApiClient.DefaultBaseUrl + "/api/dive-sites/" + UnityWebRequest.EscapeURL(shortId);
            var result = new Result();

            yield return Send("PATCH", url, body,
                (o, _) =>
                {
                    JToken site = o?["site"];
                    result.Ok = true;
                    result.Rev = site != null && site["rev"] != null ? (int)site["rev"] : -1;
                },
                (e, code) =>
                {
                    result.Error = e;
                    result.Forbidden = code == 403;
                    result.Conflict = code == 409 || e == "conflict";
                });

            Debug.Log($"[Map] save {shortId} ok={result.Ok} rev={result.Rev} " +
                      $"forbidden={result.Forbidden} conflict={result.Conflict} err={result.Error}");
            onDone?.Invoke(result);
        }

        private static IEnumerator Send(string verb, string url, JObject body,
                                        Action<JObject, long> ok, Action<string, long> fail)
        {
            byte[] payload = System.Text.Encoding.UTF8.GetBytes(
                body.ToString(Newtonsoft.Json.Formatting.None));

            using (var req = new UnityWebRequest(url, verb)
            {
                uploadHandler = new UploadHandlerRaw(payload),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = TimeoutSeconds,
            })
            {
                req.SetRequestHeader("Content-Type", "application/json");
                yield return req.SendWebRequest();

                long code = req.responseCode;
                JObject parsed = null;
                try
                {
                    string text = req.downloadHandler != null ? req.downloadHandler.text : null;
                    if (!string.IsNullOrEmpty(text)) parsed = JObject.Parse(text);
                }
                catch { /* an error page is not JSON; the status code still tells the story */ }

                if (req.result != UnityWebRequest.Result.Success)
                {
                    string key = parsed?["error"] != null ? parsed["error"].ToString() : null;
                    fail?.Invoke(key ?? $"http_{code}", code);
                    yield break;
                }
                ok?.Invoke(parsed ?? new JObject(), code);
            }
        }
    }
}
