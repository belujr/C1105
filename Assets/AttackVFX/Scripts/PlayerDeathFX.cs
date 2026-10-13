using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// The death of the player: blood and black, in the spirit of Hades. Put it on the player prefab
/// (PlayerHealth also adds it by itself if it is missing). It starts when PlayerHealth raises its Died event.
///
///   1. THE HIT     freeze frame, white flash, ONE hard camera kick along the hit, blood streaks and spray, red shockwave
///   2. BLACK       right after the flash the world is cut away: pitch black, only the player and the effects remain
///   3. THE FALL    the body falls the way it was hit. When it lands, the sigil ignites on the floor and the souls rise
///   4. THE END     the body dissolves into embers, everything fades to black, then the hub can load
///
/// While the player is not dead this component does nothing at all (no per-frame work).
/// Time is slowed and then nearly stopped during the death; it is restored on respawn or scene reload.
/// Everything is built in code on first use.
/// </summary>
public class PlayerDeathFX : MonoBehaviour
{
    /// <summary>Raised when the screen is completely black. Hook your own game-over / respawn code here.</summary>
    public static event System.Action BlackoutComplete;

    [Header("Freeze frame + slow motion")]
    [Tooltip("Seconds the whole game freezes at the moment of death.")]
    public float hitStopTime = 0.1f;
    [Tooltip("Game speed right after the freeze (1 = normal).")]
    public float slowMoScale = 0.35f;
    public float slowMoTime = 0.35f;
    public float slowMoEaseTime = 0.3f;
    [Tooltip("ON: afterwards the world stays almost frozen (enemies stop). OFF: it speeds back up to normal.")]
    public bool freezeWorldAfterDeath = true;
    [Range(0f, 0.2f)] public float endTimeScale = 0.03f;

    [Header("Camera and screen")]
    public Color flashColor = new Color(1f, 0.88f, 0.82f, 1f);
    [Range(0f, 1f)] public float flashStrength = 0.9f;
    public float flashDuration = 0.25f;
    [Tooltip("Color of the vignette, the red light and the first shockwave.")]
    public Color impactColor = new Color(0.75f, 0.02f, 0.04f, 1f);
    [Tooltip("Red vignette for the impact only. It fades out, the screen is pure black afterwards.")]
    [Range(0f, 1f)] public float vignetteStrength = 0.25f;
    [Tooltip("The vignette, the red light and the pool of blood beat like a dying heart.")]
    public bool heartbeat = true;
    public float heartbeatRate = 1.1f;
    [Tooltip("One hard camera kick along the direction of the hit (world units), settling in about a third of a second. 0 = off. Needs URP.")]
    public float cameraShake = 0.4f;
    [Tooltip("How long the small trembling after the kick lasts.")]
    public float shakeDuration = 0.6f;
    [Tooltip("Extra thud when the body lands (0 = none). A second shake is usually too much, so it is off.")]
    public float landShake = 0f;
    [Tooltip("The camera zooms in for a moment (0.07 = 7%).")]
    [Range(0f, 0.2f)] public float zoomPunch = 0.07f;
    [Tooltip("Slow camera push-in over the whole sequence.")]
    [Range(0f, 0.15f)] public float slowPushIn = 0.04f;

    [Header("The world goes black")]
    [Tooltip("ON: right after the flash every renderer, the terrain and the grass are switched off and the camera clears to black. Only the player and the death effects stay. It all comes back on respawn.")]
    public bool hideWorld = true;
    public float blackCutTime = 0.1f;
    [Tooltip("Only used when 'Hide World' is OFF: how dark the world gets.")]
    [Range(0f, 1f)] public float fadeTargetAlpha = 0.9f;

    [Header("The fall")]
    [Tooltip("The body falls over the way it was hit. Needs the model (Animator) to be a child of the player. Turn it off if you use a death animation of your own.")]
    public bool collapseBody = true;
    public float collapseDelay = 0.1f;
    public float collapseTime = 0.3f;
    [Tooltip("How far the body tips over (degrees). 90 = lying flat.")]
    public float collapseAngle = 82f;
    public float collapseSink = 0.08f;

    [Header("The hit: blood, streaks, sparks")]
    public Color bloodColor = new Color(0.55f, 0.01f, 0.03f, 1f);
    public Color bloodBright = new Color(0.95f, 0.06f, 0.06f, 1f);
    [Tooltip("Drops that spray out of the body at the hit.")]
    public int bloodDroplets = 140;
    [Tooltip("Drops that splash up when the body lands.")]
    public int landSplash = 80;
    public bool bloodMist = true;
    [Tooltip("Long thin streaks of light that shoot out of the body at the moment of the hit.")]
    public int impactStreaks = 22;
    public int burstSparks = 30;
    public Color sparkColor = new Color(1f, 0.35f, 0.1f, 1f);
    public float shockwaveRadius = 6f;

