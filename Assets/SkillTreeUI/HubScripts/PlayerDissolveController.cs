using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerDissolveController : MonoBehaviour
{
    [Header("Settings")]
    public float dissolveDuration = 1.5f;

    [Header("Dissolve Material")]
    [Tooltip("Assign your single Dissolve Shader material here")]
    public Material dissolveMaterial;

    private Renderer[] childRenderers;
    private Dictionary<Renderer, Material[]> originalMaterialsDict = new Dictionary<Renderer, Material[]>();
    private static readonly int DissolveAmountProp = Shader.PropertyToID("_DissolveAmount");

    public void TriggerDissolveIn()
    {
        StartCoroutine(DissolveInRoutine());
    }

    private IEnumerator DissolveInRoutine()
    {
        if (dissolveMaterial == null)
        {
            Debug.LogError("MISSING REFERENCE: Assign the Dissolve Material in the inspector.");
            yield break;
        }

        childRenderers = GetComponentsInChildren<Renderer>();
        if (childRenderers == null || childRenderers.Length == 0) yield break;

        originalMaterialsDict.Clear();

        // 1. Cache the original materials and swap every mesh slot to the dissolve material
        foreach (var rend in childRenderers)
        {
            if (rend == null) continue;

            originalMaterialsDict[rend] = rend.sharedMaterials;

            Material[] dissolveMats = new Material[rend.sharedMaterials.Length];
            for (int i = 0; i < dissolveMats.Length; i++)
            {
                dissolveMats[i] = new Material(dissolveMaterial);
            }
            rend.materials = dissolveMats;

            // Set initial state to fully invisible / dissolved (1f)
            foreach (var mat in rend.materials)
            {
                if (mat != null && mat.HasProperty(DissolveAmountProp))
                {
                    mat.SetFloat(DissolveAmountProp, 1f);
                }
            }
        }

        float elapsedTime = 0f;

        // 2. Animate the dissolve property from 1 down to 0
        while (elapsedTime < dissolveDuration)
        {
            elapsedTime += Time.deltaTime;
            float currentDissolve = Mathf.Lerp(1f, 0f, elapsedTime / dissolveDuration);

            foreach (var rend in childRenderers)
            {
                if (rend == null) continue;
                foreach (var mat in rend.materials)
                {
                    if (mat != null && mat.HasProperty(DissolveAmountProp))
                    {
                        mat.SetFloat(DissolveAmountProp, currentDissolve);
                    }
                }
            }

            yield return null;
        }

        // 3. Once fully faded in, instantly restore all original external toon materials
        foreach (var kvp in originalMaterialsDict)
        {
            if (kvp.Key != null)
            {
                kvp.Key.materials = kvp.Value;
            }
        }
    }
}