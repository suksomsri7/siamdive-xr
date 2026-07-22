using System;
using System.Collections;
using System.Collections.Generic;
using GLTFast;
using UnityEngine;
using DiveMap.Core;

namespace DiveMap.Runtime
{
    /// <summary>
    /// Turns a <see cref="SceneData"/> into a live 3D map: a "Map" root with one
    /// child GameObject per item (WebCoord-converted transform + glTFast GLB, or a
    /// coloured placeholder on failure), plus a sculpted polar-grid seabed and a
    /// transparent water disc.
    ///
    /// Per-item failures MUST NOT abort the scene (WO-XR-01): production GLBs use
    /// WebP textures which glTFast may reject — we catch and spawn a placeholder,
    /// counting loaded vs failed. Async loads are tracked through <see cref="SceneLoadState"/>
    /// so nothing claims "done" while a GLB is still in flight (the 108→31 save-guard lesson).
    /// </summary>
    public sealed class SceneBuilder : MonoBehaviour
    {
        public struct BuildResult
        {
            public GameObject Root;
            public int Loaded;
            public int Failed;
            public Vector3 Center;
            public float Radius;
            public string MapName;

            // Framing box (decor/structure only, swimmers excluded) — fed to OrbitCamera.FrameBox
            // so the opening shot centres on the real content (e.g. the wreck), not the whole
            // seabed + water column.
            public Vector3 FrameCenter;
            public float FrameSizeX;
            public float FrameSizeY;
            public float FrameSizeZ;
            public float FrameMinY;
        }

        // Kinds that swim (drift through the water column) — excluded from the opening-shot
        // framing box and NOT grounded to the seabed (they float at their stored Y).
        private static bool IsSwimmer(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return false;
            switch (kind.ToUpperInvariant())
            {
                case "MARINE_LIFE":
                case "SCHOOL":
                case "FISH":
                case "TURTLE":
                    return true;
                default:
                    return false;
            }
        }

        // Preliminary seabed sizing (WO-XR-01). areaScale multiplies this base radius.
        private const float BaseSeabedRadius = 15f;
        private const float PerItemLoadTimeout = 25f;   // per GLB, soft
        private const float OverallLoadTimeout = 120f;  // whole scene, hard safety

        private readonly SceneLoadState _loadState = new SceneLoadState();
        private int _loaded;
        private int _failed;

        private static readonly Dictionary<string, Color> KindColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
        {
            { "ROCK",       new Color(0.55f, 0.55f, 0.58f) },
            { "CORAL",      new Color(0.95f, 0.45f, 0.55f) },
            { "ANEMONE",    new Color(0.85f, 0.35f, 0.75f) },
            { "FISH",       new Color(0.95f, 0.80f, 0.25f) },
            { "SCHOOL",     new Color(0.30f, 0.70f, 0.95f) },
            { "TURTLE",     new Color(0.35f, 0.65f, 0.40f) },
            { "WRECK",      new Color(0.45f, 0.40f, 0.35f) },
            { "ARTIFICIAL", new Color(0.60f, 0.60f, 0.70f) },
            { "BOAT",       new Color(0.70f, 0.35f, 0.25f) },
            { "SPECIAL",    new Color(0.90f, 0.75f, 0.30f) },
        };

