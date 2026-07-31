using System;
using System.Collections.Generic;

namespace DiveMap.Core
{
    /// <summary>One row the palette can show. Neutral — Core cannot see Runtime's manifest type.</summary>
    public sealed class PaletteSource
    {
        public string Id;
        public string Kind;
        public string Name;
        public bool HasGlb;
    }

    /// <summary>A palette card, ready to draw.</summary>
    public sealed class PaletteItem
    {
        public string Id;
        public string Kind;      // folded display kind
        public string Name;
        public string ThumbUrl;  // null when the module has no GLB (no thumbnail was ever rendered)
        public int Price;        // 0 when free scenery
        public bool Buyable;
    }

    /// <summary>
    /// The builder's object palette — which on the web IS the shop: placing an object is the
    /// purchase (<c>tryPlace()</c> :4298 deducts coins, then calls <c>addAsset</c>). The
    /// separate <c>openShop()</c> list is a secondary path most users never open.
    ///
    /// Ported from builder.html:
    /// <code>
    ///   KIND_ORDER  :1039   the chip order
    ///   KIND_META   :1037   emoji + Thai label per chip
    ///   FOLD        :1322   ANEMONE/PLANT→CORAL · TURTLE→MARINE_LIFE · FISH→SCHOOL (fish:3→MARINE_LIFE)
    ///   BUILDABLE   :734    procedural placeholders — dropped once the real asset list loads
    ///   PALETTE_HIDE:2678   ids the owner pulled from the menu (still loadable in old maps)
    ///   showVariants:2684   MARINE_LIFE/SCHOOL sorted cheapest→dearest · 🪙price badge on buyables
    ///   thumbnail   :2694   models/thumbs/{id with ':'→'_'}.png — ALREADY RENDERED server-side
    /// </code>
    ///
    /// 🔑 The thumbnails are pre-rendered PNGs on the server (verified live: 200, 3-7 KB each).
    /// The plan note that called "render a 3D thumbnail from the GLB" the hard part of this
    /// screen was wrong — the web never does that, and neither do we.
    /// </summary>
    public static class Palette
    {
        // ── chips ────────────────────────────────────────────────────────────────
        public const string Rock = "ROCK";
        public const string Coral = "CORAL";
        public const string Boat = "BOAT";
        public const string MarineLife = "MARINE_LIFE";
        public const string School = "SCHOOL";
        public const string Artificial = "ARTIFICIAL";
        /// <summary>The web labels this one ✨ "พิเศษ" — SPECIAL is the admin-only warp category.</summary>
        public const string Wreck = "WRECK";
        public const string Diver = "DIVER";
        public const string Special = "SPECIAL";

        /// <summary>Chip order (builder.html:1039).</summary>
        public static readonly string[] KindOrder =
            { Rock, Coral, Boat, MarineLife, School, Artificial, Wreck, Diver, Special };

        /// <summary>Thai label per chip (builder.html:1037 KIND_META).</summary>
        public static string LabelOf(string kind)
        {
            switch (kind)
            {
                case Rock:       return "หิน";
                case Coral:      return "ปะการัง";
                case Boat:       return "เรือ";
                case MarineLife: return "สัตว์ทะเล";
                case School:     return "ฝูงปลา";
                case Artificial: return "ปะการังเทียม";
                case Wreck:      return "พิเศษ";
                case Diver:      return "นักดำน้ำ";
                case Special:    return "ประตูวาป";
                default:         return kind ?? "";
            }
        }

        /// <summary>
        /// The <c>IconPainter</c> name standing in for the web's emoji (Core cannot reference
        /// Runtime, so this is a string). NotoSansThai has no emoji coverage, which is why every
        /// glyph in this app is drawn — same rule as the ♻️ trash badge and the game HUD.
        /// </summary>
        public static string IconOf(string kind)
        {
            switch (kind)
            {
                case Rock:       return "rock";
                case Coral:      return "coral";
                case Boat:       return "boat";
                case MarineLife: return "turtle";
                case School:     return "fish";
                case Artificial: return "moai";
                case Wreck:      return "sparkle";
                case Diver:      return "mask";
                case Special:    return "radar";
                default:         return "list";
            }
        }

        // ── the three non-asset chips the web appends after the kinds ────────────
        public const string PinTool = "tool:pin";
        public const string SettingsTool = "tool:setting";
        public const string SculptTool = "tool:sculpt";

        public static string ToolLabel(string tool)
        {
            switch (tool)
            {
                case PinTool:      return "ปักหมุด";
                case SettingsTool: return "ตั้งค่า";
                case SculptTool:   return "ปั้นพื้น";
                default:           return "";
            }
        }

        public static string ToolIcon(string tool)
        {
            switch (tool)
            {
                case PinTool:      return "pin";
                case SettingsTool: return "gear";
                case SculptTool:   return "mountain";
                default:           return "list";
            }
        }

        // ── filtering ────────────────────────────────────────────────────────────

