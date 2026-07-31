using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using DiveMap.Core;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// 📷 The map's cover image — the web's <c>captureThumb()</c>, called by <c>doSave</c> when
    /// the map has no cover yet.
    ///
    /// Three things it has to get right, all of them learned from what the picture is FOR:
    ///  • **The UI must not be in it.** A cover with a joystick and a coin badge across it is
    ///    unusable as a card image, so every overlay is hidden for the one frame that is read.
    ///  • **It is read at the end of the frame**, not on demand. Reading the framebuffer mid-frame
    ///    gives a half-drawn image on some drivers.
    ///  • **It is downscaled before upload.** The hub draws it at ~330×200; sending a 1080p PNG
    ///    would cost the player a megabyte and the CDN a slot, for pixels nobody sees.
    /// </summary>
    public static class ThumbnailCapture
    {
        /// <summary>Cover size. The hub's card image is 165×100 CSS px at 2× — 330×200.</summary>
        public const int Width = 480;
        public const int Height = 300;

        /// <summary>QC: the url the last capture produced, or null.</summary>
        public static string LastUrl { get; private set; }

        /// <summary>
        /// Capture, upload, and point the map at the result. <paramref name="onDone"/> gets the
        /// url or null.
        /// </summary>
        public static IEnumerator CaptureAndSave(System.Action<string> onDone)
        {
            var boot = Object.FindFirstObjectByType<AppBoot>();
            if (boot == null || string.IsNullOrEmpty(boot.CurrentMapId))
            {
                onDone?.Invoke(null);
                yield break;
            }

            // Hide the chrome for exactly one frame. Everything that draws over the scene goes,
            // and comes back regardless of what happens below.
            RectTransform overlay = UiShell.Instance != null ? UiShell.Instance.OverlayRoot : null;
            bool hadOverlay = overlay != null && overlay.gameObject.activeSelf;
            if (overlay != null) overlay.gameObject.SetActive(false);

            byte[] png = null;
            yield return new WaitForEndOfFrame();
            try
            {
                png = Grab();
            }
            finally
            {
                if (overlay != null) overlay.gameObject.SetActive(hadOverlay);
            }

            if (png == null || png.Length == 0)
            {
                Debug.LogWarning("[Thumb] capture produced nothing");
                onDone?.Invoke(null);
                yield break;
            }

            string url = null;
            yield return Upload(png, u => url = u);
            if (string.IsNullOrEmpty(url))
            {
                Toast.ShowTr("อัปโหลดไม่สำเร็จ");
                onDone?.Invoke(null);
                yield break;
            }

            LastUrl = url;
            MapSaveClient.Result result = default;
            yield return MapSaveClient.SetThumbnail(boot.CurrentMapId, url, r => result = r);

            if (result.Ok && boot.CurrentScene != null) boot.CurrentScene.Root["thumbUrl"] = url;
            Debug.Log($"[Thumb] saved ok={result.Ok} url={url}");
            Toast.ShowTr(result.Ok ? "ตั้งรูปหน้าปกแล้ว" : "บันทึกไม่สำเร็จ");
            onDone?.Invoke(result.Ok ? url : null);
        }

        /// <summary>Read the framebuffer and scale it down to cover size.</summary>
        private static byte[] Grab()
        {
            int w = Screen.width, h = Screen.height;
            if (w <= 0 || h <= 0) return null;

            var full = new Texture2D(w, h, TextureFormat.RGB24, false);
            full.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            full.Apply();

            // Crop to the cover's aspect before scaling, so the picture is not squashed.
            float want = Width / (float)Height;
            int cw = w, ch = Mathf.RoundToInt(w / want);
            if (ch > h) { ch = h; cw = Mathf.RoundToInt(h * want); }
            int x0 = (w - cw) / 2, y0 = (h - ch) / 2;

            var small = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    int sx = x0 + Mathf.Clamp(Mathf.RoundToInt(x / (float)Width * cw), 0, cw - 1);
                    int sy = y0 + Mathf.Clamp(Mathf.RoundToInt(y / (float)Height * ch), 0, ch - 1);
                    small.SetPixel(x, y, full.GetPixel(sx, sy));
                }
            small.Apply();

            byte[] png = small.EncodeToPNG();
            Object.Destroy(full);
            Object.Destroy(small);
            return png;
        }

        private static IEnumerator Upload(byte[] png, System.Action<string> onDone)
        {
            var form = new List<IMultipartFormSection>
            {
                new MultipartFormFileSection("file", png, "cover.png", "image/png"),
            };

            using (UnityWebRequest req = UnityWebRequest.Post(
                       MapApiClient.DefaultBaseUrl + "/api/dive-sites/media", form))
            {
                req.timeout = 40;
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[Thumb] upload failed ({req.responseCode}) {req.error}");
                    onDone?.Invoke(null);
                    yield break;
                }
                try
                {
                    JObject o = JObject.Parse(req.downloadHandler.text);
                    onDone?.Invoke((string)o["url"]);
                }
                catch { onDone?.Invoke(null); }
            }
        }
    }
}
