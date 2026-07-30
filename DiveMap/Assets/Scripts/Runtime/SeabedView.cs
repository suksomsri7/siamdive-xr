using DiveMap.Core;
using UnityEngine;

namespace DiveMap.Runtime
{
    /// <summary>
    /// P2 — the seabed's two looks: sand, and the depth heat-map the web toggles with
    /// <c>setDepthView()</c> (builder.html:640). On the web the map is a vertex-colour swap; the
    /// built-in Standard shader ignores vertex colours (and a custom shader would be stripped from
    /// the build → magenta), so here the seabed's TEXTURE is swapped instead.
    ///
    /// The heat-map texture is baked with exactly the seabed's own UV mapping — radial, so a
    /// texel's distance from the centre is its distance along the boundary ray — and reads the
    /// same sculpt array the mesh was built from, so a pit is the colour of its real depth rather
    /// than of the flat plane under it. Baked once, on first use, then cached.
    /// </summary>
    public static class SeabedView
    {
        private const int TexSize = 512;   // the ramp is smooth; it needs far less than the sand

        private static Material _mat;
        private static Texture _sand;
        private static Texture2D _heat;
        private static float[] _sculpt;
        private static float _slopeX, _slopeZ, _waterLevel, _sx, _sz;
        private static int _rings, _seg;
        private static bool _on;

        /// <summary>True while the heat-map is showing.</summary>
        public static bool DepthView => _on;

        /// <summary>Called by SceneBuilder for every map it builds.</summary>
        public static void Register(Material mat, float[] sculpt, float slopeX, float slopeZ,
                                    float waterLevel, float sx, float sz, int rings, int seg)
        {
            _mat = mat;
            _sand = mat != null ? mat.mainTexture : null;
            _sculpt = sculpt;
            _slopeX = slopeX;
            _slopeZ = slopeZ;
            _waterLevel = waterLevel;
            _sx = sx;
            _sz = sz;
            _rings = rings;
            _seg = seg;
            _heat = null;    // a new map means a new depth field
            _on = false;
        }

        /// <summary>Toggle the heat-map. Returns the state it ended in.</summary>
        public static bool Toggle() => Set(!_on);

        public static bool Set(bool on)
        {
            if (_mat == null) return false;
            _on = on;
            if (on)
            {
                if (_heat == null) _heat = Bake();
                _mat.mainTexture = _heat;
                // The tint must not multiply the ramp — the readout has to be the ramp's colour.
                _mat.color = Color.white;
            }
            else
            {
                _mat.mainTexture = _sand;
                _mat.color = Color.white;
            }
            Debug.Log($"[Scene] depthView={_on}");
            return _on;
        }

        /// <summary>
        /// The heat-map, in the seabed's own UV space. Height at a texel = the same
        /// slope + sculpt expression <c>BuildPolarGrid</c> uses, so the picture agrees with the
        /// geometry it is painted on.
        /// </summary>
        private static Texture2D Bake()
        {
            var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, true)
            {
                name = "SeabedDepth",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var px = new Color32[TexSize * TexSize];

            for (int y = 0; y < TexSize; y++)
            {
                float dz = 2f * ((y + 0.5f) / TexSize - 0.5f);
                for (int x = 0; x < TexSize; x++)
                {
                    float dx = 2f * ((x + 0.5f) / TexSize - 0.5f);
                    float frac = Mathf.Sqrt(dx * dx + dz * dz);
                    float ang = Mathf.Atan2(dz, dx);

                    // Base (unscaled) coords, exactly as the grid was built.
                    float bd = SeabedGeom.BoundaryDist(ang) * Mathf.Min(frac, 1f);
                    float bx = Mathf.Cos(ang) * bd;
                    float bz = Mathf.Sin(ang) * bd;
                    float topY = bx * _slopeX + bz * _slopeZ + SculptAt(frac, ang);

                    DepthPalette.Rgb c = DepthPalette.ColorForHeight(topY, _waterLevel);
                    px[y * TexSize + x] = new Color32(
                        (byte)Mathf.Clamp(Mathf.RoundToInt(c.R * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(c.G * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(c.B * 255f), 0, 255),
                        255);
                }
            }

            tex.SetPixels32(px);
            tex.Apply(true, false);
            Debug.Log($"[Scene] depth map baked {TexSize}² water={_waterLevel:F0} " +
                      $"sculpt={(_sculpt != null ? _sculpt.Length : 0)}");
            return tex;
        }

        /// <summary>Nearest sculpt sample for a (fraction, angle) texel — the grid's own indexing.</summary>
        private static float SculptAt(float frac, float ang)
        {
            if (_sculpt == null || _sculpt.Length == 0 || _rings <= 0 || _seg <= 0) return 0f;
            if (frac <= 0.001f) return _sculpt.Length > 0 ? _sculpt[0] : 0f;

            int r = Mathf.Clamp(Mathf.RoundToInt(frac * _rings), 1, _rings);
            float a01 = ang / (Mathf.PI * 2f);
            if (a01 < 0f) a01 += 1f;
            int j = Mathf.Clamp(Mathf.RoundToInt(a01 * _seg) % _seg, 0, _seg - 1);
            int idx = (r - 1) * _seg + j;
            return idx >= 0 && idx < _sculpt.Length ? _sculpt[idx] : 0f;
        }
    }
}
