using UnityEngine;
using UnityEngine.UI;
using System.Collections;

[RequireComponent(typeof(RectTransform))]
public class UILineConnector : MonoBehaviour
{
    [Header("Connections")]
    public RectTransform parentTransform;

    [Header("Line Visuals")]
    [Tooltip("Line thickness in pixels.")]
    public float thickness = 6f;
    [Tooltip("Color of the connector line.")]
    public Color lineColor = Color.white;
    [Tooltip("Optional sprite or material texture.")]
    public Material lineMaterial;
    
    [Header("Animation")]
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
        lineObj.transform.SetSiblingIndex(0); // Place line behind the node icon

        lineRect = lineObj.GetComponent<RectTransform>();
        lineImage = lineObj.GetComponent<Image>();

        // Middle-left pivot so scaling out stretches the line from parent to child
        lineRect.anchorMin = new Vector2(0.5f, 0.5f);
        lineRect.anchorMax = new Vector2(0.5f, 0.5f);
        lineRect.pivot = new Vector2(0f, 0.5f);

        lineImage.color = lineColor;

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

        // Apply updated color from inspector
        if (lineImage != null)
        {
            lineImage.color = lineColor;
            if (lineMaterial != null) lineImage.material = lineMaterial;
        }

        // 1. Calculate world positions of node centers
        Vector3 startWorld = GetRectCenterWorld(parentTransform);
        Vector3 endWorld = GetRectCenterWorld(myRect);

        // 2. Convert to UI local container space
        Vector3 startLocal = lineContainer.InverseTransformPoint(startWorld);
        Vector3 endLocal = lineContainer.InverseTransformPoint(endWorld);

        startLocal.z = 0f;
        endLocal.z = 0f;

        Vector2 direction = (Vector2)endLocal - (Vector2)startLocal;
        float targetDistance = direction.magnitude;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        lineRect.localPosition = startLocal;
        lineRect.localRotation = Quaternion.Euler(0, 0, angle);

        // If drawDuration is 0, draw instantly
        if (drawDuration <= 0f)
        {
            lineRect.sizeDelta = new Vector2(targetDistance, thickness);
            yield break;
        }

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

    private Vector3 GetRectCenterWorld(RectTransform rect)
    {
        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        return (corners[0] + corners[2]) * 0.5f;
    }
}