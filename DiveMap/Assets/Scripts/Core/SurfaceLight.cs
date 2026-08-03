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

        // ── WO-E5c: which base-colour maps need lifting, decided by measurement ───
        //
        // 🔴 THE OLD SCREEN WAS `p95 < 160` AND IT HAS A RECALL OF ZERO. Twelve models were
        // photographed in one CI run (30800189252) through one pipeline, and their base colour
        // maps were measured surface-weighted off the very same files the run downloaded. Ranking
        // every candidate statistic against the thing being predicted — blackOfSubject, the
        // fraction of the model's own pixels that come out as exactly (0,0,0):
        //
        //     Spearman vs blackOfSubject     p1 −0.907 · p5 −0.864 · pctBelow53 +0.891
        //                                    pctBelow64 +0.891 · pctBelow45 +0.845
        //                                    p95 +0.351 · p50 −0.009 · darkOfSubject +0.191
        //
        // p95 is a statistic about the BRIGHT end and is blind to the dark tail that actually
        // produces black. Treated as a classifier against the four models that visibly went black
        // (barracuda 16.79%, singha 5.64%, ancient_byzantine 25.82%, domed_temple 33.32%) it
        // caught NONE of them, and the one file it did flag was htms732 — the second cleanest map
        // in the set at 0.16%. It was not weak, it was pointing the wrong way.
        //
        // 🔎 And darkOfSubject is not a second opinion on the same thing, it is a different axis:
        // its correlation with blackOfSubject is +0.19. The three Atlantis ruins score 85.0, 85.4
        // and 86.9% dark — nearly identical — while their black runs 1.07 → 33.32%. Dark is the
        // LIGHT (they are enormous, low texel density, dim); black is the TEXTURE's dark tail.
        // Both are real, they have different owners, and a fix for one will not move the other.

        /// <summary>
        /// The 1st percentile of a base-colour map's surface-weighted sRGB luminance, below which
        /// the model is expected to render with visible pure-black patches. The single best
        /// predictor measured (Spearman −0.907): pure black comes from texels near ZERO, so it is
        /// the extreme tail that matters, not the mean and not the bright end.
        /// </summary>
        public const int ScreenMinP1Srgb = 50;

        /// <summary>
        /// …and the fraction of SURFACE the map is allowed to leave below
        /// <see cref="CrushAlbedoSrgb"/>, in per cent.
        ///
        /// 🔎 Why this can be a depth-independent number even though the crush point is not. Across
        /// the whole range the app renders — 0 to 100 m — <see cref="CrushAlbedoSrgb"/> only moves
        /// between sRGB 45 and 64, and NOT monotonically: it is worst around 20-30 m (45), because
        /// the seabed bounce ramps in by 22 m and the depth attenuation only slowly wins it back
        /// (62 at the surface, 45 at 23 m, 53 at 52 m, 64 at 100 m). A 19-byte band is narrow
        /// enough that a single screen holds everywhere, which is why no per-module depth lookup is
        /// needed. Screen at sRGB 45 — the most permissive end — and a map that passes is safe at
        /// every depth; tighten to <c>pctBelow64</c> for content placed past 60 m if a map ever
        /// wants the stricter reading.
        /// </summary>
        public const double ScreenMaxPctBelowCrush = 2.0;

        /// <summary>
        /// Does this base-colour map need its albedo lifted? Both conditions, because they catch
        /// different shapes of the same failure: <paramref name="p1Srgb"/> catches a map with a
        /// thin but genuinely black tail, <paramref name="pctBelowCrush"/> catches one whose dark
        /// region is broad rather than extreme.
        ///
        /// Measured against the eleven models of run 30800189252: 4 of 4 blacks caught, one false
        /// positive (msh:lionfish, p1 25 / 6.85% below crush but only 2.80% black — a small fish
        /// whose dark texels are thin stripes that never cover a whole pixel). A false positive
        /// costs a slightly flatter fish; a false negative costs the bug this whole work order is.
        /// </summary>
        public static bool NeedsAlbedoLift(int p1Srgb, double pctBelowCrush)
            => p1Srgb < ScreenMinP1Srgb || pctBelowCrush > ScreenMaxPctBelowCrush;

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
