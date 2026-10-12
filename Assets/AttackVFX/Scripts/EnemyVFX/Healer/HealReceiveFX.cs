using UnityEngine;

/// <summary>
/// Lives on any enemy that is being healed (added automatically by HealerController, you don't have to add it).
/// While a healer "pings" it every frame it shows:
///   - a rune circle on the floor under the enemy (spins slowly, pulses)
///   - rings that expand out from the floor
///   - small "+" sparks rising off the floor
///   - a soft glow on the chest where the energy stream lands
///   - a green energy ripple over the body from the feet up (reuses your VFX/HitGlow shader through EnemyHitFX)
/// When the pings stop it fades out by itself, so it also works with several healers on one enemy.
/// </summary>
public class HealReceiveFX : MonoBehaviour
{
    [Header("Look")]
    public Color color = new Color(0.30f, 1f, 0.50f, 1f);
    public Color accent = new Color(0.85f, 1f, 0.90f, 1f);
    [Tooltip("HDR brightness multiplier. With Bloom on, higher = bigger glow.")]
    public float brightness = 2.2f;

    [Header("Floor aura")]
    public bool showGroundAura = true;
    [Tooltip("World radius of the rune. 0 = automatic from the enemy's collider size.")]
    public float auraRadius = 0f;
    [Tooltip("Automatic radius = collider radius x this.")]
    public float autoRadiusScale = 1.7f;
    public float spinDegreesPerSecond = 25f;
    public float fadeIn = 0.25f;
    public float fadeOut = 0.5f;
    [Tooltip("Set this to your ground layer so the aura sits on the floor and not on other enemies.")]
    public LayerMask groundMask = ~0;
    [Tooltip("Small lift above the floor so the rune does not flicker against the ground.")]
    public float groundOffset = 0.05f;
    [Tooltip("Clears grass and flowers under the aura (needs the HealAuraGrassMask shader). Grass comes back when the heal ends.")]
    public bool clearGrassUnderAura = true;

    [Header("Particles")]
    public float plusPerSecond = 14f;
    public bool pulseRings = true;
    [Tooltip("Seconds between expanding rings.")]
    public float ringInterval = 0.9f;
    public bool chestGlow = true;
    [Range(0f, 1f)] public float chestGlowStrength = 0.45f;

    [Header("Body ripple (needs HitFeedbackManager + the VFX/HitGlow shader)")]
    public bool bodyRipple = true;
    public float rippleInterval = 0.8f;
    public float rippleIntensity = 1f;
    public float rippleDuration = 0.6f;
    [ColorUsage(true, true)] public Color rippleHot = new Color(1.2f, 3f, 1.6f, 1f);
    [ColorUsage(true, true)] public Color rippleEdge = new Color(0.2f, 1.6f, 0.5f, 1f);

    private static Material fallbackGlowMat;

    private Material plusMat, ringMat, dotMat;
    private HealGroundRune rune;
    private ParticleSystem plusPS, ringPS, glowPS;
    private EnemyHitFX hitFx;
    private bool built;
    private float lastPing = -10f;
    private float ringTimer;
    private float rippleTimer;
    private float worldRadius = 1f;
    private float height = 1.8f;
    private Vector3 chestLocal = Vector3.up;

    /// <summary>Where the healer's stream should land (chest of the enemy).</summary>
    public Vector3 AimPoint
    {
        get
        {
            EnsureBuilt();
            return transform.TransformPoint(chestLocal);
        }
    }

    /// <summary>Call every frame while this enemy is being healed.</summary>
    public void Ping()
    {
        EnsureBuilt();
        lastPing = Time.time;
    }

    private void OnDisable()
    {
        lastPing = -10f;
        ringTimer = 0f;
        rippleTimer = 0f;
        if (rune != null) rune.Reset();
    }

    private void OnDestroy()
    {
        if (plusMat != null) Destroy(plusMat);
        if (ringMat != null) Destroy(ringMat);
        if (dotMat != null) Destroy(dotMat);
        if (rune != null) rune.Dispose();
    }

