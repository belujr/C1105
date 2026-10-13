using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Shared helpers for the heal VFX (HealerHealVFX + HealReceiveFX).
/// Everything is generated in code: procedural textures, additive particle material, particle systems.
/// You do not need to create any texture, material or prefab.
/// </summary>
public static class HealVFXAssets
{
    private static Texture2D dotTex, plusTex, ribbonTex, ringTex, runeTex, sigilTex, vignetteTex, beamTex, spotTex, noiseTex, blobTex, splatTex, summonTex;
    private static Mesh flatQuad;
    private static readonly RaycastHit[] rayBuf = new RaycastHit[8];

    public static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    public static readonly int ColorId = Shader.PropertyToID("_Color");
    public static readonly int TintId = Shader.PropertyToID("_TintColor");

    // ------------------------------------------------------------------ textures
    public static Texture2D Dot { get { if (dotTex == null) dotTex = MakeDot(); return dotTex; } }
    public static Texture2D Plus { get { if (plusTex == null) plusTex = MakePlus(); return plusTex; } }
    public static Texture2D Ribbon { get { if (ribbonTex == null) ribbonTex = MakeRibbon(); return ribbonTex; } }
    public static Texture2D Ring { get { if (ringTex == null) ringTex = MakeRing(); return ringTex; } }
    public static Texture2D Rune { get { if (runeTex == null) runeTex = MakeRune(); return runeTex; } }
    /// <summary>The death floor pattern: a crown of spikes, a dashed ring, three spiral arms and an eye in the middle.</summary>
    public static Texture2D Sigil { get { if (sigilTex == null) sigilTex = MakeSigil(); return sigilTex; } }
    /// <summary>The summoning circle: a double ring with eight nodes, a dashed ring and an eight-pointed star. Different from the heal rune and the death sigil.</summary>
    public static Texture2D Summon { get { if (summonTex == null) summonTex = MakeSummon(); return summonTex; } }
    /// <summary>Tileable fractal noise (Wrap Mode = Repeat). Used by the blood pool shader.</summary>
    public static Texture2D Noise { get { if (noiseTex == null) noiseTex = MakeNoise(); return noiseTex; } }
    /// <summary>Red blood splatter on a transparent background, for the camera lens (big splats near the edges, thrown drops around them).</summary>
    public static Texture2D BloodSplat { get { if (splatTex == null) splatTex = MakeBloodSplat(); return splatTex; } }
    /// <summary>A solid round blob with a soft edge, for alpha-blended particles (blood droplets).</summary>
    public static Texture2D Blob { get { if (blobTex == null) blobTex = MakeBlob(); return blobTex; } }
    /// <summary>White with alpha: clear in a small circle in the middle, fully opaque further out. A pool of light in the dark.</summary>
    public static Texture2D Spotlight { get { if (spotTex == null) spotTex = MakeSpotlight(); return spotTex; } }
    /// <summary>A tall soft beam of light: bright core, fades toward the top. For the death pillar.</summary>
    public static Texture2D Beam { get { if (beamTex == null) beamTex = MakeBeam(); return beamTex; } }
    /// <summary>White with alpha: clear in the middle, opaque at the edges (tint it with the material color).</summary>
    public static Texture2D Vignette { get { if (vignetteTex == null) vignetteTex = MakeVignette(); return vignetteTex; } }

    private static float Smooth(float e0, float e1, float x)
    {
        float t = Mathf.Clamp01((x - e0) / (e1 - e0));
        return t * t * (3f - 2f * t);
    }

    // 1 inside |x - center| < half, fading to 0 over 'soft'
    private static float Band(float x, float center, float half, float soft)
    {
        return 1f - Smooth(half, half + soft, Mathf.Abs(x - center));
    }

