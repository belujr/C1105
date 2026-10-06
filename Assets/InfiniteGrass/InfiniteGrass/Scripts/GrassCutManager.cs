using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// Sweeps the grass (and optionally flowers) away inside an arc. The cut starts at the player and travels outward fast.
/// Put ONE of these in the scene (for example on the same object as InfiniteGrassRenderer).
/// CombatHitboxController calls GrassCutManager.Cut(...) when an AOE attack lands.
///
/// Look: when the cut front reaches a blade, that blade whips outward, flies off and shrinks away.
/// Grass debris only spawns where grass really grows (it reads your terrain's grass layers). Then everything regrows.
public class GrassCutManager : MonoBehaviour
{
    public const int MaxCuts = 8;

    [Header("Sweep")]
    [Tooltip("Seconds for the cut front to travel from the player out to the full attack radius.")]
    [SerializeField] private float sweepTime = 0.2f;
    [Tooltip("Seconds each blade takes to whip outward and shrink away once the front reaches it.")]
    [SerializeField] private float blowTime = 0.3f;
    [Tooltip("Makes the leading edge ragged instead of a perfect arc (world units). 0 = clean arc.")]
    [SerializeField] private float raggedEdge = 1f;

    [Header("After the Cut")]
    [Tooltip("Seconds the grass stays gone. Use a huge number (like 9999) to keep it cut.")]
    [SerializeField] private float stayCutSeconds = 8f;
    [Tooltip("Seconds the grass takes to grow back. It regrows from tiny to full height.")]
    [SerializeField] private float regrowSeconds = 4f;

    [Header("Look: Shockwave")]
    [Tooltip("How far blades are blown over (flattened outward) when the front hits them. 0 = off.")]
    [SerializeField] private float shockwaveBendAmount = 3f;
    [Tooltip("How far (world units) each blade flies outward and upward while it shrinks away. 0 = off.")]
    [SerializeField] private float flyDistance = 1.2f;
    [Tooltip("Color of the flash on blades as the front hits them. HDR: raise the intensity for bloom.")]
    [SerializeField, ColorUsage(false, true)] private Color glowColor = new Color(0.7f, 1.8f, 0.5f);
    [Tooltip("0 = no glow.")]
    [SerializeField] private float glowIntensity = 1f;

    [Header("Look: Flying Grass Debris")]
    [SerializeField] private bool spawnDebris = true;
    [Tooltip("Only spawn debris where grass really grows. Uses the Terrain's grass layers, so it needs a Terrain.")]
    [SerializeField] private bool debrisOnlyOnGrass = true;
    [Tooltip("Optional. Any particle material, for example with your leaf or blade sprite. Empty = a plain streak.")]
    [SerializeField] private Material debrisMaterial;
    [Tooltip("Stretches each particle along its direction of travel. Leave OFF for a sprite, otherwise it gets squashed.")]
    [SerializeField] private bool stretchWithVelocity = false;
    [Tooltip("Width / height of a particle. 1 = square. Lower makes it thinner.")]
    [SerializeField] private float debrisAspect = 1f;
    [Tooltip("Particles per square meter of grass swept. 3 is juicy, 1 is subtle.")]
    [SerializeField] private float debrisDensity = 3f;
    [Tooltip("Particle tint is a random mix of these two. Set both to white to keep your sprite's own colors.")]
    [SerializeField] private Color debrisColorA = new Color(0.25f, 0.45f, 0.12f);
    [SerializeField] private Color debrisColorB = new Color(0.7f, 0.85f, 0.3f);
    [Tooltip("Min / max size in world units.")]
    [SerializeField] private Vector2 debrisSizeRange = new Vector2(0.35f, 0.7f);
    [Tooltip("Min / max speed outward, away from the player.")]
    [SerializeField] private Vector2 debrisOutSpeed = new Vector2(2f, 6f);
    [Tooltip("Min / max speed upward.")]
    [SerializeField] private Vector2 debrisUpSpeed = new Vector2(2.5f, 6f);
    [Tooltip("Min / max seconds alive.")]
    [SerializeField] private Vector2 debrisLifetime = new Vector2(0.5f, 1.1f);
    [SerializeField] private float debrisGravity = 1.5f;
    [SerializeField] private int maxDebris = 800;