        /// <summary>
        /// Coroutine that builds the whole scene and reports a <see cref="BuildResult"/>.
        /// </summary>
        public IEnumerator BuildRoutine(SceneData scene, AssetManifest manifest, Action<BuildResult> onDone)
        {
            _loaded = 0;
            _failed = 0;
            _loadState.Reset();

            var root = new GameObject("Map");

            SceneEnv env = scene?.Env;

            // ── Environment first (seabed + water) so items sit above it ──────────
            float seabedRadius = BuildSeabedAndWater(root.transform, scene, env, out Bounds bounds);

            // ── Items ─────────────────────────────────────────────────────────────
            IReadOnlyList<SceneItem> items = scene?.Items() ?? new List<SceneItem>();
            var decorGos = new List<GameObject>();   // structure/scenery (for framing box)
            var allGos = new List<GameObject>();      // fallback when a map is swimmers-only
            foreach (SceneItem item in items)
            {
                var itemGo = new GameObject(ItemName(item));
                itemGo.transform.SetParent(root.transform, false);
                ApplyTransform(itemGo.transform, item, manifest);

                bounds.Encapsulate(itemGo.transform.localPosition);

                string url = manifest != null ? manifest.ResolveUrl(item.AssetId) : null;
                AssetManifest.Module module = manifest != null ? manifest.Get(item.AssetId) : null;
                bool swimmer = IsSwimmer(module?.Kind);

                allGos.Add(itemGo);
                if (!swimmer) decorGos.Add(itemGo);

                if (string.IsNullOrEmpty(url))
                {
                    // Unknown assetId (e.g. demo "warp:0" not in manifest) → placeholder.
                    SpawnPlaceholder(itemGo.transform, item, module);
                    _failed++;
                }
                else
                {
                    _loadState.BeginLoad();
                    // Static scenery (wreck/rock/coral/statue…) is grounded so its base
                    // sits on the seabed — matching the web's bakeStatic() recenter+drop.
                    LoadItemAsync(url, itemGo.transform, item, module, ground: !swimmer);
                }
            }

            // Wait for all in-flight GLB loads, with a hard safety timeout.
            float t = 0f;
            while (_loadState.PendingCount > 0 && t < OverallLoadTimeout)
            {
                t += Time.deltaTime;
                yield return null;
            }

            Vector3 center = bounds.center;
            float radius = Mathf.Max(bounds.extents.magnitude, seabedRadius, 1f);

            // Framing box: prefer real rendered bounds of the scenery; fall back to all
            // items, then to the seabed disc. This is what the camera actually frames.
            Bounds frameBox;
            if (!TryContentBounds(decorGos, out frameBox) && !TryContentBounds(allGos, out frameBox))
            {
                frameBox = new Bounds(new Vector3(0f, 0f, 0f), new Vector3(seabedRadius * 2f, 2f, seabedRadius * 2f));
            }

            onDone?.Invoke(new BuildResult
            {
                Root = root,
                Loaded = _loaded,
                Failed = _failed,
                Center = center,
                Radius = radius,
                MapName = scene?.Name,
                FrameCenter = frameBox.center,
                FrameSizeX = frameBox.size.x,
                FrameSizeY = frameBox.size.y,
                FrameSizeZ = frameBox.size.z,
                FrameMinY = frameBox.min.y,
            });
        }

        // ── Item transform ────────────────────────────────────────────────────────

        private static void ApplyTransform(Transform tr, SceneItem item, AssetManifest manifest)
        {
            double[] p = item.P;
            double[] r = item.R;
            double[] s = item.S;

            if (p != null && p.Length >= 3)
            {
                Vec3 up = WebCoord.PositionToUnity(new Vec3(p[0], p[1], p[2]));
                tr.localPosition = new Vector3((float)up.X, (float)up.Y, (float)up.Z);
            }

            if (r != null && r.Length >= 3)
            {
                Quat q = WebCoord.RotationToUnity(new Vec3(r[0], r[1], r[2]));
                tr.localRotation = new Quaternion((float)q.X, (float)q.Y, (float)q.Z, (float)q.W);
            }

            if (s != null && s.Length >= 3)
            {
                tr.localScale = new Vector3((float)s[0], (float)s[1], (float)s[2]);
            }
            else
            {
                float ds = manifest?.Get(item.AssetId) is AssetManifest.Module m ? (float)m.DefaultScale : 1f;
                tr.localScale = Vector3.one * (ds <= 0 ? 1f : ds);
            }
        }

        // ── Async GLB load (fire-and-forget, tracked via SceneLoadState) ───────────

        private async void LoadItemAsync(string url, Transform parent, SceneItem item, AssetManifest.Module module, bool ground)
        {
            bool ok = false;
            try
            {
                var gltf = new GltfImport();
                ok = await gltf.Load(url);
                if (ok)
                {
                    // Instantiate the main scene as children of the per-item GameObject.
                    ok = await gltf.InstantiateMainSceneAsync(parent);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SceneBuilder] GLB load failed for {item?.AssetId} ({url}): {e.Message}");
                ok = false;
            }

            if (ok)
            {
                // Match the web builder's bakeStatic(): recentre the model on X/Z and drop
                // its base to the pivot (localPosition Y=0) so it rests ON the seabed instead
                // of the GLB's own (often centred) pivot half-sinking into the sand.
                if (ground && parent != null) GroundToBase(parent);
                _loaded++;
            }
            else
            {
                // parent may still be alive; guard against scene teardown.
                if (parent != null) SpawnPlaceholder(parent, item, module);
                _failed++;
            }

            _loadState.CompleteLoad();
        }

