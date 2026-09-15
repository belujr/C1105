using System.Collections;
using UnityEngine;

public class PlayerDissolveController : MonoBehaviour
{
    [Header("Settings")]
    public float dissolveDuration = 1.5f;
    
    [Header("References")]
    [Tooltip("Drag the Beta_Surface object here from your hierarchy")]
    public Renderer targetRenderer;
    
    [Header("Materials")]
    public Material dissolveMaterial;
    public Material mainMaterial;
    
    private MaterialPropertyBlock propBlock;
    private static readonly int DissolveAmountProp = Shader.PropertyToID("_DissolveAmount");

    private void Awake()
    {
        propBlock = new MaterialPropertyBlock();
    }

    public void TriggerDissolveIn()
    {
        StartCoroutine(DissolveInRoutine());
    }

    private IEnumerator DissolveInRoutine()
    {
        if (targetRenderer == null || dissolveMaterial == null || mainMaterial == null)
        {
            Debug.LogError("MISSING REFERENCES: Assign the Target Renderer, Dissolve Material, and Main Material in the inspector.");
            yield break;
        }

        // 1. Immediately apply the dissolve material and set it to fully invisible (1)
        targetRenderer.material = dissolveMaterial;
        targetRenderer.GetPropertyBlock(propBlock);
        propBlock.SetFloat(DissolveAmountProp, 1f);
        targetRenderer.SetPropertyBlock(propBlock);

        float elapsedTime = 0f;

        // 2. Animate the dissolve over 1.5 seconds
        while (elapsedTime < dissolveDuration)
        {
            elapsedTime += Time.deltaTime;
            float currentDissolve = Mathf.Lerp(1f, 0f, elapsedTime / dissolveDuration);

            targetRenderer.GetPropertyBlock(propBlock);
            propBlock.SetFloat(DissolveAmountProp, currentDissolve);
            targetRenderer.SetPropertyBlock(propBlock);

            yield return null;
        }

        // 3. Clear property blocks and swap to the final 12 material
        propBlock.Clear();
        targetRenderer.SetPropertyBlock(propBlock);
        targetRenderer.material = mainMaterial;
    }
}