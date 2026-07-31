#if UNITY_ANDROID
using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

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
