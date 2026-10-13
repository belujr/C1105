using UnityEngine;

/// <summary>
/// The "floating" look of the Ranged Minion (added automatically by RangedMinionBrain, put it on the prefab to edit the values):
///   - a soft SHADOW and a glowing RING on the ground below it. They shrink and fade as the minion floats higher,
///     so the player can read how high it is (important in a top-down view). The grass under the ring is cleared in a
///     wide patch (Clear Radius) so tall grass cannot hide it.
///   - little motes that drift down from under the body, like a levitation field
///   - a few small orbs that circle the minion
///   - it reacts to the shot: MinionShotVFX makes the ring glow stronger while the minion charges and surge when it fires
/// You can edit this component live in Play mode: it rebuilds itself when a value changes.
/// </summary>
public class MinionHoverVFX : MonoBehaviour
{
    [Header("Look")]
    public Color mainColor = new Color(0.55f, 0.5f, 1f, 1f);
    public Color accentColor = new Color(0.5f, 0.82f, 1f, 1f);
    [Tooltip("HDR brightness multiplier. With Bloom on, higher = bigger glow.")]
    public float brightness = 2.2f;

    [Header("Ground shadow and ring")]
    public bool showShadow = true;
    public float shadowRadius = 0.8f;
    [Range(0f, 1f)] public float shadowOpacity = 0.45f;
    public bool showRing = true;
    public float ringRadius = 0.9f;
    [Tooltip("The shadow and ring shrink and fade over this height (world units).")]
    public float fadeHeight = 4f;
    [Tooltip("Set this to your ground layer. The ground needs a collider (a terrain has one).")]
    public LayerMask groundMask = ~0;
    [Tooltip("Lifts the ring above the floor.")]
    public float groundOffset = 0.2f;
    [Tooltip("How far below the minion the ground is searched (world units).")]
    public float groundSearchDistance = 15f;

    [Header("Clearing the grass under the ring")]
    [Tooltip("Needs the HealAuraGrassMask shader. The grass is cleared so the ring can be seen.")]
    public bool clearGrass = true;
    [Tooltip("Radius of the cleared patch (world units). It does not shrink when the minion floats higher, and it should be larger than the ring: the grass mask is not very sharp.")]
    public float clearRadius = 2f;

    [Header("Levitation motes (drift down from under the body)")]
    public bool showMotes = true;
    public float moteRate = 14f;
    [Tooltip("Extra motes per meter the minion moves.")]
    public float motesPerMeter = 6f;
    [Tooltip("Where the motes start, relative to the minion.")]
    public Vector3 underBodyOffset = new Vector3(0f, 0.3f, 0f);

    [Header("Orbiting orbs")]
    public bool showOrbs = true;
    public float orbRadius = 0.6f;
    [Tooltip("Radians per second.")]
    public float orbSpeed = 3f;
    [Tooltip("The center of the orbit, relative to the minion.")]
    public Vector3 orbCenterOffset = new Vector3(0f, 1f, 0f);

    private ParticleSystem motePS, orbPS;
    private HealGroundRune ring;
    private Transform shadowTf;
    private MeshRenderer shadowMr;
    private Material shadowMat, moteMat, orbMat, trailMat;
    private readonly MaterialPropertyBlock shadowBlock = new MaterialPropertyBlock();
    private bool built, dirty, warnedNoGround;
    private float phase, charge, surge, lastGroundY;
    private bool haveGround;

    /// <summary>0..1: the minion is charging a shot (set by MinionShotVFX). The ring glows stronger.</summary>
    public void SetCharge(float value)
    {
        charge = Mathf.Clamp01(value);
    }

    /// <summary>The minion just fired: the ring surges.</summary>
    public void Surge()
    {
        surge = 1f;
    }

    private void OnEnable()
    {
        EnsureBuilt();
        phase = Random.value * 6.2832f;
        haveGround = false;
        if (motePS != null) motePS.Clear();
        if (orbPS != null) orbPS.Clear();
    }

    private void OnDisable()
    {
        if (!built) return;
        if (ring != null) ring.Reset();
        if (shadowMr != null) shadowMr.enabled = false;
        if (motePS != null) motePS.Clear();
        if (orbPS != null) orbPS.Clear();
    }

    private void OnDestroy()
    {
        DestroyBuilt();
    }

    // editing a value in Play mode rebuilds the effect, so every setting works live
    private void OnValidate()
    {
        if (Application.isPlaying && built) dirty = true;
    }

