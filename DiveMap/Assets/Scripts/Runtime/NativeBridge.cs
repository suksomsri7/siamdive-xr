using System.Runtime.InteropServices;
using UnityEngine;

namespace DiveMap.Runtime
{
    /// <summary>
    /// Unity → host app (WO-MERGE P1). The one direction that needs native code; the other way
    /// round the host uses <c>UnitySendMessage</c>, which needs nothing from us but a GameObject
    /// with the right name (<see cref="NativeBootReceiver"/>).
    ///
    /// The whole surface is one call. Unity has exactly one thing to tell the React Native screen
    /// — "the player backed out, take the screen away" — and keeping it to a single opaque string
    /// means the RN side can compare it against <c>NativeBoot.ExitMessage</c> and be done, instead
    /// of parsing a protocol that would have one member for the next two years.
    ///
    /// On iOS the symbol comes from <c>Assets/Plugins/iOS/NativeCallProxy.mm</c>, which is
    /// compiled INTO UnityFramework; the bridge pod registers itself as the receiver
    /// (<c>[FrameworkLibAPI registerAPIforNativeCalls:self]</c>) when it boots Unity. If nobody
    /// registered — the standalone TestFlight build, where Unity is the whole app — the message
    /// goes to a nil receiver and nothing happens, which is exactly right: there is no host to
    /// tell, and the caller does not have to know which build it is running in.
    ///
    /// 🔴 Guarded on <c>UNITY_IOS &amp;&amp; !UNITY_EDITOR</c>, not on UNITY_IOS alone. In the
    /// Editor with the iOS platform selected the DllImport would resolve against the editor
    /// process, which has no such symbol, and the first call would take the play session down
    /// with an EntryPointNotFoundException — while the code around it is being written.
    /// </summary>
    public static class NativeBridge
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void sendMessageToMobileApp(string message);
#endif

        /// <summary>
        /// Send one message to the host, if there is one. Never throws: a bridge that is not
        /// there is a normal state (standalone build, Editor, CI), and a missing symbol must not
        /// be able to break the frame the player is looking at.
        /// </summary>
        public static void Send(string message)
        {
            if (string.IsNullOrEmpty(message)) return;

#if UNITY_IOS && !UNITY_EDITOR
            try
            {
                sendMessageToMobileApp(message);
                Debug.Log("[Native] → host: " + message);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Native] host message '" + message + "' not delivered: " + e.Message);
            }
#elif UNITY_ANDROID && !UNITY_EDITOR
            // The Android half of the same bridge is a static Java method on the library's view
            // manager rather than a C symbol. Android is its own work order (the RN app ships iOS
            // first), so this is written to the library's documented contract and has never run:
            // treat a failure here as "not wired yet", not as a fault worth a red log.
            try
            {
                using (var jc = new AndroidJavaClass("com.azesmwayreactnativeunity.ReactNativeUnityViewManager"))
                {
                    jc.CallStatic("sendMessageToMobileApp", message);
                }
                Debug.Log("[Native] → host: " + message);
            }
            catch (System.Exception e)
            {
                Debug.Log("[Native] no android host bridge ('" + message + "' dropped): " + e.Message);
            }
#else
            // Editor, desktop player, CI. Logged rather than silent so that "did it try to exit?"
            // is answerable from a headless run's log without a phone in hand.
            Debug.Log("[Native] (no host bridge on this platform) → " + message);
#endif
        }

        /// <summary>
        /// Leave the 3D screen. The host pops its route; Unity itself keeps running, paused and
        /// hidden — <c>Unity as a Library</c> is one instance per process and unloading it is how
        /// the next visit gets a black screen.
        /// </summary>
        public static void RequestExit() => Send(DiveMap.Core.NativeBoot.ExitMessage);
    }
}
