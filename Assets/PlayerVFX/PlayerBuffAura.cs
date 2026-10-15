using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Settings for the power-up effect on the player. Shown as a foldout on BuffRoomPedestal.
/// </summary>
[System.Serializable]
public class BuffAuraSettings
{
    [Tooltip("Turn the whole effect off.")]
    public bool enabled = true;

    [Header("Look")]
    [Tooltip("Use the glow color of the buff the player picked up. Untick to use 'Color' below.")]
    public bool useBuffColor = true;
    public Color color = new Color(1f, 0.45f, 0.15f);
    [Tooltip("Brightness of all the energy (HDR, so it also blooms if your camera has bloom).")]
    public float brightness = 1f;

    [Header("Soul rings")]
    [Tooltip("Rings of energy start this far from the player (meters) and pull into the body.")]
    public float ringStartRadius = 1.6f;
    [Tooltip("Brightness of the rings.")]
    public float ringIntensity = 2f;
    [Tooltip("After the energy lands, rings sweep up the body from the feet to above the head.")]
    public bool risingRings = true;
    [Tooltip("A ring spreads out over the ground around the player at the moment the energy lands.")]
    public bool groundRing = true;
    [Tooltip("Optional. A tileable noise texture (Wrap Mode = Repeat) for the rings. If empty, one is made in code.")]
    public Texture2D noiseTexture;

    [Header("Soft glow")]
    [Tooltip("Gentle round sparks per second drifting up around the player. 0 = none.")]
    public float sparkRate = 28f;
    [Tooltip("Faint glow around the whole body. 0 = none.")]
    [Range(0f, 1f)] public float haloStrength = 0.25f;

    [Header("Timing")]
    [Tooltip("Seconds the soft glow, sparks and light stay after the energy lands. 0 = until the player object is gone.")]
    public float duration = 3.5f;
    [Tooltip("Seconds it takes to fade away at the end.")]
    public float fadeOut = 1.5f;

    [Header("Light on the player")]
    [Tooltip("A light in front of the player that makes the character itself glow. It flashes when the energy lands.")]
    public float lightIntensity = 4f;
    public float lightRange = 4f;
}

/// <summary>
/// The "player gains a power" effect, in three beats:
///
///   1. SOUL RINGS: thin rings of energy (your VFX/SoulRing shader), broken into spinning arcs, start wide around
///      the player and pull tight into the body.
///   2. IMPACT: at the moment they hit, a ring bursts out over the ground, two more sweep up the body from the
///      feet to above the head, and a light flashes on the character.
///   3. LINGER: soft round sparks, a faint glow around the silhouette and a gentle light stay for a few seconds
///      and fade out.
///
/// The soft sparks use your PortalParticle shader. It is created from code by BuffRoomPedestal when the player
/// gains the buff. Nothing to set up.
/// </summary>
public class PlayerBuffAura : MonoBehaviour
{
    private const float HitTime = 0.5f;   // seconds after spawning when the rings reach the body

    public static PlayerBuffAura Spawn(Transform player, Color color, Shader particleShader, Shader soulRingShader, BuffAuraSettings settings)
    {
        if (player == null || settings == null || !settings.enabled) return null;

        if (soulRingShader == null) soulRingShader = Shader.Find("VFX/SoulRing");
        if (particleShader == null) particleShader = Shader.Find("VFX/PortalParticle");
        if (soulRingShader == null && particleShader == null) return null;

        GameObject root = new GameObject("PlayerBuffAura");
        PlayerBuffAura aura = root.AddComponent<PlayerBuffAura>();
        aura.Init(player, color, particleShader, soulRingShader, settings);
        return aura;
    }

    private enum RingKind { Contract, Rise, Ground }

    private class Ring
    {
        public RingKind kind;
        public Transform tf;
        public MeshRenderer renderer;
        public Material mat;
        public float delay;
        public float duration;
        public float y0, y1;
        public float r0, r1;
        public float peak;
        public float spin;
        public float rotate;
    }

    private BuffAuraSettings s;
    private Transform player;
    private float height = 1.8f;
    private float bodyRadius = 0.4f;
    private float startTime;
    private float ringsEnd;
    private bool burstDone;

    private readonly List<Ring> rings = new List<Ring>();
    private Mesh ringMesh;

    private ParticleSystem sparks;
    private ParticleSystem halo;
    private Light lightComp;

