using DiveMap.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DiveMap.Tests
{
    public class SceneDataTests
    {
        // A scene that mirrors the DESIGN_DOC §1.2 contract AND seeds a "futureField"
        // (unknown to this client) at every nesting level: root, item, pin, env.
        private const string SceneJson = @"{
  ""name"": ""Sail Rock"",
  ""futureField"": { ""a"": 1, ""nested"": [true, null, ""x""] },
  ""items"": [
    {
      ""id"": ""m1a2b3"",
      ""assetId"": ""coral:2"",
      ""p"": [1.5, 0.0, -3.25],
      ""r"": [0.0, 1.5707963267948966, 0.0],
      ""s"": [1, 1, 1],
      ""c"": ""#ff8800"",
      ""lb"": ""Boulder"",
      ""futureField"": { ""experimentalFlag"": true }
    },
    {
      ""id"": ""m4c5d6"",
      ""assetId"": ""ckmodulecuid0001"",
      ""p"": [-2, 0.4, 5],
      ""r"": [0, 0, 0],
      ""s"": [2, 2, 2],
      ""wt"": ""warpA"",
      ""wn"": ""Cave""
    }
  ],
  ""pins"": [
    {
      ""id"": ""p1"",
      ""p"": [0, 1, 0],
      ""media"": [{ ""type"": ""image"", ""url"": ""https://x/y.jpg"" }],
      ""futureField"": 42
    }
  ],
  ""env"": {
    ""waterLevel"": 4.2,
    ""areaScale"": 1,
    ""areaScaleX"": 1.1,
    ""areaScaleZ"": 0.9,
    ""areaThickness"": 0.4,
    ""areaSlopeX"": 0,
    ""areaSlopeZ"": 0,
    ""sculpt"": [0, 1, 2, 3, 4],
    ""sculptDim"": [8, 32],
    ""ropes"": [{ ""a"": { ""mid"": ""m1a2b3"", ""lp"": 0 }, ""b"": { ""mid"": ""m4c5d6"", ""lp"": 1 }, ""sag"": 0.3, ""color"": ""#fff"", ""thick"": 0.02 }],
    ""futureField"": { ""a"": 1 }
  }
}";

        [Test]
        public void LoadSave_PreservesUnknownFields_DeepEqual()
        {
            JObject original = JObject.Parse(SceneJson);

            SceneData scene = SceneData.Parse(SceneJson);
            string saved = scene.ToJson();
            JObject reparsed = JObject.Parse(saved);

            Assert.IsTrue(JToken.DeepEquals(original, reparsed),
                "Save must be structurally identical to load (unknown fields must not drop).");
        }

        [Test]
        public void LoadSave_PreservesUnknownFields_AfterKnownFieldMutation()
        {
            // Mutating a KNOWN field must not disturb sibling unknown fields.
            JObject original = JObject.Parse(SceneJson);
            SceneData scene = SceneData.Parse(SceneJson);

            var item0 = scene.Items()[0];
            item0.P = new[] { 9.0, 9.0, 9.0 };

            JObject reparsed = JObject.Parse(scene.ToJson());

            // futureField on the mutated item survives untouched.
            Assert.IsTrue(JToken.DeepEquals(
                original["items"][0]["futureField"],
                reparsed["items"][0]["futureField"]));
            // Root-level unknown field survives.
            Assert.IsTrue(JToken.DeepEquals(original["futureField"], reparsed["futureField"]));
            // The mutation actually applied.
            Assert.AreEqual(9.0, (double)reparsed["items"][0]["p"][0], 1e-12);
        }

        [Test]
        public void TypedAccessors_ReadKnownFields()
        {
            SceneData scene = SceneData.Parse(SceneJson);

            Assert.AreEqual("Sail Rock", scene.Name);
            Assert.AreEqual(2, scene.Items().Count);
            Assert.AreEqual(1, scene.Pins().Count);

            var i0 = scene.Items()[0];
            Assert.AreEqual("m1a2b3", i0.Id);
            Assert.AreEqual("coral:2", i0.AssetId);
            Assert.AreEqual(-3.25, i0.P[2], 1e-12);
            Assert.AreEqual("#ff8800", i0.Color);
            Assert.AreEqual("Boulder", i0.Label);
            Assert.IsNull(i0.WarpTarget); // absent optional -> null, never fabricated

            var i1 = scene.Items()[1];
            Assert.AreEqual("warpA", i1.WarpTarget);
            Assert.AreEqual("Cave", i1.WarpName);
            Assert.IsNull(i1.Color);
        }

        [Test]
        public void Env_ReadsValuesAndSculptDim()
        {
            SceneData scene = SceneData.Parse(SceneJson);
            var env = scene.Env;
            Assert.IsNotNull(env);
            Assert.AreEqual(4.2, env.WaterLevel, 1e-12);
            Assert.AreEqual(1.1, env.AreaScaleX, 1e-12);
            Assert.AreEqual(0.4, env.AreaThickness, 1e-12);

            var dim = env.SculptDimensions();
            Assert.IsNotNull(dim);
            Assert.AreEqual(8, dim[0]);
            Assert.AreEqual(32, dim[1]);
            Assert.AreEqual(5, env.Sculpt.Count);
            Assert.AreEqual(1, env.Ropes.Count);
        }

        [Test]
        public void Pin_ReadsMedia()
        {
            SceneData scene = SceneData.Parse(SceneJson);
            var pin = scene.Pins()[0];
            Assert.AreEqual("p1", pin.Id);
            Assert.AreEqual(1, pin.Media.Count);
            Assert.AreEqual("image", (string)pin.Media[0]["type"]);
        }
    }
}
