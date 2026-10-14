using System.Collections.Generic;
using UnityEngine;

/// Dash afterimages: ghost copies of the real animated pose, plus an optional camera zoom kick.
/// Put this on the same GameObject as PlayerController.
public class DashVFX : MonoBehaviour
{
    [Header("Material")]
    [Tooltip("Material using shader Dash/GhostFresnel (colors/intensity/alpha are driven by this component)")]
    public Material ghostMaterial;
    [Tooltip("Material using shader Dash/GlowParticle (for motion lines and dust)")]
    public Material glowMaterial;

    [Header("Ghost Afterimages")]
    [Tooltip("Leave empty to auto-find every SkinnedMeshRenderer under the player. For better FPS, add only the main body mesh(es).")]
    public SkinnedMeshRenderer[] sourceRenderers;
    [Tooltip("Seconds between ghosts. Higher = cheaper")]
    public float ghostInterval = 0.04f;
    public float ghostLifetime = 0.4f;
    [Tooltip("Max ghosts alive at once. Lower = cheaper")]
    public int maxGhosts = 16;
    [Tooltip("Manual size trim. 1 = correct size")]
    public float ghostScaleMultiplier = 1f;
    [Tooltip("How far (world units) each ghost slides backwards over its life. 0 = stay where spawned")]
    public float ghostDrift = 0f;
    public bool ghostFlicker = false;
    public float ghostStartAlpha = 1f;
    public float ghostStartIntensity = 1.8f;
    public float ghostEndIntensity = 0.7f;
    public Color rimStart = new Color(0.6f, 1f, 1f, 1f);
    public Color rimEnd = new Color(0.55f, 0.25f, 1f, 1f);
    public Color coreStart = new Color(0.1f, 0.45f, 1f, 0.45f);
    public Color coreEnd = new Color(0.25f, 0.05f, 0.6f, 0.05f);
    [Tooltip("Every second ghost uses the alternate colors")]
    public bool useTwoTone = true;
    public Color rimAltStart = new Color(1f, 0.45f, 0.9f, 1f);
    public Color rimAltEnd = new Color(0.25f, 0.3f, 1f, 1f);

    [Header("Motion Lines & Dust")]
    public bool useMotionLines = true;
    public Color lineColor = new Color(0.85f, 0.97f, 1f, 0.55f);
    [Tooltip("Lines per second while dashing")]
    public float lineRate = 35f;
    public bool useDust = true;
    public Color dustColor = new Color(0.78f, 0.74f, 0.68f, 0.3f);
    [Tooltip("Size multiplier for the dust")]
    public float dustSize = 1f;

    [Header("Camera Kick (set off if Cinemachine drives your camera)")]
    public bool useCameraKick = true;
    public float fovKick = 10f;          // perspective camera: degrees added at full kick
    public float orthoSizeKick = 0.8f;   // orthographic camera: size added at full kick
    public float kickInSpeed = 14f;
    public float kickOutSpeed = 6f;

    // ---------------------------------------------------------------- internals
    private class Ghost
    {
        public GameObject root;
        public GameObject[] parts;
        public Mesh[] meshes;
        public Material mat;
        public float age;
        public bool active;
        public bool alt;
        public float seed;
        public Vector3 driftDir;
    }

    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int CoreColorId = Shader.PropertyToID("_CoreColor");
    private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int AdditiveId = Shader.PropertyToID("_Additive");

    private readonly List<Ghost> ghosts = new List<Ghost>();
    private Transform runtimeRoot;
    private CharacterController cc;
    private Camera cam;
    private ParticleSystem lines, gust, trailDust;
    private Material lineMat, dustMat;
    private float maxPartSize = 3f;
    private Bounds cullBounds;
    private bool[] sizeChecked;
    private bool[] partHidden;

    private bool isDashing;
    private Vector3 dashDir = Vector3.forward;
    private float spawnTimer;
    private float kickAmount;
    private int ghostCounter;

