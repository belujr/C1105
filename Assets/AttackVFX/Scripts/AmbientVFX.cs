using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Ambient atmosphere that follows the player, so the forest feels alive and lit. Needs no extra models or textures.
///   SPIRIT MOTES  slowly wandering, twinkling glowing dots (fireflies). They fit your soul-energy theme.
///   DUST          tiny pale specks drifting in the air
///   GROUND MIST   low, large, very soft wisps that drift along the ground
///   LIGHT SHAFTS  soft beams of moonlight / sunlight slanting down through the canopy
///
/// Setup: create ONE empty GameObject in the scene, add this component, press Play.
/// It finds the player by itself (or drag the player into "Follow").
/// Changes you make in the inspector while playing rebuild the effects live.
/// </summary>
[DisallowMultipleComponent]
public class AmbientVFX : MonoBehaviour
{
    [Header("General")]
    [Tooltip("What the effects follow. Empty = the player (found automatically), or the main camera.")]
    public Transform follow;
    [Tooltip("Size of the area around the target that is filled with motes and dust (world units).")]
    public Vector3 area = new Vector3(26f, 6f, 26f);

    [Header("Spirit motes (fireflies)")]
    public bool enableMotes = true;
    public int moteCount = 60;
    public Vector2 moteSize = new Vector2(0.05f, 0.11f);
    [Tooltip("Lowest and highest height above the ground.")]
    public Vector2 moteHeight = new Vector2(0.5f, 3.2f);
    public Color moteColorA = new Color(1f, 0.85f, 0.45f);
    public Color moteColorB = new Color(0.55f, 1f, 0.9f);
    [Tooltip("Brightness. With Bloom on, higher = bigger glow.")]
    public float moteIntensity = 2.5f;
    [Range(0f, 1.5f)] [Tooltip("How much each mote twinkles (its size breathes).")]
    public float moteTwinkle = 0.8f;

    [Header("Dust")]
    public bool enableDust = true;
    public int dustCount = 120;
    public Vector2 dustSize = new Vector2(0.015f, 0.035f);
    public Vector2 dustHeight = new Vector2(0.2f, 4f);
    public Color dustColor = new Color(0.75f, 0.85f, 1f, 0.6f);
    public float dustIntensity = 1.3f;

    [Header("Ground mist")]
    public bool enableMist = true;
    public int mistCount = 16;
    public Vector2 mistSize = new Vector2(3f, 6f);
    public Vector2 mistHeight = new Vector2(0.1f, 0.9f);
    [Tooltip("Cool and very transparent so it sits well in the blue forest. The last number is the opacity.")]
    public Color mistColor = new Color(0.55f, 0.68f, 0.85f, 0.07f);

    [Header("Light shafts")]
    public bool enableShafts = true;
    public int shaftCount = 6;
    public Vector2 shaftWidth = new Vector2(0.8f, 2.2f);
    [Tooltip("Length of a beam (world units).")]
    public float shaftLength = 11f;
    [Tooltip("Seconds a beam stays, then it fades out and a new one appears somewhere else.")]
    public Vector2 shaftLifetime = new Vector2(9f, 15f);
    [Tooltip("Seconds to fade in and out.")]
    public float shaftFade = 3f;
    public float shaftIntensity = 0.5f;
    [ColorUsage(false, true)] public Color shaftColor = new Color(0.55f, 0.75f, 1.1f);
    [Tooltip("Beams appear within this distance of the target.")]
    public float shaftRadius = 15f;
    [Tooltip("...but not closer than this, so they never cover the player.")]
    public float shaftMinDistance = 3f;
    [Range(0f, 30f)] [Tooltip("Random tilt of each beam (degrees).")]
    public float shaftTilt = 8f;

    [Header("Materials (optional, needed for built games)")]
    public Material particleMaterial;   // a material with VFX/FireParticle
    public Material shaftMaterial;      // a material with VFX/LightShaft

    [Header("Live tuning")]
    public bool rebuildOnChange = true;

    private class Shaft
    {
        public Transform tf;
        public MeshRenderer mr;
        public Vector3 ground;
        public float t0;
        public float life;
        public float width;
        public float seed;
        public Vector3 dir;
    }

    private static Camera[] camBuffer = new Camera[8];
    private static Mesh quadMesh;

    private Transform root;
    private Transform target;
    private readonly List<Shaft> shafts = new List<Shaft>();
    private readonly List<Material> madeMaterials = new List<Material>();
    private Material shaftMat;
    private MaterialPropertyBlock block;
    private Vector3 baseLightDir;
    private bool dirty;

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

    [ContextMenu("Rebuild")]
    public void Rebuild()
    {
        if (!Application.isPlaying) return;
        Clear();
        Build();
    }

    private void Clear()
    {
        if (root != null) Destroy(root.gameObject);
        for (int i = 0; i < madeMaterials.Count; i++)
            if (madeMaterials[i] != null) Destroy(madeMaterials[i]);
        madeMaterials.Clear();
        shafts.Clear();
        root = null;
    }

