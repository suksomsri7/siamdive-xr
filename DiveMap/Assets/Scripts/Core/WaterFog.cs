using System;

namespace DiveMap.Core
{
    /// <summary>
    /// The colour of the water — for the fog, for the backdrop behind it, and for the ambient
    /// floor — all coming out of ONE multiplication so they cannot disagree.
    ///
    /// 🔴 THE BUG THIS IS THE FIX FOR. On Poseidon at 52 m the user reported the sharks as "เงาแบน
    /// สีน้ำเงินไร้รายละเอียด ขณะที่ฉากหลัง/หมอกยังสว่าง" — the contrast the wrong way round. The
    /// cause was not that the subject was too dark; it was that only the SUBJECT was being dimmed.
    /// <see cref="DepthLight"/> multiplies the light reaching an object by a Beer–Lambert curve,
    /// while the fog and the backdrop were painted from a ramp of their own that knew nothing about
    /// depth. Two systems, one picture: the deeper the camera went, the further the two drifted
    /// apart, and at 52 m the water was several times brighter than anything standing in it.
    ///
    /// 🔎 THE SHAPE OF THE FIX, which matters more than the numbers in it. The previous attempt
    /// (fog reads off the backdrop ramp, and the ramp walks downward with depth) made the two agree
    /// with EACH OTHER but left both of them independent of the light — so the ratio the eye
    /// actually judges, subject against water, was still a thing somebody had to tune and re-tune.
    /// Here the water is
    ///
    ///     water(depth) = authored colour ⊙ DepthLight.Attenuation(depth)
    ///
    /// and the ambient light on the subject is scaled by the same
    /// <see cref="DepthLight.Attenuation"/> vector (<c>DepthAtmosphere</c>). Cancel the common
    /// factor and the subject-to-background ratio is whatever it is at the surface, at EVERY depth,
    /// by construction rather than by tuning. Retune the lights and the water follows; go deeper
    /// and the whole picture dims together, which is what descending looks like.
    ///
    /// 🔎 And the authored colours are the WEB's, not compensated ones: <see cref="BaseFog"/> is
    /// builder.html's <c>THREE.Fog(0x123a55, …)</c> and the ramp is its four gradient stops. The
    /// two agree because they always did — 0x123a55 is a point on that gradient
    /// (<see cref="FogRampV"/>) — which is why the web has never had this bug.
    ///
    /// UnityEngine-free on purpose (like the rest of Core) so EditMode tests pin the relationship
    /// itself: "fog and background dim by the same vector" is a property, and a property can be
    /// asserted.
    /// </summary>
    public static class WaterFog
    {
        /// <summary>
        /// The web's underwater fog colour, builder.html:687 — <c>new THREE.Fog(0x123a55, …)</c>.
        /// Authored sRGB, like every other colour in this file: the attenuation multiplies it, the
        /// renderer decodes it.
        /// </summary>
        public static readonly SeabedGeom.Rgb BaseFog = new SeabedGeom.Rgb(0.0706f, 0.2275f, 0.3333f);

        /// <summary>
        /// Where <see cref="BaseFog"/> lands on the backdrop ramp — just below the horizon, which
        /// is where distant geometry meets the background and therefore the only place the two have
        /// to match. Not a choice: it is where the web's own two numbers already sat (max channel
        /// error 0.009, pinned by a test). It exists as a constant so that anyone editing a
        /// gradient stop finds out immediately that the fog moved too.
        /// </summary>
        public const float FogRampV = 0.90f;

        /// <summary>
        /// How much of another system's fog colour is allowed to survive.
        ///
        /// <c>DroneLights</c> deliberately swaps the fog when the headlamps go on or off, and that
        /// swap IS the feature — losing it entirely would make carrying a torch pointless. So the
        /// mood colour is not discarded, it is diluted: a quarter of it is enough to feel the
        /// switch, and not enough to drag the fog out of the water's own colour family.
        /// </summary>
        public const float MoodWeight = 0.25f;

