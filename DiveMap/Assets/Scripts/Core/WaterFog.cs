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

        // ── WO-E5: how far the water can actually be seen through ────────────────
        //
        // 🔴 THE FOG HAS NEVER DONE ANYTHING INSIDE A MAP, AND IT IS ARITHMETIC, NOT TASTE.
        //
        // <c>AppBoot</c> ships the web's own linear-fog range — near 500 u, far 9,000 u
        // (builder.html: near = max(500, reach·1.1), far = max(9000, maxD·3.4)) — and
        // <c>DepthAtmosphere</c> scales both by <see cref="DepthLight.VisibilityScale"/>. At the
        // 61.8 m the user photographed, that scale is 0.645, so the fog starts 322 u away and does
        // not reach full strength for another 5,483 u. The seabed is
        // <see cref="SeabedGeom.SandRadius"/> = 340 u across its half-width, so the FURTHEST
        // anything on the map can be from a diver inside it is about 680 u, at which the linear
        // fog factor is
        //
        //     (680 − 322.6) / (5805.4 − 322.6) = 6.5%
        //
        // and at the 200 u a rock in that screenshot actually stood at, exactly 0%. The user's
        // words were "กลุ่มหมอกก็ไม่เห็นเลย … ก้อนหินไกลยังคมชัดไม่จมหมอก"; the fog was not weak,
        // it was mathematically out of reach.
        //
        // The numbers are not wrong for the WEB. The web frames a 340 u map from an orbit camera
        // 950 u away, so its whole map lives at 600-1,300 u and 500/9,000 is the 3-7% rim wash it
        // was measured to be. The app puts the player INSIDE the same map at 20-300 u. Same
        // constants, different geometry, and only one of the two was ever checked.
        //
        // So the range is stated as what it physically is — how far you can see — and it is tied to
        // the only two lengths that describe the shot: the map's own size, and how far back the
        // camera is standing. Both are known every frame.

        /// <summary>
        /// How much further than the camera's distance to the map the water stays clear.
        ///
        /// 1.6 is chosen from the geometry rather than by eye: a camera standing d away from the
        /// centre of a map of half-width R sees its near rim at d − R and its far rim at d + R, so
        /// a range of 1.6 d puts the far rim at (d + R)/1.6 d of full fog — 81% for the web's own
        /// orbit framing (d = 950, R = 340) and never 100%, which is the property that matters: the
        /// far side of the map must SINK into the water, never disappear from it.
        /// </summary>
        public const float FogReachOfViewDistance = 1.6f;

        /// <summary>
        /// Where the fog begins, as a fraction of where it ends. Underwater haze has no clear
        /// stand-off in reality — it starts at your mask — but a linear fog that begins at 0 tints
        /// the diver's own hands, so this keeps the nearest eighth of the visible range honest and
        /// gives the ramp somewhere to start from.
        /// </summary>
        public const float FogStartFraction = 0.12f;

        /// <summary>
        /// How much the fog is allowed to leave of the far rim of the map you are standing in.
        ///
        /// 🔴 This is the ONE number in the fog that is a design decision rather than a
        /// measurement, so it is written as the decision instead of as a distance: the far side of
        /// the map has to read as eighty per cent water — plainly sunk, still there. "ของไกลต้องจม
        /// หายเข้าไปในน้ำ" is the requirement; a map whose far half is literally blank is not that,
        /// it is a shorter draw distance.
        /// </summary>
        public const float FarRimFog = 0.80f;

        /// <summary>
        /// The reach, as a multiple of the content's DIAMETER, that delivers exactly
        /// <see cref="FarRimFog"/> at the far rim — solved from the linear fog rather than typed
        /// in, so the two cannot drift:
        ///
        ///     (2R − s)/(e − s) = FarRimFog, s = FogStartFraction·e
        ///     ⟹ e = 2R / (FarRimFog + FogStartFraction·(1 − FarRimFog))
        /// </summary>
        public static float FarRimReach
            => 1f / (FarRimFog + FogStartFraction * (1f - FarRimFog));

        /// <summary>
        /// The fog range, in world units, for a camera <paramref name="cameraToContent"/> from the
        /// centre of a map whose half-width is <paramref name="contentRadius"/>, at
        /// <paramref name="depthUnits"/> below the surface.
        ///
        /// 🔎 THE FLOOR IS WHAT MAKES THIS SAFE TO SHIP, and it is deliberately OUTSIDE the depth
        /// term. Two separate things would otherwise both want to shorten the range — the camera
        /// coming closer, and the water getting darker — and multiplying them is how a "reach"
        /// ends up smaller than the map it is describing, which is the failure being fixed here in
        /// the first place. So:
        ///
        ///   • the floor, <see cref="FarRimReach"/> × 2R, is a promise the depth cannot break: at
        ///     any depth, from anywhere inside the map, the far rim reads as
        ///     <see cref="FarRimFog"/> water and no more. Inside the footprint the range therefore
        ///     does not move at all, so the haze holds still while the diver flies through it
        ///     instead of closing in on their face as they reach the middle;
        ///   • the camera term carries the depth. Pull back to look at the whole map and how far
        ///     you can see depends on how much light is left down there
        ///     (<see cref="DepthLight.VisibilityScale"/>) — the cue the user asked to keep.
        ///
        /// 🔎 And the depth cue for the fog itself lives in its COLOUR, not in its range:
        /// <see cref="ColorAt(float)"/> is the authored water times
        /// <see cref="DepthLight.Attenuation"/>, so the deeper you go the darker and bluer the
        /// thing distant objects fade INTO. One depth dimmer per mechanism; stacking a third one on
        /// the range is what put the fog out of the map's reach.
        /// </summary>
        public static void RangeAt(float depthUnits, float contentRadius, float cameraToContent,
                                   out float start, out float end)
        {
            float r = contentRadius > 1f ? contentRadius : SeabedGeom.SandRadius;
            float d = cameraToContent > 0f && !float.IsNaN(cameraToContent) ? cameraToContent : 0f;

            float vis = DepthLight.VisibilityScale(depthUnits);
            if (float.IsNaN(vis) || vis <= 0f) vis = 1f;

            end = Math.Max(FarRimReach * 2f * r, FogReachOfViewDistance * d * vis);
            start = end * FogStartFraction;
        }

        /// <summary>
        /// The fraction of a linear fog in force at <paramref name="distance"/> — the number that
        /// turns "the fog range is 439 u" into "that rock is 34% water". Stated here so a QC log
        /// can print it beside the picture instead of a reviewer deriving it from two constants.
        /// </summary>
        public static float FactorAt(float distance, float start, float end)
        {
            if (end <= start) return distance >= end ? 1f : 0f;
            float t = (distance - start) / (end - start);
            return t <= 0f ? 0f : (t >= 1f ? 1f : t);
        }

        // ── WO-E5: the fog colour has to be the colour of the water you can SEE ──
        //
        // 🔴 <see cref="FogRampV"/> = 0.90 is right for the web and wrong for a diver, for exactly
        // the same reason the range was: it is a fact about WHERE ON THE SCREEN THE FAR RIM OF THE
        // MAP LANDS, and the two cameras do not agree about that.
        //
        // The backdrop is a SCREEN-SPACE ramp: v = 0 is the top of the frame and the surface
        // colour, v = 1 is the bottom and the deep stop. Distant geometry has to fade toward the
        // background AT THE ROW WHERE IT MEETS IT, or it fades toward a colour that is not behind
        // it. The web's orbit camera looks down on a small map from 950 u away, so its far rim
        // lands low in the frame — 0.90 is where #123a55 sits on the ramp, and the comment on
        // FogRampV is honest that this is where the web's two numbers already were rather than
        // something anybody derived. The app's diver looks roughly level, its far rim lands near
        // mid-frame, and the ramp there carries several times the light #123a55 does. Fading a
        // distant rock toward the deep stop while it stands against the mid ramp is the "distant
        // geometry reads as a black silhouette" failure DepthAtmosphere's own comment describes as
        // version 1 — fixed for the orbit camera and re-created for the diver.
        //
        // 🔎 So it is not modelled, it is PROJECTED: DepthAtmosphere puts the map's far rim through
        // the real camera matrix and asks which row it came out on. Nothing to tune, nothing to be
        // wrong about, and for a camera framed the way the web frames it the answer comes back at
        // the web's own value.

        /// <summary>
        /// Turn a viewport y (0 = BOTTOM of the frame, Unity's convention) into a backdrop ramp
        /// position (0 = TOP of the frame, the web's convention). One flip and two clamps, kept
        /// here rather than at the call site so the two conventions are reconciled in exactly one
        /// place — getting this the wrong way round would fade distant geometry toward the
        /// SURFACE colour, which is a much prettier bug and just as wrong.
        ///
        /// <paramref name="behindCamera"/> is the caller's <c>viewportPoint.z &lt;= 0</c>: a point
        /// behind the camera projects to a mirrored, meaningless y, and the honest answer there is
        /// the web's own constant rather than a number derived from nonsense.
        /// </summary>
        public static float RampVOfViewportY(float viewportY, bool behindCamera = false)
        {
            if (behindCamera || float.IsNaN(viewportY)) return FogRampV;
            float v = 1f - viewportY;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        /// <summary>
        /// Fog colour at a depth, fading toward the backdrop at ramp position
        /// <paramref name="rampV"/> rather than at the web's fixed horizon.
        ///
        /// <see cref="ColorAt(float)"/> is this with <see cref="FogRampV"/>, which is the web's own
        /// answer and is kept as the default so nothing that already reasons about #123a55 moves.
        /// </summary>
        public static SeabedGeom.Rgb ColorAt(float depthUnits, float rampV)
            => Scale(SeabedGeom.GradientStop(rampV), Attenuation(depthUnits));

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
