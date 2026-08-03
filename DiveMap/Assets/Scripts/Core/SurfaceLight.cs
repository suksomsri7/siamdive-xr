using System;

namespace DiveMap.Core
{
    /// <summary>
    /// What actually comes off a surface — albedo × the light that reaches it, through the tone
    /// curve, in bytes. The one place the whole chain is written down as arithmetic instead of as
    /// four files that each know a third of it.
    ///
    /// 🔴 WHY IT EXISTS. Two separate reports, and neither could be argued about because nobody
    /// could compute the answer without a 35-minute CI round and a screenshot:
    ///
    ///   1. "รูปปั้นสิงห์ดำทั้งก้อน" — a statue whose atlas averages sRGB 108-202 rendering as
    ///      19.93% pure black. The mechanism is in <see cref="UnderwaterLight.GroundBandAt"/>: a
    ///      down-facing surface sees only the ambient ground band, the band had been dimmed under
    ///      <see cref="ToneMap.BlackFloor"/>, and everything under that is byte 0 by construction.
    ///      <see cref="CrushAlbedoSrgb"/> turns that into one number a human can check: the
    ///      darkest base colour that can still make it off an underside at a given depth.
    ///
    ///   2. "เมื่อโดนแสงไฟฉาย สีเดิมจะกลับมา" — the user's physics, and it is correct.
    ///      Absorption follows the path the light travels through water, so the ambient (surface →
    ///      object, tens of metres) loses its red and a lamp (lamp → object → eye, a few metres)
    ///      does not. <see cref="DepthLight.PathTransmittance"/> carries the reasoning; this file
    ///      is where "switch the lamp on and the red comes back" becomes a number a test asserts.
    ///
    /// 🔎 WHAT IT IS NOT. It is not a renderer. It models a surface facing one of three ways under
    /// Unity's Trilight ambient plus, optionally, one headlamp square on it — no shadows, no
    /// specular, no reflection cube, no sun (the sun is a direction, and every case that matters
    /// here is a face the sun cannot see; that is what "down-facing" means). So the bytes it
    /// returns are a FLOOR on what the app renders, not a prediction of it. That is the useful
    /// direction: if this says a surface is black, the app cannot rescue it with anything.
    /// </summary>
    public static class SurfaceLight
    {
        /// <summary>Which of Unity's three ambient bands a surface is looking into.</summary>
        public enum Facing
        {
            /// <summary>Straight up — the sky band.</summary>
            Up,
            /// <summary>Horizontal — the equator band. A flank, a wall, a hull side.</summary>
            Side,
            /// <summary>Straight down — the ground band, and the only light such a face gets.</summary>
            Down,
        }

        /// <summary>No lamp on this surface. Reads better than a magic −1 at every call site.</summary>
        public const float NoLamp = float.PositiveInfinity;

        /// <summary>The ambient band a surface facing this way receives, authored (sRGB).</summary>
        public static SeabedGeom.Rgb Ambient(float depthUnits, Facing facing)
        {
            switch (facing)
            {
                case Facing.Up:   return UnderwaterLight.SkyBandAt(depthUnits);
                case Facing.Side: return UnderwaterLight.EquatorBandAt(depthUnits);
                default:          return UnderwaterLight.GroundBandAt(depthUnits);
            }
        }

        /// <summary>
        /// Total scene-linear irradiance on the surface: the ambient band, plus a headlamp
        /// <paramref name="lampDistanceUnits"/> away aimed square at it (<see cref="NoLamp"/> for
        /// none).
        ///
        /// The lamp term carries NO depth attenuation — that is the decision, and
        /// <see cref="DepthLight.PathTransmittance"/> is where the measurement behind it lives.
        /// </summary>
        public static void Irradiance(float depthUnits, Facing facing, float lampDistanceUnits,
                                      out float r, out float g, out float b)
        {
            SeabedGeom.Rgb band = Ambient(depthUnits, facing);
            r = ToneMap.SrgbToLinear(band.R);
            g = ToneMap.SrgbToLinear(band.G);
            b = ToneMap.SrgbToLinear(band.B);

            float f = DiveLightMath.LampFalloff(lampDistanceUnits);
            if (f <= 0f) return;

            float k = DiveLightMath.LampIntensity * f;
            r += k * ToneMap.SrgbToLinear(DiveLightMath.LampColor.R);
            g += k * ToneMap.SrgbToLinear(DiveLightMath.LampColor.G);
            b += k * ToneMap.SrgbToLinear(DiveLightMath.LampColor.B);
        }

