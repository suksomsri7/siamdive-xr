using System.Collections.Generic;
using UnityEngine;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// Draws the web's icon set into sprites at runtime. The web's chrome is stroke SVG icons
    /// (24×24 viewBox, stroke #fff, width ~2.2, round caps) and the app has to look like the same
    /// product — but there is no Unity Editor here to import SVGs or bake an atlas, and glyph
    /// fonts have no guaranteed coverage (NotoSansThai has no ☰, which is why the current
    /// hamburger is three Image bars).
    ///
    /// So: the same 24-unit coordinates as the web's <c>&lt;path&gt;</c> data, rasterised with a
    /// distance-to-segment antialias. One 96×96 texture per icon, cached for the process.
    /// </summary>
    public static class IconPainter
    {
        public const int Size = 96;
        private const float Space = 24f;                 // the web's viewBox
        private const float Scale = Size / Space;

        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        /// <summary>Icon by web name: menu, close, back, wave, sun, lamp, sound, mute, camera, play, exit, compass.</summary>
        public static Sprite Get(string name)
        {
            if (Cache.TryGetValue(name, out Sprite s) && s != null) return s;
            Sprite made = Build(name);
            Cache[name] = made;
            return made;
        }

        private static Sprite Build(string name)
        {
            var strokes = new List<Vector2[]>();
            var fills = new List<Vector2[]>();
            float width = 2.2f;

            switch (name)
            {
                case "menu":   // SVG.menu — M4 7h16 M4 12h16 M4 17h16
                    strokes.Add(Line(4, 7, 20, 7));
                    strokes.Add(Line(4, 12, 20, 12));
                    strokes.Add(Line(4, 17, 20, 17));
                    break;

                case "close":  // SVG.close — M6 6l12 12 M18 6 6 18
                    strokes.Add(Line(6, 6, 18, 18));
                    strokes.Add(Line(18, 6, 6, 18));
                    break;

                case "back":   // #backBtn — m15 5-7 7 7 7
                    strokes.Add(new[] { P(15, 5), P(8, 12), P(15, 19) });
                    break;

                case "wave":   // SVG.wave — two sine rows (water mode)
                    strokes.Add(Wave(9f));
                    strokes.Add(Wave(15f));
                    break;

                case "sun":    // SVG.sun — circle r4 + 8 rays
                    strokes.Add(Circle(12f, 12f, 4f));
                    strokes.Add(Line(12, 2, 12, 4));
                    strokes.Add(Line(12, 20, 12, 22));
                    strokes.Add(Line(2, 12, 4, 12));
                    strokes.Add(Line(20, 12, 22, 12));
                    strokes.Add(Line(5, 5, 6.4f, 6.4f));
                    strokes.Add(Line(17.6f, 17.6f, 19, 19));
                    strokes.Add(Line(19, 5, 17.6f, 6.4f));
                    strokes.Add(Line(6.4f, 17.6f, 5, 19));
                    break;

                case "lamp":   // headlamp: a bulb throwing a cone forward
                    strokes.Add(Circle(8f, 12f, 3.4f));
                    strokes.Add(new[] { P(11.4f, 9.5f), P(21, 5.5f) });
                    strokes.Add(new[] { P(11.4f, 14.5f), P(21, 18.5f) });
                    strokes.Add(Line(12.5f, 12, 19, 12));
                    break;

                case "sound":  // speaker + two arcs
                    strokes.Add(new[] { P(4, 9.5f), P(7.5f, 9.5f), P(11, 6), P(11, 18), P(7.5f, 14.5f), P(4, 14.5f), P(4, 9.5f) });
                    strokes.Add(Arc(11.5f, 12f, 4.2f, -55f, 55f));
                    strokes.Add(Arc(11.5f, 12f, 7.2f, -55f, 55f));
                    break;

                case "mute":   // speaker + X
                    strokes.Add(new[] { P(4, 9.5f), P(7.5f, 9.5f), P(11, 6), P(11, 18), P(7.5f, 14.5f), P(4, 14.5f), P(4, 9.5f) });
                    strokes.Add(Line(15, 9.5f, 20.5f, 15));
                    strokes.Add(Line(20.5f, 9.5f, 15, 15));
                    break;

                case "cart":   // 🛒 shop — basket, handle and two wheels
                    strokes.Add(new[] { P(3, 6), P(6, 6), P(8.4f, 15), P(19, 15), P(21, 8.5f), P(7, 8.5f) });
                    strokes.Add(Circle(10f, 18.6f, 1.5f));
                    strokes.Add(Circle(17.6f, 18.6f, 1.5f));
                    break;

                case "radar":  // #radarBtn — <circle r9/><circle r4.3/><path d="M12 12 17.5 8.5"/>
                    strokes.Add(Circle(12f, 12f, 9f));
                    strokes.Add(Circle(12f, 12f, 4.3f));
                    strokes.Add(Line(12, 12, 17.5f, 8.5f));
                    break;

                case "camera": // photo
                    strokes.Add(new[] { P(3.5f, 8.5f), P(8f, 8.5f), P(9.5f, 6f), P(14.5f, 6f), P(16f, 8.5f), P(20.5f, 8.5f), P(20.5f, 19f), P(3.5f, 19f), P(3.5f, 8.5f) });
                    strokes.Add(Circle(12f, 13.5f, 3.6f));
                    break;

                case "ar":     // AR — viewfinder corners around a small cube on a surface
                    strokes.Add(new[] { P(3.2f, 8f), P(3.2f, 4.6f), P(6.6f, 4.6f) });      // ┌
                    strokes.Add(new[] { P(17.4f, 4.6f), P(20.8f, 4.6f), P(20.8f, 8f) });   // ┐
                    strokes.Add(new[] { P(20.8f, 16f), P(20.8f, 19.4f), P(17.4f, 19.4f) });// ┘
                    strokes.Add(new[] { P(6.6f, 19.4f), P(3.2f, 19.4f), P(3.2f, 16f) });   // └
                    // the cube: a top face and two sides, i.e. an object standing in the frame
                    strokes.Add(new[] { P(12f, 8.2f), P(16.2f, 10.4f), P(12f, 12.6f), P(7.8f, 10.4f), P(12f, 8.2f) });
                    strokes.Add(new[] { P(7.8f, 10.4f), P(7.8f, 14.4f), P(12f, 16.6f),
                                        P(16.2f, 14.4f), P(16.2f, 10.4f) });
                    strokes.Add(Line(12f, 12.6f, 12f, 16.6f));
                    break;

                case "play":   // #playBtn — filled triangle 7 4 / 20 12 / 7 20
                    fills.Add(new[] { P(7, 4), P(20, 12), P(7, 20) });
                    break;

                case "exit":   // door + arrow out
                    strokes.Add(new[] { P(13, 4), P(5, 4), P(5, 20), P(13, 20) });
                    strokes.Add(Line(10, 12, 20, 12));
                    strokes.Add(new[] { P(17, 9), P(20, 12), P(17, 15) });
                    break;

                case "list":   // map list — three rows with bullets
                    strokes.Add(Line(9, 7, 20, 7));
                    strokes.Add(Line(9, 12, 20, 12));
                    strokes.Add(Line(9, 17, 20, 17));
                    fills.Add(Dot(5f, 7f, 1.4f));
                    fills.Add(Dot(5f, 12f, 1.4f));
                    fills.Add(Dot(5f, 17f, 1.4f));
                    break;

                case "mask":   // dive tour — the web uses a mask image (ai-mask.png)
                    strokes.Add(new[] { P(4, 9), P(4, 15.5f), P(8, 18.5f), P(16, 18.5f), P(20, 15.5f), P(20, 9), P(4, 9) });
                    strokes.Add(Circle(9f, 13f, 2.2f));
                    strokes.Add(Circle(15f, 13f, 2.2f));
                    strokes.Add(Line(4, 9, 20, 9));
                    break;

                case "gear":   // settings
                    strokes.Add(Circle(12f, 12f, 3.2f));
                    strokes.Add(Circle(12f, 12f, 7.4f));
                    for (int k = 0; k < 8; k++)
                    {
                        float a2 = Mathf.PI * 2f * k / 8f;
                        strokes.Add(new[]
                        {
                            new Vector2(12f + Mathf.Cos(a2) * 7.4f, 12f + Mathf.Sin(a2) * 7.4f),
                            new Vector2(12f + Mathf.Cos(a2) * 9.6f, 12f + Mathf.Sin(a2) * 9.6f),
                        });
                    }
                    break;

                case "depth":  // depth heat-map: layered contours
                    strokes.Add(new[] { P(3.5f, 8f), P(12f, 4f), P(20.5f, 8f), P(12f, 12f), P(3.5f, 8f) });
                    strokes.Add(new[] { P(3.5f, 13f), P(12f, 17f), P(20.5f, 13f) });
                    strokes.Add(new[] { P(3.5f, 17.5f), P(12f, 21.5f), P(20.5f, 17.5f) });
                    break;

                // ── selection toolbar (#seltool :318) — ✥ ⟳ ⤢ 🎨 ⧉ 🗑 ✓ ↺ ──

                case "move":     // ✥ — four-way arrows
                    strokes.Add(Line(12, 3.2f, 12, 20.8f));
                    strokes.Add(Line(3.2f, 12, 20.8f, 12));
                    strokes.Add(new[] { P(9.2f, 6f), P(12f, 3.2f), P(14.8f, 6f) });
                    strokes.Add(new[] { P(9.2f, 18f), P(12f, 20.8f), P(14.8f, 18f) });
                    strokes.Add(new[] { P(6f, 9.2f), P(3.2f, 12f), P(6f, 14.8f) });
                    strokes.Add(new[] { P(18f, 9.2f), P(20.8f, 12f), P(18f, 14.8f) });
                    break;

                case "rotate":   // ⟳ — an almost-closed ring with an arrow head
                    strokes.Add(Arc(12f, 12f, 7.6f, -60f, 250f));
                    strokes.Add(new[] { P(12.6f, 1.4f), P(15.8f, 4.2f), P(12.2f, 6.6f) });
                    break;

                case "resize":   // ⤢ — a diagonal with heads at both ends
                    strokes.Add(Line(5f, 19f, 19f, 5f));
                    strokes.Add(new[] { P(13.4f, 5f), P(19f, 5f), P(19f, 10.6f) });
                    strokes.Add(new[] { P(10.6f, 19f), P(5f, 19f), P(5f, 13.4f) });
                    break;

                case "palette":  // 🎨 — a palette blob with three wells
                    strokes.Add(Blob(new Vector2(11.6f, 12f), PaletteInside));
                    fills.Add(Dot(8f, 8.6f, 1.35f));
                    fills.Add(Dot(13.4f, 7.2f, 1.35f));
                    fills.Add(Dot(16.6f, 11f, 1.35f));
                    break;

                case "copy":     // ⧉ — two offset rounded squares
                    strokes.Add(new[] { P(8.6f, 3.6f), P(20.4f, 3.6f), P(20.4f, 15.4f), P(8.6f, 15.4f), P(8.6f, 3.6f) });
                    strokes.Add(new[] { P(15.4f, 8.6f), P(15.4f, 20.4f), P(3.6f, 20.4f), P(3.6f, 8.6f), P(8.6f, 8.6f) });
                    break;

                case "trash":    // 🗑 — lid, can, two ribs
                    strokes.Add(Line(3.6f, 6.4f, 20.4f, 6.4f));
                    strokes.Add(new[] { P(9f, 6.4f), P(9f, 3.6f), P(15f, 3.6f), P(15f, 6.4f) });
                    strokes.Add(new[] { P(5.8f, 6.4f), P(7f, 20.6f), P(17f, 20.6f), P(18.2f, 6.4f) });
                    strokes.Add(Line(10f, 10f, 10.4f, 17.4f));
                    strokes.Add(Line(14f, 10f, 13.6f, 17.4f));
                    break;

                case "pencil":   // ✎ — rename / edit
                    strokes.Add(new[] { P(3.8f, 20.2f), P(5.2f, 15.6f), P(16.4f, 4.4f),
                                        P(19.6f, 7.6f), P(8.4f, 18.8f), P(3.8f, 20.2f) });
                    strokes.Add(Line(14.2f, 6.6f, 17.4f, 9.8f));
                    break;

                case "objects":  // 📋 — the object list: a clipboard with rows
                    strokes.Add(new[] { P(4.6f, 5.4f), P(19.4f, 5.4f), P(19.4f, 20.6f), P(4.6f, 20.6f), P(4.6f, 5.4f) });
                    strokes.Add(new[] { P(9f, 5.4f), P(9f, 3.4f), P(15f, 3.4f), P(15f, 5.4f) });
                    strokes.Add(Line(7.8f, 10.2f, 16.2f, 10.2f));
                    strokes.Add(Line(7.8f, 13.6f, 16.2f, 13.6f));
                    strokes.Add(Line(7.8f, 17f, 13f, 17f));
                    break;

                case "history":  // 🕘 — a clock with the hands at "a while ago"
                    strokes.Add(Circle(12f, 12f, 8.4f));
                    strokes.Add(new[] { P(12f, 6.8f), P(12f, 12.4f), P(16.2f, 14.6f) });
                    break;

                case "rope":     // 🪢 — a hanging line with a knot at each end
                    strokes.Add(new[] { P(4.4f, 6.6f), P(7.2f, 14.4f), P(12f, 17.2f),
                                        P(16.8f, 14.4f), P(19.6f, 6.6f) });
                    strokes.Add(Circle(4.4f, 5.2f, 1.8f));
                    strokes.Add(Circle(19.6f, 5.2f, 1.8f));
                    break;

                case "sliders":  // ⚙️ map settings — three sliders with their handles
                    strokes.Add(Line(4, 7, 20, 7));
                    strokes.Add(Line(4, 12, 20, 12));
                    strokes.Add(Line(4, 17, 20, 17));
                    fills.Add(Dot(9f, 7f, 2.2f));
                    fills.Add(Dot(15.5f, 12f, 2.2f));
                    fills.Add(Dot(7.5f, 17f, 2.2f));
                    break;

                case "cloud":    // ☁✓ — "this map is on your device"
                    strokes.Add(Blob(new Vector2(11.5f, 13.5f), CloudInside));
                    strokes.Add(new[] { P(8.8f, 14.2f), P(10.8f, 16.2f), P(15.0f, 11.8f) });
                    break;

                case "check":    // ✓
                    strokes.Add(new[] { P(4.6f, 12.6f), P(9.8f, 18f), P(19.6f, 6.4f) });
                    break;

                case "undo":     // ↺ — the "original colour" reset
                    strokes.Add(Arc(12f, 12.4f, 7.2f, 200f, 500f));
                    strokes.Add(new[] { P(8.2f, 2.2f), P(5.2f, 6.2f), P(9.8f, 7.6f) });
                    break;

                // ── palette chips — the web's emoji, drawn (NotoSansThai has no emoji) ──

                case "rock":     // 🪨 — a faceted boulder
                    strokes.Add(new[] { P(3.2f, 19f), P(5.4f, 11.4f), P(9.6f, 6.6f), P(15.4f, 7.4f),
                                        P(20.4f, 13.2f), P(20.8f, 19f), P(3.2f, 19f) });
                    strokes.Add(new[] { P(9.6f, 6.6f), P(11.2f, 13f), P(20.4f, 13.2f) });
                    strokes.Add(new[] { P(11.2f, 13f), P(8.4f, 19f) });
                    break;

                case "coral":    // 🪸 — a branching stand
                    strokes.Add(new[] { P(12f, 21f), P(12f, 12.4f) });
                    strokes.Add(new[] { P(12f, 15.4f), P(7.6f, 11.2f), P(7.2f, 6.8f) });
                    strokes.Add(new[] { P(12f, 13.6f), P(16.4f, 10.4f), P(17f, 6.2f) });
                    strokes.Add(new[] { P(12f, 12.4f), P(12f, 5.4f) });
                    strokes.Add(new[] { P(9.6f, 12.6f), P(9.2f, 9.2f) });
                    strokes.Add(new[] { P(14.4f, 14.2f), P(14.6f, 11f) });
                    break;

                case "boat":     // ⛵ — hull, mast, sail
                    strokes.Add(new[] { P(3.4f, 15.6f), P(20.6f, 15.6f), P(17.4f, 20f), P(6.6f, 20f), P(3.4f, 15.6f) });
                    strokes.Add(Line(12, 15.6f, 12, 3.4f));
                    fills.Add(new[] { P(12.9f, 4.6f), P(19.4f, 14f), P(12.9f, 14f) });
                    break;

                case "turtle":   // 🐢 — shell, head, four flippers
                    strokes.Add(Circle(12f, 12.6f, 5.2f));
                    strokes.Add(Circle(12f, 12.6f, 2.4f));
                    strokes.Add(Circle(19.4f, 10.4f, 1.9f));                    // head
                    strokes.Add(new[] { P(7.6f, 8.4f), P(5f, 5.8f) });
                    strokes.Add(new[] { P(16.4f, 8.4f), P(19f, 5.8f) });
                    strokes.Add(new[] { P(7.6f, 16.8f), P(5f, 19.4f) });
                    strokes.Add(new[] { P(16.4f, 16.8f), P(19f, 19.4f) });
                    break;

                case "fish":     // 🐟 — body, tail, eye
                    strokes.Add(new[] { P(16.6f, 12f), P(13f, 7.4f), P(7.2f, 7.6f), P(3.6f, 12f),
                                        P(7.2f, 16.4f), P(13f, 16.6f), P(16.6f, 12f) });
                    strokes.Add(new[] { P(16.6f, 12f), P(21f, 8.2f), P(21f, 15.8f), P(16.6f, 12f) });
                    fills.Add(Dot(8.2f, 10.6f, 1.05f));
                    break;

                case "moai":     // 🗿 — the artificial-reef statue head
                    strokes.Add(new[] { P(7.6f, 21f), P(7.6f, 8.6f), P(9.2f, 4.2f), P(14.8f, 4.2f),
                                        P(16.4f, 8.6f), P(16.4f, 21f), P(7.6f, 21f) });
                    strokes.Add(new[] { P(8.2f, 10.2f), P(15.8f, 10.2f) });      // brow
                    strokes.Add(new[] { P(12f, 11.4f), P(12f, 15.4f) });         // nose
                    strokes.Add(new[] { P(10f, 17.6f), P(14f, 17.6f) });         // mouth
                    break;

                case "sparkle":  // ✨ — the web's "Special" chip: one big four-point star + one small
                    fills.Add(Star(10f, 10.4f, 7.2f, 2.3f));
                    fills.Add(Star(17.6f, 17.4f, 4.2f, 1.35f));
                    break;

                case "pin":      // 📍 — teardrop map pin
                    strokes.Add(new[] { P(12f, 21.4f), P(6.2f, 12.4f) });
                    strokes.Add(Arc(12f, 8.8f, 5.2f, 133f, 407f));
                    strokes.Add(new[] { P(17.8f, 12.4f), P(12f, 21.4f) });
                    strokes.Add(Circle(12f, 8.8f, 2.1f));
                    break;

                case "mountain": // 🏔️ — "sculpt floor"
                    strokes.Add(new[] { P(2.4f, 19.4f), P(9f, 8.2f), P(13.2f, 14.4f),
                                        P(15.6f, 11f), P(21.6f, 19.4f), P(2.4f, 19.4f) });
                    strokes.Add(new[] { P(7.2f, 11.2f), P(9f, 12.4f), P(10.8f, 11.2f) });   // snow line
                    break;

                // ── map hub (RN Ionicons: search / add / heart / ellipsis / person / image) ──

                case "search": // Ionicons "search" — lens + handle
                    strokes.Add(Circle(10.4f, 10.4f, 6.4f));
                    strokes.Add(Line(15.1f, 15.1f, 20.4f, 20.4f));
                    break;

                case "plus":   // Ionicons "add"
                    strokes.Add(Line(12, 4.5f, 12, 19.5f));
                    strokes.Add(Line(4.5f, 12, 19.5f, 12));
                    break;

                case "heart":     // Ionicons "heart-outline"
                    strokes.Add(HeartPath());
                    break;

                case "heartfill": // Ionicons "heart" (liked)
                    fills.Add(HeartPath());
                    break;

                case "dots":   // Ionicons "ellipsis-horizontal" — the per-card menu
                    fills.Add(Dot(5.4f, 12f, 1.75f));
                    fills.Add(Dot(12f, 12f, 1.75f));
                    fills.Add(Dot(18.6f, 12f, 1.75f));
                    break;

                case "person": // Ionicons "person-circle-outline" — the signed-out account button
                    strokes.Add(Circle(12f, 12f, 9.2f));
                    strokes.Add(Circle(12f, 9.9f, 3.1f));
                    strokes.Add(Arc(12f, 20.2f, 5.6f, 200f, 340f));
                    break;

                case "image":  // Ionicons "image-outline" — thumbnail placeholder
                    strokes.Add(new[] { P(4, 5), P(20, 5), P(20, 19), P(4, 19), P(4, 5) });
                    strokes.Add(Circle(8.8f, 9.6f, 1.5f));
                    strokes.Add(new[] { P(4.4f, 17.4f), P(9.6f, 11.8f), P(13.2f, 15.6f), P(15.9f, 12.9f), P(19.6f, 17f) });
                    break;

                case "needle": // one half of the compass needle (the caller tints + rotates it)
                    fills.Add(new[] { P(12, 1.6f), P(17.2f, 23f), P(6.8f, 23f) });
                    break;

                case "compass": // #compass — two triangles, north red (colour applied by the caller)
                    fills.Add(new[] { P(12, 2.4f), P(16.2f, 12.6f), P(7.8f, 12.6f) });
                    fills.Add(new[] { P(12, 22.8f), P(16.2f, 12.6f), P(7.8f, 12.6f) });
                    break;

                default:
                    strokes.Add(Circle(12f, 12f, 8f));
                    break;
            }

            return Rasterise(strokes, fills, width, name);
        }

        // ── geometry helpers (web viewBox units) ─────────────────────────────────

        private static Vector2 P(float x, float y) => new Vector2(x, y);
        private static Vector2[] Line(float x1, float y1, float x2, float y2) => new[] { P(x1, y1), P(x2, y2) };

        /// <summary>Small filled dot (list bullets).</summary>
        private static Vector2[] Dot(float cx, float cy, float r) => Circle(cx, cy, r, 12);

        private static Vector2[] Circle(float cx, float cy, float r, int seg = 40)
        {
            var pts = new Vector2[seg + 1];
            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.PI * 2f * i / seg;
                pts[i] = new Vector2(cx + Mathf.Cos(a) * r, cy + Mathf.Sin(a) * r);
            }
            return pts;
        }

        private static Vector2[] Arc(float cx, float cy, float r, float fromDeg, float toDeg, int seg = 18)
        {
            var pts = new Vector2[seg + 1];
            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.Deg2Rad * Mathf.Lerp(fromDeg, toDeg, i / (float)seg);
                pts[i] = new Vector2(cx + Mathf.Cos(a) * r, cy + Mathf.Sin(a) * r);
            }
            return pts;
        }

        /// <summary>
        /// Closed heart outline, symmetric about x=12: bottom tip → up the left side → the two
        /// lobes → back down the right. Six cubics, sampled — the same shape Ionicons draws,
        /// which the rasteriser can either stroke ("heart") or fill ("heartfill").
        /// </summary>
        private static Vector2[] HeartPath()
        {
            var pts = new List<Vector2>();
            Cubic(pts, P(12f, 20.8f), P(5.5f, 15.5f), P(2.8f, 12f), P(2.8f, 8.6f));
            Cubic(pts, P(2.8f, 8.6f), P(2.8f, 5.6f), P(5.2f, 3.5f), P(7.9f, 3.5f));
            Cubic(pts, P(7.9f, 3.5f), P(9.8f, 3.5f), P(11.2f, 4.7f), P(12f, 6.2f));
            Cubic(pts, P(12f, 6.2f), P(12.8f, 4.7f), P(14.2f, 3.5f), P(16.1f, 3.5f));
            Cubic(pts, P(16.1f, 3.5f), P(18.8f, 3.5f), P(21.2f, 5.6f), P(21.2f, 8.6f));
            Cubic(pts, P(21.2f, 8.6f), P(21.2f, 12f), P(18.5f, 15.5f), P(12f, 20.8f));
            return pts.ToArray();
        }

        /// <summary>
        /// Outline of an arbitrary star-shaped region, traced radially from <paramref name="centre"/>.
        /// Lets a blobby glyph (the painter's palette) be described as "which points are inside"
        /// instead of as a hand-fitted polyline whose ends have to meet exactly.
        /// </summary>
        private static Vector2[] Blob(Vector2 centre, System.Func<Vector2, bool> inside, int seg = 72)
        {
            var pts = new Vector2[seg + 1];
            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.PI * 2f * i / seg;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                float lo = 0f, hi = 24f;
                for (int k = 0; k < 20; k++)
                {
                    float mid = (lo + hi) * 0.5f;
                    if (inside(centre + dir * mid)) lo = mid; else hi = mid;
                }
                pts[i] = centre + dir * lo;
            }
            return pts;
        }

        /// <summary>A cloud: two bumps over a flat base with rounded feet.</summary>
        private static bool CloudInside(Vector2 q)
        {
            if ((q - new Vector2(9.7f, 11.9f)).sqrMagnitude <= 4.5f * 4.5f) return true;   // big bump
            if ((q - new Vector2(15.3f, 13.8f)).sqrMagnitude <= 3.3f * 3.3f) return true;  // small bump
            if ((q - new Vector2(7.2f, 15.4f)).sqrMagnitude <= 2.4f * 2.4f) return true;   // left foot
            if ((q - new Vector2(15.3f, 15.4f)).sqrMagnitude <= 2.4f * 2.4f) return true;  // right foot
            return q.x >= 7.2f && q.x <= 15.3f && q.y >= 12f && q.y <= 17.8f;              // body
        }

        /// <summary>A painter's palette: a disc with a thumb-hole notch bitten out of the right.</summary>
        private static bool PaletteInside(Vector2 q)
        {
            if ((q - new Vector2(11.6f, 12f)).sqrMagnitude > 8.6f * 8.6f) return false;
            if ((q - new Vector2(19.2f, 15.4f)).sqrMagnitude < 3.4f * 3.4f) return false;   // the notch
            return true;
        }

        /// <summary>
        /// Four-point sparkle: long points on the axes, short ones on the diagonals — the shape
        /// the ✨ glyph reads as at chip size. Closed, so it can be filled.
        /// </summary>
        private static Vector2[] Star(float cx, float cy, float outer, float inner)
        {
            var pts = new Vector2[9];
            for (int i = 0; i < 8; i++)
            {
                float a = Mathf.PI * 0.25f * i;
                float r = (i % 2 == 0) ? outer : inner;
                pts[i] = new Vector2(cx + Mathf.Cos(a) * r, cy + Mathf.Sin(a) * r);
            }
            pts[8] = pts[0];
            return pts;
        }

        /// <summary>Append a sampled cubic Bézier, skipping the start point after the first call.</summary>
        private static void Cubic(List<Vector2> into, Vector2 p0, Vector2 c1, Vector2 c2, Vector2 p3, int seg = 12)
        {
            if (into.Count == 0) into.Add(p0);
            for (int i = 1; i <= seg; i++)
            {
                float t = i / (float)seg, u = 1f - t;
                into.Add(u * u * u * p0 + 3f * u * u * t * c1 + 3f * u * t * t * c2 + t * t * t * p3);
            }
        }

        /// <summary>The web's wavy path, sampled: amplitude ≈1.6 over x 3…21.</summary>
        private static Vector2[] Wave(float y, int seg = 24)
        {
            var pts = new Vector2[seg + 1];
            for (int i = 0; i <= seg; i++)
            {
                float t = i / (float)seg;
                float x = Mathf.Lerp(3f, 21f, t);
                pts[i] = new Vector2(x, y - Mathf.Sin(t * Mathf.PI * 3f) * 1.6f);
            }
            return pts;
        }

        // ── rasteriser ───────────────────────────────────────────────────────────

        private static Sprite Rasterise(List<Vector2[]> strokes, List<Vector2[]> fills, float strokeWidth, string name)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = "Icon_" + name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var px = new Color32[Size * Size];
            float half = strokeWidth * Scale * 0.5f;

            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                // Texture v grows upward, the SVG's y grows downward — flip here so the icon
                // data can stay copy-pasteable from the web's paths.
                var p = new Vector2(x + 0.5f, Size - (y + 0.5f));

                float a = 0f;
                for (int i = 0; i < strokes.Count && a < 1f; i++)
                {
                    float d = DistanceToPolyline(p, strokes[i]);
                    a = Mathf.Max(a, Mathf.Clamp01((half + 0.5f - d) / 1.2f));
                }
                for (int i = 0; i < fills.Count && a < 1f; i++)
                {
                    if (InPolygon(p, fills[i])) a = 1f;
                    else
                    {
                        float d = DistanceToPolyline(p, fills[i]);
                        a = Mathf.Max(a, Mathf.Clamp01((0.7f - d) / 1.2f));   // soften the fill edge
                    }
                }

                px[y * Size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
            }

            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0f, 0f, Size, Size), new Vector2(0.5f, 0.5f));
        }

        private static float DistanceToPolyline(Vector2 p, Vector2[] pts)
        {
            float best = float.MaxValue;
            for (int i = 0; i < pts.Length - 1; i++)
            {
                Vector2 a = pts[i] * Scale, b = pts[i + 1] * Scale;
                Vector2 ab = b - a;
                float len2 = ab.sqrMagnitude;
                float t = len2 < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
                float d = Vector2.Distance(p, a + ab * t);
                if (d < best) best = d;
            }
            return best;
        }

        private static bool InPolygon(Vector2 p, Vector2[] pts)
        {
            bool inside = false;
            for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
            {
                Vector2 a = pts[i] * Scale, b = pts[j] * Scale;
                if (a.y > p.y != b.y > p.y &&
                    p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }
    }
}
