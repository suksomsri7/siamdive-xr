using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// P1.2 — the headlamp is only worth a button because it swaps the whole atmosphere, so the
    /// contrast between on and off is what these tests defend (a "light" that merely adds a cone
    /// over daylight reads as nothing).
    /// </summary>
    public class DiveLightMathTests
    {
        [Test]
        public void HeadlightOn_OpensTheWaterUp_OffClosesItIn()
        {
            DiveLightMath.Atmosphere on = DiveLightMath.HeadlightOn;
            DiveLightMath.Atmosphere off = DiveLightMath.HeadlightOff;

            // 1.5× was written when the lamps reached 380 u. The user asked for a torch, not a
            // floodlight, so the reach came down to 280 — still clearly further than the 200 u you
            // get with the lamps off, which is what this test is actually about.
            Assert.Greater(on.FogFar, off.FogFar * 1.25f, "with the lamps on you must see further");
            Assert.Less(on.FogFar, 500f,
                        "and NOT to the far rim of the map — a torch that lights everything is "
                        + "not a torch, which is what the phone build showed");
            Assert.Greater(on.FogNear, off.FogNear);
            Assert.Greater(on.AmbientMul, off.AmbientMul, "lamps off = dimmer surroundings");
            Assert.Greater(on.DiveLight, off.DiveLight);
            Assert.Greater(off.DiveLight, 0f, "even with the lamps off it must not be pitch black");
        }

        [Test]
        public void AtmospherePresets_AreTheWebsExceptTheReach()
        {
            DiveLightMath.Atmosphere on = DiveLightMath.HeadlightOn;
            Assert.AreEqual(140f, on.FogNear, 0.01f);
            // 380/0.38, not the web's 680/0.55: see DiveLightMath.HeadlightOn for why.
            Assert.AreEqual(280f, on.FogFar, 0.01f);
            Assert.AreEqual(0.52f, on.AmbientMul, 0.001f);
            Assert.AreEqual(2.2f, on.DiveLight, 0.001f);

            DiveLightMath.Atmosphere off = DiveLightMath.HeadlightOff;
            Assert.AreEqual(70f, off.FogNear, 0.01f);
            Assert.AreEqual(200f, off.FogFar, 0.01f);
            Assert.AreEqual(0.40f, off.AmbientMul, 0.001f);
            Assert.AreEqual(0.5f, off.DiveLight, 0.001f);

            // 0x18638a vs 0x08303f — on is a lit blue, off is nearly black-green.
            Assert.Greater(on.FogB, off.FogB);
            Assert.Greater(on.FogR + on.FogG + on.FogB, (off.FogR + off.FogG + off.FogB) * 1.5f);
        }

        [Test]
        public void For_SelectsThePreset()
        {
            Assert.AreEqual(DiveLightMath.HeadlightOn.FogFar, DiveLightMath.For(true).FogFar, 0.01f);
            Assert.AreEqual(DiveLightMath.HeadlightOff.FogFar, DiveLightMath.For(false).FogFar, 0.01f);
        }

        [Test]
        public void PoolRadius_GrowsWithHeight_ButStaysInTheWebsClamp()
        {
            Assert.AreEqual(20f, DiveLightMath.PoolRadius(5f, 0f), 0.01f, "clamped at 20 when hugging the sand");
            Assert.AreEqual(36f, DiveLightMath.PoolRadius(30f, 0f), 0.01f);   // 30 × 1.2
            Assert.AreEqual(92f, DiveLightMath.PoolRadius(500f, 0f), 0.01f, "clamped at 92 high up");
            Assert.AreEqual(24f, DiveLightMath.PoolRadius(120f, 100f), 0.01f, "height is ABOVE the ground");
        }

        [Test]
        public void PoolOffset_IsHalfTheRadius_SoTheTwoPoolsOverlap()
        {
            float r = 60f;
            float off = DiveLightMath.PoolOffset(r);
            Assert.AreEqual(30f, off, 0.01f);
            Assert.Less(off * 2f, r * 2f, "separation must be smaller than the pools' combined width");
        }

        [Test]
        public void BeamScale_FollowsPoolAndDistance_WithAFloor()
        {
            DiveLightMath.BeamScale(90f, 120f, out float w, out float l);
            Assert.AreEqual(10f, w, 0.01f);
            Assert.AreEqual(2f, l, 0.01f);

            DiveLightMath.BeamScale(1f, 1f, out float w2, out float l2);
            Assert.AreEqual(0.5f, w2, 0.01f, "never degenerate");
            Assert.AreEqual(0.5f, l2, 0.01f);
        }

        [Test]
        public void BubblePush_IsStrongestAtTheCentre_AndZeroAtTheRim()
        {
            float bub = DiveLightMath.FishBubble;
            Assert.AreEqual(0f, DiveLightMath.BubblePush(bub), 1e-4f);
            Assert.AreEqual(0f, DiveLightMath.BubblePush(bub + 5f), 1e-4f);
            Assert.AreEqual(0f, DiveLightMath.BubblePush(0f), 1e-4f, "a fish exactly on the camera is skipped, not NaN");

            float mid = DiveLightMath.BubblePush(bub * 0.5f);
            Assert.Greater(mid, 0f);
            Assert.Less(mid, bub);
            Assert.Greater(DiveLightMath.BubblePush(bub * 0.25f), mid, "closer = pushed harder");
        }

        [Test]
        public void BubblePush_MovesAFishClearOfTheBubble()
        {
            // A fish at 2 u from the diver ends up outside the 8 u bubble, which is the point:
            // the shoal parts around you instead of through your face.
            const float d = 2f;
            float pushed = d + DiveLightMath.BubblePush(d);
            Assert.GreaterOrEqual(pushed, DiveLightMath.FishBubble * 0.9f);
        }
    }
}
