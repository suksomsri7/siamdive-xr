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
        private const string ApplicationIdentifier = "com.siamdive.divemap";
        private const string ProductName = "DiveMap";
        private const string CompanyName = "SiamDive";
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
