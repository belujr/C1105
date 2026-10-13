using UnityEngine;

/// <summary>
/// The Healer's bat-summoning effect (added automatically by HealerController, you don't have to add it).
///   BeginCharge: while the hands rise, a magic circle spins up between them, an orb swells and sparks are sucked into it,
///                and a summoning circle lights up on the floor
///   Release:     when the bat appears: a bright flash, a burst of sparks and a ring that races out
///   Cancel:      the healer was hit or died mid-summon: it fizzles out
/// Colors are cyan and violet, like the bats. Everything is built in code on first use.
/// </summary>
public class HealerSummonVFX : MonoBehaviour
{
    [Header("Look")]
    public Color mainColor = new Color(0.25f, 0.75f, 1f, 1f);
    public Color accentColor = new Color(0.7f, 0.45f, 1f, 1f);
    public Color coreColor = new Color(0.85f, 0.97f, 1f, 1f);
    [Tooltip("HDR brightness multiplier. With Bloom on, higher = bigger glow.")]
    public float brightness = 2.5f;

    [Header("Charge (while the hands rise)")]
    [Tooltip("Size of the orb at the end of the charge.")]
    public float orbSize = 0.9f;
    [Tooltip("Size of the magic circle between the hands.")]
    public float circleSize = 1.7f;
    [Tooltip("Sparks start this far away from the orb and fly into it.")]
    public float convergeRadius = 1.4f;
    public float convergeRate = 70f;

    [Header("Release (the bat appears)")]
    public int burstSparks = 36;
    public float ringSize = 4f;

    [Header("Summoning circle on the floor")]
    public bool showFloorCircle = true;
    public float floorRadius = 1.7f;
    [Tooltip("Set this to your ground layer so the circle sits on the floor and not on other enemies.")]
    public LayerMask groundMask = ~0;
    public float groundOffset = 0.05f;
    [Tooltip("Clears grass and flowers under the circle (needs the HealAuraGrassMask shader).")]
    public bool clearGrass = true;

    private Transform anchor, point;
    private ParticleSystem orbPS, convergePS, circlePS, flashPS, burstPS, ringPS, groundRingPS;
    private HealGroundRune floor;
    private bool built, charging, floorOn;
    private float chargeStart, chargeDur, floorOffAt;

    // ------------------------------------------------------------------ API (called by HealerController)
    /// <param name="spawnPoint">Where the bat appears (between the raised hands).</param>
    /// <param name="duration">Seconds until the bat appears.</param>
    public void BeginCharge(Transform spawnPoint, float duration)
    {
        EnsureBuilt();
        point = spawnPoint;
        charging = true;
        floorOn = showFloorCircle;
        chargeStart = Time.time;
        chargeDur = Mathf.Max(0.1f, duration);
        anchor.position = PointPos();

        // the magic circle between the hands (it lives exactly as long as the charge)
        ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();
        ep.position = Vector3.zero;   // local to the anchor
        ep.velocity = Vector3.zero;
        ep.startSize = circleSize;
        ep.startLifetime = chargeDur + 0.05f;
        ep.startColor = mainColor;
        circlePS.Emit(ep, 1);
    }

    /// <summary>The bat appears: flash, sparks, rings.</summary>
    public void Release()
    {
        EnsureBuilt();
        Vector3 pos = PointPos();
        anchor.position = pos;

        charging = false;
        floorOn = showFloorCircle;
        floorOffAt = Time.time + 0.4f;   // the floor circle lingers for a moment
        StopCharge();

        ParticleSystem.EmitParams fp = new ParticleSystem.EmitParams();
        fp.position = pos;
        fp.velocity = Vector3.zero;
        fp.startSize = 2.4f;
        fp.startLifetime = 0.25f;
        fp.startColor = coreColor;
        flashPS.Emit(fp, 1);

        for (int i = 0; i < burstSparks; i++) EmitSpark(pos, Random.onUnitSphere, Random.Range(3f, 8f));

        ParticleSystem.EmitParams rp = new ParticleSystem.EmitParams();
        rp.position = pos;
        rp.velocity = Vector3.zero;
        rp.startSize = ringSize;
        rp.startLifetime = 0.4f;
        rp.startColor = mainColor;
        ringPS.Emit(rp, 1);

        ParticleSystem.EmitParams gp = rp;
        gp.position = HealVFXAssets.GroundPoint(transform, groundMask, groundOffset);
        gp.startSize = ringSize * 1.3f;
        gp.startLifetime = 0.5f;
        groundRingPS.Emit(gp, 1);
    }

