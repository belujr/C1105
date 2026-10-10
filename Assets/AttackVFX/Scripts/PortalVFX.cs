using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Fire portal. Put this on an empty object at the centre of the stone ring and rotate it so the
/// BLUE arrow (local +Z) points out of the ring's opening, toward where the player stands.
/// Built in Play mode: a glassy window (your texture, or the lens-bent scene) framed by living fire,
/// embers, sparks, rays streaming in from the FRONT, heat distortion, a glow light and a periodic surge.
/// Change any value in the Inspector while playing and it rebuilds.
/// Call Open() / Close() / Toggle() / TriggerSurge() from code.
/// </summary>
[DisallowMultipleComponent]
public class PortalVFX : MonoBehaviour
{
    [Header("Shaders (assign all three so they are included in builds)")]
    public Shader windowShader;
    public Shader particleShader;
    public Shader distortShader;

    [Header("Size & Opening")]
    [Tooltip("Radius of the window in world units. Make it slightly bigger than the ring's opening so the stone hides the seam (see the gizmo).")]
    public float radius = 1.5f;
    public bool startOpen = true;
    public float openTime = 1.4f;

    [Header("The view (middle of the ring)")]
    [Tooltip("A picture of the other world. Leave empty to show the real scene behind the portal, bent like a crystal ball (needs Opaque Texture on the URP Asset).")]
    public Texture centerTexture;
    [ColorUsage(true, true)] public Color centerTint = Color.white;
    public Vector2 textureTiling = Vector2.one;
    [Tooltip("Scrolls the picture slowly (units per second).")]
    public Vector2 textureScroll = Vector2.zero;
    [Tooltip("How much the picture slides as the camera moves. Flip the sign if it moves the wrong way.")]
    [Range(-0.5f, 0.5f)] public float parallax = 0.08f;
    [Range(0f, 0.6f)] public float lens = 0.3f;
    [Range(0f, 0.05f)] public float chroma = 0.015f;
    [Range(0f, 0.05f)] public float warp = 0.012f;
    [Range(0f, 1f)] public float vignette = 0.5f;
    [Range(0f, 1f)] public float centerOpacity = 1f;
    [Tooltip("Lower = more room around the window for the glow to spill onto the stone.")]
    [Range(0.5f, 1f)] public float discFill = 0.8f;
    [Range(0.005f, 0.3f)] public float edgeSoftness = 0.03f;

    [Header("Fire rim")]
    [ColorUsage(true, true)] public Color fireHot = new Color(2.5f, 1.6f, 0.5f, 1f);
    [ColorUsage(true, true)] public Color fireMid = new Color(2.2f, 0.55f, 0.08f, 1f);
    [ColorUsage(true, true)] public Color fireDark = new Color(0.5f, 0.06f, 0.02f, 1f);
    public float fireIntensity = 1.8f;
    [Tooltip("How far the fire reaches in from the edge.")]
    [Range(0.02f, 0.6f)] public float fireWidth = 0.24f;
    [Tooltip("0 = even band, 1 = flame tongues of very different lengths.")]
    [Range(0f, 1f)] public float flameTongues = 0.6f;
    public float tongueCount = 4f;
    [Tooltip("A brighter pulse that travels around the ring.")]
    [Range(0f, 1f)] public float runningPulse = 0.5f;
    public float crackScale = 4f;
    public float crackSharpness = 2.5f;
    [Range(0f, 1f)] public float fireFlicker = 0.3f;
    public float fireSpeed = 0.6f;
    [Range(0f, 2f)] public float innerGlow = 0.5f;
    [Range(0f, 2f)] public float outerGlow = 0.7f;
    [Tooltip("Optional crack / lava pattern (grayscale, tileable, Wrap Mode = Repeat). Replaces the built-in cracks.")]
    public Texture rimTexture;
    [Tooltip("How many times the rim texture repeats around the ring (whole number).")]
    public int rimTextureTiling = 6;

