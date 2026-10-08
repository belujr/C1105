using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// One object in the scene that plays all enemy hit feedback:
///   GLOW        energy ripple + cracks over the enemy's body from the hit point
///   DISTORTION  small shockwave ring + heat haze at the hit point
///   DEBRIS      alien shards that burst out of the back of the enemy
///   SOULS       glowing wisps that burst out, swirl, then fly into the player
///
/// Setup: create an empty GameObject "HitFeedbackManager", add this component, assign the materials.
/// CombatHitboxController calls PlayHit(...) when a hit lands. For kills call EnemyHitFX.NotifyKilled()
/// (or let it detect the enemy disappearing after a hit).
///
/// Soul energy: subscribe to HitFeedbackManager.WispArrived to add the soul energy when a wisp reaches the player.
/// </summary>
public class HitFeedbackManager : MonoBehaviour
{
    public static HitFeedbackManager Instance { get; private set; }

    /// <summary>Raised every time one wisp reaches the player. Use it to add soul energy.</summary>
    public static event System.Action<int> WispArrived;

    [Header("Switches")]
    public bool enableGlow = true;
    public bool enableDistortion = true;
    public bool enableDebris = true;
    public bool enableSouls = true;

    // ------------------------------------------------------------------ glow
    [Header("Enemy glow (material with VFX/HitGlow)")]
    public Material glowMaterial;
    [ColorUsage(true, true)] public Color glowHotColor = new Color(3f, 2.4f, 1.6f, 1f);
    [ColorUsage(true, true)] public Color glowEdgeColor = new Color(2.2f, 0.45f, 0.2f, 1f);
    [Tooltip("Seconds the glow lasts.")]
    public float glowDuration = 0.35f;
    [Tooltip("How far the energy wave travels over the body (world units).")]
    public float glowMaxRadius = 1.8f;
    public float glowIntensity = 1f;
    [Tooltip("Writes which enemy meshes get a glow shell into the Console. Use it if the glow has a wrong shape.")]
    public bool logGlowShells = false;

    // ------------------------------------------------------------------ distortion
    [Header("Distortion (material with VFX/HitDistortion, needs Opaque Texture in the URP asset)")]
    public Material distortionMaterial;
    [Header("Hit distortion (every time you hit an enemy)")]
    [Tooltip("Size of the distortion at the hit point (world units).")]
    public float hitDistortionSize = 2.2f;
    public float hitDistortionDuration = 0.3f;
    [Range(0f, 0.15f)] public float hitDistortionStrength = 0.05f;
    [Tooltip("Moves the distortion toward the camera so it is not buried inside the enemy. Raise it if the hit distortion is not visible.")]
    public float hitDistortionCameraOffset = 0.5f;

    [Header("Kill distortion (when an enemy dies and the souls burst out)")]
    public bool killDistortionEnabled = true;
    public float killDistortionSize = 2.4f;
    public float killDistortionDuration = 0.4f;
    [Range(0f, 0.15f)] public float killDistortionStrength = 0.035f;
    public float killDistortionCameraOffset = 0.35f;

    [Header("Distortion debug")]
    [Tooltip("Tints the distortion area magenta so you can see where it appears. If you see the magenta circle but nothing bends, 'Opaque Texture' is off in the URP asset.")]
    public bool distortionDebugTint = false;

    // ------------------------------------------------------------------ debris
    [Header("Debris (alien shards)")]
    [Tooltip("Optional. Leave empty to use a plain URP particle material tinted by the colors below.")]
    public Material debrisMaterial;
    public Color debrisColorA = new Color(0.12f, 0.2f, 0.22f, 1f);
    public Color debrisColorB = new Color(0.35f, 0.15f, 0.4f, 1f);
    [Tooltip("Shards per normal hit. Scaled by hit strength.")]
    public int debrisPerHit = 12;
    [Tooltip("Shards when an enemy dies.")]
    public int debrisOnKill = 30;
    public Vector2 debrisSpeed = new Vector2(4f, 9f);
    public Vector2 debrisSize = new Vector2(0.06f, 0.2f);
    [Tooltip("How wide the shards spread. 0 = straight back, 1 = in every direction.")]
    [Range(0f, 1f)] public float debrisSpread = 0.55f;
    public float debrisGravity = 2.5f;
    [Tooltip("Which layers the shards bounce on. Set this to your ground layer.")]
    public LayerMask debrisCollidesWith = ~0;