    // ------------------------------------------------------------------ build
    private void Build()
    {
        Shader particleShader = particleMaterial != null ? particleMaterial.shader : Shader.Find("VFX/FireParticle");
        Shader shaftShader = shaftMaterial != null ? shaftMaterial.shader : Shader.Find("VFX/LightShaft");
        if (particleShader == null || shaftShader == null)
        {
            Debug.LogError("[AmbientVFX] Shader not found. Make sure VFX_FireParticle.shader and VFX_LightShaft.shader are in the project " +
                           "(for built games assign materials in the 'Materials' section).", this);
            return;
        }

        block = new MaterialPropertyBlock();
        target = ResolveTarget();
        root = new GameObject("AmbientVFX_Generated").transform;
        root.SetParent(null);
        FollowTarget();

        if (enableMotes)
        {
            Material m = MakeParticleMaterial(particleShader, true, Color.white, moteIntensity, 1.6f);
            ParticleSystem ps = BuildLayer("SpiritMotes", moteCount, new Vector2(7f, 12f), new Vector2(0.05f, 0.25f),
                                           moteSize, moteHeight, m);
            ParticleSystem.MainModule main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(moteColorA, moteColorB);
            ParticleSystem.NoiseModule noise = ps.noise;
            noise.strength = 0.9f;
            noise.frequency = 0.25f;
            noise.scrollSpeed = 0.2f;
            noise.sizeAmount = moteTwinkle;   // the noise also makes each mote's size breathe = twinkle
        }

        if (enableDust)
        {
            Material m = MakeParticleMaterial(particleShader, true, Color.white, dustIntensity, 1.3f);
            ParticleSystem ps = BuildLayer("Dust", dustCount, new Vector2(8f, 14f), new Vector2(0.02f, 0.1f),
                                           dustSize, dustHeight, m);
            ParticleSystem.MainModule main = ps.main;
            main.startColor = dustColor;
            ParticleSystem.NoiseModule noise = ps.noise;
            noise.strength = 0.4f;
            noise.frequency = 0.3f;
            noise.scrollSpeed = 0.15f;
            noise.sizeAmount = 0.3f;
        }

        if (enableMist)
        {
            Material m = MakeParticleMaterial(particleShader, false, Color.white, 1f, 1f);
            ParticleSystem ps = BuildLayer("GroundMist", mistCount, new Vector2(14f, 20f), new Vector2(0.1f, 0.3f),
                                           mistSize, mistHeight, m);
            ParticleSystem.MainModule main = ps.main;
            main.startColor = mistColor;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            ParticleSystem.NoiseModule noise = ps.noise;
            noise.strength = 0.15f;
            noise.frequency = 0.15f;
            noise.scrollSpeed = 0.05f;
            noise.sizeAmount = 0f;
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.8f, 1f, 1.25f));
        }

        if (enableShafts) BuildShafts(shaftShader);
    }

    private Transform ResolveTarget()
    {
        if (follow != null) return follow;

#if UNITY_2023_1_OR_NEWER
        PlayerController pc = FindFirstObjectByType<PlayerController>();
#else
        PlayerController pc = FindObjectOfType<PlayerController>();
#endif
        if (pc != null) return pc.transform;

        Camera c = Camera.main;
        return c != null ? c.transform : null;
    }

    private Material MakeParticleMaterial(Shader shader, bool additive, Color color, float intensity, float power)
    {
        Material m = particleMaterial != null && additive == (particleMaterial.GetFloat("_Additive") > 0.5f)
            ? new Material(particleMaterial)
            : new Material(shader);
        m.SetColor("_Color", color);
        m.SetFloat("_Intensity", intensity);
        m.SetFloat("_Power", power);
        m.SetFloat("_Additive", additive ? 1f : 0f);
        m.SetFloat("_SrcBlend", additive ? (float)BlendMode.One : (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
        madeMaterials.Add(m);
        return m;
    }

    // one ParticleSystem that fills the area around the target
    private ParticleSystem BuildLayer(string name, int count, Vector2 lifetime, Vector2 speed, Vector2 size,
                                      Vector2 height, Material mat)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(root, false);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        float avgLife = (lifetime.x + lifetime.y) * 0.5f;

        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.prewarm = true;                       // the air is already full when the game starts
        main.duration = 5f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime.x, lifetime.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
        main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
        main.maxParticles = Mathf.Max(8, Mathf.CeilToInt(count * 1.3f));

        ParticleSystem.EmissionModule em = ps.emission;
        em.enabled = true;
        em.rateOverTime = count / Mathf.Max(avgLife, 0.1f);

        float h = Mathf.Max(0.1f, height.y - height.x);
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(area.x, h, area.z);
        shape.position = new Vector3(0f, height.x + h * 0.5f, 0f);

        // wander slowly, in every direction
        ParticleSystem.NoiseModule noise = ps.noise;
        noise.enabled = true;
        noise.quality = ParticleSystemNoiseQuality.Low;
        noise.damping = false;

        // fade in and out so particles never pop
        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.18f), new GradientAlphaKey(1f, 0.82f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        ParticleSystemRenderer r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;

        ps.Play();
        return ps;
    }

    // ------------------------------------------------------------------ light shafts
    private void BuildShafts(Shader shaftShader)
    {
        shaftMat = shaftMaterial != null ? new Material(shaftMaterial) : new Material(shaftShader);
        shaftMat.SetColor("_Color", shaftColor);
        madeMaterials.Add(shaftMat);

        baseLightDir = GetLightDirection();

        for (int i = 0; i < shaftCount; i++)
        {
            GameObject go = new GameObject("Shaft");
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = GetQuadMesh();
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = shaftMat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;

            Shaft s = new Shaft { tf = go.transform, mr = mr };
            RespawnShaft(s, Random.value * 0.9f); // start at different points of their life, so they don't all fade together
            shafts.Add(s);
        }
    }

    // the direction the light travels (from the sky to the ground)
    private Vector3 GetLightDirection()
    {
        Light sun = RenderSettings.sun;
        if (sun == null)
        {
#if UNITY_2023_1_OR_NEWER
            Light[] lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
#else
            Light[] lights = FindObjectsOfType<Light>();
#endif
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i].type == LightType.Directional) { sun = lights[i]; break; }
            }
        }

        Vector3 d = sun != null ? sun.transform.forward : new Vector3(0.25f, -1f, 0.15f);
        // beams too close to horizontal look wrong, keep them clearly slanted
        if (d.y > -0.4f) d = new Vector3(d.x, -0.4f, d.z);
        return d.normalized;
    }

    private void RespawnShaft(Shaft s, float startFraction)
    {
        Vector3 center = target != null ? target.position : transform.position;

        Vector2 disc = Random.insideUnitCircle;
        if (disc.sqrMagnitude < 0.0001f) disc = Vector2.right;
        float dist = Mathf.Lerp(shaftMinDistance, shaftRadius, Mathf.Sqrt(Random.value));
        Vector2 offset = disc.normalized * dist;

        s.ground = new Vector3(center.x + offset.x, center.y, center.z + offset.y);
        s.life = Random.Range(shaftLifetime.x, shaftLifetime.y);
        s.t0 = Time.time - s.life * startFraction;
        s.width = Random.Range(shaftWidth.x, shaftWidth.y);
        s.seed = Random.value * 10f;

        Quaternion tilt = Quaternion.Euler(Random.Range(-shaftTilt, shaftTilt), 0f, Random.Range(-shaftTilt, shaftTilt));
        s.dir = (tilt * baseLightDir).normalized;
    }

    private void UpdateShafts()
    {
        if (shafts.Count == 0) return;

        Vector3 center = target != null ? target.position : transform.position;

        for (int i = 0; i < shafts.Count; i++)
        {
            Shaft s = shafts[i];
            float age = Time.time - s.t0;

            // too old, or the player walked far away: fade out and appear somewhere else
            if (age >= s.life || (s.ground - center).sqrMagnitude > (shaftRadius * 1.6f) * (shaftRadius * 1.6f))
            {
                RespawnShaft(s, 0f);
                age = 0f;
            }

            float fadeIn = Mathf.SmoothStep(0f, 1f, age / Mathf.Max(shaftFade, 0.1f));
            float fadeOut = 1f - Mathf.SmoothStep(0f, 1f, (age - (s.life - shaftFade)) / Mathf.Max(shaftFade, 0.1f));
            float env = Mathf.Clamp01(Mathf.Min(fadeIn, fadeOut));

            // the strip lies along the beam, faces the camera, and its lower end sits on the ground
            Vector3 up = -s.dir;   // from the ground toward the sky, along the beam
            Camera cam = PickCamera(s.ground);
            Vector3 toCam = cam != null ? (cam.transform.position - s.ground) : Vector3.back;
            Vector3 normal = Vector3.ProjectOnPlane(toCam, up);
            if (normal.sqrMagnitude < 0.0001f) normal = Vector3.ProjectOnPlane(Vector3.forward, up);

            s.tf.rotation = Quaternion.LookRotation(normal.normalized, up);
            s.tf.position = s.ground + up * (shaftLength * 0.5f);
            s.tf.localScale = new Vector3(s.width, shaftLength, 1f);

            s.mr.GetPropertyBlock(block);
            block.SetFloat("_Intensity", shaftIntensity * env);
            block.SetFloat("_Seed", s.seed);
            s.mr.SetPropertyBlock(block);
        }
    }

    // ------------------------------------------------------------------ run
    private void FollowTarget()
    {
        if (root == null) return;
        if (target == null) target = ResolveTarget();
        if (target != null) root.position = target.position;
    }

    private void LateUpdate()
    {
        if (dirty)
        {
            dirty = false;
            Rebuild();
            return;
        }
        if (root == null) return;

        FollowTarget();
        UpdateShafts();
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.5f, 0.9f, 1f, 0.6f);
        Vector3 c = (follow != null ? follow.position : transform.position);
        Gizmos.DrawWireCube(c + Vector3.up * (area.y * 0.5f), area);
    }

    // ------------------------------------------------------------------ helpers
    private static Mesh GetQuadMesh()
    {
        if (quadMesh != null) return quadMesh;

        quadMesh = new Mesh();
        quadMesh.name = "AmbientQuad";
        // centered horizontally, V = 0 at the bottom (ground end) and 1 at the top
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
