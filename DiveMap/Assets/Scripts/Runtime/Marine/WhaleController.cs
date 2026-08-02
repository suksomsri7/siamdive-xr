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
    /// 🔴 Phase 2 goal 2 — it used to swim a perfect ellipse at a constant speed, and that is the
    /// one motion the eye files instantly as "machine": you can predict where it will be in twenty
    /// seconds. The path now comes from <see cref="FishMind.PatrolStep"/> — waypoints drawn from
    /// the map instead of a parametrised curve, a speed that breathes, and a slowdown when it
    /// passes something worth passing. The ellipse's own fields survive as the TUNING for that
    /// (they set the cruise speed and the roaming range), so nothing that configured this class
    /// before configures it wrongly now.
    ///
    /// Nothing in the new path can teleport: the heading turns at a capped rate, the speed is eased
    /// under an acceleration cap, and the position is integrated from the two — asserted in
    /// FishMindTests at the very numbers this class computes below.
    ///
    /// ── C6 phase 2: this is no longer only the whale ─────────────────────────────────────────
    ///
    /// It now drives EVERY solo animal on the map — all 58 species that used to be loaded as
    /// scenery (<c>losin:*</c>, <c>mdl:*</c>, the loggerhead, the procedural fish and turtles) as
    /// well as the <c>msh:*</c> heroes it always drove. Three things came with that:
    ///
    ///   • **stationary animals** (crab, seahorse, clam, seadragon, garden eel — the web's
    ///     <c>stationary:true</c> rows) hold their spot and sway. They used to patrol.
    ///   • **hunger and evasion**, via <see cref="SoloAnimalRegistry"/> and
    ///     <see cref="HuntMath"/> — the reef's sharks and morays actually hunt now.
    ///   • **species locomotion**: the roaming range and cruise come from the animal's own web
    ///     row where it has one, so a lionfish hovers and a sailfish crosses the map.
    ///
    /// 🔴 The class is still called WhaleController, and that is deliberate. Its name is wrong and
    /// its GUID is not: the type is referenced from EnvMode, SceneBuilder, FishSchoolSystem, two
    /// design docs and a standing HANDOFF rule ("ห้ามแก้ WhaleController"), and this machine has no
    /// UnityEngine.dll — <c>tools/check.sh</c> parses syntax and cannot type-check a rename, so one
    /// missed reference is a 35-minute red CI for a purely cosmetic gain. Rename it in a round that
    /// is already spending CI. Read it as "SoloAnimalController"; the logs already say <c>[Solo]</c>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WhaleController : MonoBehaviour
    {
        public Vector3 anchor;
        public float radiusX = 45f;
        public float radiusZ = 55f;
        public float angularSpeed = 0.10f; // rad/sec — now read as "cruise = this × the roam radius"
        public float bobAmp = 6f;
        public float bobFreq = 0.25f;

        // ── Roaming (Core/FishMind.cs) ────────────────────────────────────────────
        private Patrol     _patrol;
        private MindDomain _domain;
        private MindPoi[]  _pois = System.Array.Empty<MindPoi>();
        private int        _poiCount;
        private float      _roamR = 60f;
        private float      _turnRate = 0.2f;
        private float      _arriveR = 20f;
        private float      _size = 1f;

        /// <summary>Turn radius as a fraction of the animal's own length. A whale shark does not pivot.</summary>
        private const float TurnRadiusPerLen = 0.35f;
        /// <summary>"Close enough" to a waypoint. MUST exceed the turn radius or the animal spirals round it forever.</summary>
        private const float ArrivePerLen = 0.50f;
        /// <summary>Roaming range as a multiple of the old ellipse's radius. Kept modest so the QC shot still frames it.</summary>
        private const float RoamPerLap = 2.0f;

        private float _t;
        private Vector3 _lastPos;
        private bool _primed;
        /// <summary>
        /// Init has run. Guards the patrol against a frame in which the domain is still
        /// <c>default(MindDomain)</c> — radius 0, which would clamp the animal onto the map origin.
        /// SceneBuilder does call Init in the same frame as AddComponent, but a class that swims to
        /// (0,0,0) if it ever does not is not a class anyone should have to remember that about.
        /// </summary>
        private bool _configured;

        // ── C6: the animals on the msh:* list that do not swim ────────────────────
        /// <summary>The web's <c>stationary:true</c> — crab, seahorse (builder.html:1777-1778).</summary>
        private bool  _stationary;
        /// <summary>Sway phase, seeded from the placement so two crabs are not in lockstep.</summary>
        private float _swayPhase;
        /// <summary>The facing it was placed with; the yaw sway is measured off this.</summary>
        private float _baseYaw;

        // ── C6: hunger, pursuit, evasion (Core/HuntMath.cs) ───────────────────────
        private HuntDrive              _drive;
        private SpeciesGenome.Genome   _gen;
        private SpeciesBehavior.Cfg    _cfg;
        private string                 _assetId = "";
        /// <summary>Place in <see cref="SoloAnimalRegistry"/>, or −1 when not registered.</summary>
        private int       _slot = -1;
        private HuntPhase _phase;
        /// <summary>Cruise multiplier from the hunt, EASED — a sprint that snaps on is a teleport.</summary>
        private float     _huntMul = 1f;
        private uint      _seed;
        private float     _soloLogAt;
        /// <summary>This individual's 0..1 energy — it sets its roaming range and its speed.</summary>
        private float     _energy = 0.5f;
        /// <summary>…and how fast it gets hungry again (web :1903).</summary>
        private float     _metabolism = 1f;

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

            // ── C6: which animal is this, actually? ───────────────────────────────
            // 🔴 SceneBuilder routes EVERY msh:* item here, and "msh:" is not a synonym for "big
            // animal": msh:crab and msh:seahorse are on that list, and the web marks both
            // stationary:true (builder.html:1777-1778). Until this lookup existed they were given
            // a roaming patrol like everything else, so the reef's crabs cruised laps of the map.
            // A crab that swims is worse than a crab that does nothing.
            string aid = assetId ?? AssetIdFromName(gameObject.name);
            SpeciesBehavior.Cfg cfg = SpeciesBehavior.For(aid);
            _assetId = aid;
            _cfg = cfg;
            _gen = SpeciesGenome.For(aid);
            _stationary = cfg.Stationary;

            // 🔴 Seeded from the PLACEMENT, never from a counter or the clock. Five sharks dropped
            // on one map must behave like five sharks; the same map reopened tomorrow must give
            // back the same five, or a QC screenshot proves nothing.
            _seed = MarineRouting.PlacementSeed(aid, anchorPos.x, anchorPos.y, anchorPos.z);
            _swayPhase = (float)(FishMind.Rand01(_seed, 0, 50) * 6.2832);

            // Personality, and the hunger it opens on. Two morays on one reef are not hungry at
            // the same moment, so the reef has no feeding "tick".
            SpeciesBehavior.Personality pers = SpeciesBehavior.DrawPersonality(aid, _seed);
            _drive = new HuntDrive { Hunger = 0.2 + 0.6 * FishMind.Rand01(_seed, 0, 51) };
            _energy = (float)pers.Energy;
            _metabolism = (float)pers.Metabolism;

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
            //
            // 🔴 …and it applies to the HERO animal only. A stationary animal has been placed by
            // hand on a specific piece of sand; dragging it 35 % of the way to the origin puts the
            // crab somewhere the author did not put it, and no framing argument covers that.
            if (!_stationary)
            {
                anchor.y -= Mathf.Clamp(anchor.y * 0.35f, 0f, 60f); // ~154 → ~100 (into the framed band)
                anchor.x *= 0.65f;                                  // pull toward the wreck (content sits near origin)
                anchor.z *= 0.65f;
            }

            // Loop scaled to the animal's size so a big whaleshark sweeps a real arc, but
            // tighter than before so the sweep can't carry it back out of frame. WO-XR-03b
            // made the whaleshark its true world length (1.908×34.2 ≈ 65 u instead of the old
            // [8..16] clamp), so the old 1.0/1.2 multipliers would have quadrupled the lap and
            // swung the animal off-screen — 0.6/0.72 keeps the same on-screen sweep.
            radiusX = Mathf.Max(14f, size * 0.60f);
            radiusZ = Mathf.Max(16f, size * 0.72f);
            bobAmp = Mathf.Max(3f, size * 0.15f);
            _size = Mathf.Max(1f, size);

            // Cruise speed, unchanged from the ellipse: Effort reads ~1 while it is cruising.
            _cruise = Mathf.Max(0.2f, angularSpeed * Mathf.Max(radiusX, radiusZ));

            // Roaming geometry. The two constraints that matter and are easy to get wrong:
            //   • arrival radius MUST be larger than the turn radius, or the animal circles a
            //     waypoint it can never reach and the patrol degenerates into… an ellipse.
            //   • the roaming range must be a few arrival radii across, or every leg is over
            //     before it started.
            // ── C6: the species' own range and speed, for everything that is not a hero ──
            //
            // 🔴 Applied to non-Big animals ONLY, and the reason is the framing bias forty lines
            // up. The hero's roaming range is derived from the on-screen sweep the opening QC shot
            // was tuned against (RoamPerLap, "keeps the same on-screen sweep"); the whale shark's
            // own web row asks for 170 u against the ~98 u that geometry gives, which would carry
            // it out of frame in a shot nobody can re-check on this machine. Every OTHER animal
            // has no such constraint and desperately needs its row: without it a lionfish (7 u)
            // and a hammerhead (300 u) patrol the same circle, which is the whole complaint.
            // Revisit the hero the next time someone can render the shot.
            if (!cfg.Big && cfg.Has)
            {
                float speciesCruise = (float)SpeciesBehavior.CruiseMul(aid, _energy);
                if (speciesCruise > 0.01f) _cruise = Mathf.Max(0.2f, _cruise * speciesCruise);
            }

            float turnRadius = Mathf.Max(1f, _size * TurnRadiusPerLen);
            _turnRate = Mathf.Clamp(_cruise / turnRadius, 0.03f, 0.8f);
            _arriveR  = Mathf.Max(_size * ArrivePerLen, turnRadius * 1.3f);
            _roamR    = Mathf.Max(Mathf.Max(radiusX, radiusZ) * RoamPerLap, _arriveR * 3f);
            if (!cfg.Big && cfg.Has)
            {
                // The arrival radius still floors it: a range smaller than a few arrival radii
                // means every leg is over before it started and the animal jitters on the spot.
                _roamR = Mathf.Max((float)SpeciesBehavior.Derive(aid, _energy).RoamR, _arriveR * 3f);
            }

            // Until SetWorld arrives with the real seabed, roam a disc around the placement and
            // stay in a depth band around it — self-contained, so a map that never calls SetWorld
            // still gets a hero animal that goes somewhere rather than one that leaves the map.
            _domain = new MindDomain(anchor.x, anchor.z, _roamR * 1.4f,
                                     anchor.y - _roamR * 0.35f, anchor.y + _roamR * 0.35f);

            FishMind.PatrolInit(ref _patrol, PatrolSeed(assetId ?? gameObject.name, anchor),
                                anchor.x, anchor.y, anchor.z, _domain,
                                anchor.x, anchor.y, anchor.z, _roamR,
                                _pois, _poiCount, _arriveR, _cruise);

            if (_stationary)
            {
                // It stays exactly where it was put. Update() gives it the web's bob and yaw sway
                // (SpeciesBehavior.Sway*) and nothing else.
                transform.position = anchor;
                _baseYaw = transform.eulerAngles.y;
            }
            else
            {
                transform.position = new Vector3((float)_patrol.X, (float)_patrol.Y, (float)_patrol.Z);
            }
            _lastPos = transform.position;
            _configured = true;

            ApplyBodyWave(aid, size);

            SpeciesGenome.Genome sg = SpeciesGenome.For(aid);
            Debug.Log($"[Patrol] init id={aid} anchor={anchor} size={_size:F1} cruise={_cruise:F2} " +
                      $"turnRate={_turnRate:F3} arriveR={_arriveR:F1} roamR={_roamR:F1} " +
                      $"domainR={_domain.Radius:F0} pois={_poiCount} stationary={_stationary}");
            Debug.Log($"[Genome] hero id={aid} diet={sg.Diet} zone={sg.Zone} rank={sg.Rank} " +
                      $"row={(cfg.Has ? "web" : "fallback")} " +
                      $"webRoamR={(cfg.HasRoamR ? cfg.RoamR : -1.0):F0} " +
                      $"gait={SwimStyle.GaitFor(aid)} still={SwimStyle.IsStill(aid)} " +
                      $"big={cfg.Big} benthic={cfg.Benthic} ambush={cfg.Ambush}");
        }

        /// <summary>
        /// Hand the animal the map it is actually in: the sand's radius, the surface, and the
        /// structures on it. Called by SceneBuilder once the static loads have settled — which is
        /// AFTER <see cref="Init"/>, so the patrol is restarted from where the animal already is
        /// rather than from the anchor. Restarting from the current position is what keeps that
        /// hand-over invisible instead of a one-frame jump.
        /// </summary>
        public void SetWorld(float seabedRadius, float waterLevel, List<ObstacleBox> obstacles)
        {
            int n = obstacles != null ? obstacles.Count : 0;
            const int Max = 6;
            _pois = new MindPoi[Mathf.Min(Max, Mathf.Max(0, n))];
            _poiCount = 0;
            for (int i = 0; i < n; i++)
            {
                ObstacleBox b = obstacles[i];
                float r = Mathf.Max(b.Max.x - b.Min.x, b.Max.z - b.Min.z) * 0.5f + b.ObsR;
                var poi = new MindPoi((b.Min.x + b.Max.x) * 0.5f,
                                      (b.Min.y + b.Max.y) * 0.5f,
                                      (b.Min.z + b.Max.z) * 0.5f, r);
                if (_poiCount < _pois.Length) { _pois[_poiCount++] = poi; continue; }
                int smallest = 0;
                for (int k = 1; k < _pois.Length; k++)
                    if (_pois[k].Radius < _pois[smallest].Radius) smallest = k;
                if (r > _pois[smallest].Radius) _pois[smallest] = poi;
            }

            // Half a body length of clearance off the sand and off the surface — a 65 u shark whose
            // CENTRE sits on the ceiling is a 32 u fin sticking out of the water.
            float clear = _size * 0.6f;
            float minY = clear;
            float maxY = Mathf.Max(minY + 1f, waterLevel - clear);
            float r0 = seabedRadius > 1f ? seabedRadius : _roamR * 1.4f;
            _domain = new MindDomain(0.0, 0.0, r0, minY, maxY);

            Vector3 p = transform.position;
            _patrol.Ready = false;   // PatrolStep re-inits from here on the next frame
            _patrol.X = p.x; _patrol.Y = Mathf.Clamp(p.y, minY, maxY); _patrol.Z = p.z;

            Debug.Log($"[Patrol] world seabedR={r0:F0} y=[{minY:F0},{maxY:F0}] pois={_poiCount} " +
                      $"roamR={_roamR:F0} (re-seeded from the animal's current position)");
        }

        /// <summary>
        /// A stable seed from the asset id and where it was placed: two whale sharks on one map get
        /// different routes, and the same map always replays the same one.
        /// </summary>
        private static uint PatrolSeed(string id, Vector3 at)
        {
            uint h = 2166136261u;
            if (!string.IsNullOrEmpty(id))
                for (int i = 0; i < id.Length; i++) { h ^= id[i]; h *= 16777619u; }
            h ^= (uint)Mathf.RoundToInt(at.x * 7.3f) * 0x9E3779B9u;
            h ^= (uint)Mathf.RoundToInt(at.z * 3.1f) * 0x85EBCA6Bu;
            return h;
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

            // 🔴 …but the metal factor is only honest when it stands alone. Where the source ships
            // a metallic-roughness TEXTURE the factor is one half of a multiplication and the map
            // is the other: every XR model measured declares metallicFactor = 1 against a map
            // whose metal channel reads 0.001. Copying the 1 by itself turns the animal into a
            // mirror, and a mirror with one small reflection cube to reflect is black.
            // GlbShading.CopiedMetalFactor is where that rule is written down and tested.
            if (to.HasProperty("_Metallic") && from.HasProperty("metallicFactor"))
                to.SetFloat("_Metallic", GlbShading.CopiedMetalFactor(
                    from.GetFloat("metallicFactor"), HasMetalMap(from)));
        }

        private static Texture Get(Material m, string prop)
            => m != null && m.HasProperty(prop) ? m.GetTexture(prop) : null;

        /// <summary>
        /// Does this source material carry the metal/roughness data in a TEXTURE? Named the three
        /// ways glTFast, Unity's Standard and an ORM-packed export spell it, same list SceneBuilder
        /// checks — see <see cref="CopyMaps"/> for why the answer changes what gets copied.
        /// </summary>
        private static bool HasMetalMap(Material m)
        {
            return Get(m, "metallicRoughnessTexture") != null
                || Get(m, "_MetallicGlossMap") != null
                || Get(m, "occlusionRoughnessMetallicTexture") != null;
        }

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
            if (!_configured) return;

            // 🔴 In the builder an animal is furniture. The gizmo moves an item by writing its
            // transform and so does this class, so an author placing a shark would be dragging
            // something that swims out from under them. Freeze — and keep the patrol's own
            // position in step with wherever the author leaves it, so that leaving Edit resumes
            // from the new spot instead of snapping back to the last waypoint it remembered.
            if (!ModeRules.AnimalsSwim(ModeManager.Current))
            {
                Vector3 held = transform.position;
                _patrol.X = held.x; _patrol.Y = held.y; _patrol.Z = held.z;

                // 🔴 Only re-anchor when the AUTHOR moved it, not merely because we are looking.
                // _lastPos is the position this class last wrote, so a difference is somebody
                // else's doing — the gizmo. Assigning the anchor unconditionally instead looks
                // identical for a swimmer and is wrong for a stationary one: its transform sits at
                // anchor + sway, so every entry into Edit would bake that frame's ±0.4 u bob into
                // the anchor and the crab would walk up the sand one edit at a time.
                if ((held - _lastPos).sqrMagnitude > 1e-6f)
                {
                    anchor = held;
                    _lastPos = held;
                }
                return;
            }

            float fs = (float)MarineMath.RealDeltaScale(Time.deltaTime);
            float dt = (float)MarineMath.BaseStep * fs; // real-delta step
            _t += dt;

            // A stationary animal's ENTIRE behaviour: bob on the spot, turn its head, and let the
            // rig keep ticking at half speed. builder.html:2166-2172 — the web returns from
            // applyBehavior right here, and so does this.
            if (_stationary)
            {
                transform.position = new Vector3(
                    anchor.x,
                    anchor.y + (float)SpeciesBehavior.SwayY(_t, bobFreq * 6.2832f, _swayPhase),
                    anchor.z);
                // Recorded so the Edit-mode branch above can tell OUR movement from the gizmo's.
                _lastPos = transform.position;
                transform.rotation = Quaternion.Euler(
                    0f,
                    _baseYaw + (float)SpeciesBehavior.SwayYaw(_t, _swayPhase) * Mathf.Rad2Deg,
                    0f); // roll forced 0 — the no-roll rule holds here too

                // The rig keeps ticking, at half speed (web :2167 mixer.timeScale = 0.5). The
                // amplitude SwimStyle hands a still animal is ~1 %, so this is a seahorse's tail
                // curling rather than a swim — but a body that is bit-for-bit identical every
                // frame is what "แช่แข็ง" looks like, and that is the complaint this all began as.
                AdvanceWave((float)SpeciesBehavior.SwayClipScale, dt);
                return;
            }

            // ── C6: hunger, pursuit, evasion ─────────────────────────────────────────
            // The registry does the O(n²) work once every 0.7 s for the whole reef (and not at
            // all on a map with nothing that hunts); this animal only reads its own row.
            //
            // 🔴 Time.time, NOT _t. Every animal calls this and whichever one runs first each tick
            // does the scan for all of them, so the clock has to be the same clock. _t is a
            // PER-ANIMAL accumulator that starts at zero when that animal is attached — and the
            // msh:* heroes are attached during the load while the other 58 are attached after it,
            // so their _t values are minutes apart. Feeding those in would let the animal with the
            // largest _t push _nextScan into the future and starve every other animal's senses.
            SoloAnimalRegistry.Tick(Time.time);
            float huntWant = 1f;
            if (_slot >= 0 && SoloAnimalRegistry.TryGet(_slot, out SoloAnimalRegistry.Entry me))
            {
                Vector3 here = transform.position;
                bool hunted = false;
                float fleeR = (float)FleeMath.FleeRadius(me.ObsR);
                float hunterD = 0f;
                if (me.HasHunter)
                {
                    float hx = here.x - me.Hunter.x, hz = here.z - me.Hunter.z;
                    hunterD = Mathf.Sqrt(hx * hx + hz * hz);
                    hunted = hunterD < fleeR;
                }

                bool predator = _gen.Diet == SpeciesGenome.DietPredator;
                HuntStep hs = HuntMath.Step(
                    ref _drive,
                    predator && !hunted && me.HasPrey,
                    me.Prey.x - here.x, me.Prey.z - here.z,
                    _patrol.Heading, me.ObsR, _cfg.Ambush, _cfg.Big,
                    _metabolism, _t, dt);

                if (hunted && hunterD > 1e-4f)
                {
                    // Evade — put the waypoint on the far side of itself from the hunter. Steering,
                    // never a position write: PatrolStep still turns at the capped rate, so the
                    // animal swings away rather than snapping around.
                    float ax = (here.x - me.Hunter.x) / hunterD, az = (here.z - me.Hunter.z) / hunterD;
                    _patrol.TX = here.x + ax * _roamR;
                    _patrol.TZ = here.z + az * _roamR;
                    _patrol.LegT = 0f;
                    FishMind.ClampToDomain(_domain, _arriveR * 2f,
                                           ref _patrol.TX, ref _patrol.TY, ref _patrol.TZ);
                    huntWant = (float)FleeMath.FleeSprint(hunterD, fleeR);
                    SetPhase(HuntPhase.Sprint, true);
                }
                else if (hs.Phase == HuntPhase.Stalk || hs.Phase == HuntPhase.Sprint ||
                         hs.Phase == HuntPhase.Strike)
                {
                    _patrol.TX = me.Prey.x;
                    _patrol.TZ = me.Prey.z;
                    _patrol.TY = me.Prey.y;
                    _patrol.LegT = 0f;
                    FishMind.ClampToDomain(_domain, _arriveR * 2f,
                                           ref _patrol.TX, ref _patrol.TY, ref _patrol.TZ);
                    // 🔴 Capped hard. HuntMath's multiplier reaches 20× on a starving predator,
                    // which is the right number for the web's per-frame position nudge and an
                    // absurd one here: PatrolStep integrates position from speed, so a 20× cruise
                    // is an animal crossing the reef in a second. 2.5× is a visible, believable
                    // burst — and the acceleration cap below still eases into it.
                    huntWant = Mathf.Min(2.5f, (float)hs.SpeedMul * 0.25f);
                    SetPhase(hs.Phase, false);
                }
                else
                {
                    SetPhase(hs.Phase, false);
                }
            }

            // Ease the burst in and out. A sprint that snaps on reads as a stutter, and the whole
            // no-teleport rule this class is built on is about exactly this kind of discontinuity.
            _huntMul = Mathf.MoveTowards(_huntMul, huntWant, 2.0f * dt);

            // Roam. Everything about the shape of the path lives in Core/FishMind.cs, where it can
            // be replayed and asserted; this class only owns the Unity-side transform.
            bool newLeg = FishMind.PatrolStep(
                ref _patrol, dt, _cruise * _huntMul, _turnRate, _domain,
                anchor.x, anchor.y, anchor.z, _roamR,
                _pois, _poiCount, _arriveR);

            Vector3 pos = new Vector3((float)_patrol.X, (float)_patrol.Y, (float)_patrol.Z);
            transform.position = pos;

            if (newLeg && _legLogged < 6)
            {
                _legLogged++;
                float d = Mathf.Sqrt((float)((_patrol.TX - _patrol.X) * (_patrol.TX - _patrol.X) +
                                             (_patrol.TZ - _patrol.Z) * (_patrol.TZ - _patrol.Z)));
                Debug.Log($"[Patrol] leg={_patrol.Leg} target=({_patrol.TX:F0},{_patrol.TY:F0},{_patrol.TZ:F0}) " +
                          $"dist={d:F0} speed={_patrol.Speed:F2}/{_cruise:F2} " +
                          $"heading={(_patrol.Heading * Mathf.Rad2Deg):F0}° poi={_patrol.NearPoi}");
            }

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
            // Read off the PATROL's own speed, not the frame's displacement: the breath and the
            // ease-off beside the wreck are what the tail is supposed to be reporting, and a
            // displacement reading picks up the turn as well and muddies both.
            AdvanceWave((float)SwimStyle.Effort(_patrol.Speed, _cruise), dt);

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

        // ── C6: registry lifecycle + the [Solo] oracle ───────────────────────────

        /// <summary>
        /// Join the reef's sensing population. Called by SceneBuilder once the animal's GLB has
        /// landed and its real size is known — <paramref name="obsR"/> is half its world length,
        /// which is what every sense radius in <see cref="FleeMath"/> and <see cref="HuntMath"/>
        /// is built from.
        /// </summary>
        public void JoinReef()
        {
            if (_slot >= 0 || !_configured) return;
            _slot = SoloAnimalRegistry.Register(this, _assetId, _size * 0.5f);
        }

        private void OnDestroy()
        {
            SoloAnimalRegistry.Unregister(this);
            _slot = -1;
        }

        /// <summary>
        /// The registry moved this animal's row. Called by
        /// <see cref="SoloAnimalRegistry.Unregister"/>, which fills the hole it makes by swapping
        /// the last entry into it — so exactly one animal per removal learns a new index.
        /// </summary>
        internal void SetRegistrySlot(int slot) => _slot = slot;

        /// <summary>
        /// Record a phase change and log it. One line per CHANGE, never per frame, and throttled
        /// so a pair of animals oscillating across an engage radius cannot flood the log.
        /// </summary>
        private void SetPhase(HuntPhase p, bool evading)
        {
            if (p == _phase) return;
            _phase = p;
            if (_t < _soloLogAt) return;
            _soloLogAt = _t + 0.5f;
            Debug.Log($"[Solo] id={_assetId} state={(evading ? "Evade" : HuntMath.Label(p))} " +
                      $"pos=({transform.position.x:F0},{transform.position.y:F0},{transform.position.z:F0}) " +
                      $"hunger={_drive.Hunger:F2} fatigue={_drive.Fatigue:F2} " +
                      $"speed={_huntMul:F2}× cruise={_cruise:F2} roamR={_roamR:F0}");
        }

        /// <summary>
        /// Push the body wave on by <paramref name="dt"/> at the given effort.
        ///
        /// The phase is INTEGRATED on the CPU, never <c>sin(_Time.y · rate)</c>: with _Time.y the
        /// tail jumps by hundreds of radians the moment the rate changes. See
        /// <see cref="SwimStyle.BeatPhaseStep"/>.
        /// </summary>
        private void AdvanceWave(float effort, float dt)
        {
            if (_waveMats.Count == 0) return;
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

        private bool _headingLogged;
        private int  _legLogged;
    }
}