    private void DestroyBuilt()
    {
        if (ring != null) { ring.DestroyObjects(); ring.Dispose(); ring = null; }
        if (shadowTf != null) Destroy(shadowTf.gameObject);
        if (motePS != null) Destroy(motePS.gameObject);
        if (orbPS != null) Destroy(orbPS.gameObject);
        if (shadowMat != null) Destroy(shadowMat);
        if (moteMat != null) Destroy(moteMat);
        if (orbMat != null) Destroy(orbMat);
        if (trailMat != null) Destroy(trailMat);
        shadowTf = null; shadowMr = null; motePS = null; orbPS = null;
        shadowMat = null; moteMat = null; orbMat = null; trailMat = null;
    }

    // ------------------------------------------------------------------ build
    private void EnsureBuilt()
    {
        if (built) return;
        built = true;

        HealVFXAssets.EnsureHitFX(gameObject);   // before any child mesh, see HealVFXAssets.EnsureHitFX

        // soft dark shadow on the ground (alpha blended)
        if (showShadow)
        {
            GameObject go = new GameObject("MinionHoverShadow");
            shadowTf = go.transform;
            shadowTf.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = HealVFXAssets.FlatQuad;
            shadowMr = go.AddComponent<MeshRenderer>();
            shadowMat = HealVFXAssets.CreateAlphaMaterial(HealVFXAssets.Blob);
            shadowMr.sharedMaterial = shadowMat;
            shadowMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            shadowMr.receiveShadows = false;
            shadowMr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            shadowMr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            shadowMr.enabled = false;
            HealVFXAssets.ProtectFromHitFX(gameObject, shadowMr);
        }

        // glowing ring on the ground (with its own grass-clearing patch)
        if (showRing)
        {
            ring = new HealGroundRune(transform, "MinionHoverRing", HealVFXAssets.Ring, brightness);
            ring.clearGrass = clearGrass;
            ring.startScale = 0.8f;
            HealVFXAssets.ProtectFromHitFX(gameObject, ring.Renderer);
            HealVFXAssets.ProtectFromHitFX(gameObject, ring.MaskRenderer);
        }

        // motes drifting down from under the body (world space, emitted by time and by the distance flown)
        if (showMotes)
        {
            moteMat = HealVFXAssets.CreateMaterial(HealVFXAssets.Dot, brightness);
            motePS = HealVFXAssets.MakeSystem(transform, "MinionMotes", moteMat, 60, true);
            motePS.transform.localPosition = underBodyOffset;
            ParticleSystem.MainModule mm = motePS.main;
            mm.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            mm.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
            mm.startColor = new ParticleSystem.MinMaxGradient(mainColor, accentColor);
            ParticleSystem.ShapeModule ms = motePS.shape;
            ms.enabled = true;
            ms.shapeType = ParticleSystemShapeType.Circle;
            ms.radius = 0.25f;
            ms.rotation = new Vector3(90f, 0f, 0f);   // the circle lies flat
            ParticleSystem.VelocityOverLifetimeModule vel = motePS.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.y = new ParticleSystem.MinMaxCurve(-1.2f, -0.6f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            ParticleSystem.EmissionModule em = motePS.emission;
            em.rateOverTime = moteRate;
            em.rateOverDistance = motesPerMeter;
            HealVFXAssets.FadeInOut(motePS, 0.15f, 0.5f, 0.9f);
            HealVFXAssets.SizeCurve(motePS, new AnimationCurve(new Keyframe(0f, 0.8f), new Keyframe(1f, 0.2f)));
        }

        // a few orbs that circle the minion, with short trails (local space: they follow the minion)
        if (showOrbs)
        {
            orbMat = HealVFXAssets.CreateMaterial(HealVFXAssets.Dot, brightness);
            trailMat = HealVFXAssets.CreateMaterial(HealVFXAssets.Ribbon, brightness);
            orbPS = HealVFXAssets.MakeSystem(transform, "MinionOrbs", orbMat, 12, false);
            orbPS.transform.localPosition = orbCenterOffset;
            orbPS.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);   // local Z points up, so the orbit is flat
            ParticleSystem.MainModule om = orbPS.main;
            om.startLifetime = 3f;
            om.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.2f);
            om.startColor = new ParticleSystem.MinMaxGradient(mainColor, accentColor);
            ParticleSystem.ShapeModule os = orbPS.shape;
            os.enabled = true;
            os.shapeType = ParticleSystemShapeType.Circle;
            os.radius = orbRadius;
            os.radiusThickness = 0f;
            ParticleSystem.VelocityOverLifetimeModule ov = orbPS.velocityOverLifetime;
            ov.enabled = true;
            ov.orbitalX = new ParticleSystem.MinMaxCurve(0f);
            ov.orbitalY = new ParticleSystem.MinMaxCurve(0f);
            ov.orbitalZ = new ParticleSystem.MinMaxCurve(orbSpeed);
            ParticleSystem.EmissionModule oe = orbPS.emission;
            oe.rateOverTime = 1.5f;
            HealVFXAssets.FadeInOut(orbPS, 0.15f, 0.8f, 1f);
            ParticleSystem.TrailModule tr = orbPS.trails;
            tr.enabled = true;
            tr.mode = ParticleSystemTrailMode.PerParticle;
            tr.ratio = 1f;
            tr.lifetime = 0.08f;
            tr.minVertexDistance = 0.04f;
            tr.worldSpace = false;
            tr.dieWithParticles = true;
            tr.sizeAffectsWidth = true;
            tr.inheritParticleColor = true;
            tr.textureMode = ParticleSystemTrailTextureMode.Stretch;
            tr.widthOverTrail = new ParticleSystem.MinMaxCurve(0.6f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
            orbPS.GetComponent<ParticleSystemRenderer>().trailMaterial = trailMat;
        }
    }

