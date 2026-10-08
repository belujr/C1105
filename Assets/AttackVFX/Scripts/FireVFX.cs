using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Stylized 3D fire that also works as a light source. One component, built entirely in code when the game starts:
///   FLAME   several tapered flame tongues (real 3D meshes, so it has volume from every side, not a flat plane),
///           cel-shaded in a hot core / orange / dark red tip with ragged, rising, swaying edges
///   LIGHT   a warm point light that flickers with the flame (your secondary light source)
///   GLOW    a soft halo around the flame
///   EMBERS  small sparks that rise and flutter
///   SMOKE   a thin, dark wisp
///
/// Setup: create an empty child where the fire should burn (the crown of the pillar, the inside of the lantern),
/// add this component, then right-click its header and choose "Apply Pillar Preset" or "Apply Lantern Preset".
/// Press Play. Changes you make in the inspector while playing rebuild the fire live.
///
/// The fire is built when the game starts, so you will not see it in the Scene view before pressing Play.
/// (Select the object to see a white outline of its size.)
/// </summary>
[DisallowMultipleComponent]
public class FireVFX : MonoBehaviour
{
    [Header("Size")]
    [Tooltip("ON = Height, Radius and the other sizes are real world units, whatever the scale of the parent objects. " +
             "OFF = they are multiplied by the parents' scale (old behaviour). The presets turn this ON.")]
    public bool ignoreParentScale = false;
    [Tooltip("Height of the tallest flame tongue (world units).")]
    public float height = 1.5f;
    [Tooltip("Radius of the fire (world units). Pillar = big, lantern = tiny.")]
    public float radius = 0.42f;
    [Range(1, 12)]
    [Tooltip("Number of flame tongues. More = fuller fire.")]
    public int tongues = 8;
    [Tooltip("Height of the outer tongues as a fraction of the tallest one (min, max).")]
    public Vector2 outerHeight = new Vector2(0.5f, 0.78f);
    [Range(0f, 40f)]
    [Tooltip("How far the outer tongues lean outward (degrees).")]
    public float outerLean = 14f;

    [Header("Look (HDR colors)")]
    [ColorUsage(false, true)] public Color coreColor = new Color(1.7f, 0.75f, 0.15f);
    [ColorUsage(false, true)] public Color midColor = new Color(1.3f, 0.24f, 0.02f);
    [ColorUsage(false, true)] public Color tipColor = new Color(0.38f, 0.02f, 0.01f);
    [Tooltip("Overall brightness of the flame. With Bloom on, higher = bigger glow.")]
    public float intensity = 0.6f;
    public float noiseScale = 2.2f;
    public float riseSpeed = 1.6f;
    [Range(0f, 2f)][Tooltip("How ragged the flame is.")] public float erosion = 1.1f;
    [Range(0f, 0.9f)][Tooltip("Higher = thinner flame.")] public float cut = 0.34f;
    [Range(0.01f, 0.5f)] public float softness = 0.08f;
    [Tooltip("How much the tips move sideways.")]
    public float sway = 0.12f;
    [Range(1f, 6f)] public float celBands = 3f;
    [Range(0f, 1f)][Tooltip("0 = smooth gradient, 1 = hard painted bands.")] public float celAmount = 0.7f;

    [Header("Flicker")]
    public float flickerSpeed = 5f;
    [Range(0f, 0.6f)] public float flickerAmount = 0.2f;

    [Header("Light (secondary light source)")]
    public bool enableLight = true;
    [ColorUsage(false, false)] public Color lightColor = new Color(1f, 0.52f, 0.2f);
    public float lightIntensity = 4f;
    public float lightRange = 10f;
    [Range(0f, 1.5f)]
    [Tooltip("Height of the light above the base, as a fraction of the flame height.")]
    public float lightHeight = 0.45f;
    [Tooltip("Shadows cost performance. Try them on pillars only.")]
    public bool castShadows = false;

    [Header("Glow halo")]
    public bool enableHalo = true;
    [ColorUsage(false, true)] public Color haloColor = new Color(1.2f, 0.45f, 0.12f);
    [Tooltip("Size of the halo (world units).")]
    public float haloSize = 3.6f;
    public float haloIntensity = 0.3f;
    [Tooltip("Moves the halo toward the camera so it does not cut into the pillar or the ground.")]
    public float haloCameraOffset = 0.6f;

