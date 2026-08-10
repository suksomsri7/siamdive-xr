using System;
using Newtonsoft.Json.Linq;

namespace DiveMap.Core
{
    /// <summary>
    /// What the host app told Unity at boot (WO-MERGE P1). One message, one struct.
    ///
    /// Every field is OPTIONAL by design. The React Native screen sends
    /// <c>{"shortId":…,"deviceId":…,"lang":…,"libraryMode":1}</c> today and will grow an
    /// <c>authToken</c> later; a Unity build in the field must keep working when a newer host
    /// sends a field it has never heard of, and an older host omits one it now expects. So
    /// absence is represented, not guessed: empty string for the text fields and null for
    /// <see cref="LibraryMode"/>, and <see cref="NativeBoot.Adopt"/> merges rather than replaces.
    /// </summary>
    public struct NativeBootArgs
    {
        /// <summary>Dive-site to open, "" when the host did not name one.</summary>
        public string ShortId;

        /// <summary>
        /// The host's wallet identity. Coins and purchases are keyed by device on the server
        /// (<c>/api/wallet?deviceId=</c>), so this is what makes the money the player sees in the
        /// RN app the same money they see in the 3D screen.
        /// </summary>
        public string DeviceId;

        /// <summary>"th" / "en", or "" when the host said nothing or said something unknown.</summary>
        public string Lang;

        /// <summary>Reserved: RN owns login, and this is where its session would arrive.</summary>
        public string AuthToken;

        /// <summary>
        /// True = Unity is a SCREEN inside another app; false = the standalone DiveMap build;
        /// null = the host did not say, so whatever was decided earlier stands.
        /// </summary>
        public bool? LibraryMode;
    }

    /// <summary>
    /// The boot message from the native host: how to parse it (pure, and therefore tested on this
    /// machine rather than in a 35-minute CI round) and what the app currently believes as a
    /// result.
    ///
    /// The transport is <c>UnitySendMessage(gameObject, method, message)</c>, which can only carry
    /// ONE string — hence a JSON payload — and which addresses a GameObject BY NAME. The name and
    /// the method below are therefore a contract with the RN screen, not an implementation
    /// detail: change either and the message silently goes nowhere (UnitySendMessage logs a
    /// warning at best and the map simply never switches).
    ///
    /// 🔴 Parsing is deliberately forgiving in one direction only. Unknown fields are ignored,
    /// numbers and strings are both accepted where a value could reasonably be either
    /// (<c>"shortId":299</c> and <c>"shortId":"299"</c> mean the same thing to a JS caller), and
    /// junk is REJECTED rather than half-applied: a malformed message must leave the app exactly
    /// as it was, because the alternative — a half-adopted boot state — turns a typo on the RN
    /// side into a blank screen with no map and no way back.
    /// </summary>
    public static class NativeBoot
    {
        /// <summary>
        /// The GameObject the host addresses. It is created at runtime by
        /// <c>Runtime/NativeBootReceiver.cs</c> rather than being the scene's bootstrap object,
        /// which is called "Bootstrap" — renaming a scene object to match a string in another
        /// repo is a fragile way to hold a contract, and a dedicated receiver also exists BEFORE
        /// the scene's AppBoot does, which is half the point (the message can arrive first).
        /// </summary>
        public const string ReceiverObjectName = "AppBoot";

        /// <summary>The method UnitySendMessage invokes on that object.</summary>
        public const string ReceiverMethodName = "OnNativeBoot";

        /// <summary>
        /// What Unity sends back when the player leaves at the top level. Plain string, not JSON:
        /// the host reads it straight off <c>event.nativeEvent.message</c> and compares it, and
        /// there is exactly one thing to say.
        /// </summary>
        public const string ExitMessage = "exit";

        /// <summary>
        /// True once a host message said so. Read by the UI to skip Unity's own map hub and its
        /// login flow — both of which the host app owns when Unity is only one of its screens.
        /// False in the standalone DiveMap build, where nothing ever sends this message, so the
        /// standalone behaviour is the untouched default rather than a second code path.
        /// </summary>
        public static bool LibraryMode { get; private set; }

        /// <summary>
        /// The wallet id the host injected, or "" when it injected none. Consumed by
        /// <c>WalletClient.DeviceId</c>.
        /// </summary>
        public static string HostDeviceId { get; private set; } = "";

        /// <summary>
        /// The host's session token, stored and not yet used.
        ///
        /// TODO(WO-MERGE P2/P3): Unity's account is not token-based — every account route it
        /// calls (<c>/api/account/me</c>, favourites, saves) authenticates by <c>deviceId</c>,
        /// and the server links a device to an account server-side. That means injecting the
        /// host's deviceId ALREADY transfers the session, and a token has nothing to plug into
        /// yet. Kept because the host may start sending one at any time and dropping it on the
        /// floor silently would be worse than holding it.
        /// </summary>
        public static string AuthToken { get; private set; } = "";