    // ------------------------------------------------------------------ souls
    [Header("Soul wisps")]
    public Material soulTrailMaterial;   // VFX/SoulGlow with Radial = 0
    public Material soulOrbMaterial;     // VFX/SoulGlow with Radial = 1
    [Tooltip("ON = souls only come out when an enemy dies. OFF = a few also come out on every hit.")]
    public bool soulsOnlyOnKill = true;
    [Tooltip("Only used when 'Souls Only On Kill' is OFF.")]
    public int wispsPerHit = 1;
    [Tooltip("How many souls come out when an enemy dies (random between min and max).")]
    public int soulsPerKillMin = 3;
    public int soulsPerKillMax = 5;
    [Tooltip("Aim point on the player (height above its position).")]
    public float wispTargetHeight = 1.0f;
    public Vector2 wispBurstSpeed = new Vector2(3f, 7f);
    [Tooltip("Seconds the wisp drifts outward before it starts to home in.")]
    public Vector2 wispBurstTime = new Vector2(0.35f, 0.65f);
    [Tooltip("Extra random delay before homing, so they don't all leave together.")]
    public float wispHomingJitter = 0.25f;
    public float wispDrag = 3.5f;
    public float wispSwirl = 9f;
    public float wispHomeStartSpeed = 3f;
    public float wispHomeMaxSpeed = 24f;
    [Tooltip("Seconds it takes to reach top speed.")]
    public float wispHomeRampTime = 0.6f;
    [Tooltip("How sharply it steers toward the player. Lower = wider curves.")]
    public float wispTurnRate = 7f;
    [Tooltip("The wisp is counted as inside the player when it is this close to the aim point.")]
    public float wispEnterDistance = 0.12f;
    public float wispOrbSize = 0.32f;
    public float wispTrailTime = 0.5f;
    public float wispTrailWidth = 0.16f;
    [ColorUsage(true, true)] public Color soulColor = new Color(1.8f, 1.1f, 0.3f, 1f);

    [Header("Soul absorb: what the player does when a soul arrives")]
    public bool enableAbsorb = true;
    [Tooltip("Material with VFX/SoulBody: glow and energy veins over the player's body.")]
    public Material absorbBodyMaterial;
    [Tooltip("Material with VFX/SoulRing: thin rings of energy that contract into the chest.")]
    public Material absorbRingMaterial;
    [ColorUsage(true, true)] public Color absorbEdgeColor = new Color(1.5f, 0.45f, 0.1f, 1f);
    [Tooltip("Energy added by each soul that arrives.")]
    public float absorbPerSoul = 0.6f;
    [Tooltip("Maximum energy level (several souls in a row stack up to this).")]
    public float absorbMax = 1.5f;
    [Tooltip("Seconds for the energy to fade from 1 to 0.")]
    public float absorbDecay = 0.9f;
    [Tooltip("Glow and energy veins on the body. Set to 0 to turn the body glow off and keep only the rings.")]
    public float absorbBodyIntensity = 1.2f;
    [Header("Absorb rings")]
    public float absorbRingIntensity = 2.5f;
    [Tooltip("Radius the ring starts at (world units).")]
    public float absorbRingStartRadius = 1.0f;
    [Tooltip("Radius the ring shrinks to, inside the body.")]
    public float absorbRingEndRadius = 0.2f;
    [Tooltip("Seconds for a ring to contract into the chest.")]
    public float absorbRingDuration = 0.55f;
    [Tooltip("How fast the energy arcs spin around the ring.")]
    public float absorbRingSpin = 2.2f;
    public float absorbScrollSpeed = 2.5f;
    [Tooltip("Orbiting sparks per second at full energy.")]
    public float absorbSparkRate = 45f;
    [Tooltip("When a soul arrives, the body itself lights up with an energy ripple from the chest (a pop on the body).")]
    public bool absorbBodyPop = true;
    public float absorbPopIntensity = 1.4f;
    [Tooltip("How far the ripple travels over the body (world units).")]
    public float absorbPopRadius = 1.3f;
    public float absorbPopDuration = 0.35f;
    [Tooltip("Draws a cyan sphere (Scene view, or Game view with Gizmos on) where souls aim, so you can see if it is the SoulTarget.")]
    public bool showAimPoint = false;
    [Tooltip("Shifts the aim point toward the camera. Keep at 0 so souls end INSIDE the body (they are drawn on top anyway).")]
    public float soulAimTowardCamera = 0f;