    [Header("Embers")]
    public bool enableEmbers = true;
    public float emberRate = 14f;
    public Vector2 emberSize = new Vector2(0.02f, 0.06f);
    public Vector2 emberLifetime = new Vector2(1f, 2.2f);
    public Vector2 emberSpeed = new Vector2(0.6f, 1.6f);
    [ColorUsage(false, true)] public Color emberColor = new Color(2f, 0.85f, 0.25f);

    [Header("Smoke")]
    public bool enableSmoke = true;
    public float smokeRate = 3f;
    public Vector2 smokeSize = new Vector2(0.5f, 1f);
    public Vector2 smokeLifetime = new Vector2(2.5f, 4f);
    [Tooltip("Dark, cool and transparent so it fits the misty forest.")]
    public Color smokeColor = new Color(0.22f, 0.26f, 0.34f, 0.16f);

    [Header("Materials (optional)")]
    [Tooltip("Only needed for built games, where Shader.Find can fail. Create a material with VFX/FireFlame and assign it here.")]
    public Material flameMaterial;
    [Tooltip("Same for VFX/FireParticle.")]
    public Material particleMaterial;

    [Header("Live tuning")]
    [Tooltip("In play mode, changing a value in the inspector rebuilds the fire.")]
    public bool rebuildOnChange = true;

    private static Mesh tongueMesh;
    private static Mesh quadMesh;
    private static Camera[] camBuffer = new Camera[8];

    private Transform root;
    private Material flameMat;
    private Material emberMat;
    private Material haloMat;
    private Material smokeMat;
    private Light fireLight;
    private Transform lightTf;
    private Vector3 lightBase;
    private Transform haloTf;
    private float seed;
    private bool dirty;
    private float sizeScale = 1f;

    // ------------------------------------------------------------------ presets
    private void Reset()
    {
        ApplyPillarPreset();
    }

    [ContextMenu("Apply Pillar Preset")]
    public void ApplyPillarPreset()
    {
        ignoreParentScale = true;
        height = 1.5f; radius = 0.42f; tongues = 8; outerHeight = new Vector2(0.5f, 0.78f); outerLean = 14f;
        intensity = 1.5f; sway = 0.12f; flickerSpeed = 5f; flickerAmount = 0.2f;
        enableLight = true; lightColor = new Color(1f, 0.52f, 0.2f); lightIntensity = 4f; lightRange = 10f; lightHeight = 0.45f;
        enableHalo = true; haloSize = 3.6f; haloIntensity = 0.3f; haloCameraOffset = 0.6f;
        enableEmbers = true; emberRate = 14f; emberSize = new Vector2(0.02f, 0.06f);
        emberLifetime = new Vector2(1f, 2.2f); emberSpeed = new Vector2(0.6f, 1.6f);
        enableSmoke = true; smokeRate = 3f; smokeSize = new Vector2(0.5f, 1f); smokeLifetime = new Vector2(2.5f, 4f);
    }

    [ContextMenu("Apply Lantern Preset")]
    public void ApplyLanternPreset()
    {
        ignoreParentScale = true;
        height = 0.22f; radius = 0.05f; tongues = 3; outerHeight = new Vector2(0.55f, 0.75f); outerLean = 10f;
        intensity = 3f; sway = 0.1f; flickerSpeed = 7f; flickerAmount = 0.3f;
        enableLight = true; lightColor = new Color(1f, 0.58f, 0.24f); lightIntensity = 1.4f; lightRange = 3.5f; lightHeight = 0.6f;
        enableHalo = true; haloSize = 0.85f; haloIntensity = 0.4f; haloCameraOffset = 0.15f;
        enableEmbers = true; emberRate = 1.5f; emberSize = new Vector2(0.006f, 0.016f);
        emberLifetime = new Vector2(0.6f, 1.2f); emberSpeed = new Vector2(0.1f, 0.3f);
        enableSmoke = false;
    }

    [ContextMenu("Rebuild Fire")]
    public void Rebuild()
    {
        if (!Application.isPlaying) return;
        Clear();
        Build();
    }

    // ------------------------------------------------------------------ lifetime
    private void OnEnable()
    {
        if (Application.isPlaying && root == null) Build();
    }

    private void OnDisable()
    {
        Clear();
    }

    private void OnValidate()
    {
        if (Application.isPlaying && rebuildOnChange) dirty = true;
    }

    private void Clear()
    {
        if (root != null) Destroy(root.gameObject);
        if (flameMat != null) Destroy(flameMat);
        if (emberMat != null) Destroy(emberMat);
        if (haloMat != null) Destroy(haloMat);
        if (smokeMat != null) Destroy(smokeMat);
        root = null;
        fireLight = null;
        lightTf = null;
        haloTf = null;
    }

