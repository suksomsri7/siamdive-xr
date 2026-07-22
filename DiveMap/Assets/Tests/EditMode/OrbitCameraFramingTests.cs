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
    }
}