    private Material matSpark, matHalo;
    private Color tintSpark, tintHalo;

    private static Texture2D generatedNoise;

    // ------------------------------------------------------------------ build

    private void Init(Transform playerTransform, Color color, Shader particleShader, Shader soulRingShader, BuffAuraSettings settings)
    {
        s = settings;
        player = playerTransform;
        startTime = Time.time;

        // stand at the player's feet, as tall as the player
        Vector3 bottom = player.position;
        CharacterController cc = player.GetComponentInChildren<CharacterController>();
        if (cc != null)
        {
            Bounds b = cc.bounds;
            bottom = new Vector3(b.center.x, b.min.y, b.center.z);
            height = Mathf.Max(0.5f, b.size.y);
            bodyRadius = Mathf.Max(0.15f, cc.radius);
        }

        transform.SetParent(player, false);
        transform.position = bottom;
        transform.rotation = Quaternion.identity;      // rings stay flat and level

        int layer = player.gameObject.layer;
        gameObject.layer = layer;

        if (soulRingShader != null) BuildRings(soulRingShader, color, layer);

        if (particleShader != null)
        {
            Color hot = Color.Lerp(color, Color.white, 0.5f);
            tintSpark = hot * (1.6f * s.brightness);
            tintHalo = color * (0.9f * s.brightness);
            matSpark = MakeParticleMaterial(particleShader, "BuffAuraSpark", tintSpark, 1.6f);
            matHalo = MakeParticleMaterial(particleShader, "BuffAuraHalo", tintHalo, 1.2f);

            if (s.sparkRate > 0.01f) BuildSparks(layer);
            if (s.haloStrength > 0.001f) BuildHalo(layer);
        }

        BuildLight(color, layer);
    }

    // ---- rings

    private void BuildRings(Shader shader, Color color, int layer)
    {
        Texture2D noise = s.noiseTexture != null ? s.noiseTexture : GetNoise();
        ringMesh = BuildRingMesh(96, 0.82f);

        Color hot = Color.Lerp(color, Color.white, 0.55f) * (2.2f * s.brightness);
        Color edge = color * (1.5f * s.brightness);

        // three rings pull into the body at chest, hip and shoulder height
        float[] fractions = { 0.3f, 0.55f, 0.8f };
        for (int i = 0; i < fractions.Length; i++)
        {
            Ring r = new Ring();
            r.kind = RingKind.Contract;
            r.delay = i * 0.1f;
            r.duration = 0.6f;
            r.r0 = s.ringStartRadius * (1f - i * 0.1f);
            r.r1 = bodyRadius * 0.3f;
            r.y0 = height * fractions[i];
            r.y1 = r.y0 + height * 0.05f;
            r.peak = s.ringIntensity;
            r.spin = 1.8f + i * 0.4f;
            AddRing(r, shader, noise, hot, edge, 4f, 0.45f, 4f, layer);
        }

        // two rings sweep up the body once the energy has landed
        if (s.risingRings)
        {
            for (int i = 0; i < 2; i++)
            {
                Ring r = new Ring();
                r.kind = RingKind.Rise;
                r.delay = HitTime + i * 0.2f;
                r.duration = 1f;
                r.r0 = bodyRadius * 1.4f;
                r.r1 = bodyRadius * 1.8f;
                r.y0 = 0.02f;
                r.y1 = height + 0.35f;
                r.peak = s.ringIntensity * 0.8f;
                r.spin = 2.5f;
                AddRing(r, shader, noise, hot, edge, 6f, 0.35f, 5f, layer);
            }
        }

        // a shockwave across the ground
        if (s.groundRing)
        {
            Ring r = new Ring();
            r.kind = RingKind.Ground;
            r.delay = HitTime;
            r.duration = 0.95f;
            r.r0 = 0.3f;
            r.r1 = Mathf.Max(2.2f, s.ringStartRadius * 1.4f);
            r.y0 = 0.03f;
            r.y1 = 0.03f;
            r.peak = s.ringIntensity;
            r.spin = 1.2f;
            AddRing(r, shader, noise, hot, edge, 8f, 0.3f, 4f, layer);
        }

        foreach (Ring r in rings) ringsEnd = Mathf.Max(ringsEnd, r.delay + r.duration);
    }

