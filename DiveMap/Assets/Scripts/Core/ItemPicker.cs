using System;
using System.Collections.Generic;
using UnityEngine;

namespace DiveMap.Core
{
    /// <summary>
    /// Pure picking / labelling logic for the scene info card (WO-XR-05.3).
    ///
    /// Everything here is deliberately free of scene lookups so it can be unit-tested:
    /// the runtime side (<c>InfoCardController</c>) only collects world-space AABBs from
    /// the live "Map" hierarchy and hands them over.
    ///
    /// Why AABBs instead of colliders: <c>SceneBuilder</c> attaches a MeshCollider to the
    /// seabed only, and adding one per item would mean editing SceneBuilder (owned by
    /// another work order) and paying for collider cooking on every GLB. A ray/AABB slab
    /// test over ~15 items costs nothing and touches no other file.
    /// </summary>
    public static class ItemPicker
    {
        /// <summary>Prefix SceneBuilder gives every placed item GameObject: <c>Item_{id}_{assetId}</c>.</summary>
        public const string ItemPrefix = "Item_";

        /// <summary>
        /// Scene units per metre. Source of truth: builder.html L600
        /// <c>const U_PER_M = 6;</c> — the same constant its <c>depthMetres()</c>
        /// (L601) uses. NEVER guess this number; the whole depth readout scales by it.
        /// </summary>
        public const double UnitsPerMetre = 6.0;

        /// <summary>builder.html L601 clamps the depth readout to 0..100 m — mirrored here.</summary>
        public const double MaxDepthMetres = 100.0;

        /// <summary>One pickable object: a key (the GameObject name) plus its world AABB.</summary>
        public readonly struct Target
        {
            public readonly string Key;
            public readonly Vector3 Min;
            public readonly Vector3 Max;

            public Target(string key, Vector3 min, Vector3 max)
            {
                Key = key;
                Min = min;
                Max = max;
            }

            /// <summary>Axis-aligned box around a point — the fallback for renderer-less items.</summary>
            public static Target Sphere(string key, Vector3 centre, float radius)
            {
                var r = new Vector3(radius, radius, radius);
                return new Target(key, centre - r, centre + r);
            }
        }

        // ── name parsing ─────────────────────────────────────────────────────────

        /// <summary>
        /// Split <c>Item_{id}_{assetId}</c>.
        ///
        /// The id is a mid ("mef2q6k18z591") and never contains an underscore, but the
        /// assetId very much does — "cc0:wreck_chang", "school:scad", "msh:whaleshark".
        /// So we strip the prefix and split on the FIRST underscore only; everything
        /// after it (underscores, colons and all) is the assetId.
        /// </summary>
        public static bool ParseItemName(string goName, out string id, out string assetId)
        {
            id = null;
            assetId = null;
            if (string.IsNullOrEmpty(goName)) return false;
            if (!goName.StartsWith(ItemPrefix, StringComparison.Ordinal)) return false;

            string rest = goName.Substring(ItemPrefix.Length);
            int cut = rest.IndexOf('_');
            if (cut <= 0 || cut >= rest.Length - 1) return false;

            id = rest.Substring(0, cut);
            assetId = rest.Substring(cut + 1);
            return true;
        }

        public static bool IsItemName(string goName)
            => !string.IsNullOrEmpty(goName) && goName.StartsWith(ItemPrefix, StringComparison.Ordinal);

        // ── depth ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Metres of water above a point, exactly as builder.html L601:
        /// <c>depthMetres(topY) = clamp((waterLevel - topY) / U_PER_M, 0, 100)</c>.
        /// Both spaces share the Y axis (WebCoord only flips Z), so a Unity world Y can
        /// be fed in unchanged.
        /// </summary>
        public static double DepthMetres(double waterLevel, double y)
        {
            double d = (waterLevel - y) / UnitsPerMetre;
            if (d < 0.0) return 0.0;
            if (d > MaxDepthMetres) return MaxDepthMetres;
            return d;
        }

        // ── ray / AABB ───────────────────────────────────────────────────────────

        /// <summary>
        /// Slab test. <paramref name="distance"/> is the entry distance in multiples of
        /// <paramref name="dir"/> (0 when the origin is already inside the box).
        /// Boxes entirely behind the origin do not count as a hit.
        /// </summary>
        public static bool RayAabb(Vector3 origin, Vector3 dir, Vector3 min, Vector3 max,
                                   out float distance)
        {
            distance = 0f;
            float tMin = 0f;
            float tMax = float.PositiveInfinity;

            for (int axis = 0; axis < 3; axis++)
            {
                float o = Axis(origin, axis);
                float d = Axis(dir, axis);
                float lo = Axis(min, axis);
                float hi = Axis(max, axis);
                if (lo > hi) { float sw = lo; lo = hi; hi = sw; }

                // Parallel to this slab: only a hit if the origin already lies inside it.
                // (Explicit branch — 0 * infinity would otherwise produce NaN.)
                if (d > -1e-8f && d < 1e-8f)
                {
                    if (o < lo || o > hi) return false;
                    continue;
                }

                float inv = 1f / d;
                float t1 = (lo - o) * inv;
                float t2 = (hi - o) * inv;
                if (t1 > t2) { float sw = t1; t1 = t2; t2 = sw; }

                if (t1 > tMin) tMin = t1;
                if (t2 < tMax) tMax = t2;
                if (tMin > tMax) return false;
            }

            distance = tMin;
            return true;
        }

