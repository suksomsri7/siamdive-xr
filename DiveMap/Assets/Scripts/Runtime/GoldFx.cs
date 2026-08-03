using DiveMap.Core;
using UnityEngine;

namespace DiveMap.Runtime
{
    /// <summary>
    /// B8 — the two effects the web puts on its hero statues.
    ///
    /// <b>Gold glow</b> (<c>_fxGold</c> :1465). The web's own comment records that the sparkle
    /// sprites and light rays were REMOVED in v.0684 at the user's request, because an additive
    /// sprite is overdraw and cost frames for decoration. What is left is free: the model's own
    /// material re-authored as gold metal, no per-frame tick at all. Porting the removed version
    /// would be porting a decision that was already reversed. The property names and the numbers
    /// live in <see cref="GoldShading"/>, where a test can reach them — the first version of this
    /// wrote to properties glTFast's shader does not have and was a silent no-op for months.
    ///
    /// <b>Beard sway</b> (<c>_fxBeard</c> :1476). Vertex displacement through the shader, so no
    /// rig is needed: a hump-shaped mask over the model's height makes the beard and the robe
    /// drift while the crown and the plinth stay still. The web injects GLSL into three.js's
    /// shader; Unity's GLB materials cannot be patched the same way, so this moves the object's
    /// lower CHILDREN instead — the same visual rule, expressed with transforms.
    /// </summary>
    public static class GoldFx
    {
        /// <summary>
        /// Gold's base colour in LINEAR, from <see cref="GoldShading"/>. On a metal this is the
        /// tint of the reflection, not "the colour of the object".
        /// </summary>
        public static readonly Color GoldLinear = new Color(
            GoldShading.BaseColorLinearR, GoldShading.BaseColorLinearG, GoldShading.BaseColorLinearB, 1f);

        /// <summary>The web's emissive gold #ffb733, in LINEAR, already scaled to strength.</summary>
        public static readonly Color GoldEmissiveLinear = new Color(
            GoldShading.EmissiveLinearR * GoldShading.EmissiveStrength,
            GoldShading.EmissiveLinearG * GoldShading.EmissiveStrength,
            GoldShading.EmissiveLinearB * GoldShading.EmissiveStrength, 1f);

        /// <summary>Asset ids that get the treatment. The web tags these by hand too.</summary>
        public static bool IsGolden(string assetId) =>
            !string.IsNullOrEmpty(assetId) &&
            (assetId.Contains("golden") || assetId.Contains("trident") || assetId.Contains("poseidon"));

        public static bool HasBeard(string assetId) =>
            !string.IsNullOrEmpty(assetId) &&
            (assetId.Contains("stone_king") || assetId.Contains("poseidon"));

        /// <summary>First of <paramref name="names"/> this material actually has, or null.
        /// Same helper, same order-of-candidates rule as <c>QcModelShot.PropOn</c>.</summary>
        private static string PropOn(Material m, string[] names) =>
            GoldShading.FirstPresent(names, m.HasProperty);