    // ================================================================ lifecycle
    private void Awake()
    {
        runtimeRoot = new GameObject("DashVFX_Runtime").transform;

        if (sourceRenderers == null || sourceRenderers.Length == 0)
            sourceRenderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        sizeChecked = new bool[sourceRenderers.Length];
        partHidden = new bool[sourceRenderers.Length];

        // Safety net: a baked part much larger than the character is a glitch and gets hidden
        float charHeight = 1.8f;
        cc = GetComponentInParent<CharacterController>();
        if (cc != null) charHeight = Mathf.Max(0.5f, cc.height * Mathf.Abs(cc.transform.lossyScale.y));
        maxPartSize = charHeight * 1.6f;
        cullBounds = new Bounds(Vector3.zero, Vector3.one * maxPartSize * 2f);

        if (ghostMaterial == null)
            Debug.LogWarning("[DashVFX] No ghostMaterial assigned - afterimages disabled.", this);

        if (useMotionLines || useDust) BuildParticles();
    }

    private void OnDisable()
    {
        isDashing = false;
        ApplyKick(0f);
        SetContinuousEmission(false);
    }

    private void OnDestroy()
    {
        foreach (var g in ghosts)
        {
            if (g.mat != null) Destroy(g.mat);
            foreach (var m in g.meshes) if (m != null) Destroy(m);
        }
        if (lineMat != null) Destroy(lineMat);
        if (dustMat != null) Destroy(dustMat);
        if (runtimeRoot != null) Destroy(runtimeRoot.gameObject);
    }

    // ================================================================ public API
    public void Begin(Vector3 direction, float dashSpeed)
    {
        if (direction.sqrMagnitude < 0.0001f) direction = transform.forward;
        direction.y = 0f;
        dashDir = direction.normalized;
        isDashing = true;
        spawnTimer = 0f; // first ghost spawns this frame

        Vector3 pos = transform.position;
        bool grounded = IsOnGround();

        // Dust gust kicked backwards at the start of the dash
        if (gust != null && grounded)
        {
            gust.transform.SetPositionAndRotation(
                pos + Vector3.up * 0.15f,
                Quaternion.LookRotation(-dashDir) * Quaternion.Euler(-12f, 0f, 0f));
            gust.Emit(9);
        }

        // Motion lines flow backwards past the player
        if (lines != null)
        {
            var lm = lines.main;
            lm.startSpeed = new ParticleSystem.MinMaxCurve(-dashSpeed * 0.8f, -dashSpeed * 0.4f);
        }

        SetContinuousEmission(true);
    }

    public void End()
    {
        if (!isDashing) return;
        isDashing = false;
        SetContinuousEmission(false);

        // Small dust puff where the dash ends
        if (trailDust != null && IsOnGround())
        {
            trailDust.transform.position = transform.position + Vector3.up * 0.1f;
            trailDust.Emit(4);
        }
    }

    private bool IsOnGround()
    {
        if (cc != null && cc.isGrounded) return true;
        return Physics.Raycast(transform.position + Vector3.up * 0.2f, Vector3.down, 0.7f, ~0, QueryTriggerInteraction.Ignore);
    }

    private void SetContinuousEmission(bool on)
    {
        if (lines != null) { var e = lines.emission; e.enabled = on; }
        if (trailDust != null) { var e = trailDust.emission; e.enabled = on && IsOnGround(); }
    }

    // ================================================================ update
    private void Update()
    {
        float dt = Time.deltaTime;

        if (isDashing && ghostMaterial != null)
        {
            spawnTimer -= dt;
            while (spawnTimer <= 0f)
            {
                SpawnGhost();
                spawnTimer += ghostInterval;
            }
        }

        if (isDashing)
        {
            if (lines != null)
            {
                lines.transform.SetPositionAndRotation(
                    transform.position + Vector3.up * 1f + dashDir * 1.2f,
                    Quaternion.LookRotation(dashDir));
            }
            if (trailDust != null)
            {
                trailDust.transform.position = transform.position + Vector3.up * 0.1f;
                var te = trailDust.emission;
                te.enabled = IsOnGround();
            }
        }

        for (int i = 0; i < ghosts.Count; i++)
        {
            Ghost g = ghosts[i];
            if (!g.active) continue;

            g.age += dt;
            float t = g.age / ghostLifetime;
            if (t >= 1f)
            {
                g.active = false;
                g.root.SetActive(false);
                continue;
            }

            if (ghostDrift > 0f)
                g.root.transform.position = g.driftDir * (ghostDrift * (1f - (1f - t) * (1f - t)));

            Color rim = (useTwoTone && g.alt) ? Color.Lerp(rimAltStart, rimAltEnd, t) : Color.Lerp(rimStart, rimEnd, t);
            float flicker = ghostFlicker ? 0.88f + 0.12f * Mathf.Sin((g.age + g.seed) * 70f) : 1f;

            g.mat.SetColor(ColorId, rim);
            g.mat.SetColor(CoreColorId, Color.Lerp(coreStart, coreEnd, t));
            g.mat.SetFloat(AlphaId, Mathf.Pow(1f - t, 1.4f) * ghostStartAlpha * flicker);
            g.mat.SetFloat(IntensityId, Mathf.Lerp(ghostStartIntensity, ghostEndIntensity, t));
        }
    }