    private void AddRing(Ring r, Shader shader, Texture2D noise, Color hot, Color edge, float arcs, float gap, float sharp, int layer)
    {
        GameObject go = new GameObject("SoulRing_" + r.kind);
        go.layer = layer;
        go.transform.SetParent(transform, false);

        MeshFilter mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = ringMesh;

        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        Material m = new Material(shader);
        m.name = "BuffSoulRing (instance)";
        m.SetTexture("_NoiseTex", noise);
        m.SetFloat("_Arcs", arcs);
        m.SetFloat("_GapSize", gap);
        m.SetFloat("_Sharp", sharp);
        m.SetColor("_ColorHot", hot);
        m.SetColor("_ColorEdge", edge);
        m.SetFloat("_Intensity", 0f);
        m.SetFloat("_Rotate", 0f);
        m.SetFloat("_Seed", Random.value * 10f);
        mr.sharedMaterial = m;
        mr.enabled = false;               // shown when its turn comes

        r.tf = go.transform;
        r.renderer = mr;
        r.mat = m;
        rings.Add(r);
    }

    // A flat ring: U goes around (0..1), V goes from the inner edge (0) to the outer edge (1), as the shader expects.
    private static Mesh BuildRingMesh(int segments, float inner)
    {
        Mesh mesh = new Mesh();
        mesh.name = "BuffSoulRingMesh";

        Vector3[] verts = new Vector3[(segments + 1) * 2];
        Vector2[] uvs = new Vector2[verts.Length];
        int[] tris = new int[segments * 6];

        for (int i = 0; i <= segments; i++)
        {
            float u = i / (float)segments;
            float a = u * Mathf.PI * 2f;
            float c = Mathf.Cos(a);
            float sn = Mathf.Sin(a);

            verts[i * 2] = new Vector3(c * inner, 0f, sn * inner);
            uvs[i * 2] = new Vector2(u, 0f);
            verts[i * 2 + 1] = new Vector3(c, 0f, sn);
            uvs[i * 2 + 1] = new Vector2(u, 1f);
        }

        for (int i = 0; i < segments; i++)
        {
            int b = i * 2;
            int t = i * 6;
            tris[t] = b;
            tris[t + 1] = b + 1;
            tris[t + 2] = b + 2;
            tris[t + 3] = b + 1;
            tris[t + 4] = b + 3;
            tris[t + 5] = b + 2;
        }

        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        return mesh;
    }

    // ---- tileable noise (made once, shared)

