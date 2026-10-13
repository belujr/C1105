using UnityEngine;

/// <summary>
/// Visuals of a Kamikaze Bat (added automatically by KamikazeBatController, put it on the bat prefab to edit the values):
///   - a glowing trail that fades behind the bat, and a few glints that drift off it
///   - when the fuse is lit the trail turns hot orange-red and gets thicker
///   - the explosion and the death puff are played by the shared BatBlastFX
/// Works with pooled bats: the trail is cleared whenever a bat is (re)spawned, so there is never a streak
/// from the old pool position to the new one.
/// </summary>
public class BatVFX : MonoBehaviour
{
    [Header("Trail")]
    public bool showTrail = true;
    public Color trailColor = new Color(0.3f, 0.8f, 1f, 1f);
    public Color trailAccent = new Color(0.65f, 0.4f, 1f, 1f);
    [Tooltip("Trail color while the fuse is burning.")]
    public Color fuseColor = new Color(1f, 0.4f, 0.12f, 1f);
    [Tooltip("How long the trail lasts, in seconds.")]
    public float trailTime = 0.45f;
    public float trailWidth = 0.3f;
    [Tooltip("The trail starts here, relative to the bat (the middle of the body).")]
    public Vector3 trailOffset = Vector3.zero;

    [Header("Glints")]
    public bool glints = true;
    [Tooltip("Small sparkles that drift off the bat, per meter it flies.")]
    public float glintsPerMeter = 5f;

    [Tooltip("HDR brightness multiplier. With Bloom on, higher = bigger glow.")]
    public float brightness = 2.5f;

    private static Material sharedTrailMat;
    private TrailRenderer trail;
    private ParticleSystem glintPS;
    private Gradient normalGradient, fuseGradient;
    private bool built, fusing;
    private float fuseStart, fuseDur = 1.5f;
    private int startDelay;

    // ------------------------------------------------------------------ API (called by KamikazeBatController)
    /// <summary>The fuse is lit: the trail turns hot and thick.</summary>
    public void SetFusing(float duration)
    {
        EnsureBuilt();
        fusing = true;
        fuseStart = Time.time;
        fuseDur = Mathf.Max(0.1f, duration);
        if (trail != null) trail.colorGradient = fuseGradient;
    }

    /// <summary>The bat explodes. The ring grows to the damage radius.</summary>
    public void PlayExplosion(Vector3 position, float groundY, float radius)
    {
        BatBlastFX.Play(position, groundY, radius);
        StopTrail();
    }

    /// <summary>The player killed the bat: a small puff.</summary>
    public void PlayDeathPuff(Vector3 position)
    {
        BatBlastFX.PlayPuff(position);
        StopTrail();
    }

    // ------------------------------------------------------------------ lifecycle
    private void OnEnable()
    {
        EnsureBuilt();
        BatBlastFX.EnsureCreated();   // build the shared blast now, not in the middle of an explosion

        fusing = false;
        trail.colorGradient = normalGradient;
        trail.widthMultiplier = trailWidth;
        trail.Clear();
        trail.emitting = false;
        glintPS.Clear();
        ParticleSystem.EmissionModule em = glintPS.emission;
        em.rateOverDistance = 0f;

        // a pool may move the bat AFTER enabling it, so the trail only starts a couple of frames later
        startDelay = 2;
    }

    private void OnDisable()
    {
        if (!built) return;
        trail.emitting = false;
        trail.Clear();
        glintPS.Clear();
    }

    private void StopTrail()
    {
        if (trail != null) trail.emitting = false;
        if (glintPS != null)
        {
            ParticleSystem.EmissionModule em = glintPS.emission;
            em.rateOverDistance = 0f;
        }
    }

    private void LateUpdate()
    {
        if (!built) return;

        if (startDelay > 0)
        {
            startDelay--;
            if (startDelay == 0)
            {
                trail.Clear();
                trail.emitting = showTrail;
                glintPS.Clear();
            }
            return;
        }

        // while the fuse burns: thicker trail, more glints
        float f = fusing ? Mathf.Clamp01((Time.time - fuseStart) / fuseDur) : 0f;
        trail.widthMultiplier = trailWidth * (1f + 0.6f * f);
        ParticleSystem.EmissionModule em = glintPS.emission;
        em.rateOverDistance = glints ? glintsPerMeter * (1f + 2f * f) : 0f;
    }

    // ------------------------------------------------------------------ build
    private void EnsureBuilt()
    {
        if (built) return;
        built = true;

        if (sharedTrailMat == null) sharedTrailMat = HealVFXAssets.CreateMaterial(HealVFXAssets.Ribbon, brightness);

        normalGradient = MakeGradient(trailColor, trailAccent);
        fuseGradient = MakeGradient(fuseColor, new Color(0.8f, 0.05f, 0.05f, 1f));

        GameObject go = new GameObject("BatTrail");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = trailOffset;
        trail = go.AddComponent<TrailRenderer>();
        trail.sharedMaterial = sharedTrailMat;
        trail.time = trailTime;
        trail.minVertexDistance = 0.04f;
        trail.numCapVertices = 4;
        trail.alignment = LineAlignment.View;
        trail.textureMode = LineTextureMode.Stretch;
        trail.widthMultiplier = trailWidth;
        trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.35f, 0.7f), new Keyframe(1f, 0f));
        trail.colorGradient = normalGradient;
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.receiveShadows = false;
        trail.emitting = false;

        // glints: tiny sparkles left behind in the air (world space, emitted by the distance the bat flies)
        Material glintMat = HealVFXAssets.CreateMaterial(HealVFXAssets.Dot, brightness);
        glintPS = HealVFXAssets.MakeSystem(transform, "BatGlints", glintMat, 60, true);
        ParticleSystem.MainModule m = glintPS.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
        m.startColor = new ParticleSystem.MinMaxGradient(trailColor, trailAccent);
        HealVFXAssets.FadeInOut(glintPS, 0.1f, 0.5f, 1f);
        HealVFXAssets.SizeCurve(glintPS, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.1f)));
    }

    private static Gradient MakeGradient(Color head, Color tail)
    {
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(head, 0f), new GradientColorKey(tail, 1f) },
            new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0.5f, 0.4f), new GradientAlphaKey(0f, 1f) });
        return g;
    }
}
