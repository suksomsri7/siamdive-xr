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
}
