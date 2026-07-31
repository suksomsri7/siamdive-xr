using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace DiveMap.Core
{
    /// <summary>
    /// I4 — moving, scaling and rotating several objects at once, about their shared pivot.
    ///
    /// Ported from the web's <c>_msStart</c> / <c>_msSnap</c>. The maths that matters is the
    /// PIVOT: a group transform has to happen about the group's own centre, not about each
    /// object's origin. Scale the group about each origin and the objects grow without moving
    /// apart — the arrangement silently collapses into itself, and there is no undo step that
    /// looks wrong until you notice everything is overlapping.
    ///
    /// Snapping is the web's too: a grid step that only applies when the user asks for it, so a
    /// careful arrangement is never quietly nudged.
    /// </summary>
    public static class MultiSelect
    {
        /// <summary>The web's snap step, in world units.</summary>
        public const double SnapStep = 5.0;

        /// <summary>Centre of a set of items — the pivot every group operation turns about.</summary>
        public static bool Pivot(JArray items, IEnumerable<string> ids,
                                 out double px, out double py, out double pz)
        {
            px = py = pz = 0.0;
            if (items == null || ids == null) return false;

            int n = 0;
            foreach (string id in ids)
            {
                JObject o = SceneEdit.Find(items, id);
                if (o == null) continue;
                double[] p = Vec(o, "p", 0, 0, 0);
                px += p[0]; py += p[1]; pz += p[2];
                n++;
            }
            if (n == 0) return false;

            px /= n; py /= n; pz /= n;
            return true;
        }

        /// <summary>Shift every selected item by the same offset.</summary>
        public static int MoveBy(JArray items, IEnumerable<string> ids, double dx, double dy, double dz)
        {
            if (items == null || ids == null) return 0;
            int n = 0;
            foreach (string id in ids)
            {
                JObject o = SceneEdit.Find(items, id);
                if (o == null) continue;
                double[] p = Vec(o, "p", 0, 0, 0);
                if (SceneEdit.Move(items, id, p[0] + dx, p[1] + dy, p[2] + dz)) n++;
            }
            return n;
        }

        /// <summary>
        /// Scale the group about its pivot: each item's own scale multiplies AND its distance
        /// from the pivot scales with it. Doing only the first is the mistake described above.
        /// </summary>
        public static int ScaleBy(JArray items, IEnumerable<string> ids, double factor)
        {
            if (items == null || ids == null || factor <= 0.0) return 0;
            if (!Pivot(items, ids, out double cx, out double cy, out double cz)) return 0;

            int n = 0;
            foreach (string id in ids)
            {
                JObject o = SceneEdit.Find(items, id);
                if (o == null) continue;

                double[] p = Vec(o, "p", 0, 0, 0);
                double[] s = Vec(o, "s", 1, 1, 1);

                SceneEdit.Move(items, id,
                               cx + (p[0] - cx) * factor,
                               cy + (p[1] - cy) * factor,
                               cz + (p[2] - cz) * factor);
                SceneEdit.Scale(items, id, s[0] * factor, s[1] * factor, s[2] * factor);
                n++;
            }
            return n;
        }

        /// <summary>
        /// Rotate the group about its pivot on Y: every item turns AND orbits. Turning them in
        /// place would leave a row of objects facing a new direction but still in a straight line
        /// — which reads as a bug, not a rotation.
        /// </summary>
        public static int RotateBy(JArray items, IEnumerable<string> ids, double radians)
        {
            if (items == null || ids == null) return 0;
            if (!Pivot(items, ids, out double cx, out double cy, out double cz)) return 0;

            double cos = Math.Cos(radians), sin = Math.Sin(radians);
            int n = 0;
            foreach (string id in ids)
            {
                JObject o = SceneEdit.Find(items, id);
                if (o == null) continue;

                double[] p = Vec(o, "p", 0, 0, 0);
                double[] r = Vec(o, "r", 0, 0, 0);

                double dx = p[0] - cx, dz = p[2] - cz;
                SceneEdit.Move(items, id, cx + dx * cos - dz * sin, p[1], cz + dx * sin + dz * cos);
                SceneEdit.Rotate(items, id, r[0], GizmoWrap(r[1] + radians), r[2]);
                n++;
            }
            return n;
        }

        /// <summary>Snap every selected item's position to the grid (opt-in, never automatic).</summary>
        public static int Snap(JArray items, IEnumerable<string> ids, double step = SnapStep)
        {
            if (items == null || ids == null || step <= 0.0) return 0;
            int n = 0;
            foreach (string id in ids)
            {
                JObject o = SceneEdit.Find(items, id);
                if (o == null) continue;
                double[] p = Vec(o, "p", 0, 0, 0);
                if (SceneEdit.Move(items, id,
                                   Math.Round(p[0] / step) * step,
                                   p[1],                             // height is deliberate, never snapped
                                   Math.Round(p[2] / step) * step)) n++;
            }
            return n;
        }

        /// <summary>Delete the whole selection. Returns how many went.</summary>
        public static int DeleteAll(JArray items, IEnumerable<string> ids) =>
            SceneEdit.DeleteMany(items, new List<string>(ids ?? new string[0]));

        /// <summary>Duplicate the whole selection; returns the new ids, in the same order.</summary>
        public static List<string> DuplicateAll(JArray items, IEnumerable<string> ids, long stamp)
        {
            var made = new List<string>();
            if (items == null || ids == null) return made;

            // Snapshot the ids first: Duplicate appends to the same array we are iterating.
            var source = new List<string>(ids);
            foreach (string id in source)
            {
                JObject copy = SceneEdit.Duplicate(items, id, stamp);
                if (copy != null) made.Add((string)copy["id"]);
            }
            return made;
        }

        private static double GizmoWrap(double radians)
        {
            const double twoPi = Math.PI * 2.0;
            double r = radians % twoPi;
            if (r > Math.PI) r -= twoPi;
            else if (r < -Math.PI) r += twoPi;
            return r;
        }

        private static double[] Vec(JObject o, string key, double dx, double dy, double dz)
        {
            var v = new[] { dx, dy, dz };
            if (o != null && o[key] is JArray a)
                for (int i = 0; i < 3 && i < a.Count; i++)
                    try { v[i] = a[i].Value<double>(); } catch { }
            return v;
        }
    }
}