    private static Texture2D Make(string name, int w, int h, bool mips, System.Func<float, float, float> f)
    {
        Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, mips, true);
        t.name = name;
        t.wrapMode = TextureWrapMode.Clamp;
        t.filterMode = FilterMode.Bilinear;
        t.hideFlags = HideFlags.HideAndDontSave;
        Color[] px = new Color[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float v = Mathf.Clamp01(f((x + 0.5f) / w, (y + 0.5f) / h));
                px[y * w + x] = new Color(v, v, v, v);
            }
        }
        t.SetPixels(px);
        t.Apply(mips, true);
        return t;
    }

    private static Texture2D MakeDot()
    {
        return Make("HealDot", 64, 64, false, (u, v) =>
        {
            float d = Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f)) * 2f;
            float s = Mathf.Clamp01(1f - d);
            return Mathf.Pow(s, 2.2f);
        });
    }

    private static Texture2D MakePlus()
    {
        return Make("HealPlus", 64, 64, false, (u, v) =>
        {
            float x = Mathf.Abs(u - 0.5f) * 2f;
            float y = Mathf.Abs(v - 0.5f) * 2f;
            float thinY = Mathf.Pow(Mathf.Clamp01(1f - y / 0.24f), 1.5f);
            float thinX = Mathf.Pow(Mathf.Clamp01(1f - x / 0.24f), 1.5f);
            float armX = thinY * Smooth(1f, 0.55f, x);
            float armY = thinX * Smooth(1f, 0.55f, y);
            float d = Mathf.Sqrt(x * x + y * y);
            float glow = 0.35f * Mathf.Pow(Mathf.Clamp01(1f - d), 2f);
            return Mathf.Max(armX, armY) + glow;
        });
    }

    private static Texture2D MakeRibbon()
    {
        return Make("HealRibbon", 8, 32, false, (u, v) => Mathf.Pow(Mathf.Sin(Mathf.PI * v), 1.6f));
    }

    private static Texture2D MakeRing()
    {
        return Make("HealRing", 256, 256, true, (u, v) =>
        {
            float x = (u - 0.5f) * 2f;
            float y = (v - 0.5f) * 2f;
            float r = Mathf.Sqrt(x * x + y * y);
            float line = Band(r, 0.88f, 0.025f, 0.07f);
            float glow = 0.25f * Smooth(0.5f, 0.9f, r) * (1f - Smooth(0.9f, 1f, r));
            return (line + glow) * (1f - Smooth(0.95f, 1f, r));
        });
    }

    private static Texture2D MakeRune()
    {
        const float TwoPi = Mathf.PI * 2f;
        return Make("HealRune", 256, 256, true, (u, v) =>
        {
            float x = (u - 0.5f) * 2f;
            float y = (v - 0.5f) * 2f;
            float r = Mathf.Sqrt(x * x + y * y);
            float ang = Mathf.Atan2(y, x);

            float outer = Band(r, 0.93f, 0.022f, 0.02f);
            float second = 0.8f * Band(r, 0.82f, 0.007f, 0.012f);

            // short ticks between the two rings
            float k = Mathf.Repeat(ang / TwoPi * 24f, 1f);
            float ticks = Band(k, 0.5f, 0.07f, 0.05f) * Band(r, 0.875f, 0.04f, 0.01f);

            // dashed inner ring
            float k2 = Mathf.Repeat(ang / TwoPi * 12f, 1f);
            float dashed = Band(k2, 0.5f, 0.3f, 0.05f) * Band(r, 0.62f, 0.01f, 0.012f) * 0.9f;

            // hexagram (two triangles)
            float v1 = r * Mathf.Cos(Mathf.Repeat(ang, TwoPi / 3f) - Mathf.PI / 3f);
            float v2 = r * Mathf.Cos(Mathf.Repeat(ang + Mathf.PI / 3f, TwoPi / 3f) - Mathf.PI / 3f);
            float star = Mathf.Max(Band(v1, 0.4f, 0.008f, 0.012f), Band(v2, 0.4f, 0.008f, 0.012f));
            star *= 1f - Smooth(0.8f, 0.84f, r);
            star *= 0.85f;

            float fill = 0.14f * Smooth(0.25f, 0.95f, r);

            float a = Mathf.Max(Mathf.Max(outer, second), Mathf.Max(Mathf.Max(ticks, dashed), star)) + fill;
            return a * (1f - Smooth(0.96f, 1f, r));
        });
    }

    private static Texture2D MakeSigil()
    {
        const float TwoPi = Mathf.PI * 2f;
        return Make("DeathSigil", 256, 256, true, (u, v) =>
        {
            float x = (u - 0.5f) * 2f;
            float y = (v - 0.5f) * 2f;
            float r = Mathf.Sqrt(x * x + y * y);
            float ang = Mathf.Atan2(y, x);

            // crown of 14 sharp spikes around the edge
            float k = Mathf.Repeat(ang / TwoPi * 14f, 1f);
            float tri = 1f - Mathf.Abs(k - 0.5f) * 2f;               // 0 at the tooth edges, 1 at the tip
            float tipR = 0.87f + 0.12f * tri;
            float crown = Smooth(0.85f, 0.87f, r) * (1f - Smooth(tipR - 0.012f, tipR, r));

            float ring = Band(r, 0.80f, 0.010f, 0.012f);
            float dashed = Band(Mathf.Repeat(ang / TwoPi * 20f, 1f), 0.5f, 0.28f, 0.06f) * Band(r, 0.69f, 0.010f, 0.012f) * 0.9f;

            // three spiral arms (they match the souls spiralling up)
            float sp = Mathf.Repeat(ang / TwoPi * 3f - r * 1.7f, 1f);
            float arms = Band(sp, 0.5f, 0.045f, 0.05f) * Smooth(0.14f, 0.22f, r) * (1f - Smooth(0.60f, 0.66f, r));

            // an eye in the middle
            float eye = Band(r, 0.10f, 0.010f, 0.012f) + (1f - Smooth(0.045f, 0.065f, r)) * 0.9f;

            float fill = 0.10f * Smooth(0.2f, 0.8f, r) * (1f - Smooth(0.8f, 0.86f, r));

            float a = Mathf.Max(Mathf.Max(crown, ring), Mathf.Max(Mathf.Max(dashed, arms), eye)) + fill;
            return a * (1f - Smooth(0.985f, 1f, r));
        });
    }

    private static Texture2D MakeBeam()
    {
        return Make("DeathBeam", 64, 128, false, (u, v) =>
        {
            float x = Mathf.Abs(u - 0.5f) * 2f;                       // 0 in the middle, 1 at the sides
            float body = Mathf.Pow(Mathf.Clamp01(1f - x), 1.6f);
            float core = Mathf.Pow(Mathf.Clamp01(1f - x), 7f) * 0.8f;
            float rise = Smooth(0f, 0.04f, v);                          // soft start at the floor
            float fall = 1f - Smooth(0.25f, 1f, v);                     // fades toward the top
            return (body * 0.6f + core) * rise * Mathf.Pow(fall, 1.2f);
        });
    }

    private static Texture2D MakeSummon()
    {
        const float TwoPi = Mathf.PI * 2f;
        return Make("SummonCircle", 256, 256, true, (u, v) =>
        {
            float x = (u - 0.5f) * 2f;
            float y = (v - 0.5f) * 2f;
            float r = Mathf.Sqrt(x * x + y * y);
            float ang = Mathf.Atan2(y, x);

            float outer = Band(r, 0.94f, 0.018f, 0.015f);
            float outer2 = 0.8f * Band(r, 0.86f, 0.008f, 0.012f);

            // eight round nodes between the two rings
            float arc = (Mathf.Repeat(ang / TwoPi * 8f, 1f) - 0.5f) * TwoPi * 0.90f / 8f;
            float nd = Mathf.Sqrt((r - 0.90f) * (r - 0.90f) + arc * arc);
            float nodes = 1f - Smooth(0.022f, 0.034f, nd);

            float dashed = Band(Mathf.Repeat(ang / TwoPi * 24f, 1f), 0.5f, 0.3f, 0.05f) * Band(r, 0.70f, 0.008f, 0.012f) * 0.9f;

            // eight-pointed star: two squares, one turned by 45 degrees
            float sq1 = r * Mathf.Cos(Mathf.Repeat(ang, TwoPi / 4f) - Mathf.PI / 4f);
            float sq2 = r * Mathf.Cos(Mathf.Repeat(ang + Mathf.PI / 4f, TwoPi / 4f) - Mathf.PI / 4f);
            float star = Mathf.Max(Band(sq1, 0.5f, 0.008f, 0.012f), Band(sq2, 0.5f, 0.008f, 0.012f)) * (1f - Smooth(0.68f, 0.72f, r)) * 0.9f;

            float core = Band(r, 0.16f, 0.010f, 0.012f) + (1f - Smooth(0.04f, 0.06f, r)) * 0.9f;
            float fill = 0.10f * Smooth(0.25f, 0.85f, r) * (1f - Smooth(0.85f, 0.9f, r));

            float a = Mathf.Max(Mathf.Max(Mathf.Max(outer, outer2), Mathf.Max(nodes, dashed)), Mathf.Max(star, core)) + fill;
            return a * (1f - Smooth(0.985f, 1f, r));
        });
    }

    private static int Mod(int a, int n) { return ((a % n) + n) % n; }

    private static float Hash2(int x, int y)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFFu) / (float)0x1000000;
        }
    }

    // value noise on a lattice that repeats every 'period' cells, so the texture tiles
    private static float ValueNoise(float x, float y, int period)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        float tx = x - xi, ty = y - yi;
        tx = tx * tx * (3f - 2f * tx);
        ty = ty * ty * (3f - 2f * ty);
        int x0 = Mod(xi, period), x1 = Mod(xi + 1, period), y0 = Mod(yi, period), y1 = Mod(yi + 1, period);
        float a = Hash2(x0, y0), b = Hash2(x1, y0), c = Hash2(x0, y1), d = Hash2(x1, y1);
        return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
    }

    private static Texture2D MakeNoise()
    {
        Texture2D t = Make("DeathNoise", 256, 256, true, (u, v) =>
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int o = 0; o < 4; o++)
            {
                int period = 4 << o;
                sum += amp * ValueNoise(u * period, v * period, period);
                norm += amp;
                amp *= 0.5f;
            }
            return sum / norm;
        });
        t.wrapMode = TextureWrapMode.Repeat;
        return t;
    }

    // stamps one irregular blob into an alpha buffer (c in -1..1 screen space)
    private static void StampSplat(float[] a, int n, Vector2 c, float radius, float seed, bool ragged)
    {
        float ext = radius * (ragged ? 1.8f : 1.25f);
        int x0 = Mathf.Clamp(Mathf.FloorToInt(((c.x - ext) * 0.5f + 0.5f) * n), 0, n - 1);
        int x1 = Mathf.Clamp(Mathf.CeilToInt(((c.x + ext) * 0.5f + 0.5f) * n), 0, n - 1);
        int y0 = Mathf.Clamp(Mathf.FloorToInt(((c.y - ext) * 0.5f + 0.5f) * n), 0, n - 1);
        int y1 = Mathf.Clamp(Mathf.CeilToInt(((c.y + ext) * 0.5f + 0.5f) * n), 0, n - 1);

        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                float px = ((x + 0.5f) / n - 0.5f) * 2f;
                float py = ((y + 0.5f) / n - 0.5f) * 2f;
                float d = Mathf.Sqrt((px - c.x) * (px - c.x) + (py - c.y) * (py - c.y)) / radius;
                if (ragged) d += (ValueNoise(px * 9f + seed, py * 9f + seed * 0.7f, 64) - 0.5f) * 1.1f;   // ragged edge with spikes
                float v = 1f - Smooth(0.75f, 1f, d);
                int i = y * n + x;
                if (v > a[i]) a[i] = v;
            }
        }
    }

    private static Texture2D MakeBloodSplat()
    {
        int n = 512;
        float[] a = new float[n * n];

        for (int i = 0; i < 9; i++)
        {
            float ang = Hash2(i, 31) * Mathf.PI * 2f;
            float rr = 0.50f + 0.45f * Hash2(i, 47);
            Vector2 c = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rr;
            float cr = 0.09f + 0.13f * Hash2(i, 59);
            StampSplat(a, n, c, cr, i * 13 + 1, true);

            for (int k = 0; k < 14; k++)   // drops thrown around the splat
            {
                float da = Hash2(i * 100 + k, 71) * Mathf.PI * 2f;
                float dd = cr * (1.4f + 2.6f * Hash2(i * 100 + k, 83));
                Vector2 dc = c + new Vector2(Mathf.Cos(da), Mathf.Sin(da)) * dd;
                StampSplat(a, n, dc, cr * (0.06f + 0.16f * Hash2(i * 100 + k, 97)), 0f, false);
            }
        }

        Texture2D t = new Texture2D(n, n, TextureFormat.RGBA32, false, true);
        t.name = "DeathBloodSplat";
        t.wrapMode = TextureWrapMode.Clamp;
        t.filterMode = FilterMode.Bilinear;
        t.hideFlags = HideFlags.HideAndDontSave;
        Color[] px2 = new Color[n * n];
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float shade = 0.7f + 0.3f * ValueNoise(x * 0.03f, y * 0.03f, 64);
                px2[y * n + x] = new Color(0.68f * shade, 0.02f, 0.04f * shade, a[y * n + x]);
            }
        }
        t.SetPixels(px2);
        t.Apply(false, true);
        return t;
    }

    private static Texture2D MakeBlob()
    {
        return Make("DeathBlob", 64, 64, false, (u, v) =>
        {
            float r = Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f)) * 2f;
            return 1f - Smooth(0.7f, 1f, r);
        });
    }

    private static Texture2D MakeSpotlight()
    {
        int n = 256;
        Texture2D t = new Texture2D(n, n, TextureFormat.RGBA32, false, true);
        t.name = "DeathSpotlight";
        t.wrapMode = TextureWrapMode.Clamp;
        t.filterMode = FilterMode.Bilinear;
        t.hideFlags = HideFlags.HideAndDontSave;
        Color[] px = new Color[n * n];
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float fx = ((x + 0.5f) / n - 0.5f) * 2f;
                float fy = ((y + 0.5f) / n - 0.5f) * 2f;
                float r = Mathf.Sqrt(fx * fx + fy * fy);
                px[y * n + x] = new Color(1f, 1f, 1f, Smooth(0.08f, 0.36f, r));
            }
        }
        t.SetPixels(px);
        t.Apply(false, true);
        return t;
    }

    private static Texture2D MakeVignette()
    {
        int n = 128;
        Texture2D t = new Texture2D(n, n, TextureFormat.RGBA32, false, true);
        t.name = "DeathVignette";
        t.wrapMode = TextureWrapMode.Clamp;
        t.filterMode = FilterMode.Bilinear;
        t.hideFlags = HideFlags.HideAndDontSave;
        Color[] px = new Color[n * n];
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float fx = ((x + 0.5f) / n - 0.5f) * 2f;
                float fy = ((y + 0.5f) / n - 0.5f) * 2f;
                float r = Mathf.Sqrt(fx * fx + fy * fy);
                float a = Smooth(0.3f, 1.5f, r);   // very gradual: no visible edge
                a = Mathf.Pow(a, 1.4f);
                px[y * n + x] = new Color(1f, 1f, 1f, a);
            }
        }
        t.SetPixels(px);
        t.Apply(false, true);
        return t;
    }

    public static Mesh FlatQuad
    {
        get
        {
            if (flatQuad != null) return flatQuad;
            flatQuad = new Mesh();
            flatQuad.name = "HealFlatQuad";
            flatQuad.hideFlags = HideFlags.HideAndDontSave;
            flatQuad.vertices = new[]
            {
                new Vector3(-1f, 0f, -1f), new Vector3(-1f, 0f, 1f),
                new Vector3(1f, 0f, 1f), new Vector3(1f, 0f, -1f)
            };
            flatQuad.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            flatQuad.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            flatQuad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            flatQuad.RecalculateBounds();
            return flatQuad;
        }
    }

    // ------------------------------------------------------------------ material
    /// <summary>Additive, unlit, no depth write. 'brightness' is an HDR multiplier (bloom picks it up).</summary>
    public static Material CreateMaterial(Texture2D tex, float brightness)
    {
        Shader s = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (s == null) s = Shader.Find("Legacy Shaders/Particles/Additive");
        if (s == null) s = Shader.Find("Sprites/Default");

        Material m = new Material(s);
        m.name = "HealVFX_" + tex.name;
        Color c = new Color(brightness, brightness, brightness, 1f);

        if (s.name.StartsWith("Universal"))
        {
            m.SetFloat("_Surface", 1f);   // transparent
            m.SetFloat("_Blend", 2f);     // additive
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.One);
            if (m.HasProperty("_SrcBlendAlpha")) m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            if (m.HasProperty("_DstBlendAlpha")) m.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_ZWrite", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", c);
        }
        else
        {
            m.mainTexture = tex;
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(brightness * 0.5f, brightness * 0.5f, brightness * 0.5f, 0.5f));
            else if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }
        return m;
    }

    /// <summary>Alpha-blended (not additive), unlit, no depth write. For things that must look solid, like blood droplets.</summary>
    public static Material CreateAlphaMaterial(Texture2D tex)
    {
        Shader s = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (s == null) s = Shader.Find("Sprites/Default");

        Material m = new Material(s);
        m.name = "DeathAlpha_" + tex.name;
        if (s.name.StartsWith("Universal"))
        {
            m.SetFloat("_Surface", 1f);   // transparent
            m.SetFloat("_Blend", 0f);     // alpha blend
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_SrcBlendAlpha")) m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            if (m.HasProperty("_DstBlendAlpha")) m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);
        }
        else
        {
            m.mainTexture = tex;
        }
        return m;
    }

    // ------------------------------------------------------------------ particles
    public static ParticleSystem MakeSystem(Transform parent, string name, Material mat, int maxParticles, bool worldSpace)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.playOnAwake = true;   // so it comes back by itself when a pooled enemy is re-enabled
        main.simulationSpace = worldSpace ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
        main.startSpeed = 0f;
        main.startLifetime = 1f;
        main.startSize = 0.2f;
        main.maxParticles = maxParticles;
        main.gravityModifier = 0f;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        main.scalingMode = ParticleSystemScalingMode.Local;

        ParticleSystem.EmissionModule em = ps.emission;
        em.enabled = true;
        em.rateOverTime = 0f;

        ParticleSystem.ShapeModule sh = ps.shape;
        sh.enabled = false;

        ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;

        ps.Play();
        return ps;
    }

    public static void SetRate(ParticleSystem ps, float rate)
    {
        if (ps == null) return;
        ParticleSystem.EmissionModule em = ps.emission;
        em.rateOverTime = rate;
    }

    /// <summary>Alpha: 0 at birth, 'peak' after fadeIn, holds until fadeOutStart, 0 at death (colors untouched).</summary>
    public static void FadeInOut(ParticleSystem ps, float fadeIn, float fadeOutStart, float peak)
    {
        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[]
            {
                new GradientAlphaKey(0f, 0f), new GradientAlphaKey(peak, fadeIn),
                new GradientAlphaKey(peak, fadeOutStart), new GradientAlphaKey(0f, 1f)
            });
        col.color = new ParticleSystem.MinMaxGradient(g);
    }

    public static void SizeCurve(ParticleSystem ps, AnimationCurve curve)
    {
        ParticleSystem.SizeOverLifetimeModule sz = ps.sizeOverLifetime;
        sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, curve);
    }

    // ------------------------------------------------------------------ ground
    /// <summary>Point on the floor under 'owner', raised by 'offset' (raise it to clear grass). Falls back to the owner's own height.</summary>
    public static Vector3 GroundPoint(Transform owner, LayerMask mask, float offset)
    {
        Vector3 p = owner.position;
        float y = p.y;
        int n = Physics.RaycastNonAlloc(p + Vector3.up, Vector3.down, rayBuf, 3f, mask, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            RaycastHit h = rayBuf[i];
            if (h.collider == null || h.collider.transform.IsChildOf(owner)) continue;
            if (h.distance < best) { best = h.distance; y = h.point.y; }
        }
        p.y = y + offset;
        return p;
    }

    // ------------------------------------------------------------------ EnemyHitFX safety
    /// <summary>
    /// EnemyHitFX copies visible static meshes into glow shells when an enemy has no skinned mesh.
    /// Call EnsureHitFX BEFORE creating child visuals and ProtectFromHitFX for each mesh quad,
    /// so the heal VFX never gets a hit glow shell.
    /// </summary>
    public static EnemyHitFX EnsureHitFX(GameObject owner)
    {
        EnemyHitFX fx = owner.GetComponent<EnemyHitFX>();
        if (fx == null) fx = owner.AddComponent<EnemyHitFX>();
        return fx;
    }

    public static void ProtectFromHitFX(GameObject owner, Renderer r)
    {
        EnemyHitFX fx = EnsureHitFX(owner);
        if (r != null && !fx.excludeRenderers.Contains(r)) fx.excludeRenderers.Add(r);
    }
}