    private static Texture2D GetNoise()
    {
        if (generatedNoise != null) return generatedNoise;

        const int size = 128;
        float[,] coarse = MakeLattice(8, 1234);
        float[,] fine = MakeLattice(16, 777);

        Color32[] px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size;
                float v = y / (float)size;
                float n = 0.6f * SampleLattice(coarse, 8, u, v) + 0.4f * SampleLattice(fine, 16, u, v);
                n = Mathf.Clamp01((n - 0.5f) * 1.6f + 0.5f);        // a bit more contrast
                byte c = (byte)(n * 255f);
                px[y * size + x] = new Color32(c, c, c, 255);
            }
        }

        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
        tex.name = "BuffSoulRingNoise";
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;
        tex.SetPixels32(px);
        tex.Apply();

        generatedNoise = tex;
        return tex;
    }

    private static float[,] MakeLattice(int n, int seed)
    {
        System.Random rnd = new System.Random(seed);
        float[,] g = new float[n, n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                g[x, y] = (float)rnd.NextDouble();
        return g;
    }

    // smooth value noise that wraps around, so the texture tiles seamlessly
    private static float SampleLattice(float[,] g, int n, float u, float v)
    {
        float fx = u * n;
        float fy = v * n;
        int ix = Mathf.FloorToInt(fx);
        int iy = Mathf.FloorToInt(fy);
        float tx = fx - ix;
        float ty = fy - iy;
        tx = tx * tx * (3f - 2f * tx);
        ty = ty * ty * (3f - 2f * ty);

        int x0 = ix % n;
        int y0 = iy % n;
        int x1 = (ix + 1) % n;
        int y1 = (iy + 1) % n;

        float a = Mathf.Lerp(g[x0, y0], g[x1, y0], tx);
        float b = Mathf.Lerp(g[x0, y1], g[x1, y1], tx);
        return Mathf.Lerp(a, b, ty);
    }

    // ---- soft sparks, halo, light

    private static Material MakeParticleMaterial(Shader shader, string name, Color tint, float softness)
    {
        Material m = new Material(shader);
        m.name = name + " (instance)";
        m.renderQueue = 3001;
        m.SetColor("_Tint", tint);
        m.SetFloat("_Softness", softness);
        return m;
    }

    // A particle system is configured while its object is inactive and switched on afterwards,
    // so it never plays with half-finished settings.
    private ParticleSystem MakeSystem(string objName, Material mat, Vector3 localPos, int layer, out ParticleSystemRenderer psr)
    {
        GameObject go = new GameObject(objName);
        go.SetActive(false);
        go.layer = layer;
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPos;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        psr = go.GetComponent<ParticleSystemRenderer>();
        psr.sharedMaterial = mat;
        psr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        psr.receiveShadows = false;
        return ps;
    }

    private static Gradient FadeGradient(float peakAlpha, float peakTime, float holdTime)
    {
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(peakAlpha, peakTime),
                new GradientAlphaKey(peakAlpha, holdTime),
                new GradientAlphaKey(0f, 1f)
            });
        return g;
    }

    // small round sparks drifting up around the body, like fireflies: calm, not busy
    private void BuildSparks(int layer)
    {
        ParticleSystemRenderer psr;
        sparks = MakeSystem("Sparks", matSpark, Vector3.zero, layer, out psr);

        var main = sparks.main;
        main.loop = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.4f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
        main.startColor = Color.white;
        main.maxParticles = 200;

        var shape = sparks.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.ConeVolume;   // a cylinder when the angle is 0
        shape.angle = 0f;
        shape.radius = bodyRadius * 1.6f;
        shape.radiusThickness = 0.6f;
        shape.length = height;
        shape.rotation = new Vector3(-90f, 0f, 0f);             // stand it upright

        var vel = sparks.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.x = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
        vel.y = new ParticleSystem.MinMaxCurve(0.25f, 0.7f);
        vel.z = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
        vel.orbitalX = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.orbitalY = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
        vel.orbitalZ = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.radial = new ParticleSystem.MinMaxCurve(0f, 0f);

        var col = sparks.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(FadeGradient(1f, 0.2f, 0.55f));

        var em = sparks.emission;
        em.rateOverTime = 0f;                                   // starts when the energy lands

        psr.renderMode = ParticleSystemRenderMode.Billboard;

        sparks.gameObject.SetActive(true);
    }

    // a big soft glow behind the body: it shows as a glow around the silhouette
    private void BuildHalo(int layer)
    {
        ParticleSystemRenderer psr;
        halo = MakeSystem("Halo", matHalo, new Vector3(0f, height * 0.5f, 0f), layer, out psr);

        var main = halo.main;
        main.loop = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = 1.6f;
        main.startSpeed = 0f;
        main.startSize = height * 1.15f;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
        main.startColor = Color.white;
        main.maxParticles = 8;

        var shape = halo.shape;
        shape.enabled = false;

        var col = halo.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(FadeGradient(s.haloStrength, 0.3f, 0.5f));

        var em = halo.emission;
        em.rateOverTime = 0f;                                   // starts when the energy lands

        psr.renderMode = ParticleSystemRenderMode.Billboard;

        halo.gameObject.SetActive(true);
    }

    private void BuildLight(Color color, int layer)
    {
        if (s.lightIntensity <= 0f) return;

        GameObject lg = new GameObject("AuraLight");
        lg.layer = layer;
        lg.transform.SetParent(transform, false);

        lightComp = lg.AddComponent<Light>();
        lightComp.type = LightType.Point;
        lightComp.color = color;
        lightComp.range = Mathf.Max(0.1f, s.lightRange);
        lightComp.shadows = LightShadows.None;
        lightComp.intensity = 0f;
    }

    // ------------------------------------------------------------------ playback

    private void Update()
    {
        if (player == null) { Destroy(gameObject); return; }

        float dt = Time.deltaTime;
        float t = Time.time - startTime;

        // --- soul rings
        foreach (Ring r in rings) UpdateRing(r, t, dt);

        // --- soft parts: fade in as the rings hit, fade out at the end
        float ramp = Mathf.Clamp01((t - HitTime) / 0.35f);
        float endT = s.duration > 0f ? HitTime + s.duration : float.PositiveInfinity;
        float fade = s.duration > 0f ? Mathf.Clamp01((endT - t) / Mathf.Max(0.05f, s.fadeOut)) : 1f;
        float linger = ramp * fade;

        if (matSpark != null) matSpark.SetColor("_Tint", tintSpark * linger);
        if (matHalo != null) matHalo.SetColor("_Tint", tintHalo * linger);
        SetRate(sparks, s.sparkRate * linger);
        SetRate(halo, 1.6f * linger);

        // one burst of sparks at the moment of impact
        if (!burstDone && t >= HitTime)
        {
            burstDone = true;
            if (sparks != null) sparks.Emit(Mathf.RoundToInt(s.sparkRate));
        }

        // --- light: builds as the rings close in, flashes on impact, then a soft glow
        if (lightComp != null)
        {
            Vector3 chest = transform.position + Vector3.up * (height * 0.55f);
            Vector3 pos = chest + Vector3.up * 0.3f;
            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 toCam = cam.transform.position - chest;
                if (toCam.sqrMagnitude > 0.0001f) pos += toCam.normalized * 0.9f;
            }
            lightComp.transform.position = pos;

            float build = Mathf.Clamp01(t / HitTime) * 0.3f;
            float flash = t >= HitTime ? Mathf.Exp(-(t - HitTime) * 3f) * 1.8f : 0f;
            float flicker = 0.9f + 0.07f * Mathf.Sin(Time.time * 11f) + 0.03f * Mathf.Sin(Time.time * 17.3f);
            float level = t < HitTime ? build : (flash + linger * 0.5f) * Mathf.Min(1f, fade * 1.5f);

            lightComp.intensity = s.lightIntensity * level * flicker;
        }

        // --- done?
        if (s.duration > 0f && t >= endT && t >= ringsEnd)
            Destroy(gameObject);
    }

    private void UpdateRing(Ring r, float t, float dt)
    {
        if (r.tf == null) return;

        float k = (t - r.delay) / r.duration;
        if (k < 0f || k > 1f)
        {
            if (r.renderer.enabled) r.renderer.enabled = false;
            return;
        }
        if (!r.renderer.enabled) r.renderer.enabled = true;

        float radius;
        float y;
        float intensity;

        switch (r.kind)
        {
            case RingKind.Contract:
                {
                    float e = k * k;                                        // pulls in faster and faster
                    radius = Mathf.Lerp(r.r0, r.r1, e);
                    y = Mathf.Lerp(r.y0, r.y1, k);
                    float appear = Mathf.Clamp01(k / 0.2f);
                    float vanish = k > 0.85f ? 1f - (k - 0.85f) / 0.15f : 1f;
                    intensity = r.peak * appear * (0.4f + 0.6f * k) * vanish;   // brighter as it tightens
                    break;
                }
            case RingKind.Rise:
                {
                    float e = 1f - (1f - k) * (1f - k);
                    radius = Mathf.Lerp(r.r0, r.r1, Mathf.Sin(k * Mathf.PI));
                    y = Mathf.Lerp(r.y0, r.y1, e);
                    intensity = r.peak * Mathf.Sin(k * Mathf.PI);
                    break;
                }
            default:   // Ground
                {
                    float e = 1f - Mathf.Pow(1f - k, 3f);
                    radius = Mathf.Lerp(r.r0, r.r1, e);
                    y = r.y0;
                    intensity = r.peak * Mathf.Pow(1f - k, 1.6f) * Mathf.Min(1f, k * 8f);
                    break;
                }
        }

        r.tf.localPosition = new Vector3(0f, y, 0f);
        r.tf.localScale = new Vector3(radius, 1f, radius);

        r.rotate += r.spin * dt;
        r.mat.SetFloat("_Rotate", r.rotate);
        r.mat.SetFloat("_Intensity", intensity);
    }

    private static void SetRate(ParticleSystem ps, float rate)
    {
        if (ps == null) return;
        var em = ps.emission;
        em.rateOverTime = rate;
    }

    private void OnDestroy()
    {
        foreach (Ring r in rings)
            if (r.mat != null) Destroy(r.mat);
        if (ringMesh != null) Destroy(ringMesh);
        if (matSpark != null) Destroy(matSpark);
        if (matHalo != null) Destroy(matHalo);
    }
}