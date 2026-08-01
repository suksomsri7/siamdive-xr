using System;

namespace DiveMap.Core
{
    /// <summary>
    /// The colour the water fades things into — taken from the SAME ramp the backdrop is painted
    /// with, so distant geometry and the background behind it are never two different colours.
    ///
    /// 🔴 Why this exists. The backdrop gradient and the fog were authored independently:
    /// <see cref="SeabedGeom.GradientStop"/> runs #eaf7fb → #1b5a85 (bright cyan to a still-readable
    /// blue), while <c>AppBoot</c> set <c>RenderSettings.fogColor</c> to the web's #123a55 — about a
    /// third as bright as the ramp anywhere the eye actually looks. Linear fog blends every distant
    /// pixel toward that dark navy, but the pixels BEHIND it come from the bright ramp. The result
    /// was reported as "the wreck and the fish are black silhouettes": nothing was unlit, the fog
    /// was simply painting them a colour the background never uses.
    ///
    /// The fix is not a brighter constant — that would be wrong at 40 m the way the old one was
    /// wrong at 3 m. It is to make the fog READ OFF THE RAMP, at the height of the ramp that sits
    /// behind the horizon. Then "matching" is structural rather than a pair of numbers somebody has
    /// to keep in step: change a gradient stop and the fog follows it.
    ///
    /// UnityEngine-free on purpose (like the rest of Core) so EditMode tests pin the relationship
    /// itself — "fog is inside the ramp" is a property, and a property can be asserted.
    /// </summary>
    public static class WaterFog
    {
        /// <summary>
        /// Where in the backdrop ramp the fog colour is sampled, from the surface to the deep.
        ///
        /// Not 0 and not 1. The gradient's v is a SCREEN position (0 = top of the frame), and what
        /// the fog has to match is the row where distant geometry meets the backdrop — the horizon,
        /// slightly below centre. Sampling v=0 would hand back the near-white surface haze and
        /// wash the shallows out; sampling v=1 gives the ramp's own darkest stop, which is what the
        /// deepest water should look like and nothing shallower should.
        /// </summary>
        public const float ShallowV = 0.44f;
        public const float DeepV = 0.94f;

        /// <summary>
        /// Depth, in metres, at which the fog reaches the bottom of the ramp. Recreational diving
        /// tops out around here, and it is also where <see cref="DepthLight"/>'s curves have
        /// substantially flattened, so the two depth cues finish together instead of one of them
        /// continuing to darken after the other has stopped.
        /// </summary>
        public const float DeepMetres = 40f;

        /// <summary>
        /// How much of another system's fog colour is allowed to survive.
        ///
        /// <c>DroneLights</c> deliberately swaps the fog when the headlamps go on or off, and that
        /// swap IS the feature — losing it entirely would make carrying a torch pointless. So the
        /// mood colour is not discarded, it is diluted: a quarter of it is enough to feel the
        /// switch, and not enough to drag the fog back out of the backdrop's colour family.
        /// </summary>
        public const float MoodWeight = 0.25f;

        /// <summary>
        /// Position in the ramp for a camera <paramref name="depthUnits"/> below the surface.
        /// Above the surface the shallowest sample is used — the map view often sits up there and
        /// must not read as "deeper than the deep".
        /// </summary>
        public static float RampV(float depthUnits)
        {
            float metres = depthUnits / DepthLight.UnitsPerMetre;
            if (float.IsNaN(metres) || metres <= 0f) return ShallowV;
            float t = metres / DeepMetres;
            if (t > 1f) t = 1f;
            return ShallowV + (DeepV - ShallowV) * t;
        }

        /// <summary>Fog colour at a depth, straight off the backdrop's own gradient.</summary>
        public static SeabedGeom.Rgb ColorAt(float depthUnits) => SeabedGeom.GradientStop(RampV(depthUnits));

        /// <summary>
        /// The ramp colour, nudged toward whatever another system asked for.
        /// <paramref name="weight"/> is normally <see cref="MoodWeight"/>; 0 ignores the mood
        /// entirely and 1 restores the old behaviour of letting it win.
        /// </summary>
        public static SeabedGeom.Rgb Blend(SeabedGeom.Rgb ramp, SeabedGeom.Rgb mood, float weight)
        {
            if (float.IsNaN(weight) || weight <= 0f) return ramp;
            if (weight > 1f) weight = 1f;
            return new SeabedGeom.Rgb(
                ramp.R + (mood.R - ramp.R) * weight,
                ramp.G + (mood.G - ramp.G) * weight,
                ramp.B + (mood.B - ramp.B) * weight);
        }

        /// <summary>
        /// How far off the ramp a colour is — the largest per-channel gap to the nearest ramp
        /// sample. Exists so the tests can state the actual requirement ("the fog is a colour the
        /// background uses") rather than restating whichever constants happen to be in the file.
        /// </summary>
        public static float DistanceFromRamp(SeabedGeom.Rgb c)
        {
            const int steps = 1000;   // fine enough that the sampling error is far below any
                                      // difference a test would be asserting about
            float best = float.MaxValue;
            for (int i = 0; i <= steps; i++)
            {
                SeabedGeom.Rgb s = SeabedGeom.GradientStop((float)i / steps);
                float d = Math.Max(Math.Abs(s.R - c.R), Math.Max(Math.Abs(s.G - c.G), Math.Abs(s.B - c.B)));
                if (d < best) best = d;
            }
            return best;
        }
    }
}
