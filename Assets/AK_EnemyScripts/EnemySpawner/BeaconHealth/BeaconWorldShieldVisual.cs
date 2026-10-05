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

    [Tooltip("Optional. If assigned, the shield dissolves top-to-bottom when it drops instead of vanishing instantly.")]
    [SerializeField] private ShieldDissolve shieldDissolve;

    [Header("Visual Settings")]
    [Tooltip("Starting local scale of the shield when at 100% integrity.")]
    [SerializeField] private Vector3 initialShieldScale = Vector3.one * 5f;

    [Tooltip("If true, the shield scales down as it wears off.")]
    [SerializeField] private bool shrinkAsItWearsOff = true;

    [Tooltip("Smallest size (fraction of the initial scale) the shield shrinks to. Keep above 0, otherwise the shield is invisible by the time the dissolve plays.")]
    [SerializeField, Range(0f, 1f)] private float minScaleFactor = 0.6f;

    [Header("Color Gradient")]
    [Tooltip("Color of the shield when at full integrity (0 kills). HDR: raise intensity for glow.")]
    [SerializeField, ColorUsage(true, true)] private Color fullShieldColor = Color.green;

    [Tooltip("Color of the shield when the quota is almost met (Low shield). HDR: raise intensity for glow.")]
    [SerializeField, ColorUsage(true, true)] private Color lowShieldColor = Color.red;

    // Reference names of the color properties in the shield Shader Graph.
    private static readonly int FrontColorId = Shader.PropertyToID("_FronColor");
    private static readonly int FresnelColorId = Shader.PropertyToID("_FresnelColor");

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

        ApplyColor(fullShieldColor);
    }

    /// <summary>
    /// Scales and recolors the 3D world shield as enemies are defeated and quota progresses.
    /// </summary>
    private void UpdateShieldVisuals(int currentKills, int requiredQuota)
    {
        if (requiredQuota <= 0) return;

        float quotaRatio = Mathf.Clamp01((float)currentKills / requiredQuota);
        float shieldIntegrity = 1f - quotaRatio;

        if (shrinkAsItWearsOff && shieldVisualTransform != null)
        {
            float scaleFactor = Mathf.Lerp(minScaleFactor, 1f, shieldIntegrity);
            shieldVisualTransform.localScale = initialShieldScale * scaleFactor;
        }

        ApplyColor(Color.Lerp(fullShieldColor, lowShieldColor, quotaRatio));
    }

    private void ApplyColor(Color color)
    {
        if (shieldRenderer == null) return;

        Material m = shieldRenderer.material;
        bool hasFront = m.HasProperty(FrontColorId);
        bool hasFresnel = m.HasProperty(FresnelColorId);

        if (hasFront) m.SetColor(FrontColorId, color);
        if (hasFresnel) m.SetColor(FresnelColorId, color);

        if (!hasFront && !hasFresnel)
            Debug.LogWarning("[BeaconWorldShieldVisual] Shield material has no '_FronColor' / '_FresnelColor' property. Check the Reference names in the Shader Graph.", this);
    }

    /// <summary>
    /// Triggered when the shield officially drops to zero.
    /// </summary>
    private void DisableShieldVisuals()
    {
        if (shieldDissolve != null)
        {
            shieldDissolve.Break();   // animated; switches the shield off when finished
        }
        else if (shieldRenderer != null)
        {
            // Only hide the mesh. Never deactivate the object: it may also hold BeaconHealth and the hitbox.
            shieldRenderer.enabled = false;
        }
        else if (shieldVisualTransform != null)
        {
            shieldVisualTransform.gameObject.SetActive(false);
        }

        Debug.Log("[BeaconWorldShieldVisual] World shield depleted.");
    }
}