    [Header("Options")]
    [Tooltip("Flowers can't shrink, so they simply disappear when the cut reaches them.")]
    [SerializeField] private bool affectFlowers = true;
    [Tooltip("Optional whoosh played where the attack starts.")]
    [SerializeField] private AudioClip cutSound;
    [SerializeField, Range(0f, 1f)] private float cutSoundVolume = 0.7f;

    [Header("Testing (Play mode: right-click this component > Test Cut)")]
    [Tooltip("Drag the player here.")]
    [SerializeField] private Transform testOrigin;
    [SerializeField] private float testRadius = 6f;
    [SerializeField] private float testConeAngle = 120f;

    private class CutData
    {
        public Vector2 origin;
        public Vector2 dir;
        public float radius;
        public float cosHalf;
        public float startTime;
        public float emittedFront;
        public float debrisRemainder;

        // terrain grass weights around this cut (for the "only where grass grows" debris check)
        public float[,,] alpha;
        public int ax0, az0, aw, ah, layers, grassLayer, mixedLayer, res;
        public float terrainX, terrainZ, sizeX, sizeZ;
    }

    private static GrassCutManager instance;
    private readonly List<CutData> cuts = new List<CutData>();
    private ParticleSystem debris;
    private bool warnedNoTerrain;

    private static readonly Vector4[] cutA = new Vector4[MaxCuts];   // xy = origin XZ, zw = forward XZ
    private static readonly Vector4[] cutB = new Vector4[MaxCuts];   // x = radius, y = cos(half angle), z = age

    private static readonly int CutAId = Shader.PropertyToID("_GrassCutA");
    private static readonly int CutBId = Shader.PropertyToID("_GrassCutB");
    private static readonly int CountId = Shader.PropertyToID("_GrassCutCount");        // compute shader (int)
    private static readonly int CountFId = Shader.PropertyToID("_GrassCutCountF");      // blade shader (float)
    private static readonly int TimingsId = Shader.PropertyToID("_GrassCutTimings");
    private static readonly int FlowersId = Shader.PropertyToID("_GrassCutFlowers");
    private static readonly int JitterId = Shader.PropertyToID("_GrassCutJitter");
    private static readonly int VisualId = Shader.PropertyToID("_GrassCutVisual");
    private static readonly int GlowColorId = Shader.PropertyToID("_GrassCutGlowColor");

    private void OnEnable() { instance = this; }

    private void OnDisable()
    {
        if (instance == this) instance = null;
        Shader.SetGlobalFloat(CountFId, 0f);
    }

    /// Cut the grass in a cone in front of 'origin'. Does nothing if there is no GrassCutManager in the scene.
    public static void Cut(Vector3 origin, Vector3 forward, float radius, float coneAngleDegrees)
    {
        if (instance == null) return;
        instance.AddCut(origin, forward, radius, coneAngleDegrees);
    }

    private void AddCut(Vector3 origin, Vector3 forward, float radius, float coneAngleDegrees)
    {
        Vector2 dir = new Vector2(forward.x, forward.z);
        if (dir.sqrMagnitude < 0.0001f) dir = Vector2.up; else dir.Normalize();

        CutData c = new CutData
        {
            origin = new Vector2(origin.x, origin.z),
            dir = dir,
            radius = radius,
            cosHalf = Mathf.Cos(Mathf.Clamp(coneAngleDegrees * 0.5f, 0f, 180f) * Mathf.Deg2Rad),
            startTime = Time.time
        };

        if (spawnDebris && debrisOnlyOnGrass) CacheTerrainWeights(c);

        if (cuts.Count >= MaxCuts) cuts.RemoveAt(0);   // replace the oldest one
        cuts.Add(c);

        if (cutSound != null) AudioSource.PlayClipAtPoint(cutSound, origin, cutSoundVolume);
    }