    // ------------------------------------------------------------------ build
    private void Build()
    {
        Shader flameShader = flameMaterial != null ? flameMaterial.shader : Shader.Find("VFX/FireFlame");
        Shader particleShader = particleMaterial != null ? particleMaterial.shader : Shader.Find("VFX/FireParticle");
        if (flameShader == null || particleShader == null)
        {
            Debug.LogError("[FireVFX] Shader not found. Make sure VFX_FireFlame.shader and VFX_FireParticle.shader are in the project " +
                           "(for built games assign materials in the 'Materials' section).", this);
            return;
        }

        seed = (GetInstanceID() & 0xFFFF) * 0.001f;
        root = new GameObject("FireVFX_Generated").transform;
        root.SetParent(transform, false);

        // sizes in real world units, whatever the scale of the parents
        Vector3 ls = transform.lossyScale;
        if (ignoreParentScale)
        {
            root.localScale = new Vector3(SafeInverse(ls.x), SafeInverse(ls.y), SafeInverse(ls.z));
            sizeScale = 1f;
        }
        else
        {
            sizeScale = Mathf.Abs(ls.y);
            if (Mathf.Abs(ls.x - 1f) > 0.25f || Mathf.Abs(ls.y - 1f) > 0.25f || Mathf.Abs(ls.z - 1f) > 0.25f)
            {
                Debug.LogWarning("[FireVFX] '" + name + "' sits under scaled parents (world scale " + ls.ToString("F3") +
                                 "), so Height, Radius and the other sizes are multiplied by it. Tick 'Ignore Parent Scale' " +
                                 "to type real world sizes instead.", this);
            }
        }

        // ---- materials (one set per fire, so different fires can look different)
        flameMat = flameMaterial != null ? new Material(flameMaterial) : new Material(flameShader);
        flameMat.SetColor("_ColorCore", coreColor);
        flameMat.SetColor("_ColorMid", midColor);
        flameMat.SetColor("_ColorTip", tipColor);
        flameMat.SetFloat("_Intensity", intensity);
        flameMat.SetFloat("_NoiseScale", noiseScale);
        flameMat.SetFloat("_RiseSpeed", riseSpeed);
        flameMat.SetFloat("_Erosion", erosion);
        flameMat.SetFloat("_Cut", cut);
        flameMat.SetFloat("_Softness", softness);
        flameMat.SetFloat("_Sway", sway);
        flameMat.SetFloat("_Bands", celBands);
        flameMat.SetFloat("_Cel", celAmount);

        emberMat = MakeParticleMaterial(particleShader, true, emberColor, 1f, 1.2f);
        haloMat = MakeParticleMaterial(particleShader, true, haloColor, haloIntensity, 2.2f);
        smokeMat = MakeParticleMaterial(particleShader, false, Color.white, 1f, 1f);

        BuildFlames();
        if (enableLight) BuildLight();
        if (enableHalo) BuildHalo();
        if (enableEmbers) BuildEmbers();
        if (enableSmoke) BuildSmoke();
    }

    private static float SafeInverse(float v)
    {
        return Mathf.Abs(v) > 0.00001f ? 1f / v : 1f;
    }

