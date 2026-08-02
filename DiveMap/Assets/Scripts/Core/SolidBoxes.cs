using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace DiveMap.Core
{
    /// <summary>
    /// "รูคือรู" — a hole is a hole.
    ///
    /// 🔴 Reported twice. An open cube-frame reef module has a hole you can see straight through
    /// and cannot swim through: <c>SceneBuilder</c> gives every placed object ONE axis-aligned box
    /// taken from its renderer bounds, and <see cref="DroneFlight.Step"/> pushes the diver out of
    /// that box. A frame becomes a brick. The user is not describing a physics bug; they are
    /// describing a shape we never sent.
    ///
    /// The fix is a hull per MODEL, not per object: a small JSON published next to the GLB on the
    /// CDN (<c>art_1330_xr0.glb</c> → <c>art_1330_xr0.solids.json</c>) holding up to ~64 boxes that
    /// cover the solid parts and leave the openings empty.
    ///
    /// <code>
    /// { "v": 1, "grid": [32,16,32], "bbox": {"min":[x,y,z], "max":[x,y,z]},
    ///   "boxes": [[minx,miny,minz,maxx,maxy,maxz], ...] }   // 0..1 inside bbox, model space
    /// </code>
    ///
    /// This class is the whole decision layer — read the file, refuse the ones we cannot trust,
    /// put the boxes where the object actually stands, and choose which of them the drone is
    /// allowed to carry this frame. UnityEngine-free on purpose (the DiveMap.Core rule) so every
    /// one of those decisions is pinned by EditMode tests instead of by a 35-minute CI round and
    /// a user flying into a wall.
    ///
    /// ⚠️ Two limits worth knowing before expecting miracles:
    ///   • <see cref="DroneFlight.CamRadius"/> is 3.2, and the collision test grows every box by
    ///     it on all six sides. A hole narrower than ~6.4 units plus the bar thickness stays shut
    ///     no matter how good the hull is — that is the camera's near volume, not the geometry.
    ///   • The drone only ever tests AABBs, so a hull on a yawed object is stored as the
    ///     axis-aligned box of each rotated part. At yaw 0/90/180/270 that is exact; in between it
    ///     is CONSERVATIVE (slightly fat), never leaky. Fat is the safe direction: the worst case
    ///     is the old behaviour back again for that one object, not a diver inside the scenery.
    /// </summary>
    public static class SolidBoxes
    {
        /// <summary>The only file format we know. A "v": 2 must fall back, not be guessed at.</summary>
        public const int FormatVersion = 1;

        /// <summary>
        /// A file claiming more boxes than this is refused outright and the object keeps its single
        /// AABB. The contract says "up to ~64"; the ceiling sits above that so a legitimate 68 is
        /// not thrown away over the "~", but far enough below a runaway generator that one bad file
        /// cannot multiply the per-frame collision cost by a hundred on every map that uses it.
        /// </summary>
        public const int MaxBoxesPerModel = 96;

        // ── The budget ────────────────────────────────────────────────────────────────
        //
        // 🔴 Read this before raising anything here. DroneFlight.Step walks the whole box array
        // EVERY FRAME of the dive, on a phone.
        //
        //   today          494 objects × 1 box   =    494 boxes   (map T-13, the biggest we ship)
        //   naive hulls    494 objects × 64      = 31,616 boxes   — 64× the work, every frame
        //   here           ≤ 2,048 detailed + 1 box for every object left over  ≈ 2,542 worst case
        //
        // The way out is not a smaller number, it is a smaller QUESTION: you can only swim through
        // a hole you are next to. So the nearest objects carry their real hull and everything else
        // carries exactly the box it carries today. T-13 is 394 copies of the same frame — a flat
        // global cap would give holes to the first 32 of them and leave the other 362 as bricks,
        // which is the same bug reported a third time. Near-first has no such edge: whatever you
        // are close enough to enter is always in the detailed set.
        //
        // Whole groups only. Half a hull is a wall across the middle of a doorway, which is worse
        // than the brick because it is invisible.

        /// <summary>Boxes the drone may carry as real hull geometry at one time.</summary>
        public const int DiverBoxBudget = 2048;

        /// <summary>Objects within this many units of the diver get their hull; the rest get their AABB.</summary>
        public const double DetailRadius = 90.0;

        /// <summary>
        /// Re-pick after the diver has moved this far. DISTANCE, not frames and not seconds: the
        /// tour runs at 3 fps on CI and 60 on a phone, and the drone's speed is capped at 30 u/s,
        /// so distance is the only trigger that means the same thing on both (HANDOFF §4 rule 13).
        /// 12 u of travel against a 90 u radius leaves 78 u — over two seconds of full throttle —
        /// between an object entering range and the diver reaching it.
        /// </summary>
        public const double RefreshMove = 12.0;

        // ── Types ─────────────────────────────────────────────────────────────────────

        /// <summary>One box. Normalised 0..1 as parsed; world units once through <see cref="ToWorld"/>.</summary>
        public struct Box
        {
            public Vec3 Min, Max;
            public Box(Vec3 min, Vec3 max) { Min = min; Max = max; }

            public static Box FromMinMax(double x0, double y0, double z0,
                                         double x1, double y1, double z1)
                => new Box(new Vec3(x0, y0, z0), new Vec3(x1, y1, z1));

            public override string ToString() => $"[{Min} → {Max}]";
        }

        /// <summary>A parsed <c>.solids.json</c>. Never partially valid: either we trust it or it is null.</summary>
        public sealed class Model
        {
            public int Version;
            public Vec3 BboxMin, BboxMax;
            /// <summary>Boxes in 0..1 of the bbox, model space. Sorted as the file listed them.</summary>
            public Box[] Boxes;

            /// <summary>Readable but nothing solid in it — treated exactly like an absent file.</summary>
            public bool IsEmpty => Boxes == null || Boxes.Length == 0;
        }

        /// <summary>
        /// One placed object's two shapes: the box it has today, and the hull it could have.
        /// <see cref="Fine"/> null means no hull — the object is still solid, through
        /// <see cref="Coarse"/>. There is no state in which an object becomes passable by accident.
        /// </summary>
        public struct Group
        {
            public Box Coarse;
            public Box[] Fine;
        }

        /// <summary>
        /// Which model-space axes are mirrored on the way into Unity's left-handed space.
        ///
        /// glTF is right-handed and Unity is not, so the importer negates exactly one axis on its
        /// way in. Two conventions exist in this project's world: glTFast — the importer that
        /// actually places these meshes — negates X, while three.js/the web builder negate Z, which
        /// is why <see cref="WebCoord"/> flips Z for item positions. They are not interchangeable.
        ///
        /// 🔴 This machine has no Unity Editor and no glTFast source to read, so <see cref="Importer"/>
        /// is an ASSUMPTION, held in one place on purpose — the same shape as the one negation in
        /// <c>GyroMath.ToUnity</c> that could only be settled on a device. If a module's hole turns
        /// up on the wrong side of the module, this is the line: switch to <see cref="FlipZ"/> (or
        /// <see cref="None"/>) and nothing else moves. Both mappings are pinned by tests.
        ///
        /// It costs nothing on the shape that was actually reported: an open cube frame is
        /// symmetric, so every one of these choices produces the same hull for it.
        /// </summary>
        public struct Mirror
        {
            public bool X, Y, Z;
            public static Mirror None => default;
            public static Mirror FlipX => new Mirror { X = true };
            public static Mirror FlipZ => new Mirror { Z = true };

            /// <summary>What glTFast is believed to do. See the remarks above before changing it.</summary>
            public static Mirror Importer => FlipX;
        }

        // ── Where the file lives ──────────────────────────────────────────────────────

        /// <summary>
        /// The hull URL beside a GLB URL. Null when the input is not a <c>.glb</c> — a caller that
        /// gets null asks for nothing and keeps its single AABB. Any query string is carried over,
        /// because a CDN URL with <c>?v=</c> on it is still the same directory.
        /// </summary>
        public static string UrlFor(string glbUrl)
        {
            if (string.IsNullOrWhiteSpace(glbUrl)) return null;
            string u = glbUrl.Trim();

            int cut = u.IndexOfAny(new[] { '?', '#' });
            string path = cut >= 0 ? u.Substring(0, cut) : u;
            string tail = cut >= 0 ? u.Substring(cut) : "";

            if (!path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)) return null;
            return path.Substring(0, path.Length - 4) + ".solids.json" + tail;
        }

        // ── Reading the file ──────────────────────────────────────────────────────────

        /// <summary>
        /// Parse a <c>.solids.json</c>. Returns null for everything we will not act on — absent,
        /// empty, malformed, a version we do not know, a degenerate bbox, a row that is not six
        /// numbers, or more boxes than <see cref="MaxBoxesPerModel"/>.
        ///
        /// Null and "empty" both mean the same thing to the caller (keep the single AABB), and that
        /// is the point: there is no failure mode in this file that can make an object passable.
        /// Refusing a file we half-understand is strictly better than building a hull with a hole
        /// where the model has a wall.
        /// </summary>
        public static Model Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            JObject root;
            try { root = JObject.Parse(json); }
            catch { return null; }               // malformed → the object stays solid, as today
            if (root == null) return null;

            if (!Num(root["v"], out double version) || (int)version != FormatVersion) return null;

            // Typed, not just non-null: JToken's string indexer THROWS on an array, and this file
            // arrives from a CDN, so "bbox": [] must be a refusal and not an exception on the
            // async fetch's thread.
            if (!(root["bbox"] is JObject bbox)) return null;
            if (!ReadVec(bbox["min"], out Vec3 bmin) || !ReadVec(bbox["max"], out Vec3 bmax)) return null;
            // A bbox with no volume makes "normalised inside the bbox" meaningless.
            if (!(bmax.X > bmin.X) || !(bmax.Y > bmin.Y) || !(bmax.Z > bmin.Z)) return null;

            if (!(root["boxes"] is JArray arr)) return null;
            if (arr.Count > MaxBoxesPerModel) return null;

            var boxes = new List<Box>(arr.Count);
            foreach (JToken row in arr)
            {
                if (!(row is JArray r) || r.Count < 6) return null;
                var v = new double[6];
                for (int i = 0; i < 6; i++)
                {
                    if (!Num(r[i], out v[i])) return null;
                    v[i] = Clamp01(v[i]);        // a generator rounding to 1.0000001 is not an error
                }

                double x0 = Math.Min(v[0], v[3]), x1 = Math.Max(v[0], v[3]);
                double y0 = Math.Min(v[1], v[4]), y1 = Math.Max(v[1], v[4]);
                double z0 = Math.Min(v[2], v[5]), z1 = Math.Max(v[2], v[5]);

                // A box with no thickness on some axis is a point or a line. It cannot be a wall,
                // but grown by the camera radius it becomes a 6.4-unit invisible ball in mid-water.
                if (x1 <= x0 || y1 <= y0 || z1 <= z0) continue;

                boxes.Add(Box.FromMinMax(x0, y0, z0, x1, y1, z1));
            }

            return new Model
            {
                Version = (int)version,
                BboxMin = bmin,
                BboxMax = bmax,
                Boxes = boxes.ToArray(),
            };
        }

        /// <summary>
        /// Does this hull plausibly belong to the object we are about to wrap in it?
        ///
        /// The hull is fitted to the object's MEASURED local bounds rather than to the bbox the file
        /// declares, so that grounding (SceneBuilder's <c>GroundToBase</c> recentre-and-drop) needs
        /// no replay here. That fit is a stretch, and a stretch will happily map a hull onto a model
        /// it has nothing to do with. Comparing the two boxes' proportions is the one check that
        /// catches it: a hull for the wrong asset, or one authored in different units, does not
        /// match the model's own shape within a third.
        ///
        /// Deliberately generous. A false refusal costs a hole; a false accept costs a hull in the
        /// wrong place, which reads as the map itself being broken.
        /// </summary>
        public static bool FitsBbox(Model model, Box fit, double tolerance = 0.35)
        {
            if (model == null) return false;
            double t = tolerance < 0 ? 0 : tolerance;
            return AxisFits(model.BboxMax.X - model.BboxMin.X, fit.Max.X - fit.Min.X, t)
                && AxisFits(model.BboxMax.Y - model.BboxMin.Y, fit.Max.Y - fit.Min.Y, t)
                && AxisFits(model.BboxMax.Z - model.BboxMin.Z, fit.Max.Z - fit.Min.Z, t);
        }

        private static bool AxisFits(double declared, double measured, double tol)
        {
            // A flat model (a plate) has a near-zero axis in both boxes; treat "both tiny" as fine
            // rather than dividing by it.
            const double Tiny = 1e-4;
            if (declared <= Tiny || measured <= Tiny) return declared <= Tiny && measured <= Tiny;
            double ratio = measured / declared;
            return ratio <= 1.0 + tol && ratio >= 1.0 / (1.0 + tol);
        }

        // ── Putting the hull where the object stands ──────────────────────────────────

        /// <summary>
        /// Hull → world boxes. <paramref name="fit"/> is the object's content bounds in its OWN
        /// pivot space (i.e. after grounding), <paramref name="pos"/>/<paramref name="rot"/>/
        /// <paramref name="scale"/> are that pivot's world transform — Unity's own order,
        /// <c>world = pos + rot · (scale ⊙ local)</c>.
        ///
        /// Each rotated box comes back as its world AABB, because that is the only shape
        /// <see cref="DroneFlight"/> tests. See the class remarks for why fat-but-never-leaky is
        /// the right side to err on.
        ///
        /// Null in, null out: a caller with no hull keeps the box it already has.
        /// </summary>
        public static Box[] ToWorld(Model model, Box fit, Vec3 pos, Quat rot, Vec3 scale)
            => ToWorld(model, fit, pos, rot, scale, Mirror.Importer);

        public static Box[] ToWorld(Model model, Box fit, Vec3 pos, Quat rot, Vec3 scale, Mirror mirror)
        {
            if (model == null || model.IsEmpty) return null;

            double spanX = fit.Max.X - fit.Min.X;
            double spanY = fit.Max.Y - fit.Min.Y;
            double spanZ = fit.Max.Z - fit.Min.Z;

            var outBoxes = new Box[model.Boxes.Length];
            for (int i = 0; i < model.Boxes.Length; i++)
            {
                Box n = model.Boxes[i];
                MapAxis(n.Min.X, n.Max.X, mirror.X, fit.Min.X, spanX, out double lx0, out double lx1);
                MapAxis(n.Min.Y, n.Max.Y, mirror.Y, fit.Min.Y, spanY, out double ly0, out double ly1);
                MapAxis(n.Min.Z, n.Max.Z, mirror.Z, fit.Min.Z, spanZ, out double lz0, out double lz1);

                double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

                for (int c = 0; c < 8; c++)
                {
                    var local = new Vec3(((c & 1) == 0 ? lx0 : lx1) * scale.X,
                                         ((c & 2) == 0 ? ly0 : ly1) * scale.Y,
                                         ((c & 4) == 0 ? lz0 : lz1) * scale.Z);
                    Vec3 w = Rotate(rot, local);
                    double wx = pos.X + w.X, wy = pos.Y + w.Y, wz = pos.Z + w.Z;
                    if (wx < minX) minX = wx; if (wx > maxX) maxX = wx;
                    if (wy < minY) minY = wy; if (wy > maxY) maxY = wy;
                    if (wz < minZ) minZ = wz; if (wz > maxZ) maxZ = wz;
                }

                outBoxes[i] = Box.FromMinMax(minX, minY, minZ, maxX, maxY, maxZ);
            }
            return outBoxes;
        }

        /// <summary>
        /// One axis of the normalised → local mapping. Mirroring is a reflection of the normalised
        /// interval (<c>t → 1−t</c>), so it stays inside the object's own bounds whichever way it
        /// goes — a wrong guess moves the hole, it never moves the hull off the model.
        /// </summary>
        private static void MapAxis(double n0, double n1, bool mirror,
                                    double fitMin, double fitSpan, out double a, out double b)
        {
            if (mirror)
            {
                double t = n0;
                n0 = 1.0 - n1;
                n1 = 1.0 - t;
            }
            a = fitMin + n0 * fitSpan;
            b = fitMin + n1 * fitSpan;
        }

        /// <summary>v′ = v + 2w(q⃗ × v) + 2 q⃗ × (q⃗ × v) — the standard rotate, no matrix needed.</summary>
        private static Vec3 Rotate(Quat q, Vec3 v)
        {
            double x = q.X, y = q.Y, z = q.Z, w = q.W;
            double tx = 2.0 * (y * v.Z - z * v.Y);
            double ty = 2.0 * (z * v.X - x * v.Z);
            double tz = 2.0 * (x * v.Y - y * v.X);
            return new Vec3(v.X + w * tx + (y * tz - z * ty),
                            v.Y + w * ty + (z * tx - x * tz),
                            v.Z + w * tz + (x * ty - y * tx));
        }

        // ── Choosing what the drone carries ───────────────────────────────────────────

        /// <summary>
        /// Fill <paramref name="into"/> with the boxes the diver should collide with from here.
        /// Returns how many objects got their real hull.
        ///
        /// Every group contributes something — its hull if it was picked, otherwise the single box
        /// it has today — so the count of SOLID OBJECTS never changes with the budget, only their
        /// detail. That is the invariant worth keeping: a budget that could make scenery passable
        /// would trade a visible bug for one nobody can reproduce.
        ///
        /// Output order follows the group order, not the distance order, so the result is stable
        /// between refreshes and reproducible in a test.
        /// </summary>
        public static int Select(IReadOnlyList<Group> groups, Vec3 eye, List<Box> into,
                                 double detailRadius = DetailRadius, int budget = DiverBoxBudget)
        {
            if (into == null) return 0;
            into.Clear();
            if (groups == null || groups.Count == 0) return 0;

            int n = groups.Count;
            var dist = new double[n];
            var order = new List<int>();
            double r2 = detailRadius * detailRadius;

            for (int i = 0; i < n; i++)
            {
                Box[] fine = groups[i].Fine;
                if (fine == null || fine.Length == 0) continue;
                double d2 = DistanceSq(groups[i].Coarse, eye);
                if (d2 > r2) continue;
                dist[i] = d2;
                order.Add(i);
            }

            // Nearest first, ties by index so two identical modules the same distance away always
            // resolve the same way (T-13 is 394 copies of one frame — ties are the normal case).
            order.Sort((a, b) =>
            {
                int c = dist[a].CompareTo(dist[b]);
                return c != 0 ? c : a.CompareTo(b);
            });

            var detailed = new bool[n];
            int used = 0, picked = 0;
            for (int k = 0; k < order.Count; k++)
            {
                int i = order[k];
                int need = groups[i].Fine.Length;
                if (used + need > budget) break;   // stop at the budget; nearer always wins
                detailed[i] = true;
                used += need;
                picked++;
            }

            for (int i = 0; i < n; i++)
            {
                if (detailed[i]) into.AddRange(groups[i].Fine);
                else into.Add(groups[i].Coarse);
            }
            return picked;
        }

        /// <summary>Squared distance from a point to a box; 0 when the point is inside it.</summary>
        public static double DistanceSq(Box b, Vec3 p)
        {
            double dx = p.X < b.Min.X ? b.Min.X - p.X : (p.X > b.Max.X ? p.X - b.Max.X : 0.0);
            double dy = p.Y < b.Min.Y ? b.Min.Y - p.Y : (p.Y > b.Max.Y ? p.Y - b.Max.Y : 0.0);
            double dz = p.Z < b.Min.Z ? b.Min.Z - p.Z : (p.Z > b.Max.Z ? p.Z - b.Max.Z : 0.0);
            return dx * dx + dy * dy + dz * dz;
        }

        // ── JSON helpers ──────────────────────────────────────────────────────────────

        private static bool Num(JToken t, out double d)
        {
            d = 0;
            if (t == null) return false;
            if (t.Type != JTokenType.Integer && t.Type != JTokenType.Float) return false;
            d = (double)t;
            return !double.IsNaN(d) && !double.IsInfinity(d);
        }

        private static bool ReadVec(JToken t, out Vec3 v)
        {
            v = default;
            if (!(t is JArray a) || a.Count < 3) return false;
            if (!Num(a[0], out double x) || !Num(a[1], out double y) || !Num(a[2], out double z)) return false;
            v = new Vec3(x, y, z);
            return true;
        }

        private static double Clamp01(double d) => d < 0.0 ? 0.0 : (d > 1.0 ? 1.0 : d);
    }
}