    private void Update()
    {
        float lifetime = sweepTime + blowTime + stayCutSeconds + regrowSeconds;
        for (int i = cuts.Count - 1; i >= 0; i--)
        {
            CutData c = cuts[i];
            float age = Time.time - c.startTime;
            if (age > lifetime) { cuts.RemoveAt(i); continue; }
            if (spawnDebris && age <= sweepTime + 0.1f) EmitDebris(c, age);
        }

        PushGlobals();
    }

    [ContextMenu("Test Cut")]
    private void TestCut()
    {
        if (testOrigin == null)
        {
            Debug.LogWarning("[GrassCutManager] Drag the player into 'Test Origin' first.", this);
            return;
        }
        AddCut(testOrigin.position, testOrigin.forward, testRadius, testConeAngle);
    }

    // ---------------------------------------------------------------- shader data

    private int FillArrays()
    {
        int n = Mathf.Min(cuts.Count, MaxCuts);
        float now = Time.time;
        for (int i = 0; i < n; i++)
        {
            CutData c = cuts[i];
            cutA[i] = new Vector4(c.origin.x, c.origin.y, c.dir.x, c.dir.y);
            cutB[i] = new Vector4(c.radius, c.cosHalf, now - c.startTime, 0f);
        }
        return n;
    }

    // x = sweep time, y = stay-cut time, z = regrow time, w = blow time
    private Vector4 Timings()
    {
        return new Vector4(sweepTime, stayCutSeconds, regrowSeconds, blowTime);
    }

    // The grass BLADE shader reads these as globals (blades whip outward, fly off, flash)
    private void PushGlobals()
    {
        int n = FillArrays();
        Shader.SetGlobalFloat(CountFId, n);
        if (n == 0) return;

        Shader.SetGlobalVectorArray(CutAId, cutA);
        Shader.SetGlobalVectorArray(CutBId, cutB);
        Shader.SetGlobalVector(TimingsId, Timings());
        Shader.SetGlobalVector(VisualId, new Vector4(shockwaveBendAmount, glowIntensity, raggedEdge, flyDistance));
        Shader.SetGlobalColor(GlowColorId, glowColor);
    }

    /// Called by GrassDataRendererFeature every frame, right before the grass compute shader runs.
    public static void UploadTo(CommandBuffer cmd, ComputeShader cs)
    {
        if (instance == null)
        {
            cmd.SetComputeIntParam(cs, CountId, 0);
            return;
        }

        int n = instance.FillArrays();
        cmd.SetComputeIntParam(cs, CountId, n);
        if (n == 0) return;

        cmd.SetComputeVectorArrayParam(cs, CutAId, cutA);
        cmd.SetComputeVectorArrayParam(cs, CutBId, cutB);
        cmd.SetComputeVectorParam(cs, TimingsId, instance.Timings());
        cmd.SetComputeFloatParam(cs, FlowersId, instance.affectFlowers ? 1f : 0f);
        cmd.SetComputeFloatParam(cs, JitterId, instance.raggedEdge);
    }

    // ---------------------------------------------------------------- where does grass grow?

    private Terrain FindTerrain()
    {
        Terrain t = null;
        if (InfiniteGrassRenderer.instance != null) t = InfiniteGrassRenderer.instance.targetTerrain;
        if (t == null) t = Terrain.activeTerrain;
        return t;
    }