    [Header("Soul glow, head shape and trail sparks")]
    [Tooltip("How bright the head glows. With Bloom on, higher = bigger glow.")]
    public float wispOrbIntensity = 3.5f;
    [Tooltip("How bright the trail glows.")]
    public float wispTrailIntensity = 2.5f;
    [Tooltip("Optional head shape: white on black. Try soul_head_comet / flame / star / burst / diamond. Leave empty for a round orb.")]
    public Texture2D wispHeadShape;
    [Tooltip("Rotates the head so comet / flame / diamond shapes point along the direction of flight.")]
    public bool wispAlignHeadToMotion = true;
    [Tooltip("Stretch the head: X = length, Y = height.")]
    public Vector2 wispHeadStretch = new Vector2(1.6f, 1f);
    [Range(0f, 1f)]
    [Tooltip("Soft glow halo behind a custom head shape.")]
    public float wispHaloAmount = 0.35f;
    [Tooltip("Small glowing sparks that fall off the trail.")]
    public bool trailSparks = true;
    [Tooltip("Sparks per world unit travelled.")]
    public float sparkRatePerUnit = 28f;
    public float sparkIntensity = 2.5f;
    public Vector2 sparkSize = new Vector2(0.025f, 0.08f);
    public Vector2 sparkLifetime = new Vector2(0.4f, 0.9f);

    // ------------------------------------------------------------------ runtime
    private static Camera[] camBuffer = new Camera[8];

    private Transform lastPlayer;
    private Mesh quadMesh;
    private Mesh[] shardMeshes;
    private ParticleSystem debris;

    private class Pulse
    {
        public Transform tf;
        public MeshRenderer mr;
        public float t0;
        public float dur;
        public float strength;
        public float seed;
        public bool isFlash;
        public float size;
        public Transform follow;   // the absorb flash sticks to this (the player), so it never stays behind
    }

    private readonly List<Pulse> activePulses = new List<Pulse>();
    private readonly Stack<Pulse> freePulses = new Stack<Pulse>();
    private readonly Stack<Pulse> freeFlashes = new Stack<Pulse>();
    private readonly Stack<SoulWisp> wispPool = new Stack<SoulWisp>();
    private MaterialPropertyBlock block;
    private Transform wispRoot;
    private Material runtimeSoulTrail;
    private Material runtimeSoulOrb;
    private Transform cachedAbsorbTarget;
    private PlayerSoulAbsorbFX cachedAbsorb;
    private Material runtimeAbsorbBody;
    private Material runtimeAbsorbRing;

