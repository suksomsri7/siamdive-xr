using System.Collections.Generic;
using DiveMap.Core;
using UnityEngine;

namespace DiveMap.Runtime
{
    /// <summary>
    /// Read a normal map back through the GPU and ask the texels whether the sampler decoded them.
    ///
    /// 🔴 WHY THIS EXISTS. Two builds threw away the normal map on every model in the app on the
    /// strength of an INFERENCE — the project is in gamma, therefore glTFast never tags a texture
    /// as data, therefore KTX for Unity transcodes to an sRGB format, therefore the sampler
    /// decodes. Every step of that is true of the source code and none of it was ever observed on
    /// a device. Worse, the one number the log printed as evidence
    /// (<c>neutralTiltNow=38.8°</c>) was a CONSTANT — the tilt a neutral texel WOULD have if it
    /// were decoded — dressed up as a measurement. It said the same thing on a healthy map.
    ///
    /// The texels are right there and they settle it without any ground truth: a tangent-space
    /// normal map stores unit vectors, so whichever interpretation of the returned bytes yields
    /// unit vectors is the interpretation the sampler used. See
    /// <see cref="NormalMapDecode.Verdict"/>.
    ///
    /// 🔴 WHY IT BLITS INSTEAD OF READING THE TEXTURE. The texture is compressed (ETC2/ASTC) and
    /// not readable, so there is no CPU path to its bytes — and a CPU path would be the wrong
    /// measurement anyway. The question is not "what is stored", it is "what does the SAMPLER
    /// hand the shader", and only a sample can answer that. The blit goes through the same
    /// hardware path a fragment shader would.
    ///
    /// 🔴 WHY FOUR SMALL WINDOWS AND NOT ONE BIG DOWNSCALE. Blitting a 2048² texture into a 64²
    /// target makes the sampler pick mip 5 — each output pixel would be an average of 32×32
    /// texels, and averaging unit vectors SHORTENS them. The probe would report every map on
    /// earth as broken, including the healthy ones, and the mistake would look exactly like a
    /// confirmed diagnosis. Each window is instead blitted at 1:1 (scale = 64/width) so the UV
    /// step is one texel, the LOD is 0, and each output pixel is one real texel. Four of them,
    /// spread across the atlas, because one 64×64 corner of a 2048² sheet is 1/1024 of it and can
    /// easily be all gutter or all one flat chart.
    /// </summary>
    public static class NormalMapProbe
    {
        /// <summary>Side of each sampled window, in texels.</summary>
        public const int Window = 64;

        /// <summary>
        /// Where the windows are taken from, in normalised UV. Deliberately not on a grid line or
        /// a chart boundary, and not the exact centre — an atlas often has its largest island
        /// dead-centre, and four samples of the same island is one sample.
        /// </summary>
        private static readonly Vector2[] Offsets =
        {
            new Vector2(0.10f, 0.12f),
            new Vector2(0.37f, 0.63f),
            new Vector2(0.64f, 0.31f),
            new Vector2(0.86f, 0.82f),
        };

        /// <summary>
        /// One verdict per texture, for the life of the session.
        ///
        /// A map is shared by every material on the model and often by several models, and the
        /// answer cannot change once the texture exists — the sampler is not going to start
        /// decoding differently halfway through. Probing per material would multiply a
        /// GPU stall by the material count for no new information.
        /// </summary>
        private static readonly Dictionary<int, Result> Cache = new Dictionary<int, Result>();

        /// <summary>What the probe saw. <c>Fraction</c> values are 0..1, or −1 for "no sample".</summary>
        public readonly struct Result
        {
            public readonly NormalReadVerdict Verdict;
            public readonly double UnitAsRead;
            public readonly double UnitAfterUndo;
            public readonly int Texels;

            public Result(NormalReadVerdict verdict, double unitAsRead, double unitAfterUndo, int texels)
            {
                Verdict = verdict;
                UnitAsRead = unitAsRead;
                UnitAfterUndo = unitAfterUndo;
                Texels = texels;
            }

            public static Result None => new Result(NormalReadVerdict.Unknown, -1.0, -1.0, 0);

            public override string ToString()
                => $"readVerdict={Verdict} unitAsRead={UnitAsRead:0.000} " +
                   $"unitAfterUndo={UnitAfterUndo:0.000} texels={Texels}";
        }

        /// <summary>
        /// Measure <paramref name="texture"/>, or return the cached answer for it.
        ///
        /// Never throws and never leaves render state behind: a probe that broke the frame it was
        /// measuring would be worse than no probe, and this runs during scene load with the real
        /// camera one call away.
        /// </summary>
        public static Result Measure(Texture texture)
        {
            if (texture == null) return Result.None;
            int id = texture.GetInstanceID();
            if (Cache.TryGetValue(id, out Result cached)) return cached;

            Result result = Sample(texture);
            Cache[id] = result;
            return result;
        }

        private static Result Sample(Texture texture)
        {
            if (texture.width < Window || texture.height < Window) return Result.None;

            RenderTexture rt = null;
            Texture2D readback = null;
            RenderTexture previousActive = RenderTexture.active;
            FilterMode previousFilter = texture.filterMode;

            try
            {
                // Linear read-write: in a gamma project this is a plain untyped target, so the
                // value the sampler produced is what lands in the byte — no second conversion on
                // the way out to confuse the measurement with the thing being measured.
                rt = RenderTexture.GetTemporary(
                    Window, Window, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                readback = new Texture2D(Window, Window, TextureFormat.RGB24, false, true);

                // Point, so that even at 1:1 no neighbour bleeds in through bilinear rounding.
                texture.filterMode = FilterMode.Point;

                var scale = new Vector2(Window / (float)texture.width, Window / (float)texture.height);
                var pooled = new List<byte>(Offsets.Length * Window * Window * 3);

                foreach (Vector2 offset in Offsets)
                {
                    // Clamp so a window never runs off the edge and wraps into the far side.
                    var uv = new Vector2(
                        Mathf.Clamp(offset.x, 0f, 1f - scale.x),
                        Mathf.Clamp(offset.y, 0f, 1f - scale.y));

                    Graphics.Blit(texture, rt, scale, uv);
                    RenderTexture.active = rt;
                    readback.ReadPixels(new Rect(0f, 0f, Window, Window), 0, 0, false);
                    readback.Apply(false);
                    pooled.AddRange(readback.GetRawTextureData());
                }

                byte[] pixels = pooled.ToArray();
                double asRead = NormalMapDecode.UnitFraction(pixels, undoSrgb: false);
                double undone = NormalMapDecode.UnitFraction(pixels, undoSrgb: true);
                return new Result(NormalMapDecode.Verdict(pixels), asRead, undone, pixels.Length / 3);
            }
            catch (System.Exception e)
            {
                // Unknown, not "broken". A probe that fails must not be able to condemn a map —
                // that is how the previous version of this decision went wrong.
                Debug.LogWarning("[Shading] normal-map probe failed, falling back to the colour " +
                                 "space rule: " + e.Message);
                return Result.None;
            }
            finally
            {
                RenderTexture.active = previousActive;
                if (texture != null) texture.filterMode = previousFilter;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (readback != null) Object.Destroy(readback);
            }
        }

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnLoad() => Cache.Clear();
#endif
    }
}