    // Reads the terrain's grass + mixed layer weights around the cut once, so debris can follow the real grass density
    private void CacheTerrainWeights(CutData c)
    {
        Terrain t = FindTerrain();
        if (t == null || t.terrainData == null || t.terrainData.alphamapLayers == 0)
        {
            if (!warnedNoTerrain)
            {
                warnedNoTerrain = true;
                Debug.LogWarning("[GrassCutManager] No Terrain found, so grass debris is skipped. Assign 'Target Terrain' on InfiniteGrassRenderer, or untick 'Debris Only On Grass'.", this);
            }
            return;
        }

        TerrainData td = t.terrainData;
        Vector3 tp = t.transform.position;
        int res = td.alphamapResolution;

        int rawX0 = Mathf.FloorToInt((c.origin.x - c.radius - tp.x) / td.size.x * res);
        int rawX1 = Mathf.CeilToInt((c.origin.x + c.radius - tp.x) / td.size.x * res);
        int rawZ0 = Mathf.FloorToInt((c.origin.y - c.radius - tp.z) / td.size.z * res);
        int rawZ1 = Mathf.CeilToInt((c.origin.y + c.radius - tp.z) / td.size.z * res);
        if (rawX1 < 0 || rawZ1 < 0 || rawX0 > res - 1 || rawZ0 > res - 1) return;   // cut is outside the terrain

        int x0 = Mathf.Clamp(rawX0, 0, res - 1), x1 = Mathf.Clamp(rawX1, 0, res - 1);
        int z0 = Mathf.Clamp(rawZ0, 0, res - 1), z1 = Mathf.Clamp(rawZ1, 0, res - 1);

        c.aw = x1 - x0 + 1;
        c.ah = z1 - z0 + 1;
        c.ax0 = x0;
        c.az0 = z0;
        c.alpha = td.GetAlphamaps(x0, z0, c.aw, c.ah);   // [z, x, layer]
        c.layers = td.alphamapLayers;
        c.res = res;
        c.terrainX = tp.x;
        c.terrainZ = tp.z;
        c.sizeX = td.size.x;
        c.sizeZ = td.size.z;

        InfiniteGrassRenderer ig = InfiniteGrassRenderer.instance;
        c.grassLayer = ig != null ? ig.grassTerrainLayerIndex : 0;
        c.mixedLayer = ig != null ? ig.mixedTerrainLayerIndex : 2;
    }

    // 0..1: how much grass grows here (same grass + mixed layer weights the grass spawner uses)
    private float GrassWeightAt(CutData c, float x, float z)
    {
        if (c.alpha == null) return 0f;

        int ix = Mathf.FloorToInt((x - c.terrainX) / c.sizeX * c.res) - c.ax0;
        int iz = Mathf.FloorToInt((z - c.terrainZ) / c.sizeZ * c.res) - c.az0;
        if (ix < 0 || iz < 0 || ix >= c.aw || iz >= c.ah) return 0f;

        float w = 0f;
        if (c.grassLayer >= 0 && c.grassLayer < c.layers) w += c.alpha[iz, ix, c.grassLayer];
        if (c.mixedLayer >= 0 && c.mixedLayer < c.layers) w += c.alpha[iz, ix, c.mixedLayer];
        return Mathf.Clamp01(w);
    }

    private float GroundY(float x, float z, float fallbackY)
    {
        Terrain t = FindTerrain();
        if (t == null) return fallbackY;
        return t.SampleHeight(new Vector3(x, 0f, z)) + t.transform.position.y;
    }

    // ---------------------------------------------------------------- flying debris

