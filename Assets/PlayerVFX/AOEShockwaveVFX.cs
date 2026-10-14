using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Visual effect for the player's AOE attack (Y button). Sized to the attack's own radius and cone, but built so it
/// NEVER hides the grass cutting: nothing is a solid shape, there is no visible cone outline, everything is thin,
/// broken, transparent or made of particles.
///
///   CHARGE UP     embers are pulled off the ground and rushed into the player (no sphere, no circle)
///   IMPACT        burst of sparks, a light pulse, a flare over the body, camera shake + zoom punch
///   FIRE WAVE     a thin broken line of fire runs across the area with sparse flame streaks behind it
///   RIPPLE        the grass and the picture are pushed outward by the wave front (distortion)
///   ALONG THE WAVE   flame puffs, embers, wind streaks, dust and grass clippings are thrown off the moving front
///   AFTERGLOW     faint thin glowing cracks on the ground that fade away
///
/// Setup: add this component to the Player (same object as PlayerController). The materials can stay empty.
/// AOEAttackState calls Play(radius, coneAngle, animationDuration) when the attack starts.
/// Tune "Impact Time" so the slam lines up with the moment the attack animation hits the ground.
/// </summary>
[DefaultExecutionOrder(1000)]
public class AOEShockwaveVFX : MonoBehaviour
{
    [Header("Materials (optional - created from the shaders if empty)")]
    [Tooltip("Material using shader VFX/AOEWave")]
    public Material waveMaterial;
    [Tooltip("Material using shader VFX/AOEDistortion (needs Opaque Texture in the URP asset)")]
    public Material distortionMaterial;
    [Tooltip("Material using shader VFX/PlayerParticle (the one from the hit effect)")]
    public Material particleMaterial;
    [Tooltip("Material using shader VFX/PlayerImpact, only used for the optional center flash")]
    public Material impactMaterial;
    [Tooltip("Material using VFX/HitGlow for the body flare. Leave empty to use the one on HitFeedbackManager.")]
    public Material bodyGlowMaterial;

    [Header("Switches")]
    public bool enableChargeUp = true;
    public bool enableGroundWave = true;
    public bool enableDistortion = true;
    public bool enableFlames = true;
    public bool enableSparks = true;
    public bool enableEmbers = true;
    public bool enableWindStreaks = true;
    public bool enableDust = true;
    public bool enableClippings = true;
    public bool enableLight = true;
    public bool enableBodyFlare = true;
    public bool enableCameraShake = true;
    public bool enableZoomPunch = true;
    [Tooltip("A round flash at the player's chest. OFF by default: it reads as a circle around the player.")]
    public bool enableCenterFlash = false;

    [Header("Timing")]
    [Range(0.05f, 0.95f)]
    [Tooltip("When the slam lands, as a fraction of the attack animation (0.4 = 40% in). Tune this until it matches your animation.")]
    public float impactTime = 0.4f;
    [Tooltip("Seconds the shockwave takes to reach the edge of the area.")]
    public float waveDuration = 0.6f;
    [Tooltip("Seconds the faint cracks glow before they are gone.")]
    public float afterglowDuration = 1.6f;

    [Header("Colors")]
    [ColorUsage(true, true)] public Color hotColor = new Color(3.2f, 2.4f, 1.2f, 1f);
    [ColorUsage(true, true)] public Color edgeColor = new Color(2.4f, 0.55f, 0.12f, 1f);
    [ColorUsage(true, true)] public Color flashCoreColor = new Color(3.2f, 2.6f, 1.6f, 1f);
    [ColorUsage(true, true)] public Color flashEdgeColor = new Color(2.6f, 0.6f, 0.1f, 1f);
    [Tooltip("Sparks, embers and charge streaks")]
    public Color emberColorA = new Color(1f, 0.85f, 0.45f, 1f);
    public Color emberColorB = new Color(1f, 0.35f, 0.08f, 1f);
    public Color dustColor = new Color(0.72f, 0.66f, 0.55f, 0.3f);
    [Tooltip("Grass clippings thrown up by the wave")]
    public Color clippingColorA = new Color(0.2f, 0.36f, 0.08f, 1f);
    public Color clippingColorB = new Color(0.46f, 0.56f, 0.16f, 1f);
    [ColorUsage(true, true)] public Color lightColor = new Color(1f, 0.55f, 0.18f, 1f);