    private void LateUpdate()
    {
        if (!useCameraKick) return;

        float target = isDashing ? 1f : 0f;
        float k = isDashing ? kickInSpeed : kickOutSpeed;
        float next = Mathf.Lerp(kickAmount, target, 1f - Mathf.Exp(-k * Time.deltaTime));
        if (!isDashing && next < 0.002f) next = 0f;
        ApplyKick(next);
    }

    private void ApplyKick(float newAmount)
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) { kickAmount = newAmount; return; }

        float delta = newAmount - kickAmount;
        if (cam.orthographic) cam.orthographicSize += delta * orthoSizeKick;
        else cam.fieldOfView += delta * fovKick;
        kickAmount = newAmount;
    }

    // ================================================================ ghosts
    private void SpawnGhost()
    {
        Ghost g = GetFreeGhost();
        if (g == null) return;

        g.age = 0f;
        g.active = true;
        g.alt = (ghostCounter++ & 1) == 1;
        g.seed = Random.value * 10f;
        g.driftDir = -dashDir;
        g.root.transform.position = Vector3.zero; // parts are placed in world space while the root sits at the origin
        g.root.SetActive(true);

        for (int i = 0; i < sourceRenderers.Length; i++)
        {
            SkinnedMeshRenderer smr = sourceRenderers[i];
            GameObject part = g.parts[i];

            if (partHidden[i] || smr == null || !smr.enabled || !smr.gameObject.activeInHierarchy)
            {
                part.SetActive(false);
                continue;
            }

            // BakeMesh already returns vertices at world scale, so NO extra scaling is applied.
            smr.BakeMesh(g.meshes[i]);

            // One-time size sanity check per renderer (RecalculateBounds is too costly to run every spawn)
            if (!sizeChecked[i])
            {
                sizeChecked[i] = true;
                g.meshes[i].RecalculateBounds();
                Vector3 size = g.meshes[i].bounds.size * ghostScaleMultiplier;
                if (Mathf.Max(size.x, Mathf.Max(size.y, size.z)) > maxPartSize)
                {
                    partHidden[i] = true;
                    part.SetActive(false);
                    Debug.LogWarning("[DashVFX] Hid ghost part '" + smr.name + "' because it baked far larger than the character (" +
                        size.ToString("F1") + "). Remove it from Source Renderers if it isn't part of the body.", this);
                    continue;
                }
            }

            g.meshes[i].bounds = cullBounds; // cheap fixed bounds so the ghost doesn't get culled wrongly

            part.SetActive(true);
            part.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
            part.transform.localScale = Vector3.one * ghostScaleMultiplier;
        }
    }

    private Ghost GetFreeGhost()
    {
        foreach (var g in ghosts)
            if (!g.active) return g;

        if (ghosts.Count < maxGhosts)
        {
            Ghost created = CreateGhost();
            ghosts.Add(created);
            return created;
        }

        // Pool full: recycle the oldest one
        Ghost oldest = null;
        foreach (var g in ghosts)
            if (oldest == null || g.age > oldest.age) oldest = g;
        return oldest;
    }

    private Ghost CreateGhost()
    {
        var g = new Ghost();
        g.root = new GameObject("DashGhost");
        g.root.transform.SetParent(runtimeRoot, false);
        g.mat = new Material(ghostMaterial);
        g.parts = new GameObject[sourceRenderers.Length];
        g.meshes = new Mesh[sourceRenderers.Length];

        for (int i = 0; i < sourceRenderers.Length; i++)
        {
            var part = new GameObject("Part" + i);
            part.transform.SetParent(g.root.transform, false);

            var mf = part.AddComponent<MeshFilter>();
            var mr = part.AddComponent<MeshRenderer>();
            mr.sharedMaterial = g.mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            var mesh = new Mesh();
            mesh.MarkDynamic();
            mf.sharedMesh = mesh;

            g.parts[i] = part;
            g.meshes[i] = mesh;
        }

        g.root.SetActive(false);
        return g;
    }

    // ================================================================ particles
    private void BuildParticles()
    {
        if (glowMaterial == null)
        {
            // Slot is empty: try to build a material straight from the shader so the effect still works
            Shader fallback = Shader.Find("Dash/GlowParticle");
            if (fallback == null)
            {
                Debug.LogWarning("[DashVFX] Glow Material slot is empty and shader 'Dash/GlowParticle' was not found. " +
                    "Add DashGlow.shader to the project, create a material from it and assign it to Glow Material.", this);
                return;
            }
            glowMaterial = new Material(fallback);
            Debug.LogWarning("[DashVFX] Glow Material slot was empty - created one from the shader automatically. " +
                "Assign a material to the slot to remove this warning (required for builds).", this);
        }

        // --- Motion lines: thin streaks flowing past the player (subtle)
        if (useMotionLines)
        {
            lineMat = new Material(glowMaterial);
            lineMat.SetFloat(IntensityId, 1.2f);
            lineMat.SetFloat(AdditiveId, 1f);

            lines = MakeSystem("MotionLines", lineMat, ParticleSystemRenderMode.Stretch);
            var m = lines.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.35f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
            m.startColor = lineColor;
            var r = lines.GetComponent<ParticleSystemRenderer>();
            r.lengthScale = 1.5f;
            r.velocityScale = 0.1f;
            var sh = lines.shape;
            sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(2.2f, 1.8f, 3f);
            var em = lines.emission;
            em.rateOverTime = lineRate;
            em.enabled = false;
            SizeOverLife(lines, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.1f)));
            FadeOverLife(lines);
        }

        // --- Dust: a backwards gust at the start + light puffs trailing from the feet
        if (useDust)
        {
            dustMat = new Material(glowMaterial);
            dustMat.SetFloat(AdditiveId, 0f); // normal alpha blend so it reads as dust, not light
            dustMat.SetFloat(IntensityId, 1f);

            gust = MakeSystem("DustGust", dustMat, ParticleSystemRenderMode.Billboard);
            var gm = gust.main;
            gm.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            gm.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f);
            gm.startSize = new ParticleSystem.MinMaxCurve(0.35f * dustSize, 0.7f * dustSize);
            gm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            gm.startColor = dustColor;
            var gs = gust.shape;
            gs.enabled = true;
            gs.shapeType = ParticleSystemShapeType.Cone;
            gs.angle = 30f;
            gs.radius = 0.3f;
            var gl = gust.limitVelocityOverLifetime;   // let the gust lose speed quickly, like air drag
            gl.enabled = true;
            gl.limit = 0.4f;
            gl.dampen = 0.2f;
            SizeOverLife(gust, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.8f)));
            FadeOverLife(gust);

            trailDust = MakeSystem("TrailDust", dustMat, ParticleSystemRenderMode.Billboard);
            var tm = trailDust.main;
            tm.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
            tm.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
            tm.startSize = new ParticleSystem.MinMaxCurve(0.3f * dustSize, 0.55f * dustSize);
            tm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            tm.startColor = new Color(dustColor.r, dustColor.g, dustColor.b, dustColor.a * 0.8f);
            var ts = trailDust.shape;
            ts.enabled = true;
            ts.shapeType = ParticleSystemShapeType.Circle;
            ts.radius = 0.25f;
            ts.rotation = new Vector3(-90f, 0f, 0f); // lay the circle flat on the ground
            var te = trailDust.emission;
            te.rateOverTime = 16f;
            te.enabled = false;
            SizeOverLife(trailDust, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 1.5f)));
            FadeOverLife(trailDust);
        }

        // Systems stay "playing" so Emit() works instantly; emission is switched by code.
        if (lines != null) lines.Play();
        if (gust != null) gust.Play();
        if (trailDust != null) trailDust.Play();
    }

    private ParticleSystem MakeSystem(string name, Material mat, ParticleSystemRenderMode mode)
    {
        var go = new GameObject(name);
        go.transform.SetParent(runtimeRoot, false);

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = true;
        main.duration = 5f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 150;

        var emission = ps.emission;
        emission.enabled = false;

        var shape = ps.shape;
        shape.enabled = false;

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.renderMode = mode;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
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
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        c.color = g;
    }
}