        /// <summary>The merged view of every host message so far (diagnostics, and tests).</summary>
        public static NativeBootArgs Current { get; private set; }

        /// <summary>True once any well-formed host message has been adopted.</summary>
        public static bool Received { get; private set; }

        /// <summary>
        /// Fold one message into the current state. MERGE, not replace: a later message that
        /// carries only a token must not wipe the deviceId an earlier one established.
        /// </summary>
        public static void Adopt(NativeBootArgs args)
        {
            NativeBootArgs next = Current;

            if (!string.IsNullOrEmpty(args.ShortId)) next.ShortId = args.ShortId;
            if (!string.IsNullOrEmpty(args.DeviceId)) next.DeviceId = args.DeviceId;
            if (!string.IsNullOrEmpty(args.Lang)) next.Lang = args.Lang;
            if (!string.IsNullOrEmpty(args.AuthToken)) next.AuthToken = args.AuthToken;
            if (args.LibraryMode.HasValue) next.LibraryMode = args.LibraryMode;

            Current = next;
            Received = true;
            LibraryMode = next.LibraryMode ?? false;
            HostDeviceId = next.DeviceId ?? "";
            AuthToken = next.AuthToken ?? "";
        }

        /// <summary>Back to "no host has spoken" — the standalone build's state, and every test's.</summary>
        public static void Reset()
        {
            Current = default;
            Received = false;
            LibraryMode = false;
            HostDeviceId = "";
            AuthToken = "";
        }

        /// <summary>
        /// Read one boot payload. Returns false — leaving <paramref name="args"/> at its default —
        /// for anything that is not a JSON OBJECT: null, empty, a bare number, an array, or text
        /// that does not parse at all. Any object parses, including <c>{}</c>, because "the host
        /// sent a message with nothing in it" is a legitimate no-op and not an error.
        /// </summary>
        public static bool TryParse(string json, out NativeBootArgs args)
        {
            args = default;
            if (string.IsNullOrWhiteSpace(json)) return false;

            JObject root;
            try
            {
                JToken token = JToken.Parse(json);
                root = token as JObject;
                if (root == null) return false;
            }
            catch (Exception)
            {
                // Newtonsoft throws JsonReaderException, but a payload arriving from another
                // process is not the place to be precise about which exception type: anything
                // thrown here means "not a message", and the caller logs the text.
                return false;
            }

            args.ShortId = Text(root["shortId"]);
            args.DeviceId = Text(root["deviceId"]);
            args.Lang = Language(Text(root["lang"]));
            args.AuthToken = Text(root["authToken"]);
            args.LibraryMode = Flag(root["libraryMode"]);
            return true;
        }

        /// <summary>
        /// A scalar as text, "" for absent/null/object/array. Numbers are accepted because a
        /// JavaScript caller has no reason to think <c>shortId: 299</c> is different from
        /// <c>shortId: "299"</c>, and being strict here would fail in a way nobody could see.
        /// </summary>
        private static string Text(JToken token)
        {
            if (token == null) return "";
            if (token.Type == JTokenType.Null || token.Type == JTokenType.Undefined) return "";
            if (token.Type == JTokenType.Object || token.Type == JTokenType.Array) return "";
            return (token.ToString() ?? "").Trim();
        }

        /// <summary>
        /// Clamp a language code to something the app has strings for. Anything else — "de",
        /// "TH-th", junk — returns "" so the caller leaves the current language alone rather than
        /// forcing English on a Thai user because the host sent a locale nobody supports.
        /// The two codes match <c>UiStrings.Thai</c> / <c>UiStrings.English</c>; they are spelled
        /// out here because UiStrings reaches for <c>Application.systemLanguage</c> and this file
        /// must stay free of UnityEngine to be testable off-device.
        /// </summary>
        private static string Language(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            string l = raw.Trim().ToLowerInvariant();
            return l == "th" || l == "en" ? l : "";
        }

        /// <summary>
        /// A boolean as any of the shapes a JSON caller might send it in: <c>true</c>, <c>1</c>,
        /// <c>"1"</c>, <c>"true"</c>, <c>"yes"</c>. The RN screen sends the number 1 today; the
        /// value of accepting the rest is that a future host is never wrong about a flag whose
        /// only two states are "the whole hub UI" and "not the whole hub UI".
        /// Absent → null, which <see cref="Adopt"/> reads as "no opinion".
        /// </summary>
        private static bool? Flag(JToken token)
        {
            if (token == null) return null;
            switch (token.Type)
            {
                case JTokenType.Null:
                case JTokenType.Undefined:
                    return null;
                case JTokenType.Boolean:
                    return (bool)token;
                case JTokenType.Integer:
                case JTokenType.Float:
                    return Math.Abs((double)token) > 0.0001;
                case JTokenType.String:
                    string s = ((string)token ?? "").Trim().ToLowerInvariant();
                    if (s.Length == 0) return null;
                    if (s == "0" || s == "false" || s == "no") return false;
                    return true;
                default:
                    return null;
            }
        }
    }
}
