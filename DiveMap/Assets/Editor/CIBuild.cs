using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

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

                // Apple accepts a given build number exactly ONCE per version. Unity's default is
                // empty, which reaches App Store Connect as "0", so a second upload of version 1.0
                // is rejected as a duplicate — at the end of the run, after everything expensive
                // has already been paid for. The CI run number is monotonic and never reused, so
                // it is the build number.
                var buildNumber = Environment.GetEnvironmentVariable("IOS_BUILD_NUMBER");
                if (!string.IsNullOrWhiteSpace(buildNumber))
                {
                    PlayerSettings.iOS.buildNumber = buildNumber;
                    Debug.Log($"[CIBuild] build number {PlayerSettings.bundleVersion} ({buildNumber})");
                }

                // ProvisioningProfileType lives in UnityEditor, NOT UnityEditor.iOS — checked in
                // Unity's own source (Editor/Mono/PlayerSettingsIOS.bindings.cs), after guessing it
                // twice and paying for both: `UnityEditor.iOS.ProvisioningProfileType` fails to
                // compile on the Linux test image where no iOS module exists, and `using
                // UnityEditor.iOS` makes every plain `BuildPipeline` in this file ambiguous. Being
                // in the core editor assembly, this needs no platform guard.
                StampBuildNumber(buildNumber);
                if (!ArkitDefinePresent())
                {
                    Fail("UNITY_XR_ARKIT_LOADER_ENABLED is missing from the iOS scripting defines. " +
                         "Without it the ARKit native plug-in is left out of the Xcode project and " +
                         "the loader silently declines to start — the app would install and have no " +
                         "AR, which is the failure this build is here to stop. Add it under " +
                         "scriptingDefineSymbols/iPhone in ProjectSettings.asset.");
                    return;
                }
                if (!EnsureXrSettings(out string xrWhy))
                {
                    Fail("XR settings are not usable: " + xrWhy);
                    return;
                }
                PreloadXrSettings();

                var profileUuid = Environment.GetEnvironmentVariable("IOS_PROFILE_UUID");
                if (!string.IsNullOrWhiteSpace(profileUuid))
                {
                    PlayerSettings.iOS.iOSManualProvisioningProfileType = ProvisioningProfileType.Distribution;
                    PlayerSettings.iOS.iOSManualProvisioningProfileID = profileUuid;
                    Debug.Log($"[CIBuild] signing with App Store profile {profileUuid}");
                }
                else
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
        /// <summary>
        /// Put the CI build number where the running app can read it (Core.BuildStamp), so every
        /// screenshot says which build it came from. Written into Resources during the build only:
        /// the runner starts from a fresh checkout each time, so nothing is left behind in git.
        /// </summary>
        /// <summary>
        /// Put the XR settings into Preloaded Assets so <c>XRGeneralSettings.Instance</c> is not
        /// null at runtime.
        ///
        /// 🔴 This is what "ARKit OFF: ไม่พบการตั้งค่า XR (settings/manager = null)" on the phone
        /// was telling us. The asset exists, the loader is listed in it, EditorBuildSettings points
        /// at it — and none of that reaches the PLAYER. XR Management normally adds the asset to
        /// preloaded assets from a build hook that runs when the settings were created through its
        /// own window; these were hand-written (no Unity Editor on this machine), so that path
        /// never ran and the player booted with no XR settings at all. Every symptom followed from
        /// it: no plane detection, nothing anchored, the map floating with the camera — because the
        /// app quietly fell back to the attitude-only session, exactly as designed.
        ///
        /// Doing it here means it happens on every CI build regardless of who created the asset.
        /// </summary>
        private static void PreloadXrSettings()
        {
            const string path = "Assets/XR/Settings/XRGeneralSettings-iOS.asset";
            var settings = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (settings == null)
            {
                Debug.LogWarning("[CIBuild] no XR settings at " + path + " — AR will fall back");
                return;
            }

            var preloaded = new System.Collections.Generic.List<UnityEngine.Object>(
                PlayerSettings.GetPreloadedAssets());
            preloaded.RemoveAll(o => o == null);
            if (!preloaded.Contains(settings))
            {
                preloaded.Add(settings);
                PlayerSettings.SetPreloadedAssets(preloaded.ToArray());
            }
            Debug.Log($"[CIBuild] XR settings preloaded ({preloaded.Count} assets) — ARKit can start");
        }

        /// <summary>
        /// Fail the build NOW if the one define ARKit actually hangs off is missing.
        ///
        /// 🔴 This is the thing four TestFlight builds were spent not finding. Everything about the
        /// XR settings chain was correct — the asset, the loader in its list, EditorBuildSettings,
        /// preloaded assets — and ARKit still could not start, because none of that is what turns
        /// ARKit ON. <c>UNITY_XR_ARKIT_LOADER_ENABLED</c> is, and it was absent:
        ///
        ///   • every <c>DllImport("__Internal")</c> in the ARKit package is inside
        ///     <c>#if UNITY_XR_ARKIT_LOADER_ENABLED</c>, so without it <c>Api.AtLeast11_0()</c> is a
        ///     stub returning <c>false</c> → <c>ARKitSessionSubsystem.RegisterDescriptor()</c>
        ///     returns early → no descriptor exists → <c>ARKitLoader.Initialize()</c> finds no
        ///     session subsystem and returns false. No error, no exception: the loader simply
        ///     declines, and the app falls back to the gyroscope exactly as designed.
        ///   • <c>ARKitBuildProcessor</c> gates the copy of <c>libUnityARKit.a</c> into the Xcode
        ///     project on the same flag, so the native half was not even in the app.
        ///
        /// Unity's own editor sets this define from a coroutine that never runs in batch mode, so
        /// it has to be committed in ProjectSettings.asset instead. It cannot be repaired from
        /// here — the editor assemblies were compiled before this method runs — so the only useful
        /// thing to do is stop, loudly, two minutes in rather than after a 45-minute build that
        /// would install and quietly have no AR.
        /// </summary>
        private static bool ArkitDefinePresent()
        {
            string defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.iOS) ?? "";
            bool ok = Array.IndexOf(defines.Split(';'), "UNITY_XR_ARKIT_LOADER_ENABLED") >= 0;
            Debug.Log($"[CIBuild] iOS scripting defines = '{defines}' → ARKit {(ok ? "ENABLED" : "OFF")}");
            return ok;
        }

        // ── XR settings, built through Unity's own API ───────────────────────────
        //
        // 🔴 Five attempts were spent hand-writing these four .asset files, and build 199 finally
        // said why in one line: "Unity found NO iOS settings". The YAML was byte-plausible — right
        // script GUIDs, right BuildTargetGroup number, registered in EditorBuildSettings — and
        // Unity still would not resolve the chain, so ARKitBuildProcessor left libUnityARKit.a out
        // and the Xcode link failed on ~40 undefined _UnityARKit_* symbols.
        //
        // The mistake was the whole approach, not any one file. These assets are normally produced
        // by Unity's XR Plug-in Management window, which the build machine does not have — but the
        // CI RUNNER does have an Editor, and the same operations are public API. So the settings
        // are assembled here, at build time, by the code path Unity itself uses. Nothing then
        // depends on hand-written YAML deserialising correctly.
        //
        // Order matters and is not obvious: the loader has to exist as a saved asset before
        // TryAddLoader will keep it (XR Management stores a reference, not a copy), and the
        // config object has to be registered before ARKitBuildProcessor's own hook looks it up.
        private const string XrLoaderPath = "Assets/XR/Loaders/ARKitLoader.asset";
        private const string XrManagerPath = "Assets/XR/Settings/XRManagerSettings-iOS.asset";
        private const string XrGeneralPath = "Assets/XR/Settings/XRGeneralSettings-iOS.asset";
        private const string XrPerTargetPath = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";

        private static bool EnsureXrSettings(out string why)
        {
            why = null;
            // Reflection, not a direct reference: this file also compiles on the Linux test image
            // where no iOS module exists. The assembly scan is the fallback for the day the
            // assembly-qualified name stops resolving — a null here would otherwise read as
            // "the package is missing", which is a different and much more misleading problem.
            Type loaderType = Type.GetType("UnityEngine.XR.ARKit.ARKitLoader, Unity.XR.ARKit");
            if (loaderType == null)
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    loaderType = asm.GetType("UnityEngine.XR.ARKit.ARKitLoader");
                    if (loaderType != null) break;
                }
            }
            if (loaderType == null)
            {
                why = "ARKitLoader type not found — the com.unity.xr.arkit package did not load. " +
                      "Check Packages/manifest.json.";
                return false;
            }

            Directory.CreateDirectory(Path.Combine(Application.dataPath, "XR/Loaders"));
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "XR/Settings"));
            AssetDatabase.Refresh();   // CreateAsset needs the folders known to the AssetDatabase

            var loader = LoadOrCreate<UnityEngine.XR.Management.XRLoader>(XrLoaderPath, loaderType);
            var manager = LoadOrCreate<UnityEngine.XR.Management.XRManagerSettings>(XrManagerPath, null);
            var general = LoadOrCreate<UnityEngine.XR.Management.XRGeneralSettings>(XrGeneralPath, null);
            var perTarget = LoadOrCreate<UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget>(XrPerTargetPath, null);
            if (loader == null || manager == null || general == null || perTarget == null)
            {
                why = "could not create the XR settings assets under Assets/XR/";
                return false;
            }

            bool already = false;
            foreach (var l in manager.activeLoaders) if (l == loader) already = true;
            if (!already && !manager.TryAddLoader(loader))
            {
                why = "XRManagerSettings.TryAddLoader refused the ARKit loader";
                return false;
            }

            general.Manager = manager;
            perTarget.SetSettingsForBuildTarget(BuildTargetGroup.iOS, general);

            EditorUtility.SetDirty(loader);
            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(general);
            EditorUtility.SetDirty(perTarget);
            AssetDatabase.SaveAssets();
            EditorBuildSettings.AddConfigObject(
                UnityEngine.XR.Management.XRGeneralSettings.settingsKey, perTarget, true);

            // Verify through UNITY'S OWN lookup, not ours. Everything above can succeed while the
            // thing ARKitBuildProcessor actually calls still returns null — which is precisely the
            // failure this method exists to end, so it is checked rather than assumed.
            var seen = UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget
                .XRGeneralSettingsForBuildTarget(BuildTargetGroup.iOS);
            if (seen == null) { why = "XRGeneralSettingsForBuildTarget(iOS) still returns null"; return false; }
            if (seen.Manager == null) { why = "the iOS settings have no manager"; return false; }

            var names = new System.Collections.Generic.List<string>();
            foreach (var l in seen.Manager.activeLoaders) names.Add(l == null ? "null" : l.GetType().Name);
            if (!names.Contains("ARKitLoader"))
            {
                why = "no ARKitLoader in the iOS loader list — got [" + string.Join(", ", names) + "]";
                return false;
            }
            Debug.Log($"[CIBuild] XR iOS loaders = [{string.Join(", ", names)}] — libUnityARKit.a will be copied");
            return true;
        }

        /// <summary>
        /// The asset at <paramref name="path"/>, created if it is missing OR unreadable.
        /// A hand-written file that Unity cannot deserialise loads as null, and creating over it
        /// is the repair — leaving it in place is how five builds kept inheriting the same broken
        /// chain. <paramref name="concreteType"/> is for types this assembly must not name
        /// (ARKitLoader lives in an iOS-only assembly).
        /// </summary>
        private static T LoadOrCreate<T>(string path, Type concreteType) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null && (concreteType == null || existing.GetType() == concreteType))
                return existing;

            if (existing != null || File.Exists(path))
            {
                Debug.LogWarning($"[CIBuild] XR: replacing unusable asset {path}");
                AssetDatabase.DeleteAsset(path);
            }

            var made = concreteType != null
                ? ScriptableObject.CreateInstance(concreteType) as T
                : ScriptableObject.CreateInstance<T>();
            if (made == null) return null;

            AssetDatabase.CreateAsset(made, path);
            AssetDatabase.ImportAsset(path);
            Debug.Log($"[CIBuild] XR: created {path} ({made.GetType().Name})");
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        private static void StampBuildNumber(string buildNumber)
        {
            try
            {
                string dir = Path.Combine(Application.dataPath, "Resources");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, DiveMap.Core.BuildStamp.ResourceName + ".txt"),
                                  buildNumber ?? "");
                AssetDatabase.ImportAsset("Assets/Resources/" + DiveMap.Core.BuildStamp.ResourceName + ".txt");
                Debug.Log($"[CIBuild] stamped build number {buildNumber}");
            }
            catch (Exception e)
            {
                // A missing stamp is a worse screenshot, not a broken build.
                Debug.LogWarning("[CIBuild] could not stamp the build number: " + e.Message);
            }
        }

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
