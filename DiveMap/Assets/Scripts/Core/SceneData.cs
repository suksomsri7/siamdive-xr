using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DiveMap.Core
{
    /// <summary>
    /// Wraps a builder scene JSON (DESIGN_DOC §1.2, the frozen contract from
    /// builder.html serialize()) as a live JObject.
    ///
    /// CRITICAL RULE (DESIGN_DOC §1.2 rule 1 / QC_PLAN §1.1 "Scene JSON preserve"):
    /// unknown / future fields MUST survive load → save unchanged. We therefore
    /// keep the parsed JObject as the single backing store and expose typed
    /// accessors that read/write INTO that same JObject. We never deserialize into
    /// POCOs that would silently drop fields (the PATCH upsert-overwrite lesson).
    /// </summary>
    public sealed class SceneData
    {
        public JObject Root { get; }

        public SceneData() { Root = new JObject(); }
        public SceneData(JObject root) { Root = root ?? new JObject(); }

        public static SceneData Parse(string json)
        {
            // Do not mutate / reformat: preserve declared property order & values.
            return new SceneData(JObject.Parse(json));
        }

        /// <summary>Serialize back. Structurally identical to input for untouched fields.</summary>
        public string ToJson(Formatting formatting = Formatting.None) => Root.ToString(formatting);

        public string Name
        {
            get => (string)Root["name"];
            set => Root["name"] = value;
        }

        public IReadOnlyList<SceneItem> Items()
        {
            var list = new List<SceneItem>();
            if (Root["items"] is JArray arr)
            {
                foreach (var t in arr)
                {
                    if (t is JObject o) list.Add(new SceneItem(o));
                }
            }
            return list;
        }

        public IReadOnlyList<ScenePin> Pins()
        {
            var list = new List<ScenePin>();
            if (Root["pins"] is JArray arr)
            {
                foreach (var t in arr)
                {
                    if (t is JObject o) list.Add(new ScenePin(o));
                }
            }
            return list;
        }

        public SceneEnv Env => Root["env"] is JObject o ? new SceneEnv(o) : null;

        // ── shared JToken helpers (keep unknown fields intact) ────────────────────
        internal static double[] ReadVec3(JObject o, string key)
        {
            if (o[key] is JArray a && a.Count >= 3)
                return new[] { (double)a[0], (double)a[1], (double)a[2] };
            return null;
        }

        internal static void WriteVec3(JObject o, string key, double[] v)
        {
            o[key] = new JArray(v[0], v[1], v[2]);
        }
    }

    /// <summary>One entry of "items". Backed by the underlying JObject.</summary>
    public sealed class SceneItem
    {
        public JObject Token { get; }
        public SceneItem(JObject token) { Token = token; }

        public string Id { get => (string)Token["id"]; set => Token["id"] = value; }
        public string AssetId { get => (string)Token["assetId"]; set => Token["assetId"] = value; }

        public double[] P { get => SceneData.ReadVec3(Token, "p"); set => SceneData.WriteVec3(Token, "p", value); }
        public double[] R { get => SceneData.ReadVec3(Token, "r"); set => SceneData.WriteVec3(Token, "r", value); }
        public double[] S { get => SceneData.ReadVec3(Token, "s"); set => SceneData.WriteVec3(Token, "s", value); }

        // Optional fields — return null when absent, never fabricate.
        public string Color { get => (string)Token["c"]; set => Token["c"] = value; }
        public string WarpTarget { get => (string)Token["wt"]; set => Token["wt"] = value; }
        public string WarpName { get => (string)Token["wn"]; set => Token["wn"] = value; }
        public string Label { get => (string)Token["lb"]; set => Token["lb"] = value; }

        public bool Has(string field) => Token[field] != null;
    }

    /// <summary>One entry of "pins".</summary>
    public sealed class ScenePin
    {
        public JObject Token { get; }
        public ScenePin(JObject token) { Token = token; }

        public string Id { get => (string)Token["id"]; set => Token["id"] = value; }
        public double[] P { get => SceneData.ReadVec3(Token, "p"); set => SceneData.WriteVec3(Token, "p", value); }
        public JArray Media => Token["media"] as JArray;
    }

    /// <summary>The "env" object (water level, area transform, sculpt, ropes …).</summary>
    public sealed class SceneEnv
    {
        public JObject Token { get; }
        public SceneEnv(JObject token) { Token = token; }

        private double GetD(string key, double fallback)
            => Token[key] != null ? (double)Token[key] : fallback;

        public double WaterLevel => GetD("waterLevel", 0);
        public double AreaScale => GetD("areaScale", 1);
        public double AreaScaleX => GetD("areaScaleX", 1);
        public double AreaScaleZ => GetD("areaScaleZ", 1);
        public double AreaThickness => GetD("areaThickness", 0);
        public double AreaSlopeX => GetD("areaSlopeX", 0);
        public double AreaSlopeZ => GetD("areaSlopeZ", 0);

        public JArray Sculpt => Token["sculpt"] as JArray;
        public JArray SculptDim => Token["sculptDim"] as JArray;
        public JArray Ropes => Token["ropes"] as JArray;

        /// <summary>(rings, seg) from sculptDim, or null when absent.</summary>
        public int[] SculptDimensions()
        {
            if (Token["sculptDim"] is JArray a && a.Count >= 2)
                return new[] { (int)a[0], (int)a[1] };
            return null;
        }
    }
}
