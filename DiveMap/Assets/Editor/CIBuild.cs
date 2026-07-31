using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
// The EditMode tests run on a Linux image that has no iOS build support installed, so this
// namespace does not exist there at all — an unguarded reference fails the whole test job with
// CS0234 before a single test runs. Same guard the Android half of the build already uses.
#if UNITY_IOS
using UnityEditor.iOS;
#endif

namespace DiveMap.EditorTools
{
    /// <summary>
    /// Batch-mode build entry point invoked by GameCI (game-ci/unity-builder@v4)
    /// via -buildMethod CIBuild.BuildAndroid.
    ///
    /// All PlayerSettings that must be deterministic are applied HERE (not
    /// hand-authored in ProjectSettings.asset) because Unity regenerates
    /// default ProjectSettings in batchmode and would clobber hand edits.
    /// </summary>
    public static class CIBuild
    {
        /// <summary>
        /// Bundle id. Overridable with APPLICATION_ID because this app is meant to REPLACE the
        /// live SIAMDIVE app (com.siamdive.app, App Store 6787005046) eventually, and Apple does
        /// not let an app change its bundle id after the first upload. So the switch cannot be
        /// "edit the record later" — it has to be this build producing com.siamdive.app and going
        /// to that record as an update, or existing users keep the old app forever.
        ///
        /// Until then it ships under its own id so TestFlight cannot touch the live listing.
        ///
        /// ⚠️ Before that switch: the two apps do NOT agree on who the user is. This one makes a
        /// device id from PlayerPrefs/SystemInfo (WalletClient.cs:50); the React Native app has
        /// its own in AsyncStorage. Every map, coin and favourite on the server is keyed to that
        /// id, so an update that changes it shows the user an empty account — data intact on the
        /// server, key lost on the phone. That has to be solved first, not noticed on release day.
        /// </summary>
        private static string ApplicationIdentifier =>
            Environment.GetEnvironmentVariable("APPLICATION_ID") is string s && !string.IsNullOrWhiteSpace(s)
                ? s.Trim() : "com.siamdive.divemap";
        private const string ProductName = "DiveMap";
        // The legal entity Apple and Google have on file (Apple Team 3DD2VCN6JQ). Stores show
        // this next to the app, so "SiamDive" — which is nobody's registered name — would have
        // had to be corrected during review rather than before it.
        private const string CompanyName = "SIAM DIVE CENTER COMPANY LIMITED";
        private const string DefaultOutputPath = "Build/DiveMap.apk";

        // Called by CI:  -buildMethod CIBuild.BuildAndroid
        public static void BuildAndroid()
        {
            try
            {
                ConfigurePlayerSettings();

                string outputPath = ResolveOutputPath();
                EnsureParentDirectory(outputPath);

                var scenes = ResolveScenes();
                if (scenes.Length == 0)
                {
                    Fail("No enabled scenes found in EditorBuildSettings. Aborting build.");
                    return;
                }

                var options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = outputPath,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = BuildOptions.None,
                };

                Debug.Log($"[CIBuild] Building Android APK -> {outputPath}");
                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary summary = report.summary;

                if (summary.result == BuildResult.Succeeded)
                {
                    Debug.Log(
                        $"[CIBuild] Build succeeded: {summary.totalSize} bytes in {summary.totalTime}. " +
                        $"Output: {summary.outputPath}");
                    // Explicit clean exit so batchmode returns 0.
                    EditorApplication.Exit(0);
                }
                else
                {
                    Fail($"Build failed: result={summary.result}, errors={summary.totalErrors}, " +
                         $"warnings={summary.totalWarnings}.");
                }
            }
            catch (Exception ex)
            {
                Fail($"Unhandled exception during build: {ex}");
            }
        }

