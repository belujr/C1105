using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Lives on every enemy (added automatically by HitFeedbackManager on the first hit, you don't have to add it).
///
///  1. GLOW: builds a hidden "shell" copy of each of the enemy's meshes with the VFX/HitGlow material and switches it
///     on for a moment when the enemy is hit. The shell follows animation and skinning, and the enemy's own materials
///     are never touched.
///  2. KILL: when the enemy is destroyed or switched off shortly after being hit, soul wisps burst out of it.
///     For an exact moment, call NotifyKilled() from your death code instead.
/// </summary>
public class EnemyHitFX : MonoBehaviour
{
    [Tooltip("An enemy that disappears within this many seconds of a hit counts as killed (soul burst).")]
    public float killWindow = 4f;

    [Tooltip("Meshes that must never glow (helper capsules, shields, indicators).")]
    public List<Renderer> excludeRenderers = new List<Renderer>();
    [Tooltip("Writes which meshes got a glow shell into the Console (for finding a wrong shape).")]
    public bool logShells = false;

    private readonly List<Renderer> shells = new List<Renderer>();
    private readonly List<SkinnedMeshRenderer> shellSources = new List<SkinnedMeshRenderer>();
    private readonly List<SkinnedMeshRenderer> shellSkinned = new List<SkinnedMeshRenderer>();
    private MaterialPropertyBlock block;
    private bool built;

    private bool glowActive;
    private float glowStart;
    private float glowDuration;
    private float glowIntensity;
    private float glowRadius;
    private Vector3 glowPos;
    private Color glowHot;
    private Color glowEdge;

    private float lastHitTime = -100f;
    private Vector3 lastHitDirection = Vector3.forward;
    private Vector3 lastCenter;
    private bool killHandled;

    private Renderer centerRenderer;

    private void Awake()
    {
        // the enemy's own first renderer (found before any glow shell exists), used to know where the enemy is
        centerRenderer = GetComponentInChildren<Renderer>();
    }

    private void OnEnable()
    {
        killHandled = false;
        lastHitTime = -100f;
    }

    private void OnDisable()
    {
        TryKillBurst();
    }

    private void OnDestroy()
    {
        TryKillBurst();
    }

    /// <summary>Remember a hit (used for kill detection).</summary>
    public void RegisterHit(Vector3 direction)
    {
        lastHitTime = Time.unscaledTime;
        if (direction.sqrMagnitude > 0.0001f) lastHitDirection = direction.normalized;
    }

    /// <summary>Call this from your enemy death code for an exact soul burst at the moment of death.</summary>
    public void NotifyKilled()
    {
        if (killHandled) return;
        killHandled = true;
        HitFeedbackManager mgr = HitFeedbackManager.Instance;
        if (mgr != null) mgr.PlayKill(GetCenter(), lastHitDirection);
    }

    private void TryKillBurst()
    {
        if (killHandled) return;
        if (!Application.isPlaying) return;
        if (!gameObject.scene.isLoaded) return;                      // the scene is being unloaded
        if (Time.unscaledTime - lastHitTime > killWindow) return;    // was not hit recently

        killHandled = true;
        HitFeedbackManager mgr = HitFeedbackManager.Instance;
        if (mgr != null) mgr.PlayKill(lastCenter != Vector3.zero ? lastCenter : transform.position, lastHitDirection);
    }

    private Vector3 GetCenter()
    {
        return centerRenderer != null ? centerRenderer.bounds.center : transform.position + Vector3.up;
    }

    // ------------------------------------------------------------------ glow
    public void PlayGlow(Material glowMaterial, Vector3 hitPoint, float intensity, float duration, float radius,
                         Color hot, Color edge)
    {
        if (glowMaterial == null) return;
        if (!built) BuildShells(glowMaterial);
        if (shells.Count == 0) return;

        glowActive = true;
        glowStart = Time.unscaledTime;
        glowDuration = Mathf.Max(0.05f, duration);
        glowIntensity = intensity;
        glowRadius = radius;
        glowPos = hitPoint;
        glowHot = hot;
        glowEdge = edge;

        for (int i = 0; i < shells.Count; i++)
            if (shells[i] != null) shells[i].enabled = true;

        ApplyGlow(0f);
    }

    private static bool IsVisibleRenderer(Renderer r)
    {
        // skip anything the player cannot actually see: disabled renderers, inactive objects, shadow-only or material-less meshes
        if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) return false;
        if (r.shadowCastingMode == ShadowCastingMode.ShadowsOnly) return false;
        if (r.sharedMaterial == null) return false;
        if (r.gameObject.name == "HitGlowShell") return false;
        return true;
    }

    private void BuildShells(Material glowMaterial)
    {
        built = true;
        block = new MaterialPropertyBlock();

        // 1) skinned meshes (characters). If the enemy has any, ONLY these get a glow shell,
        //    so helper meshes (capsules, shields, indicators) never glow.
        SkinnedMeshRenderer[] skinned = GetComponentsInChildren<SkinnedMeshRenderer>(false);
        for (int i = 0; i < skinned.Length; i++)
        {
            SkinnedMeshRenderer src = skinned[i];
            if (!IsVisibleRenderer(src) || src.sharedMesh == null) continue;
            if (excludeRenderers.Contains(src)) continue;

            GameObject go = new GameObject("HitGlowShell");
            go.transform.SetParent(src.transform, false);

            SkinnedMeshRenderer shell = go.AddComponent<SkinnedMeshRenderer>();
            shell.sharedMesh = src.sharedMesh;
            shell.bones = src.bones;
            shell.rootBone = src.rootBone;
            shell.localBounds = src.localBounds;
            shell.updateWhenOffscreen = src.updateWhenOffscreen;
            shell.quality = src.quality;

            int sub = Mathf.Max(1, src.sharedMesh.subMeshCount);
            Material[] mats = new Material[sub];
            for (int m = 0; m < sub; m++) mats[m] = glowMaterial;
            shell.sharedMaterials = mats;

            SetupShell(shell);
            shells.Add(shell);
            shellSources.Add(src);
            shellSkinned.Add(shell);
            if (logShells) Debug.Log("[EnemyHitFX] Glow shell on skinned mesh: " + src.name, src);
        }

        // 2) static meshes: only used when the enemy has no skinned mesh (rigid props / simple enemies)
        if (shells.Count == 0)
        {
            MeshFilter[] filters = GetComponentsInChildren<MeshFilter>(false);
            for (int i = 0; i < filters.Length; i++)
            {
                MeshFilter mf = filters[i];
                if (mf == null || mf.sharedMesh == null) continue;
                MeshRenderer mr = mf.GetComponent<MeshRenderer>();
                if (!IsVisibleRenderer(mr)) continue;
                if (excludeRenderers.Contains(mr)) continue;

                GameObject go = new GameObject("HitGlowShell");
                go.transform.SetParent(mf.transform, false);

                MeshFilter shellFilter = go.AddComponent<MeshFilter>();
                shellFilter.sharedMesh = mf.sharedMesh;
                MeshRenderer shell = go.AddComponent<MeshRenderer>();

                int sub = Mathf.Max(1, mf.sharedMesh.subMeshCount);
                Material[] mats = new Material[sub];
                for (int m = 0; m < sub; m++) mats[m] = glowMaterial;
                shell.sharedMaterials = mats;

                SetupShell(shell);
                shells.Add(shell);
                if (logShells) Debug.Log("[EnemyHitFX] Glow shell on mesh: " + mf.name, mf);
            }
        }

        if (shells.Count == 0 && logShells)
            Debug.LogWarning("[EnemyHitFX] No visible meshes found on " + name + ", so there is nothing to glow.", this);
    }

    private static void SetupShell(Renderer shell)
    {
        shell.shadowCastingMode = ShadowCastingMode.Off;
        shell.receiveShadows = false;
        shell.lightProbeUsage = LightProbeUsage.Off;
        shell.reflectionProbeUsage = ReflectionProbeUsage.Off;
        shell.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        shell.enabled = false;
    }

    private void LateUpdate()
    {
        if (!killHandled) lastCenter = GetCenter();

        if (!glowActive) return;

        float p = (Time.unscaledTime - glowStart) / glowDuration; // real time, so the glow plays during hit stop
        if (p >= 1f)
        {
            glowActive = false;
            for (int i = 0; i < shells.Count; i++)
                if (shells[i] != null) shells[i].enabled = false;
            return;
        }

        // blend shapes (facial expressions, squash) must follow the original
        for (int i = 0; i < shellSkinned.Count; i++)
        {
            SkinnedMeshRenderer src = shellSources[i];
            SkinnedMeshRenderer dst = shellSkinned[i];
            if (src == null || dst == null || src.sharedMesh == null) continue;
            int count = src.sharedMesh.blendShapeCount;
            for (int b = 0; b < count; b++) dst.SetBlendShapeWeight(b, src.GetBlendShapeWeight(b));
        }

        ApplyGlow(p);
    }

    private void ApplyGlow(float p)
    {
        block.SetVector("_HitPos", glowPos);
        block.SetFloat("_Progress", p);
        block.SetFloat("_Intensity", glowIntensity);
        block.SetFloat("_MaxRadius", glowRadius);
        block.SetColor("_ColorHot", glowHot);
        block.SetColor("_ColorEdge", glowEdge);

        for (int i = 0; i < shells.Count; i++)
            if (shells[i] != null) shells[i].SetPropertyBlock(block);
    }
}