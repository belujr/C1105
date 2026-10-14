using System.Collections.Generic;
using UnityEngine;

/// Dash afterimages: ghost copies of the real animated pose, plus an optional camera zoom kick.
/// Put this on the same GameObject as PlayerController.
public class DashVFX : MonoBehaviour
{
    [Header("Material")]
    [Tooltip("Material using shader Dash/GhostFresnel (colors/intensity/alpha are driven by this component)")]
    public Material ghostMaterial;

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

    private readonly List<Ghost> ghosts = new List<Ghost>();
    private Transform runtimeRoot;
    private Camera cam;
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
        CharacterController cc = GetComponentInParent<CharacterController>();
        if (cc != null) charHeight = Mathf.Max(0.5f, cc.height * Mathf.Abs(cc.transform.lossyScale.y));
        maxPartSize = charHeight * 1.6f;
        cullBounds = new Bounds(Vector3.zero, Vector3.one * maxPartSize * 2f);

        if (ghostMaterial == null)
            Debug.LogWarning("[DashVFX] No ghostMaterial assigned - afterimages disabled.", this);
    }

    private void OnDisable()
    {
        isDashing = false;
        ApplyKick(0f);
    }

    private void OnDestroy()
    {
        foreach (var g in ghosts)
        {
            if (g.mat != null) Destroy(g.mat);
            foreach (var m in g.meshes) if (m != null) Destroy(m);
        }
        if (runtimeRoot != null) Destroy(runtimeRoot.gameObject);
    }

    // ================================================================ public API
    // dashSpeed is unused now but kept so DashState doesn't need to change
    public void Begin(Vector3 direction, float dashSpeed)
    {
        if (direction.sqrMagnitude < 0.0001f) direction = transform.forward;
        direction.y = 0f;
        dashDir = direction.normalized;
        isDashing = true;
        spawnTimer = 0f; // first ghost spawns this frame
    }

    public void End()
    {
        isDashing = false;
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
}