    /// <summary>The summon was interrupted (hit or killed): it fizzles out.</summary>
    public void Cancel()
    {
        if (!built) return;
        bool wasCharging = charging;
        charging = false;
        floorOn = false;
        StopCharge();

        if (wasCharging)
            for (int i = 0; i < 10; i++) EmitSpark(PointPos(), Random.onUnitSphere, Random.Range(1f, 3f));
    }

    private void OnDisable()
    {
        // pooled / disabled: clean up without playing anything
        charging = false;
        floorOn = false;
        if (!built) return;
        StopCharge();
        if (floor != null) floor.Reset();
    }

    private void OnDestroy()
    {
        if (floor != null) floor.Dispose();
    }

    // ------------------------------------------------------------------ build
    private void StopCharge()
    {
        HealVFXAssets.SetRate(orbPS, 0f);
        HealVFXAssets.SetRate(convergePS, 0f);
        circlePS.Clear();
        orbPS.Clear();
        convergePS.Clear();
    }

    private Vector3 PointPos()
    {
        if (point != null) return point.position;
        return transform.position + Vector3.up * 1.6f + transform.forward * 0.4f;
    }

    private static float S(float x)
    {
        x = Mathf.Clamp01(x);
        return x * x * (3f - 2f * x);
    }

    private void EnsureBuilt()
    {
        if (built) return;
        built = true;

        HealVFXAssets.EnsureHitFX(gameObject);   // before any child mesh, see HealVFXAssets.EnsureHitFX

        anchor = new GameObject("SummonAnchor").transform;
        anchor.SetParent(transform, false);

        Material dotMat = HealVFXAssets.CreateMaterial(HealVFXAssets.Dot, brightness);
        Material ringMat = HealVFXAssets.CreateMaterial(HealVFXAssets.Ring, brightness);
        Material circleMat = HealVFXAssets.CreateMaterial(HealVFXAssets.Summon, brightness);

        // the orb swells between the hands (local space: it follows the hands)
        orbPS = HealVFXAssets.MakeSystem(anchor, "SummonOrb", dotMat, 40, false);
        ParticleSystem.MainModule om = orbPS.main;
        om.startLifetime = 0.4f;
        om.startColor = new ParticleSystem.MinMaxGradient(coreColor, mainColor);
        HealVFXAssets.FadeInOut(orbPS, 0.3f, 0.5f, 0.9f);

        // sparks are sucked into the orb: they spawn on a sphere and fly inward (negative speed)
        convergePS = HealVFXAssets.MakeSystem(anchor, "SummonConverge", dotMat, 160, false);
        ParticleSystem.MainModule cm = convergePS.main;
        cm.startSpeed = -(convergeRadius / 0.45f);
        cm.startLifetime = 0.45f;
        cm.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
        cm.startColor = new ParticleSystem.MinMaxGradient(mainColor, accentColor);
        ParticleSystem.ShapeModule cs = convergePS.shape;
        cs.enabled = true;
        cs.shapeType = ParticleSystemShapeType.Sphere;
        cs.radius = convergeRadius;
        cs.radiusThickness = 0f;
        HealVFXAssets.FadeInOut(convergePS, 0.1f, 0.6f, 1f);
        HealVFXAssets.SizeCurve(convergePS, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.3f)));

        // the spinning magic circle (an eight-pointed star in two rings), facing the camera
        circlePS = HealVFXAssets.MakeSystem(anchor, "SummonCircle", circleMat, 4, false);
        ParticleSystem.RotationOverLifetimeModule rot = circlePS.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(120f * Mathf.Deg2Rad);
        HealVFXAssets.FadeInOut(circlePS, 0.15f, 0.85f, 1f);
        HealVFXAssets.SizeCurve(circlePS, new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(0.3f, 1f), new Keyframe(1f, 1f)));

        // release: flash, sparks, rings (world space)
        flashPS = HealVFXAssets.MakeSystem(anchor, "SummonFlash", dotMat, 4, true);
        HealVFXAssets.FadeInOut(flashPS, 0.08f, 0.25f, 1f);
        HealVFXAssets.SizeCurve(flashPS, new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(0.3f, 1f), new Keyframe(1f, 1.4f)));

        burstPS = HealVFXAssets.MakeSystem(anchor, "SummonBurst", dotMat, 120, true);
        ParticleSystemRenderer br = burstPS.GetComponent<ParticleSystemRenderer>();
        br.renderMode = ParticleSystemRenderMode.Stretch;
        br.velocityScale = 0.03f;
        br.lengthScale = 4f;
        HealVFXAssets.FadeInOut(burstPS, 0.05f, 0.5f, 1f);
        HealVFXAssets.SizeCurve(burstPS, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.2f)));

        ringPS = HealVFXAssets.MakeSystem(anchor, "SummonRing", ringMat, 4, true);   // faces the camera
        HealVFXAssets.FadeInOut(ringPS, 0.05f, 0.3f, 1f);
        HealVFXAssets.SizeCurve(ringPS, new AnimationCurve(new Keyframe(0f, 0.1f), new Keyframe(0.4f, 0.75f), new Keyframe(1f, 1f)));

        groundRingPS = HealVFXAssets.MakeSystem(anchor, "SummonGroundRing", ringMat, 4, true);
        groundRingPS.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        HealVFXAssets.FadeInOut(groundRingPS, 0.05f, 0.3f, 1f);
        HealVFXAssets.SizeCurve(groundRingPS, new AnimationCurve(new Keyframe(0f, 0.1f), new Keyframe(0.4f, 0.75f), new Keyframe(1f, 1f)));

        // the summoning circle on the floor
        if (showFloorCircle)
        {
            floor = new HealGroundRune(transform, "HealerSummonCircle", HealVFXAssets.Summon, brightness * 0.8f);
            floor.clearGrass = clearGrass;
            floor.startScale = 0.5f;
            HealVFXAssets.ProtectFromHitFX(gameObject, floor.Renderer);
            HealVFXAssets.ProtectFromHitFX(gameObject, floor.MaskRenderer);
        }
    }

    private void EmitSpark(Vector3 pos, Vector3 dir, float speed)
    {
        ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();
        ep.position = pos;
        ep.velocity = dir.normalized * speed;
        ep.startLifetime = Random.Range(0.4f, 0.8f);
        ep.startSize = Random.Range(0.05f, 0.1f);
        float r = Random.value;
        ep.startColor = r < 0.4f ? mainColor : (r < 0.75f ? coreColor : accentColor);
        burstPS.Emit(ep, 1);
    }

    // ------------------------------------------------------------------ update
    private void LateUpdate()
    {
        if (!built) return;

        anchor.position = PointPos();

        float p = charging ? Mathf.Clamp01((Time.time - chargeStart) / chargeDur) : 0f;
        if (charging && Time.time - chargeStart > chargeDur + 0.5f)
        {
            // safety: the release never came
            charging = false;
            StopCharge();
        }

        // the orb swells, the sparks come faster and faster
        ParticleSystem.MainModule om = orbPS.main;
        float size = Mathf.Lerp(0.12f, orbSize, S(p));
        om.startSize = new ParticleSystem.MinMaxCurve(size * 0.8f, size * 1.1f);
        HealVFXAssets.SetRate(orbPS, charging ? 12f : 0f);
        HealVFXAssets.SetRate(convergePS, charging ? convergeRate * (0.3f + 0.7f * p) : 0f);

        // the floor circle
        if (floor != null)
        {
            if (!charging && floorOn && Time.time >= floorOffAt) floorOn = false;
            if (floorOn || floor.IsVisible)
            {
                floor.clearGrass = clearGrass;
                floor.Tick(Time.deltaTime, floorOn, HealVFXAssets.GroundPoint(transform, groundMask, groundOffset),
                           floorRadius, mainColor, 60f, 0.25f, 0.4f);
            }
        }
    }
}
