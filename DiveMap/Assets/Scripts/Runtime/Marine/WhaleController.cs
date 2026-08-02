using System.Collections.Generic;
using DiveMap.Core;
using UnityEngine;

namespace DiveMap.Runtime.Marine
{
    /// <summary>
    /// A large animal (whaleshark / manta) that swims a slow looping path around its
    /// placed anchor with a gentle vertical bob (WO-XR-03: "ว่ายวนช้า"). Orientation is
    /// driven ENTIRELY through <see cref="MarineMath.OrientationFromVelocity"/> — yaw
    /// follows travel, pitch follows the climb/dive angle (clamped ±0.5 rad), and roll
    /// is a literal 0. That is the anti-regression form of the web whale rule: because
    /// rotation.z is never written, the "stuck barrel-roll / frozen dive-pitch" bug that
    /// bit the web build cannot recur here.
    ///
    /// The web whaleshark actually free-roams within roamR; a deterministic ellipse loop
    /// is a faithful, on-screen-stable interpretation for the mobile viewer + QC shot,
    /// and the vertical bob exercises the pitch path every lap.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WhaleController : MonoBehaviour
    {
        public Vector3 anchor;
        public float radiusX = 45f;
        public float radiusZ = 55f;
        public float angularSpeed = 0.10f; // rad/sec (~60s per lap)
        public float bobAmp = 6f;
        public float bobFreq = 0.25f;

        private float _angle;
        private float _t;
        private Vector3 _lastPos;
        private bool _primed;

        // ── Body wave (WO-XR: "ปลาว่ายไม่สมจริง ตัวแข็งมาก", 3rd report) ──────────────
        // 🔴 This is the animal in the screenshot. A big animal is a REAL GLB with `skins 0,
        // anims 0` — the manifest's "animated" flag is parsed and then never used, and nothing
        // ever played a clip — so up to now the hero shark orbited the kraken as one solid
        // extruded block. The schools got DM_FishWave and the one animal a diver actually stops
        // to look at got nothing.
        private readonly List<Material> _waveMats = new List<Material>();
        private double _wavePhase;
        private float  _beatHz;
        private float  _cruise = 1f;
        private const double TwoPi = 6.283185307179586;

        private static readonly int IdWaveLen    = Shader.PropertyToID("_WaveLen");
        private static readonly int IdWaveSpan   = Shader.PropertyToID("_WaveSpan");
        private static readonly int IdWaveAmp    = Shader.PropertyToID("_WaveAmp");
        private static readonly int IdWaveCycles = Shader.PropertyToID("_WaveCycles");
        private static readonly int IdWaveAnchor = Shader.PropertyToID("_WaveAnchor");
        private static readonly int IdWaveRecoil = Shader.PropertyToID("_WaveRecoil");
        private static readonly int IdWaveGust   = Shader.PropertyToID("_WaveGust");
        private static readonly int IdWaveEffort = Shader.PropertyToID("_WaveEffort");
        private static readonly int IdWavePhase  = Shader.PropertyToID("_WavePhase");
        private static readonly int IdWaveMode   = Shader.PropertyToID("_WaveMode");
        private static readonly int IdWaveFwd    = Shader.PropertyToID("_WaveFwd");
        private static readonly int IdWaveSide   = Shader.PropertyToID("_WaveSide");
        private static readonly int IdWaveDir    = Shader.PropertyToID("_WaveDir");

        /// <summary>
        /// Place the loop and give the animal its body wave.
        /// </summary>
        /// <param name="anchorPos">Where the item was placed.</param>
        /// <param name="size">The animal's true world length (u) — 65 for the whaleshark.</param>
        /// <param name="assetId">
        /// e.g. <c>msh:whaleshark</c> — picks the gait. Optional so the existing two-argument
        /// call in SceneBuilder still compiles; when it is omitted the id is recovered from the
        /// pivot's name, which SceneBuilder builds as <c>Item_{id}_{assetId}</c>.
        /// </param>
        public void Init(Vector3 anchorPos, float size, string assetId = null)
        {
            anchor = anchorPos;

            // ── Viewer/QC framing bias ────────────────────────────────────────────
            // The whaleshark is placed high in the water column (Htms Chang: web y≈154,
            // just under the mast top) and well off to one side. The opening shot frames
            // the WRECK box — it aims low (~y45) and looks up only ~12° above the horizon,
            // so a hero animal at y154 sweeps clean off the top of the frame (QC r5: whale
            // absent). The web's whaleshark is a free-roamer, not a fixed placement, so the
            // class already treats the loop as a "faithful interpretation" rather than data.
            // Dip that loop down toward the wreck and reel it in horizontally so the big
            // animal actually reads in the shot — proportional, so a low/near whale barely
            // moves while a sky-high far one is brought home.
            anchor.y -= Mathf.Clamp(anchor.y * 0.35f, 0f, 60f); // ~154 → ~100 (into the framed band)
            anchor.x *= 0.65f;                                  // pull toward the wreck (content sits near origin)
            anchor.z *= 0.65f;

            // Loop scaled to the animal's size so a big whaleshark sweeps a real arc, but
            // tighter than before so the sweep can't carry it back out of frame. WO-XR-03b
            // made the whaleshark its true world length (1.908×34.2 ≈ 65 u instead of the old
            // [8..16] clamp), so the old 1.0/1.2 multipliers would have quadrupled the lap and
            // swung the animal off-screen — 0.6/0.72 keeps the same on-screen sweep.
            radiusX = Mathf.Max(14f, size * 0.60f);
            radiusZ = Mathf.Max(16f, size * 0.72f);
            bobAmp = Mathf.Max(3f, size * 0.15f);
            transform.position = PathPoint(0f);
            _lastPos = transform.position;

            // Cruise speed of the loop, so Effort reads ~1 while it is going round normally.
            _cruise = Mathf.Max(0.2f, angularSpeed * Mathf.Max(radiusX, radiusZ));
            ApplyBodyWave(assetId ?? AssetIdFromName(gameObject.name), size);
        }

        /// <summary>
        /// Recover <c>msh:whaleshark</c> from the pivot GameObject's <c>Item_7_msh:whaleshark</c>.
        /// Splitting on '_' is not an option — <c>msh:tiger_shark</c> contains one.
        /// </summary>
        internal static string AssetIdFromName(string goName)
        {
            if (string.IsNullOrEmpty(goName)) return "";
            int i = goName.IndexOf("msh:", System.StringComparison.OrdinalIgnoreCase);
            return i >= 0 ? goName.Substring(i) : goName;
        }

        /// <summary>
        /// Swap every renderer under this pivot onto a wave material and aim the wave down the
        /// animal's own body. Entirely separate from the orientation above: the no-roll rule
        /// stands, and it has to — a whale shark rolling into its turn is the regression this
        /// project has already paid for. The BEND is what makes it read as alive, not the roll.
        /// </summary>
        private void ApplyBodyWave(string assetId, float worldLen)
        {
            _waveMats.Clear();

            var rends = GetComponentsInChildren<Renderer>(true);
            if (rends == null || rends.Length == 0) return;

            // DM_FishWaveDetail keeps the model's normal/emissive maps; DM_FishWave is the same
            // motion with base colour only. Both live in Resources because a shader reached only
            // from code is stripped from the build and comes back magenta on the device. If
            // neither loads, the animal keeps its glTFast material and simply does not bend —
            // today's behaviour, not a broken one.
            Material src = Resources.Load<Material>("DM_FishWaveDetail")
                        ?? Resources.Load<Material>("DM_FishWave");
            if (src == null || src.shader == null || !src.HasProperty(IdWavePhase))
            {
                Debug.LogWarning("[Marine] ไม่พบ DM_FishWaveDetail/DM_FishWave — สัตว์ใหญ่จะไม่โค้งตัว");
                return;
            }

            if (SwimStyle.IsStill(assetId)) return;
            SwimWave w = SwimStyle.For(assetId, worldLen);

            int swapped = 0;
            for (int i = 0; i < rends.Length; i++)
            {
                Renderer r = rends[i];
                if (r == null || r is ParticleSystemRenderer) continue;

                Mesh mesh = MeshOf(r);
                if (mesh == null) continue;

                // The shader bends vertices in the space this renderer is DRAWN in, but the swim
                // heading lives on the pivot. This is the map between them — a GLB whose mesh
                // node is rotated (glTF is Y-up, Unity is not) would otherwise be bent across its
                // width instead of along its length.
                //
                // Renderer.localToWorldMatrix, not transform.localToWorldMatrix: for a
                // SkinnedMeshRenderer those are different things, and the renderer's is the one
                // that ends up in unity_ObjectToWorld.
                Matrix4x4 toMesh = r.localToWorldMatrix.inverse * transform.localToWorldMatrix;
                Vector3 fwd  = SafeDir(toMesh.MultiplyVector(Vector3.forward), Vector3.forward);
                Vector3 side = SafeDir(toMesh.MultiplyVector(Vector3.right),   Vector3.right);
                Vector3 up   = SafeDir(toMesh.MultiplyVector(Vector3.up),      Vector3.up);

                Vector3 ms = mesh.bounds.size;   // metadata — safe on a non-readable Draco mesh
                float meshLen  = (float)SwimStyle.AxisExtent(ms.x, ms.y, ms.z, fwd.x, fwd.y, fwd.z);
                float meshSpan = 0.5f * (float)SwimStyle.AxisExtent(ms.x, ms.y, ms.z, side.x, side.y, side.z);
                if (meshLen < 1e-5f) continue;

                // Every slot, not just slot 0 — a hero GLB with a separate material for the eyes
                // or the fins would otherwise leave part of the animal behind, rigid, while the
                // rest of it swam away.
                Material[] slots = r.sharedMaterials;
                if (slots == null || slots.Length == 0) continue;
                var swap = new Material[slots.Length];
                for (int s = 0; s < slots.Length; s++)
                {
                    Material m = new Material(src);
                    CopyMaps(slots[s], m);

                    m.SetFloat(IdWaveLen,    meshLen);
                    m.SetFloat(IdWaveSpan,   Mathf.Max(1e-4f, meshSpan));
                    m.SetFloat(IdWaveAmp,    (float)w.Amp);
                    m.SetFloat(IdWaveCycles, (float)w.Cycles);
                    m.SetFloat(IdWaveAnchor, 1f);
                    m.SetFloat(IdWaveRecoil, (float)w.Recoil);
                    m.SetFloat(IdWaveGust,   (float)w.Gust);
                    m.SetFloat(IdWaveMode,   w.Gait == SwimGait.Wing ? 1f : 0f);
                    m.SetFloat(IdWaveEffort, 1f);
                    m.SetFloat(IdWavePhase,  0f);
                    m.SetVector(IdWaveFwd,  fwd);
                    m.SetVector(IdWaveSide, side);
                    m.SetVector(IdWaveDir,  w.Gait == SwimGait.Body ? side : up);

                    swap[s] = m;
                    _waveMats.Add(m);
                }
                r.sharedMaterials = swap;
                swapped++;
            }

            _beatHz = (float)w.BeatHz;

            Debug.Log($"[Swim] whale asset={assetId} gait={w.Gait} renderers={swapped}/{rends.Length} " +
                      $"worldLen={worldLen:F1} beatHz={w.BeatHz:F2} amp={w.Amp:F3} " +
                      $"ampWorld={(w.Amp * worldLen):F2} cruise={_cruise:F2} " +
                      $"shader={src.shader.name}");
        }

        /// <summary>
        /// Carry the GLB's own maps across to the wave material. glTFast names its slots
        /// <c>baseColorTexture</c> / <c>normalTexture</c> / <c>emissiveTexture</c> — there is no
        /// <c>_MainTex</c> on those shader variants — and every read is HasProperty-guarded
        /// because asking a glTFast material for a property it never declared logs per call.
        /// </summary>
        private static void CopyMaps(Material from, Material to)
        {
            if (from == null || to == null) return;

            Texture baseCol = Get(from, "baseColorTexture") ?? Get(from, "_MainTex");
            if (baseCol != null && to.HasProperty("_MainTex")) to.SetTexture("_MainTex", baseCol);

            Texture nrm = Get(from, "normalTexture") ?? Get(from, "_BumpMap");
            if (nrm != null && to.HasProperty("_BumpMap")) to.SetTexture("_BumpMap", nrm);

            Texture emi = Get(from, "emissiveTexture") ?? Get(from, "_EmissionMap");
            if (emi != null && to.HasProperty("_EmissionMap"))
            {
                to.SetTexture("_EmissionMap", emi);
                // No EnableKeyword("_EMISSION") — a runtime keyword is stripped from the build and
                // comes back magenta. DM_FishWaveDetail samples the map unconditionally instead.
                if (to.HasProperty("_EmissionColor")) to.SetColor("_EmissionColor", Color.white);
            }

            // glTF's own baseColorFactor × baseColorTexture, which is exactly _Color × _MainTex.
            // Falling back to _Color covers the placeholder path: when the GLB never downloaded,
            // SceneBuilder leaves a palette-coloured primitive here, and forcing white would turn
            // a failed load into a white blob instead of the marker it is meant to be.
            if (to.HasProperty("_Color"))
            {
                Color tint = Color.white;
                if (from.HasProperty("baseColorFactor")) tint = from.GetColor("baseColorFactor");
                else if (from.HasProperty("_Color"))     tint = from.GetColor("_Color");
                to.SetColor("_Color", tint);
            }

            // Roughness/metallic FACTORS only. glTF packs the maps into one texture (G = rough,
            // B = metal) that Standard cannot read in that layout, so the scalars are the honest
            // approximation rather than a wrong sample.
            if (to.HasProperty("_Glossiness") && from.HasProperty("roughnessFactor"))
                to.SetFloat("_Glossiness", Mathf.Clamp01(1f - from.GetFloat("roughnessFactor")));
            if (to.HasProperty("_Metallic") && from.HasProperty("metallicFactor"))
                to.SetFloat("_Metallic", Mathf.Clamp01(from.GetFloat("metallicFactor")));
        }

        private static Texture Get(Material m, string prop)
            => m != null && m.HasProperty(prop) ? m.GetTexture(prop) : null;

        private static Mesh MeshOf(Renderer r)
        {
            if (r is SkinnedMeshRenderer smr) return smr.sharedMesh;
            var mf = r.GetComponent<MeshFilter>();
            return mf != null ? mf.sharedMesh : null;
        }

        private static Vector3 SafeDir(Vector3 v, Vector3 fallback)
            => v.sqrMagnitude > 1e-8f ? v.normalized : fallback;

        private void Update()
        {
            float fs = (float)MarineMath.RealDeltaScale(Time.deltaTime);
            float dt = (float)MarineMath.BaseStep * fs; // real-delta step
            _t += dt;
            _angle += angularSpeed * dt;

            Vector3 pos = PathPoint(_angle);
            transform.position = pos;

            Vector3 vel = _primed ? (pos - _lastPos) / Mathf.Max(1e-5f, dt) : Vector3.forward;
            _lastPos = pos;
            _primed = true;

            MarineMath.Orientation o = MarineMath.OrientationFromVelocity(vel.x, vel.y, vel.z);
            transform.rotation = Quaternion.Euler(
                (float)(o.PitchRad * Mathf.Rad2Deg),
                (float)(o.YawRad   * Mathf.Rad2Deg),
                0f); // roll forced 0 — no-roll rule

            // Advance the body wave. Integrated on the CPU, never sin(_Time.y · rate): with
            // _Time.y the tail jumps by hundreds of radians the moment the rate changes.
            if (_waveMats.Count > 0)
            {
                float effort = (float)SwimStyle.Effort(vel.magnitude, _cruise);
                _wavePhase += SwimStyle.BeatPhaseStep(_beatHz * effort, dt);
                if (_wavePhase >= TwoPi) _wavePhase %= TwoPi;
                for (int i = 0; i < _waveMats.Count; i++)
                {
                    Material m = _waveMats[i];
                    if (m == null) continue;
                    m.SetFloat(IdWavePhase, (float)_wavePhase);
                    m.SetFloat(IdWaveEffort, effort);
                }
            }

            // QC oracle (r8): a still screenshot cannot prove the GLB's own forward axis is +Z.
            // Log the alignment once, a few frames in — dot ≈ +1 means the model swims nose-first,
            // ≈ −1 means the GLB faces backwards and its child needs a 180° yaw.
            if (!_headingLogged && _t > 0.5f && vel.sqrMagnitude > 1e-6f)
            {
                _headingLogged = true;
                Debug.Log($"[Marine] whale heading dot(forward,vel)={Vector3.Dot(transform.forward, vel.normalized):F3} " +
                          "(expect ≈ +1.0)");
            }
        }

        private bool _headingLogged;

        private Vector3 PathPoint(float angle)
        {
            float y = anchor.y + Mathf.Sin(_t * bobFreq) * bobAmp;
            return new Vector3(
                anchor.x + Mathf.Cos(angle) * radiusX,
                y,
                anchor.z + Mathf.Sin(angle) * radiusZ);
        }
    }
}
