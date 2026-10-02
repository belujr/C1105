using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// Dissolves the player in using Unity Toon Shader's Clipping feature,
/// plus an optional glowing edge drawn by a temporary overlay copy of the meshes.
public class PlayerDissolveController : MonoBehaviour
{
    [Header("Settings")]
    public float dissolveDuration = 1.5f;

    [Header("Noise (same texture the UTS materials use as Clipping Mask)")]
    public Texture2D dissolveNoise;

    [Header("Clipping Level range (tune on a UTS material)")]
    [Tooltip("Clipping_Level where the character is JUST completely invisible")]
    public float hiddenLevel = -0.6f;
    [Tooltip("Clipping_Level where the character is JUST completely visible")]
    public float visibleLevel = 0.6f;

    [Header("Glow Edge (optional)")]
    [Tooltip("Material using the DissolveGlow shader. Leave empty for no glow.")]
    public Material glowMaterial;
    [Range(0.005f, 0.3f)] public float glowWidth = 0.05f;
    [Tooltip("Nudges the glow relative to the dissolve edge if they don't line up")]
    [Range(-0.2f, 0.2f)] public float glowOffset = 0f;

    private static readonly int ClippingMaskProp = Shader.PropertyToID("_ClippingMask");
    private static readonly int ClippingLevelProp = Shader.PropertyToID("_Clipping_Level");
    private static readonly int GlowNoiseProp = Shader.PropertyToID("_NoiseTex");
    private static readonly int GlowDissolveProp = Shader.PropertyToID("_DissolveAmount");
    private static readonly int GlowWidthProp = Shader.PropertyToID("_GlowWidth");

    private readonly List<Renderer> meshRenderers = new List<Renderer>();
    private readonly List<GameObject> overlays = new List<GameObject>();
    private MaterialPropertyBlock block;
    private Material glowInstance;

    public void TriggerDissolveIn()
    {
        StartCoroutine(DissolveInRoutine());
    }

    private IEnumerator DissolveInRoutine()
    {
        if (dissolveNoise == null)
        {
            Debug.LogError("MISSING REFERENCE: Assign the Dissolve Noise texture in the inspector.");
            yield break;
        }

        meshRenderers.Clear();
        foreach (var r in GetComponentsInChildren<Renderer>())
        {
            if (r is SkinnedMeshRenderer || r is MeshRenderer)
                meshRenderers.Add(r);
        }
        if (meshRenderers.Count == 0) yield break;

        block = new MaterialPropertyBlock();
        BuildGlowOverlays();

        ApplyProgress(0f); // fully hidden before the first rendered frame

        float elapsed = 0f;
        while (elapsed < dissolveDuration)
        {
            elapsed += Time.deltaTime;
            ApplyProgress(Mathf.Clamp01(elapsed / dissolveDuration));
            yield return null;
        }

        // Back to the untouched toon materials
        foreach (var r in meshRenderers)
            if (r != null) r.SetPropertyBlock(null);

        CleanupOverlays();
    }

    // t: 0 = fully hidden, 1 = fully visible
    private void ApplyProgress(float t)
    {
        float level = Mathf.Lerp(hiddenLevel, visibleLevel, t);
        foreach (var r in meshRenderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(block);
            block.SetTexture(ClippingMaskProp, dissolveNoise);
            block.SetFloat(ClippingLevelProp, level);
            r.SetPropertyBlock(block);
        }

        if (glowInstance != null)
        {
            // character shows pixels where noise >= (1 - t); glow sits just inside that edge
            glowInstance.SetFloat(GlowDissolveProp, Mathf.Clamp01(1f - t + glowOffset));
        }
    }

    private void BuildGlowOverlays()
    {
        CleanupOverlays();
        if (glowMaterial == null) return;

        glowInstance = new Material(glowMaterial);
        glowInstance.SetTexture(GlowNoiseProp, dissolveNoise);
        glowInstance.SetFloat(GlowWidthProp, glowWidth);

        foreach (var r in meshRenderers)
        {
            GameObject go = new GameObject(r.name + "_GlowOverlay");
            go.layer = r.gameObject.layer;
            go.transform.SetParent(r.transform, false);

            Renderer overlayRenderer = null;
            int subMeshCount = 1;

            if (r is SkinnedMeshRenderer smr)
            {
                if (smr.sharedMesh == null) { Destroy(go); continue; }
                var o = go.AddComponent<SkinnedMeshRenderer>();
                o.sharedMesh = smr.sharedMesh;
                o.bones = smr.bones;
                o.rootBone = smr.rootBone;
                o.localBounds = smr.localBounds;
                o.updateWhenOffscreen = smr.updateWhenOffscreen;
                subMeshCount = smr.sharedMesh.subMeshCount;
                overlayRenderer = o;
            }
            else
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) { Destroy(go); continue; }
                go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                overlayRenderer = go.AddComponent<MeshRenderer>();
                subMeshCount = mf.sharedMesh.subMeshCount;
            }

            Material[] mats = new Material[subMeshCount];
            for (int i = 0; i < mats.Length; i++) mats[i] = glowInstance;
            overlayRenderer.sharedMaterials = mats;
            overlayRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            overlayRenderer.receiveShadows = false;

            overlays.Add(go);
        }
    }

    private void CleanupOverlays()
    {
        foreach (var go in overlays)
            if (go != null) Destroy(go);
        overlays.Clear();

        if (glowInstance != null)
        {
            Destroy(glowInstance);
            glowInstance = null;
        }
    }

    private void OnDestroy()
    {
        CleanupOverlays();
    }
}