        /// <summary>Key of the nearest AABB the ray enters, or null when it hits nothing.</summary>
        public static string Pick(Vector3 rayOrigin, Vector3 rayDir, IEnumerable<Target> targets)
            => Pick(rayOrigin, rayDir, targets, out _);

        /// <summary>
        /// Pick with an occlusion limit: hits beyond <paramref name="maxDistance"/> are ignored.
        /// The caller passes the seabed's own ray hit — the user's report was literal: tapping
        /// SAND popped a fish card, because a school's pick-sphere floats mid-water on the way
        /// to the sand and nothing ever said "the sand is closer".
        /// </summary>
        public static string Pick(Vector3 rayOrigin, Vector3 rayDir, IEnumerable<Target> targets,
                                  float maxDistance)
            => Pick(rayOrigin, rayDir, targets, out _, maxDistance);

        public static string Pick(Vector3 rayOrigin, Vector3 rayDir, IEnumerable<Target> targets,
                                  out float distance, float maxDistance = float.PositiveInfinity)
        {
            distance = 0f;
            if (targets == null) return null;

            string best = null;
            float bestT = float.PositiveInfinity;

            foreach (Target t in targets)
            {
                if (string.IsNullOrEmpty(t.Key)) continue;
                if (!RayAabb(rayOrigin, rayDir, t.Min, t.Max, out float hit)) continue;
                if (hit > maxDistance) continue;   // ของที่อยู่เลยพื้น/ฉากบัง = มองไม่เห็น = คลิกไม่ได้
                if (hit < bestT)
                {
                    bestT = hit;
                    best = t.Key;
                }
            }

            if (best != null) distance = bestT;
            return best;
        }

        private static float Axis(Vector3 v, int axis) => axis == 0 ? v.x : (axis == 1 ? v.y : v.z);

        // ── kind → Thai label ────────────────────────────────────────────────────

        // Ported from builder.html KIND_META (L1037-1038): the exact wording the web
        // shows for each palette group. TERRAIN is the one deviation — the web says
        // "พื้น", but that English value ("Terrain") is already taken by the asset name
        // "ภูมิประเทศ", and UiStringsTests forbids duplicate English values. Same meaning.
        private static readonly Dictionary<string, string> KindThai =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "ROCK",        "หิน" },
                { "CORAL",       "ปะการัง" },
                { "BOAT",        "เรือ" },
                { "MARINE_LIFE", "สัตว์ทะเล" },
                { "SCHOOL",      "ฝูงปลา" },
                { "ARTIFICIAL",  "ปะการังเทียม" },
                { "WRECK",       "พิเศษ" },   // the web's catch-all "✨ Special" tab
                { "DIVER",       "นักดำน้ำ" },
                { "SPECIAL",     "ประตูวาป" },
                { "ANEMONE",     "ดอกไม้ทะเล" },
                { "FISH",        "ปลา" },
                { "TURTLE",      "เต่า" },
                { "PLANT",       "พืช" },
                { "TERRAIN",     "ภูมิประเทศ" },
                { "OTHER",       "อื่นๆ" },
            };

        /// <summary>
        /// Thai label for the info card's "type" row — the SOURCE string, i.e. a key for
        /// <see cref="UiStrings.Tr"/>.
        ///
        /// The assetId wins over the manifest kind where it is strictly more informative:
        /// the web files trees, statues AND shipwrecks under kind WRECK (its "✨ Special"
        /// tab), so a wreck would otherwise read "พิเศษ". Schools/pods carry no manifest
        /// entry at all, so their kind can only come from the id.
        /// </summary>
        public static string KindLabel(string kind, string assetId)
        {
            string a = string.IsNullOrEmpty(assetId) ? "" : assetId.ToLowerInvariant();

            if (a.StartsWith("school:", StringComparison.Ordinal) ||
                a.StartsWith("pod:", StringComparison.Ordinal)) return "ฝูงปลา";
            if (a.StartsWith("warp:", StringComparison.Ordinal)) return "ประตูวาป";
            if (a.StartsWith("nat:", StringComparison.Ordinal)) return "พืช";
            if (a.Contains("wreck")) return "ซากเรือ";

            if (!string.IsNullOrEmpty(kind) && KindThai.TryGetValue(kind, out string label))
                return label;

            return "อื่นๆ";
        }
    }
}
