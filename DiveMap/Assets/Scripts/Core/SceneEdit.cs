using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace DiveMap.Core
{
    /// <summary>
    /// Editing operations on a map, as pure functions over its JSON.
    ///
    /// The scene JSON is the source of truth — the GameObjects SceneBuilder makes are a
    /// rendering of it. Every edit therefore changes the JSON and lets the normal build path
    /// redraw, rather than moving a Transform and trying to write it back later. That is the
    /// same rule the purchase path already follows, and it is why undo can be a plain snapshot
    /// of the item array instead of a log of reversible commands.
    ///
    /// Mirrors builder.html's item mutations:
    /// <code>
    ///   dupSelected()      :   clone with a fresh id, offset so it is not hidden underneath
    ///   recolorSelected()  :   item.c = '#rrggbb'
    ///   _objName           :   item.n = display name
    ///   delete             :   splice out of items
    ///   histClearScene()   :   items = []
    /// </code>
    /// Unit-tested in <c>SceneEditTests</c>.
    /// </summary>
    public static class SceneEdit
    {
        /// <summary>How far a duplicate is nudged so it does not sit exactly inside the original.</summary>
        public const double DuplicateOffset = 6.0;

        /// <summary>The item array, created if the scene has none.</summary>
        public static JArray Items(SceneData scene)
        {
            if (scene == null) return new JArray();
            if (!(scene.Root["items"] is JArray arr))
            {
                arr = new JArray();
                scene.Root["items"] = arr;
            }
            return arr;
        }

        /// <summary>Index of the item with <paramref name="id"/>, or -1.</summary>
        public static int IndexOf(JArray items, string id)
        {
            if (items == null || string.IsNullOrEmpty(id)) return -1;
            for (int i = 0; i < items.Count; i++)
                if (items[i] is JObject o && string.Equals((string)o["id"], id, StringComparison.Ordinal))
                    return i;
            return -1;
        }

        public static JObject Find(JArray items, string id)
        {
            int i = IndexOf(items, id);
            return i >= 0 ? (JObject)items[i] : null;
        }

        /// <summary>Remove one item. Returns true when something was removed.</summary>
        public static bool Delete(JArray items, string id)
        {
            int i = IndexOf(items, id);
            if (i < 0) return false;
            items.RemoveAt(i);
            return true;
        }

        /// <summary>Remove several. Returns how many went.</summary>
        public static int DeleteMany(JArray items, IEnumerable<string> ids)
        {
            if (ids == null) return 0;
            int n = 0;
            foreach (string id in ids) if (Delete(items, id)) n++;
            return n;
        }

        /// <summary>
        /// Copy an item, offset on X/Z so the copy is visible rather than buried inside the
        /// original. <paramref name="stamp"/> makes the new id unique — pass a tick count; two
        /// duplicates in the same frame must not collide.
        /// </summary>
        public static JObject Duplicate(JArray items, string id, long stamp,
                                        double offset = DuplicateOffset)
        {
            JObject src = Find(items, id);
            if (src == null) return null;

            var copy = (JObject)src.DeepClone();
            copy["id"] = NewId(stamp);

            if (copy["p"] is JArray p && p.Count >= 3)
            {
                p[0] = ToDouble(p[0]) + offset;
                p[2] = ToDouble(p[2]) + offset;
            }
            items.Add(copy);
            return copy;
        }

        /// <summary>Set an item's position (world units).</summary>
        public static bool Move(JArray items, string id, double x, double y, double z)
        {
            JObject o = Find(items, id);
            if (o == null) return false;
            o["p"] = new JArray(x, y, z);
            return true;
        }

        /// <summary>Set an item's Euler rotation, in radians — the units the map JSON uses.</summary>
        public static bool Rotate(JArray items, string id, double rx, double ry, double rz)
        {
            JObject o = Find(items, id);
            if (o == null) return false;
            o["r"] = new JArray(rx, ry, rz);
            return true;
        }

        /// <summary>
        /// Set an item's scale. Clamped to a sane range: the web's gizmo cannot produce a zero
        /// or negative scale, and a 0 would make the mesh vanish with no way to grab it again.
        /// </summary>
        public static bool Scale(JArray items, string id, double sx, double sy, double sz)
        {
            JObject o = Find(items, id);
            if (o == null) return false;
            o["s"] = new JArray(ClampScale(sx), ClampScale(sy), ClampScale(sz));
            return true;
        }

        public const double MinScale = 0.05;
        public const double MaxScale = 40.0;

        public static double ClampScale(double v)
        {
            if (double.IsNaN(v)) return 1.0;
            if (v < MinScale) return MinScale;
            if (v > MaxScale) return MaxScale;
            return v;
        }

        /// <summary>
        /// Tint an item. <paramref name="hex"/> must be <c>#rrggbb</c>; null clears the tint.
        /// Anything else is refused rather than written — a malformed colour reaches the web
        /// too, and there it renders as black.
        /// </summary>
        public static bool Recolor(JArray items, string id, string hex)
        {
            JObject o = Find(items, id);
            if (o == null) return false;

            if (hex == null) { o.Remove("c"); return true; }
            if (!IsHexColor(hex)) return false;
            o["c"] = hex.ToLowerInvariant();
            return true;
        }

        public static bool IsHexColor(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex.Length != 7 || hex[0] != '#') return false;
            for (int i = 1; i < 7; i++)
            {
                char c = char.ToLowerInvariant(hex[i]);
                bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>Give an item a display name (the web's <c>_objName</c>). Empty clears it.</summary>
        public static bool Rename(JArray items, string id, string name)
        {
            JObject o = Find(items, id);
            if (o == null) return false;

            string n = (name ?? "").Trim();
            if (n.Length == 0) o.Remove("n");
            else o["n"] = n.Length > 60 ? n.Substring(0, 60) : n;
            return true;
        }

        /// <summary>Everything off the map (the web's <c>histClearScene</c>). Returns how many.</summary>
        public static int Clear(JArray items)
        {
            if (items == null) return 0;
            int n = items.Count;
            items.Clear();
            return n;
        }

        /// <summary>
        /// A fresh item id. The web uses a random string; a tick stamp plus a counter is
        /// equally unique here and reproducible in tests, which a random one is not.
        /// </summary>
        public static string NewId(long stamp)
        {
            unchecked { _seq++; }
            return "u" + stamp.ToString("x") + "_" + _seq.ToString("x");
        }
        private static int _seq;

        private static double ToDouble(JToken t)
        {
            if (t == null || t.Type == JTokenType.Null) return 0.0;
            try { return t.Value<double>(); } catch { return 0.0; }
        }
    }
}