    [Header("Blood sigil (ignites on the floor when the body lands)")]
    public bool showSigil = true;
    public Color sigilColor = new Color(0.9f, 0.08f, 0.06f, 1f);
    public float sigilRadius = 2.5f;
    public float sigilBrightness = 2.4f;
    [Tooltip("Only matters when 'Hide World' is OFF: clears the grass under the sigil.")]
    public bool clearGrassUnderSigil = true;

    [Header("Souls (rise from the body once it has landed)")]
    public bool showSouls = true;
    public int soulCount = 14;
    public float soulEmitTime = 1.2f;
    public float soulLifetime = 2f;
    public float circleRadius = 0.8f;
    public float riseHeight = 5f;
    [Tooltip("Radians per second.")]
    public float spinSpeed = 2.6f;
    public Vector2 soulSize = new Vector2(0.12f, 0.2f);
    [Tooltip("Trail length as a fraction of the soul's lifetime.")]
    [Range(0.05f, 0.6f)] public float trailLength = 0.28f;
    [Tooltip("Fraction of a soul's life it needs to fly out of the body to the circle. Lower = a harder burst.")]
    [Range(0.05f, 0.4f)] public float emergeFraction = 0.16f;
    public Color soulColor = new Color(1f, 0.3f, 0.18f, 1f);
    public Color soulAccent = new Color(1f, 0.6f, 0.4f, 1f);
    [Tooltip("HDR brightness of the souls. Lower = more color and less white.")]
    public float soulBrightness = 2f;

    [Header("Atmosphere and finish")]
    [Tooltip("Slow red ash drifting through the black, so the empty background feels alive.")]
    public bool ash = true;
    public float ashRate = 25f;
    [Tooltip("The body dissolves into red embers that rise off it.")]
    public bool showEmbers = true;
    public float emberRate = 140f;
    public Color emberColorA = new Color(1f, 0.3f, 0.1f, 1f);
    public Color emberColorB = new Color(0.7f, 0.04f, 0.03f, 1f);
    [Tooltip("A red light over the body that beats with the heart (only affects lit materials).")]
    public bool redLight = true;
    public float lightIntensity = 2.5f;
    public float lightRange = 6f;
    [Tooltip("HDR brightness of the glowing effects. With Bloom on, higher = bigger glow.")]
    public float brightness = 3f;

    [Header("Timeline (real seconds after death)")]
    public float dissolveDelay = 0.8f;
    public float dissolveDuration = 1.6f;
    [Tooltip("Everything fades to pitch black.")]
    public float blackoutDelay = 2.9f;
    public float blackoutDuration = 1f;
    [Tooltip("Scene to load once the screen is black. Empty = just stay black. The scene must be in Build Settings.")]
    public string hubSceneName = "";
    public float hubLoadDelay = 0.5f;

    [Header("Floor and sound")]
    [Tooltip("Set this to your ground layer so the sigil and the blood sit on the floor.")]
    public LayerMask groundMask = ~0;
    public float groundOffset = 0.04f;
    public AudioClip impactSound;
    public AudioClip landSound;
    [Range(0f, 1f)] public float soundVolume = 1f;

    private static readonly ParticleSystem.Particle[] buf = new ParticleSystem.Particle[128];

    private readonly List<Material> mats = new List<Material>();
    private readonly List<ParticleSystem> emberPS = new List<ParticleSystem>();
    private ParticleSystem sparkPS, flashPS, shockPS, bloodPS, mistPS, streakPS, ashPS, soulPS;
    private HealGroundRune sigil;
    private Transform planeTf;
    private Light bloodLightComp;
    private PlayerDissolveController dissolve;
    private PlayerHealth health;
    private CharacterController cc;
    private Animator anim;

    // the body that falls over
    private Transform visual;
    private Vector3 visualPos;
    private Quaternion visualRot;
    private bool visualSaved;
    private Vector3 fallDir = Vector3.back;
    private float animSpeedBefore = 1f;
    private AnimatorUpdateMode animModeBefore;
    private bool animChanged;

    private readonly DeathWorldHider world = new DeathWorldHider();
    private readonly DeathCameraKick kick = new DeathCameraKick();

    private DeathCamQuad fadeQuad, vignetteQuad, flashQuad, blackQuad;

    private bool built, playing, dissolveStarted, landed, blackoutDone, hubLoading, timeControlled;
    private float startTime;        // real time (unscaled)
    private float blackoutDoneTime;
    private float soulAccum;
    private int soulIndex;

    private float LandTime { get { return collapseDelay + Mathf.Max(0.05f, collapseTime); } }

    // ------------------------------------------------------------------ lifetime
    private void OnEnable()
    {
        PlayerHealth.Died += OnDied;
        kick.Enable();
        ResetAll();
    }

    private void OnDisable()
    {
        PlayerHealth.Died -= OnDied;
        kick.Disable();
        ResetAll();
    }

    private void OnDestroy()
    {
        RestoreTime();
        for (int i = 0; i < mats.Count; i++) if (mats[i] != null) Destroy(mats[i]);
        if (sigil != null) sigil.Dispose();
        if (fadeQuad != null) fadeQuad.Dispose();
        if (vignetteQuad != null) vignetteQuad.Dispose();
        if (flashQuad != null) flashQuad.Dispose();
        if (blackQuad != null) blackQuad.Dispose();
    }