    [Header("Rays (stream in from the front)")]
    public bool enableRays = true;
    [ColorUsage(true, true)] public Color rayColor = new Color(3f, 1.4f, 0.4f, 1f);
    public float rayRate = 10f;
    public Vector2 rayLifetime = new Vector2(0.6f, 1f);
    public Vector2 rayWidth = new Vector2(0.008f, 0.02f);
    [Tooltip("How far in front of the portal the rays start, as a multiple of the radius.")]
    public float rayFrontDistance = 2.2f;
    [Tooltip("Width of the area the rays start from, as a multiple of the radius. Small = a narrow beam, large = a wide cone.")]
    public float rayConeRadius = 0.9f;
    public float rayLengthScale = 14f;
    public float rayVelocityScale = 0.3f;

    [Header("Embers & sparks")]
    public bool enableEmbers = true;
    [ColorUsage(true, true)] public Color emberColor = new Color(3f, 1.3f, 0.35f, 1f);
    [Tooltip("Floating specks inside the ring.")]
    public float emberRate = 14f;
    public Vector2 emberSize = new Vector2(0.03f, 0.07f);
    public Vector2 emberLifetime = new Vector2(2.5f, 4f);
    public float emberRise = 0.15f;
    [Tooltip("Quick sparks that fly off the rim.")]
    public float sparkRate = 18f;
    public float sparkRise = 0.7f;

    [Header("Heat distortion (needs 'Opaque Texture' on the URP Asset)")]
    public bool enableDistortion = true;
    [Tooltip("Halo of heat ripples around the portal, as a multiple of the radius.")]
    public float haloRadius = 3f;
    public float haloStrength = 0.008f;
    [Tooltip("On: bends everything on screen, with no cut-off line where the ground crosses it.")]
    public bool haloIgnoresDepth = true;
    [Tooltip("Warped trails that ride along with the rays.")]
    public bool enableRayHaze = true;
    public float hazeStrength = 0.01f;

    [Header("Surge (periodic flare)")]
    public bool enableSurge = true;
    public Vector2 surgeInterval = new Vector2(5f, 9f);
    public float surgeDuration = 1.4f;
    [Tooltip("Extra ray rate at the peak of a surge (3 = four times as many).")]
    public float surgeRayBurst = 3f;
    public float shockStrength = 0.02f;

    [Header("Player reaction")]
    [Tooltip("Leave empty to find the object tagged 'Player' automatically.")]
    public Transform reactTo;
    public float reactNear = 4f;
    public float reactFar = 12f;
    [Tooltip("How much stronger the portal gets as the player arrives (0 = no reaction).")]
    public float proximityBoost = 0.6f;

    [Header("Glow light (no shadows, cheap)")]
    public bool enableLight = true;
    public Color lightColor = new Color(1f, 0.45f, 0.15f, 1f);
    public float lightIntensity = 3.5f;
    public float lightRange = 7f;
    [Range(0f, 1f)] public float lightFlicker = 0.2f;

    // shader property ids
    static readonly int PCenterTex = Shader.PropertyToID("_CenterTex");
    static readonly int PRimTex = Shader.PropertyToID("_RimTex");
    static readonly int PUseTexture = Shader.PropertyToID("_UseTexture");
    static readonly int PUseRimTex = Shader.PropertyToID("_UseRimTex");
    static readonly int PRimTexTiling = Shader.PropertyToID("_RimTexTiling");
    static readonly int PCenterTint = Shader.PropertyToID("_CenterTint");
    static readonly int PTexParams = Shader.PropertyToID("_TexParams");
    static readonly int PParallax = Shader.PropertyToID("_Parallax");
    static readonly int PLens = Shader.PropertyToID("_LensStrength");
    static readonly int PChroma = Shader.PropertyToID("_Chroma");
    static readonly int PWarp = Shader.PropertyToID("_Warp");
    static readonly int PVignette = Shader.PropertyToID("_Vignette");
    static readonly int PCenterOpacity = Shader.PropertyToID("_CenterOpacity");
    static readonly int PFireHot = Shader.PropertyToID("_FireHot");
    static readonly int PFireMid = Shader.PropertyToID("_FireMid");
    static readonly int PFireDark = Shader.PropertyToID("_FireDark");
    static readonly int PIntensity = Shader.PropertyToID("_Intensity");
    static readonly int PFireWidth = Shader.PropertyToID("_FireWidth");
    static readonly int PTongues = Shader.PropertyToID("_Tongues");
    static readonly int PTongueFreq = Shader.PropertyToID("_TongueFreq");
    static readonly int PRunPulse = Shader.PropertyToID("_RunPulse");
    static readonly int PCrackScale = Shader.PropertyToID("_CrackScale");
    static readonly int PCrackSharp = Shader.PropertyToID("_CrackSharp");
    static readonly int PFireFlicker = Shader.PropertyToID("_FireFlicker");
    static readonly int PFireSpeed = Shader.PropertyToID("_FireSpeed");
    static readonly int PInnerGlow = Shader.PropertyToID("_InnerGlow");
    static readonly int POuterGlow = Shader.PropertyToID("_OuterGlow");
    static readonly int PDiscRadius = Shader.PropertyToID("_DiscRadius");
    static readonly int PSoftness = Shader.PropertyToID("_Softness");
    static readonly int PSurge = Shader.PropertyToID("_Surge");
    static readonly int PFade = Shader.PropertyToID("_Fade");
    static readonly int PTint = Shader.PropertyToID("_Tint");
    // distortion shader
    static readonly int PMode = Shader.PropertyToID("_Mode");
    static readonly int PStrength = Shader.PropertyToID("_Strength");
    static readonly int PFalloff = Shader.PropertyToID("_Falloff");
    static readonly int PShock = Shader.PropertyToID("_Shock");
    static readonly int PShockStrength = Shader.PropertyToID("_ShockStrength");
    static readonly int PCenter = Shader.PropertyToID("_CenterWS");
    static readonly int PZTest = Shader.PropertyToID("_ZTest");