        /// <summary>
        /// Id prefixes with procedural geometry (builder.html:734). The web drops them from the
        /// palette as soon as the real asset list loads — the CC0 models replace them — but keeps
        /// them resolvable so maps that already placed one still load.
        /// </summary>
        private static readonly HashSet<string> Buildable = new HashSet<string>(StringComparer.Ordinal)
            { "rock", "coral", "anemone", "fish", "turtle", "wreck", "warp" };

        /// <summary>ids pulled from the menu by the owner (builder.html:2678).</summary>
        private static readonly HashSet<string> Hidden = new HashSet<string>(StringComparer.Ordinal)
        {
            "wreck:0", "wreck:1",
            "cc0:wreck_ship", "cc0:wreck_anchor", "cc0:wreck_airplane", "cc0:wreck_moto",
            "cc0:wreck_car", "cc0:wreck_barrel", "cc0:wreck_truck", "cc0:wreck_tire",
            "cc0:wreck_container",
            "nat:tree", "nat:pine", "nat:palm", "nat:palms", "nat:mountain", "nat:peak",
            "nat:bush", "nat:grass", "nat:terrain",
            "glb_turtle_loggerhead",
        };

        public static bool IsHidden(string id) => id != null && Hidden.Contains(id);

        /// <summary>True for the crude procedural placeholders the palette no longer offers.</summary>
        public static bool IsProcedural(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            int i = id.IndexOf(':');
            return i > 0 && Buildable.Contains(id.Substring(0, i));
        }

        /// <summary>
        /// Legacy kinds folded into the display categories (builder.html:1322), so an old row
        /// never vanishes just because the enum was reorganised. <c>fish:3</c> is the one big
        /// procedural fish and folds the other way.
        /// </summary>
        public static string FoldKind(string kind, string id)
        {
            if (kind == "FISH" && string.Equals(id, "fish:3", StringComparison.Ordinal)) return MarineLife;
            switch (kind)
            {
                case "ANEMONE":
                case "PLANT":   return Coral;
                case "TURTLE":  return MarineLife;
                case "FISH":    return School;
                default:        return kind;
            }
        }

        /// <summary>Pre-rendered palette thumbnail, or null when the module has no model.</summary>
        public static string ThumbUrl(string id, bool hasGlb, string baseUrl)
        {
            if (!hasGlb || string.IsNullOrEmpty(id)) return null;
            string b = (baseUrl ?? "").TrimEnd('/');
            return b + "/models/thumbs/" + id.Replace(':', '_') + ".png";
        }

        // ── build ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Group the asset registry into the palette's display categories, in chip order.
        /// Animals and schools come out cheapest-first, like <c>showVariants</c>.
        /// </summary>
        public static Dictionary<string, List<PaletteItem>> Build(IEnumerable<PaletteSource> sources,
                                                                  string baseUrl)
        {
            var byKind = new Dictionary<string, List<PaletteItem>>(StringComparer.Ordinal);
            if (sources == null) return byKind;

            foreach (PaletteSource s in sources)
            {
                if (s == null || string.IsNullOrEmpty(s.Id)) continue;
                if (IsProcedural(s.Id) || IsHidden(s.Id)) continue;

                string kind = FoldKind(s.Kind, s.Id);
                // The admin-only warp category has no place in a player build.
                if (kind == Special) continue;

                if (!byKind.TryGetValue(kind, out List<PaletteItem> list))
                {
                    list = new List<PaletteItem>();
                    byKind[kind] = list;
                }

                bool buyable = Shop.IsBuyable(s.Id);
                list.Add(new PaletteItem
                {
                    Id = s.Id,
                    Kind = kind,
                    Name = string.IsNullOrWhiteSpace(s.Name) ? s.Id : s.Name.Trim(),
                    ThumbUrl = ThumbUrl(s.Id, s.HasGlb, baseUrl),
                    Price = buyable ? Shop.PriceOf(s.Id) : 0,
                    Buyable = buyable,
                });
            }

            foreach (KeyValuePair<string, List<PaletteItem>> kv in byKind)
            {
                if (kv.Key != MarineLife && kv.Key != School) continue;
                // OrderBy would be stable but allocates; List.Sort is not stable, so tie-break on
                // id to keep the order identical between runs (QC screenshots must be comparable).
                kv.Value.Sort((a, b) =>
                {
                    int c = a.Price.CompareTo(b.Price);
                    return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
                });
            }

            return byKind;
        }

        /// <summary>Chips to draw: the kinds that actually have items, in <see cref="KindOrder"/>.</summary>
        public static List<string> ChipKinds(Dictionary<string, List<PaletteItem>> byKind)
        {
            var chips = new List<string>();
            if (byKind == null) return chips;
            foreach (string kind in KindOrder)
                if (byKind.TryGetValue(kind, out List<PaletteItem> list) && list.Count > 0)
                    chips.Add(kind);
            return chips;
        }
    }
}