    private void OnDied(PlayerHealth h)
    {
        if (h != null && h.gameObject == gameObject) Begin();
    }

    [ContextMenu("Test Death Sequence (Play mode)")]
    private void TestDeathSequence()
    {
        if (!Application.isPlaying) return;
        if (playing) ResetAll();
        Begin();
    }

    /// <summary>Puts the game speed back to normal (called automatically on respawn / scene reload).</summary>
    public void RestoreTime()
    {
        if (!timeControlled) return;
        timeControlled = false;
        Time.timeScale = 1f;
    }

    // ------------------------------------------------------------------ start / reset
    /// <summary>Play the death sequence (also called automatically when the player dies).</summary>
    public void Begin()
    {
        if (playing) return;
        EnsureBuilt();

        playing = true;
        dissolveStarted = landed = blackoutDone = hubLoading = false;
        soulAccum = 0f;
        soulIndex = 0;
        startTime = Time.unscaledTime;

        // the body falls the way it was hit
        Vector3 hit = health != null ? health.LastHitDirection : Vector3.zero;
        if (hit.sqrMagnitude < 0.0001f) hit = -transform.forward;
        hit.y = 0f;
        fallDir = hit.normalized;

        if (visual != null)
        {
            visualPos = visual.position;
            visualRot = visual.rotation;
            visualSaved = true;
        }

        // stop the idle animation (or let a death animation play in real time)
        if (anim != null)
        {
            animSpeedBefore = anim.speed;
            animModeBefore = anim.updateMode;
            animChanged = true;
            if (health != null && !string.IsNullOrEmpty(health.deathAnimatorTrigger)) anim.updateMode = AnimatorUpdateMode.UnscaledTime;
            else anim.speed = 0f;
        }

        Vector3 c = BodyCenter();
        Vector3 feet = HealVFXAssets.GroundPoint(transform, groundMask, groundOffset);

        // THE HIT: flash, streaks, sparks, shockwave, blood spray, mist
        ParticleSystem.EmitParams fp = new ParticleSystem.EmitParams();
        fp.position = c;
        fp.velocity = Vector3.zero;
        fp.startSize = 3f;
        fp.startLifetime = 0.45f;
        fp.startColor = new Color(impactColor.r, impactColor.g, impactColor.b, 1f);
        flashPS.Emit(fp, 1);

        for (int i = 0; i < impactStreaks; i++)
        {
            Vector3 dir = Random.onUnitSphere;
            dir.y *= 0.5f;
            dir = (dir + fallDir * 0.6f).normalized;
            ParticleSystem.EmitParams sp = new ParticleSystem.EmitParams();
            sp.position = c;
            sp.velocity = dir * Random.Range(28f, 50f);
            sp.startLifetime = Random.Range(0.14f, 0.26f);
            sp.startSize = Random.Range(0.05f, 0.1f);
            sp.startColor = Color.Lerp(flashColor, impactColor, Random.value);
            streakPS.Emit(sp, 1);
        }

        for (int i = 0; i < burstSparks; i++) EmitSpark(c, Random.onUnitSphere, Random.Range(3f, 9f));
        EmitShock(feet, impactColor, shockwaveRadius);
        EmitBlood(c, fallDir, bloodDroplets, 3f, 9f, 0.8f);

        if (bloodMist)
        {
            for (int i = 0; i < 14; i++)
            {
                ParticleSystem.EmitParams mp = new ParticleSystem.EmitParams();
                mp.position = c + Random.insideUnitSphere * 0.3f;
                mp.velocity = fallDir * Random.Range(1f, 3f) + Random.insideUnitSphere;
                mp.startLifetime = Random.Range(0.6f, 1f);
                mp.startSize = Random.Range(0.8f, 1.4f);
                mp.startColor = new Color(bloodColor.r, bloodColor.g, bloodColor.b, 0.55f);
                mistPS.Emit(mp, 1);
            }
        }

        // the ash is already floating in the black when the world is cut away
        if (ash)
        {
            for (int i = 0; i < 70; i++)
            {
                ParticleSystem.EmitParams ap = new ParticleSystem.EmitParams();
                ap.position = feet + new Vector3(Random.Range(-7f, 7f), Random.Range(0f, 5f), Random.Range(-7f, 7f));
                ap.velocity = Vector3.up * Random.Range(0.3f, 0.9f);
                ap.startLifetime = Random.Range(3f, 5f);
                ap.startSize = Random.Range(0.03f, 0.07f);
                ap.startColor = Color.Lerp(emberColorB, emberColorA, Random.value);
                ashPS.Emit(ap, 1);
            }
        }

        PlaySound(impactSound);
    }