    private static Material MakeParticleMaterial(Shader shader, bool additive, Color color, float intensity, float power)
    {
        Material m = new Material(shader);
        m.SetColor("_Color", color);
        m.SetFloat("_Intensity", intensity);
        m.SetFloat("_Power", power);
        m.SetFloat("_Additive", additive ? 1f : 0f);
        m.SetFloat("_SrcBlend", additive ? (float)BlendMode.One : (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
        return m;
    }

    private void BuildFlames()
    {
        System.Random rng = new System.Random(GetInstanceID());
        Mesh mesh = GetTongueMesh();

        // the tallest tongue in the middle
        MakeTongue(mesh, Vector3.zero, Quaternion.identity, radius * 0.75f, height, (float)rng.NextDouble() * 10f);

        // the others in a ring around it, leaning outward
        int outer = Mathf.Max(0, tongues - 1);
        for (int i = 0; i < outer; i++)
        {
            float angle = (i / (float)Mathf.Max(outer, 1)) * Mathf.PI * 2f + ((float)rng.NextDouble() - 0.5f) * 0.6f;
            Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 pos = dir * (radius * 0.8f);

            float lean = outerLean + ((float)rng.NextDouble() - 0.5f) * 10f;
            Quaternion rot = Quaternion.AngleAxis(lean, Vector3.Cross(Vector3.up, dir));

            float h = height * Mathf.Lerp(outerHeight.x, outerHeight.y, (float)rng.NextDouble());
            MakeTongue(mesh, pos, rot, radius * 0.42f, h, (float)rng.NextDouble() * 10f);
        }
    }

    private void MakeTongue(Mesh mesh, Vector3 localPos, Quaternion localRot, float baseRadius, float tongueHeight, float tongueSeed)
    {
        GameObject go = new GameObject("Tongue");
        go.transform.SetParent(root, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;
        go.transform.localScale = new Vector3(baseRadius, tongueHeight, baseRadius);

        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = flameMat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;

        MaterialPropertyBlock block = new MaterialPropertyBlock();
        block.SetFloat("_Seed", tongueSeed + seed);
        mr.SetPropertyBlock(block);
    }

    private void BuildLight()
    {
        GameObject go = new GameObject("FireLight");
        go.transform.SetParent(root, false);
        lightBase = new Vector3(0f, height * lightHeight, 0f);
        go.transform.localPosition = lightBase;
        lightTf = go.transform;

        fireLight = go.AddComponent<Light>();
        fireLight.type = LightType.Point;
        fireLight.color = lightColor;
        fireLight.intensity = lightIntensity;
        fireLight.range = lightRange;
        fireLight.shadows = castShadows ? LightShadows.Soft : LightShadows.None;
    }

    private void BuildHalo()
    {
        GameObject go = new GameObject("Halo");
        go.transform.SetParent(root, false);
        go.AddComponent<MeshFilter>().sharedMesh = GetQuadMesh();
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = haloMat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        go.transform.localScale = Vector3.one * haloSize;
        haloTf = go.transform;
    }

    private void BuildEmbers()
    {
        ParticleSystem ps = CreateSystem("Embers", new Vector3(0f, height * 0.1f, 0f));

        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.duration = 5f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(emberLifetime.x, emberLifetime.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(emberSpeed.x, emberSpeed.y);
        main.startSize = new ParticleSystem.MinMaxCurve(emberSize.x, emberSize.y);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.8f, 0.35f), new Color(1f, 0.45f, 0.1f));
        main.gravityModifier = -0.03f;
        main.maxParticles = 150;

        ParticleSystem.EmissionModule em = ps.emission;
        em.enabled = true;
        em.rateOverTime = emberRate;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 15f;
        shape.radius = Mathf.Max(0.01f, radius * 0.6f);
        shape.rotation = new Vector3(-90f, 0f, 0f);   // emit upward

        ParticleSystem.NoiseModule noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.7f;
        noise.frequency = 0.5f;
        noise.scrollSpeed = 0.5f;
        noise.quality = ParticleSystemNoiseQuality.Low;

        ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        SetupRenderer(ps, emberMat);
    }

    private void BuildSmoke()
    {
        ParticleSystem ps = CreateSystem("Smoke", new Vector3(0f, height * 0.85f, 0f));

        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.duration = 5f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(smokeLifetime.x, smokeLifetime.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.35f, 0.75f);
        main.startSize = new ParticleSystem.MinMaxCurve(smokeSize.x, smokeSize.y);
        main.startColor = smokeColor;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = -0.02f;
        main.maxParticles = 60;

        ParticleSystem.EmissionModule em = ps.emission;
        em.enabled = true;
        em.rateOverTime = smokeRate;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 12f;
        shape.radius = Mathf.Max(0.01f, radius * 0.4f);
        shape.rotation = new Vector3(-90f, 0f, 0f);

        ParticleSystem.NoiseModule noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.35f;
        noise.frequency = 0.3f;
        noise.scrollSpeed = 0.2f;
        noise.quality = ParticleSystemNoiseQuality.Low;

        ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.6f));

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        SetupRenderer(ps, smokeMat);
    }

