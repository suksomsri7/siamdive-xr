// Implementation half of the RN bridge (WO-MERGE P1) — verbatim from the library's own
// unity/Assets/Plugins/iOS/NativeCallProxy.mm. See NativeCallProxy.h for why every name here is
// load-bearing.
//
// How a message actually travels, Unity → phone:
//
//   C#  NativeBridge.Send("exit")
//     → [DllImport("__Internal")] sendMessageToMobileApp        (the extern "C" symbol below)
//     → the registered `api`, which is the RNUnityView instance  (registerAPIforNativeCalls:)
//     → -[RNUnityView sendMessageToMobileApp:] → self.onUnityMessage → JS onUnityMessage
//
// `api` is a plain file-scope global with no retain: that is the library's design, and the view
// registers itself in initUnityModule every time it boots Unity, so the pointer is refreshed
// rather than owned here. A nil `api` (Unity running with no view attached — the standalone
// TestFlight build) makes the call below a no-op message-to-nil, which is exactly the wanted
// behaviour: the standalone app has nobody to tell.

#import <Foundation/Foundation.h>
#import "NativeCallProxy.h"

@implementation FrameworkLibAPI

id<NativeCallsProtocol> api = NULL;
+(void) registerAPIforNativeCalls:(id<NativeCallsProtocol>) aApi
{
    api = aApi;
}

@end

extern "C"
{
    void sendMessageToMobileApp(const char* message)
    {
        return [api sendMessageToMobileApp:[NSString stringWithUTF8String:message]];
    }

    // ── Additions (WO-MERGE P1b) — see the header for why there are two of these ──────────

    bool dm_hostAttached(void)
    {
        return api != NULL;
    }

    bool dm_embeddedInHost(void)
    {
        // The bundle this very class was compiled into. In the standalone DiveMap app that is
        // the app itself, so the two bundles are the same object; under "Unity as a Library"
        // this file lives in UnityFramework.framework and they differ. Compared by identity —
        // +bundleForClass: returns the cached singleton for a loaded bundle, and comparing paths
        // instead would only add a way to be wrong about trailing slashes.
        //
        // Cached because AppBoot asks once per frame while it waits for the host's boot message,
        // and the answer is decided at link time — it cannot change while the process lives.
        static bool computed = false;
        static bool embedded = false;
        if (!computed)
        {
            NSBundle *own = [NSBundle bundleForClass:[FrameworkLibAPI class]];
            embedded = (own != nil && own != [NSBundle mainBundle]);
            computed = true;
            NSLog(@"[DiveMap] embedded in host = %@ (own bundle %@)",
                  embedded ? @"YES" : @"NO", own.bundleIdentifier);
        }
        return embedded;
    }
}