    private readonly List<GameObject> created = new List<GameObject>();
    private readonly List<Material> mats = new List<Material>();

    private Transform discT;
    private MeshRenderer discR;
    private Material discMat;
    private Transform haloT;
    private MeshRenderer haloR;
    private Material haloMat;
    private Material hazeMat;
    private ParticleSystem rays, haze, embers, sparks;
    private Light glow;
    private Camera cam;

    private bool built;
    private bool sceneAvailable;
    private bool distortionOn;
    private bool rebuildQueued;

    private float open;
    private bool isOpen;
    private float lastRateMul = -1f;
    private float prox;
    private float findTimer;
    private bool surgeActive;
    private float surgeT;
    private float surge;
    private float nextSurgeTime;

    private static Mesh quad;

    public bool IsOpen => isOpen;
    public void Open() { isOpen = true; }
    public void Close() { isOpen = false; }
    public void Toggle() { isOpen = !isOpen; }

    /// <summary>Fire a surge now (flare, shockwave, ray burst).</summary>
    public void TriggerSurge()
    {
        if (built && !surgeActive && open > 0.99f) StartSurge();
    }

    private void Awake()
    {
        isOpen = startOpen;
        open = 0f;
        Build();
    }

    private void OnValidate()
    {
        if (Application.isPlaying) rebuildQueued = true;
    }

    private void OnDestroy()
    {
        Clear();
    }

    private void Update()
    {
        if (rebuildQueued)
        {
            rebuildQueued = false;
            Build();
        }
        if (!built) return;
        if (cam == null) cam = Camera.main;

        open = Mathf.MoveTowards(open, isOpen ? 1f : 0f, Time.deltaTime / Mathf.Max(0.01f, openTime));
        float e = Mathf.SmoothStep(0f, 1f, open);

        UpdateProximity();
        UpdateSurge(e);

        float boost = 1f + proximityBoost * prox;
        float s = surge;
        Vector3 center = transform.position;

        // window
        discT.localScale = Vector3.one * (radius * 2f / Mathf.Max(0.5f, discFill)) * Mathf.Lerp(0.1f, 1f, e) * (1f + 0.05f * s);
        discR.enabled = e > 0.001f;
        discMat.SetFloat(PFade, e);
        discMat.SetFloat(PIntensity, fireIntensity * boost * (1f + 1.2f * s));
        discMat.SetFloat(PSurge, s);

        // heat halo
        if (haloT != null)
        {
            haloT.localScale = Vector3.one * radius * 2f * haloRadius * Mathf.Lerp(0.2f, 1f, e);
            haloR.enabled = e > 0.001f;
            haloMat.SetFloat(PFade, e);
            haloMat.SetFloat(PStrength, haloStrength * boost);
            haloMat.SetFloat(PShock, surgeActive ? Mathf.Clamp01(surgeT / Mathf.Max(0.1f, surgeDuration)) : 2f);
            haloMat.SetVector(PCenter, center);
        }
        if (hazeMat != null)
        {
            hazeMat.SetFloat(PFade, e);
            hazeMat.SetFloat(PStrength, hazeStrength * boost);
            hazeMat.SetVector(PCenter, center);
        }

        // light
        if (glow != null)
        {
            float flick = 1f + (Mathf.PerlinNoise(Time.time * 3f, 0.37f) - 0.5f) * 2f * lightFlicker;
            glow.intensity = lightIntensity * e * boost * flick * (1f + 0.8f * s);
            glow.enabled = e > 0.001f;
        }

        // particle rates
        float rateMul = e * boost * (1f + surgeRayBurst * s);
        if (Mathf.Abs(rateMul - lastRateMul) > 0.005f)
        {
            lastRateMul = rateMul;
            SetRate(rays, rayRate * rateMul);
            SetRate(haze, rayRate * 0.5f * rateMul);
            SetRate(embers, emberRate * e * boost);
            SetRate(sparks, sparkRate * e * boost * (1f + s));
        }
    }

