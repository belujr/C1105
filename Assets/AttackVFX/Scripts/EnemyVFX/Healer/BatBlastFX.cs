using UnityEngine;

/// <summary>
/// The blast of an exploding Kamikaze Bat. ONE shared object plays every explosion (it is created automatically
/// the first time a bat spawns, you don't have to add it; you can also drop it into the scene to edit the colors).
/// The rings grow to the real damage radius, so the player can read how far the blast reaches.
/// Cyan and violet, like the bats and the Healer's summon: flash, a fireball, two shockwave rings,
/// streaks of light, dark smoke and drifting embers.
/// </summary>
public class BatBlastFX : MonoBehaviour
{
    public static BatBlastFX Instance { get; private set; }

    [Header("Look")]
    public Color coreColor = new Color(0.9f, 0.98f, 1f, 1f);
    public Color mainColor = new Color(0.25f, 0.75f, 1f, 1f);
    public Color accentColor = new Color(0.7f, 0.45f, 1f, 1f);
    public Color smokeColor = new Color(0.22f, 0.14f, 0.38f, 0.5f);
    [Tooltip("HDR brightness multiplier. With Bloom on, higher = bigger glow.")]
    public float brightness = 2.8f;

    [Header("Amounts")]
    public int sparks = 28;
    public bool smoke = true;
    public int embers = 12;

    private ParticleSystem flashPS, corePS, ringPS, ringGroundPS, sparkPS, smokePS, emberPS;
    private bool built;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { Instance = null; }

    /// <summary>Makes sure the shared blast object exists (call it when a bat spawns, so nothing is built during an explosion).</summary>
    public static void EnsureCreated()
    {
        if (Instance != null) return;
        new GameObject("BatBlastFX").AddComponent<BatBlastFX>();
    }

    public static void Play(Vector3 position, float groundY, float radius)
    {
        EnsureCreated();
        Instance.Blast(position, groundY, radius);
    }

    /// <summary>A small puff, for a bat that is killed by the player.</summary>
    public static void PlayPuff(Vector3 position)
    {
        EnsureCreated();
        Instance.Puff(position);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        Build();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ------------------------------------------------------------------ build
    private void Build()
    {
        if (built) return;
        built = true;

        Material dotMat = HealVFXAssets.CreateMaterial(HealVFXAssets.Dot, brightness);
        Material ringMat = HealVFXAssets.CreateMaterial(HealVFXAssets.Ring, brightness);
        Material smokeMat = HealVFXAssets.CreateAlphaMaterial(HealVFXAssets.Dot);

        flashPS = HealVFXAssets.MakeSystem(transform, "BlastFlash", dotMat, 16, true);
        HealVFXAssets.FadeInOut(flashPS, 0.05f, 0.3f, 1f);
        HealVFXAssets.SizeCurve(flashPS, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.3f, 1f), new Keyframe(1f, 1.3f)));