    private void ResetAll()
    {
        playing = false;
        blackoutDone = hubLoading = false;
        RestoreTime();
        world.Show();
        kick.Clear();

        // the body gets up again
        if (visual != null && visualSaved)
        {
            visual.position = visualPos;
            visual.rotation = visualRot;
        }
        visualSaved = false;
        if (anim != null && animChanged)
        {
            anim.speed = animSpeedBefore;
            anim.updateMode = animModeBefore;
        }
        animChanged = false;

        if (!built) return;

        // (null checks: when the scene is torn down, children may already be destroyed)
        ClearFx(sparkPS); ClearFx(flashPS); ClearFx(shockPS); ClearFx(bloodPS);
        ClearFx(mistPS); ClearFx(streakPS); ClearFx(ashPS); ClearFx(soulPS);
        for (int i = 0; i < emberPS.Count; i++) ClearFx(emberPS[i]);
        if (sigil != null) sigil.Reset();
        if (bloodLightComp != null) bloodLightComp.enabled = false;
        if (fadeQuad != null) fadeQuad.Hide();
        if (vignetteQuad != null) vignetteQuad.Hide();
        if (flashQuad != null) flashQuad.Hide();
        if (blackQuad != null) blackQuad.Hide();

        if (dissolveStarted && dissolve != null) dissolve.ResetVisible();   // respawn: the player is solid again
        dissolveStarted = false;
    }

    private static void ClearFx(ParticleSystem ps)
    {
        if (ps == null) return;
        ps.Clear();
        HealVFXAssets.SetRate(ps, 0f);
    }

    // ------------------------------------------------------------------ build
    private Material Mat(Texture2D tex, float bright)
    {
        Material m = HealVFXAssets.CreateMaterial(tex, bright);
        mats.Add(m);
        return m;
    }

    // a particle system that keeps playing while the world is slowed or frozen
    private ParticleSystem Fx(string name, Material mat, int max)
    {
        ParticleSystem ps = HealVFXAssets.MakeSystem(transform, name, mat, max, true);
        ParticleSystem.MainModule m = ps.main;
        m.useUnscaledTime = true;
        return ps;
    }

