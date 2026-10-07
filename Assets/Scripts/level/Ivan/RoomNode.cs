using System.Collections.Generic;
using UnityEngine;

public enum RoomState
{
    Hidden,             // Not visible
    UnvisitedAdjacent,  // Black box with '?'
    Visited,            // Previously accessed (White shape)
    Current             // Player currently inside (Yellow shape)
}

public class RoomNode : MonoBehaviour
{
    [Header("Room Shape & Connections")]
    public Sprite roomShapeSprite;
    public List<RoomNode> adjacentRooms = new List<RoomNode>();

    [HideInInspector] public RoomState currentState = RoomState.Hidden;
}