/// <summary>A flat glowing rune that lies on the floor, spins slowly, fades in and out.</summary>
public class HealGroundRune
{
    private readonly Transform owner;
    private readonly Transform tf;
    private readonly MeshRenderer mr;
    private readonly Material mat;
    private readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
    private float level;
    private float angle;

    // invisible quad that clears grass/flowers under the rune (draws into your grass mask textures)
    private Transform maskTf;
    private MeshRenderer maskMr;
    private Material maskMat;
    private readonly MaterialPropertyBlock maskMpb = new MaterialPropertyBlock();
    private static readonly int OpacityId = Shader.PropertyToID("_Opacity");

    public bool clearGrass = true;
    public float clearScale = 1.3f;
    [Tooltip("Size when it starts to fade in (1 = full size). Smaller = it grows more.")]
    public float startScale = 0.75f;

    public Renderer Renderer { get { return mr; } }
    public Renderer MaskRenderer { get { return maskMr; } }
    public bool IsVisible { get { return level > 0.001f; } }

    public HealGroundRune(Transform owner, string name, Texture2D tex, float brightness)
    {
        this.owner = owner;
        GameObject go = new GameObject(name);
        tf = go.transform;
        tf.SetParent(owner, false);
        go.AddComponent<MeshFilter>().sharedMesh = HealVFXAssets.FlatQuad;
        mr = go.AddComponent<MeshRenderer>();
        mat = HealVFXAssets.CreateMaterial(tex, brightness);
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        mr.enabled = false;

        Shader maskShader = Shader.Find("InfiniteGrass/Modifiers/HealAuraGrassMask");
        if (maskShader != null)
        {
            GameObject mg = new GameObject(name + "_GrassClear");
            maskTf = mg.transform;
            maskTf.SetParent(owner, false);
            mg.AddComponent<MeshFilter>().sharedMesh = HealVFXAssets.FlatQuad;
            maskMr = mg.AddComponent<MeshRenderer>();
            maskMat = new Material(maskShader);
            maskMr.sharedMaterial = maskMat;
            maskMr.shadowCastingMode = ShadowCastingMode.Off;
            maskMr.receiveShadows = false;
            maskMr.lightProbeUsage = LightProbeUsage.Off;
            maskMr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            maskMr.enabled = false;
        }
    }