    [Header("Ground wave (kept light so the grass cutting stays visible)")]
    [Range(0f, 2f)] public float waveOpacity = 0.7f;
    [Tooltip("Lifts the wave off the ground. Raise it if tall grass hides it.")]
    public float groundOffset = 0.1f;
    public LayerMask groundMask = ~0;
    public float crackScale = 1.6f;
    [Tooltip("Higher = thinner, fainter cracks.")]
    public float crackThinness = 12f;

    [Header("Ripple (pushes the grass outward)")]
    public float distortionStrength = 0.02f;
    [Range(0.03f, 0.4f)] public float distortionWidth = 0.1f;

    [Header("Charge up")]
    [Tooltip("Embers per second pulled toward the player.")]
    public float chargeAmount = 45f;
    [Tooltip("Distance the embers start from the player.")]
    public float chargeRadius = 1.8f;

    [Header("Along the wave (per second while the wave runs)")]
    public float flameRate = 90f;
    public float windStreakRate = 60f;

    [Header("Impact particles (counts over the whole wave)")]
    public int sparkCount = 45;
    public int emberCount = 32;
    public int dustCount = 14;
    public int clippingCount = 40;

    [Header("Impact light")]
    public float lightPulse = 4f;
    public float lightDuration = 0.35f;

    [Header("Body flare")]
    public float bodyFlareStrength = 0.9f;
    public float bodyFlareDuration = 0.4f;
    public float bodyFlareRadius = 1.6f;
    public float chestHeight = 1.1f;

    [Header("Camera (turn off if Cinemachine drives your camera)")]
    public float shakeAmount = 0.22f;
    public float shakeDuration = 0.4f;
    public float orthoPunch = 0.5f;
    public float fovPunch = 3f;
    public float zoomDuration = 0.35f;

    // ------------------------------------------------------------------ runtime
    private enum Phase { Idle, Charging, Wave }

    private Phase phase = Phase.Idle;
    private float radius = 6f;
    private float coneAngle = 120f;
    private bool fullCircle;
    private float halfAngleDeg = 60f;
    private Vector3 forward = Vector3.forward;
    private bool areaSet;

    private float chargeStart, impactAt;
    private float waveStart;
    private float seed;

    private Transform runtimeRoot;
    private Mesh quadMesh;
    private MaterialPropertyBlock block;

    private Transform waveTf, distortTf;
    private MeshRenderer waveMr, distortMr;

    private Transform flashTf;
    private MeshRenderer flashMr;
    private float flashT0 = -10f, flashDur = 0.28f, flashRoll;

    private ParticleSystem charge, sparks, embers, dust, clippings, flames, windStreaks;
    private Material chargeMat, sparkMat, emberMat, dustMat, clippingMat, flameMat, windMat;

    private Light flashLight;
    private float lightT0 = -10f;

    private Camera cam;
    private Vector3 lastShakeOffset;
    private float shakeT0 = -10f;
    private float zoomT0 = -10f, zoomApplied;

    private float emberCarry, dustCarry, clippingCarry, flameCarry, windCarry;

    // ------------------------------------------------------------------ lifecycle
    private void Awake()
    {
        block = new MaterialPropertyBlock();
        quadMesh = BuildQuad();

        GameObject root = new GameObject("AOEShockwaveVFX_Runtime");
        runtimeRoot = root.transform; // scene root on purpose: effects must not become children of the player

        BuildWave();
        BuildFlash();
        BuildParticles();
        BuildLight();
    }

    private void OnDisable()
    {
        Cancel();
        ClearCameraEffects();
    }

    private void OnDestroy()
    {
        if (chargeMat != null) Destroy(chargeMat);
        if (sparkMat != null) Destroy(sparkMat);
        if (emberMat != null) Destroy(emberMat);
        if (dustMat != null) Destroy(dustMat);
        if (clippingMat != null) Destroy(clippingMat);
        if (flameMat != null) Destroy(flameMat);
        if (windMat != null) Destroy(windMat);
        if (runtimeRoot != null) Destroy(runtimeRoot.gameObject);
    }

    // ------------------------------------------------------------------ public API
    /// <summary>Call when the AOE attack starts. The charge-up plays now, the slam lands at impactTime.</summary>
    public void Play(float aoeRadius, float aoeConeAngle, float animationDuration)
    {
        SetArea(aoeRadius, aoeConeAngle);
        forward = FlatForward();
        seed = Random.value;

        chargeStart = Time.time;
        impactAt = chargeStart + Mathf.Max(0.05f, animationDuration * impactTime);
        phase = Phase.Charging;

        if (enableChargeUp && charge != null)
        {
            var em = charge.emission;
            em.enabled = true;
        }
    }

