using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class UILineConnector : MonoBehaviour
{
    [Header("References")]
    public RectTransform parentTransform;
    
    [Header("Line Visuals")]
    public float thickness = 4f;
    public Color lineColor = Color.white; 
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
        
        // Parent the line to the same container as this node
        lineObj.transform.SetParent(transform.parent, false);
        lineObj.transform.SetSiblingIndex(0);

        lineRect = lineObj.GetComponent<RectTransform>();
        lineImage = lineObj.GetComponent<Image>();

        lineImage.color = lineColor;
        
        // Center-left pivot ensures it stretches correctly toward the target
        lineRect.pivot = new Vector2(0f, 0.5f);
    }

    public void AnimateLine()
    {
        if (parentTransform == null) return;

        StopAllCoroutines();
        StartCoroutine(DrawLineRoutine());
    }

    private IEnumerator DrawLineRoutine()
    {
        RectTransform lineParent = lineRect.parent as RectTransform;

        // Convert the actual world positions of the nodes into the line's local space.
        // This makes the lines perfectly accurate regardless of your UI Anchors.
        Vector3 startLocal = lineParent.InverseTransformPoint(parentTransform.position);
        Vector3 endLocal = lineParent.InverseTransformPoint(myRect.position);

        Vector2 direction = (Vector2)endLocal - (Vector2)startLocal;
        float targetDistance = direction.magnitude;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        // Set the line to start exactly at the parent's center point
        lineRect.localPosition = startLocal;
        lineRect.localRotation = Quaternion.Euler(0, 0, angle);

        float elapsed = 0f;
        while (elapsed < drawDuration)
        {
            elapsed += Time.deltaTime;
            float currentLength = Mathf.Lerp(0f, targetDistance, elapsed / drawDuration);
            
            // Stretch the line width over time
            lineRect.sizeDelta = new Vector2(currentLength, thickness);
            yield return null;
        }

        lineRect.sizeDelta = new Vector2(targetDistance, thickness);
    }
}