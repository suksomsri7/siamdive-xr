using System.IO;
using System.Xml;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
#if UNITY_ANDROID
using UnityEditor.Android;
#endif

namespace DiveMap.EditorTools
{
    /// <summary>
    /// Guarantees the Android build asks for the camera — AR is dead without it, and the failure
    /// is total and silent: <c>WebCamTexture.devices</c> simply comes back empty, which looks
    /// exactly like a phone with no camera.
    ///
    /// 🔎 Why a post-processor rather than <c>Assets/Plugins/Android/AndroidManifest.xml</c>:
    /// that file REPLACES Unity's main manifest, so it has to carry the launcher activity, the
    /// theme, the meta-data — everything. Get one line wrong and the app does not start at all,
    /// and this project cannot run an APK to find out. Editing the manifest Unity has already
    /// generated only ADDS, so the worst case is the permission is missing, not a dead app.
    ///
    /// Why not rely on Unity's automatic detection: it does normally add CAMERA when a project
    /// references <c>WebCamTexture</c>, but "normally" is not something this repo can verify from
    /// here, and the cost of being wrong is the whole AR feature. Declaring it explicitly is
    /// cheap and removes the question.
    ///
    /// The camera is declared NOT required, so phones without one can still install the app and
    /// use every other mode.
    /// </summary>
#if UNITY_ANDROID
    public sealed class AndroidCameraPermission : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 1;

        private const string Ns = "http://schemas.android.com/apk/res/android";

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string manifest = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifest))
            {
                Debug.LogWarning("[Build] no AndroidManifest at " + manifest + " — camera permission NOT added");
                return;
            }

            var doc = new XmlDocument();
            doc.Load(manifest);
            XmlElement root = doc.DocumentElement;
            if (root == null) return;

            bool added = Ensure(doc, root, "uses-permission", "android.permission.CAMERA", null);
            bool feature = Ensure(doc, root, "uses-feature", "android.hardware.camera", "false");

            if (added || feature) doc.Save(manifest);
            Debug.Log($"[Build] android manifest: camera permission {(added ? "added" : "already present")}, " +
                      $"feature {(feature ? "added" : "already present")}");
        }

        /// <summary>Add &lt;tag android:name="name"&gt; unless it is already there.</summary>
        private static bool Ensure(XmlDocument doc, XmlElement root, string tag, string name, string required)
        {
            foreach (XmlNode node in root.SelectNodes(tag))
            {
                var e = node as XmlElement;
                if (e != null && e.GetAttribute("name", Ns) == name) return false;
            }

            XmlElement added = doc.CreateElement(tag);
            added.SetAttribute("name", Ns, name);
            if (required != null) added.SetAttribute("required", Ns, required);
            root.AppendChild(added);
            return true;
        }
    }
}
#endif

    /// <summary>
    /// The iOS half of the same problem, and a harsher one: Android without the permission simply
    /// reports no cameras, but iOS TERMINATES the app the instant it touches the camera unless
    /// Info.plist explains why. No prompt, no log — the app just closes, which reads as "the build
    /// is broken" rather than "one plist key is missing".
    ///
    /// The text is what the user sees in the system dialog, so it says what the camera is FOR.
    /// Thai first: the app's audience is Thai divers, and iOS shows this string verbatim.
    /// </summary>
    public static class IosCameraUsage
    {
        public const string Reason =
            "ใช้กล้องเพื่อวางแผนที่ดำน้ำแบบ AR บนพื้นจริงตรงหน้าคุณ";

        [PostProcessBuild(1)]
        public static void OnPostProcessBuild(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS) return;

            string plist = Path.Combine(path, "Info.plist");
            if (!File.Exists(plist))
            {
                Debug.LogWarning("[Build] no Info.plist at " + plist + " — camera usage NOT declared");
                return;
            }

            string text = File.ReadAllText(plist);
            if (text.Contains("NSCameraUsageDescription"))
            {
                Debug.Log("[Build] ios Info.plist: camera usage already present");
                return;
            }

            // Insert before the closing </dict> of the root dictionary.
            int at = text.LastIndexOf("</dict>", System.StringComparison.Ordinal);
            if (at < 0)
            {
                Debug.LogWarning("[Build] Info.plist has no </dict> — camera usage NOT declared");
                return;
            }

            string entry = "\t<key>NSCameraUsageDescription</key>\n\t<string>" + Reason + "</string>\n";
            File.WriteAllText(plist, text.Insert(at, entry));
            Debug.Log("[Build] ios Info.plist: camera usage added");
        }
    }
}
