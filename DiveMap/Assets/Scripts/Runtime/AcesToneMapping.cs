using DiveMap.Core;
using UnityEngine;

namespace DiveMap.Runtime
{
    /// <summary>
    /// The film curve, attached to the scene camera — WO-E3's third leg, after linear light and the
    /// web's own light rig.
    ///
    /// 🔴 WHAT IT IS FOR. The web renders every frame through ACES at exposure 1.05
    /// (builder.html:485) and this app rendered through nothing. That is not a stylistic
    /// difference: with no curve, a lit surface climbs a straight line and clips, so the top end of
    /// every shading gradient — the part that carries surface detail — is thrown away before it
    /// reaches the screen. The user reported it as the textures looking worse than the web's while
    /// the app was loading textures four times the size.
    ///
    /// 🔎 WHY AN IMAGE EFFECT AND NOT A GLOBAL SETTING. Two reasons, and both are constraints
    /// rather than preferences:
    ///
    ///   • The uGUI shell is a ScreenSpaceOverlay canvas (UiShell:149). Overlay canvases are
    ///     composited by Unity AFTER every camera and after image effects, so an OnRenderImage on
    ///     the scene camera cannot touch the UI. Tone mapping the UI would shift every panel and
    ///     every Thai glyph off its authored colour, which is a regression nobody asked for.
    ///   • Built-in RP has no tone mapping of its own, and the post-processing package is not in
    ///     this project's manifest. One blit is what is on the table, and one blit is enough.
    ///
    /// 🔎 IT MUST BE ABLE TO NOT RUN. This project has shipped magenta twice from a stripped
    /// shader, so: the material lives in Resources (and the shader in Always Included Shaders), the
    /// shader is checked with <c>isSupported</c> before it is ever used, and if anything is missing
    /// the component disables itself and the frame passes through untouched. A slightly flat frame
    /// is a bad picture; a magenta frame is a broken build.
    ///
    /// 🔎 AND IT MUST BE ABLE TO BE TURNED OFF ON PURPOSE. <see cref="Enabled"/> is static and is
    /// flipped by the QC depth pass so one CI run photographs the same scene with and without the
    /// curve. A/B in one run is the only comparison that is worth anything — two runs differ in
    /// more than the thing under test.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class AcesToneMapping : MonoBehaviour
    {
        /// <summary>Resource name of the material that keeps the shader out of the stripper.</summary>
        public const string MaterialResource = "DM_AcesToneMap";

        private static bool _enabled = true;

        /// <summary>
        /// Whether the curve is applied. Off = a plain blit, i.e. exactly what the app looked like
        /// before this component existed, so an A/B pair is a real A/B.
        /// </summary>
        public static bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        /// <summary>Exposure actually in force, so a log line can state it rather than imply it.</summary>
        public static float Exposure { get; private set; } = ToneMap.Exposure;

        private Material _mat;

        /// <summary>
        /// Attach (idempotently) to <paramref name="cam"/>. Returns the component, or null when the
        /// shader is missing or unsupported — in which case nothing is attached at all and the
        /// camera renders exactly as it did before.
        /// </summary>
        public static AcesToneMapping Attach(Camera cam)
        {
            if (cam == null) return null;
            AcesToneMapping existing = cam.GetComponent<AcesToneMapping>();
            if (existing != null) return existing;

            Material src = Resources.Load<Material>(MaterialResource);
            if (src == null || src.shader == null || !src.shader.isSupported)
            {
                Debug.LogWarning("[Tone] ACES DISABLED — " +
                                 (src == null ? "Resources/" + MaterialResource + " missing"
                                              : "shader not supported on this GPU") +
                                 " (frames render untone-mapped, not magenta)");
                return null;
            }

            var c = cam.gameObject.AddComponent<AcesToneMapping>();
            c._mat = new Material(src) { name = "DM_AcesToneMap (instance)" };
            Exposure = ToneMap.Exposure;
            if (c._mat.HasProperty("_Exposure")) c._mat.SetFloat("_Exposure", Exposure);
            Debug.Log($"[Tone] ACES on — exposure={Exposure:F2} (web builder.html:485) " +
                      $"colorSpace={QualitySettings.activeColorSpace} shader={src.shader.name}");
            return c;
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (!_enabled || _mat == null)
            {
                Graphics.Blit(source, destination);
                return;
            }
            Graphics.Blit(source, destination, _mat);
        }

        private void OnDestroy()
        {
            if (_mat != null) Destroy(_mat);
        }
    }
}