    // ------------------------------------------------------------------ lifetime
    public static HitFeedbackManager Get()
    {
        if (Instance == null)
        {
#if UNITY_2023_1_OR_NEWER
            Instance = FindFirstObjectByType<HitFeedbackManager>();
#else
            Instance = FindObjectOfType<HitFeedbackManager>();
#endif
        }
        if (Instance == null)
        {
            Debug.LogWarning("[HitFeedbackManager] None in the scene, creating one with default settings. " +
                             "Create an empty GameObject with this component and assign the materials for the full look.");
            GameObject go = new GameObject("HitFeedbackManager");
            Instance = go.AddComponent<HitFeedbackManager>();
        }
        return Instance;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        block = new MaterialPropertyBlock();
        quadMesh = BuildQuad();
        wispRoot = new GameObject("SoulWisps").transform;
        wispRoot.SetParent(transform, false);

        if (enableDebris) BuildDebrisSystem();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ------------------------------------------------------------------ public API
    /// <summary>Called when the player hits an enemy.</summary>
    public void PlayHit(GameObject enemy, Vector3 point, Vector3 hitDirection, float intensity, Transform player)
    {
        if (player != null) lastPlayer = player;

        hitDirection.y = 0f;
        if (hitDirection.sqrMagnitude < 0.0001f) hitDirection = Vector3.forward;
        hitDirection.Normalize();
        intensity = Mathf.Clamp01(intensity);

        if (enemy != null)
        {
            EnemyHitFX fx = enemy.GetComponent<EnemyHitFX>();
            if (fx == null) fx = enemy.AddComponent<EnemyHitFX>();
            fx.RegisterHit(hitDirection);
            if (logGlowShells) fx.logShells = true;

            if (enableGlow)
            {
                Material mat = GetGlowMaterial();
                fx.PlayGlow(mat, point, glowIntensity * (0.7f + 0.6f * intensity), glowDuration,
                            glowMaxRadius * (0.8f + 0.5f * intensity), glowHotColor, glowEdgeColor);
            }
        }

        if (enableDistortion)
            SpawnDistortion(point, intensity, hitDistortionSize, hitDistortionDuration, hitDistortionStrength, hitDistortionCameraOffset);

        // shards leave from the BACK of the enemy (the side away from the player)
        Vector3 behind = point + hitDirection * 0.25f;
        if (enableDebris) EmitDebris(behind, hitDirection, Mathf.RoundToInt(debrisPerHit * (0.6f + 0.8f * intensity)));
        if (enableSouls && !soulsOnlyOnKill) SpawnWisps(behind, hitDirection, wispsPerHit);
    }

    /// <summary>Called when an enemy dies.</summary>
    public void PlayKill(Vector3 center, Vector3 awayDirection)
    {
        awayDirection.y = 0f;
        if (awayDirection.sqrMagnitude < 0.0001f) awayDirection = Vector3.forward;
        awayDirection.Normalize();

        if (enableDistortion && killDistortionEnabled)
            SpawnDistortion(center, 1f, killDistortionSize, killDistortionDuration, killDistortionStrength, killDistortionCameraOffset);
        if (enableDebris) EmitDebris(center, awayDirection, debrisOnKill);
        if (enableSouls) SpawnWisps(center, awayDirection, Random.Range(soulsPerKillMin, Mathf.Max(soulsPerKillMin, soulsPerKillMax) + 1));
    }

    public static void NotifyKill(Vector3 center, Vector3 awayDirection)
    {
        if (Instance != null) Instance.PlayKill(center, awayDirection);
    }

    // ------------------------------------------------------------------ materials
    private Material GetGlowMaterial()
    {
        if (glowMaterial != null) return glowMaterial;
        Shader s = Shader.Find("VFX/HitGlow");
        if (s == null)
        {
            Debug.LogError("[HitFeedbackManager] Assign a material with the VFX/HitGlow shader.");
            return null;
        }
        glowMaterial = new Material(s);
        return glowMaterial;
    }

    private Material GetDistortionMaterial()
    {
        if (distortionMaterial != null) return distortionMaterial;
        Shader s = Shader.Find("VFX/HitDistortion");
        if (s == null)
        {
            Debug.LogError("[HitFeedbackManager] Assign a material with the VFX/HitDistortion shader.");
            return null;
        }
        distortionMaterial = new Material(s);
        return distortionMaterial;
    }

    private Material GetSoulMaterial(bool orb)
    {
        Material assigned = orb ? soulOrbMaterial : soulTrailMaterial;
        if (assigned != null) return assigned;

        Material cached = orb ? runtimeSoulOrb : runtimeSoulTrail;
        if (cached != null) return cached;

        Shader s = Shader.Find("VFX/SoulGlow");
        if (s == null)
        {
            Debug.LogError("[HitFeedbackManager] Assign materials with the VFX/SoulGlow shader.");
            return null;
        }
        Material m = new Material(s);
        m.SetColor("_Color", soulColor);
        m.SetFloat("_Radial", orb ? 1f : 0f);
        if (orb) runtimeSoulOrb = m; else runtimeSoulTrail = m;
        return m;
    }

    // ------------------------------------------------------------------ distortion + flashes
    private void SpawnDistortion(Vector3 point, float intensity, float size, float duration, float strength, float cameraOffset)
    {
        Material mat = GetDistortionMaterial();
        if (mat == null) return;

        Camera cam = PickCamera(point);
        if (cam == null) return;

        Pulse p = freePulses.Count > 0 ? freePulses.Pop() : CreatePulse("HitDistortion", mat);

        p.isFlash = false;
        p.size = size * (0.85f + 0.3f * intensity);
        p.dur = duration;
        p.t0 = Time.unscaledTime;
        p.strength = strength * (0.8f + 0.4f * intensity);
        p.seed = Random.value * 10f;

        p.tf.position = point + (cam.transform.position - point).normalized * cameraOffset;
        p.tf.localScale = Vector3.one * p.size;
        p.tf.gameObject.SetActive(true);
        activePulses.Add(p);
        UpdatePulse(p, 0f, cam);
    }

    private void SpawnAbsorbFlash(Vector3 position, Transform follow)
    {
        Material mat = GetSoulMaterial(true);
        if (mat == null) return;

        Pulse p = freeFlashes.Count > 0 ? freeFlashes.Pop() : CreatePulse("SoulFlash", mat);
        p.isFlash = true;
        p.size = wispOrbSize * 3.2f;
        p.dur = 0.25f;
        p.follow = follow;
        p.t0 = Time.unscaledTime;
        p.tf.position = position;
        p.tf.gameObject.SetActive(true);
        activePulses.Add(p);
        UpdatePulse(p, 0f, PickCamera(position));
    }

    private Pulse CreatePulse(string name, Material mat)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = quadMesh;
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        go.SetActive(false);

        return new Pulse { tf = go.transform, mr = mr };
    }

