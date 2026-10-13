using UnityEngine;

/// <summary>
/// Shared effects of the enemy bullets. ONE object plays everything (created automatically by the first BulletVisual):
///   Pop      the end of a bullet: a small flash, a ring and shards of light (hit, wall, or end of its life)
///   Sparkle  a small four-pointed star that twinkles in the air behind a flying bullet
/// Budgets limit how many pops and sparkles can play in one frame, so a bullet-hell wave cannot flood the screen.
/// You can edit this component live in Play mode: it rebuilds itself when a value changes.
/// </summary>
public class BulletImpactFX : MonoBehaviour
{
    public static BulletImpactFX Instance { get; private set; }

    [Tooltip("Pops beyond this many in one frame are skipped (bullet-hell safety).")]
    public int maxPopsPerFrame = 12;
    [Tooltip("Sparkles beyond this many in one frame are skipped.")]
    public int maxSparklesPerFrame = 60;
    [Tooltip("HDR brightness multiplier. With Bloom on, higher = bigger glow.")]
    public float brightness = 2f;
    public int shardsPerPop = 6;
    [Tooltip("Size of the ring of a pop, compared to the bullet.")]
    public float popRingScale = 1.8f;
    [Tooltip("Size of the sparkle stars.")]
    public Vector2 sparkleSize = new Vector2(0.08f, 0.18f);

    private ParticleSystem flashPS, ringPS, shardPS, sparklePS;
    private int popFrame, popsThisFrame, sparkleFrame, sparklesThisFrame;
    private bool built, rebuild;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { Instance = null; }

    public static void EnsureCreated()
    {
        if (Instance != null) return;
        new GameObject("BulletImpactFX").AddComponent<BulletImpactFX>();
    }

    /// <param name="intensity">1 = full pop, less = a quieter fizzle (fewer shards).</param>
    public static void Pop(Vector3 position, Color main, Color core, float size, float intensity = 1f)
    {
        EnsureCreated();
        Instance.DoPop(position, main, core, size, intensity);
    }

    /// <summary>A twinkling star left behind by a flying bullet. sizeMul 2 = a bigger flare.</summary>
    public static void Sparkle(Vector3 position, Color color, float sizeMul = 1f)
    {
        EnsureCreated();
        Instance.DoSparkle(position, color, sizeMul);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Build();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // editing a value in Play mode rebuilds the effect, so every setting works live
    private void OnValidate()
    {
        if (Application.isPlaying && built) rebuild = true;
    }

    private void Update()
    {
        if (!rebuild) return;
        rebuild = false;
        for (int i = transform.childCount - 1; i >= 0; i--) Destroy(transform.GetChild(i).gameObject);
        built = false;
        Build();
    }

    private void Build()
    {
        if (built) return;
        built = true;

        Material dotMat = HealVFXAssets.CreateMaterial(HealVFXAssets.Dot, brightness);
        Material ringMat = HealVFXAssets.CreateMaterial(HealVFXAssets.Ring, brightness);
        Material starMat = HealVFXAssets.CreateMaterial(HealVFXAssets.BulletStar, brightness);

        flashPS = HealVFXAssets.MakeSystem(transform, "PopFlash", dotMat, 64, true);
        HealVFXAssets.FadeInOut(flashPS, 0.05f, 0.3f, 1f);
        HealVFXAssets.SizeCurve(flashPS, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 1.2f)));

        ringPS = HealVFXAssets.MakeSystem(transform, "PopRing", ringMat, 64, true);   // faces the camera
        HealVFXAssets.FadeInOut(ringPS, 0.05f, 0.3f, 0.8f);
        HealVFXAssets.SizeCurve(ringPS, new AnimationCurve(new Keyframe(0f, 0.2f), new Keyframe(0.4f, 0.8f), new Keyframe(1f, 1f)));

        // long thin shards of light
        shardPS = HealVFXAssets.MakeSystem(transform, "PopShards", dotMat, 300, true);
        ParticleSystemRenderer sr = shardPS.GetComponent<ParticleSystemRenderer>();
        sr.renderMode = ParticleSystemRenderMode.Stretch;
        sr.velocityScale = 0.05f;
        sr.lengthScale = 6f;
        HealVFXAssets.FadeInOut(shardPS, 0.05f, 0.5f, 1f);
        HealVFXAssets.SizeCurve(shardPS, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.2f)));

        // twinkling stars: they turn while they fade
        sparklePS = HealVFXAssets.MakeSystem(transform, "BulletSparkles", starMat, 1500, true);
        HealVFXAssets.FadeInOut(sparklePS, 0.1f, 0.4f, 1f);
        HealVFXAssets.SizeCurve(sparklePS, new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0f)));
        ParticleSystem.RotationOverLifetimeModule rot = sparklePS.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-2f, 2f);
    }

    private void Emit(ParticleSystem ps, Vector3 pos, Vector3 vel, float life, float size, Color color)
    {
        if (ps == null) return;
        ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();
        ep.position = pos;
        ep.velocity = vel;
        ep.startLifetime = life;
        ep.startSize = size;
        ep.startColor = color;
        ps.Emit(ep, 1);
    }

    private void DoPop(Vector3 pos, Color main, Color core, float size, float intensity)
    {
        if (popFrame != Time.frameCount) { popFrame = Time.frameCount; popsThisFrame = 0; }
        if (popsThisFrame++ >= maxPopsPerFrame) return;

        Emit(flashPS, pos, Vector3.zero, 0.12f, size * 1.1f, core);
        Emit(ringPS, pos, Vector3.zero, 0.2f, size * popRingScale, main);
        int shards = Mathf.RoundToInt(shardsPerPop * Mathf.Clamp01(intensity));
        for (int i = 0; i < shards; i++)
            Emit(shardPS, pos, Random.onUnitSphere * Random.Range(3f, 7f), Random.Range(0.2f, 0.4f),
                 Random.Range(0.03f, 0.06f), Random.value < 0.5f ? main : core);
        if (intensity > 0.5f) Emit(sparklePS, pos, Vector3.zero, 0.3f, size * 0.9f, core);   // a flare in the middle of a full pop
    }

    private void DoSparkle(Vector3 pos, Color color, float sizeMul)
    {
        if (sparkleFrame != Time.frameCount) { sparkleFrame = Time.frameCount; sparklesThisFrame = 0; }
        if (sparklesThisFrame++ >= maxSparklesPerFrame) return;

        Emit(sparklePS, pos, Random.insideUnitSphere * 0.2f, Random.Range(0.35f, 0.7f),
             Random.Range(sparkleSize.x, sparkleSize.y) * sizeMul, color);
    }
}