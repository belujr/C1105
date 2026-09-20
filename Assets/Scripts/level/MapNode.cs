using System.Collections.Generic;
using UnityEngine;

public class MapNode : MonoBehaviour
{
    public List<MapNode> nextNodes;
    public Material lineMaterial;
    public float lineWidth = 0.5f;
    public float yOffset = 0.5f;

    [Tooltip("Lower numbers stretch the dash into a longer rectangle (e.g., 0.2 or 0.3)")]
    public float dashLengthMultiplier = 0.3f;

    public Dictionary<MapNode, LineRenderer> pathLines = new Dictionary<MapNode, LineRenderer>();

    void Start()
    {
        foreach (MapNode nextNode in nextNodes)
        {
            if (nextNode == null) continue;

            GameObject pathLine = new GameObject("PathTo_" + nextNode.gameObject.name);
            pathLine.transform.SetParent(transform);

            LineRenderer lr = pathLine.AddComponent<LineRenderer>();
            lr.positionCount = 2;

            Vector3 startPos = transform.position + Vector3.up * yOffset;
            Vector3 endPos = nextNode.transform.position + Vector3.up * yOffset;

            lr.SetPosition(0, startPos);
            lr.SetPosition(1, endPos);

            lr.startWidth = lineWidth;
            lr.endWidth = lineWidth;

            if (lineMaterial != null)
            {
                // Instantiate a unique material for this line so we can animate it later
                lr.material = new Material(lineMaterial);
                // Stretch the texture along the X axis to make it rectangular
                lr.material.SetTextureScale("_BaseMap", new Vector2(dashLengthMultiplier, 1f));
            }

            lr.textureMode = LineTextureMode.Tile;
            pathLines.Add(nextNode, lr);
        }
    }
}