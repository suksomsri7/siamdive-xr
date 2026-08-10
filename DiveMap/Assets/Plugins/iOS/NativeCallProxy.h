// The Unity↔React-Native bridge header — and the single file that decides whether the RN app
// COMPILES AT ALL (WO-MERGE P1).
//
// 🔴 Why this is P0-critical and not a nicety: the host app embeds Unity through
// @azesmway/react-native-unity, whose iOS view is compiled from source inside the RN pod. Its
// very first lines are
//
//     #include <UnityFramework/UnityFramework.h>
//     #include <UnityFramework/NativeCallProxy.h>          ← node_modules/@azesmway/react-native-unity/ios/RNUnityView.mm
//
// and its view class is declared `RNUnityView : RCTViewComponentView <…, NativeCallsProtocol,
// UnityFrameworkListener>` (RNUnityView.h). So if UnityFramework does not EXPORT this header,
// the pod fails at the #include with "file not found" — before a single line of Unity ever runs.
// There is no fallback and no runtime symptom to debug: it is a red build in the RN repo that
// points at a file in THIS repo.
//
// The contract has three parts and all three are copied verbatim from the library's own
// `unity/Assets/Plugins/iOS/NativeCallProxy.h` (the folder its README tells you to copy into the
// Unity project). Do not "improve" any of them — the names are matched by the pod at compile time
// and by `NSClassFromString(@"FrameworkLibAPI")` at run time:
//
//   1. protocol NativeCallsProtocol           — the view adopts it; -sendMessageToMobileApp: is @required
//   2. class    FrameworkLibAPI               — +registerAPIforNativeCalls: is what RNUnityView calls
//                                               (RNUnityView.mm, initUnityModule) to register itself
//   3. C symbol sendMessageToMobileApp        — declared in the .mm; Unity C# reaches it through
//                                               [DllImport("__Internal")] (Scripts/Runtime/NativeBridge.cs)
//
// `visibility("default")` matters as much as the Public header membership: Unity builds the
// framework with -fvisibility=hidden, so without the attribute the class exists but is not in the
// dynamic symbol table and NSClassFromString returns nil at run time — Unity would render fine and
// simply never be able to talk back to React Native.
//
// Membership is set to Public on the UnityFramework target by Editor/IosNativeCallProxyHeader.cs;
// Unity marks plugin headers project-private by default, which is the manual Xcode step the
// library's README shows as a screenshot (step 5) and which no CI run can perform by hand.

#import <Foundation/Foundation.h>
#include <stdbool.h>

@protocol NativeCallsProtocol
@required

- (void) sendMessageToMobileApp:(NSString*)message;

@end

__attribute__ ((visibility("default")))
@interface FrameworkLibAPI : NSObject

+(void) registerAPIforNativeCalls:(id<NativeCallsProtocol>) aApi;

@end

// ── Additions below this line (WO-MERGE P1b) ─────────────────────────────────────────────────
//
// 🔴 ADD ONLY. The three declarations above are the contract the RN pod compiles against; the
// two functions below are ours and the pod neither knows nor cares about them. Nothing above may
// be renamed, reordered or re-typed — the pod's #include would still succeed and it would fail
// later, at link time or (worse) at runtime through NSClassFromString.
//
// Both answer the same question from opposite directions: "is Unity a screen inside somebody
// else's app right now?" — the question AppBoot has to answer BEFORE it opens a map, because the
// beta shares an iOS sandbox with the standalone DiveMap install and PlayerPrefs "shortId"
// therefore holds whatever map the OTHER app was last looking at (the "tapped Htms Chang, got
// Posidon" report). Two of them because they become true at different moments:
//
//   dm_embeddedInHost()  true from the first instruction Unity ever executes — it is a fact about
//                        how this code was PACKAGED, not about anything that has happened yet.
//   dm_hostAttached()    true only once the host has called registerAPIforNativeCalls:, which on
//                        iOS happens right AFTER runEmbeddedWithArgc: returns — and Unity's first
//                        scene is loaded inside that call. So this is false during Awake and true
//                        by the first Update, and anything that gates on it during startup has to
//                        be prepared to wait.

#ifdef __cplusplus
extern "C" {
#endif

/// YES once the host app has registered itself through +registerAPIforNativeCalls: — i.e. once
/// there is somebody on the other end of sendMessageToMobileApp. Used by C# to decide whether a
/// message is worth sending at all, and to know when "dm:ready" can actually be delivered.
bool dm_hostAttached(void);

/// YES when this code is running from inside UnityFramework.framework rather than from the app's
/// own executable — the definition of "Unity as a Library". Available immediately and never
/// changes, which is what makes it safe to gate a startup WAIT on: in the standalone build it is
/// false on the first line and nothing waits for anything.
bool dm_embeddedInHost(void);

#ifdef __cplusplus
}
#endif