    private void EnsureBuilt()
    {
        if (built) return;
        built = true;

        cc = GetComponent<CharacterController>();
        health = GetComponent<PlayerHealth>();
        dissolve = GetComponentInChildren<PlayerDissolveController>(true);
        if (dissolve == null)
            Debug.LogWarning("[PlayerDeathFX] No PlayerDissolveController found on the player, so the body will not dissolve.", this);

        anim = GetComponentInChildren<Animator>();
        if (anim != null && anim.transform != transform) visual = anim.transform;

        Material dotMat = Mat(HealVFXAssets.Dot, brightness);
        Material ringMat = Mat(HealVFXAssets.Ring, brightness);
        Material ribbonMat = Mat(HealVFXAssets.Ribbon, brightness);
        Material soulMat = Mat(HealVFXAssets.Dot, soulBrightness);
        Material bloodMat = HealVFXAssets.CreateAlphaMaterial(HealVFXAssets.Blob);
        Material mistMat = HealVFXAssets.CreateAlphaMaterial(HealVFXAssets.Dot);
        mats.Add(bloodMat);
        mats.Add(mistMat);

        sparkPS = Fx("DeathSparks", dotMat, 200);
        SetGravity(sparkPS, 1.5f);
        HealVFXAssets.FadeInOut(sparkPS, 0.05f, 0.5f, 1f);
        HealVFXAssets.SizeCurve(sparkPS, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.2f)));

        flashPS = Fx("DeathFlash", dotMat, 4);
        HealVFXAssets.FadeInOut(flashPS, 0.08f, 0.25f, 1f);
        HealVFXAssets.SizeCurve(flashPS, new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(0.3f, 1f), new Keyframe(1f, 1.4f)));

        // streaks of light: stretched along their flight, very short lived
        streakPS = Fx("DeathStreaks", dotMat, 60);
        ParticleSystemRenderer sr = streakPS.GetComponent<ParticleSystemRenderer>();
        sr.renderMode = ParticleSystemRenderMode.Stretch;
        sr.velocityScale = 0.04f;
        sr.lengthScale = 6f;
        HealVFXAssets.FadeInOut(streakPS, 0.02f, 0.4f, 1f);
        HealVFXAssets.SizeCurve(streakPS, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.3f)));

        shockPS = Fx("DeathShock", ringMat, 4);
        shockPS.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        HealVFXAssets.FadeInOut(shockPS, 0.04f, 0.25f, 1f);
        HealVFXAssets.SizeCurve(shockPS, new AnimationCurve(new Keyframe(0f, 0.05f), new Keyframe(0.35f, 0.7f), new Keyframe(1f, 1f)));

        // blood drops: solid, stretched along their flight, they die when they reach the floor
        bloodPS = Fx("DeathBlood", bloodMat, 300);
        SetGravity(bloodPS, 3f);
        ParticleSystemRenderer br = bloodPS.GetComponent<ParticleSystemRenderer>();
        br.renderMode = ParticleSystemRenderMode.Stretch;
        br.velocityScale = 0.05f;
        br.lengthScale = 2f;
        HealVFXAssets.FadeInOut(bloodPS, 0.02f, 0.85f, 1f);
        planeTf = new GameObject("DeathBloodPlane").transform;
        planeTf.SetParent(transform, false);
        ParticleSystem.CollisionModule col = bloodPS.collision;
        col.enabled = true;
        col.type = ParticleSystemCollisionType.Planes;
        col.mode = ParticleSystemCollisionMode.Collision3D;
        col.AddPlane(planeTf);
        col.bounce = 0f;
        col.dampen = 1f;
        col.lifetimeLoss = 1f;

        mistPS = Fx("DeathMist", mistMat, 40);
        HealVFXAssets.FadeInOut(mistPS, 0.1f, 0.3f, 1f);
        HealVFXAssets.SizeCurve(mistPS, new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(1f, 1.4f)));

        // slow ash rising from a wide flat box on the floor
        ashPS = Fx("DeathAsh", dotMat, 220);
        ParticleSystem.MainModule am = ashPS.main;
        am.startLifetime = new ParticleSystem.MinMaxCurve(3.5f, 5.5f);
        am.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.9f);
        am.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
        am.startColor = new ParticleSystem.MinMaxGradient(emberColorB, emberColorA);
        ParticleSystem.ShapeModule ashShape = ashPS.shape;
        ashShape.enabled = true;
        ashShape.shapeType = ParticleSystemShapeType.Box;
        ashShape.scale = new Vector3(14f, 14f, 0.2f);   // the box is rotated so its z axis points up
        HealVFXAssets.FadeInOut(ashPS, 0.2f, 0.7f, 0.8f);

        // souls: an orb with a trail, placed by hand along a rising spiral
        soulPS = Fx("DeathSouls", soulMat, 64);
        HealVFXAssets.FadeInOut(soulPS, 0.06f, 0.6f, 1f);
        HealVFXAssets.SizeCurve(soulPS, new AnimationCurve(
            new Keyframe(0f, 0.3f), new Keyframe(0.15f, 1f), new Keyframe(0.7f, 1f), new Keyframe(1f, 0.2f)));
        ParticleSystem.TrailModule tr = soulPS.trails;
        tr.enabled = true;
        tr.mode = ParticleSystemTrailMode.PerParticle;
        tr.ratio = 1f;
        tr.lifetime = trailLength;
        tr.minVertexDistance = 0.05f;
        tr.worldSpace = true;
        tr.dieWithParticles = true;
        tr.sizeAffectsWidth = true;
        tr.sizeAffectsLifetime = false;
        tr.inheritParticleColor = true;
        tr.textureMode = ParticleSystemTrailTextureMode.Stretch;
        tr.widthOverTrail = new ParticleSystem.MinMaxCurve(0.25f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
        Gradient fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        tr.colorOverLifetime = new ParticleSystem.MinMaxGradient(fade);
        soulPS.GetComponent<ParticleSystemRenderer>().trailMaterial = ribbonMat;

        if (showEmbers) BuildEmbers(dotMat);

        // the blood sigil on the floor
        if (showSigil)
        {
            sigil = new HealGroundRune(transform, "DeathSigil", HealVFXAssets.Sigil, sigilBrightness);
            sigil.clearGrass = clearGrassUnderSigil && !hideWorld;
            sigil.startScale = 0.4f;   // it spreads out across the floor
            if (GetComponent<EnemyHitFX>() != null)
            {
                HealVFXAssets.ProtectFromHitFX(gameObject, sigil.Renderer);
                HealVFXAssets.ProtectFromHitFX(gameObject, sigil.MaskRenderer);
            }
        }

        // a red light over the body
        if (redLight)
        {
            GameObject lg = new GameObject("DeathLight");
            lg.transform.SetParent(transform, false);
            bloodLightComp = lg.AddComponent<Light>();
            bloodLightComp.type = LightType.Point;
            bloodLightComp.color = bloodBright;
            bloodLightComp.range = lightRange;
            bloodLightComp.intensity = 0f;
            bloodLightComp.shadows = LightShadows.None;
            bloodLightComp.enabled = false;
        }
    }

    private static void SetGravity(ParticleSystem ps, float g)
    {
        ParticleSystem.MainModule m = ps.main;
        m.gravityModifier = g;
    }

    // embers that rise off the body surface while it dissolves
    private void BuildEmbers(Material dotMat)
    {
        foreach (SkinnedMeshRenderer smr in GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (emberPS.Count >= 4) break;
            if (smr.sharedMesh == null || smr.sharedMesh.vertexCount < 100) continue;
            string n = smr.gameObject.name;
            if (n.EndsWith("Shell") || n.Contains("GlowOverlay")) continue;

            ParticleSystem ps = Fx("DeathEmbers", dotMat, 200);
            ParticleSystem.MainModule em = ps.main;
            em.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.3f);
            em.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.4f);
            em.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
            em.startColor = new ParticleSystem.MinMaxGradient(emberColorA, emberColorB);

            ParticleSystem.ShapeModule sh = ps.shape;
            sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.SkinnedMeshRenderer;
            sh.skinnedMeshRenderer = smr;
            sh.meshShapeType = ParticleSystemMeshShapeType.Triangle;
            sh.normalOffset = 0.02f;

            ParticleSystem.VelocityOverLifetimeModule vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.y = new ParticleSystem.MinMaxCurve(0.7f, 1.5f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            HealVFXAssets.FadeInOut(ps, 0.1f, 0.5f, 1f);
            HealVFXAssets.SizeCurve(ps, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.2f)));
            emberPS.Add(ps);
        }
    }

    // ------------------------------------------------------------------ helpers
    private Vector3 BodyCenter()
    {
        if (cc != null) return transform.TransformPoint(cc.center) + Vector3.up * (cc.height * 0.1f);
        return transform.position + Vector3.up;
    }

    private static float S(float x)
    {
        x = Mathf.Clamp01(x);
        return x * x * (3f - 2f * x);
    }

    // one heartbeat: a strong beat and a weaker one right after, fading as the player dies
    private float Beat(float T)
    {
        if (!heartbeat) return 0f;
        float c = Mathf.Repeat(T * heartbeatRate, 1f);
        float a = (c - 0.05f) / 0.05f;
        float b = (c - 0.28f) / 0.06f;
        return (Mathf.Exp(-a * a) + 0.6f * Mathf.Exp(-b * b)) * Mathf.Exp(-T * 0.35f);
    }

    private void PlaySound(AudioClip clip)
    {
        if (clip == null) return;
        Camera cam = Camera.main;
        AudioSource.PlayClipAtPoint(clip, cam != null ? cam.transform.position : transform.position, soundVolume);
    }

    private void EmitSpark(Vector3 pos, Vector3 dir, float speed)
    {
        dir.y = Mathf.Abs(dir.y) * 0.6f + 0.2f;
        ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();
        ep.position = pos;
        ep.velocity = dir.normalized * speed;
        ep.startLifetime = Random.Range(0.5f, 1.1f);
        ep.startSize = Random.Range(0.04f, 0.09f);
        ep.startColor = Color.Lerp(sparkColor, emberColorA, Random.value);
        sparkPS.Emit(ep, 1);
    }

    // a spray of blood drops: baseDir is the general direction, spread how wild it is
    private void EmitBlood(Vector3 pos, Vector3 baseDir, int count, float speedMin, float speedMax, float spread)
    {
        for (int i = 0; i < count; i++)
        {
            Vector3 dir = (baseDir + Random.onUnitSphere * spread + Vector3.up * 0.4f).normalized;
            ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();
            ep.position = pos + Random.insideUnitSphere * 0.1f;
            ep.velocity = dir * Random.Range(speedMin, speedMax);
            ep.startLifetime = Random.Range(0.6f, 1.2f);
            ep.startSize = Random.Range(0.05f, 0.16f);
            Color c = Color.Lerp(bloodColor, bloodBright, Random.value);
            c.a = 1f;
            ep.startColor = c;
            bloodPS.Emit(ep, 1);
        }
    }

    private void EmitShock(Vector3 feet, Color color, float radius)
    {
        ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();
        ep.position = feet;
        ep.velocity = Vector3.zero;
        ep.startSize = radius * 2f;
        ep.startLifetime = 0.55f;
        ep.startColor = color;
        shockPS.Emit(ep, 1);
    }

    // ------------------------------------------------------------------ update
    private void LateUpdate()
    {
        if (!built || !playing) return;   // nothing to do until the player dies

        float dt = Time.unscaledDeltaTime;
        float T = Time.unscaledTime - startTime;
        Camera cam = Camera.main;
        Vector3 center = BodyCenter();
        Vector3 feet = HealVFXAssets.GroundPoint(transform, groundMask, groundOffset);

        UpdateTime(T);
        UpdateScreen(T, cam);
        UpdateCamera(T, cam);

        // right after the flash the whole world is cut away
        if (hideWorld && !world.IsHidden && T >= blackCutTime) world.Hide(cam, transform);

        if (planeTf != null) planeTf.position = feet;   // blood drops die when they reach the floor
        UpdateCollapse(T, feet);
        UpdateSigil(T, dt, feet);
        UpdateSouls(T, dt, feet);
        UpdateAtmosphere(T, feet);
        UpdateLight(T, center);

        // the body dissolves
        if (!dissolveStarted && T >= dissolveDelay)
        {
            dissolveStarted = true;
            if (dissolve != null) dissolve.TriggerDissolveOut(dissolveDuration, true);
        }
    }

    // the body falls over; when it lands the blood splashes up and the sigil ignites
    private void UpdateCollapse(float T, Vector3 feet)
    {
        float landT = LandTime;

        if (collapseBody && visual != null && visualSaved)
        {
            float t = Mathf.Clamp01((T - collapseDelay) / Mathf.Max(0.05f, collapseTime));
            float fall = Mathf.Pow(t, 2.2f);                       // accelerates like a real fall
            float after = T - landT;
            float wob = after > 0f ? 0.05f * Mathf.Sin(after * 22f) * Mathf.Exp(-after * 9f) : 0f;   // small bounce when it lands

            visual.rotation = Quaternion.AngleAxis(collapseAngle * (fall + wob), Vector3.Cross(Vector3.up, fallDir)) * visualRot;
            visual.position = visualPos + fallDir * (0.35f * fall) + Vector3.down * (collapseSink * fall);
        }

        if (!landed && T >= landT)
        {
            landed = true;
            EmitBlood(feet + fallDir * 0.35f + Vector3.up * 0.1f, Vector3.up * 0.8f, landSplash, 2f, 5.5f, 0.9f);
            EmitShock(feet, sigilColor, 3.2f);   // a small ring: the sigil ignites from the body outward
            PlaySound(landSound);
        }
    }

    private void UpdateSigil(float T, float dt, Vector3 feet)
    {
        if (sigil == null) return;
        bool on = T >= LandTime && T < blackoutDelay + 0.3f;
        if (!on && !sigil.IsVisible) return;

        sigil.clearGrass = clearGrassUnderSigil && !hideWorld;
        sigil.Tick(dt, on, feet + Vector3.up * 0.02f, sigilRadius, sigilColor, -25f, 0.3f, 0.5f);
    }

    // souls leave the body once it has landed
    private void UpdateSouls(float T, float dt, Vector3 feet)
    {
        Vector3 center = feet + fallDir * 0.35f + Vector3.up * 0.35f;
        float landT = LandTime;

        if (showSouls && soulCount > 0 && T >= landT && T < landT + soulEmitTime)
        {
            soulAccum += (soulCount / Mathf.Max(0.1f, soulEmitTime)) * dt;
            int n = (int)soulAccum;
            soulAccum -= n;
            for (; n > 0; n--) EmitSoul(center);
        }
        if (soulPS.particleCount > 0) RepositionSouls(center);
    }

    private void EmitSoul(Vector3 center)
    {
        // golden-ratio spacing: souls are spread evenly around the circle
        float r01 = Mathf.Repeat(soulIndex * 0.61803f, 1f);
        soulIndex++;
        int rnd = Random.Range(0, 1000);

        ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();
        ep.position = center;
        ep.velocity = Vector3.zero;
        ep.startLifetime = soulLifetime * Random.Range(0.9f, 1.1f);
        ep.startSize = Mathf.Lerp(soulSize.x, soulSize.y, rnd / 1000f);
        ep.startColor = Color.Lerp(soulColor, soulAccent, Random.value * 0.5f);
        ep.randomSeed = (uint)(rnd * 1000 + Mathf.Clamp(Mathf.FloorToInt(r01 * 999f), 0, 999));
        soulPS.Emit(ep, 1);
    }

    // each soul leaves the body, swings out to a circle, then spirals upward while the circle narrows
    private void RepositionSouls(Vector3 center)
    {
        int n = soulPS.GetParticles(buf);

        for (int i = 0; i < n; i++)
        {
            ParticleSystem.Particle p = buf[i];
            float age = p.startLifetime - p.remainingLifetime;
            float u = Mathf.Clamp01(age / Mathf.Max(0.0001f, p.startLifetime));

            uint seed = p.randomSeed;
            float r01 = (seed % 1000u) / 1000f;
            float r02 = ((seed / 1000u) % 1000u) / 1000f;

            float emerge = S(u / Mathf.Max(0.05f, emergeFraction));
            float eo = 1f - (1f - emerge) * (1f - emerge);
            float radius = Mathf.Lerp(0.12f, circleRadius * (0.8f + 0.4f * r02), eo);
            radius *= 1f - 0.45f * S((u - 0.55f) / 0.45f);

            float ang = r01 * Mathf.PI * 2f + spinSpeed * age * (1f + 0.2f * (r02 - 0.5f));
            float rise = riseHeight * Mathf.Pow(u, 1.8f) * (0.8f + 0.4f * r02);

            p.position = center + new Vector3(Mathf.Cos(ang) * radius, 0.4f * eo + rise, Mathf.Sin(ang) * radius);
            p.velocity = Vector3.zero;
            p.startSize = Mathf.Lerp(soulSize.x, soulSize.y, r02) * (1f + 0.2f * Mathf.Sin(age * 10f + r01 * 6.2832f));
            buf[i] = p;
        }
        soulPS.SetParticles(buf, n);
    }

    // ash drifting through the black, embers rising off the dissolving body
    private void UpdateAtmosphere(float T, Vector3 feet)
    {
        ashPS.transform.SetPositionAndRotation(feet, Quaternion.Euler(-90f, 0f, 0f));
        HealVFXAssets.SetRate(ashPS, ash && T >= 0.3f && T < blackoutDelay ? ashRate : 0f);

        float rate = 0f;
        if (showEmbers)
        {
            float prog = S((T - dissolveDelay) / Mathf.Max(0.1f, dissolveDuration));
            float tail = 1f - S((T - dissolveDelay - dissolveDuration) / 0.6f);
            rate = emberRate * prog * tail / Mathf.Max(1, emberPS.Count);
        }
        for (int i = 0; i < emberPS.Count; i++) HealVFXAssets.SetRate(emberPS[i], rate);
    }

    private void UpdateLight(float T, Vector3 center)
    {
        if (bloodLightComp == null) return;
        bloodLightComp.enabled = true;
        bloodLightComp.transform.position = center + Vector3.up * 0.6f;
        bloodLightComp.range = lightRange;
        bloodLightComp.color = bloodBright;
        bloodLightComp.intensity = lightIntensity * S(T / 0.15f) * (0.7f + 0.8f * Beat(T));
    }

    // ------------------------------------------------------------------ slow motion
    // freeze frame -> slow motion -> (nearly) stopped world. Set every frame, so other hit-stop code cannot undo it.
    private void UpdateTime(float T)
    {
        float endScale = freezeWorldAfterDeath ? endTimeScale : 1f;
        float target;
        if (T < hitStopTime)
        {
            target = 0f;
        }
        else
        {
            float t2 = T - hitStopTime;
            target = t2 < slowMoTime
                ? slowMoScale
                : Mathf.Lerp(slowMoScale, endScale, S((t2 - slowMoTime) / Mathf.Max(0.01f, slowMoEaseTime)));
        }

        timeControlled = true;
        Time.timeScale = target;
    }

    // ------------------------------------------------------------------ screen: flash, vignette, blackout
    private void UpdateScreen(float T, Camera cam)
    {
        if (cam == null) return;

        if (fadeQuad == null)
        {
            fadeQuad = DeathCamQuad.Create("DeathScreenFade", 2995, null);                      // dims the world (only when it is not hidden)
            vignetteQuad = DeathCamQuad.Create("DeathVignette", 4000, HealVFXAssets.Vignette);
            blackQuad = DeathCamQuad.Create("DeathBlackout", 4001, null);                       // pitch black over everything
            flashQuad = DeathCamQuad.Create("DeathFlash", 4002, null);                          // over everything, even the blackout
        }

        fadeQuad.Set(cam, new Color(0f, 0f, 0f, hideWorld ? 0f : fadeTargetAlpha * S(T / 0.12f)), 1.35f);

        // the red vignette is only for the impact: it fades out and the screen is pure black afterwards
        float vig = Mathf.Clamp01(vignetteStrength * S(T / 0.15f) * (0.65f + 0.7f * Beat(T)) * (1f - S((T - 0.5f) / 1f)));
        vignetteQuad.Set(cam, new Color(impactColor.r, impactColor.g, impactColor.b, vig), 1.05f);

        float fl = flashStrength * Mathf.Pow(1f - Mathf.Clamp01(T / Mathf.Max(0.01f, flashDuration)), 2f);
        flashQuad.Set(cam, new Color(flashColor.r, flashColor.g, flashColor.b, fl), 1.35f);

        // everything fades to pitch black once the effects are done, then the hub loads
        float b = S((T - blackoutDelay) / Mathf.Max(0.01f, blackoutDuration));
        blackQuad.Set(cam, new Color(0f, 0f, 0f, b), 1.35f);

        if (!blackoutDone && b >= 0.999f)
        {
            blackoutDone = true;
            blackoutDoneTime = T;
            if (BlackoutComplete != null) BlackoutComplete();
        }

        if (blackoutDone && !hubLoading && !string.IsNullOrEmpty(hubSceneName) && T >= blackoutDoneTime + hubLoadDelay)
        {
            hubLoading = true;
            RestoreTime();   // the next scene must not start in slow motion
            if (Application.CanStreamedLevelBeLoaded(hubSceneName))
            {
                CameraTransitionManager.RequestGameplayEntry(hubSceneName);
                SceneManager.LoadScene(hubSceneName);
            }
            else Debug.LogError("[PlayerDeathFX] Scene '" + hubSceneName + "' is not in Build Settings, so the hub cannot load.", this);
        }
    }

    // ------------------------------------------------------------------ camera: ONE hard kick, a short tremble, zoom
    private void UpdateCamera(float T, Camera cam)
    {
        if (cam == null) return;

        // the kick: the camera is shoved along the hit and settles in about a third of a second (one move, not a wobble)
        Vector3 offset = fallDir * (cameraShake * 1.2f * Mathf.Exp(-T * 9f));

        // a small tremble that dies out smoothly
        float k = 1f - Mathf.Clamp01(T / Mathf.Max(0.01f, shakeDuration));
        float env = k * k;
        if (landShake > 0f && T >= LandTime)
        {
            float k2 = 1f - Mathf.Clamp01((T - LandTime) / 0.4f);
            env = Mathf.Max(env, landShake * k2 * k2 / Mathf.Max(0.01f, cameraShake));
        }
        Vector2 r = Random.insideUnitCircle * (cameraShake * 0.6f * env);
        offset += cam.transform.right * r.x + cam.transform.up * r.y;

        // zoom in fast and ease back out, then a slow push-in
        float zoom = T < 0.6f ? zoomPunch * Mathf.Sin(Mathf.PI * Mathf.Clamp01(T / 0.6f)) : 0f;
        float push = slowPushIn * S(T / Mathf.Max(0.1f, blackoutDelay + blackoutDuration));
        kick.Set(cam, offset, (1f - zoom) * (1f - push));
    }
}