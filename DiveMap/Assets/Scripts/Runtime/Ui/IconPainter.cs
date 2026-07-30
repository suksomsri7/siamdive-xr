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

                case "camera": // photo
                    strokes.Add(new[] { P(3.5f, 8.5f), P(8f, 8.5f), P(9.5f, 6f), P(14.5f, 6f), P(16f, 8.5f), P(20.5f, 8.5f), P(20.5f, 19f), P(3.5f, 19f), P(3.5f, 8.5f) });
                    strokes.Add(Circle(12f, 13.5f, 3.6f));
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