    private ParticleSystem CreateSystem(string name, Vector3 localPos)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(root, false);
        go.transform.localPosition = localPos;
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        return ps;
    }

    private static void SetupRenderer(ParticleSystem ps, Material mat)
    {
        ParticleSystemRenderer r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        ps.Play();
    }

    // ------------------------------------------------------------------ run
    private void Update()
    {
        if (dirty)
        {
            dirty = false;
            Rebuild();
            return;
        }
        if (root == null) return;

        // two layers of noise: a slow, big breathing and a fast, small shimmer
        float t = Time.time;
        float n1 = Mathf.PerlinNoise(t * flickerSpeed, seed);
        float n2 = Mathf.PerlinNoise(t * flickerSpeed * 2.7f, seed + 11.3f);
        float f = 1f + (n1 - 0.5f) * 2f * flickerAmount + (n2 - 0.5f) * flickerAmount * 0.8f;
        f = Mathf.Max(0.3f, f);

        if (flameMat != null) flameMat.SetFloat("_Flicker", f);

        if (fireLight != null)
        {
            fireLight.intensity = lightIntensity * f;
            // the light also moves a little, which makes the light on the ground dance
            lightTf.localPosition = lightBase + new Vector3(n1 - 0.5f, 0f, n2 - 0.5f) * (height * 0.1f);
        }

        if (haloMat != null) haloMat.SetFloat("_Intensity", haloIntensity * f);
    }

    private void LateUpdate()
    {
        if (haloTf == null) return;

        Vector3 center = transform.position + Vector3.up * (height * 0.45f * sizeScale);
        Camera cam = PickCamera(center);
        if (cam == null) return;

        // face the camera, and sit a little in front of the fire so it never cuts into geometry
        haloTf.rotation = Quaternion.LookRotation(cam.transform.forward, cam.transform.up);
        haloTf.position = center + (cam.transform.position - center).normalized * haloCameraOffset;
    }

    private void OnDrawGizmosSelected()
    {
        // shows the real size of the fire (taking the "Ignore Parent Scale" option into account)
        float sc = ignoreParentScale ? 1f : Mathf.Abs(transform.lossyScale.y);
        Gizmos.color = new Color(1f, 0.6f, 0.2f, 1f);
        Gizmos.DrawWireSphere(transform.position, radius * sc);
        Gizmos.DrawWireSphere(transform.position + Vector3.up * (height * sc), radius * sc * 0.2f);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * (height * sc));
        if (enableLight) Gizmos.DrawWireSphere(transform.position + Vector3.up * (height * sc * lightHeight), 0.05f * Mathf.Max(sc, 0.2f));
    }

    // ------------------------------------------------------------------ meshes
    // A flame tongue: 1 unit tall, base radius 1 (scaled by the transform). U goes around, V goes from base to tip.
    private static Mesh GetTongueMesh()
    {
        if (tongueMesh != null) return tongueMesh;

        const int radial = 12;
        const int rows = 10;
        Vector3[] verts = new Vector3[(radial + 1) * (rows + 1)];
        Vector2[] uvs = new Vector2[verts.Length];
        int[] tris = new int[radial * rows * 6];

        for (int j = 0; j <= rows; j++)
        {
            float v = j / (float)rows;
            float r = Mathf.Pow(1f - v, 0.9f) * (0.65f + 0.35f * Mathf.Sin(Mathf.Clamp01(v * 2.2f) * Mathf.PI * 0.5f));
            for (int i = 0; i <= radial; i++)
            {
                float u = i / (float)radial;
                float ang = u * Mathf.PI * 2f;
                int idx = j * (radial + 1) + i;
                verts[idx] = new Vector3(Mathf.Cos(ang) * r, v, Mathf.Sin(ang) * r);
                uvs[idx] = new Vector2(u, v);
            }
        }

        int t = 0;
        for (int j = 0; j < rows; j++)
        {
            for (int i = 0; i < radial; i++)
            {
                int a = j * (radial + 1) + i;
                int b = a + 1;
                int c = a + radial + 1;
                int d = c + 1;
                tris[t++] = a; tris[t++] = c; tris[t++] = b;
                tris[t++] = b; tris[t++] = c; tris[t++] = d;
            }
        }

        tongueMesh = new Mesh();
        tongueMesh.name = "FireTongue";
        tongueMesh.vertices = verts;
        tongueMesh.uv = uvs;
        tongueMesh.triangles = tris;
        tongueMesh.RecalculateNormals();
        tongueMesh.RecalculateBounds();
        return tongueMesh;
    }

    private static Mesh GetQuadMesh()
    {
        if (quadMesh != null) return quadMesh;

        quadMesh = new Mesh();
        quadMesh.name = "FireQuad";
        quadMesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f)
        };
        quadMesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
        quadMesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        quadMesh.RecalculateNormals();
        quadMesh.RecalculateBounds();
        return quadMesh;
    }

    private static Camera PickCamera(Vector3 pos)
    {
        int count = Camera.allCamerasCount;
        if (count > camBuffer.Length) camBuffer = new Camera[count];
        count = Camera.GetAllCameras(camBuffer);

        Camera best = null;
        float bestDist = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            Camera c = camBuffer[i];
            if (c == null || !c.isActiveAndEnabled) continue;

            float d = (c.transform.position - pos).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                best = c;
            }
        }
        return best;
    }
}