    /// <param name="radius">World radius of the rune.</param>
    public void Tick(float dt, bool on, Vector3 groundPos, float radius, Color color,
                     float spinDegPerSec, float fadeIn, float fadeOut)
    {
        level = Mathf.MoveTowards(level, on ? 1f : 0f, dt / Mathf.Max(0.01f, on ? fadeIn : fadeOut));
        if (level <= 0.001f)
        {
            if (mr.enabled) mr.enabled = false;
            if (maskMr != null && maskMr.enabled) maskMr.enabled = false;
            return;
        }

        mr.enabled = true;
        angle += spinDegPerSec * dt;
        tf.position = groundPos;
        tf.rotation = Quaternion.Euler(0f, angle, 0f);

        float ease = level * level * (3f - 2f * level);
        float world = radius * Mathf.Lerp(startScale, 1f, ease) * (1f + 0.05f * Mathf.Sin(Time.time * 5f));
        tf.localScale = Vector3.one * (world / Mathf.Max(0.0001f, owner.lossyScale.x));

        Color c = color;
        c.a = ease * (0.85f + 0.15f * Mathf.Sin(Time.time * 7f));
        mpb.SetColor(HealVFXAssets.BaseColorId, c);
        mpb.SetColor(HealVFXAssets.ColorId, c);
        mpb.SetColor(HealVFXAssets.TintId, c);
        mr.SetPropertyBlock(mpb);

        if (maskMr != null)
        {
            maskMr.enabled = clearGrass;
            if (clearGrass)
            {
                maskTf.position = groundPos;
                maskTf.rotation = Quaternion.identity;
                maskTf.localScale = Vector3.one * (world * clearScale / Mathf.Max(0.0001f, owner.lossyScale.x));
                maskMpb.SetFloat(OpacityId, ease);
                maskMr.SetPropertyBlock(maskMpb);
            }
        }
    }

