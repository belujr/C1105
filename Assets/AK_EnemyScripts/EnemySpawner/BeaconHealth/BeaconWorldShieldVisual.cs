using UnityEngine;

public class BeaconWorldShieldVisual : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Reference to the BeaconHealth component in the scene.")]
    [SerializeField] private BeaconHealth beaconHealth;

    [Tooltip("The 3D world-space shield visual Transform (e.g., energy bubble around the beacon).")]
    [SerializeField] private Transform shieldVisualTransform;

    [Tooltip("The Renderer component on the shield visual (used for changing material color).")]
    [SerializeField] private Renderer shieldRenderer;

    [Header("Visual Settings")]
    [Tooltip("Starting local scale of the shield when at 100% integrity.")]
    [SerializeField] private Vector3 initialShieldScale = Vector3.one * 5f;

    [Tooltip("If true, the shield scales down as it wears off.")]
    [SerializeField] private bool shrinkAsItWearsOff = true;

    [Header("Color Gradient")]
    [Tooltip("Color of the shield when at full integrity (0 kills).")]
    [SerializeField] private Color fullShieldColor = Color.green;

    [Tooltip("Color of the shield when the quota is almost met (Low shield).")]
    [SerializeField] private Color lowShieldColor = Color.red;

    private void OnEnable()
    {
        if (beaconHealth != null)
        {
            beaconHealth.OnQuotaUpdated.AddListener(UpdateShieldVisuals);
            beaconHealth.OnShieldDropped.AddListener(DisableShieldVisuals);
        }
    }

    private void OnDisable()
    {
        if (beaconHealth != null)
        {
            beaconHealth.OnQuotaUpdated.RemoveListener(UpdateShieldVisuals);
            beaconHealth.OnShieldDropped.RemoveListener(DisableShieldVisuals);
        }
    }

    private void Start()
    {
        if (shieldVisualTransform != null)
        {
            shieldVisualTransform.localScale = initialShieldScale;
            shieldVisualTransform.gameObject.SetActive(true);
        }

        // Initialize shield color to full green
        if (shieldRenderer != null)
        {
            shieldRenderer.material.color = fullShieldColor;
        }
    }

    /// <summary>
    /// Scales and recolors the 3D world shield as enemies are defeated and quota progresses.
    /// </summary>
    private void UpdateShieldVisuals(int currentKills, int requiredQuota)
    {
        if (requiredQuota <= 0) return;

        // Calculate quota progress ratio (0.0 at start up to 1.0 at quota met)
        float quotaRatio = Mathf.Clamp01((float)currentKills / requiredQuota);
        float shieldIntegrity = 1f - quotaRatio;

        // 1. Optional Shrink
        if (shrinkAsItWearsOff && shieldVisualTransform != null)
        {
            shieldVisualTransform.localScale = initialShieldScale * shieldIntegrity;
        }

        // 2. Smooth Color Lerp (Green -> Red)
        if (shieldRenderer != null)
        {
            Color lerpedColor = Color.Lerp(fullShieldColor, lowShieldColor, quotaRatio);
            shieldRenderer.material.color = lerpedColor;
        }
    }

    /// <summary>
    /// Triggered when the shield officially drops to zero.
    /// </summary>
    private void DisableShieldVisuals()
    {
        if (shieldVisualTransform != null)
        {
            shieldVisualTransform.gameObject.SetActive(false);
        }

        Debug.Log("[BeaconWorldShieldVisual] World shield depleted and disabled.");
    }
}