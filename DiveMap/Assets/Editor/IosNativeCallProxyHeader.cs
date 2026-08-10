using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
#if UNITY_IOS
using UnityEditor.iOS.Xcode;
#endif

namespace DiveMap.EditorTools
{
    /// <summary>
    /// Export <c>NativeCallProxy.h</c> from the UnityFramework target (WO-MERGE P1) — the one
    /// build step that decides whether the React Native app compiles.
    ///
    /// Unity adds every header under <c>Assets/Plugins/iOS/</c> to the UnityFramework target with
    /// PROJECT visibility, which means the framework can use it internally but does not copy it
    /// into <c>UnityFramework.framework/Headers/</c>. The RN bridge pod
    /// (@azesmway/react-native-unity, ios/RNUnityView.mm line 4) opens with
    /// <c>#include &lt;UnityFramework/NativeCallProxy.h&gt;</c>, so with project visibility the
    /// pod fails to compile: "'UnityFramework/NativeCallProxy.h' file not found". The library's
    /// README calls this out as its step 5 — "change UnityFramework's target membership from
    /// Project to Public. Don't forget this step!" — illustrated with a screenshot of somebody
    /// clicking a dropdown in Xcode. CI has no hands, and the Xcode project is regenerated from
    /// scratch on every run, so the click has to be code.
    ///
    /// 🔴 Why this never throws, however wrong things look: the SAME post-processor runs in the
    /// `ios` job that ships fish-QC builds to TestFlight, and that channel must keep working while
    /// the RN merge is still being proven. A missing header makes an artifact the RN app cannot
    /// use; it does not make a bad TestFlight build. So this logs loudly and returns, and the
    /// ios_framework CI job fails the run instead by checking for Headers/NativeCallProxy.h in the
    /// built framework — a check on the ARTIFACT, which is the thing that actually has to be right.
    ///
    /// API notes, because a wrong name here costs a 40-minute CI round (the same trap as
    /// <c>ProvisioningProfileType</c> in CIBuild.cs, which lives in UnityEditor and not in
    /// UnityEditor.iOS). Every member used below was read off the Unity scripting reference for
    /// <c>iOS.Xcode.PBXProject</c> before it was written, not guessed:
    ///   • <c>PBXProject.GetPBXProjectPath(buildPath)</c>          → static, returns …/Unity-iPhone.xcodeproj/project.pbxproj
    ///   • <c>ReadFromFile / WriteToFile</c>                       → instance
    ///   • <c>GetUnityFrameworkTargetGuid()</c>                    → instance, the UnityFramework target
    ///   • <c>FindFileGuidByProjectPath(path)</c>                  → instance, path is project-relative
    ///   • <c>AddPublicHeaderToBuild(targetGuid, fileGuid)</c>     → instance, ARGUMENT ORDER IS (target, file)
    ///
    /// The whole file is behind <c>#if UNITY_IOS</c> for the reason
    /// <c>AndroidCameraPermission</c> is behind UNITY_ANDROID: the
    /// <c>UnityEditor.iOS.Xcode</c> assembly ships with the iOS build-support module only, and an
    /// editor image without it (the Android CI job's) would fail to compile every Editor script in
    /// the project over a using directive it cannot resolve.
    /// </summary>
#if UNITY_IOS
    public static class IosNativeCallProxyHeader
    {
        /// <summary>
        /// Where Unity puts <c>Assets/Plugins/iOS/*</c> in the generated Xcode project. The
        /// library's README names the same path from the other side ("the NativeCallProxy.h
        /// inside the Unity-iPhone/Libraries/Plugins/iOS folder").
        /// </summary>
        private const string HeaderProjectPath = "Libraries/Plugins/iOS/NativeCallProxy.h";

        /// <summary>Runs after <see cref="IosCameraUsage"/> (order 1) — they touch different files.</summary>
        [PostProcessBuild(2)]
        public static void OnPostProcessBuild(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS) return;

            string projPath = PBXProject.GetPBXProjectPath(path);
            if (!System.IO.File.Exists(projPath))
            {
                Debug.LogError("[Build] no project.pbxproj at " + projPath +
                               " — NativeCallProxy.h NOT exported; the RN pod will not compile");
                return;
            }

            var proj = new PBXProject();
            proj.ReadFromFile(projPath);

            string frameworkGuid = proj.GetUnityFrameworkTargetGuid();
            if (string.IsNullOrEmpty(frameworkGuid))
            {
                Debug.LogError("[Build] no UnityFramework target in the generated project — " +
                               "NativeCallProxy.h NOT exported");
                return;
            }

            string fileGuid = proj.FindFileGuidByProjectPath(HeaderProjectPath);
            if (string.IsNullOrEmpty(fileGuid))
            {
                // Nothing to make public means the plugin file never reached the project at all —
                // usually because Assets/Plugins/iOS/NativeCallProxy.h was deleted or its importer
                // was switched off for iOS. Name both the path looked for and the fix.
                Debug.LogError("[Build] " + HeaderProjectPath + " is not in the Xcode project — " +
                               "is Assets/Plugins/iOS/NativeCallProxy.h still there and enabled for iOS? " +
                               "The RN pod's #include <UnityFramework/NativeCallProxy.h> will fail.");
                return;
            }

            proj.AddPublicHeaderToBuild(frameworkGuid, fileGuid);
            proj.WriteToFile(projPath);

            Debug.Log("[Build] UnityFramework exports " + HeaderProjectPath +
                      " as a PUBLIC header (RN bridge contract)");
        }
    }
#endif
}
