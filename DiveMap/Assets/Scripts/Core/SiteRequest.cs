using System;

namespace DiveMap.Core
{
    /// <summary>
    /// The URL of a dive-site GET (WO-MERGE P1d).
    ///
    /// 🔴 Why this is its own file with its own test, for one string concatenation. The app could
    /// not edit ANY map — not the owner's, not the admin's, not in the embedded build and not in
    /// the standalone one — and the whole cause was a missing query parameter on this one URL.
    /// <c>canEdit</c> is the SERVER's verdict, computed from who is asking, and nothing in the
    /// request said who that was: Unity fetched <c>/api/dive-sites/{shortId}</c> bare, so the
    /// answer could only ever be "no". The symptom reached the user as "This map is not editable"
    /// while they were signed in as its owner, which reads like a permission bug and is a URL bug.
    ///
    /// The web's own call is the contract (builder.html:3541):
    /// <code>
    ///   api('/api/dive-sites/'+shortId+'?deviceId='+encodeURIComponent(getDeviceId()))
    /// </code>
    /// and the comment three lines above it is the other half of the contract — worth copying in
    /// full, because it says what NOT to send:
    /// <code>
    ///   // NOTE: no `&amp;email=` anymore — the server resolves the caller's email from the
    ///   // device→account link and ignores anything the client claims (it was an
    ///   // editPolicy:'some' bypass).
    /// </code>
    /// So there is no token, no header and no email: the DEVICE id is the whole identity, and the
    /// server does the device→account lookup itself. That is why injecting the host app's deviceId
    /// (WO-MERGE P1) is all the embedded build needs to be recognised as the owner — provided the
    /// host's login has bound that device to the account on the server side.
    ///
    /// Escaped with <see cref="Uri.EscapeDataString"/> rather than Unity's <c>EscapeURL</c>: the
    /// latter is form encoding and turns a space into '+', which is not what encodeURIComponent
    /// does and not what a path/query component means.
    /// </summary>
    public static class SiteRequest
    {
        /// <summary>
        /// <c>{baseUrl}/api/dive-sites/{shortId}</c>, with <c>?deviceId=</c> when there is one.
        ///
        /// A missing device id is not an error and must not become the string "null" in a query:
        /// the app has to keep working for somebody browsing a public map before anything has
        /// identified them, and the server answers such a request perfectly well with
        /// <c>canEdit:false</c>.
        /// </summary>
        public static string Url(string baseUrl, string shortId, string deviceId)
        {
            string b = (baseUrl ?? "").TrimEnd('/');
            string url = b + "/api/dive-sites/" + Uri.EscapeDataString(shortId ?? "");
            if (!string.IsNullOrEmpty(deviceId))
                url += "?deviceId=" + Uri.EscapeDataString(deviceId);
            return url;
        }
    }
}