        // Called by CI:  -buildMethod DiveMap.EditorTools.CIBuild.BuildWindows
        // Windows player (Mono — IL2CPP cross-compile จาก Linux ไป Windows ทำไม่ได้)
        // ใช้เทสบน Windows Server ผ่าน RDP: เมาส์ควบคุมได้ (OrbitCamera มี mouse fallback)
        public static void BuildWindows()
        {
            try
            {
                PlayerSettings.productName = ProductName;
                PlayerSettings.companyName = CompanyName;
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
                // หน้าต่างธรรมดา 1280x720 — เหมาะกับ RDP (fullscreen บน RDP มักค้าง/สลับจอลำบาก)
                PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
                PlayerSettings.defaultScreenWidth = 1280;
                PlayerSettings.defaultScreenHeight = 720;
                PlayerSettings.resizableWindow = true;
                PlayerSettings.runInBackground = true;

                string outputPath = ResolveOutputPathWithExt(".exe", "Build/Windows/DiveMap.exe");
                EnsureParentDirectory(outputPath);

                var scenes = ResolveScenes();
                if (scenes.Length == 0)
                {
                    Fail("No enabled scenes found in EditorBuildSettings. Aborting build.");
                    return;
                }

                var options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = outputPath,
                    target = BuildTarget.StandaloneWindows64,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = BuildOptions.None,
                };

                Debug.Log($"[CIBuild] Building Windows player -> {outputPath}");
                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary summary = report.summary;

                if (summary.result == BuildResult.Succeeded)
                {
                    Debug.Log($"[CIBuild] Build succeeded: {summary.totalSize} bytes in {summary.totalTime}. Output: {summary.outputPath}");
                    EditorApplication.Exit(0);
                }
                else
                {
                    Fail($"Build failed: result={summary.result}, errors={summary.totalErrors}, warnings={summary.totalWarnings}.");
                }
            }
            catch (Exception ex)
            {
                Fail($"Unhandled exception during build: {ex}");
            }
        }

        // Called by CI:  -buildMethod DiveMap.EditorTools.CIBuild.BuildIos
        //
        // Produces an Xcode PROJECT, not an .ipa — Unity's iOS target always does. The macOS
        // runner then archives and signs it (fastlane), which is the only place a signing identity
        // exists. Splitting it that way also means a Unity error and a signing error look
        // different in the log instead of both reading as "iOS build failed".
        //
        // Nothing here touches the camera permission: that is Info.plist, written after Xcode
        // generation by IosCameraUsage. Without it iOS terminates the app the moment AR opens.
        public static void BuildIos()
        {
            try
            {
                PlayerSettings.applicationIdentifier = ApplicationIdentifier;
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, ApplicationIdentifier);
                PlayerSettings.productName = ProductName;
                PlayerSettings.companyName = CompanyName;

                // Apple requires a 64-bit ARM build; Unity only offers IL2CPP for iOS anyway, but
                // stating it keeps the build honest if a default ever changes.
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
                PlayerSettings.SetArchitecture(NamedBuildTarget.iOS, 1);   // ARM64

                // The CERTIFICATE stays the runner's job — it lives in a keychain, never here. What
                // has to be decided at generation time is WHICH PROFILE each target signs with,
                // because that is written per target inside the Xcode project and cannot be
                // supplied from the command line afterwards: xcodebuild build settings apply to
                // every target at once, and UnityFramework must NOT carry the app's profile.
                //
                // Automatic signing is not an option here even though it sounds easier. Without an
                // Apple ID inside the Unity step it defaults to a DEVELOPMENT profile, and Xcode
                // then refuses the distribution identity the runner supplies:
                //   "Unity-iPhone is automatically signed for development, but a conflicting code
                //    signing identity iPhone Distribution has been manually specified"
                // TestFlight only accepts distribution, so the profile is named outright.
                PlayerSettings.iOS.appleEnableAutomaticSigning = false;
                PlayerSettings.iOS.appleDeveloperTeamID = Environment.GetEnvironmentVariable("APPLE_TEAM_ID") ?? "";

                var profileUuid = Environment.GetEnvironmentVariable("IOS_PROFILE_UUID");
                bool profileApplied = false;
#if UNITY_IOS
                if (!string.IsNullOrWhiteSpace(profileUuid))
                {
                    PlayerSettings.iOS.iOSManualProvisioningProfileType = ProvisioningProfileType.Distribution;
                    PlayerSettings.iOS.iOSManualProvisioningProfileID = profileUuid;
                    Debug.Log($"[CIBuild] signing with App Store profile {profileUuid}");
                    profileApplied = true;
                }
#endif
                if (!profileApplied)
                {
                    // Local builds open in Xcode and get signed by hand, so this is a warning and
                    // not a failure — but on CI it is the difference between a TestFlight build and
                    // 12 minutes of IL2CPP thrown away at the archive step.
                    Debug.LogWarning("[CIBuild] IOS_PROFILE_UUID not set — the Xcode project will " +
                                     "have no provisioning profile and cannot be archived unattended.");
                }

                // iOS 13 is the floor Metal + this Unity version want, and is old enough to cover
                // any iPhone or iPad still receiving updates.
                PlayerSettings.iOS.targetOSVersionString = "13.0";
                PlayerSettings.iOS.requiresFullScreen = true;

                // The camera reason string ALSO lives in PlayerSettings; IosCameraUsage writes it
                // into the generated plist as well because a Unity version that ignores this field
                // would take AR down with it and nothing would say why.
                PlayerSettings.iOS.cameraUsageDescription = IosCameraUsage.Reason;

                string outputPath = ResolveOutputPathDir("Build/iOS");
                EnsureParentDirectory(Path.Combine(outputPath, "placeholder"));

                var scenes = ResolveScenes();
                if (scenes.Length == 0)
                {
                    Fail("No enabled scenes found in EditorBuildSettings. Aborting build.");
                    return;
                }

                var options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = outputPath,
                    target = BuildTarget.iOS,
                    targetGroup = BuildTargetGroup.iOS,
                    options = BuildOptions.None,
                };

                Debug.Log($"[CIBuild] Building iOS Xcode project -> {outputPath} " +
                          $"team='{PlayerSettings.iOS.appleDeveloperTeamID}' id={ApplicationIdentifier}");
                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary summary = report.summary;

                if (summary.result == BuildResult.Succeeded)
                {
                    Debug.Log($"[CIBuild] Xcode project written in {summary.totalTime}: {summary.outputPath}");
                    EditorApplication.Exit(0);
                }
                else
                {
                    Fail($"Build failed: result={summary.result}, errors={summary.totalErrors}.");
                }
            }
            catch (Exception ex)
            {
                Fail($"Unhandled exception during iOS build: {ex}");
            }
        }

        /// <summary>
        /// Output DIRECTORY (iOS writes a folder, not a file).
        ///
        /// Reads DM_BUILD_PATH, not BUILD_PATH: game-ci/unity-builder exports BUILD_PATH itself
        /// and its value wins, so ours silently became the action's relative "build/iOS" — which,
        /// with Unity's working directory set to the project folder, put the Xcode project
        /// somewhere no later step was looking.
        /// </summary>
        private static string ResolveOutputPathDir(string fallback)
        {
            string p = Environment.GetEnvironmentVariable("DM_BUILD_PATH");
            return string.IsNullOrWhiteSpace(p) ? fallback : p;
        }

        // Called by CI:  -buildMethod DiveMap.EditorTools.CIBuild.BuildLinux
        // Linux player สำหรับ QC screenshot อัตโนมัติบน CI (xvfb + llvmpipe)
        public static void BuildLinux()
        {
            try
            {
                PlayerSettings.productName = ProductName;
                PlayerSettings.companyName = CompanyName;
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
                PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
                PlayerSettings.defaultScreenWidth = 1280;
                PlayerSettings.defaultScreenHeight = 720;
                PlayerSettings.runInBackground = true;

                // BUILD_PATH จาก unity-builder = โฟลเดอร์ → ต่อชื่อไบนารีเสมอ (Linux ไม่มีนามสกุล)
                string fromEnv = Environment.GetEnvironmentVariable("BUILD_PATH");
                string dir = string.IsNullOrWhiteSpace(fromEnv) ? "Build/Linux" : fromEnv.Trim().TrimEnd('/', '\\');
                string outputPath = dir + "/DiveMap";
                EnsureParentDirectory(outputPath);

                var scenes = ResolveScenes();
                if (scenes.Length == 0) { Fail("No enabled scenes."); return; }

                var options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = outputPath,
                    target = BuildTarget.StandaloneLinux64,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = BuildOptions.None,
                };

                Debug.Log($"[CIBuild] Building Linux player -> {outputPath}");
                BuildReport report = BuildPipeline.BuildPlayer(options);
                if (report.summary.result == BuildResult.Succeeded)
                {
                    Debug.Log($"[CIBuild] Build succeeded: {report.summary.totalSize} bytes. Output: {report.summary.outputPath}");
                    EditorApplication.Exit(0);
                }
                else Fail($"Build failed: result={report.summary.result}, errors={report.summary.totalErrors}.");
            }
            catch (Exception ex) { Fail($"Unhandled exception during build: {ex}"); }
        }

        private static void ConfigurePlayerSettings()
        {
            PlayerSettings.applicationIdentifier = ApplicationIdentifier;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, ApplicationIdentifier);
            PlayerSettings.productName = ProductName;
            PlayerSettings.companyName = CompanyName;

            // IL2CPP + ARM64 (required for modern Android / Play + Android XR).
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            // Android 8.0 (API 26) minimum.
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;

            // Vulkan-first graphics (URP on Android XR). Disable auto so Vulkan is chosen.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[]
            {
                UnityEngine.Rendering.GraphicsDeviceType.Vulkan,
            });
        }

        private static string ResolveOutputPath()
        {
            // unity-builder (GameCI) ตั้ง BUILD_PATH เป็น "โฟลเดอร์" (เช่น build/Android)
            // ไม่ใช่ path ไฟล์ — ถ้าไม่ลงท้าย .apk ให้ต่อชื่อไฟล์เสมอ ไม่งั้น
            // BuildPlayer เขียน output ไร้นามสกุล แล้ว artifact glob *.apk หาไม่เจอ
            // (บทเรียนจริง run 29620954253)
            string fromEnv = Environment.GetEnvironmentVariable("BUILD_PATH");
            string path = string.IsNullOrWhiteSpace(fromEnv) ? DefaultOutputPath : fromEnv.Trim();
            if (!path.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
                path = path.TrimEnd('/', '\\') + "/DiveMap.apk";
            return path;
        }

        private static string ResolveOutputPathWithExt(string ext, string defaultPath)
        {
            string fromEnv = Environment.GetEnvironmentVariable("BUILD_PATH");
            string path = string.IsNullOrWhiteSpace(fromEnv) ? defaultPath : fromEnv.Trim();
            if (!path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                path = path.TrimEnd('/', '\\') + "/DiveMap" + ext;
            return path;
        }

        private static void EnsureParentDirectory(string outputPath)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        private static string[] ResolveScenes()
        {
            // Force the build scene list deterministically to Main.unity so CI never
            // depends on whatever happens to be checked in EditorBuildSettings.
            const string mainScene = "Assets/Scenes/Main.unity";
            if (File.Exists(mainScene))
            {
                EditorBuildSettings.scenes = new[]
                {
                    new EditorBuildSettingsScene(mainScene, true),
                };
            }

            var enabled = new System.Collections.Generic.List<string>();
            foreach (var s in EditorBuildSettings.scenes)
            {
                if (s.enabled)
                {
                    enabled.Add(s.path);
                }
            }
            return enabled.ToArray();
        }

        private static void Fail(string message)
        {
            Debug.LogError($"[CIBuild] {message}");
            EditorApplication.Exit(1);
        }
    }
}
