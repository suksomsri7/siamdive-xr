using System;
using System.Collections.Generic;
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

        /// <summary>
        /// Show the fps/build/fish badge even though we are embedded. The user asked for those
        /// numbers gone from the merged app, but they are the instrument that settled the whole
        /// "fish are twitching" investigation, so the switch to bring them back has to exist
        /// somewhere the host can reach without a new build.
        /// </summary>
        public bool? Badge;

        /// <summary>
        /// Run the reef in economy mode (half the fish). The host's lever for the memory ceiling
        /// of the merged app — see the note where it is consumed in <c>SceneBuilder</c>.
        /// </summary>
        public bool? Eco;

        /// <summary>
        /// โหมดที่เจ้าบ้านอยากให้เปิดค้างไว้เมื่อแมพขึ้น: "preview" | "ar" | "tour" (WO-PIVOT).
        /// ค่าว่าง/ไม่รู้จัก = ไม่สั่งอะไร — พฤติกรรมเดิมทั้งหมด. ตัวแปลค่าอยู่ที่
        /// <see cref="BootMode.Parse"/> ซึ่งมีเทสของตัวเอง
        /// </summary>
        public string Mode;

        /// <summary>
        /// ไฟล์โมเดลที่ "เจ้าบ้านโหลดเก็บไว้แล้ว": { urlบนเซิร์ฟเวอร์ → path ในเครื่อง }
        ///
        /// 🔴 16 ส.ค. 2026 — ปิดช่องว่างที่ทำให้ผู้ใช้เจอ "กดเก็บลงเครื่องแล้ว แต่โมเดลยังไม่ครบ":
        /// แอปกับ Unity ต่างมีคลังไฟล์ของตัวเองและไม่รู้จักกัน ⇒ ของที่ผู้ใช้อุตส่าห์ดาวน์โหลด
        /// ตอนมีเน็ตไม่เคยถูกใช้เลย · รายการนี้ทำให้ Unity หยิบไฟล์ที่มีอยู่แล้วมาใช้ก่อนเสมอ
        /// ว่าง = ไม่มีอะไรเปลี่ยน (ยังโหลดเองเหมือนเดิม)
        /// </summary>
        public Dictionary<string, string> HostAssets;
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

        // ── Unity → host, contract v2 ────────────────────────────────────────────
        //
        // Namespaced "dm:" while <see cref="ExitMessage"/> deliberately is not: "exit" already
        // shipped in a build the user has on a phone, and renaming it would break that build
        // against a newer host for no gain. Everything added since is prefixed so the host can
        // tell our protocol apart from anything else that might one day share the channel.

        /// <summary>
        /// "I can receive OnNativeBoot now." The host waits for this before posting its payload,
        /// because UnitySendMessage to a GameObject that does not exist yet is dropped in
        /// SILENCE — no exception, no log, no delivery. That is half of the "tapped Htms Chang,
        /// got Posidon" report: the host posted while Unity was still starting and the message
        /// went nowhere.
        /// </summary>
        public const string ReadyMessage = "dm:ready";

        /// <summary>
        /// "I received your boot payload and this is the map I took from it." The suffix is the
        /// shortId, echoed back rather than a bare acknowledgement so the host can tell an ack
        /// for the map it just asked for from an ack for the one before it.
        /// </summary>
        public const string BootAckPrefix = "dm:boot-ack:";

        /// <summary>Entering the drone tour — the host locks itself to landscape.</summary>
        public const string TourOnMessage = "dm:tour:on";

        /// <summary>Leaving it — the host unlocks.</summary>
        public const string TourOffMessage = "dm:tour:off";

        /// <summary>Compose the boot acknowledgement for one map.</summary>
        public static string BootAck(string shortId) => BootAckPrefix + (shortId ?? "");

        /// <summary>
        /// What (if anything) the host must be told about a mode change, from the ONE rule that
        /// already decides this for the standalone app: <see cref="ModeRules.LocksLandscape"/>.
        ///
        /// Deriving it instead of listing modes is what keeps the host in step for free. Tour and
        /// Game are both first-person and both landscape, so swapping between them signals
        /// NOTHING — a host that unlocked and relocked on that transition would spin the screen
        /// in the player's hands halfway through a dive. And when a future mode joins the
        /// landscape set, it joins here too without anyone remembering to come back.
        ///
        /// Returns null when nothing changed, which is most calls.
        /// </summary>
        public static string TourSignal(AppMode prev, AppMode next)
        {
            bool was = ModeRules.LocksLandscape(prev);
            bool now = ModeRules.LocksLandscape(next);
            if (was == now) return null;
            return now ? TourOnMessage : TourOffMessage;
        }

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

        /// <summary>
        /// Has the host explicitly asked for the corner badge? Default false, which only matters
        /// when embedded — the standalone build shows the badge regardless, because there it is
        /// the QC instrument every video from the user is measured with (see <c>Ui.FpsBadge</c>).
        /// </summary>
        public static bool BadgeForced { get; private set; }

        /// <summary>
        /// Has the host asked for economy mode? Consumed by <c>SceneBuilder</c> on the next map
        /// load, through the same switch the in-app "ประหยัดพลังงาน" setting uses.
        /// </summary>
        public static bool EcoMode { get; private set; }

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
            if (args.Badge.HasValue) next.Badge = args.Badge;
            if (args.Eco.HasValue) next.Eco = args.Eco;

            // 🔴 โหมดถูก MERGE เหมือนช่องอื่น แต่ความหมายต่างกันหนึ่งอย่างที่ต้องรู้: เจ้าบ้านส่ง
            // mode มาคู่กับ shortId ทุกครั้งที่ผู้ใช้แตะหมุด ⇒ ค่าที่ค้างอยู่คือ "โหมดที่ผู้ใช้เลือก
            // ครั้งล่าสุด" ซึ่งถูกต้องแล้วสำหรับการโหลดแมพรอบถัดไป. ถ้าวันหนึ่งเจ้าบ้านอยากกลับไป
            // ใช้พฤติกรรมเดิม ให้ส่ง "" มา ไม่ใช่ละช่องนี้ไว้เฉย ๆ
            if (!string.IsNullOrEmpty(args.Mode)) next.Mode = args.Mode;

            Current = next;
            Received = true;
            LibraryMode = next.LibraryMode ?? false;
            HostDeviceId = next.DeviceId ?? "";
            AuthToken = next.AuthToken ?? "";
            BadgeForced = next.Badge ?? false;
            EcoMode = next.Eco ?? false;
            HostMode = BootMode.Parse(next.Mode);
            // payload ที่ไม่ได้ส่ง assets มา = ไม่มีข้อมูลใหม่ ⇒ เก็บของเดิมไว้ (อย่าล้างทิ้ง)
            if (next.HostAssets != null) HostAssets = next.HostAssets;
        }

        /// <summary>
        /// โหมดที่เจ้าบ้านสั่งไว้ล่าสุด (WO-PIVOT). <see cref="BootMode.Requested.None"/> ในบิลด์
        /// เดี่ยว ซึ่งไม่มีใครส่งข้อความนี้เลย — พฤติกรรมเดิมจึงเป็นค่าตั้งต้น ไม่ใช่ทางแยกที่สอง
        /// </summary>
        public static BootMode.Requested HostMode { get; private set; }

        /// <summary>ไฟล์ที่เจ้าบ้านมีอยู่แล้ว (ดู <see cref="NativeBootArgs.HostAssets"/>) — ว่างเสมอในบิลด์เดี่ยว</summary>
        public static IReadOnlyDictionary<string, string> HostAssets { get; private set; }

        /// <summary>path ในเครื่องของ url นี้ ถ้าเจ้าบ้านบอกมาว่ามี — ไม่งั้น null</summary>
        public static string HostAsset(string url)
        {
            if (HostAssets == null || string.IsNullOrEmpty(url)) return null;
            return HostAssets.TryGetValue(url, out string p) ? p : null;
        }

        /// <summary>Back to "no host has spoken" — the standalone build's state, and every test's.</summary>
        public static void Reset()
        {
            Current = default;
            Received = false;
            LibraryMode = false;
            HostDeviceId = "";
            AuthToken = "";
            BadgeForced = false;
            EcoMode = false;
            HostMode = BootMode.Requested.None;
            HostAssets = null;
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
            args.Badge = Flag(root["badge"]);
            args.Eco = Flag(root["eco"]);
            args.Mode = Text(root["mode"]);
            args.HostAssets = Map(root["assets"]);
            return true;
        }

        /// <summary>ออบเจ็กต์ JSON → พจนานุกรมสตริง (คีย์/ค่าที่ไม่ใช่สตริงถูกข้าม)</summary>
        private static Dictionary<string, string> Map(JToken token)
        {
            if (!(token is JObject obj)) return null;
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in obj)
            {
                string v = Text(kv.Value);
                if (!string.IsNullOrEmpty(kv.Key) && !string.IsNullOrEmpty(v)) d[kv.Key] = v;
            }
            return d.Count > 0 ? d : null;
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