        corePS = HealVFXAssets.MakeSystem(transform, "BlastCore", dotMat, 64, true);
        HealVFXAssets.FadeInOut(corePS, 0.05f, 0.3f, 1f);
        HealVFXAssets.SizeCurve(corePS, new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(0.4f, 1f), new Keyframe(1f, 1.2f)));

        ringPS = HealVFXAssets.MakeSystem(transform, "BlastRing", ringMat, 16, true);   // faces the camera
        HealVFXAssets.FadeInOut(ringPS, 0.05f, 0.3f, 1f);
        HealVFXAssets.SizeCurve(ringPS, new AnimationCurve(new Keyframe(0f, 0.1f), new Keyframe(0.4f, 0.75f), new Keyframe(1f, 1f)));

        ringGroundPS = HealVFXAssets.MakeSystem(transform, "BlastGroundRing", ringMat, 16, true);
        ringGroundPS.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        HealVFXAssets.FadeInOut(ringGroundPS, 0.05f, 0.3f, 1f);
        HealVFXAssets.SizeCurve(ringGroundPS, new AnimationCurve(new Keyframe(0f, 0.1f), new Keyframe(0.4f, 0.75f), new Keyframe(1f, 1f)));

        sparkPS = HealVFXAssets.MakeSystem(transform, "BlastSparks", dotMat, 300, true);
        ParticleSystem.MainModule sm = sparkPS.main;
        sm.gravityModifier = 1.5f;
        ParticleSystemRenderer sr = sparkPS.GetComponent<ParticleSystemRenderer>();
        sr.renderMode = ParticleSystemRenderMode.Stretch;
        sr.velocityScale = 0.04f;
        sr.lengthScale = 5f;
        HealVFXAssets.FadeInOut(sparkPS, 0.05f, 0.5f, 1f);
        HealVFXAssets.SizeCurve(sparkPS, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.2f)));

        smokePS = HealVFXAssets.MakeSystem(transform, "BlastSmoke", smokeMat, 64, true);
        HealVFXAssets.FadeInOut(smokePS, 0.15f, 0.4f, 1f);
        HealVFXAssets.SizeCurve(smokePS, new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(1f, 1.3f)));

        emberPS = HealVFXAssets.MakeSystem(transform, "BlastEmbers", dotMat, 120, true);
        ParticleSystem.MainModule em = emberPS.main;
        em.gravityModifier = -0.15f;   // they drift upward
        HealVFXAssets.FadeInOut(emberPS, 0.1f, 0.5f, 1f);
        HealVFXAssets.SizeCurve(emberPS, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.2f)));
    }

    // ------------------------------------------------------------------ play
    private Color Pick()
    {
        float r = Random.value;
        return r < 0.45f ? mainColor : (r < 0.8f ? coreColor : accentColor);
    }

    private void EmitOne(ParticleSystem ps, Vector3 pos, Vector3 vel, float life, float size, Color color)
    {
        ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();
        ep.position = pos;
        ep.velocity = vel;
        ep.startLifetime = life;
        ep.startSize = size;
        ep.startColor = color;
        ps.Emit(ep, 1);
    }

    private void Blast(Vector3 pos, float groundY, float radius)
    {
        float k = radius / 3.5f;   // everything scales with the damage radius (3.5 = the bat's default)

        EmitOne(flashPS, pos, Vector3.zero, 0.2f, radius * 1.3f, coreColor);

        // the fireball: a few soft blobs that swell
        for (int i = 0; i < 6; i++)
            EmitOne(corePS, pos + Random.insideUnitSphere * 0.2f * radius, Vector3.zero,
                    Random.Range(0.3f, 0.45f), radius * Random.Range(0.5f, 0.9f), Color.Lerp(coreColor, mainColor, Random.value));

        // two shockwave rings that reach the real damage radius: one standing (faces the camera), one on the floor
        EmitOne(ringPS, pos, Vector3.zero, 0.3f, radius * 1.6f, coreColor);
        EmitOne(ringGroundPS, new Vector3(pos.x, groundY + 0.05f, pos.z), Vector3.zero, 0.45f, radius * 2f, mainColor);

        // streaks of light
        for (int i = 0; i < sparks; i++)
            EmitOne(sparkPS, pos, Random.onUnitSphere * Random.Range(5f, 13f) * k,
                    Random.Range(0.35f, 0.7f), Random.Range(0.05f, 0.1f), Pick());

        if (smoke)
        {
            for (int i = 0; i < 8; i++)
                EmitOne(smokePS, pos + Random.insideUnitSphere * 0.4f * radius,
                        Vector3.up * Random.Range(0.4f, 1.2f) + Random.insideUnitSphere * 0.5f,
                        Random.Range(0.8f, 1.2f), radius * Random.Range(0.35f, 0.6f), smokeColor);
        }

        for (int i = 0; i < embers; i++)
            EmitOne(emberPS, pos, Random.insideUnitSphere * 1.5f * k, Random.Range(0.8f, 1.4f),
                    Random.Range(0.04f, 0.08f), Pick());
    }

    private void Puff(Vector3 pos)
    {
        EmitOne(flashPS, pos, Vector3.zero, 0.15f, 1.2f, coreColor);
        EmitOne(ringPS, pos, Vector3.zero, 0.25f, 1.6f, mainColor);
        for (int i = 0; i < 12; i++)
            EmitOne(sparkPS, pos, Random.onUnitSphere * Random.Range(2f, 6f),
                    Random.Range(0.3f, 0.5f), Random.Range(0.04f, 0.08f), Pick());
    }
}
