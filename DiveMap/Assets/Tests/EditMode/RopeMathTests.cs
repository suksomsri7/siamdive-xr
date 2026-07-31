using System.Collections.Generic;
using DiveMap.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// Unit tests for ropes.
    ///
    /// The shape is asserted against the web's ACTUAL formula, not its function name: the web
    /// calls it <c>_catenaryPts</c> but computes a parabola. Porting the name instead of the
    /// code would change the shape of every rope in every saved map on first load.
    /// </summary>
    public class RopeMathTests
    {
        // ── curve ────────────────────────────────────────────────────────────────

        [Test]
        public void Droop_IsZeroAtBothEndsAndMaxInTheMiddle()
        {
            Assert.AreEqual(0.0, RopeMath.DroopAt(0.0, 10.0), 1e-9);
            Assert.AreEqual(0.0, RopeMath.DroopAt(1.0, 10.0), 1e-9);
            Assert.AreEqual(10.0, RopeMath.DroopAt(0.5, 10.0), 1e-9, "peak droop == sag");
        }

        [Test]
        public void Droop_IsAParabolaNotACatenary()
        {
            // sag·4·t·(1−t). At t=0.25 a parabola gives 0.75·sag; a cosh catenary would not.
            Assert.AreEqual(7.5, RopeMath.DroopAt(0.25, 10.0), 1e-9);
            Assert.AreEqual(7.5, RopeMath.DroopAt(0.75, 10.0), 1e-9, "symmetric");
        }

        [Test]
        public void Curve_StartsAndEndsExactlyOnTheAnchors()
        {
            var pts = new List<double[]>();
            RopeMath.Curve(0, 100, 0, 60, 100, 0, 12, pts);

            Assert.AreEqual(RopeMath.Samples + 1, pts.Count);
            Assert.AreEqual(0, pts[0][0], 1e-9);
            Assert.AreEqual(100, pts[0][1], 1e-9, "no droop at the anchor");
            Assert.AreEqual(60, pts[pts.Count - 1][0], 1e-9);
            Assert.AreEqual(100, pts[pts.Count - 1][1], 1e-9);
        }

        [Test]
        public void Curve_HangsBelowTheStraightLine()
        {
            var pts = new List<double[]>();
            RopeMath.Curve(0, 100, 0, 60, 100, 0, 12, pts);
            double[] middle = pts[RopeMath.Samples / 2];
            Assert.AreEqual(30, middle[0], 1e-9, "halfway along");
            Assert.AreEqual(100 - 12, middle[1], 1e-9, "and one full sag down");
        }

        [Test]
        public void Curve_WorksBetweenDifferentHeights()
        {
            var pts = new List<double[]>();
            RopeMath.Curve(0, 100, 0, 0, 40, 80, 10, pts);
            Assert.AreEqual(100, pts[0][1], 1e-9);
            Assert.AreEqual(40, pts[pts.Count - 1][1], 1e-9);
            // The middle is the average height MINUS the sag.
            Assert.AreEqual(70 - 10, pts[RopeMath.Samples / 2][1], 1e-9);
        }

        [Test]
        public void Curve_ReusesTheListAndSurvivesNonsense()
        {
            var pts = new List<double[]> { new double[] { 9, 9, 9 } };
            RopeMath.Curve(0, 0, 0, 1, 0, 0, 1, pts);
            Assert.AreEqual(RopeMath.Samples + 1, pts.Count, "the old contents are cleared");

            RopeMath.Curve(0, 0, 0, 1, 0, 0, 1, pts, samples: 0);
            Assert.AreEqual(3, pts.Count, "a silly sample count is clamped to 2 segments");

            Assert.DoesNotThrow(() => RopeMath.Curve(0, 0, 0, 1, 0, 0, 1, null));
        }

        // ── default sag ──────────────────────────────────────────────────────────

        [Test]
        public void DefaultSag_IsProportionalWithAFloor()
        {
            Assert.AreEqual(6.0, RopeMath.DefaultSagFor(0), 1e-9, "a short rope still hangs a little");
            Assert.AreEqual(6.0, RopeMath.DefaultSagFor(30), 1e-9, "…and 30×0.16 = 4.8 is below the floor");
            Assert.AreEqual(16.0, RopeMath.DefaultSagFor(100), 1e-9);
        }

        [Test]
        public void Distance_IsEuclidean()
        {
            Assert.AreEqual(5.0, RopeMath.Distance(0, 0, 0, 3, 4, 0), 1e-9);
        }

        // ── storage ──────────────────────────────────────────────────────────────

        private static JArray Sample() => JArray.Parse(@"[
          {""id"":""r1"",""a"":{""mid"":""i1"",""lp"":[1,2,3]},""b"":{""mid"":""i2"",""lp"":[4,5,6]},
           ""sag"":12,""color"":""#b03a2e"",""thick"":0.8},
          {""id"":""r2"",""a"":{""mid"":""i2"",""lp"":[0,0,0]},""b"":{""mid"":""i3"",""lp"":[0,1,0]}}
        ]");

        [Test]
        public void Parse_ReadsBothEndsAndTheDefaults()
        {
            List<Rope> ropes = RopeMath.Parse(Sample());
            Assert.AreEqual(2, ropes.Count);

            Assert.AreEqual("i1", ropes[0].A.ItemId);
            Assert.AreEqual(3, ropes[0].A.Lz, 1e-9);
            Assert.AreEqual(12, ropes[0].Sag, 1e-9);
            Assert.AreEqual("#b03a2e", ropes[0].Color);
            Assert.AreEqual(0.8, ropes[0].Thick, 1e-9);

            Assert.AreEqual(8.0, ropes[1].Sag, 1e-9, "the web's default sag");
            Assert.AreEqual(RopeMath.DefaultColor, ropes[1].Color);
            Assert.AreEqual(RopeMath.DefaultThick, ropes[1].Thick, 1e-9);
        }

        [Test]
        public void Parse_SkipsRowsWithoutBothEnds()
        {
            var arr = JArray.Parse(@"[
              {""id"":""bad1"",""a"":{""mid"":""i1"",""lp"":[0,0,0]}},
              {""id"":""bad2""},
              ""not an object"",
              {""id"":""bad3"",""a"":{""lp"":[0,0,0]},""b"":{""mid"":""i2"",""lp"":[0,0,0]}}
            ]");
            Assert.AreEqual(0, RopeMath.Parse(arr).Count, "a rope with no anchor is not a rope");
            Assert.AreEqual(0, RopeMath.Parse(null).Count);
        }

        [Test]
        public void RoundTrip_KeepsEverything()
        {
            List<Rope> ropes = RopeMath.Parse(Sample());
            List<Rope> again = RopeMath.Parse(RopeMath.Serialise(ropes));

            Assert.AreEqual(ropes.Count, again.Count);
            Assert.AreEqual(ropes[0].A.ItemId, again[0].A.ItemId);
            Assert.AreEqual(ropes[0].B.Ly, again[0].B.Ly, 1e-9);
            Assert.AreEqual(ropes[0].Sag, again[0].Sag, 1e-9);
            Assert.AreEqual(ropes[0].Color, again[0].Color);
            Assert.AreEqual(ropes[0].Thick, again[0].Thick, 1e-9);
        }

        [Test]
        public void Serialise_ToleratesNulls()
        {
            Assert.AreEqual(0, RopeMath.Serialise(null).Count);
            Assert.AreEqual(0, RopeMath.Serialise(new List<Rope> { null }).Count);
        }

        // ── detaching ────────────────────────────────────────────────────────────

        [Test]
        public void DetachFrom_DropsEveryRopeTouchingADeletedObject()
        {
            List<Rope> ropes = RopeMath.Parse(Sample());
            // i2 is an end of BOTH ropes.
            Assert.AreEqual(2, RopeMath.DetachFrom(ropes, "i2"));
            Assert.AreEqual(0, ropes.Count);
        }

        [Test]
        public void DetachFrom_LeavesUnrelatedRopesAlone()
        {
            List<Rope> ropes = RopeMath.Parse(Sample());
            Assert.AreEqual(1, RopeMath.DetachFrom(ropes, "i1"));
            Assert.AreEqual(1, ropes.Count);
            Assert.AreEqual("r2", ropes[0].Id);
        }

        [Test]
        public void DetachFrom_ToleratesNonsense()
        {
            Assert.AreEqual(0, RopeMath.DetachFrom(null, "i1"));
            Assert.AreEqual(0, RopeMath.DetachFrom(new List<Rope>(), null));
        }

        // ── colours ──────────────────────────────────────────────────────────────

        [Test]
        public void Colors_AreTheWebsSeven()
        {
            Assert.AreEqual(7, RopeMath.Colors.Length);
            Assert.AreEqual("#6b5836", RopeMath.Colors[0], "the default rope colour comes first");
            foreach (string c in RopeMath.Colors)
                Assert.IsTrue(SceneEdit.IsHexColor(c), c + " is not a valid hex colour");
        }

        [Test]
        public void NormaliseColor_FallsBackRatherThanWritingRubbish()
        {
            Assert.AreEqual("#b03a2e", RopeMath.NormaliseColor("#B03A2E"));
            Assert.AreEqual(RopeMath.DefaultColor, RopeMath.NormaliseColor("chartreuse"));
            Assert.AreEqual(RopeMath.DefaultColor, RopeMath.NormaliseColor(null));
        }

        [Test]
        public void NewId_IsUnique()
        {
            var seen = new HashSet<string>();
            for (int i = 0; i < 200; i++) Assert.IsTrue(seen.Add(RopeMath.NewId(i % 3)));
        }
    }
}