        /// <summary>
        /// The light left at this depth, as a colour so it can be multiplied into one.
        /// This is <see cref="DepthLight.Attenuation"/> and nothing else — the single vector every
        /// depth-dependent thing in the scene is scaled by.
        /// </summary>
        public static SeabedGeom.Rgb Attenuation(float depthUnits)
        {
            DepthLight.Attenuation(depthUnits, out float r, out float g, out float b);
            return new SeabedGeom.Rgb(r, g, b);
        }

        /// <summary>Fog colour at a depth: the web's fog, dimmed by the water above it.</summary>
        public static SeabedGeom.Rgb ColorAt(float depthUnits) => Scale(BaseFog, Attenuation(depthUnits));

        /// <summary>
        /// Backdrop colour for screen row <paramref name="v"/> (0 = top of the frame) seen from
        /// <paramref name="depthUnits"/> down.
        ///
        /// The attenuation is applied uniformly to the whole ramp rather than per-row. Per-row
        /// would be more nearly true — look up from 50 m and you are looking through less water
        /// than you are looking sideways through — but it would also make the background dim by a
        /// different amount from the subject in front of it, which is precisely the drift this
        /// whole file exists to remove. A uniform factor keeps the guarantee; the ramp itself still
        /// carries "brighter above, darker below".
        /// </summary>
        public static SeabedGeom.Rgb BackdropAt(float v, float depthUnits)
            => Scale(SeabedGeom.GradientStop(v), Attenuation(depthUnits));

        /// <summary>
        /// Dim an authored (sRGB) colour by the attenuation — in LIGHT, not in the numbers.
        /// See <see cref="ToneMap.ScaleLight"/> for why that distinction is the difference between
        /// "half the light" and about a fifth of it. Everything depth-dependent in the scene goes
        /// through here, which is what makes the ratios hold.
        /// </summary>
        public static SeabedGeom.Rgb Scale(SeabedGeom.Rgb c, SeabedGeom.Rgb k)
            => new SeabedGeom.Rgb(ToneMap.ScaleLight(c.R, k.R),
                                  ToneMap.ScaleLight(c.G, k.G),
                                  ToneMap.ScaleLight(c.B, k.B));

        /// <summary>
        /// The water colour, nudged toward whatever another system asked for.
        /// <paramref name="weight"/> is normally <see cref="MoodWeight"/>; 0 ignores the mood
        /// entirely and 1 restores the old behaviour of letting it win.
        /// </summary>
        public static SeabedGeom.Rgb Blend(SeabedGeom.Rgb water, SeabedGeom.Rgb mood, float weight)
        {
            if (float.IsNaN(weight) || weight <= 0f) return water;
            if (weight > 1f) weight = 1f;
            return new SeabedGeom.Rgb(
                water.R + (mood.R - water.R) * weight,
                water.G + (mood.G - water.G) * weight,
                water.B + (mood.B - water.B) * weight);
        }

        /// <summary>
        /// How far off the backdrop a colour is — the largest per-channel gap to the nearest point
        /// of the ramp AS SEEN FROM <paramref name="depthUnits"/>. Exists so the tests can state the
        /// actual requirement ("the fog is a colour the background uses, at every depth") rather
        /// than restating whichever constants happen to be in the file.
        /// </summary>
        public static float DistanceFromRamp(SeabedGeom.Rgb c, float depthUnits = 0f)
        {
            const int steps = 1000;   // fine enough that the sampling error is far below any
                                      // difference a test would be asserting about
            SeabedGeom.Rgb k = Attenuation(depthUnits);
            float best = float.MaxValue;
            for (int i = 0; i <= steps; i++)
            {
                SeabedGeom.Rgb s = Scale(SeabedGeom.GradientStop((float)i / steps), k);
                float d = Math.Max(Math.Abs(s.R - c.R), Math.Max(Math.Abs(s.G - c.G), Math.Abs(s.B - c.B)));
                if (d < best) best = d;
            }
            return best;
        }
    }
}