    private void LateUpdate()
    {
        // the halo always faces the camera so it reads as a round lens from any angle
        if (haloT != null && cam != null) haloT.rotation = cam.transform.rotation;
    }

    private static void SetRate(ParticleSystem ps, float rate)
    {
        if (ps == null) return;
        var em = ps.emission;
        em.rateOverTime = rate;
    }

    // ------------------------------------------------------------------ behaviour
    private void UpdateProximity()
    {
        if (reactTo == null)
        {
            findTimer -= Time.deltaTime;
            if (findTimer <= 0f)
            {
                findTimer = 1f;
                GameObject go = GameObject.FindWithTag("Player");
                if (go != null) reactTo = go.transform;
            }
        }

        float target = 0f;
        if (reactTo != null)
        {
            float d = Vector3.Distance(reactTo.position, transform.position);
            target = 1f - Mathf.Clamp01((d - reactNear) / Mathf.Max(0.01f, reactFar - reactNear));
        }
        prox = Mathf.MoveTowards(prox, target, Time.deltaTime * 1.5f);
    }

    private void UpdateSurge(float e)
    {
        if (!enableSurge || e < 0.99f)
        {
            surge = 0f;
            surgeActive = false;
            return;
        }

        if (surgeActive)
        {
            surgeT += Time.deltaTime;
            float x = surgeT / Mathf.Max(0.1f, surgeDuration);
            if (x >= 1f)
            {
                surgeActive = false;
                surge = 0f;
                ScheduleNextSurge();
            }
            else
            {
                surge = Mathf.Sin(x * Mathf.PI);
                surge *= surge;
            }
        }
        else if (Time.time >= nextSurgeTime)
        {
            StartSurge();
        }
    }

    private void StartSurge()
    {
        surgeActive = true;
        surgeT = 0f;
    }

    private void ScheduleNextSurge()
    {
        nextSurgeTime = Time.time + Random.Range(Mathf.Min(surgeInterval.x, surgeInterval.y),
                                                 Mathf.Max(surgeInterval.x, surgeInterval.y));
    }

    // ------------------------------------------------------------------ building
    private void Build()
    {
        Clear();
        built = false;

        if (windowShader == null) windowShader = Shader.Find("VFX/PortalWindow");
        if (particleShader == null) particleShader = Shader.Find("VFX/PortalParticle");
        if (distortShader == null) distortShader = Shader.Find("VFX/PortalDistort");
        if (windowShader == null || particleShader == null)
        {
            Debug.LogError("[PortalVFX] Assign the PortalWindow and PortalParticle shaders in the Inspector.", this);
            return;
        }

        // the screen copy that the lens view and heat distortion rely on
        UniversalRenderPipelineAsset urp = UniversalRenderPipeline.asset;
        sceneAvailable = urp == null || urp.supportsCameraOpaqueTexture;
        if (!sceneAvailable)
            Debug.LogWarning("[PortalVFX] Tick 'Opaque Texture' on your URP Asset (Rendering section) for the lens view and heat distortion. Until then those are off.", this);

        distortionOn = enableDistortion && distortShader != null && sceneAvailable;
        if (enableDistortion && distortShader == null)
            Debug.LogWarning("[PortalVFX] Assign the PortalDistort shader to get heat distortion.", this);

        cam = Camera.main;

        BuildWindow();
        if (distortionOn) BuildHalo();
        BuildParticles();
        if (enableLight) BuildLight();

        lastRateMul = -1f;
        surge = 0f;
        surgeActive = false;
        ScheduleNextSurge();
        built = true;
    }