    /// <summary>Cancels a charge-up that has not landed yet (called when the attack is interrupted).</summary>
    public void Cancel()
    {
        if (phase == Phase.Charging) phase = Phase.Idle;
        StopCharge();
    }

    /// <summary>The slam lands: sparks, light, camera, flare and the fire wave. Usually called automatically.</summary>
    public void Impact()
    {
        if (!areaSet) { SetArea(radius, coneAngle); forward = FlatForward(); }
        StopCharge();

        PlaceWave();
        phase = Phase.Wave;
        waveStart = Time.unscaledTime;
        emberCarry = dustCarry = clippingCarry = flameCarry = windCarry = 0f;

        Vector3 center = transform.position;
        float rs = Mathf.Clamp(radius / 5f, 0.6f, 2f);

        if (enableCenterFlash) SpawnFlash(center + Vector3.up * chestHeight);

        if (enableSparks && sparks != null)
        {
            ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < sparkCount; i++)
            {
                Vector3 dir = (ConeDir() + Vector3.up * Random.Range(0.1f, 0.7f)).normalized;
                ep.position = center + Vector3.up * 0.4f + dir * Random.Range(0.2f, 0.8f);
                ep.velocity = dir * Random.Range(5f, 14f) * rs;
                ep.startLifetime = Random.Range(0.4f, 0.9f);
                ep.startSize = Random.Range(0.05f, 0.11f);
                ep.startColor = Color.Lerp(emberColorA, emberColorB, Random.value);
                sparks.Emit(ep, 1);
            }
        }

        if (enableLight && flashLight != null)
        {
            lightT0 = Time.unscaledTime;
            flashLight.range = Mathf.Max(4f, radius * 1.4f);
            flashLight.transform.position = center + Vector3.up * 1.2f;
            flashLight.color = lightColor;
            flashLight.intensity = lightPulse;
            flashLight.enabled = true;
        }