    private void LateUpdate()
    {
        for (int i = activePulses.Count - 1; i >= 0; i--)
        {
            Pulse p = activePulses[i];
            float progress = (Time.unscaledTime - p.t0) / Mathf.Max(p.dur, 0.01f); // real time, plays during hit stop

            if (progress >= 1f)
            {
                p.tf.gameObject.SetActive(false);
                activePulses.RemoveAt(i);
                if (p.isFlash) freeFlashes.Push(p); else freePulses.Push(p);
                continue;
            }

            // the absorb flash stays glued to the moving player
            if (p.isFlash && p.follow != null) p.tf.position = GetAimPoint(p.follow, wispTargetHeight);

            UpdatePulse(p, progress, PickCamera(p.tf.position));
        }
    }

    private void UpdatePulse(Pulse p, float progress, Camera cam)
    {
        if (cam != null) p.tf.rotation = Quaternion.LookRotation(cam.transform.forward, cam.transform.up);

        p.mr.GetPropertyBlock(block);
        if (p.isFlash)
        {
            float ease = 1f - (1f - progress) * (1f - progress);
            p.tf.localScale = Vector3.one * (p.size * (0.35f + 0.65f * ease));
            block.SetFloat("_Fade", (1f - progress) * (1f - progress));
        }
        else
        {
            block.SetFloat("_Progress", progress);
            block.SetFloat("_Strength", p.strength);
            block.SetFloat("_Seed", p.seed);
            block.SetFloat("_Debug", distortionDebugTint ? 1f : 0f);
        }
        p.mr.SetPropertyBlock(block);
    }