    private void Clear()
    {
        for (int i = 0; i < created.Count; i++)
            if (created[i] != null) Destroy(created[i]);
        created.Clear();

        for (int i = 0; i < mats.Count; i++)
            if (mats[i] != null) Destroy(mats[i]);
        mats.Clear();

        discT = null; discR = null; discMat = null;
        haloT = null; haloR = null; haloMat = null; hazeMat = null;
        rays = null; haze = null; embers = null; sparks = null;
        glow = null;
    }

    private GameObject NewChild(string childName)
    {
        GameObject go = new GameObject(childName);
        go.transform.SetParent(transform, false);
        created.Add(go);
        return go;
    }

    private Material NewMaterial(Shader shader, string matName, int queue)
    {
        Material m = new Material(shader) { name = matName + " (instance)", renderQueue = queue };
        mats.Add(m);
        return m;
    }

    private static Mesh GetQuad()
    {
        if (quad != null) return quad;
        quad = new Mesh { name = "PortalQuad" };
        quad.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
            new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f)
        };
        quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        quad.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
        quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
        quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        quad.RecalculateBounds();
        return quad;
    }

    private MeshRenderer NewQuad(string childName, Material m, out Transform t)
    {
        GameObject go = NewChild(childName);
        go.AddComponent<MeshFilter>().sharedMesh = GetQuad();
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        mr.sharedMaterial = m;
        t = go.transform;
        return mr;
    }

    private void BuildWindow()
    {
        discMat = NewMaterial(windowShader, "PortalWindow", 3000);
        discR = NewQuad("PortalWindow", discMat, out discT);

        bool hasTex = centerTexture != null;
        bool hasRim = rimTexture != null;
        if (hasTex) discMat.SetTexture(PCenterTex, centerTexture);
        if (hasRim) discMat.SetTexture(PRimTex, rimTexture);
        discMat.SetFloat(PUseTexture, hasTex ? 1f : 0f);
        discMat.SetFloat(PUseRimTex, hasRim ? 1f : 0f);
        discMat.SetFloat(PRimTexTiling, Mathf.Max(1, rimTextureTiling));

        discMat.SetColor(PCenterTint, centerTint);
        discMat.SetVector(PTexParams, new Vector4(textureTiling.x, textureTiling.y, textureScroll.x, textureScroll.y));
        discMat.SetFloat(PParallax, parallax);
        discMat.SetFloat(PLens, lens);
        discMat.SetFloat(PChroma, chroma);
        discMat.SetFloat(PWarp, warp);
        discMat.SetFloat(PVignette, vignette);
        // no picture and no screen copy: leave the middle clear instead of black
        discMat.SetFloat(PCenterOpacity, (hasTex || sceneAvailable) ? centerOpacity : 0f);

        discMat.SetColor(PFireHot, fireHot);
        discMat.SetColor(PFireMid, fireMid);
        discMat.SetColor(PFireDark, fireDark);
        discMat.SetFloat(PIntensity, fireIntensity);
        discMat.SetFloat(PFireWidth, fireWidth);
        discMat.SetFloat(PTongues, flameTongues);
        discMat.SetFloat(PTongueFreq, tongueCount);
        discMat.SetFloat(PRunPulse, runningPulse);
        discMat.SetFloat(PCrackScale, crackScale);
        discMat.SetFloat(PCrackSharp, crackSharpness);
        discMat.SetFloat(PFireFlicker, fireFlicker);
        discMat.SetFloat(PFireSpeed, fireSpeed);
        discMat.SetFloat(PInnerGlow, innerGlow);
        discMat.SetFloat(POuterGlow, outerGlow);

        discMat.SetFloat(PDiscRadius, discFill);
        discMat.SetFloat(PSoftness, edgeSoftness);
        discMat.SetFloat(PSurge, 0f);
        discMat.SetFloat(PFade, 0f);
    }

    // a camera-facing quad that bends the picture around the portal with heat ripples
    private void BuildHalo()
    {
        haloMat = NewMaterial(distortShader, "PortalHalo", 2990);
        haloR = NewQuad("PortalHalo", haloMat, out haloT);

        haloMat.SetFloat(PMode, 1f);
        haloMat.SetFloat(PStrength, haloStrength);
        haloMat.SetFloat(PShock, 2f);
        haloMat.SetFloat(PShockStrength, shockStrength);
        haloMat.SetFloat(PFade, 0f);
        haloMat.SetFloat(PZTest, (float)(haloIgnoresDepth ? CompareFunction.Always : CompareFunction.LessEqual));
        haloMat.SetVector(PCenter, transform.position);
    }

    private void BuildParticles()
    {
        // rays start on a disc in FRONT of the portal and fly straight at its centre
        Vector2 rl = Sorted(rayLifetime);
        float cone = radius * rayConeRadius;
        float front = radius * rayFrontDistance;
        Vector3 frontPos = new Vector3(0f, 0f, front);
        float raySpeed = -(Mathf.Sqrt(front * front + cone * cone) * 0.95f / Mathf.Max(0.05f, rl.y));

        if (enableRays)
        {
            Material m = NewMaterial(particleShader, "PortalRays", 3001);
            m.SetColor(PTint, rayColor);
            rays = Emitter("PortalRays", m, rayLifetime, rayWidth, cone, 1f, frontPos, raySpeed, 0f, 0f, 500, rayLengthScale, false);
        }

        if (distortionOn && enableRayHaze)
        {
            hazeMat = NewMaterial(distortShader, "PortalRayHaze", 2992);
            hazeMat.SetFloat(PMode, 0f);
            hazeMat.SetFloat(PStrength, hazeStrength);
            hazeMat.SetFloat(PFalloff, 1.5f);
            hazeMat.SetFloat(PFade, 0f);
            hazeMat.SetFloat(PZTest, (float)CompareFunction.LessEqual);
            hazeMat.SetVector(PCenter, transform.position);
            haze = Emitter("PortalRayHaze", hazeMat, rayLifetime, new Vector2(0.25f, 0.5f), cone, 1f, frontPos, raySpeed, 0f, 0f, 300, 5f, false);
        }

        if (enableEmbers)
        {
            Material m = NewMaterial(particleShader, "PortalEmbers", 3001);
            m.SetColor(PTint, emberColor);
            embers = Emitter("PortalEmbers", m, emberLifetime, emberSize, radius * 0.95f, 1f, Vector3.zero, 0f, emberRise, 0.3f, 150, 0f, true);

            Material ms = NewMaterial(particleShader, "PortalSparks", 3001);
            ms.SetColor(PTint, emberColor);
            sparks = Emitter("PortalSparks", ms, new Vector2(0.8f, 1.6f), new Vector2(0.015f, 0.04f), radius, 0.08f,
                             Vector3.zero, 0f, sparkRise, 0.5f, 150, 0f, true);
        }
    }

    private static ParticleSystem.MinMaxCurve Zero => new ParticleSystem.MinMaxCurve(0f);

    private static Vector2 Sorted(Vector2 v)
    {
        return new Vector2(Mathf.Min(v.x, v.y), Mathf.Max(v.x, v.y));
    }

    // one helper builds every particle layer
    //  shapePos / radialSpeed : where particles spawn and how fast they fly toward (negative) or away from the portal centre
    //  worldUp                : drift along world up (embers, sparks)
    //  stretch                : > 0 draws streaks of that length scale (rays), 0 draws round dots
    //  flicker                : dots flicker like embers instead of simply fading in and out
    private ParticleSystem Emitter(string childName, Material mat, Vector2 life, Vector2 size, float shapeRadius, float thickness,
                                   Vector3 shapePos, float radialSpeed, float worldUp, float wander, int max, float stretch, bool flicker)
    {
        GameObject go = NewChild(childName);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); // configure while stopped

        ParticleSystemRenderer psr = go.GetComponent<ParticleSystemRenderer>();
        psr.shadowCastingMode = ShadowCastingMode.Off;
        psr.receiveShadows = false;
        psr.lightProbeUsage = LightProbeUsage.Off;
        psr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        psr.sharedMaterial = mat;
        if (stretch > 0f)
        {
            psr.renderMode = ParticleSystemRenderMode.Stretch;
            psr.lengthScale = stretch;
            psr.velocityScale = rayVelocityScale;
            psr.cameraVelocityScale = 0f;
        }
        else
        {
            psr.renderMode = ParticleSystemRenderMode.Billboard;
        }

        life = Sorted(life);
        size = Sorted(size);

        var main = ps.main;
        main.loop = true;
        main.duration = 5f;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
        main.startColor = Color.white;
        main.maxParticles = max;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f; // raised by Update while the portal opens

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle; // lies in the local XY plane, like the window
        shape.radius = shapeRadius;
        shape.radiusThickness = thickness;
        shape.arc = 360f;
        shape.position = shapePos;

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = worldUp != 0f ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
        vel.x = Zero; vel.y = new ParticleSystem.MinMaxCurve(worldUp); vel.z = Zero;
        vel.orbitalX = Zero; vel.orbitalY = Zero; vel.orbitalZ = Zero;
        vel.radial = new ParticleSystem.MinMaxCurve(radialSpeed);

        if (wander > 0f)
        {
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = wander;
            noise.frequency = 0.6f;
            noise.scrollSpeed = 0.5f;
            noise.damping = true;
        }

        var sz = ps.sizeOverLifetime;
        sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.4f), new Keyframe(0.15f, 1f), new Keyframe(1f, 0.15f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = flicker ? FlickerGradient() : FadeGradient();

        ps.Play();
        return ps;
    }

    private static Gradient FadeGradient()
    {
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[]
            {
                new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f),
                new GradientAlphaKey(1f, 0.55f), new GradientAlphaKey(0f, 1f)
            });
        return g;
    }

    private static Gradient FlickerGradient()
    {
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[]
            {
                new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f),
                new GradientAlphaKey(0.35f, 0.25f), new GradientAlphaKey(1f, 0.4f),
                new GradientAlphaKey(0.45f, 0.55f), new GradientAlphaKey(0.9f, 0.7f),
                new GradientAlphaKey(0f, 1f)
            });
        return g;
    }

    private void BuildLight()
    {
        GameObject go = NewChild("PortalGlow");
        glow = go.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = lightColor;
        glow.range = lightRange;
        glow.intensity = 0f;
        glow.shadows = LightShadows.None;
    }

    // ------------------------------------------------------------------ editor helper
    private void OnDrawGizmosSelected()
    {
        Gizmos.matrix = transform.localToWorldMatrix;

        // the window
        Gizmos.color = new Color(1f, 0.7f, 0.3f, 0.95f);
        DrawCircle(radius, 0f);

        // the heat halo
        Gizmos.color = new Color(1f, 0.7f, 0.3f, 0.2f);
        DrawCircle(radius * haloRadius, 0f);

        // where the rays start (in front), and the cone they fly through
        float front = radius * rayFrontDistance;
        float cone = radius * rayConeRadius;
        Gizmos.color = new Color(1f, 0.5f, 0.2f, 0.7f);
        DrawCircle(cone, front);
        Gizmos.DrawLine(new Vector3(cone, 0f, front), Vector3.zero);
        Gizmos.DrawLine(new Vector3(-cone, 0f, front), Vector3.zero);
        Gizmos.DrawLine(new Vector3(0f, cone, front), Vector3.zero);
        Gizmos.DrawLine(new Vector3(0f, -cone, front), Vector3.zero);

        // the portal faces along local +Z
        Gizmos.color = new Color(0.3f, 0.5f, 1f, 1f);
        Gizmos.DrawLine(Vector3.zero, Vector3.forward * radius * 0.6f);
    }

    private static void DrawCircle(float r, float z)
    {
        const int steps = 48;
        Vector3 prev = new Vector3(r, 0f, z);
        for (int i = 1; i <= steps; i++)
        {
            float a = i / (float)steps * Mathf.PI * 2f;
            Vector3 next = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, z);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }
}