    public void Reset()
    {
        level = 0f;
        mr.enabled = false;
        if (maskMr != null) maskMr.enabled = false;
    }

    public void Dispose()
    {
        if (mat != null) Object.Destroy(mat);
        if (maskMat != null) Object.Destroy(maskMat);
    }
}

/// <summary>An invisible disc that clears grass and flowers (draws into the grass mask textures). Grows and fades from code.</summary>
public class HealGrassClear
{
    private readonly Transform owner;
    private readonly Transform tf;
    private readonly MeshRenderer mr;
    private readonly Material mat;
    private readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
    private static readonly int OpacityId = Shader.PropertyToID("_Opacity");

    public Renderer Renderer { get { return mr; } }

    // the name must contain "GrassClear": PlayerDissolveController and EnemyHitFX skip meshes with that name
    public HealGrassClear(Transform owner, string name)
    {
        this.owner = owner;
        Shader sh = Shader.Find("InfiniteGrass/Modifiers/HealAuraGrassMask");
        if (sh == null) return;

        GameObject go = new GameObject(name + "_GrassClear");
        tf = go.transform;
        tf.SetParent(owner, false);
        go.AddComponent<MeshFilter>().sharedMesh = HealVFXAssets.FlatQuad;
        mr = go.AddComponent<MeshRenderer>();
        mat = new Material(sh);
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        mr.enabled = false;
    }

    /// <param name="radius">World radius of the cleared disc.</param>
    public void Tick(Vector3 pos, float radius, float opacity)
    {
        if (mr == null) return;
        if (opacity <= 0.001f || radius <= 0.01f)
        {
            if (mr.enabled) mr.enabled = false;
            return;
        }
        mr.enabled = true;
        tf.position = pos;
        tf.rotation = Quaternion.identity;
        tf.localScale = Vector3.one * (radius / Mathf.Max(0.0001f, owner.lossyScale.x));
        mpb.SetFloat(OpacityId, opacity);
        mr.SetPropertyBlock(mpb);
    }

    public void Hide()
    {
        if (mr != null && mr.enabled) mr.enabled = false;
    }

    public void Dispose()
    {
        if (mat != null) Object.Destroy(mat);
    }
}