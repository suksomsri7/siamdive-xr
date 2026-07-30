using DiveMap.Core;
using NUnit.Framework;
using UnityEngine;

namespace DiveMap.Tests
{
    /// <summary>
    /// WO-XR-04.3 — the sun shafts must be parallel to the sun, land in the same place on
    /// every run (or two QC screenshots cannot be compared) and fade the way the web's beam
    /// texture does.
    /// </summary>
    public class GodRayMathTests
    {
        [Test]
        public void SunDirection_MatchesUnitysOwnEulerRotation()
        {
            GodRayMath.Vec3 d = GodRayMath.SunDirection();
            Vector3 unity = Quaternion.Euler(GodRayMath.SunPitchDeg, GodRayMath.SunYawDeg, 0f) * Vector3.forward;
            Assert.AreEqual(unity.x, d.X, 1e-4f);
            Assert.AreEqual(unity.y, d.Y, 1e-4f);
            Assert.AreEqual(unity.z, d.Z, 1e-4f);
        }

        [Test]
        public void SunDirection_IsAUnitVectorPointingDown()
        {
            GodRayMath.Vec3 d = GodRayMath.SunDirection();
            float len = Mathf.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z);
            Assert.AreEqual(1f, len, 1e-4f);
            Assert.AreEqual(-0.788f, d.Y, 0.002f);   // Euler(52) ⇒ −sin 52°
            Assert.AreEqual(-0.353f, d.X, 0.002f);
            Assert.AreEqual(0.505f, d.Z, 0.002f);
        }

        [Test]
        public void Direction_StraightDownAtNinetyDegrees()
        {
            GodRayMath.Vec3 d = GodRayMath.Direction(90f, 0f);
            Assert.AreEqual(0f, d.X, 1e-4f);
            Assert.AreEqual(-1f, d.Y, 1e-4f);
            Assert.AreEqual(0f, d.Z, 1e-4f);
        }

        [Test]
        public void BeamOffsets_StayInsideTheSpread_AndAreDeterministic()
        {
            const float radius = 160f;
            for (int i = 0; i < 12; i++)
            {
                GodRayMath.Vec2 a = GodRayMath.BeamOffset(i, 12, radius, 7);
                GodRayMath.Vec2 b = GodRayMath.BeamOffset(i, 12, radius, 7);
                Assert.AreEqual(a.X, b.X, 1e-6f, "beam placement must be deterministic");
                Assert.AreEqual(a.Z, b.Z, 1e-6f);
                float r = Mathf.Sqrt(a.X * a.X + a.Z * a.Z);
                Assert.LessOrEqual(r, radius + 1e-3f, $"beam {i} fell outside the spread");
            }
        }

        [Test]
        public void BeamOffsets_AreSpreadOut_NotStackedOnEachOther()
        {
            var pts = new Vector2[10];
            for (int i = 0; i < pts.Length; i++)
            {
                GodRayMath.Vec2 o = GodRayMath.BeamOffset(i, pts.Length, 160f, 7);
                pts[i] = new Vector2(o.X, o.Z);
            }
            for (int i = 0; i < pts.Length; i++)
            for (int j = i + 1; j < pts.Length; j++)
                Assert.Greater(Vector2.Distance(pts[i], pts[j]), 8f, $"beams {i}/{j} are on top of each other");
        }

        [Test]
        public void BeamWidth_VariesButStaysSane()
        {
            float min = 9f, max = -9f;
            for (int i = 0; i < 12; i++)
            {
                float w = GodRayMath.BeamWidthMul(i, 7);
                Assert.GreaterOrEqual(w, 0.55f);
                Assert.LessOrEqual(w, 1.0f);
                min = Mathf.Min(min, w);
                max = Mathf.Max(max, w);
            }
            Assert.Greater(max - min, 0.1f, "every beam came out the same width");
        }