    // ------------------------------------------------------------------ build
    private void EnsureBuilt()
    {
        if (built) return;
        built = true;

        // must happen before we create any child mesh (see HealVFXAssets.EnsureHitFX)
        hitFx = HealVFXAssets.EnsureHitFX(gameObject);

        Vector3 centerWorld = transform.position + Vector3.up;
        float bodyRadius = 0.5f;
        Collider col = GetComponentInChildren<Collider>();
        if (col != null)
        {
            Bounds b = col.bounds;
            height = Mathf.Max(0.8f, b.size.y);
            bodyRadius = Mathf.Max(b.extents.x, b.extents.z);
            centerWorld = b.center;
        }
        chestLocal = transform.InverseTransformPoint(centerWorld);
        worldRadius = auraRadius > 0f ? auraRadius : Mathf.Clamp(bodyRadius * autoRadiusScale, 0.9f, 3f);

        plusMat = HealVFXAssets.CreateMaterial(HealVFXAssets.Plus, brightness);
        ringMat = HealVFXAssets.CreateMaterial(HealVFXAssets.Ring, brightness);
        dotMat = HealVFXAssets.CreateMaterial(HealVFXAssets.Dot, brightness * 0.8f);

        // floor rune
        rune = new HealGroundRune(transform, "HealAuraRune", HealVFXAssets.Rune, brightness);
        rune.clearGrass = clearGrassUnderAura;
        HealVFXAssets.ProtectFromHitFX(gameObject, rune.Renderer);
        HealVFXAssets.ProtectFromHitFX(gameObject, rune.MaskRenderer);

        // rising plus signs, spawned over the floor disc and flying straight up
        plusPS = HealVFXAssets.MakeSystem(transform, "HealPlus", plusMat, 120, true);
        ParticleSystem.MainModule pm = plusPS.main;
        pm.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.4f);
        pm.startSpeed = new ParticleSystem.MinMaxCurve(0.9f, 1.7f);
        pm.startSize = new ParticleSystem.MinMaxCurve(0.16f, 0.28f);
        pm.startRotation = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f);
        pm.startColor = new ParticleSystem.MinMaxGradient(color, accent);
        ParticleSystem.ShapeModule ps = plusPS.shape;
        ps.enabled = true;
        ps.shapeType = ParticleSystemShapeType.Cone;
        ps.angle = 2f;
        ps.radius = worldRadius * 0.55f;
        ps.radiusThickness = 1f;
        HealVFXAssets.FadeInOut(plusPS, 0.15f, 0.6f, 1f);
        HealVFXAssets.SizeCurve(plusPS, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.2f, 1f), new Keyframe(1f, 0.7f)));

        // expanding rings on the floor
        ringPS = HealVFXAssets.MakeSystem(transform, "HealRings", ringMat, 12, false);   // local space: rings follow the enemy
        ringPS.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        ParticleSystem.MainModule rm = ringPS.main;
        rm.startLifetime = 0.8f;
        rm.startColor = color;
        HealVFXAssets.FadeInOut(ringPS, 0.1f, 0.3f, 0.9f);
        HealVFXAssets.SizeCurve(ringPS, new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(0.4f, 0.8f), new Keyframe(1f, 1f)));

        // chest glow where the stream lands
        glowPS = HealVFXAssets.MakeSystem(transform, "HealChestGlow", dotMat, 20, false);
        glowPS.transform.localPosition = chestLocal;
        ParticleSystem.MainModule gm = glowPS.main;
        gm.startLifetime = 0.7f;
        gm.startSize = new ParticleSystem.MinMaxCurve(height * 0.55f, height * 0.75f);
        gm.startColor = color;
        HealVFXAssets.FadeInOut(glowPS, 0.4f, 0.5f, chestGlowStrength);
    }

    // ------------------------------------------------------------------ update
    private void LateUpdate()
    {
        if (!built) return;
        float dt = Time.deltaTime;
        bool active = Time.time - lastPing < 0.2f;

        Vector3 feet = transform.position;
        if (active || rune.IsVisible) feet = HealVFXAssets.GroundPoint(transform, groundMask, groundOffset);

        rune.Tick(dt, active && showGroundAura, feet, worldRadius, color, spinDegreesPerSecond, fadeIn, fadeOut);

        // plus sparks: emitter lies flat on the floor (cone points up)
        plusPS.transform.SetPositionAndRotation(feet, Quaternion.Euler(-90f, 0f, 0f));
        HealVFXAssets.SetRate(plusPS, active ? plusPerSecond : 0f);

        HealVFXAssets.SetRate(glowPS, active && chestGlow ? 3.5f : 0f);
        ringPS.transform.position = feet;

        if (active && pulseRings)
        {
            ringTimer -= dt;
            if (ringTimer <= 0f)
            {
                ringTimer = Mathf.Max(0.2f, ringInterval);
                ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();
                ep.position = Vector3.zero;   // local to the ring system, which sits on the floor under the enemy
                ep.velocity = Vector3.zero;
                ep.startSize = worldRadius * 2.1f;
                ringPS.Emit(ep, 1);
            }
        }
        else if (!active)
        {
            ringTimer = 0f;   // the first ring appears right when the heal starts
        }

        if (active && bodyRipple)
        {
            rippleTimer -= dt;
            if (rippleTimer <= 0f)
            {
                rippleTimer = Mathf.Max(0.2f, rippleInterval);
                PlayRipple(feet);
            }
        }
        else if (!active)
        {
            rippleTimer = 0f;
        }
    }

    private void PlayRipple(Vector3 feet)
    {
        Material glow = GetRippleMaterial();
        if (glow == null || hitFx == null) return;
        // the ripple starts at the feet and climbs over the whole body
        hitFx.PlayGlow(glow, feet, rippleIntensity, rippleDuration, height * 1.3f, rippleHot, rippleEdge);
    }

    private static Material GetRippleMaterial()
    {
        HitFeedbackManager mgr = HitFeedbackManager.Instance;
        if (mgr != null && mgr.glowMaterial != null) return mgr.glowMaterial;
        if (fallbackGlowMat == null)
        {
            Shader s = Shader.Find("VFX/HitGlow");
            if (s != null) fallbackGlowMat = new Material(s);
        }
        return fallbackGlowMat;
    }
}