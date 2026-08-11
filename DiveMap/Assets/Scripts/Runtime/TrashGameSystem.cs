using System;
using System.Collections.Generic;
using GLTFast;
using DiveMap.Core;
using DiveMap.Runtime.Ui;
using UnityEngine;

namespace DiveMap.Runtime
{
    /// <summary>
    /// P3 — the clean-up game inside the tour: litter drifts down, you swim into it, it turns into
    /// coins. The rules (cadence, cap, scoring, combo, despawn) all live in <see cref="TrashGame"/>
    /// and are unit-tested; this class is the scene half.
    ///
    /// It only runs in a first-person mode, and leaving clears the field — the web is emphatic
    /// about that (<c>gameTick</c>: "ขยะ/เหรียญตกเฉพาะโหมด tour, edit=ไม่มี"), because litter
    /// raining onto a map you are editing would be absurd.
    ///
    /// Meshes are Unity primitives harvested once (cylinder/sphere/cube) rather than hand-built:
    /// the web's lathe-and-torus models are lovely, but on a phone at 30 pieces what matters is
    /// that a can reads as a can at 10 metres, and the colours do that work.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TrashGameSystem : MonoBehaviour
    {
        public const string CoinsPrefKey = "coins";

        private sealed class Piece
        {
            public GameObject Go;
            public TrashGame.Kind Kind;
            public bool IsCoin;
            public bool Landed;
            public float LandedAt;
            public float SpawnY;
            public float FloorY;
            public float Phase;
            /// <summary>Template key ("coin" / kind key) — what this piece SHOULD look like.</summary>
            public string Key;
            /// <summary>True while it is still the primitive stand-in (WO-N item 5).</summary>
            public bool Primitive;
        }

        private static TrashGameSystem _instance;

        private readonly List<Piece> _pieces = new List<Piece>();
        private Transform _root;
        private Camera _cam;
        private float _lastSpawn, _lastCoinCycle;
        private int _combo;
        private string _comboKind;
        private float _waterLevel = 240f;
        private float _scaleX = 1f, _scaleZ = 1f;
        private Vector3 _center;
        private bool _running;
        private uint _rng = 0x9E3779B9;

        // The scenery's true shape — the same hulls the drone collides with (see TrashPhysics:
        // litter must land on the wreck's deck and fall through the arch's opening, not rest on
        // an invisible lid). Handed in by TourController, which already holds them.
        private IReadOnlyList<SolidBoxes.Group> _solids;

        // ── โมเดลจริงจาก user (7 ส.ค.) — game_*_k1.glb บน CDN (mesh ~3k · texture 1K PNG
        // สูตรไม่บีบ) · โหลดครั้งเดียวเป็นแม่แบบแล้ว clone ต่อชิ้น · โหลดไม่ทัน/ล้มเหลว =
        // ใช้ primitive เดิม — เกมไม่มีวันค้างรอโมเดล (เรือ/เน็ตห่วยคือเคสปกติของเกมนี้)
        private const string ModelCdn = "https://siamdive-cdn.b-cdn.net/models/xr/";
        private static readonly Dictionary<string, string> ModelFiles =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "can", "game_trash_can_k1.glb" },
                { "bottle", "game_trash_bottle_k1.glb" },
                { "plastic", "game_trash_bag_k1.glb" },
                { "net", "game_trash_net_k1.glb" },
                { "tire", "game_trash_tire_k1.glb" },   // ไฟล์จาก user 8 ส.ค. (มีจริงบน CDN — คอมเมนต์เก่าที่บอกว่า "ไม่มีโมเดล" ผิด)
                { "coin", "game_coin_gold2_k1.glb" },   // รุ่น mirror หน้า->หลัง (user: หลังเดิมพัง)
            };

        /// <summary>
        /// ลำดับโหลด — 🪙 "coin" มาก่อน (WO-N item 5).
        ///
        /// 🔴 เดิมวนตามลำดับใน ModelFiles ซึ่ง coin อยู่ท้ายสุด = ต้องรอ trash 5 ไฟล์ ~22.6 MB
        /// ดาวน์โหลดจบทีละไฟล์ก่อนถึงจะเริ่มขอเหรียญ · เหรียญเกิดใหม่ทุก 60 วิ และตายทุก 60 วิ
        /// เพราะฉะนั้นบนเน็ตเรือ/โรงแรม ผู้เล่นเห็น "จานสีน้ำตาลแบน" ทั้งเซสชัน ซึ่งคือรูปที่
        /// user ถ่ายมา · เหรียญคือชิ้นที่ผู้เล่นจ้องมากที่สุด (เป็นตัวให้รางวัล) จึงต้องมาก่อน
        /// </summary>
        private static readonly string[] LoadOrder =
            { "coin", "can", "bottle", "plastic", "net", "tire" };

        /// <summary>ขนาดเป้าหมาย (ด้านยาวสุด, world unit) ให้ใกล้เคียง primitive เดิมต่อชนิด.</summary>
        private static readonly Dictionary<string, float> ModelSize =
            new Dictionary<string, float>(StringComparer.Ordinal)
            {
                { "can", 2.6f }, { "bottle", 2.9f }, { "plastic", 5.5f },
                { "net", 27.0f },   // user 8 ส.ค.: อีก 3 เท่า — อวนจริงผืนใหญ่มาก
                { "tire", 5.5f }, { "coin", 4.2f },
            };

        private readonly Dictionary<string, GameObject> _templates =
            new Dictionary<string, GameObject>(StringComparer.Ordinal);

        /// <summary>Coins in the purse. Local for now; the wallet sync is its own step.</summary>
        public static int Coins
        {
            get => PlayerPrefs.GetInt(CoinsPrefKey, TrashGame.StartingCoins);
            set { PlayerPrefs.SetInt(CoinsPrefKey, Mathf.Max(0, value)); PlayerPrefs.Save(); }
        }

        /// <summary>
        /// Coins picked up since this session started — what the arena exit gate warns about.
        /// Session-scoped on purpose: the question at the gate is "you earned N just now and
        /// have nowhere to put them", not "your lifetime total".
        /// </summary>
        public static int EarnedThisSession { get; private set; }

        /// <summary>The coins reached an account (or the player chose to drop them).</summary>
        public static void ClearEarned() => EarnedThisSession = 0;

        /// <summary>
        /// ตำแหน่งชิ้นที่ยังเก็บได้ตอนนี้ — เรดาร์ใช้ (คำสั่ง user 7 ส.ค.: เรดาร์แสดงแค่
        /// ขยะกับเหรียญ). ชิ้นที่กะพริบหายชั่วขณะไม่นับ เพราะบนจอก็มองไม่เห็นเช่นกัน.
        /// </summary>
        public static void CollectPositions(List<Vector3> trash, List<Vector3> coins)
        {
            trash?.Clear();
            coins?.Clear();
            if (_instance == null) return;
            foreach (Piece p in _instance._pieces)
            {
                if (p.Go == null || !p.Go.activeSelf) continue;
                (p.IsCoin ? coins : trash)?.Add(p.Go.transform.position);
            }
        }

        public static TrashGameSystem Ensure(Transform parent)
        {
            if (_instance != null) return _instance;
            var go = new GameObject("TrashGame");
            if (parent != null) go.transform.SetParent(parent, false);
            _instance = go.AddComponent<TrashGameSystem>();
            _instance._root = go.transform;
            return _instance;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        /// <summary>Start dropping litter for this map.</summary>
        public void Begin(Vector3 center, float waterLevel, float scaleX, float scaleZ,
                          IReadOnlyList<SolidBoxes.Group> solids = null)
        {
            _solids = solids;
            _cam = Camera.main;
            _center = center;
            _waterLevel = waterLevel;
            _scaleX = scaleX;
            _scaleZ = scaleZ;
            _lastSpawn = Time.time;
            _lastCoinCycle = Time.time - TrashGame.CoinCycle;   // coins right away
            _combo = 0;
            _comboKind = null;
            _running = true;
            LoadTemplates();

            // Seed a few pieces so a fresh dive has something in it. The web's field persists
            // across a session, so its 5 s cadence is enough there; ours starts empty every time
            // and an empty first minute reads as "the game is not working".
            for (int i = 0; i < 3; i++) Spawn(false);

            CoinCounter.Ensure();
            CoinCounter.Show(Coins);

            // Pull the server's balance (keyed by device, no account needed) and reconcile it with
            // anything this device earned while offline.
            WalletClient.Refresh(coins =>
            {
                Coins = coins;
                CoinCounter.Show(coins);
            });

            Debug.Log($"[Game] begin coins={Coins} water={waterLevel:F0}");
        }

        /// <summary>Stop and clear the field (leaving the tour).</summary>
        public void End()
        {
            _running = false;
            for (int i = 0; i < _pieces.Count; i++)
                if (_pieces[i].Go != null) Destroy(_pieces[i].Go);
            _pieces.Clear();
            CoinCounter.Hide();
            WalletClient.Flush(coins =>
            {
                Coins = coins;
                CoinCounter.Show(coins);
            });
            Debug.Log($"[Game] end coins={Coins}");
        }

        private void Update()
        {
            if (!_running) return;
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;

            float now = Time.time;

            int live = 0;
            for (int i = 0; i < _pieces.Count; i++) if (!_pieces[i].IsCoin) live++;
            if (TrashGame.ShouldSpawn(live, now, _lastSpawn))
            {
                Spawn(false);
                _lastSpawn = now;
            }
            if (TrashGame.ShouldCycleCoins(now, _lastCoinCycle))
            {
                for (int i = _pieces.Count - 1; i >= 0; i--)
                {
                    if (!_pieces[i].IsCoin) continue;
                    if (_pieces[i].Go != null) Destroy(_pieces[i].Go);
                    _pieces.RemoveAt(i);
                }
                for (int i = 0; i < TrashGame.CoinsPerCycle; i++) Spawn(true);
                _lastCoinCycle = now;
            }

            Vector3 eye = _cam.transform.position;
            for (int i = _pieces.Count - 1; i >= 0; i--)
            {
                Piece p = _pieces[i];
                if (p.Go == null) { _pieces.RemoveAt(i); continue; }

                Transform t = p.Go.transform;
                if (!p.Landed)
                {
                    Vector3 pos = t.position;
                    pos.y -= TrashGame.FallSpeed * Time.deltaTime;
                    pos.x += Mathf.Sin(now * 0.7f + p.Phase) * 0.04f;   // the web's drift
                    // Re-floor at the CURRENT x/z: the piece drifts while it falls, and the
                    // floor under a drifted piece is not the floor under its spawn (the whole
                    // half-buried-in-the-sand report). Hulls first, sand as the fallback.
                    // +ExtraLift: โมเดลจริง pivot อยู่กลางตัว ชิ้นใหญ่เลยดูจมทราย —
                    // user 8 ส.ค.: "ขยับขึ้นมาอีกนิด ไม่ต้องเยอะ"
                    float floor = TrashPhysics.FloorUnder(pos.x, pos.z, pos.y + 1f,
                                      FloorAt(pos), _solids) + TrashGame.LandOffset + ExtraLift;
                    p.FloorY = floor;   // HeightFactor scores against where it actually rests
                    if (pos.y <= floor) { pos.y = floor; p.Landed = true; p.LandedAt = now; }
                    t.position = pos;
                }
                // user 8 ส.ค. (รอบ 2): ขยะทุกชนิดตกตรงๆ ไม่หมุน — เหลือเหรียญตัวเดียวที่ควง
                if (p.IsCoin) t.Rotate(new Vector3(0f, 3.4f, 0f));

                if (!p.IsCoin && p.Landed)
                {
                    float age = now - p.LandedAt;
                    if (TrashGame.Expired(age))
                    {
                        Destroy(p.Go);
                        _pieces.RemoveAt(i);
                        continue;
                    }
                    bool vis = TrashGame.VisibleWhileFading(age, now);
                    if (p.Go.activeSelf != vis) p.Go.SetActive(vis);
                }

                // ป้ายรีไซเคิลเขียว = "อยู่ในระยะเก็บ" — โผล่เฉพาะตอนแตะเก็บได้จริง
                // (เดิมโผล่ตลอด user เลยไม่รู้ระยะ)
                Transform badge = t.Find("RecycleBadge");
                if (badge != null)
                {
                    bool inRange = Vector3.Distance(eye, t.position) <= TapCollectRange;
                    if (badge.gameObject.activeSelf != inRange) badge.gameObject.SetActive(inRange);
                }

                if (Vector3.Distance(eye, t.position) < TrashGame.CollectRadius) Collect(i);
            }

            TapCollect();
        }

        /// <summary>
        /// Tap to pick up: the piece nearest the tap ray, no farther than the torch can throw
        /// light (<see cref="DiveLightMath.LampRange"/> — you can only grab what you can see).
        /// Driving into a piece still collects it; this is the second hand, not a replacement.
        /// </summary>
        private void TapCollect()
        {
            if (!Input.GetMouseButtonDown(0)) return;
            if (UnityEngine.EventSystems.EventSystem.current != null &&
                UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;

            Ray ray = _cam.ScreenPointToRay(Input.mousePosition);
            var positions = new List<Vec3>(_pieces.Count);
            var map = new List<int>(_pieces.Count);
            for (int i = 0; i < _pieces.Count; i++)
            {
                Piece p = _pieces[i];
                if (p.Go == null || !p.Go.activeSelf) continue;   // blinking-out litter is not a target
                Vector3 at = p.Go.transform.position;
                positions.Add(new Vec3(at.x, at.y, at.z));
                map.Add(i);
            }
            int hit = TrashPhysics.PickForTap(
                new Vec3(ray.origin.x, ray.origin.y, ray.origin.z),
                new Vec3(ray.direction.x, ray.direction.y, ray.direction.z),
                positions, TapPickRadius, TapCollectRange);
            if (hit >= 0) Collect(map[hit]);
        }

        /// <summary>
        /// How far off the tap ray a piece may sit and still count as tapped. Litter is small and
        /// drifts; a finger is fat. 3 world units ≈ the piece plus a thumb of forgiveness.
        /// </summary>
        private const float TapPickRadius = 3f;

        /// <summary>
        /// ระยะเก็บด้วยการแตะ — user สั่ง "ไกลกว่าไฟฉายอีกนิด" (LampRange 90 -> 115) และ
        /// เป็นเส้นเดียวกับที่ป้ายรีไซเคิลโผล่: เห็นป้ายเขียว = แตะเก็บได้แน่นอน.
        /// </summary>
        public const float TapCollectRange = 115f;

        /// <summary>ยกจุดวางเพิ่มจาก LandOffset ของเว็บ — โมเดลจริง pivot กลางตัว (user 8 ส.ค.).</summary>
        private const float ExtraLift = 1.2f;

        // ── spawn / collect ──────────────────────────────────────────────────────

        private void Spawn(bool coin)
        {
            // Uniform over the footprint, like the web: angle × √u × 0.85 of the boundary.
            float a = Rand() * Mathf.PI * 2f;
            float rr = Mathf.Sqrt(Rand()) * 0.85f;
            float bd = SeabedGeom.BoundaryDist(a) * rr;
            var pos = new Vector3(_center.x + Mathf.Cos(a) * bd * _scaleX,
                                  _waterLevel - TrashGame.SpawnBelowSurface,
                                  _center.z + Mathf.Sin(a) * bd * _scaleZ);

            TrashGame.Kind kind = TrashGame.Pick(Rand());
            string key = coin ? "coin" : kind.Key;
            GameObject go = FromTemplate(key);
            bool primitive = go == null;
            if (primitive) go = coin ? BuildCoin() : BuildTrash(kind);
            go.transform.SetParent(_root, false);
            go.transform.position = pos;
            // ♻️ over litter only — a gold coin already reads as "pick me up" on its own, and the
            // web tags only trash (spawnTrash adds the sprite; _spawnCoin does not).
            if (!coin) RecycleBadge.Attach(go.transform);

            _pieces.Add(new Piece
            {
                Go = go,
                Kind = kind,
                IsCoin = coin,
                SpawnY = pos.y,
                FloorY = FloorAt(pos) + TrashGame.LandOffset,
                Phase = Rand() * 6.283f,
                Key = key,
                Primitive = primitive,
            });
        }

        /// <summary>
        /// Are all six models already in the on-disk cache? (WO-P)
        ///
        /// This is what decides whether the preload may run DURING the map build: a local read
        /// costs no bandwidth and cannot slow the map down, while a 26 MB download in parallel
        /// with the map's own GLBs certainly could. <c>AssetCacheStore.Resolve</c> returns the
        /// url unchanged when there is no local copy, which is the same test
        /// <c>SceneBuilder.CachedUri</c> makes before deciding to download.
        ///
        /// All six, not any: a partial cache still means a download, and the point of the early
        /// path is that it is free.
        /// </summary>
        public static bool AllTemplatesCached()
        {
            foreach (KeyValuePair<string, string> kv in ModelFiles)
            {
                string url = ModelCdn + kv.Value;
                if (AssetCacheStore.Resolve(url) == url) return false;
            }
            return true;
        }

        /// <summary>
        /// Start fetching the templates before the tour asks for them (WO-P).
        ///
        /// 🔴 The gap this closes: <see cref="Begin"/> is the only thing that used to call
        /// <see cref="LoadTemplates"/>, and Begin also seeds three pieces of litter and a coin
        /// cycle in the SAME frame. So the first pieces of every game were primitives by
        /// construction — a white sphere and a flat brown disc, at the exact moment the player is
        /// looking hardest, which is what the user photographed.
        ///
        /// Only called when <c>GamePreload</c> says the tour is certain, so this never spends
        /// memory on a map nobody is going to play. Safe to call more than once and safe to call
        /// alongside Begin — see the in-flight guard on LoadTemplates.
        /// </summary>
        public static void PreloadTemplates(Transform parent)
        {
            TrashGameSystem g = Ensure(parent);
            if (g == null) return;
            g.LoadTemplates();
        }

        /// <summary>
        /// One template load at a time, whoever asks (WO-P).
        ///
        /// 🔴 Without this, a preload in flight plus <see cref="Begin"/> a second later would each
        /// see an empty <c>_templates</c> and start their own download of all six models: 52 MB
        /// over the wire, two GltfImports per model, and the loser's copy leaked because only one
        /// can win the dictionary. The existing per-key check cannot catch it — a model that is
        /// still downloading is not in the dictionary yet.
        /// </summary>
        private bool _loadingTemplates;

        /// <summary>โหลดแม่แบบโมเดลจริงเบื้องหลัง — สำเร็จเมื่อไหร่ ชิ้นที่เกิดใหม่ใช้ทันที.</summary>
        private async void LoadTemplates()
        {
            if (_loadingTemplates) return;
            _loadingTemplates = true;
            try
            {
            foreach (string key in LoadOrder)
            {
                if (!ModelFiles.TryGetValue(key, out string file)) continue;
                var kv = new KeyValuePair<string, string>(key, file);
                if (_templates.ContainsKey(kv.Key)) continue;
                var holder = new GameObject($"tpl_{kv.Key}");
                holder.transform.SetParent(_root, false);
                holder.SetActive(false);
                GltfImport import = await SceneBuilder.LoadForQc(
                    ModelCdn + kv.Value, $"game:{kv.Key}", holder.transform);
                if (import == null || this == null || holder == null)
                {
                    if (holder != null) Destroy(holder);
                    continue;
                }
                // normalize: ด้านยาวสุด = ขนาดเป้าหมายของชนิดนั้น
                var rends = holder.GetComponentsInChildren<Renderer>(true);
                if (rends.Length > 0)
                {
                    Bounds b = rends[0].bounds;
                    for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                    float max = Mathf.Max(b.size.x, b.size.y, b.size.z);
                    float want = ModelSize.TryGetValue(kv.Key, out float w) ? w : 2f;
                    if (max > 1e-4f) holder.transform.localScale = Vector3.one * (want / max);
                }
                _templates[kv.Key] = holder;
                Debug.Log($"[Game] template {kv.Key} ready");
                UpgradePieces(kv.Key);
            }
            }
            finally
            {
                // Released whatever happened, including the `this == null` teardown path above —
                // a stuck flag would mean the templates never load again for the rest of the run.
                _loadingTemplates = false;
            }
        }

        /// <summary>
        /// เปลี่ยนชิ้นที่ยังเป็น primitive ให้เป็นโมเดลจริง ทันทีที่แม่แบบของชนิดนั้นมาถึง
        /// (WO-N item 5).
        ///
        /// 🔴 นี่คือรูรั่วตัวจริงของรูปที่ user ถ่ายมา ไม่ใช่แค่ลำดับโหลด: `Spawn` เลือกระหว่าง
        /// โมเดลกับ primitive "ครั้งเดียวตอนเกิด" แล้วไม่มีใครกลับมาดูอีกเลย — `Update` ไม่เคย
        /// แตะ `_templates` · ทัวร์เริ่มปุ๊บโปรยขยะ 3 ชิ้น + เหรียญ 3 เหรียญในเฟรมแรก ตอนที่
        /// `_templates` ยังว่างแน่ ๆ · เหรียญมีอายุ 60 วิ ขยะ 30 วิหลังตกถึงพื้น เพราะฉะนั้น
        /// ชิ้นแรกที่ผู้เล่นเห็น (และถ่ายรูป) เป็น primitive เสมอ 100% ต่อให้เน็ตเร็วแค่ไหน
        ///
        /// ย้ายตำแหน่ง/การหมุน/badge ♻️ ไปให้ตัวใหม่ครบ แล้วทำลายตัวเก่า — ชิ้นที่ตกถึงพื้นแล้ว
        /// จะไม่กระโดดกลับขึ้นไป เพราะเราคัดลอก transform ปัจจุบัน ไม่ใช่ตำแหน่งตอนเกิด
        /// </summary>
        private void UpgradePieces(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            int swapped = 0;
            for (int i = 0; i < _pieces.Count; i++)
            {
                Piece p = _pieces[i];
                if (p == null || !p.Primitive || p.Key != key || p.Go == null) continue;

                GameObject repl = FromTemplate(key);
                if (repl == null) return;   // แม่แบบหายไประหว่างทาง — ไว้รอบหน้า

                Transform old = p.Go.transform;
                repl.transform.SetParent(_root, false);
                repl.transform.SetPositionAndRotation(old.position, old.rotation);
                if (!p.IsCoin) RecycleBadge.Attach(repl.transform);

                Destroy(p.Go);
                p.Go = repl;
                p.Primitive = false;
                swapped++;
            }
            if (swapped > 0) Debug.Log($"[Game] upgraded {swapped} {key} piece(s) to the real model");
        }

        /// <summary>ชิ้นจากแม่แบบโมเดลจริง หรือ null เมื่อยังโหลดไม่เสร็จ (ผู้เรียกใช้ primitive).</summary>
        private GameObject FromTemplate(string key)
        {
            if (!_templates.TryGetValue(key, out GameObject tpl) || tpl == null) return null;
            GameObject go = Instantiate(tpl);
            go.name = $"piece_{key}";
            go.SetActive(true);
            return go;
        }

        private void Collect(int index)
        {
            Piece p = _pieces[index];
            _pieces.RemoveAt(index);
            if (p.Go == null) return;

            Vector3 at = p.Go.transform.position;
            Destroy(p.Go);

            string key = p.IsCoin ? "coin" : p.Kind.Key;
            _combo = TrashGame.NextCombo(_comboKind, key, _combo);
            _comboKind = key;

            float h = TrashGame.HeightFactor(at.y, p.FloorY, p.SpawnY);
            int gain = TrashGame.Score(p.Kind, h, _combo, p.IsCoin);
            Coins = Wallet.Earn(Coins, gain);
            EarnedThisSession += gain;   // what the arena exit gate warns is about to be lost
            WalletClient.Earn(gain);     // queued, debounced, re-queued if the request fails

            CoinCounter.Show(Coins);
            CoinCounter.Fly(gain);
            AudioBank.PlaySfx("coin");
            Debug.Log($"[Game] +{gain} ({key} h={h:F2} combo={_combo}) coins={Coins}");
        }

        /// <summary>Seabed height under a point — the same ray the drone uses.</summary>
        private float FloorAt(Vector3 at)
        {
            var from = new Vector3(at.x, _waterLevel + 50f, at.z);
            if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, _waterLevel + 400f))
                return hit.point.y;
            return 0f;
        }

        /// <summary>xorshift — deterministic per session and free of UnityEngine.Random's globals.</summary>
        private float Rand()
        {
            _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5;
            return (_rng & 0xFFFFFF) / (float)0x1000000;
        }

        // ── meshes ───────────────────────────────────────────────────────────────

        private static Mesh _cylinder, _sphere, _cube;

        private static Mesh Primitive(PrimitiveType type, ref Mesh cache)
        {
            if (cache != null) return cache;
            GameObject temp = GameObject.CreatePrimitive(type);
            cache = temp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(temp.GetComponent<Collider>());
            Destroy(temp);
            return cache;
        }

        private static GameObject BuildTrash(TrashGame.Kind kind)
        {
            Color colour;
            Vector3 scale;
            Mesh mesh;

            switch (kind.Key)
            {
                case "can":
                    mesh = Primitive(PrimitiveType.Cylinder, ref _cylinder);
                    colour = new Color(0.761f, 0.220f, 0.165f);   // 0xc2382a
                    scale = new Vector3(1.6f, 1.2f, 1.6f);
                    break;
                case "bottle":
                    mesh = Primitive(PrimitiveType.Cylinder, ref _cylinder);
                    colour = new Color(0.494f, 0.784f, 0.847f);   // 0x7ec8d8
                    scale = new Vector3(1.3f, 1.7f, 1.3f);
                    break;
                case "plastic":
                    mesh = Primitive(PrimitiveType.Sphere, ref _sphere);
                    colour = new Color(0.949f, 0.965f, 0.973f);   // 0xf2f6f8
                    scale = new Vector3(2.6f, 3.0f, 2.6f);
                    break;
                case "tire":
                    mesh = Primitive(PrimitiveType.Cylinder, ref _cylinder);
                    colour = new Color(0.094f, 0.098f, 0.114f);   // 0x18191d
                    scale = new Vector3(3.4f, 0.8f, 3.4f);
                    break;
                default: // net
                    mesh = Primitive(PrimitiveType.Cube, ref _cube);
                    colour = new Color(0.298f, 0.647f, 0.478f);
                    scale = new Vector3(3.2f, 0.5f, 3.2f);
                    break;
            }

            var go = new GameObject("Trash_" + kind.Key);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = TrashMaterial(colour);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.transform.localScale = scale;
            return go;
        }

        private static GameObject BuildCoin()
        {
            var go = new GameObject("Coin");
            go.AddComponent<MeshFilter>().sharedMesh = Primitive(PrimitiveType.Cylinder, ref _cylinder);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = CoinMaterial();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.transform.localScale = new Vector3(3.2f, 0.16f, 3.2f);
            return go;
        }

        private static readonly Dictionary<int, Material> _mats = new Dictionary<int, Material>();

        private static Material TrashMaterial(Color c)
        {
            int key = c.GetHashCode();
            if (_mats.TryGetValue(key, out Material cached) && cached != null) return cached;
            Material src = Resources.Load<Material>("DM_Standard");
            var mat = src != null ? new Material(src) : new Material(Shader.Find("Standard"));
            mat.color = c;
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.3f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.1f);
            // WO-N item 5 — เว็บให้ทุกวัสดุขยะ emissive เท่ากับสีตัวเอง แรง 0.1
            // (builder.html:4108) เพราะโหมดทัวร์มีหมอกน้ำ + ambient หรี่ ของที่ไม่เรืองเองจะจม
            // หายไปในน้ำ · เรามีหมอกเดียวกัน (DroneLights:176-180, UnderwaterShading:133-135)
            // แต่ลืมส่วนนี้ไป
            Emit(mat, c * 0.1f);
            _mats[key] = mat;
            return mat;
        }

        private static Material _coinMat;
        private static Material CoinMaterial()
        {
            if (_coinMat != null) return _coinMat;
            Material src = Resources.Load<Material>("DM_Standard");
            var mat = src != null ? new Material(src) : new Material(Shader.Find("Standard"));
            mat.color = new Color(1f, 0.776f, 0.180f);            // 0xffc62e
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.72f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.92f);
            // 🔴 นี่คือ "จานสีน้ำตาล" ในรูปที่ user ถ่าย · metalness 0.92 แปลว่าสีเกือบทั้งหมด
            // มาจากการสะท้อนสภาพแวดล้อม — ใต้น้ำไม่มี reflection probe ก็ไม่มีอะไรให้สะท้อน
            // ทองจึงกลายเป็นน้ำตาลเข้ม · เว็บเจอปัญหาเดียวกันแล้วแก้ด้วย emissive พร้อม
            // คอมเมนต์ว่า "เรืองทอง ไม่มืด" (builder.html:4138 emissive 0x6a4800 × 0.55)
            // เราลอก metalness มาแต่ไม่ได้ลอก emissive มาด้วย
            Emit(mat, new Color(0.416f, 0.282f, 0f) * 0.55f);      // 0x6a4800 × 0.55
            _coinMat = mat;
            return mat;
        }

        /// <summary>เปิด emission ให้วัสดุ Standard (ต้องเปิด keyword ด้วย ไม่งั้นสีถูกเมิน).</summary>
        private static void Emit(Material mat, Color c)
        {
            if (mat == null || !mat.HasProperty("_EmissionColor")) return;
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            mat.SetColor("_EmissionColor", c);
        }
    }
}
