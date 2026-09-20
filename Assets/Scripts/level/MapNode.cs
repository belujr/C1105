using System.Collections.Generic;
using UnityEngine;

public class MapNode : MonoBehaviour
{
    public List<MapNode> nextNodes;
    public Material lineMaterial;
    public float lineWidth = 0.5f;
    public float yOffset = 0.5f;
    public float dashLengthMultiplier = 0.3f;

    [Tooltip("Adds multiple points to the line so the moving glow renders smoothly.")]
    public int lineResolution = 20;

    public Dictionary<MapNode, LineRenderer> pathLines = new Dictionary<MapNode, LineRenderer>();

    void Start()
    {
        foreach (MapNode nextNode in nextNodes)
        {
            if (nextNode == null) continue;

            GameObject pathLine = new GameObject("PathTo_" + nextNode.gameObject.name);
            pathLine.transform.SetParent(transform);

            pathLine.layer = gameObject.layer;

            LineRenderer lr = pathLine.AddComponent<LineRenderer>();

            Vector3 startPos = transform.position + Vector3.up * yOffset;
            Vector3 endPos = nextNode.transform.position + Vector3.up * yOffset;

            // FIX: Subdivide the line into 20 segments so the gradient can physically travel
            lr.positionCount = lineResolution;
            for (int i = 0; i < lineResolution; i++)
            {
                float t = i / (float)(lineResolution - 1);
                lr.SetPosition(i, Vector3.Lerp(startPos, endPos, t));
            }

            lr.startWidth = lineWidth;
            lr.endWidth = lineWidth;

            if (lineMaterial != null)
            {
                lr.material = new Material(lineMaterial);
                lr.material.SetTextureScale("_BaseMap", new Vector2(dashLengthMultiplier, 1f));
            }

            lr.textureMode = LineTextureMode.Tile;
            pathLines.Add(nextNode, lr);
        }
    }
}