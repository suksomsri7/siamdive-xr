using DiveMap.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace DiveMap.Tests
{
    /// <summary>
    /// Unit tests for OrbitCamera.ComputeFraming — the pure opening-shot solution that
    /// frames the loaded content (e.g. the wreck) instead of the whole seabed + water
    /// column. Mirrors the web builder's frameContent() geometry.
    /// </summary>
    public class OrbitCameraFramingTests
    {
        private const float MinD = 2f, MaxD = 950f;

        [Test]
        public void Framing_AimsAtLowerPartOfBox_NotCentre()
        {
            // Tall box (a 300-unit wreck) rising from the seabed at y=0.
            var f = OrbitCamera.ComputeFraming(0f, 0f, 120f, 300f, 120f, 0f, MinD, MaxD);
            // aimY = minY + min(sizeY*0.4, 45) = 0 + 45  (capped, so NOT the 150 centre).
            Assert.AreEqual(45f, f.TargetY, 0.001f);
        }

        [Test]
        public void Framing_UsesHorizontalExtentForDistance_IgnoringHeight()
        {
            // Two boxes: same horizontal footprint, very different heights → same distance.
            var wide = OrbitCamera.ComputeFraming(0f, 0f, 100f, 50f, 100f, 0f, MinD, MaxD);
            var tall = OrbitCamera.ComputeFraming(0f, 0f, 100f, 900f, 100f, 0f, MinD, MaxD);
            Assert.AreEqual(wide.Distance, tall.Distance, 0.001f,
                "height must not push the camera further back");
        }

        [Test]
        public void Framing_CameraSitsAboveAndBehind_GentleDownAngle()
        {
            var f = OrbitCamera.ComputeFraming(0f, 0f, 100f, 100f, 100f, 0f, MinD, MaxD);
            // Reconstruct the camera position from yaw/pitch/distance the way OrbitCamera.Apply does.
            Quaternion rot = Quaternion.Euler(f.Pitch, f.Yaw, 0f);
            Vector3 target = new Vector3(f.TargetX, f.TargetY, f.TargetZ);
            Vector3 pos = target - (rot * Vector3.forward) * f.Distance;

            float r = 50f;                                  // max(sizeX,sizeZ)*0.5
            float dist = Mathf.Min(MaxD, r * 1.45f + 40f);  // web formula
            // Web places the camera at (cx, aimY + dist*0.32, cz + dist).
            Assert.AreEqual(target.x, pos.x, 0.05f);
            Assert.AreEqual(target.y + dist * 0.32f, pos.y, 0.25f);
            Assert.AreEqual(target.z + dist, pos.z, 0.25f);
            Assert.Greater(pos.y, target.y, "camera must look DOWN onto the scene");
        }

        [Test]
        public void Framing_ZeroSize_UsesFallbackRadius()
        {
            var f = OrbitCamera.ComputeFraming(5f, 7f, 0f, 0f, 0f, 0f, MinD, MaxD);
            Assert.AreEqual(5f, f.TargetX, 0.001f);
            Assert.AreEqual(7f, f.TargetZ, 0.001f);
            Assert.Greater(f.Distance, 0f);
        }

        /// <summary>
        /// 🔴 2026-08-06. The opening shot must NOT move when the zoom-out ceiling is raised.
        ///
        /// The ceiling is now per-map (AppBoot.ApplyViewRange, from CameraRange — the web's
        /// updateViewRange, builder.html:709-722) and on a large map it is several thousand units
        /// instead of 950. These two used to be the same number, so widening one would have quietly
        /// pushed the other back and changed the first thing the user sees on every big map, while
        /// answering a question they asked about the last thing. The framing cap is now
        /// <see cref="OrbitCamera.FrameDistanceCap"/> and nothing else.
        /// </summary>
        [Test]
        public void Framing_IsUnchangedByTheZoomOutCeiling()
        {
            // A map big enough that the old cap was the binding constraint: r = 2000 → the web
            // formula wants 2940 and the old code clamped it to 950.
            foreach (float ceiling in new[] { 950f, 2600f, 7000f, 20000f })
            {
                var big = OrbitCamera.ComputeFraming(0f, 0f, 4000f, 100f, 4000f, 0f, MinD, ceiling);
                Assert.AreEqual(OrbitCamera.FrameDistanceCap, big.Distance, 1f,
                                $"ceiling {ceiling} moved the opening shot");
            }

            // …and an ordinary map, which never reached either cap, is identical to the unit.
            var small = OrbitCamera.ComputeFraming(0f, 0f, 100f, 100f, 100f, 0f, MinD, MaxD);
            var loose = OrbitCamera.ComputeFraming(0f, 0f, 100f, 100f, 100f, 0f, MinD, 20000f);
            Assert.AreEqual(small.Distance, loose.Distance, 0.001f);
            Assert.AreEqual(small.Pitch, loose.Pitch, 0.001f);
        }
    }
}