    // ------------------------------------------------------------------ update
    private void LateUpdate()
    {
        if (!built) return;
        if (dirty)
        {
            dirty = false;
            DestroyBuilt();
            built = false;
            EnsureBuilt();
            return;
        }

        surge = Mathf.MoveTowards(surge, 0f, Time.deltaTime * 3f);
        if (motePS != null)
        {
            ParticleSystem.EmissionModule me = motePS.emission;
            me.rateOverTime = moteRate * (1f + 2f * charge + 3f * surge);   // more motes while it charges and fires
            me.rateOverDistance = motesPerMeter;
        }

        // find the ground below the minion (a long ray: a minion floating high needs it)
        Vector3 found;
        float groundY;
        if (HealVFXAssets.TryGroundPoint(transform, groundMask, groundOffset, groundSearchDistance, out found))
        {
            groundY = found.y;
            lastGroundY = groundY;
            haveGround = true;
            warnedNoGround = false;
        }
        else
        {
            // nothing below: keep the last known ground (and warn once, this is almost always a wrong Ground Mask or a ground without a collider)
            groundY = haveGround ? lastGroundY : transform.position.y - Mathf.Min(1.5f, groundSearchDistance) + groundOffset;
            if (!warnedNoGround)
            {
                warnedNoGround = true;
                Debug.LogWarning("[MinionHoverVFX] No ground found below " + name + ". Check 'Ground Mask' (it must include your ground layer) " +
                                 "and that the ground has a collider. The ring and shadow are placed with a guess until it is found.", this);
            }
        }

        float h = Mathf.Max(0f, transform.position.y - (groundY - groundOffset));
        float t = Mathf.Clamp01(h / Mathf.Max(0.1f, fadeHeight));   // 0 = near the ground, 1 = high up

        // shadow: smaller and fainter the higher the minion floats
        if (shadowMr != null)
        {
            float a = shadowOpacity * Mathf.Lerp(1f, 0.35f, t);
            shadowMr.enabled = a > 0.01f;
            shadowTf.position = new Vector3(transform.position.x, groundY - groundOffset + 0.02f, transform.position.z);
            shadowTf.rotation = Quaternion.identity;
            float r = shadowRadius * Mathf.Lerp(1f, 0.55f, t) / Mathf.Max(0.0001f, transform.lossyScale.x);
            shadowTf.localScale = new Vector3(r, r, r);

            Color c = new Color(0.02f, 0.01f, 0.05f, a);
            shadowBlock.SetColor(HealVFXAssets.BaseColorId, c);
            shadowBlock.SetColor(HealVFXAssets.ColorId, c);
            shadowMr.SetPropertyBlock(shadowBlock);
        }

        // ring: a glowing circle that breathes, smaller and fainter when the minion is high
        if (ring != null)
        {
            float excite = Mathf.Clamp01(charge) * 0.6f + surge;   // charging while it aims, a surge when it fires
            float breathe = 1f + 0.06f * Mathf.Sin(Time.time * 3f + phase) + 0.18f * excite;
            float radius = ringRadius * Mathf.Lerp(1f, 0.6f, t) * breathe;
            Color c = Color.Lerp(mainColor, accentColor, 0.25f + 0.5f * excite) * (Mathf.Lerp(1f, 0.45f, t) * (1f + 1.2f * excite));
            c.a = 1f;

            // the cleared patch of grass has a fixed size, whatever the ring does
            ring.clearGrass = clearGrass;
            ring.clearScale = Mathf.Max(1f, clearRadius / Mathf.Max(0.1f, radius));
            ring.Tick(Time.deltaTime, true, new Vector3(transform.position.x, groundY, transform.position.z), radius, c, 0f, 0.4f, 0.4f);
        }
    }
}