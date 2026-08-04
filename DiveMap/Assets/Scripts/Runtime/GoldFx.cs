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
    /// <b>Beard sway — REMOVED, and it should never have been ported.</b> <c>_fxBeard</c> still
    /// exists in builder.html:1476, and that is the trap: it is DEAD CODE there. An effect only
    /// runs on the web if its catalog row carries an <c>fx:</c> field, and exactly ONE row in the
    /// whole catalog does — <c>sw:golden_trident</c>, <c>fx:'gold'</c> (builder.html:1227).
    /// <c>sw:stone_king</c>'s row says so out loud: "รูปปั้นกษัตริย์ static (ยกเลิกเคราพริ้วตาม
    /// user 2026-07-04)" (builder.html:1228). The user had already asked for this to stop, on the
    /// web, a month before it was ported here.
    ///
    /// 🔴 This file's own remark about the gold sparkles — "porting the removed version would be
    /// porting a decision that was already reversed" — was the right rule applied to the wrong
    /// half. The beard was the removed version.
    ///
    /// And the port was louder than the original: the web displaced VERTICES inside one mesh
    /// through a shader, whereas <c>BeardSway</c> moved whole child TRANSFORMS, matched by the
    /// substring <c>poseidon</c>/<c>stone_king</c> against the asset id with no kind gate at all.
    /// So <c>sw:stone_king</c>, <c>cc0:poseidon</c> and <c>stat:verdant_poseidon</c> — three
    /// SPECIAL statues — visibly shifted their own parts every frame, in edit mode too, since
    /// nothing froze it. Build 261: "รูปปั้น/สิ่งก่อสร้างบางชิ้นขยับได้เอง".
    ///
    /// The gold glow below is untouched: it is a material property with no per-frame tick, and
    /// the web really does apply it.
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
    }
}