        /// <summary>
        /// Scene-linear radiance off a surface whose base colour is <paramref name="albedoSrgb"/>
        /// (authored sRGB, the byte in the texture) — irradiance × albedo, both in light.
        /// </summary>
        public static void Radiance(SeabedGeom.Rgb albedoSrgb, float depthUnits, Facing facing,
                                    float lampDistanceUnits,
                                    out float r, out float g, out float b)
        {
            Irradiance(depthUnits, facing, lampDistanceUnits, out r, out g, out b);
            r *= ToneMap.SrgbToLinear(albedoSrgb.R);
            g *= ToneMap.SrgbToLinear(albedoSrgb.G);
            b *= ToneMap.SrgbToLinear(albedoSrgb.B);
        }

        /// <summary>The bytes a screenshot would hold for that surface — ACES, then the encode.</summary>
        public static void Bytes(SeabedGeom.Rgb albedoSrgb, float depthUnits, Facing facing,
                                 float lampDistanceUnits,
                                 out byte r, out byte g, out byte b)
        {
            Radiance(albedoSrgb, depthUnits, facing, lampDistanceUnits,
                     out float lr, out float lg, out float lb);
            ToneMap.Aces(lr, lg, lb, out float ar, out float ag, out float ab);
            r = ToneMap.LinearToByte(ar);
            g = ToneMap.LinearToByte(ag);
            b = ToneMap.LinearToByte(ab);
        }

        /// <summary>True when the surface renders as EXACTLY (0,0,0) — what
        /// <see cref="QcPixels.Shot.BlackOfSubjectPercent"/> counts.</summary>
        public static bool IsBlack(SeabedGeom.Rgb albedoSrgb, float depthUnits, Facing facing,
                                  float lampDistanceUnits = NoLamp)
        {
            Bytes(albedoSrgb, depthUnits, facing, lampDistanceUnits,
                  out byte r, out byte g, out byte b);
            return r == 0 && g == 0 && b == 0;
        }

        /// <summary>
        /// 🔴 THE NUMBER THE WHOLE WORK ORDER IS SCORED ON: the darkest NEUTRAL base colour, as an
        /// sRGB byte, that still comes off a surface facing this way at this depth without being
        /// pure black. Everything below it is (0,0,0) no matter what the model does.
        ///
        /// Shipped band, down-facing, QC staging depth (23.4 m): 72 — and 47.9% of the Singha
        /// atlas is darker than 71. That is not a model problem; it is a light problem with a
        /// receipt.
        /// </summary>
        public static int CrushAlbedoSrgb(float depthUnits, Facing facing,
                                          float lampDistanceUnits = NoLamp)
        {
            for (int v = 0; v <= 255; v++)
            {
                float s = v / 255f;
                if (!IsBlack(new SeabedGeom.Rgb(s, s, s), depthUnits, facing, lampDistanceUnits))
                    return v;
            }
            return 256;   // nothing survives: the band is gone entirely
        }

        /// <summary>
        /// Relative luminance of a band, for the one question a scalar can honestly answer: is the
        /// deep still darker than the shallows?
        /// </summary>
        public static float Luminance(SeabedGeom.Rgb authoredSrgb)
            => 0.2126f * ToneMap.SrgbToLinear(authoredSrgb.R)
             + 0.7152f * ToneMap.SrgbToLinear(authoredSrgb.G)
             + 0.0722f * ToneMap.SrgbToLinear(authoredSrgb.B);

        /// <summary>
        /// Red against blue in the radiance off a surface — the number that says whether the
        /// picture has kept the surface's own colour or handed it the water's. A torch's whole job
        /// is to pull this back toward what it is at the surface.
        /// </summary>
        public static float RedToBlue(SeabedGeom.Rgb albedoSrgb, float depthUnits, Facing facing,
                                      float lampDistanceUnits = NoLamp)
        {
            Radiance(albedoSrgb, depthUnits, facing, lampDistanceUnits,
                     out float r, out float _, out float b);
            return b <= 1e-9f ? float.PositiveInfinity : r / b;
        }
    }
}
