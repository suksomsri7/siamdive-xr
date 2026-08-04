using UnityEngine;

namespace DiveMap.Runtime
{
    /// <summary>
    /// B8 — the two effects the web puts on its hero statues.
    ///
    /// <b>Gold glow</b> (<c>_fxGold</c> :1465). The web's own comment records that the sparkle
    /// sprites and light rays were REMOVED in v.0684 at the user's request, because an additive
    /// sprite is overdraw and cost frames for decoration. What is left is free: emissive on the
    /// model's own material, no per-frame tick at all. Porting the removed version would be
    /// porting a decision that was already reversed.
    ///
    /// <b>Beard sway</b> (<c>_fxBeard</c> :1476). Vertex displacement through the shader, so no
    /// rig is needed: a hump-shaped mask over the model's height makes the beard and the robe
    /// drift while the crown and the plinth stay still. The web injects GLSL into three.js's
    /// shader; Unity's GLB materials cannot be patched the same way, so this moves the object's
    /// lower CHILDREN instead — the same visual rule, expressed with transforms.
    /// </summary>
    public static class GoldFx
    {
        /// <summary>#ffb733 — the web's emissive gold.</summary>
        public static readonly Color Gold = new Color(1f, 0.718f, 0.200f, 1f);

        /// <summary>
        /// Asset ids that get the treatment. The list — and the reason it is a list of ids rather
        /// than a substring test that used to gild two stone statues by accident — is in
        /// <see cref="DiveMap.Core.FxRules"/>, where a test on this machine can reach it.
        /// </summary>
        public static bool IsGolden(string assetId) => DiveMap.Core.FxRules.IsGolden(assetId);

        /// <inheritdoc cref="DiveMap.Core.FxRules.HasBeard"/>
        public static bool HasBeard(string assetId) => DiveMap.Core.FxRules.HasBeard(assetId);

        /// <summary>
        /// Make every material on the object glow gold. Materials are CLONED first: they come
        /// from the shared GLB import, and tinting one in place would turn every copy of that
        /// model on the map gold as well.
        /// </summary>
        public static void ApplyGold(GameObject go)
        {
            if (go == null) return;
            int touched = 0;

            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.materials;   // .materials already clones
                for (int i = 0; i < mats.Length; i++)
                {
                    Material m = mats[i];
                    if (m == null) continue;
                    if (m.HasProperty("_EmissionColor"))
                    {
                        m.EnableKeyword("_EMISSION");
                        m.SetColor("_EmissionColor", Gold * 0.5f);
                        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    }
                    if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.9f);
                    if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.75f);   // 1 − roughness 0.25
                    touched++;
                }
                r.materials = mats;
            }
            Debug.Log($"[Fx] gold on {go.name} materials={touched}");
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
