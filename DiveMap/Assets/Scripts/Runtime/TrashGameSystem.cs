using System.Collections.Generic;
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
        public void Begin(Vector3 center, float waterLevel, float scaleX, float scaleZ)
        {
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
                    if (pos.y <= p.FloorY) { pos.y = p.FloorY; p.Landed = true; p.LandedAt = now; }
                    t.position = pos;
                }
                t.Rotate(p.IsCoin ? new Vector3(0f, 3.4f, 0f) : new Vector3(0.7f, 0.4f, 0.5f));

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

                if (Vector3.Distance(eye, t.position) < TrashGame.CollectRadius) Collect(i);
            }
        }

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
            GameObject go = coin ? BuildCoin() : BuildTrash(kind);
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
            });
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
            _coinMat = mat;
            return mat;
        }
    }
}