        [Test]
        public void RampAlpha_HitsTheWebsThreeStops()
        {
            Assert.AreEqual(0f, GodRayMath.RampAlpha(0f), 1e-4f);
            Assert.AreEqual(0.5f, GodRayMath.RampAlpha(0.72f), 1e-4f);
            Assert.AreEqual(0.95f, GodRayMath.RampAlpha(1f), 1e-4f);
            Assert.AreEqual(0f, GodRayMath.RampAlpha(-1f), 1e-4f);
            Assert.AreEqual(0.95f, GodRayMath.RampAlpha(3f), 1e-4f);
        }

        [Test]
        public void RampAlpha_RisesMonotonicallyTowardsTheTip()
        {
            float prev = -1f;
            for (float t = 0f; t <= 1.0001f; t += 0.05f)
            {
                float a = GodRayMath.RampAlpha(t);
                Assert.GreaterOrEqual(a, prev - 1e-5f, $"ramp dipped at t={t}");
                prev = a;
            }
        }

        [Test]
        public void SoftProfile_IsZeroAtBothEdges_PeaksInTheMiddle_AndIsSymmetric()
        {
            // A shaft must have no silhouette: alpha has to reach 0 exactly at the edges.
            Assert.AreEqual(0f, GodRayMath.SoftProfile(0f), 1e-6f);
            Assert.AreEqual(0f, GodRayMath.SoftProfile(1f), 1e-6f);
            Assert.AreEqual(1f, GodRayMath.SoftProfile(0.5f), 1e-4f);
            for (float u = 0.05f; u < 0.5f; u += 0.05f)
                Assert.AreEqual(GodRayMath.SoftProfile(u), GodRayMath.SoftProfile(1f - u), 1e-5f);
        }

        [Test]
        public void SoftProfile_FeathersHard_NotLinearly()
        {
            // Squared smoothstep: a quarter of the way in it is still faint. This is the
            // difference between "much softer" and merely "dimmer".
            float quarter = GodRayMath.SoftProfile(0.25f);
            Assert.Less(quarter, 0.35f, "edge falloff is too abrupt to read as light");
            Assert.Greater(quarter, 0.05f);
            float prev = -1f;
            for (float u = 0f; u <= 0.5f; u += 0.05f)
            {
                float a = GodRayMath.SoftProfile(u);
                Assert.GreaterOrEqual(a, prev - 1e-6f, $"profile dipped at u={u}");
                prev = a;
            }
        }

        [Test]
        public void TopFade_KillsTheHardCutAtTheSurface()
        {
            Assert.AreEqual(0f, GodRayMath.TopFade(1f), 1e-6f);      // right on the water plane
            Assert.AreEqual(1f, GodRayMath.TopFade(0.5f), 1e-6f);    // well below it
            Assert.Greater(GodRayMath.TopFade(0.94f), 0f);
            Assert.Less(GodRayMath.TopFade(0.94f), 1f);
        }

        [Test]
        public void BeamAlpha_IsZeroOnEveryEdgeOfTheQuad()
        {
            for (float v = 0f; v <= 1f; v += 0.1f)
            {
                Assert.AreEqual(0f, GodRayMath.BeamAlpha(0f, v), 1e-6f);
                Assert.AreEqual(0f, GodRayMath.BeamAlpha(1f, v), 1e-6f);
            }
            for (float u = 0f; u <= 1f; u += 0.1f)
            {
                Assert.AreEqual(0f, GodRayMath.BeamAlpha(u, 0f), 1e-6f);   // deep end
                Assert.AreEqual(0f, GodRayMath.BeamAlpha(u, 1f), 1e-6f);   // surface end
            }
            // Brightest along the centre line, a little below the surface.
            Assert.Greater(GodRayMath.BeamAlpha(0.5f, 0.85f), 0.5f);
        }

        [Test]
        public void SwayAndBreath_StayWithinTheirBounds()
        {
            for (float t = 0f; t < 30f; t += 0.37f)
            {
                for (int i = 0; i < 10; i++)
                {
                    Assert.LessOrEqual(Mathf.Abs(GodRayMath.SwayDeg(i, t)), 2.0001f);
                    float b = GodRayMath.BreathMul(i, t);
                    Assert.GreaterOrEqual(b, 0.749f);
                    Assert.LessOrEqual(b, 1.001f);
                }
            }
        }
    }
}
