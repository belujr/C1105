using UnityEngine;

[RequireComponent(typeof(RectTransform))]
public class DialogueSkipArrow : MonoBehaviour
{
    [Header("Bobbing Motion Settings")]
    [Tooltip("Distance in UI pixels the arrow drops downward from its baseline.")]
    public float bounceAmplitude = 14f;

    [Tooltip("Frequency/speed of the bobbing cycle.")]
    public float bounceSpeed = 5.5f;

    [Header("Squash & Stretch Settings")]
    [Tooltip("Percentage to flatten Y and expand X upon hitting the bottom (e.g. 0.2 = 20% deformation).")]
    [Range(0f, 0.5f)]
    public float squashAmount = 0.22f;

    [Tooltip("Higher values isolate the squash impact exclusively to the lowest point.")]
    public float squashSharpness = 3.5f;

    private RectTransform rectTransform;
    private Vector2 startPosition;
    private Vector3 initialScale;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        startPosition = rectTransform.anchoredPosition;
        initialScale = rectTransform.localScale;
    }

    private void OnEnable()
    {
        if (rectTransform == null) rectTransform = GetComponent<RectTransform>();
        
        // Reset position and scale cleanly whenever prompt is activated
        rectTransform.anchoredPosition = startPosition;
        rectTransform.localScale = initialScale;
    }

    private void Update()
    {
        // 1. Calculate normalized wave cycle [0 = Bottom peak, 1 = Top peak]
        float rawCycle = (Mathf.Sin(Time.time * bounceSpeed) + 1f) * 0.5f;

        // 2. Apply SmoothStep (Easy Ease) to ease acceleration and deceleration
        float easedT = Mathf.SmoothStep(0f, 1f, rawCycle);

        // 3. Smooth Y-axis movement
        float yOffset = Mathf.Lerp(-bounceAmplitude, 0f, easedT);
        rectTransform.anchoredPosition = startPosition + new Vector2(0f, yOffset);

        // 4. Calculate squash intensity (power curve triggers squash right at lowest point)
        float bottomImpact = Mathf.Pow(1f - easedT, squashSharpness);

        // X stretches out while Y squashes down on impact
        float scaleX = Mathf.Lerp(initialScale.x, initialScale.x * (1f + squashAmount), bottomImpact);
        float scaleY = Mathf.Lerp(initialScale.y, initialScale.y * (1f - squashAmount), bottomImpact);

        rectTransform.localScale = new Vector3(scaleX, scaleY, initialScale.z);
    }
}