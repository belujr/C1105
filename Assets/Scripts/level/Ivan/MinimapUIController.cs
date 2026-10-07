using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class MinimapUIController : MonoBehaviour
{
    [Header("References")]
    public RectTransform mapContainer;       // Container holding room nodes
    public GameObject nodeUIPrefab;          // MinimapNode_Prefab
    public float uiScaleFactor = 4.5f;        // Scales world coordinates to canvas coordinates

    private Dictionary<RoomNode, MinimapNodeUI> uiNodeMap = new Dictionary<RoomNode, MinimapNodeUI>();
    private RoomNode currentRoom;

    public void InitializeMap(RoomNode startRoom)
    {
        // Clear previous run objects
        foreach (Transform child in mapContainer) Destroy(child.gameObject);
        uiNodeMap.Clear();

        if (startRoom == null) return;

        List<RoomNode> allRooms = GetAllRoomsInLayout(startRoom);

        foreach (var room in allRooms)
        {
            room.currentState = RoomState.Hidden;
        }

        // Instantiate nodes based on 3D world positioning relative to start point
        foreach (RoomNode room in allRooms)
        {
            GameObject nodeObj = Instantiate(nodeUIPrefab, mapContainer);
            MinimapNodeUI uiNode = nodeObj.GetComponent<MinimapNodeUI>();

            Vector3 relWorld = room.transform.position - startRoom.transform.position;
            uiNode.RectTransform.anchoredPosition = new Vector2(relWorld.x, relWorld.z) * uiScaleFactor;

            uiNodeMap.Add(room, uiNode);
        }

        CreateConnectorLines(allRooms, startRoom.transform.position);
        OnPlayerEnteredRoom(startRoom);
    }

    public void OnPlayerEnteredRoom(RoomNode newRoom)
    {
        if (newRoom == null) return;

        if (currentRoom != null && currentRoom != newRoom)
        {
            currentRoom.currentState = RoomState.Visited;
        }

        currentRoom = newRoom;
        currentRoom.currentState = RoomState.Current;

        foreach (RoomNode adj in currentRoom.adjacentRooms)
        {
            if (adj != null && adj.currentState == RoomState.Hidden)
            {
                adj.currentState = RoomState.UnvisitedAdjacent;
            }
        }

        RefreshMapUI();
    }

    private void RefreshMapUI()
    {
        foreach (var kvp in uiNodeMap)
        {
            kvp.Value.UpdateVisuals(kvp.Key.currentState, kvp.Key.roomShapeSprite);
        }

        // Center map container on player's current node
        if (uiNodeMap.TryGetValue(currentRoom, out MinimapNodeUI currentUI))
        {
            mapContainer.anchoredPosition = -currentUI.RectTransform.anchoredPosition;
        }
    }

    private void CreateConnectorLines(List<RoomNode> rooms, Vector3 startPos)
    {
        HashSet<(RoomNode, RoomNode)> drawnPairs = new HashSet<(RoomNode, RoomNode)>();

        foreach (var room in rooms)
        {
            Vector2 posA = GetUIPos(room.transform.position, startPos);

            foreach (var adj in room.adjacentRooms)
            {
                if (adj == null) continue;

                if (drawnPairs.Contains((room, adj)) || drawnPairs.Contains((adj, room)))
                    continue;

                drawnPairs.Add((room, adj));
                Vector2 posB = GetUIPos(adj.transform.position, startPos);

                GameObject lineObj = new GameObject("UI_PathConnector", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                lineObj.transform.SetParent(mapContainer, false);
                lineObj.transform.SetAsFirstSibling();

                RectTransform lineRect = lineObj.GetComponent<RectTransform>();
                Image lineImg = lineObj.GetComponent<Image>();
                lineImg.color = new Color(0.25f, 0.25f, 0.25f, 0.6f);

                Vector2 dir = posB - posA;
                lineRect.anchorMin = lineRect.anchorMax = new Vector2(0.5f, 0.5f);
                lineRect.pivot = new Vector2(0f, 0.5f);
                lineRect.anchoredPosition = posA;
                lineRect.sizeDelta = new Vector2(dir.magnitude, 3f);
                lineRect.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
            }
        }
    }

    private Vector2 GetUIPos(Vector3 worldPos, Vector3 startWorldPos)
    {
        Vector3 rel = worldPos - startWorldPos;
        return new Vector2(rel.x, rel.z) * uiScaleFactor;
    }

    private List<RoomNode> GetAllRoomsInLayout(RoomNode start)
    {
        List<RoomNode> result = new List<RoomNode>();
        Queue<RoomNode> queue = new Queue<RoomNode>();
        HashSet<RoomNode> visited = new HashSet<RoomNode>();

        queue.Enqueue(start);
        visited.Add(start);

        while (queue.Count > 0)
        {
            RoomNode curr = queue.Dequeue();
            result.Add(curr);

            foreach (RoomNode adj in curr.adjacentRooms)
            {
                if (adj != null && !visited.Contains(adj))
                {
                    visited.Add(adj);
                    queue.Enqueue(adj);
                }
            }
        }
        return result;
    }
}