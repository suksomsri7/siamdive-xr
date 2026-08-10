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

@protocol NativeCallsProtocol
@required

- (void) sendMessageToMobileApp:(NSString*)message;

@end

__attribute__ ((visibility("default")))
@interface FrameworkLibAPI : NSObject

+(void) registerAPIforNativeCalls:(id<NativeCallsProtocol>) aApi;

@end
