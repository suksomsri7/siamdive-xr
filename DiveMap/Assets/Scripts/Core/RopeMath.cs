using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace DiveMap.Core
{
    /// <summary>One end of a rope: an item, and a point in THAT ITEM's local space.</summary>
    public struct RopeEnd
    {
        public string ItemId;      // the web's `mid`
        public double Lx, Ly, Lz;  // the web's `lp` — local, so the rope follows the object
    }

    /// <summary>A rope as stored in <c>env.ropes</c>.</summary>
    public sealed class Rope
    {
        public string Id;
        public RopeEnd A, B;
        public double Sag = 8.0;
        public string Color = RopeMath.DefaultColor;
        public double Thick = RopeMath.DefaultThick;
    }

    /// <summary>
    /// Ropes strung between two objects — builder.html:3200-3216.
    ///
    /// 🔎 The web's function is called <c>_catenaryPts</c> and its own comment admits what it
    /// really is: <c>"parabola droop (peak sag ที่กลางเส้น)"</c>. A true catenary is a cosh
    /// curve; this is <c>y -= sag·4·t·(1−t)</c>, a parabola. Ported as written, not as named —
    /// swapping in real catenary maths would make every existing rope in every saved map change
    /// shape on first load.
    ///
    /// Anchors are stored in the OBJECT's local space, which is what makes a rope follow its
    /// object when that object is moved, rotated or scaled. Storing world points would have been
    /// simpler and would have quietly detached every rope the first time anyone used the gizmo.
    /// </summary>
    public static class RopeMath
    {
        /// <summary>builder.html:3203 — ROPE_COLOR 0x6b5836, ROPE_THICK 0.55.</summary>
        public const string DefaultColor = "#6b5836";
        public const double DefaultThick = 0.55;

        /// <summary>Samples along the curve. The web uses N=24 (25 points).</summary>
        public const int Samples = 24;

        /// <summary>builder.html:3218 ROPE_COLORS — the seven the panel offers.</summary>
        public static readonly string[] Colors =
        {
            "#6b5836", "#3a3a3a", "#ded2b0", "#ffffff", "#b03a2e", "#1c6ea4", "#ffc62e",
        };

        /// <summary>
        /// Sag a new rope gets: <c>max(6, distance × 0.16)</c> (builder.html:3231). Proportional,
        /// so a short rope between two nearby rocks does not hang to the seabed.
        /// </summary>
        public static double DefaultSagFor(double distance) => Math.Max(6.0, distance * 0.16);

        /// <summary>
        /// The droop curve. <paramref name="t"/> 0..1 along the straight line; the return is how
        /// far DOWN the rope hangs at that point. Zero at both ends, <c>sag</c> at the middle.
        /// </summary>
        public static double DroopAt(double t, double sag) => sag * 4.0 * t * (1.0 - t);

        /// <summary>
        /// Sampled rope, world space. <paramref name="into"/> is filled with
        /// <see cref="Samples"/>+1 points; reused across frames so a rope that follows a dragged
        /// object does not allocate every frame.
        /// </summary>
        public static void Curve(double ax, double ay, double az,
                                 double bx, double by, double bz,
                                 double sag, List<double[]> into, int samples = Samples)
        {
            if (into == null) return;
            into.Clear();
            if (samples < 2) samples = 2;

            for (int i = 0; i <= samples; i++)
            {
                double t = i / (double)samples;
                into.Add(new[]
                {
                    ax + (bx - ax) * t,
                    ay + (by - ay) * t - DroopAt(t, sag),
                    az + (bz - az) * t,
                });
            }
        }

        /// <summary>Straight-line distance between two ends, for the default sag.</summary>
        public static double Distance(double ax, double ay, double az,
                                      double bx, double by, double bz)
        {
            double dx = bx - ax, dy = by - ay, dz = bz - az;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        // ── storage ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Read <c>env.ropes</c>. Rows missing either end are skipped rather than thrown on —
        /// the web does the same (<c>if(!d||!d.a||!d.b)continue</c>), because a rope whose object
        /// was deleted is data that legitimately exists in old maps.
        /// </summary>
        public static List<Rope> Parse(JArray arr)
        {
            var list = new List<Rope>();
            if (arr == null) return list;

            foreach (JToken t in arr)
            {
                if (!(t is JObject o)) continue;
                if (!(o["a"] is JObject a) || !(o["b"] is JObject b)) continue;

                var rope = new Rope
                {
                    Id = (string)o["id"] ?? NewId(list.Count),
                    A = ReadEnd(a),
                    B = ReadEnd(b),
                    Sag = ReadD(o["sag"], 8.0),
                    Color = (string)o["color"] ?? DefaultColor,
                    Thick = ReadD(o["thick"], DefaultThick),
                };
                if (string.IsNullOrEmpty(rope.A.ItemId) || string.IsNullOrEmpty(rope.B.ItemId)) continue;
                list.Add(rope);
            }
            return list;
        }

        public static JArray Serialise(IEnumerable<Rope> ropes)
        {
            var arr = new JArray();
            if (ropes == null) return arr;

            foreach (Rope r in ropes)
            {
                if (r == null) continue;
                arr.Add(new JObject
                {
                    ["id"] = r.Id,
                    ["a"] = WriteEnd(r.A),
                    ["b"] = WriteEnd(r.B),
                    ["sag"] = r.Sag,
                    ["color"] = r.Color ?? DefaultColor,
                    ["thick"] = r.Thick,
                });
            }
            return arr;
        }

        /// <summary>
        /// Drop every rope attached to a deleted object (the web's <c>removeRopesForMid</c>).
        /// Returns how many went. Without this a rope stays in the JSON pointing at nothing and
        /// silently vanishes from the scene — present in the data, absent on screen.
        /// </summary>
        public static int DetachFrom(List<Rope> ropes, string itemId)
        {
            if (ropes == null || string.IsNullOrEmpty(itemId)) return 0;
            int before = ropes.Count;
            ropes.RemoveAll(r => r != null &&
                                 (string.Equals(r.A.ItemId, itemId, StringComparison.Ordinal) ||
                                  string.Equals(r.B.ItemId, itemId, StringComparison.Ordinal)));
            return before - ropes.Count;
        }

        /// <summary>A rope id. Same shape as the item ids so both read alike in a log.</summary>
        public static string NewId(int seq) => "rope" + seq.ToString("x") + "_" + (_seq++).ToString("x");
        private static int _seq;

        /// <summary>#rrggbb → the colour the panel should show as selected, or the default.</summary>
        public static string NormaliseColor(string hex)
            => SceneEdit.IsHexColor(hex) ? hex.ToLowerInvariant() : DefaultColor;

        private static RopeEnd ReadEnd(JObject o)
        {
            var end = new RopeEnd { ItemId = (string)o["mid"] };
            if (o["lp"] is JArray lp && lp.Count >= 3)
            {
                end.Lx = ReadD(lp[0], 0);
                end.Ly = ReadD(lp[1], 0);
                end.Lz = ReadD(lp[2], 0);
            }
            return end;
        }

        private static JObject WriteEnd(RopeEnd e) => new JObject
        {
            ["mid"] = e.ItemId,
            ["lp"] = new JArray(e.Lx, e.Ly, e.Lz),
        };

        private static double ReadD(JToken t, double fallback)
        {
            if (t == null || t.Type == JTokenType.Null) return fallback;
            try { return t.Value<double>(); } catch { return fallback; }
        }
    }
}
