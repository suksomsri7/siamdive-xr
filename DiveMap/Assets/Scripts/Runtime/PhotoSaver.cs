using System;
using UnityEngine;

namespace DiveMap.Runtime
{
    /// <summary>
    /// Puts a captured frame where the user expects to find it: the phone's own gallery.
    ///
    /// Android 10+ (API 29) exposes MediaStore with scoped storage — an app writes an image by
    /// inserting a row into <c>MediaStore.Images.Media.EXTERNAL_CONTENT_URI</c> and streaming
    /// bytes into the returned URI. No permission is needed for the app's own inserts, which is
    /// why this path is preferred over WRITE_EXTERNAL_STORAGE.
    ///
    /// Everything is best-effort with a file fallback: this is the one piece of the app that
    /// CANNOT be verified in CI (no device, no gallery), so it is written to degrade to a plain
    /// file in the app's folder and to report which path it took, rather than to assume.
    /// </summary>
    public static class PhotoSaver
    {
        /// <summary>Where the bytes ended up, for the toast and the log.</summary>
        public enum Result { Gallery, AppFolder, Failed }

        /// <summary>
        /// Save <paramref name="jpg"/> as <paramref name="fileName"/>. Returns where it landed and,
        /// through <paramref name="where"/>, a human-readable location.
        /// </summary>
        public static Result Save(byte[] jpg, string fileName, out string where)
        {
            where = null;
            if (jpg == null || jpg.Length == 0) return Result.Failed;

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                if (SaveToGallery(jpg, fileName, out where)) return Result.Gallery;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Photo] gallery insert failed ({e.Message}) — falling back to the app folder");
            }
#endif
            try
            {
                string path = System.IO.Path.Combine(Application.persistentDataPath, fileName);
                System.IO.File.WriteAllBytes(path, jpg);
                where = path;
                return Result.AppFolder;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Photo] file write failed: {e.Message}");
                return Result.Failed;
            }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static bool SaveToGallery(byte[] jpg, string fileName, out string where)
        {
            where = null;

            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (AndroidJavaObject resolver = activity.Call<AndroidJavaObject>("getContentResolver"))
            using (var media = new AndroidJavaClass("android.provider.MediaStore$Images$Media"))
            using (AndroidJavaObject external = media.GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI"))
            using (var values = new AndroidJavaObject("android.content.ContentValues"))
            {
                values.Call("put", "_display_name", fileName);
                values.Call("put", "mime_type", "image/jpeg");
                // Scoped storage (API 29+): RELATIVE_PATH puts it in Pictures/DiveMap. On older
                // devices the column does not exist and the insert simply ignores it.
                values.Call("put", "relative_path", "Pictures/DiveMap");

                using (AndroidJavaObject uri = resolver.Call<AndroidJavaObject>("insert", external, values))
                {
                    if (uri == null) return false;

                    using (AndroidJavaObject stream = resolver.Call<AndroidJavaObject>("openOutputStream", uri))
                    {
                        if (stream == null) return false;
                        // Java's OutputStream.write(byte[]) — Unity marshals a C# sbyte[] to a
                        // Java byte[]; passing byte[] would bind to write(int).
                        var signed = new sbyte[jpg.Length];
                        Buffer.BlockCopy(jpg, 0, signed, 0, jpg.Length);
                        stream.Call("write", signed);
                        stream.Call("flush");
                        stream.Call("close");
                    }

                    where = uri.Call<string>("toString");
                    Debug.Log($"[Photo] saved to the gallery: {where}");
                    return true;
                }
            }
        }
#endif
    }
}