    // ------------------------------------------------------------------ debris
    private void BuildDebrisSystem()
    {
        shardMeshes = new Mesh[4];
        for (int i = 0; i < shardMeshes.Length; i++) shardMeshes[i] = BuildShard(i * 7 + 3);

        GameObject go = new GameObject("HitDebris");
        go.transform.SetParent(transform, false);
        debris = go.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = debris.main;
        main.loop = true;
        main.playOnAwake = true;
        main.duration = 5f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = 1.2f;
        main.startSpeed = 0f;
        main.startSize3D = true;
        main.startRotation3D = true;
        main.gravityModifier = debrisGravity;
        main.maxParticles = 800;
        main.useUnscaledTime = false;

        ParticleSystem.EmissionModule emission = debris.emission;
        emission.enabled = false;

        ParticleSystem.ShapeModule shape = debris.shape;
        shape.enabled = false;

        ParticleSystem.RotationOverLifetimeModule rot = debris.rotationOverLifetime;
        rot.enabled = true;
        rot.separateAxes = true;
        rot.x = new ParticleSystem.MinMaxCurve(-8f, 8f);
        rot.y = new ParticleSystem.MinMaxCurve(-8f, 8f);
        rot.z = new ParticleSystem.MinMaxCurve(-8f, 8f);

        // shards shrink away during the last third of their life
        ParticleSystem.SizeOverLifetimeModule size = debris.sizeOverLifetime;
        size.enabled = true;
        AnimationCurve shrink = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.65f, 1f), new Keyframe(1f, 0f));
        size.size = new ParticleSystem.MinMaxCurve(1f, shrink);

        // bounce on the ground (static colliders only, so shards never push enemies or the player)
        ParticleSystem.CollisionModule col = debris.collision;
        col.enabled = true;
        col.type = ParticleSystemCollisionType.World;
        col.mode = ParticleSystemCollisionMode.Collision3D;
        col.collidesWith = debrisCollidesWith;
        col.enableDynamicColliders = false;
        col.bounce = 0.35f;
        col.dampen = 0.45f;
        col.lifetimeLoss = 0.1f;
        col.radiusScale = 0.5f;

        ParticleSystemRenderer r = debris.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Mesh;
        r.SetMeshes(shardMeshes);
        r.meshDistribution = ParticleSystemMeshDistribution.UniformRandom;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;

        Material mat = debrisMaterial;
        if (mat == null)
        {
            Shader s = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (s != null) mat = new Material(s);
        }
        if (mat != null) r.sharedMaterial = mat;
        else Debug.LogWarning("[HitFeedbackManager] No debris material found. Assign one in the inspector.");

        debris.Play();
    }

    private void EmitDebris(Vector3 origin, Vector3 awayDir, int count)
    {
        if (debris == null || count <= 0) return;

        ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();
        for (int i = 0; i < count; i++)
        {
            // mostly away from the player, with an upward kick and a random spread
            Vector3 dir = awayDir + Vector3.up * Random.Range(0.25f, 0.9f);
            dir = Vector3.Slerp(dir.normalized, Random.onUnitSphere, debrisSpread * 0.6f).normalized;
            float speed = Random.Range(debrisSpeed.x, debrisSpeed.y);

            ep.position = origin + Random.insideUnitSphere * 0.18f;
            ep.velocity = dir * speed;
            ep.startLifetime = Random.Range(0.9f, 1.5f);

            float s = Random.Range(debrisSize.x, debrisSize.y);
            ep.startSize3D = new Vector3(s * Random.Range(0.6f, 1.2f), s * Random.Range(0.5f, 1.4f), s * Random.Range(0.8f, 2.2f));
            ep.rotation3D = new Vector3(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f));
            ep.startColor = Color.Lerp(debrisColorA, debrisColorB, Random.value);

            debris.Emit(ep, 1);
        }
    }

    // ------------------------------------------------------------------ souls
    private void SpawnWisps(Vector3 origin, Vector3 awayDir, int count)
    {
        if (count <= 0) return;

        Transform target = lastPlayer;
        if (target == null)
        {
#if UNITY_2023_1_OR_NEWER
            PlayerController pc = FindFirstObjectByType<PlayerController>();
#else
            PlayerController pc = FindObjectOfType<PlayerController>();
#endif
            if (pc != null) target = pc.transform;
        }
        if (target == null) return;

        for (int i = 0; i < count; i++)
        {
            SoulWisp wisp = GetWisp();
            if (wisp == null) return;

            Vector3 dir = (awayDir * 0.6f + Vector3.up * 0.9f + Random.insideUnitSphere * 0.8f).normalized;
            Vector3 vel = dir * Random.Range(wispBurstSpeed.x, wispBurstSpeed.y);
            float burst = Random.Range(wispBurstTime.x, wispBurstTime.y);
            float jitter = Random.value * wispHomingJitter;

            wisp.Launch(origin + Random.insideUnitSphere * 0.2f, vel, target, wispTargetHeight, burst, jitter);
        }
    }

    private SoulWisp GetWisp()
    {
        if (wispPool.Count > 0) return wispPool.Pop();

        Material trailMat = GetSoulMaterial(false);
        Material orbMat = GetSoulMaterial(true);
        if (trailMat == null || orbMat == null) return null;

        GameObject go = new GameObject("SoulWisp");
        go.transform.SetParent(wispRoot, false);

        TrailRenderer tr = go.AddComponent<TrailRenderer>();
        tr.sharedMaterial = trailMat;
        tr.time = wispTrailTime;
        tr.widthMultiplier = wispTrailWidth;
        tr.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.15f, 0.8f), new Keyframe(1f, 0f));
        tr.minVertexDistance = 0.03f;
        tr.alignment = LineAlignment.View;
        tr.numCapVertices = 4;
        tr.numCornerVertices = 2;
        tr.shadowCastingMode = ShadowCastingMode.Off;
        tr.receiveShadows = false;
        tr.lightProbeUsage = LightProbeUsage.Off;
        tr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        tr.emitting = false;

        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
        tr.colorGradient = g;

        GameObject orb = new GameObject("Orb");
        orb.transform.SetParent(go.transform, false);
        orb.AddComponent<MeshFilter>().sharedMesh = quadMesh;
        MeshRenderer mr = orb.AddComponent<MeshRenderer>();
        mr.sharedMaterial = orbMat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;

        // glow strength, and the optional custom head shape
        MaterialPropertyBlock trailBlock = new MaterialPropertyBlock();
        tr.GetPropertyBlock(trailBlock);
        trailBlock.SetFloat("_Intensity", wispTrailIntensity);
        tr.SetPropertyBlock(trailBlock);

        MaterialPropertyBlock orbBlock = new MaterialPropertyBlock();
        mr.GetPropertyBlock(orbBlock);
        orbBlock.SetFloat("_Intensity", wispOrbIntensity);
        orbBlock.SetFloat("_HaloAmount", wispHaloAmount);
        if (wispHeadShape != null)
        {
            orbBlock.SetTexture("_ShapeTex", wispHeadShape);
            orbBlock.SetFloat("_UseShape", 1f);
        }
        mr.SetPropertyBlock(orbBlock);

        ParticleSystem sparks = trailSparks ? BuildSparks(go.transform, orbMat) : null;

        SoulWisp wisp = go.AddComponent<SoulWisp>();
        wisp.Init(this, tr, orb.transform, sparks);
        return wisp;
    }

    // Small glowing sparks that fall off the wisp as it flies (they stay in the world, so they form a trail of embers)
    private ParticleSystem BuildSparks(Transform parent, Material mat)
    {
        GameObject go = new GameObject("Sparks");
        go.transform.SetParent(parent, false);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.duration = 1f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(sparkLifetime.x, sparkLifetime.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.7f);
        main.startSize = new ParticleSystem.MinMaxCurve(sparkSize.x, sparkSize.y);
        main.startColor = Color.white;
        main.gravityModifier = -0.15f;
        main.maxParticles = 120;

        ParticleSystem.EmissionModule em = ps.emission;
        em.enabled = true;
        em.rateOverTime = 8f;
        em.rateOverDistance = sparkRatePerUnit;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.06f;

        ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

        ParticleSystem.ColorOverLifetimeModule colLife = ps.colorOverLifetime;
        colLife.enabled = true;
        Gradient fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) });
        colLife.color = fade;

        ParticleSystem.NoiseModule noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.5f;
        noise.frequency = 1.2f;
        noise.scrollSpeed = 0.5f;
        noise.damping = true;

        ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;

        MaterialPropertyBlock sparkBlock = new MaterialPropertyBlock();
        r.GetPropertyBlock(sparkBlock);
        sparkBlock.SetFloat("_Intensity", sparkIntensity);
        r.SetPropertyBlock(sparkBlock);

        return ps;
    }

    // called by SoulWisp
    public void OnWispArrived(Vector3 position, Transform target)
    {
        SpawnAbsorbFlash(position, target);
        if (enableAbsorb)
        {
            PlayerSoulAbsorbFX fx = GetAbsorbFx(target);
            if (fx != null) fx.Absorb();

            // pop on the body: an energy ripple spreads over the player's mesh from the chest
            if (absorbBodyPop && target != null)
            {
                Material popMat = GetGlowMaterial();
                if (popMat != null)
                {
                    EnemyHitFX pop = target.GetComponent<EnemyHitFX>();
                    if (pop == null) pop = target.gameObject.AddComponent<EnemyHitFX>();
                    pop.PlayGlow(popMat, GetAimPoint(target, wispTargetHeight), absorbPopIntensity, absorbPopDuration,
                                 absorbPopRadius, soulColor, absorbEdgeColor);
                }
            }
        }
        if (WispArrived != null) WispArrived(1);
    }

    public void ReturnWisp(SoulWisp wisp)
    {
        wispPool.Push(wisp);
    }

    // ------------------------------------------------------------------ player absorb
    private PlayerSoulAbsorbFX GetAbsorbFx(Transform target)
    {
        if (target == null) return null;
        if (target == cachedAbsorbTarget && cachedAbsorb != null) return cachedAbsorb;

        PlayerSoulAbsorbFX fx = target.GetComponent<PlayerSoulAbsorbFX>();
        if (fx == null) fx = target.gameObject.AddComponent<PlayerSoulAbsorbFX>();
        fx.Init(this);

        cachedAbsorbTarget = target;
        cachedAbsorb = fx;
        return fx;
    }

    /// <summary>The point souls fly to: the middle of the player's body, a little toward the camera.</summary>
    public Vector3 GetAimPoint(Transform target, float fallbackHeight)
    {
        Vector3 p = target.position + Vector3.up * fallbackHeight;

        PlayerSoulAbsorbFX fx = enableAbsorb ? GetAbsorbFx(target) : null;
        if (fx != null) p = fx.ChestPoint;

        Camera cam = PickCamera(p);
        if (cam != null && soulAimTowardCamera > 0f)
            p += (cam.transform.position - p).normalized * soulAimTowardCamera;

        return p;
    }

    public Material GetAbsorbBodyMaterial()
    {
        if (absorbBodyMaterial != null) return absorbBodyMaterial;
        if (runtimeAbsorbBody != null) return runtimeAbsorbBody;
        Shader s = Shader.Find("VFX/SoulBody");
        if (s == null) { Debug.LogError("[HitFeedbackManager] Assign a material with the VFX/SoulBody shader."); return null; }
        runtimeAbsorbBody = new Material(s);
        return runtimeAbsorbBody;
    }

    public Material GetAbsorbRingMaterial()
    {
        if (absorbRingMaterial != null) return absorbRingMaterial;
        if (runtimeAbsorbRing != null) return runtimeAbsorbRing;
        Shader s = Shader.Find("VFX/SoulRing");
        if (s == null) { Debug.LogError("[HitFeedbackManager] Assign a material with the VFX/SoulRing shader."); return null; }
        runtimeAbsorbRing = new Material(s);
        return runtimeAbsorbRing;
    }

    public Material GetSoulOrbMaterial()
    {
        return GetSoulMaterial(true);
    }

    // ------------------------------------------------------------------ helpers
    public static Camera PickCamera(Vector3 pos)
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

    private static Mesh BuildQuad()
    {
        Mesh m = new Mesh();
        m.name = "HitFXQuad";
        m.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f)
        };
        m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
        m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    // A small irregular crystal shard (4 faces, flat shaded)
    private static Mesh BuildShard(int seed)
    {
        System.Random rng = new System.Random(seed);
        System.Func<float, float, float> r = (a, b) => a + (float)rng.NextDouble() * (b - a);

        Vector3[] p =
        {
            new Vector3(r(-0.08f, 0.08f), r(-0.08f, 0.08f), r(0.45f, 0.75f)),   // sharp tip
            new Vector3(r(-0.30f, -0.12f), r(-0.15f, 0.05f), r(-0.35f, -0.15f)),
            new Vector3(r(0.12f, 0.30f), r(-0.15f, 0.05f), r(-0.35f, -0.15f)),
            new Vector3(r(-0.08f, 0.08f), r(0.10f, 0.28f), r(-0.40f, -0.20f))
        };
        Vector3 centroid = (p[0] + p[1] + p[2] + p[3]) * 0.25f;
        int[][] faces = { new[] { 0, 1, 2 }, new[] { 0, 2, 3 }, new[] { 0, 3, 1 }, new[] { 1, 3, 2 } };

        List<Vector3> verts = new List<Vector3>();
        List<int> tris = new List<int>();
        foreach (int[] f in faces)
        {
            Vector3 a = p[f[0]], b = p[f[1]], c = p[f[2]];
            Vector3 n = Vector3.Cross(b - a, c - a);
            Vector3 faceCenter = (a + b + c) / 3f;
            if (Vector3.Dot(n, faceCenter - centroid) < 0f)   // make sure the face points outward
            {
                Vector3 t = b; b = c; c = t;
            }
            int start = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            tris.Add(start); tris.Add(start + 1); tris.Add(start + 2);
        }

        Mesh m = new Mesh();
        m.name = "HitShard" + seed;
        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }
}