        /// <summary>
        /// Turn every material on the object into gold metal. Materials are CLONED first: they
        /// come from the shared GLB import, and tinting one in place would turn every copy of that
        /// model on the map gold as well.
        ///
        /// 🔴 THIS USED TO DO NOTHING AT ALL — see <see cref="GoldShading"/> for the property list
        /// that proves it. Three writes, all three guarded on Unity Standard's property names, on
        /// materials that glTFast had put on <c>glTF/PbrMetallicRoughness</c>, which has none of
        /// them. Hence: candidates rather than names, the FULL metallic-roughness set rather than
        /// emission alone, and a log line per material that says what it could not find.
        ///
        /// No <c>Shader.Find</c> and no shader swap anywhere in here, deliberately: a shader
        /// reached only from code is stripped out of a player build and comes back magenta. This
        /// only ever writes properties on the material the importer already made.
        /// </summary>
        public static void ApplyGold(GameObject go)
        {
            if (go == null) return;
            int materials = 0, applied = 0;

            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.materials;   // .materials already clones
                for (int i = 0; i < mats.Length; i++)
                {
                    Material m = mats[i];
                    if (m == null) continue;
                    materials++;

                    string colorProp = PropOn(m, GoldShading.BaseColorNames);
                    string metalProp = PropOn(m, GoldShading.MetallicNames);
                    string roughProp = PropOn(m, GoldShading.RoughnessNames);
                    // Only one of the two conventions is written: whichever the shader speaks.
                    string glossProp = roughProp ?? PropOn(m, GoldShading.SmoothnessNames);
                    string emitProp = PropOn(m, GoldShading.EmissiveNames);

                    // Gamma for the plain Color property, linear for the [HDR] one — glTFast's own
                    // asymmetry (BuiltInMaterialGenerator.cs:383 vs :387), copied rather than
                    // rediscovered. Getting this backwards is a 2.2-power error, not a nuance.
                    if (colorProp != null) m.SetColor(colorProp, GoldLinear.gamma);
                    if (metalProp != null) m.SetFloat(metalProp, GoldShading.Metallic);
                    if (roughProp != null) m.SetFloat(roughProp, GoldShading.Roughness);
                    else if (glossProp != null) m.SetFloat(glossProp, GoldShading.Smoothness);
                    if (emitProp != null)
                    {
                        // The keyword is what compiles the emission branch in; the colour alone is
                        // read by nothing (glTFPbrMetallicRoughness.shader: shader_feature _EMISSION,
                        // and Emission() returns 0 without it).
                        m.EnableKeyword("_EMISSION");
                        m.SetColor(emitProp, GoldEmissiveLinear);
                        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    }

                    string texProp = PropOn(m, GoldShading.BaseColorTextureNames);
                    bool baseTex = texProp != null && m.GetTexture(texProp) != null;

                    // 🔴 One line per material, unconditionally, including the misses. `baseTex` is
                    // here because the base-colour texture MULTIPLIES the factor: a dark albedo
                    // atlas can swallow the gold with every property below reported as found, and
                    // that is the next question anyone debugging this will ask.
                    if (colorProp != null || metalProp != null || glossProp != null || emitProp != null) applied++;
                    Debug.Log($"[Fx] gold mat={m.name} obj={go.name} " +
                              $"shader={(m.shader != null ? m.shader.name : "(none)")} " +
                              $"{GoldShading.Report(colorProp, metalProp, glossProp, emitProp)} " +
                              $"baseTex={(baseTex ? "t" : "f")}");
                }
                r.materials = mats;
            }

            // The summary counts CHANGED as well as seen: "materials=3" on its own was the line
            // that let the no-op look like a working pass.
            Debug.Log($"[Fx] gold on {go.name} materials={materials} applied={applied}");
        }

        /// <summary>Attach the sway. Does nothing if the object has no separable lower parts.</summary>
        public static void ApplyBeard(GameObject go)
        {
            if (go == null || go.GetComponent<BeardSway>() != null) return;
            go.AddComponent<BeardSway>().Init();
        }
    }

    /// <summary>
    /// The beard/robe drift. One component per statue, no shader patching.
    ///
    /// The mask is the web's: nothing below 12 % of the model's height moves (the plinth),
    /// nothing above 80 % moves (the head and crown), and the middle sways with a phase that
    /// runs down the body so it reads as a current flowing through the hair rather than the
    /// whole statue wobbling.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BeardSway : MonoBehaviour
    {
        private Transform[] _parts;
        private Vector3[] _home;
        private float[] _mask;
        private float _amp;

        /// <summary>QC: how many child parts ended up in the sway.</summary>
        public int PartCount => _parts != null ? _parts.Length : 0;

        public void Init()
        {
            var bounds = new Bounds(transform.position, Vector3.zero);
            bool any = false;
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            if (!any) return;

            float minY = bounds.min.y;
            float range = Mathf.Max(0.001f, bounds.size.y);
            _amp = range * 0.014f;   // the web's rng*0.014

            var parts = new System.Collections.Generic.List<Transform>();
            var mask = new System.Collections.Generic.List<float>();
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (t == transform) continue;
                float h = (t.position.y - minY) / range;
                float m = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.12f, 0.36f, h)) *
                          (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.60f, 0.80f, h)));
                if (m <= 0.01f) continue;   // plinth and crown stay put
                parts.Add(t);
                mask.Add(m);
            }

            _parts = parts.ToArray();
            _mask = mask.ToArray();
            _home = new Vector3[_parts.Length];
            for (int i = 0; i < _parts.Length; i++) _home[i] = _parts[i].localPosition;

            Debug.Log($"[Fx] beard on {name} parts={_parts.Length} amp={_amp:F3}");
        }

        private void Update()
        {
            if (_parts == null || _parts.Length == 0) return;
            float t = Time.time;

            for (int i = 0; i < _parts.Length; i++)
            {
                if (_parts[i] == null) continue;
                float h = _mask[i];
                _parts[i].localPosition = _home[i] + new Vector3(
                    Mathf.Sin(t * 1.25f + h * 6f) * _amp * h,
                    0f,
                    Mathf.Cos(t * 1.0f + h * 5f) * _amp * 0.5f * h);
            }
        }
    }
}