        // ── Grounding (bakeStatic parity) ─────────────────────────────────────────────

        /// <summary>
        /// Recentre the freshly-instantiated GLB content under <paramref name="pivot"/> so
        /// that, in the pivot's local space, the content is centred on X/Z and its lowest
        /// point sits at Y=0. Mirrors the web builder's bakeStatic() so that a stored item
        /// position with Y=0 places the model's BASE on the seabed (not its centre).
        /// No-op when there is nothing renderable yet.
        /// </summary>
        private static void GroundToBase(Transform pivot)
        {
            if (!TryLocalBounds(pivot, out Bounds local)) return;

            var offset = new Vector3(-local.center.x, -local.min.y, -local.center.z);
            if (offset.sqrMagnitude < 1e-8f) return;

            for (int i = 0; i < pivot.childCount; i++)
                pivot.GetChild(i).localPosition += offset;
        }

        /// <summary>AABB of all mesh renderers under <paramref name="pivot"/>, expressed in
        /// the pivot's LOCAL space (labels excluded). Exact under rotation via mesh corners.</summary>
        private static bool TryLocalBounds(Transform pivot, out Bounds local)
        {
            local = default;
            var renderers = pivot.GetComponentsInChildren<MeshRenderer>(true);
            bool has = false;
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            Matrix4x4 worldToPivot = pivot.worldToLocalMatrix;

            foreach (var r in renderers)
            {
                if (r == null || r.transform.name == "Label") continue;
                var mf = r.GetComponent<MeshFilter>();
                Mesh mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null) continue;

                Bounds mb = mesh.bounds; // local to the renderer
                Matrix4x4 m = worldToPivot * r.transform.localToWorldMatrix;
                Vector3 c = mb.center, e = mb.extents;
                for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Vector3 corner = m.MultiplyPoint3x4(c + new Vector3(sx * e.x, sy * e.y, sz * e.z));
                    min = Vector3.Min(min, corner);
                    max = Vector3.Max(max, corner);
                    has = true;
                }
            }