        if (enableBodyFlare) PlayBodyFlare(center + Vector3.up * chestHeight);
        if (enableCameraShake) shakeT0 = Time.unscaledTime;
        if (enableZoomPunch) zoomT0 = Time.unscaledTime;
    }

    // ------------------------------------------------------------------ update
    private void Update()
    {
        // take last frame's camera shake off again BEFORE the camera script moves the camera
        if (lastShakeOffset != Vector3.zero)
        {
            Camera c = GetCamera();
            if (c != null) c.transform.position -= lastShakeOffset;
            lastShakeOffset = Vector3.zero;
        }

        float now = Time.unscaledTime;

        if (phase == Phase.Charging)
        {
            if (charge != null) charge.transform.position = transform.position + Vector3.up * 0.15f;
            if (Time.time >= impactAt) Impact();
        }
        else if (phase == Phase.Wave)
        {
            float age = now - waveStart;

            // ripple only lives a little longer than the wave itself
            if (distortTf != null && distortTf.gameObject.activeSelf)
            {
                if (age < waveDuration + 0.25f) SetDistortProps(Mathf.Clamp(age / Mathf.Max(0.05f, waveDuration), 0.001f, 1f));
                else distortTf.gameObject.SetActive(false);
            }

            if (age >= afterglowDuration)
            {
                phase = Phase.Idle;
                if (waveTf != null) waveTf.gameObject.SetActive(false);
                if (distortTf != null) distortTf.gameObject.SetActive(false);
            }
            else
            {
                float prog = Mathf.Clamp(age / Mathf.Max(0.05f, waveDuration), 0.001f, 1f);
                float glow = Mathf.Pow(1f - Mathf.Clamp01(age / afterglowDuration), 1.5f);
                SetWaveProps(prog, glow);

                if (age < waveDuration) EmitAlongFront(prog, Time.deltaTime);
            }
        }

        UpdateFlash(now);
        UpdateLight(now);
    }

    private void LateUpdate()
    {
        Camera c = GetCamera();
        if (c == null) return;
        float now = Time.unscaledTime;

        // ---- shake: applied last, removed again at the start of the next frame
        float st = (now - shakeT0) / Mathf.Max(shakeDuration, 0.01f);
        if (st >= 0f && st < 1f)
        {
            float decay = (1f - st) * (1f - st);
            float f = now * 34f;
            float nx = Mathf.PerlinNoise(f, 0.37f) * 2f - 1f;
            float ny = Mathf.PerlinNoise(0.91f, f) * 2f - 1f;
            float nz = Mathf.PerlinNoise(f, 7.3f) * 2f - 1f;
            Vector3 offset = (c.transform.right * nx + c.transform.up * (0.7f * ny) + c.transform.forward * (0.2f * nz)) * (shakeAmount * decay);
            c.transform.position += offset;
            lastShakeOffset = offset;
        }

        // ---- zoom punch (applied as a change, so it works together with other camera scripts, DashVFX and PlayerHitFX)
        float zt = (now - zoomT0) / Mathf.Max(zoomDuration, 0.01f);
        float zoomNow = 0f;
        if (zt >= 0f && zt < 1f) zoomNow = Mathf.Sin(Mathf.Sqrt(zt) * Mathf.PI) * (1f - zt);
        float delta = zoomNow - zoomApplied;
        if (Mathf.Abs(delta) > 0.00001f)
        {
            if (c.orthographic) c.orthographicSize -= delta * orthoPunch;
            else c.fieldOfView -= delta * fovPunch;
            zoomApplied = zoomNow;
        }
    }

    private Camera GetCamera()
    {
        if (cam == null || !cam.isActiveAndEnabled) cam = Camera.main;
        return cam;
    }

    private void ClearCameraEffects()
    {
        Camera c = cam != null ? cam : Camera.main;
        if (c != null)
        {
            if (lastShakeOffset != Vector3.zero) c.transform.position -= lastShakeOffset;
            if (zoomApplied != 0f)
            {
                if (c.orthographic) c.orthographicSize += zoomApplied * orthoPunch;
                else c.fieldOfView += zoomApplied * fovPunch;
            }
        }
        lastShakeOffset = Vector3.zero;
        zoomApplied = 0f;
        shakeT0 = -10f;
        zoomT0 = -10f;
    }

    // ------------------------------------------------------------------ area + ground layers
    private void SetArea(float aoeRadius, float aoeConeAngle)
    {
        areaSet = true;
        radius = Mathf.Max(0.5f, aoeRadius);
        coneAngle = aoeConeAngle;
        fullCircle = aoeConeAngle <= 0.1f || aoeConeAngle >= 359f;
        halfAngleDeg = fullCircle ? 180f : aoeConeAngle * 0.5f;
    }

    private Vector3 FlatForward()
    {
        Vector3 f = transform.forward;
        f.y = 0f;
        return f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
    }

    private MeshRenderer MakeQuadObject(string name, Material mat, out Transform tf)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(runtimeRoot, false);
        go.AddComponent<MeshFilter>().sharedMesh = quadMesh;
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        tf = go.transform;
        go.SetActive(false);
        return mr;
    }

    private void BuildWave()
    {
        if (enableGroundWave)
        {
            Material mat = waveMaterial;
            if (mat == null)
            {
                Shader s = Shader.Find("VFX/AOEWave");
                if (s == null) Debug.LogError("[AOEShockwaveVFX] Shader 'VFX/AOEWave' not found. Add VFX_AOEWave.shader to the project.");
                else mat = waveMaterial = new Material(s);
            }
            if (mat != null) waveMr = MakeQuadObject("AOEWave", mat, out waveTf);
        }

        if (enableDistortion)
        {
            Material mat = distortionMaterial;
            if (mat == null)
            {
                Shader s = Shader.Find("VFX/AOEDistortion");
                if (s == null) Debug.LogWarning("[AOEShockwaveVFX] Shader 'VFX/AOEDistortion' not found - the grass ripple is skipped. Add VFX_AOEDistortion.shader to the project.");
                else mat = distortionMaterial = new Material(s);
            }
            if (mat != null) distortMr = MakeQuadObject("AOERipple", mat, out distortTf);
        }
    }

    // puts the wave quads flat on the ground under the player, aligned to the slope, +Y pointing forward
    private void PlaceWave()
    {
        Vector3 pos = transform.position;
        Vector3 normal = Vector3.up;
        float y = pos.y;

        RaycastHit hit;
        if (Physics.Raycast(pos + Vector3.up * 1f, Vector3.down, out hit, 4f, groundMask, QueryTriggerInteraction.Ignore)
            && !hit.collider.transform.IsChildOf(transform)
            && hit.collider.GetComponentInParent<IDamageable>() == null)
        {
            y = hit.point.y;
            normal = hit.normal;
        }

        Vector3 point = new Vector3(pos.x, y, pos.z) + normal * groundOffset;
        Vector3 up = Vector3.ProjectOnPlane(forward, normal);
        if (up.sqrMagnitude < 0.0001f) up = Vector3.forward;
        Quaternion rot = Quaternion.LookRotation(-normal, up.normalized);
        Vector3 size = new Vector3(radius * 2f, radius * 2f, 1f);

        if (waveTf != null)
        {
            waveTf.SetPositionAndRotation(point, rot);
            waveTf.localScale = size;
            waveTf.gameObject.SetActive(true);
            SetWaveProps(0f, 0f);
        }
        if (distortTf != null)
        {
            distortTf.SetPositionAndRotation(point, rot);
            distortTf.localScale = size;
            distortTf.gameObject.SetActive(true);
            SetDistortProps(0f);
        }
    }

    private void SetWaveProps(float progress, float glow)
    {
        if (waveMr == null) return;
        waveMr.GetPropertyBlock(block);
        block.SetFloat("_Progress", progress);
        block.SetFloat("_GlowFade", glow);
        block.SetFloat("_HalfAngle", fullCircle ? 4f : halfAngleDeg * Mathf.Deg2Rad);
        block.SetFloat("_Seed", seed);
        block.SetFloat("_Intensity", waveOpacity);
        block.SetFloat("_Radius", radius);
        block.SetFloat("_CrackScale", crackScale);
        block.SetFloat("_CrackSharpness", crackThinness);
        block.SetColor("_ColorHot", hotColor);
        block.SetColor("_ColorEdge", edgeColor);
        waveMr.SetPropertyBlock(block);
    }

    private void SetDistortProps(float progress)
    {
        if (distortMr == null) return;
        distortMr.GetPropertyBlock(block);
        block.SetFloat("_Progress", progress);
        block.SetFloat("_HalfAngle", fullCircle ? 4f : halfAngleDeg * Mathf.Deg2Rad);
        block.SetFloat("_Seed", seed);
        block.SetFloat("_Strength", distortionStrength);
        block.SetFloat("_Width", distortionWidth);
        distortMr.SetPropertyBlock(block);
    }

    // ------------------------------------------------------------------ optional center flash
    private void BuildFlash()
    {
        if (!enableCenterFlash) return;
        Material mat = impactMaterial;
        if (mat == null)
        {
            Shader s = Shader.Find("VFX/PlayerImpact");
            if (s == null) return;
            mat = impactMaterial = new Material(s);
        }
        flashMr = MakeQuadObject("AOEFlash", mat, out flashTf);
    }

    private void SpawnFlash(Vector3 point)
    {
        if (flashTf == null) return;
        Camera c = GetCamera();
        if (c == null) return;

        flashT0 = Time.unscaledTime;
        flashRoll = Random.Range(0f, 360f);
        flashTf.position = point + (c.transform.position - point).normalized * 0.8f;
        flashTf.localScale = Vector3.one * Mathf.Clamp(radius * 0.8f, 2.5f, 6f);
        flashTf.gameObject.SetActive(true);
        UpdateFlash(flashT0);
    }

    private void UpdateFlash(float now)
    {
        if (flashTf == null || !flashTf.gameObject.activeSelf) return;

        float progress = (now - flashT0) / Mathf.Max(flashDur, 0.01f);
        if (progress >= 1f)
        {
            flashTf.gameObject.SetActive(false);
            return;
        }

        Camera c = GetCamera();
        if (c != null)
            flashTf.rotation = Quaternion.AngleAxis(flashRoll, c.transform.forward) * Quaternion.LookRotation(c.transform.forward, c.transform.up);

        flashMr.GetPropertyBlock(block);
        block.SetFloat("_Progress", progress);
        block.SetFloat("_Seed", seed);
        block.SetFloat("_Intensity", 1.2f);
        block.SetColor("_ColorCore", flashCoreColor);
        block.SetColor("_ColorEdge", flashEdgeColor);
        flashMr.SetPropertyBlock(block);
    }

    // ------------------------------------------------------------------ light
    private void BuildLight()
    {
        if (!enableLight) return;
        GameObject go = new GameObject("AOELight");
        go.transform.SetParent(runtimeRoot, false);
        flashLight = go.AddComponent<Light>();
        flashLight.type = LightType.Point;
        flashLight.shadows = LightShadows.None;
        flashLight.color = lightColor;
        flashLight.intensity = 0f;
        flashLight.enabled = false;
    }

    private void UpdateLight(float now)
    {
        if (flashLight == null || !flashLight.enabled) return;
        float t = (now - lightT0) / Mathf.Max(lightDuration, 0.01f);
        if (t >= 1f)
        {
            flashLight.enabled = false;
            return;
        }
        flashLight.intensity = lightPulse * (1f - t) * (1f - t);
    }

    // ------------------------------------------------------------------ body flare
    private void PlayBodyFlare(Vector3 point)
    {
        Material mat = bodyGlowMaterial;
        if (mat == null && HitFeedbackManager.Instance != null) mat = HitFeedbackManager.Instance.glowMaterial;
        if (mat == null)
        {
            Shader s = Shader.Find("VFX/HitGlow");
            if (s != null) { mat = new Material(s); bodyGlowMaterial = mat; }
        }
        if (mat == null) return;

        EnemyHitFX fx = GetComponent<EnemyHitFX>();
        if (fx == null) fx = gameObject.AddComponent<EnemyHitFX>();
        fx.PlayGlow(mat, point, bodyFlareStrength, bodyFlareDuration, bodyFlareRadius, hotColor, edgeColor);
    }

    // ------------------------------------------------------------------ particles
    private void BuildParticles()
    {
        Shader shader = particleMaterial != null ? particleMaterial.shader : Shader.Find("VFX/PlayerParticle");
        if (shader == null)
        {
            Debug.LogError("[AOEShockwaveVFX] Shader 'VFX/PlayerParticle' not found. Add VFX_PlayerParticle.shader (from the player hit effect) to the project.");
            return;
        }

        chargeMat = MakeParticleMaterial(shader, 1f, 1f, 2.2f);    // glowing, soft
        sparkMat = MakeParticleMaterial(shader, 1f, 1f, 2.5f);
        emberMat = MakeParticleMaterial(shader, 1f, 1f, 2f);
        flameMat = MakeParticleMaterial(shader, 1f, 1f, 1.6f);
        windMat = MakeParticleMaterial(shader, 1f, 1f, 1.2f);
        dustMat = MakeParticleMaterial(shader, 0f, 1f, 1f);        // normal alpha, soft
        clippingMat = MakeParticleMaterial(shader, 0f, 0.15f, 1f); // normal alpha, hard edge

        // --- charge up: embers lifted off the GROUND around the player and pulled into it (flat disc, not a sphere)
        charge = MakeSystem("Charge", chargeMat, ParticleSystemRenderMode.Stretch, 150);
        var cm = charge.main;
        cm.startLifetime = 0.55f;
        cm.startSpeed = new ParticleSystem.MinMaxCurve(-chargeRadius * 2.2f, -chargeRadius * 1.6f); // negative = toward the player
        cm.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
        cm.startColor = new ParticleSystem.MinMaxGradient(emberColorA, emberColorB);
        var cr = charge.GetComponent<ParticleSystemRenderer>();
        cr.lengthScale = 1.5f;
        cr.velocityScale = 0.1f;
        var cs = charge.shape;
        cs.enabled = true;
        cs.shapeType = ParticleSystemShapeType.Circle;
        cs.radius = chargeRadius;
        cs.radiusThickness = 0.2f;                 // mostly from the outer edge
        cs.rotation = new Vector3(-90f, 0f, 0f);   // lay the disc flat on the ground
        var cv = charge.velocityOverLifetime;      // and rise while being pulled in
        cv.enabled = true;
        cv.space = ParticleSystemSimulationSpace.World;
        cv.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        cv.y = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
        cv.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        var ce = charge.emission;
        ce.rateOverTime = chargeAmount;
        ce.enabled = false;
        SizeOverLife(charge, new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(1f, 1.1f)));
        FadeOverLife(charge);

        // --- sparks (burst at impact)
        sparks = MakeSystem("Sparks", sparkMat, ParticleSystemRenderMode.Stretch, 200);
        var sm = sparks.main;
        sm.gravityModifier = 0.8f;
        var sr = sparks.GetComponent<ParticleSystemRenderer>();
        sr.lengthScale = 1.3f;
        sr.velocityScale = 0.07f;
        SizeOverLife(sparks, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.1f)));
        FadeOverLife(sparks);

        // --- flame puffs thrown off the wave front: orange-white, turning red, soft and see-through
        flames = MakeSystem("Flames", flameMat, ParticleSystemRenderMode.Billboard, 300);
        var fm = flames.main;
        fm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        fm.startColor = new Color(1f, 1f, 1f, 0.6f);
        var fc = flames.colorOverLifetime;
        fc.enabled = true;
        Gradient fire = new Gradient();
        fire.SetKeys(
            new[] { new GradientColorKey(new Color(1f, 0.95f, 0.6f), 0f), new GradientColorKey(new Color(1f, 0.45f, 0.1f), 0.45f), new GradientColorKey(new Color(0.55f, 0.08f, 0.02f), 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
        fc.color = fire;
        SizeOverLife(flames, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.4f, 1f), new Keyframe(1f, 1.3f)));

        // --- embers: small glowing specks that float up
        embers = MakeSystem("Embers", emberMat, ParticleSystemRenderMode.Billboard, 200);
        var em = embers.main;
        em.gravityModifier = -0.12f;
        var en = embers.noise;
        en.enabled = true;
        en.strength = 0.5f;
        en.frequency = 1.2f;
        SizeOverLife(embers, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.1f)));
        FadeOverLife(embers);

        // --- wind streaks: thin light lines racing low over the grass in the direction of the wave
        windStreaks = MakeSystem("WindStreaks", windMat, ParticleSystemRenderMode.Stretch, 200);
        var wr = windStreaks.GetComponent<ParticleSystemRenderer>();
        wr.lengthScale = 4f;
        wr.velocityScale = 0.12f;
        FadeOverLife(windStreaks);

        // --- dust: soft smoke puffs along the wave
        dust = MakeSystem("Dust", dustMat, ParticleSystemRenderMode.Billboard, 100);
        SizeOverLife(dust, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 1.6f)));
        FadeOverLife(dust);

        // --- grass clippings: thin green blades thrown up
        clippings = MakeSystem("Clippings", clippingMat, ParticleSystemRenderMode.Stretch, 200);
        var km = clippings.main;
        km.gravityModifier = 2.5f;
        var kr = clippings.GetComponent<ParticleSystemRenderer>();
        kr.lengthScale = 3f;
        kr.velocityScale = 0.02f;
        SizeOverLife(clippings, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.7f, 1f), new Keyframe(1f, 0f)));

        charge.Play();
        sparks.Play();
        flames.Play();
        embers.Play();
        windStreaks.Play();
        dust.Play();
        clippings.Play();
    }

    private static Material MakeParticleMaterial(Shader shader, float additive, float softness, float intensity)
    {
        Material m = new Material(shader);
        m.SetFloat("_Additive", additive);
        m.SetFloat("_Softness", softness);
        m.SetFloat("_Intensity", intensity);
        return m;
    }

    private ParticleSystem MakeSystem(string name, Material mat, ParticleSystemRenderMode mode, int maxParticles)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(runtimeRoot, false);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = true;
        main.duration = 5f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = 0f;
        main.maxParticles = maxParticles;

        var emission = ps.emission;
        emission.enabled = false;
        var shape = ps.shape;
        shape.enabled = false;

        ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.renderMode = mode;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        return ps;
    }

    private static void SizeOverLife(ParticleSystem ps, AnimationCurve curve)
    {
        var s = ps.sizeOverLifetime;
        s.enabled = true;
        s.size = new ParticleSystem.MinMaxCurve(1f, curve);
    }

    private static void FadeOverLife(ParticleSystem ps)
    {
        var c = ps.colorOverLifetime;
        c.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        c.color = g;
    }

    private void StopCharge()
    {
        if (charge == null) return;
        var em = charge.emission;
        em.enabled = false;
    }

    // random direction inside the attack's cone (or any direction for a full circle)
    private Vector3 ConeDir()
    {
        float ang = Random.Range(-halfAngleDeg, halfAngleDeg);
        return Quaternion.AngleAxis(ang, Vector3.up) * forward;
    }

    // takes the whole number of particles to emit this frame out of a per-second rate (keeps the remainder)
    private static int TakeCount(ref float carry, float perSecond, float dt)
    {
        carry += perSecond * dt;
        int n = Mathf.FloorToInt(carry);
        carry -= n;
        return n;
    }

    // the wave throws things off right along its moving front
    private void EmitAlongFront(float prog, float dt)
    {
        float frontR = (1f - Mathf.Pow(1f - prog, 3f)) * radius;
        float rs = Mathf.Clamp(radius / 5f, 0.6f, 2f);
        float perSecond = 1f / Mathf.Max(0.05f, waveDuration);
        Vector3 origin = transform.position;

        if (enableFlames && flames != null)
        {
            int n = TakeCount(ref flameCarry, flameRate, dt);
            ParticleSystem.EmitParams fp = new ParticleSystem.EmitParams();
            for (int i = 0; i < n; i++)
            {
                Vector3 dir = ConeDir();
                fp.position = origin + dir * Mathf.Max(0.3f, frontR - Random.Range(0f, 0.6f)) + Vector3.up * 0.12f;
                fp.velocity = dir * Random.Range(0.5f, 1.5f) + Vector3.up * Random.Range(0.8f, 2f);
                fp.startLifetime = Random.Range(0.35f, 0.6f);
                fp.startSize = Random.Range(0.5f, 1.1f);
                flames.Emit(fp, 1);
            }
        }

        if (enableEmbers && embers != null)
        {
            int n = TakeCount(ref emberCarry, emberCount * perSecond, dt);
            ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < n; i++)
            {
                Vector3 dir = ConeDir();
                ep.position = origin + dir * Mathf.Max(0.3f, frontR - Random.Range(0f, 0.8f)) + Vector3.up * 0.15f;
                ep.velocity = dir * Random.Range(0.5f, 2f) + Vector3.up * Random.Range(0.8f, 2.5f);
                ep.startLifetime = Random.Range(1.2f, 2.4f);
                ep.startSize = Random.Range(0.06f, 0.14f);
                ep.startColor = Color.Lerp(emberColorA, emberColorB, Random.value);
                embers.Emit(ep, 1);
            }
        }

        if (enableWindStreaks && windStreaks != null)
        {
            int n = TakeCount(ref windCarry, windStreakRate, dt);
            ParticleSystem.EmitParams wp = new ParticleSystem.EmitParams();
            for (int i = 0; i < n; i++)
            {
                Vector3 dir = ConeDir();
                wp.position = origin + dir * Mathf.Max(0.3f, frontR - Random.Range(0f, 1.5f)) + Vector3.up * Random.Range(0.15f, 0.9f);
                wp.velocity = dir * Random.Range(7f, 13f) * rs + Vector3.up * Random.Range(0f, 0.5f);
                wp.startLifetime = Random.Range(0.25f, 0.4f);
                wp.startSize = Random.Range(0.03f, 0.06f);
                wp.startColor = new Color(1f, 0.9f, 0.7f, 0.45f);
                windStreaks.Emit(wp, 1);
            }
        }

        if (enableDust && dust != null)
        {
            int n = TakeCount(ref dustCarry, dustCount * perSecond, dt);
            ParticleSystem.EmitParams dp = new ParticleSystem.EmitParams();
            for (int i = 0; i < n; i++)
            {
                Vector3 dir = ConeDir();
                dp.position = origin + dir * Mathf.Max(0.3f, frontR - Random.Range(0f, 0.6f)) + Vector3.up * 0.2f;
                dp.velocity = dir * Random.Range(1.5f, 3.5f) * rs + Vector3.up * Random.Range(0.3f, 1f);
                dp.startLifetime = Random.Range(0.7f, 1.2f);
                dp.startSize = Random.Range(0.9f, 1.6f);
                dp.startColor = dustColor;
                dust.Emit(dp, 1);
            }
        }

        if (enableClippings && clippings != null)
        {
            int n = TakeCount(ref clippingCarry, clippingCount * perSecond, dt);
            ParticleSystem.EmitParams kp = new ParticleSystem.EmitParams();
            for (int i = 0; i < n; i++)
            {
                Vector3 dir = ConeDir();
                kp.position = origin + dir * Mathf.Max(0.3f, frontR - Random.Range(0f, 0.5f)) + Vector3.up * 0.1f;
                kp.velocity = dir * Random.Range(2f, 5f) * rs + Vector3.up * Random.Range(3f, 7f);
                kp.startLifetime = Random.Range(0.9f, 1.5f);
                kp.startSize = Random.Range(0.05f, 0.1f);
                kp.startColor = Color.Lerp(clippingColorA, clippingColorB, Random.value);
                clippings.Emit(kp, 1);
            }
        }
    }

    // ------------------------------------------------------------------ helpers
    private static Mesh BuildQuad()
    {
        Mesh m = new Mesh();
        m.name = "AOEShockwaveQuad";
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
}