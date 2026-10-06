using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class UILineConnector : MonoBehaviour
{
    [Header("References")]
    public RectTransform parentTransform;
    
    [Header("Line Visuals")]
    [Tooltip("Increase thickness (e.g., 24-32) to allow room for the outer electric glow.")]
    public float thickness = 28f;
    public Material lineMaterial;
    public float drawDuration = 0.35f;

    private RectTransform myRect;
    private RectTransform lineRect;
    private Image lineImage;

    private void Awake()
    {
        myRect = GetComponent<RectTransform>();
        CreateLineObject();
    }

    private void CreateLineObject()
    {
        GameObject lineObj = new GameObject("Line_To_Parent", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        
        lineObj.transform.SetParent(transform.parent, false);
        lineObj.transform.SetSiblingIndex(0);

        lineRect = lineObj.GetComponent<RectTransform>();
        lineImage = lineObj.GetComponent<Image>();

        // Lock anchors to middle-center to prevent layout stretching distortion
        lineRect.anchorMin = new Vector2(0.5f, 0.5f);
        lineRect.anchorMax = new Vector2(0.5f, 0.5f);
        lineRect.pivot = new Vector2(0f, 0.5f);

        if (lineMaterial != null)
        {
            lineImage.material = lineMaterial;
        }
    }

    public void AnimateLine()
    {
        if (parentTransform == null) return;

        StopAllCoroutines();
        StartCoroutine(DrawLineRoutine());
    }

    private IEnumerator DrawLineRoutine()
    {
        RectTransform lineContainer = lineRect.parent as RectTransform;

        // 1. Calculate true visual center of both UI nodes (ignores pivot settings)
        Vector3 startWorld = GetRectCenterWorld(parentTransform);
        Vector3 endWorld = GetRectCenterWorld(myRect);

        // 2. Convert world centers to the local space of the line's parent
        Vector3 startLocal = lineContainer.InverseTransformPoint(startWorld);
        Vector3 endLocal = lineContainer.InverseTransformPoint(endWorld);

        // 3. Flatten Z-depth to force exact 2D alignment
        startLocal.z = 0f;
        endLocal.z = 0f;

        Vector2 direction = (Vector2)endLocal - (Vector2)startLocal;
        float targetDistance = direction.magnitude;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        lineRect.localPosition = startLocal;
        lineRect.localRotation = Quaternion.Euler(0, 0, angle);

        float elapsed = 0f;
        while (elapsed < drawDuration)
        {
            elapsed += Time.deltaTime;
            float currentLength = Mathf.Lerp(0f, targetDistance, elapsed / drawDuration);
            
            lineRect.sizeDelta = new Vector2(currentLength, thickness);
            yield return null;
        }

        lineRect.sizeDelta = new Vector2(targetDistance, thickness);
    }

    // Calculates true bounding-box center regardless of RectTransform Pivot settings
    private Vector3 GetRectCenterWorld(RectTransform rect)
    {
        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        return (corners[0] + corners[2]) * 0.5f;
    }
}