    private void EnsureDebrisSystem()
    {
        if (debris != null) return;

        GameObject go = new GameObject("GrassCutDebris");
        debris = go.AddComponent<ParticleSystem>();

        var main = debris.main;
        main.loop = true;                       // stays alive so Emit() always works (emission itself is off)
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = maxDebris;
        main.gravityModifier = debrisGravity;
        main.startSize3D = true;                // lets us set width and height separately (see Debris Aspect)

        var emission = debris.emission; emission.enabled = false;
        var shape = debris.shape; shape.enabled = false;

        var colorOverLife = debris.colorOverLifetime;
        colorOverLife.enabled = true;
        Gradient fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
        colorOverLife.color = fade;

        var sizeOverLife = debris.sizeOverLifetime;
        sizeOverLife.enabled = true;
        sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));

        var rotOverLife = debris.rotationOverLifetime;
        rotOverLife.enabled = true;
        rotOverLife.z = new ParticleSystem.MinMaxCurve(-6f, 6f);

        var psr = go.GetComponent<ParticleSystemRenderer>();
        if (stretchWithVelocity)
        {
            psr.renderMode = ParticleSystemRenderMode.Stretch;
            psr.velocityScale = 0.04f;
            psr.lengthScale = 2.2f;
        }
        else
        {
            psr.renderMode = ParticleSystemRenderMode.Billboard;   // keeps the sprite's shape, no squashing
        }
        psr.shadowCastingMode = ShadowCastingMode.Off;
        psr.receiveShadows = false;

        Material mat = debrisMaterial;
        if (mat == null)
        {
            Shader sh = Shader.Find("Sprites/Default");
            if (sh != null) mat = new Material(sh);
        }
        psr.sharedMaterial = mat;

        debris.Play();
    }

    // Emits grass bits exactly where the cut front is passing, so the debris is synced with the sweep
    private void EmitDebris(CutData c, float age)
    {
        if (debrisOnlyOnGrass && c.alpha == null) return;   // no grass data here = no debris

        EnsureDebrisSystem();

        float t = Mathf.Clamp01(age / Mathf.Max(sweepTime, 0.001f));
        float eased = 1f - (1f - t) * (1f - t);
        float front = Mathf.Min(c.radius, (c.radius + 0.5f * raggedEdge) * eased);
        if (front <= c.emittedFront) return;

        float r1 = Mathf.Max(0f, c.emittedFront);
        float r2 = front;
        float halfAngle = Mathf.Acos(Mathf.Clamp(c.cosHalf, -1f, 1f));          // radians
        float wanted = halfAngle * (r2 * r2 - r1 * r1) * debrisDensity + c.debrisRemainder;
        int count = Mathf.FloorToInt(wanted);
        c.debrisRemainder = wanted - count;
        c.emittedFront = front;

        count = Mathf.Min(count, Mathf.Max(0, maxDebris - debris.particleCount));
        if (count <= 0) return;

        ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();
        for (int i = 0; i < count; i++)
        {
            float angle = Random.Range(-halfAngle, halfAngle);
            float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
            Vector2 d = new Vector2(c.dir.x * cos - c.dir.y * sin, c.dir.x * sin + c.dir.y * cos);

            float r = Mathf.Sqrt(Random.Range(r1 * r1, r2 * r2));
            float px = c.origin.x + d.x * r;
            float pz = c.origin.y + d.y * r;

            // only where grass actually grows (probability = the grass density there)
            if (debrisOnlyOnGrass && Random.value > GrassWeightAt(c, px, pz)) continue;

            Vector3 outward = new Vector3(d.x, 0f, d.y);
            float size = Random.Range(debrisSizeRange.x, debrisSizeRange.y);

            ep.position = new Vector3(px, GroundY(px, pz, 0f) + 0.1f, pz);
            ep.velocity = outward * Random.Range(debrisOutSpeed.x, debrisOutSpeed.y)
                        + Vector3.up * Random.Range(debrisUpSpeed.x, debrisUpSpeed.y)
                        + Random.insideUnitSphere * 0.5f;
            ep.startLifetime = Random.Range(debrisLifetime.x, debrisLifetime.y);
            ep.startSize3D = new Vector3(size * debrisAspect, size, size);
            ep.startColor = Color.Lerp(debrisColorA, debrisColorB, Random.value);
            ep.rotation = Random.Range(0f, 360f);
            debris.Emit(ep, 1);
        }
    }
}