            if (!has) return false;
            local = new Bounds((min + max) * 0.5f, max - min);
            return true;
        }

        /// <summary>World-space AABB over a set of item GameObjects (labels excluded).</summary>
        private static bool TryContentBounds(List<GameObject> gos, out Bounds bounds)
        {
            bounds = default;
            bool has = false;
            foreach (var go in gos)
            {
                if (go == null) continue;
                var renderers = go.GetComponentsInChildren<MeshRenderer>(true);
                foreach (var r in renderers)
                {
                    if (r == null || r.transform.name == "Label") continue;
                    if (!has) { bounds = r.bounds; has = true; }
                    else bounds.Encapsulate(r.bounds);
                }
            }
            return has;
        }

        // ── Placeholder ─────────────────────────────────────────────────────────────

        private static void SpawnPlaceholder(Transform parent, SceneItem item, AssetManifest.Module module)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Placeholder";
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = Vector3.zero;
            cube.transform.localScale = Vector3.one; // parent already carries item scale

            var rend = cube.GetComponent<Renderer>();
            if (rend != null) rend.sharedMaterial = PlaceholderMaterial(item, module);

            string label = item?.Label ?? module?.Name ?? item?.AssetId ?? "?";
            AddLabel(parent, label);
        }

        private static Color ColorForItem(SceneItem item, AssetManifest.Module module)
        {
            string kind = module?.Kind;
            if (string.IsNullOrEmpty(kind))
            {
                // Fall back to the assetId prefix (e.g. "warp:0", "pod:dolphin").
                string aid = item?.AssetId ?? "";
                int colon = aid.IndexOf(':');
                string prefix = colon > 0 ? aid.Substring(0, colon) : aid;
                switch (prefix.ToLowerInvariant())
                {
                    case "rock": kind = "ROCK"; break;
                    case "coral": kind = "CORAL"; break;
                    case "anemone": kind = "ANEMONE"; break;
                    case "fish": kind = "FISH"; break;
                    case "school": kind = "SCHOOL"; break;
                    case "pod": case "msh": kind = "SPECIAL"; break;
                    case "wreck": kind = "WRECK"; break;
                    case "cc0": kind = "ROCK"; break;
                    default: kind = null; break;
                }
            }
            if (kind != null && KindColors.TryGetValue(kind, out Color c)) return c;
            return new Color(0.5f, 0.5f, 0.5f); // gray fallback
        }

        // Material ฐานจาก Resources — บังคับให้ Standard shader (+variant โปร่งใส) ถูกฝังเข้า build
        // (Shader.Find เฉยๆ ใช้ใน build ไม่ได้ ถ้าไม่มี asset อ้าง shader → โดน strip → จอชมพู
        //  บทเรียนจริงจากเทสบน Windows Server รอบแรก)
        private static Material BaseMat(bool transparent)
        {
            var src = Resources.Load<Material>(transparent ? "DM_StandardTransparent" : "DM_Standard");
            if (src != null) return new Material(src); // clone กัน asset โดนแก้
            return new Material(Shader.Find("Standard")); // fallback ใน editor
        }

        private static Material PlaceholderMaterial(SceneItem item, AssetManifest.Module module)
        {
            var mat = BaseMat(false);
            mat.color = ColorForItem(item, module);
            mat.SetFloat("_Glossiness", 0f); // flat-shaded look
            return mat;
        }

        private static void AddLabel(Transform parent, string text)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0.9f, 0f);

            var tm = go.AddComponent<TextMesh>();
            // Bundled Thai font so labels like "HTMS ช้าง" keep their Thai glyphs on the
            // font-less Linux player (a TextMesh needs the font's material on its renderer).
            Font font = UiFont.Get();
            if (font != null)
            {
                tm.font = font;
                var mr = go.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = font.material;
            }
            tm.text = text;
            tm.characterSize = 0.15f;
            tm.fontSize = 48;
            tm.anchor = TextAnchor.LowerCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.white;

            go.AddComponent<Billboard>();
        }

        private static string ItemName(SceneItem item)
        {
            if (item == null) return "Item";
            string id = item.Id ?? "?";
            string aid = item.AssetId ?? "?";
            return $"Item_{id}_{aid}";
        }

        // ── Seabed (polar grid) + Water ─────────────────────────────────────────────

        private float BuildSeabedAndWater(Transform root, SceneData scene, SceneEnv env, out Bounds bounds)
        {
            float areaScale = env != null ? (float)env.AreaScale : 1f;
            float scaleX = env != null ? (float)env.AreaScaleX : 1f;
            float scaleZ = env != null ? (float)env.AreaScaleZ : 1f;
            float slopeX = env != null ? (float)env.AreaSlopeX : 0f;
            float slopeZ = env != null ? (float)env.AreaSlopeZ : 0f;
            float waterLevel = env != null ? (float)env.WaterLevel : 4f;

            float baseR = BaseSeabedRadius * Mathf.Max(0.05f, areaScale);

            // Grow the seabed to comfortably contain the items' horizontal spread.
            float itemMaxR = 0f;
            if (scene != null)
            {
                foreach (SceneItem it in scene.Items())
                {
                    double[] p = it.P;
                    if (p == null || p.Length < 3) continue;
                    Vec3 up = WebCoord.PositionToUnity(new Vec3(p[0], p[1], p[2]));
                    float d = Mathf.Sqrt((float)(up.X * up.X + up.Z * up.Z));
                    if (d > itemMaxR) itemMaxR = d;
                }
            }

            float rx = Mathf.Max(baseR * Mathf.Max(0.05f, scaleX), itemMaxR * 1.2f);
            float rz = Mathf.Max(baseR * Mathf.Max(0.05f, scaleZ), itemMaxR * 1.2f);
            float radius = Mathf.Max(rx, rz);

            int rings = 16, seg = 48;
            int[] dim = env?.SculptDimensions();
            if (dim != null && dim.Length >= 2 && dim[0] > 1 && dim[1] > 2)
            {
                rings = Mathf.Clamp(dim[0], 2, 128);
                seg = Mathf.Clamp(dim[1], 3, 256);
            }

            float[] sculpt = ReadSculpt(env);

            var seabed = new GameObject("Seabed");
            seabed.transform.SetParent(root, false);
            var mf = seabed.AddComponent<MeshFilter>();
            var mr = seabed.AddComponent<MeshRenderer>();
            mf.sharedMesh = BuildPolarGrid(rings, seg, rx, rz, slopeX, slopeZ, sculpt);
            mr.sharedMaterial = SandMaterial();
            seabed.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;

            // Water disc at env.waterLevel.
            var water = new GameObject("Water");
            water.transform.SetParent(root, false);
            water.transform.localPosition = new Vector3(0f, waterLevel, 0f);
            var wmf = water.AddComponent<MeshFilter>();
            var wmr = water.AddComponent<MeshRenderer>();
            // Double-sided disc: the orbit camera commonly sits BELOW the water level
            // (looking up at a tall wreck), and a single-sided (Cull Back) plane would be
            // invisible from underneath — the "no water in the QC shot" bug.
            wmf.sharedMesh = BuildDisc(radius * 1.02f, 64, doubleSided: true);
            wmr.sharedMaterial = WaterMaterial();
            water.AddComponent<WaterScroll>();

            bounds = new Bounds(Vector3.zero, new Vector3(rx * 2f, Mathf.Max(waterLevel, 2f) * 2f, rz * 2f));
            return radius;
        }

        private static float[] ReadSculpt(SceneEnv env)
        {
            if (env?.Sculpt == null) return null;
            var arr = env.Sculpt;
            var heights = new float[arr.Count];
            for (int i = 0; i < arr.Count; i++)
            {
                try { heights[i] = (float)(double)arr[i]; }
                catch { heights[i] = 0f; }
            }
            return heights;
        }

        /// <summary>
        /// Radial (polar) grid centred at origin. Verifies normals face UP after
        /// triangulation and flips winding if not — the seabed winding gotcha carried
        /// over from the web headless test (DESIGN_DOC §1.2 rule 3).
        /// </summary>
        private static Mesh BuildPolarGrid(int rings, int seg, float rx, float rz, float slopeX, float slopeZ, float[] sculpt)
        {
            int vertCount = 1 + rings * seg;
            var verts = new Vector3[vertCount];
            var uvs = new Vector2[vertCount];

            // Center vertex.
            verts[0] = new Vector3(0f, HeightAt(0f, 0f, slopeX, slopeZ, sculpt, seg, 0, 0), 0f);
            uvs[0] = new Vector2(0.5f, 0.5f);

            for (int r = 1; r <= rings; r++)
            {
                float frac = (float)r / rings;
                for (int j = 0; j < seg; j++)
                {
                    float ang = (Mathf.PI * 2f) * j / seg;
                    float x = Mathf.Cos(ang) * rx * frac;
                    float z = Mathf.Sin(ang) * rz * frac;
                    float y = HeightAt(x, z, slopeX, slopeZ, sculpt, seg, r, j);
                    int idx = 1 + (r - 1) * seg + j;
                    verts[idx] = new Vector3(x, y, z);
                    uvs[idx] = new Vector2(0.5f + 0.5f * frac * Mathf.Cos(ang), 0.5f + 0.5f * frac * Mathf.Sin(ang));
                }
            }

            var tris = new List<int>((rings * seg) * 6);

            // Inner fan: center → first ring.
            for (int j = 0; j < seg; j++)
            {
                int b = 1 + j;
                int c = 1 + (j + 1) % seg;
                tris.Add(0); tris.Add(b); tris.Add(c);
            }

            // Ring quads.
            for (int r = 1; r < rings; r++)
            {
                int ringStart = 1 + (r - 1) * seg;
                int nextStart = 1 + r * seg;
                for (int j = 0; j < seg; j++)
                {
                    int j2 = (j + 1) % seg;
                    int v00 = ringStart + j;
                    int v01 = ringStart + j2;
                    int v10 = nextStart + j;
                    int v11 = nextStart + j2;
                    tris.Add(v00); tris.Add(v10); tris.Add(v11);
                    tris.Add(v00); tris.Add(v11); tris.Add(v01);
                }
            }

            var mesh = new Mesh { name = "SeabedPolarGrid" };
            if (vertCount > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            // Winding verification: seabed must face UP. If the average normal points
            // down, reverse every triangle and recompute.
            if (AverageNormalY(mesh) < 0f)
            {
                int[] t = mesh.triangles;
                for (int i = 0; i < t.Length; i += 3)
                {
                    (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]);
                }
                mesh.triangles = t;
                mesh.RecalculateNormals();
            }

            return mesh;
        }

        private static float HeightAt(float x, float z, float slopeX, float slopeZ, float[] sculpt, int seg, int r, int j)
        {
            float y = x * slopeX + z * slopeZ;
            if (sculpt != null && sculpt.Length > 0)
            {
                int idx = r == 0 ? 0 : (r - 1) * seg + j;
                if (idx >= 0 && idx < sculpt.Length) y += sculpt[idx];
            }
            return y;
        }

        private static float AverageNormalY(Mesh mesh)
        {
            Vector3[] n = mesh.normals;
            if (n == null || n.Length == 0) return 1f;
            float sum = 0f;
            for (int i = 0; i < n.Length; i++) sum += n[i].y;
            return sum / n.Length;
        }

        private static Mesh BuildDisc(float radius, int seg, bool doubleSided = false)
        {
            var verts = new Vector3[seg + 1];
            var uvs = new Vector2[seg + 1];
            verts[0] = Vector3.zero;
            uvs[0] = new Vector2(0.5f, 0.5f);
            for (int j = 0; j < seg; j++)
            {
                float ang = (Mathf.PI * 2f) * j / seg;
                float x = Mathf.Cos(ang) * radius;
                float z = Mathf.Sin(ang) * radius;
                verts[1 + j] = new Vector3(x, 0f, z);
                uvs[1 + j] = new Vector2(0.5f + 0.5f * Mathf.Cos(ang), 0.5f + 0.5f * Mathf.Sin(ang));
            }

            var triList = new List<int>(seg * (doubleSided ? 6 : 3));
            for (int j = 0; j < seg; j++)
            {
                triList.Add(0); triList.Add(1 + j); triList.Add(1 + (j + 1) % seg);
            }
            // Second, reverse-wound copy so the disc renders from above AND below without
            // needing a Cull-Off shader variant (which the build strips → magenta lesson).
            if (doubleSided)
            {
                for (int j = 0; j < seg; j++)
                {
                    triList.Add(0); triList.Add(1 + (j + 1) % seg); triList.Add(1 + j);
                }
            }

            var mesh = new Mesh { name = "WaterDisc" };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = triList.ToArray();
            if (doubleSided)
            {
                // Coplanar opposing tris make RecalculateNormals cancel to ~zero (→ black
                // lighting). Force a constant up normal so shading is stable from both sides.
                var normals = new Vector3[verts.Length];
                for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;
                mesh.normals = normals;
            }
            else
            {
                mesh.RecalculateNormals();
            }
            mesh.RecalculateBounds();
            // Single-sided path keeps the up-facing winding guarantee; double-sided already
            // covers both hemispheres so we leave its (constant-up) normals as built.
            if (!doubleSided && AverageNormalY(mesh) < 0f)
            {
                int[] t = mesh.triangles;
                for (int i = 0; i < t.Length; i += 3)
                {
                    (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]);
                }
                mesh.triangles = t;
                mesh.RecalculateNormals();
            }
            return mesh;
        }

        private static Material SandMaterial()
        {
            var mat = BaseMat(false);
            mat.color = new Color(0.82f, 0.74f, 0.58f);
            mat.SetFloat("_Glossiness", 0.1f);
            mat.SetFloat("_Metallic", 0f);
            return mat;
        }

        private static Material WaterMaterial()
        {
            var mat = BaseMat(true);
            // Transparent rendering mode (material ฐานตั้ง keyword มาแล้ว — เซ็ตซ้ำกัน regress)
            mat.SetFloat("_Mode", 3f);
            mat.SetFloat("_SrcBlend", (float)(int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)(int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            // Clearer, more saturated tropical blue with a higher alpha — the old (0.28)
            // wash was nearly invisible over the deep-blue background in the QC shot.
            mat.color = new Color(0.10f, 0.52f, 0.78f, 0.55f);
            mat.SetFloat("_Glossiness", 0.85f);

            var tex = GenerateCausticTexture(64);
            mat.mainTexture = tex;
            mat.mainTextureScale = new Vector2(4f, 4f);
            return mat;
        }

        private static Texture2D GenerateCausticTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float n = Mathf.PerlinNoise(x * 0.12f, y * 0.12f);
                    n = Mathf.Pow(n, 2.2f);
                    byte a = (byte)Mathf.Clamp(120 + n * 135f, 0, 255);
                    px[y * size + x] = new Color32(200, 235, 255, a);
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}
